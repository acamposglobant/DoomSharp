// Generates C# table files from the ORIGINAL C sources (mechanical extraction,
// no hand-copying) and cross-checks against the C characterization dump.
//
// Run from repo root:  bun DoomSharp/tools/gen/run.ts
import { readFileSync, writeFileSync } from "node:fs";

const root = new URL("../../../", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1");

function read(rel: string): string {
  return readFileSync(root + rel, "utf8");
}

// ---------- extract C array ----------
function extractArray(source: string, name: string): string[] {
  // find "type name[...] = {" ... "};"
  const re = new RegExp(name + "\\s*\\[[^\\]]*\\]\\s*=\\s*\\{([\\s\\S]*?)\\};");
  const m = re.exec(source);
  if (!m) throw new Error(`array ${name} not found`);
  return m[1].split(",").map(s => s.trim()).filter(s => s.length > 0);
}

// ---------- m_random.c ----------
const randomSrc = read("Legacy/linuxdoom-1.10/m_random.c");
const rndTable = extractArray(randomSrc, "rndtable");
if (rndTable.length !== 256) throw new Error(`rndtable has ${rndTable.length} entries, expected 256`);

// ---------- tables.c ----------
const tablesSrc = read("Legacy/linuxdoom-1.10/tables.c");
const finesine = extractArray(tablesSrc, "finesine");
const finetangent = extractArray(tablesSrc, "finetangent");
const tantoangle = extractArray(tablesSrc, "tantoangle");
if (finesine.length !== 5 * 8192 / 4) throw new Error(`finesine len ${finesine.length}`);
if (finetangent.length !== 4096) throw new Error(`finetangent len ${finetangent.length}`);
if (tantoangle.length !== 2049) throw new Error(`tantoangle len ${tantoangle.length}`);

// ---------- cross-check against C dump ----------
const vectors = JSON.parse(read("DoomSharp/tools/charact/vectors.json"));
const fail = (msg: string): never => { throw new Error("CROSS-CHECK FAILED: " + msg); };

// P_Random walk: first call returns rndtable[1], ..., 256th returns rndtable[0]
const pWalk = vectors.find((v: any) => Array.isArray(v));
if (!pWalk) throw new Error("random walk vector not found");
for (let i = 0; i < 256; i++) {
  const expected = Number(rndTable[(i + 1) & 0xff]);
  if (pWalk[i] !== expected) fail(`P_Random[${i}]: dump=${pWalk[i]} table=${expected}`);
}

// finesine/finetangent/tantoangle from dump
function checkDump(op: string, arr: string[]) {
  const rec = vectors.find((v: any) => v.op === op);
  if (!rec) fail(`${op} record missing`);
  if (rec.len !== arr.length) fail(`${op} length ${rec.len} vs ${arr.length}`);
  for (let i = 0; i < arr.length; i++) {
    const a = Number(arr[i]) | 0;
    const b = rec.data[i];
    if (a !== b) fail(`${op}[${i}]: dump=${b} source=${a}`);
  }
}

checkDump("finesine", finesine);
checkDump("finetangent", finetangent);
checkDump("tantoangle", tantoangle);
console.log("cross-check vs C dump: OK");

// ---------- emit C# ----------
function fmt(values: string[], perLine: number, signed: boolean): string {
  const lines: string[] = [];
  for (let i = 0; i < values.length; i += perLine) {
    const chunk = values.slice(i, i + perLine).map(v => {
      const n = Number(v) | 0;
      return (signed ? String(n) : String(n >>> 0)) + ",";
    });
    lines.push("        " + chunk.join(" "));
  }
  return lines.join("\n");
}

const license = `// Emacs style mode select   -*- C# -*-
//-----------------------------------------------------------------------------
//
// Copyright (C) 1993-1996 by id Software, Inc.
//
// This source is available for distribution and/or modification
// only under the terms of the GNU General Public License 2.0 as
// published by the Free Software Foundation.
//
// GENERATED FILE — extracted mechanically from the original C sources.
// Do not edit by hand; regenerate with: bun DoomSharp/tools/gen/run.ts
// Source: Legacy/linuxdoom-1.10/tables.c (tables data), m_random.c (rndtable).
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Core;
`;

const tablesCs = `${license}
/// <summary>
/// Lookup tables. Migration of tables.h/tables.c:
/// finesine[10240] (finecosine aliases into it), finetangent[4096],
/// tantoangle[2049]. BAM constants from tables.h:50-78.
/// </summary>
public static class AngleTables
{
    public const int FineAngles = 8192;          // FINEANGLES
    public const int FineMask = FineAngles - 1;  // FINEMASK
    public const int AngleToFineShift = 19;      // ANGLETOFINESHIFT
    public const int SlopeRange = 2048;          // SLOPERANGE

    // Binary Angle Measurement, BAM (tables.h:68-71).
    public const uint Ang45 = 0x20000000;
    public const uint Ang90 = 0x40000000;
    public const uint Ang180 = 0x80000000;
    public const uint Ang270 = 0xc0000000;

    /// <summary>finesine[5*FINEANGLES/4] (tables.c).</summary>
    public static readonly int[] FineSine =
    [
${fmt(finesine, 8, true)}
    ];

    /// <summary>
    /// finecosine — alias into FineSine at PI/2 phase shift (tables.h:61).
    /// In C: finecosine = finesine + FINEANGLES/4.
    /// </summary>
    public static readonly int[] FineCosine =
    [
${fmt(finesine.slice(8192 / 4).concat(finesine.slice(0, 8192 / 4)), 8, true)}
    ];

    /// <summary>finetangent[FINEANGLES/2] (tables.c).</summary>
    public static readonly int[] FineTangent =
    [
${fmt(finetangent, 8, true)}
    ];

    /// <summary>tantoangle[SLOPERANGE+1] (tables.c).</summary>
    public static readonly uint[] TanToAngle =
    [
${fmt(tantoangle, 8, false)}
    ];
}
`;

writeFileSync(root + "DoomSharp/src/DoomSharp.Core/AngleTables.cs", tablesCs);

const randomCs = `// Emacs style mode select   -*- C# -*-
//-----------------------------------------------------------------------------
//
// Copyright (C) 1993-1996 by id Software, Inc.
//
// This source is available for distribution and/or modification
// only under the terms of the GNU General Public License 2.0 as
// published by the Free Software Foundation.
//
// DESCRIPTION:
//      Deterministic random number LUT.
//      Migration of Legacy/linuxdoom-1.10/m_random.h and m_random.c.
//      Table extracted mechanically from m_random.c:31-51.
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Core;

/// <summary>
/// Deterministic random numbers (m_random.c). Two independent index
/// counters over the same 256-entry table: P_Random drives gameplay
/// (demo determinism), M_Random drives cosmetic effects.
/// </summary>
public sealed class DoomRandom
{
    /// <summary>The original 256-entry random table (m_random.c:31-51).</summary>
    public static readonly byte[] RndTable =
    [
${fmt(rndTable, 14, true)}
    ];

    private int _prndIndex;
    private int _rndIndex;

    /// <summary>P_Random — gameplay random, 0-255 (m_random.c:57-61).</summary>
    public int PRandom()
    {
        unchecked
        {
            _prndIndex = (_prndIndex + 1) & 0xff;
            return RndTable[_prndIndex];
        }
    }

    /// <summary>M_Random — cosmetic random, 0-255 (m_random.c:63-67).</summary>
    public int MRandom()
    {
        unchecked
        {
            _rndIndex = (_rndIndex + 1) & 0xff;
            return RndTable[_rndIndex];
        }
    }

    /// <summary>M_ClearRandom — resets both indices (m_random.c:69-72).</summary>
    public void ClearRandom()
    {
        _prndIndex = 0;
        _rndIndex = 0;
    }

    /// <summary>Current P_Random index (prndindex, m_random.c:53).</summary>
    public int PRndIndex => _prndIndex;

    /// <summary>Current M_Random index (rndindex, m_random.c:53).</summary>
    public int RndIndex => _rndIndex;
}
`;

writeFileSync(root + "DoomSharp/src/DoomSharp.Core/DoomRandom.cs", randomCs);

console.log("Generated AngleTables.cs and DoomRandom.cs");

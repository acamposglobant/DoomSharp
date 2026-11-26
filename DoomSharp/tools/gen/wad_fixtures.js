// Builds synthetic WAD fixtures, runs the C harness (waddump) over them
// via Docker, and saves the characterization vectors.
//
// Run from repo root:  node DoomSharp/tools/gen/wad_fixtures.js
const { execSync } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "../../..");
const fixtureDir = path.join(root, "DoomSharp", "tools", "charact", "wadfixtures");

fs.rmSync(fixtureDir, { recursive: true, force: true });
fs.mkdirSync(fixtureDir, { recursive: true });

// ---------- WAD builder ----------
function buildWad(id, lumps) {
  // lumps: [{ name (<=8 chars), data: Buffer }]
  const header = Buffer.alloc(12);
  header.write("IWAD".startsWith(id) ? id : id, 0, "ascii");
  const dirSize = lumps.length * 16;
  const infotableofs = 12 + lumps.reduce((acc, l) => acc + l.data.length, 0);
  header.writeInt32LE(lumps.length, 4);
  header.writeInt32LE(infotableofs, 8);

  const parts = [header];
  for (const l of lumps) parts.push(l.data);

  const dir = [];
  let pos = 12;
  for (const l of lumps) {
    const entry = Buffer.alloc(16);
    entry.writeInt32LE(pos, 0);
    entry.writeInt32LE(l.data.length, 4);
    const nameBytes = Buffer.alloc(8);
    Buffer.from(l.name, "ascii").copy(nameBytes, 0, 0, Math.min(8, l.name.length));
    nameBytes.copy(entry, 8);
    dir.push(entry);
    pos += l.data.length;
  }

  return Buffer.concat([...parts, ...dir]);
}

let pos = 12; // helper for sequential positions
function resetPos() { pos = 12; }
function nextPos(len) { const p = pos; pos += len; return p; }

// ---------- fixtures ----------
// IWAD: E1M1 (8 bytes), MAP01 (4 bytes)
resetPos();
const e1m1 = Buffer.from([1, 2, 3, 4, 5, 6, 7, 8]);
const map01Iwad = Buffer.from([0xAA, 0xBB, 0xCC, 0xDD]);
const iwad = buildWad("IWAD", [
  { name: "E1M1", data: e1m1, pos: nextPos(8) },
  { name: "MAP01", data: map01Iwad, pos: nextPos(4) },
]);
fs.writeFileSync(path.join(fixtureDir, "test_iwad.wad"), iwad);

// PWAD: MAP01 (4 bytes, different data) — overrides IWAD's MAP01
resetPos();
const map01Pwad = Buffer.from([0x11, 0x22, 0x33, 0x44]);
const pwad = buildWad("PWAD", [
  { name: "MAP01", data: map01Pwad, pos: nextPos(4) },
]);
fs.writeFileSync(path.join(fixtureDir, "test_pwad.wad"), pwad);

// Non-WAD single lump file (DEHACKED) — 3 bytes
fs.writeFileSync(path.join(fixtureDir, "DEHACKED"), Buffer.from([0xDE, 0xAD, 0xBE]));

// Malformed: bad magic
const badId = Buffer.alloc(32);
badId.write("XXXX", 0, "ascii");
badId.writeInt32LE(1, 4);
badId.writeInt32LE(28, 8);
fs.writeFileSync(path.join(fixtureDir, "bad_id.wad"), badId);

// Malformed: truncated directory (numlumps=2 but only 1 entry present)
const truncated = Buffer.alloc(12 + 8 + 16);
truncated.write("IWAD", 0, "ascii");
truncated.writeInt32LE(2, 4);
truncated.writeInt32LE(20, 8);
fs.writeFileSync(path.join(fixtureDir, "truncated_dir.wad"), truncated);

// Malformed: lump out of bounds (pos+size beyond file length)
resetPos();
const oob = buildWad("IWAD", [
  { name: "BIG", data: Buffer.alloc(4), pos: nextPos(4) },
]);
oob.writeInt32LE(0x7FFFFFFF, 12 + 0); // filepos of lump 0 -> huge
fs.writeFileSync(path.join(fixtureDir, "oob_lump.wad"), oob);

console.log("fixtures written to", fixtureDir);

// ---------- run C harness in Docker ----------
const vectorsOut = path.join(root, "DoomSharp", "tools", "charact", "wad_vectors.json");
const cmd =
  `MSYS_NO_PATHCONV=1 docker run --rm -v "${root}:/src" -w //src gcc:13 sh -c ` +
  `"gcc -O0 -DNORMALUNIX -DLINUX -o /tmp/waddump ` +
  `DoomSharp/tools/charact/waddump.c Legacy/linuxdoom-1.10/w_wad.c ` +
  `-I Legacy/linuxdoom-1.10 2>&1 | head -20 && ` +
  `/tmp/waddump /src/DoomSharp/tools/charact/wadfixtures > /tmp/wad_vectors.json && ` +
  `cp /tmp/wad_vectors.json /src/DoomSharp/tools/charact/wad_vectors.json && echo WAD_VECTORS_OK"`;

try {
  const out = execSync(cmd, { encoding: "utf8", stdio: ["pipe", "pipe", "pipe"] });
  console.log(out.trim());
} catch (e) {
  console.error("harness failed:", e.stdout?.toString(), e.stderr?.toString());
  process.exit(1);
}

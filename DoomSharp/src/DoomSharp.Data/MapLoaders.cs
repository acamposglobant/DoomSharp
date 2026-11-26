// Emacs style mode select   -*- C# -*-
//-----------------------------------------------------------------------------
//
// Copyright (C) 1993-1996 by id Software, Inc.
//
// This source is available for distribution and/or modification
// only under the terms of the GNU General Public License 2.0 as
// published by the Free Software Foundation.
//
// DESCRIPTION:
//      Map lump loaders. Migration of the P_Load* functions of
//      Legacy/linuxdoom-1.10/p_setup.c (lines 120-560), orchestrated
//      by LoadLevel in P_SetupLevel dependency order.
//      See docs/adr/ADR-004 for trace logging and the DOOM-020 plan
//      for documented deviations (raw texture names, index-based
//      references, C UB -> DoomFatalException).
//
//-----------------------------------------------------------------------------

using System.Buffers.Binary;
using DoomSharp.Core;
using Serilog;

namespace DoomSharp.Data;

/// <summary>
/// A fully loaded map level: runtime arrays plus the raw things and
/// reject/blockmap tables. All runtime arrays are zone-allocated with
/// PU_LEVEL (freed by zone.FreeTags(PurgeTags.Level, ...) at level exit).
/// </summary>
public sealed class LevelData
{
    public Vertex[] Vertexes = [];
    public Sector[] Sectors = [];
    public Side[] Sides = [];
    public Line[] Lines = [];
    public Subsector[] Subsectors = [];
    public Seg[] Segs = [];
    public Node[] Nodes = [];
    public MapThing[] Things = [];
    public BlockMap? BlockMap;
    public byte[] Reject = [];

    /// <summary>Zone blocks holding the runtime arrays (PU_LEVEL).</summary>
    public List<ZoneBlock> ZoneBlocks { get; } = [];
}

/// <summary>
/// The ten map lump loaders (p_setup.c:120-560). Each mirrors its
/// original: count = lumpLength / recordSize (truncating), parse
/// little-endian records, build runtime structures, cross-reference.
/// </summary>
public static class MapLoaders
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(MapLoaders));

    /// <summary>
    /// Loads all map lumps for the map whose marker lump is
    /// <paramref name="lumpBase"/>, in P_SetupLevel dependency order
    /// (p_setup.c:590-660): Vertexes, Sectors, SideDefs, LineDefs,
    /// Subsectors, Segs, Nodes, Things, BlockMap, Reject.
    /// </summary>
    public static LevelData LoadLevel(WadDirectory wad, int lumpBase, ZoneMemory zone)
    {
        Log.Verbose("LoadLevel({LumpBase})", lumpBase);

        // Validate the marker and all standard offsets are in range
        // (p_setup.c:590-660 uses W_GetNumForName, which fatals on a
        // missing lump; here the marker bounds the relative offsets).
        var numLumps = wad.Lumps.Count;
        if (lumpBase < 0 || lumpBase + MapLumpOffsets.BlockMap >= numLumps)
        {
            Log.Verbose("LoadLevel fatal: map lumps out of bounds (base {LumpBase}, lumps {NumLumps})", lumpBase, numLumps);
            throw new DoomFatalException(
                $"P_SetupLevel: map lumps out of bounds (base {lumpBase}, {numLumps} lumps)");
        }

        var level = new LevelData();

        // P_SetupLevel order (dependency-driven):
        level.Vertexes = LoadVertexes(wad, lumpBase + MapLumpOffsets.Vertexes, zone).Data;
        level.Sectors = LoadSectors(wad, lumpBase + MapLumpOffsets.Sectors, zone).Data;
        level.Sides = LoadSideDefs(wad, lumpBase + MapLumpOffsets.SideDefs, zone).Data;
        level.Lines = LoadLineDefs(wad, lumpBase + MapLumpOffsets.LineDefs, zone, level).Data;
        level.Subsectors = LoadSubsectors(wad, lumpBase + MapLumpOffsets.SubSectors, zone).Data;
        level.Segs = LoadSegs(wad, lumpBase + MapLumpOffsets.Segs, zone, level).Data;
        level.Nodes = LoadNodes(wad, lumpBase + MapLumpOffsets.Nodes, zone).Data;
        level.Things = LoadThings(wad, lumpBase + MapLumpOffsets.Things);
        level.BlockMap = LoadBlockMap(wad, lumpBase + MapLumpOffsets.BlockMap);
        level.Reject = LoadReject(wad, lumpBase + MapLumpOffsets.Reject);

        Log.Verbose(
            "LoadLevel({LumpBase}) = {Vertexes}v {Sectors}s {Sides}sd {Lines}l {Subsectors}ss {Segs}sg {Nodes}n {Things}t",
            lumpBase, level.Vertexes.Length, level.Sectors.Length, level.Sides.Length,
            level.Lines.Length, level.Subsectors.Length, level.Segs.Length,
            level.Nodes.Length, level.Things.Length);
        return level;
    }

    /// <summary>
    /// P_LoadVertexes (p_setup.c:122-152): count = length/4, convert
    /// shorts to fixed-point (SHORT(ml->x) << FRACBITS).
    /// </summary>
    public static ZoneBlock<Vertex[]> LoadVertexes(WadDirectory wad, int lump, ZoneMemory zone)
    {
        Log.Verbose("LoadVertexes({Lump})", lump);

        var data = wad.CacheLumpNum(lump, PurgeTags.Static);
        var count = data.Length / MapVertex.Size; // truncating division (p_setup.c:131)

        var vertexes = new Vertex[count];
        for (var i = 0; i < count; i++)
        {
            var ml = MapVertex.Read(data.AsSpan(i * MapVertex.Size));
            vertexes[i] = new Vertex
            {
                X = ml.X << FixedPoint.FracBits,   // p_setup.c:146
                Y = ml.Y << FixedPoint.FracBits,   // p_setup.c:147
            };
        }

        var block = zone.Allocate(vertexes, PurgeTags.Level);
        Log.Verbose("LoadVertexes({Lump}) = {Count} vertexes", lump, count);
        return block;
    }

    /// <summary>
    /// P_LoadSectors (p_setup.c:231-262): heights to fixed, flat names
    /// stored raw (R_FlatNumForName deferred to DOOM-030).
    /// </summary>
    public static ZoneBlock<Sector[]> LoadSectors(WadDirectory wad, int lump, ZoneMemory zone)
    {
        Log.Verbose("LoadSectors({Lump})", lump);

        var data = wad.CacheLumpNum(lump, PurgeTags.Static);
        var count = data.Length / MapSector.Size;

        var sectors = new Sector[count];
        for (var i = 0; i < count; i++)
        {
            var ms = MapSector.Read(data.AsSpan(i * MapSector.Size));
            sectors[i] = new Sector
            {
                FloorHeight = ms.FloorHeight << FixedPoint.FracBits,     // p_setup.c:246
                CeilingHeight = ms.CeilingHeight << FixedPoint.FracBits, // p_setup.c:247
                FloorPic = ms.FloorPic,
                CeilingPic = ms.CeilingPic,
                LightLevel = ms.LightLevel,
                Special = ms.Special,
                Tag = ms.Tag,
            };
        }

        var block = zone.Allocate(sectors, PurgeTags.Level);
        Log.Verbose("LoadSectors({Lump}) = {Count} sectors", lump, count);
        return block;
    }

    /// <summary>
    /// P_LoadSideDefs (p_setup.c:438-463): offsets to fixed, texture
    /// names stored raw (R_TextureNumForName deferred to DOOM-030).
    /// </summary>
    public static ZoneBlock<Side[]> LoadSideDefs(WadDirectory wad, int lump, ZoneMemory zone)
    {
        Log.Verbose("LoadSideDefs({Lump})", lump);

        var data = wad.CacheLumpNum(lump, PurgeTags.Static);
        var count = data.Length / MapSideDef.Size;

        var sides = new Side[count];
        for (var i = 0; i < count; i++)
        {
            var msd = MapSideDef.Read(data.AsSpan(i * MapSideDef.Size));
            sides[i] = new Side
            {
                TextureOffset = msd.TextureOffset << FixedPoint.FracBits, // p_setup.c:454
                RowOffset = msd.RowOffset << FixedPoint.FracBits,         // p_setup.c:455
                TopTexture = msd.TopTexture,
                BottomTexture = msd.BottomTexture,
                MidTexture = msd.MidTexture,
                Sector = msd.Sector,
            };
        }

        var block = zone.Allocate(sides, PurgeTags.Level);
        Log.Verbose("LoadSideDefs({Lump}) = {Count} sidedefs", lump, count);
        return block;
    }

    /// <summary>
    /// P_LoadLineDefs (p_setup.c:357-432): dx/dy from vertex delta,
    /// slopetype via FixedDiv(dy,dx) > 0, bbox from endpoint min/max,
    /// front/back sector from sidenum != -1.
    /// </summary>
    public static ZoneBlock<Line[]> LoadLineDefs(
        WadDirectory wad, int lump, ZoneMemory zone, LevelData level)
    {
        Log.Verbose("LoadLineDefs({Lump})", lump);

        var data = wad.CacheLumpNum(lump, PurgeTags.Static);
        var count = data.Length / MapLineDef.Size;

        var lines = new Line[count];
        for (var i = 0; i < count; i++)
        {
            var mld = MapLineDef.Read(data.AsSpan(i * MapLineDef.Size));
            var ld = new Line
            {
                Flags = mld.Flags,       // p_setup.c:375
                Special = mld.Special,   // p_setup.c:376
                Tag = mld.Tag,           // p_setup.c:377
                V1 = mld.V1,
                V2 = mld.V2,
            };

            // C would read out of bounds on a bad index (UB); managed
            // code fatals (documented deviation, ADR-002).
            if (mld.V1 < 0 || mld.V1 >= level.Vertexes.Length ||
                mld.V2 < 0 || mld.V2 >= level.Vertexes.Length)
            {
                Log.Verbose("LoadLineDefs fatal: line {Index} vertex index out of range", i);
                throw new DoomFatalException(
                    $"P_LoadLineDefs: line {i} vertex index out of range");
            }
            var v1 = level.Vertexes[mld.V1];  // p_setup.c:378
            var v2 = level.Vertexes[mld.V2];  // p_setup.c:379
            ld.Dx = v2.X - v1.X;              // p_setup.c:380
            ld.Dy = v2.Y - v1.Y;              // p_setup.c:380

            if (ld.Dx == 0)
            {
                ld.SlopeType = SlopeType.Vertical;    // p_setup.c:383-384
            }
            else if (ld.Dy == 0)
            {
                ld.SlopeType = SlopeType.Horizontal;  // p_setup.c:385-386
            }
            else
            {
                ld.SlopeType = FixedPoint.FixedDiv(ld.Dy, ld.Dx) > 0
                    ? SlopeType.Positive   // p_setup.c:389-390
                    : SlopeType.Negative;  // p_setup.c:391-392
            }

            if (v1.X < v2.X)              // p_setup.c:395-404
            {
                ld.BBox[BBox.Left] = v1.X;
                ld.BBox[BBox.Right] = v2.X;
            }
            else
            {
                ld.BBox[BBox.Left] = v2.X;
                ld.BBox[BBox.Right] = v1.X;
            }

            if (v1.Y < v2.Y)              // p_setup.c:406-415
            {
                ld.BBox[BBox.Bottom] = v1.Y;
                ld.BBox[BBox.Top] = v2.Y;
            }
            else
            {
                ld.BBox[BBox.Bottom] = v2.Y;
                ld.BBox[BBox.Top] = v1.Y;
            }

            ld.Sidenum[0] = mld.Side0;    // p_setup.c:417-418
            ld.Sidenum[1] = mld.Side1;

            if (ld.Sidenum[0] != -1)
            {
                if (ld.Sidenum[0] < 0 || ld.Sidenum[0] >= level.Sides.Length)
                {
                    Log.Verbose("LoadLineDefs fatal: line {Index} front sidedef out of range", i);
                    throw new DoomFatalException(
                        $"P_LoadLineDefs: line {i} sidedef {ld.Sidenum[0]} out of range");
                }
                ld.FrontSector = level.Sides[ld.Sidenum[0]].Sector;   // p_setup.c:420-421
            }
            else
            {
                ld.FrontSector = -1;                                  // p_setup.c:422-423 (NULL)
            }

            if (ld.Sidenum[1] != -1)
            {
                if (ld.Sidenum[1] < 0 || ld.Sidenum[1] >= level.Sides.Length)
                {
                    Log.Verbose("LoadLineDefs fatal: line {Index} back sidedef out of range", i);
                    throw new DoomFatalException(
                        $"P_LoadLineDefs: line {i} sidedef {ld.Sidenum[1]} out of range");
                }
                ld.BackSector = level.Sides[ld.Sidenum[1]].Sector;    // p_setup.c:425-426
            }
            else
            {
                ld.BackSector = -1;                                   // p_setup.c:427-428
            }

            lines[i] = ld;
        }

        var block = zone.Allocate(lines, PurgeTags.Level);
        Log.Verbose("LoadLineDefs({Lump}) = {Count} linedefs", lump, count);
        return block;
    }

    /// <summary>
    /// P_LoadSubsectors (p_setup.c:202-224): numsegs -> numlines,
    /// firstseg -> firstline (segs are stored sequentially).
    /// </summary>
    public static ZoneBlock<Subsector[]> LoadSubsectors(WadDirectory wad, int lump, ZoneMemory zone)
    {
        Log.Verbose("LoadSubsectors({Lump})", lump);

        var data = wad.CacheLumpNum(lump, PurgeTags.Static);
        var count = data.Length / MapSubsector.Size;

        var subsectors = new Subsector[count];
        for (var i = 0; i < count; i++)
        {
            var ms = MapSubsector.Read(data.AsSpan(i * MapSubsector.Size));
            subsectors[i] = new Subsector
            {
                NumSegs = ms.NumSegs,    // p_setup.c:219
                FirstSeg = ms.FirstSeg,  // p_setup.c:220
            };
        }

        var block = zone.Allocate(subsectors, PurgeTags.Level);
        Log.Verbose("LoadSubsectors({Lump}) = {Count} subsectors", lump, count);
        return block;
    }

    /// <summary>
    /// P_LoadSegs (p_setup.c:159-196): cross-references into vertexes,
    /// lines, sides; ML_TWOSIDED backsector rule (p_setup.c:189-192).
    /// </summary>
    public static ZoneBlock<Seg[]> LoadSegs(
        WadDirectory wad, int lump, ZoneMemory zone, LevelData level)
    {
        Log.Verbose("LoadSegs({Lump})", lump);

        var data = wad.CacheLumpNum(lump, PurgeTags.Static);
        var count = data.Length / MapSeg.Size;

        var segs = new Seg[count];
        for (var i = 0; i < count; i++)
        {
            var ml = MapSeg.Read(data.AsSpan(i * MapSeg.Size));
            var seg = new Seg
            {
                V1 = ml.V1,              // p_setup.c:178 (pointer -> index)
                V2 = ml.V2,              // p_setup.c:179
                Angle = ml.Angle << 16,  // p_setup.c:181 (angle_t BAM)
                Offset = ml.Offset << 16, // p_setup.c:182
                LineDef = ml.LineDef,
                Side = ml.Side,
            };

            // C would read out of bounds on a bad index (UB); managed
            // code fatals (documented deviation, ADR-002).
            if (ml.LineDef < 0 || ml.LineDef >= level.Lines.Length)
            {
                Log.Verbose("LoadSegs fatal: seg {Index} linedef {LineDef} out of range ({Count} lines)", i, ml.LineDef, level.Lines.Length);
                throw new DoomFatalException(
                    $"P_LoadSegs: seg {i} linedef index {ml.LineDef} out of range");
            }
            var ldef = level.Lines[ml.LineDef];       // p_setup.c:184
            var sidenum = ldef.Sidenum[ml.Side];      // p_setup.c:187
            if (sidenum < 0 || sidenum >= level.Sides.Length)
            {
                Log.Verbose("LoadSegs fatal: sidedef {Sidenum} out of range ({Count} sides)", sidenum, level.Sides.Length);
                throw new DoomFatalException(
                    $"P_LoadSegs: sidedef {sidenum} out of range");
            }
            seg.FrontSector = level.Sides[sidenum].Sector; // p_setup.c:188

            if ((ldef.Flags & LineDefFlags.TwoSided) != 0)
            {
                // p_setup.c:189-190 — side^1 XOR trick.
                var backSidenum = ldef.Sidenum[ml.Side ^ 1];
                if (backSidenum < 0 || backSidenum >= level.Sides.Length)
                {
                    Log.Verbose("LoadSegs fatal: back sidedef {Sidenum} out of range", backSidenum);
                    throw new DoomFatalException(
                        $"P_LoadSegs: sidedef {backSidenum} out of range");
                }
                seg.BackSector = level.Sides[backSidenum].Sector;
            }
            else
            {
                seg.BackSector = -1;                  // p_setup.c:191-192 (NULL)
            }

            segs[i] = seg;
        }

        var block = zone.Allocate(segs, PurgeTags.Level);
        Log.Verbose("LoadSegs({Lump}) = {Count} segs", lump, count);
        return block;
    }

    /// <summary>
    /// P_LoadNodes (p_setup.c:264-297): coordinates to fixed, children
    /// read UNSIGNED so the NF_SUBSECTOR (0x8000) bit does not
    /// sign-extend.
    /// </summary>
    public static ZoneBlock<Node[]> LoadNodes(WadDirectory wad, int lump, ZoneMemory zone)
    {
        Log.Verbose("LoadNodes({Lump})", lump);

        var data = wad.CacheLumpNum(lump, PurgeTags.Static);
        var count = data.Length / MapNode.Size;

        var nodes = new Node[count];
        for (var i = 0; i < count; i++)
        {
            var mn = MapNode.Read(data.AsSpan(i * MapNode.Size));
            var node = new Node
            {
                X = mn.X << FixedPoint.FracBits,
                Y = mn.Y << FixedPoint.FracBits,
                Dx = mn.Dx << FixedPoint.FracBits,
                Dy = mn.Dy << FixedPoint.FracBits,
                Children = [mn.Child0, mn.Child1],
            };
            node.BBox[0] = [mn.BBox0[0] << FixedPoint.FracBits, mn.BBox0[1] << FixedPoint.FracBits, mn.BBox0[2] << FixedPoint.FracBits, mn.BBox0[3] << FixedPoint.FracBits];
            node.BBox[1] = [mn.BBox1[0] << FixedPoint.FracBits, mn.BBox1[1] << FixedPoint.FracBits, mn.BBox1[2] << FixedPoint.FracBits, mn.BBox1[3] << FixedPoint.FracBits];
            nodes[i] = node;
        }

        var block = zone.Allocate(nodes, PurgeTags.Level);
        Log.Verbose("LoadNodes({Lump}) = {Count} nodes", lump, count);
        return block;
    }

    /// <summary>
    /// P_LoadThings (p_setup.c:301-313): raw records stored;
    /// P_SpawnMapThing conversion is DOOM-021.
    /// </summary>
    public static MapThing[] LoadThings(WadDirectory wad, int lump)
    {
        Log.Verbose("LoadThings({Lump})", lump);

        var data = wad.CacheLumpNum(lump, PurgeTags.Static);
        var count = data.Length / MapThing.Size;

        var things = new MapThing[count];
        for (var i = 0; i < count; i++)
        {
            things[i] = MapThing.Read(data.AsSpan(i * MapThing.Size));
        }

        Log.Verbose("LoadThings({Lump}) = {Count} things", lump, count);
        return things;
    }

    /// <summary>
    /// P_LoadBlockMap (p_setup.c:469-490): all lump words read
    /// little-endian (the SHORT() swap), origin to fixed, width/height.
    /// </summary>
    public static BlockMap LoadBlockMap(WadDirectory wad, int lump)
    {
        Log.Verbose("LoadBlockMap({Lump})", lump);

        var data = wad.CacheLumpNum(lump, PurgeTags.Level);
        var count = data.Length / 2;   // p_setup.c:476

        if (count < 4)
        {
            // C would read garbage from a truncated blockmap (UB);
            // managed code fatals (documented deviation).
            Log.Verbose("LoadBlockMap fatal: {Count} words (need >= 4)", count);
            throw new DoomFatalException(
                $"P_LoadBlockMap: blockmap too small ({count} words)");
        }

        var blockmap = new BlockMap
        {
            Lump = new short[count],
        };

        for (var i = 0; i < count; i++)
        {
            blockmap.Lump[i] = BinaryPrimitives.ReadInt16LittleEndian(
                data.AsSpan(i * 2)); // p_setup.c:478-479 SHORT() swap
        }

        blockmap.OrgX = blockmap.Lump[0] << FixedPoint.FracBits;   // p_setup.c:481
        blockmap.OrgY = blockmap.Lump[1] << FixedPoint.FracBits;   // p_setup.c:482
        blockmap.Width = blockmap.Lump[2];                          // p_setup.c:483
        blockmap.Height = blockmap.Lump[3];                         // p_setup.c:484

        Log.Verbose(
            "LoadBlockMap({Lump}) = {Width}x{Height} at ({OrgX}, {OrgY})",
            lump, blockmap.Width, blockmap.Height, blockmap.OrgX, blockmap.OrgY);
        return blockmap;
    }

    /// <summary>P_LoadReject (p_setup.c:493-497): stored as-is.</summary>
    public static byte[] LoadReject(WadDirectory wad, int lump)
    {
        Log.Verbose("LoadReject({Lump})", lump);
        var reject = wad.CacheLumpNum(lump, PurgeTags.Level);
        Log.Verbose("LoadReject({Lump}) = {Size} bytes", lump, reject.Length);
        return reject;
    }
}

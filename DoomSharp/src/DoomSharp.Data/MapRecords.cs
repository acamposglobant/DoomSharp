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
//      Raw on-disk map data records (doomdata.h). All fields are
//      little-endian; readers use BinaryPrimitives explicitly.
//      Migration of Legacy/linuxdoom-1.10/doomdata.h mapX_t structs.
//
//-----------------------------------------------------------------------------

using System.Buffers.Binary;
using System.Text;

namespace DoomSharp.Data;

/// <summary>
/// Standard WAD map lump offsets from the map marker lump
/// (p_setup.c:76-90, P_SetupLevel's W_GetNumForName calls).
/// </summary>
public static class MapLumpOffsets
{
    public const int Things = 1;
    public const int LineDefs = 2;
    public const int SideDefs = 3;
    public const int Vertexes = 4;
    public const int Segs = 5;
    public const int SubSectors = 6;
    public const int Nodes = 7;
    public const int Sectors = 8;
    public const int Reject = 9;
    public const int BlockMap = 10;
}

/// <summary>LineDef attributes (doomdata.h:98-135).</summary>
public static class LineDefFlags
{
    /// <summary>Solid, is an obstacle (doomdata.h:101).</summary>
    public const short Blocking = 1;

    /// <summary>Blocks monsters only (doomdata.h:104).</summary>
    public const short BlockMonsters = 2;

    /// <summary>Backside will not be present at all if not two sided (doomdata.h:108).</summary>
    public const short TwoSided = 4;

    /// <summary>Upper texture unpegged (doomdata.h:120).</summary>
    public const short DontPegTop = 8;

    /// <summary>Lower texture unpegged (doomdata.h:123).</summary>
    public const short DontPegBottom = 16;

    /// <summary>In AutoMap: don't map as two sided: IT'S A SECRET! (doomdata.h:126).</summary>
    public const short Secret = 32;

    /// <summary>Sound rendering: don't let sound cross two of these (doomdata.h:129).</summary>
    public const short SoundBlock = 64;

    /// <summary>Don't draw on the automap at all (doomdata.h:132).</summary>
    public const short DontDraw = 128;

    /// <summary>Set if already seen, thus drawn in automap (doomdata.h:135).</summary>
    public const short Mapped = 256;
}

/// <summary>Indicate a leaf in the BSP tree (doomdata.h:178).</summary>
public static class NodeFlags
{
    /// <summary>NF_SUBSECTOR — if set, the child is a subsector (doomdata.h:178).</summary>
    public const ushort SubSector = 0x8000;
}

/// <summary>mapvertex_t (doomdata.h:60-64) — 4 bytes.</summary>
public readonly record struct MapVertex(short X, short Y)
{
    public const int Size = 4;

    public static MapVertex Read(ReadOnlySpan<byte> data) => new(
        BinaryPrimitives.ReadInt16LittleEndian(data),
        BinaryPrimitives.ReadInt16LittleEndian(data.Slice(2)));
}

/// <summary>mapsector_t (doomdata.h:141-150) — 26 bytes.</summary>
public readonly record struct MapSector(
    short FloorHeight,
    short CeilingHeight,
    byte[] FloorPic,
    byte[] CeilingPic,
    short LightLevel,
    short Special,
    short Tag)
{
    public const int Size = 26;

    public static MapSector Read(ReadOnlySpan<byte> span)
    {
        var floorPic = span.Slice(4, 8).ToArray();
        var ceilingPic = span.Slice(12, 8).ToArray();
        return new MapSector(
            BinaryPrimitives.ReadInt16LittleEndian(span),
            BinaryPrimitives.ReadInt16LittleEndian(span.Slice(2)),
            floorPic,
            ceilingPic,
            BinaryPrimitives.ReadInt16LittleEndian(span.Slice(20)),
            BinaryPrimitives.ReadInt16LittleEndian(span.Slice(22)),
            BinaryPrimitives.ReadInt16LittleEndian(span.Slice(24)));
    }
}

/// <summary>mapsidedef_t (doomdata.h:69-78) — 30 bytes.</summary>
public readonly record struct MapSideDef(
    short TextureOffset,
    short RowOffset,
    byte[] TopTexture,
    byte[] BottomTexture,
    byte[] MidTexture,
    short Sector)
{
    public const int Size = 30;

    public static MapSideDef Read(ReadOnlySpan<byte> span)
    {
        return new MapSideDef(
            BinaryPrimitives.ReadInt16LittleEndian(span),
            BinaryPrimitives.ReadInt16LittleEndian(span.Slice(2)),
            span.Slice(4, 8).ToArray(),
            span.Slice(12, 8).ToArray(),
            span.Slice(20, 8).ToArray(),
            BinaryPrimitives.ReadInt16LittleEndian(span.Slice(28)));
    }
}

/// <summary>maplinedef_t (doomdata.h:84-93) — 14 bytes. sidenum -1 = one-sided.</summary>
public readonly record struct MapLineDef(
    short V1,
    short V2,
    short Flags,
    short Special,
    short Tag,
    short Side0,
    short Side1)
{
    public const int Size = 14;

    public static MapLineDef Read(ReadOnlySpan<byte> span) => new(
        BinaryPrimitives.ReadInt16LittleEndian(span),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(2)),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(4)),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(6)),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(8)),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(10)),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(12)));
}

/// <summary>mapsubsector_t (doomdata.h:153-158) — 4 bytes.</summary>
public readonly record struct MapSubsector(short NumSegs, short FirstSeg)
{
    public const int Size = 4;

    public static MapSubsector Read(ReadOnlySpan<byte> span) => new(
        BinaryPrimitives.ReadInt16LittleEndian(span),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(2)));
}

/// <summary>mapseg_t (doomdata.h:163-171) — 12 bytes.</summary>
public readonly record struct MapSeg(
    short V1,
    short V2,
    short Angle,
    short LineDef,
    short Side,
    short Offset)
{
    public const int Size = 12;

    public static MapSeg Read(ReadOnlySpan<byte> span) => new(
        BinaryPrimitives.ReadInt16LittleEndian(span),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(2)),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(4)),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(6)),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(8)),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(10)));
}

/// <summary>mapnode_t (doomdata.h:180-196) — 28 bytes. children are UNSIGNED (NF_SUBSECTOR bit).</summary>
public readonly record struct MapNode(
    short X, short Y, short Dx, short Dy,
    short[] BBox0,   // 4 shorts
    short[] BBox1,   // 4 shorts
    ushort Child0,   // unsigned — 0x8000 bit must not sign-extend
    ushort Child1)
{
    public const int Size = 28;

    public static MapNode Read(ReadOnlySpan<byte> span)
    {
        var bbox0 = new short[4];
        var bbox1 = new short[4];
        for (var i = 0; i < 4; i++)
        {
            bbox0[i] = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(8 + i * 2));
            bbox1[i] = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(16 + i * 2));
        }
        return new MapNode(
            BinaryPrimitives.ReadInt16LittleEndian(span),
            BinaryPrimitives.ReadInt16LittleEndian(span.Slice(2)),
            BinaryPrimitives.ReadInt16LittleEndian(span.Slice(4)),
            BinaryPrimitives.ReadInt16LittleEndian(span.Slice(6)),
            bbox0,
            bbox1,
            BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(24)),
            BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(26)));
    }
}

/// <summary>mapthing_t (doomdata.h:203-210) — 10 bytes.</summary>
public readonly record struct MapThing(
    short X, short Y, short Angle, short Type, short Options)
{
    public const int Size = 10;

    public static MapThing Read(ReadOnlySpan<byte> span) => new(
        BinaryPrimitives.ReadInt16LittleEndian(span),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(2)),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(4)),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(6)),
        BinaryPrimitives.ReadInt16LittleEndian(span.Slice(8)));
}

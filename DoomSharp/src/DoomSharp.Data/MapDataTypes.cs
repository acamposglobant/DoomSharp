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
//      Runtime map data structures (r_defs.h equivalents). C pointer
//      references become int indices into the owning LevelData arrays.
//      Texture/flat names are stored raw (8 bytes) — R_TextureNumForName
//      resolution is deferred to the renderer item (DOOM-030), per the
//      approved DOOM-020 plan (Option A).
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Data;

/// <summary>slope_t (r_defs.h:52-57) — line orientation class.</summary>
public enum SlopeType
{
    /// <summary>ST_HORIZONTAL (r_defs.h:54).</summary>
    Horizontal = 0,

    /// <summary>ST_VERTICAL (r_defs.h:55).</summary>
    Vertical = 1,

    /// <summary>ST_POSITIVE (r_defs.h:56).</summary>
    Positive = 2,

    /// <summary>ST_NEGATIVE (r_defs.h:57).</summary>
    Negative = 3,
}

/// <summary>Bounding box slot order (r_defs.h:60-65, m_bbox.h).</summary>
public static class BBox
{
    public const int Left = 0;
    public const int Top = 1;
    public const int Right = 2;
    public const int Bottom = 3;
}

/// <summary>vertex_t (r_defs.h:44-48) — fixed-point coordinates.</summary>
public sealed class Vertex
{
    public int X;
    public int Y;
}

/// <summary>
/// side_t (r_defs.h:70-88). Texture names stored raw (8 bytes) —
/// resolved to texture indices by the renderer (DOOM-030 deviation).
/// </summary>
public sealed class Side
{
    public int TextureOffset;   // fixed
    public int RowOffset;       // fixed
    public byte[] TopTexture = [];    // raw 8-byte name
    public byte[] BottomTexture = []; // raw 8-byte name
    public byte[] MidTexture = [];    // raw 8-byte name
    public int Sector;          // index

    /// <summary>
    /// Resolved texture indices (side_t.toptexture etc. in C are
    /// shorts after P_LoadSideDefs calls R_TextureNumForName). -1 =
    /// not yet resolved; populated by TextureManager.ResolveLevelTextures.
    /// </summary>
    public int TopTextureIndex = -1;
    public int BottomTextureIndex = -1;
    public int MidTextureIndex = -1;
}

/// <summary>line_t (r_defs.h:150-190) — index-based references.</summary>
public sealed class Line
{
    public short Flags;
    public short Special;
    public short Tag;
    public int V1;              // vertex index
    public int V2;              // vertex index
    public int Dx;              // fixed
    public int Dy;              // fixed
    public SlopeType SlopeType;
    public int[] BBox = new int[4];
    public int[] Sidenum = new int[2];
    public int FrontSector = -1;
    public int BackSector = -1;

    /// <summary>
    /// line_t.validcount (r_defs.h) — game-side dedup marker used by
    /// P_BlockLinesIterator. Lives here so the renderer and game share
    /// the same Line instances.
    /// </summary>
    public int ValidCount;
}

/// <summary>sector_t (r_defs.h:246-289). Flat names stored raw (DOOM-030 resolves).</summary>
public sealed class Sector
{
    public int FloorHeight;     // fixed
    public int CeilingHeight;   // fixed
    public byte[] FloorPic = [];      // raw 8-byte flat name
    public byte[] CeilingPic = [];    // raw 8-byte flat name
    public short LightLevel;
    public short Special;
    public short Tag;

    /// <summary>
    /// Resolved flat indices (sector_t.floorpic/ceilingpic in C are
    /// ints after P_LoadSectors calls R_FlatNumForName). -1 = not yet
    /// resolved; populated by TextureManager.ResolveLevelTextures.
    /// </summary>
    public int FloorFlat = -1;
    public int CeilingFlat = -1;
}

/// <summary>seg_t (r_defs.h:92-118) — index-based references.</summary>
public sealed class Seg
{
    public int V1;              // vertex index
    public int V2;              // vertex index
    public int Angle;           // angle_t (unsigned BAM)
    public int Offset;          // fixed
    public int LineDef;         // index
    public int Side;            // 0 front / 1 back
    public int FrontSector = -1;
    public int BackSector = -1;
}

/// <summary>subsector_t (r_defs.h:292-299).</summary>
public sealed class Subsector
{
    public int NumSegs;
    public int FirstSeg;
}

/// <summary>node_t (r_defs.h:292-311) — children keep the NF_SUBSECTOR bit.</summary>
public sealed class Node
{
    public int X, Y, Dx, Dy;    // fixed
    public int[][] BBox = [new int[4], new int[4]];
    public ushort[] Children = new ushort[2];
}

/// <summary>
/// blockmap: origin, dimensions, offsets table, and the raw lump words
/// (p_setup.c:469-490). blocklinks allocation is deferred to the
/// blockmap iteration item (links are built by P_CreateBlockMap-era
/// code at thing spawn time in the original).
/// </summary>
public sealed class BlockMap
{
    public int OrgX;            // fixed (blockmaplump[0] << FRACBITS)
    public int OrgY;            // fixed
    public int Width;
    public int Height;
    public short[] Lump = [];   // all words, SHORT()-swapped (little-endian read)
}

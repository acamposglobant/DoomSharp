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
//      patch_t (r_data.h:20-27 / doomdata.h): column-based masked
//      picture with posts. Used by V_DrawPatch and sprite rendering.
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Rendering;

/// <summary>
/// patch_t (r_data.h:16-27): width, height, and per-column offsets
/// into the lump. Columns are walked as posts (topdelta/length/pixels)
/// terminated by topdelta 0xff.
/// </summary>
public sealed class Patch
{
    private readonly byte[] _data;

    public Patch(byte[] lump)
    {
        _data = lump;
        Width = lump[0] | (lump[1] << 8);
        Height = lump[2] | (lump[3] << 8);
        LeftOffset = (short)(lump[4] | (lump[5] << 8));
        TopOffset = (short)(lump[6] | (lump[7] << 8));
    }

    /// <summary>patch_t.width (SHORT).</summary>
    public int Width { get; }

    /// <summary>patch_t.height (SHORT).</summary>
    public int Height { get; }

    /// <summary>patch_t.leftoffset (SHORT).</summary>
    public int LeftOffset { get; }

    /// <summary>patch_t.topoffset (SHORT).</summary>
    public int TopOffset { get; }

    /// <summary>Column directory entry (patch_t.columnofs[]).</summary>
    public int ColumnOfs(int column) =>
        _data[8 + column * 4]
        | (_data[9 + column * 4] << 8)
        | (_data[10 + column * 4] << 16)
        | (_data[11 + column * 4] << 24);

    /// <summary>Returns the post reader for a column.</summary>
    public PatchColumn Column(int column) => new(_data, ColumnOfs(column));
}

/// <summary>
/// Post cursor over one patch column (the C walks raw bytes:
/// topdelta, length, padding, pixels, padding, 0xff terminator).
/// </summary>
public struct PatchColumn
{
    private readonly byte[] _data;
    private int _ofs;

    public PatchColumn(byte[] data, int ofs)
    {
        _data = data;
        _ofs = ofs;
        TopDelta = data[_ofs];
        // The 0xff terminator may be the last byte of the lump; only
        // read the length for a real post.
        Length = TopDelta != 0xff ? data[_ofs + 1] : (byte)0;
    }

    /// <summary>topdelta — 0xff terminates the column.</summary>
    public byte TopDelta { get; }

    /// <summary>length — pixel count of this post.</summary>
    public byte Length { get; }

    /// <summary>Pixel bytes of the current post (starts after the 2-byte header + pad).</summary>
    public readonly byte[] Pixels => _data;

    /// <summary>Offset of the first pixel byte of the current post.</summary>
    public readonly int PixelOfs => _ofs + 3;

    /// <summary>Advances to the next post (length + 4 bytes per post).</summary>
    public PatchColumn Next()
    {
        _ofs += Length + 4;
        return new PatchColumn(_data, _ofs);
    }
}

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
//      Binary builders for renderer-data WAD fixtures: patch_t lumps
//      with known pixel data, PNAMES, TEXTURE1, COLORMAP.
//      Layouts per r_data.h (patch_t, column_t, maptexture_t).
//
//-----------------------------------------------------------------------------

using System.Buffers.Binary;

namespace DoomSharp.Tests.Unit;

/// <summary>
/// Builds little-endian binary renderer lumps for tests.
/// </summary>
public static class TextureWadBuilder
{
    /// <summary>
    /// Builds a patch_t with a single full-height column post per
    /// column: topdelta=0, length=height, pixels, pad, 0xff terminator.
    /// Pixel value = (column * 16 + row) &amp; 0xff by default, or a
    /// caller-supplied generator.
    /// </summary>
    public static byte[] Patch(int width, int height, Func<int, int, byte>? pixel = null)
    {
        pixel ??= (x, y) => (byte)((x * 16 + y) & 0xff);

        // Header: width(2), height(2), columnofs[width](4*width),
        // leftoffset(2), topoffset(2) = 8 + 4*width.
        var headerSize = 8 + width * 4;
        var columnData = new List<byte[]>();

        for (var x = 0; x < width; x++)
        {
            // One post: topdelta(1), length(1), pad(1), pixels(height),
            // pad(1) — pixels at post+3 (r_data.c:200), post size
            // length+4 (r_data.c:216). Column ends with a 0xff topdelta.
            var post = new byte[3 + height + 1];
            post[0] = 0;                 // topdelta
            post[1] = (byte)height;      // length
            post[2] = 0;                 // pad byte (before pixels)
            for (var y = 0; y < height; y++)
            {
                post[3 + y] = pixel(x, y);
            }
            post[3 + height] = 0;        // pad byte (after pixels)
            columnData.Add(post);
            // Column terminator: a lone 0xff topdelta byte (per column).
            columnData.Add([0xff]);
        }

        var totalSize = headerSize + columnData.Sum(c => c.Length);
        var data = new byte[totalSize];
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0), (short)width);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), (short)height);
        // columnofs[x] = position of column x's first post. columnData
        // holds 2 entries per column (post + 0xff terminator).
        var pos = headerSize;
        for (var x = 0; x < width; x++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8 + x * 4), pos);
            columnData[x * 2].CopyTo(data, pos);      // post
            pos += columnData[x * 2].Length;
            columnData[x * 2 + 1].CopyTo(data, pos);  // 0xff terminator
            pos += columnData[x * 2 + 1].Length;
        }
        return data;
    }

    /// <summary>PNAMES lump: int32 count + count x 8-byte names.</summary>
    public static byte[] PNames(params string[] names)
    {
        var data = new byte[4 + names.Length * 8];
        BinaryPrimitives.WriteInt32LittleEndian(data, names.Length);
        for (var i = 0; i < names.Length; i++)
        {
            WriteName(data.AsSpan(4 + i * 8), names[i]);
        }
        return data;
    }

    /// <summary>
    /// TEXTURE1 lump: int32 count, int32 offsets[], maptexture_t records
    /// (name[8], width, height, patchcount, mappatch_t[patchcount]).
    /// </summary>
    public static byte[] TextureX(params (string name, short width, short height, (short ox, short oy, short patch)[] patches)[] textures)
    {
        var offsets = new int[textures.Length];
        var ofs = 4 + textures.Length * 4;
        var body = new List<byte[]>();
        for (var i = 0; i < textures.Length; i++)
        {
            offsets[i] = ofs;
            var (name, width, height, patches) = textures[i];
            var size = 16 + patches.Length * 6;
            var tex = new byte[size];
            WriteName(tex.AsSpan(0, 8), name);
            BinaryPrimitives.WriteInt16LittleEndian(tex.AsSpan(8), width);
            BinaryPrimitives.WriteInt16LittleEndian(tex.AsSpan(10), height);
            BinaryPrimitives.WriteInt16LittleEndian(tex.AsSpan(12), (short)patches.Length);
            for (var p = 0; p < patches.Length; p++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(tex.AsSpan(16 + p * 6), patches[p].ox);
                BinaryPrimitives.WriteInt16LittleEndian(tex.AsSpan(18 + p * 6), patches[p].oy);
                BinaryPrimitives.WriteInt16LittleEndian(tex.AsSpan(20 + p * 6), patches[p].patch);
            }
            body.Add(tex);
            ofs += size;
        }

        var data = new byte[4 + textures.Length * 4 + body.Sum(b => b.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(data, textures.Length);
        for (var i = 0; i < textures.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4 + i * 4), offsets[i]);
        }
        var pos = 4 + textures.Length * 4;
        foreach (var b in body)
        {
            b.CopyTo(data, pos);
            pos += b.Length;
        }
        return data;
    }

    /// <summary>8-byte NUL-padded name.</summary>
    public static void WriteName(Span<byte> dest, string name)
    {
        var i = 0;
        for (; i < name.Length && i < 8; i++)
        {
            dest[i] = (byte)name[i];
        }
        for (; i < 8; i++)
        {
            dest[i] = 0;
        }
    }

    /// <summary>COLORMAP lump: 34 x 256 bytes, byte[i] = i &amp; 0xff.</summary>
    public static byte[] Colormap()
    {
        var data = new byte[34 * 256];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i & 0xff);
        }
        return data;
    }
}

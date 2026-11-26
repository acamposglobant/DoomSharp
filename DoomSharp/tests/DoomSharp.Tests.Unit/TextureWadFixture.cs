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
//      Assembles renderer-data lumps into a WAD file for tests.
//      Layout: header (12B), directory (16B/lump), then lump data.
//
//-----------------------------------------------------------------------------

using System.Buffers.Binary;

namespace DoomSharp.Tests.Unit;

/// <summary>
/// Builds a synthetic PWAD with renderer data: PNAMES, TEXTURE1,
/// patch lumps, F_START/F_END flats, S_START/S_END sprites, COLORMAP.
/// </summary>
public static class TextureWadFixture
{
    /// <summary>
    /// Writes a WAD with: 2 patches (8x4 and 4x4), PNAMES, TEXTURE1
    /// (single-patch 8x4 texture + two-patch overlapping 8x4 texture),
    /// F_START/2 flats/F_END, S_START/1 sprite/S_END, COLORMAP.
    /// Returns the file path.
    /// </summary>
    public static string WriteTextureWad(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "textest.wad");

        var patchA = TextureWadBuilder.Patch(8, 4);                 // pixels (x*16+y)&0xff
        var patchB = TextureWadBuilder.Patch(4, 4, (x, y) => 0xEE); // solid

        var lumps = new List<(string Name, byte[] Data)>
        {
            ("PPSTART", []),                    // patch marker (before patches)
            ("WALL0000", patchA),               // patch 0
            ("WALL0001", patchB),               // patch 1
            ("PPEND", []),                      // patch marker (after patches)
            ("PNAMES", TextureWadBuilder.PNames("WALL0000", "WALL0001")),
            ("TEXTURE1", TextureWadBuilder.TextureX(
                ("TEXSINGL", 8, 4, [(0, 0, 0)]),                    // single patch
                ("TEXDOUBL", (short)8, (short)4, new[]
                {
                    ((short)0, (short)0, (short)0),   // patch A at x=0
                    ((short)2, (short)0, (short)1),   // patch B overlaps x>=2
                }))),
            ("F_START", []),
            ("FLAT1", Enumerable.Repeat((byte)0xAB, 4096).ToArray()),
            ("FLAT2", Enumerable.Repeat((byte)0xCD, 4096).ToArray()),
            ("F_END", []),
            ("S_START", []),
            ("SPR1", SpritePatch(4, 6, 2, 1)),
            ("S_END", []),
            ("COLORMAP", TextureWadBuilder.Colormap()),
        };

        return WriteWad(path, lumps);
    }

    /// <summary>Minimal sprite patch_t (width/height/offsets only used).</summary>
    public static byte[] SpritePatch(int width, int height, short leftoffset, short topoffset)
    {
        var data = new byte[8 + width * 4];
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0), (short)width);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), (short)height);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(4), leftoffset);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(6), topoffset);
        // columnofs all point past header (empty columns are fine for
        // header-only reads).
        for (var x = 0; x < width; x++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8 + x * 4), 8 + width * 4);
        }
        return data;
    }

    /// <summary>Writes lumps into a PWAD (header, directory, data).</summary>
    public static string WriteWad(string path, List<(string Name, byte[] Data)> lumps)
    {
        using var stream = new FileStream(path, FileMode.Create);
        var numLumps = lumps.Count;
        var dataStart = 12 + numLumps * 16;

        var header = new byte[12];
        WriteAscii(header.AsSpan(0, 4), "PWAD");
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), numLumps);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), 12);
        stream.Write(header);

        var directoryData = new byte[numLumps * 16];
        var dataOfs = dataStart;
        for (var i = 0; i < numLumps; i++)
        {
            var entry = directoryData.AsSpan(i * 16);
            BinaryPrimitives.WriteInt32LittleEndian(entry, dataOfs);
            BinaryPrimitives.WriteInt32LittleEndian(entry.Slice(4), lumps[i].Data.Length);
            TextureWadBuilder.WriteName(entry.Slice(8, 8), lumps[i].Name);
            dataOfs += lumps[i].Data.Length;
        }
        stream.Write(directoryData);

        foreach (var (_, data) in lumps)
        {
            stream.Write(data);
        }

        return path;
    }

    private static void WriteAscii(Span<byte> dest, string text)
    {
        for (var i = 0; i < dest.Length; i++)
        {
            dest[i] = i < text.Length ? (byte)text[i] : (byte)0;
        }
    }
}

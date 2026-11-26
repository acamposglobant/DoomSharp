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
//      Assembles synthetic map lumps into a WAD file for tests.
//      Layout: header (12B), directory (16B/lump), then lump data.
//
//-----------------------------------------------------------------------------

using System.Buffers.Binary;

namespace DoomSharp.Tests.Unit;

/// <summary>
/// Builds a synthetic PWAD containing one complete map (MAP01 marker
/// + 10 standard lumps) for map loader tests.
/// </summary>
public static class MapWadFixture
{
    /// <summary>
    /// Writes a WAD with a MAP01 map: 4 vertexes forming a square,
    /// 1 sector, 2 sidedefs, 2 linedefs (one two-sided, one
    /// single-sided), 1 subsector, 2 segs, 1 node, 1 thing, blockmap,
    /// reject. Returns the file path.
    /// </summary>
    public static string WriteMapWad(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "maptest.wad");

        var lumps = new List<(string Name, byte[] Data)>
        {
            ("MAP01", []),
            ("THINGS", MapLumpBuilder.Things((32, 32, 90, 1, 7))),
            ("LINEDEFS", MapLumpBuilder.LineDefs(
                (0, 1, 4, 0, 0, 0, 1),    // line 0: ML_TWOSIDED, both sides
                (1, 2, 1, 0, 0, 0, -1))), // line 1: single-sided
            ("SIDEDEFS", MapLumpBuilder.SideDefs(
                (0, 0, "STONE1", "STONE2", "DOOR1", 0),
                (8, 16, "STONE3", "STONE4", "DOOR2", 0))),
            ("VERTEXES", MapLumpBuilder.Vertexes(
                (0, 0), (100, 0), (100, 50), (0, 50))),
            ("SEGS", MapLumpBuilder.Segs(
                (0, 1, 0, 0, 0, 0),
                (1, 2, 16384, 1, 0, 32))),
            ("SSECTORS", MapLumpBuilder.Subsectors((2, 0))),
            ("NODES", MapLumpBuilder.Nodes(
                (50, 25, 100, 50,
                    [-10, -20, 110, 60],
                    [-5, -5, 105, 55],
                    0x8000, 0x8001))),
            ("SECTORS", MapLumpBuilder.Sectors(
                (0, 128, "FLOOR4_8", "CEIL3_5", 160, 0, 0))),
            ("REJECT", MapLumpBuilder.Reject(0x01, 0x02, 0x03)),
            ("BLOCKMAP", MapLumpBuilder.BlockMap(-64, -64, 4, 3,
                0, 0, 0, 0, 0, 0, 0, 0, -1, -1, -1, -1)),
        };

        using var stream = new FileStream(path, FileMode.Create);
        var numLumps = lumps.Count;
        var dataStart = 12 + numLumps * 16;

        // Header: infotableofs = 12 (directory directly after header).
        var header = new byte[12];
        WriteAscii(header.AsSpan(0, 4), "PWAD");
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), numLumps);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), 12);
        stream.Write(header);

        // Directory entries (positions known: data follows directory).
        var directoryData = new byte[numLumps * 16];
        var dataOfs = dataStart;
        for (var i = 0; i < numLumps; i++)
        {
            var entry = directoryData.AsSpan(i * 16);
            BinaryPrimitives.WriteInt32LittleEndian(entry, dataOfs);
            BinaryPrimitives.WriteInt32LittleEndian(entry.Slice(4), lumps[i].Data.Length);
            MapLumpBuilder.WriteName(entry.Slice(8, 8), lumps[i].Name);
            dataOfs += lumps[i].Data.Length;
        }
        stream.Write(directoryData);

        // Lump data.
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

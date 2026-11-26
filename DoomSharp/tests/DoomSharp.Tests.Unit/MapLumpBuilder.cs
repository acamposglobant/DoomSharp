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
//      Binary builders for synthetic map lumps (little-endian, exact
//      doomdata.h layouts). Expected values are hand-derived from the
//      binary format spec — same coverage rationale as the C harness
//      decision documented in the DOOM-020 plan.
//
//-----------------------------------------------------------------------------

using System.Buffers.Binary;

namespace DoomSharp.Tests.Unit;

/// <summary>
/// Builds little-endian binary map lumps for tests.
/// </summary>
public static class MapLumpBuilder
{
    public static byte[] Vertexes(params (short X, short Y)[] v)
    {
        var data = new byte[v.Length * 4];
        for (var i = 0; i < v.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 4), v[i].X);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 4 + 2), v[i].Y);
        }
        return data;
    }

    public static byte[] Sectors(params (short floorH, short ceilH, string floorPic, string ceilPic, short light, short special, short tag)[] s)
    {
        var data = new byte[s.Length * 26];
        for (var i = 0; i < s.Length; i++)
        {
            var (floorH, ceilH, floorPic, ceilPic, light, special, tag) = s[i];
            var span = data.AsSpan(i * 26);
            BinaryPrimitives.WriteInt16LittleEndian(span, floorH);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(2), ceilH);
            WriteName(span.Slice(4, 8), floorPic);
            WriteName(span.Slice(12, 8), ceilPic);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(20), light);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(22), special);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(24), tag);
        }
        return data;
    }

    public static byte[] SideDefs(params (short texOfs, short rowOfs, string top, string bottom, string mid, short sector)[] s)
    {
        var data = new byte[s.Length * 30];
        for (var i = 0; i < s.Length; i++)
        {
            var (texOfs, rowOfs, top, bottom, mid, sector) = s[i];
            var span = data.AsSpan(i * 30);
            BinaryPrimitives.WriteInt16LittleEndian(span, texOfs);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(2), rowOfs);
            WriteName(span.Slice(4, 8), top);
            WriteName(span.Slice(12, 8), bottom);
            WriteName(span.Slice(20, 8), mid);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(28), sector);
        }
        return data;
    }

    public static byte[] LineDefs(params (short v1, short v2, short flags, short special, short tag, short side0, short side1)[] l)
    {
        var data = new byte[l.Length * 14];
        for (var i = 0; i < l.Length; i++)
        {
            var (v1, v2, flags, special, tag, side0, side1) = l[i];
            var span = data.AsSpan(i * 14);
            BinaryPrimitives.WriteInt16LittleEndian(span, v1);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(2), v2);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(4), flags);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(4 + 2), special);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(8), tag);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(10), side0);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(12), side1);
        }
        return data;
    }

    public static byte[] Subsectors(params (short numSegs, short firstSeg)[] s)
    {
        var data = new byte[s.Length * 4];
        for (var i = 0; i < s.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 4), s[i].numSegs);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 4 + 2), s[i].firstSeg);
        }
        return data;
    }

    public static byte[] Segs(params (short v1, short v2, short angle, short linedef, short side, short offset)[] s)
    {
        var data = new byte[s.Length * 12];
        for (var i = 0; i < s.Length; i++)
        {
            var span = data.AsSpan(i * 12);
            BinaryPrimitives.WriteInt16LittleEndian(span, s[i].v1);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(2), s[i].v2);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(4), s[i].angle);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(6), s[i].linedef);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(8), s[i].side);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(10), s[i].offset);
        }
        return data;
    }

    public static byte[] Nodes(params (short x, short y, short dx, short dy, short[] bbox0, short[] bbox1, ushort child0, ushort child1)[] n)
    {
        var data = new byte[n.Length * 28];
        for (var i = 0; i < n.Length; i++)
        {
            var (x, y, dx, dy, bbox0, bbox1, child0, child1) = n[i];
            var span = data.AsSpan(i * 28);
            BinaryPrimitives.WriteInt16LittleEndian(span, x);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(2), y);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(4), dx);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(6), dy);
            for (var j = 0; j < 4; j++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(span.Slice(8 + j * 2), bbox0[j]);
                BinaryPrimitives.WriteInt16LittleEndian(span.Slice(16 + j * 2), bbox1[j]);
            }
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(24), child0);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(26), child1);
        }
        return data;
    }

    public static byte[] Things(params (short x, short y, short angle, short type, short options)[] t)
    {
        var data = new byte[t.Length * 10];
        for (var i = 0; i < t.Length; i++)
        {
            var span = data.AsSpan(i * 10);
            BinaryPrimitives.WriteInt16LittleEndian(span, t[i].x);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(2), t[i].y);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(4), t[i].angle);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(6), t[i].type);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(8), t[i].options);
        }
        return data;
    }

    public static byte[] BlockMap(short orgX, short orgY, short width, short height, params short[] offsets)
    {
        var words = new List<short> { orgX, orgY, width, height };
        words.AddRange(offsets);
        var data = new byte[words.Count * 2];
        for (var i = 0; i < words.Count; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), words[i]);
        }
        return data;
    }

    public static byte[] Reject(params byte[] bytes) => bytes;

    /// <summary>Writes an 8-byte name, NUL-padded, ASCII.</summary>
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
}

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
//      Tests for Video (v_video.c): V_DrawPatch post transparency,
//      flipped variant, out-of-bounds behavior, CopyRect, block ops,
//      dirtybox marking, and the gamma table.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Rendering;
using Xunit;

namespace DoomSharp.Tests.Unit;

[Collection("RendererSerial")]
public class VideoTests
{
    /// <summary>
    /// Builds a 4x4 patch: column 0 has two posts (transparency gap),
    /// column 1 empty, column 2 one post at topdelta 1, column 3 empty.
    /// </summary>
    private static byte[] BuildTestPatch()
    {
        var data = new byte[256];
        var pos = 8 + 4 * 4;   // header + 4 column offsets
        var ofs = new int[4];

        // column 0: post(topdelta 0, len 2), post(topdelta 3, len 1)
        ofs[0] = pos;
        data[pos++] = 0; data[pos++] = 2; data[pos++] = 0;
        data[pos++] = 0x11;
        data[pos++] = 0x22;
        data[pos++] = 0;
        data[pos++] = 3; data[pos++] = 1; data[pos++] = 0;
        data[pos++] = 0x33;
        data[pos++] = 0;
        data[pos++] = 0xff;

        // column 1: empty
        ofs[1] = pos;
        data[pos++] = 0xff;

        // column 2: post(topdelta 1, 4 pixels)
        ofs[2] = pos;
        data[pos++] = 1; data[pos++] = 4; data[pos++] = 0;
        data[pos++] = 0xAA; data[pos++] = 0x22; data[pos++] = 0x33; data[pos++] = 0x44;
        data[pos++] = 0;
        data[pos++] = 0xff;

        // column 3: empty
        ofs[3] = pos;
        data[pos++] = 0xff;

        // header: width, height, leftofs, topofs (little-endian shorts)
        data[0] = 4; data[1] = 0;
        data[2] = 4; data[3] = 0;
        data[4] = 2; data[5] = 0;
        data[6] = 1; data[7] = 0;
        for (var i = 0; i < 4; i++)
        {
            data[8 + i * 4] = (byte)(ofs[i] & 0xff);
            data[9 + i * 4] = (byte)((ofs[i] >> 8) & 0xff);
            data[10 + i * 4] = (byte)((ofs[i] >> 16) & 0xff);
            data[11 + i * 4] = (byte)((ofs[i] >> 24) & 0xff);
        }

        return data;
    }

    [Fact]
    public void DrawPatch_MatchesCVector()
    {
        Video.Init();
        Array.Clear(Video.Screens[0]);

        var patch = new Patch(BuildTestPatch());
        Video.DrawPatch(10, 10, 0, patch);

        var expected = RMainVectors.DrawPatch.EnumerateArray().First();
        Assert.Equal("patch_10_10", expected.GetProperty("name").GetString());

        var ey = expected.GetProperty("y").GetInt32();
        var ex = expected.GetProperty("x").GetInt32();
        var eh = expected.GetProperty("h").GetInt32();
        var ew = expected.GetProperty("w").GetInt32();
        var pixels = expected.GetProperty("pixels").EnumerateArray().Select(p => (byte)p.GetInt32()).ToArray();

        var actual = new byte[eh * ew];
        for (var row = 0; row < eh; row++)
        {
            for (var col = 0; col < ew; col++)
            {
                actual[row * ew + col] = Video.Screens[0][(ey + row) * Video.ScreenWidth + ex + col];
            }
        }

        Assert.Equal(pixels, actual);
    }

    [Fact]
    public void DrawPatch_PostTransparency_LeavesBackground()
    {
        Video.Init();
        Array.Clear(Video.Screens[0]);

        var patch = new Patch(BuildTestPatch());
        Video.DrawPatch(10, 10, 0, patch);

        // Patch drawn at (10,10) with leftofs 2, topofs 1 → origin (8, 9).
        // Column 0 (screen x=8): post at topdelta 0 → rows 9,10 = 0x11,0x22;
        // post at topdelta 3 → row 12 = 0x33. Row 11 stays transparent.
        Assert.Equal(0x11, Video.Screens[0][9 * Video.ScreenWidth + 8]);
        Assert.Equal(0x22, Video.Screens[0][10 * Video.ScreenWidth + 8]);
        Assert.Equal(0x33, Video.Screens[0][12 * Video.ScreenWidth + 8]);
        // Gap row (topdelta 1..2 of column 0) stays 0.
        Assert.Equal(0, Video.Screens[0][11 * Video.ScreenWidth + 8]);

        // Column 1 (screen x=9) is empty.
        Assert.Equal(0, Video.Screens[0][9 * Video.ScreenWidth + 9]);

        // Column 2 (screen x=10): topdelta 1 → rows 10..13.
        Assert.Equal(0xAA, Video.Screens[0][10 * Video.ScreenWidth + 10]);
        Assert.Equal(0x44, Video.Screens[0][13 * Video.ScreenWidth + 8 + 2]);
    }

    [Fact]
    public void DrawPatch_OutOfBounds_IsIgnoredNotFatal()
    {
        Video.Init();
        var patch = new Patch(BuildTestPatch());

        // TNT.WAD behavior: out-of-range patches are ignored (stderr
        // warning), not fatal.
        Video.DrawPatch(-10, -10, 0, patch);
        Video.DrawPatch(Video.ScreenWidth - 1, 0, 0, patch);
        Video.DrawPatch(0, Video.ScreenHeight - 1, 0, patch);
    }

    [Fact]
    public void DrawPatchFlipped_MirrorsColumnOrder()
    {
        Video.Init();
        Array.Clear(Video.Screens[0]);

        var patch = new Patch(BuildTestPatch());
        Video.DrawPatchFlipped(10, 10, 0, patch);

        // Flipped: source column w-1-col. Screen x=8 shows original col 3
        // (empty), x=9 shows original col 2 (post at topdelta 1 → rows
        // 10..13), x=10 shows original col 1 (empty), x=11 shows original
        // col 0 (posts at rows 9,10 and 12).
        Assert.Equal(0, Video.Screens[0][9 * Video.ScreenWidth + 8]);
        Assert.Equal(0x11, Video.Screens[0][9 * Video.ScreenWidth + 11]);
        Assert.Equal(0x22, Video.Screens[0][10 * Video.ScreenWidth + 11]);
        Assert.Equal(0xAA, Video.Screens[0][10 * Video.ScreenWidth + 9]);
    }

    [Fact]
    public void CopyRect_RoundTripsBetweenScreens()
    {
        Video.Init();
        Array.Clear(Video.Screens[0]);
        Array.Clear(Video.Screens[1]);

        for (var i = 0; i < Video.ScreenSize; i++)
        {
            Video.Screens[1][i] = (byte)(i & 0xff);
        }

        Video.CopyRect(5, 6, 1, 10, 4, 20, 30, 1);

        for (var row = 0; row < 4; row++)
        {
            for (var col = 0; col < 10; col++)
            {
                Assert.Equal(
                    (byte)(((5 + col) + (6 + row) * Video.ScreenWidth) & 0xff),
                    Video.Screens[1][(20 + col) + (30 + row) * Video.ScreenWidth]);
            }
        }
    }

    [Fact]
    public void DrawBlock_GetBlock_RoundTrip()
    {
        Video.Init();
        Array.Clear(Video.Screens[2]);

        var src = new byte[3 * 2];
        for (var i = 0; i < src.Length; i++)
        {
            src[i] = (byte)(i + 1);
        }

        Video.DrawBlock(7, 9, 2, 3, 2, src);

        var dest = new byte[3 * 2];
        Video.GetBlock(7, 9, 2, 3, 2, dest);

        Assert.Equal(src, dest);
    }

    [Fact]
    public void MarkRect_ExpandsDirtyBox()
    {
        // dirtybox starts at all zeros (v_video.c:46) and only expands
        // (M_AddToBox semantics: min for left/top, max for right/bottom).
        Array.Clear(Video.DirtyBox);

        Video.MarkRect(10, 20, 5, 6);

        Assert.Equal(0, Video.DirtyBox[0]);    // left: 0 < 10, stays 0
        Assert.Equal(25, Video.DirtyBox[1]);   // top: max(0,20), then max(20,25)
        Assert.Equal(14, Video.DirtyBox[2]);   // right (10+5-1)
        Assert.Equal(0, Video.DirtyBox[3]);    // bottom: 0 < 25, stays 0
    }

    [Fact]
    public void GammaTable_HasFiveRowsOf256()
    {
        Assert.Equal(5, Video.GammaTable.Length);
        Assert.All(Video.GammaTable, row => Assert.Equal(256, row.Length));
        // Row 0 is identity-ish (1..128,128..255); row 4 brightens 0 → 16.
        Assert.Equal(16, Video.GammaTable[4][0]);
        Assert.Equal(1, Video.GammaTable[0][0]);
    }

    [Fact]
    public void Init_AllocatesFourScreens()
    {
        Video.Init();
        Assert.Equal(5, Video.Screens.Length);
        for (var i = 0; i < 4; i++)
        {
            Assert.NotNull(Video.Screens[i]);
            Assert.Equal(Video.ScreenSize, Video.Screens[i].Length);
        }
    }
}

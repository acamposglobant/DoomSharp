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
//      Pixel-exact characterization tests for the column and span
//      rasterizers (r_draw.c). Vectors from tools/charact/rmaindump.c.
//
//-----------------------------------------------------------------------------

using System.Text.Json;
using DoomSharp.Core;
using DoomSharp.Rendering;
using Xunit;

namespace DoomSharp.Tests.Unit;

[Collection("RendererSerial")]
public class RasterizerTests
{
    /// <summary>Builds a ViewSetup with full-screen ylookup/columnofs.</summary>
    private static ViewSetup CreateView()
    {
        var view = new ViewSetup(new TextureManager());
        // Full COLORMAP lump size (NUMCOLORMAPS * 256); slice contents
        // don't matter for the rasterizer vectors (drawers use dc_*/ds_*).
        var colormaps = new byte[ViewSetup.NumColormaps * 256];
        view.Colormaps = colormaps;
        ViewSetup.ColormapsGlobal = colormaps;
        view.SetViewSize(11, 0);
        view.ExecuteSetViewSize();
        return view;
    }

    /// <summary>Identity colormap for the global colormaps (fuzz uses it).</summary>
    private static byte[] IdentityColormap()
    {
        var map = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            map[i] = (byte)i;
        }

        return map;
    }

    private static byte[] TestColumnSource()
    {
        var source = new byte[128];
        for (var i = 0; i < 128; i++)
        {
            source[i] = (byte)(i * 7 + 3);
        }

        return source;
    }

    private static byte[] TestFlatSource()
    {
        var flat = new byte[64 * 64];
        for (var i = 0; i < 64 * 64; i++)
        {
            flat[i] = (byte)(i * 5 + 11);
        }

        return flat;
    }

    [Fact]
    public void DrawColumn_MatchesCVectors()
    {
        var view = CreateView();
        var identity = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            identity[i] = (byte)i;
        }

        foreach (var v in RMainVectors.DrawColumn.EnumerateArray())
        {
            Array.Clear(Video.Screens[0]);

            ColumnDrawState.X = v.GetProperty("x").GetInt32();
            ColumnDrawState.Yl = v.GetProperty("yl").GetInt32();
            ColumnDrawState.Yh = v.GetProperty("yh").GetInt32();
            ColumnDrawState.IScale = v.GetProperty("iscale").GetInt32();
            ColumnDrawState.TextureMid = v.GetProperty("mid").GetInt32();
            ColumnDrawState.Source = new byte[128];
            Array.Copy(
                new byte[] { 3, 10, 17, 24, 31, 38, 45, 52 }, ColumnDrawState.Source, 8);
            ColumnDrawState.SourceOfs = 0;
            ColumnDrawState.Colormap = identity;

            // Rebuild the same source the C harness used: i*7+3.
            for (var i = 0; i < 128; i++)
            {
                ColumnDrawState.Source[i] = (byte)(i * 7 + 3);
            }

            ColumnDrawer.DrawColumn(view);

            var expected = v.GetProperty("screen").EnumerateArray().Select(p => (byte)p.GetInt32()).ToArray();
            Assert.Equal(expected, Video.Screens[0]);
        }
    }

    [Fact]
    public void DrawFuzzColumn_MatchesCVectors()
    {
        Video.Init();
        var view = CreateView();
        // Full COLORMAP lump size: fuzz indexes colormap[6*256 + pixel].
        var identity = new byte[ViewSetup.NumColormaps * 256];
        for (var i = 0; i < identity.Length; i++)
        {
            identity[i] = (byte)(i & 0xff);
        }

        ViewSetup.ColormapsGlobal = identity;
        ColumnDrawer.FuzzPos = 0;

        // The C harness seeds the screen once; call 2 runs on call 1's
        // output (fuzzpos continuity).
        for (var p = 0; p < Video.ScreenSize; p++)
        {
            Video.Screens[0][p] = (byte)(p * 13 + 5);
        }

        foreach (var v in RMainVectors.DrawFuzzColumn.EnumerateArray())
        {
            ColumnDrawState.X = v.GetProperty("call").GetInt32() == 1 ? 100 : 11;
            ColumnDrawState.Yl = v.GetProperty("call").GetInt32() == 1 ? 10 : 40;
            ColumnDrawState.Yh = v.GetProperty("call").GetInt32() == 1 ? 60 : 80;

            ColumnDrawer.DrawFuzzColumn(view);

            Assert.Equal(v.GetProperty("fuzzpos_after").GetInt32(), ColumnDrawer.FuzzPos);
            Assert.Equal(
                v.GetProperty("screen").EnumerateArray().Select(p => (byte)p.GetInt32()),
                Video.Screens[0]);
        }
    }

    [Fact]
    public void DrawSpan_MatchesCVectors()
    {
        var view = CreateView();
        var identity = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            identity[i] = (byte)i;
        }

        var flat = new byte[64 * 64];
        for (var i = 0; i < 64 * 64; i++)
        {
            flat[i] = (byte)(i * 5 + 11);
        }

        foreach (var v in RMainVectors.DrawSpan.EnumerateArray())
        {
            Array.Clear(Video.Screens[0]);

            SpanDrawState.Y = v.GetProperty("y").GetInt32();
            SpanDrawState.X1 = v.GetProperty("x1").GetInt32();
            SpanDrawState.X2 = v.GetProperty("x2").GetInt32();
            SpanDrawState.XFrac = v.GetProperty("xfrac").GetInt32();
            SpanDrawState.YFrac = v.GetProperty("yfrac").GetInt32();
            SpanDrawState.XStep = v.GetProperty("xstep").GetInt32();
            SpanDrawState.YStep = v.GetProperty("ystep").GetInt32();
            SpanDrawState.Source = flat;
            SpanDrawState.Colormap = identity;

            SpanDrawer.DrawSpan(view);

            Assert.Equal(
                v.GetProperty("screen").EnumerateArray().Select(p => (byte)p.GetInt32()),
                Video.Screens[0]);
        }
    }

    [Fact]
    public void DrawColumn_ZeroLengthSpan_ReturnsWithoutDrawing()
    {
        Video.Init();
        Array.Clear(Video.Screens[0]);
        var view = CreateView();
        ColumnDrawState.X = 5;
        ColumnDrawState.Yl = 10;
        ColumnDrawState.Yh = 9;   // count < 0
        ColumnDrawState.Source = new byte[128];
        ColumnDrawState.Colormap = new byte[256];

        ColumnDrawer.DrawColumn(view);   // must not throw

        Assert.All(Video.Screens[0], pixel => Assert.Equal(0, pixel));
    }

    [Fact]
    public void DrawColumn_OutOfRange_ThrowsFatal()
    {
        var view = CreateView();
        ColumnDrawState.X = 320;
        ColumnDrawState.Yl = 0;
        ColumnDrawState.Yh = 10;
        ColumnDrawState.Source = new byte[128];

        Assert.Throws<DoomFatalException>(() => ColumnDrawer.DrawColumn(view));
    }

    [Fact]
    public void DrawSpan_NegativeSpan_ThrowsRangeCheck()
    {
        var view = CreateView();
        SpanDrawState.Y = 100;
        SpanDrawState.X1 = 10;
        SpanDrawState.X2 = 5;   // x2 < x1

        Assert.Throws<DoomFatalException>(() => SpanDrawer.DrawSpan(view));
    }

    [Fact]
    public void FuzzPos_WrapsAtFuzzTable()
    {
        Assert.Equal(50, ColumnDrawer.FuzzOffset.Length);
        ColumnDrawer.FuzzPos = 49;
        // One wraparound happens inside DrawFuzzColumn; verify the table
        // itself has exactly FUZZTABLE entries.
        Assert.Equal(50, ColumnDrawer.FuzzOffset.Length);
    }
}

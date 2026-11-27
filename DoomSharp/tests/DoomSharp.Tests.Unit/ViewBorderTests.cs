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
//      Tests for Video.FillBackScreen / VideoErase / DrawViewBorder
//      (r_draw.c:726-875) with a synthetic border WAD.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;
using Xunit;

namespace DoomSharp.Tests.Unit;

[Collection("RendererSerial")]
public class ViewBorderTests : IDisposable
{
    private readonly string _tempDir;

    public ViewBorderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "doomsharp_bordertests", Guid.NewGuid().ToString("N"));
        Video.Init();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    /// <summary>
    /// Builds a WAD with the border flat (FLOOR7_2 / GRNROCK) and the
    /// eight brdr_* patches, then initializes a TextureManager.
    /// </summary>
    private (WadDirectory Wad, TextureManager Textures, ZoneMemory Zone) LoadBorderWad()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "bordertest.wad");

        var flat = new byte[64 * 64];
        for (var i = 0; i < flat.Length; i++)
        {
            flat[i] = (byte)(i & 0xff);
        }

        var lumps = new List<(string Name, byte[] Data)>
        {
            ("PPSTART", []),
            ("FLOOR7_2", flat),
            ("GRNROCK", flat),
            ("brdr_t", TextureWadBuilder.Patch(8, 8)),
            ("brdr_b", TextureWadBuilder.Patch(8, 8)),
            ("brdr_l", TextureWadBuilder.Patch(8, 8)),
            ("brdr_r", TextureWadBuilder.Patch(8, 8)),
            ("brdr_tl", TextureWadBuilder.Patch(8, 8)),
            ("brdr_tr", TextureWadBuilder.Patch(8, 8)),
            ("brdr_bl", TextureWadBuilder.Patch(8, 8)),
            ("brdr_br", TextureWadBuilder.Patch(8, 8)),
            ("PPEND", []),
            ("PNAMES", TextureWadBuilder.PNames(
                "brdr_t", "brdr_b", "brdr_l", "brdr_r",
                "brdr_tl", "brdr_tr", "brdr_bl", "brdr_br")),
            ("TEXTURE1", TextureWadBuilder.TextureX(("BORDER", 8, 8, [(0, 0, 0)]))),
            ("F_START", []),
            ("FLOOR7_2F", flat),
            ("F_END", []),
            ("COLORMAP", TextureWadBuilder.Colormap()),
        };
        TextureWadFixture.WriteWad(path, lumps);

        var wad = new WadDirectory();
        wad.InitMultipleFiles([path]);
        var zone = new ZoneMemory();
        var textures = new TextureManager();
        textures.InitTextures(wad, zone);
        textures.InitFlats(wad, zone);
        return (wad, textures, zone);
    }

    /// <summary>ViewSetup with a full-size identity colormap.</summary>
    private static ViewSetup NewView()
    {
        var view = new ViewSetup(new TextureManager());
        var colormaps = new byte[ViewSetup.NumColormaps * 256];
        view.Colormaps = colormaps;
        ViewSetup.ColormapsGlobal = colormaps;
        return view;
    }

    [Fact]
    public void FillBackScreen_FullScreenView_IsNoOp()
    {
        var (wad, textures, zone) = LoadBorderWad();
        using var _ = zone;
        var view = NewView();
        view.SetViewSize(11, 0);
        view.ExecuteSetViewSize();

        Array.Fill(Video.Screens[1], (byte)0x77);
        Video.FillBackScreen(view, textures, wad, commercial: false);

        // Full-screen view: nothing drawn — the back screen keeps the
        // 0x77 fill.
        Assert.All(Video.Screens[1], p => Assert.Equal(0x77, p));
    }

    [Fact]
    public void FillBackScreen_SmallView_FillsFlatAndBorder()
    {
        var (wad, textures, zone) = LoadBorderWad();
        using var _ = zone;
        var view = NewView();
        view.SetViewSize(9, 0);
        view.ExecuteSetViewSize();

        Array.Fill(Video.Screens[1], (byte)0x00);
        Video.FillBackScreen(view, textures, wad, commercial: false);

        // The flat fill covers rows 0..167 (168 = 200-32); the flat is
        // (i & 0xff) row-major, so row 0 col 0 = 0 and row 1 col 0 =
        // flat[64] = 64.
        Assert.Equal(0, Video.Screens[1][0]);
        Assert.Equal(64, Video.Screens[1][320]);
        // Last pixel of the flat area (row 167, col 319) comes from the
        // flat: flat[(167&63)*64 + (319&63)] & 0xff = 255.
        Assert.Equal((byte)(((167 & 63) * 64 + (319 & 63)) & 0xff),
                     Video.Screens[1][(168 * 320) - 1]);
    }

    [Fact]
    public void VideoErase_CopiesBackToDisplay()
    {
        Video.Screens[0].AsSpan().Fill(0);
        Video.Screens[1].AsSpan().Fill(0);
        Video.Screens[1][100] = 0xAB;
        Video.Screens[1][105] = 0xCD;

        Video.VideoErase(100, 6);

        Assert.Equal(0xCD, Video.Screens[0][105]);
        Assert.Equal(0, Video.Screens[0][106]);
    }

    [Fact]
    public void DrawViewBorder_FullScreenView_IsNoOp()
    {
        var view = NewView();
        view.SetViewSize(11, 0);
        view.ExecuteSetViewSize();

        Array.Fill(Video.Screens[0], (byte)0x11);
        Array.Fill(Video.Screens[1], (byte)0x22);
        Video.DrawViewBorder(view);

        // Full-screen view: display screen untouched.
        Assert.All(Video.Screens[0], p => Assert.Equal(0x11, p));
    }

    [Fact]
    public void DrawViewBorder_SmallView_CopiesBorderFromBackScreen()
    {
        var view = NewView();
        view.SetViewSize(9, 0);
        view.ExecuteSetViewSize();

        // Back screen: byte i & 0xff; display: 0.
        for (var i = 0; i < Video.ScreenSize; i++)
        {
            Video.Screens[1][i] = (byte)(i & 0xff);
            Video.Screens[0][i] = 0;
        }

        Video.DrawViewBorder(view);

        // The border region (everything outside the view window but
        // above the status bar) must now match the back screen.
        var viewHeight = view.ViewHeight;
        var top = ((Video.ScreenHeight - Video.SBarHeight) - viewHeight) / 2;
        var side = (Video.ScreenWidth - view.ScaledViewWidth) / 2;

        // Top band (first VideoErase covers 0 .. top*320+side-1).
        Assert.Equal((byte)(5 & 0xff), Video.Screens[0][5]);
        Assert.Equal((byte)((top * Video.ScreenWidth + side - 1) & 0xff),
                     Video.Screens[0][(top * Video.ScreenWidth) + side - 1]);
        // Bottom band (second VideoErase).
        var bottomOfs = ((viewHeight + top) * Video.ScreenWidth) - side;
        Assert.Equal((byte)((bottomOfs + 3) & 0xff), Video.Screens[0][bottomOfs + 3]);
        // Left side band of the first view row (wraparound copy i=1
        // copies right band of row top and left band of row top+1).
        var leftBand = (top + 1) * Video.ScreenWidth;
        Assert.Equal((byte)(leftBand & 0xff), Video.Screens[0][leftBand]);
        // A pixel inside the view window must NOT be copied.
        var inside = (top + 5) * Video.ScreenWidth + side + 5;
        Assert.Equal(0, Video.Screens[0][inside]);
    }
}

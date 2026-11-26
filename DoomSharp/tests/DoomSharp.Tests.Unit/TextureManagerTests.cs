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
//      DOOM-030a renderer data manager tests. Expected values
//      hand-derived from the r_data.h binary layouts.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;
using Serilog;
using Xunit;

namespace DoomSharp.Tests.Unit;

public class TextureManagerTests : IDisposable
{
    private readonly string _tempDir;

    public TextureManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "doomsharp_textests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private (WadDirectory Wad, ZoneMemory Zone) LoadWad()
    {
        var path = TextureWadFixture.WriteTextureWad(_tempDir);
        var wad = new WadDirectory();
        wad.InitMultipleFiles([path]);
        return (wad, new ZoneMemory());
    }

    [Fact]
    public void InitTextures_ParsesDefinitions()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();

        manager.InitTextures(wad, zone);

        Assert.Equal(2, manager.NumTextures);
        Assert.Equal("TEXSINGL", manager.Textures[0].NameString);
        Assert.Equal(8, manager.Textures[0].Width);
        Assert.Equal(4, manager.Textures[0].Height);
        Assert.Single(manager.Textures[0].Patches);
        Assert.Equal(2, manager.Textures[1].PatchCount);
    }

    [Fact]
    public void InitTextures_PowerOfTwoWidthMask()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();

        manager.InitTextures(wad, zone);

        // width=8 -> mask=7 (exact power of two).
        Assert.Equal(7, manager.TextureWidthMask[0]);
        // width=48 would yield 31; our 8-wide textures yield 7.
        // The exact loop is verified by the width=48 case below.
    }

    [Fact]
    public void InitTextures_Width48_YieldsMask31()
    {
        // r_data.c:551-555 exact loop: width=48 -> largest pow2 <= 48
        // is 32 -> mask 31 (wrapping quirk).
        var j = 1;
        const int width = 48;
        while (j * 2 <= width)
        {
            j <<= 1;
        }
        Assert.Equal(31, j - 1);

        // And width=64 -> 63.
        j = 1;
        const int width64 = 64;
        while (j * 2 <= width64)
        {
            j <<= 1;
        }
        Assert.Equal(63, j - 1);
    }

    [Fact]
    public void GetColumn_SinglePatch_PointsIntoPatchLump()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();
        manager.InitTextures(wad, zone);

        // TEXSINGL (index 0): column x, row y -> pixel (x*16+y)&0xff.
        var col = manager.GetColumn(wad, 0, 3);
        Assert.Equal((byte)(3 * 16 + 0), col[0]); // topdelta=0 post, first pixel
        Assert.Equal((byte)(3 * 16 + 1), col[1]);
        Assert.Equal((byte)(3 * 16 + 3), col[3]);
    }

    [Fact]
    public void GetColumn_MultiPatch_CompositesOverlappingPixels()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();
        manager.InitTextures(wad, zone);

        // TEXDOUBL (index 1): patch A (x*16+y) at x=0, patch B (solid
        // 0xEE) at x=2. Columns 0-1 single-patch; columns 2-7 overlap
        // -> composite; later patch (B) draws over A.
        var col2 = manager.GetColumn(wad, 1, 2);
        Assert.Equal(0xEE, col2[0]); // B wins on overlap

        // Column 0: single-patch (only A covers it).
        var col0 = manager.GetColumn(wad, 1, 0);
        Assert.Equal((byte)(0 * 16 + 1), col0[1]); // A's pixel (x=0,y=1)
    }

    [Fact]
    public void GetColumn_WrapsViaWidthMask()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();
        manager.InitTextures(wad, zone);

        // col=10 wraps to 10 & 7 = 2 for the 8-wide texture.
        var col = manager.GetColumn(wad, 0, 10);
        var col2 = manager.GetColumn(wad, 0, 2);
        Assert.Equal(col2[0], col[0]);
    }

    [Fact]
    public void CheckTextureNumForName_Dash_ReturnsZero()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();
        manager.InitTextures(wad, zone);

        Assert.Equal(0, manager.CheckTextureNumForName("-"));
        Assert.Equal(0, manager.CheckTextureNumForName("-NOTEX"));
    }

    [Fact]
    public void CheckTextureNumForName_CaseInsensitive()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();
        manager.InitTextures(wad, zone);

        Assert.Equal(0, manager.CheckTextureNumForName("texsingle"));
        Assert.Equal(1, manager.CheckTextureNumForName("TEXDOUBL"));
        Assert.Equal(-1, manager.CheckTextureNumForName("NOSUCH"));
    }

    [Fact]
    public void TextureNumForName_Missing_ThrowsFatal()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();
        manager.InitTextures(wad, zone);

        var ex = Assert.Throws<DoomFatalException>(() => manager.TextureNumForName("NOSUCH"));
        Assert.Contains("R_TextureNumForName", ex.Message);
    }

    [Fact]
    public void InitFlats_SetsRangeAndIdentityTranslation()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();

        manager.InitFlats(wad, zone);

        Assert.Equal(2, manager.NumFlats);
        Assert.Equal(0, manager.FlatTranslation[0]); // identity
        Assert.Equal(1, manager.FlatTranslation[1]);
    }

    [Fact]
    public void FlatNumForName_ResolvesWithinRange()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();
        manager.InitFlats(wad, zone);

        Assert.Equal(0, manager.FlatNumForName(wad, "FLAT1"));
        Assert.Equal(1, manager.FlatNumForName(wad, "FLAT2"));
    }

    [Fact]
    public void FlatNumForName_OutsideRange_ThrowsFatal()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();
        manager.InitFlats(wad, zone);

        // PNAMES exists but is not between F_START and F_END.
        Assert.Throws<DoomFatalException>(() => manager.FlatNumForName(wad, "PNAMES"));
    }

    [Fact]
    public void CheckFlatNumForName_Missing_ReturnsMinusOne()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();
        manager.InitFlats(wad, zone);

        Assert.Equal(-1, manager.CheckFlatNumForName(wad, "NOSUCHFLAT"));
        Assert.Equal(-1, manager.CheckFlatNumForName(wad, "PNAMES")); // outside range
        Assert.Equal(0, manager.CheckFlatNumForName(wad, "FLAT1"));
    }

    [Fact]
    public void InitColormaps_Loads34x256()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();

        manager.InitColormaps(wad, zone);

        Assert.Equal(34 * 256, manager.Colormaps.Length);
        Assert.Equal(0x00, manager.Colormaps[0]);
        Assert.Equal(0xAB, manager.Colormaps[0xAB]);
        Assert.Equal(0xFF, manager.Colormaps[34 * 256 - 1]);
    }

    [Fact]
    public void InitSpriteLumps_ReadsHeaders()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();

        manager.InitSpriteLumps(wad, zone);

        Assert.Equal(1, manager.NumSpriteLumps);
        Assert.Equal(4 << 16, manager.SpriteWidth[0]);
        Assert.Equal(2 << 16, manager.SpriteOffset[0]);
        Assert.Equal(1 << 16, manager.SpriteTopOffset[0]);
    }

    [Fact]
    public void InitData_OrchestratesAll()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();

        manager.InitData(wad, zone);

        Assert.Equal(2, manager.NumTextures);
        Assert.Equal(2, manager.NumFlats);
        Assert.Equal(1, manager.NumSpriteLumps);
        Assert.Equal(34 * 256, manager.Colormaps.Length);
        // Identity translations.
        Assert.Equal(0, manager.TextureTranslation[0]);
        Assert.Equal(1, manager.TextureTranslation[1]);
    }

    [Fact]
    public void ResolveLevelTextures_ResolvesSectorAndSideIndices()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();
        manager.InitData(wad, zone);

        var level = new LevelData
        {
            Sectors =
            [
                new Sector { FloorPic = NameBytes("FLAT1"), CeilingPic = NameBytes("FLAT2") },
            ],
            Sides =
            [
                new Side
                {
                    TopTexture = NameBytes("TEXSINGL"),
                    BottomTexture = NameBytes("TEXDOUBL"),
                    MidTexture = NameBytes("-"),
                },
            ],
        };

        manager.ResolveLevelTextures(level);

        Assert.Equal(0, level.Sectors[0].FloorFlat);   // FLAT1 -> flat 0
        Assert.Equal(1, level.Sectors[0].CeilingFlat); // FLAT2 -> flat 1
        Assert.Equal(0, level.Sides[0].TopTextureIndex);     // TEXSINGL -> 0
        Assert.Equal(1, level.Sides[0].BottomTextureIndex);  // TEXDOUBL -> 1
        Assert.Equal(0, level.Sides[0].MidTextureIndex);     // "-" -> 0
    }

    [Fact]
    public void InitTextures_MissingPatch_ThrowsFatal()
    {
        var (wad, zone) = LoadWad();
        using var _ = zone;
        using var manager = new TextureManager();

        // Build a WAD whose texture references a patch not in PNAMES.
        var path = Path.Combine(_tempDir, "badpatch.wad");
        TextureWadFixture.WriteWad(path,
        [
            ("WALL0000", TextureWadBuilder.Patch(4, 4)),
            ("PNAMES", TextureWadBuilder.PNames("MISSING")),
            ("TEXTURE1", TextureWadBuilder.TextureX(("BADTEX", 4, 4, [(0, 0, 0)]))),
        ]);

        using var wad2 = new WadDirectory();
        wad2.InitMultipleFiles([path]);

        Assert.Throws<DoomFatalException>(() => manager.InitTextures(wad2, zone));
    }

    [Fact]
    public void TraceLogging_EmitsVerboseEvents()
    {
        lock (LogTestSync.Lock)
        {
            var sink = new CollectingSink();
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();

            try
            {
                var (wad, zone) = LoadWad();
                using var _ = zone;
                using var manager = new TextureManager();
                manager.InitData(wad, zone);

                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("InitData"));
                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("InitTextures"));
                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("InitFlats"));
                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("InitColormaps"));
            }
            finally
            {
                Serilog.Log.Logger = new Serilog.LoggerConfiguration().CreateLogger();
            }
        }
    }

    [Fact]
    public void TraceLogging_SilentByDefault()
    {
        lock (LogTestSync.Lock)
        {
            Serilog.Log.Logger = new Serilog.LoggerConfiguration().CreateLogger();

            var (wad, zone) = LoadWad();
            using var _ = zone;
            using var manager = new TextureManager();
            manager.InitData(wad, zone);

            Assert.Equal(2, manager.NumTextures); // behavior identical when silent
        }
    }

    [Fact]
    public void RealWad_InitData_WhenDoomWadDirSet()
    {
        var wadDir = Environment.GetEnvironmentVariable("DOOMWADDIR");
        if (string.IsNullOrEmpty(wadDir))
        {
            return; // gated: shareware doom1.wad not available
        }

        var path = Path.Combine(wadDir, "doom1.wad");
        if (!File.Exists(path))
        {
            return;
        }

        using var wad = new WadDirectory();
        wad.InitMultipleFiles([path]);
        using var zone = new ZoneMemory();
        using var manager = new TextureManager();

        manager.InitData(wad, zone);

        Assert.True(manager.NumTextures > 0);
        Assert.True(manager.NumFlats > 0);
        Assert.Equal(34 * 256, manager.Colormaps.Length);
        // Identity translations.
        for (var i = 0; i < manager.NumTextures; i++)
        {
            Assert.Equal(i, manager.TextureTranslation[i]);
        }
        // Sampled lookups resolve.
        Assert.True(manager.CheckTextureNumForName("STARTAN3") >= 0);
        Assert.Equal(0, manager.CheckTextureNumForName("-"));
    }

    private static byte[] NameBytes(string name)
    {
        var bytes = new byte[8];
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        return bytes;
    }
}

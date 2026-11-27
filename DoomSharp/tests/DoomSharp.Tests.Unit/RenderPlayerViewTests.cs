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
//      End-to-end RenderPlayerView tests over the synthetic one-seg
//      level: pipeline wiring, frame state, and idempotent re-render.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;
using Xunit;

namespace DoomSharp.Tests.Unit;

[Collection("RendererSerial")]
public class RenderPlayerViewTests
{
    [Fact]
    public void RenderPlayerView_RunsPipeline_WithoutThrowing()
    {
        var level = BspRendererTests.BuildLevel();
        var (wad, textures, view, scene) = CreateScene(level);

        // No sprites: the pipeline must run end to end.
        scene.RenderPlayerView(0, 0, 48 << 16, AngleTables.Ang90, []);

        // The BSP traversal visited the subsector and stored a drawseg.
        Assert.Equal(1, scene.Segs.DsP);
        Assert.Equal(0, scene.Bsp.CurLine);
    }

    [Fact]
    public void RenderPlayerView_ClearsStateBetweenFrames()
    {
        var level = BspRendererTests.BuildLevel();
        var (wad, textures, view, scene) = CreateScene(level);

        scene.RenderPlayerView(0, 0, 48 << 16, AngleTables.Ang90, []);
        var firstFrameDrawsegs = scene.Segs.DsP;
        Assert.Equal(1, firstFrameDrawsegs);

        // Second frame from the same spot: state must be reset, not
        // accumulated.
        scene.RenderPlayerView(0, 0, 48 << 16, AngleTables.Ang90, []);
        Assert.Equal(1, scene.Segs.DsP);
        Assert.Equal(2, scene.Bsp.NewEnd);
    }

    [Fact]
    public void RenderPlayerView_WithSprite_ProjectsAndSorts()
    {
        var level = BspRendererTests.BuildLevel();
        var (wad, textures, view, scene) = CreateScene(level);

        var frame = new SpriteFrame { Rotate = false };
        frame.Lump[0] = 0;
        var spriteDefs = new SpriteDef[] { new() { NumFrames = 1, Frames = [frame] } };

        var thing = new MobjVisual
        {
            X = 0,
            Y = 256 << 16,
            Z = 0,
            Angle = 0,
            Sprite = 0,
            Frame = 0,
            Flags = 0,
        };
        scene.Things.Add(0, thing);

        scene.RenderPlayerView(0, 0, 48 << 16, AngleTables.Ang90, spriteDefs);

        Assert.Equal(1, scene.Sprites.VisSpriteP);
    }

    private static (WadDirectory Wad, TextureManager Textures, ViewSetup View, SceneRenderer Scene) CreateScene(LevelData level)
    {
        var textures = SceneFixture.Textures;
        var view = new ViewSetup(textures);
        var colormaps = new byte[ViewSetup.NumColormaps * 256];
        view.Colormaps = colormaps;
        ViewSetup.ColormapsGlobal = colormaps;
        view.SetViewSize(11, 0);
        view.ExecuteSetViewSize();

        var wad = SceneFixture.Wad;
        SpriteDefs.InitSprites(view, wad, ["SPR1"]);
        var scene = new SceneRenderer(wad, level, textures, view);
        return (wad, textures, view, scene);
    }
}

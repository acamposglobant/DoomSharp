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
//      Characterization tests for SpriteRenderer.ProjectSprite
//      (r_things.c), driven by vectors from tools/charact/rscenedump.c.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;
using Xunit;

namespace DoomSharp.Tests.Unit;

[Collection("RendererSerial")]
public class SpriteTests
{
    [Fact]
    public void ProjectSprite_MatchesCVectors()
    {
        // TextureManager with the fixture WAD: SPR1 is 4 wide, offset 2,
        // top 1 — matching the C harness's spritewidth/offset/topoffset.
        var level = BspRendererTests.BuildLevel();
        var (_, _, _, sprites, _, things) = BspRendererTests.CreateRenderers(level);

        // Sprite table like the C harness: one sprite, one frame,
        // non-rotating, lump 0.
        var frame = new SpriteFrame
        {
            Rotate = false,
        };
        frame.Lump[0] = 0;
        frame.Flip[0] = 0;
        var spriteDefs = new SpriteDef[]
        {
            new() { NumFrames = 1, Frames = [frame] },
        };

        // Same cases as the C harness's dump_projectsprite().
        var cases = new (int X, int Y, int Z, uint Angle)[]
        {
            (0, 256 << 16, 0, AngleTables.Ang90),
            (64 << 16, 256 << 16, 0, AngleTables.Ang90),
            (-200 << 16, 128 << 16, 0, AngleTables.Ang45),
        };

        var vectors = SceneVectors.ProjectSprite.EnumerateArray().ToList();
        Assert.Equal(cases.Length, vectors.Count);
        for (var i = 0; i < cases.Length; i++)
        {
            var v = vectors[i];
            sprites.ClearSprites();
            things.Clear();
            RendererMath.ViewX = 0;
            RendererMath.ViewY = 0;
            RendererMath.ViewZ = 41 << 16;
            RendererMath.ViewAngle = AngleTables.Ang90;
            RendererMath.ViewSin = AngleTables.FineSine[(int)(AngleTables.Ang90 >> AngleTables.AngleToFineShift)];
            RendererMath.ViewCos = AngleTables.FineCosine[(int)(AngleTables.Ang90 >> AngleTables.AngleToFineShift)];
            RendererMath.DetailShift = 0;
            RendererMath.ValidCount++;

            var thing = new MobjVisual
            {
                X = cases[i].X,
                Y = cases[i].Y,
                Z = cases[i].Z,
                Angle = cases[i].Angle,
                Sprite = 0,
                Frame = 0,
                Flags = 0,
            };

            things.Add(0, thing);
            sprites.AddSprites(0, spriteDefs);

            var expectedCount = v.GetProperty("count").GetInt32();
            Assert.Equal(expectedCount, sprites.VisSpriteP);
            if (expectedCount > 0)
            {
                var vis = sprites.VisSprites[0];
                Assert.Equal(v.GetProperty("x1").GetInt32(), vis.X1);
                Assert.Equal(v.GetProperty("x2").GetInt32(), vis.X2);
                Assert.Equal(v.GetProperty("scale").GetInt32(), vis.Scale);
                Assert.Equal(v.GetProperty("startfrac").GetInt32(), vis.StartFrac);
                Assert.Equal(v.GetProperty("xiscale").GetInt32(), vis.XIScale);
                Assert.Equal(v.GetProperty("texturemid").GetInt32(), vis.TextureMid);
                Assert.Equal(v.GetProperty("patch").GetInt32(), vis.Patch);
            }
        }
    }
}

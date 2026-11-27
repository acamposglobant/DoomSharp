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
//      Characterization tests for BspRenderer clip-segment handling
//      (r_bsp.c R_ClipSolidWallSegment / R_ClipPassWallSegment),
//      driven by vectors from tools/charact/rscenedump.c.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;
using Xunit;

namespace DoomSharp.Tests.Unit;

[Collection("RendererSerial")]
public class BspRendererTests
{
    /// <summary>
    /// Builds the same one-seg level as the C harness's build_level():
    /// a two-sided wall along +x at y=256, front floor 0 / ceiling 128,
    /// back floor 32 / ceiling 128, view at origin looking +y (ANG90).
    /// </summary>
    internal static LevelData BuildLevel()
    {
        var level = new LevelData
        {
            Vertexes = [new Vertex { X = -128 << 16, Y = 256 << 16 }, new Vertex { X = 128 << 16, Y = 256 << 16 }],
            Sectors =
            [
                // FloorFlat/CeilingFlat 0 mirrors the C harness's
                // calloc'd flattranslation (every flat maps to 0).
                new Sector { FloorHeight = 0, CeilingHeight = 128 << 16, LightLevel = 200, FloorFlat = 0, CeilingFlat = 0 },
                new Sector { FloorHeight = 32 << 16, CeilingHeight = 128 << 16, LightLevel = 200, FloorFlat = 0, CeilingFlat = 0 },
            ],
            // The C harness's texturetranslation is calloc'd (all zero),
            // so texturetranslation[side.*texture] == 0 for every tier:
            // the seg is untextured and RenderSegLoop only marks clips.
            Sides = [new Side { Sector = 0, TopTextureIndex = 0, BottomTextureIndex = 0, MidTextureIndex = 0 }],
            Lines =
            [
                new Line
                {
                    V1 = 0,
                    V2 = 1,
                    Dx = 256 << 16,
                    Dy = 0,
                    Sidenum = [0, 0],
                    FrontSector = 0,
                    BackSector = 1,
                },
            ],
            Segs =
            [
                new Seg { V1 = 0, V2 = 1, Angle = 0, LineDef = 0, Side = 0, FrontSector = 0, BackSector = 1 },
            ],
            Subsectors = [new Subsector { NumSegs = 1, FirstSeg = 0 }],
            Nodes = [],
        };
        return level;
    }

    /// <summary>Shared renderer graph wired like SceneRenderer.</summary>
    internal static (BspRenderer Bsp, SegRenderer Segs, VisplaneManager Planes, SpriteRenderer Sprites, ViewSetup View, SectorThings Things) CreateRenderers(LevelData level)
    {
        Video.Init();
        var view = new ViewSetup(new TextureManager());
        var colormaps = new byte[ViewSetup.NumColormaps * 256];
        view.Colormaps = colormaps;
        ViewSetup.ColormapsGlobal = colormaps;
        view.SetViewSize(11, 0);
        view.ExecuteSetViewSize();

        RendererMath.ViewX = 0;
        RendererMath.ViewY = 0;
        RendererMath.ViewZ = 48 << 16;
        RendererMath.ViewAngle = AngleTables.Ang90;
        RendererMath.ViewSin = AngleTables.FineSine[(int)(AngleTables.Ang90 >> AngleTables.AngleToFineShift)];
        RendererMath.ViewCos = AngleTables.FineCosine[(int)(AngleTables.Ang90 >> AngleTables.AngleToFineShift)];
        RendererMath.ExtraLight = 0;
        RendererMath.FixedColormap = null;
        RendererMath.ValidCount++;

        var planes = new VisplaneManager(view);
        var textures = SceneFixture.Textures;
        var wad = SceneFixture.Wad;
        var segs = new SegRenderer(view, planes, level, textures, wad);
        var things = new SectorThings(level);
        var sprites = new SpriteRenderer(view, segs, planes, textures, wad, things);
        var bsp = new BspRenderer(view, segs, planes, sprites, level);
        return (bsp, segs, planes, sprites, view, things);
    }

    [Fact]
    public void ClipSegments_MatchCVectors()
    {
        var level = BuildLevel();
        var (bsp, segs, planes, _, _, _) = CreateRenderers(level);

        // Same ops as the C harness's dump_clips().
        var ops = new (bool Solid, int First, int Last)[]
        {
            (true, 10, 20),
            (true, 40, 60),
            (false, 25, 35),
            (true, 15, 45),
            (false, 5, 12),
            (true, 0, 319),
            (false, 100, 120),
            (true, 200, 250),
            (false, 300, 310),
        };

        var vectors = SceneVectors.ClipSolid.EnumerateArray().ToList();
        Assert.Equal(ops.Length, vectors.Count);

        // The C harness clears once before the loop; drawsegs and
        // visplanes accumulate across ops.
        bsp.ClearClipSegs();
        segs.ClearDrawSegs();
        planes.ClearPlanes();
        bsp.CurLine = 0;
        segs.CurLine = 0;
        bsp.FrontSector = 0;
        bsp.BackSector = 1;
        segs.RwAngle1 = RendererMath.PointToAngle2(
            RendererMath.ViewX, RendererMath.ViewY,
            level.Vertexes[level.Segs[0].V1].X, level.Vertexes[level.Segs[0].V1].Y);
        planes.FloorPlane = planes.FindPlane(0, 1, 200, SkyRenderer.SkyFlatNum);
        planes.CeilingPlane = planes.FindPlane((128 << 16) - RendererMath.ViewZ, 1, 200, SkyRenderer.SkyFlatNum);

        for (var opIndex = 0; opIndex < ops.Length; opIndex++)
        {
            var op = vectors[opIndex];

            if (ops[opIndex].Solid)
            {
                bsp.ClipSolidWallSegment(ops[opIndex].First, ops[opIndex].Last);
            }
            else
            {
                bsp.ClipPassWallSegment(ops[opIndex].First, ops[opIndex].Last);
            }

            // solidsegs
            var expectedSegs = op.GetProperty("solidsegs").EnumerateArray().ToList();
            Assert.Equal(expectedSegs.Count, bsp.NewEnd);
            for (var i = 0; i < expectedSegs.Count; i++)
            {
                Assert.Equal(expectedSegs[i].GetProperty("first").GetInt32(), bsp.SolidSegs[i].First);
                Assert.Equal(expectedSegs[i].GetProperty("last").GetInt32(), bsp.SolidSegs[i].Last);
            }

            // drawsegs
            var expectedDrawsegs = op.GetProperty("drawsegs").EnumerateArray().ToList();
            Assert.Equal(expectedDrawsegs.Count, segs.DsP);
            for (var i = 0; i < expectedDrawsegs.Count; i++)
            {
                var ds = segs.DrawSegs[i];
                var e = expectedDrawsegs[i];
                Assert.Equal(e.GetProperty("x1").GetInt32(), ds.X1);
                Assert.Equal(e.GetProperty("x2").GetInt32(), ds.X2);
                Assert.Equal(e.GetProperty("scale1").GetInt32(), ds.Scale1);
                Assert.Equal(e.GetProperty("scale2").GetInt32(), ds.Scale2);
                Assert.Equal(e.GetProperty("scalestep").GetInt32(), ds.ScaleStep);
                Assert.Equal(e.GetProperty("silhouette").GetInt32(), ds.Silhouette);
                Assert.Equal(e.GetProperty("bsil").GetInt32(), ds.BsilHeight);
                Assert.Equal(e.GetProperty("tsil").GetInt32(), ds.TsilHeight);
            }
        }
    }
}

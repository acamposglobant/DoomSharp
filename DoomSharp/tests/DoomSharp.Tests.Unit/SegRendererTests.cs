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
//      Characterization tests for SegRenderer.StoreWallRange geometry
//      (r_segs.c), driven by vectors from tools/charact/rscenedump.c.
//
//-----------------------------------------------------------------------------

using DoomSharp.Data;
using DoomSharp.Rendering;
using Xunit;

namespace DoomSharp.Tests.Unit;

[Collection("RendererSerial")]
public class SegRendererTests
{
    [Fact]
    public void StoreWallRange_MatchesCVectors()
    {
        var level = BspRendererTests.BuildLevel();
        var (bsp, segs, planes, _, _, _) = BspRendererTests.CreateRenderers(level);

        // Same ranges as the C harness's dump_storewallrange().
        var ranges = new (int Start, int Stop)[]
        {
            (100, 140),
            (50, 90),
            (150, 151),
        };

        var vectors = SceneVectors.StoreWallRange.EnumerateArray().ToList();
        Assert.Equal(ranges.Length, vectors.Count);
        for (var i = 0; i < ranges.Length; i++)
        {
            var v = vectors[i];
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

            segs.StoreWallRange(ranges[i].Start, ranges[i].Stop);

            var ds = segs.DrawSegs[0];
            Assert.Equal(v.GetProperty("scale1").GetInt32(), ds.Scale1);
            Assert.Equal(v.GetProperty("scale2").GetInt32(), ds.Scale2);
            Assert.Equal(v.GetProperty("scalestep").GetInt32(), ds.ScaleStep);
            Assert.Equal(v.GetProperty("silhouette").GetInt32(), ds.Silhouette);
            Assert.Equal(v.GetProperty("bsil").GetInt32(), ds.BsilHeight);
            Assert.Equal(v.GetProperty("tsil").GetInt32(), ds.TsilHeight);

            // floorclip / ceilingclip written by RenderSegLoop
            var start = v.GetProperty("start").GetInt32();
            var stop = v.GetProperty("stop").GetInt32();
            var expectedFloor = v.GetProperty("floorclip").EnumerateArray().Select(p => (short)p.GetInt32()).ToArray();
            var expectedCeiling = v.GetProperty("ceilingclip").EnumerateArray().Select(p => (short)p.GetInt32()).ToArray();
            for (var x = start; x <= stop; x++)
            {
                Assert.Equal(expectedFloor[x - start], planes.FloorClip[x]);
                Assert.Equal(expectedCeiling[x - start], planes.CeilingClip[x]);
            }
        }
    }
}

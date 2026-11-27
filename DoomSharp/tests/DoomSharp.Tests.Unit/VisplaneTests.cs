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
//      Characterization tests for VisplaneManager FindPlane /
//      CheckPlane (r_plane.c), driven by vectors from
//      tools/charact/rscenedump.c.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Rendering;
using Xunit;

namespace DoomSharp.Tests.Unit;

[Collection("RendererSerial")]
public class VisplaneTests
{
    [Fact]
    public void FindPlane_MatchesCVectors()
    {
        var view = new ViewSetup(new TextureManager());
        var colormaps = new byte[ViewSetup.NumColormaps * 256];
        view.Colormaps = colormaps;
        ViewSetup.ColormapsGlobal = colormaps;
        view.SetViewSize(11, 0);
        view.ExecuteSetViewSize();
        RendererMath.ViewAngle = AngleTables.Ang90;

        var planes = new VisplaneManager(view);
        planes.ClearPlanes();

        // Same inputs as the C harness's dump_planes().
        var finds = new (int Height, int PicNum, int LightLevel)[]
        {
            (64 << 16, 1, 160),
            (64 << 16, 1, 160),     // duplicate -> reuses plane 0
            (128 << 16, 1, 160),
            (0, 42, 0),             // sky plane (picnum == skyflatnum)
            (96 << 16, 1, 160),
        };

        var vectors = SceneVectors.Planes.EnumerateArray().ToList();
        Assert.Equal(finds.Length, vectors.Count);
        for (var i = 0; i < finds.Length; i++)
        {
            var v = vectors[i];
            var index = planes.FindPlane(finds[i].Height, finds[i].PicNum, finds[i].LightLevel, 42);
            Assert.Equal(v.GetProperty("index").GetInt32(), index);
            var pl = planes.Visplanes[index];
            Assert.Equal(v.GetProperty("minx").GetInt32(), pl.MinX);
            Assert.Equal(v.GetProperty("maxx").GetInt32(), pl.MaxX);
        }
    }

    [Fact]
    public void CheckPlane_SplitAndExtend_MatchCVectors()
    {
        var view = new ViewSetup(new TextureManager());
        var colormaps = new byte[ViewSetup.NumColormaps * 256];
        view.Colormaps = colormaps;
        ViewSetup.ColormapsGlobal = colormaps;
        view.SetViewSize(11, 0);
        view.ExecuteSetViewSize();
        RendererMath.ViewAngle = AngleTables.Ang90;

        var planes = new VisplaneManager(view);
        planes.ClearPlanes();

        // Set up visplane[0] like the C harness: minx=10, maxx=20, top=0xff
        var pl = planes.Visplanes[0];
        planes.LastVisplane = 1;
        pl.MinX = 10;
        pl.MaxX = 20;
        Array.Fill(pl.Top, (byte)0xff);

        // Overlapping range -> split (new plane)
        var split = planes.CheckPlane(0, 15, 25);
        Assert.Equal(SceneVectors.CheckPlaneSplit.GetProperty("index").GetInt32(), split);
        Assert.Equal(SceneVectors.CheckPlaneSplit.GetProperty("minx").GetInt32(), planes.Visplanes[split].MinX);
        Assert.Equal(SceneVectors.CheckPlaneSplit.GetProperty("maxx").GetInt32(), planes.Visplanes[split].MaxX);

        // Free range -> extend in place
        var extend = planes.CheckPlane(0, 30, 40);
        Assert.Equal(SceneVectors.CheckPlaneExtend.GetProperty("index").GetInt32(), extend);
        Assert.Equal(SceneVectors.CheckPlaneExtend.GetProperty("minx").GetInt32(), planes.Visplanes[extend].MinX);
        Assert.Equal(SceneVectors.CheckPlaneExtend.GetProperty("maxx").GetInt32(), planes.Visplanes[extend].MaxX);
    }
}

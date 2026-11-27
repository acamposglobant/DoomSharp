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
//      Sky rendering state tests (r_sky.c): InitSkyMap and the
//      constants used by the plane drawer's sky branch.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Rendering;
using Xunit;

namespace DoomSharp.Tests.Unit;

public class SkyTests
{
    [Fact]
    public void InitSkyMap_ResetsTextureMid()
    {
        SkyRenderer.SkyTextureMid = 12345;
        SkyRenderer.InitSkyMap();
        Assert.Equal(100 * FixedPoint.FracUnit, SkyRenderer.SkyTextureMid);
    }

    [Fact]
    public void SkyConstants_MatchLegacyValues()
    {
        Assert.Equal("F_SKY1", SkyRenderer.SkyFlatName);
        Assert.Equal(22, SkyRenderer.AngleToSkyShift);
    }
}

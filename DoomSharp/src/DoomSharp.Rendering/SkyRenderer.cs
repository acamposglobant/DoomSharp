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
//      Sky rendering state.
//      Migration of Legacy/linuxdoom-1.10/r_sky.c: skyflatnum,
//      skytexture, skytexturemid, R_InitSkyMap.
//      See docs/adr/ADR-004 for tracing; DOOM-030c plan for the
//      documented deviations.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using Serilog;

namespace DoomSharp.Rendering;

/// <summary>
/// Sky mapping (r_sky.c): the sky flat number, the sky texture and
/// its vertical anchor. skyflatnum/skytexture are resolved by the
/// level setup caller (R_FlatNumForName("F_SKY1") /
/// R_TextureNumForName("SKY1")).
/// </summary>
public static class SkyRenderer
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(SkyRenderer));

    /// <summary>SKYFLATNAME (r_sky.h:32).</summary>
    public const string SkyFlatName = "F_SKY1";

    /// <summary>ANGLETOSKYSHIFT (r_sky.h:35).</summary>
    public const int AngleToSkyShift = 22;

    /// <summary>skyflatnum (r_sky.c:47) — set by the level setup.</summary>
    public static int SkyFlatNum;

    /// <summary>skytexture (r_sky.c:48) — set by the level setup.</summary>
    public static int SkyTexture;

    /// <summary>skytexturemid (r_sky.c:49).</summary>
    public static int SkyTextureMid = 100 * FixedPoint.FracUnit;

    /// <summary>
    /// R_InitSkyMap (r_sky.c:57-61): called whenever the view size
    /// changes. skyflatnum is resolved by the level setup (the C has
    /// the R_FlatNumForName call commented out).
    /// </summary>
    public static void InitSkyMap()
    {
        SkyTextureMid = 100 * FixedPoint.FracUnit;   // r_sky.c:60
        Log.Verbose("InitSkyMap() skytexturemid={Mid}", SkyTextureMid);
    }
}

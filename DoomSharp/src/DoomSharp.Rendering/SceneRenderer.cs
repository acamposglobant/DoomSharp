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
//      Top-level scene rendering orchestration.
//      Migration of Legacy/linuxdoom-1.10/r_main.c R_RenderPlayerView
//      and the per-frame pipeline wiring of R_InitData/R_Init.
//      See docs/adr/ADR-004 for tracing; DOOM-030c plan for the
//      documented deviations.
//
//-----------------------------------------------------------------------------

using DoomSharp.Data;
using Serilog;
using Serilog.Events;

namespace DoomSharp.Rendering;

/// <summary>
/// Owns the renderer pipeline (BSP, segs, visplanes, sprites, sky)
/// and runs R_RenderPlayerView (r_main.c:870-898).
/// </summary>
public sealed class SceneRenderer
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(SceneRenderer));

    public ViewSetup View { get; }
    public TextureManager Textures { get; }
    public VisplaneManager Planes { get; }
    public SegRenderer Segs { get; }
    public BspRenderer Bsp { get; }
    public SpriteRenderer Sprites { get; }
    public SectorThings Things { get; }

    private readonly WadDirectory _wad;
    private readonly LevelData _level;

    public SceneRenderer(WadDirectory wad, LevelData level, TextureManager textures, ViewSetup view)
    {
        _wad = wad;
        _level = level;
        Textures = textures;
        View = view;
        Things = new SectorThings(level);
        Planes = new VisplaneManager(view);
        Segs = new SegRenderer(view, Planes, level, textures, wad);
        Sprites = new SpriteRenderer(view, Segs, Planes, textures, wad, Things);
        Bsp = new BspRenderer(view, Segs, Planes, Sprites, level);

        Log.Verbose("SceneRenderer({Sectors} sectors, {Segs} segs)", level.Sectors.Length, level.Segs.Length);
    }

    /// <summary>
    /// R_RenderPlayerView (r_main.c:870-898): SetupFrame, clear
    /// buffers, traverse the BSP, draw planes, draw masked things.
    /// NetUpdate calls are the platform boundary's concern (no-ops
    /// here, documented deviation).
    /// </summary>
    public void RenderPlayerView(int viewX, int viewY, int viewZ, uint viewAngle, SpriteDef[] sprites)
    {
        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("RenderPlayerView(x={ViewX} y={ViewY} z={ViewZ} angle={Angle})",
                viewX, viewY, viewZ, viewAngle);
        }

        // R_SetupFrame (r_main.c:795-863)
        View.SetupFrame(viewX, viewY, viewZ, viewAngle, 0, 0);

        // Clear buffers (r_main.c:874-878).
        Bsp.ClearClipSegs();
        Segs.ClearDrawSegs();
        Planes.ClearPlanes();
        Sprites.ClearSprites();

        // The head node is the last node output (r_main.c:884).
        Bsp.RenderBSPNode(_level.Nodes.Length - 1);

        Planes.DrawPlanes(
            SkyRenderer.SkyFlatNum,
            SkyRenderer.SkyTexture,
            SkyRenderer.SkyTextureMid,
            _wad,
            Textures);

        Sprites.DrawMasked(sprites);
    }
}

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
//      Sprite (thing) rendering.
//      Migration of Legacy/linuxdoom-1.10/r_things.c: R_DrawMaskedColumn
//      (lives in SegRenderer), R_DrawVisSprite, R_ProjectSprite,
//      R_AddSprites, R_SortVisSprites, R_DrawSprite, R_DrawMasked.
//      R_DrawPlayerSprites / R_DrawPSprite are deferred to DOOM-021+.
//      See docs/adr/ADR-004 for tracing; DOOM-030c plan for the
//      documented deviations.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using Serilog;
using Serilog.Events;

namespace DoomSharp.Rendering;

/// <summary>vissprite_t (r_defs.h:375-409).</summary>
public sealed class VisSprite
{
    public int X1;
    public int X2;
    public int Gx;              // fixed — for line side calculation
    public int Gy;
    public int Gz;              // global bottom for silhouette clipping
    public int Gzt;             // global top
    public int StartFrac;       // horizontal position of x1
    public int Scale;           // fixed
    public int XIScale;         // negative if flipped
    public int TextureMid;
    public int Patch;
    public byte[]? Colormap;    // null = shadow draw
    public int MobjFlags;
}

/// <summary>
/// Sprite rendering (r_things.c): vissprite projection, sorting and
/// masked drawing, plus the drawseg-clipping pass of R_DrawSprite.
/// </summary>
public sealed class SpriteRenderer
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(SpriteRenderer));

    /// <summary>MAXVISSPRITES (r_things.h:31).</summary>
    public const int MaxVisSprites = 128;

    /// <summary>MINZ (r_things.c:46).</summary>
    public const int MinZ = FixedPoint.FracUnit * 4;

    /// <summary>BASEYCENTER (r_things.c:47).</summary>
    public const int BaseYCenter = 100;

    /// <summary>vissprites[MAXVISSPRITES] (r_things.c:40).</summary>
    public readonly VisSprite[] VisSprites = new VisSprite[MaxVisSprites];

    /// <summary>vissprite_p (r_things.c:41) — one past the last vissprite.</summary>
    public int VisSpriteP;

    /// <summary>overflowsprite (r_things.c:42) — reused slot on overflow.</summary>
    public readonly VisSprite OverflowSprite = new();

    /// <summary>vsprsortedhead order (r_things.c:812) — sorted indices.</summary>
    public readonly int[] SortedOrder = new int[MaxVisSprites];
    public int SortedCount;

    /// <summary>spritelights (r_things.c:76).</summary>
    public byte[][] SpriteLights = [];

    private readonly ViewSetup _view;
    private readonly SegRenderer _segs;
    private readonly VisplaneManager _planes;
    private readonly TextureManager _textures;
    private readonly WadDirectory _wad;
    private readonly SectorThings _things;

    /// <summary>
    /// Two scratch clip buffers reused by every DrawSprite call
    /// (DOOM-030c refinement: per-sprite rendering stays
    /// allocation-free; the C allocates clipbot/cliptop on the stack).
    /// </summary>
    private readonly short[] _clipBot = new short[Video.ScreenWidth];
    private readonly short[] _clipTop = new short[Video.ScreenWidth];

    public SpriteRenderer(ViewSetup view, SegRenderer segs, VisplaneManager planes, TextureManager textures, WadDirectory wad, SectorThings things)
    {
        _view = view;
        _segs = segs;
        _planes = planes;
        _textures = textures;
        _wad = wad;
        _things = things;

        for (var i = 0; i < MaxVisSprites; i++)
        {
            VisSprites[i] = new VisSprite();
        }

        Log.Verbose("SpriteRenderer()");
    }

    /// <summary>R_ClearSprites (r_things.c:415-419).</summary>
    public void ClearSprites()
    {
        VisSpriteP = 0;
    }

    /// <summary>
    /// R_PointOnSegSide (r_main.c:213-247) against a drawseg's curline
    /// (r_things.c:899). Returns true when the point is on the back
    /// side of the seg.
    /// </summary>
    private bool PointOnSegSideOf(int x, int y, int segIndex)
    {
        var seg = _things.Level.Segs[segIndex];
        var v1 = _things.Level.Vertexes[seg.V1];
        var v2 = _things.Level.Vertexes[seg.V2];
        return RendererMath.PointOnSegSide(x, y, v1.X, v1.Y, v2.X, v2.Y) != 0;
    }

    /// <summary>R_NewVisSprite (r_things.c:325-336): overflow reuses the last slot.</summary>
    private VisSprite NewVisSprite()
    {
        if (VisSpriteP == MaxVisSprites)
        {
            return OverflowSprite;
        }

        return VisSprites[VisSpriteP++];
    }

    // ------------------------------------------------------------------
    // R_DrawVisSprite (r_things.c:394-451)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_DrawVisSprite (r_things.c:394-451): rasterize one vissprite
    /// over x1..x2 using the masked column walker.
    /// </summary>
    public void DrawVisSprite(VisSprite vis, int x1, int x2)
    {
        var patch = _wad.CacheLumpNum(vis.Patch + _textures.FirstSpriteLump, PurgeTags.Cache);
        var patchDef = new Patch(patch);

        ColumnDrawState.Colormap = vis.Colormap!;

        var useFuzz = false;
        var useTranslation = false;
        if (vis.Colormap == null)
        {
            // NULL colormap = shadow draw (r_things.c:412-415)
            useFuzz = true;
        }
        else if ((vis.MobjFlags & MobjFlags.Translation) != 0)
        {
            useTranslation = true;
        }

        ColumnDrawState.IScale = Math.Abs(vis.XIScale) >> RendererMath.DetailShift;
        ColumnDrawState.TextureMid = vis.TextureMid;
        var frac = vis.StartFrac;
        _segs.SprScale = vis.Scale;
        _segs.SprTopScreen = _view.CenterYFrac - FixedPoint.FixedMul(ColumnDrawState.TextureMid, _segs.SprScale);

        for (var dcX = vis.X1; dcX <= vis.X2; dcX++, frac += vis.XIScale)
        {
            var textureColumn = frac >> FixedPoint.FracBits;

            if (textureColumn < 0 || textureColumn >= patchDef.Width)
            {
                throw new DoomFatalException("R_DrawSpriteRange: bad texturecolumn");
            }

            var columnOfs = patchDef.ColumnOfs(textureColumn);
            ColumnDrawState.X = dcX;

            if (useFuzz)
            {
                // The fuzz path walks posts itself (colfunc = fuzzcolfunc).
                DrawMaskedColumnFuzz(patch, columnOfs);
            }
            else
            {
                if (useTranslation)
                {
                    ColumnDrawState.Translation = TranslationTablesFor(vis.MobjFlags);
                }

                _segs.DrawMaskedColumn(patch, columnOfs, vis.Patch);
            }
        }

        ColumnDrawState.Translation = [];
    }

    /// <summary>
    /// translationtables - 256 + ((flags & MF_TRANSLATION) >> (MF_TRANSSHIFT-8))
    /// (r_things.c:417-419). The table array is owned by ViewSetup.
    /// </summary>
    private byte[] TranslationTablesFor(int mobjFlags)
    {
        var index = ((mobjFlags & MobjFlags.Translation) >> (MobjFlags.TransShift - 8)) - 256;
        var all = _view.TranslationTables;
        if (all.Length == 0 || index < 0)
        {
            return [];
        }

        var start = index;
        if (start + 256 > all.Length)
        {
            return [];
        }

        var table = new byte[256];
        Array.Copy(all, start, table, 0, 256);
        return table;
    }

    /// <summary>
    /// Fuzz variant of the masked column walk: identical post walk but
    /// the drawer is R_DrawFuzzColumn (colfunc = fuzzcolfunc).
    /// </summary>
    private void DrawMaskedColumnFuzz(byte[] columnData, int columnOfs)
    {
        var baseTextureMid = _segs.DcTextureMid;
        var topDelta = columnData[columnOfs];
        var postOfs = columnOfs;

        while (topDelta != 0xff)
        {
            var length = columnData[postOfs + 1];
            var topScreen = _segs.SprTopScreen + _segs.SprScale * topDelta;
            var bottomScreen = topScreen + _segs.SprScale * length;

            ColumnDrawState.Yl = (topScreen + FixedPoint.FracUnit - 1) >> FixedPoint.FracBits;
            ColumnDrawState.Yh = (bottomScreen - 1) >> FixedPoint.FracBits;

            if (ColumnDrawState.Yh >= _segs.ReadFloorClip(ColumnDrawState.X))
            {
                ColumnDrawState.Yh = _segs.ReadFloorClip(ColumnDrawState.X) - 1;
            }

            if (ColumnDrawState.Yl <= _segs.ReadCeilingClip(ColumnDrawState.X))
            {
                ColumnDrawState.Yl = _segs.ReadCeilingClip(ColumnDrawState.X) + 1;
            }

            if (ColumnDrawState.Yl <= ColumnDrawState.Yh)
            {
                ColumnDrawState.Source = columnData;
                ColumnDrawState.SourceOfs = postOfs + 3;
                _segs.DcTextureMid = baseTextureMid - (topDelta << FixedPoint.FracBits);
                ColumnDrawState.TextureMid = _segs.DcTextureMid;

                ColumnDrawer.DrawFuzzColumn(_view);
            }

            postOfs += length + 4;
            topDelta = columnData[postOfs];
        }

        _segs.DcTextureMid = baseTextureMid;
    }

    // ------------------------------------------------------------------
    // R_ProjectSprite (r_things.c:452-604)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_ProjectSprite (r_things.c:452-604): generates a vissprite for
    /// a thing if it might be visible.
    /// </summary>
    public void ProjectSprite(MobjVisual thing, SpriteDef[] sprites)
    {
        // transform the origin point (r_things.c:476-497)
        var trX = thing.X - RendererMath.ViewX;
        var trY = thing.Y - RendererMath.ViewY;

        var gxt = FixedPoint.FixedMul(trX, RendererMath.ViewCos);
        var gyt = -FixedPoint.FixedMul(trY, RendererMath.ViewSin);

        var tz = gxt - gyt;

        // thing is behind view plane?
        if (tz < MinZ)
        {
            return;
        }

        var xscale = FixedPoint.FixedDiv(RendererMath.Projection, tz);

        gxt = -FixedPoint.FixedMul(trX, RendererMath.ViewSin);
        gyt = FixedPoint.FixedMul(trY, RendererMath.ViewCos);
        var tx = -(gyt + gxt);

        // too far off the side?
        if (Math.Abs(tx) > (tz << 2))
        {
            return;
        }

        if ((uint)thing.Sprite >= (uint)sprites.Length)
        {
            throw new DoomFatalException($"R_ProjectSprite: invalid sprite number {thing.Sprite}");
        }

        var sprdef = sprites[thing.Sprite];
        if ((thing.Frame & ThingFrame.FrameMask) >= sprdef.NumFrames)
        {
            throw new DoomFatalException(
                $"R_ProjectSprite: invalid sprite frame {thing.Sprite} : {thing.Frame}");
        }

        var sprframe = sprdef.Frames[thing.Frame & ThingFrame.FrameMask];

        int lump;
        bool flip;
        if (sprframe.Rotate)
        {
            // choose a different rotation based on player view
            // (r_things.c:530-535)
            var ang = RendererMath.PointToAngle(thing.X, thing.Y);
            var rot = (int)((ang - thing.Angle + (uint)(AngleTables.Ang45 / 2) * 9) >> 29);
            lump = sprframe.Lump[rot];
            flip = sprframe.Flip[rot] != 0;
        }
        else
        {
            // use single rotation for all views (r_things.c:536-540)
            lump = sprframe.Lump[0];
            flip = sprframe.Flip[0] != 0;
        }

        // calculate edges of the shape (r_things.c:543-563)
        tx -= _textures.SpriteOffset[lump];
        var x1 = (_view.CenterXFrac + FixedPoint.FixedMul(tx, xscale)) >> FixedPoint.FracBits;

        // off the right side?
        if (x1 > _view.ViewWidth)
        {
            return;
        }

        tx += _textures.SpriteWidth[lump];
        var x2 = ((_view.CenterXFrac + FixedPoint.FixedMul(tx, xscale)) >> FixedPoint.FracBits) - 1;

        // off the left side
        if (x2 < 0)
        {
            return;
        }

        // store information in a vissprite (r_things.c:566-601)
        var vis = NewVisSprite();
        vis.MobjFlags = thing.Flags;
        vis.Scale = xscale << RendererMath.DetailShift;
        vis.Gx = thing.X;
        vis.Gy = thing.Y;
        vis.Gz = thing.Z;
        vis.Gzt = thing.Z + _textures.SpriteTopOffset[lump];
        vis.TextureMid = vis.Gzt - RendererMath.ViewZ;
        vis.X1 = x1 < 0 ? 0 : x1;
        vis.X2 = x2 >= _view.ViewWidth ? _view.ViewWidth - 1 : x2;
        var iscale = FixedPoint.FixedDiv(FixedPoint.FracUnit, xscale);

        if (flip)
        {
            vis.StartFrac = _textures.SpriteWidth[lump] - 1;
            vis.XIScale = -iscale;
        }
        else
        {
            vis.StartFrac = 0;
            vis.XIScale = iscale;
        }

        if (vis.X1 > x1)
        {
            vis.StartFrac += vis.XIScale * (vis.X1 - x1);
        }

        vis.Patch = lump;

        // get light level (r_things.c:576-601)
        if ((thing.Flags & MobjFlags.Shadow) != 0)
        {
            vis.Colormap = null;                        // shadow draw
        }
        else if (RendererMath.FixedColormap != null)
        {
            vis.Colormap = RendererMath.FixedColormap;  // fixed map
        }
        else if ((thing.Frame & ThingFrame.FullBright) != 0)
        {
            vis.Colormap = ViewSetup.ColormapsGlobal;   // full bright
        }
        else
        {
            // diminished light
            var index = (uint)xscale >> (ViewSetup.LightScaleShift - RendererMath.DetailShift);
            if (index >= ViewSetup.MaxLightScale)
            {
                index = ViewSetup.MaxLightScale - 1;
            }

            vis.Colormap = SpriteLights[index];
        }
    }

    // ------------------------------------------------------------------
    // R_AddSprites (r_things.c:607-633)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_AddSprites (r_things.c:607-633): during BSP traversal, adds
    /// sprites by sector. The validcount check uses the SectorThings
    /// bridge (sec->validcount in the C).
    /// </summary>
    public void AddSprites(int sectorIndex, SpriteDef[] sprites)
    {
        // BSP is traversed by subsector. A sector might have been
        // split into several subsectors during BSP building. Thus we
        // check whether it's already added (r_things.c:613-619).
        if (_things.IsValidated(sectorIndex, RendererMath.ValidCount))
        {
            return;
        }

        _things.MarkValidated(sectorIndex, RendererMath.ValidCount);

        var sector = _things.Level.Sectors[sectorIndex];
        var lightnum = (sector.LightLevel >> ViewSetup.LightSegShift) + RendererMath.ExtraLight;

        if (lightnum < 0)
        {
            SpriteLights = _view.ScaleLight[0];
        }
        else if (lightnum >= ViewSetup.LightLevels)
        {
            SpriteLights = _view.ScaleLight[ViewSetup.LightLevels - 1];
        }
        else
        {
            SpriteLights = _view.ScaleLight[lightnum];
        }

        // Handle all things in sector (r_things.c:630-632).
        foreach (var thing in _things.ForSector(sectorIndex))
        {
            ProjectSprite(thing, sprites);
        }
    }

    // ------------------------------------------------------------------
    // R_SortVisSprites (r_things.c:812-860)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_SortVisSprites (r_things.c:812-860): painter's sort by scale
    /// (smallest scale = farthest drawn first). The C uses an
    /// intrusive doubly-linked list; here an index array with the same
    /// selection order (documented deviation).
    /// </draw>
    /// </summary>
    public void SortVisSprites()
    {
        var count = VisSpriteP;
        SortedCount = 0;

        if (count == 0)
        {
            return;
        }

        var taken = new bool[count];
        for (var i = 0; i < count; i++)
        {
            var best = -1;
            var bestScale = int.MaxValue;
            for (var ds = 0; ds < count; ds++)
            {
                if (!taken[ds] && VisSprites[ds].Scale < bestScale)
                {
                    bestScale = VisSprites[ds].Scale;
                    best = ds;
                }
            }

            taken[best] = true;
            SortedOrder[SortedCount++] = best;
        }
    }

    // ------------------------------------------------------------------
    // R_DrawSprite (r_things.c:864-975)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_DrawSprite (r_things.c:864-975): clip a vissprite against the
    /// drawsegs' silhouettes and draw it. Uses the two reusable
    /// scratch buffers (DOOM-030c refinement) instead of the C's
    /// stack arrays.
    /// </summary>
    public void DrawSprite(VisSprite spr)
    {
        for (var x = spr.X1; x <= spr.X2; x++)
        {
            _clipBot[x] = -2;
            _clipTop[x] = -2;
        }

        // Scan drawsegs from end to start for obscuring segs.
        // The first drawseg that has a greater scale is the clip seg
        // (r_things.c:878-950).
        for (var dsIndex = _segs.DsP - 1; dsIndex >= 0; dsIndex--)
        {
            var ds = _segs.DrawSegs[dsIndex];

            // determine if the drawseg obscures the sprite
            if (ds.X1 > spr.X2
                || ds.X2 < spr.X1
                || (ds.Silhouette == 0 && ds.MaskedTextureCol == -1))
            {
                // does not cover sprite
                continue;
            }

            var r1 = ds.X1 < spr.X1 ? spr.X1 : ds.X1;
            var r2 = ds.X2 > spr.X2 ? spr.X2 : ds.X2;

            int lowscale;
            int scale;
            if (ds.Scale1 > ds.Scale2)
            {
                lowscale = ds.Scale2;
                scale = ds.Scale1;
            }
            else
            {
                lowscale = ds.Scale1;
                scale = ds.Scale2;
            }

            if (scale < spr.Scale
                || (lowscale < spr.Scale
                    && !PointOnSegSideOf(spr.Gx, spr.Gy, ds.CurLine)))
            {
                // masked mid texture?
                if (ds.MaskedTextureCol != -1)
                {
                    _segs.RenderMaskedSegRange(ds, r1, r2);
                }

                // seg is behind sprite
                continue;
            }

            // clip this piece of the sprite (r_things.c:916-950)
            var silhouette = ds.Silhouette;

            if (spr.Gz >= ds.BsilHeight)
            {
                silhouette &= ~1;       // ~SIL_BOTTOM
            }

            if (spr.Gzt <= ds.TsilHeight)
            {
                silhouette &= ~2;       // ~SIL_TOP
            }

            if (silhouette == 1)
            {
                // bottom sil
                for (var x = r1; x <= r2; x++)
                {
                    if (_clipBot[x] == -2)
                    {
                        _clipBot[x] = _segs.ReadClip(ds.SprBottomClip, x);
                    }
                }
            }
            else if (silhouette == 2)
            {
                // top sil
                for (var x = r1; x <= r2; x++)
                {
                    if (_clipTop[x] == -2)
                    {
                        _clipTop[x] = _segs.ReadClip(ds.SprTopClip, x);
                    }
                }
            }
            else if (silhouette == 3)
            {
                // both
                for (var x = r1; x <= r2; x++)
                {
                    if (_clipBot[x] == -2)
                    {
                        _clipBot[x] = _segs.ReadClip(ds.SprBottomClip, x);
                    }

                    if (_clipTop[x] == -2)
                    {
                        _clipTop[x] = _segs.ReadClip(ds.SprTopClip, x);
                    }
                }
            }
        }

        // all clipping has been performed, so draw the sprite
        // (r_things.c:953-963)
        for (var x = spr.X1; x <= spr.X2; x++)
        {
            if (_clipBot[x] == -2)
            {
                _clipBot[x] = (short)_view.ViewHeight;
            }

            if (_clipTop[x] == -2)
            {
                _clipTop[x] = -1;
            }
        }

        _segs.MFloorClip = 0;
        _segs.MCeilingClip = 0;
        _segs.SetClipArrays(_clipBot, _clipTop);
        DrawVisSprite(spr, spr.X1, spr.X2);
        _segs.ClearClipArrays();
    }

    // ------------------------------------------------------------------
    // R_DrawMasked (r_things.c:978-1005)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_DrawMasked (r_things.c:978-1005): draw all vissprites back to
    /// front, then any remaining masked mid textures. Player sprites
    /// are deferred to DOOM-021+.
    /// </summary>
    public void DrawMasked(SpriteDef[] sprites)
    {
        SortVisSprites();

        if (VisSpriteP > 0)
        {
            // draw all vissprites back to front (r_things.c:985-994)
            for (var i = 0; i < SortedCount; i++)
            {
                DrawSprite(VisSprites[SortedOrder[i]]);
            }
        }

        // render any remaining masked mid textures (r_things.c:996-999)
        for (var dsIndex = _segs.DsP - 1; dsIndex >= 0; dsIndex--)
        {
            var ds = _segs.DrawSegs[dsIndex];
            if (ds.MaskedTextureCol != -1)
            {
                _segs.RenderMaskedSegRange(ds, ds.X1, ds.X2);
            }
        }

        // R_DrawPlayerSprites() — deferred to DOOM-021+.
    }
}

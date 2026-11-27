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
//      Floor/ceiling visplanes and per-column clipping.
//      Migration of Legacy/linuxdoom-1.10/r_plane.c: R_InitPlanes,
//      R_MapPlane, R_ClearPlanes, R_FindPlane, R_CheckPlane,
//      R_MakeSpans, R_DrawPlanes, floorclip/ceilingclip, openings.
//      See docs/adr/ADR-004 for tracing; DOOM-030c plan for the
//      documented deviations.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using Serilog;
using Serilog.Events;

namespace DoomSharp.Rendering;

/// <summary>
/// visplane_t (r_defs.h:460-480): one floor/ceiling plane candidate.
/// top/bottom keep the 0xff "no span" sentinel of the original.
/// </summary>
public sealed class Visplane
{
    public int Height;          // fixed
    public int PicNum;
    public int LightLevel;
    public int MinX = Video.ScreenWidth;
    public int MaxX = -1;
    public byte[] Top = new byte[Video.ScreenWidth];
    public byte[] Bottom = new byte[Video.ScreenWidth];

    public void Reset()
    {
        MinX = Video.ScreenWidth;
        MaxX = -1;
        Array.Fill(Top, (byte)0xff);
        Array.Fill(Bottom, (byte)0xff);
    }
}

/// <summary>
/// Visplane and plane-span management (r_plane.c). Also owns the
/// per-column wall clipping arrays (floorclip/ceilingclip) and the
/// shared openings pool that drawsegs slice into.
/// </summary>
public sealed class VisplaneManager
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(VisplaneManager));

    /// <summary>MAXVISPLANES (r_plane.c:52).</summary>
    public const int MaxVisplanes = 128;

    /// <summary>MAXOPENINGS (r_plane.c:59).</summary>
    public const int MaxOpenings = Video.ScreenWidth * 64;

    /// <summary>visplanes[MAXVISPLANES] (r_plane.c:53).</summary>
    public readonly Visplane[] Visplanes = new Visplane[MaxVisplanes];

    /// <summary>lastvisplane (r_plane.c:54) — one past the last used.</summary>
    public int LastVisplane;

    /// <summary>floorplane / ceilingplane (r_plane.c:55-56) — indices; -1 = NULL.</summary>
    public int FloorPlane = -1;
    public int CeilingPlane = -1;

    /// <summary>openings[MAXOPENINGS] (r_plane.c:60) — shared short pool.</summary>
    public readonly short[] Openings = new short[MaxOpenings];

    /// <summary>lastopening (r_plane.c:61) — allocation cursor into Openings.</summary>
    public int LastOpening;

    /// <summary>floorclip[SCREENWIDTH] (r_plane.c:69).</summary>
    public readonly short[] FloorClip = new short[Video.ScreenWidth];

    /// <summary>ceilingclip[SCREENWIDTH] (r_plane.c:70).</summary>
    public readonly short[] CeilingClip = new short[Video.ScreenWidth];

    /// <summary>spanstart/spanstop[SCREENHEIGHT] (r_plane.c:76-77).</summary>
    public readonly int[] SpanStart = new int[Video.ScreenHeight];
    public readonly int[] SpanStop = new int[Video.ScreenHeight];

    // texture mapping (r_plane.c:82-93)
    /// <summary>basexscale / baseyscale (r_plane.c:87-88).</summary>
    public int BaseXScale;
    public int BaseYScale;

    /// <summary>cachedheight/distance/xstep/ystep[SCREENHEIGHT] (r_plane.c:90-93).</summary>
    public readonly int[] CachedHeight = new int[Video.ScreenHeight];
    public readonly int[] CachedDistance = new int[Video.ScreenHeight];
    public readonly int[] CachedXStep = new int[Video.ScreenHeight];
    public readonly int[] CachedYStep = new int[Video.ScreenHeight];

    private readonly ViewSetup _view;

    public VisplaneManager(ViewSetup view)
    {
        _view = view;
        for (var i = 0; i < MaxVisplanes; i++)
        {
            Visplanes[i] = new Visplane();
        }

        Log.Verbose("VisplaneManager()");
    }

    // ------------------------------------------------------------------
    // R_InitPlanes (r_plane.c:101-104)
    // ------------------------------------------------------------------

    /// <summary>R_InitPlanes (r_plane.c:101-104): "Doh!" — no-op.</summary>
    public static void InitPlanes()
    {
        Log.Verbose("InitPlanes()");
    }

    // ------------------------------------------------------------------
    // R_ClearPlanes (r_plane.c:185-209)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_ClearPlanes (r_plane.c:185-209): reset clipping arrays, the
    /// visplane/opening pools and the plane slope caches; compute
    /// basexscale/baseyscale from the view angle.
    /// </summary>
    public void ClearPlanes()
    {
        Log.Verbose("ClearPlanes()");

        for (var i = 0; i < _view.ViewWidth; i++)
        {
            FloorClip[i] = (short)_view.ViewHeight;   // r_plane.c:193
            CeilingClip[i] = -1;                      // r_plane.c:194
        }

        LastVisplane = 0;
        LastOpening = 0;

        // texture calculation (r_plane.c:201)
        Array.Clear(CachedHeight);

        // left to right mapping (r_plane.c:204-208)
        var angle = (int)((RendererMath.ViewAngle - AngleTables.Ang90) >> AngleTables.AngleToFineShift);
        BaseXScale = FixedPoint.FixedDiv(AngleTables.FineCosine[angle], _view.CenterXFrac);
        BaseYScale = -FixedPoint.FixedDiv(AngleTables.FineSine[angle], _view.CenterXFrac);
    }

    // ------------------------------------------------------------------
    // R_FindPlane (r_plane.c:217-259)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_FindPlane (r_plane.c:217-259): find or allocate a visplane
    /// with the given height/pic/light. Sky flats collapse to
    /// height 0 / light 0 so all skies map together (r_plane.c:225-229).
    /// Returns the visplane index; -1 would be the overflow fatal.
    /// </summary>
    public int FindPlane(int height, int picnum, int lightlevel, int skyFlatNum)
    {
        if (picnum == skyFlatNum)
        {
            height = 0;             // all skys map together (r_plane.c:227)
            lightlevel = 0;
        }

        for (var check = 0; check < LastVisplane; check++)
        {
            var candidate = Visplanes[check];
            if (height == candidate.Height && picnum == candidate.PicNum && lightlevel == candidate.LightLevel)
            {
                return check;
            }
        }

        if (LastVisplane == MaxVisplanes)
        {
            Log.Verbose("FindPlane fatal: no more visplanes");
            throw new DoomFatalException("R_FindPlane: no more visplanes");
        }

        var pl = Visplanes[LastVisplane++];
        pl.Height = height;
        pl.PicNum = picnum;
        pl.LightLevel = lightlevel;
        pl.Reset();                 // minx/maxx + top[] 0xff fill (r_plane.c:253-256)

        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("FindPlane(h={Height} pic={PicNum} light={LightLevel}) = #{Index}",
                height, picnum, lightlevel, LastVisplane - 1);
        }

        return LastVisplane - 1;
    }

    // ------------------------------------------------------------------
    // R_CheckPlane (r_plane.c:265-324)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_CheckPlane (r_plane.c:265-324): extend a visplane with a new
    /// column range if the range is free; otherwise split off a new
    /// visplane. Returns the (possibly new) visplane index.
    /// </summary>
    public int CheckPlane(int plIndex, int start, int stop)
    {
        var pl = Visplanes[plIndex];

        int intrl;
        int intrh;
        int unionl;
        int unionh;

        if (start < pl.MinX)
        {
            intrl = pl.MinX;
            unionl = start;
        }
        else
        {
            unionl = pl.MinX;
            intrl = start;
        }

        if (stop > pl.MaxX)
        {
            intrh = pl.MaxX;
            unionh = stop;
        }
        else
        {
            unionh = pl.MaxX;
            intrh = stop;
        }

        int x;
        for (x = intrl; x <= intrh; x++)
        {
            if (pl.Top[x] != 0xff)
            {
                break;
            }
        }

        if (x > intrh)
        {
            // use the same one (r_plane.c:303-309)
            pl.MinX = unionl;
            pl.MaxX = unionh;
            return plIndex;
        }

        // make a new visplane (r_plane.c:312-323)
        if (LastVisplane >= MaxVisplanes)
        {
            Log.Verbose("CheckPlane fatal: no more visplanes");
            throw new DoomFatalException("R_FindPlane: no more visplanes");
        }

        var newPl = Visplanes[LastVisplane];
        newPl.Height = pl.Height;
        newPl.PicNum = pl.PicNum;
        newPl.LightLevel = pl.LightLevel;
        LastVisplane++;

        pl = newPl;
        pl.MinX = start;
        pl.MaxX = stop;
        pl.Reset();

        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("CheckPlane(#{Pl} {Start}..{Stop}) = new #{Index}", plIndex, start, stop, LastVisplane - 1);
        }

        return LastVisplane - 1;
    }

    // ------------------------------------------------------------------
    // R_MapPlane (r_plane.c:366-437)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_MapPlane (r_plane.c:366-437): texture-map one horizontal span
    /// of a plane at screen row y, columns x1..x2. Uses the cached
    /// distance/xstep/ystep per row (r_plane.c:391-412) and the
    /// planezlight LUT for lighting.
    /// </summary>
    public void MapPlane(int y, int x1, int x2, WadDirectory wad, TextureManager textures)
    {
        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("MapPlane(y={Y} x1={X1} x2={X2})", y, x1, x2);
        }

        // r_plane.c:391-412 — per-row cached distance/steps.
        int distance;
        if (PlaneHeight != CachedHeight[y])
        {
            CachedHeight[y] = PlaneHeight;
            distance = CachedDistance[y] = FixedPoint.FixedMul(PlaneHeight, _view.YSlope[y]);
            CachedXStep[y] = FixedPoint.FixedMul(distance, BaseXScale);
            CachedYStep[y] = FixedPoint.FixedMul(distance, BaseYScale);
        }
        else
        {
            distance = CachedDistance[y];
        }

        var length = FixedPoint.FixedMul(distance, _view.DistScale[x1]);
        var angle = (int)((RendererMath.ViewAngle + _view.XToViewAngle[x1]) >> AngleTables.AngleToFineShift);
        var xfrac = RendererMath.ViewX + FixedPoint.FixedMul(AngleTables.FineCosine[angle], length);
        var yfrac = -RendererMath.ViewY - FixedPoint.FixedMul(AngleTables.FineSine[angle], length);

        if (RendererMath.FixedColormap != null)
        {
            SpanDrawState.Colormap = RendererMath.FixedColormap;
        }
        else
        {
            var index = (uint)distance >> ViewSetup.LightZShift;
            if (index >= ViewSetup.MaxLightZ)
            {
                index = ViewSetup.MaxLightZ - 1;
            }

            SpanDrawState.Colormap = PlaneZLight[index];
        }

        SpanDrawState.Y = y;
        SpanDrawState.X1 = x1;
        SpanDrawState.X2 = x2;
        SpanDrawState.XFrac = xfrac;
        SpanDrawState.YFrac = yfrac;
        SpanDrawState.XStep = CachedXStep[y];
        SpanDrawState.YStep = CachedYStep[y];
        SpanDrawState.Source = textures.GetFlat(wad, CurrentPicNum);

        // high or low detail (r_plane.c:435)
        if (RendererMath.DetailShift == 0)
        {
            SpanDrawer.DrawSpan(_view);
        }
        else
        {
            SpanDrawer.DrawSpanLow(_view);
        }
    }

    /// <summary>planeheight (r_plane.c:83) — set by DrawPlanes per plane.</summary>
    public int PlaneHeight { get; set; }

    /// <summary>Current visplane picnum being mapped (ds_source selector).</summary>
    public int CurrentPicNum;

    /// <summary>planezlight — current plane light LUT (r_plane.c:82).</summary>
    public byte[][] PlaneZLight = [];

    // ------------------------------------------------------------------
    // R_DrawPlanes (r_plane.c:443-452 + 366-437)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_DrawPlanes (r_plane.c:443-452): walk every visplane and emit
    /// its spans. Sky planes (picnum == skyflatnum) take the sky
    /// texture path (r_plane.c:366-437 sky branch).
    /// </summary>
    public void DrawPlanes(int skyFlatNum, int skyTexture, int skyTextureMid, WadDirectory wad, TextureManager textures)
    {
        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("DrawPlanes({Count} visplanes)", LastVisplane);
        }

        for (var plIndex = 0; plIndex < LastVisplane; plIndex++)
        {
            var pl = Visplanes[plIndex];

            if (pl.MinX > pl.MaxX)
            {
                continue;               // nothing to draw (r_plane.c:452)
            }

            if (Log.IsEnabled(LogEventLevel.Verbose))
            {
                Log.Verbose("DrawPlanes plane #{Index} pic={Pic} {MinX}..{MaxX}",
                    plIndex, pl.PicNum, pl.MinX, pl.MaxX);
            }

            // sky flat (r_plane.c:366-380)
            if (pl.PicNum == skyFlatNum)
            {
                // Sky is always drawn full bright, i.e. colormaps[0]
                // is used (r_plane.c:369-374).
                ColumnDrawState.IScale = _view.PspriteIScale >> RendererMath.DetailShift;
                ColumnDrawState.Colormap = ViewSetup.ColormapsGlobal;
                ColumnDrawState.TextureMid = skyTextureMid;

                for (var x = pl.MinX; x <= pl.MaxX; x++)
                {
                    ColumnDrawState.Yl = pl.Top[x];
                    ColumnDrawState.Yh = pl.Bottom[x];

                    if (ColumnDrawState.Yl <= ColumnDrawState.Yh)
                    {
                        var angle = (int)((RendererMath.ViewAngle + _view.XToViewAngle[x]) >> SkyRenderer.AngleToSkyShift);
                        ColumnDrawState.X = x;
                        ColumnDrawState.Source = textures.GetColumn(wad, skyTexture, angle, out var sourceOfs);
                        ColumnDrawState.SourceOfs = sourceOfs;

                        if (RendererMath.DetailShift == 0)
                        {
                            ColumnDrawer.DrawColumn(_view);
                        }
                        else
                        {
                            ColumnDrawer.DrawColumnLow(_view);
                        }
                    }
                }

                continue;
            }

            // regular flat (r_plane.c:381-437)
            CurrentPicNum = pl.PicNum;
            PlaneHeight = Math.Abs(pl.Height - RendererMath.ViewZ);
            var light = (pl.LightLevel >> ViewSetup.LightSegShift) + RendererMath.ExtraLight;

            if (light >= ViewSetup.LightLevels)
            {
                light = ViewSetup.LightLevels - 1;
            }

            if (light < 0)
            {
                light = 0;
            }

            PlaneZLight = _view.ZLight[light];

            // leave pads for [minx-1]/[maxx+1] (r_plane.c:425-426)
            pl.Top[pl.MaxX + 1] = 0xff;
            pl.Top[pl.MinX - 1] = 0xff;

            var stop = pl.MaxX + 1;

            for (var x = pl.MinX; x <= stop; x++)
            {
                MakeSpans(x, pl.Top[x - 1], pl.Bottom[x - 1], pl.Top[x], pl.Bottom[x], wad, textures);
            }
        }
    }

    /// <summary>
    /// R_MakeSpans (r_plane.c:330-359): emit horizontal spans where the
    /// plane's top/bottom profile changes between columns x-1 and x.
    /// </summary>
    public void MakeSpans(int x, int t1, int b1, int t2, int b2, WadDirectory wad, TextureManager textures)
    {
        while (t1 < t2 && t1 <= b1)
        {
            MapPlane(t1, SpanStart[t1], x - 1, wad, textures);
            t1++;
        }

        while (b1 > b2 && b1 >= t1)
        {
            MapPlane(b1, SpanStart[b1], x - 1, wad, textures);
            b1--;
        }

        while (t2 < t1 && t2 <= b2)
        {
            SpanStart[t2] = x;
            t2++;
        }

        while (b2 > b1 && b2 >= t2)
        {
            SpanStart[b2] = x;
            b2--;
        }
    }
}

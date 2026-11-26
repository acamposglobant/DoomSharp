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
//      Span (floor/ceiling) rasterizers.
//      Migration of Legacy/linuxdoom-1.10/r_draw.c: R_DrawSpan,
//      R_DrawSpanLow.
//      See docs/adr/ADR-004 for tracing; DOOM-030b plan for the
//      documented deviations.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using Serilog;
using Serilog.Events;

namespace DoomSharp.Rendering;

/// <summary>
/// Per-span draw state (r_draw.c ds_* globals).
/// </summary>
public static class SpanDrawState
{
    /// <summary>ds_y — screen row.</summary>
    public static int Y;

    /// <summary>ds_x1 — left edge (inclusive).</summary>
    public static int X1;

    /// <summary>ds_x2 — right edge (inclusive; do-while draws count+1 pixels).</summary>
    public static int X2;

    /// <summary>ds_colormap — lighting LUT.</summary>
    public static byte[] Colormap = [];

    /// <summary>ds_xfrac — 16.16 texture u at ds_x1 (fixed).</summary>
    public static int XFrac;

    /// <summary>ds_yfrac — 16.16 texture v at ds_x1 (fixed).</summary>
    public static int YFrac;

    /// <summary>ds_xstep — u step per screen pixel (fixed).</summary>
    public static int XStep;

    /// <summary>ds_ystep — v step per screen pixel (fixed).</summary>
    public static int YStep;

    /// <summary>ds_source — start of the 64x64 flat tile.</summary>
    public static byte[] Source = [];
}

/// <summary>
/// Span rasterizers (r_draw.c): R_DrawSpan and R_DrawSpanLow.
/// </summary>
public static class SpanDrawer
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(SpanDrawer));

    /// <summary>dscount (r_draw.c:510) — profiling counter.</summary>
    public static int DsCount;

    /// <summary>
    /// R_DrawSpan (r_draw.c:517-580): texture-map a horizontal span of
    /// a 64x64 flat. spot = ((yfrac>>10)&amp;(63*64)) + ((xfrac>>16)&amp;63).
    /// No zero-span check (preserved from the original).
    /// </summary>
    public static void DrawSpan(ViewSetup view)
    {
        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("DrawSpan(y={Y} x1={X1} x2={X2})", SpanDrawState.Y, SpanDrawState.X1, SpanDrawState.X2);
        }

        if (SpanDrawState.X2 < SpanDrawState.X1
            || SpanDrawState.X1 < 0
            || SpanDrawState.X2 >= Video.ScreenWidth
            || (uint)SpanDrawState.Y > Video.ScreenHeight)
        {
            throw new DoomFatalException(
                $"R_DrawSpan: {SpanDrawState.X1} to {SpanDrawState.X2} at {SpanDrawState.Y}");
        }

        var xfrac = SpanDrawState.XFrac;
        var yfrac = SpanDrawState.YFrac;

        var dest = view.YLookup[SpanDrawState.Y] + view.ColumnOfs[SpanDrawState.X1];

        // We do not check for zero spans here (preserved).
        var count = SpanDrawState.X2 - SpanDrawState.X1;

        var colormap = SpanDrawState.Colormap;
        var source = SpanDrawState.Source;
        var screen = Video.Screens[0];

        do
        {
            // Current texture index in u,v.
            var spot = ((yfrac >> (16 - 6)) & (63 * 64)) + ((xfrac >> 16) & 63);

            // Lookup pixel from flat texture tile, re-index using
            // light/colormap.
            screen[dest++] = colormap[source[spot]];

            // Next step in u,v.
            xfrac += SpanDrawState.XStep;
            yfrac += SpanDrawState.YStep;
        }
        while (count-- > 0);
    }

    /// <summary>
    /// R_DrawSpanLow (r_draw.c:600-660): blocky low-detail variant;
    /// ds_x1/ds_x2 are doubled and each texel is written twice.
    /// </summary>
    public static void DrawSpanLow(ViewSetup view)
    {
        if (SpanDrawState.X2 < SpanDrawState.X1
            || SpanDrawState.X1 < 0
            || SpanDrawState.X2 >= Video.ScreenWidth
            || (uint)SpanDrawState.Y > Video.ScreenHeight)
        {
            throw new DoomFatalException(
                $"R_DrawSpan: {SpanDrawState.X1} to {SpanDrawState.X2} at {SpanDrawState.Y}");
        }

        var xfrac = SpanDrawState.XFrac;
        var yfrac = SpanDrawState.YFrac;

        var x1 = SpanDrawState.X1 << 1;
        var dest = view.YLookup[SpanDrawState.Y] + view.ColumnOfs[x1];
        var dest2 = view.YLookup[SpanDrawState.Y] + view.ColumnOfs[x1 + 1];

        var count = SpanDrawState.X2 - SpanDrawState.X1;

        var colormap = SpanDrawState.Colormap;
        var source = SpanDrawState.Source;
        var screen = Video.Screens[0];

        do
        {
            var spot = ((yfrac >> (16 - 6)) & (63 * 64)) + ((xfrac >> 16) & 63);
            var texel = colormap[source[spot]];

            screen[dest++] = texel;
            screen[dest++] = texel;

            xfrac += SpanDrawState.XStep;
            yfrac += SpanDrawState.YStep;
        }
        while (count-- > 0);
    }
}

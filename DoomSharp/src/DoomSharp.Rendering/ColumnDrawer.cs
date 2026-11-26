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
//      Column rasterizers.
//      Migration of Legacy/linuxdoom-1.10/r_draw.c: R_DrawColumn,
//      R_DrawColumnLow, R_DrawFuzzColumn, R_DrawTranslatedColumn,
//      R_DrawTranslatedColumnLow.
//      See docs/adr/ADR-004 for tracing; DOOM-030b plan for the
//      documented deviations.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using Serilog;
using Serilog.Events;

namespace DoomSharp.Rendering;

/// <summary>
/// Per-column draw state (r_draw.c dc_* globals). The renderer sets
/// these before calling a drawer; the drawers are static like the C.
/// </summary>
public static class ColumnDrawState
{
    /// <summary>dc_colormap — lighting LUT (256 bytes).</summary>
    public static byte[] Colormap = [];

    /// <summary>dc_x — screen column.</summary>
    public static int X;

    /// <summary>dc_yl — top of the vertical span (inclusive).</summary>
    public static int Yl;

    /// <summary>dc_yh — bottom of the vertical span (inclusive).</summary>
    public static int Yh;

    /// <summary>dc_iscale — texel step per screen pixel (fixed).</summary>
    public static int IScale;

    /// <summary>dc_texturemid — texture vertical anchor (fixed).</summary>
    public static int TextureMid;

    /// <summary>dc_source — first pixel of the source column.</summary>
    public static byte[] Source = [];

    /// <summary>Offset of the first source pixel within Source.</summary>
    public static int SourceOfs;

    /// <summary>dc_translation — player color translation table.</summary>
    public static byte[] Translation = [];
}

/// <summary>
/// Column rasterizers (r_draw.c): R_DrawColumn, R_DrawColumnLow,
/// R_DrawFuzzColumn, R_DrawTranslatedColumn. All write into
/// Video.Screens[0] via ylookup/columnofs.
/// </summary>
public static class ColumnDrawer
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(ColumnDrawer));

    /// <summary>FUZZTABLE (r_draw.c:259).</summary>
    public const int FuzzTable = 50;

    /// <summary>FUZZOFF (r_draw.c:260).</summary>
    public const int FuzzOff = Video.ScreenWidth;

    /// <summary>fuzzoffset[FUZZTABLE] (r_draw.c:263-272) — verbatim.</summary>
    public static readonly int[] FuzzOffset =
    [
        FuzzOff, -FuzzOff, FuzzOff, -FuzzOff, FuzzOff, FuzzOff, -FuzzOff,
        FuzzOff, FuzzOff, -FuzzOff, FuzzOff, FuzzOff, FuzzOff, -FuzzOff,
        FuzzOff, FuzzOff, FuzzOff, -FuzzOff, -FuzzOff, -FuzzOff, -FuzzOff,
        FuzzOff, -FuzzOff, -FuzzOff, FuzzOff, FuzzOff, FuzzOff, FuzzOff, -FuzzOff,
        FuzzOff, -FuzzOff, FuzzOff, FuzzOff, -FuzzOff, -FuzzOff, FuzzOff,
        FuzzOff, -FuzzOff, -FuzzOff, -FuzzOff, -FuzzOff, FuzzOff, FuzzOff,
        FuzzOff, FuzzOff, -FuzzOff, FuzzOff, FuzzOff, -FuzzOff, FuzzOff,
    ];

    /// <summary>fuzzpos (r_draw.c:274) — shared mutable position, wraps at FUZZTABLE.</summary>
    public static int FuzzPos { get; set; }

    /// <summary>dccount (r_draw.c:98) — profiling counter.</summary>
    public static int DcCount;

    /// <summary>
    /// R_DrawColumn (r_draw.c:107-160): texture-map a vertical column.
    /// count = dc_yh - dc_yl (do-while draws count+1 pixels); the
    /// source index is masked with &amp;127 (128-tall wall columns).
    /// </summary>
    public static void DrawColumn(ViewSetup view)
    {
        var count = ColumnDrawState.Yh - ColumnDrawState.Yl;

        // Zero length, column does not exceed a pixel.
        if (count < 0)
        {
            return;
        }

        if ((uint)ColumnDrawState.X >= Video.ScreenWidth
            || ColumnDrawState.Yl < 0
            || ColumnDrawState.Yh >= Video.ScreenHeight)
        {
            throw new DoomFatalException(
                $"R_DrawColumn: {ColumnDrawState.Yl} to {ColumnDrawState.Yh} at {ColumnDrawState.X}");
        }

        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("DrawColumn(x={X} yl={Yl} yh={Yh} iscale={IScale} mid={Mid})",
                ColumnDrawState.X, ColumnDrawState.Yl, ColumnDrawState.Yh,
                ColumnDrawState.IScale, ColumnDrawState.TextureMid);
        }

        // Framebuffer destination address.
        var dest = view.YLookup[ColumnDrawState.Yl] + view.ColumnOfs[ColumnDrawState.X];

        // Determine scaling, which is the only mapping to be done.
        var fracstep = ColumnDrawState.IScale;
        var frac = ColumnDrawState.TextureMid
            + (ColumnDrawState.Yl - view.CenterY) * fracstep;

        var source = ColumnDrawState.Source;
        var sourceOfs = ColumnDrawState.SourceOfs;
        var colormap = ColumnDrawState.Colormap;
        var screen = Video.Screens[0];

        // Inner loop that does the actual texture mapping.
        do
        {
            // Re-map color indices from wall texture column
            // using a lighting/special effects LUT.
            screen[dest] = colormap[source[sourceOfs + ((frac >> FixedPoint.FracBits) & 127)]];

            dest += Video.ScreenWidth;
            frac += fracstep;
        }
        while (count-- > 0);
    }

    /// <summary>
    /// R_DrawColumnLow (r_draw.c:180-247): blocky low-detail variant;
    /// dc_x is doubled and both destination columns are written.
    /// </summary>
    public static void DrawColumnLow(ViewSetup view)
    {
        var count = ColumnDrawState.Yh - ColumnDrawState.Yl;

        if (count < 0)
        {
            return;
        }

        if ((uint)ColumnDrawState.X >= Video.ScreenWidth
            || ColumnDrawState.Yl < 0
            || ColumnDrawState.Yh >= Video.ScreenHeight)
        {
            throw new DoomFatalException(
                $"R_DrawColumn: {ColumnDrawState.Yl} to {ColumnDrawState.Yh} at {ColumnDrawState.X}");
        }

        // low detail, 2 pixels wide
        var x = ColumnDrawState.X << 1;
        var dest1 = view.YLookup[ColumnDrawState.Yl] + view.ColumnOfs[x];
        var dest2 = view.YLookup[ColumnDrawState.Yl] + view.ColumnOfs[x + 1];

        var fracstep = ColumnDrawState.IScale;
        var frac = ColumnDrawState.TextureMid + (ColumnDrawState.Yl - view.CenterY) * fracstep;

        var source = ColumnDrawState.Source;
        var sourceOfs = ColumnDrawState.SourceOfs;
        var colormap = ColumnDrawState.Colormap;
        var screen = Video.Screens[0];

        do
        {
            var texel = source[sourceOfs + ((frac >> FixedPoint.FracBits) & 127)];
            screen[dest1] = colormap[texel];
            screen[dest2] = colormap[texel];

            dest1 += Video.ScreenWidth;
            dest2 += Video.ScreenWidth;
            frac += fracstep;
        }
        while (count-- > 0);
    }

    /// <summary>
    /// R_DrawFuzzColumn (r_draw.c:282-368): Spectre/invisibility
    /// effect. Reads the framebuffer pixel one column left or right
    /// (fuzzoffset) and re-indexes through colormap 6. fuzzpos is
    /// shared state that wraps at FUZZTABLE.
    /// </summary>
    public static void DrawFuzzColumn(ViewSetup view)
    {
        // Adjust borders. Low...
        if (ColumnDrawState.Yl == 0)
        {
            ColumnDrawState.Yl = 1;
        }

        // .. and high.
        if (ColumnDrawState.Yh == view.ViewHeight - 1)
        {
            ColumnDrawState.Yh = view.ViewHeight - 2;
        }

        var count = ColumnDrawState.Yh - ColumnDrawState.Yl;

        // Zero length.
        if (count < 0)
        {
            return;
        }

        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("DrawFuzzColumn(x={X} yl={Yl} yh={Yh} fuzzpos={FuzzPos})",
                ColumnDrawState.X, ColumnDrawState.Yl, ColumnDrawState.Yh, FuzzPos);
        }

        if ((uint)ColumnDrawState.X >= Video.ScreenWidth
            || ColumnDrawState.Yl < 0
            || ColumnDrawState.Yh >= Video.ScreenHeight)
        {
            throw new DoomFatalException(
                $"R_DrawFuzzColumn: {ColumnDrawState.Yl} to {ColumnDrawState.Yh} at {ColumnDrawState.X}");
        }

        // Does not work with blocky mode.
        var dest = view.YLookup[ColumnDrawState.Yl] + view.ColumnOfs[ColumnDrawState.X];
        var colormap = RendererMath.FixedColormap ?? ViewSetup.ColormapsGlobal;
        var screen = Video.Screens[0];

        // Looks like an attempt at dithering, using the colormap #6.
        do
        {
            // Lookup framebuffer, and retrieve a pixel that is either
            // one column left or right of the current one. Add index
            // from colormap to index.
            screen[dest] = colormap[6 * 256 + screen[dest + FuzzOffset[FuzzPos]]];

            // Clamp table lookup index.
            if (++FuzzPos == FuzzTable)
            {
                FuzzPos = 0;
            }

            dest += Video.ScreenWidth;
        }
        while (count-- > 0);
    }

    /// <summary>
    /// R_DrawTranslatedColumn (r_draw.c:388-447): player sprite color
    /// translation. No &amp;127 mask (sprite columns are unmasked).
    /// </summary>
    public static void DrawTranslatedColumn(ViewSetup view)
    {
        var count = ColumnDrawState.Yh - ColumnDrawState.Yl;

        if (count < 0)
        {
            return;
        }

        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("DrawTranslatedColumn(x={X} yl={Yl} yh={Yh})",
                ColumnDrawState.X, ColumnDrawState.Yl, ColumnDrawState.Yh);
        }

        if ((uint)ColumnDrawState.X >= Video.ScreenWidth
            || ColumnDrawState.Yl < 0
            || ColumnDrawState.Yh >= Video.ScreenHeight)
        {
            throw new DoomFatalException(
                $"R_DrawColumn: {ColumnDrawState.Yl} to {ColumnDrawState.Yh} at {ColumnDrawState.X}");
        }

        var dest = view.YLookup[ColumnDrawState.Yl] + view.ColumnOfs[ColumnDrawState.X];

        var fracstep = ColumnDrawState.IScale;
        var frac = ColumnDrawState.TextureMid + (ColumnDrawState.Yl - view.CenterY) * fracstep;

        var colormap = ColumnDrawState.Colormap;
        var translation = ColumnDrawState.Translation;
        var source = ColumnDrawState.Source;
        var sourceOfs = ColumnDrawState.SourceOfs;
        var screen = Video.Screens[0];

        // Here we do an additional index re-mapping.
        do
        {
            // Translation tables are used to map certain colorramps to
            // other ones, used with PLAY sprites.
            screen[dest] = colormap[translation[source[sourceOfs + (frac >> FixedPoint.FracBits)]]];

            dest += Video.ScreenWidth;
            frac += fracstep;
        }
        while (count-- > 0);
    }
}

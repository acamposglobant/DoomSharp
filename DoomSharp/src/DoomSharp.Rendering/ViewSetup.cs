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
//      View setup, projection LUTs and light tables.
//      Migration of Legacy/linuxdoom-1.10/r_main.c: R_SetViewSize,
//      R_ExecuteSetViewSize, R_InitTextureMapping, R_InitLightTables,
//      R_SetupFrame, and r_draw.c: R_InitBuffer,
//      R_InitTranslationTables. R_InitPlanes / R_InitSkyMap are
//      deferred to DOOM-030c.
//      See docs/adr/ADR-004 for tracing; DOOM-030b plan for the
//      documented deviations.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using Serilog;

namespace DoomSharp.Rendering;

/// <summary>
/// View setup and projection tables (r_main.c): view size, texture
/// mapping (viewangletox / xtoviewangle / clipangle), light tables
/// (zlight, scalelight), and per-frame view state.
/// </summary>
public sealed class ViewSetup
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(ViewSetup));

    /// <summary>Fineangles in the SCREENWIDTH wide window (r_main.c:48).</summary>
    public const int FieldOfView = 2048;

    /// <summary>LIGHTLEVELS (r_main.h:69).</summary>
    public const int LightLevels = 16;

    /// <summary>MAXLIGHTSCALE (r_main.h:72).</summary>
    public const int MaxLightScale = 48;

    /// <summary>LIGHTSCALESHIFT (r_main.h:73).</summary>
    public const int LightScaleShift = 12;

    /// <summary>MAXLIGHTZ (r_main.h:74).</summary>
    public const int MaxLightZ = 128;

    /// <summary>LIGHTZSHIFT (r_main.h:75).</summary>
    public const int LightZShift = 20;

    /// <summary>NUMCOLORMAPS (r_main.h:87).</summary>
    public const int NumColormaps = 32;

    /// <summary>DISTMAP (r_main.c:612).</summary>
    public const int DistMap = 2;

    // --- r_main.c globals ---

    /// <summary>setsizeneeded (r_main.c:652).</summary>
    public bool SetSizeNeeded;

    /// <summary>setblocks (r_main.c:653).</summary>
    public int SetBlocks;

    /// <summary>setdetail (r_main.c:654).</summary>
    public int SetDetail;

    /// <summary>scaledviewwidth (r_draw.c:65).</summary>
    public int ScaledViewWidth;

    /// <summary>viewwidth (r_draw.c:64).</summary>
    public int ViewWidth;

    /// <summary>viewheight (r_draw.c:66).</summary>
    public int ViewHeight;

    /// <summary>centerx (r_main.c:61).</summary>
    public int CenterX;

    /// <summary>centery (r_main.c:62).</summary>
    public int CenterY;

    /// <summary>centerxfrac (r_main.c:64).</summary>
    public int CenterXFrac;

    /// <summary>centeryfrac (r_main.c:65).</summary>
    public int CenterYFrac;

    /// <summary>viewangletox[FINEANGLES/2] (r_main.c:98).</summary>
    public int[] ViewAngleToX = new int[AngleTables.FineAngles / 2];

    /// <summary>xtoviewangle[SCREENWIDTH+1] (r_main.c:103).</summary>
    public uint[] XToViewAngle = new uint[Video.ScreenWidth + 1];

    /// <summary>clipangle (r_main.c:92).</summary>
    public uint ClipAngle;

    /// <summary>yslope[viewheight] — per-row plane slope (r_local.h).</summary>
    public int[] YSlope = [];

    /// <summary>distscale[viewwidth] (r_local.h).</summary>
    public int[] DistScale = [];

    /// <summary>screenheightarray[viewwidth] (r_local.h).</summary>
    public int[] ScreenHeightArray = [];

    /// <summary>scalelight[LIGHTLEVELS][MAXLIGHTSCALE] (r_main.c:116).</summary>
    public byte[][][] ScaleLight { get; } = new byte[LightLevels][][];

    /// <summary>scalelightfixed[MAXLIGHTSCALE] (r_main.c:117).</summary>
    public byte[][] ScaleLightFixed { get; } = new byte[MaxLightScale][];

    /// <summary>zlight[LIGHTLEVELS][MAXLIGHTZ] (r_main.c:118).</summary>
    public byte[][][] ZLight { get; } = new byte[LightLevels][][];

    /// <summary>pspritescale (r_segs.c:44).</summary>
    public int PspriteScale { get; private set; }

    /// <summary>pspriteiscale (r_segs.c).</summary>
    public int PspriteIScale { get; private set; }

    /// <summary>ylookup (r_draw.c:69) — row start offsets into screens[0].</summary>
    public int[] YLookup { get; private set; } = [];

    /// <summary>columnofs (r_draw.c:70) — column offsets.</summary>
    public int[] ColumnOfs { get; private set; } = [];

    /// <summary>viewwindowx (r_draw.c:67).</summary>
    public int ViewWindowX { get; private set; }

    /// <summary>viewwindowy (r_draw.c:68).</summary>
    public int ViewWindowY { get; private set; }

    /// <summary>translationtables (r_draw.c:383).</summary>
    public byte[] TranslationTables { get; private set; } = [];

    /// <summary>COLORMAP lump (from TextureManager, r_data.c).</summary>
    private byte[] _colormaps = [];

    private readonly TextureManager _textureManager;

    public ViewSetup(TextureManager textureManager)
    {
        _textureManager = textureManager;
        Log.Verbose("ViewSetup({HasTextureManager})", textureManager != null);
    }

    /// <summary>
    /// R_SetViewSize (r_main.c:657-665): defer the change to the next
    /// refresh (setsizeneeded flag).
    /// </summary>
    public void SetViewSize(int blocks, int detail)
    {
        Log.Verbose("SetViewSize({Blocks}, {Detail})", blocks, detail);
        SetSizeNeeded = true;
        SetBlocks = blocks;
        SetDetail = detail;
    }

    /// <summary>
    /// R_ExecuteSetViewSize (r_main.c:671-761): compute view geometry,
    /// select drawer functions, init buffer LUTs, texture mapping and
    /// light tables.
    /// </summary>
    public void ExecuteSetViewSize()
    {
        Log.Verbose("ExecuteSetViewSize({SetBlocks}, {SetDetail})", SetBlocks, SetDetail);

        SetSizeNeeded = false;

        if (SetBlocks == 11)
        {
            ScaledViewWidth = Video.ScreenWidth;
            ViewHeight = Video.ScreenHeight;
        }
        else
        {
            ScaledViewWidth = SetBlocks * 32;
            ViewHeight = (SetBlocks * 168 / 10) & ~7;
        }

        RendererMath.DetailShift = SetDetail;
        ViewWidth = ScaledViewWidth >> RendererMath.DetailShift;

        CenterY = ViewHeight / 2;
        CenterX = ViewWidth / 2;
        CenterXFrac = CenterX << FixedPoint.FracBits;
        CenterYFrac = CenterY << FixedPoint.FracBits;
        RendererMath.Projection = CenterXFrac;

        InitBuffer(ScaledViewWidth, ViewHeight);

        InitTextureMapping();

        // psprite scales (r_main.c:722-723)
        PspriteScale = FixedPoint.FracUnit * ViewWidth / Video.ScreenWidth;
        PspriteIScale = FixedPoint.FracUnit * Video.ScreenWidth / ViewWidth;

        // thing clipping
        ScreenHeightArray = new int[ViewWidth];
        for (var i = 0; i < ViewWidth; i++)
        {
            ScreenHeightArray[i] = ViewHeight;
        }

        // planes
        YSlope = new int[ViewHeight];
        for (var i = 0; i < ViewHeight; i++)
        {
            var dy = ((i - ViewHeight / 2) << FixedPoint.FracBits) + FixedPoint.FracUnit / 2;
            dy = Math.Abs(dy);
            YSlope[i] = FixedPoint.FixedDiv((ViewWidth << RendererMath.DetailShift) / 2 * FixedPoint.FracUnit, dy);
        }

        DistScale = new int[ViewWidth];
        for (var i = 0; i < ViewWidth; i++)
        {
            var cosAdj = Math.Abs(AngleTables.FineCosine[(int)(XToViewAngle[i] >> AngleTables.AngleToFineShift)]);
            DistScale[i] = FixedPoint.FixedDiv(FixedPoint.FracUnit, cosAdj);
        }

        // Calculate the light levels to use for each level / scale
        // combination (r_main.c:745-760).
        for (var i = 0; i < LightLevels; i++)
        {
            var startMap = ((LightLevels - 1 - i) * 2) * NumColormaps / LightLevels;
            ScaleLight[i] = new byte[MaxLightScale][];
            for (var j = 0; j < MaxLightScale; j++)
            {
                var level = startMap - j * Video.ScreenWidth / (ViewWidth << RendererMath.DetailShift) / DistMap;

                if (level < 0)
                {
                    level = 0;
                }

                if (level >= NumColormaps)
                {
                    level = NumColormaps - 1;
                }

                ScaleLight[i][j] = GetColormapSlice(level);
            }
        }
    }

    /// <summary>
    /// R_InitTextureMapping (r_main.c:544-603): build viewangletox and
    /// xtoviewangle from the finetangent table.
    /// </summary>
    public void InitTextureMapping()
    {
        Log.Verbose("InitTextureMapping()");

        // Calc focallength so FIELDOFVIEW angles covers SCREENWIDTH.
        var focallength = FixedPoint.FixedDiv(
            CenterXFrac,
            AngleTables.FineTangent[AngleTables.FineAngles / 4 + FieldOfView / 2]);

        // Use tangent table to generate viewangletox: viewangletox
        // will give the next greatest x after the view angle.
        for (var i = 0; i < AngleTables.FineAngles / 2; i++)
        {
            int t;
            if (AngleTables.FineTangent[i] > FixedPoint.FracUnit * 2)
            {
                t = -1;
            }
            else if (AngleTables.FineTangent[i] < -FixedPoint.FracUnit * 2)
            {
                t = ViewWidth + 1;
            }
            else
            {
                t = FixedPoint.FixedMul(AngleTables.FineTangent[i], focallength);
                t = (CenterXFrac - t + FixedPoint.FracUnit - 1) >> FixedPoint.FracBits;

                if (t < -1)
                {
                    t = -1;
                }
                else if (t > ViewWidth + 1)
                {
                    t = ViewWidth + 1;
                }
            }

            ViewAngleToX[i] = t;
        }

        // Scan viewangletox[] to generate xtoviewangle[]: xtoviewangle
        // will give the smallest view angle that maps to x.
        for (var x = 0; x <= ViewWidth; x++)
        {
            var i = 0;
            while (ViewAngleToX[i] > x)
            {
                i++;
            }

            XToViewAngle[x] = (uint)((i << AngleTables.AngleToFineShift) - (int)AngleTables.Ang90);
        }

        // Take out the fencepost cases from viewangletox.
        for (var i = 0; i < AngleTables.FineAngles / 2; i++)
        {
            var t = FixedPoint.FixedMul(AngleTables.FineTangent[i], focallength);
            t = CenterX - t;

            if (ViewAngleToX[i] == -1)
            {
                ViewAngleToX[i] = 0;
            }
            else if (ViewAngleToX[i] == ViewWidth + 1)
            {
                ViewAngleToX[i] = ViewWidth;
            }
        }

        ClipAngle = XToViewAngle[0];
    }

    /// <summary>
    /// R_InitLightTables (r_main.c:614-642): only inits the zlight
    /// table, because the scalelight table changes with view size.
    /// </summary>
    public void InitLightTables()
    {
        Log.Verbose("InitLightTables()");

        for (var i = 0; i < LightLevels; i++)
        {
            var startMap = ((LightLevels - 1 - i) * 2) * NumColormaps / LightLevels;
            ZLight[i] = new byte[MaxLightZ][];
            for (var j = 0; j < MaxLightZ; j++)
            {
                var scale = FixedPoint.FixedDiv(Video.ScreenWidth / 2 * FixedPoint.FracUnit, (j + 1) << LightZShift);
                scale >>= LightScaleShift;
                var level = startMap - scale / DistMap;

                if (level < 0)
                {
                    level = 0;
                }

                if (level >= NumColormaps)
                {
                    level = NumColormaps - 1;
                }

                ZLight[i][j] = GetColormapSlice(level);
            }
        }
    }

    /// <summary>
    /// R_SetupFrame (r_main.c:830-863): set view position, angle,
    /// sin/cos, fixed colormap and counters for a new frame.
    /// </summary>
    public void SetupFrame(int viewX, int viewY, int viewZ, uint viewAngle, int playerFixedColormap, int playerExtraLight)
    {
        Log.Verbose(
            "SetupFrame(view=({ViewX}, {ViewY}, {ViewZ}) angle={ViewAngle} fixedcm={FixedCm} extra={ExtraLight})",
            viewX, viewY, viewZ, viewAngle, playerFixedColormap, playerExtraLight);

        RendererMath.ViewX = viewX;
        RendererMath.ViewY = viewY;
        RendererMath.ViewAngle = viewAngle + (uint)RendererMath.ViewAngleOffset;
        RendererMath.ExtraLight = playerExtraLight;
        RendererMath.ViewZ = viewZ;

        RendererMath.ViewSin = AngleTables.FineSine[(int)(viewAngle >> AngleTables.AngleToFineShift)];
        RendererMath.ViewCos = AngleTables.FineCosine[(int)(viewAngle >> AngleTables.AngleToFineShift)];

        RendererMath.SsCount = 0;

        if (playerFixedColormap != 0)
        {
            RendererMath.FixedColormap = GetColormapSlice(playerFixedColormap);

            for (var i = 0; i < MaxLightScale; i++)
            {
                ScaleLightFixed[i] = RendererMath.FixedColormap;
            }
        }
        else
        {
            RendererMath.FixedColormap = null;
        }

        RendererMath.FrameCount++;
        RendererMath.ValidCount++;
    }

    /// <summary>
    /// R_InitBuffer (r_draw.c:695-720): ylookup/columnofs LUTs and
    /// view window position.
    /// </summary>
    public void InitBuffer(int width, int height)
    {
        Log.Verbose("InitBuffer({Width}, {Height})", width, height);

        // Handle resize, e.g. smaller view windows with border/status bar.
        ViewWindowX = (Video.ScreenWidth - width) >> 1;

        // Column offset. For windows.
        var columnOfs = new int[width];
        for (var i = 0; i < width; i++)
        {
            columnOfs[i] = ViewWindowX + i;
        }
        ColumnOfs = columnOfs;

        // Same with base row offset.
        ViewWindowY = width == Video.ScreenWidth
            ? 0
            : (Video.ScreenHeight - Video.SBarHeight - height) >> 1;

        // Pre-calculate all row offsets.
        var yLookup = new int[height];
        for (var i = 0; i < height; i++)
        {
            yLookup[i] = (i + ViewWindowY) * Video.ScreenWidth;
        }
        YLookup = yLookup;
    }

    /// <summary>
    /// R_InitTranslationTables (r_draw.c:459-483): 3x256 translation
    /// table; green ramp 0x70..0x7f maps to 0x60/0x40/0x20 + (i&amp;0xf).
    /// The 256-byte alignment is an allocator detail (documented
    /// deviation).
    /// </summary>
    public void InitTranslationTables()
    {
        Log.Verbose("InitTranslationTables()");

        var tables = new byte[256 * 3];

        // translate just the 16 green colors
        for (var i = 0; i < 256; i++)
        {
            if (i >= 0x70 && i <= 0x7f)
            {
                // map green ramp to gray, brown, red
                tables[i] = (byte)(0x60 + (i & 0xf));
                tables[i + 256] = (byte)(0x40 + (i & 0xf));
                tables[i + 512] = (byte)(0x20 + (i & 0xf));
            }
            else
            {
                // Keep all other colors as is.
                tables[i] = (byte)i;
                tables[i + 256] = (byte)i;
                tables[i + 512] = (byte)i;
            }
        }

        TranslationTables = tables;
    }

    /// <summary>colormaps accessor (set from TextureManager).</summary>
    public byte[] Colormaps
    {
        get => _colormaps;
        set
        {
            _colormaps = value;
            Log.Verbose("Colormaps set ({Length} bytes)", value.Length);
        }
    }

    /// <summary>
    /// Global access to the COLORMAP lump for drawers (the C uses the
    /// global colormaps variable from r_data.c).
    /// </summary>
    public static byte[] ColormapsGlobal { get; set; } = [];

    /// <summary>colormaps + level*256 (r_main.c:639, 758, 850-851).</summary>
    private byte[] GetColormapSlice(int level)
    {
        var offset = level * 256;
        if (offset + 256 > _colormaps.Length)
        {
            throw new DoomFatalException($"colormap level {level} out of range");
        }

        return _colormaps[offset..(offset + 256)];
    }
}

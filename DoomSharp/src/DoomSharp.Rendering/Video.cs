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
//      320x200 8-bit framebuffer utilities.
//      Migration of Legacy/linuxdoom-1.10/v_video.c: V_Init,
//      V_DrawPatch, V_DrawPatchFlipped, V_DrawPatchDirect, V_CopyRect,
//      V_DrawBlock, V_GetBlock, V_MarkRect, gammatable.
//      See docs/adr/ADR-004 for tracing; DOOM-030b plan for the
//      documented deviations.
//
//-----------------------------------------------------------------------------

using System.Buffers.Binary;
using DoomSharp.Core;
using DoomSharp.Data;
using Serilog;
using Serilog.Events;

namespace DoomSharp.Rendering;

/// <summary>
/// Video buffer management (v_video.c): five 320x200 8-bit screen
/// buffers, patch blitting, block copy, dirty-rect marking, and the
/// gamma LUT.
/// </summary>
public static class Video
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(Video));

    /// <summary>SCREENWIDTH (doomdef.h).</summary>
    public const int ScreenWidth = 320;

    /// <summary>SCREENHEIGHT (doomdef.h).</summary>
    public const int ScreenHeight = 200;

    /// <summary>SBARHEIGHT (r_draw.c:51) — status bar height.</summary>
    public const int SBarHeight = 32;

    /// <summary>Each screen is [SCREENWIDTH*SCREENHEIGHT] (v_video.c:44).</summary>
    public const int ScreenSize = ScreenWidth * ScreenHeight;

    /// <summary>dirtybox (v_video.c:46) — BOXLEFT/BOXTOP/BOXRIGHT/BOXBOTTOM.</summary>
    public static readonly int[] DirtyBox = new int[4];

    /// <summary>usegamma (v_video.c:137).</summary>
    public static int UseGamma;

    /// <summary>screens[5] (v_video.c:44) — screen 0 is the display buffer, 1 is an extra buffer.</summary>
    public static byte[][] Screens { get; } = new byte[5][];

    /// <summary>gammatable[5][256] (v_video.c:51-133) — verbatim from the original.</summary>
    public static readonly byte[][] GammaTable = BuildGammaTable();

    /// <summary>
    /// V_Init (v_video.c:482-493): allocate the four screen buffers
    /// (the C uses one I_AllocLow block; managed arrays are the
    /// documented deviation).
    /// </summary>
    public static void Init()
    {
        Log.Verbose("V_Init()");

        for (var i = 0; i < 4; i++)
        {
            Screens[i] = new byte[ScreenSize];
        }
    }

    /// <summary>
    /// V_MarkRect (v_video.c:142-151): expand dirtybox to enclose the
    /// rectangle.
    /// </summary>
    public static void MarkRect(int x, int y, int width, int height)
    {
        RendererMath.AddPointToBox(x, y, DirtyBox);
        RendererMath.AddPointToBox(x + width - 1, y + height - 1, DirtyBox);
    }

    /// <summary>
    /// V_DrawPatch (v_video.c:203-263): masks a column-based masked pic
    /// to the screen. Post transparency: 0xff topdelta terminates a
    /// column; pixels are blitted per post. Out-of-range patches are
    /// ignored (TNT.WAD behavior, v_video.c:221-233) — not fatal.
    /// </summary>
    public static void DrawPatch(int x, int y, int scrn, Patch patch)
    {
        y -= patch.TopOffset;
        x -= patch.LeftOffset;

        if (x < 0
            || x + patch.Width > ScreenWidth
            || y < 0
            || y + patch.Height > ScreenHeight
            || (uint)scrn > 4)
        {
            // No I_Error abort - what is up with TNT.WAD?
            Console.Error.WriteLine($"Patch at {x},{y} exceeds LFB");
            Console.Error.WriteLine("V_DrawPatch: bad patch (ignored)");
            return;
        }

        if (scrn == 0)
        {
            MarkRect(x, y, patch.Width, patch.Height);
        }

        var destTop = y * ScreenWidth + x;
        var w = patch.Width;

        for (var col = 0; col < w; x++, col++, destTop++)
        {
            var column = patch.Column(col);

            // step through the posts in a column
            while (column.TopDelta != 0xff)
            {
                var source = column.Pixels;
                var srcOfs = column.PixelOfs;
                var dest = destTop + column.TopDelta * ScreenWidth;
                var count = column.Length;

                while (count-- > 0)
                {
                    Screens[scrn][dest] = source[srcOfs++];
                    dest += ScreenWidth;
                }

                column = column.Next();
            }
        }
    }

    /// <summary>
    /// V_DrawPatchFlipped (v_video.c:270-328): horizontally mirrored
    /// patch (e.g. to mirror face). Column order w-1-col.
    /// </summary>
    public static void DrawPatchFlipped(int x, int y, int scrn, Patch patch)
    {
        y -= patch.TopOffset;
        x -= patch.LeftOffset;

        if (x < 0
            || x + patch.Width > ScreenWidth
            || y < 0
            || y + patch.Height > ScreenHeight
            || (uint)scrn > 4)
        {
            Console.Error.WriteLine($"Patch origin {x},{y} exceeds LFB");
            throw new DoomFatalException("Bad V_DrawPatch in V_DrawPatchFlipped");
        }

        if (scrn == 0)
        {
            MarkRect(x, y, patch.Width, patch.Height);
        }

        var destTop = y * ScreenWidth + x;
        var w = patch.Width;

        for (var col = 0; col < w; x++, col++, destTop++)
        {
            var column = patch.Column(w - 1 - col);

            // step through the posts in a column
            while (column.TopDelta != 0xff)
            {
                var source = column.Pixels;
                var srcOfs = column.PixelOfs;
                var dest = destTop + column.TopDelta * ScreenWidth;
                var count = column.Length;

                while (count-- > 0)
                {
                    Screens[scrn][dest] = source[srcOfs++];
                    dest += ScreenWidth;
                }

                column = column.Next();
            }
        }
    }

    /// <summary>
    /// V_DrawPatchDirect (v_video.c:336-396): draws directly to the
    /// screen; in the LFB build it just delegates to V_DrawPatch.
    /// </summary>
    public static void DrawPatchDirect(int x, int y, int scrn, Patch patch)
    {
        Log.Verbose("DrawPatchDirect({X}, {Y}, {Scrn})", x, y, scrn);
        DrawPatch(x, y, scrn, patch);
    }

    /// <summary>
    /// V_CopyRect (v_video.c:157-196): blit a rectangle between two
    /// screen buffers, row by row.
    /// </summary>
    public static void CopyRect(int srcX, int srcY, int srcScrn, int width, int height, int destX, int destY, int destScrn)
    {
        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("CopyRect(src=({SrcX},{SrcY}) {Width}x{Height} -> dst=({DestX},{DestY}) screens {SrcScrn}->{DestScrn})",
                srcX, srcY, width, height, destX, destY, srcScrn, destScrn);
        }

        if (srcX < 0
            || srcX + width > ScreenWidth
            || srcY < 0
            || srcY + height > ScreenHeight
            || destX < 0
            || destX + width > ScreenWidth
            || destY < 0
            || destY + height > ScreenHeight
            || (uint)srcScrn > 4
            || (uint)destScrn > 4)
        {
            throw new DoomFatalException("Bad V_CopyRect");
        }

        MarkRect(destX, destY, width, height);

        var src = srcY * ScreenWidth + srcX;
        var dest = destY * ScreenWidth + destX;

        for (; height > 0; height--)
        {
            Array.Copy(Screens[srcScrn], src, Screens[destScrn], dest, width);
            src += ScreenWidth;
            dest += ScreenWidth;
        }
    }

    /// <summary>
    /// V_DrawBlock (v_video.c:404-436): draw a linear block of pixels
    /// into the view buffer.
    /// </summary>
    public static void DrawBlock(int x, int y, int scrn, int width, int height, byte[] src)
    {
        Log.Verbose("DrawBlock({X}, {Y}, {Scrn}, {Width}, {Height})", x, y, scrn, width, height);

        if (x < 0
            || x + width > ScreenWidth
            || y < 0
            || y + height > ScreenHeight
            || (uint)scrn > 4)
        {
            throw new DoomFatalException("Bad V_DrawBlock");
        }

        MarkRect(x, y, width, height);

        var dest = y * ScreenWidth + x;
        var srcIdx = 0;

        while (height-- > 0)
        {
            Array.Copy(src, srcIdx, Screens[scrn], dest, width);
            srcIdx += width;
            dest += ScreenWidth;
        }
    }

    /// <summary>
    /// V_GetBlock (v_video.c:444-474): read a linear block of pixels
    /// from the view buffer.
    /// </summary>
    public static void GetBlock(int x, int y, int scrn, int width, int height, byte[] dest)
    {
        Log.Verbose("GetBlock({X}, {Y}, {Scrn}, {Width}, {Height})", x, y, scrn, width, height);

        if (x < 0
            || x + width > ScreenWidth
            || y < 0
            || y + height > ScreenHeight
            || (uint)scrn > 4)
        {
            throw new DoomFatalException("Bad V_DrawBlock");
        }

        var src = y * ScreenWidth + x;
        var destIdx = 0;

        while (height-- > 0)
        {
            Array.Copy(Screens[scrn], src, dest, destIdx, width);
            src += ScreenWidth;
            destIdx += width;
        }
    }

    /// <summary>Builds the 5x256 gamma table (v_video.c:51-133).</summary>
    private static byte[][] BuildGammaTable()
    {
        // gammatable[5][256] verbatim from v_video.c:51-133.
        return
        [
            [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,
             17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,
             33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,
             49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,
             65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,
             81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,
             97,98,99,100,101,102,103,104,105,106,107,108,109,110,111,112,
             113,114,115,116,117,118,119,120,121,122,123,124,125,126,127,128,
             128,129,130,131,132,133,134,135,136,137,138,139,140,141,142,143,
             144,145,146,147,148,149,150,151,152,153,154,155,156,157,158,159,
             160,161,162,163,164,165,166,167,168,169,170,171,172,173,174,175,
             176,177,178,179,180,181,182,183,184,185,186,187,188,189,190,191,
             192,193,194,195,196,197,198,199,200,201,202,203,204,205,206,207,
             208,209,210,211,212,213,214,215,216,217,218,219,220,221,222,223,
             224,225,226,227,228,229,230,231,232,233,234,235,236,237,238,239,
             240,241,242,243,244,245,246,247,248,249,250,251,252,253,254,255],

            [2,4,5,7,8,10,11,12,14,15,16,18,19,20,21,23,24,25,26,27,29,30,31,
             32,33,34,36,37,38,39,40,41,42,44,45,46,47,48,49,50,51,52,54,55,
             56,57,58,59,60,61,62,63,64,65,66,67,69,70,71,72,73,74,75,76,77,
             78,79,80,81,82,83,84,85,86,87,88,89,90,91,92,93,94,95,96,97,98,
             99,100,101,102,103,104,105,106,107,108,109,110,111,112,113,114,
             115,116,117,118,119,120,121,122,123,124,125,126,127,128,129,129,
             130,131,132,133,134,135,136,137,138,139,140,141,142,143,144,145,
             146,147,148,148,149,150,151,152,153,154,155,156,157,158,159,160,
             161,162,163,163,164,165,166,167,168,169,170,171,172,173,174,175,
             175,176,177,178,179,180,181,182,183,184,185,186,186,187,188,189,
             190,191,192,193,194,195,196,196,197,198,199,200,201,202,203,204,
             205,205,206,207,208,209,210,211,212,213,214,214,215,216,217,218,
             219,220,221,222,222,223,224,225,226,227,228,229,230,230,231,232,
             233,234,235,236,237,237,238,239,240,241,242,243,244,245,245,246,
             247,248,249,250,251,252,252,253,254,255],

            [4,7,9,11,13,15,17,19,21,22,24,26,27,29,30,32,33,35,36,38,39,40,42,
             43,45,46,47,48,50,51,52,54,55,56,57,59,60,61,62,63,65,66,67,68,69,
             70,72,73,74,75,76,77,78,79,80,82,83,84,85,86,87,88,89,90,91,92,93,
             94,95,96,97,98,100,101,102,103,104,105,106,107,108,109,110,111,112,
             113,114,114,115,116,117,118,119,120,121,122,123,124,125,126,127,128,
             129,130,131,132,133,133,134,135,136,137,138,139,140,141,142,143,144,
             144,145,146,147,148,149,150,151,152,153,153,154,155,156,157,158,159,
             160,160,161,162,163,164,165,166,166,167,168,169,170,171,172,172,173,
             174,175,176,177,178,178,179,180,181,182,183,183,184,185,186,187,188,
             188,189,190,191,192,193,193,194,195,196,197,197,198,199,200,201,201,
             202,203,204,205,206,206,207,208,209,210,210,211,212,213,213,214,215,
             216,217,217,218,219,220,221,221,222,223,224,224,225,226,227,228,228,
             229,230,231,231,232,233,234,235,235,236,237,238,238,239,240,241,241,
             242,243,244,244,245,246,247,247,248,249,250,251,251,252,253,254,254,
             255],

            [8,12,16,19,22,24,27,29,31,34,36,38,40,41,43,45,47,49,50,52,53,55,
             57,58,60,61,63,64,65,67,68,70,71,72,74,75,76,77,79,80,81,82,84,85,
             86,87,88,90,91,92,93,94,95,96,98,99,100,101,102,103,104,105,106,107,
             108,109,110,111,112,113,114,115,116,117,118,119,120,121,122,123,124,
             125,126,127,128,129,130,131,132,133,134,135,135,136,137,138,139,140,
             141,142,143,143,144,145,146,147,148,149,150,150,151,152,153,154,155,
             155,156,157,158,159,160,160,161,162,163,164,165,165,166,167,168,169,
             169,170,171,172,173,173,174,175,176,176,177,178,179,180,180,181,182,
             183,183,184,185,186,186,187,188,189,189,190,191,192,192,193,194,195,
             195,196,197,197,198,199,200,200,201,202,202,203,204,205,205,206,207,
             207,208,209,210,210,211,212,212,213,214,214,215,216,216,217,218,219,
             219,220,221,221,222,223,223,224,225,225,226,227,227,228,229,229,230,
             231,231,232,233,233,234,235,235,236,237,237,238,238,239,240,240,241,
             242,242,243,244,244,245,246,246,247,247,248,249,249,250,251,251,252,
             253,253,254,254,255],

            [16,23,28,32,36,39,42,45,48,50,53,55,57,60,62,64,66,68,69,71,73,75,76,
             78,80,81,83,84,86,87,89,90,92,93,94,96,97,98,100,101,102,103,105,106,
             107,108,109,110,112,113,114,115,116,117,118,119,120,121,122,123,124,
             125,126,128,128,129,130,131,132,133,134,135,136,137,138,139,140,141,
             142,143,143,144,145,146,147,148,149,150,150,151,152,153,154,155,155,
             156,157,158,159,159,160,161,162,163,163,164,165,166,166,167,168,169,
             169,170,171,172,172,173,174,175,175,176,177,177,178,179,180,180,181,
             182,182,183,184,184,185,186,187,187,188,189,189,190,191,191,192,193,
             193,194,195,195,196,196,197,198,198,199,200,200,201,202,202,203,203,
             204,205,205,206,207,207,208,208,209,210,210,211,211,212,213,213,214,
             214,215,216,216,217,217,218,219,219,220,220,221,221,222,223,223,224,
             224,225,225,226,227,227,228,228,229,229,230,230,231,232,232,233,233,
             234,234,235,235,236,236,237,237,238,239,239,240,240,241,241,242,242,
             243,243,244,244,245,245,246,246,247,247,248,248,249,249,250,250,251,
             251,252,252,253,254,254,255,255]
        ];
    }

    // ------------------------------------------------------------------
    // R_FillBackScreen / R_VideoErase / R_DrawViewBorder (r_draw.c:726-875)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_FillBackScreen (r_draw.c:731-811): fills the back screen with
    /// a pattern for variable screen sizes and draws a beveled edge.
    /// Uses FLOOR7_2 (DOOM) or GRNROCK (DOOM II) as the border flat.
    /// </summary>
    public static void FillBackScreen(ViewSetup view, TextureManager textures, WadDirectory wad, bool commercial)
    {
        if (view.ScaledViewWidth == ScreenWidth)
        {
            return;                                 // r_draw.c:747-748
        }

        Log.Verbose("FillBackScreen()");

        var name = commercial ? "GRNROCK" : "FLOOR7_2";   // r_draw.c:740-753
        var src = wad.CacheLumpName(name, PurgeTags.Cache);
        var dest = Screens[1];

        for (int y = 0, destOfs = 0; y < ScreenHeight - SBarHeight; y++)
        {
            for (var x = 0; x < ScreenWidth / 64; x++)
            {
                Array.Copy(src, (y & 63) << 6, dest, destOfs, 64);
                destOfs += 64;
            }
        }

        var patch = new Patch(wad.CacheLumpName("brdr_t", PurgeTags.Cache));
        for (var x = 0; x < view.ScaledViewWidth; x += 8)
        {
            DrawPatch(view.ViewWindowX + x, view.ViewWindowY - 8, 1, patch);
        }

        patch = new Patch(wad.CacheLumpName("brdr_b", PurgeTags.Cache));
        for (var x = 0; x < view.ScaledViewWidth; x += 8)
        {
            DrawPatch(view.ViewWindowX + x, view.ViewWindowY + view.ViewHeight, 1, patch);
        }

        patch = new Patch(wad.CacheLumpName("brdr_l", PurgeTags.Cache));
        for (var y = 0; y < view.ViewHeight; y += 8)
        {
            DrawPatch(view.ViewWindowX - 8, view.ViewWindowY + y, 1, patch);
        }

        patch = new Patch(wad.CacheLumpName("brdr_r", PurgeTags.Cache));
        for (var y = 0; y < view.ViewHeight; y += 8)
        {
            DrawPatch(view.ViewWindowX + view.ScaledViewWidth, view.ViewWindowY + y, 1, patch);
        }

        // Draw beveled edge (r_draw.c:791-810).
        DrawPatch(view.ViewWindowX - 8, view.ViewWindowY - 8, 1,
            new Patch(wad.CacheLumpName("brdr_tl", PurgeTags.Cache)));
        DrawPatch(view.ViewWindowX + view.ScaledViewWidth, view.ViewWindowY - 8, 1,
            new Patch(wad.CacheLumpName("brdr_tr", PurgeTags.Cache)));
        DrawPatch(view.ViewWindowX - 8, view.ViewWindowY + view.ViewHeight, 1,
            new Patch(wad.CacheLumpName("brdr_bl", PurgeTags.Cache)));
        DrawPatch(view.ViewWindowX + view.ScaledViewWidth, view.ViewWindowY + view.ViewHeight, 1,
            new Patch(wad.CacheLumpName("brdr_br", PurgeTags.Cache)));
    }

    /// <summary>
    /// R_VideoErase (r_draw.c:818-828): copy a screen buffer range from
    /// the back screen to the display screen.
    /// </summary>
    public static void VideoErase(int ofs, int count)
    {
        Array.Copy(Screens[1], ofs, Screens[0], ofs, count);
    }

    /// <summary>
    /// R_DrawViewBorder (r_draw.c:843-875): draws the border around the
    /// view for different size windows.
    /// </summary>
    public static void DrawViewBorder(ViewSetup view)
    {
        if (view.ScaledViewWidth == ScreenWidth)
        {
            return;                                 // r_draw.c:850-851
        }

        Log.Verbose("DrawViewBorder()");

        var top = ((ScreenHeight - SBarHeight) - view.ViewHeight) / 2;
        var side = (ScreenWidth - view.ScaledViewWidth) / 2;

        // copy top and one line of left side (r_draw.c:856-857)
        VideoErase(0, (top * ScreenWidth) + side);

        // copy one line of right side and bottom (r_draw.c:859-861)
        var ofs = ((view.ViewHeight + top) * ScreenWidth) - side;
        VideoErase(ofs, (top * ScreenWidth) + side);

        // copy sides using wraparound (r_draw.c:863-871)
        ofs = (top * ScreenWidth) + ScreenWidth - side;
        side <<= 1;

        for (var i = 1; i < view.ViewHeight; i++)
        {
            VideoErase(ofs, side);
            ofs += ScreenWidth;
        }

        MarkRect(0, 0, ScreenWidth, ScreenHeight - SBarHeight);   // r_draw.c:874
    }
}

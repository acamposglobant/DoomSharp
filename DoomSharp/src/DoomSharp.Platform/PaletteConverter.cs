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
//      PLAYPAL palette handling and the indexed-to-RGBA blit
//      (I_SetPalette / UploadNewPalette, i_video.c:530-585).
//      The C expanded each palette channel through gammatable[usegamma]
//      and uploaded to the X colormap; the managed presenter expands
//      the 320x200 indexed framebuffer into an RGBA8888 buffer with the
//      same gamma pass. Pure managed code — headless testable.
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Platform;

/// <summary>
/// Converts the 320×200 8-bit indexed framebuffer (Video.Screens[0])
/// to RGBA8888 using a PLAYPAL palette slice and the gamma table
/// (UploadNewPalette, i_video.c:560-571).
/// </summary>
public static class PaletteConverter
{
    /// <summary>Bytes per palette entry (RGB).</summary>
    public const int PaletteEntrySize = 3;

    /// <summary>PLAYPAL palettes (d_main.c: I_SetPalette call sites).</summary>
    public const int PaletteCount = 14;

    /// <summary>Bytes of one full PLAYPAL palette.</summary>
    public const int PaletteSize = 256 * PaletteEntrySize;

    /// <summary>Framebuffer dimensions (SCREENWIDTH/SCREENHEIGHT).</summary>
    public const int ScreenWidth = 320;
    public const int ScreenHeight = 200;

    /// <summary>
    /// Converts an indexed framebuffer to RGBA8888. Each palette channel
    /// passes through <c>gamma[c]</c> before expansion — the exact
    /// UploadNewPalette order (i_video.c:565-570: c = gammatable[usegamma]
    /// [*palette++]). Output is 4 bytes per pixel, R,G,B,255.
    /// </summary>
    /// <param name="indexed">320×200 framebuffer (Video.Screens[0]).</param>
    /// <param name="palette">768-byte palette slice (one PLAYPAL entry).</param>
    /// <param name="gamma">256-byte gamma row (Video.GammaTable[usegamma]).</param>
    /// <param name="rgba">Destination buffer (320*200*4 bytes), reused across frames.</param>
    public static void Convert(ReadOnlySpan<byte> indexed, ReadOnlySpan<byte> palette, ReadOnlySpan<byte> gamma, Span<byte> rgba)
    {
        if (indexed.Length < ScreenWidth * ScreenHeight)
        {
            throw new ArgumentException($"indexed buffer too small: {indexed.Length}", nameof(indexed));
        }

        if (palette.Length < PaletteSize)
        {
            throw new ArgumentException($"palette too small: {palette.Length}", nameof(palette));
        }

        if (gamma.Length < 256)
        {
            throw new ArgumentException($"gamma table too small: {gamma.Length}", nameof(gamma));
        }

        if (rgba.Length < ScreenWidth * ScreenHeight * 4)
        {
            throw new ArgumentException($"rgba buffer too small: {rgba.Length}", nameof(rgba));
        }

        for (var i = 0; i < ScreenWidth * ScreenHeight; i++)
        {
            var c = indexed[i] * PaletteEntrySize;
            rgba[i * 4] = gamma[palette[c]];
            rgba[(i * 4) + 1] = gamma[palette[c + 1]];
            rgba[(i * 4) + 2] = gamma[palette[c + 2]];
            rgba[(i * 4) + 3] = 0xff;
        }
    }

    /// <summary>
    /// Returns the palette slice for <paramref name="index"/> from a full
    /// PLAYPAL lump (14 × 768 bytes): palette 0 = normal, 1-8 pain reds,
    /// 9-12 bonus golds, 13 radiation suit green.
    /// </summary>
    public static byte[] GetPalette(ReadOnlySpan<byte> playpal, int index)
    {
        if (index < 0 || index >= PaletteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"palette index {index} out of range 0..{PaletteCount - 1}");
        }

        if (playpal.Length < PaletteCount * PaletteSize)
        {
            throw new ArgumentException($"PLAYPAL lump too small: {playpal.Length}", nameof(playpal));
        }

        return playpal.Slice(index * PaletteSize, PaletteSize).ToArray();
    }
}

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
//      Headless IFramePresenter: records presented frames for
//      inspection in unit tests. No display server required.
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Platform;

/// <summary>
/// Test double for <see cref="IFramePresenter"/>: records every
/// presented frame so tests can assert on the converted RGBA output
/// without a display server.
/// </summary>
public sealed class HeadlessFramePresenter : IFramePresenter
{
    private readonly List<byte[]> _frames = [];

    /// <summary>Frames presented so far (copies of the RGBA buffers).</summary>
    public IReadOnlyList<byte[]> Frames => _frames;

    /// <inheritdoc />
    public int PresentWidth { get; }

    /// <inheritdoc />
    public int PresentHeight { get; }

    /// <summary>Creates a headless presenter at the given present size.</summary>
    public HeadlessFramePresenter(int presentWidth = PaletteConverter.ScreenWidth, int presentHeight = PaletteConverter.ScreenHeight)
    {
        PresentWidth = presentWidth;
        PresentHeight = presentHeight;
    }

    /// <inheritdoc />
    public void Present(ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length < PaletteConverter.ScreenWidth * PaletteConverter.ScreenHeight * 4)
        {
            throw new ArgumentException($"rgba buffer too small: {rgba.Length}", nameof(rgba));
        }

        _frames.Add(rgba[..(PaletteConverter.ScreenWidth * PaletteConverter.ScreenHeight * 4)].ToArray());
    }

    /// <inheritdoc />
    public void PumpEvents()
    {
        // Headless: no OS event loop.
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

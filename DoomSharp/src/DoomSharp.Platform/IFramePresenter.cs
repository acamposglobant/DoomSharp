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
//      Frame presentation seam (I_InitGraphics / I_FinishUpdate,
//      i_video.c). The window adapter (Silk.NET) and the headless test
//      double both implement this; the game loop only sees the seam.
//
//-----------------------------------------------------------------------------

using System;

namespace DoomSharp.Platform;

/// <summary>
/// Presents a 320×200 RGBA8888 frame. Implemented by the Silk.NET
/// window adapter (interactive) and <see cref="HeadlessFramePresenter"/>
/// (tests / CI without a display server).
/// </summary>
public interface IFramePresenter : IDisposable
{
    /// <summary>Window/present width in pixels (scaled).</summary>
    int PresentWidth { get; }

    /// <summary>Window/present height in pixels (scaled).</summary>
    int PresentHeight { get; }

    /// <summary>
    /// Blits one 320×200 RGBA8888 frame (320*200*4 bytes). Implementations
    /// must not retain the buffer beyond the call.
    /// </summary>
    void Present(ReadOnlySpan<byte> rgba);

    /// <summary>Pumps the OS event loop (no-op headless).</summary>
    void PumpEvents();
}

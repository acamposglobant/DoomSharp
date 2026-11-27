// Emacs style mode select   -*- C# -*-
//-----------------------------------------------------------------------------
//
// Copyright (C) 1993-1996 by id Software, Inc.
//
// This source is available for distribution and/or modification
// only under the terms of the GNU General Public License 2.0
// as published by the Free Software Foundation.
//
// DESCRIPTION:
//      Input source seam: the window adapter feeds translated DoomEvents
//      into the EventQueue; the game loop drains it. Headless tests
//      post synthetic events directly.
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Platform;

/// <summary>
/// Produces DoomEvents from the OS (I_GetEvent, i_video.c:194-260).
/// The Silk.NET adapter implements this; tests use a queue directly.
/// </summary>
public interface IInputSource
{
    /// <summary>Polls the OS and posts translated events to the queue.</summary>
    void Poll(EventQueue queue);
}

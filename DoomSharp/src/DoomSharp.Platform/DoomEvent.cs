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
//      Input event types and structure (d_event.h:34-50).
//      DOOM-040a: platform events are translated into DoomEvent records
//      and posted to the EventQueue exactly as the C game loop consumed
//      event_t records from the events[] ring.
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Platform;

/// <summary>evtype_t (d_event.h:35-41).</summary>
public enum EventType
{
    KeyDown,
    KeyUp,
    Mouse,
    Joystick,
}

/// <summary>
/// event_t (d_event.h:43-50): data1 = keys / mouse/joystick buttons,
/// data2 = mouse/joystick x move, data3 = mouse/joystick y move.
/// </summary>
public readonly record struct DoomEvent(EventType Type, int Data1, int Data2, int Data3);

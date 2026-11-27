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
//      Event ring buffer (d_event.h:108-112, d_main.c:141-154, 161-176).
//      D_PostEvent writes at eventhead and advances with wraparound;
//      D_ProcessEvents drains from eventtail until it catches up. When
//      the producer laps the consumer the C overwrites unread events
//      (head catches tail) — that behavior is preserved.
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Platform;

/// <summary>
/// events[MAXEVENTS] ring (d_main.c:141-154). MAXEVENTS = 64
/// (d_event.h:108); the ring indices wrap with &amp;(MAXEVENTS-1).
/// </summary>
public sealed class EventQueue
{
    /// <summary>MAXEVENTS (d_event.h:108).</summary>
    public const int MaxEvents = 64;

    private readonly DoomEvent[] _events = new DoomEvent[MaxEvents];
    private int _head;
    private int _tail;

    /// <summary>Number of events waiting to be drained.</summary>
    public int Count
    {
        get
        {
            unchecked
            {
                return (_head - _tail) & (MaxEvents - 1);
            }
        }
    }

    /// <summary>D_PostEvent (d_main.c:150-154).</summary>
    public void PostEvent(in DoomEvent ev)
    {
        _events[_head] = ev;
        _head = (_head + 1) & (MaxEvents - 1);
    }

    /// <summary>
    /// D_ProcessEvents drain step (d_main.c:170): pops the next event
    /// in FIFO order; false when the queue is empty (tail == head).
    /// </summary>
    public bool TryPop(out DoomEvent ev)
    {
        if (_tail == _head)
        {
            ev = default;
            return false;
        }

        ev = _events[_tail];
        _tail = (_tail + 1) & (MaxEvents - 1);
        return true;
    }

    /// <summary>Empties the queue (tail catches head).</summary>
    public void Clear() => _tail = _head;
}

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
//      Platform boundary: clock, fatal error, quit.
//      Migration of Legacy/linuxdoom-1.10/i_system.h / i_system.c
//      (I_GetTime, I_WaitVBL, I_Error, I_Quit). See ADR-002 for the
//      fatal-error contract.
//
//-----------------------------------------------------------------------------

using System.Diagnostics;

namespace DoomSharp.Platform;

/// <summary>
/// Tic rate of the original engine (doomdef.h:122).
/// </summary>
public static class TimeConstants
{
    /// <summary>TICRATE (doomdef.h:122): game tics per second.</summary>
    public const int TicRate = 35;

    /// <summary>VBL rate used by I_WaitVBL (i_system.c:134: 1000000/70).</summary>
    public const int VblRate = 70;

    /// <summary>Microseconds per VBL unit (integer constant 1000000/70).</summary>
    public const int MicrosPerVbl = 1_000_000 / 70; // 14285
}

/// <summary>
/// Platform clock boundary — I_GetTime and I_WaitVBL
/// (i_system.h:43-45, i_system.c:88-137).
/// </summary>
public interface IClock
{
    /// <summary>
    /// I_GetTime (i_system.c:88-100): time in 1/35s tics since the
    /// first call. Anchors on first call (the C static basetime).
    /// </summary>
    int GetTime();

    /// <summary>
    /// I_WaitVBL (i_system.c:126-137): waits count VBL periods
    /// (count * 14285 microseconds at the 70 Hz VBL rate).
    /// </summary>
    void WaitVbl(int count);
}

/// <summary>
/// Host boundary for the I_Error fatal path (i_system.c:162-183).
/// The library formats the message and throws DoomFatalException
/// (ADR-002); the host implementation decides how to surface it and
/// terminate with a non-success exit code.
/// </summary>
public interface IErrorHandler
{
    /// <summary>Handles a fatal error. Must not return normally in a running game.</summary>
    void Fatal(string message);
}

/// <summary>
/// Host boundary for the I_Quit path (i_system.c:116-124): orderly
/// shutdown, then successful process termination.
/// </summary>
public interface IQuitHandler
{
    /// <summary>Performs orderly shutdown and terminates with exit code 0.</summary>
    void Quit();
}

/// <summary>
/// Real wall-clock implementation of IClock using a monotonic
/// high-resolution source (Stopwatch/QPC — immune to wall-clock jumps).
/// Mirrors I_GetTime (i_system.c:88-100): anchors on the first call and
/// computes (sec * 35) + (usec * 35 / 1_000_000) with the exact integer
/// truncation of the original, in 64-bit arithmetic.
/// </summary>
public sealed class SystemClock : IClock
{
    private long _baseTimestamp;
    private bool _anchored;

    public int GetTime()
    {
        var now = Stopwatch.GetTimestamp();

        if (!_anchored)
        {
            // i_system.c:96-97 — basetime anchors on the FIRST call,
            // so the first GetTime() returns ~0 tics.
            _baseTimestamp = now;
            _anchored = true;
        }

        // 64-bit elapsed microseconds (no 32-bit overflow).
        var elapsedUs = (now - _baseTimestamp) * 1_000_000L / Stopwatch.Frequency;

        // Exact original expression: (sec * TICRATE) + (usec * TICRATE / 1000000),
        // integer truncation on the sub-second part (i_system.c:98).
        var seconds = elapsedUs / 1_000_000L;
        var usec = elapsedUs % 1_000_000L;
        return (int)(seconds * TimeConstants.TicRate + usec * TimeConstants.TicRate / 1_000_000L);
    }

    public void WaitVbl(int count)
    {
        // i_system.c:134 — usleep(count * (1000000/70)).
        var microseconds = (long)count * (1_000_000 / 70);
        if (microseconds <= 0)
        {
            return;
        }
        // Sleep in whole milliseconds (sub-ms remainder spun, like a
        // busy-wait refinement of usleep; tests never hit this path).
        var milliseconds = (int)(microseconds / 1000);
        if (milliseconds > 0)
        {
            Thread.Sleep(milliseconds);
        }
        var remainingTicks = (long)(microseconds % 1000) * Stopwatch.Frequency / 1_000_000L;
        if (remainingTicks > 0)
        {
            var spinStart = Stopwatch.GetTimestamp();
            while (Stopwatch.GetTimestamp() - spinStart < remainingTicks) { }
        }
    }
}

/// <summary>
/// Deterministic clock for tests and replays: time is set explicitly
/// in microseconds; WaitVbl records requests instead of sleeping.
/// Mirrors the anchor-on-first-call semantics of I_GetTime.
/// </summary>
public sealed class FakeClock : IClock
{
    private long _baseUs;
    private bool _anchored;

    /// <summary>Current fake time in microseconds since an arbitrary epoch.</summary>
    public long CurrentTimeUs { get; set; }

    /// <summary>VBL waits recorded as (count, microseconds) pairs.</summary>
    public List<(int Count, long Microseconds)> VblWaits { get; } = [];

    public int GetTime()
    {
        if (!_anchored)
        {
            // i_system.c:96-97 — anchor on first call.
            _baseUs = CurrentTimeUs;
            _anchored = true;
        }

        var elapsedUs = CurrentTimeUs - _baseUs;
        var seconds = elapsedUs / 1_000_000L;
        var usec = elapsedUs % 1_000_000L;
        return (int)(seconds * 35 + usec * 35 / 1_000_000L);
    }

    public void WaitVbl(int count)
    {
        VblWaits.Add((count, (long)count * (1_000_000 / 70)));
        // No real sleep — deterministic tests.
    }
}

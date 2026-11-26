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
//      Tests for the platform boundary (DOOM-010): I_GetTime tic
//      arithmetic (i_system.c:88-100), I_WaitVBL scaling
//      (i_system.c:126-137), and the I_Error/I_Quit lifecycle
//      (i_system.c:116-183). No C harness: I_GetTime reads
//      gettimeofday directly (unmockable without LD_PRELOAD); the
//      arithmetic is ported literally and tested exhaustively on
//      FakeClock boundary values instead (documented in backlog).
//
//-----------------------------------------------------------------------------

using System.Diagnostics;
using DoomSharp.Core;
using DoomSharp.Platform;
using Xunit;

namespace DoomSharp.Tests.Unit;

public class FakeClockTests
{
    [Fact]
    public void FirstCall_Anchors_AndReturnsZero()
    {
        // i_system.c:96-97 — basetime anchors on the FIRST call.
        var clock = new FakeClock { CurrentTimeUs = 123_456_789 };

        Assert.Equal(0, clock.GetTime());
    }

    [Fact]
    public void ExactTicBoundaries_TruncationMatchesC()
    {
        // C expression: (sec-base)*35 + usec*35/1000000 (truncating).
        var clock = new FakeClock();
        _ = clock.GetTime(); // anchor at 0

        // 999,999 us = 0.999999 s -> 34 tics (34.999... truncated)
        clock.CurrentTimeUs = 999_999;
        Assert.Equal(34, clock.GetTime());

        // 1,000,000 us = exactly 1 s -> 35 tics
        clock.CurrentTimeUs = 1_000_000;
        Assert.Equal(35, clock.GetTime());

        // 1,000,001 us -> still 35 tics (sub-tic truncated)
        clock.CurrentTimeUs = 1_000_001;
        Assert.Equal(35, clock.GetTime());

        // 1,028,571 us: 1*35 + 28571*35/1000000 = 35 + 0 = 35
        clock.CurrentTimeUs = 1_028_571;
        Assert.Equal(35, clock.GetTime());

        // 1,028,572 us -> 35 + 28572*35/1000000 = 35 + 1 = 36
        clock.CurrentTimeUs = 1_028_572;
        Assert.Equal(36, clock.GetTime());
    }

    [Fact]
    public void OneSecond_IsExactly35Tics()
    {
        var clock = new FakeClock();
        clock.GetTime(); // anchor

        clock.CurrentTimeUs = 1_000_000;
        Assert.Equal(35, clock.GetTime());

        clock.CurrentTimeUs = 2_000_000;
        Assert.Equal(70, clock.GetTime());
    }

    [Fact]
    public void SubTicRemainder_Truncates()
    {
        var clock = new FakeClock();
        clock.GetTime(); // anchor at 0

        // 1/35 s = 28571.428... us. At 28571 us: 28571*35/1000000 = 0 tics.
        clock.CurrentTimeUs = 28_571;
        Assert.Equal(0, clock.GetTime());

        // At 57142 us (2/35 s = 57142.857): 57142*35/1000000 = 1 (truncated)
        clock.CurrentTimeUs = 57_142;
        Assert.Equal(1, clock.GetTime());
    }

    [Fact]
    public void MultiHourUptime_NoOverflow()
    {
        // 3h 26m 40s = 12400 s -> 434000 tics; well within int range
        // (int tics overflow only after ~1.94 years).
        var clock = new FakeClock();
        clock.GetTime(); // anchor

        clock.CurrentTimeUs = 12_400L * 1_000_000;
        Assert.Equal(12_400 * 35, clock.GetTime());

        // 24 hours: 86400 s * 35 = 3,024,000 tics — fits int.
        var longClock = new FakeClock();
        longClock.GetTime();
        longClock.CurrentTimeUs = 86_400L * 1_000_000;
        Assert.Equal(3_024_000, longClock.GetTime());
    }

    [Fact]
    public void GetTime_IsMonotonic()
    {
        var clock = new FakeClock();
        clock.GetTime();

        var last = clock.GetTime();
        for (var us = 100_000; us <= 1_000_000; us += 100_000)
        {
            clock.CurrentTimeUs = us;
            var t = clock.GetTime();
            Assert.True(t >= last, $"non-monotonic at {us}us");
            last = t;
        }
    }

    [Fact]
    public void WaitVbl_RecordsScaledMicroseconds_NoRealSleep()
    {
        // i_system.c:134 — count * (1000000/70) = count * 14285 us.
        var clock = new FakeClock();

        clock.WaitVbl(1);
        clock.WaitVbl(4);
        clock.WaitVbl(0);

        Assert.Equal(3, clock.VblWaits.Count);
        Assert.Equal((1, 14_285L), clock.VblWaits[0]);
        Assert.Equal((4, 57_140L), clock.VblWaits[1]);
        Assert.Equal((0, 0L), clock.VblWaits[2]);
    }
}

public class SystemClockTests
{
    [Fact]
    public void FirstCall_ReturnsNearZero()
    {
        var clock = new SystemClock();
        var first = clock.GetTime();
        // Anchor-on-first-call: first value is 0 or a tiny positive
        // number (computation time between anchor and expression).
        Assert.InRange(first, 0, TimeConstants.TicRate);
    }

    [Fact]
    public void TicsIncrease_Monotonically()
    {
        var clock = new SystemClock();
        clock.GetTime(); // anchor

        var last = clock.GetTime();
        for (var i = 0; i < 10; i++)
        {
            Thread.Sleep(40); // > 1 tic (28.6 ms)
            var t = clock.GetTime();
            Assert.True(t > last, "tics must increase across 40ms gaps");
            last = t;
        }
    }

    [Fact]
    public void WaitVbl_ZeroCount_DoesNotThrow()
    {
        var clock = new SystemClock();
        clock.WaitVbl(0);
    }

    [Fact]
    public void WaitVbl_OneUnit_TakesAtLeastOneVblPeriod()
    {
        var clock = new SystemClock();
        var start = Stopwatch.GetTimestamp();
        clock.WaitVbl(1);
        var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        // 14285 us = 14.285 ms; Thread.Sleep granularity may add a bit.
        Assert.True(elapsedMs >= 14.0, $"expected >= 14ms, got {elapsedMs:F2}ms");
    }
}

public class DoomShutdownTests
{
    [Fact]
    public void Fatal_WritesErrorPrefixToStderr_AndThrows()
    {
        // i_system.c:168-173 — "Error: " + message + "\n" to stderr.
        var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);
        try
        {
            var ex = Assert.Throws<DoomFatalException>(
                () => DoomShutdown.Fatal("FixedDiv: divide by zero"));
            Assert.Equal("FixedDiv: divide by zero", ex.Message);
        }
        finally
        {
            Console.SetError(original);
        }

        var text = stderr.ToString();
        Assert.StartsWith("Error: ", text);
        Assert.Contains("FixedDiv: divide by zero", text);
        Assert.EndsWith("\n", text);
    }

    [Fact]
    public void Fatal_RunsShutdownCallbacks_ThenThrows()
    {
        var order = new List<string>();
        var r1 = DoomShutdown.Register(() => order.Add("net"));
        var r2 = DoomShutdown.Register(() => order.Add("graphics"));
        try
        {
            Assert.Throws<DoomFatalException>(() => DoomShutdown.Fatal("boom"));
            // LIFO: graphics (last registered) runs first.
            Assert.Equal(["graphics", "net"], order);
        }
        finally
        {
            r1.Dispose();
            r2.Dispose();
        }
    }

    [Fact]
    public void Quit_RunsShutdownCallbacks_LifoOrder()
    {
        // i_system.c:116-124 — orderly teardown; LIFO registration.
        var order = new List<string>();
        var r1 = DoomShutdown.Register(() => order.Add("net"));
        var r2 = DoomShutdown.Register(() => order.Add("music"));
        var r3 = DoomShutdown.Register(() => order.Add("graphics"));
        try
        {
            DoomShutdown.Quit();
            // LIFO: graphics, music, net.
            Assert.Equal(["graphics", "music", "net"], order);
        }
        finally
        {
            r1.Dispose();
            r2.Dispose();
            r3.Dispose();
        }
    }

    [Fact]
    public void Quit_ClearsRegistry_AfterRun()
    {
        var r = DoomShutdown.Register(() => { });
        try
        {
            DoomShutdown.Quit();
            Assert.Equal(0, DoomShutdown.Callbacks.Count); // cleared after run
        }
        finally
        {
            r.Dispose();
        }
    }

    [Fact]
    public void CallbackException_DoesNotPreventRemainingCallbacks()
    {
        var order = new List<string>();
        var r1 = DoomShutdown.Register(() => order.Add("first"));
        var r2 = DoomShutdown.Register(() => throw new InvalidOperationException("broken"));
        var r3 = DoomShutdown.Register(() => order.Add("last"));
        try
        {
            DoomShutdown.Quit();
            // LIFO: "last" runs first, throws, "first" still runs.
            Assert.Equal(["last", "first"], order);
        }
        finally
        {
            r1.Dispose();
            r3.Dispose();
        }
    }

    [Fact]
    public void RegistrationToken_RemovesCallback()
    {
        var calls = 0;
        var token = DoomShutdown.Register(() => calls++);
        token.Dispose();

        DoomShutdown.Quit();
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ShutdownRegistry_IsolatedInstances_DoNotInterfere()
    {
        // Test-safety refinement: instantiable registries keep
        // parallel tests isolated.
        var a = new ShutdownRegistry();
        var b = new ShutdownRegistry();
        var calls = new List<string>();

        var tokenA = a.Register(() => calls.Add("a"));
        var tokenB = b.Register(() => calls.Add("b"));

        a.RunAll();
        Assert.Equal(["a"], calls);

        b.RunAll();
        Assert.Equal(["a", "b"], calls);

        Assert.Equal(0, a.Count);
        Assert.Equal(0, b.Count);
        GC.KeepAlive(tokenA);
        GC.KeepAlive(tokenB);
    }

    [Fact]
    public void Fatal_ThrowsDoomFatalException_TestableWithoutKillingRunner()
    {
        // ADR-002: the fatal condition is observable as an exception;
        // the test runner survives.
        Assert.Throws<DoomFatalException>(() => DoomShutdown.Fatal("test"));
    }

    public DoomShutdownTests()
    {
        // Test isolation: clear any leaked callbacks from other tests.
        DoomShutdown.Callbacks.Clear();
    }
}

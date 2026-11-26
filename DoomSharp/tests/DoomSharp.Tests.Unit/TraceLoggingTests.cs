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
//      Tests for the ADR-004 trace logging mandate: Verbose events are
//      emitted when a sink is attached, and operations are silent and
//      behaviorally identical by default (no sink).
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Platform;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace DoomSharp.Tests.Unit;

/// <summary>
/// Serializes tests that swap the process-global Serilog logger —
/// parallel test classes emit into whatever sink is currently
/// installed, so logger-mutating tests must not overlap.
/// </summary>
public static class LogTestSync
{
    public static readonly object Lock = new();
}

/// <summary>
/// In-memory sink capturing events for assertions. Thread-safe: the
/// static Serilog logger is process-global, so parallel tests emit
/// into the same sink.
/// </summary>
public sealed class CollectingSink : ILogEventSink
{
    private readonly object _lock = new();
    private readonly List<LogEvent> _events = [];

    /// <summary>Snapshot of captured events.</summary>
    public List<LogEvent> Events
    {
        get { lock (_lock) { return [.. _events]; } }
    }

    public void Emit(LogEvent logEvent)
    {
        lock (_lock)
        {
            _events.Add(logEvent);
        }
    }
}

public class TraceLoggingTests : IDisposable
{
    private readonly CollectingSink _sink = new();

    // NOTE: no constructor logger installation — the static logger is
    // process-global; each test configures it inside LogTestSync.Lock.

    [Fact]
    public void FixedPoint_EmitsVerboseEvents_WithSinkAttached()
    {
        lock (LogTestSync.Lock)
        {
            var sink = new CollectingSink();
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();

            try
            {
                FixedPoint.FixedMul(3 << 16, 2 << 16);
                FixedPoint.FixedDiv(1 << 16, 2);

                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("FixedMul"));
                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("FixedDiv"));
            }
            finally
            {
                ResetToSilent();
            }
        }
    }

    [Fact]
    public void DoomRandom_EmitsVerboseEvents()
    {
        lock (LogTestSync.Lock)
        {
            var sink = new CollectingSink();
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();

            try
            {
                var rng = new DoomRandom();
                rng.PRandom();
                rng.MRandom();
                rng.ClearRandom();

                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("PRandom"));
                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("MRandom"));
                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("ClearRandom"));
            }
            finally
            {
                ResetToSilent();
            }
        }
    }

    [Fact]
    public void ZoneMemory_EmitsVerboseEvents()
    {
        lock (LogTestSync.Lock)
        {
            var sink = new CollectingSink();
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();

            try
            {
                using var zone = new ZoneMemory();
                var block = zone.Allocate(new byte[64], PurgeTags.Static);
                zone.ChangeTag(block, PurgeTags.Level);
                zone.FreeTags(PurgeTags.Level, PurgeTags.Level);

                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("Allocate"));
                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("ChangeTag"));
                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("FreeTags"));
            }
            finally
            {
                ResetToSilent();
            }
        }
    }

    [Fact]
    public void WadDirectory_EmitsVerboseEvents()
    {
        lock (LogTestSync.Lock)
        {
            var sink = new CollectingSink();
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();

            var tempDir = Path.Combine(Path.GetTempPath(), "doomsharp_logtests", Guid.NewGuid().ToString("N"));
            try
            {
                WadTestFixtures.WriteFixtures(tempDir);
                using var dir = new WadDirectory();
                dir.InitMultipleFiles([Path.Combine(tempDir, "test_iwad.wad")]);
                _ = dir.CheckNumForName("E1M1");
                _ = dir.CacheLumpName("MAP01");

                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("InitMultipleFiles"));
                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("CheckNumForName"));
                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("CacheLumpName"));
            }
            finally
            {
                ResetToSilent();
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void SystemClock_EmitsVerboseEvents()
    {
        lock (LogTestSync.Lock)
        {
            var sink = new CollectingSink();
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();

            try
            {
                var clock = new SystemClock();
                _ = clock.GetTime();

                Assert.Contains(sink.Events, e => e.MessageTemplate.Text!.Contains("GetTime"));
            }
            finally
            {
                ResetToSilent();
            }
        }
    }

    [Fact]
    public void SilentByDefault_NoSink_NoOutput_NoBehaviorChange()
    {
        lock (LogTestSync.Lock)
        {
            // Default static logger: no sink configured -> silent.
            Serilog.Log.Logger = new Serilog.LoggerConfiguration().CreateLogger();

            // Operations behave identically with logging disabled.
            Assert.Equal(65536, FixedPoint.FixedMul(1 << 16, 1 << 16));
            var rng = new DoomRandom();
            rng.ClearRandom();
            Assert.Equal(0, rng.PRndIndex);
            Assert.Equal(8, rng.PRandom()); // first P_Random value (rndtable[1])

            Assert.Empty(_sink.Events);
        }
    }

    [Fact]
    public void FatalMinimumLevel_NoEventsEmitted()
    {
        lock (LogTestSync.Lock)
        {
            var sink = new CollectingSink();
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Fatal()
                .WriteTo.Sink(sink)
                .CreateLogger();

            try
            {
                var rng = new DoomRandom();
                rng.PRandom();
                FixedPoint.FixedMul(1 << 16, 1 << 16);

                Assert.Empty(sink.Events);
            }
            finally
            {
                ResetToSilent();
            }
        }
    }

    [Fact]
    public void StructuredProperties_AreCaptured()
    {
        lock (LogTestSync.Lock)
        {
            var sink = new CollectingSink();
            Serilog.Log.Logger = new Serilog.LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();

            try
            {
                FixedPoint.FixedMul(3 << 16, 2 << 16);

                var evt = sink.Events.First(e => e.MessageTemplate.Text!.Contains("FixedMul"));
                Assert.True(evt.Properties.ContainsKey("A"));
                Assert.True(evt.Properties.ContainsKey("B"));
                Assert.True(evt.Properties.ContainsKey("Result"));
            }
            finally
            {
                ResetToSilent();
            }
        }
    }

    private static void ResetToSilent()
    {
        Serilog.Log.Logger = new Serilog.LoggerConfiguration().CreateLogger();
    }

    // NOTE: no Dispose logger reset — it would run outside
    // LogTestSync.Lock and race with parallel logging tests. Each test
    // resets the logger in its own finally block.
    public void Dispose()
    {
    }
}

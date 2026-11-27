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
//      ADR-004 trace tests for the DOOM-040a platform slice: the
//      window host emits Verbose at initialization granularity and
//      never per-pixel during the palette blit.
//
//-----------------------------------------------------------------------------

using DoomSharp.Platform;
using Serilog;
using Serilog.Events;
using Xunit;

namespace DoomSharp.Tests.Unit;

public class PlatformTraceLoggingTests
{
    [Fact]
    public void PaletteConverter_IsSilentByDefault()
    {
        lock (LogTestSync.Lock)
        {
            var sink = new CollectingSink();
            Serilog.Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();
            try
            {
                var palette = new byte[PaletteConverter.PaletteSize];
                var gamma = new byte[256];
                var indexed = new byte[PaletteConverter.ScreenWidth * PaletteConverter.ScreenHeight];
                var rgba = new byte[PaletteConverter.ScreenWidth * PaletteConverter.ScreenHeight * 4];

                PaletteConverter.Convert(indexed, palette, gamma, rgba);

                // The blit is a hot path: no logging at any level.
                Assert.Empty(sink.Events);
            }
            finally
            {
                Serilog.Log.Logger = new LoggerConfiguration().CreateLogger();
            }
        }
    }

    [Fact]
    public void EventQueue_IsSilentByDefault()
    {
        lock (LogTestSync.Lock)
        {
            var sink = new CollectingSink();
            Serilog.Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();
            try
            {
                var queue = new EventQueue();
                queue.PostEvent(new DoomEvent(EventType.KeyDown, DoomKeys.Escape, 0, 0));
                queue.TryPop(out _);

                Assert.Empty(sink.Events);
            }
            finally
            {
                Serilog.Log.Logger = new LoggerConfiguration().CreateLogger();
            }
        }
    }
}

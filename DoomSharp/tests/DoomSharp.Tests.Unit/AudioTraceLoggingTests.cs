using System;
using DoomSharp.Platform;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace DoomSharp.Tests.Unit;

/// <summary>
/// ADR-004 trace tests for the audio path: sound start/stop/channel
/// allocation may log at Verbose; the per-sample mixing loop must never log.
/// </summary>
[Collection("RendererSerial")]
public class AudioTraceLoggingTests
{
    private sealed class CollectingSink : ILogEventSink
    {
        public System.Collections.Generic.List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [Fact]
    public void UpdateSound_IsSilentAtAllLevels()
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
                var mixer = new SoundMixer();
                var data = new byte[512];
                Array.Fill(data, DmxSound.Silence);
                mixer.AddSfx(SfxId.Pistol, data, 512, 100, 65536, 128, 0);

                var buffer = new short[SoundMixer.BufferSize];
                mixer.UpdateSound(buffer);

                // The mixing loop is the hottest path in the engine: no
                // logging at any level. Filter by SourceContext — other
                // test classes run in parallel and may log into the global
                // sink (same race as the DOOM-040a CI fix).
                Assert.DoesNotContain(sink.Events,
                    e => (e.Properties["SourceContext"] as ScalarValue)?.Value?.ToString()!.StartsWith("DoomSharp.Platform") == true);
            }
            finally
            {
                Serilog.Log.Logger = new LoggerConfiguration().CreateLogger();
            }
        }
    }

    [Fact]
    public void HeadlessAudioOutput_CapturesSubmittedBuffers()
    {
        var output = new HeadlessAudioOutput();

        var buffer = new short[] { 1, -2, 3, -4 };
        output.Submit(buffer);
        output.Submit(buffer);

        Assert.Equal(2, output.SubmitCount);
        Assert.Equal(4, output.Frames[0].Length);
        Assert.Equal(1, output.Frames[0][0]);
        Assert.Equal(-4, output.Frames[1][3]);
        Assert.False(output.IsLive);
    }

    [Fact]
    public void HeadlessAudioOutput_CopiesBufferNotReference()
    {
        var output = new HeadlessAudioOutput();
        var buffer = new short[] { 1, 2, 3 };

        output.Submit(buffer);
        buffer[0] = 999;

        Assert.Equal(1, output.Frames[0][0]);
    }

    [Fact]
    public void HeadlessAudioOutput_RejectsNothing_SubmitCountTracks()
    {
        var output = new HeadlessAudioOutput();
        for (int i = 0; i < 10; i++)
        {
            output.Submit(new short[SoundMixer.BufferSize]);
        }
        Assert.Equal(10, output.SubmitCount);
    }
}

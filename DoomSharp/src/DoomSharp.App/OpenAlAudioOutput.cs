using System;
using DoomSharp.Platform;
using Serilog;
using Silk.NET.OpenAL;

namespace DoomSharp.App;

/// <summary>
/// OpenAL streaming audio output — the live <see cref="IAudioOutput"/> for
/// desktop machines (ADR-006). Streams 11,025 Hz stereo 16-bit PCM into an
/// OpenAL source via queued buffers.
/// </summary>
/// <remarks>
/// Fails gracefully: when no audio device or context can be created
/// (headless machines, CI), construction logs a warning and
/// <see cref="IsLive"/> stays false; the app then falls back to
/// <see cref="HeadlessAudioOutput"/>.
/// </remarks>
public sealed unsafe class OpenAlAudioOutput : IAudioOutput
{
    private const int NumBuffers = 4;

    private AL? _al;
    private ALContext? _alc;
    private Device* _device;
    private Context* _context;
    private uint _source;
    private readonly uint[] _buffers = new uint[NumBuffers];
    private bool _initialized;

    /// <summary>True when a device and context were acquired successfully.</summary>
    public bool IsLive { get; private set; }

    /// <summary>
    /// Tries to acquire the default OpenAL device. Never throws — on failure
    /// the instance stays inert (<see cref="IsLive"/> == false) and the app
    /// falls back to a headless output.
    /// </summary>
    public static OpenAlAudioOutput TryCreate()
    {
        try
        {
            var output = new OpenAlAudioOutput();
            if (!output.IsLive)
            {
                Log.Warning("OpenAL unavailable; audio disabled (headless fallback).");
            }
            return output;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "OpenAL initialization failed ({Reason}); audio disabled.", ex.Message);
            return new OpenAlAudioOutput();
        }
    }

    private OpenAlAudioOutput()
    {
        var alc = ALContext.GetApi();
        var device = alc.OpenDevice(null);
        if (device == null)
        {
            Log.Warning("No OpenAL device (headless machine?); audio disabled.");
            return;
        }

        var context = alc.CreateContext(device, (int*)0);
        if (context is null || !alc.MakeContextCurrent(context))
        {
            Log.Warning("OpenAL context creation failed; audio disabled.");
            return;
        }

        _alc = alc;
        _device = device;
        _context = context;
        _al = AL.GetApi();

        try
        {
            var al = _al;

            _source = al.GenSource();
            fixed (uint* pBuffers = _buffers)
            {
                al.GenBuffers(NumBuffers, pBuffers);

                for (int i = 0; i < NumBuffers; i++)
                {
                    // Pre-fill each buffer with silence so the queue never starves.
                    var silence = new short[SoundMixer.SampleCount * 2];
                    fixed (short* p = silence)
                    {
                        al.BufferData(_buffers[i], BufferFormat.Stereo16, p,
                            silence.Length * sizeof(short), SoundMixer.SampleRate);
                    }
                    al.SourceQueueBuffers(_source, 1, pBuffers + i);
                }
            }

            al.SourcePlay(_source);
            _initialized = true;
            IsLive = true;
            Log.Information("OpenAL audio output live: {Rate} Hz stereo 16-bit, {Buffers} x {Frames} frames.",
                SoundMixer.SampleRate, NumBuffers, SoundMixer.SampleCount);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "OpenAL buffer setup failed; audio disabled.");
        }
    }

    /// <inheritdoc />
    public unsafe void Submit(ReadOnlySpan<short> stereoPcm)
    {
        if (!_initialized || _al is not { } al)
        {
            return;
        }

        // Recycle finished buffers if the queue has drained.
        al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);
        uint done = 0;
        while (processed > 0)
        {
            al.SourceUnqueueBuffers(_source, 1, &done);
            fixed (short* p = stereoPcm)
            {
                al.BufferData(done, BufferFormat.Stereo16, p,
                    stereoPcm.Length * sizeof(short), SoundMixer.SampleRate);
            }
            al.SourceQueueBuffers(_source, 1, &done);
            processed--;
        }

        // Keep the source playing (underruns stop it).
        al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
        if (state != (int)SourceState.Playing)
        {
            al.SourcePlay(_source);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_initialized)
        {
            return;
        }

        _al?.SourceStop(_source);
        if (_alc is { } alc)
        {
            alc.MakeContextCurrent((Context*)0);
            if (_context is not null)
            {
                alc.DestroyContext(_context);
            }
            if (_device is not null)
            {
                alc.CloseDevice(_device);
            }
        }
        _initialized = false;
        IsLive = false;
    }
}

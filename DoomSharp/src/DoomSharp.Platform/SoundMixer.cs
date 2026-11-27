using System;

namespace DoomSharp.Platform;

/// <summary>
/// The 8-channel software PCM mixer — port of the mixing half of
/// <c>i_sound.c</c> (<c>addsfx</c>, <c>I_UpdateSound</c>, <c>I_SetChannels</c>).
/// </summary>
/// <remarks>
/// <para>
/// Produces 11,025 Hz stereo interleaved signed 16-bit PCM in blocks of
/// <see cref="SampleCount"/> (512) sample pairs, exactly like the C
/// <c>mixbuffer</c> written to <c>/dev/dsp</c> by <c>I_SubmitSound</c>.
/// </para>
/// <para>
/// Channels are stepped through their raw unsigned 8-bit data with a 16-bit
/// fractional step accumulator; each sample is scaled through per-channel
/// left/right volume lookup rows (<c>vol_lookup</c>) and accumulated into the
/// frame, then clamped to the 16-bit range (i_sound.c:539-635).
/// </para>
/// </remarks>
public sealed class SoundMixer
{
    /// <summary>Samples per mixing buffer (SAMPLECOUNT, i_sound.c:93).</summary>
    public const int SampleCount = 512;

    /// <summary>Internal mixing channels (NUM_CHANNELS, i_sound.c:94).</summary>
    public const int NumChannels = 8;

    /// <summary>Output sample rate (SAMPLERATE, i_sound.c:99).</summary>
    public const int SampleRate = 11025;

    /// <summary>Stereo interleaved short count per buffer (SAMPLECOUNT*2; BUFMUL=4 bytes).</summary>
    public const int BufferSize = SampleCount * 2;

    /// <summary>Pitch step table size (steptable[256], mid index 128).</summary>
    public const int PitchSteps = 256;

    /// <summary>Volume levels (0..127) for the vol_lookup table.</summary>
    public const int VolumeLevels = 128;

    /// <summary>Number of raw sample values (unsigned 8-bit range).</summary>
    public const int SampleValues = 256;

    /// <summary>
    /// Pitch to stepping lookup (steptable, i_sound.c:414-415):
    /// <c>steptable[i] = (int)(pow(2.0, (i/64.0)) * 65536.0)</c> for i in
    /// -128..127, indexed here as i+128. Set but unused by the C mixing
    /// loop — preserved as data.
    /// </summary>
    public static readonly int[] StepTable = BuildStepTable();

    /// <summary>
    /// Volume lookup (vol_lookup, i_sound.c:421-423): converts unsigned 8-bit
    /// samples to signed and scales by volume level 0..127:
    /// <c>vol_lookup[i*256+j] = (i*(j-128)*256)/127</c>.
    /// </summary>
    public static readonly int[] VolLookup = BuildVolLookup();

    // Per-channel state (channels/channelsend/channelstep/channelstepremainder/
    // channelstart/channelhandles/channelids, i_sound.c:115-141). The C's
    // channels[i] pointer + channelsend[i] become (data, pos, end).
    private readonly byte[]?[] _channelData = new byte[NumChannels][];
    private readonly int[] _channelPos = new int[NumChannels];
    private readonly int[] _channelEnd = new int[NumChannels];
    private readonly int[] _channelStep = new int[NumChannels];
    private readonly int[] _channelStepRemainder = new int[NumChannels];
    private readonly int[] _channelStart = new int[NumChannels];
    private readonly int[] _channelHandles = new int[NumChannels];
    private readonly int[] _channelIds = new int[NumChannels];
    private readonly int[] _channelLeftVolBase = new int[NumChannels];
    private readonly int[] _channelRightVolBase = new int[NumChannels];

    private ushort _handleNums;
    private int _gameTic;

    /// <summary>Current game tic, used for oldest-channel eviction and the I_SoundIsPlaying quirk.</summary>
    public int GameTic
    {
        get => _gameTic;
        set => _gameTic = value;
    }

    private static int[] BuildStepTable()
    {
        var table = new int[PitchSteps];
        for (int i = -128; i < 128; i++)
        {
            table[i + 128] = (int)(Math.Pow(2.0, i / 64.0) * 65536.0);
        }
        return table;
    }

    private static int[] BuildVolLookup()
    {
        var table = new int[VolumeLevels * SampleValues];
        for (int i = 0; i < VolumeLevels; i++)
        {
            for (int j = 0; j < SampleValues; j++)
            {
                table[(i * SampleValues) + j] = (i * (j - 128) * 256) / 127;
            }
        }
        return table;
    }

    /// <summary>
    /// Starts a sound on a channel — port of <c>addsfx</c> (i_sound.c:264-381).
    /// Returns the handle (1..100, wrapping), or -1 if no channel was free.
    /// </summary>
    /// <param name="sfxId">SFX id (used for singularity and duplicate checks).</param>
    /// <param name="data">Raw unsigned 8-bit sample data (the padded DMX payload).</param>
    /// <param name="length">Number of usable samples (the C <c>lengths[sfxid]</c>).</param>
    /// <param name="volume">Volume 0..127.</param>
    /// <param name="step">Pitch step from <see cref="StepTable"/> (unused by mixing, as in C).</param>
    /// <param name="separation">Stereo separation, 1..256 (NORM_SEP=128 is center).</param>
    /// <param name="gameTic">Current game tic (channelstart for eviction).</param>
    public int AddSfx(int sfxId, byte[] data, int length, int volume, int step, int separation, int gameTic)
    {
        // Chainsaw troubles: play these only one at a time (i_sound.c:283-306).
        if (sfxId is SfxId.Pistol or SfxId.SawUp or SfxId.SawIdl or SfxId.Sawful
            or SfxId.SawHit or SfxId.StnMov)
        {
            for (int i = 0; i < NumChannels; i++)
            {
                if (_channelData[i] is not null && _channelIds[i] == sfxId)
                {
                    _channelData[i] = null;
                    break;
                }
            }
        }

        // Loop all channels to find the oldest SFX (i_sound.c:308-325).
        // If we found a channel, fine; if not, we simply overwrite the
        // first one (0) — probably only happens at startup.
        int oldest = gameTic;
        int oldestNum = 0;
        int slot;
        int i2;
        for (i2 = 0; i2 < NumChannels && _channelData[i2] is not null; i2++)
        {
            if (_channelStart[i2] < oldest)
            {
                oldestNum = i2;
                oldest = _channelStart[i2];
            }
        }
        slot = i2 == NumChannels ? oldestNum : i2;

        // Set pointer to raw data and end (i_sound.c:329-332).
        _channelData[slot] = data;
        _channelEnd[slot] = length;
        _channelPos[slot] = 0;

        // Reset current handle number, limited to 0..100 (i_sound.c:334-340).
        if (_handleNums == 0)
        {
            _handleNums = 100;
        }
        int handle = _handleNums++;
        _channelHandles[slot] = handle;
        _channelStep[slot] = step;
        _channelStepRemainder[slot] = 0;
        _channelStart[slot] = gameTic;

        // Separation, that is, orientation/stereo. Range is 1..256.
        // Per left/right channel: x^2 separation, adjust volume properly
        // (i_sound.c:350-361).
        int sep = separation + 1;
        int leftVol = volume - ((volume * sep * sep) >> 16);
        sep -= 257;
        int rightVol = volume - ((volume * sep * sep) >> 16);

        // Sanity check, clamp volume (i_sound.c:363-368).
        if (rightVol < 0 || rightVol > 127)
        {
            throw new InvalidOperationException($"rightvol out of bounds: {rightVol}");
        }

        if (leftVol < 0 || leftVol > 127)
        {
            throw new InvalidOperationException($"leftvol out of bounds: {leftVol}");
        }

        // Get the proper lookup table piece for this volume level
        // (i_sound.c:370-373). C stores &vol_lookup[leftvol*256]; we store
        // the row offset.
        _channelLeftVolBase[slot] = leftVol * SampleValues;
        _channelRightVolBase[slot] = rightVol * SampleValues;

        // Preserve sound SFX id (e.g. for avoiding duplicates of chainsaw).
        _channelIds[slot] = sfxId;

        return handle;
    }

    /// <summary>
    /// Mixes one 512-frame stereo block into <paramref name="mixBuffer"/> —
    /// exact port of <c>I_UpdateSound</c> (i_sound.c:539-635). The buffer
    /// holds <see cref="BufferSize"/> shorts: left/right interleaved.
    /// </summary>
    public void UpdateSound(Span<short> mixBuffer)
    {
        if (mixBuffer.Length < BufferSize)
        {
            throw new ArgumentException(
                $"Mix buffer too small: {mixBuffer.Length} (need {BufferSize}).");
        }

        int outIndex = 0;
        for (int frame = 0; frame < SampleCount; frame++)
        {
            int dl = 0;
            int dr = 0;

            for (int chan = 0; chan < NumChannels; chan++)
            {
                var data = _channelData[chan];
                if (data is null)
                {
                    continue;
                }

                // Get the raw data from the channel (unsigned 8-bit).
                uint sample = data[_channelPos[chan]];

                // Add left and right parts, adjusted by volume.
                dl += VolLookup[_channelLeftVolBase[chan] + sample];
                dr += VolLookup[_channelRightVolBase[chan] + sample];

                // 16-bit fractional step through the raw data
                // (i_sound.c:599-603).
                _channelStepRemainder[chan] += _channelStep[chan];
                _channelPos[chan] += _channelStepRemainder[chan] >> 16;
                _channelStepRemainder[chan] &= 65536 - 1;

                // Check whether we are done.
                if (_channelPos[chan] >= _channelEnd[chan])
                {
                    _channelData[chan] = null;
                }
            }

            // Clamp to range (i_sound.c:617-630).
            mixBuffer[outIndex++] = dl > 0x7fff ? short.MaxValue
                : dl < -0x8000 ? short.MinValue
                : (short)dl;
            mixBuffer[outIndex++] = dr > 0x7fff ? short.MaxValue
                : dr < -0x8000 ? short.MinValue
                : (short)dr;
        }
    }

    /// <summary>
    /// Port of <c>I_SoundIsPlaying</c> (i_sound.c:517-521): the C compares
    /// <c>gametic &lt; handle</c> — a quirk that makes s_sound's bookkeeping
    /// channels free themselves once the tic counter passes the handle value,
    /// while the mixer itself drains channels by data end. Preserved exactly.
    /// </summary>
    public bool SoundIsPlaying(int handle) => GameTic < handle;

    /// <summary>Number of channels currently holding active sound data.</summary>
    public int ActiveChannels
    {
        get
        {
            int count = 0;
            for (int i = 0; i < NumChannels; i++)
            {
                if (_channelData[i] is not null)
                {
                    count++;
                }
            }
            return count;
        }
    }
}

using System;

namespace DoomSharp.Platform;

/// <summary>
/// Parsed DMX format-3 sound effect lump (the <c>DS*</c> lumps in the WAD).
/// </summary>
/// <remarks>
/// <para>
/// Port of <c>getsfx</c> (<c>i_sound.c:185-251</c>). The DMX format is an
/// 8-byte header followed by unsigned 8-bit PCM samples:
/// </para>
/// <list type="bullet">
/// <item>bytes 0-1: format (little-endian ushort, must be 3)</item>
/// <item>bytes 2-3: sample rate (little-endian ushort, 11025 for all DOOM SFX)</item>
/// <item>bytes 4-7: number of samples (little-endian uint)</item>
/// </list>
/// <para>
/// The C code pads the sample data out to a multiple of <see cref="SampleCount"/>
/// (512) samples with the silence value 0x80 so the mixer can read full
/// buffers without bounds checks. We keep the padding semantics but track the
/// true sample count separately, so the mixer stops at the real end of the
/// sound exactly like the C <c>channelsend</c> pointer does.
/// </para>
/// </remarks>
public sealed class DmxSound
{
    /// <summary>DMX format tag for unsigned 8-bit PCM. Only 3 is valid in DOOM's WADs.</summary>
    public const int ValidFormat = 3;

    /// <summary>Size of the DMX header in bytes.</summary>
    public const int HeaderSize = 8;

    /// <summary>Silence value for unsigned 8-bit samples.</summary>
    public const byte Silence = 0x80;

    /// <summary>Samples per mixing buffer (SAMPLECOUNT, i_sound.c:93).</summary>
    public const int SampleCount = 512;

    /// <summary>DMX format tag from the lump header.</summary>
    public int Format { get; }

    /// <summary>Sample rate from the lump header (11025 for all DOOM SFX).</summary>
    public int SampleRate { get; }

    /// <summary>Number of samples in the sound (excluding padding).</summary>
    public int NumSamples { get; }

    /// <summary>
    /// The raw unsigned 8-bit PCM samples (length == <see cref="SampleCount"/>,
    /// padded up to a 512-sample boundary with silence).
    /// </summary>
    public byte[] Samples { get; }

    private DmxSound(int format, int sampleRate, byte[] samples, int numSamples)
    {
        Format = format;
        SampleRate = sampleRate;
        Samples = samples;
        NumSamples = numSamples;
    }

    /// <summary>
    /// Parses a DMX sound lump. Mirrors <c>getsfx</c>: validates the header,
    /// extracts the sample data, and pads to a 512-sample boundary with 0x80
    /// (i_sound.c:230-241 — the C pads so the mixer can read full
    /// SAMPLECOUNT blocks; we preserve the padded length for
    /// characterization parity).
    /// </summary>
    public static DmxSound Parse(ReadOnlySpan<byte> lump)
    {
        if (lump.Length < HeaderSize)
        {
            throw new ArgumentException(
                $"DMX sound lump too small: {lump.Length} bytes (need {HeaderSize}-byte header).");
        }

        int format = lump[0] | (lump[1] << 8);
        if (format != ValidFormat)
        {
            throw new ArgumentException(
                $"Unsupported DMX sound format {format} (expected {ValidFormat}).");
        }

        int sampleRate = lump[2] | (lump[3] << 8);
        uint sampleCount = (uint)(lump[4] | (lump[5] << 8) | (lump[6] << 16) | (lump[7] << 24));

        if (sampleCount > lump.Length - HeaderSize)
        {
            throw new ArgumentException(
                $"DMX sound header claims {sampleCount} samples but lump only holds {lump.Length - HeaderSize}.");
        }

        // Copy the samples out of the lump buffer.
        var samples = new byte[sampleCount];
        lump.Slice(HeaderSize, (int)sampleCount).CopyTo(samples);

        // Pad to a 512-sample boundary with silence, exactly like getsfx
        // (i_sound.c:230-241).
        int paddedSize = (int)(((sampleCount + SampleCount - 1) / SampleCount) * SampleCount);
        if (paddedSize > samples.Length)
        {
            Array.Resize(ref samples, paddedSize);
            for (int i = (int)sampleCount; i < paddedSize; i++)
            {
                samples[i] = Silence;
            }
        }

        return new DmxSound(format, sampleRate, samples, (int)sampleCount);
    }
}

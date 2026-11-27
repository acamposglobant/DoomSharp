using System;
using DoomSharp.Platform;
using Xunit;

namespace DoomSharp.Tests.Unit;

/// <summary>
/// DMX format-3 lump parsing — characterization of <c>getsfx</c>
/// (i_sound.c:185-251).
/// </summary>
public class DmxSoundTests
{
    private static byte[] MakeLump(int format, int sampleRate, int sampleCount, byte fill = 0x40)
    {
        var lump = new byte[8 + sampleCount];
        lump[0] = (byte)(format & 0xff);
        lump[1] = (byte)(format >> 8);
        lump[2] = (byte)(sampleRate & 0xff);
        lump[3] = (byte)((sampleRate >> 8) & 0xff);
        lump[4] = (byte)(sampleCount & 0xff);
        lump[5] = (byte)((sampleCount >> 8) & 0xff);
        lump[6] = (byte)((sampleCount >> 16) & 0xff);
        lump[7] = (byte)((sampleCount >> 24) & 0xff);
        for (int i = 0; i < sampleCount; i++)
        {
            lump[8 + i] = fill;
        }
        return lump;
    }

    [Fact]
    public void Parse_ValidLump_ExtractsHeaderAndSamples()
    {
        var lump = MakeLump(3, 11025, 1000);

        var sound = DmxSound.Parse(lump);

        Assert.Equal(3, sound.Format);
        Assert.Equal(11025, sound.SampleRate);
        Assert.Equal(1000, sound.NumSamples);
        Assert.Equal(0x40, sound.Samples[0]);
        Assert.Equal(0x40, sound.Samples[999]);
    }

    [Fact]
    public void Parse_PadsTo512SampleBoundary()
    {
        // 1000 samples -> padded to 1024 with 0x80.
        var lump = MakeLump(3, 11025, 1000);

        var sound = DmxSound.Parse(lump);

        Assert.Equal(1024, sound.Samples.Length);
        Assert.Equal(1000, sound.NumSamples);
        for (int i = 1000; i < 1024; i++)
        {
            Assert.Equal(DmxSound.Silence, sound.Samples[i]);
        }
    }

    [Fact]
    public void Parse_ExactMultipleOf512_NoExtraPadding()
    {
        var lump = MakeLump(3, 11025, 1024);

        var sound = DmxSound.Parse(lump);

        Assert.Equal(1024, sound.Samples.Length);
    }

    [Fact]
    public void Parse_TinySound_PadsTo512()
    {
        var lump = MakeLump(3, 11025, 10);

        var sound = DmxSound.Parse(lump);

        Assert.Equal(512, sound.Samples.Length);
        Assert.Equal(0x40, sound.Samples[0]);
        Assert.Equal(DmxSound.Silence, sound.Samples[511]);
    }
    [Fact]
    public void Parse_BadFormat_Throws()
    {
        var lump = MakeLump(7, 11025, 100);

        Assert.ThrowsAny<ArgumentException>(() => DmxSound.Parse(lump));
    }

    [Fact]
    public void Parse_TruncatedLump_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => DmxSound.Parse(new byte[4]));
    }

    [Fact]
    public void Parse_SampleCountExceedsLump_Throws()
    {
        var lump = MakeLump(3, 11025, 100);
        lump[4] = 0xff; // claim 0x...ff samples
        lump[5] = 0xff;
        lump[6] = 0xff;
        lump[7] = 0x00;

        Assert.ThrowsAny<ArgumentException>(() => DmxSound.Parse(lump));
    }
}

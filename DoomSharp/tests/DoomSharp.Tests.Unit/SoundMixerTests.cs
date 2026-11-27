using System;
using DoomSharp.Platform;
using Xunit;

namespace DoomSharp.Tests.Unit;

/// <summary>
/// Characterization of the 8-channel PCM mixer (<c>I_UpdateSound</c>,
/// <c>addsfx</c>, <c>I_SetChannels</c> — i_sound.c).
/// </summary>
public class SoundMixerTests
{
    private static byte[] MakeSound(int length, byte fill = 0xff)
    {
        var data = new byte[512]; // padded
        for (int i = 0; i < length; i++)
        {
            data[i] = fill;
        }
        return data;
    }

    [Fact]
    public void StepTable_MatchesCFormula()
    {
        // steptable[i] = pow(2, i/64) * 65536 for i in -128..127.
        Assert.Equal(65536, SoundMixer.StepTable[128]);          // i=0
        Assert.Equal((int)(Math.Pow(2.0, -2.0) * 65536.0), SoundMixer.StepTable[0]);   // i=-128
        Assert.Equal((int)(Math.Pow(2.0, 127 / 64.0) * 65536.0), SoundMixer.StepTable[255]);
    }

    [Fact]
    public void VolLookup_MatchesCFormula()
    {
        // vol_lookup[i*256+j] = (i*(j-128)*256)/127
        for (int i = 0; i < 128; i += 7)
        {
            for (int j = 0; j < 256; j += 17)
            {
                Assert.Equal((i * (j - 128) * 256) / 127, SoundMixer.VolLookup[i * 256 + j]);
            }
        }
    }

    [Fact]
    public void VolLookup_SilenceSample_IsZero()
    {
        // j=128 (0x80) is the unsigned silence value -> (j-128) == 0.
        for (int i = 0; i < 128; i++)
        {
            Assert.Equal(0, SoundMixer.VolLookup[(i * 256) + 128]);
        }
    }

    [Fact]
    public void AddSfx_ReturnsHandlesStartingAt100()
    {
        var mixer = new SoundMixer();
        var data = new byte[512];

        // handlenums starts 0 -> set to 100 -> first handle is 100
        // (i_sound.c:334-340). It is an unsigned short, so it wraps at
        // 65536, not 100 (the C comment "limited to 0..100" is misleading).
        int h1 = mixer.AddSfx(SfxId.Pistol, data, 512, 100, SoundMixer.StepTable[128], 128, 0);
        Assert.Equal(100, h1);

        int h2 = mixer.AddSfx(SfxId.Shotgn, data, 512, 100, 65536, 128, 0);
        Assert.Equal(101, h2);
    }

    [Fact]
    public void AddSfx_Singularity_KillsExistingPistol()
    {
        var mixer = new SoundMixer();
        var data = new byte[512];

        mixer.AddSfx(SfxId.Pistol, data, 512, 100, 65536, 128, 0);
        Assert.Equal(1, mixer.ActiveChannels);

        // A second pistol must kill the first (chainsaw troubles).
        mixer.AddSfx(SfxId.Pistol, data, 512, 100, 65536, 128, 1);
        Assert.Equal(1, mixer.ActiveChannels);
    }

    [Fact]
    public void AddSfx_AllChannelsBusy_EvictsOldest()
    {
        var mixer = new SoundMixer();
        var data = new byte[512];

        // Fill all 8 channels.
        for (int i = 0; i < SoundMixer.NumChannels; i++)
        {
            mixer.AddSfx(SfxId.Shotgn, data, 512, 100, 65536, 128, i);
        }
        Assert.Equal(8, mixer.ActiveChannels);

        // A 9th sound evicts the oldest (channelstart == 0).
        mixer.AddSfx(SfxId.Pistol, data, 512, 100, 65536, 128, 9);
        Assert.Equal(8, mixer.ActiveChannels);
    }

    [Fact]
    public void UpdateSound_SingleChannel_ProducesExpectedAmplitude()
    {
        var mixer = new SoundMixer();
        var data = new byte[512];
        // Sample 0x40 = 64 -> signed value (64-128) = -64, scaled by vol.
        for (int i = 0; i < 512; i++)
        {
            data[i] = 0x40;
        }

        // Full volume, centered separation.
        mixer.AddSfx(SfxId.Pistol, data, 512, 127, 65536, 128, 0);

        var buffer = new short[SoundMixer.BufferSize];
        mixer.UpdateSound(buffer);

        // sep=128 (NORM_SEP): leftvol = 127 - (127*129*129 >> 16) = 95,
        // rightvol = 127 - (127*(129-257)^2 >> 16) = 96 — the C's famous
        // off-by-one asymmetry (seperation+1 on the left, -257 on the right).
        // vol_lookup[95*256+64] = -12255; vol_lookup[96*256+64] = -12384.
        Assert.Equal(-12255, buffer[0]);
        Assert.Equal(-12384, buffer[1]);
    }

    [Fact]
    public void UpdateSound_SilenceData_ProducesSilence()
    {
        var mixer = new SoundMixer();
        var data = new byte[512];
        Array.Fill(data, DmxSound.Silence);

        mixer.AddSfx(SfxId.Pistol, data, 512, 127, 65536, 128, 0);

        var buffer = new short[SoundMixer.BufferSize];
        mixer.UpdateSound(buffer);

        Assert.All(buffer, s => Assert.Equal(0, s));
    }

    [Fact]
    public void UpdateSound_ClampsToShortRange()
    {
        var mixer = new SoundMixer();
        var data = new byte[512];
        Array.Fill(data, byte.MaxValue); // loudest sample

        // 8 channels at full volume, all centered: 8 * 127 * 127 * 256/127
        // = 8 * 127 * 256 = 260096 -> clamps to 32767.
        for (int i = 0; i < SoundMixer.NumChannels; i++)
        {
            mixer.AddSfx(SfxId.Shotgn, data, 512, 127, 65536, 128, i);
        }

        var buffer = new short[SoundMixer.BufferSize];
        mixer.UpdateSound(buffer);

        Assert.Equal(short.MaxValue, buffer[0]);
        Assert.Equal(short.MaxValue, buffer[1]);
    }

    [Fact]
    public void UpdateSound_ChannelDrainsAtDataEnd()
    {
        var mixer = new SoundMixer();
        var data = new byte[512];
        Array.Fill(data, DmxSound.Silence);

        mixer.AddSfx(SfxId.Pistol, data, 10, 127, 65536, 128, 0);
        Assert.Equal(1, mixer.ActiveChannels);

        var buffer = new short[SoundMixer.BufferSize];
        mixer.UpdateSound(buffer);

        // step = 65536 -> 1 sample per frame; after 512 frames the 10-sample
        // sound is long done.
        Assert.Equal(0, mixer.ActiveChannels);
    }

    [Fact]
    public void UpdateSound_StereoSeparation_LeftOnly()
    {
        var mixer = new SoundMixer();
        var data = new byte[512];
        Array.Fill(data, (byte)0x40);

        // separation 1 -> hard left: leftvol = 127 - (127*2*2>>16) = 127,
        // rightvol = 127 - (127*(1+1-257)^2 >> 16) = 127 - (127*65536>>16) = 0.
        mixer.AddSfx(SfxId.Pistol, data, 512, 127, 65536, 1, 0);

        var buffer = new short[SoundMixer.BufferSize];
        mixer.UpdateSound(buffer);

        // sep=1: leftvol = 127 - (127*2*2 >> 16) = 127,
        // rightvol = 127 - (127*(2-257)^2 >> 16) = 127 - 126 = 1.
        // vol_lookup[127*256+64] = -16384; vol_lookup[1*256+64] = -128.9 -> -128
        // (C integer division truncates toward zero; the -129 observed is the
        // exact table value for this row).
        Assert.Equal(-16384, buffer[0]); // left
        Assert.Equal(-129, buffer[1]);   // right (near-silent, not exactly 0)
    }

    [Fact]
    public void SoundIsPlaying_PreservesGameticQuirk()
    {
        var mixer = new SoundMixer { GameTic = 50 };

        // I_SoundIsPlaying: gametic < handle.
        Assert.True(mixer.SoundIsPlaying(100));
        Assert.False(mixer.SoundIsPlaying(50));
        Assert.False(mixer.SoundIsPlaying(10));
    }
}

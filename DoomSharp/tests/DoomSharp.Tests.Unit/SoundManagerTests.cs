using System;
using DoomSharp.Core;
using DoomSharp.Platform;
using DoomSharp.Rendering;
using Xunit;

namespace DoomSharp.Tests.Unit;

/// <summary>
/// Characterization of the s_sound.c management layer: attenuation
/// (<c>S_AdjustSoundParams</c>), channel allocation/eviction
/// (<c>S_getChannel</c>), and volume clamping.
/// </summary>
public class SoundManagerTests
{
    private static (SoundManager Manager, SoundMixer Mixer, HeadlessAudioOutput Output) CreateManager(
        int numChannels = 2, int gameMap = 1)
    {
        var mixer = new SoundMixer();
        var output = new HeadlessAudioOutput();
        var manager = new SoundManager(
            mixer,
            pointToAngle2: RendererMath.PointToAngle2,
            fineSine: fineIndex => AngleTables.FineSine[fineIndex],
            random: () => 0, // deterministic pitch perturbation
            lookupMusicLump: _ => 1,
            numChannels: numChannels,
            gameMap: gameMap);
        return (manager, mixer, output);
    }

    [Fact]
    public void Init_SetsVolumesAndResetsSfx()
    {
        var (manager, _, _) = CreateManager();

        manager.Init(10, 20);

        Assert.Equal(10, manager.SfxVolume);
        Assert.Equal(20, manager.MusicVolume);
        Assert.Equal(-1, SoundTables.Sfx[SfxId.Pistol].LumpNum);
    }

    [Fact]
    public void SetSfxVolume_OutOfRange_Throws()
    {
        var (manager, _, _) = CreateManager();

        Assert.Throws<ArgumentOutOfRangeException>(() => manager.SetSfxVolume(128));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.SetSfxVolume(-1));
    }

    [Fact]
    public void StartSound_BadSfxId_Throws()
    {
        var (manager, _, _) = CreateManager();
        manager.Init(15, 15);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => manager.StartSound(null, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => manager.StartSound(null, SfxId.NumSfx));
    }

    [Fact]
    public void StartSound_LocalSound_UsesNormSep()
    {
        var (manager, mixer, _) = CreateManager();
        manager.Init(15, 15);
        SoundTables.Sfx[SfxId.Pistol].Data = new byte[512];
        SoundTables.Sfx[SfxId.Pistol].Length = 512;

        var origin = new SoundOrigin(0, 0, 0);
        manager.Listener = origin;
        manager.StartSound(origin, SfxId.Pistol);

        // The sound must be on a mixer channel now.
        Assert.Equal(1, mixer.ActiveChannels);
    }

    [Fact]
    public void StartSound_DistantSound_Clipped()
    {
        var (manager, mixer, _) = CreateManager();
        manager.Init(15, 15);
        SoundTables.Sfx[SfxId.Pistol].Data = new byte[512];
        SoundTables.Sfx[SfxId.Pistol].Length = 512;

        // Listener at origin; source 2000*FRACUNIT away (> S_CLIPPING_DIST).
        var listener = new SoundOrigin(0, 0, 0);
        manager.Listener = listener;
        var far = new SoundOrigin(2000 << 16, 0, 0);

        manager.StartSound(far, SfxId.Pistol);

        // Not audible -> no channel allocated.
        Assert.Equal(0, mixer.ActiveChannels);
    }

    [Fact]
    public void AdjustSoundParams_CloseSound_MaxVolume()
    {
        var (manager, _, _) = CreateManager();
        manager.Init(100, 15);
        manager.Listener = new SoundOrigin(0, 0, 0);

        int vol = 0, sep = 0, pitch = 0;
        var near = new SoundOrigin(10 << 16, 0, 0);

        bool audible = manager.AdjustSoundParams(near, ref vol, ref sep, ref pitch);

        Assert.True(vol == 100);
        // Source directly ahead (angle 0 relative): finesine ~ 0 -> sep ~ 128.
        // The exact value depends on the finesine[8191] table quirk (the C
        // wraps 0 + (0xffffffff - 0) for equal angles, landing at index 8191).
        Assert.InRange(sep, 120, 136);
        // S_AdjustSoundParams never touches pitch (s_sound.c:752-818).
        Assert.Equal(0, pitch);
    }

    [Fact]
    public void AdjustSoundParams_MidDistance_Attenuates()
    {
        var (manager, _, _) = CreateManager();
        manager.Init(100, 15);
        manager.Listener = new SoundOrigin(0, 0, 0);

        int vol = 0, sep = 0, pitch = 0;
        var source = new SoundOrigin(600 << 16, 0, 0);

        bool audible = manager.AdjustSoundParams(source, ref vol, ref sep, ref pitch);

        // approxDist = 600*FRACUNIT; (1200-600) = 600; vol = 100*600/1040 = 57
        Assert.True(audible);
        Assert.Equal(57, vol);
    }

    [Fact]
    public void AdjustSoundParams_Gamemap8_NeverClips()
    {
        var (manager, _, _) = CreateManager(gameMap: 8);
        manager.Init(100, 15);
        manager.Listener = new SoundOrigin(0, 0, 0);

        int vol = 0, sep = 0, pitch = 0;
        var far = new SoundOrigin(5000 << 16, 0, 0);

        bool audible = manager.AdjustSoundParams(far, ref vol, ref sep, ref pitch);

        // gamemap 8: distance clamps to CLIPPING_DIST, vol >= 15.
        Assert.True(vol >= 15);
    }

    [Fact]
    public void GetChannel_PriorityEviction()
    {
        var (manager, mixer, _) = CreateManager(numChannels: 1);
        manager.Init(15, 15);
        SoundTables.Sfx[SfxId.Pistol].Data = new byte[512];
        SoundTables.Sfx[SfxId.Pistol].Length = 512;
        SoundTables.Sfx[SfxId.CybSit].Data = new byte[512];
        SoundTables.Sfx[SfxId.CybSit].Length = 512;

        var origin = new SoundOrigin(0, 0, 0);
        manager.Listener = origin;

        // Low priority sound occupies the only channel.
        manager.StartSound(origin, SfxId.Pistol); // priority 64
        Assert.Equal(1, manager.ActiveBookkeepingChannels);

        // High priority sound (cybsit, 92) kicks it out.
        manager.StartSound(origin, SfxId.CybSit);
        Assert.Equal(1, manager.ActiveBookkeepingChannels);
    }

    [Fact]
    public void StopSound_FreesBookkeepingChannelOnly()
    {
        var (manager, mixer, _) = CreateManager();
        manager.Init(15, 15);
        SoundTables.Sfx[SfxId.Pistol].Data = new byte[512];
        SoundTables.Sfx[SfxId.Pistol].Length = 512;

        var origin = new SoundOrigin(0, 0, 0);
        manager.Listener = origin;
        manager.StartSound(origin, SfxId.Pistol);
        Assert.Equal(1, manager.ActiveBookkeepingChannels);

        manager.StopSound(origin);

        // I_StopSound is a no-op in the C: the mixer channel keeps draining
        // by data end; only the s_sound bookkeeping slot is freed.
        Assert.Equal(0, manager.ActiveBookkeepingChannels);
        Assert.Equal(1, mixer.ActiveChannels);
    }

    [Fact]
    public void UpdateSounds_FreesStalledChannels()
    {
        var (manager, mixer, _) = CreateManager();
        manager.Init(15, 15);
        SoundTables.Sfx[SfxId.Pistol].Data = new byte[512];
        SoundTables.Sfx[SfxId.Pistol].Length = 512;

        var origin = new SoundOrigin(0, 0, 0);
        manager.Listener = origin;
        manager.StartSound(origin, SfxId.Pistol);
        Assert.Equal(1, mixer.ActiveChannels);

        // Advance gametic past the handle (the I_SoundIsPlaying quirk).
        mixer.GameTic = 200;
        manager.UpdateSounds(origin);

        // Channel freed by s_sound bookkeeping (mixer drains independently).
        Assert.Equal(0, manager.ActiveBookkeepingChannels);
    }
}

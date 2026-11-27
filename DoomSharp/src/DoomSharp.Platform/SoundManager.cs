using System;

namespace DoomSharp.Platform;

/// <summary>
/// Listener/source position snapshot for sound attenuation — stands in for
/// <c>mobj_t</c> (mobjs are DOOM-021 scope). The App passes the camera.
/// </summary>
public readonly record struct SoundOrigin(int X, int Y, uint Angle);

/// <summary>
/// The not-so-system-specific sound interface — port of <c>s_sound.c</c>.
/// </summary>
/// <remarks>
/// <para>
/// Manages the s_sound bookkeeping channels (default 2, <c>numChannels</c>),
/// distance attenuation/stereo separation (<c>S_AdjustSoundParams</c>),
/// per-origin sound stop, and the music bookkeeping (a faithful stub —
/// linuxdoom 1.10 has no music; the <c>I_*Song</c> API is dummies).
/// </para>
/// <para>
/// <see cref="AdjustSoundParams"/> needs <c>R_PointToAngle2</c> +
/// <c>finesine</c>; to keep DoomSharp.Platform decoupled from
/// DoomSharp.Rendering both are injected as delegates (same pattern as
/// PaletteConverter taking gamma as a parameter). The listener position is
/// supplied per call, standing in for <c>players[consoleplayer].mo</c>.
/// </para>
/// </remarks>
public sealed class SoundManager
{
    /// <summary>S_MAX_VOLUME (s_sound.c:51).</summary>
    public const int MaxVolume = 127;

    /// <summary>When to clip out sounds (s_sound.c:55): 1200*FRACUNIT.</summary>
    public const int ClippingDist = 1200 * 0x10000;

    /// <summary>Distance to origin when sounds should be maxed out (s_sound.c:61).</summary>
    public const int CloseDist = 160 * 0x10000;

    /// <summary>Attenuator divisor (s_sound.c:64).</summary>
    public const int Attenuator = (ClippingDist - CloseDist) >> 16;

    /// <summary>NORM_PITCH (s_sound.c:69).</summary>
    public const int NormPitch = 128;

    /// <summary>NORM_PRIORITY (s_sound.c:70).</summary>
    public const int NormPriority = 64;

    /// <summary>NORM_SEP (s_sound.c:71).</summary>
    public const int NormSep = 128;

    /// <summary>S_STEREO_SWING (s_sound.c:74).</summary>
    public const int StereoSwing = 96 * 0x10000;

    /// <summary>ANGLETOFINESHIFT (tables.h) — angle_t index into finesine.</summary>
    public const int AngleToFineShift = 19;

    /// <summary>Default channel count (numChannels, set by M_misc defaults).</summary>
    public const int DefaultNumChannels = 2;

    private readonly SoundMixer _mixer;
    private readonly Func<int, int, int, int, uint> _pointToAngle2;
    private readonly Func<uint, int> _fineSine;
    private readonly Func<int> _random;
    private readonly Func<string, int> _lookupMusicLump;

    // the set of channels available (channel_t, s_sound.c:93-108).
    private readonly SfxInfo?[] _channelSfxInfo;
    private readonly SoundOrigin?[] _channelOrigin;
    private readonly int[] _channelHandle;

    // Adjustable by menu (s_sound.c:113-116).
    private int _sndSfxVolume = 15;

    private bool _musPaused;
    private MusicInfo? _musPlaying;
    private int _numChannels;
    private int _gameMap;

    /// <summary>Current sfx volume (0..127).</summary>
    public int SfxVolume => _sndSfxVolume;

    /// <summary>Current music volume.</summary>
    public int MusicVolume { get; private set; } = 15;

    /// <summary>The music currently playing, or null.</summary>
    public MusicInfo? MusPlaying => _musPlaying;

    /// <summary>Whether music is paused (S_PauseSound/S_ResumeSound).</summary>
    public bool MusPaused => _musPaused;

    /// <summary>
    /// The listener (console player) position — set by the caller before
    /// <see cref="StartSound"/>/<see cref="UpdateSounds"/>; stands in for
    /// <c>players[consoleplayer].mo</c>.
    /// </summary>
    public SoundOrigin? Listener { get; set; }

    /// <summary>
    /// Creates a sound manager.
    /// </summary>
    /// <param name="mixer">The PCM mixer backing I_StartSound.</param>
    /// <param name="pointToAngle2">
    /// Angle function for stereo separation — wire
    /// <c>RendererMath.PointToAngle2</c> here (signature: x1, y1, x2, y2 →
    /// angle_t). Tests may inject a stub.
    /// </param>
    /// <param name="fineSine">
    /// Sine table lookup for stereo separation — wire
    /// <c>RendererMath.FineSine(angle &gt;&gt; ANGLETOFINESHIFT)</c> here
    /// (angle already shifted to the fine index).
    /// </param>
    /// <param name="random">Random source for pitch perturbation (M_Random).</param>
    /// <param name="lookupMusicLump">WAD lump lookup for "d_*" lumps (W_GetNumForName).</param>
    /// <param name="numChannels">s_sound bookkeeping channels (numChannels).</param>
    /// <param name="gameMap">Current gamemap (the gamemap==8 special case).</param>
    public SoundManager(
        SoundMixer mixer,
        Func<int, int, int, int, uint> pointToAngle2,
        Func<uint, int> fineSine,
        Func<int> random,
        Func<string, int> lookupMusicLump,
        int numChannels = DefaultNumChannels,
        int gameMap = 1)
    {
        _mixer = mixer;
        _pointToAngle2 = pointToAngle2;
        _fineSine = fineSine;
        _random = random;
        _lookupMusicLump = lookupMusicLump;
        _numChannels = numChannels;
        _gameMap = gameMap;
        _channelSfxInfo = new SfxInfo[numChannels];
        _channelOrigin = new SoundOrigin?[numChannels];
        _channelHandle = new int[numChannels];
    }

    /// <summary>
    /// Initializes sound stuff, including volume — port of <c>S_Init</c>
    /// (s_sound.c:161-192). Sets volumes and marks all SFX as uncached.
    /// </summary>
    public void Init(int sfxVolume, int musicVolume)
    {
        SetSfxVolume(sfxVolume);
        SetMusicVolume(musicVolume);

        // Free all channels for use; no sounds playing, not paused.
        for (int i = 0; i < _numChannels; i++)
        {
            _channelSfxInfo[i] = null;
        }
        _musPaused = false;

        // Note that sounds have not been cached (yet) (s_sound.c:190-191).
        for (int i = 1; i < SfxId.NumSfx; i++)
        {
            SoundTables.Sfx[i].LumpNum = -1;
            SoundTables.Sfx[i].Usefulness = -1;
        }
    }

    /// <summary>
    /// Port of <c>S_SetSfxVolume</c> (s_sound.c:631-639).
    /// </summary>
    public void SetSfxVolume(int volume)
    {
        if (volume < 0 || volume > MaxVolume)
        {
            throw new ArgumentOutOfRangeException(nameof(volume),
                $"Attempt to set sfx volume at {volume}");
        }
        _sndSfxVolume = volume;
    }

    /// <summary>
    /// Port of <c>S_SetMusicVolume</c> (s_sound.c:616-627).
    /// </summary>
    public void SetMusicVolume(int volume)
    {
        if (volume < 0 || volume > MaxVolume)
        {
            throw new ArgumentOutOfRangeException(nameof(volume),
                $"Attempt to set music volume at {volume}");
        }
        MusicVolume = volume;
    }

    /// <summary>
    /// Start sound for thing at origin using sound_id — port of
    /// <c>S_StartSound</c> (s_sound.c:397-408).
    /// </summary>
    public void StartSound(SoundOrigin? origin, int sfxId)
    {
        StartSoundAtVolume(origin, sfxId, _sndSfxVolume);
    }

    /// <summary>
    /// Will start a sound at a given volume — port of
    /// <c>S_StartSoundAtVolume</c> (s_sound.c:254-395).
    /// </summary>
    public void StartSoundAtVolume(SoundOrigin? originP, int sfxId, int volume)
    {
        // check for bogus sound # (s_sound.c:277-278).
        if (sfxId < 1 || sfxId >= SfxId.NumSfx)
        {
            throw new ArgumentOutOfRangeException(nameof(sfxId), $"Bad sfx #: {sfxId}");
        }

        var sfx = SoundTables.Sfx[sfxId];

        // Initialize sound parameters (s_sound.c:282-299).
        int pitch;
        int priority;
        if (sfx.Link is not null)
        {
            pitch = sfx.Pitch;
            priority = sfx.Priority;
            volume += sfx.Volume;

            if (volume < 1)
            {
                return;
            }
            if (volume > _sndSfxVolume)
            {
                volume = _sndSfxVolume;
            }
        }
        else
        {
            pitch = NormPitch;
            priority = NormPriority;
        }

        // Check to see if it is audible, and if not, modify the params
        // (s_sound.c:302-324). A sound at the listener's own position is
        // local (sep = NORM_SEP).
        int sep = NormSep;
        if (originP is { } originValue && !IsLocalSound(originValue))
        {
            if (!AdjustSoundParams(originValue, ref volume, ref sep, ref pitch))
            {
                return;
            }
        }

        // hacks to vary the sfx pitches (s_sound.c:326-346).
        if (sfxId >= SfxId.SawUp && sfxId <= SfxId.SawHit)
        {
            pitch += 8 - (_random() & 15);
            pitch = Math.Clamp(pitch, 0, 255);
        }
        else if (sfxId != SfxId.Itemup && sfxId != SfxId.Tink)
        {
            pitch += 16 - (_random() & 31);
            pitch = Math.Clamp(pitch, 0, 255);
        }

        // kill old sound (s_sound.c:348-349).
        StopSound(originP);

        // try to find a channel (s_sound.c:351-352).
        int cnum = GetChannel(originP, sfx);
        if (cnum < 0)
        {
            return;
        }

        // increase the usefulness (s_sound.c:383-385).
        if (sfx.Usefulness++ < 0)
        {
            sfx.Usefulness = 1;
        }

        // Assigns the handle to one of the channels in the mix/output
        // buffer (s_sound.c:389-394) — I_StartSound → addsfx.
        _channelHandle[cnum] = _mixer.AddSfx(
            sfxId, sfx.Data!, sfx.Length, volume,
            SoundMixer.StepTable[pitch], sep, _mixer.GameTic);
    }

    /// <summary>
    /// Stop sound for thing at origin — port of <c>S_StopSound</c>
    /// (s_sound.c:471-484).
    /// </summary>
    public void StopSound(SoundOrigin? origin)
    {
        if (origin is null)
        {
            return;
        }
        for (int cnum = 0; cnum < _numChannels; cnum++)
        {
            if (_channelSfxInfo[cnum] is not null &&
                _channelOrigin[cnum] == origin)
            {
                StopChannel(cnum);
                break;
            }
        }
    }

    /// <summary>
    /// Updates music &amp; sounds — port of <c>S_UpdateSounds</c>
    /// (s_sound.c:519-613).
    /// </summary>
    public void UpdateSounds(SoundOrigin listener)
    {
        Listener = listener;

        for (int cnum = 0; cnum < _numChannels; cnum++)
        {
            var sfx = _channelSfxInfo[cnum];
            if (sfx is null)
            {
                continue;
            }

            if (_mixer.SoundIsPlaying(_channelHandle[cnum]))
            {
                // initialize parameters (s_sound.c:562-565).
                int volume = _sndSfxVolume;
                int pitch = NormPitch;
                int sep = NormSep;

                if (sfx.Link is not null)
                {
                    pitch = sfx.Pitch;
                    volume += sfx.Volume;
                    if (volume < 1)
                    {
                        StopChannel(cnum);
                        continue;
                    }
                    if (volume > _sndSfxVolume)
                    {
                        volume = _sndSfxVolume;
                    }
                }

                // check non-local sounds for distance clipping or modify
                // their params (s_sound.c:582-598).
                if (_channelOrigin[cnum] is { } origin && origin != listener)
                {
                    if (!AdjustSoundParams(origin, ref volume, ref sep, ref pitch))
                    {
                        StopChannel(cnum);
                    }
                    // I_UpdateSoundParams is a no-op in the C (i_sound.c:674-688).
                }
            }
            else
            {
                // if channel is allocated but sound has stopped, free it
                // (s_sound.c:600-605).
                StopChannel(cnum);
            }
        }
    }

    /// <summary>
    /// Starts some music with the music id found in sounds.h
    /// (s_sound.c:644-647).
    /// </summary>
    public void StartMusic(int musicId) => ChangeMusic(musicId, looping: false);

    /// <summary>
    /// Starts music with looping flag — port of <c>S_ChangeMusic</c>
    /// (s_sound.c:649-686). Music playback itself is a faithful stub
    /// (linuxdoom's I_*Song API is dummies); the lump lookup is performed
    /// so the bookkeeping matches.
    /// </summary>
    public void ChangeMusic(int musicNum, bool looping)
    {
        if (musicNum <= MusicId.None || musicNum >= MusicId.NumMusic)
        {
            throw new ArgumentOutOfRangeException(nameof(musicNum),
                $"Bad music number {musicNum}");
        }
        var music = SoundTables.Music[musicNum];

        if (_musPlaying == music)
        {
            return;
        }

        // shutdown old music (s_sound.c:668-669).
        StopMusic();

        // get lumpnum if necessary (s_sound.c:671-676).
        if (music.LumpNum == 0)
        {
            music.LumpNum = _lookupMusicLump("d_" + music.Name);
        }

        _musPlaying = music;
        _musPaused = false;
    }

    /// <summary>
    /// Stops the music fer sure — port of <c>S_StopMusic</c>
    /// (s_sound.c:689-703).
    /// </summary>
    public void StopMusic()
    {
        if (_musPlaying is not null)
        {
            _musPlaying.Data = null;
            _musPlaying = null;
        }
    }

    /// <summary>
    /// Stop and resume music, during game PAUSE (s_sound.c:497-513).
    /// </summary>
    public void PauseSound()
    {
        if (_musPlaying is not null && !_musPaused)
        {
            _musPaused = true;
        }
    }

    public void ResumeSound()
    {
        if (_musPlaying is not null && _musPaused)
        {
            _musPaused = false;
        }
    }

    /// <summary>
    /// Per level startup: kills playing sounds, restarts music — port of
    /// <c>S_Start</c> (s_sound.c:202-248). Music selection needs
    /// gameepisode/gamemap; the caller supplies the resolved music id.
    /// </summary>
    public void Start(int musicId)
    {
        for (int cnum = 0; cnum < _numChannels; cnum++)
        {
            if (_channelSfxInfo[cnum] is not null)
            {
                StopChannel(cnum);
            }
        }

        _musPaused = false;
        ChangeMusic(musicId, looping: true);
    }

    /// <summary>
    /// Changes volume, stereo-separation, and pitch variables from the norm
    /// of a sound effect to be played — port of <c>S_AdjustSoundParams</c>
    /// (s_sound.c:752-818). Returns false if the sound is not audible.
    /// </summary>
    public bool AdjustSoundParams(SoundOrigin source, ref int vol, ref int sep, ref int pitch)
    {
        if (Listener is not { } listener)
        {
            throw new InvalidOperationException(
                "Listener must be set (UpdateSounds or a direct assignment) before AdjustSoundParams.");
        }

        int listenerX = listener.X;
        int listenerY = listener.Y;
        uint listenerAngle = listener.Angle;

        // calculate the distance to sound origin and clip it if necessary
        // (s_sound.c:765-777). From _GG1_ p.428: approx. euclidean distance.
        int adx = Math.Abs(listenerX - source.X);
        int ady = Math.Abs(listenerY - source.Y);

        int approxDist = adx + ady - ((adx < ady ? adx : ady) >> 1);

        if (_gameMap != 8 && approxDist > ClippingDist)
        {
            return false;
        }

        // angle of source to listener (s_sound.c:779-790).
        uint angle = _pointToAngle2(listenerX, listenerY, source.X, source.Y);
        if (angle > listenerAngle)
        {
            angle = angle - listenerAngle;
        }
        else
        {
            angle = angle + (0xffffffff - listenerAngle);
        }

        angle >>= AngleToFineShift;

        // stereo separation (s_sound.c:792-793):
        // *sep = 128 - (FixedMul(S_STEREO_SWING, finesine[angle]) >> FRACBITS)
        // FixedMul already shifts by 16; the explicit >>FRACBITS is a second
        // shift (96<<16 * finesine >> 16 >> 16 == 96*finesine >> 16).
        sep = 128 - (FixedMul(StereoSwing, _fineSine(angle)) >> 16);

        // volume calculation (s_sound.c:795-815).
        if (approxDist < CloseDist)
        {
            vol = _sndSfxVolume;
        }
        else if (_gameMap == 8)
        {
            if (approxDist > ClippingDist)
            {
                approxDist = ClippingDist;
            }
            vol = 15 + ((_sndSfxVolume - 15) * ((ClippingDist - approxDist) >> 16)) / Attenuator;
        }
        else
        {
            // distance effect
            vol = (_sndSfxVolume * ((ClippingDist - approxDist) >> 16)) / Attenuator;
        }

        return vol > 0;
    }

    /// <summary>FixedMul (m_fixed.c): (a*b) >> 16 with 64-bit intermediate.</summary>
    private static int FixedMul(int a, int b) => (int)(((long)a * b) >> 16);

    /// <summary>
    /// True when the sound origin is the listener itself (the C compares
    /// <c>origin == players[consoleplayer].mo</c>, s_sound.c:304).
    /// </summary>
    private bool IsLocalSound(SoundOrigin origin) =>
        Listener is { } listener && origin == listener;

    /// <summary>
    /// If none available, return -1. Otherwise channel # — port of
    /// <c>S_getChannel</c> (s_sound.c:827-875).
    /// </summary>
    private int GetChannel(SoundOrigin? origin, SfxInfo sfxInfo)
    {
        // Find an open channel (s_sound.c:837-847).
        int cnum;
        for (cnum = 0; cnum < _numChannels; cnum++)
        {
            if (_channelSfxInfo[cnum] is null)
            {
                break;
            }
            if (origin is { } o && _channelOrigin[cnum] == o)
            {
                StopChannel(cnum);
                break;
            }
        }

        // None available (s_sound.c:849-866).
        if (cnum == _numChannels)
        {
            // Look for lower priority.
            for (cnum = 0; cnum < _numChannels; cnum++)
            {
                if (_channelSfxInfo[cnum]!.Priority >= sfxInfo.Priority)
                {
                    break;
                }
            }

            if (cnum == _numChannels)
            {
                // No lower priority. Sorry, Charlie.
                return -1;
            }
            // Otherwise, kick out lower priority.
            StopChannel(cnum);
        }

        // channel is decided to be cnum (s_sound.c:868-874).
        _channelSfxInfo[cnum] = sfxInfo;
        _channelOrigin[cnum] = origin;
        return cnum;
    }

    /// <summary>
    /// Port of <c>S_StopChannel</c> (s_sound.c:708-742).
    /// </summary>
    public void StopChannel(int cnum)
    {
        var c = _channelSfxInfo[cnum];
        if (c is null)
        {
            return;
        }

        // stop the sound playing (I_StopSound is a no-op in the C).
        // The C's "other channels playing the same sound" loop result is
        // unused (s_sound.c:727-735); kept as a comment for parity.

        // degrade usefulness of sound data (s_sound.c:737-738).
        c.Usefulness--;
        _channelSfxInfo[cnum] = null;
    }

    /// <summary>
    /// Number of s_sound bookkeeping channels currently allocated. Note this
    /// is independent of the mixer's active data channels: the C's
    /// <c>I_StopSound</c> is a no-op, so stopping a sound frees only the
    /// s_sound bookkeeping slot while the mixer drains by data end.
    /// </summary>
    public int ActiveBookkeepingChannels
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _numChannels; i++)
            {
                if (_channelSfxInfo[i] is not null)
                {
                    count++;
                }
            }
            return count;
        }
    }
}

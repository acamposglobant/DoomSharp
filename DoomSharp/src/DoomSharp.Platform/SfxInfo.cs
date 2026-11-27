namespace DoomSharp.Platform;

/// <summary>
/// Per-sound-effect descriptor — port of <c>sfxinfo_t</c> (sounds.h:32-62).
/// </summary>
public sealed class SfxInfo
{
    /// <summary>Up to 6-character name (the DS* lump is "ds" + name).</summary>
    public string Name { get; }

    /// <summary>Sfx singularity (only one at a time).</summary>
    public bool Singularity { get; }

    /// <summary>Sfx priority.</summary>
    public int Priority { get; }

    /// <summary>Referenced sound if a link (e.g. chgun → pistol), or null.</summary>
    public SfxInfo? Link { get; set; }

    /// <summary>Pitch if a link.</summary>
    public int Pitch { get; set; }

    /// <summary>Volume if a link.</summary>
    public int Volume { get; set; }

    /// <summary>Sound data (padded DMX payload), set by the loader.</summary>
    public byte[]? Data { get; set; }

    /// <summary>Usable sample count (the C <c>lengths[]</c>), set by the loader.</summary>
    public int Length { get; set; }

    /// <summary>
    /// Checked every second to see if sound can be thrown out
    /// (if 0, then decrement, if -1, then throw out, if &gt; 0, in use).
    /// </summary>
    public int Usefulness { get; set; }

    /// <summary>Lump number of sfx (-1 = not yet looked up).</summary>
    public int LumpNum { get; set; }

    public SfxInfo(string name, bool singularity, int priority, SfxInfo? link = null, int pitch = -1, int volume = -1)
    {
        Name = name;
        Singularity = singularity;
        Priority = priority;
        Link = link;
        Pitch = pitch;
        Volume = volume;
        LumpNum = -1;
    }
}

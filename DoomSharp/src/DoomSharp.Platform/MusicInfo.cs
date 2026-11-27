namespace DoomSharp.Platform;

/// <summary>
/// Per-music descriptor — port of <c>musicinfo_t</c> (sounds.h:70-84).
/// </summary>
public sealed class MusicInfo
{
    /// <summary>Up to 6-character name (the D_* lump is "d_" + name).</summary>
    public string Name { get; }

    /// <summary>Lump number of music (0 = not yet looked up, as in C).</summary>
    public int LumpNum { get; set; }

    /// <summary>Music data (raw MUS lump), set when registered.</summary>
    public byte[]? Data { get; set; }

    /// <summary>Music handle once registered.</summary>
    public int Handle { get; set; }

    public MusicInfo(string name)
    {
        Name = name;
    }
}

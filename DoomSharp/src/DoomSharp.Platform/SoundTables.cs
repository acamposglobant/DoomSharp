namespace DoomSharp.Platform;

/// <summary>
/// The complete set of sound effects and music — port of <c>sounds.c</c>.
/// </summary>
/// <remarks>
/// <para>
/// The C tables are static struct arrays with string names and link pointers
/// (<c>&amp;S_sfx[sfx_pistol]</c>). Here the link is resolved after
/// construction by index. <c>S_sfx[0]</c> is a dummy, as in C.
/// </para>
/// </remarks>
public static class SoundTables
{
    /// <summary>The complete set of music (S_music, sounds.c:37-107).</summary>
    public static readonly MusicInfo[] Music = BuildMusic();

    /// <summary>The complete set of sound effects (S_sfx, sounds.c:114-227).</summary>
    public static readonly SfxInfo[] Sfx = BuildSfx();

    private static MusicInfo[] BuildMusic()
    {
        // Order and names exactly as sounds.c:39-107.
        string[] names =
        [
            "e1m1", "e1m2", "e1m3", "e1m4", "e1m5", "e1m6", "e1m7", "e1m8", "e1m9",
            "e2m1", "e2m2", "e2m3", "e2m4", "e2m5", "e2m6", "e2m7", "e2m8", "e2m9",
            "e3m1", "e3m2", "e3m3", "e3m4", "e3m5", "e3m6", "e3m7", "e3m8", "e3m9",
            "inter", "intro", "bunny", "victor", "introa",
            "runnin", "stalks", "countd", "betwee", "doom", "the_da", "shawn",
            "ddtblu", "in_cit", "dead", "stlks2", "theda2", "doom2", "ddtbl2",
            "runni2", "dead2", "stlks3", "romero", "shawn2", "messag", "count2",
            "ddtbl3", "ampie", "theda3", "adrian", "messg2", "romer2", "tense",
            "shawn3", "openin", "evil", "ultima", "read_m", "dm2ttl", "dm2int",
        ];

        var music = new MusicInfo[names.Length + 1];
        for (int i = 0; i < names.Length; i++)
        {
            music[i + 1] = new MusicInfo(names[i]);
        }
        return music;
    }

    private static SfxInfo[] BuildSfx()
    {
        // (name, singularity, priority) — sounds.c:119-226.
        (string Name, bool Singularity, int Priority)[] entries =
        [
            ("pistol", false, 64),
            ("shotgn", false, 64),
            ("sgcock", false, 64),
            ("dshtgn", false, 64),
            ("dbopn", false, 64),
            ("dbcls", false, 64),
            ("dbload", false, 64),
            ("plasma", false, 64),
            ("bfg", false, 64),
            ("sawup", false, 64),
            ("sawidl", false, 118),
            ("sawful", false, 64),
            ("sawhit", false, 64),
            ("rlaunc", false, 64),
            ("rxplod", false, 70),
            ("firsht", false, 70),
            ("firxpl", false, 70),
            ("pstart", false, 100),
            ("pstop", false, 100),
            ("doropn", false, 100),
            ("dorcls", false, 100),
            ("stnmov", false, 119),
            ("swtchn", false, 78),
            ("swtchx", false, 78),
            ("plpain", false, 96),
            ("dmpain", false, 96),
            ("popain", false, 96),
            ("vipain", false, 96),
            ("mnpain", false, 96),
            ("pepain", false, 96),
            ("slop", false, 78),
            ("itemup", true, 78),
            ("wpnup", true, 78),
            ("oof", false, 96),
            ("telept", false, 32),
            ("posit1", true, 98),
            ("posit2", true, 98),
            ("posit3", true, 98),
            ("bgsit1", true, 98),
            ("bgsit2", true, 98),
            ("sgtsit", true, 98),
            ("cacsit", true, 98),
            ("brssit", true, 94),
            ("cybsit", true, 92),
            ("spisit", true, 90),
            ("bspsit", true, 90),
            ("kntsit", true, 90),
            ("vilsit", true, 90),
            ("mansit", true, 90),
            ("pesit", true, 90),
            ("sklatk", false, 70),
            ("sgtatk", false, 70),
            ("skepch", false, 70),
            ("vilatk", false, 70),
            ("claw", false, 70),
            ("skeswg", false, 70),
            ("pldeth", false, 32),
            ("pdiehi", false, 32),
            ("podth1", false, 70),
            ("podth2", false, 70),
            ("podth3", false, 70),
            ("bgdth1", false, 70),
            ("bgdth2", false, 70),
            ("sgtdth", false, 70),
            ("cacdth", false, 70),
            ("skldth", false, 70),
            ("brsdth", false, 32),
            ("cybdth", false, 32),
            ("spidth", false, 32),
            ("bspdth", false, 32),
            ("vildth", false, 32),
            ("kntdth", false, 32),
            ("pedth", false, 32),
            ("skedth", false, 32),
            ("posact", true, 120),
            ("bgact", true, 120),
            ("dmact", true, 120),
            ("bspact", true, 100),
            ("bspwlk", true, 100),
            ("vilact", true, 100),
            ("noway", false, 78),
            ("barexp", false, 60),
            ("punch", false, 64),
            ("hoof", false, 70),
            ("metal", false, 70),
            ("chgun", false, 64),   // link to pistol, pitch 150 — resolved below
            ("tink", false, 60),
            ("bdopn", false, 100),
            ("bdcls", false, 100),
            ("itmbk", false, 100),
            ("flame", false, 32),
            ("flamst", false, 32),
            ("getpow", false, 60),
            ("bospit", false, 70),
            ("boscub", false, 70),
            ("bossit", false, 70),
            ("bospn", false, 70),
            ("bosdth", false, 70),
            ("manatk", false, 70),
            ("mandth", false, 70),
            ("sssit", false, 70),
            ("ssdth", false, 70),
            ("keenpn", false, 70),
            ("keendt", false, 70),
            ("skeact", false, 70),
            ("skesit", false, 70),
            ("skeatk", false, 70),
            ("radio", false, 60),
        ];

        var sfx = new SfxInfo[SfxId.NumSfx];

        // S_sfx[0] needs to be a dummy for odd reasons (sounds.c:116-117).
        sfx[0] = new SfxInfo("none", false, 0);

        for (int i = 0; i < entries.Length; i++)
        {
            sfx[i + 1] = new SfxInfo(entries[i].Name, entries[i].Singularity, entries[i].Priority);
        }

        // chgun links to pistol at pitch 150 (sounds.c:204).
        sfx[SfxId.Chgun].Link = sfx[SfxId.Pistol];
        sfx[SfxId.Chgun].Pitch = 150;
        sfx[SfxId.Chgun].Volume = 0;

        return sfx;
    }
}

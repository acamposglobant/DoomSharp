// Emacs style mode select   -*- C# -*-
//-----------------------------------------------------------------------------
//
// Copyright (C) 1993-1996 by id Software, Inc.
//
// This source is available for distribution and/or modification
// only under the terms of the GNU General Public License 2.0 as
// published by the Free Software Foundation.
//
// DESCRIPTION:
//      DOOM-040a interactive smoke test: loads doom1.wad (shareware),
//      renders E1M1 with the DOOM-030 software renderer, presents the
//      320x200 indexed framebuffer through the PLAYPAL/gamma RGBA
//      converter, and lets the user move/turn the camera with the
//      arrow keys (WASD aliases). ESC quits.
//
//-----------------------------------------------------------------------------

using DoomSharp.App;
using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Game;
using DoomSharp.Platform;
using DoomSharp.Rendering;
using Serilog;

// ADR-004: Information+ on console; Verbose traces stay available via
// the minimum level for diagnostics.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Verbose()
    .WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
    .CreateLogger();

// ---- argument parsing -----------------------------------------------------
var iwadPath = Environment.GetEnvironmentVariable("DOOMWADDIR") is { } dir
    ? Path.Combine(dir, "doom1.wad")
    : "doom1.wad";
var scale = 2;

var cliArgs = args.ToList();
for (var i = 0; i < cliArgs.Count; i++)
{
    if (cliArgs[i] is "-iwad" or "--iwad" && i + 1 < cliArgs.Count)
    {
        iwadPath = cliArgs[i + 1];
        i++;
    }
    else if (cliArgs[i] is "-scale" && i + 1 < cliArgs.Count)
    {
        scale = int.Parse(cliArgs[i + 1]);
        i++;
    }
}

if (!File.Exists(iwadPath))
{
    Console.Error.WriteLine($"IWAD not found: {iwadPath}");
    Console.Error.WriteLine("Usage: dotnet run --project src/DoomSharp.App -- -iwad <path-to-doom1.wad> [-scale 1..4]");
    Console.Error.WriteLine("       (or set DOOMWADDIR to the directory containing doom1.wad)");
    return 1;
}

// ---- WAD + level load (DOOM-020 loaders) ----------------------------------
using var zone = new ZoneMemory();
using var wad = new WadDirectory();
wad.AddFile(iwadPath);
Log.Information("Loaded {Path} ({Lumps} lumps)", iwadPath, wad.NumLumps);

var textures = new TextureManager();
textures.InitTextures(wad, zone);
textures.InitFlats(wad, zone);
textures.InitSpriteLumps(wad, zone);
textures.InitColormaps(wad, zone);

var mapLump = wad.GetNumForName("E1M1");
var level = MapLoaders.LoadLevel(wad, mapLump, zone);
Log.Information("Loaded E1M1: {Sectors} sectors, {Lines} lines, {Segs} segs",
    level.Sectors.Length, level.Lines.Length, level.Segs.Length);

// ---- renderer setup (DOOM-030) --------------------------------------------
Video.Init();
var view = new ViewSetup(textures);
view.Colormaps = textures.Colormaps;
ViewSetup.ColormapsGlobal = view.Colormaps;
view.SetViewSize(11, 0);            // full-screen 320x200 view
view.ExecuteSetViewSize();

SpriteDefs.InitSprites(view, wad, InfoTables.SpriteNames);
SkyRenderer.InitSkyMap();

var scene = new SceneRenderer(wad, level, textures, view);

// ---- platform host (DOOM-040a) --------------------------------------------
using var host = new SilkWindowHost(scale);
host.Initialize();

var playpal = wad.CacheLumpName("PLAYPAL");
var palette = PaletteConverter.GetPalette(playpal, 0);
var rgba = new byte[PaletteConverter.ScreenWidth * PaletteConverter.ScreenHeight * 4];
var queue = new EventQueue();

// ---- audio (DOOM-040b) ------------------------------------------------------
// Live OpenAL output when a device exists; headless fallback otherwise.
IAudioOutput audioOutput = OpenAlAudioOutput.TryCreate();
if (!audioOutput.IsLive)
{
    audioOutput.Dispose();
    audioOutput = new HeadlessAudioOutput();
}

var mixer = new SoundMixer();
var random = new DoomRandom();
var soundManager = new SoundManager(
    mixer,
    pointToAngle2: RendererMath.PointToAngle2,
    fineSine: fineIndex => AngleTables.FineSine[fineIndex],
    random: () => random.MRandom(),
    lookupMusicLump: name => wad.CheckNumForName(name),
    numChannels: SoundManager.DefaultNumChannels,
    gameMap: 1);
soundManager.Init(sfxVolume: 15, musicVolume: 15);

// Pre-cache all SFX data (I_InitSound, i_sound.c:799-813): load each DS*
// lump, parse the DMX header, pad, and share linked sounds' data.
for (int i = 1; i < SfxId.NumSfx; i++)
{
    var sfx = SoundTables.Sfx[i];
    if (sfx.Link is not null)
    {
        continue; // resolved after the loop, like the C's second pass
    }
    var lumpName = "ds" + sfx.Name;
    var lumpNum = wad.CheckNumForName(lumpName);
    if (lumpNum < 0)
    {
        lumpNum = wad.GetNumForName("dspistol"); // missing-lump fallback
    }
    var dmx = DmxSound.Parse(wad.CacheLumpNum(lumpNum));
    sfx.Data = dmx.Samples;
    sfx.Length = dmx.Samples.Length;
}

// Resolve aliases after loading (i_sound.c:807-812).
for (int i = 1; i < SfxId.NumSfx; i++)
{
    var sfx = SoundTables.Sfx[i];
    if (sfx.Link is { } link && sfx.Data is null)
    {
        sfx.Data = link.Data;
        sfx.Length = link.Length;
    }
}

var mixBuffer = new short[SoundMixer.BufferSize];
var listener = new SoundOrigin(0, 0, 0u);
soundManager.Listener = listener;

// ---- game simulation (DOOM-021a) -------------------------------------------
// P_SetupLevel-era init: game-side level state, then spawn all map things
// (P_SpawnMapThing) including the player (P_SpawnPlayer).
if (level.Things.Length == 0)
{
    Console.Error.WriteLine("No things found in E1M1");
    return 1;
}

GameLevel.Init(level);
MapObject.PRandom = () => random.PRandom();
MapObject.GameSkill = SkillKind.Medium;

// Resolve skyflatnum the way R_InitSkyMap does (r_sky.c:62-66).
MapObject.SkyFlatNum = textures.FlatNumForName(wad, "F_SKY1");

var player = new Player();
Thinker.InitThinkers(ref MapObject.ThinkerCap);
foreach (var thing in level.Things)
{
    MapObject.SpawnMapThing(thing);
}

// P_SpawnPlayer created the player mobj (MT_PLAYER is the first thinker);
// wire the Player object to it.
var firstMobj = MapObject.ThinkerCap.Next as Mobj;
firstMobj!.Player = player;
player.Mo = firstMobj;
player.ViewHeight = Player.ViewHeightConst;


Log.Information("Starting render loop — arrows/WASD to move, SPACE fires, ESC to quit");

// ---- main loop ------------------------------------------------------------
while (!host.ShouldClose)
{
    host.PumpEvents();
    host.Poll(queue);

    // Build the tic command from input (the game responder's movement part).
    var forward = 0;
    var side = 0; _ = side;
    var turn = 0;

    while (queue.TryPop(out var ev))
    {
        if (ev.Type == EventType.KeyDown)
        {
            switch (ev.Data1)
            {
                case DoomKeys.UpArrow or 'w': forward = 1; break;
                case DoomKeys.DownArrow or 's': forward = -1; break;
                case DoomKeys.LeftArrow or 'a': turn = -1; break;
                case DoomKeys.RightArrow or 'd': turn = 1; break;
                case DoomKeys.Escape:
                    host.Dispose();
                    return 0;
                case ' ':
                    // Demo SFX trigger: fire the pistol at the listener.
                    soundManager.StartSound(listener, SfxId.Pistol);
                    break;
            }
        }
    }

    // TicCmd (d_ticcmd.h): forwardmove/sidemove *2048, angleturn <<16.
    player.Cmd.ForwardMove = (sbyte)(forward * 25);   // run speed
    player.Cmd.SideMove = 0;
    player.Cmd.AngleTurn = (short)(turn * 4);         // <<16 in P_MovePlayer

    // P_Ticker: player think + thinkers + leveltime.
    GameTicker.Ticker(player);

    // Render (DOOM-030 pipeline) from the live player mobj.
    var mo = player.Mo!;
    scene.RenderPlayerView(mo.X, mo.Y, player.ViewZ, mo.Angle, SpriteDefs.Sprites);

    // Audio: refresh listener, mix one 512-frame block, submit to device.
    listener = new SoundOrigin(mo.X, mo.Y, mo.Angle);
    soundManager.Listener = listener;
    soundManager.UpdateSounds(listener);
    mixer.UpdateSound(mixBuffer);
    audioOutput.Submit(mixBuffer);

    // Present: indexed -> RGBA with palette 0 + current gamma.
    PaletteConverter.Convert(Video.Screens[0], palette, Video.GammaTable[Video.UseGamma], rgba);
    host.Present(rgba);
}

return 0;

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

SpriteDefs.InitSprites(view, wad, ["TROO", "SARG", "PLAY", "POSS", "SHTG", "PUNG", "MEDI", "BON1", "BON2", "ARM1", "ARM2", "SOUL", "CLIP", "SHEL"]);
SkyRenderer.InitSkyMap();

var scene = new SceneRenderer(wad, level, textures, view);

// ---- platform host (DOOM-040a) --------------------------------------------
using var host = new SilkWindowHost(scale);
host.Initialize();

var playpal = wad.CacheLumpName("PLAYPAL");
var palette = PaletteConverter.GetPalette(playpal, 0);
var rgba = new byte[PaletteConverter.ScreenWidth * PaletteConverter.ScreenHeight * 4];
var queue = new EventQueue();

// Player state: first deathmatch/player start as a placeholder until
// DOOM-021 spawns the real player.
if (level.Things.Length == 0)
{
    Console.Error.WriteLine("No things found in E1M1");
    return 1;
}

var start = level.Things[0];
int posX = start.X << 16, posY = start.Y << 16;
var angle = (uint)(start.Angle << 16);   // mapthing angle byte -> BAM

const int moveSpeed = 4 << 16;           // per frame, fixed
const uint turnSpeed = 2u << 25;         // per frame, BAM

Log.Information("Starting render loop — arrows/WASD to move, ESC to quit");

// ---- main loop ------------------------------------------------------------
while (!host.ShouldClose)
{
    host.PumpEvents();
    host.Poll(queue);

    // Simple camera control (not the real game responder — DOOM-021).
    var forward = 0;
    var side = 0;
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
            }
        }
    }

    // Move in the view direction (fine trig from the BAM angle).
    var fineAngle = (int)(angle >> AngleTables.AngleToFineShift);
    var sin = AngleTables.FineSine[fineAngle];
    var cos = AngleTables.FineCosine[fineAngle];
    posX += (cos * (forward * moveSpeed)) >> 16;
    posX += (sin * (side * moveSpeed)) >> 16;
    posY += (sin * (forward * moveSpeed)) >> 16;
    posY -= (cos * (side * moveSpeed)) >> 16;
    angle += (uint)(turn * (int)turnSpeed);

    // Render (DOOM-030 pipeline).
    scene.RenderPlayerView(posX, posY, 41 << 16, angle, SpriteDefs.Sprites);

    // Present: indexed -> RGBA with palette 0 + current gamma.
    PaletteConverter.Convert(Video.Screens[0], palette, Video.GammaTable[Video.UseGamma], rgba);
    host.Present(rgba);
}

return 0;

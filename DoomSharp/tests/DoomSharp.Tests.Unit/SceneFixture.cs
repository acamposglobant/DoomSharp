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
//      Shared WAD + TextureManager for the scene renderer tests.
//      The fixture WAD provides a 128-tall texture (matching the C
//      harness's textureheight[1] = 128), flats, a sprite and a
//      COLORMAP.
//
//-----------------------------------------------------------------------------

using System.Buffers.Binary;
using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;

namespace DoomSharp.Tests.Unit;

/// <summary>
/// Lazily builds the scene WAD once per test process (tests that use
/// it run inside the serialized "RendererSerial" collection).
/// </summary>
internal static class SceneFixture
{
    private static readonly object Lock = new();
    private static string? _tempDir;
    private static WadDirectory? _wad;
    private static TextureManager? _textures;

    public static WadDirectory Wad
    {
        get
        {
            Ensure();
            return _wad!;
        }
    }

    public static TextureManager Textures
    {
        get
        {
            Ensure();
            return _textures!;
        }
    }

    private static void Ensure()
    {
        if (_wad != null)
        {
            return;
        }

        lock (Lock)
        {
            if (_wad != null)
            {
                return;
            }

            _tempDir = Path.Combine(Path.GetTempPath(), "doomsharp_scenefixture", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            // 128-tall patch so the &127 column mask in DrawColumn stays
            // in bounds (the C harness uses a 128-byte column buffer and
            // textureheight[1] = 128).
            var patch = TextureWadBuilder.Patch(64, 128);

            var lumps = new List<(string Name, byte[] Data)>
            {
                ("PPSTART", []),
                ("WALL0000", patch),
                ("PPEND", []),
                ("PNAMES", TextureWadBuilder.PNames("WALL0000")),
                ("TEXTURE1", TextureWadBuilder.TextureX(
                    ("WALLTEX0", 64, 128, [(0, 0, 0)]),
                    ("WALLTEX1", 64, 128, [(0, 0, 0)]))),
                ("F_START", []),
                ("FLAT1", new byte[4096]),
                ("FLAT2", new byte[4096]),
                ("F_END", []),
                ("S_START", []),
                ("SPR1A0", TextureWadFixture.SpritePatch(4, 128, 2, 1)),
                ("S_END", []),
                ("COLORMAP", TextureWadBuilder.Colormap()),
            };
            var path = Path.Combine(_tempDir, "scenetest.wad");
            TextureWadFixture.WriteWad(path, lumps);

            _wad = new WadDirectory();
            _wad.InitMultipleFiles([path]);
            _textures = new TextureManager();
            var zone = new ZoneMemory();
            _textures.InitTextures(_wad, zone);
            _textures.InitFlats(_wad, zone);
            _textures.InitSpriteLumps(_wad, zone);
            _textures.InitColormaps(_wad, zone);
        }
    }
}

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
//      Sprite definition tables (r_defs.h:427-453) and the sprite
//      initialization slice of r_things.c: R_InstallSpriteLump,
//      R_InitSpriteDefs, R_InitSprites.
//      See docs/adr/ADR-004 for tracing; DOOM-030c plan for the
//      documented deviations.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using Serilog;

namespace DoomSharp.Rendering;

/// <summary>spriteframe_t (r_defs.h:427-440).</summary>
public sealed class SpriteFrame
{
    /// <summary>If false use 0 for any position (r_defs.h:429-432).</summary>
    public bool Rotate;

    /// <summary>Lump to use for view angles 0-7 (r_defs.h:434-435).</summary>
    public readonly int[] Lump = new int[8];

    /// <summary>Flip bit (1 = flip) to use for view angles 0-7 (r_defs.h:438).</summary>
    public readonly byte[] Flip = new byte[8];

    /// <summary>memset(sprtemp, -1, sizeof(sprtemp)) per sprite (r_things.c:200).</summary>
    public void Reset()
    {
        Rotate = false;
        Array.Fill(Lump, -1);
        Array.Fill(Flip, (byte)0);
    }
}

/// <summary>spritedef_t (r_defs.h:448-453).</summary>
public sealed class SpriteDef
{
    public int NumFrames;
    public SpriteFrame[] Frames = [];
}

/// <summary>
/// Sprite definition tables and R_InitSprites (r_things.c:80-410).
/// </summary>
public static class SpriteDefs
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(SpriteDefs));

    /// <summary>sprtemp[29] (r_things.c:88).</summary>
    private static readonly SpriteFrame[] SprTemp = new SpriteFrame[29];

    /// <summary>maxframe (r_things.c:89).</summary>
    private static int _maxFrame;

    /// <summary>spritename (r_things.c:90).</summary>
    private static string _spriteName = string.Empty;

    static SpriteDefs()
    {
        for (var i = 0; i < SprTemp.Length; i++)
        {
            SprTemp[i] = new SpriteFrame();
        }
    }

    /// <summary>sprites[] (r_things.c:85).</summary>
    public static SpriteDef[] Sprites { get; private set; } = [];

    /// <summary>numsprites (r_things.c:86).</summary>
    public static int NumSprites => Sprites.Length;

    /// <summary>
    /// R_InitSprites (r_things.c:377-386): fill negonearray and build
    /// the sprite rotation tables.
    /// </summary>
    public static void InitSprites(ViewSetup view, WadDirectory wad, string[] namelist)
    {
        for (var i = 0; i < Video.ScreenWidth; i++)
        {
            view.NegOneArray[i] = -1;
        }

        InitSpriteDefs(wad, namelist);
        Log.Verbose("InitSprites({Count} sprites)", NumSprites);
    }

    // ------------------------------------------------------------------
    // R_InstallSpriteLump (r_things.c:100-152)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_InstallSpriteLump (r_things.c:100-152): install one sprite
    /// lump into sprtemp, validating rotation consistency.
    /// </summary>
    private static void InstallSpriteLump(int lump, int frame, int rotation, bool flipped, int firstSpriteLump)
    {
        if (frame >= 29 || rotation > 8)
        {
            throw new DoomFatalException($"R_InstallSpriteLump: Bad frame characters in lump {lump}");
        }

        if (frame > _maxFrame)
        {
            _maxFrame = frame;
        }

        if (rotation == 0)
        {
            // the lump should be used for all rotations (r_things.c:114-131)
            if (SprTemp[frame].Rotate == false && SprTemp[frame].Lump[0] != -1)
            {
                throw new DoomFatalException(
                    $"R_InitSprites: Sprite {_spriteName} frame {(char)('A' + frame)} has multip rot=0 lump");
            }

            if (SprTemp[frame].Rotate)
            {
                throw new DoomFatalException(
                    $"R_InitSprites: Sprite {_spriteName} frame {(char)('A' + frame)} has rotations and a rot=0 lump");
            }

            SprTemp[frame].Rotate = false;
            for (var r = 0; r < 8; r++)
            {
                SprTemp[frame].Lump[r] = lump - firstSpriteLump;
                SprTemp[frame].Flip[r] = (byte)(flipped ? 1 : 0);
            }

            return;
        }

        // the lump is only used for one rotation (r_things.c:133-149)
        if (SprTemp[frame].Rotate == false)
        {
            throw new DoomFatalException(
                $"R_InitSprites: Sprite {_spriteName} frame {(char)('A' + frame)} has rotations and a rot=0 lump");
        }

        SprTemp[frame].Rotate = true;

        // make 0 based (r_things.c:136)
        rotation--;
        if (SprTemp[frame].Lump[rotation] != -1)
        {
            throw new DoomFatalException(
                $"R_InitSprites: Sprite {_spriteName} : {(char)('A' + frame)} : {(char)('1' + rotation)} has two lumps mapped to it");
        }

        SprTemp[frame].Lump[rotation] = lump - firstSpriteLump;
        SprTemp[frame].Flip[rotation] = (byte)(flipped ? 1 : 0);
    }

    // ------------------------------------------------------------------
    // R_InitSpriteDefs (r_things.c:160-310)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_InitSpriteDefs (r_things.c:160-310): pass a null terminated
    /// list of sprite names (4 chars exactly) to be used. Builds the
    /// sprite rotation matrices to account for horizontally flipped
    /// sprites.
    /// </summary>
    public static void InitSpriteDefs(WadDirectory wad, string[] namelist)
    {
        var numSprites = namelist.Length;
        if (numSprites == 0)
        {
            return;
        }

        Sprites = new SpriteDef[numSprites];
        for (var i = 0; i < numSprites; i++)
        {
            Sprites[i] = new SpriteDef();
        }

        var firstSpriteLump = wad.GetNumForName("S_START") + 1;
        var lastSpriteLump = wad.GetNumForName("S_END") - 1;

        // scan all the lump names for each of the names, noting the
        // highest frame letter (r_things.c:196-243)
        for (var i = 0; i < numSprites; i++)
        {
            _spriteName = namelist[i];
            foreach (var frame in SprTemp)
            {
                frame.Reset();
            }

            _maxFrame = -1;

            // scan the lumps, filling in the frames for whatever is
            // found (r_things.c:203-226)
            for (var l = firstSpriteLump; l <= lastSpriteLump; l++)
            {
                var name = wad.Lumps[l].Name;
                if (name.Length < 6 || name.Substring(0, 4) != _spriteName)
                {
                    continue;
                }

                var frame = name[4] - 'A';
                var rotation = name[5] - '0';

                InstallSpriteLump(l, frame, rotation, flipped: false, firstSpriteLump);

                if (name.Length > 6 && name[6] != 0)
                {
                    frame = name[6] - 'A';
                    rotation = name[7] - '0';
                    InstallSpriteLump(l, frame, rotation, flipped: true, firstSpriteLump);
                }
            }

            // check the frames that were found for completeness
            // (r_things.c:246-283)
            if (_maxFrame == -1)
            {
                Sprites[i].NumFrames = 0;
                continue;
            }

            _maxFrame++;

            for (var frame = 0; frame < _maxFrame; frame++)
            {
                switch (SprTemp[frame].Rotate)
                {
                    case false when SprTemp[frame].Lump[0] == -1:
                        // no rotations were found for that frame at all
                        throw new DoomFatalException(
                            $"R_InitSprites: No patches found for {_spriteName} frame {(char)('A' + frame)}");
                    case false:
                        // only the first rotation is needed
                        break;
                    case true:
                        // must have all 8 frames
                        for (var rotation = 0; rotation < 8; rotation++)
                        {
                            if (SprTemp[frame].Lump[rotation] == -1)
                            {
                                throw new DoomFatalException(
                                    $"R_InitSprites: Sprite {_spriteName} frame {(char)('A' + frame)} is missing rotations");
                            }
                        }

                        break;
                }
            }

            // allocate space for the frames present and copy sprtemp
            // (r_things.c:286-290)
            Sprites[i].NumFrames = _maxFrame;
            Sprites[i].Frames = new SpriteFrame[_maxFrame];
            for (var f = 0; f < _maxFrame; f++)
            {
                Sprites[i].Frames[f] = SprTemp[f];
            }
        }
    }
}

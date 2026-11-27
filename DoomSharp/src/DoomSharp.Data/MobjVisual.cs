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
//      Render-facing subset of mobj_t (r_defs.h:100-148 / p_mobj.h).
//      DOOM-030c bridge: the renderer consumes things through this
//      record instead of the full game object (P_SpawnMapThing and
//      the mobj lifecycle are DOOM-021+). The game layer will build
//      MobjVisual records from live mobjs; tests construct them
//      directly.
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Data;

/// <summary>mobjflag_t bits used by the renderer (p_mobj.h:150-202).</summary>
public static class MobjFlags
{
    /// <summary>MF_SHADOW (p_mobj.h:167) — fuzzy draw (spectres).</summary>
    public const int Shadow = 0x40000;

    /// <summary>MF_TRANSLATION (p_mobj.h:199) — player color mask.</summary>
    public const int Translation = 0xc000000;

    /// <summary>MF_TRANSSHIFT (p_mobj.h:201).</summary>
    public const int TransShift = 26;
}

/// <summary>thing frame flags (p_pspr.h:50-51).</summary>
public static class ThingFrame
{
    /// <summary>FF_FULLBRIGHT — full bright frame.</summary>
    public const int FullBright = unchecked((int)0x8000);

    /// <summary>FF_FRAMEMASK — frame number mask.</summary>
    public const int FrameMask = 0x7fff;
}

/// <summary>
/// The renderer's view of a thing (mobj_t fields consumed by
/// R_ProjectSprite, r_things.c:452-604). Coordinates are fixed-point;
/// <see cref="Sprite"/> and <see cref="Frame"/> index the sprite
/// definition tables built by the sprite-init slice.
/// </summary>
public sealed class MobjVisual
{
    public int X;               // fixed
    public int Y;               // fixed
    public int Z;               // fixed
    public uint Angle;          // angle_t BAM
    public int Sprite;          // index into spritedef_t table
    public int Frame;           // frame + FF_FULLBRIGHT
    public int Flags;           // mobjflag_t bits
    public int ValidCount;      // sec->validcount bookkeeping lives on the sector side
}

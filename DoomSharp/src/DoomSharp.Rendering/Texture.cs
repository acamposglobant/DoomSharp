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
//      Renderer data types (r_data.h / r_defs.h texture subset).
//      texture_t / texpatch_t with managed ownership.
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Rendering;

/// <summary>texpatch_t (r_data.h:36-41) — one patch in a composite texture.</summary>
public sealed class TexPatch
{
    public int OriginX;   // SHORT(mpatch->originx)
    public int OriginY;   // SHORT(mpatch->originy)
    public int Patch;     // patch lump index (patchlookup), -1 = missing
}

/// <summary>texture_t (r_data.h:96-110) — composite texture definition.</summary>
public sealed class Texture
{
    public byte[] Name = new byte[8];  // raw 8-byte name
    public int Width;                  // SHORT(mtexture->width)
    public int Height;                 // SHORT(mtexture->height)
    public int PatchCount;
    public TexPatch[] Patches = [];

    /// <summary>ASCII name for diagnostics (NUL-terminated).</summary>
    public string NameString
    {
        get
        {
            var end = Array.IndexOf(Name, (byte)0);
            if (end < 0) end = Name.Length;
            return System.Text.Encoding.ASCII.GetString(Name, 0, end);
        }
    }
}

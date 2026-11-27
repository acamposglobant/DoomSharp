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
//      Doom key codes (doomdef.h:250-280). Values are the original
//      constants: printable ASCII for text keys, 0x80+PC-scancode for
//      extended keys.
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Platform;

/// <summary>Key codes as consumed by the game (doomdef.h:250-280).</summary>
public static class DoomKeys
{
    public const int RightArrow = 0xae;
    public const int LeftArrow = 0xac;
    public const int UpArrow = 0xad;
    public const int DownArrow = 0xaf;
    public const int Escape = 27;
    public const int Enter = 13;
    public const int Tab = 9;

    public const int F1 = 0x80 + 0x3b;
    public const int F2 = 0x80 + 0x3c;
    public const int F3 = 0x80 + 0x3d;
    public const int F4 = 0x80 + 0x3e;
    public const int F5 = 0x80 + 0x3f;
    public const int F6 = 0x80 + 0x40;
    public const int F7 = 0x80 + 0x41;
    public const int F8 = 0x80 + 0x42;
    public const int F9 = 0x80 + 0x43;
    public const int F10 = 0x80 + 0x44;
    public const int F11 = 0x80 + 0x57;
    public const int F12 = 0x80 + 0x58;

    public const int Backspace = 127;
    public const int Pause = 0xff;

    public const int Equals_ = 0x3d;
    public const int Minus = 0x2d;

    public const int RShift = 0x80 + 0x36;
    public const int RCtrl = 0x80 + 0x1d;
    public const int RAlt = 0x80 + 0x38;

    public const int LAlt = RAlt;
}

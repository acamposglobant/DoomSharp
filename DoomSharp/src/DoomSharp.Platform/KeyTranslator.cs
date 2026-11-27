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
//      Platform-neutral key identifiers for KeyTranslator (the port of
//      xlatekey, i_video.c:97-162). The window adapter maps its native
//      key enum onto this one; the translator itself stays pure managed
//      code so it is unit-testable headless.
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Platform;

/// <summary>
/// Platform-neutral key set consumed by <see cref="KeyTranslator"/> —
/// the analogue of the X11 keysyms xlatekey() switched on.
/// </summary>
public enum PlatformKey
{
    None = 0,

    // Named keys (XK_* cases in xlatekey). Values start at 0x100 so
    // they never collide with the printable ASCII range below.
    Left = 0x100,
    Right,
    Down,
    Up,
    Escape,
    Return,
    Tab,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Backspace,
    Delete,
    Pause,
    KpEqual,
    KpSubtract,
    Equal,
    Minus,
    ShiftL,
    ShiftR,
    ControlL,
    ControlR,
    AltL,
    AltR,
    MetaL,
    MetaR,

    // Printable range: values equal the ASCII code (' '..'~').
    Space = 0x20,
    AsciiTilde = 0x7e,
}

/// <summary>
/// xlatekey (i_video.c:97-162): platform key → Doom key code.
/// </summary>
public static class KeyTranslator
{
    /// <summary>XK_space (i_video.c:153).</summary>
    private const int XkSpace = ' ';

    /// <summary>XK_asciitilde (i_video.c:153).</summary>
    private const int XkAsciiTilde = '~';

    /// <summary>
    /// xlatekey (i_video.c:97-162). Named keys map to their Doom
    /// constants; the printable range maps space..tilde to ' '..'~'
    /// and folds A-Z to lowercase.
    /// </summary>
    public static int XlateKey(PlatformKey key)
    {
        switch (key)
        {
            case PlatformKey.Left: return DoomKeys.LeftArrow;
            case PlatformKey.Right: return DoomKeys.RightArrow;
            case PlatformKey.Down: return DoomKeys.DownArrow;
            case PlatformKey.Up: return DoomKeys.UpArrow;
            case PlatformKey.Escape: return DoomKeys.Escape;
            case PlatformKey.Return: return DoomKeys.Enter;
            case PlatformKey.Tab: return DoomKeys.Tab;
            case PlatformKey.F1: return DoomKeys.F1;
            case PlatformKey.F2: return DoomKeys.F2;
            case PlatformKey.F3: return DoomKeys.F3;
            case PlatformKey.F4: return DoomKeys.F4;
            case PlatformKey.F5: return DoomKeys.F5;
            case PlatformKey.F6: return DoomKeys.F6;
            case PlatformKey.F7: return DoomKeys.F7;
            case PlatformKey.F8: return DoomKeys.F8;
            case PlatformKey.F9: return DoomKeys.F9;
            case PlatformKey.F10: return DoomKeys.F10;
            case PlatformKey.F11: return DoomKeys.F11;
            case PlatformKey.F12: return DoomKeys.F12;

            case PlatformKey.Backspace:
            case PlatformKey.Delete:
                return DoomKeys.Backspace;

            case PlatformKey.Pause: return DoomKeys.Pause;

            case PlatformKey.KpEqual:
            case PlatformKey.Equal:
                return DoomKeys.Equals_;

            case PlatformKey.KpSubtract:
            case PlatformKey.Minus:
                return DoomKeys.Minus;

            case PlatformKey.ShiftL:
            case PlatformKey.ShiftR:
                return DoomKeys.RShift;

            case PlatformKey.ControlL:
            case PlatformKey.ControlR:
                return DoomKeys.RCtrl;

            case PlatformKey.AltL:
            case PlatformKey.MetaL:
            case PlatformKey.AltR:
            case PlatformKey.MetaR:
                return DoomKeys.RAlt;

            default:
                // Printable range (i_video.c:152-157): map to ASCII,
                // fold uppercase to lowercase.
                var rc = (int)key;
                if (rc >= XkSpace && rc <= XkAsciiTilde)
                {
                    rc = rc - XkSpace + ' ';
                }

                if (rc >= 'A' && rc <= 'Z')
                {
                    rc = rc - 'A' + 'a';
                }

                return rc;
        }
    }
}

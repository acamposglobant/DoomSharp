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
//      Fixed point arithmetic, 32bit as 16.16.
//      Migration of Legacy/linuxdoom-1.10/m_fixed.h and m_fixed.c.
//
//-----------------------------------------------------------------------------

namespace DoomSharp.Core;

/// <summary>
/// Fatal engine error, equivalent to the original <c>I_Error</c> call
/// (i_system.c) which prints a message and terminates the process.
/// Thrown by engine code where the original would abort.
/// The application boundary must catch this and terminate with a
/// non-success exit code.
/// </summary>
public sealed class DoomFatalException : Exception
{
    public DoomFatalException(string message) : base(message)
    {
    }
}

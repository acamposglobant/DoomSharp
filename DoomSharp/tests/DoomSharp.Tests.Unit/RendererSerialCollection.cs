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
//      Test collection forcing renderer tests to run sequentially:
//      the C renderer state (Video.Screens, ColumnDrawState,
//      SpanDrawState, RendererMath globals) is process-global, so
//      parallel classes would corrupt each other's fixtures.
//
//-----------------------------------------------------------------------------

using Xunit;

namespace DoomSharp.Tests.Unit;

[CollectionDefinition("RendererSerial", DisableParallelization = true)]
public sealed class RendererSerialCollection
{
}

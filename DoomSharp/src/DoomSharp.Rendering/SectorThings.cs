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
//      Sector-to-things bridge (DOOM-030c). The C renderer walks
//      sec->thinglist (mobj_t.snext chain); the managed renderer
//      consumes MobjVisual records registered per sector index.
//      The game layer (DOOM-021+) will maintain the registration;
//      tests populate it directly.
//
//-----------------------------------------------------------------------------

using DoomSharp.Data;

namespace DoomSharp.Rendering;

/// <summary>
/// Maps sector indices to the MobjVisual records currently in them
/// (the sec->thinglist / snext chain of the C) and tracks per-sector
/// validcount for R_AddSprites.
/// </summary>
public sealed class SectorThings
{
    private readonly Dictionary<int, List<MobjVisual>> _sectorThings = [];
    private int[] _validCount = [];

    /// <summary>The level whose sectors are indexed.</summary>
    public LevelData Level { get; }

    public SectorThings(LevelData level)
    {
        Level = level;
    }

    /// <summary>Registers a thing into a sector's render list.</summary>
    public void Add(int sectorIndex, MobjVisual thing)
    {
        if (!_sectorThings.TryGetValue(sectorIndex, out var list))
        {
            _sectorThings[sectorIndex] = thing == null ? [] : [thing];
            return;
        }

        _sectorThings[sectorIndex].Add(thing);
    }

    /// <summary>Clears all registrations (level teardown).</summary>
    public void Clear()
    {
        _sectorThings.Clear();
    }

    /// <summary>The things of one sector (empty if none).</summary>
    public IReadOnlyList<MobjVisual> ForSector(int sectorIndex)
    {
        return _sectorThings.TryGetValue(sectorIndex, out var list) ? list : [];
    }

    /// <summary>sec->validcount == validcount check (r_things.c:613-616).</summary>
    public bool IsValidated(int sectorIndex, int validCount) =>
        sectorIndex < _validCount.Length && _validCount[sectorIndex] == validCount;

    /// <summary>sec->validcount = validcount (r_things.c:618).</summary>
    public void MarkValidated(int sectorIndex, int validCount)
    {
        if (_validCount.Length != Level.Sectors.Length)
        {
            _validCount = new int[Level.Sectors.Length];
        }

        _validCount[sectorIndex] = validCount;
    }
}

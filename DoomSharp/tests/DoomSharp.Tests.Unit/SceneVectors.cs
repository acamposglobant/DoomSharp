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
//      Loader for the scene characterization vectors produced by
//      tools/charact/rscenedump.c (r_bsp.c / r_segs.c / r_plane.c /
//      r_things.c over unmodified C source).
//
//-----------------------------------------------------------------------------

using System.Text.Json;

namespace DoomSharp.Tests.Unit;

/// <summary>
/// Lazily loads rscene_vectors.json from tools/charact.
/// </summary>
public static class SceneVectors
{
    private static JsonDocument? _doc;

    public static JsonElement ClipSolid => Load("clipSolid");
    public static JsonElement Planes => Load("planes");
    public static JsonElement CheckPlaneSplit => Load("checkPlaneSplit");
    public static JsonElement CheckPlaneExtend => Load("checkPlaneExtend");
    public static JsonElement StoreWallRange => Load("storeWallRange");
    public static JsonElement ProjectSprite => Load("projectSprite");

    private static JsonElement Load(string section)
    {
        if (_doc == null)
        {
            var json = File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                    "tools", "charact", "rscene_vectors.json"));
            _doc = JsonDocument.Parse(json);
        }

        return _doc.RootElement.GetProperty(section).Clone();
    }
}

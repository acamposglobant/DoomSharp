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
//      Unit tests for DoomRandom (m_random.c) and AngleTables (tables.c).
//      Table values were extracted mechanically from the original C
//      sources and cross-checked against the C characterization dump.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using Xunit;

namespace DoomSharp.Tests.Unit;

public class DoomRandomTests
{
    [Fact]
    public void PRandom_FullWalk_MatchesOriginalSequence()
    {
        // m_random.c:57-61 — first call returns rndtable[1], 256th returns rndtable[0].
        var random = new DoomRandom();
        var expected = new int[256];
        for (var i = 0; i < 256; i++)
        {
            expected[i] = DoomRandom.RndTable[(i + 1) & 0xff];
        }

        for (var i = 0; i < 256; i++)
        {
            Assert.Equal(expected[i], random.PRandom());
        }
        Assert.Equal(0, random.PRndIndex); // wrapped after 256 calls
    }

    [Fact]
    public void MRandom_IndependentOfPStream()
    {
        var random = new DoomRandom();
        var mValues = new int[300];
        for (var i = 0; i < 300; i++)
        {
            mValues[i] = random.MRandom();
        }

        // M stream starts at index 1 too, but is independent of P.
        Assert.Equal(300 % 256, random.RndIndex);
        Assert.Equal(0, random.PRndIndex); // P untouched
    }

    [Fact]
    public void ClearRandom_ResetsBothIndices()
    {
        var random = new DoomRandom();
        random.PRandom();
        random.MRandom();
        random.ClearRandom();

        Assert.Equal(0, random.PRndIndex);
        Assert.Equal(0, random.RndIndex);
        // First call after clear returns rndtable[1] = 8 (m_random.c:32).
        Assert.Equal(8, random.PRandom());
    }

    [Fact]
    public void RndTable_MatchesOriginalC()
    {
        // Spot values from m_random.c:31-51 (verified against C dump).
        Assert.Equal(256, DoomRandom.RndTable.Length);
        Assert.Equal(0, DoomRandom.RndTable[0]);
        Assert.Equal(8, DoomRandom.RndTable[1]);
        Assert.Equal(109, DoomRandom.RndTable[2]);
        Assert.Equal(249, DoomRandom.RndTable[255]);
    }

    [Fact]
    public void PRandom_WrapsAt256()
    {
        var random = new DoomRandom();
        for (var i = 0; i < 256; i++) random.PRandom();
        Assert.Equal(0, random.PRndIndex);
        // First call of the new cycle: index 0 -> 1, returns RndTable[1] = 8.
        Assert.Equal(8, random.PRandom());
        Assert.Equal(1, random.PRndIndex);
    }
}

public class AngleTablesTests
{
    [Fact]
    public void FineSine_HasOriginalLength()
    {
        Assert.Equal(5 * AngleTables.FineAngles / 4, AngleTables.FineSine.Length); // 10240
    }

    [Fact]
    public void FineTangent_HasOriginalLength()
    {
        Assert.Equal(AngleTables.FineAngles / 2, AngleTables.FineTangent.Length); // 4096
    }

    [Fact]
    public void TanToAngle_HasOriginalLength()
    {
        Assert.Equal(AngleTables.SlopeRange + 1, AngleTables.TanToAngle.Length); // 2049
    }

    [Fact]
    public void FineCosine_IsPhaseShiftedFineSine()
    {
        // C: finecosine = finesine + FINEANGLES/4 (tables.h:61).
        // The generated array must equal finesine rotated by FINEANGLES/4.
        for (var i = 0; i < AngleTables.FineSine.Length - AngleTables.FineAngles / 4; i++)
        {
            Assert.Equal(AngleTables.FineSine[i + AngleTables.FineAngles / 4],
                         AngleTables.FineCosine[i]);
        }
    }

    [Fact]
    public void BamConstants_MatchOriginal()
    {
        // tables.h:68-71
        Assert.Equal(0x20000000u, AngleTables.Ang45);
        Assert.Equal(0x40000000u, AngleTables.Ang90);
        Assert.Equal(0x80000000u, AngleTables.Ang180);
        Assert.Equal(0xc0000000u, AngleTables.Ang270);
    }

    [Fact]
    public void FineSine_KnownValues_MatchOriginalC()
    {
        // Values verified against the C characterization dump
        // (tools/charact/vectors.json, op "finesine").
        // Note: the original table peaks at 65535, not FRACUNIT (65536) —
        // a quirk of the original data that must be preserved.
        Assert.Equal(25, AngleTables.FineSine[0]);
        Assert.Equal(65535, AngleTables.FineSine[AngleTables.FineAngles / 4]);
        Assert.Equal(-25, AngleTables.FineSine[AngleTables.FineAngles / 2]);
        Assert.Equal(-65535, AngleTables.FineSine[3 * AngleTables.FineAngles / 4]);
        Assert.Equal(65535, AngleTables.FineSine[5 * AngleTables.FineAngles / 4 - 1]);
    }

    [Fact]
    public void FineTangent_KnownValues_MatchOriginal()
    {
        // Values verified against the C characterization dump
        // (tools/charact/vectors.json, op "finetangent").
        Assert.Equal(-170910304, AngleTables.FineTangent[0]);
        Assert.Equal(25, AngleTables.FineTangent[2048]);
        Assert.Equal(170910304, AngleTables.FineTangent[4095]);
    }
}

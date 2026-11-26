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
//      Tests for R_PointInSubsector (r_main.c BSP walk) over a
//      synthetic 3-node BSP, plus the single-subsector special case.
//
//-----------------------------------------------------------------------------

using DoomSharp.Data;
using DoomSharp.Rendering;
using Xunit;

namespace DoomSharp.Tests.Unit;

[Collection("RendererSerial")]
public class BspLookupTests
{
    /// <summary>
    /// Builds a synthetic level: 4 subsectors split by a vertical
    /// partition at x=0 (root), then each half split by a horizontal
    /// partition at y=0. Children keep the NF_SUBSECTOR bit.
    /// </summary>
    private static LevelData BuildLevel()
    {
        var level = new LevelData();

        level.Subsectors = new[]
        {
            new Subsector { NumSegs = 1, FirstSeg = 0 },   // ss0: x<0, y<0
            new Subsector { NumSegs = 0, FirstSeg = 0 },   // ss1: x<0, y>=0
            new Subsector { NumSegs = 0, FirstSeg = 0 },   // ss2: x>=0, y<0
            new Subsector { NumSegs = 0, FirstSeg = 0 },   // ss3: x>0, y>0
        };

        var nodes = new Node[3];

        // Root: vertical partition x=0, dy>0 → left(x<=0)=child1, right=child0.
        nodes[2] = new Node
        {
            X = 0, Y = 0, Dx = 0, Dy = 1 << 16,
            Children = [0, 1],   // right child 0 = node0, left child 1 = node1
        };

        // Node 0: horizontal partition y=0, dx>0. C semantics for dy==0:
        // y <= node->y → front (0); y > node->y → back (1).
        // front (y<=0) = ss2, back (y>0) = ss3.
        nodes[0] = new Node
        {
            X = 0, Y = 0, Dx = 1 << 16, Dy = 0,
            Children = [(ushort)(NodeFlags.SubSector | 2), (ushort)(NodeFlags.SubSector | 3)],
        };

        // Node 1: horizontal partition y=0, dx>0 → front (y<=0) = ss0,
        // back (y>0) = ss1.
        nodes[1] = new Node
        {
            X = 0, Y = 0, Dx = 1 << 16, Dy = 0,
            Children = [(ushort)(NodeFlags.SubSector | 0), (ushort)(NodeFlags.SubSector | 1)],
        };

        // Root children: right (x>0) → node2.Children[0] = node 0;
        // left (x<=0) → node2.Children[1] = node 1.
        nodes[2].Children = [0, 1];

        level.Nodes = nodes;
        return level;
    }

    [Fact]
    public void PointInSubsector_WalksToCorrectLeaf()
    {
        var level = BuildLevel();

        // x>0, y>0 → root right → node0 front (y>0) → ss3
        Assert.Same(level.Subsectors[3], RendererMath.PointInSubsector(100 << 16, 100 << 16, level));

        // x>0, y<0 → root right → node0 back → ss2
        Assert.Same(level.Subsectors[2], RendererMath.PointInSubsector(100 << 16, -100 << 16, level));

        // x<0, y>0 → root left → node1 front → ss1
        Assert.Same(level.Subsectors[1], RendererMath.PointInSubsector(-100 << 16, 100 << 16, level));

        // x<0, y<0 → root left → node1 back → ss0
        Assert.Same(level.Subsectors[0], RendererMath.PointInSubsector(-100 << 16, -100 << 16, level));
    }

    [Fact]
    public void PointInSubsector_SingleSubsectorMap_ReturnsLeaf0()
    {
        var level = new LevelData
        {
            Subsectors = [new Subsector { NumSegs = 1, FirstSeg = 0 }],
            Nodes = [],
        };

        var ss = RendererMath.PointInSubsector(12345, -6789, level);
        Assert.Same(level.Subsectors[0], ss);
    }

    [Fact]
    public void PointOnSegSide_MatchesPointOnSideSemantics()
    {
        // Seg from (0,0) to (100,0): horizontal, ldy=0, ldx>0.
        // C: y <= ly → (ldx<0 ? 1 : 0) = 0; y > ly → (ldx>0 ? 1 : 0) = 1.
        Assert.Equal(1, RendererMath.PointOnSegSide(50, 10 << 16, 0, 0, 100 << 16, 0));
        Assert.Equal(0, RendererMath.PointOnSegSide(50, -10 << 16, 0, 0, 100 << 16, 0));
        Assert.Equal(0, RendererMath.PointOnSegSide(-10, 0, 0, 0, 100 << 16, 0));
    }

    [Fact]
    public void AddPointToBox_ExpandsBox()
    {
        // Box layout: BOXLEFT/BOXTOP/BOXRIGHT/BOXBOTTOM (r_defs.h:60-65).
        var box = new[] { 100, 200, 300, 400 };

        RendererMath.AddPointToBox(5, 500, box);

        Assert.Equal(5, box[0]);    // left expanded
        Assert.Equal(500, box[1]);  // top expanded
        Assert.Equal(300, box[2]);  // right unchanged
        Assert.Equal(400, box[3]);  // bottom unchanged
    }

    [Fact]
    public void AddPointToBox_InsideBox_NoChange()
    {
        var box = new[] { 0, 100, 100, 0 };

        RendererMath.AddPointToBox(50, 50, box);

        Assert.Equal(new[] { 0, 100, 100, 0 }, box);
    }
}

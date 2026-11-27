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
//      BSP traversal and wall-segment clipping.
//      Migration of Legacy/linuxdoom-1.10/r_bsp.c: R_ClearDrawSegs,
//      R_ClipSolidWallSegment, R_ClipPassWallSegment, R_ClearClipSegs,
//      R_AddLine, R_CheckBBox, R_Subsector, R_RenderBSPNode.
//      See docs/adr/ADR-004 for tracing; DOOM-030c plan for the
//      documented deviations.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using Serilog;
using Serilog.Events;

namespace DoomSharp.Rendering;

/// <summary>cliprange_t (r_bsp.c:80-85).</summary>
public struct ClipRange
{
    public int First;
    public int Last;
}

/// <summary>
/// BSP traversal (r_bsp.c): solid-segment clipping, R_AddLine,
/// R_CheckBBox, R_Subsector, R_RenderBSPNode. Sector references are
/// indices into LevelData (-1 = NULL, the documented deviation).
/// </summary>
public sealed class BspRenderer
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(BspRenderer));

    /// <summary>MAXSEGS (r_bsp.c:88).</summary>
    public const int MaxSegs = 32;

    /// <summary>solidsegs[MAXSEGS] (r_bsp.c:92).</summary>
    public readonly ClipRange[] SolidSegs = new ClipRange[MaxSegs];

    /// <summary>newend (r_bsp.c:91) — index one past the last valid seg.</summary>
    public int NewEnd;

    /// <summary>curline (r_bsp.c:47) — index into LevelData.Segs; -1 = NULL.</summary>
    public int CurLine = -1;

    /// <summary>frontsector / backsector (r_bsp.c:50-51) — sector indices; -1 = NULL.</summary>
    public int FrontSector = -1;
    public int BackSector = -1;

    private readonly ViewSetup _view;
    private readonly SegRenderer _segs;
    private readonly VisplaneManager _planes;
    private readonly SpriteRenderer _sprites;
    private readonly LevelData _level;

    public BspRenderer(ViewSetup view, SegRenderer segs, VisplaneManager planes, SpriteRenderer sprites, LevelData level)
    {
        _view = view;
        _segs = segs;
        _planes = planes;
        _sprites = sprites;
        _level = level;

        Log.Verbose("BspRenderer()");
    }

    // ------------------------------------------------------------------
    // R_ClearDrawSegs / R_ClearClipSegs (r_bsp.c:68-71, 245-252)
    // ------------------------------------------------------------------

    /// <summary>R_ClearDrawSegs (r_bsp.c:68-71).</summary>
    public void ClearDrawSegs()
    {
        Log.Verbose("ClearDrawSegs()");
        _segs.ClearDrawSegs();
    }

    /// <summary>R_ClearClipSegs (r_bsp.c:245-252).</summary>
    public void ClearClipSegs()
    {
        SolidSegs[0].First = -0x7fffffff;
        SolidSegs[0].Last = -1;
        SolidSegs[1].First = _view.ViewWidth;
        SolidSegs[1].Last = 0x7fffffff;
        NewEnd = 2;
    }

    // ------------------------------------------------------------------
    // R_ClipSolidWallSegment (r_bsp.c:103-185)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_ClipSolidWallSegment (r_bsp.c:103-185): handles solid walls
    /// (single-sided linedefs) that entirely block the view.
    /// </summary>
    public void ClipSolidWallSegment(int first, int last)
    {
        // Find the first range that touches the range
        // (adjacent pixels are touching) (r_bsp.c:113-115).
        var start = 0;
        while (SolidSegs[start].Last < first - 1)
        {
            start++;
        }

        if (first < SolidSegs[start].First)
        {
            if (last < SolidSegs[start].First - 1)
            {
                // Post is entirely visible (above start), so insert a
                // new clippost (r_bsp.c:119-135).
                _segs.StoreWallRange(first, last);
                var next = NewEnd;
                NewEnd++;

                while (next != start)
                {
                    SolidSegs[next] = SolidSegs[next - 1];
                    next--;
                }

                SolidSegs[next].First = first;
                SolidSegs[next].Last = last;
                return;
            }

            // There is a fragment above *start (r_bsp.c:137-140).
            _segs.StoreWallRange(first, SolidSegs[start].First - 1);
            SolidSegs[start].First = first;
        }

        // Bottom contained in start? (r_bsp.c:143-145)
        if (last <= SolidSegs[start].Last)
        {
            return;
        }

        var next2 = start;
        while (last >= SolidSegs[next2 + 1].First - 1)
        {
            // There is a fragment between two posts (r_bsp.c:150-151).
            _segs.StoreWallRange(SolidSegs[next2].Last + 1, SolidSegs[next2 + 1].First - 1);
            next2++;

            if (last <= SolidSegs[next2].Last)
            {
                // Bottom is contained in next. Adjust the clip size.
                SolidSegs[start].Last = SolidSegs[next2].Last;
                Crunch(start, next2);
                return;
            }
        }

        // There is a fragment after *next (r_bsp.c:163-166).
        _segs.StoreWallRange(SolidSegs[next2].Last + 1, last);
        SolidSegs[start].Last = last;

        // Remove start+1 to next from the clip list (r_bsp.c:168-184).
        Crunch(start, next2);
    }

    /// <summary>
    /// The "crunch" tail of R_ClipSolidWallSegment (r_bsp.c:170-184):
    /// remove start+1..next from the clip list because start now
    /// covers their area.
    /// </summary>
    private void Crunch(int start, int next)
    {
        if (next == start)
        {
            // Post just extended past the bottom of one post.
            return;
        }

        while (next++ != NewEnd)
        {
            // Remove a post.
            SolidSegs[++start] = SolidSegs[next];
        }

        NewEnd = start + 1;
    }

    // ------------------------------------------------------------------
    // R_ClipPassWallSegment (r_bsp.c:196-238)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_ClipPassWallSegment (r_bsp.c:196-238): clips the given range
    /// of columns but does not include it in the clip list (windows).
    /// </summary>
    public void ClipPassWallSegment(int first, int last)
    {
        // Find the first range that touches the range
        // (adjacent pixels are touching) (r_bsp.c:204-207).
        var start = 0;
        while (SolidSegs[start].Last < first - 1)
        {
            start++;
        }

        if (first < SolidSegs[start].First)
        {
            if (last < SolidSegs[start].First - 1)
            {
                // Post is entirely visible (above start) (r_bsp.c:211-216).
                _segs.StoreWallRange(first, last);
                return;
            }

            // There is a fragment above *start (r_bsp.c:217-219).
            _segs.StoreWallRange(first, SolidSegs[start].First - 1);
        }

        // Bottom contained in start? (r_bsp.c:222-224)
        if (last <= SolidSegs[start].Last)
        {
            return;
        }

        while (last >= SolidSegs[start + 1].First - 1)
        {
            // There is a fragment between two posts (r_bsp.c:227-229).
            _segs.StoreWallRange(SolidSegs[start].Last + 1, SolidSegs[start + 1].First - 1);
            start++;

            if (last <= SolidSegs[start].Last)
            {
                return;
            }
        }

        // There is a fragment after *next (r_bsp.c:236-237).
        _segs.StoreWallRange(SolidSegs[start].Last + 1, last);
    }

    // ------------------------------------------------------------------
    // R_AddLine (r_bsp.c:259-356)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_AddLine (r_bsp.c:259-356): clips the given segment and adds
    /// any visible pieces to the line list. Preserves the exact
    /// backface cull, view-edge clip, viewangletox mapping and the
    /// closed-door / window / trigger-line classification.
    /// </summary>
    public void AddLine(int segIndex)
    {
        CurLine = segIndex;
        _segs.CurLine = segIndex;   // C shares the single curline global
        var line = _level.Segs[segIndex];

        // OPTIMIZE: quickly reject orthogonal back sides (r_bsp.c:271-272).
        var angle1 = RendererMath.PointToAngle(_level.Vertexes[line.V1].X, _level.Vertexes[line.V1].Y);
        var angle2 = RendererMath.PointToAngle(_level.Vertexes[line.V2].X, _level.Vertexes[line.V2].Y);

        // Clip to view edges (r_bsp.c:276).
        var span = angle1 - angle2;

        // Back side? I.e. backface culling? (r_bsp.c:279-280)
        if (span >= AngleTables.Ang180)
        {
            return;
        }

        // Global angle needed by segcalc (r_bsp.c:283).
        _segs.RwAngle1 = angle1;
        angle1 -= RendererMath.ViewAngle;
        angle2 -= RendererMath.ViewAngle;

        var tspan = angle1 + _view.ClipAngle;
        if (tspan > 2 * _view.ClipAngle)
        {
            tspan -= 2 * _view.ClipAngle;

            // Totally off the left edge? (r_bsp.c:292-294)
            if (tspan >= span)
            {
                return;
            }

            angle1 = _view.ClipAngle;
        }

        tspan = _view.ClipAngle - angle2;
        if (tspan > 2 * _view.ClipAngle)
        {
            tspan -= 2 * _view.ClipAngle;

            // Totally off the left edge? (r_bsp.c:303-305)
            if (tspan >= span)
            {
                return;
            }

            angle2 = unchecked(0u - _view.ClipAngle);   // -clipangle (r_bsp.c:306)
        }

        // The seg is in the view range, but not necessarily visible
        // (r_bsp.c:309-314).
        angle1 = (angle1 + AngleTables.Ang90) >> AngleTables.AngleToFineShift;
        angle2 = (angle2 + AngleTables.Ang90) >> AngleTables.AngleToFineShift;
        var x1 = _view.ViewAngleToX[angle1];
        var x2 = _view.ViewAngleToX[angle2];

        // Does not cross a pixel? (r_bsp.c:316-318)
        if (x1 == x2)
        {
            return;
        }

        BackSector = line.BackSector;

        // Single sided line? (r_bsp.c:322-324)
        if (BackSector == -1)
        {
            goto clipsolid;
        }

        var front = _level.Sectors[FrontSector];
        var back = _level.Sectors[BackSector];

        // Closed door (r_bsp.c:326-329).
        if (back.CeilingHeight <= front.FloorHeight
            || back.FloorHeight >= front.CeilingHeight)
        {
            goto clipsolid;
        }

        // Window (r_bsp.c:331-334).
        if (back.CeilingHeight != front.CeilingHeight
            || back.FloorHeight != front.FloorHeight)
        {
            goto clippass;
        }

        // Reject empty lines used for triggers and special events
        // (r_bsp.c:336-347).
        var ldef = _level.Lines[line.LineDef];
        var segSide = _level.Sides[ldef.Sidenum[line.Side]];
        if (back.CeilingFlat == front.CeilingFlat
            && back.FloorFlat == front.FloorFlat
            && back.LightLevel == front.LightLevel
            && ldef.Sidenum[0] >= 0
            && segSide.MidTextureIndex == 0)
        {
            return;
        }

    clippass:
        ClipPassWallSegment(x1, x2 - 1);
        return;

    clipsolid:
        ClipSolidWallSegment(x1, x2 - 1);
    }

    // ------------------------------------------------------------------
    // R_CheckBBox (r_bsp.c:360-487)
    // ------------------------------------------------------------------

    /// <summary>checkcoord[12][4] (r_bsp.c:365-378) — verbatim.</summary>
    private static readonly int[][] CheckCoord =
    [
        [3, 0, 2, 1],
        [3, 0, 2, 0],
        [3, 1, 2, 0],
        [0, 0, 0, 0],
        [2, 0, 2, 1],
        [0, 0, 0, 0],
        [3, 1, 3, 0],
        [0, 0, 0, 0],
        [2, 0, 3, 1],
        [2, 1, 3, 1],
        [2, 1, 3, 0],
    ];

    /// <summary>
    /// R_CheckBBox (r_bsp.c:381-487): checks a BSP node/subtree
    /// bounding box; returns true if some part of the bbox might be
    /// visible.
    /// </summary>
    public bool CheckBBox(int[] bspCoord)
    {
        // Find the corners of the box that define the edges from the
        // current viewpoint (r_bsp.c:402-416).
        int boxx;
        if (RendererMath.ViewX <= bspCoord[BBox.Left])
        {
            boxx = 0;
        }
        else if (RendererMath.ViewX < bspCoord[BBox.Right])
        {
            boxx = 1;
        }
        else
        {
            boxx = 2;
        }

        int boxy;
        if (RendererMath.ViewY >= bspCoord[BBox.Top])
        {
            boxy = 0;
        }
        else if (RendererMath.ViewY > bspCoord[BBox.Bottom])
        {
            boxy = 1;
        }
        else
        {
            boxy = 2;
        }

        var boxpos = (boxy << 2) + boxx;
        if (boxpos == 5)
        {
            return true;
        }

        var x1 = bspCoord[CheckCoord[boxpos][0]];
        var y1 = bspCoord[CheckCoord[boxpos][1]];
        var x2 = bspCoord[CheckCoord[boxpos][2]];
        var y2 = bspCoord[CheckCoord[boxpos][3]];

        // check clip list for an open space (r_bsp.c:427-429)
        var angle1 = RendererMath.PointToAngle(x1, y1) - RendererMath.ViewAngle;
        var angle2 = RendererMath.PointToAngle(x2, y2) - RendererMath.ViewAngle;

        var span = angle1 - angle2;

        // Sitting on a line? (r_bsp.c:433-435)
        if (span >= AngleTables.Ang180)
        {
            return true;
        }

        var tspan = angle1 + _view.ClipAngle;
        if (tspan > 2 * _view.ClipAngle)
        {
            tspan -= 2 * _view.ClipAngle;

            // Totally off the left edge? (r_bsp.c:443-445)
            if (tspan >= span)
            {
                return false;
            }

            angle1 = _view.ClipAngle;
        }

        tspan = _view.ClipAngle - angle2;
        if (tspan > 2 * _view.ClipAngle)
        {
            tspan -= 2 * _view.ClipAngle;

            // Totally off the left edge? (r_bsp.c:453-456)
            if (tspan >= span)
            {
                return false;
            }

            angle2 = unchecked(0u - _view.ClipAngle);
        }

        // Find the first clippost that touches the source post
        // (r_bsp.c:462-468).
        angle1 = (angle1 + AngleTables.Ang90) >> AngleTables.AngleToFineShift;
        angle2 = (angle2 + AngleTables.Ang90) >> AngleTables.AngleToFineShift;
        var sx1 = _view.ViewAngleToX[angle1];
        var sx2 = _view.ViewAngleToX[angle2];

        // Does not cross a pixel (r_bsp.c:470-472).
        if (sx1 == sx2)
        {
            return false;
        }

        sx2--;

        var start = 0;
        while (SolidSegs[start].Last < sx2)
        {
            start++;
        }

        if (sx1 >= SolidSegs[start].First && sx2 <= SolidSegs[start].Last)
        {
            // The clippost contains the new span (r_bsp.c:479-484).
            return false;
        }

        return true;
    }

    // ------------------------------------------------------------------
    // R_Subsector (r_bsp.c:497-542)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_Subsector (r_bsp.c:497-542): determine floor/ceiling planes,
    /// add sprites of things in the sector, draw one or more line
    /// segments.
    /// </summary>
    public void Subsector(int num)
    {
        if (num >= _level.Subsectors.Length)
        {
            Log.Verbose("Subsector fatal: ss {Num} with numss = {Count}", num, _level.Subsectors.Length);
            throw new DoomFatalException($"R_Subsector: ss {num} with numss = {_level.Subsectors.Length}");
        }

        RendererMath.SsCount++;
        var sub = _level.Subsectors[num];
        FrontSector = _level.Segs[sub.FirstSeg].FrontSector;
        var count = sub.NumSegs;

        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("Subsector({Num}) segs={Count} sector={Sector}", num, count, FrontSector);
        }

        var front = _level.Sectors[FrontSector];

        if (front.FloorHeight < RendererMath.ViewZ)
        {
            _planes.FloorPlane = _planes.FindPlane(
                front.FloorHeight, front.FloorFlat, front.LightLevel, SkyRenderer.SkyFlatNum);
        }
        else
        {
            _planes.FloorPlane = -1;
        }

        if (front.CeilingHeight > RendererMath.ViewZ
            || front.CeilingFlat == SkyRenderer.SkyFlatNum)
        {
            _planes.CeilingPlane = _planes.FindPlane(
                front.CeilingHeight, front.CeilingFlat, front.LightLevel, SkyRenderer.SkyFlatNum);
        }
        else
        {
            _planes.CeilingPlane = -1;
        }

        _sprites.AddSprites(FrontSector, SpriteDefs.Sprites);

        var segIndex = sub.FirstSeg;
        while (count-- > 0)
        {
            AddLine(segIndex);
            segIndex++;
        }
    }

    // ------------------------------------------------------------------
    // R_RenderBSPNode (r_bsp.c:552-578)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_RenderBSPNode (r_bsp.c:552-578): renders all subsectors below
    /// a given node, traversing the subtree recursively. Call with the
    /// BSP root (numnodes-1).
    /// </summary>
    public void RenderBSPNode(int bspnum)
    {
        // Found a subsector? (r_bsp.c:558-565)
        if ((bspnum & NodeFlags.SubSector) != 0)
        {
            Subsector(bspnum == -1 ? 0 : bspnum & ~NodeFlags.SubSector);
            return;
        }

        var bsp = _level.Nodes[bspnum];

        // Decide which side the view point is on (r_bsp.c:570).
        var side = RendererMath.PointOnSide(RendererMath.ViewX, RendererMath.ViewY, bsp);

        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("RenderBSPNode({BspNum}) side={Side}", bspnum, side);
        }

        // Recursively divide front space (r_bsp.c:573).
        RenderBSPNode(bsp.Children[side]);

        // Possibly divide back space (r_bsp.c:576-577).
        if (CheckBBox(bsp.BBox[side ^ 1]))
        {
            RenderBSPNode(bsp.Children[side ^ 1]);
        }
    }
}

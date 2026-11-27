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
//      Wall segment rendering.
//      Migration of Legacy/linuxdoom-1.10/r_segs.c: R_PointOnSegSide
//      (stays in RendererMath), R_RenderMaskedSegRange, R_StoreWallRange,
//      R_RenderSegLoop, rw_* globals, drawsegs.
//      See docs/adr/ADR-004 for tracing; DOOM-030c plan for the
//      documented deviations.
//
//-----------------------------------------------------------------------------

using DoomSharp.Core;
using DoomSharp.Data;
using Serilog;
using Serilog.Events;

namespace DoomSharp.Rendering;

/// <summary>drawseg_t (r_defs.h:322-347).</summary>
public sealed class DrawSeg
{
    /// <summary>curline — index into LevelData.Segs; -1 = NULL.</summary>
    public int CurLine = -1;
    public int X1;
    public int X2;
    public int Scale1;          // fixed
    public int Scale2;          // fixed
    public int ScaleStep;       // fixed

    /// <summary>0=none, 1=bottom, 2=top, 3=both (r_defs.h:332).</summary>
    public int Silhouette;

    /// <summary>do not clip sprites above this (r_defs.h:336).</summary>
    public int BsilHeight;

    /// <summary>do not clip sprites below this (r_defs.h:339).</summary>
    public int TsilHeight;

    /// <summary>Offsets into VisplaneManager.Openings; -1 = NULL.</summary>
    public int SprTopClip = -1;
    public int SprBottomClip = -1;
    public int MaskedTextureCol = -1;
}

/// <summary>
/// Wall segment rendering (r_segs.c): R_StoreWallRange computes the
/// per-column wall geometry; R_RenderSegLoop rasterizes it.
/// </summary>
public sealed class SegRenderer
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(SegRenderer));

    /// <summary>MAXDRAWSEGS (r_defs.h:55).</summary>
    public const int MaxDrawSegs = 256;

    /// <summary>drawsegs[MAXDRAWSEGS] (r_bsp.c:53).</summary>
    public readonly DrawSeg[] DrawSegs = new DrawSeg[MaxDrawSegs];

    /// <summary>ds_p (r_bsp.c:54) — one past the last stored drawseg.</summary>
    public int DsP;

    /// <summary>rw_angle1 (r_segs.c:57) — angle to line origin.</summary>
    public uint RwAngle1;

    /// <summary>curline (r_bsp.c:47) — index into LevelData.Segs; -1 = NULL.</summary>
    public int CurLine = -1;

    /// <summary>lastopening (r_plane.c:61) — allocation cursor into Openings.</summary>
    public int LastOpening
    {
        get => _planes.LastOpening;
        set => _planes.LastOpening = value;
    }

    // rw_* globals of r_segs.c (59-100) — shared with RenderSegLoop.
    public bool Mark;
    public int RwX;
    public int RwStopX;
    public uint RwCenterAngle;
    public int RwOffset;
    public int RwScaleStep;
    public int RwMidTextureMid;
    public int RwTopTextureMid;
    public int RwBottomTextureMid;

    /// <summary>rw_normalangle / rw_distance / rw_scale (r_segs.c:62-64).</summary>
    public int RwNormalAngle;
    public int RwDistance;
    public int RwScale;

    // world* / pix* / frac/step locals (r_segs.c:71-96)
    public int WorldTop;
    public int WorldBottom;
    public int WorldHigh;
    public int WorldLow;
    public int PixHigh;
    public int PixLow;
    public int PixHighStep;
    public int PixLowStep;
    public int TopFrac;
    public int TopStep;
    public int BottomFrac;
    public int BottomStep;

    /// <summary>walllights (r_segs.c:102).</summary>
    public byte[][] WallLights = [];

    /// <summary>maskedtexturecol — current drawseg slice (r_segs.c:104).</summary>
    public int MaskedTextureCol = -1;

    // segtextured / markfloor / markceiling / texture tiers (r_segs.c:59-66 locals)
    public bool SegTextured;
    public bool MarkFloor;
    public bool MarkCeiling;
    public int MidTexture;
    public int TopTexture;
    public int BottomTexture;
    public bool MaskedTexture;

    /// <summary>spryscale / sprtopscreen (r_things.c:341-342, shared).</summary>
    public int SprScale;
    public int SprTopScreen;

    /// <summary>mfloorclip / mceilingclip — offsets into Openings; -1 = NULL.</summary>
    public int MFloorClip = -1;
    public int MCeilingClip = -1;

    /// <summary>dc_texturemid / dc_colormap staging for masked columns.</summary>
    public int DcTextureMid;

    /// <summary>
    /// Sentinel offsets for the screenheightarray / negonearray clip
    /// slices (the C stores the array pointers directly). Values
    /// distinguish "points at screenheightarray" from "points into
    /// openings" without extra fields.
    /// </summary>
    public const int ScreenHeightArraySentinel = -2;
    public const int NegOneArraySentinel = -3;

    /// <summary>
    /// Reads one clip value from a drawseg clip slice. Sentinel
    /// offsets resolve to view.ScreenHeightArray / view.NegOneArray.
    /// </summary>
    public short ReadClip(int clipOffset, int x)
    {
        return clipOffset switch
        {
            ScreenHeightArraySentinel => _view.ScreenHeightArray[x],
            NegOneArraySentinel => _view.NegOneArray[x],
            _ => _planes.Openings[clipOffset + x],
        };
    }

    /// <summary>
    /// Copies a clip array range into the openings pool
    /// (the memcpy of r_segs.c:496-509).
    /// </summary>
    private void CopyClipToOpening(short[] clip, int start, int count)
    {
        Array.Copy(clip, start, _planes.Openings, LastOpening, count);
    }

    /// <summary>
    /// DOOM-030c: when sprites draw, mfloorclip/mceilingclip point at
    /// the caller's scratch buffers instead of openings slices.
    /// </summary>
    private short[]? _floorClipArray;
    private short[]? _ceilingClipArray;

    public void SetClipArrays(short[] floorClip, short[] ceilingClip)
    {
        _floorClipArray = floorClip;
        _ceilingClipArray = ceilingClip;
    }

    public void ClearClipArrays()
    {
        _floorClipArray = null;
        _ceilingClipArray = null;
    }

    /// <summary>mfloorclip[dc_x] — openings slice or scratch array.</summary>
    public short ReadFloorClip(int x)
    {
        return _floorClipArray != null ? _floorClipArray[x] : _planes.Openings[MFloorClip + x];
    }

    /// <summary>mceilingclip[dc_x] — openings slice or scratch array.</summary>
    public short ReadCeilingClip(int x)
    {
        return _ceilingClipArray != null ? _ceilingClipArray[x] : _planes.Openings[MCeilingClip + x];
    }

    private readonly ViewSetup _view;
    private readonly VisplaneManager _planes;
    private readonly LevelData _level;
    private readonly TextureManager _textures;
    private readonly WadDirectory _wad;

    public SegRenderer(ViewSetup view, VisplaneManager planes, LevelData level, TextureManager textures, WadDirectory wad)
    {
        _view = view;
        _planes = planes;
        _level = level;
        _textures = textures;
        _wad = wad;

        for (var i = 0; i < MaxDrawSegs; i++)
        {
            DrawSegs[i] = new DrawSeg();
        }

        Log.Verbose("SegRenderer()");
    }

    /// <summary>R_ClearDrawSegs (r_bsp.c:68-71): ds_p = drawsegs.</summary>
    public void ClearDrawSegs()
    {
        DsP = 0;
    }

    // ------------------------------------------------------------------
    // R_RenderMaskedSegRange (r_segs.c:110-197)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_RenderMaskedSegRange (r_segs.c:110-197): re-render a masked
    /// mid-texture range of an already-stored drawseg (sprites clip
    /// against it, then it is drawn back to front).
    /// </summary>
    public void RenderMaskedSegRange(DrawSeg ds, int x1, int x2)
    {
        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("RenderMaskedSegRange(ds {X1}..{X2} range {R1}..{R2})", ds.X1, ds.X2, x1, x2);
        }

        CurLine = ds.CurLine;
        var line = _level.Segs[CurLine];
        var front = _level.Sectors[line.FrontSector];
        var back = line.BackSector >= 0 ? _level.Sectors[line.BackSector] : null;
        var ldef = _level.Lines[line.LineDef];
        var side = _level.Sides[ldef.Sidenum[line.Side]];
        var texnum = _textures.TextureTranslation[side.MidTextureIndex];

        // calculate lighting (r_segs.c:126-143)
        var lightnum = (front.LightLevel >> ViewSetup.LightSegShift) + RendererMath.ExtraLight;

        if (_level.Vertexes[line.V1].Y == _level.Vertexes[line.V2].Y)
        {
            lightnum--;
        }
        else if (_level.Vertexes[line.V1].X == _level.Vertexes[line.V2].X)
        {
            lightnum++;
        }

        if (lightnum < 0)
        {
            WallLights = _view.ScaleLight[0];
        }
        else if (lightnum >= ViewSetup.LightLevels)
        {
            WallLights = _view.ScaleLight[ViewSetup.LightLevels - 1];
        }
        else
        {
            WallLights = _view.ScaleLight[lightnum];
        }

        MaskedTextureCol = ds.MaskedTextureCol;

        RwScaleStep = ds.ScaleStep;
        SprScale = ds.Scale1 + (x1 - ds.X1) * RwScaleStep;
        MFloorClip = ds.SprBottomClip;
        MCeilingClip = ds.SprTopClip;

        // find positioning (r_segs.c:150-163)
        if ((ldef.Flags & LineDefFlags.DontPegBottom) != 0)
        {
            DcTextureMid = front.FloorHeight > back!.FloorHeight
                ? front.FloorHeight : back.FloorHeight;
            DcTextureMid = DcTextureMid + _textures.TextureHeight[texnum] - RendererMath.ViewZ;
        }
        else
        {
            DcTextureMid = front.CeilingHeight < back!.CeilingHeight
                ? front.CeilingHeight : back.CeilingHeight;
            DcTextureMid = DcTextureMid - RendererMath.ViewZ;
        }

        DcTextureMid += side.RowOffset;

        // draw the columns (r_segs.c:165-193)
        for (var dcX = x1; dcX <= x2; dcX++)
        {
            if (_planes.Openings[MaskedTextureCol + dcX] != short.MaxValue)
            {
                if (RendererMath.FixedColormap == null)
                {
                    var index = (uint)SprScale >> ViewSetup.LightScaleShift;
                    if (index >= ViewSetup.MaxLightScale)
                    {
                        index = ViewSetup.MaxLightScale - 1;
                    }

                    ColumnDrawState.Colormap = WallLights[index];
                }
                else
                {
                    ColumnDrawState.Colormap = RendererMath.FixedColormap;
                }

                SprTopScreen = _view.CenterYFrac - FixedPoint.FixedMul(DcTextureMid, SprScale);
                ColumnDrawState.IScale = unchecked((int)(0xffffffffu / (uint)SprScale));

                // draw the texture (r_segs.c:184-186)
                var textureColumn = _planes.Openings[MaskedTextureCol + dcX];
                var column = _textures.GetColumn(_wad, texnum, textureColumn, out var sourceOfs);

                DrawMaskedColumn(column, sourceOfs, texnum);

                _planes.Openings[MaskedTextureCol + dcX] = short.MaxValue;
            }

            SprScale += RwScaleStep;
        }
    }

    /// <summary>
    /// R_DrawMaskedColumn (r_things.c:346-391): walk the posts of one
    /// masked column, clipping against mfloorclip/mceilingclip.
    /// </summary>
    public void DrawMaskedColumn(byte[] columnData, int columnOfs, int texNum)
    {
        var baseTextureMid = DcTextureMid;
        var topDelta = columnData[columnOfs];
        var postOfs = columnOfs;

        while (topDelta != 0xff)
        {
            var length = columnData[postOfs + 1];

            // calculate unclipped screen coordinates for post
            // (r_things.c:355-358)
            var topScreen = SprTopScreen + SprScale * topDelta;
            var bottomScreen = topScreen + SprScale * length;

            ColumnDrawState.Yl = (topScreen + FixedPoint.FracUnit - 1) >> FixedPoint.FracBits;
            ColumnDrawState.Yh = (bottomScreen - 1) >> FixedPoint.FracBits;

            if (ColumnDrawState.Yh >= ReadFloorClip(ColumnDrawState.X))
            {
                ColumnDrawState.Yh = ReadFloorClip(ColumnDrawState.X) - 1;
            }

            if (ColumnDrawState.Yl <= ReadCeilingClip(ColumnDrawState.X))
            {
                ColumnDrawState.Yl = ReadCeilingClip(ColumnDrawState.X) + 1;
            }

            if (ColumnDrawState.Yl <= ColumnDrawState.Yh)
            {
                ColumnDrawState.Source = columnData;
                ColumnDrawState.SourceOfs = postOfs + 3;
                DcTextureMid = baseTextureMid - (topDelta << FixedPoint.FracBits);
                ColumnDrawState.TextureMid = DcTextureMid;

                // Drawn by either R_DrawColumn or R_DrawFuzzColumn
                // (r_things.c:377-379).
                if (ColumnDrawState.Colormap == null)
                {
                    ColumnDrawer.DrawFuzzColumn(_view);
                }
                else
                {
                    ColumnDrawer.DrawColumn(_view);
                }
            }

            postOfs += length + 4;
            topDelta = columnData[postOfs];
        }

        DcTextureMid = baseTextureMid;
    }

    // ------------------------------------------------------------------
    // R_StoreWallRange (r_segs.c:200-746)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_StoreWallRange (r_segs.c:200-746): a wall segment will be
    /// drawn between start and stop pixels (inclusive). Computes
    /// rw_distance, scales, texture boundaries, silhouette info and
    /// stepping values, then runs R_RenderSegLoop.
    /// </summary>
    public void StoreWallRange(int start, int stop)
    {
        // don't overflow and crash (r_segs.c:210-212)
        if (DsP == MaxDrawSegs)
        {
            return;
        }

        if (Log.IsEnabled(LogEventLevel.Verbose))
        {
            Log.Verbose("StoreWallRange({Start}..{Stop}) seg={Seg}", start, stop, CurLine);
        }

        var line = _level.Segs[CurLine];
        var ldef = _level.Lines[line.LineDef];
        var side = _level.Sides[ldef.Sidenum[line.Side]];
        var front = _level.Sectors[line.FrontSector];
        var back = line.BackSector >= 0 ? _level.Sectors[line.BackSector] : null;

        // mark the segment as visible for auto map (r_segs.c:219)
        ldef.Flags |= LineDefFlags.Mapped;

        // calculate rw_distance for scale calculation (r_segs.c:222-234)
        RwNormalAngle = unchecked(line.Angle + (int)AngleTables.Ang90);
        var offsetAngle = Math.Abs(RwNormalAngle - (int)RwAngle1);

        if (offsetAngle > (int)AngleTables.Ang90)
        {
            offsetAngle = (int)AngleTables.Ang90;
        }

        var distAngle = AngleTables.Ang90 - offsetAngle;
        var hyp = RendererMath.PointToDist(_level.Vertexes[line.V1].X, _level.Vertexes[line.V1].Y);
        var sineval = AngleTables.FineSine[distAngle >> AngleTables.AngleToFineShift];
        RwDistance = FixedPoint.FixedMul(hyp, sineval);
        // R_ScaleFromGlobalAngle reads the module-level rw_normalangle /
        // rw_distance; mirror them into the shared statics (r_main.c).
        RendererMath.RwNormalAngle = RwNormalAngle;
        RendererMath.RwDistance = RwDistance;

        var ds = DrawSegs[DsP];
        ds.X1 = RwX = start;
        ds.X2 = stop;
        ds.CurLine = CurLine;
        RwStopX = stop + 1;

        // calculate scale at both ends and step (r_segs.c:241-259)
        ds.Scale1 = RwScale = RendererMath.ScaleFromGlobalAngle(
            RendererMath.ViewAngle + _view.XToViewAngle[start]);

        if (stop > start)
        {
            ds.Scale2 = RendererMath.ScaleFromGlobalAngle(
                RendererMath.ViewAngle + _view.XToViewAngle[stop]);
            ds.ScaleStep = RwScaleStep = (ds.Scale2 - RwScale) / (stop - start);
        }
        else
        {
            ds.Scale2 = ds.Scale1;
        }

        // calculate texture boundaries and decide if floor / ceiling
        // marks are needed (r_segs.c:262-265)
        WorldTop = front.CeilingHeight - RendererMath.ViewZ;
        WorldBottom = front.FloorHeight - RendererMath.ViewZ;

        MidTexture = TopTexture = BottomTexture = 0;
        MaskedTexture = false;
        ds.MaskedTextureCol = -1;

        if (back == null)
        {
            // single sided line (r_segs.c:269-296)
            MidTexture = _textures.TextureTranslation[side.MidTextureIndex];
            MarkFloor = MarkCeiling = true;

            if ((ldef.Flags & LineDefFlags.DontPegBottom) != 0)
            {
                var vtop = front.FloorHeight + _textures.TextureHeight[side.MidTextureIndex];
                RwMidTextureMid = vtop - RendererMath.ViewZ;   // bottom of texture at bottom
            }
            else
            {
                RwMidTextureMid = WorldTop;                    // top of texture at top
            }

            RwMidTextureMid += side.RowOffset;

            ds.Silhouette = 3;                                 // SIL_BOTH
            ds.SprTopClip = ScreenHeightArraySentinel;
            ds.SprBottomClip = NegOneArraySentinel;
            ds.BsilHeight = int.MaxValue;
            ds.TsilHeight = int.MinValue;
        }
        else
        {
            // two sided line (r_segs.c:298-436)
            ds.SprTopClip = ds.SprBottomClip = -1;
            ds.Silhouette = 0;

            if (front.FloorHeight > back.FloorHeight)
            {
                ds.Silhouette = 1;                             // SIL_BOTTOM
                ds.BsilHeight = front.FloorHeight;
            }
            else if (back.FloorHeight > RendererMath.ViewZ)
            {
                ds.Silhouette = 1;
                ds.BsilHeight = int.MaxValue;
            }

            if (front.CeilingHeight < back.CeilingHeight)
            {
                ds.Silhouette |= 2;                            // SIL_TOP
                ds.TsilHeight = front.CeilingHeight;
            }
            else if (back.CeilingHeight < RendererMath.ViewZ)
            {
                ds.Silhouette |= 2;
                ds.TsilHeight = int.MinValue;
            }

            if (back.CeilingHeight <= front.FloorHeight)
            {
                ds.SprBottomClip = NegOneArraySentinel;
                ds.BsilHeight = int.MaxValue;
                ds.Silhouette |= 1;
            }

            if (back.FloorHeight >= front.CeilingHeight)
            {
                ds.SprTopClip = ScreenHeightArraySentinel;
                ds.TsilHeight = int.MinValue;
                ds.Silhouette |= 2;
            }

            WorldHigh = back.CeilingHeight - RendererMath.ViewZ;
            WorldLow = back.FloorHeight - RendererMath.ViewZ;

            // hack to allow height changes in outdoor areas
            // (r_segs.c:325-330) — the exact two-sided sky-hack.
            if (front.CeilingFlat == SkyRenderer.SkyFlatNum
                && back.CeilingFlat == SkyRenderer.SkyFlatNum)
            {
                WorldTop = WorldHigh;
            }

            if (WorldLow != WorldBottom
                || back.FloorFlat != front.FloorFlat
                || back.LightLevel != front.LightLevel)
            {
                MarkFloor = true;
            }
            else
            {
                MarkFloor = false;                             // same plane on both sides
            }

            if (WorldHigh != WorldTop
                || back.CeilingFlat != front.CeilingFlat
                || back.LightLevel != front.LightLevel)
            {
                MarkCeiling = true;
            }
            else
            {
                MarkCeiling = false;
            }

            if (back.CeilingHeight <= front.FloorHeight
                || back.FloorHeight >= front.CeilingHeight)
            {
                MarkCeiling = MarkFloor = true;                // closed door
            }

            if (WorldHigh < WorldTop)
            {
                // top texture (r_segs.c:352-368)
                TopTexture = _textures.TextureTranslation[side.TopTextureIndex];
                if ((ldef.Flags & LineDefFlags.DontPegTop) != 0)
                {
                    RwTopTextureMid = WorldTop;                // top of texture at top
                }
                else
                {
                    var vtop = back.CeilingHeight + _textures.TextureHeight[side.TopTextureIndex];
                    RwTopTextureMid = vtop - RendererMath.ViewZ;   // bottom of texture
                }
            }

            if (WorldLow > WorldBottom)
            {
                // bottom texture (r_segs.c:370-381)
                BottomTexture = _textures.TextureTranslation[side.BottomTextureIndex];
                if ((ldef.Flags & LineDefFlags.DontPegBottom) != 0)
                {
                    RwBottomTextureMid = WorldTop;             // bottom of texture at bottom
                }
                else
                {
                    RwBottomTextureMid = WorldLow;             // top of texture at top
                }
            }

            RwTopTextureMid += side.RowOffset;
            RwBottomTextureMid += side.RowOffset;

            // allocate space for masked texture tables (r_segs.c:384-391)
            if (side.MidTextureIndex != 0)
            {
                MaskedTexture = true;
                ds.MaskedTextureCol = MaskedTextureCol = LastOpening - RwX;
                LastOpening += RwStopX - RwX;
            }
        }

        // calculate rw_offset (only needed for textured lines)
        // (r_segs.c:394-436)
        SegTextured = MidTexture != 0 || TopTexture != 0 || BottomTexture != 0 || MaskedTexture;

        if (SegTextured)
        {
            offsetAngle = RwNormalAngle - (int)RwAngle1;

            if (offsetAngle > unchecked((int)AngleTables.Ang180))
            {
                offsetAngle = -offsetAngle;
            }

            if (offsetAngle > (int)AngleTables.Ang90)
            {
                offsetAngle = (int)AngleTables.Ang90;
            }

            sineval = AngleTables.FineSine[offsetAngle >> AngleTables.AngleToFineShift];
            RwOffset = FixedPoint.FixedMul(hyp, sineval);

            if (RwNormalAngle - (int)RwAngle1 < unchecked((int)AngleTables.Ang180))
            {
                RwOffset = -RwOffset;
            }

            RwOffset += side.TextureOffset + line.Offset;
            RwCenterAngle = (uint)(AngleTables.Ang90 + RendererMath.ViewAngle - RwNormalAngle);

            // calculate light table (r_segs.c:419-435)
            if (RendererMath.FixedColormap == null)
            {
                var lightnum = (front.LightLevel >> ViewSetup.LightSegShift) + RendererMath.ExtraLight;

                if (_level.Vertexes[line.V1].Y == _level.Vertexes[line.V2].Y)
                {
                    lightnum--;
                }
                else if (_level.Vertexes[line.V1].X == _level.Vertexes[line.V2].X)
                {
                    lightnum++;
                }

                if (lightnum < 0)
                {
                    WallLights = _view.ScaleLight[0];
                }
                else if (lightnum >= ViewSetup.LightLevels)
                {
                    WallLights = _view.ScaleLight[ViewSetup.LightLevels - 1];
                }
                else
                {
                    WallLights = _view.ScaleLight[lightnum];
                }
            }
        }

        // if a floor / ceiling plane is on the wrong side of the view
        // plane, it is definitely invisible (r_segs.c:440-452)
        if (front.FloorHeight >= RendererMath.ViewZ)
        {
            MarkFloor = false;
        }

        if (front.CeilingHeight <= RendererMath.ViewZ
            && front.CeilingFlat != SkyRenderer.SkyFlatNum)
        {
            MarkCeiling = false;
        }

        // calculate incremental stepping values for texture edges
        // (r_segs.c:455-486)
        WorldTop >>= 4;
        WorldBottom >>= 4;

        TopStep = -FixedPoint.FixedMul(RwScaleStep, WorldTop);
        TopFrac = (_view.CenterYFrac >> 4) - FixedPoint.FixedMul(WorldTop, RwScale);

        BottomStep = -FixedPoint.FixedMul(RwScaleStep, WorldBottom);
        BottomFrac = (_view.CenterYFrac >> 4) - FixedPoint.FixedMul(WorldBottom, RwScale);

        if (back != null)
        {
            WorldHigh >>= 4;
            WorldLow >>= 4;

            if (WorldHigh < WorldTop)
            {
                PixHigh = (_view.CenterYFrac >> 4) - FixedPoint.FixedMul(WorldHigh, RwScale);
                PixHighStep = -FixedPoint.FixedMul(RwScaleStep, WorldHigh);
            }

            if (WorldLow > WorldBottom)
            {
                PixLow = (_view.CenterYFrac >> 4) - FixedPoint.FixedMul(WorldLow, RwScale);
                PixLowStep = -FixedPoint.FixedMul(RwScaleStep, WorldLow);
            }
        }

        // render it (r_segs.c:489-493)
        if (MarkCeiling)
        {
            _planes.CeilingPlane = _planes.CheckPlane(_planes.CeilingPlane, RwX, RwStopX - 1);
        }

        if (MarkFloor)
        {
            _planes.FloorPlane = _planes.CheckPlane(_planes.FloorPlane, RwX, RwStopX - 1);
        }

        RenderSegLoop();

        // save sprite clipping info (r_segs.c:492-521)
        if ((ds.Silhouette & 2) != 0 || MaskedTexture)
        {
            if (ds.SprTopClip == -1)
            {
                CopyClipToOpening(_planes.CeilingClip, start, RwStopX - start);
                ds.SprTopClip = LastOpening - start;
                LastOpening += RwStopX - start;
            }
        }

        if ((ds.Silhouette & 1) != 0 || MaskedTexture)
        {
            if (ds.SprBottomClip == -1)
            {
                CopyClipToOpening(_planes.FloorClip, start, RwStopX - start);
                ds.SprBottomClip = LastOpening - start;
                LastOpening += RwStopX - start;
            }
        }

        if (MaskedTexture && (ds.Silhouette & 2) == 0)
        {
            ds.Silhouette |= 2;
            ds.TsilHeight = int.MinValue;
        }

        if (MaskedTexture && (ds.Silhouette & 1) == 0)
        {
            ds.Silhouette |= 1;
            ds.BsilHeight = int.MaxValue;
        }

        DsP++;
    }

    // ------------------------------------------------------------------
    // R_RenderSegLoop (r_segs.c:203-318)
    // ------------------------------------------------------------------

    /// <summary>
    /// R_RenderSegLoop (r_segs.c:203-318): draws zero, one, or two
    /// textures (and possibly a masked texture) for walls. Can draw or
    /// mark the starting pixel of floor and ceiling textures.
    /// HEIGHTBITS = 12, HEIGHTUNIT = 1 &lt;&lt; 12 (r_segs.c:200-201).
    /// </summary>
    public void RenderSegLoop()
    {
        const int heightBits = 12;
        const int heightUnit = 1 << heightBits;

        for (; RwX < RwStopX; RwX++)
        {
            // mark floor / ceiling areas (r_segs.c:216-247)
            var yl = (TopFrac + heightUnit - 1) >> heightBits;

            // no space above wall?
            if (yl < _planes.CeilingClip[RwX] + 1)
            {
                yl = _planes.CeilingClip[RwX] + 1;
            }

            if (MarkCeiling)
            {
                var top = _planes.CeilingClip[RwX] + 1;
                var bottom = yl - 1;

                if (bottom >= _planes.FloorClip[RwX])
                {
                    bottom = _planes.FloorClip[RwX] - 1;
                }

                if (top <= bottom)
                {
                    _planes.Visplanes[_planes.CeilingPlane].Top[RwX] = (byte)top;
                    _planes.Visplanes[_planes.CeilingPlane].Bottom[RwX] = (byte)bottom;
                }
            }

            var yh = BottomFrac >> heightBits;

            if (yh >= _planes.FloorClip[RwX])
            {
                yh = _planes.FloorClip[RwX] - 1;
            }

            if (MarkFloor)
            {
                var top = yh + 1;
                var bottom = _planes.FloorClip[RwX] - 1;
                if (top <= _planes.CeilingClip[RwX])
                {
                    top = _planes.CeilingClip[RwX] + 1;
                }

                if (top <= bottom)
                {
                    _planes.Visplanes[_planes.FloorPlane].Top[RwX] = (byte)top;
                    _planes.Visplanes[_planes.FloorPlane].Bottom[RwX] = (byte)bottom;
                }
            }

            // texturecolumn and lighting are independent of wall tiers
            // (r_segs.c:250-266)
            int texturecolumn = 0;
            if (SegTextured)
            {
                // calculate texture offset
                var angle = (int)((RwCenterAngle + _view.XToViewAngle[RwX]) >> AngleTables.AngleToFineShift);
                texturecolumn = RwOffset - FixedPoint.FixedMul(AngleTables.FineTangent[angle], RwDistance);
                texturecolumn >>= FixedPoint.FracBits;

                // calculate lighting
                var index = (uint)RwScale >> ViewSetup.LightScaleShift;
                if (index >= ViewSetup.MaxLightScale)
                {
                    index = ViewSetup.MaxLightScale - 1;
                }

                ColumnDrawState.Colormap = WallLights[index];
                ColumnDrawState.X = RwX;
                ColumnDrawState.IScale = unchecked((int)(0xffffffffu / (uint)RwScale));
            }

            // draw the wall tiers (r_segs.c:268-311)
            if (MidTexture != 0)
            {
                // single sided line
                ColumnDrawState.Yl = yl;
                ColumnDrawState.Yh = yh;
                ColumnDrawState.TextureMid = RwMidTextureMid;
                ColumnDrawState.Source = _textures.GetColumn(_wad, MidTexture, texturecolumn, out var midOfs);
                ColumnDrawState.SourceOfs = midOfs;

                if (RendererMath.DetailShift == 0)
                {
                    ColumnDrawer.DrawColumn(_view);
                }
                else
                {
                    ColumnDrawer.DrawColumnLow(_view);
                }

                _planes.CeilingClip[RwX] = (short)_view.ViewHeight;
                _planes.FloorClip[RwX] = -1;
            }
            else
            {
                // two sided line
                if (TopTexture != 0)
                {
                    // top wall (r_segs.c:275-293)
                    var mid = PixHigh >> heightBits;
                    PixHigh += PixHighStep;

                    if (mid >= _planes.FloorClip[RwX])
                    {
                        mid = _planes.FloorClip[RwX] - 1;
                    }

                    if (mid >= yl)
                    {
                        ColumnDrawState.Yl = yl;
                        ColumnDrawState.Yh = mid;
                        ColumnDrawState.TextureMid = RwTopTextureMid;
                        ColumnDrawState.Source = _textures.GetColumn(_wad, TopTexture, texturecolumn, out var topOfs);
                        ColumnDrawState.SourceOfs = topOfs;

                        if (RendererMath.DetailShift == 0)
                        {
                            ColumnDrawer.DrawColumn(_view);
                        }
                        else
                        {
                            ColumnDrawer.DrawColumnLow(_view);
                        }

                        _planes.CeilingClip[RwX] = (short)mid;
                    }
                    else
                    {
                        _planes.CeilingClip[RwX] = (short)(yl - 1);
                    }
                }
                else
                {
                    // no top wall
                    if (MarkCeiling)
                    {
                        _planes.CeilingClip[RwX] = (short)(yl - 1);
                    }
                }

                if (BottomTexture != 0)
                {
                    // bottom wall (r_segs.c:292-311)
                    var mid = (PixLow + heightUnit - 1) >> heightBits;
                    PixLow += PixLowStep;

                    // no space above wall?
                    if (mid <= _planes.CeilingClip[RwX])
                    {
                        mid = _planes.CeilingClip[RwX] + 1;
                    }

                    if (mid <= yh)
                    {
                        ColumnDrawState.Yl = mid;
                        ColumnDrawState.Yh = yh;
                        ColumnDrawState.TextureMid = RwBottomTextureMid;
                        ColumnDrawState.Source = _textures.GetColumn(_wad, BottomTexture, texturecolumn, out var botOfs);
                        ColumnDrawState.SourceOfs = botOfs;

                        if (RendererMath.DetailShift == 0)
                        {
                            ColumnDrawer.DrawColumn(_view);
                        }
                        else
                        {
                            ColumnDrawer.DrawColumnLow(_view);
                        }

                        _planes.FloorClip[RwX] = (short)mid;
                    }
                    else
                    {
                        _planes.FloorClip[RwX] = (short)(yh + 1);
                    }
                }
                else
                {
                    // no bottom wall
                    if (MarkFloor)
                    {
                        _planes.FloorClip[RwX] = (short)(yh + 1);
                    }
                }

                if (MaskedTexture)
                {
                    // save texturecol for backdrawing of masked mid
                    // texture (r_segs.c:313-316)
                    _planes.Openings[MaskedTextureCol + RwX] = (short)texturecolumn;
                }
            }

            RwScale += RwScaleStep;
            TopFrac += TopStep;
            BottomFrac += BottomStep;
        }
    }
}

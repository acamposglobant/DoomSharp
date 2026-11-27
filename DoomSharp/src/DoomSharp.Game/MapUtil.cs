using DoomSharp.Core;
using DoomSharp.Data;
using DoomSharp.Rendering;

namespace DoomSharp.Game;

/// <summary>
/// Port of p_maputl.c line/box side tests (P_PointOnLineSide,
/// P_BoxOnLineSide) — the game-side (p_*) variants, distinct from the
/// renderer's R_PointOnSide.
/// </summary>
public static partial class MapObject
{
    /// <summary>P_PointOnLineSide (p_maputl.c:75-100).</summary>
    public static int PointOnLineSide(int x, int y, Line ld)
    {
        if (ld.Dx == 0)
        {
            return x <= Level!.Vertexes[ld.V1].X ? (ld.Dy > 0 ? 1 : 0) : (ld.Dy < 0 ? 1 : 0);
        }

        if (ld.Dy == 0)
        {
            return y <= Level!.Vertexes[ld.V1].Y ? (ld.Dx < 0 ? 1 : 0) : (ld.Dx > 0 ? 1 : 0);
        }

        int dx = x - Level!.Vertexes[ld.V1].X;
        int dy = y - Level!.Vertexes[ld.V1].Y;

        int left = FixedPoint.FixedMul(ld.Dy >> FixedPoint.FracBits, dx);
        int right = FixedPoint.FixedMul(dy, ld.Dx >> FixedPoint.FracBits);

        return right < left ? 0 : 1;    // 0 = front side, 1 = back side
    }

    /// <summary>P_BoxOnLineSide (p_maputl.c:105-165).</summary>
    public static int BoxOnLineSide(int[] tmBox, Line ld)
    {
        int p1, p2;

        switch (ld.SlopeType)
        {
            case SlopeType.Horizontal:
                p1 = tmBox[BBox.Top] > Level!.Vertexes[ld.V1].Y ? 1 : 0;
                p2 = tmBox[BBox.Bottom] > Level!.Vertexes[ld.V1].Y ? 1 : 0;
                if (ld.Dx < 0)
                {
                    p1 ^= 1;
                    p2 ^= 1;
                }
                break;

            case SlopeType.Vertical:
                p1 = tmBox[BBox.Right] < Level!.Vertexes[ld.V1].X ? 1 : 0;
                p2 = tmBox[BBox.Left] < Level!.Vertexes[ld.V1].X ? 1 : 0;
                if (ld.Dy < 0)
                {
                    p1 ^= 1;
                    p2 ^= 1;
                }
                break;

            case SlopeType.Positive:
                p1 = PointOnLineSide(tmBox[BBox.Left], tmBox[BBox.Top], ld);
                p2 = PointOnLineSide(tmBox[BBox.Right], tmBox[BBox.Bottom], ld);
                break;

            case SlopeType.Negative:
                p1 = PointOnLineSide(tmBox[BBox.Right], tmBox[BBox.Top], ld);
                p2 = PointOnLineSide(tmBox[BBox.Left], tmBox[BBox.Bottom], ld);
                break;

            default:
                return -1;
        }

        return p1 == p2 ? p1 : -1;
    }
}

public static partial class MapObject
{
    /// <summary>line->v1->x via the vertex table.</summary>
    public static int VertexX(Line ld) => Level!.Vertexes[ld.V1].X;

    /// <summary>line->v1->y via the vertex table.</summary>
    public static int VertexY(Line ld) => Level!.Vertexes[ld.V1].Y;

    /// <summary>skyflatnum (r_sky.c) — resolved by the host at level load.</summary>
    public static int SkyFlatNum { get; set; } = -1;

    /// <summary>P_NightmareRespawn (p_mobj.c:385-450).</summary>
    private static void NightmareRespawn(Mobj mobj)
    {
        int x = mobj.SpawnPoint.X << FixedPoint.FracBits;
        int y = mobj.SpawnPoint.Y << FixedPoint.FracBits;

        // something is occupying its position?
        if (!CheckPosition(mobj, x, y))
        {
            return;     // no respawn
        }

        // spawn a teleport fog at old spot and new spot
        _ = SpawnMobj(mobj.X, mobj.Y, Level!.Sectors[mobj.SectorIndex].FloorHeight, (int)MobjType.TFog);
        _ = SpawnMobj(x, y, SectorFloorAt(x, y), (int)MobjType.TFog);

        // spawn the new monster, inheriting attributes from the deceased one
        int z = (InfoTables.MobjInfo[mobj.Type].Flags & MobjFlags.SpawnCeiling) != 0
            ? OnCeilingZ
            : OnFloorZ;

        var mo = SpawnMobj(x, y, z, mobj.Type);
        mo.SpawnPoint = mobj.SpawnPoint;
        mo.Angle = AngleTables.Ang45 * (uint)(mobj.SpawnPoint.Angle / 45);

        if ((mobj.SpawnPoint.Options & MapThingFlags.Ambush) != 0)
        {
            mo.Flags |= MobjFlags.Ambush;
        }

        mo.ReactionTime = 18;

        // remove the old monster
        RemoveMobj(mobj);
    }
}

public static partial class MapObject
{
    /// <summary>
    /// subsector -> sector floor height at a point (the C reads
    /// ss->sector->floorheight directly).
    /// </summary>
    public static int SectorFloorAt(int x, int y)
    {
        var ss = RendererMath.PointInSubsector(x, y, Level!);
        int ssIndex = Array.IndexOf(Level!.Subsectors, ss);
        return Level.Sectors[SubsectorSector(ssIndex)].FloorHeight;
    }
}

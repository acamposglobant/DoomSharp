using DoomSharp.Core;
using DoomSharp.Data;

namespace DoomSharp.Game;

/// <summary>
/// Map Object definition — mobj_t (p_mobj.h:210-295). Links into the
/// sector thing lists and the blockmap are direct references, matching
/// the C's intrusive lists.
/// </summary>
public sealed class Mobj : Thinker
{
    // Info for drawing: position.
    public int X;               // fixed
    public int Y;               // fixed
    public int Z;               // fixed

    // More list: links in sector (if needed).
    public Mobj? SNext;
    public Mobj? SPrev;

    // More drawing info: to determine current sprite.
    public uint Angle;          // angle_t BAM

    // Interaction info, by BLOCKMAP.
    public Mobj? BNext;
    public Mobj? BPrev;

    public Subsector? SubSector;

    // The closest interval over all contacted Sectors.
    public int FloorZ;
    public int CeilingZ;

    // For movement checking.
    public int Radius;
    public int Height;

    // Momentums, used to update position.
    public int MomX;
    public int MomY;
    public int MomZ;

    // If == validcount, already checked.
    public int ValidCount;

    public int Type;            // mobjtype_t
    public MobjInfo Info;
    public int Tics;            // state tic counter
    public StateDef State;
    public int StateIndex;
    public uint Flags;
    public int Health;

    // Movement direction, movement generation.
    public DirType MoveDir;
    public int MoveCount;
    public Mobj? Target;

    // Reaction time: if non 0, don't attack yet.
    public int ReactionTime;

    // Used by player to freeze a bit after attacking.
    public int Threshold;

    // Extended player object if this is a player.
    public Player? Player;

    public int LastLook;

    public MapThing SpawnPoint;

    // Thing being chased/attacked (or NULL) — also the originator for missiles.
    public Mobj? Tracer;

    // Game-side sector index (the C reaches it via subsector->sector).
    public int SectorIndex = -1;

    /// <summary>
    /// The renderer's view of this thing, registered in SectorThings and
    /// synced by the thinker each tic (the MobjVisual bridge).
    /// </summary>
    public MobjVisual Visual = new();

    /// <summary>Syncs the render view fields (called after movement).</summary>
    /// <summary>P_MobjThinker dispatch (p_mobj.c:655-710).</summary>
    public override void Think()
    {
        MapObject.MobjThinker(this);
    }

    public void SyncVisual()
    {
        Visual.X = X;
        Visual.Y = Y;
        Visual.Z = Z;
        Visual.Angle = Angle;
        Visual.Sprite = Sprite;
        Visual.Frame = Frame;
        Visual.Flags = (int)Flags;
    }

    // Sprite/frame live on the visual (the renderer reads them there);
    // these properties keep the C's mobj->sprite / mobj->frame spelling.
    public int Sprite { get => Visual.Sprite; set => Visual.Sprite = value; }
    public int Frame { get => Visual.Frame; set => Visual.Frame = value; }
}

/// <summary>dirtype_t (p_local.h:60-70).</summary>
public enum DirType
{
    East = 0,
    NorthEast = 1,
    North = 2,
    NorthWest = 3,
    West = 4,
    SouthWest = 5,
    South = 6,
    SouthEast = 7,
    NoDir = 8,
}

using Serilog;

namespace DoomSharp.Game;

/// <summary>
/// thinker_t (d_think.h:43-56) and the thinker list management from
/// p_tick.c:60-125. Deallocation is lazy — a removed thinker is unlinked
/// only when its thinking turn comes up, so removal mid-iteration is safe.
/// </summary>
public abstract class Thinker
{
    private static ILogger Log => Serilog.Log.ForContext(typeof(Thinker));

    public Thinker? Next;
    public Thinker? Prev;

    /// <summary>
    /// The C's <c>function.acv == (actionf_v)(-1)</c> deferred-removal
    /// sentinel (p_tick.c:83-86).
    /// </summary>
    public bool Removed;

    /// <summary>
    /// The per-tic think function (function.acp1). Null thinkers (plain
    /// thinkers with no action) are supported, matching the C.
    /// </summary>
    public virtual void Think()
    {
    }

    /// <summary>P_InitThinkers (p_tick.c:50-56).</summary>
    public static void InitThinkers(ref Thinker cap)
    {
        cap.Next = cap;
        cap.Prev = cap;
    }

    /// <summary>P_AddThinker (p_tick.c:66-75): adds at the end of the list.</summary>
    public static void AddThinker(Thinker cap, Thinker thinker)
    {
        cap.Prev!.Next = thinker;
        thinker.Next = cap;
        thinker.Prev = cap.Prev;
        cap.Prev = thinker;
    }

    /// <summary>
    /// P_RemoveThinker (p_tick.c:86-91): deallocation is lazy — the thinker
    /// is unlinked on its next thinking turn.
    /// </summary>
    public static void RemoveThinker(Thinker thinker)
    {
        thinker.Removed = true;
    }

    /// <summary>P_RunThinkers (p_tick.c:104-131).</summary>
    public static void RunThinkers(Thinker cap)
    {
        var currentThinker = cap.Next;
        while (currentThinker != cap)
        {
            if (currentThinker!.Removed)
            {
                // time to remove it
                currentThinker.Next!.Prev = currentThinker.Prev;
                currentThinker.Prev!.Next = currentThinker.Next;
            }
            else
            {
                currentThinker.Think();
            }

            currentThinker = currentThinker.Next;
        }
    }
}

using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DeathrestUnbound;

/// <summary>
/// Vanilla keeps one pawn and one presence counter per deathrest building. While vanilla (or another mod's) code
/// runs on behalf of a pawn, we write that pawn and its link's presence into each building comp, then read
/// the presence back and restore the resting state (<c>null</c>, <c>0</c>). This keeps every original method and
/// every third-party patch on <see cref="CompDeathrestBindable"/> working unchanged, once per served pawn.
/// </summary>
public static class PawnContext
{
    private struct Swap
    {
        public CompDeathrestBindable comp;
        public DeathrestLink link;
        public Pawn prevPawn;
        public int prevPresence;
    }

    private sealed class Frame
    {
        public Pawn pawn;
        public int depth;
        public readonly List<Swap> swaps = new List<Swap>();
    }

    private static readonly List<Frame> Stack = new List<Frame>();

    /// <summary>Starts (or nests) a context for every active link serving <paramref name="pawn"/>.</summary>
    public static void Enter(Pawn pawn)
    {
        if (Stack.Count > 0 && Stack[Stack.Count - 1].pawn == pawn)
        {
            Stack[Stack.Count - 1].depth++;
            return;
        }
        Frame frame = new Frame { pawn = pawn, depth = 1 };
        DeathrestLinkTracker tracker = DeathrestLinkTracker.Instance;
        if (tracker != null && pawn != null)
        {
            List<DeathrestLink> active = tracker.ActiveLinksFor(pawn);
            for (int i = 0; i < active.Count; i++)
            {
                frame.swaps.Add(SwapIn(active[i], pawn, outermost: Stack.Count == 0));
            }
        }
        Stack.Add(frame);
    }

    public static void Exit(Pawn pawn)
    {
        if (Stack.Count == 0)
        {
            return;
        }
        Frame frame = Stack[Stack.Count - 1];
        if (frame.pawn != pawn)
        {
            Log.ErrorOnce($"[DeathrestUnbound] Unbalanced deathrest context exit for {pawn}.", 0x5DEA7);
        }
        if (--frame.depth > 0)
        {
            return;
        }
        for (int i = frame.swaps.Count - 1; i >= 0; i--)
        {
            SwapOut(frame.swaps[i]);
        }
        Stack.RemoveAt(Stack.Count - 1);
    }

    /// <summary>Runs <paramref name="action"/> with one building comp temporarily serving one pawn.</summary>
    public static void RunAs(DeathrestLink link, Pawn pawn, Action action)
    {
        Swap swap = SwapIn(link, pawn, outermost: false);
        try
        {
            action();
        }
        finally
        {
            SwapOut(swap);
        }
    }

    private static Swap SwapIn(DeathrestLink link, Pawn pawn, bool outermost)
    {
        CompDeathrestBindable comp = link.BuildingComp;
        Swap swap = new Swap { comp = comp, link = link, prevPawn = BindableUtil.BoundPawnRef(comp), prevPresence = comp.presenceTicks };
        // At rest a building's own counter is always 0; a positive value means code outside any context wrote it
        // (vanilla's "DEV: Wake and apply bonuses" sets presence directly). Keep the larger value.
        if (outermost && swap.prevPresence > link.presenceTicks)
        {
            link.presenceTicks = swap.prevPresence;
        }
        BindableUtil.WriteState(comp, pawn, link.presenceTicks);
        return swap;
    }

    private static void SwapOut(Swap swap)
    {
        swap.link.presenceTicks = swap.comp.presenceTicks;
        BindableUtil.WriteState(swap.comp, swap.prevPawn, swap.prevPresence > 0 && swap.prevPawn == null ? 0 : swap.prevPresence);
    }
}

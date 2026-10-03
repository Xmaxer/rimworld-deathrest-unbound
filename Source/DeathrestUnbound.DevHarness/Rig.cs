using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace DeathrestUnbound.DevHarness;

/// <summary>A sealed, roofed, force-powered deathrest room with caskets, buildings and deathresting sanguophages.</summary>
public sealed class Rig
{
    public readonly Map Map;
    public readonly CellRect Inner;
    public readonly List<Building_Bed> Caskets = new List<Building_Bed>();
    public readonly List<Thing> Buildings = new List<Thing>();
    public readonly List<Pawn> Pawns = new List<Pawn>();

    public static DeathrestLinkTracker T => DeathrestLinkTracker.Instance;

    public Rig(Map map, CellRect area, int width, int height)
    {
        Map = map;
        Inner = HarnessUtil.BuildSealedRoom(map, new CellRect(area.minX, area.minZ, width, height), roofed: true);
    }

    public IntVec3 At(int x, int z)
    {
        return new IntVec3(Inner.minX + x, 0, Inner.minZ + z);
    }

    public Building_Bed Casket(int x, int z)
    {
        Building_Bed casket = (Building_Bed)HarnessUtil.SpawnBuilding(ThingDef.Named("DeathrestCasket"), At(x, z), Map, Rot4.North);
        Caskets.Add(casket);
        return casket;
    }

    public Thing Building(string defName, int x, int z)
    {
        Thing building = HarnessUtil.SpawnBuilding(ThingDef.Named(defName), At(x, z), Map, Rot4.North);
        Buildings.Add(building);
        return building;
    }

    /// <summary>Spawns a sanguophage on the casket, assigns it, and starts the vanilla deathrest job.</summary>
    public Pawn Deathrester(Building_Bed casket, Action<Pawn> beforeJob = null)
    {
        Pawn pawn = HarnessUtil.SpawnColonist(casket.Position, Map, XenotypeDefOf.Sanguophage);
        casket.GetComp<CompAssignableToPawn>().TryAssignPawn(pawn);
        beforeJob?.Invoke(pawn);
        pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Deathrest, casket), JobCondition.InterruptForced);
        Pawns.Add(pawn);
        return pawn;
    }

    public bool AllDeathresting
    {
        get
        {
            for (int i = 0; i < Pawns.Count; i++)
            {
                if (!Pawns[i].Deathresting)
                {
                    return false;
                }
            }
            return true;
        }
    }

    public static DeathrestLink Link(Thing building, Thing casket)
    {
        List<DeathrestLink> links = T.LinksOfBuilding(building);
        for (int i = 0; i < links.Count; i++)
        {
            if (links[i].casket == casket)
            {
                return links[i];
            }
        }
        return null;
    }

    public static CompDeathrestBindable Comp(Thing thing)
    {
        return thing.Bindable();
    }

    /// <summary>Runs the comp's rare tick (which sets power) and returns the draw in watts.</summary>
    public static float Draw(Thing thing)
    {
        T.MarkDirty();
        T.EnsureResolved();
        Comp(thing).CompTickRare();
        return -thing.TryGetComp<CompPowerTrader>().PowerOutput;
    }

    public static void Resolve()
    {
        T.MarkDirty();
        T.EnsureResolved();
    }

    /// <summary>Despawns the pawns so later scenarios aren't affected by them.</summary>
    public void DespawnPawns()
    {
        for (int i = 0; i < Pawns.Count; i++)
        {
            if (!Pawns[i].Destroyed)
            {
                Pawns[i].Destroy(DestroyMode.Vanish);
            }
        }
    }
}

public static class Check
{
    public static void Near(List<string> failures, string what, float actual, float expected, float tolerance = 0.5f)
    {
        if (Math.Abs(actual - expected) > tolerance)
        {
            failures.Add($"{what}: expected {expected:0.###}, got {actual:0.###}");
        }
    }

    public static void Eq<T>(List<string> failures, string what, T actual, T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
        {
            failures.Add($"{what}: expected {expected}, got {actual}");
        }
    }

    public static void True(List<string> failures, string what, bool condition)
    {
        if (!condition)
        {
            failures.Add(what);
        }
    }

    /// <summary>Every linkable building must be back in its resting state (no vanilla pawn, no presence) outside contexts.</summary>
    public static void AtRest(List<string> failures, IEnumerable<Thing> buildings)
    {
        foreach (Thing b in buildings)
        {
            CompDeathrestBindable comp = b.Bindable();
            if (comp == null || comp.IsCasket() || b.Destroyed)
            {
                continue;
            }
            if (BindableUtil.BoundPawnRef(comp) != null || comp.presenceTicks != 0)
            {
                failures.Add($"{b.LabelShort} not at rest: boundPawn={BindableUtil.BoundPawnRef(comp)}, presence={comp.presenceTicks}");
            }
        }
    }
}

/// <summary>
/// Base for rig scenarios: waits until every pawn deathrests, then runs timed steps (ticks counted from that moment).
/// Passes when all steps ran with no failures.
/// </summary>
public abstract class RigScenario : HarnessScenario
{
    protected Rig rig;
    private int readyTick = -1;
    private int nextStep;
    private List<(int at, Action<List<string>> step)> steps;

    public override int TimeoutTicks => 2500 + LastStepTick;

    protected abstract Rig Build(Map map, CellRect area);

    protected abstract IEnumerable<(int at, Action<List<string>> step)> Steps();

    private int LastStepTick
    {
        get
        {
            steps ??= new List<(int, Action<List<string>>)>(Steps());
            return steps.Count == 0 ? 0 : steps[steps.Count - 1].at;
        }
    }

    public override void Setup(Map map, CellRect area)
    {
        rig = Build(map, area);
        Rig.Resolve();
        steps ??= new List<(int, Action<List<string>>)>(Steps());
        HarnessUtil.JumpCamera(rig.Inner.CenterCell);
    }

    public override ScenarioStatus Tick(Map map, int ticksSinceSetup, List<string> failures)
    {
        if (readyTick < 0)
        {
            if (!rig.AllDeathresting)
            {
                return ScenarioStatus.Running;
            }
            readyTick = ticksSinceSetup;
            Metrics.Add($"deathrest started after {readyTick} ticks");
        }
        int t = ticksSinceSetup - readyTick;
        while (nextStep < steps.Count && t >= steps[nextStep].at)
        {
            steps[nextStep].step(failures);
            nextStep++;
        }
        if (nextStep >= steps.Count)
        {
            return failures.Count == 0 ? ScenarioStatus.Passed : ScenarioStatus.Failed;
        }
        return ScenarioStatus.Running;
    }

    public override void Cleanup(Map map)
    {
        rig?.DespawnPawns();
    }
}

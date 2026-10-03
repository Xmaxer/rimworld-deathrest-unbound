using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DeathrestUnbound.DevHarness.Scenarios;

/// <summary>
/// Opens the links tab on a casket and a building, the pawn's deathrest gizmo and the mod settings, and screenshots
/// each. Any exception while drawing is logged and fails the run through the error summary.
/// </summary>
public sealed class UiScenario : RigScenario
{
    private Window settings;
    private bool savedEnforce;

    public override string Name => "ui";

    protected override Rig Build(Map map, CellRect area)
    {
        Rig r = new Rig(map, area, 14, 6);
        Building_Bed a = r.Casket(0, 0);
        r.Casket(2, 0);
        r.Building("DeathrestAccelerator", 5, 0);
        r.Building("Hemopump", 7, 0);
        r.Building("GlucosoidPump", 9, 0);
        r.Deathrester(a);
        return r;
    }

    private static void SelectAndOpen(Thing thing)
    {
        Find.Selector.ClearSelection();
        Find.Selector.Select(thing, playSound: false, forceDesignatorDeselect: false);
        InspectPaneUtility.OpenTab(typeof(ITab_DeathrestLinks));
        HarnessUtil.JumpCamera(thing.Position);
    }

    protected override IEnumerable<(int, Action<List<string>>)> Steps()
    {
        yield return (30, f =>
        {
            Rig.T.SetEnabled(Rig.Link(rig.Buildings[2], rig.Caskets[0]), false);
            SelectAndOpen(rig.Caskets[0]);
            string text = Rig.Comp(rig.Caskets[0]).CompInspectStringExtra();
            Check.True(f, "casket inspect text missing formula: " + text, text.Contains("^2"));
            string btext = Rig.Comp(rig.Buildings[0]).CompInspectStringExtra();
            Check.True(f, "building inspect text missing: " + btext, !btext.NullOrEmpty());
        });
        yield return (150, f =>
        {
            ITab_DeathrestLinks tab = (ITab_DeathrestLinks)InspectTabManager.GetSharedInstance(typeof(ITab_DeathrestLinks));
            Check.True(f, "links tab not visible on casket", tab.IsVisible);
            HarnessUtil.Screenshot("ui-casket-tab");
        });
        yield return (200, f => SelectAndOpen(rig.Buildings[0]));
        yield return (320, f => HarnessUtil.Screenshot("ui-building-tab"));
        yield return (330, f =>
        {
            Find.Selector.ClearSelection();
            Find.Selector.Select(rig.Pawns[0], playSound: false, forceDesignatorDeselect: false);
        });
        yield return (450, f => HarnessUtil.Screenshot("ui-pawn-gizmo"));
        yield return (460, f =>
        {
            Find.Selector.ClearSelection();
            savedEnforce = DeathrestUnboundMod.Settings.enforceStackLimits;
            DeathrestUnboundMod.Settings.enforceStackLimits = true;
            KeepWindows = true;
            settings = new Dialog_ModSettings(DeathrestUnboundMod.Instance) { forcePause = false };
            Find.WindowStack.Add(settings);
        });
        yield return (600, f =>
        {
            Check.True(f, "settings window closed unexpectedly", Find.WindowStack.IsOpen(settings));
            HarnessUtil.Screenshot("ui-settings");
        });
        yield return (640, f =>
        {
            Find.WindowStack.TryRemove(settings, doCloseSound: false);
            KeepWindows = false;
            DeathrestUnboundMod.Settings.enforceStackLimits = savedEnforce;
        });
    }
}

/// <summary>
/// VRE Sanguophage: its buildings link and share like vanilla ones, the Invocation Matrix ability (an Apply
/// postfix reading BoundPawn) reaches both pawns, and the Psychofluid Well's per-hour psyfocus (a CompTickRare
/// postfix reading boundPawn) reaches both pawns. Skipped when VRE Sanguophage is not active.
/// </summary>
public sealed class VreScenario : RigScenario
{
    private bool active;
    private Thing matrix;
    private Thing well;
    private bool skipped;

    public override string Name => "vre";

    public override int TimeoutTicks => 7000;

    public override void Setup(Map map, CellRect area)
    {
        active = ModsConfig.IsActive("vanillaracesexpanded.sanguophage");
        if (!active)
        {
            skipped = true;
            Metrics.Add("skipped: Vanilla Races Expanded - Sanguophage is not active");
            return;
        }
        base.Setup(map, area);
    }

    public override ScenarioStatus Tick(Map map, int ticksSinceSetup, List<string> failures)
    {
        return skipped ? ScenarioStatus.Passed : base.Tick(map, ticksSinceSetup, failures);
    }

    protected override Rig Build(Map map, CellRect area)
    {
        Rig r = new Rig(map, area, 16, 7);
        Building_Bed a = r.Casket(0, 0);
        Building_Bed b = r.Casket(2, 0);
        matrix = r.Building("VRE_InvocationMatrix", 5, 0);
        if (ModsConfig.RoyaltyActive)
        {
            well = r.Building("VRE_PsychofluidWell", 8, 0);
        }
        r.Building("VRE_SmallHemopump", 11, 0);
        Action<Pawn> psycaster = p =>
        {
            if (ModsConfig.RoyaltyActive)
            {
                p.ChangePsylinkLevel(1, sendLetter: false);
                p.psychicEntropy.OffsetPsyfocusDirectly(-1f);
            }
        };
        r.Deathrester(a, psycaster);
        r.Deathrester(b, psycaster);
        return r;
    }

    protected override IEnumerable<(int, Action<List<string>>)> Steps()
    {
        yield return (10, f =>
        {
            foreach (Thing t in rig.Buildings)
            {
                Check.True(f, t.def.defName + " has no links tab", t.def.inspectorTabs.Contains(typeof(ITab_DeathrestLinks)));
                Check.Eq(f, t.def.defName + " active links", Rig.T.ActiveCount(Rig.Comp(t)), 2);
            }
            Check.Near(f, "invocation matrix draw (2 caskets)", Rig.Draw(matrix), 400f);
            Check.AtRest(f, rig.Buildings);
        });
        if (ModsConfig.RoyaltyActive)
        {
            yield return (2600, f =>
            {
                foreach (Pawn p in rig.Pawns)
                {
                    float focus = p.psychicEntropy.CurrentPsyfocus;
                    Metrics.Add($"{p.LabelShort} psyfocus after 2600 ticks: {focus:0.###}");
                    Check.True(f, p.LabelShort + " got no psyfocus from the shared psychofluid well", focus >= 0.04f);
                }
            });
        }
        yield return (2610, f =>
        {
            AbilityDef coagulate = DefDatabase<AbilityDef>.GetNamed("VRE_Coagulate_SingleUse");
            for (int i = 0; i < rig.Pawns.Count; i++)
            {
                Gene_Deathrest gene = rig.Pawns[i].DeathrestGene();
                gene.deathrestTicks = gene.MinDeathrestTicks + 1;
                Rig.Link(matrix, rig.Caskets[i]).presenceTicks = gene.deathrestTicks;
            }
            foreach (Pawn p in rig.Pawns)
            {
                p.DeathrestGene().Wake();
                Check.True(f, p.LabelShort + " did not gain the invocation matrix ability", p.abilities.GetAbility(coagulate) != null);
            }
            Check.AtRest(f, rig.Buildings);
        });
    }
}

/// <summary>Six deathresting pawns, 330 links: reports our per-tick cost and fails if it is unreasonable.</summary>
public sealed class PerfScenario : RigScenario
{
    public override string Name => "perf";

    protected override Rig Build(Map map, CellRect area)
    {
        Rig r = new Rig(map, area, 24, 24);
        List<Building_Bed> caskets = new List<Building_Bed>();
        for (int i = 0; i < 6; i++)
        {
            caskets.Add(r.Casket(i * 2, 0));
        }
        for (int row = 0; row < 5; row++)
        {
            for (int x = 0; x < 21; x += 2)
            {
                r.Building("Hemopump", x, 4 + row * 3);
            }
        }
        foreach (Building_Bed c in caskets)
        {
            r.Deathrester(c);
        }
        return r;
    }

    protected override IEnumerable<(int, Action<List<string>>)> Steps()
    {
        yield return (10, f => HarnessTiming.Reset());
        yield return (610, f =>
        {
            double perTick = HarnessTiming.Total / 600.0;
            Metrics.Add($"links: {Rig.T.AllLinks.Count}");
            Metrics.Add($"tracker tick avg: {HarnessTiming.Micros(HarnessTiming.TrackerTicks) / 600.0:0.0} us/tick");
            Metrics.Add($"building CompTick total: {HarnessTiming.Micros(HarnessTiming.CompTicks) / 600.0:0.0} us/tick");
            Metrics.Add($"gene TickDeathresting total: {HarnessTiming.Micros(HarnessTiming.GeneTicks) / 600.0:0.0} us/tick");
            Metrics.Add($"all mod-touched work: {perTick:0.0} us/tick");
            Check.True(f, $"per-tick cost too high: {perTick:0} us", perTick < 2000.0);
            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                Rig.Resolve();
            }
            sw.Stop();
            Metrics.Add($"full resolve: {sw.Elapsed.TotalMilliseconds * 10.0:0.0} us");
            Check.True(f, "resolve error: wrong link count", Rig.T.AllLinks.Count >= 330);
        });
    }
}

/// <summary>Wall-clock time in the mod's per-tick entry points. Dev harness only.</summary>
[StaticConstructorOnStartup]
public static class HarnessTiming
{
    public static long TrackerTicks;
    public static long CompTicks;
    public static long GeneTicks;

    public static double Total => Micros(TrackerTicks) + Micros(CompTicks) + Micros(GeneTicks);

    static HarnessTiming()
    {
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("Xmaxer.DeathrestUnbound.DevHarness");
        Wrap(harmony, AccessTools.Method(typeof(DeathrestLinkTracker), nameof(DeathrestLinkTracker.GameComponentTick)), nameof(StopTracker));
        Wrap(harmony, AccessTools.Method(typeof(CompDeathrestBindable), nameof(CompDeathrestBindable.CompTick)), nameof(StopComp));
        Wrap(harmony, AccessTools.Method(typeof(Gene_Deathrest), nameof(Gene_Deathrest.TickDeathresting)), nameof(StopGene));
    }

    private static void Wrap(HarmonyLib.Harmony harmony, System.Reflection.MethodInfo target, string stop)
    {
        HarmonyMethod prefix = new HarmonyMethod(typeof(HarnessTiming), nameof(Start)) { priority = Priority.First };
        HarmonyMethod postfix = new HarmonyMethod(typeof(HarnessTiming), stop) { priority = Priority.Last };
        harmony.Patch(target, prefix: prefix, postfix: postfix);
    }

    public static void Reset()
    {
        TrackerTicks = CompTicks = GeneTicks = 0;
    }

    public static double Micros(long ticks)
    {
        return ticks * 1000000.0 / Stopwatch.Frequency;
    }

    private static void Start(out long __state)
    {
        __state = Stopwatch.GetTimestamp();
    }

    private static void StopTracker(long __state)
    {
        TrackerTicks += Stopwatch.GetTimestamp() - __state;
    }

    private static void StopComp(long __state)
    {
        CompTicks += Stopwatch.GetTimestamp() - __state;
    }

    private static void StopGene(long __state)
    {
        GeneTicks += Stopwatch.GetTimestamp() - __state;
    }
}

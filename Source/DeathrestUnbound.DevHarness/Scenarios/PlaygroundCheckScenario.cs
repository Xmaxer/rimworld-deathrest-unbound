using System.Collections.Generic;
using System.Linq;
using DeathrestUnbound.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace DeathrestUnbound.DevHarness.Scenarios;

/// <summary>
/// Builds the interactive playground (what `make play` gives a new colony) inside a test run, sends its three
/// sanguophages to deathrest and checks the result works as a starting point: generators cover the projected draw,
/// every enabled link ends up in use on a powered casket, and vanilla's dev "Wake and apply bonuses" gives every
/// pawn its bonuses (with VRE, the shared Invocation Matrix ability).
/// </summary>
public sealed class PlaygroundCheckScenario : HarnessScenario
{
    private PlaygroundScenario playground;
    private List<Pawn> pawns;
    private int step;

    public override string Name => "playground-check";

    public override int TimeoutTicks => 4000;

    public override void Setup(Map map, CellRect area)
    {
        playground = new PlaygroundScenario();
        playground.Setup(map, area);
        pawns = playground.Caskets.Select(c => c.GetComp<CompAssignableToPawn>().AssignedPawnsForReading.FirstOrDefault()).ToList();
    }

    public override ScenarioStatus Tick(Map map, int t, List<string> failures)
    {
        List<Building_Bed> caskets = playground.Caskets;
        DeathrestLinkTracker tracker = DeathrestLinkTracker.Instance;
        bool vre = ModsConfig.IsActive("vanillaracesexpanded.sanguophage");
        if (step == 0)
        {
            step++;
            Check.Eq(failures, "playground caskets", caskets.Count, 3);
            Check.True(failures, "a casket has no owner", pawns.All(p => p != null));
            if (failures.Count > 0)
            {
                return ScenarioStatus.Failed;
            }
            int[] enabled = caskets.Select(c => tracker.LinksOfCasket(c).Count(l => l.enabled)).ToArray();
            Metrics.Add($"links per casket: {tracker.LinksOfCasket(caskets[0]).Count}; enabled: {string.Join("/", enabled)}");
            Metrics.Add($"projected draw {playground.ProjectedDemand:0} W, generators {playground.Generators} kW");
            int types = tracker.LinksOfCasket(caskets[0]).Select(l => l.building.def).Distinct().Count();
            Check.Eq(failures, "enabled links", string.Join("/", enabled), $"{System.Math.Min(6, types)}/4/3");
            Check.True(failures, "generators do not cover 1.4x the projected draw",
                playground.Generators >= 10 || playground.Generators * 1000f >= playground.ProjectedDemand * 1.4f);
            if (vre)
            {
                foreach (Building_Bed c in caskets)
                {
                    Check.True(failures, "Invocation Matrix not enabled on every casket",
                        tracker.LinksOfCasket(c).Any(l => l.enabled && l.building.def.defName == "VRE_InvocationMatrix"));
                }
            }
            for (int i = 0; i < 3; i++)
            {
                pawns[i].jobs.StartJob(JobMaker.MakeJob(JobDefOf.Deathrest, caskets[i]), JobCondition.InterruptForced);
            }
            HarnessUtil.JumpCamera(caskets[1].Position);
        }
        else if (step == 1 && t >= 1800)
        {
            step++;
            HarnessUtil.Screenshot("playground");
            Check.True(failures, "not every playground pawn is deathresting", pawns.All(p => p.Deathresting));
            tracker.MarkDirty();
            tracker.EnsureResolved();
            PowerNet net = caskets[0].GetComp<CompPowerTrader>().PowerNet;
            Metrics.Add($"net after 1800 ticks: {net?.powerComps.Count(c => !c.PowerOn)} of {net?.powerComps.Count} consumers off, " +
                $"gain {net?.CurrentEnergyGainRate() / CompPower.WattsToWattDaysPerTick:0} W");
            foreach (Building_Bed c in caskets)
            {
                Check.True(failures, c.LabelShort + " unpowered", c.GetComp<CompPowerTrader>().PowerOn);
                foreach (DeathrestLink l in tracker.LinksOfCasket(c).Where(l => l.enabled))
                {
                    Check.Eq(failures, $"{l.building.LabelShort} on casket {caskets.IndexOf(c) + 1}", l.status, LinkStatus.Active);
                }
            }
        }
        else if (step == 2 && t >= 1810)
        {
            bool saved = DebugSettings.godMode;
            DebugSettings.godMode = true;
            try
            {
                foreach (Pawn p in pawns)
                {
                    p.DeathrestGene().GetGizmos().OfType<Command_Action>()
                        .FirstOrDefault(g => g.defaultLabel == "DEV: Wake and apply bonuses")?.action();
                }
            }
            finally
            {
                DebugSettings.godMode = saved;
            }
            if (vre)
            {
                AbilityDef coagulate = DefDatabase<AbilityDef>.GetNamed("VRE_Coagulate_SingleUse");
                foreach (Pawn p in pawns)
                {
                    Check.True(failures, p.LabelShort + " got no Invocation Matrix ability from dev wake", p.abilities.GetAbility(coagulate) != null);
                }
            }
            return failures.Count == 0 ? ScenarioStatus.Passed : ScenarioStatus.Failed;
        }
        return ScenarioStatus.Running;
    }
}

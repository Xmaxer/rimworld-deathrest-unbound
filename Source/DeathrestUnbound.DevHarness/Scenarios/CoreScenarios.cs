using System;
using System.Collections.Generic;
using DeathrestUnbound.Core;
using RimWorld;
using Verse;

namespace DeathrestUnbound.DevHarness.Scenarios;

/// <summary>The mod loaded: tracker exists, tabs registered, game ticks without errors.</summary>
public sealed class BootScenario : HarnessScenario
{
    public override string Name => "boot";

    public override void Setup(Map map, CellRect area)
    {
    }

    public override ScenarioStatus Tick(Map map, int ticksSinceSetup, List<string> failures)
    {
        if (ticksSinceSetup < 120)
        {
            return ScenarioStatus.Running;
        }
        Check.True(failures, "tracker missing", DeathrestLinkTracker.Instance != null);
        Check.True(failures, "settings missing", DeathrestUnboundMod.Settings != null);
        foreach (string def in new[] { "DeathrestCasket", "Hemopump", "HemogenAmplifier", "GlucosoidPump", "PsychofluidPump", "DeathrestAccelerator" })
        {
            Check.True(failures, def + " has no links tab", ThingDef.Named(def).inspectorTabs?.Contains(typeof(ITab_DeathrestLinks)) == true);
        }
        return failures.Count == 0 ? ScenarioStatus.Passed : ScenarioStatus.Failed;
    }
}

/// <summary>One pawn uses eight buildings (vanilla capacity 1); casket power is 100 x 1.5^8.</summary>
public sealed class UncapScenario : RigScenario
{
    private static readonly string[] Defs =
        { "Hemopump", "Hemopump", "HemogenAmplifier", "HemogenAmplifier", "GlucosoidPump", "GlucosoidPump", "DeathrestAccelerator", "DeathrestAccelerator" };

    public override string Name => "uncap";

    protected override Rig Build(Map map, CellRect area)
    {
        Rig r = new Rig(map, area, 20, 6);
        Building_Bed casket = r.Casket(0, 0);
        for (int i = 0; i < Defs.Length; i++)
        {
            r.Building(Defs[i], 2 + i * 2, 0);
        }
        r.Deathrester(casket);
        return r;
    }

    protected override IEnumerable<(int, Action<List<string>>)> Steps()
    {
        yield return (300, f =>
        {
            Building_Bed casket = rig.Caskets[0];
            Gene_Deathrest gene = rig.Pawns[0].DeathrestGene();
            List<DeathrestLink> links = Rig.T.LinksOfCasket(casket);
            Check.Eq(f, "links on casket", links.Count, 8);
            foreach (DeathrestLink l in links)
            {
                Check.Eq(f, l.building.LabelShort + " status", l.status, LinkStatus.Active);
                Check.True(f, l.building.LabelShort + " gained no presence", l.presenceTicks > 0);
            }
            Check.Eq(f, "BoundComps (casket + 8)", gene.BoundComps.Count, 9);
            Check.Eq(f, "vanilla deathrest capacity untouched", gene.DeathrestCapacity, 1);
            Check.Eq(f, "CurrentCapacity readout", gene.CurrentCapacity, 8);
            Check.Eq(f, "gene bound buildings are caskets only", gene.BoundBuildings.Count, 1);
            Check.AtRest(f, rig.Buildings);
            Check.Near(f, "deathrest efficiency (1.1 x 1.2^2)", gene.DeathrestEfficiency, 1.1f * 1.2f * 1.2f, 0.001f);
            float casketDraw = Rig.Draw(casket);
            Metrics.Add($"casket draw with 8 buildings: {casketDraw:0.#} W");
            Check.Near(f, "casket draw", casketDraw, 100f * (float)Math.Pow(1.5, 8));
            Check.Near(f, "hemopump draw", Rig.Draw(rig.Buildings[0]), 100f);
            Check.Near(f, "accelerator draw", Rig.Draw(rig.Buildings[7]), 800f);
            Check.AtRest(f, rig.Buildings);
        });
    }
}

/// <summary>
/// Two pawns share an accelerator and a hemopump: building power and fuel scale with caskets served, waking or
/// disabling a link drops them again, and disabled links stay disabled through rescans.
/// </summary>
public sealed class SharedBuildingScenario : RigScenario
{
    private Thing accel;
    private Thing pumpShared;
    private Thing pumpSingle;
    private float sharedFuel0;
    private float singleFuel0;

    public override string Name => "shared";

    protected override Rig Build(Map map, CellRect area)
    {
        Rig r = new Rig(map, area, 14, 6);
        Building_Bed a = r.Casket(0, 0);
        Building_Bed b = r.Casket(2, 0);
        accel = r.Building("DeathrestAccelerator", 5, 0);
        pumpShared = r.Building("Hemopump", 7, 0);
        pumpSingle = r.Building("Hemopump", 9, 0);
        r.Deathrester(a);
        r.Deathrester(b);
        return r;
    }

    public override void Setup(Map map, CellRect area)
    {
        base.Setup(map, area);
        Rig.T.SetEnabled(Rig.Link(pumpSingle, rig.Caskets[1]), false);
    }

    private static float Fuel(Thing t)
    {
        return t.TryGetComp<CompRefuelable>().Fuel;
    }

    protected override IEnumerable<(int, Action<List<string>>)> Steps()
    {
        Building_Bed a = null;
        Building_Bed b = null;
        yield return (10, f =>
        {
            a = rig.Caskets[0];
            b = rig.Caskets[1];
            sharedFuel0 = Fuel(pumpShared);
            singleFuel0 = Fuel(pumpSingle);
        });
        yield return (610, f =>
        {
            float shared = sharedFuel0 - Fuel(pumpShared);
            float single = singleFuel0 - Fuel(pumpSingle);
            Metrics.Add($"fuel used over 600 ticks: shared {shared:0.######}, single {single:0.######}");
            Check.True(f, "single hemopump burned no fuel", single > 0f);
            if (single > 0f)
            {
                Check.Near(f, "fuel ratio shared/single", shared / single, 2f, 0.1f);
            }
            string fuelText = pumpShared.TryGetComp<CompRefuelable>().CompInspectStringExtra();
            Metrics.Add("shared hemopump fuel readout: " + fuelText.Replace("\n", " | "));
            Check.True(f, "fuel readout ignores the 2 caskets served: " + fuelText, fuelText.Contains("serving 2 caskets") && fuelText.Contains("5 days"));
            Check.Eq(f, "accelerator active links", Rig.T.ActiveCount(Rig.Comp(accel)), 2);
            Check.Near(f, "accelerator draw (2 caskets)", Rig.Draw(accel), 1600f);
            Check.Near(f, "shared hemopump draw", Rig.Draw(pumpShared), 200f);
            Check.Near(f, "single hemopump draw", Rig.Draw(pumpSingle), 100f);
            Check.Near(f, "casket A draw (3 buildings)", Rig.Draw(a), 337.5f);
            Check.Near(f, "casket B draw (2 buildings)", Rig.Draw(b), 225f);
            Check.True(f, "both pawns got the accelerator's efficiency",
                Math.Abs(rig.Pawns[0].DeathrestGene().DeathrestEfficiency - 1.32f) < 0.001f &&
                Math.Abs(rig.Pawns[1].DeathrestGene().DeathrestEfficiency - 1.32f) < 0.001f);
            Check.True(f, "presence tracked per casket",
                Rig.Link(accel, a).presenceTicks > 0 && Rig.Link(accel, b).presenceTicks > 0);
            Check.AtRest(f, rig.Buildings);
        });
        yield return (620, f => rig.Pawns[1].DeathrestGene().Wake());
        yield return (700, f =>
        {
            Check.Eq(f, "accelerator active links after B woke", Rig.T.ActiveCount(Rig.Comp(accel)), 1);
            Check.Near(f, "accelerator draw (1 casket)", Rig.Draw(accel), 800f);
            Check.Near(f, "casket B idle draw", Rig.Draw(b), 0f);
            Check.Eq(f, "B link presence reset on wake", Rig.Link(accel, b).presenceTicks, 0);
        });
        yield return (710, f =>
        {
            Rig.T.SetEnabled(Rig.Link(accel, a), false);
            Check.Eq(f, "accelerator active links after disabling", Rig.T.ActiveCount(Rig.Comp(accel)), 0);
            Check.Near(f, "accelerator idle draw", Rig.Draw(accel), 0f);
            Check.Near(f, "casket A draw (2 buildings)", Rig.Draw(a), 225f);
            Check.Eq(f, "disabled link status", Rig.Link(accel, a).status, LinkStatus.Disabled);
        });
        yield return (1000, f =>
        {
            Check.True(f, "rescan re-enabled a disabled link", !Rig.Link(accel, a).enabled);
            Check.True(f, "rescan re-enabled the single hemopump link", !Rig.Link(pumpSingle, b).enabled);
            Rig.T.SetEnabled(Rig.Link(accel, a), true);
            Check.Eq(f, "accelerator active after re-enabling", Rig.T.ActiveCount(Rig.Comp(accel)), 1);
            Check.AtRest(f, rig.Buildings);
        });
    }
}

/// <summary>Shared buildings apply their effects to every pawn that met the 75% presence rule, and only to those.</summary>
public sealed class BonusesScenario : RigScenario
{
    private Thing gluco;
    private Thing amp;

    public override string Name => "bonuses";

    protected override Rig Build(Map map, CellRect area)
    {
        Rig r = new Rig(map, area, 12, 6);
        Building_Bed a = r.Casket(0, 0);
        Building_Bed b = r.Casket(2, 0);
        gluco = r.Building("GlucosoidPump", 5, 0);
        amp = r.Building("HemogenAmplifier", 7, 0);
        r.Deathrester(a);
        r.Deathrester(b);
        return r;
    }

    protected override IEnumerable<(int, Action<List<string>>)> Steps()
    {
        yield return (60, f =>
        {
            Building_Bed a = rig.Caskets[0];
            Building_Bed b = rig.Caskets[1];
            Pawn pa = rig.Pawns[0];
            Pawn pb = rig.Pawns[1];
            Gene_Deathrest ga = pa.DeathrestGene();
            Gene_Deathrest gb = pb.DeathrestGene();
            ga.deathrestTicks = ga.MinDeathrestTicks + 1;
            gb.deathrestTicks = gb.MinDeathrestTicks + 1;
            Rig.Link(gluco, a).presenceTicks = ga.deathrestTicks;
            Rig.Link(amp, a).presenceTicks = ga.deathrestTicks;
            Rig.Link(amp, b).presenceTicks = gb.deathrestTicks;
            Rig.Link(gluco, b).presenceTicks = 0;
            ga.Wake();
            gb.Wake();
            HediffDef rush = DefDatabase<HediffDef>.GetNamed("GlucosoidRush");
            HediffDef amplified = DefDatabase<HediffDef>.GetNamed("HemogenAmplified");
            Check.True(f, "pawn A missing glucosoid rush", pa.health.hediffSet.HasHediff(rush));
            Check.True(f, "pawn A missing hemogen amplified", pa.health.hediffSet.HasHediff(amplified));
            Check.True(f, "pawn B missing hemogen amplified (shared building)", pb.health.hediffSet.HasHediff(amplified));
            Check.True(f, "pawn B got glucosoid rush without presence", !pb.health.hediffSet.HasHediff(rush));
            Check.Eq(f, "A presence reset after wake", Rig.Link(gluco, a).presenceTicks, 0);
            Check.AtRest(f, rig.Buildings);
        });
    }
}

/// <summary>Stack limits and vanilla capacity, both off by default, work per casket when switched on.</summary>
public sealed class LimitsScenario : RigScenario
{
    private bool savedUncap;
    private bool savedEnforce;
    private Dictionary<string, int> savedOverrides;

    public override string Name => "limits";

    protected override Rig Build(Map map, CellRect area)
    {
        DeathrestUnboundSettings s = DeathrestUnboundMod.Settings;
        savedUncap = s.uncapCapacity;
        savedEnforce = s.enforceStackLimits;
        savedOverrides = new Dictionary<string, int>(s.stackLimitOverrides);
        Rig r = new Rig(map, area, 16, 6);
        Building_Bed casket = r.Casket(0, 0);
        r.Building("DeathrestAccelerator", 2, 0);
        r.Building("DeathrestAccelerator", 4, 0);
        r.Building("DeathrestAccelerator", 6, 0);
        r.Building("Hemopump", 8, 0);
        r.Building("Hemopump", 10, 0);
        r.Deathrester(casket);
        return r;
    }

    private List<LinkStatus> Statuses()
    {
        Rig.Resolve();
        List<LinkStatus> result = new List<LinkStatus>();
        foreach (Thing b in rig.Buildings)
        {
            result.Add(Rig.Link(b, rig.Caskets[0]).status);
        }
        return result;
    }

    private static string Show(List<LinkStatus> s)
    {
        return string.Join(",", s);
    }

    protected override IEnumerable<(int, Action<List<string>>)> Steps()
    {
        DeathrestUnboundSettings s = DeathrestUnboundMod.Settings;
        yield return (30, f =>
        {
            Check.Eq(f, "defaults: all active", Show(Statuses()), "Active,Active,Active,Active,Active");
            Check.Near(f, "casket draw (5)", Rig.Draw(rig.Caskets[0]), 100f * (float)Math.Pow(1.5, 5));
        });
        yield return (31, f =>
        {
            s.enforceStackLimits = true;
            Check.Eq(f, "vanilla stack limit 2 on accelerators", Show(Statuses()), "Active,Active,OverStackLimit,Active,Active");
            Check.Near(f, "casket draw (4)", Rig.Draw(rig.Caskets[0]), 100f * (float)Math.Pow(1.5, 4));
            Check.Eq(f, "over-limit accelerator unused", Rig.T.ActiveCount(Rig.Comp(rig.Buildings[2])), 0);
        });
        yield return (32, f =>
        {
            s.stackLimitOverrides["DeathrestAccelerator"] = 1;
            Check.Eq(f, "custom stack limit 1", Show(Statuses()), "Active,OverStackLimit,OverStackLimit,Active,Active");
        });
        yield return (33, f =>
        {
            s.enforceStackLimits = false;
            s.uncapCapacity = false;
            Check.Eq(f, "vanilla capacity 1", Show(Statuses()), "Active,OverCapacity,OverCapacity,OverCapacity,OverCapacity");
            Check.Eq(f, "gizmo shows vanilla max", Harmony.Patch_DeathrestGizmo.FormatBuildings("{0}: {1} / {2}", "Buildings", 1, 1), "Buildings: 1 / 1");
        });
        yield return (34, f =>
        {
            rig.Pawns[0].DeathrestGene().OffsetCapacity(2, sendNotification: false);
            Check.Eq(f, "capacity 3", Show(Statuses()), "Active,Active,Active,OverCapacity,OverCapacity");
            s.uncapCapacity = true;
            Check.Eq(f, "gizmo shows infinity when uncapped", Harmony.Patch_DeathrestGizmo.FormatBuildings("{0}: {1} / {2}", "Buildings", 5, 3), "Buildings: 5 / ∞");
            Check.Eq(f, "uncapped again", Show(Statuses()), "Active,Active,Active,Active,Active");
        });
    }

    public override void Cleanup(Map map)
    {
        DeathrestUnboundSettings s = DeathrestUnboundMod.Settings;
        s.uncapCapacity = savedUncap;
        s.enforceStackLimits = savedEnforce;
        s.stackLimitOverrides = savedOverrides ?? new Dictionary<string, int>();
        Rig.Resolve();
        base.Cleanup(map);
    }
}

/// <summary>An unpowered casket keeps demanding its exponential power, but none of its buildings work or draw.</summary>
public sealed class CasketUnpoweredScenario : RigScenario
{
    public override string Name => "unpowered";

    protected override Rig Build(Map map, CellRect area)
    {
        Rig r = new Rig(map, area, 12, 6);
        Building_Bed casket = r.Casket(0, 0);
        r.Building("Hemopump", 2, 0);
        r.Building("Hemopump", 4, 0);
        r.Building("Hemopump", 6, 0);
        r.Deathrester(casket);
        return r;
    }

    protected override IEnumerable<(int, Action<List<string>>)> Steps()
    {
        yield return (60, f =>
        {
            Building_Bed casket = rig.Caskets[0];
            HarnessUtil.Unforce(casket, powerOn: false);
            Rig.Resolve();
            foreach (DeathrestLink l in Rig.T.LinksOfCasket(casket))
            {
                Check.Eq(f, "status with casket unpowered", l.status, LinkStatus.CasketUnpowered);
            }
            Check.Near(f, "unpowered casket still demands 100 x 1.5^3", Rig.Draw(casket), 337.5f);
            Check.Near(f, "building idle while casket unpowered", Rig.Draw(rig.Buildings[0]), 0f);
            Check.Eq(f, "BoundComps is just the casket", rig.Pawns[0].DeathrestGene().BoundComps.Count, 1);
        });
        yield return (61, f =>
        {
            HarnessUtil.Force(rig.Caskets[0]);
            Rig.Resolve();
            Check.Eq(f, "BoundComps back after power returns", rig.Pawns[0].DeathrestGene().BoundComps.Count, 4);
            Check.Near(f, "building draw after power returns", Rig.Draw(rig.Buildings[0]), 100f);
        });
    }
}

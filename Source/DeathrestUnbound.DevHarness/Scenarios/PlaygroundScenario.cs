using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeathrestUnbound.DevHarness.Scenarios;

/// <summary>
/// Interactive dev convenience, not a test (tools/play.ps1 requests it). On a new colony: finishes the research
/// for every deathrest building and power, then builds a roofed deathrest hall next to the colonists with three
/// caskets owned by three new sanguophages, two of every vanilla deathrest building plus one of each VRE
/// Sanguophage building (when active), a 3 kW chemfuel power plant with charged batteries, and supplies. The
/// caskets start with different link sets so the power trade-off is visible right away. Control is then handed
/// back to the player, paused.
/// </summary>
public sealed class PlaygroundScenario : HarnessScenario
{
    private static readonly string[] Vanilla = { "Hemopump", "HemogenAmplifier", "GlucosoidPump", "PsychofluidPump", "DeathrestAccelerator" };

    private static readonly (string def, int width)[] Vre =
    {
        ("VRE_InvocationMatrix", 2), ("VRE_HemodynamicAccelerator", 2), ("VRE_HemogenSolidifier", 1),
        ("VRE_SmallHemogenAmplifier", 1), ("VRE_SmallHemopump", 1), ("VRE_PsychofluidWell", 2)
    };

    private static readonly (string def, int count)[] Supplies =
    {
        ("HemogenPack", 40), ("Chemfuel", 500), ("Steel", 800), ("ComponentIndustrial", 40), ("Plasteel", 150), ("Silver", 2000)
    };

    /// <summary>The caskets built by the last Setup (for PlaygroundCheckScenario).</summary>
    public List<Building_Bed> Caskets { get; private set; }

    public override string Name => "playground";

    public override bool IsPersistent => true;

    public override void Setup(Map map, CellRect area)
    {
        FinishResearch();
        IntVec3 origin = ColonistCenter(map);
        CellRect outer = new CellRect(origin.x - 11, origin.z + 4, 23, 15).ClipInsideMap(map);
        HarnessUtil.FlattenCells(map, outer.ExpandedBy(3));
        CellRect inner = HarnessUtil.BuildSealedRoom(map, outer, roofed: true, ThingDefOf.Steel);
        IntVec3 doorCell = new IntVec3(outer.CenterCell.x, 0, outer.minZ);
        HarnessUtil.SpawnBuilding(ThingDefOf.Door, doorCell, map, Rot4.North, ThingDefOf.Steel, forcePower: false);
        IntVec3 At(int x, int z) => new IntVec3(inner.minX + x, 0, inner.minZ + z);

        // Conduits first so every consumer connects on spawn: a row through the deathrest area, a row along the
        // generator row, and a spur on the east side joining them.
        for (int x = 0; x < inner.Width; x++)
        {
            Spawn("PowerConduit", At(x, 3), map);
            Spawn("PowerConduit", At(x, 10), map);
        }
        for (int z = 4; z < 10; z++)
        {
            Spawn("PowerConduit", At(inner.Width - 1, z), map);
        }

        List<Building_Bed> caskets = new List<Building_Bed>();
        for (int i = 0; i < 3; i++)
        {
            caskets.Add((Building_Bed)Spawn("DeathrestCasket", At(i * 3, 0), map));
        }
        Caskets = caskets;
        int bx = 0;
        foreach (string def in Vanilla)
        {
            Spawn(def, At(bx, 4), map);
            Spawn(def, At(bx + 2, 4), map);
            bx += 4;
        }
        int vx = 0;
        bool vre = ModsConfig.IsActive("vanillaracesexpanded.sanguophage");
        if (vre)
        {
            foreach ((string def, int width) in Vre)
            {
                if (DefDatabase<ThingDef>.GetNamedSilentFail(def) != null && vx + width <= 15)
                {
                    Spawn(def, At(vx, 7), map);
                    vx += width + 1;
                }
            }
        }
        for (int i = 0; i < 4; i++)
        {
            Thing battery = Spawn("Battery", At(16 + i, 7), map);
            CompPowerBattery cell = battery.TryGetComp<CompPowerBattery>();
            cell?.AddEnergy(cell.Props.storedEnergyMax);
        }

        // Sanguophages that own the caskets and will want to deathrest soon.
        IntVec3 outside = doorCell + new IntVec3(0, 0, -2);
        for (int i = 0; i < caskets.Count; i++)
        {
            Pawn p = HarnessUtil.SpawnColonist(outside + new IntVec3(i - 1, 0, 0), map, XenotypeDefOf.Sanguophage);
            caskets[i].GetComp<CompAssignableToPawn>().TryAssignPawn(p);
            Need_Deathrest need = p.needs?.TryGetNeed<Need_Deathrest>();
            if (need != null)
            {
                need.CurLevel = 0.05f;
            }
        }
        foreach ((string def, int count) in Supplies)
        {
            ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(def);
            if (thingDef != null)
            {
                DropStacks(thingDef, count, outside + new IntVec3(0, 0, -2), map);
            }
        }

        DeathrestLinkTracker tracker = DeathrestLinkTracker.Instance;
        tracker.MarkDirty();
        tracker.EnsureResolved();
        // A working starting point: casket 1 uses one building of each type, casket 2 four (preferring buildings
        // casket 1 does not use), casket 3 the matrix, an accelerator and a hemopump. The Invocation Matrix is shared.
        HashSet<Thing> used = new HashSet<Thing>();
        EnableTypes(tracker, caskets[0], 6, used, "VRE_InvocationMatrix");
        EnableTypes(tracker, caskets[1], 4, used, "VRE_InvocationMatrix");
        EnableTypes(tracker, caskets[2], 3, used, "VRE_InvocationMatrix", "DeathrestAccelerator", "Hemopump");
        tracker.MarkDirty();
        tracker.EnsureResolved();

        // Generators: 1.5x the draw with all three deathresting, in a row along the north wall.
        ProjectedDemand = Demand(tracker, caskets);
        int generators = Mathf.Clamp(Mathf.CeilToInt(ProjectedDemand * 1.5f / 1000f), 2, 10);
        for (int i = 0; i < generators; i++)
        {
            Spawn("ChemfuelPoweredGenerator", At(i * 2, 11), map);
        }
        Generators = generators;

        HarnessUtil.JumpCamera(inner.CenterCell);
        Find.LetterStack.ReceiveLetter("Deathrest Unbound playground",
            "A deathrest hall was built next to your colonists (dev harness only):\n\n" +
            "- Three caskets, each owned by a new sanguophage. Their deathrest need is low, so they will go to their caskets soon. " +
            "You can also right-click a sanguophage on its casket to deathrest now.\n" +
            "- Two of every vanilla deathrest building" + (vre ? ", plus one of each VRE Sanguophage building" : "") + ". " +
            "Every building is linked to all three caskets, but only some links start enabled: casket 1 uses one " +
            "building of each type, casket 2 uses four and casket 3 uses three" + (vre ? " (all three share the Invocation Matrix)" : "") + ".\n" +
            $"- {generators} chemfuel generators ({generators} kW, about 1.5x what the enabled links draw) and charged batteries.\n\n" +
            "Enable more links in a casket's 'Deathrest' tab to watch its draw climb (x1.5 per building) until the grid " +
            "browns out. An unpowered casket's buildings stop working and give no bonuses, including with the dev " +
            "'Wake and apply bonuses' button.\n\n" +
            "Select a casket or building and open its 'Deathrest' tab to toggle links and see the power change. " +
            "The mod settings (Options > Mod settings > Deathrest Unbound) change the exponent, limits and scaling live.",
            LetterDefOf.PositiveEvent, new LookTargets(caskets[0]));
    }

    public override ScenarioStatus Tick(Map map, int ticksSinceSetup, List<string> failures)
    {
        return ScenarioStatus.Passed;
    }

    /// <summary>Projected total draw (caskets + buildings) with every casket deathresting, set in Setup.</summary>
    public float ProjectedDemand { get; private set; }

    public int Generators { get; private set; }

    /// <summary>
    /// Enables up to <paramref name="max"/> links: the named defs first, then one building of each other type,
    /// preferring buildings no other casket uses yet. Everything else on the casket is disabled.
    /// </summary>
    private static void EnableTypes(DeathrestLinkTracker tracker, Thing casket, int max, HashSet<Thing> used, params string[] first)
    {
        List<DeathrestLink> links = tracker.LinksOfCasket(casket);
        List<DeathrestLink> chosen = new List<DeathrestLink>();
        HashSet<ThingDef> types = new HashSet<ThingDef>();
        void Pick(ThingDef def)
        {
            if (chosen.Count >= max || types.Contains(def))
            {
                return;
            }
            DeathrestLink best = links.Where(l => l.building.def == def).OrderBy(l => used.Contains(l.building) ? 1 : 0).FirstOrDefault();
            if (best != null)
            {
                chosen.Add(best);
                types.Add(def);
            }
        }
        foreach (string name in first)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
            if (def != null)
            {
                Pick(def);
            }
        }
        foreach (DeathrestLink l in links)
        {
            Pick(l.building.def);
        }
        foreach (DeathrestLink l in links)
        {
            tracker.SetEnabled(l, chosen.Contains(l));
        }
        foreach (DeathrestLink l in chosen)
        {
            used.Add(l.building);
        }
    }

    /// <summary>Casket draws for their enabled link counts, plus each building's draw per casket it would serve.</summary>
    public static float Demand(DeathrestLinkTracker tracker, List<Building_Bed> caskets)
    {
        DeathrestUnboundSettings s = DeathrestUnboundMod.Settings;
        float total = 0f;
        Dictionary<Thing, int> served = new Dictionary<Thing, int>();
        foreach (Building_Bed casket in caskets)
        {
            List<DeathrestLink> enabled = tracker.LinksOfCasket(casket).Where(l => l.enabled).ToList();
            CompPowerTrader power = casket.GetComp<CompPowerTrader>();
            total += Core.PowerMath.CasketDraw(power.Props.PowerConsumption, s.casketExponent, enabled.Count, s.casketPowerScaling);
            foreach (DeathrestLink l in enabled)
            {
                served.TryGetValue(l.building, out int n);
                served[l.building] = n + 1;
            }
        }
        foreach (KeyValuePair<Thing, int> kv in served)
        {
            CompPowerTrader power = kv.Key.TryGetComp<CompPowerTrader>();
            if (power != null)
            {
                total += Core.PowerMath.BuildingDraw(power.Props.PowerConsumption, 0f, kv.Value, s.buildingPowerScaling);
            }
        }
        return total;
    }

    private static Thing Spawn(string defName, IntVec3 cell, Map map, ThingDef stuff = null)
    {
        return HarnessUtil.SpawnBuilding(ThingDef.Named(defName), cell, map, Rot4.North, stuff, forcePower: false);
    }

    private static void FinishResearch()
    {
        HashSet<ResearchProjectDef> projects = new HashSet<ResearchProjectDef>();
        foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
        {
            if (def.GetCompProperties<CompProperties_DeathrestBindable>() != null && def.researchPrerequisites != null)
            {
                projects.AddRange(def.researchPrerequisites);
            }
        }
        foreach (string name in new[] { "Electricity", "Batteries" })
        {
            ResearchProjectDef p = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(name);
            if (p != null)
            {
                projects.Add(p);
            }
        }
        foreach (ResearchProjectDef p in projects)
        {
            if (!p.IsFinished)
            {
                Find.ResearchManager.FinishProject(p, doCompletionDialog: false, researcher: null, doCompletionLetter: false);
            }
        }
    }

    private static IntVec3 ColonistCenter(Map map)
    {
        List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
        if (colonists.Count == 0)
        {
            return map.Center;
        }
        int x = 0;
        int z = 0;
        foreach (Pawn p in colonists)
        {
            x += p.Position.x;
            z += p.Position.z;
        }
        return new IntVec3(x / colonists.Count, 0, z / colonists.Count);
    }

    private static void DropStacks(ThingDef def, int count, IntVec3 near, Map map)
    {
        while (count > 0)
        {
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = System.Math.Min(count, def.stackLimit);
            count -= thing.stackCount;
            GenPlace.TryPlaceThing(thing, near, map, ThingPlaceMode.Near);
        }
    }
}

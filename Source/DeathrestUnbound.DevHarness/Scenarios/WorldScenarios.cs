using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeathrestUnbound.Core;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DeathrestUnbound.DevHarness.Scenarios;

/// <summary>
/// Links follow rooms: only same-room buildings link; merging rooms creates links; splitting them makes links
/// dormant (kept); destroying a building removes its links.
/// </summary>
public sealed class RoomsScenario : HarnessScenario
{
    private Rig rig;
    private Thing left;
    private Thing right;
    private Thing outside;
    private IntVec3 gap;
    private int step;

    public override string Name => "rooms";

    public override int TimeoutTicks => 1200;

    public override void Setup(Map map, CellRect area)
    {
        rig = new Rig(map, area, 15, 7);
        int wallX = rig.Inner.minX + 6;
        for (int z = rig.Inner.minZ; z <= rig.Inner.maxZ; z++)
        {
            HarnessUtil.SpawnBuilding(ThingDefOf.Wall, new IntVec3(wallX, 0, z), map, Rot4.North, ThingDefOf.Plasteel);
        }
        gap = new IntVec3(wallX, 0, rig.Inner.minZ + 3);
        rig.Casket(1, 1);
        left = rig.Building("Hemopump", 3, 1);
        right = rig.Building("Hemopump", 9, 1);
        outside = HarnessUtil.SpawnBuilding(ThingDef.Named("Hemopump"), new IntVec3(area.minX + 1, 0, area.minZ + 10), map, Rot4.North);
        Rig.Resolve();
    }

    public override ScenarioStatus Tick(Map map, int t, List<string> failures)
    {
        Building_Bed casket = rig.Caskets[0];
        if (step == 0 && t >= 5)
        {
            step++;
            Check.True(failures, "same-room building not linked", Rig.Link(left, casket) != null);
            Check.True(failures, "other-room building linked", Rig.Link(right, casket) == null);
            Check.True(failures, "outdoor building linked", Rig.Link(outside, casket) == null);
            Check.Eq(failures, "idle status with nobody deathresting", Rig.Link(left, casket)?.status, LinkStatus.Idle);
            gap.GetEdifice(map).Destroy(DestroyMode.Vanish);
        }
        else if (step == 1 && t >= 300)
        {
            step++;
            Check.True(failures, "merged rooms: building not linked after rescan", Rig.Link(right, casket) != null);
            HarnessUtil.SpawnBuilding(ThingDefOf.Wall, gap, map, Rot4.North, ThingDefOf.Plasteel);
        }
        else if (step == 2 && t >= 400)
        {
            step++;
            Check.Eq(failures, "split rooms: link kept but dormant", Rig.Link(right, casket)?.status, LinkStatus.Dormant);
            left.Destroy(DestroyMode.Deconstruct);
            Rig.Resolve();
            Check.Eq(failures, "destroyed building's links removed", Rig.T.LinksOfBuilding(left).Count, 0);
            Check.True(failures, "destroyed building still listed on casket", Rig.T.LinksOfCasket(casket).All(l => l.building != left));
            return failures.Count == 0 ? ScenarioStatus.Passed : ScenarioStatus.Failed;
        }
        return ScenarioStatus.Running;
    }
}

/// <summary>Vanilla single-pawn bindings (old saves) are converted: cleared from the comp and the gene.</summary>
public sealed class MigrationScenario : HarnessScenario
{
    private Rig rig;
    private Pawn pawn;

    public override string Name => "migration";

    public override void Setup(Map map, CellRect area)
    {
        rig = new Rig(map, area, 8, 6);
        rig.Casket(0, 0);
        rig.Building("Hemopump", 3, 0);
        pawn = HarnessUtil.SpawnColonist(rig.At(5, 2), map, XenotypeDefOf.Sanguophage);
        Rig.Resolve();
    }

    public override ScenarioStatus Tick(Map map, int t, List<string> failures)
    {
        if (t < 5)
        {
            return ScenarioStatus.Running;
        }
        Thing pump = rig.Buildings[0];
        CompDeathrestBindable comp = Rig.Comp(pump);
        Gene_Deathrest gene = pawn.DeathrestGene();
        Check.True(failures, "gene can still bind a non-casket building", !gene.CanBindToBindable(comp));
        BindableUtil.BoundPawnRef(comp) = pawn;
        comp.presenceTicks = 50;
        gene.BoundBuildings.Add(pump);
        int cleared = Rig.T.MigrateVanillaBindings();
        Check.True(failures, "migration cleared nothing", cleared >= 1);
        Check.True(failures, "comp still bound to pawn", BindableUtil.BoundPawnRef(comp) == null);
        Check.Eq(failures, "comp presence after migration", comp.presenceTicks, 0);
        Check.True(failures, "gene still lists the building", !gene.BoundBuildings.Contains(pump));
        Check.True(failures, "building not linked to casket", Rig.Link(pump, rig.Caskets[0]) != null);
        pawn.Destroy(DestroyMode.Vanish);
        return failures.Count == 0 ? ScenarioStatus.Passed : ScenarioStatus.Failed;
    }
}

/// <summary>The serum is gone from traders, rewards and loot, but its def still exists.</summary>
public sealed class SerumScenario : HarnessScenario
{
    public override string Name => "serum";

    public override void Setup(Map map, CellRect area)
    {
    }

    public override ScenarioStatus Tick(Map map, int t, List<string> failures)
    {
        ThingDef serum = DefDatabase<ThingDef>.GetNamedSilentFail("DeathrestCapacitySerum");
        Check.True(failures, "serum def removed (it must stay for saves)", serum != null);
        if (serum == null)
        {
            return ScenarioStatus.Failed;
        }
        Check.True(failures, "serum still has thingSetMakerTags", serum.thingSetMakerTags.NullOrEmpty());
        Check.Eq(failures, "serum tradeability", serum.tradeability, Tradeability.Sellable);
        foreach (TraderKindDef trader in DefDatabase<TraderKindDef>.AllDefs)
        {
            foreach (StockGenerator gen in trader.stockGenerators)
            {
                if (gen is StockGenerator_SingleDef single && Traverse.Create(single).Field("thingDef").GetValue<ThingDef>() == serum)
                {
                    failures.Add("trader still stocks serum: " + trader.defName);
                }
                if (gen.HandlesThingDef(serum) && serum.tradeability.TraderCanSell())
                {
                    failures.Add("trader can still sell serum: " + trader.defName);
                }
            }
        }
        int checkedMakers = 0;
        int skipped = 0;
        foreach (ThingSetMakerDef maker in DefDatabase<ThingSetMakerDef>.AllDefs)
        {
            try
            {
                if (maker.root.AllGeneratableThingsDebug().Contains(serum))
                {
                    failures.Add("thing set maker can still generate serum: " + maker.defName);
                }
                checkedMakers++;
            }
            catch (Exception)
            {
                skipped++;
            }
        }
        Metrics.Add($"thing set makers checked: {checkedMakers}, skipped (need params): {skipped}");
        return failures.Count == 0 ? ScenarioStatus.Passed : ScenarioStatus.Failed;
    }
}

/// <summary>
/// Saves links (one disabled, one with presence), reloads the save in-session, and verifies them. The runner
/// continues with <see cref="SaveLoadVerifyScenario"/> in the reloaded game via a marker file.
/// </summary>
public sealed class SaveLoadScenario : HarnessScenario
{
    public const string SaveName = "DeathrestUnbound_HarnessSaveLoad";
    public static string MarkerPath => Path.Combine(HarnessUtil.HarnessDir, "saveload-marker.txt");

    private Rig rig;

    public override string Name => "saveload";

    public override void Setup(Map map, CellRect area)
    {
        rig = new Rig(map, area, 10, 6);
        Building_Bed casket = rig.Casket(0, 0);
        rig.Building("Hemopump", 2, 0);
        rig.Building("Hemopump", 4, 0);
        rig.Building("Hemopump", 6, 0);
        Rig.Resolve();
        Rig.T.SetEnabled(Rig.Link(rig.Buildings[1], casket), false);
        Rig.Link(rig.Buildings[2], casket).presenceTicks = 1234;
        File.WriteAllText(MarkerPath, string.Join(",", new[] { casket.thingIDNumber }.Concat(rig.Buildings.Select(b => b.thingIDNumber))));
    }

    public override ScenarioStatus Tick(Map map, int t, List<string> failures)
    {
        if (t == 5)
        {
            // Outside the tick loop (next frame, before the game updates), the same way the load menu does it.
            LongEventHandler.QueueLongEvent(() =>
            {
                GameDataSaveLoader.SaveGame(SaveName);
                GameDataSaveLoader.LoadGame(SaveName);
            }, "LoadingLongEvent", false, null);
        }
        return ScenarioStatus.Running;
    }
}

public sealed class SaveLoadVerifyScenario : HarnessScenario
{
    public override string Name => "saveload";

    public override void Setup(Map map, CellRect area)
    {
    }

    public override ScenarioStatus Tick(Map map, int t, List<string> failures)
    {
        if (t < 30)
        {
            return ScenarioStatus.Running;
        }
        int[] ids = File.ReadAllText(SaveLoadScenario.MarkerPath).Split(',').Select(int.Parse).ToArray();
        File.Delete(SaveLoadScenario.MarkerPath);
        Thing Find(int id) => map.listerThings.AllThings.FirstOrDefault(x => x.thingIDNumber == id);
        Thing casket = Find(ids[0]);
        Check.True(failures, "casket missing after load", casket != null);
        if (casket == null)
        {
            return ScenarioStatus.Failed;
        }
        Thing[] pumps = { Find(ids[1]), Find(ids[2]), Find(ids[3]) };
        Rig.Resolve();
        Check.Eq(failures, "links after load", Rig.T.LinksOfCasket(casket).Count, 3);
        Check.True(failures, "link 1 enabled after load", Rig.Link(pumps[0], casket)?.enabled == true);
        Check.True(failures, "link 2 disabled after load", Rig.Link(pumps[1], casket)?.enabled == false);
        Check.Eq(failures, "link 3 presence after load", Rig.Link(pumps[2], casket)?.presenceTicks, 1234);
        Check.AtRest(failures, pumps);
        return failures.Count == 0 ? ScenarioStatus.Passed : ScenarioStatus.Failed;
    }
}

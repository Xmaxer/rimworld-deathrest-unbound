using System.Collections.Generic;
using System.IO;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeathrestUnbound.DevHarness;

public static class HarnessUtil
{
    public static string HarnessDir => Path.Combine(GenFilePaths.SaveDataFolderPath, "DeathrestHarness");

    private static readonly string[] AllowedErrorFragments =
    {
        // Environment noise: duplicate Workshop subscriptions of unrelated mods on the dev machine.
        "Tried loading mod with the same packageId multiple times"
    };

    public static CellRect ReserveArea(Map map, int index)
    {
        int x0 = 10 + (index % 5) * 30;
        int z0 = 10 + (index / 5) * 30;
        CellRect rect = new CellRect(x0, z0, 26, 26).ClipInsideMap(map);
        FlattenCells(map, rect);
        return rect;
    }

    /// <summary>Concretes, unroofs and unfogs cells, destroying anything natural (and non-colonist) on them.</summary>
    public static void FlattenCells(Map map, IEnumerable<IntVec3> cells)
    {
        List<Thing> doomed = new List<Thing>();
        foreach (IntVec3 c in cells)
        {
            if (!c.InBounds(map))
            {
                continue;
            }
            List<Thing> things = c.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                // Non-destroyable things (steam geysers, map-feature buildings) log an error when destroyed.
                if (things[i].def.destroyable && (!(things[i] is Pawn p) || p.Faction != Faction.OfPlayer))
                {
                    doomed.Add(things[i]);
                }
            }
            map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete);
            map.roofGrid.SetRoof(c, null);
            map.fogGrid.Unfog(c);
        }
        for (int i = 0; i < doomed.Count; i++)
        {
            if (!doomed[i].Destroyed)
            {
                doomed[i].Destroy(DestroyMode.Vanish);
            }
        }
    }

    public static void Teleport(Pawn pawn, IntVec3 cell, Map map)
    {
        if (pawn.Spawned)
        {
            pawn.DeSpawn(DestroyMode.Vanish);
        }
        GenSpawn.Spawn(pawn, cell, map);
    }

    /// <summary>
    /// Test rooms have no power grid, so the power-net logic switches any CompPowerTrader back off. Every
    /// building spawned with <c>forcePower</c> is switched back on each harness tick instead.
    /// </summary>
    private static readonly List<CompPowerTrader> ForcedPower = new List<CompPowerTrader>();

    public static Thing SpawnBuilding(ThingDef def, IntVec3 cell, Map map, Rot4 rot, ThingDef stuff = null, bool forcePower = true)
    {
        Thing thing = ThingMaker.MakeThing(def, stuff ?? (def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null));
        if (def.CanHaveFaction)
        {
            thing.SetFactionDirect(Faction.OfPlayer);
        }
        Thing spawned = GenSpawn.Spawn(thing, cell, map, rot, WipeMode.Vanish);
        CompPowerTrader power = (spawned as ThingWithComps)?.GetComp<CompPowerTrader>();
        if (power != null && forcePower)
        {
            power.PowerOn = true;
            ForcedPower.Add(power);
        }
        CompRefuelable fuel = (spawned as ThingWithComps)?.GetComp<CompRefuelable>();
        fuel?.Refuel(fuel.Props.fuelCapacity);
        return spawned;
    }

    public static void Unforce(Thing thing, bool powerOn)
    {
        CompPowerTrader power = thing.TryGetComp<CompPowerTrader>();
        ForcedPower.Remove(power);
        if (power != null)
        {
            power.PowerOn = powerOn;
        }
    }

    public static void Force(Thing thing)
    {
        CompPowerTrader power = thing.TryGetComp<CompPowerTrader>();
        if (power != null && !ForcedPower.Contains(power))
        {
            ForcedPower.Add(power);
            power.PowerOn = true;
        }
    }

    public static void ClearForcedPower()
    {
        ForcedPower.Clear();
    }

    public static void ReapplyForcedPower()
    {
        for (int i = 0; i < ForcedPower.Count; i++)
        {
            CompPowerTrader power = ForcedPower[i];
            if (power?.parent?.Spawned == true)
            {
                power.PowerOn = true;
            }
        }
    }

    /// <summary>Builds walls on the border of <paramref name="outer"/>; optionally roofs it. Returns the interior.</summary>
    public static CellRect BuildSealedRoom(Map map, CellRect outer, bool roofed, ThingDef wallStuff = null)
    {
        foreach (IntVec3 c in outer.EdgeCells)
        {
            SpawnBuilding(ThingDefOf.Wall, c, map, Rot4.North, wallStuff ?? ThingDefOf.Plasteel);
        }
        if (roofed)
        {
            foreach (IntVec3 c in outer)
            {
                map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            }
        }
        return outer.ContractedBy(1);
    }

    public static Pawn SpawnColonist(IntVec3 cell, Map map, XenotypeDef xenotype)
    {
        PawnGenerationRequest request = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
            forceGenerateNewPawn: true, canGeneratePawnRelations: false, allowAddictions: false,
            developmentalStages: DevelopmentalStage.Adult);
        request.ForcedXenotype = xenotype;
        Pawn pawn = PawnGenerator.GeneratePawn(request);
        GenSpawn.Spawn(pawn, cell, map);
        return pawn;
    }

    public static void JumpCamera(IntVec3 cell)
    {
        Find.CameraDriver.JumpToCurrentMapLoc(cell);
    }

    public static void Screenshot(string name)
    {
        Directory.CreateDirectory(HarnessDir);
        EditWindow_Log log = Find.WindowStack.WindowOfType<EditWindow_Log>();
        if (log != null)
        {
            Find.WindowStack.TryRemove(log, doCloseSound: false);
        }
        ScreenCapture.CaptureScreenshot(Path.Combine(HarnessDir, name + ".png"));
    }

    public static void CloseBlockingWindows()
    {
        IList<Window> windows = Find.WindowStack.Windows;
        for (int i = windows.Count - 1; i >= 0; i--)
        {
            Window w = windows[i];
            if (w.forcePause || w.absorbInputAroundWindow)
            {
                Find.WindowStack.TryRemove(w, doCloseSound: false);
            }
        }
    }

    public static List<string> UnexpectedLoggedErrors()
    {
        List<string> result = new List<string>();
        using (Log.LockMessages())
        {
            foreach (LogMessage m in Log.Messages)
            {
                if (m.type != LogMessageType.Error)
                {
                    continue;
                }
                bool allowed = false;
                for (int i = 0; i < AllowedErrorFragments.Length; i++)
                {
                    if (m.text != null && m.text.Contains(AllowedErrorFragments[i]))
                    {
                        allowed = true;
                        break;
                    }
                }
                if (!allowed)
                {
                    result.Add(m.text);
                }
            }
        }
        return result;
    }

    public static string JsonEscape(string s)
    {
        if (s == null)
        {
            return "";
        }
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
    }
}

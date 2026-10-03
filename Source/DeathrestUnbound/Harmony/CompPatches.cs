using System.Collections.Generic;
using DeathrestUnbound.Core;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace DeathrestUnbound.Harmony;

[HarmonyPatch(typeof(CompDeathrestBindable), nameof(CompDeathrestBindable.PostSpawnSetup))]
public static class Patch_Comp_PostSpawnSetup
{
    public static void Postfix(CompDeathrestBindable __instance)
    {
        DeathrestLinkTracker.Instance?.Register(__instance);
    }
}

[HarmonyPatch(typeof(CompDeathrestBindable), nameof(CompDeathrestBindable.PostDeSpawn))]
public static class Patch_Comp_PostDeSpawn
{
    public static void Postfix(CompDeathrestBindable __instance)
    {
        DeathrestLinkTracker.Instance?.Unregister(__instance, keepLinks: __instance.parent.BeingTransportedOnGravship);
    }
}

/// <summary>
/// Buildings: run the original once per active link, in that link's pawn context (so per-pawn effects from other
/// mods fire for every served pawn), then set the scaled power draw. Caskets: set the exponential draw.
/// </summary>
[HarmonyPatch(typeof(CompDeathrestBindable), nameof(CompDeathrestBindable.CompTickRare))]
public static class Patch_Comp_CompTickRare
{
    private static bool inner;

    [HarmonyPriority(Priority.First)]
    public static bool Prefix(CompDeathrestBindable __instance)
    {
        DeathrestLinkTracker tracker = DeathrestLinkTracker.Instance;
        if (inner || tracker == null || __instance.IsCasket())
        {
            return true;
        }
        LinkLoop.ForEachActive(tracker, __instance, () =>
        {
            inner = true;
            try
            {
                __instance.CompTickRare();
            }
            finally
            {
                inner = false;
            }
        });
        CompPowerTrader power = __instance.parent.TryGetComp<CompPowerTrader>();
        if (power != null)
        {
            power.PowerOutput = -PowerMath.BuildingDraw(power.Props.PowerConsumption, power.Props.idlePowerDraw,
                tracker.ActiveCount(__instance), DeathrestUnboundMod.Settings.buildingPowerScaling);
        }
        return false;
    }

    public static void Postfix(CompDeathrestBindable __instance)
    {
        DeathrestLinkTracker tracker = DeathrestLinkTracker.Instance;
        DeathrestUnboundSettings settings = DeathrestUnboundMod.Settings;
        if (inner || tracker == null || !__instance.IsCasket() || !settings.casketPowerScaling)
        {
            return;
        }
        CompPowerTrader power = __instance.parent.TryGetComp<CompPowerTrader>();
        DeathrestLinkTracker.CasketInfo info = tracker.InfoOf(__instance.parent);
        if (power == null || info?.occupant == null)
        {
            return;
        }
        power.PowerOutput = -PowerMath.CasketDraw(power.Props.PowerConsumption, settings.casketExponent, info.wanted, true);
    }
}

/// <summary>Buildings: sound and fuel use follow the number of caskets served, not vanilla's single presence counter.</summary>
[HarmonyPatch(typeof(CompDeathrestBindable), nameof(CompDeathrestBindable.CompTick))]
public static class Patch_Comp_CompTick
{
    public static bool Prefix(CompDeathrestBindable __instance)
    {
        DeathrestLinkTracker tracker = DeathrestLinkTracker.Instance;
        if (tracker == null || __instance.IsCasket())
        {
            return true;
        }
        ThingWithComps parent = __instance.parent;
        if (parent.IsHashIntervalTick(250))
        {
            __instance.CompTickRare();
        }
        int active = tracker.ActiveCount(__instance);
        CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
        ref Sustainer sustainer = ref BindableUtil.SustainerRef(__instance);
        if (active <= 0 || (power != null && !power.PowerOn))
        {
            if (sustainer != null && !sustainer.Ended)
            {
                sustainer.End();
            }
            return false;
        }
        if (!__instance.Props.soundWorking.NullOrUndefined())
        {
            if (sustainer == null || sustainer.Ended)
            {
                sustainer = __instance.Props.soundWorking.TrySpawnSustainer(SoundInfo.InMap(parent));
            }
            sustainer.Maintain();
        }
        CompRefuelable refuelable = parent.TryGetComp<CompRefuelable>();
        if (refuelable != null)
        {
            int uses = PowerMath.FuelUsesPerTick(active, DeathrestUnboundMod.Settings.fuelScaling);
            for (int i = 0; i < uses; i++)
            {
                refuelable.Notify_UsedThisTick();
            }
        }
        return false;
    }
}

/// <summary>Buildings: draw the hose to every casket served.</summary>
[HarmonyPatch(typeof(CompDeathrestBindable), nameof(CompDeathrestBindable.PostDraw))]
public static class Patch_Comp_PostDraw
{
    private static bool inner;

    public static bool Prefix(CompDeathrestBindable __instance)
    {
        DeathrestLinkTracker tracker = DeathrestLinkTracker.Instance;
        if (inner || tracker == null || __instance.IsCasket())
        {
            return true;
        }
        LinkLoop.ForEachActive(tracker, __instance, () =>
        {
            inner = true;
            try
            {
                __instance.PostDraw();
            }
            finally
            {
                inner = false;
            }
        });
        return false;
    }
}

[HarmonyPatch(typeof(CompDeathrestBindable), nameof(CompDeathrestBindable.CompInspectStringExtra))]
public static class Patch_Comp_CompInspectStringExtra
{
    public static bool Prefix(CompDeathrestBindable __instance, ref string __result)
    {
        if (DeathrestLinkTracker.Instance == null)
        {
            return true;
        }
        __result = InspectText.For(__instance);
        return false;
    }
}

/// <summary>
/// The fuel readout's "(10 days)" is vanilla's estimate for one user. For a building serving several caskets,
/// show the real time left at its current burn (one burn per casket served).
/// </summary>
[HarmonyPatch(typeof(CompRefuelable), nameof(CompRefuelable.CompInspectStringExtra))]
public static class Patch_Refuelable_InspectString
{
    public static void Postfix(CompRefuelable __instance, ref string __result)
    {
        DeathrestLinkTracker tracker = DeathrestLinkTracker.Instance;
        CompDeathrestBindable comp = __instance.parent.Bindable();
        if (tracker == null || comp == null || comp.IsCasket() || __result == null || !__instance.HasFuel)
        {
            return;
        }
        int active = tracker.ActiveCount(comp);
        int uses = PowerMath.FuelUsesPerTick(active, DeathrestUnboundMod.Settings.fuelScaling);
        if (uses <= 0)
        {
            return;
        }
        CompProperties_Refuelable props = __instance.Props;
        // Vanilla deathrest buildings tick their fuel externally: no idle burn, one burn per use.
        bool idleBurn = !props.consumeFuelOnlyWhenUsed && !props.externalTicking;
        int ticks = PowerMath.FuelTicksLeft(__instance.Fuel, props.fuelConsumptionRate, idleBurn, uses);
        string ours = "DU_FuelLeft".Translate(ticks.ToStringTicksToPeriod(), active);
        if (!props.consumeFuelOnlyWhenUsed)
        {
            // Same formula vanilla uses for its idle-only estimate.
            string vanilla = " (" + ((int)(__instance.Fuel / props.fuelConsumptionRate * 60000f)).ToStringTicksToPeriod() + ")";
            int at = __result.IndexOf(vanilla, System.StringComparison.Ordinal);
            if (at >= 0)
            {
                __result = __result.Remove(at, vanilla.Length).Insert(at, " (" + ours + ")");
                return;
            }
        }
        __result += "\n" + props.FuelLabel + ": " + ours;
    }
}

public static class LinkLoop
{
    /// <summary>Runs <paramref name="action"/> once per active link of a building, with the comp serving that link's pawn.</summary>
    public static void ForEachActive(DeathrestLinkTracker tracker, CompDeathrestBindable comp, System.Action action)
    {
        if (tracker.ActiveCount(comp) <= 0)
        {
            return;
        }
        // Copy: the action runs third-party code that could, in principle, change links.
        List<DeathrestLink> links = new List<DeathrestLink>(tracker.LinksOfBuilding(comp.parent));
        for (int i = 0; i < links.Count; i++)
        {
            DeathrestLink link = links[i];
            if (link.status != LinkStatus.Active)
            {
                continue;
            }
            Pawn occupant = tracker.InfoOf(link.casket)?.occupant;
            if (occupant != null)
            {
                PawnContext.RunAs(link, occupant, action);
            }
        }
    }
}

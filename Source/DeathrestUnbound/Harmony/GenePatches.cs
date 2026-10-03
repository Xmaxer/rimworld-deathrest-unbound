using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DeathrestUnbound.Harmony;

/// <summary>Vanilla binding of non-casket buildings to a pawn is replaced by casket links.</summary>
[HarmonyPatch(typeof(Gene_Deathrest), nameof(Gene_Deathrest.CanBindToBindable))]
public static class Patch_Gene_CanBindToBindable
{
    public static void Postfix(CompDeathrestBindable bindComp, ref bool __result)
    {
        if (__result && bindComp != null && !bindComp.IsCasket())
        {
            __result = false;
        }
    }
}

/// <summary>The pawn's bound comps are its casket plus every active link of that casket.</summary>
[HarmonyPatch(typeof(Gene_Deathrest), nameof(Gene_Deathrest.BoundComps), MethodType.Getter)]
public static class Patch_Gene_BoundComps
{
    public static bool Prefix(Gene_Deathrest __instance, ref List<CompDeathrestBindable> __result)
    {
        DeathrestLinkTracker tracker = DeathrestLinkTracker.Instance;
        if (tracker == null)
        {
            return true;
        }
        __result = tracker.BoundCompsFor(__instance.pawn);
        return false;
    }
}

/// <summary>
/// Methods that read or write per-pawn building state (presence, bound pawn, Apply) run inside the pawn's context.
/// </summary>
[HarmonyPatch]
public static class Patch_Gene_PawnContext
{
    public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Gene_Deathrest), nameof(Gene_Deathrest.TickDeathresting));
        yield return AccessTools.PropertyGetter(typeof(Gene_Deathrest), nameof(Gene_Deathrest.DeathrestEfficiency));
        yield return AccessTools.Method(typeof(Gene_Deathrest), "ApplyDeathrestBuildingBonuses");
    }

    [HarmonyPriority(Priority.First)]
    public static void Prefix(Gene_Deathrest __instance)
    {
        PawnContext.Enter(__instance.pawn);
    }

    public static Exception Finalizer(Exception __exception, Gene_Deathrest __instance)
    {
        PawnContext.Exit(__instance.pawn);
        return __exception;
    }
}

[HarmonyPatch(typeof(Gene_Deathrest), nameof(Gene_Deathrest.Notify_DeathrestStarted))]
public static class Patch_Gene_DeathrestStarted
{
    public static void Postfix(Gene_Deathrest __instance)
    {
        DeathrestLinkTracker.Instance?.OnDeathrestStarted(__instance.pawn);
    }
}

[HarmonyPatch(typeof(Gene_Deathrest), nameof(Gene_Deathrest.Notify_DeathrestEnded))]
public static class Patch_Gene_DeathrestEnded
{
    public static void Postfix(Gene_Deathrest __instance)
    {
        DeathrestLinkTracker.Instance?.OnDeathrestEnded(__instance.pawn);
    }
}

/// <summary>The "Buildings: n / max" readout counts the pawn's casket links.</summary>
[HarmonyPatch(typeof(Gene_Deathrest), nameof(Gene_Deathrest.CurrentCapacity), MethodType.Getter)]
public static class Patch_Gene_CurrentCapacity
{
    public static void Postfix(Gene_Deathrest __instance, ref int __result)
    {
        DeathrestLinkTracker tracker = DeathrestLinkTracker.Instance;
        if (tracker != null && __instance.pawn != null)
        {
            __result = tracker.UsedCapacityFor(__instance.pawn);
        }
    }
}

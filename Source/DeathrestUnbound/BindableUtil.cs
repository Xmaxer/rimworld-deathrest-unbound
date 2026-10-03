using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace DeathrestUnbound;

/// <summary>Access to <see cref="CompDeathrestBindable"/> private state, and casket/building classification.</summary>
public static class BindableUtil
{
    public static readonly AccessTools.FieldRef<CompDeathrestBindable, Pawn> BoundPawnRef =
        AccessTools.FieldRefAccess<CompDeathrestBindable, Pawn>("boundPawn");

    public static readonly AccessTools.FieldRef<CompDeathrestBindable, Gene_Deathrest> CachedGeneRef =
        AccessTools.FieldRefAccess<CompDeathrestBindable, Gene_Deathrest>("cachedDeathrestGene");

    public static readonly AccessTools.FieldRef<CompDeathrestBindable, Sustainer> SustainerRef =
        AccessTools.FieldRefAccess<CompDeathrestBindable, Sustainer>("sustainer");

    /// <summary>A casket is the bed the pawn lies in; everything else is a linkable building.</summary>
    public static bool IsCasket(this CompDeathrestBindable comp)
    {
        return comp.Props.mustBeLayingInToBind;
    }

    public static bool IsCasketDef(ThingDef def)
    {
        CompProperties_DeathrestBindable props = def.GetCompProperties<CompProperties_DeathrestBindable>();
        return props != null && props.mustBeLayingInToBind;
    }

    public static bool IsLinkableBuildingDef(ThingDef def)
    {
        CompProperties_DeathrestBindable props = def.GetCompProperties<CompProperties_DeathrestBindable>();
        return props != null && !props.mustBeLayingInToBind;
    }

    public static CompDeathrestBindable Bindable(this Thing thing)
    {
        return (thing as ThingWithComps)?.GetComp<CompDeathrestBindable>();
    }

    public static Gene_Deathrest DeathrestGene(this Pawn pawn)
    {
        return pawn?.genes?.GetFirstGeneOfType<Gene_Deathrest>();
    }

    /// <summary>Sets a building comp's per-pawn fields (vanilla's single-pawn state) to a given pawn.</summary>
    public static void WriteState(CompDeathrestBindable comp, Pawn pawn, int presenceTicks)
    {
        BoundPawnRef(comp) = pawn;
        CachedGeneRef(comp) = null;
        comp.presenceTicks = presenceTicks;
    }
}

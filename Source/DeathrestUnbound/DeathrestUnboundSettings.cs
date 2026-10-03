using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DeathrestUnbound;

public class DeathrestUnboundSettings : ModSettings
{
    public const float DefaultExponent = 1.5f;

    /// <summary>When false, each casket is limited by its occupant's vanilla deathrest capacity.</summary>
    public bool uncapCapacity = true;

    /// <summary>When true, per-building-type stack limits apply per casket.</summary>
    public bool enforceStackLimits;

    /// <summary>defName to stack limit (0 = unlimited). Missing entries use the def's vanilla stackLimit.</summary>
    public Dictionary<string, int> stackLimitOverrides = new Dictionary<string, int>();

    public bool casketPowerScaling = true;
    public float casketExponent = DefaultExponent;
    public bool buildingPowerScaling = true;
    public bool fuelScaling = true;

    /// <summary>Read by the serum XML patches at load time, so changes need a restart.</summary>
    public bool hideSerum = true;

    public int StackLimitFor(ThingDef def)
    {
        if (stackLimitOverrides.TryGetValue(def.defName, out int limit))
        {
            return limit;
        }
        return VanillaStackLimit(def);
    }

    public static int VanillaStackLimit(ThingDef def)
    {
        return def.GetCompProperties<CompProperties_DeathrestBindable>()?.stackLimit ?? 0;
    }

    public void ResetToDefaults()
    {
        uncapCapacity = true;
        enforceStackLimits = false;
        stackLimitOverrides.Clear();
        casketPowerScaling = true;
        casketExponent = DefaultExponent;
        buildingPowerScaling = true;
        fuelScaling = true;
        hideSerum = true;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref uncapCapacity, "uncapCapacity", true);
        Scribe_Values.Look(ref enforceStackLimits, "enforceStackLimits", false);
        Scribe_Collections.Look(ref stackLimitOverrides, "stackLimitOverrides", LookMode.Value, LookMode.Value);
        Scribe_Values.Look(ref casketPowerScaling, "casketPowerScaling", true);
        Scribe_Values.Look(ref casketExponent, "casketExponent", DefaultExponent);
        Scribe_Values.Look(ref buildingPowerScaling, "buildingPowerScaling", true);
        Scribe_Values.Look(ref fuelScaling, "fuelScaling", true);
        Scribe_Values.Look(ref hideSerum, "hideSerum", true);
        if (Scribe.mode == LoadSaveMode.PostLoadInit && stackLimitOverrides == null)
        {
            stackLimitOverrides = new Dictionary<string, int>();
        }
    }
}

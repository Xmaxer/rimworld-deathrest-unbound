using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DeathrestUnbound.Harmony;

/// <summary>The deathrest gizmo's "Buildings: n / max" shows "n / ∞" while capacity is uncapped.</summary>
[HarmonyPatch(typeof(GeneGizmo_DeathrestCapacity), nameof(GeneGizmo_DeathrestCapacity.GizmoOnGUI))]
public static class Patch_DeathrestGizmo
{
    private static readonly MethodInfo Format3 =
        AccessTools.Method(typeof(string), nameof(string.Format), new[] { typeof(string), typeof(object), typeof(object), typeof(object) });

    private static readonly MethodInfo Replacement = AccessTools.Method(typeof(Patch_DeathrestGizmo), nameof(FormatBuildings));

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        bool found = false;
        foreach (CodeInstruction ins in instructions)
        {
            if (!found && ins.Calls(Format3))
            {
                found = true;
                yield return new CodeInstruction(OpCodes.Call, Replacement).MoveLabelsFrom(ins);
                continue;
            }
            yield return ins;
        }
        if (!found)
        {
            Log.Warning("[DeathrestUnbound] Deathrest gizmo layout changed; capacity still shows the vanilla maximum.");
        }
    }

    public static string FormatBuildings(string format, object label, object current, object max)
    {
        if (DeathrestUnboundMod.Settings.uncapCapacity)
        {
            max = "∞";
        }
        return string.Format(format, label, current, max);
    }
}

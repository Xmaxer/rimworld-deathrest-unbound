using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace DeathrestUnbound.DevHarness.Scenarios;

/// <summary>
/// Presses vanilla's real "DEV: Wake and apply bonuses" gizmo (which writes presence straight into the building
/// comps, outside any pawn context) for three pawns sharing a glucosoid pump and, with VRE, an Invocation Matrix.
/// Pawns in powered caskets get every bonus; the pawn in an unpowered casket gets none (its links are inactive).
/// </summary>
public sealed class DevWakeScenario : RigScenario
{
    private bool vre;
    private bool savedGodMode;

    public override string Name => "devwake";

    protected override Rig Build(Map map, CellRect area)
    {
        vre = ModsConfig.IsActive("vanillaracesexpanded.sanguophage");
        Rig r = new Rig(map, area, 14, 6);
        Building_Bed a = r.Casket(0, 0);
        Building_Bed b = r.Casket(2, 0);
        Building_Bed c = r.Casket(4, 0);
        r.Building("GlucosoidPump", 7, 0);
        if (vre)
        {
            r.Building("VRE_InvocationMatrix", 9, 0);
        }
        r.Deathrester(a);
        r.Deathrester(b);
        r.Deathrester(c);
        return r;
    }

    protected override IEnumerable<(int, Action<List<string>>)> Steps()
    {
        yield return (20, f => HarnessUtil.Unforce(rig.Caskets[2], powerOn: false));
        yield return (60, f =>
        {
            savedGodMode = DebugSettings.godMode;
            DebugSettings.godMode = true;
            try
            {
                foreach (Pawn p in rig.Pawns)
                {
                    Command_Action dev = p.DeathrestGene().GetGizmos().OfType<Command_Action>()
                        .FirstOrDefault(g => g.defaultLabel == "DEV: Wake and apply bonuses");
                    Check.True(f, "dev wake gizmo not found", dev != null);
                    dev?.action();
                }
            }
            finally
            {
                DebugSettings.godMode = savedGodMode;
            }
            HediffDef rush = DefDatabase<HediffDef>.GetNamed("GlucosoidRush");
            AbilityDef coagulate = vre ? DefDatabase<AbilityDef>.GetNamed("VRE_Coagulate_SingleUse") : null;
            for (int i = 0; i < rig.Pawns.Count; i++)
            {
                Pawn p = rig.Pawns[i];
                bool powered = i < 2;
                bool hasRush = p.health.hediffSet.HasHediff(rush);
                Check.True(f, $"{p.LabelShort} (casket {i + 1}, powered={powered}) glucosoid rush={hasRush}", hasRush == powered);
                if (vre)
                {
                    bool hasAbility = p.abilities.GetAbility(coagulate) != null;
                    Check.True(f, $"{p.LabelShort} (casket {i + 1}, powered={powered}) matrix ability={hasAbility}", hasAbility == powered);
                }
            }
            Check.AtRest(f, rig.Buildings);
        });
    }
}

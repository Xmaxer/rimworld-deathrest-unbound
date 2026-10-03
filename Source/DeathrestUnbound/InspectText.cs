using System.Collections.Generic;
using System.Text;
using DeathrestUnbound.Core;
using RimWorld;
using Verse;

namespace DeathrestUnbound;

/// <summary>Inspect strings and shared number formatting for caskets and linked buildings.</summary>
public static class InspectText
{
    public static string Watts(float watts)
    {
        return watts.ToString("#,0") + " W";
    }

    public static string For(CompDeathrestBindable comp)
    {
        return comp.IsCasket() ? ForCasket(comp) : ForBuilding(comp);
    }

    /// <summary>Casket draw if someone deathrests now with the current enabled set.</summary>
    public static float ProjectedCasketDraw(Thing casket, int wanted)
    {
        CompPowerTrader power = casket.TryGetComp<CompPowerTrader>();
        if (power == null)
        {
            return 0f;
        }
        DeathrestUnboundSettings s = DeathrestUnboundMod.Settings;
        return PowerMath.CasketDraw(power.Props.PowerConsumption, s.casketExponent, wanted, s.casketPowerScaling);
    }

    /// <summary>Wanted links now, or the would-be-wanted (idle) ones when nobody is deathresting.</summary>
    public static int WantedOrProjected(DeathrestLinkTracker.CasketInfo info)
    {
        if (info == null)
        {
            return 0;
        }
        return info.occupant != null ? info.wanted : info.idle;
    }

    private static string ForCasket(CompDeathrestBindable comp)
    {
        DeathrestLinkTracker tracker = DeathrestLinkTracker.Instance;
        StringBuilder sb = new StringBuilder();
        Pawn bound = BindableUtil.BoundPawnRef(comp);
        if (bound != null)
        {
            sb.Append(("BoundTo".Translate() + ": " + bound.NameShortColored).Resolve());
        }
        List<DeathrestLink> links = tracker.LinksOfCasket(comp.parent);
        DeathrestLinkTracker.CasketInfo info = tracker.InfoOf(comp.parent);
        int enabled = 0;
        bool unpowered = false;
        for (int i = 0; i < links.Count; i++)
        {
            if (links[i].enabled)
            {
                enabled++;
            }
            unpowered |= links[i].status == LinkStatus.CasketUnpowered;
        }
        AppendLine(sb, "DU_CasketLinks".Translate(info?.wanted ?? 0, enabled, links.Count));
        CompPowerTrader power = comp.parent.TryGetComp<CompPowerTrader>();
        DeathrestUnboundSettings s = DeathrestUnboundMod.Settings;
        if (power != null && s.casketPowerScaling)
        {
            int n = WantedOrProjected(info);
            string formula = "DU_CasketPowerFormula".Translate(Watts(power.Props.PowerConsumption), s.casketExponent.ToString("0.##"), n,
                Watts(ProjectedCasketDraw(comp.parent, n)));
            AppendLine(sb, info?.occupant != null ? "DU_CasketPower".Translate(formula) : "DU_CasketPowerProjected".Translate(formula));
        }
        if (unpowered)
        {
            AppendLine(sb, "DU_CasketUnpowered".Translate().Colorize(ColorLibrary.RedReadable));
        }
        return sb.ToString();
    }

    private static string ForBuilding(CompDeathrestBindable comp)
    {
        DeathrestLinkTracker tracker = DeathrestLinkTracker.Instance;
        List<DeathrestLink> links = tracker.LinksOfBuilding(comp.parent);
        if (links.Count == 0)
        {
            return "DU_BuildingNoLinks".Translate();
        }
        StringBuilder sb = new StringBuilder();
        int active = tracker.ActiveCount(comp);
        sb.Append("DU_BuildingLinks".Translate(links.Count, active));
        CompPowerTrader power = comp.parent.TryGetComp<CompPowerTrader>();
        if (power != null && active > 0 && DeathrestUnboundMod.Settings.buildingPowerScaling)
        {
            AppendLine(sb, "DU_BuildingPower".Translate(Watts(power.Props.PowerConsumption), active, Watts(-power.PowerOutput)));
        }
        return sb.ToString();
    }

    private static void AppendLine(StringBuilder sb, string line)
    {
        if (sb.Length > 0)
        {
            sb.AppendLine();
        }
        sb.Append(line);
    }
}

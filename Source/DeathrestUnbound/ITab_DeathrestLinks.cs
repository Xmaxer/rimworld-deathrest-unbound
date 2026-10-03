using System.Collections.Generic;
using DeathrestUnbound.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeathrestUnbound;

/// <summary>
/// "Deathrest links" tab on caskets and linkable buildings: one row per link with an enable toggle, the other end,
/// the occupant, status, presence and power. On a casket it also shows the power change of toggling each link.
/// </summary>
public class ITab_DeathrestLinks : ITab
{
    private const float RowHeight = 28f;
    private Vector2 scroll;
    private float viewHeight;

    public ITab_DeathrestLinks()
    {
        size = new Vector2(640f, 480f);
        labelKey = "DU_TabLinks";
    }

    private CompDeathrestBindable SelComp => SelThing?.Bindable();

    public override bool IsVisible => SelComp != null && DeathrestLinkTracker.Instance != null && SelThing.Faction == Faction.OfPlayer;

    protected override void FillTab()
    {
        CompDeathrestBindable comp = SelComp;
        DeathrestLinkTracker tracker = DeathrestLinkTracker.Instance;
        if (comp == null || tracker == null)
        {
            return;
        }
        tracker.EnsureResolved();
        bool isCasket = comp.IsCasket();
        List<DeathrestLink> links = isCasket ? tracker.LinksOfCasket(comp.parent) : tracker.LinksOfBuilding(comp.parent);
        Rect inner = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
        Text.Font = GameFont.Small;

        // Header: summary on the left, bulk toggles on the right.
        Rect header = new Rect(inner.x, inner.y, inner.width - 180f, 72f);
        Widgets.Label(header, InspectText.For(comp));
        if (links.Count > 0)
        {
            if (Widgets.ButtonText(new Rect(inner.xMax - 170f, inner.y, 80f, 24f), "DU_EnableAll".Translate()))
            {
                SetAll(tracker, links, true);
            }
            if (Widgets.ButtonText(new Rect(inner.xMax - 85f, inner.y, 85f, 24f), "DU_DisableAll".Translate()))
            {
                SetAll(tracker, links, false);
            }
        }

        float y = inner.y + 78f;
        if (links.Count == 0)
        {
            Widgets.Label(new Rect(inner.x, y, inner.width, 60f), (isCasket ? "DU_CasketNoLinks" : "DU_BuildingNoLinks").Translate());
            return;
        }

        Columns cols = new Columns(inner.x, inner.width);
        GUI.color = ColoredText.SubtleGrayColor;
        Widgets.Label(cols.Name(y), (isCasket ? "DU_ColBuilding" : "DU_ColCasket").Translate());
        Widgets.Label(cols.Occupant(y), "DU_ColOccupant".Translate());
        Widgets.Label(cols.Status(y), "DU_ColStatus".Translate());
        Widgets.Label(cols.Presence(y), "DU_ColPresence".Translate());
        Widgets.Label(cols.Power(y), (isCasket ? "DU_ColPowerDelta" : "DU_ColPower").Translate());
        GUI.color = Color.white;
        y += 24f;
        Widgets.DrawLineHorizontal(inner.x, y, inner.width);
        y += 4f;

        Rect outRect = new Rect(inner.x, y, inner.width, inner.yMax - y);
        Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, viewHeight);
        Widgets.BeginScrollView(outRect, ref scroll, viewRect);
        Columns rowCols = new Columns(0f, viewRect.width);
        float ry = 0f;
        for (int i = 0; i < links.Count; i++)
        {
            DrawRow(tracker, links[i], isCasket, rowCols, ry, i);
            ry += RowHeight;
        }
        viewHeight = ry;
        Widgets.EndScrollView();
    }

    private static void SetAll(DeathrestLinkTracker tracker, List<DeathrestLink> links, bool enabled)
    {
        for (int i = 0; i < links.Count; i++)
        {
            tracker.SetEnabled(links[i], enabled);
        }
    }

    private static void DrawRow(DeathrestLinkTracker tracker, DeathrestLink link, bool isCasketTab, Columns cols, float y, int index)
    {
        Rect row = new Rect(0f, y, cols.Width, RowHeight);
        if (index % 2 == 1)
        {
            Widgets.DrawLightHighlight(row);
        }
        Widgets.DrawHighlightIfMouseover(row);
        bool enabled = link.enabled;
        Widgets.Checkbox(cols.X, y + 2f, ref enabled);
        if (enabled != link.enabled)
        {
            tracker.SetEnabled(link, enabled);
        }
        TooltipHandler.TipRegion(new Rect(cols.X, y, 24f, RowHeight), "DU_ToggleTip".Translate());

        Thing other = isCasketTab ? link.building : link.casket;
        Text.Anchor = TextAnchor.MiddleLeft;
        Rect name = cols.Name(y);
        if (Widgets.ButtonText(name, other.LabelShortCap, drawBackground: false))
        {
            CameraJumper.TryJumpAndSelect(other);
        }
        TooltipHandler.TipRegion(name, "DU_JumpTip".Translate());

        DeathrestLinkTracker.CasketInfo info = tracker.InfoOf(link.casket);
        Pawn occupant = info?.occupant;
        Widgets.Label(cols.Occupant(y), occupant != null ? occupant.LabelShortCap : "-");

        GUI.color = StatusColor(link.status);
        Widgets.Label(cols.Status(y), ("DU_Status_" + link.status).Translate());
        GUI.color = Color.white;
        TooltipHandler.TipRegion(cols.Status(y), ("DU_Status_" + link.status + "_Desc").Translate());

        Gene_Deathrest gene = occupant.DeathrestGene();
        string presence = "-";
        if (gene != null && gene.deathrestTicks > 0 && link.status == LinkStatus.Active)
        {
            presence = Mathf.Clamp01((float)link.presenceTicks / gene.deathrestTicks).ToStringPercent();
        }
        Widgets.Label(cols.Presence(y), presence);
        Widgets.Label(cols.Power(y), isCasketTab ? PowerDelta(link, info) : BuildingShare(link));
        Text.Anchor = TextAnchor.UpperLeft;
    }

    /// <summary>Building tab: the watts this building spends on that casket.</summary>
    private static string BuildingShare(DeathrestLink link)
    {
        CompPowerTrader power = link.building.TryGetComp<CompPowerTrader>();
        if (power == null || link.status != LinkStatus.Active)
        {
            return "-";
        }
        return InspectText.Watts(power.Props.PowerConsumption);
    }

    /// <summary>Casket tab: how total draw (casket + building) changes if this link is toggled.</summary>
    private static string PowerDelta(DeathrestLink link, DeathrestLinkTracker.CasketInfo info)
    {
        int n = InspectText.WantedOrProjected(info);
        bool counted = LinkResolver.IsWanted(link.status) || link.status == LinkStatus.Idle;
        bool wouldCount = link.status == LinkStatus.Disabled;
        if (!counted && !wouldCount)
        {
            return "-";
        }
        CompPowerTrader buildingPower = link.building.TryGetComp<CompPowerTrader>();
        DeathrestUnboundSettings s = DeathrestUnboundMod.Settings;
        float building = buildingPower != null ? (s.buildingPowerScaling ? buildingPower.Props.PowerConsumption : 0f) : 0f;
        float before = InspectText.ProjectedCasketDraw(link.casket, n);
        float after = InspectText.ProjectedCasketDraw(link.casket, counted ? n - 1 : n + 1);
        float delta = after - before + (counted ? -building : building);
        string sign = delta >= 0f ? "+" : "-";
        return sign + InspectText.Watts(Mathf.Abs(delta));
    }

    private static Color StatusColor(LinkStatus status)
    {
        switch (status)
        {
            case LinkStatus.Active:
                return ColorLibrary.Green;
            case LinkStatus.CasketUnpowered:
                return ColorLibrary.RedReadable;
            case LinkStatus.OverCapacity:
            case LinkStatus.OverStackLimit:
                return ColorLibrary.Orange;
            case LinkStatus.Dormant:
                return ColorLibrary.Yellow;
            default:
                return ColoredText.SubtleGrayColor;
        }
    }

    private readonly struct Columns
    {
        public readonly float X;
        public readonly float Width;

        public Columns(float x, float width)
        {
            X = x;
            Width = width;
        }

        public Rect Name(float y) => new Rect(X + 30f, y, 180f, RowHeight);
        public Rect Occupant(float y) => new Rect(X + 215f, y, 110f, RowHeight);
        public Rect Status(float y) => new Rect(X + 330f, y, 110f, RowHeight);
        public Rect Presence(float y) => new Rect(X + 445f, y, 60f, RowHeight);
        public Rect Power(float y) => new Rect(X + 510f, y, Width - 510f, RowHeight);
    }
}

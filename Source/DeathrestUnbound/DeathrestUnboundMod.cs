using System.Collections.Generic;
using System.Linq;
using DeathrestUnbound.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeathrestUnbound;

public class DeathrestUnboundMod : Mod
{
    public const string HarmonyId = "Xmaxer.DeathrestUnbound";

    public static DeathrestUnboundMod Instance { get; private set; }

    /// <summary>Loaded in the constructor, before XML patching, so serum patches can read it.</summary>
    public static DeathrestUnboundSettings Settings { get; private set; }

    private Vector2 scroll;
    private float viewHeight = 900f;
    private readonly Dictionary<string, string> limitBuffers = new Dictionary<string, string>();

    public DeathrestUnboundMod(ModContentPack content) : base(content)
    {
        Instance = this;
        Settings = GetSettings<DeathrestUnboundSettings>();
        new HarmonyLib.Harmony(HarmonyId).PatchAll(typeof(DeathrestUnboundMod).Assembly);
    }

    public override string SettingsCategory()
    {
        return "Deathrest Unbound";
    }

    public override void WriteSettings()
    {
        base.WriteSettings();
        DeathrestLinkTracker.Instance?.MarkDirty();
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        DeathrestUnboundSettings s = Settings;
        Rect view = new Rect(0f, 0f, inRect.width - 16f, viewHeight);
        Widgets.BeginScrollView(inRect, ref scroll, view);
        // viewHeight is last frame's height, so a row shown this frame overflows it. Without maxOneColumn the
        // listing wraps the overflow into an off-screen column and CurHeight measures only that column, shrinking
        // the page further every frame.
        Listing_Standard l = new Listing_Standard { maxOneColumn = true };
        l.Begin(view);

        Heading(l, "DU_SettingsCapacity".Translate());
        l.CheckboxLabeled("DU_SettingUncap".Translate(), ref s.uncapCapacity, "DU_SettingUncapDesc".Translate());
        l.CheckboxLabeled("DU_SettingStackLimits".Translate(), ref s.enforceStackLimits, "DU_SettingStackLimitsDesc".Translate());
        if (s.enforceStackLimits)
        {
            StackLimitRows(l, s);
        }

        Heading(l, "DU_SettingsPower".Translate());
        l.CheckboxLabeled("DU_SettingCasketPower".Translate(), ref s.casketPowerScaling, "DU_SettingCasketPowerDesc".Translate());
        if (s.casketPowerScaling)
        {
            l.Label("DU_SettingExponent".Translate(s.casketExponent.ToString("0.00")));
            s.casketExponent = Mathf.Round(l.Slider(s.casketExponent, 1f, 3f) * 20f) / 20f;
            l.Label(ExponentPreview(s.casketExponent).Colorize(ColoredText.SubtleGrayColor));
        }
        l.CheckboxLabeled("DU_SettingBuildingPower".Translate(), ref s.buildingPowerScaling, "DU_SettingBuildingPowerDesc".Translate());
        l.CheckboxLabeled("DU_SettingFuel".Translate(), ref s.fuelScaling, "DU_SettingFuelDesc".Translate());

        Heading(l, "DU_SettingsSerum".Translate());
        l.CheckboxLabeled("DU_SettingHideSerum".Translate(), ref s.hideSerum, "DU_SettingHideSerumDesc".Translate());

        l.Gap();
        if (l.ButtonText("DU_SettingsReset".Translate()))
        {
            s.ResetToDefaults();
            limitBuffers.Clear();
        }
        viewHeight = l.CurHeight + 20f;
        l.End();
        Widgets.EndScrollView();
        DeathrestLinkTracker.Instance?.MarkDirty();
    }

    private static void Heading(Listing_Standard l, string text)
    {
        l.Gap(8f);
        Text.Font = GameFont.Medium;
        l.Label(text);
        Text.Font = GameFont.Small;
        l.GapLine(6f);
    }

    public static string ExponentPreview(float exponent)
    {
        int[] ns = { 2, 5, 8, 12 };
        return "DU_SettingExponentPreview".Translate(string.Join(", ", ns.Select(n =>
            $"{n}: {InspectText.Watts(PowerMath.CasketDraw(100f, exponent, n, true))}")));
    }

    private void StackLimitRows(Listing_Standard l, DeathrestUnboundSettings s)
    {
        l.Label("DU_SettingStackLimitsHint".Translate().Colorize(ColoredText.SubtleGrayColor));
        foreach (ThingDef def in LinkableDefs())
        {
            Rect row = l.GetRect(28f);
            int vanilla = DeathrestUnboundSettings.VanillaStackLimit(def);
            int value = s.StackLimitFor(def);
            Rect labelRect = row.LeftPart(0.5f);
            string label = def.LabelCap + " (" + def.modContentPack?.Name + ")";
            Widgets.Label(labelRect, label.Truncate(labelRect.width - 8f));
            TooltipHandler.TipRegion(labelRect, label);
            Rect field = new Rect(row.x + row.width * 0.5f, row.y, 60f, 24f);
            if (!limitBuffers.TryGetValue(def.defName, out string buffer))
            {
                buffer = value.ToString();
            }
            int edited = value;
            Widgets.TextFieldNumeric(field, ref edited, ref buffer, 0f, 99f);
            limitBuffers[def.defName] = buffer;
            if (edited != value)
            {
                s.stackLimitOverrides[def.defName] = edited;
            }
            Widgets.Label(new Rect(field.xMax + 10f, row.y, 140f, 24f),
                "DU_SettingVanillaLimit".Translate(vanilla == 0 ? "DU_Unlimited".Translate().ToString() : vanilla.ToString()));
            if (s.stackLimitOverrides.ContainsKey(def.defName) &&
                Widgets.ButtonText(new Rect(row.xMax - 80f, row.y, 80f, 24f), "DU_SettingResetOne".Translate()))
            {
                s.stackLimitOverrides.Remove(def.defName);
                limitBuffers.Remove(def.defName);
            }
        }
    }

    public static IEnumerable<ThingDef> LinkableDefs()
    {
        return DefDatabase<ThingDef>.AllDefs.Where(BindableUtil.IsLinkableBuildingDef).OrderBy(d => d.label);
    }
}

/// <summary>Adds the links tab to every casket and linkable building def, vanilla or modded.</summary>
[StaticConstructorOnStartup]
public static class DeathrestUnboundStartup
{
    static DeathrestUnboundStartup()
    {
        int tabs = 0;
        foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
        {
            if (def.GetCompProperties<CompProperties_DeathrestBindable>() == null)
            {
                continue;
            }
            if (def.inspectorTabs == null)
            {
                def.inspectorTabs = new List<System.Type>();
            }
            if (def.inspectorTabsResolved == null)
            {
                def.inspectorTabsResolved = new List<InspectTabBase>();
            }
            if (!def.inspectorTabs.Contains(typeof(ITab_DeathrestLinks)))
            {
                def.inspectorTabs.Add(typeof(ITab_DeathrestLinks));
                def.inspectorTabsResolved.Add(InspectTabManager.GetSharedInstance(typeof(ITab_DeathrestLinks)));
                tabs++;
            }
        }
        Log.Message($"[DeathrestUnbound] Initialized: deathrest links tab added to {tabs} building type(s).");
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace DeathrestUnbound.DevHarness.Scenarios;

/// <summary>
/// A copy of every public settings field (dictionaries copied by value), so scenarios can restore, compare and
/// iterate over all settings, including ones added later.
/// </summary>
public sealed class SettingsSnapshot
{
    public static readonly FieldInfo[] Fields =
        typeof(DeathrestUnboundSettings).GetFields(BindingFlags.Public | BindingFlags.Instance);

    public static IEnumerable<FieldInfo> Toggles => Fields.Where(f => f.FieldType == typeof(bool));

    private readonly Dictionary<string, object> values = new Dictionary<string, object>();

    public static DeathrestUnboundSettings S => DeathrestUnboundMod.Settings;

    public static SettingsSnapshot Of(DeathrestUnboundSettings s)
    {
        SettingsSnapshot snap = new SettingsSnapshot();
        foreach (FieldInfo f in Fields)
        {
            snap.values[f.Name] = Copy(f.GetValue(s));
        }
        return snap;
    }

    public void ApplyTo(DeathrestUnboundSettings s)
    {
        foreach (FieldInfo f in Fields)
        {
            f.SetValue(s, Copy(values[f.Name]));
        }
    }

    /// <summary>Empty when equal, otherwise "field: other -> this" for each difference.</summary>
    public string DiffFrom(SettingsSnapshot other)
    {
        List<string> diffs = new List<string>();
        foreach (FieldInfo f in Fields)
        {
            if (!Same(values[f.Name], other.values[f.Name]))
            {
                diffs.Add($"{f.Name}: {Show(other.values[f.Name])} -> {Show(values[f.Name])}");
            }
        }
        return string.Join("; ", diffs);
    }

    public override string ToString()
    {
        return string.Join(", ", Fields.Select(f => f.Name + "=" + Show(values[f.Name])));
    }

    public static void Flip(FieldInfo toggle)
    {
        toggle.SetValue(S, !(bool)toggle.GetValue(S));
    }

    /// <summary>What the settings page's Reset button does.</summary>
    public static void PressReset()
    {
        S.ResetToDefaults();
        ((IDictionary)AccessTools.Field(typeof(DeathrestUnboundMod), "limitBuffers").GetValue(DeathrestUnboundMod.Instance)).Clear();
    }

    private static object Copy(object v)
    {
        return v is Dictionary<string, int> d ? new Dictionary<string, int>(d) : v;
    }

    private static bool Same(object a, object b)
    {
        if (a is Dictionary<string, int> da && b is Dictionary<string, int> db)
        {
            return da.Count == db.Count && da.All(kv => db.TryGetValue(kv.Key, out int v) && v == kv.Value);
        }
        return Equals(a, b);
    }

    private static string Show(object v)
    {
        if (v is Dictionary<string, int> d)
        {
            return "{" + string.Join(",", d.OrderBy(kv => kv.Key).Select(kv => kv.Key + ":" + kv.Value)) + "}";
        }
        return v is float f ? f.ToString("0.###") : v?.ToString() ?? "null";
    }
}

/// <summary>
/// Settings page regression: opens the real settings dialog and, one frame at a time, flicks every setting off and
/// on (singly, rapidly, all at once), changes hidden values, edits stack limits and resets. Every frame the page
/// must stay in one column (Listing_Standard wraps overflowing rows into an off-screen column, which is how
/// re-enabling exponential power once broke the page), drawing must not change any setting, and the same settings
/// must always settle to the same page height.
/// </summary>
public sealed class SettingsUiScenario : HarnessScenario
{
    private const string ProbeId = "DeathrestUnbound.DevHarness.SettingsProbe";
    private const int Settle = 3;
    private const int MaxFailures = 30;

    private static bool probing;
    private static int columnWraps;
    private static readonly AccessTools.FieldRef<DeathrestUnboundMod, float> ViewHeight =
        AccessTools.FieldRefAccess<DeathrestUnboundMod, float>("viewHeight");

    private HarmonyLib.Harmony harmony;
    private SettingsSnapshot saved;
    private FlickDialog dialog;

    public override string Name => "settingsui";

    public override int TimeoutTicks => 60000;

    private static DeathrestUnboundSettings S => DeathrestUnboundMod.Settings;

    private sealed class Step
    {
        public string Label;
        public int Frames;
        public Action<List<string>> Act;
        public string Shot;
    }

    public static void NewColumnPrefix()
    {
        if (probing)
        {
            columnWraps++;
        }
    }

    public override void Setup(Map map, CellRect area)
    {
        saved = SettingsSnapshot.Of(S);
        SettingsSnapshot.PressReset();
        harmony = new HarmonyLib.Harmony(ProbeId);
        harmony.Patch(AccessTools.Method(typeof(Listing), nameof(Listing.NewColumn)),
            prefix: new HarmonyMethod(typeof(SettingsUiScenario), nameof(NewColumnPrefix)));
        KeepWindows = true;
        dialog = new FlickDialog(BuildSteps());
        Find.WindowStack.Add(dialog);
    }

    private static List<Step> BuildSteps()
    {
        List<Step> steps = new List<Step>();
        void Add(string label, Action<List<string>> act, int frames = Settle, string shot = null)
        {
            steps.Add(new Step { Label = label, Act = act, Frames = frames, Shot = shot });
        }

        Add("defaults", f => { });
        foreach (FieldInfo t in SettingsSnapshot.Toggles)
        {
            for (int cycle = 1; cycle <= 2; cycle++)
            {
                Add($"{t.Name} flip (cycle {cycle})", f => SettingsSnapshot.Flip(t));
                Add($"{t.Name} flip back (cycle {cycle})", f => SettingsSnapshot.Flip(t));
            }
        }

        // The reported bug: exponential power off, then on again.
        Add("exponent 3.00", f => S.casketExponent = 3f);
        Add("casketPowerScaling off", f => S.casketPowerScaling = false);
        Add("exponent 2.25 while its slider is hidden", f => S.casketExponent = 2.25f);
        Add("casketPowerScaling on again", f => S.casketPowerScaling = true, 30, "settings-exponent-reenabled");
        Add("exponent kept through off/on", f => Check.Near(f, "exponent after off/on", S.casketExponent, 2.25f, 0.001f));
        Add("exponent 1.00", f => S.casketExponent = 1f);
        Add("exponent back to default", f => S.casketExponent = DeathrestUnboundSettings.DefaultExponent);

        // Flicks on consecutive frames, before the page height can settle.
        foreach (string name in new[] { nameof(DeathrestUnboundSettings.casketPowerScaling), nameof(DeathrestUnboundSettings.enforceStackLimits) })
        {
            FieldInfo t = SettingsSnapshot.Toggles.First(x => x.Name == name);
            for (int i = 1; i <= 7; i++)
            {
                Add($"rapid {name} flip {i}", f => SettingsSnapshot.Flip(t), 1);
            }
            Add($"rapid {name} settle", f => SettingsSnapshot.Flip(t));
        }

        // Stack-limit overrides survive hiding and showing their rows.
        string def = DeathrestUnboundMod.LinkableDefs().First().defName;
        Add("enforceStackLimits on", f => S.enforceStackLimits = true);
        Add($"override {def} = 3", f => S.stackLimitOverrides[def] = 3);
        Add("enforceStackLimits off", f => S.enforceStackLimits = false);
        Add("enforceStackLimits on again", f => S.enforceStackLimits = true, 30, "settings-stacklimits-reenabled");
        Add("override kept through off/on", f =>
            Check.True(f, $"override for {def} lost after hiding the rows", S.stackLimitOverrides.TryGetValue(def, out int v) && v == 3));
        Add($"remove override {def}", f => S.stackLimitOverrides.Remove(def));

        Add("all toggles off", f =>
        {
            foreach (FieldInfo t in SettingsSnapshot.Toggles)
            {
                t.SetValue(S, false);
            }
        });
        Add("all toggles on", f =>
        {
            foreach (FieldInfo t in SettingsSnapshot.Toggles)
            {
                t.SetValue(S, true);
            }
        });

        Add("scramble then Reset", f =>
        {
            foreach (FieldInfo t in SettingsSnapshot.Toggles)
            {
                SettingsSnapshot.Flip(t);
            }
            S.casketExponent = 2.5f;
            S.stackLimitOverrides[def] = 7;
            SettingsSnapshot.PressReset();
            string diff = SettingsSnapshot.Of(S).DiffFrom(SettingsSnapshot.Of(new DeathrestUnboundSettings()));
            Check.True(f, "Reset does not match a fresh install: " + diff, diff.Length == 0);
        });
        Add("defaults again", f => { });
        return steps;
    }

    public override ScenarioStatus Tick(Map map, int ticksSinceSetup, List<string> failures)
    {
        if (dialog.Shot != null)
        {
            HarnessUtil.Screenshot(dialog.Shot);
            dialog.Shot = null;
        }
        if (!dialog.Done)
        {
            if (!Find.WindowStack.IsOpen(dialog))
            {
                failures.Add("settings window closed before the script finished");
                return ScenarioStatus.Failed;
            }
            return ScenarioStatus.Running;
        }
        Metrics.Add($"{dialog.StepCount} steps over {dialog.Frames} frames, {dialog.StatesSeen} distinct settings states");
        failures.AddRange(dialog.Failures);
        return failures.Count == 0 ? ScenarioStatus.Passed : ScenarioStatus.Failed;
    }

    public override void Cleanup(Map map)
    {
        if (dialog != null)
        {
            Find.WindowStack.TryRemove(dialog, doCloseSound: false);
        }
        KeepWindows = false;
        harmony?.UnpatchAll(ProbeId);
        SettingsSnapshot.PressReset();
        saved?.ApplyTo(S);
    }

    /// <summary>The real settings dialog, advancing the script by one step after each repainted frame.</summary>
    private sealed class FlickDialog : Dialog_ModSettings
    {
        public readonly List<string> Failures = new List<string>();
        public bool Done;
        public string Shot;
        public int Frames;

        private readonly List<Step> steps;
        private readonly Dictionary<string, float> heights = new Dictionary<string, float>();
        private int index = -1;
        private int framesLeft;
        private int frameInStep;

        public FlickDialog(List<Step> steps) : base(DeathrestUnboundMod.Instance)
        {
            this.steps = steps;
            forcePause = false;
            closeOnClickedOutside = false;
        }

        public int StepCount => steps.Count;

        public int StatesSeen => heights.Count;

        private string Where => index < 0 ? "on open" : $"step '{steps[index].Label}' frame {frameInStep}";

        private void Fail(string message)
        {
            if (Failures.Count < MaxFailures && !Failures.Contains(message))
            {
                Failures.Add(message);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            EventType type = Event.current.type;
            SettingsSnapshot before = SettingsSnapshot.Of(S);
            int wraps = columnWraps;
            probing = true;
            try
            {
                base.DoWindowContents(inRect);
            }
            finally
            {
                probing = false;
            }
            if (Done)
            {
                return;
            }
            if (columnWraps != wraps)
            {
                Fail($"{Where}: page overflowed into {columnWraps - wraps} extra column(s), rows drawn off-screen");
            }
            string diff = SettingsSnapshot.Of(S).DiffFrom(before);
            if (diff.Length > 0)
            {
                Fail($"{Where}: drawing the page changed settings: {diff}");
            }
            if (type == EventType.Repaint)
            {
                EndFrame();
            }
        }

        private void EndFrame()
        {
            Frames++;
            frameInStep++;
            if (index >= 0)
            {
                if (--framesLeft > 0)
                {
                    return;
                }
                if (steps[index].Frames >= Settle)
                {
                    CheckHeight();
                }
            }
            index++;
            if (index >= steps.Count)
            {
                Done = true;
                return;
            }
            Step step = steps[index];
            frameInStep = 0;
            framesLeft = step.Frames;
            step.Act(Failures);
            if (step.Shot != null)
            {
                Shot = step.Shot;
            }
        }

        private void CheckHeight()
        {
            float h = ViewHeight(DeathrestUnboundMod.Instance);
            string state = SettingsSnapshot.Of(S).ToString();
            if (heights.TryGetValue(state, out float first))
            {
                if (Math.Abs(first - h) > 0.5f)
                {
                    Fail($"{Where}: page height {h:0} differs from {first:0} the last time these settings were shown ({state})");
                }
            }
            else
            {
                heights[state] = h;
            }
        }
    }
}

/// <summary>
/// Settings behaviour regression on a live rig (two caskets sharing an accelerator and a hemopump, plus a hemopump
/// only casket A uses): every setting flipped both ways twice must change what it controls and nothing else, and
/// flipping it back must restore the exact baseline. Also: the exponent survives casket scaling off/on, fuel burn
/// follows fuel scaling, Reset and all-at-once flicks return to baseline, and settings survive a save/load round trip.
/// </summary>
public sealed class SettingsScenario : RigScenario
{
    private SettingsSnapshot saved;
    private Readout baseline;
    private float sharedFuel0;
    private float singleFuel0;

    public override string Name => "settings";

    private static DeathrestUnboundSettings S => DeathrestUnboundMod.Settings;

    protected override Rig Build(Map map, CellRect area)
    {
        Rig r = new Rig(map, area, 14, 6);
        Building_Bed a = r.Casket(0, 0);
        Building_Bed b = r.Casket(2, 0);
        r.Building("DeathrestAccelerator", 5, 0);
        r.Building("Hemopump", 7, 0);
        r.Building("Hemopump", 9, 0);
        r.Deathrester(a);
        r.Deathrester(b);
        return r;
    }

    public override void Setup(Map map, CellRect area)
    {
        saved = SettingsSnapshot.Of(S);
        SettingsSnapshot.PressReset();
        base.Setup(map, area);
        Rig.T.SetEnabled(Rig.Link(rig.Buildings[2], rig.Caskets[1]), false);
    }

    private sealed class Readout
    {
        public float a, b, accel, pumpShared, pumpSingle;
        public int wantedA, wantedB;

        public override string ToString()
        {
            return $"casketA {a:0.#}W (n={wantedA}), casketB {b:0.#}W (n={wantedB}), accelerator {accel:0.#}W, " +
                   $"shared pump {pumpShared:0.#}W, single pump {pumpSingle:0.#}W";
        }
    }

    private Readout Read()
    {
        Readout r = new Readout
        {
            a = Rig.Draw(rig.Caskets[0]),
            b = Rig.Draw(rig.Caskets[1]),
            accel = Rig.Draw(rig.Buildings[0]),
            pumpShared = Rig.Draw(rig.Buildings[1]),
            pumpSingle = Rig.Draw(rig.Buildings[2])
        };
        r.wantedA = Rig.T.InfoOf(rig.Caskets[0])?.wanted ?? -1;
        r.wantedB = Rig.T.InfoOf(rig.Caskets[1])?.wanted ?? -1;
        return r;
    }

    private void CheckBaseline(List<string> f, string when)
    {
        string now = Read().ToString();
        Check.True(f, $"{when}: expected baseline [{baseline}], got [{now}]", now == baseline.ToString());
    }

    private static float Fuel(Thing t)
    {
        return t.TryGetComp<CompRefuelable>().Fuel;
    }

    protected override IEnumerable<(int, Action<List<string>>)> Steps()
    {
        yield return (10, f =>
        {
            baseline = Read();
            Metrics.Add("baseline: " + baseline);
            Check.Eq(f, "casket A links", baseline.wantedA, 3);
            Check.Near(f, "casket A draw (3 buildings)", baseline.a, 337.5f);
            Check.Near(f, "accelerator draw (2 caskets)", baseline.accel, 1600f);
        });
        yield return (20, f =>
        {
            for (int cycle = 1; cycle <= 2; cycle++)
            {
                foreach (FieldInfo t in SettingsSnapshot.Toggles)
                {
                    SettingsSnapshot.Flip(t);
                    bool on = (bool)t.GetValue(S);
                    Readout r = Read();
                    string what = $"{t.Name}={on} (cycle {cycle})";
                    if (cycle == 1)
                    {
                        Metrics.Add(what + ": " + r);
                    }
                    switch (t.Name)
                    {
                        case nameof(DeathrestUnboundSettings.casketPowerScaling):
                            Check.True(f, $"{what}: caskets should draw vanilla power, got {r}", r.a <= 100.5f && r.b <= 100.5f);
                            break;
                        case nameof(DeathrestUnboundSettings.buildingPowerScaling):
                            Check.Near(f, what + ": accelerator draw", r.accel, 800f);
                            Check.Near(f, what + ": shared pump draw", r.pumpShared, 100f);
                            break;
                        case nameof(DeathrestUnboundSettings.uncapCapacity):
                        case nameof(DeathrestUnboundSettings.enforceStackLimits):
                            Check.True(f, $"{what}: casket A gained links ({r.wantedA} > {baseline.wantedA})", r.wantedA <= baseline.wantedA);
                            break;
                        default:
                            // Fuel scaling shows over time (checked below); hideSerum is read at startup only.
                            Check.True(f, $"{what}: instant readout changed: [{r}] vs [{baseline}]", r.ToString() == baseline.ToString());
                            break;
                    }
                    SettingsSnapshot.Flip(t);
                    CheckBaseline(f, $"{t.Name} flipped back (cycle {cycle})");
                }
            }
        });
        yield return (30, f =>
        {
            S.casketPowerScaling = false;
            S.casketExponent = 2f;
            Read();
            S.casketPowerScaling = true;
            Check.Near(f, "casket A draw with exponent 2 set while scaling was off", Read().a, 100f * 8f);
            S.casketExponent = DeathrestUnboundSettings.DefaultExponent;
            CheckBaseline(f, "exponent restored");
        });
        yield return (40, f =>
        {
            foreach (FieldInfo t in SettingsSnapshot.Toggles)
            {
                t.SetValue(S, !(bool)t.GetValue(S));
            }
            Read();
            SettingsSnapshot.PressReset();
            CheckBaseline(f, "every toggle flipped at once, then Reset");
        });
        yield return (50, f =>
        {
            foreach (FieldInfo t in SettingsSnapshot.Toggles)
            {
                SettingsSnapshot.Flip(t);
            }
            S.casketExponent = 2.35f;
            S.stackLimitOverrides["Hemopump"] = 3;
            SettingsSnapshot written = SettingsSnapshot.Of(S);
            DeathrestUnboundSettings loaded = RoundTrip(S);
            string diff = SettingsSnapshot.Of(loaded).DiffFrom(written);
            Check.True(f, "settings changed across save/load: " + diff, diff.Length == 0);
            SettingsSnapshot.PressReset();
            CheckBaseline(f, "after round trip and Reset");
        });
        yield return (60, f =>
        {
            S.fuelScaling = false;
            sharedFuel0 = Fuel(rig.Buildings[1]);
            singleFuel0 = Fuel(rig.Buildings[2]);
        });
        yield return (660, f =>
        {
            float shared = sharedFuel0 - Fuel(rig.Buildings[1]);
            float single = singleFuel0 - Fuel(rig.Buildings[2]);
            Metrics.Add($"fuel scaling off, 600 ticks: shared {shared:0.######}, single {single:0.######}");
            Check.True(f, "single pump burned no fuel", single > 0f);
            if (single > 0f)
            {
                Check.Near(f, "fuel ratio shared/single with fuel scaling off", shared / single, 1f, 0.1f);
            }
            S.fuelScaling = true;
            sharedFuel0 = Fuel(rig.Buildings[1]);
            singleFuel0 = Fuel(rig.Buildings[2]);
        });
        yield return (1260, f =>
        {
            float shared = sharedFuel0 - Fuel(rig.Buildings[1]);
            float single = singleFuel0 - Fuel(rig.Buildings[2]);
            Metrics.Add($"fuel scaling back on, 600 ticks: shared {shared:0.######}, single {single:0.######}");
            if (single > 0f)
            {
                Check.Near(f, "fuel ratio shared/single with fuel scaling back on", shared / single, 2f, 0.1f);
            }
            CheckBaseline(f, "end");
        });
    }

    /// <summary>Writes and reads the settings the way LoadedModManager does for the real config file.</summary>
    private static DeathrestUnboundSettings RoundTrip(DeathrestUnboundSettings settings)
    {
        string path = Path.Combine(HarnessUtil.HarnessDir, "settings-roundtrip.xml");
        ModSettings toWrite = settings;
        Scribe.saver.InitSaving(path, "SettingsBlock");
        try
        {
            Scribe_Deep.Look(ref toWrite, "ModSettings");
        }
        finally
        {
            Scribe.saver.FinalizeSaving();
        }
        DeathrestUnboundSettings loaded = null;
        Scribe.loader.InitLoading(path);
        try
        {
            Scribe_Deep.Look(ref loaded, "ModSettings");
        }
        finally
        {
            Scribe.loader.FinalizeLoading();
        }
        return loaded;
    }

    public override void Cleanup(Map map)
    {
        base.Cleanup(map);
        SettingsSnapshot.PressReset();
        saved?.ApplyTo(S);
    }
}

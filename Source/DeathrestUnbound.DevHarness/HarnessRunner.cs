using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Verse;

namespace DeathrestUnbound.DevHarness;

/// <summary>
/// Reads scenario names from DeathrestHarness/request.txt in the save-data folder and runs them in order, one
/// 24x24 map area each, writing results.jsonl and done.txt, then quits. Inert without a request file.
/// </summary>
public sealed class HarnessRunner : GameComponent
{
    private static readonly Dictionary<string, Func<HarnessScenario>> Registry =
        new Dictionary<string, Func<HarnessScenario>>(StringComparer.OrdinalIgnoreCase)
        {
            ["boot"] = () => new Scenarios.BootScenario(),
            ["uncap"] = () => new Scenarios.UncapScenario(),
            ["shared"] = () => new Scenarios.SharedBuildingScenario(),
            ["bonuses"] = () => new Scenarios.BonusesScenario(),
            ["devwake"] = () => new Scenarios.DevWakeScenario(),
            ["limits"] = () => new Scenarios.LimitsScenario(),
            ["rooms"] = () => new Scenarios.RoomsScenario(),
            ["unpowered"] = () => new Scenarios.CasketUnpoweredScenario(),
            ["migration"] = () => new Scenarios.MigrationScenario(),
            ["serum"] = () => new Scenarios.SerumScenario(),
            ["ui"] = () => new Scenarios.UiScenario(),
            ["vre"] = () => new Scenarios.VreScenario(),
            ["perf"] = () => new Scenarios.PerfScenario(),
            ["saveload"] = () => new Scenarios.SaveLoadScenario(),
            ["playground"] = () => new Scenarios.PlaygroundScenario(),
            ["playground-check"] = () => new Scenarios.PlaygroundCheckScenario(),
        };

    /// <summary>
    /// What "all" expands to: every automated test, in a sensible order (not the playground). "saveload" reloads the
    /// game, so it must be last: the reloaded game only runs its verification half.
    /// </summary>
    private static readonly string[] All =
        { "boot", "serum", "uncap", "shared", "bonuses", "devwake", "limits", "rooms", "unpowered", "migration", "ui", "vre", "perf", "playground-check", "saveload" };

    private readonly List<HarnessScenario> queue = new List<HarnessScenario>();
    private readonly List<string> failures = new List<string>();
    private HarnessScenario current;
    private bool active;
    private bool finished;
    private int setupTick;
    private int areaIndex;
    private int warmupTicks = 180;

    public HarnessRunner(Game game)
    {
    }

    public override void FinalizeInit()
    {
        HarnessUtil.ClearForcedPower();
        if (File.Exists(Scenarios.SaveLoadScenario.MarkerPath))
        {
            // Second half of the save/load round trip: this game was just loaded by SaveLoadScenario.
            queue.Add(new Scenarios.SaveLoadVerifyScenario());
            active = true;
            warmupTicks = 60;
            Log.Message("[DeathrestHarness] reloaded game: verifying saved links");
            return;
        }
        string request = Path.Combine(HarnessUtil.HarnessDir, "request.txt");
        if (!File.Exists(request))
        {
            return;
        }
        foreach (string raw in File.ReadAllLines(request))
        {
            string name = raw.Trim();
            if (name.Length == 0)
            {
                continue;
            }
            if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string n in All)
                {
                    queue.Add(Registry[n]());
                }
            }
            else if (Registry.TryGetValue(name, out Func<HarnessScenario> factory))
            {
                queue.Add(factory());
            }
            else
            {
                WriteLine($"{{\"scenario\":\"{HarnessUtil.JsonEscape(name)}\",\"status\":\"Failed\",\"failures\":[\"unknown scenario\"]}}");
            }
        }
        active = true;
        Log.Message($"[DeathrestHarness] active with {queue.Count} scenario(s)");
    }

    /// <summary>Persistent (interactive) scenarios only run on a new game, never on a loaded save.</summary>
    public override void LoadedGame()
    {
        if (queue.Count == 1 && queue[0] is Scenarios.SaveLoadVerifyScenario)
        {
            return;
        }
        int before = queue.Count;
        queue.RemoveAll(s => s.IsPersistent);
        if (before > 0 && queue.Count == 0)
        {
            active = false;
            Log.Message("[DeathrestHarness] loaded save: skipping persistent scenario(s); harness inactive");
        }
    }

    public override void GameComponentUpdate()
    {
        if (!active || finished)
        {
            return;
        }
        if (current == null || !current.KeepWindows)
        {
            HarnessUtil.CloseBlockingWindows();
        }
        if (Find.TickManager.CurTimeSpeed != TimeSpeed.Ultrafast)
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
        }
    }

    public override void GameComponentTick()
    {
        if (!active || finished)
        {
            return;
        }
        Map map = Find.CurrentMap;
        if (map == null)
        {
            return;
        }
        HarnessUtil.ReapplyForcedPower();
        if (warmupTicks > 0)
        {
            warmupTicks--;
            return;
        }
        if (current == null)
        {
            if (queue.Count == 0)
            {
                Finish();
                return;
            }
            current = queue[0];
            queue.RemoveAt(0);
            failures.Clear();
            try
            {
                current.Setup(map, HarnessUtil.ReserveArea(map, areaIndex++));
            }
            catch (Exception e)
            {
                failures.Add("Setup threw: " + e);
                Record(ScenarioStatus.Failed, 0);
                current = null;
                return;
            }
            if (current.IsPersistent)
            {
                HandOffToPlayer();
                return;
            }
            setupTick = Find.TickManager.TicksGame;
            return;
        }

        int elapsed = Find.TickManager.TicksGame - setupTick;
        ScenarioStatus status;
        try
        {
            status = current.Tick(map, elapsed, failures);
        }
        catch (Exception e)
        {
            failures.Add("Tick threw: " + e);
            status = ScenarioStatus.Failed;
        }
        if (status == ScenarioStatus.Running && elapsed >= current.TimeoutTicks)
        {
            failures.Add($"Timed out after {elapsed} ticks");
            status = ScenarioStatus.Failed;
        }
        if (status != ScenarioStatus.Running)
        {
            try
            {
                current.Cleanup(map);
            }
            catch (Exception e)
            {
                failures.Add("Cleanup threw: " + e);
                status = ScenarioStatus.Failed;
            }
            Record(status, elapsed);
            current = null;
        }
    }

    private void HandOffToPlayer()
    {
        Log.Message($"[DeathrestHarness] {current.Name}: persistent setup complete, control handed back to the player");
        current = null;
        queue.Clear();
        active = false;
        HarnessUtil.ClearForcedPower();
        Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
    }

    private void Record(ScenarioStatus status, int ticks)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("{\"scenario\":\"").Append(HarnessUtil.JsonEscape(current?.Name ?? "?")).Append("\",");
        sb.Append("\"status\":\"").Append(status).Append("\",\"ticks\":").Append(ticks).Append(",\"failures\":[");
        AppendStrings(sb, failures);
        sb.Append("],\"metrics\":[");
        AppendStrings(sb, current?.Metrics ?? new List<string>());
        sb.Append("]}");
        WriteLine(sb.ToString());
        Log.Message($"[DeathrestHarness] {current?.Name}: {status} after {ticks} ticks");
    }

    private static void AppendStrings(StringBuilder sb, List<string> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append('"').Append(HarnessUtil.JsonEscape(items[i])).Append('"');
        }
    }

    private void Finish()
    {
        finished = true;
        List<string> errors = HarnessUtil.UnexpectedLoggedErrors();
        StringBuilder sb = new StringBuilder("{\"summary\":true,\"unexpectedErrors\":[");
        AppendStrings(sb, errors);
        sb.Append("]}");
        WriteLine(sb.ToString());
        File.WriteAllText(Path.Combine(HarnessUtil.HarnessDir, "done.txt"), "done");
        Root.Shutdown();
    }

    private static void WriteLine(string line)
    {
        Directory.CreateDirectory(HarnessUtil.HarnessDir);
        File.AppendAllText(Path.Combine(HarnessUtil.HarnessDir, "results.jsonl"), line + "\n");
    }
}

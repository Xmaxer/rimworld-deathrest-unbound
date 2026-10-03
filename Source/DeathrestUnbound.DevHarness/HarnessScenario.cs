using System.Collections.Generic;
using Verse;

namespace DeathrestUnbound.DevHarness;

public enum ScenarioStatus
{
    Running,
    Passed,
    Failed
}

public abstract class HarnessScenario
{
    public abstract string Name { get; }

    public virtual int TimeoutTicks => 5000;

    /// <summary>
    /// True for interactive setups (not tests): after a successful Setup the runner hands control back to the
    /// player, never ticks the scenario, records no result and does not shut the game down.
    /// </summary>
    public virtual bool IsPersistent => false;

    /// <summary>True while the scenario shows its own windows (settings dialog): the runner stops closing them.</summary>
    public bool KeepWindows;

    /// <summary>Name/value measurements written to results.jsonl (not failures).</summary>
    public readonly List<string> Metrics = new List<string>();

    /// <summary>Called once. <paramref name="area"/> is a cleared, unroofed, unfogged 24x24 rectangle.</summary>
    public abstract void Setup(Map map, CellRect area);

    /// <summary>Called every game tick after Setup. Add readable messages to <paramref name="failures"/>.</summary>
    public abstract ScenarioStatus Tick(Map map, int ticksSinceSetup, List<string> failures);

    /// <summary>Called once after the scenario finishes (pass or fail): restore settings, despawn pawns.</summary>
    public virtual void Cleanup(Map map)
    {
    }
}

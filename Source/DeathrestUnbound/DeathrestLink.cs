using DeathrestUnbound.Core;
using RimWorld;
using Verse;

namespace DeathrestUnbound;

/// <summary>A deathrest building serving one casket. Saved; status is recomputed by the tracker.</summary>
public class DeathrestLink : IExposable
{
    public Thing building;
    public Thing casket;
    public bool enabled = true;

    /// <summary>Ticks this building has worked for the casket's occupant during the current deathrest.</summary>
    public int presenceTicks;

    /// <summary>Creation order; earlier links win limited slots.</summary>
    public int id;

    [Unsaved(false)]
    public LinkStatus status = LinkStatus.Idle;

    public CompDeathrestBindable BuildingComp => building.Bindable();

    public CompDeathrestBindable CasketComp => casket.Bindable();

    public bool IsValid => building != null && casket != null && !building.Destroyed && !casket.Destroyed;

    public void ExposeData()
    {
        Scribe_References.Look(ref building, "building");
        Scribe_References.Look(ref casket, "casket");
        Scribe_Values.Look(ref enabled, "enabled", true);
        Scribe_Values.Look(ref presenceTicks, "presenceTicks", 0);
        Scribe_Values.Look(ref id, "id", 0);
    }
}

using System.Collections.Generic;
using DeathrestUnbound.Core;
using RimWorld;
using Verse;

namespace DeathrestUnbound;

/// <summary>
/// Owns every building-to-casket link: creates them for buildings sharing a room with a casket, saves the player's
/// enable choices and per-link presence, and resolves which links are active. Statuses are recomputed every
/// <see cref="ResolveInterval"/> ticks or immediately after anything marks the tracker dirty.
/// </summary>
public class DeathrestLinkTracker : GameComponent
{
    public const int ResolveInterval = 60;
    public const int RescanInterval = 250;

    public static DeathrestLinkTracker Instance { get; private set; }

    private List<DeathrestLink> links = new List<DeathrestLink>();
    private int nextId;

    // Unsaved indexes, rebuilt from links.
    private readonly Dictionary<Thing, List<DeathrestLink>> byBuilding = new Dictionary<Thing, List<DeathrestLink>>();
    private readonly Dictionary<Thing, List<DeathrestLink>> byCasket = new Dictionary<Thing, List<DeathrestLink>>();
    private readonly HashSet<long> pairs = new HashSet<long>();

    /// <summary>Spawned bindable comps (caskets and buildings) on any map.</summary>
    private readonly HashSet<CompDeathrestBindable> registered = new HashSet<CompDeathrestBindable>();

    // Resolve results.
    private readonly Dictionary<Thing, CasketInfo> casketInfos = new Dictionary<Thing, CasketInfo>();
    private readonly Dictionary<CompDeathrestBindable, int> activeCounts = new Dictionary<CompDeathrestBindable, int>();
    private readonly Dictionary<Pawn, BoundCache> boundCache = new Dictionary<Pawn, BoundCache>();
    private bool dirty = true;
    private bool rescanDirty = true;
    private int version;

    private static readonly List<DeathrestLink> NoLinks = new List<DeathrestLink>();
    private static readonly List<CompDeathrestBindable> NoComps = new List<CompDeathrestBindable>();

    public sealed class CasketInfo
    {
        /// <summary>The pawn deathresting in the casket, if any.</summary>
        public Pawn occupant;
        /// <summary>Links making the casket draw power (Active + CasketUnpowered).</summary>
        public int wanted;
        /// <summary>Links that would become wanted once someone deathrests (Idle).</summary>
        public int idle;
        public readonly List<DeathrestLink> active = new List<DeathrestLink>();
    }

    private sealed class BoundCache
    {
        public int version;
        public Building_Bed bed;
        public List<CompDeathrestBindable> comps;
    }

    public DeathrestLinkTracker(Game game)
    {
        Instance = this;
    }

    public IReadOnlyList<DeathrestLink> AllLinks => links;

    public static DeathrestUnboundSettings Settings => DeathrestUnboundMod.Settings;

    // ---------------------------------------------------------------- registration

    public void Register(CompDeathrestBindable comp)
    {
        if (registered.Add(comp))
        {
            rescanDirty = true;
            dirty = true;
        }
    }

    /// <summary>A bindable left the map. Links are kept only for gravship transport.</summary>
    public void Unregister(CompDeathrestBindable comp, bool keepLinks)
    {
        registered.Remove(comp);
        if (!keepLinks)
        {
            RemoveLinksOf(comp.parent);
        }
        dirty = true;
    }

    public void MarkDirty()
    {
        dirty = true;
    }

    public void SetEnabled(DeathrestLink link, bool enabled)
    {
        if (link.enabled != enabled)
        {
            link.enabled = enabled;
            dirty = true;
        }
    }

    // ---------------------------------------------------------------- queries

    public List<DeathrestLink> LinksOfBuilding(Thing building)
    {
        return building != null && byBuilding.TryGetValue(building, out List<DeathrestLink> list) ? list : NoLinks;
    }

    public List<DeathrestLink> LinksOfCasket(Thing casket)
    {
        return casket != null && byCasket.TryGetValue(casket, out List<DeathrestLink> list) ? list : NoLinks;
    }

    public CasketInfo InfoOf(Thing casket)
    {
        EnsureResolved();
        return casket != null && casketInfos.TryGetValue(casket, out CasketInfo info) ? info : null;
    }

    /// <summary>Caskets this building currently works for.</summary>
    public int ActiveCount(CompDeathrestBindable comp)
    {
        EnsureResolved();
        return activeCounts.TryGetValue(comp, out int n) ? n : 0;
    }

    /// <summary>Links that make the casket draw power.</summary>
    public int WantedCount(Thing casket)
    {
        return InfoOf(casket)?.wanted ?? 0;
    }

    public static Building_Bed CasketOf(Pawn pawn)
    {
        Building_Bed bed = pawn?.CurrentBed();
        CompDeathrestBindable comp = bed?.Bindable();
        return comp != null && comp.IsCasket() ? bed : null;
    }

    /// <summary>Active links serving this pawn right now (the pawn must be deathresting in a casket).</summary>
    public List<DeathrestLink> ActiveLinksFor(Pawn pawn)
    {
        Building_Bed casket = CasketOf(pawn);
        if (casket == null)
        {
            return NoLinks;
        }
        CasketInfo info = InfoOf(casket);
        return info != null && info.occupant == pawn ? info.active : NoLinks;
    }

    /// <summary>Replacement for <see cref="Gene_Deathrest.BoundComps"/>: the casket plus every active linked building.</summary>
    public List<CompDeathrestBindable> BoundCompsFor(Pawn pawn)
    {
        Building_Bed casket = CasketOf(pawn);
        if (casket == null)
        {
            return NoComps;
        }
        EnsureResolved();
        if (boundCache.TryGetValue(pawn, out BoundCache cache) && cache.version == version && cache.bed == casket)
        {
            return cache.comps;
        }
        List<CompDeathrestBindable> comps = new List<CompDeathrestBindable>();
        CompDeathrestBindable casketComp = casket.Bindable();
        if (BindableUtil.BoundPawnRef(casketComp) == pawn)
        {
            comps.Add(casketComp);
        }
        List<DeathrestLink> active = ActiveLinksFor(pawn);
        for (int i = 0; i < active.Count; i++)
        {
            comps.Add(active[i].BuildingComp);
        }
        boundCache[pawn] = new BoundCache { version = version, bed = casket, comps = comps };
        return comps;
    }

    /// <summary>Buildings counted against the pawn's (assigned or current) casket for the capacity readout.</summary>
    public int UsedCapacityFor(Pawn pawn)
    {
        Thing casket = (Thing)pawn.ownership?.AssignedDeathrestCasket ?? CasketOf(pawn);
        if (casket == null)
        {
            return 0;
        }
        EnsureResolved();
        int n = 0;
        List<DeathrestLink> list = LinksOfCasket(casket);
        for (int i = 0; i < list.Count; i++)
        {
            LinkStatus s = list[i].status;
            if ((s == LinkStatus.Active || s == LinkStatus.CasketUnpowered || s == LinkStatus.Idle) &&
                list[i].BuildingComp.Props.countsTowardsBuildingLimit)
            {
                n++;
            }
        }
        return n;
    }

    // ---------------------------------------------------------------- deathrest lifecycle

    public void OnDeathrestStarted(Pawn pawn)
    {
        Building_Bed casket = CasketOf(pawn);
        rescanDirty = true;
        dirty = true;
        if (casket != null)
        {
            ResetPresence(casket);
        }
        EnsureResolved();
    }

    public void OnDeathrestEnded(Pawn pawn)
    {
        Thing casket = CasketOf(pawn);
        if (casket == null)
        {
            foreach (KeyValuePair<Thing, CasketInfo> kv in casketInfos)
            {
                if (kv.Value.occupant == pawn)
                {
                    casket = kv.Key;
                    break;
                }
            }
        }
        if (casket != null)
        {
            ResetPresence(casket);
        }
        dirty = true;
    }

    private void ResetPresence(Thing casket)
    {
        List<DeathrestLink> list = LinksOfCasket(casket);
        for (int i = 0; i < list.Count; i++)
        {
            list[i].presenceTicks = 0;
        }
    }

    // ---------------------------------------------------------------- ticking and resolving

    public override void GameComponentTick()
    {
        int tick = Find.TickManager.TicksGame;
        if (tick % RescanInterval == 0)
        {
            rescanDirty = true;
        }
        if (rescanDirty || dirty || tick % ResolveInterval == 0)
        {
            dirty = true;
            EnsureResolved();
        }
    }

    public void EnsureResolved()
    {
        if (rescanDirty)
        {
            rescanDirty = false;
            Rescan();
            dirty = true;
        }
        if (dirty)
        {
            dirty = false;
            Resolve();
        }
    }

    /// <summary>Creates a link for every building sharing a room with a casket that has none yet. Never re-enables a link.</summary>
    private void Rescan()
    {
        List<CompDeathrestBindable> caskets = new List<CompDeathrestBindable>();
        List<CompDeathrestBindable> buildings = new List<CompDeathrestBindable>();
        foreach (CompDeathrestBindable comp in registered)
        {
            if (comp.parent == null || !comp.parent.Spawned)
            {
                continue;
            }
            (comp.IsCasket() ? caskets : buildings).Add(comp);
        }
        if (caskets.Count == 0 || buildings.Count == 0)
        {
            return;
        }
        // Deterministic link ids regardless of HashSet order.
        caskets.SortBy(c => c.parent.thingIDNumber);
        buildings.SortBy(b => b.parent.thingIDNumber);
        for (int c = 0; c < caskets.Count; c++)
        {
            Thing casket = caskets[c].parent;
            Room room = casket.GetRoom();
            if (room == null)
            {
                continue;
            }
            for (int b = 0; b < buildings.Count; b++)
            {
                Thing building = buildings[b].parent;
                if (building.Map != casket.Map || building.Faction != casket.Faction || pairs.Contains(PairKey(building, casket)))
                {
                    continue;
                }
                if (building.GetRoom() == room)
                {
                    AddLink(building, casket);
                }
            }
        }
    }

    private void Resolve()
    {
        version++;
        PruneInvalid();
        casketInfos.Clear();
        activeCounts.Clear();
        List<LinkCandidate> candidates = new List<LinkCandidate>();
        DeathrestUnboundSettings settings = Settings;
        foreach (KeyValuePair<Thing, List<DeathrestLink>> kv in byCasket)
        {
            Thing casket = kv.Key;
            List<DeathrestLink> list = kv.Value;
            Pawn occupant = DeathrestingOccupant(casket);
            CompPowerTrader power = casket.TryGetComp<CompPowerTrader>();
            CasketState state = new CasketState
            {
                HasDeathrestingOccupant = occupant != null,
                CasketPowered = power == null || power.PowerOn,
                EnforceCapacity = !settings.uncapCapacity,
                Capacity = CapacityFor(casket, occupant),
                EnforceStackLimits = settings.enforceStackLimits
            };
            candidates.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                DeathrestLink link = list[i];
                candidates.Add(new LinkCandidate
                {
                    DefKey = link.building.def.defName,
                    CountsTowardsCapacity = link.BuildingComp.Props.countsTowardsBuildingLimit,
                    Enabled = link.enabled,
                    Reachable = Reachable(link, occupant),
                    StackLimit = settings.StackLimitFor(link.building.def)
                });
            }
            LinkStatus[] statuses = LinkResolver.Resolve(candidates, state);
            CasketInfo info = new CasketInfo { occupant = occupant };
            for (int i = 0; i < list.Count; i++)
            {
                DeathrestLink link = list[i];
                link.status = statuses[i];
                if (LinkResolver.IsWanted(statuses[i]))
                {
                    info.wanted++;
                }
                else if (statuses[i] == LinkStatus.Idle)
                {
                    info.idle++;
                }
                if (statuses[i] == LinkStatus.Active)
                {
                    info.active.Add(link);
                    CompDeathrestBindable comp = link.BuildingComp;
                    activeCounts.TryGetValue(comp, out int n);
                    activeCounts[comp] = n + 1;
                }
            }
            casketInfos[casket] = info;
        }
    }

    public static Pawn DeathrestingOccupant(Thing casket)
    {
        if (!(casket is Building_Bed bed) || !bed.Spawned)
        {
            return null;
        }
        foreach (Pawn p in bed.CurOccupants)
        {
            if (p.Deathresting && p.DeathrestGene() != null)
            {
                return p;
            }
        }
        return null;
    }

    private static int CapacityFor(Thing casket, Pawn occupant)
    {
        Pawn pawn = occupant ?? BindableUtil.BoundPawnRef(casket.Bindable());
        Gene_Deathrest gene = pawn.DeathrestGene();
        return gene?.DeathrestCapacity ?? -1;
    }

    /// <summary>Same rules as vanilla's CanUseBindableNow: same map and room, line of sight from the sleeper.</summary>
    private static bool Reachable(DeathrestLink link, Pawn occupant)
    {
        Thing building = link.building;
        Thing casket = link.casket;
        if (!building.Spawned || !casket.Spawned || building.Map != casket.Map)
        {
            return false;
        }
        Room room = casket.GetRoom();
        if (room == null || building.GetRoom() != room)
        {
            return false;
        }
        IntVec3 from = occupant != null && occupant.Spawned ? occupant.Position : casket.Position;
        return GenSight.LineOfSight(from, building.OccupiedRect().CenterCell, casket.Map);
    }

    // ---------------------------------------------------------------- link bookkeeping

    private static long PairKey(Thing building, Thing casket)
    {
        return ((long)building.thingIDNumber << 32) | (uint)casket.thingIDNumber;
    }

    public DeathrestLink AddLink(Thing building, Thing casket)
    {
        DeathrestLink link = new DeathrestLink { building = building, casket = casket, id = nextId++ };
        links.Add(link);
        Index(link);
        dirty = true;
        return link;
    }

    private void Index(DeathrestLink link)
    {
        if (!byBuilding.TryGetValue(link.building, out List<DeathrestLink> b))
        {
            byBuilding[link.building] = b = new List<DeathrestLink>();
        }
        b.Add(link);
        if (!byCasket.TryGetValue(link.casket, out List<DeathrestLink> c))
        {
            byCasket[link.casket] = c = new List<DeathrestLink>();
        }
        c.Add(link);
        pairs.Add(PairKey(link.building, link.casket));
    }

    private void RebuildIndex()
    {
        byBuilding.Clear();
        byCasket.Clear();
        pairs.Clear();
        links.SortBy(l => l.id);
        for (int i = 0; i < links.Count; i++)
        {
            Index(links[i]);
        }
    }

    private void RemoveLinksOf(Thing thing)
    {
        if (links.RemoveAll(l => l.building == thing || l.casket == thing) > 0)
        {
            RebuildIndex();
        }
    }

    private void PruneInvalid()
    {
        if (links.RemoveAll(l => !l.IsValid || l.BuildingComp == null || l.CasketComp == null) > 0)
        {
            RebuildIndex();
        }
    }

    // ---------------------------------------------------------------- load and migration

    public override void FinalizeInit()
    {
        Instance = this;
        registered.Clear();
        foreach (Map map in Find.Maps)
        {
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                CompDeathrestBindable comp = thing.Bindable();
                if (comp != null)
                {
                    registered.Add(comp);
                }
            }
        }
        MigrateVanillaBindings();
        RebuildIndex();
        rescanDirty = true;
        dirty = true;
    }

    /// <summary>
    /// Clears vanilla single-pawn bindings from linkable buildings (old saves, or anything that slipped through) so
    /// links fully own them. Returns how many bindings were cleared.
    /// </summary>
    public int MigrateVanillaBindings()
    {
        int cleared = 0;
        foreach (CompDeathrestBindable comp in registered)
        {
            if (comp.IsCasket())
            {
                continue;
            }
            Pawn bound = BindableUtil.BoundPawnRef(comp);
            if (bound != null || comp.presenceTicks != 0)
            {
                bound.DeathrestGene()?.Notify_BoundBuildingDeSpawned(comp.parent);
                BindableUtil.WriteState(comp, null, 0);
                cleared++;
            }
        }
        foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
        {
            Gene_Deathrest gene = pawn.DeathrestGene();
            if (gene == null)
            {
                continue;
            }
            List<Thing> bound = gene.BoundBuildings;
            for (int i = bound.Count - 1; i >= 0; i--)
            {
                CompDeathrestBindable comp = bound[i]?.Bindable();
                if (comp == null || !comp.IsCasket())
                {
                    gene.Notify_BoundBuildingDeSpawned(bound[i]);
                    cleared++;
                }
            }
        }
        if (cleared > 0)
        {
            Log.Message($"[DeathrestUnbound] Converted {cleared} vanilla deathrest building binding(s) to casket links.");
        }
        return cleared;
    }

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref links, "links", LookMode.Deep);
        Scribe_Values.Look(ref nextId, "nextId", 0);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            if (links == null)
            {
                links = new List<DeathrestLink>();
            }
            links.RemoveAll(l => l == null || l.building == null || l.casket == null);
            RebuildIndex();
        }
    }
}

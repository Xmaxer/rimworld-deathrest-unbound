using System.Collections.Generic;

namespace DeathrestUnbound.Core;

public enum LinkStatus
{
    /// <summary>In use: the occupant gets this building's effects.</summary>
    Active,
    /// <summary>Wanted, but the casket has no power, so nothing linked to it works.</summary>
    CasketUnpowered,
    /// <summary>Would be used, but nobody is deathresting in the casket.</summary>
    Idle,
    /// <summary>Switched off by the player.</summary>
    Disabled,
    /// <summary>The building and casket no longer share a room, or have no line of sight.</summary>
    Dormant,
    /// <summary>The occupant's deathrest capacity is full (only when capacity is enforced).</summary>
    OverCapacity,
    /// <summary>Too many of this building type on the casket (only when stack limits are enforced).</summary>
    OverStackLimit
}

/// <summary>One building's link to a casket, as seen by the resolver.</summary>
public struct LinkCandidate
{
    /// <summary>Groups buildings for stack limits (the building's def name).</summary>
    public string DefKey;
    public bool CountsTowardsCapacity;
    public bool Enabled;
    public bool Reachable;
    /// <summary>Per-casket limit for this def; 0 or less means unlimited.</summary>
    public int StackLimit;
}

public struct CasketState
{
    public bool HasDeathrestingOccupant;
    public bool CasketPowered;
    public bool EnforceCapacity;
    /// <summary>Occupant deathrest capacity; negative means unknown (not enforced).</summary>
    public int Capacity;
    public bool EnforceStackLimits;
}

/// <summary>Resolves every link of one casket, in link creation order (earlier links win limited slots).</summary>
public static class LinkResolver
{
    public static LinkStatus[] Resolve(IReadOnlyList<LinkCandidate> links, CasketState casket)
    {
        LinkStatus[] result = new LinkStatus[links.Count];
        Dictionary<string, int> perDef = new Dictionary<string, int>();
        int capacityUsed = 0;
        for (int i = 0; i < links.Count; i++)
        {
            LinkCandidate link = links[i];
            string key = link.DefKey ?? "";
            if (!link.Enabled)
            {
                result[i] = LinkStatus.Disabled;
                continue;
            }
            if (!link.Reachable)
            {
                result[i] = LinkStatus.Dormant;
                continue;
            }
            bool stackLimited = casket.EnforceStackLimits && link.StackLimit > 0;
            perDef.TryGetValue(key, out int usedOfDef);
            if (stackLimited && usedOfDef >= link.StackLimit)
            {
                result[i] = LinkStatus.OverStackLimit;
                continue;
            }
            if (casket.EnforceCapacity && casket.Capacity >= 0 && link.CountsTowardsCapacity && capacityUsed >= casket.Capacity)
            {
                result[i] = LinkStatus.OverCapacity;
                continue;
            }
            perDef[key] = usedOfDef + 1;
            if (link.CountsTowardsCapacity)
            {
                capacityUsed++;
            }
            if (!casket.HasDeathrestingOccupant)
            {
                result[i] = LinkStatus.Idle;
            }
            else
            {
                result[i] = casket.CasketPowered ? LinkStatus.Active : LinkStatus.CasketUnpowered;
            }
        }
        return result;
    }

    /// <summary>Links that make the casket draw power: active ones, plus ones blocked only by the casket's own power.</summary>
    public static bool IsWanted(LinkStatus status)
    {
        return status == LinkStatus.Active || status == LinkStatus.CasketUnpowered;
    }

    public static int CountWanted(LinkStatus[] statuses)
    {
        int n = 0;
        for (int i = 0; i < statuses.Length; i++)
        {
            if (IsWanted(statuses[i]))
            {
                n++;
            }
        }
        return n;
    }
}

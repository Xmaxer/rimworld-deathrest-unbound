using System.Collections.Generic;
using DeathrestUnbound.Core;
using NUnit.Framework;

namespace DeathrestUnbound.Core.Tests;

public class LinkResolverTests
{
    private static LinkCandidate L(string def, bool enabled = true, bool reachable = true, int stack = 0, bool counts = true)
    {
        return new LinkCandidate { DefKey = def, Enabled = enabled, Reachable = reachable, StackLimit = stack, CountsTowardsCapacity = counts };
    }

    private static readonly CasketState Running = new CasketState { HasDeathrestingOccupant = true, CasketPowered = true, Capacity = 1 };

    [Test]
    public void Uncapped_AllEnabledReachableLinksAreActive()
    {
        List<LinkCandidate> links = new List<LinkCandidate> { L("A"), L("A"), L("B"), L("C"), L("C"), L("C") };
        LinkStatus[] s = LinkResolver.Resolve(links, Running);
        Assert.That(s, Has.All.EqualTo(LinkStatus.Active));
        Assert.That(LinkResolver.CountWanted(s), Is.EqualTo(6));
    }

    [Test]
    public void DisabledAndDormantTakePrecedence()
    {
        LinkStatus[] s = LinkResolver.Resolve(new List<LinkCandidate> { L("A", enabled: false, reachable: false), L("A", reachable: false), L("A") }, Running);
        Assert.That(s, Is.EqualTo(new[] { LinkStatus.Disabled, LinkStatus.Dormant, LinkStatus.Active }));
    }

    [Test]
    public void StackLimits_OnlyWhenEnforced_EarliestWin()
    {
        List<LinkCandidate> links = new List<LinkCandidate> { L("G", stack: 2), L("G", stack: 2), L("G", stack: 2), L("H", stack: 2) };
        Assert.That(LinkResolver.Resolve(links, Running), Has.All.EqualTo(LinkStatus.Active));
        CasketState enforced = Running;
        enforced.EnforceStackLimits = true;
        Assert.That(LinkResolver.Resolve(links, enforced),
            Is.EqualTo(new[] { LinkStatus.Active, LinkStatus.Active, LinkStatus.OverStackLimit, LinkStatus.Active }));
    }

    [Test]
    public void DisabledLinksDoNotUseLimitedSlots()
    {
        CasketState enforced = Running;
        enforced.EnforceStackLimits = true;
        LinkStatus[] s = LinkResolver.Resolve(new List<LinkCandidate> { L("G", enabled: false, stack: 1), L("G", stack: 1) }, enforced);
        Assert.That(s, Is.EqualTo(new[] { LinkStatus.Disabled, LinkStatus.Active }));
    }

    [Test]
    public void Capacity_OnlyWhenEnforced_IgnoresNonCountingLinks()
    {
        CasketState cap = Running;
        cap.EnforceCapacity = true;
        cap.Capacity = 2;
        LinkStatus[] s = LinkResolver.Resolve(new List<LinkCandidate> { L("A"), L("B", counts: false), L("C"), L("D") }, cap);
        Assert.That(s, Is.EqualTo(new[] { LinkStatus.Active, LinkStatus.Active, LinkStatus.Active, LinkStatus.OverCapacity }));
    }

    [Test]
    public void OverCapacityLinksDoNotUseStackSlots()
    {
        CasketState both = Running;
        both.EnforceCapacity = true;
        both.Capacity = 1;
        both.EnforceStackLimits = true;
        LinkStatus[] s = LinkResolver.Resolve(new List<LinkCandidate> { L("A", stack: 1), L("B", stack: 1), L("B", counts: false, stack: 1) }, both);
        Assert.That(s, Is.EqualTo(new[] { LinkStatus.Active, LinkStatus.OverCapacity, LinkStatus.Active }));
    }

    [Test]
    public void UnknownCapacityIsNotEnforced()
    {
        CasketState cap = Running;
        cap.EnforceCapacity = true;
        cap.Capacity = -1;
        Assert.That(LinkResolver.Resolve(new List<LinkCandidate> { L("A"), L("B") }, cap), Has.All.EqualTo(LinkStatus.Active));
    }

    [Test]
    public void NoOccupant_IsIdle_UnpoweredCasket_IsStillWanted()
    {
        List<LinkCandidate> links = new List<LinkCandidate> { L("A"), L("B") };
        Assert.That(LinkResolver.Resolve(links, new CasketState { CasketPowered = true }), Has.All.EqualTo(LinkStatus.Idle));
        LinkStatus[] s = LinkResolver.Resolve(links, new CasketState { HasDeathrestingOccupant = true, CasketPowered = false });
        Assert.That(s, Has.All.EqualTo(LinkStatus.CasketUnpowered));
        Assert.That(LinkResolver.CountWanted(s), Is.EqualTo(2));
    }
}

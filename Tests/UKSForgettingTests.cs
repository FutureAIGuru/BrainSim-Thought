using System;
using System.Linq;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public class UKSForgettingTests
{
    private static UKS.UKS CreateUKS()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateMinimumStructureForTests();
        UKS.UKS.theUKS = uks;
        return uks;
    }

    [Fact]
    public void Thought_expires_after_TimeToLive()
    {
        var uks = CreateUKS();
        Thought temp = uks.GetOrAddThought("temp-ttl");
        temp.TimeToLive = TimeSpan.FromMilliseconds(10);
        temp.LastFiredTime = DateTime.Now - TimeSpan.FromMilliseconds(20);

        // Trigger expiration check
        _ = temp.LinksTo;

        Assert.DoesNotContain(temp, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought("temp-ttl"));
    }

    [Fact]
    public void Link_expires_after_TimeToLive_and_is_removed()
    {
        var uks = CreateUKS();
        Thought source = uks.GetOrAddThought("source");
        Thought target = uks.GetOrAddThought("target");
        uks.GetOrAddThought("rel");
        Link link = source.AddLink("rel", target);
        link.TimeToLive = TimeSpan.FromMilliseconds(10);
        link.LastFiredTime = DateTime.Now - TimeSpan.FromMilliseconds(20);

        // Sanity
        Assert.Contains(link, source.LinksTo);

        // Trigger expiration check on the link itself
        _ = link.LinksTo;

        Assert.DoesNotContain(link, source.LinksTo);
        Assert.DoesNotContain(link, target.LinksFrom);
    }

    [Fact]
    public void Expired_is_a_link_reverts_child_to_unknown_parent()
    {
        var uks = CreateUKS();
        Thought fido = uks.GetOrAddThought("Fido");
        Thought dog = uks.GetOrAddThought("dog");
        Link isa = fido.AddParent(dog);

        isa.TimeToLive = TimeSpan.FromMilliseconds(10);
        isa.LastFiredTime = DateTime.Now - TimeSpan.FromMilliseconds(20);

        // Sanity: dog is a parent alongside Unknown
        Assert.Contains(dog, fido.Parents);

        // Expire the is-a link
        var x = isa.LinksTo;

        Thought unknown = ThoughtLabels.GetThought("Unknown");
        Assert.NotNull(unknown);
        Assert.DoesNotContain(dog, fido.Parents);
        Assert.Contains(unknown, fido.Parents);
        Assert.Equal(1, fido.Parents.Count(p => p == unknown));
    }

    [Fact]
    public void TimeToLive_increases_with_repeated_fire_for_thought_and_link()
    {
        var uks = CreateUKS();

        // Thought TTL growth
        Thought t = uks.GetOrAddThought("ttl-grow");
        t.TimeToLive = TimeSpan.FromSeconds(10);
        var ttl1 = t.TimeToLive;
        t.Fire(); // UseCount = 1 => +20s
        var ttl2 = t.TimeToLive;
        t.Fire(); // UseCount = 2 => +40s
        var ttl3 = t.TimeToLive;

        Assert.True(ttl2 > ttl1, "Thought TTL should increase after first fire");
        Assert.True(ttl3 > ttl2, "Thought TTL should increase after second fire");

        // Link TTL growth
        Thought src = uks.GetOrAddThought("src-grow");
        Thought dst = uks.GetOrAddThought("dst-grow");
        uks.GetOrAddThought("rel-grow");
        Link link = src.AddLink("rel-grow", dst);
        link.TimeToLive = TimeSpan.FromSeconds(5);
        var lttl1 = link.TimeToLive;
        link.Fire(); // UseCount = 1 => +20s
        var lttl2 = link.TimeToLive;
        link.Fire(); // UseCount = 2 => +40s
        var lttl3 = link.TimeToLive;

        Assert.True(lttl2 > lttl1, "Link TTL should increase after first fire");
        Assert.True(lttl3 > lttl2, "Link TTL should increase after second fire");
    }

    [Fact]
    public void Expiring_owner_forgets_sequence_but_preserves_elements()
    {
        var uks = CreateUKS();
        Thought word = uks.GetOrAddThought("seqword");
        Thought spelled = uks.GetOrAddThought("spelled", "LinkType");
        Thought lA = uks.GetOrAddThought("A", "symbol");
        Thought lB = uks.GetOrAddThought("B", "symbol");

        var seqStart = uks.AddSequence(word, spelled, new() { lA, lB });
        string seqLabel = $"{word.Label.ToLower()}-seq0";
        Assert.Same(seqStart, ThoughtLabels.GetThought(seqLabel));

        // Force TTL expiration on the owner
        word.TimeToLive = TimeSpan.FromMilliseconds(10);
        word.LastFiredTime = DateTime.Now - TimeSpan.FromMilliseconds(20);

        // Trigger expiration check
        _ = word.LinksTo;

        // Owner and its sequence are gone
        Assert.DoesNotContain(word, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought(word.Label));
        Assert.DoesNotContain(seqStart, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought(seqLabel));

        // Element values remain
        Assert.Contains(lA, uks.AtomicThoughts);
        Assert.Contains(lB, uks.AtomicThoughts);
    }
}
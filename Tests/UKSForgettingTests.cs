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
        temp.TimeToLive = 2;

        uks.AdvanceCycle();
        Assert.Contains(temp, uks.AtomicThoughts);
        uks.AdvanceCycle();

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
        // Sanity
        Assert.Contains(link, source.LinksTo);

        link.TimeToLive = 1;

        uks.AdvanceCycle();

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

        isa.TimeToLive = 1;

        // Sanity: dog is a parent alongside Unknown
        Assert.Contains(dog, fido.Parents);

        uks.AdvanceCycle();

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
        t.TimeToLive = 10 * UKS.UKS.CyclesPerSecond;
        var ttl1 = t.TimeToLive;
        t.Fire(); // fixed +20s
        var ttl2 = t.TimeToLive;
        t.Fire(); // another fixed +20s
        var ttl3 = t.TimeToLive;

        Assert.True(ttl2 > ttl1, "Thought TTL should increase after first fire");
        Assert.True(ttl3 > ttl2, "Thought TTL should increase after second fire");
        Assert.Equal(20 * UKS.UKS.CyclesPerSecond, ttl2 - ttl1);
        Assert.Equal(20 * UKS.UKS.CyclesPerSecond, ttl3 - ttl2);

        // Link TTL growth
        Thought src = uks.GetOrAddThought("src-grow");
        Thought dst = uks.GetOrAddThought("dst-grow");
        uks.GetOrAddThought("rel-grow");
        Link link = src.AddLink("rel-grow", dst);
        link.TimeToLive = 5 * UKS.UKS.CyclesPerSecond;
        var lttl1 = link.TimeToLive;
        link.Fire(); // fixed +20s
        var lttl2 = link.TimeToLive;
        link.Fire(); // another fixed +20s
        var lttl3 = link.TimeToLive;

        Assert.True(lttl2 > lttl1, "Link TTL should increase after first fire");
        Assert.True(lttl3 > lttl2, "Link TTL should increase after second fire");
        Assert.Equal(20 * UKS.UKS.CyclesPerSecond, lttl2 - lttl1);
        Assert.Equal(20 * UKS.UKS.CyclesPerSecond, lttl3 - lttl2);
    }

    [Fact]
    public void Expiring_owner_forgets_sequence_but_preserves_elements()
    {
        var uks = CreateUKS();
        Thought word = uks.GetOrAddThought("seqword");
        Thought spelled = uks.GetOrAddThought("spelled", "LinkType");
        Thought lA = uks.GetOrAddThought("A", "symbol");
        Thought lB = uks.GetOrAddThought("B", "symbol");

        var seqStart = uks.AddSequenceAndLink(word, spelled, new() { lA, lB });
        string seqLabel = $"{word.Label.ToLower()}-seq0";
        Assert.Same(seqStart, ThoughtLabels.GetThought(seqLabel));

        // Force TTL expiration on the owner
        word.TimeToLive = 1;

        uks.AdvanceCycle();

        // Owner and its sequence are gone
        Assert.DoesNotContain(word, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought(word.Label));
        Assert.DoesNotContain(seqStart, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought(seqLabel));

        // Element values remain
        Assert.Contains(lA, uks.AtomicThoughts);
        Assert.Contains(lB, uks.AtomicThoughts);
    }

    [Fact]
    public void Forever_is_the_default_and_never_counts_down()
    {
        var uks = CreateUKS();
        Thought permanent = uks.GetOrAddThought("permanent");
        Assert.Equal(Thought.Forever, permanent.TimeToLive);
        permanent.Fire();
        for (int i = 0; i < 10; i++) uks.AdvanceCycle();
        Assert.Equal(Thought.Forever, permanent.TimeToLive);
        Assert.Contains(permanent, uks.AtomicThoughts);
    }

    [Fact]
    public void Nothing_expires_while_the_cycle_does_not_advance()
    {
        var uks = CreateUKS();
        Thought temp = uks.GetOrAddThought("paused");
        temp.TimeToLive = 1;
        System.Threading.Thread.Sleep(20);
        Assert.Contains(temp, uks.AtomicThoughts);
        Assert.Equal(1, temp.TimeToLive);
    }

    [Fact]
    public void Firing_records_the_cycle()
    {
        var uks = CreateUKS();
        Thought t = uks.GetOrAddThought("fired");
        t.LastFiredCycle = 0;
        Assert.False(t.FiredWithin(10)); // 0 means never fired
        uks.AdvanceCycle();
        t.Fire();
        Assert.Equal(uks.Cycle, t.LastFiredCycle);
        Assert.True(t.FiredWithin(1));
        uks.AdvanceCycle();
        Assert.False(t.FiredWithin(1));
    }

    [Fact]
    public void Clock_runs_at_its_rate_and_stops()
    {
        var uks = CreateUKS();
        long start = uks.Cycle;
        uks.StartClock();
        System.Threading.Thread.Sleep(500);
        uks.StopClock();
        long ran = uks.Cycle - start;
        Assert.InRange(ran, 35, 65); // 50 expected; a coarse timer must not slow it down
        System.Threading.Thread.Sleep(50);
        long stopped = uks.Cycle;
        System.Threading.Thread.Sleep(100);
        Assert.Equal(stopped, uks.Cycle);
    }

    [Fact]
    public void Transaction_knows_which_Thoughts_it_created()
    {
        var uks = CreateUKS();
        Thought existing = uks.GetOrAddThought("dog");
        Thought created = null;
        uks.ChangeUndoably("add", () =>
        {
            created = uks.GetOrAddThought("Spot");
            Assert.True(uks.CurrentTransaction.Created(created));
            Assert.False(uks.CurrentTransaction.Created(existing));
        });
        Assert.Null(uks.CurrentTransaction);
    }

    [Fact]
    public void Undoing_a_deletion_resumes_the_countdown()
    {
        var uks = CreateUKS();
        Thought temp = uks.GetOrAddThought("undone");
        temp.TimeToLive = 5;
        uks.ChangeUndoably("delete", temp.Delete);
        uks.UndoLast();
        Assert.Contains(temp, uks.AtomicThoughts);
        for (int i = 0; i < 5; i++) uks.AdvanceCycle();
        Assert.DoesNotContain(temp, uks.AtomicThoughts);
    }
}

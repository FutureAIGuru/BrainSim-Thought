using System;
using System.Collections.Generic;
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public partial class ModuleSequenceSegmenterTests
{
    [Fact]
    public void InputQueue_AppendsForwardAndReadsNewObservationsImmediately()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought a = uks.GetOrAddThought("queue:a");
        Thought b = uks.GetOrAddThought("queue:b");
        Thought c = uks.GetOrAddThought("queue:c");
        SeqElement head = module.InputQueueHead;
        module.QueueInput(new[] { a });
        Assert.Equal(new[] { module.InputPause, a }, ReadQueue(module));
        module.QueueInput(new[] { b, c });
        Assert.Same(head, module.InputQueueHead);
        Assert.Equal(new[] { b, c }, ReadQueue(module));
        Assert.Equal(new[] { module.InputPause, a, b, c }, uks.FlattenSequence(head));
        for (SeqElement element = head; element is not null; element = element.NXT)
            Assert.Null(element.FRST);
        Assert.Null(module.CurrentPointer);
    }

    [Fact]
    public void InputQueue_UsesSingleCallerSuppliedPauses()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought a = uks.GetOrAddThought("queue:a");
        Thought b = uks.GetOrAddThought("queue:b");
        Thought pause = module.InputPause;
        module.QueueInput(new[] { a, pause });
        Assert.Equal(new[] { pause, a, pause }, uks.FlattenSequence(module.InputQueueHead));
        Assert.Equal(new[] { pause, a, pause }, ReadQueue(module));
        module.QueueInput(new[] { b, pause });
        Assert.Equal(new[] { b, pause }, ReadQueue(module));
        module.QueueInput(new[] { pause });
        Assert.Equal(new[] { pause }, ReadQueue(module));
    }

    [Fact]
    public void InputQueue_TrimmingIsExplicitAndKeepsUnexpiredEntries()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.InputQueueLifetime = TimeSpan.FromSeconds(5);
        Thought a = uks.GetOrAddThought("queue:a");
        module.QueueInput(new[] { a, a, a });
        SeqElement next = module.InputQueueHead.NXT;
        var before = uks.FlattenSequence(module.InputQueueHead);
        module.TrimInputQueue(DateTime.Now);
        Assert.Same(next, module.InputQueueHead.NXT);
        Assert.Equal(before, uks.FlattenSequence(module.InputQueueHead));
    }

    [Fact]
    public void InputQueue_ExpirationPopsTheHeadAndStartsFreshWhenEmpty()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought a = uks.GetOrAddThought("queue:a");
        Thought b = uks.GetOrAddThought("queue:b");
        module.QueueInput(new[] { a, b });
        SeqElement oldHead = module.InputQueueHead;
        SeqElement expired = oldHead.NXT;
        module.TrimInputQueue(DateTime.Now.AddSeconds(6));
        Assert.NotSame(oldHead, module.InputQueueHead);
        Assert.DoesNotContain(oldHead, uks.AtomicThoughts);
        Assert.DoesNotContain(expired, uks.AtomicThoughts);
        Assert.Null(module.InputQueueHead.FRST);
        Assert.Null(module.InputQueueHead.NXT);
        Assert.Equal(new[] { module.InputPause }, ReadQueue(module));
        Assert.Same(a, uks.Labeled(a.Label));
        Assert.Same(b, uks.Labeled(b.Label));
        Assert.Null(module.CurrentPointer);
        module.QueueInput(new[] { a });
        Assert.Equal(new[] { a }, ReadQueue(module));
    }

    [Fact]
    public void InputQueue_TrimmingRemovesExpiredPrefixAndKeepsRecentSuffix()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought a = uks.GetOrAddThought("queue:a");
        Thought b = uks.GetOrAddThought("queue:b");
        module.QueueInput(new[] { a, a });
        SeqElement oldHead = module.InputQueueHead;
        SeqElement oldTail = uks.GetLastlement(oldHead);
        DateTime cutoff = DateTime.Now.AddTicks(1);
        System.Threading.Thread.Sleep(15);
        module.QueueInput(new[] { b, b });
        SeqElement recent = oldTail.NXT;
        module.TrimInputQueue(cutoff + module.InputQueueLifetime);
        Assert.Same(recent, module.InputQueueHead);
        Assert.DoesNotContain(oldHead, uks.AtomicThoughts);
        Assert.DoesNotContain(oldTail, uks.AtomicThoughts);
        Assert.Equal(new[] { b, b }, uks.FlattenSequence(module.InputQueueHead));
        Assert.Equal(new[] { b, b }, ReadQueue(module));
        Assert.Single(recent.LinksFrom, link => link.LinkType.Label == "hasInputQueue");
    }

    [Fact]
    public void InputQueue_InstancesRetainIndependentHeadsPointersAndLifetimes()
    {
        var uks = CreateUKS();
        var sound = CreateInitializedSegmenter(uks);
        var sight = CreateInitializedSegmenter(uks);
        sound.InputQueueLifetime = TimeSpan.FromSeconds(1);
        sight.InputQueueLifetime = TimeSpan.FromSeconds(10);
        Thought a = uks.GetOrAddThought("queue:a");
        sound.QueueInput(new[] { a, a });
        sight.QueueInput(new[] { a, sight.InputPause });
        SeqElement sightPointer = sight.CurrentPointer;
        sound.TrimInputQueue(DateTime.Now.AddSeconds(2));
        Assert.Equal(new[] { sound.InputPause }, ReadQueue(sound));
        Assert.Null(sound.CurrentPointer);
        Assert.Same(sightPointer, sight.CurrentPointer);
        Assert.Equal(new[] { sight.InputPause, a, sight.InputPause }, ReadQueue(sight));
    }

    [Fact]
    public void InputQueue_OccurrenceSearchCountsPositionsAndPreservesOrdering()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought c = uks.GetOrAddThought("queue:c");
        Thought a = uks.GetOrAddThought("queue:a");
        Thought t = uks.GetOrAddThought("queue:t");
        var pattern = new[] { c, a, t };
        module.QueueInput(WithPause(module, pattern));
        Assert.Single(module.FindInputOccurrences(pattern));
        module.QueueInput(pattern);
        Assert.Equal(2, module.FindInputOccurrences(pattern).Count);
        Assert.Empty(module.FindInputOccurrences(new[] { t, a, c }));
        Assert.Empty(module.FindInputOccurrences(new[] { t, c }));
    }

    [Fact]
    public void InputQueue_OccurrenceSearchReturnsDistinctPositionsIncludingOverlaps()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought a = uks.GetOrAddThought("queue:a");
        module.QueueInput(new[] { a, a, a });
        Assert.Equal(2, module.FindInputOccurrences(new[] { a, a }).Count);
        module.QueueInput(new[] { a });
        Assert.Equal(3, module.FindInputOccurrences(new[] { a, a }).Count);
    }

    [Fact]
    public void InputQueue_TrimmingAndClearingPreserveSequenceValuedObservations()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought a = uks.GetOrAddThought("queue:a");
        SeqElement value = uks.CreateFirstElement("queue-value", a);
        module.QueueInput(new Thought[] { value, a, a });
        module.TrimInputQueue(DateTime.Now.AddSeconds(6));
        Assert.Same(a, value.VLU);
        Assert.Same(value, uks.Labeled(value.Label));
        module.QueueInput(new Thought[] { value });
        module.ClearInputQueue();
        Assert.Same(a, value.VLU);
        Assert.Same(value, uks.Labeled(value.Label));
    }

    private static List<Thought> ReadQueue(ModuleSequenceSegmenter module)
    {
        List<Thought> result = new();
        while (module.TryReadQueuedInput(out Thought observation)) result.Add(observation);
        return result;
    }
}

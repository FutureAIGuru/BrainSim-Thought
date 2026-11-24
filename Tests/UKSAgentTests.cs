using System;
using System.Collections.Generic;
using System.Threading;
using UKS;
using Xunit;

namespace UKS.Tests;

public class UKSAgentTests
{
    [Fact]
    public void RunPeriodicallyRunsTheAgentUntilStopped()
    {
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        using var ran = new ManualResetEventSlim();
        int runs = 0;
        List<Thought> Agent() { Interlocked.Increment(ref runs); ran.Set(); return new List<Thought>(); }

        SynchronizationContext previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            uks.RunPeriodically(Agent, TimeSpan.FromMilliseconds(20));
            Assert.True(ran.Wait(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            uks.RunPeriodically(Agent, null);
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Thread.Sleep(100); // let any callback already in flight finish
        int stoppedAt = Volatile.Read(ref runs);
        Thread.Sleep(200);
        Assert.Equal(stoppedAt, Volatile.Read(ref runs));
    }
}

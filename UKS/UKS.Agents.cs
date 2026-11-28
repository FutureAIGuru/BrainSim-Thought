/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License. You may use, copy, modify, merge, publish, distribute,
 * sublicense, and/or sell copies of this software under the terms of
 * the MIT License.
 *
 * See the LICENSE file in the project root for full license information.
 */
using System.Diagnostics;

namespace UKS;

// Running the store over time: the cycle count and time-to-live expiry (AdvanceCycle), the clock that
// advances it, and RunPeriodically, which schedules agents (processes that reorganize the store, such
// as those in namespace UKS.Agents).
public partial class UKS
{
    private readonly Dictionary<Func<IReadOnlyList<Thought>>, Timer> agentTimers = new();
    private readonly object agentLock = new();

    /// <summary>
    /// Runs an agent every interval, e.g. RunPeriodically(uks.BubbleAttributes, TimeSpan.FromSeconds(10)).
    /// A null interval stops it. If called from a thread with a synchronization context (a UI thread),
    /// the agent runs on that thread; otherwise on a pool thread, never overlapping another agent run.
    /// </summary>
    public void RunPeriodically(Func<IReadOnlyList<Thought>> agent, TimeSpan? interval)
    {
        lock (agentLock)
        {
            if (agentTimers.Remove(agent, out Timer? old))
                old.Dispose();
            if (interval is not TimeSpan period) return;

            SynchronizationContext? context = SynchronizationContext.Current;
            void Run()
            {
                lock (agentLock) agent();
            }
            agentTimers[agent] = new Timer(_ =>
            {
                if (context is null) Run();
                else context.Post(_ => Run(), null);
            }, null, period, period);
        }
    }

    /// <summary>
    /// The UKS has no clock: time is counted in cycles, advanced by <see cref="AdvanceCycle"/>.
    /// While nothing advances it (the host paused, a debugger stopped), nothing ages or expires.
    /// Starts at 1, so a <see cref="Thought.LastFiredCycle"/> of 0 means never fired.
    /// </summary>
    public long Cycle { get; private set; } = 1;

    /// <summary>The clock's rate (<see cref="StartClock"/>): one cycle every 10 ms.</summary>
    public const int CyclesPerSecond = 100;

    // Cancels the running clock thread, if any.
    private CancellationTokenSource? clock;

    /// <summary>
    /// Starts a background thread that calls <see cref="AdvanceCycle"/> <see cref="CyclesPerSecond"/> times a second.
    /// It keeps to the rate however coarse the system timer is, but after a stall (a debugger break)
    /// it skips the lost time instead of aging everything at once.
    /// </summary>
    public void StartClock()
    {
        lock (agentLock)
        {
            if (clock is not null) return;
            clock = new CancellationTokenSource();
            CancellationToken stop = clock.Token;
            new Thread(() => RunClock(stop)) { IsBackground = true, Name = "UKS clock" }.Start();
        }
    }

    /// <summary>Stops the clock: nothing ages or expires until it is started again.</summary>
    public void StopClock()
    {
        lock (agentLock)
        {
            clock?.Cancel();
            clock = null;
        }
    }

    private void RunClock(CancellationToken stop)
    {
        const long maxCatchUp = CyclesPerSecond / 10;
        Stopwatch watch = Stopwatch.StartNew();
        long done = 0;
        while (!stop.IsCancellationRequested)
        {
            long due = watch.ElapsedMilliseconds * CyclesPerSecond / 1000;
            done = Math.Max(done, due - maxCatchUp);
            for (; done < due && !stop.IsCancellationRequested; done++)
            {
                try { AdvanceCycle(); }
                catch (Exception e) { Debug.WriteLine($"UKS clock: {e.Message}"); } // a background thread must not take the process down
            }
            Thread.Sleep(1);
        }
    }

    // Thoughts with a finite TimeToLive, counted down by AdvanceCycle.
    private readonly HashSet<Thought> mortalThoughts = new(ReferenceEqualityComparer.Instance);

    // Called whenever a Thought's TimeToLive is set, and when it is deleted (forget: true).
    internal void TrackMortality(Thought thought, bool forget = false)
    {
        lock (mortalThoughts)
        {
            if (forget || thought.TimeToLive == Thought.Forever) mortalThoughts.Remove(thought);
            else mortalThoughts.Add(thought);
        }
    }

    private void ClearMortalThoughts()
    {
        lock (mortalThoughts) mortalThoughts.Clear();
    }

    /// <summary>
    /// Agent: advances <see cref="Cycle"/> by one and counts down every finite TimeToLive.
    /// A Thought whose time runs out is deleted (a Link is removed from its source).
    /// </summary>
    /// <returns>The Thoughts that expired.</returns>
    public IReadOnlyList<Thought> AdvanceCycle()
    {
        Cycle++;
        List<Thought> expired = new();
        lock (mortalThoughts)
        {
            foreach (Thought thought in mortalThoughts)
                if (thought.CountDown()) expired.Add(thought);
            mortalThoughts.ExceptWith(expired);
        }
        foreach (Thought thought in expired)
        {
            if (thought is Link link) link.From?.RemoveLink(link);
            else if (AtomicThoughts.Contains(thought)) thought.Delete(); // an earlier deletion may have taken it
        }
        return expired;
    }
}

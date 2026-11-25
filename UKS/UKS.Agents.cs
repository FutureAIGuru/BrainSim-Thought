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

/// <summary>
/// Agents are background processes which reorganize the UKS (e.g. <see cref="BubbleAttributes"/>).
/// Each can be called directly to run once, or scheduled here to run periodically.
/// </summary>
public partial class UKS
{
    private readonly Dictionary<Func<List<Thought>>, Timer> agentTimers = new();
    private readonly object agentLock = new();

    /// <summary>
    /// Runs an agent every interval, e.g. RunPeriodically(uks.BubbleAttributes, TimeSpan.FromSeconds(10)).
    /// A null interval stops it. If called from a thread with a synchronization context (a UI thread),
    /// the agent runs on that thread; otherwise on a pool thread, never overlapping another agent run.
    /// </summary>
    public void RunPeriodically(Func<List<Thought>> agent, TimeSpan? interval)
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
    public List<Thought> AdvanceCycle()
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

    /// <summary>
    /// Agent: runs bubbling once over every Thought under Object or Unknown.
    /// Can be run periodically with <see cref="RunPeriodically"/>.
    /// </summary>
    /// <returns>The parents whose links changed.</returns>
    public List<Thought> BubbleAttributes()
    {
        List<Thought> parents = AtomicThoughts.Where(t => t.HasAncestor("Object") || t.HasAncestor("Unknown")).ToList();
        List<Thought> changed = new();
        foreach (Thought parent in parents)
            if (BubbleSharedAttributes(parent))
                changed.Add(parent);
        return changed;
    }

    /// <summary>
    /// Bubbling: when more than minFraction of a parent's children share a link (same type and target),
    /// the parent takes the link and the children's copies are removed (they still inherit it).
    /// A shared link is blocked if as many siblings hold a conflicting (exclusive) link.
    /// </summary>
    private bool BubbleSharedAttributes(Thought parent, float minFraction = 0.6f)
    {
        // A single child is no evidence of a shared attribute.
        if (parent.Children.Count < 2 || parent == Labeled("Unknown")) return false;

        // Every link held by a child, except inheritance and properties.
        Thought? isA = Labeled("is-a");
        Thought? hasProperty = Labeled("hasProperty");
        List<Link> childLinks = parent.Children.SelectMany(child => child.LinksTo).ToList();
        List<Link> candidates = childLinks
            .Where(link => link.LinkType is not null && link.To is not null && link.LinkType != isA && link.LinkType != hasProperty)
            .ToList();

        // Links with the same type and target, the most widely shared first.
        List<List<Link>> sharedLinks = candidates
            .GroupBy(link => (link.LinkType, link.To))
            .Select(group => group.ToList())
            .OrderByDescending(links => links.Count)
            .ToList();

        float threshold = parent.Children.Count * minFraction;
        bool changed = false;
        foreach (List<Link> links in sharedLinks)
        {
            if (links.Count <= threshold) continue;

            // Siblings holding a conflicting link (such as a different exclusive value) vote against it.
            int conflicting = sharedLinks
                .Where(other => other != links && LinksAreExclusive(links[0], other[0]))
                .Sum(other => other.Count);
            if (conflicting >= links.Count) continue;

            Link bubbled = parent.AddLink(links[0].LinkType!, links[0].To)!;
            bubbled.Weight = links.Average(link => link.Weight);
            foreach (Link link in links)
                link.From!.RemoveLink(link);

            List<Link> parentConflicts = parent.LinksTo.Where(link => link != bubbled && LinksAreExclusive(bubbled, link)).ToList();
            foreach (Link conflict in parentConflicts)
                parent.RemoveLink(conflict);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Agent: where a Thought links by one type to several targets with a common ancestor, adds a
    /// counted link to that ancestor (Fido has paw, has paw → Fido has.2 paw). Only ancestors under Unknown count.
    /// </summary>
    /// <returns>The links added.</returns>
    public List<Thought> AddCounts()
    {
        List<Thought> added = new();
        foreach (Thought t in AtomicThoughts.ToList())
        {
            for (int j = 0; j < t.LinksTo.Count; j++)
            {
                Link r = t.LinksTo[j];
                if (r.LinkType is not Thought linkType || linkType == Labeled("is-a")) continue;

                // Count the ancestors shared by the targets of this link type.
                List<Thought> targets = t.LinksTo.Where(x => x.LinkType == linkType && x.To is not null).Select(x => x.To!).ToList();
                Dictionary<Thought, int> ancestorCounts = new();
                foreach (Thought target in targets)
                    foreach (Thought ancestor in target.Ancestors)
                        ancestorCounts[ancestor] = ancestorCounts.GetValueOrDefault(ancestor) + 1;

                foreach ((Thought ancestor, int count) in ancestorCounts)
                {
                    if (!ancestor.HasAncestor("Unknown") || ancestor == Labeled("Unknown") || count < 2) continue;
                    string countedLabel = linkType.Label + "." + count;
                    if (Labeled(countedLabel) is Thought existingType && GetLink(t, existingType, ancestor) is not null) continue;
                    Thought countedType = GetOrAddThought(countedLabel, linkType.Parents[0])!;
                    if (AddStatement(t, countedType, ancestor) is Link link)
                        added.Add(link);
                }
            }
        }
        return added;
    }

    /// <summary>
    /// Agent: when a Thought under Object has more than 6 children, moves them into new
    /// subclasses of at most 6.
    /// </summary>
    /// <returns>The subclasses created.</returns>
    public List<Thought> BalanceTree()
    {
        const int maxChildren = 6;
        List<Thought> created = new();
        foreach (Thought t in AtomicThoughts.ToList())
        {
            if (!t.HasAncestor("Object") || t.Label.Contains('.')) continue;
            while (t.Children.Count > maxChildren)
            {
                Thought newParent = AddThought(t.Label, t);
                created.Add(newParent);
                while (newParent.Children.Count < maxChildren)
                {
                    Thought child = t.Children[0];
                    child.RemoveParent(t);
                    child.AddParent(newParent);
                }
            }
        }
        return created;
    }

    /// <summary>
    /// Agent: among the children of each Thought under Object, finds groups (3 to 12 members) that
    /// share the same attributes and makes each group an anonymous subclass.
    /// </summary>
    /// <returns>The classes created or reused.</returns>
    public List<Thought> CreateClasses()
    {
        List<Thought> classes = new();
        foreach (Thought t in AtomicThoughts.ToList())
        {
            if (t.HasAncestor("Object") && !HasDirectProperty(t, "isAnonymousClass") &&
                !t.Label.Contains("unknown", StringComparison.OrdinalIgnoreCase))
                classes.AddRange(CreateClassesFromCommonAttributes(t));
        }
        return classes;
    }

    private List<Thought> CreateClassesFromCommonAttributes(Thought t)
    {
        const int maxChildren = 12;
        const int minClassMembers = 3;
        List<Thought> classes = new();

        // Work one taxonomy level at a time. Every parent is visited by the
        // agent, so scanning descendants here would repeatedly rediscover the
        // same population and create nested duplicate classes.
        List<Thought> candidates = t.Children
            .Where(child => child is not Link && !HasDirectProperty(child, "isAnonymousClass"))
            .ToList();
        if (candidates.Count < minClassMembers) return classes;

        // Record the member set supporting each attribute. Attributes with exactly the same
        // supporting population describe one discovered class, rather than one class per attribute.
        Dictionary<(Thought linkType, Thought target), HashSet<Thought>> attributes = new();
        foreach (Thought child in candidates)
        {
            foreach (Link link in child.LinksTo)
            {
                if (link.LinkType is null || link.To is null || link.LinkType == Labeled("is-a"))
                    continue;
                if (link.LinkType.Label.Equals("hasProperty", StringComparison.OrdinalIgnoreCase) ||
                    link.LinkType.Label.Equals("hasImage", StringComparison.OrdinalIgnoreCase) ||
                    link.LinkType.HasProperty("isGrounding"))
                    continue;

                var key = (link.LinkType, link.To);
                if (!attributes.TryGetValue(key, out HashSet<Thought>? members))
                {
                    members = new HashSet<Thought>();
                    attributes.Add(key, members);
                }
                members.Add(child);
            }
        }

        List<HashSet<Thought>> groups = new();
        foreach (var item in attributes.Where(item => item.Value.Count >= minClassMembers && item.Value.Count <= maxChildren))
            if (!groups.Any(existing => existing.SetEquals(item.Value)))
                groups.Add(new HashSet<Thought>(item.Value));

        foreach (HashSet<Thought> members in groups.OrderByDescending(group => group.Count))
        {
            Thought? existingClass = t.Children.FirstOrDefault(child => child.Children.ToHashSet().SetEquals(members));
            Thought newParent = GetOrCreateThoughtClass(t, members, "class*");
            if (existingClass is null)
                newParent.AddProperty(GetOrAddThought("isAnonymousClass", "Property"));
            foreach (Thought child in members)
                child.RemoveParent(t);
            classes.Add(newParent);
        }
        return classes;
    }

    private static bool HasDirectProperty(Thought thought, string propertyLabel) =>
        thought.LinksTo.Any(link =>
            link.LinkType?.Label.Equals("hasProperty", StringComparison.OrdinalIgnoreCase) == true &&
            link.To?.Label.Equals(propertyLabel, StringComparison.OrdinalIgnoreCase) == true);
}

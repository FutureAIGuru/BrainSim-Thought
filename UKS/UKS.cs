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

namespace UKS;

// The store: creating and finding Thoughts by label, and the store as a whole. Links are stored by
// UKS.Statement.cs and read by UKS.Query.cs; the label index and notation are in UKS.Labels.cs.

/// <summary>
/// A Universal Knowledge Store: Thoughts connected by links, with inheritance through is-a, queries
/// that resolve inherited and conflicting knowledge, sequences, time counted in cycles, undo, and
/// text and XML files.
/// </summary>
/// <remarks>
/// Each store is independent: a label always means a Thought of the store being called. Start a store
/// with <c>new UKS()</c> and <see cref="CreateInitialStructure"/>, or load one with <see cref="LoadXml"/>.
/// </remarks>
public partial class UKS
{
    /// <summary>
    /// Creates an empty, independent store. Call <see cref="CreateInitialStructure"/> for the core types.
    /// </summary>
    public UKS() { }

    /// <summary>
    /// The Thought with this label, created if there is none. A dotted label composes its parts: big.dog
    /// gets the parents big and dog (each created if needed), and is.not.! gets is.not and !. A trailing *
    /// makes a new numbered Thought (dog0, dog1, ...).
    /// </summary>
    /// <param name="label">The label; an empty one returns null.</param>
    /// <param name="parent">
    /// The parent, a Thought or a label; empty means Unknown. An existing Thought under Unknown moves to
    /// the given parent.
    /// </param>
    /// <param name="source">
    /// With a label ending in *, reuse the first numbered Thought that <paramref name="source"/> does not
    /// already have a link of (so each of its links gets its own number).
    /// </param>
    /// <returns>The Thought, or null for an empty label.</returns>
    public Thought? GetOrAddThought(string label, ThoughtRef parent = default, Thought? source = null)
    {
        if (source is not null && !ReferenceEquals(source.Store, this))
            throw new ArgumentException("Source belongs to another UKS.", nameof(source));
        if (string.IsNullOrEmpty(label)) return null;
        Thought? parentThought = parent.Find(this) ?? Labeled("Unknown");

        if (Labeled(label) is Thought existing)
            return Reclassify(existing, label, parentThought);

        // Label notation is expanded once, when the Thought is created (UKS.Labels.cs).
        int pathMarker = label.IndexOf(".^", StringComparison.Ordinal);
        if (pathMarker > 0) return AddPath(label, pathMarker, parentThought);
        if (IsDottedLabel(label)) return AddCompound(label);

        if (parentThought is null) return null;
        if (label.EndsWith('*') && source is not null && ReusableNumberedThought(label, source) is Thought numbered)
            return numbered;
        Thought added = AddThought(label, parentThought);
        added.Fire();
        return added;
    }

    /// <summary>
    /// Always creates a new Thought; if the label is in use, a number is appended to it (a trailing *
    /// becomes the next free number). Without a parent the Thought is a root. Use
    /// <see cref="GetOrAddThought"/> to find a labeled Thought or create it.
    /// </summary>
    /// <param name="label">Label of the Thought to create.</param>
    /// <param name="parent">Optional parent, a Thought or a label; an empty or unknown one adds no parent.</param>
    /// <returns>The new Thought.</returns>
    public virtual Thought AddThought(string label, ThoughtRef parent = default)
    {
        Thought? t = parent.Find(this);
        Thought newThought = new(this);
        newThought.Label = label;
        newThought.AddDefaultLabel();
        if (t is not null)
        {
            newThought.AddParent(t);
        }
        lock (AtomicThoughts)
        {
            AtomicThoughts.Add(newThought);
        }

        return newThought;
    }

    /// <summary>The Thought with this label (ignoring case), or null if there is none.</summary>
    public Thought? Labeled(string label)
    {
        if (string.IsNullOrEmpty(label)) return null;
        Labels.TryGetValue(label.ToLowerInvariant(), out Thought? thought);
        return thought;
    }

    /// <summary>
    /// The Thoughts whose labels start with <paramref name="prefix"/> (ignoring case), in label order,
    /// e.g. to complete a label as it is typed. Links and sequence elements are included.
    /// </summary>
    public IReadOnlyList<Thought> LabeledStartingWith(string prefix) =>
        Labels.Values.Where(thought => thought.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(thought => thought.Label, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The Thoughts with no parents, the tops of the hierarchy (e.g. Thought, BrainSim).</summary>
    public IReadOnlyList<Thought> Roots()
    {
        List<Thought> thoughts;
        lock (AtomicThoughts) thoughts = AtomicThoughts.Where(thought => thought is not Link).ToList();
        return thoughts.Where(thought => thought.Parents.Count == 0).ToList();
    }

    // Every stored Thought, link and sequence element has a label (only private, unlabeled patterns
    // don't), so the counts come from the label index, which is safe to read while other threads write.

    /// <summary>The number of Thoughts in the store, not counting links or sequence elements.</summary>
    public int ThoughtCount() => Labels.Values.Count(thought => thought is not Link && thought is not SeqElement);

    /// <summary>The number of links in the store, including those within sequences.</summary>
    public int LinkCount() => Labels.Values.Count(thought => thought is Link);

    /// <summary>The number of sequence elements in the store.</summary>
    public int SequenceElementCount() => Labels.Values.Count(thought => thought is SeqElement);

    /// <summary>
    /// Merges <paramref name="redundantThought"/> into <paramref name="canonicalThought"/>:
    /// every link from, to, or typed by the redundant Thought is redirected to the canonical
    /// one, reinforcement is combined, and the redundant Thought is deleted.
    /// </summary>
    /// <returns>The number of links which were redirected.</returns>
    public int Merge(Thought redundantThought, Thought canonicalThought)
    {
        ArgumentNullException.ThrowIfNull(redundantThought);
        ArgumentNullException.ThrowIfNull(canonicalThought);
        if (!ReferenceEquals(redundantThought.Store, this) || !ReferenceEquals(canonicalThought.Store, this))
            throw new ArgumentException("Cannot merge Thoughts from another UKS.");
        if (ReferenceEquals(redundantThought, canonicalThought)) return 0;

        Thought Redirect(Thought? t) => ReferenceEquals(t, redundantThought) ? canonicalThought : t!;

        List<Link> typedLinks;
        lock (redundantThought.LinksAsTypeWriteable)
            typedLinks = redundantThought.LinksAsTypeWriteable.ToList();
        List<Link> oldLinks = redundantThought.LinksTo
            .Concat(redundantThought.LinksFrom)
            .Concat(typedLinks)
            .Distinct(ReferenceEqualityComparer.Instance).Cast<Link>()
            .ToList();

        int replacedCount = 0;
        foreach (Link oldLink in oldLinks)
        {
            if (oldLink.From is null || oldLink.LinkType is null) continue;
            Thought newSource = Redirect(oldLink.From);
            Thought newType = Redirect(oldLink.LinkType);
            Thought? newTarget = oldLink.To is null ? null : Redirect(oldLink.To);

            // Merging a parent with its child would otherwise manufacture A is-a A.
            bool selfInheritance = newType == Labeled("is-a") && ReferenceEquals(newSource, newTarget);
            if (!selfInheritance && newSource.AddLink(newType, newTarget) is Link replacement)
                CombineReinforcement(replacement, oldLink);

            oldLink.From.RemoveLink(oldLink);
            replacedCount++;
        }

        CombineReinforcement(canonicalThought, redundantThought);
        canonicalThought.Value ??= redundantThought.Value;
        ClearExtraneousParents(canonicalThought);
        redundantThought.Delete();
        return replacedCount;
    }

    // Keeps the stronger, more recent, longer-lived of two Thoughts' reinforcement.
    private static void CombineReinforcement(Thought kept, Thought merged)
    {
        kept.Weight = Math.Max(kept.Weight, merged.Weight);
        kept.MaxWeight = Math.Max(kept.MaxWeight, merged.MaxWeight);
        kept.IsPlastic |= merged.IsPlastic;
        kept.LastFiredCycle = Math.Max(kept.LastFiredCycle, merged.LastFiredCycle);
        if (kept.TimeToLive != Thought.Forever &&
            (merged.TimeToLive == Thought.Forever || merged.TimeToLive > kept.TimeToLive))
            kept.TimeToLive = merged.TimeToLive;
    }

    /// <summary>
    /// Deletes every descendant of <paramref name="t"/> (even one with another parent) and removes all
    /// of <paramref name="t"/>'s own links, including its is-a links and any sequences its links point to.
    /// <paramref name="t"/> itself stays in the store, unlinked.
    /// </summary>
    public void DeleteDescendants(Thought t)
    {
        if (t is not null && !ReferenceEquals(t.Store, this))
            throw new ArgumentException("Thought belongs to another UKS.", nameof(t));
        if (t is not null)
        {
            List<Thought> descendants = t.Descendants.ToList();
            foreach (Link t1 in t.LinksTo)
            {
                if (t1.To is SeqElement s)
                {
                    DeleteSequence(s);
                }
                t1.Delete();
            }
            foreach (Thought t1 in descendants)
                t1.Delete();
        }
    }

    /// <summary>
    /// Empties the store: every Thought and label, the sequence cache, the undo history and
    /// everything counting down, and the cycle starts again at 1.
    /// Call <see cref="CreateInitialStructure"/> afterward for the core types.
    /// </summary>
    public void Reset()
    {
        lock (AtomicThoughts) AtomicThoughts.Clear();
        Labels.Clear();
        nextGeneratedLabel = 999;
        ClearSequenceCache();
        ClearMortalThoughts();
        ClearUndoHistory();
        Cycle = 1;
    }

    /// <summary>Raised whenever a Thought in this UKS fires. Handlers run on the firing thread and should be brief.</summary>
    public event Action<Thought>? ThoughtFired;

    internal void RaiseThoughtFired(Thought thought) => ThoughtFired?.Invoke(thought);

    // Handy for temporarily suppressing firing during bulk updates or similar operations
    /// <summary>
    /// While true, <see cref="Thought.Fire"/> does nothing, e.g. during a bulk import
    /// (<see cref="ImportTextFile"/> sets it while reading).
    /// </summary>
    public bool SuppressFiring { get; set; } = false;

    /// <summary>
    /// Every Thought in the store, for the UKS's own use (and its tests). Outside code reaches
    /// Thoughts through labels, links, <see cref="Roots"/> and the counts.
    /// Membership uses object identity so changing a label cannot invalidate an entry.
    /// </summary>
    internal HashSet<Thought> AtomicThoughts { get; } = new(ReferenceEqualityComparer.Instance);
}

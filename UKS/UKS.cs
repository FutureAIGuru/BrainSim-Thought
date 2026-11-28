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

/// <summary>
/// Contains a collection of Thoughts linked by Links to implement Common Sense and general knowledge.
/// </summary>
public partial class UKS
{
    //This is the actual internal Universal Knowledge Store
    private readonly HashSet<Thought> uKSList = new(ReferenceEqualityComparer.Instance);
    internal readonly System.Collections.Concurrent.ConcurrentDictionary<string, Thought> Labels = new();
    private long nextGeneratedLabel = 999;
    /// <summary>Raised whenever a Thought in this UKS fires. Handlers run on the firing thread and should be brief.</summary>
    public event Action<Thought>? ThoughtFired;
    internal void RaiseThoughtFired(Thought thought) => ThoughtFired?.Invoke(thought);

    // A flattened copy of the UKS used while saving and loading XML: circular references are
    // broken by storing indexes into this list (sThought) instead of Thought references.
    private List<sThought> UKSTemp = new();

    /// <summary>
    /// Every Thought in the store, for the UKS's own use (and its tests). Outside code reaches
    /// Thoughts through labels, links, <see cref="Roots"/> and the counts.
    /// Membership uses object identity so changing a label cannot invalidate an entry.
    /// </summary>
    internal HashSet<Thought> AtomicThoughts { get => uKSList; }

    // Every stored Thought, link and sequence element has a label (only private, unlabeled patterns
    // don't), so the counts come from the label index, which is safe to read while other threads write.

    /// <summary>The number of Thoughts in the store, not counting links or sequence elements.</summary>
    public int ThoughtCount() => Labels.Values.Count(thought => thought is not Link && thought is not SeqElement);

    /// <summary>The number of links in the store, including those within sequences.</summary>
    public int LinkCount() => Labels.Values.Count(thought => thought is Link);

    /// <summary>The number of sequence elements in the store.</summary>
    public int SequenceElementCount() => Labels.Values.Count(thought => thought is SeqElement);

    /// <summary>The Thoughts with no parents, the tops of the hierarchy (e.g. Thought, BrainSim).</summary>
    public IReadOnlyList<Thought> Roots()
    {
        List<Thought> thoughts;
        lock (AtomicThoughts) thoughts = AtomicThoughts.Where(thought => thought is not Link).ToList();
        return thoughts.Where(thought => thought.Parents.Count == 0).ToList();
    }

    /// <summary>
    /// Creates an empty, independent store. Call <see cref="CreateInitialStructure"/> for the core types.
    /// </summary>
    public UKS() { }

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

    // Handy for temporarily suppressing firing during bulk updates or similar operations
    public bool SuppressFiring { get; set; } = false;

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

    /// <summary>
    /// Uses a hash table to return the Thought with the given label or null if it does not exist.
    /// </summary>
    /// <param name="label">Label to look up.</param>
    /// <returns>The Thought or null.</returns>
    public Thought? Labeled(string label)
    {
        if (string.IsNullOrEmpty(label)) return null;
        Labels.TryGetValue(label.ToLowerInvariant(), out Thought? thought);
        return thought;
    }

    /// <summary>
    /// Indexes <paramref name="thought"/> under <paramref name="newLabel"/>, replacing its current entry,
    /// and returns the label actually used: a trailing * becomes the next free number, and a label
    /// already in use gets a number appended. An empty label is not indexed.
    /// </summary>
    internal string IndexLabel(string newLabel, Thought thought)
    {
        if (newLabel == "") return newLabel;
        if (Labels.TryGetValue(thought.Label.ToLowerInvariant(), out Thought? previous) && ReferenceEquals(previous, thought))
            Labels.TryRemove(thought.Label.ToLowerInvariant(), out _);
        int number = -1;
        string baseLabel = newLabel;
        if (newLabel.EndsWith('*'))
        {
            number = 0;
            baseLabel = newLabel[..^1].TrimEnd("0123456789".ToCharArray());
            newLabel = baseLabel + number;
        }
        while (!Labels.TryAdd(newLabel.ToLowerInvariant(), thought))
        {
            number++;
            newLabel = baseLabel + number;
        }
        ReserveGeneratedLabel(newLabel);
        return newLabel;
    }

    /// <summary>Removes a label from the index.</summary>
    internal void UnindexLabel(string label)
    {
        if (!string.IsNullOrEmpty(label)) Labels.TryRemove(label.ToLowerInvariant(), out _);
    }

    /// <summary>
    /// The Thoughts whose labels start with <paramref name="prefix"/> (ignoring case), in label order,
    /// e.g. to complete a label as it is typed. Links and sequence elements are included.
    /// </summary>
    public IReadOnlyList<Thought> LabeledStartingWith(string prefix) =>
        Labels.Values.Where(thought => thought.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(thought => thought.Label, StringComparer.OrdinalIgnoreCase).ToList();

    internal string NextGeneratedLabel()
    {
        string label;
        do label = "unl_" + Interlocked.Increment(ref nextGeneratedLabel).ToString(System.Globalization.CultureInfo.InvariantCulture);
        while (Labels.ContainsKey(label));
        return label;
    }

    internal void ReserveGeneratedLabel(string label)
    {
        if (!label.StartsWith("unl_", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(label.AsSpan(4), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int number)) return;
        long current = Interlocked.Read(ref nextGeneratedLabel);
        while (number > current)
        {
            long previous = Interlocked.CompareExchange(ref nextGeneratedLabel, number, current);
            if (previous == current) break;
            current = previous;
        }
    }

    // The link types the store itself relies on, found by label (the index keys are lower case).
    internal Thought? IsAType => CoreType("is-a");
    internal Thought? HasPropertyType => CoreType("hasproperty");
    internal Thought? FrstType => CoreType("frst");
    internal Thought? NxtType => CoreType("nxt");
    internal Thought? VluType => CoreType("vlu");
    private Thought? CoreType(string key) => Labels.TryGetValue(key, out Thought? type) ? type : null;

    // The same connection, optionally ignoring the sources (two query results from different
    // sources that say the same thing).
    private static bool LinksAreEqual(Link r1, Link r2, bool ignoreSource = true) =>
        ReferenceEquals(r1.LinkType, r2.LinkType) && Link.SameConnection(r1.To, r2.To) &&
        (ignoreSource || Link.SameConnection(r1.From, r2.From));

    /// <summary>
    /// Gets an existing link matching the specified from/linkType/to triple, if present.
    /// </summary>
    /// <param name="from">Source thought of the link.</param>
    /// <param name="linkType">Link type thought.</param>
    /// <param name="to">Target thought of the link.</param>
    /// <returns>The existing link Thought, or null if not found.</returns>
    public Link? GetLink(ThoughtRef from, ThoughtRef linkType, ThoughtRef to)
    {
        Thought? source = from.Find(this), type = linkType.Find(this), target = to.Find(this);
        if (source is null || type is null || (target is null && !to.IsEmpty)) return null;
        //create a temporary link
        Link r = new(this) { From = source, LinkType = type, To = target };
        //see if it already exists
        return GetLink(r);
    }

    // The stored link that a prototype (an unstored link with the same source, type and target) stands for.
    internal Link? GetLink(Link r)
    {
        if (r.From is null) return null;
        foreach (Link r1 in r.From.LinksTo)
        {
            if (LinksAreEqual(r, r1)) return r1;
        }
        return null;
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
    /// Creates a new Thought in the UKS OR returns an existing Thought, based on the label.
    /// </summary>
    /// <param name="label">Label for the thought. Trailing '*' auto-increments to a unique label.</param>
    /// <param name="parent">Optional parent, a Thought or a label; an empty or unknown one means Unknown.</param>
    /// <param name="source">Optional source thought used to probe existing numbered links.</param>
    /// <returns>The existing or newly created Thought.</returns>
    public Thought? GetOrAddThought(string label, ThoughtRef parent = default, Thought? source = null)
    {
        if (source is not null && !ReferenceEquals(source.Store, this))
            throw new ArgumentException("Source belongs to another UKS.", nameof(source));
        if (string.IsNullOrEmpty(label)) return null;

        Thought? thoughtToReturn = Labeled(label);
        Thought? correctParent = parent.Find(this) ?? Labeled("Unknown");

        if (thoughtToReturn is not null)
        {
            // The roots stay roots.
            if (thoughtToReturn.Parents.Count == 0 &&
                thoughtToReturn != Labeled("BrainSim") &&
                thoughtToReturn != Labeled("Thought") &&
                correctParent is not null)
                thoughtToReturn.AddParent(correctParent);
            if (correctParent is not null && correctParent != Labeled("Unknown"))
            {
                thoughtToReturn.RemoveParent("Unknown");
                thoughtToReturn.AddParent(correctParent);
            }
            if (label.Split('.').Contains("?") && Labeled("?") is Thought possibleType)
                thoughtToReturn.AddParent(possibleType);

            return thoughtToReturn;
        }
        // Readable input notation is expanded once, when creating the Thought.
        // The resulting Path is an ordinary, uncompressed sequence with inherited properties.
        int sequenceMarker = label.IndexOf(".^", StringComparison.Ordinal);
        if (sequenceMarker > 0)
        {
            Thought? capability = GetOrAddThought(label[..sequenceMarker]);
            string[] elements = label[(sequenceMarker + 2)..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (capability is null || elements.Length == 0) return null;
            List<Thought> values = new();
            foreach (string element in elements)
            {
                Thought? value = GetOrAddThought(element);
                if (value is null) return null;
                values.Add(value);
            }

            SeqElement sequence = CreateFirstElement("path*", values[0]);
            SeqElement last = sequence;
            foreach (Thought value in values.Skip(1))
                last = InsertAfter(last, value);
            sequence.Label = label;
            sequence.AddParent(capability);
            if (correctParent is not null && correctParent != Labeled("Unknown"))
                sequence.AddParent(correctParent);
            return sequence;
        }
        //. are used to indicate attributes to be added
        if (label.Contains(".") && label != "." && !label.Contains(".py") &&
            !new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(Path.GetExtension(label), StringComparer.OrdinalIgnoreCase))
        {
            string[] attribs = label.Split(".");
            // Materialize the ordinary compound before adding execution capabilities.
            // Thus is.not.? retains is.not as an identifiable link type; execution
            // never has to strip punctuation or reconstruct a type from label text.
            List<Thought> components = new();
            foreach (string componentLabel in attribs)
            {
                Thought? component = Labeled(componentLabel) ?? AddThought(componentLabel, "Unknown");
                if (component is not null) components.Add(component);
            }
            Thought? ordinaryCompound = null;
            if (components.Any(component => component.HasProperty("isPossible") || component.HasProperty("setRelationship")))
            {
                List<Thought> ordinaryComponents = components.Where(component =>
                    !component.HasProperty("isPossible") && !component.HasProperty("setRelationship")).ToList();
                if (ordinaryComponents.Count > 1)
                    ordinaryCompound = GetOrAddThought(string.Join(".", ordinaryComponents.Select(component => component.Label)));
            }
            Thought? baseThought = Labeled(attribs[0]) ?? AddThought(attribs[0], "Unknown");
            Thought? instanceThought = Labeled(label) ?? AddThought(label, baseThought);
            if (instanceThought is not null)
            {
                for (int i = 1; i < attribs.Length; i++)
                {
                    Thought? attrib = Labeled(attribs[i]) ?? AddThought(attribs[i], "Unknown");
                    if (attrib is not null)
                    {
                        // Compound components contribute through ordinary inheritance.
                        instanceThought.AddParent(attrib);
                    }
                }
                if (ordinaryCompound is not null)
                {
                    instanceThought.AddParent(ordinaryCompound);
                }
            }
            return instanceThought;
        }

        if (correctParent is null) return null;

        if (label.EndsWith("*"))
        {
            string baseLabel = label.Substring(0, label.Length - 1);
            //instead of creating a new label, see if the next label for this item already exists and can be reused
            if (source is not null)
            {
                int digit = 0;
                while (source.LinksTo.FirstOrDefault(x => x.LinkType?.Label == baseLabel + digit) is not null) digit++;
                Thought? labeled = Labeled(baseLabel + digit);
                if (labeled is not null)
                    return labeled;
            }
        }

        thoughtToReturn = AddThought(label, correctParent);
        thoughtToReturn.Fire();
        return thoughtToReturn;
    }

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
}

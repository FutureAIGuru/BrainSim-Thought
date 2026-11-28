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

// Sequences: ordered lists of Thoughts, made of SeqElements (FRST, NXT, VLU links). An identical
// sequence is stored once and shared, and a stored sequence can be reused inside a longer one.
// Building, reading and editing them is here; finding them is in UKS.SequenceSearch.cs.
//
// The spelling of "cat", [w:cat->spelled->^c a t], is three elements:
//   seq0 -FRST-> seq0   seq0 -VLU-> c   seq0 -NXT-> seq1
//   seq1 -FRST-> seq0   seq1 -VLU-> a   seq1 -NXT-> seq2
//   seq2 -FRST-> seq0   seq2 -VLU-> t   (the last element has no NXT)
// Every element's FRST is the first element, so any element leads back to the start. A VLU target
// can itself be a sequence (nesting). Element labels are generated; only the links matter.
//
// TODO: circular sequences, which a search could enter anywhere.
public partial class UKS
{
    /// <summary>
    /// Stores a sequence of values, or returns the identical one already stored (a sequence is stored once
    /// and shared). With compression, a run of values that is already a stored sequence is replaced by that
    /// sequence (so "cats" can reuse "cat").
    /// </summary>
    /// <param name="label">The label of the first element of a new sequence.</param>
    /// <param name="targets">The values in order; a value may be the first element of another sequence.</param>
    /// <param name="allowCompression">False to store the values flat.</param>
    /// <returns>The first element, or null for no values.</returns>
    public SeqElement? AddSequence(string label, IReadOnlyList<Thought>? targets, bool allowCompression = true)
    {
        if (targets is not null && targets.Any(value => value is not null && !ReferenceEquals(value.Store, this)))
            throw new ArgumentException("Sequence values must belong to this UKS.", nameof(targets));
        if (targets is null || targets.Count == 0) return null;

        List<Thought> resolvedTargets = new(targets);

        // does the exact sequence already exist?
        //  (wildcards are not allowed)
        var existing = FindSequencesByActivation(targets, "ExactSequenceSearch");
        if (existing.Count > 0)
            return existing[0].seqNode;

        //check for any existing sequences which begins with the targets[startIndex]
        (Thought? seqStart, int length) FindExistingSubsequence(int startIndex)
        {
            int remaining = resolvedTargets.Count - startIndex;
            for (int len = remaining; len > 1; len--)
            {
                var testSequence = resolvedTargets.GetRange(startIndex, len);
                if (SequenceCache.TryGetValue(testSequence, out Thought? existing) && existing is not null)
                    return (existing, len);
            }
            return (null, 0);
        }

        //Are there any existing seqnences in the target list?
        // edit the resolved target list that reuses any existing subsequences
        if (allowCompression)
        {
            for (int i = 0; i < resolvedTargets.Count; i++)
            {
                (Thought? seqStart, int length) = FindExistingSubsequence(i);
                if (seqStart is not null)
                {
                    resolvedTargets.RemoveRange(i, length);
                    resolvedTargets.Insert(i, seqStart);
                    continue;
                }
            }
        }
        //Finally, create the sequence and link to it
        SeqElement rawSequence = CreateRawSequence(resolvedTargets, label)!;
        var newSequence = FlattenSequence(rawSequence);
        SequenceCache[newSequence] = rawSequence;
        return rawSequence;

    }

    /// <summary>
    /// Stores a sequence (<see cref="AddSequence"/>) and links an owner to it, e.g. [w:cat→spelled→^c a t],
    /// replacing the owner's previous link of that type.
    /// </summary>
    /// <param name="source">The owner of the sequence.</param>
    /// <param name="linkType">The type of the owner's link; an unknown label creates it under LinkType.</param>
    /// <param name="targets">The values in order; a value may be the first element of another sequence.</param>
    /// <param name="allowCompression">False to store the values flat.</param>
    /// <returns>The first element, or null for no values.</returns>
    public SeqElement? AddSequenceAndLink(
        Thought source,
        ThoughtRef linkType,
        IReadOnlyList<Thought> targets,
        bool allowCompression = true)
    {
        if (targets is null || targets.Count == 0) return null;

        if (!ReferenceEquals(source.Store, this) ||
            targets.Any(value => value is not null && !ReferenceEquals(value.Store, this)))
            throw new ArgumentException("Sequence and its links must belong to this UKS.");
        Thought? type = linkType.GetOrAdd(this, "LinkType");

        // Replace any existing sequence link of this type. TODO: delete the old sequence.
        if (type is not null)
            foreach (Link old in source.LinksTo.Where(link => link.LinkType == type))
                source.RemoveLink(old);
        SeqElement rawSequence = AddSequence(source.Label, targets, allowCompression)!;
        if (type is not null) source.AddLink(type, rawSequence);
        return rawSequence;
    }

    /// <summary>The values of a sequence in order, with nested sequences expanded.</summary>
    /// <param name="sequenceStart">The first element; anything else gives an empty list.</param>
    public IReadOnlyList<Thought> FlattenSequence(Thought? sequenceStart)
    {
        List<Thought> result = new();
        foreach (SeqElement element in EnumerateSequenceElements(sequenceStart as SeqElement))
            if (GetElementValue(element) is Thought value)
                result.Add(value);
        return result;
    }

    /// <summary>
    /// The elements of a sequence in order, descending into nested sequences, so each element returned
    /// holds a value that is not a sequence. A sequence that contains itself is not repeated.
    /// </summary>
    /// <param name="sequenceStart">The first element (or any element, to start there).</param>
    public IEnumerable<SeqElement> EnumerateSequenceElements(SeqElement? sequenceStart)
    {
        // Each enumeration needs its own record of the sequences it is inside.
        foreach (SeqElement element in EnumerateSequenceElements(sequenceStart, new Stack<SeqElement>()))
            yield return element;
    }

    private IEnumerable<SeqElement> EnumerateSequenceElements(SeqElement? sequenceStart, Stack<SeqElement> visitedSequences)
    {
        if (sequenceStart is null) yield break;
        if (visitedSequences.Contains(sequenceStart)) yield break; // Already visited this sequence, stop to prevent infinite recursion
        visitedSequences.Push(sequenceStart);
        var current = sequenceStart;
        var visitedInMain = new HashSet<SeqElement>();

        while (current is not null)
        {
            if (!visitedInMain.Add(current))
                break;

            // The VLU value: either a leaf or a subsequence to descend into
            Thought? valueRel = GetElementValue(current);

            if (valueRel is SeqElement s)
            {
                // Recursively enumerate the subsequence, passing the shared visitedSequences set
                foreach (var subElement in EnumerateSequenceElements(s, visitedSequences))
                    yield return subElement;
            }
            else
            {
                yield return current;
            }

            // Move to next node via NXT Link
            current = GetNextElement(current);
        }
        visitedSequences.Pop();
    }

    /// <summary>
    /// An element's value. An element can carry several values of different classes (a note and its
    /// duration, say); <paramref name="valueClass"/> selects one.
    /// </summary>
    /// <param name="s">The element.</param>
    /// <param name="valueClass">
    /// Optional: the value that is, or descends from, this class. Without it, the primary value (not one
    /// marked isSequenceMetadata).
    /// </param>
    /// <returns>The value, or null if there is none (or the class label is unknown).</returns>
    public Thought? GetElementValue(SeqElement? s, ThoughtRef valueClass = default)
    {
        if (s is null) return null;
        Thought? facetClass = valueClass.Find(s.Store);
        if (facetClass is null && !valueClass.IsEmpty) return null;
        // A class selects one facet by ancestry. Without one, the primary value is read;
        // metadata VLU targets do not add sequence positions or affect matching.
        return s.LinksTo.FirstOrDefault(link => link.LinkType == s.Store.Labeled("VLU") && link.To is not null &&
            (facetClass is null ? !link.To.HasProperty("isSequenceMetadata") : ReferenceEquals(link.To, facetClass) || link.To.HasAncestor(facetClass)))?.To;
    }

    /// <summary>True if the Thought is a sequence element.</summary>
    public bool IsSequenceElement(Thought? t) => t is SeqElement;

    /// <summary>True if the Thought is the first element of its sequence (its FRST is itself).</summary>
    public bool IsSequenceFirstElement(Thought? t) => t is SeqElement s && ReferenceEquals(s.FRST, s);

    /// <summary>
    /// Adds <paramref name="value"/> to a sequence after <paramref name="element"/>, or, with
    /// <paramref name="atBeginning"/>, at the front of the sequence whose first element is
    /// <paramref name="element"/>. The first element keeps its identity (its value moves to a new second
    /// element), so links to the sequence stay valid. An unknown label creates the value under Unknown.
    /// </summary>
    /// <returns>The element holding the new value.</returns>
    public SeqElement InsertAfter(SeqElement element, ThoughtRef value, bool atBeginning = false)
    {
        if (!ReferenceEquals(element.Store, this))
            throw new ArgumentException("Sequence belongs to another UKS.", nameof(element));
        if (atBeginning && !IsSequenceFirstElement(element))
            throw new ArgumentException("To insert at the beginning, give the first element.", nameof(element));
        Thought? t = value.GetOrAdd(this, "Unknown");
        SeqElement newNode = new(this)
        {
            Label = element.Label + "*", //the label will auto-increment.
            FRST = element.FRST,
            NXT = element.NXT,
        };
        element.NXT = newNode;
        if (!atBeginning)
        {
            newNode.AddLink("VLU", t);
            return newNode;
        }
        foreach (Link link in element.LinksTo.Where(link => link.LinkType == VluType).ToList())
        {
            newNode.AddLink(link.LinkType!, link.To);
            element.RemoveLink(link);
        }
        element.AddLink("VLU", t);
        return element;
    }

    /// <summary>
    /// Starts a new, unshared sequence of one element, to be extended with <see cref="InsertAfter"/>.
    /// Unlike <see cref="AddSequence"/>, it never reuses an existing sequence.
    /// </summary>
    /// <param name="labelBase">The element's label is this followed by -seq0.</param>
    /// <param name="value">The value; an unknown label creates it under Unknown.</param>
    public SeqElement CreateFirstElement(string labelBase, ThoughtRef value)
    {
        Thought? t = value.GetOrAdd(this, "Unknown");
        SeqElement firstNode = new(this)
        {
            Label = labelBase + "-seq0",
        };
        firstNode.AddLink("VLU", t);
        firstNode.FRST = firstNode; //points to itself as the first element
        return firstNode;
    }

    /// <summary>
    /// Deletes a sequence's elements, given its first element. Nothing is deleted if another Thought also
    /// refers to the sequence (it is shared) or if <paramref name="s"/> is not a first element.
    /// </summary>
    public void DeleteSequence(SeqElement? s)
    {
        if (s is not null && !ReferenceEquals(s.Store, this))
            throw new ArgumentException("Sequence belongs to another UKS.", nameof(s));
        //make sure there's only one reference to this sequence
        if (s is null || !IsSequenceFirstElement(s)) return;
        if (s.LinksFrom.Count(x => x.LinkType != FrstType) > 1) return;
        // Remove its cache entry, if it has one (an identical sequence may own the entry instead).
        IReadOnlyList<Thought> sequenceContent = FlattenSequence(s);
        if (SequenceCache.TryGetValue(sequenceContent, out Thought? cached) && ReferenceEquals(cached, s))
            SequenceCache.Remove(sequenceContent);
        //follow the chain and delete the elements.
        SeqElement? current = s;
        while (current is not null)
        {
            //This replicates DeleteThought but eliminates problems of re-entrance
            SeqElement? next = GetNextElement(current);
            //recursively delete subsequences which are no longer used anywhere else
            var subsequences = current.VLU?.LinksFrom.Where(x => x.LinkType != FrstType);
            if (subsequences?.Count() == 1 && subsequences.First().To is SeqElement s1)
                DeleteSequence(s1);
            foreach (Link lnk in current.LinksTo)
                current.RemoveLink(lnk);
            UnindexLabel(current.Label);
            lock (AtomicThoughts)
                AtomicThoughts.Remove(current);
            current = next;
        }
    }

    /// <summary>The owners of a sequence: the sources of links of the given type to its element <paramref name="s"/>.</summary>
    public IReadOnlyList<Thought> GetReferringThoughts(SeqElement s, ThoughtRef linkType) =>
        linkType.Find(this) is Thought type
            ? s.LinksFrom.Where(x => x.LinkType == type).Select(x => x.From!).ToList()
            : new List<Thought>();

    private static bool IsSequenceLastElement(SeqElement? s) => s is not null && s.NXT is null;

    private static SeqElement? GetNextElement(SeqElement s) => s.NXT;

    private static SeqElement? GetFirstElement(SeqElement s) => s.FRST;

    /// <summary>Follows NXT links to the last element of the sequence.</summary>
    internal SeqElement GetLastlement(SeqElement s)
    {
        while (s.NXT is SeqElement next) s = next;
        return s;
    }

    internal int GetSequenceLength(SeqElement firstNode)
    {
        return FlattenSequence(firstNode).Count;
    }

    internal void ReplaceDeletedSequenceValues(Thought value)
    {
        List<SeqElement> affectedElements = value.LinksFrom
            .Where(link => link.LinkType == VluType && link.From is SeqElement)
            .Select(link => (SeqElement)link.From!)
            .ToList();
        if (affectedElements.Count == 0) return;
        Thought unknownSequenceValue = GetOrAddThought("--")!;

        HashSet<SeqElement> affectedSequences = new(ReferenceEqualityComparer.Instance);
        foreach (SeqElement element in affectedElements)
            CollectAffectedSequence(element.FRST);

        Dictionary<SeqElement, IReadOnlyList<Thought>> oldCacheKeys =
            new(ReferenceEqualityComparer.Instance);
        foreach (SeqElement sequence in affectedSequences)
            oldCacheKeys[sequence] = FlattenSequence(sequence);

        // Only the deleted value's own link goes; the element's other values stay. An element left
        // without a value gets "--", so the sequence keeps its length.
        foreach (SeqElement element in affectedElements)
        {
            element.RemoveLink(Labeled("VLU")!, value);
            if (GetElementValue(element) is null) element.AddLink("VLU", unknownSequenceValue);
        }

        foreach ((SeqElement sequence, IReadOnlyList<Thought> oldCacheKey) in oldCacheKeys)
        {
            // Only a sequence the cache holds is re-keyed.
            if (!SequenceCache.TryGetValue(oldCacheKey, out Thought? cached) || !ReferenceEquals(cached, sequence)) continue;
            SequenceCache.Remove(oldCacheKey);
            SequenceCache[FlattenSequence(sequence)] = sequence;
        }

        void CollectAffectedSequence(SeqElement? sequence)
        {
            if (sequence is null || !affectedSequences.Add(sequence)) return;

            foreach (Link reference in sequence.LinksFrom
                .Where(link => link.LinkType == VluType && link.From is SeqElement))
            {
                SeqElement? outerSequence = ((SeqElement)reference.From!).FRST;
                CollectAffectedSequence(outerSequence);
            }
        }
    }

    //This unconditionally creates a sequence of Thoughts
    //No checking for existing sequences, no subsequences detection
    private SeqElement? CreateRawSequence(List<Thought> targets, string baseLabel = "seq*")
    {
        if (targets.Count < 1) return null;
        Thought newTarget = targets[0];

        SeqElement firstElement = CreateFirstElement(baseLabel, newTarget);
        SeqElement prevElement = firstElement;

        for (int i = 1; i < targets.Count; i++)
        {
            newTarget = targets[i];
            SeqElement newElement = InsertAfter(prevElement, newTarget);
            prevElement = newElement;
        }
        return firstElement;
    }

    //the sequence cache
    private readonly Dictionary<IReadOnlyList<Thought>, Thought> SequenceCache = new(new ThoughtListComparer());

    internal void ClearSequenceCache()
    {
        SequenceCache.Clear();
    }

    // Reference-only comparer for a list of Thought
    sealed class ThoughtListComparer : IEqualityComparer<IReadOnlyList<Thought>>
    {
        public bool Equals(IReadOnlyList<Thought>? sequenceContent, IReadOnlyList<Thought>? y)
        {
            if (ReferenceEquals(sequenceContent, y)) return true;
            if (sequenceContent is null || y is null || sequenceContent.Count != y.Count) return false;
            for (int i = 0; i < sequenceContent.Count; i++)
                if (!ReferenceEquals(sequenceContent[i], y[i])) return false;
            return true;
        }

        public int GetHashCode(IReadOnlyList<Thought> list)
        {
            if (list is null) return 0;
            var hc = new HashCode();
            foreach (var t in list) hc.Add(t); // reference-based hash
            return hc.ToHashCode();
        }
    }

    internal float CompareSequences(SeqElement seq1, SeqElement seq2)
    {
        //TODO make this non-digital
        var flat1 = FlattenSequence(seq1);
        var flat2 = FlattenSequence(seq2);
        if (flat1.Count != flat2.Count) return 0f;
        for (int i = 0; i < flat1.Count; i++)
            if (!ReferenceEquals(flat1[i], flat2[i]))
                return 0f;
        return 1f;
    }

    /// <summary>
    /// Replace a plain Thought with a SeqElement while preserving label, value, weight, and links.
    /// </summary>
    internal SeqElement? PromoteToSeqElement(Thought? t)
    {
        if (t is null) return null;
        if (!ReferenceEquals(t.Store, this))
            throw new ArgumentException("Cannot promote a Thought from another UKS.", nameof(t));
        if (t is SeqElement sExisting) return sExisting;
        UnindexLabel(t.Label);

        var seq = new SeqElement(this)
        {
            Label = t.Label,   // keeps label mapping
            Value = t.Value,
            Weight = t.Weight
        };

        // Move outgoing links
        foreach (var link in t.LinksToWriteable.ToList())
        {
            link.From = seq;
            seq.LinksToWriteable.Add(link);
        }
        t.LinksToWriteable.Clear();

        // Move incoming links
        foreach (var link in t.LinksFromWriteable.ToList())
        {
            link.To = seq;
            seq.LinksFromWriteable.Add(link);
        }
        t.LinksFromWriteable.Clear();
        //At this time...   Sequence elements are always parentless
        t.RemoveParent("Unknown");

        // Sequence nodes are implementation details, not atomic Thoughts.  A
        // promoted node commonly started life in AtomicThoughts while an XML or
        // text loader was resolving forward references, but it must leave that
        // collection once its VLU link identifies it as a sequence element.
        lock (AtomicThoughts)
        {
            AtomicThoughts.Remove(t);
            AtomicThoughts.Remove(seq);
        }
        t.Delete();

        IndexLabel(seq.Label, seq);
        return seq;
    }
}

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
/// One element of a sequence: a Thought with a FRST link to the sequence's first element,
/// an NXT link to the next element (none on the last), and a VLU link to its value.
/// </summary>
public class SeqElement : Thought
{
    /// <summary>Creates an unlinked sequence element.</summary>
    public SeqElement(UKS? store = null) : base(store) { }

    /// <summary>The first element of this element's sequence (itself, for the first element).</summary>
    public SeqElement? FRST
    {
        get => LinksToWriteable.FindFirst(x => x.LinkType?.Label == "FRST")?.To as SeqElement;
        set => SetSequenceLink("FRST", value);
    }

    /// <summary>The next element of the sequence, or null for the last element.</summary>
    public SeqElement? NXT
    {
        get => LinksTo.FindFirst(x => x.LinkType?.Label == "NXT")?.To as SeqElement;
        set => SetSequenceLink("NXT", value);
    }

    // Replaces this element's single FRST or NXT link.
    private void SetSequenceLink(string linkTypeLabel, SeqElement? value)
    {
        if (value is not null && !ReferenceEquals(value.Store, Store))
            throw new ArgumentException("Sequence values must belong to the same UKS.", nameof(value));
        RemoveLinks(Store.Labeled(linkTypeLabel)!);
        if (value is not null)
            AddLink(Store.GetOrAddThought(linkTypeLabel, "LinkType")!, value);
    }

    /// <summary>The element's primary value (a non-metadata VLU target), or null.</summary>
    public Thought? VLU
    {
        get => Store.GetElementValue(this);
        set
        {
            if (value is not null && !ReferenceEquals(value.Store, Store))
                throw new ArgumentException("Sequence values must belong to the same UKS.", nameof(value));
            // Replace the primary value without discarding timing/other metadata.
            bool metadata = value?.HasProperty("isSequenceMetadata") == true;
            foreach (var link in LinksTo.Where(link => link.LinkType == Store.Labeled("VLU") &&
                (link.To?.HasProperty("isSequenceMetadata") == true) == metadata).ToList()) RemoveLink(link);
            if (value is not null)
                AddLink(Store.GetOrAddThought("VLU", "LinkType")!, value);
        }
    }
    /// <summary>Returns the sequence in text-file notation, e.g. ^c a t.</summary>
    public override string ToString() => "^" + string.Join(" ", Store.FlattenSequence(this));
}

public partial class UKS
{
    public const string CharacterPrefix = "c:";

    public static string GetCharacterLabel(char character) =>
        CharacterPrefix + char.ToUpperInvariant(character);

    //The structure of a sequence is a series (linked list) of elements, each with 3 links.
    //"NXT" with a To of the next element in the sequence
    //"VLU" to the actual Thought in the sequence
    //"FRST" which points to the first element of the list
    //the "owner" of the sequences had a link of LinkType (spelled e.g.) to the first element in the sequence
    //the last element in the sequence has no link of LinkType "NXT"
    //Example, to represent the spelling of "CAT":
    // [cat -> spelled -> seq0]
    // seq0 ->NXT--> seq1
    // seq0 ->FRST-> seq0
    // seq0 ->VLU--> C
    // seq1 ->NXT -> seq2
    // seq1 ->FRST-> seq0
    // seq1 ->VLU--> A
    // seq2 ->FRST-> seq0
    // seq2 ->VLU--> T
    // NOTE: the elements seq* need not have labels at all, they are just used here for clarity
    // each seq* element must also have a FRST releationship back to the owner Thought
    // The Thought ToString() method will automatically follow the sequences and return cat->spelled->^cat
    // VLU targets may be other sequences
    //the last element has no NXT link

    // A few Special cases can be detected by comparing targets
    // Is a sequence element:  Has a FRST link
    // Start of sequence:  seq->NXT = seq->FRST
    // End of sequence:    seq->NXT = null 

    //TODO:
    // add circular sequences (search can start at any location in the sequence)
    // search with errors and scoring: First/Last are correct, Elements out of order, Elements near others

    /// <summary>
    /// Determines whether a Thought participates as a sequence element (has a FRST link).
    /// </summary>
    /// <param name="t">Thought to test.</param>
    /// <returns>True if the thought has a FRST link; otherwise false.</returns>
    public bool IsSequenceElement(Thought? t) => t is SeqElement;

    /// <summary>True if the Thought is the first element of its sequence (its FRST is itself).</summary>
    public bool IsSequenceFirstElement(Thought? t) => t is SeqElement s && ReferenceEquals(s.FRST, s);

    private static bool IsSequenceLastElement(SeqElement? s) => s is not null && s.NXT is null;

    private static SeqElement? GetNextElement(SeqElement s) => s.NXT;

    private static SeqElement? GetFirstElement(SeqElement s) => s.FRST;

    /// <summary>Follows NXT links to the last element of the sequence.</summary>
    public SeqElement GetLastlement(SeqElement s)
    {
        while (s.NXT is SeqElement next) s = next;
        return s;
    }

    /// <summary>
    /// Gets a primary VLU value, or a VLU value belonging to the requested class.
    /// </summary>
    /// <param name="s">Sequence element to read.</param>
    /// <param name="valueClass">Optional ancestor class of the requested VLU facet; null selects a non-metadata value.</param>
    /// <returns>The selected VLU value, or null if none.</returns>
    public Thought? GetElementValue(SeqElement? s, Thought? valueClass = null)
    {
        if (s is null) return null;
        // A class selects one facet by ancestry. Without one, the primary value is read;
        // metadata VLU targets do not add sequence positions or affect matching.
        return s.LinksTo.FirstOrDefault(link => link.LinkType == s.Store.Labeled("VLU") && link.To is not null &&
            (valueClass is null ? !link.To.HasProperty("isSequenceMetadata") : ReferenceEquals(link.To, valueClass) || link.To.HasAncestor(valueClass)))?.To;
    }
    public int GetSequenceLength(SeqElement firstNode)
    {
        return FlattenSequence(firstNode).Count;
    }

    /// <summary>
    /// Inserts a new element at the beginning of the sequence, shifting the previous first element forward.
    /// </summary>
    /// <param name="prevElementIn">Current first sequence element.</param>
    /// <param name="value">Value to insert as the new first element.</param>
    /// <returns>The (updated) first element of the sequence.</returns>
    public SeqElement InsertElement(SeqElement prevElementIn, Thought value)
    {
        if (!ReferenceEquals(prevElementIn.Store, this) || (value is not null && !ReferenceEquals(value.Store, this)))
            throw new ArgumentException("Sequence values must belong to this UKS.");
        SeqElement first = prevElementIn;
        if (IsSequenceFirstElement(prevElementIn))
        {
            //this is a bit tricky...
            //it actually adds a 2nd element but then copies old 1st element values to the 2nd element and puts the new value on the old first
            //why? all the FRST links and external pointers to the sequence will still be correct without modification
            SeqElement newNode = new(this)
            {
                Label = prevElementIn.Label + "*", //the label will auto-increment.
                FRST = first,
                NXT = first.NXT,
            };
            foreach (var link in first.LinksTo.Where(link => link.LinkType == Labeled("VLU")).ToList())
            {
                newNode.AddLink(link.LinkType!, link.To);
                first.RemoveLink(link);
            }
            first.AddLink("VLU", value);
            first.NXT = newNode;
        }
        else
        {
            SeqElement? predecessor = prevElementIn.FRST;
            while (predecessor?.NXT is not null && predecessor.NXT != prevElementIn)
                predecessor = predecessor.NXT;
            if (predecessor is null || predecessor.NXT != prevElementIn)
                throw new ArgumentException("prevElementIn is not in its FRST chain", nameof(prevElementIn));

            SeqElement newNode = new(this)
            {
                Label = prevElementIn.Label + "*",
                FRST = prevElementIn.FRST,
                NXT = prevElementIn,
            };
            newNode.AddLink("VLU", value);
            predecessor.NXT = newNode;
            return prevElementIn.FRST ?? first;
        }
        return first;
    }

    //Adds a new element after prevElementIn) and links its value. Returns the new element.
    public SeqElement AddElement(SeqElement prevElement, Thought value)
    {
        if (!ReferenceEquals(prevElement.Store, this) || (value is not null && !ReferenceEquals(value.Store, this)))
            throw new ArgumentException("Sequence values must belong to this UKS.");
        SeqElement newNode = new(this)
        {
            Label = prevElement.Label + "*",
            FRST = prevElement.FRST,
            NXT = prevElement.NXT,
        };  //the label will auto-increment.
        newNode.AddLink("VLU", value);
        prevElement.NXT = newNode;
        return newNode;
    }
    /// <summary>
    /// Creates the first sequence element for a source Thought and links its value.
    /// </summary>
    public SeqElement CreateFirstElement(string labelBase, Thought value)
    {
        if (value is not null && !ReferenceEquals(value.Store, this))
            throw new ArgumentException("Sequence value belongs to another UKS.", nameof(value));
        SeqElement firstNode = new(this)
        {
            Label = labelBase + "-seq0",
        };
        firstNode.AddLink("VLU", value);
        firstNode.FRST = firstNode; //points to itself as the first element
        return firstNode;
    }
    /// <summary>Pops a streaming queue head (FRST is null), returning its successor.</summary>
    public SeqElement? RemoveFirstSequenceElement(SeqElement first)
    {
        if (!ReferenceEquals(first.Store, this))
            throw new ArgumentException("Sequence belongs to another UKS.", nameof(first));
        if (first.FRST is not null) throw new ArgumentException("Expected a streaming queue element.", nameof(first));
        SeqElement? next = first.NXT;
        foreach (Link link in first.LinksTo) first.RemoveLink(link);
        first.Delete();
        return next;
    }

    /// <summary>Finds the preceding element whose NXT points to this element.</summary>
    public SeqElement? GetPreviousSequenceElement(SeqElement? element)
    {
        SeqElement? result = null;
        if (element is null) return result;
        foreach (Link link in element.LinksFrom)
        {
            if (link.LinkType?.Label != "NXT" || link.From is not SeqElement predecessor) continue;
            if (!ReferenceEquals(predecessor.FRST, element.FRST)) continue;
            result = predecessor;
            break;
        }
        return result;
    }

    /// <summary>
    /// Detaches and deletes the oldest element, leaving the first element intact.
    /// Deletes the sequence node only, never its VLU Thought.
    /// </summary>
    public SeqElement? RemoveLastSequenceElement(SeqElement? first)
    {
        if (first is not null && !ReferenceEquals(first.Store, this))
            throw new ArgumentException("Sequence belongs to another UKS.", nameof(first));
        SeqElement? result = null;
        if (first is null) return result;
        if (!IsSequenceFirstElement(first))
            throw new ArgumentException("Expected the first sequence element.", nameof(first));
        if (first.NXT is null) return result;
        List<Thought> oldValues = FlattenSequence(first);
        bool wasCached = SequenceCache.TryGetValue(oldValues, out Thought? cached) &&
            ReferenceEquals(cached, first);
        if (wasCached) SequenceCache.Remove(oldValues);
        SeqElement predecessor = first;
        while (predecessor.NXT!.NXT is not null) predecessor = predecessor.NXT;
        result = predecessor.NXT!;
        predecessor.NXT = null;
        // Detach FRST before Delete: otherwise its root reference could delete
        // the whole sequence through Thought.Delete's sequence-owner handling.
        result.FRST = null;
        result.RemoveLinks(Labeled("VLU")!);
        result.Delete();
        if (wasCached)
        {
            List<Thought> remainingValues = FlattenSequence(first);
            SequenceCache[remainingValues] = first;
        }
        return result;
    }

    public void DeleteSequence(SeqElement? s)
    {
        if (s is not null && !ReferenceEquals(s.Store, this))
            throw new ArgumentException("Sequence belongs to another UKS.", nameof(s));
        //make sure there's only one reference to this sequence
        if (s is null || !IsSequenceFirstElement(s)) return;
        if (s.LinksFrom.Count(x => x.LinkType?.Label != "FRST") > 1) return;
        if (!s.Label.StartsWith("thequery"))  //queries don't have cache entries
        {        //delete it from the cache
            var sequenceContent = FlattenSequence(s);
            SequenceCache.Remove(sequenceContent);
        }
        //follow the chain and delete the elements.
        SeqElement? current = s;
        while (current is not null)
        {
            //This replicates DeleteThought but eliminates problems of re-entrance
            SeqElement? next = GetNextElement(current);
            //recursively delete subsequences which are no longer used anywhere else
            var subsequences = current.VLU?.LinksFrom.Where(x => x.LinkType?.Label != "FRST");
            if (subsequences?.Count() == 1 && subsequences.First().To is SeqElement s1)
                DeleteSequence(s1);
            foreach (Link lnk in current.LinksTo)
                current.RemoveLink(lnk);
            ThoughtLabels.RemoveThoughtLabel(current.Label, this);
            lock (AtomicThoughts)
                AtomicThoughts.Remove(current);
            current = next;
        }
    }

    internal void ReplaceDeletedSequenceValues(Thought value)
    {
        Thought unknownSequenceValue = GetOrAddThought("--")!;
        List<SeqElement> affectedElements = value.LinksFrom
            .Where(link => link.LinkType?.Label == "VLU" && link.From is SeqElement)
            .Select(link => (SeqElement)link.From!)
            .ToList();

        HashSet<SeqElement> affectedSequences = new(ReferenceEqualityComparer.Instance);
        foreach (SeqElement element in affectedElements)
            CollectAffectedSequence(element.FRST);

        Dictionary<SeqElement, List<Thought>> oldCacheKeys =
            new(ReferenceEqualityComparer.Instance);
        foreach (SeqElement sequence in affectedSequences)
            oldCacheKeys[sequence] = FlattenSequence(sequence);

        foreach (SeqElement element in affectedElements)
            if (value.HasProperty("isSequenceMetadata")) element.RemoveLink(Labeled("VLU")!, value);
            else element.VLU = unknownSequenceValue;

        foreach ((SeqElement sequence, List<Thought> oldCacheKey) in oldCacheKeys)
        {
            if (sequence.Label.StartsWith("thequery")) continue;
            SequenceCache.Remove(oldCacheKey);
            SequenceCache[FlattenSequence(sequence)] = sequence;
        }

        void CollectAffectedSequence(SeqElement? sequence)
        {
            if (sequence is null || !affectedSequences.Add(sequence)) return;

            foreach (Link reference in sequence.LinksFrom
                .Where(link => link.LinkType?.Label == "VLU" && link.From is SeqElement))
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
            SeqElement newElement = AddElement(prevElement, newTarget);
            prevElement = newElement;
        }
        return firstElement;
    }

    //the sequence cache
    private readonly Dictionary<IReadOnlyList<Thought>, Thought> SequenceCache = new(new ThoughtListComparer());
    public void ClearSequenceCache()
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
    public SeqElement? AddSequence(string label, List<Thought>? targets, bool allowCompression = true)
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
    /// Adds a sequence of Thoughts as ordered links from a source Thought. Handles nested sequences.
    /// </summary>
    /// <param name="source">The 'owner' of the sequence.</param>
    /// <param name="linkType">The link type to use for the sequence link.</param>
    /// <param name="targets">Targets in order; can be sequence start nodes.</param>
    /// <param name="baseWeight">Base weight for the links (currently unused).</param>
    /// <param name="allowCompression">Whether existing subsequences may replace flat target ranges.</param>
    /// <returns>The first node of the created or reused sequence, or null if insufficient targets.</returns>
    public SeqElement? AddSequenceAndLink(
        Thought source,
        Thought? linkType,
        List<Thought> targets,
        float baseWeight = 1.0f,
        bool allowCompression = true)
    {
        if (targets is null || targets.Count == 0) return null;

        if (!ReferenceEquals(source.Store, this) || (linkType is not null && !ReferenceEquals(linkType.Store, this)) ||
            targets.Any(value => value is not null && !ReferenceEquals(value.Store, this)))
            throw new ArgumentException("Sequence and its links must belong to this UKS.");

        // Replace any existing sequence link of this type. TODO: delete the old sequence.
        if (linkType is not null) source.RemoveLinks(linkType);
        SeqElement rawSequence = AddSequence(source.Label, targets, allowCompression)!;
        if (linkType is not null) source.AddLink(linkType, rawSequence);
        return rawSequence;
    }

    /// <summary>
    /// Gets the canonical Thought for a written character.
    /// </summary>
    public Thought GetOrAddCharacter(char character)
    {
        Thought characterRoot = GetOrAddThought("character", "Abstract")!;
        return GetOrAddThought(GetCharacterLabel(character), characterRoot)!;
    }

    /// <summary>
    /// Creates a canonical character sequence and links it to its owner as its spelling.
    /// </summary>
    public SeqElement? CreateSpellingSequence(string spelling, Thought? owner)
    {
        if (owner is null || string.IsNullOrEmpty(spelling)) return null;

        List<Thought> characters = spelling
            .Select(GetOrAddCharacter)
            .ToList();
        Thought spelled = GetOrAddThought("spelled", "LinkType")!;
        return AddSequenceAndLink(owner, spelled, characters);
    }

    /// <summary>The source of the first link of the given type to this sequence element, or null.</summary>
    public Thought? GetReferrer(SeqElement seqNode, Thought linkType) =>
        seqNode.LinksFrom.FirstOrDefault(x => x.LinkType == linkType)?.From;

    /// <summary>
    /// Flatten a sequence into a list of leaf Thoughts (letters). Handles nested sequences via VLU.
    /// </summary>
    public List<Thought> FlattenSequence(Thought? sequenceStart, bool skipPlusValues = false)
    {
        List<Thought> result = new();
        foreach (SeqElement element in EnumerateSequenceElements(sequenceStart as SeqElement))
            if (GetElementValue(element) is Thought value)
                result.Add(value);
        return result;
    }

    public float CompareSequences(SeqElement seq1, SeqElement seq2)
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
    /// Enumerates all leaf elements in a sequence, recursively traversing into subsequences.
    /// Protected against circular subsequence references.
    /// </summary>
    /// <param name="sequenceStart">The first node of the sequence.</param>
    /// <param name="visitedSequences">Optional stack to track visited sequences across recursion.</param>
    /// <returns>Leaf sequence elements in order.</returns>
    public IEnumerable<SeqElement> EnumerateSequenceElements(SeqElement? sequenceStart, Stack<SeqElement>? visitedSequences = null)
    {
        if (sequenceStart is null) yield break;

        // Initialize visited sequences tracker if this is the top-level call
        if (visitedSequences is null) visitedSequences = new();
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

    /// <summary>The sources of all links of the given type to this sequence element.</summary>
    public List<Thought> GetReferringThoughts(SeqElement s, Thought linkType) =>
        s.LinksFrom.Where(x => x.LinkType == linkType).Select(x => x.From!).ToList();

    /// <summary>
    /// Replace a plain Thought with a SeqElement while preserving label, value, weight, and links.
    /// </summary>
    public SeqElement? PromoteToSeqElement(Thought? t)
    {
        if (t is null) return null;
        if (!ReferenceEquals(t.Store, this))
            throw new ArgumentException("Cannot promote a Thought from another UKS.", nameof(t));
        if (t is SeqElement sExisting) return sExisting;
        ThoughtLabels.RemoveThoughtLabel(t.Label, this);

        var seq = new SeqElement(this)
        {
            Label = t.Label,   // keeps label mapping
            V = t.V,
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

        ThoughtLabels.AddThoughtLabel(seq.Label, seq);
        return seq;
    }

    //This essentially keeps track of the activation of the various sequence elements as the search progresses through the pattern.
    private class SequenceSearchState
    {
        public SeqElement FirstMatchElement = null!;
        public SeqElement LastMatchElement = null!;
        public SeqElement? CurPos;
        public Stack<SeqElement> ReturnStack = new();
        public float Confidence = 1.0f;
    }

    /// <summary>
    /// Searches directly from input sequence elements. valueClass selects a VLU facet
    /// (for example, note or duration) in both input and stored sequences. alternativeClass
    /// prefers an input VLU of that class and consumes its contiguous covered run as one value.
    /// matchClass restricts results to sequences referred to by Thoughts of that class.
    /// </summary>
    public List<(SeqElement seqNode, float confidence)> FindSequencesByActivation(
        SeqElement? sequence, Thought? searchOptions = null, bool getFirstElements = true, SeqElement? last = null,
        Thought? valueClass = null, Thought? alternativeClass = null, Thought? matchClass = null)
    {
        List<(SeqElement seqNode, float confidence)> results = new();
        searchOptions ??= Labeled("ExactSequenceSearch");
        if (sequence is null || searchOptions is null) return results;
        if (!getFirstElements && (searchOptions.HasProperty("allowWildcards") || searchOptions.HasProperty("allowNestedSequences")))
            throw new ArgumentException("Occurrence locations require a flat, literal search.", nameof(getFirstElements));

        List<SequenceSearchState>? active = null;
        SeqElement? skipThrough = null;
        int count = 0;
        var inputElements = EnumerateSequenceElements(sequence);
        foreach (SeqElement input in inputElements)
        {
            if (skipThrough is not null)
            {
                if (ReferenceEquals(input, skipThrough)) skipThrough = null;
                continue;
            }
            Thought? value = GetElementValue(input, valueClass);
            if (value is null && IsWildcardPatternElement(input.VLU, searchOptions)) value = input.VLU;
            Thought? alternative = alternativeClass is null ? null : GetElementValue(input, alternativeClass);
            SeqElement coveredEnd = input;
            if (alternative is not null)
            {
                value = alternative;
                while (coveredEnd != last && coveredEnd.NXT is SeqElement next &&
                    ReferenceEquals(GetElementValue(next, alternativeClass), alternative)) coveredEnd = next;
                if (!ReferenceEquals(coveredEnd, input)) skipThrough = coveredEnd;
            }
            // Preserve the existing cardinality matcher for explicit wildcard input.
            if (IsWildcardPatternElement(value, searchOptions))
            {
                var wildcardPattern = new List<Thought>();
                var elements = EnumerateSequenceElements(sequence);
                foreach (SeqElement element in elements)
                {
                    Thought? selected = GetElementValue(element, valueClass);
                    if (selected is null && IsWildcardPatternElement(element.VLU, searchOptions)) selected = element.VLU;
                    wildcardPattern.Add(selected!); // a null element matches nothing
                    if (ReferenceEquals(element, last)) break;
                }
                results = FindSequencesByActivation(wildcardPattern, searchOptions, getFirstElements, valueClass, matchClass);
                return results;
            }
            if (active is null) active = InitializeSequenceSearchByActivation(value, 0, searchOptions, valueClass);
            else active = ActivateNextSequenceElements(active, value, count, searchOptions, valueClass);
            count++;
            if (active.Count == 0 || ReferenceEquals(coveredEnd, last)) break;
        }
        results = CollectSequenceSearchResults(active, searchOptions, count, getFirstElements, matchClass);
        return results;
    }
    /// <summary>
    /// Searches for pattern activation. Returns sequence roots, or individual stream positions when FRST is null.
    /// getFirstElements=false also returns individual positions within ordinary flat sequences.
    /// The allowTrailingInput search property retains completed stored sequences when input continues.
    /// With mustMatchFirst and mustMatchLast, this returns complete templates matching an input prefix.
    /// </summary>
    public List<(SeqElement seqNode, float confidence)> FindSequencesByActivation(
        List<Thought> pattern, Thought? searchOptions = null,
        bool getFirstElements = true, Thought? valueClass = null, Thought? matchClass = null)
    {
        List<(SeqElement seqNode, float confidence)> retVal = new();
        if (pattern is null || pattern.Count == 0) return retVal;

        searchOptions ??= Labeled("ExactSequenceSearch");
        if (searchOptions is null) return retVal;
        if (searchOptions.HasProperty("allowOutOfOrder"))
        {
            retVal = FindSequencesOutOfOrder(pattern, searchOptions);
            if (matchClass is not null)
                retVal.RemoveAll(match => !match.seqNode.LinksFrom.Any(link => link.From?.HasAncestor(matchClass) == true));
            return retVal;
        }
        if (!getFirstElements &&
            (searchOptions.HasProperty("allowWildcards") || searchOptions.HasProperty("allowNestedSequences")))
            throw new ArgumentException("Occurrence locations require a flat, literal search.", nameof(getFirstElements));

        if (searchOptions.HasProperty("allowWildcards") &&
            pattern.Any(x => IsWildcardPatternElement(x, searchOptions)))
        {
            retVal = FindExactWildcardMatches(pattern, valueClass);
            if (matchClass is not null)
                retVal.RemoveAll(match => !match.seqNode.LinksFrom.Any(link => link.From?.HasAncestor(matchClass) == true));
            return retVal;
        }

        int seedPatternIndex = pattern.FindIndex(p => !IsWildcardPatternElement(p, searchOptions));
        if (seedPatternIndex < 0) return retVal;

        // Step 1: seed from the first non-wildcard pattern element.
        List<SequenceSearchState> activeElements = InitializeSequenceSearchByActivation(pattern[seedPatternIndex], seedPatternIndex, searchOptions, valueClass);

        // Step 2: propagate through NXT links for each remaining pattern element.
        for (int i = seedPatternIndex + 1; i < pattern.Count; i++)
        {
            activeElements = ActivateNextSequenceElements(activeElements, pattern[i], i, searchOptions, valueClass);
            if (activeElements.Count == 0) break;
        }
        // Step 3: collect matching sequence starts and confidence values.
        retVal = CollectSequenceSearchResults(activeElements, searchOptions, pattern.Count, getFirstElements, matchClass);

        return retVal;
    }

    /// <summary>
    /// Finds complete flat stored sequences and partial stream matches within an input range.
    /// Seeds once per start and extends the same activation states. last is exclusive.
    /// </summary>
    public List<(SeqElement start, SeqElement end, SeqElement match)> FindSequenceSpansByActivation(
        SeqElement first, SeqElement last, Thought searchOptions)
    {
        if (searchOptions.HasProperty("allowWildcards") || searchOptions.HasProperty("allowNestedSequences"))
            throw new ArgumentException("Span search requires flat literal matching.", nameof(searchOptions));
        List<(SeqElement start, SeqElement end, SeqElement match)> matches = new();
        for (SeqElement? start = first; start is not null && start != last; start = start.NXT)
        {
            var active = InitializeSequenceSearchByActivation(start.VLU, 0, searchOptions);
            // Internal positions of stored patterns cannot become complete matches as we extend.
            for (int index = active.Count - 1; index >= 0; index--)
                if (active[index].FirstMatchElement.FRST is not null &&
                    !ReferenceEquals(active[index].FirstMatchElement.FRST, active[index].FirstMatchElement)) active.RemoveAt(index);
            int length = 0;
            for (SeqElement? end = start; end is not null && end != last && active.Count > 0; end = end.NXT)
            {
                if (length > 0) active = ActivateNextSequenceElements(active, end.VLU, length, searchOptions);
                length++;
                foreach (var state in active)
                {
                    bool stream = state.FirstMatchElement.FRST is null;
                    if ((!stream || searchOptions.HasProperty("mustMatchLast")) && state.LastMatchElement.NXT is not null) continue;
                    matches.Add((start, end, state.FirstMatchElement));
                }
            }
        }
        return matches;
    }

    private List<(SeqElement seqNode, float confidence)> FindExactWildcardMatches(List<Thought> pattern, Thought? valueClass = null)
    {
        Thought? anchor = pattern
            .Where(x => !new[] { "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard" }.Any(x.HasProperty))
            .OrderBy(x => x.LinksFrom.Count(link => link.LinkType?.Label == "VLU"))
            .FirstOrDefault();
        if (anchor is null) return new List<(SeqElement seqNode, float confidence)>();

        HashSet<SeqElement> candidates = new();
        foreach (Link link in anchor.LinksFrom)
        {
            if (link.LinkType?.Label != "VLU" || link.From is not SeqElement element) continue;
            AddEnclosingSequenceRoots(element.FRST, candidates);
        }

        var results = new List<(SeqElement seqNode, float confidence)>();
        foreach (SeqElement candidate in candidates)
        {
            // The cardinality matcher already uses a list; select the requested VLU facet.
            var sequence = new List<Thought?>();
            var elements = EnumerateSequenceElements(candidate);
            foreach (SeqElement element in elements) sequence.Add(GetElementValue(element, valueClass));
            bool matches = ExactWildcardPatternMatches(pattern, sequence);
            if (matches) results.Add((candidate, 1.0f));
        }
        return results;
    }

    private void AddEnclosingSequenceRoots(SeqElement? sequenceRoot, HashSet<SeqElement> candidates)
    {
        if (sequenceRoot is null || !candidates.Add(sequenceRoot)) return;
        foreach (Link link in sequenceRoot.LinksFrom)
        {
            if (link.LinkType?.Label == "VLU" && link.From is SeqElement caller)
                AddEnclosingSequenceRoots(caller.FRST, candidates);
        }
    }

    private static bool ExactWildcardPatternMatches(List<Thought> pattern, List<Thought?> sequence)
    {
        HashSet<int> activePositions = new() { 0 };
        foreach (Thought patternElement in pattern)
        {
            HashSet<int> nextPositions = new();
            foreach (int position in activePositions)
            {
                if (!new[] { "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard" }.Any(patternElement.HasProperty))
                {
                    if (position < sequence.Count && ReferenceEquals(patternElement, sequence[position]))
                        nextPositions.Add(position + 1);
                    continue;
                }

                if (patternElement.HasProperty("isOptionalWildcard"))
                {
                    nextPositions.Add(position);
                    if (position < sequence.Count && WildcardMatchesValue(patternElement, sequence[position]))
                        nextPositions.Add(position + 1);
                    continue;
                }

                bool allowZero = patternElement.HasProperty("is*Wildcard");
                bool allowMany = allowZero || patternElement.HasProperty("is+Wildcard");
                if (allowMany)
                {
                    if (allowZero) nextPositions.Add(position);
                    int current = position;
                    while (current < sequence.Count && WildcardMatchesValue(patternElement, sequence[current]))
                    {
                        current++;
                        nextPositions.Add(current);
                    }
                    continue;
                }

                if (patternElement.HasProperty("isWildcard") && position < sequence.Count &&
                    WildcardMatchesValue(patternElement, sequence[position]))
                    nextPositions.Add(position + 1);
            }

            activePositions = nextPositions;
            if (activePositions.Count == 0) return false;
        }

        return activePositions.Contains(sequence.Count);
    }

    private static bool WildcardMatchesValue(Thought? wildcard, Thought? value)
    {
        if (wildcard is null || value is null ||
            !new[] { "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard" }.Any(wildcard.HasProperty)) return false;
        List<Thought> constraints = wildcard.AncestorsWithSelf
            .Where(ancestor => new[] { "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard" }.Any(ancestor.HasProperty))
            .SelectMany(ancestor => ancestor.Parents)
            .Where(parent => !new[] { "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard" }.Any(parent.HasProperty) &&
                !ReferenceEquals(parent, wildcard.Store.Labeled("Property")) &&
                !ReferenceEquals(parent, wildcard.Store.Labeled("Thought")) &&
                !ReferenceEquals(parent, wildcard.Store.Labeled("Unknown")))
            .ToList();
        return constraints.Count == 0 || constraints.Any(value.HasAncestor);
    }

    /// <summary>
    /// Creates a search options Thought with specified properties for sequence searching.
    /// </summary>
    public Thought CreateSearchOptions(bool mustMatchFirst = false, bool mustMatchLast = false,
        bool allowWildcard = false, bool allowNestedSequences = false, bool allowTrailingInput = false)
    {
        Thought searchOptions = GetOrAddThought("SearchOption*", "SearchOption")!;

        if (mustMatchFirst)
            searchOptions.AddLink("hasProperty", GetOrAddThought("mustMatchFirst"));
        if (mustMatchLast)
            searchOptions.AddLink("hasProperty", GetOrAddThought("mustMatchLast"));
        if (allowWildcard)
            searchOptions.AddLink("hasProperty", GetOrAddThought("allowWildcards"));
        if (allowNestedSequences)
            searchOptions.AddLink("hasProperty", GetOrAddThought("allowNestedSequences"));
        if (allowTrailingInput)
            searchOptions.AddLink("hasProperty", GetOrAddThought("allowTrailingInput", "SearchOption"));

        return searchOptions;
    }

    private List<SequenceSearchState> InitializeSequenceSearchByActivation(Thought? firstPatternElement, int patternIndex, Thought searchOptions, Thought? valueClass = null)
    {
        List<SequenceSearchState> activeElements = new();
        if (firstPatternElement is null || searchOptions is null) return activeElements;

        bool mustMatchFirst = searchOptions.HasProperty("mustMatchFirst");
        bool allowNestedSequences = searchOptions.HasProperty("allowNestedSequences");

        // Class wildcards are children of the input's classes. Property also
        // contains unrestricted wildcards, which have no class parent to follow.
        IEnumerable<Link> startingLinks = firstPatternElement.LinksFrom;
        if (searchOptions.HasProperty("allowWildcards"))
        {
            IEnumerable<Thought> wildcardCandidates = firstPatternElement.AncestorsWithSelf
                .Append(Labeled("Property")).OfType<Thought>()
                .SelectMany(parent => parent.Children);
            // An unclassified word has no noun/adjective/etc. ancestry yet.
            // Follow declarations of the wildcard properties, wherever those Thoughts
            // are grouped, rather than relying on Property parentage or scanning all Thoughts.
            if (Labeled("unknownWordType") is Thought unknownWordType && firstPatternElement.HasAncestor(unknownWordType))
                wildcardCandidates = wildcardCandidates.Concat(
                    new[] { "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard" }
                        .Select(Labeled).OfType<Thought>().SelectMany(property => property.LinksFrom)
                        .Where(link => link.LinkType == Labeled("hasProperty") && link.From is not null)
                        .SelectMany(link => link.From!.Descendants.Prepend(link.From!)));
            startingLinks = startingLinks.Concat(wildcardCandidates
                .Where(candidate => IsWildcardPatternElement(candidate, searchOptions) &&
                    SequenceElementMatches(firstPatternElement, candidate, searchOptions))
                .Distinct()
                .SelectMany(candidate => candidate.LinksFrom));
        }

        foreach (Link link in startingLinks)
        {
            if (link.LinkType?.Label != "VLU") continue;
            if (link.From is not SeqElement seqElement) continue;
            Thought? selectedValue = GetElementValue(seqElement, valueClass);
            if (selectedValue is null && IsWildcardPatternElement(seqElement.VLU, searchOptions)) selectedValue = seqElement.VLU;
            if (!SequenceElementMatches(firstPatternElement, selectedValue, searchOptions)) continue;
            if (mustMatchFirst && patternIndex == 0 && !IsSequenceFirstElement(seqElement)) continue;
            SeqElement? patternStart = GetPatternStartElement(seqElement, seqElement, patternIndex, searchOptions);
            if (patternStart != null)
                activeElements.Add(new SequenceSearchState { CurPos = seqElement, Confidence = 1.0f, FirstMatchElement = patternStart, LastMatchElement = seqElement });
            if (allowNestedSequences)
            {
                CheckForCallersToElement(activeElements, searchOptions, seqElement, patternIndex);
            }
        }

        return activeElements;
    }

    private void CheckForCallersToElement(List<SequenceSearchState> activeElements, Thought searchOptions, SeqElement seqElement,
        int patternIndex, SeqElement? curPos = null, List<SeqElement>? prevStack = null)
    {
        var callers = seqElement.LinksFrom
            .Where(x => x.LinkType?.Label == "VLU" && x.From is SeqElement)
            .Select(x => (SeqElement)x.From!)
            .ToList();
        if (curPos is null) curPos = seqElement;
        foreach (SeqElement caller in callers)
        {
            SeqElement? patternStart = GetPatternStartElement(caller, seqElement, patternIndex, searchOptions);
            if (patternStart is null) continue;
            if (curPos is null) curPos = seqElement;

            SequenceSearchState newEntry = new SequenceSearchState
            {
                CurPos = curPos,
                Confidence = 1.0f,
                FirstMatchElement = patternStart,
                LastMatchElement = seqElement
            };
            if (prevStack is null) prevStack = new List<SeqElement>();
            newEntry.ReturnStack.Push(caller.FRST!);
            foreach (SeqElement t in prevStack.AsEnumerable().Reverse())
                newEntry.ReturnStack.Push(t);
            activeElements.Add(newEntry);
            CheckForCallersToElement(activeElements, searchOptions, caller, patternIndex, curPos, newEntry.ReturnStack.ToList());
        }
    }
    private SeqElement? GetPatternStartElement(SeqElement? sequenceStart, SeqElement? seedElement, int seedPatternIndex, Thought searchOptions)
    {
        SeqElement? patternStartElement = null;
        if (sequenceStart is null || seedElement is null || searchOptions is null) return null;

        if (searchOptions.HasProperty("mustMatchFirst") && !searchOptions.HasProperty("allowWildcard"))
        {
            if (seedPatternIndex != 0) //there was a leading wildcard(s)
            {
                //easy case with no callers
                SeqElement? t = sequenceStart;
                for (int i = 0; i < seedPatternIndex; i++)
                {
                    t = (SeqElement?)t?.LinksFrom.FirstOrDefault(x => x.LinkType?.Label == "NXT")?.From;
                }
                return t;
            }
            patternStartElement = GetFirstElement(sequenceStart);
            return patternStartElement;
        }

        patternStartElement = seedElement;
        return patternStartElement;
    }

    private List<SequenceSearchState> ActivateNextSequenceElements(
        List<SequenceSearchState> activeElements, Thought? nextPatternElement, int patternIndex, Thought searchOptions, Thought? valueClass = null)
    {
        if (nextPatternElement is null || searchOptions is null) return activeElements;

        if (searchOptions.HasProperty("allowWildcards"))
            AddWildcardPrefixCandidates(activeElements, nextPatternElement, patternIndex, searchOptions);

        for (int i = 0; i < activeElements.Count; i++)
        {
            SequenceSearchState activeElement = activeElements[i];
            if (activeElement.CurPos is null) continue; //protection for unremoved activeElement

            // A finished template remains a result even if the sentence has more words.
            // A nested sequence is finished only when all its callers are also at their ends.
            if (searchOptions.HasProperty("allowTrailingInput") && IsSequenceLastElement(activeElement.CurPos) &&
                activeElement.ReturnStack.All(IsSequenceLastElement)) continue;

            //CALL [must handle multiple pushes]
            activeElement.CurPos = activeElement.CurPos.NXT;
            CheckForSubsequenceCall(searchOptions, activeElement);
            //RETURN  [Must handle multiple pops
            while (searchOptions.HasProperty("allowNestedSequences") && activeElement.CurPos is null && activeElement.ReturnStack.Count > 0)
            {
                activeElement.CurPos = activeElement.ReturnStack.Pop();
                activeElement.CurPos = activeElement.CurPos.NXT;
                CheckForSubsequenceCall(searchOptions, activeElement);
            }

            Thought? nextValue = GetElementValue(activeElement.CurPos, valueClass);
            if (nextValue is null && IsWildcardPatternElement(activeElement.CurPos?.VLU, searchOptions)) nextValue = activeElement.CurPos!.VLU;
            if (!SequenceElementMatches(nextPatternElement, nextValue, searchOptions))
            {
                activeElements.RemoveAt(i);
                i--;
                continue;
            }

            activeElement.LastMatchElement = activeElement.CurPos!;
            // TODO: adjust confidence based on match quality (exact match vs wildcard, etc.)
        }

        return activeElements;
    }
    private void AddWildcardPrefixCandidates(List<SequenceSearchState> activeElements, Thought patternElement,
                int patternIndex, Thought searchOptions)
    {
        foreach (Link link in patternElement.LinksFrom)
        {
            if (link.LinkType?.Label != "VLU") continue;
            if (link.From is not SeqElement anchorElement) continue;

            SeqElement? firstElement = anchorElement.FRST;
            if (firstElement is null) continue;

            SeqElement? current = firstElement;
            SeqElement? prevElement = null; // the caller advances CurPos before testing, so start one element back

            for (int i = 0; i < patternIndex; i++)
            {
                if (current is null || !IsWildcardPatternElement(current.VLU, searchOptions))
                {
                    current = null;
                    break;
                }
                prevElement = current;
                current = current.NXT;
            }

            if (!ReferenceEquals(current, anchorElement)) continue;

            activeElements.Add(new SequenceSearchState
            {
                CurPos = prevElement,
                FirstMatchElement = firstElement,
                LastMatchElement = anchorElement,
                Confidence = 1.0f
            });
        }
    }
    private void CheckForSubsequenceCall(Thought searchOptions, SequenceSearchState activeElement)
    {
        while (searchOptions.HasProperty("allowNestedSequences") && IsSequenceElement(activeElement.CurPos?.VLU))
        {
            activeElement.ReturnStack.Push(activeElement.CurPos!);
            activeElement.CurPos = activeElement.CurPos!.VLU as SeqElement;
        }
    }

    private bool SequenceElementMatches(Thought? patternElement, Thought? sequenceElementValue, Thought searchOptions)
    {
        if (patternElement is null || sequenceElementValue is null || searchOptions is null) return false;

        if (ReferenceEquals(patternElement, sequenceElementValue)) return true;

        if (!searchOptions.HasProperty("allowWildcards")) return false;

        if (WildcardMatchesValue(patternElement, sequenceElementValue) ||
            WildcardMatchesValue(sequenceElementValue, patternElement)) return true;
        // Unknown word types can provisionally fill a word-class wildcard. This
        // never changes the word's properties or accepts an unrelated class/literal.
        if (Labeled("unknownWordType") is Thought unknownWordType && Labeled("wordType") is Thought wordType)
        {
            Thought? wildcard = patternElement.HasAncestor(unknownWordType) ? sequenceElementValue
                : sequenceElementValue.HasAncestor(unknownWordType) ? patternElement : null;
            if (HasClassConstraint(wildcard) && wildcard!.Parents
                .Where(parent => !IsWildcardPatternElement(parent, searchOptions) && parent != Labeled("Property"))
                .All(parent => parent.HasAncestor(wordType))) return true;
        }
        // Manual template learning may propose a new member for the class
        // referenced by this particular wildcard. The value may already
        // belong to another learned class; that other observation must not
        // prevent it from acquiring an additional syntactic role.
        if (searchOptions.HasProperty("allowNewClassMembers") &&
            HasClassConstraint(sequenceElementValue))
            return true;
        return false;
    }
    private static bool HasClassConstraint(Thought? wildcard)
    {
        return wildcard is not null &&
            new[] { "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard" }.Any(wildcard.HasProperty) &&
            wildcard.AncestorsWithSelf
                .Where(ancestor => new[] { "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard" }.Any(ancestor.HasProperty))
                .SelectMany(ancestor => ancestor.Parents)
                .Any(parent => !new[] { "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard" }.Any(parent.HasProperty) &&
                    !ReferenceEquals(parent, wildcard.Store.Labeled("Property")) &&
                    !ReferenceEquals(parent, wildcard.Store.Labeled("Thought")) &&
                    !ReferenceEquals(parent, wildcard.Store.Labeled("Unknown")));
    }

    private static bool IsWildcardPatternElement(Thought? patternElement, Thought? searchOptions)
    {
        if (patternElement is null) return false;
        if (searchOptions is null) return false;
        if (!searchOptions.HasProperty("allowWildcards")) return false;

        return new[] { "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard" }.Any(patternElement.HasProperty);
    }

    private List<(SeqElement seqNode, float confidence)> CollectSequenceSearchResults(
        List<SequenceSearchState>? activeElements, Thought searchOptions, int inputLength,
        bool getFirstElements = true, Thought? matchClass = null)
    {
        List<(SeqElement seqNode, float confidence)> retVal = new();
        if (activeElements is null || searchOptions is null) return retVal;

        bool mustMatchLast = searchOptions.HasProperty("mustMatchLast");
        bool mustMatchFirst = searchOptions.HasProperty("mustMatchFirst");
        bool allowTrailingInput = searchOptions.HasProperty("allowTrailingInput");

        foreach (SequenceSearchState activeElement in activeElements)
        {
            if (mustMatchLast && !IsSequenceLastElement(activeElement.LastMatchElement)) continue;

            SeqElement firstElement = activeElement.FirstMatchElement;
            if (firstElement is null) continue;
            if (!IsSequenceFirstElement(firstElement) && mustMatchFirst) continue;

            int sequenceLength = GetSequenceLength(firstElement);
            float confidence = sequenceLength == 0 ? 0 : Math.Min(1.0f, (float)inputLength / sequenceLength);
            SeqElement result = getFirstElements ? firstElement.FRST ?? firstElement : firstElement;
            if (matchClass is not null && !result.LinksFrom.Any(link => link.From?.HasAncestor(matchClass) == true)) continue;
            retVal.Add((result, confidence));
        }

        return retVal
            .GroupBy(x => x.seqNode)
            .Select(x => x.OrderByDescending(y => y.confidence).First())
            .OrderByDescending(x => x.confidence)
            .ThenByDescending(x => allowTrailingInput ? GetSequenceLength(x.seqNode) : 0)
            .ToList();
    }

    public Thought CreateWildcard(
        string wildcardName,
        List<Thought> parentThoughts,
        string wildcardProperty = "isWildcard")
    {
        string[] cardinalityProperties =
        {
            "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard"
        };
        if (!cardinalityProperties.Contains(wildcardProperty, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Unknown wildcard cardinality property.", nameof(wildcardProperty));

        Thought wildcard = GetOrAddThought(wildcardName, "Property")!;
        Thought selectedProperty = GetOrAddThought(wildcardProperty, "Property")!;
        Thought? existingProperty = wildcard.LinksTo
            .Where(x => x.LinkType?.Label == "hasProperty" && x.To is not null)
            .Select(x => x.To!)
            .FirstOrDefault(x => cardinalityProperties.Contains(
                x.Label, StringComparer.OrdinalIgnoreCase));
        if (existingProperty is not null && existingProperty != selectedProperty)
            throw new InvalidOperationException(
                $"Wildcard '{wildcardName}' already has cardinality '{existingProperty.Label}'.");

        wildcard.AddProperty(selectedProperty);
        foreach (var parent in parentThoughts)
        {
            wildcard.AddParent(parent);
        }
        return wildcard;
    }
}

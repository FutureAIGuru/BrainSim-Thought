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

#nullable disable

using static UKS.UKS;

namespace UKS;

public class SeqElement : Thought
{
    /// <summary>
    /// Default constructor for sequence element placeholder.
    /// </summary>
    public SeqElement() { }
    public SeqElement FRST
    {
        get
        {
            Link nxt = LinksToWriteable.FindFirst(x => x.LinkType?.Label == "FRST");
            return nxt?.To as SeqElement;
        }
        set
        {
            // Remove existing links
            RemoveLinks("FRST");
            if (value is null) return;

            Thought nxtType = theUKS.GetOrAddThought("FRST", "LinkType");
            AddLink(nxtType, value);
        }
    }
    public SeqElement NXT
    {
        get
        {
            Link nxt = LinksTo.FindFirst(x => x.LinkType?.Label == "NXT");
            return nxt?.To as SeqElement;
        }
        set
        {
            // Remove existing links
            RemoveLinks("NXT");
            if (value is null) return;

            Thought nxtType = theUKS.GetOrAddThought("NXT", "LinkType");
            AddLink(nxtType, value);
        }
    }
    public Thought VLU
    {
        get
        {
            Link nxt = LinksTo.FindFirst(x => x.LinkType?.Label == "VLU");
            return nxt?.To;
        }
        set
        {
            // Remove existing links ??
            RemoveLinks("VLU");  //Removing this would allow for multiple values per element , but would require changes to the search and flattening functions
            if (value is null) return;

            Thought nxtType = theUKS.GetOrAddThought("VLU", "LinkType");
            AddLink(nxtType, value);
        }
    }
    public override string ToString()
    {
        string retVal = "";
        var valuList = theUKS.FlattenSequence(this);
        retVal = "^" + string.Join(" ", valuList);
        return retVal;
    }
}

public partial class UKS
{
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
    public bool IsSequenceElement(Thought t)
    {
        return (t is SeqElement);
    }
    public bool IsSequenceFirstElement(Thought t)
    {
        if (t is not SeqElement s) return false;
        SeqElement t1 = s.FRST;
        return object.ReferenceEquals(t1, t); //use this to check for same object becaue == is be overloaded
    }
    private bool IsSequenceLastElement(SeqElement s)
    {
        if (s is null) return false;
        return (s.NXT is null);
    }
    private SeqElement GetNextElement(SeqElement s)
    {
        return s.NXT;
    }
    private SeqElement GetFirstElement(SeqElement s)
    {
        return s.FRST;
    }
    public SeqElement GetLastlement(SeqElement s)
    {
        SeqElement retVal = s;
        SeqElement next = s.NXT;
        while (next is not null)
        {
            next = retVal.NXT;
            if (next is not null) retVal = next;
        }
        return retVal;
    }

    /// <summary>
    /// Gets the single VLU target value of a sequence node.
    /// </summary>
    /// <param name="t">Sequence element to read.</param>
    /// <returns>The linked VLU value, or null if none.</returns>
    public Thought GetElementValue(SeqElement s)  //assuming there is only one
    {
        if (s is null) return null;
        Thought retVal = s.LinksTo.FindFirst(x => x.LinkType?.Label == "VLU")?.To;
        return retVal;
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
        SeqElement first = prevElementIn;
        if (IsSequenceFirstElement(prevElementIn))
        {
            //this is a bit tricky...
            //it actually adds a 2nd element but then copies old 1st element values to the 2nd element and puts the new value on the old first
            //why? all the FRST links and external pointers to the sequence will still be correct without modification
            SeqElement newNode = new()
            {
                Label = prevElementIn.Label + "*", //the label will auto-increment.
                FRST = first,
                NXT = first.NXT,
            };
            Thought origValue = GetElementValue(first);
            newNode.AddLink("VLU", origValue);
            first.RemoveLink("VLU", origValue);
            first.AddLink("VLU", value);
            first.NXT = newNode;
        }
        else
        {
            SeqElement predecessor = prevElementIn.FRST;
            while (predecessor?.NXT is not null && predecessor.NXT != prevElementIn)
                predecessor = predecessor.NXT;
            if (predecessor is null || predecessor.NXT != prevElementIn)
                throw new ArgumentException("prevElementIn is not in its FRST chain", nameof(prevElementIn));

            SeqElement newNode = new()
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
        SeqElement newNode = new()
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
        SeqElement firstNode = new()
        {
            Label = labelBase + "-seq0",
        };
        firstNode.AddLink("VLU", value);
        firstNode.FRST = firstNode; //points to itself as the first element
        return firstNode;
    }
    public void DeleteSequence(SeqElement s)
    {
        //make sure there's only one reference to this sequence
        if (!IsSequenceFirstElement(s)) return;
        if (s.LinksFrom.Count(x => x.LinkType.Label != "FRST") > 1) return;
        if (!s.Label.StartsWith("thequery"))  //queries don't have cache entries
        {        //delete it from the cache
            var sequenceContent = FlattenSequence(s);
            SequenceCache.Remove(sequenceContent);
        }
        //follow the chain and delete the elements.
        SeqElement current = s;
        while (current is not null)
        {
            //This replicates DeleteThought but eliminates problems of re-entrance
            SeqElement next = GetNextElement(current);
            //recursively delete subsequences which are no longer used anywhere else
            var subsequences = current.VLU?.LinksFrom.Where(x => x.LinkType.Label != "FRST");
            if (subsequences?.Count() == 1 && subsequences.First().To is SeqElement s1)
                DeleteSequence(s1);
            foreach (Link lnk in current.LinksTo)
                current.RemoveLink(lnk);
            ThoughtLabels.RemoveThoughtLabel(current.Label);
            lock (AtomicThoughts)
                AtomicThoughts.Remove(current);
            current = next;
        }
    }
    //This unconditionally creates a sequence of Thoughts
    //No checking for existing sequences, no subsequences detection
    private SeqElement CreateRawSequence(List<Thought> targets, string baseLabel = "seq*")
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
    private Dictionary<IReadOnlyList<Thought>, Thought> SequenceCache = new(new ThoughtListComparer());
    public void ClearSequenceCache()
    {
        SequenceCache.Clear();
    }

    // Reference-only comparer for a list of Thought
    sealed class ThoughtListComparer : IEqualityComparer<IReadOnlyList<Thought>>
    {
        public bool Equals(IReadOnlyList<Thought> sequenceContent, IReadOnlyList<Thought> y)
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
    public SeqElement AddSequence(string label, List<Thought> targets, bool allowCompression = true)
    {
        if (targets.Count < 2) return null;  //a sequence must have at least 2 elements

        List<Thought> resolvedTargets = new(targets);

        // does sequence one already exist?
        // Note: this returns the existing sequence as opposed to creating a new sequence which references the
        // existing as a sub-sequence
        /////// this breaks the read-in if there are wildcards in the sequence because the wildcard will match any existing sequence and then the rest of the sequence will be lost
        //var existingSequences = RawSearchExact(resolvedTargets);
        //foreach (var t in existingSequences)
        //{
        //    if (IsSequenceFirstElement(t.seqNode) && GetSequenceLength(t.seqNode) == targets.Count)
        //    {
        //        return t.seqNode;
        //    }
        //}

        //check for any existing sequences which begins with the targets[startIndex]
        (Thought seqStart, int length) FindExistingSubsequence(int startIndex)
        {
            int remaining = resolvedTargets.Count - startIndex;
            for (int len = remaining; len > 1; len--)
            {
                var testSequence = resolvedTargets.GetRange(startIndex, len);
                if (SequenceCache.TryGetValue(testSequence, out Thought existing) && existing is not null)
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
                (Thought seqStart, int length) = FindExistingSubsequence(i);
                if (seqStart is not null)
                {
                    resolvedTargets.RemoveRange(i, length);
                    resolvedTargets.Insert(i, seqStart);
                    continue;
                }
            }
        }
        //Finally, create the sequence and link to it
        SeqElement rawSequence = CreateRawSequence(resolvedTargets, label);
        var newSequence = FlattenSequence(rawSequence);
        SequenceCache[newSequence] = rawSequence;
        return rawSequence;

    }
    /// <summary>
    /// Adds a sequence of Thoughts as ordered links from a source Thought. Handles nested sequences.
    /// </summary>
    /// <param name="source">The 'owner' of the sequence.</param>
    /// <param name="linkType">The link type to use for the sequence relationship.</param>
    /// <param name="targets">Targets in order; can be sequence start nodes.</param>
    /// <param name="baseWeight">Base weight for the links (currently unused).</param>
    /// <returns>The first node of the created or reused sequence, or null if insufficient targets.</returns>
    public SeqElement AddSequenceAndLink(Thought source, Thought linkType, List<Thought> targets, float baseWeight = 1.0f)
    {
        if (targets.Count < 2) return null;  //a sequence must have at least 2 elements

        //clear out any existing sequence links of this type
        source.RemoveLinks(linkType);  //TODO delete the sequence
        SeqElement rawSequence = AddSequence(source.Label, targets);
        source.AddLink(linkType, rawSequence);
        return rawSequence;
    }
    public Thought GetReferrer(SeqElement seqNode, Thought linkType)
    {
        var referrers = seqNode.LinksFrom.Where(x => x.LinkType == linkType);
        if (referrers.Count() > 0)
            return referrers.First().From;
        return null;
    }

    /*

        [Obsolete("Use FindSequencesByActivation with linkType filtering instead. This method will be removed in a future version.")]
        public List<(Thought result, float confidence)> HasSequence2(List<Thought> targets, Thought linkType,
         bool mustMatchFirst = false, bool mustMatchLast = false, bool circularSearch = false, bool allowOutOfOrder = false)
        {
            var result1 = HasSequence(targets, linkType, mustMatchFirst, mustMatchLast, circularSearch, allowOutOfOrder);
            List<(Thought result, float confidence)> retVal = new();
            foreach (var result in result1)
            {
                foreach (Link l in result.seqNode.LinksFrom.Where(x => x.LinkType == linkType))
                {
                    retVal.Add(new(l.From, result.confidence));
                }
            }
            return retVal;
        }

        //TODO: make mustMatchLast, circularSearch & allowOutOfOrder work
        /// <summary>
        /// Finds sequences matching the ordered targets and returns candidate links with confidence scores.
        /// </summary>
        /// <param name="targets">Pattern to search for.</param>
        /// <param name="linkType">Specific link type to follow; null matches all sequence link types.</param>
        /// <param name="mustMatchFirst">Require candidate to start at the first sequence element.</param>
        /// <param name="mustMatchLast">Require candidate to end at the last sequence element.</param>
        /// <param name="circularSearch">Reserved for circular search (not implemented).</param>
        /// <param name="allowOutOfOrder">Reserved for out-of-order search (not implemented).</param>
        /// <returns>List of candidate links with confidence values.</returns>
        [Obsolete("Use FindSequencesByActivation with appropriate searchOptions instead. This method will be removed in a future version.")]
        public List<(SeqElement seqNode, float confidence)> HasSequence(List<Thought> targets, Thought linkType,
            bool mustMatchFirst = false, bool mustMatchLast = false, bool circularSearch = false, bool allowOutOfOrder = false)
        {
            if (circularSearch || allowOutOfOrder)
                throw new NotSupportedException("circularSearch and allowOutOfOrder are not yet implemented.");

            // Use FindSequencesByActivation for the core search
            Thought searchOptions = CreateSearchOptions(mustMatchFirst, mustMatchLast, allowWildcard: false);
            var searchResults = FindSequencesByActivation(targets, searchOptions);

            // Filter by linkType if specified
            return FilterByLinkType(searchResults, linkType);
        }

        public List<(SeqElement seqNode, float confidence)> RawAnchoredFuzzyMatch(List<Thought> targets)
        {
            List<(SeqElement seqNode, float confidence)> matches = new();
            if (targets is null || targets.Count < 2) return matches;

            //get direct sequences
            var candidateNodes = targets[0].LinksFrom
                .Where(r => r.LinkType?.Label == "VLU" && IsSequenceFirstElement(r.From))
                .Select(r => r.From)
                .ToList();
            //add in sequences which refer to this at the beginning
            for (int i = 0; i < candidateNodes.Count; i++)
            {
                Thought candidate = candidateNodes[i];
                var referrers = candidate.LinksFrom.Where(x => x.LinkType.Label == "VLU" && IsSequenceFirstElement(x.From));
                foreach (Link referrer in referrers)
                    candidateNodes.Add(referrer.From);
            }
            ;

            //see which of the candidates qualifies and get the scores
            foreach (var candidate in candidateNodes)
            {
                if (candidate is not SeqElement seqNode) continue;

                var flat = FlattenSequence(seqNode);
                if (flat.Count < targets.Count - 1 || flat.Count > targets.Count + 1) continue;
                if (!ReferenceEquals(flat.LastOrDefault(), targets.Last())) continue; // anchor last

                var seqInner = flat.Skip(1).Take(Math.Max(0, flat.Count - 2)).ToList();
                var targetInner = targets.Skip(1).Take(Math.Max(0, targets.Count - 2)).ToList();

                int missingTargets = targetInner.Count(t => !seqInner.Any(s => ReferenceEquals(s, t)));
                int extraSeq = seqInner.Count(s => !targetInner.Any(t => ReferenceEquals(t, s)));
                if (missingTargets > 2 || extraSeq > 2) continue;

                int matchedInternal = targetInner.Count - missingTargets;
                int targetCountInner = Math.Max(1, targetInner.Count); // avoid div/0

                // Coverage with extra penalty
                float matchedRatio = (float)matchedInternal / targetCountInner;
                float extraPenalty = (float)extraSeq / (extraSeq + targetCountInner); // 0..1
                float coverageScore = matchedRatio * (1f - extraPenalty); // 0..1

                // Order bonus: fraction of in-order pairs among matched elements
                float orderScore = ComputeOrderPairFraction(flat, targets);

                // Blend: favor coverage slightly, order strongly influences perfect ranking
                float confidence = 0.6f * coverageScore + 0.4f * orderScore;
                if (confidence > 0)
                    matches.Add((seqNode, confidence));
            }

            return matches
                .OrderByDescending(m => m.confidence)
                .ToList();
        }


    private static float ComputeOrderPairFraction(List<Thought> seq, List<Thought> targets)
    {
        float count = 0;
        for (int j = 0; j < seq.Count - 1; j++)
        {
            for (int i = 0; i < targets.Count - 1; i++)
            {
                if (ReferenceEquals(targets[i], seq[j]) && ReferenceEquals(targets[i + 1], seq[j + 1]))
                {
                    count++;
                    break;
                }
            }
        }
        //ignoring the possibility that a pair might occur multiple times.
        float score = count / (Math.Max(seq.Count, targets.Count) - 1);
        return score;
    }
    [Obsolete("Use FindSequencesByActivation instead. This method will be removed in a future version.")]
    public List<(SeqElement seqNode, IEnumerator<SeqElement> curPos, int matchCount)> RawSearchExact(List<Thought> targets)
    {
        List<(SeqElement seqNode, IEnumerator<SeqElement> curPos, int matchCount)> searchCandidates = new();
        if (targets is null || targets.Count < 2) return searchCandidates;
        //Step 1: initialize enuerators for each candidate sequence
        var candidateNodes = targets[0].LinksFrom
            .Where(r => r.LinkType?.Label == "VLU")
            .Select(r => (seqNode: r.From, matchedCount: 1))
            .ToList();
        if (targets[0].HasAncestor("word"))
        {
            var wildCardNodes = Labeled("w:??").LinksFrom
            .Where(r => r.LinkType?.Label == "VLU")
            .Select(r => (seqNode: r.From, matchedCount: 1))
            .ToList();
            candidateNodes.AddRange(wildCardNodes);
        }

        foreach (var candidate in candidateNodes)
        {
            var enumerator = EnumerateSequenceElements((SeqElement)candidate.seqNode).GetEnumerator();
            searchCandidates.Add(new((SeqElement)candidate.seqNode, enumerator, 1));
            searchCandidates.Last().curPos.MoveNext();
        }

        // Step 2: For each subsequent target, filter candidates by following NXT links
        for (int i = 1; i < targets.Count; i++)
        {
            Thought currentTarget = targets[i];
            if (currentTarget is null) break; // Stop if we hit a null target

            for (int j = 0; j < searchCandidates.Count; j++)
            {
                SeqElement nextThought = null;
                //have we reached the end of the current subsequence?
                if (!searchCandidates[j].curPos.MoveNext())
                {
                    var referrers = GetAllFollowingNodes(searchCandidates[j].seqNode);
                    foreach (var referrer in referrers)
                    {
                        var x = searchCandidates.FindFirst(x => x.seqNode == referrer);
                        if (x.seqNode is null)
                            searchCandidates.Add(new(referrer, EnumerateSequenceElements(referrer).GetEnumerator(), searchCandidates[j].matchCount));
                    }
                    searchCandidates.RemoveAt(j);
                    j--;
                    continue;
                }
                nextThought = searchCandidates[j].curPos.Current;
                // Check if the next thought matches the current target
                Thought theNextValue = GetElementValue(nextThought);
                if (theNextValue != currentTarget && theNextValue != Labeled("w:??"))
                {
                    searchCandidates.RemoveAt(j);
                    j--; // Adjust index after removal
                }
                else
                {
                    int temp = searchCandidates[j].matchCount; //hack to increment a value within a tuple
                    temp++;
                    searchCandidates[j] = (searchCandidates[j].seqNode, searchCandidates[j].curPos, temp);
                }
            }
        }
        return searchCandidates;
    }

    private List<SeqElement> GetAllFollowingNodes(SeqElement node)
    {
        List<SeqElement> retVal = new();
        //if this is a subsequence, get the caller(s)
        SeqElement startOfSequence = GetFirstElement(node);
        List<Link> referrers = startOfSequence.LinksFrom.Where(x => x.LinkType.Label == "VLU").ToList();
        foreach (var referrer in referrers)
        {
            SeqElement nextLocation = (referrer.From as SeqElement)?.NXT;
            if (nextLocation is not null)
                retVal.Add(nextLocation);
            else
                retVal.AddRange(GetAllFollowingNodes((SeqElement)referrer.From));
        }
        return retVal;
    }

    */
    /// <summary>
    /// Flatten a sequence into a list of leaf Thoughts (letters). Handles nested sequences via VLU.
    /// </summary>
    public List<Thought> FlattenSequence(SeqElement sequenceStart, bool skipPlusValues = false)
    {
        //experimentating with an enumartor for sequences
        List<Thought> result = new();
        var e = EnumerateSequenceElements(sequenceStart).GetEnumerator();
        while (e.MoveNext())
            if (GetElementValue(e.Current) is not null)
                result.Add(GetElementValue(e.Current));
        //foreach (Thought t in EnumerateSequenceElements(sequenceStart))
        //    result.Add(t);
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
    public IEnumerable<SeqElement> EnumerateSequenceElements(SeqElement sequenceStart, Stack<SeqElement> visitedSequences = null)
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

            // Get the VLU Linkto find what this sequence node points to
            Thought valueRel = GetElementValue(current);

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
    public List<Thought> GetReferringThoughts(SeqElement s, Thought linkType)
    {
        List<Thought> retVal = new();
        foreach (Link l in s.LinksFrom.Where(x => x.LinkType == linkType))
            retVal.Add(l.From);
        return retVal;
    }
    /*
    /// <summary>
    /// Recursively finds all sequences that reference the given sequence.
    /// </summary>
    private void AddReferencingSequences(List<Link> currentReferences, Thought linkType, List<Link> accumulator)
    {
        if (currentReferences is null || currentReferences.Count == 0) return;

        var visited = new HashSet<Thought>();

        foreach (var link in currentReferences)
        {
            if (link.From is null || visited.Contains(link.From)) continue;
            visited.Add(link.From);

            // Check if this Thought is part of a sequence (has FRST link)
            var frstLink = link.From.LinksTo?.FirstOrDefault(r => r.LinkType?.Label == "FRST");
            if (frstLink?.To is null) continue;

            // Find what references this sequence
            var parentReferences = frstLink.To.LinksFrom
                ?.Where(r => (linkType is null || r.LinkType == linkType) &&
                              r.LinkType?.Label != "FRST" &&
                              !accumulator.Contains(r))
                .ToList();

            if (parentReferences is not null && parentReferences.Count > 0)
            {
                accumulator.AddRange(parentReferences);
                accumulator.Remove(link);
                // Recurse to find sequences that reference these
                AddReferencingSequences(parentReferences, linkType, accumulator);
            }
        }
    }
    */
    /// <summary>
    /// Replace a plain Thought with a SeqElement while preserving label, value, weight, and links.
    /// </summary>
    public SeqElement PromoteToSeqElement(Thought t)
    {
        if (t is null) return null;
        if (t is SeqElement sExisting) return sExisting;
        ThoughtLabels.RemoveThoughtLabel(t.Label);

        var seq = new SeqElement
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

        // Replace in global list
        lock (AtomicThoughts)
        {
            int idx = AtomicThoughts.IndexOf(t);
            if (idx >= 0)
                AtomicThoughts[idx] = seq;
        }
        t.Delete();

        ThoughtLabels.AddThoughtLabel(seq.Label, seq);
        return seq;
    }

    private class SequenceSearchState
    {
        public SeqElement FirstMatchElement;
        public SeqElement LastMatchElement;
        public SeqElement CurPos;
        public Stack<SeqElement> ReturnStack = new();
        public float Confidence = 1.0f;
    }

    public List<(SeqElement seqNode, float confidence)> FindSequencesByActivation(
        List<Thought> pattern, Thought searchOptions = null)
    {
        List<(SeqElement seqNode, float confidence)> retVal = new();
        if (pattern is null || pattern.Count == 0) return retVal;

        searchOptions ??= Labeled("ExactSequenceSearch");
        if (searchOptions is null) return retVal;

        int seedPatternIndex = pattern.FindIndex(p => !IsWildcardPatternElement(p, searchOptions));
        if (seedPatternIndex < 0) return retVal;

        // Step 1: seed from the first non-wildcard pattern element.
        List<SequenceSearchState> activeElements = InitializeSequenceSearchByActivation(pattern[seedPatternIndex], seedPatternIndex, searchOptions);

        // Step 2: propagate through NXT links for each remaining pattern element.
        for (int i = seedPatternIndex + 1; i < pattern.Count; i++)
        {
            activeElements = ActivateNextSequenceElements(activeElements, pattern[i],i, searchOptions);
            if (activeElements.Count == 0) break;
        }
        // Step 3: collect matching sequence starts and confidence values.
        retVal = CollectSequenceSearchResults(activeElements, searchOptions, pattern);

        return retVal;
    }   

    /// <summary>
    /// Creates a search options Thought with specified properties for sequence searching.
    /// </summary>
    public Thought CreateSearchOptions(bool mustMatchFirst = false, bool mustMatchLast = false, 
        bool allowWildcard = false, bool allowNestedSequences = false)
    {
        Thought searchOptions = GetOrAddThought("SequenceSearchOptions*");

        if (mustMatchFirst)
            searchOptions.AddLink("hasProperty", GetOrAddThought("mustMatchFirst"));
        if (mustMatchLast)
            searchOptions.AddLink("hasProperty", GetOrAddThought("mustMatchLast"));
        if (allowWildcard)
            searchOptions.AddLink("hasProperty", GetOrAddThought("allowWildcards"));
        if (allowNestedSequences)
            searchOptions.AddLink("hasProperty", GetOrAddThought("allowNestedSequences"));

        return searchOptions;
    }

    private List<SequenceSearchState> InitializeSequenceSearchByActivation(Thought firstPatternElement, int patternIndex, Thought searchOptions)
    {
        List<SequenceSearchState> activeElements = new();
        if (firstPatternElement is null || searchOptions is null) return activeElements;

        bool mustMatchFirst = searchOptions.HasProperty("mustMatchFirst");
        bool allowNestedSequences= searchOptions.HasProperty("allowNestedSequences");

        foreach (Link link in firstPatternElement.LinksFrom)
        {
            if (link.LinkType?.Label != "VLU") continue;
            if (link.From is not SeqElement seqElement) continue;
            if (mustMatchFirst && patternIndex == 0 && !IsSequenceFirstElement(seqElement)) continue;
            SeqElement patternStart = GetPatternStartElement(seqElement, seqElement, patternIndex, searchOptions);
            if (patternStart != null) 
                activeElements.Add(new SequenceSearchState { CurPos = seqElement, Confidence = 1.0f, FirstMatchElement = patternStart, LastMatchElement = seqElement });
            if (allowNestedSequences)
            {
                CheckForClallersToElement(activeElements, searchOptions, seqElement, patternIndex);
            }
        }
         
        return activeElements;
    }

    private void CheckForClallersToElement(List<SequenceSearchState> activeElements, Thought searchOptions, SeqElement seqElement, 
        int patternIndex, SeqElement curPos = null, List<SeqElement> prevStack = null)
    {
        var callers = seqElement.LinksFrom
            .Where(x => x.LinkType?.Label == "VLU" && x.From is SeqElement)
            .Select(x => (SeqElement)x.From)
            .ToList();
        if (curPos is null) curPos = seqElement;
        foreach (SeqElement caller in callers)
        {
            SeqElement patternStart = GetPatternStartElement(caller, seqElement, patternIndex, searchOptions);
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
            newEntry.ReturnStack.Push((SeqElement)caller.FRST);
            foreach (SeqElement t in prevStack.AsEnumerable().Reverse())
                newEntry.ReturnStack.Push(t);
            activeElements.Add(newEntry);
            CheckForClallersToElement(activeElements, searchOptions, caller, patternIndex, curPos,newEntry.ReturnStack.ToList());
        }
    }
    private SeqElement GetPatternStartElement(SeqElement sequenceStart,SeqElement seedElement,int seedPatternIndex,Thought searchOptions)
    {
        SeqElement patternStartElement = null;
        if (sequenceStart is null || seedElement is null || searchOptions is null) return null;

        if (searchOptions.HasProperty("mustMatchFirst") && !searchOptions.HasProperty("allowWildcard"))
        {
            if (seedPatternIndex != 0) //there was a leading wildcard(s)
            {
                //easy case with no callers
                SeqElement t = sequenceStart;
                for (int i = 0; i < seedPatternIndex; i++)
                {
                    t = (SeqElement) t?.LinksFrom.FirstOrDefault(x => x.LinkType?.Label == "NXT")?.From;
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
        List<SequenceSearchState> activeElements, Thought nextPatternElement, int patternIndex, Thought searchOptions)
    {
        if (activeElements is null || nextPatternElement is null || searchOptions is null) return activeElements;

        if (searchOptions.HasProperty("allowWildcards"))
            AddWildcardPrefixCandidates(activeElements,nextPatternElement,patternIndex,searchOptions);

        for (int i = 0; i < activeElements.Count; i++)
        {
            SequenceSearchState activeElement = activeElements[i];
            if (activeElement.CurPos is null) continue; //protection for unremoved activeElement

            //CALL [must handle multiple pushes]
            activeElement.CurPos = activeElement.CurPos.NXT;
            CheckForSubsequenceCall(searchOptions, activeElement);
            //RETURN  [Must handle multiple pops
            while (searchOptions.HasProperty("allowNestedSequences") && activeElement.CurPos is null && activeElement.ReturnStack.Count > 0)
            {
                activeElement.CurPos = activeElement.ReturnStack.Pop();
                activeElement.CurPos = activeElement.CurPos.NXT;
                CheckForSubsequenceCall(searchOptions,activeElement);
            }

            Thought nextValue = activeElement.CurPos?.VLU;
            if (!SequenceElementMatches(nextPatternElement, nextValue, searchOptions))
            {
                //if (searchOptions.HasProperty("mustMatchLast"))
                {
                    activeElements.RemoveAt(i);
                    i--;
                }
                continue;
            }

            activeElement.LastMatchElement = activeElement.CurPos;
            // TODO: adjust confidence based on match quality (exact match vs wildcard, etc.)
        }

        return activeElements;
    }
    private void AddWildcardPrefixCandidates(List<SequenceSearchState> activeElements,Thought patternElement,
                int patternIndex,Thought searchOptions)
    {
        foreach (Link link in patternElement.LinksFrom)
        {
            if (link.LinkType?.Label != "VLU") continue;
            if (link.From is not SeqElement anchorElement) continue;

            SeqElement firstElement = anchorElement.FRST;
            if (firstElement is null) continue;

            SeqElement current = firstElement;
            SeqElement prevElement = null; //you have to get the prev element because the caller sets next before testing

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
            activeElement.ReturnStack.Push(activeElement.CurPos);
            activeElement.CurPos = activeElement.CurPos.VLU as SeqElement;
        }
    }

    private bool SequenceElementMatches(Thought patternElement, Thought sequenceElementValue, Thought searchOptions)
    {
        //if (patternElement is null || sequenceElementValue is null || searchOptions is null) return false;
        //if (ReferenceEquals(patternElement, sequenceElementValue)) return true;

        //return IsWildcardPatternElement(sequenceElementValue, searchOptions);

        if (patternElement is null ||sequenceElementValue is null ||searchOptions is null)return false;

        if (ReferenceEquals(patternElement, sequenceElementValue)) return true;

        if (!searchOptions.HasProperty("allowWildcards")) return false;

        return patternElement.HasAncestor("Wildcard") ||
               sequenceElementValue.HasAncestor("Wildcard");
    }

    private bool IsWildcardPatternElement(Thought patternElement, Thought searchOptions)
    {
        if (patternElement is null) return false;
        if (searchOptions is null) return false;
        if (!searchOptions.HasProperty("allowWildcards")) return false;
        return patternElement.HasAncestor("Wildcard");
    }

    private List<(SeqElement seqNode, float confidence)> CollectSequenceSearchResults(
        List<SequenceSearchState> activeElements, Thought searchOptions, List<Thought> pattern)
    {
        List<(SeqElement seqNode, float confidence)> retVal = new();
        if (activeElements is null || searchOptions is null) return retVal;

        bool mustMatchLast = searchOptions.HasProperty("mustMatchLast");
        bool mustMatchFirst = searchOptions.HasProperty("mustMatchFirst");

        foreach (SequenceSearchState activeElement in activeElements)
        {
            if (mustMatchLast && !IsSequenceLastElement(activeElement.LastMatchElement)) continue;

            SeqElement firstElement = activeElement.FirstMatchElement;
            if (firstElement is null) continue;
            if (!IsSequenceFirstElement(firstElement) && mustMatchFirst) continue;

            int sequenceLength = GetSequenceLength(firstElement);
            float confidence = sequenceLength == 0 ? 0 : Math.Min(1.0f, (float)pattern.Count / sequenceLength);
            retVal.Add((firstElement.FRST, confidence));
            if (searchOptions.HasProperty("allowNestedSequences"))
            {
                List<SequenceSearchState> newElements = new();

                CheckForClallersToElement(newElements,searchOptions, firstElement.FRST,0);
            }
        }

        return retVal
            .GroupBy(x => x.seqNode)
            .Select(x => x.OrderByDescending(y => y.confidence).First())
            .OrderByDescending(x => x.confidence)
            .ToList();
    }
}

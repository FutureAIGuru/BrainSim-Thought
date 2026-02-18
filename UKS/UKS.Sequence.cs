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
    // seq2 ->NXT -> null  (or has no NXT entry)
    // seq2 ->FRST-> seq0
    // seq2 ->VLU--> T
    // NOTE: the elements seq* need not have labels at all, they are just used here for clarity
    // each seq* element must also have a FRST releationship back to the owner Thought
    // The Thought ToString() method will automatically follow the sequences and return cat->spelled->^cat
    // VLU targets may be other sequences

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
    private bool IsSequenceFirstElement(Thought t)
    {
        if (t is not SeqElement s) return false;
        SeqElement t1 = s.FRST;
        return object.ReferenceEquals(t1, t); //use this to check for same object becaue == is be overloaded
    }
    private bool IsSequenceLastElement(SeqElement s)
    {
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
    private SeqElement GetLastlement(SeqElement s)
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
    private int GetSequenceLength(SeqElement firstNode)
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
            first.RemoveLink1("VLU", origValue);
            first.AddLink("VLU", value);
            first.NXT = newNode;
        }
        else
        {
            throw new NotImplementedException();
        }
        return first;
    }

    //Adds a new element after prevElementIn) and links its value. Returns the new element.
    private SeqElement AddElement(SeqElement prevElement, Thought value)
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
    public SeqElement CreateFirstElement(Thought source, Thought value)
    {
        SeqElement firstNode = new()
        {
            Label = source.Label.ToLower() + "-seq0",
        };
        firstNode.AddLink("VLU", value);
        firstNode.FRST = firstNode; //points to itself as the first element
        return firstNode;
    }
    private void DeleteSequence(SeqElement s)
    {
        //make sure there's only one reference to this sequence
        if (!IsSequenceFirstElement(s)) return;
        if (s.LinksFrom.Count(x => x.LinkType.Label != "FRST") > 1) return;
        //delete it from the cache
        var sequenceContent = FlattenSequence(s);
        SequenceCache.Remove(sequenceContent);

        //follow the chain and delete the elements.
        SeqElement current = s;
        SeqElement next = GetNextElement(current);
        while (current is not null)
        {  //This replicates DeleteThought but eliminates problems of re-entrance
            next = GetNextElement(current);
            ThoughtLabels.RemoveThoughtLabel(current.Label);
            lock (AllThoughts)
                AllThoughts.Remove(current);
            current = next;
        }
    }
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
    //private readonly Dictionary<string,Thought> SequenceCache = new();
    Dictionary<IReadOnlyList<Thought>, Thought> SequenceCache = new(new ThoughtListComparer());
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

    /// <summary>
    /// Adds a sequence of Thoughts as ordered links from a source Thought. Handles nested sequences.
    /// </summary>
    /// <param name="source">The 'owner' of the sequence.</param>
    /// <param name="linkType">The link type to use for the sequence relationship.</param>
    /// <param name="targets">Targets in order; can be sequence start nodes.</param>
    /// <param name="baseWeight">Base weight for the links (currently unused).</param>
    /// <returns>The first node of the created or reused sequence, or null if insufficient targets.</returns>
    public SeqElement AddSequence(Thought source, Thought linkType, List<Thought> targets, float baseWeight = 1.0f)
    {
        if (targets.Count < 1) return null;  //a sequence must have at least 2 elements

        //clear out any existing sequence links of this type
        source.RemoveLinks(linkType);  //TODO delete the sequence

        List<Thought> resolvedTargets = new(targets);

        // does sequence one already exist?
        var existingSequences = RawSearchExact(resolvedTargets);
        foreach (var t in existingSequences)
        {
            if (IsSequenceFirstElement(t.seqNode) && GetSequenceLength(t.seqNode) == targets.Count)
            {
                source.AddLink(linkType, t.seqNode);
                return t.seqNode;
            }
        }


        //check for any existing sequences which begins with the targets[startIndes]
        (Thought seqStart, int length) FindExistingSubsequence(int startIndex)
        {
            int remaining = targets.Count - startIndex;
            for (int len = remaining; len > 1; len--)
            {
                var testSequence = targets.GetRange(startIndex, len);
                if (SequenceCache.TryGetValue(testSequence, out Thought existing) && existing is not null)
                    return (existing, len);
            }
            return (null, 0);
        }

        //Are there any existing seqnences in the target list?
        // edit the resolved target list that reuses any existing subsequences
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
        //Finally, create the sequence and link to it
        SeqElement rawSequence = CreateRawSequence(resolvedTargets, source.Label);
        source.AddLink(linkType, rawSequence);
        var newSequence = FlattenSequence(rawSequence);
        SequenceCache[newSequence] = rawSequence;
        return rawSequence;
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
    public List<(SeqElement seqNode, float confidence)> HasSequence(List<Thought> targets, Thought linkType,
        bool skipPlusEntries = false, bool mustMatchFirst = false, bool mustMatchLast = false, bool circularSearch = false, bool allowOutOfOrder = false)
    {
        //this function searches the UKS for sequences matching the specified pattern in targets. 

        //If circularSearch is true, then the search will consider sequences that wrap around from end to start Thought.
        //If firstLastPriority is true, then matches that have the first and last elements matching will be given higher confidence
        ///AND the order of the elements will be lower priority if all intervening elements are found.  
        ///Example: given the stored sequance S E A T , S A E T would match with higher confidence than E S A T
        ///

        // Returns a list of tuples of (Thought to the start of the matching sequence, confidence value between 0 and 1)

        //CASE 0:  elements must be in exact order, no circular search, no first/last priority
        //Ignoring, for now, the circularSearch and firstLastPriority options
        //start by finding all sequences that contain the first target.  This is a superset of the actual matches.
        // it is target[0].LinksFrom.Where(r => r.LinkType.Label == "VLU") 
        // this builds an initial list of candidate sequence entry points
        // then we can walk each sequence to see if it matches the full pattern and remove from the candidate list if it does not match
        // that is: for targets[1] create a new list of candidates that have targets[1] as the VLU of the NXT link from the first candidate list
        // then match the two lists: if an entry in the new list does not have a LinkFrom an enement in list 1 with LinkType NXT, it is removed from the candidate list
        // if it does match, we put this new element in the initial ist as the new candidate for the next iteration.
        // repeat for all targets
        // at the end, the candidate list contains only sequences that match all targets in order and includes partial matches
        // we can then calculate confidence based on how many targets were matched in order
        // a perfect match will have a Final Next link back to the source Thought and the source thought will have a link of linkType to the first element in the sequence
        // return the list of source thoughts and their confidence values

        List<(SeqElement seqNode, float confidence)> retVal = new();

        // Handle edge cases
        if (targets is null || targets.Count == 0) return retVal;
        if (targets[0] is null) return retVal;

        // Step 1: Find all sequence nodes that have targets[0] as their VLU
        // These are potential starting points for matching sequences

        // When this returns, seqNode is the first matching node.  curPos.Current is the last
        List<(SeqElement seqNode, IEnumerator<SeqElement>? curPos, int matchCount)> searchCandidates = RawSearchExact(targets, skipPlusEntries);
        if (searchCandidates.Count == 0) return retVal;

        if (mustMatchFirst)
        {
            for (int j = 0; j < searchCandidates.Count; j++)
                if (!IsSequenceFirstElement(searchCandidates[j].seqNode))
                {
                    searchCandidates.RemoveAt(j);
                    j--;
                }
            searchCandidates = searchCandidates.Where(c => IsSequenceFirstElement(c.seqNode)).ToList();
        }


        if (mustMatchLast)
        {
            for (int j = 0; j < searchCandidates.Count; j++)
                if (!IsSequenceLastElement(searchCandidates[j].curPos.Current))
                {
                    searchCandidates.RemoveAt(j);
                    j--;
                }
        }


        // Step 3: Calculate confidence and find the Thoughts that reference these sequences
        foreach (var candidate in searchCandidates)
        {
            // Find FRST link from the candidate node to get the sequence's first node
            SeqElement firstSeqNode = (SeqElement)candidate.seqNode.FRST;

            if (firstSeqNode is null) continue;

            // Find all Thoughts that reference this sequence (have links pointing to firstSeqNode)
            var referencingThoughts = firstSeqNode.LinksFrom?.Where(r => (linkType is null || r.LinkType == linkType))
                .ToList();

            // Recursively add sequences that reference these
            if (referencingThoughts is not null)
            {
                var allReferencingThoughts = new List<Link>(referencingThoughts);
                AddReferencingSequences(referencingThoughts, linkType, allReferencingThoughts);
                referencingThoughts = allReferencingThoughts;
            }

            if (referencingThoughts is not null)
            {
                foreach (var refRel in referencingThoughts)
                {
                    if (refRel.From is not null)
                    {
                        firstSeqNode = (SeqElement)refRel.To;
                        // Calculate confidence
                        float confidence;
                        if (candidate.matchCount == targets.Count)
                        {
                            // Full match - check if it's a complete sequence
                            int actualSequenceLength = GetSequenceLength(firstSeqNode);

                            if (actualSequenceLength == targets.Count)
                                confidence = 1.0f; // Perfect complete match
                            else
                                // Matched all targets but sequence is longer than pattern
                                confidence = (float)candidate.matchCount / actualSequenceLength;
                        }
                        else
                        {
                            // Partial match
                            confidence = (float)candidate.matchCount / targets.Count;
                        }

                        // Add the link from the referencing Thought to the sequence
                        retVal.Add(new(firstSeqNode, confidence));
                    }
                }
            }
        }

        // Sort by confidence descending and remove duplicates
        retVal = retVal
            .GroupBy(x => x.seqNode)
            .Select(g => g.OrderByDescending(x => x.confidence).First())
            .OrderByDescending(x => x.confidence)
            .ToList();

        return retVal;
    }

    public List<(SeqElement seqNode, float confidence)> RawAnchoredFuzzyMatch(List<Thought> targets)
    {
        List<(SeqElement seqNode, float confidence)> matches = new();
        if (targets is null || targets.Count < 2) return matches;

        //get direct sequences
        var candidateNodes = targets[0].LinksFrom
            .Where(r => r.LinkType?.Label == "VLU" && IsSequenceFirstElement(r.From))
            .Select(r=>r.From)
            .ToList();
        //add in sequences which refer to this at the beginning
        for (int i = 0; i < candidateNodes.Count; i++)
        {
            Thought candidate = candidateNodes[i];
            var referrers = candidate.LinksFrom.Where(x => x.LinkType.Label == "VLU" && IsSequenceFirstElement(x.From));
            foreach (Link referrer in referrers)
                candidateNodes.Add(referrer.From);
        };

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
        float score = count / (Math.Max(seq.Count,targets.Count) - 1);
        return score;
    }
    private List<(SeqElement seqNode, IEnumerator<SeqElement>? curPos, int matchCount)> RawSearchExact(List<Thought> targets, bool skipPlusEntries = false)
    {
        List<(SeqElement seqNode, IEnumerator<SeqElement>? curPos, int matchCount)> searchCandidates = new();
        if (targets is null || targets.Count < 2) return searchCandidates;
        //Step 1: initialize enuerators for each candidate sequence
        var candidateNodes = targets[0].LinksFrom
            .Where(r => r.LinkType?.Label == "VLU")
            .Select(r => (seqNode: r.From, matchedCount: 1))
            .ToList();
        foreach (var candidate in candidateNodes)
        {
            var enumerator = EnumerateSequenceElements((SeqElement)candidate.seqNode, skipPlusEntries).GetEnumerator();
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
                SeqElement? nextThought = null;
                Thought theValue = null;
                //have we reached the end of the current subsequence?
                if (!searchCandidates[j].curPos.MoveNext())
                {
                    var referrers = GetAllFollowingNodes(searchCandidates[j].seqNode);
                    foreach (var referrer in referrers)
                    {
                        var x = searchCandidates.FindFirst(x => x.seqNode == referrer);
                        if (x.seqNode is null)
                            searchCandidates.Add(new(referrer, EnumerateSequenceElements(referrer, skipPlusEntries).GetEnumerator(), searchCandidates[j].matchCount));
                    }
                    searchCandidates.RemoveAt(j);
                    j--;
                    continue;
                }
                nextThought = searchCandidates[j].curPos.Current;
                // Check if the next thought matches the current target
                if (GetElementValue(nextThought) != currentTarget)
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

    /// <summary>
    /// Flatten a sequence into a list of leaf Thoughts (letters). Handles nested sequences via VLU.
    /// </summary>
    public List<Thought> FlattenSequence(SeqElement sequenceStart, bool skipPlusValues = false)
    {
        if (sequenceStart.Label.StartsWith("abstr"))
        { }
        //experimentating with an enumartor for sequences
        List<Thought> result = new();
        var e = EnumerateSequenceElements(sequenceStart, skipPlusValues).GetEnumerator();
        while (e.MoveNext())
            result.Add(GetElementValue(e.Current));
        //foreach (Thought t in EnumerateSequenceElements(sequenceStart))
        //    result.Add(t);
        return result;
    }

    /// <summary>
    /// Enumerates all leaf elements in a sequence, recursively traversing into subsequences.
    /// Protected against circular subsequence references.
    /// </summary>
    /// <param name="sequenceStart">The first node of the sequence.</param>
    /// <param name="visitedSequences">Optional stack to track visited sequences across recursion.</param>
    /// <param name="skipPlusValues">When true, elements whose VLU label is "+" are skipped.</param>
    /// <returns>Leaf sequence elements in order.</returns>
    public IEnumerable<SeqElement> EnumerateSequenceElements(SeqElement sequenceStart, bool skipPlusValues = false, Stack<SeqElement> visitedSequences = null)
    {
        if (sequenceStart is null) yield break;

        // Initialize visited sequences tracker if this is the top-level call
        if (visitedSequences is null) visitedSequences = new();
        if (visitedSequences.Contains(sequenceStart)) yield break; // Already visited this sequence, stop to prevent infinite recursion
        visitedSequences.Push(sequenceStart);
        var current = sequenceStart;

        while (current is not null)
        {
            // Get the VLU Linkto find what this sequence node points to
            Thought valueRel = GetElementValue(current);

            if (valueRel is SeqElement s)
            {
                // Recursively enumerate the subsequence, passing the shared visitedSequences set
                foreach (var subElement in EnumerateSequenceElements(s, skipPlusValues, visitedSequences))
                    yield return subElement;
            }
            else
            {
                // It's a leaf element, return it unless we're skipping over it
                if (!skipPlusValues || valueRel?.Label != "+") yield return current;
            }

            // Move to next node via NXT Link
            current = GetNextElement(current);
            if (current is null) break;

            // Stop if we've circled back to the start
            if (current == sequenceStart) break;  //BROKEN?
        }
        visitedSequences.Pop();
    }
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
                ?.Where(r => ((linkType is null || r.LinkType == linkType) &&
                              r.LinkType?.Label != "FRST" &&
                              !accumulator.Contains(r)))
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
}

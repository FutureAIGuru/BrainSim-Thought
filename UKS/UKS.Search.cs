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

// Searching by attributes: which known Thought most resembles a given one, judged by the links they
// share (a recognizer's question: what is this thing I see?).
public partial class UKS
{
    /// <summary>
    /// Finds the Thoughts that most resemble <paramref name="target"/> by its links, e.g. an observed
    /// object compared with known ones.
    /// </summary>
    /// <param name="target">The links of this (often unstored) Thought are the attributes to match.</param>
    /// <param name="root">Only descendants of this Thought are candidates; an unknown label finds none.</param>
    /// <returns>The candidates with a confidence, best first.</returns>
    public IReadOnlyList<(Thought t, float conf)> SearchByAttributes(Thought target, ThoughtRef root)
    {
        List<(Thought t, float conf)> retVal = new();
        Thought? scope = root.Require(this);
        if (scope is null || target.LinksTo.Count == 0) return retVal;
        //initialize the search queues
        List<Thought> thoughtsToSearch = new();
        List<Thought> alreadySearched = new();
        Dictionary<Thought, float> searchCandidates = new();
        //Nothing changes the UKS during a search, so these answers are computed once per Thought.
        //(They are asked for every link visited, and each answer walks the ancestors.)
        Dictionary<Thought, bool> inheritableLinkTypes = new(ReferenceEqualityComparer.Instance);
        Dictionary<Thought, bool> underRoot = new(ReferenceEqualityComparer.Instance);
        bool IsInheritable(Thought? linkType)
        {
            if (linkType is null) return false;
            if (!inheritableLinkTypes.TryGetValue(linkType, out bool value))
                inheritableLinkTypes[linkType] = value = linkType.HasProperty("inheritable");
            return value;
        }
        bool IsUnderRoot(Thought t)
        {
            if (!underRoot.TryGetValue(t, out bool value))
                underRoot[t] = value = t.HasAncestor(scope);
            return value;
        }

        //seed the search queue with the given parameters.
        foreach (Link r in target.LinksTo)
        {
            if (r.To is SeqElement s)
            {
                var x = FlattenSequence(s);  //if this is a sequence fragment, try to get the whole sequence
                if (r.LinkType is null) continue;

                // Keep only stored sequences referred to through the same link type.
                var y = FindSequencesByActivation(x, Labeled("ExactSequenceSearch"))
                    .Where(result => result.seqNode.LinksFrom.Any(link => link.LinkType == r.LinkType))
                    .ToList();

                foreach (var z in y)
                {
                    foreach (var w in z.seqNode.LinksFrom.Where(x => x.From != target))
                    {
                        if (w.From is null) continue;
                        var existing = thoughtsToSearch.FirstOrDefault(x => x == w.From);
                        if (r.LinkType is not null &&
                            (w.LinkType == r.LinkType || w.LinkType?.HasAncestor(r.LinkType) == true) &&
                            existing is null)
                        {
                            thoughtsToSearch.Add(w.From);
                            if (!searchCandidates.ContainsKey(w.From))
                                searchCandidates[w.From] = 0; //initialize a new dictionary entry if needed
                            searchCandidates[w.From] += w.Weight * r.Weight * z.confidence;
                        }
                        else if (existing is not null)
                        {
                            searchCandidates[w.From] += w.Weight * r.Weight * z.confidence;
                        }
                    }
                }
            }
            if (r.To is null) continue;
            foreach (Link r1 in r.To.LinksFrom)
            {
                if (r1.From == target || r1.From is null) continue;
                var existing = thoughtsToSearch.FirstOrDefault(x => x == r1.From);
                if (r.LinkType is not null &&
                    (r1.LinkType == r.LinkType || r1.LinkType?.HasAncestor(r.LinkType) == true) &&
                    IsUnderRoot(r1.From) &&
                    r1.To == r.To && existing is null)
                {
                    thoughtsToSearch.Add(r1.From);
                    if (!searchCandidates.ContainsKey(r1.From))
                        searchCandidates[r1.From] = 0; //initialize a new dictionary entry if needed
                    searchCandidates[r1.From] += r1.Weight * r.Weight;
                }
                else if (existing is not null)
                {
                    searchCandidates[r1.From] += r1.Weight * r.Weight;
                }
            }
        }

        int inheritedValueCount = 0;
        //fan out from these seeds following all "inheritable" reverse connections.
        while (thoughtsToSearch.Count > 0)
        {
            var t = thoughtsToSearch[0];
            thoughtsToSearch.RemoveAt(0);
            alreadySearched.Add(t);
            foreach (Link r in t.LinksFrom)
            {
                if (!IsInheritable(r.LinkType)) continue;
                if (r.From == target || r.From is null) continue;
                AddToQueues(t, r.From);
                inheritedValueCount++;
                // TODO: also follow isSimilarTo (and transitive) links.
            }
        }

        if (inheritedValueCount > 0)
        {
            foreach (var key in searchCandidates.ToList())
            {
                if (!ThoughtsHaveConflictingLink(key.Key, target)) continue;
                searchCandidates[key.Key] = searchCandidates[key.Key] - .5f;
            }
            if (searchCandidates.Count == 0)
                return retVal;
            // delete items which have ancestor in list too
            for (int i = 0; i < searchCandidates.Keys.Count; i++)
            {
                Thought t = searchCandidates.Keys.ToList()[i];
                foreach (Thought t1 in t.Ancestors.Prepend(t))
                {
                    if (t1 != t && searchCandidates.ContainsKey(t1) && searchCandidates[t1] < 0)
                        searchCandidates.Remove(t);
                }
            }

        }

        //create the output list
        var ordered = searchCandidates.OrderByDescending(kv => kv.Value);
        foreach (var kv in ordered)
            retVal.Add((kv.Key, kv.Value));

        return retVal;

        bool AddToQueues(Thought tPrev, Thought tNew)
        {
            if (!IsUnderRoot(tNew)) return false;
            if (!searchCandidates.ContainsKey(tNew))
                searchCandidates[tNew] = 0; //initialize a new dictionary entry if needed
            searchCandidates[tNew] += searchCandidates[tPrev] * GetLinkWeight(tNew, tPrev);
            if (alreadySearched.FirstOrDefault(x => x == tNew) is not null) return false;
            if (thoughtsToSearch.FirstOrDefault(x => x == tNew) is not null) return false;
            thoughtsToSearch.Add(tNew);
            return true;
        }
    }

    // The weight of the first link between two Thoughts, or 0 if there is none.
    private static float GetLinkWeight(Thought t1, Thought t2)
    {
        foreach (var r in t1.LinksTo)
            if (r.To == t2) return r.Weight;
        foreach (var r in t1.LinksFrom)
            if (r.To == t2) return r.Weight;
        return 0;
    }

    private bool ThoughtsHaveConflictingLink(Thought source, Thought target)
    {
        foreach (Link r1 in source.LinksTo)
            foreach (Link r2 in target.LinksTo)
                if (LinksAreExclusive(r1, r2))
                    return true;
        return false;
    }
}

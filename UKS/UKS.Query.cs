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

using System.Collections.Frozen;

namespace UKS;

public partial class UKS
{
    //keeps track of the conditions of the previous query in order to answer "Why?" or "Why not?"
    List<Link> failedConditions = new();
    List<Link> succeededConditions = new();

    /// <summary>
    /// Gets all links to a group of Thoughts including inherited links.
    /// </summary>
    /// <param name="sources">Thoughts that seed the search for related links.</param>
    /// <returns>List of matching links.</returns>
    public List<Link> GetAllLinks(List<Thought> sources) //with inheritance, conflicts, etc
    {
        List<Link> result2 = new();
        if (sources.Count == 0) return result2;
        //expand search list to include instances of given objects  WHY??
        for (int i = 0; i < sources.Count; i++)
        {
            Thought t = sources[i];
            foreach (Thought child in t.Children)
            {
                Thought? isInstance = "isInstance";
                if (isInstance is not null && child.HasProperty(isInstance))
                    sources.Add(child);
            }
        }

        var querySources = sources.ToList();
        var result1 = BuildSearchList(sources);
        result2 = GetAllLinksInternal(result1);
        if (result2.Count < 200)  //the conflict-remover is really slow on large numbers
            RemoveConflictingResults(result2, querySources);
        RemoveFalseConditionals(result2);
        SortLinks(ref result2);
        return result2;
    }

    private void SortLinks(ref List<Link> result2)
    {
        result2 = result2.OrderByDescending(x => x.Weight).ToList();
    }

    //This is used to store temporary activation during queries
    private class ThoughtWithQueryParams
    {
        public Thought thought = null!;
        public int hopCount;
        public int haveCount = 1;
        public int hitCount = 1;
        public float weight;
        public Thought? reachedWith;
        public Thought? querySource;
        public bool corner = false;
        public override string ToString()
        {
            return (thought.Label + "  : " + hopCount + " : " + weight + "  Count: " +
                haveCount + " Hits: " + hitCount + " Corner: " + corner);
        }
    }

    // BFS along inheritable links (is-a chains, etc.) with a hop cap for transitive inheritance.
    private List<ThoughtWithQueryParams> BuildSearchList(List<Thought> q)
    {
        const int maxHops = 8;
        List<ThoughtWithQueryParams> thoughtsToExamine = new();
        HashSet<Thought> seen = new();

        foreach (Thought t in q)
        {
            if (t is null || !seen.Add(t)) continue;
            thoughtsToExamine.Add(new ThoughtWithQueryParams
            {
                thought = t,
                hopCount = 0,
                weight = 1,
                reachedWith = null,
                querySource = t
            });
        }

        for (int i = 0; i < thoughtsToExamine.Count; i++)
        {
            ThoughtWithQueryParams entry = thoughtsToExamine[i];
            Thought t = entry.thought;
            if (t is null) continue;

            int nextHop = entry.hopCount + 1;
            if (nextHop > maxHops) continue;

            foreach (Link r in t.LinksTo)
            {
                Thought? inheritable = "inheritable";
                if (inheritable is null || r.LinkType?.HasProperty(inheritable) != true || r.To is null)
                    continue;

                if (thoughtsToExamine.FindFirst(x => x.thought == r.To) is ThoughtWithQueryParams twgp)
                {
                    twgp.hitCount++;
                    if (nextHop < twgp.hopCount)
                        twgp.hopCount = nextHop;
                }
                else
                {
                    bool corner = entry.reachedWith is not null &&
                        r.LinkType is not null &&
                        !ThoughtInTree(r.LinkType, entry.reachedWith);
                    entry.corner |= corner;
                    ThoughtWithQueryParams thoughtToAdd = new()
                    {
                        thought = r.To,
                        hopCount = nextHop,
                        weight = entry.weight * r.Weight,
                        reachedWith = r.LinkType,
                        querySource = entry.querySource ?? entry.thought,
                    };
                    int val = GetCount(r.LinkType);
                    thoughtToAdd.haveCount = entry.haveCount * val;
                    thoughtsToExamine.Add(thoughtToAdd);
                    seen.Add(r.To);
                }
            }
        }
        return thoughtsToExamine;
    }

    private List<Link> GetLinksBetween(Thought t1, Thought t2)
    {
        List<Link> retVal = new();
        foreach (Link r in t1.LinksTo)
            if (r.To == t2) retVal.Add(r);
        foreach (Link r in t1.LinksFrom)
            if (r.To == t2) retVal.Add(r);
        foreach (Link r in t2.LinksTo)
            if (r.To == t1) retVal.Add(r);
        foreach (Link r in t2.LinksFrom)
            if (r.To == t1) retVal.Add(r);
        return retVal;
    }

    private List<Link> GetAllLinksInternal(List<ThoughtWithQueryParams> thoughtsToExamine)
    {
        List<Link> result = new();
        for (int i = 0; i < thoughtsToExamine.Count; i++)
        {
            Thought t = thoughtsToExamine[i].thought;
            if (t is null) continue; //safety
            int haveCount = thoughtsToExamine[i].haveCount;
            int inheritanceDepth = thoughtsToExamine[i].hopCount;
            Thought? querySource = thoughtsToExamine[i].querySource ?? t;
            foreach (Link r in t.LinksTo)
            {
                if (r.LinkType == Thought.IsA) continue;
                //only add the new relationship to the list if it is not already in the list
                bool ignoreSource = thoughtsToExamine[i].hopCount > 1;
                Link? existing = result.FindFirst(x => LinksAreEqual(x, r, ignoreSource));
                if (existing is not null) continue;

                Thought? hasAncestor = "has";
                if (haveCount > 1 && hasAncestor is not null && r.LinkType?.HasAncestor(hasAncestor) == true)
                {
                    if (r.From is null || r.LinkType is null || r.To is null) continue;
                    Link r1 = new Link(querySource, r.LinkType, r.To)
                    {
                        Weight = r.Weight * thoughtsToExamine[i].weight,
                        InheritanceDepth = inheritanceDepth,
                        InheritedFromCategory = inheritanceDepth > 0 ? t : null
                    };
                    Thought? newCountType = GetOrAddThought((GetCount(r.LinkType) * haveCount).ToString(), "number");

                    //hack for numeric labels
                    Thought? rootThought = r1.LinkType;
                    if (r.LinkType.Label.Contains("."))
                        rootThought = GetOrAddThought(r.LinkType.Label.Substring(0, r.LinkType.Label.IndexOf(".")));
                    Thought? bestMatch = r.LinkType;
                    List<Thought> missingAttributes = new();
                    Thought? newLinkType = null;
                    if (rootThought is not null && newCountType is not null)
                    {
                        newLinkType = SubclassExists(rootThought, new List<Thought> { newCountType }, ref bestMatch, ref missingAttributes);
                        if (newLinkType is null)
                            newLinkType = CreateSubclass(rootThought, new List<Thought> { newCountType });
                    }
                    if (newLinkType is null) continue;
                    r1.LinkType = newLinkType;
                    result.Add(r1);
                }
                else
                {
                    if (r.From is null || r.LinkType is null || r.To is null) continue;
                    Link r1 = new Link(querySource, r.LinkType, r.To)
                    {
                        Weight = r.Weight * thoughtsToExamine[i].weight,
                        InheritanceDepth = inheritanceDepth,
                        InheritedFromCategory = inheritanceDepth > 0 ? t : null
                    };
                    foreach (Link r3 in r.LinksTo.Where(x => x.LinkType?.Label != "is-a"))
                    {
                        if (r3.LinkType is not null)
                            r1.AddLink(r3.LinkType, r3.To);
                    }
                    result.Add(r1);
                }
            }
        }
        return result;
    }

    // Ch.5 exception rule: more-specific (lower inheritance depth) wins over inherited defaults.
    // Tie-break: link From on a query source, then higher Weight.
    private void RemoveConflictingResults(List<Link> result, List<Thought> querySources)
    {
        for (int i = 0; i < result.Count; i++)
        {
            Link r1 = result[i];

            //remove properties from the results list (they are internal)
            if (r1.LinkType?.Label == "hasProperty")
            {
                result.RemoveAt(i);
                i--;
                continue;
            }
            for (int j = i + 1; j < result.Count; j++)
            {
                Link r2 = result[j];
                bool duplicate = r1.LinkType == r2.LinkType && r1.To == r2.To;
                bool exclusive = LinksAreExclusive(r1, r2);
                if (!duplicate && !exclusive) continue;

                Link keep = PreferMoreSpecificLink(r1, r2, querySources);
                Link drop = keep == r1 ? r2 : r1;
                int dropIndex = drop == r1 ? i : j;
                result.RemoveAt(dropIndex);
                if (dropIndex == i)
                {
                    i--;
                    break;
                }
                j--;
            }
        }
    }

    private static Link PreferMoreSpecificLink(Link r1, Link r2, List<Thought> querySources)
    {
        if (r1.InheritanceDepth != r2.InheritanceDepth)
            return r1.InheritanceDepth < r2.InheritanceDepth ? r1 : r2;

        bool r1OnSource = querySources.Contains(r1.From);
        bool r2OnSource = querySources.Contains(r2.From);
        if (r1OnSource != r2OnSource)
            return r1OnSource ? r1 : r2;

        return r1.Weight >= r2.Weight ? r1 : r2;
    }

    private void RemoveFalseConditionals(List<Link> result)
    {
        for (int i = 0; i < result.Count; i++)
        {
            Link r1 = result[i];
            Thought? isResult = "isResult";
            if (isResult is null || !r1.HasProperty(isResult)) continue;
            if (!ConditionsAreMet(r1))
            {
                failedConditions.Add(r1);
                result.RemoveAt(i);
                i--;
            }
            else
            {
                succeededConditions.Add(r1);
            }
        }
    }

    /// <summary>
    /// Filters a list of Links returning only those with at least one component which has an ancestor in the list of ancestors.
    /// </summary>
    /// <param name="result">List of links from a previous query.</param>
    /// <param name="ancestors">Ancestor filter list.</param>
    /// <returns>Filtered list containing only links that match the ancestor filter.</returns>
    public IReadOnlyList<Link> FilterResults(List<Link> result, List<Thought> ancestors)
    {
        List<Link> retVal = new();
        if (ancestors is null || ancestors.Count == 0)
            return result;
        foreach (Link r in result)
            if (LinkHasAncestor(r, ancestors))
                retVal.Add(r);
        return retVal;
    }

    private bool LinkHasAncestor(Link r, List<Thought> ancestors)
    {
        foreach (Thought ancestor in ancestors)
        {
            if (r.From?.HasAncestor(ancestor) == true) return true;
            if (r.LinkType?.HasAncestor(ancestor) == true) return true;
            if (r.To?.HasAncestor(ancestor) == true) return true;
        }
        return false;
    }

    int GetCount(Thought t)
    {
        int retVal = 1;
        foreach (Link r in t.LinksTo)
            if (r.LinkType?.Label == "is")
                if (int.TryParse(r.To?.Label, out int val))
                    return val;
        return retVal;
    }

    bool ConditionsAreMet(Link r)
    {
        Thought? isResult = "isResult";
        Thought? isCondition = "isCondition";
        foreach (Link r1 in r.LinksTo)
        {
            if (isResult is null || r1.From?.HasProperty(isResult) != true) continue;
            if (isCondition is null || r1.To?.HasProperty(isCondition) != true) continue;

            Link? r2 = r1.To as Link;
            //is r1 true?
            if (GetUnconditionalLink(r2) is null)
                return false;
        }
        return true;
    }

    Link? GetUnconditionalLink(Link? r)
    {
        if (r?.From is null) return null;
        Thought? isCondition = "isCondition";
        foreach (Link r1 in r.From.LinksTo)
        {
            if (Equals(r, r1))
            {
                if (isCondition is null || !r1.HasProperty(isCondition))
                    return r1;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a list of Links which were false in the previous query.
    /// </summary>
    /// <returns>Links that failed conditional evaluation.</returns>
    public List<Link> WhyNot()
    {
        return failedConditions;
    }
    /// <summary>
    /// Returns a list of Links which were true in the previous query.
    /// </summary>
    /// <returns>Links that succeeded conditional evaluation.</returns>
    public List<Link> Why()
    {
        return succeededConditions;
    }

    Dictionary<Thought, float> searchCandidates = null!;

    /// <summary>
    /// Search for the Thought which most closely resembles the target Thought based on the attributes of the target.
    /// </summary>
    /// <param name="target">The links of this Thought are the attributes to search on.</param>
    /// <param name="root">All searching is done within the descendants of this Thought.</param>
    /// <param name="confidence">Unused output parameter reserved for match quality (not currently assigned).</param>
    /// <returns>Ordered list of candidate thoughts with confidence scores.</returns>
    public List<(Thought t, float conf)> SearchForClosestMatch(Thought target, Thought root)
    {
        List<(Thought t, float conf)> retVal = new();
        if (target.LinksTo.Count == 0) return retVal;
        //initialize the search queues
        List<Thought> thoughtsToSearch = new();
        List<Thought> alreadySearched = new();
        searchCandidates = new();

        //seed the search queue with the given parameters.
        foreach (Link r in target.LinksTo)
        {
            if (r.To is SeqElement s)
            {
                var x = FlattenSequence(s);  //if this is a sequence fragment, try to get the whole sequence
                if (r.LinkType is null) continue;

                // Use FindSequencesByActivation instead of HasSequence
                Thought searchOptions = Labeled("ExactSequenceSearch");
                var y = FindSequencesByActivation(x, searchOptions);
                // Filter by the linkType from the link
                if (r.LinkType is not null)
                {
                    y = y.Where(result => 
                        result.seqNode.LinksFrom.Any(link => link.LinkType == r.LinkType))
                        .ToList();
                }

                foreach (var z in y)
                {
                    foreach (var w in z.seqNode.LinksFrom.Where(x => x.From != target))
                    {
                        if (w.From is null) continue;
                        var existing = thoughtsToSearch.FindFirst(x => x == w.From);
                        if (r.LinkType is not null &&
                            (w.LinkType == r.LinkType || w.LinkType?.HasAncestor(r.LinkType) == true) &&
                            r.To == r.To && existing is null)
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
            foreach (Link r1 in r.To?.LinksFrom ?? Enumerable.Empty<Link>())
            {
                if (r1.From == target || r1.From is null) continue;
                var existing = thoughtsToSearch.FindFirst(x => x == r1.From);
                if (r.LinkType is not null &&
                    (r1.LinkType == r.LinkType || r1.LinkType?.HasAncestor(r.LinkType) == true) &&
                    r1.From.HasAncestor(root) &&
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
        //fan out from these seeds following all "inheritable" reverse connections.
        while (thoughtsToSearch.Count > 0)
        {
            var t = thoughtsToSearch[0];
            thoughtsToSearch.RemoveAt(0);
            alreadySearched.Add(t);
            foreach (Link r in t.LinksFrom)
            {
                Thought? inheritable = "inheritable";
                if (inheritable is null || r.LinkType?.HasProperty(inheritable) != true) continue;
                if (r.From == target || r.From is null) continue;
                AddToQueues(t, r.From);
                //TODO fix this to handle isSimilarTo  (and transitive...?)
                //var similarThoughts = GetListOfSimilarThoughts(r.source);
                //foreach (Thought t1 in similarThoughts)
                //    AddToQueues(t, t1);
            }
        }

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
            foreach (Thought t1 in t.AncestorsWithSelf)
            {
                if (t1 != t && searchCandidates.ContainsKey(t1) && searchCandidates[t1] < 0)
                    searchCandidates.Remove(t);
            }
        }

        //create the output list
        var ordered = searchCandidates.OrderByDescending(kv => kv.Value);
        foreach (var kv in ordered)
            retVal.Add((kv.Key, kv.Value));

        return retVal;

        bool AddToQueues(Thought tPrev, Thought tNew)
        {
            if (!tNew.HasAncestor(root)) return false;
            if (!searchCandidates.ContainsKey(tNew))
                searchCandidates[tNew] = 0; //initialize a new dictionary entry if needed
            searchCandidates[tNew] += searchCandidates[tPrev] * GetLinkWeight(tNew, tPrev);
            if (alreadySearched.FindFirst(x => x == tNew) is not null) return false;
            if (thoughtsToSearch.FindFirst(x => x == tNew) is not null) return false;
            thoughtsToSearch.Add(tNew);
            return true;
        }
    }

    /// <summary>
    /// DEPRECATED: Helper for SearchForClosestMatch that gets the weight of the link between two Thoughts.
    /// </summary>
    /// <param name="t1">First thought.</param>
    /// <param name="t2">Second thought.</param>
    /// <returns>Weight of the direct link between the two thoughts, or 0 if none.</returns>
    public float GetLinkWeight(Thought t1, Thought t2)
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

    private bool LinksAreSimilar(Link r1, Link r2)
    {
        if (r1.LinkType != r2.LinkType) return false;
        if (r1.To is null || r2.To is null) return false;
        if (FindCommonParents(r1.To, r2.To).Count == 0) return false;
        return true;
    }

    /// <summary>
    /// DEPRECATED Determines whether two thoughts share at least one similar link (same link type and compatible targets).
    /// </summary>
    /// <param name="source">First thought to compare.</param>
    /// <param name="target">Second thought to compare.</param>
    /// <returns>True if a similar link exists; otherwise false.</returns>
    public bool ThoughtsHaveSimilarLink(Thought source, Thought target)
    {
        foreach (Link r1 in source.LinksTo)
            foreach (Link r2 in target.LinksTo)
                if (LinksAreSimilar(r1, r2))
                    return true;
        return false;
    }

    /// <summary>
    /// Searches for links matching the specified criteria. Null parameters act as wildcards.
    /// </summary>
    /// <param name="from">The source thought, or null to match any source.</param>
    /// <param name="linkType">The link type, or null to match any link type.</param>
    /// <param name="to">The target thought, or null to match any target.</param>
    /// <returns>List of links matching all specified (non-null) criteria.</returns>
    public List<Link> SearchForRelationships(Link l)
    {
        List<Link> results = new List<Link>();
        Thought? from = l.From?.Label.Contains("??") is true ? null : l.From;
        Thought? linkType = l.LinkType?.Label.Contains("??") is true ? null : l.LinkType;
        Thought? to = l.To?.Label.Contains("??") is true ? null : l.To;
        if (from is null && to is null && linkType is null) return results;

        // hack to handle is-a searches
        if (linkType is not null && linkType.Label == "is-a" && from is not null)
        {
            foreach (Thought child in from.Parents)
            {
                results.Add(new Link { From = from, LinkType = linkType, To = child });
            }
            return results;
        }
           // If from is specified, start there for efficiency (most constrained search)
        if (from is not null)
        {
            var attribs = GetAllLinks(new List<Thought> { from });
            foreach (Link link in attribs)
            {
                if ((linkType is null || link.LinkType?.HasAncestor(linkType) == true) &&
                    (to is null || link.To == to))
                {
//                    results.Add(link);
                    results.Add(new Link
                    {
                        From = from,
                        LinkType = link.LinkType,
                        To = link.To,
                        Weight = link.Weight,
                        InheritanceDepth = link.InheritanceDepth,
                        InheritedFromCategory = link.InheritedFromCategory
                    });
                }
            }
        }
        // If from is null but to is specified, search backwards from to
        else if (to is not null)
        {
            foreach (Link link in to.LinksFrom)
            {
                if (linkType is null || link.LinkType == linkType)
                {
                    results.Add(link);
                }
            }
        }
        return results;
    }
}

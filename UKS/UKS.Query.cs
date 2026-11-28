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
    //keeps track of the conditions of the previous query in order to answer "Why?" or "Why not?"
    List<Link> failedConditions = new();
    List<Link> succeededConditions = new();
    private int conditionalQueryDepth = 0;
    private readonly HashSet<Link> evaluatingConditions = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Gets all links to a single Thought including inherited links.
    /// </summary>
    /// <param name="source">Thought that seeds the search for related links.</param>
    /// <param name="filter">Optional partially populated link filter. When set, LinkType and To each require the corresponding result component to have that ancestor, self-inclusive.</param>
    /// <param name="maxResults">Optional maximum number of results to return (0 = no limit).</param>
    /// <returns>List of matching links.</returns>
    public List<Link> GetAttributes(ThoughtRef sourceRef, Link? filter = null, int maxResults = 0) //with inheritance, conflicts, etc
    {
        Thought? source = sourceRef.Find(this);
        if (conditionalQueryDepth == 0)
        {
            failedConditions.Clear();
            succeededConditions.Clear();
        }
        conditionalQueryDepth++;
        try
        {
            List<Link> result2 = new();
            if (source is null) return result2;

            List<Thought> sources = new() { source };
            var querySources = sources.ToList();
            var result1 = BuildSearchList(sources);
            result2 = GetAllLinksInternal(result1, out var foundIn);

            // An unsupported local rule must not suppress an otherwise valid fact/default.
            RemoveFalseConditionals(result2);
            if (result2.Count < 200)  //the conflict-remover is really slow on large numbers
                RemoveConflictingResults(result2, querySources, foundIn);
            // Filter after resolving conditions and conflicts; excluded exceptions must
            // still be able to suppress inherited defaults, and proven types are ordinary.
            if (filter is not null)
            {
                result2 = result2.Where(link =>
                    (filter.LinkType is null || link.LinkType?.HasAncestor(filter.LinkType) == true) &&
                    (filter.To is null || link.To?.HasAncestor(filter.To) == true)).ToList();
            }
            SortLinks(ref result2);
            // Apply max results limit if specified
            if (maxResults > 0 && result2.Count > maxResults)
                result2 = result2.Take(maxResults).ToList();
            return result2;
        }
        finally
        {
            conditionalQueryDepth--;
        }
    }

    private void SortLinks(ref List<Link> result2)
    {
        result2 = result2.OrderByDescending(x => x.Weight).ToList();
    }

    // Temporary activation of one Thought reached during a query.
    private class ThoughtWithQueryParams
    {
        public Thought thought = null!;
        public int hopCount;
        public int haveCount = 1;
        public float weight;
        public Thought? querySource;
        public override string ToString() =>
            thought.Label + "  : " + hopCount + " : " + weight + "  Count: " + haveCount;
    }

    // How far up inheritable links (is-a chains, etc.) a query and its evidence look.
    private const int MaxInheritanceHops = 8;

    // BFS along inheritable links (is-a chains, etc.) with a hop cap for transitive inheritance.
    private List<ThoughtWithQueryParams> BuildSearchList(List<Thought> q)
    {
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
                querySource = t
            });
        }

        //follow inheritable links to find all related thoughts, up to a maximum number of hops
        for (int i = 0; i < thoughtsToExamine.Count; i++)
        {
            ThoughtWithQueryParams entry = thoughtsToExamine[i];
            Thought t = entry.thought;
            if (t is null) continue;

            int nextHop = entry.hopCount + 1;
            if (nextHop > MaxInheritanceHops) continue;

            foreach (Link r in t.LinksTo)
            {
                Thought? inheritable = Labeled("inheritable");
                if (inheritable is null || r.LinkType is not Thought linkType || !linkType.HasProperty(inheritable) || r.To is null)
                    continue;

                if (thoughtsToExamine.FindFirst(x => x.thought == r.To) is ThoughtWithQueryParams twgp)
                {
                    if (nextHop < twgp.hopCount)
                        twgp.hopCount = nextHop;
                }
                else
                {
                    thoughtsToExamine.Add(new ThoughtWithQueryParams
                    {
                        thought = r.To,
                        hopCount = nextHop,
                        weight = entry.weight * r.Weight,
                        querySource = entry.querySource ?? entry.thought,
                        haveCount = entry.haveCount * GetCount(linkType),
                    });
                    seen.Add(r.To);
                }
            }
        }
        return thoughtsToExamine;
    }

    // Every link reachable from the query sources, as new result Links. foundIn records where
    // each result was found, which decides conflicts (the nearer wins).
    private List<Link> GetAllLinksInternal(List<ThoughtWithQueryParams> thoughtsToExamine,
        out Dictionary<Link, ThoughtWithQueryParams> foundIn)
    {
        List<Link> result = new();
        foundIn = new(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < thoughtsToExamine.Count; i++)
        {
            Thought t = thoughtsToExamine[i].thought;
            if (t is null) continue; //safety
            int haveCount = thoughtsToExamine[i].haveCount;
            Thought? querySource = thoughtsToExamine[i].querySource ?? t;
            foreach (Link r in t.LinksTo)
            {
                if (r.LinkType == IsAType) continue;
                bool conditional = HasConditions(r);
                // A possible link supplies no evidence unless a conditional
                // supports it as a conclusion. Premises cannot assert their own truth.
                if (r.LinkType?.HasProperty("isPossible") == true && !conditional) continue;
                //only add the new link to the list if it is not already in the list
                bool ignoreSource = thoughtsToExamine[i].hopCount > 1;
                Link? existing = result.FindFirst(x => LinksAreEqual(x, r, ignoreSource));
                bool existingConditional = existing is not null && HasConditions(existing);
                if (existing is not null && !conditional && !existingConditional) continue;

                //this is a fun hack which allows us to say that mary has 10 fingers because mary has 2 hands and
                //each hand has 5 fingers.  The "has" link type is special-cased here to allow this kind of transitive counting.
                Thought? hasAncestor = Labeled("has");
                if (haveCount > 1 && hasAncestor is not null && r.LinkType?.HasAncestor(hasAncestor) == true)
                {
                    if (r.From is null || r.LinkType is null || r.To is null) continue;
                    Link r1 = new Link(querySource, r.LinkType, r.To)
                    {
                        Weight = r.Weight * thoughtsToExamine[i].weight,
                    };
                    Thought? newCountType = GetOrAddThought((GetCount(r.LinkType) * haveCount).ToString(), "number");

                    // A counted type (has.2) is counted again from its uncounted base (has).
                    Thought? rootThought = r.LinkType.Parents.Any(parent => parent.HasAncestor("number"))
                        ? r.LinkType.Parents.FirstOrDefault(parent => !parent.HasAncestor("number")) ?? r.LinkType
                        : r.LinkType;
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
                    foundIn[r1] = thoughtsToExamine[i];
                }
                else
                {
                    if (r.From is null || r.LinkType is null || r.To is null) continue;
                    Link r1 = new Link(querySource, r.LinkType, r.To)
                    {
                        Weight = r.Weight * thoughtsToExamine[i].weight,
                    };
                    foreach (Link r3 in r.LinksTo.Where(x => x.LinkType != IsAType))
                    {
                        if (r3.LinkType is not null)
                            r1.LinksToWriteable.Add(new Link(this) { From = r1, LinkType = r3.LinkType, To = r3.To });
                    }
                    result.Add(r1);
                    foundIn[r1] = thoughtsToExamine[i];
                }
            }
        }
        return result;
    }

    // Exception rule: the more specific (lower inheritance depth) wins over inherited defaults.
    // Tie-break: link From on a query source, then higher Weight.
    private void RemoveConflictingResults(List<Link> result, List<Thought> querySources,
        Dictionary<Link, ThoughtWithQueryParams> foundIn)
    {
        for (int i = 0; i < result.Count; i++)
        {
            Link r1 = result[i];

            //remove properties from the results list (they are internal)
            if (r1.LinkType == HasPropertyType && r1.LinkType is not null)
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

                Link keep = PreferMoreSpecificLink(r1, r2, querySources, foundIn);
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

    private static Link PreferMoreSpecificLink(Link r1, Link r2, List<Thought> querySources,
        Dictionary<Link, ThoughtWithQueryParams> foundIn)
    {
        int depth1 = foundIn.TryGetValue(r1, out var entry1) ? entry1.hopCount : 0;
        int depth2 = foundIn.TryGetValue(r2, out var entry2) ? entry2.hopCount : 0;
        if (depth1 != depth2)
            return depth1 < depth2 ? r1 : r2;

        bool r1OnSource = r1.From is Thought from1 && querySources.Contains(from1);
        bool r2OnSource = r2.From is Thought from2 && querySources.Contains(from2);
        if (r1OnSource != r2OnSource)
            return r1OnSource ? r1 : r2;

        return r1.Weight >= r2.Weight ? r1 : r2;
    }

    private void RemoveFalseConditionals(List<Link> result)
    {
        for (int i = 0; i < result.Count; i++)
        {
            Link r1 = result[i];
            if (!HasConditions(r1)) continue;
            if (!ConditionsAreMet(r1))
            {
                failedConditions.Add(r1);
                result.RemoveAt(i);
                i--;
            }
            else
            {
                // A satisfied possibility is returned as an ordinary link;
                // this changes the query result, never the stored rule or its premise.
                if (r1.LinkType?.HasProperty("isPossible") == true)
                {
                    Thought? ordinaryType = GetAssertionLinkType(r1.LinkType);
                    if (ordinaryType is null)
                    {
                        failedConditions.Add(r1);
                        result.RemoveAt(i--);
                        continue;
                    }
                    r1.LinkType = ordinaryType;
                }
                succeededConditions.Add(r1);
                if (r1.LinkType is not null)
                    r1.LinkType = GetAssertionLinkType(r1.LinkType);
            }
        }
    }

    private Thought GetAssertionLinkType(Thought possibleLinkType)
    {
        if (!possibleLinkType.HasProperty("isPossible")) return possibleLinkType;
        var ordinaryParents = possibleLinkType.Parents.Where(parent =>
            !parent.HasProperty("isPossible") && !parent.HasProperty("setRelationship")).ToList();
        return ordinaryParents.FirstOrDefault(parent => ordinaryParents.All(other => parent.HasAncestor(other)))
            ?? ordinaryParents.FirstOrDefault() ?? possibleLinkType;
    }

    /// <summary>
    /// Determines whether a link type represents a conditional clause.
    /// </summary>
    /// <param name="linkType">Link type to inspect.</param>
    /// <returns>True when the link type inherits the isConditional property.</returns>
    internal static bool IsConditionalLinkType(Thought? linkType)
    {
        return linkType?.HasProperty("isConditional") == true;
    }

    // A link type joining statements, not Thoughts: a condition (IF) or a conjunction (AND).
    internal static bool IsClauseLinkType(Thought? linkType) =>
        IsConditionalLinkType(linkType) || linkType?.HasProperty("isConjunction") == true;

    // A rule: the link has conditions (IF links) attached.
    internal static bool HasConditions(Link link) => link.LinksTo.Any(condition => IsConditionalLinkType(condition.LinkType));

    /// <summary>
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
        foreach (Thought parent in t.Ancestors.Prepend(t))
            if (parent.HasAncestor("number") && int.TryParse(parent.Label, out int val))
                return val;
        return retVal;
    }

    bool ConditionsAreMet(Link r)
    {
        bool foundCondition = false;
        foreach (Link r1 in r.LinksTo)
        {
            if (!IsConditionalLinkType(r1.LinkType)) continue;
            foundCondition = true;
            if (r1.To is not Link condition || GetUnconditionalLink(condition) is null)
                return false;
        }
        return foundCondition;
    }

    Link? GetUnconditionalLink(Link? r)
    {
        if (r?.From is null || r.LinkType is null || r.To is null) return null;
        // Re-entering the same premise cannot supply independent support for a rule.
        if (!evaluatingConditions.Add(r)) return null;
        try
        {
            if (r.LinkType.HasProperty("isConjunction"))
            {
                // Evaluate both branches so explanations include every failed leaf.
                bool left = r.From is Link leftCondition && GetUnconditionalLink(leftCondition) is not null;
                bool right = r.To is Link rightCondition && GetUnconditionalLink(rightCondition) is not null;
                return left && right ? r : null;
            }
            Thought relationshipType = r.LinkType;
            if (relationshipType.HasProperty("isPossible"))
            {
                Thought? ordinaryType = GetAssertionLinkType(relationshipType);
                if (ordinaryType is null) return null;
                relationshipType = ordinaryType;
            }
            // Use the established inheritance/conflict pipeline, so local exceptions also
            // override inherited conditions. is.not is matched as an ordinary type identity.
            Link? evidence = GetAttributes(r.From).FirstOrDefault(candidate =>
                ReferenceEquals(candidate.LinkType, relationshipType) && ReferenceEquals(candidate.To, r.To));
            if (evidence is null)
            {
                if (!failedConditions.Contains(r)) failedConditions.Add(r);
            }
            else if (!succeededConditions.Contains(r)) succeededConditions.Add(r);
            return evidence;
        }
        finally
        {
            evaluatingConditions.Remove(r);
        }
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

    /// <summary>
    /// The stored links that produce an attribute returned by <see cref="GetAttributes"/>: the
    /// inheritable links (is-a, ...) from the attribute's source up to the Thought that stores it,
    /// nearest first, then the stored link itself. For Fido's inherited fur:
    /// [Fido→is-a→dog], [dog→is-a→animal], [animal→has→fur]. A stored link alone means the attribute
    /// is stated on the source itself. A rule's conditions are the IF links on the stored link.
    /// </summary>
    /// <returns>The evidence, or an empty list if no stored link produces the attribute (a count the
    /// query computed, for example).</returns>
    public List<Link> GetEvidence(Link attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        List<Link> evidence = new();
        if (attribute.From is not Thought source) return evidence;
        Thought? inheritable = Labeled("inheritable");
        // Nearest first, as the query looks: the first Thought storing the attribute is where it came from.
        var reachedBy = new Dictionary<Thought, Link?>(ReferenceEqualityComparer.Instance) { [source] = null };
        var pending = new Queue<(Thought Thought, int Hops)>();
        pending.Enqueue((source, 0));
        while (pending.TryDequeue(out var entry))
        {
            if (entry.Thought.LinksTo.FirstOrDefault(link => Produces(link, attribute)) is Link stored)
            {
                for (Thought t = entry.Thought; reachedBy[t] is Link step; t = step.From!)
                    evidence.Insert(0, step);
                evidence.Add(stored);
                return evidence;
            }
            if (inheritable is null || entry.Hops >= MaxInheritanceHops) continue;
            foreach (Link link in entry.Thought.LinksTo)
            {
                if (link.LinkType?.HasProperty(inheritable) != true || link.To is not Thought next ||
                    reachedBy.ContainsKey(next)) continue;
                reachedBy[next] = link;
                pending.Enqueue((next, entry.Hops + 1));
            }
        }
        return evidence;
    }

    // True if a stored link produces the query result: the same type and target, or a rule's
    // possibility (is.?) that the query returned with the ordinary type; a rule's conditions must match.
    private bool Produces(Link stored, Link result)
    {
        if (ReferenceEquals(stored, result)) return true;
        if (!ReferenceEquals(stored.To, result.To) || stored.LinkType is not Thought storedType) return false;
        if (!ReferenceEquals(storedType, result.LinkType) &&
            !ReferenceEquals(GetAssertionLinkType(storedType), result.LinkType)) return false;
        List<Link> conditions = stored.LinksTo.Where(link => IsConditionalLinkType(link.LinkType)).ToList();
        List<Link> copied = result.LinksTo.Where(link => IsConditionalLinkType(link.LinkType)).ToList();
        return conditions.Count == copied.Count && copied.All(copy => conditions.Any(condition =>
            ReferenceEquals(condition.LinkType, copy.LinkType) && ReferenceEquals(condition.To, copy.To)));
    }

    /// <summary>
    /// Search for the Thought which most closely resembles the target Thought based on the attributes of the target.
    /// </summary>
    /// <param name="target">The links of this Thought are the attributes to search on.</param>
    /// <param name="rootRef">All searching is done within the descendants of this Thought.</param>
    /// <returns>Ordered list of candidate thoughts with confidence scores.</returns>
    public List<(Thought t, float conf)> SearchByAttributes(Thought target, ThoughtRef rootRef)
    {
        List<(Thought t, float conf)> retVal = new();
        Thought? root = rootRef.Require(this);
        if (root is null || target.LinksTo.Count == 0) return retVal;
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
                underRoot[t] = value = t.HasAncestor(root);
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
                        var existing = thoughtsToSearch.FindFirst(x => x == w.From);
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
                var existing = thoughtsToSearch.FindFirst(x => x == r1.From);
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
            if (alreadySearched.FindFirst(x => x == tNew) is not null) return false;
            if (thoughtsToSearch.FindFirst(x => x == tNew) is not null) return false;
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

    /// <summary>
    /// Searches for links matching the specified criteria. Null parameters act as wildcards.
    /// </summary>
    /// <param name="l">Pattern link; a null or wildcard From, LinkType or To matches anything. A filterBy link on it restricts targets to descendants of its target.</param>
    /// <returns>List of links matching all specified (non-null) criteria.</returns>
    public List<Link> SearchForRelationships(Link l)
    {
        List<Link> results = new List<Link>();
        Thought? from = l.From?.HasProperty("isWildcard") is true ? null : l.From;
        Thought? linkType = l.LinkType?.HasProperty("isWildcard") is true ? null : l.LinkType;
        Thought? to = l.To?.HasProperty("isWildcard") is true ? null : l.To;
        Thought? filterTarget = l.HasLink("filterBy")?.To;
        Link? filter = filterTarget is null ? null : new Link(this) { To = filterTarget };
        if (from is null && to is null && linkType is null) return results;

        // hack to handle is-a searches
        if (linkType is not null && linkType == IsAType && from is not null)
        {
            foreach (Thought child in from.Parents)
            {
                if (filterTarget is not null && !child.HasAncestor(filterTarget)) continue;
                results.Add(new Link(this) { From = from, LinkType = linkType, To = child });
            }
            return results;
        }
        // If from is specified, start there for efficiency (most constrained search)
        if (from is not null)
        {
            var attribs = GetAttributes(from, filter);
            foreach (Link link in attribs)
            {
                if ((linkType is null || link.LinkType?.HasAncestor(linkType) == true) &&
                    (to is null || link.To == to))
                {
                    results.Add(new Link(this)
                    {
                        From = from,
                        LinkType = link.LinkType,
                        To = link.To,
                        Weight = link.Weight,
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

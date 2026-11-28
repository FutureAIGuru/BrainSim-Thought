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

// Finding sequences. A search spreads activation: each input value activates the stored elements
// that hold it, and activation passes along NXT links, so every stored sequence matching the input is
// found in one pass. Search options (properties of a SearchOption Thought) say how strict the match
// is; wildcards (??.noun) match any value of their class. The out-of-order search used for spelling
// correction is in UKS.SequenceApproximate.cs.
public partial class UKS
{
    /// <summary>
    /// Finds the stored sequences that match an input sequence, by activation: each input value activates
    /// the stored elements holding it, and activation passes along NXT links.
    /// </summary>
    /// <param name="sequence">The first input element.</param>
    /// <param name="searchOptions">
    /// A search options Thought (<see cref="CreateSearchOptions"/>, or one of those
    /// <see cref="CreateInitialStructure"/> makes: ExactSequenceSearch, the default, TemplateSequenceSearch,
    /// ApproximateSequenceSearch, ...).
    /// </param>
    /// <param name="getFirstElements">
    /// True for each matching sequence's first element; false for the element where each match starts,
    /// which finds occurrences inside longer sequences (flat, literal searches only).
    /// </param>
    /// <param name="last">The last input element to use; null for the whole input.</param>
    /// <param name="valueClass">Optional: compare the values of this class (<see cref="GetElementValue"/>) in input and stored sequences.</param>
    /// <param name="alternativeClass">
    /// Optional: where an input element has a value of this class, use it instead, as one value for the
    /// run of following elements that share it.
    /// </param>
    /// <param name="matchClass">Optional: keep only sequences owned by a Thought of this class.</param>
    /// <returns>The matches with a confidence (the share of the stored sequence matched), best first.</returns>
    public IReadOnlyList<(SeqElement seqNode, float confidence)> FindSequencesByActivation(
        SeqElement? sequence, ThoughtRef searchOptions = default, bool getFirstElements = true, SeqElement? last = null,
        ThoughtRef valueClass = default, ThoughtRef alternativeClass = default, ThoughtRef matchClass = default) =>
        TryResolveSearch(searchOptions, out Thought? options, valueClass, out Thought? values,
            matchClass, out Thought? matches, alternativeClass, out Thought? alternatives)
            ? SearchByActivation(sequence, options, getFirstElements, last, values, alternatives, matches) : new();

    /// <summary>
    /// Finds the stored sequences that match a pattern of values, by activation (see the overload taking
    /// an input sequence). With wildcards allowed, a wildcard in the pattern matches values of its class.
    /// </summary>
    /// <param name="pattern">The values to look for, in order.</param>
    /// <param name="searchOptions">A search options Thought; ExactSequenceSearch if empty.</param>
    /// <param name="getFirstElements">True for each matching sequence's first element; false for where each match starts.</param>
    /// <param name="valueClass">Optional: compare the stored values of this class.</param>
    /// <param name="matchClass">Optional: keep only sequences owned by a Thought of this class.</param>
    /// <returns>The matches with a confidence, best first.</returns>
    public IReadOnlyList<(SeqElement seqNode, float confidence)> FindSequencesByActivation(
        IReadOnlyList<Thought> pattern, ThoughtRef searchOptions = default,
        bool getFirstElements = true, ThoughtRef valueClass = default, ThoughtRef matchClass = default) =>
        TryResolveSearch(searchOptions, out Thought? options, valueClass, out Thought? values,
            matchClass, out Thought? matches, default, out _)
            ? SearchByActivation(pattern, options, getFirstElements, values, matches) : new();

    /// <summary>Creates a search options Thought (under SearchOption) for <see cref="FindSequencesByActivation"/>.</summary>
    /// <param name="mustMatchFirst">The match must start at a sequence's first element.</param>
    /// <param name="mustMatchLast">The match must end at a sequence's last element.</param>
    /// <param name="allowWildcard">Wildcards in the input match values of their class.</param>
    /// <param name="allowNestedSequences">Values inside nested sequences are matched.</param>
    /// <param name="allowTrailingInput">A stored sequence matches even if the input continues past its end.</param>
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

    /// <summary>True if the Thought is a wildcard: it, or an ancestor, has a wildcard property (<see cref="CreateWildcard"/>).</summary>
    public static bool IsWildcard(Thought t) => WildcardProperties.Any(property => t.HasProperty(property));

    /// <summary>
    /// Gets or creates a wildcard: a Thought that, in a sequence pattern, matches any value of its
    /// classes (??.noun matches any noun).
    /// </summary>
    /// <param name="wildcardName">The wildcard's label.</param>
    /// <param name="parentThoughts">The classes it matches; none matches anything.</param>
    /// <param name="wildcardProperty">
    /// How many values it matches: isWildcard (one), isOptionalWildcard (zero or one), is*Wildcard
    /// (any number), is+Wildcard (one or more).
    /// </param>
    /// <exception cref="ArgumentException">The property is not one of these.</exception>
    /// <exception cref="InvalidOperationException">The wildcard already exists with another property.</exception>
    public Thought CreateWildcard(
        string wildcardName,
        IEnumerable<Thought> parentThoughts,
        string wildcardProperty = "isWildcard")
    {
        if (!WildcardProperties.Contains(wildcardProperty, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Unknown wildcard cardinality property.", nameof(wildcardProperty));

        Thought wildcard = GetOrAddThought(wildcardName, "Property")!;
        Thought selectedProperty = GetOrAddThought(wildcardProperty, "Property")!;
        Thought? existingProperty = wildcard.LinksTo
            .Where(x => x.LinkType == HasPropertyType && x.To is not null)
            .Select(x => x.To!)
            .FirstOrDefault(x => WildcardProperties.Select(Labeled).Contains(x));
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

    /// <summary>
    /// Finds complete flat stored sequences and partial stream matches within an input range.
    /// Seeds once per start and extends the same activation states. last is exclusive.
    /// </summary>
    public IReadOnlyList<(SeqElement start, SeqElement end, SeqElement match)> FindSequenceSpansByActivation(
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

    /// <summary>
    /// Resolves a search's options and classes in this store: empty options mean ExactSequenceSearch,
    /// an empty class means none. False if any of them names an unknown label (nothing can match).
    /// </summary>
    private bool TryResolveSearch(ThoughtRef searchOptionsRef, out Thought? searchOptions,
        ThoughtRef valueClassRef, out Thought? valueClass, ThoughtRef matchClassRef, out Thought? matchClass,
        ThoughtRef alternativeClassRef, out Thought? alternativeClass)
    {
        searchOptions = searchOptionsRef.IsEmpty ? Labeled("ExactSequenceSearch") : searchOptionsRef.Find(this);
        valueClass = valueClassRef.Find(this);
        matchClass = matchClassRef.Find(this);
        alternativeClass = alternativeClassRef.Find(this);
        return (searchOptions is not null || searchOptionsRef.IsEmpty) &&
            (valueClass is not null || valueClassRef.IsEmpty) &&
            (matchClass is not null || matchClassRef.IsEmpty) &&
            (alternativeClass is not null || alternativeClassRef.IsEmpty);
    }

    private List<(SeqElement seqNode, float confidence)> SearchByActivation(SeqElement? sequence, Thought? searchOptions,
        bool getFirstElements, SeqElement? last, Thought? valueClass, Thought? alternativeClass, Thought? matchClass)
    {
        List<(SeqElement seqNode, float confidence)> results = new();
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
                results = SearchByActivation(wildcardPattern, searchOptions, getFirstElements, valueClass, matchClass);
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

    private List<(SeqElement seqNode, float confidence)> SearchByActivation(IReadOnlyList<Thought> pattern, Thought? searchOptions,
        bool getFirstElements, Thought? valueClass, Thought? matchClass)
    {
        List<(SeqElement seqNode, float confidence)> retVal = new();
        if (pattern is null || pattern.Count == 0) return retVal;
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

        int seedPatternIndex = pattern.ToList().FindIndex(p => !IsWildcardPatternElement(p, searchOptions));
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

    //This essentially keeps track of the activation of the various sequence elements as the search progresses through the pattern.
    private class SequenceSearchState
    {
        public SeqElement FirstMatchElement = null!;
        public SeqElement LastMatchElement = null!;
        public SeqElement? CurPos;
        public Stack<SeqElement> ReturnStack = new();
        public float Confidence = 1.0f;
    }

    private List<(SeqElement seqNode, float confidence)> FindExactWildcardMatches(IReadOnlyList<Thought> pattern, Thought? valueClass = null)
    {
        Thought? anchor = pattern
            .Where(x => !IsWildcard(x))
            .OrderBy(x => x.LinksFrom.Count(link => link.LinkType == VluType))
            .FirstOrDefault();
        if (anchor is null) return new List<(SeqElement seqNode, float confidence)>();

        HashSet<SeqElement> candidates = new();
        foreach (Link link in anchor.LinksFrom)
        {
            if (link.LinkType != VluType || link.From is not SeqElement element) continue;
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
            if (link.LinkType == VluType && link.From is SeqElement caller)
                AddEnclosingSequenceRoots(caller.FRST, candidates);
        }
    }

    private static bool ExactWildcardPatternMatches(IReadOnlyList<Thought> pattern, List<Thought?> sequence)
    {
        HashSet<int> activePositions = new() { 0 };
        foreach (Thought patternElement in pattern)
        {
            HashSet<int> nextPositions = new();
            foreach (int position in activePositions)
            {
                if (!IsWildcard(patternElement))
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
            !IsWildcard(wildcard)) return false;
        List<Thought> constraints = wildcard.Ancestors.Prepend(wildcard)
            .Where(ancestor => IsWildcard(ancestor))
            .SelectMany(ancestor => ancestor.Parents)
            .Where(parent => !IsWildcard(parent) &&
                !ReferenceEquals(parent, wildcard.Store.Labeled("Property")) &&
                !ReferenceEquals(parent, wildcard.Store.Labeled("Thought")) &&
                !ReferenceEquals(parent, wildcard.Store.Labeled("Unknown")))
            .ToList();
        return constraints.Count == 0 || constraints.Any(constraint => value.HasAncestor(constraint));
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
            IEnumerable<Thought> wildcardCandidates = firstPatternElement.Ancestors.Prepend(firstPatternElement)
                .Append(Labeled("Property")).OfType<Thought>()
                .SelectMany(parent => parent.Children);
            // An unclassified word has no noun/adjective/etc. ancestry yet.
            // Follow declarations of the wildcard properties, wherever those Thoughts
            // are grouped, rather than relying on Property parentage or scanning all Thoughts.
            if (Labeled("unknownWordType") is Thought unknownWordType && firstPatternElement.HasAncestor(unknownWordType))
                wildcardCandidates = wildcardCandidates.Concat(
                    WildcardProperties
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
            if (link.LinkType != VluType) continue;
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
            .Where(x => x.LinkType == VluType && x.From is SeqElement)
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
                    t = (SeqElement?)t?.LinksFrom.FirstOrDefault(x => x.LinkType == NxtType)?.From;
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
            if (link.LinkType != VluType) continue;
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
            IsWildcard(wildcard) &&
            wildcard.Ancestors.Prepend(wildcard)
                .Where(ancestor => IsWildcard(ancestor))
                .SelectMany(ancestor => ancestor.Parents)
                .Any(parent => !IsWildcard(parent) &&
                    !ReferenceEquals(parent, wildcard.Store.Labeled("Property")) &&
                    !ReferenceEquals(parent, wildcard.Store.Labeled("Thought")) &&
                    !ReferenceEquals(parent, wildcard.Store.Labeled("Unknown")));
    }

    /// <summary>The properties marking a wildcard, one per cardinality.</summary>
    private static readonly string[] WildcardProperties = { "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard" };

    private static bool IsWildcardPatternElement(Thought? patternElement, Thought? searchOptions)
    {
        if (patternElement is null) return false;
        if (searchOptions is null) return false;
        if (!searchOptions.HasProperty("allowWildcards")) return false;

        return IsWildcard(patternElement);
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
}

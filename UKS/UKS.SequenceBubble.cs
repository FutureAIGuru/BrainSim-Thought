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
/// Cardinality inferred for a non-common region between fixed sequence values.
/// </summary>
public enum SequenceGapCardinality
{
    ExactlyOne,
    ZeroOrOne,
    ZeroOrMore,
    OneOrMore,
}

/// <summary>
/// One transient element of a common-sequence description. It is either a
/// fixed Thought or a gap with an inferred cardinality.
/// </summary>
public sealed class CommonSequenceElement
{
    private CommonSequenceElement(Thought? value, SequenceGapCardinality? gapCardinality)
    {
        Value = value;
        GapCardinality = gapCardinality;
    }

    public Thought? Value { get; }
    public SequenceGapCardinality? GapCardinality { get; }
    public bool IsGap => GapCardinality.HasValue;

    internal static CommonSequenceElement Fixed(Thought value) => new(value, null);
    internal static CommonSequenceElement Gap(SequenceGapCardinality cardinality) => new(null, cardinality);
}

/// <summary>
/// A side-effect-free alignment result. Persisted knowledge is created only
/// when a caller bubbles this description onto a Thought class.
/// </summary>
public sealed class CommonSequencePattern
{
    internal CommonSequencePattern(
        IReadOnlyList<SequenceView> observations,
        IReadOnlyList<CommonSequenceElement> elements,
        int fixedElementCount)
    {
        Observations = observations;
        Elements = elements;
        FixedElementCount = fixedElementCount;
    }

    public IReadOnlyList<SequenceView> Observations { get; }
    public IReadOnlyList<CommonSequenceElement> Elements { get; }
    public int FixedElementCount { get; }
}

public partial class UKS
{
    /// <summary>
    /// Finds an ordered description common to all supplied sequence views.
    /// Exact shared Thoughts become fixed elements. Regions between them become
    /// wildcard gaps whose cardinality is inferred from the observed lengths.
    /// The method does not change the UKS.
    /// </summary>
    public CommonSequencePattern? FindCommonSequence(
        IEnumerable<SequenceView> observations,
        int minFixedElements = 1)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (minFixedElements < 0) throw new ArgumentOutOfRangeException(nameof(minFixedElements));

        if (SequenceDiscoveryDiagnostics.Enabled)
            SequenceDiscoveryDiagnostics.FindCommonSequenceCalls++;

        List<SequenceView> observationList = observations
            .Where(observation => observation is not null && observation.Elements.Count > 0)
            .ToList();
        if (observationList.Count == 0) return null;

        List<Thought> common = observationList[0].Elements.ToList();
        for (int i = 1; i < observationList.Count && common.Count > 0; i++)
            common = LongestCommonSubsequence(common, observationList[i].Elements);

        if (common.Count < minFixedElements) return null;

        List<int[]> gapsByObservation = observationList
            .Select(observation => GetGapLengths(observation.Elements, common))
            .ToList();
        List<CommonSequenceElement> pattern = new();

        for (int gapIndex = 0; gapIndex <= common.Count; gapIndex++)
        {
            AppendInferredGap(pattern, gapsByObservation.Select(gaps => gaps[gapIndex]));
            if (gapIndex < common.Count)
                pattern.Add(CommonSequenceElement.Fixed(common[gapIndex]));
        }

        return new CommonSequencePattern(observationList, pattern, common.Count);
    }

    /// <summary>
    /// Determines whether an observation fits a transient common-sequence
    /// pattern without materializing wildcard Thoughts.
    /// </summary>
    public bool SequenceMatchesPattern(CommonSequencePattern pattern, SequenceView observation)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(observation);

        if (SequenceDiscoveryDiagnostics.Enabled)
            SequenceDiscoveryDiagnostics.MatchCalls++;

        HashSet<int> activePositions = new() { 0 };
        foreach (CommonSequenceElement patternElement in pattern.Elements)
        {
            HashSet<int> nextPositions = new();
            if (SequenceDiscoveryDiagnostics.Enabled)
                SequenceDiscoveryDiagnostics.MatchPositionSteps += activePositions.Count;
            foreach (int position in activePositions)
            {
                if (!patternElement.IsGap)
                {
                    if (position < observation.Elements.Count &&
                        ReferenceEquals(patternElement.Value, observation.Elements[position]))
                        nextPositions.Add(position + 1);
                    continue;
                }

                switch (patternElement.GapCardinality!.Value)
                {
                    case SequenceGapCardinality.ExactlyOne:
                        if (position < observation.Elements.Count) nextPositions.Add(position + 1);
                        break;
                    case SequenceGapCardinality.ZeroOrOne:
                        nextPositions.Add(position);
                        if (position < observation.Elements.Count) nextPositions.Add(position + 1);
                        break;
                    case SequenceGapCardinality.ZeroOrMore:
                        for (int end = position; end <= observation.Elements.Count; end++)
                            nextPositions.Add(end);
                        break;
                    case SequenceGapCardinality.OneOrMore:
                        for (int end = position + 1; end <= observation.Elements.Count; end++)
                            nextPositions.Add(end);
                        break;
                }
            }

            activePositions = nextPositions;
            if (activePositions.Count == 0) return false;
        }
        return activePositions.Contains(observation.Elements.Count);
    }

    /// <summary>
    /// Discovers templates directly from an unclassified population of sequence
    /// owners. Wildcard fillers become ordinary classes, while complete sequence
    /// owners are retained as evidence links on the learned templates.
    /// </summary>
    public List<Thought> DiscoverSequenceTemplates(
        IEnumerable<SequenceView> observations,
        Thought templateRoot,
        Thought fillerClassRoot,
        int minMembers = 4,
        int minFixedElements = 2,
        string templateLabel = "template*",
        string classLabel = "class*",
        int maxAdjacentGapPairs = 0)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(templateRoot);
        ArgumentNullException.ThrowIfNull(fillerClassRoot);
        if (minMembers < 2) throw new ArgumentOutOfRangeException(nameof(minMembers));
        if (minFixedElements < 1) throw new ArgumentOutOfRangeException(nameof(minFixedElements));

        long groupingStart = SequenceDiscoveryDiagnostics.Start();

        // Do not mix observations which describe different kinds of sequences,
        // such as Phrase hasWords and Word spelled sequences.
        List<(Thought linkType, List<SequenceView> observations, List<SequenceView> allObservations)> relationshipGroups = observations
            .Where(observation => observation is not null && observation.LinkType is not null)
            .GroupBy(observation => observation.LinkType!)
            .Select(group =>
            {
                List<SequenceView> all = group
                    .GroupBy(observation => observation.Owner)
                    .Select(ownerGroup => ownerGroup.First())
                    .ToList();
                List<SequenceView> distinct = all
                    .GroupBy(SequenceObservationSignature, StringComparer.Ordinal)
                    .Select(contentGroup => contentGroup.First())
                    .ToList();
                return (group.Key, distinct, all);
            })
            .Where(group => group.Item2.Count >= minMembers)
            .ToList();

        SequenceDiscoveryDiagnostics.GroupingMs += SequenceDiscoveryDiagnostics.Elapsed(groupingStart);

        List<Thought> results = new();
        foreach (var relationshipGroup in relationshipGroups)
        {
            long pairStart = SequenceDiscoveryDiagnostics.Start();

            // Pairs propose structures without changing the UKS. Only separated
            // singleton gaps are currently allowed into learned templates.
            Dictionary<string, CommonSequencePattern> proposals = new(StringComparer.Ordinal);
            List<SequenceView> groupObservations = relationshipGroup.observations;
            for (int first = 0; first < groupObservations.Count - 1; first++)
            {
                for (int second = first + 1; second < groupObservations.Count; second++)
                {
                    CommonSequencePattern? proposal = FindCommonSequence(
                        new[] { groupObservations[first], groupObservations[second] },
                        minFixedElements);
                    if (proposal is null || proposal.Elements.Count < 2 ||
                        !IsLearnableTemplatePattern(proposal, maxAdjacentGapPairs)) continue;
                    proposals.TryAdd(CommonSequenceSignature(proposal), proposal);
                }
            }

            SequenceDiscoveryDiagnostics.PairProposalMs +=
                SequenceDiscoveryDiagnostics.Elapsed(pairStart);
            long consolidateStart = SequenceDiscoveryDiagnostics.Start();

            List<(HashSet<Thought> owners, CommonSequencePattern pattern)> consolidated = new();
            foreach (CommonSequencePattern proposal in proposals.Values)
            {
                // Expand each proposal to every matching observation, then
                // generalize again until the evidence population stops growing.
                List<SequenceView> matches = groupObservations
                    .Where(observation => SequenceMatchesPattern(proposal, observation))
                    .ToList();
                CommonSequencePattern generalized;
                while (true)
                {
                    generalized = FindCommonSequence(matches, minFixedElements)!;
                    if (!IsLearnableTemplatePattern(generalized, maxAdjacentGapPairs)) break;
                    List<SequenceView> expanded = groupObservations
                        .Where(observation => SequenceMatchesPattern(generalized, observation))
                        .ToList();
                    HashSet<Thought> currentOwners = matches.Select(match => match.Owner).ToHashSet();
                    HashSet<Thought> expandedOwners = expanded.Select(match => match.Owner).ToHashSet();
                    if (expandedOwners.SetEquals(currentOwners)) break;
                    matches = expanded;
                }

                if (!IsLearnableTemplatePattern(generalized, maxAdjacentGapPairs)) continue;

                HashSet<Thought> owners = matches.Select(match => match.Owner).ToHashSet();
                if (owners.Count < minMembers) continue;
                if (consolidated.Any(candidate => candidate.owners.SetEquals(owners))) continue;
                consolidated.Add((owners, generalized));
            }

            SequenceDiscoveryDiagnostics.ConsolidationMs +=
                SequenceDiscoveryDiagnostics.Elapsed(consolidateStart);
            long materializeStart = SequenceDiscoveryDiagnostics.Start();

            Thought evidenceType = GetOrAddThought("evidence", "LinkType")
                ?? throw new InvalidOperationException("The evidence link type could not be created.");
            foreach (var candidate in consolidated.OrderByDescending(candidate => candidate.owners.Count))
            {
                // Distinct phrase contents determine whether a pattern is
                // significant. Every occurrence is retained as evidence.
                long memberStart = SequenceDiscoveryDiagnostics.Start();
                List<SequenceView> members = relationshipGroup.allObservations
                    .Where(observation => SequenceMatchesPattern(candidate.pattern, observation))
                    .ToList();
                SequenceDiscoveryDiagnostics.MemberSelectionMs +=
                    SequenceDiscoveryDiagnostics.Elapsed(memberStart);

                // Each wildcard position gets a class containing the individual
                // Thoughts observed in that position.
                long classStart = SequenceDiscoveryDiagnostics.Start();
                List<Thought> description = MaterializeClassSequence(
                    candidate.pattern, members, fillerClassRoot, classLabel);
                SequenceDiscoveryDiagnostics.ClassCreationMs +=
                    SequenceDiscoveryDiagnostics.Elapsed(classStart);
                if (description.Count < 2) continue;

                // The wildcard sequence belongs to a template. The complete
                // Phrase/Word owners are evidence, never children of it.
                long lookupStart = SequenceDiscoveryDiagnostics.Start();
                Thought? existingTemplate = templateRoot.Children.FirstOrDefault(existing =>
                    GetSequenceViews(existing).Any(view =>
                        view.LinkType == relationshipGroup.linkType &&
                        view.Elements.SequenceEqual(description)));
                Thought learnedTemplate = existingTemplate ?? GetOrAddThought(templateLabel, templateRoot)
                    ?? throw new InvalidOperationException("The learned template could not be created.");
                if (!GetSequenceViews(learnedTemplate).Any(view =>
                    view.LinkType == relationshipGroup.linkType &&
                    view.Elements.SequenceEqual(description)))
                    AddSequenceAndLink(learnedTemplate, relationshipGroup.linkType, description);
                SequenceDiscoveryDiagnostics.TemplateLookupMs +=
                    SequenceDiscoveryDiagnostics.Elapsed(lookupStart);

                long evidenceStart = SequenceDiscoveryDiagnostics.Start();
                foreach (SequenceView member in members)
                    AddStatement(learnedTemplate, evidenceType, member.Owner);
                SequenceDiscoveryDiagnostics.EvidenceMs +=
                    SequenceDiscoveryDiagnostics.Elapsed(evidenceStart);
                if (SequenceDiscoveryDiagnostics.Enabled)
                    SequenceDiscoveryDiagnostics.EvidenceStatements += members.Count;
                learnedTemplate.Weight = Math.Max(learnedTemplate.Weight, members.Count);
                if (!results.Contains(learnedTemplate)) results.Add(learnedTemplate);
            }

            SequenceDiscoveryDiagnostics.MaterializeMs +=
                SequenceDiscoveryDiagnostics.Elapsed(materializeStart);
        }

        return results;
    }

    private static string SequenceObservationSignature(SequenceView observation)
    {
        // Thought labels are unique in a UKS. Length-prefixing prevents two
        // neighboring labels from producing an ambiguous concatenation.
        string retVal = string.Join("|", observation.Elements.Select(element =>
            $"{element.Label.Length}:{element.Label}"));
        if (SequenceDiscoveryDiagnostics.Enabled)
        {
            SequenceDiscoveryDiagnostics.SignatureCalls++;
            SequenceDiscoveryDiagnostics.SignatureChars += retVal.Length;
        }
        return retVal;
    }

    /// <summary>
    /// Converts a transient common-sequence pattern into ordinary Thoughts.
    /// Gap Thoughts are members of Wildcard and use properties to control their
    /// traversal cardinality.
    /// </summary>
    public List<Thought> MaterializeCommonSequence(CommonSequencePattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        List<Thought> result = new();
        foreach (CommonSequenceElement element in pattern.Elements)
        {
            if (!element.IsGap)
            {
                if (element.Value is not null) result.Add(element.Value);
                continue;
            }

            result.Add(GetCommonSequenceWildcard(element.GapCardinality!.Value));
        }
        return result;
    }

    /// <summary>
    /// Finds and bubbles common sequence descriptions from the direct children
    /// of a class. Observations are grouped by their relationship type, and the
    /// original child sequences are retained.
    /// </summary>
    public bool BubbleSharedSequences(
        Thought parent,
        float minFraction = 0.6f,
        int minFixedElements = 1)
    {
        if (parent is null || parent.Children.Count == 0) return false;
        if (parent.Label.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return false;
        if (minFraction <= 0 || minFraction > 1) throw new ArgumentOutOfRangeException(nameof(minFraction));

        int childCount = parent.Children.Count;
        bool changed = false;
        var groups = GetSequenceViews(parent.Children)
            .Where(view => view.LinkType is not null && view.LinkType != Thought.IsA)
            .GroupBy(view => view.LinkType!);

        foreach (var group in groups)
        {
            List<SequenceView> observations = group
                .GroupBy(view => view.Owner)
                .Select(ownerGroup => ownerGroup.First())
                .ToList();
            if (observations.Count < childCount * minFraction) continue;

            CommonSequencePattern? common = FindCommonSequence(observations, minFixedElements);
            if (common is null) continue;
            List<Thought> elements = MaterializeCommonSequence(common);
            if (elements.Count < 2) continue;

            SequenceView? existing = GetSequenceViews(parent)
                .FirstOrDefault(view => view.LinkType == group.Key);
            if (existing is not null && existing.Elements.SequenceEqual(elements)) continue;

            SeqElement sequence = AddSequenceAndLink(parent, group.Key, elements);
            if (sequence is null) continue;

            Link? bubbledLink = GetLink(parent, group.Key, sequence);
            if (bubbledLink is not null)
                bubbledLink.Weight = observations.Count / (float)childCount;
            changed = true;
        }

        return changed;
    }

    private static List<Thought> LongestCommonSubsequence(
        IReadOnlyList<Thought> first,
        IReadOnlyList<Thought> second)
    {
        if (SequenceDiscoveryDiagnostics.Enabled)
        {
            SequenceDiscoveryDiagnostics.LcsCalls++;
            SequenceDiscoveryDiagnostics.LcsCells +=
                (long)(first.Count + 1) * (second.Count + 1);
        }

        int[,] lengths = new int[first.Count + 1, second.Count + 1];
        for (int i = first.Count - 1; i >= 0; i--)
        {
            for (int j = second.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = ReferenceEquals(first[i], second[j])
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        List<Thought> result = new();
        int firstIndex = 0;
        int secondIndex = 0;
        while (firstIndex < first.Count && secondIndex < second.Count)
        {
            if (ReferenceEquals(first[firstIndex], second[secondIndex]))
            {
                result.Add(first[firstIndex]);
                firstIndex++;
                secondIndex++;
            }
            else if (lengths[firstIndex + 1, secondIndex] >= lengths[firstIndex, secondIndex + 1])
            {
                firstIndex++;
            }
            else
            {
                secondIndex++;
            }
        }
        return result;
    }

    private static int[] GetGapLengths(IReadOnlyList<Thought> sequence, IReadOnlyList<Thought> anchors)
    {
        int[] gaps = new int[anchors.Count + 1];
        int cursor = 0;
        for (int anchorIndex = 0; anchorIndex < anchors.Count; anchorIndex++)
        {
            int position = cursor;
            while (position < sequence.Count && !ReferenceEquals(sequence[position], anchors[anchorIndex]))
                position++;
            if (position == sequence.Count)
                throw new InvalidOperationException("The common sequence is not a subsequence of an observation.");

            gaps[anchorIndex] = position - cursor;
            cursor = position + 1;
        }
        gaps[anchors.Count] = sequence.Count - cursor;
        return gaps;
    }

    private static void AppendInferredGap(
        List<CommonSequenceElement> pattern,
        IEnumerable<int> observedLengths)
    {
        int[] lengths = observedLengths.ToArray();
        int minimum = lengths.Min();
        int maximum = lengths.Max();
        if (maximum == 0) return;

        if (minimum == maximum)
        {
            for (int i = 0; i < minimum; i++)
                pattern.Add(CommonSequenceElement.Gap(SequenceGapCardinality.ExactlyOne));
        }
        else if (minimum == 0 && maximum == 1)
        {
            pattern.Add(CommonSequenceElement.Gap(SequenceGapCardinality.ZeroOrOne));
        }
        else if (minimum == 0)
        {
            pattern.Add(CommonSequenceElement.Gap(SequenceGapCardinality.ZeroOrMore));
        }
        else
        {
            pattern.Add(CommonSequenceElement.Gap(SequenceGapCardinality.OneOrMore));
        }
    }

    private Thought GetCommonSequenceWildcard(SequenceGapCardinality cardinality)
    {
        (string label, string property) = cardinality switch
        {
            SequenceGapCardinality.ExactlyOne => ("??", "isWildcard"),
            SequenceGapCardinality.ZeroOrOne => ("???", "isOptionalWildcard"),
            SequenceGapCardinality.ZeroOrMore => ("??*", "is*Wildcard"),
            SequenceGapCardinality.OneOrMore => ("??+", "is+Wildcard"),
            _ => throw new ArgumentOutOfRangeException(nameof(cardinality)),
        };
        return CreateWildcard(label, new List<Thought>(), property);
    }

    private List<Thought> MaterializeClassSequence(
        CommonSequencePattern pattern,
        IReadOnlyList<SequenceView> observations,
        Thought classRoot,
        string classLabel)
    {
        List<Thought> result = new();
        for (int position = 0; position < pattern.Elements.Count; position++)
        {
            CommonSequenceElement element = pattern.Elements[position];
            if (!element.IsGap)
            {
                result.Add(element.Value!);
                continue;
            }

            HashSet<Thought> fillers = observations
                .Select(observation => observation.Elements[position])
                .ToHashSet();
            Thought fillerClass = GetOrCreateThoughtClass(classRoot, fillers, classLabel);
            Thought wildcard = Labeled("??" + fillerClass.Label) ??
                CreateWildcard("??" + fillerClass.Label, new List<Thought> { fillerClass });
            result.Add(wildcard);
        }
        return result;
    }

    private static bool HasOnlySingletonGaps(CommonSequencePattern pattern)
    {
        return pattern.Elements.All(element => !element.IsGap ||
            element.GapCardinality == SequenceGapCardinality.ExactlyOne);
    }

    /// <summary>
    /// Adjacent gaps are ordinarily rejected because a run of them describes
    /// almost any observation. Permitting a single pair admits the structures in
    /// which one slot qualifies another, while a run of three or more remains
    /// too general to be worth learning.
    /// </summary>
    private static bool IsLearnableTemplatePattern(
        CommonSequencePattern pattern,
        int maxAdjacentGapPairs = 0)
    {
        if (!HasOnlySingletonGaps(pattern)) return false;

        int adjacentPairs = 0;
        int runLength = 0;
        foreach (CommonSequenceElement element in pattern.Elements)
        {
            runLength = element.IsGap ? runLength + 1 : 0;
            if (runLength > 2) return false;
            if (runLength == 2) adjacentPairs++;
        }
        return adjacentPairs <= maxAdjacentGapPairs;
    }

    private static string CommonSequenceSignature(CommonSequencePattern pattern)
    {
        string retVal = string.Join("\u001f", pattern.Elements.Select(element => element.IsGap
            ? "G:" + element.GapCardinality
            : "V:" + element.Value!.Label));
        if (SequenceDiscoveryDiagnostics.Enabled)
        {
            SequenceDiscoveryDiagnostics.SignatureCalls++;
            SequenceDiscoveryDiagnostics.SignatureChars += retVal.Length;
        }
        return retVal;
    }

}

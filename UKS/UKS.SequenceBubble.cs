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
    internal CommonSequencePattern(IReadOnlyList<CommonSequenceElement> elements, int fixedElementCount)
    {
        Elements = elements;
        FixedElementCount = fixedElementCount;
    }

    public IReadOnlyList<CommonSequenceElement> Elements { get; }
    public int FixedElementCount { get; }
}

public partial class UKS
{
    /// <summary>
    /// Gets the links by which an owner describes its sequences (phrase hasWords, word spelled, ...).
    /// A link's From is the owner, its LinkType the kind of sequence and its To the first element.
    /// </summary>
    public List<Link> GetSequenceLinks(Thought owner) => owner.LinksTo.Where(link => link.To is SeqElement).ToList();

    /// <summary>Gets the sequence links of each owner.</summary>
    public List<Link> GetSequenceLinks(IEnumerable<Thought> owners) => owners.SelectMany(GetSequenceLinks).ToList();

    /// <summary>
    /// Gets or creates a class under classRoot whose direct members are exactly the supplied
    /// ordinary Thoughts. Sequence elements and wildcards are not members.
    /// </summary>
    public Thought GetOrCreateThoughtClass(Thought classRoot, IEnumerable<Thought> members, string classLabel = "class*")
    {
        static bool IsMember(Thought? t) => t is not null and not SeqElement && !new[] { "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard" }.Any(t.HasProperty);

//        HashSet<Thought> memberSet = members.Where(IsMember).ToHashSet();
        HashSet<Thought> memberSet = members.ToHashSet();
        if (memberSet.Count == 0)
            throw new ArgumentException("A learned class requires at least one ordinary Thought.", nameof(members));

        Thought learnedClass = classRoot.Children.FirstOrDefault(existing => existing.Children.Where(IsMember).ToHashSet().SetEquals(memberSet))
            ?? GetOrAddThought(classLabel, classRoot)
            ?? throw new InvalidOperationException("The learned class could not be created.");
        foreach (Thought member in memberSet)
            member.AddParent(learnedClass);
        return learnedClass;
    }

    /// <summary>
    /// Finds an ordered description common to all supplied sequences.
    /// Exact shared Thoughts become fixed elements. Regions between them become
    /// wildcard gaps whose cardinality is inferred from the observed lengths.
    /// The method does not change the UKS.
    /// </summary>
    public CommonSequencePattern? FindCommonSequence(
        IEnumerable<Link> observations,
        int minFixedElements = 1)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (minFixedElements < 0) throw new ArgumentOutOfRangeException(nameof(minFixedElements));

        List<List<Thought>> sequences = observations
            .Where(observation => observation is not null)
            .Select(observation => FlattenSequence(observation.To))
            .Where(sequence => sequence.Count > 0)
            .ToList();
        if (sequences.Count == 0) return null;

        List<Thought> common = sequences[0];
        for (int i = 1; i < sequences.Count && common.Count > 0; i++)
            common = LongestCommonSubsequence(common, sequences[i]);

        if (common.Count < minFixedElements) return null;

        List<int[]> gapsByObservation = sequences
            .Select(sequence => GetGapLengths(sequence, common))
            .ToList();
        List<CommonSequenceElement> pattern = new();

        for (int gapIndex = 0; gapIndex <= common.Count; gapIndex++)
        {
            AppendInferredGap(pattern, gapsByObservation.Select(gaps => gaps[gapIndex]));
            if (gapIndex < common.Count)
                pattern.Add(CommonSequenceElement.Fixed(common[gapIndex]));
        }

        return new CommonSequencePattern(pattern, common.Count);
    }

    /// <summary>
    /// Determines whether an observation fits a transient common-sequence
    /// pattern without materializing wildcard Thoughts.
    /// </summary>
    public bool SequenceMatchesPattern(CommonSequencePattern pattern, Link observation)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(observation);
        List<Thought> elements = FlattenSequence(observation.To);

        HashSet<int> activePositions = new() { 0 };
        foreach (CommonSequenceElement patternElement in pattern.Elements)
        {
            HashSet<int> nextPositions = new();
            foreach (int position in activePositions)
            {
                if (!patternElement.IsGap)
                {
                    if (position < elements.Count &&
                        ReferenceEquals(patternElement.Value, elements[position]))
                        nextPositions.Add(position + 1);
                    continue;
                }

                switch (patternElement.GapCardinality!.Value)
                {
                    case SequenceGapCardinality.ExactlyOne:
                        if (position < elements.Count) nextPositions.Add(position + 1);
                        break;
                    case SequenceGapCardinality.ZeroOrOne:
                        nextPositions.Add(position);
                        if (position < elements.Count) nextPositions.Add(position + 1);
                        break;
                    case SequenceGapCardinality.ZeroOrMore:
                        for (int end = position; end <= elements.Count; end++)
                            nextPositions.Add(end);
                        break;
                    case SequenceGapCardinality.OneOrMore:
                        for (int end = position + 1; end <= elements.Count; end++)
                            nextPositions.Add(end);
                        break;
                }
            }

            activePositions = nextPositions;
            if (activePositions.Count == 0) return false;
        }
        return activePositions.Contains(elements.Count);
    }

    /// <summary>
    /// Discovers templates directly from an unclassified population of sequence
    /// owners. Wildcard fillers become ordinary classes, while complete sequence
    /// owners are retained as evidence links on the learned templates.
    /// </summary>
    public List<Thought> DiscoverSequenceTemplates(
        IEnumerable<Link> observations,
        Thought templateRoot,
        Thought fillerClassRoot,
        int minMembers = 4,
        int minFixedElements = 2,
        string templateLabel = "template*",
        string classLabel = "class*")
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(templateRoot);
        ArgumentNullException.ThrowIfNull(fillerClassRoot);
        if (minMembers < 2) throw new ArgumentOutOfRangeException(nameof(minMembers));
        if (minFixedElements < 1) throw new ArgumentOutOfRangeException(nameof(minFixedElements));

        // Do not mix observations which describe different kinds of sequences,
        // such as Phrase hasWords and Word spelled sequences.
        List<(Thought linkType, List<Link> observations, List<Link> allObservations)> relationshipGroups = observations
            .Where(observation => observation is not null && observation.LinkType is not null)
            .GroupBy(observation => observation.LinkType!)
            .Select(group =>
            {
                List<Link> all = group
                    .GroupBy(observation => observation.From!)
                    .Select(ownerGroup => ownerGroup.First())
                    .ToList();
                List<Link> distinct = all
                    .GroupBy(SequenceObservationSignature, StringComparer.Ordinal)
                    .Select(contentGroup => contentGroup.First())
                    .ToList();
                return (group.Key, distinct, all);
            })
            .Where(group => group.Item2.Count >= minMembers)
            .ToList();

        List<Thought> results = new();
        foreach (var relationshipGroup in relationshipGroups)
        {
            // Pairs propose structures without changing the UKS. Only separated
            // singleton gaps are currently allowed into learned templates.
            Dictionary<string, CommonSequencePattern> proposals = new(StringComparer.Ordinal);
            List<Link> groupObservations = relationshipGroup.observations;
            for (int first = 0; first < groupObservations.Count - 1; first++)
            {
                for (int second = first + 1; second < groupObservations.Count; second++)
                {
                    CommonSequencePattern? proposal = FindCommonSequence(
                        new[] { groupObservations[first], groupObservations[second] },
                        minFixedElements);
                    if (proposal is null || proposal.Elements.Count < 2 ||
                        !IsLearnableTemplatePattern(proposal)) continue;
                    proposals.TryAdd(CommonSequenceSignature(proposal), proposal);
                }
            }

            List<(HashSet<Thought> owners, CommonSequencePattern pattern)> consolidated = new();
            foreach (CommonSequencePattern proposal in proposals.Values)
            {
                // Expand each proposal to every matching observation, then
                // generalize again until the evidence population stops growing.
                List<Link> matches = groupObservations
                    .Where(observation => SequenceMatchesPattern(proposal, observation))
                    .ToList();
                CommonSequencePattern generalized;
                while (true)
                {
                    generalized = FindCommonSequence(matches, minFixedElements)!;
                    if (!IsLearnableTemplatePattern(generalized)) break;
                    List<Link> expanded = groupObservations
                        .Where(observation => SequenceMatchesPattern(generalized, observation))
                        .ToList();
                    HashSet<Thought> currentOwners = matches.Select(match => match.From!).ToHashSet();
                    HashSet<Thought> expandedOwners = expanded.Select(match => match.From!).ToHashSet();
                    if (expandedOwners.SetEquals(currentOwners)) break;
                    matches = expanded;
                }

                if (!IsLearnableTemplatePattern(generalized)) continue;

                HashSet<Thought> owners = matches.Select(match => match.From!).ToHashSet();
                if (owners.Count < minMembers) continue;
                if (consolidated.Any(candidate => candidate.owners.SetEquals(owners))) continue;
                consolidated.Add((owners, generalized));
            }

            Thought evidenceType = GetOrAddThought("evidence", "LinkType")
                ?? throw new InvalidOperationException("The evidence link type could not be created.");
            foreach (var candidate in consolidated.OrderByDescending(candidate => candidate.owners.Count))
            {
                // Distinct phrase contents determine whether a pattern is
                // significant. Every occurrence is retained as evidence.
                List<Link> members = relationshipGroup.allObservations
                    .Where(observation => SequenceMatchesPattern(candidate.pattern, observation))
                    .ToList();

                // Each wildcard position gets a class containing the individual
                // Thoughts observed in that position.
                List<Thought> description = MaterializeClassSequence(
                    candidate.pattern, members, fillerClassRoot, classLabel);
                if (description.Count < 2) continue;

                // The wildcard sequence belongs to a template. The complete
                // Phrase/Word owners are evidence, never children of it.
                Thought? existingTemplate = templateRoot.Children.FirstOrDefault(existing =>
                    GetSequenceLinks(existing).Any(link =>
                        link.LinkType == relationshipGroup.linkType &&
                        FlattenSequence(link.To).SequenceEqual(description)));
                Thought learnedTemplate = existingTemplate ?? GetOrAddThought(templateLabel, templateRoot)
                    ?? throw new InvalidOperationException("The learned template could not be created.");
                if (!GetSequenceLinks(learnedTemplate).Any(link =>
                    link.LinkType == relationshipGroup.linkType &&
                    FlattenSequence(link.To).SequenceEqual(description)))
                    AddSequenceAndLink(learnedTemplate, relationshipGroup.linkType, description);

                foreach (Link member in members)
                    AddStatement(learnedTemplate, evidenceType, member.From!);
                learnedTemplate.Weight = Math.Max(learnedTemplate.Weight, members.Count);
                if (!results.Contains(learnedTemplate)) results.Add(learnedTemplate);
            }
        }

        return results;
    }

    private string SequenceObservationSignature(Link observation)
    {
        // Thought labels are unique in a UKS. Length-prefixing prevents two
        // neighboring labels from producing an ambiguous concatenation.
        return string.Join("|", FlattenSequence(observation.To).Select(element =>
            $"{element.Label.Length}:{element.Label}"));
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
    /// of a class. Observations are grouped by their link type, and the
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
        var groups = GetSequenceLinks(parent.Children)
            .Where(link => link.LinkType is not null && link.LinkType != Labeled("is-a"))
            .GroupBy(link => link.LinkType!);

        foreach (var group in groups)
        {
            List<Link> observations = group
                .GroupBy(link => link.From!)
                .Select(ownerGroup => ownerGroup.First())
                .ToList();
            if (observations.Count < childCount * minFraction) continue;

            CommonSequencePattern? common = FindCommonSequence(observations, minFixedElements);
            if (common is null) continue;
            List<Thought> elements = MaterializeCommonSequence(common);
            if (elements.Count < 2) continue;

            Link? existing = GetSequenceLinks(parent)
                .FirstOrDefault(link => link.LinkType == group.Key);
            if (existing is not null && FlattenSequence(existing.To).SequenceEqual(elements)) continue;

            SeqElement? sequence = AddSequenceAndLink(parent, group.Key, elements);
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
        IReadOnlyList<Link> observations,
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
                .Select(observation => FlattenSequence(observation.To)[position])
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
        if (pattern is null) return false;
        return pattern.Elements.All(element => !element.IsGap ||
            element.GapCardinality == SequenceGapCardinality.ExactlyOne);
    }

    private static bool IsLearnableTemplatePattern(CommonSequencePattern pattern)
    {
        if (!HasOnlySingletonGaps(pattern)) return false;
        for (int position = 1; position < pattern.Elements.Count; position++)
            if (pattern.Elements[position - 1].IsGap && pattern.Elements[position].IsGap)
                return false;
        return true;
    }

    private static string CommonSequenceSignature(CommonSequencePattern pattern)
    {
        return string.Join("\u001f", pattern.Elements.Select(element => element.IsGap
            ? "G:" + element.GapCardinality
            : "V:" + element.Value!.Label));
    }

}

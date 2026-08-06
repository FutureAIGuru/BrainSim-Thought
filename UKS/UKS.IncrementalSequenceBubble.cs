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
    private const float LearnedSequenceDecayFactor = 0.9999f;
    private const float LearnedSequenceReinforcement = 1f;
    private const int PlasticTemplateCapacity = 50;
    private const int PlasticClassCapacity = 100;

    private sealed record IncrementalPatternPart(
        Thought? FixedValue,
        List<Thought> Fillers,
        int MinimumLength,
        int MaximumLength);

    /// <summary>
    /// Incrementally learns from one newly observed sequence. A compatible
    /// learned template is reinforced immediately. If none exists, the new
    /// observation is compared with the most similar sibling observation and
    /// one provisional template is written directly into the UKS.
    /// </summary>
    /// <remarks>
    /// The learned result consists only of ordinary UKS Thoughts, sequences,
    /// class memberships, and evidence links. No comparison result is retained
    /// outside the UKS. Unequal-length observations are aligned on their common
    /// elements; the intervening spans become cardinality-aware wildcards.
    /// </remarks>
    /// <returns>The reinforced or created template, or null.</returns>
    public Thought? IncrementalSequenceBubble(Thought sequenceOwner)
    {
        Thought? retVal = null;
        if (sequenceOwner is null) return retVal;

        Link? observationLink = sequenceOwner.LinksTo.FirstOrDefault(link =>
            link.LinkType is not null && link.To is SeqElement);
        if (observationLink?.To is not SeqElement observationElement) return retVal;
        SeqElement observationRoot = observationElement.FRST ?? observationElement;
        List<Thought> observationValues = EnumerateSequenceElements(observationRoot)
            .Select(GetElementValue)
            .Where(value => value is not null)
            .Cast<Thought>()
            .ToList();

        Thought? templateRoot = Labeled("LearnedTemplate");
        Thought? classRoot = Labeled("LearnedClass");
        DecayLearnedSequenceStructures(templateRoot);
        DecayLearnedSequenceStructures(classRoot);
        Thought? matchingTemplate = null;
        List<(Thought wildcard, Thought value)> matchingBindings = new();
        int mostFixedElements = -1;

        // First allow an existing abstraction to explain the observation. A
        // wildcard accepts an unknown value and teaches that value to its class.
        if (templateRoot is not null && classRoot is not null)
        {
            foreach (Thought template in templateRoot.Children)
            {
                foreach (Link templateLink in template.LinksTo.Where(link =>
                    link.LinkType == observationLink.LinkType && link.To is SeqElement))
                {
                    SeqElement templateElement = (SeqElement)templateLink.To!;
                    SeqElement templateSequenceRoot = templateElement.FRST ?? templateElement;
                    List<Thought> templateValues = EnumerateSequenceElements(templateSequenceRoot)
                        .Select(GetElementValue)
                        .Where(value => value is not null)
                        .Cast<Thought>()
                        .ToList();
                    bool matches = TryMatchIncrementalTemplate(
                        templateValues,
                        observationValues,
                        out List<(Thought wildcard, Thought value)> bindings,
                        out int fixedElements);
                    bool hasWildcard = templateValues.Any(value => value.HasAncestor("Wildcard"));
                    if (!matches || !hasWildcard || fixedElements <= mostFixedElements)
                        continue;

                    matchingTemplate = template;
                    matchingBindings = bindings;
                    mostFixedElements = fixedElements;
                }
            }
        }

        if (matchingTemplate is not null)
        {
            foreach ((Thought wildcard, Thought observationValue) in matchingBindings)
            {
                Thought? fillerClass = wildcard.Parents.FirstOrDefault(parent =>
                    classRoot!.Children.Contains(parent));
                if (fillerClass is null) continue;

                observationValue.AddParent(fillerClass);
                fillerClass.isPlastic = true;
                fillerClass.Weight += LearnedSequenceReinforcement;
                fillerClass.Fire();
            }

            Thought evidenceType = GetOrAddThought("evidence", "LinkType")
                ?? throw new InvalidOperationException("The evidence link type could not be created.");
            AddStatement(matchingTemplate, evidenceType, sequenceOwner);
            matchingTemplate.isPlastic = true;
            matchingTemplate.Weight += LearnedSequenceReinforcement;
            matchingTemplate.Fire();
            PruneIncrementalSequenceStructures(templateRoot!, classRoot!);
            retVal = matchingTemplate;
            if (!AtomicThoughts.Contains(matchingTemplate)) retVal = null;
            return retVal;
        }

        // A new abstraction must have at least one fixed element, at least one
        // differing element, and no adjacent differing positions. These limits
        // prevent an uninformative row of adjacent wildcards.
        Thought? bestComparisonOwner = null;
        List<IncrementalPatternPart>? bestPattern = null;
        int bestFixedCount = -1;
        int bestElementCount = 0;
        HashSet<Thought> candidateOwners = sequenceOwner.Parents
            .SelectMany(parent => parent.Children)
            .Where(candidate => candidate != sequenceOwner)
            .ToHashSet();

        foreach (Thought candidateOwner in candidateOwners)
        {
            foreach (Link candidateLink in candidateOwner.LinksTo.Where(link =>
                link.LinkType == observationLink.LinkType && link.To is SeqElement))
            {
                SeqElement candidateElement = (SeqElement)candidateLink.To!;
                SeqElement candidateRoot = candidateElement.FRST ?? candidateElement;
                List<Thought> candidateValues = EnumerateSequenceElements(candidateRoot)
                    .Select(GetElementValue)
                    .Where(value => value is not null)
                    .Cast<Thought>()
                    .ToList();
                List<IncrementalPatternPart>? candidatePattern = BuildIncrementalPattern(
                    observationValues,
                    candidateValues,
                    out int fixedCount);
                if (candidatePattern is null || fixedCount == 0) continue;
                if (fixedCount <= bestFixedCount) continue;

                bestComparisonOwner = candidateOwner;
                bestPattern = candidatePattern;
                bestFixedCount = fixedCount;
                bestElementCount = Math.Max(observationValues.Count, candidateValues.Count);
            }
        }

        if (bestComparisonOwner is null || bestPattern is null)
        {
            if (templateRoot is not null && classRoot is not null)
                PruneIncrementalSequenceStructures(templateRoot, classRoot);
            return retVal;
        }

        templateRoot = GetOrAddThought("LearnedTemplate", "LanguageElement")
            ?? throw new InvalidOperationException("The learned-template root could not be created.");
        classRoot = GetOrAddThought("LearnedClass", "LanguageElement")
            ?? throw new InvalidOperationException("The learned-class root could not be created.");
        List<Thought> templateElements = new();
        foreach (IncrementalPatternPart patternPart in bestPattern)
        {
            if (patternPart.FixedValue is not null)
            {
                templateElements.Add(patternPart.FixedValue);
                continue;
            }

            Thought fillerClass = GetOrCreateThoughtClass(
                classRoot,
                patternPart.Fillers,
                "class*");
            fillerClass.isPlastic = true;
            fillerClass.Weight = Math.Max(fillerClass.Weight, fillerClass.Children.Count);
            fillerClass.Fire();
            (string wildcardPrefix, string wildcardProperty) =
                GetWildcardCardinality(patternPart.MinimumLength, patternPart.MaximumLength);
            string wildcardLabel = wildcardPrefix + fillerClass.Label;
            Thought wildcard = Labeled(wildcardLabel) ??
                CreateWildcard(wildcardLabel, new List<Thought> { fillerClass }, wildcardProperty);
            templateElements.Add(wildcard);
        }

        Thought learnedTemplate = GetOrAddThought("template*", templateRoot)
            ?? throw new InvalidOperationException("The learned template could not be created.");
        learnedTemplate.isPlastic = true;
        AddSequenceAndLink(learnedTemplate, observationLink.LinkType!, templateElements);

        Thought newEvidenceType = GetOrAddThought("evidence", "LinkType")
            ?? throw new InvalidOperationException("The evidence link type could not be created.");
        Link? firstEvidence = AddStatement(learnedTemplate, newEvidenceType, bestComparisonOwner);
        Link? secondEvidence = AddStatement(learnedTemplate, newEvidenceType, sequenceOwner);
        float comparisonLikelihood = bestFixedCount / (float)bestElementCount;
        if (firstEvidence is not null) firstEvidence.Weight = comparisonLikelihood;
        if (secondEvidence is not null) secondEvidence.Weight = comparisonLikelihood;
        int newEvidenceCount = learnedTemplate.LinksTo.Count(link => link.LinkType == newEvidenceType);
        learnedTemplate.Weight = Math.Max(learnedTemplate.Weight, newEvidenceCount);
        learnedTemplate.Fire();

        PruneIncrementalSequenceStructures(templateRoot, classRoot);
        retVal = learnedTemplate;
        if (!AtomicThoughts.Contains(learnedTemplate)) retVal = null;
        return retVal;
    }

    private static List<IncrementalPatternPart>? BuildIncrementalPattern(
        IReadOnlyList<Thought> first,
        IReadOnlyList<Thought> second,
        out int fixedCount)
    {
        int[,] commonLengths = new int[first.Count + 1, second.Count + 1];
        for (int firstIndex = first.Count - 1; firstIndex >= 0; firstIndex--)
        {
            for (int secondIndex = second.Count - 1; secondIndex >= 0; secondIndex--)
            {
                commonLengths[firstIndex, secondIndex] = first[firstIndex] == second[secondIndex]
                    ? commonLengths[firstIndex + 1, secondIndex + 1] + 1
                    : Math.Max(
                        commonLengths[firstIndex + 1, secondIndex],
                        commonLengths[firstIndex, secondIndex + 1]);
            }
        }

        List<(int firstIndex, int secondIndex)> fixedPositions = new();
        int firstPosition = 0;
        int secondPosition = 0;
        while (firstPosition < first.Count && secondPosition < second.Count)
        {
            if (first[firstPosition] == second[secondPosition])
            {
                fixedPositions.Add((firstPosition, secondPosition));
                firstPosition++;
                secondPosition++;
            }
            else if (commonLengths[firstPosition + 1, secondPosition] >=
                commonLengths[firstPosition, secondPosition + 1])
            {
                firstPosition++;
            }
            else
            {
                secondPosition++;
            }
        }

        fixedCount = fixedPositions.Count;
        if (fixedCount == 0) return null;

        List<IncrementalPatternPart> retVal = new();
        int firstGapStart = 0;
        int secondGapStart = 0;
        foreach ((int firstFixed, int secondFixed) in fixedPositions)
        {
            AddIncrementalGap(
                retVal,
                first,
                firstGapStart,
                firstFixed,
                second,
                secondGapStart,
                secondFixed);
            retVal.Add(new IncrementalPatternPart(
                first[firstFixed], new List<Thought>(), 1, 1));
            firstGapStart = firstFixed + 1;
            secondGapStart = secondFixed + 1;
        }

        AddIncrementalGap(
            retVal,
            first,
            firstGapStart,
            first.Count,
            second,
            secondGapStart,
            second.Count);

        bool hasVariablePart = retVal.Any(part => part.FixedValue is null);
        if (!hasVariablePart) return null;
        return retVal;
    }

    private static void AddIncrementalGap(
        List<IncrementalPatternPart> pattern,
        IReadOnlyList<Thought> first,
        int firstStart,
        int firstEnd,
        IReadOnlyList<Thought> second,
        int secondStart,
        int secondEnd)
    {
        int firstLength = firstEnd - firstStart;
        int secondLength = secondEnd - secondStart;
        if (firstLength == 0 && secondLength == 0) return;

        List<Thought> fillers = first.Skip(firstStart).Take(firstLength)
            .Concat(second.Skip(secondStart).Take(secondLength))
            .Distinct()
            .ToList();
        int minimumLength = Math.Min(firstLength, secondLength);
        int maximumLength = Math.Max(firstLength, secondLength);
        pattern.Add(new IncrementalPatternPart(
            null, fillers, minimumLength, maximumLength));
    }

    private static (string prefix, string property) GetWildcardCardinality(
        int minimumLength,
        int maximumLength)
    {
        (string prefix, string property) retVal;
        if (minimumLength == 1 && maximumLength == 1)
            retVal = ("??", "isWildcard");
        else if (minimumLength == 0 && maximumLength == 1)
            retVal = ("???", "isOptionalWildcard");
        else if (minimumLength == 0)
            retVal = ("??*", "is*Wildcard");
        else
            retVal = ("??+", "is+Wildcard");
        return retVal;
    }

    private static bool TryMatchIncrementalTemplate(
        IReadOnlyList<Thought> template,
        IReadOnlyList<Thought> observation,
        out List<(Thought wildcard, Thought value)> bindings,
        out int fixedCount)
    {
        fixedCount = template.Count(value => !value.HasAncestor("Wildcard"));
        bindings = new List<(Thought wildcard, Thought value)>();
        bool retVal = MatchIncrementalTemplateFrom(
            template, observation, 0, 0, bindings);
        if (!retVal) bindings.Clear();
        return retVal;
    }

    private static bool MatchIncrementalTemplateFrom(
        IReadOnlyList<Thought> template,
        IReadOnlyList<Thought> observation,
        int templatePosition,
        int observationPosition,
        List<(Thought wildcard, Thought value)> bindings)
    {
        if (templatePosition == template.Count)
            return observationPosition == observation.Count;

        Thought templateValue = template[templatePosition];
        if (!templateValue.HasAncestor("Wildcard"))
        {
            if (observationPosition >= observation.Count ||
                templateValue != observation[observationPosition])
                return false;
            return MatchIncrementalTemplateFrom(
                template,
                observation,
                templatePosition + 1,
                observationPosition + 1,
                bindings);
        }

        int minimumCount = templateValue.HasProperty("is+Wildcard") ||
            templateValue.HasProperty("isWildcard") ? 1 : 0;
        int maximumCount = templateValue.HasProperty("isWildcard") ||
            templateValue.HasProperty("isOptionalWildcard")
            ? Math.Min(1, observation.Count - observationPosition)
            : observation.Count - observationPosition;

        for (int consumed = minimumCount; consumed <= maximumCount; consumed++)
        {
            int originalBindingCount = bindings.Count;
            for (int offset = 0; offset < consumed; offset++)
                bindings.Add((templateValue, observation[observationPosition + offset]));

            bool suffixMatches = MatchIncrementalTemplateFrom(
                template,
                observation,
                templatePosition + 1,
                observationPosition + consumed,
                bindings);
            if (suffixMatches) return true;

            bindings.RemoveRange(originalBindingCount, bindings.Count - originalBindingCount);
        }
        return false;
    }

    private static void DecayLearnedSequenceStructures(Thought? root)
    {
        if (root is null) return;

        foreach (Thought child in root.Children)
        {
            if (!child.isPlastic) continue;
            child.Weight *= LearnedSequenceDecayFactor;
        }
    }

    private void PruneIncrementalSequenceStructures(
        Thought templateRoot,
        Thought classRoot)
    {
        while (templateRoot.Children.Count(template => template.isPlastic) >
            PlasticTemplateCapacity)
        {
            Thought? prunedTemplate = PruneLowestScoringChild(templateRoot);
            if (prunedTemplate is null) break;
        }

        RemoveUnusedLearnedClasses(templateRoot, classRoot);

        while (classRoot.Children.Count(learnedClass => learnedClass.isPlastic) >
            PlasticClassCapacity)
        {
            Thought? weakestClass = classRoot.Children
                .Where(learnedClass => learnedClass.isPlastic)
                .OrderBy(learnedClass => learnedClass.Weight)
                .ThenBy(learnedClass => learnedClass.LastFiredTime)
                .FirstOrDefault();
            if (weakestClass is null) break;

            List<Thought> dependentTemplates = templateRoot.Children
                .Where(template => TemplateUsesClass(template, weakestClass))
                .OrderBy(template => template.Weight)
                .ThenBy(template => template.LastFiredTime)
                .ToList();
            if (dependentTemplates.Count == 0)
            {
                DeleteLearnedClass(weakestClass);
            }
            else
            {
                dependentTemplates[0].Delete();
                RemoveUnusedLearnedClasses(templateRoot, classRoot);
            }
        }
    }

    private void RemoveUnusedLearnedClasses(
        Thought templateRoot,
        Thought classRoot)
    {
        List<Thought> unusedClasses = classRoot.Children
            .Where(learnedClass => learnedClass.isPlastic &&
                !templateRoot.Children.Any(template =>
                    TemplateUsesClass(template, learnedClass)))
            .ToList();

        foreach (Thought unusedClass in unusedClasses)
            DeleteLearnedClass(unusedClass);
    }

    private bool TemplateUsesClass(Thought template, Thought learnedClass)
    {
        bool retVal = false;
        foreach (Link templateLink in template.LinksTo.Where(link => link.To is SeqElement))
        {
            SeqElement templateElement = (SeqElement)templateLink.To!;
            SeqElement templateSequenceRoot = templateElement.FRST ?? templateElement;
            foreach (SeqElement sequenceElement in EnumerateSequenceElements(templateSequenceRoot))
            {
                Thought? value = GetElementValue(sequenceElement);
                if (value is null || !value.HasAncestor("Wildcard")) continue;
                if (!value.Parents.Contains(learnedClass)) continue;

                retVal = true;
                break;
            }
            if (retVal) break;
        }
        return retVal;
    }

    private static void DeleteLearnedClass(Thought learnedClass)
    {
        List<Thought> children = learnedClass.Children.ToList();
        foreach (Thought child in children)
        {
            if (child.HasAncestor("Wildcard"))
                child.Delete();
            else
                child.RemoveParent(learnedClass);
        }
        learnedClass.Delete();
    }
}

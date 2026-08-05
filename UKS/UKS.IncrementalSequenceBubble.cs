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
    /// <summary>
    /// Incrementally learns from one newly observed sequence. A compatible
    /// learned template is reinforced immediately. If none exists, the new
    /// observation is compared with the most similar sibling observation and
    /// one provisional template is written directly into the UKS.
    /// </summary>
    /// <remarks>
    /// The learned result consists only of ordinary UKS Thoughts, sequences,
    /// class memberships, and evidence links. No comparison result is retained
    /// outside the UKS. For now, comparisons require equal-length sequences and
    /// create only separated, exactly-one wildcards.
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

        Thought? templateRoot = Labeled("LearnedTemplate");
        Thought? classRoot = Labeled("LearnedClass");
        Thought? matchingTemplate = null;
        SeqElement? matchingTemplateRoot = null;
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
                    using IEnumerator<SeqElement> templateEnumerator =
                        EnumerateSequenceElements(templateSequenceRoot).GetEnumerator();
                    using IEnumerator<SeqElement> observationEnumerator =
                        EnumerateSequenceElements(observationRoot).GetEnumerator();

                    bool matches = true;
                    bool hasWildcard = false;
                    int fixedElements = 0;
                    bool hasTemplateElement = templateEnumerator.MoveNext();
                    bool hasObservationElement = observationEnumerator.MoveNext();
                    while (hasTemplateElement && hasObservationElement)
                    {
                        Thought? templateValue = GetElementValue(templateEnumerator.Current);
                        Thought? observationValue = GetElementValue(observationEnumerator.Current);
                        if (templateValue is null || observationValue is null)
                        {
                            matches = false;
                            break;
                        }

                        if (templateValue.HasAncestor("Wildcard") &&
                            templateValue.HasProperty("isWildcard"))
                        {
                            hasWildcard = true;
                        }
                        else if (templateValue != observationValue)
                        {
                            matches = false;
                            break;
                        }
                        else
                        {
                            fixedElements++;
                        }

                        hasTemplateElement = templateEnumerator.MoveNext();
                        hasObservationElement = observationEnumerator.MoveNext();
                    }

                    if (hasTemplateElement != hasObservationElement) matches = false;

                    if (!matches || !hasWildcard || fixedElements <= mostFixedElements)
                        continue;

                    matchingTemplate = template;
                    matchingTemplateRoot = templateSequenceRoot;
                    mostFixedElements = fixedElements;
                }
            }
        }

        if (matchingTemplate is not null && matchingTemplateRoot is not null)
        {
            using IEnumerator<SeqElement> templateEnumerator =
                EnumerateSequenceElements(matchingTemplateRoot).GetEnumerator();
            using IEnumerator<SeqElement> observationEnumerator =
                EnumerateSequenceElements(observationRoot).GetEnumerator();
            while (templateEnumerator.MoveNext() && observationEnumerator.MoveNext())
            {
                Thought? wildcard = GetElementValue(templateEnumerator.Current);
                Thought? observationValue = GetElementValue(observationEnumerator.Current);
                if (wildcard is null || observationValue is null) continue;
                if (!wildcard.HasAncestor("Wildcard") || !wildcard.HasProperty("isWildcard"))
                    continue;

                Thought? fillerClass = wildcard.Parents.FirstOrDefault(parent =>
                    classRoot!.Children.Contains(parent));
                if (fillerClass is null) continue;

                observationValue.AddParent(fillerClass);
                fillerClass.Weight = Math.Max(fillerClass.Weight, fillerClass.Children.Count);
            }

            Thought evidenceType = GetOrAddThought("evidence", "LinkType")
                ?? throw new InvalidOperationException("The evidence link type could not be created.");
            AddStatement(matchingTemplate, evidenceType, sequenceOwner);
            int evidenceCount = matchingTemplate.LinksTo.Count(link => link.LinkType == evidenceType);
            matchingTemplate.Weight = Math.Max(matchingTemplate.Weight, evidenceCount);
            retVal = matchingTemplate;
            return retVal;
        }

        // A new abstraction must have at least one fixed element, at least one
        // differing element, and no adjacent differing positions. These limits
        // prevent an uninformative row of adjacent wildcards.
        Thought? bestComparisonOwner = null;
        SeqElement? bestComparisonRoot = null;
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
                using IEnumerator<SeqElement> observationEnumerator =
                    EnumerateSequenceElements(observationRoot).GetEnumerator();
                using IEnumerator<SeqElement> candidateEnumerator =
                    EnumerateSequenceElements(candidateRoot).GetEnumerator();

                int fixedCount = 0;
                int differenceCount = 0;
                int elementCount = 0;
                bool previousWasDifferent = false;
                bool hasAdjacentDifferences = false;
                bool validComparison = true;
                bool hasObservationElement = observationEnumerator.MoveNext();
                bool hasCandidateElement = candidateEnumerator.MoveNext();
                while (hasObservationElement && hasCandidateElement)
                {
                    Thought? observationValue = GetElementValue(observationEnumerator.Current);
                    Thought? candidateValue = GetElementValue(candidateEnumerator.Current);
                    if (observationValue is null || candidateValue is null)
                    {
                        validComparison = false;
                        break;
                    }

                    elementCount++;
                    bool isDifferent = observationValue != candidateValue;
                    if (isDifferent)
                    {
                        differenceCount++;
                        if (previousWasDifferent) hasAdjacentDifferences = true;
                    }
                    else
                    {
                        fixedCount++;
                    }
                    previousWasDifferent = isDifferent;

                    hasObservationElement = observationEnumerator.MoveNext();
                    hasCandidateElement = candidateEnumerator.MoveNext();
                }

                if (hasObservationElement != hasCandidateElement) validComparison = false;
                if (!validComparison || differenceCount == 0 || fixedCount == 0 ||
                    hasAdjacentDifferences)
                    continue;
                if (fixedCount <= bestFixedCount) continue;

                bestComparisonOwner = candidateOwner;
                bestComparisonRoot = candidateRoot;
                bestFixedCount = fixedCount;
                bestElementCount = elementCount;
            }
        }

        if (bestComparisonOwner is null || bestComparisonRoot is null) return retVal;

        templateRoot = GetOrAddThought("LearnedTemplate", "LanguageElement")
            ?? throw new InvalidOperationException("The learned-template root could not be created.");
        classRoot = GetOrAddThought("LearnedClass", "LanguageElement")
            ?? throw new InvalidOperationException("The learned-class root could not be created.");
        List<Thought> templateElements = new();
        using IEnumerator<SeqElement> newObservationEnumerator =
            EnumerateSequenceElements(observationRoot).GetEnumerator();
        using IEnumerator<SeqElement> bestComparisonEnumerator =
            EnumerateSequenceElements(bestComparisonRoot).GetEnumerator();
        while (newObservationEnumerator.MoveNext() && bestComparisonEnumerator.MoveNext())
        {
            Thought? observedElement = GetElementValue(newObservationEnumerator.Current);
            Thought? comparisonElement = GetElementValue(bestComparisonEnumerator.Current);
            if (observedElement is null || comparisonElement is null) continue;
            if (observedElement == comparisonElement)
            {
                templateElements.Add(observedElement);
                continue;
            }

            Thought fillerClass = GetOrCreateThoughtClass(
                classRoot,
                new[] { comparisonElement, observedElement },
                "class*");
            fillerClass.Weight = Math.Max(fillerClass.Weight, fillerClass.Children.Count);
            Thought wildcard = Labeled("??" + fillerClass.Label) ??
                CreateWildcard("??" + fillerClass.Label, new List<Thought> { fillerClass });
            templateElements.Add(wildcard);
        }

        Thought learnedTemplate = GetOrAddThought("template*", templateRoot)
            ?? throw new InvalidOperationException("The learned template could not be created.");
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

        retVal = learnedTemplate;
        return retVal;
    }
}

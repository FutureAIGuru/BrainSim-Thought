/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License. You may use, copy, modify, merge, publish, distribute,
 * sublicense, and/or sell copies of the Software, subject to the License.
 */

namespace UKS;

public partial class UKS
{
    public Dictionary<Thought, Thought> GetSequenceBindings(SeqElement templateSequence,SeqElement concreteSequence)
    {
        Dictionary<Thought, Thought> bindings = new();

        List<Thought> template = theUKS.FlattenSequence(templateSequence);
        List<Thought> concrete = theUKS.FlattenSequence(concreteSequence);

        if (template.Count != concrete.Count)
            return null;

        for (int i = 0; i < template.Count; i++)
        {
            Thought t = template[i];
            Thought c = concrete[i];

            if (!t.HasProperty("isWildcard"))
                continue;

            // Repeated use of the same wildcard must bind consistently.
            if (bindings.TryGetValue(t, out Thought previous))
            {
                if (previous != c)
                    return null;
            }
            else
            {
                bindings[t] = c;
            }
        }

        return bindings;
    }

    /// <summary>
    /// Compares two equal-length sequences and writes their positional commonality as a learned template.
    /// Equal values remain fixed; each pair of unequal values becomes a learned class and a singleton wildcard.
    /// </summary>
    /// <returns>The existing or newly created template, or null when the sequences cannot form a useful template.</returns>
    public Thought? CreateNewTemplate(SeqElement firstSequence, SeqElement secondSequence)
    {
        Thought? retVal = null;
        if (firstSequence is null || secondSequence is null) return retVal;

        SeqElement firstRoot = GetFirstElement(firstSequence) ?? firstSequence;
        SeqElement secondRoot = GetFirstElement(secondSequence) ?? secondSequence;
        SeqElement? firstElement = firstRoot;
        SeqElement? secondElement = secondRoot;
        int fixedCount = 0;
        int differenceCount = 0;
        //scan throught the two sequences and count the differences and similarities.
        //If there are no differences or no similarities, then we cannot create a useful template.
        while (firstElement is not null && secondElement is not null)
        {
            Thought? firstValue = GetElementValue(firstElement);
            Thought? secondValue = GetElementValue(secondElement);
            if (firstValue is null || secondValue is null) return retVal;

            if (firstValue == secondValue) fixedCount++;
            else differenceCount++;

            firstElement = GetNextElement(firstElement);
            secondElement = GetNextElement(secondElement);
        }

        // Unequal lengths need cardinality wildcards and alignment, which are deliberately outside this first comparator.
        if (firstElement is not null || secondElement is not null || fixedCount == 0 || differenceCount == 0)
            return retVal;

        //possible matching template, so create a new template and class root if they don't already exist.
        Thought? templateRoot = Labeled("LearnedTemplate");
        Thought? classRoot = Labeled("LearnedClass");
        if (templateRoot is null || classRoot is null) return retVal;

        List<Thought> templateValues = new();
        firstElement = firstRoot;
        secondElement = secondRoot;
        bool lastWasWildcard = false;
        while (firstElement is not null && secondElement is not null)
        {
            Thought firstValue = GetElementValue(firstElement);
            Thought secondValue = GetElementValue(secondElement);
            if (firstValue == secondValue)
            {
                templateValues.Add(firstValue);
                lastWasWildcard = false;
            }
            else
            {
                if (lastWasWildcard) return null;
                List<Thought> members = new() { firstValue, secondValue };
                Thought fillerClass = GetOrCreateThoughtClass(classRoot, members, "class*");
                string wildcardLabel = "??" + fillerClass.Label;
                Thought? wildcard = Labeled(wildcardLabel);
                if (wildcard is null)
                    wildcard = CreateWildcard(wildcardLabel, new List<Thought> { fillerClass });
                templateValues.Add(wildcard);
                lastWasWildcard = true;
            }

            firstElement = GetNextElement(firstElement);
            secondElement = GetNextElement(secondElement);
        }
        retVal = GetOrAddThought("template*", templateRoot);
        AddSequenceAndLink(retVal, "hasWords", templateValues);
        return retVal;
    }
}

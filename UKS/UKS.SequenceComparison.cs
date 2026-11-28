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

namespace UKS.Agents;

public static partial class SequenceAgents
{
    /// <summary>
    /// Binds each wildcard of a template sequence to the value at the same place in an equal-length
    /// concrete sequence; a wildcard used twice must bind to the same value.
    /// </summary>
    /// <returns>The bindings, or null if the lengths differ or a repeated wildcard binds inconsistently.</returns>
    public static Dictionary<Thought, Thought>? GetSequenceBindings(this UKS store, SeqElement templateSequence, SeqElement concreteSequence)
    {
        Dictionary<Thought, Thought> bindings = new();

        List<Thought> template = store.FlattenSequence(templateSequence);
        List<Thought> concrete = store.FlattenSequence(concreteSequence);

        if (template.Count != concrete.Count)
            return null;

        for (int i = 0; i < template.Count; i++)
        {
            Thought t = template[i];
            Thought c = concrete[i];

            if (!t.HasProperty("isWildcard"))
                continue;

            // Repeated use of the same wildcard must bind consistently.
            if (bindings.TryGetValue(t, out Thought? previous))
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
    public static Thought? CreateNewTemplate(this UKS store, SeqElement firstSequence, SeqElement secondSequence)
    {
        Thought? retVal = null;
        if (firstSequence is null || secondSequence is null) return retVal;

        SeqElement firstRoot = firstSequence.FRST ?? firstSequence;
        SeqElement secondRoot = secondSequence.FRST ?? secondSequence;
        SeqElement? firstElement = firstRoot;
        SeqElement? secondElement = secondRoot;
        int fixedCount = 0;
        int differenceCount = 0;
        //scan throught the two sequences and count the differences and similarities.
        //If there are no differences or no similarities, then we cannot create a useful template.
        while (firstElement is not null && secondElement is not null)
        {
            Thought? firstValue = store.GetElementValue(firstElement);
            Thought? secondValue = store.GetElementValue(secondElement);
            if (firstValue is null || secondValue is null) return retVal;

            if (firstValue == secondValue) fixedCount++;
            else differenceCount++;

            firstElement = firstElement.NXT;
            secondElement = secondElement.NXT;
        }

        // Unequal lengths need cardinality wildcards and alignment, which are deliberately outside this first comparator.
        if (firstElement is not null || secondElement is not null || fixedCount == 0 || differenceCount == 0)
            return retVal;

        //possible matching template, so create a new template and class root if they don't already exist.
        Thought? templateRoot = store.Labeled("LearnedTemplate");
        Thought? classRoot = store.Labeled("LearnedClass");
        if (templateRoot is null || classRoot is null) return retVal;

        List<Thought> templateValues = new();
        firstElement = firstRoot;
        secondElement = secondRoot;
        bool lastWasWildcard = false;
        while (firstElement is not null && secondElement is not null)
        {
            Thought firstValue = store.GetElementValue(firstElement)!; // checked non-null in the first pass
            Thought secondValue = store.GetElementValue(secondElement)!;
            if (firstValue == secondValue)
            {
                templateValues.Add(firstValue);
                lastWasWildcard = false;
            }
            else
            {
                if (lastWasWildcard) return null;
                List<Thought> members = new() { firstValue, secondValue };
                Thought fillerClass = store.GetOrCreateThoughtClass(classRoot, members, "class*");
                string wildcardLabel = "??" + fillerClass.Label;
                Thought? wildcard = store.Labeled(wildcardLabel);
                if (wildcard is null)
                    wildcard = store.CreateWildcard(wildcardLabel, new List<Thought> { fillerClass });
                templateValues.Add(wildcard);
                lastWasWildcard = true;
            }

            firstElement = firstElement.NXT;
            secondElement = secondElement.NXT;
        }
        retVal = store.GetOrAddThought("template*", templateRoot)!;
        store.AddSequenceAndLink(retVal, "hasWords", templateValues);
        return retVal;
    }
}

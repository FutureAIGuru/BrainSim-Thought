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
    /// Finds flat stored sequences close to a pattern, for search options with allowOutOfOrder.
    /// Each pattern element activates an element with the same value in every stored sequence, in
    /// any order, and each stored element can be activated once ("fdio" fully activates "fido").
    /// Confidence is the share of activated elements, of the longer of pattern and sequence, so a
    /// missing or extra element lowers it (without allowPartialMatch, every element of both must be
    /// activated); out-of-order elements lower it a little, and with preferFirstLast a wrong first
    /// or last element lowers it more. An exact match has confidence 1.
    /// </summary>
    /// <returns>Sequence first elements with confidence, best first.</returns>
    internal List<(SeqElement seqNode, float confidence)> FindSequencesOutOfOrder(List<Thought> pattern, Thought searchOptions)
    {
        var results = new List<(SeqElement seqNode, float confidence)>();
        if (pattern is null || pattern.Count == 0 || searchOptions is null) return results;
        bool preferFirstLast = searchOptions.HasProperty("preferFirstLast");
        bool allowPartialMatch = searchOptions.HasProperty("allowPartialMatch");
        Thought? vluType = Labeled("VLU");

        // The sequences an element belongs to: its own, and (as stored sequences reuse common
        // subsequences) every sequence that holds that one as an element, at any depth.
        IEnumerable<SeqElement> Containing(SeqElement element)
        {
            var seen = new HashSet<SeqElement>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<SeqElement>();
            if (element.FRST is SeqElement own) pending.Push(own);
            while (pending.Count > 0)
            {
                SeqElement first = pending.Pop();
                if (!seen.Add(first)) continue;
                yield return first;
                foreach (Link link in first.LinksFrom)
                    if (link.LinkType == vluType && link.From is SeqElement holder && holder.FRST is SeqElement outer)
                        pending.Push(outer);
            }
        }

        // Spread activation: from each pattern value to the stored elements holding it, and on to
        // the sequences containing them.
        var activated = new Dictionary<SeqElement, HashSet<SeqElement>>(ReferenceEqualityComparer.Instance);
        foreach (Thought value in pattern)
        {
            var reachedThisTime = new HashSet<SeqElement>(ReferenceEqualityComparer.Instance);
            foreach (Link link in value.LinksFrom)
            {
                if (link.LinkType != vluType || link.From is not SeqElement element) continue;
                foreach (SeqElement first in Containing(element))
                {
                    if (!activated.TryGetValue(first, out HashSet<SeqElement>? elements))
                        activated[first] = elements = new HashSet<SeqElement>(ReferenceEqualityComparer.Instance);
                    // One element per pattern element and sequence: a repeated value activates the next copy.
                    if (reachedThisTime.Contains(first) || elements.Contains(element)) continue;
                    elements.Add(element);
                    reachedThisTime.Add(first);
                }
            }
        }

        foreach (var (first, elements) in activated)
        {
            List<Thought?> stored = EnumerateSequenceElements(first).Select(element => element.VLU).ToList();
            float coverage = (float)elements.Count / Math.Max(stored.Count, pattern.Count);
            if (!allowPartialMatch && coverage < 1) continue;
            float order = InOrderPairs(stored, pattern);
            float confidence = coverage * (0.9f + 0.1f * order);
            if (preferFirstLast)
            {
                if (!ReferenceEquals(stored[0], pattern[0])) confidence *= 0.8f;
                if (!ReferenceEquals(stored[^1], pattern[^1])) confidence *= 0.8f;
            }
            results.Add((first, confidence));
        }
        return results.OrderByDescending(result => result.confidence).ToList();
    }

    // The share of the pattern's neighboring pairs that are neighbors, in that order, in the stored
    // sequence: 1 for the same order, lower as elements are swapped.
    private static float InOrderPairs(List<Thought?> stored, List<Thought> pattern)
    {
        if (pattern.Count < 2) return 1;
        var storedPairs = stored.Zip(stored.Skip(1)).ToList();
        int kept = pattern.Zip(pattern.Skip(1)).Count(pair => storedPairs.Any(candidate =>
            ReferenceEquals(candidate.First, pair.First) && ReferenceEquals(candidate.Second, pair.Second)));
        return (float)kept / (pattern.Count - 1);
    }
}

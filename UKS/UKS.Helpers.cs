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

namespace UKS.Helpers;

/// <summary>
/// Shorthands for common lookups on a Thought's links. A helper uses only the public UKS API and is
/// not part of the core: add <c>using UKS.Helpers;</c> to use one.
/// </summary>
public static class ThoughtHelpers
{
    /// <summary>The Thought itself, then its ancestors, nearest first.</summary>
    public static IEnumerable<Thought> AncestorsWithSelf(this Thought thought) => thought.Ancestors.Prepend(thought);

    /// <summary>The target of this Thought's first link of the given type, or null.</summary>
    public static Thought? GetTargetOfFirstLinkOfType(this Thought thought, ThoughtRef linkType) =>
        linkType.Find(thought.Store) is Thought type
            ? thought.LinksTo.FirstOrDefault(link => link.LinkType == type)?.To : null;

    /// <summary>The source of the first link of the given type that points to this Thought, or null.</summary>
    public static Thought? GetSourceOfFirstLinkOfType(this Thought thought, ThoughtRef linkType) =>
        linkType.Find(thought.Store) is Thought type
            ? thought.LinksFrom.FirstOrDefault(link => link.LinkType == type)?.From : null;

    /// <summary>The target of this Thought's highest-weight link of the given type, or null.</summary>
    public static Thought? GetTargetOfHighestWeightLinkOfType(this Thought thought, ThoughtRef linkType) =>
        linkType.Find(thought.Store) is Thought type
            ? thought.LinksTo.Where(link => link.LinkType == type).MaxBy(link => link.Weight)?.To : null;

    /// <summary>Removes every link of the given type from this Thought; an unknown label removes nothing.</summary>
    public static void RemoveLinks(this Thought thought, ThoughtRef linkType)
    {
        if (linkType.Find(thought.Store) is not Thought type) return;
        foreach (Link link in thought.LinksTo.Where(link => link.LinkType == type))
            thought.RemoveLink(link);
    }
}

/// <summary>
/// Reading typed input as Thoughts. A helper uses only the public UKS API and is not part of the
/// core: add <c>using UKS.Helpers;</c> to use one.
/// </summary>
public static class PhraseHelpers
{
    /// <summary>
    /// Gets or adds the Thought a typed phrase names. Several words make a dotted label: with
    /// <paramref name="attributesFollow"/> the first word is the base ("has 4" is has.4), otherwise the
    /// last ("big dogs" is dogs.big). A phrase starting with ^ is a sequence ("^a b c"), where a
    /// one-character element is that character.
    /// </summary>
    /// <param name="store">The store.</param>
    /// <param name="phrase">The typed words.</param>
    /// <param name="attributesFollow">True if the first word is the base (a link type, "has 4").</param>
    /// <param name="singularize">
    /// Optional, applied to each word that doesn't start with a capital letter (e.g. an English
    /// singularizer, so "big dogs" is dog.big). The UKS itself knows no language.
    /// </param>
    /// <returns>The Thought, or null for an empty phrase.</returns>
    public static Thought? CreateThoughtFromMultipleAttributes(this UKS store, string phrase, bool attributesFollow,
        Func<string, string>? singularize = null)
    {
        phrase = phrase.Trim();
        if (phrase.StartsWith('^'))
        {
            string[] elementLabels = phrase[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            List<Thought> elements = new();
            foreach (string elementLabel in elementLabels)
            {
                Thought? element = elementLabel.Length == 1 ? store.GetOrAddCharacter(elementLabel[0])
                    : store.Labeled(elementLabel) ?? (elementLabel.StartsWith("w:") ? store.GetOrAddThought(elementLabel, "word") : store.GetOrAddThought(elementLabel));
                if (element is not null) elements.Add(element);
            }
            return store.AddSequence(string.Join("", elementLabels), elements, false);
        }

        string[] words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;
        if (singularize is not null)
            words = words.Select(word => char.IsUpper(word[0]) ? word : singularize(word)).ToArray();
        IEnumerable<string> ordered = attributesFollow ? words : words.Skip(words.Length - 1).Concat(words.SkipLast(1));
        return store.GetOrAddThought(string.Join(".", ordered));
    }
}

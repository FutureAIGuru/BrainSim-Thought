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

// Characters: a Thought for each written character (c:A) and spellings as character sequences.
// Letters are language content rather than store mechanics; these are grouped here so they can move
// out of the UKS once the starter content is settled (API.md, Open).
public partial class UKS
{
    /// <summary>The label prefix of character Thoughts (c:A is the letter a).</summary>
    public const string CharacterPrefix = "c:";

    /// <summary>The label of a character's Thought, e.g. c:A for 'a' (letters are upper-cased).</summary>
    public static string GetCharacterLabel(char character) =>
        CharacterPrefix + char.ToUpperInvariant(character);

    /// <summary>The Thought for a written character (under character), created if needed.</summary>
    public Thought GetOrAddCharacter(char character)
    {
        Thought characterRoot = GetOrAddThought("character", "Abstract")!;
        return GetOrAddThought(GetCharacterLabel(character), characterRoot)!;
    }

    /// <summary>Stores a word's spelling as a sequence of characters and links the owner to it with spelled.</summary>
    /// <returns>The first element, or null for an empty spelling or no owner.</returns>
    public SeqElement? CreateSpellingSequence(string spelling, Thought? owner)
    {
        if (owner is null || string.IsNullOrEmpty(spelling)) return null;

        List<Thought> characters = spelling
            .Select(GetOrAddCharacter)
            .ToList();
        Thought spelled = GetOrAddThought("spelled", "LinkType")!;
        return AddSequenceAndLink(owner, spelled, characters);
    }
}

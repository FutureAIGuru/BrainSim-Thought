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
using System.Collections.Generic;
using System.Linq;
using UKS;
using UKS.Helpers;

namespace BrainSimulator.Modules;

// Spelling lookup needs only the UKS. It is kept separate from the WPF-dependent
// module so the headless UKS.Tests project can compile it (with ModuleInterpret).
public partial class ModuleWord
{
    // Exact sequence search returns spelling heads. A head can describe several words.
    internal static IEnumerable<Thought> FindWordsBySpelling(UKS.UKS store, string spelling)
    {
        Thought spelled = store.Labeled("spelled");
        Thought wordRoot = store.Labeled("word");
        if (spelled is null || wordRoot is null || string.IsNullOrEmpty(spelling)) yield break;

        var letters = new List<Thought>(spelling.Length);
        foreach (char character in spelling)
        {
            Thought letter = store.Labeled(UKS.UKS.GetCharacterLabel(character));
            if (letter is null) yield break;
            letters.Add(letter);
        }

        foreach (var (head, confidence) in store.FindSequencesByActivation(letters, "ExactSequenceSearch"))
        {
            if (confidence < 1 || !store.EnumerateSequenceElements(head).Select(element => element.VLU).SequenceEqual(letters))
                continue;
            foreach (Thought word in store.GetReferringThoughts(head, spelled))
                if (word.HasAncestor(wordRoot)) yield return word;
        }
    }

    /// <summary>
    /// Words spelled close to a spelling that has no exact match: the same letters jumbled, or a
    /// letter missing or extra (UKS ApproximateSequenceSearch). Best first, each with its confidence;
    /// an exact spelling has confidence 1.
    /// </summary>
    internal static List<(Thought word, float confidence)> FindWordsCloseTo(UKS.UKS store, string spelling)
    {
        var found = new List<(Thought word, float confidence)>();
        Thought spelled = store.Labeled("spelled");
        Thought wordRoot = store.Labeled("word");
        Thought options = store.Labeled("ApproximateSequenceSearch");
        if (spelled is null || wordRoot is null || options is null || string.IsNullOrEmpty(spelling)) return found;

        var letters = new List<Thought>(spelling.Length);
        foreach (char character in spelling)
            if (store.Labeled(UKS.UKS.GetCharacterLabel(character)) is Thought letter) letters.Add(letter);
        foreach (var (head, confidence) in store.FindSequencesByActivation(letters, options))
            foreach (Thought word in store.GetReferringThoughts(head, spelled))
                if (word.HasAncestor(wordRoot) && !found.Any(entry => ReferenceEquals(entry.word, word)))
                    found.Add((word, confidence));
        return found.OrderByDescending(entry => entry.confidence).ToList();
    }

    internal static Thought RememberSpelling(UKS.UKS store, string spelling, out bool isNewWord)
    {
        Thought word = FindWordsBySpelling(store, spelling).FirstOrDefault();
        if (word is not null)
        {
            isNewWord = false;
            return word;
        }

        // A saved vocabulary may already have a conventional label but no spelling.
        string label = "w:" + spelling;
        word = store.Labeled(label);
        isNewWord = word is null;
        word ??= store.GetOrAddThought(label, "word");
        if (word.GetTargetOfFirstLinkOfType("spelled") is SeqElement existing)
        {
            // Minimal stores can lack the search options. Check the stored spelling
            // before deciding the conventional label is a collision.
            if (store.EnumerateSequenceElements(existing).Select(element => element.VLU?.Label)
                .SequenceEqual(spelling.Select(UKS.UKS.GetCharacterLabel)))
                return word;
            // The label is taken by a word with a different spelling.
            word = store.GetOrAddThought("w:*", "word");
            isNewWord = true;
        }
        store.CreateSpellingSequence(spelling, word);
        return word;
    }
}

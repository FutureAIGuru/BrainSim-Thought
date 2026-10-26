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
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UKS;
using System.Linq;

namespace BrainSimulator.Modules;

public class ModuleWord : ModuleBase
{
    private const float ObservationWeightIncrease = 1f;
    private const float ObservationDecayFactor = 0.9999f;
    private const float ConsolidationWeight = 10f;
    private const int PlasticWordCapacity =50;

    //to put letters one by one into the mental Model
    DateTime lastLetterTime = DateTime.Now;
    readonly Queue<Thought> letterQueue = new();

    public ModuleWord()
    {
        Label = "Word";
    }
    public override void Fire()
    {
        // Only stream letters when both MentalModel and Attention are active
        if (!HasActiveMentalModelAndAttention(out var mm))
            return;

        if (letterQueue.Count > 0)
        {
            //add letters to the mental model one at a time
            if (lastLetterTime < DateTime.Now - TimeSpan.FromSeconds(1))
            {
                mm.RotateMentalModel(Angle.FromDegrees(-5f), Angle.FromDegrees(0));
                Thought center = mm.GetCell(0, 0);
                Thought firstChar = letterQueue.Peek();
                mm.BindThoughtToMentalModel(firstChar, center);
                lastLetterTime = DateTime.Now;
                letterQueue.Dequeue();
            }
        }
    }
    public override void Initialize()
    {
    }

    public override void SetUpAfterLoad()
    {
    }
    public override void UKSInitializedNotification()
    {
        theUKS.GetOrAddThought("character", "Abstract");
        EnsureWordRoot();
    }


    public string GetWordSuggestion(string word)
    {
        List<Thought> characters = new List<Thought>();
        foreach (char c in word.ToUpper())
        {
            characters.Add(theUKS.GetOrAddCharacter(c));
        }
        string retVal = word;

        // Use FindSequencesByActivation instead of HasSequence
        Thought searchOptions = theUKS.CreateSearchOptions(mustMatchFirst: true);
        var suggestions = theUKS.FindSequencesByActivation(characters, searchOptions);
        // Filter by linkType "spelled"
        suggestions = suggestions
            .Where(r => r.seqNode.LinksFrom.Any(l => l.LinkType?.Label == "spelled"))
            .ToList();

        if (suggestions.Count > 0)
        {
            var suggestionList = theUKS.FlattenSequence(suggestions[0].seqNode);
            string suggestionString = string.Join("", suggestionList.Select(
                x => x.Label[UKS.UKS.CharacterPrefix.Length..]));
            retVal = suggestionString;
        }
        return retVal;
    }

    public Thought AddWordSpelling(string word)
    {
        var theUKS = MainWindow.theUKS;
        word = word.Trim();

        if (string.IsNullOrWhiteSpace(word)) return null;

        bool streamOnly = HasActiveMentalModelAndAttention(out _);

        // When streaming mode is active, do not create words/sequences here
        if (streamOnly)
            return null;

        // Get or create the word thought. Words are observations, so their
        // persistent weights are plastic. Letters and spelling sequences are
        // structural and retain their fixed weights.
        Thought wordRoot = EnsureWordRoot();
        Thought existingWord = theUKS.Labeled("w:" + word);
        bool isNewWord = existingWord is null;
        Thought wordThought = existingWord ?? theUKS.GetOrAddThought("w:" + word, wordRoot);
        bool wordWasRetained = ObserveWord(wordThought, isNewWord);
        Thought retVal = wordThought;
        if (!wordWasRetained)
        {
            retVal = null;
            return retVal;
        }
        if (wordThought.LinksTo.FindFirst(x => x.LinkType.Label == "spelled") is not null)
        {
            return retVal; // Spelling already exists, no need to add again
        }
        theUKS.CreateSpellingSequence(word, wordThought);
        //wordThought.TimeToLive = TimeSpan.FromSeconds(10);

        return retVal;
    }

    /// <summary>
    /// Treats each word observation as one time step. All word evidence decays
    /// exponentially, the observed word gains one unit of evidence, and only
    /// the strongest ten unconsolidated words are retained.
    /// </summary>
    private bool ObserveWord(Thought observedWord, bool isNewWord)
    {
        Thought wordRoot = EnsureWordRoot();
        DecayWordWeights(wordRoot);

        if (isNewWord)
        {
            observedWord.isPlastic = true;
            observedWord.Weight = ObservationWeightIncrease;
        }
        else
        {
            observedWord.Weight += ObservationWeightIncrease;
        }

        observedWord.Fire();
        if (observedWord.isPlastic && observedWord.Weight >= ConsolidationWeight)
            observedWord.isPlastic = false;

        int plasticChildCount = wordRoot.Children.Count(child => child.isPlastic);
        bool observedWordWasPruned = false;
        while (plasticChildCount > PlasticWordCapacity)
        {
            Thought prunedChild = theUKS.PruneLowestScoringChild(wordRoot);
            if (prunedChild is null) break;

            if (prunedChild == observedWord)
                observedWordWasPruned = true;
            plasticChildCount--;
        }

        bool retVal = !observedWordWasPruned;
        return retVal;
    }

    private static void DecayWordWeights(Thought wordRoot)
    {
        foreach (Thought word in wordRoot.Children)
            word.Weight *= ObservationDecayFactor;
    }

    private Thought EnsureWordRoot()
    {
        theUKS.GetOrAddThought("LanguageElement", "Thought");
        Thought wordRoot = theUKS.GetOrAddThought("Word", "LanguageElement");
        wordRoot.RemoveParent("Thought");
        wordRoot.RemoveParent("Object");
        Thought retVal = wordRoot;
        return retVal;
    }

    public int LoadWordsFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return 0;

        int count = 0;
        char[] trimChars = { '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}' };
        try
        {
            string[] lines = File.ReadAllLines(filePath);
            Random.Shared.Shuffle(lines);
            foreach (string line in lines)
            {
                string text = line.Trim();
                if (string.IsNullOrWhiteSpace(text) || text.StartsWith("#")) continue;

                // Existing dictionary files use a tab after the word. Corpus
                // files may instead contain ordinary phrases to provide a
                // realistic stream of scattered word observations.
                string[] words = text.Contains('\t')
                    ? new[] { text.Split('\t')[0] }
                    : text.Split(new[] { ' ', '\t', '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries);

                foreach (string rawWord in words)
                {
                    string word = rawWord.Trim(trimChars).ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(word)) continue;
                    if (word.Any(ch => !char.IsLetterOrDigit(ch))) continue;

                    AddWordSpelling(word);
                    count++;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading words from file: {ex.Message}");
        }

        return count;
    }

    private bool HasActiveMentalModelAndAttention(out ModuleMentalModel mm)
    {
        var mods = MainWindow.theWindow?.activeModules;
        mm = mods?.OfType<ModuleMentalModel>().FirstOrDefault();
        var attn = mods?.OfType<ModuleAttention>().FirstOrDefault();
        return mm is not null && attn is not null;
    }

    public void EnqueueLetters(string added)
    {
        foreach (char c in added)
        {
            letterQueue.Enqueue(theUKS.GetOrAddCharacter(c));
        }
    }
    public void RebuildQueueFromCurrentText(string current)
    {
        letterQueue.Clear();
        foreach (char c in current.ToUpper())
        {
            letterQueue.Enqueue(theUKS.GetOrAddCharacter(c));
        }
    }
}

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
        theUKS.GetOrAddThought("EnglishWord", "Object");
        theUKS.GetOrAddThought("letter", "Object");
    }


    public string GetWordSuggestion(string word)
    {
        List<Thought> letters = new List<Thought>();
        foreach (char c in word.ToUpper())
        {
            string letterLabel = c.ToString();
            Thought letter = theUKS.GetOrAddThought("c:"+letterLabel, "letter");
            letters.Add(letter);
        }
        string retVal = word;
        var suggestions = theUKS.HasSequence(letters,"spelled",true);
        if (suggestions.Count > 0)
        {
            var suggestionList = theUKS.FlattenSequence(suggestions[0].seqNode);
            string suggestionString = string.Join("", suggestionList.Select(x => x.Label[2..]));
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

        // Get or create the word thought
        Thought wordThought = theUKS.GetOrAddThought("w:" + word, "EnglishWord");
        if (wordThought.LinksTo.FindFirst(x => x.LinkType.Label == "spelled") is not null)
        {
            wordThought.Fire();
            return wordThought; // Spelling already exists, no need to add again
        }
        // Create list of letter thoughts
        List<Thought> letters = new();
        foreach (char c in word.ToUpper())
        {
            Thought letter = theUKS.GetOrAddThought("c:" + c, "letter");
            letters.Add(letter);
        }

        // Get or create the "spelled" Linktype
        Thought spelledLinkType = theUKS.GetOrAddThought("spelled", "LinkType");

        // Add the sequence
        var t = theUKS.AddSequenceAndLink(wordThought, spelledLinkType, letters);
        wordThought.TimeToLive = TimeSpan.FromSeconds(10);

        return wordThought;
    }

    public int LoadWordsFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return 0;

        int count = 0;
        try
        {
            string[] lines = File.ReadAllLines(filePath);
            foreach (string line in lines)
            {
                string word = line.Trim();
                var splits = word.Split("\t");
                word = splits[0];
                if (!string.IsNullOrWhiteSpace(word))
                {
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
            letterQueue.Enqueue(theUKS.GetOrAddThought("c:" + char.ToUpper(c), "letter"));
        }
    }
    public void RebuildQueueFromCurrentText(string current)
    {
        letterQueue.Clear();
        foreach (char c in current.ToUpper())
        {
            letterQueue.Enqueue(theUKS.GetOrAddThought("c:" + c, "letter"));
        }
    }
}
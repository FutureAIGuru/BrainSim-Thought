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
    public ModuleWord()
    {
        Label = "Word";
    }

    public override void Fire()
    {
        // Called periodically by the module engine
    }
    public override void Initialize()
    {
    }

    public override void SetUpAfterLoad()
    {
    }

    
    public string GetWordSuggestion(string word)
    {
        List<Thought> letters = new List<Thought>();
        foreach (char c in word.ToUpper())
        {
            string letterLabel = c.ToString();
            Thought letter = theUKS.GetOrAddThought("c:"+letterLabel, "symbol");
            letters.Add(letter);
        }
        string retVal = word;
        var suggestions = theUKS.HasSequence(letters,"spelled",true,true);
        if (suggestions.Count > 0)
        {
            var suggestionList = theUKS.FlattenSequence(suggestions[0].seqNode);
            string suggestionString = string.Join("", suggestionList.Select(x => x.Label));
            retVal = suggestionString;
        }
        return retVal;
    }

    public static Thought AddWordSpelling(string word)
    {
        var theUKS = MainWindow.theUKS;
        if (string.IsNullOrWhiteSpace(word)) return null;

        word = word.Trim();
        theUKS.GetOrAddThought("EnglishWord", "Thought");
        theUKS.GetOrAddThought("symbol", "Object");

        // Get or create the word thought
        Thought wordThought = theUKS.GetOrAddThought("w:" + word, "EnglishWord");
        if (wordThought.LinksTo.FindFirst(x=>x.LinkType.Label == "spelled") is not null)
            return wordThought; // Spelling already exists, no need to add again

        // Create list of letter thoughts
        List<Thought> letters = new List<Thought>();
        foreach (char c in word.ToUpper())
        {
            string letterLabel = c.ToString();
            Thought letter = theUKS.GetOrAddThought("c:"+letterLabel, "symbol");
            letters.Add(letter);
        }

        // Get or create the "spelled" Linktype
        Thought spelledLinkType = theUKS.GetOrAddThought("spelled", "LinkType");

        // Add the sequence
        var t = theUKS.AddSequence(wordThought, spelledLinkType, letters);

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
            //Parallel.ForEach (lines, line=>
            {
                string word = line.Trim();
                var splits = word.Split("\t");
                word = splits[0];
                if (!string.IsNullOrWhiteSpace(word))
                {
                    AddWordSpelling(word);
                    count++;
                }
                //    });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading words from file: {ex.Message}");
        }

        return count;
    }
}
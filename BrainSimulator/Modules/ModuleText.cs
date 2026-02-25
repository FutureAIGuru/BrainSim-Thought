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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UKS;

namespace BrainSimulator.Modules;

public class ModuleText : ModuleBase
{

    // Fill this method in with code which will execute
    // once for each cycle of the engine
    public override void Fire()
    {
        Init();

        UpdateDialog();
    }

    // Fill this method in with code which will execute once
    // when the module is added, when "initialize" is selected from the context menu,
    // or when the engine restart button is pressed
    public override void Initialize()
    {
    }

    // called whenever the UKS performs an Initialize()
    public override void UKSInitializedNotification()
    {

    }

    public static string AddPhrase(string phrase)
    {
        var theUKS = MainWindow.theUKS;
        theUKS.GetOrAddThought("EnglishWord", "Thought");
        char[] trimChars = { '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}' };

        int attempted = 0;
        int ingested = 0;
        try
        {
            List<Thought> wordsInPhrase = new();
            foreach (string token in phrase.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string clean = token.Trim(trimChars).ToLowerInvariant();
                if (string.IsNullOrEmpty(clean)) continue;
                if (clean.Any(ch => !char.IsLetterOrDigit(ch))) continue;
                if (clean.Count(char.IsDigit) > 2) continue;
                attempted++;
                if (MainWindow.theWindow.GetModuleByLabel("ModuleWord0") is ModuleWord mw)
                {
                    var wordThought = mw.AddWordSpelling(clean);
                    wordsInPhrase.Add(wordThought);
                }
                ingested++;
            }

            theUKS.GetOrAddThought("Phrase");
            theUKS.GetOrAddThought("hasWords", "LinkType");
            Thought thePhrase = theUKS.GetOrAddThought("p*", "Phrase");
            if (wordsInPhrase.Count > 1)
            {
                theUKS.AddSequence(thePhrase, "hasWords", wordsInPhrase);
                //create bigrams
                CreateBigrams(wordsInPhrase);
            }
            return $"Processed {attempted} tokens; ingested {ingested} words.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }

    }

    private static void CreateBigrams(List<Thought> wordsInPhrase)
    {
        var theUKS = MainWindow.theUKS;
        theUKS.GetOrAddThought("bigram");
        theUKS.GetOrAddThought("followedBy", "LinkType");
        for (int i = 0; i < wordsInPhrase.Count - 1; i++)
        {
            Link bigram = wordsInPhrase[i].LinksTo.FindFirst(x => x.LinkType == "followedBy" && x.To == wordsInPhrase[i + 1]);
            if (bigram is null)
            {  //does not exist, create a new pair.
                {
                    bigram = theUKS.AddStatement(wordsInPhrase[i], "followedBy", wordsInPhrase[i + 1]);
                    bigram.Weight = .1f;
                    theUKS.AddStatement(bigram, "is-a", "bigram");
                }
                int MAX_FOLLOWEDBY = 10;
                if (wordsInPhrase[i].LinksTo.Count > MAX_FOLLOWEDBY)
                {
                    var outgoing = wordsInPhrase[1].LinksTo
                        .Where(l => l.LinkType == "followedBy")
                        .OrderByDescending(l => l.Weight /* **Recency(l.LastFiredTime*/)
                        .ToList();

                    if (outgoing.Count > MAX_FOLLOWEDBY)
                    {
                        var losers = outgoing.Skip(MAX_FOLLOWEDBY);
                        foreach (var l in losers)
                            l.From.RemoveLink(l);   // or hard-decay
                    }
                }
            }
            else
            {
                bigram.LastFiredTime = DateTime.Now;
                bigram.Weight = MathF.Min(1f, bigram.Weight + 0.05f * (1f - bigram.Weight));
            }
        }
    }

    public static string AddText(string text)
    {
        var theUKS = MainWindow.theUKS;
        theUKS.GetOrAddThought("EnglishWord", "Thought");
        if (string.IsNullOrWhiteSpace(text)) return "Null input";

        string[] sentences = Regex.Split(text, @"(?<=[\.!\?])\s+");
        foreach (string sentence in sentences)
        {
            string trimmed = sentence.Trim();
            if (trimmed.Length == 0) continue;

            AddPhrase(trimmed);
        }
        return "OK";
    }

    public int LoadTextFromFileOld(string filePath)
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
                    AddText(word);
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
    /// <summary>
    /// Incrementally loads up to <paramref name="phrasesPerCall"/> phrases from <paramref name="filePath"/>.
    /// Each line is treated as a phrase; tab-delimited extras are ignored. Returns phrases ingested this call.
    /// When it returns 0, the file is finished or unreadable.
    /// </summary>
    /// 
    // Incremental file-load state
    private StreamReader _phraseReader;
    private string _phraseReaderPath;
    public async Task<int> LoadTextFromFile(string filePath, int phrasesPerCall = 500)
    {
        if (phrasesPerCall <= 0) phrasesPerCall = 1;
        if (!File.Exists(filePath))
        {
            ResetPhraseReader();
            return 0;
        }

        try
        {
            // (Re)open reader if this is a new file or we haven't started yet
            if (_phraseReader == null || !string.Equals(_phraseReaderPath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                ResetPhraseReader();
                _phraseReader = new StreamReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read));
                _phraseReaderPath = filePath;
            }

            int count = 0;
            while (count < phrasesPerCall && _phraseReader != null)
            {
                string line = _phraseReader.ReadLine();
                if (line == null) break; // EOF

                string phrase = line.Trim();
                if (phrase.Length == 0) continue;

                int tabIdx = phrase.IndexOf('\t');
                if (tabIdx >= 0)
                    phrase = phrase[..tabIdx].Trim();

                if (phrase.Length == 0) continue;

                // Split into sentences so we cap by phrases, not by lines
                string[] sentences = Regex.Split(phrase, @"(?<=[\.!\?])\s+");
                foreach (string sentence in sentences)
                {
                    if (count >= phrasesPerCall) break;

                    string trimmed = sentence.Trim();
                    if (trimmed.Length == 0) continue;

                    AddPhrase(trimmed);
                    count++;
                }
            }

            if (_phraseReader != null && _phraseReader.EndOfStream)
            {
                ResetPhraseReader();
            }

            return count;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading phrases from file: {ex.Message}");
            ResetPhraseReader();
            return 0;
        }
    }

    /// <summary>
    /// Cancels any in-progress incremental load.
    /// </summary>
    public void CancelIncrementalLoad()
    {
        ResetPhraseReader();
    }

    private void ResetPhraseReader()
    {
        _phraseReader?.Dispose();
        _phraseReader = null;
        _phraseReaderPath = null;
    }


    public static int CreateTrigrams()
    {
        int retVal = 0;
        var theUKS = MainWindow.theUKS;

        // Ensure type + link types exist
        theUKS.GetOrAddThought("trigram");
        theUKS.GetOrAddThought("first", "LinkType");
        theUKS.GetOrAddThought("second", "LinkType");
        theUKS.GetOrAddThought("third", "LinkType");

        // Iterate all existing bigram link-thoughts
        foreach (Thought t in ((Thought)"bigram").Children)
        {
            if (t is not Link lnk) continue;
            // Expect: t is a followedBy link from A -> B
            Thought a = lnk.From;
            Thought b = lnk.To;

            if (a == null || b == null) continue;

            // Find B --followedBy--> C (second bigram)
            foreach (Link l in b.LinksTo.Where(x => x.LinkType.Label == "followedBy"))
            {
                Thought c = l.To;
                if (c == null) continue;

                // Optional: ensure A,B,C occurs somewhere in actual ingested sequences
                // This prevents creating trigrams that never appeared.
                var results = theUKS.HasSequence(new List<Thought> { a, b, c }, null);
                if (results.Count == 0)
                    continue;

                // Build a deterministic trigram key (prefer IDs if stable)
                string trigramKey = $"tg_{a.Label}_{b.Label}_{c.Label}";

                Thought tg = theUKS.Labeled(trigramKey);
                if (tg is null)
                {
                    tg = theUKS.GetOrAddThought(trigramKey, "trigram");

                    // Link to components (idempotent if AddStatement de-dupes)
                    theUKS.AddStatement(tg, "first", a);
                    theUKS.AddStatement(tg, "second", b);
                    theUKS.AddStatement(tg, "third", c);

                    // Set / reinforce trigram weight (use avg or min; min is more conservative)
                    float w = MathF.Min(t.Weight, l.Weight);     // conservative
                    //float w = 0.5f * (t.Weight + l.Weight);   // alternative

                    tg.Weight = MathF.Min(tg.Weight, 0.10f * w); // start small but proportional
                    retVal++;
                }
                else
                {
                    // reinforce existing trigram
                    tg.Weight = MathF.Min(1f, tg.Weight + 0.05f * (1f - tg.Weight));
                }

                tg.LastFiredTime = DateTime.Now; // swap for UKS ticks later if you add recency
            }
        }
        Thought trigrams = theUKS.Labeled("trigram");
        var topTrigrams = trigrams.Children.OrderByDescending(x => x.Weight).ToList();
        for (int i = 50; i < topTrigrams.Count; i++)
            topTrigrams[i].Delete();
        return retVal;
    }

}
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


using Microsoft.Msagl.GraphmapsWithMesh;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Automation;
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
            //thePhrase.TimeToLive = TimeSpan.FromSeconds(30); // adjust as needed
            if (wordsInPhrase.Count > 1)
            {
                theUKS.AddSequenceAndLink(thePhrase, "hasWords", wordsInPhrase);
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
        theUKS.GetOrAddThought("bigram", "LanguageElement");
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
        theUKS.GetOrAddThought("Word", "Thought");
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

                //TEMPORARY ignore questions
                if (phrase.ToLower().Contains("what")) continue;


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

    public static int FindUniversalPatterns(int minMatches = 4, int maxWildcards = 1, int maxLength = 8)
    {
        var theUKS = MainWindow.theUKS;
        Thought patternParent = theUKS.GetOrAddThought("UniversalPattern", "languageElement");
        theUKS.GetOrAddThought("example", "linkType");
        Thought wildcard = theUKS.Labeled("??");
        Thought searchOptions = theUKS.Labeled("TemplateSequenceSearch");
        int created = 0;

        List<(Thought owner, SeqElement seq, string source)> sequences = new();

        void AddSources(string parentLabel, string sequenceLink, string source)
        {
            Thought parent = theUKS.Labeled(parentLabel);
            if (parent is null) return;

            foreach (Thought owner in parent.Children)
            {
                if (owner.Parents[0] == patternParent) continue;
                if (owner.GetTargetOfFirstLinkOfType(sequenceLink) is SeqElement seq)
                    sequences.Add((owner, seq, source));
            }
        }

        //        AddSources("Word", "spelled", "word");
        AddSources("Phrase", "hasWords", "phrase");
        // AddSources("phrase", "hasWords", "phrase");

        foreach (var item in sequences.OrderBy(x => theUKS.GetSequenceLength(x.seq)))
        {
            List<Thought> elements = theUKS.FlattenSequence(item.seq);
            int length = elements.Count;
            if (length < 2 || length > maxLength) continue;

            foreach (var wildcardPositions in WildcardMasks(length, maxWildcards))
            {
                List<Thought> testPattern = new(elements);
                foreach (int pos in wildcardPositions)
                    testPattern[pos] = wildcard;
                string label = "up_" + item.source + "_" + string.Join("_", testPattern);
                if (theUKS.Labeled(label) is not null) continue; // already exists)

                int fixedCount = length - wildcardPositions.Count;
                if (fixedCount == 0) continue;

                var matches = theUKS.FindSequencesByActivation(testPattern, searchOptions);
                //remove matches which have wildcards--they have already been processed
                // we SHOULd make this into a search option in the future, but for now, this is a quick fix.
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    var fullSequence = theUKS.FlattenSequence(matches[i].seqNode);
                    if (fullSequence.FindFirst(x => x.HasAncestor(wildcard)) != null)
                    {
                        matches.RemoveAt(i);
                        continue;
                    }
                }
                int matchCount = matches.Count;
                if (matchCount < minMatches) continue;

                float score = matchCount * fixedCount / (float)length;
                Thought pattern = theUKS.GetOrAddThought(label, "UniversalPattern");

                if (pattern.GetTargetOfFirstLinkOfType("hasPattern") is null)
                    theUKS.AddSequenceAndLink(pattern, "hasPattern", testPattern);

                pattern.Weight = MathF.Max(pattern.Weight, score);
                foreach (var match in matches)
                {
                    if (match.seqNode is SeqElement matchSeq)
                    {
                        //add an example
                        theUKS.AddStatement(pattern, "example", match.seqNode);

                        //What is the wildcard word?  Make it a child of the pattern so we can find it later.
                        var words = theUKS.FlattenSequence(matchSeq);
                        Thought wildcardWord = words[wildcardPositions[0]];
                        wildcardWord.AddParent(pattern);
                    }
                }
                created++;
            }
        }

        return created;
    }

    public static int ComputeUniversalPatternOverlap(int minShared = 3, float minOverlap = 0.6f)
    {
        var theUKS = MainWindow.theUKS;
        Thought patternParent = theUKS.Labeled("UniversalPattern");
        if (patternParent is null) return 0;

        Thought overlapType = theUKS.GetOrAddThought("overlaps", "linkType");
        theUKS.AddStatement(overlapType, "hasProperty", "isCommutative");

        List<Thought> patterns = patternParent.Children.ToList();
        int overlapCount = 0;

        for (int i = 0; i < patterns.Count - 1; i++)
        {
            HashSet<Thought> membersA = patterns[i].Children.ToHashSet();
            if (membersA.Count == 0) continue;

            for (int j = i + 1; j < patterns.Count; j++)
            {
                HashSet<Thought> membersB = patterns[j].Children.ToHashSet();
                if (membersB.Count == 0) continue;

                int shared = membersA.Intersect(membersB).Count();
                if (shared < minShared) continue;

                float overlap = shared / (float)Math.Max(membersA.Count, membersB.Count);
                if (overlap < minOverlap) continue;

                Link overlapLink = theUKS.AddStatement(patterns[i], overlapType, patterns[j]);
                overlapLink.Weight = overlap;
                overlapCount++;
            }
        }
        return overlapCount;
    }

    private static IEnumerable<List<int>> WildcardMasks(int length, int maxWildcards)
    {
        for (int count = 1; count <= maxWildcards && count < length; count++)
        {
            foreach (var mask in WildcardMasks(length, count, 0, new List<int>()))
                yield return mask;
        }
    }

    private static IEnumerable<List<int>> WildcardMasks(int length, int count, int start, List<int> current)
    {
        if (current.Count == count)
        {
            yield return new List<int>(current);
            yield break;
        }

        for (int i = start; i < length; i++)
        {
            current.Add(i);
            foreach (var mask in WildcardMasks(length, count, i + 1, current))
                yield return mask;
            current.RemoveAt(current.Count - 1);
        }
    }

    public static int CreateUniversalPatternClasses(int minMembers = 7, int minPatterns = 2)
    {
        var theUKS = MainWindow.theUKS;
        Thought patternRoot = theUKS.Labeled("UniversalPattern");
        if (patternRoot is null) return 0;

        Thought classRoot = theUKS.GetOrAddThought("LearnedClass", "languageElement");
        List<HashSet<Thought>> memberSets = new();
        List<List<Thought>> patternGroups = new();

        foreach (Thought pattern in patternRoot.Children)
        {
            HashSet<Thought> members = pattern.Children.ToHashSet();
            if (members.Count < minMembers) continue;

            int groupIndex = memberSets.FindIndex(x => x.SetEquals(members));
            if (groupIndex < 0)
            {
                memberSets.Add(members);
                patternGroups.Add(new List<Thought> { pattern });
            }
            else
            {
                patternGroups[groupIndex].Add(pattern);
            }
        }

        int created = 0;

        for (int i = 0; i < patternGroups.Count; i++)
        {
            if (patternGroups[i].Count < minPatterns) continue;

            Thought learnedClass = theUKS.GetOrAddThought("class*", classRoot);
            learnedClass.Weight = memberSets[i].Count;

            foreach (Thought pattern in patternGroups[i])
                pattern.AddParent(learnedClass);

            created++;
        }

        return created;
    }


    public static int CreateTrigrams()
    {
        int retVal = FindUniversalPatterns();
        ComputeUniversalPatternOverlap();
        CreateUniversalPatternClasses();
        //        FindPatterns();
        return retVal;

        var theUKS = MainWindow.theUKS;
        //int retVal = 0;

        // Ensure type + link types exist
        theUKS.GetOrAddThought("trigram", "LanguageElement");
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
                if (a.Label == "w:the" && b.Label == "w:baby")
                { }

                // Optional: ensure A,B,C occurs somewhere in actual ingested sequences
                // This prevents creating trigrams that never appeared.
                var results = theUKS.HasSequence(new List<Thought> { a, b, c }, null);
                if (results.Count == 0)
                    continue;

                // Build a deterministic trigram key (prefer IDs if stable)
                string trigramKey = $"tg_{a.Label}_{b.Label}_{c.Label}";
                if (trigramKey == "tg_the_baby_bird")
                { }

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
                    tg.Weight = results.Count;
                    retVal++;
                }
                //else
                //{
                //    // reinforce existing trigram
                //    //tg.Weight = MathF.Min(1f, tg.Weight + 0.05f * (1f - tg.Weight));
                //    tg.Weight += 1; // simple increment; could also use a weighted average of component weights
                //}

                tg.LastFiredTime = DateTime.Now; // swap for UKS ticks later if you add recency
            }
        }
        Thought trigrams = theUKS.Labeled("trigram");
        var topTrigrams = trigrams.Children.OrderByDescending(x => x.Weight).ToList();
        for (int i = 50; i < topTrigrams.Count; i++)
            topTrigrams[i].Delete();
        return retVal;
    }
    public static void FindPatterns()
    {
        var theUKS = MainWindow.theUKS;
        theUKS.GetOrAddThought("EndsWithS", "languageElement");
        var resultsX = theUKS.FindSequencesByActivation(new List<Thought> { "c:S" }, "mustMatchLast");
        foreach (var result in resultsX)
        {
            var searchPattern = theUKS.FlattenSequence(result.seqNode);
            searchPattern.RemoveAt(searchPattern.Count - 1);
            var resultsSingular = theUKS.FindSequencesByActivation(searchPattern, "SearchExactMatch");
            if (resultsSingular.Count > 0)
                theUKS.GetReferrer(resultsSingular[0].seqNode, "spelled")?.AddParent("EndsWithS");
        }

        theUKS.GetOrAddThought("Pattern", "languageElement");
        int length = 5;
        foreach (Thought t in ((Thought)"phrase").Children)
        {
            SeqElement thePhrase = t.GetTargetOfFirstLinkOfType("hasWords") as SeqElement;
            Thought searchOptions = theUKS.Labeled("TemplateSequenceSearch");
            if (theUKS.GetSequenceLength(thePhrase) == length)
            {
                var words = theUKS.FlattenSequence(thePhrase);
                var bestCount = 0;
                List<Thought> bestPattern = new();
                for (int i = 0; i < length; i++)
                {
                    List<Thought> testPattern = new();

                    testPattern.AddRange(words);
                    testPattern[i] = theUKS.Labeled("??");
                    var results = theUKS.FindSequencesByActivation(testPattern, searchOptions);
                    if (results.Count > bestCount && results.Count > bestCount)
                    {
                        bestCount = results.Count;
                        bestPattern = testPattern;
                    }
                }
                if (bestCount > 2)
                {
                    string patternLabel = "tp_" + string.Join("_", bestPattern.Select(w => w.Label));
                    Thought patternThought = theUKS.GetOrAddThought(patternLabel, "Pattern");
                    theUKS.AddSequenceAndLink(patternThought, "hasWords", bestPattern);
                    patternThought.Weight = bestCount;
                }
            }
        }
    }

}

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

    public static string AddPhrase(string phrase, bool applyExistingTemplates = false)
    {
        var theUKS = MainWindow.theUKS;
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
                Thought wordThought = null;
                if (MainWindow.theWindow?.GetModuleByLabel("ModuleWord0") is ModuleWord mw)
                    wordThought = mw.AddWordSpelling(clean);

                // ModuleWord returns null while its attention/mental-model
                // streaming path is active. ModuleText still needs a stable
                // word value now so the observed phrase can be learned.
                wordThought ??= theUKS.Labeled("w:" + clean) ??
                    theUKS.GetOrAddThought("w:" + clean, "Word");
                if (wordThought is not null)
                {
                    wordsInPhrase.Add(wordThought);
                    ingested++;
                }
            }

            theUKS.GetOrAddThought("Phrase");
            theUKS.GetOrAddThought("hasWords", "LinkType");
            Thought thePhrase = theUKS.GetOrAddThought("p*", "Phrase");
            //thePhrase.TimeToLive = TimeSpan.FromSeconds(30); // adjust as needed
            if (wordsInPhrase.Count > 1)
            {
                theUKS.AddSequenceAndLink(thePhrase, "hasWords", wordsInPhrase);
                if (applyExistingTemplates)
                {
                    Thought theTemplate = ApplyExistingTemplatesToPhrase(thePhrase);
                }
            }
            return $"Processed {attempted} tokens; ingested {ingested} words.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }

    }

    public static string AddText(string text, bool applyExistingTemplates = true)
    {
        var theUKS = MainWindow.theUKS;
        theUKS.GetOrAddThought("Word", "Thought");
        if (string.IsNullOrWhiteSpace(text)) return "Null input";

        string[] sentences = Regex.Split(text, @"(?<=[\.!\?])\s+");
        foreach (string sentence in sentences)
        {
            string trimmed = sentence.Trim();
            if (trimmed.Length == 0) continue;

            AddPhrase(trimmed, applyExistingTemplates);
        }
        return "OK";
    }

    /// <summary>
    /// Applies already learned templates to one manually entered phrase. An
    /// unclassified word may occupy a class wildcard; a successful match then
    /// adds that word to the wildcard's learned class.
    /// </summary>
    /// <returns>The selected learned template, or null when none matches.</returns>
    public static Thought ApplyExistingTemplatesToPhrase(Thought phrase)
    {
        var theUKS = MainWindow.theUKS;
        Thought templateRoot = theUKS.Labeled("LearnedTemplate");
        Thought learnedClassRoot = theUKS.Labeled("LearnedClass");
        Thought searchOptions = theUKS.Labeled("TemplateLearningSearch");
        if (phrase is null || templateRoot is null || learnedClassRoot is null || searchOptions is null)
            return null;

        SequenceView phraseSequence = theUKS.GetSequenceViews(phrase)
            .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
        if (phraseSequence is null) return null;

        Dictionary<Thought, float> matchingTemplates = new();
        foreach (var match in theUKS.FindSequencesByActivation(
            phraseSequence.Elements.ToList(), searchOptions))
        {
            foreach (Link ownerLink in match.seqNode.FRST.LinksFrom.Where(link =>
                link.LinkType?.Label == "hasWords" && link.From is not null &&
                templateRoot.Children.Contains(link.From)))
            {
                if (!matchingTemplates.TryGetValue(ownerLink.From, out float previousConfidence) ||
                    match.confidence > previousConfidence)
                    matchingTemplates[ownerLink.From] = match.confidence;
            }
        }

        // Prefer a template whose classes already explain the largest number
        // of phrase elements. Fixed elements and accumulated evidence break
        // ties. This keeps a known noun/noun frame ahead of a noun/adjective
        // frame once those classes have begun to separate.
        Thought foundTemplate = matchingTemplates
            .Select(candidate =>
            {
                SequenceView sequence = theUKS.GetSequenceViews(candidate.Key)
                    .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
                int classifiedMatches = sequence is null ? 0 : sequence.Elements
                    .Select((element, position) => (element, position))
                    .Count(item => item.position < phraseSequence.Elements.Count &&
                        item.element.HasAncestor("Wildcard") &&
                        item.element.Parents.Any(parent => parent != theUKS.Labeled("Wildcard") &&
                            phraseSequence.Elements[item.position].HasAncestor(parent)));
                int fixedElements = sequence?.Elements.Count(element =>
                    !element.HasAncestor("Wildcard")) ?? 0;
                int evidence = candidate.Key.LinksTo.Count(link => link.LinkType?.Label == "evidence");
                return (template: candidate.Key, candidate.Value, classifiedMatches, fixedElements, evidence);
            })
            .OrderByDescending(candidate => candidate.classifiedMatches)
            .ThenByDescending(candidate => candidate.fixedElements)
            .ThenByDescending(candidate => candidate.Value)
            .ThenByDescending(candidate => candidate.evidence)
            .ThenBy(candidate => candidate.template.Label, StringComparer.Ordinal)
            .Select(candidate => candidate.template)
            .FirstOrDefault();
        if (foundTemplate is null) return null;

        Thought evidenceType = theUKS.GetOrAddThought("evidence", "LinkType");
        SequenceView templateSequence = theUKS.GetSequenceViews(foundTemplate)
            .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
        if (templateSequence is null ||
            templateSequence.Elements.Count != phraseSequence.Elements.Count)
            return null;

        for (int position = 0; position < templateSequence.Elements.Count; position++)
        {
            Thought wildcard = templateSequence.Elements[position];
            if (!wildcard.HasAncestor("Wildcard") || !wildcard.HasProperty("isWildcard"))
                continue;
            Thought valueClass = wildcard.Parents.FirstOrDefault(
                parent => learnedClassRoot.Children.Contains(parent));
            if (valueClass is not null)
                phraseSequence.Elements[position].AddParent(valueClass);
        }

        theUKS.AddStatement(foundTemplate, evidenceType, phrase);
        foundTemplate.Weight = Math.Max(foundTemplate.Weight,
            foundTemplate.LinksTo.Count(link => link.LinkType == evidenceType));
        return foundTemplate;
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
                    AddText(word, applyExistingTemplates: false);
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
    public int LoadTextFromFile(string filePath, int phrasesPerCall = 500)
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

                    string result = AddPhrase(trimmed);
                    if (result.StartsWith("Error:", StringComparison.Ordinal))
                        throw new InvalidOperationException(result);
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

    private sealed class PatternBucket
    {
        public List<Thought> Pattern { get; init; } = new();
        public List<int> WildcardPositions { get; init; } = new();
        public HashSet<SeqElement> Examples { get; } = new();
        public HashSet<Thought> Fillers { get; } = new();
        public string Source { get; init; } = "";
    }

    public static int FindUniversalPatterns(int minMatches = 4, int maxWildcards = 1, int maxLength = 8)
    {
        var theUKS = MainWindow.theUKS;
        Thought patternParent = theUKS.GetOrAddThought("UniversalPattern", "languageElement");
        theUKS.GetOrAddThought("example", "linkType");
        theUKS.GetOrAddThought("hasPattern", "linkType");
        Thought wildcard = theUKS.Labeled("??") ?? theUKS.CreateWildcard("??", new List<Thought> { "Thought" });

        List<(Thought owner, SeqElement seq, string source)> sequences = new();

        void AddSources(string parentLabel, string sequenceLink, string source)
        {
            Thought parent = theUKS.Labeled(parentLabel);
            if (parent is null) return;

            foreach (Thought owner in parent.Children)
            {
                if (owner.GetTargetOfFirstLinkOfType(sequenceLink) is SeqElement seq)
                    sequences.Add((owner, seq, source));
            }
        }

        //        AddSources("Word", "spelled", "word");
        AddSources("Phrase", "hasWords", "phrase");
        // AddSources("phrase", "hasWords", "phrase");

        // Build every surface pattern in one pass. A signature bucket replaces the
        // previous global sequence search for every phrase and wildcard position.
        Dictionary<string, PatternBucket> buckets = new(StringComparer.OrdinalIgnoreCase);
        foreach (var item in sequences)
        {
            List<Thought> elements = theUKS.FlattenSequence(item.seq);
            int length = elements.Count;
            if (length < 2 || length > maxLength) continue;
            if (elements.Any(x => x.HasAncestor("Wildcard"))) continue;

            foreach (var wildcardPositions in WildcardMasks(length, maxWildcards))
            {
                List<Thought> testPattern = new(elements);
                foreach (int pos in wildcardPositions)
                    testPattern[pos] = wildcard;

                int fixedCount = length - wildcardPositions.Count;
                if (fixedCount == 0) continue;

                string signature = item.source + "\u001f" +
                    string.Join("\u001f", testPattern.Select(x => x.Label));
                if (!buckets.TryGetValue(signature, out PatternBucket bucket))
                {
                    bucket = new PatternBucket
                    {
                        Pattern = testPattern,
                        WildcardPositions = new List<int>(wildcardPositions),
                        Source = item.source,
                    };
                    buckets.Add(signature, bucket);
                }

                bucket.Examples.Add(item.seq.FRST);
                // Initial class discovery intentionally uses the first wildcard.
                // Multi-wildcard patterns are retained as templates, but their slot
                // classes must be learned independently before they can be combined.
                bucket.Fillers.Add(elements[wildcardPositions[0]]);
            }
        }

        int created = 0;
        foreach (PatternBucket bucket in buckets.Values.Where(x => x.Examples.Count >= minMatches))
        {
            int fixedCount = bucket.Pattern.Count - bucket.WildcardPositions.Count;
            float score = bucket.Examples.Count * fixedCount / (float)bucket.Pattern.Count;
            string label = "up_" + bucket.Source + "_" +
                string.Join("_", bucket.Pattern.Select(x => x.Label));
            Thought pattern = theUKS.Labeled(label);
            if (pattern is null)
            {
                pattern = theUKS.GetOrAddThought(label, patternParent);
                created++;
            }

            if (pattern.GetTargetOfFirstLinkOfType("hasPattern") is null)
                theUKS.AddSequenceAndLink(pattern, "hasPattern", bucket.Pattern);

            pattern.Weight = MathF.Max(pattern.Weight, score);
            foreach (SeqElement example in bucket.Examples)
                theUKS.AddStatement(pattern, "example", example);

            foreach (Thought filler in bucket.Fillers)
                filler.AddParent(pattern);
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

    public static int CreateUniversalPatternClasses(int minMembers = 7, int minPatterns = 2, float minOverlap = 0.6f)
    {
        var theUKS = MainWindow.theUKS;
        Thought patternRoot = theUKS.Labeled("UniversalPattern");
        if (patternRoot is null) return 0;

        Thought classRoot = theUKS.GetOrAddThought("LearnedClass", "languageElement");
        Thought evidenceType = theUKS.GetOrAddThought("evidence", "linkType");
        List<Thought> patterns = patternRoot.Children.ToList();
        Dictionary<Thought, HashSet<Thought>> membersByPattern = patterns.ToDictionary(
            pattern => pattern,
            pattern => pattern.Children.Where(x => !x.HasAncestor("Wildcard")).ToHashSet());

        // Each anchor grows a dense group. A candidate joins only if the shared
        // core remains large enough, preventing weak overlap chains from merging
        // unrelated word classes.
        List<(HashSet<Thought> members, List<Thought> patterns)> candidates = new();
        foreach (Thought anchor in patterns)
        {
            HashSet<Thought> anchorMembers = membersByPattern[anchor];
            if (anchorMembers.Count < minMembers) continue;

            var neighbors = patterns
                .Where(x => x != anchor && membersByPattern[x].Count >= minMembers)
                .Select(x => new
                {
                    Pattern = x,
                    Overlap = anchorMembers.Intersect(membersByPattern[x]).Count() /
                        (float)Math.Max(anchorMembers.Count, membersByPattern[x].Count)
                })
                .Where(x => x.Overlap >= minOverlap)
                .OrderByDescending(x => x.Overlap)
                .ToList();

            HashSet<Thought> core = new(anchorMembers);
            List<Thought> group = new() { anchor };
            foreach (var neighbor in neighbors)
            {
                HashSet<Thought> reducedCore = core.Intersect(membersByPattern[neighbor.Pattern]).ToHashSet();
                if (reducedCore.Count < minMembers) continue;
                core = reducedCore;
                group.Add(neighbor.Pattern);
            }

            if (group.Count < minPatterns) continue;
            if (candidates.Any(x => x.members.SetEquals(core))) continue;
            candidates.Add((core, group));
        }

        int created = 0;
        foreach (var candidate in candidates.OrderByDescending(x => x.members.Count))
        {
            Thought learnedClass = classRoot.Children.FirstOrDefault(existing =>
            {
                HashSet<Thought> existingMembers = existing.Children
                    .Where(x => !x.HasAncestor("Wildcard"))
                    .ToHashSet();
                return existingMembers.SetEquals(candidate.members);
            });

            if (learnedClass is null)
            {
                learnedClass = theUKS.GetOrAddThought("class*", classRoot);
                created++;
            }

            learnedClass.Weight = MathF.Max(learnedClass.Weight, candidate.members.Count);
            foreach (Thought member in candidate.members)
                member.AddParent(learnedClass);
            foreach (Thought pattern in candidate.patterns)
                theUKS.AddStatement(learnedClass, evidenceType, pattern);

            theUKS.CreateWildcard("??" + learnedClass.Label, new List<Thought> { learnedClass });
        }

        return created;
    }

    private sealed class SpelledClassMember
    {
        public Thought Word { get; init; }
        public string Spelling { get; init; } = "";
    }

    private sealed class SpellingChange
    {
        public string Location { get; init; } = "";
        public string Removed { get; init; } = "";
        public string Inserted { get; init; } = "";
        public int EditDistance { get; init; }
        public string Key => Location + "\u001f" + Removed + "\u001f" + Inserted;
    }

    private sealed class FollowerEvidence
    {
        public Thought Word { get; init; }
        public HashSet<Thought> Templates { get; } = new();
        public HashSet<Thought> SubjectClasses { get; } = new();
    }

    /// <summary>
    /// Exhaustively compares the spellings of members of every ordered pair of
    /// learned classes. A word pair proposes a contiguous spelling change; a rule
    /// is retained only when the same change maps at least <paramref name="minMatches"/>
    /// distinct source words to distinct target words in the other class.
    /// Identical spellings do not support a change rule.
    /// </summary>
    public static int FindClassSpellingRules(int minMatches = 4, int maxEditDistance = 0)
    {
        var theUKS = MainWindow.theUKS;
        Thought classRoot = theUKS.Labeled("LearnedClass");
        if (classRoot is null) return 0;

        Thought ruleRoot = theUKS.GetOrAddThought("SpellingRule", "languageElement");
        Thought sourcePatternType = theUKS.GetOrAddThought("hasSourcePattern", "linkType");
        Thought targetPatternType = theUKS.GetOrAddThought("hasTargetPattern", "linkType");
        Thought changesAtType = theUKS.GetOrAddThought("changesAt", "linkType");
        Thought evidenceType = theUKS.GetOrAddThought("evidence", "linkType");
        Thought appliesToType = theUKS.GetOrAddThought("appliesTo", "linkType");
        Thought usesRuleType = theUKS.GetOrAddThought("usesRule", "linkType");
        Thought changesToType = theUKS.GetOrAddThought("spellingChangesTo", "linkType");
        Thought positionRoot = theUKS.GetOrAddThought("SpellingPosition", "languageElement");

        Thought prefixWildcard = theUKS.Labeled("??spellingPrefix") ??
            theUKS.CreateWildcard("??spellingPrefix", new List<Thought> { "letter" });
        Thought suffixWildcard = theUKS.Labeled("??spellingSuffix") ??
            theUKS.CreateWildcard("??spellingSuffix", new List<Thought> { "letter" });

        List<(Thought learnedClass, List<SpelledClassMember> members)> classes = classRoot.Children
            .Select(learnedClass => (learnedClass, members: learnedClass.Children
                .Where(word => !word.HasAncestor("Wildcard"))
                .Select(word => new SpelledClassMember
                {
                    Word = word,
                    Spelling = GetSpelling(theUKS, word),
                })
                .Where(x => x.Spelling.Length > 0)
                .GroupBy(x => x.Spelling, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList()))
            .Where(x => x.members.Count >= minMatches)
            .ToList();

        int created = 0;
        foreach (var sourceClass in classes)
        {
            foreach (var targetClass in classes)
            {
                if (sourceClass.learnedClass == targetClass.learnedClass) continue;

                Dictionary<string, SpellingChange> candidates = new(StringComparer.OrdinalIgnoreCase);
                foreach (SpelledClassMember source in sourceClass.members)
                {
                    foreach (SpelledClassMember target in targetClass.members)
                    {
                        if (string.Equals(source.Spelling, target.Spelling,
                            StringComparison.OrdinalIgnoreCase))
                            continue;

                        SpellingChange change = DescribeSpellingChange(source.Spelling, target.Spelling);
                        if (change is null || change.Location == "whole") continue;
                        if (maxEditDistance > 0 && change.EditDistance > maxEditDistance) continue;
                        candidates.TryAdd(change.Key, change);
                    }
                }

                foreach (SpellingChange change in candidates.Values.OrderBy(x => x.EditDistance))
                {
                    List<(SpelledClassMember source, SpelledClassMember target)> supportingPairs =
                        FindSupportingPairs(sourceClass.members, targetClass.members, change);
                    if (supportingPairs.Count < minMatches) continue;

                    // Rule identity is the spelling transformation itself. Every
                    // class pair exhibiting it points to this one shared rule.
                    string ruleLabel = "sr_" + change.Location + "_" +
                        SafeLabel(change.Removed.Length == 0 ? "empty" : change.Removed) + "_to_" +
                        SafeLabel(change.Inserted.Length == 0 ? "empty" : change.Inserted);
                    Thought rule = theUKS.Labeled(ruleLabel);
                    if (rule is null)
                    {
                        rule = theUKS.GetOrAddThought(ruleLabel, ruleRoot);
                        created++;
                    }

                    theUKS.AddStatement(rule, changesAtType,
                        theUKS.GetOrAddThought("spelling" + change.Location, positionRoot));

                    if (rule.GetTargetOfFirstLinkOfType(sourcePatternType) is null)
                        theUKS.AddSequenceAndLink(rule, sourcePatternType,
                            BuildSpellingPattern(theUKS, change, source: true,
                                prefixWildcard, suffixWildcard));
                    if (rule.GetTargetOfFirstLinkOfType(targetPatternType) is null)
                        theUKS.AddSequenceAndLink(rule, targetPatternType,
                            BuildSpellingPattern(theUKS, change, source: false,
                                prefixWildcard, suffixWildcard));

                    Link classPairLink = theUKS.AddStatement(
                        sourceClass.learnedClass, changesToType, targetClass.learnedClass);
                    classPairLink.Weight = supportingPairs.Count /
                        (float)Math.Max(sourceClass.members.Count, targetClass.members.Count);
                    classPairLink.AddLink(usesRuleType, rule);
                    rule.AddLink(appliesToType, classPairLink);

                    foreach (var pair in supportingPairs)
                    {
                        Link pairLink = theUKS.AddStatement(pair.source.Word, changesToType, pair.target.Word);
                        classPairLink.AddLink(evidenceType, pairLink);
                    }

                    // A consolidated rule's weight is its total independent word-pair evidence.
                    rule.Weight = rule.LinksTo
                        .Where(x => x.LinkType == appliesToType)
                        .Select(x => x.To)
                        .OfType<Link>()
                        .SelectMany(application => application.LinksTo
                            .Where(x => x.LinkType == evidenceType)
                            .Select(x => x.To))
                        .Distinct()
                        .Count();
                }
            }
        }

        return created;
    }

    /// <summary>
    /// Applies a spelling rule whose source and target patterns preserve one prefix
    /// wildcard and optionally change literal letters at the end of the spelling.
    /// Returns a newly created or existing w: word Thought with its spelled sequence,
    /// or null when the rule does not match this spelling or is not yet supported.
    /// </summary>
    public static Thought ApplySpellingRule(Thought rule, Thought word)
    {
        var theUKS = MainWindow.theUKS;
        if (rule is null || word is null) return null;
        if (rule.GetTargetOfFirstLinkOfType("hasSourcePattern") is not SeqElement sourceSequence)
            return null;
        if (rule.GetTargetOfFirstLinkOfType("hasTargetPattern") is not SeqElement targetSequence)
            return null;

        List<Thought> sourcePattern = theUKS.FlattenSequence(sourceSequence);
        List<Thought> targetPattern = theUKS.FlattenSequence(targetSequence);
        if (!TryReadEndSpellingPattern(sourcePattern, out Thought sourceWildcard, out string sourceEnding))
            return null;
        if (!TryReadEndSpellingPattern(targetPattern, out Thought targetWildcard, out string targetEnding))
            return null;
        if (sourceWildcard != targetWildcard) return null;

        string sourceSpelling = GetSpelling(theUKS, word);
        if (sourceSpelling.Length == 0 ||
            !sourceSpelling.EndsWith(sourceEnding, StringComparison.OrdinalIgnoreCase))
            return null;

        string preserved = sourceSpelling[..(sourceSpelling.Length - sourceEnding.Length)];
        if (preserved.Length == 0) return null; // the spelling wildcard is one-or-more here

        string resultSpelling = preserved + targetEnding;
        Thought result = theUKS.GetOrAddThought("w:" + resultSpelling.ToLowerInvariant(), "Word");
        if (result.GetTargetOfFirstLinkOfType("spelled") is null)
        {
            List<Thought> letters = resultSpelling
                .Select(letter => theUKS.GetOrAddThought("c:" + char.ToUpperInvariant(letter), "letter"))
                .ToList();
            if (letters.Count < 2) return null;
            theUKS.AddSequenceAndLink(result,
                theUKS.GetOrAddThought("spelled", "linkType"), letters);
        }
        return result;
    }

    /// <summary>
    /// Consolidates the source and target classes participating in the same
    /// spelling rule beneath two anonymous parent classes. The parent-to-parent
    /// relationship retains the original class applications as evidence.
    /// </summary>
    public static int CreateClassFamiliesFromSpellingRules(int minChildClasses = 2)
    {
        var theUKS = MainWindow.theUKS;
        Thought classRoot = theUKS.Labeled("LearnedClass");
        Thought ruleRoot = theUKS.Labeled("SpellingRule");
        if (classRoot is null || ruleRoot is null) return 0;

        Thought appliesToType = theUKS.GetOrAddThought("appliesTo", "linkType");
        Thought usesRuleType = theUKS.GetOrAddThought("usesRule", "linkType");
        Thought evidenceType = theUKS.GetOrAddThought("evidence", "linkType");
        Thought changesToType = theUKS.GetOrAddThought("spellingChangesTo", "linkType");
        Thought classFamilyProperty = theUKS.GetOrAddThought("isClassFamily", "Property");

        int created = 0;
        foreach (Thought rule in ruleRoot.Children)
        {
            List<Link> applications = rule.LinksTo
                .Where(x => x.LinkType == appliesToType)
                .Select(x => x.To)
                .OfType<Link>()
                .Where(x => !HasDirectProperty(x.From, classFamilyProperty) &&
                    x.To is not null && !HasDirectProperty(x.To, classFamilyProperty))
                .ToList();
            HashSet<Thought> sourceClasses = applications.Select(x => x.From).ToHashSet();
            HashSet<Thought> targetClasses = applications.Select(x => x.To).ToHashSet();
            if (sourceClasses.Count < minChildClasses || targetClasses.Count < minChildClasses)
                continue;

            Thought sourceFamily = GetOrCreateClassFamily(
                theUKS, classRoot, sourceClasses, classFamilyProperty, ref created);
            Thought targetFamily = GetOrCreateClassFamily(
                theUKS, classRoot, targetClasses, classFamilyProperty, ref created);
            Link familyApplication = theUKS.AddStatement(
                sourceFamily, changesToType, targetFamily);
            familyApplication.Weight = applications.Count;
            familyApplication.AddLink(usesRuleType, rule);
            foreach (Link application in applications)
                familyApplication.AddLink(evidenceType, application);
            rule.AddLink(appliesToType, familyApplication);
        }
        return created;
    }

    private static Thought GetOrCreateClassFamily(
        UKS.UKS theUKS,
        Thought classRoot,
        HashSet<Thought> childClasses,
        Thought classFamilyProperty,
        ref int created)
    {
        Thought family = classRoot.Children.FirstOrDefault(existing =>
            HasDirectProperty(existing, classFamilyProperty) &&
            existing.Children.Where(x => !x.HasAncestor("Wildcard"))
                .ToHashSet().SetEquals(childClasses));
        if (family is null)
        {
            family = theUKS.GetOrAddThought("class*", classRoot);
            family.AddProperty("isClassFamily");
            foreach (Thought childClass in childClasses)
            {
                childClass.AddParent(family);
                childClass.RemoveParent("LearnedClass");
            }
            created++;
        }

        if (theUKS.Labeled("??" + family.Label) is null)
            theUKS.CreateWildcard("??" + family.Label, new List<Thought> { family });
        family.Weight = childClasses.Count;
        return family;
    }

    private static bool HasDirectProperty(Thought thought, Thought property)
    {
        return thought.LinksTo.Any(x =>
            x.LinkType?.Label == "hasProperty" && x.To == property);
    }

    /// <summary>
    /// Creates anonymous word classes from spelled words which repeatedly occur
    /// immediately after class wildcards on the same side of a learned spelling
    /// rule. No grammatical meaning is assigned to the resulting classes.
    /// </summary>
    public static int CreateFollowerClassesFromSpellingRules(
        int minMembers = 2,
        int minTemplatesPerMember = 2,
        int minSubjectClassesPerMember = 1)
    {
        var theUKS = MainWindow.theUKS;
        Thought classRoot = theUKS.Labeled("LearnedClass");
        Thought ruleRoot = theUKS.Labeled("SpellingRule");
        Thought templateRoot = theUKS.Labeled("LearnedTemplate");
        if (classRoot is null || ruleRoot is null || templateRoot is null) return 0;

        Thought appliesToType = theUKS.GetOrAddThought("appliesTo", "linkType");
        Thought templateEvidenceType = theUKS.GetOrAddThought("templateEvidence", "linkType");
        Thought contextChangesToType = theUKS.GetOrAddThought("contextChangesTo", "linkType");
        Thought conditionedByType = theUKS.GetOrAddThought("conditionedBy", "linkType");
        Thought classFamilyProperty = theUKS.GetOrAddThought("isClassFamily", "Property");
        List<(Thought template, List<Thought> elements)> templates = templateRoot.Children
            .Select(template => (template,
                elements: template.GetTargetOfFirstLinkOfType("hasPattern") is SeqElement sequence
                    ? theUKS.FlattenSequence(sequence)
                    : new List<Thought>()))
            .Where(x => x.elements.Count >= 2)
            .ToList();

        int created = 0;
        foreach (Thought rule in ruleRoot.Children)
        {
            List<Link> applications = rule.LinksTo
                .Where(x => x.LinkType == appliesToType)
                .Select(x => x.To)
                .OfType<Link>()
                .Where(x => !HasDirectProperty(x.From, classFamilyProperty) &&
                    x.To is not null && !HasDirectProperty(x.To, classFamilyProperty))
                .ToList();
            if (applications.Count == 0) continue;

            HashSet<Thought> sourceClasses = applications.Select(x => x.From).ToHashSet();
            HashSet<Thought> targetClasses = applications.Select(x => x.To).ToHashSet();
            Dictionary<Thought, FollowerEvidence> sourceEvidence =
                GatherFollowerEvidence(templates, sourceClasses);
            Dictionary<Thought, FollowerEvidence> targetEvidence =
                GatherFollowerEvidence(templates, targetClasses);

            Thought sourceFollowerClass = CreateFollowerClass(
                theUKS, classRoot, sourceEvidence, minMembers,
                minTemplatesPerMember, minSubjectClassesPerMember, templateEvidenceType, ref created);
            Thought targetFollowerClass = CreateFollowerClass(
                theUKS, classRoot, targetEvidence, minMembers,
                minTemplatesPerMember, minSubjectClassesPerMember, templateEvidenceType, ref created);
            if (sourceFollowerClass is null || targetFollowerClass is null) continue;

            Link classAssociation = theUKS.AddStatement(
                sourceFollowerClass, contextChangesToType, targetFollowerClass);
            classAssociation.AddLink(conditionedByType, rule);
        }
        return created;
    }

    /// <summary>
    /// Creates paired two-slot grammar templates from subject-class spelling
    /// applications and the anonymous follower classes conditioned by the same
    /// spelling rule. Longer learned templates are retained as evidence.
    /// </summary>
    public static int CreateClassPairTemplates(int minEvidenceTemplates = 1)
    {
        var theUKS = MainWindow.theUKS;
        Thought classRoot = theUKS.Labeled("LearnedClass");
        Thought ruleRoot = theUKS.Labeled("SpellingRule");
        Thought learnedTemplateRoot = theUKS.Labeled("LearnedTemplate");
        if (classRoot is null || ruleRoot is null || learnedTemplateRoot is null) return 0;

        Thought grammarTemplateRoot = theUKS.GetOrAddThought("GrammarTemplate", "languageElement");
        Thought appliesToType = theUKS.GetOrAddThought("appliesTo", "linkType");
        Thought contextChangesToType = theUKS.GetOrAddThought("contextChangesTo", "linkType");
        Thought conditionedByType = theUKS.GetOrAddThought("conditionedBy", "linkType");
        Thought correspondsToType = theUKS.GetOrAddThought("correspondsTo", "linkType");
        Thought templateEvidenceType = theUKS.GetOrAddThought("templateEvidence", "linkType");
        Thought evidenceType = theUKS.GetOrAddThought("evidence", "linkType");
        Thought hasPatternType = theUKS.GetOrAddThought("hasPattern", "linkType");
        Thought usesClassType = theUKS.GetOrAddThought("usesClass", "linkType");
        Thought classFamilyProperty = theUKS.GetOrAddThought("isClassFamily", "Property");

        List<(Thought template, List<Thought> elements)> learnedTemplates = learnedTemplateRoot.Children
            .Select(template => (template,
                elements: template.GetTargetOfFirstLinkOfType(hasPatternType) is SeqElement sequence
                    ? theUKS.FlattenSequence(sequence)
                    : new List<Thought>()))
            .Where(x => x.elements.Count >= 2)
            .ToList();

        List<Link> followerAssociations = classRoot.Children
            .SelectMany(x => x.LinksTo)
            .Where(x => x.LinkType == contextChangesToType && x.To is not null)
            .ToList();

        int created = 0;
        foreach (Thought rule in ruleRoot.Children)
        {
            List<Link> subjectApplications = rule.LinksTo
                .Where(x => x.LinkType == appliesToType)
                .Select(x => x.To)
                .OfType<Link>()
                .Where(x => HasDirectProperty(x.From, classFamilyProperty) &&
                    x.To is not null && HasDirectProperty(x.To, classFamilyProperty))
                .ToList();
            List<Link> verbAssociations = followerAssociations
                .Where(x => x.GetTargetOfFirstLinkOfType(conditionedByType) == rule)
                .ToList();

            foreach (Link subjectApplication in subjectApplications)
            {
                foreach (Link verbAssociation in verbAssociations)
                {
                    List<Thought> sourceEvidence = FindClassPairTemplateEvidence(
                        learnedTemplates, subjectApplication.From, verbAssociation.From);
                    List<Thought> targetEvidence = FindClassPairTemplateEvidence(
                        learnedTemplates, subjectApplication.To, verbAssociation.To);
                    if (sourceEvidence.Count < minEvidenceTemplates ||
                        targetEvidence.Count < minEvidenceTemplates)
                        continue;

                    Thought sourceTemplate = GetOrCreateClassPairTemplate(
                        theUKS, grammarTemplateRoot, subjectApplication.From,
                        verbAssociation.From, sourceEvidence, hasPatternType,
                        usesClassType, templateEvidenceType, ref created);
                    Thought targetTemplate = GetOrCreateClassPairTemplate(
                        theUKS, grammarTemplateRoot, subjectApplication.To,
                        verbAssociation.To, targetEvidence, hasPatternType,
                        usesClassType, templateEvidenceType, ref created);

                    Link correspondence = theUKS.AddStatement(
                        sourceTemplate, correspondsToType, targetTemplate);
                    correspondence.AddLink(conditionedByType, rule);
                    correspondence.AddLink(evidenceType, subjectApplication);
                }
            }
        }
        return created;
    }

    private static List<Thought> FindClassPairTemplateEvidence(
        List<(Thought template, List<Thought> elements)> templates,
        Thought subjectClass,
        Thought followerClass)
    {
        HashSet<Thought> followerMembers = followerClass.Children
            .Where(x => !x.HasAncestor("Wildcard"))
            .ToHashSet();
        List<Thought> evidence = new();
        foreach (var item in templates)
        {
            for (int i = 0; i < item.elements.Count - 1; i++)
            {
                if (!item.elements[i].HasAncestor(subjectClass)) continue;
                if (!followerMembers.Contains(item.elements[i + 1])) continue;
                evidence.Add(item.template);
                break;
            }
        }
        return evidence.Distinct().ToList();
    }

    private static Thought GetOrCreateClassPairTemplate(
        UKS.UKS theUKS,
        Thought grammarTemplateRoot,
        Thought subjectClass,
        Thought followerClass,
        List<Thought> evidence,
        Thought hasPatternType,
        Thought usesClassType,
        Thought templateEvidenceType,
        ref int created)
    {
        string label = "gt_" + SafeLabel(subjectClass.Label) + "_" + SafeLabel(followerClass.Label);
        Thought template = theUKS.Labeled(label);
        if (template is null)
        {
            template = theUKS.GetOrAddThought(label, grammarTemplateRoot);
            created++;
        }

        Thought subjectWildcard = theUKS.Labeled("??" + subjectClass.Label) ??
            theUKS.CreateWildcard("??" + subjectClass.Label, new List<Thought> { subjectClass });
        Thought followerWildcard = theUKS.Labeled("??" + followerClass.Label) ??
            theUKS.CreateWildcard("??" + followerClass.Label, new List<Thought> { followerClass });
        if (template.GetTargetOfFirstLinkOfType(hasPatternType) is null)
            theUKS.AddSequenceAndLink(template, hasPatternType,
                new List<Thought> { subjectWildcard, followerWildcard });

        theUKS.AddStatement(template, usesClassType, subjectClass);
        theUKS.AddStatement(template, usesClassType, followerClass);
        foreach (Thought supportingTemplate in evidence)
            template.AddLink(templateEvidenceType, supportingTemplate);
        template.Weight = template.LinksTo.Count(x => x.LinkType == templateEvidenceType);
        return template;
    }

    private static Dictionary<Thought, FollowerEvidence> GatherFollowerEvidence(
        List<(Thought template, List<Thought> elements)> templates,
        HashSet<Thought> subjectClasses)
    {
        Dictionary<Thought, FollowerEvidence> evidence = new();
        foreach (var item in templates)
        {
            for (int i = 0; i < item.elements.Count - 1; i++)
            {
                Thought matchedClass = item.elements[i].Parents.FirstOrDefault(subjectClasses.Contains);
                if (matchedClass is null) continue;

                Thought follower = item.elements[i + 1];
                if (follower.HasAncestor("Wildcard") ||
                    follower.GetTargetOfFirstLinkOfType("spelled") is not SeqElement)
                    continue;
                if (!evidence.TryGetValue(follower, out FollowerEvidence followerEvidence))
                {
                    followerEvidence = new FollowerEvidence { Word = follower };
                    evidence.Add(follower, followerEvidence);
                }
                followerEvidence.Templates.Add(item.template);
                followerEvidence.SubjectClasses.Add(matchedClass);
            }
        }
        return evidence;
    }

    private static Thought CreateFollowerClass(
        UKS.UKS theUKS,
        Thought classRoot,
        Dictionary<Thought, FollowerEvidence> evidence,
        int minMembers,
        int minTemplatesPerMember,
        int minSubjectClassesPerMember,
        Thought templateEvidenceType,
        ref int created)
    {
        List<FollowerEvidence> qualified = evidence.Values
            .Where(x => x.Templates.Count >= minTemplatesPerMember &&
                x.SubjectClasses.Count >= minSubjectClassesPerMember)
            .OrderByDescending(x => x.Templates.Count)
            .ThenBy(x => x.Word.Label)
            .ToList();
        if (qualified.Count < minMembers) return null;

        HashSet<Thought> members = qualified.Select(x => x.Word).ToHashSet();
        Thought learnedClass = classRoot.Children.FirstOrDefault(existing =>
            existing.Children.Where(x => !x.HasAncestor("Wildcard")).ToHashSet().SetEquals(members));
        if (learnedClass is null)
        {
            learnedClass = theUKS.GetOrAddThought("class*", classRoot);
            created++;
        }

        HashSet<Thought> allTemplates = qualified.SelectMany(x => x.Templates).ToHashSet();
        learnedClass.Weight = allTemplates.Count;
        foreach (FollowerEvidence memberEvidence in qualified)
        {
            Link membership = memberEvidence.Word.AddParent(learnedClass);
            membership.Weight = memberEvidence.Templates.Count / (float)allTemplates.Count;
            foreach (Thought template in memberEvidence.Templates)
            {
                membership.AddLink(templateEvidenceType, template);
                learnedClass.AddLink(templateEvidenceType, template);
            }
        }
        if (theUKS.Labeled("??" + learnedClass.Label) is null)
            theUKS.CreateWildcard("??" + learnedClass.Label, new List<Thought> { learnedClass });
        return learnedClass;
    }

    private static bool TryReadEndSpellingPattern(
        List<Thought> pattern,
        out Thought wildcard,
        out string ending)
    {
        wildcard = null;
        ending = "";
        if (pattern.Count < 2 ||
            !string.Equals(pattern[^1].Label, "spellingend", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!pattern[0].HasAncestor("Wildcard")) return false;
        if (pattern.Skip(1).Take(pattern.Count - 2).Any(x => x.HasAncestor("Wildcard")))
            return false;

        wildcard = pattern[0];
        ending = string.Concat(pattern.Skip(1).Take(pattern.Count - 2).Select(letter =>
            letter.Label.StartsWith("c:", StringComparison.OrdinalIgnoreCase)
                ? letter.Label[2..]
                : letter.Label));
        return true;
    }

    private static string GetSpelling(UKS.UKS theUKS, Thought word)
    {
        if (word.GetTargetOfFirstLinkOfType("spelled") is not SeqElement spellingSequence)
            return "";

        return string.Concat(theUKS.FlattenSequence(spellingSequence).Select(letter =>
            letter.Label.StartsWith("c:", StringComparison.OrdinalIgnoreCase)
                ? letter.Label[2..]
                : letter.Label));
    }

    private static SpellingChange DescribeSpellingChange(string source, string target)
    {
        int prefixLength = 0;
        while (prefixLength < source.Length && prefixLength < target.Length &&
            char.ToUpperInvariant(source[prefixLength]) == char.ToUpperInvariant(target[prefixLength]))
            prefixLength++;

        int suffixLength = 0;
        while (suffixLength < source.Length - prefixLength &&
            suffixLength < target.Length - prefixLength &&
            char.ToUpperInvariant(source[source.Length - suffixLength - 1]) ==
            char.ToUpperInvariant(target[target.Length - suffixLength - 1]))
            suffixLength++;

        string removed = source.Substring(prefixLength, source.Length - prefixLength - suffixLength);
        string inserted = target.Substring(prefixLength, target.Length - prefixLength - suffixLength);
        if (removed.Length == 0 && inserted.Length == 0) return null;

        string location = prefixLength == 0
            ? suffixLength == 0 ? "whole" : "beginning"
            : suffixLength == 0 ? "end" : "inside";
        return new SpellingChange
        {
            Location = location,
            Removed = removed.ToUpperInvariant(),
            Inserted = inserted.ToUpperInvariant(),
            EditDistance = SpellingEditDistance(source, target),
        };
    }

    private static int SpellingEditDistance(string source, string target)
    {
        int[,] distances = new int[source.Length + 1, target.Length + 1];
        for (int i = 0; i <= source.Length; i++) distances[i, 0] = i;
        for (int j = 0; j <= target.Length; j++) distances[0, j] = j;

        for (int i = 1; i <= source.Length; i++)
        {
            for (int j = 1; j <= target.Length; j++)
            {
                int substitution = char.ToUpperInvariant(source[i - 1]) ==
                    char.ToUpperInvariant(target[j - 1]) ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + substitution);
            }
        }
        return distances[source.Length, target.Length];
    }

    private static List<(SpelledClassMember source, SpelledClassMember target)> FindSupportingPairs(
        List<SpelledClassMember> sourceMembers,
        List<SpelledClassMember> targetMembers,
        SpellingChange change)
    {
        Dictionary<string, SpelledClassMember> targets = targetMembers
            .ToDictionary(x => x.Spelling, StringComparer.OrdinalIgnoreCase);
        HashSet<Thought> usedTargets = new();
        List<(SpelledClassMember source, SpelledClassMember target)> pairs = new();

        foreach (SpelledClassMember source in sourceMembers)
        {
            foreach (string result in ApplySpellingChange(source.Spelling, change))
            {
                if (string.Equals(source.Spelling, result, StringComparison.OrdinalIgnoreCase)) continue;
                if (!targets.TryGetValue(result, out SpelledClassMember target)) continue;
                if (!usedTargets.Add(target.Word)) continue;
                pairs.Add((source, target));
                break;
            }
        }
        return pairs;
    }

    private static IEnumerable<string> ApplySpellingChange(string spelling, SpellingChange change)
    {
        StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        if (change.Location == "beginning")
        {
            if (spelling.StartsWith(change.Removed, comparison))
                yield return change.Inserted + spelling[change.Removed.Length..];
            yield break;
        }

        if (change.Location == "end")
        {
            if (spelling.EndsWith(change.Removed, comparison))
                yield return spelling[..(spelling.Length - change.Removed.Length)] + change.Inserted;
            yield break;
        }

        if (change.Location != "inside") yield break;
        if (change.Removed.Length == 0)
        {
            for (int i = 1; i < spelling.Length; i++)
                yield return spelling[..i] + change.Inserted + spelling[i..];
            yield break;
        }

        int start = 1;
        while (start + change.Removed.Length < spelling.Length)
        {
            int index = spelling.IndexOf(change.Removed, start, comparison);
            if (index < 0 || index + change.Removed.Length >= spelling.Length) yield break;
            yield return spelling[..index] + change.Inserted + spelling[(index + change.Removed.Length)..];
            start = index + 1;
        }
    }

    private static List<Thought> BuildSpellingPattern(
        UKS.UKS theUKS,
        SpellingChange change,
        bool source,
        Thought prefixWildcard,
        Thought suffixWildcard)
    {
        string changedLetters = source ? change.Removed : change.Inserted;
        List<Thought> letters = changedLetters
            .Select(letter => theUKS.GetOrAddThought("c:" + letter, "letter"))
            .ToList();

        if (change.Location == "end")
        {
            letters.Insert(0, prefixWildcard);
            letters.Add(theUKS.GetOrAddThought("spellingend", "SpellingPosition"));
        }
        else if (change.Location == "beginning")
        {
            letters.Insert(0, theUKS.GetOrAddThought("spellingbeginning", "SpellingPosition"));
            letters.Add(suffixWildcard);
        }
        else if (change.Location == "inside")
        {
            letters.Insert(0, prefixWildcard);
            letters.Add(suffixWildcard);
        }
        return letters;
    }

    private static string SafeLabel(string value)
    {
        return Regex.Replace(value, "[^A-Za-z0-9]+", "-").Trim('-');
    }

    /// <summary>
    /// Converts the surface patterns supporting each learned class into templates
    /// constrained by that class's wildcard. Words occurring in enough independent
    /// evidence patterns are admitted as tentative class members before the templates
    /// are scored against the preserved phrase observations.
    /// </summary>
    public static int CreateClassBasedTemplates(
        int minTemplateAgreements = 2,
        float minMembershipConfidence = 0.5f)
    {
        var theUKS = MainWindow.theUKS;
        Thought classRoot = theUKS.Labeled("LearnedClass");
        if (classRoot is null) return 0;

        Thought templateRoot = theUKS.GetOrAddThought("LearnedTemplate", "languageElement");
        Thought evidenceType = theUKS.GetOrAddThought("evidence", "linkType");
        Thought usesClassType = theUKS.GetOrAddThought("usesClass", "linkType");
        theUKS.GetOrAddThought("exception", "linkType");
        theUKS.GetOrAddThought("example", "linkType");
        Thought searchOptions = theUKS.Labeled("TemplateSequenceSearch");
        if (searchOptions is null) return 0;

        HashSet<SeqElement> observationSequences = new();
        Thought phraseRoot = theUKS.Labeled("Phrase");
        if (phraseRoot is not null)
        {
            foreach (Thought phrase in phraseRoot.Children)
                if (phrase.GetTargetOfFirstLinkOfType("hasWords") is SeqElement sequence)
                    observationSequences.Add(sequence.FRST);
        }

        int created = 0;
        foreach (Thought learnedClass in classRoot.Children.ToList())
        {
            List<Thought> evidencePatterns = learnedClass.LinksTo
                .Where(x => x.LinkType == evidenceType && x.To is not null)
                .Select(x => x.To)
                .Distinct()
                .ToList();
            if (evidencePatterns.Count == 0) continue;

            // A word must occupy the learned slot in more than one independent
            // surface pattern. The ratio prevents a large evidence group from
            // admitting a word supported by only a small minority of its frames.
            Dictionary<Thought, List<Thought>> candidateEvidence = new();
            foreach (Thought pattern in evidencePatterns)
            {
                foreach (Thought candidate in pattern.Children
                    .Where(x => !x.HasAncestor("Wildcard"))
                    .Distinct())
                {
                    if (!candidateEvidence.TryGetValue(candidate, out List<Thought> supportingPatterns))
                    {
                        supportingPatterns = new();
                        candidateEvidence.Add(candidate, supportingPatterns);
                    }
                    supportingPatterns.Add(pattern);
                }
            }

            foreach (var candidate in candidateEvidence)
            {
                float confidence = candidate.Value.Count / (float)evidencePatterns.Count;
                if (candidate.Value.Count < minTemplateAgreements || confidence < minMembershipConfidence)
                    continue;

                bool wasAlreadyMember = candidate.Key.Parents.Contains(learnedClass);
                Link membership = candidate.Key.AddParent(learnedClass);
                if (membership is not null)
                {
                    membership.Weight = wasAlreadyMember
                        ? MathF.Max(membership.Weight, confidence)
                        : confidence;
                    foreach (Thought supportingPattern in candidate.Value)
                        membership.AddLink(evidenceType, supportingPattern);
                }
            }

            Thought classWildcard = theUKS.Labeled("??" + learnedClass.Label) ??
                theUKS.CreateWildcard("??" + learnedClass.Label, new List<Thought> { learnedClass });
            HashSet<Thought> classMembers = learnedClass.Children
                .Where(x => !x.HasAncestor("Wildcard"))
                .ToHashSet();

            foreach (Thought surfacePattern in evidencePatterns)
            {
                if (surfacePattern.GetTargetOfFirstLinkOfType("hasPattern") is not SeqElement patternSequence)
                    continue;

                List<Thought> templateElements = theUKS.FlattenSequence(patternSequence);
                int slotPosition = templateElements.FindIndex(x => x.HasAncestor("Wildcard"));
                if (slotPosition < 0) continue;
                templateElements[slotPosition] = classWildcard;

                string label = "lt_" + learnedClass.Label + "_" +
                    string.Join("_", templateElements.Select(x => x.Label));
                Thought template = theUKS.Labeled(label);
                if (template is null)
                {
                    template = theUKS.GetOrAddThought(label, templateRoot);
                    created++;
                }

                if (template.GetTargetOfFirstLinkOfType("hasPattern") is null)
                    theUKS.AddSequenceAndLink(template, "hasPattern", templateElements);

                List<SeqElement> surfaceExamples = surfacePattern.LinksTo
                    .Where(x => x.LinkType?.Label == "example" && x.To is SeqElement)
                    .Select(x => ((SeqElement)x.To).FRST)
                    .Distinct()
                    .ToList();

                HashSet<SeqElement> matches = theUKS
                    .FindSequencesByActivation(templateElements, searchOptions)
                    .Select(x => x.seqNode.FRST)
                    .Where(observationSequences.Contains)
                    .ToHashSet();

                template.RemoveLinks("example");
                template.RemoveLinks("exception");
                foreach (SeqElement match in matches)
                    theUKS.AddStatement(template, "example", match);
                foreach (SeqElement exception in surfaceExamples.Where(x => !matches.Contains(x)))
                    theUKS.AddStatement(template, "exception", exception);

                float observationCoverage = surfaceExamples.Count == 0
                    ? 0
                    : matches.Count / (float)surfaceExamples.Count;
                HashSet<Thought> matchedMembers = matches
                    .Select(x => theUKS.FlattenSequence(x))
                    .Where(x => slotPosition < x.Count)
                    .Select(x => x[slotPosition])
                    .Where(classMembers.Contains)
                    .ToHashSet();
                float classCoverage = classMembers.Count == 0
                    ? 0
                    : matchedMembers.Count / (float)classMembers.Count;

                template.Weight = matches.Count;
                Link evidence = theUKS.AddStatement(template, evidenceType, surfacePattern);
                evidence.Weight = observationCoverage;
                Link usesClass = theUKS.AddStatement(template, usesClassType, learnedClass);
                usesClass.Weight = classCoverage;
            }
        }

        return created;
    }

    private sealed class TemplateAlignment
    {
        public Dictionary<int, List<Thought>> Insertions { get; } = new();
    }

    private sealed class SegmentDescriptor
    {
        public string Key { get; init; } = "";
        public Thought Value { get; init; }
        public bool UsesClass { get; init; }
    }

    /// <summary>
    /// Groups finite learned templates that differ only by material inserted before
    /// compatible class slots. The shortest observed variant becomes the core. The
    /// inserted material is retained as ordered optional/repeatable segment metadata;
    /// sequence search continues to use the finite variants for now.
    /// </summary>
    public static int CreateTemplateFamilies(int minVariants = 2, int maxInsertedElements = 4)
    {
        var theUKS = MainWindow.theUKS;
        Thought templateRoot = theUKS.Labeled("LearnedTemplate");
        if (templateRoot is null) return 0;

        Thought familyRoot = theUKS.GetOrAddThought("TemplateFamily", "languageElement");
        Thought segmentRoot = theUKS.GetOrAddThought("TemplateSegment", "languageElement");
        Thought learnedClassRoot = theUKS.Labeled("LearnedClass");
        theUKS.GetOrAddThought("variant", "linkType");
        theUKS.GetOrAddThought("hasCorePattern", "linkType");
        theUKS.GetOrAddThought("hasSegment", "linkType");
        theUKS.GetOrAddThought("usesToken", "linkType");
        theUKS.GetOrAddThought("usesClass", "linkType");
        theUKS.GetOrAddThought("atPosition", "linkType");
        theUKS.GetOrAddThought("segmentOrder", "linkType");
        theUKS.GetOrAddThought("minimum", "linkType");
        theUKS.GetOrAddThought("maximum", "linkType");
        theUKS.GetOrAddThought("isOptional", "Property");
        theUKS.GetOrAddThought("isRepeatable", "Property");

        List<(Thought template, List<Thought> elements)> templates = templateRoot.Children
            .Select(template =>
            {
                SeqElement sequence = template.GetTargetOfFirstLinkOfType("hasPattern") as SeqElement;
                return (template, elements: sequence is null
                    ? new List<Thought>()
                    : theUKS.FlattenSequence(sequence));
            })
            .Where(x => x.elements.Count >= 2 && x.elements.Any(y => y.HasAncestor("Wildcard")))
            .OrderBy(x => x.elements.Count)
            .ThenBy(x => x.template.Label)
            .ToList();

        HashSet<Thought> assigned = new();
        int created = 0;
        foreach (var core in templates)
        {
            if (assigned.Contains(core.template)) continue;

            List<(Thought template, TemplateAlignment alignment)> variants = new()
            {
                (core.template, new TemplateAlignment())
            };
            foreach (var candidate in templates)
            {
                if (candidate.template == core.template || assigned.Contains(candidate.template)) continue;
                int inserted = candidate.elements.Count - core.elements.Count;
                if (inserted <= 0 || inserted > maxInsertedElements) continue;
                if (TryAlignTemplate(core.elements, candidate.elements, learnedClassRoot, out TemplateAlignment alignment))
                    variants.Add((candidate.template, alignment));
            }

            if (variants.Count < minVariants) continue;

            string familyLabel = "tf_" + string.Join("_", core.elements.Select(x => x.Label));
            Thought family = theUKS.Labeled(familyLabel);
            if (family is null)
            {
                family = theUKS.GetOrAddThought(familyLabel, familyRoot);
                created++;
            }

            if (family.GetTargetOfFirstLinkOfType("hasCorePattern") is null)
                theUKS.AddSequenceAndLink(family, "hasCorePattern", core.elements);
            family.Weight = variants.Sum(x => x.template.Weight);
            foreach (var variant in variants)
            {
                Link variantLink = theUKS.AddStatement(family, "variant", variant.template);
                variantLink.Weight = variant.template.Weight;
                assigned.Add(variant.template);
            }

            for (int gap = 0; gap < core.elements.Count; gap++)
            {
                List<List<Thought>> observedInsertions = variants
                    .Select(x => x.alignment.Insertions.TryGetValue(gap, out List<Thought> values)
                        ? values
                        : new List<Thought>())
                    .ToList();
                if (observedInsertions.All(x => x.Count == 0)) continue;

                List<SegmentDescriptor> orderedDescriptors = new();
                foreach (List<Thought> insertion in observedInsertions.OrderByDescending(x => x.Count))
                {
                    foreach (Thought value in insertion)
                    {
                        SegmentDescriptor descriptor = DescribeSegmentValue(value, learnedClassRoot);
                        if (!orderedDescriptors.Any(x => x.Key == descriptor.Key))
                            orderedDescriptors.Add(descriptor);
                    }
                }

                for (int order = 0; order < orderedDescriptors.Count; order++)
                {
                    SegmentDescriptor descriptor = orderedDescriptors[order];
                    List<int> counts = observedInsertions
                        .Select(insertion => insertion.Count(value =>
                            DescribeSegmentValue(value, learnedClassRoot).Key == descriptor.Key))
                        .ToList();
                    int minimum = counts.Min();
                    int maximum = counts.Max();

                    string segmentLabel = family.Label + "-segment" + gap + "-" + order;
                    Thought segment = theUKS.GetOrAddThought(segmentLabel, segmentRoot);
                    segment.Weight = counts.Count(x => x > 0) / (float)counts.Count;
                    if (minimum == 0) segment.AddProperty("isOptional");
                    if (maximum > 1) segment.AddProperty("isRepeatable");

                    theUKS.AddStatement(family, "hasSegment", segment);
                    theUKS.AddStatement(segment,
                        descriptor.UsesClass ? "usesClass" : "usesToken",
                        descriptor.Value);
                    theUKS.AddStatement(segment, "atPosition", theUKS.GetOrAddThought(gap.ToString(), "number"));
                    theUKS.AddStatement(segment, "segmentOrder", theUKS.GetOrAddThought(order.ToString(), "number"));
                    theUKS.AddStatement(segment, "minimum", theUKS.GetOrAddThought(minimum.ToString(), "number"));
                    theUKS.AddStatement(segment, "maximum", theUKS.GetOrAddThought(maximum.ToString(), "number"));
                }
            }
        }

        return created;
    }

    private static bool TryAlignTemplate(
        List<Thought> core,
        List<Thought> expanded,
        Thought learnedClassRoot,
        out TemplateAlignment alignment)
    {
        alignment = new TemplateAlignment();
        int expandedIndex = 0;
        for (int coreIndex = 0; coreIndex < core.Count; coreIndex++)
        {
            int matchIndex = expandedIndex;
            while (matchIndex < expanded.Count &&
                !TemplateElementsAreCompatible(core[coreIndex], expanded[matchIndex], learnedClassRoot))
                matchIndex++;
            if (matchIndex >= expanded.Count) return false;

            if (matchIndex > expandedIndex)
            {
                // For now, consolidation only recognizes modifiers immediately
                // before a wildcard slot. This avoids treating arbitrary inserted
                // sentence material as an optional constituent.
                if (!core[coreIndex].HasAncestor("Wildcard")) return false;
                alignment.Insertions[coreIndex] = expanded
                    .GetRange(expandedIndex, matchIndex - expandedIndex);
            }
            expandedIndex = matchIndex + 1;
        }

        return expandedIndex == expanded.Count;
    }

    private static bool TemplateElementsAreCompatible(
        Thought left,
        Thought right,
        Thought learnedClassRoot)
    {
        if (left == right) return true;

        Thought leftClass = GetWildcardClass(left, learnedClassRoot);
        Thought rightClass = GetWildcardClass(right, learnedClassRoot);
        if (leftClass is not null && rightClass is not null)
            return leftClass == rightClass || leftClass.HasAncestor(rightClass) || rightClass.HasAncestor(leftClass);
        if (leftClass is not null)
            return right.HasAncestor(leftClass);
        if (rightClass is not null)
            return left.HasAncestor(rightClass);
        return false;
    }

    private static SegmentDescriptor DescribeSegmentValue(Thought value, Thought learnedClassRoot)
    {
        Thought valueClass = GetWildcardClass(value, learnedClassRoot);
        if (valueClass is null && learnedClassRoot is not null)
        {
            valueClass = value.Parents
                .Where(learnedClassRoot.Children.Contains)
                .OrderBy(x => x.Children.Count)
                .ThenBy(x => x.Label)
                .FirstOrDefault();
        }

        return valueClass is not null
            ? new SegmentDescriptor { Key = "class:" + valueClass.Label, Value = valueClass, UsesClass = true }
            : new SegmentDescriptor { Key = "token:" + value.Label, Value = value, UsesClass = false };
    }

    private static Thought GetWildcardClass(Thought value, Thought learnedClassRoot)
    {
        if (value is null || learnedClassRoot is null || !value.HasAncestor("Wildcard")) return null;
        return value.Parents.FirstOrDefault(learnedClassRoot.Children.Contains);
    }

    /// <summary>
    /// Discovers learned templates from every Phrase hasWords sequence currently
    /// in the UKS. Phrase owners become evidence; wildcard fillers become
    /// ordinary learned classes.
    /// </summary>
    public static List<Thought> DiscoverPhraseTemplates(
        int minMembers = 30,
        int minFixedElements = 2)
    {
        var theUKS = MainWindow.theUKS;
        Thought phraseRoot = theUKS.Labeled("Phrase");
        if (phraseRoot is null) return new List<Thought>();

        Thought templateRoot = theUKS.GetOrAddThought("LearnedTemplate", "LanguageElement");
        Thought fillerClassRoot = theUKS.GetOrAddThought("LearnedClass", "LanguageElement");
        List<SequenceView> phraseObservations = theUKS.GetSequenceViews(phraseRoot.Children)
            .Where(view => view.LinkType?.Label == "hasWords")
            .ToList();
        return theUKS.DiscoverSequenceTemplates(
            phraseObservations,
            templateRoot,
            fillerClassRoot,
            minMembers,
            minFixedElements);
    }


    public static int ProcessTheExistingText()
    {
        // Previous directed learning pipeline, retained for comparison while
        // the general sequence-class discovery mechanism is evaluated.
        //int retVal = FindUniversalPatterns();
        //ComputeUniversalPatternOverlap();
        //CreateUniversalPatternClasses();
        //FindClassSpellingRules();
        //CreateClassBasedTemplates();
        //CreateClassFamiliesFromSpellingRules();
        //CreateFollowerClassesFromSpellingRules();
        //CreateClassPairTemplates();
        //CreateTemplateFamilies();
        //return retVal;

        List<Thought> learnedTemplates = DiscoverPhraseTemplates(10,1);
        Thought learnedClassRoot = MainWindow.theUKS.Labeled("LearnedClass");
        if (learnedClassRoot is not null)
            MainWindow.theUKS.CoalesceSimilarClasses(learnedClassRoot);
        return learnedTemplates.Count;
    }
    public static void FindPlurals()
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

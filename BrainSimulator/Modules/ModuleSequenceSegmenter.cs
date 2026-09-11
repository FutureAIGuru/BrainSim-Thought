/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UKS;

namespace BrainSimulator.Modules;

/// <summary>
/// Incrementally learns short perceptual chunks from a pause-delimited stream.
/// Each input is consumed continuously; only candidate sequences persist.
/// </summary>
public class ModuleSequenceSegmenter : ModuleBase
{
    public const float NewCandidateWeight = 1f;
    // Reinforcement is 0.2 per matched symbol: no fixed occurrence premium.
    public const float RecognitionIncrement = 0.2f;
    public const float LengthBonusPerSymbol = 0.2f;
    public const float RecognitionThreshold = 1f;
    // Applied once per nonempty submitted line, including matched candidates.
    public const float DecayPerChunk = 0.05f;
    public const float InterferencePerChunk = 0.005f;
    public const string EvidenceLinkType = "hasSegmentationEvidence";
    public const string StartEvidence = "segmentation:pauseStart";
    public const string EndEvidence = "segmentation:pauseEnd";
    public const string RemainderEvidence = "segmentation:remainder";
    public const string DecayEvidence = "segmentation:weightLost";

    private Thought _candidateRoot;
    private Thought _hasSymbols;
    private Thought _exactSearch;
    private StreamReader _sequenceReader;
    private string _sequenceReaderPath;

    public int MaximumChunkSize { get; set; } = 4;
    public string LastSegmentation { get; private set; } = string.Empty;
    public string LastStatus { get; private set; } = "Enter an unsegmented line.";
    public string LastTrace { get; private set; } = string.Empty;

    public override void Fire()
    {
        Init();
        UpdateDialog();
    }

    public override void Initialize()
    {
        EnsureKnowledge();
    }

    public override void UKSInitializedNotification()
    {
        _candidateRoot = null;
        _hasSymbols = null;
        _exactSearch = null;
        EnsureKnowledge();
    }

    /// <summary>
    /// Learns every short prefix and suffix because pauses make those word
    /// boundaries certain. Established nonplastic words divide the input into
    /// remainders, which receive evidence as plastic candidate sequences.
    /// </summary>
    public string SubmitLine(string input)
    {
        EnsureKnowledge();
        List<Thought> symbols = (input ?? string.Empty)
            .Where(char.IsLetter)
            .Select(letter => theUKS.Labeled(UKS.UKS.GetCharacterLabel(letter)))
            .Where(letter => letter is not null)
            .ToList();
        if (symbols.Count == 0)
        {
            LastSegmentation = string.Empty;
            LastStatus = "No symbols were submitted.";
            LastTrace = LastStatus;
            return LastStatus;
        }

        List<string> trace = new() { $"Input: {GetSymbolText(symbols)}" };
        var plasticityBefore = _candidateRoot.Children.ToDictionary(
            candidate => candidate, candidate => candidate.isPlastic);
        HashSet<Thought> knownBeforeInput = _candidateRoot.Children
            .Where(candidate => !candidate.isPlastic)
            .ToHashSet();
        HashSet<Thought> observedCandidates = new();
        int maximum = Math.Min(Math.Max(1, MaximumChunkSize), symbols.Count);

        for (int length = 1; length <= maximum; length++)
        {
            ObserveEdgeCandidate(symbols.GetRange(0, length),
                "start", observedCandidates, trace, StartEvidence);
            ObserveEdgeCandidate(symbols.GetRange(symbols.Count - length, length),
                "end", observedCandidates, trace, EndEvidence);
        }

        List<string> recognizedInterior = new();
        int knownMaximum = knownBeforeInput.Select(candidate =>
            GetCandidateSymbols(candidate).Count()).DefaultIfEmpty(0).Max();
        int remainderStart = 0;
        int position = 0;
        while (position < symbols.Count && knownMaximum > 0)
        {
            int interiorMaximum = Math.Min(
                knownMaximum, symbols.Count - position);
            Thought candidate = FindGuidingCandidate(
                symbols, position, interiorMaximum, out int length,
                knownBeforeInput);
            if (candidate is null)
            {
                position++;
                continue;
            }

            if (position > remainderStart)
                ObserveEdgeCandidate(
                    symbols.GetRange(remainderStart, position - remainderStart),
                    $"remainder before {candidate.Label}", observedCandidates, trace,
                    RemainderEvidence);
            candidate.Fire();
            observedCandidates.Add(candidate);
            string text = GetSymbolText(symbols.GetRange(position, length));
            recognizedInterior.Add(text);
            trace.Add($"interior {position}-{position + length - 1}: " +
                $"recognized established {candidate.Label}; supplies boundaries");
            position += length;
            remainderStart = position;
        }
        if (recognizedInterior.Count > 0 && remainderStart < symbols.Count)
            ObserveEdgeCandidate(
                symbols.GetRange(remainderStart, symbols.Count - remainderStart),
                "remainder after last established word", observedCandidates, trace,
                RemainderEvidence);
        if (recognizedInterior.Count == 0)
            trace.Add("Interior: no known candidates; nothing stored");

        DecayInterfereAndForgetCandidates(
            symbols, observedCandidates, trace);
        LastSegmentation = recognizedInterior.Count == 0
            ? "no known interior words"
            : string.Join(" ", recognizedInterior);
        LastStatus = BuildStatus(LastSegmentation);
        trace.Add("Survivors: " + BuildCandidateSummary(10));
        trace.Add("Boundary evidence: cumulative hits since instrumentation/candidate creation; " +
            "missing evidence is unknown, not a miss. Remainders may contain multiple words.");
        foreach (string word in new[] { "cat", "hat", "at", "t" })
        {
            Thought candidate = theUKS.Labeled("candidate:" + word);
            if (candidate is null || !candidate.Parents.Contains(_candidateRoot))
            {
                trace.Add($"Watch {word}: absent (never created or deleted)");
                continue;
            }
            string before = plasticityBefore.TryGetValue(candidate, out bool wasPlastic)
                ? wasPlastic.ToString() : "new";
            trace.Add($"Watch {word}: weight={candidate.Weight:0.000}; " +
                $"start={EvidenceCount(candidate, StartEvidence):0}; " +
                $"end={EvidenceCount(candidate, EndEvidence):0}; " +
                $"remainder={EvidenceCount(candidate, RemainderEvidence):0}; " +
                $"both={ComplementaryEvidence(candidate):0}; " +
                $"decay total={EvidenceCount(candidate, DecayEvidence):0.000}; " +
                $"isPlastic={before}->{candidate.isPlastic}");
        }
        trace.Add("Boundary evidence is diagnostic only: no automatic consolidation or suppression.");
        LastTrace = string.Join(Environment.NewLine, trace);
        return LastStatus;
    }

    private void ObserveEdgeCandidate(
        List<Thought> percept,
        string edge,
        ISet<Thought> observedCandidates,
        ICollection<string> trace,
        string evidenceSource)
    {
        Thought candidate = FindExactCandidate(percept);
        string text = GetSymbolText(percept);
        if (candidate is null)
        {
            candidate = CreateCandidate(percept);
            trace.Add($"{edge} {text}: created ({candidate.Weight:0.00})");
        }
        else
        {
            float oldWeight = candidate.Weight;
            candidate.Weight += GetRecognitionIncrement(percept.Count);
            candidate.Fire();
            trace.Add($"{edge} {text}: reinforced " +
                $"({oldWeight:0.00}->{candidate.Weight:0.00})");
        }
        observedCandidates.Add(candidate);
        AddEvidence(candidate, evidenceSource, 1);
    }

    // These link weights are counts, not confidence scores. No length bonus or
    // decay is applied to them, and they do not alter recognition or ranking.
    private void AddEvidence(Thought candidate, string source, float amount)
    {
        Thought type = theUKS.GetOrAddThought(EvidenceLinkType, "LinkType");
        Thought root = theUKS.GetOrAddThought("SegmentationEvidence", "Thought");
        Thought target = theUKS.GetOrAddThought(source, root);
        Link link = candidate.HasLink(type, target);
        if (link is null)
        {
            link = candidate.AddLink(type, target);
            link.Weight = 0;
        }
        link.Weight += amount;
    }

    private static float EvidenceCount(Thought candidate, string source) =>
        candidate.LinksTo.FirstOrDefault(link =>
            link.LinkType?.Label == EvidenceLinkType && link.To?.Label == source)?.Weight ?? 0;

    // A whole remainder has two inferred boundaries, not necessarily one word.
    private static float ComplementaryEvidence(Thought candidate) =>
        Math.Min(EvidenceCount(candidate, StartEvidence),
            EvidenceCount(candidate, EndEvidence)) + EvidenceCount(candidate, RemainderEvidence);

    private static float GetRecognitionIncrement(int symbolCount)
    {
        return RecognitionIncrement +
            LengthBonusPerSymbol * Math.Max(0, symbolCount - 1);
    }

    private Thought FindGuidingCandidate(
        List<Thought> input,
        int position,
        int maximum,
        out int matchedLength,
        ISet<Thought> eligibleCandidates = null)
    {
        for (int length = maximum; length >= 1; length--)
        {
            Thought candidate = FindExactCandidate(input.GetRange(position, length));
            if (candidate is null) continue;
            if (eligibleCandidates is not null &&
                !eligibleCandidates.Contains(candidate)) continue;
            matchedLength = length;
            return candidate;
        }
        matchedLength = 0;
        return null;
    }

    /// <summary>
    /// Generates pause-delimited test sequences from a supplied vocabulary
    /// and presents each one through the ordinary SubmitLine path.
    /// Words are sampled independently with replacement and concatenated
    /// without exposing their original boundaries to the learner.
    /// </summary>
    public int RunVocabularyExperiment(
        IEnumerable<string> vocabulary,
        int wordsPerSequence = 5,
        int sequenceCount = 1000,
        int? randomSeed = null)
    {
        List<string> words = (vocabulary ?? Enumerable.Empty<string>())
            .Select(word => string.Concat((word ?? string.Empty)
                .Where(char.IsLetter)
                .Select(char.ToLowerInvariant)))
            .Where(word => word.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (words.Count == 0 || wordsPerSequence <= 0 || sequenceCount <= 0)
            return 0;

        Random random = randomSeed.HasValue
            ? new Random(randomSeed.Value)
            : Random.Shared;
        for (int sequence = 0; sequence < sequenceCount; sequence++)
        {
            string input = string.Concat(Enumerable.Range(0, wordsPerSequence)
                .Select(_ => words[random.Next(words.Count)]));
            SubmitLine(input);
        }
        return sequenceCount;
    }

    /// <summary>
    /// Presents cat at opposite pause edges with independently randomized word filler.
    /// Existing learning is retained; an optional seed makes a pair reproducible.
    /// Keeps both traces so the first observation is not hidden by the second.
    /// </summary>
    public void RunBoundaryExperiment(int? randomSeed = null)
    {
        Random random = randomSeed.HasValue ? new Random(randomSeed.Value) : Random.Shared;
        // Exclude cat so filler cannot add extra edge exposures of the target.
        string[] fillerWords = { "dog", "has", "fur", "hat", "car" };
        string RandomWords() => string.Concat(Enumerable.Range(0, random.Next(3, 6))
            .Select(_ => fillerWords[random.Next(fillerWords.Length)]));

        SubmitLine("cat" + RandomWords());
        string firstTrace = LastTrace;
        SubmitLine(RandomWords() + "cat");
        LastTrace = "exp1: cat at opposite edges; filler is 3-5 random words " +
            "from dog/has/fur/hat/car (no spaces); existing candidates retained." +
            Environment.NewLine + firstTrace + Environment.NewLine +
            Environment.NewLine + LastTrace;
    }

    private Thought FindExactCandidate(List<Thought> percept)
    {
        return theUKS.FindSequencesByActivation(percept, _exactSearch)
            .SelectMany(match => match.seqNode.LinksFrom)
            .Where(link => ReferenceEquals(link.LinkType, _hasSymbols) &&
                link.From?.Parents.Contains(_candidateRoot) == true)
            .Select(link => link.From)
            .Where(candidate => GetCandidateSymbols(candidate)
                .SequenceEqual(percept))
            .OrderByDescending(candidate => candidate.Weight)
            .FirstOrDefault();
    }

    private Thought CreateCandidate(List<Thought> percept)
    {
        string text = GetSymbolText(percept);
        Thought candidate = theUKS.GetOrAddThought(
            "candidate:" + text, _candidateRoot);
        candidate.isPlastic = true;
        candidate.Weight = NewCandidateWeight;
        theUKS.AddSequenceAndLink(candidate, _hasSymbols, percept);
        candidate.Fire();
        return candidate;
    }

    private void DecayInterfereAndForgetCandidates(
        IReadOnlyCollection<Thought> percept,
        IEnumerable<Thought> observedCandidates,
        ICollection<string> trace)
    {
        HashSet<Thought> observedSet = observedCandidates.ToHashSet();
        HashSet<Thought> perceptSymbols = percept.ToHashSet();
        List<string> changes = new();
        int decayedCount = 0;
        int deletedCount = 0;
        foreach (Thought candidate in _candidateRoot.Children.ToList())
        {
            if (!candidate.isPlastic) continue;
            float oldWeight = candidate.Weight;
            candidate.Weight = Math.Max(0, candidate.Weight - DecayPerChunk);
            if (!observedSet.Contains(candidate) &&
                GetCandidateSymbols(candidate).Any(perceptSymbols.Contains))
            {
                candidate.Weight = Math.Max(
                    0, candidate.Weight - InterferencePerChunk);
            }
            decayedCount++;
            AddEvidence(candidate, DecayEvidence, oldWeight - candidate.Weight);
            if (candidate.Weight <= 0.0001f)
            {
                if (changes.Count < 8)
                    changes.Add($"{candidate.Label} {oldWeight:0.00}->deleted");
                candidate.Delete();
                deletedCount++;
            }
            else if (changes.Count < 8)
            {
                changes.Add($"{candidate.Label} " +
                    $"{oldWeight:0.00}->{candidate.Weight:0.00}");
            }
        }
        trace.Add(decayedCount == 0
            ? "Plastic candidates decayed: none"
            : $"Plastic candidates decayed: {decayedCount} " +
                $"(including matches; base {DecayPerChunk:0.00}/line); deleted: " +
                $"{deletedCount}; {string.Join(", ", changes)}");
    }

    private IEnumerable<Thought> GetCandidateSymbols(Thought candidate)
    {
        SeqElement sequence = candidate.GetTargetOfFirstLinkOfType(
            _hasSymbols) as SeqElement;
        return sequence is null
            ? Enumerable.Empty<Thought>()
            : theUKS.FlattenSequence(sequence);
    }

    private static string GetSymbolText(IEnumerable<Thought> symbols)
    {
        return string.Concat(symbols.Select(symbol =>
            symbol.Label.StartsWith(UKS.UKS.CharacterPrefix, StringComparison.OrdinalIgnoreCase)
                ? symbol.Label[UKS.UKS.CharacterPrefix.Length..].ToLowerInvariant()
                : symbol.Label.ToLowerInvariant()));
    }

    /// <summary>
    /// Incrementally presents pause-delimited sequences from a corpus file.
    /// Text beginning with the first tab on each line is metadata and is not
    /// presented to the learner.
    /// </summary>
    public int LoadTextFromFile(string filePath, int sequencesPerCall = 1000)
    {
        if (sequencesPerCall <= 0) sequencesPerCall = 1;
        if (!File.Exists(filePath))
        {
            ResetSequenceReader();
            return 0;
        }

        try
        {
            if (_sequenceReader is null || !string.Equals(
                _sequenceReaderPath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                ResetSequenceReader();
                _sequenceReader = new StreamReader(File.Open(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.Read));
                _sequenceReaderPath = filePath;
            }

            int count = 0;
            while (count < sequencesPerCall && _sequenceReader is not null)
            {
                string line = _sequenceReader.ReadLine();
                if (line is null) break;

                int tab = line.IndexOf('\t');
                string phrase = (tab < 0 ? line : line[..tab]).Trim();
                if (phrase.Length == 0) continue;

                foreach (string sentence in Regex.Split(
                    phrase, @"(?<=[\.!\?])\s+"))
                {
                    if (count >= sequencesPerCall) break;
                    string trimmed = sentence.Trim();
                    if (trimmed.Length == 0) continue;
                    SubmitLine(trimmed);
                    count++;
                }
            }

            if (_sequenceReader is not null && _sequenceReader.EndOfStream)
                ResetSequenceReader();
            return count;
        }
        catch
        {
            ResetSequenceReader();
            throw;
        }
    }

    public void CancelIncrementalLoad()
    {
        ResetSequenceReader();
    }

    private void ResetSequenceReader()
    {
        _sequenceReader?.Dispose();
        _sequenceReader = null;
        _sequenceReaderPath = null;
    }

    private void EnsureKnowledge()
    {
        if (theUKS is null) GetUKS();
        if (theUKS is null) return;

        Thought languageElement = theUKS.GetOrAddThought(
            "LanguageElement", "Thought");
        _candidateRoot = theUKS.GetOrAddThought(
            "CandidateSequence", languageElement);
        _hasSymbols = theUKS.GetOrAddThought("hasSymbols", "LinkType");
        _exactSearch = theUKS.GetOrAddThought(
            "CandidateSequenceExactSearch", "SequenceSearchOption");
        _exactSearch.AddLink("hasProperty",
            theUKS.GetOrAddThought("mustMatchFirst", "Property"));
        _exactSearch.AddLink("hasProperty",
            theUKS.GetOrAddThought("mustMatchLast", "Property"));
        _exactSearch.AddLink("hasProperty",
            theUKS.GetOrAddThought("allowNestedSequences", "Property"));
    }

    private string BuildStatus(string perceived)
    {
        return $"{perceived}    {BuildCandidateSummary(6)}";
    }

    private string BuildCandidateSummary(int maximum)
    {
        string candidates = string.Join(", ", _candidateRoot.Children
            .OrderByDescending(candidate => candidate.Weight)
            .ThenBy(candidate => candidate.Label, StringComparer.Ordinal)
            .Take(maximum)
            .Select(candidate =>
                $"{candidate.Label["candidate:".Length..]}:{candidate.Weight:0.00}" +
                $" [start={EvidenceCount(candidate, StartEvidence):0}," +
                $" end={EvidenceCount(candidate, EndEvidence):0}," +
                $" rem={EvidenceCount(candidate, RemainderEvidence):0}]"));
        if (candidates.Length == 0) candidates = "no surviving proto-words";
        return candidates;
    }
}

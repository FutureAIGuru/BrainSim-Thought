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
/// Learns maximal repeated prefixes and suffixes of pause-delimited inputs.
/// Candidate sequences, boundary contexts, and confirmed phrase patterns persist in the UKS.
/// </summary>
public class ModuleSequenceSegmenter : ModuleBase
{
    public const float NewCandidateWeight = 1f;
    // One reward per winning candidate per input, independent of length.
    public const float RecognitionIncrement = 1f;
    public const float RecognitionThreshold = 1f;
    public const float EstablishedWeight = 3f;
    public const float EstablishedWins = 3f;
    public const double CompleteSplitMargin = 0.6931471805599453; // log(2)
    // Applied once per nonempty submitted line to candidates that did not win.
    public const float DecayPerChunk = 0.05f;
    public const string EvidenceLinkType = "hasSegmentationEvidence";
    public const string StartEvidence = "segmentation:pauseStart";
    public const string EndEvidence = "segmentation:pauseEnd";
    public const string MaximalEvidence = "segmentation:maximalMatch";
    public const string InferredBoundaryEvidence = "segmentation:knownWordBoundary";
    public const string StartContextLinkType = "hasSegmentationFollowingContext";
    public const string EndContextLinkType = "hasSegmentationPrecedingContext";
    public const string PauseContext = "segmentation:pause";

    private Thought _candidateRoot;
    private Thought _hasSymbols;
    private Thought _phraseRoot;
    private Thought _hasWords;
    private Thought _matchKnownSuffix;
    private Thought _matchKnownPrefix;
    private StreamReader _sequenceReader;
    private string _sequenceReaderPath;

    public int MaximumChunkSize { get; set; } = 32;
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
        _phraseRoot = null;
        _hasWords = null;
        _matchKnownSuffix = null;
        _matchKnownPrefix = null;
        EnsureKnowledge();
    }

    /// <summary>
    /// Store edge hypotheses, then reward only the longest shared sequence at
    /// each edge whose inward context varies. A pause is also a context, so
    /// "cat" followed by "thecat" supplies the same evidence as "acat"/"thecat".
    /// Spaces are not supplied to the learner as word boundaries.
    /// </summary>
    public string SubmitLine(string input)
    {
        EnsureKnowledge();
        List<Thought> symbols = InputSymbols(input);
        if (symbols.Count == 0)
        {
            LastSegmentation = string.Empty;
            LastStatus = "No symbols were submitted.";
            LastTrace = LastStatus;
            return LastStatus;
        }

        List<string> trace = new() { $"Input: {GetSymbolText(symbols)}" };
        var complete = FindCompleteSplits(symbols);
        bool confident = complete.Count > 0 &&
            complete[0].Words.All(IsEstablished) &&
            (symbols.Count == 1 || complete[0].Words.Any(word => GetCandidateSymbols(word).Count() > 1)) &&
            (complete.Count == 1 || complete[0].Score - complete[1].Score >= CompleteSplitMargin);
        if (complete.Count > 0)
        {
            trace.Add($"Best complete split: {FormatSplit(complete[0])}; score={complete[0].Score:0.000}");
            if (complete.Count > 1)
                trace.Add($"Alternative: {FormatSplit(complete[1])}; score={complete[1].Score:0.000}");
        }
        if (confident)
        {
            RememberCompletePhrase(complete[0]);
            HashSet<Thought> selected = complete[0].Words.ToHashSet();
            foreach (Thought word in selected)
            {
                word.Weight += RecognitionIncrement;
                word.Fire();
            }
            // Recognition is not new boundary-discovery evidence.
            DecayAndForgetCandidates(selected, trace);
            LastSegmentation = FormatSplit(complete[0]);
            LastStatus = BuildStatus(LastSegmentation);
            trace.Add("Confident complete split: candidate discovery skipped; selected words reinforced.");
            LastTrace = string.Join(Environment.NewLine, trace);
            return LastStatus;
        }
        trace.Add(complete.Count == 0 ? "No complete split: boundary discovery enabled." :
            "Complete split is provisional, ambiguous, or letters-only: boundary discovery enabled.");
        // A per-input index only; learned state and contexts live in the UKS.
        Dictionary<string, Thought> candidates = _candidateRoot.Children
            .ToDictionary(candidate => GetSymbolText(GetCandidateSymbols(candidate)),
                candidate => candidate, StringComparer.Ordinal);
        HashSet<Thought> rewarded = new();
        // Freeze eligibility so the first edge cannot bootstrap the other edge
        // from a single observation of the same whole input.
        HashSet<Thought> knownBefore = candidates.Values.Where(IsRecognized).ToHashSet();
        ObserveKnownWordBoundaries(symbols, candidates, knownBefore, rewarded, trace);
        ObserveBoundary(symbols, true, candidates, knownBefore, rewarded, trace);
        ObserveBoundary(symbols, false, candidates, knownBefore, rewarded, trace);
        foreach (Thought candidate in rewarded)
        {
            candidate.Weight += RecognitionIncrement;
            candidate.Fire();
            AddEvidence(candidate, MaximalEvidence, 1);
        }
        DecayAndForgetCandidates(rewarded, trace);
        LastSegmentation = Segment(symbols);
        LastStatus = BuildStatus(LastSegmentation);
        trace.Add("Segmentation: " + LastSegmentation + " (brackets mark unknown spans)");
        trace.Add("Survivors: " + BuildCandidateSummary(10));
        LastTrace = string.Join(Environment.NewLine, trace);
        return LastStatus;
    }

    private void ObserveBoundary(
        List<Thought> symbols, bool start, IDictionary<string, Thought> candidates,
        ISet<Thought> knownBefore, ISet<Thought> rewarded, ICollection<string> trace)
    {
        string side = start ? "start" : "end";
        string evidence = start ? StartEvidence : EndEvidence;
        Thought contextType = theUKS.GetOrAddThought(
            start ? StartContextLinkType : EndContextLinkType, "LinkType");
        Thought pause = theUKS.GetOrAddThought(PauseContext, "SegmentationEvidence");
        Thought variedWinner = null, knownWinner = null, wholeRepeat = null;
        int maximum = Math.Min(Math.Max(1, MaximumChunkSize), symbols.Count);
        for (int length = 1; length <= maximum; length++)
        {
            List<Thought> percept = symbols.GetRange(start ? 0 : symbols.Count - length, length);
            string text = GetSymbolText(percept);
            if (!candidates.TryGetValue(text, out Thought candidate))
            {
                candidate = CreateCandidate(percept);
                candidates.Add(text, candidate);
                trace.Add($"{side} {text}: created hypothesis");
            }
            // Read the actual adjacent symbol even at the configured length cap.
            // Truncating a candidate must never manufacture a pause.
            Thought context = length == symbols.Count ? pause :
                symbols[start ? length : symbols.Count - length - 1];
            bool varied = candidate.LinksTo.Any(link =>
                ReferenceEquals(link.LinkType, contextType) &&
                !ReferenceEquals(link.To, context));
            if (varied) variedWinner = candidate; // ascending lengths: keep longest
            if (knownBefore.Contains(candidate)) knownWinner = candidate;
            if (length == symbols.Count && EvidenceCount(candidate, evidence) > 0)
                wholeRepeat = candidate;
            candidate.AddLink(contextType, context);
            AddEvidence(candidate, evidence, 1);
        }

        // Changing context is stronger evidence than an old whole-phrase guess.
        // Identical whole-input repeats are a fallback, not evidence for every
        // embedded fragment. A candidate at both edges earns only one reward.
        Thought winner = variedWinner ?? knownWinner ?? wholeRepeat;
        if (winner is null)
            trace.Add($"{side}: no repeated boundary candidate yet");
        else
        {
            rewarded.Add(winner);
            string reason = variedWinner is not null ? "varying inward context" :
                knownWinner is not null ? "previously learned word" : "whole-input repeat";
            trace.Add($"{side}: winner {winner.Label} ({reason}); shorter alternatives receive no reward");
        }
    }

    private static bool IsRecognized(Thought candidate) =>
        candidate.Weight >= RecognitionThreshold &&
        (EvidenceCount(candidate, MaximalEvidence) > 0 ||
            EvidenceCount(candidate, InferredBoundaryEvidence) > 0);

    private static bool IsEstablished(Thought candidate) =>
        candidate.Weight >= EstablishedWeight &&
        (EvidenceCount(candidate, MaximalEvidence) >= EstablishedWins ||
            EvidenceCount(candidate, InferredBoundaryEvidence) > 0);

    // A known complete suffix supplies a right boundary for the unknown prefix;
    // a known complete prefix supplies a left boundary for the unknown suffix.
    // Eligibility is frozen before this input so inferred words cannot confirm
    // themselves or divide another part of the same input.
    private void ObserveKnownWordBoundaries(
        List<Thought> symbols, IDictionary<string, Thought> candidates,
        ISet<Thought> knownBefore, ISet<Thought> rewarded, ICollection<string> trace)
    {
        HashSet<Thought> established = knownBefore.Where(IsEstablished).ToHashSet();
        if (established.Count == 0) return;
        int maximum = Math.Min(Math.Max(1, MaximumChunkSize), symbols.Count - 1);
        for (int length = 1; length <= maximum; length++)
        {
            if (!FindCompleteSplits(symbols.GetRange(length, symbols.Count - length), established)
                .Any(split => split.Words.Count >= 2 && MatchesKnownPhrase(split.Words, true, length)))
                continue;
            RewardInferredSpan(symbols.GetRange(0, length), "start", candidates, rewarded, trace);
            break;
        }
        for (int length = 1; length <= maximum; length++)
        {
            if (!FindCompleteSplits(symbols.GetRange(0, symbols.Count - length), established)
                .Any(split => split.Words.Count >= 2 && MatchesKnownPhrase(split.Words, false, length)))
                continue;
            RewardInferredSpan(symbols.GetRange(symbols.Count - length, length), "end", candidates, rewarded, trace);
            break;
        }
    }

    private bool MatchesKnownPhrase(List<Thought> knownWords, bool unknownAtStart, int unknownLength)
    {
        Thought options = unknownAtStart ? _matchKnownSuffix : _matchKnownPrefix;
        foreach (var (sequence, _) in theUKS.FindSequencesByActivation(knownWords, options))
        {
            if (!sequence.LinksFrom.Any(link => ReferenceEquals(link.LinkType, _hasWords) &&
                link.From.Parents.Contains(_phraseRoot))) continue;
            List<Thought> phrase = theUKS.FlattenSequence(sequence);
            if (phrase.Count != knownWords.Count + 1) continue;
            Thought oldWord = unknownAtStart ? phrase[0] : phrase[^1];
            if (!_candidateRoot.Children.Contains(oldWord) || !IsEstablished(oldWord)) continue;
            int oldLength = GetCandidateSymbols(oldWord).Count();
            if (Math.Abs(oldLength - unknownLength) > 2) continue;
            IEnumerable<Thought> compared = unknownAtStart ? phrase.Skip(1) : phrase.Take(phrase.Count - 1);
            if (compared.SequenceEqual(knownWords)) return true;
        }
        return false;
    }

    private void RememberCompletePhrase(CompleteSplit split)
    {
        if (split.Words.Count < 3) return;
        string label = "segmentation:phrase:" + FormatSplit(split).Replace(" ", "");
        Thought pattern = theUKS.GetOrAddThought(label, _phraseRoot);
        if (pattern.GetTargetOfFirstLinkOfType(_hasWords) is not null) return;
        SeqElement first = theUKS.CreateFirstElement(label, split.Words[0]);
        SeqElement last = first;
        foreach (Thought word in split.Words.Skip(1))
            last = theUKS.AddElement(last, word);
        pattern.AddLink(_hasWords, first);
    }

    private void RewardInferredSpan(List<Thought> span, string side,
        IDictionary<string, Thought> candidates, ISet<Thought> rewarded, ICollection<string> trace)
    {
        string text = GetSymbolText(span);
        if (!candidates.TryGetValue(text, out Thought candidate))
        {
            candidate = CreateCandidate(span);
            candidates.Add(text, candidate);
        }
        // A pause and an established word provide two boundaries at once.
        candidate.Weight = Math.Max(candidate.Weight, EstablishedWeight - RecognitionIncrement);
        AddEvidence(candidate, InferredBoundaryEvidence, 1);
        rewarded.Add(candidate);
        trace.Add($"{side} {text}: inferred from pause and established words");
    }

    /// <summary>Recognizes without adding observations or changing weights.</summary>
    public string RecognizeLine(string input)
    {
        EnsureKnowledge();
        return Segment(InputSymbols(input));
    }

    private List<Thought> InputSymbols(string input) => (input ?? string.Empty)
        .Where(char.IsLetter)
        .Select(letter => theUKS.Labeled(UKS.UKS.GetCharacterLabel(letter)))
        .Where(letter => letter is not null).ToList();

    private string Segment(List<Thought> symbols)
    {
        var complete = FindCompleteSplits(symbols);
        if (complete.Count > 0) return FormatSplit(complete[0]);
        var words = _candidateRoot.Children.Where(IsRecognized)
            .Select(candidate => (candidate, symbols: GetCandidateSymbols(candidate).ToList()))
            .Where(word => word.symbols.Count > 0)
            // Recognition follows accumulated support. Maximal length decides
            // discovery, but must not let a rare multiword chunk hide a much
            // better supported word during recognition.
            .OrderByDescending(word => word.candidate.Weight)
            .ThenByDescending(word => word.symbols.Count).ToList();
        List<string> parts = new();
        int position = 0, unknownStart = 0;
        while (position < symbols.Count)
        {
            var match = words.FirstOrDefault(word =>
                position + word.symbols.Count <= symbols.Count &&
                symbols.Skip(position).Take(word.symbols.Count).SequenceEqual(word.symbols));
            if (match.candidate is null) { position++; continue; }
            if (position > unknownStart)
                parts.Add("[" + GetSymbolText(symbols.GetRange(unknownStart, position - unknownStart)) + "]");
            parts.Add(GetSymbolText(match.symbols));
            position += match.symbols.Count;
            unknownStart = position;
        }
        if (position > unknownStart)
            parts.Add("[" + GetSymbolText(symbols.GetRange(unknownStart, position - unknownStart)) + "]");
        return string.Join(" ", parts);
    }

    private sealed record CompleteSplit(double Score, List<Thought> Words);

    private string FormatSplit(CompleteSplit split) => string.Join(" ",
        split.Words.Select(word => GetSymbolText(GetCandidateSymbols(word))));

    // Keep the best two paths at each position: suffix costs depend only on
    // position, so discarding lower-ranked prefixes cannot hide a better split.
    // This is a confidence heuristic, not a calibrated language probability.
    // Length-weighted log confidence compares equal character coverage; a small
    // per-word cost discourages gratuitous fragmentation without summing weights.
    private List<CompleteSplit> FindCompleteSplits(List<Thought> input, ISet<Thought> eligible = null)
    {
        if (input.Count == 0) return new();
        var words = _candidateRoot.Children.Where(word =>
                eligible is null ? IsRecognized(word) : eligible.Contains(word))
            .OrderBy(word => word.Label, StringComparer.Ordinal)
            .Select(word => (word, symbols: GetCandidateSymbols(word).ToList())).ToList();
        var paths = Enumerable.Range(0, input.Count + 1)
            .Select(_ => new List<CompleteSplit>()).ToArray();
        paths[0].Add(new CompleteSplit(0, new()));
        for (int position = 0; position < input.Count; position++)
        {
            if (paths[position].Count == 0) continue;
            foreach (var (word, symbols) in words)
            {
                int end = position + symbols.Count;
                if (symbols.Count == 0 || end > input.Count ||
                    !input.Skip(position).Take(symbols.Count).SequenceEqual(symbols)) continue;
                double score = symbols.Count * Math.Log(word.Weight / (word.Weight + 1.0)) - 0.1;
                foreach (CompleteSplit prefix in paths[position])
                    paths[end].Add(new CompleteSplit(prefix.Score + score,
                        prefix.Words.Append(word).ToList()));
                paths[end] = paths[end].OrderByDescending(path => path.Score).Take(2).ToList();
            }
        }
        return paths[input.Count];
    }
    // These link weights are counts, not confidence scores. No length bonus or
    // decay is applied to them. Maximal-match evidence gates recognition.
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

    private Thought CreateCandidate(List<Thought> percept)
    {
        string text = GetSymbolText(percept);
        Thought candidate = theUKS.GetOrAddThought(
            "candidate:" + text, _candidateRoot);
        candidate.isPlastic = true;
        candidate.Weight = NewCandidateWeight;
        // Each hypothesis owns a flat UKS sequence. Candidates are already
        // deduplicated by the per-input index; global sequence search/compression
        // adds no value here and couples forgetting to other hypotheses.
        SeqElement sequence = theUKS.CreateFirstElement(candidate.Label, percept[0]);
        SeqElement last = sequence;
        foreach (Thought symbol in percept.Skip(1))
            last = theUKS.AddElement(last, symbol);
        candidate.AddLink(_hasSymbols, sequence);
        candidate.Fire();
        return candidate;
    }

    private void DecayAndForgetCandidates(ISet<Thought> rewarded, ICollection<string> trace)
    {
        int decayed = 0, deleted = 0;
        foreach (Thought candidate in _candidateRoot.Children.ToList())
        {
            if (!candidate.isPlastic || rewarded.Contains(candidate)) continue;
            candidate.Weight = Math.Max(0, candidate.Weight - DecayPerChunk);
            decayed++;
            if (candidate.Weight <= 0.0001f)
            {
                candidate.Delete();
                deleted++;
            }
        }
        trace.Add($"Unrewarded candidates decayed: {decayed}; deleted: {deleted}");
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
        _phraseRoot = theUKS.GetOrAddThought("SegmentationPhrasePattern", "Thought");
        _hasWords = theUKS.GetOrAddThought("hasSegmentationWords", "LinkType");
        _matchKnownSuffix ??= theUKS.CreateSearchOptions(mustMatchLast: true);
        _matchKnownPrefix ??= theUKS.CreateSearchOptions(mustMatchFirst: true);

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
                $" wins={EvidenceCount(candidate, MaximalEvidence):0}]"));
        if (candidates.Length == 0) candidates = "no surviving proto-words";
        return candidates;
    }
}

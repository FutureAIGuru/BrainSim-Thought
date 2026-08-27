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
    public const float RecognitionIncrement = 0.5f;
    public const float RecognitionThreshold = 1f;
    public const float DecayPerChunk = 0.01f;
    public const float InterferencePerChunk = 0.005f;

    private Thought _candidateRoot;
    private Thought _hasSymbols;
    private Thought _exactSearch;
    private StreamReader _sequenceReader;
    private string _sequenceReaderPath;

    public int MaximumChunkSize { get; set; } = 4;
    public string LastSegmentation { get; private set; } = string.Empty;
    public string LastStatus { get; private set; } = "Enter an unsegmented line.";

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
    /// Consumes one pause-bounded observation from left to right. Each random
    /// attentional span is assembled from primitive letters or the longest
    /// recognized candidates. The percept and its shaping components are
    /// reinforced before decay and interference are applied.
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
            return LastStatus;
        }

        List<string> perceivedChunks = new();
        int position = 0;
        while (position < symbols.Count)
        {
            int maximumUnits = Math.Min(Math.Max(1, MaximumChunkSize), symbols.Count - position);
            int unitCount = Math.Clamp(SelectChunkSize(maximumUnits), 1, maximumUnits);
            List<Thought> percept = new();
            HashSet<Thought> shapingCandidates = new();
            int unitsSelected = 0;
            while (unitsSelected < unitCount && position + percept.Count < symbols.Count)
            {
                int componentPosition = position + percept.Count;
                int componentMaximum = symbols.Count - componentPosition;
                Thought component = FindGuidingCandidate(symbols, componentPosition, componentMaximum,out int componentLength);
                if (component is null)
                {
                    componentLength = 1;
                }
                else
                {
                    shapingCandidates.Add(component);
                }
                percept.AddRange(symbols.GetRange(componentPosition, componentLength));
                unitsSelected++;
            }

            Thought candidate = FindExactCandidate(percept);
            if (candidate is null)
                candidate = CreateCandidate(percept);
            else
            {
                candidate.Weight += RecognitionIncrement;
                candidate.Fire();
            }

            foreach (Thought component in shapingCandidates)
            {
                if (ReferenceEquals(component, candidate)) continue;
                component.Weight += RecognitionIncrement;
                component.Fire();
            }

            perceivedChunks.Add(GetSymbolText(percept));
            DecayInterfereAndForgetCandidates(
                percept, shapingCandidates.Append(candidate));
            position += percept.Count;
        }

        LastSegmentation = string.Join(" ", perceivedChunks);
        LastStatus = BuildStatus(LastSegmentation);
        return LastStatus;
    }

    private Thought FindGuidingCandidate(
        List<Thought> input,
        int position,
        int maximum,
        out int matchedLength)
    {
        for (int length = maximum; length >= 1; length--)
        {
            Thought candidate = FindExactCandidate(input.GetRange(position, length));
            if (candidate?.Weight < RecognitionThreshold) continue;
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
    /// Selects the next attentional span. Overridable only to make controlled
    /// experiments reproducible; normal processing samples uniformly from
    /// one through the available maximum.
    /// </summary>
    protected virtual int SelectChunkSize(int maximum)
    {
        int retVal = Random.Shared.Next(1, maximum + 1);
        return retVal;
    }

    private Thought FindExactCandidate(List<Thought> percept)
    {
        return theUKS.FindSequencesByActivation(percept, _exactSearch)
            .SelectMany(match => match.seqNode.LinksFrom)
            .Where(link => ReferenceEquals(link.LinkType, _hasSymbols) &&
                link.From?.Parents.Contains(_candidateRoot) == true)
            .Select(link => link.From)
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
        IEnumerable<Thought> protectedCandidates)
    {
        HashSet<Thought> protectedSet = protectedCandidates.ToHashSet();
        HashSet<Thought> perceptSymbols = percept.ToHashSet();
        foreach (Thought candidate in _candidateRoot.Children.ToList())
        {
            if (!candidate.isPlastic) continue;
            candidate.Weight = Math.Max(0, candidate.Weight - DecayPerChunk);
            if (!protectedSet.Contains(candidate) &&
                GetCandidateSymbols(candidate).Any(perceptSymbols.Contains))
            {
                candidate.Weight = Math.Max(
                    0, candidate.Weight - InterferencePerChunk);
            }
            if (candidate.Weight <= 0.0001f)
                candidate.Delete();
        }
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
        string candidates = string.Join(", ", _candidateRoot.Children
            .OrderByDescending(candidate => candidate.Weight)
            .ThenBy(candidate => candidate.Label, StringComparer.Ordinal)
            .Take(6)
            .Select(candidate =>
                $"{candidate.Label["candidate:".Length..]}:{candidate.Weight:0.00}"));
        if (candidates.Length == 0) candidates = "no surviving proto-words";
        return $"{perceived}    {candidates}";
    }
}

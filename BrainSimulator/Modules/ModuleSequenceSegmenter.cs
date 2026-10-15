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
using UKS;

namespace BrainSimulator.Modules;

/// <summary>
/// Retains recent input in a rolling UKS sequence.
/// Repeated pause-delimited input strengthens plastic sequence candidates.
/// </summary>
public class ModuleSequenceSegmenter : ModuleBase
{
    #region Input queue

    // Newest observation is in the mutable head. Everything behind it is stable.
    private Thought _inputQueueOwner;
    private SeqElement _inputQueueHead;
    private SeqElement _currentPointer;
    private Thought _inputPause;
    private Thought _inputOccurrenceOptions;
    private DateTime _lastInputTrim;

    public TimeSpan InputQueueLifetime { get; set; } = TimeSpan.FromSeconds(5);
    public SeqElement InputQueueHead { get { return _inputQueueHead; } }
    public SeqElement CurrentPointer { get { return _currentPointer; } }
    public Thought InputPause { get { return _inputPause; } }

    /// <summary>Queues observations only. The caller supplies PAUSE Thoughts.</summary>
    public void QueueInput(IReadOnlyList<Thought> input)
    {
        foreach (Thought observation in input)
        {
            InsertQueuedObservation(observation);
            if (ReferenceEquals(observation, _inputPause)) InsertQueuedObservation(observation);
        }
    }

    private void InsertQueuedObservation(Thought observation)
    {
        theUKS.InsertElement(_inputQueueHead, observation);
        if (_currentPointer is null) _currentPointer = _inputQueueHead.NXT;
    }

    /// <summary>Returns the next stable observation, including pauses, and advances toward the head.</summary>
    public bool TryReadQueuedInput(out Thought observation)
    {
        observation = null;
        if (_currentPointer is null || ReferenceEquals(_currentPointer, _inputQueueHead)) return false;
        observation = theUKS.GetElementValue(_currentPointer);
        SeqElement newer = theUKS.GetPreviousSequenceElement(_currentPointer);
        _currentPointer = ReferenceEquals(newer, _inputQueueHead) ? null : newer;
        return true;
    }

    /// <summary>Deletes the expired suffix, retaining the head and the initial pause.</summary>
    public void TrimInputQueue(DateTime? now = null)
    {
        DateTime cutoff = (now ?? DateTime.Now) - InputQueueLifetime;
        SeqElement previous = _inputQueueHead;
        SeqElement element = previous.NXT;
        while (element?.NXT is not null && element.CreatedTime >= cutoff)
        {
            previous = element;
            element = element.NXT;
        }
        while (element?.NXT is not null)
        {
            SeqElement next = element.NXT;
            if (ReferenceEquals(element, _currentPointer) || ReferenceEquals(element, _learnerSpanStart))
            {
                _currentPointer = null;
                _learnerSpanStart = null;
                _pendingInput.Clear();
            }
            element.NXT = null;
            element.FRST = null;
            element.RemoveLinks("VLU");
            element.Delete();
            element = next;
        }
        previous.NXT = element;
        // The head's node identity is permanent; its VLU link dates its current contents.
        Link value = _inputQueueHead.HasLink(theUKS.Labeled("VLU"), _inputQueueHead.VLU);
        if (value.CreatedTime < cutoff) _inputQueueHead.VLU = _inputPause;
    }

    /// <summary>Finds occurrence positions in this queue for a chronological input span.</summary>
    public List<SeqElement> FindInputOccurrences(IReadOnlyList<Thought> input)
    {
        List<Thought> reversed = new(input);
        reversed.Reverse();
        List<SeqElement> starts = new();
        theUKS.FindSequencesByActivation(reversed, _inputOccurrenceOptions, starts, _inputQueueHead);
        return starts;
    }

    public void ClearInputQueue()
    {
        TrimInputQueue(DateTime.MaxValue);
        _inputQueueHead.VLU = _inputPause;
        ResetInputQueueState();
        _pendingInput.Clear();
    }

    private void ResetInputQueueState()
    {
        _currentPointer = null;
        _learnerSpanStart = null;
        _lastInputTrim = DateTime.Now;
    }

    #endregion

    private readonly List<Thought> _pendingInput = new();
    private Thought _hasSequence;
    private Thought _patternSearch;
    private DateTime _lastCandidateDecay;
    public Thought PatternRoot { get; set; }
    public string PatternPrefix { get; set; }
    public int SingleUseMaximumLength { get; set; } = 3;
    public int MinimumOccurrences { get; set; } = 2;
    public TimeSpan RepetitionWindow { get; set; } = TimeSpan.FromSeconds(5);
    public int CandidateCapacity { get; set; } = 30;
    public int MaximumProposalsPerEpisode { get; set; } = 3;
    public float InitialStrength { get; set; } = 1;
    public float Reinforcement { get; set; } = 1;
    public float CompetitionPenalty { get; set; } = 0.1f;
    public float CompetitionStrengthScale { get; set; } = 0.1f;
    public float MaximumCompetitionPenalty { get; set; } = 1;
    public float ConsolidationStrength { get; set; } = 3;
    public float CandidateDecayPerSecond { get; set; } = 0.1f;
    public float LearnedDecayPerSecond { get; set; } = 0.05f;
    public float DecayThreshold { get; set; } = 3.5f;
    public string LastStatus { get; private set; } = "Awaiting input observations.";

    public override void Fire()
    {
        Init();
        DateTime now = DateTime.Now;
        if (PatternRoot is not null) DecayCandidates(now - _lastCandidateDecay);
        _lastCandidateDecay = now;
        if (now - _lastInputTrim >= TimeSpan.FromSeconds(1))
        {
            _lastInputTrim = now;
            TrimInputQueue(now);
        }
        if (_currentPointer is not null) ProcessQueuedInput();
        UpdateDialog();
    }

    public override void Initialize()
    {
    }

    public override void UKSInitializedNotification()
    {
        ResetInputQueueState();
        _lastCandidateDecay = DateTime.Now;
        _pendingInput.Clear();
        if (theUKS is null) GetUKS();
        theUKS.GetOrAddThought("LanguageElement","Thought");
        theUKS.GetOrAddThought("Word", "LanguageElement");
        _hasSequence = theUKS.GetOrAddThought("spelled", "LinkType");
        _patternSearch = theUKS.Labeled("ExactSequenceSearch");
        _inputPause = theUKS.GetOrAddThought("PAUSE", "Thought");
        _inputOccurrenceOptions = theUKS.CreateSearchOptions();
        _inputQueueOwner = theUKS.GetOrAddThought("InputQueue*", "Thought");
        _inputQueueHead = theUKS.CreateFirstElement(_inputQueueOwner.Label, _inputPause);
        _inputQueueOwner.AddLink("hasInputQueue", _inputQueueHead);
    }

    /// <summary>Emphasis applies to episodes completed by this call; the caller supplies their pauses.</summary>
    public string InputReceived(IReadOnlyList<Thought> input, float emphasis = 0)
    {
        QueueInput(input);
        string result = ProcessQueuedInput(emphasis);
        return result;
    }

    // The learner consumes stable observations separately from queue mechanics.
    // Queue consumers can instead call TryReadQueuedInput directly.
    private SeqElement _learnerSpanStart;
    public string ProcessQueuedInput(float emphasis = 0)
    {
        bool completed = false;
        while (_currentPointer is not null)
        {
            SeqElement element = _currentPointer;
            if (!TryReadQueuedInput(out Thought observation)) break;
            if (ReferenceEquals(observation, _inputPause))
            {
                _learnerSpanStart = null;
                if (_pendingInput.Count == 0) continue;
                List<Thought> episode = new(_pendingInput);
                _pendingInput.Clear();
                ProcessEpisode(episode, emphasis, element.NXT);
                completed = true;
            }
            else
            {
                _learnerSpanStart ??= element;
                _pendingInput.Add(observation);
            }
        }
        if (!completed)
            LastStatus = $"Received {_pendingInput.Count} stable observation(s); awaiting more input.";
        string result = LastStatus;
        return result;
    }

    private void ProcessEpisode(List<Thought> input, float emphasis, SeqElement newest)
    {
        List<string> status = new();
        HashSet<Thought> strengthened = new();
        bool wholeAdmitted = false;
        List<Thought> competitors = new();
        var parts = FindExistingSubPatterns(input, competitors);
        foreach (var part in parts) competitors.Remove(part.pattern);
        // A new candidate must gather repetition evidence before competing with learned patterns.
        for (int index = competitors.Count - 1; index >= 0; index--)
            if (competitors[index].isPlastic) competitors.RemoveAt(index);
        // Only a complete, learned explanation can suppress alternatives during recognition.
        foreach (var part in parts)
            if (part.pattern is null || part.pattern.isPlastic) competitors.Clear();
        Dictionary<Thought, float> penalties = new();
        foreach (Thought competitor in competitors)
            penalties[competitor] = GetCompetitionPenalty(input, parts, competitor);
        // Pauses define a whole-pattern proposal even when smaller patterns cover it.
        if (FindExistingPattern(input) is null)
        {
            status.Add(LearnPattern(input, null, emphasis));
            Thought whole = FindExistingPattern(input);
            if (whole is not null)
            {
                strengthened.Add(whole);
                wholeAdmitted = true;
            }
        }
        foreach (var part in parts)
        {
            // An earlier gap in this episode may already have created this candidate.
            Thought pattern = part.pattern ?? FindExistingPattern(part.input);
            if (pattern is not null && strengthened.Contains(pattern)) continue;
            bool reinforce = part.input.Count == input.Count || emphasis > 0 ||
                (pattern is not null && !pattern.isPlastic && !wholeAdmitted);
            status.Add(LearnPattern(part.input, pattern, emphasis, reinforce));
            if (pattern is null || reinforce)
            {
                Thought learned = FindExistingPattern(part.input);
                if (learned is not null) strengthened.Add(learned);
            }
        }
        foreach (List<Thought> proposal in FindRepeatedPatterns(input, newest))
        {
            Thought pattern = FindExistingPattern(proposal);
            if (competitors.Contains(pattern)) continue; // Losing recognition must not earn a discovery reward.
            if (pattern is not null && strengthened.Contains(pattern)) continue;
            status.Add(LearnPattern(proposal, pattern, emphasis));
            Thought learned = FindExistingPattern(proposal);
            if (learned is not null) strengthened.Add(learned);
        }
        foreach (Thought competitor in competitors)
        {
            competitor.Weight -= penalties[competitor];
            if (competitor.Weight < InitialStrength) competitor.isPlastic = true;
            if (competitor.Weight <= 0) competitor.Delete();
        }
        LastStatus = string.Join("; ", status);
    }

    private float GetCompetitionPenalty(List<Thought> input,
        List<(Thought pattern, List<Thought> input)> parts, Thought competitor)
    {
        if (CompetitionStrengthScale == 0) return CompetitionPenalty;
        float penalty = 0;
        var values = theUKS.FlattenSequence((SeqElement)competitor.GetTargetOfFirstLinkOfType(_hasSequence));
        for (int start = 0; start <= input.Count - values.Count; start++)
        {
            bool matches = true;
            for (int index = 0; index < values.Count; index++)
                if (!ReferenceEquals(input[start + index], values[index])) { matches = false; break; }
            if (!matches) continue;
            int position = 0;
            float support = float.MaxValue;
            foreach (var part in parts)
            {
                int end = position + part.input.Count;
                if (position < start + values.Count && end > start)
                    support = Math.Min(support, part.pattern.Weight);
                position = end;
            }
            // The weakest overlapping winner must support suppressing this alternative.
            float difference = Math.Max(0, support - competitor.Weight);
            penalty = Math.Max(penalty, Math.Min(MaximumCompetitionPenalty, CompetitionStrengthScale * difference));
        }
        return penalty;
    }

    private string LearnPattern(List<Thought> input, Thought pattern, float emphasis, bool reinforce = true)
    {
        if (pattern is null)
        {
            int occurrences = 0, candidates = 0;
            DateTime cutoff = DateTime.Now - RepetitionWindow;
            foreach (SeqElement start in FindInputOccurrences(input))
                if (start.CreatedTime >= cutoff) occurrences++;
            foreach (Thought existing in PatternRoot.Children)
                if (existing.isPlastic) candidates++;
            if ((emphasis <= 0 && input.Count > SingleUseMaximumLength && occurrences < MinimumOccurrences) || candidates >= CandidateCapacity)
            {
                string rejected = $"{CreatePatternLabel(input)}: no candidate admitted";
                return rejected;
            }
            string label = CreatePatternLabel(input);
            if (theUKS.Labeled(label) is not null) label += "*";
            pattern = theUKS.GetOrAddThought(label, PatternRoot);
            pattern.isPlastic = true;
            pattern.Weight = InitialStrength + emphasis;
            theUKS.AddSequenceAndLink(pattern, _hasSequence, input, allowCompression: false);
        }
        else if (reinforce) pattern.Weight += Reinforcement + emphasis;
        if (pattern.Weight >= ConsolidationStrength) pattern.isPlastic = false;
        pattern.Fire();
        string result = $"{pattern.Label}: strength {pattern.Weight:0.##}, " +
            (pattern.isPlastic ? "candidate" : "non-plastic");
        return result;
    }

    /// <summary>Ranks repeated spans by support across contexts; confirmed boundaries allow repeat rewards.</summary>
    private List<List<Thought>> FindRepeatedPatterns(List<Thought> input, SeqElement newest)
    {
        List<List<Thought>> proposals = new();
        List<(List<Thought> input, int support, int occurrences)> ranked = new();
        // Single observations need a separate segment and cannot borrow evidence from a containing pattern.
        bool[] coveredByLargerPattern = new bool[input.Count];
        bool[] separateInput = new bool[input.Count];
        int position = 0;
        foreach (var part in FindExistingSubPatterns(input))
        {
            if (part.input.Count == 1) separateInput[position] = true;
            position += part.input.Count;
        }
        HashSet<SeqElement> currentEpisode = new();
        SeqElement element = newest;
        for (int index = 0; index < input.Count; index++, element = element.NXT) currentEpisode.Add(element);
        DateTime cutoff = DateTime.Now - RepetitionWindow;
        for (int length = input.Count; length >= 1; length--)
        {
            for (int start = 0; start <= input.Count - length; start++)
            {
                List<Thought> span = input.GetRange(start, length);
                Thought pattern = FindExistingPattern(span);
                if (length > 1 && pattern is not null)
                    for (int index = start; index < start + length; index++) coveredByLargerPattern[index] = true;
                if (length == 1 && (!separateInput[start] || coveredByLargerPattern[start])) continue;
                int occurrences = 0;
                Thought before = start == 0 ? _inputPause : input[start - 1];
                Thought after = start + length == input.Count ? _inputPause : input[start + length];
                bool leftBoundary = start == 0, rightBoundary = start + length == input.Count;
                HashSet<Thought> leftContexts = new() { before };
                HashSet<Thought> rightContexts = new() { after };
                foreach (SeqElement occurrence in FindInputOccurrences(span))
                {
                    if (occurrence.CreatedTime < cutoff) continue;
                    occurrences++;
                    if (currentEpisode.Contains(occurrence)) continue;
                    SeqElement oldest = occurrence;
                    for (int index = 1; index < length; index++) oldest = oldest.NXT;
                    SeqElement newer = theUKS.GetPreviousSequenceElement(occurrence);
                    if (newer is null) continue; // The mutable head has no following observation yet.
                    Thought preceding = oldest.NXT.VLU;
                    Thought following = newer.VLU;
                    leftContexts.Add(preceding);
                    rightContexts.Add(following);
                    // An old pause does not validate a boundary inside the current input.
                    if (!ReferenceEquals(preceding, _inputPause))
                    {
                        leftBoundary |= !ReferenceEquals(before, preceding);
                    }
                    if (!ReferenceEquals(following, _inputPause))
                    {
                        rightBoundary |= !ReferenceEquals(after, following);
                    }
                }
                if (occurrences < MinimumOccurrences || !leftBoundary || !rightBoundary) continue;
                if (pattern is not null && !pattern.isPlastic) continue;
                int support = leftContexts.Count * rightContexts.Count;
                int rank = 0;
                while (rank < ranked.Count && (ranked[rank].support > support ||
                    (ranked[rank].support == support && ranked[rank].occurrences >= occurrences))) rank++;
                if (rank >= MaximumProposalsPerEpisode) continue;
                ranked.Insert(rank, (span, support, occurrences));
                if (ranked.Count > MaximumProposalsPerEpisode) ranked.RemoveAt(MaximumProposalsPerEpisode);
            }
        }
        foreach (var proposal in ranked) proposals.Add(proposal.input);
        return proposals;
    }

    /// <summary>Prefers coverage, fewer recognized units, then combined strength. Null patterns mark gaps.</summary>
    public List<(Thought pattern, List<Thought> input)> FindExistingSubPatterns(List<Thought> input, List<Thought> competitors = null)
    {
        Thought[] patterns = new Thought[input.Count];
        int[] lengths = new int[input.Count];
        int[] coverage = new int[input.Count + 1];
        int[] units = new int[input.Count + 1];
        float[] strength = new float[input.Count + 1];
        for (int start = input.Count - 1; start >= 0; start--)
        {
            lengths[start] = 1;
            coverage[start] = coverage[start + 1];
            units[start] = units[start + 1];
            strength[start] = strength[start + 1];
            for (int length = 1; length <= input.Count - start; length++)
            {
                Thought pattern = FindExistingPattern(input.GetRange(start, length));
                if (pattern is null) continue;
                if (competitors is not null && !competitors.Contains(pattern)) competitors.Add(pattern);
                int covered = length + coverage[start + length];
                int count = 1 + units[start + length];
                float combined = pattern.Weight + strength[start + length];
                bool better = covered > coverage[start];
                if (covered == coverage[start])
                {
                    better = count < units[start] || (count == units[start] && combined > strength[start]);
                    bool tied = count == units[start] && combined == strength[start];
                    better |= tied && (patterns[start] is null || length > lengths[start]);
                }
                if (!better) continue;
                patterns[start] = pattern;
                lengths[start] = length;
                coverage[start] = covered;
                units[start] = count;
                strength[start] = combined;
            }
        }
        List<(Thought pattern, List<Thought> input)> parts = new();
        for (int start = 0; start < input.Count;)
        {
            int length = lengths[start];
            if (patterns[start] is null)
                while (start + length < input.Count && patterns[start + length] is null) length++;
            parts.Add((patterns[start], input.GetRange(start, length)));
            start += length;
        }
        return parts;
    }

    private Thought FindExistingPattern(List<Thought> input)
    {
        Thought pattern = null;
        var matches = theUKS.FindSequencesByActivation(input, _patternSearch);
        foreach (var match in matches)
        {
            foreach (Link link in match.seqNode.LinksFrom)
            {
                if (!ReferenceEquals(link.LinkType, _hasSequence) || !link.From.Parents.Contains(PatternRoot)) continue;
                pattern = link.From;
                break;
            }
            if (pattern is not null) break;
        }
        return pattern;
    }

    public void DecayCandidates(TimeSpan elapsed)
    {
        float decay = CandidateDecayPerSecond * (float)elapsed.TotalSeconds;
        foreach (Thought candidate in new List<Thought>(PatternRoot.Children))
        {
            if (candidate.Weight >= DecayThreshold) continue;
            if (candidate.isPlastic) candidate.Weight -= decay;
            else
            {
                candidate.Weight *= (float)Math.Exp(-LearnedDecayPerSecond * elapsed.TotalSeconds);
                if (candidate.Weight < InitialStrength) candidate.isPlastic = true;
            }
            if (candidate.Weight <= 0) candidate.Delete();
        }
    }

    public string CreatePatternLabel(IReadOnlyList<Thought> input)
    {
        string label = PatternPrefix;
        foreach (Thought observation in input)
        {
            string part = observation.Label;
            if (part.StartsWith(UKS.UKS.CharacterPrefix, StringComparison.OrdinalIgnoreCase))
                part = part[UKS.UKS.CharacterPrefix.Length..].ToLowerInvariant();
            label += part;
        }
        return label;
    }
}

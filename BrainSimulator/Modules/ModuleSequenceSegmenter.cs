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
using System.Linq;
using System.Threading;
using UKS;

namespace BrainSimulator.Modules;

/// <summary>
/// Retains recent input in a rolling UKS sequence.
/// Repeated spans propose candidates; all recognized patterns can strengthen.
/// </summary>
public class ModuleSequenceSegmenter : ModuleBase
{
    #region Input queue

    // Oldest observation is at the head. Stream elements have no FRST links.
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
            InsertQueuedObservation(observation);
    }

    private void InsertQueuedObservation(Thought observation)
    {
        SeqElement tail = _inputQueueHead;
        while (tail.NXT is not null) tail = tail.NXT;
        SeqElement added = theUKS.AddElement(tail, observation);
        _currentPointer ??= added;
    }

    /// <summary>Returns the next observation and advances toward the tail.</summary>
    public bool TryReadQueuedInput(out Thought observation)
    {
        observation = null;
        if (_currentPointer is null) return false;
        observation = _currentPointer.VLU;
        _currentPointer = _currentPointer.NXT;
        return true;
    }

    /// <summary>Pops expired elements from the head; starts a fresh queue if all have expired.</summary>
    public void TrimInputQueue(DateTime? now = null)
    {
        DateTime cutoff = (now ?? DateTime.Now) - InputQueueLifetime;
        SeqElement oldHead = _inputQueueHead;
        while (_inputQueueHead is not null && _inputQueueHead.CreatedTime < cutoff)
        {
            if (ReferenceEquals(_inputQueueHead, _currentPointer)) _currentPointer = _inputQueueHead.NXT;
            if (ReferenceEquals(_inputQueueHead, _learnerSpanStart)) _learnerSpanStart = null;
            _inputQueueHead = theUKS.RemoveFirstSequenceElement(_inputQueueHead);
        }
        if (_inputQueueHead is null) ResetInputQueueState();
        else if (!ReferenceEquals(oldHead, _inputQueueHead)) _inputQueueOwner.AddLink("hasInputQueue", _inputQueueHead);
    }

    /// <summary>Finds occurrence positions in this queue for a chronological input span.</summary>
    public List<SeqElement> FindInputOccurrences(IReadOnlyList<Thought> input, SeqElement before = null)
    {
        var matches = theUKS.FindSequencesByActivation(new List<Thought>(input), _inputOccurrenceOptions, getFirstElements: false);
        var positions = matches.Select(match => match.seqNode).ToHashSet();
        List<SeqElement> starts = new();
        for (SeqElement element = _inputQueueHead; element is not null && !ReferenceEquals(element, before); element = element.NXT)
            if (positions.Contains(element)) starts.Add(element);
        return starts;
    }

    public void ClearInputQueue()
    {
        TrimInputQueue(DateTime.MaxValue);
    }

    private void ResetInputQueueState()
    {
        _inputQueueHead = new SeqElement { Label = _inputQueueOwner.Label + "-seq*", VLU = _inputPause };
        _inputQueueOwner.AddLink("hasInputQueue", _inputQueueHead);
        _currentPointer = _inputQueueHead;
        _learnerSpanStart = null;
        _lastInputTrim = DateTime.Now;
    }

    #endregion

    private Thought _hasSequence;
    private Thought _usedBy;
    private DateTime _lastCandidateDecay;
    public Thought PatternRoot { get; set; }
    public string PatternPrefix { get; set; }
    public int MinimumOccurrences { get; set; } = 2;
    public int CandidateCapacity { get; set; } = 40;
    public int MaximumProposalsPerEpisode { get; set; } = 2;
    public float InitialStrength { get; set; } = 1;
    public float Reinforcement { get; set; } = 1;
    // Optional diagnostic counts of surviving occurrences, independent of once-per-episode reinforcement.
    public Dictionary<Thought, int> RecognitionCounts { get; set; }
    public float ConsolidationStrength { get; set; } = 25;
    public float CandidateDecayPerSecond { get; set; } = 0.1f;
    public string LastStatus { get; private set; } = "Awaiting input observations.";
    // Optional diagnostics only; these do not affect matching or learning decisions.
    public bool ExplainLearning { get; set; }
    public string LastLearningDetails { get; private set; } = string.Empty;
    private readonly List<string> _learningDetails = new();
    private readonly object _inputLock = new();


    public override void Fire()
    {
        // The UI engine must not wait for a background phrase or trim its live sequence.
        if (!Monitor.TryEnter(_inputLock)) return;
        try
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
        }
        finally { Monitor.Exit(_inputLock); }
        UpdateDialog();
    }

    public override void Initialize()
    {
    }

    public override void UKSInitializedNotification()
    {
        _lastCandidateDecay = DateTime.Now;
        if (theUKS is null) GetUKS();
        theUKS.GetOrAddThought("LanguageElement","Thought");
        theUKS.GetOrAddThought("Word", "LanguageElement");
        _hasSequence = theUKS.GetOrAddThought("spelled", "LinkType");
        _usedBy = theUKS.GetOrAddThought("usedBy", "LinkType");
        _inputPause = theUKS.GetOrAddThought("PAUSE", "Thought");
        _inputOccurrenceOptions = theUKS.CreateSearchOptions();
        theUKS.GetOrAddThought("hasInputQueue", "LinkType");
        _inputQueueOwner = theUKS.GetOrAddThought("InputQueue*", "Thought");
        ResetInputQueueState();
    }

    /// <summary>Emphasis applies to episodes completed by this call; the caller supplies their pauses.</summary>
    public string InputReceived(IReadOnlyList<Thought> input, float emphasis = 0)
    {
        lock (_inputLock)
        {
            QueueInput(input);
            string result = ProcessQueuedInput(emphasis);
            return result;
        }
    }

    // The learner consumes observations separately from queue mechanics.
    // Queue consumers can instead call TryReadQueuedInput directly.
    private SeqElement _learnerSpanStart;
    public string ProcessQueuedInput(float emphasis = 0)
    {
        bool completed = false;
        while (_currentPointer is not null)
        {
            SeqElement element = _currentPointer;
            TryReadQueuedInput(out Thought observation);
            if (!ReferenceEquals(observation, _inputPause)) _learnerSpanStart ??= element;
            else if (_learnerSpanStart is not null)
            {
                ProcessEpisode(_learnerSpanStart, element, emphasis);
                _learnerSpanStart = null;
                completed = true;
            }
        }
        if (!completed)
        {
            LastStatus = "Awaiting more input.";
            LastLearningDetails = string.Empty;
        }
        string result = LastStatus;
        return result;
    }

    private void ProcessEpisode(SeqElement first, SeqElement pause, float emphasis)
    {
        _learningDetails.Clear();
        FindExistingSubPatterns(first);

        HashSet<SeqElement> recognizedSequences = new();
        HashSet<Thought> activeThoughts = ProcessExistingMatches(first, pause, recognizedSequences, out bool fullyRecognized);
        foreach (Thought thought in activeThoughts) thought.Fire();
        List<string> status = ReinforceCandidates(activeThoughts, emphasis);

        if (!fullyRecognized) status.AddRange(ProcessPotentialCandidates(first, pause, recognizedSequences, emphasis));
        else
        {
            if (ExplainLearning) _learningDetails.Add("No proposals: complete coverage by established patterns or an independently bounded candidate.");
        }
        LastStatus = string.Join("; ", status);
        LastLearningDetails = string.Join(Environment.NewLine, _learningDetails);
    }

    /// <summary>Process existing matches only. Plastic candidates and established patterns share the same tags.</summary>
    private HashSet<Thought> ProcessExistingMatches(SeqElement first, SeqElement pause, HashSet<SeqElement> recognizedSequences, out bool fullyRecognized)
    {
        HashSet<Thought> activeThoughts = new(); // Several occurrences can activate the same Thought only once.
        List<(SeqElement sequence, int start, int end)> activeOccurrences = new(); // End is exclusive.
        int position = 0;
        for (SeqElement element = first; element != pause; element = element.NXT, position++)
        {
            var tags = element.LinksTo.Where(link => ReferenceEquals(link.LinkType, _usedBy)).ToList();
            // Case 1: an existing candidate or pattern excites an occurrence, pending inhibition.
            // Tags cover every matched element, so recover each complete occurrence's start and end here.
            foreach (Link tag in tags.Where(link => ((SeqElement)link.To).FRST is not null))
            {
                SeqElement sequence = (SeqElement)tag.To;
                recognizedSequences.Add(sequence);
                SeqElement current = element, expected = sequence;
                int end = position;
                while (expected is not null && current != pause && ReferenceEquals(current.VLU, expected.VLU) &&
                    current.HasLink(_usedBy, sequence) is not null)
                {
                    current = current.NXT;
                    expected = expected.NXT;
                    end++;
                }
                if (expected is null) activeOccurrences.Add((sequence, position, end));
            }
        }

        // First establish boundaries using non-plastic patterns only.
        var coverageOccurrences = activeOccurrences.Where(match => match.sequence.LinksFrom.Any(link =>
            ReferenceEquals(link.LinkType, _hasSequence) && !link.From.isPlastic)).ToList();
        int unreachable = position + 1;
        int[] fromStart = Enumerable.Repeat(unreachable, position + 1).ToArray();
        int[] toEnd = Enumerable.Repeat(unreachable, position + 1).ToArray();
        fromStart[0] = toEnd[position] = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int boundary = 0; boundary < position; boundary++)
                foreach (var match in coverageOccurrences.Where(match => match.start == boundary))
                    if (fromStart[boundary] < unreachable)
                        fromStart[match.end] = Math.Min(fromStart[match.end], fromStart[boundary] + 1);
            for (int boundary = position; boundary > 0; boundary--)
                foreach (var match in coverageOccurrences.Where(match => match.end == boundary))
                    if (toEnd[boundary] < unreachable)
                        toEnd[match.start] = Math.Min(toEnd[match.start], toEnd[boundary] + 1);
            // Prefer an established complete split before admitting any plastic alternatives.
            if (pass == 1 || fromStart[position] < unreachable) break;
            // A candidate needs established context, with a pause also allowed as one boundary.
            // Exclude a whole-episode candidate; freeze choices so plastic fragments cannot support each other.
            // A plastic single-symbol orphan at a pause must not suppress its longer expansion (cat|s versus cats).
            var boundedCandidates = activeOccurrences.Where(match =>
                (match.start > 0 && match.end < position || match.end - match.start > 1 && (match.start > 0 || match.end < position)) &&
                fromStart[match.start] < unreachable && toEnd[match.end] < unreachable && !coverageOccurrences.Contains(match)).ToList();
            if (boundedCandidates.Count == 0) break;
            coverageOccurrences.AddRange(boundedCandidates);
        }
        fullyRecognized = position > 0 && fromStart[position] < unreachable;

        List<(SeqElement sequence, int start, int end)> supportedOccurrences = new();
        if (fullyRecognized)
        {
            // Keep every match in any minimum-unit complete split; equal-count alternatives share support.
            // Forward/backward minimum counts avoid enumerating combinations.
            foreach (var match in coverageOccurrences)
                if (fromStart[match.start] + 1 + toEnd[match.end] == fromStart[position]) supportedOccurrences.Add(match);
        }
        else
        {
            // Propagation and inhibition use all existing occurrences, without proposing or creating anything.
            // Case 3: abutting occurrences extend one another's coverage.
            // The two sweeps simulate propagation along the input; they do not enumerate possible segmentations.
            int[] coverageStart = Enumerable.Range(0, position + 1).ToArray();
            int[] coverageEnd = Enumerable.Range(0, position + 1).ToArray();
            for (int boundary = 0; boundary <= position; boundary++)
                foreach (var match in activeOccurrences.Where(match => match.start == boundary))
                    coverageStart[match.end] = Math.Min(coverageStart[match.end], coverageStart[match.start]);
            for (int boundary = position; boundary >= 0; boundary--)
                foreach (var match in activeOccurrences.Where(match => match.end == boundary))
                    coverageEnd[match.start] = Math.Max(coverageEnd[match.start], coverageEnd[match.end]);

            // Case 4: inhibit an occurrence when another explanation covers its supported region plus more input.
            foreach (var match in activeOccurrences)
            {
                int start = coverageStart[match.start], end = coverageEnd[match.end];
                bool betterCoverage = activeOccurrences.Any(other => coverageStart[other.start] <= start && coverageEnd[other.end] >= end &&
                    (coverageStart[other.start] < start || coverageEnd[other.end] > end));
                if (!betterCoverage) supportedOccurrences.Add(match);
            }
        }
        // Case 5: a surviving longer occurrence inhibits occurrences contained within it.
        // With full supported coverage, preserve alternative complete explanations instead.
        foreach (var match in supportedOccurrences)
        {
            bool contained = !fullyRecognized && supportedOccurrences.Any(other => other.start <= match.start && other.end >= match.end &&
                (other.start < match.start || other.end > match.end));
            if (contained) continue;
            // An independent occurrence can still activate the same Thought even if another occurrence was inhibited.
            foreach (Link owner in match.sequence.LinksFrom.Where(link => ReferenceEquals(link.LinkType, _hasSequence)))
            {
                bool firstActivation = activeThoughts.Add(owner.From);
                if (ExplainLearning && firstActivation)
                {
                    string reason = match.start == 0 && match.end == position ? "standalone match" :
                        fullyRecognized ? $"complete coverage with established boundaries; minimum {fromStart[position]} units" :
                        match.start == 0 || match.end == position ? "phrase boundary; partial coverage" : "internal match; partial coverage";
                    _learningDetails.Add($"{owner.From.Label}: selected - {reason}.");
                }
                if (RecognitionCounts is null) continue;
                RecognitionCounts.TryGetValue(owner.From, out int count);
                RecognitionCounts[owner.From] = count + 1;
            }
        }

        if (ExplainLearning)
        {
            HashSet<Thought> reported = new(activeThoughts);
            foreach (var match in activeOccurrences)
                foreach (Link owner in match.sequence.LinksFrom.Where(link => ReferenceEquals(link.LinkType, _hasSequence)))
                    if (reported.Add(owner.From))
                    {
                        string reason = fullyRecognized ? coverageOccurrences.Contains(match) ? "complete split requires more units" :
                            "not admitted to a complete split supported by established boundaries" :
                            supportedOccurrences.Contains(match) ? "contained by a longer surviving match" : "another explanation covers more input";
                        _learningDetails.Add($"{owner.From.Label}: no reinforcement - {reason}.");
                    }
        }
        return activeThoughts;
    }

    /// <summary>Propose repeated queue spans and admit candidates independently of existing-match processing.</summary>
    private List<string> ProcessPotentialCandidates(SeqElement first, SeqElement pause, HashSet<SeqElement> recognizedSequences, float emphasis)
    {
        // New hypotheses are considered only after existing matches have been processed.
        // Reinforcement may have consolidated a candidate, freeing a plastic slot.
        List<string> status = new();
        List<(List<Thought> input, int support, string reason)> proposals = new();
        List<Thought> episode = new(), unrecognized = new(), gap = new();
        SeqElement orphan = null;
        List<Thought> boundedGap = null;
        bool establishedNeighbor = false;
        for (SeqElement element = first; element != pause; element = element.NXT)
        {
            var tags = element.LinksTo.Where(link => ReferenceEquals(link.LinkType, _usedBy)).ToList();
            episode.Add(element.VLU);
            if (!tags.Any(link => ((SeqElement)link.To).FRST is not null))
            {
                unrecognized.Add(element.VLU);
                gap.Add(element.VLU);
                orphan = element;
            }
            else
            {
                establishedNeighbor = tags.Any(link => ((SeqElement)link.To).FRST is not null &&
                    link.To.LinksFrom.Any(owner => ReferenceEquals(owner.LinkType, _hasSequence) && !owner.From.isPlastic));
                // A leading pause can supply one boundary when the right neighbor is established.
                if (gap.Count > 1 && (gap.Count < episode.Count - 1 || establishedNeighbor)) boundedGap = new List<Thought>(gap);
                gap.Clear();
            }
            // Case 2: repeated input -> excite a proposal where agreeing queue occurrences diverge.
            // Keep different earlier occurrences separate; unrelated character hits must not join a span.
            List<SeqElement> supportingPositions = new();
            SeqElement previous = theUKS.GetPreviousSequenceElement(element);
            foreach (Link tag in tags.Where(link => ((SeqElement)link.To).FRST is null))
            {
                SeqElement earlier = theUKS.GetPreviousSequenceElement((SeqElement)tag.To);
                if (previous is not null && previous.VLU != _inputPause && earlier is not null &&
                    previous.HasLink(_usedBy, earlier) is not null) continue; // Already part of a run starting earlier.
                supportingPositions.Add((SeqElement)tag.To);
            }
            List<Thought> span = new();
            SeqElement currentInput = element;
            while (supportingPositions.Count > 0 && currentInput != pause)
            {
                int support = supportingPositions.Count;
                span.Add(currentInput.VLU);
                currentInput = currentInput.NXT;
                for (int index = supportingPositions.Count - 1; index >= 0; index--)
                {
                    SeqElement next = supportingPositions[index].NXT;
                    if (currentInput == pause || next is null || next.VLU == _inputPause || currentInput.HasLink(_usedBy, next) is null)
                        supportingPositions.RemoveAt(index);
                    else supportingPositions[index] = next;
                }
                // A fall in support suggests an end boundary. Include this current occurrence in the minimum count.
                if (supportingPositions.Count == support || support + 1 + emphasis < MinimumOccurrences) continue;
                if (span.Count < 2) continue; // Single symbols are inputs, not learned sequences.
                // Several positions can excite the same proposal; retain its strongest support, rather than summing it.
                int proposalIndex = proposals.FindIndex(proposal => proposal.input.SequenceEqual(span));
                if (proposalIndex < 0) proposals.Add((new List<Thought>(span), support, "repetition in recent input"));
                else if (support > proposals[proposalIndex].support) proposals[proposalIndex] = (proposals[proposalIndex].input, support, proposals[proposalIndex].reason);
            }
        }

        // The ending pause can also bound a gap after an established pattern.
        if (gap.Count > 1 && establishedNeighbor) boundedGap = gap;
        // Prefer explaining the sole unknown gap over proposing the whole phrase again.
        // Emphasis strengthens that gap; it must not also nominate the whole episode.
        bool hasBoundedGap = boundedGap is not null && boundedGap.Count == unrecognized.Count;
        if (hasBoundedGap)
        {
            proposals.Clear();
            proposals.Add((boundedGap, 1, "sole gap bounded by recognized patterns or a pause"));
        }

        // An orphan may signal a wrong neighboring boundary: also try absorbing the shortest adjacent match.
        if (unrecognized.Count == 1)
        {
            proposals.Clear();
            proposals.Add((unrecognized, 1, "sole unrecognized element"));
            List<Thought> expansion = null;
            SeqElement before = theUKS.GetPreviousSequenceElement(orphan);
            foreach (SeqElement neighbor in new[] { before, orphan.NXT })
            {
                if (neighbor is null || neighbor.VLU == _inputPause) continue;
                foreach (Link tag in neighbor.LinksTo.Where(link => ReferenceEquals(link.LinkType, _usedBy) &&
                    ((SeqElement)link.To).FRST is not null))
                {
                    List<Thought> extended = new(theUKS.FlattenSequence((SeqElement)tag.To));
                    if (neighbor == before) extended.Add(orphan.VLU);
                    else extended.Insert(0, orphan.VLU);
                    if (expansion is null || extended.Count < expansion.Count) expansion = extended;
                }
            }
            if (expansion is not null) proposals.Add((expansion, 1, "shortest expansion of sole orphan"));
        }
        else if (!hasBoundedGap && emphasis > 0 && 1 + emphasis >= MinimumOccurrences && episode.Count > 1)
            proposals.Add((episode, (int)emphasis, "emphasized input")); // Emphasis supplies extra hits without earlier queue matches.

        // Case 6: remove known sequences before choosing one frequent and one longest nomination.
        for (int index = proposals.Count - 1; index >= 0; index--)
            if (recognizedSequences.Any(sequence => theUKS.FlattenSequence(sequence).SequenceEqual(proposals[index].input)))
                proposals.RemoveAt(index);
        if (proposals.Count == 0) return status;
        proposals.Sort((firstInput, secondInput) =>
        {
            int order = secondInput.support.CompareTo(firstInput.support);
            if (order == 0) order = secondInput.input.Count.CompareTo(firstInput.input.Count);
            return order;
        });
        int longest = 0;
        for (int index = 1; index < proposals.Count; index++)
            if (proposals[index].input.Count > proposals[longest].input.Count) longest = index;
        List<int> nominations = new() { 0 }; // Most frequent eligible proposal.
        if (unrecognized.Count == 1 && proposals.Count > 1) nominations.Add(1); // Expansion first, orphan second.
        else if (longest != 0) nominations.Add(longest); // Same winner in both routes consumes only one slot.
        int created = 0;
        foreach (int index in nominations)
        {
            if (created >= MaximumProposalsPerEpisode || PatternRoot.Children.Count(thought => thought.isPlastic) >= CandidateCapacity)
            {
                if (ExplainLearning) _learningDetails.Add("Further proposals blocked by proposal limit or candidate capacity.");
                break;
            }
            status.Add(CreateCandidate(proposals[index].input, emphasis));
            if (ExplainLearning) _learningDetails.Add($"{status[^1]} - created from {proposals[index].reason}; support {proposals[index].support}.");
            created++;
        }
        return status;
    }


    /// <summary>Tags complete stored patterns and partial matches in the earlier queue.</summary>
    public void FindExistingSubPatterns(SeqElement first)
    {
        // Determine earlier positions once, instead of walking the queue again for every hit.
        HashSet<SeqElement> earlierInput = new();
        for (SeqElement element = _inputQueueHead; element is not null && !ReferenceEquals(element, first); element = element.NXT)
            earlierInput.Add(element);
        SeqElement pause = first;
        while (pause is not null && !ReferenceEquals(pause.VLU, _inputPause)) pause = pause.NXT;
        var matches = theUKS.FindSequenceSpansByActivation(first, pause, _inputOccurrenceOptions);
        foreach (var match in matches)
        {
            SeqElement target = match.match;
            bool stream = target.FRST is null;
            if (stream && !earlierInput.Contains(target)) continue;
            for (SeqElement element = match.start; ; element = element.NXT)
            {
                element.AddLink(_usedBy, target);
                if (ReferenceEquals(element, match.end)) break;
                if (stream) target = target.NXT;
            }
        }
    }

    /// <summary>Create a plastic hypothesis; sufficiently emphasized input may establish it immediately.</summary>
    private string CreateCandidate(List<Thought> input, float emphasis)
    {
        string label = CreatePatternLabel(input);
        if (theUKS.Labeled(label) is not null) label += "*";
        Thought candidate = theUKS.GetOrAddThought(label, PatternRoot);
        candidate.Weight = InitialStrength + emphasis;
        candidate.isPlastic = candidate.Weight < ConsolidationStrength;
        theUKS.AddSequenceAndLink(candidate, _hasSequence, input, allowCompression: false);
        candidate.Fire();
        string result = $"{candidate.Label}: strength {candidate.Weight:0.##}, " +
            (candidate.isPlastic ? "candidate" : "pattern");
        return result;
    }

    /// <summary>Apply one learning response per active Thought, independently of candidate creation.</summary>
    private List<string> ReinforceCandidates(HashSet<Thought> activeThoughts, float emphasis)
    {
        List<string> status = new();
        foreach (Thought thought in activeThoughts) status.Add(ReinforceCandidate(thought, emphasis));
        return status;
    }

    /// <summary>Strengthen a candidate into a pattern; established patterns continue accumulating usage weight.</summary>
    private string ReinforceCandidate(Thought thought, float emphasis)
    {
        float increase = Reinforcement * Math.Max(1, emphasis);
        thought.Weight += increase;
        if (thought.Weight >= ConsolidationStrength) thought.isPlastic = false;
        if (ExplainLearning) _learningDetails.Add($"{thought.Label}: reinforced +{increase:0.##}, strength {thought.Weight:0.##}" +
            (emphasis > 0 ? $"; emphasis {emphasis:0.##}" : "; ordinary match") +
            (thought.isPlastic ? "; candidate." : "; non-plastic pattern."));
        string result = $"{thought.Label}: strength {thought.Weight:0.##}, " +
            (thought.isPlastic ? "candidate" : "pattern");
        return result;
    }

    public void DecayCandidates(TimeSpan elapsed)
    {
        List<Thought> candidates = PatternRoot.Children.Where(thought => thought.isPlastic).ToList();
        // No forgetting below half capacity; reach the configured rate at full capacity.
        float pressure = Math.Clamp(2f * candidates.Count / CandidateCapacity - 1, 0, 1);
        if (pressure <= 0) return;
        float decay = CandidateDecayPerSecond * (float)elapsed.TotalSeconds * pressure;
        foreach (Thought candidate in candidates)
        {
            candidate.Weight -= decay;
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

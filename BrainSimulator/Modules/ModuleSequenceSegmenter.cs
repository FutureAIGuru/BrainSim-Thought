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
        QueueInput(input);
        string result = ProcessQueuedInput(emphasis);
        return result;
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
        if (!completed) LastStatus = "Awaiting more input.";
        string result = LastStatus;
        return result;
    }

    private void ProcessEpisode(SeqElement first, SeqElement pause, float emphasis)
    {
        FindExistingSubPatterns(first);

        HashSet<SeqElement> recognizedSequences = new();
        HashSet<Thought> activeThoughts = ProcessExistingMatches(first, pause, recognizedSequences, out bool fullyRecognized);
        foreach (Thought thought in activeThoughts) thought.Fire();
        List<string> status = ReinforceCandidates(activeThoughts, emphasis);

        if (!fullyRecognized) status.AddRange(ProcessPotentialCandidates(first, pause, recognizedSequences, emphasis));
        LastStatus = string.Join("; ", status);
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

        // Established patterns propagate from both phrase boundaries. No segmentation enumeration is needed.
        var establishedOccurrences = activeOccurrences.Where(match => match.sequence.LinksFrom.Any(link =>
            ReferenceEquals(link.LinkType, _hasSequence) && !link.From.isPlastic)).ToList();
        bool[] fromStart = new bool[position + 1], toEnd = new bool[position + 1];
        fromStart[0] = toEnd[position] = true;
        for (int boundary = 0; boundary < position; boundary++)
            foreach (var match in establishedOccurrences.Where(match => match.start == boundary))
                if (fromStart[boundary]) fromStart[match.end] = true;
        for (int boundary = position; boundary > 0; boundary--)
            foreach (var match in establishedOccurrences.Where(match => match.end == boundary))
                if (toEnd[boundary]) toEnd[match.start] = true;
        fullyRecognized = position > 0 && fromStart[position];

        List<(SeqElement sequence, int start, int end)> supportedOccurrences = new();
        if (fullyRecognized)
        {
            // Reinforce every established occurrence belonging to any complete explanation.
            // Tentative compounds cannot displace those words, and this episode creates no candidates.
            foreach (var match in establishedOccurrences)
                if (fromStart[match.start] && toEnd[match.end]) supportedOccurrences.Add(match);
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
        // With full established coverage, preserve alternative complete explanations instead.
        foreach (var match in supportedOccurrences)
        {
            bool contained = !fullyRecognized && supportedOccurrences.Any(other => other.start <= match.start && other.end >= match.end &&
                (other.start < match.start || other.end > match.end));
            if (contained) continue;
            // An independent occurrence can still activate the same Thought even if another occurrence was inhibited.
            foreach (Link owner in match.sequence.LinksFrom.Where(link => ReferenceEquals(link.LinkType, _hasSequence)))
            {
                activeThoughts.Add(owner.From);
                if (RecognitionCounts is null) continue;
                RecognitionCounts.TryGetValue(owner.From, out int count);
                RecognitionCounts[owner.From] = count + 1;
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
        List<(List<Thought> input, int support)> proposals = new();
        List<Thought> episode = new(), unrecognized = new(), gap = new();
        SeqElement orphan = null;
        List<Thought> boundedGap = null;
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
                // Keep an unknown run bounded by recognized matches on both sides.
                if (gap.Count > 1 && gap.Count < episode.Count - 1) boundedGap = new List<Thought>(gap);
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
                if (proposalIndex < 0) proposals.Add((new List<Thought>(span), support));
                else if (support > proposals[proposalIndex].support) proposals[proposalIndex] = (proposals[proposalIndex].input, support);
            }
        }

        // Admit a sole bounded gap without waiting for repetition.
        if (boundedGap is not null && boundedGap.Count == unrecognized.Count) proposals.Add((boundedGap, 1));

        // An orphan may signal a wrong neighboring boundary: also try absorbing the shortest adjacent match.
        if (unrecognized.Count == 1)
        {
            proposals.Clear();
            proposals.Add((unrecognized, 1));
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
            if (expansion is not null) proposals.Add((expansion, 1));
        }
        else if (emphasis > 0 && 1 + emphasis >= MinimumOccurrences && episode.Count > 1)
            proposals.Add((episode, (int)emphasis)); // Emphasis supplies extra hits without earlier queue matches.

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
            if (created >= MaximumProposalsPerEpisode || PatternRoot.Children.Count(thought => thought.isPlastic) >= CandidateCapacity) break;
            status.Add(CreateCandidate(proposals[index].input, emphasis));
            created++;
        }
        return status;
    }


    /// <summary>Tags complete stored patterns and partial matches in the earlier queue.</summary>
    public void FindExistingSubPatterns(SeqElement first)
    {
        for (SeqElement start = first; start is not null && !ReferenceEquals(start.VLU, _inputPause); start = start.NXT)
        {
            int length = 0;
            for (SeqElement end = start; end is not null && !ReferenceEquals(end.VLU, _inputPause); end = end.NXT)
            {
                length++;
                var matches = theUKS.FindSequencesByActivation(start, _inputOccurrenceOptions, getFirstElements: false, last: end);
                foreach (var match in matches)
                {
                    SeqElement target = match.seqNode;
                    if (target.FRST is null)
                    {
                        SeqElement member = _inputQueueHead;
                        while (member is not null && !ReferenceEquals(member, first) && !ReferenceEquals(member, target)) member = member.NXT;
                        if (!ReferenceEquals(member, target) || ReferenceEquals(member, first)) continue;
                    }
                    else if (!ReferenceEquals(target.FRST, target) || theUKS.GetSequenceLength(target) != length) continue;
                    for (SeqElement element = start; ; element = element.NXT)
                    {
                        element.AddLink(_usedBy, target);
                        if (ReferenceEquals(element, end)) break;
                        if (match.seqNode.FRST is null) target = target.NXT;
                    }
                }
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
        thought.Weight += Reinforcement * Math.Max(1, emphasis);
        if (thought.Weight >= ConsolidationStrength) thought.isPlastic = false;
        string result = $"{thought.Label}: strength {thought.Weight:0.##}, " +
            (thought.isPlastic ? "candidate" : "pattern");
        return result;
    }

    public void DecayCandidates(TimeSpan elapsed)
    {
        float decay = CandidateDecayPerSecond * (float)elapsed.TotalSeconds;
        foreach (Thought candidate in new List<Thought>(PatternRoot.Children))
        {
            if (!candidate.isPlastic) continue;
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

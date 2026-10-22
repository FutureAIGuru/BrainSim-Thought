# Sequence segmenter: remaining foundation

The previous boundary learner, evidence rules, split scoring, candidate creation,
reinforcement, decay, and recognition API have been removed. The former learner tests and text test adapter have
been retired along with that implementation. A minimal `ProcessEpisode` now
segments pause-delimited spans and learns their unknown gaps.

## Rolling input queue

The `Input queue` section near the top of `ModuleSequenceSegmenter.cs` owns
one rolling UKS sequence per module instance. `UKSInitializedNotification` creates
the initial pause, PAUSE Thought, search options, and search options.
Callers initialize the module through that notification before using it; operations
do not initialize missing knowledge. The head retains its identity when cleared. The initial pause is never removed by reading or trimming. `QueueInput` inserts supplied Thoughts
newest first and doubles each explicit `PAUSE`. It has no pause flag or input
validation; the caller supplies the correct observations and any pauses. `TryReadQueuedInput` consumes
stable nodes chronologically using incoming NXT links; it withholds the mutable
head and returns all stable observations, including consecutive pauses. The learner
handles pause boundaries and tracks its own incomplete span. Both methods work without the learner
or dialog. `FindInputOccurrences` reverses a chronological query and reports
distinct physical match positions in that instance's queue, including overlaps. This includes the
latest occurrence: repetition requires at least two matches. The caller selects spans without pauses.

`InputQueueLifetime` defaults to five seconds. `Fire()` checks once per second
and explicitly calls `TrimInputQueue`. Insertion and retrieval do not trim.
Trimming walks from the head to the first expired stable node and deletes that
suffix, retaining the initial pause. Stable node creation times determine age;
the fixed head's VLU relationship creation time dates its changing contents.
Queue node deletion preserves input Thoughts. An expired active span is discarded.
There is no queue-length limit, length counter, or missed-input counter.
Each instance has its own head, processing pointer, lifetime, and trim schedule.

`InputReceived` and `ProcessQueuedInput` adapt queue observations to
`ProcessEpisode`; module firing also drains queued observations. The queue can
be consumed incrementally, and completed episodes are delivered to the learner. Queue tests are in
`ModuleSequenceSegmenterQueueTests.cs`, extending the existing test class.

## Initial candidate lifecycle

A standalone span of at most three inputs can be admitted once. Longer spans
require at least two occurrence positions in recent input. `RepetitionWindow`
defaults to five seconds and filters occurrence creation times independently of
the queue sweep. Admission creates one plastic UKS Thought with a flat spelling
sequence and strength 1. Subsequent supported matches add 1; reaching strength 3
makes the Thought non-plastic. Selected non-plastic matches continue gaining strength.
The default pool permits 30 plastic Thoughts; a full pool declines new admission,
and consolidation frees a slot. These values are exposed module parameters.
The caller sets `PatternRoot` and `PatternPrefix` before learning. The text dialog
selects `Word` and `w:`. `CreatePatternLabel` concatenates input labels, stripping
`c:` from characters and lowercasing those characters. Other Thought labels are
retained as supplied. Label collisions receive a unique suffix; identity is
determined by UKS sequence search. There is no external label callback.

`FindExistingSubPatterns` searches every contiguous input span using complete UKS
sequence matches. It continues after shorter matches and considers overlapping
alternatives. A backward pass chooses maximum existing-pattern coverage, then
the fewest recognized units, then greatest combined weight. Otherwise equal
splits retain the longer-first tie rule. It returns the selected parts in input
order, each containing its original observations and an existing Thought or null
for an unknown gap. Adjacent misses form one gap. The method does not create or
strengthen Thoughts. Both plastic and non-plastic patterns participate.

`ProcessEpisode` activates selected existing patterns and applies admission
rules independently to each gap. Thus `dogandcat` can produce `dog`, a new `and`
candidate, and `cat`. A rejected gap does not stop later matches. Repeated gaps
in one episode reuse a candidate created by an earlier gap without double reward.
Recognition alone does not reinforce an embedded plastic pattern. Whole paused inputs
and explicit emphasis can reinforce directly; embedded patterns require selection
by the repeated-span boundary test. Discovery does not also reward a Thought
already created or directly reinforced in that episode.

Every complete paused input is also considered for whole-pattern admission,
independently of its existing coverage. The same length, repetition, emphasis,
and candidate capacity rules apply. Existing splits are captured before admission
so learning a whole phrase does not discard its unknown gaps. A newly admitted
whole pattern is not reinforced again on that presentation. Thus an established
`ca` and a plastic `r` do not prevent first-presentation admission of `car` at
strength 1. Longer fully covered input, such as `frog` after learning its letters,
still requires repetition or emphasis.

After segmentation, `FindRepeatedPatterns` independently examines the complete
episode, including inputs already covered by recognized patterns. It queries
recent input for spans of one or more observations, longest first. Single-input
spans are eligible only as separate segments or remainders, and an occurrence
inside any larger stored pattern is excluded from discovery rewards, whether
that larger pattern is plastic or consolidated. Each side must
have a pause boundary in the current episode or different immediately adjacent
non-pause Thoughts across occurrences. An old pause does not validate a boundary
inside the current input. `MaximumProposalsPerEpisode` defaults to three qualifying
spans. They are ranked by distinct left contexts times distinct right contexts,
then occurrence count. Longer spans win only otherwise equal ties; overlapping
shorter alternatives remain eligible. Context sets are transient, rebuilt from
the retained queue rather than stored on each candidate.
Unknown spans are proposed and plastic existing spans can be reinforced. Non-plastic
matches do not consume a proposal slot.
Candidate capacity, repetition window, and minimum occurrence count still apply.
`SingleUseMaximumLength` is only a single-exposure admission limit, not a maximum
learned pattern length. Searches use transient lists; all tested substrings are
not stored as candidates.

For an existing embedded candidate, repetition can earn another reward once the
retained history supports both boundaries, even when its current neighbors have
appeared previously. All occurrences in the current episode are excluded from
the historical context comparison. Boundary evidence still has to be present in
the retained queue. Whole paused inputs and explicit emphasis keep their direct
reinforcement rule.

Thus `xxfrogabc` followed by `deffrogzz` creates `frog` even after the entire
alphabet has become non-plastic. Later presentations with varying surroundings
reinforce it through boundary evidence. Discovery runs after normal learning so a new proposal is not also
reinforced by recognition on its creation episode. If `a` always immediately
precedes `frog`, the longer `afrog` can be inferred instead; the shorter boundary
needs different preceding context. Repeated whole phrases can also become larger
patterns, even if smaller patterns already cover them.

The text dialog strips trailing `!` characters and passes their count as emphasis
to `InputReceived(input, emphasis)`. Trailing whitespace is ignored. Each unit
adds one to initial strength or reinforcement. Positive
emphasis admits a gap without the usual length/repetition requirement, while
respecting candidate capacity. Non-plastic weights can continue increasing. Emphasis
applies to all parts of episodes completed by that call; it is not retained for
later input. Other callers can supply emphasis directly without punctuation.

The dialog's `exp` command and Load button share a randomized vocabulary
experiment. Top N defaults to fourteen; zero selects all words. Cycles defaults
to one hundred. `exp` uses equal weights and the first N entries of its built-in
fourteen-word list. Load reads a text word list, accepting either a word alone
or a word followed by a positive usage weight such as a tab-separated percentage.
Words are normalized to the letters supplied to the learner, and duplicates
have their weights combined. The file vocabulary is ranked by combined usage
weight; ties retain file order. Top N is applied before sampling, and the remaining
weights define relative selection probabilities. Each subsequent draw excludes
the preceding word and samples proportionally among the remaining choices.
Non-adjacent repeats are allowed, and the restriction resets at each phrase.
At least two distinct words are required for a multi-word phrase. Excluding
adjacent repeats changes overall frequencies, especially for very common words.
Plain unique-word lists use equal weights. Each phrase concatenates six sampled words without exposing their
boundaries, then supplies the usual final pause. Invalid usage weights report a
file error rather than being silently changed to uniform sampling.

The experiment runs in a background task.
It dispatches each submission to the UI thread and waits 10 milliseconds between
phrases, allowing other windows to redraw. The dialog reports progress and appends
each phrase and its learning status to the display. The missing count covers
the whole selected vocabulary; the displayed missing-name list is limited to
twenty entries. Text input, Browse, Load, and the experiment settings are disabled
during the experiment to prevent overlapping runs. UKS mutations are dispatched
to the UI thread. The existing direct `CorpusReader` API remains available, but
the Load button now runs the randomized word test instead of presenting file
lines individually.

`Fire()` calls `DecayCandidates` with elapsed time on every firing. Only plastic
candidates decay. The default `ConsolidationStrength` is 10. Plastic weights lose
`CandidateDecayPerSecond` (current default 0.1) per second, regardless of weight;
reaching zero deletes the candidate and frees a pool slot. Non-plastic patterns
retain their weights without decay and do not become plastic through inactivity.
The decay clock is independent of input queue trimming and clearing. The decay
method can also be exercised independently with an explicit elapsed duration.

Consolidated patterns receive reinforcement when selected as recognized segments,
including embedded segments. A newly admitted whole pattern prevents these normal
recognition rewards for its fragments on that episode; explicit emphasis retains
its existing behavior. Consolidated matches still do not take discovery proposal
slots. Weight thus records accumulated rewarded
recognition beyond consolidation, without changing the sequence's plasticity.

There is no weakest-candidate replacement. Recognition competition applies when
the selected split completely covers the episode with consolidated patterns.
Stored matching patterns not selected anywhere in that split lose
`CompetitionPenalty` (default 0.1) once per episode; selected patterns and unrelated
patterns are not penalized. Those losers cannot recover a discovery reward on
the same episode. Below `InitialStrength` they become plastic; at zero they are
deleted. Incomplete or provisional splits do not suppress discovery alternatives.

The fixed-seed diagnostic uses seeds 17, 42, and 73, comparing one and three
proposals with consolidation strength five, capacity twenty, and decay 0.05 per
second. It retains all 100 training episodes independent of machine speed and
applies ten milliseconds of decay per episode. Reports at episodes twenty and
one hundred count consolidated vocabulary entries separately from plastic
candidates. These are controlled measurements, not a five-second live queue test
or an assertion that the complete vocabulary will be learned. Earlier lifecycle
regressions explicitly retain threshold three; a separate test verifies the new
configurable threshold five. The current defaults are threshold three and capacity
thirty.

Before context ranking and repeat reinforcement, all three seeds had twenty plastic candidates and
zero consolidated vocabulary entries at both checkpoints, with either proposal
limit. Allowing three proposals passes the targeted overlap regression, but did
not improve vocabulary recovery in this retained-history diagnostic. The pool
fills before alternatives can be admitted, and one simulated second of decay
does not release initial-strength candidates. This is a remaining limitation;
these results do not measure the live five-second queue's forgetting behavior.

## Fixed phrase reproduction

`FixedVocabularyExperiment_ReportsWordsAndCombinations` submits fourteen explicit
six-word phrases drawn from the `exp` vocabulary, without supplying their spaces
as boundaries. Three passes give checkpoints at fourteen, twenty-eight, and
forty-two episodes. It reports every vocabulary entry's weight and plasticity,
plus all other patterns. History is retained and ten milliseconds of decay are
applied per episode, as in the controlled seeded diagnostic.

Before this correction, with capacity twenty, at forty-two episodes:

| Consolidation threshold | Vocabulary entries created | Vocabulary entries consolidated | Plastic candidates |
|---|---:|---:|---:|
| 3 | 4/14 | 0/14 | 20 |
| 5 | 3/14 | 0/14 | 20 |

With threshold three, `theca` is non-plastic at 3.998 while `dog` is plastic at
0.979, `cat` at 1.981, and `the` at 0.980. `capca` and `dogca` are also created.
Replaying the same phrases changes these weights only by decay after the first
pass. This reproduces combination preference and the plateau at either threshold.

After context ranking and repeat reinforcement, using the current capacity thirty:

| Consolidation threshold | Vocabulary entries created | Vocabulary entries consolidated | Plastic candidates |
|---|---:|---:|---:|
| 3 | 14/14 | 12/14 | 30 |
| 5 | 14/14 | 7/14 | 30 |

These are not a capacity-controlled before/after comparison. At threshold three,
`a` and `the` remain plastic. False patterns including `ca`, `capca`, `dogca`, and
`theca` also consolidate. Discovery improves, but context diversity is a heuristic
and does not uniquely identify word boundaries. Repeated complete phrases also
occupy candidate slots. Retaining all history differs from the live five-second
queue.

`FixedDogContexts_ReusedNeighborsAllowConsolidation` uses `fur dog cat`, `hat dog
has`, and `cam dog the`. These create `dog` and raise its strength to two. Repeated
`fur dog has` inputs now raise it to five and consolidate it, despite reuse of
familiar neighbors. `ContextSupport_PrioritizesDogOverLongerDogCa` verifies that
`dog` gets the only available proposal slot ahead of `dogca` when its context
support is greater.

With guarded single-input discovery, the same forty-two-episode test still creates
14/14 words and consolidates 12/14 at threshold three. `a` receives an additional
reward, but remains plastic at approximately 1.985. Once complete repeated phrases
become candidates, their containment also blocks single-input discovery rewards.
False patterns `ca`, `capca`, `dogca`, and `theca` still consolidate. This change
permits single-input learning without fixing ambiguous multi-input boundaries.
The targeted tests verify that `a` consolidates between changing recognized
neighbors and remains at strength one inside plastic or consolidated larger
patterns.

After enabling continued consolidated reinforcement, the fixed test's threshold
three ranking at episode forty-two starts with `t` (8.998), `ca` (7.999), then
`theca` and `car` (4.998 each). Unexpected patterns do not automatically fall to
the bottom. Early fragment recognition gives them a head start, and replayed
complete-phrase candidates subsequently compete with constituent words. These
are cumulative weights, not recent recognition rates.

Those rankings precede learned-weight decay and split competition. The earlier
longer-first rule chose `hasca` plus `rt` over `has` plus `cart`. `car` plus `rt` would spell
`carrt`, so it is not a complete split of `cart`. This reproduces a competing
boundary explanation; it does not establish which splits occurred in the user's
existing trained network.

`SplitCompetition_PrefersWholeCartAndStrongerHasCart` now verifies `cart` and
`has` plus `cart` win. `SplitCompetition_RewardsHasCatAndWeakensHasCaTButLeavesUnrelatedPatternsAlone`
uses weights comparable to the user's display: `has` 92, `cat` 32, `hasca` 18,
and `t` 74. `hascat` rewards `has` and `cat` by one and reduces the two displaced
patterns by 0.1; an unrelated `dog` is unchanged. The fixed forty-two-episode
experiment still consolidates 12/14 words at threshold three, with `t` and `ca`
at the top. Passing the targeted competition test does not demonstrate that the
entire vocabulary ranking is corrected. Repeated complete-phrase candidates
remain part of that diagnostic.

The experimental length-weighted score was reverted after the larger experiment
showed strong fragments displacing words. Split selection again prefers coverage,
then fewer recognized units, then combined strength. Plastic alternatives remain
excluded from recognition penalties so repeated candidates can gather evidence
even when learned letters cover the input.

Competition now uses the weakest selected pattern overlapping each losing
occurrence. Before any reinforcement, its penalty is
`min(MaximumCompetitionPenalty, CompetitionStrengthScale * max(0, support - loser weight))`.
Defaults are a strength scale of 0.1 and a maximum penalty of one. Equal or
stronger alternatives are not penalized; unrelated input cannot increase the
penalty. Multiple occurrences still cause at most one penalty per pattern per
episode. Setting the scale to zero restores the fixed `CompetitionPenalty` for
comparison. Coverage and split selection are unchanged, and plastic alternatives
remain protected. The existing complete-learned-coverage requirement still applies.

`CompetitionVocabularyExperiment_ComparesFixedAndStrengthPenalties` compares both
rules on the same no-adjacent-repeat six-word phrases at seed 73,
reporting consolidated vocabulary, actual words among the top fourteen weights,
and non-vocabulary patterns at or above the decay threshold after 100 and 200
phrases. It retains approximately the last 100 phrases in the input queue and applies
10 ms of decay per phrase, independently of test-machine speed; this is a
controlled comparison rather than an exact replay of UI timing.

At seed 73, both rules consolidate all fourteen words and place them in the top
fourteen at 100 and 200 phrases. Fixed-penalty noise at or above 3.5 grows from
11 to 24; strength-based competition reduces it from 4 to 3. The single paired
run took approximately six minutes and thirty-seven seconds. This shows an
improvement in this controlled case, not a guarantee across seeds or vocabulary
sizes. The planned additional seeds and 1,000-phrase runs were stopped because
of their cost; they are not included in these results.

The dialog's **One phrase per line** option makes Load read the file in order
rather than generate randomized phrases. Cycles limits the number of nonblank
phrases submitted; Top N does not filter this mode. Punctuation is removed before
submission (so exclamation marks do not add emphasis), spaces are ignored by the
character adapter, and each line supplies exactly one trailing pause. Reading
runs in the background with submissions dispatched to the UI, allowing the UKS
display to evolve. The direct CorpusReader API also preserves one phrase per
line instead of splitting at sentence punctuation; its existing tab-metadata
handling remains in place.

Single-symbol candidates require the sole-unrecognized-element exception. Existing
stored single-symbol sequences still tag input and receive recognition
reinforcement under the same coverage and containment rules as longer patterns.
Single-symbol queue tags remain available as evidence for longer repeated spans.

For recognition diagnostics, select **Seed all words before reading phrases**
alongside **One phrase per line**. Load first reads the entire file synchronously
on the UI thread and splits it into letter runs at whitespace or punctuation.
Each distinct lowercase word gets a non-plastic w: Thought under Word with a
spelled sequence and weight at least ConsolidationStrength. Existing spelled
sequences are reused. Input queue history is cleared, then the whole phrase file
is presented in the background as usual. Source counts, surviving recognition
occurrence counts, and newly created patterns/candidates are appended to the
output. Recognition counting is optional diagnostic bookkeeping and does not
change once-per-Thought-per-episode reinforcement. Single-letter words are
seeded and recognized; new single-symbol candidates require the sole-orphan rule. Existing
vocabulary is preserved; use an empty UKS for an isolated run.

When non-plastic patterns provide a complete, abutting path from the beginning
of an episode to its pause, recognition reinforces all established occurrences
that participate in any such path. Forward and backward boundary propagation
finds these without enumerating combinations. Tentative candidates cannot
suppress established words in this case, and candidate creation is skipped for
the entire episode. Overlapping matches alone do not establish full coverage.
Without complete established coverage, the existing partial recognition and
candidate discovery rules still apply. The seeded three-line shame test now
counts all three occurrences and creates no compounds.

The per-element tag-coverage shortcut was removed: overlapping stored matches
such as ca and an must not stop discovery of can. Candidate creation is skipped
only when established matches form a complete abutting segmentation, as above.
A single unrecognized element may be admitted immediately as a one-symbol
candidate; multiple unrecognized elements do not qualify for that exception.
Emphasized input adds extra evidence to candidate admission: with default
MinimumOccurrences=2, frog! creates frog without an earlier queue occurrence.
Recognized patterns receive max(1, emphasis) reinforcement hits, so !! gives two
hits. Creation strength remains InitialStrength + emphasis.

For a sole unrecognized element, candidate proposals include both that element
and its shortest expansion across an immediately adjacent stored match. For
cat|an|d|dog, an+d is shorter than d+dog, so and is nominated. For cat|s, cats is
nominated. Equal-length alternatives prefer the left neighbor. The expansion
receives the first proposal slot; a second slot admits the one-symbol orphan.
The configured MaximumProposalsPerEpisode and candidate capacity still apply.

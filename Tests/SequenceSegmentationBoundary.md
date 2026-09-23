# Maximal boundary-match learner

Each `SubmitLine` is one pause-delimited input. Spaces are ignored. Learning
only creates prefixes and suffixes, never arbitrary interior substrings and
never a sequence crossing two submissions. The configurable candidate-length
limit now defaults to 32 characters (previously four).

For each prefix, store the following character; for each suffix, store the
preceding character. A whole input uses a pause as its context. These context
links live on the candidate in the UKS and survive XML save/load. No separate
corpus or previous-input list is kept.

At each edge, choose the longest candidate with a previously observed, different
inward context. Thus `acat` followed by `thecat` supports `cat`: its preceding
characters differ, whereas the preceding characters of `at` and `t` do not.
Prefix discovery is symmetric. If no such candidate exists, use the longest
already recognized candidate at that edge, or a repeated whole input. The two
edges use eligibility from before the input, so a single input cannot confirm
itself. An identical whole input counts only once, not twice.

Candidates start at weight 1. Each winning candidate gains 1 per submitted
input, regardless of length. Every other plastic candidate loses 0.05; zero
weight deletes it. A deleted edge hypothesis can be recreated on a later input,
but remains unrecognized until it wins. Observation counts are separate from
reinforcement. There is no penalty for merely sharing letters with the input.

Recognition requires at least one win and weight at least 1. Candidates remain
plastic. Before discovery, dynamic programming finds the two highest-scoring
complete splits. A word contributes `length * log(weight / (weight + 1)) - 0.1`.
The length term compares confidence over equal character coverage; the small
per-word cost discourages excessive splitting. This is a heuristic score, not a
calibrated probability. It considers the entire input rather than making greedy
word choices that can strand an unrecognized remainder.

Discovery is skipped only if every word in the best complete split has weight
at least 3 and at least 3 boundary wins, and that split beats the runner-up by
at least `log(2)` (or has no alternative). Multi-character inputs covered solely
by one-character words do not qualify. These are initial experimental thresholds.
Qualifying splits reinforce each distinct selected word once per input, without
adding candidates, contexts, or invented boundary evidence. All other plastic
candidates still decay. Thus deleted fragments are not recreated by familiar
inputs. Provisional, ambiguous, and incomplete inputs still run discovery.

Output uses the best complete split when available. When none exists, the prior
greedy partial recognizer displays known words and bracketed unknown spans.
`RecognizeLine` uses the same complete-split search without learning.
Recognized interior words do not generate remainder candidates in this version.

A confidently segmented phrase of at least three established words is also saved
as a UKS word sequence. When a new input matches all but its first or last word,
sequence search can reuse that phrase pattern. For example, after `fido plays
fetch` is established, one presentation of `rover plays fetch` gives `rover`
a boundary from the pause and one from the known phrase. This one-shot inference
requires at least two established matching words and a new edge span within two
characters of the replaced word's length. The inferred candidate remains plastic
so later evidence can still revise it. Phrase patterns survive XML save/load.

The trace reports edge winners and their reasons, decay/deletion counts, current
segmentation, and candidate observations/wins. In a fresh learner, enter `a cat`
and then `the cat`: the result is `[the] cat`. Prefix example: `cat runs`, then
`cat walked`, produces `cat [walked]`. Existing saved candidate weights alone do
not count as confirmation; they need evidence under the new rule.

Candidate sequences use flat UKS sequence nodes owned by each hypothesis. This
avoids the observed corruption during deletion/recreation when candidates reused
compressed subsequences, and avoids global sequence searching for each new edge.

## Scope and tests

This is an experimental segmentation rule, not a claim that maximal repeats
always correspond to words. Shared inflections (`catruns` / `catsleeps` suggests
`s`), coincidentally shared letters, and recurring multiword phrases can still
win. The algorithm does not permanently lock candidates, but unbounded accumulated
weights mean well-reinforced mistakes can take many misses to disappear.

Regression tests cover the motivating examples in both directions, long words,
fragment competition, independently supported short words, repeated whole inputs,
real versus truncated pauses, forgetting and sequence integrity, XML persistence,
interior recognition, corpus metadata, and no cross-pause discovery. A test makes
the shared-inflection ambiguity explicit rather than claiming to solve it.

The seeded convergence test uses the dialog's 14-word vocabulary: dog/has/fur/
cat/hat/car/a/an/the/cam/can/cap/cart/camp. For each of seeds 17, 42, and 73,
it trains on 150 independently sampled six-word phrases. It then evaluates 200
fresh phrases from the same vocabulary without learning, requires at least 190
exact splits, and checks that all 14 words gained support. The short two-input
tests check individual boundary rules; this repeated-exposure test checks the
behavior of the full learner. Held-out results were:

| Seed | Exact phrases | Boundary F1 | Surviving hypotheses |
| --- | --- | --- | --- |
| 17 | 200/200 | 1.000 | 14 |
| 42 | 200/200 | 1.000 | 14 |
| 73 | 200/200 | 1.000 | 14 |

In a separate local probe, all three seeds already reached 200/200 by 100
training phrases, although hundreds of extra hypotheses still survived then.
The 150-phrase results cover this controlled vocabulary and generator. The gate
can reinforce a confident but incorrect split; these results do not establish
general vocabulary recovery or eliminate the shared-suffix ambiguity.

Run with detailed output:

```powershell
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~ModuleSequenceSegmenterTests&FullyQualifiedName!~CompleteSegmentationExperiment" --logger "console;verbosity=detailed"
```

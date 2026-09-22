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

The seeded experiment trains on 1,000 six-word phrases sampled independently from
dog/has/fur/cat/hat/car for seeds 17, 42, and 73. It checks that all six source words
gain support and that `doghasfur` is recognized. It also reports exact-phrase
accuracy and boundary F1 on 200 fresh phrases without changing weights. Those
reported metrics are observations, not assertions of perfect convergence.

Validation after adding the recognition gate: all 29 tests in the command below
passed (the historical complete-segmentation diagnostic is excluded). Tests cover
greedy dead ends, ambiguous alternatives, provisional and letters-only coverage,
incomplete input, skipped creation, reinforcement, and deletion without recreation.
Held-out measurements using the production recognizer were:

| Seed | Exact phrases | Boundary F1 | Surviving hypotheses |
| --- | --- | --- | --- |
| 17 | 200/200 | 1.000 | 6 |
| 42 | 200/200 | 1.000 | 6 |
| 73 | 200/200 | 1.000 | 6 |

These results cover only this six-word synthetic vocabulary. Before the gate,
the same experiment retained 521, 530, and 503 hypotheses respectively, with the
same held-out accuracy. The gate can reinforce a confident but incorrect split;
these results do not establish general vocabulary recovery or eliminate the
shared-suffix ambiguity.

Run with detailed output:

```powershell
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~ModuleSequenceSegmenterTests&FullyQualifiedName!~CompleteSegmentationExperiment" --logger "console;verbosity=detailed"
```

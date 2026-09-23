# Existing-corpus segmentation evaluation

`ModuleSequenceSegmenterCorpusTests.cs` evaluates the production learner against
the repository's English template corpus (`bst_true_template_corpus.txt`) and
its more varied durability corpus (`bst_word_durability_corpus.txt`). Production
learning rules and thresholds are unchanged by this evaluation.

## Protocol

- Remove tab-delimited metadata; split sentences using the file loader's pause
  convention. Normalize letters to lowercase. Original whitespace supplies gold
  word boundaries only to the evaluator. The learner receives concatenated words.
- Deduplicate by the entire unspaced input and reject conflicting gold splits.
  This prevents copies of the same phrase leaking into both partitions. Repeated
  source lines therefore do not implicitly weight training frequency.
- Deterministically shuffle unique phrases with seeds 17 and 42. Hold out one
  fifth; train on the rest for three independently shuffled passes, with a fresh
  UKS for each corpus/seed combination. Defaults match the application.
- After every pass, recognize all training and held-out phrases without learning.
  Check that candidate identities, weights, plasticity, and evidence links stay
  unchanged during evaluation, and every output preserves all input characters.
- Report exact phrase accuracy, complete coverage, boundary precision/recall/F1,
  unknown-character coverage, candidate counts, recognized training-word counts,
  discovery-gate skips, and incorrect splits that nevertheless skipped discovery.
- Split held-out results into mutually exclusive groups: all words appeared at
  training-string edges; all words appeared in training but some only internally;
  and at least one unseen word. This exposes the edge-only discovery limitation.
- Show up to five held-out errors per checkpoint. Boundary metrics treat unknown
  spans as single chunks; exact correctness requires the actual unbracketed words.
  Complete coverage is not correctness. Empty groups have zero metric denominators
  and are printed with `n=0`; their displayed zeros are not performance estimates.

These are diagnostic experiments. Passing means the measurements and invariants
are valid; it does **not** mean segmentation quality is acceptable. No success
threshold is chosen after observing these corpora. A separate scorer test checks
normalization, metadata removal, unknown coverage, and boundary counting.

## Run

```powershell
dotnet test Tests/Tests.csproj --no-restore -p:OutputPath=bin/CorpusEvaluation/ --filter "FullyQualifiedName~ExistingCorpus_|FullyQualifiedName~CorpusScorer_" --logger "console;verbosity=detailed"
```

The separate output directory avoids replacing an executable open in BrainSim.
The two corpus files are copied to the test output by `Tests.csproj`, so the test
does not depend on the shell's working directory. There are four corpus runs and
one scorer test; the corpora are evaluated in full after deduplication.

## Initial results (2026-09-21)

All five measurement/integrity tests passed in approximately 3 minutes 40 seconds.
Segmentation quality was poor. After three passes:

| Corpus | Seed | Training / held-out phrases | Held-out exact | Boundary precision | Recall | F1 | Surviving candidates |
| --- | --- | --- | --- | --- | --- | --- | --- |
| English templates | 17 | 734 / 184 | 0/184 | .308 | .888 | .458 | 477 |
| English templates | 42 | 734 / 184 | 0/184 | .294 | .926 | .446 | 409 |
| Word durability | 17 | 103 / 26 | 0/26 | .211 | .887 | .341 | 1070 |
| Word durability | 42 | 103 / 26 | 0/26 | .227 | .901 | .362 | 1099 |

The template file contained 919 phrases, of which 918 were unique. The durability
file contained 129 unique phrases. Across the four runs there were 5,022 training
submissions; evaluation at all three checkpoints did not change learned state.
Seeded partitions overlap with one another; these are not 420 distinct held-out
phrases combined across seeds.

No complete phrase was exactly correct even on the training partitions. The
discovery gate skipped zero training inputs in all twelve epochs, so these runs
did not exercise confident-but-wrong suppression: the learner never reached the
required confidence. Candidate learning/retention is the immediate bottleneck,
not a successful recognition gate preventing further discovery.

The boundary-exposure split is important:

- Template seed 17 had 14 held-out phrases whose every word appeared at a training
  edge. None was exactly correct; final F1 was .497.
- Template seed 42 had 10 such phrases. None was exactly correct; final F1 was .458.
- Every held-out durability phrase contained at least one unseen training word.
  That corpus is a vocabulary-sparsity stress test, not a fair expectation of
  complete recovery by an edge-only learner.

Representative final outputs (brackets indicate unknown spans):

```text
Expected: the mouse can squeak
Actual:   the [mou] s e [c] a n s [qu] e a [k]

Expected: the bushes have leaves
Actual:   the [bu] s [h] e s have l e a [v] e s

Expected: the dog sleeps while the clock ticks
Actual:   thedog s [l] e e [p] s [w] h il e the [clock] t [ick] s
```

High recall combined with low precision reflects excessive inserted boundaries,
often from single-letter candidates. Some multiword chunks also survive. These
measurements overturn any inference that success on a small synthetic vocabulary
established performance on these larger corpora. They do not prove that additional
training could never help; this run used exactly three passes. No production
algorithm changes were made to improve the reported results.

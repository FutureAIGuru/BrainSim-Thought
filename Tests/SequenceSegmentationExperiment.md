# Complete-segmentation competition experiment

This is an isolated test, not a production change. Run the tests matching
`CompleteSegmentationExperiment` with detailed console output to see comparisons.
Passing diagnostic tests mean the experiment ran correctly, not that learning succeeded.

## Protocol

- Fresh UKS per learner; 1,000 training phrases, six uniformly sampled words per phrase.
- Seeds 17, 42, 73; independent selection and evaluation random streams.
- Evaluate on 200 further phrases without updating weights.
- Expanded vocabulary: dog, has, fur, cat, hat, car, cam, can, cap, cart, camp.
- Mixed vocabulary: dog, has, fur, tail, a, is.
- No internal boundaries or vocabulary supplied to either learner.
- Maximum candidate length four, matching production.
- Baseline: unchanged edge/remainder learner.
- Experimental learner: sample complete non-overlapping segmentations, then reinforce
  only selected chunks. Use existing initial weight, length reinforcement, decay and
  character-overlap interference. All learned candidates remain plastic.
- Both sets of learned candidates are evaluated using the SAME new best-complete-path
  decoder, allowing provisional candidates. This decoder is test-only; production
  recognition is not claimed to achieve these results.

The experimental score assigns a chunk probability proportional to its weight plus
a novel-chunk prior (uniform 26-letter alphabet and geometric length, truncated at four).
Normalize by total candidate weight plus one. Path probability is the product of
chunk probabilities. Backward dynamic programming supports either sampling a full
path in training or maximizing its score at evaluation. Temporary arrays are search
workspace; persistent learned state is stored as Thoughts and linked sequences.

This prior and sampling policy are assumptions of this particular trial, not a
faithful PARSER reproduction or the only possible competitive learning rule.

## Results

| Vocabulary | Seed | Baseline boundary F1 | Trial boundary F1 | Baseline exact phrases | Trial exact phrases |
|---|---:|---:|---:|---:|---:|
| Expanded | 17 | .949 | .554 | 122/200 | 0/200 |
| Expanded | 42 | 1.000 | .529 | 200/200 | 0/200 |
| Expanded | 73 | .996 | .656 | 196/200 | 0/200 |
| Mixed | 17 | 1.000 | .710 | 200/200 | 0/200 |
| Mixed | 42 | .982 | .664 | 171/200 | 1/200 |
| Mixed | 73 | 1.000 | .687 | 200/200 | 0/200 |

Acceptance was specified as improved held-out boundary F1 AND all source words in
the top N, across all seeds and both vocabularies. The trial fails: F1 declines in
every run, independently of the ranking criterion. Do not promote this learning rule.

The trial often retains subdivisions such as `ha | s` and `tai | l`. Selected-only
reinforcement can preserve erroneous early decompositions; competition alone does
not establish a mechanism for revising them. This is a failure of this implementation,
not a disproof of competitive learning generally.

The useful finding is that candidate rank is not equivalent to recognition quality.
The existing learner plus whole-path decoding performs substantially better than its
top-candidate list suggests. Further recognition experiments should measure actual
boundary and exact-phrase accuracy, including unseen vocabulary, longer words,
compound words and ambiguous interpretations, rather than require every fragment
to disappear from memory.

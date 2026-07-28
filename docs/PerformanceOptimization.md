# UKS Performance: finding and fixing the limits

This document describes why the UKS became slow as it grew, how the real causes
were located, and what was changed. It also records the things that looked like
causes and were not, so that work is not repeated.

The short version: **storing knowledge was never the problem.** Creating
Thoughts is flat and fast. What made a large UKS unusable was the cost of
*reading* and *reorganizing* one — and almost all of that turned out to sit in
one place nobody would have guessed.

---

## 1. What was measured first

Before changing anything, a benchmark was written to establish where the cost
actually was (`Tests/UksPerformanceBenchmarks.cs`). The starting picture:

```
Thought creation            flat — ~2 ms per 2000, unchanged from 2k to 16k
uks.Labeled(string)         0.03 us      (dictionary lookup)
member.Parents              0.23 us
member.HasAncestor(string)  0.52 us
hub.Children (4000 members) 99.3 us      (25 ns per link)
hub.LinksFrom (same links)   3.9 us

ReplaceThoughtReferences    15.5 ms      for a single merge
CoalesceSimilarClasses      95.7 ms      for 20 classes of 40 members
Delete                       135 us      per Thought

template discovery @1200 phrases  1663 ms,  growth exponent 1.67
```

An exponent near 1 means cost grows in proportion to the corpus; near 2 means
every observation is being compared with every other. **1.67 was the number that
mattered**: it meant every increase in corpus size cost disproportionately more,
so a larger corpus was not merely slower but eventually impossible.

---

## 2. Two false starts, recorded so they are not repeated

### `Children` is not slow because of LINQ

`Parents` and `Children` were written as LINQ chains comparing the link type by
label:

```csharp
_linksTo.Where(x => x.LinkType?.Label == "is-a").Select(x => x.To).OfType<Thought>().ToList()
```

`hub.Children` cost 99.3 µs where `hub.LinksFrom` cost 3.9 µs over the *same*
4000 links, which looked like conclusive evidence that the query was the
problem. Both were rewritten as plain loops with reference comparison.

**`Children` did not measurably improve.**

The comparison was misleading. `LinksFrom` is fast because
`new List<Link>(_linksFrom.AsReadOnly())` bulk-copies the *pointer array* — 4000
contiguous references — and never dereferences a single `Link`. `Children` must
dereference all 4000 scattered `Link` objects to read `.LinkType`. That is cache
misses, inherent to an object graph, and no rewrite of the query changes it.

The loop form was kept because it is clearer, not because it was faster.

### The all-pairs comparison is not the bottleneck

`DiscoverSequenceTemplates` compares every pair of observations, each comparison
running a longest-common-subsequence with a freshly allocated matrix. That is the
obvious quadratic suspect.

Instrumentation showed it is **1%** of discovery time — and it does not grow with
the corpus at all (7,210 LCS calls at both 300 and 1200 phrases), because it
operates on *distinct* phrase shapes, which are bounded.

---

## 3. How the real cause was found

Guessing had failed twice, so `UKS/UKS.SequenceDiagnostics.cs` was added: exact
counters plus coarse phase timers, off by default and costing one boolean test
when off. Work is *counted* exactly and *timed* only in phases — timing every
call of a method that runs millions of times would report the measurement rather
than the work.

The cause was four layers deep, and each layer only became visible once the one
above it was fixed:

```
discovery 1779 ms
  pair proposals          20 ms   (1%)     <- the suspected bottleneck
  materialize/create    1743 ms  (98%)
      attach evidence   1713 ms  (98%)     <- 2,400 AddStatement calls
          WeakenConflictingLinks  89%
              HasProperty, called 4x per pair
              LinkTypesAreExclusive
```

Attaching evidence links was quadratic: 600 statements took 130 ms, 2,400 took
1713 ms — four times the work for thirteen times the cost. Every new evidence
link on a template was compared against every link the template already had, and
each of those comparisons was far more expensive than it looked.

---

## 4. The changes

### `HasProperty` — the innermost cost

It was called four times for every pair of links compared. Each call:

- read `LinksTo`, which **copies the entire link list** and tests every link for
  expiry before the question can even be considered;
- compared `x.LinkType?.Label == "hasProperty"` — a string comparison per link;
- then walked `Ancestors`, allocating a queue and a set, and repeated the whole
  thing for each ancestor.

It now reads the link lists in place, compares the link type by reference against
the registered `hasProperty` Thought, and tests the *target* first — one
reference comparison that rejects almost every link immediately. It also returns
early when the Thought has no parents at all, which is the case for `Link`
objects, where setting up an inheritance walk cost more than the walk.

### `LinkTypesAreExclusive` — attributes gathered before they could matter

It computed `GetAttributes()` on **both** link types — each copying a link list
and comparing labels — and only then checked `r1.To == r2.To`, which its result
requires. The identity check now comes first.

### `LinksAreExclusive` — an exact early-out

This is the change that mattered most, and it is exact rather than heuristic.

`FindCommonParents(t, t1)` returns only the **direct parents of `t`**. Every
remaining test in `LinksAreExclusive` either requires the two links to share a
target, or looks for an exclusive Thought among the targets' common parents.
Therefore: *if the targets differ and no direct parent of the first target is
marked exclusive, nothing below can succeed.*

That condition depends only on the new link, so a caller comparing one new link
against many existing ones settles it **once** instead of once per comparison.

It is placed above the property tests deliberately. That is safe because
`LinkTypesAreExclusive` can only return true when the targets are the *same*
Thought, while this early-out only fires when they *differ* — the two cases
cannot overlap. The public two-argument `LinksAreExclusive` is unchanged; it
calls the new overload with the flag set to true, preserving the original
behaviour for every other caller.

### `WeakenConflictingLinks` — scan in place, mutate afterwards

It read `newSource.LinksTo` (a full copy plus an expiry walk) and then called
`.ToList()` on it, copying a second time — once per statement, against a list
growing to thousands of links.

It now examines the list in place and collects the few genuinely conflicting
links before changing anything, so the changes cannot disturb the scan. It also
returns immediately when the new link is itself a result or a condition, since
such a link never conflicts.

### `ReplaceThoughtReferences` — searching without copying the graph

It located references by reading every Thought's `LinksTo` property, which copied
every link list in the UKS and ran an expiry check over each, purely to search
them. It now reads the lists in place. A merge also no longer deletes things as a
side effect of looking for references.

### `HasAncestor` — answer the common case without allocating

Nearly every such question is settled by the Thought itself or a direct parent.
Those are now answered before the queue and set that a full breadth-first search
requires are ever created.

---

## 5. Results

| | before | after | |
|---|---|---|---|
| **template discovery @1200 phrases** | 1663 ms | **197 ms** | 8.4× |
| **discovery growth exponent** | 1.67 | **0.80** | no longer superlinear |
| ingest @1200 phrases | 949 ms | 797 ms | 1.2× |
| `ReplaceThoughtReferences` | 15.5 ms/merge | 5.8 ms | 2.7× |
| `CoalesceSimilarClasses` | 95.7 ms | 51.7 ms | 1.9× |
| `Delete` | 135 µs | 53 µs | 2.5× |
| `HasAncestor` (string) | 0.52 µs | 0.11 µs | 4.7× |
| `HasAncestor` (Thought) | 0.30 µs | 0.07 µs | 4.3× |
| `Parents` | 0.23 µs | 0.08 µs | 2.9× |
| `Children` @16 000 members | 536 µs | 404 µs | 1.3× |

The exponent falling below 1.0 is the important result. Discovery no longer
degrades faster than the corpus grows, so a larger corpus is now a matter of
patience rather than impossibility.

All 268 tests pass, including the 28 covering exclusivity, forgetting and
inheritance — the areas these changes could plausibly have broken, since
exclusivity governs how conflicting beliefs are resolved.

One honest caveat: `Children` improved from 99 µs to 58 µs, but *not* from the
rewrite described in §2, which measured flat on its own. The likely cause is
reduced garbage-collection pressure across the system once the allocations were
removed elsewhere. That is inference, not an isolated measurement.

---

## 6. On the benchmark itself

The benchmark had a defect which had to be fixed before any of this was legible:
its structural measurements were single-shot, and **varied fivefold between runs
of identical code** (9.1 → 21.5 → 50.6 ms). They now time several independent
merges and report the median, and are stable to about 4%.

Its assertions are deliberately about the **shape** of the cost rather than
absolute timings — "four times the members must not cost more than eight times
the time", "the growth exponent must stay below 2.6", "creation must not slow as
the graph grows". A threshold in milliseconds says more about the machine than
the code and fails for the wrong reasons; these hold on a slower machine and
still catch an operation quietly becoming quadratic.

---

## 7. Still open

- **Ingest.** `AddSequenceAndLink` at 0.363 ms is now the largest ingest cost:
  `AddSequence` runs a `FindSequencesByActivation` existence scan for every
  phrase. This is the next target if ingest matters.
- **`Delete` is still O(n).** `AtomicThoughts` is a `List<Thought>`, so
  `Remove` scans it. The fix is entangled: list *order* currently encodes
  creation age, which `CoalesceSimilarClasses` relies on to choose the canonical
  Thought of a merge, and `CreateInitialStructure` iterates the list by index
  while deleting from it. A creation ordinal on `Thought` would let all three
  change together.
- **`CreateInitialStructure` is O(n²)** for the same reason — it deletes Thoughts
  one at a time, and each delete scans the list. This runs on engine restart and
  file load, and is why clearing a large UKS appears to hang.
- **Hub access.** Reading a class with many members costs what it costs, because
  the links are scattered objects. Making it cheaper means either maintaining a
  separate members list per Thought — which requires also updating the seven
  places that mutate `LinksToWriteable` / `LinksFromWriteable` directly, in
  `UKS.Sequence.cs` and `UKS.Statement.cs` — or changing the storage layout.
- **The ceiling.** These are optimizations of the current design. Beyond roughly
  a million Thoughts the design itself binds: every Thought carries two
  `List<Link>`, every `Link` is a separate heap object, and traversal is pointer
  chasing. Going further means interned integer identifiers and struct-of-arrays
  adjacency — a rewrite of the storage layer, not a tuning pass.

---

## 8. Using the diagnostics

`SequenceDiscoveryDiagnostics` is off by default. To find where discovery is
spending time:

```csharp
SequenceDiscoveryDiagnostics.Reset();
SequenceDiscoveryDiagnostics.Enabled = true;
ModuleText.DiscoverPhraseTemplates(10, 1);
SequenceDiscoveryDiagnostics.Enabled = false;
Console.WriteLine(SequenceDiscoveryDiagnostics.Report());
```

`Tests/PipelineProfile.cs` does exactly this at two corpus sizes, and is the
quickest way to see whether a change helped or moved the cost somewhere else.

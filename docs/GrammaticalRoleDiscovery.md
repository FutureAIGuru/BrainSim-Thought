# Grammatical Role Discovery

This document describes the changes that let the text modules identify the
grammatical role of each learned class — subjects, verbs, articles, adjectives,
and predicates — from a corpus of observed phrases, without being given any
English grammar to start from.

It covers what was built, why each piece exists, how the pieces fit together,
what the measured results are, and where the current boundary lies.

---

## 1. Goal and approach

The system already learned **templates** from observed phrases: recurring
structures such as `the ??class2 are ??class3`, where fixed words (`the`, `are`)
are shared across many phrases and variable positions become wildcard-backed
learned classes. What it could not do was say what those positions or classes
*are*: which class is the subject, which the verb, which the article.

The approach here keeps the project's existing principle — **nothing begins from
English grammar**. Concretely:

1. **Positions are told apart by structure**, not by word identity. A position is
   described by where it sits relative to the word that separates the two halves
   of a template and to the words that introduce a following position.
2. **Positions that do the same job are merged by the populations that fill
   them.** Two positions are the same role when the same words appear in them.
3. **A role becomes *understood* only at the end**, by being bound to a part of
   an action the template performs. The English names (`subject`, `verb`, …) are
   read off those bindings; no step of discovery consults them.

This directly answers the slide-deck goal *"reliably identify the grammatical
role of each learned class"* while respecting the listed constraint that grammar
roles should be *understood concepts*, not merely anonymous classes, and the
constraint that action templates should be learned from examples rather than
hand-attached.

Two different notions are deliberately represented separately:

| | attaches to | example | ambiguous? |
|---|---|---|---|
| **Slot role** — subject, verb, article, predicate | a (template, position) pair | position 1 of `the ?? are ??` | no — one role per slot |
| **Lexical category** — article, noun, verb, adjective | a word class | `{a, an, the}` | yes — e.g. `runs` |

Roles are properties of positions; categories are properties of words. Categories
are *derived* by pooling the fillers of every position that carries a given role.

---

## 2. What changed, file by file

### New files

| File | Purpose |
|---|---|
| `UKS/UKS.SequenceRole.cs` | Generic mechanism: the `SlotRole` vocabulary, the parallel `hasRoles` sequence written beside a template's words, and role coalescing by population overlap. |
| `BrainSimulator/Modules/ModuleText.Grammar.cs` | The grammar-specific policy: discover function words, assign and merge role candidates, ground roles in actions, derive lexical categories. A `partial` extension of `ModuleText`. |
| `Tests/ModuleTextGrammarRoleTests.cs` | Gold-standard evaluation against the closed corpus vocabulary, plus grounding, separation, and idempotence tests. |

### Modified files

| File | Change |
|---|---|
| `UKS/UKS.SequenceBubble.cs` | `IsLearnableTemplatePattern` now permits **one** adjacent wildcard pair, opt-in via a new `maxAdjacentGapPairs` parameter on `DiscoverSequenceTemplates`. |
| `BrainSimulator/Modules/ModuleText.cs` | `DiscoverPhraseTemplates` opts in to one adjacent gap pair; `ProcessTheExistingText` runs `DiscoverGrammaticalRoles` as its final step; class marked `partial`. |
| `BrainSimulator/Modules/ModuleTextDlg.xaml.cs` | The "Discover structures" button's status line now reports role and category counts. |

---

## 3. The pipeline, phase by phase

The whole pass is driven by `ModuleText.DiscoverGrammaticalRoles()`, called at
the end of `ModuleText.ProcessTheExistingText()`. It runs over the templates that
template discovery has already learned.

### Phase 0 — Allow one adjacent wildcard pair

**File:** `UKS/UKS.SequenceBubble.cs`

Adjacent wildcards were previously forbidden entirely: any pattern with two gaps
in a row was rejected because a run of gaps matches almost any phrase. But that
rule also rejected the single most important adjective evidence in the corpus.

The 100 sentences of the form `the quiet dog can run` / `the sleepy dogs can eat`
share the subsequence `[the, can]`, which yields the pattern `the _ _ can _` —
two adjacent gaps. Under the old rule these produced **no template at all**, so
the attributive-adjective position (the word before the noun) was structurally
invisible.

`IsLearnableTemplatePattern` now counts adjacent-gap runs and accepts at most one
pair, still rejecting runs of three or more:

```csharp
private static bool IsLearnableTemplatePattern(
    CommonSequencePattern pattern,
    int maxAdjacentGapPairs = 0)
```

The parameter is threaded through `DiscoverSequenceTemplates`. The default stays
`0`, so the melody and spelling callers are unaffected; only
`DiscoverPhraseTemplates` opts in to `1`.

**Effect:** the corpus now learns `the ??adjective ??noun can ??verb`
(≈100 phrases of evidence), the template that makes attributive adjectives
learnable.

### Phase 1 — Slot-role vocabulary and the `hasRoles` sequence

**File:** `UKS/UKS.SequenceRole.cs`

A role has to live somewhere and has to be per-position, not per-word. This phase
adds:

- **`SlotRole`** — a root under `LanguageElement`; discovered roles are its
  anonymous children (`role0`, `role1`, …).
- **`hasRoles`** — a link type. Each template gains a `hasRoles` sequence written
  *beside* its `hasWords` sequence, the same length and index-aligned. Position
  *i* of `hasRoles` is the role of position *i* of `hasWords`.
- **`unassignedRole`** — a placeholder written where a position has no role yet,
  so the two sequences stay the same length. It is deliberately *not* a member of
  `SlotRole`, so enumerating discovered roles never returns it.

Key methods:

| Method | Does |
|---|---|
| `AssignSlotRoles(owner, elementLinkType, roles)` | Writes the role sequence. If an identical sequence already exists it is left in place, so reruns don't grow the graph. |
| `GetSlotRoleView(owner, elementLinkType)` | Reads elements and roles together as a `SlotRoleView`. |
| `CoalesceSimilarRoles(populationsByRole, …)` | Merges roles whose supplied populations overlap (see Phase 3). |

Why a parallel sequence rather than a property on the wildcard? Because the same
wildcard can appear twice in one template — `the ??class8 visit the ??class8`
uses one wildcard thought in both subject and object position. A property on the
wildcard could not tell those two positions apart; a parallel sequence can.

### Phase 2 — Distributional role-candidate discovery

**File:** `ModuleText.Grammar.cs` — `DiscoverFunctionWords`, `MarkStructuralPositions`, `AssignRoleCandidates`, `DescribePosition`

This phase reads each template as a `TemplateStructure` and gives every position
a **role candidate** based purely on structure.

**Function words first.** `DiscoverFunctionWords` finds two closed populations by
counting how fixed words sit relative to positions:

- A **separator** is a fixed word that predominantly *follows* a position
  (`is`, `are`, `has`, `have`, `can`). It stands between the two halves of a
  template.
- An **introducer** is a fixed word that predominantly *precedes content*
  (`a`, `an`, `the`). Introducers are recognized only after separators are, and
  are measured against "content that follows" rather than "a learned position
  that follows" — otherwise `an` looks introducing in `is an ??` but not in
  `is an animal` and is missed. `an` is additionally admitted because it *only*
  ever precedes content and never follows a position, which is conclusive even
  though it appears in too few templates to meet the frequency threshold.

**Then each position is described.** `MarkStructuralPositions` finds each
template's separator (a fixed separating word, or — for a transitive frame like
`the dog sees a pig` with no closed-population verb — the lone position that
nothing introduces, with introduced positions on both sides). `DescribePosition`
then names each position's situation:

```
side | introduced-or-bare | adjacent-or-distant | which-separator-governs-it
```

for example `before|introduced|distant|w:is` or `after|bare|adjacent|w:can`.
Positions with the same description share a role candidate. Keying on the
governing separator is deliberate: the same bare position after `is` holds a
quality, after `can` holds an action.

### Phase 3 — Coalesce roles by filler-pool overlap

**File:** `ModuleText.Grammar.cs` (the `CoalesceSimilarRoles` call) + `UKS.SequenceRole.cs` (the mechanism)

Phase 2 over-splits: subject-of-`is`, subject-of-`has`, and subject-of-`can`
start as different candidates. This phase merges candidates whose **filler
populations** substantially overlap — the same ~100 animal words fill all three
subject candidates, so they become one Subject role.

Crucially, coalescing runs **within each side of the separator separately**:

```csharp
foreach (var partition in populations.GroupBy(entry => SideOf(DescriptorOf(entry.Key))))
    theUKS.CoalesceSimilarRoles(group, minRoleOverlap);
```

Without that partition, the subject pool and the object pool — both nouns —
would merge into a single role, losing the subject/predicate distinction. On
opposite sides of the verb, identical populations are still different roles.

This is also what keeps adjectives apart from predicate nominals: the pool after
`is` that also appears as a thing (`animal`, `dog`) does not overlap the pool of
qualities (`brown`, `large`), so they never merge.

### Phase 4 — Ground roles in action arguments

**File:** `ModuleText.Grammar.cs` — `GroundRolesInActions`

Up to here roles are anonymous. This phase makes them *understood* by binding
them to the actions their templates perform. The corpus attaches a few
demonstrated actions (e.g. `A dog is an animal. [dog->SET.is-a->animal]`), which
template learning has already turned into parameterized `means` links.

For each template with such an action, the position that supplies the action's
**source** is bound to `actionSource`, the **target** to `actionTarget`, and the
separator to `actionRelation`:

```
role  --supplies-->  actionSource
```

The English names are then read off these bindings and attached **as parents**,
not as renames:

| binding | name (parent under `GrammaticalRole`) |
|---|---|
| supplies `actionSource` | `subjectRole` |
| supplies `actionTarget` | `predicateRole` |
| supplies `actionRelation` | `verbRole` |
| descriptor is `introducer` | `articleRole` |

Parents rather than labels because one English name can cover several discovered
roles: singular and plural subjects are two distinct roles, and both are
subjects. A rename could not represent that; a shared parent can. Only a handful
of exemplars are needed because grounding happens *after* coalescing — eight
demonstrated actions ground the roles shared by hundreds of templates.

### Phase 5 — Propagate roles to unanchored templates

**File:** `ModuleText.Grammar.cs` — `RecoverFrozenPositions`

Most templates carry no exemplar, and some froze a position into a single
literal word because that word dominated the observations — e.g. `the ?? can
bark` instead of `the ?? can ??`. `RecoverFrozenPositions` reduces each template
to a **shape** (introducer `D`, separator `R`, everything else `_`) and, where a
template of the same shape keeps a position where another has a frozen word,
copies the role across. This gives the frozen `bark` the same role the general
template's verb position has.

### Phase 6 — Derive lexical categories from role pools

**File:** `ModuleText.Grammar.cs` — `DeriveLexicalCategories`

Finally, roles (properties of positions) become categories (properties of words)
under a `LexicalCategory` root. The pools are combined with structural evidence,
and the categories that cannot be told apart by *where they occur* are told apart
by *where they never occur*:

- **article** = the introducer pool → `{a, an, the}`
- **noun** = words in a position that names a thing: an introduced complement, or
  the position adjacent to the separator on the subject side.
- **adjective** = words in a bare position after a *thing-linking* separator
  (one that elsewhere takes an introduced complement, like `is … an animal`), or
  the earlier of two positions before the separator (`the quiet dog`) — **minus**
  anything already known to be a thing.
- **verb** = the separator pool, plus bare positions after separators that are
  *not* thing-linking (`can bark`) — minus things and qualities.

A subtle propagation makes the true-template corpus work: `are` is never written
with an introduced complement in that corpus (`dogs are animals`, no article),
but it shares its bare complements with `is`, which *is* thing-linking. So `are`
inherits thing-linking status by complement overlap, and `dogs are brown` is read
as a quality rather than an action.

---

## 4. Results

Measured by `Tests/ModuleTextGrammarRoleTests.cs` against the corpus's known,
closed vocabulary.

### `bst_simple_1000_corpus.txt` — perfect

| category | precision | recall |
|---|---|---|
| article | 100% | 100% |
| noun | 100% | 100% (100/100) |
| verb | 100% | 100% (46/46) |
| adjective | 100% | 100% (25/25) |

Grounding produces disjoint subject and predicate role sets; every template
position receives a role.

### `bst_true_template_corpus.txt`

- articles exactly `{a, an, the}`
- 13 qualities (`brown, large, small, black, white, green, tall, short, …`)
  correctly separated from nouns and verbs
- nouns and verbs clean; subject and predicate roles disjoint

---

## 5. Current boundary

Every remaining error on the true-template corpus is the **singular/plural
classification** case, and it is a known dependency on later work, not a defect
in this one.

`A dog is an animal` establishes `animal` as a *thing* (it takes an article). But
nothing yet relates the plural `animals` to the singular `animal`, so
`dogs are animals` is structurally identical to `dogs are brown` — a bare word
after `are` — and `animals`, `tails`, `plants` are read as qualities.

Resolving this needs the singular↔plural relation (a separate planned phase). The
test suite **asserts this boundary explicitly** rather than hiding it:

```csharp
foreach (string plural in new[] { "animals", "tails" })
    Assert.DoesNotContain(plural, nouns);
```

When the plural work lands, these assertions flipping is the signal that it
worked.

---

## 6. Notes for future work

- **Word-label consistency.** `ModuleText.AddPhrase` creates words as `w:dog`;
  some existing tests create them as bare `dog`. When both paths run over one
  corpus they build two parallel vocabularies (`a` vs `w:a`) that split every
  statistic this discovery depends on. `ModuleTextGrammarRoleTests` routes its
  corpus loading through `ModuleText.AddPhrase` to avoid this. The older
  `ModuleTextSequenceBubbleEvaluationTests` still uses bare labels and passes
  only because it strips the prefix in its assertions; a separate cleanup is
  warranted.
- **Idempotence.** `DiscoverGrammaticalRoles` is safe to rerun: descriptor
  markers and role sequences are reused rather than recreated, so a second pass
  over unchanged templates adds nothing to the UKS. This is covered by
  `RediscoveringRolesAddsNothingToTheUks`.
- **Thresholds** (`minFunctionWordTemplates`, `minRoleOverlap`,
  `maxAdjacentGapPairs`) are parameters with corpus-tuned defaults, consistent
  with the project's existing manually-selected thresholds. They are the natural
  knobs to revisit on a larger or less structured corpus.

# Grammatical Role Discovery

This document describes the changes that let the text modules identify the
grammatical role of each learned class — subjects, verbs, articles, adjectives,
and predicates — from a corpus of observed phrases, without being given any
English grammar to start from. It also covers the follow-on steps that build on
those roles: relating singular and plural word forms, telling a classification
(`is-a`) apart from an attribute assertion (`is`), and learning what an observed
question asks so it can be answered from what is already known.

It covers what was built, why each piece exists, how the pieces fit together,
what the measured results are, and what is and is not covered.

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
| `UKS/UKS.Actions.cs` | Adds `ApplyTestAction`, the read-only counterpart of `ApplySetAction`; both now share one `GetActionRelationship` helper. |
| `UKS/UKS.Query.cs` | `SearchForRelationships` matches the relationship by inheritance when searching **backwards**, as it already did forwards (see §7). |
| `BrainSimulator/Modules/ModuleText.cs` | `DiscoverPhraseTemplates` opts in to one adjacent gap pair; `ProcessTheExistingText` runs `DiscoverGrammaticalRoles` (which now also relates number) as its final step; class marked `partial`. |
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

### Phase 7 — Learn the singular/plural relation

**File:** `ModuleText.Grammar.cs` — `DiscoverNumberRelation`

Phase 6 leaves one gap. A plural noun that is only ever seen as a *complement* —
`animals` in `dogs are animals`, `tails` in `dogs have tails`, neither of which
ever appears as a subject — has no position that marks it as a thing, so it is
mistaken for a quality. Its singular (`animal`, `tail`) *is* known to be a thing,
because `a dog is an animal` places it after an article. The two forms need to be
tied together.

`DiscoverNumberRelation` learns that tie from the spellings the words already
carry, **not** from a built-in pluralizer (the deck asks specifically to replace
English-specific spelling assumptions with learned ones):

1. Over the known nouns, count the suffix that most often turns one noun into
   another (`dog`→`dogs`, `cat`→`cats`, …). In English this discovers `s`; the
   method never assumes it, and a different corpus would settle on a different
   ending. The rule is stored as ordinary knowledge (`pluralSuffix:s` under
   `NumberTransform`).
2. For each singular noun `s`, if `s` + suffix is a known word `p`, then `p` is
   the plural of `s`: it is marked a noun (correcting any earlier guess that put
   it among qualities or actions), it is linked `p --means--> (s's concept)` so
   it denotes the same thing, and the relation `p --pluralOf--> s` is recorded.

This makes `animals` a thing that means `animal`, and `tails` a thing that means
`tail`.

### Phase 8 — Distinguish assertions from classifications

**No new apply-path code was needed.** This is worth stating plainly, because the
plan expected a change here and the code turned out not to require one.

`dogs are animals` (a classification → `is-a`) and `dogs are brown` (an assertion
→ `is`) share a surface. `LearnActionsFromExemplars` already produces two
separate action templates for `?? are ??` — one per SET type — because it groups
exemplars by relation before discovering templates. The remaining question is
only which of the two a given phrase should use, and the existing selection in
`ApplyExistingTemplatesToPhrase` already answers it: it scores each candidate by
how many of the phrase's words fall into that template's learned slot classes.

The `is-a` template's complement class contains `animals`; the `is` template's
contains qualities like `brown`. Once Phase 7 has made `animals` a thing and
given it its singular meaning:

- `dogs are animals` scores higher on the `is-a` template → asserts
  `[dog -is-a-> animal]` (with the singular concepts, via the Phase 7 meaning
  links).
- `dogs are brown` scores higher on the `is` template → asserts
  `[dog -is-> brown]`.

So Phase 8 is delivered by Phase 7 plus code that already existed. The singular
case (`a dog is an animal` vs `a dog is brown`) was already unambiguous, because
the article before the complement puts those two in *different* templates.

### Phase 9 — Separate questions from statements

**File:** `ModuleText.cs` — `GetPhraseKind`, `GetPhrasesOfKind`, `DiscoverQuestionTemplates`

261 of the corpus's 903 lines — **29%** — are questions, and every one of them was
being discarded by a temporary line in `LoadTextFromFile`
(`if (phrase.ToLower().Contains("what")) continue;`). That line is gone.

Each observed phrase is now filed under the kind of utterance it is, taken from
the mark it ends with: `Question` or `Statement`, both under `Phrase`. Using the
punctuation rather than the word "what" keeps the split language-neutral — and it
lets the system go on to *discover* that "what" is the word characteristic of
question phrases, the same way it discovered articles and separators.

Templates are then discovered per kind, into separate roots (`LearnedTemplate`
for statements, `LearnedQuestionTemplate` for questions). Keeping the populations
apart is what stops questions from distorting the roles and categories measured
in Phases 2–6 — those results are unchanged by this phase.

The learned question templates are exactly the corpus's question forms:

```
what is a ??class3          what does a ??class3 have
what are ??class7           what do ??class7 have
what can a ??class17 do     what can ??class20
what has a ??class21        what can ??class18 do
```

### Phase 10 — `ApplyTestAction`

**File:** `UKS/UKS.Actions.cs`

Slide 5 of the design deck promises `[Fido → TEST.is-a → dog]` acting "as TRUE
if the relationship already exists," but only `ApplySetAction` existed. The
dotted-type machinery was already in place — `AddActionTypeInheritance` in
`UKS.cs` has always handled `TEST.` as well as `SET.` — so only the execution
was missing.

`ApplyTestAction` is the read-only counterpart of `ApplySetAction`: it resolves
`TEST.can` to the underlying `can` and **queries** instead of asserting. A
wildcard at either end leaves that end open, so the action asks *which* Thoughts
stand in the relationship rather than whether two particular ones do. Both
methods now share one `GetActionRelationship` helper rather than duplicating the
parent-resolution logic.

### Phase 11 — Learn what a question asks

**File:** `ModuleText.Grammar.cs` — `LearnQuestionsFromStatementTemplates`

A question and the statement that answers it share the word naming the
relationship, and the words a question accepts in its open position are the words
standing at one end of that relationship. Matching those two populations says
which end the question *supplies*, and therefore which end it *asks for*:

- `what can a ??` — the open position holds animals, which are what stands at the
  **source** of `can`. So the question supplies the source and asks for the
  target → `[dog → TEST.can → ??]`
- `what can ??` — the open position holds `bark`, `swim`, `hop`, which stand at
  the **target**. So it asks for the source → `[?? → TEST.can → bark]`

Two details make this work on the real corpus:

- **Evidence is pooled per relationship, not per template.** The same possession
  is written `has` beside one thing and `have` beside several, so `what does a
  dog have` matches `SET.has` through `have` even though the singular statement
  says `has`. Without pooling, that question learns nothing.
- **Overlap is measured against the smaller population.** Only words carried by
  an exemplar reach an assertion's ends, so that population is far smaller than
  the one a question accepts. Dividing by the smaller of the two asks whether one
  sits inside the other rather than whether they are the same size; at least two
  shared words are required so a single coincidence cannot decide it.

A question whose surface does not settle which relationship is meant acquires
**more than one** TEST action — `what is a dog` asks both `is-a` and `is` — and
answering reports all of them rather than inventing a preference.

### Phase 12 — Answer

**File:** `ModuleText.Grammar.cs` — `AnswerQuestion`

A question phrase is matched to a learned question template, the supplied word is
resolved to its meaning, the open end is left open, and each TEST action is run.
Results are converted back to words, preferring the singular form (the one the
plural was derived from) as the citation form.

Asking is read-only: `AskingChangesNothing` asserts that answering a question
adds no Thoughts and no links.

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
- complement-only plurals (`animals`, `tails`, …) correctly recognized as things
- **assertions distinguished from classifications**: `dogs are animals` asserts
  `[dog -is-a-> animal]`, `dogs are brown` asserts `[dog -is-> brown]`, each with
  the opposite relation confirmed absent

### Questions

All 16 learned question templates ask at least one relationship, except the two
two-slot forms which this step does not yet interpret. Answers are read back out
of relationships that were only ever asserted by statements:

| question | answer |
|---|---|
| `What can a dog do?` | `bark` |
| `What does a dog have?` | `tail` |
| `What can bark?` | `dog` |

The third is the inverse direction — supplying the target and asking for the
source — which exercises a different path through the query engine.

---

## 5. What is covered, and what is not

The role and category work (Phases 0–6) plus the number relation (Phase 7) and
the assertion/classification distinction (Phase 8) together deliver, on the two
structured corpora:

- every learned class identified as subject, verb, article, adjective, or
  predicate, grounded in the actions its templates perform;
- singular and plural noun forms related by a learned suffix rule;
- classifications (`is-a`) told apart from attribute assertions (`is`), in both
  singular and plural surface forms.

The test suite records the singular/plural boundary explicitly rather than
leaving it implicit — the assertions that once documented the gap now document
its closure:

```csharp
foreach (string plural in new[] { "animals", "tails" })
    Assert.Contains(plural, nouns);           // Phase 7 recognizes plural things
```

```csharp
ModuleText.AddPhrase("dogs are animals", applyExistingTemplates: true);
Assert.NotNull(uks.GetLink(dog, isA, animal)); // classification
Assert.Null(uks.GetLink(dog, is, animal));     // and not an assertion
```

### Still open (out of scope here)

These remain future work, consistent with the project's roadmap:

- **Attribute-type questions.** `What color is the dog?` and `What size is the
  dog?` ask for an attribute *of a named kind*. The templates are discovered and
  do bind TEST actions, but answering them correctly needs `color` and `size` to
  be known as attribute categories — deliberately deferred.
- **Two-slot questions.** `what ??class12 is the ??class3` has two open
  positions; `LearnQuestionsFromStatementTemplates` currently interprets only
  single-unknown questions and skips these.
- **The legacy query path.** `ModuleTextIn.FindAndMapTemplates` still reads the
  hand-authored `tpl:*` templates in `UKSContent/QueryTemplates.txt`, which use
  the older `hasWords`/`outputs` format. The learned question templates are a
  parallel mechanism; replacing the legacy path was deliberately not attempted.
- **Irregular plurals.** The learned suffix relates regular forms
  (`dog`/`dogs`); `mouse`/`mice`, `goose`/`geese` are not paired. This is an
  honest limit of a single learned transformation, not a bug — such pairs simply
  stay unrelated.
- **Larger, less structured corpora.** All results are on closed, deliberately
  structured vocabularies. Real prose (`tinyStories.txt`) would need punctuation
  and clause handling not attempted here.
- **Ambiguous meanings, questions, durability, natural-language output** — the
  remaining items on the deck's "Next Steps" and "Shortcomings" slides.

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
  markers, role sequences, and the number relation are reused rather than
  recreated, so a second pass over unchanged templates adds nothing to the UKS.
  This is covered by `RediscoveringRolesAddsNothingToTheUks`.
- **Thresholds** (`minFunctionWordTemplates`, `minRoleOverlap`,
  `maxAdjacentGapPairs`, `minFillerOverlap`) are parameters with corpus-tuned
  defaults, consistent with the project's existing manually-selected thresholds.
  They are the natural knobs to revisit on a larger or less structured corpus.

---

## 7. A fix made to shared query code

`SearchForRelationships` in `UKS/UKS.Query.cs` matched the relationship
**by inheritance** when searching forwards from a known source:

```csharp
link.LinkType?.HasAncestor(linkType) == true      // forward
```

but by **exact equality** when searching backwards from a known target:

```csharp
link.LinkType == linkType                          // backward, before
```

The consequence was that a search for `can` found the `SET.can` links that
inherit from it in one direction but not the other — so `What can a dog do?`
could be answered and `What can bark?` could not, from the same knowledge. The
backward branch now uses the same inheritance test as the forward one.

This is shared code with two callers (`ModuleTextIn.SubmitText` and the new
`ApplyTestAction`). The change strictly widens what the backward search finds, in
the direction the forward search already went; `HasAncestor` includes the Thought
itself, so previously-exact matches still match.

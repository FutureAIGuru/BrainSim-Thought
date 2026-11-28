# UKS public API: plan and reference

The plan for the UKS's public API before publication (2027-02-01): what stays public, what becomes
internal, what moves out, and the order of the work. Each member's fate is checked off as its phase lands.

## Principles

1. **Public types:** `UKS`, `Thought`, `Link`, `SeqElement`, plus the input type `ThoughtRef` and the
   read-only `ThoughtList`. Results are Thoughts, links, sequences, `ThoughtList`, or search tuples.
2. **Every label is resolved by a named store** (`uks.Labeled("dog")`, `thought.Store`, or a method
   taking a `ThoughtRef`). There is no ambient store and no implicit string-to-Thought conversion.
3. **Identity, not labels.** Two Thoughts are equal only if they are the same object; labels find
   Thoughts and are never compared.
4. **One way to do each thing**, with consistent verbs (`Add`, `Remove`, `Has`, `Get`); always "link".
5. **Test-only members are `internal`** (InternalsVisibleTo Tests and UKS.Tests).

## Layers

- **Core:** the representation and its basic operations: Thoughts, links, inheritance, queries,
  firing and time, sequences, files, undo.
- **Conveniences:** thin helpers built on the core that never compare labels
  (e.g. `GetTargetOfFirstLinkOfType`).
- **Agents:** processes that reorganize the store (abstract, consolidate, count, prune). They may use
  only the public API, are packaged as extension methods in their own file and namespace, and can be
  run on demand or with `RunPeriodically`. The clock and TimeToLive expiry stay in the core.

## ThoughtRef

A parameter type holding a Thought or a label, converted implicitly from either. Converting a string
only stores the label; the receiving method resolves it in its own store. A bare `null` is ambiguous
(it could be either), so write `default` for "none". Each method uses one resolution policy:

| Policy | Unknown label | Used by |
|---|---|---|
| Find | answers "no" (null/false), nothing created | queries: `HasLink`, `HasProperty`, `HasAncestor`, ... |
| Require | does nothing, returns null/false | operations on an existing Thought: `RemoveLink`, `RemoveParent`, ... |
| GetOrAdd | creates it (under LinkType for a link type, else Unknown) | writes: `AddStatement`, `AddLink`, `AddParent`, ... |

A value that is neither a Thought nor a string doesn't compile; a Thought from another store throws.
Applied (Phase B):
- Thought: `AddLink`, `RemoveLink`, `RemoveLinks`, `HasLink`, `AddParent`, `RemoveParent`, `RemoveChild`,
  `HasAncestor`, `HasProperty`, `AddProperty`, the `Get...OfType` helpers.
- UKS: `AddStatement`, `AddStatementRecordingHistory`, `GetOrAddThought` and `AddThought` (parent),
  `GetLink`, `GetAttributes`, `SearchByAttributes` (root), `ExportTextFile` (root),
  `FindSequencesByActivation` (options and classes), `AddSequenceAndLink` (link type),
  `CreateFirstElement`/`AddElement`/`InsertElement` (value), `GetElementValue` (class),
  `GetReferringThoughts` (link type).

`AddParent` finds `is-a` but never creates it (creating a Thought adds its parent, which would recurse).
Parameters that take a Thought built for the call (query prototypes, patterns, sequence elements,
lists) stay `Thought`.

## Decisions

Settled:
- Sequence editing: `AddElement` and `InsertElement` merge into
  `InsertAfter(SeqElement element, ThoughtRef value, bool atBeginning = false)`; with `atBeginning`,
  `element` is the first element, which keeps its identity. `CreateFirstElement` stays (a sequence that
  will grow starts there); `AddSequence` keeps its compression and reuse behavior.
  `RemoveFirstSequenceElement` and `GetPreviousSequenceElement` move to ModuleSequenceSegmenter.
- Spelling helpers leave the UKS: letters are language content, not store mechanics.
- Query annotations on Link (`InheritanceDepth`, `InheritedFromCategory`) become internal.
- Naming: `isPlastic` to `IsPlastic`, `maxWeight` to `MaxWeight`, `V` to `Value`,
  `SaveUKStoXMLFile`/`LoadUKSfromXMLFile` to `SaveXml`/`LoadXml`.
- `ThoughtList` is the read-only type for everything list-like, links included (plain, not generic:
  a caller needing links casts, e.g. `.OfType<Link>()`); a caller needing a writable list calls `.ToList()`.

Open:
- `ThoughtRef` parameter names currently end in `Ref` (`sourceRef`, `valueClassRef`), which shows in
  IntelliSense, named arguments and the generated documentation. Rename them to the plain names
  (`source`, `valueClass`) in Phase H?
- Require policy: silent, or a debug warning for an unknown label?
- Whether `AddStatementRecordingHistory` folds into `AddStatement`.
- The user's review of the wrapper methods marked Review below.

Deferred (known issues, not API):
- Editing a sequence that other owners share changes it for all of them and leaves the sequence
  cache stale. Editing an existing sequence is rare.
- The sequence cache may be unnecessary now that activation search has been rewritten.
- Thread safety (see UKS.Tests/UKSMultiThreadTests.cs, which fails on purpose).
- `DeleteSequence` leaves the owner's link pointing at the deleted elements.

## Phases

Each phase ends with the solution, UKSWeb and iOS building, all tests green, and a review stop.

- [x] **A. Reference list** (this file)
- [x] **B. ThoughtRef** on every public method; collapse the Thought/string overload pairs
- [x] **C. Remove the ambient store:** the implicit operator, `Thought.IsA`, `ThoughtLabels` and
      `UKS.theUKS` are deleted; label indexing is internal to the store (`IndexLabel`/`UnindexLabel`);
      the Thought, Link and SeqElement constructors require their store; `new UKS()` no longer takes `clear`.
      Hosts hold their own store (desktop `MainWindow.theUKS`, iOS `Exports.Store`, UKSWeb per session).
- [ ] **D. Visibility:** Internal members become internal; Delete members are deleted; Move members move
- [ ] **E. Identity equality:** remove the label-based `Equals`, `GetHashCode`, `==` and `!=`
- [ ] **F. Agents layer:** extension methods in their own file and namespace, including the
      sequence/template machinery
- [ ] **G. Conveniences:** no label comparisons; apply the wrapper review
- [ ] **H. Naming** (the renames above) and `ThoughtList`
- [ ] **I. Documentation:** upgrade every public member's XML doc comment so Doxygen works again
      (`UKS/Doxyfile`, output to `UKS/Documentation`; about 140 of 226 public declarations have a
      comment today). The book's readers will read these, so: one-sentence summary, `<param>` and
      `<returns>` where they add something, no book references, "link" not "relationship".
      Run Doxygen and fix its warnings.
- [ ] **J. Freeze:** a public API baseline checked at build time, so any change to the public
      surface fails the build until the baseline is updated on purpose

## Members

"Overloads" counts the declarations sharing a name. Fates:
**Core**, **Convenience**, **Agent**, **Internal** (stays, not public), **Move** (out of the UKS
to a module), **Delete**, **Review** (your wrapper review decides).

Totals by name: Core 92, Internal 57, Agent 15, Review 12, Delete 9, Move 6, Convenience 5 (196 names).


### UKS

| Member | Fate | Note | Done |
|---|---|---|---|
| `AddCounts` | Agent |  | |
| `AddElement` | Core | merge into InsertAfter(element, value, atBeginning) | |
| `AddSequence` | Core |  | |
| `AddSequenceAndLink` | Core |  | |
| `AddStatement` | Core |  | |
| `AddStatementRecordingHistory` | Core | fold into AddStatement (open) | |
| `AddThought` | Core | fold into GetOrAddThought | |
| `AdvanceCycle` | Core |  | |
| `ApplyTestOrSetAction` | Core |  | |
| `BalanceTree` | Agent |  | |
| `BeginTransaction` | Internal | ChangeUndoably/UndoLast are the public undo | |
| `BubbleAttributes` | Agent |  | |
| `BubbleSharedSequences` | Agent | sequence/template machinery; its result types become internal | |
| `CanUndo` | Core |  | |
| `ChangeUndoably` | Core |  | |
| `CharacterPrefix` | Move | letters are language content: word module | |
| `ClearSequenceCache` | Internal |  | |
| `CompareSequences` | Internal |  | |
| `Copy` | Core |  | |
| `CreateClasses` | Agent |  | |
| `CreateFirstElement` | Core |  | |
| `CreateInitialStructure` | Core |  | |
| `CreateInstanceOf` | Review |  | |
| `CreateMinimumStructureForTests` | Internal | test-only | |
| `CreateNewTemplate` | Agent | sequence/template machinery; its result types become internal | |
| `CreateSearchOptions` | Core |  | |
| `CreateSpellingSequence` | Move | letters are language content: word module | |
| `CreateThoughtFromMultipleAttributes` | Review |  | |
| `CreateWildcard` | Core |  | |
| `CurrentTransaction` | Internal | replaced by a store method for "created in this change" | |
| `Cycle` | Core |  | |
| `CyclesPerSecond` | Core |  | |
| `DeleteAllChildrenAndLinks` | Review |  | |
| `DeleteSequence` | Core |  | |
| `DiscoverSequenceTemplates` | Agent | sequence/template machinery; its result types become internal | |
| `EndTransaction` | Internal | ChangeUndoably/UndoLast are the public undo | |
| `EnumerateSequenceElements` | Core |  | |
| `ExportTextFile` | Core |  | |
| `FileName` | Core |  | |
| `FilterResults` | Delete | dead | |
| `FindCommonSequence` | Agent | sequence/template machinery; its result types become internal | |
| `FindSequenceSpansByActivation` | Internal | reached through FindSequencesByActivation options | |
| `FindSequencesByActivation (2)` | Core |  | |
| `FindSequencesOutOfOrder` | Internal | reached through FindSequencesByActivation options | |
| `FlattenSequence` | Core |  | |
| `GetActionRelationshipType` | Internal |  | |
| `GetAttributes` | Core |  | |
| `GetCharacterLabel` | Move | letters are language content: word module | |
| `GetElementValue` | Core |  | |
| `GetLastlement` | Internal | test-only | |
| `GetLink (2)` | Review |  | |
| `GetLinkWeight` | Review |  | |
| `GetLinks` | Review |  | |
| `GetOrAddCharacter` | Move | letters are language content: word module | |
| `GetOrAddThought` | Core |  | |
| `GetOrCreateThoughtClass` | Agent | sequence/template machinery; its result types become internal | |
| `GetPreviousSequenceElement` | Move | to ModuleSequenceSegmenter | |
| `GetReferrer` | Delete | dead | |
| `GetReferringThoughts` | Internal |  | |
| `GetSequenceBindings` | Agent | sequence/template machinery; its result types become internal | |
| `GetSequenceLength` | Internal |  | |
| `GetSequenceLinks (2)` | Agent | sequence/template machinery; its result types become internal | |
| `ImportTextFile` | Core |  | |
| `InsertElement` | Core | merge into InsertAfter(first, value, atBeginning: true) | |
| `IsConditionalLinkType` | Internal |  | |
| `IsSequenceElement` | Core |  | |
| `IsSequenceFirstElement` | Core |  | |
| `Labeled` | Core |  | |
| `LabeledStartingWith` | Core | new (Phase C): label completion and search, replacing `ThoughtLabels.LabelList` | done |
| `LinkCount` | Core |  | |
| `LinksAreExclusive` | Internal |  | |
| `LoadUKSfromXMLFile` | Core | rename to LoadXml | |
| `MaterializeCommonSequence` | Agent | sequence/template machinery; its result types become internal | |
| `ProcessSingleLine` | Core |  | |
| `ProcessSingleLineWithNesting` | Core |  | |
| `PromoteToSeqElement` | Internal |  | |
| `RememberForUndo` | Internal | ChangeUndoably/UndoLast are the public undo | |
| `RemoveFirstSequenceElement` | Move | to ModuleSequenceSegmenter | |
| `RemoveLastSequenceElement` | Delete | dead | |
| `ReplaceThoughtReferences` | Agent | with class coalescing | |
| `Reset` | Core |  | |
| `Roots` | Core |  | |
| `RunPeriodically` | Agent |  | |
| `SaveUKStoXMLFile` | Core | rename to SaveXml | |
| `SearchByAttributes` | Core |  | |
| `SearchForRelationships` | Review |  | |
| `SequenceElementCount` | Core |  | |
| `SequenceMatchesPattern` | Agent | sequence/template machinery; its result types become internal | |
| `StartClock` | Core |  | |
| `StopClock` | Core |  | |
| `SubclassExists` | Review |  | |
| `SuppressFiring` | Core |  | |
| `ThoughtCount` | Core |  | |
| `ThoughtFired` | Core |  | |
| `ThoughtsHaveSimilarLink` | Review |  | |
| `constructor` | Core |  | |
| `Undo` | Internal | ChangeUndoably/UndoLast are the public undo | |
| `UndoLast` | Core |  | |
| `Why` | Core |  | |
| `WhyNot` | Core |  | |
| `WriteTheLink` | Internal |  | |
| `theUKS` | Delete | ambient store (Phase C) | done |

### Thought

| Member | Fate | Note | Done |
|---|---|---|---|
| `AddDefaultLabel` | Internal |  | |
| `AddLink (2)` | Core |  | |
| `AddParent` | Core |  | |
| `AddProperty` | Core |  | |
| `Ancestors` | Core |  | |
| `AncestorsWithSelf` | Convenience |  | |
| `Children` | Core |  | |
| `CreatedCycle` | Core |  | |
| `Delete` | Core |  | |
| `Descendants` | Core |  | |
| `EnumerateSubThoughts` | Review |  | |
| `Equals` | Delete | label-based equality becomes identity (Phase E) | |
| `Fire` | Core |  | |
| `FiredWithin` | Core |  | |
| `Forever` | Core |  | |
| `GetAttributes` | Review |  | |
| `GetHashCode` | Delete | label-based equality becomes identity (Phase E) | |
| `GetSourceOfFirstLinkOfType` | Convenience | resolve the label once, then compare identity (no label comparison) | |
| `GetTargetOfFirstLinkOfType (2)` | Convenience | resolve the label once, then compare identity (no label comparison) | |
| `GetTargetOfHighestWeightLinkOfType` | Convenience | resolve the label once, then compare identity (no label comparison) | |
| `HasAncestor (2)` | Core |  | |
| `HasGeneratedLabel` | Internal |  | |
| `HasLink (2)` | Core |  | |
| `HasProperty (2)` | Core |  | |
| `IsA` | Delete | uses the ambient store; write store.Labeled("is-a") | done |
| `Label` | Core |  | |
| `LastFiredCycle` | Core |  | |
| `LinksAsType` | Core |  | |
| `LinksFrom` | Core |  | |
| `LinksFromWriteable` | Internal |  | |
| `LinksTo` | Core |  | |
| `LinksToWriteable` | Internal |  | |
| `Parents` | Core |  | |
| `RemoveChild` | Review |  | |
| `RemoveLink (2)` | Core |  | |
| `RemoveLinks` | Convenience |  | |
| `RemoveParent (2)` | Core |  | |
| `Store` | Core |  | |
| `constructor (2)` | Core |  | |
| `TimeToLive` | Core |  | |
| `ToString` | Core |  | |
| `V` | Core | rename to Value | |
| `Weight` | Core |  | |
| `isPlastic` | Core | rename to IsPlastic | |
| `maxWeight` | Core | rename to MaxWeight | |
| `operators (3)` | Delete | implicit string-to-Thought (Phase C, done) and label-based ==/!= (Phase E) | |

### Link

| Member | Fate | Note | Done |
|---|---|---|---|
| `From` | Core |  | |
| `InheritanceDepth` | Internal | query annotations, kept for later use | |
| `InheritedFromCategory` | Internal | query annotations, kept for later use | |
| `constructor (3)` | Core |  | |
| `LinkType` | Core |  | |
| `To` | Core |  | |
| `ToString` | Core |  | |

### SeqElement

| Member | Fate | Note | Done |
|---|---|---|---|
| `FRST` | Core |  | |
| `NXT` | Core |  | |
| `constructor` | Core |  | |
| `ToString` | Core |  | |
| `VLU` | Core |  | |

### ThoughtRef

| Member | Fate | Note | Done |
|---|---|---|---|
| `IsEmpty` | Core |  | |
| `ToString` | Core |  | |
| `operators (2)` | Core |  | |

### CommonSequenceElement

| Member | Fate | Note | Done |
|---|---|---|---|
| `GapCardinality` | Internal | type becomes internal | |
| `IsGap` | Internal | type becomes internal | |
| `Value` | Internal | type becomes internal | |

### CommonSequencePattern

| Member | Fate | Note | Done |
|---|---|---|---|
| `Elements` | Internal | type becomes internal | |
| `FixedElementCount` | Internal | type becomes internal | |

### IReadOnlyListExtensions

| Member | Fate | Note | Done |
|---|---|---|---|
| `Contains` | Internal | LINQ covers it | |
| `FindAll` | Internal | LINQ covers it | |
| `FindFirst` | Internal | LINQ covers it | |
| `FindIndex` | Internal | LINQ covers it | |

### ThoughtLabels

| Member | Fate | Note | Done |
|---|---|---|---|
| `AddThoughtLabel` | Internal | then removed with the ambient store (Phase C) | done (class deleted) |
| `AllThoughtsInLabelList` | Delete | dead | done (class deleted) |
| `ClearLabelList` | Internal | then removed with the ambient store (Phase C) | done (class deleted) |
| `GetLabelCount` | Internal | then removed with the ambient store (Phase C) | done (class deleted) |
| `GetLinksCount` | Internal | then removed with the ambient store (Phase C) | done (class deleted) |
| `GetThought` | Internal | then removed with the ambient store (Phase C) | done (class deleted) |
| `LabelList` | Internal | then removed with the ambient store (Phase C) | done (class deleted) |
| `RemoveThoughtLabel` | Internal | then removed with the ambient store (Phase C) | done (class deleted) |
| `RemoveTrailingDigits` | Internal | then removed with the ambient store (Phase C) | done (class deleted) |

### UKSTransaction

| Member | Fate | Note | Done |
|---|---|---|---|
| `Added` | Internal | type becomes internal; "created in this change" becomes a store method | |
| `Created` | Internal | type becomes internal; "created in this change" becomes a store method | |
| `IsEmpty` | Internal | type becomes internal; "created in this change" becomes a store method | |

### sThought

| Member | Fate | Note | Done |
|---|---|---|---|
| `CanWriteToFile` | Internal | serialization | |
| `CaptureSaveSnapshot` | Internal | serialization | |
| `ToString` | Internal | serialization | |
| `V` | Internal | serialization | |
| `WriteSaveSnapshot` | Internal | serialization | |
| `index` | Internal | serialization | |
| `isPlastic` | Internal | serialization | |
| `label` | Internal | serialization | |
| `linkType` | Internal | serialization | |
| `maxWeight` | Internal | serialization | |
| `source` | Internal | serialization | |
| `target` | Internal | serialization | |
| `weight` | Internal | serialization | |

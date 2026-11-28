# UKS public API: plan and reference

The plan for the UKS's public API before publication (2027-02-01): what stays public, what becomes
internal, what moves out, and the order of the work. Each member's fate is checked off as its phase lands.

## Principles

1. **Public types:** `UKS`, `Thought`, `Link`, `SeqElement`, plus the input type `ThoughtRef`. Results
   are Thoughts, links, sequences, read-only lists (`IReadOnlyList<Thought>`, `IReadOnlyList<Link>`),
   or search tuples.
2. **Every label is resolved by a named store** (`uks.Labeled("dog")`, `thought.Store`, or a method
   taking a `ThoughtRef`). There is no ambient store and no implicit string-to-Thought conversion.
3. **Identity, not labels.** Two Thoughts are equal only if they are the same object; labels find
   Thoughts and are never compared.
4. **One way to do each thing**, with consistent verbs (`Add`, `Remove`, `Has`, `Get`); always "link".
5. **Experiments don't shape the API:** a member only an experiment needs is Undocumented, not Core.
6. **Test-only members are `internal`** (InternalsVisibleTo Tests and UKS.Tests).

## Layers

- **Core:** the representation and its basic operations: Thoughts, links, inheritance, queries,
  firing and time, sequences, files, undo.
- **Helpers:** shorthands built on the core, in namespace `UKS.Helpers` (UKS.Helpers.cs):
  `ThoughtHelpers` (lookups on a Thought's links) and `PhraseHelpers` (reading typed input). Like
  agents they use only the public API and are documented apart from the core and outside the
  Phase J freeze. The core never calls them.
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
- Thought: `AddLink`, `RemoveLink`, `RemoveLinks`, `HasLink`, `AddParent`, `RemoveParent`,
  `HasAncestor`, `HasProperty`, `AddProperty`, the `Get...OfType` helpers (now in `UKS.Helpers`).
- UKS: `AddStatement`, `AddStatementRecordingHistory`, `GetOrAddThought` and `AddThought` (parent),
  `GetLink`, `GetAttributes`, `SearchByAttributes` (root), `ExportTextFile` (root),
  `FindSequencesByActivation` (options and classes), `AddSequenceAndLink` (link type),
  `CreateFirstElement`/`InsertAfter` (value), `GetElementValue` (class),
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
- `ThoughtRef` parameters take the plain names (`source`, `valueClass`, not `sourceRef`): done in Phase H.
- Lists: a public result that is a list is an `IReadOnlyList<Thought>` or `IReadOnlyList<Link>` (a
  caller needing a writable list calls `.ToList()`); a list parameter that is only read is an
  `IReadOnlyList` or `IEnumerable`. A custom `ThoughtList` type was considered and dropped (2026-11-28):
  it added a type to learn and, unless generic, casts at every link filter, for no gain over the
  standard interface (Thoughts already compare by identity).

Open:
- When the spelling helpers leave (`CharacterPrefix`, `GetCharacterLabel`, `GetOrAddCharacter`,
  `CreateSpellingSequence`): the UKS still uses characters itself, for the starter alphabet
  (`CreateInitialStructure`) and single letters in phrases (`^a b c`, the text reader and `PhraseHelpers`).
  They can move once the starter content is decided.
- Require policy: silent, or a debug warning for an unknown label?
- Whether `AddStatementRecordingHistory` folds into `AddStatement`.

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
- [x] **D. Visibility:** Internal members become internal; Delete members are deleted; Move members move.
      Kept public because modules or hosts use them (now Core): `FindSequenceSpansByActivation`,
      `GetActionRelationshipType`, `GetReferringThoughts`, `IsConditionalLinkType`, `LinksAreExclusive`,
      `HasGeneratedLabel`, and the query annotations `InheritanceDepth`/`InheritedFromCategory`.
      The transaction API became internal; new public `TryChange` (keep the change only if it succeeds)
      and `CreatedInCurrentChange` replace it. `sThought` stays public (XmlSerializer requires it).
      Deferred: the spelling helpers (see Open) and the bubble result types (with the agents, Phase F).
      Second step: `InheritanceDepth`/`InheritedFromCategory` left Link (the query keeps them privately);
      new `GetEvidence(attribute)` explains a result with stored links; `IsConditionalLinkType` and
      `HasGeneratedLabel` are internal.
- [x] **E. Identity equality:** remove the label-based `Equals`, `GetHashCode`, `==` and `!=`.
      Where the store asks whether an unstored Link stands for a stored one (no duplicate links,
      `GetLink`, `HasLink`, `RemoveLink`, loading), it now says so explicitly: internal
      `Link.SameConnection` (same type, same source and target by identity, nested Links compared
      the same way). The case-insensitive label match in `HasLink` is gone.
- [x] **F. Agents layer:** extension methods in namespace `UKS.Agents`, using only the public API
      (checked by compiling them outside the UKS assembly): `ClassAgents` (UKS.Agents.Classes.cs) and
      `SequenceAgents` (UKS.SequenceBubble.cs, UKS.SequenceComparison.cs). Callers add `using UKS.Agents;`;
      call syntax is unchanged. Whole-store agents walk the hierarchy from its roots instead of the
      internal set of all Thoughts. `RunPeriodically` and `ReplaceThoughtReferences` stay in the core
      (see their rows); new public `UKS.IsWildcard`. The common-sequence types are internal.
- [x] **G. Helpers and label comparisons:** the wrapper review is applied (see the rows). New
      namespace `UKS.Helpers` (Layers) holds the former conveniences and
      `CreateThoughtFromMultipleAttributes`; callers add `using UKS.Helpers;`. `ThoughtRef.Find` is
      public. The UKS compares no labels: the core link types (is-a, hasProperty, FRST, NXT, VLU) are
      compared by identity (internal lookups by label, `IsAType` etc.), the sequence agents group
      sequences by their elements instead of label text, and the sequence cache is updated only for the
      sequence it holds (it used to skip query sequences by a "thequery" label prefix). Labels are
      still read where they are the notation itself: dotted labels, the text and XML formats, and
      numbers (`GetCount`). `isConditional`/`isPossible` audit: the logic was consistent; misleading
      names ("conditional" for `?` types) were fixed. Pluralize.NET left the UKS.
- [x] **H. Naming and lists:** `ThoughtRef` parameters have plain names (the resolved Thought is a local such as `type`,
      `target`; the activation searches resolve, then call a private search taking Thoughts);
      GetPlainLinkType, SaveXml/LoadXml, FindLinks, DeleteDescendants, Merge, Value, IsPlastic,
      MaxWeight; `InsertAfter` replaces AddElement/InsertElement. File formats are unchanged.
      Every public list result (core, agents) is an `IReadOnlyList`, and `RunPeriodically` takes a
      `Func<IReadOnlyList<Thought>>`; `AddSequence`, `AddSequenceAndLink` and the pattern search take
      an `IReadOnlyList<Thought>`. The internal `IReadOnlyListExtensions` (`FindFirst`, ...) were deleted
      in favor of LINQ. Callers that change a result call `.ToList()`.
- [x] **I. Documentation:** every public member has an XML doc comment written for readers (one
      summary, `<param>`/`<returns>` where they add something, "link", no book references), and
      Doxygen (`UKS/Doxyfile`, output to `UKS/Documentation`) and the C# compiler's documentation
      check (`-p:GenerateDocumentationFile=true`, which also verifies every `cref`) report nothing.
      Doxyfile: only `*.cs`, `EXTRACT_STATIC = YES` (without it the agents, helpers and constants were
      missing), the Undocumented members excluded (`sThought`, `FindSequenceSpansByActivation`), no
      logo (the file was gone). Each partial class has one summary (the others became `//` notes).
      Corrected comments that were wrong (`HasAncestor`, `GetAttributes`, `IsSequenceElement`).
      Also: `ThoughtHelpers` uses classic extension methods, since Doxygen does not read C# 14
      extension blocks (`AncestorsWithSelf` is now a method); unused parameters removed
      (`AddSequenceAndLink` baseWeight, `FlattenSequence` skipPlusValues, `ExportTextFile` maxDepth);
      `EnumerateSequenceElements`'s recursion stack is private; `SaveXml`/`LoadXml` take `path`;
      `CreateWildcard` takes `IEnumerable<Thought>` and finds its cardinality property by identity.
- [ ] **J. Freeze:** a public API baseline checked at build time, so any change to the public
      surface fails the build until the baseline is updated on purpose

## Members

"Overloads" counts the declarations sharing a name. Fates:
**Core**, **Helper**, **Agent**, **Internal** (stays, not public), **Undocumented** (public only
because an experiment or the serializer needs it; left out of the documentation and the Phase J freeze), **Move** (out of the UKS
to a module), **Delete**.

Totals by name: Core 105, Internal 45, Delete 17, Agent 13, Undocumented 11, Move 6, Helper 6 (203 names).


### UKS

| Member | Fate | Note | Done |
|---|---|---|---|
| `AddCounts` | Agent | ClassAgents | done |
| `AddElement` | Delete | merged into InsertAfter(element, value) | done |
| `AddSequence` | Core |  | |
| `AddSequenceAndLink` | Core |  | |
| `AddStatement` | Core |  | |
| `AddStatementRecordingHistory` | Core | fold into AddStatement (open) | |
| `AddThought` | Core | kept apart from GetOrAddThought: Add always creates (a label in use gets a number), GetOrAdd finds first; module instances and roots need Add. Doc fixed | done |
| `AdvanceCycle` | Core |  | |
| `ApplyTestOrSetAction` | Core |  | |
| `BalanceTree` | Agent | ClassAgents | done |
| `BeginTransaction` | Internal | TryChange, ChangeUndoably and UndoLast are the public undo | done |
| `BubbleAttributes` | Agent | ClassAgents | done |
| `BubbleSharedSequences` | Agent | SequenceAgents | done |
| `CanUndo` | Core |  | |
| `ChangeUndoably` | Core |  | |
| `CharacterPrefix` | Move | letters are language content: word module; deferred (see Open) |  |
| `ClearSequenceCache` | Internal |  | done |
| `CompareSequences` | Internal | ModuleLearnMelody compares the flattened sequences | done |
| `Copy` | Core |  | |
| `CreateClasses` | Agent | ClassAgents | done |
| `CreateFirstElement` | Core |  | |
| `CreateInitialStructure` | Core |  | |
| `CreateInstanceOf` | Delete | was AddThought(type.Label, type); callers write that | done |
| `CreateMinimumStructureForTests` | Internal | test-only | done |
| `CreateNewTemplate` | Agent | SequenceAgents | done |
| `CreateSearchOptions` | Core |  | |
| `CreateSpellingSequence` | Move | letters are language content: word module; deferred (see Open) |  |
| `CreateThoughtFromMultipleAttributes` | Helper | PhraseHelpers; takes an optional singularizer (hosts pass English), so Pluralize.NET left the UKS. The text reader keeps only what files use: `^a b c` sequences, else GetOrAddThought (private SequenceOrThought); no content file had a multi-word part | done |
| `CreateWildcard` | Core |  | |
| `CreatedInCurrentChange` | Core | new (Phase D): replaces CurrentTransaction.Created | done |
| `CurrentTransaction` | Internal | replaced by CreatedInCurrentChange | done |
| `Cycle` | Core |  | |
| `CyclesPerSecond` | Core |  | |
| `DeleteAllChildrenAndLinks` | Core | renamed DeleteDescendants; the doc says it deletes every descendant (even one with another parent) and removes the Thought's own links | done |
| `DeleteSequence` | Core |  | |
| `DiscoverSequenceTemplates` | Agent | SequenceAgents | done |
| `EndTransaction` | Internal | TryChange, ChangeUndoably and UndoLast are the public undo | done |
| `EnumerateSequenceElements` | Core |  | |
| `ExportTextFile` | Core |  | |
| `FileName` | Core |  | |
| `FilterResults` | Delete | dead | done |
| `FindCommonSequence` | Agent | SequenceAgents, internal (its pattern type is internal; only tests call it) | done |
| `FindSequenceSpansByActivation` | Undocumented | only the segmenter (an experiment) uses it; moving it would expose the activation search internals | done |
| `FindSequencesByActivation (2)` | Core |  | |
| `FindSequencesOutOfOrder` | Internal | reached through FindSequencesByActivation options | done |
| `FlattenSequence` | Core |  | |
| `GetActionRelationshipType` | Core | renamed GetPlainLinkType; the query's private GetAssertionLinkType now calls it | done |
| `GetAttributes` | Core |  | |
| `GetCharacterLabel` | Move | letters are language content: word module; deferred (see Open) |  |
| `GetEvidence` | Core | new (Phase D): the stored links behind a query result (is-a chain, then the stored link); replaces the Link query annotations | done |
| `GetElementValue` | Core |  | |
| `GetLastlement` | Internal | test-only | done |
| `GetLink (2)` | Core | (from, type, to) is Core; the prototype overload is internal | done |
| `GetLinkWeight` | Internal | private; only SearchByAttributes uses it | done |
| `GetLinks` | Delete | a store keeps one link per connection, so it was GetLink as a list | done |
| `GetOrAddCharacter` | Move | letters are language content: word module; deferred (see Open) |  |
| `GetOrAddThought` | Core |  | |
| `GetOrCreateThoughtClass` | Agent | ClassAgents | done |
| `GetPreviousSequenceElement` | Move | ModuleSequenceSegmenter.PreviousElement (by identity, not the NXT label) | done |
| `GetReferrer` | Delete | dead | done |
| `GetReferringThoughts` | Core | later: probably duplicates a general "sources of links of a type" convenience (Phase G) | done |
| `GetSequenceBindings` | Agent | SequenceAgents | done |
| `GetSequenceLength` | Internal | callers use FlattenSequence(...).Count | done |
| `GetSequenceLinks (2)` | Agent | SequenceAgents | done |
| `ImportTextFile` | Core |  | |
| `InsertAfter` | Core | new (H): replaces AddElement and InsertElement | done |
| `InsertElement` | Delete | merged into InsertAfter(first, value, atBeginning: true); its insert-before-a-later-element case had no callers | done |
| `IsConditionalLinkType` | Internal | IF is evaluated only by queries; hosts check HasProperty("isConditional"). The core's inline copies call it, and new internal IsClauseLinkType (IF or AND) and HasConditions (a rule) replace the other repeated tests | done |
| `IsSequenceElement` | Core |  | |
| `IsSequenceFirstElement` | Core |  | |
| `IsWildcard` | Core | new (Phase F): was internal HasWildcardProperty; pairs with CreateWildcard | done |
| `Labeled` | Core |  | |
| `LabeledStartingWith` | Core | new (Phase C): label completion and search, replacing `ThoughtLabels.LabelList` | done |
| `LinkCount` | Core |  | |
| `LinksAreExclusive` | Core | public (decided) | done |
| `LoadUKSfromXMLFile` | Core | renamed LoadXml | done |
| `MaterializeCommonSequence` | Agent | SequenceAgents, internal (its pattern type is internal; only tests call it) | done |
| `ProcessSingleLine` | Core |  | |
| `ProcessSingleLineWithNesting` | Core |  | |
| `PromoteToSeqElement` | Internal |  | done |
| `RememberForUndo` | Internal | TryChange, ChangeUndoably and UndoLast are the public undo | done |
| `RemoveFirstSequenceElement` | Move | ModuleSequenceSegmenter.RemoveQueueHead | done |
| `RemoveLastSequenceElement` | Delete | dead | done |
| `ReplaceThoughtReferences` | Core | was Agent: merging two Thoughts is a store operation (it keeps the parent invariant); ModuleText's class coalescing uses it. Renamed Merge (H) | done |
| `Reset` | Core |  | |
| `Roots` | Core |  | |
| `RunPeriodically` | Core | was Agent: the scheduler agents run on, with per-store timers | done |
| `SaveUKStoXMLFile` | Core | renamed SaveXml | done |
| `SearchByAttributes` | Core |  | |
| `SearchForRelationships` | Core | renamed FindLinks | done |
| `SequenceElementCount` | Core |  | |
| `SequenceMatchesPattern` | Agent | SequenceAgents, internal (its pattern type is internal; only tests call it) | done |
| `StartClock` | Core |  | |
| `StopClock` | Core |  | |
| `SubclassExists` | Internal | private; only the query's counting uses it | done |
| `SuppressFiring` | Core |  | |
| `ThoughtCount` | Core |  | |
| `ThoughtFired` | Core |  | |
| `ThoughtsHaveSimilarLink` | Delete | deprecated, unused (with its private helper LinksAreSimilar) | done |
| `TryChange` | Core | new (Phase D): a change kept only if it succeeds (the interpreter) | done |
| `constructor` | Core |  | |
| `Undo` | Internal | TryChange, ChangeUndoably and UndoLast are the public undo | done |
| `UndoLast` | Core |  | |
| `Why` | Core |  | |
| `WhyNot` | Core |  | |
| `WriteTheLink` | Internal |  | done |
| `theUKS` | Delete | ambient store (Phase C) | done |

### Thought

| Member | Fate | Note | Done |
|---|---|---|---|
| `AddDefaultLabel` | Internal |  | done |
| `AddLink (2)` | Core |  | |
| `AddParent` | Core |  | |
| `AddProperty` | Core |  | |
| `Ancestors` | Core |  | |
| `AncestorsWithSelf` | Helper | ThoughtHelpers (an extension property); the core uses Ancestors | done |
| `Children` | Core |  | |
| `CreatedCycle` | Core |  | |
| `Delete` | Core |  | |
| `Descendants` | Core |  | |
| `EnumerateSubThoughts` | Internal | only the file writers use it | done |
| `Equals` | Delete | removed: equality is identity | done |
| `Fire` | Core |  | |
| `FiredWithin` | Core |  | |
| `Forever` | Core |  | |
| `GetAttributes` | Internal | renamed DirectAttributes (it collided with UKS.GetAttributes); compares link types by identity; only exclusivity uses it | done |
| `GetHashCode` | Delete | removed: equality is identity | done |
| `GetSourceOfFirstLinkOfType` | Helper | ThoughtHelpers | done |
| `GetTargetOfFirstLinkOfType (2)` | Helper | ThoughtHelpers; the core uses HasLink(type)?.To | done |
| `GetTargetOfHighestWeightLinkOfType` | Helper | ThoughtHelpers | done |
| `HasAncestor (2)` | Core |  | |
| `HasGeneratedLabel` | Internal | internal; hosts show Links with ToString()/their own format. Still a prefix test: a real flag needs the XML and text files to stop writing generated labels (file-format change) | done |
| `HasLink (2)` | Core |  | |
| `HasProperty (2)` | Core |  | |
| `IsA` | Delete | uses the ambient store; write store.Labeled("is-a") | done |
| `Label` | Core |  | |
| `LastFiredCycle` | Core |  | |
| `LinksAsType` | Core |  | |
| `LinksFrom` | Core |  | |
| `LinksFromWriteable` | Internal |  | done |
| `LinksTo` | Core |  | |
| `LinksToWriteable` | Internal |  | done |
| `Parents` | Core |  | |
| `RemoveChild` | Delete | one way: child.RemoveParent(parent) | done |
| `RemoveLink (2)` | Core |  | |
| `RemoveLinks` | Helper | ThoughtHelpers | done |
| `RemoveParent (2)` | Core |  | |
| `Store` | Core |  | |
| `constructor (2)` | Core |  | |
| `TimeToLive` | Core |  | |
| `ToString` | Core |  | |
| `V` | Core | renamed Value (the XML field and the text format's V: token are unchanged) | done |
| `Weight` | Core |  | |
| `isPlastic` | Core | renamed IsPlastic (XML field unchanged) | done |
| `maxWeight` | Core | renamed MaxWeight (XML field unchanged) | done |
| `operators (3)` | Delete | implicit string-to-Thought (Phase C) and label-based ==/!= (Phase E) removed | done |

### Link

| Member | Fate | Note | Done |
|---|---|---|---|
| `From` | Core |  | |
| `InheritanceDepth` | Delete | the query keeps it privately (ThoughtWithQueryParams); explanations use GetEvidence | done |
| `InheritedFromCategory` | Delete | as InheritanceDepth | done |
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
| `Find` | Core | public in G: resolving a ThoughtRef is what code built on the core (helpers, agents) needs | done |
| `IsEmpty` | Core |  | |
| `ToString` | Core |  | |
| `operators (2)` | Core |  | |

### CommonSequenceElement

| Member | Fate | Note | Done |
|---|---|---|---|
| `GapCardinality` | Internal | type is internal | done |
| `IsGap` | Internal | type is internal | done |
| `Value` | Internal | type is internal | done |

### CommonSequencePattern

| Member | Fate | Note | Done |
|---|---|---|---|
| `Elements` | Internal | type is internal | done |
| `FixedElementCount` | Internal | type is internal | done |

### IReadOnlyListExtensions

| Member | Fate | Note | Done |
|---|---|---|---|
| `Contains` | Internal | LINQ covers it | done |
| `FindAll` | Internal | LINQ covers it | done |
| `FindFirst` | Internal | LINQ covers it | done |
| `FindIndex` | Internal | LINQ covers it | done |

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
| `Added` | Internal | type is internal; CreatedInCurrentChange is the public question | done |
| `Created` | Internal | type is internal; CreatedInCurrentChange is the public question | done |
| `IsEmpty` | Internal | type is internal; CreatedInCurrentChange is the public question | done |

### sThought

| Member | Fate | Note | Done |
|---|---|---|---|
| `CanWriteToFile` | Internal | serialization | done |
| `CaptureSaveSnapshot` | Internal | serialization; iOS compiles the UKS source, so it still sees it | done |
| `ToString` | Undocumented | public only because XmlSerializer needs a public type and fields | done |
| `V` | Undocumented | public only because XmlSerializer needs a public type and fields | done |
| `WriteSaveSnapshot` | Internal | serialization | done |
| `index` | Undocumented | public only because XmlSerializer needs a public type and fields | done |
| `isPlastic` | Undocumented | public only because XmlSerializer needs a public type and fields | done |
| `label` | Undocumented | public only because XmlSerializer needs a public type and fields | done |
| `linkType` | Undocumented | public only because XmlSerializer needs a public type and fields | done |
| `maxWeight` | Undocumented | public only because XmlSerializer needs a public type and fields | done |
| `source` | Undocumented | public only because XmlSerializer needs a public type and fields | done |
| `target` | Undocumented | public only because XmlSerializer needs a public type and fields | done |
| `weight` | Undocumented | public only because XmlSerializer needs a public type and fields | done |

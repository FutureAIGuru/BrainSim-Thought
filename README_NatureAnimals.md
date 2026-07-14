# BrainSimAnimals — Nature UKS knowledge base

Collected artifacts from the BrainSim-Thought **NatureAnimals** implementation (2026-07-07). These files model animals, taxonomy, habitats, and cross-species relationships using Charles Simon's atomic Thought / Link architecture.

**Source repo:** `~/Desktop/GitHub/BrainSim-Thought`

## Contents

| Path | Purpose |
|------|---------|
| `BrainSimulator/UKSContent/NatureAnimals.xml` | Importable UKS knowledge base (**979** `sThought` on golden bootstrap + Phase 4 pack) |
| `tools/generate_nature_animals_xml.py` | Generator — `--domain none\|animals_core\|animals`; `--validate-only`; `--check-file` |
| `Tests/NatureAnimalsXmlTests.cs` | xUnit tests (Ch.5 inheritance via `GetAllLinks` for Fido anatomy) |
| `BrainSimulator/UKSContent/NatureAnimals-HumanTests.md` | Human Gates 1–5 checklist |
| `tools/fixtures/NewDocument-golden.xml` | Frozen Initialize + Fido bootstrap |

## Import

In BrainSim-Thought: **File → Open** → select `NatureAnimals.xml`.

Programmatically:

```csharp
UKS.theUKS.LoadUKSfromXMLFile("path/to/NatureAnimals.xml");
```

Regenerate XML after editing the generator:

```bash
python3 tools/generate_nature_animals_xml.py
```

Run tests (from repo root, requires .NET SDK):

```bash
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~NatureAnimalsXmlTests"
```

## Knowledge model

Follows the Fido pattern from Simon's README and `DemoText.xml`:

- **Thought** — atomic unit (objects, attributes, link types, instances)
- **Link** — relationship as `[From → LinkType → To]`, stored as `sThought` with `source` / `linkType` / `target` index refs
- **Inheritance** — class facts via `is-a` + `has` (not domain `hasProperty`); instances inherit unless overridden (Ch.5)

### Taxonomy (is-a)

```
livingThing → animal → mammal | bird | reptile | fish | amphibian | insect
                      → dog, cat, horse, wolf, eagle, salmon, snake, bee, …
plant → tree
```

### Named instances (Fido-style)

| Instance | Key links |
|----------|-----------|
| Fido | is-a dog, is brown, livesIn grassland (inherits has.4 leg / fur from dog) |
| Tripper | is-a dog, **has.3 leg** (exception overriding inherited 4) |
| Whiskers | is-a cat, is black |
| Shadow | is-a horse, is brown |
| Buddy | is-a dog, is golden |
| Talon | is-a eagle, livesIn sky |
| Slither | is-a snake, is green |
| Penny | is-a penguin, livesIn antarctica |
| Nemo | is-a salmon, livesIn river |
| Bramble | is-a deer, is brown |

### Cross-species relationships

- **isSimilarTo** — dog↔wolf, dog↔cat, salmon↔trout, eagle↔owl
- **differsFrom** — dog↔bird, whale↔fish, mammal↔reptile
- **predatorOf / preyOf** — eagle→mouse, wolf→deer, shark→salmon, cat→mouse
- **livesIn** — forest, ocean, sky, antarctica, river, grassland
- **eats** — dog→salmon, wolf→deer, eagle→mouse

### Class-level anatomy (inherited)

| Class | Facts |
|-------|-------|
| mammal | warmBlooded, givesLiveBirth, vertebrate |
| dog | tail, has.4 leg, fur, can bark(dog) |
| bird | wing, feather, has.2 leg, beak, laysEggs |
| whale | fin, has.0 leg |
| snake | has.0 leg, scale |
| fish | fin, gill, scale |

## sThought XML format

Each entry is an `sThought` element:

- **Node** — `label` only (Thought without links)
- **Link** — `label` (auto `unl_*`), plus `source`, `linkType`, `target` as integer indices into the array

The loader (`UKS.LoadUKSfromXMLFile`) registers all labels in pass 1, then wires links in pass 2 via `ThoughtLabels` lookup.


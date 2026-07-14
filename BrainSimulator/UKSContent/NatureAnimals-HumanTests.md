# NatureAnimals — human + automated test pack

Phases: see vault `Work-Log/2026-07-14-research-loop-nature-animals-generator-phased-plan.md`.

## Generate / validate (tower or any machine with Python 3)

```bash
cd /path/to/BrainSim-Thought

# Full pack (default) + write NatureAnimals.xml
python3 tools/generate_nature_animals_xml.py

# Explicit Gate 4/5 artifact
python3 tools/generate_nature_animals_xml.py --domain animals \
  --output BrainSimulator/UKSContent/Generated-Phase4-NatureAnimals.xml

# Validate without writing
python3 tools/generate_nature_animals_xml.py --validate-only

# Check an existing file (after regenerate or app re-save)
python3 tools/generate_nature_animals_xml.py --check-file BrainSimulator/UKSContent/NatureAnimals.xml

# Earlier phases
python3 tools/generate_nature_animals_xml.py --domain none -o .../Generated-FidoGolden.xml
python3 tools/generate_nature_animals_xml.py --domain animals_core -o .../Generated-Phase23-Ch5Core.xml
```

**Machine validators always run before write:** list-index integrity, parent audit (seq* allowlist), no domain hasProperty, content triples.

---

## Gate 1 — golden parity (`--domain none`)

File: `Generated-FidoGolden.xml`

| # | Check | PASS? |
|---|--------|-------|
| 1.1 | Opens without error | |
| 1.2 | Unknown → dog → Fido | |
| 1.3 | Fido is-a dog | |
| 1.4 | No multi-species taxonomy | |
| 1.5 | ModuleUKS / Query usable | |

---

## Gates 2–3 — hierarchy + Ch.5 (`--domain animals_core`)

File: `Generated-Phase23-Ch5Core.xml`

| # | Check | PASS? |
|---|--------|-------|
| 2.1 | Loads (no stack overflow) | |
| 2.2 | Object → livingThing → animal → mammal → dog → Fido | |
| 2.3 | dog is-a mammal (may also be under Unknown) | |
| 3.1 | Query Fido / has / fur → yes (inherited) | |
| 3.2 | Tripper has.3 leg; not has.4 | |
| 3.3 | Fido is brown | |

---

## Gate 4 — full multi-species (`--domain animals`)

File: `Generated-Phase4-NatureAnimals.xml` or `NatureAnimals.xml`

| # | Check | PASS? |
|---|--------|-------|
| 4.1 | Loads; multi-class under animal | |
| 4.2 | Fido under dog; inherits fur; Tripper has.3 | |
| 4.3 | bird has.many feather; bird lays egg | |
| 4.4 | whale/snake has.no leg | |
| 4.5 | eagle livesIn sky; shark eats salmon | |
| 4.6 | Unknown still present | |
| 4.7 | dog isSimilarTo wolf; eagle predatorOf mouse | |

---

## Gate 5 — hardening / regression

| # | Check | PASS? |
|---|--------|-------|
| 5.1 | Full Gate 4 smoke after **clean regenerate** | |
| 5.2 | **Re-save** from BrainSim to a new file; re-open; key facts still present (note index rewrite if any) | |
| 5.3 | Ch4Ch5-FidoDemo-style queries on **generated** graph (Fido fur inherit; Tripper exception) | |
| 5.4 | Unknown still exists for new manual statements | |
| 5.5 | `python3 tools/generate_nature_animals_xml.py --check-file <saved-or-generated.xml>` → CHECK PASS | |

### Round-trip note

App save uses dense discovery order (`GetIndex`); re-export may **rewrite indices** and `unl_*` labels. Facts (labels + triples) should survive; byte-identical XML is **not** required. Prefer `--check-file` after re-save.

### Automated (Windows / with .NET)

```bash
dotnet test Tests/Tests.csproj --filter "FullyQualifiedName~NatureAnimalsXmlTests"
```

Tests expect Ch.5 inheritance for Fido legs (via `GetAllLinks`), not a local `Fido has.4 leg` fact.

---

## Gate E — multi-domain extension (Phase 6)

See `tools/domains/README.md` to add packs **without** editing the merge engine.

```bash
# animals only still works (E.1)
python3 tools/generate_nature_animals_xml.py --validate-only

# animals + plants → NatureWorld.xml by default when multi-domain
python3 tools/generate_nature_animals_xml.py --domain animals,plants \
  --output BrainSimulator/UKSContent/NatureWorld.xml

# full stack
python3 tools/generate_nature_animals_xml.py --domain animals,plants,inanimates
```

| # | Check | PASS? |
|---|--------|-------|
| E.1 | `--domain animals` still validates / loads (Phase 5) | |
| E.2 | `--domain animals,plants` loads; plant→tree→oak; animal tree unchanged | |
| E.3 | GrandmaOak under oak; no module label collisions | |
| E.4 | `--domain animals,plants,inanimates`: sun is hot; RedCar is red; car has wheel | |
| E.5 | Adding a new `tools/domains/foo.yaml` works without Python engine edits | |

---

## Load rule (do not regress)

Domain merge is **append-only**. Never delete mid-list `sThought` entries: UKS load uses `UKSTemp[source]` as **list position**. Mid-list deletes cause stack overflows.

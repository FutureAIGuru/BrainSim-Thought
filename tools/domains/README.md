# UKS domain layers (Phase 6)

Data-only extension packs for `tools/generate_nature_animals_xml.py`.

## Layout

| File | Role |
|------|------|
| `plants.yaml` | Plants / trees / flowers (5yo) |
| `inanimates.yaml` | Rock, water, sun, car, house, … |
| `*.yaml` | Add new packs here — **no merge-engine edits required** |

Bootstrap stays in `tools/fixtures/NewDocument-golden.xml`.  
Built-in animal packs remain in the generator (`animals` / `animals_core` / `none`).

## Schema

```yaml
name: plants          # optional display name
requires: []          # optional other domain ids that must load first
nodes:
  - label: flower
    is_a: [plant]     # one or more is-a parents
links:
  - [tree, has, leaf] # [source, linkType, target]
instances:
  - label: GrandmaOak
    is_a: [oak]
    links:
      - [GrandmaOak, livesIn, forest]
```

Rules (same as Phase 6 plan):

1. Prefer **class** facts; instances only for exceptions/stories.
2. Do **not** use `hasProperty` for domain facts.
3. Parents must already exist (bootstrap, animals pack, or earlier domains in `--domain` list).
4. Keep each file small (~100–200 nodes).
5. Append-only merge — never delete mid-list UKSTemp entries.

## CLI

```bash
# animals only (default)
python3 tools/generate_nature_animals_xml.py

# animals + plants
python3 tools/generate_nature_animals_xml.py --domain animals,plants \
  --output BrainSimulator/UKSContent/NatureWorld.xml

# animals + plants + inanimates
python3 tools/generate_nature_animals_xml.py --domain animals,plants,inanimates \
  --output BrainSimulator/UKSContent/NatureWorld.xml

# plants alone still needs livingThing/plant spine — include animals_core or animals first
python3 tools/generate_nature_animals_xml.py --domain animals_core,plants

python3 tools/generate_nature_animals_xml.py --validate-only --domain animals,plants
```

Alias: `python3 tools/generate_uks_project_xml.py` (same CLI).

## Adding a new area (e.g. weather)

1. Copy `plants.yaml` → `weather.yaml`.
2. Edit nodes/links/instances only.
3. Run `--domain animals,plants,weather` (or whatever stack you need).
4. Add 5 lines to `BrainSimulator/UKSContent/NatureAnimals-HumanTests.md` Gate E.

No changes to the Python merge engine unless you invent a new schema field.

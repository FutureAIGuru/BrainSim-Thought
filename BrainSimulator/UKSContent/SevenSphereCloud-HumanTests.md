# SevenSphere Cloud — human + automated test pack

Source: `~/Desktop/Areas/Personal/sevenspheres/data/*.json`  
Generator: `tools/generate_sevensphere_cloud.py`  
Plan: vault Work-Log sevensphere thought cloud (2026-07-15)

**Mapping rule:** centers and satellites are Thoughts; color roles are typed
`has*Aspect` links — **not** `center is-a aspect`.

**Center-only exclusion:** spheres with only Grey filled (empty or self-only
color satellites) are **not** imported — no `sphereCenter` node in the XML.

## Generate / validate (tower)

```bash
cd ~/Desktop/GitHub/BrainSim-Thought
# NAS → local sync first when NAS is available

# Phase 1 seed
python3 tools/generate_sevensphere_cloud.py --domain seed --validate-only
python3 tools/generate_sevensphere_cloud.py --domain seed \
  --output BrainSimulator/UKSContent/Generated-SS-Phase1-Seed.xml

# Phase 2 curated cognition
python3 tools/generate_sevensphere_cloud.py --domain curated \
  --output BrainSimulator/UKSContent/Generated-SS-Phase2-Curated.xml

# Phase 3 sense layer
python3 tools/generate_sevensphere_cloud.py --domain sense \
  --output BrainSimulator/UKSContent/Generated-SS-Phase3-Sense.xml

# Phase 4 full corpus
python3 tools/generate_sevensphere_cloud.py --domain full \
  --output BrainSimulator/UKSContent/SevenSphereCloud.xml
```

**Always:** list-index integrity, parent audit, bootstrap Fido, no false taxonomy.

---

## Gate 1 — seed (`--domain seed`)

File: `Generated-SS-Phase1-Seed.xml`

| # | Check | PASS? |
|---|--------|-------|
| 1.1 | Opens without error | |
| 1.2 | `sevenSphereDomain` / `sphereCenter` / `sphereAspect` present | |
| 1.3 | Six link types `hasGreenAspect` … `hasYellowAspect` under LinkType | |
| 1.4 | `UNIPHICS --hasBlueAspect--> Energy Density` | |
| 1.5 | `BrainSim3 --hasGreenAspect--> UKS` | |
| 1.6 | Unknown → dog → Fido still present | |
| 1.7 | **No** `UNIPHICS is-a Energy Density` | |

---

## Gate 2 — curated (`--domain curated`)

File: `Generated-SS-Phase2-Curated.xml`

| # | Check | PASS? |
|---|--------|-------|
| 2.1 | Loads; ~40–70 sphere centers under `sphereCenter` | |
| 2.2 | Spot-check 10 centers have up to 6 aspect links | |
| 2.3 | Shared labels (e.g. Thought, Symbol, Learning) are single pure nodes | |
| 2.4 | ModuleUKSQuery usable; expand from `sevenSphereDomain` | |
| 2.5 | Bootstrap Fido intact | |

---

## Gate 3 — sense layer (`--domain sense`)

File: `Generated-SS-Phase3-Sense.xml`

| # | Check | PASS? |
|---|--------|-------|
| 3.1 | Geometric aspects still present | |
| 3.2 | `BrainSim3 --has--> UKS` (sense YAML) | |
| 3.3 | `Algorithm --has--> Termination` | |
| 3.4 | Still no center `is-a` aspect false taxonomy | |

---

## Gate 4 — full cloud (`--domain full`)

File: `SevenSphereCloud.xml`

| # | Check | PASS? |
|---|--------|-------|
| 4.1 | Loads (note UI performance) | |
| 4.2 | Sample 20 random centers for aspect completeness | |
| 4.3 | Skip list applied (corrupt / empty grey) | |
| 4.4 | Daily use: prefer curated if full is slow | |

---

## Git policy

Never `git commit` / `git push` BrainSim-Thought from the Grok tower.
Publish from the other system after copying artifacts / dirty tree as needed.

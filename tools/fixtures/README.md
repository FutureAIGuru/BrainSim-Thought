# UKS bootstrap fixtures

## NewDocument-golden.xml

Frozen snapshot of a BrainSim-Thought project after:

1. UKS dialog **Initialize**
2. Statement **Fido is-a dog** (lands under `Thought → Unknown → dog → Fido`)

**Source (2026-07-14):** `smb://mooretechnas.local/shared/GitHub/BrainSim-Thought/BrainSimulator/UKSContent/NewDocument.xml`

**Do not hand-edit.** Re-export from BrainSim-Thought on the app machine if Initialize changes, then replace this file.

Used by `tools/generate_nature_animals_xml.py` as the Phase 0–1 bootstrap template (see vault `Work-Log/2026-07-14-research-loop-nature-animals-generator-phased-plan.md`).

Git policy: never commit/push BrainSim-Thought from the Grok tower; publish from the other system.

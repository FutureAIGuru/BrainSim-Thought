# Ch.4 relationship gating matrix

Simon Ch.4: traversal requires **source thought AND relationship type** active together (biological AND-gate). Ch.2: sparse activation — only active subgraph participates per engine tick.

## Runtime

- `UKS.BeginTraversalCycle()` — called at start of `MainWindow.Dt_Tick`; clears `CurrentTraversal`.
- Modules call `CurrentTraversal.Activate(thought)` and `ActivateRelationship(linkType)` before gated queries.
- Activation marks thoughts in `TraversalContext` only — it does **not** call `Thought.Fire()` (that would re-enter `ModuleAlgorithm`'s interpreter queue).


## Module status (Phase B)

| Module | Gating | Notes |
|--------|--------|-------|
| `ModuleAlgorithm` | **Reference** | `EvaluateContext` activates context root + `has` |
| `ModuleUKSQuery` | Ungated | Future: relationship filter → `ActivateRelationship` |
| Other modules | No change | Default `Fire()` only |

> **Note (2026-07):** Legacy `ModuleVision` (old CV pipeline for corners/segments + discrete color) removed. Discrete sensory ingress now handled via direct Thought attribute injection (see DiscreteAttributeCh4 work and future modules). See [[Work-Log/2026-07-10-research-loop-brainsim-legacy-vision-removal]].

## API

- `GetGatedLinks(source, linkType, ctx?)` — inherited `has` links when both active; direct `is-a` when both active.
- `Traverse(source, linkType, ctx?)` — gated target thoughts.


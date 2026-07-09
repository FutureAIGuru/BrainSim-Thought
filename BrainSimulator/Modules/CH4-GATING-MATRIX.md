# Ch.4 relationship gating matrix

Simon Ch.4: traversal requires **source thought AND relationship type** active together (biological AND-gate). Ch.2: sparse activation — only active subgraph participates per engine tick.

## Runtime

- `UKS.BeginTraversalCycle()` — called at start of `MainWindow.Dt_Tick`; clears `CurrentTraversal`.
- Modules call `CurrentTraversal.Activate(thought)` and `ActivateRelationship(linkType)` before gated queries.

## Module status (Phase B)

| Module | Gating | Notes |
|--------|--------|-------|
| `ModuleAlgorithm` | **Reference** | `EvaluateContext` activates context root + `has` |
| `ModuleUKSQuery` | Ungated | Future: relationship filter → `ActivateRelationship` |
| `ModuleVision` | **Partial** | `ApplyDiscreteColorAttributes` — discrete RGBI + `has` links + `CurrentTraversal` |
| Other modules | No change | Default `Fire()` only |

## API

- `GetGatedLinks(source, linkType, ctx?)` — inherited `has` links when both active; direct `is-a` when both active.
- `Traverse(source, linkType, ctx?)` — gated target thoughts.
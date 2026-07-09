# Vision modules

Re-enabled 2026-07-07 via BrainSim3 compatibility layer in `UKS/`:

- `Relationship.cs` — `Link` wrapper
- `Thought.BrainSim3Compat.cs` — `SetAttribute`, `GetAttribute`, `Relationships`, etc.
- `UKS.BrainSim3Compat.cs` — `GetOrAddThing`, `SearchForClosestMatch(ref)`, `HasSequence`, etc.
- `BrainSimulator/GlobalUsings.cs` — `Thing` → `Thought` alias

`ModuleMentalModel*` in this folder are legacy BrainSim3 stubs. The canonical
implementation is `Modules/ModuleMentalModel*` and is excluded here in
`BrainSim Thought.csproj` to avoid duplicate-type build errors.
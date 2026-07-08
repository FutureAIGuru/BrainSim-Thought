# Legacy BrainSim3 sources (not compiled)

Moved from `BrainSimulator/` root on 2026-07-07. Excluded via `<Compile Remove="archive\legacy\**" />` in `BrainSim Thought.csproj`.

| File | Reason |
|------|--------|
| `XmlFile.cs` | BrainSim3Data XML serializer; app uses `UKS.LoadUKSfromXMLFile` / `SaveUKStoXMLFile` |
| `MainWindowPythonModules.cs` | Duplicate Python glue with hardcoded `Python310`; superseded by `ModuleHandler.cs` |

Do not re-enable without porting to current UKS persistence and `PythonPath` configuration.
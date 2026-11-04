# Brain Simulator Thought for iOS

A native SwiftUI app for Brain Simulator Thought: the Universal Knowledge Store (Thoughts
and Links) and the desktop app's modules (agents). Nothing is reimplemented: the original
C# sources in `../UKS` and `../BrainSimulator/Modules` are compiled with .NET NativeAOT
into a static library and linked straight into the app binary.

```
UKS/*.cs (unchanged) ──────────────────┐
BrainSimulator/Modules/**/*.cs ────────┤  (module logic; not the WPF *Dlg.xaml.cs dialogs,
BrainSimulator/Network.cs ─────────────┤   ModuleBase.cs or ModuleBaseDlg.cs)
UKSNative/Host/*.cs (iOS host) ────────┤
UKSNative/Exports.cs, ModuleExports.cs ┴─ dotnet publish (NativeAOT, NativeLib=Static)
   (C ABI)                     └─ out/<platform>/libUKSNative.a + UKSBootstrap.o
                                    └─ linked into BrainSimThought.app, called via UKSNative.h
```

## Build

Requirements: Xcode and the .NET 10 SDK.

```bash
./build-uks.sh            # builds out/iphoneos and out/iphonesimulator
open BrainSimThought/BrainSimThought.xcodeproj
```

Re-run `build-uks.sh` whenever anything under `../UKS`, `../BrainSimulator/Modules` or `UKSNative/` changes;
Xcode picks the right archive via `$(PLATFORM_NAME)`. The simulator archive is
arm64 only, so build for a specific simulator rather than "Any iOS Simulator".

## Modules

The **Modules** tab is the desktop main window's module side: add/remove/enable modules,
and an engine thread that fires every active module each cycle (10 ms, like the desktop
timer). Each desktop `*Dlg.xaml.cs` mixed a dialog's layout with the work behind it; on iOS
the two are split. C# keeps only the logic, and every bit of UI is SwiftUI:

- `UKSNative/Host/ModuleBase.cs`, `MainWindow.cs`: headless stand-ins for the WPF base class and main
  window (active module list, engine tick, file load/save hooks, ModuleUKS's
  UKS-initialized broadcast).
- `SequenceSegmenterInput.cs`: how typed text and corpus lines become the Sequence
  Segmenter's input. On the desktop this is in the WPF dialog, so the platform-neutral
  parts are copied here.
- `ModuleRegistry.cs`: the list of module types the app can add. The desktop finds them by
  reflection, which NativeAOT can't do, so they are spelled out.
- `ModuleApi.cs`, `ModuleApis.cs`, `MentalModelApi.cs`, `VisionApi.cs`,
  `ObjectRecognizerApi.cs`, `SequenceEditorApi.cs`: one logic interface per dialog. It
  reports the module's state as plain data (JSON) and performs named operations
  ("turn", "submit", "load"…). It knows nothing about controls, layout, colours or screen
  coordinates. The app reaches it through two C calls: `uks_module_query(label, op, args)`
  (read-only: the "state" every dialog polls, plus a few others such as the Object
  Recognizer's "picture" and Show Graph's "graph") and `uks_module_call(label, op, args)`.
  `ApiJson.cs` holds the source-generated JSON contexts (camelCase, nulls left out), since
  NativeAOT can't serialize by reflection.
- `BrainSimThought/Modules/Dialogs/*.swift`: one SwiftUI dialog per module; `ModuleDetailView.swift` picks the dialog for a module type (modules without one
  show a short note). Each dialog holds a `ModuleLink` (`Modules/ModuleLink.swift`), which
  polls the module's state while the dialog is visible and sends operations, and lays
  itself out in a `ModuleForm` (status line, then the dialog's sections). The shared
  controls (text fields with suggestions, sliders, menus, toggle buttons, lists, file
  choosers, logs, the piano keyboard) are in `Modules/DialogParts.swift`, and the camera
  button in `Modules/CameraControl.swift`. What is typed, toggles, histories and the
  drawing of the mental model sheet, the vision canvas, the recognizer's picture and the
  graph all live in Swift.
- `WpfShims.cs`, `HostServices.cs`, `Utils.cs`, `HSLColor.cs`, `NewtonsoftAot.cs`: the few
  WPF / NAudio / System.Drawing / app.config types the module sources use, mapped to iOS:
  MIDI goes to an `AVAudioUnitSampler`, images are decoded with ImageIO, MessageBox becomes
  an alert, `APIKey` comes from the Settings screen (stored in the Keychain). The Swift
  side of these (MIDI, image decoding, settings) is `Modules/HostBridge.swift`; module
  notices and errors reach the app through `uks_notices`, which `ModuleEngine` drains.

`UKSContent`, `WordFIles` (minus the 39 MB `tinyStories.txt`) and `Resources/wordlist.txt`
are copied into the app by the "Copy desktop content" build phase, so the modules find
their corpora, lessons and grounded images where the desktop does.

### Object Recognizer (camera)

`BrainSimulator/Modules/ModuleObjectRecognizer.cs` is a new, platform-neutral module:
point the camera at an object and it recognizes it or learns it.

1. The photo (centre square, saved to `Documents/ObjectCamera/Images`) goes through
   ModuleVision's pipeline: the ≤50×50 pixel array and its boundary points
   (`ModuleVision.AnalyzeImageFile`, in `Vision/ModuleVision.Analyze.cs`). If a Vision
   module is active it is used, so its dialog shows the same frame.
2. The object is the region the boundary points enclose. The module records where the
   edges are within it (6×6), its tones (8×8), its colours and its proportions. Colours
   come from the pixels that differ from the background (the average of the picture's
   border), so a ring's hole or the backdrop doesn't count as the object's colour.
3. Each look is an `Appearance` Thought whose links are those features
   (`hasEdgeAt`, `hasTone`, `hasColor`, `hasAspect`). An object is any Thought with
   `hasAppearance` links, plus `hasImage` pointing to its latest photo, so the Mental Model
   shows the photo when the object is placed there. The features are read back from a
   compact copy in the Appearance's `V`, not from the link weights, because other modules
   reset link weights (ModuleVision's `ClearBoundaryArray` sets every weight in the UKS to
   0). Appearances without that copy, or with reset weights, are re-analyzed once from
   their photo.
4. A new look is compared with every stored Appearance, scoring edges, colours, tones and
   proportions with a weighted geometric mean, so one clear disagreement (the same outline
   in another colour) is enough to tell two objects apart. A score of 80% or more (the
   default) means that object: it fires and is placed at the Mental Model's attention, and
   a view that is new enough is stored too. Anything else becomes `Thing1`, `Thing2`…
   Typing a name (or tapping "Yes, it's …" or a close match) teaches what it is. That
   renames the Thing, or merges it into an existing Thought such as `dog`.

The dialog shows the pixel array with its boundary points and the box taken as the
object, the closest known objects and their scores, and the list of known objects
(tap one, then "Forget …" to remove it). Looking runs on a background thread (the dialog says "Looking…"),
and the picture is sent as compact pixels and fetched again (the "picture" query) only when
the state's `pictureRevision` changes. The Simulator has no camera, so the button becomes "Choose Photo" there (add
images with `xcrun simctl addmedia <device> file.png`).

Behaviour differences from the desktop:
- An exception in a module's `Fire()` is reported once as an alert (naming the module)
  instead of stopping the app; the other modules keep running.
- A module added while running gets `UKSInitializedNotification()` right away (the
  desktop only sends it from ModuleUKS's first Fire, e.g. after a file load).
- Python modules are not available (no Python runtime on iOS).
- The UKS autosaves a few seconds after changes and when the app goes to the background,
  on a background thread. Only the snapshot (`UKS.CaptureSaveSnapshot`) holds the UKS lock;
  the XML is written outside it.

Threading: every export runs under one lock (`gate` in `Exports.cs`), and the engine thread
holds it while it fires the modules. The calls the app polls on a timer (module list,
module state queries) use `PollGuard`, which waits at most 15 ms and otherwise answers "busy",
so the screen keeps its last state rather than freezing while a module works. Long module
work started from a dialog (Vision's "100", file loaders) runs through `Exports.RunLocked`,
which takes the lock for each step and pauses between steps.

The app has one UKS. The UKS sources keep the "current" UKS (`UKS.theUKS`, which labels,
`Thought.Delete()` and the modules use) per execution context, and the app calls in from
several native threads (main, engine, background saves), each of which would otherwise get a
new, empty UKS. So whatever takes the lock (`Guard`, `PollGuard`, `RunLocked`, the engine)
first selects the shared instance (`Exports.UseShared`). Clearing the UKS replaces that
instance and rebuilds the modules on the new one.

## Layout

- `build-uks.sh`: publishes `UKSNative` for each platform and folds the managed code and
  the NativeAOT runtime into one `libUKSNative.a` (plus the loose `UKSBootstrap.o`).
- `UKSNative/UKSNative.csproj`: compiles `../UKS/*.cs`, the desktop module sources and
  everything in `UKSNative/` with NativeAOT. `ILLink.Descriptors.xml` keeps the types
  `XmlSerializer` needs.
- `UKSNative/Exports.cs`: C ABI for the UKS (`uks_*`), with JSON results, the lock, and
  save/load. Statement, query and Why? handling mirror the desktop `ModuleUKSStatement` /
  `ModuleUKSQuery` dialogs (nested Links, `IF` conditionals, `^` sequences, inheritance,
  search by attributes).
- `UKSNative/ModuleExports.cs`: C ABI for modules (engine, add/remove/enable, module
  query/call) and the host hooks (content folder, MIDI, image decoder, settings, notices).
- `UKSNative/Host/`: the headless module host and the per-dialog logic interfaces (above).
- `UKSNative/include/`: `UKSNative.h` and the module map Swift imports.
- `BrainSimThought/`: the SwiftUI app, with Knowledge (graph/list), Add, Query, Modules
  and Files tabs. `UKS/` wraps the UKS calls (`UKSStore`, which also autosaves to
  `Documents/Autosave.xml`), `Graph/` is the force-directed graph view, `Views/` the tabs,
  and `Modules/` the module screens and dialogs.

## Notes

- The NativeAOT bootstrapper exports no symbols. It must be linked as a loose
  object (`UKSBootstrap.o` in `OTHER_LDFLAGS`), or the runtime never starts.
- UKS XML save/load uses `XmlSerializer` in reflection mode. `Exports.PreserveXmlTypes`
  keeps `UKS.sThought`, so files are interchangeable with the desktop app.

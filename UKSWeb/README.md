# UKS web proof of concept

## Guided experiment draft

Open `/experiment1.html` for the Chapter 2 demo: Fido's attributes,
Relationship types, Relationships as Thoughts, and attributes of Relationships.
Presets follow Chapter 2 of *Current Manuscript*: color, tail, barking, yard,
chasing a ball, and Figure 2.3's fetch/frisbee/personal-experience structure.
Graph labels use `[source → type → target]`, including nested brackets; English
descriptions stay in the choice buttons. A choice toggles between adding its
Relationship (+) and deleting it (−). Choices accumulate across steps,
starting with Fido is a dog. Next requires a successful add, delete, root change,
node drag, or expansion in that experiment; Back preserves the graph.
Buttons reflect actual UKS membership after reload and changes from another tab.
Tapping an edge reveals its Relationship Thought, with labels excluded from hit
testing so they cannot intercept edge clicks. Hover details appear at lower left.
The graph measures its own pane rather than the browser. On narrow screens the
prompt and graph stack, with the prompt scrolling independently.

The root field offers suggestions and also accepts an existing Thought label.
Help contains the graph instructions and inactivity note. The color key remains
visible below the graph; the embedded page has no separate site header or footer.
Deleting a Relationship also deletes its dependent attributes through the UKS;
unconnected ordinary Thoughts remain. Reload preserves the session's knowledge.
The draft keeps prompts in JavaScript pending agreement on their UKS representation.
Existing editor and graph pages remain
available at their original addresses. Embed this page using
`https://Brainsim-Thought.onrender.com/experiment1.html` after deployment.
The previous `/experiments.html` address redirects to it, preserving session and root.

Open `/demo-frame.html` to preview the demo inside an iframe with a local
approximation of the WordPress header, footer, colors, and typography. The width
selector tests available width, an 820px tablet, and a 390px phone without
reloading the embedded demo or losing its session. This is a development preview,
not a replacement for WordPress or a synchronized copy of its menus.

### Adding another experiment

| File | Responsibility |
| --- | --- |
| `wwwroot/experiment1.html` | Chapter 2 layout, facts, steps, title, and default root. |
| `wwwroot/experiments.js` | Common guided-demo engine: choices, add/remove, prerequisites, membership checks, prompts, progress, and Help. No chapter-specific content or graph implementation. |
| `wwwroot/demo-components.js` | Mounts selectors, editors, sequence controls, and results from each step's choices. |
| `wwwroot/uks-query.js`, `wwwroot/uks-sequences.js` | Shared query/editor and sequence component behavior. |
| `wwwroot/experiments.css` | Common responsive layout, typography, colors, buttons, and panels. |
| `wwwroot/uks-graph.js` | Shared UKS graph component: snapshots, root loading, expansion/collapse, refresh, reachable graph composition, component-role descriptions, labels, rendering, camera, and resizing. |
| `wwwroot/graph.js` | Small adapter connecting the standalone `graph.html` controls to the same graph component. It is not a second graph implementation. |

To add a demo, copy `experiment1.html` to a new page such as `experiment2.html`.
Change its layout, chapter/title, initial root, `facts`, and `steps`. Keep the
common CSS import and `startExperiment({ facts, steps })` call. Do not copy or
modify the common JavaScript for each chapter. Help is populated by the engine.
The existing element IDs are the engine's UI hooks; they can be rearranged.
The prompt column width can be adjusted with `--prompt-width` on that page's
`main` element. More substantial layout differences can be supplied in the page.

Each fact supplies `source`, `relationshipType`, `target`, and an English
`caption`. A quoted multiword target stays one Thought. `sourceFact` names another
fact's array index when the source is itself a Relationship; the engine builds
the bracketed input and adds that prerequisite automatically. `requires` can
name additional prerequisite indices. Each step supplies `title`, `copy`, `try`,
and `choices` (fact indices and component entries). Earlier choices carry forward automatically.
See “Composing a step's content” below for selectors, editors, and result components.

`uks-graph.js` exports `createUKSGraph` for demo engines or independent pages.
Pages use `loadRoot`, `setPresets`, `refresh`, `fit`, and `setLabels`; graph state
and drawing stay inside the component. Hovering expanded components or their
connectors identifies Source, Type, and Target in the lower-left display.
Changes to the common engine, component, or CSS apply to every importing demo.

One ASP.NET Core application serves a relationship editor, a 3D graph page, and JSON operations. It references `../UKS/UKS.csproj` directly. Each sandbox gets an independent UKS instance. Demo selection, shared URL access, and limits live entirely in this web host; it uses the current CJS UKS library without importing the MultiUser branch's UKS changes. No desktop application, Python installation, frontend build, or test project is needed.

## Build and run in Visual Studio

1. Install Visual Studio 2022 version 17.8 or newer with the **ASP.NET and web development** workload and the .NET 8 SDK/targeting pack.
2. Open `UKSWeb.sln` in this folder.
3. Set **UKSWeb** as the startup project if necessary.
4. Choose **Build Solution**, then press **Ctrl+F5**. The browser opens at <http://localhost:5080>.

The separate solution includes only UKSWeb and UKS, avoiding the desktop solution's other dependencies.

## Build and run from a terminal

With the .NET 8 SDK or a later SDK with .NET 8 targeting support installed, run these commands from the repository root:

```powershell
dotnet build UKSWeb/UKSWeb.csproj
dotnet run --project UKSWeb/UKSWeb.csproj --no-build
```

Open <http://localhost:5080>. Stop the service with **Ctrl+C**. Running a framework-dependent build requires the .NET 8 ASP.NET Core runtime (included with the .NET 8 SDK).

## Deploy with Docker / Render

The repository-root `Dockerfile` builds UKSWeb and UKS, then runs the published service in a Linux ASP.NET Core image. The `.dockerignore` includes the two projects and their XML demo content while excluding desktop code and local build outputs.

When ready to deploy, commit and push the web project, deployment files, and independent-instance UKS changes to the branch selected by your host. For a Render Docker web service, use:

- **Branch:** select the branch you intend to deploy. `MultiUser` still deploys the older live demo until you change the host configuration or merge the integration into that branch.
- **Root Directory:** leave empty (the repository root). Do not set it to `UKSWeb`, because the build also needs the sibling UKS project.
- **Dockerfile Path:** `./Dockerfile`.
- **Docker Build Context Directory:** repository root (`.`), if this setting is shown.
- **Docker Command:** leave empty; the image supplies its own start command.

Redeploy after pushing. The application listens on all interfaces at the host's `PORT` environment variable, or port 8080 when PORT is unset. Docker is needed on the build host, not on visitors' computers.

To build and run locally with Docker installed, run from the repository root:

```powershell
docker build -t uks-web .
docker run --rm -p 5080:8080 uks-web
```

Open <http://localhost:5080>. Container restarts and deployments discard in-memory additions. Sandbox links remain valid only while this server process retains their in-memory UKSs.

## Try it

1. Add **Fido**, **is-a**, **dog** using the three fields.
2. Look up **dog** in the Thought content area. **Fido** appears as a child.
3. Add **Rover**, **is-a**, **dog**. The displayed children refresh to include both.
4. An ordinary relationship such as **Fido**, **is**, **brown** can also be added. It does not make brown a child of Fido: children specifically follow the UKS is-a hierarchy.
5. Click the trash button beside **Fido** to delete that Thought from UKS. The child list refreshes automatically. This deletes the Thought, rather than just its parent relationship.
6. Add **dog**, **has**, **fur**, then show **dog**. A separate `[dog → has → fur]` row appears alongside the children. The scrollable list shows only direct stored relationships, including parent relationships; it does not include inherited attributes or evaluate exceptions. Each relationship row also has a trash button. It deletes that specific stored relationship while preserving its source, relationship type, and target Thoughts.

After a lookup, the displayed Thought's content refreshes every two seconds, including changes made in another browser tab. Editing the lookup field does not change the watched Thought until **Show content** is pressed. Unchanged results retain their existing rows; changed results preserve the list's scroll position. Automatic requests pause while the page is hidden and resume when it becomes visible. No server or UKS changes are required for this polling mechanism.

Opening a demo entry URL creates a fresh in-memory UKS. Opening a copied address reuses that exact UKS with full access. Editor and graph tabs preserve the current content. The service is separate from any running desktop BrainSim process. Restarting the service discards its sandboxes and invalidates their links.

## Three-field statement input

The personal demo editor parses each field using `StatementInputParser.cs`, which calls the existing UKS phrase and nested-statement functions.

- Existing Thought labels are reused. Quoted labels such as `"family dog"` can contain spaces literally.
- Source and Target phrases put attributes before the noun: `big dog` creates/reuses `dog.big`.
- Relationship type phrases put attributes after the verb: `has 3` creates/reuses `has.3`. Nouns follow the desktop singularization convention. `have 4` normalizes to `has.4`, including inside nested Relationships; other verb forms are preserved.
- Bracketed arrow expressions can occupy any field. Both `->` and `→` are accepted.

For example, enter these three fields:

```text
Source:             [Fido->gets->wet]
Relationship type:  IF
Target:             [[Fido->is->outside] AND [weather->is->rainy]]
```

The existing parser makes the conclusion and premises possible Relationships (`gets.?`, `is.?`) and retains IF/AND as structural Relationships with inherited capabilities. Entering the rule does not assert that Fido is outside, that the weather is rainy, or that Fido is wet. The Added message shows the complete nested expression. The content list continues to show stored direct Relationships; inference uses the existing UKS query API.

The same parsing adapter runs first in the quota preview and then in the live UKS. Syntax errors and additions over the allowance are rejected before live parsing.

## 3D graph explorer

Click **Explore UKS in 3D** on the editor, or open `/graph.html`. The link preserves the current demo session and root label. The graph page uses [3d-force-graph](https://github.com/vasturiano/3d-force-graph), pinned to version 1.80.1 and loaded from jsDelivr, plus three-spritetext 1.10.0 from esm.sh for visible labels. It requires internet access to those CDNs and a WebGL-capable browser; no Node.js or frontend build is needed.

- Enter a root label and press **Load root**. Initially only that Thought appears.
- **Single-click a node** to expand its immediate incoming and outgoing relationships; click an expanded node again to collapse it. Descendant neighborhoods that are no longer reachable through another expanded branch are removed too. Neighbors do not expand until clicked. Loading a root again resets the explored view.
- **Click an ordinary edge** to replace it visually with its actual relationship Thought and three connector edges: source → relationship, relationship type → relationship, and relationship → target. Those connector edges are structural display elements and cannot be expanded. The source, type, and target nodes remain normal expandable Thoughts. For example, expanding `dog → has.4 → leg`, then clicking the `has.4` node, lets you navigate to `4`. Collapsing a promoted relationship node restores the ordinary edge. These operations do not modify UKS content or consume quota.
- Node labels are shown by default. Uncheck **Labels** to hide them; hovering still shows the full label. Hover over an edge for its relationship type and endpoints; arrows indicate direction.
- Already-expanded neighborhoods refresh automatically every two seconds; **Refresh now** performs an immediate check and fits the graph. New nodes are also fitted into view automatically so they cannot be added off-screen unnoticed. Unchanged graphs keep their view and are not rebuilt. Disconnected branches disappear. Unexpanded neighbors stay unexpanded. Refresh pauses while the page is hidden and checks immediately when it becomes visible again.
- Drag the background to rotate, scroll to zoom, and right-drag to pan. **Fit graph** frames all revealed nodes; expansion also fits after the layout settles.
- Gold denotes the root, blue denotes unexpanded nodes, green denotes expanded nodes, and purple denotes relationship nodes and their structural connectors. Only the current demo session's content is returned. No inherited relationships are computed.

`GET /api/graph?label=dog&session=<key>` returns the root alone. `GET /api/graph?id=<handle>&expand=true&session=<key>` returns one neighborhood. Returned `nodes` carry labels; `links` carry source/target handles and a `type` label. Opaque graph handles are temporary server-process references, not persistent Thought identities or changes to UKS labels. They also allow unnamed Thoughts and nested Relationships to be explored. Handles from another sandbox return HTTP 404. This is a read-only view; expanding nodes does not consume the Thought allowance.

`GET /api/graph?ids=<handle>,<handle>&session=<key>` refreshes up to 256 expanded neighborhoods in one request and returns `snapshots` plus `missingIds` for deleted or inaccessible nodes. Larger revealed graphs are refreshed in batches. No descendants are fetched unless their nodes were explicitly expanded.

Expanded neighborhoods refresh through the shared graph component's batch API. HTML and JavaScript responses require cache revalidation. Reload the page after changing static files; restart the service after changing server code. An already-open tab continues using the scripts it loaded until reloaded.

## Demo links and shared addresses

Most visitors open a demo URL and use the page directly:

```text
http://localhost:5080/?demo=Demo1.xml
http://localhost:5080/graph.html?demo=DemoText.xml
http://localhost:5080/graph.html?demo=NatureAnimals.xml&root=Fido
```

The server recognizes filenames and demo names from its packaged XML catalog. Each entry link opens a fresh UKS loaded from that XML. The source XML remains unchanged. Desktop modules do not execute in the web host; these pages explore and edit the stored content.

The **XML dropdown at the top right** is the only demo control. Changing its selection immediately opens a fresh copy of the selected file. Standard UKS starts from the normal initialized content. There is no separate create button or sharing toolbar.

The page automatically preserves its current content with a short opaque `session` key in the URL, along with the demo and root. Refreshing or moving between the editor and 3D page reopens the same content. Users do not need to manage IDs or accounts.

**Copy the address bar to share the current live demo.** Anyone with that address has full access, including additions and deletion. There are no viewing/editing modes or separate editing secrets. To distribute fresh-start demos, use a URL containing only `demo` (and optionally `root`), without a session key.

## Warnings when leaving changes

Selecting another XML file opens it immediately. Leaving, closing, reloading, and switching demos never prompt about edits. Moving between the editor and graph for the same session preserves its content; reloading the session does too.

Switching demos does not immediately erase the previous one. Its original address continues to work until inactivity expiration or a server restart. The footer explains when edits are deleted.

## Inactivity expiration

Sandboxes expire after **30 minutes without user activity** by default. Set `SandboxIdleMinutes` to a positive number to change this; fractional minutes are supported. For example:

```powershell
$env:SandboxIdleMinutes = '15'
dotnet run --project UKSWeb/UKSWeb.csproj
```

Manual browsing, page opens, edits, and trusted input such as graph dragging, scrolling, and typing reset inactivity. Any visitor using a shared address can keep that session active. Local input is reported through a throttled activity request; no heartbeat runs merely because a tab is open.

Automatic refresh calls carry `X-Sandbox-Poll: 1` and do not reset inactivity. An unattended visible tab therefore expires normally. When the server reports `sandbox_unavailable`, the pages stop polling, disable edits, and invite the visitor to choose an XML file again. The expired URL is never silently replaced with fresh content.

The server checks expiry before admitting a request and sweeps idle entries periodically (at most once a minute). Requests already in progress hold a lease until their response completes. Cleanup skips busy sandboxes rather than blocking behind their work; a manual operation gets a fresh idle interval after it finishes. Cleanup removes the UKS from the registry and drops expired graph handles. Memory is reclaimed by normal garbage collection.

## Configuring XML demos

The web project links the top-level `BrainSimulator/UKSContent/*.xml` files into a `Demos` directory in its build and publish output. Docker includes the same content. No duplicate XML source files are needed.

To use a different server-controlled catalog, set the **`DemoDirectory`** configuration value or environment variable to a directory of XML files. Relative paths are resolved against the application's content root. Only top-level XML files in that directory are available. Browser requests select a catalog entry; arbitrary file paths, traversal paths, and remote URLs are rejected. Add files and restart the service to refresh the catalog.

## Sandbox isolation and Thought allowance

Each session key identifies an independent UKS, including its own initial content. Requests select it through execution-local `UKS.UKS.theUKS` and restore the previous selection afterward. Each sandbox has its own lock, so operations in different sandboxes can proceed concurrently. Graph handles are checked against the selected UKS, including batch requests.

Each sandbox may add **1,000 stored Thoughts** by default. Links count too, including automatically created hierarchy and sequence links. The complete XML-loaded or standard initial content is excluded from this allowance. Deleting additions releases capacity; deleting initial content does not grant extra capacity.

Set **`ThoughtLimitPerUser`** to another positive integer through service configuration or an environment variable. The existing configuration name is retained:

```powershell
$env:ThoughtLimitPerUser = '2000'
dotnet run --project UKSWeb/UKSWeb.csproj
```

The host previews writes in an isolated in-memory copy before applying them to the live sandbox. An addition exceeding the allowance returns HTTP 409 without changing existing content or leaving partial additions. Reads, deletion, and repeating an existing relationship remain available at the limit. Preview copies do not replace the live UKS or invalidate graph handles. Copying adds work to each write; the allowance is intended for small demonstration sandboxes.

## Service interface

`POST /api/sandboxes?demo=dogs` creates an XML-loaded sandbox; omit `demo` for standard initialization. Returns `id`, `demo`, `thoughtLimit`, `idleTimeoutMinutes`, and `hasChanges`. Creation is explicit; reading pages or existing sandbox metadata never calls this operation when a session key is supplied.

`GET /api/sandbox?session=<key>` returns `id`, `demo`, `thoughtLimit`, `idleTimeoutMinutes`, and `hasChanges`. Unknown or expired keys return HTTP 404 with `code: "sandbox_unavailable"`. `GET /api/demos` returns the public XML catalog.

All content operations below require `session=<key>`. The key grants full access; no separate credentials or editing headers are used. Successful additions and deletions mark the session changed. Content responses include `X-Demo-Changed: 1` once it has been edited, reporting whether the session has been edited.

`POST /api/statements`, with `Content-Type: application/json`:

```json
{"source":"Fido","relationshipType":"is-a","target":"dog"}
```

Calls `StatementInputParser.Add`, which resolves labels and attribute phrases, parses nested Relationships, and calls the existing UKS statement API. Standard sandboxes use `CreateInitialStructure()`; XML sandboxes use `LoadUKSfromXMLFile()` and establish their allowance baseline after loading. Returns readable `source`, `relationshipType`, and `target` expressions, formatting nested Relationships recursively. Normal UKS relationship rules apply, including inverse normalization.

`GET /api/children?label=dog`:

```json
{"label":"dog","children":[{"label":"Fido"}],"relationships":[{"id":42,"source":"dog","relationshipType":"has","target":"fur"}]}
```

Looks up the existing Thought and returns its direct children plus its directly stored outgoing relationships, from the selected sandbox. Children are sorted alphabetically; relationships are sorted by type and target. Missing Thoughts return HTTP 404; blank fields return HTTP 400. Only explicit response data is serialized, never the graph objects. Requests to these operations are serialized within each sandbox.

`GET /api/graph?label=Fido` (or `id=<handle>`) returns a graph packet. Add `expand=true` for direct connections. `labels=Fido&labels=weather` or `roots=<handle>,<handle>` returns a single combined packet for multiple starting Thoughts (maximum 256). The packet includes `roots`, deduplicated `nodes` and `links`, and `sequences`. Missing members are reported in `missingIds` / `missingLabels`; an empty `roots=` returns an empty graph. The existing `ids=` query remains a batch of individual expanded snapshots for refresh.

Sequence detection uses direct `VLU` link identity, then follows `NXT` until the end or a repeated Thought. It never checks for `SeqElement`. Each sequence describes its ordered cell handles and optional cycle endpoint; each cell supplies its `sequenceValue` separately, preserving repeated values as distinct positions. Structural Relationship endpoints are checked too. The underlying UKS is unchanged.

Both graph pages use `uks-graph.js` for sequence composition, compact rigid rows, whole-row dragging, camera fitting, and graph-command Help. The controller's `loadThoughts([handle, ...])` or `loadThoughts([label, ...])` displays multiple starting Thoughts; `loadThoughts([])` clears the view. This supplies the graph interface for future search results, without implementing search itself.

`DELETE /api/relationships?id=<handle>&session=<key>` removes exactly the stored relationship identified by a row's `id`. It uses the existing UKS link cleanup and preserves the endpoint Thoughts. The server checks the containing UKS and that the relationship is still attached; stale, deleted, or foreign-session handles return HTTP 404. Successful deletion marks the demo changed and releases its stored-content allowance. Row IDs are stable across ordinary refreshes.

`DELETE /api/thoughts?label=Fido` calls the existing `Thought.Delete()` method and returns `{"label":"Fido"}`. A missing Thought returns HTTP 404; a blank label returns HTTP 400. This deletes the Thought itself using the DLL's existing relationship cleanup; it does not recursively delete its children.

For example, `GET /api/children?label=dog&session=<key>` lists children in that UKS. Use the same session key for POST and DELETE.

This project uses the BrainSim Thought API: `Thought`, `Link : Thought`, `LinksTo`, and `LinksFrom`. Every Thought has an immutable containing `Store`. Equality distinguishes containing UKSs, so identical labels in different sandboxes remain distinct. Only response data is sent to the browser, never live UKS objects.

The `*_wpftmp.csproj` file in the desktop folder is a generated WPF build file. It is not referenced by this project or solution.

## Before a public deployment

The current web host still needs resource controls beyond per-sandbox Thought quotas: a total sandbox/memory cap, limits on sandbox creation and concurrent expensive operations, and limits on statement field sizes and graph response work. XML baselines consume memory too; quotas check additions after previewing an operation, so a large rejected input can still consume CPU and temporary memory.

The web project and Docker images target .NET 8, whose support ends November 10, 2026. Upgrade the web host and container runtime to a supported release such as .NET 10 before ongoing public operation. See [Microsoft's support notice](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/).

Use HTTPS for shared demo addresses. The 3D page currently executes scripts loaded from external CDNs; self-hosting the complete graph dependency bundle would remove that external script trust from pages whose URLs grant access to demo content.

Run a single application process/replica with the current in-memory design. Multiple processes have different session registries; requests routed to another process cannot find existing content. A restart intentionally loses its sandboxes. Plan memory/error monitoring and deployment restarts accordingly.


## Search, inheritance, and exceptions demo

Open `/experiment2.html` for the second guided demo, or `/demo-frame.html?experiment=2` to test it inside the WordPress-style frame. Its three steps show effective attributes (with direct/inherited provenance), candidate search from selected attributes, and the dog/Tripper leg-count exception. The attribute dialog uses dropdowns throughout. Candidate scores are the existing UKS ranking, not probabilities; displayed match counts are checked against effective attributes.

`experiment2.html` contains the sample facts, steps, and selectable vocabulary. `experiments.js` manages the common progression and optional initial facts; `uks-query.js` manages reusable query controls and results; `uks-graph.js` remains the only graph implementation. On a fresh sandbox the page seeds a small animal hierarchy; reloads preserve edits while the sample Thought Tweety remains present. The third step's Tripper exception starts absent and can be added or removed. The comparison highlights differing relationship types for the same target.

`GET /api/attributes?label=Fido&session=<key>` calls `GetAttributes` and returns effective relationships with inheritance depth/category. These are query values, not stored Relationship handles. `GET /api/candidates?root=animal&types=has&targets=fur&types=is&targets=brown&session=<key>` accepts up to 16 attribute pairs, calls `SearchByAttributes`, and returns up to 100 positive-score candidates with graph handles and effective match counts. The graph displays the first 32 together; individual candidates can be inspected. Query patterns are temporary unlabeled Thoughts, detached in a `finally` block. All query calls use the sandbox lock; no underlying UKS changes are required.


Query-demo results now live in the wider left column. Thought selection loads attributes immediately; it needs no submit button. Manage attributes lists direct relationships using the existing children endpoint and deletes the chosen stored handle through the relationship endpoint. Inherited results are managed at their originating Thought.

Attribute responses include `evidenceLinkIds` and `evidenceThoughtIds`. The web host reconstructs one stored inheritance route to `InheritedFromCategory` and appends the matching stored attribute. `depth` is the UKS's `InheritanceDepth`, not a browser estimate and not a candidate search score. If a computed result has no matching stored support, its evidence arrays are empty. This route is supporting evidence, not an internal search execution trace. Candidate responses include the effective attributes matching their search criteria with the same evidence metadata. The shared graph controller's `setEvidence(attributes, {fit, automatic})` displays and highlights those stored links in orange, including structural connectors if a highlighted Relationship is expanded. Clicking an attribute focuses its support; polling refreshes the selected explanation without refitting the camera.


Background refresh pauses while a select/input is focused or any dialog is open; polling no longer disables controls. The attribute dialog opens beside the left edge of the graph and is clamped to the viewport. Experiment instructions follow query results immediately above navigation. Supporting relationships change color to orange without changing line/arrow size. Emphasis expires after eight seconds; automatic polling updates evidence without renewing the timeout. The supporting graph remains visible after its colors return to normal.


The query demo opens with no selected Thought or results. The exception step uses the same selector as attribute lookup and shows only the selected Thought's effective attributes. Its suggested add/remove relationships are filtered to that Thought: Tripper's leg count/barking exceptions and robot-dog's food/fur exceptions. `robot-dog is-a dog` is a baseline fact; explicit negative properties are optional. Adding an exception removes the inherited default from effective results; removing it restores the default. Existing sandboxes missing robot-dog receive only its baseline membership via the configurable `seedGroups` bootstrap, without resetting prior edits. Manage attributes places Add and Delete to the right of their dropdown rows.


## Sequences demo

Open `/experiment3.html` or `/demo-frame.html?experiment=3`. The four steps cover spelling, matching letter fragments, recording order on a 3-by-3 digit pad, and recognizing/completing melodies. Words can be chosen or typed; every other input can be selected. Tune examples are short public-domain melody phrases with fixed-duration note playback through Web Audio. Matching compares ordered values, not timing intervals or rhythm. A partial match is an exact contiguous fragment of a stored sequence, rather than approximate spelling/pitch matching.

The page supplies words, fragments, digit-order examples, tunes and step text. Shared `experiments.js` handles progression; `uks-sequences.js` handles sequence input/results; all graph behavior remains in `uks-graph.js`. The optional `sequenceValues=true` graph query includes all VLU targets and links alongside the NXT chain. Sequence cells remain a rigid row; exposed distinct value Thoughts occupy a separate row and repeated values share their canonical Thought. Graph discovery continues to follow VLU/NXT identities, without requiring SeqElement classification.

`POST /api/sequences` accepts `{kind:"word"|"digits"|"music",label,values:[...]}` within the selected session. It calls the existing UKS `AddSequenceAndLink` with compression disabled to keep the introductory representation flat. Writes are previewed in a separate in-memory UKS for the existing Thought allowance. A repeated identical addition reuses its sequence; an occupied name with different content returns 409. `GET /api/sequences?kind=...` lists that sandbox's demo sequences. `GET /api/sequence-matches?kind=...&values=<JSON array>` uses `FindSequencesByActivation` to return complete matches and occurrences within stored sequences, including offset, matched length and the following values. This remainder supplies music completion. Search uses a temporary, unlabelled options Thought that is deleted afterward; it creates no stored query sequence.


The shared graph temporarily disables its global centering force when fixed sequence rows are visible. Otherwise that force can push free referrer nodes far away while trying to balance an immovable row. Links into a sequence start use a shorter, stronger spring (including expanded Relationship target connectors); owners remain free to drag. Ordinary centering resumes when no sequence rows remain.


All three guided demos use the shared `guided-demo` layout: a 411px left pane, a bounded explanation above a persistent scrollable `action-zone`, optional independently scrollable results, and a bottom Try This region above Back/Next. Choices, query controls, keyboards, digit pads and tune controls all live inside the action zone. Its size is independent of the number of controls; step changes reset its scroll position and ordinary updates preserve it. Feedback space is reserved so messages do not resize the actions. Try This reserves approximately two extra text lines (7.5 line heights total), scales with its font, and scrolls on overflow; responsive height caps keep navigation reachable in smaller viewports.


The author-written step fields `title`, `copy`, and `try` accept trusted HTML, including `<strong>`, `<b>`, `<em>`, `<br>`, `<p>`, `<ol>`, `<ul>`, and `<li>`. Use JavaScript template literals (backticks) for multiline content, for example:

```javascript
try: `<strong>Try these steps:</strong>
  <ol>
    <li>Add <b>Fido is a dog</b>.</li>
    <li>Click the Relationship to explore it.</li>
  </ol>`
```

These HTML fields are supplied only by the experiment author. UKS labels, visitor input, query results, and status messages still render as plain text. Try This and explanation containers support block elements such as lists and remain scrollable.


Refreshing a guided demo resumes its current step and completed-step gates, input selections, entered sequence buffers, displayed query/match results, scroll positions, graph roots/expansions, node positions and camera view. Each tab stores its UI state in sessionStorage, keyed by the UKS session and experiment path. Results and graph handles are revalidated against the live UKS on restoration; the browser does not restore an old copy of the knowledge store. A different session/demo has separate progress, shared URLs do not transfer a visitor's UI state, and expiry/server restarts still discard UKS content. Browser storage failures leave the demo usable with ordinary initial UI state. No exit warnings are installed.


For embedded demos, the shared session client also remembers the active sandbox ID/root for each demo path/XML selection in that tab. When WordPress reloads an iframe's original URL without a `session` parameter, the iframe recovers that sandbox and its UI progress. An expired remembered sandbox starts a fresh demo; an explicitly supplied expired/shared session still reports unavailable. Choosing an XML file explicitly clears that destination's remembered session so it continues to create fresh content. All of this uses tab-scoped browser storage and does not extend server lifetime or introduce exit warnings.


Relationship annotations retain their source/type/target components in the graph even if the base Relationship was previously displayed only as an edge. The server projects those components from the actual UKS Link fields, recursively handling nested Relationship endpoints. The shared selection helper keeps that structure when trimming a snapshot to a selected fact, and composition replaces the ordinary edge with its represented Relationship node to avoid duplicate representations. These component connectors are display metadata, not newly asserted UKS relationships. Genuinely disconnected stored content remains separate; the renderer does not invent a connection to the current root.


Clicking a represented Relationship now collapses it even when its components were revealed automatically for an annotation. The view restores its ordinary edge and hides dependent annotation branches, leaving their stored UKS links and added-choice state untouched. Clicking that edge reopens the Relationship. Explicit collapsed IDs are tab-state data, survive refresh, and are respected by automatic polling. Adding a newly selected annotation reveals the relevant source components again. Disconnected explicit roots are preserved; collapse filters a dependency branch rather than inventing connections or deleting content.


Reset Content beside Fit View starts a fresh sandbox for the same demo/XML baseline and returns to its first step. Guided demo baseline facts are seeded normally; progress, inputs and graph view start fresh under the new session ID. The old shared address is not modified. This action also works when the previous sandbox has expired and needs no departure confirmation.

Choices accumulate by default. To start a new set of action buttons at a particular step, set `inheritChoices: false` alongside that step's `choices` array. Subsequent steps accumulate from that boundary until the next such reset. For example:

```javascript
{ title: 'A fresh set of choices', inheritChoices: false, choices: [5, 6, 7],
  copy: '...', try: '...' }
```

This changes which choices are offered, not the stored UKS content. Use Reset Content when the knowledge store itself should start fresh.


To remove only selected preset choices while retaining others, use `removeChoices: [0, 1]` on a step. These numbers index the page's facts array, like `choices`. Removal is applied after that step's additions and carries forward; a later step can offer a removed choice again by listing it in `choices`. Neither `removeChoices` nor `inheritChoices` deletes a stored relationship.

### Composing a step's content

A step's `choices` array accepts fact indices and reusable component entries in display order. All controls and results live in the scrollable action area; Try This remains below it. Experiment 1's fifth step now includes a Thought selector, attribute editor, and attribute results. Experiments 2 and 3 use the same component mechanism.

```js
{
  title: 'Advanced topics',
  inheritChoices: false,
  choices: [
    14, 15,
    { component: 'thought-selector', id: 'subject' },
    { component: 'attribute-editor', label: 'Manage attributes…' },
    { component: 'attribute-results', source: 'subject', height: '12rem' }
  ]
}
```

`demo-components.js` mounts the components; `uks-query.js` and `uks-sequences.js` own their behavior and state. The experiment HTML supplies facts, steps, selectable vocabulary (`query`), words, and tunes (`sequence`). For an attribute editor without a `query` configuration, vocabulary defaults to the page's facts. Supply `query.subjects`, `query.types`, and `query.targets` to offer additional selections.

| Component | Purpose / step mode |
| --- | --- |
| `thought-selector` | Choose the subject in attributes/exceptions mode. |
| `attribute-editor` | Open the existing Add/Delete attribute dialog. |
| `attribute-search` | Attribute checkboxes and Find candidates, in candidates mode. |
| `attribute-choices`, `candidate-search` | The two search controls, if separate placement is wanted. |
| `attribute-results`, `candidate-results` | Output of the current query; optional `source` names the selector/search `id`. |
| `word-input` | Word selection/input and Add, in spelling mode. |
| `letter-keyboard`, `digit-pad`, `note-keyboard` | Input controls for letters, digits, or music respectively. |
| `sequence-input` | Current input buffer with Undo/Clear. |
| `sequence-match` | Match the current letter, digit, or note buffer. |
| `sequence-examples`, `order-examples` | Letter fragments or remembered digit examples. |
| `remember-sequence` | Store the current digit order. |
| `tune-selector`, `playback` | Select/learn a tune or play entered notes, in music mode. |
| `sequence-values`, `sequence-library` | VLU display toggle or stored-sequence list. |
| `sequence-results` | Sequence display, matches, or melody completion output. |
| `html` | Trusted author HTML, for example `{component:'html', id:'note', html:'<b>Try this</b>'}`. |

Use the step's existing `query: 'attributes' | 'candidates' | 'exceptions'` or `sequence: 'spelling' | 'letters' | 'digits' | 'music'` to choose its mode. There is one shared query stream and one shared sequence stream per page, with at most one instance of each control/result component per step. The optional result `source` binds to that stream's selector/search ID; it does not create additional independent queries. Place the producer and its results in the same step. `height` optionally bounds the result box; results scroll when longer.

Components carry forward like fact choices. An `id` identifies a component across steps; otherwise its component name does. Repeating its ID/name updates it in place. Use `inheritChoices: false` to start fresh, or `removeChoices: [14, 'attribute-editor', 'subject']` to remove selected fact indices, component names, or explicit IDs. These settings affect the offered UI only, never delete UKS data.

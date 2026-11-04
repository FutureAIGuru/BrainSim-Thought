# UKS web proof of concept

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

The graph falls back to individual node-expansion requests when an older running service does not support batching. HTML and JavaScript responses require cache revalidation, and the graph script URL includes a revision. After changing the web project, restart the running service and reload the page; seeing a new HTML control alone does not establish that an older tab has loaded the latest script.

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

Selecting another XML file after editing asks for confirmation. Cancelling restores the previous selection and leaves the current content intact. Moving between the editor and graph for the same session preserves edits and does not prompt. Reloading the current session retains its content.

A browser departure warning also protects edits when leaving through other navigation, such as entering a different demo URL or closing the tab. Browsers display their own standard warning text. Changes are tracked on the server, so reopening a shared address or moving to another page retains the warning behavior; normal refresh responses also identify changes made by another visitor. A write still in progress is protected too.

Switching demos does not immediately erase the previous one. Its original address continues to work until inactivity expiration or a server restart. The warning helps avoid losing track of that address. The footer explains when edits are deleted.

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

All content operations below require `session=<key>`. The key grants full access; no separate credentials or editing headers are used. Successful additions and deletions mark the session changed. Content responses include `X-Demo-Changed: 1` once it has been edited, allowing other pages to protect those changes when navigating away.

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

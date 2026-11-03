# UKS web proof of concept

One ASP.NET Core application serves a relationship editor, a 3D graph page, and four JSON operations. It references `../UKS/UKS.csproj` directly. Each selected user gets an independent UKS instance. User selection and limits live entirely in this web host; it uses the current CJS UKS library without importing the MultiUser branch's UKS changes. No desktop application, Python installation, frontend build, or test project is needed.

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

The repository-root `Dockerfile` builds UKSWeb and UKS, then runs the published service in a Linux ASP.NET Core image. The `.dockerignore` keeps desktop files, documentation, and local build outputs out of the build context.

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

Open <http://localhost:5080>. Container restarts and deployments discard in-memory additions. The user dropdown remains a demonstration selector, including user 0; it is not authentication.

## Try it

1. Add **Fido**, **is-a**, **dog** using the three fields.
2. Look up **dog** in the Thought content area. **Fido** appears as a child.
3. Add **Rover**, **is-a**, **dog**. The displayed children refresh to include both.
4. An ordinary relationship such as **Fido**, **is**, **brown** can also be added. It does not make brown a child of Fido: children specifically follow the UKS is-a hierarchy.
5. Click the trash button beside **Fido** to delete that Thought from UKS. The child list refreshes automatically. This deletes the Thought, rather than just its parent relationship.
6. Add **dog**, **has**, **fur**, then show **dog**. A separate `[dog → has → fur]` row appears alongside the children. The scrollable list shows only direct stored relationships, including parent relationships; it does not include inherited attributes or evaluate exceptions. Relationship rows have no Thought-delete button.

After a lookup, the displayed Thought's content refreshes every two seconds, including changes made in another browser tab. Editing the lookup field does not change the watched Thought until **Show content** is pressed. Unchanged results retain their existing rows; changed results preserve the list's scroll position. Automatic requests pause while the page is hidden and resume when it becomes visible. No server or UKS changes are required for this polling mechanism.

At startup, the host creates five independent in-memory UKSs and calls `CreateInitialStructure()` on each. Tabs selecting the same user see the same sandbox; different users have separate copies of the initialized content. The service is separate from any running desktop BrainSim process. There is no save/load interface, account handling, or persistence; restarting the service resets all sandboxes.

## 3D graph explorer

Click **Explore UKS in 3D** on the editor, or open `/graph.html`. The link carries the selected user and root label. The graph page uses [3d-force-graph](https://github.com/vasturiano/3d-force-graph), pinned to version 1.80.1 and loaded from jsDelivr, plus three-spritetext 1.10.0 from esm.sh for visible labels. It requires internet access to those CDNs and a WebGL-capable browser; no Node.js or frontend build is needed.

- Enter a root label and press **Load root**. Initially only that Thought appears.
- **Single-click a node** to expand its immediate incoming and outgoing relationships; click an expanded node again to collapse it. Descendant neighborhoods that are no longer reachable through another expanded branch are removed too. Neighbors do not expand until clicked. Loading a root again resets the explored view.
- **Click an ordinary edge** to replace it visually with its actual relationship Thought and three connector edges: source → relationship, relationship type → relationship, and relationship → target. Those connector edges are structural display elements and cannot be expanded. The source, type, and target nodes remain normal expandable Thoughts. For example, expanding `dog → has.4 → leg`, then clicking the `has.4` node, lets you navigate to `4`. Collapsing a promoted relationship node restores the ordinary edge. These operations do not modify UKS content or consume quota.
- Node labels are shown by default. Uncheck **Labels** to hide them; hovering still shows the full label. Hover over an edge for its relationship type and endpoints; arrows indicate direction.
- Already-expanded neighborhoods refresh automatically every two seconds; **Refresh now** performs an immediate check and fits the graph. New nodes are also fitted into view automatically so they cannot be added off-screen unnoticed. Unchanged graphs keep their view and are not rebuilt. Disconnected branches disappear. Unexpanded neighbors stay unexpanded. Refresh pauses while the page is hidden and checks immediately when it becomes visible again.
- Drag the background to rotate, scroll to zoom, and right-drag to pan. **Fit graph** frames all revealed nodes; expansion also fits after the layout settles.
- Gold denotes the root, blue denotes unexpanded nodes, green denotes expanded nodes, and purple denotes relationship nodes and their structural connectors. Only the selected sandbox's content is returned. No inherited relationships are computed.

`GET /api/graph?label=dog&user=1` returns the root alone. `GET /api/graph?id=<handle>&expand=true&user=1` returns one neighborhood. Returned `nodes` carry labels; `links` carry source/target handles and a `type` label. Opaque graph handles are temporary server-process references, not persistent Thought identities or changes to UKS labels. They also allow unnamed Thoughts and nested Relationships to be explored. Handles from another sandbox return HTTP 404. This is a read-only view; expanding nodes does not consume the Thought allowance.

`GET /api/graph?ids=<handle>,<handle>&user=1` refreshes up to 256 expanded neighborhoods in one request and returns `snapshots` plus `missingIds` for deleted or inaccessible nodes. Larger revealed graphs are refreshed in batches. No descendants are fetched unless their nodes were explicitly expanded.

The graph falls back to individual node-expansion requests when an older running service does not support batching. HTML and JavaScript responses require cache revalidation, and the graph script URL includes a revision. After changing the web project, restart the running service and reload the page; seeing a new HTML control alone does not establish that an older tab has loaded the latest script.

## User selection and sandbox isolation

The dropdown selects user 0�4. Every service request includes `user=<number>`; omitting it selects user 0. This is a demonstration selector, not authentication: anyone can select any of the five sandboxes.

- Each user has their own UKS, including their own fundamental content. Changes by user 0 are not broadcast or inherited by the others.
- The same label, such as Fido, resolves to separate Thoughts in separate sandboxes. There are no owner fields, prefixed labels, overrides, or deletion lists.
- Users can change or delete initialized content in their own sandbox. Deleting a Thought never changes another user's content. All child trash buttons are enabled.
- Requests select the sandbox through the execution-local `UKS.UKS.theUKS` and restore the previous selection afterward. Instance calls use that sandbox explicitly.
- Each sandbox has its own lock. Requests for one sandbox are serialized, while requests for different sandboxes can proceed concurrently. Graph handles are also checked against the selected UKS, including batch refresh requests; another sandbox's handle returns HTTP 404 or appears in `missingIds`.

Try adding `Fido is-a dog` and `Fido has fur` as user 1. Switch to user 2 and add `Fido is-a bird` and `Fido has feathers`. Looking up Fido in each sandbox shows only its own relationships. Delete user 1's Fido and verify that user 2's Fido remains.

## Per-user Thought limit

Users 1�4 may add **1,000 stored Thoughts each** by default. Links count as Thoughts, including automatically created hierarchy and sequence links. Each sandbox's initialized content is excluded from its allowance. User 0 is exempt. Deleting added content releases capacity; deleting initialized content does not grant extra capacity.

Set **`ThoughtLimitPerUser`** to another positive integer through service configuration or an environment variable. For example:

```powershell
$env:ThoughtLimitPerUser = '2000'
dotnet run --project UKSWeb/UKSWeb.csproj
```

The web host previews a limited user's write in a temporary in-memory copy before applying it to the live sandbox. An addition exceeding the limit returns HTTP 409 without altering existing content or leaving partial additions. Repeating an existing relationship, reading, and deletion remain available at the limit. Preview copies are discarded; they do not replace the live UKS or invalidate graph handles. This approach favors correctness for small demonstration sandboxes; copying adds work to each limited user's write.

The quota and sandbox selection require no user-specific changes to the UKS library. To preload an XML demo later, initialize each sandbox separately with the existing `LoadUKSfromXMLFile` API and establish its allowance baseline after loading.

## Service interface

`POST /api/statements`, with `Content-Type: application/json`:

```json
{"source":"Fido","relationshipType":"is-a","target":"dog"}
```

Calls the existing `UKS.AddStatement(string, string, string)` method, which resolves or creates the necessary Thoughts. The service explicitly calls `CreateInitialStructure()` at startup on each sandbox to create independent fundamental content. Returns the stored relationship's source, relationshipType, and target labels. Normal UKS relationship rules apply, including inverse normalization.

`GET /api/children?label=dog`:

```json
{"label":"dog","children":[{"label":"Fido"}],"relationships":[{"source":"dog","relationshipType":"has","target":"fur"}]}
```

Looks up the existing Thought and returns its direct children plus its directly stored outgoing relationships, from the selected sandbox. Children are sorted alphabetically; relationships are sorted by type and target. Missing Thoughts return HTTP 404; blank fields return HTTP 400. Only explicit response data is serialized, never the graph objects. Requests to these operations are serialized within each sandbox.

`DELETE /api/thoughts?label=Fido` calls the existing `Thought.Delete()` method and returns `{"label":"Fido"}`. A missing Thought returns HTTP 404; a blank label returns HTTP 400. This deletes the Thought itself using the DLL's existing relationship cleanup; it does not recursively delete its children.

For example, `GET /api/children?label=dog&user=1` lists children in user 1's UKS. Use the same `user` query parameter for POST and DELETE.

This project uses the BrainSim Thought API: `Thought`, `Link : Thought`, `LinksTo`, and `LinksFrom`. Every Thought has an immutable containing `Store`. Equality distinguishes containing UKSs, so identical labels in different sandboxes remain distinct. Only response data is sent to the browser, never live UKS objects.

The `*_wpftmp.csproj` file in the desktop folder is a generated WPF build file. It is not referenced by this project or solution.

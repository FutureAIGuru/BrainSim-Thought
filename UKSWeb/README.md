# UKS web proof of concept

One ASP.NET Core application serves a single HTML page and three JSON operations. It references `../UKS/UKS.csproj` directly. UKS now supplies the ownership support used by these operations. No desktop application, Python installation, frontend build, or test project is needed.

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

## Try it

1. Add **Fido**, **is-a**, **dog** using the three fields.
2. Look up **dog** in the Thought content area. **Fido** appears as a child.
3. Add **Rover**, **is-a**, **dog**. The displayed children refresh to include both.
4. An ordinary relationship such as **Fido**, **is**, **brown** can also be added. It does not make brown a child of Fido: children specifically follow the UKS is-a hierarchy.
5. Click the trash button beside **Fido** to delete that Thought from UKS. The child list refreshes automatically. This deletes the Thought, rather than just its parent relationship.
6. Add **dog**, **has**, **fur**, then show **dog**. A separate `[dog → has → fur]` row appears alongside the children. The scrollable list shows only direct stored relationships, including parent relationships; it does not include inherited attributes or evaluate exceptions. Relationship rows have no Thought-delete button.

After a lookup, the displayed Thought's content refreshes every two seconds, including changes made in another browser tab. Editing the lookup field does not change the watched Thought until **Show content** is pressed. Unchanged results retain their existing rows; changed results preserve the list's scroll position. Automatic requests pause while the page is hidden and resume when it becomes visible. No server or UKS changes are required for this polling mechanism.

UKS initializes its normal fundamental content at startup. All browser tabs use the same in-memory UKS in this service process. It is separate from any running desktop BrainSim process. There is no save/load, account handling, or persistence; restarting the service loses additions. This prototype is intended to run locally.

## User selection

The dropdown selects user 0–4. Every service request includes `user=<number>`; omitting it selects user 0. This is a demonstration selector, not authentication.

- User 0 creates and sees shared content, including UKS's initialized content.
- Users 1–4 see shared content plus their own Thoughts and Relationships. Other users' private content is invisible.
- New Thoughts, including Links, get an immutable `Owner` field from `UKS.SetUser`. The selected user is stored per execution context, not in a single global variable.
- Label lookup checks the selected user's labels, then shared labels. Users can independently create a private Rover when no shared Rover exists. Visible labels do not contain the owner prefix used internally in the label dictionary.
- A user may add a private relationship between shared Thoughts. It does not alter another owner's relationship weights or hierarchy. Two users can independently add the same relationship.
- Only the owner may delete a Thought. Shared items have disabled trash buttons for users 1–4, and the service also rejects their deletion with HTTP 403. Per-user hiding of shared items is not implemented.

Try adding `SharedDog is-a Object` as user 0, then `Rover is-a SharedDog` as user 1. User 2 cannot see that Rover, but can add a separate Rover under SharedDog. Deleting user 1's Rover leaves user 2's intact.

Ownership is implemented for the current add, direct-children, and delete operations and their supporting UKS paths. Other UKS operations, raw collection access, persistence, background processing, and the desktop application have not been adapted or audited for multi-user use.

## Service interface

`POST /api/statements`, with `Content-Type: application/json`:

```json
{"source":"Fido","relationshipType":"is-a","target":"dog"}
```

Calls the existing `UKS.AddStatement(string, string, string)` method, which resolves or creates the necessary Thoughts. The service explicitly calls `CreateInitialStructure()` at startup to create shared fundamental content. Returns the stored relationship's source, relationshipType, and target labels. Normal UKS relationship rules apply, including inverse normalization.

`GET /api/children?label=dog`:

```json
{"label":"dog","owner":0,"children":[{"label":"Fido","owner":1}],"relationships":[{"source":"dog","relationshipType":"has","target":"fur","owner":0}]}
```

Looks up the existing Thought and returns its direct children plus its directly stored outgoing relationships, filtered to shared and selected-user content. Children are sorted alphabetically; relationships are sorted by type and target. Missing Thoughts return HTTP 404; blank fields return HTTP 400. Only explicit response data is serialized, never the graph objects. Requests to these operations are serialized with a lock around UKS access.

`DELETE /api/thoughts?label=Fido` calls the existing `Thought.Delete()` method and returns `{"label":"Fido"}`. A missing Thought returns HTTP 404; a blank label returns HTTP 400. This deletes the Thought itself using the DLL's existing relationship cleanup; it does not recursively delete its children.

For example, `GET /api/children?label=dog&user=1` lists children visible to user 1. Use the same `user` query parameter for POST and DELETE. Statement responses also include the relationship's owner.

This project uses the BrainSim Thought API: `Thought`, `Link : Thought`, `LinksTo`, and `LinksFrom`. Links inherit the same immutable owner field as other Thoughts. Owner is included in Thought equality so identical labels in separate workspaces remain distinct.

The `*_wpftmp.csproj` file in the desktop folder is a generated WPF build file. It is not referenced by this project or solution.

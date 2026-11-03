var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// The URL carries a short opaque session key; everyone with that URL has full access.
var thoughtLimit = builder.Configuration.GetValue<int>("ThoughtLimitPerUser", 1000);
if (thoughtLimit < 1) throw new InvalidOperationException("ThoughtLimitPerUser must be a positive integer.");
var idleMinutes = builder.Configuration.GetValue<double>("SandboxIdleMinutes", 30);
if (!double.IsFinite(idleMinutes) || idleMinutes <= 0 || idleMinutes > TimeSpan.MaxValue.TotalMinutes)
    throw new InvalidOperationException("SandboxIdleMinutes must be a positive, finite duration.");
var idleTimeout = TimeSpan.FromMinutes(idleMinutes);
var sandboxes = new System.Collections.Concurrent.ConcurrentDictionary<string,
    (UKS.UKS Store, object Sync, string? Demo,
     System.Runtime.CompilerServices.StrongBox<long> LastActivity,
     System.Runtime.CompilerServices.StrongBox<int> ActiveRequests,
     System.Runtime.CompilerServices.StrongBox<bool> HasChanges)>(StringComparer.Ordinal);
var demoDirectory = builder.Configuration["DemoDirectory"];
demoDirectory = string.IsNullOrWhiteSpace(demoDirectory) ? Path.Combine(AppContext.BaseDirectory, "Demos")
    : Path.GetFullPath(demoDirectory, app.Environment.ContentRootPath);
var demoFiles = new Dictionary<string, (string Path, string Id)>(StringComparer.OrdinalIgnoreCase);
var demoCatalog = new List<object>();
if (Directory.Exists(demoDirectory))
    foreach (var path in Directory.EnumerateFiles(demoDirectory, "*.xml").OrderBy(Path.GetFileName))
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var id = (name.StartsWith("Demo", StringComparison.OrdinalIgnoreCase) && name.Length > 4
            ? name[4..] : name).ToLowerInvariant();
        demoFiles.Add(id, (path, id));
        demoFiles.TryAdd(name, (path, id));
        demoFiles.TryAdd(Path.GetFileName(path), (path, id));
        demoCatalog.Add(new { id, label = name, file = Path.GetFileName(path) });
    }
var initialThoughts = new System.Runtime.CompilerServices.ConditionalWeakTable<UKS.Thought, object>();

// Links are Thoughts too, and not every Link is in AtomicThoughts. Walk their
// stored structure to count additions, excluding activity queues and query prototypes.
Func<UKS.UKS, HashSet<UKS.Thought>> storedThoughts = store =>
{
    var result = new HashSet<UKS.Thought>(store.AtomicThoughts, ReferenceEqualityComparer.Instance);
    var pending = new Queue<UKS.Thought>(result);
    while (pending.TryDequeue(out var thought))
    {
        foreach (var link in thought.LinksTo)
            if (result.Add(link)) pending.Enqueue(link);
        if (thought is not UKS.Link relationship) continue;
        foreach (var endpoint in new[] { relationship.From, relationship.LinkType, relationship.To })
            if (endpoint is not null && result.Add(endpoint)) pending.Enqueue(endpoint);
    }
    return result;
};
// Handles are globally unique, but every lookup also checks the containing UKS.
// Weak references let deleted content be collected. Separate sandbox locks can
// access this registry concurrently.
var graphIds = new System.Runtime.CompilerServices.ConditionalWeakTable<UKS.Thought, System.Runtime.CompilerServices.StrongBox<long>>();
var graphThoughts = new System.Collections.Concurrent.ConcurrentDictionary<long, WeakReference<UKS.Thought>>();
long nextGraphId = 0;
Func<UKS.Thought, long> graphId = thought =>
{
    var id = graphIds.GetValue(thought, _ =>
        new System.Runtime.CompilerServices.StrongBox<long>(Interlocked.Increment(ref nextGraphId))).Value;
    graphThoughts.TryAdd(id, new WeakReference<UKS.Thought>(thought));
    return id;
};
Func<UKS.UKS, UKS.Thought, bool> graphRegistered = (store, thought) =>
    ReferenceEquals(thought.Store, store) && (
    store.AtomicThoughts.Contains(thought) ||
    (!string.IsNullOrEmpty(thought.Label) && ReferenceEquals(store.Labeled(thought.Label), thought)) ||
    thought.LinksTo.Any(link => ReferenceEquals(link.From, thought)) ||
    thought.LinksFrom.Any(link => link.From?.LinksTo.Any(stored => ReferenceEquals(stored, link)) == true) ||
    (thought is UKS.Link storedLink && storedLink.From?.LinksTo.Any(l => ReferenceEquals(l, storedLink)) == true));
Func<UKS.Thought, bool, object> graphSnapshot = (thought, expand) =>
{
    var links = expand
        ? thought.LinksTo.Concat(thought.LinksFrom)
            .Where(link => link.From is not null && link.To is not null && link.LinkType is not null)
            .Distinct(ReferenceEqualityComparer.Instance).Cast<UKS.Link>().ToArray()
        : Array.Empty<UKS.Link>();
    var nodes = new HashSet<UKS.Thought>(ReferenceEqualityComparer.Instance) { thought };
    foreach (var link in links)
    {
        nodes.Add(link.From!);
        nodes.Add(link.To!);
    }
    var displayedLinks = links.Select(link => new
    {
        id = (object)graphId(link), source = graphId(link.From!), target = graphId(link.To!),
        type = link.LinkType!.Label, structural = false
    }).ToList();
    if (expand && thought is UKS.Link relationship)
    {
        var relationshipId = graphId(relationship);
        if (relationship.From is not null)
        {
            nodes.Add(relationship.From);
            displayedLinks.Add(new { id = (object)$"part:{relationshipId}:source", source = graphId(relationship.From), target = relationshipId,
                type = "source", structural = true });
        }
        if (relationship.LinkType is not null)
        {
            nodes.Add(relationship.LinkType);
            displayedLinks.Add(new { id = (object)$"part:{relationshipId}:type", source = graphId(relationship.LinkType), target = relationshipId,
                type = "relationship type", structural = true });
        }
        if (relationship.To is not null)
        {
            nodes.Add(relationship.To);
            displayedLinks.Add(new { id = (object)$"part:{relationshipId}:target", source = relationshipId, target = graphId(relationship.To),
                type = "target", structural = true });
        }
    }
    return new
    {
        root = graphId(thought),
        nodes = nodes.Select(t => new
        {
            id = graphId(t),
            label = !string.IsNullOrEmpty(t.Label) ? t.Label : t is UKS.Link l
                ? $"[{l.From?.Label} → {l.LinkType?.Label} → {l.To?.Label}]" : "(unnamed Thought)",
            isRelationship = t is UKS.Link
        }).ToArray(),
        links = displayedLinks.ToArray()
    };
};

// Expire idle stores without waiting behind a busy sandbox. Active request leases
// cover response delivery as well as graph work. The monotonic clock is unaffected
// by wall-clock adjustments; polling never moves LastActivity.
var stopping = app.Lifetime.ApplicationStopping;
var cleanupTask = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(idleTimeout.TotalSeconds / 2, 1, 60)));
    try
    {
        while (await timer.WaitForNextTickAsync(stopping))
        {
            var expiredStores = new HashSet<UKS.UKS>(ReferenceEqualityComparer.Instance);
            foreach (var pair in sandboxes)
            {
                var sandbox = pair.Value;
                if (!Monitor.TryEnter(sandbox.Sync)) continue;
                try
                {
                    if (sandbox.ActiveRequests.Value == 0 &&
                        Environment.TickCount64 - sandbox.LastActivity.Value >= idleTimeout.TotalMilliseconds &&
                        sandboxes.TryRemove(pair.Key, out _))
                        expiredStores.Add(sandbox.Store);
                }
                finally { Monitor.Exit(sandbox.Sync); }
            }
            // Drop display handles too, without keeping discarded graphs alive.
            foreach (var pair in graphThoughts)
                if (!pair.Value.TryGetTarget(out var thought) || expiredStores.Contains(thought.Store))
                    graphThoughts.TryRemove(pair.Key, out _);
            if (expiredStores.Count > 0)
                app.Logger.LogInformation("Discarded {Count} idle sandboxes", expiredStores.Count);
        }
    }
    catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            context.File.Name.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            context.Context.Response.Headers.CacheControl = "no-cache";
    }
});

// Static pages, the demo catalog, and explicit sandbox creation need no sandbox.
// Every other API operation resolves an existing session.
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api") ||
        context.Request.Path == "/api/demos" || context.Request.Path == "/api/sandboxes")
    {
        await next(context);
        return;
    }
    context.Response.Headers.CacheControl = "no-store";
    var id = context.Request.Query["session"].ToString();
    if (id.Length != 22 || !sandboxes.TryGetValue(id, out var sandbox))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = "This demo has expired or is unavailable after a server restart. Choose an XML file to start again.", code = "sandbox_unavailable" });
        return;
    }
    var isActivity = (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) ||
        context.Request.Headers["X-Sandbox-Poll"] != "1";
    bool expired;
    lock (sandbox.Sync)
    {
        expired = !sandboxes.ContainsKey(id) || (sandbox.ActiveRequests.Value == 0 &&
            Environment.TickCount64 - sandbox.LastActivity.Value >= idleTimeout.TotalMilliseconds);
        if (expired) sandboxes.TryRemove(id, out _);
        else
        {
            sandbox.ActiveRequests.Value++;
            if (isActivity) sandbox.LastActivity.Value = Environment.TickCount64;
        }
    }
    if (expired)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = "This demo has expired. Choose an XML file to start again.", code = "sandbox_unavailable" });
        return;
    }
    var previousUKS = UKS.UKS.theUKS;
    context.Items["session"] = id;
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Demo-Changed"] = Volatile.Read(ref sandbox.HasChanges.Value) ? "1" : "0";
        return Task.CompletedTask;
    });
    UKS.UKS.theUKS = sandbox.Store;
    try { await next(context); }
    finally
    {
        UKS.UKS.theUKS = previousUKS;
        lock (sandbox.Sync)
        {
            sandbox.ActiveRequests.Value--;
            // A long user operation gets the full idle allowance after finishing.
            if (isActivity) sandbox.LastActivity.Value = Environment.TickCount64;
        }
    }
});

app.MapGet("/api/demos", () => Results.Ok(demoCatalog));

app.MapPost("/api/sandboxes", (string? demo, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    (string Path, string Id) selectedDemo = default;
    if (!string.IsNullOrWhiteSpace(demo) && !demoFiles.TryGetValue(demo, out selectedDemo))
        return Results.NotFound(new { error = "That XML demo is not available. Choose a demo from the list." });
    var previousUKS = UKS.UKS.theUKS;
    try
    {
        var store = new UKS.UKS();
        if (selectedDemo.Path is not null)
        {
            if (!store.LoadUKSfromXMLFile(selectedDemo.Path))
                return Results.Problem("The XML demo could not be loaded.");
        }
        else store.CreateInitialStructure();
        foreach (var thought in storedThoughts(store)) initialThoughts.Add(thought, new object());
        var id = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (!sandboxes.TryAdd(id, (store, new object(), selectedDemo.Id,
            new System.Runtime.CompilerServices.StrongBox<long>(Environment.TickCount64),
            new System.Runtime.CompilerServices.StrongBox<int>(0),
            new System.Runtime.CompilerServices.StrongBox<bool>(false))))
            return Results.Problem("The demo could not be opened. Please try again.");
        return Results.Ok(new { id, demo = selectedDemo.Id, thoughtLimit, idleTimeoutMinutes = idleMinutes, hasChanges = false });
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Xml.XmlException)
    {
        app.Logger.LogError(error, "Could not initialize XML demo {Demo}", demo);
        return Results.Problem("The XML demo could not be loaded.");
    }
    finally { UKS.UKS.theUKS = previousUKS; }
});

app.MapGet("/api/sandbox", (HttpContext context) =>
{
    var id = (string)context.Items["session"]!;
    var sandbox = sandboxes[id];
    return Results.Ok(new { id, demo = sandbox.Demo, thoughtLimit, idleTimeoutMinutes = idleMinutes, hasChanges = Volatile.Read(ref sandbox.HasChanges.Value) });
});

app.MapPost("/api/statements", (StatementInput input, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(input.Source) ||
        string.IsNullOrWhiteSpace(input.RelationshipType) ||
        string.IsNullOrWhiteSpace(input.Target))
        return Results.BadRequest(new { error = "Enter a source, relationship type, and target." });

    var sandbox = sandboxes[(string)context.Items["session"]!];
    var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        // Preview the write in an in-memory copy. A rejected addition must
        // not weaken existing relationships or leave partly-created content.
        // This stays in the web host; the UKS library has no user/quota rules.
        var before = storedThoughts(uks);
        var preview = new UKS.UKS();
        try
        {
            var copies = new Dictionary<UKS.Thought, UKS.Thought>(ReferenceEqualityComparer.Instance);
            foreach (var thought in before)
            {
                UKS.Thought copy = thought is UKS.SeqElement ? new UKS.SeqElement(preview)
                    : thought is UKS.Link ? new UKS.Link(preview) : new UKS.Thought(preview);
                copies.Add(thought, copy);
                copy.Label = thought.Label;
                copy.Weight = thought.Weight;
                copy.maxWeight = thought.maxWeight;
                copy.isPlastic = thought.isPlastic;
                copy.V = thought.V;
                copy.LastFiredTime = thought.LastFiredTime;
                copy.TimeToLive = thought.TimeToLive;
            }
            foreach (var storedRelationship in before.OfType<UKS.Link>())
            {
                var copy = (UKS.Link)copies[storedRelationship];
                copy.From = storedRelationship.From is null ? null : copies[storedRelationship.From];
                copy.LinkType = storedRelationship.LinkType is null ? null : copies[storedRelationship.LinkType];
                copy.To = storedRelationship.To is null ? null : copies[storedRelationship.To];
                // A Relationship stored as an exemplar is not an asserted fact.
                if (storedRelationship.From?.LinksTo.Any(link => ReferenceEquals(link, storedRelationship)) == true)
                    UKS.UKS.WriteTheLink(copy);
            }
            preview.AtomicThoughts.UnionWith(uks.AtomicThoughts.Select(thought => copies[thought]));
            var previewInitial = new HashSet<UKS.Thought>(copies
                .Where(pair => initialThoughts.TryGetValue(pair.Key, out _)).Select(pair => pair.Value),
                ReferenceEqualityComparer.Instance);
            UKSWeb.StatementInputParser.Add(preview, input.Source, input.RelationshipType, input.Target);
            if (storedThoughts(preview).Count(thought => !previewInitial.Contains(thought)) > thoughtLimit)
                return Results.Json(new { error = $"This demo is limited to {thoughtLimit} added Thoughts, including relationships. Delete some content before adding more.", limit = thoughtLimit }, statusCode: 409);
        }
        catch (Exception error) when (error is FormatException or ArgumentException or System.Text.Json.JsonException)
        {
            return Results.BadRequest(new { error = error.Message });
        }
        finally { UKS.UKS.theUKS = uks; }

        var relationship = UKSWeb.StatementInputParser.Add(
            uks, input.Source, input.RelationshipType, input.Target);
        Volatile.Write(ref sandbox.HasChanges.Value, true);

        return Results.Ok(new
        {
            source = UKSWeb.StatementInputParser.Format(relationship.From),
            relationshipType = UKSWeb.StatementInputParser.Format(relationship.LinkType),
            target = UKSWeb.StatementInputParser.Format(relationship.To)
        });
    }
});

app.MapGet("/api/children", (string? label, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(label))
        return Results.BadRequest(new { error = "Enter a Thought label." });

    var sandbox = sandboxes[(string)context.Items["session"]!];
    var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        var thought = uks.Labeled(label.Trim());
        if (thought is null)
            return Results.NotFound(new { error = $"No Thought named '{label.Trim()}' was found." });

        var children = thought.Children.Distinct().OrderBy(child => child.Label, StringComparer.OrdinalIgnoreCase)
            .Select(child => new { label = child.Label }).ToArray();
        // Only stored outgoing relationships: no inherited attributes or query evaluation.
        var relationships = thought.LinksTo.Where(r => r.From == thought)
            .OrderBy(r => r.LinkType?.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.To?.Label, StringComparer.OrdinalIgnoreCase)
            .Select(r => new
            {
                id = graphId(r),
                source = thought.Label,
                relationshipType = r.LinkType?.Label,
                target = r.To?.Label
            }).ToArray();
        return Results.Ok(new { label = thought.Label, children, relationships });
    }
});

app.MapDelete("/api/relationships", (long? id, HttpContext context) =>
{
    if (id is null || id <= 0)
        return Results.BadRequest(new { error = "Select a relationship to delete." });

    var sandbox = sandboxes[(string)context.Items["session"]!];
    lock (sandbox.Sync)
    {
        if (!graphThoughts.TryGetValue(id.Value, out var reference) || !reference.TryGetTarget(out var thought) ||
            thought is not UKS.Link relationship || !ReferenceEquals(relationship.Store, sandbox.Store) ||
            relationship.From is null || !relationship.From.LinksTo.Any(link => ReferenceEquals(link, relationship)))
            return Results.NotFound(new { error = "That relationship is no longer available in this demo." });

        relationship.From.RemoveLink(relationship);
        graphThoughts.TryRemove(id.Value, out _);
        Volatile.Write(ref sandbox.HasChanges.Value, true);
        return Results.Ok(new { id = id.Value });
    }
});

app.MapDelete("/api/thoughts", (string? label, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(label))
        return Results.BadRequest(new { error = "Enter a Thought label." });

    var sandbox = sandboxes[(string)context.Items["session"]!];
    var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        var thought = uks.Labeled(label.Trim());
        if (thought is null)
            return Results.NotFound(new { error = $"No Thought named '{label.Trim()}' was found." });

        var deletedLabel = thought.Label;
        thought.Delete();
        Volatile.Write(ref sandbox.HasChanges.Value, true);
        return Results.Ok(new { label = deletedLabel });
    }
});

app.MapGet("/api/graph", (string? label, long? id, string? ids, bool? expand, HttpContext context) =>
{
    var sandbox = sandboxes[(string)context.Items["session"]!];
    var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        // The handle table never owns a Thought or keeps deleted content alive.
        foreach (var stale in graphThoughts.Where(pair => !pair.Value.TryGetTarget(out _)).Select(pair => pair.Key).ToArray())
            graphThoughts.TryRemove(stale, out _);

        if (ids is not null)
        {
            // Refresh only nodes the browser has already expanded, in bounded batches.
            var handles = ids.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (handles.Length == 0 || handles.Length > 256 || handles.Any(handle => !long.TryParse(handle, out var number) || number < 1))
                return Results.BadRequest(new { error = "Supply between 1 and 256 valid graph handles." });
            var snapshots = new List<object>();
            var missingIds = new List<long>();
            foreach (var handle in handles.Select(long.Parse).Distinct())
            {
                if (!graphThoughts.TryGetValue(handle, out var reference) || !reference.TryGetTarget(out var node) ||
                    !graphRegistered(uks, node))
                    missingIds.Add(handle);
                else snapshots.Add(graphSnapshot(node, true));
            }
            return Results.Ok(new { snapshots, missingIds });
        }

        UKS.Thought? thought = null;
        if (id.HasValue)
        {
            if (graphThoughts.TryGetValue(id.Value, out var reference)) reference.TryGetTarget(out thought);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(label))
                return Results.BadRequest(new { error = "Enter a root Thought label." });
            thought = uks.Labeled(label.Trim());
        }
        if (thought is null || !ReferenceEquals(thought.Store, uks))
            return Results.NotFound(new { error = "That Thought is not available in this demo." });

        if (!graphRegistered(uks, thought))
            return Results.NotFound(new { error = "That Thought has been deleted. Reload the root to refresh the graph." });

        return Results.Ok(graphSnapshot(thought, expand == true));
    }
});

await app.RunAsync();
await cleanupTask;

record StatementInput(string? Source, string? RelationshipType, string? Target);

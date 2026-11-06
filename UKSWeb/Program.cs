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
// One packet can describe several starting Thoughts. A VLU link, not a CLR
// subclass or label notation, identifies the start of a sequence to display.
Func<IReadOnlyList<UKS.Thought>, bool, bool, object> graphSnapshot = (starts, expand, sequenceValues) =>
{
    var roots = starts.Distinct(ReferenceEqualityComparer.Instance).Cast<UKS.Thought>().ToArray();
    var nodes = new HashSet<UKS.Thought>(roots, ReferenceEqualityComparer.Instance);
    var links = new HashSet<UKS.Link>(ReferenceEqualityComparer.Instance);
    foreach (var thought in roots)
        if (expand)
            foreach (var link in thought.LinksTo.Concat(thought.LinksFrom))
                if (link.From is not null && link.To is not null && link.LinkType is not null)
                    links.Add(link);
    foreach (var link in links) { nodes.Add(link.From!); nodes.Add(link.To!); }

    // A Relationship used as an endpoint is a visible Thought. Include its
    // actual components even when it was not manually expanded; otherwise an
    // annotation of a collapsed edge appears disconnected from that edge.
    var relationshipParts = new HashSet<UKS.Link>(ReferenceEqualityComparer.Instance);
    var pendingRelationships = new Queue<UKS.Link>(links.SelectMany(link => new[] { link.From, link.To }).OfType<UKS.Link>());
    if (expand) foreach (var relationship in roots.OfType<UKS.Link>()) pendingRelationships.Enqueue(relationship);
    while (pendingRelationships.TryDequeue(out var relationship))
    {
        if (!relationshipParts.Add(relationship)) continue;
        nodes.Add(relationship);
        foreach (var part in new[] { relationship.From, relationship.LinkType, relationship.To })
        {
            if (part is null) continue;
            nodes.Add(part);
            if (part is UKS.Link nested) pendingRelationships.Enqueue(nested);
        }
    }

    Func<UKS.Thought, string> labelOf = thought => !string.IsNullOrEmpty(thought.Label) ? thought.Label
        : thought is UKS.Link link ? $"[{link.From?.Label} → {link.LinkType?.Label} → {link.To?.Label}]" : "(unnamed Thought)";
    Func<UKS.Thought, UKS.Link?> valueLink = thought => thought.LinksTo.FirstOrDefault(link =>
        link.LinkType is not null && link.To is not null && ReferenceEquals(link.LinkType, thought.Store.Labeled("VLU")));
    var sequenceLinks = new HashSet<UKS.Link>(ReferenceEqualityComparer.Instance);
    var chains = new List<(List<UKS.Thought> Elements, UKS.Thought? CycleTo)>();
    foreach (var start in nodes.ToArray())
    {
        if (valueLink(start) is null) continue;
        var chain = new List<UKS.Thought>();
        var visited = new HashSet<UKS.Thought>(ReferenceEqualityComparer.Instance);
        UKS.Thought? current = start;
        while (current is not null && visited.Add(current))
        {
            chain.Add(current); nodes.Add(current);
            var next = current.LinksTo.FirstOrDefault(link => link.LinkType is not null && link.To is not null &&
                ReferenceEquals(link.LinkType, current.Store.Labeled("NXT")));
            if (next is null) { current = null; break; }
            links.Add(next); sequenceLinks.Add(next);
            current = next.To;
        }
        chains.Add((chain, current)); // A repeated Thought closes a cycle; never loop indefinitely.
    }
    // Several requested Thoughts may expose the same row or one of its suffixes.
    var occupied = new HashSet<UKS.Thought>(ReferenceEqualityComparer.Instance);
    chains = chains.OrderByDescending(chain => chain.Elements.Count).Where(chain =>
    {
        if (chain.Elements.Any(occupied.Contains)) return false;
        foreach (var cell in chain.Elements) occupied.Add(cell);
        return true;
    }).ToList();
    if (sequenceValues)
        foreach (var cell in chains.SelectMany(chain => chain.Elements))
            if (valueLink(cell) is UKS.Link value) { links.Add(value); nodes.Add(value.To!); }
    var displayedLinks = links.Select(link => new
    {
        id = (object)graphId(link), source = graphId(link.From!), target = graphId(link.To!),
        type = link.LinkType!.Label, structural = false, sequence = sequenceLinks.Contains(link)
    }).ToList();
    foreach (var relationship in relationshipParts)
    {
        var relationshipId = graphId(relationship);
        foreach (var part in new[] { (Thought: relationship.From, Role: "source"),
            (Thought: relationship.LinkType, Role: "relationship type"), (Thought: relationship.To, Role: "target") })
        {
            if (part.Thought is null) continue;
            nodes.Add(part.Thought);
            displayedLinks.Add(new { id = (object)$"part:{relationshipId}:{part.Role}",
                source = part.Role == "target" ? relationshipId : graphId(part.Thought),
                target = part.Role == "target" ? graphId(part.Thought) : relationshipId,
                type = part.Role, structural = true, sequence = false });
        }
    }
    return new Dictionary<string, object?>
    {
        ["root"] = roots.Length == 0 ? null : graphId(roots[0]),
        ["roots"] = roots.Select(graphId).ToArray(),
        ["nodes"] = nodes.Select(thought => new
        {
            id = graphId(thought), label = labelOf(thought), isRelationship = thought is UKS.Link,
            showSequenceCellLabel = sequenceValues && valueLink(thought) is not null,
            sequenceValue = valueLink(thought)?.To is UKS.Thought value
                ? new { id = graphId(value), label = labelOf(value), isRelationship = value is UKS.Link } : null
        }).ToArray(),
        ["links"] = displayedLinks.ToArray(),
        ["sequences"] = chains.Select(chain => new
        {
            id = graphId(chain.Elements[0]), elements = chain.Elements.Select(graphId).ToArray(),
            cycleTo = chain.CycleTo is null ? (long?)null : graphId(chain.CycleTo)
        }).ToArray()
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

Func<UKS.UKS, UKS.UKS> sequencePreview = store =>
{
    var preview = new UKS.UKS();
    var copies = new Dictionary<UKS.Thought, UKS.Thought>(ReferenceEqualityComparer.Instance);
    foreach (var thought in storedThoughts(store))
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
    foreach (var storedRelationship in copies.Keys.OfType<UKS.Link>())
    {
        var copy = (UKS.Link)copies[storedRelationship];
        copy.From = storedRelationship.From is null ? null : copies[storedRelationship.From];
        copy.LinkType = storedRelationship.LinkType is null ? null : copies[storedRelationship.LinkType];
        copy.To = storedRelationship.To is null ? null : copies[storedRelationship.To];
        // A Relationship stored as an exemplar is not an asserted fact.
        if (storedRelationship.From?.LinksTo.Any(link => ReferenceEquals(link, storedRelationship)) == true)
            UKS.UKS.WriteTheLink(copy);
    }
    preview.AtomicThoughts.UnionWith(store.AtomicThoughts.Select(thought => copies[thought]));
    return preview;
};
Func<string, string> sequenceType = kind => kind == "word" ? "spelled" : kind == "digits" ? "ordered" : "notes";
Func<UKS.UKS, string, string, string[], UKS.Thought> storeDemoSequence = (store, kind, label, values) =>
{
    var owner = store.GetOrAddThought(label, store.GetOrAddThought("DemoSequences." + kind))!;
    var type = store.GetOrAddThought(sequenceType(kind), "LinkType")!;
    var thoughts = values.Select(value => kind == "word" ? store.GetOrAddCharacter(value[0])
        : store.GetOrAddThought(kind == "music" ? "note:" + value : value)!).ToList();
    store.AddSequenceAndLink(owner, type, thoughts, allowCompression: false);
    return owner;
};
Func<UKS.Thought, List<UKS.Thought>> demoSequenceCells = first =>
{
    var cells = new List<UKS.Thought>(); var seen = new HashSet<UKS.Thought>(ReferenceEqualityComparer.Instance);
    for (UKS.Thought? cell = first; cell is not null && seen.Add(cell) && cells.Count < 256;
        cell = cell.LinksTo.FirstOrDefault(link => link.LinkType == cell.Store.Labeled("NXT"))?.To)
        cells.Add(cell);
    return cells;
};
Func<UKS.Thought, string, object> demoSequence = (owner, kind) =>
{
    var first = owner.LinksTo.First(link => link.LinkType == owner.Store.Labeled(sequenceType(kind))).To!;
    return new { id = graphId(owner), label = owner.Label, startId = graphId(first), kind,
        cells = demoSequenceCells(first).Select(cell => new {
            id = graphId(cell), value = cell.LinksTo.FirstOrDefault(link => link.LinkType == cell.Store.Labeled("VLU"))?.To?.Label,
            nextId = cell.LinksTo.FirstOrDefault(link => link.LinkType == cell.Store.Labeled("NXT"))?.To is UKS.Thought next ? (long?)graphId(next) : null
        }).ToArray() };
};
app.MapPost("/api/sequences", (System.Text.Json.JsonElement input, HttpContext context) =>
{
    if (input.ValueKind != System.Text.Json.JsonValueKind.Object) return Results.BadRequest(new { error = "Supply a sequence object." });
    string? kind = input.TryGetProperty("kind", out var kindJson) && kindJson.ValueKind == System.Text.Json.JsonValueKind.String ? kindJson.GetString() : null;
    string? label = input.TryGetProperty("label", out var labelJson) && labelJson.ValueKind == System.Text.Json.JsonValueKind.String ? labelJson.GetString()?.Trim() : null;
    if (kind is not ("word" or "digits" or "music") || string.IsNullOrEmpty(label) || label.Length > 80 ||
        !input.TryGetProperty("values", out var valuesJson) || valuesJson.ValueKind != System.Text.Json.JsonValueKind.Array)
        return Results.BadRequest(new { error = "Choose a sequence kind, name, and values." });
    if (valuesJson.GetArrayLength() is < 1 or > 32 || valuesJson.EnumerateArray().Any(value => value.ValueKind != System.Text.Json.JsonValueKind.String))
        return Results.BadRequest(new { error = "Use between one and 32 sequence values." });
    var values = valuesJson.EnumerateArray().Select(value => value.GetString()!).ToArray();
    if (values.Any(value => kind == "word" ? value.Length != 1 || !char.IsAsciiLetter(value[0])
        : kind == "digits" ? value.Length != 1 || value[0] < '1' || value[0] > '9'
        : !new[] { "C4", "D4", "E4", "F4", "G4", "A4", "B4", "C5" }.Contains(value)))
        return Results.BadRequest(new { error = "Select letters, digits 1–9, or notes C4–C5." });
    var sandbox = sandboxes[(string)context.Items["session"]!]; var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        var existing = uks.Labeled(label);
        if (existing is not null)
        {
            var link = existing.LinksTo.FirstOrDefault(link => link.LinkType == uks.Labeled(sequenceType(kind)));
            var expected = values.Select(value => kind == "word" ? UKS.UKS.GetCharacterLabel(value[0]) : kind == "music" ? "note:" + value : value);
            if (link?.To is not null && demoSequenceCells(link.To).Select(cell => cell.LinksTo.FirstOrDefault(edge => edge.LinkType == uks.Labeled("VLU"))?.To?.Label).SequenceEqual(expected))
                return Results.Ok(demoSequence(existing, kind));
            return Results.Conflict(new { error = "That name already has different content. Choose another name." });
        }
        var previous = UKS.UKS.theUKS;
        try
        {
            var preview = sequencePreview(uks);
            storeDemoSequence(preview, kind, label, values);
            var baseline = storedThoughts(uks).Count(thought => initialThoughts.TryGetValue(thought, out _));
            if (storedThoughts(preview).Count - baseline > thoughtLimit)
                return Results.Json(new { error = $"This demo is limited to {thoughtLimit} added Thoughts, including relationships." }, statusCode: 409);
        }
        finally { UKS.UKS.theUKS = previous; }
        var owner = storeDemoSequence(uks, kind, label, values);
        Volatile.Write(ref sandbox.HasChanges.Value, true);
        return Results.Ok(demoSequence(owner, kind));
    }
});
app.MapGet("/api/sequences", (string? kind, HttpContext context) =>
{
    if (kind is not ("word" or "digits" or "music")) return Results.BadRequest(new { error = "Choose word, digits, or music." });
    var sandbox = sandboxes[(string)context.Items["session"]!]; var uks = sandbox.Store;
    lock (sandbox.Sync) return Results.Ok(new { sequences = uks.Labeled("DemoSequences." + kind)?.Children
        .Where(owner => owner.LinksTo.Any(link => link.LinkType == uks.Labeled(sequenceType(kind)) && link.To is not null))
        .Select(owner => demoSequence(owner, kind)).ToArray() ?? Array.Empty<object>() });
});
app.MapGet("/api/sequence-matches", (string? kind, string? values, HttpContext context) =>
{
    if (kind is not ("word" or "digits" or "music")) return Results.BadRequest(new { error = "Choose word, digits, or music." });
    string[] pattern;
    try { pattern = System.Text.Json.JsonSerializer.Deserialize<string[]>(values ?? "[]") ?? Array.Empty<string>(); }
    catch (System.Text.Json.JsonException) { return Results.BadRequest(new { error = "Supply a list of sequence values." }); }
    if (pattern.Length is < 1 or > 32 || pattern.Any(value => string.IsNullOrEmpty(value) || value.Length > 16))
        return Results.BadRequest(new { error = "Choose between one and 32 values." });
    if (pattern.Any(value => kind == "word" ? value.Length != 1 || !char.IsAsciiLetter(value[0])
        : kind == "digits" ? value.Length != 1 || value[0] < '1' || value[0] > '9'
        : !new[] { "C4", "D4", "E4", "F4", "G4", "A4", "B4", "C5" }.Contains(value)))
        return Results.BadRequest(new { error = "Use letters, digits 1–9, or notes C4–C5." });
    var sandbox = sandboxes[(string)context.Items["session"]!]; var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        var thoughts = pattern.Select(value => uks.Labeled(kind == "word" ? UKS.UKS.GetCharacterLabel(value[0]) : kind == "music" ? "note:" + value : value)).ToList();
        if (thoughts.Any(thought => thought is null)) return Results.Ok(new { matches = Array.Empty<object>() });
        var options = new UKS.Thought(uks);
        try
        {
            var matches = uks.FindSequencesByActivation(thoughts!, options, getFirstElements:false);
            var owners = uks.Labeled("DemoSequences." + kind)?.Children ?? new List<UKS.Thought>();
            var rows = new List<object>();
            foreach (var owner in owners)
            {
                var first = owner.LinksTo.FirstOrDefault(link => link.LinkType == uks.Labeled(sequenceType(kind)))?.To;
                if (first is null) continue;
                var cells = demoSequenceCells(first);
                foreach (var match in matches)
                {
                    int offset = cells.FindIndex(cell => ReferenceEquals(cell, match.seqNode));
                    if (offset < 0 || offset + pattern.Length > cells.Count) continue;
                    var labels = cells.Select(cell => cell.LinksTo.First(link => link.LinkType == uks.Labeled("VLU")).To!.Label).ToArray();
                    rows.Add(new { sequence = demoSequence(owner,kind), offset, matched = pattern.Length, total = cells.Count,
                        full = offset == 0 && pattern.Length == cells.Count, confidence = match.confidence,
                        continuation = labels.Skip(offset + pattern.Length).ToArray() });
                }
            }
            return Results.Ok(new { matches = rows });
        }
        finally { options.Delete(); }
    }
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
            id = graphId(relationship),
            targetId = graphId(relationship.To!),
            source = UKSWeb.StatementInputParser.Format(relationship.From),
            sourceLabel = relationship.From?.Label,
            sourceId = graphId(relationship.From!),
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

// UKS returns the winning category/depth, not a full path. Reconstruct one
// stored supporting route; never claim this is the search engine's execution trace.
Func<UKS.Thought, UKS.Link, object> attributeSnapshot = (source, result) =>
{
    var origin = result.InheritedFromCategory ?? source;
    var paths = new Dictionary<UKS.Thought, List<UKS.Link>>(ReferenceEqualityComparer.Instance) { [source] = new() };
    var pending = new Queue<UKS.Thought>(); pending.Enqueue(source);
    while (pending.TryDequeue(out var current) && !paths.ContainsKey(origin))
    {
        if (paths[current].Count >= result.InheritanceDepth) continue;
        foreach (var edge in current.LinksTo)
        {
            if (edge.To is null || edge.LinkType?.HasProperty("inheritable") != true || paths.ContainsKey(edge.To)) continue;
            paths[edge.To] = new List<UKS.Link>(paths[current]) { edge };
            pending.Enqueue(edge.To);
        }
    }
    var stored = origin.LinksTo.FirstOrDefault(edge => edge.LinkType == result.LinkType && edge.To == result.To);
    var evidence = stored is not null && paths.TryGetValue(origin, out var route)
        ? route.Append(stored).ToArray() : Array.Empty<UKS.Link>();
    return new
    {
        source = source.Label, relationshipType = result.LinkType?.Label, target = result.To?.Label,
        inheritedFrom = result.InheritedFromCategory?.Label, depth = result.InheritanceDepth, weight = result.Weight,
        evidenceLinkIds = evidence.Select(graphId).ToArray(),
        evidenceThoughtIds = evidence.SelectMany(edge => new[] { edge.From!, edge.To! }).Distinct().Select(graphId).ToArray()
    };
};

// Query results are values, not stored Relationship identities. Never register
// GetAttributes' temporary Links as graph handles; the graph shows stored facts.
app.MapGet("/api/attributes", (string? label, HttpContext context) =>
{
    var sandbox = sandboxes[(string)context.Items["session"]!];
    var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        if (string.IsNullOrWhiteSpace(label)) return Results.BadRequest(new { error = "Choose a Thought." });
        var thought = uks.Labeled(label.Trim());
        if (thought is null) return Results.NotFound(new { error = "That Thought was not found." });
        return Results.Ok(new { label = thought.Label, id = graphId(thought), attributes = uks.GetAttributes(thought)
            .Where(link => link.LinkType is not null && link.To is not null).Select(link => attributeSnapshot(thought, link)).ToArray() });
    }
});

app.MapGet("/api/candidates", (string? root, HttpContext context) =>
{
    var sandbox = sandboxes[(string)context.Items["session"]!];
    var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        var types = context.Request.Query["types"];
        var targets = context.Request.Query["targets"];
        if (types.Count == 0 || types.Count > 16 || types.Count != targets.Count)
            return Results.BadRequest(new { error = "Choose between one and sixteen attributes." });
        var scope = string.IsNullOrWhiteSpace(root) ? null : uks.Labeled(root.Trim());
        if (scope is null) return Results.NotFound(new { error = "Choose an existing search root." });
        var pairs = new List<(UKS.Thought Type, UKS.Thought Target)>();
        for (int index = 0; index < types.Count; index++)
        {
            var type = uks.Labeled(types[index] ?? "");
            var target = uks.Labeled(targets[index] ?? "");
            // An offered attribute may not have been asserted yet (e.g. three legs
            // before adding Tripper's exception). It contributes no matches or weight.
            if (type is null || target is null) continue;
            pairs.Add((type, target));
        }
        // An unlabeled temporary Thought supplies the existing desktop search
        // API's pattern. Always detach it, including on query failure.
        var pattern = new UKS.Thought(uks);
        try
        {
            // Match ModuleUKSQueryDlg.CreateTheQueryThought's default query-link weight.
            foreach (var pair in pairs.Distinct()) pattern.AddLink(pair.Type, pair.Target)!.Weight = .9f;
            var candidates = uks.SearchByAttributes(pattern, scope).Where(item => item.conf > 0).Take(100).ToArray();
            return Results.Ok(new { root = scope.Label, candidates = candidates.Select(item =>
            {
                var effective = uks.GetAttributes(item.t);
                return new { id = graphId(item.t), label = item.t.Label, weight = item.conf,
                    matched = pairs.Count(pair => effective.Any(link => link.LinkType?.HasAncestor(pair.Type) == true && link.To == pair.Target)),
                    total = types.Count, attributes = effective.Where(link => pairs.Any(pair =>
                        link.LinkType?.HasAncestor(pair.Type) == true && link.To == pair.Target))
                        .Select(link => attributeSnapshot(item.t, link)).ToArray() };
            }).ToArray() });
        }
        finally { pattern.Delete(); }
    }
});

app.MapGet("/api/graph", (string? label, long? id, string? ids, string? roots, bool? expand, bool? sequenceValues, HttpContext context) =>
{
    var sandbox = sandboxes[(string)context.Items["session"]!];
    var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        // The handle table never owns a Thought or keeps deleted content alive.
        foreach (var stale in graphThoughts.Where(pair => !pair.Value.TryGetTarget(out _)).Select(pair => pair.Key).ToArray())
            graphThoughts.TryRemove(stale, out _);

        var names = context.Request.Query["labels"];
        if (roots is not null || names.Count > 0)
        {
            var selected = new List<UKS.Thought>();
            var missingIds = new List<long>();
            var missingLabels = new List<string>();
            if (roots is not null)
            {
                var handles = roots.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (handles.Length > 256 || handles.Any(handle => !long.TryParse(handle, out var number) || number < 1))
                    return Results.BadRequest(new { error = "Supply at most 256 valid graph handles." });
                foreach (var handle in handles.Select(long.Parse).Distinct())
                    if (graphThoughts.TryGetValue(handle, out var reference) && reference.TryGetTarget(out var selectedThought) && graphRegistered(uks, selectedThought)) selected.Add(selectedThought);
                    else missingIds.Add(handle);
            }
            else
            {
                if (names.Count > 256) return Results.BadRequest(new { error = "Supply at most 256 Thought labels." });
                foreach (var name in names)
                    if (!string.IsNullOrWhiteSpace(name) && uks.Labeled(name.Trim()) is UKS.Thought namedThought && graphRegistered(uks, namedThought)) selected.Add(namedThought);
                    else missingLabels.Add(name ?? "");
            }
            var packet = (Dictionary<string, object?>)graphSnapshot(selected, expand == true, sequenceValues == true);
            packet["missingIds"] = missingIds;
            packet["missingLabels"] = missingLabels;
            return Results.Ok(packet);
        }
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
                else snapshots.Add(graphSnapshot(new[] { node }, true, sequenceValues == true));
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

        return Results.Ok(graphSnapshot(new[] { thought }, expand == true, sequenceValues == true));
    }
});

await app.RunAsync();
await cleanupTask;

record StatementInput(string? Source, string? RelationshipType, string? Target);

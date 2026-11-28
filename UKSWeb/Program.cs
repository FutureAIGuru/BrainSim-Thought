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
// The quota counts Thoughts and links added since the session's UKS was created.
var initialSizes = new System.Runtime.CompilerServices.ConditionalWeakTable<UKS.UKS, System.Runtime.CompilerServices.StrongBox<int>>();
int storeSize(UKS.UKS store) => store.ThoughtCount() + store.LinkCount() + store.SequenceElementCount();
int addedSoFar(UKS.UKS store) => storeSize(store) - (initialSizes.TryGetValue(store, out var initial) ? initial.Value : 0);
// A graph ID is the stored label within the selected UKS. A node's caption
// can render the Relationship's structure without changing that lookup key.
string graphId(UKS.Thought thought) => thought.Label;
// One packet can describe several starting Thoughts. A VLU link, not a CLR
// subclass or label notation, identifies the start of a sequence to display.
Func<IReadOnlyList<UKS.Thought>, bool, bool, object> graphSnapshot = (starts, expand, sequenceValues) =>
{
    var roots = starts.Distinct(ReferenceEqualityComparer.Instance).Cast<UKS.Thought>().ToArray();
    var nodes = new HashSet<UKS.Thought>(roots, ReferenceEqualityComparer.Instance);
    var links = new HashSet<UKS.Link>(ReferenceEqualityComparer.Instance);
    bool isArchivedEvent(UKS.Thought? value) =>
        value is UKS.Link archived && (archived.LinkType?.HasProperty("isHistorical") == true ||
            archived.GetTargetOfFirstLinkOfType("validUntil") is not null);
    foreach (var thought in roots)
        if (expand)
            foreach (var link in thought.LinksTo.Concat(thought.LinksFrom))
                if (link.From is not null && link.To is not null && link.LinkType is not null &&
                    !(link.From is UKS.SeqElement && link.LinkType == thought.Store.Labeled("validUntil")))
                    links.Add(link);
    foreach (var link in links) { nodes.Add(link.From!); nodes.Add(link.To!); }

    // A Relationship used as an endpoint is a visible Thought. Include its
    // actual components even when it was not manually expanded; otherwise an
    // annotation of a collapsed edge appears disconnected from that edge.
    var relationshipParts = new HashSet<UKS.Link>(ReferenceEqualityComparer.Instance);
    // History values appear as labeled Relationship nodes with VLU connectors.
    var pendingRelationships = new Queue<UKS.Link>(links.SelectMany(link => new[] { link.From, link.To })
        .OfType<UKS.Link>().Where(relationship => !isArchivedEvent(relationship)));
    if (expand) foreach (var relationship in roots.OfType<UKS.Link>()) pendingRelationships.Enqueue(relationship);
    while (pendingRelationships.TryDequeue(out var relationship))
    {
        if (!relationshipParts.Add(relationship)) continue;
        nodes.Add(relationship);
        foreach (var part in new[] { relationship.From, relationship.LinkType, relationship.To })
        {
            if (part is null) continue;
            nodes.Add(part);
            if (part is UKS.Link nested && !isArchivedEvent(nested)) pendingRelationships.Enqueue(nested);
        }
    }

    Func<UKS.Thought, string> labelOf = thought => thought is UKS.Link && (thought.HasGeneratedLabel || isArchivedEvent(thought))
        ? UKSWeb.StatementInputParser.Format(thought)
        : !string.IsNullOrEmpty(thought.Label) ? thought.Label
        : thought is UKS.Link link ? $"[{link.From?.Label} → {link.LinkType?.Label} → {link.To?.Label}]" : "(unnamed Thought)";
    Func<UKS.Thought, UKS.Link?> valueLink = thought => thought.LinksTo.FirstOrDefault(link =>
        link.LinkType is not null && link.To is not null && ReferenceEquals(link.LinkType, thought.Store.Labeled("VLU")) && !link.To.HasProperty("isSequenceMetadata"));
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
            foreach (var value in cell.LinksTo.Where(link => link.LinkType == cell.Store.Labeled("VLU") && link.To is not null))
            { links.Add(value); nodes.Add(value.To!); }
    var displayedLinks = links.Select(link => new
    {
        id = graphId(link), source = graphId(link.From!), target = graphId(link.To!),
        type = link.LinkType!.Label, structural = false, sequence = sequenceLinks.Contains(link),
        weight = float.IsFinite(link.Weight) ? (float?)link.Weight : null
    }).ToList();
    foreach (var relationship in relationshipParts)
    {
        var relationshipId = graphId(relationship);
        foreach (var part in new[] { (Thought: relationship.From, Role: "source"),
            (Thought: relationship.LinkType, Role: "relationship type"), (Thought: relationship.To, Role: "target") })
        {
            if (part.Thought is null) continue;
            nodes.Add(part.Thought);
            displayedLinks.Add(new { id = $"\0part:{relationshipId}:{part.Role}",
                source = part.Role == "target" ? relationshipId : graphId(part.Thought),
                target = part.Role == "target" ? graphId(part.Thought) : relationshipId,
                type = part.Role, structural = true, sequence = false, weight = (float?)null });
        }
    }
    return new Dictionary<string, object?>
    {
        ["root"] = roots.Length == 0 ? null : graphId(roots[0]),
        ["roots"] = roots.Select(graphId).ToArray(),
        ["nodes"] = nodes.Select(thought =>
        {
            var value = valueLink(thought)?.To;
            return new
            {
                id = graphId(thought), label = labelOf(thought), isRelationship = thought is UKS.Link,
                isEvent = isArchivedEvent(thought), isEventCell = isArchivedEvent(value),
                weight = thought is UKS.Link relationship && float.IsFinite(relationship.Weight) ? (float?)relationship.Weight : null,
                showSequenceCellLabel = sequenceValues && value is not null,
                sequenceValues = thought.LinksTo.Where(link => link.LinkType == thought.Store.Labeled("VLU") && link.To is not null)
                    .Select(link => new { id = graphId(link.To!), label = labelOf(link.To!), metadata = link.To!.HasProperty("isSequenceMetadata") }).ToArray(),
                sequenceValue = value is not null
                    ? new { id = graphId(value), label = labelOf(value), isRelationship = value is UKS.Link } : null
            };
        }).ToArray(),
        ["links"] = displayedLinks.ToArray(),
        ["sequences"] = chains.Select(chain => new
        {
            id = graphId(chain.Elements[0]), elements = chain.Elements.Select(graphId).ToArray(),
            cycleTo = chain.CycleTo is null ? null : graphId(chain.CycleTo)
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
            int expiredCount = 0;
            foreach (var pair in sandboxes)
            {
                var sandbox = pair.Value;
                if (!Monitor.TryEnter(sandbox.Sync)) continue;
                try
                {
                    if (sandbox.ActiveRequests.Value == 0 &&
                        Environment.TickCount64 - sandbox.LastActivity.Value >= idleTimeout.TotalMilliseconds &&
                        sandboxes.TryRemove(pair.Key, out _))
                        expiredCount++;
                }
                finally { Monitor.Exit(sandbox.Sync); }
            }
            if (expiredCount > 0)
                app.Logger.LogInformation("Discarded {Count} idle sandboxes", expiredCount);
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
    context.Items["session"] = id;
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Demo-Changed"] = Volatile.Read(ref sandbox.HasChanges.Value) ? "1" : "0";
        return Task.CompletedTask;
    });
    try { await next(context); }
    finally
    {
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
    try
    {
        var store = new UKS.UKS();
        if (selectedDemo.Path is not null)
        {
            if (!store.LoadUKSfromXMLFile(selectedDemo.Path))
                return Results.Problem("The XML demo could not be loaded.");
        }
        else store.CreateInitialStructure();
        initialSizes.Add(store, new System.Runtime.CompilerServices.StrongBox<int>(storeSize(store)));
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
});

app.MapGet("/api/sandbox", (HttpContext context) =>
{
    var id = (string)context.Items["session"]!;
    var sandbox = sandboxes[id];
    return Results.Ok(new { id, demo = sandbox.Demo, thoughtLimit, idleTimeoutMinutes = idleMinutes, hasChanges = Volatile.Read(ref sandbox.HasChanges.Value) });
});

// Import previews the merge and quota on a copy.
app.MapGet("/api/text-export", (string? root, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(root)) return Results.BadRequest(new { error = "Select a root Thought to save." });
    var sandbox = sandboxes[(string)context.Items["session"]!];
    var path = Path.GetTempFileName();
    try
    {
        lock (sandbox.Sync)
        {
            if (sandbox.Store.Labeled(root) is null) return Results.NotFound(new { error = "That root Thought does not exist." });
            sandbox.Store.ExportTextFile(root, path);
        }
        var content = File.ReadAllText(path);
        var filename = string.Concat(root.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        return Results.Ok(new { filename = filename + ".txt", content });
    }
    finally { File.Delete(path); }
});
app.MapPost("/api/text-import", async (HttpContext context) =>
{
    const int limit = 1024 * 1024;
    var size = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
    if (size is not null && !size.IsReadOnly) size.MaxRequestBodySize = 2 * limit;
    System.Text.Json.JsonElement input;
    try { input = await System.Text.Json.JsonSerializer.DeserializeAsync<System.Text.Json.JsonElement>(context.Request.Body); }
    catch (System.Text.Json.JsonException) { return Results.BadRequest(new { error = "Choose a UKS text file." }); }
    if (input.ValueKind != System.Text.Json.JsonValueKind.Object || !input.TryGetProperty("content", out var contentJson) || contentJson.ValueKind != System.Text.Json.JsonValueKind.String)
        return Results.BadRequest(new { error = "Choose a UKS text file." });
    var content = contentJson.GetString()!.TrimStart('\uFEFF');
    if (string.IsNullOrWhiteSpace(content) || System.Text.Encoding.UTF8.GetByteCount(content) > limit)
        return Results.BadRequest(new { error = "Choose a nonempty text file no larger than 1 MB." });
    // Bound nesting before invoking the recursive desktop parser. Quoted labels
    // and comments may legitimately contain brackets and must not count.
    int depth = 0; bool quoted = false, escaped = false;
    foreach (var line in content.Split('\n'))
    {
        if (line.Length > 8192) return Results.BadRequest(new { error = "A text-file line is too long." });
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (escaped) { escaped = false; continue; }
            if (quoted && character == '\\') { escaped = true; continue; }
            if (character == '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            if (character == '/' && index + 1 < line.Length && line[index + 1] == '/') { break; }
            if (character == '[' && ++depth > 64) return Results.BadRequest(new { error = "The text file is nested too deeply." });
            if (character == ']' && --depth < 0) return Results.BadRequest(new { error = "The text file has unmatched brackets." });
        }
    }
    if (quoted || depth != 0) return Results.BadRequest(new { error = "The text file has unmatched quotes or brackets." });
    var path = Path.GetTempFileName();
    try
    {
        await File.WriteAllTextAsync(path, content);
        var sandbox = sandboxes[(string)context.Items["session"]!]; var uks = sandbox.Store;
        lock (sandbox.Sync)
        {
            int before = storeSize(uks);
            try
            {
                var preview = uks.Copy(); int previewBefore = storeSize(preview);
                preview.ImportTextFile(path);
                if (addedSoFar(uks) + storeSize(preview) - previewBefore > thoughtLimit)
                    return Results.Json(new { error = $"This import would exceed the limit of {thoughtLimit} added Thoughts, including relationships." }, statusCode: 409);
                uks.ImportTextFile(path);
            }
            catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException or IndexOutOfRangeException)
            { return Results.BadRequest(new { error = "The text file could not be imported: " + error.Message }); }
            Volatile.Write(ref sandbox.HasChanges.Value, true);
            return Results.Ok(new { added = storeSize(uks) - before });
        }
    }
    finally { File.Delete(path); }
});
// Sequence owners belong to ordinary knowledge categories, not demo-only groups.
Func<string, string> sequenceCategory = kind => kind == "word" ? "word" : kind == "colors" ? "color-sequence" : "melody";
Func<string, string> sequenceType = kind => kind == "word" ? "spelled" : kind == "colors" ? "ordered" : "notes";
Func<UKS.UKS, string, string, string[], int[], UKS.Thought> storeDemoSequence = (store, kind, label, values, delays) =>
{
    var owner = store.GetOrAddThought(label, store.GetOrAddThought(sequenceCategory(kind), "Abstract"))!;
    var type = store.GetOrAddThought(sequenceType(kind), "LinkType")!;
    var thoughts = values.Select(value => kind == "word" ? store.GetOrAddCharacter(value[0])
        : store.GetOrAddThought(value, kind == "music" ? store.GetOrAddThought("note", "Abstract") : null)!).ToList();
    if (kind is "colors" or "music")
    {
        // Keep remembered examples independent of the disposable live-input cells.
        // AddSequenceAndLink may reuse an exact sequence, including the live input.
        var first = store.CreateFirstElement(owner.Label, thoughts[0]);
        var previous = first;
        foreach (var value in thoughts.Skip(1)) previous = store.AddElement(previous, value);
        owner.AddLink(type, first);
        var duration = store.GetOrAddThought("duration", "Abstract")!;
        duration.AddLink(store.Labeled("hasProperty")!, store.GetOrAddThought("isSequenceMetadata", "Property"));
        var cell = first;
        for (int index = 0; cell is not null; index++, cell = cell.NXT)
        {
            int units = delays[index];
            var delay = store.GetOrAddThought("delay:" + units, duration)!; delay.V = units;
            cell.AddLink(store.Labeled("VLU")!, delay);
        }
    }
    else store.AddSequenceAndLink(owner, type, thoughts, allowCompression: false);
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
            id = graphId(cell), label = cell.Label, value = cell.LinksTo.FirstOrDefault(link => link.LinkType == cell.Store.Labeled("VLU") && link.To?.HasProperty("isSequenceMetadata") != true)?.To?.Label,
            delay = cell.LinksTo.FirstOrDefault(link => link.LinkType == cell.Store.Labeled("VLU") && link.To?.HasAncestor(cell.Store.Labeled("duration")) == true)?.To?.V is int units ? units : 1,
            nextId = cell.LinksTo.FirstOrDefault(link => link.LinkType == cell.Store.Labeled("NXT"))?.To?.Label
        }).ToArray() };
};
// Live input uses private, ordinary Thoughts with VLU/NXT links. It is not a
// remembered example, so it cannot match itself. Replacing input deletes only
// these private cells, never the shared color Thoughts or remembered sequences.
var sequenceInputs = new System.Runtime.CompilerServices.ConditionalWeakTable<UKS.UKS, List<UKS.Thought>>();
app.MapPost("/api/sequence-input", (System.Text.Json.JsonElement input, HttpContext context) =>
{
    if (input.ValueKind != System.Text.Json.JsonValueKind.Object ||
        !input.TryGetProperty("values", out var valuesJson) || valuesJson.ValueKind != System.Text.Json.JsonValueKind.Array ||
        valuesJson.GetArrayLength() > 32 || valuesJson.EnumerateArray().Any(value => value.ValueKind != System.Text.Json.JsonValueKind.String))
        return Results.BadRequest(new { error = "Choose up to 32 colors." });
    var values = valuesJson.EnumerateArray().Select(value => value.GetString()!).ToArray();
    if (values.Any(value => !new[] { "red", "orange", "yellow", "green", "blue", "purple", "brown", "pink", "white" }.Contains(value)))
        return Results.BadRequest(new { error = "Choose a color from the squares." });
    var sandbox = sandboxes[(string)context.Items["session"]!]; var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        var colors = values.Select(value => uks.Labeled(value)).ToArray();
        if (colors.Any(color => color is null))
            return Results.BadRequest(new { error = "This UKS does not contain that color." });
        var previous = sequenceInputs.GetOrCreateValue(uks);
        // Keep handles stable when restoring a page or toggling VLU visibility.
        if (previous.Select(cell => cell.LinksTo.FirstOrDefault(link => link.LinkType == uks.Labeled("VLU"))?.To?.Label).SequenceEqual(values))
            return Results.Ok(new { startId = previous.Count == 0 ? null : graphId(previous[0]) });
        var cells = new List<UKS.Thought>();
        foreach (var color in colors)
        {
            var cell = new UKS.Thought(uks); // unlabeled: a private pattern, not stored knowledge
            cell.AddLink(uks.Labeled("VLU")!, color);
            if (cells.Count > 0) cells[^1].AddLink(uks.Labeled("NXT")!, cell);
            cells.Add(cell);
        }
        // Count the replacement (the previous input will be removed).
        if (addedSoFar(uks) + cells.Count + cells.Sum(cell => cell.LinksTo.Count) > thoughtLimit)
        {
            foreach (var cell in cells) cell.Delete();
            return Results.Json(new { error = $"This demo is limited to {thoughtLimit} added Thoughts, including relationships." }, statusCode: 409);
        }
        foreach (var cell in previous) cell.Delete();
        previous.Clear(); previous.AddRange(cells);
        return Results.Ok(new { startId = cells.Count == 0 ? null : graphId(cells[0]) });
    }
});
app.MapPost("/api/sequences", (System.Text.Json.JsonElement input, HttpContext context) =>
{
    if (input.ValueKind != System.Text.Json.JsonValueKind.Object) return Results.BadRequest(new { error = "Supply a sequence object." });
    string? kind = input.TryGetProperty("kind", out var kindJson) && kindJson.ValueKind == System.Text.Json.JsonValueKind.String ? kindJson.GetString() : null;
    string? label = input.TryGetProperty("label", out var labelJson) && labelJson.ValueKind == System.Text.Json.JsonValueKind.String ? labelJson.GetString()?.Trim() : null;
    if (kind is not ("word" or "colors" or "music") || string.IsNullOrEmpty(label) || label.Length > 80 ||
        !input.TryGetProperty("values", out var valuesJson) || valuesJson.ValueKind != System.Text.Json.JsonValueKind.Array)
        return Results.BadRequest(new { error = "Choose a sequence kind, name, and values." });
    if (valuesJson.GetArrayLength() is < 1 or > 32 || valuesJson.EnumerateArray().Any(value => value.ValueKind != System.Text.Json.JsonValueKind.String))
        return Results.BadRequest(new { error = "Use between one and 32 sequence values." });
    var values = valuesJson.EnumerateArray().Select(value => value.GetString()!).ToArray();
    if (values.Any(value => kind == "word" ? value.Length != 1 || !char.IsAsciiLetter(value[0])
        : kind == "colors" ? !new[] { "red", "orange", "yellow", "green", "blue", "purple", "brown", "pink", "white" }.Contains(value)
        : !new[] { "C4", "C#4", "D4", "D#4", "E4", "F4", "F#4", "G4", "G#4", "A4", "A#4", "B4", "C5" }.Contains(value)))
        return Results.BadRequest(new { error = "Select letters, common colors, or notes C4–C5." });
    int[] delays = Enumerable.Repeat(1, values.Length).ToArray();
    if (input.TryGetProperty("delays", out var delayJson))
    {
        if (kind is not ("colors" or "music") || delayJson.ValueKind != System.Text.Json.JsonValueKind.Array || delayJson.GetArrayLength() != values.Length ||
            delayJson.EnumerateArray().Any(value => value.ValueKind != System.Text.Json.JsonValueKind.Number || !value.TryGetInt32(out var units) || units is < 1 or > 12))
            return Results.BadRequest(new { error = "Supply one delay from 1 to 12 per element (each unit is 0.25 seconds)." });
        delays = delayJson.EnumerateArray().Select(value => value.GetInt32()).ToArray();
    }
    var sandbox = sandboxes[(string)context.Items["session"]!]; var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        // Remember is idempotent for a complete pattern, including timing facets.
        foreach (var candidate in uks.Labeled(sequenceCategory(kind))?.Children ?? new List<UKS.Thought>())
        {
            var start = candidate.LinksTo.FirstOrDefault(link => link.LinkType == uks.Labeled(sequenceType(kind)))?.To;
            if (start is null) continue;
            var cells = demoSequenceCells(start);
            if (!cells.Select(cell => cell.LinksTo.FirstOrDefault(link => link.LinkType == uks.Labeled("VLU") && link.To?.HasProperty("isSequenceMetadata") != true)?.To?.Label)
                .SequenceEqual(values.Select(value => kind == "word" ? UKS.UKS.GetCharacterLabel(value[0]) : value))) continue;
            if (kind != "word" && !cells.Select(cell => cell.LinksTo.FirstOrDefault(link => link.LinkType == uks.Labeled("VLU") && link.To?.HasAncestor(uks.Labeled("duration")) == true)?.To?.V is int units ? units : 1).SequenceEqual(delays)) continue;
            return Results.Ok(demoSequence(candidate, kind));
        }
        // The server allocates order names under the sandbox lock.
        if (kind == "colors" || kind == "music" && label == "t:new")
        {
            var prefix = kind == "colors" ? "o:" : "t:";
            int order = 1; while (uks.Labeled(prefix + order) is not null) order++;
            label = prefix + order;
        }
        var existing = uks.Labeled(label);
        if (existing is not null)
        {
            var link = existing.LinksTo.FirstOrDefault(link => link.LinkType == uks.Labeled(sequenceType(kind)));
            var expected = values.Select(value => kind == "word" ? UKS.UKS.GetCharacterLabel(value[0]) : value);
            if (link?.To is not null && demoSequenceCells(link.To).Select(cell => cell.LinksTo.FirstOrDefault(edge => edge.LinkType == uks.Labeled("VLU") && edge.To?.HasProperty("isSequenceMetadata") != true)?.To?.Label).SequenceEqual(expected))
                return Results.Ok(demoSequence(existing, kind));
            return Results.Conflict(new { error = "That name already has different content. Choose another name." });
        }
        var preview = uks.Copy(); int previewBefore = storeSize(preview);
        storeDemoSequence(preview, kind, label, values, delays);
        if (addedSoFar(uks) + storeSize(preview) - previewBefore > thoughtLimit)
            return Results.Json(new { error = $"This demo is limited to {thoughtLimit} added Thoughts, including relationships." }, statusCode: 409);
        var owner = storeDemoSequence(uks, kind, label, values, delays);
        Volatile.Write(ref sandbox.HasChanges.Value, true);
        return Results.Ok(demoSequence(owner, kind));
    }
});
app.MapGet("/api/sequences", (string? kind, HttpContext context) =>
{
    if (kind is not ("word" or "colors" or "music")) return Results.BadRequest(new { error = "Choose word, colors, or music." });
    var sandbox = sandboxes[(string)context.Items["session"]!]; var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        // Make the category available for an initially empty lab graph.
        uks.GetOrAddThought(sequenceCategory(kind!), "Abstract");
        return Results.Ok(new { sequences = uks.Labeled(sequenceCategory(kind))?.Children
        .Where(owner => owner.LinksTo.Any(link => link.LinkType == uks.Labeled(sequenceType(kind)) && link.To is not null))
        .Select(owner => demoSequence(owner, kind!)).ToArray() ?? Array.Empty<object>() });
    }
});
app.MapGet("/api/sequence-matches", (string? kind, string? values, bool? matchFirst, bool? matchLast, HttpContext context) =>
{
    if (kind is not ("word" or "colors" or "music")) return Results.BadRequest(new { error = "Choose word, colors, or music." });
    string[] pattern;
    try { pattern = System.Text.Json.JsonSerializer.Deserialize<string[]>(values ?? "[]") ?? Array.Empty<string>(); }
    catch (System.Text.Json.JsonException) { return Results.BadRequest(new { error = "Supply a list of sequence values." }); }
    if (pattern.Length is < 1 or > 32 || pattern.Any(value => string.IsNullOrEmpty(value) || value.Length > 16))
        return Results.BadRequest(new { error = "Choose between one and 32 values." });
    if (pattern.Any(value => kind == "word" ? value.Length != 1 || !char.IsAsciiLetter(value[0])
        : kind == "colors" ? !new[] { "red", "orange", "yellow", "green", "blue", "purple", "brown", "pink", "white" }.Contains(value)
        : !new[] { "C4", "C#4", "D4", "D#4", "E4", "F4", "F#4", "G4", "G#4", "A4", "A#4", "B4", "C5" }.Contains(value)))
        return Results.BadRequest(new { error = "Use letters, common colors, or notes C4–C5." });
    var sandbox = sandboxes[(string)context.Items["session"]!]; var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        var thoughts = pattern.Select(value => uks.Labeled(kind == "word" ? UKS.UKS.GetCharacterLabel(value[0]) : value)).ToList();
        if (thoughts.Any(thought => thought is null)) return Results.Ok(new { matches = Array.Empty<object>() });
        var options = new UKS.Thought(uks);
        try
        {
            // Use the UKS's inherited search-option properties for boundary matching.
            if (matchFirst == true) options.AddLink(uks.Labeled("hasProperty")!, uks.GetOrAddThought("mustMatchFirst"));
            if (matchLast == true) options.AddLink(uks.Labeled("hasProperty")!, uks.GetOrAddThought("mustMatchLast"));
            var matches = uks.FindSequencesByActivation(thoughts!, options, getFirstElements:false);
            var owners = uks.Labeled(sequenceCategory(kind))?.Children ?? new List<UKS.Thought>();
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
                    var labels = cells.Select(cell => cell.LinksTo.First(link => link.LinkType == uks.Labeled("VLU") && link.To?.HasProperty("isSequenceMetadata") != true).To!.Label).ToArray();
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
        var preview = uks.Copy(); int previewBefore = storeSize(preview);
        try
        {
            UKSWeb.StatementInputParser.Add(preview, input.Source, input.RelationshipType, input.Target);
            if (addedSoFar(uks) + storeSize(preview) - previewBefore > thoughtLimit)
                return Results.Json(new { error = $"This demo is limited to {thoughtLimit} added Thoughts, including relationships. Delete some content before adding more.", limit = thoughtLimit }, statusCode: 409);
        }
        catch (Exception error) when (error is FormatException or ArgumentException or System.Text.Json.JsonException)
        {
            return Results.BadRequest(new { error = error.Message });
        }

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
                source = UKSWeb.StatementInputParser.Format(thought),
                relationshipType = r.LinkType is null ? null : UKSWeb.StatementInputParser.Format(r.LinkType),
                target = r.To is null ? null : UKSWeb.StatementInputParser.Format(r.To)
            }).ToArray();
        return Results.Ok(new { label = thought.Label, children, relationships });
    }
});

app.MapGet("/api/events", (string? label, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(label)) return Results.BadRequest(new { error = "Choose a Thought." });
    var sandbox = sandboxes[(string)context.Items["session"]!];
    lock (sandbox.Sync)
    {
        var subject = sandbox.Store.Labeled(label.Trim());
        if (subject is null) return Results.NotFound(new { error = "That Thought was not found." });
        var head = subject.GetTargetOfFirstLinkOfType("events") as UKS.SeqElement;
        var visited = new HashSet<UKS.SeqElement>(ReferenceEqualityComparer.Instance);
        var history = new List<object>();
        var now = DateTimeOffset.UtcNow;
        for (var cell = head; cell is not null && visited.Add(cell); cell = cell.NXT)
        {
            if (cell.VLU is not UKS.Link archived) continue;
            var timestamp = cell.GetTargetOfFirstLinkOfType("validUntil") ?? archived.GetTargetOfFirstLinkOfType("validUntil");
            if (!DateTimeOffset.TryParseExact(timestamp?.Label,
                    "O", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var ended)) continue;
            history.Add(new
            {
                relationship = UKSWeb.StatementInputParser.Format(archived),
                validUntil = ended,
                secondsAgo = Math.Max(0, (long)(now - ended).TotalSeconds)
            });
        }
        return Results.Ok(new { events = history });
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
        var thought = uks.Labeled(label);
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
object attributeSnapshot(UKS.Thought source, UKS.Link result,
    HashSet<UKS.Link>? displayed = null, bool conditionalRule = false)
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
    var stored = conditionalRule ? result :
        origin.LinksTo.FirstOrDefault(edge => edge.LinkType == result.LinkType && edge.To == result.To);
    string? conditionalStatus = conditionalRule ? "Indeterminate" : null;
    if (displayed is not null)
    {
        // The UKS returns a proven possibility with its ordinary type. Use the
        // copied clauses to find the stored rule even if an assertion also exists.
        var clauses = result.LinksTo.Where(clause => UKS.UKS.IsConditionalLinkType(clause.LinkType)).ToArray();
        if (!conditionalRule && clauses.Length > 0 && result.LinkType is { } type)
        {
            conditionalStatus = "True";
            stored = origin.LinksTo.FirstOrDefault(edge => edge.To == result.To &&
                edge.LinkType?.HasAncestor(type) == true &&
                clauses.All(clause => edge.LinksTo.Any(original => original.LinkType == clause.LinkType &&
                    ReferenceEquals(original.To, clause.To))));
        }
        if (stored is not null) displayed.Add(stored);
    }
    var evidence = stored is not null && paths.TryGetValue(origin, out var route)
        ? route.Append(stored).ToArray() : Array.Empty<UKS.Link>();
    return new
    {
        source = source.Label, relationshipType = result.LinkType?.Label, target = result.To?.Label,
        inheritedFrom = result.InheritedFromCategory?.Label, depth = result.InheritanceDepth, weight = result.Weight,
        conditionalRule, conditionalStatus,
        details = displayed is not null && stored is not null
            ? attributeDetails(stored, new HashSet<UKS.Link>(ReferenceEqualityComparer.Instance))
            : Array.Empty<object>(),
        evidenceLinkIds = evidence.Select(graphId).ToArray(),
        evidenceThoughtIds = evidence.SelectMany(edge => new[] { edge.From!, edge.To! }).Distinct().Select(graphId).ToArray()
    };
}

// A subsidiary Relationship can itself be the source of more Relationships.
object[] attributeDetails(UKS.Link parent, HashSet<UKS.Link> path)
{
    if (path.Count >= 16 || !path.Add(parent)) return Array.Empty<object>();
    var details = parent.LinksTo
        .Where(child => child.LinkType is not null && child.To is not null && !path.Contains(child))
        .Select(child => new
        {
            relationshipType = UKSWeb.StatementInputParser.Format(child.LinkType),
            target = UKSWeb.StatementInputParser.Format(child.To),
            details = attributeDetails(child, path)
        }).ToArray();
    path.Remove(parent);
    return details;
}

// Query results are values, not stored Relationship identities. Never register
// GetAttributes' temporary Links as graph handles; the graph shows stored facts.
app.MapGet("/api/attributes", (string? label, string? explainTarget, string? explainType, string? compareGroup, HttpContext context) =>
{
    var sandbox = sandboxes[(string)context.Items["session"]!];
    var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        if (string.IsNullOrWhiteSpace(label)) return Results.BadRequest(new { error = "Choose a Thought." });
        var thought = uks.Labeled(label.Trim());
        if (thought is null) return Results.NotFound(new { error = "That Thought was not found." });
        var evaluated = uks.GetAttributes(thought);
        object? explanation = null;
        if (explainTarget is not null && explainType is not null)
        {
            var target = uks.Labeled(explainTarget);
            var type = uks.Labeled(explainType);
            var passed = uks.Why().ToHashSet(ReferenceEqualityComparer.Instance);
            var failed = uks.WhyNot().ToHashSet(ReferenceEqualityComparer.Instance);
            var clauses = thought.LinksTo.Where(link => target is not null && type is not null &&
                    link.To == target && link.LinkType?.HasAncestor(type) == true)
                .SelectMany(link => link.LinksTo)
                .Where(link => UKS.UKS.IsConditionalLinkType(link.LinkType)).ToArray();
            var leaves = new HashSet<UKS.Link>(ReferenceEqualityComparer.Instance);
            var visited = new HashSet<UKS.Link>(ReferenceEqualityComparer.Instance);
            var pending = new Queue<UKS.Link>(clauses.Select(link => link.To).OfType<UKS.Link>());
            while (pending.TryDequeue(out var premise))
            {
                if (!visited.Add(premise)) continue;
                if (premise.LinkType?.HasProperty("isConjunction") == true)
                {
                    if (premise.From is UKS.Link left) pending.Enqueue(left);
                    if (premise.To is UKS.Link right) pending.Enqueue(right);
                }
                else leaves.Add(premise);
            }
            explanation = new
            {
                holds = target is not null && type is not null && evaluated.Any(link => link.To == target && link.LinkType == type),
                asserted = target is not null && type is not null && thought.LinksTo.Any(link =>
                    link.To == target && link.LinkType == type && !link.LinksTo.Any(clause => UKS.UKS.IsConditionalLinkType(clause.LinkType))),
                hasRule = clauses.Length > 0,
                conditions = leaves.Select(premise => new
                {
                    relationship = UKSWeb.StatementInputParser.Format(premise),
                    status = passed.Contains(premise) ? "True" : failed.Contains(premise) ? "False" : "Not evaluated"
                }).ToArray()
            };
        }
        // The optional comparison is for display. Only evaluated attributes
        // establish facts and satisfy conditions.
        var group = compareGroup is null ? null : uks.Labeled(compareGroup);
        var displayed = new HashSet<UKS.Link>(ReferenceEqualityComparer.Instance);
        var attributes = evaluated.Where(link => link.LinkType is not null && link.To is not null)
            .Select(link => attributeSnapshot(thought, link, displayed)).ToArray();
        var suppressed = Array.Empty<object>();
        if (group?.HasProperty("isExclusive") == true)
        {
            var winners = evaluated.Where(link => link.To?.HasAncestor(group) == true).ToArray();
            suppressed = thought.LinksTo.Where(link =>
                    link.To?.HasAncestor(group) == true && link.LinkType is not null &&
                    !link.LinksTo.Any(clause => UKS.UKS.IsConditionalLinkType(clause.LinkType)) &&
                    winners.Any(winner => winner.LinkType == link.LinkType) &&
                    !winners.Any(winner => winner.LinkType == link.LinkType && winner.To == link.To))
                .Select(link => attributeSnapshot(thought, link, displayed)).ToArray();
        }
        // Keep the rule and its premises visible even when it yields no current attribute.
        var rules = thought.LinksTo.Where(link => !displayed.Contains(link) && link.LinkType is not null &&
                link.To is not null && link.LinksTo.Any(clause => UKS.UKS.IsConditionalLinkType(clause.LinkType)))
            .Select(link => attributeSnapshot(thought, link, displayed, conditionalRule: true)).ToArray();
        return Results.Ok(new { label = thought.Label, id = graphId(thought), explanation, suppressed, attributes, rules });
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

app.MapGet("/api/graph", (string? label, string[] labels, string[] ids, bool? expand, bool? sequenceValues, HttpContext context) =>
{
    var sandbox = sandboxes[(string)context.Items["session"]!];
    var uks = sandbox.Store;
    lock (sandbox.Sync)
    {
        if (labels.Length > 0)
        {
            if (labels.Length > 256) return Results.BadRequest(new { error = "Supply at most 256 Thought labels." });
            var selected = new List<UKS.Thought>();
            var missingLabels = new List<string>();
            foreach (var name in labels)
                if (uks.Labeled(name) is UKS.Thought namedThought) selected.Add(namedThought);
                else missingLabels.Add(name);
            var packet = (Dictionary<string, object?>)graphSnapshot(selected, expand == true, sequenceValues == true);
            packet["missingLabels"] = missingLabels;
            return Results.Ok(packet);
        }
        if (ids.Length > 0)
        {
            // Refresh only nodes the browser has already expanded, in bounded batches.
            if (ids.Length > 256) return Results.BadRequest(new { error = "Supply at most 256 Thought labels." });
            var snapshots = new List<object>();
            var missingIds = new List<string>();
            foreach (var name in ids.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (uks.Labeled(name) is UKS.Thought node)
                    snapshots.Add(graphSnapshot(new[] { node }, true, sequenceValues == true));
                else missingIds.Add(name);
            }
            return Results.Ok(new { snapshots, missingIds });
        }

        if (string.IsNullOrWhiteSpace(label))
            return Results.BadRequest(new { error = "Enter a root Thought label." });
        if (uks.Labeled(label) is not UKS.Thought thought)
            return Results.NotFound(new { error = "That Thought is not available in this demo." });

        return Results.Ok(graphSnapshot(new[] { thought }, expand == true, sequenceValues == true));
    }
});

await app.RunAsync();
await cleanupTask;

record StatementInput(string? Source, string? RelationshipType, string? Target);

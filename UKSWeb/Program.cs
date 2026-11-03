var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Keep the graph on the server. Only labels and response data leave this process.
var uks = new UKS.UKS();
UKS.UKS.ThoughtLimitPerUser = builder.Configuration.GetValue<int>("ThoughtLimitPerUser", 1000);
uks.CreateInitialStructure();
var graphLock = new object();
// Browser-only handles distinguish unnamed Thoughts without changing UKS labels.
var graphIds = new System.Runtime.CompilerServices.ConditionalWeakTable<UKS.Thought, System.Runtime.CompilerServices.StrongBox<long>>();
var graphThoughts = new Dictionary<long, WeakReference<UKS.Thought>>();
long nextGraphId = 0;
Func<UKS.Thought, long> graphId = thought =>
{
    if (graphIds.TryGetValue(thought, out var existing)) return existing.Value;
    var id = ++nextGraphId;
    graphIds.Add(thought, new System.Runtime.CompilerServices.StrongBox<long>(id));
    graphThoughts[id] = new WeakReference<UKS.Thought>(thought);
    return id;
};
Func<UKS.Thought, bool> graphRegistered = thought =>
    uks.AtomicThoughts.Any(t => ReferenceEquals(t, thought)) ||
    UKS.ThoughtLabels.LabelList.Values.Any(t => ReferenceEquals(t, thought)) ||
    thought.LinksToWriteable.Any(link => ReferenceEquals(link.From, thought)) ||
    thought.LinksFromWriteable.Any(link => link.From?.LinksToWriteable.Any(stored => ReferenceEquals(stored, link)) == true) ||
    (thought is UKS.Link storedLink && storedLink.From?.LinksToWriteable.Any(l => ReferenceEquals(l, storedLink)) == true);
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
    return new
    {
        root = graphId(thought),
        nodes = nodes.Select(t => new
        {
            id = graphId(t),
            label = !string.IsNullOrEmpty(t.Label) ? t.Label : t is UKS.Link l
                ? $"[{l.From?.Label} → {l.LinkType?.Label} → {l.To?.Label}]" : "(unnamed Thought)",
            owner = t.Owner,
            isRelationship = t is UKS.Link
        }).ToArray(),
        links = links.Select(link => new
        {
            id = graphId(link), source = graphId(link.From!), target = graphId(link.To!),
            type = link.LinkType!.Label, owner = link.Owner
        }).ToArray()
    };
};

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

// This selector is for the prototype, not authentication. Each request carries its own user.
app.Use(async (context, next) =>
{
    var userText = context.Request.Query["user"].ToString();
    var user = 0;
    if (userText.Length > 0 && (!int.TryParse(userText, out user) || user < 0 || user > 4))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = "Select a user number from 0 to 4." });
        return;
    }

    uks.SetUser(user);
    try { await next(context); }
    finally { uks.SetUser(0); }
});

app.MapPost("/api/statements", (StatementInput input) =>
{
    if (string.IsNullOrWhiteSpace(input.Source) ||
        string.IsNullOrWhiteSpace(input.RelationshipType) ||
        string.IsNullOrWhiteSpace(input.Target))
        return Results.BadRequest(new { error = "Enter a source, relationship type, and target." });

    // Serialize graph changes; SetUser itself is local to each request's execution context.
    lock (graphLock)
    {
        var before = uks.OwnedThoughts;
        UKS.Link? relationship;
        try
        {
            relationship = uks.AddStatement(
                input.Source.Trim(), input.RelationshipType.Trim(), input.Target.Trim());
        }
        catch (InvalidOperationException error) when (error.Data.Contains("ThoughtLimitPerUser"))
        {
            // Remove new nodes and parent links from this failed addition as well.
            foreach (var thought in uks.OwnedThoughts.Except(before, ReferenceEqualityComparer.Instance)
                .Cast<UKS.Thought>().Reverse().ToArray())
                thought.Delete();
            return Results.Json(new { error = error.Message, limit = UKS.UKS.ThoughtLimitPerUser }, statusCode: 409);
        }
        if (relationship is null)
            return Results.Problem("UKS could not add the relationship.");

        return Results.Ok(new
        {
            source = relationship.From?.Label,
            relationshipType = relationship.LinkType?.Label,
            target = relationship.To?.Label,
            owner = relationship.Owner
        });
    }
});

app.MapGet("/api/children", (string? label) =>
{
    if (string.IsNullOrWhiteSpace(label))
        return Results.BadRequest(new { error = "Enter a Thought label." });

    lock (graphLock)
    {
        var thought = uks.Labeled(label.Trim());
        if (thought is null)
            return Results.NotFound(new { error = $"No Thought named '{label.Trim()}' was found." });

        var children = thought.Children.Distinct().OrderBy(child => child.Label, StringComparer.OrdinalIgnoreCase)
            .Select(child => new { label = child.Label, owner = child.Owner }).ToArray();
        // Only stored outgoing relationships: no inherited attributes or query evaluation.
        var relationships = thought.LinksTo.Where(r => r.From == thought)
            .OrderBy(r => r.LinkType?.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.To?.Label, StringComparer.OrdinalIgnoreCase)
            .Select(r => new
            {
                source = thought.Label,
                relationshipType = r.LinkType?.Label,
                target = r.To?.Label,
                owner = r.Owner
            }).ToArray();
        return Results.Ok(new { label = thought.Label, owner = thought.Owner, children, relationships });
    }
});

app.MapDelete("/api/thoughts", (string? label, int? user) =>
{
    if (string.IsNullOrWhiteSpace(label))
        return Results.BadRequest(new { error = "Enter a Thought label." });

    lock (graphLock)
    {
        var thought = uks.Labeled(label.Trim());
        if (thought is null)
            return Results.NotFound(new { error = $"No Thought named '{label.Trim()}' was found." });

        if (thought.Owner != (user ?? 0))
            return Results.Json(new { error = "Shared Thoughts can only be deleted by user 0." }, statusCode: 403);

        var deletedLabel = thought.Label;
        thought.Delete();
        return Results.Ok(new { label = deletedLabel });
    }
});

app.MapGet("/api/graph", (string? label, long? id, string? ids, bool? expand, int? user) =>
{
    lock (graphLock)
    {
        // The handle table never owns a Thought or keeps deleted content alive.
        foreach (var stale in graphThoughts.Where(pair => !pair.Value.TryGetTarget(out _)).Select(pair => pair.Key).ToArray())
            graphThoughts.Remove(stale);

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
                    (node.Owner != 0 && node.Owner != (user ?? 0)) || !graphRegistered(node))
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
        if (thought is null || (thought.Owner != 0 && thought.Owner != (user ?? 0)))
            return Results.NotFound(new { error = "That Thought is not available to this user." });

        if (!graphRegistered(thought))
            return Results.NotFound(new { error = "That Thought has been deleted. Reload the root to refresh the graph." });

        return Results.Ok(graphSnapshot(thought, expand == true));
    }
});

app.Run();

record StatementInput(string? Source, string? RelationshipType, string? Target);

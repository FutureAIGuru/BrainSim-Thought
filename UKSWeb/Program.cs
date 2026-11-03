var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// User numbers select independent in-memory sandboxes. No ownership is stored in UKS.
var thoughtLimit = builder.Configuration.GetValue<int>("ThoughtLimitPerUser", 1000);
if (thoughtLimit < 1) throw new InvalidOperationException("ThoughtLimitPerUser must be a positive integer.");
var sandboxes = new Dictionary<int, UKS.UKS>();
var graphLocks = Enumerable.Range(0, 5).ToDictionary(user => user, _ => new object());
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
for (var user = 0; user <= 4; user++)
{
    var store = new UKS.UKS();
    store.CreateInitialStructure();
    sandboxes.Add(user, store);
    foreach (var thought in storedThoughts(store)) initialThoughts.Add(thought, new object());
}

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

    var previousUKS = UKS.UKS.theUKS;
    context.Items["user"] = user;
    UKS.UKS.theUKS = sandboxes[user];
    try { await next(context); }
    finally { UKS.UKS.theUKS = previousUKS; }
});

app.MapPost("/api/statements", (StatementInput input, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(input.Source) ||
        string.IsNullOrWhiteSpace(input.RelationshipType) ||
        string.IsNullOrWhiteSpace(input.Target))
        return Results.BadRequest(new { error = "Enter a source, relationship type, and target." });

    var user = (int)context.Items["user"]!;
    var uks = sandboxes[user];
    lock (graphLocks[user])
    {
        if (user != 0)
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
                if (preview.AddStatement(input.Source.Trim(), input.RelationshipType.Trim(), input.Target.Trim()) is null)
                    return Results.Problem("UKS could not add the relationship.");
                if (storedThoughts(preview).Count(thought => !previewInitial.Contains(thought)) > thoughtLimit)
                    return Results.Json(new { error = $"This sandbox is limited to {thoughtLimit} added Thoughts, including relationships. Delete some content before adding more.", limit = thoughtLimit }, statusCode: 409);
            }
            finally { UKS.UKS.theUKS = uks; }
        }

        var relationship = uks.AddStatement(
            input.Source.Trim(), input.RelationshipType.Trim(), input.Target.Trim());
        if (relationship is null)
            return Results.Problem("UKS could not add the relationship.");

        return Results.Ok(new
        {
            source = relationship.From?.Label,
            relationshipType = relationship.LinkType?.Label,
            target = relationship.To?.Label
        });
    }
});

app.MapGet("/api/children", (string? label, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(label))
        return Results.BadRequest(new { error = "Enter a Thought label." });

    var user = (int)context.Items["user"]!;
    var uks = sandboxes[user];
    lock (graphLocks[user])
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
                source = thought.Label,
                relationshipType = r.LinkType?.Label,
                target = r.To?.Label
            }).ToArray();
        return Results.Ok(new { label = thought.Label, children, relationships });
    }
});

app.MapDelete("/api/thoughts", (string? label, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(label))
        return Results.BadRequest(new { error = "Enter a Thought label." });

    var user = (int)context.Items["user"]!;
    var uks = sandboxes[user];
    lock (graphLocks[user])
    {
        var thought = uks.Labeled(label.Trim());
        if (thought is null)
            return Results.NotFound(new { error = $"No Thought named '{label.Trim()}' was found." });

        var deletedLabel = thought.Label;
        thought.Delete();
        return Results.Ok(new { label = deletedLabel });
    }
});

app.MapGet("/api/graph", (string? label, long? id, string? ids, bool? expand, HttpContext context) =>
{
    var user = (int)context.Items["user"]!;
    var uks = sandboxes[user];
    lock (graphLocks[user])
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
            return Results.NotFound(new { error = "That Thought is not available to this user." });

        if (!graphRegistered(uks, thought))
            return Results.NotFound(new { error = "That Thought has been deleted. Reload the root to refresh the graph." });

        return Results.Ok(graphSnapshot(thought, expand == true));
    }
});

app.Run();

record StatementInput(string? Source, string? RelationshipType, string? Target);

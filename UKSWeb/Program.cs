var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Keep the graph on the server. Only labels and response data leave this process.
var uks = new UKS.UKS();
uks.CreateInitialStructure();
var graphLock = new object();

app.UseDefaultFiles();
app.UseStaticFiles();

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
        var relationship = uks.AddStatement(
            input.Source.Trim(), input.RelationshipType.Trim(), input.Target.Trim());
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

app.Run();

record StatementInput(string? Source, string? RelationshipType, string? Target);

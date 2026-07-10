/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * Ch.2 sparse activation + Ch.4 relationship AND-gate: traversal requires active
 * source thought and active relationship type together.
 */

namespace UKS;

/// <summary>
/// Per engine-tick activation context for relationship-gated UKS traversal.
/// </summary>
public sealed class TraversalContext
{
    public HashSet<Thought> ActiveThoughts { get; } = new();
    public HashSet<Thought> ActiveLinkTypes { get; } = new();

    public void Clear()
    {
        ActiveThoughts.Clear();
        ActiveLinkTypes.Clear();
    }

    /// <summary>
    /// Mark a thought active for gated traversal (Ch.2 sparse activation).
    /// Does not call <see cref="Thought.Fire"/> — firing re-enters ModuleAlgorithm's
    /// interpreter queue and can prevent <c>ExecuteAllSteps</c> from terminating.
    /// </summary>
    public void Activate(Thought? thought)
    {
        if (thought is null) return;
        ActiveThoughts.Add(thought);
    }

    /// <summary>
    /// Mark a relationship type active for the Ch.4 AND-gate.
    /// Does not call <see cref="Thought.Fire"/> (see <see cref="Activate"/>).
    /// </summary>
    public void ActivateRelationship(Thought? linkType)
    {
        if (linkType is null) return;
        ActiveLinkTypes.Add(linkType);
        ActiveThoughts.Add(linkType);
    }

    public bool CanTraverse(Thought? source, Thought? linkType)
    {
        if (source is null || linkType is null) return false;
        return ActiveThoughts.Contains(source) && ActiveLinkTypes.Contains(linkType);
    }
}

public partial class UKS
{
    /// <summary>Active traversal context for the current engine tick.</summary>
    public TraversalContext CurrentTraversal { get; } = new();

    /// <summary>Clear traversal activation at the start of each engine cycle.</summary>
    public void BeginTraversalCycle() => CurrentTraversal.Clear();

    /// <summary>
    /// Collect links of <paramref name="linkType"/> from <paramref name="source"/> only when
    /// both are active in <paramref name="ctx"/> (Ch.4 AND-gate).
    /// </summary>
    public List<Link> GetGatedLinks(Thought source, Thought linkType, TraversalContext? ctx = null)
    {
        ctx ??= CurrentTraversal;
        var results = new List<Link>();
        if (!ctx.CanTraverse(source, linkType)) return results;

        if (IsIsARelationship(linkType))
            return CollectDirectLinks(source, linkType, source, 0);

        return CollectGatedInheritableLinks(source, linkType, ctx);
    }

    /// <summary>Return target thoughts reachable via gated traversal along <paramref name="linkType"/>.</summary>
    public List<Thought> Traverse(Thought source, Thought linkType, TraversalContext? ctx = null)
    {
        ctx ??= CurrentTraversal;
        return GetGatedLinks(source, linkType, ctx)
            .Where(l => l.To is not null)
            .Select(l => l.To!)
            .Distinct()
            .ToList();
    }

    private List<Link> CollectGatedInheritableLinks(Thought querySource, Thought linkType, TraversalContext ctx)
    {
        const int maxHops = 8;
        var results = new List<Link>();
        var frontier = new List<(Thought thought, int depth)> { (querySource, 0) };
        var seen = new HashSet<Thought> { querySource };

        for (int i = 0; i < frontier.Count; i++)
        {
            (Thought thought, int depth) = frontier[i];
            results.AddRange(CollectDirectLinks(thought, linkType, querySource, depth));

            if (depth >= maxHops) continue;
            foreach (Link r in thought.LinksTo)
            {
                if (!IsIsARelationship(r.LinkType) || r.To is null || !seen.Add(r.To)) continue;
                frontier.Add((r.To, depth + 1));
            }
        }

        return results;
    }

    private static List<Link> CollectDirectLinks(Thought from, Thought linkType, Thought querySource, int inheritanceDepth)
    {
        var results = new List<Link>();
        foreach (Link r in from.LinksTo)
        {
            if (r.LinkType is null || !LinkTypeMatchesRequested(r.LinkType, linkType) || r.To is null) continue;
            results.Add(new Link(querySource, r.LinkType, r.To)
            {
                Weight = r.Weight,
                InheritanceDepth = inheritanceDepth
            });
        }
        return results;
    }

    private static bool IsIsARelationship(Thought? linkType)
    {
        if (linkType is null) return false;
        return ReferenceEquals(linkType, Thought.IsA) ||
               string.Equals(linkType.Label, "is-a", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LinkTypeMatchesRequested(Thought? actual, Thought requested)
    {
        if (actual is null) return false;
        if (ReferenceEquals(actual, requested)) return true;
        if (string.Equals(actual.Label, requested.Label, StringComparison.OrdinalIgnoreCase)) return true;
        return actual.HasAncestor(requested);
    }
}
/*
 * Ch.5 context resolution among conflicting inherited expectations.
 * Context competition belongs to the algorithm interpreter.
 */

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

public sealed class ContextCaseResult
{
    public Thought? Case { get; init; }
    public Thought? Response { get; init; }
    public float Weight { get; init; }
}

public partial class ModuleAlgorithm
{
    public ContextCaseResult? SelectBestContextCase(
        Thought contextRoot,
        Func<Thought, Thought?> resolveIndirection)
    {
        if (contextRoot is null) return null;

        Thought? bestCase = null;
        Thought? bestResponse = null;
        float bestWeight = 0;

        foreach (Thought caseThought in contextRoot.Children)
        {
            float weight = ScoreContextCase(caseThought, resolveIndirection);
            if (weight <= bestWeight) continue;
            bestCase = caseThought;
            bestResponse = caseThought.GetTargetOfFirstLinkOfType("response");
            bestWeight = weight;
        }

        if (bestCase is null) return null;
        return new ContextCaseResult
        {
            Case = bestCase,
            Response = bestResponse,
            Weight = bestWeight
        };
    }

    public float ScoreContextCase(Thought caseThought, Func<Thought, Thought?> resolveIndirection)
    {
        if (caseThought is null) return 0;
        float weight = 0;

        foreach (Link l in caseThought.LinksTo.Where(x => x.LinkType?.Label == "has"))
        {
            if (l.To is not Link test) continue;
            if (test.LinkType?.HasProperty("isPossible") != true) continue;
            if (test.From is null || test.To is null) continue;

            Thought? testType = null;
            bool legacy = false;
            foreach (Thought actionType in test.LinkType.AncestorsWithSelf)
            {
                if (!actionType.HasProperty("isPossible")) continue;
                // Older EXIST types store their ordinary type as an attribute.
                testType = actionType.LinksTo.LastOrDefault(link =>
                    ReferenceEquals(link.LinkType, theUKS.Labeled("is")) && link.To is not null &&
                    !link.To.HasProperty("isPossible") && !link.To.HasProperty("setRelationship"))?.To;
                if (testType is not null)
                {
                    legacy = !actionType.Parents.Any(parent => ReferenceEquals(parent, testType));
                    break;
                }
                List<Thought> ordinaryParents = actionType.Parents.Where(parent =>
                    !parent.HasProperty("isPossible") && !parent.HasProperty("setRelationship")).ToList();
                testType = ordinaryParents.FirstOrDefault(parent =>
                    ordinaryParents.All(other => parent.HasAncestor(other))) ?? ordinaryParents.FirstOrDefault();
                if (testType is not null) break;
            }
            Thought? src = resolveIndirection(test.From);
            Thought? target = resolveIndirection(test.To);
            // Unresolved operands contribute no score, even for legacy negative conditions.
            if (src is null || target is null) continue;

            bool equality = test.LinkType.HasProperty("isEqual") || testType?.HasProperty("isEqual") == true;
            if (equality)
            {
                bool equal = ReferenceEquals(src, target);
                if (test.LinkType.HasProperty("isNegative")) equal = !equal;
                if (equal) weight += l.Weight;
                continue;
            }
            if (testType is null) continue;

            bool matched = false;
            IEnumerable<Link> candidates = src.HasProperty("isWildcard")
                ? theUKS.AtomicThoughts.SelectMany(thought => thought.LinksTo)
                : src.LinksTo;
            foreach (Link candidate in candidates)
            {
                if (candidate.From is null || candidate.LinkType is null || candidate.To is null ||
                    candidate.LinkType.HasProperty("isPossible") || candidate.LinkType.HasProperty("setRelationship"))
                    continue;
                bool matches = true;
                foreach (var (pattern, actual) in new[]
                {
                    (src, candidate.From), (testType, candidate.LinkType), (target, candidate.To)
                })
                {
                    if (!pattern.HasProperty("isWildcard"))
                    {
                        if (!ReferenceEquals(pattern, actual)) { matches = false; break; }
                        continue;
                    }
                    // Capability-bearing parents supply matching behavior; their ordinary
                    // class parents supply constraints, including inherited constraints.
                    List<Thought> constraints = pattern.AncestorsWithSelf
                        .Where(ancestor => ancestor.HasProperty("isWildcard"))
                        .SelectMany(ancestor => ancestor.Parents)
                        .Where(parent => !parent.HasProperty("isWildcard") &&
                            !ReferenceEquals(parent, theUKS.Labeled("Wildcard")) &&
                            !ReferenceEquals(parent, theUKS.Labeled("Thought")) &&
                            !ReferenceEquals(parent, theUKS.Labeled("Unknown")))
                        .ToList();
                    // Preserve the established OR interpretation for multiple class restrictions.
                    if (constraints.Count > 0 && !constraints.Any(constraint =>
                        actual.AncestorsWithSelf.Any(ancestor => ReferenceEquals(ancestor, constraint))))
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches) { matched = true; break; }
            }
            // Only old attribute-based NOT.EXIST types mean absence. New is.not.?
            // tests the ordinary is.not Relationship, including its negative assertion.
            if (legacy && test.LinkType.HasProperty("isNegative")) matched = !matched;
            if (matched) weight += l.Weight * test.Weight;
        }

        return weight;
    }

    /// <summary>
    /// Keep inherited links from the winning context's preferred is-a category; locals always kept.
    /// </summary>
    public List<Link> FilterLinksByContext(
        List<Link> links,
        Thought subject,
        Thought contextRoot,
        Func<Thought, Thought?> resolveIndirection)
    {
        var best = SelectBestContextCase(contextRoot, resolveIndirection);
        if (best?.Case is null) return links;

        Thought? preferredCategory = InferCategoryFromCase(best.Case, subject, resolveIndirection);
        if (preferredCategory is null) return links;

        return links.Where(l =>
            l.InheritanceDepth == 0 ||
            l.InheritedFromCategory is null ||
            ReferenceEquals(l.InheritedFromCategory, preferredCategory)).ToList();
    }

    public Thought? InferCategoryFromCase(
        Thought caseThought,
        Thought subject,
        Func<Thought, Thought?> resolveIndirection)
    {
        if (caseThought is null) return null;

        foreach (Link l in caseThought.LinksTo.Where(x => x.LinkType?.Label == "has"))
        {
            if (l.To is not Link test) continue;
            if (test.LinkType?.HasProperty("isPossible") != true) continue;
            if (test.From is null || test.To is null) continue;

            Thought? isA = theUKS.Labeled("is-a");
            bool testsMembership = test.LinkType.HasAncestor(isA) ||
                test.LinkType.AncestorsWithSelf.Any(ancestor =>
                    ancestor.HasProperty("isPossible") && ancestor.LinksTo.Any(link =>
                        ReferenceEquals(link.LinkType, theUKS.Labeled("is")) && ReferenceEquals(link.To, isA)));
            if (!testsMembership || test.LinkType.HasProperty("isEqual"))
                continue;

            Thought? src = resolveIndirection(test.From);
            if (!ReferenceEquals(src, subject)) continue;

            Thought? target = resolveIndirection(test.To);
            if (target is not null) return target;
        }

        return null;
    }
}

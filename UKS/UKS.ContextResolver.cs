/*
 * Ch.5 context resolution among conflicting inherited expectations.
 * Extracted from ModuleAlgorithm.EvaluateContext.
 */

namespace UKS;

public sealed class ContextCaseResult
{
    public Thought? Case { get; init; }
    public Thought? Response { get; init; }
    public float Weight { get; init; }
}

public partial class UKS
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
            if (test.LinkType?.HasAncestor("exist") != true) continue;

            bool not = test.LinkType.HasAncestor("not");
            Thought? testType = test.LinkType.LinksTo.FindFirst(x =>
                string.Equals(x.LinkType?.Label, "is", StringComparison.OrdinalIgnoreCase) &&
                x.To?.Label != "EXIST")?.To;
            Thought? src = resolveIndirection(test.From);
            if (src is null) continue;

            if (test.LinkType.HasAncestor("same") ||
                test.LinkType.Label.Contains("same", StringComparison.OrdinalIgnoreCase))
            {
                Thought? target = resolveIndirection(test.To);
                if (!not && src == target) weight += l.Weight;
                if (not && src != target) weight += l.Weight;
            }
            else if (test.To?.Label == "??")
            {
                if (!not && src.HasLink(testType) is not null) weight += l.Weight * test.Weight;
                if (not && src.HasLink(testType) is null) weight += l.Weight * test.Weight;
            }
            else
            {
                Thought? target = resolveIndirection(test.To);
                if (!not && src.HasLink(testType, target) is not null) weight += l.Weight * test.Weight;
                if (not && src.HasLink(testType, target) is null) weight += l.Weight * test.Weight;
            }
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
            l.InheritedFromCategory == preferredCategory).ToList();
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
            if (test.LinkType?.HasAncestor("exist") != true) continue;

            Thought? testType = test.LinkType.LinksTo.FindFirst(x =>
                string.Equals(x.LinkType?.Label, "is", StringComparison.OrdinalIgnoreCase) &&
                x.To?.Label != "EXIST")?.To;
            if (testType is null ||
                !string.Equals(testType.Label, "is-a", StringComparison.OrdinalIgnoreCase))
                continue;

            Thought? src = resolveIndirection(test.From);
            if (src != subject) continue;

            Thought? target = resolveIndirection(test.To);
            if (target is not null) return target;
        }

        return null;
    }
}
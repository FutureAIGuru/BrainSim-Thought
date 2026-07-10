/*
 * Ch.5 attribute bubbling on learn + BubbleLog (Ch.6 provenance prep).
 */

using System.Text.RegularExpressions;

namespace UKS;

public sealed class BubbleLogEntry
{
    public DateTime When { get; init; }
    public string ParentLabel { get; init; } = "";
    public string LinkTypeLabel { get; init; } = "";
    public string TargetLabel { get; init; } = "";
    public string Action { get; init; } = "";
}

public partial class UKS
{
    public event Action<Link>? LinkAdded;

    private readonly List<BubbleLogEntry> bubbleLog = new();
    private const int MaxBubbleLogEntries = 256;

    public IReadOnlyList<BubbleLogEntry> BubbleLog => bubbleLog;

    private static readonly string[] BubbleExcludeTypes =
    {
        "hasProperty", "isTransitive", "isCommutative", "inverseOf", "hasAttribute", "hasDigit"
    };

    private sealed class LinkDest
    {
        public Thought linkType = null!;
        public Thought target = null!;
        public List<Link> links = new();
    }

    internal void RaiseLinkAdded(Link lnk) => LinkAdded?.Invoke(lnk);

    /// <summary>Ch.5 category emergence: bubble shared child attrs; return parent if changed.</summary>
    public Thought? TryFormCategoryFromChildren(Thought parent, float minFraction = 0.6f) =>
        BubbleSharedAttributes(parent, minFraction) ? parent : null;

    /// <summary>
    /// Port of ModuleAttributeBubble majority logic — bubble shared child links to parent.
    /// </summary>
    public bool BubbleSharedAttributes(Thought parent, float minFraction = 0.6f)
    {
        if (parent is null || parent.Children.Count == 0) return false;
        if (parent.Label == "Unknown") return false;

        bool changed = false;
        List<LinkDest> itemCounts = new();
        foreach (Thought child in parent.ChildrenWithSubclasses)
        {
            foreach (Link r in child.LinksTo)
            {
                if (r.LinkType == Thought.IsA) continue;
                Thought useLinkType = GetBubbleInstanceType(r.LinkType);
                LinkDest? found = itemCounts.FindFirst(x => x.linkType == useLinkType && x.target == r.To);
                if (found is null)
                {
                    found = new LinkDest { linkType = useLinkType, target = r.To! };
                    itemCounts.Add(found);
                }
                found.links.Add(r);
            }
        }

        if (itemCounts.Count == 0) return false;
        var sortedItems = itemCounts.OrderByDescending(x => x.links.Count).ToList();
        float totalCount = parent.Children.Count;

        for (int i = 0; i < sortedItems.Count; i++)
        {
            LinkDest rr = sortedItems[i];
            if (BubbleExcludeTypes.Contains(rr.linkType.Label, StringComparer.OrdinalIgnoreCase)) continue;

            Link? existing = GetLink(parent, rr.linkType, rr.target);
            float currentWeight = existing?.Weight ?? 0f;
            float positiveCount = rr.links.FindAll(x => x.Weight > 0.5f).Count;
            float positiveWeight = rr.links.Sum(x => x.Weight);
            float negativeCount = 0;
            float negativeWeight = 0;

            for (int j = 0; j < sortedItems.Count; j++)
            {
                if (j == i) continue;
                if (BubbleLinksConflict(rr, sortedItems[j]))
                {
                    negativeCount += sortedItems[j].links.Count;
                    negativeWeight += sortedItems[j].links.Sum(x => x.Weight);
                }
            }

            float noInfoCount = totalCount - (positiveCount + negativeCount);
            positiveWeight += currentWeight + noInfoCount * 0.51f;
            if (noInfoCount < 0) noInfoCount = 0;

            if (negativeCount >= positiveCount)
            {
                if (existing is not null)
                {
                    parent.RemoveLink(existing);
                    RecordBubble("prune", parent, rr.linkType, rr.target);
                    changed = true;
                }
                continue;
            }

            float deltaWeight = positiveWeight - negativeWeight;
            float targetWeight = deltaWeight switch
            {
                < 0.8f => -0.1f,
                < 1.7f => 0.01f,
                < 2.7f => 0.2f,
                _ => 0.3f
            };
            if (currentWeight == 0) currentWeight = 0.5f;
            float newWeight = Math.Min(currentWeight + targetWeight, 0.99f);

            if (positiveCount <= totalCount * minFraction) continue;
            if (newWeight == currentWeight && existing is not null) continue;

            if (newWeight < 0.5f)
            {
                if (existing is not null)
                {
                    parent.RemoveLink(existing);
                    RecordBubble("prune", parent, rr.linkType, rr.target);
                    changed = true;
                }
                continue;
            }

            Link bubbled = parent.AddLink(rr.linkType, rr.target)!;
            bubbled.Weight = newWeight;
            bubbled.Fire();
            RecordBubble("bubble", parent, rr.linkType, rr.target);
            changed = true;

            foreach (Thought child in parent.Children)
                child.RemoveLink(rr.linkType, rr.target);

            for (int j = 0; j < parent.LinksTo.Count; j++)
            {
                Link parentLink = parent.LinksTo[j];
                if (BubbleLinksConflict(new LinkDest { linkType = rr.linkType, target = rr.target },
                        new LinkDest { linkType = parentLink.LinkType!, target = parentLink.To! }))
                {
                    parent.RemoveLink(parentLink);
                    j--;
                }
            }
        }

        return changed;
    }

    private void RecordBubble(string action, Thought parent, Thought linkType, Thought target)
    {
        bubbleLog.Add(new BubbleLogEntry
        {
            When = DateTime.UtcNow,
            ParentLabel = parent.Label,
            LinkTypeLabel = linkType.Label,
            TargetLabel = target.Label,
            Action = action
        });
        if (bubbleLog.Count > MaxBubbleLogEntries)
            bubbleLog.RemoveAt(0);
    }

    private static bool BubbleLinksConflict(LinkDest r1, LinkDest r2)
    {
        if (r1.linkType == r2.linkType && r1.target == r2.target) return false;
        if (r1.linkType == r2.linkType)
        {
            foreach (Thought parent in FindBubbleCommonParents(r1.target, r2.target))
                if (parent.HasProperty("isExclusive") || parent.HasProperty("allowMultiple")) return true;
        }
        if (r1.target == r2.target)
        {
            IReadOnlyList<Thought> r1Attribs = r1.linkType.GetAttributes();
            IReadOnlyList<Thought> r2Attribs = r2.linkType.GetAttributes();
            Thought? r1Not = r1Attribs.FindFirst(x => x.Label is "not" or "no");
            Thought? r2Not = r2Attribs.FindFirst(x => x.Label is "not" or "no");
            if (r1Not is null != (r2Not is null)) return true;

            foreach (Thought t1 in r1Attribs)
            {
                foreach (Thought t2 in r2Attribs)
                {
                    if (t1 == t2) continue;
                    foreach (Thought t3 in FindBubbleCommonParents(t1, t2))
                    {
                        if (t3.HasProperty("isexclusive") || t3.HasProperty("allowMultiple"))
                            return true;
                    }
                }
            }

            bool hasNumber1 = r1Attribs.Any(x => x.HasAncestor("number"));
            bool hasNumber2 = r2Attribs.Any(x => x.HasAncestor("number"));
            if (hasNumber1 || hasNumber2) return true;
        }
        return false;
    }

    private static List<Thought> FindBubbleCommonParents(Thought t, Thought t1)
    {
        List<Thought> common = new();
        foreach (Thought p in t.Parents)
            if (t1.Parents.Contains(p))
                common.Add(p);
        return common;
    }

    public static Thought GetBubbleInstanceType(Thought t)
    {
        Thought useLinkType = t;
        while (useLinkType.Parents.Count > 0 &&
               Regex.IsMatch(useLinkType.Label, @"\d+$") &&
               !t.Label.Contains('.') &&
               useLinkType.Label.StartsWith(useLinkType.Parents[0].Label))
            useLinkType = useLinkType.Parents[0];
        return useLinkType;
    }
}
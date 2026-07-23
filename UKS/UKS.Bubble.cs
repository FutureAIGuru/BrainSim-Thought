namespace UKS;

public partial class UKS
{
    /// <summary>
    /// Replaces every graph reference to <paramref name="redundantThought"/>
    /// with <paramref name="canonicalThought"/>, transfers the redundant
    /// Thought's relationships and reinforcement, then removes it.
    /// </summary>
    /// <returns>The number of links which were redirected.</returns>
    public int ReplaceThoughtReferences(Thought redundantThought, Thought canonicalThought)
    {
        ArgumentNullException.ThrowIfNull(redundantThought);
        ArgumentNullException.ThrowIfNull(canonicalThought);
        if (ReferenceEquals(redundantThought, canonicalThought)) return 0;

        // Links are stored on their source Thoughts. Scanning the atomic nodes
        // also finds sequence VLU links, wildcard constraints, memberships, and
        // ordinary incoming/outgoing relationships.
        List<Link> affectedLinks = AtomicThoughts
            .SelectMany(thought => thought.LinksTo)
            .Where(link => link.From == redundantThought ||
                link.LinkType == redundantThought || link.To == redundantThought)
            .Distinct()
            .ToList();

        int replacedCount = 0;
        foreach (Link oldLink in affectedLinks)
        {
            Thought? newSource = oldLink.From == redundantThought
                ? canonicalThought : oldLink.From;
            Thought? newType = oldLink.LinkType == redundantThought
                ? canonicalThought : oldLink.LinkType;
            Thought? newTarget = oldLink.To == redundantThought
                ? canonicalThought : oldLink.To;
            if (newSource is null || newType is null) continue;

            // Merging a parent with its child can otherwise manufacture A is-a A.
            bool selfInheritance = newType == Thought.IsA && newSource == newTarget;
            if (!selfInheritance)
            {
                Link? replacement = newSource.AddLink(newType, newTarget);
                if (replacement is not null)
                {
                    replacement.Weight = Math.Max(replacement.Weight, oldLink.Weight);
                    replacement.maxWeight = Math.Max(replacement.maxWeight, oldLink.maxWeight);
                    replacement.LastFiredTime = replacement.LastFiredTime > oldLink.LastFiredTime
                        ? replacement.LastFiredTime : oldLink.LastFiredTime;
                    replacement.TimeToLive = replacement.TimeToLive > oldLink.TimeToLive
                        ? replacement.TimeToLive : oldLink.TimeToLive;
                }
            }

            oldLink.From?.RemoveLink(oldLink);
            replacedCount++;
        }

        canonicalThought.Weight = Math.Max(canonicalThought.Weight, redundantThought.Weight);
        canonicalThought.maxWeight = Math.Max(canonicalThought.maxWeight, redundantThought.maxWeight);
        canonicalThought.LastFiredTime = canonicalThought.LastFiredTime > redundantThought.LastFiredTime
            ? canonicalThought.LastFiredTime : redundantThought.LastFiredTime;
        canonicalThought.TimeToLive = canonicalThought.TimeToLive > redundantThought.TimeToLive
            ? canonicalThought.TimeToLive : redundantThought.TimeToLive;
        canonicalThought.UseCount += redundantThought.UseCount;
        canonicalThought.V ??= redundantThought.V;
        redundantThought.Delete();
        return replacedCount;
    }

    /// <summary>
    /// Coalesces classes beneath one root when their ordinary direct-child sets
    /// have strong reciprocal overlap. Wildcards and sequence implementation
    /// nodes are excluded from the comparison.
    /// </summary>
    /// <returns>The number of redundant classes removed.</returns>
    public int CoalesceSimilarClasses(
        Thought classRoot,
        float minOverlapFraction = 0.8f,
        int minSharedChildren = 4)
    {
        ArgumentNullException.ThrowIfNull(classRoot);
        if (minOverlapFraction <= 0 || minOverlapFraction > 1)
            throw new ArgumentOutOfRangeException(nameof(minOverlapFraction));
        if (minSharedChildren < 1)
            throw new ArgumentOutOfRangeException(nameof(minSharedChildren));

        int mergeCount = 0;
        while (true)
        {
            List<Thought> classes = classRoot.Children.ToList();
            (Thought first, Thought second, float similarity)? bestPair = null;
            for (int firstIndex = 0; firstIndex < classes.Count - 1; firstIndex++)
            {
                HashSet<Thought> firstChildren = OrdinaryClassChildren(classes[firstIndex]);
                for (int secondIndex = firstIndex + 1; secondIndex < classes.Count; secondIndex++)
                {
                    HashSet<Thought> secondChildren = OrdinaryClassChildren(classes[secondIndex]);
                    int sharedCount = firstChildren.Intersect(secondChildren).Count();
                    if (sharedCount < minSharedChildren) continue;

                    // Dividing by the larger population requires both classes,
                    // not merely the smaller one, to be substantially shared.
                    float similarity = sharedCount /
                        (float)Math.Max(firstChildren.Count, secondChildren.Count);
                    if (similarity < minOverlapFraction) continue;
                    if (bestPair is null || similarity > bestPair.Value.similarity)
                        bestPair = (classes[firstIndex], classes[secondIndex], similarity);
                }
            }
            if (bestPair is null) break;

            // Preserve the older node as the canonical identity. This keeps
            // labels and existing external references as stable as possible.
            Thought first = bestPair.Value.first;
            Thought second = bestPair.Value.second;
            int firstAge = AtomicThoughts.IndexOf(first);
            int secondAge = AtomicThoughts.IndexOf(second);
            Thought canonical = firstAge <= secondAge ? first : second;
            Thought redundant = canonical == first ? second : first;
            ReplaceThoughtReferences(redundant, canonical);
            mergeCount++;
        }
        return mergeCount;
    }

    private static HashSet<Thought> OrdinaryClassChildren(Thought learnedClass)
    {
        return learnedClass.Children
            .Where(child => child is not SeqElement && !child.HasAncestor("Wildcard"))
            .ToHashSet();
    }

    public bool BubbleSharedAttributes(Thought parent, float minFraction = 0.6f)
    {
        if (parent is null || parent.Children.Count == 0) return false;
        if (parent.Label.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return false;

        Dictionary<(Thought linkType, Thought target), List<Link>> itemCounts = new();
        foreach (Thought child in parent.Children)
        {
            foreach (Link link in child.LinksTo)
            {
                if (link.LinkType is null || link.To is null || link.LinkType == Thought.IsA)
                    continue;
                if (link.LinkType.Label.Equals("hasProperty", StringComparison.OrdinalIgnoreCase))
                    continue;

                var key = (link.LinkType, link.To);
                if (!itemCounts.TryGetValue(key, out List<Link> links))
                {
                    links = new List<Link>();
                    itemCounts.Add(key, links);
                }
                links.Add(link);
            }
        }

        if (itemCounts.Count == 0) return false;
        var sortedItems = itemCounts.OrderByDescending(x => x.Value.Count).ToList();
        float totalCount = parent.Children.Count;
        bool changed = false;

        for (int i = 0; i < sortedItems.Count; i++)
        {
            var item = sortedItems[i];
            Thought linkType = item.Key.linkType;
            Thought target = item.Key.target;
            List<Link> links = item.Value;

            Link? existing = GetLink(parent, linkType, target);
            float currentWeight = existing?.Weight ?? 0f;
            float positiveCount = links.Count(x => x.Weight > 0.5f);
            float positiveWeight = links.Sum(x => x.Weight);
            float negativeCount = 0;
            float negativeWeight = 0;

            for (int j = 0; j < sortedItems.Count; j++)
            {
                if (j == i) continue;
                if (!LinksAreExclusive(links[0], sortedItems[j].Value[0])) continue;
                negativeCount += sortedItems[j].Value.Count;
                negativeWeight += sortedItems[j].Value.Sum(x => x.Weight);
            }

            float noInfoCount = totalCount - (positiveCount + negativeCount);
            positiveWeight += currentWeight + noInfoCount * 0.51f;
            if (noInfoCount < 0) noInfoCount = 0;

            if (negativeCount >= positiveCount)
            {
                if (existing is not null)
                {
                    parent.RemoveLink(existing);
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
                    changed = true;
                }
                continue;
            }

            Link bubbled = parent.AddLink(linkType, target)!;
            bubbled.Weight = newWeight;
            bubbled.Fire();
            changed = true;

            for (int j = 0; j < parent.LinksTo.Count; j++)
            {
                Link parentLink = parent.LinksTo[j];
                if (parentLink == bubbled || !LinksAreExclusive(bubbled, parentLink)) continue;
                parent.RemoveLink(parentLink);
                j--;
            }
        }

        return changed;
    }
}

/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License. You may use, copy, modify, merge, publish, distribute,
 * sublicense, and/or sell copies of this software under the terms of
 * the MIT License.
 *
 * See the LICENSE file in the project root for full license information.
 */

namespace UKS.Agents;

/// <summary>
/// Agents that reorganize the class hierarchy. An agent uses only the public UKS API; call one to run
/// it once, or schedule it with <see cref="UKS.RunPeriodically"/> (e.g. uks.RunPeriodically(uks.BubbleAttributes, ...)).
/// </summary>
public static class ClassAgents
{
    /// <summary>
    /// Bubbling: under Object and Unknown, when more than 60% of a parent's children share a link (same
    /// type and target), the parent takes the link and the children's copies are removed (they still
    /// inherit it).
    /// </summary>
    /// <returns>The parents whose links changed.</returns>
    public static IReadOnlyList<Thought> BubbleAttributes(this UKS store)
    {
        List<Thought> parents = Under(store, "Object").Concat(Under(store, "Unknown")).Distinct().ToList();
        List<Thought> changed = new();
        foreach (Thought parent in parents)
            if (BubbleSharedAttributes(store, parent))
                changed.Add(parent);
        return changed;
    }

    // A shared link is blocked if as many siblings hold a conflicting (exclusive) link.
    private static bool BubbleSharedAttributes(UKS store, Thought parent, float minFraction = 0.6f)
    {
        // A single child is no evidence of a shared attribute.
        if (parent.Children.Count < 2 || parent == store.Labeled("Unknown")) return false;

        // Every link held by a child, except inheritance and properties.
        Thought? isA = store.Labeled("is-a");
        Thought? hasProperty = store.Labeled("hasProperty");
        List<Link> candidates = parent.Children.SelectMany(child => child.LinksTo)
            .Where(link => link.LinkType is not null && link.To is not null && link.LinkType != isA && link.LinkType != hasProperty)
            .ToList();

        // Links with the same type and target, the most widely shared first.
        List<List<Link>> sharedLinks = candidates
            .GroupBy(link => (link.LinkType, link.To))
            .Select(group => group.ToList())
            .OrderByDescending(links => links.Count)
            .ToList();

        float threshold = parent.Children.Count * minFraction;
        bool changed = false;
        foreach (List<Link> links in sharedLinks)
        {
            if (links.Count <= threshold) continue;

            // Siblings holding a conflicting link (such as a different exclusive value) vote against it.
            int conflicting = sharedLinks
                .Where(other => other != links && store.LinksAreExclusive(links[0], other[0]))
                .Sum(other => other.Count);
            if (conflicting >= links.Count) continue;

            Link bubbled = parent.AddLink(links[0].LinkType!, links[0].To)!;
            bubbled.Weight = links.Average(link => link.Weight);
            foreach (Link link in links)
                link.From!.RemoveLink(link);

            foreach (Link conflict in parent.LinksTo.Where(link => link != bubbled && store.LinksAreExclusive(bubbled, link)).ToList())
                parent.RemoveLink(conflict);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Where a Thought links by one type to several targets with a common ancestor, adds a counted link
    /// to that ancestor (Fido has paw, has paw → Fido has.2 paw). Only ancestors under Unknown count.
    /// </summary>
    /// <returns>The links added.</returns>
    public static IReadOnlyList<Thought> AddCounts(this UKS store)
    {
        List<Thought> added = new();
        Thought? isA = store.Labeled("is-a");
        Thought? unknown = store.Labeled("Unknown");
        foreach (Thought t in AllThoughts(store))
        {
            for (int j = 0; j < t.LinksTo.Count; j++)
            {
                Link r = t.LinksTo[j];
                if (r.LinkType is not Thought linkType || linkType == isA) continue;

                // Count the ancestors shared by the targets of this link type.
                List<Thought> targets = t.LinksTo.Where(x => x.LinkType == linkType && x.To is not null).Select(x => x.To!).ToList();
                Dictionary<Thought, int> ancestorCounts = new();
                foreach (Thought target in targets)
                    foreach (Thought ancestor in target.Ancestors)
                        ancestorCounts[ancestor] = ancestorCounts.GetValueOrDefault(ancestor) + 1;

                foreach ((Thought ancestor, int count) in ancestorCounts)
                {
                    if (unknown is null || !ancestor.HasAncestor(unknown) || ancestor == unknown || count < 2) continue;
                    string countedLabel = linkType.Label + "." + count;
                    if (store.Labeled(countedLabel) is Thought existingType && store.GetLink(t, existingType, ancestor) is not null) continue;
                    Thought countedType = store.GetOrAddThought(countedLabel, linkType.Parents[0])!;
                    if (store.AddStatement(t, countedType, ancestor) is Link link)
                        added.Add(link);
                }
            }
        }
        return added;
    }

    /// <summary>
    /// When a Thought under Object has more than 6 children, moves them into new subclasses of at most 6.
    /// A Thought with several parents (such as a compound, dog.big) is left alone.
    /// </summary>
    /// <returns>The subclasses created.</returns>
    public static IReadOnlyList<Thought> BalanceTree(this UKS store)
    {
        const int maxChildren = 6;
        List<Thought> created = new();
        foreach (Thought t in Under(store, "Object").ToList())
        {
            if (t.Parents.Count > 1) continue;
            while (t.Children.Count > maxChildren)
            {
                Thought newParent = store.AddThought(t.Label, t);
                created.Add(newParent);
                while (newParent.Children.Count < maxChildren)
                {
                    Thought child = t.Children[0];
                    child.RemoveParent(t);
                    child.AddParent(newParent);
                }
            }
        }
        return created;
    }

    /// <summary>
    /// Among the children of each Thought under Object, finds groups (3 to 12 members) that share the
    /// same attributes and makes each group an anonymous subclass. Anonymous classes and Thoughts
    /// under Unknown are not divided.
    /// </summary>
    /// <returns>The classes created or reused.</returns>
    public static IReadOnlyList<Thought> CreateClasses(this UKS store)
    {
        List<Thought> classes = new();
        foreach (Thought t in Under(store, "Object").ToList())
        {
            if (!HasDirectProperty(store, t, "isAnonymousClass") && !t.HasAncestor("Unknown"))
                classes.AddRange(CreateClassesFromCommonAttributes(store, t));
        }
        return classes;
    }

    private static List<Thought> CreateClassesFromCommonAttributes(UKS store, Thought t)
    {
        const int maxChildren = 12;
        const int minClassMembers = 3;
        List<Thought> classes = new();

        // Work one taxonomy level at a time. Every parent is visited by the
        // agent, so scanning descendants here would repeatedly rediscover the
        // same population and create nested duplicate classes.
        List<Thought> candidates = t.Children
            .Where(child => child is not Link && !HasDirectProperty(store, child, "isAnonymousClass"))
            .ToList();
        if (candidates.Count < minClassMembers) return classes;

        // Record the member set supporting each attribute. Attributes with exactly the same
        // supporting population describe one discovered class, rather than one class per attribute.
        Thought? isA = store.Labeled("is-a");
        Thought? hasProperty = store.Labeled("hasProperty");
        Thought? hasImage = store.Labeled("hasImage");
        Dictionary<(Thought linkType, Thought target), HashSet<Thought>> attributes = new();
        foreach (Thought child in candidates)
        {
            foreach (Link link in child.LinksTo)
            {
                if (link.LinkType is null || link.To is null || link.LinkType == isA ||
                    link.LinkType == hasProperty || link.LinkType == hasImage || link.LinkType.HasProperty("isGrounding"))
                    continue;

                var key = (link.LinkType, link.To);
                if (!attributes.TryGetValue(key, out HashSet<Thought>? members))
                {
                    members = new HashSet<Thought>();
                    attributes.Add(key, members);
                }
                members.Add(child);
            }
        }

        List<HashSet<Thought>> groups = new();
        foreach (var item in attributes.Where(item => item.Value.Count >= minClassMembers && item.Value.Count <= maxChildren))
            if (!groups.Any(existing => existing.SetEquals(item.Value)))
                groups.Add(new HashSet<Thought>(item.Value));

        foreach (HashSet<Thought> members in groups.OrderByDescending(group => group.Count))
        {
            Thought? existingClass = t.Children.FirstOrDefault(child => child.Children.ToHashSet().SetEquals(members));
            Thought newParent = store.GetOrCreateThoughtClass(t, members, "class*");
            if (existingClass is null)
                newParent.AddProperty(store.GetOrAddThought("isAnonymousClass", "Property")!);
            foreach (Thought child in members)
                child.RemoveParent(t);
            classes.Add(newParent);
        }
        return classes;
    }

    /// <summary>
    /// Gets or creates a class under classRoot whose direct members are exactly the supplied ordinary
    /// Thoughts. Sequence elements and wildcards are not members.
    /// </summary>
    public static Thought GetOrCreateThoughtClass(this UKS store, Thought classRoot, IEnumerable<Thought> members, string classLabel = "class*")
    {
        static bool IsMember(Thought? t) => t is not null and not SeqElement && !UKS.IsWildcard(t);

        HashSet<Thought> memberSet = members.Where(IsMember).ToHashSet();
        if (memberSet.Count == 0)
            throw new ArgumentException("A learned class requires at least one ordinary Thought.", nameof(members));

        Thought learnedClass = classRoot.Children.FirstOrDefault(existing => existing.Children.Where(IsMember).ToHashSet().SetEquals(memberSet))
            ?? store.GetOrAddThought(classLabel, classRoot)
            ?? throw new InvalidOperationException("The learned class could not be created.");
        foreach (Thought member in memberSet)
            member.AddParent(learnedClass);
        return learnedClass;
    }

    // The root and everything under it.
    private static IEnumerable<Thought> Under(UKS store, string rootLabel) =>
        store.Labeled(rootLabel) is Thought root ? root.Descendants.Prepend(root) : Enumerable.Empty<Thought>();

    // Every Thought in the hierarchy, from the roots down.
    private static List<Thought> AllThoughts(UKS store) =>
        store.Roots().SelectMany(root => root.Descendants.Prepend(root)).Distinct().ToList();

    // A property held by the Thought itself, not inherited.
    private static bool HasDirectProperty(UKS store, Thought thought, string propertyLabel) =>
        store.Labeled("hasProperty") is Thought hasProperty && store.Labeled(propertyLabel) is Thought property &&
        thought.LinksTo.Any(link => link.LinkType == hasProperty && link.To == property);
}

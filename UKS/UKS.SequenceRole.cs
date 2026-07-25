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

namespace UKS;

/// <summary>
/// A non-persistent view which pairs the elements of a described sequence with
/// the roles those positions play. The two lists are the same length, so a
/// position may be read from either side. A null role means that position has
/// not been given one.
/// </summary>
public sealed class SlotRoleView
{
    internal SlotRoleView(
        Thought owner,
        Thought elementLinkType,
        IReadOnlyList<Thought> elements,
        IReadOnlyList<Thought?> roles)
    {
        Owner = owner;
        ElementLinkType = elementLinkType;
        Elements = elements;
        Roles = roles;
    }

    public Thought Owner { get; }
    public Thought ElementLinkType { get; }
    public IReadOnlyList<Thought> Elements { get; }
    public IReadOnlyList<Thought?> Roles { get; }

    public int Count => Elements.Count;
}

public partial class UKS
{
    /// <summary>
    /// The class beneath which discovered slot roles are created. Roles are
    /// anonymous when discovered; they acquire meaning by being bound to the
    /// argument positions of the actions their templates perform.
    /// </summary>
    public Thought GetSlotRoleRoot()
    {
        GetOrAddThought("LanguageElement", "Thought");
        return GetOrAddThought("SlotRole", "LanguageElement")
            ?? throw new InvalidOperationException("The slot role root could not be created.");
    }

    /// <summary>
    /// The placeholder written into a role sequence where a position has no
    /// role. It is deliberately not a member of SlotRole so that enumerating
    /// discovered roles never returns it.
    /// </summary>
    public Thought GetUnassignedRole()
    {
        GetOrAddThought("LanguageElement", "Thought");
        return GetOrAddThought("unassignedRole", "LanguageElement")
            ?? throw new InvalidOperationException("The unassigned role could not be created.");
    }

    /// <summary>
    /// Creates a new anonymous slot role.
    /// </summary>
    public Thought AddSlotRole(string roleLabel = "role*")
    {
        return GetOrAddThought(roleLabel, GetSlotRoleRoot())
            ?? throw new InvalidOperationException("The slot role could not be created.");
    }

    /// <summary>
    /// Writes an index-aligned role sequence beside an existing element
    /// sequence. Positions with no role are filled with the unassigned
    /// placeholder so that the two sequences stay the same length.
    /// </summary>
    public SeqElement? AssignSlotRoles(
        Thought owner,
        Thought elementLinkType,
        IReadOnlyList<Thought?> roles)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(elementLinkType);
        ArgumentNullException.ThrowIfNull(roles);

        SequenceView? elementView = GetSequenceViews(owner)
            .FirstOrDefault(view => view.LinkType == elementLinkType);
        if (elementView is null) return null;
        if (elementView.Elements.Count != roles.Count)
            throw new ArgumentException(
                "A role sequence must have one entry for each sequence element.", nameof(roles));
        if (roles.Count < 2) return null;

        Thought unassigned = GetUnassignedRole();
        List<Thought> roleElements = roles.Select(role => role ?? unassigned).ToList();
        Thought roleLinkType = GetOrAddThought("hasRoles", "LinkType")
            ?? throw new InvalidOperationException("The hasRoles link type could not be created.");

        // Rewriting a sequence abandons its existing elements, so an unchanged
        // assignment must leave the previous sequence in place. This keeps
        // repeated discovery passes from growing the UKS.
        SequenceView? existing = GetSequenceViews(owner)
            .FirstOrDefault(view => view.LinkType == roleLinkType);
        if (existing is not null && existing.Elements.SequenceEqual(roleElements))
            return existing.Sequence;

        return AddSequenceAndLink(owner, roleLinkType, roleElements);
    }

    /// <summary>
    /// Reads the elements of a sequence together with the roles assigned to
    /// their positions. Returns null when the owner describes no such sequence.
    /// </summary>
    public SlotRoleView? GetSlotRoleView(Thought owner, Thought elementLinkType)
    {
        if (owner is null || elementLinkType is null) return null;

        List<SequenceView> views = GetSequenceViews(owner);
        SequenceView? elementView = views.FirstOrDefault(view => view.LinkType == elementLinkType);
        if (elementView is null) return null;

        Thought? roleLinkType = Labeled("hasRoles");
        SequenceView? roleView = roleLinkType is null
            ? null
            : views.FirstOrDefault(view => view.LinkType == roleLinkType);

        Thought? unassigned = Labeled("unassignedRole");
        List<Thought?> roles = new();
        for (int position = 0; position < elementView.Elements.Count; position++)
        {
            Thought? role = roleView is not null && position < roleView.Elements.Count
                ? roleView.Elements[position]
                : null;
            roles.Add(role == unassigned ? null : role);
        }
        return new SlotRoleView(owner, elementLinkType, elementView.Elements, roles);
    }

    /// <summary>
    /// Merges slot roles whose supplied populations substantially overlap, then
    /// rewrites every reference to the redundant role. The populations are
    /// computed by the caller because what fills a role is specific to the kind
    /// of sequence being described.
    /// </summary>
    /// <returns>The number of redundant roles removed.</returns>
    public int CoalesceSimilarRoles(
        IDictionary<Thought, HashSet<Thought>> populationsByRole,
        float minOverlapFraction = 0.5f,
        int minSharedMembers = 3)
    {
        ArgumentNullException.ThrowIfNull(populationsByRole);
        if (minOverlapFraction <= 0 || minOverlapFraction > 1)
            throw new ArgumentOutOfRangeException(nameof(minOverlapFraction));
        if (minSharedMembers < 1)
            throw new ArgumentOutOfRangeException(nameof(minSharedMembers));

        int mergeCount = 0;
        while (true)
        {
            List<Thought> roles = populationsByRole.Keys.ToList();
            (Thought first, Thought second, float similarity)? bestPair = null;
            for (int firstIndex = 0; firstIndex < roles.Count - 1; firstIndex++)
            {
                HashSet<Thought> firstPopulation = populationsByRole[roles[firstIndex]];
                for (int secondIndex = firstIndex + 1; secondIndex < roles.Count; secondIndex++)
                {
                    HashSet<Thought> secondPopulation = populationsByRole[roles[secondIndex]];
                    int sharedCount = firstPopulation.Intersect(secondPopulation).Count();
                    if (sharedCount < minSharedMembers) continue;

                    // Dividing by the larger population keeps a small
                    // specialized population from being absorbed merely because
                    // it is a subset of a much larger one.
                    float similarity = sharedCount /
                        (float)Math.Max(firstPopulation.Count, secondPopulation.Count);
                    if (similarity < minOverlapFraction) continue;
                    if (bestPair is null || similarity > bestPair.Value.similarity)
                        bestPair = (roles[firstIndex], roles[secondIndex], similarity);
                }
            }
            if (bestPair is null) break;

            Thought first = bestPair.Value.first;
            Thought second = bestPair.Value.second;
            int firstAge = AtomicThoughts.IndexOf(first);
            int secondAge = AtomicThoughts.IndexOf(second);
            Thought canonical = firstAge <= secondAge ? first : second;
            Thought redundant = canonical == first ? second : first;

            populationsByRole[canonical].UnionWith(populationsByRole[redundant]);
            populationsByRole.Remove(redundant);
            ReplaceThoughtReferences(redundant, canonical);
            mergeCount++;
        }
        return mergeCount;
    }
}

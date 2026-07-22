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
/// A non-persistent view of a sequence and the relationship through which its
/// owner describes it. The owner, rather than any <see cref="SeqElement"/>, is
/// the item which can become a member of a learned class.
/// </summary>
public sealed class SequenceView
{
    internal SequenceView(Thought owner, Link ownerLink, SeqElement sequence, IReadOnlyList<Thought> elements)
    {
        Owner = owner;
        OwnerLink = ownerLink;
        Sequence = sequence;
        Elements = elements;
    }

    public Thought Owner { get; }
    public Link OwnerLink { get; }
    public Thought? LinkType => OwnerLink.LinkType;
    public SeqElement Sequence { get; }
    public IReadOnlyList<Thought> Elements { get; }
}

public partial class UKS
{
    /// <summary>
    /// Gets every sequence directly described by an owner, regardless of the
    /// relationship name. This provides the same view for phrases, spellings,
    /// class descriptions, and other sequence-bearing Thoughts.
    /// </summary>
    public List<SequenceView> GetSequenceViews(Thought owner)
    {
        if (owner is null) return new List<SequenceView>();

        List<SequenceView> views = new();
        foreach (Link link in owner.LinksTo)
        {
            if (link.To is not SeqElement target) continue;
            SeqElement sequence = target.FRST ?? target;
            views.Add(new SequenceView(owner, link, sequence, FlattenSequence(sequence)));
        }
        return views;
    }

    /// <summary>
    /// Gets the sequence views directly described by each supplied owner.
    /// </summary>
    public List<SequenceView> GetSequenceViews(IEnumerable<Thought> owners)
    {
        if (owners is null) return new List<SequenceView>();
        return owners.Where(owner => owner is not null)
            .SelectMany(GetSequenceViews)
            .ToList();
    }

    /// <summary>
    /// Gets or creates an anonymous class whose direct members are exactly the
    /// supplied ordinary Thoughts. Sequence implementation nodes and wildcards
    /// are not treated as learned members.
    /// </summary>
    public Thought GetOrCreateThoughtClass(
        Thought classRoot,
        IEnumerable<Thought> members,
        string classLabel = "class*")
    {
        ArgumentNullException.ThrowIfNull(classRoot);
        ArgumentNullException.ThrowIfNull(members);

        HashSet<Thought> memberSet = members
            .Where(member => member is not null && member is not SeqElement && !member.HasAncestor("Wildcard"))
            .ToHashSet();
        if (memberSet.Count == 0)
            throw new ArgumentException("A learned class requires at least one ordinary Thought.", nameof(members));

        Thought? learnedClass = classRoot.Children.FirstOrDefault(existing =>
        {
            HashSet<Thought> existingMembers = existing.Children
                .Where(member => member is not SeqElement && !member.HasAncestor("Wildcard"))
                .ToHashSet();
            return existingMembers.SetEquals(memberSet);
        });

        learnedClass ??= GetOrAddThought(classLabel, classRoot)
            ?? throw new InvalidOperationException("The learned class could not be created.");

        foreach (Thought member in memberSet)
            member.AddParent(learnedClass);

        return learnedClass;
    }

    /// <summary>
    /// Gets or creates a class for sequence observations. Only the owners of
    /// those observations become members; raw sequence nodes remain internal.
    /// </summary>
    public Thought GetOrCreateSequenceClass(
        Thought classRoot,
        IEnumerable<SequenceView> observations,
        string classLabel = "class*")
    {
        ArgumentNullException.ThrowIfNull(observations);
        return GetOrCreateThoughtClass(classRoot, observations.Select(observation => observation.Owner), classLabel);
    }
}

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
/// A link: a Thought that connects three others, its source (<see cref="From"/>), its type
/// (<see cref="LinkType"/>) and its target (<see cref="To"/>), as in [Fido→is-a→dog]. Because a link
/// is a Thought, it can itself be the source or target of another link (a nested statement).
/// </summary>
/// <remarks>
/// A link built with a constructor is not stored: use it as a pattern (<see cref="UKS.FindLinks"/>,
/// <see cref="UKS.ApplyTestOrSetAction"/>). Store a link with <see cref="UKS.AddStatement"/> or
/// <see cref="Thought.AddLink"/>.
/// </remarks>
public class Link : Thought
{
    /// <summary>Creates an unconnected, unstored link in the store.</summary>
    public Link(UKS store) : base(store) { }

    /// <summary>Creates an unstored link [from → linkType → to], e.g. a query pattern.</summary>
    /// <exception cref="ArgumentException">All three are null.</exception>
    public Link(Thought? from, Thought? linkType, Thought? to)
        : base((from ?? linkType ?? to)?.Store ?? throw new ArgumentException("A link needs at least one Thought."))
    {
        From = from;
        LinkType = linkType;
        To = to;
    }

    /// <summary>Creates an unstored copy of a link; nested links are copied too.</summary>
    public Link(Link l) : base(l.Store)
    {
        if (l.From is Link l1)
            From = new(l1);
        else
            From = l.From;
        if (l.LinkType is Link l2)
            LinkType = new(l2);
        else
            LinkType = l.LinkType;
        if (l.To is Link l3)    
            To = new(l3);   
        else
            To = l.To;
    }

    private Thought? from;

    private Thought? linkType;

    private Thought? to;

    /// <summary>The source of the link.</summary>
    public Thought? From
    {
        get => from;
        set
        {
            if (value is not null && !ReferenceEquals(value.Store, Store))
                throw new ArgumentException("A Link's source belongs to another UKS.", nameof(value));
            from = value;
        }
    }

    /// <summary>The type of the link (is-a, has, ...).</summary>
    public Thought? LinkType
    {
        get => linkType;
        set
        {
            if (value is not null && !ReferenceEquals(value.Store, Store))
                throw new ArgumentException("A Link's type belongs to another UKS.", nameof(value));
            linkType = value;
        }
    }

    /// <summary>The target of the link; may be null.</summary>
    public Thought? To
    {
        get => to;
        set
        {
            if (value is not null && !ReferenceEquals(value.Store, Store))
                throw new ArgumentException("A Link's target belongs to another UKS.", nameof(value));
            to = value;
        }
    }

    /// <summary>The link as text: [From→LinkType→To], preceded by its label if it has a name.</summary>
    public override string ToString()
    {
        string retVal = (HasGeneratedLabel ? "" : Label) + "[";
        if (From is not null) retVal += From.ToString();
        if (LinkType is not null) retVal += ((retVal == "") ? "" : "→") + LinkType.ToString();
        if (To is not null) retVal += ((retVal == "") ? "" : "→") + To.ToString();
        retVal += "]";
        return retVal;
    }

    /// <summary>
    /// True if the two are the same Thought, or Links making the same connection: the same source,
    /// link type and target, where a source or target that is itself a Link (a nested statement) is
    /// compared the same way. A store keeps one link per connection, so this finds the stored link
    /// that an unstored one (built for a query or a pattern) stands for.
    /// </summary>
    internal static bool SameConnection(Thought? a, Thought? b)
    {
        if (ReferenceEquals(a, b)) return true;
        return a is Link la && b is Link lb && ReferenceEquals(la.LinkType, lb.LinkType) &&
            SameConnection(la.From, lb.From) && SameConnection(la.To, lb.To);
    }
}

/*
 * BrainSim3 compatibility wrapper over UKS Link (Thought/Link model).
 */

namespace UKS;

/// <summary>Legacy BrainSim3 relationship view of a <see cref="Link"/>.</summary>
public sealed class Relationship
{
    public Link Link { get; }

    public Relationship(Link link) => Link = link;

    public Thought? target => Link?.To;
    public Thought? relType => Link?.LinkType;
    public Thought? reltype => Link?.LinkType;

    public float Weight
    {
        get => Link?.Weight ?? 0f;
        set { if (Link is not null) Link.Weight = value; }
    }

    public TimeSpan TimeToLive
    {
        get => Link?.TimeToLive ?? TimeSpan.MaxValue;
        set { if (Link is not null) Link.TimeToLive = value; }
    }

    public static Relationship? FromLink(Link? link) => link is null ? null : new Relationship(link);

    public static implicit operator Relationship?(Link? link) => FromLink(link);
}
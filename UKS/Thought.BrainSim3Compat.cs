/*
 * BrainSim3 / Vision API compatibility on Thought (maps to Link graph).
 */

namespace UKS;

public partial class Thought
{
    /// <summary>Legacy casing alias for <see cref="LastFiredTime"/>.</summary>
    public DateTime lastFiredTime
    {
        get => LastFiredTime;
        set => LastFiredTime = value;
    }

    public IReadOnlyList<Relationship> Relationships =>
        LinksTo.Select(r => Relationship.FromLink(r)).Where(r => r is not null).Cast<Relationship>().ToList();

    public IReadOnlyList<Relationship> RelationshipsFrom =>
        LinksFrom.Select(r => Relationship.FromLink(r)).Where(r => r is not null).Cast<Relationship>().ToList();

    public void SetFired() => Fire();

    public bool HasAncestorLabeled(string label)
    {
        Thought? ancestor = UKS.theUKS?.Labeled(label);
        return ancestor is not null && HasAncestor(ancestor);
    }

    public Thought? GetAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (Link r in LinksTo)
        {
            if (r.LinkType?.Label is not ("hasAttribute" or "is")) continue;
            if (r.To is null) continue;
            if (string.Equals(r.To.Label, name, StringComparison.OrdinalIgnoreCase))
                return r.To;
            foreach (Thought parent in r.To.Parents)
            {
                if (string.Equals(parent.Label, name, StringComparison.OrdinalIgnoreCase))
                    return r.To;
            }
            if (r.To.HasAncestorLabeled(name))
                return r.To;
        }
        return null;
    }

    public Relationship? SetAttribute(Thought attribute)
    {
        if (attribute is null || UKS.theUKS is null) return null;
        Thought hasAttribute = UKS.theUKS.GetOrAddThought("hasAttribute", "LinkType");
        foreach (Link existing in LinksTo.ToList())
        {
            if (existing.LinkType?.Label != "hasAttribute") continue;
            if (existing.To is null) continue;
            bool sameCategory = existing.To.Parents.Any(p => attribute.Parents.Contains(p))
                || existing.To.Label == attribute.Label;
            if (sameCategory)
                RemoveLink(existing);
        }
        Link link = UKS.theUKS.AddStatement(this, hasAttribute, attribute);
        return Relationship.FromLink(link);
    }

    public void AddRelationship(Thought target, string linkTypeLabel)
    {
        if (target is null || UKS.theUKS is null) return;
        Thought linkType = UKS.theUKS.GetOrAddThought(linkTypeLabel, "LinkType");
        UKS.theUKS.AddStatement(this, linkType, target);
    }

    public void AddRelationship(string targetLabel, string linkTypeLabel)
    {
        if (UKS.theUKS is null) return;
        Thought target = UKS.theUKS.GetOrAddThought(targetLabel, "Object");
        AddRelationship(target, linkTypeLabel);
    }

    public List<Relationship> GetRelationshipsWithAncestor(Thought ancestor)
    {
        List<Relationship> retVal = new();
        if (ancestor is null) return retVal;
        foreach (Link r in LinksTo)
            if (r.To?.HasAncestor(ancestor) == true)
            {
                var wrapped = Relationship.FromLink(r);
                if (wrapped is not null) retVal.Add(wrapped);
            }
        return retVal;
    }

    public Relationship? HasRelationshipWithAncestorLabeled(string label)
    {
        Thought? ancestor = UKS.theUKS?.Labeled(label);
        if (ancestor is null) return null;
        Link? link = LinksTo.FindFirst(r => r.To?.HasAncestor(ancestor) == true);
        return Relationship.FromLink(link);
    }

    public void AddRelationship(Thought target, Thought linkType) =>
        UKS.theUKS?.AddStatement(this, linkType, target);
}
namespace UKS;

public partial class UKS
{
    /// <summary>
    /// Applies a SET action or evaluates a TEST action. The operation and its
    /// underlying relationship type are identified through inheritance.
    /// </summary>
    /// <returns>The written relationship for SET, or all matching relationships for TEST.</returns>
    public List<Link> ApplyTestOrSetAction(Link action)
    {
        //THIS PROBABLY DOESN"T WORK ON COMPOUND ACTIONS, NEED TO TEST AND FIX
        ArgumentNullException.ThrowIfNull(action);
        List<Link> retVal = new();
        Thought? actionType = action.LinkType;
        if (actionType is null) return retVal;

        if (actionType.HasAncestor("SET"))
        {
            Link? result = ApplySetAction(action);
            if (result is not null) retVal.Add(result);
            return retVal;
        }

        if (actionType.HasAncestor("TEST"))
        {
            retVal = ApplyTestAction(action);
        }
        return retVal;
    }

    /// <summary>Evaluates an action whose relationship type inherits from TEST.</summary>
    /// <returns>All ordinary relationships which satisfy the TEST action.</returns>
    private List<Link> ApplyTestAction(Link action)
    {
        ArgumentNullException.ThrowIfNull(action);
        List<Link> retVal = new();
        Thought? actionType = action.LinkType;
        if (actionType?.HasAncestor("TEST") != true) return retVal;

        Thought? relationshipType = GetActionRelationshipType(actionType);
        if (relationshipType is null || action.From is null || action.To is null) return retVal;

        Link query = new(action.From, relationshipType, action.To);
        bool hasWildcard = action.From.Label.Contains("??") || relationshipType.Label.Contains("??") ||
            action.To.Label.Contains("??");
        if (!hasWildcard)
        {
            Link? existing = GetLink(query);
            if (existing is not null) retVal.Add(existing);
            return retVal;
        }

        Thought? filterTarget = action.GetTargetOfFirstLinkOfType("filterBy");
        if (filterTarget is not null)
        {
            Thought? filterBy = GetOrAddThought("filterBy", "Property");
            if (filterBy is not null) query.AddLink(filterBy, filterTarget);
        }
        retVal = SearchForRelationships(query);
        return retVal;
    }

    /// <summary>
    /// Applies an action whose relationship type inherits from SET. The
    /// underlying relationship type is obtained from the action type's ancestry.
    /// </summary>
    /// <returns>The asserted or reinforced ordinary relationship.</returns>
    private Link? ApplySetAction(Link action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Link? retVal = null;
        if (action.From is null || action.LinkType is null ||
            !action.LinkType.HasAncestor("SET"))
            return retVal;

        Thought? newLinkType = GetActionRelationshipType(action.LinkType);
        if (newLinkType is null) return retVal;

        Link? existing = GetLink(action.From, newLinkType, action.To);
        if (existing is not null)
        {
            existing.Fire();
            retVal = existing;
        }
        else
        {
            if (newLinkType.HasProperty("isExclusive"))
                action.From.RemoveLinks(newLinkType);
            retVal = AddStatement(action.From, newLinkType, action.To);
        }
        return retVal;
    }

    /// <summary>Gets the ordinary relationship type inherited by a SET or TEST action type.</summary>
    public Thought? GetActionRelationshipType(Thought? actionType)
    {
        Thought? retVal = null;
        if (actionType is null) return retVal;

        Thought? setRoot = Labeled("SET");
        Thought? testRoot = Labeled("TEST");
        Thought? linkTypeRoot = Labeled("LinkType");
        retVal = actionType.Parents.FirstOrDefault(parent =>
            parent != setRoot && parent != testRoot && parent != linkTypeRoot &&
            linkTypeRoot is not null && parent.HasAncestor(linkTypeRoot) &&
            (setRoot is null || !parent.HasAncestor(setRoot)) &&
            (testRoot is null || !parent.HasAncestor(testRoot)));
        return retVal;
    }
}

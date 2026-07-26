namespace UKS;

public partial class UKS
{
    /// <summary>
    /// Applies an action whose relationship type inherits from SET. The dotted
    /// SET type identifies the underlying relationship through its "is" link,
    /// for example SET.is-a describes an is-a statement.
    /// </summary>
    /// <returns>The asserted or reinforced ordinary relationship.</returns>
    public Link? ApplySetAction(Link action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Link? retVal = null;
        if (action.From is null || action.LinkType is null ||
            !action.LinkType.HasAncestor("SET"))
            return retVal;

        // SET.ref inherits from both SET and ref. The non-SET LinkType parent
        // is therefore the relationship which this action writes.
        Thought? newLinkType = GetActionRelationship(action.LinkType, "SET");
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

    /// <summary>
    /// Applies an action whose relationship type inherits from TEST. It is the
    /// counterpart of <see cref="ApplySetAction"/>: where SET writes a
    /// relationship, TEST reads one, and changes nothing.
    ///
    /// A wildcard in the source or the target leaves that end open, so the
    /// action asks which Thoughts stand in the relationship rather than whether
    /// two particular ones do. With neither end open the result is empty when
    /// the relationship is absent, which is how a plain test reads as false.
    /// </summary>
    /// <returns>The relationships which satisfy the action; never null.</returns>
    public List<Link> ApplyTestAction(Link action)
    {
        ArgumentNullException.ThrowIfNull(action);
        List<Link> retVal = new();
        if (action.LinkType is null || !action.LinkType.HasAncestor("TEST"))
            return retVal;

        Thought? relationship = GetActionRelationship(action.LinkType, "TEST");
        if (relationship is null) return retVal;

        // SearchForRelationships treats a "??" label as an open end, which is the
        // same convention the learned templates use for an unfilled position.
        Link query = new()
        {
            From = action.From,
            LinkType = relationship,
            To = action.To,
        };
        retVal = SearchForRelationships(query);
        return retVal;
    }

    /// <summary>
    /// Finds the ordinary relationship an action type writes or reads. A dotted
    /// action type such as SET.is-a or TEST.is-a inherits from both the action
    /// root and the relationship, so the relationship is the parent which is
    /// neither.
    /// </summary>
    private Thought? GetActionRelationship(Thought actionType, string actionRootLabel)
    {
        Thought? actionRoot = Labeled(actionRootLabel);
        Thought? linkTypeRoot = Labeled("LinkType");
        if (linkTypeRoot is null) return null;
        return actionType.Parents.FirstOrDefault(parent =>
            parent != actionRoot &&
            parent != linkTypeRoot &&
            parent.HasAncestor(linkTypeRoot) &&
            (actionRoot is null || !parent.HasAncestor(actionRoot)));
    }
}

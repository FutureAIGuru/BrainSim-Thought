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
        Thought? setRoot = Labeled("SET");
        Thought? linkTypeRoot = Labeled("LinkType");
        Thought? newLinkType = action.LinkType.Parents.FirstOrDefault(parent =>
            parent != setRoot &&
            parent != linkTypeRoot &&
            linkTypeRoot is not null &&
            parent.HasAncestor(linkTypeRoot) &&
            (setRoot is null || !parent.HasAncestor(setRoot)));
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
}

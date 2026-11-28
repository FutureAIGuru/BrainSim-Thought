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

public partial class UKS
{
    /// <summary>
    /// Applies a write action or evaluates a query action. The operation and its
    /// underlying link type are identified through inheritance.
    /// </summary>
    /// <returns>The written link for writes, or all matching links for ?.</returns>
    public IReadOnlyList<Link> ApplyTestOrSetAction(Link action)
    {
        // TODO: compound (nested) actions are untested.
        ArgumentNullException.ThrowIfNull(action);
        List<Link> retVal = new();
        Thought? actionType = action.LinkType;
        if (actionType is null) return retVal;

        if (actionType.HasProperty("setRelationship"))
        {
            Link? result = ApplySetAction(action);
            if (result is not null) retVal.Add(result);
            return retVal;
        }

        if (actionType.HasProperty("isPossible"))
        {
            retVal = ApplyTestAction(action);
        }
        return retVal;
    }

    /// <summary>Evaluates an action whose link type inherits from ?.</summary>
    /// <returns>All ordinary links which satisfy the query action.</returns>
    private List<Link> ApplyTestAction(Link action)
    {
        ArgumentNullException.ThrowIfNull(action);
        List<Link> retVal = new();
        Thought? actionType = action.LinkType;
        if (actionType?.HasProperty("isPossible") != true) return retVal;

        Thought? relationshipType = GetPlainLinkType(actionType);
        if (relationshipType is null || action.From is null || action.To is null) return retVal;

        Link query = new(action.From, relationshipType, action.To);
        bool hasWildcard = new[] { action.From, relationshipType, action.To }.Any(thought =>
            IsWildcard(thought));
        if (!hasWildcard)
        {
            // is-a is not among a Thought's attributes; it is answered from the stored link.
            if (relationshipType == IsAType)
            {
                if (GetLink(query) is Link existing) retVal.Add(existing);
                return retVal;
            }
            // Answered from everything known about the subject (inheritance, conflicts, rules whose
            // conditions are met), not only a link stored on it. The type must be the one asked
            // about: is.past is not an answer to is.
            retVal.AddRange(GetAttributes(action.From).Where(link =>
                link.To == action.To && link.LinkType == relationshipType));
            return retVal;
        }

        Thought? filterTarget = action.HasLink("filterBy")?.To;
        if (filterTarget is not null)
        {
            Thought? filterBy = GetOrAddThought("filterBy", "Property");
            if (filterBy is not null) query.AddLink(filterBy, filterTarget);
        }
        retVal = FindLinks(query).ToList();
        return retVal;
    }

    /// <summary>
    /// Applies an action whose link type inherits from !. The
    /// underlying link type is obtained from the action type's ancestry.
    /// </summary>
    /// <returns>The asserted or reinforced ordinary link.</returns>
    private Link? ApplySetAction(Link action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Link? retVal = null;
        if (action.From is null || action.LinkType is null ||
            !action.LinkType.HasProperty("setRelationship"))
            return retVal;

        Thought? newLinkType = GetPlainLinkType(action.LinkType);
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
                foreach (Link old in action.From.LinksTo.Where(link => link.LinkType == newLinkType))
                    action.From.RemoveLink(old);
            // A value displacing one whose history is recorded archives it first (UKS.History.cs).
            retVal = action.To is null ? AddStatement(action.From, newLinkType, action.To)
                : AddStatementRecordingHistory(action.From, newLinkType, action.To);
        }
        return retVal;
    }

    /// <summary>
    /// The ordinary link type a SET (TYPE.!) or query (TYPE.?) type stands for: its most specific parent
    /// that is neither a marker nor a root; null if it has none.
    /// </summary>
    public Thought? GetPlainLinkType(Thought? linkType)
    {
        if (linkType is null) return null;
        Thought? linkTypeRoot = Labeled("LinkType"), propertyRoot = Labeled("Property");
        List<Thought> candidates = linkType.Parents.Where(parent =>
            parent != linkTypeRoot && parent != propertyRoot &&
            !parent.HasProperty("setRelationship") && !parent.HasProperty("isPossible"))
            .ToList();
        return candidates.FirstOrDefault(parent => candidates.All(other => parent.HasAncestor(other)))
            ?? candidates.FirstOrDefault();
    }
}

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
    public List<Link> ApplyTestOrSetAction(Link action)
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

        Thought? relationshipType = GetActionRelationshipType(actionType);
        if (relationshipType is null || action.From is null || action.To is null) return retVal;

        Link query = new(action.From, relationshipType, action.To);
        bool hasWildcard = new[] { action.From, relationshipType, action.To }.Any(thought =>
            HasWildcardProperty(thought));
        if (!hasWildcard)
        {
            // is-a is not among a Thought's attributes; it is answered from the stored link.
            if (relationshipType.Label == "is-a")
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
            // A value displacing one whose history is recorded archives it first (UKS.History.cs).
            retVal = action.To is null ? AddStatement(action.From, newLinkType, action.To)
                : AddStatementRecordingHistory(action.From, newLinkType, action.To);
        }
        return retVal;
    }

    /// <summary>Gets the ordinary link type inherited by a SET or query action type.</summary>
    public Thought? GetActionRelationshipType(Thought? actionType)
    {
        Thought? retVal = null;
        if (actionType is null) return retVal;

        Thought? linkTypeRoot = Labeled("LinkType");
        List<Thought> relationshipParents = actionType.Parents.Where(parent =>
            parent != linkTypeRoot && !ReferenceEquals(parent, Labeled("Property")) &&
            !parent.HasProperty("setRelationship") && !parent.HasProperty("isPossible"))
            .ToList();
        retVal = relationshipParents.FirstOrDefault(parent =>
            relationshipParents.All(other => parent.HasAncestor(other))) ?? relationshipParents.FirstOrDefault();
        return retVal;
    }
}

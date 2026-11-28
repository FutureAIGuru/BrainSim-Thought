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
using System.Globalization;

namespace UKS;

// History: a statement that displaces a value of a recordsHistory type or target archives the old
// value as was.TYPE in the source's events sequence before removing it.
public partial class UKS
{
    /// <summary>
    /// Adds the statement [from → type → to] like <see cref="AddStatement"/>, first keeping the history of
    /// the values it displaces. When the type or the target has the recordsHistory property, each statement
    /// of the source that is exclusive with the new one (<see cref="LinksAreExclusive"/>) is archived as a
    /// was.TYPE link at the head of the source's events sequence (newest first, with a validUntil time)
    /// and then removed, so only the new value remains in effect.
    /// </summary>
    /// <returns>The added (or reinforced) statement, or null if the source or type is empty.</returns>
    public Link? AddStatementRecordingHistory(ThoughtRef from, ThoughtRef type, ThoughtRef to)
    {
        Thought? source = from.GetOrAdd(this, "Unknown");
        Thought? linkType = source is null ? null : type.GetOrAdd(this, "LinkType", source);
        if (source is null || linkType is null) return null;
        Thought? target = to.GetOrAdd(this, "Unknown");
        var pastMarker = Labeled("was");
        if (pastMarker?.HasProperty("isHistorical") != true)
        {
            pastMarker = GetOrAddThought("was", "LinkType")!;
            pastMarker.AddLink("hasProperty", GetOrAddThought("isHistorical", "Property"));
        }

        bool conditional = IsClauseLinkType(linkType);
        var historyProperty = Labeled("recordsHistory");
        if (!conditional && source is not Link && !linkType.HasProperty("isPossible") && !linkType.HasProperty("isHistorical") &&
            historyProperty is not null && target is not null &&
            (linkType.HasProperty(historyProperty) || target.HasProperty(historyProperty)))
        {
            var incoming = new Link(source, linkType, target);
            var conflicts = source.LinksTo.Where(previous => previous.LinkType is not null && previous.To is not null &&
                !previous.LinksTo.Any(clause => IsConditionalLinkType(clause.LinkType)) &&
                LinksAreExclusive(incoming, previous)).ToArray();
            if (conflicts.Length > 0)
            {
                var effective = GetAttributes(source);
                var displaced = conflicts.Where(previous => effective.Any(result =>
                    result.LinkType == previous.LinkType && result.To == previous.To)).ToArray();
                if (displaced.Length > 0)
                {
                    var events = GetOrAddThought("events", "LinkType")!;
                    var validUntil = GetOrAddThought("validUntil", "LinkType")!;
                    var timestamp = GetOrAddThought(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), "EventTime")!;
                    var head = source.HasLink(events)?.To as SeqElement;
                    var pastValues = new Dictionary<(Thought Type, Thought Target), Link>();
                    var visited = new HashSet<SeqElement>(ReferenceEqualityComparer.Instance);
                    for (var cell = head; cell is not null && visited.Add(cell); cell = cell.NXT)
                    {
                        if (cell.VLU is Link { LinkType: { } historicalType, To: { } targetThought } value &&
                            ReferenceEquals(value.From, source))
                            pastValues.TryAdd((historicalType, targetThought), value);
                    }
                    foreach (var previous in displaced)
                    {
                        // A repeat visit gets a new sequence cell pointing to the same link.
                        var previousType = previous.LinkType!;
                        var pastType = GetOrAddThought($"was.{previousType.Label}", "LinkType")!;
                        pastType.AddParent(pastMarker);
                        pastType.AddParent(previousType);
                        var key = (pastType, previous.To!);
                        if (!pastValues.TryGetValue(key, out var archived))
                        {
                            archived = source.HasLink(pastType, previous.To) ??
                                new Link(source, pastType, previous.To!) { Weight = previous.Weight };
                            archived.AddDefaultLabel();
                            pastValues.Add(key, archived);
                        }
                        if (head is null)
                        {
                            head = CreateFirstElement(source.Label, archived);
                            source.AddLink(events, head);
                        }
                        else
                        {
                            // Inserting at the beginning keeps the head identity and moves its old VLU to NXT.
                            // Move the occurrence's timestamp with that value.
                            var previousEnd = head.HasLink(validUntil)?.To;
                            InsertAfter(head, archived, atBeginning: true);
                            if (previousEnd is not null)
                            {
                                head.RemoveLink(validUntil, previousEnd);
                                head.NXT!.AddLink(validUntil, previousEnd);
                            }
                        }
                        head.AddLink(validUntil, timestamp);
                    }
                    // Suppressed alternatives were already out of effect.
                    foreach (var conflict in conflicts) source.RemoveLink(conflict);
                }
            }
        }

        return AddStatement(source, linkType, target);
    }
}

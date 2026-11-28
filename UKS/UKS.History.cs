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

public partial class UKS
{
    /// <summary>
    /// Adds a statement, first recording the history of the values it displaces (UKSWeb Lab 4).
    /// When the link type or the target has the recordsHistory property and the new statement
    /// is exclusive with statements the source holds, each displaced statement that is in effect is
    /// archived as a was.TYPE link (inheriting was, which isHistorical, and the original type)
    /// at the head of the source's events sequence, newest first, with a validUntil time. A repeat
    /// visit adds a new sequence cell pointing to the same archived link. The conflicting
    /// statements are then removed, so only the new value remains in effect.
    /// </summary>
    /// <returns>The added (or reinforced) statement.</returns>
    public Link? AddStatementRecordingHistory(ThoughtRef fromRef, ThoughtRef typeRef, ThoughtRef toRef)
    {
        Thought? from = fromRef.GetOrAdd(this, "Unknown");
        Thought? type = from is null ? null : typeRef.GetOrAdd(this, "LinkType", from);
        if (from is null || type is null) return null;
        Thought? to = toRef.GetOrAdd(this, "Unknown");
        var pastMarker = Labeled("was");
        if (pastMarker?.HasProperty("isHistorical") != true)
        {
            pastMarker = GetOrAddThought("was", "LinkType")!;
            pastMarker.AddLink("hasProperty", GetOrAddThought("isHistorical", "Property"));
        }

        bool conditional = IsClauseLinkType(type);
        var historyProperty = Labeled("recordsHistory");
        if (!conditional && from is not Link && !type.HasProperty("isPossible") && !type.HasProperty("isHistorical") &&
            historyProperty is not null && to is not null &&
            (type.HasProperty(historyProperty) || to.HasProperty(historyProperty)))
        {
            var incoming = new Link(from, type, to);
            var conflicts = from.LinksTo.Where(previous => previous.LinkType is not null && previous.To is not null &&
                !previous.LinksTo.Any(clause => IsConditionalLinkType(clause.LinkType)) &&
                LinksAreExclusive(incoming, previous)).ToArray();
            if (conflicts.Length > 0)
            {
                var effective = GetAttributes(from);
                var displaced = conflicts.Where(previous => effective.Any(result =>
                    result.LinkType == previous.LinkType && result.To == previous.To)).ToArray();
                if (displaced.Length > 0)
                {
                    var events = GetOrAddThought("events", "LinkType")!;
                    var validUntil = GetOrAddThought("validUntil", "LinkType")!;
                    var timestamp = GetOrAddThought(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), "EventTime")!;
                    var head = from.HasLink(events)?.To as SeqElement;
                    var pastValues = new Dictionary<(Thought Type, Thought Target), Link>();
                    var visited = new HashSet<SeqElement>(ReferenceEqualityComparer.Instance);
                    for (var cell = head; cell is not null && visited.Add(cell); cell = cell.NXT)
                    {
                        if (cell.VLU is Link { LinkType: { } historicalType, To: { } targetThought } value &&
                            ReferenceEquals(value.From, from))
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
                            archived = from.HasLink(pastType, previous.To) ??
                                new Link(from, pastType, previous.To!) { Weight = previous.Weight };
                            archived.AddDefaultLabel();
                            pastValues.Add(key, archived);
                        }
                        if (head is null)
                        {
                            head = CreateFirstElement(from.Label, archived);
                            from.AddLink(events, head);
                        }
                        else
                        {
                            // InsertElement keeps the head identity and moves its old VLU to NXT.
                            // Move the occurrence's timestamp with that value.
                            var previousEnd = head.HasLink(validUntil)?.To;
                            InsertElement(head, archived);
                            if (previousEnd is not null)
                            {
                                head.RemoveLink(validUntil, previousEnd);
                                head.NXT!.AddLink(validUntil, previousEnd);
                            }
                        }
                        head.AddLink(validUntil, timestamp);
                    }
                    // Suppressed alternatives were already out of effect.
                    foreach (var conflict in conflicts) from.RemoveLink(conflict);
                }
            }
        }

        return AddStatement(from, type, to);
    }
}

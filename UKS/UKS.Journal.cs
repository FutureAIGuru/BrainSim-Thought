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
/// The structural changes made to a UKS during a transaction, in the order they were made, so they
/// can be undone: Links added and removed (is-a parents are Links), Thoughts newly labeled
/// (created), relabeled and deleted. Weights, firing and time-to-live are not recorded.
/// </summary>
public sealed class UKSTransaction
{
    internal enum Change { LinkAdded, LinkRemoved, ThoughtCreated, ThoughtRelabeled, ThoughtDeleted }

    // OldLabel: the label before a relabel, or the label of a deleted Thought.
    internal readonly List<(Change Kind, Thought Thought, string? OldLabel)> Changes = new();

    /// <summary>True if nothing was changed.</summary>
    public bool IsEmpty => Changes.Count == 0;

    /// <summary>True if this transaction added the Link (rather than finding it already there).</summary>
    public bool Added(Link link)
    {
        lock (Changes) return Changes.Any(change => change.Kind == Change.LinkAdded && ReferenceEquals(change.Thought, link));
    }

    /// <summary>True if this transaction created the Thought (rather than finding it already there).</summary>
    public bool Created(Thought thought)
    {
        lock (Changes) return Changes.Any(change => change.Kind == Change.ThoughtCreated && ReferenceEquals(change.Thought, thought));
    }
}

public partial class UKS
{
    private UKSTransaction? journal;
    private bool undoing;

    /// <summary>The open transaction, or null if none is recording.</summary>
    public UKSTransaction? CurrentTransaction => journal;

    /// <summary>
    /// Starts recording structural changes. Transactions do not nest: end (or undo) one before
    /// beginning another.
    /// </summary>
    public UKSTransaction BeginTransaction()
    {
        if (journal is not null) throw new InvalidOperationException("A UKS transaction is already open.");
        journal = new UKSTransaction();
        return journal;
    }

    /// <summary>Stops recording. The transaction can still be undone later.</summary>
    public void EndTransaction(UKSTransaction transaction)
    {
        if (ReferenceEquals(journal, transaction)) journal = null;
    }

    internal void Record(UKSTransaction.Change kind, Thought thought, string? oldLabel = null)
    {
        if (journal is null || undoing) return;
        lock (journal.Changes) journal.Changes.Add((kind, thought, oldLabel));
    }

    // Changes that can be undone, newest last, with what each was: for an Undo button.
    private readonly List<(string Description, UKSTransaction Changes)> undoHistory = new();

    /// <summary>
    /// Makes a change as one transaction and remembers it for UndoLast, if it changed anything.
    /// If a transaction is already open, the change becomes part of it instead.
    /// </summary>
    public void ChangeUndoably(string description, Action change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (journal is not null) { change(); return; }
        UKSTransaction transaction = BeginTransaction();
        try { change(); }
        finally
        {
            EndTransaction(transaction);
            RememberForUndo(description, transaction);
        }
    }

    /// <summary>Remembers a finished transaction for UndoLast, unless it changed nothing.</summary>
    public void RememberForUndo(string description, UKSTransaction transaction)
    {
        if (transaction.IsEmpty) return;
        lock (undoHistory) undoHistory.Add((description, transaction));
    }

    /// <summary>True if there is a remembered change to undo.</summary>
    public bool CanUndo { get { lock (undoHistory) return undoHistory.Count > 0; } }

    private void ClearUndoHistory()
    {
        lock (undoHistory) undoHistory.Clear();
        journal = null;
    }

    /// <summary>
    /// Undoes the most recent remembered change, from whichever source made it (the interpreter, a
    /// dialog). Earlier changes can be undone in turn.
    /// </summary>
    /// <returns>The description of what was undone, or null if there was nothing to undo.</returns>
    public string? UndoLast()
    {
        (string Description, UKSTransaction Changes) last;
        lock (undoHistory)
        {
            if (undoHistory.Count == 0) return null;
            last = undoHistory[^1];
            undoHistory.RemoveAt(undoHistory.Count - 1);
        }
        Undo(last.Changes);
        return last.Description;
    }

    /// <summary>
    /// Reverses a transaction's changes, newest first: added Links are removed, removed Links are
    /// restored (with their labels), and created Thoughts are deleted. Ends the transaction if it is
    /// still open. Undo is exact only if later changes did not build on this transaction's results.
    /// </summary>
    public void Undo(UKSTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        EndTransaction(transaction);
        undoing = true;
        try
        {
            List<(UKSTransaction.Change Kind, Thought Thought, string? OldLabel)> changes;
            lock (transaction.Changes) changes = transaction.Changes.ToList();
            for (int i = changes.Count - 1; i >= 0; i--)
            {
                var (kind, thought, oldLabel) = changes[i];
                switch (kind)
                {
                    case UKSTransaction.Change.ThoughtRelabeled:
                        thought.Label = oldLabel ?? "";
                        break;
                    // Deleting removed the label and the registration last (its Links were removed,
                    // and are restored, before that), so this is undone first.
                    case UKSTransaction.Change.ThoughtDeleted:
                        if (!string.IsNullOrEmpty(oldLabel) && Labeled(oldLabel) is null)
                            ThoughtLabels.AddThoughtLabel(oldLabel, thought);
                        lock (AtomicThoughts) AtomicThoughts.Add(thought);
                        TrackMortality(thought);
                        break;
                    case UKSTransaction.Change.LinkAdded when thought is Link added:
                        added.From?.RemoveLink(added);
                        break;
                    case UKSTransaction.Change.LinkRemoved when thought is Link removed:
                        WriteTheLink(removed);
                        if (!string.IsNullOrEmpty(removed.Label) && Labeled(removed.Label) is null)
                            ThoughtLabels.AddThoughtLabel(removed.Label, removed);
                        TrackMortality(removed);
                        break;
                    case UKSTransaction.Change.ThoughtCreated:
                        if (thought is Link link) ThoughtLabels.RemoveThoughtLabel(link.Label, this);
                        else if (!ReferenceEquals(Labeled(thought.Label), thought)) break;
                        // Its later Links have already been undone, so an unlinked Thought only needs
                        // unregistering (Delete would also add placeholder Thoughts for sequences).
                        else if (thought.LinksTo.Count == 0 && thought.LinksFrom.Count == 0)
                        {
                            ThoughtLabels.RemoveThoughtLabel(thought.Label, this);
                            lock (AtomicThoughts) AtomicThoughts.Remove(thought);
                        }
                        else thought.Delete();
                        break;
                }
            }
            // The sequence cache must not offer a sequence the transaction created: it is gone, and
            // reusing it (to compress a new sequence) would build on empty elements.
            var created = new HashSet<Thought>(changes.Where(change => change.Kind == UKSTransaction.Change.ThoughtCreated)
                .Select(change => change.Thought), ReferenceEqualityComparer.Instance);
            foreach (var key in SequenceCache.Where(entry => created.Contains(entry.Value)).Select(entry => entry.Key).ToList())
                SequenceCache.Remove(key);
            lock (transaction.Changes) transaction.Changes.Clear();
        }
        finally
        {
            undoing = false;
        }
    }
}

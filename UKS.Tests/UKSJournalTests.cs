using Xunit;
using UKS.Helpers;

namespace UKS.Tests;

/// <summary>
/// A transaction records the structural changes made to a UKS; undoing it restores the store exactly:
/// the same labeled Thoughts and the same Links.
/// </summary>
public class UKSJournalTests
{
    // Every labeled Thought, and every Link from one, as text.
    private static List<string> Snapshot(UKS store) => store.Labels
        .SelectMany(entry => new[] { "T " + entry.Key + (entry.Value is Link ? " (link)" : "") }
            .Concat(entry.Value.LinksTo.Select(link => $"L {link.From?.Label} {link.LinkType?.Label} {link.To?.Label}")))
        .OrderBy(line => line, StringComparer.Ordinal).ToList();

    private static UKS NewStore()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        return store;
    }

    [Fact]
    public void UndoRemovesAddedStatementsAndTheThoughtsTheyCreated()
    {
        UKS store = NewStore();
        List<string> before = Snapshot(store);

        UKSTransaction transaction = store.BeginTransaction();
        store.AddStatement("Zorb", "is-a", "Animal");
        store.AddStatement("Zorb", "has", "wing");
        store.EndTransaction(transaction);
        Assert.NotNull(store.Labeled("Zorb"));

        store.Undo(transaction);

        Assert.Null(store.Labeled("Zorb"));
        Assert.Null(store.Labeled("wing"));
        Assert.Equal(before, Snapshot(store));
    }

    [Fact]
    public void UndoRestoresRemovedLinks()
    {
        UKS store = NewStore();
        store.AddStatement("Fido", "is-a", "dog");
        store.AddStatement("Fido", "has", "collar");
        List<string> before = Snapshot(store);

        UKSTransaction transaction = store.BeginTransaction();
        store.Labeled("Fido").RemoveLink(store.Labeled("has"), store.Labeled("collar"));
        store.Labeled("Fido").RemoveParent(store.Labeled("dog"));
        store.Undo(transaction);

        Assert.Equal(before, Snapshot(store));
        Assert.True(store.Labeled("Fido").HasAncestor(store.Labeled("dog")));
    }

    [Fact]
    public void UndoReversesARecordedHistory()
    {
        UKS store = NewStore();
        store.AddStatement("location", "hasProperty", "isExclusive");
        store.AddStatement("location", "hasProperty", "recordsHistory");
        store.AddStatement("yard", "is-a", "location");
        store.AddStatement("kitchen", "is-a", "location");
        Thought fido = store.GetOrAddThought("Fido");
        Thought located = store.GetOrAddThought("located", "LinkType");
        store.AddStatementRecordingHistory(fido, located, store.Labeled("yard"));
        List<string> before = Snapshot(store);

        UKSTransaction transaction = store.BeginTransaction();
        store.AddStatementRecordingHistory(fido, located, store.Labeled("kitchen"));
        Assert.NotNull(fido.GetTargetOfFirstLinkOfType("events"));
        store.Undo(transaction);

        Assert.Equal(before, Snapshot(store));
        Assert.Same(store.Labeled("yard"), Assert.Single(fido.LinksTo, link => link.LinkType == located).To);
        Assert.Null(fido.GetTargetOfFirstLinkOfType("events"));
    }

    [Fact]
    public void ASequenceCreatedAgainAfterUndoIsComplete()
    {
        UKS store = NewStore();
        UKSTransaction transaction = store.BeginTransaction();
        store.CreateSpellingSequence("rover", store.GetOrAddThought("w:rover"));
        store.Undo(transaction);

        Thought word = store.GetOrAddThought("w:rover");
        SeqElement spelling = store.CreateSpellingSequence("rover", word);

        Assert.Equal(new[] { "c:r", "c:o", "c:v", "c:e", "c:r" },
            store.EnumerateSequenceElements(spelling).Select(element => element.VLU.Label.ToLowerInvariant()));
    }

    [Fact]
    public void UndoRestoresADeletedThought()
    {
        UKS store = NewStore();
        store.AddStatement("Fido", "is-a", "dog");
        store.AddStatement("Fido", "has", "collar");
        store.AddStatement("Spot", "is-a", "Fido"); // a child, reparented by the delete
        Thought fido = store.Labeled("Fido");
        List<string> before = Snapshot(store);

        store.ChangeUndoably("delete Fido", fido.Delete);
        Assert.Null(store.Labeled("Fido"));

        Assert.Equal("delete Fido", store.UndoLast());
        Assert.Same(fido, store.Labeled("Fido"));
        Assert.Contains(fido, store.AtomicThoughts);
        Assert.Equal(before, Snapshot(store));
    }

    [Fact]
    public void UndoRestoresARenamedThought()
    {
        UKS store = NewStore();
        store.AddStatement("Fido", "is-a", "dog");
        Thought fido = store.Labeled("Fido");

        store.ChangeUndoably("rename", () => fido.Label = "Rex");
        Assert.Null(store.Labeled("Fido"));

        store.UndoLast();
        Assert.Same(fido, store.Labeled("Fido"));
        Assert.Null(store.Labeled("Rex"));
    }

    [Fact]
    public void UndoLastUndoesRememberedChangesNewestFirst()
    {
        UKS store = NewStore();
        store.ChangeUndoably("first", () => store.AddStatement("Zorb", "is-a", "Animal"));
        store.ChangeUndoably("nothing", () => { });
        store.ChangeUndoably("second", () => store.AddStatement("Zorb", "has", "wing"));

        Assert.Equal("second", store.UndoLast());
        Assert.Null(store.Labeled("wing"));
        Assert.NotNull(store.Labeled("Zorb"));
        Assert.Equal("first", store.UndoLast()); // a change that changed nothing isn't remembered
        Assert.Null(store.Labeled("Zorb"));
        Assert.Null(store.UndoLast());
        Assert.False(store.CanUndo);
    }

    [Fact]
    public void ChangesOutsideATransactionAreNotRecorded()
    {
        UKS store = NewStore();
        UKSTransaction transaction = store.BeginTransaction();
        store.EndTransaction(transaction);

        store.AddStatement("Zorb", "is-a", "Animal");

        Assert.True(transaction.IsEmpty);
        Assert.Throws<InvalidOperationException>(() => { store.BeginTransaction(); store.BeginTransaction(); });
    }
}

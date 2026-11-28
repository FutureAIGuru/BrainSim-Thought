using Xunit;
using UKS.Helpers;

namespace UKS.Tests;

/// <summary>
/// The history of displaced values, as in UKSWeb Lab 4: once location records history, a new
/// location archives the previous one as was.located at the head of the source's events sequence.
/// </summary>
public class UKSHistoryTests
{
    private static UKS LocationStore(bool recordsHistory = true)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        store.AddStatement("location", "hasProperty", "isExclusive");
        foreach (string place in new[] { "inside", "outside", "doghouse", "kitchen" })
            store.AddStatement(place, "is-a", "location");
        if (recordsHistory) store.AddStatement("location", "hasProperty", "recordsHistory");
        return store;
    }

    private static List<Link> Events(UKS store, Thought subject)
    {
        var cells = new List<Link>();
        for (var cell = subject.GetTargetOfFirstLinkOfType("events") as SeqElement; cell is not null; cell = cell.NXT)
            cells.Add((Link)cell.VLU);
        return cells;
    }

    [Fact]
    public void ANewLocationArchivesThePreviousOneAsWasLocated()
    {
        UKS store = LocationStore();
        Thought fido = store.GetOrAddThought("Fido");
        Thought located = store.GetOrAddThought("located", "LinkType");

        store.AddStatementRecordingHistory(fido, located, store.Labeled("inside"));
        store.AddStatementRecordingHistory(fido, located, store.Labeled("kitchen"));

        Link current = Assert.Single(fido.LinksTo, link => link.LinkType == located);
        Assert.Same(store.Labeled("kitchen"), current.To);
        Link archived = Assert.Single(Events(store, fido));
        Assert.Same(store.Labeled("inside"), archived.To);
        Assert.Same(store.Labeled("was.located"), archived.LinkType);
        Assert.True(archived.LinkType.HasProperty("isHistorical"));
        Assert.True(archived.LinkType.HasAncestor(located));
        Assert.NotNull(((SeqElement)fido.GetTargetOfFirstLinkOfType("events")).GetTargetOfFirstLinkOfType("validUntil"));
    }

    [Fact]
    public void ARepeatVisitAddsACellForTheSameArchivedRelationship()
    {
        UKS store = LocationStore();
        Thought fido = store.GetOrAddThought("Fido");
        Thought located = store.GetOrAddThought("located", "LinkType");

        foreach (string place in new[] { "inside", "kitchen", "inside", "outside" })
            store.AddStatementRecordingHistory(fido, located, store.Labeled(place));

        // Newest first: inside (left for outside), kitchen, inside (left for kitchen).
        List<Link> history = Events(store, fido);
        Assert.Equal(new[] { "inside", "kitchen", "inside" }, history.Select(link => link.To.Label));
        Assert.Same(history[0], history[2]);
        Assert.Same(store.Labeled("outside"), Assert.Single(fido.LinksTo, link => link.LinkType == located).To);
    }

    [Fact]
    public void WithoutRecordsHistoryNoHistoryIsKept()
    {
        UKS store = LocationStore(recordsHistory: false);
        Thought fido = store.GetOrAddThought("Fido");
        Thought located = store.GetOrAddThought("located", "LinkType");

        store.AddStatementRecordingHistory(fido, located, store.Labeled("inside"));
        store.AddStatementRecordingHistory(fido, located, store.Labeled("kitchen"));

        Assert.Null(fido.GetTargetOfFirstLinkOfType("events"));
    }

    [Fact]
    public void AWriteActionRecordsHistory()
    {
        UKS store = LocationStore();
        Thought fido = store.GetOrAddThought("Fido");
        Thought write = store.GetOrAddThought("located.!");

        store.ApplyTestOrSetAction(new Link(fido, write, store.Labeled("doghouse")));
        store.ApplyTestOrSetAction(new Link(fido, write, store.Labeled("outside")));

        Assert.Same(store.Labeled("doghouse"), Assert.Single(Events(store, fido)).To);
    }
}

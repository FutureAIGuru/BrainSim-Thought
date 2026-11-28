using Xunit;

namespace UKS.Tests;

/// <summary>Whole-store operations: Reset, Roots, the counts, Copy and LinksAsType.</summary>
public class UKSStoreTests
{
    private static UKS DogStore()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        store.AddStatement("Fido", "is-a", "dog");
        store.AddStatement("Fido", "has", "fur");
        return store;
    }

    [Fact]
    public void CountsSeparateThoughtsFromLinks()
    {
        UKS store = DogStore();
        int thoughts = store.ThoughtCount();
        int links = store.LinkCount();

        Thought rover = store.GetOrAddThought("Rover", "dog");

        Assert.Equal(thoughts + 1, store.ThoughtCount());
        Assert.Equal(links + 1, store.LinkCount()); // Rover's is-a link
        rover.AddLink(store.Labeled("has")!, store.Labeled("fur"));
        Assert.Equal(thoughts + 1, store.ThoughtCount());
        Assert.Equal(links + 2, store.LinkCount());
    }

    [Fact]
    public void CountsIncludeSequencesAndTheirLinks()
    {
        UKS store = DogStore();
        Thought spelled = store.GetOrAddThought("spelled", "LinkType");
        List<Thought> letters = new() { store.GetOrAddThought("x", "Object"), store.GetOrAddThought("y", "Object") };
        int elements = store.SequenceElementCount(), links = store.LinkCount(), thoughts = store.ThoughtCount();

        store.AddSequenceAndLink(store.Labeled("Fido")!, spelled, letters);

        Assert.Equal(thoughts, store.ThoughtCount());         // elements are not counted as Thoughts
        Assert.True(store.SequenceElementCount() > elements);
        Assert.True(store.LinkCount() > links + 1);           // the owner's link and the elements' links
    }

    [Fact]
    public void CountsAgreeAfterSavingAndLoading()
    {
        UKS store = DogStore();
        string path = Path.GetTempFileName();
        try
        {
            Assert.True(store.SaveXml(path));
            var loaded = new UKS();
            Assert.True(loaded.LoadXml(path));
            // The loader also puts links in the Thought set; the counts must not care.
            Assert.Equal(store.ThoughtCount(), loaded.ThoughtCount());
            Assert.Equal(store.LinkCount(), loaded.LinkCount());
            Assert.Equal(store.SequenceElementCount(), loaded.SequenceElementCount());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RootsAreTheThoughtsWithoutParents()
    {
        UKS store = DogStore();
        List<Thought> roots = store.Roots().ToList();

        Assert.Contains(store.Labeled("Thought")!, roots);
        Assert.DoesNotContain(store.Labeled("dog")!, roots);
        Assert.All(roots, root => Assert.Empty(root.Parents));
        Assert.DoesNotContain(roots, root => root is Link);
    }

    [Fact]
    public void ResetEmptiesTheStore()
    {
        UKS store = DogStore();
        Thought fido = store.Labeled("Fido")!;
        fido.TimeToLive = 5;
        store.AdvanceCycle();
        store.ChangeUndoably("add", () => store.GetOrAddThought("Spot", "dog"));

        store.Reset();

        Assert.Equal(0, store.ThoughtCount());
        Assert.Equal(0, store.LinkCount());
        Assert.Null(store.Labeled("Fido"));
        Assert.Equal(1, store.Cycle);
        Assert.False(store.CanUndo);
        store.CreateInitialStructure();
        Assert.NotNull(store.Labeled("Thought"));
        for (int i = 0; i < 10; i++) store.AdvanceCycle(); // the old countdown is gone
    }

    [Fact]
    public void CopyIsIndependentOfTheOriginal()
    {
        UKS store = DogStore();
        UKS copy = store.Copy();

        Assert.Equal(store.ThoughtCount(), copy.ThoughtCount());
        Assert.Equal(store.LinkCount(), copy.LinkCount());
        Assert.NotSame(store.Labeled("Fido"), copy.Labeled("Fido"));
        Assert.NotNull(copy.Labeled("Fido")!.HasLink(copy.Labeled("has")!, copy.Labeled("fur")));

        copy.AddStatement("Spot", "is-a", "dog");
        Assert.Null(store.Labeled("Spot"));
    }

    [Fact]
    public void LinksAsTypeListsTheLinksUsingIt()
    {
        UKS store = DogStore();
        Thought has = store.Labeled("has")!;
        Link furLink = store.Labeled("Fido")!.HasLink(has, store.Labeled("fur"))!;

        Assert.Contains(furLink, has.LinksAsType);
        store.Labeled("Fido")!.RemoveLink(furLink);
        Assert.DoesNotContain(furLink, has.LinksAsType);
    }
}

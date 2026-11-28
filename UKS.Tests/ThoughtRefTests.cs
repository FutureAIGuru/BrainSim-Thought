using Xunit;
using UKS.Helpers;

namespace UKS.Tests;

/// <summary>ThoughtRef: a Thought or a label, resolved by the store being called (here through AddStatement).</summary>
public class ThoughtRefTests
{
    private static UKS NewStore()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        return store;
    }

    [Fact]
    public void LabelsAndThoughtsMixFreely()
    {
        UKS store = NewStore();
        Thought dog = store.GetOrAddThought("dog", "Object")!;

        Link? link = store.AddStatement("Fido", "is-a", dog);

        Assert.NotNull(link);
        Assert.Same(store.Labeled("Fido"), link!.From);
        Assert.Same(dog, link.To);
        Assert.Contains(dog, store.Labeled("Fido")!.Parents);
    }

    [Fact]
    public void ALabelIsResolvedInTheStoreBeingCalled()
    {
        UKS store = NewStore();
        UKS other = NewStore();
        other.GetOrAddThought("cat", "Object");

        Link? link = store.AddStatement("Tom", "is-a", "cat");

        Assert.Same(store, link!.Store);
        Assert.Same(store, link.To!.Store);   // a new "cat" in store, not other's "cat"
        Assert.NotSame(other.Labeled("cat"), link.To);
    }

    [Fact]
    public void AnUnknownLinkTypeIsCreatedUnderLinkType()
    {
        UKS store = NewStore();

        store.AddStatement("Fido", "chases", "ball");

        Assert.True(store.Labeled("chases")!.HasAncestor(store.Labeled("LinkType")));
        Assert.True(store.Labeled("ball")!.HasAncestor(store.Labeled("Unknown")));
    }

    [Fact]
    public void AnEmptyTargetIsAllowedAndAnEmptySourceAddsNothing()
    {
        UKS store = NewStore();

        Assert.NotNull(store.AddStatement("Fido", "is-a", default));
        Assert.Null(store.AddStatement("", "is-a", "dog"));
        Assert.Null(store.Labeled("dog"));
    }

    [Fact]
    public void QueriesAnswerNoForAnUnknownLabelAndCreateNothing()
    {
        UKS store = NewStore();
        Thought fido = store.GetOrAddThought("Fido", "Object")!;
        int before = store.ThoughtCount();

        Assert.False(fido.HasProperty("isFurry"));
        Assert.False(fido.HasAncestor("dog"));
        Assert.Null(fido.HasLink("chases"));
        Assert.Null(fido.GetTargetOfFirstLinkOfType("chases"));
        Assert.Equal(before, store.ThoughtCount());
    }

    [Fact]
    public void AnEmptyTargetMatchesAnyButAnUnknownOneMatchesNone()
    {
        UKS store = NewStore();
        Thought fido = store.GetOrAddThought("Fido", "Object")!;
        fido.AddLink("chases", "ball");

        Assert.NotNull(fido.HasLink("chases"));
        Assert.NotNull(fido.HasLink("chases", "ball"));
        Assert.Null(fido.HasLink("chases", "cat"));
    }

    [Fact]
    public void RemovingByAnUnknownLabelDoesNothing()
    {
        UKS store = NewStore();
        Thought fido = store.GetOrAddThought("Fido", "Object")!;
        int before = store.ThoughtCount();

        fido.RemoveParent("dog");
        fido.RemoveLink("chases", "ball");

        Assert.Equal(before, store.ThoughtCount());
        Assert.Contains(store.Labeled("Object")!, fido.Parents);
    }

    [Fact]
    public void WritesCreateUnknownLabels()
    {
        UKS store = NewStore();
        Thought fido = store.GetOrAddThought("Fido", "Object")!;

        fido.AddLink("chases", "ball");
        fido.AddParent("dog");
        fido.AddProperty("isFurry");

        Assert.True(store.Labeled("chases")!.HasAncestor("LinkType"));
        Assert.True(fido.HasAncestor("dog"));
        Assert.True(fido.HasProperty("isFurry"));
        fido.RemoveParent("dog");
        Assert.False(fido.HasAncestor("dog"));
    }

    [Fact]
    public void AThoughtFromAnotherStoreThrows()
    {
        UKS store = NewStore();
        UKS other = NewStore();
        Thought foreign = other.GetOrAddThought("cat", "Object")!;

        Assert.Throws<ArgumentException>(() => store.AddStatement("Tom", "is-a", foreign));
    }
}

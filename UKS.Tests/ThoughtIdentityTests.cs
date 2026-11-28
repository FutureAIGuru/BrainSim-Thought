using System.Collections.Generic;
using Xunit;

namespace UKS.Tests;

/// <summary>Two Thoughts are equal only if they are the same object; labels are never compared.</summary>
public class ThoughtIdentityTests
{
    private static UKS NewStore()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        return store;
    }

    [Fact]
    public void UnlabeledThoughtsAreDistinct()
    {
        UKS store = NewStore();
        var first = new Thought(store);
        var second = new Thought(store);

        Assert.False(first == second);
        Assert.False(first.Equals(second));
        Assert.Equal(2, new HashSet<Thought> { first, second }.Count);
    }

    [Fact]
    public void AnUnstoredLinkIsNotTheStoredOneButFindsIt()
    {
        UKS store = NewStore();
        Link stored = store.AddStatement("Fido", "is-a", "dog")!;
        var prototype = new Link(store.Labeled("Fido"), store.Labeled("is-a"), store.Labeled("dog"));

        Assert.False(prototype == stored);
        Assert.Same(stored, store.GetLink(prototype));
        Assert.Same(stored, store.Labeled("Fido")!.HasLink("is-a", "dog"));
    }

    [Fact]
    public void StatingALinkTwiceStoresItOnce()
    {
        UKS store = NewStore();
        Link first = store.AddStatement("Fido", "has", "fur")!;
        Link second = store.AddStatement("Fido", "has", "fur")!;

        Assert.Same(first, second);
        Assert.Single(store.Labeled("Fido")!.LinksTo, link => link.LinkType == store.Labeled("has"));
    }

    [Fact]
    public void ANestedStatementIsFoundByWhatItSays()
    {
        UKS store = NewStore();
        Link condition = new(store.GetOrAddThought("Fido"), store.GetOrAddThought("is", "LinkType"), store.GetOrAddThought("outside"));
        Link result = store.AddStatement("Fido", "is", "wet")!;
        store.AddStatement(result, "IF", condition);
        var sameCondition = new Link(store.Labeled("Fido"), store.Labeled("is"), store.Labeled("outside"));

        Assert.NotNull(result.HasLink("IF", sameCondition));
    }
}

/*
 * Ch.5 inheritance regression tests (Simon Atomic Thoughts).
 * Fido/dog/fur, Tripper three-legs exception, transitive is-a chains.
 */

using System.Collections.Generic;
using System.Linq;
using UKS;
using Xunit;

namespace UKS.Tests;

public class InheritanceCh5RegressionTests
{
    private static UKS CreateUks()
    {
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        return uks;
    }

    private static bool HasLink(List<Link> links, string linkTypeLabel, string targetLabel, UKS uks)
    {
        Thought target = uks.Labeled(targetLabel);
        return links.Any(l => l.LinkType?.Label == linkTypeLabel && l.To == target);
    }

    [Fact]
    public void Fido_inherits_dog_fur_via_is_a_traversal()
    {
        var uks = CreateUks();
        uks.AddStatement("animal", "is-a", "Object");
        uks.AddStatement("dog", "is-a", "animal");
        uks.AddStatement("fur", "is-a", "Object");
        uks.AddStatement("dog", "has", "fur");
        uks.AddStatement("Fido", "is-a", "dog");

        var links = uks.GetAllLinks(new List<Thought> { uks.Labeled("Fido") });

        Assert.True(HasLink(links, "has", "fur", uks));
    }

    [Fact]
    public void Fido_inherits_four_legs_from_dog_without_local_copy()
    {
        var uks = CreateUks();
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("legs", "is-a", "Object");
        uks.AddStatement("has.4", "is-a", "has");
        uks.AddStatement("dog", "has.4", "legs");
        uks.AddStatement("Fido", "is-a", "dog");

        var links = uks.GetAllLinks(new List<Thought> { uks.Labeled("Fido") });

        Assert.True(HasLink(links, "has.4", "legs", uks));
    }

    [Fact]
    public void Tripper_local_three_legs_overrides_inherited_four_legs()
    {
        var uks = CreateUks();
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("legs", "is-a", "Object");
        uks.AddStatement("has.4", "is-a", "has");
        uks.AddStatement("has.3", "is-a", "has");
        uks.AddStatement("dog", "has.4", "legs");
        uks.AddStatement("Tripper", "is-a", "dog");
        uks.AddStatement("Tripper", "has.3", "legs");

        var links = uks.GetAllLinks(new List<Thought> { uks.Labeled("Tripper") });

        Assert.True(HasLink(links, "has.3", "legs", uks));
        Assert.False(HasLink(links, "has.4", "legs", uks));
    }

    [Fact]
    public void Tripper_exception_wins_when_local_link_added_after_category_default()
    {
        var uks = CreateUks();
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("legs", "is-a", "Object");
        uks.AddStatement("has.4", "is-a", "has");
        uks.AddStatement("has.3", "is-a", "has");
        uks.AddStatement("Tripper", "is-a", "dog");
        uks.AddStatement("dog", "has.4", "legs");
        uks.AddStatement("Tripper", "has.3", "legs");

        var links = uks.GetAllLinks(new List<Thought> { uks.Labeled("Tripper") });

        Assert.True(HasLink(links, "has.3", "legs", uks));
        Assert.False(HasLink(links, "has.4", "legs", uks));
    }

    [Fact]
    public void Deep_is_a_chain_inherits_category_attribute()
    {
        var uks = CreateUks();
        uks.AddStatement("living", "is-a", "Object");
        uks.AddStatement("animal", "is-a", "living");
        uks.AddStatement("dog", "is-a", "animal");
        uks.AddStatement("alive", "is-a", "Object");
        uks.AddStatement("living", "has", "alive");
        uks.AddStatement("Fido", "is-a", "dog");

        var links = uks.GetAllLinks(new List<Thought> { uks.Labeled("Fido") });

        Assert.True(HasLink(links, "has", "alive", uks));
    }

    [Fact]
    public void Inheritance_stops_beyond_max_hops()
    {
        var uks = CreateUks();
        const int chainLength = 12; // chain0 -> chain12 is 12 hops; default maxHops = 8
        Thought prev = uks.GetOrAddThought("chain0", "Object");
        for (int i = 1; i <= chainLength; i++)
        {
            Thought next = uks.GetOrAddThought($"chain{i}", "Object");
            uks.AddStatement(prev.Label, "is-a", next.Label);
            prev = next;
        }
        uks.AddStatement("deepFact", "is-a", "Object");
        uks.AddStatement(prev.Label, "has", "deepFact");

        var links = uks.GetAllLinks(new List<Thought> { uks.Labeled("chain0") });

        Assert.False(HasLink(links, "has", "deepFact", uks));
    }

    [Fact]
    public void Inherited_attributes_have_positive_inheritance_depth()
    {
        var uks = CreateUks();
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("fur", "is-a", "Object");
        uks.AddStatement("dog", "has", "fur");
        uks.AddStatement("Fido", "is-a", "dog");

        var links = uks.GetAllLinks(new List<Thought> { uks.Labeled("Fido") });
        Link inherited = links.First(l => l.LinkType?.Label == "has" && l.To == uks.Labeled("fur"));

        Assert.True(inherited.InheritanceDepth > 0);
    }

    [Fact]
    public void Local_exception_link_has_zero_inheritance_depth()
    {
        var uks = CreateUks();
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("legs", "is-a", "Object");
        uks.AddStatement("has.3", "is-a", "has");
        uks.AddStatement("Tripper", "is-a", "dog");
        uks.AddStatement("Tripper", "has.3", "legs");

        var links = uks.GetAllLinks(new List<Thought> { uks.Labeled("Tripper") });
        Link local = links.First(l => l.LinkType?.Label == "has.3");

        Assert.Equal(0, local.InheritanceDepth);
    }
}
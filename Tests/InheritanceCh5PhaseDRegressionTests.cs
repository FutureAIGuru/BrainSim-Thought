/*
 * Ch.5 Phase D — shortcuts, provenance, multi-parent,
 * ephemeral TTL.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using UKS;
using Xunit;

namespace UKS.Tests;

public class InheritanceCh5PhaseDRegressionTests
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
    public void Shortcut_is_a_reaches_physical_object_attributes()
    {
        var uks = CreateUks();
        uks.AddStatement("physical-object", "is-a", "Object");
        uks.AddStatement("animal", "is-a", "physical-object");
        uks.AddStatement("dog", "is-a", "animal");
        uks.AddStatement("dog", "is-a", "physical-object");
        uks.AddStatement("tangible", "is-a", "Object");
        uks.AddStatement("physical-object", "has", "tangible");
        uks.AddStatement("Fido", "is-a", "dog");

        var links = uks.GetAttributes(uks.Labeled("Fido"));
        Assert.True(HasLink(links, "has", "tangible", uks));
    }

    [Fact]
    public void Inherited_link_records_dog_as_InheritedFromCategory()
    {
        var uks = CreateUks();
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("fur", "is-a", "Object");
        uks.AddStatement("dog", "has", "fur");
        uks.AddStatement("Fido", "is-a", "dog");

        Thought fido = uks.Labeled("Fido");
        var links = uks.GetAttributes(fido);
        Link inherited = links.First(l => l.To == uks.Labeled("fur"));

        Assert.Equal("dog", inherited.InheritedFromCategory?.Label);
        Assert.Equal(fido, inherited.From);

    }

    [Fact]
    public void Multiple_is_a_parents_merge_dog_and_pet_attributes()
    {
        var uks = CreateUks();
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("pet", "is-a", "Object");
        uks.AddStatement("fur", "is-a", "Object");
        uks.AddStatement("owner", "is-a", "Object");
        uks.AddStatement("dog", "has", "fur");
        uks.AddStatement("pet", "has", "owner");
        uks.AddStatement("Fido", "is-a", "dog");
        uks.AddStatement("Fido", "is-a", "pet");

        var links = uks.GetAttributes(uks.Labeled("Fido"));
        Assert.True(HasLink(links, "has", "fur", uks));
        Assert.True(HasLink(links, "has", "owner", uks));
    }

    [Fact]
    public void Located_in_links_get_ephemeral_default_ttl()
    {
        var uks = CreateUks();
        uks.AddStatement("yard", "is-a", "Object");
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("Fido", "is-a", "dog");

        Link located = uks.AddStatement("Fido", "located-in", "yard");
        Link stable = uks.AddStatement("Fido", "is-a", "dog");

        Assert.Equal(TimeSpan.FromSeconds(30), located.TimeToLive);
        Assert.Equal(TimeSpan.MaxValue, stable.TimeToLive);
    }

}

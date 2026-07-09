/*
 * Ch.5 Phase D — shortcuts, provenance, multi-parent, taxonomy insert,
 * context resolver, ephemeral TTL.
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

    private static Thought? ResolveLabel(UKS uks, Thought? t)
    {
        if (t is null) return null;
        return uks.Labeled(t.Label) ?? t;
    }

    private static void EnsureExistOntology(UKS uks)
    {
        uks.AddStatement("exist", "is-a", "LinkType");
        uks.AddStatement("EXIST", "is-a", "exist");
        uks.AddStatement("exist.is-a", "is-a", "exist");
        Thought existIsA = uks.Labeled("exist.is-a");
        Thought isA = uks.Labeled("is-a");
        existIsA.AddLink(uks.Labeled("is"), isA);
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

        var links = uks.GetAllLinks(new List<Thought> { uks.Labeled("Fido") });
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
        var links = uks.GetAllLinks(new List<Thought> { fido });
        Link inherited = links.First(l => l.To == uks.Labeled("fur"));

        Assert.Equal("dog", inherited.InheritedFromCategory?.Label);
        Assert.Equal(fido, inherited.From);

        var trace = uks.ExplainInheritance(inherited, fido);
        Assert.Equal(new[] { "Fido", "dog", "fur" }, trace.Select(t => t.Label).ToArray());
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

        var links = uks.GetAllLinks(new List<Thought> { uks.Labeled("Fido") });
        Assert.True(HasLink(links, "has", "fur", uks));
        Assert.True(HasLink(links, "has", "owner", uks));
    }

    [Fact]
    public void InsertCategoryBetween_propagates_mammal_attributes_to_dogs()
    {
        var uks = CreateUks();
        uks.AddStatement("animal", "is-a", "Object");
        uks.AddStatement("dog", "is-a", "animal");
        uks.AddStatement("mammal", "is-a", "Object");
        uks.AddStatement("warm-blooded", "is-a", "Object");

        uks.InsertCategoryBetween(uks.Labeled("dog"), uks.Labeled("mammal"), uks.Labeled("animal"));
        uks.AddStatement("mammal", "has", "warm-blooded");
        uks.AddStatement("Fido", "is-a", "dog");

        var links = uks.GetAllLinks(new List<Thought> { uks.Labeled("Fido") });
        Assert.True(HasLink(links, "has", "warm-blooded", uks));
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

    [Fact]
    public void ContextResolver_selects_higher_weight_case()
    {
        var uks = CreateUks();
        EnsureExistOntology(uks);

        Thought fido = uks.GetOrAddThought("Fido", "Object");
        Thought dog = uks.GetOrAddThought("dog", "Object");
        Thought pet = uks.GetOrAddThought("pet", "Object");
        uks.AddStatement("Fido", "is-a", "dog");
        uks.AddStatement("Fido", "is-a", "pet");

        Thought contextRoot = uks.GetOrAddThought("park", "Context");
        Thought caseDog = uks.GetOrAddThought("case-dog", contextRoot);
        Thought casePet = uks.GetOrAddThought("case-pet", contextRoot);
        Thought has = uks.Labeled("has");
        Thought existIsA = uks.Labeled("exist.is-a");

        Link dogCheck = new Link(fido, existIsA, dog);
        Link petCheck = new Link(fido, existIsA, pet);
        Link dogWrap = caseDog.AddLink(has, dogCheck);
        Link petWrap = casePet.AddLink(has, petCheck);
        dogWrap.Weight = 1;
        petWrap.Weight = 2;

        ContextCaseResult? selected = uks.SelectBestContextCase(contextRoot, t => ResolveLabel(uks, t));
        Assert.NotNull(selected);
        Assert.Equal("case-pet", selected.Case?.Label);
    }

    [Fact]
    public void FilterLinksByContext_keeps_preferred_category_inherited_links()
    {
        var uks = CreateUks();
        EnsureExistOntology(uks);

        Thought fido = uks.GetOrAddThought("Fido", "Object");
        Thought dog = uks.GetOrAddThought("dog", "Object");
        Thought pet = uks.GetOrAddThought("pet", "Object");
        Thought loud = uks.GetOrAddThought("loud", "Object");
        Thought calm = uks.GetOrAddThought("calm", "Object");
        uks.AddStatement("dog", "has", "loud");
        uks.AddStatement("pet", "has", "calm");
        uks.AddStatement("Fido", "is-a", "dog");
        uks.AddStatement("Fido", "is-a", "pet");

        Thought contextRoot = uks.GetOrAddThought("park", "Context");
        Thought caseDog = uks.GetOrAddThought("case-dog", contextRoot);
        Thought casePet = uks.GetOrAddThought("case-pet", contextRoot);
        Thought has = uks.Labeled("has");
        Thought existIsA = uks.Labeled("exist.is-a");

        Link dogCheck = new Link(fido, existIsA, dog);
        Link petCheck = new Link(fido, existIsA, pet);
        Link dogWrap = caseDog.AddLink(has, dogCheck);
        Link petWrap = casePet.AddLink(has, petCheck);
        dogWrap.Weight = 1;
        petWrap.Weight = 2;

        var all = uks.GetAllLinks(new List<Thought> { fido });
        Assert.True(HasLink(all, "has", "loud", uks));
        Assert.True(HasLink(all, "has", "calm", uks));

        var filtered = uks.FilterLinksByContext(all, fido, contextRoot, t => ResolveLabel(uks, t));
        Assert.True(HasLink(filtered, "has", "calm", uks));
        Assert.False(HasLink(filtered, "has", "loud", uks));
    }
}
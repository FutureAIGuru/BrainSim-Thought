/*
 * Ch.3 Atomic Thought substrate regression tests (Simon).
 * Locks the UKS data-model contracts: uniform units, typed triples, shared attributes,
 * directional links, reverse traversal, weights, word/meaning split, open vocabulary.
 */

using System.Linq;
using UKS;
using Xunit;

namespace UKS.Tests;

public class SubstrateCh3RegressionTests
{
    private static UKS CreateUks()
    {
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        return uks;
    }

    [Fact]
    public void Uniform_unit_object_attribute_and_linktype_are_Thought()
    {
        var uks = CreateUks();
        Thought fido = uks.GetOrAddThought("Fido", "Object");
        Thought brown = uks.GetOrAddThought("brown", "Attribute");
        Thought isType = uks.Labeled("is");

        Assert.IsType<Thought>(fido);
        Assert.IsType<Thought>(brown);
        Assert.IsType<Thought>(isType);
        Assert.NotSame(fido.GetType(), typeof(Link));
    }

    [Fact]
    public void Shared_attribute_Thought_reused_across_objects()
    {
        var uks = CreateUks();
        Thought brown = uks.GetOrAddThought("brown", "Attribute");
        Thought fido = uks.GetOrAddThought("Fido", "Object");
        Thought table = uks.GetOrAddThought("table", "Object");

        uks.AddStatement(fido, "is", brown);
        uks.AddStatement(table, "is", brown);

        Assert.Same(brown, fido.LinksTo.First(l => l.LinkType?.Label == "is").To);
        Assert.Same(brown, table.LinksTo.First(l => l.LinkType?.Label == "is").To);
    }

    [Fact]
    public void Typed_triple_from_linktype_and_target_are_all_Thoughts()
    {
        var uks = CreateUks();
        Link link = uks.AddStatement("Fido", "is-a", "dog");

        Assert.NotNull(link);
        Assert.IsType<Thought>(link.From);
        Assert.IsType<Thought>(link.LinkType);
        Assert.IsType<Thought>(link.To);
        Assert.Equal("Fido", link.From?.Label);
        Assert.Equal("is-a", link.LinkType?.Label);
        Assert.Equal("dog", link.To?.Label);
    }

    [Fact]
    public void Relationship_instance_is_a_Link_Thought()
    {
        var uks = CreateUks();
        Thought fido = uks.GetOrAddThought("Fido", "Object");
        Thought fur = uks.GetOrAddThought("fur", "Object");
        Link link = uks.AddStatement(fido, "has", fur);

        Assert.NotNull(link);
        Assert.IsAssignableFrom<Thought>(link);
        Assert.IsType<Link>(link);
        Assert.Same(link, fido.HasLink(uks.Labeled("has"), fur));
    }

    [Fact]
    public void Directional_links_are_asymmetric()
    {
        var uks = CreateUks();
        uks.GetOrAddThought("located-in", "LinkType");
        Thought fido = uks.GetOrAddThought("Fido", "Object");
        Thought yard = uks.GetOrAddThought("yard", "Object");

        uks.AddStatement(fido, "located-in", yard);

        Assert.NotNull(fido.HasLink(uks.Labeled("located-in"), yard));
        Assert.Null(yard.HasLink(uks.Labeled("located-in"), fido));
    }

    [Fact]
    public void Reverse_traversal_from_attribute_finds_multiple_objects()
    {
        var uks = CreateUks();
        Thought brown = uks.GetOrAddThought("brown", "Attribute");
        Thought fido = uks.GetOrAddThought("Fido", "Object");
        Thought table = uks.GetOrAddThought("table", "Object");
        uks.AddStatement(fido, "is", brown);
        uks.AddStatement(table, "is", brown);

        var sources = brown.LinksFrom
            .Where(l => l.LinkType?.Label == "is")
            .Select(l => l.From?.Label)
            .ToList();

        Assert.Contains("Fido", sources);
        Assert.Contains("table", sources);
    }

    [Fact]
    public void Link_weight_persists_on_relationship_instance()
    {
        var uks = CreateUks();
        Link link = uks.AddStatement("Fido", "is", "brown");
        link.Weight = 0.75f;

        Link roundTrip = uks.GetLink(uks.Labeled("Fido"), uks.Labeled("is"), uks.Labeled("brown"));
        Assert.NotNull(roundTrip);
        Assert.Equal(0.75f, roundTrip.Weight);
    }

    [Fact]
    public void Word_Thought_separate_from_meaning_via_means_link()
    {
        var uks = CreateUks();
        uks.GetOrAddThought("means", "LinkType");
        Thought word = uks.GetOrAddThought("w:fido", "word");
        Thought fido = uks.GetOrAddThought("fido", "Object");

        uks.AddStatement(word, "means", fido);

        Assert.NotSame(word, fido);
        Assert.Same(fido, word.LinksTo.First(l => l.LinkType?.Label == "means").To);
    }

    [Fact]
    public void Open_vocabulary_allocates_Thought_on_first_encounter()
    {
        var uks = CreateUks();
        const string novel = "QuokkaInstance42";

        Assert.Null(uks.Labeled(novel));
        Thought created = uks.GetOrAddThought(novel, "Object");

        Assert.NotNull(created);
        Assert.Same(created, uks.Labeled(novel));
        Assert.Contains(created, uks.AtomicThoughts);
    }

    [Fact]
    public void Grounding_chain_links_sensory_experience_to_object()
    {
        var uks = CreateUks();
        Thought sight = uks.GetOrAddThought("sight-experience", "Sensory");
        Thought brown = uks.GetOrAddThought("brown", "Attribute");
        Thought fido = uks.GetOrAddThought("Fido", "Object");

        uks.GetOrAddThought("perceived", "LinkType");
        uks.AddStatement(sight, "perceived", brown);
        uks.AddStatement(fido, "is", brown);

        Assert.NotNull(sight.HasLink(uks.Labeled("perceived"), brown));
        Assert.Contains(fido, brown.LinksFrom.Select(l => l.From));
    }
}
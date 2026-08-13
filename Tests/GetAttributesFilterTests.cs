using System.Linq;
using Xunit;

namespace UKS.Tests;

public class GetAttributesFilterTests
{
    [Fact]
    public void No_filter_returns_all_attributes()
    {
        var (uks, fido, _, _, _) = CreateFixture();

        var results = uks.GetAttributes(fido);

        Assert.Contains(results, link => link.To?.Label == "red");
        Assert.Contains(results, link => link.To?.Label == "blue");
    }

    [Fact]
    public void Filters_by_link_type_only()
    {
        var (uks, fido, has, _, _) = CreateFixture();

        var results = uks.GetAttributes(fido, new Link { LinkType = has });

        Assert.Single(results);
        Assert.Equal("red", results[0].To?.Label);
    }

    [Fact]
    public void Filters_by_target_ancestor_only()
    {
        var (uks, fido, _, _, color) = CreateFixture();

        var results = uks.GetAttributes(fido, new Link { To = color });

        Assert.Contains(results, link => link.To?.Label == "red");
        Assert.Contains(results, link => link.To?.Label == "blue");
    }

    [Fact]
    public void Filters_by_link_type_and_target()
    {
        var (uks, fido, _, can, color) = CreateFixture();

        var results = uks.GetAttributes(fido, new Link { LinkType = can, To = color });

        Assert.Single(results);
        Assert.Equal("blue", results[0].To?.Label);
    }

    [Fact]
    public void Target_filter_matches_target_itself()
    {
        var (uks, fido, _, _, _) = CreateFixture();
        Thought red = uks.Labeled("red");

        var results = uks.GetAttributes(fido, new Link { To = red });

        Assert.Single(results);
        Assert.Equal(red, results[0].To);
    }

    [Fact]
    public void Relationship_search_uses_a_query_filter_link()
    {
        var (uks, fido, _, _, color) = CreateFixture();
        Thought unknown = uks.GetOrAddThought("??", "Unknown");
        Thought filterBy = uks.GetOrAddThought("filterBy", "Property");
        Link query = new(fido, null, unknown);
        query.AddLink(filterBy, color);

        var results = uks.SearchForRelationships(query);

        Assert.Equal(new[] { "blue", "red" }, results.Select(link => link.To.Label).OrderBy(label => label));
    }

    [Fact]
    public void Wildcard_relationship_search_returns_every_matching_relationship_type()
    {
        var (uks, fido, _, _, _) = CreateFixture();
        Thought unknown = uks.GetOrAddThought("??", "Unknown");
        Link query = new(fido, null, unknown);

        var results = uks.SearchForRelationships(query);

        Assert.Contains(results, link => link.LinkType?.Label == "has" && link.To?.Label == "red");
        Assert.Contains(results, link => link.LinkType?.Label == "can" && link.To?.Label == "blue");
    }

    private static (UKS uks, Thought fido, Thought has, Thought can, Thought color) CreateFixture()
    {
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        uks.AddStatement("animal", "is-a", "Object");
        uks.AddStatement("color", "is-a", "Object");
        uks.AddStatement("red", "is-a", "color");
        uks.AddStatement("blue", "is-a", "color");
        uks.AddStatement("Fido", "is-a", "animal");
        uks.AddStatement("animal", "has", "red");
        uks.AddStatement("animal", "can", "blue");

        return (uks, uks.Labeled("Fido"), uks.Labeled("has"), uks.Labeled("can"), uks.Labeled("color"));
    }
}

/*
 * Brain Simulator Thought — NatureAnimals.xml import verification
 */

using System.IO;
using UKS;
using Xunit;

namespace UKS.Tests;

public class NatureAnimalsXmlTests
{
    private static string XmlPath()
    {
        string fromTest = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..",
            "BrainSimulator", "UKSContent", "NatureAnimals.xml");
        if (File.Exists(fromTest)) return Path.GetFullPath(fromTest);

        string fromRepo = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "BrainSimulator", "UKSContent", "NatureAnimals.xml");
        return Path.GetFullPath(fromRepo);
    }

    private static UKS LoadNatureUks()
    {
        var uks = new UKS(clear: true);
        string path = XmlPath();
        Assert.True(File.Exists(path), $"NatureAnimals.xml not found at {path}");
        Assert.True(uks.LoadUKSfromXMLFile(path));
        return uks;
    }

    private static void AssertLink(UKS uks, string from, string linkType, string to)
    {
        Thought f = uks.Labeled(from);
        Thought lt = uks.Labeled(linkType);
        Thought t = uks.Labeled(to);
        Assert.NotNull(f);
        Assert.NotNull(lt);
        Assert.NotNull(t);
        Assert.NotNull(f.HasLink(lt, t));
    }

    [Fact]
    public void NatureAnimals_LoadsFromXml()
    {
        var uks = LoadNatureUks();
        Assert.NotNull(uks.Labeled("Fido"));
        Assert.NotNull(uks.Labeled("mammal"));
        Assert.True(uks.AtomicThoughts.Count > 100);
    }

    [Fact]
    public void NatureAnimals_FidoPattern()
    {
        var uks = LoadNatureUks();
        AssertLink(uks, "Fido", "is-a", "dog");
        AssertLink(uks, "Fido", "is", "brown");
        AssertLink(uks, "Fido", "has.4", "leg");
        AssertLink(uks, "dog", "has.4", "leg");
    }

    [Fact]
    public void NatureAnimals_TripperException()
    {
        var uks = LoadNatureUks();
        AssertLink(uks, "Tripper", "is-a", "dog");
        AssertLink(uks, "Tripper", "has.3", "leg");
    }

    [Fact]
    public void NatureAnimals_TaxonomyAndCrossSpecies()
    {
        var uks = LoadNatureUks();
        AssertLink(uks, "dog", "is-a", "mammal");
        AssertLink(uks, "eagle", "is-a", "bird");
        AssertLink(uks, "salmon", "is-a", "fish");
        AssertLink(uks, "dog", "isSimilarTo", "wolf");
        AssertLink(uks, "eagle", "predatorOf", "mouse");
        AssertLink(uks, "whale", "differsFrom", "fish");
        AssertLink(uks, "penguin", "livesIn", "antarctica");
    }
}
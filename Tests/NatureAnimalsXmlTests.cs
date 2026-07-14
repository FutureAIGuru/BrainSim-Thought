/*
 * Brain Simulator Thought — NatureAnimals.xml import verification
 *
 * Aligned with Ch.5 inheritance: class facts on dog; Fido inherits via GetAllLinks.
 * Generator: tools/generate_nature_animals_xml.py (Phase 4+5).
 */

using System.IO;
using System.Linq;
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

    private static void AssertLocalLink(UKS uks, string from, string linkType, string to)
    {
        Thought f = uks.Labeled(from);
        Thought lt = uks.Labeled(linkType);
        Thought t = uks.Labeled(to);
        Assert.NotNull(f);
        Assert.NotNull(lt);
        Assert.NotNull(t);
        Assert.NotNull(f.HasLink(lt, t));
    }

    /// <summary>Link may be local or inherited (Ch.5 traversal via GetAllLinks).</summary>
    private static void AssertLinkIncludingInheritance(UKS uks, string from, string linkType, string to)
    {
        Thought f = uks.Labeled(from);
        Thought lt = uks.Labeled(linkType);
        Thought t = uks.Labeled(to);
        Assert.NotNull(f);
        Assert.NotNull(lt);
        Assert.NotNull(t);

        if (f.HasLink(lt, t) is not null)
            return;

        var all = uks.GetAllLinks(new System.Collections.Generic.List<Thought> { f });
        Assert.Contains(all, l =>
            l.LinkType is not null && l.To is not null &&
            l.LinkType.Label == linkType && l.To.Label == to);
    }

    [Fact]
    public void NatureAnimals_LoadsFromXml()
    {
        var uks = LoadNatureUks();
        Assert.NotNull(uks.Labeled("Fido"));
        Assert.NotNull(uks.Labeled("mammal"));
        Assert.NotNull(uks.Labeled("Unknown"));
        Assert.True(uks.AtomicThoughts.Count > 100);
    }

    [Fact]
    public void NatureAnimals_FidoPattern_Ch5Inheritance()
    {
        var uks = LoadNatureUks();
        AssertLocalLink(uks, "Fido", "is-a", "dog");
        AssertLocalLink(uks, "Fido", "is", "brown");
        // Class facts on dog; Fido inherits (no required local has.4 on Fido)
        AssertLocalLink(uks, "dog", "has.4", "leg");
        AssertLocalLink(uks, "dog", "has", "fur");
        AssertLinkIncludingInheritance(uks, "Fido", "has.4", "leg");
        AssertLinkIncludingInheritance(uks, "Fido", "has", "fur");
        // Must not require duplicated local anatomy on Fido for the class fact path
        Thought fido = uks.Labeled("Fido")!;
        Thought has4 = uks.Labeled("has.4")!;
        Thought leg = uks.Labeled("leg")!;
        // Local may be absent (preferred Ch.5); if present, still OK for older files
        _ = fido.HasLink(has4, leg);
    }

    [Fact]
    public void NatureAnimals_TripperException()
    {
        var uks = LoadNatureUks();
        AssertLocalLink(uks, "Tripper", "is-a", "dog");
        AssertLocalLink(uks, "Tripper", "has.3", "leg");
    }

    [Fact]
    public void NatureAnimals_TaxonomyAndCrossSpecies()
    {
        var uks = LoadNatureUks();
        AssertLocalLink(uks, "dog", "is-a", "mammal");
        AssertLocalLink(uks, "eagle", "is-a", "bird");
        AssertLocalLink(uks, "salmon", "is-a", "fish");
        AssertLocalLink(uks, "dog", "isSimilarTo", "wolf");
        AssertLocalLink(uks, "eagle", "predatorOf", "mouse");
        AssertLocalLink(uks, "whale", "differsFrom", "fish");
        AssertLocalLink(uks, "penguin", "livesIn", "antarctica");
    }

    [Fact]
    public void NatureAnimals_Phase4AnatomyAndEcology()
    {
        var uks = LoadNatureUks();
        AssertLocalLink(uks, "bird", "has.many", "feather");
        AssertLocalLink(uks, "bird", "lays", "egg");
        AssertLocalLink(uks, "whale", "has.no", "leg");
        AssertLocalLink(uks, "snake", "has.no", "leg");
        AssertLocalLink(uks, "eagle", "livesIn", "sky");
        AssertLocalLink(uks, "shark", "eats", "salmon");
        AssertLocalLink(uks, "livingThing", "is-a", "nature");
    }
}

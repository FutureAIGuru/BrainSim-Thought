/*
 * Regression tests for UKS fixes (2026-07-07 inspection)
 */

using System.IO;
using UKS;
using Xunit;

namespace UKS.Tests;

public class UKSFixesRegressionTests
{
    private static UKS CreateUks() { var uks = new UKS(clear: true); uks.CreateInitialStructure(); return uks; }

    [Fact]
    public void LinksAreExclusive_CommonParentSources_AreExclusive()
    {
        var uks = CreateUks();
        uks.AddStatement("animal", "is-a", "Object");
        uks.AddStatement("dog", "is-a", "animal");
        uks.AddStatement("cat", "is-a", "animal");
        uks.AddStatement("legs", "is-a", "Object");
        uks.AddStatement("has.4", "is-a", "has");
        uks.AddStatement("has.3", "is-a", "has");

        var dogLegs = uks.GetLink(uks.AddStatement("dog", "has.4", "legs"));
        var catLegs = uks.GetLink(uks.AddStatement("cat", "has.3", "legs"));

        Assert.NotNull(dogLegs);
        Assert.NotNull(catLegs);
        Assert.True(uks.LinksAreExclusive_ForTests(dogLegs, catLegs));
    }

    [Fact]
    public void Descendants_UsesIsA_NotLabelPrefix()
    {
        var uks = CreateUks();
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("doghouse", "is-a", "Object");
        uks.AddStatement("puppy", "is-a", "dog");

        var dog = uks.Labeled("dog");
        var doghouse = uks.Labeled("doghouse");
        var puppy = uks.Labeled("puppy");
        var children = dog.Descendants;

        Assert.Contains(puppy, children);
        Assert.DoesNotContain(doghouse, children);
    }

    [Fact]
    public void AddStatement_WiresLabeledPlaceholder()
    {
        var uks = CreateUks();
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("Fido", "is-a", "dog");

        var placeholder = new Link { Label = "mylink" };
        uks.AtomicThoughts.Add(placeholder);

        var link = uks.AddStatement(uks.Labeled("Fido"), uks.Labeled("is-a"), uks.Labeled("dog"), "mylink");

        Assert.NotNull(link);
        Assert.Same(uks.Labeled("Fido"), link.From);
        Assert.NotNull(link.From.HasLink(uks.Labeled("is-a"), uks.Labeled("dog")));
    }

    [Fact]
    public void XmlRoundTrip_PreservesLink()
    {
        var uks = CreateUks();
        uks.AddStatement("dog", "is-a", "Object");
        Link original = uks.AddStatement("Fido", "is-a", "dog");
        original.Weight = 0.42f;
        original.maxWeight = 0.8f;
        original.isPlastic = true;

        string path = Path.Combine(Path.GetTempPath(), $"uks_roundtrip_{Guid.NewGuid():N}.xml");
        try
        {
            Assert.True(uks.SaveUKStoXMLFile(path));

            var uks2 = new UKS(clear: true);
            Assert.True(uks2.LoadUKSfromXMLFile(path));

            Assert.NotNull(uks2.Labeled("Fido"));
            Link restored = uks2.GetLink(
                uks2.Labeled("Fido"), uks2.Labeled("is-a"), uks2.Labeled("dog"));
            Assert.NotNull(restored);
            Assert.Equal(0.42f, restored.Weight, 2);
            Assert.Equal(0.8f, restored.maxWeight, 2);
            Assert.True(restored.isPlastic);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void XmlRoundTrip_DoesNotPromoteSequenceNodesToAtomicThoughts()
    {
        var uks = CreateUks();
        Thought word = uks.GetOrAddThought("roundTripWord", "Word");
        Thought spelled = uks.GetOrAddThought("spelled", "LinkType");
        List<Thought> letters = new[] { "R", "O", "U", "N", "D" }
            .Select(label => uks.GetOrAddThought(label, "letter"))
            .ToList();
        SeqElement originalSequence = uks.AddSequenceAndLink(word, spelled, letters);

        Assert.DoesNotContain(originalSequence, uks.AtomicThoughts);

        string path = Path.Combine(
            Path.GetTempPath(), $"uks_sequence_roundtrip_{Guid.NewGuid():N}.xml");
        try
        {
            Assert.True(uks.SaveUKStoXMLFile(path));

            var restored = new UKS(clear: true);
            Assert.True(restored.LoadUKSfromXMLFile(path));

            Thought restoredWord = restored.Labeled("roundTripWord");
            SeqElement restoredSequence = Assert.IsType<SeqElement>(
                restoredWord.GetTargetOfFirstLinkOfType("spelled"));
            Assert.Equal(
                new[] { "R", "O", "U", "N", "D" },
                restored.FlattenSequence(restoredSequence)
                    .Select(element => element.Label));
            Assert.DoesNotContain(
                restored.AtomicThoughts, thought => thought is SeqElement);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExportTextFile_ThrowsOnInvalidPath()
    {
        var uks = CreateUks();
        uks.AddStatement("dog", "is-a", "Object");
        Assert.Throws<DirectoryNotFoundException>(() =>
            uks.ExportTextFile("Object", "/nonexistent_path_zzzz/export.txt"));
    }

    //[Fact]
    //public void HasSequence_UnsupportedFlagsThrow()
    //{
    //    var uks = CreateUks();
    //    var targets = new List<Thought> { uks.GetOrAddThought("A"), uks.GetOrAddThought("B") };
    //    Assert.Throws<NotSupportedException>(() =>
    //        uks.HasSequence(targets, uks.Labeled("spelled"), circularSearch: true));
    //}

    [Fact]
    public void CreateMinimumStructure_RegistersNxtNotNxe()
    {
        var uks = new UKS(clear: true);
        uks.CreateMinimumStructureForTests();
        Assert.NotNull(uks.Labeled("NXT"));
        Assert.Null(uks.Labeled("NXE"));
    }
}

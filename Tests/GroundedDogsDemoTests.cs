using BrainSimulator;
using BrainSimulator.Modules;
using System.IO;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public class GroundedDogsDemoTests
{
    [Fact]
    public void DemoImportsImagesAndDiscoversOneAnonymousDogClass()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks;
        var visualInput = new ModuleVisualInput { theUKS = uks };
        visualInput.EnsureVocabulary();
        foreach (string observation in new[] { "Fido.txt", "Rover.txt", "Spot.txt" })
        {
            string subjectLabel = Path.GetFileNameWithoutExtension(observation);
            bool subjectAlreadyExisted = uks.Labeled(subjectLabel) is not null;
            uks.ImportTextFile(LocateObservationFile(observation));
            if (!subjectAlreadyExisted)
                uks.GetOrAddThought(subjectLabel, "Object");
        }

        Thought fido = uks.Labeled("Fido")!;
        Thought rover = uks.Labeled("Rover")!;
        Thought spot = uks.Labeled("Spot")!;
        Thought imageRoot = uks.Labeled("Image")!;
        Thought visualElementRoot = uks.Labeled("VisualElement")!;
        Thought propertyRoot = uks.Labeled("Property")!;

        Assert.Contains(imageRoot, visualElementRoot.Children);
        Assert.Contains(uks.Labeled("Fido.png")!, imageRoot.Children);
        Assert.Contains(uks.Labeled("isGrounding")!, propertyRoot.Children);
        Assert.Contains(uks.Labeled("isAnonymousClass")!, propertyRoot.Children);
        Assert.Equal("Fido.png",
            GroundedImageResolver.GetGroundingImageThought(fido)?.Label);
        Assert.EndsWith("Fido.png", GroundedImageResolver.ResolveImagePath(fido));
        Assert.Contains(uks.Labeled("color")!, uks.Labeled("brown")!.Parents);
        Assert.Contains(uks.Labeled("color")!, uks.Labeled("black")!.Parents);
        Assert.Contains(uks.Labeled("Unknown")!, uks.Labeled("spotted")!.Parents);
        Assert.DoesNotContain(uks.AtomicThoughts.OfType<Link>(), link =>
            link.LinkType?.Label == "_mm:contains");

        var classCreator = new ModuleClassCreate { theUKS = uks };
        classCreator.DoTheWork();

        Thought learnedClass = uks.Labeled("Object")!.Children.Single(child =>
            child.LinksTo.Any(link => link.LinkType?.Label == "hasProperty" &&
                link.To?.Label == "isAnonymousClass"));
        Assert.Equal(
            new[] { fido, rover, spot }.ToHashSet(),
            learnedClass.Children.ToHashSet());

        var bubbler = new ModuleAttributeBubble { theUKS = uks };
        bubbler.DoTheWork();

        Assert.NotNull(learnedClass.HasLink(uks.Labeled("has")!, uks.Labeled("fur")));
        Assert.NotNull(learnedClass.HasLink(uks.Labeled("has.4")!, uks.Labeled("leg")));
        Assert.NotNull(learnedClass.HasLink(uks.Labeled("can")!, uks.Labeled("bark")));
        Assert.DoesNotContain(learnedClass.LinksTo, link =>
            link.LinkType?.Label == GroundedImageResolver.GroundingLinkType);
        Assert.All(new[] { fido, rover, spot }, dog =>
        {
            Assert.Null(dog.HasLink(uks.Labeled("has")!, uks.Labeled("fur")));
            Assert.Null(dog.HasLink(uks.Labeled("has.4")!, uks.Labeled("leg")));
            Assert.Null(dog.HasLink(uks.Labeled("can")!, uks.Labeled("bark")));
            Assert.NotNull(dog.HasLink(uks.Labeled("hasImage")!));
        });
        Assert.NotNull(fido.HasLink(uks.Labeled("is")!, uks.Labeled("brown")));
        Assert.NotNull(rover.HasLink(uks.Labeled("is")!, uks.Labeled("black")));
        Assert.NotNull(spot.HasLink(uks.Labeled("is")!, uks.Labeled("spotted")));
    }

    [Theory]
    [InlineData("..\\Fido.png")]
    [InlineData("folder/Fido.png")]
    [InlineData("C:\\Dogs\\Fido.png")]
    [InlineData("Fido.txt")]
    public void ResolverRejectsPathsAndUnsupportedFiles(string label)
    {
        Assert.False(GroundedImageResolver.IsSafeImageFileName(label));
    }

    private static string LocateObservationFile(string fileName)
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "UKSContent", "GroundedDogs",
                "Observations", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "BrainSimulator", "UKSContent",
                "GroundedDogs", "Observations", fileName),
        };

        return candidates.First(File.Exists);
    }
}

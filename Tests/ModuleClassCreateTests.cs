using BrainSimulator;
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public class ModuleClassCreateTests
{
    private static UKS.UKS CreateUKS()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks;
        return uks;
    }

    [Fact]
    public void SharedAttributePopulation_CreatesOneAnonymousClass_ThenBubblesAttributes()
    {
        UKS.UKS uks = CreateUKS();
        Thought objectRoot = uks.Labeled("Object")!;
        Thought visualElementRoot = uks.GetOrAddThought("VisualElement", "Thought");
        Thought imageRoot = uks.GetOrAddThought("Image", visualElementRoot);
        Thought hasImage = uks.GetOrAddThought("hasImage", "LinkType");
        hasImage.AddProperty(uks.GetOrAddThought("isGrounding", "Property"));

        Thought fur = uks.GetOrAddThought("fur", objectRoot);
        Thought leg = uks.GetOrAddThought("leg", objectRoot);
        Thought bark = uks.GetOrAddThought("bark", objectRoot);
        Thought has = uks.GetOrAddThought("has", "LinkType");
        Thought hasFour = uks.GetOrAddThought("has.4", "LinkType");
        Thought can = uks.GetOrAddThought("can", "LinkType");

        Thought[] dogs =
        {
            uks.GetOrAddThought("Fido", objectRoot),
            uks.GetOrAddThought("Rover", objectRoot),
            uks.GetOrAddThought("Spot", objectRoot),
        };

        foreach (Thought dog in dogs)
        {
            dog.AddLink(has, fur);
            dog.AddLink(hasFour, leg);
            dog.AddLink(can, bark);
            Thought image = uks.GetOrAddThought(dog.Label + ".png", imageRoot);
            dog.AddLink(hasImage, image);
        }

        var classCreator = new ModuleClassCreate { theUKS = uks };
        classCreator.DoTheWork();

        List<Thought> anonymousClasses = objectRoot.Children.Where(child =>
            child.HasProperty("isAnonymousClass")).ToList();
        Assert.True(anonymousClasses.Count == 1,
            classCreator.debugString + "\nObject children: " +
            string.Join(", ", objectRoot.Children.Select(child =>
                $"{child.Label}[anonymous={child.HasProperty("isAnonymousClass")} children={child.Children.Count}]")));
        Thought anonymousClass = anonymousClasses.Single();
        Assert.StartsWith("class", anonymousClass.Label);
        Assert.Equal(dogs.ToHashSet(), anonymousClass.Children.ToHashSet());
        Assert.All(dogs, dog => Assert.DoesNotContain(objectRoot, dog.Parents));
        Assert.Null(anonymousClass.HasLink(has, fur));
        Assert.DoesNotContain(objectRoot.Children, child => child.Label.StartsWith("Object."));

        var bubbler = new ModuleAttributeBubble { theUKS = uks };
        bubbler.DoTheWork();

        Assert.NotNull(anonymousClass.HasLink(has, fur));
        Assert.NotNull(anonymousClass.HasLink(hasFour, leg));
        Assert.NotNull(anonymousClass.HasLink(can, bark));
        Assert.DoesNotContain(anonymousClass.LinksTo, link => link.LinkType == hasImage);
        Assert.All(dogs, dog =>
        {
            Assert.Null(dog.HasLink(has, fur));
            Assert.Null(dog.HasLink(hasFour, leg));
            Assert.Null(dog.HasLink(can, bark));
            Assert.NotNull(dog.HasLink(hasImage));
        });

        classCreator.DoTheWork();
        Assert.DoesNotContain(anonymousClass.Children, child =>
            child.LinksTo.Any(link => link.LinkType?.Label == "hasProperty" &&
                link.To?.Label == "isAnonymousClass"));
    }

    [Fact]
    public void FewerThanThreeSupportingMembers_DoesNotCreateClass()
    {
        UKS.UKS uks = CreateUKS();
        Thought objectRoot = uks.Labeled("Object")!;
        Thought fur = uks.GetOrAddThought("fur", objectRoot);
        Thought has = uks.GetOrAddThought("has", "LinkType");
        uks.GetOrAddThought("Fido", objectRoot).AddLink(has, fur);
        uks.GetOrAddThought("Rover", objectRoot).AddLink(has, fur);

        var classCreator = new ModuleClassCreate { theUKS = uks };
        classCreator.DoTheWork();

        Assert.DoesNotContain(objectRoot.Children, child =>
            child.HasProperty("isAnonymousClass"));
    }
}

using BrainSimulator;
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public class ModuleMentelModelTests
{
    private static UKS.UKS CreateUKS()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks; // provide the static used by modules
        return uks;
    }

    [Fact]
    public void InitializationPlacesAttentionAtZeroZero()
    {
        var uks = CreateUKS();
        var module = new ModuleMentalModel();

        module.UKSInitializedNotification();
        module.RefreshVisibleContents();

        var position = module.GetAnglesFromCell(module.AttentionCell);
        Assert.Equal(0, position.azimuth.Degrees, 3);
        Assert.Equal(0, position.elevation.Degrees, 3);
        Thought attention = uks.Labeled("attention")!;
        Assert.Contains(uks.Labeled("activeThought")!, attention.Parents);
        Link activation = Assert.Single(attention.LinksTo.Where(link =>
            link.LinkType == Thought.IsA && link.To == uks.Labeled("activeThought")));
        Assert.Equal(TimeSpan.MaxValue, activation.TimeToLive);
        Link location = Assert.Single(attention.LinksFrom.Where(link => link.LinkType?.Label == "_mm:contains"));
        Assert.Same(module.AttentionCell, location.From);
    }

    [Fact]
    public void RotateMentalModel_MovesContainsLinkToNewCell()
    {
        // arrange
        var uks = CreateUKS();
        var module = new ModuleMentalModel();
        module.UKSInitializedNotification();

        Thought obj = uks.GetOrAddThought("obj");
        Thought startCell = module.GetCell(Angle.FromDegrees(0), Angle.FromDegrees(0));
        module.BindThoughtToMentalModel(obj,startCell);

        // ensure the loop in RotateMentalModel will see this cell
        startCell.AddLink("is-a", module.Root);

        // act: rotate slightly to the right on the horizontal axis
        module.RotateMentalModel(Angle.FromDegrees(20), Angle.FromDegrees(20));

        // assert: the object should now be bound to the rotated cell
        Thought expectedCell = module.GetCell(Angle.FromDegrees(20), Angle.FromDegrees(20));
        Link containsAfter = expectedCell.LinksTo
            .FirstOrDefault(l => l.LinkType?.Label == "_mm:contains" && l.To == obj);

        Assert.NotNull(containsAfter);
        Assert.Same(expectedCell, containsAfter!.From);
    }

    [Fact]
    public void BindingState_DistinguishesImaginedFromPerceivedAtSameLocation()
    {
        var uks = CreateUKS();
        var module = new ModuleMentalModel();
        module.UKSInitializedNotification();
        Thought obj = uks.GetOrAddThought("O1", "Object");

        module.ImagineThought(obj, module.Center);

        Assert.Contains(uks.Labeled("imaginedThought")!, obj.Parents);
        Assert.DoesNotContain(uks.Labeled("activeThought")!, obj.Parents);

        module.BindThoughtToMentalModel(obj, module.Center);

        Assert.Contains(uks.Labeled("activeThought")!, obj.Parents);
        Assert.DoesNotContain(uks.Labeled("imaginedThought")!, obj.Parents);
    }

    [Fact]
    public void ConceptCard_ListsKnownAttributesWithoutGroundingMetadata()
    {
        var uks = CreateUKS();
        Thought dogClass = uks.GetOrAddThought("class0", "Object");
        dogClass.AddLink(
            uks.GetOrAddThought("can", "LinkType"),
            uks.GetOrAddThought("bark", "Unknown"));
        dogClass.AddLink(
            uks.GetOrAddThought("has", "LinkType"),
            uks.GetOrAddThought("fur", "Unknown"));
        Thought hasImage = uks.GetOrAddThought("hasImage", "LinkType");
        hasImage.AddProperty(uks.GetOrAddThought("isGrounding", "Property"));
        dogClass.AddLink(hasImage, uks.GetOrAddThought("Fido.png", "Unknown"));

        string summary = ModuleMentalModelDlg.FormatKnownAttributes(uks, dogClass);

        Assert.Contains("[can->bark]", summary);
        Assert.Contains("[has->fur]", summary);
        Assert.DoesNotContain("hasImage", summary);
    }

    [Fact]
    public void RefreshVisibleContents_DoesNotRenewImaginedItems()
    {
        var uks = CreateUKS();
        var module = new ModuleMentalModel();
        module.UKSInitializedNotification();
        Thought imagined = uks.GetOrAddThought("imagined", "Object");
        Thought perceived = uks.GetOrAddThought("perceived", "Object");
        Link imaginedBinding = module.ImagineThought(imagined, module.Center);
        Link perceivedBinding = module.BindThoughtToMentalModel(perceived, module.Center);
        DateTime oldTime = DateTime.Now - TimeSpan.FromSeconds(3);
        imaginedBinding.LastFiredTime = oldTime;
        perceivedBinding.LastFiredTime = oldTime;

        module.RefreshVisibleContents();

        Assert.Equal(oldTime, imaginedBinding.LastFiredTime);
        Assert.True(perceivedBinding.LastFiredTime > oldTime);
    }

    [Fact]
    public void ExpiredFinalAppearance_RemovesImaginedActivationState()
    {
        var uks = CreateUKS();
        var module = new ModuleMentalModel();
        module.UKSInitializedNotification();
        Thought imagined = uks.GetOrAddThought("imagined", "Object");
        Link binding = module.ImagineThought(imagined, module.Center);
        binding.TimeToLive = TimeSpan.FromMilliseconds(10);
        binding.LastFiredTime = DateTime.Now - TimeSpan.FromSeconds(1);

        module.RefreshVisibleContents();

        Assert.DoesNotContain(uks.Labeled("imaginedThought")!, imagined.Parents);
        Assert.Contains(uks.Labeled("inActiveThought")!, imagined.Parents);
        Assert.DoesNotContain(imagined.LinksFrom, link => link.LinkType?.Label == "_mm:contains");
    }

    [Fact]
    public void RefreshVisibleContents_GroundsOnlyCurrentPerceivedVisualContents()
    {
        var uks = CreateUKS();
        var module = new ModuleMentalModel();
        module.UKSInitializedNotification();
        Thought dog = uks.GetOrAddThought("O1", "Object");
        Thought tree = uks.GetOrAddThought("tree1", "Object");
        Thought imagined = uks.GetOrAddThought("imaginedDog", "Object");
        module.BindThoughtToMentalModel(dog, module.Center);
        module.BindThoughtToMentalModel(
            tree, module.GetCell(Angle.FromDegrees(120), Angle.FromDegrees(0)));
        module.ImagineThought(imagined, module.Center);

        module.RefreshVisibleContents();

        Thought self = uks.Labeled("self")!;
        Thought sees = uks.Labeled("sees")!;
        Assert.NotNull(uks.GetLink(self, sees, dog));
        Assert.Null(uks.GetLink(self, sees, tree));
        Assert.Null(uks.GetLink(self, sees, imagined));

        module.UnbindThought(dog);
        module.RefreshVisibleContents();

        Assert.Null(uks.GetLink(self, sees, dog));
    }

    [Fact]
    public void NearerMentalModelMarkersPaintAboveFartherMarkers()
    {
        int far = ModuleMentalModelDlg.MarkerZIndexForDistance(8);
        int near = ModuleMentalModelDlg.MarkerZIndexForDistance(2);

        Assert.True(near > far);
    }
}

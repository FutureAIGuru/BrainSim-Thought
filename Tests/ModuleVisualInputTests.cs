using BrainSimulator;
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public class ModuleVisualInputTests
{
    [Fact]
    public void PresentSelected_CreatesDogAtAttentionAndReplacesCurrentContents()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks;

        var mentalModel = new ModuleMentalModel();
        mentalModel.UKSInitializedNotification();
        Thought selectedCell = mentalModel._cells[5][2];
        mentalModel.SetAttentionCell(selectedCell);

        var visualInput = new ModuleVisualInput { theUKS = uks };
        visualInput.EnsureVocabulary();

        Assert.Null(uks.Labeled("Fido"));
        Assert.Contains("Fido.txt", visualInput.GetObservationFiles());

        visualInput.SelectObservationFile("Fido.txt");
        Thought fido = visualInput.PresentSelected(2, mentalModel)!;

        Assert.Same(uks.Labeled("Fido"), fido);
        Assert.Equal(new[] { fido }, mentalModel.GetCurrentContents());
        Link fidoBinding = Assert.Single(fido.LinksFrom.Where(link =>
            link.LinkType?.Label == "_mm:contains"));
        Assert.Same(selectedCell, fidoBinding.From);
        Assert.Contains(fidoBinding.LinksTo, link =>
            link.LinkType?.Label == "distance" && link.To?.Label == "distance:2");

        visualInput.SelectObservationFile("Rover.txt");
        Thought rover = visualInput.PresentSelected(4, mentalModel)!;

        Assert.NotNull(uks.Labeled("Fido"));
        Assert.Equal(new[] { rover }, mentalModel.GetCurrentContents());
        Assert.Same(selectedCell, mentalModel.AttentionCell);

        Thought objectRoot = uks.Labeled("Object")!;
        rover.RemoveParent(objectRoot);
        Thought learnedClass = uks.GetOrAddThought("classForTest", objectRoot)!;
        rover.AddParent(learnedClass);
        visualInput.PresentSelected(4, mentalModel);
        Assert.DoesNotContain(objectRoot, rover.Parents);
        Assert.Contains(learnedClass, rover.Parents);

        visualInput.ClearPresentation(mentalModel);
        Assert.Empty(mentalModel.GetCurrentContents());
        Assert.Same(selectedCell, mentalModel.AttentionCell);
    }

    [Fact]
    public void VisualVocabulary_PutsImagesUnderVisualElementAndGroundingUnderProperty()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        var module = new ModuleVisualInput { theUKS = uks };

        module.EnsureVocabulary();

        Assert.Contains(uks.Labeled("Image")!, uks.Labeled("VisualElement")!.Children);
        Assert.Contains(uks.Labeled("isGrounding")!, uks.Labeled("Property")!.Children);
        Assert.True(uks.Labeled("hasImage")!.HasProperty("isGrounding"));
    }
}

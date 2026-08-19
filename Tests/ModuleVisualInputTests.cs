using BrainSimulator;
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public class ModuleVisualInputTests
{
    private static (UKS.UKS uks, ModuleMentalModel mentalModel, ModuleVisualInput visualInput)
        CreateDemoModules()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks;
        var text = new ModuleText { theUKS = uks };
        text.UKSInitializedNotification();
        var mentalModel = new ModuleMentalModel { theUKS = uks };
        mentalModel.UKSInitializedNotification();
        var visualInput = new ModuleVisualInput { theUKS = uks };
        visualInput.UKSInitializedNotification();
        return (uks, mentalModel, visualInput);
    }

    [Fact]
    public void PresentSelected_AddsDogsUntilPresentationIsExplicitlyCleared()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks;

        var mentalModel = new ModuleMentalModel();
        mentalModel.UKSInitializedNotification();
        Thought selectedCell = mentalModel._cells[5][2];
        mentalModel.SetAttentionCell(selectedCell);

        var visualInput = new ModuleVisualInput { theUKS = uks };
        visualInput.UKSInitializedNotification();

        Assert.Null(uks.Labeled("O1"));
        Assert.Contains("O1.txt", visualInput.GetObservationFiles());

        visualInput.SelectObservationFile("O1.txt");
        Thought fido = visualInput.PresentSelected(2, mentalModel)!;

        Assert.Same(uks.Labeled("O1"), fido);
        Assert.Equal(new[] { fido }, mentalModel.GetCurrentContents());
        Link fidoBinding = Assert.Single(fido.LinksFrom.Where(link =>
            link.LinkType?.Label == "_mm:contains"));
        Assert.Same(selectedCell, fidoBinding.From);
        Assert.Contains(fidoBinding.LinksTo, link =>
            link.LinkType?.Label == "distance" && link.To?.Label == "distance:2");

        visualInput.SelectObservationFile("O2.txt");
        Thought roverCell = mentalModel._cells[5][4];
        Thought rover = visualInput.PresentSelected(4, mentalModel, roverCell)!;

        Assert.NotNull(uks.Labeled("O1"));
        Assert.Equal(2, mentalModel.GetCurrentContents().Count);
        Assert.Contains(fido, mentalModel.GetCurrentContents());
        Assert.Contains(rover, mentalModel.GetCurrentContents());
        Assert.Same(roverCell, rover.LinksFrom.Single(link =>
            link.LinkType?.Label == "_mm:contains").From);
        Assert.Same(selectedCell, mentalModel.AttentionCell);

        Thought objectRoot = uks.Labeled("Object")!;
        rover.RemoveParent(objectRoot);
        Thought learnedClass = uks.GetOrAddThought("classForTest", objectRoot)!;
        rover.AddParent(learnedClass);
        visualInput.PresentSelected(4, mentalModel, roverCell);
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

        module.UKSInitializedNotification();

        Assert.Contains(uks.Labeled("Image")!, uks.Labeled("VisualElement")!.Children);
        Assert.Contains(uks.Labeled("isGrounding")!, uks.Labeled("Property")!.Children);
        Assert.True(uks.Labeled("hasImage")!.HasProperty("isGrounding"));
    }

    [Fact]
    public void LessonStep_ShowsThenGroundsWordsToAttendedContents()
    {
        var (uks, mentalModel, visualInput) = CreateDemoModules();

        Assert.Contains("Dogs-Language-1.txt", visualInput.GetLessonFiles());
        Assert.True(visualInput.SelectLessonFile("Dogs-Language-1.txt"));
        Assert.Equal("English", visualInput.CurrentLessonLanguage);

        Assert.True(visualInput.StepLesson(mentalModel)); // clear
        Assert.Empty(mentalModel.GetCurrentContents());

        Assert.True(visualInput.StepLesson(mentalModel)); // attention
        Assert.True(mentalModel.GetAnglesFromCell(mentalModel.AttentionCell)
            .azimuth.Degrees < 0);

        Assert.True(visualInput.StepLesson(mentalModel)); // show
        Thought fido = Assert.Single(mentalModel.GetAttendedContents());
        Assert.Equal("O1", fido.Label);
        Thought objectRoot = uks.Labeled("Object")!;
        Thought dogClass = uks.GetOrAddThought("class0", objectRoot)!;
        fido.RemoveParent(objectRoot);
        fido.AddParent(dogClass);

        Assert.True(visualInput.StepLesson(mentalModel)); // hear
        Thought wordThis = uks.Labeled("w:this")!;
        Thought means = uks.Labeled("means")!;
        Link candidate = wordThis.HasLink(means, dogClass)!;
        Assert.NotNull(candidate);
        Assert.True(candidate.isPlastic);
        Assert.Equal(visualInput.MeaningInitialWeight, candidate.Weight, 3);
        Assert.Equal(visualInput.MeaningMaximumWeight, candidate.maxWeight, 3);
        Assert.NotNull(wordThis.HasLink(means, fido));
        Assert.NotNull(uks.Labeled("w:is")!.HasLink(means, dogClass));
        Assert.Null(wordThis.HasLink(
            means, uks.Labeled("mentalModel")!));
        Assert.Null(wordThis.HasLink(
            means, uks.Labeled("Abstract")!));
        Assert.Null(wordThis.HasLink(
            means, uks.Labeled("activeThought")!));
        Assert.Null(wordThis.HasLink(
            means, uks.Labeled("imaginedThought")!));
    }

    [Fact]
    public void LessonHearWithoutAttendedObject_IsSkippedAndRemainsRunnable()
    {
        var (_, mentalModel, visualInput) = CreateDemoModules();
        Assert.True(visualInput.SelectLessonFile("Dogs-Language-1.txt"));
        Assert.True(visualInput.StepLesson(mentalModel)); // clear
        Assert.True(visualInput.StepLesson(mentalModel)); // attention
        Assert.True(visualInput.StepLesson(mentalModel)); // show
        visualInput.ClearPresentation(mentalModel);
        Assert.True(visualInput.StartLesson(1));

        Assert.True(visualInput.StepLesson(mentalModel)); // skipped hear

        Assert.True(visualInput.IsLessonRunning);
        Assert.Equal(4, visualInput.NextLessonStepIndex);
        Assert.Contains("Skipped", visualInput.Status);
        visualInput.PauseLesson();
    }

    [Fact]
    public void LessonStep_RewindsAfterLastEvent()
    {
        var (_, mentalModel, visualInput) = CreateDemoModules();
        Assert.True(visualInput.SelectLessonFile("Dogs-Language-1.txt"));
        int eventCount = visualInput.LessonSteps.Count;
        Assert.True(visualInput.StartLesson(1));

        for (int index = 0; index < eventCount; index++)
        {
            Assert.True(visualInput.StepLesson(mentalModel));
            Assert.All(mentalModel.GetCurrentContents(), thought =>
            {
                Thought cell = thought.LinksFrom.Single(link =>
                    link.LinkType?.Label == "_mm:contains").From!;
                var position = mentalModel.GetAnglesFromCell(cell);
                Assert.True(mentalModel.IsInVisualField(cell),
                    $"{thought.Label} was at Horiz {position.azimuth.Degrees:0.#}, " +
                    $"Vert {position.elevation.Degrees:0.#}");
            });
        }

        Assert.Equal(0, visualInput.NextLessonStepIndex);
        Assert.True(visualInput.IsLessonRunning);
        Assert.Contains("rewound", visualInput.Status,
            System.StringComparison.OrdinalIgnoreCase);
        visualInput.PauseLesson();
    }

    [Fact]
    public void RepeatedDogWord_ConvergesOnSharedAnonymousClass()
    {
        var (uks, mentalModel, visualInput) = CreateDemoModules();
        Thought objectRoot = uks.Labeled("Object")!;
        Thought dogClass = uks.GetOrAddThought("class0", objectRoot)!;

        foreach ((string observation, string objectLabel) in new[]
            { ("O1.txt", "O1"), ("O2.txt", "O2"), ("O3.txt", "O3") })
        {
            visualInput.ClearPresentation(mentalModel);
            visualInput.SelectObservationFile(observation);
            Thought dog = visualInput.PresentSelected(1, mentalModel)!;
            Assert.Equal(objectLabel, dog.Label);
            dog.RemoveParent(objectRoot);
            dog.AddParent(dogClass);
            visualInput.HearPhrase("dog", mentalModel);
        }

        Thought wordDog = uks.Labeled("w:dog")!;
        Thought means = uks.Labeled("means")!;
        Link classMeaning = wordDog.HasLink(means, dogClass)!;
        Assert.NotNull(classMeaning);
        Assert.All(new[] { "O1", "O2", "O3" }, objectLabel =>
        {
            Link individualMeaning = wordDog.HasLink(means, uks.Labeled(objectLabel)!)!;
            Assert.NotNull(individualMeaning);
            Assert.True(classMeaning.Weight > individualMeaning.Weight);
        });
        Assert.Null(wordDog.HasLink(means, objectRoot));

        Link fidoMeaning = wordDog.HasLink(means, uks.Labeled("O1")!)!;
        float beforeUnsupportedObservation = fidoMeaning.Weight;
        visualInput.ClearPresentation(mentalModel);
        visualInput.SelectObservationFile("O2.txt");
        visualInput.PresentSelected(1, mentalModel);
        visualInput.HearPhrase("dog", mentalModel);
        Assert.True(fidoMeaning.Weight < beforeUnsupportedObservation);
    }

    [Fact]
    public void ThisAndIsConvergeOnDogClassWithOnlyDogTrainingExamples()
    {
        var (uks, mentalModel, visualInput) = CreateDemoModules();
        Thought objectRoot = uks.Labeled("Object")!;
        Thought dogClass = uks.GetOrAddThought("class0", objectRoot)!;

        foreach ((string observation, string objectLabel, string spokenName) in new[]
            {
                ("O1.txt", "O1", "Fido"),
                ("O2.txt", "O2", "Rover"),
                ("O3.txt", "O3", "Spot"),
            })
        {
            visualInput.ClearPresentation(mentalModel);
            visualInput.SelectObservationFile(observation);
            Thought dog = visualInput.PresentSelected(1, mentalModel)!;
            Assert.Equal(objectLabel, dog.Label);
            dog.RemoveParent(objectRoot);
            dog.AddParent(dogClass);
            visualInput.HearPhrase($"this is {spokenName}", mentalModel);
        }

        Thought means = uks.Labeled("means")!;
        Link thisMeaning = uks.Labeled("w:this")!
            .HasLink(means, dogClass)!;
        Link isMeaning = uks.Labeled("w:is")!
            .HasLink(means, dogClass)!;
        Assert.Equal(0.3f, thisMeaning.Weight, 3);
        Assert.Equal(0.3f, isMeaning.Weight, 3);
        Assert.True(thisMeaning.Weight > uks.Labeled("w:this")!
            .HasLink(means, uks.Labeled("O1")!)!.Weight);
        Assert.True(isMeaning.Weight > uks.Labeled("w:is")!
            .HasLink(means, uks.Labeled("O1")!)!.Weight);
    }

    [Fact]
    public void MeaningCompetition_UsesBothWordAndAttendedTargetAsEvidence()
    {
        var (uks, mentalModel, visualInput) = CreateDemoModules();
        Thought objectRoot = uks.Labeled("Object")!;
        Thought dogClass = uks.GetOrAddThought("class0", objectRoot)!;
        Thought means = uks.Labeled("means") ??
            uks.GetOrAddThought("means", "LinkType");

        visualInput.SelectObservationFile("O1.txt");
        Thought fido = visualInput.PresentSelected(1, mentalModel)!;
        fido.RemoveParent(objectRoot);
        fido.AddParent(dogClass);
        visualInput.HearPhrase("fido", mentalModel);

        Thought wordFido = uks.Labeled("w:fido")!;
        Link individualMeaning = wordFido.HasLink(means, fido)!;
        Link classMeaning = wordFido.HasLink(means, dogClass)!;
        Assert.Equal(individualMeaning.Weight, classMeaning.Weight, 3);

        Thought activeThought = uks.Labeled("activeThought")!;
        Link obsoleteMeaning = wordFido.AddLink(means, activeThought)!;
        obsoleteMeaning.isPlastic = true;
        obsoleteMeaning.Weight = 0.2f;
        obsoleteMeaning.maxWeight = 1;

        visualInput.ClearPresentation(mentalModel);
        visualInput.SelectObservationFile("O2.txt");
        Thought rover = visualInput.PresentSelected(1, mentalModel)!;
        rover.RemoveParent(objectRoot);
        rover.AddParent(dogClass);

        float individualBefore = individualMeaning.Weight;
        float classBefore = classMeaning.Weight;
        float obsoleteBefore = obsoleteMeaning.Weight;
        visualInput.HearPhrase("rover", mentalModel);

        Assert.Equal(individualBefore, individualMeaning.Weight, 3);
        Assert.True(classMeaning.Weight < classBefore);
        Assert.True(obsoleteMeaning.Weight < obsoleteBefore);
        Assert.Null(uks.Labeled("w:rover")!.HasLink(means, activeThought));
    }

    [Fact]
    public void ReplayingObservation_RecognizesObjectAndDoesNotRestoreBubbledAttributes()
    {
        var (uks, mentalModel, visualInput) = CreateDemoModules();
        Thought objectRoot = uks.Labeled("Object")!;

        foreach (string observation in new[] { "O1.txt", "O2.txt", "O3.txt" })
        {
            visualInput.ClearPresentation(mentalModel);
            visualInput.SelectObservationFile(observation);
            Assert.NotNull(visualInput.PresentSelected(1, mentalModel));
        }

        var classCreator = new ModuleClassCreate { theUKS = uks };
        classCreator.DoTheWork();
        Thought dogClass = objectRoot.Children.Single(child =>
            child.LinksTo.Any(link => link.LinkType?.Label == "hasProperty" &&
                link.To?.Label == "isAnonymousClass"));

        var bubbler = new ModuleAttributeBubble { theUKS = uks };
        bubbler.DoTheWork();

        Thought existing = uks.Labeled("O2")!;
        Assert.Contains(dogClass, existing.Parents);
        Assert.Null(existing.HasLink(uks.Labeled("has")!, uks.Labeled("fur")));
        Assert.Null(existing.HasLink(uks.Labeled("has.4")!, uks.Labeled("leg")));
        Assert.Null(existing.HasLink(uks.Labeled("can")!, uks.Labeled("bark")));

        visualInput.ClearPresentation(mentalModel);
        visualInput.SelectObservationFile("O2.txt");
        DateTime lastFiredBeforeRecognition = existing.LastFiredTime;
        Thought replayed = visualInput.PresentSelected(1, mentalModel)!;

        Assert.Same(existing, replayed);
        Assert.True(existing.LastFiredTime > lastFiredBeforeRecognition);
        Assert.Null(uks.Labeled("O4"));
        Assert.Null(existing.HasLink(uks.Labeled("has")!, uks.Labeled("fur")));
        Assert.Null(existing.HasLink(uks.Labeled("has.4")!, uks.Labeled("leg")));
        Assert.Null(existing.HasLink(uks.Labeled("can")!, uks.Labeled("bark")));
        Assert.NotNull(existing.HasLink(uks.Labeled("is")!, uks.Labeled("black")));
    }

    [Fact]
    public void KnownWord_IsReportedForSeenObjectAndCanImagineIt()
    {
        var (uks, mentalModel, visualInput) = CreateDemoModules();
        Thought means = uks.GetOrAddThought("means", "LinkType");

        visualInput.SelectObservationFile("O1.txt");
        Thought objectThought = visualInput.PresentSelected(1, mentalModel)!;
        Thought word = uks.GetOrAddThought("w:fido", "Word");
        Link meaning = word.AddLink(means, objectThought)!;
        meaning.Weight = 0.8f;

        visualInput.ClearPresentation(mentalModel);
        Thought recognized = visualInput.PresentSelected(1, mentalModel)!;

        Assert.Same(objectThought, recognized);
        Assert.Equal("fido", visualInput.CurrentWord);

        visualInput.ClearPresentation(mentalModel);
        Thought imagined = visualInput.ImagineWord("Fido", 4, mentalModel)!;

        Assert.Same(objectThought, imagined);
        Assert.Contains(objectThought, mentalModel.GetCurrentContents());
        Assert.Equal("fido", visualInput.CurrentWord);
        Assert.Contains("Imagining O1", visualInput.Status);
        Link imaginedBinding = objectThought.LinksFrom.Single(link =>
            link.LinkType?.Label == "_mm:contains");
        Assert.Contains(imaginedBinding.LinksTo, link =>
            link.LinkType?.Label == "distance" && link.To?.Label == "distance:4");
    }

    [Fact]
    public void ImagineWord_PrefersSharedClassWhenClassAndInstanceTie()
    {
        var (uks, mentalModel, visualInput) = CreateDemoModules();
        Thought means = uks.GetOrAddThought("means", "LinkType");
        Thought dogClass = uks.GetOrAddThought("class0", "Object");
        Thought rover = uks.GetOrAddThought("O2", dogClass);
        Thought wordDog = uks.GetOrAddThought("w:dog", "Word");
        wordDog.AddLink(means, dogClass)!.Weight = 0.4f;
        wordDog.AddLink(means, rover)!.Weight = 0.4f;

        Thought imagined = visualInput.ImagineWord("dog", mentalModel)!;

        Assert.Same(dogClass, imagined);
        Assert.Contains(dogClass, mentalModel.GetCurrentContents());
        Assert.DoesNotContain(rover, mentalModel.GetCurrentContents());
    }

    [Fact]
    public void ImaginedThought_IsNeitherLearnedNorSelectedAsAMeaning()
    {
        var (uks, mentalModel, visualInput) = CreateDemoModules();
        Thought means = uks.GetOrAddThought("means", "LinkType");
        Thought dogClass = uks.GetOrAddThought("class0", "Object");
        Thought wordDog = uks.GetOrAddThought("w:dog", "Word");
        wordDog.AddLink(means, dogClass)!.Weight = 1;
        Assert.Same(dogClass, visualInput.ImagineWord("dog", mentalModel));

        visualInput.HearPhrase("chien", mentalModel, "French");

        Thought wordChien = uks.Labeled("w:chien")!;
        Thought imaginedThought = uks.Labeled("imaginedThought")!;
        Link classMeaning = wordChien.HasLink(means, dogClass)!;
        Assert.NotNull(classMeaning);
        Assert.Null(wordChien.HasLink(means, imaginedThought));

        classMeaning.Weight = 1;
        Link obsoleteStructuralMeaning = wordChien.AddLink(means, imaginedThought)!;
        obsoleteStructuralMeaning.Weight = 1;
        Assert.Same(dogClass, visualInput.ImagineWord("chien", mentalModel));
    }

    [Fact]
    public void StrongMeaning_ConsolidatesAwayWeakAlternatives()
    {
        var (uks, mentalModel, visualInput) = CreateDemoModules();
        Thought means = uks.GetOrAddThought("means", "LinkType");
        Thought winner = uks.GetOrAddThought("O1", "Object");
        Thought alternative = uks.GetOrAddThought("O2", "Object");
        Thought word = uks.GetOrAddThought("w:testword", "Word");
        Link winnerMeaning = word.AddLink(means, winner)!;
        winnerMeaning.isPlastic = true;
        winnerMeaning.Weight = 0.9f;
        winnerMeaning.maxWeight = 1;
        Link weakMeaning = word.AddLink(means, alternative)!;
        weakMeaning.isPlastic = true;
        weakMeaning.Weight = 0.4f;
        weakMeaning.maxWeight = 1;
        mentalModel.BindThoughtToMentalModel(winner, mentalModel.AttentionCell);

        visualInput.HearPhrase("testword", mentalModel);

        Assert.NotNull(word.HasLink(means, winner));
        Assert.Null(word.HasLink(means, alternative));
    }

    [Fact]
    public void FrenchGrounding_DoesNotDecayEnglishOnlyWordsAndSharesProperNames()
    {
        var (uks, mentalModel, visualInput) = CreateDemoModules();
        Thought objectRoot = uks.Labeled("Object")!;
        Thought dogClass = uks.GetOrAddThought("class0", objectRoot);
        Thought means = uks.GetOrAddThought("means", "LinkType");

        visualInput.SelectObservationFile("O1.txt");
        Thought fido = visualInput.PresentSelected(1, mentalModel)!;
        fido.RemoveParent(objectRoot);
        fido.AddParent(dogClass);
        visualInput.HearPhrase("dog fido", mentalModel, "English");

        Link dogToClass = uks.Labeled("w:dog")!.HasLink(means, dogClass)!;
        Link dogToFido = uks.Labeled("w:dog")!.HasLink(means, fido)!;
        float classWeightBeforeFrench = dogToClass.Weight;
        float fidoWeightBeforeFrench = dogToFido.Weight;

        visualInput.HearPhrase("chien fido", mentalModel, "French");

        Assert.Equal(classWeightBeforeFrench, dogToClass.Weight, 3);
        Assert.Equal(fidoWeightBeforeFrench, dogToFido.Weight, 3);
        Thought usedInLanguage = uks.Labeled("usedInLanguage")!;
        Thought wordFido = uks.Labeled("w:fido")!;
        Assert.NotNull(wordFido.HasLink(usedInLanguage, uks.Labeled("English")!));
        Assert.NotNull(wordFido.HasLink(usedInLanguage, uks.Labeled("French")!));
        Assert.NotNull(uks.Labeled("w:dog")!
            .HasLink(usedInLanguage, uks.Labeled("English")!));
        Assert.Null(uks.Labeled("w:dog")!
            .HasLink(usedInLanguage, uks.Labeled("French")!));

        Assert.True(visualInput.SelectLessonFile("Chiens-Langue-1.txt"));
        Assert.Equal("French", visualInput.CurrentLessonLanguage);
    }

    [Fact]
    public void SceneLesson_BuildsAnAdditiveEightItemScene()
    {
        var (_, mentalModel, visualInput) = CreateDemoModules();
        Assert.True(visualInput.SelectLessonFile("Scene.txt"));

        int eventCount = visualInput.LessonSteps.Count;
        for (int index = 0; index < eventCount; index++)
            Assert.True(visualInput.StepLesson(mentalModel));

        IReadOnlyList<Thought> contents = mentalModel.GetCurrentContents();
        Assert.Equal(8, contents.Count);
        Assert.Equal(3, contents.Count(thought =>
            GroundedImageResolver.GetGroundingImageThought(thought)?.Label == "Tree.png"));
        Assert.Contains(contents, thought =>
            GroundedImageResolver.GetGroundingImageThought(thought)?.Label == "Sky.png");
        Assert.Contains(contents, thought =>
            GroundedImageResolver.GetGroundingImageThought(thought)?.Label == "Grass.png");
        Thought sky = contents.Single(thought =>
            GroundedImageResolver.GetGroundingImageThought(thought)?.Label == "Sky.png");
        Thought grass = contents.Single(thought =>
            GroundedImageResolver.GetGroundingImageThought(thought)?.Label == "Grass.png");
        int upperVisibleCellCount = mentalModel._cells
            .SelectMany(ring => ring)
            .Count(cell => mentalModel.IsInVisualField(cell) &&
                mentalModel.GetAnglesFromCell(cell).elevation.Degrees > 20);
        int lowerVisibleCellCount = mentalModel._cells
            .SelectMany(ring => ring)
            .Count(cell => mentalModel.IsInVisualField(cell) &&
                mentalModel.GetAnglesFromCell(cell).elevation.Degrees < -20);
        Assert.Equal(upperVisibleCellCount, sky.LinksFrom.Count(link =>
            link.LinkType?.Label == "_mm:contains"));
        Assert.Equal(lowerVisibleCellCount, grass.LinksFrom.Count(link =>
            link.LinkType?.Label == "_mm:contains"));
        Assert.All(contents, thought =>
        {
            Assert.All(thought.LinksFrom.Where(link =>
                link.LinkType?.Label == "_mm:contains"), binding =>
                    Assert.True(mentalModel.IsInVisualField(binding.From!)));
        });
    }
}

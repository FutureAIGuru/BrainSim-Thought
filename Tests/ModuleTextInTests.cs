using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrainSimulator;
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public class ModuleTextInteractionTests
{
    private static UKS.UKS CreateUKS()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks; // required by ModuleWord and AddWordSpelling
        return uks;
    }
    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BrainSim Thought.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("BrainSim Thought repository root not found.");
    }

    [Fact]
    public void TextTaxonomyUsesPhraseAndSpecificTemplateClasses()
    {
        UKS.UKS uks = CreateUKS();
        string contentPath = Path.Combine(
            FindRepositoryRoot(), "BrainSimulator", "UKSContent");
        uks.ImportTextFile(Path.Combine(contentPath, "BasicWords.txt"));
        uks.ImportTextFile(Path.Combine(contentPath, "QueryTemplates.txt"));

        Assert.Null(uks.Labeled("Sentence"));
        Assert.Null(uks.Labeled("Template"));
        Assert.True(uks.Labeled("StatementTemplate").HasAncestor("Phrase"));
        Assert.True(uks.Labeled("QueryTemplate").HasAncestor("Phrase"));
        Assert.True(uks.Labeled("tpl:X_is_a_Y").HasAncestor("StatementTemplate"));
        Assert.True(uks.Labeled("tpl:what_is_X").HasAncestor("QueryTemplate"));
    }


    [Fact]
    public void SubmitText_ReturnsCompletedRelationship()
    {
        var uks = CreateUKS();
        string contentPath = Path.Combine(FindRepositoryRoot(), "BrainSimulator", "UKSContent");
        uks.ImportTextFile(Path.Combine(contentPath, "BasicWords.txt"));
        uks.ImportTextFile(Path.Combine(contentPath, "QueryTemplates.txt"));
        uks.CreateWildcard("w:??", new List<Thought> { "languageElement" });
        var module = new ModuleText { theUKS = uks };

        string answer = module.SubmitText("What is fido")?.ToLower();

        Assert.Equal("fido is a dog", answer);
    }

    [Fact]
    public void SubmitText_ReturnsNewlyAddedRelationship()
    {
        var uks = CreateUKS();
        string contentPath = Path.Combine(FindRepositoryRoot(), "BrainSimulator", "UKSContent");
        uks.ImportTextFile(Path.Combine(contentPath, "BasicWords.txt"));
        uks.ImportTextFile(Path.Combine(contentPath, "QueryTemplates.txt"));
        uks.CreateWildcard("w:??", new List<Thought> { "languageElement" });
        var module = new ModuleText { theUKS = uks };

        module.SubmitText("Fifi is a cat");
        string answer = module.SubmitText("what is Fifi");

        Assert.Equal("Fifi is a cat", answer);
    }

    [Fact]
    public void ActionExemplarUsesGroundedAnonymousMeanings()
    {
        UKS.UKS uks = CreateUKS();
        Thought means = uks.GetOrAddThought("means", "LinkType");
        Thought dogClass = uks.GetOrAddThought("class0", "Object");
        Thought fidoObject = uks.GetOrAddThought("O1", dogClass);
        Thought dogWord = uks.GetOrAddThought("w:dog", "Word");
        Thought fidoWord = uks.GetOrAddThought("w:fido", "Word");
        uks.AddStatement(dogWord, means, dogClass);
        uks.AddStatement(fidoWord, means, fidoObject);

        Thought exemplar = ModuleText.AddActionExemplar(
            "Fido is a dog", "[fido->SET.is-a->dog]");

        Link action = Assert.IsType<Link>(
            exemplar.GetTargetOfFirstLinkOfType("demonstrates"));
        Assert.Same(fidoObject, action.From);
        Assert.Same(dogClass, action.To);
        Assert.Null(uks.Labeled("fido"));
        Assert.Null(uks.Labeled("dog"));
    }

    [Fact]
    public void ActionExemplarRetainsWildcardAndFilterQueryMeaning()
    {
        UKS.UKS uks = CreateUKS();
        Thought color = uks.GetOrAddThought("Color", "Object");

        Thought wildcardExemplar = ModuleText.AddActionExemplar(
            "What is a sheep like", "[sheep->TEST.??->??]");
        Thought filteredExemplar = ModuleText.AddActionExemplar(
            "What color is Fido", "[[Fido->TEST.is->??]->filterBy->Color]");

        Link wildcardAction = Assert.IsType<Link>(
            wildcardExemplar.GetTargetOfFirstLinkOfType("demonstrates"));
        Link filteredAction = Assert.IsType<Link>(
            filteredExemplar.GetTargetOfFirstLinkOfType("demonstrates"));
        Assert.Equal("TEST.??", wildcardAction.LinkType.Label, ignoreCase: true);
        Assert.Same(color, filteredAction.GetTargetOfFirstLinkOfType("filterBy"));
        Assert.True(uks.Labeled("filterBy").HasAncestor("Property"));
    }

    [Fact]
    public void InterpretationDisplayIncludesTheFilterParameter()
    {
        UKS.UKS uks = CreateUKS();
        Link query = new(
            uks.GetOrAddThought("Fido", "Object"),
            uks.GetOrAddThought("is", "LinkType"),
            uks.GetOrAddThought("??", "Object"));
        query.AddLink(uks.GetOrAddThought("filterBy", "LinkType"), uks.GetOrAddThought("Color", "Object"));

        string interpretation = ModuleTextDlg.FormatInterpretation(query);

        Assert.Equal("[[Fido→is→??]→filterBy→Color]", interpretation, ignoreCase: true);
    }

    [Fact]
    public void UKSTreeDisplaysAFilterStoredOnAnExemplarAction()
    {
        UKS.UKS uks = CreateUKS();
        Thought exemplar = ModuleText.AddActionExemplar(
            "What color is Rover", "[[Rover->TEST.is->??]->filterBy->Color]");
        Link demonstrates = Assert.Single(exemplar.LinksTo.Where(link => link.LinkType.Label == "demonstrates"));

        string display = ModuleUKSDlg.FormatLinkForDisplay(demonstrates);

        Assert.Contains("→filterBy→Color", display, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActionExemplarWordsAreNotPrunedFromTheirSequence()
    {
        UKS.UKS uks = CreateUKS();
        Thought mary = uks.GetOrAddThought("w:mary", "Word");
        Thought john = uks.GetOrAddThought("w:john", "Word");
        uks.GetOrAddThought("hasWords", "LinkType");
        mary.isPlastic = true;
        john.isPlastic = true;
        Thought exemplar = ModuleText.AddActionExemplar(
            "How is Mary related to John", "[Mary->TEST.??->John]");
        var wordModule = new ModuleWord { theUKS = uks };

        List<SequenceView> initialSequences = uks.GetSequenceViews(exemplar)
            .Where(view => view.LinkType?.Label == "hasWords")
            .ToList();
        Assert.True(initialSequences.Count == 1,
            "Exemplar links: " + string.Join("; ", exemplar.LinksTo.Select(link => link.ToString())));

        int firstMissingIndex = -1;
        for (int index = 0; index < 60; index++)
        {
            wordModule.AddWordSpelling("filler" + index);
            bool retained = uks.GetSequenceViews(exemplar).Any(view => view.LinkType?.Label == "hasWords");
            if (!retained && firstMissingIndex < 0) firstMissingIndex = index;
        }

        Assert.True(firstMissingIndex < 0, $"The exemplar sequence disappeared after filler{firstMissingIndex}.");
        SequenceView sequence = Assert.Single(uks.GetSequenceViews(exemplar)
            .Where(view => view.LinkType?.Label == "hasWords"));
        Assert.Contains(mary, sequence.Elements);
        Assert.Contains(john, sequence.Elements);
        Assert.DoesNotContain(sequence.Elements, word => word.Label == "--");
        Assert.Same(mary, uks.Labeled("w:mary"));
        Assert.Same(john, uks.Labeled("w:john"));
    }

    [Fact]
    public void CorpusActionAnnotationMayBeSeparatedBySpaces()
    {
        bool found = ModuleText.TrySplitActionAnnotation(
            "Mary loves John.    [Mary->SET.loves->John]", out string phrase, out string action);

        Assert.True(found);
        Assert.Equal("Mary loves John.", phrase);
        Assert.Equal("[Mary->SET.loves->John]", action);
    }

    [Fact]
    public void SeedTemplateStatementUsesGroundedAnonymousMeanings()
    {
        UKS.UKS uks = CreateUKS();
        string contentPath = Path.Combine(
            FindRepositoryRoot(), "BrainSimulator", "UKSContent");
        uks.ImportTextFile(Path.Combine(contentPath, "QueryTemplates.txt"));
        uks.CreateWildcard("w:??", new List<Thought> { "languageElement" });
        Thought means = uks.GetOrAddThought("means", "LinkType");
        Thought dogClass = uks.GetOrAddThought("class0", "Object");
        Thought fidoObject = uks.GetOrAddThought("O1", dogClass);
        uks.AddStatement(uks.GetOrAddThought("w:dog", "Word"), means, dogClass);
        uks.AddStatement(uks.GetOrAddThought("w:fido", "Word"), means, fidoObject);
        ModuleText module = new() { theUKS = uks };

        module.SubmitText("Fido is a dog");

        Assert.Same(fidoObject, module.LastRelationship?.From);
        Assert.Same(dogClass, module.LastRelationship?.To);
        Assert.NotNull(uks.GetLink(fidoObject, uks.Labeled("is-a"), dogClass));
        Assert.Null(uks.Labeled("fido"));
        Assert.Null(uks.Labeled("dog"));
    }
}

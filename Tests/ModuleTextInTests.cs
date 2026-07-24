using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrainSimulator;
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public class ModuleTextInTests
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
        var module = new ModuleTextIn { theUKS = uks };

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
        var module = new ModuleTextIn { theUKS = uks };

        module.SubmitText("Fifi is a cat");
        string answer = module.SubmitText("what is Fifi");

        Assert.Equal("Fifi is a cat", answer);
    }
}

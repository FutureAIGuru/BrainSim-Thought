using System.Collections.Generic;
using System.Linq;
using BrainSimulator;
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public class ModuleWordTests
{
    private static UKS.UKS CreateUKS()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks; // required by ModuleWord and AddWordSpelling
        return uks;
    }

    [Fact]
    public void GetWordSuggestion_ReturnsExactMatchWhenSequenceExists()
    {
        var uks = CreateUKS();
        ModuleWord.AddWordSpelling("CAT");

        var module = new ModuleWord { theUKS = uks };

        string suggestion = module.GetWordSuggestion("cat");

        Assert.Equal("CAT", suggestion);
    }

    [Fact]
    public void GetWordSuggestion_FallsBackToOriginalWhenNoMatch()
    {
        var uks = CreateUKS();
        var module = new ModuleWord { theUKS = uks };

        string suggestion = module.GetWordSuggestion("zzz");

        Assert.Equal("zzz", suggestion);
    }

    [Fact]
    public void AddWordSpelling_CreatesSpellingSequenceAndLink()
    {
        var uks = CreateUKS();

        Thought word = ModuleWord.AddWordSpelling("dog");

        Assert.NotNull(word);
        var spelledLink = word.LinksTo.FirstOrDefault(l => l.LinkType?.Label == "spelled");
        Assert.NotNull(spelledLink);
        Assert.IsType<SeqElement>(spelledLink.To);

        var flat = uks.FlattenSequence((SeqElement)spelledLink.To);
        Assert.Equal(new[] { "D", "O", "G" }, flat.Select(t => t.Label));
    }
}
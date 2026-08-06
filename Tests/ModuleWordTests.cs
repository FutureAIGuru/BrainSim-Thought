using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
        uks.CreateMinimumStructureForTests();
        MainWindow.theUKS = uks; // required by ModuleWord and AddWordSpelling
        return uks;
    }

    [Fact]
    public void GetWordSuggestion_ReturnsExactMatchWhenSequenceExists()
    {
        var uks = CreateUKS();
        var module = new ModuleWord { theUKS = uks };
        module.AddWordSpelling("CAT");


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

        var module = new ModuleWord { theUKS = uks };
        Thought word = module.AddWordSpelling("dog");

        Assert.NotNull(word);
        var spelledLink = word.LinksTo.FirstOrDefault(l => l.LinkType?.Label == "spelled");
        Assert.NotNull(spelledLink);
        Assert.IsType<SeqElement>(spelledLink.To);

        var flat = uks.FlattenSequence((SeqElement)spelledLink.To);
        Assert.Equal(new[] { "c:D", "c:O", "c:G" }, flat.Select(t => t.Label));
    }

    [Fact]
    public void AddWordSpelling_PreservesAOneCharacterWord()
    {
        // The written numeral "4" is a word-level language element whose
        // spelling happens to contain a single sequence element.
        var uks = CreateUKS();
        var module = new ModuleWord { theUKS = uks };

        Thought word = module.AddWordSpelling("4");

        Link spelledLink = word.LinksTo
            .FirstOrDefault(link => link.LinkType?.Label == "spelled");
        Assert.NotNull(spelledLink);
        SeqElement spelling = Assert.IsType<SeqElement>(spelledLink.To);
        Assert.Equal(new[] { "c:4" },
            uks.FlattenSequence(spelling).Select(element => element.Label));
    }

    [Fact]
    public void AddWordSpelling_ReinforcesRepeatedWordsAndWeakensCompetitors()
    {
        // A word which is heard repeatedly should become easier to activate,
        // while a different word which is not heard again should fade.
        var uks = CreateUKS();
        var module = new ModuleWord { theUKS = uks };

        Thought fadingWord = module.AddWordSpelling("fading");
        float initialFadingWeight = fadingWord.Weight;
        Thought repeatedWord = module.AddWordSpelling("repeated");

        // The first observation contributes one unit. Because every observation
        // also applies a small decay, ten additional hits are needed to cross
        // the consolidation threshold of ten units.
        for (int i = 0; i < 10; i++)
            repeatedWord = module.AddWordSpelling("repeated");

        Assert.False(repeatedWord.isPlastic);
        Assert.InRange(repeatedWord.Weight, 10f, 11f);
        Assert.True(fadingWord.Weight < initialFadingWeight,
            $"Expected fading below {initialFadingWeight}, actual {fadingWord.Weight}; " +
            $"Word children: {string.Join(",", uks.Labeled("Word").Children.Select(x => x.Label))}");
        Assert.False(uks.Labeled("spelled").isPlastic);
    }

    [Fact]
    public void ConsolidatedWord_StillDrivesForgettingOfPlasticWords()
    {
        // Hearing a familiar stable word is still intervening experience. It
        // should remain fixed while an unrepeated plastic word becomes weaker.
        var uks = CreateUKS();
        var module = new ModuleWord { theUKS = uks };

        Thought stableWord = module.AddWordSpelling("familiar");
        for (int i = 0; i < 10; i++)
            stableWord = module.AddWordSpelling("familiar");
        Assert.False(stableWord.isPlastic);
        float stableWeightBeforeInterveningWord = stableWord.Weight;

        Thought fadingWord = module.AddWordSpelling("unrepeated");
        float initialFadingWeight = fadingWord.Weight;
        stableWord = module.AddWordSpelling("familiar");
        float expectedFadingWeight = initialFadingWeight * 0.9999f;

        Assert.False(stableWord.isPlastic);
        Assert.True(stableWord.Weight > stableWeightBeforeInterveningWord);
        Assert.Equal(expectedFadingWeight, fadingWord.Weight, 5);
    }

    [Fact]
    public void WordDurabilityCorpus_LoadsPhraseTokensAndScatteredVocabulary()
    {
        // The corpus is a stream of ordinary phrases containing hundreds of
        // distinct words. This test verifies ingestion rather than prescribing
        // the durability outcome which the larger corpus is intended to expose.
        var uks = CreateUKS();
        var module = new ModuleWord { theUKS = uks };
        string corpusPath = Path.Combine(
            FindRepositoryRoot(), "BrainSimulator", "WordFIles",
            "bst_word_durability_corpus.txt");

        int loadedCount = module.LoadWordsFromFile(corpusPath);

        Thought mostFrequent = uks.Labeled("w:the");
        Thought frequent = uks.Labeled("w:dog");
        Thought occasional = uks.Labeled("w:thermometer");
        int corpusWordCount = File.ReadLines(corpusPath)
            .SelectMany(line => line.Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            .Count();
        Assert.Equal(corpusWordCount, loadedCount);
        Assert.True(corpusWordCount > 800);
        Assert.NotNull(mostFrequent);
        Assert.NotNull(frequent);
        Assert.NotNull(occasional);
        Assert.Null(uks.Labeled("w:the dog waits beside the garden gate"));

        // Weight retains frequency information after consolidation rather than
        // being clamped to one. All three repeated words consolidate, and their
        // scores preserve the large differences in observed frequency.
        Assert.False(mostFrequent.isPlastic);
        Assert.False(frequent.isPlastic);
        Assert.False(occasional.isPlastic);
        Assert.True(mostFrequent.Weight > frequent.Weight);
        Assert.True(frequent.Weight > occasional.Weight);
        Assert.True(occasional.Weight >= 10f);

    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        DirectoryInfo directory = new(Path.GetDirectoryName(sourceFilePath)!);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "BrainSim Thought.sln")))
            directory = directory.Parent;

        string retVal = directory?.FullName ??
            throw new DirectoryNotFoundException("Repository root not found.");
        return retVal;
    }
}

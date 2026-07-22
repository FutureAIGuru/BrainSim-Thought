using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrainSimulator;
using BrainSimulator.Modules;
using UKS;
using Xunit;
using Xunit.Abstractions;

namespace BrainSimulator.Tests;

[Collection("ModuleTextPatternLearning")]
public class ModuleTextSequenceBubbleEvaluationTests
{
    private readonly ITestOutputHelper output;

    public ModuleTextSequenceBubbleEvaluationTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void EntireCorpusPhraseSetProducesClassesOfCommonPhraseStructures()
    {
        // No grammar pipeline supplies classes or templates. The sequence
        // discovery mechanism starts with every observed phrase and groups the
        // phrase owners solely by common structures in their hasWords sequences.
        UKS.UKS uks = CreateTextUKS();
        LoadCorpus(uks);

        Thought phraseRoot = uks.Labeled("Phrase");
        List<Thought> discoveredClasses = ModuleText.DiscoverPhraseSequenceClasses();

        Assert.NotEmpty(discoveredClasses);
        Assert.All(discoveredClasses, learnedClass =>
        {
            SequenceView description = Assert.Single(uks.GetSequenceViews(learnedClass)
                .Where(view => view.LinkType?.Label == "hasWords"));
            Assert.True(description.Elements.Count(element => !element.HasAncestor("Wildcard")) >= 2);
            Assert.All(description.Elements.Where(element => element.HasAncestor("Wildcard")), wildcard =>
            {
                Assert.True(wildcard.HasProperty("isWildcard"));
                Assert.StartsWith("??class", wildcard.Label);
                Assert.Contains(wildcard.Parents, parent => parent.Label != "Wildcard");
            });
            Assert.DoesNotContain(learnedClass.Children, member => member is SeqElement);
            Assert.All(learnedClass.Children, member => Assert.Contains(member, phraseRoot.Children));
            Assert.All(uks.GetSequenceViews(learnedClass.Children)
                .Where(view => view.LinkType?.Label == "hasWords"),
                observation => Assert.Contains(observation.Owner, phraseRoot.Children));
        });

        foreach (Thought learnedClass in discoveredClasses.Take(20))
        {
            SequenceView description = uks.GetSequenceViews(learnedClass)
                .Single(view => view.LinkType?.Label == "hasWords");
            output.WriteLine(
                $"{learnedClass.Label} ({learnedClass.Children.Count} phrases): " +
                string.Join(' ', description.Elements.Select(x => x.Label)));
        }
    }

    [Fact]
    public void ProcessExistingTextCanBeRerunWithoutDuplicatingClasses()
    {
        UKS.UKS uks = CreateTextUKS();
        AddPhrase(uks, "the", "dog", "runs");
        AddPhrase(uks, "the", "cat", "runs");
        AddPhrase(uks, "the", "bird", "runs");
        AddPhrase(uks, "the", "goat", "runs");
        AddPhrase(uks, "the", "fish", "swims");

        int firstResult = ModuleText.ProcessTheExistingText();
        int learnedClassCount = uks.Labeled("LearnedClass").Children.Count;
        int thoughtCount = uks.AtomicThoughts.Count;

        int secondResult = ModuleText.ProcessTheExistingText();

        Assert.True(firstResult > 0);
        Assert.Equal(firstResult, secondResult);
        Assert.Equal(learnedClassCount, uks.Labeled("LearnedClass").Children.Count);
        Assert.Equal(thoughtCount, uks.AtomicThoughts.Count);
    }

    private static void LoadCorpus(UKS.UKS uks)
    {
        string corpusPath = Path.Combine(
            FindRepositoryRoot(), "BrainSimulator", "WordFIles", "bst_true_template_corpus.txt");
        foreach (string line in File.ReadLines(corpusPath))
        {
            if (string.IsNullOrWhiteSpace(line) ||
                line.Contains("what", StringComparison.OrdinalIgnoreCase))
                continue;

            string[] words = line.Trim().TrimEnd('.', '!', '?')
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            AddPhrase(uks, words);
        }
    }

    private static void AddPhrase(UKS.UKS uks, params string[] labels)
    {
        Thought phrase = uks.GetOrAddThought("testPhrase*", "Phrase");
        List<Thought> words = labels.Select(label => AddSpelledWord(uks, label)).ToList();
        uks.AddSequenceAndLink(phrase, "hasWords", words);
    }

    private static Thought AddSpelledWord(UKS.UKS uks, string label)
    {
        Thought word = uks.GetOrAddThought(label, "Word");
        if (word.GetTargetOfFirstLinkOfType("spelled") is null)
        {
            List<Thought> letters = label.ToUpperInvariant()
                .Select(letter => uks.GetOrAddThought("c:" + letter, "letter"))
                .ToList();
            uks.AddSequenceAndLink(word, uks.GetOrAddThought("spelled", "LinkType"), letters);
        }
        return word;
    }

    private static UKS.UKS CreateTextUKS()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks;
        uks.GetOrAddThought("LanguageElement", "Thought");
        uks.GetOrAddThought("Phrase", "LanguageElement");
        uks.GetOrAddThought("Word", "LanguageElement");
        uks.GetOrAddThought("hasWords", "LinkType");
        return uks;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BrainSim Thought.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

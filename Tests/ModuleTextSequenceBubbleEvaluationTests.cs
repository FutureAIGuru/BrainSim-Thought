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
    public void EntireCorpusPhraseSetProducesTemplatesWithPhraseEvidence()
    {
        // No grammar pipeline supplies classes or templates. The sequence
        // discovery mechanism starts with every observed phrase and groups the
        // phrase owners solely by common structures in their hasWords sequences.
        UKS.UKS uks = CreateTextUKS();
        LoadCorpus(uks);

        Thought phraseRoot = uks.Labeled("Phrase");
        List<Thought> learnedTemplates = ModuleText.DiscoverPhraseTemplates(10, 1);

        Assert.NotEmpty(learnedTemplates);
        Assert.All(learnedTemplates, learnedTemplate =>
        {
            SequenceView description = Assert.Single(uks.GetSequenceViews(learnedTemplate)
                .Where(view => view.LinkType?.Label == "hasWords"));
            Assert.True(description.Elements.Count(element => !element.HasAncestor("Wildcard")) >= 1);
            Assert.All(description.Elements.Where(element => element.HasAncestor("Wildcard")), wildcard =>
            {
                Assert.True(wildcard.HasProperty("isWildcard"));
                Assert.StartsWith("??class", wildcard.Label);
                Assert.Contains(wildcard.Parents, parent => parent.Label != "Wildcard");
            });
            Assert.Empty(learnedTemplate.Children);
            List<Thought> evidence = learnedTemplate.LinksTo
                .Where(link => link.LinkType?.Label == "evidence")
                .Select(link => link.To)
                .ToList();
            Assert.NotEmpty(evidence);
            Assert.All(evidence, phrase => Assert.Contains(phrase, phraseRoot.Children));
        });

        foreach (Thought learnedTemplate in learnedTemplates.Take(20))
        {
            SequenceView description = uks.GetSequenceViews(learnedTemplate)
                .Single(view => view.LinkType?.Label == "hasWords");
            int evidenceCount = learnedTemplate.LinksTo.Count(link => link.LinkType?.Label == "evidence");
            output.WriteLine(
                $"{learnedTemplate.Label} ({evidenceCount} phrases): " +
                string.Join(' ', description.Elements.Select(x => x.Label)));
        }
    }

    [Fact]
    public void IndependentThousandSentenceCorpusProducesPhraseTemplates()
    {
        // This corpus was generated independently of the original template
        // corpus and contains 1,000 unique simple declarative sentences.
        UKS.UKS uks = CreateTextUKS();
        int phraseCount = LoadCorpus(uks, "bst_simple_1000_corpus.txt");

        List<Thought> learnedTemplates = ModuleText.DiscoverPhraseTemplates();

        Assert.Equal(1000, phraseCount);
        Assert.NotEmpty(learnedTemplates);
        Assert.All(learnedTemplates, template =>
        {
            Assert.Empty(template.Children);
            Assert.True(template.LinksTo.Count(link => link.LinkType?.Label == "evidence") >= 44);
        });

        List<List<string>> patterns = learnedTemplates
            .Select(template => uks.GetSequenceViews(template).Single().Elements
                .Select(element => element.Label)
                .ToList())
            .ToList();
        Assert.Contains(patterns, pattern => pattern.Contains("is"));
        Assert.Contains(patterns, pattern => pattern.Contains("are"));
        Assert.Contains(patterns, pattern => pattern.Contains("has"));
        Assert.Contains(patterns, pattern => pattern.Contains("can"));

        foreach (Thought template in learnedTemplates.Take(20))
        {
            SequenceView description = uks.GetSequenceViews(template).Single();
            int evidenceCount = template.LinksTo.Count(link => link.LinkType?.Label == "evidence");
            output.WriteLine(
                $"{template.Label} ({evidenceCount} phrases): " +
                string.Join(' ', description.Elements.Select(element => element.Label)));
        }
    }

    [Fact]
    public void ProcessExistingTextCanBeRerunWithoutDuplicatingClasses()
    {
        UKS.UKS uks = CreateTextUKS();
        for (int i = 0; i < 44; i++)
            AddPhrase(uks, "the", "subject" + i, "runs");
        AddPhrase(uks, "the", "fish", "swims");

        int firstResult = ModuleText.ProcessTheExistingText();
        int learnedClassCount = uks.Labeled("LearnedClass").Children.Count;
        int learnedTemplateCount = uks.Labeled("LearnedTemplate").Children.Count;
        int thoughtCount = uks.AtomicThoughts.Count;

        int secondResult = ModuleText.ProcessTheExistingText();

        Assert.True(firstResult > 0);
        Assert.Equal(firstResult, secondResult);
        Assert.Equal(learnedClassCount, uks.Labeled("LearnedClass").Children.Count);
        Assert.Equal(learnedTemplateCount, uks.Labeled("LearnedTemplate").Children.Count);
        Assert.Equal(thoughtCount, uks.AtomicThoughts.Count);
    }

    [Fact]
    public void ProcessExistingTextCoalescesSimilarLearnedClassesAfterDiscovery()
    {
        // Class cleanup is a post-processing operation and also runs when there
        // are no new phrase templates to discover.
        UKS.UKS uks = CreateTextUKS();
        Thought root = uks.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought classA = uks.GetOrAddThought("classA", root);
        Thought classB = uks.GetOrAddThought("classB", root);
        foreach (string label in new[] { "a", "b", "c", "d", "e" })
            uks.GetOrAddThought(label, "Word").AddParent(classA);
        foreach (string label in new[] { "b", "c", "d", "e", "f" })
            uks.GetOrAddThought(label, "Word").AddParent(classB);

        ModuleText.ProcessTheExistingText();

        Assert.Single(root.Children);
        Assert.Null(uks.Labeled("classB"));
    }

    [Fact]
    public void AddPhrasePreservesWordsForLaterBatchDiscovery()
    {
        // ModuleWord may decline synchronous spelling creation while its
        // attention stream is active. AddPhrase must still preserve usable word
        // values rather than storing empty p* nodes.
        UKS.UKS uks = CreateTextUKS();
        for (int i = 0; i < 44; i++)
        {
            string result = ModuleText.AddPhrase($"the subject{i} runs");
            Assert.False(result.StartsWith("Error:", StringComparison.Ordinal), result);
        }

        Assert.All(uks.Labeled("Phrase").Children, phrase =>
            Assert.NotNull(phrase.GetTargetOfFirstLinkOfType("hasWords")));
        Assert.Null(uks.Labeled("LearnedClass"));
        Assert.Null(uks.Labeled("LearnedTemplate"));

        ModuleText.ProcessTheExistingText();

        Assert.NotEmpty(uks.Labeled("LearnedClass").Children);
        Assert.NotEmpty(uks.Labeled("LearnedTemplate").Children);
    }

    [Fact]
    public void ManualTextCanUseLearnedTemplateToClassifyNewWordsWithoutChangingCorpusLoading()
    {
        // A manually entered phrase may use an established template as a frame
        // for previously unseen words. Corpus observations remain passive: they
        // are retained as evidence for later batch discovery but do not modify
        // learned classes while they are being loaded.
        UKS.UKS uks = CreateTextUKS();
        Thought learnedClassRoot = uks.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought subjectClass = uks.GetOrAddThought("subjectClass", learnedClassRoot);
        Thought descriptionClass = uks.GetOrAddThought("descriptionClass", learnedClassRoot);
        Thought learnedTemplateRoot = uks.GetOrAddThought("LearnedTemplate", "LanguageElement");
        Thought template = uks.GetOrAddThought("template0", learnedTemplateRoot);
        Thought subject = uks.CreateWildcard("??subjectClass", new List<Thought> { subjectClass });
        Thought description = uks.CreateWildcard("??descriptionClass", new List<Thought> { descriptionClass });
        uks.AddSequenceAndLink(template, "hasWords", new List<Thought>
        {
            uks.GetOrAddThought("w:the", "Word"), subject,
            uks.GetOrAddThought("w:are", "Word"), description
        });

        Thought enteredPhrase = uks.GetOrAddThought("manualPhrase", "Phrase");
        uks.AddSequenceAndLink(enteredPhrase, "hasWords", new List<Thought>
        {
            uks.Labeled("w:the"), uks.GetOrAddThought("w:pigs", "Word"),
            uks.Labeled("w:are"), uks.GetOrAddThought("w:happy", "Word")
        });
        Thought foundTemplate = ModuleText.ApplyExistingTemplatesToPhrase(enteredPhrase);

        Assert.Same(template, foundTemplate);
        Assert.Contains(subjectClass, uks.Labeled("w:pigs").Parents);
        Assert.Contains(descriptionClass, uks.Labeled("w:happy").Parents);
        Assert.Contains(template.LinksTo, link => link.LinkType?.Label == "evidence" &&
            link.To.GetTargetOfFirstLinkOfType("hasWords") is not null);

        string result = ModuleText.AddPhrase("the cows are calm");
        Assert.False(result.StartsWith("Error:", StringComparison.Ordinal), result);
        Assert.DoesNotContain(subjectClass, uks.Labeled("w:cows").Parents);
        Assert.DoesNotContain(descriptionClass, uks.Labeled("w:calm").Parents);
    }

    [Fact]
    public void DuplicatePhraseContentsReinforceExistingTemplatesWithoutCreatingNewOnes()
    {
        // Replaying observations is reinforcement, not discovery of another
        // structure. Significance is therefore based on distinct sequences,
        // while every occurrence remains attached as evidence.
        UKS.UKS uks = CreateTextUKS();
        for (int i = 0; i < 44; i++)
            AddPhrase(uks, "the", "subject" + i, "runs");

        Assert.NotEmpty(ModuleText.DiscoverPhraseTemplates());
        int learnedClassCount = uks.Labeled("LearnedClass").Children.Count;
        int learnedTemplateCount = uks.Labeled("LearnedTemplate").Children.Count;

        for (int i = 0; i < 44; i++)
            AddPhrase(uks, "the", "subject" + i, "runs");
        ModuleText.DiscoverPhraseTemplates();

        Assert.Equal(learnedClassCount, uks.Labeled("LearnedClass").Children.Count);
        Assert.Equal(learnedTemplateCount, uks.Labeled("LearnedTemplate").Children.Count);
        Assert.All(uks.Labeled("LearnedTemplate").Children, template =>
            Assert.Equal(88, template.LinksTo.Count(link => link.LinkType?.Label == "evidence")));
    }

    private static void LoadCorpus(UKS.UKS uks)
    {
        LoadCorpus(uks, "bst_true_template_corpus.txt");
    }

    private static int LoadCorpus(UKS.UKS uks, string fileName)
    {
        string corpusPath = Path.Combine(
            FindRepositoryRoot(), "BrainSimulator", "WordFIles", fileName);
        int phraseCount = 0;
        foreach (string line in File.ReadLines(corpusPath))
        {
            if (string.IsNullOrWhiteSpace(line) ||
                line.Contains("what", StringComparison.OrdinalIgnoreCase))
                continue;

            string[] words = line.Trim().TrimEnd('.', '!', '?')
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            AddPhrase(uks, words);
            phraseCount++;
        }
        return phraseCount;
    }

    private static Thought AddPhrase(UKS.UKS uks, params string[] labels)
    {
        Thought phrase = uks.GetOrAddThought("testPhrase*", "Phrase");
        List<Thought> words = labels.Select(label => AddSpelledWord(uks, label)).ToList();
        uks.AddSequenceAndLink(phrase, "hasWords", words);
        return phrase;
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

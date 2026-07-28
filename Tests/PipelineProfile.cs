using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BrainSimulator.Modules;
using UKS;
using Xunit;
using Xunit.Abstractions;

namespace BrainSimulator.Tests;

/// <summary>
/// Temporary: finds where the text pipeline spends its time.
/// </summary>
[Collection("ModuleTextPatternLearning")]
public class PipelineProfile
{
    private readonly ITestOutputHelper output;
    public PipelineProfile(ITestOutputHelper output) => this.output = output;

    private static UKS.UKS FreshTextUks()
    {
        UKS.UKS uks = new(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks;
        uks.GetOrAddThought("LanguageElement", "Thought");
        uks.GetOrAddThought("Phrase", "LanguageElement");
        uks.GetOrAddThought("Word", "LanguageElement");
        uks.GetOrAddThought("hasWords", "LinkType");
        return uks;
    }

    private static void Ingest(int subjects)
    {
        string[] verbs = { "run", "eat", "swim", "jump" };
        string[] qualities = { "quiet", "quick", "large", "small" };
        for (int i = 0; i < subjects; i++)
        {
            ModuleText.AddPhrase($"the noun{i % 60} is {qualities[i % qualities.Length]}.");
            ModuleText.AddPhrase($"the noun{i % 60} can {verbs[i % verbs.Length]}.");
        }
    }

    [Theory]
    [InlineData(150)]
    [InlineData(600)]
    public void WhereDoesDiscoverySpendItsTime(int subjects)
    {
        UKS.UKS uks = FreshTextUks();
        Ingest(subjects);

        SequenceDiscoveryDiagnostics.Reset();
        SequenceDiscoveryDiagnostics.Enabled = true;
        Stopwatch stopwatch = Stopwatch.StartNew();
        ModuleText.DiscoverPhraseTemplates(10, 1);
        double wallClock = stopwatch.Elapsed.TotalMilliseconds;
        SequenceDiscoveryDiagnostics.Enabled = false;

        output.WriteLine($"=== {subjects * 2} phrases, {uks.AtomicThoughts.Count} Thoughts ===");
        output.WriteLine($"wall clock {wallClock:F0} ms");
        output.WriteLine(SequenceDiscoveryDiagnostics.Report());
    }

    [Fact]
    public void WhereDoesIngestSpendItsTime()
    {
        // AddPhrase does three separable things: turn tokens into word Thoughts,
        // decide the phrase kind, and build the word sequence.
        UKS.UKS uks = FreshTextUks();
        string[] qualities = { "quiet", "quick", "large", "small" };

        // Warm up so the first call does not carry compilation.
        for (int i = 0; i < 20; i++) ModuleText.AddPhrase($"the warm{i} is quiet.");

        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 600; i++)
            ModuleText.AddPhrase($"the noun{i % 60} is {qualities[i % qualities.Length]}.");
        double addPhrase = stopwatch.Elapsed.TotalMilliseconds;

        // The same word lookups on their own.
        stopwatch.Restart();
        for (int i = 0; i < 600; i++)
        {
            uks.Labeled("w:the");
            uks.Labeled($"w:noun{i % 60}");
            uks.Labeled("w:is");
            uks.Labeled($"w:{qualities[i % qualities.Length]}");
        }
        double lookups = stopwatch.Elapsed.TotalMilliseconds;

        // Building a 4-element sequence on its own.
        List<Thought> words = new()
        {
            uks.Labeled("w:the"), uks.Labeled("w:noun0"),
            uks.Labeled("w:is"), uks.Labeled("w:quiet"),
        };
        stopwatch.Restart();
        for (int i = 0; i < 600; i++)
        {
            Thought owner = uks.GetOrAddThought($"seqOwner{i}", "Phrase");
            uks.AddSequenceAndLink(owner, "hasWords", words);
        }
        double sequences = stopwatch.Elapsed.TotalMilliseconds;

        output.WriteLine("Ingest, per 600 phrases");
        output.WriteLine($"  AddPhrase total        {addPhrase,8:F0} ms  ({addPhrase / 600:F3} ms each)");
        output.WriteLine($"  word lookups only      {lookups,8:F0} ms");
        output.WriteLine($"  AddSequenceAndLink only{sequences,8:F0} ms  ({sequences / 600:F3} ms each)");
    }
}

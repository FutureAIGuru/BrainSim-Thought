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
/// Measures the operations which decide how large a UKS can usefully become.
///
/// The numbers printed here are for reading; the assertions are deliberately
/// about the *shape* of the cost rather than its absolute value. An assertion
/// that one measurement is under so many milliseconds says more about the
/// machine than about the code, and fails for the wrong reasons. An assertion
/// that cost grows in proportion to the work still holds on a slow machine, and
/// still fails when an operation quietly becomes quadratic.
/// </summary>
[Collection("ModuleTextPatternLearning")]
public class UksPerformanceBenchmarks
{
    private readonly ITestOutputHelper output;

    public UksPerformanceBenchmarks(ITestOutputHelper output)
    {
        this.output = output;
    }

    /// <summary>
    /// The cost of one call, taken as the median of three timed runs after a
    /// warm-up run. The warm-up keeps first-call JIT compilation out of the
    /// result; the median keeps a single unlucky collection out of it.
    /// </summary>
    private static double MeasureMs(int iterations, Action action)
    {
        action();
        List<double> runs = new();
        for (int run = 0; run < 3; run++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) action();
            runs.Add(stopwatch.Elapsed.TotalMilliseconds / iterations);
        }
        runs.Sort();
        return runs[1];
    }

    private static UKS.UKS FreshUks()
    {
        UKS.UKS uks = new(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks;
        return uks;
    }

    private static UKS.UKS FreshTextUks()
    {
        UKS.UKS uks = FreshUks();
        uks.GetOrAddThought("LanguageElement", "Thought");
        uks.GetOrAddThought("Phrase", "LanguageElement");
        uks.GetOrAddThought("Word", "LanguageElement");
        uks.GetOrAddThought("hasWords", "LinkType");
        return uks;
    }

    /// <summary>
    /// Builds a class with the requested number of direct members.
    /// </summary>
    private static Thought BuildHub(UKS.UKS uks, string label, int width)
    {
        Thought hub = uks.GetOrAddThought(label, "Thought");
        for (int i = 0; i < width; i++)
            uks.GetOrAddThought($"{label}_m{i}", hub);
        return hub;
    }

    [Fact]
    public void GraphAccessorCost()
    {
        UKS.UKS uks = FreshUks();
        Thought hub = BuildHub(uks, "hub", 4000);
        Thought member = uks.Labeled("hub_m0");
        int sink = 0;

        double children = MeasureMs(50, () => sink += hub.Children.Count);
        double linksFrom = MeasureMs(50, () => sink += hub.LinksFrom.Count);
        double parents = MeasureMs(2000, () => sink += member.Parents.Count);
        double linksTo = MeasureMs(2000, () => sink += member.LinksTo.Count);
        double ancestorByLabel = MeasureMs(2000, () => sink += member.HasAncestor("hub") ? 1 : 0);
        double ancestorByThought = MeasureMs(2000, () => sink += member.HasAncestor(hub) ? 1 : 0);
        double labeled = MeasureMs(2000, () => sink += uks.Labeled("hub_m17") is null ? 0 : 1);

        output.WriteLine("Accessor cost (hub has 4000 members)");
        output.WriteLine($"  hub.Children            {children * 1000,9:F1} us/call " +
            $"({children * 1_000_000 / 4000,6:F0} ns per link)");
        output.WriteLine($"  hub.LinksFrom           {linksFrom * 1000,9:F1} us/call");
        output.WriteLine($"  member.Parents          {parents * 1000,9:F2} us/call");
        output.WriteLine($"  member.LinksTo          {linksTo * 1000,9:F2} us/call");
        output.WriteLine($"  member.HasAncestor(str) {ancestorByLabel * 1000,9:F2} us/call");
        output.WriteLine($"  member.HasAncestor(obj) {ancestorByThought * 1000,9:F2} us/call");
        output.WriteLine($"  uks.Labeled(str)        {labeled * 1000,9:F2} us/call");
        output.WriteLine($"  (checksum {sink})");

        // Reading a Thought's own two links must not cost anything like reading a
        // class with four thousand members.
        Assert.True(parents * 200 < children,
            $"member.Parents ({parents:F4} ms) is not cheap relative to a 4000-wide " +
            $"hub.Children ({children:F4} ms); a per-Thought accessor has become " +
            "dependent on unrelated graph size.");
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(4000)]
    [InlineData(16000)]
    public void HubAccessCostByDegree(int width)
    {
        UKS.UKS uks = FreshUks();
        Thought hub = BuildHub(uks, "hub", width);
        int sink = 0;

        double children = MeasureMs(50, () => sink += hub.Children.Count);
        output.WriteLine($"width={width,6}  Children={children * 1000,9:F1} us/call  " +
            $"({children * 1_000_000 / width,6:F0} ns per link)  (checksum {sink})");
    }

    [Fact]
    public void HubAccessGrowsWithDegreeAndNotFasterThanDegree()
    {
        // Four times the members may cost four times as much. It must not cost
        // sixteen times as much, which is what a nested scan over the members
        // would produce.
        UKS.UKS uks = FreshUks();
        Thought small = BuildHub(uks, "small", 2000);
        Thought large = BuildHub(uks, "large", 8000);
        int sink = 0;

        double smallCost = MeasureMs(50, () => sink += small.Children.Count);
        double largeCost = MeasureMs(50, () => sink += large.Children.Count);
        double ratio = largeCost / smallCost;

        output.WriteLine($"Children: 2000 members {smallCost * 1000,8:F1} us, " +
            $"8000 members {largeCost * 1000,8:F1} us, ratio {ratio:F2} " +
            "(4.00 would be exactly proportional)");
        Assert.True(ratio < 8.0,
            $"Reading a class's members cost {ratio:F1}x more for 4x the members; " +
            "the accessor is growing faster than the number of members.");
    }

    [Fact]
    public void StructuralOperationCost()
    {
        // These are the operations which run while knowledge is being tidied:
        // merging two classes, and removing what is left over.
        UKS.UKS uks = FreshUks();
        Thought root = uks.GetOrAddThought("classRoot", "Thought");
        for (int i = 0; i < 4000; i++) uks.GetOrAddThought("filler" + i, root);

        Thought keep = uks.GetOrAddThought("keepClass", root);
        Thought drop = uks.GetOrAddThought("dropClass", root);
        for (int i = 0; i < 200; i++)
        {
            Thought shared = uks.GetOrAddThought("shared" + i, keep);
            shared.AddParent(drop);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        int replaced = uks.ReplaceThoughtReferences(drop, keep);
        double replace = stopwatch.Elapsed.TotalMilliseconds;

        int before = uks.AtomicThoughts.Count;
        stopwatch.Restart();
        for (int i = 0; i < 200; i++) uks.Labeled("filler" + i)?.Delete();
        double deletes = stopwatch.Elapsed.TotalMilliseconds;

        output.WriteLine($"Structural cost (graph of {before} Thoughts)");
        output.WriteLine($"  ReplaceThoughtReferences  {replace,9:F1} ms " +
            $"for one merge ({replaced} links redirected)");
        output.WriteLine($"  Delete                    {deletes / 200,9:F3} ms per Thought " +
            $"({deletes:F0} ms for 200)");
    }

    [Fact]
    public void ClassCoalescingCost()
    {
        UKS.UKS uks = FreshUks();
        Thought root = uks.GetOrAddThought("coalesceRoot", "Thought");
        // Twenty classes which overlap enough to merge, so the work is real
        // rather than a scan which rejects everything immediately.
        for (int classIndex = 0; classIndex < 20; classIndex++)
        {
            Thought learnedClass = uks.GetOrAddThought("cls" + classIndex, root);
            for (int member = 0; member < 40; member++)
                uks.GetOrAddThought("word" + ((classIndex / 2) * 40 + member), "Thought")
                    .AddParent(learnedClass);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        int merges = uks.CoalesceSimilarClasses(root);
        double elapsed = stopwatch.Elapsed.TotalMilliseconds;

        output.WriteLine($"CoalesceSimilarClasses    {elapsed,9:F1} ms " +
            $"for 20 classes of 40 members ({merges} merges)");
    }

    [Theory]
    [InlineData(150)]
    [InlineData(300)]
    [InlineData(600)]
    public void PipelineCostByCorpusSize(int subjects)
    {
        UKS.UKS uks = FreshTextUks();
        string[] verbs = { "run", "eat", "swim", "jump" };
        string[] qualities = { "quiet", "quick", "large", "small" };

        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < subjects; i++)
        {
            ModuleText.AddPhrase($"the noun{i % 60} is {qualities[i % qualities.Length]}.");
            ModuleText.AddPhrase($"the noun{i % 60} can {verbs[i % verbs.Length]}.");
        }
        double ingest = stopwatch.Elapsed.TotalMilliseconds;

        stopwatch.Restart();
        ModuleText.DiscoverPhraseTemplates(10, 1);
        double templates = stopwatch.Elapsed.TotalMilliseconds;

        stopwatch.Restart();
        ModuleText.DiscoverGrammaticalRoles();
        double roles = stopwatch.Elapsed.TotalMilliseconds;

        output.WriteLine(
            $"phrases={subjects * 2,5}  thoughts={uks.AtomicThoughts.Count,6}  " +
            $"ingest={ingest,7:F0} ms  templates={templates,7:F0} ms  roles={roles,6:F0} ms");
    }

    [Fact]
    public void TemplateDiscoveryGrowthExponent()
    {
        // Reports how discovery responds to more observations. An exponent near
        // 1 is proportional; near 2 means every observation is being compared
        // with every other. This is the single number to watch when the corpus
        // grows, so it is printed rather than only asserted.
        (int phrases, double ms) small = TimeDiscovery(200);
        (int phrases, double ms) large = TimeDiscovery(800);
        double exponent =
            Math.Log(large.ms / small.ms) / Math.Log(large.phrases / (double)small.phrases);

        output.WriteLine("Template discovery growth");
        output.WriteLine($"  {small.phrases,5} phrases  {small.ms,8:F0} ms");
        output.WriteLine($"  {large.phrases,5} phrases  {large.ms,8:F0} ms");
        output.WriteLine($"  growth exponent {exponent:F2} " +
            "(1.00 proportional, 2.00 all-pairs)");

        Assert.True(exponent < 2.6,
            $"Template discovery grew with exponent {exponent:F2}; it is now worse " +
            "than comparing every observation with every other.");

        static (int, double) TimeDiscovery(int phraseCount)
        {
            FreshTextUks();
            string[] verbs = { "run", "eat", "swim", "jump" };
            string[] qualities = { "quiet", "quick", "large", "small" };
            for (int i = 0; i < phraseCount / 2; i++)
            {
                ModuleText.AddPhrase($"the noun{i % 60} is {qualities[i % qualities.Length]}.");
                ModuleText.AddPhrase($"the noun{i % 60} can {verbs[i % verbs.Length]}.");
            }
            Stopwatch stopwatch = Stopwatch.StartNew();
            ModuleText.DiscoverPhraseTemplates(10, 1);
            return (phraseCount, stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    [Fact]
    public void ThoughtCreationCostIsIndependentOfGraphSize()
    {
        // Creating knowledge must not get slower as knowledge accumulates.
        UKS.UKS uks = FreshUks();
        Thought root = uks.GetOrAddThought("bulkRoot", "Thought");
        List<double> blocks = new();

        for (int block = 0; block < 8; block++)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 2000; i++)
                uks.GetOrAddThought($"bulk{block}_{i}", root);
            blocks.Add(stopwatch.Elapsed.TotalMilliseconds);
            output.WriteLine($"  thoughts={uks.AtomicThoughts.Count,6}  " +
                $"last 2000 created in {blocks[^1],7:F1} ms");
        }

        // The last block is done with eight times as much already in the UKS as
        // the second. Some growth is tolerable; a trend towards the square is
        // what would make a large UKS unreachable.
        double early = blocks[1];
        double late = blocks[^1];
        Assert.True(late < early * 8 + 20,
            $"Creating Thoughts slowed from {early:F1} ms to {late:F1} ms per 2000 as the " +
            "UKS grew; creation cost now depends on how much is already stored.");
    }
}

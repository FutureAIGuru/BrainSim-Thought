using System;
using System.Linq;
using BrainSimulator.Modules;
using UKS;
using Xunit;
using Xunit.Abstractions;

namespace BrainSimulator.Tests;

[Collection("ModuleTextPatternLearning")]
public class ModuleStressTestTests
{
    private readonly ITestOutputHelper output;

    public ModuleStressTestTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    private static UKS.UKS FreshUks()
    {
        UKS.UKS uks = new(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks;
        return uks;
    }

    [Fact]
    public void AddManyTestItemsCreatesTheNumberAsked()
    {
        // The requested count used to be validated and then ignored: the module
        // always built the same fixed hierarchy no matter what was entered.
        UKS.UKS uks = FreshUks();
        int before = uks.AtomicThoughts.Count;

        string message = ModuleStressTest.AddManyTestItems(1000);
        int added = uks.AtomicThoughts.Count - before;

        output.WriteLine(message);
        Assert.InRange(added, 990, 1010);
    }

    [Fact]
    public void CreatedLabelsAreDistinct()
    {
        // Labels were built by running numbers together, so "B" + 1 + 11 and
        // "B" + 11 + 1 collided and silently became one Thought.
        UKS.UKS uks = FreshUks();
        ModuleStressTest.AddManyTestItems(2000);

        var testLabels = uks.AtomicThoughts
            .Select(thought => thought.Label)
            .Where(label => label.StartsWith(ModuleStressTest.TestPrefix, StringComparison.Ordinal))
            .ToList();

        Assert.Equal(testLabels.Count, testLabels.Distinct(StringComparer.Ordinal).Count());
        Assert.InRange(testLabels.Count, 1990, 2010);
    }

    [Fact]
    public void RejectsCountsOutsideTheAllowedRange()
    {
        FreshUks();
        Assert.StartsWith("Enter a count", ModuleStressTest.AddManyTestItems(0));
        Assert.StartsWith("Enter a count", ModuleStressTest.AddManyTestItems(-5));
        Assert.Contains("limit", ModuleStressTest.AddManyTestItems(2000000));
    }

    [Fact]
    public void BenchmarkReportsEveryMeasuredSection()
    {
        UKS.UKS uks = FreshUks();
        string report = ModuleStressTest.RunBenchmark(3000);
        output.WriteLine(report);

        foreach (string section in new[]
        {
            "Creation", "Graph access", "Reorganizing",
            "Children of a class", "LinksFrom", "Merge two classes", "Delete one Thought",
        })
            Assert.Contains(section, report);
    }

    [Fact]
    public void BenchmarkProgressIsReported()
    {
        FreshUks();
        int updates = 0;
        ModuleStressTest.RunBenchmark(3000, _ => updates++);
        Assert.True(updates > 0, "the benchmark reported no progress at all");
    }

    [Fact]
    public void ClearRemovesOnlyWhatTheStressTestCreated()
    {
        UKS.UKS uks = FreshUks();
        Thought keeper = uks.GetOrAddThought("somethingElse", "Thought");
        int baseline = uks.AtomicThoughts.Count;

        ModuleStressTest.AddManyTestItems(1500);
        Assert.True(uks.AtomicThoughts.Count > baseline);

        string message = ModuleStressTest.ClearTestItems();
        output.WriteLine(message);

        Assert.Equal(baseline, uks.AtomicThoughts.Count);
        Assert.NotNull(uks.Labeled("somethingElse"));
        Assert.Empty(uks.AtomicThoughts.Where(thought =>
            thought.Label.StartsWith(ModuleStressTest.TestPrefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void RepeatedRunsKeepAddingRatherThanSilentlyDoingNothing()
    {
        // Fixed labels meant a second run re-requested the same Thoughts and
        // measured nothing. Each run must contribute its own.
        UKS.UKS uks = FreshUks();
        ModuleStressTest.AddManyTestItems(1000);
        int afterFirst = uks.AtomicThoughts.Count;
        ModuleStressTest.AddManyTestItems(1000);
        int afterSecond = uks.AtomicThoughts.Count;

        Assert.InRange(afterSecond - afterFirst, 990, 1010);
    }
}

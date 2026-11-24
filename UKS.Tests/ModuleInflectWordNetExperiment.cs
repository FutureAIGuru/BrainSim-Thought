using System;
using System.Linq;
using BrainSimulator.Modules;
using Xunit;
using Xunit.Abstractions;

namespace UKS.Tests;

/// <summary>
/// ModuleInflect with its default knowledge, including WordNetWords.txt (common WordNet words). An
/// experiment more than a test: it prints what each spelling is read as, and how long loading and
/// analyzing take. Slow (the load takes a minute or more).
/// </summary>
public class ModuleInflectWordNetExperiment
{
    private readonly ITestOutputHelper output;
    public ModuleInflectWordNetExperiment(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void WhatTheWordNetVocabularyMakesOfSomeForms()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInflect { theUKS = store };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        module.Initialize();
        output.WriteLine($"load: {watch.Elapsed.TotalSeconds:F0} s");

        string[] spellings = ["washes", "horses", "roses", "buses", "kisses", "boxes", "liked", "walked", "cooled",
            "smiled", "hoped", "stopped", "visited", "opened", "quizzed", "flying", "lying", "taller", "quickly",
            "gently", "basically", "fully", "hardly", "largely", "barked", "barks", "mice", "children", "swam",
            "better", "running", "glorped"];
        double total = 0;
        foreach (string spelling in spellings)
        {
            watch.Restart();
            var analyses = module.Analyze(spelling);
            var guesses = analyses.Any(analysis => analysis.Base is not null) ? [] : module.Guesses(spelling);
            total += watch.Elapsed.TotalMilliseconds;
            output.WriteLine($"{spelling} ({watch.Elapsed.TotalMilliseconds:F0} ms):");
            foreach (var analysis in analyses) output.WriteLine("    " + analysis);
            foreach (var guess in guesses.GroupBy(guess => guess.Inflection).Select(group => group.First()))
                output.WriteLine("    guess: " + guess);
        }
        output.WriteLine($"average: {total / spellings.Length:F0} ms per word");
    }
}

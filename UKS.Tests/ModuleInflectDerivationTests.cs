using System;
using System.Linq;
using BrainSimulator.Modules;
using Xunit;
using Xunit.Abstractions;

namespace UKS.Tests;

/// <summary>
/// Derivation by rule (ModuleInflect): -ly makes a manner adverb of an adjective. A derivation is an
/// inflection that derives a word of another class; it is spelled, inherited, preferred and checked
/// like any inflection (UKSContent/Inflections.txt).
/// </summary>
public class ModuleInflectDerivationTests
{
    private readonly ITestOutputHelper output;
    public ModuleInflectDerivationTests(ITestOutputHelper output) => this.output = output;

    private static (UKS store, ModuleInflect module) FreshInflector()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInflect { theUKS = store, KnowledgeFiles = ModuleInflect.CoreFiles };
        module.Initialize();
        return (store, module);
    }

    [Theory]
    [InlineData("quick", "quickly")]
    [InlineData("happy", "happily")]
    [InlineData("gentle", "gently")]
    [InlineData("simple", "simply")]
    [InlineData("true", "truly")]
    [InlineData("full", "fully")]
    [InlineData("basic", "basically")]
    [InlineData("pale", "palely")]     // vowel before l-e: the default
    [InlineData("gray", "grayly")]     // vowel before y: the default
    public void AnAdjectivesAdverbComesFromTheMostSpecificLyRule(string adjective, string adverb)
    {
        var (store, module) = FreshInflector();
        Assert.Equal(adverb, module.ApplyRules(adjective, store.Labeled("adverbial"), out _));
    }

    [Theory]
    [InlineData("quickly", "quick")]
    [InlineData("happily", "happy")]    // not happi
    [InlineData("gently", "gentle")]    // not gent
    [InlineData("simply", "simple")]
    [InlineData("humbly", "humble")]
    [InlineData("idly", "idle")]
    [InlineData("truly", "true")]       // not tru
    [InlineData("fully", "full")]       // not ful
    [InlineData("basically", "basic")]  // not basical
    public void AnUnknownAdverbsMostLikelyAdjectiveComesFirst(string adverb, string expected)
    {
        var (store, module) = FreshInflector();
        var guesses = module.Guesses(adverb).Where(guess => guess.Inflection == store.Labeled("adverbial")).ToList();
        output.WriteLine(string.Join(Environment.NewLine, guesses));
        Assert.Equal(expected, guesses.First().Stem);
    }

    [Fact]
    public void AKnownAdjectivesAdverbIsRecognizedAndSaysWhatClassItMakes()
    {
        var (store, module) = FreshInflector();
        var analysis = Assert.Single(module.Analyze("largely"));
        Assert.Same(store.Labeled("w:large"), analysis.Base);
        Assert.Same(store.Labeled("adverbial"), analysis.Inflection);
        string report = module.Report("largely");
        output.WriteLine(report);
        Assert.Contains("adverbial of w:large → mannerAdverb  (lyDefault)", report);
        Assert.Contains("adverbial → mannerAdverb: largely (lyDefault)", report);
        Assert.Empty(module.Analyze("dogly"));   // a noun takes no adverbial
    }

    // good -> well is stored, so it blocks the rule. hard's adverb is hard; "hardly" is a word of its
    // own (its meaning drifted), never the rule's reading of hard.
    [Fact]
    public void AStoredAdverbBlocksTheRuleAndADriftedWordStandsAlone()
    {
        var (store, module) = FreshInflector();
        foreach (string line in new[]
        {
            "[w:good->is-a->adjective]", "[w:good->spelled->^g o o d]",
            "[w:well->is-a->wordForm]", "[w:well->spelled->^w e l l]", "[w:good->hasAdverbial->w:well]",
            "[w:hard->is-a->adjective]", "[w:hard->spelled->^h a r d]",
            "[w:hardAdverb->is-a->wordForm]", "[w:hardAdverb->spelled->^h a r d]", "[w:hard->hasAdverbial->w:hardAdverb]",
            "[w:hardly->is-a->mannerAdverb]", "[w:hardly->spelled->^h a r d l y]", "[w:hardly->means->barely]",
        })
            store.ProcessSingleLine(line);

        Assert.Equal("well", module.Forms(store.Labeled("w:good"), store.Labeled("adverbial")).Single().form);
        var well = Assert.Single(module.Analyze("well"));
        Assert.Same(store.Labeled("w:good"), well.Base);
        Assert.Empty(module.Analyze("goodly"));
        Assert.Contains(module.Blocked("goodly"), blocked => blocked.Source.Contains("it is well"));

        string report = module.Report("hardly");
        output.WriteLine(report);
        var hardly = Assert.Single(module.Analyze("hardly"));
        Assert.Same(store.Labeled("w:hardly"), hardly.Base);
        Assert.Null(hardly.Inflection);
        Assert.Contains(module.Blocked("hardly"), blocked => blocked.Source.Contains("it is hard"));
    }
}

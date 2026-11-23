using BrainSimulator.Modules;
using Xunit;

namespace UKS.Tests;

/// <summary>
/// A spelling with no exact match is looked up approximately before it is taken as a new word: one
/// clear candidate (same letters, first and last in place) is read in its place; a doubtful one is
/// asked about, and yes or no decides. Nothing is learned while the question is open.
/// </summary>
public class ModuleInterpretSpellingTests
{
    private static (UKS store, ModuleInterpret module) FreshInterpreter()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        return (store, module);
    }

    private static List<Thought> Letters(UKS store, string spelling) =>
        spelling.Select(store.GetOrAddCharacter).ToList();

    [Fact]
    public void ApproximateSearchScoresJumbledMissingAndExactSpellings()
    {
        UKS store = new();
        store.CreateInitialStructure();
        SeqElement fido = store.CreateSpellingSequence("fido", store.GetOrAddThought("w:fido"));
        Thought options = store.Labeled("ApproximateSequenceSearch");

        float Score(string spelling) => store.FindSequencesByActivation(Letters(store, spelling), options)
            .FirstOrDefault(match => ReferenceEquals(match.seqNode, fido)).confidence;

        Assert.Equal(1f, Score("fido"), 3);
        Assert.Equal(0.9f, Score("fdio"), 3);          // jumbled, first and last in place
        Assert.True(Score("odif") < Score("fdio"));    // first and last out of place
        Assert.True(Score("fio") is > 0 and < 0.9f);   // a letter missing
    }

    [Fact]
    public void AClearMisspellingIsReadAsTheWordItIsCloseTo()
    {
        var (store, module) = FreshInterpreter();
        module.Input("Fido is a dog.", rememberUnknownWords: true);

        module.Input("Fdio is a cat.", rememberUnknownWords: true);

        Assert.Equal("OK. (I read \"fdio\" as \"fido\".)", module.Answer);
        Assert.NotNull(store.GetLink(store.Labeled("fido"), store.Labeled("is-a"), store.Labeled("cat")));
        Assert.Null(store.Labeled("w:fdio"));
    }

    [Fact]
    public void ADoubtfulMisspellingIsAskedAboutAndYesReadsItThatWay()
    {
        var (store, module) = FreshInterpreter();
        int labels = store.Labels.Count;

        module.Input("Teh dog is brown.", rememberUnknownWords: true);
        Assert.Equal("Did you mean \"the\"?", module.Answer);
        Assert.Equal(labels, store.Labels.Count); // nothing learned while asking

        module.Input("yes", rememberUnknownWords: true);
        Assert.Equal("OK. (I read \"teh\" as \"the\".)", module.Answer);
        Assert.Null(store.Labeled("w:teh"));
    }

    [Fact]
    public void NoTakesTheSpellingAsANewWord()
    {
        var (store, module) = FreshInterpreter();

        module.Input("Fido is a dgo.", rememberUnknownWords: true);
        Assert.Equal("Did you mean \"dog\"?", module.Answer);

        module.Input("No.", rememberUnknownWords: true);
        Assert.Equal("OK.", module.Answer);
        Assert.NotNull(store.Labeled("w:dgo"));
    }

    [Fact]
    public void AnythingButYesOrNoIsReadAsNewInput()
    {
        var (store, module) = FreshInterpreter();

        module.Input("Teh dog is brown.", rememberUnknownWords: true);
        module.Input("The dog is black.", rememberUnknownWords: true);

        Assert.Equal("OK.", module.Answer);
        module.Input("yes", rememberUnknownWords: true); // the question is no longer open
        Assert.Null(store.Labeled("w:teh"));
        Assert.NotEqual("OK. (I read \"teh\" as \"the\".)", module.Answer);
    }
}

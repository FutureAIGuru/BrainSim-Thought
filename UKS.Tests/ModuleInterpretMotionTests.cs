using BrainSimulator.Modules;
using Xunit;

namespace UKS.Tests;

/// <summary>
/// Motion and auxiliaries: "Fido went outside." is [fido→goes.past→outside]. In "Where did Fido go?"
/// the do auxiliary lends its tense to the verb (did go = went); "has gone" (the perfect) is read as
/// the past for now.
/// </summary>
public class ModuleInterpretMotionTests
{
    private static (UKS store, ModuleInterpret module) FreshInterpreter()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        return (store, module);
    }

    [Fact]
    public void WhereSomethingWentCanBeAskedWithDidOrHas()
    {
        var (store, module) = FreshInterpreter();
        string report = module.Input("Fido went outside.", rememberUnknownWords: true);

        Assert.Equal("OK.", module.Answer);
        Assert.Contains("[fido→goes.past.!→outside]", report);
        Assert.NotNull(store.GetLink(store.Labeled("fido"), store.Labeled("goes.past"), store.Labeled("outside")));

        Assert.Contains("[fido→goes.past.?→??]", module.Input("Where did Fido go?", rememberUnknownWords: true));
        Assert.Equal("Fido went outside.", module.Answer);
        module.Input("Where has Fido gone?", rememberUnknownWords: true);
        Assert.Equal("Fido went outside.", module.Answer);
    }

    [Fact]
    public void WhetherSomethingWentSomewhereCanBeAsked()
    {
        var (_, module) = FreshInterpreter();
        module.Input("Fido went outside.", rememberUnknownWords: true);

        module.Input("Did Fido go outside?", rememberUnknownWords: true);
        Assert.Equal("Fido went outside.", module.Answer);
        module.Input("Has Fido gone outside?", rememberUnknownWords: true);
        Assert.Equal("Fido went outside.", module.Answer);
        module.Input("Did Fido go inside?", rememberUnknownWords: true);
        Assert.Equal("I don't know.", module.Answer);
    }

    // "did" lends the past to any verb: a past-tense fact is asked about with "did", not "does".
    [Fact]
    public void DidAsksAboutThePast()
    {
        var (_, module) = FreshInterpreter();
        module.Input("Fido chased the cat.", rememberUnknownWords: true);

        module.Input("Did Fido chase the cat?", rememberUnknownWords: true);
        Assert.Equal("Fido chased the cat.", module.Answer);
        module.Input("Does Fido chase the cat?", rememberUnknownWords: true);
        Assert.Equal("I don't know.", module.Answer);
    }
}

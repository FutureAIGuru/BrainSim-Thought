using System.Linq;
using BrainSimulator.Modules;
using Xunit;

namespace UKS.Tests;

/// <summary>
/// A described noun phrase refers to something; it says nothing about its class. "The brown dog"
/// picks out an existing dog that is brown (asking which if several are), or introduces one;
/// "brown dogs" and "a brown dog" name a kind, an anonymous subclass found or made.
/// BasicWords says Fido is a brown dog.
/// </summary>
public class ModuleInterpretReferenceTests
{
    private static (UKS store, ModuleInterpret module) FreshInterpreter()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        return (store, module);
    }

    private static bool Is(UKS store, Thought thought, string attribute) =>
        store.GetLink(thought, store.Labeled("is"), store.Labeled(attribute)) is not null;

    // The children of a class this module made, carrying the given property.
    private static Thought[] Made(UKS store, string kind, string property) =>
        store.Labeled(kind).Children.Where(child => child.LinksTo.Any(link =>
            link.LinkType?.Label == "hasProperty" && link.To == store.Labeled(property))).ToArray();

    [Fact]
    public void TheBlackDogIsADogThatIsBlackNotEveryDog()
    {
        var (store, module) = FreshInterpreter();

        module.Input("The black dog is brown.", rememberUnknownWords: true);

        Assert.Equal("OK.", module.Answer);
        Thought blackDog = Assert.Single(Made(store, "dog", "isInstance"));
        Assert.True(Is(store, blackDog, "black"));
        Assert.True(Is(store, blackDog, "brown"));
        Assert.False(Is(store, store.Labeled("dog"), "black"));
        Assert.False(Is(store, store.Labeled("dog"), "brown"));
    }

    [Fact]
    public void TheSameDescriptionFindsTheSameDog()
    {
        var (store, module) = FreshInterpreter();
        module.Input("The black dog is brown.", rememberUnknownWords: true);

        module.Input("The black dog is big.", rememberUnknownWords: true);
        module.Input("Is the black dog brown?", rememberUnknownWords: true);

        Assert.Equal("The black dog is brown.", module.Answer);
        Assert.True(Is(store, Assert.Single(Made(store, "dog", "isInstance")), "big"));
    }

    [Fact]
    public void ANamedThingCanBeTheOneDescribed()
    {
        var (store, module) = FreshInterpreter();

        module.Input("The brown dog is big.", rememberUnknownWords: true);

        Assert.True(Is(store, store.Labeled("fido"), "big"));
        Assert.Empty(Made(store, "dog", "isInstance"));
    }

    [Fact]
    public void WhenSeveralFitItAsksWhich()
    {
        var (store, module) = FreshInterpreter();
        module.Input("Rover is a dog.", rememberUnknownWords: true);
        module.Input("Rover is brown.", rememberUnknownWords: true);

        module.Input("The brown dog is big.", rememberUnknownWords: true);

        Assert.Equal("I can't tell which dog you mean: fido or rover.", module.Answer);
        Assert.False(Is(store, store.Labeled("fido"), "big"));
        Assert.False(Is(store, store.Labeled("rover"), "big"));
    }

    [Fact]
    public void AQuestionAboutNothingKnownIsNotAnsweredAndMakesNothing()
    {
        var (store, module) = FreshInterpreter();

        module.Input("Is the black dog brown?", rememberUnknownWords: true);

        Assert.Equal("I don't know.", module.Answer);
        Assert.Empty(Made(store, "dog", "isInstance"));
    }

    [Fact]
    public void APluralNamesAKindFoundAgainByTheSingular()
    {
        var (store, module) = FreshInterpreter();

        module.Input("Black dogs are big.", rememberUnknownWords: true);
        module.Input("A black dog is friendly.", rememberUnknownWords: true);

        Thought kind = Assert.Single(Made(store, "dog", "isAnonymousClass"));
        Assert.True(Is(store, kind, "black"));
        Assert.True(Is(store, kind, "big"));
        Assert.True(Is(store, kind, "friendly"));
        Assert.False(Is(store, store.Labeled("dog"), "big"));
    }

    [Fact]
    public void JoiningADescribedKindJoinsItsClassWithItsDescription()
    {
        var (store, module) = FreshInterpreter();

        module.Input("Rover is a small dog.", rememberUnknownWords: true);

        Assert.NotNull(store.GetLink(store.Labeled("rover"), store.Labeled("is-a"), store.Labeled("dog")));
        Assert.True(Is(store, store.Labeled("rover"), "small"));
        Assert.Empty(Made(store, "dog", "isAnonymousClass"));
        Assert.False(Is(store, store.Labeled("dog"), "small"));
    }

    [Fact]
    public void ADescriptionTheWholeClassHasTellsNothingApart()
    {
        var (store, module) = FreshInterpreter();
        module.Input("Dogs are black.", rememberUnknownWords: true);

        module.Input("The black dog is big.", rememberUnknownWords: true);

        Assert.True(Is(store, store.Labeled("dog"), "big"));
        Assert.Empty(Made(store, "dog", "isInstance"));
    }

    [Fact]
    public void UndoForgetsTheDogThatWasIntroduced()
    {
        var (store, module) = FreshInterpreter();
        module.Input("The black dog is brown.", rememberUnknownWords: true);

        module.UndoLastInput();

        Assert.Empty(Made(store, "dog", "isInstance"));
    }
}

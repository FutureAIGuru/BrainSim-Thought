using BrainSimulator.Modules;
using Xunit;
using UKS.Helpers;

namespace UKS.Tests;

/// <summary>
/// Inflected forms are stored words linked from their base word ([w:dog->hasPlural->w:dogs]).
/// Meanings belong to the base word, singular and present tense; a form carries only its spelling.
/// A sentence holds the base word, marked with the inflection the form adds.
/// </summary>
public class ModuleInterpretInflectionTests
{
    private static (UKS store, ModuleInterpret module) FreshInterpreter()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        return (store, module);
    }

    private static List<Link> InflectionLinks(UKS store) => store.Labeled("word").Descendants
        .SelectMany(word => word.LinksTo)
        .Where(link => link.LinkType.HasAncestor(store.Labeled("hasInflection"))).ToList();

    private static List<SeqElement> SentenceElements(UKS store, ModuleInterpret module, string sentence)
    {
        Thought sentenceThought = module.GetSequenceFromText(sentence);
        return store.EnumerateSequenceElements((SeqElement)sentenceThought.GetTargetOfFirstLinkOfType("hasWords")).ToList();
    }

    [Fact]
    public void AFormCarriesOnlyItsSpellingAndItsBaseWordCarriesTheMeaning()
    {
        var (store, _) = FreshInterpreter();
        List<Link> inflections = InflectionLinks(store);
        Assert.Contains(inflections, link => link.From.Label == "w:dog" && link.To.Label == "w:dogs");
        Assert.Contains(inflections, link => link.From.Label == "w:is" && link.To.Label == "w:are");

        foreach (Link inflection in inflections)
        {
            Thought form = inflection.To;
            Assert.Null(form.GetTargetOfFirstLinkOfType("means"));
            Assert.NotNull(form.GetTargetOfFirstLinkOfType("spelled"));
            Assert.False(form.HasAncestor(store.Labeled("wordType")), $"{form.Label} has a word class of its own.");
            Assert.NotNull(inflection.From.GetTargetOfFirstLinkOfType("means"));
        }
    }

    [Theory]
    // Plural nouns, and verbs agreeing with a plural subject.
    [InlineData("dogs", "w:dog")]
    [InlineData("legs", "w:leg")]
    [InlineData("are", "w:is")]
    [InlineData("have", "w:has")]
    [InlineData("do", "w:does")]
    [InlineData("chase", "w:chases")]
    public void AFormIsReadAsItsBaseWordMarkedWithItsInflection(string spelling, string baseWord)
    {
        var (store, module) = FreshInterpreter();

        SeqElement element = Assert.Single(SentenceElements(store, module, spelling));

        Assert.Equal(baseWord, element.VLU.Label);
        Assert.Same(store.Labeled("plural"), element.GetTargetOfFirstLinkOfType("inflectedAs"));
    }

    [Fact]
    public void ABaseWordIsReadWithoutAnInflection()
    {
        var (store, module) = FreshInterpreter();

        SeqElement element = Assert.Single(SentenceElements(store, module, "dog"));

        Assert.Equal("w:dog", element.VLU.Label);
        Assert.Null(element.GetTargetOfFirstLinkOfType("inflectedAs"));
    }

    [Fact]
    public void ThePluralChangesNoMeaningAndIsShownInTheReport()
    {
        var (store, module) = FreshInterpreter();

        string output = module.Input("Dogs have tails.");

        Assert.Contains("Sentence input: ^w:dog w:has w:tail", output);
        Assert.Contains("w:dog (countNoun, plural)", output);
        Assert.Contains("[dog→has.!→tail]", output);
    }

    [Fact]
    public void SentencesWithTheSameBaseWordsAreEachReadWithTheirOwnInflections()
    {
        var (_, module) = FreshInterpreter();
        module.Input("Fido was black.");

        // "Was Fido black?" and "Is Fido black?" share one stored word sequence: ^w:is w:fido w:black.
        module.Input("Was Fido black?");
        Assert.Equal("Fido was black.", module.Answer);
        Assert.Contains("w:is (copula)    ", module.Input("Is Fido black?"));
        Assert.Equal("I don't know.", module.Answer);

        module.Input("The dog is chasing the cat.");
        module.Input("The dog is chases the cat.");
        Assert.Equal("I don't understand. Can you say it another way?", module.Answer);
    }

    [Fact]
    public void AnInflectionThatAddsMeaningMustReachAMeaning()
    {
        var (store, module) = FreshInterpreter();

        // The classification templates say is-a whatever their copula is, so the past would be lost.
        module.Input("Fido was a dog.");

        Assert.Equal("I don't understand. Can you say it another way?", module.Answer);
        Assert.Null(store.GetLink(store.Labeled("fido"), store.Labeled("is-a.past"), store.Labeled("dog")));
    }

    [Fact]
    public void TheReportShowsEachWordsClassesAndInflection()
    {
        var (_, module) = FreshInterpreter();

        string output = module.Input("The dogs are chasing cats.");

        Assert.Contains("w:the (article, isDefinite)    ", output);
        Assert.Contains("w:is (copula, plural)    ", output);
        Assert.Contains("w:chases (actionVerb, presentParticiple)    ", output);
    }

    [Fact]
    public void GenerationSpellsAPluralThroughTheStoredForm()
    {
        var (store, module) = FreshInterpreter();
        var fact = new Link(store.Labeled("cat"), store.Labeled("has.2"), store.Labeled("tail"));

        // w:2 precedes a plural word: w:tails is plural through its inflection, a noun through w:tail.
        Assert.Equal("A cat has 2 tails.", module.GenerateSentence(fact));
    }

    [Fact]
    public void InitializationRemovesTheClassesAndMeaningsAnEarlierVersionGaveAForm()
    {
        var (store, module) = FreshInterpreter();
        Thought dogs = store.Labeled("w:dogs");
        dogs.AddParent(store.Labeled("noun"));
        dogs.AddLink(store.Labeled("means"), store.Labeled("dog"));

        module.Initialize();

        Assert.False(dogs.HasAncestor(store.Labeled("noun")));
        Assert.Null(dogs.GetTargetOfFirstLinkOfType("means"));
        Assert.Equal("w:dog", Assert.Single(SentenceElements(store, module, "dogs")).VLU.Label);
    }
}

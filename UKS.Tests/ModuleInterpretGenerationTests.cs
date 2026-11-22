using BrainSimulator.Modules;
using Xunit;

namespace UKS.Tests;

/// <summary>
/// GenerateSentence runs an assertion template in reverse: the fact binds the roles named in the
/// template's MEANS pattern, and the template's word sequence is expressed in order.
/// </summary>
public class ModuleInterpretGenerationTests
{
    private static (UKS store, ModuleInterpret module) FreshInterpreter()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        return (store, module);
    }

    private static Link Fact(UKS store, string from, string type, string to) =>
        new(store.Labeled(from) ?? store.GetOrAddThought(from), store.Labeled(type) ?? store.GetOrAddThought(type),
            store.Labeled(to) ?? store.GetOrAddThought(to));

    [Theory]
    // Attribute: copulaAttributeTemplate; a bare noun phrase is preferred because it needs no free choice.
    [InlineData("fido", "is", "brown", "Fido is brown.")]
    [InlineData("dog", "is", "black", "Dog is black.")]
    // Classification: the first sentence template whose MEANS unifies, classificationTemplate ("is a").
    [InlineData("fido", "is-a", "dog", "Fido is a dog.")]
    // "a" precedes a word starting with a consonant, "an" one starting with a vowel.
    [InlineData("fido", "is-a", "animal", "Fido is an animal.")]
    // A verb element in the MEANS pattern is bound to the relationship type: w:has means has.!.
    // The plural-agreement forms (have, chase) are passed over for a singular subject.
    [InlineData("dog", "has", "tail", "Dog has tail.")]
    [InlineData("fido", "chases", "cat", "Fido chases cat.")]
    // A fixed MEANS type selects the template: quantityPossession4 says has.4. Digits other than 1
    // precede a plural noun.
    [InlineData("dog", "has.4", "leg", "Dog has 4 legs.")]
    [InlineData("cat", "has.2", "tail", "Cat has 2 tails.")]
    [InlineData("dog", "has.1", "tail", "Dog has 1 tail.")]
    [InlineData("fido", "is.not", "black", "Fido is not black.")]
    [InlineData("dog", "is-a.not", "cat", "Dog is not cat.")]
    // The verb phrase is expressed through verbPhrase1, whose own MEANS is its ongoing verb.
    // An ongoing verb follows a copula, which excludes "be", "does", and "should".
    [InlineData("dog", "chases.ongoing", "cat", "Dog is chasing cat.")]
    public void GeneratesTheSimplestSentenceForEachRelationshipForm(string from, string type, string to, string expected)
    {
        var (store, module) = FreshInterpreter();

        Assert.Equal(expected, module.GenerateSentence(Fact(store, from, type, to)));
    }

    [Fact]
    public void AnElementThatCarriesMeaningIsNeverFilledByAFreeChoice()
    {
        var (store, module) = FreshInterpreter();

        // nounPhrase1 would need some adjective for "the ___ dog", which would also assert [dog->is->___].
        string sentence = module.GenerateSentence(Fact(store, "dog", "is", "brown"));

        Assert.Equal("Dog is brown.", sentence);
    }

    [Fact]
    public void ReturnsNullWhenNoTemplateExpressesTheRelationship()
    {
        var (store, module) = FreshInterpreter();

        // Query and write actions are not facts, even though templates with those MEANS types exist.
        Assert.Null(module.GenerateSentence(Fact(store, "fido", "is.?", "brown")));
        Assert.Null(module.GenerateSentence(Fact(store, "fido", "is.!", "brown")));
        // No verb means plain "eats" (w:ate means eats.past.!).
        Assert.Null(module.GenerateSentence(Fact(store, "fido", "eats", "dinner")));
        // No word means the concept, so the noun phrase cannot be expressed.
        Assert.Null(module.GenerateSentence(Fact(store, "unnamedConcept", "is", "brown")));
    }

    [Fact]
    public void GenerationOnlyReadsTheUKS()
    {
        var (store, module) = FreshInterpreter();
        Link fact = Fact(store, "dog", "chases.ongoing", "cat");
        module.GenerateSentence(fact);
        int labels = ThoughtLabels.GetLabelCount();

        module.GenerateSentence(fact);
        module.GenerateSentence(Fact(store, "fido", "is-a", "dog"));

        Assert.Equal(labels, ThoughtLabels.GetLabelCount());
    }

    [Fact]
    public void WordOrderConstraintsAreUKSKnowledge()
    {
        var (store, module) = FreshInterpreter();
        Assert.Equal("Fido is an animal.", module.GenerateSentence(Fact(store, "fido", "is-a", "animal")));

        // Without the constraint, the first fixed phrase ("is a") is the free choice.
        Link rule = Assert.Single(store.Labeled("w:an").LinksTo, link => link.LinkType == store.Labeled("precedesWordStartingWith"));
        store.Labeled("w:a").RemoveLink(store.Labeled("precedesWordStartingWith"), store.Labeled("consonant"));
        Assert.Same(store.Labeled("vowel"), rule.To);

        Assert.Equal("Fido is a animal.", module.GenerateSentence(Fact(store, "fido", "is-a", "animal")));
    }

    [Fact]
    public void WordsComeFromTheirSpellingAndLearnedWordsCanBeGenerated()
    {
        var (store, module) = FreshInterpreter();
        module.Input("The hungry dog", rememberUnknownWords: true);

        Assert.Equal("Dog is hungry.", module.GenerateSentence(Fact(store, "dog", "is", "hungry")));
    }
}

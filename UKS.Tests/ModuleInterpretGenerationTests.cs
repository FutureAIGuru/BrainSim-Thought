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
    [InlineData("dog", "is", "black", "A dog is black.")]
    // Classification: the first sentence template whose MEANS unifies, classificationTemplate ("is a").
    [InlineData("fido", "is-a", "dog", "Fido is a dog.")]
    // "a" precedes a word starting with a consonant, "an" one starting with a vowel.
    [InlineData("fido", "is-a", "animal", "Fido is an animal.")]
    // A verb element in the MEANS pattern is bound to the relationship type: w:has means has.!.
    // The plural-agreement forms (have, chase) are passed over for a singular subject.
    [InlineData("dog", "has", "tail", "A dog has a tail.")]
    [InlineData("fido", "chases", "cat", "Fido chases a cat.")]
    // A fixed MEANS type selects the template: quantityPossession4 says has.4. Digits other than 1
    // precede a plural noun.
    [InlineData("dog", "has.4", "leg", "A dog has 4 legs.")]
    [InlineData("cat", "has.2", "tail", "A cat has 2 tails.")]
    [InlineData("dog", "has.1", "tail", "A dog has 1 tail.")]
    [InlineData("fido", "is.not", "black", "Fido is not black.")]
    [InlineData("dog", "is-a.not", "cat", "A dog is not a cat.")]
    // The verb phrase is expressed through verbPhrase1, whose own MEANS is its ongoing verb.
    // An ongoing verb follows a copula, which excludes "be", "does", and "should".
    [InlineData("dog", "chases.ongoing", "cat", "A dog is chasing a cat.")]
    [InlineData("dog", "eats.ongoing", "dinner", "A dog is eating dinner.")]
    // A tense is the base verb's meaning plus what the past tense form adds.
    [InlineData("fido", "eats", "dinner", "Fido eats dinner.")]
    [InlineData("fido", "eats.past", "dinner", "Fido ate dinner.")]
    [InlineData("fido", "chases.past", "cat", "Fido chased a cat.")]
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

        Assert.Equal("A dog is brown.", sentence);
    }

    [Fact]
    public void ReturnsNullWhenNoTemplateExpressesTheRelationship()
    {
        var (store, module) = FreshInterpreter();

        // Query and write actions are not facts, even though templates with those MEANS types exist.
        Assert.Null(module.GenerateSentence(Fact(store, "fido", "is.?", "brown")));
        Assert.Null(module.GenerateSentence(Fact(store, "fido", "is.!", "brown")));
        // No verb means "likes".
        Assert.Null(module.GenerateSentence(Fact(store, "fido", "likes", "dinner")));
        // No inflection adds "future" to the meaning of w:eats.
        Assert.Null(module.GenerateSentence(Fact(store, "fido", "eats.future", "dinner")));
        // No word means the concept, so the noun phrase cannot be expressed.
        Assert.Null(module.GenerateSentence(Fact(store, "unnamedConcept", "is", "brown")));
    }

    [Fact]
    public void GenerationOnlyReadsTheUKS()
    {
        var (store, module) = FreshInterpreter();
        Link fact = Fact(store, "dog", "chases.ongoing", "cat");
        module.GenerateSentence(fact);
        int labels = store.Labels.Count;

        module.GenerateSentence(fact);
        module.GenerateSentence(Fact(store, "fido", "is-a", "dog"));

        Assert.Equal(labels, store.Labels.Count);
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

        Assert.Equal("A dog is hungry.", module.GenerateSentence(Fact(store, "dog", "is", "hungry")));
    }
}

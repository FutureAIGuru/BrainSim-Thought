using System.Text.RegularExpressions;
using BrainSimulator.Modules;
using Xunit;

namespace UKS.Tests;

/// <summary>
/// The trial sentences in BrainSimulator/UKSContent/InterpretTrials.txt, run headlessly through
/// ModuleInterpret.Input() exactly as the dialog does (rememberUnknownWords: true). Each case starts
/// from a fresh UKS, as the trial file instructs. Expected relationships use the file's own notation.
/// </summary>
public class ModuleInterpretTrialTests
{
    private static (UKS store, ModuleInterpret module) FreshInterpreter()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        return (store, module);
    }

    /// <summary>Finds the stored relationship written as [from->type->to] in the trial file.</summary>
    private static Link Find(UKS store, string expected)
    {
        string[] parts = expected.Trim('[', ']').Split("->");
        Assert.Equal(3, parts.Length);
        Thought from = store.Labeled(parts[0]), type = store.Labeled(parts[1]), to = store.Labeled(parts[2]);
        return from is null || type is null || to is null ? null : store.GetLink(from, type, to);
    }

    private static void AssertStored(UKS store, string output, params string[] expected)
    {
        Assert.Contains("Executed relationships", output);
        foreach (string relationship in expected)
            Assert.True(Find(store, relationship) is not null, $"Expected {relationship} to be stored.\n\n{output}");
    }

    // 1. CLASSIFICATION AND ATTRIBUTES
    [Theory]
    [InlineData("Fido is a dog.", "[fido->is-a->dog]")]
    [InlineData("Fido is an animal.", "[fido->is-a->animal]")]
    [InlineData("Dogs are animals.", "[dog->is-a->animal]")]
    [InlineData("Cats are animals.", "[cat->is-a->animal]")]
    [InlineData("Fido is brown.", "[fido->is->brown]")]
    [InlineData("Fido is green.", "[fido->is->green]")]
    [InlineData("Dogs are black.", "[dog->is->black]")]
    [InlineData("Cats are large.", "[cat->is->large]")]
    public void Section1_ClassificationAndAttributes(string sentence, string expected)
    {
        var (store, module) = FreshInterpreter();
        AssertStored(store, module.Input(sentence, rememberUnknownWords: true), expected);
    }

    // 2. NOUN PHRASES, VERBS, AND POSSESSION
    [Theory]
    [InlineData("The black dog is brown.", "[dog->is->black]", "[dog->is->brown]")]
    [InlineData("A small cat is an animal.", "[cat->is->small]", "[cat->is-a->animal]")]
    [InlineData("Rover is a small dog.", "[rover->is-a->dog]", "[rover->is->small]")]
    [InlineData("Fido is a small dog.", "[fido->is-a->dog]", "[fido->is->small]")]
    [InlineData("Rover is a big dog.", "[rover->is-a->dog]", "[rover->is->big]")]
    [InlineData("Fido is a big dog.", "[fido->is-a->dog]", "[fido->is->big]")]
    [InlineData("Dogs are small animals.", "[dog->is-a->animal]", "[dog->is->small]")]
    [InlineData("Dogs have tails.", "[dog->has->tail]")]
    [InlineData("Fido has fur.", "[fido->has->fur]")]
    [InlineData("Fido chases cats.", "[fido->chases->cat]")]
    [InlineData("Dogs chase animals.", "[dog->chases->animal]")]
    [InlineData("The dog is chasing the cat.", "[dog->chases.ongoing->cat]")]
    public void Section2_NounPhrasesVerbsAndPossession(string sentence, params string[] expected)
    {
        var (store, module) = FreshInterpreter();
        AssertStored(store, module.Input(sentence, rememberUnknownWords: true), expected);
    }

    [Fact]
    public void Section2_AnAdjectiveInTheComplementDescribesTheSubject()
    {
        var (store, module) = FreshInterpreter();

        module.Input("Rover is a small dog.", rememberUnknownWords: true);

        Assert.True(store.Labeled("w:rover").HasAncestor("noun"));
        Assert.NotNull(Find(store, "[rover->is->small]"));
        // Not every dog is small.
        Assert.Null(Find(store, "[dog->is->small]"));
        Assert.Equal("OK.", module.Answer);
    }

    [Theory]
    [InlineData("Rover is a big dog.")]
    [InlineData("Fido is a big dog.")]
    public void Section6_AnUnknownAdjectiveInTheComplementIsLearnedAsAnAdjective(string sentence)
    {
        var (store, module) = FreshInterpreter();

        module.Input(sentence, rememberUnknownWords: true);

        // Not an article (a closed class), and not a bare noun phrase guessed on its own.
        Assert.Same(store.Labeled("adjective"), Assert.Single(store.Labeled("w:big").Parents));
        Assert.Null(Find(store, "[dog->is->big]"));
    }

    [Fact]
    public void Section6_NewWordsAreNeverLearnedIntoClosedClasses()
    {
        var (store, module) = FreshInterpreter();
        Assert.True(store.Labeled("article").HasProperty("closedClass"));
        Assert.True(store.Labeled("copula").HasProperty("closedClass"));

        // "zib dog" would match article-noun if zib could become an article.
        module.Input("Fido is a big dog.", rememberUnknownWords: true);
        module.Input("zib dog", rememberUnknownWords: true);

        Assert.DoesNotContain(store.Labeled("w:zib").Parents, parent => parent.HasAncestor(store.Labeled("article")));
        Assert.DoesNotContain(store.Labeled("w:big").Parents, parent => parent.HasAncestor(store.Labeled("article")));
    }

    // 3. NUMERIC QUANTITIES
    [Theory]
    [InlineData("Dogs have 4 legs.", "[dog->has.4->leg]")]
    [InlineData("Fido has 3 legs.", "[fido->has.3->leg]")]
    [InlineData("Cats have 2 tails.", "[cat->has.2->tail]")]
    public void Section3_NumericQuantities(string sentence, string expected)
    {
        var (store, module) = FreshInterpreter();
        string output = module.Input(sentence, rememberUnknownWords: true);
        AssertStored(store, output, expected);
        // The digit is a quantity on the relationship, not an attribute of the object.
        string digit = expected.Split("->")[1].Split('.')[1];
        Assert.Null(Find(store, $"[leg->is->{digit}]"));
        Assert.Null(Find(store, $"[tail->is->{digit}]"));
    }

    // 4. NEGATION
    [Theory]
    [InlineData("Fido is not black.", "[fido->is.not->black]", "[fido->is->black]")]
    [InlineData("Cats are not large.", "[cat->is.not->large]", "[cat->is->large]")]
    [InlineData("Dogs are not cats.", "[dog->is-a.not->cat]", "[dog->is-a->cat]")]
    [InlineData("Fido is not a cat.", "[fido->is-a.not->cat]", "[fido->is-a->cat]")]
    public void Section4_Negation(string sentence, string expected, string positive)
    {
        var (store, module) = FreshInterpreter();
        AssertStored(store, module.Input(sentence, rememberUnknownWords: true), expected);
        Assert.Null(Find(store, positive));
    }

    // 5. QUERIES (on the initial knowledge)
    [Theory]
    [InlineData("Is Fido brown?", "[fido->is->brown]")]
    [InlineData("Is Fido a dog?", "[fido->is-a->dog]")]
    [InlineData("Are cats animals?", "[cat->is-a->animal]")]
    [InlineData("What is brown?", "[fido->is->brown]")]
    [InlineData("What is Fido?", "[fido->is-a->dog]")]
    [InlineData("What is a dog?", "[dog->is-a->animal]")]
    [InlineData("Do dogs have tails?", "[dog->has->tail]")]
    [InlineData("Do cats have fur?", "[cat->has->fur]")]
    public void Section5_QueriesReturnExistingRelationships(string sentence, string expected)
    {
        var (store, module) = FreshInterpreter();
        Link existing = Find(store, expected);
        Assert.NotNull(existing);

        string output = module.Input(sentence, rememberUnknownWords: true);

        Assert.Contains("Query results", output);
        Assert.DoesNotContain("Executed relationships", output);
        string results = output[output.IndexOf("Query results", StringComparison.Ordinal)..];
        Assert.Contains(existing.ToString(), results);
    }

    [Fact]
    public void Section5_UnmatchedQueryReportsNoMatchAndDoesNotAssert()
    {
        var (store, module) = FreshInterpreter();

        string output = module.Input("Is Fido black?", rememberUnknownWords: true);

        Assert.Contains("Query results\nNo matching relationships.", output);
        Assert.Null(Find(store, "[fido->is->black]"));
    }

    private static string Section(string output, string heading)
    {
        int start = output.IndexOf(heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No {heading} section.\n\n{output}");
        string rest = output[(start + heading.Length + 1)..];
        int end = rest.IndexOf("\n\n", StringComparison.Ordinal);
        return end < 0 ? rest : rest[..end];
    }

    [Fact]
    public void Section5_WhatDoesFidoHaveIncludesInheritedPossessions()
    {
        var (store, module) = FreshInterpreter();

        string results = Section(module.Input("What does Fido have?", rememberUnknownWords: true), "Query results");

        // Both are inherited from dog; neither is stored on fido itself.
        Assert.Contains("[fido→has→tail]", results);
        Assert.Contains("[fido→has.4→leg]", results);
        Assert.Null(Find(store, "[fido->has->tail]"));
    }

    [Theory]
    [InlineData("Is Fido not black?", "Fido is not black.", "[fido->is.not->black]")]
    [InlineData("Are dogs not cats?", "Dogs are not cats.", "[dog->is-a.not->cat]")]
    public void Section5_NegativeQueriesMatchOnlyAnExplicitNegative(string query, string assertion, string expected)
    {
        var (store, module) = FreshInterpreter();

        Assert.Contains("Query results\nNo matching relationships.", module.Input(query, rememberUnknownWords: true));
        Assert.Null(Find(store, expected));

        module.Input(assertion, rememberUnknownWords: true);
        Link stored = Find(store, expected);
        Assert.NotNull(stored);
        Assert.Equal(stored.ToString(), Section(module.Input(query, rememberUnknownWords: true), "Query results"));
    }

    [Fact]
    public void Section5_PossessionQueryAsksWithoutAsserting()
    {
        var (store, module) = FreshInterpreter();

        // The assertion "Fido have fur" is inside this question; it must not be executed.
        string output = module.Input("Does Fido have fur?", rememberUnknownWords: true);
        Assert.Contains("Query results\nNo matching relationships.", output);
        Assert.DoesNotContain("Executed relationships", output);
        Assert.Null(Find(store, "[fido->has->fur]"));

        module.Input("Fido has fur.", rememberUnknownWords: true);
        Assert.Contains("[fido→has→fur]", Section(module.Input("Does Fido have fur?", rememberUnknownWords: true), "Query results"));
    }

    // 6. LEARNING AN UNKNOWN NOUN
    [Fact]
    public void Section6_PigsAreLearnedThenDescribed()
    {
        var (store, module) = FreshInterpreter();
        Assert.Null(store.Labeled("w:pig"));

        AssertStored(store, module.Input("Pigs are animals.", rememberUnknownWords: true), "[pig->is-a->animal]");
        Thought pig = store.Labeled("w:pig");
        Assert.NotNull(pig);
        Assert.True(pig.HasAncestor("noun"));
        Assert.Same(store.Labeled("pig"), pig.GetTargetOfFirstLinkOfType("means"));

        AssertStored(store, module.Input("Pigs are brown.", rememberUnknownWords: true), "[pig->is->brown]");
    }

    [Fact]
    public void Section6_TheHungryDogLearnsAnAdjective()
    {
        var (store, module) = FreshInterpreter();

        AssertStored(store, module.Input("The hungry dog", rememberUnknownWords: true), "[dog->is->hungry]");
        Thought hungry = store.Labeled("w:hungry");
        Assert.Same(store.Labeled("adjective"), Assert.Single(hungry.Parents));
        Assert.Same(store.Labeled("hungry"), hungry.GetTargetOfFirstLinkOfType("means"));
    }

    [Theory]
    [InlineData("The hungry dog ate dinner", "adjective")]
    [InlineData("The hungry should be fed", "noun")]
    public void Section6_LongerMatchingPhraseDecidesTheWordClass(string sentence, string wordClass)
    {
        var (store, module) = FreshInterpreter();

        module.Input(sentence, rememberUnknownWords: true);

        // Classification replaces the temporary unknownWordType parent; no direct word parent remains.
        Assert.Same(store.Labeled(wordClass), Assert.Single(store.Labeled("w:hungry").Parents));
    }

    [Fact]
    public void Section6_TheGlobularClusterClassifiesBothUnknownWords()
    {
        var (store, module) = FreshInterpreter();

        module.Input("The globular cluster", rememberUnknownWords: true);

        Assert.Same(store.Labeled("adjective"), Assert.Single(store.Labeled("w:globular").Parents));
        Assert.Same(store.Labeled("noun"), Assert.Single(store.Labeled("w:cluster").Parents));
    }

    // 7. ANSWERS
    [Theory]
    [InlineData("Is Fido brown?", "Fido is brown.")]
    [InlineData("Is Fido a dog?", "Fido is a dog.")]
    [InlineData("What is Fido?", "Fido is a dog.")]
    [InlineData("What does Fido have?", "Fido has tail.", "Fido has 4 legs.")]
    [InlineData("Are cats animals?", "Cat is an animal.")]
    [InlineData("What is a dog?", "Dog is an animal.")]
    [InlineData("Do dogs have tails?", "Dog has tail.")]
    [InlineData("Is Fido black?", "I don't know.")]
    public void Section7_QueryResultsAreAnsweredInEnglish(string sentence, params string[] expected)
    {
        var (_, module) = FreshInterpreter();

        string answer = Section(module.Input(sentence, rememberUnknownWords: true), "Answer");

        Assert.Equal(expected.OrderBy(line => line), answer.Split('\n').OrderBy(line => line));
    }

    [Fact]
    public void AssertionsHaveNoAnswerSection()
    {
        var (_, module) = FreshInterpreter();
        Assert.DoesNotContain("Answer", module.Input("Fido is brown.", rememberUnknownWords: true));
    }

    // The dialog's Answer box shows ModuleInterpret.Answer after each Input.
    [Theory]
    [InlineData("Is Fido brown?", "Fido is brown.")]
    [InlineData("Is Fido black?", "I don't know.")]
    [InlineData("Fido is green.", "OK.")]
    [InlineData("Fido the dog", "I don't understand.")]
    [InlineData("", "Enter a sentence.")]
    public void AnswerHoldsTheReplyToTheLastInput(string sentence, string expected)
    {
        var (_, module) = FreshInterpreter();

        module.Input(sentence, rememberUnknownWords: true);

        Assert.Equal(expected, module.Answer);
    }

    [Fact]
    public void AnswerExplainsUnknownWordsWhenTheyAreNotRemembered()
    {
        var (_, module) = FreshInterpreter();

        module.Input("Fido is zorbly.");

        Assert.Equal("Unknown words: zorbly.", module.Answer);
    }

    /// <summary>
    /// For every relationship stored by Sections 1-4: generate a sentence for it, enter that
    /// sentence into a fresh UKS, and the same relationship must be stored. This checks the
    /// generated wording only through comprehension, not against fixed strings.
    /// </summary>
    [Theory]
    [InlineData("Fido is a dog.", "[fido->is-a->dog]")]
    [InlineData("Fido is an animal.", "[fido->is-a->animal]")]
    [InlineData("Dogs are animals.", "[dog->is-a->animal]")]
    [InlineData("Cats are animals.", "[cat->is-a->animal]")]
    [InlineData("Fido is brown.", "[fido->is->brown]")]
    [InlineData("Fido is green.", "[fido->is->green]")]
    [InlineData("Dogs are black.", "[dog->is->black]")]
    [InlineData("Cats are large.", "[cat->is->large]")]
    [InlineData("The black dog is brown.", "[dog->is->black]")]
    [InlineData("The black dog is brown.", "[dog->is->brown]")]
    [InlineData("A small cat is an animal.", "[cat->is->small]")]
    [InlineData("Rover is a small dog.", "[rover->is-a->dog]")]
    [InlineData("Rover is a small dog.", "[rover->is->small]")]
    [InlineData("Rover is a big dog.", "[rover->is->big]")]
    [InlineData("Dogs have tails.", "[dog->has->tail]")]
    [InlineData("Fido has fur.", "[fido->has->fur]")]
    [InlineData("Fido chases cats.", "[fido->chases->cat]")]
    [InlineData("Dogs chase animals.", "[dog->chases->animal]")]
    [InlineData("The dog is chasing the cat.", "[dog->chases.ongoing->cat]")]
    [InlineData("Dogs have 4 legs.", "[dog->has.4->leg]")]
    [InlineData("Fido has 3 legs.", "[fido->has.3->leg]")]
    [InlineData("Cats have 2 tails.", "[cat->has.2->tail]")]
    [InlineData("Fido is not black.", "[fido->is.not->black]")]
    [InlineData("Cats are not large.", "[cat->is.not->large]")]
    [InlineData("Dogs are not cats.", "[dog->is-a.not->cat]")]
    [InlineData("Fido is not a cat.", "[fido->is-a.not->cat]")]
    public void GeneratedSentencesRoundTripToTheSameRelationship(string sentence, string expected)
    {
        var (store, module) = FreshInterpreter();
        module.Input(sentence, rememberUnknownWords: true);
        Link stored = Find(store, expected);
        Assert.NotNull(stored);

        string generated = module.GenerateSentence(stored);
        Assert.False(string.IsNullOrEmpty(generated), $"No sentence for {stored}.");

        var (freshStore, freshModule) = FreshInterpreter();
        string output = freshModule.Input(generated, rememberUnknownWords: true);
        Assert.True(Find(freshStore, expected) is not null, $"\"{generated}\" did not store {expected}.\n\n{output}");
        Assert.Contains("Executed relationships", output);
    }

    /// <summary>
    /// Keeps this class in step with the trial file: every sentence in a "Sentence  Expected" table
    /// of InterpretTrials.txt must appear in one of the theories above.
    /// </summary>
    [Fact]
    public void EveryTableSentenceInTheTrialFileIsCovered()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "UKSContent", "InterpretTrials.txt");
        var row = new Regex(@"^(?<sentence>[A-Z][^\[]*?[.?])\s{2,}\S");
        var sentences = File.ReadLines(path).Select(line => row.Match(line))
            .Where(match => match.Success).Select(match => match.Groups["sentence"].Value).ToList();
        Assert.True(sentences.Count >= 20, $"Found only {sentences.Count} trial sentences; has the file format changed?");

        string source = File.ReadAllText(Path.Combine(SourceDirectory(), "ModuleInterpretTrialTests.cs"));
        var missing = sentences.Where(sentence => !source.Contains($"\"{sentence}\"")).ToList();
        Assert.True(missing.Count == 0, "Trial sentences without a test: " + string.Join(" | ", missing));
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path);
}

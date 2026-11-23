using BrainSimulator.Modules;
using Xunit;

namespace UKS.Tests;

/// <summary>
/// A sentence that is not understood can be explained: the next input is taken as saying it another
/// way, and a template is learned from the two, starting weak and growing stronger with use.
/// </summary>
public class ModuleInterpretTeachingTests
{
    private const string Sentence = "The cat chased Fido and Rover.";
    private const string Explanation = "The cat chased Fido. The cat chased Rover.";

    private static (UKS store, ModuleInterpret module) FreshInterpreter()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        return (store, module);
    }

    private static Link Chase(UKS store, string from, string to) =>
        store.GetLink(store.Labeled(from), store.Labeled("chases.past"), store.Labeled(to));

    [Fact]
    public void AnExplainedSentenceTeachesATemplate()
    {
        var (store, module) = FreshInterpreter();

        module.Input(Sentence, rememberUnknownWords: true);
        Assert.Equal("I don't understand. Can you say it another way?", module.Answer);
        string report = module.Input(Explanation, rememberUnknownWords: true);

        Assert.StartsWith("Thanks, I understand now.", module.Answer);
        Assert.Contains("[learnedTemplate0->hasWords->^??.nounPhrase.learnedRole0 ??.actionVerb.learnedRole1 ??.nounPhrase.learnedRole2 w:and ??.nounPhrase.learnedRole3]", report);
        Link fido = Chase(store, "cat", "fido"), rover = Chase(store, "cat", "rover");
        Assert.NotNull(fido);
        Assert.NotNull(rover);
        // The template is captured weak and plastic; its facts record that it stated them.
        Thought template = store.Labeled("learnedTemplate0");
        Assert.Equal(0.25f, template.Weight, 3);
        Assert.True(template.isPlastic);
        Assert.NotNull(fido.HasLink(store.Labeled("statedBy"), template));
    }

    [Theory]
    [InlineData("[cat->chases.past->fido] [cat->chases.past->rover]")]
    [InlineData("[cat→chases.past.!→fido]\n[cat→chases.past.!→rover]")]
    public void ASentenceCanBeExplainedWithRelationships(string relationships)
    {
        var (store, module) = FreshInterpreter();

        module.Input(Sentence, rememberUnknownWords: true);
        string report = module.Input(relationships, rememberUnknownWords: true);

        Assert.True(module.Answer.StartsWith("Thanks, I understand now."), module.Answer + "\n\n" + report);
        Assert.Contains("[learnedTemplate0->hasWords->^??.nounPhrase.learnedRole0 ??.actionVerb.learnedRole1 ??.nounPhrase.learnedRole2 w:and ??.nounPhrase.learnedRole3]", report);
        Assert.NotNull(Chase(store, "cat", "fido"));
        Assert.NotNull(Chase(store, "cat", "rover"));
        // The learned template reads a new sentence of the same form.
        module.Input("The dog chased Fido and Rover.", rememberUnknownWords: true);
        Assert.StartsWith("OK.", module.Answer);
        Assert.NotNull(Chase(store, "dog", "rover"));
    }

    [Theory]
    [InlineData("[cat->chases.past->fiddo]", "Your explanation mentions fiddo")]
    [InlineData("[cat->chases.past->fido] cat", "Expected a relationship like [fido->is-a->dog] at \"cat\".")]
    [InlineData("[cat->chases.past fido]", "Expected \"->\" at \"]\".")]
    public void UnusableRelationshipsSaySoAndTheSentenceStillWaits(string relationships, string answer)
    {
        var (store, module) = FreshInterpreter();

        module.Input(Sentence, rememberUnknownWords: true);
        module.Input(relationships, rememberUnknownWords: true);
        Assert.StartsWith(answer, module.Answer);
        Assert.Null(Chase(store, "cat", "fido"));

        // Still waiting: a correct explanation is taken next.
        module.Input("[cat->chases.past->fido] [cat->chases.past->rover]", rememberUnknownWords: true);
        Assert.StartsWith("Thanks, I understand now.", module.Answer);
    }

    [Fact]
    public void RelationshipsWithNoSentenceWaitingSaySo()
    {
        var (_, module) = FreshInterpreter();
        module.Input("[fido->is-a->dog]", rememberUnknownWords: true);
        Assert.Equal("Relationships explain a sentence I didn't understand, and none is waiting.", module.Answer);
    }

    // A rival template for the same words, meaning something else: the chase reversed.
    private static Thought DefineRival(UKS store, float weight)
    {
        Thought rival = store.GetOrAddThought("rivalTemplate", "template");
        store.ProcessSingleLine("[rivalTemplate->hasWords->^??.nounPhrase.learnedRole0 ??.actionVerb.learnedRole1 ??.nounPhrase.learnedRole2 w:and ??.nounPhrase.learnedRole3]");
        store.ProcessSingleLine("[rivalTemplate->means->[??.nounPhrase.learnedRole2->??.actionVerb.learnedRole1->??.nounPhrase.learnedRole0]]");
        rival.Weight = weight;
        return rival;
    }

    [Fact]
    public void RivalReadingsCompeteByWeightAndOnlyTheWinnerIsActedOn()
    {
        var (store, module) = FreshInterpreter();
        module.Input(Sentence, rememberUnknownWords: true);
        module.Input(Explanation, rememberUnknownWords: true);
        Thought learned = store.Labeled("learnedTemplate0");
        Thought rival = DefineRival(store, 0.5f);

        string report = module.Input("The dog chased Fido and the cat.", rememberUnknownWords: true);

        Assert.Contains("Competing readings", report);
        Assert.NotNull(Chase(store, "fido", "dog"));
        Assert.Null(Chase(store, "dog", "fido"));      // the losing reading is not acted on
        Assert.Equal(0.75f, rival.Weight, 3);          // the winner succeeded and is strengthened
        Assert.Equal(0.25f, learned.Weight, 3);        // losing costs nothing
    }

    [Fact]
    public void TheStrongerTemplateWinsWhicheverItIs()
    {
        var (store, module) = FreshInterpreter();
        module.Input(Sentence, rememberUnknownWords: true);
        module.Input(Explanation, rememberUnknownWords: true);
        Thought learned = store.Labeled("learnedTemplate0");
        DefineRival(store, 0.5f);
        learned.Weight = 0.6f;

        module.Input("The dog chased Fido and the cat.", rememberUnknownWords: true);

        Assert.NotNull(Chase(store, "dog", "fido"));
        Assert.Null(Chase(store, "fido", "dog"));
        Assert.Equal(0.8f, learned.Weight, 3);
        Assert.False(learned.isPlastic);               // strong enough to consolidate
    }

    [Fact]
    public void SeveralSentencesInOneInputAreReadInTurn()
    {
        var (store, module) = FreshInterpreter();

        module.Input(Explanation, rememberUnknownWords: true);

        Assert.Equal("OK. OK.", module.Answer);
        Assert.NotNull(Chase(store, "cat", "fido"));
        Assert.NotNull(Chase(store, "cat", "rover"));
    }

    [Fact]
    public void TheSameWordsRepunctuatedAreReadAsNewNotAsAnExplanation()
    {
        var (store, module) = FreshInterpreter();

        module.Input("the cat chased fido the cat chased rover", rememberUnknownWords: true);
        Assert.Equal("I don't understand. Can you say it another way?", module.Answer);
        module.Input("the cat chased fido. the cat chased rover", rememberUnknownWords: true);

        Assert.Equal("OK. OK.", module.Answer);
        Assert.Null(store.Labeled("learnedTemplate0"));
        Assert.Equal(1f, Chase(store, "cat", "rover").Weight, 3);
    }

    [Fact]
    public void StatementsMadeInOneSentenceAreConjoined()
    {
        var (store, module) = FreshInterpreter();
        module.Input(Sentence, rememberUnknownWords: true);
        module.Input(Explanation, rememberUnknownWords: true);

        Link fido = Chase(store, "cat", "fido"), rover = Chase(store, "cat", "rover");
        Thought conjoined = store.Labeled("conjoinedWith");

        Assert.NotNull(store.GetLink(fido, conjoined, rover));
        Assert.NotNull(store.GetLink(rover, conjoined, fido)); // conjoinedWith is commutative
    }

    [Fact]
    public void TheLearnedTemplateGeneralizesAndGrowsStrongerWithUse()
    {
        var (store, module) = FreshInterpreter();
        module.Input(Sentence, rememberUnknownWords: true);
        module.Input(Explanation, rememberUnknownWords: true);

        module.Input("The dog chased Fido and the cat.", rememberUnknownWords: true);
        Assert.Equal("OK. (I'm still learning sentences like this one.)", module.Answer);
        Assert.NotNull(Chase(store, "dog", "cat"));
        module.Input("Fido chased the cat and the dog.", rememberUnknownWords: true);
        Assert.Equal("OK. (I'm still learning sentences like this one.)", module.Answer);
        module.Input("The cat chased the dog and Fido.", rememberUnknownWords: true);

        Assert.Equal("OK.", module.Answer);
    }

    [Fact]
    public void AnExplanationThatLeavesSomethingOutSaysWhat()
    {
        var (store, module) = FreshInterpreter();
        module.Input(Sentence, rememberUnknownWords: true);

        module.Input("The cat chased Fido.", rememberUnknownWords: true);
        Assert.Equal("Your explanation doesn't mention \"rover\". Can you say it another way?", module.Answer);
        Assert.Null(Chase(store, "cat", "fido"));

        // Still waiting for the explanation.
        module.Input(Explanation, rememberUnknownWords: true);
        Assert.StartsWith("Thanks, I understand now.", module.Answer);
    }

    [Theory]
    // Shares nothing with the sentence: a new statement.
    [InlineData("Dogs have tails.", "OK.")]
    // A question is a question.
    [InlineData("Is Fido brown?", "Fido is brown.")]
    public void AnInputThatIsNotAnExplanationIsReadAsNew(string input, string answer)
    {
        var (store, module) = FreshInterpreter();
        module.Input(Sentence, rememberUnknownWords: true);

        module.Input(input, rememberUnknownWords: true);

        Assert.Equal(answer, module.Answer);
        Assert.Null(store.Labeled("learnedTemplate0"));
    }

    [Fact]
    public void UndoForgetsTheLearnedTemplateAndWhatItStated()
    {
        var (store, module) = FreshInterpreter();
        module.Input(Sentence, rememberUnknownWords: true);
        module.Input(Explanation, rememberUnknownWords: true);

        module.UndoLastInput();

        Assert.Null(store.Labeled("learnedTemplate0"));
        Assert.Null(store.Labeled("w:rover"));
        Assert.Null(Chase(store, "cat", "fido"));
        module.Input(Sentence, rememberUnknownWords: true);
        Assert.Equal("I don't understand. Can you say it another way?", module.Answer);
    }
}

using BrainSimulator.Modules;
using Xunit;
using Xunit.Abstractions;

namespace UKS.Tests;

/// <summary>
/// Clauses: when nothing reads a whole sentence, its sentence templates are also matched between words
/// that join clauses ("and", "if", a comma). "A and B" states both and links them by conjoinedWith;
/// "A if B" (or "If B, A") stores a rule the UKS answers from once its condition holds.
/// </summary>
public class ModuleInterpretClauseTests
{
    private readonly ITestOutputHelper output;
    public ModuleInterpretClauseTests(ITestOutputHelper output) => this.output = output;

    private static (UKS store, ModuleInterpret module) FreshInterpreter()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        return (store, module);
    }

    // The templates the report lists for one sentence word ("w:fido (...)    nounPhrase3, ...").
    private static string[] TemplatesOf(string report, string word)
    {
        string line = report.Split('\n').First(line => line.StartsWith(word + " ("));
        return line[(line.LastIndexOf("    ") + 4)..].TrimEnd('\r').Split(", ");
    }

    private static Link Stored(UKS store, string from, string type, string to) =>
        store.GetLink(store.Labeled(from), store.Labeled(type), store.Labeled(to));

    [Fact]
    public void TwoClausesJoinedByAndAreBothStatedAndConjoined()
    {
        var (store, module) = FreshInterpreter();
        string report = module.Input("Fido is a dog and Fido is green.", rememberUnknownWords: true);
        output.WriteLine(report);

        Assert.Equal("OK.", module.Answer);
        Link isADog = Stored(store, "fido", "is-a", "dog"), isGreen = Stored(store, "fido", "is", "green");
        Assert.NotNull(isADog);
        Assert.NotNull(isGreen);
        Thought conjoinedWith = store.Labeled("conjoinedWith");
        Assert.True(store.GetLink(isADog, conjoinedWith, isGreen) is not null || store.GetLink(isGreen, conjoinedWith, isADog) is not null,
            "Expected the two statements to be conjoined.\n\n" + report);
        // Each clause is still a sentence template of its own inside the compound.
        Assert.Contains("classificationTemplate", TemplatesOf(report, "w:dog"));
        Assert.Contains("copulaAttributeTemplate", TemplatesOf(report, "w:green"));
        Assert.Contains("compoundTemplate", TemplatesOf(report, "w:and"));
    }

    [Fact]
    public void AConjoinedStatementIsUndoneWhole()
    {
        var (store, module) = FreshInterpreter();
        module.Input("Fido is a dog and Fido is green.", rememberUnknownWords: true);
        module.UndoLastInput();

        Assert.Null(Stored(store, "fido", "is", "green"));
        Assert.NotNull(Stored(store, "fido", "is-a", "dog"));  // a BasicWords fact, there before
    }

    [Fact]
    public void EachClauseCanBeAskedAboutAfterwards()
    {
        var (store, module) = FreshInterpreter();
        module.Input("Fido is green and Fido is outside.", rememberUnknownWords: true);
        Assert.Equal("OK.", module.Answer);

        module.Input("Is Fido green?", rememberUnknownWords: true);
        Assert.Equal("Fido is green.", module.Answer);
        module.Input("Where is Fido?", rememberUnknownWords: true);
        Assert.Contains("outside", module.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThreeClausesJoinedByAndAreAllStated()
    {
        var (store, module) = FreshInterpreter();
        string report = module.Input("Fido is green and Fido is outside and Fido is black.", rememberUnknownWords: true);
        output.WriteLine(report);

        Assert.Equal("OK.", module.Answer);
        Assert.NotNull(Stored(store, "fido", "is", "green"));
        Assert.NotNull(Stored(store, "fido", "located", "outside"));
        Assert.NotNull(Stored(store, "fido", "is", "black"));
    }

    // A compound is a clause, so "A and B and C and D" is ((A and B) and C) and D: one template.
    [Fact]
    public void AnyNumberOfClausesCanBeJoinedByAnd()
    {
        var (store, module) = FreshInterpreter();
        string report = module.Input("Fido is green and Fido is outside and Fido is black and Fido is hungry.", rememberUnknownWords: true);
        output.WriteLine(report);

        Assert.Equal("OK.", module.Answer);
        Assert.NotNull(Stored(store, "fido", "is", "green"));
        Assert.NotNull(Stored(store, "fido", "located", "outside"));
        Assert.NotNull(Stored(store, "fido", "is", "black"));
        Assert.NotNull(Stored(store, "fido", "is", "hungry"));
        Assert.Null(store.Labeled("compound3Template"));
    }

    // "if": a rule. Nothing is stated; the UKS answers from it once its condition holds.
    [Theory]
    [InlineData("Fido is green if Fido is outside.")]
    [InlineData("If Fido is outside, Fido is green.")]
    public void AConditionalIsARuleThatAnswersOnceItsConditionHolds(string rule)
    {
        var (store, module) = FreshInterpreter();
        string report = module.Input(rule, rememberUnknownWords: true);
        output.WriteLine(report);

        Assert.Equal("OK.", module.Answer);
        Assert.Contains("[[fido→is.?→green]→IF→[fido→located.?→outside]]", report);
        Assert.Null(Stored(store, "fido", "is", "green"));
        Assert.Null(Stored(store, "fido", "located", "outside"));

        module.Input("Is Fido green?", rememberUnknownWords: true);
        Assert.Equal("I don't know. Fido is green if Fido is outside, but I don't know whether Fido is outside.", module.Answer);
        module.Input("Fido is outside.", rememberUnknownWords: true);
        module.Input("Is Fido green?", rememberUnknownWords: true);
        Assert.Equal("Fido is green, because Fido is outside.", module.Answer);
    }

    [Fact]
    public void InAConditionAndJoinsTheConditions()
    {
        var (store, module) = FreshInterpreter();
        string report = module.Input("Fido is green if Fido is outside and Fido is black.", rememberUnknownWords: true);
        output.WriteLine(report);

        Assert.Equal("OK.", module.Answer);
        Assert.Contains("[[fido→is.?→green]→IF→[[fido→located.?→outside]→AND→[fido→is.?→black]]]", report);

        module.Input("Fido is outside.", rememberUnknownWords: true);
        module.Input("Is Fido green?", rememberUnknownWords: true);
        Assert.Equal("I don't know. Fido is green if Fido is outside and Fido is black, but I don't know whether Fido is black.", module.Answer);  // only one condition holds
        module.Input("Fido is black.", rememberUnknownWords: true);
        module.Input("Is Fido green?", rememberUnknownWords: true);
        Assert.Equal("Fido is green, because Fido is outside and Fido is black.", module.Answer);
    }

    [Fact]
    public void AContradictedConditionIsGivenAsTheReason()
    {
        var (_, module) = FreshInterpreter();
        module.Input("Fido is green if Fido is outside.", rememberUnknownWords: true);
        module.Input("Fido is inside.", rememberUnknownWords: true);
        module.Input("Is Fido green?", rememberUnknownWords: true);
        Assert.Equal("I don't know. Fido is green if Fido is outside, but Fido is inside.", module.Answer);
    }

    [Fact]
    public void AQuestionNoRuleBearsOnIsPlainlyNotKnown()
    {
        var (_, module) = FreshInterpreter();
        module.Input("Fido is green if Fido is outside.", rememberUnknownWords: true);
        module.Input("Is Fido black?", rememberUnknownWords: true);
        Assert.Equal("I don't know.", module.Answer);
    }

    [Fact]
    public void AnUnknownWordInAClauseIsReadAsItIsAlone()
    {
        var (store, module) = FreshInterpreter();
        module.Input("Fido is green and Fido is hungry.", rememberUnknownWords: true);
        Assert.NotNull(Stored(store, "fido", "is", "hungry"));  // an adjective, not "Fido is-a hungry"
    }

    // "Is Fido green if Fido is outside?": asked on the assumption, which does not stay.
    [Fact]
    public void AQuestionCanBeAskedOnAnAssumption()
    {
        var (store, module) = FreshInterpreter();
        module.Input("Fido is green if Fido is outside.", rememberUnknownWords: true);

        string report = module.Input("Is Fido green if Fido is outside?", rememberUnknownWords: true);
        output.WriteLine(report);
        Assert.Equal("Fido is green if Fido is outside.", module.Answer);
        Assert.Null(Stored(store, "fido", "located", "outside"));

        module.Input("Is Fido green if Fido is inside?", rememberUnknownWords: true);
        Assert.Equal("I don't know. Fido is green if Fido is outside, but Fido is inside.", module.Answer);
        module.Input("Is Fido black if Fido is outside?", rememberUnknownWords: true);
        Assert.Equal("I don't know.", module.Answer);
    }

    // Pronouns: the first thing the sentence is about, else what the previous sentence was about.
    [Fact]
    public void APronounInARuleRefersToTheSubjectOfTheSentence()
    {
        var (_, module) = FreshInterpreter();
        string report = module.Input("Fido is green if he is outside.", rememberUnknownWords: true);
        output.WriteLine(report);
        Assert.Contains("[[fido→is.?→green]→IF→[fido→located.?→outside]]", report);
    }

    [Fact]
    public void APronounCanReferToThePreviousSentence()
    {
        var (store, module) = FreshInterpreter();
        module.Input("Rover is a dog.", rememberUnknownWords: true);
        module.Input("He is black.", rememberUnknownWords: true);
        Assert.NotNull(Stored(store, "rover", "is", "black"));
        module.Input("Is he black?", rememberUnknownWords: true);
        Assert.Equal("Rover is black.", module.Answer);
    }

    [Fact]
    public void APronounWithNothingToReferToSaysSo()
    {
        var (_, module) = FreshInterpreter();
        module.Input("He is black.", rememberUnknownWords: true);
        Assert.Equal("I don't know what the pronoun refers to.", module.Answer);
    }

    [Fact]
    public void ARuleWithTwoResultsIsARuleForEach()
    {
        var (_, module) = FreshInterpreter();
        string report = module.Input("Fido is green and Fido is black if Fido is outside.", rememberUnknownWords: true);
        output.WriteLine(report);

        Assert.Equal("OK.", module.Answer);
        Assert.Contains("[[fido→is.?→green]→IF→[fido→located.?→outside]]", report);
        Assert.Contains("[[fido→is.?→black]→IF→[fido→located.?→outside]]", report);
        module.Input("Fido is outside.", rememberUnknownWords: true);
        module.Input("Is Fido black?", rememberUnknownWords: true);
        Assert.Equal("Fido is black, because Fido is outside.", module.Answer);
    }

    [Fact]
    public void ARuleIsUndoneWhole()
    {
        var (store, module) = FreshInterpreter();
        module.Input("Fido is green if Fido is outside.", rememberUnknownWords: true);
        module.UndoLastInput();
        module.Input("Fido is outside.", rememberUnknownWords: true);
        module.Input("Is Fido green?", rememberUnknownWords: true);
        Assert.Equal("I don't know.", module.Answer);
    }

    [Fact]
    public void WithoutTheCommaAConditionFirstIsNotUnderstood()
    {
        var (_, module) = FreshInterpreter();
        module.Input("If Fido is outside Fido is green.", rememberUnknownWords: true);
        Assert.Equal("I don't understand. Can you say it another way?", module.Answer);
    }

    [Fact]
    public void ASentenceReadWholeIsNotSplitIntoClauses()
    {
        var (store, module) = FreshInterpreter();
        string report = module.Input("Fido and Rover are dogs.", rememberUnknownWords: true);

        Assert.Equal("OK.", module.Answer);
        Assert.DoesNotContain("copulaClassificationTemplate", TemplatesOf(report, "w:rover"));
        Assert.DoesNotContain("classificationTemplate", TemplatesOf(report, "w:rover"));
    }
}

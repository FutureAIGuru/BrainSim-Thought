using System.IO;
using BrainSimulator.Modules;
using Xunit;
using InterpretationIntent = BrainSimulator.Modules.ModuleInterpret.InterpretationIntent;

namespace UKS.Tests;

public class ModuleInterpretTests
{
    [Fact]
    public void ModuleLoadsItsTemplatesAndExecutesAgainstItsStore()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };

        var result = module.Interpret("Which dogs are brown?");
        Assert.Equal(InterpretationIntent.Query, result.Intent);
        var matches = module.Execute(result);
        Assert.Equal(2, matches.Count);
        Assert.All(matches, match => Assert.Equal("fido", match.From.Label));
    }

    private static UKS CreateStore(bool examples = false)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        foreach (string file in new[] { "BasicWords.txt", "InterpretLanguage.txt" })
            store.ImportTextFile(Path.Combine(AppContext.BaseDirectory, "UKSContent", file));
        if (examples)
            store.ImportTextFile(Path.Combine(AppContext.BaseDirectory, "UKSContent", "InterpretExamples.txt"));
        return store;
    }

    [Theory]
    [InlineData("Fido is a dog.", InterpretationIntent.Assertion, "[fido→is-a→dog]")]
    [InlineData("Fido is a brown dog.", InterpretationIntent.Assertion, "[fido→is-a→dog]|[fido→is→brown]")]
    [InlineData("Birds are animals.", InterpretationIntent.Assertion, "[bird→is-a→animal]")]
    [InlineData("The brown dog chases the cat.", InterpretationIntent.Assertion, "[dog→chases→cat]|[dog→is→brown]")]
    [InlineData("Dogs have tails.", InterpretationIntent.Assertion, "[dog→has→tail]")]
    [InlineData("Fido is chasing cats.", InterpretationIntent.Assertion, "[fido→chases.ongoing→cat]")]
    [InlineData("Dogs can bark.", InterpretationIntent.Assertion, "[dog→can→bark]")]
    [InlineData("Fido is not brown.", InterpretationIntent.Assertion, "[fido→is.NOT→brown]")]
    [InlineData("Fido is not a cat.", InterpretationIntent.Assertion, "[fido→is-a.NOT→cat]")]
    [InlineData("Fido does not chase cats.", InterpretationIntent.Assertion, "[fido→chases.NOT→cat]")]
    [InlineData("Is Fido not brown?", InterpretationIntent.Query, "[fido→is.NOT→brown]")]
    [InlineData("Is Fido a dog?", InterpretationIntent.Query, "[fido→is-a→dog]")]
    [InlineData("Do dogs have tails?", InterpretationIntent.Query, "[dog→has→tail]")]
    [InlineData("What does Fido chase?", InterpretationIntent.Query, "[fido→chases→??]")]
    [InlineData("Which dogs are brown?", InterpretationIntent.Query, "[??→is-a→dog]|[??→is→brown]")]
    [InlineData("Show Fido", InterpretationIntent.Command, "[fido→answerType→??]")]
    public void InterpretsUsingStoredTemplates(string input, InterpretationIntent intent, string expected)
    {
        var store = CreateStore();
        int labels = ThoughtLabels.GetLabelCount();
        var result = ModuleInterpret.InterpretEnglish(store, input);
        Assert.Null(result.Diagnostic);
        Assert.Equal(intent, result.Intent);
        Assert.Equal(expected.Split('|').Order(), result.Relationships.Select(link => link.ToString()).Order());
        Assert.Contains(result.Trace, item => item.StartsWith("Word"));
        Assert.Contains(result.Trace, item => item.StartsWith("Phrase"));
        Assert.Contains(result.Trace, item => item.StartsWith("Template"));
        Assert.Equal(labels, ThoughtLabels.GetLabelCount());
    }

    [Fact]
    public void ExecutionUsesUksAndKeepsTheSharedWildcardBound()
    {
        var store = CreateStore(examples: true);
        var query = ModuleInterpret.InterpretEnglish(store, "Which dogs are brown?");
        Assert.Same(query.Relationships[0].From, query.Relationships[1].From);
        var matches = ModuleInterpret.ExecuteInterpretation(store, query);
        Assert.Equal(2, matches.Count);
        Assert.All(matches, match => Assert.Equal("fido", match.From.Label));

        Assert.Empty(ModuleInterpret.ExecuteInterpretation(store, ModuleInterpret.InterpretEnglish(store, "Is Fido a cat?")));
        Assert.Single(ModuleInterpret.ExecuteInterpretation(store, ModuleInterpret.InterpretEnglish(store, "Is Fido a dog?")));
        Assert.Equal("cat", Assert.Single(ModuleInterpret.ExecuteInterpretation(store, 
            ModuleInterpret.InterpretEnglish(store, "What does Fido chase?"))).To.Label);
        Assert.Contains(ModuleInterpret.ExecuteInterpretation(store, ModuleInterpret.InterpretEnglish(store, "Show Fido")),
            match => match.LinkType.Label == "is" && match.To.Label == "brown");

        Thought rover = store.Labeled("rover");
        var assertion = ModuleInterpret.InterpretEnglish(store, "Rover is large.");
        Assert.Null(rover.HasLink("is", "large"));
        Assert.Single(ModuleInterpret.ExecuteInterpretation(store, assertion));
        Assert.NotNull(rover.HasLink("is", "large"));

        Assert.Single(ModuleInterpret.ExecuteInterpretation(store, ModuleInterpret.InterpretEnglish(store, "Rover is not small.")));
        Assert.Empty(ModuleInterpret.ExecuteInterpretation(store, ModuleInterpret.InterpretEnglish(store, "Is Rover small?")));
        Assert.Single(ModuleInterpret.ExecuteInterpretation(store, ModuleInterpret.InterpretEnglish(store, "Is Rover not small?")));
    }

    [Fact]
    public void UnrelatedLearnedTemplateDoesNotConsumeTheQueryAnswerSlot()
    {
        var store = CreateStore(examples: true);
        store.ProcessSingleLine("[learned:savedQuery->is-a->Query]");
        store.ProcessSingleLine("[learned:savedQuery->hasWords->^w:what ??]");
        store.ProcessSingleLine("[learned:savedQuery->means->[fido->?.is->??]]");

        var result = ModuleInterpret.InterpretEnglish(store, "Which dogs are brown?");
        Assert.Null(result.Diagnostic);
        Assert.Equal(InterpretationIntent.Query, result.Intent);
        Assert.All(result.Relationships, relationship => Assert.Same(store.Labeled("??"), relationship.From));
        var matches = ModuleInterpret.ExecuteInterpretation(store, result);
        Assert.Equal(2, matches.Count);
        Assert.All(matches, match => Assert.Equal("fido", match.From.Label));
    }

    [Fact]
    public void TemplatesWorkWithLegacyActionAndWildcardDefinitions()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var properties = store.Labeled("hasProperty");
        var queryMarker = store.Labeled("?");
        queryMarker.RemoveLinks(properties);
        queryMarker.AddProperty(store.Labeled("isConditional"));
        store.Labeled("!").RemoveLinks(properties);
        store.Labeled("??").AddParent(store.GetOrAddThought("Wildcard", "Thought"));
        foreach (string file in new[] { "BasicWords.txt", "InterpretLanguage.txt", "InterpretExamples.txt" })
            store.ImportTextFile(Path.Combine(AppContext.BaseDirectory, "UKSContent", file));

        var query = ModuleInterpret.InterpretEnglish(store, "Which dogs are brown?");
        Assert.Null(query.Diagnostic);
        var matches = ModuleInterpret.ExecuteInterpretation(store, query);
        Assert.Equal(2, matches.Count);
        Assert.All(matches, match => Assert.Equal("fido", match.From.Label));
        var assertion = ModuleInterpret.InterpretEnglish(store, "Rover is large.");
        Assert.Same(store.Labeled("is"), Assert.Single(assertion.Relationships).LinkType);
        Assert.True(queryMarker.HasProperty("isConditional"));
    }

    [Fact]
    public void ANewTemplateReusesNounPhrases()
    {
        var store = CreateStore();
        Assert.NotNull(ModuleInterpret.InterpretEnglish(store, "Fido seems brown.").Diagnostic);
        foreach (var line in new[]
        {
            "[w:seems->is-a->verb]",
            "[interpret:seems->is-a->Assertion]",
            "[interpret:seems->hasWords->^slot:subject w:seems slot:adjective]",
            "[interpret:seems->means->[slot:subject->!.is->slot:adjective]]"
        })
            store.ProcessSingleLine(line);
        var result = ModuleInterpret.InterpretEnglish(store, "Fido seems brown.");
        Assert.Null(result.Diagnostic);
        Assert.Equal("[fido→is→brown]", Assert.Single(result.Relationships).ToString());
    }

    [Fact]
    public void StaleInterpretationsCannotWriteToTheUks()
    {
        var store = CreateStore();
        var result = ModuleInterpret.InterpretEnglish(store, "Rover is large.");
        store.Labeled("rover").Delete();
        Assert.Throws<ArgumentException>(() => ModuleInterpret.ExecuteInterpretation(store, result));
    }

    [Theory]
    [InlineData("Fido blargs cats.", "Unknown words: blargs")]
    [InlineData("Fido chase", "no sentence template matched")]
    [InlineData("Fido is can.", "no sentence template matched")]
    public void ReportsIncompleteInput(string input, string diagnostic)
    {
        var result = ModuleInterpret.InterpretEnglish(CreateStore(), input);
        Assert.Null(result.Intent);
        Assert.Empty(result.Relationships);
        Assert.Contains(diagnostic, result.Diagnostic);
    }
}

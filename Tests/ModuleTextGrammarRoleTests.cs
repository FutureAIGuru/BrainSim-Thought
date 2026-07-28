using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrainSimulator;
using BrainSimulator.Modules;
using UKS;
using Xunit;
using Xunit.Abstractions;

namespace BrainSimulator.Tests;

/// <summary>
/// Measures role and category discovery against the closed vocabulary of the
/// generated corpus. That corpus is built from a fixed word list, so it can be
/// scored exactly rather than judged by inspection.
/// </summary>
[Collection("ModuleTextPatternLearning")]
public class ModuleTextGrammarRoleTests
{
    private readonly ITestOutputHelper output;

    public ModuleTextGrammarRoleTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    private static readonly string[] GoldArticles = { "the", "a" };

    private static readonly string[] GoldRelators = { "is", "are", "has", "have", "can" };

    private static readonly string[] GoldAdjectives =
    {
        "young", "warm", "strong", "soft", "small", "slow", "sleepy", "quiet", "quick",
        "playful", "old", "noisy", "muddy", "large", "hungry", "happy", "gentle",
        "friendly", "dark", "cold", "clean", "bright", "brave", "alert", "calm",
    };

    // The copula, the possessives and the modal are verbs like any other; they
    // are listed separately only because they also carry the template's shape.
    private static readonly string[] GoldVerbs =
    {
        "is", "are", "has", "have", "can",
        "watch", "watches", "eat", "eats", "visit", "visits", "find", "finds",
        "climb", "climbs", "sees", "run", "runs", "jump", "jumps", "walk",
        "walks", "swim", "swims", "sleep", "sleeps", "play", "plays", "sing",
        "sings", "rest", "rests", "push", "pushes", "pull", "pulls", "need",
        "needs", "follow", "follows", "dance", "dances", "chase", "chases",
        "carry", "carries",
    };

    private static readonly string[] GoldNouns =
    {
        "ant", "badger", "bear", "beaver", "bee", "bird", "camel", "cat", "chicken",
        "cow", "crab", "deer", "dog", "dolphin", "donkey", "duck", "eagle",
        "elephant", "falcon", "frog", "giraffe", "goat", "hippo", "horse", "lion",
        "lizard", "lobster", "monkey", "otter", "owl", "panda", "parrot", "penguin",
        "pig", "rabbit", "rhino", "robin", "salmon", "seal", "shark", "sheep",
        "snake", "spider", "squirrel", "tiger", "turtle", "whale", "wolf", "yak",
        "zebra",
    };

    [Fact]
    public void GeneratedCorpusRolesAreGroundedInActionArguments()
    {
        UKS.UKS uks = LoadAndDiscover("bst_simple_1000_corpus.txt");

        Thought roleRoot = uks.Labeled("SlotRole");
        Assert.NotNull(roleRoot);
        Assert.NotEmpty(roleRoot.Children);

        // Every position of every template is accounted for, and the role
        // sequence lines up with the words it describes.
        Thought hasWords = uks.Labeled("hasWords");
        List<Thought> templates = uks.Labeled("LearnedTemplate").Children.ToList();
        int described = 0;
        foreach (Thought template in templates)
        {
            SlotRoleView view = uks.GetSlotRoleView(template, hasWords);
            Assert.NotNull(view);
            Assert.Equal(view.Elements.Count, view.Roles.Count);
            if (view.Roles.All(role => role is not null)) described++;
            output.WriteLine($"{template.Label}: " + string.Join("  ", Enumerable
                .Range(0, view.Count)
                .Select(i => $"{Show(view.Elements[i])}[{view.Roles[i]?.Label ?? "-"}]")));
        }
        Assert.Equal(templates.Count, described);
    }

    [Fact]
    public void GeneratedCorpusCategoriesMatchTheKnownVocabulary()
    {
        UKS.UKS uks = LoadAndDiscover("bst_simple_1000_corpus.txt");

        Thought categoryRoot = uks.Labeled("LexicalCategory");
        Assert.NotNull(categoryRoot);

        var scores = new Dictionary<string, (double precision, double recall, int found)>();
        foreach ((string name, string[] gold) in new[]
        {
            ("article", GoldArticles),
            ("noun", GoldNouns),
            ("verb", GoldVerbs),
            ("adjective", GoldAdjectives),
        })
        {
            HashSet<string> found = Members(uks, name);
            HashSet<string> expected = ExpandGold(name, gold);
            int hits = found.Count(word => expected.Contains(word));
            double precision = found.Count == 0 ? 0 : hits / (double)found.Count;
            double recall = expected.Count == 0 ? 0 : hits / (double)expected.Count;
            scores[name] = (precision, recall, found.Count);
            output.WriteLine($"{name,-10} precision {precision:P1}  recall {recall:P1}  " +
                $"({hits}/{found.Count} correct, {expected.Count} expected)");
            List<string> wrong = found.Where(word => !expected.Contains(word)).OrderBy(x => x).ToList();
            if (wrong.Count > 0)
                output.WriteLine($"           not expected: {string.Join(", ", wrong.Take(30))}");
        }

        foreach ((string name, (double precision, double recall, int found)) in scores)
        {
            Assert.True(precision >= 0.90, $"{name} precision {precision:P1} below 90%");
            Assert.True(recall >= 0.90, $"{name} recall {recall:P1} below 90%");
        }
    }

    [Fact]
    public void AdjectivesAreNotClassifiedAsNounsOrVerbs()
    {
        // Telling a quality from a thing and from an action is the point of the
        // exercise; overlap between those categories is the failure to look for.
        UKS.UKS uks = LoadAndDiscover("bst_simple_1000_corpus.txt");

        HashSet<string> nouns = Members(uks, "noun");
        HashSet<string> verbs = Members(uks, "verb");
        HashSet<string> adjectives = Members(uks, "adjective");

        Assert.Empty(adjectives.Intersect(nouns));
        Assert.Empty(adjectives.Intersect(verbs));
        Assert.Empty(nouns.Intersect(verbs));
        foreach (string adjective in GoldAdjectives)
        {
            Assert.DoesNotContain(adjective, nouns);
            Assert.DoesNotContain(adjective, verbs);
        }
    }

    [Fact]
    public void TemplateCorpusSeparatesQualitiesFromClassifications()
    {
        // This corpus has no "the quiet dog" sentences, so a quality can only be
        // recognized by the separator which governs it: "is" also takes "an
        // animal", "can" never does.
        UKS.UKS uks = LoadAndDiscover("bst_true_template_corpus.txt");

        DumpTemplates(uks);
        HashSet<string> adjectives = Members(uks, "adjective");
        HashSet<string> nouns = Members(uks, "noun");
        HashSet<string> verbs = Members(uks, "verb");
        foreach (string name in new[] { "article", "noun", "verb", "adjective" })
            output.WriteLine($"CAT {name}: " + string.Join(", ", Members(uks, name).OrderBy(x => x)));

        Assert.Equal(new[] { "a", "an", "the" }, Members(uks, "article").OrderBy(x => x));
        foreach (string quality in new[]
            { "brown", "large", "small", "black", "white", "green", "tall", "short" })
            Assert.Contains(quality, adjectives);
        foreach (string thing in new[] { "dog", "cat", "animal", "tail", "leg" })
            Assert.Contains(thing, nouns);
        foreach (string action in new[] { "bark", "run", "swim", "fly", "is", "can" })
            Assert.Contains(action, verbs);
        Assert.Empty(adjectives.Intersect(nouns));
        Assert.Empty(adjectives.Intersect(verbs));

        // A plural seen only as a complement ("dogs are animals", "dogs have
        // tails") is now recognized as a thing, because the number relation ties
        // it to the singular which "is an animal" established as one.
        foreach (string plural in new[] { "animals", "tails" })
            Assert.Contains(plural, nouns);
    }

    [Fact]
    public void RolesAreUnderstoodThroughThePartsOfTheActionTheySupply()
    {
        // A role earns its name by what it does. The corpus supplies a handful
        // of demonstrated actions; the position which feeds the source of one is
        // a subject, whatever that position was called when it was discovered.
        UKS.UKS uks = LoadAndDiscover("bst_true_template_corpus.txt");

        Thought roleRoot = uks.Labeled("SlotRole");
        foreach (string name in new[] { "subjectRole", "predicateRole", "verbRole", "articleRole" })
        {
            Thought named = uks.Labeled(name);
            Assert.NotNull(named);
            Assert.NotEmpty(named.Children);
            Assert.All(named.Children, role => Assert.Contains(role, roleRoot.Children));
            output.WriteLine($"{name}: " + string.Join(", ", named.Children.Select(r => r.Label)));
        }

        // Grounding is what makes the name meaningful, so the binding it was
        // read from has to be present too.
        Thought supplies = uks.Labeled("supplies");
        foreach (Thought subjectRole in uks.Labeled("subjectRole").Children)
            Assert.Contains(subjectRole.LinksTo, link =>
                link.LinkType == supplies && link.To?.Label == "actionSource");

        // A subject and a predicate are different roles however alike the words
        // which fill them, because they sit on opposite sides of the verb.
        Assert.Empty(uks.Labeled("subjectRole").Children
            .Intersect(uks.Labeled("predicateRole").Children));
    }

    [Fact]
    public void PluralsAreRelatedToTheirSingularsByALearnedSuffix()
    {
        // The corpus never states the rule; it is read off the spellings the
        // words already have. Every plural formed by the discovered suffix means
        // the singular's concept and inherits its category.
        UKS.UKS uks = LoadAndDiscover("bst_true_template_corpus.txt");

        Thought pluralSuffix = uks.Labeled("pluralSuffix:s");
        Assert.NotNull(pluralSuffix);

        Thought pluralOf = uks.Labeled("pluralOf");
        foreach ((string plural, string singular) in new[]
        {
            ("dogs", "dog"), ("animals", "animal"), ("tails", "tail"), ("legs", "leg"),
        })
        {
            Thought pluralWord = uks.Labeled("w:" + plural);
            Assert.NotNull(pluralWord);
            Assert.Contains(pluralWord.LinksTo, link =>
                link.LinkType == pluralOf && Show(link.To) == singular);
            Assert.Equal(uks.Labeled("w:" + singular).GetTargetOfFirstLinkOfType("means"),
                pluralWord.GetTargetOfFirstLinkOfType("means"));
        }
    }

    [Fact]
    public void PluralClassificationsAndAssertionsAreDistinguished()
    {
        // This is the deck's example. "dogs are animals" classifies; "dogs are
        // noisy" asserts a quality. The two share a surface but not a meaning,
        // and the difference is decided by what fills the complement — a thing
        // or a quality — now that plural things are recognized as things.
        UKS.UKS uks = LoadAndDiscover("bst_true_template_corpus.txt");

        ModuleText.AddPhrase("dogs are animals", applyExistingTemplates: true);
        Assert.NotNull(uks.GetLink(
            uks.Labeled("dog"), uks.Labeled("is-a"), uks.Labeled("animal")));
        Assert.Null(uks.GetLink(
            uks.Labeled("dog"), uks.Labeled("is"), uks.Labeled("animal")));

        ModuleText.AddPhrase("dogs are brown", applyExistingTemplates: true);
        Assert.NotNull(uks.GetLink(
            uks.Labeled("dog"), uks.Labeled("is"), uks.Labeled("brown")));
        Assert.Null(uks.GetLink(
            uks.Labeled("dog"), uks.Labeled("is-a"), uks.Labeled("brown")));
    }

    [Fact]
    public void QuestionsAreSeparatedFromStatementsAndLearnedApart()
    {
        UKS.UKS uks = LoadAndDiscover("bst_true_template_corpus.txt");

        // The two populations are kept apart, so neither distorts what is
        // learned from the other.
        Assert.NotEmpty(ModuleText.GetPhrasesOfKind("Question"));
        Assert.NotEmpty(ModuleText.GetPhrasesOfKind("Statement"));
        Assert.Empty(ModuleText.GetPhrasesOfKind("Question")
            .Intersect(ModuleText.GetPhrasesOfKind("Statement")));

        Thought questionTemplates = uks.Labeled("LearnedQuestionTemplate");
        Assert.NotNull(questionTemplates);
        Assert.NotEmpty(questionTemplates.Children);

        Thought hasWords = uks.Labeled("hasWords");
        foreach (Thought template in questionTemplates.Children)
        {
            SequenceView view = uks.GetSequenceViews(template).Single(v => v.LinkType == hasWords);
            int asks = template.LinksTo.Count(link => link.LinkType?.Label == "means" &&
                link.To is Link action && action.LinkType?.HasAncestor("TEST") == true);
            output.WriteLine($"{template.Label} (asks {asks}): " +
                string.Join(' ', view.Elements.Select(Show)));
        }

        // A learned question template asks about a relationship rather than
        // asserting one.
        Assert.Contains(questionTemplates.Children, template =>
            template.LinksTo.Any(link => link.LinkType?.Label == "means" &&
                link.To is Link action && action.LinkType?.HasAncestor("TEST") == true));
        Assert.DoesNotContain(questionTemplates.Children, template =>
            template.LinksTo.Any(link => link.LinkType?.Label == "means" &&
                link.To is Link action && action.LinkType?.HasAncestor("SET") == true));
    }

    [Fact]
    public void LearnedQuestionsAreAnsweredFromWhatIsKnown()
    {
        UKS.UKS uks = LoadAndDiscover("bst_true_template_corpus.txt");

        // Nothing states these answers; they are read back out of the
        // relationships the statements asserted.
        List<string> canDo = ModuleText.AnswerQuestion("What can a dog do?");
        output.WriteLine("what can a dog do -> " + string.Join(", ", canDo));
        Assert.Contains("bark", canDo);

        List<string> hasWhat = ModuleText.AnswerQuestion("What does a dog have?");
        output.WriteLine("what does a dog have -> " + string.Join(", ", hasWhat));
        Assert.Contains("tail", hasWhat);

        // The inverse question supplies the other end of the same relationship.
        List<string> whoBarks = ModuleText.AnswerQuestion("What can bark?");
        output.WriteLine("what can bark -> " + string.Join(", ", whoBarks));
        Assert.Contains("dog", whoBarks);
    }

    [Fact]
    public void QuestionsAreAnsweredInEnglishRatherThanInFragments()
    {
        // The same knowledge, said as a phrase instead of the bare word which
        // completes it.
        UKS.UKS uks = LoadAndDiscover("bst_true_template_corpus.txt");

        foreach ((string question, string expected) in new[]
        {
            ("What can a dog do?", "a dog can bark"),
            ("What does a dog have?", "a dog has a tail"),
            ("What can bark?", "a dog can bark"),
        })
        {
            List<string> said = ModuleText.AnswerQuestionInEnglish(question);
            output.WriteLine($"{question,-24} => " + string.Join(" / ", said));
            Assert.Contains(expected, said);
        }

        // Asked from either end, the same fact is stated the same way.
        Assert.Equal(
            ModuleText.AnswerQuestionInEnglish("What can a dog do?"),
            ModuleText.AnswerQuestionInEnglish("What can bark?"));

        // The word form still works, so nothing which relied on it has changed.
        Assert.Contains("bark", ModuleText.AnswerQuestion("What can a dog do?"));
    }

    [Fact]
    public void AskingChangesNothing()
    {
        // A question reads knowledge; it must not create any.
        UKS.UKS uks = LoadAndDiscover("bst_true_template_corpus.txt");
        int thoughtCount = uks.AtomicThoughts.Count;
        int dogLinks = uks.Labeled("dog").LinksTo.Count;

        ModuleText.AnswerQuestion("What can a dog do?");
        ModuleText.AnswerQuestion("What can bark?");

        Assert.Equal(thoughtCount, uks.AtomicThoughts.Count);
        Assert.Equal(dogLinks, uks.Labeled("dog").LinksTo.Count);
    }

    [Fact]
    public void RediscoveringRolesAddsNothingToTheUks()
    {
        UKS.UKS uks = LoadAndDiscover("bst_simple_1000_corpus.txt");
        int roleCount = uks.Labeled("SlotRole").Children.Count;
        int categoryCount = uks.Labeled("LexicalCategory").Children.Count;
        int thoughtCount = uks.AtomicThoughts.Count;

        ModuleText.DiscoverGrammaticalRoles();

        Assert.Equal(roleCount, uks.Labeled("SlotRole").Children.Count);
        Assert.Equal(categoryCount, uks.Labeled("LexicalCategory").Children.Count);
        Assert.Equal(thoughtCount, uks.AtomicThoughts.Count);
    }

    /// <summary>
    /// The corpus writes plurals which the category test scores alongside their
    /// singulars. Relating the two forms is a separate piece of work, so both
    /// spellings are simply accepted here.
    /// </summary>
    private static HashSet<string> ExpandGold(string category, string[] gold)
    {
        HashSet<string> retVal = new(gold, StringComparer.Ordinal);
        if (category == "noun")
            foreach (string noun in gold)
                retVal.Add(noun + "s");
        return retVal;
    }

    private void DumpTemplates(UKS.UKS uks)
    {
        var (introducers, separators) = ModuleText.DescribeFunctionWords();
        output.WriteLine("INTRODUCERS: " + string.Join(", ", introducers));
        output.WriteLine("SEPARATORS: " + string.Join(", ", separators));
        Thought hasWords = uks.Labeled("hasWords");
        foreach (Thought template in uks.Labeled("LearnedTemplate").Children)
        {
            SlotRoleView view = uks.GetSlotRoleView(template, hasWords);
            if (view is null) continue;
            output.WriteLine($"{template.Label}: " + string.Join("  ", Enumerable
                .Range(0, view.Count)
                .Select(i => $"{Show(view.Elements[i])}[{view.Roles[i]?.Label ?? "-"}]")));
        }
        foreach (Thought slotClass in uks.Labeled("LearnedClass").Children.Take(14))
            output.WriteLine($"  {slotClass.Label}: " + string.Join(", ", slotClass.Children
                .Where(c => c is not SeqElement && !c.HasAncestor("Wildcard"))
                .Select(Show).Take(14)));
    }

    private static HashSet<string> Members(UKS.UKS uks, string categoryName)
    {
        Thought category = uks.Labeled(categoryName);
        if (category is null) return new HashSet<string>(StringComparer.Ordinal);
        return category.Children
            .Where(member => member is not SeqElement && !member.HasAncestor("Wildcard"))
            .Select(Show)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string Show(Thought word)
    {
        return word.Label.StartsWith("w:", StringComparison.Ordinal) ? word.Label[2..] : word.Label;
    }

    private static UKS.UKS LoadAndDiscover(string fileName)
    {
        UKS.UKS uks = new(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks;
        uks.GetOrAddThought("LanguageElement", "Thought");
        uks.GetOrAddThought("Phrase", "LanguageElement");
        uks.GetOrAddThought("Word", "LanguageElement");
        uks.GetOrAddThought("hasWords", "LinkType");

        string corpusPath = Path.Combine(FindRepositoryRoot(), "BrainSimulator", "WordFIles", fileName);
        foreach (string line in File.ReadLines(corpusPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Both the phrase and its action exemplar have to be built the way
            // the running program builds them. Creating words here by hand once
            // produced a second vocabulary alongside the "w:" one, which split
            // every count this discovery depends on.
            string[] fields = line.Split('\t', 2);
            string phraseText = fields[0].Trim();
            ModuleText.AddPhrase(phraseText, applyExistingTemplates: false);
            if (fields.Length == 2 && !string.IsNullOrWhiteSpace(fields[1]))
                ModuleText.AddActionExemplar(phraseText, fields[1]);
        }

        ModuleText.ProcessTheExistingText();
        return uks;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BrainSim Thought.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

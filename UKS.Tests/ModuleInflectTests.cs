using System.Linq;
using BrainSimulator.Modules;
using Xunit;
using Xunit.Abstractions;

namespace UKS.Tests;

/// <summary>
/// ModuleInflect: regular forms come from spelling rules (UKSContent/Inflections.txt), only exceptions
/// are stored, and each rule also runs backward to recognize a form.
/// </summary>
public class ModuleInflectTests
{
    private readonly ITestOutputHelper output;
    public ModuleInflectTests(ITestOutputHelper output) => this.output = output;

    private static (UKS store, ModuleInflect module) FreshInflector()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInflect { theUKS = store, KnowledgeFiles = ModuleInflect.CoreFiles };
        module.Initialize();
        return (store, module);
    }

    private static string Form(UKS store, ModuleInflect module, string word, string inflection) =>
        module.Forms(store.Labeled(word), store.Labeled(inflection)).Select(form => form.form).FirstOrDefault();

    [Theory]
    [InlineData("w:dog", "plural", "dogs")]
    [InlineData("w:large", "comparative", "larger")]
    [InlineData("w:large", "superlative", "largest")]
    [InlineData("w:chases", "pastParticiple", "chased")]   // e: +d
    [InlineData("w:plays", "pastParticiple", "played")]    // vowel + y: no change
    [InlineData("w:goes", "pastTense", "went")]            // stored: blocks the rule
    [InlineData("w:goes", "thirdPersonSingular", "goes")]
    public void ABaseWordsFormsComeFromItsStoredFormsOrTheRules(string word, string inflection, string expected)
    {
        var (store, module) = FreshInflector();
        Assert.Equal(expected, Form(store, module, word, inflection));
    }

    [Theory]
    [InlineData("box", "plural", "boxes")]
    [InlineData("church", "plural", "churches")]
    [InlineData("pony", "plural", "ponies")]
    [InlineData("day", "plural", "days")]
    [InlineData("hope", "pastTense", "hoped")]
    [InlineData("carry", "pastTense", "carried")]
    [InlineData("stop", "pastTense", "stopped")]
    [InlineData("chase", "presentParticiple", "chasing")]
    [InlineData("lie", "presentParticiple", "lying")]
    [InlineData("see", "presentParticiple", "seeing")]
    [InlineData("big", "comparative", "bigger")]
    [InlineData("happy", "superlative", "happiest")]
    public void TheMostSpecificRuleThatMatchesWins(string spelling, string inflection, string expected)
    {
        var (store, module) = FreshInflector();
        Assert.Equal(expected, module.ApplyRules(spelling, store.Labeled(inflection), out _));
    }

    [Fact]
    public void AFormIsRecognizedByRunningTheRulesBackward()
    {
        var (store, module) = FreshInflector();
        var analyses = module.Analyze("largest");
        var analysis = Assert.Single(analyses);
        Assert.Same(store.Labeled("w:large"), analysis.Base);
        Assert.Same(store.Labeled("superlative"), analysis.Inflection);
    }

    [Fact]
    public void AStoredFormBlocksTheRegularOne()
    {
        var (_, module) = FreshInflector();
        string report = module.Report("goed");
        output.WriteLine(report);
        Assert.Empty(module.Analyze("goed"));
        Assert.Contains("(not pastTense of w:goes  (edDefault; it is went))", report);
    }

    [Fact]
    public void AnUnknownFormGetsItsMostLikelyBaseFirst()
    {
        var (_, module) = FreshInflector();
        var guesses = module.Guesses("glorped");
        Assert.Equal("glorp", guesses.First().Stem);
        string report = module.Report("glorped");
        output.WriteLine(report);
        Assert.Contains("\"glorp\" (verb): ", report);
    }

    // Backward, a candidate base counts only if the rules give the form back from it; of those, a
    // preferred rule's comes first, then a more specific rule's (UKSContent/Inflections.txt).
    [Theory]
    [InlineData("washes", "plural", "wash")]       // sAfterSH under sDefault
    [InlineData("boxes", "plural", "box")]
    [InlineData("churches", "plural", "church")]
    [InlineData("buzzes", "plural", "buzz")]
    [InlineData("kisses", "plural", "kiss")]       // sAfterSS (preferred) under sAfterS (avoided)
    [InlineData("horses", "plural", "horse")]      // sAfterS avoided: not hors
    [InlineData("roses", "plural", "rose")]
    [InlineData("ponies", "plural", "pony")]
    [InlineData("days", "plural", "day")]
    [InlineData("vetoes", "thirdPersonSingular", "veto")]
    [InlineData("walked", "pastTense", "walk")]    // edAfterE avoided: not walke
    [InlineData("liked", "pastTense", "like")]     // edAfterVowelConsonantE preferred: not lik
    [InlineData("hoped", "pastTense", "hope")]     // hop gives hopped
    [InlineData("played", "pastTense", "play")]
    [InlineData("carried", "pastTense", "carry")]
    [InlineData("stopped", "pastTense", "stop")]
    [InlineData("stopping", "presentParticiple", "stop")]
    [InlineData("hoping", "presentParticiple", "hope")]
    [InlineData("walking", "presentParticiple", "walk")]
    [InlineData("flying", "presentParticiple", "fly")]   // ingAfterIE avoided: not flie
    [InlineData("taller", "comparative", "tall")]
    [InlineData("nicer", "comparative", "nice")]
    [InlineData("hotter", "comparative", "hot")]
    [InlineData("happiest", "superlative", "happy")]
    [InlineData("cooled", "pastTense", "cool")]    // not coole: vowel-vowel-consonant-e is rare
    [InlineData("rained", "pastTense", "rain")]
    [InlineData("seemed", "pastTense", "seem")]
    [InlineData("smiled", "pastTense", "smile")]   // smil gives smilled
    [InlineData("trekked", "pastTense", "trek")]
    [InlineData("starred", "pastTense", "star")]
    [InlineData("stuffed", "pastTense", "stuff")]   // floss: not stuf
    [InlineData("kissed", "pastTense", "kiss")]
    [InlineData("quizzed", "pastTense", "quizz")]  // the floss rule's exception: quiz would have to be stored or known
    public void AnUnknownFormsMostLikelyBaseComesFirst(string spelling, string inflection, string expected)
    {
        var (store, module) = FreshInflector();
        var guesses = module.Guesses(spelling).Where(guess => guess.Inflection == store.Labeled(inflection)).ToList();
        output.WriteLine(string.Join(Environment.NewLine, guesses));
        Assert.Equal(expected, guesses.First().Stem);
    }

    // "lik" would make "likked", so the forward check leaves "like" as the only reading of "liked".
    [Fact]
    public void ACandidateWhoseFormWouldBeDifferentIsRuledOut()
    {
        var (store, module) = FreshInflector();
        var guesses = module.Guesses("liked").Where(guess => guess.Inflection == store.Labeled("pastTense")).ToList();
        output.WriteLine(string.Join(Environment.NewLine, guesses));
        Assert.Equal("like", Assert.Single(guesses).Stem);
    }

    // sAfterO inherits sDefault's plural and thirdPersonSingular but overrides the plural
    // (formsInflection.not): a noun's -o varies, so its plural takes the default rule.
    [Fact]
    public void AChildRuleCanOverrideAnInflectionItWouldInherit()
    {
        var (store, module) = FreshInflector();
        Assert.Equal("vetoes", module.ApplyRules("veto", store.Labeled("thirdPersonSingular"), out var rule));
        Assert.Equal("sAfterO", rule.Label);
        Assert.Equal("photos", module.ApplyRules("photo", store.Labeled("plural"), out rule));
        Assert.Equal("sDefault", rule.Label);
    }

    [Fact]
    public void AnalyzingIsFastEnough()
    {
        var (_, module) = FreshInflector();
        string[] words = ["chased", "washes", "largest", "glorped", "horses", "stopping", "dogs", "went"];
        foreach (string word in words) { module.Analyze(word); module.Guesses(word); }   // warm the rule cache
        var watch = System.Diagnostics.Stopwatch.StartNew();
        const int rounds = 20;
        for (int round = 0; round < rounds; round++)
            foreach (string word in words) { module.Analyze(word); module.Guesses(word); }
        double each = watch.Elapsed.TotalMilliseconds / (rounds * words.Length);
        output.WriteLine($"Analyze + Guesses: {each:F3} ms per word");
        Assert.True(each < 20, $"{each:F3} ms per word");
    }

    // "fetched": fetch is known, but only as a mass noun, which has no tenses. The report says so.
    [Fact]
    public void AKnownWordOfAClassThatDoesNotTakeTheInflectionIsReportedAsSuch()
    {
        var (_, module) = FreshInflector();
        string report = module.Report("fetched");
        output.WriteLine(report);
        Assert.Contains("(not pastTense of w:fetch  (edDefault; massNoun doesn't take pastTense))", report);
        Assert.Contains("no form of a known word.", report);
        Assert.DoesNotContain("(not plural", module.Report("chases"));
    }

    // A spelling shared by two entries (fetch: a mass noun and, here, a verb stored as its bare stem):
    // each entry takes its own class's inflections.
    [Fact]
    public void AWordWithTwoClassesTakesTheInflectionsOfEach()
    {
        var (store, module) = FreshInflector();
        store.ProcessSingleLine("[w:fetch1->is-a->actionVerb]");
        store.ProcessSingleLine("[w:fetch1->means->fetches.!]");
        store.ProcessSingleLine("[w:fetch1->spelled->^f e t c h]");

        output.WriteLine(module.Report("fetched"));
        output.WriteLine(module.Report("fetch"));
        foreach (var (spelling, inflection) in new[] { ("fetched", "pastTense"), ("fetches", "thirdPersonSingular"), ("fetching", "presentParticiple") })
            Assert.Contains(module.Analyze(spelling), analysis =>
                analysis.Base == store.Labeled("w:fetch1") && analysis.Inflection == store.Labeled(inflection));
        Assert.Equal(2, module.Analyze("fetch").Count);   // the mass noun and the verb
    }

    // One entry with a parent per class (as WordNetWords.txt stores words): it takes the inflections
    // of each class, and one class lacking an inflection (properNoun: plural) doesn't stop another.
    [Fact]
    public void AWordWithSeveralParentsTakesTheInflectionsOfEachClass()
    {
        var (store, module) = FreshInflector();
        foreach (string line in new[]
        {
            "[w:glorp->is-a->commonNoun]", "[w:glorp->is-a->verb]", "[w:glorp->spelled->^g l o r p]",
            "[w:zorbia->is-a->commonNoun]", "[w:zorbia->is-a->properNoun]", "[w:zorbia->spelled->^z o r b i a]",
        })
            store.ProcessSingleLine(line);
        var glorp = store.Labeled("w:glorp");
        Assert.Contains(store.Labeled("plural"), module.InflectionsOf(glorp));
        Assert.Contains(store.Labeled("pastTense"), module.InflectionsOf(glorp));
        Assert.Contains(module.Analyze("glorps"), analysis => analysis.Base == glorp && analysis.Inflection == store.Labeled("plural"));
        Assert.Contains(module.Analyze("glorps"), analysis => analysis.Base == glorp && analysis.Inflection == store.Labeled("thirdPersonSingular"));
        Assert.Contains(module.Analyze("glorped"), analysis => analysis.Base == glorp);
        Assert.Contains(store.Labeled("plural"), module.InflectionsOf(store.Labeled("w:zorbia")));
    }

    // One spelling, one word: w:better is an adjective and verb in its own right and the stored
    // comparative of good and of well; "better" is read every way.
    [Fact]
    public void AWordCanAlsoBeAStoredFormOfOthers()
    {
        var (store, module) = FreshInflector();
        foreach (string line in new[]
        {
            "[w:good->is-a->adjective]", "[w:good->spelled->^g o o d]",
            "[w:well->is-a->adjective]", "[w:well->spelled->^w e l l]",
            "[w:better->is-a->adjective]", "[w:better->is-a->verb]", "[w:better->spelled->^b e t t e r]",
            "[w:better->is-a->wordForm]", "[w:good->hasComparative->w:better]", "[w:well->hasComparative->w:better]",
        })
            store.ProcessSingleLine(line);
        var analyses = module.Analyze("better");
        output.WriteLine(string.Join(System.Environment.NewLine, analyses));
        Assert.Contains(analyses, analysis => analysis.Base == store.Labeled("w:better") && analysis.Inflection is null);
        Assert.Contains(analyses, analysis => analysis.Base == store.Labeled("w:good") && analysis.Inflection == store.Labeled("comparative"));
        Assert.Contains(analyses, analysis => analysis.Base == store.Labeled("w:well") && analysis.Inflection == store.Labeled("comparative"));
        Assert.Contains(module.Analyze("betters"), analysis => analysis.Base == store.Labeled("w:better"));   // not "bettered": doubling misfires (betterred)
    }

    [Fact]
    public void AMassNounHasNoPlural()
    {
        var (store, module) = FreshInflector();
        Assert.DoesNotContain(store.Labeled("plural"), module.InflectionsOf(store.Labeled("w:fur")));
        Assert.Contains(store.Labeled("plural"), module.InflectionsOf(store.Labeled("w:dog")));
    }
}

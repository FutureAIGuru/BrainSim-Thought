using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrainSimulator;
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

[CollectionDefinition("ModuleTextPatternLearning", DisableParallelization = true)]
public class ModuleTextPatternLearningCollection
{
}

[Collection("ModuleTextPatternLearning")]
public class ModuleTextPatternLearningTests
{
    [Fact]
    public void TrueTemplateCorpusProducesPatternsAndClasses()
    {
        // The current training pass deliberately excludes questions. Verify the
        // remaining observations exercise the complete discovery pipeline.
        var uks = CreateTextUKS();
        string corpusPath = Path.Combine(
            FindRepositoryRoot(), "BrainSimulator", "WordFIles", "bst_true_template_corpus.txt");

        int phraseCount = 0;
        foreach (string line in File.ReadLines(corpusPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.Contains("what", System.StringComparison.OrdinalIgnoreCase))
                continue;

            string[] words = line
                .Trim()
                .TrimEnd('.', '!', '?')
                .ToLowerInvariant()
                .Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            AddPhrase(uks, words);
            phraseCount++;
        }

        int patternsCreated = ModuleText.FindUniversalPatterns();
        int overlapsCreated = ModuleText.ComputeUniversalPatternOverlap();
        int classesCreated = ModuleText.CreateUniversalPatternClasses();
        int spellingRulesCreated = ModuleText.FindClassSpellingRules();
        int templatesCreated = ModuleText.CreateClassBasedTemplates();
        int classFamiliesCreated = ModuleText.CreateClassFamiliesFromSpellingRules();
        int followerClassesCreated = ModuleText.CreateFollowerClassesFromSpellingRules();
        int grammarTemplatesCreated = ModuleText.CreateClassPairTemplates();

        Assert.Equal(582, phraseCount);
        Assert.True(patternsCreated > 0);
        Assert.True(overlapsCreated > 0);
        Assert.True(classesCreated > 0);
        Assert.True(spellingRulesCreated > 0);
        Assert.Contains(uks.Labeled("SpellingRule").Children, rule =>
            uks.FlattenSequence((SeqElement)rule.GetTargetOfFirstLinkOfType("hasSourcePattern"))
                .Select(x => x.Label)
                .SequenceEqual(new[] { "??spellingPrefix", "spellingend" }) &&
            uks.FlattenSequence((SeqElement)rule.GetTargetOfFirstLinkOfType("hasTargetPattern"))
                .Select(x => x.Label)
                .SequenceEqual(new[] { "??spellingPrefix", "c:S", "spellingend" }));
        Assert.True(templatesCreated > 0);
        Assert.True(classFamiliesCreated >= 2);
        Assert.True(followerClassesCreated >= 2);
        Assert.True(grammarTemplatesCreated > 0);

        Thought sourceFollowers = uks.Labeled("LearnedClass").Children.FirstOrDefault(x =>
        {
            HashSet<string> labels = x.Children
                .Where(y => !y.HasProperty("isWildcard"))
                .Select(y => y.Label.StartsWith("w:") ? y.Label[2..] : y.Label)
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
            return new[] { "is", "has", "eats", "plays" }.All(labels.Contains);
        });
        Thought targetFollowers = uks.Labeled("LearnedClass").Children.FirstOrDefault(x =>
        {
            HashSet<string> labels = x.Children
                .Where(y => !y.HasProperty("isWildcard"))
                .Select(y => y.Label.StartsWith("w:") ? y.Label[2..] : y.Label)
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
            return new[] { "are", "have", "eat", "play" }.All(labels.Contains);
        });
        string learnedMemberships = string.Join(" | ", uks.Labeled("LearnedClass").Children
            .Select(x => x.Label + ":" + string.Join(",", x.Children
                .Where(y => !y.HasProperty("isWildcard"))
                .Select(y => y.Label))));
        Assert.True(sourceFollowers is not null, learnedMemberships);
        Assert.True(targetFollowers is not null, learnedMemberships);
        Assert.NotNull(sourceFollowers.HasLink(uks.Labeled("contextChangesTo"), targetFollowers));

        Thought sourceGrammarTemplate = uks.Labeled("GrammarTemplate").Children.FirstOrDefault(x =>
            x.LinksTo.Any(y => y.LinkType?.Label == "usesClass" && y.To == sourceFollowers));
        Assert.NotNull(sourceGrammarTemplate);
        Assert.Contains(sourceGrammarTemplate.LinksTo,
            x => x.LinkType?.Label == "usesClass" && x.To.HasProperty("isClassFamily"));
        Assert.Equal(2, uks.FlattenSequence(
            (SeqElement)sourceGrammarTemplate.GetTargetOfFirstLinkOfType("hasPattern")).Count);
        Thought targetGrammarTemplate = sourceGrammarTemplate.GetTargetOfFirstLinkOfType("correspondsTo");
        Assert.NotNull(targetGrammarTemplate);
        Assert.Contains(targetGrammarTemplate.LinksTo,
            x => x.LinkType?.Label == "usesClass" && x.To == targetFollowers);
    }

    [Fact]
    public void PatternDiscoveryBuildsClassFromOverlappingSurfacePatterns()
    {
        // The two sentence frames have the same interchangeable subject words.
        // Pattern discovery should learn the frames without repeated sequence
        // searches, then turn their shared fillers into a constrained wildcard.
        var uks = CreateTextUKS();

        AddPhrase(uks, "the", "dog", "runs");
        AddPhrase(uks, "the", "cat", "runs");
        AddPhrase(uks, "the", "bird", "runs");
        AddPhrase(uks, "the", "dog", "sleeps");
        AddPhrase(uks, "the", "cat", "sleeps");
        AddPhrase(uks, "the", "bird", "sleeps");

        int patternsCreated = ModuleText.FindUniversalPatterns(minMatches: 3, maxWildcards: 1, maxLength: 3);
        int overlapsCreated = ModuleText.ComputeUniversalPatternOverlap(minShared: 3, minOverlap: 1f);
        int classesCreated = ModuleText.CreateUniversalPatternClasses(minMembers: 3, minPatterns: 2, minOverlap: 1f);

        Assert.True(patternsCreated >= 2);
        Assert.True(overlapsCreated >= 1);
        Assert.Equal(1, classesCreated);

        Thought learnedClass = uks.Labeled("LearnedClass").Children.Single(x =>
            x.Children.Any(member => member.Label == "dog"));
        HashSet<string> members = learnedClass.Children
            .Where(x => !x.HasProperty("isWildcard"))
            .Select(x => x.Label)
            .ToHashSet();
        Assert.Equal(new HashSet<string> { "dog", "cat", "bird" }, members);

        Thought classWildcard = uks.Labeled("??" + learnedClass.Label);
        Assert.NotNull(classWildcard);
        Assert.True(classWildcard.HasProperty("isWildcard"));
        Assert.Contains(learnedClass, classWildcard.Parents);

        var classMatches = uks.FindSequencesByActivation(
            new List<Thought> { uks.Labeled("the"), classWildcard, uks.Labeled("runs") },
            uks.Labeled("TemplateSequenceSearch"));
        Assert.Equal(3, classMatches.Count(x =>
            !uks.FlattenSequence(x.seqNode).Any(word => word.HasProperty("isWildcard"))));

        Assert.Equal(0, ModuleText.CreateUniversalPatternClasses(
            minMembers: 3, minPatterns: 2, minOverlap: 1f));
        Assert.Equal(2, learnedClass.LinksTo.Count(x => x.LinkType?.Label == "evidence"));

        int templatesCreated = ModuleText.CreateClassBasedTemplates(
            minTemplateAgreements: 2, minMembershipConfidence: 1f);
        Assert.Equal(2, templatesCreated);
        Assert.Equal(2, uks.Labeled("LearnedTemplate").Children.Count);
        Assert.All(uks.Labeled("LearnedTemplate").Children, template =>
        {
            Assert.Equal(3, template.Weight);
            Assert.Empty(template.LinksTo.Where(x => x.LinkType?.Label == "exception"));
        });

        Assert.Equal(0, ModuleText.CreateClassBasedTemplates(
            minTemplateAgreements: 2, minMembershipConfidence: 1f));
    }

    [Fact]
    public void IndependentTemplateAgreementExpandsLearnedClass()
    {
        // Goat occurs in two of the three supporting frames. It is absent from
        // the strict shared core, but two independent templates provide enough
        // evidence to admit it as a tentative class member.
        var uks = CreateTextUKS();

        AddPhrase(uks, "the", "dog", "runs");
        AddPhrase(uks, "the", "cat", "runs");
        AddPhrase(uks, "the", "bird", "runs");
        AddPhrase(uks, "the", "goat", "runs");
        AddPhrase(uks, "the", "dog", "sleeps");
        AddPhrase(uks, "the", "cat", "sleeps");
        AddPhrase(uks, "the", "bird", "sleeps");
        AddPhrase(uks, "the", "goat", "sleeps");
        AddPhrase(uks, "the", "dog", "eats");
        AddPhrase(uks, "the", "cat", "eats");
        AddPhrase(uks, "the", "bird", "eats");

        ModuleText.FindUniversalPatterns(minMatches: 3, maxWildcards: 1, maxLength: 3);
        ModuleText.ComputeUniversalPatternOverlap(minShared: 3, minOverlap: 0.7f);
        ModuleText.CreateUniversalPatternClasses(minMembers: 3, minPatterns: 3, minOverlap: 0.7f);

        Thought learnedClass = uks.Labeled("LearnedClass").Children.Single(x =>
            x.Children.Any(member => member.Label == "dog"));
        Assert.DoesNotContain(learnedClass, uks.Labeled("goat").Parents);

        ModuleText.CreateClassBasedTemplates(minTemplateAgreements: 2, minMembershipConfidence: 0.5f);

        Assert.Contains(learnedClass, uks.Labeled("goat").Parents);
        Link membership = uks.Labeled("goat").LinksTo.Single(x =>
            x.LinkType?.Label == "is-a" && x.To == learnedClass);
        Assert.InRange(membership.Weight, 0.66f, 0.67f);
        Assert.Equal(2, membership.LinksTo.Count(x => x.LinkType?.Label == "evidence"));
    }

    [Fact]
    public void ClassSpellingRulesDiscoverRepeatedAppendWithoutNamingItsMeaning()
    {
        // The learner knows only that these are two distributional classes. Four
        // independent spelling pairs should support preserving a spelling and
        // adding S at its end. The shared spelling supplies no change evidence.
        var uks = CreateTextUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought classA = uks.GetOrAddThought("testClassA", classRoot);
        Thought classB = uks.GetOrAddThought("testClassB", classRoot);
        Thought classC = uks.GetOrAddThought("testClassC", classRoot);
        Thought classD = uks.GetOrAddThought("testClassD", classRoot);

        foreach (string word in new[] { "dog", "cat", "bird", "mouse", "shared" })
            AddSpelledWord(uks, word).AddParent(classA);
        foreach (string word in new[] { "dogs", "cats", "birds", "mouses", "shared" })
            AddSpelledWord(uks, word).AddParent(classB);
        foreach (string word in new[] { "car", "tree", "book", "shoe" })
            AddSpelledWord(uks, word).AddParent(classC);
        foreach (string word in new[] { "cars", "trees", "books", "shoes" })
            AddSpelledWord(uks, word).AddParent(classD);

        int rulesCreated = ModuleText.FindClassSpellingRules(minMatches: 4);

        Assert.True(rulesCreated >= 2); // append S and its reverse removal
        Thought rule = uks.Labeled("SpellingRule").Children.Single(x =>
            uks.FlattenSequence((SeqElement)x.GetTargetOfFirstLinkOfType("hasSourcePattern"))
                .Select(y => y.Label)
                .SequenceEqual(new[] { "??spellingPrefix", "spellingend" }) &&
            uks.FlattenSequence((SeqElement)x.GetTargetOfFirstLinkOfType("hasTargetPattern"))
                .Select(y => y.Label)
                .SequenceEqual(new[] { "??spellingPrefix", "c:S", "spellingend" }));
        Assert.Equal("spellingend", rule.GetTargetOfFirstLinkOfType("changesAt").Label);
        Assert.Equal(8, rule.Weight);

        Assert.Equal(new[] { "??spellingPrefix", "spellingend" },
            uks.FlattenSequence((SeqElement)rule.GetTargetOfFirstLinkOfType("hasSourcePattern"))
                .Select(x => x.Label));
        Assert.Equal(new[] { "??spellingPrefix", "c:S", "spellingend" },
            uks.FlattenSequence((SeqElement)rule.GetTargetOfFirstLinkOfType("hasTargetPattern"))
                .Select(x => x.Label));

        List<Link> applications = rule.LinksTo
            .Where(x => x.LinkType?.Label == "appliesTo")
            .Select(x => x.To)
            .OfType<Link>()
            .ToList();
        Assert.Equal(2, applications.Count);
        Link classApplication = applications.Single(x => x.From == classA && x.To == classB);
        Assert.Equal(0.8f, classApplication.Weight);
        Assert.Same(rule, classApplication.GetTargetOfFirstLinkOfType("usesRule"));

        List<Link> evidence = classApplication.LinksTo
            .Where(x => x.LinkType?.Label == "evidence")
            .Select(x => x.To)
            .OfType<Link>()
            .ToList();
        Assert.Equal(4, evidence.Count);
        Assert.Contains(evidence, x => x.From.Label == "dog" && x.To.Label == "dogs");
        Assert.DoesNotContain(evidence, x => x.From.Label == "shared" || x.To.Label == "shared");

        Thought predicted = ModuleText.ApplySpellingRule(rule, AddSpelledWord(uks, "fish"));
        Assert.Equal("w:fishs", predicted.Label);
        Assert.Equal(new[] { "c:F", "c:I", "c:S", "c:H", "c:S" },
            uks.FlattenSequence((SeqElement)predicted.GetTargetOfFirstLinkOfType("spelled"))
                .Select(x => x.Label));
        Assert.Same(predicted, ModuleText.ApplySpellingRule(rule, uks.Labeled("fish")));

        Assert.Equal(0, ModuleText.FindClassSpellingRules(minMatches: 4));
    }

    [Fact]
    public void SpellingRuleSidesProduceAnonymousFollowerClasses()
    {
        // Words immediately following several subject classes on the same side
        // of a spelling rule should form distributional classes. The UKS does not
        // label them as verbs or assign any other grammatical interpretation.
        var uks = CreateTextUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought classA = uks.GetOrAddThought("subjectClassA", classRoot);
        Thought classB = uks.GetOrAddThought("subjectClassB", classRoot);
        Thought classC = uks.GetOrAddThought("subjectClassC", classRoot);
        Thought classD = uks.GetOrAddThought("subjectClassD", classRoot);

        foreach (string word in new[] { "dog", "cat", "bird", "mouse" })
            AddSpelledWord(uks, word).AddParent(classA);
        foreach (string word in new[] { "dogs", "cats", "birds", "mouses" })
            AddSpelledWord(uks, word).AddParent(classB);
        foreach (string word in new[] { "car", "tree", "book", "shoe" })
            AddSpelledWord(uks, word).AddParent(classC);
        foreach (string word in new[] { "cars", "trees", "books", "shoes" })
            AddSpelledWord(uks, word).AddParent(classD);
        ModuleText.FindClassSpellingRules(minMatches: 4);
        int familiesCreated = ModuleText.CreateClassFamiliesFromSpellingRules();
        Assert.Equal(2, familiesCreated);
        Thought sourceFamily = classRoot.Children.Single(x =>
            x.HasProperty("isClassFamily") && x.Children.Contains(classA));
        Thought targetFamily = classRoot.Children.Single(x =>
            x.HasProperty("isClassFamily") && x.Children.Contains(classB));
        Assert.Contains(classC, sourceFamily.Children);
        Assert.Contains(classD, targetFamily.Children);
        Link familyApplication = sourceFamily.HasLink(uks.Labeled("spellingChangesTo"), targetFamily);
        Assert.NotNull(familyApplication);
        Assert.Equal(2, familyApplication.LinksTo.Count(x => x.LinkType?.Label == "evidence"));

        Thought wildcardA = uks.CreateWildcard("??subjectClassA", new List<Thought> { classA });
        Thought wildcardB = uks.CreateWildcard("??subjectClassB", new List<Thought> { classB });
        Thought wildcardC = uks.CreateWildcard("??subjectClassC", new List<Thought> { classC });
        Thought wildcardD = uks.CreateWildcard("??subjectClassD", new List<Thought> { classD });
        Thought isWord = AddSpelledWord(uks, "w:is");
        Thought areWord = AddSpelledWord(uks, "w:are");
        Thought hasWord = AddSpelledWord(uks, "w:has");
        Thought haveWord = AddSpelledWord(uks, "w:have");

        AddLearnedTemplate(uks, "a-is", wildcardA, isWord, AddSpelledWord(uks, "w:small"));
        AddLearnedTemplate(uks, "c-is", wildcardC, isWord, AddSpelledWord(uks, "w:large"));
        AddLearnedTemplate(uks, "a-has", wildcardA, hasWord, AddSpelledWord(uks, "w:fur"));
        AddLearnedTemplate(uks, "c-has", wildcardC, hasWord, AddSpelledWord(uks, "w:tail"));
        AddLearnedTemplate(uks, "b-are", wildcardB, areWord, uks.Labeled("w:small"));
        AddLearnedTemplate(uks, "d-are", wildcardD, areWord, uks.Labeled("w:large"));
        AddLearnedTemplate(uks, "b-have", wildcardB, haveWord, uks.Labeled("w:fur"));
        AddLearnedTemplate(uks, "d-have", wildcardD, haveWord, uks.Labeled("w:tail"));

        int classesCreated = ModuleText.CreateFollowerClassesFromSpellingRules(
            minMembers: 2, minTemplatesPerMember: 2, minSubjectClassesPerMember: 2);

        Assert.Equal(2, classesCreated);
        Thought sourceFollowers = classRoot.Children.Single(x =>
            x.Children.Contains(isWord) && x.Children.Contains(hasWord));
        Thought targetFollowers = classRoot.Children.Single(x =>
            x.Children.Contains(areWord) && x.Children.Contains(haveWord));
        Assert.Equal(new HashSet<Thought> { isWord, hasWord },
            sourceFollowers.Children.Where(x => !x.HasProperty("isWildcard")).ToHashSet());
        Assert.Equal(new HashSet<Thought> { areWord, haveWord },
            targetFollowers.Children.Where(x => !x.HasProperty("isWildcard")).ToHashSet());

        Link association = sourceFollowers.HasLink(uks.Labeled("contextChangesTo"), targetFollowers);
        Assert.NotNull(association);
        Assert.NotNull(association.GetTargetOfFirstLinkOfType("conditionedBy"));

        int grammarTemplatesCreated = ModuleText.CreateClassPairTemplates();
        string grammarTemplateDetails = string.Join(" | ", uks.Labeled("GrammarTemplate").Children.Select(x =>
            x.Label + ":" + string.Join(",", x.LinksTo
                .Where(y => y.LinkType?.Label == "usesClass")
                .Select(y => y.To?.Label))));
        Assert.True(grammarTemplatesCreated == 2,
            $"Expected two grammar templates but created {grammarTemplatesCreated}: {grammarTemplateDetails}");
        Thought sourceTemplate = uks.Labeled("GrammarTemplate").Children.Single(x =>
            x.LinksTo.Count(y => y.LinkType?.Label == "usesClass" &&
                (y.To == sourceFamily || y.To == sourceFollowers)) == 2);
        Assert.Equal(new[] { "??" + sourceFamily.Label, "??" + sourceFollowers.Label },
            uks.FlattenSequence((SeqElement)sourceTemplate.GetTargetOfFirstLinkOfType("hasPattern"))
                .Select(x => x.Label));
        Assert.Equal(4, sourceTemplate.LinksTo.Count(x => x.LinkType?.Label == "templateEvidence"));
        Thought pairedTemplate = sourceTemplate.GetTargetOfFirstLinkOfType("correspondsTo");
        Assert.NotNull(pairedTemplate);
        Assert.Contains(pairedTemplate.LinksTo,
            x => x.LinkType?.Label == "usesClass" && x.To == targetFamily);
        Assert.Contains(pairedTemplate.LinksTo,
            x => x.LinkType?.Label == "usesClass" && x.To == targetFollowers);
        Assert.Equal(0, ModuleText.CreateClassPairTemplates());
        Assert.Equal(0, ModuleText.CreateClassFamiliesFromSpellingRules());

        Assert.Equal(0, ModuleText.CreateFollowerClassesFromSpellingRules(
            minMembers: 2, minTemplatesPerMember: 2, minSubjectClassesPerMember: 2));
    }

    [Fact]
    public void TemplateFamiliesLearnOptionalArticleAndRepeatableAdjectives()
    {
        // These remain four finite observed templates. Consolidation should retain
        // every variant while describing their common noun-phrase prefix as an
        // optional article followed by zero or more adjectives.
        var uks = CreateTextUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought nounClass = uks.GetOrAddThought("nounClass", classRoot);
        Thought articleClass = uks.GetOrAddThought("articleClass", classRoot);
        Thought adjectiveClass = uks.GetOrAddThought("adjectiveClass", classRoot);
        Thought noun = uks.CreateWildcard("??nounClass", new List<Thought> { nounClass });
        Thought article = uks.CreateWildcard("??articleClass", new List<Thought> { articleClass });
        Thought adjective = uks.CreateWildcard("??adjectiveClass", new List<Thought> { adjectiveClass });

        AddLearnedTemplate(uks, "bare", noun, uks.GetOrAddThought("is"), uks.GetOrAddThought("wet"));
        AddLearnedTemplate(uks, "article", article, noun, uks.Labeled("is"), uks.Labeled("wet"));
        AddLearnedTemplate(uks, "one-adjective", article, adjective, noun, uks.Labeled("is"), uks.Labeled("wet"));
        AddLearnedTemplate(uks, "two-adjectives", article, adjective, adjective, noun, uks.Labeled("is"), uks.Labeled("wet"));

        int familiesCreated = ModuleText.CreateTemplateFamilies();

        Assert.Equal(1, familiesCreated);
        Thought family = uks.Labeled("TemplateFamily").Children.Single();
        Assert.Equal(4, family.LinksTo.Count(x => x.LinkType?.Label == "variant"));
        Assert.Equal(new[] { "??nounClass", "is", "wet" },
            uks.FlattenSequence((SeqElement)family.GetTargetOfFirstLinkOfType("hasCorePattern"))
                .Select(x => x.Label));

        List<Thought> segments = family.LinksTo
            .Where(x => x.LinkType?.Label == "hasSegment")
            .Select(x => x.To)
            .ToList();
        Assert.Equal(2, segments.Count);

        Thought articleSegment = segments.Single(x =>
            x.GetTargetOfFirstLinkOfType("usesClass") == articleClass);
        Assert.True(articleSegment.HasProperty("isOptional"));
        Assert.False(articleSegment.HasProperty("isRepeatable"));
        Assert.Equal("0", articleSegment.GetTargetOfFirstLinkOfType("minimum").Label);
        Assert.Equal("1", articleSegment.GetTargetOfFirstLinkOfType("maximum").Label);

        Thought adjectiveSegment = segments.Single(x =>
            x.GetTargetOfFirstLinkOfType("usesClass") == adjectiveClass);
        Assert.True(adjectiveSegment.HasProperty("isOptional"));
        Assert.True(adjectiveSegment.HasProperty("isRepeatable"));
        Assert.Equal("0", adjectiveSegment.GetTargetOfFirstLinkOfType("minimum").Label);
        Assert.Equal("2", adjectiveSegment.GetTargetOfFirstLinkOfType("maximum").Label);

        Assert.Equal(0, ModuleText.CreateTemplateFamilies());
        Assert.Single(uks.Labeled("TemplateFamily").Children);
    }

    private static void AddPhrase(UKS.UKS uks, params string[] labels)
    {
        Thought phrase = uks.GetOrAddThought("testPhrase*", "Phrase");
        List<Thought> words = labels
            .Select(label => AddSpelledWord(uks, label))
            .ToList();
        uks.AddSequenceAndLink(phrase, "hasWords", words);
    }

    private static void AddLearnedTemplate(UKS.UKS uks, string label, params Thought[] elements)
    {
        Thought root = uks.GetOrAddThought("LearnedTemplate", "LanguageElement");
        uks.GetOrAddThought("hasPattern", "LinkType");
        Thought template = uks.GetOrAddThought("testTemplate-" + label, root);
        template.Weight = 1;
        uks.AddSequenceAndLink(template, "hasPattern", elements.ToList());
    }

    private static Thought AddSpelledWord(UKS.UKS uks, string label)
    {
        Thought word = uks.GetOrAddThought(label, "Word");
        if (word.GetTargetOfFirstLinkOfType("spelled") is null)
        {
            string spelling = label.StartsWith("w:", System.StringComparison.OrdinalIgnoreCase)
                ? label[2..]
                : label;
            List<Thought> letters = spelling.ToUpperInvariant()
                .Select(letter => uks.GetOrAddThought("c:" + letter, "letter"))
                .ToList();
            uks.AddSequenceAndLink(word, uks.GetOrAddThought("spelled", "LinkType"), letters);
        }
        return word;
    }

    private static UKS.UKS CreateTextUKS()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks;
        uks.GetOrAddThought("LanguageElement", "Thought");
        uks.GetOrAddThought("Phrase", "LanguageElement");
        uks.GetOrAddThought("Word", "LanguageElement");
        uks.GetOrAddThought("hasWords", "LinkType");
        return uks;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new(System.AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BrainSim Thought.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

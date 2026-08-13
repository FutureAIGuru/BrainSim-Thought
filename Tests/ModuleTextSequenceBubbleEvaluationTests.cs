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

[CollectionDefinition("ModuleTextPatternLearning", DisableParallelization = true)]
public class ModuleTextPatternLearningCollection
{
}

[Collection("ModuleTextPatternLearning")]
public class ModuleTextSequenceBubbleEvaluationTests
{
    private readonly ITestOutputHelper output;

    public ModuleTextSequenceBubbleEvaluationTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void EntireCorpusPhraseSetProducesTemplatesWithPhraseEvidence()
    {
        // No grammar pipeline supplies classes or templates. The sequence
        // discovery mechanism starts with every observed phrase and groups the
        // phrase owners solely by common structures in their hasWords sequences.
        UKS.UKS uks = CreateTextUKS();
        LoadCorpus(uks);

        Thought phraseRoot = uks.Labeled("Phrase");
        List<Thought> learnedTemplates = ModuleText.DiscoverPhraseTemplates(10, 1);

        Assert.NotEmpty(learnedTemplates);
        Assert.All(learnedTemplates, learnedTemplate =>
        {
            SequenceView description = Assert.Single(uks.GetSequenceViews(learnedTemplate)
                .Where(view => view.LinkType?.Label == "hasWords"));
            Assert.True(description.Elements.Count(element => !element.HasAncestor("Wildcard")) >= 1);
            Assert.All(description.Elements.Where(element => element.HasAncestor("Wildcard")), wildcard =>
            {
                Assert.True(wildcard.HasProperty("isWildcard"));
                Assert.StartsWith("??class", wildcard.Label);
                Assert.Contains(wildcard.Parents, parent => parent.Label != "Wildcard");
            });
            Assert.Empty(learnedTemplate.Children);
            List<Thought> evidence = learnedTemplate.LinksTo
                .Where(link => link.LinkType?.Label == "evidence")
                .Select(link => link.To)
                .ToList();
            Assert.NotEmpty(evidence);
            Assert.All(evidence, phrase => Assert.Contains(phrase, phraseRoot.Children));
        });

        foreach (Thought learnedTemplate in learnedTemplates.Take(20))
        {
            SequenceView description = uks.GetSequenceViews(learnedTemplate)
                .Single(view => view.LinkType?.Label == "hasWords");
            int evidenceCount = learnedTemplate.LinksTo.Count(link => link.LinkType?.Label == "evidence");
            output.WriteLine(
                $"{learnedTemplate.Label} ({evidenceCount} phrases): " +
                string.Join(' ', description.Elements.Select(x => x.Label)));
        }

        List<Thought> tokenClasses = ModuleText.DiscoverTemplateTokenClasses();
        Assert.Contains(tokenClasses, learnedClass =>
        {
            HashSet<string> members = learnedClass.Children.Select(member =>
                member.Label.StartsWith("w:") ? member.Label[2..] : member.Label).ToHashSet();
            return new[] { "a", "an", "the" }.All(members.Contains);
        });
        Assert.Contains(tokenClasses, learnedClass =>
        {
            HashSet<string> members = learnedClass.Children.Select(member =>
                member.Label.StartsWith("w:") ? member.Label[2..] : member.Label).ToHashSet();
            return new[] { "is", "are", "has", "have", "can" }.All(members.Contains);
        });
        foreach (Thought tokenClass in tokenClasses)
            output.WriteLine($"{tokenClass.Label}: " +
                string.Join(", ", tokenClass.Children.Select(member => member.Label)));
    }

    [Fact]
    public void CorpusActionExamplesTeachTemplatesToPerformSimpleAssertions()
    {
        // English intent: the learner is shown several ordinary assertions
        // together with the relationships they mean. It should learn the
        // sentence frames, then use those frames to understand a new subject
        // without being given an action specifically for that subject.
        UKS.UKS uks = CreateTextUKS();
        LoadCorpus(uks);

        // The SET relationship is an instruction demonstrated by the corpus,
        // not an assertion bird should own. Loading the annotated observation
        // executes it and retains the resulting ordinary relationship.
        Thought bird = uks.Labeled("bird");
        Thought fly = uks.Labeled("fly");
        Assert.Null(uks.GetLink(bird, uks.Labeled("SET.can"), fly));
        Assert.NotNull(uks.GetLink(bird, uks.Labeled("can"), fly));

        int learnedActionTemplates = ModuleText.LearnActionsFromExemplars();

        Assert.True(learnedActionTemplates >= 4);
        HashSet<string> learnedActionTypes = uks.Labeled("Assertion").Children
            .SelectMany(template => template.LinksTo)
            .Where(link => link.LinkType?.Label == "means")
            .Select(link => link.To)
            .OfType<Link>()
            .Select(action => action.LinkType.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("SET.is-a", learnedActionTypes);
        Assert.Contains("SET.is", learnedActionTypes);
        Assert.Contains("SET.has", learnedActionTypes);
        Assert.Contains("SET.can", learnedActionTypes);

        // "otter" has never appeared in the corpus. Known predicate values
        // make the intended learned frame unambiguous in this first version.
        AssertUnderstands("A otter is a dog.", "is-a", "dog");
        AssertUnderstands("A otter is brown.", "is", "brown");
        AssertUnderstands("A otter has a tail.", "has", "tail");
        AssertUnderstands("A otter can bark.", "can", "bark");

        void AssertUnderstands(string sentence, string linkType, string target)
        {
            var module = new ModuleText { theUKS = uks };
            module.SubmitText(sentence);
            Assert.True(module.LastTemplate?.HasAncestor("Assertion") == true, module.LastStatus);
            Assert.NotNull(uks.GetLink(
                uks.Labeled("otter"), uks.Labeled(linkType), uks.Labeled(target)));
        }
    }

    [Fact]
    public void NumericWordsAndDigitsProduceTheSameParameterizedRelationship()
    {
        // "four" and "4" are different written words, but both mean the
        // canonical number 4. A learned action uses that meaning to construct
        // has.4 rather than treating the quantity as an ordinary object.
        UKS.UKS uks = CreateTextUKS();
        ModuleText.AddActionExemplar(
            "dog has four legs", "[dog->SET.has.4->leg]");
        ModuleText.AddActionExemplar(
            "cat has 4 legs", "[cat->SET.has.4->leg]");
        ModuleText.AddActionExemplar(
            "rabbit has four legs", "[rabbit->SET.has.4->leg]");
        ModuleText.AddActionExemplar(
            "horse has 4 legs", "[horse->SET.has.4->leg]");

        int learnedActionTemplates = ModuleText.LearnActionsFromExemplars();
        var module = new ModuleText { theUKS = uks };
        module.SubmitText("otter has four legs");
        Thought wordTemplate = module.LastTemplate;
        module.SubmitText("badger has 4 legs");
        Thought digitTemplate = module.LastTemplate;

        Assert.True(learnedActionTemplates > 0);
        Assert.True(wordTemplate?.HasAncestor("Assertion") == true);
        Assert.True(digitTemplate?.HasAncestor("Assertion") == true);
        Assert.Same(uks.Labeled("4"),
            uks.Labeled("w:four").GetTargetOfFirstLinkOfType("means"));
        Assert.Same(uks.Labeled("4"),
            uks.Labeled("w:4").GetTargetOfFirstLinkOfType("means"));
        Assert.NotNull(uks.GetLink(
            uks.Labeled("otter"), uks.Labeled("has.4"), uks.Labeled("leg")));
        Assert.NotNull(uks.GetLink(
            uks.Labeled("badger"), uks.Labeled("has.4"), uks.Labeled("leg")));
    }

    [Fact]
    public void WildcardQueryTemplatesReturnAttributesAndRelationships()
    {
        UKS.UKS uks = CreateTextUKS();
        ModuleText.AddActionExemplar("What is a dog like", "[dog->TEST.??->??]");
        ModuleText.AddActionExemplar("What is a sheep like", "[sheep->TEST.??->??]");
        ModuleText.AddActionExemplar("How is Mary related to John", "[Mary->TEST.??->John]");
        ModuleText.AddActionExemplar("How is Alice related to Bob", "[Alice->TEST.??->Bob]");
        ModuleText.LearnActionsFromExemplars();

        uks.AddStatement("cat", "can", "meow");
        uks.AddStatement("Carol", "knows", "Dave");
        ModuleText module = new() { theUKS = uks };

        string attributeAnswer = module.SubmitText("What is a cat like");
        string relationshipAnswer = module.SubmitText("How is Carol related to Dave");

        Assert.Contains("meow", attributeAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("knows", relationshipAnswer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExactActionExemplarRemainsUsableBeforeAQueryTemplateCanBeLearned()
    {
        UKS.UKS uks = CreateTextUKS();
        ModuleText.AddActionExemplar("How is Mary related to John", "[Mary->TEST.??->John]");
        uks.AddStatement("Mary", "knows", "John");
        ModuleText module = new() { theUKS = uks };

        string answer = module.SubmitText("How is mary related to John?");

        Assert.Contains("knows", answer, StringComparison.OrdinalIgnoreCase);
        Assert.True(module.LastTemplate?.HasAncestor("ActionExemplar") == true);
    }

    [Fact]
    public void FilteredQueryTemplatesConstrainTargetsAndRetainNumericLinkTypes()
    {
        UKS.UKS uks = CreateTextUKS();
        Thought color = uks.GetOrAddThought("Color", "Object");
        uks.GetOrAddThought("brown", color);
        uks.GetOrAddThought("black", color);
        uks.GetOrAddThought("large", "Object");
        uks.GetOrAddThought("leg", "Object");
        uks.AddStatement("Spot", "is", "brown");
        uks.AddStatement("Spot", "is", "large");
        uks.AddStatement("dog", "has.4", "leg");
        ModuleText.AddActionExemplar("What color is Fido", "[[Fido->TEST.is->??]->filterBy->Color]");
        ModuleText.AddActionExemplar("What color is Rover", "[[Rover->TEST.is->??]->filterBy->Color]");
        ModuleText.AddActionExemplar("How many legs does a dog have", "[[dog->TEST.has->??]->filterBy->leg]");
        ModuleText.AddActionExemplar("How many legs does a cat have", "[[cat->TEST.has->??]->filterBy->leg]");
        ModuleText.LearnActionsFromExemplars();
        ModuleText module = new() { theUKS = uks };

        string colorAnswer = module.SubmitText("What color is Spot");
        string legAnswer = module.SubmitText("How many legs does a dog have");

        Assert.Contains("brown", colorAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("large", colorAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4", legAnswer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FrenchCorpusRetainsAnnotatedActionExemplars()
    {
        // File ingestion learns the observed language forms, retains the
        // tab-delimited SET annotations, and completes the shared template and
        // action-learning pass without requiring a separate Process click.
        UKS.UKS uks = CreateTextUKS();
        string corpusPath = Path.Combine(
            FindRepositoryRoot(), "BrainSimulator", "WordFIles",
            "bst_true_template_corpus_fr.txt");
        int expectedPhrases = File.ReadLines(corpusPath)
            .Count(line => !string.IsNullOrWhiteSpace(line));
        var module = new ModuleText { theUKS = uks };

        int loadedPhrases = module.LoadTextFromFile(
            corpusPath, expectedPhrases + 1);
        int incrementallyLearnedTemplates = uks.Labeled("Assertion")?.Children.Count ?? 0;

        Assert.Equal(expectedPhrases, loadedPhrases);
        Assert.True(incrementallyLearnedTemplates > 0);
        Assert.NotNull(uks.Labeled("ActionExemplar"));
        Assert.NotEmpty(uks.Labeled("ActionExemplar").Children);
        Assert.True(uks.Labeled("Phrase").Children.Count <= 50);
        Assert.NotNull(uks.Labeled("w:chien"));
        Assert.NotNull(uks.Labeled("w:bêler"));
        Assert.Same(uks.Labeled("4"),
            uks.Labeled("w:quatre").GetTargetOfFirstLinkOfType("means"));
        Assert.Same(uks.Labeled("4"),
            uks.Labeled("w:4").GetTargetOfFirstLinkOfType("means"));
    }

    [Fact]
    public void EnglishAndFrenchCorporaSupportFrenchAssertionsAndQueriesEndToEnd()
    {
        UKS.UKS uks = CreateTextUKS();
        string wordFiles = Path.Combine(
            FindRepositoryRoot(), "BrainSimulator", "WordFIles");
        var module = new ModuleText { theUKS = uks };

        LoadEntireCorpus("bst_true_template_corpus.txt");
        LoadEntireCorpus("bst_true_template_corpus_fr.txt");

        module.SubmitText("Un chien peut aboyer.");
        Assert.True(module.LastTemplate?.HasAncestor("Assertion") == true,
            module.LastStatus);
        Assert.Same(uks.Labeled("dog"), module.LastRelationship?.From);
        Assert.Same(uks.Labeled("can"), module.LastRelationship?.LinkType);
        Assert.Same(uks.Labeled("bark"), module.LastRelationship?.To);

        AssertFrenchAnswer(
            module.SubmitText("Quel animal est le chien ?"),
            "chien", "est", "animal");
        AssertFrenchAnswer(
            module.SubmitText("Que possède le chien ?"),
            "chien", "a", "queue");
        AssertFrenchAnswer(
            module.SubmitText("Que peut faire le chien ?"),
            "chien", "peut", "aboyer");
        AssertFrenchAnswer(
            module.SubmitText("Que peuvent faire les chiens ?"),
            "chiens", "peuvent", "aboyer");

        void LoadEntireCorpus(string fileName)
        {
            string path = Path.Combine(wordFiles, fileName);
            int phraseCount = File.ReadLines(path)
                .Count(line => !string.IsNullOrWhiteSpace(line));
            Assert.Equal(phraseCount,
                module.LoadTextFromFile(path, phraseCount + 1));
        }

        void AssertFrenchAnswer(string answer, params string[] expectedWords)
        {
            Assert.False(string.IsNullOrWhiteSpace(answer), module.LastStatus);
            Assert.True(module.LastTemplate?.HasAncestor("Query") == true,
                module.LastStatus);
            foreach (string expectedWord in expectedWords)
                Assert.Contains(expectedWord, answer,
                    StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dog", answer,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bark", answer,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void LoadingAFileAlsoLearnsItsActionTemplates()
    {
        UKS.UKS uks = CreateTextUKS();
        string corpusPath = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(corpusPath, new[]
            {
                "A dog is an animal.\t[dog->SET.is-a->animal]",
                "A cat is an animal.\t[cat->SET.is-a->animal]",
            });
            var module = new ModuleText { theUKS = uks };

            int loadedPhrases = module.LoadTextFromFile(corpusPath, 10);

            Assert.Equal(2, loadedPhrases);
            Assert.Contains(uks.Labeled("Assertion").Children, template =>
                template.LinksTo.Any(link =>
                    link.LinkType?.Label == "means" &&
                    link.To is Link action &&
                    action.LinkType?.Label == "SET.is-a"));
        }
        finally
        {
            File.Delete(corpusPath);
        }
    }

    [Fact]
    public void FileInputBuildsSharedTemplatesInsteadOfOneIncrementalTemplatePerLine()
    {
        UKS.UKS uks = CreateTextUKS();
        string corpusPath = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(corpusPath,
                Enumerable.Range(0, 40).Select(index => $"The subject{index} is value{index}."));
            var module = new ModuleText { theUKS = uks };

            int loadedPhrases = module.LoadTextFromFile(corpusPath, 40);

            Assert.Equal(40, loadedPhrases);
            Assert.InRange(uks.Labeled("Assertion").Children.Count, 1, 3);
            Assert.NotEmpty(uks.Labeled("LearnedClass").Children);
            Assert.Contains(uks.Labeled("Assertion").Children, template =>
                uks.GetSequenceViews(template).Any(sequence =>
                    sequence.Elements.Count(element => element.HasAncestor("Wildcard")) == 2));
        }
        finally
        {
            File.Delete(corpusPath);
        }
    }

    [Fact]
    public void TrainingInputConsolidatesWithoutUsingTheFileLoader()
    {
        UKS.UKS uks = CreateTextUKS();
        var module = new ModuleText { theUKS = uks };

        for (int index = 0; index < 40; index++)
            module.SubmitText($"The subject{index} is value{index}.", answerQueries: false);

        Assert.NotEmpty(uks.Labeled("LearnedClass").Children);
        Assert.NotEmpty(uks.Labeled("Assertion").Children);
    }

    [Fact]
    public void TestExemplarsTeachAQueryTemplateWithoutAssertingTheTest()
    {
        UKS.UKS uks = CreateTextUKS();
        ModuleText.AddActionExemplar(
            "A bird is an animal", "[bird->SET.is-a->animal]");
        ModuleText.AddActionExemplar(
            "What is a dog", "[dog->TEST.is-a->??]");
        ModuleText.AddActionExemplar(
            "What is a cat", "[cat->TEST.is-a->??]");

        Assert.True(ModuleText.LearnActionsFromExemplars() > 0);
        var module = new ModuleText { theUKS = uks };

        string answer = module.SubmitText("What is a bird");

        Assert.Contains("animal", answer, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(module.LastTemplate);
        Assert.True(module.LastTemplate.HasAncestor("Query"));
        Assert.Equal(new[] { "Assertion", "Query" },
            uks.Labeled("LearnedTemplate").Children
                .Select(child => child.Label)
                .OrderBy(label => label));
        Assert.Null(uks.Labeled("LearnedQuestionTemplate"));
        Assert.Null(uks.Labeled(".is-a"));
        Assert.DoesNotContain(uks.AtomicThoughts,
            thought => string.IsNullOrEmpty(thought.Label));
        Assert.Equal("is-a", module.LastRelationship.LinkType.Label);
        Assert.Null(uks.GetLink(
            uks.Labeled("bird"), uks.Labeled("TEST.is-a"), uks.Labeled("??")));

        string suppressedAnswer = module.SubmitText("What is a bird", answerQueries: false);
        Assert.Null(suppressedAnswer);
        Assert.Empty(module.LastAnswer);
        Assert.Equal("is-a", module.LastRelationship.LinkType.Label);

        module.SubmitText("A fish is an animal", answerQueries: false, providedAction: "[fish->SET.is-a->animal]");
        Assert.NotNull(uks.GetLink(uks.Labeled("fish"), uks.Labeled("is-a"), uks.Labeled("animal")));
    }

    [Fact]
    public void ActionExemplarAssignsTheRelationshipMeaningToItsConnectorPhrase()
    {
        UKS.UKS uks = CreateTextUKS();
        Thought means = uks.GetOrAddThought("means", "LinkType");
        Thought dog = uks.GetOrAddThought("class0", "Object");
        Thought fido = uks.GetOrAddThought("O1", dog);
        Thought rover = uks.GetOrAddThought("O2", dog);
        uks.AddStatement(uks.GetOrAddThought("w:fido", "Word"), means, fido);
        uks.AddStatement(uks.GetOrAddThought("w:rover", "Word"), means, rover);
        uks.AddStatement(uks.GetOrAddThought("w:dog", "Word"), means, dog);
        ModuleText.AddActionExemplar(
            "Fido is a dog", "[fido->SET.is-a->dog]");
        ModuleText.AddActionExemplar(
            "Rover is a dog", "[rover->SET.is-a->dog]");

        ModuleText.LearnActionsFromExemplars();

        Thought connector = Assert.Single(uks.Labeled("MeaningPhrase").Children);
        SequenceView words = Assert.Single(uks.GetSequenceViews(connector)
            .Where(view => view.LinkType?.Label == "hasWords"));
        Assert.Equal(new[] { "w:is", "w:a" },
            words.Elements.Select(word => word.Label));
        Assert.NotNull(uks.GetLink(connector, means, uks.Labeled("is-a")));
        Assert.Null(uks.GetLink(uks.Labeled("w:is"), means, uks.Labeled("is-a")));
        Assert.Null(uks.GetLink(uks.Labeled("w:a"), means, uks.Labeled("is-a")));
    }

    [Fact]
    public void ActionExemplarPrefersAnExistingSemanticEndpointOverAWeakWordMeaning()
    {
        UKS.UKS uks = CreateTextUKS();
        Thought means = uks.GetOrAddThought("means", "LinkType");
        Thought dog = uks.GetOrAddThought("class0", "Object");
        Thought rover = uks.GetOrAddThought("O2", dog);
        Thought bark = uks.GetOrAddThought("bark", "Object");
        uks.AddStatement(uks.GetOrAddThought("w:dog", "Word"), means, dog);
        Link misleadingMeaning = uks.AddStatement(
            uks.GetOrAddThought("w:bark", "Word"), means, rover);
        misleadingMeaning.Weight = 0.26f;

        Thought exemplar = ModuleText.AddActionExemplar(
            "A dog can bark", "[dog->SET.can->bark]");

        Link demonstrated = Assert.IsType<Link>(
            exemplar.GetTargetOfFirstLinkOfType("demonstrates"));
        Assert.Same(dog, demonstrated.From);
        Assert.Same(bark, demonstrated.To);
        Assert.NotNull(uks.GetLink(dog, uks.Labeled("can"), bark));
        Assert.Null(uks.GetLink(dog, uks.Labeled("can"), rover));
    }

    [Fact]
    public void FullEnglishCorpusKeepsTheQuerySubjectAtTheNounPosition()
    {
        var uks = new UKS.UKS(clear: true);
        MainWindow.theUKS = uks;
        uks.LoadUKSfromXMLFile(Path.Combine(
            FindRepositoryRoot(), "BrainSimulator", "UKSContent",
            "DemoText.xml"));
        string corpusPath = Path.Combine(
            FindRepositoryRoot(), "BrainSimulator", "WordFIles",
            "bst_true_template_corpus.txt");
        var module = new ModuleText { theUKS = uks };
        Thought means = uks.Labeled("means");
        Thought animal = uks.Labeled("animal");
        Thought class0 = uks.GetOrAddThought("class0", "Object");
        class0.AddParent(animal);
        uks.Labeled("w:dog").RemoveLinks(means);
        uks.Labeled("w:is").RemoveLinks(means);
        uks.AddStatement(uks.Labeled("w:dog"), means, class0);
        uks.AddStatement(uks.Labeled("w:is"), means, class0);
        Thought fido = uks.GetOrAddThought("O1", class0);
        Thought fidoWord = uks.Labeled("w:fido") ??
            uks.GetOrAddThought("w:fido", "Word");
        uks.AddStatement(fidoWord, means, fido);

        int loaded = module.LoadTextFromFile(corpusPath, 1000);

        Thought isA = uks.Labeled("is-a");
        Thought meansRelationship = uks.Labeled("means");
        Assert.Null(uks.GetLink(uks.Labeled("w:is"), meansRelationship, isA));
        Assert.Null(uks.GetLink(uks.Labeled("w:are"), meansRelationship, isA));
        Assert.Null(uks.GetLink(uks.Labeled("w:a"), meansRelationship, isA));
        Assert.Null(uks.GetLink(uks.Labeled("w:an"), meansRelationship, isA));
        AssertPhraseMeans(isA, "w:is", "w:a");
        AssertPhraseMeans(isA, "w:is", "w:an");
        AssertPhraseMeans(isA, "w:are");
        Thought dogsWord = uks.Labeled("w:dogs");
        Assert.True(ReferenceEquals(class0, ModuleText.GetBestMeaning(dogsWord)?.To),
            "w:dogs means: " + string.Join(", ", dogsWord.LinksTo
                .Where(link => link.LinkType == meansRelationship)
                .Select(link => $"{link.To?.Label}:{link.Weight:0.00}")));

        string dogAnswer = module.SubmitText("What is a dog?");

        Assert.Equal(922, loaded);
        Assert.Equal(72, module.LastLoadedActionExemplarCount);
        Assert.Equal(72, module.LastRetainedActionExemplarCount);
        Assert.Empty(module.LastMissingActionExemplars);
        Assert.Same(class0, module.LastRelationship?.From);
        Assert.Contains("dog is an animal", dogAnswer,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dog are", dogAnswer,
            StringComparison.OrdinalIgnoreCase);

        string fidoAnswer = module.SubmitText("What is Fido?");

        Assert.NotNull(module.LastTemplate);
        Assert.True(module.LastRelationship is not null,
            $"{module.LastStatus} Template: {module.LastTemplate?.Label}; Answer: {fidoAnswer}");
        Assert.True(ReferenceEquals(fido, module.LastRelationship.From),
            $"From: {module.LastRelationship.From?.Label ?? "null"}; " +
            "w:fido means: " + string.Join(", ", fidoWord.LinksTo
                .Where(link => link.LinkType == means)
                .Select(link => $"{link.To?.Label}:{link.Weight:0.00}")));
        Assert.Equal("Fido is a dog", fidoAnswer, ignoreCase: true);

        string hasAnswer = module.SubmitText("What does a dog have?");
        Assert.False(string.IsNullOrWhiteSpace(hasAnswer), module.LastStatus);
        Assert.Equal("Dog has a tail", hasAnswer, ignoreCase: true);

        string canAnswer = module.SubmitText("What can a dog do?");
        Assert.False(string.IsNullOrWhiteSpace(canAnswer), module.LastStatus);
        Assert.Equal("Dog can bark", canAnswer, ignoreCase: true);

        string pluralDogAnswer = module.SubmitText("What are dogs?");
        Assert.Contains("dogs are animals", pluralDogAnswer,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dogs are objects", pluralDogAnswer,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dogs is", pluralDogAnswer,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Dogs have tails",
            module.SubmitText("What do dogs have?"), ignoreCase: true);
        Assert.Equal("Dogs can bark",
            module.SubmitText("What can dogs do?"), ignoreCase: true);
        string singularCatAnswer = module.SubmitText("What is a cat?");
        Assert.Contains("cat is an animal", singularCatAnswer,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cat are", singularCatAnswer,
            StringComparison.OrdinalIgnoreCase);
        string pluralCatAnswer = module.SubmitText("What are cats?");
        Assert.Contains("cats are animals", pluralCatAnswer,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cats is", pluralCatAnswer,
            StringComparison.OrdinalIgnoreCase);

        string relationshipAnswer = module.SubmitText("How is mary related to John?");
        Assert.NotNull(module.LastTemplate);
        Assert.True(module.LastRelationship is not null,
            $"{module.LastStatus} Template: {module.LastTemplate?.Label}; Answer: {relationshipAnswer}");
        Assert.NotNull(uks.GetLink(uks.Labeled("Mary"), uks.Labeled("loves"), uks.Labeled("John")));
        Assert.Contains("loves", relationshipAnswer, StringComparison.OrdinalIgnoreCase);

        module.SubmitText("Carol loves Dave.");
        Assert.NotNull(uks.GetLink(uks.Labeled("Carol"), uks.Labeled("loves"), uks.Labeled("Dave")));

        module.SubmitText("An animal is an object.");
        Assert.Same(uks.Labeled("animal"), module.LastRelationship?.From);
        Assert.Same(uks.Labeled("is-a"), module.LastRelationship?.LinkType);
        Assert.Same(uks.Labeled("Object"), module.LastRelationship?.To);

        void AssertPhraseMeans(Thought meaning, params string[] expectedWords)
        {
            Thought phrase = uks.Labeled("MeaningPhrase").Children.FirstOrDefault(candidate =>
            {
                SequenceView sequence = uks.GetSequenceViews(candidate)
                    .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
                return sequence is not null && sequence.Elements
                    .Select(word => word.Label)
                    .SequenceEqual(expectedWords);
            });
            Assert.True(phrase is not null,
                "Meaning phrases: " + string.Join(" | ",
                    uks.Labeled("MeaningPhrase").Children.Select(candidate =>
                        string.Join(" ", uks.GetSequenceViews(candidate)
                            .First(view => view.LinkType?.Label == "hasWords")
                            .Elements.Select(word => word.Label)) + " -> " +
                        string.Join(",", candidate.LinksTo
                            .Where(link => link.LinkType == meansRelationship)
                            .Select(link => link.To?.Label)))));
            Assert.NotNull(uks.GetLink(phrase, meansRelationship, meaning));
        }
    }

    [Fact]
    public void DemoDogsCorpusLearnsTheProperNameQueryForm()
    {
        var uks = new UKS.UKS(clear: true);
        MainWindow.theUKS = uks;
        string repositoryRoot = FindRepositoryRoot();
        uks.LoadUKSfromXMLFile(Path.Combine(
            repositoryRoot, "BrainSimulator", "UKSContent", "DemoDogs.xml"));
        var module = new ModuleText { theUKS = uks };
        module.LoadTextFromFile(Path.Combine(
            repositoryRoot, "BrainSimulator", "WordFIles",
            "bst_true_template_corpus.txt"), 1000);
        Thought fido = ModuleText.GetBestMeaning(uks.Labeled("w:fido"))?.To;

        string answer = module.SubmitText("What is Fido?");

        Assert.False(string.IsNullOrWhiteSpace(answer), module.LastStatus);
        Assert.Same(fido, module.LastRelationship?.From);
        Assert.True(module.LastTemplate?.HasAncestor("Query") == true);

        Thought dog = uks.Labeled("class0");
        Thought rover = uks.Labeled("O2");
        Thought can = uks.Labeled("can");
        Assert.Null(uks.GetLink(dog, can, rover));
        string capabilityAnswer = module.SubmitText("What can a dog do?");
        Assert.Contains("dog can bark", capabilityAnswer,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rover", capabilityAnswer,
            StringComparison.OrdinalIgnoreCase);

        module.SubmitText("An animal is an object.");
        Assert.True(module.LastTemplate?.HasAncestor("Assertion") == true,
            module.LastStatus);
        Assert.Same(uks.Labeled("animal"), module.LastRelationship?.From);
        Assert.Same(uks.Labeled("is-a"), module.LastRelationship?.LinkType);
        Assert.Same(uks.Labeled("Object"), module.LastRelationship?.To);
    }

    [Fact]
    public void DemoDogsCorpusRetainsEveryAnnotatedExemplarSequence()
    {
        var uks = new UKS.UKS(clear: true);
        MainWindow.theUKS = uks;
        string repositoryRoot = FindRepositoryRoot();
        uks.LoadUKSfromXMLFile(Path.Combine(repositoryRoot, "BrainSimulator", "UKSContent", "DemoDogs.xml"));
        string corpusPath = Path.Combine(
            repositoryRoot, "BrainSimulator", "WordFIles", "bst_true_template_corpus.txt");
        var module = new ModuleText { theUKS = uks };

        module.LoadTextFromFile(corpusPath, 1000);

        List<string> expected = File.ReadLines(corpusPath)
            .Where(line => line.Contains('\t'))
            .Select(line => ModuleText.GetPhraseWords(line[..line.IndexOf('\t')]))
            .Select(words => string.Join(" ", words.Select(word => word.Label)))
            .ToList();
        Thought exemplarRoot = uks.Labeled("ActionExemplar");
        List<string> actual = uks.GetSequenceViews(exemplarRoot.Children)
            .Where(view => view.LinkType?.Label == "hasWords")
            .Select(view => string.Join(" ", view.Elements.Select(word => word.Label)))
            .ToList();
        List<string> missing = expected.Except(actual).ToList();

        Assert.Equal(72, exemplarRoot.Children.Count);
        Assert.Equal(72, actual.Count);
        Assert.True(missing.Count == 0, "Missing exemplar sequences: " + string.Join("; ", missing));
    }

    [Fact]
    public void AddPhrasePreservesAOneWordPhrase()
    {
        // A one-word utterance such as "Stop" is still a phrase and must be
        // available to later template and action learning.
        UKS.UKS uks = CreateTextUKS();

        string result = ModuleText.AddPhrase("stop");

        Thought phrase = Assert.Single(ModuleText.GetPhrasesOfKind("Statement"));
        SequenceView words = Assert.Single(uks.GetSequenceViews(phrase)
            .Where(view => view.LinkType?.Label == "hasWords"));
        Assert.False(result.StartsWith("Error:", StringComparison.Ordinal));
        Assert.Equal(new[] { "w:stop" },
            words.Elements.Select(element => element.Label));
    }

    [Fact]
    public void StatementsAndQuestionsAreStoredAsSeparateObservations()
    {
        UKS.UKS uks = CreateTextUKS();

        ModuleText.AddPhrase("dogs are animals");
        ModuleText.AddPhrase("what are dogs?");

        Thought statement = Assert.Single(ModuleText.GetPhrasesOfKind("Statement"));
        Thought question = Assert.Single(ModuleText.GetPhrasesOfKind("Question"));
        Assert.Contains(uks.Labeled("Statement"), statement.Parents);
        Assert.Contains(uks.Labeled("Question"), question.Parents);
        Assert.NotSame(statement, question);
    }

    [Fact]
    public void RepeatedPhraseInputReusesTheExistingPhraseThought()
    {
        // Repeating an observation should reinforce the existing phrase. It must
        // not create p1, p2, and so on which all point to the same sequence.
        UKS.UKS uks = CreateTextUKS();

        ModuleText.AddText("dogs are animals");
        Thought originalPhrase = Assert.Single(ModuleText.GetPhrasesOfKind("Statement"));
        Thought originalSequence = originalPhrase.GetTargetOfFirstLinkOfType("hasWords");
        float originalWeight = originalPhrase.Weight;

        ModuleText.AddText("dogs are animals");

        Thought repeatedPhrase = Assert.Single(ModuleText.GetPhrasesOfKind("Statement"));
        Assert.Same(originalPhrase, repeatedPhrase);
        Assert.Same(originalSequence,
            repeatedPhrase.GetTargetOfFirstLinkOfType("hasWords"));
        Assert.True(repeatedPhrase.Weight > originalWeight);
    }

    [Fact]
    public void PhraseStoragePrunesWeakPhrasesButPreservesMeaningfulOnes()
    {
        // Ordinary observations compete within bounded short-term phrase
        // memory. A phrase with an attached meaning is no longer disposable,
        // even when many newer phrases arrive.
        UKS.UKS uks = CreateTextUKS();
        ModuleText.AddPhrase("special phrase");
        Thought meaningfulPhrase = Assert.Single(ModuleText.GetPhrasesOfKind("Statement"));
        Thought means = uks.GetOrAddThought("means", "LinkType");
        Thought meaning = uks.GetOrAddThought("remembered meaning", "Thought");
        uks.AddStatement(meaningfulPhrase, means, meaning);

        for (int index = 0; index < 60; index++)
            ModuleText.AddPhrase($"ordinary phrase {index}");

        List<Thought> phrases = ModuleText.GetPhrasesOfKind("Statement");
        Assert.Contains(meaningfulPhrase, phrases);
        Assert.Equal(50, phrases.Count(phrase =>
            !phrase.LinksTo.Any(link => link.LinkType?.Label == "means") &&
            !phrase.LinksFrom.Any(link => link.LinkType?.Label == "means")));
        Assert.Equal(51, phrases.Count);
    }

    [Fact]
    public void ConsolidatedCorpusRecognizesANewPluralClassificationPhrase()
    {
        // After the complete learning pass, a manually entered phrase using
        // new words must still match the learned plural classification frame.
        UKS.UKS uks = CreateTextUKS();
        LoadCorpus(uks);
        Assert.True(ModuleText.ConsolidateLanguageLearning() > 0);
        Assert.Single(uks.Labeled("SpellingPattern").Children);

        var module = new ModuleText { theUKS = uks };
        module.SubmitText("pigs are animals");
        string result = module.LastStatus;

        Assert.True(module.LastTemplate?.HasAncestor("Assertion") == true, result);
        Link learnedAssertion = uks.GetLink(
            uks.Labeled("pig"), uks.Labeled("is-a"), uks.Labeled("animal"));
        string pigAttributes = string.Join(", ", uks.Labeled("pig")?.LinksTo
            .Select(link => $"{link.LinkType?.Label}->{link.To?.Label}") ??
            Enumerable.Empty<string>());
        Assert.True(learnedAssertion is not null,
            $"{result} Pig relationships: {pigAttributes}");
    }

    [Fact]
    public void IndependentThousandSentenceCorpusProducesPhraseTemplates()
    {
        // This corpus was generated independently of the original template
        // corpus and contains 1,000 unique simple declarative sentences.
        UKS.UKS uks = CreateTextUKS();
        int phraseCount = LoadCorpus(uks, "bst_simple_1000_corpus.txt");

        List<Thought> learnedTemplates = ModuleText.DiscoverPhraseTemplates();

        Assert.Equal(1000, phraseCount);
        Assert.NotEmpty(learnedTemplates);
        Assert.All(learnedTemplates, template =>
        {
            Assert.Empty(template.Children);
            Assert.True(template.LinksTo.Count(link => link.LinkType?.Label == "evidence") >= 44);
        });

        List<List<string>> patterns = learnedTemplates
            .Select(template => uks.GetSequenceViews(template).Single().Elements
                .Select(element => element.Label)
                .ToList())
            .ToList();
        Assert.Contains(patterns, pattern => pattern.Contains("is"));
        Assert.Contains(patterns, pattern => pattern.Contains("are"));
        Assert.Contains(patterns, pattern => pattern.Contains("has"));
        Assert.Contains(patterns, pattern => pattern.Contains("can"));

        foreach (Thought template in learnedTemplates.Take(20))
        {
            SequenceView description = uks.GetSequenceViews(template).Single();
            int evidenceCount = template.LinksTo.Count(link => link.LinkType?.Label == "evidence");
            output.WriteLine(
                $"{template.Label} ({evidenceCount} phrases): " +
                string.Join(' ', description.Elements.Select(element => element.Label)));
        }
    }

    [Fact]
    public void ConsolidationCanBeRerunWithoutDuplicatingClasses()
    {
        UKS.UKS uks = CreateTextUKS();
        for (int i = 0; i < 44; i++)
            AddPhrase(uks, "the", "subject" + i, "runs");
        AddPhrase(uks, "the", "fish", "swims");

        int firstResult = ModuleText.ConsolidateLanguageLearning();
        int learnedClassCount = uks.Labeled("LearnedClass").Children.Count;
        int learnedTemplateCount = uks.Labeled("Assertion").Children.Count;
        int thoughtCount = uks.AtomicThoughts.Count;

        int secondResult = ModuleText.ConsolidateLanguageLearning();

        Assert.True(firstResult > 0);
        Assert.Equal(firstResult, secondResult);
        Assert.Equal(learnedClassCount, uks.Labeled("LearnedClass").Children.Count);
        Assert.Equal(learnedTemplateCount, uks.Labeled("Assertion").Children.Count);
        Assert.Equal(thoughtCount, uks.AtomicThoughts.Count);
    }

    [Fact]
    public void ConsolidationCoalescesSimilarLearnedClassesAfterDiscovery()
    {
        // Class cleanup is a post-processing operation and also runs when there
        // are no new phrase templates to discover.
        UKS.UKS uks = CreateTextUKS();
        Thought root = uks.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought classA = uks.GetOrAddThought("classA", root);
        Thought classB = uks.GetOrAddThought("classB", root);
        foreach (string label in new[] { "a", "b", "c", "d", "e" })
            uks.GetOrAddThought(label, "Word").AddParent(classA);
        foreach (string label in new[] { "b", "c", "d", "e", "f" })
            uks.GetOrAddThought(label, "Word").AddParent(classB);

        ModuleText.ConsolidateLanguageLearning();

        Assert.Single(root.Children);
        Assert.Null(uks.Labeled("classB"));
    }

    [Fact]
    public void AddPhrasePreservesWordsForLaterBatchDiscovery()
    {
        // ModuleWord may decline synchronous spelling creation while its
        // attention stream is active. AddPhrase must still preserve usable word
        // values rather than storing empty p* nodes.
        UKS.UKS uks = CreateTextUKS();
        for (int i = 0; i < 44; i++)
        {
            string result = ModuleText.AddPhrase($"the subject{i} runs");
            Assert.False(result.StartsWith("Error:", StringComparison.Ordinal), result);
        }

        Assert.All(ModuleText.GetPhrasesOfKind("Statement"), phrase =>
            Assert.NotNull(phrase.GetTargetOfFirstLinkOfType("hasWords")));
        Assert.Null(uks.Labeled("LearnedClass"));
        Assert.Null(uks.Labeled("LearnedTemplate"));

        ModuleText.ConsolidateLanguageLearning();

        Assert.NotEmpty(uks.Labeled("LearnedClass").Children);
        Assert.NotEmpty(uks.Labeled("Assertion").Children);
    }

    [Fact]
    public void ManualTextCanUseLearnedTemplateToClassifyNewWordsWithoutChangingCorpusLoading()
    {
        // A manually entered phrase may use an established template as a frame
        // for previously unseen words. Corpus observations remain passive: they
        // are retained as evidence for later batch discovery but do not modify
        // learned classes while they are being loaded.
        UKS.UKS uks = CreateTextUKS();
        Thought learnedClassRoot = uks.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought subjectClass = uks.GetOrAddThought("subjectClass", learnedClassRoot);
        Thought descriptionClass = uks.GetOrAddThought("descriptionClass", learnedClassRoot);
        Thought learnedTemplateRoot = uks.GetOrAddThought("LearnedTemplate", "LanguageElement");
        Thought template = uks.GetOrAddThought("template0", learnedTemplateRoot);
        Thought subject = uks.CreateWildcard("??subjectClass", new List<Thought> { subjectClass });
        Thought description = uks.CreateWildcard("??descriptionClass", new List<Thought> { descriptionClass });
        uks.AddSequenceAndLink(template, "hasWords", new List<Thought>
        {
            uks.GetOrAddThought("w:the", "Word"), subject,
            uks.GetOrAddThought("w:are", "Word"), description
        });

        Thought enteredPhrase = uks.GetOrAddThought("manualPhrase", "Phrase");
        uks.AddSequenceAndLink(enteredPhrase, "hasWords", new List<Thought>
        {
            uks.Labeled("w:the"), uks.GetOrAddThought("w:pigs", "Word"),
            uks.Labeled("w:are"), uks.GetOrAddThought("w:happy", "Word")
        });
        Thought foundTemplate = ModuleText.ApplyExistingTemplatesToPhrase(enteredPhrase);

        Assert.Same(template, foundTemplate);
        Assert.Contains(subjectClass, uks.Labeled("w:pigs").Parents);
        Assert.Contains(descriptionClass, uks.Labeled("w:happy").Parents);
        Assert.Contains(template.LinksTo, link => link.LinkType?.Label == "evidence" &&
            link.To.GetTargetOfFirstLinkOfType("hasWords") is not null);

        string result = ModuleText.AddPhrase("the cows are calm");
        Assert.False(result.StartsWith("Error:", StringComparison.Ordinal), result);
        Assert.DoesNotContain(subjectClass, uks.Labeled("w:cows").Parents);
        Assert.DoesNotContain(descriptionClass, uks.Labeled("w:calm").Parents);
    }

    [Fact]
    public void ManualTemplateLearningCanAddAWordToASecondCompatibleSlotClass()
    {
        // "pigs are animals" first teaches that pigs occupy the unqualified
        // plural-subject slot. Later, "the pigs are smelly" must still match
        // the article-bearing template and add pigs to that template's subject
        // class. Existing membership in the first class is supporting evidence,
        // not a reason to reject the second syntax.
        UKS.UKS uks = CreateTextUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought bareSubjectClass = uks.GetOrAddThought("bareSubjectClass", classRoot);
        Thought classificationClass = uks.GetOrAddThought("classificationClass", classRoot);
        Thought articleSubjectClass = uks.GetOrAddThought("articleSubjectClass", classRoot);
        Thought attributeClass = uks.GetOrAddThought("attributeClass", classRoot);
        Thought templateRoot = uks.GetOrAddThought("LearnedTemplate", "LanguageElement");
        Thought bareTemplate = uks.GetOrAddThought("bareTemplate", templateRoot);
        Thought articleTemplate = uks.GetOrAddThought("articleTemplate", templateRoot);
        Thought are = uks.GetOrAddThought("w:are", "Word");
        Thought animals = uks.GetOrAddThought("w:animals", "Word");
        animals.AddParent(classificationClass);

        uks.AddSequenceAndLink(bareTemplate, "hasWords", new List<Thought>
        {
            uks.CreateWildcard("??bareSubjectClass", new List<Thought> { bareSubjectClass }),
            are,
            uks.CreateWildcard("??classificationClass", new List<Thought> { classificationClass })
        });
        uks.AddSequenceAndLink(articleTemplate, "hasWords", new List<Thought>
        {
            uks.GetOrAddThought("w:the", "Word"),
            uks.CreateWildcard("??articleSubjectClass", new List<Thought> { articleSubjectClass }),
            are,
            uks.CreateWildcard("??attributeClass", new List<Thought> { attributeClass })
        });

        Thought firstPhrase = uks.GetOrAddThought("firstManualPhrase", "Phrase");
        Thought pigs = uks.GetOrAddThought("w:pigs", "Word");
        uks.AddSequenceAndLink(firstPhrase, "hasWords", new List<Thought> { pigs, are, animals });
        Assert.Same(bareTemplate, ModuleText.ApplyExistingTemplatesToPhrase(firstPhrase));
        Assert.Contains(bareSubjectClass, pigs.Parents);

        Thought secondPhrase = uks.GetOrAddThought("secondManualPhrase", "Phrase");
        Thought smelly = uks.GetOrAddThought("w:smelly", "Word");
        uks.AddSequenceAndLink(secondPhrase, "hasWords", new List<Thought>
        {
            uks.Labeled("w:the"), pigs, are, smelly
        });
        Assert.Same(articleTemplate, ModuleText.ApplyExistingTemplatesToPhrase(secondPhrase));
        Assert.Contains(articleSubjectClass, pigs.Parents);
        Assert.Contains(attributeClass, smelly.Parents);
    }

    [Fact]
    public void LearnedTemplatesProduceBoundaryAndConnectorWordClasses()
    {
        // These are structural discoveries, not supplied English grammar
        // labels. Fixed words immediately before class slots should collect as
        // one class, while the first fixed word following a class slot should
        // collect as another. The shared internal/leading "a" observation
        // joins a/an/the into the same boundary population.
        UKS.UKS uks = CreateTextUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought templateRoot = uks.GetOrAddThought("LearnedTemplate", "LanguageElement");
        Thought subjectClass = uks.GetOrAddThought("subjectClass", classRoot);
        Thought predicateClass = uks.GetOrAddThought("predicateClass", classRoot);
        Thought objectClass = uks.GetOrAddThought("objectClass", classRoot);
        Thought subject = uks.CreateWildcard("??subjectClass", new List<Thought> { subjectClass });
        Thought predicate = uks.CreateWildcard("??predicateClass", new List<Thought> { predicateClass });
        Thought obj = uks.CreateWildcard("??objectClass", new List<Thought> { objectClass });
        Thought a = uks.GetOrAddThought("w:a", "Word");
        Thought an = uks.GetOrAddThought("w:an", "Word");
        Thought the = uks.GetOrAddThought("w:the", "Word");
        Thought isWord = uks.GetOrAddThought("w:is", "Word");
        Thought are = uks.GetOrAddThought("w:are", "Word");
        Thought has = uks.GetOrAddThought("w:has", "Word");
        Thought can = uks.GetOrAddThought("w:can", "Word");

        AddTemplate("singularClassification", a, subject, isWord, an, predicate);
        AddTemplate("definiteAttribute", the, subject, isWord, predicate);
        AddTemplate("pluralClassification", subject, are, predicate);
        AddTemplate("definiteCapability", the, subject, can, predicate);
        AddTemplate("singularPossession", a, subject, has, a, obj);

        List<Thought> tokenClasses = ModuleText.DiscoverTemplateTokenClasses();

        Thought boundaryClass = Assert.Single(tokenClasses.Where(candidate =>
            OrdinaryMembers(candidate).SetEquals(new[] { a, an, the })));
        Thought connectorClass = Assert.Single(tokenClasses.Where(candidate =>
            OrdinaryMembers(candidate).SetEquals(new[] { isWord, are, has, can })));
        Assert.Equal(4, boundaryClass.LinksTo.Count(link => link.LinkType?.Label == "evidence"));
        Assert.Equal(5, connectorClass.LinksTo.Count(link => link.LinkType?.Label == "evidence"));

        int classCount = classRoot.Children.Count;
        List<Thought> repeated = ModuleText.DiscoverTemplateTokenClasses();
        Assert.Equal(classCount, classRoot.Children.Count);
        Assert.Contains(boundaryClass, repeated);
        Assert.Contains(connectorClass, repeated);

        void AddTemplate(string label, params Thought[] elements)
        {
            Thought template = uks.GetOrAddThought(label, templateRoot);
            uks.AddSequenceAndLink(template, "hasWords", elements.ToList());
        }

        static HashSet<Thought> OrdinaryMembers(Thought learnedClass) =>
            learnedClass.Children
                .Where(member => !member.HasAncestor("Wildcard") && member is not SeqElement)
                .ToHashSet();
    }

    [Fact]
    public void LearnedWordClassesCanRevealARepeatedSpellingPattern()
    {
        // These classes are anonymous structural discoveries. Without being
        // told anything about English plurality, the spelling comparison
        // should notice that members of one class repeatedly correspond to
        // members of the other by adding S at the end. This first step records
        // the observation and its evidence but does not alter word meanings.
        UKS.UKS uks = CreateTextUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought firstClass = uks.GetOrAddThought("firstWordClass", classRoot);
        Thought secondClass = uks.GetOrAddThought("secondWordClass", classRoot);
        Thought thirdClass = uks.GetOrAddThought("thirdWordClass", classRoot);
        Thought fourthClass = uks.GetOrAddThought("fourthWordClass", classRoot);
        Thought unrelatedClass = uks.GetOrAddThought("unrelatedWordClass", classRoot);

        AddMember(firstClass, "dog");
        AddMember(firstClass, "cat");
        AddMember(firstClass, "bird");
        AddMember(firstClass, "animal");
        AddMember(secondClass, "dogs");
        AddMember(secondClass, "cats");
        AddMember(secondClass, "birds");
        AddMember(secondClass, "animals");
        AddMember(thirdClass, "tree");
        AddMember(thirdClass, "book");
        AddMember(thirdClass, "river");
        AddMember(thirdClass, "house");
        AddMember(fourthClass, "trees");
        AddMember(fourthClass, "books");
        AddMember(fourthClass, "rivers");
        AddMember(fourthClass, "houses");
        AddMember(unrelatedClass, "happy");
        AddMember(unrelatedClass, "small");
        AddMember(unrelatedClass, "hungry");
        AddMember(unrelatedClass, "large");

        List<Thought> patterns = ModuleText.DiscoverClassSpellingPatterns(
            minMatchedPairs: 3,
            minCoverage: 0.75f,
            minCommonLetters: 3);

        Thought pattern = Assert.Single(patterns);
        Assert.Equal("end", pattern.GetTargetOfFirstLinkOfType("position").Label);
        Assert.Equal("c:S", pattern.GetTargetOfFirstLinkOfType("adds").Label);

        List<Link> classPairs = pattern.LinksTo
            .Where(link => link.LinkType?.Label == "classEvidence")
            .Select(link => link.To)
            .OfType<Link>()
            .ToList();
        Assert.Equal(2, classPairs.Count);
        Assert.Contains(classPairs, pair =>
            pair.From == firstClass && pair.To == secondClass);
        Assert.Contains(classPairs, pair =>
            pair.From == thirdClass && pair.To == fourthClass);

        List<Link> evidencePairs = pattern.LinksTo
            .Where(link => link.LinkType?.Label == "evidence")
            .Select(link => link.To)
            .OfType<Link>()
            .ToList();
        Assert.Equal(8, evidencePairs.Count);
        Assert.Contains(evidencePairs, pair =>
            pair.From.Label == "w:dog" && pair.To.Label == "w:dogs");
        Assert.All(firstClass.Children.Concat(secondClass.Children)
            .Concat(thirdClass.Children).Concat(fourthClass.Children),
            word => Assert.Null(word.GetTargetOfFirstLinkOfType("means")));

        // At this next stage the observed pairs, rather than a generated
        // English plural rule, provide the safe basis for shared meanings.
        Thought provisionalPluralMeaning = uks.GetOrAddThought("dogs");
        uks.AddStatement(uks.Labeled("w:dogs"),
            uks.GetOrAddThought("means", "LinkType"), provisionalPluralMeaning);
        int normalizedMeanings = ModuleText.NormalizeMeaningsFromSpellingPatterns();

        Assert.Equal(8, normalizedMeanings);
        foreach ((string first, string second) in new[]
        {
            ("dog", "dogs"),
            ("cat", "cats"),
            ("bird", "birds"),
            ("animal", "animals"),
            ("tree", "trees"),
            ("book", "books"),
            ("river", "rivers"),
            ("house", "houses")
        })
        {
            Thought canonical = uks.Labeled("w:" + first)
                .GetTargetOfFirstLinkOfType("means");
            Assert.Equal(first, canonical.Label);
            Assert.Same(canonical, uks.Labeled("w:" + second)
                .GetTargetOfFirstLinkOfType("means"));
        }
        Assert.Equal(0, ModuleText.NormalizeMeaningsFromSpellingPatterns());

        // Re-running discovery reinforces the same graph objects rather than
        // manufacturing duplicate patterns or correspondence relationships.
        List<Thought> repeated = ModuleText.DiscoverClassSpellingPatterns(
            minMatchedPairs: 3,
            minCoverage: 0.75f,
            minCommonLetters: 3);
        Assert.Same(pattern, Assert.Single(repeated));
        Assert.Single(uks.Labeled("SpellingPattern").Children);
        Assert.Equal(2, pattern.LinksTo.Count(
            link => link.LinkType?.Label == "classEvidence"));
        Assert.Equal(8, pattern.LinksTo.Count(
            link => link.LinkType?.Label == "evidence"));

        Thought AddMember(Thought learnedClass, string spelling)
        {
            Thought word = uks.GetOrAddThought("w:" + spelling, "Word");
            List<Thought> letters = spelling.ToUpperInvariant()
                .Select(letter => uks.GetOrAddThought("c:" + letter, "letter"))
                .ToList();
            uks.AddSequenceAndLink(word, "spelled", letters);
            word.AddParent(learnedClass);
            return word;
        }
    }

    [Fact]
    public void IncidentalSpellingPairsDoNotCreateAClassPattern()
    {
        // One or two coincidental word pairs are not enough to characterize
        // two entire classes. A useful class-level pattern needs repeated,
        // representative evidence.
        UKS.UKS uks = CreateTextUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought firstClass = uks.GetOrAddThought("firstWordClass", classRoot);
        Thought secondClass = uks.GetOrAddThought("secondWordClass", classRoot);
        foreach (string word in new[] { "dog", "cat", "bird", "animal" })
            uks.GetOrAddThought("w:" + word, firstClass).AddParent(uks.Labeled("Word"));
        foreach (string word in new[] { "dogs", "table", "green", "quickly" })
            uks.GetOrAddThought("w:" + word, secondClass).AddParent(uks.Labeled("Word"));

        List<Thought> patterns = ModuleText.DiscoverClassSpellingPatterns(
            minMatchedPairs: 3,
            minCoverage: 0.5f,
            minCommonLetters: 3);

        Assert.Empty(patterns);
    }

    [Fact]
    public void ExistingDuplicateSpellingPatternsAreConsolidated()
    {
        // Earlier discovery stored one pattern per class pair. Reprocessing an
        // existing UKS must migrate those nodes into one operation with several
        // class-pair evidence relationships.
        UKS.UKS uks = CreateTextUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought patternRoot = uks.GetOrAddThought("SpellingPattern", "LanguageElement");
        Thought position = uks.GetOrAddThought("end");
        Thought addedLetter = uks.GetOrAddThought("c:S", "letter");
        for (int index = 0; index < 3; index++)
        {
            Thought sourceClass = uks.GetOrAddThought($"sourceClass{index}", classRoot);
            Thought targetClass = uks.GetOrAddThought($"targetClass{index}", classRoot);
            Thought pattern = uks.GetOrAddThought($"oldSpellingPattern{index}", patternRoot);
            uks.AddStatement(pattern,
                uks.GetOrAddThought("sourceClass", "LinkType"), sourceClass);
            uks.AddStatement(pattern,
                uks.GetOrAddThought("targetClass", "LinkType"), targetClass);
            uks.AddStatement(pattern,
                uks.GetOrAddThought("position", "LinkType"), position);
            uks.AddStatement(pattern,
                uks.GetOrAddThought("adds", "LinkType"), addedLetter);
        }

        int mergeCount = ModuleText.ConsolidateSpellingPatterns();

        Assert.Equal(2, mergeCount);
        Thought consolidated = Assert.Single(patternRoot.Children);
        Assert.Equal(3, consolidated.LinksTo.Count(
            link => link.LinkType?.Label == "classEvidence"));
        Assert.DoesNotContain(consolidated.LinksTo,
            link => link.LinkType?.Label is "sourceClass" or "targetClass");
    }

    [Fact]
    public void DuplicatePhraseContentsReinforceExistingTemplatesWithoutCreatingNewOnes()
    {
        // Replaying observations is reinforcement, not discovery of another
        // structure. Significance is therefore based on distinct sequences,
        // while every occurrence remains attached as evidence.
        UKS.UKS uks = CreateTextUKS();
        for (int i = 0; i < 44; i++)
            AddPhrase(uks, "the", "subject" + i, "runs");

        Assert.NotEmpty(ModuleText.DiscoverPhraseTemplates());
        int learnedClassCount = uks.Labeled("LearnedClass").Children.Count;
        int learnedTemplateCount = uks.Labeled("Assertion").Children.Count;

        for (int i = 0; i < 44; i++)
            AddPhrase(uks, "the", "subject" + i, "runs");
        ModuleText.DiscoverPhraseTemplates();

        Assert.Equal(learnedClassCount, uks.Labeled("LearnedClass").Children.Count);
        Assert.Equal(learnedTemplateCount, uks.Labeled("Assertion").Children.Count);
        Assert.All(uks.Labeled("Assertion").Children, template =>
            Assert.Equal(88, template.LinksTo.Count(link => link.LinkType?.Label == "evidence")));
    }

    private static void LoadCorpus(UKS.UKS uks)
    {
        LoadCorpus(uks, "bst_true_template_corpus.txt");
    }

    private static int LoadCorpus(UKS.UKS uks, string fileName)
    {
        string corpusPath = Path.Combine(
            FindRepositoryRoot(), "BrainSimulator", "WordFIles", fileName);
        int phraseCount = 0;
        foreach (string line in File.ReadLines(corpusPath))
        {
            if (string.IsNullOrWhiteSpace(line) ||
                line.Contains("what", StringComparison.OrdinalIgnoreCase))
                continue;

            string[] fields = line.Split('\t', 2);
            string phraseText = fields[0].Trim();
            string[] words = phraseText.TrimEnd('.', '!', '?')
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            AddPhrase(uks, words);
            if (fields.Length == 2 && !string.IsNullOrWhiteSpace(fields[1]))
                ModuleText.AddActionExemplar(phraseText, fields[1]);
            phraseCount++;
        }
        return phraseCount;
    }

    private static Thought AddPhrase(UKS.UKS uks, params string[] labels)
    {
        Thought phrase = uks.GetOrAddThought("testPhrase*", "Phrase");
        List<Thought> words = labels.Select(label => AddSpelledWord(uks, label)).ToList();
        uks.AddSequenceAndLink(phrase, "hasWords", words);
        return phrase;
    }

    private static Thought AddSpelledWord(UKS.UKS uks, string label)
    {
        Thought word = uks.GetOrAddThought(label, "Word");
        if (word.GetTargetOfFirstLinkOfType("spelled") is null)
        {
            List<Thought> letters = label.ToUpperInvariant()
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
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BrainSim Thought.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}

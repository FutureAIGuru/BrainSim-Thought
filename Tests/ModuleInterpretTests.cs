using BrainSimulator.Modules;
using Xunit;

namespace UKS.Tests;

public class ModuleInterpretTests
{
    [Fact]
    public void SentenceFacetSearchConsumesARecognizedPhraseAndPreservesTheWords()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        Thought sentence = module.GetSequenceFromText("the black dog is brown");
        SeqElement first = (SeqElement)sentence.GetTargetOfFirstLinkOfType("hasWords");
        Thought patterns = store.Labeled("languagePattern");
        Thought nounPhrase = store.Labeled("nounPhrase1");
        SeqElement dog = first.NXT.NXT;
        for (SeqElement element = first; element != dog.NXT; element = element.NXT)
        {
            element.AddLink("matchesTemplate", nounPhrase);
            element.AddLink("VLU", nounPhrase);
        }
        Thought options = store.Labeled("InterpretTemplateSearch");
        SeqElement attributePattern = (SeqElement)store.Labeled("copulaAttributeTemplate").GetTargetOfFirstLinkOfType("hasWords");

        var wordsOnly = store.FindSequencesByActivation(first, options, valueClass: store.Labeled("word"), matchClass: patterns);
        var withPhrases = store.FindSequencesByActivation(first, options, alternativeClass: patterns, matchClass: patterns);
        Assert.DoesNotContain(wordsOnly, match => ReferenceEquals(match.seqNode, attributePattern));
        Assert.Contains(withPhrases, match => ReferenceEquals(match.seqNode, attributePattern));
        Assert.Same(store.Labeled("w:the"), first.VLU);
        Assert.Same(store.Labeled("w:dog"), dog.VLU);
        Assert.Same(nounPhrase, store.GetElementValue(first, patterns));

        module.GetTemplates(sentence);
        Assert.Same(store.Labeled("w:the"), store.GetElementValue(first, store.Labeled("word")));
        Assert.Same(store.Labeled("copulaAttributeTemplate"), store.GetElementValue(first, patterns));
        Assert.Contains(module.GetMeanings(sentence), meaning => meaning is Link link &&
            link.From == store.Labeled("dog") && link.LinkType == store.Labeled("is.!") && link.To == store.Labeled("brown"));
    }

    [Theory]
    [InlineData("Rover is hungry.", "rover")]
    [InlineData("Fido is hungry.", "fido")]
    [InlineData("The dog is hungry.", "dog")]
    [InlineData("The black dog is hungry.", "dog")]
    public void NewlyRecognizedSubjectLetsTheLongerAttributeTemplateWin(string input, string subject)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();

        string output = module.Input(input, rememberUnknownWords: true);

        Assert.Same(store.Labeled("adjective"), Assert.Single(store.Labeled("w:hungry").Parents));
        Assert.Contains($"[{subject}→is.!→hungry]", output);
        Assert.NotNull(store.GetLink(store.Labeled(subject), store.Labeled("is"), store.Labeled("hungry")));
        Assert.DoesNotContain("verbPhrase1", output);
    }

    [Theory]
    [InlineData("fresh")]
    [InlineData("oldWildcardRoot")]
    [InlineData("genericWordType")]
    public void RoverIsADogLearnsANewSubjectAcrossExistingVocabularySetups(string setup)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        if (setup == "oldWildcardRoot")
        {
            Thought wildcard = store.Labeled("??");
            wildcard.RemoveParent("Property");
            wildcard.AddParent(store.Labeled("Unknown"));
        }
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        if (setup == "genericWordType")
        {
            Thought word = ModuleWord.RememberSpelling(store, "rover", out _);
            word.AddParent(store.Labeled("wordType"));
            word.RemoveParent("word");
        }

        string output = module.Input("Rover is a dog.", rememberUnknownWords: true);

        Assert.Contains("[rover→is-a.!→dog]", output);
        Assert.NotNull(store.GetLink(store.Labeled("rover"), store.Labeled("is-a"), store.Labeled("dog")));
        Assert.Same(store.Labeled("noun"), Assert.Single(store.Labeled("w:rover").Parents));
    }

    [Fact]
    public void InitializationCreatesTheNewPrefixOptionWhenOpeningOlderKnowledge()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        // Older saved networks contain the existing search definitions, but not this new option.
        store.Labeled("allowTrailingInput").Delete();
        var module = new ModuleInterpret { theUKS = store };

        module.Initialize();
        string output = module.Input("Fido is brown.");

        Assert.True(store.Labeled("InterpretTemplateSearch").HasProperty("allowTrailingInput"));
        Assert.Contains("[fido→is.!→brown]", output);
        Assert.Contains("Executed relationships", output);
    }

    [Fact]
    public void EqualLongestTemplatesKeepBothWordTypes()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        store.ProcessSingleLine("[alternativeNounPhrase->is-a->nounPhrase]");
        store.ProcessSingleLine("[alternativeNounPhrase->hasWords->^??.article ??.noun ??.noun]");
        Thought sentence = module.GetSequenceFromText("the hungry dog", rememberUnknownWords: true);

        module.GetTemplates(sentence);

        Thought word = store.Labeled("w:hungry");
        Assert.Contains(store.Labeled("adjective"), word.Parents);
        Assert.Contains(store.Labeled("noun"), word.Parents);
        Assert.DoesNotContain(store.Labeled("unknownWordType"), word.Parents);
        Assert.DoesNotContain(store.Labeled("word"), word.Parents);
    }

    [Theory]
    [InlineData("the hungry dog ate dinner", "adjective", "nounPhrase1", "nounPhrase2")]
    [InlineData("the hungry should be fed", "noun", "nounPhrase2", "nounPhrase1")]
    public void LongestMatchClassifiesHungryFromTheWinningPhrase(
        string input, string expectedClass, string winner, string shorter)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        Thought sentence = module.GetSequenceFromText(input, rememberUnknownWords: true);
        SeqElement first = (SeqElement)sentence.GetTargetOfFirstLinkOfType("hasWords");

        module.GetTemplates(sentence);

        Assert.Same(store.Labeled(expectedClass), Assert.Single(store.Labeled("w:hungry").Parents));
        Assert.Contains(first.LinksTo, link => link.LinkType == store.Labeled("matchesTemplate") &&
            link.To == store.Labeled(winner));
        Assert.DoesNotContain(first.LinksTo, link => link.LinkType == store.Labeled("matchesTemplate") &&
            link.To == store.Labeled(shorter));
    }

    [Fact]
    public void UnknownAdjectiveLearnsFromItsNounPhraseAndReplacesItsTemporaryParent()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        Thought sentence = module.GetSequenceFromText("the hungry dog", rememberUnknownWords: true);
        Thought word = store.Labeled("w:hungry");
        Assert.Same(store.Labeled("unknownWordType"), Assert.Single(word.Parents));

        module.GetTemplates(sentence);

        Assert.Same(store.Labeled("adjective"), Assert.Single(word.Parents));
        Assert.False(word.HasProperty("isWildcard"));
        Assert.Same(store.Labeled("hungry"), word.GetTargetOfFirstLinkOfType("means"));
        Assert.Contains("[dog→is.!→hungry]", module.Input("the hungry dog"));
    }

    [Fact]
    public void InitializationRemovesRedundantWordParentFromClassifiedVocabulary()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        Thought word = ModuleWord.RememberSpelling(store, "pig", out _);
        word.AddParent(store.Labeled("noun"));
        Assert.Contains(store.Labeled("word"), word.Parents);

        module.Initialize();

        Assert.Same(store.Labeled("noun"), Assert.Single(word.Parents));
        Thought sentence = module.GetSequenceFromText("pig is an animal");
        Assert.NotNull(sentence);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClassificationCanBeReassertedAfterDeletingTheConceptOrItsParentLink(bool deleteConcept)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        Thought oldFido = store.Labeled("fido");
        Thought word = store.Labeled("w:fido");
        if (deleteConcept)
        {
            oldFido.Delete();
            Assert.Null(word.GetTargetOfFirstLinkOfType("means"));
            Assert.True(word.HasAncestor("noun"));
        }
        else oldFido.RemoveParent("dog");

        string output = module.Input("Fido is a dog.", rememberUnknownWords: true);

        Thought fido = store.Labeled("fido");
        Assert.NotNull(fido);
        Assert.Same(fido, word.GetTargetOfFirstLinkOfType("means"));
        Assert.NotNull(store.GetLink(fido, store.Labeled("is-a"), store.Labeled("dog")));
        Assert.Contains("[fido→is-a.!→dog]", output);
        Assert.Contains("Executed relationships", output);
        if (!deleteConcept) Assert.Same(oldFido, fido);
        Assert.Single(word.LinksTo.Where(link => link.LinkType == store.Labeled("means")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InitializationLoadsThePhraseAndSentenceTemplates(bool useUksCallback)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        if (useUksCallback) module.UKSInitializedNotification();
        else module.Initialize();

        Assert.Null(store.Labeled("interpret:classify"));
        Thought fixedPhrase = store.Labeled("p:is_a");
        Assert.True(fixedPhrase.HasAncestor("phrase"));
        Assert.False(fixedPhrase.HasAncestor("template"));
        Assert.True(store.Labeled("nounPhrase").HasAncestor("template"));
        Assert.False(store.Labeled("nounPhrase").HasAncestor("phrase"));
        Assert.True(store.Labeled("template").HasAncestor("LanguageElement"));
        Assert.True(store.Labeled("is.!").HasProperty("setRelationship"));
        Assert.Null(store.GetLink(store.Labeled("??.noun"), store.Labeled("is"), store.Labeled("??.adjective")));
        Assert.Same(store.Labeled("chases.ongoing.!"), store.Labeled("w:chasing").GetTargetOfFirstLinkOfType("means"));
        Assert.Equal(3, store.Labeled("nounPhrase").Children.Count(child => !child.HasProperty("isWildcard")));
        Assert.Single(store.Labeled("verbPhrase").Children.Where(child => !child.HasProperty("isWildcard")));
        foreach (string name in new[] { "nounPhrase1", "nounPhrase2", "nounPhrase3", "verbPhrase1",
            "sentenceTemplate", "classificationTemplate", "copulaClassificationTemplate", "copulaAttributeTemplate" })
        {
            Thought template = store.Labeled(name);
            Assert.NotEmpty(template.LinksTo.Where(link => link.LinkType == store.Labeled("means")));
            SeqElement head = Assert.IsType<SeqElement>(template.GetTargetOfFirstLinkOfType("hasWords"));
            foreach (SeqElement element in store.EnumerateSequenceElements(head))
            {
                Assert.StartsWith("??.", element.VLU.Label);
                Assert.True(element.VLU.HasProperty("isWildcard"));
                Assert.DoesNotContain(":", element.VLU.Label);
            }
        }
    }

    [Theory]
    [InlineData("the black dog is chasing the cat", "nounPhrase2", 4)]
    [InlineData("the black dog is chasing the brown cat", "nounPhrase1", 3)]
    public void GetTemplatesCombinesThePhraseMatchesIntoASentence(string input, string objectPhrase, int templateCount)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        Thought sentence = module.GetSequenceFromText(input);
        SeqElement first = Assert.IsType<SeqElement>(sentence.GetTargetOfFirstLinkOfType("hasWords"));

        var templates = module.GetTemplates(sentence);
        Assert.Equal(templateCount, templates.Count);
        Thought sentenceTemplate = store.Labeled("sentenceTemplate");
        Assert.Contains(sentenceTemplate, templates);
        var meanings = module.GetMeanings(sentence);
        Assert.Contains(store.Labeled("dog"), meanings);
        Assert.Contains(store.Labeled("cat"), meanings);
        Assert.Contains(store.Labeled("chases.ongoing.!"), meanings);
        Assert.Contains(meanings, meaning => meaning is Link relationship &&
            relationship.From == store.Labeled("dog") &&
            relationship.LinkType == store.Labeled("is.!") &&
            relationship.To == store.Labeled("black"));
        Assert.Contains(meanings, meaning => meaning is Link relationship &&
            relationship.From == store.Labeled("dog") &&
            relationship.LinkType == store.Labeled("chases.ongoing.!") &&
            relationship.To == store.Labeled("cat"));
        if (objectPhrase == "nounPhrase1")
            Assert.Contains(meanings, meaning => meaning is Link relationship &&
                relationship.From == store.Labeled("cat") &&
                relationship.LinkType == store.Labeled("is.!") &&
                relationship.To == store.Labeled("brown"));
        Assert.DoesNotContain(meanings, meaning => meaning.HasProperty("isWildcard"));
        Assert.Null(store.GetLink(store.Labeled("dog"), store.Labeled("is"), store.Labeled("black")));
        int labelCount = ThoughtLabels.GetLabelCount();
        Assert.Equal(meanings.Count, module.GetMeanings(sentence).Count);
        Assert.Equal(labelCount, ThoughtLabels.GetLabelCount());
        SeqElement element = first;
        foreach (string name in new[]
        {
            "nounPhrase1", "nounPhrase1", "nounPhrase1",
            "verbPhrase1", "verbPhrase1"
        })
        {
            var attachments = element.LinksTo.Where(link => link.LinkType == store.Labeled("matchesTemplate")).ToList();
            Assert.Equal(2, attachments.Count);
            Assert.Contains(attachments, link => link.To == store.Labeled(name));
            Assert.Contains(attachments, link => link.To == sentenceTemplate);
            element = element.NXT;
        }
        for (; element is not null; element = element.NXT)
        {
            var attachments = element.LinksTo.Where(link => link.LinkType == store.Labeled("matchesTemplate")).ToList();
            Assert.Equal(2, attachments.Count);
            Assert.Contains(attachments, link => link.To == store.Labeled(objectPhrase));
            Assert.Contains(attachments, link => link.To == sentenceTemplate);
        }

        // A second call preserves the same attachments.
        module.GetTemplates(sentence);
        foreach (SeqElement position in store.EnumerateSequenceElements(first))
            Assert.Equal(2,
                position.LinksTo.Count(link => link.LinkType == store.Labeled("matchesTemplate")));

        string output = module.Input(input);
        Assert.Contains("Word attachments\nw:the    nounPhrase1, sentenceTemplate", output);
        Assert.Contains("w:chasing    verbPhrase1, sentenceTemplate", output);
        Assert.Contains($"w:cat    {objectPhrase}, sentenceTemplate", output);
        Assert.Contains("[dog→chases.ongoing.!→cat]", output);
        Assert.Contains("[dog→is.!→black]", output);
    }

    [Fact]
    public void GetSequenceFromTextRetainsSentenceHistoryAndRejectsUnknownWords()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();

        Assert.Null(module.GetSequenceFromText(" ... "));
        Assert.Throws<FormatException>(() => module.GetSequenceFromText("the unknown dog"));
        Assert.Null(store.Labeled("SentenceIn0"));

        Thought sentence = module.GetSequenceFromText("the black dog is chasing the cat");
        Assert.Contains(store.Labeled("Sentence"), sentence.Parents);
        Assert.True(sentence.HasAncestor("LanguageElement"));
        Assert.StartsWith("Sentence input: ^w:the w:black w:dog w:is w:chasing w:the w:cat", module.Input("the black dog is chasing the cat"));
        Assert.Equal(2, store.Labeled("Sentence").Children.Count);

        Thought remembered = module.GetSequenceFromText("quux", rememberUnknownWords: true);
        Thought word = store.Labeled("w:quux");
        Assert.IsType<SeqElement>(word.GetTargetOfFirstLinkOfType("spelled"));
        Assert.Same(word, Assert.IsType<SeqElement>(remembered.GetTargetOfFirstLinkOfType("hasWords")).VLU);
    }

    [Theory]
    [InlineData("the black dog is chasing the pig", false)]
    [InlineData("the black dog is chasing the black pig", false)]
    [InlineData("the black dog is chasing the pig", true)]
    public void LongestTemplateLearnsTheNounAndItsMeaning(string input, bool conceptExists)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        Thought existingConcept = conceptExists ? store.GetOrAddThought("pig") : null;
        Thought sentence = module.GetSequenceFromText(input, rememberUnknownWords: true);
        Thought pig = store.Labeled("w:pig");
        Assert.Same(store.Labeled("unknownWordType"), Assert.Single(pig.Parents));
        Assert.IsType<SeqElement>(pig.GetTargetOfFirstLinkOfType("spelled"));

        module.GetTemplates(sentence);
        Thought concept = pig.GetTargetOfFirstLinkOfType("means");
        Assert.Same(store.Labeled("pig"), concept);
        if (conceptExists) Assert.Same(existingConcept, concept);
        Assert.True(pig.HasAncestor("noun"));
        Assert.DoesNotContain(store.Labeled("word"), pig.Parents);
        Assert.DoesNotContain(store.Labeled("unknownWordType"), pig.Parents);
        Assert.Contains(store.Labeled("sentenceTemplate"), module.GetTemplates(sentence));
        Assert.Contains(module.GetMeanings(sentence), meaning => meaning is Link relationship &&
            relationship.From == store.Labeled("dog") && relationship.LinkType == store.Labeled("chases.ongoing.!") &&
            relationship.To == concept);
        module.GetTemplates(sentence);
        Assert.Single(pig.LinksTo.Where(link => link.LinkType == store.Labeled("means")));
        Assert.Contains("[pig→is-a.!→animal]", module.Input("pig is an animal."));
    }

    [Fact]
    public void LongestTemplateCanClassifyTwoUnknownPositions()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        Thought sentence = module.GetSequenceFromText("the globular cluster", rememberUnknownWords: true);

        module.GetTemplates(sentence);

        Assert.Same(store.Labeled("adjective"), Assert.Single(store.Labeled("w:globular").Parents));
        Assert.Same(store.Labeled("noun"), Assert.Single(store.Labeled("w:cluster").Parents));
    }
    [Fact]
    public void FixedIsAPhraseCombinesWithKnownNounsToProduceAClassificationDirective()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        Thought sentence = module.GetSequenceFromText("Fido is a dog.");
        SeqElement first = (SeqElement)sentence.GetTargetOfFirstLinkOfType("hasWords");

        var matches = module.GetTemplates(sentence);
        Assert.Contains(store.Labeled("p:is_a"), matches);
        Assert.Contains(store.Labeled("classificationTemplate"), matches);
        Assert.Contains(first.NXT.LinksTo, link => link.LinkType == store.Labeled("matchesTemplate") &&
            link.To == store.Labeled("p:is_a"));
        Assert.Contains(first.NXT.NXT.LinksTo, link => link.LinkType == store.Labeled("matchesTemplate") &&
            link.To == store.Labeled("p:is_a"));

        var meanings = module.GetMeanings(sentence);
        Assert.Contains(meanings, meaning => meaning is Link action &&
            action.From == store.Labeled("fido") && action.To == store.Labeled("dog") &&
            action.LinkType == store.Labeled("is-a.!") && action.LinkType.HasProperty("setRelationship"));
        Assert.Contains("[fido→is-a.!→dog]", module.Input("Fido is a dog."));
    }

    [Theory]
    [InlineData("pigs are animals", "copulaClassificationTemplate", "is-a.!", "animal")]
    [InlineData("pigs are brown", "copulaAttributeTemplate", "is.!", "brown")]
    [InlineData("pig is brown", "copulaAttributeTemplate", "is.!", "brown")]
    public void CopulaTemplateUsesTheTargetClassToChooseItsMeaning(
        string input, string expectedTemplate, string relationshipType, string target)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        module.Input("the dog is chasing the pig", rememberUnknownWords: true);

        Thought sentence = module.GetSequenceFromText(input);
        var templates = module.GetTemplates(sentence);
        Assert.Contains(store.Labeled(expectedTemplate), templates);
        Assert.DoesNotContain(store.Labeled(expectedTemplate == "copulaClassificationTemplate"
            ? "copulaAttributeTemplate" : "copulaClassificationTemplate"), templates);
        Assert.Contains(module.GetMeanings(sentence), meaning => meaning is Link action &&
            action.From == store.Labeled("pig") && action.LinkType == store.Labeled(relationshipType) &&
            action.To == store.Labeled(target) && action.LinkType.HasProperty("setRelationship"));
        Assert.Contains($"[pig→{relationshipType}→{target}]", module.Input(input));
    }

    [Theory]
    [InlineData("pigs are animals", false, "is-a", "animal")]
    [InlineData("pigs are animals", true, "is-a", "animal")]
    [InlineData("pigs are brown", false, "is", "brown")]
    public void FreshPluralInputLearnsTheSingularWordAndExecutesTheMeaning(
        string input, bool pluralAlreadyRemembered, string relationshipType, string target)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        if (pluralAlreadyRemembered) ModuleWord.RememberSpelling(store, "pigs", out _);

        string output = module.Input(input, rememberUnknownWords: true);
        Thought word = store.Labeled("w:pig");
        Assert.NotNull(word);
        Assert.True(word.HasAncestor("noun"));
        Assert.Same(store.Labeled("pig"), word.GetTargetOfFirstLinkOfType("means"));
        Assert.StartsWith("Sentence input: ^w:pig w:are", output);
        Assert.Contains($"[pig→{relationshipType}.!→{target}]", output);
        Link result = Assert.IsType<Link>(store.GetLink(store.Labeled("pig"),
            store.Labeled(relationshipType), store.Labeled(target)));
        Assert.Contains($"Executed relationships\n{result}", output);
        Assert.Null(store.Labeled("pigs"));

        module.Input(input, rememberUnknownWords: true);
        Assert.Same(result, store.GetLink(store.Labeled("pig"),
            store.Labeled(relationshipType), store.Labeled(target)));
        Assert.Single(word.LinksTo.Where(link => link.LinkType == store.Labeled("means")));
    }

    [Theory]
    [InlineData("Fido is a dog.", "fido", "is-a", "dog")]
    [InlineData("Fido is an animal.", "fido", "is-a", "animal")]
    [InlineData("Dogs are animals.", "dog", "is-a", "animal")]
    [InlineData("Cats are animals.", "cat", "is-a", "animal")]
    [InlineData("Fido is brown.", "fido", "is", "brown")]
    [InlineData("Fido is green.", "fido", "is", "green")]
    [InlineData("Fido is small.", "fido", "is", "small")]
    [InlineData("Dogs are black.", "dog", "is", "black")]
    [InlineData("Cats are large.", "cat", "is", "large")]
    [InlineData("The black dog is brown.", "dog", "is", "brown")]
    [InlineData("A small cat is an animal.", "cat", "is-a", "animal")]
    [InlineData("Pigs are animals.", "pig", "is-a", "animal")]
    [InlineData("Pigs are brown.", "pig", "is", "brown")]
    public void CurrentSentenceExamplesProduceStoredRelationships(
        string sentence, string source, string relationshipType, string target)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();

        string output = module.Input(sentence, rememberUnknownWords: true);
        Assert.Contains("Executed relationships", output);
        Assert.NotNull(store.GetLink(store.Labeled(source), store.Labeled(relationshipType), store.Labeled(target)));
    }

    [Theory]
    [InlineData("Dogs have tails.", "dog", "has", "tail")]
    [InlineData("Fido has fur.", "fido", "has", "fur")]
    [InlineData("Fido chases cats.", "fido", "chases", "cat")]
    [InlineData("Dogs chase animals.", "dog", "chases", "animal")]
    [InlineData("The dog is chasing the cat.", "dog", "chases.ongoing", "cat")]
    [InlineData("Fido is not black.", "fido", "is.not", "black")]
    [InlineData("Cats are not large.", "cat", "is.not", "large")]
    [InlineData("Dogs are not cats.", "dog", "is-a.not", "cat")]
    [InlineData("Fido is not a cat.", "fido", "is-a.not", "cat")]
    [InlineData("Dogs have 4 legs.", "dog", "has.4", "leg")]
    [InlineData("Fido has 3 legs.", "fido", "has.3", "leg")]
    [InlineData("Cats have 2 tails.", "cat", "has.2", "tail")]
    public void ExpandedTemplatesExecuteTheirIntendedRelationships(
        string sentence, string source, string type, string target)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();

        string output = module.Input(sentence);
        Assert.Contains("Executed relationships", output);
        Assert.NotNull(store.GetLink(store.Labeled(source), store.Labeled(type), store.Labeled(target)));
        Assert.Null(store.GetLink(store.Labeled("leg"), store.Labeled("is"), store.Labeled("3")));
    }

    [Theory]
    [InlineData("Is Fido brown?", "fido", "is", "brown")]
    [InlineData("Is Fido a dog?", "fido", "is-a", "dog")]
    [InlineData("Are cats animals?", "cat", "is-a", "animal")]
    [InlineData("What is brown?", "fido", "is", "brown")]
    public void QueryTemplatesReturnExistingRelationships(string sentence, string source, string type, string target)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        Thought input = module.GetSequenceFromText(sentence);
        module.GetTemplates(input);
        var meanings = module.GetMeanings(input);

        Assert.Contains(meanings, meaning => meaning is Link query && query.LinkType.HasProperty("isPossible"));
        var results = module.Execute(meanings);
        Assert.Contains(results, link => link.From == store.Labeled(source) &&
            link.LinkType == store.Labeled(type) && link.To == store.Labeled(target));
        Assert.Contains("Query results", module.Input(sentence));
    }

    [Fact]
    public void UnmatchedQueryDoesNotAssertItsProposition()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();

        string output = module.Input("Is Fido black?");
        Assert.Contains("Query results\nNo matching relationships.", output);
        Assert.Null(store.GetLink(store.Labeled("fido"), store.Labeled("is"), store.Labeled("black")));
    }

    [Fact]
    public void GreenUsesTheAttributeTemplateAndCanBeQueriedAfterAssertion()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();

        Assert.True(store.Labeled("w:green").HasAncestor("adjective"));
        Assert.Single(ModuleWord.FindWordsBySpelling(store, "green"));
        Assert.Contains("No matching relationships.", module.Input("Is Fido green?"));

        string output = module.Input("Fido is green.", rememberUnknownWords: true);
        Assert.Contains("[fido→is.!→green]", output);
        Link assertion = store.GetLink(store.Labeled("fido"), store.Labeled("is"), store.Labeled("green"));
        Assert.NotNull(assertion);
        Assert.Null(store.GetLink(store.Labeled("fido"), store.Labeled("is-a"), store.Labeled("green")));
        Assert.Contains("Query results\n" + assertion, module.Input("Is Fido green?"));
    }
}

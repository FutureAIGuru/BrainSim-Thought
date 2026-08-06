using UKS;
using Xunit;

namespace UKS.Tests;

public class UKSIncrementalSequenceBubbleTests
{
    [Fact]
    public void NewPhrasesCreateThenReinforceOneTemplateInTheUKS()
    {
        UKS uks = new(clear: true);
        uks.CreateInitialStructure();
        Thought phraseRoot = uks.GetOrAddThought("Phrase", "LanguageElement");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");

        Thought firstPhrase = AddPhrase(uks, phraseRoot, hasWords,
            "the", "cat", "is", "small");
        Thought firstResult = uks.IncrementalSequenceBubble(firstPhrase);

        // One observation is evidence, but it cannot yet reveal which parts
        // are stable and which parts should become variable classes.
        Assert.Null(firstResult);
        Assert.Null(uks.Labeled("LearnedTemplate"));

        Thought secondPhrase = AddPhrase(uks, phraseRoot, hasWords,
            "the", "dog", "is", "furry");
        Thought learnedTemplate = uks.IncrementalSequenceBubble(secondPhrase);

        Assert.NotNull(learnedTemplate);
        Thought templateRoot = uks.Labeled("LearnedTemplate");
        Thought classRoot = uks.Labeled("LearnedClass");
        Assert.Single(templateRoot.Children);
        Assert.Equal(2, classRoot.Children.Count);

        SequenceView templateSequence = Assert.Single(uks.GetSequenceViews(learnedTemplate));
        Assert.Equal(new[] { "w:the", "w:is" }, templateSequence.Elements
            .Where(element => !element.HasAncestor("Wildcard"))
            .Select(element => element.Label));
        Assert.Equal(2, templateSequence.Elements.Count(element =>
            element.HasAncestor("Wildcard")));
        Assert.Equal(2, learnedTemplate.LinksTo.Count(link =>
            link.LinkType?.Label == "evidence"));

        Thought thirdPhrase = AddPhrase(uks, phraseRoot, hasWords,
            "the", "pig", "is", "happy");
        Thought reinforcedTemplate = uks.IncrementalSequenceBubble(thirdPhrase);

        Assert.Same(learnedTemplate, reinforcedTemplate);
        Assert.Single(templateRoot.Children);
        Assert.Equal(3, learnedTemplate.LinksTo.Count(link =>
            link.LinkType?.Label == "evidence"));
        Assert.Contains(classRoot.Children, learnedClass =>
            learnedClass.Children.Any(member => member.Label == "w:pig"));
        Assert.Contains(classRoot.Children, learnedClass =>
            learnedClass.Children.Any(member => member.Label == "w:happy"));
    }

    [Fact]
    public void UnequalLengthPhrasesCreateAndReuseAVariableLengthTemplate()
    {
        // An article-bearing noun phrase and a proper name have different
        // lengths but still share the useful "is a dog" structure.
        UKS uks = new(clear: true);
        uks.CreateInitialStructure();
        Thought phraseRoot = uks.GetOrAddThought("Phrase", "LanguageElement");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");

        Thought articlePhrase = AddPhrase(uks, phraseRoot, hasWords,
            "a", "terrier", "is", "a", "dog");
        Assert.Null(uks.IncrementalSequenceBubble(articlePhrase));

        Thought namePhrase = AddPhrase(uks, phraseRoot, hasWords,
            "fido", "is", "a", "dog");
        Thought learnedTemplate = uks.IncrementalSequenceBubble(namePhrase);

        Assert.NotNull(learnedTemplate);
        SequenceView templateSequence = Assert.Single(uks.GetSequenceViews(learnedTemplate));
        Assert.Contains(templateSequence.Elements,
            element => element.HasProperty("is+Wildcard"));

        Thought anotherName = AddPhrase(uks, phraseRoot, hasWords,
            "rex", "is", "a", "dog");
        Thought reusedTemplate = uks.IncrementalSequenceBubble(anotherName);

        Assert.Same(learnedTemplate, reusedTemplate);
        Assert.Equal(3, learnedTemplate.LinksTo.Count(link =>
            link.LinkType?.Label == "evidence"));
    }

    private static Thought AddPhrase(
        UKS uks,
        Thought phraseRoot,
        Thought hasWords,
        params string[] words)
    {
        Thought phrase = uks.GetOrAddThought("p*", phraseRoot);
        List<Thought> wordThoughts = words
            .Select(word => uks.GetOrAddThought("w:" + word, "Word"))
            .ToList();
        uks.AddSequenceAndLink(phrase, hasWords, wordThoughts);
        Thought retVal = phrase;
        return retVal;
    }
}

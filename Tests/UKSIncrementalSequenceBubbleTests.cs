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

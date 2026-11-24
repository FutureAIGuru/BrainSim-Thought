using System.Collections.Generic;
using System.Linq;
using UKS;
using Xunit;

namespace UKS.Tests;

public class UKSSequenceBubbleTests
{
    private static UKS CreateUKS()
    {
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        return uks;
    }

    private static List<Thought> Thoughts(UKS uks, params string[] labels) =>
        labels.Select(label => uks.GetOrAddThought(label, "Thought")).ToList();

    private static Link Observation(
        UKS uks,
        Thought linkType,
        string ownerLabel,
        params string[] values)
    {
        Thought owner = uks.GetOrAddThought(ownerLabel, "Phrase");
        uks.AddSequenceAndLink(owner, linkType, Thoughts(uks, values));
        return Assert.Single(uks.GetSequenceLinks(owner));
    }

    private static SequenceGapCardinality SingleGap(CommonSequencePattern pattern) =>
        Assert.Single(pattern.Elements.Where(element => element.IsGap)).GapCardinality!.Value;

    [Fact]
    public void MatcherFindsFixedBackboneAndAnExactlyOneGap()
    {
        UKS uks = CreateUKS();
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Link first = Observation(uks, hasWords, "p1", "A", "X", "C");
        Link second = Observation(uks, hasWords, "p2", "A", "Y", "C");

        CommonSequencePattern pattern = uks.FindCommonSequence(new[] { first, second });

        Assert.Equal(2, pattern.FixedElementCount);
        Assert.Equal(SequenceGapCardinality.ExactlyOne, SingleGap(pattern));
        Assert.Equal(new[] { "A", "C" }, pattern.Elements
            .Where(element => !element.IsGap)
            .Select(element => element.Value!.Label));
    }

    [Fact]
    public void MatcherInfersOptionalGapFromZeroOrOneObservedElements()
    {
        UKS uks = CreateUKS();
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Link first = Observation(uks, hasWords, "p1", "A", "X", "C");
        Link second = Observation(uks, hasWords, "p2", "A", "C");

        CommonSequencePattern pattern = uks.FindCommonSequence(new[] { first, second });

        Assert.Equal(SequenceGapCardinality.ZeroOrOne, SingleGap(pattern));
    }

    [Fact]
    public void MatcherInfersStarGapFromZeroOrSeveralObservedElements()
    {
        UKS uks = CreateUKS();
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Link first = Observation(uks, hasWords, "p1", "A", "C");
        Link second = Observation(uks, hasWords, "p2", "A", "X", "Y", "C");

        CommonSequencePattern pattern = uks.FindCommonSequence(new[] { first, second });

        Assert.Equal(SequenceGapCardinality.ZeroOrMore, SingleGap(pattern));
    }

    [Fact]
    public void MatcherInfersPlusGapFromDifferentPositiveObservedLengths()
    {
        UKS uks = CreateUKS();
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Link first = Observation(uks, hasWords, "p1", "A", "X", "C");
        Link second = Observation(uks, hasWords, "p2", "A", "Y", "Z", "C");

        CommonSequencePattern pattern = uks.FindCommonSequence(new[] { first, second });

        Assert.Equal(SequenceGapCardinality.OneOrMore, SingleGap(pattern));
    }

    [Fact]
    public void MatcherHandlesLeadingAndTrailingDifferences()
    {
        UKS uks = CreateUKS();
        Thought spelled = uks.GetOrAddThought("spelled", "LinkType");
        Link first = Observation(uks, spelled, "w:first", "X", "A", "B", "M");
        Link second = Observation(uks, spelled, "w:second", "Y", "Z", "A", "B", "N");

        CommonSequencePattern pattern = uks.FindCommonSequence(new[] { first, second });
        List<SequenceGapCardinality> gaps = pattern.Elements
            .Where(element => element.IsGap)
            .Select(element => element.GapCardinality!.Value)
            .ToList();

        Assert.Equal(new[] { SequenceGapCardinality.OneOrMore, SequenceGapCardinality.ExactlyOne }, gaps);
        Assert.Equal(new[] { "A", "B" }, pattern.Elements
            .Where(element => !element.IsGap)
            .Select(element => element.Value!.Label));
    }

    [Fact]
    public void MatcherReturnsNullWhenSequencesHaveNoFixedCommonElement()
    {
        UKS uks = CreateUKS();
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Link first = Observation(uks, hasWords, "p1", "A", "B");
        Link second = Observation(uks, hasWords, "p2", "X", "Y");

        CommonSequencePattern pattern = uks.FindCommonSequence(new[] { first, second });

        Assert.Null(pattern);
    }

    [Fact]
    public void FindingACommonSequenceDoesNotChangeTheUKS()
    {
        UKS uks = CreateUKS();
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Link first = Observation(uks, hasWords, "p1", "A", "X", "C");
        Link second = Observation(uks, hasWords, "p2", "A", "Y", "C");
        int thoughtCount = uks.AtomicThoughts.Count;
        int wildcardCount = uks.AtomicThoughts.Count(thought => new[] { "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard" }.Any(thought.HasProperty));

        CommonSequencePattern pattern = uks.FindCommonSequence(new[] { first, second });

        Assert.NotNull(pattern);
        Assert.Equal(thoughtCount, uks.AtomicThoughts.Count);
        Assert.Equal(wildcardCount, uks.AtomicThoughts.Count(thought => new[] { "isWildcard", "isOptionalWildcard", "is*Wildcard", "is+Wildcard" }.Any(thought.HasProperty)));
    }

    [Fact]
    public void BubblerStoresACommonDescriptionOnTheClassAndKeepsObservations()
    {
        UKS uks = CreateUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "Thought");
        Thought phraseClass = uks.GetOrAddThought("class0", classRoot);
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Link first = Observation(uks, hasWords, "p1", "dogs", "bark");
        Link second = Observation(uks, hasWords, "p2", "cats", "bark");
        first.From.AddParent(phraseClass);
        second.From.AddParent(phraseClass);
        SeqElement firstSequence = (SeqElement)first.To;
        SeqElement secondSequence = (SeqElement)second.To;

        bool changed = uks.BubbleSharedSequences(phraseClass);

        Assert.True(changed);
        Link description = Assert.Single(uks.GetSequenceLinks(phraseClass));
        Assert.Same(hasWords, description.LinkType);
        Assert.Equal("bark", uks.FlattenSequence(description.To)[1].Label);
        Assert.True(uks.FlattenSequence(description.To)[0].HasAncestor("Property"));
        Assert.True(uks.FlattenSequence(description.To)[0].HasProperty("isWildcard"));
        Assert.Contains(first.From.LinksTo, link => link.To == firstSequence);
        Assert.Contains(second.From.LinksTo, link => link.To == secondSequence);
    }

    [Fact]
    public void BubbledDescriptionMatchesEverySupportingObservation()
    {
        UKS uks = CreateUKS();
        Thought phraseClass = uks.GetOrAddThought("class0", "LearnedClass");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Link first = Observation(uks, hasWords, "p1", "dogs", "can", "bark");
        Link second = Observation(uks, hasWords, "p2", "cats", "bark");
        first.From.AddParent(phraseClass);
        second.From.AddParent(phraseClass);
        uks.BubbleSharedSequences(phraseClass);
        List<Thought> description = uks.FlattenSequence(uks.GetSequenceLinks(phraseClass).Single().To).ToList();
        Thought options = uks.CreateSearchOptions(mustMatchFirst: true, mustMatchLast: true, allowWildcard: true);

        HashSet<SeqElement> matches = uks.FindSequencesByActivation(description, options)
            .Select(result => result.seqNode)
            .ToHashSet();

        Assert.Contains((SeqElement)first.To, matches);
        Assert.Contains((SeqElement)second.To, matches);
    }

    [Fact]
    public void BubblerRequiresEnoughChildrenWithTheRelationship()
    {
        UKS uks = CreateUKS();
        Thought phraseClass = uks.GetOrAddThought("class0", "LearnedClass");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Thought first = Observation(uks, hasWords, "p1", "dogs", "bark").From;
        Thought second = uks.GetOrAddThought("p2", "Phrase");
        first.AddParent(phraseClass);
        second.AddParent(phraseClass);

        bool changed = uks.BubbleSharedSequences(phraseClass, minFraction: 0.6f);

        Assert.False(changed);
        Assert.Empty(uks.GetSequenceLinks(phraseClass));
    }

    [Fact]
    public void RepeatingSequenceBubblingIsIdempotent()
    {
        UKS uks = CreateUKS();
        Thought phraseClass = uks.GetOrAddThought("class0", "LearnedClass");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Thought first = Observation(uks, hasWords, "p1", "dogs", "bark").From;
        Thought second = Observation(uks, hasWords, "p2", "cats", "bark").From;
        first.AddParent(phraseClass);
        second.AddParent(phraseClass);

        Assert.True(uks.BubbleSharedSequences(phraseClass));
        Thought initial = uks.GetSequenceLinks(phraseClass).Single().To;

        Assert.False(uks.BubbleSharedSequences(phraseClass));
        Assert.Same(initial, uks.GetSequenceLinks(phraseClass).Single().To);
    }

    [Fact]
    public void DiscoveryCreatesTemplateWithPhraseEvidenceAndWordFillerClass()
    {
        UKS uks = CreateUKS();
        Thought phraseRoot = uks.GetOrAddThought("Phrase", "Thought");
        Thought templateRoot = uks.GetOrAddThought("LearnedTemplate", "Thought");
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "Thought");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Link dog = Observation(uks, hasWords, "the-dog-runs", "the", "dog", "runs");
        Link cat = Observation(uks, hasWords, "the-cat-runs", "the", "cat", "runs");
        Link bird = Observation(uks, hasWords, "the-bird-runs", "the", "bird", "runs");
        Link fish = Observation(uks, hasWords, "the-fish-swims", "the", "fish", "swims");
        foreach (Link observation in new[] { dog, cat, bird, fish })
            observation.From.AddParent(phraseRoot);

        List<Thought> templates = uks.DiscoverSequenceTemplates(
            uks.GetSequenceLinks(phraseRoot.Children), templateRoot, classRoot,
            minMembers: 3, minFixedElements: 2);

        Thought runsTemplate = Assert.Single(templates.Where(template => template.LinksTo
            .Where(link => link.LinkType?.Label == "evidence")
            .Select(link => link.To)
            .ToHashSet()
            .SetEquals(new[] { dog.From, cat.From, bird.From })));
        Assert.Empty(runsTemplate.Children);
        Link description = Assert.Single(uks.GetSequenceLinks(runsTemplate));
        Assert.Equal("the", uks.FlattenSequence(description.To)[0].Label);
        Assert.Equal("runs", uks.FlattenSequence(description.To)[2].Label);
        Thought subjectWildcard = uks.FlattenSequence(description.To)[1];
        Assert.StartsWith("??class", subjectWildcard.Label);
        Thought subjectClass = subjectWildcard.Parents.Single(parent => parent.Label != "Property");
        Assert.Equal(new HashSet<Thought> { uks.Labeled("dog"), uks.Labeled("cat"), uks.Labeled("bird") },
            subjectClass.Children.Where(child => !child.HasProperty("isWildcard")).ToHashSet());
    }

    [Fact]
    public void DiscoveryCanOperateDirectlyOnWordSpellings()
    {
        UKS uks = CreateUKS();
        Thought wordRoot = uks.GetOrAddThought("Word", "Thought");
        Thought templateRoot = uks.GetOrAddThought("LearnedTemplate", "Thought");
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "Thought");
        Thought spelled = uks.GetOrAddThought("spelled", "LinkType");
        Link cats = Observation(uks, spelled, "CAT", "C", "A", "T");
        Link cots = Observation(uks, spelled, "COT", "C", "O", "T");
        Link cuts = Observation(uks, spelled, "CUT", "C", "U", "T");
        Link fish = Observation(uks, spelled, "FISH", "F", "I", "S", "H");
        foreach (Link observation in new[] { cats, cots, cuts, fish })
            observation.From.AddParent(wordRoot);

        List<Thought> templates = uks.DiscoverSequenceTemplates(
            uks.GetSequenceLinks(wordRoot.Children), templateRoot, classRoot,
            minMembers: 3, minFixedElements: 1);

        Thought cVowelT = Assert.Single(templates.Where(template => template.LinksTo
            .Where(link => link.LinkType?.Label == "evidence")
            .Select(link => link.To)
            .ToHashSet()
            .SetEquals(new[] { cats.From, cots.From, cuts.From })));
        Assert.Empty(cVowelT.Children);
        List<Thought> description = uks.FlattenSequence(uks.GetSequenceLinks(cVowelT).Single().To).ToList();
        Assert.Equal("C", description[0].Label);
        Assert.StartsWith("??class", description[1].Label);
        Assert.True(description[1].HasProperty("isWildcard"));
        Assert.Equal("T", description[2].Label);
    }

    [Fact]
    public void DiscoveryRejectsTemplatesWithAdjacentInferredWildcards()
    {
        // With adjacent wildcards forbidden, these observations share only the
        // two-word suffix.  Their two differing leading positions must not be
        // turned into a vague "??classA ??classB are small" template.
        UKS uks = CreateUKS();
        Thought phraseRoot = uks.GetOrAddThought("Phrase", "Thought");
        Thought templateRoot = uks.GetOrAddThought("LearnedTemplate", "Thought");
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "Thought");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Link first = Observation(uks, hasWords, "p1", "the", "cats", "are", "small");
        Link second = Observation(uks, hasWords, "p2", "some", "dogs", "are", "small");
        first.From.AddParent(phraseRoot);
        second.From.AddParent(phraseRoot);

        List<Thought> templates = uks.DiscoverSequenceTemplates(
            uks.GetSequenceLinks(phraseRoot.Children), templateRoot, classRoot,
            minMembers: 2, minFixedElements: 2);

        Assert.Empty(templates);
    }
}

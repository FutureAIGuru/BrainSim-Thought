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

    private static SequenceView Observation(
        UKS uks,
        Thought linkType,
        string ownerLabel,
        params string[] values)
    {
        Thought owner = uks.GetOrAddThought(ownerLabel, "Phrase");
        uks.AddSequenceAndLink(owner, linkType, Thoughts(uks, values));
        return Assert.Single(uks.GetSequenceViews(owner));
    }

    private static SequenceGapCardinality SingleGap(CommonSequencePattern pattern) =>
        Assert.Single(pattern.Elements.Where(element => element.IsGap)).GapCardinality!.Value;

    [Fact]
    public void MatcherFindsFixedBackboneAndAnExactlyOneGap()
    {
        UKS uks = CreateUKS();
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        SequenceView first = Observation(uks, hasWords, "p1", "A", "X", "C");
        SequenceView second = Observation(uks, hasWords, "p2", "A", "Y", "C");

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
        SequenceView first = Observation(uks, hasWords, "p1", "A", "X", "C");
        SequenceView second = Observation(uks, hasWords, "p2", "A", "C");

        CommonSequencePattern pattern = uks.FindCommonSequence(new[] { first, second });

        Assert.Equal(SequenceGapCardinality.ZeroOrOne, SingleGap(pattern));
    }

    [Fact]
    public void MatcherInfersStarGapFromZeroOrSeveralObservedElements()
    {
        UKS uks = CreateUKS();
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        SequenceView first = Observation(uks, hasWords, "p1", "A", "C");
        SequenceView second = Observation(uks, hasWords, "p2", "A", "X", "Y", "C");

        CommonSequencePattern pattern = uks.FindCommonSequence(new[] { first, second });

        Assert.Equal(SequenceGapCardinality.ZeroOrMore, SingleGap(pattern));
    }

    [Fact]
    public void MatcherInfersPlusGapFromDifferentPositiveObservedLengths()
    {
        UKS uks = CreateUKS();
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        SequenceView first = Observation(uks, hasWords, "p1", "A", "X", "C");
        SequenceView second = Observation(uks, hasWords, "p2", "A", "Y", "Z", "C");

        CommonSequencePattern pattern = uks.FindCommonSequence(new[] { first, second });

        Assert.Equal(SequenceGapCardinality.OneOrMore, SingleGap(pattern));
    }

    [Fact]
    public void MatcherHandlesLeadingAndTrailingDifferences()
    {
        UKS uks = CreateUKS();
        Thought spelled = uks.GetOrAddThought("spelled", "LinkType");
        SequenceView first = Observation(uks, spelled, "w:first", "X", "A", "B", "M");
        SequenceView second = Observation(uks, spelled, "w:second", "Y", "Z", "A", "B", "N");

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
        SequenceView first = Observation(uks, hasWords, "p1", "A", "B");
        SequenceView second = Observation(uks, hasWords, "p2", "X", "Y");

        CommonSequencePattern pattern = uks.FindCommonSequence(new[] { first, second });

        Assert.Null(pattern);
    }

    [Fact]
    public void FindingACommonSequenceDoesNotChangeTheUKS()
    {
        UKS uks = CreateUKS();
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        SequenceView first = Observation(uks, hasWords, "p1", "A", "X", "C");
        SequenceView second = Observation(uks, hasWords, "p2", "A", "Y", "C");
        int thoughtCount = uks.AtomicThoughts.Count;
        int wildcardCount = uks.Labeled("Wildcard").Children.Count;

        CommonSequencePattern pattern = uks.FindCommonSequence(new[] { first, second });

        Assert.NotNull(pattern);
        Assert.Equal(thoughtCount, uks.AtomicThoughts.Count);
        Assert.Equal(wildcardCount, uks.Labeled("Wildcard").Children.Count);
    }

    [Fact]
    public void BubblerStoresACommonDescriptionOnTheClassAndKeepsObservations()
    {
        UKS uks = CreateUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "Thought");
        Thought phraseClass = uks.GetOrAddThought("class0", classRoot);
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        SequenceView first = Observation(uks, hasWords, "p1", "dogs", "bark");
        SequenceView second = Observation(uks, hasWords, "p2", "cats", "bark");
        first.Owner.AddParent(phraseClass);
        second.Owner.AddParent(phraseClass);
        SeqElement firstSequence = first.Sequence;
        SeqElement secondSequence = second.Sequence;

        bool changed = uks.BubbleSharedSequences(phraseClass);

        Assert.True(changed);
        SequenceView description = Assert.Single(uks.GetSequenceViews(phraseClass));
        Assert.Same(hasWords, description.LinkType);
        Assert.Equal("bark", description.Elements[1].Label);
        Assert.True(description.Elements[0].HasAncestor("Wildcard"));
        Assert.True(description.Elements[0].HasProperty("isWildcard"));
        Assert.Contains(first.Owner.LinksTo, link => link.To == firstSequence);
        Assert.Contains(second.Owner.LinksTo, link => link.To == secondSequence);
    }

    [Fact]
    public void BubbledDescriptionMatchesEverySupportingObservation()
    {
        UKS uks = CreateUKS();
        Thought phraseClass = uks.GetOrAddThought("class0", "LearnedClass");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        SequenceView first = Observation(uks, hasWords, "p1", "dogs", "can", "bark");
        SequenceView second = Observation(uks, hasWords, "p2", "cats", "bark");
        first.Owner.AddParent(phraseClass);
        second.Owner.AddParent(phraseClass);
        uks.BubbleSharedSequences(phraseClass);
        List<Thought> description = uks.GetSequenceViews(phraseClass).Single().Elements.ToList();
        Thought options = uks.CreateSearchOptions(mustMatchFirst: true, mustMatchLast: true, allowWildcard: true);

        HashSet<SeqElement> matches = uks.FindSequencesByActivation(description, options)
            .Select(result => result.seqNode)
            .ToHashSet();

        Assert.Contains(first.Sequence, matches);
        Assert.Contains(second.Sequence, matches);
    }

    [Fact]
    public void BubblerRequiresEnoughChildrenWithTheRelationship()
    {
        UKS uks = CreateUKS();
        Thought phraseClass = uks.GetOrAddThought("class0", "LearnedClass");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Thought first = Observation(uks, hasWords, "p1", "dogs", "bark").Owner;
        Thought second = uks.GetOrAddThought("p2", "Phrase");
        first.AddParent(phraseClass);
        second.AddParent(phraseClass);

        bool changed = uks.BubbleSharedSequences(phraseClass, minFraction: 0.6f);

        Assert.False(changed);
        Assert.Empty(uks.GetSequenceViews(phraseClass));
    }

    [Fact]
    public void RepeatingSequenceBubblingIsIdempotent()
    {
        UKS uks = CreateUKS();
        Thought phraseClass = uks.GetOrAddThought("class0", "LearnedClass");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Thought first = Observation(uks, hasWords, "p1", "dogs", "bark").Owner;
        Thought second = Observation(uks, hasWords, "p2", "cats", "bark").Owner;
        first.AddParent(phraseClass);
        second.AddParent(phraseClass);

        Assert.True(uks.BubbleSharedSequences(phraseClass));
        SeqElement initial = uks.GetSequenceViews(phraseClass).Single().Sequence;

        Assert.False(uks.BubbleSharedSequences(phraseClass));
        Assert.Same(initial, uks.GetSequenceViews(phraseClass).Single().Sequence);
    }

    [Fact]
    public void DiscoveryCreatesTemplateWithPhraseEvidenceAndWordFillerClass()
    {
        UKS uks = CreateUKS();
        Thought phraseRoot = uks.GetOrAddThought("Phrase", "Thought");
        Thought templateRoot = uks.GetOrAddThought("LearnedTemplate", "Thought");
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "Thought");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        SequenceView dog = Observation(uks, hasWords, "the-dog-runs", "the", "dog", "runs");
        SequenceView cat = Observation(uks, hasWords, "the-cat-runs", "the", "cat", "runs");
        SequenceView bird = Observation(uks, hasWords, "the-bird-runs", "the", "bird", "runs");
        SequenceView fish = Observation(uks, hasWords, "the-fish-swims", "the", "fish", "swims");
        foreach (SequenceView observation in new[] { dog, cat, bird, fish })
            observation.Owner.AddParent(phraseRoot);

        List<Thought> templates = uks.DiscoverSequenceTemplates(
            uks.GetSequenceViews(phraseRoot.Children), templateRoot, classRoot,
            minMembers: 3, minFixedElements: 2);

        Thought runsTemplate = Assert.Single(templates.Where(template => template.LinksTo
            .Where(link => link.LinkType?.Label == "evidence")
            .Select(link => link.To)
            .ToHashSet()
            .SetEquals(new[] { dog.Owner, cat.Owner, bird.Owner })));
        Assert.Empty(runsTemplate.Children);
        SequenceView description = Assert.Single(uks.GetSequenceViews(runsTemplate));
        Assert.Equal("the", description.Elements[0].Label);
        Assert.Equal("runs", description.Elements[2].Label);
        Thought subjectWildcard = description.Elements[1];
        Assert.StartsWith("??class", subjectWildcard.Label);
        Thought subjectClass = subjectWildcard.Parents.Single(parent => parent.Label != "Wildcard");
        Assert.Equal(new HashSet<Thought> { uks.Labeled("dog"), uks.Labeled("cat"), uks.Labeled("bird") },
            subjectClass.Children.Where(child => !child.HasAncestor("Wildcard")).ToHashSet());
    }

    [Fact]
    public void DiscoveryCanOperateDirectlyOnWordSpellings()
    {
        UKS uks = CreateUKS();
        Thought wordRoot = uks.GetOrAddThought("Word", "Thought");
        Thought templateRoot = uks.GetOrAddThought("LearnedTemplate", "Thought");
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "Thought");
        Thought spelled = uks.GetOrAddThought("spelled", "LinkType");
        SequenceView cats = Observation(uks, spelled, "CAT", "C", "A", "T");
        SequenceView cots = Observation(uks, spelled, "COT", "C", "O", "T");
        SequenceView cuts = Observation(uks, spelled, "CUT", "C", "U", "T");
        SequenceView fish = Observation(uks, spelled, "FISH", "F", "I", "S", "H");
        foreach (SequenceView observation in new[] { cats, cots, cuts, fish })
            observation.Owner.AddParent(wordRoot);

        List<Thought> templates = uks.DiscoverSequenceTemplates(
            uks.GetSequenceViews(wordRoot.Children), templateRoot, classRoot,
            minMembers: 3, minFixedElements: 1);

        Thought cVowelT = Assert.Single(templates.Where(template => template.LinksTo
            .Where(link => link.LinkType?.Label == "evidence")
            .Select(link => link.To)
            .ToHashSet()
            .SetEquals(new[] { cats.Owner, cots.Owner, cuts.Owner })));
        Assert.Empty(cVowelT.Children);
        List<Thought> description = uks.GetSequenceViews(cVowelT).Single().Elements.ToList();
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
        SequenceView first = Observation(uks, hasWords, "p1", "the", "cats", "are", "small");
        SequenceView second = Observation(uks, hasWords, "p2", "some", "dogs", "are", "small");
        first.Owner.AddParent(phraseRoot);
        second.Owner.AddParent(phraseRoot);

        List<Thought> templates = uks.DiscoverSequenceTemplates(
            uks.GetSequenceViews(phraseRoot.Children), templateRoot, classRoot,
            minMembers: 2, minFixedElements: 2);

        Assert.Empty(templates);
    }
}

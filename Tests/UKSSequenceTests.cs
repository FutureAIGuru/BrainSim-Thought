using System.Collections.Generic;
using System.Linq;
using UKS;
using Xunit;

namespace UKS.Tests;

public class UKSSequenceTests
{
    [Fact]
    public void SequenceSearchSelectsNoteOrDurationFacetsAndFiltersTheLibrary()
    {
        var uks = CreateUKS();
        Thought notes = uks.GetOrAddThought("note", "Abstract");
        Thought durations = uks.GetOrAddThought("duration", "Abstract");
        durations.AddProperty("isSequenceMetadata");
        Thought c = uks.GetOrAddThought("C4", notes), d = uks.GetOrAddThought("D4", notes);
        Thought one = uks.GetOrAddThought("delay1", durations), two = uks.GetOrAddThought("delay2", durations);
        Thought three = uks.GetOrAddThought("delay3", durations);
        Thought tunes = uks.GetOrAddThought("tune", "Thought");
        SeqElement stored = uks.CreateFirstElement("tuneCells", c);
        SeqElement storedLast = uks.AddElement(stored, d);
        stored.AddLink("VLU", one);
        storedLast.AddLink("VLU", two);
        Thought sequenceType = uks.GetOrAddThought("hasSequence", "LinkType");
        uks.GetOrAddThought("rememberedTune", tunes).AddLink(sequenceType, stored);
        SeqElement unrelated = uks.CreateFirstElement("otherCells", c);
        uks.AddElement(unrelated, d);
        uks.GetOrAddThought("unrelatedWord", "word").AddLink(uks.GetOrAddThought("spelled", "LinkType"), unrelated);
        SeqElement input = uks.CreateFirstElement("inputCells", c);
        SeqElement inputLast = uks.AddElement(input, d);
        input.AddLink("VLU", one);
        inputLast.AddLink("VLU", three);
        Thought options = uks.CreateSearchOptions(mustMatchFirst: true, mustMatchLast: true);

        var noteMatches = uks.FindSequencesByActivation(input, options, valueClass: notes, matchClass: tunes);
        var durationMatches = uks.FindSequencesByActivation(input, options, valueClass: durations, matchClass: tunes);
        Assert.Contains(noteMatches, match => ReferenceEquals(match.seqNode, stored));
        Assert.DoesNotContain(noteMatches, match => ReferenceEquals(match.seqNode, unrelated));
        Assert.DoesNotContain(durationMatches, match => ReferenceEquals(match.seqNode, stored));
        Assert.Same(c, input.VLU);
        Assert.Same(one, uks.GetElementValue(input, durations));

        inputLast.RemoveLink(uks.Labeled("VLU"), three);
        inputLast.AddLink("VLU", two);
        var matchingDurations = uks.FindSequencesByActivation(input, options, valueClass: durations, matchClass: tunes);
        Assert.Contains(matchingDurations, match => ReferenceEquals(match.seqNode, stored));
        Thought wildcard = uks.CreateWildcard("??.duration", new List<Thought> { durations });
        options.AddProperty("allowWildcards");
        var wildcardMatches = uks.FindSequencesByActivation(new List<Thought> { one, wildcard }, options,
            valueClass: durations, matchClass: tunes);
        Assert.Contains(wildcardMatches, match => ReferenceEquals(match.seqNode, stored));
        SeqElement query = uks.CreateFirstElement("durationQuery", one);
        uks.AddElement(query, uks.Labeled("??"));
        var wildcardSequenceMatches = uks.FindSequencesByActivation(query, options, valueClass: durations, matchClass: tunes);
        Assert.Contains(wildcardSequenceMatches, match => ReferenceEquals(match.seqNode, stored));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrailingInputReturnsShorterCompletedPrefixes(bool sequenceInput)
    {
        var uks = CreateUKS();
        Thought a = uks.Labeled("A"), b = uks.Labeled("B"), c = uks.Labeled("C"), x = uks.Labeled("X");
        SeqElement one = uks.AddSequence("prefixOne", new List<Thought> { a }, allowCompression: false);
        SeqElement two = uks.AddSequence("prefixTwo", new List<Thought> { a, b }, allowCompression: false);
        SeqElement three = uks.AddSequence("prefixThree", new List<Thought> { a, b, c }, allowCompression: false);
        SeqElement mismatch = uks.AddSequence("prefixMismatch", new List<Thought> { a, b, uks.Labeled("D") }, allowCompression: false);
        SeqElement later = uks.AddSequence("laterPhrase", new List<Thought> { b, c }, allowCompression: false);
        var input = new List<Thought> { a, b, c, x };
        input.AddRange(Enumerable.Repeat(x, 100));
        Thought strict = uks.CreateSearchOptions(mustMatchFirst: true, mustMatchLast: true);
        Thought prefix = uks.CreateSearchOptions(mustMatchFirst: true, mustMatchLast: true, allowTrailingInput: true);
        SeqElement sentence = sequenceInput ? uks.AddSequence("longInput", input, allowCompression: false) : null;

        var exact = sequenceInput ? uks.FindSequencesByActivation(sentence, strict) : uks.FindSequencesByActivation(input, strict);
        Assert.DoesNotContain(exact, match => ReferenceEquals(match.seqNode, one) ||
            ReferenceEquals(match.seqNode, two) || ReferenceEquals(match.seqNode, three));
        var matches = sequenceInput ? uks.FindSequencesByActivation(sentence, prefix) : uks.FindSequencesByActivation(input, prefix);
        Assert.Contains(matches, match => ReferenceEquals(match.seqNode, one) && match.confidence == 1);
        Assert.Contains(matches, match => ReferenceEquals(match.seqNode, two) && match.confidence == 1);
        Assert.Contains(matches, match => ReferenceEquals(match.seqNode, three) && match.confidence == 1);
        Assert.DoesNotContain(matches, match => ReferenceEquals(match.seqNode, mismatch) || ReferenceEquals(match.seqNode, later));
        Assert.True(matches.FindIndex(match => ReferenceEquals(match.seqNode, three)) <
            matches.FindIndex(match => ReferenceEquals(match.seqNode, two)));
    }

    [Fact]
    public void TrailingInputDoesNotCompleteAnUnfinishedTemplate()
    {
        var uks = CreateUKS();
        var input = new List<Thought> { uks.Labeled("A"), uks.Labeled("B") };
        SeqElement template = uks.AddSequence("unfinished", new List<Thought> { input[0], input[1], uks.Labeled("C") }, allowCompression: false);
        Thought options = uks.CreateSearchOptions(mustMatchFirst: true, mustMatchLast: true, allowTrailingInput: true);

        Assert.DoesNotContain(uks.FindSequencesByActivation(input, options), match => ReferenceEquals(match.seqNode, template));
    }

    [Fact]
    public void TrailingInputWaitsForTheEndOfNestedCallers()
    {
        var uks = CreateUKS();
        Thought a = uks.Labeled("A"), b = uks.Labeled("B"), c = uks.Labeled("C"), x = uks.Labeled("X");
        SeqElement inner = uks.AddSequence("prefixInner", new List<Thought> { a, b }, allowCompression: false);
        SeqElement outer = uks.AddSequence("prefixOuter", new List<Thought> { inner, c }, allowCompression: false);
        Thought options = uks.CreateSearchOptions(mustMatchFirst: true, mustMatchLast: true,
            allowNestedSequences: true, allowTrailingInput: true);

        var incomplete = uks.FindSequencesByActivation(new List<Thought> { a, b, x }, options);
        Assert.Contains(incomplete, match => ReferenceEquals(match.seqNode, inner));
        Assert.DoesNotContain(incomplete, match => ReferenceEquals(match.seqNode, outer));
        var complete = uks.FindSequencesByActivation(new List<Thought> { a, b, c, x }, options);
        Assert.Contains(complete, match => ReferenceEquals(match.seqNode, outer));
    }

    [Fact]
    public void TrailingInputWorksWithStoredWordWildcards()
    {
        var uks = CreateUKS();
        Thought noun = uks.GetOrAddThought("noun", "wordType");
        Thought article = uks.GetOrAddThought("the", "word");
        Thought dog = uks.GetOrAddThought("dog", noun);
        Thought wildcard = uks.CreateWildcard("??.noun", new List<Thought> { noun });
        SeqElement template = uks.AddSequence("nounPrefix", new List<Thought> { article, wildcard }, allowCompression: false);
        Thought options = uks.CreateSearchOptions(mustMatchFirst: true, mustMatchLast: true,
            allowWildcard: true, allowTrailingInput: true);

        var matches = uks.FindSequencesByActivation(new List<Thought> { article, dog, uks.Labeled("X"), uks.Labeled("Y") }, options);

        Assert.Contains(matches, match => ReferenceEquals(match.seqNode, template) && match.confidence == 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnknownWordTypeProvisionallyMatchesOnlyWordClassWildcards(int unknownPosition)
    {
        var uks = CreateUKS();
        Thought word = uks.GetOrAddThought("word", "Thought");
        Thought wordType = uks.GetOrAddThought("wordType", word);
        Thought unknownType = uks.GetOrAddThought("unknownWordType", wordType);
        var classes = new List<Thought>
        {
            uks.GetOrAddThought("article", wordType),
            uks.GetOrAddThought("adjective", wordType),
            uks.GetOrAddThought("noun", wordType)
        };
        var values = classes.Select((type, index) => uks.GetOrAddThought("known" + index, type)).ToList();
        var wildcards = classes.Select(type => uks.CreateWildcard("??." + type.Label, new List<Thought> { type })).ToList();
        SeqElement template = uks.AddSequence("unknownWordPattern", wildcards, allowCompression: false);
        Thought unknown = uks.GetOrAddThought("unclassifiedWord", unknownType);
        values[unknownPosition] = unknown;

        Assert.Contains(uks.FindSequencesByActivation(values, uks.Labeled("TemplateSequenceSearch")),
            match => ReferenceEquals(match.seqNode, template));
        Assert.DoesNotContain(uks.FindSequencesByActivation(values, uks.Labeled("ExactSequenceSearch")),
            match => ReferenceEquals(match.seqNode, template));
        Assert.False(unknown.HasProperty("isWildcard"));
        Assert.Same(unknownType, Assert.Single(unknown.Parents));

        // Uncertainty about grammar does not mean an arbitrary literal or non-word class.
        var literalValues = values.ToList();
        literalValues[unknownPosition] = uks.GetOrAddThought("differentLiteral");
        SeqElement literal = uks.AddSequence("literalPattern", literalValues, allowCompression: false);
        var unrelatedValues = wildcards.ToList();
        unrelatedValues[unknownPosition] = uks.CreateWildcard("??.unrelated", new List<Thought> { uks.GetOrAddThought("unrelated") });
        SeqElement unrelated = uks.AddSequence("unrelatedPattern", unrelatedValues, allowCompression: false);
        var matches = uks.FindSequencesByActivation(values, uks.Labeled("TemplateSequenceSearch"));
        Assert.DoesNotContain(matches, match => ReferenceEquals(match.seqNode, literal) || ReferenceEquals(match.seqNode, unrelated));

        unknown.RemoveParent(unknownType);
        unknown.AddParent(classes[(unknownPosition + 1) % classes.Count]);
        Assert.DoesNotContain(uks.FindSequencesByActivation(values, uks.Labeled("TemplateSequenceSearch")),
            match => ReferenceEquals(match.seqNode, template));
    }

    [Fact]
    public void SequenceSearch_LastElementBoundsTheInputSpan()
    {
        var uks = CreateUKS();
        var stored = uks.AddSequence("bounded", new List<Thought> { uks.Labeled("A"), uks.Labeled("B") }, allowCompression: false);
        SeqElement input = new() { Label = "input*", VLU = uks.Labeled("A") };
        SeqElement last = uks.AddElement(input, uks.Labeled("B"));
        uks.AddElement(last, uks.Labeled("C"));
        var bounded = uks.FindSequencesByActivation(input, last: last);
        Assert.Contains(bounded, match => ReferenceEquals(match.seqNode, stored));
        Assert.DoesNotContain(uks.FindSequencesByActivation(input), match => ReferenceEquals(match.seqNode, stored));
    }

    [Fact]
    public void StreamingQueue_SearchReturnsEachPositionWithoutFirstPointers()
    {
        var uks = CreateUKS();
        Thought a = uks.Labeled("A");
        SeqElement first = new() { Label = "stream*", VLU = a };
        SeqElement second = uks.AddElement(first, a);
        Assert.Null(first.FRST);
        Assert.Null(second.FRST);
        var matches = uks.FindSequencesByActivation(new List<Thought> { a }, uks.CreateSearchOptions());
        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, match => ReferenceEquals(match.seqNode, first));
        Assert.Contains(matches, match => ReferenceEquals(match.seqNode, second));
    }

    [Fact]
    public void StreamingQueue_PopDeletesOnlyTheHeadAndPreservesSequenceValues()
    {
        var uks = CreateUKS();
        SeqElement value = uks.CreateFirstElement("value", uks.Labeled("A"));
        SeqElement first = new() { Label = "stream*", VLU = value };
        first.AddLink(uks.GetOrAddThought("usedBy", "LinkType"), value);
        SeqElement next = uks.AddElement(first, uks.Labeled("B"));
        Assert.Same(next, uks.RemoveFirstSequenceElement(first));
        Assert.Null(uks.Labeled(first.Label));
        Assert.Same(next, uks.Labeled(next.Label));
        Assert.Same(value, uks.Labeled(value.Label));
        Assert.Same(uks.Labeled("A"), value.VLU);
        Assert.Null(uks.RemoveFirstSequenceElement(next));
        Assert.Null(uks.Labeled(next.Label));
        Assert.Throws<System.ArgumentException>(() => uks.RemoveFirstSequenceElement(value));
    }

    [Fact]
    public void ActivationSearch_CanReturnRootsOrIndividualMatchPositions()
    {
        var uks = CreateUKS();
        Thought a = uks.Labeled("A"), b = uks.Labeled("B");
        SeqElement sequence = uks.AddSequence("repeated", new List<Thought> { a, b, a, b }, allowCompression: false);
        Thought options = uks.CreateSearchOptions();
        var roots = uks.FindSequencesByActivation(new List<Thought> { a, b }, options);
        Assert.Same(sequence, Assert.Single(roots).seqNode);
        var positions = uks.FindSequencesByActivation(new List<Thought> { a, b }, options, getFirstElements: false);
        Assert.Equal(2, positions.Count);
        Assert.Contains(positions, match => ReferenceEquals(match.seqNode, sequence));
        Assert.Contains(positions, match => ReferenceEquals(match.seqNode, sequence.NXT.NXT));
    }

    [Fact]
    public void ActivationSearch_IndividualPositionsRequireLiteralFlatSearch()
    {
        var uks = CreateUKS();
        Thought options = uks.CreateSearchOptions();
        options.AddProperty("allowNestedSequences");
        Assert.Throws<System.ArgumentException>(() =>
            uks.FindSequencesByActivation(new List<Thought> { uks.Labeled("A") }, options, getFirstElements: false));
    }

    private UKS CreateUKS()
    {
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        uks.GetOrAddThought("letter");
        for (char c = 'A'; c <= 'Z'; c++)
        {
            uks.GetOrAddThought(c.ToString(), "Letter");
        }

        return uks;
    }

    private static List<Thought> GetTopLevelValues(UKS uks, SeqElement start)
    {
        var values = new List<Thought>();
        var seen = new HashSet<SeqElement>();
        var current = start;
        while (current is not null && seen.Add(current))
        {
            values.Add(uks.GetElementValue(current));
            current = current.NXT;
        }
        return values;
    }

    private static Thought CreatePartialSequenceSearchOptions(UKS uks)
    {
        var searchOptions = uks.GetOrAddThought("PartialSequenceSearch", "SearchOption");
        searchOptions.AddLink("hasProperty", "allowNestedSequences");
        searchOptions.AddLink("hasProperty", "allowPartialMatch");
        return searchOptions;
    }

    public static IEnumerable<object[]> WildcardCardinalityCases()
    {
        yield return new object[] { "isWildcard", "beginning", new[] { "A", "B", "C" }, true };
        yield return new object[] { "isWildcard", "beginning", new[] { "A", "X", "B", "C" }, false };
        yield return new object[] { "isWildcard", "middle", new[] { "A", "B", "C" }, true };
        yield return new object[] { "isWildcard", "middle", new[] { "A", "B", "X", "C" }, false };
        yield return new object[] { "isWildcard", "end", new[] { "A", "B", "C" }, true };
        yield return new object[] { "isWildcard", "end", new[] { "A", "B", "C", "D" }, false };

        yield return new object[] { "isOptionalWildcard", "beginning", new[] { "B", "C" }, true };
        yield return new object[] { "isOptionalWildcard", "beginning", new[] { "A", "B", "C" }, true };
        yield return new object[] { "isOptionalWildcard", "beginning", new[] { "A", "X", "B", "C" }, false };
        yield return new object[] { "isOptionalWildcard", "middle", new[] { "A", "C" }, true };
        yield return new object[] { "isOptionalWildcard", "middle", new[] { "A", "B", "C" }, true };
        yield return new object[] { "isOptionalWildcard", "middle", new[] { "A", "B", "X", "C" }, false };
        yield return new object[] { "isOptionalWildcard", "end", new[] { "A", "B" }, true };
        yield return new object[] { "isOptionalWildcard", "end", new[] { "A", "B", "C" }, true };
        yield return new object[] { "isOptionalWildcard", "end", new[] { "A", "B", "C", "D" }, false };

        yield return new object[] { "is*Wildcard", "beginning", new[] { "B", "C" }, true };
        yield return new object[] { "is*Wildcard", "beginning", new[] { "A", "B", "C" }, true };
        yield return new object[] { "is*Wildcard", "beginning", new[] { "A", "X", "B", "C" }, true };
        yield return new object[] { "is*Wildcard", "beginning", new[] { "A", "X", "B", "D" }, false };
        yield return new object[] { "is*Wildcard", "middle", new[] { "A", "C" }, true };
        yield return new object[] { "is*Wildcard", "middle", new[] { "A", "B", "C" }, true };
        yield return new object[] { "is*Wildcard", "middle", new[] { "A", "B", "X", "C" }, true };
        yield return new object[] { "is*Wildcard", "middle", new[] { "A", "B", "X", "D" }, false };
        yield return new object[] { "is*Wildcard", "end", new[] { "A", "B" }, true };
        yield return new object[] { "is*Wildcard", "end", new[] { "A", "B", "C" }, true };
        yield return new object[] { "is*Wildcard", "end", new[] { "A", "B", "C", "D" }, true };
        yield return new object[] { "is*Wildcard", "end", new[] { "A", "X", "C", "D" }, false };

        yield return new object[] { "is+Wildcard", "beginning", new[] { "A", "B", "C" }, true };
        yield return new object[] { "is+Wildcard", "beginning", new[] { "A", "X", "B", "C" }, true };
        yield return new object[] { "is+Wildcard", "beginning", new[] { "B", "C" }, false };
        yield return new object[] { "is+Wildcard", "middle", new[] { "A", "B", "C" }, true };
        yield return new object[] { "is+Wildcard", "middle", new[] { "A", "B", "X", "C" }, true };
        yield return new object[] { "is+Wildcard", "middle", new[] { "A", "C" }, false };
        yield return new object[] { "is+Wildcard", "end", new[] { "A", "B", "C" }, true };
        yield return new object[] { "is+Wildcard", "end", new[] { "A", "B", "C", "D" }, true };
        yield return new object[] { "is+Wildcard", "end", new[] { "A", "B" }, false };
    }

    [Theory]
    [MemberData(nameof(WildcardCardinalityCases))]
    public void FindSequencesByActivation_EnforcesWildcardCardinalityAtEveryPosition(
        string wildcardProperty,
        string position,
        string[] sequenceLabels,
        bool shouldMatch)
    {
        var uks = CreateUKS();
        Thought wildcard = uks.CreateWildcard(
            $"wildcard-{wildcardProperty}-{position}",
            new List<Thought> { uks.Labeled("Letter") },
            wildcardProperty);
        Thought a = uks.Labeled("A");
        Thought b = uks.Labeled("B");
        Thought c = uks.Labeled("C");
        List<Thought> pattern = position switch
        {
            "beginning" => new List<Thought> { wildcard, b, c },
            "middle" => new List<Thought> { a, wildcard, c },
            "end" => new List<Thought> { a, b, wildcard },
            _ => throw new System.ArgumentOutOfRangeException(nameof(position)),
        };
        SeqElement sequence = uks.AddSequenceAndLink(
            uks.GetOrAddThought("cardinality-case"),
            uks.GetOrAddThought("hasSequence", "LinkType"),
            sequenceLabels.Select(label => uks.Labeled(label)).ToList());

        var matches = uks.FindSequencesByActivation(pattern, uks.Labeled("TemplateSequenceSearch"));

        if (shouldMatch)
            Assert.Contains(matches, x => ReferenceEquals(x.seqNode, sequence) && x.confidence == 1.0f);
        else
            Assert.DoesNotContain(matches, x => ReferenceEquals(x.seqNode, sequence));
    }

    [Theory]
    [InlineData("isWildcard")]
    [InlineData("isOptionalWildcard")]
    [InlineData("is*Wildcard")]
    [InlineData("is+Wildcard")]
    public void CreateWildcard_UsesClassForIdentityAndPropertyForTraversal(string wildcardProperty)
    {
        var uks = CreateUKS();

        Thought wildcard = uks.CreateWildcard(
            "cardinality-" + wildcardProperty,
            new List<Thought> { uks.Labeled("Letter") },
            wildcardProperty);

        Assert.True(wildcard.HasAncestor("Property"));
        Assert.True(wildcard.HasProperty(wildcardProperty));
    }

    [Fact]
    public void LeadingZeroOrMoreWildcard_TriesLaterOccurrenceOfFollowingLiteral()
    {
        var uks = CreateUKS();
        Thought prefix = uks.CreateWildcard(
            "??*Letter",
            new List<Thought> { uks.Labeled("Letter") },
            "is*Wildcard");
        SeqElement attention = uks.AddSequenceAndLink(
            uks.GetOrAddThought("ATTENTION"),
            uks.GetOrAddThought("spelled", "LinkType"),
            new List<Thought> { "A", "T", "T", "E", "N", "T", "I", "O", "N" });

        var matches = uks.FindSequencesByActivation(
            new List<Thought> { prefix, "T", "I", "O", "N" },
            uks.Labeled("TemplateSequenceSearch"));

        Assert.Contains(matches, x => ReferenceEquals(x.seqNode, attention));
    }

    [Fact]
    public void VariableWildcard_RequiresEveryConsumedValueToMatchItsClass()
    {
        var uks = CreateUKS();
        Thought letters = uks.CreateWildcard(
            "??*letters-only",
            new List<Thought> { uks.Labeled("Letter") },
            "is*Wildcard");
        Thought nonLetter = uks.GetOrAddThought("non-letter", "Object");
        SeqElement sequence = uks.AddSequenceAndLink(
            uks.GetOrAddThought("mixed-sequence"),
            uks.GetOrAddThought("hasSequence", "LinkType"),
            new List<Thought> { uks.Labeled("A"), nonLetter, uks.Labeled("C") });

        var matches = uks.FindSequencesByActivation(
            new List<Thought> { uks.Labeled("A"), letters, uks.Labeled("C") },
            uks.Labeled("TemplateSequenceSearch"));

        Assert.DoesNotContain(matches, x => ReferenceEquals(x.seqNode, sequence));
    }

    [Fact]
    public void OptionalArticleWildcard_MatchesSentenceWithOrWithoutOneArticle()
    {
        var uks = CreateUKS();
        Thought articleClass = uks.GetOrAddThought("Article", "WordClass");
        Thought nounClass = uks.GetOrAddThought("Noun", "WordClass");
        Thought the = uks.GetOrAddThought("the", articleClass);
        Thought dog = uks.GetOrAddThought("dog", nounClass);
        Thought runs = uks.GetOrAddThought("runs", "Word");
        Thought optionalArticle = uks.CreateWildcard(
            "???article", new List<Thought> { articleClass }, "isOptionalWildcard");
        Thought noun = uks.CreateWildcard(
            "??noun", new List<Thought> { nounClass }, "isWildcard");
        Thought words = uks.GetOrAddThought("hasWords", "LinkType");
        SeqElement withoutArticle2 = uks.AddSequenceAndLink(
            uks.GetOrAddThought("dog-runs"), words, new List<Thought> { "dog", "runs" });
        SeqElement withoutArticle = uks.AddSequenceAndLink(
            uks.GetOrAddThought("dog-runs"), words, new List<Thought> { dog, runs });
        SeqElement withArticle = uks.AddSequenceAndLink(
            uks.GetOrAddThought("the-dog-runs"), words, new List<Thought> { the, dog, runs });

        var matches = uks.FindSequencesByActivation(
            new List<Thought> { optionalArticle, noun, runs },
            uks.Labeled("TemplateSequenceSearch"));

        Assert.Contains(matches, x => ReferenceEquals(x.seqNode, withoutArticle));
        Assert.Contains(matches, x => ReferenceEquals(x.seqNode, withArticle));
    }

    [Fact]
    public void IsSequenceElement_DetectsSeqElement()
    {
        var uks = CreateUKS();
        var word = uks.GetOrAddThought("word", "Thought");
        var linkType = uks.GetOrAddThought("spelled", "LinkType");
        var seq = uks.CreateFirstElement(word.Label, uks.GetOrAddThought("a"));

        Assert.True(uks.IsSequenceElement(seq));
        Assert.False(uks.IsSequenceElement(word));
    }

    [Fact]
    public void AddSequence_CreatesSequenceAndFlattens()
    {
        var uks = CreateUKS();
        var source = uks.GetOrAddThought("cat", "Thought");
        var linkType = uks.GetOrAddThought("spelled", "LinkType");
        var targets = new List<Thought>
        {
            uks.GetOrAddThought("C"),
            uks.GetOrAddThought("A"),
            uks.GetOrAddThought("T"),
        };

        SeqElement first = uks.AddSequenceAndLink(source, linkType, targets);

        Assert.NotNull(first);
        Assert.True(uks.IsSequenceElement(first));

        var flat = uks.FlattenSequence(first);
        Assert.Equal(new[] { "C", "A", "T" }, flat.Select(x => x.Label));
    }

    [Fact]
    public void AddSequence_CreatesAndFindsAOneElementSequence()
    {
        // A one-character spelling and a one-word phrase are still sequences:
        // their length must not make them disappear from the representation.
        var uks = CreateUKS();
        Thought source = uks.GetOrAddThought("w:4", "Thought");
        Thought spelled = uks.GetOrAddThought("spelled", "LinkType");
        Thought digit = uks.GetOrAddThought("c:4", "Thought");

        SeqElement sequence = uks.AddSequenceAndLink(
            source, spelled, new List<Thought> { digit });
        List<(SeqElement seqNode, float confidence)> matches =
            uks.FindSequencesByActivation(
                new List<Thought> { digit }, uks.Labeled("ExactSequenceSearch"));

        Assert.NotNull(sequence);
        Assert.Equal(new[] { "c:4" },
            uks.FlattenSequence(sequence).Select(element => element.Label));
        Assert.Contains(matches, match =>
            ReferenceEquals(match.seqNode, sequence) && match.confidence == 1.0f);
    }

    [Fact]
    public void InsertElement_PrependsValue()
    {
        var uks = CreateUKS();
        var source = uks.GetOrAddThought("dog", "Thought");
        var linkType = uks.GetOrAddThought("spelled", "LinkType");
        var targets = new List<Thought>
        {
            uks.GetOrAddThought("d"),
            uks.GetOrAddThought("o"),
            uks.GetOrAddThought("g"),
        };
        SeqElement first = uks.AddSequenceAndLink(source, linkType, targets);

        var newVal = uks.GetOrAddThought("!"); // prepend
        SeqElement updatedFirst = uks.InsertElement(first, newVal);

        var flat = uks.FlattenSequence(updatedFirst);
        Assert.Equal(new[] { "!", "D", "O", "G" }, flat.Select(x => x.Label));
    }

    [Fact]
    public void FindSequencesByActivation_FindsExactMatch()
    {
        var uks = CreateUKS();
        var source = uks.GetOrAddThought("pi", "Thought");
        var source2 = uks.GetOrAddThought("pi2", "Thought");
        var linkType = uks.GetOrAddThought("hasDigit", "LinkType");
        uks.GetOrAddThought("spelled", "LinkType");
        var digits = new List<Thought>
        {
            uks.GetOrAddThought("3"),
            uks.GetOrAddThought("."),
            uks.GetOrAddThought("1"),
            uks.GetOrAddThought("4"),
        };
        SeqElement seq = uks.AddSequenceAndLink(source, linkType, digits);
        SeqElement interference = uks.AddSequenceAndLink(source2, linkType, new List<Thought> {"3",".","1" });

        var matches = uks.FindSequencesByActivation(digits);

        Assert.Contains(matches, m => ReferenceEquals(m.seqNode, seq) && m.confidence >= 1.0f);

        var set = uks.AddSequenceAndLink(uks.GetOrAddThought("SET"), "spelled", new List<Thought> { "S", "E", "T" });
        matches = uks.FindSequencesByActivation(new List<Thought> { "S", "E", "T" });
        Assert.Contains(matches, m => ReferenceEquals(m.seqNode, set) && m.confidence >= 1.0f);

        var reset = uks.AddSequenceAndLink(uks.GetOrAddThought("RESET"), "spelled", new List<Thought> { "R", "E", set });
        matches = uks.FindSequencesByActivation(new List<Thought> { "R", "E", "S", "E", "T" });
        Assert.Contains(matches, m => ReferenceEquals(m.seqNode, reset) && m.confidence >= 1.0f);

        var sets = uks.AddSequenceAndLink(uks.GetOrAddThought("SETS"), "spelled", new List<Thought> { set, "S" });
        matches = uks.FindSequencesByActivation(new List<Thought> { "S", "E", "T", "S" });
        Assert.Contains(matches, m => ReferenceEquals(m.seqNode, sets) && m.confidence >= 1.0f);

        var upsets = uks.AddSequenceAndLink(uks.GetOrAddThought("UPSETS"), "spelled", new List<Thought> { "U", "P", set, "S" });
        matches = uks.FindSequencesByActivation(new List<Thought> { "U", "P", "S", "E", "T", "S" });
        Assert.Contains(matches, m => ReferenceEquals(m.seqNode, upsets) && m.confidence >= 1.0f);

        var resets = uks.AddSequenceAndLink(uks.GetOrAddThought("RESETS"), "spelled", new List<Thought> { reset, "S" });
        matches = uks.FindSequencesByActivation(new List<Thought> { "R", "E", "S", "E", "T", "S" });
        Assert.Contains(matches, m => ReferenceEquals(m.seqNode, resets) && m.confidence >= 1.0f);

        var preset = uks.AddSequenceAndLink(uks.GetOrAddThought("PRESET"), "spelled", new List<Thought> { "P", reset });
        matches = uks.FindSequencesByActivation(new List<Thought> { "P", "R", "E", "S", "E", "T" });
        Assert.Contains(matches, m => ReferenceEquals(m.seqNode, preset) && m.confidence >= 1.0f);

        var presets = uks.AddSequenceAndLink(uks.GetOrAddThought("PRESETS"), "spelled", new List<Thought> { preset, "S" });
        matches = uks.FindSequencesByActivation(new List<Thought> { "P", "R", "E", "S", "E", "T", "S" });
        Assert.Contains(matches, m => ReferenceEquals(m.seqNode, presets) && m.confidence >= 1.0f);

        var presetting = uks.AddSequenceAndLink(uks.GetOrAddThought("PRESETTING"), "spelled", new List<Thought> { preset, "T", "I", "N", "G" });
        matches = uks.FindSequencesByActivation(new List<Thought> { "P", "R", "E", "S", "E", "T", "T", "I", "N", "G" });
        Assert.Contains(matches, m => ReferenceEquals(m.seqNode, presetting) && m.confidence >= 1.0f);

        var setsixx = uks.AddSequenceAndLink(uks.GetOrAddThought("SETSIXX"), "spelled", new List<Thought> { sets, "I", "X"});
        matches = uks.FindSequencesByActivation(new List<Thought> { "S", "E", "T", "S", "I", "X"});
        Assert.Contains(matches, m => ReferenceEquals(m.seqNode, setsixx) && m.confidence >= 1.0f);

        var setreset = uks.AddSequenceAndLink(uks.GetOrAddThought("SETRESET"), "spelled", new List<Thought> { set, reset });
        matches = uks.FindSequencesByActivation(new List<Thought> { "S", "E", "T", "R", "E", "S", "E", "T" });
        Assert.Contains(matches, m => ReferenceEquals(m.seqNode, setreset) && m.confidence >= 1.0f);
    }

    [Fact]
    public void FindSequencesByActivation_MatchesWildcardsInPatternOnly()
    {
        var uks = CreateUKS();
        var searchOptions = uks.Labeled("TemplateSequenceSearch");
        var wildcard = uks.Labeled("??");
        wildcard.AddLink("hasProperty", "isWildcard");

        var cat = uks.AddSequenceAndLink(uks.GetOrAddThought("CAT"), "spelled", new List<Thought> { "C", "A", "T" });
        var cot = uks.AddSequenceAndLink(uks.GetOrAddThought("COT"), "spelled", new List<Thought> { "C", "O", "T" });
        var dog = uks.AddSequenceAndLink(uks.GetOrAddThought("DOG"), "spelled", new List<Thought> { "D", "O", "G" });
        var frog = uks.AddSequenceAndLink(uks.GetOrAddThought("FROG"), "spelled", new List<Thought> { "F", "R", "O", "G" });

        var middleWildcard = uks.FindSequencesByActivation(new List<Thought> { "C", wildcard, "T" }, searchOptions);
        Assert.Contains(middleWildcard, m => ReferenceEquals(m.seqNode, cat) && m.confidence >= 1.0f);
        Assert.Contains(middleWildcard, m => ReferenceEquals(m.seqNode, cot) && m.confidence >= 1.0f);
        Assert.DoesNotContain(middleWildcard, m => ReferenceEquals(m.seqNode, dog));

        var firstWildcard = uks.FindSequencesByActivation(new List<Thought> { wildcard, "O", "G" }, searchOptions);
        Assert.Contains(firstWildcard, m => ReferenceEquals(m.seqNode, dog) && m.confidence >= 1.0f);
        Assert.DoesNotContain(firstWildcard, m => ReferenceEquals(m.seqNode, cat));
        Assert.DoesNotContain(firstWildcard, m => ReferenceEquals(m.seqNode, frog));

        var twoLeadingWildcards = uks.FindSequencesByActivation(new List<Thought> { wildcard, wildcard, "O", "G" }, searchOptions);
        Assert.Contains(twoLeadingWildcards, m => ReferenceEquals(m.seqNode, frog) && m.confidence >= 1.0f);
        Assert.DoesNotContain(twoLeadingWildcards, m => ReferenceEquals(m.seqNode, dog));

        var lastWildcard = uks.FindSequencesByActivation(new List<Thought> { "D", "O", wildcard }, searchOptions);
        Assert.Contains(lastWildcard, m => ReferenceEquals(m.seqNode, dog) && m.confidence >= 1.0f);
        Assert.DoesNotContain(lastWildcard, m => ReferenceEquals(m.seqNode, cat));

        var multipleWildcards = uks.FindSequencesByActivation(new List<Thought> { wildcard, "O", wildcard }, searchOptions);
        Assert.Contains(multipleWildcards, m => ReferenceEquals(m.seqNode, cot) && m.confidence >= 1.0f);
        Assert.Contains(multipleWildcards, m => ReferenceEquals(m.seqNode, dog) && m.confidence >= 1.0f);
        Assert.DoesNotContain(multipleWildcards, m => ReferenceEquals(m.seqNode, cat));
    }

    [Fact]
    public void FindSequencesByActivation_ConcreteInputFindsStoredGenericTemplate()
    {
        // Interactive text input searches in this direction: the input is concrete while
        // the sequence already in the UKS contains unconstrained wildcards.
        UKS uks = CreateUKS();
        Thought wildcard = uks.Labeled("??");
        Thought isWord = uks.GetOrAddThought("is", "Word");
        SeqElement template = uks.AddSequenceAndLink(
            uks.GetOrAddThought("inputTemplate"),
            "hasWords",
            new List<Thought> { wildcard, isWord, wildcard });

        List<(SeqElement seqNode, float confidence)> matches =
            uks.FindSequencesByActivation(
                new List<Thought>
                {
                    uks.GetOrAddThought("fido", "Word"),
                    isWord,
                    uks.GetOrAddThought("wet", "Word")
                },
                uks.Labeled("TemplateSequenceSearch"));

        Assert.Contains(matches, match => ReferenceEquals(match.seqNode, template));
    }

    [Fact]
    public void FindSequencesByActivation_LearningSearchAllowsNewMembershipForClassifiedFillers()
    {
        // Strict matching must reject a word that is not already in the
        // wildcard's class. The learning search may provisionally accept it as
        // a new member even when another learned class already contains it.
        UKS uks = CreateUKS();
        Thought learnedClassRoot = uks.GetOrAddThought("LearnedClass", "Thought");
        Thought subjectClass = uks.GetOrAddThought("subjectClass", learnedClassRoot);
        Thought descriptionClass = uks.GetOrAddThought("descriptionClass", learnedClassRoot);
        Thought otherClass = uks.GetOrAddThought("otherClass", learnedClassRoot);
        Thought subject = uks.CreateWildcard("??subjectClass", new List<Thought> { subjectClass });
        Thought description = uks.CreateWildcard("??descriptionClass", new List<Thought> { descriptionClass });
        Thought the = uks.GetOrAddThought("w:the", "Word");
        Thought are = uks.GetOrAddThought("w:are", "Word");
        Thought pigs = uks.GetOrAddThought("w:pigs", "Word");
        Thought happy = uks.GetOrAddThought("w:happy", "Word");
        SeqElement template = uks.AddSequenceAndLink(
            uks.GetOrAddThought("learnedTemplate"),
            "hasWords",
            new List<Thought> { the, subject, are, description });
        List<Thought> input = new() { the, pigs, are, happy };

        Assert.DoesNotContain(
            uks.FindSequencesByActivation(input, uks.Labeled("TemplateSequenceSearch")),
            match => ReferenceEquals(match.seqNode, template));
        Assert.Contains(
            uks.FindSequencesByActivation(input, uks.Labeled("TemplateLearningSearch")),
            match => ReferenceEquals(match.seqNode, template));

        pigs.AddParent(otherClass);
        Assert.Contains(
            uks.FindSequencesByActivation(input, uks.Labeled("TemplateLearningSearch")),
            match => ReferenceEquals(match.seqNode, template));
    }

    [Fact]
    public void FindSequencesByActivation_LearningSearchAcceptsAnyAnonymousClassWildcard()
    {
        UKS uks = CreateUKS();
        Thought anonymousClassRoot = uks.GetOrAddThought("AnonymousClass", "Thought");
        Thought actionClass = uks.GetOrAddThought("actionClass0", anonymousClassRoot);
        Thought subject = uks.CreateWildcard(
            "??actionClass0", new List<Thought> { actionClass });
        Thought what = uks.GetOrAddThought("w:what", "Word");
        Thought does = uks.GetOrAddThought("w:does", "Word");
        Thought article = uks.GetOrAddThought("w:a", "Word");
        Thought have = uks.GetOrAddThought("w:have", "Word");
        Thought dog = uks.GetOrAddThought("w:dog", "Word");
        SeqElement template = uks.AddSequenceAndLink(
            uks.GetOrAddThought("questionTemplate"),
            "hasWords",
            new List<Thought> { what, does, article, subject, have });

        List<(SeqElement seqNode, float confidence)> strictMatches =
            uks.FindSequencesByActivation(
                new List<Thought> { what, does, article, dog, have },
                uks.Labeled("TemplateSequenceSearch"));
        List<(SeqElement seqNode, float confidence)> learningMatches =
            uks.FindSequencesByActivation(
                new List<Thought> { what, does, article, dog, have },
                uks.Labeled("TemplateLearningSearch"));

        Assert.DoesNotContain(strictMatches,
            match => ReferenceEquals(match.seqNode, template));
        Assert.Contains(learningMatches,
            match => ReferenceEquals(match.seqNode, template));
    }

    [Fact]
    public void FindSequencesByActivation_MatchesClassBasedWildcards()
    {
        var uks = CreateUKS();
        var searchOptions = uks.GetOrAddThought("ClassWildcardSearch", "SearchOption");
        searchOptions.AddLink("hasProperty", "allowWildcards");

        // Create word classes
        var verb = uks.GetOrAddThought("Verb", "WordClass");
        var noun = uks.GetOrAddThought("Noun", "WordClass");

        // Create specific words as instances of classes
        var run = uks.GetOrAddThought("run", "Word");
        run.AddParent(verb);

        var jump = uks.GetOrAddThought("jump", "Word");
        jump.AddParent(verb);

        var dog = uks.GetOrAddThought("dog", "Word");
        dog.AddParent(noun);

        var cat = uks.GetOrAddThought("cat", "Word");
        cat.AddParent(noun);

        // Create class-based wildcards with isWildcard property
        var verbWildcard = uks.GetOrAddThought("??verb", "Property");
        verbWildcard.AddLink("hasProperty", "isWildcard");
        verbWildcard.AddParent(verb);

        var nounWildcard = uks.GetOrAddThought("??noun", "Property");
        nounWildcard.AddLink("hasProperty", "isWildcard");
        nounWildcard.AddParent(noun);

        // Create test sequences: "the dog runs", "the cat jumps", "the dog sleeps"
        var the = uks.GetOrAddThought("the", "Word");
        var runs = uks.GetOrAddThought("runs", "Word");
        runs.AddParent(verb);

        var jumps = uks.GetOrAddThought("jumps", "Word");
        jumps.AddParent(verb);

        var sleeps = uks.GetOrAddThought("sleeps", "Word");
        sleeps.AddParent(verb);

        var seq1 = uks.AddSequenceAndLink(uks.GetOrAddThought("sentence1"), "words", 
            new List<Thought> { the, dog, runs });
        var seq2 = uks.AddSequenceAndLink(uks.GetOrAddThought("sentence2"), "words", 
            new List<Thought> { the, cat, jumps });
        var seq3 = uks.AddSequenceAndLink(uks.GetOrAddThought("sentence3"), "words", 
            new List<Thought> { the, dog, sleeps });

        // Test: Pattern "the <noun> <verb>" should match all three sentences
        var nounVerbPattern = uks.FindSequencesByActivation(
            new List<Thought> { the, nounWildcard, verbWildcard }, searchOptions);

        Assert.Contains(nounVerbPattern, m => ReferenceEquals(m.seqNode, seq1) && m.confidence >= 1.0f);
        Assert.Contains(nounVerbPattern, m => ReferenceEquals(m.seqNode, seq2) && m.confidence >= 1.0f);
        Assert.Contains(nounVerbPattern, m => ReferenceEquals(m.seqNode, seq3) && m.confidence >= 1.0f);

        // Test: Pattern "the dog <verb>" should match sentences with "dog"
        var dogVerbPattern = uks.FindSequencesByActivation(
            new List<Thought> { the, dog, verbWildcard }, searchOptions);

        Assert.Contains(dogVerbPattern, m => ReferenceEquals(m.seqNode, seq1) && m.confidence >= 1.0f);
        Assert.DoesNotContain(dogVerbPattern, m => ReferenceEquals(m.seqNode, seq2)); // has cat, not dog
        Assert.Contains(dogVerbPattern, m => ReferenceEquals(m.seqNode, seq3) && m.confidence >= 1.0f);

        // Test: Pattern "the <noun> runs" should only match seq1
        var nounRunsPattern = uks.FindSequencesByActivation(
            new List<Thought> { the, nounWildcard, runs }, searchOptions);

        Assert.Contains(nounRunsPattern, m => ReferenceEquals(m.seqNode, seq1) && m.confidence >= 1.0f);
        Assert.DoesNotContain(nounRunsPattern, m => ReferenceEquals(m.seqNode, seq2)); // has jumps, not runs
        Assert.DoesNotContain(nounRunsPattern, m => ReferenceEquals(m.seqNode, seq3)); // has sleeps, not runs

        // Test: Pattern "the cat <verb>" should only match seq2
        var catVerbPattern = uks.FindSequencesByActivation(
            new List<Thought> { the, cat, verbWildcard }, searchOptions);

        Assert.DoesNotContain(catVerbPattern, m => ReferenceEquals(m.seqNode, seq1)); // has dog, not cat
        Assert.Contains(catVerbPattern, m => ReferenceEquals(m.seqNode, seq2) && m.confidence >= 1.0f);
        Assert.DoesNotContain(catVerbPattern, m => ReferenceEquals(m.seqNode, seq3)); // has dog, not cat

        // Test: Pattern "the cat <noun>" should match NOTHING (no sequence has cat + noun)
        var catNounPattern = uks.FindSequencesByActivation(
            new List<Thought> { the, cat, nounWildcard }, searchOptions);

        Assert.DoesNotContain(catNounPattern, m => ReferenceEquals(m.seqNode, seq1)); // has dog, not cat
        Assert.DoesNotContain(catNounPattern, m => ReferenceEquals(m.seqNode, seq2)); // has cat, but jumps is a verb
        Assert.DoesNotContain(catNounPattern, m => ReferenceEquals(m.seqNode, seq3)); // has dog, not cat
        Assert.Empty(catNounPattern); // Verify no matches at all
    }

    [Fact]
    public void AddSequence_ReusesExistingSubsequence()
    {
        var uks = CreateUKS();
        var linkType = uks.GetOrAddThought("spelled", "LinkType");

        var setSource = uks.GetOrAddThought("SET", "Thought");
        var setLetters = new List<Thought>
        {
            uks.GetOrAddThought("S"),
            uks.GetOrAddThought("E"),
            uks.GetOrAddThought("T"),
        };
        SeqElement setSeq = uks.AddSequenceAndLink(setSource, linkType, setLetters);

        var resetSource = uks.GetOrAddThought("RESET", "Thought");
        var resetLetters = new List<Thought>
        {
            uks.GetOrAddThought("R"),
            uks.GetOrAddThought("E"),
            uks.GetOrAddThought("S"),
            uks.GetOrAddThought("E"),
            uks.GetOrAddThought("T"),
        };
        SeqElement resetSeq = uks.AddSequenceAndLink(resetSource, linkType, resetLetters);

        var topLevel = GetTopLevelValues(uks, resetSeq);
        Assert.Equal(3, topLevel.Count);                           // R, E, and the SET subsequence
        Assert.Equal(new[] { "R", "E" }, topLevel.Take(2).Select(t => t.Label));
        Assert.Same(setSeq, topLevel[2]);                          // SET was reused as a subsequence

        var flat = uks.FlattenSequence(resetSeq);
        Assert.Equal(new[] { "R", "E", "S", "E", "T" }, flat.Select(x => x.Label));
    }

    [Fact]
    public void AddSequence_SupportsNestedSubsequencesDepth3()
    {
        var uks = CreateUKS();
        var linkType = uks.GetOrAddThought("spelled", "LinkType");

        var setSeq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("SET", "Thought"),
            linkType,
            new List<Thought> { uks.GetOrAddThought("S"), uks.GetOrAddThought("E"), uks.GetOrAddThought("T") });

        var resetSeq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("RESET", "Thought"),
            linkType,
            new List<Thought>
            {
                uks.GetOrAddThought("R"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("S"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("T"),
            });

        var presetSeq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("PRESET", "Thought"),
            linkType,
            new List<Thought>
            {
                uks.GetOrAddThought("P"),
                uks.GetOrAddThought("R"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("S"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("T"),
            });

        var presetTop = GetTopLevelValues(uks, presetSeq);
        Assert.Equal(2, presetTop.Count);               // P + RESET (which already embeds SET)
        Assert.Equal("P", presetTop[0].Label);
        Assert.Same(resetSeq, presetTop[1]);            // depth: PRESET -> RESET -> SET

        var flat = uks.FlattenSequence(presetSeq);
        Assert.Equal(new[] { "P", "R", "E", "S", "E", "T" }, flat.Select(x => x.Label));
    }

    [Fact]
    public void FindSequencesByActivation_FindsPartialMatchThroughSubsequence()
    {
        var uks = CreateUKS();
        var linkType = uks.GetOrAddThought("spelled", "LinkType");
        var searchOptions = CreatePartialSequenceSearchOptions(uks);

        var setSeq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("SET", "Thought"),
            linkType,
            new List<Thought> { uks.GetOrAddThought("S"), uks.GetOrAddThought("E"), uks.GetOrAddThought("T") });

        var resetSeq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("RESET", "Thought"),
            linkType,
            new List<Thought>
            {
                uks.GetOrAddThought("R"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("S"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("T"),
            });

        var pattern = new List<Thought>
        {
            uks.GetOrAddThought("E"),
            uks.GetOrAddThought("S"),
            uks.GetOrAddThought("E"),
        };

        var matches = uks.FindSequencesByActivation(pattern, searchOptions);

        Assert.Contains(matches, m => ReferenceEquals(m.seqNode, resetSeq) && m.confidence >= 0.6f); // 3 of 5 letters matched
    }

    [Fact]
    public void FindSequencesByActivation_FindsResetAndBesetForESEPattern()
    {
        var uks = CreateUKS();
        var linkType = uks.GetOrAddThought("spelled", "LinkType");
        var searchOptions = CreatePartialSequenceSearchOptions(uks);

        var setSeq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("SET", "Thought"),
            linkType,
            new List<Thought> { uks.GetOrAddThought("S"), uks.GetOrAddThought("E"), uks.GetOrAddThought("T") });

        var resetSeq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("RESET", "Thought"),
            linkType,
            new List<Thought>
            {
                uks.GetOrAddThought("R"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("S"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("T"),
            });

        var besetSeq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("BESET", "Thought"),
            linkType,
            new List<Thought>
            {
                uks.GetOrAddThought("B"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("S"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("T"),
            });

        var pattern = new List<Thought>
        {
            uks.GetOrAddThought("E"),
            uks.GetOrAddThought("S"),
            uks.GetOrAddThought("E"),
        };

        var matches = uks.FindSequencesByActivation(pattern, searchOptions);

        var resetMatch = Assert.Single(matches.Where(m => ReferenceEquals(m.seqNode, resetSeq)));
        Assert.Equal(3f / 5f, resetMatch.confidence, .75);

        var besetMatch = Assert.Single(matches.Where(m => ReferenceEquals(m.seqNode, besetSeq)));
        Assert.Equal(3f / 5f, besetMatch.confidence, .75);
    }

    [Fact]
    public void AddSequence_ReusesSubsequenceAtStartWithTrailingContinuation()
    {
        var uks = CreateUKS();
        var linkType = uks.GetOrAddThought("spelled", "LinkType");

        var setSeq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("SET", "Thought"),
            linkType,
            new List<Thought> { uks.GetOrAddThought("S"), uks.GetOrAddThought("E"), uks.GetOrAddThought("T") });

        var setupSeq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("SETUP", "Thought"),
            linkType,
            new List<Thought>
            {
                uks.GetOrAddThought("S"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("T"),
                uks.GetOrAddThought("U"),
                uks.GetOrAddThought("P"),
            });

        var topLevel = GetTopLevelValues(uks, setupSeq);
        Assert.Equal(3, topLevel.Count);            // SET subsequence + U + P
        Assert.Same(setSeq, topLevel[0]);
        Assert.Equal(new[] { "U", "P" }, topLevel.Skip(1).Select(t => t.Label));

        var flat = uks.FlattenSequence(setupSeq);
        Assert.Equal(new[] { "S", "E", "T", "U", "P" }, flat.Select(x => x.Label));
    }

    [Fact]
    public void FindSequencesByActivation_FourWords_MultiplePatternsReturnExpectedConfidences()
    {
        var uks = CreateUKS();
        var linkType = uks.GetOrAddThought("spelled", "LinkType");
        var searchOptions = CreatePartialSequenceSearchOptions(uks);

        var setSeq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("SET", "Thought"),
            linkType,
            new List<Thought> { uks.GetOrAddThought("S"), uks.GetOrAddThought("E"), uks.GetOrAddThought("T") });

        var resetSeq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("RESET", "Thought"),
            linkType,
            new List<Thought>
            {
                uks.GetOrAddThought("R"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("S"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("T"),
            });

        var besetSeq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("BESET", "Thought"),
            linkType,
            new List<Thought>
            {
                uks.GetOrAddThought("B"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("S"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("T"),
            });

        var presetSeq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("PRESET", "Thought"),
            linkType,
            new List<Thought>
            {
                uks.GetOrAddThought("P"),
                uks.GetOrAddThought("R"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("S"),
                uks.GetOrAddThought("E"),
                uks.GetOrAddThought("T"),
            });

        // BES -> only BESET
        var besPattern = new List<Thought> { "B", "E", "S" };
        var besMatches = uks.FindSequencesByActivation(besPattern, searchOptions);
        var besetMatch = Assert.Single(besMatches.Where(m => ReferenceEquals(m.seqNode, besetSeq)));
        Assert.Equal(3f / 5f, besetMatch.confidence, 3);

        // ESE -> RESET, BESET
        var esePattern = new List<Thought> { uks.GetOrAddThought("E"), uks.GetOrAddThought("S"), uks.GetOrAddThought("E") };
        var eseMatches = uks.FindSequencesByActivation(esePattern, searchOptions);
        var eseReset = Assert.Single(eseMatches.Where(m => ReferenceEquals(m.seqNode, resetSeq)));
        Assert.Equal(3f / 5f, eseReset.confidence, .75);
        var eseBeset = Assert.Single(eseMatches.Where(m => ReferenceEquals(m.seqNode, besetSeq)));
        Assert.Equal(3f / 5f, eseBeset.confidence, .75);

        // PRE -> PRESET
        var prePattern = new List<Thought> { uks.GetOrAddThought("P"), uks.GetOrAddThought("R"), uks.GetOrAddThought("E") };
        var preMatches = uks.FindSequencesByActivation(prePattern, searchOptions);
        var prePreset = Assert.Single(preMatches.Where(m => ReferenceEquals(m.seqNode, presetSeq)));
        Assert.Equal(3f / 6f, prePreset.confidence, .75);


        //NOTE
        // ET -> JUST SET
        var etPattern = new List<Thought> { uks.GetOrAddThought("E"), uks.GetOrAddThought("T") };
        var etMatches = uks.FindSequencesByActivation(etPattern, searchOptions);

        var etSet = Assert.Single(etMatches.Where(m => ReferenceEquals(m.seqNode, setSeq)));
        Assert.Equal(2f / 3f, etSet.confidence, .75);
    }


    [Fact]
    public void CircularSequence_FlattensWithoutLooping()
    {
        var uks = CreateUKS();
        var linkType = uks.GetOrAddThought("spelled", "LinkType");

        var seq = uks.AddSequenceAndLink(
            uks.GetOrAddThought("CIRC", "Thought"),
            linkType,
            new List<Thought>
            {
                uks.GetOrAddThought("A"),
                uks.GetOrAddThought("B"),
                uks.GetOrAddThought("C"),
            });

        // make it circular: last -> first
        var last = seq;
        while (last.NXT is not null && last.NXT != seq)
            last = last.NXT;
        last.NXT = seq;

        var flat = uks.FlattenSequence(seq);
        Assert.Equal(new[] { "A", "B", "C" }, flat.Select(x => x.Label));
    }
    /*
        //Circular Sequences are NOT IMPLEMENTED YET
    [Fact]
    public void CircularSequence_FindSequencesByActivationMatchesAcrossWrap()
    {
        var uks = CreateUKS();
        var linkType = uks.GetOrAddThought("spelled", "LinkType");

        var seq = uks.AddSequence(
            uks.GetOrAddThought("CIRC", "Thought"),
            linkType,
            new List<Thought>
            {
                uks.GetOrAddThought("A"),
                uks.GetOrAddThought("B"),
                uks.GetOrAddThought("C"),
            });

        // make it circular: last -> first
        var last = seq;
        while (last.NXT is not null && last.NXT != seq)
            last = last.NXT;
        last.NXT = seq;

        var pattern = new List<Thought>
        {
            uks.GetOrAddThought("B"),
            uks.GetOrAddThought("C"),
            uks.GetOrAddThought("A"),
        };

        var matches = uks.FindSequencesByActivation(pattern, uks.Labeled("MelodySearchOptions"));
        var match = Assert.Single(matches);
        Assert.Equal(1.0f, match.confidence, 3);
        Assert.Same(seq.NXT, match.seqNode); // seq.NXT is the element whose VLU is B
    }
    */
}

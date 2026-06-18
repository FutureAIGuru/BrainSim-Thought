using System.Collections.Generic;
using System.Linq;
using UKS;
using Xunit;

namespace UKS.Tests;

public class UKSSequenceTests
{
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
        var searchOptions = uks.GetOrAddThought("PartialSequenceSearch", "SequenceSearchOptions");
        searchOptions.AddLink("hasProperty", "allowNestedSequences");
        searchOptions.AddLink("hasProperty", "allowPartialMatch");
        return searchOptions;
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

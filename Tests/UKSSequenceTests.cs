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

    [Fact]
    public void IsSequenceElement_DetectsSeqElement()
    {
        var uks = CreateUKS();
        var word = uks.GetOrAddThought("word", "Thought");
        var linkType = uks.GetOrAddThought("spelled", "LinkType");
        var seq = uks.CreateFirstElement(word, uks.GetOrAddThought("a"));

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
            uks.GetOrAddThought("c"),
            uks.GetOrAddThought("a"),
            uks.GetOrAddThought("t"),
        };

        SeqElement first = uks.AddSequence(source, linkType, targets);

        Assert.NotNull(first);
        Assert.True(uks.IsSequenceElement(first));

        var flat = uks.FlattenSequence(first);
        Assert.Equal(new[] { "c", "a", "t" }, flat.Select(x => x.Label));
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
        SeqElement first = uks.AddSequence(source, linkType, targets);

        var newVal = uks.GetOrAddThought("!"); // prepend
        SeqElement updatedFirst = uks.InsertElement(first, newVal);

        var flat = uks.FlattenSequence(updatedFirst);
        Assert.Equal(new[] { "!", "d", "o", "g" }, flat.Select(x => x.Label));
    }

    [Fact]
    public void HasSequence_FindsExactMatch()
    {
        var uks = CreateUKS();
        var source = uks.GetOrAddThought("pi", "Thought");
        var linkType = uks.GetOrAddThought("hasDigit", "LinkType");
        var digits = new List<Thought>
        {
            uks.GetOrAddThought("3"),
            uks.GetOrAddThought("."),
            uks.GetOrAddThought("1"),
            uks.GetOrAddThought("4"),
        };
        SeqElement seq = uks.AddSequence(source, linkType, digits);

        var matches = uks.HasSequence(digits, linkType, skipPlusEntries: false, mustMatchFirst: true, mustMatchLast: true);

        Assert.Contains(matches, m => ReferenceEquals(m.seqNode, seq) && m.confidence >= 1.0f);
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
        SeqElement setSeq = uks.AddSequence(setSource, linkType, setLetters);

        var resetSource = uks.GetOrAddThought("RESET", "Thought");
        var resetLetters = new List<Thought>
        {
            uks.GetOrAddThought("R"),
            uks.GetOrAddThought("E"),
            uks.GetOrAddThought("S"),
            uks.GetOrAddThought("E"),
            uks.GetOrAddThought("T"),
        };
        SeqElement resetSeq = uks.AddSequence(resetSource, linkType, resetLetters);

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

        var setSeq = uks.AddSequence(
            uks.GetOrAddThought("SET", "Thought"),
            linkType,
            new List<Thought> { uks.GetOrAddThought("S"), uks.GetOrAddThought("E"), uks.GetOrAddThought("T") });

        var resetSeq = uks.AddSequence(
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

        var presetSeq = uks.AddSequence(
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
    public void HasSequence_FindsPartialMatchThroughSubsequence()
    {
        var uks = CreateUKS();
        var linkType = uks.GetOrAddThought("spelled", "LinkType");

        var setSeq = uks.AddSequence(
            uks.GetOrAddThought("SET", "Thought"),
            linkType,
            new List<Thought> { uks.GetOrAddThought("S"), uks.GetOrAddThought("E"), uks.GetOrAddThought("T") });

        var resetSeq = uks.AddSequence(
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

        var matches = uks.HasSequence(pattern, linkType, skipPlusEntries: false);

        Assert.Contains(matches, m => ReferenceEquals(m.seqNode, resetSeq) && m.confidence >= 0.6f); // 3 of 5 letters matched
    }

    [Fact]
    public void HasSequence_FindsResetAndBesetForESEPattern()
    {
        var uks = CreateUKS();
        var linkType = uks.GetOrAddThought("spelled", "LinkType");

        var setSeq = uks.AddSequence(
            uks.GetOrAddThought("SET", "Thought"),
            linkType,
            new List<Thought> { uks.GetOrAddThought("S"), uks.GetOrAddThought("E"), uks.GetOrAddThought("T") });

        var resetSeq = uks.AddSequence(
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

        var besetSeq = uks.AddSequence(
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

        var matches = uks.HasSequence(pattern, linkType, skipPlusEntries: false);

        var resetMatch = Assert.Single(matches.Where(m => ReferenceEquals(m.seqNode, resetSeq)));
        Assert.Equal(3f / 5f, resetMatch.confidence, 3);

        var besetMatch = Assert.Single(matches.Where(m => ReferenceEquals(m.seqNode, besetSeq)));
        Assert.Equal(3f / 5f, besetMatch.confidence, 3);
    }

    [Fact]
    public void AddSequence_ReusesSubsequenceAtStartWithTrailingContinuation()
    {
        var uks = CreateUKS();
        var linkType = uks.GetOrAddThought("spelled", "LinkType");

        var setSeq = uks.AddSequence(
            uks.GetOrAddThought("SET", "Thought"),
            linkType,
            new List<Thought> { uks.GetOrAddThought("S"), uks.GetOrAddThought("E"), uks.GetOrAddThought("T") });

        var setupSeq = uks.AddSequence(
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
    public void HasSequence_FourWords_MultiplePatternsReturnExpectedConfidences()
    {
        var uks = CreateUKS();
        var linkType = uks.GetOrAddThought("spelled", "LinkType");

        var setSeq = uks.AddSequence(
            uks.GetOrAddThought("SET", "Thought"),
            linkType,
            new List<Thought> { uks.GetOrAddThought("S"), uks.GetOrAddThought("E"), uks.GetOrAddThought("T") });

        var resetSeq = uks.AddSequence(
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

        var besetSeq = uks.AddSequence(
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

        var presetSeq = uks.AddSequence(
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
        var besPattern = new List<Thought> { uks.GetOrAddThought("B"), uks.GetOrAddThought("E"), uks.GetOrAddThought("S") };
        var besMatches = uks.HasSequence(besPattern, linkType, skipPlusEntries: false);
        var besetMatch = Assert.Single(besMatches.Where(m => ReferenceEquals(m.seqNode, besetSeq)));
        Assert.Equal(3f / 5f, besetMatch.confidence, 3);

        // ESE -> RESET, BESET
        var esePattern = new List<Thought> { uks.GetOrAddThought("E"), uks.GetOrAddThought("S"), uks.GetOrAddThought("E") };
        var eseMatches = uks.HasSequence(esePattern, linkType, skipPlusEntries: false);
        var eseReset = Assert.Single(eseMatches.Where(m => ReferenceEquals(m.seqNode, resetSeq)));
        Assert.Equal(3f / 5f, eseReset.confidence, 3);
        var eseBeset = Assert.Single(eseMatches.Where(m => ReferenceEquals(m.seqNode, besetSeq)));
        Assert.Equal(3f / 5f, eseBeset.confidence, 3);

        // PRE -> PRESET
        var prePattern = new List<Thought> { uks.GetOrAddThought("P"), uks.GetOrAddThought("R"), uks.GetOrAddThought("E") };
        var preMatches = uks.HasSequence(prePattern, linkType, skipPlusEntries: false);
        var prePreset = Assert.Single(preMatches.Where(m => ReferenceEquals(m.seqNode, presetSeq)));
        Assert.Equal(3f / 6f, prePreset.confidence, 3);

        // ET -> all four
        var etPattern = new List<Thought> { uks.GetOrAddThought("E"), uks.GetOrAddThought("T") };
        var etMatches = uks.HasSequence(etPattern, linkType, skipPlusEntries: false);

        var etSet = Assert.Single(etMatches.Where(m => ReferenceEquals(m.seqNode, setSeq)));
        Assert.Equal(2f / 3f, etSet.confidence, 3);

        var etReset = Assert.Single(etMatches.Where(m => ReferenceEquals(m.seqNode, resetSeq)));
        Assert.Equal(2f / 5f, etReset.confidence, 3);

        var etBeset = Assert.Single(etMatches.Where(m => ReferenceEquals(m.seqNode, besetSeq)));
        Assert.Equal(2f / 5f, etBeset.confidence, 3);

        var etPreset = Assert.Single(etMatches.Where(m => ReferenceEquals(m.seqNode, presetSeq)));
        Assert.Equal(2f / 6f, etPreset.confidence, 3);
    }

    [Fact]
    public void HasSequence_SkipsPlusEntriesWhenRequested()
    {
        var uks = CreateUKS();
        var linkType = uks.GetOrAddThought("spelled", "LinkType");

        var seq = uks.AddSequence(
            uks.GetOrAddThought("A_PLUS_B", "Thought"),
            linkType,
            new List<Thought>
            {
                uks.GetOrAddThought("A"),
                uks.GetOrAddThought("+"),
                uks.GetOrAddThought("B"),
            });

        var pattern = new List<Thought>
        {
            uks.GetOrAddThought("A"),
            uks.GetOrAddThought("B"),
        };

        // Without skipping "+", no full match should be found
        var noSkip = uks.HasSequence(pattern, linkType, skipPlusEntries: false, mustMatchFirst: true, mustMatchLast: true);
        Assert.Empty(noSkip);

        // With skipping "+", we should match with confidence 2/3 (two of three nodes)
        var withSkip = uks.HasSequence(pattern, linkType, skipPlusEntries: true, mustMatchFirst: true, mustMatchLast: true);
        var match = Assert.Single(withSkip);
        Assert.Same(seq, match.seqNode);
        Assert.Equal(2f / 3f, match.confidence, 3);
    }

    [Fact]
    public void CircularSequence_FlattensWithoutLooping()
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

        var flat = uks.FlattenSequence(seq);
        Assert.Equal(new[] { "A", "B", "C" }, flat.Select(x => x.Label));
    }

    [Fact]
    public void CircularSequence_HasSequenceMatchesAcrossWrap()
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

        var matches = uks.HasSequence(pattern, linkType, skipPlusEntries: false, mustMatchFirst: false, mustMatchLast: false);
        var match = Assert.Single(matches);
        Assert.Equal(1.0f, match.confidence, 3);
        Assert.Same(seq.NXT, match.seqNode); // seq.NXT is the element whose VLU is B
    }
}
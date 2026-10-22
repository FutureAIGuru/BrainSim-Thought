using BrainSimulator.Modules;
using UKS;
using System.Linq;
using Xunit;

namespace BrainSimulator.Tests;

[CollectionDefinition("SequenceSegmenter", DisableParallelization = true)]
public class SequenceSegmenterCollection { }

[Collection("SequenceSegmenter")]
public partial class ModuleSequenceSegmenterTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public ModuleSequenceSegmenterTests(Xunit.Abstractions.ITestOutputHelper output) { _output = output; }

    [Fact]
    public void RepeatedDog_CreatesThenStrengthensAndConsolidates()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.MaximumProposalsPerEpisode = 1;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        Assert.Empty(module.PatternRoot.Children);
        for (int repeat = 1; repeat <= 3; repeat++)
        {
            ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
            Thought dog = uks.Labeled("w:dog");
            Assert.Equal(repeat, dog.Weight);
            Assert.Equal(repeat < 3, dog.isPlastic);
        }
        Assert.Single(module.PatternRoot.Children); // No inherited do/og/d/o/g candidates.
    }

    [Theory]
    [InlineData("dog")]
    public void UnemphasizedSingleUse_DoesNotAdmitMultiSymbolCandidates(string text)
    {
        var module = CreateInitializedSegmenter(CreateUKS());
        ModuleSequenceSegmenterDlg.SubmitLine(module, text);
        Assert.Empty(module.PatternRoot.Children);
    }

    [Theory]
    [InlineData("herefidofidofido", "herefidofidofido", "fido", false)]
    [InlineData("frogdog", "frogpig", "frog", false)]
    [InlineData("dogfrog", "pigfrog", "gfrog", false)]
    [InlineData("dogfrog", "catfrog", "frog", false)]
    [InlineData("xay", "catadog", "cata", true)]
    [InlineData("xay", "zad", "", false)]
    public void CandidateSelection_AllowsOnlySoleUnrecognizedSingleSymbols(string first, string second, string expected, bool knownNeighbors)
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.MaximumProposalsPerEpisode = 1;
        if (knownNeighbors)
        {
            SeedPattern(module, "cat", 3);
            SeedPattern(module, "dog", 3);
        }
        ModuleSequenceSegmenterDlg.SubmitLine(module, first);
        Assert.Equal(knownNeighbors ? 2 : 0, module.PatternRoot.Children.Count);
        ModuleSequenceSegmenterDlg.SubmitLine(module, second);
        var candidates = module.PatternRoot.Children.Where(thought => thought.isPlastic);
        if (expected.Length == 0) Assert.Empty(candidates);
        else
        {
            Thought candidate = Assert.Single(candidates);
            Assert.Equal("w:" + expected, candidate.Label);
            Assert.Equal(1, candidate.Weight);
        }
    }

    [Fact]
    public void StoredSingleSymbol_IsTaggedAndReinforcedButNotCreatedByDiscovery()
    {
        var module = CreateInitializedSegmenter(CreateUKS());
        Thought s = SeedPattern(module, "s", 30);
        SeedPattern(module, "cat", 30);
        SeedPattern(module, "dog", 30);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "catsdog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "catsdog");
        Assert.Equal(32, s.Weight);
        SeqElement single = (SeqElement)s.GetTargetOfFirstLinkOfType("spelled");
        SeqElement sPosition = module.InputQueueHead.NXT.NXT.NXT.NXT;
        Assert.Contains(sPosition.LinksTo, link => link.LinkType.Label == "usedBy" && ReferenceEquals(link.To, single));
        Assert.DoesNotContain(module.PatternRoot.Children, thought => thought.Label == "w:catsdog");
    }

    [Theory]
    [InlineData(3, "!", 1)]
    [InlineData(3, "!!", 1.5f)]
    [InlineData(3, "!!!", 3)]
    [InlineData(25, "!", 1)]
    [InlineData(25, "!!", 12.5f)]
    [InlineData(25, "!!!", 25)]
    [InlineData(25, "!!!!", 25)]
    public void Emphasis_ScalesCreationAndReinforcementWithConsolidation(float threshold, string marks, float emphasis)
    {
        var module = CreateInitializedSegmenter(CreateUKS());
        module.ConsolidationStrength = threshold;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "frog" + marks);
        Thought candidate = Assert.Single(module.PatternRoot.Children);
        Assert.Equal("w:frog", candidate.Label);
        Assert.Equal(module.InitialStrength + emphasis, candidate.Weight);
        Assert.Equal(candidate.Weight < threshold, candidate.isPlastic);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "frog" + marks);
        Assert.Equal(module.InitialStrength + emphasis + module.Reinforcement * emphasis, candidate.Weight);
    }

    [Fact]
    public void TwoExclamations_ReinforceEachRecognizedWordWithoutCreatingCandidates()
    {
        var module = CreateInitializedSegmenter(CreateUKS());
        module.ConsolidationStrength = 25;
        Thought the = SeedPattern(module, "the", 25);
        Thought cat = SeedPattern(module, "cat", 25);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "thecat!!");
        Assert.Equal(37.5f, the.Weight);
        Assert.Equal(37.5f, cat.Weight);
        Assert.Equal(2, module.PatternRoot.Children.Count);
    }

    [Theory]
    [InlineData("catadog", true)]
    [InlineData("acatandadog", false)]
    public void SingleLetterCandidate_RequiresExactlyOneUnrecognizedElement(string input, bool createsA)
    {
        var module = CreateInitializedSegmenter(CreateUKS());
        module.MaximumProposalsPerEpisode = 2;
        SeedPattern(module, "cat", 3);
        SeedPattern(module, "dog", 3);
        ModuleSequenceSegmenterDlg.SubmitLine(module, input);
        Assert.Equal(createsA, module.theUKS.Labeled("w:a") is not null);
        Assert.Equal(createsA ? 4 : 2, module.PatternRoot.Children.Count);
    }

    [Fact]
    public void RecognizedNeighbors_ProposeAnUnrepeatedMultiSymbolGap()
    {
        var module = CreateInitializedSegmenter(CreateUKS());
        module.ConsolidationStrength = 25;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "cat!!!");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog!!!");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "catanddog");
        Thought candidate = module.theUKS.Labeled("w:and");
        Assert.NotNull(candidate);
        Assert.True(candidate.isPlastic);
        Assert.Equal(1, candidate.Weight);
        Assert.Equal(3, module.PatternRoot.Children.Count);
    }

    [Theory]
    [InlineData("catanddog", "and", "d", 1)]
    [InlineData("catanddog", "and", "d", 2)]
    [InlineData("cats", "cats", "s", 1)]
    [InlineData("cats", "cats", "s", 2)]
    [InlineData("scat", "scat", "s", 1)]
    [InlineData("scat", "scat", "s", 2)]
    public void OrphanExpansion_AbsorbsTheShortestAdjacentMatch(string input, string expanded, string orphan, int limit)
    {
        var module = CreateInitializedSegmenter(CreateUKS());
        module.MaximumProposalsPerEpisode = limit;
        foreach (string word in new[] { "cat", "an", "dog" }) SeedPattern(module, word, 3);
        ModuleSequenceSegmenterDlg.SubmitLine(module, input);
        Thought candidate = module.theUKS.Labeled("w:" + expanded);
        Assert.NotNull(candidate);
        Assert.Equal(1, candidate.Weight);
        Assert.Equal(limit == 2, module.theUKS.Labeled("w:" + orphan) is not null);
        Assert.Equal(3 + limit, module.PatternRoot.Children.Count);
        if (input == "catanddog")
        {
            Assert.Null(module.theUKS.Labeled("w:ddog"));
            Assert.Null(module.theUKS.Labeled("w:catanddog"));
        }
        ModuleSequenceSegmenterDlg.SubmitLine(module, input);
        Assert.Equal(2, candidate.Weight);
        // Until the expansion consolidates, repetition may still propose the whole phrase.
        if (input == "catanddog") Assert.NotNull(module.theUKS.Labeled("w:catanddog"));
        Assert.Equal(3 + limit + (input == "catanddog" ? 1 : 0), module.PatternRoot.Children.Count);
    }

    [Fact]
    public void RepetitionSearch_RestoresForwardOrderAndExcludesOtherSequences()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.MaximumProposalsPerEpisode = 1;
        var reverse = ModuleSequenceSegmenterDlg.InputSymbols(module, "godgod");
        uks.AddSequence("unrelated", reverse, allowCompression: false);
        var other = CreateInitializedSegmenter(uks);
        other.QueueInput(ModuleSequenceSegmenterDlg.InputSymbols(other, "dogdog"));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        Assert.Empty(module.PatternRoot.Children);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        Assert.Equal("w:dog", Assert.Single(module.PatternRoot.Children).Label);
        Assert.Null(uks.Labeled("w:god"));
    }

    [Fact]
    public void WholePattern_InhibitsContainedComponentsButIndependentUsesCanLearn()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought cat = SeedPattern(module, "cat", 3);
        Thought dog = SeedPattern(module, "dog", 3);
        Thought a = SeedPattern(module, "an", 1);
        Thought whole = SeedPattern(module, "catandog", 1);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "catandog");
        Assert.Equal(3, cat.Weight);
        Assert.Equal(3, dog.Weight);
        Assert.Equal(1, a.Weight);
        Assert.Equal(2, whole.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "catandog");
        Assert.Equal(1, a.Weight);
        Assert.True(a.isPlastic);
        Assert.Equal(3, whole.Weight);
        Assert.Equal(3, cat.Weight);
        Assert.Equal(3, dog.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "an");
        Assert.Equal(2, a.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "an");
        Assert.Equal(3, a.Weight);
        Assert.False(a.isPlastic);
        Assert.Equal(4, module.PatternRoot.Children.Count);
    }

    [Fact]
    public void OrphanExpansion_CanOutgrowTheOriginalFragmentSplit()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.MaximumProposalsPerEpisode = 2;
        Thought fragment = SeedPattern(module, "ca", 1);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "car");
        Assert.Equal(1, uks.Labeled("w:r").Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "car");
        Assert.Equal(2, uks.Labeled("w:car").Weight); // The expansion wins reinforcement on the next occurrence.
        Assert.Equal(1, uks.Labeled("w:r").Weight);
        Assert.Equal(2, fragment.Weight);
    }

    [Fact]
    public void ContainedWords_LearnOnlyFromOccurrencesOutsideTheWholeWord()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought fragment = SeedPattern(module, "og", 1);
        var sequence = uks.AddSequence("unparented", ModuleSequenceSegmenterDlg.InputSymbols(module, "dog"), allowCompression: false);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        Assert.Same(fragment, Assert.Single(module.PatternRoot.Children));
        Assert.Equal(1, fragment.Weight); // Containment depends on complete tags, not ancestry.
        Assert.Contains(module.InputQueueHead.NXT.LinksTo, link => link.LinkType.Label == "usedBy" && ReferenceEquals(link.To, sequence));
        module.MaximumProposalsPerEpisode = 0;
        Thought every = SeedPattern(module, "every", 1);
        Thought one = SeedPattern(module, "one", 1);
        Thought everyone = SeedPattern(module, "everyone", 1);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "everyone");
        Assert.Equal(1, every.Weight);
        Assert.Equal(1, one.Weight);
        Assert.Equal(2, everyone.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "everyoneevery");
        Assert.Equal(2, every.Weight); // The trailing every is outside everyone.
        Assert.Equal(1, one.Weight);
        Assert.Equal(3, everyone.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "one");
        Assert.Equal(2, one.Weight);
        Thought ca = SeedPattern(module, "ca", 1);
        Thought t = SeedPattern(module, "t", 1);
        Thought cat = SeedPattern(module, "cat", 1);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "cat");
        Assert.Equal(1, ca.Weight); // ca|t must not bypass containment inhibition.
        Assert.Equal(1, t.Weight);
        Assert.Equal(2, cat.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "ca");
        Assert.Equal(2, ca.Weight);
    }

    [Fact]
    public void BothCases_StrengthenKnownPatternAndCreateContainingCandidate()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.MaximumProposalsPerEpisode = 1;
        Thought cat = SeedPattern(module, "cat", 1);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "catdog");
        Assert.Equal(2, cat.Weight);
        Assert.Null(uks.Labeled("w:catdog"));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "catdog");
        Assert.Equal(3, cat.Weight);
        Assert.False(cat.isPlastic);
        Assert.Equal(1, uks.Labeled("w:catdog").Weight);
        Assert.Null(uks.Labeled("w:dog"));
        Assert.Single(module.PatternRoot.Children.Where(pattern => pattern.Label == "w:cat"));
        module.MaximumProposalsPerEpisode = 0;
        Thought the = SeedPattern(module, "the", 1);
        Thought crossing = SeedPattern(module, "theca", 1);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "thecat");
        Assert.Equal(2, the.Weight);
        Assert.Equal(4, cat.Weight);
        Assert.Equal(1, crossing.Weight); // the|cat covers the extra t, so theca gets no reinforcement.
        ModuleSequenceSegmenterDlg.SubmitLine(module, "theca");
        Assert.Equal(2, crossing.Weight); // A complete independent occurrence is still allowed to learn.
        Assert.Equal(2, the.Weight);
    }

    [Fact]
    public void FullCoverage_LeavesInhibitedMatchesUnchangedAndRewardsIndependentUses()
    {
        var module = CreateInitializedSegmenter(CreateUKS());
        module.MaximumProposalsPerEpisode = 0;
        Thought dog = SeedPattern(module, "dog", 10);
        Thought cat = SeedPattern(module, "cat", 10);
        Thought crossing = SeedPattern(module, "dogca", 10);
        Thought absent = SeedPattern(module, "pig", 10);

        ModuleSequenceSegmenterDlg.SubmitLine(module, "dogcatx");
        Assert.Equal(10, crossing.Weight); // Partial coverage leaves the inhibited match unchanged.
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dogcatdogcat");
        Assert.Equal(10, crossing.Weight); // Full coverage also leaves losing occurrences unchanged.
        Assert.False(crossing.isPlastic);
        Assert.Equal(12, dog.Weight);
        Assert.Equal(12, cat.Weight);
        Assert.Equal(10, absent.Weight);

        ModuleSequenceSegmenterDlg.SubmitLine(module, "dogcatdogca");
        Assert.Equal(11, crossing.Weight); // Independent final occurrence survives and earns reinforcement.
        Assert.Equal(10, absent.Weight);
    }

    [Fact]
    public void QueueTags_RequireOneAlignedEarlierOccurrenceForEachProposal()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.MaximumProposalsPerEpisode = 10;
        module.QueueInput(WithPause(module, ModuleSequenceSegmenterDlg.InputSymbols(module, "dogcat")));
        module.QueueInput(WithPause(module, ModuleSequenceSegmenterDlg.InputSymbols(module, "catpig")));
        while (module.TryReadQueuedInput(out _)) { }
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dogcatpig");
        Assert.NotNull(uks.Labeled("w:dogcat"));
        Assert.Null(uks.Labeled("w:catpig")); // Both routes select dogcat when support and length tie.
        Assert.Null(uks.Labeled("w:dogcatpig")); // All characters have queue tags, but not from one occurrence.
        Assert.DoesNotContain(module.PatternRoot.Children, pattern => pattern.Label == "w:catp");

        var coreUKS = CreateUKS();
        var core = CreateInitializedSegmenter(coreUKS);
        core.MaximumProposalsPerEpisode = 2; // Exercise both nomination routes explicitly.
        foreach (string text in new[] { "adogcat", "bdogpig", "cdogfur" })
            core.QueueInput(WithPause(core, ModuleSequenceSegmenterDlg.InputSymbols(core, text)));
        while (core.TryReadQueuedInput(out _)) { }
        ModuleSequenceSegmenterDlg.SubmitLine(core, "zdogcar");
        Assert.Equal(2, core.PatternRoot.Children.Count);
        Assert.Equal(1, coreUKS.Labeled("w:dog").Weight); // Most frequent.
        Assert.Equal(1, coreUKS.Labeled("w:dogca").Weight); // Longest.
        Assert.Null(coreUKS.Labeled("w:zdogcar"));
        ModuleSequenceSegmenterDlg.SubmitLine(core, "zdogcar");
        Assert.Single(core.PatternRoot.Children.Where(thought => thought.Label == "w:dogca"));
    }

    [Fact]
    public void StoredWordTags_DoNotSplitAQueueSupportedCandidateRegion()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.MaximumProposalsPerEpisode = 1;
        SeedPattern(module, "cat", 3);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "uvcatxz");
        Assert.Single(module.PatternRoot.Children);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "uvcatxz");
        Assert.Equal(1, uks.Labeled("w:uvcatxz").Weight);
        Assert.Null(uks.Labeled("w:uv"));
        Assert.Null(uks.Labeled("w:xz"));
        Assert.Equal(2, module.PatternRoot.Children.Count);
    }

    [Fact]
    public void CandidateCapacity_NonPlasticPatternsLeaveASlot()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.CandidateCapacity = 1;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "fido");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "fido");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "xyz");
        Assert.Single(module.PatternRoot.Children);
        Assert.Null(uks.Labeled("w:xyz"));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "fido");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "fido");
        Assert.False(uks.Labeled("w:fido").isPlastic);
        module.MaximumProposalsPerEpisode = 1;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "xyz");
        Assert.NotNull(uks.Labeled("w:xyz"));
    }

    [Fact]
    public void Pauses_DoNotJoinEpisodesOrDuplicateKnownPatterns()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.MaximumProposalsPerEpisode = 0;
        Thought dog = SeedPattern(module, "dog", 3);
        module.InputReceived(ModuleSequenceSegmenterDlg.InputSymbols(module, "dog"));
        Assert.Equal(3, dog.Weight);
        module.InputReceived(new[] { module.InputPause, module.InputPause });
        Assert.Equal(4, dog.Weight);
        Assert.Empty(module.FindInputOccurrences(ModuleSequenceSegmenterDlg.InputSymbols(module, "gdog")));
    }

    [Fact]
    public void ExpiredQueue_DoesNotAdmitCandidates()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        module.TrimInputQueue(System.DateTime.Now.AddSeconds(6));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        Assert.Empty(module.PatternRoot.Children);
    }

    [Fact]
    public void RepeatedGenericInput_PreservesIdentityAndUsesConfiguredRootAndPrefix()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.PatternRoot = uks.GetOrAddThought("SoundPattern", "Thought");
        module.PatternPrefix = "s:";
        module.MaximumProposalsPerEpisode = 1;
        Thought pitch = uks.GetOrAddThought("pitch:G4");
        Thought duration = uks.GetOrAddThought("duration:half");
        var input = new[] { pitch, duration };
        module.InputReceived(WithPause(module, input));
        Assert.Empty(module.PatternRoot.Children);
        module.InputReceived(WithPause(module, input), 2);
        Thought candidate = Assert.Single(module.PatternRoot.Children);
        Assert.StartsWith("s:", candidate.Label);
        Assert.Equal(3, candidate.Weight);
        var stored = (SeqElement)candidate.GetTargetOfFirstLinkOfType("spelled");
        Assert.Equal(input, uks.FlattenSequence(stored));
        Assert.Empty(uks.Labeled("Word").Children);
    }

    [Fact]
    public void QueueEpisode_AllStoredMatchesAreTaggedDirectlyAndStopAtThePause()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        foreach (string text in new[] { "dog", "cat", "a", "at" }) SeedPattern(module, text, 3);
        var input = ModuleSequenceSegmenterDlg.InputSymbols(module, "dogcat");
        module.QueueInput(WithPause(module, input));
        SeqElement first = module.InputQueueHead.NXT;
        SeqElement pause = uks.GetLastlement(first);
        module.QueueInput(WithPause(module, ModuleSequenceSegmenterDlg.InputSymbols(module, "at")));
        module.FindExistingSubPatterns(first);
        SeqElement position = first;
        foreach (int count in new[] { 1, 1, 1, 1, 3, 2 })
        {
            var uses = position.LinksTo.Where(link => link.LinkType.Label == "usedBy");
            Assert.Equal(count, uses.Count());
            foreach (Link use in uses)
            {
                SeqElement sequence = Assert.IsType<SeqElement>(use.To);
                Assert.Same(sequence, sequence.FRST);
                Assert.Contains(sequence.LinksFrom, link => link.LinkType.Label == "spelled" && module.PatternRoot.Children.Contains(link.From));
            }
            position = position.NXT;
        }
        Assert.Same(pause, position);
        Assert.DoesNotContain(pause.LinksTo, link => link.LinkType.Label == "usedBy");
        Assert.DoesNotContain(pause.NXT.LinksTo, link => link.LinkType.Label == "usedBy");
        foreach (Thought value in input) Assert.DoesNotContain(value.LinksTo, link => link.LinkType.Label == "usedBy");
    }

    [Fact]
    public void PartialStoredAndAlphabetHits_DoNotBlockQueueBasedCandidateCreation()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought dog = SeedPattern(module, "dog", 3);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "og");
        Assert.DoesNotContain(module.InputQueueHead.NXT.LinksTo, link => link.LinkType.Label == "usedBy");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "og");
        Assert.Equal(1, uks.Labeled("w:og").Weight);
        Assert.Equal(3, dog.Weight);
    }

    [Fact]
    public void QueueTags_PreserveBothStoredAndEarlierQueueMatches()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought a = uks.GetOrAddCharacter('a');
        Thought b = uks.GetOrAddCharacter('b');
        Thought pattern = SeedPattern(module, "ab", 3);
        module.QueueInput(new[] { a, b, module.InputPause, a, b, module.InputPause, a, b, module.InputPause });
        SeqElement earlier = module.InputQueueHead.NXT;
        SeqElement current = earlier.NXT.NXT.NXT;
        SeqElement future = current.NXT.NXT.NXT;
        module.FindExistingSubPatterns(current);
        var uses = current.LinksTo.Where(link => link.LinkType.Label == "usedBy").ToList();
        Assert.Equal(2, uses.Count);
        Assert.DoesNotContain(uses, link => link.To.Label.StartsWith("alphabet-seq"));
        Assert.Contains(uses, link => ReferenceEquals(link.To, earlier));
        Assert.Contains(uses, link => ReferenceEquals(link.To, pattern.GetTargetOfFirstLinkOfType("spelled")));
        Assert.DoesNotContain(uses, link => ReferenceEquals(link.To, current) || ReferenceEquals(link.To, future));
        Assert.Null(earlier.FRST);
    }

    [Fact]
    public void QueueTags_UnknownSpanUsesOnlyEarlierOccurrencesAndStillFindsLongerSpans()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought a = uks.GetOrAddCharacter('a');
        Thought b = uks.GetOrAddCharacter('b');
        Thought c = uks.GetOrAddCharacter('c');
        Thought pattern = SeedPattern(module, "ab", 3);
        module.QueueInput(new[] { a, b, c, module.InputPause, a, b, c, module.InputPause, a, b, c, module.InputPause });
        SeqElement earlier = module.InputQueueHead.NXT;
        SeqElement current = earlier.NXT.NXT.NXT.NXT;
        SeqElement future = current.NXT.NXT.NXT.NXT;
        module.FindExistingSubPatterns(current);
        Assert.Contains(current.LinksTo, link => link.LinkType.Label == "usedBy" &&
            ReferenceEquals(link.To, pattern.GetTargetOfFirstLinkOfType("spelled")));
        // The longer abc span still gets historical tags despite the stored ab match.
        Assert.Contains(current.LinksTo, link => link.LinkType.Label == "usedBy" && ReferenceEquals(link.To, earlier));
        Assert.Contains(current.NXT.LinksTo, link => link.LinkType.Label == "usedBy" && ReferenceEquals(link.To, earlier.NXT));
        Assert.DoesNotContain(current.LinksTo, link => ReferenceEquals(link.To, current) || ReferenceEquals(link.To, future));
        Assert.DoesNotContain(future.LinksTo, link => link.LinkType.Label == "usedBy");
    }

    [Fact]
    public void BatchedEpisodes_DoNotUseFutureInputAsEarlierEvidence()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.MaximumProposalsPerEpisode = 1;
        var dog = ModuleSequenceSegmenterDlg.InputSymbols(module, "dog");
        var input = WithPause(module, dog);
        input.AddRange(WithPause(module, dog));
        module.InputReceived(input);
        Assert.Equal(1, uks.Labeled("w:dog").Weight);
    }

    [Fact]
    public void EpisodeProcessing_KeepsTagsOnTheOriginalQueueAndPreservesKnowledgeWhenTrimmed()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        SeqElement first = module.InputQueueHead.NXT;
        SeqElement third = first;
        for (int index = 0; index < 8; index++) third = third.NXT;
        var stored = (SeqElement)uks.Labeled("w:dog").GetTargetOfFirstLinkOfType("spelled");
        Assert.Contains(third.LinksTo, link => link.LinkType.Label == "usedBy" && ReferenceEquals(link.To, stored));
        Assert.Equal(2, uks.Labeled("w:dog").Weight);
        module.ClearInputQueue();
        Assert.Equal(ModuleSequenceSegmenterDlg.InputSymbols(module, "dog"), uks.FlattenSequence(stored));
        Assert.Same(stored, uks.Labeled(stored.Label));
    }

    [Fact]
    public void ConfiguredConsolidation_RequiresTwentyFiveAndOnlyPlasticCandidatesDecay()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.ConsolidationStrength = 25;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        for (int strength = 1; strength <= 25; strength++)
        {
            ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
            Assert.Equal(strength, uks.Labeled("w:dog").Weight);
            Assert.Equal(strength < 25, uks.Labeled("w:dog").isPlastic);
        }
        Thought candidate = SeedPattern(module, "cat", 9);
        Thought pattern = uks.Labeled("w:dog");
        module.DecayCandidates(System.TimeSpan.FromSeconds(1));
        Assert.Equal(9 - module.CandidateDecayPerSecond, candidate.Weight, 4);
        Assert.Equal(25, pattern.Weight);
        Assert.False(pattern.isPlastic);
        module.DecayCandidates(System.TimeSpan.FromSeconds(100));
        Assert.Null(uks.Labeled("w:cat"));
        Assert.Same(pattern, uks.Labeled("w:dog"));
        Assert.Equal(25, pattern.Weight);
        Assert.False(pattern.isPlastic);
    }

    [Fact]
    public void ExistingDecayPolicy_StillRunsFromFire()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.initialized = true;
        module.CandidateDecayPerSecond = 1;
        Thought candidate = SeedPattern(module, "dog", 1);
        System.Threading.Thread.Sleep(20);
        module.Fire();
        Assert.True(candidate.Weight < 1);
        module.DecayCandidates(System.TimeSpan.FromSeconds(2));
        Assert.Null(uks.Labeled("w:dog"));
    }

    private static Thought SeedPattern(ModuleSequenceSegmenter module, string text, float strength)
    {
        Thought pattern = module.theUKS.GetOrAddThought("w:" + text, module.PatternRoot);
        pattern.Weight = strength;
        pattern.isPlastic = strength < module.ConsolidationStrength;
        module.theUKS.AddSequenceAndLink(pattern, "spelled", ModuleSequenceSegmenterDlg.InputSymbols(module, text), allowCompression: false);
        return pattern;
    }

    [Fact]
    public void SeededPhraseFile_ContainsEveryWordAndCountsRecognizedOccurrencesInsteadOfRewards()
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, "Cat, cat! A dog.\nCAT-dog?\n");
            var uks = CreateUKS();
            var module = CreateInitializedSegmenter(uks);
            var expected = ModuleSequenceSegmenterDlg.SeedWordsFromFile(module, path);
            Thought cat = uks.Labeled("w:cat"), dog = uks.Labeled("w:dog"), a = uks.Labeled("w:a");
            Assert.Equal(3, expected.Count);
            Assert.Equal(3, expected[cat]);
            Assert.Equal(2, expected[dog]);
            Assert.Equal(1, expected[a]);
            foreach (Thought word in expected.Keys)
            {
                Assert.False(word.isPlastic);
                Assert.Equal(module.ConsolidationStrength, word.Weight);
                Assert.NotNull(word.GetTargetOfFirstLinkOfType("spelled"));
            }
            Thought sequence = cat.GetTargetOfFirstLinkOfType("spelled");
            ModuleSequenceSegmenterDlg.SeedWordsFromFile(module, path);
            Assert.Same(sequence, cat.GetTargetOfFirstLinkOfType("spelled"));
            Assert.Equal(3, module.PatternRoot.Children.Count);

            module.RecognitionCounts = new();
            using var reader = new ModuleSequenceSegmenterDlg.CorpusReader();
            Assert.Equal(2, reader.LoadTextFromFile(module, path));
            Assert.Equal(3, module.RecognitionCounts[cat]);
            Assert.Equal(2, module.RecognitionCounts[dog]);
            Assert.Equal(1, module.RecognitionCounts[a]); // Seeded single-letter words are recognized; embedded a occurrences are inhibited.
            Assert.Equal(module.ConsolidationStrength + 2, cat.Weight); // Two phrases, three occurrences.
            Assert.Equal(3, module.PatternRoot.Children.Count);
        }
        finally { System.IO.File.Delete(path); }
    }

    [Fact]
    public void SeededCatInTheHat_RepeatedShameLinesAreRecognizedWithoutNewCandidates()
    {
        string path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "SegmentationCorpora", "TheCatInTheHat.txt");
        foreach (int proposals in new[] { 0, 2 })
        {
            var uks = CreateUKS();
            var module = CreateInitializedSegmenter(uks);
            module.ConsolidationStrength = 25;
            module.MaximumProposalsPerEpisode = proposals;
            var source = ModuleSequenceSegmenterDlg.SeedWordsFromFile(module, path);
            module.RecognitionCounts = new();
            Assert.Equal(3, source[uks.Labeled("w:shame")]);
            foreach (string line in System.IO.File.ReadLines(path))
                if (line.Contains("shame")) ModuleSequenceSegmenterDlg.SubmitLine(module, System.Text.RegularExpressions.Regex.Replace(line, @"\p{P}", ""));
            Assert.Equal(3, module.RecognitionCounts[uks.Labeled("w:shame")]);
            Assert.Equal(source.Count, module.PatternRoot.Children.Count);
            foreach (string word in new[] { "shame", "a", "i", "s" })
            {
                Thought thought = uks.Labeled("w:" + word);
                module.RecognitionCounts.TryGetValue(thought, out int hits);
                _output.WriteLine($"Proposals {proposals}: {word}, source {source[thought]}, recognized {hits}, weight {thought.Weight}");
            }
            _output.WriteLine($"New candidates: {module.PatternRoot.Children.Count - source.Count}");
        }
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(2, 3)]
    public void RepeatedKnownPhrase_ReinforcesSeededWordsWithoutCreatingACompound(int proposals, int shameHits)
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, "what a shame!\nwhat a shame!\nwhat a shame!\nmother's\n");
            var uks = CreateUKS();
            var module = CreateInitializedSegmenter(uks);
            var expected = ModuleSequenceSegmenterDlg.SeedWordsFromFile(module, path);
            Assert.Equal(1, expected[uks.Labeled("w:s")]); // Splitting at punctuation seeds possessive suffixes too.
            Assert.Equal(3, expected[uks.Labeled("w:shame")]);
            module.MaximumProposalsPerEpisode = proposals;
            module.RecognitionCounts = new();
            using var reader = new ModuleSequenceSegmenterDlg.CorpusReader();
            Assert.Equal(3, reader.LoadTextFromFile(module, path, 3));
            Assert.Equal(shameHits, module.RecognitionCounts[uks.Labeled("w:shame")]);
            Assert.Null(uks.Labeled("w:whatashame"));
        }
        finally { System.IO.File.Delete(path); }
    }

    [Fact]
    public void FullEstablishedCoverage_RewardsAlternativeSplitsAndIgnoresTentativeCompounds()
    {
        var module = CreateInitializedSegmenter(CreateUKS());
        Thought every = SeedPattern(module, "every", 3);
        Thought one = SeedPattern(module, "one", 3);
        Thought everyone = SeedPattern(module, "everyone", 3);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "everyone");
        Assert.Equal(4, every.Weight);
        Assert.Equal(4, one.Weight);
        Assert.Equal(4, everyone.Weight);
        Thought dog = SeedPattern(module, "dog", 3);
        Thought cat = SeedPattern(module, "cat", 3);
        Thought crossing = SeedPattern(module, "dogca", 1);
        Thought compound = SeedPattern(module, "dogcat", 1);
        module.RecognitionCounts = new();
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dogcatdogcat");
        Assert.Equal(4, dog.Weight);
        Assert.Equal(4, cat.Weight);
        Assert.Equal(2, module.RecognitionCounts[dog]);
        Assert.Equal(2, module.RecognitionCounts[cat]);
        Assert.Equal(1, crossing.Weight);
        Assert.Equal(1, compound.Weight);
        Assert.Equal(7, module.PatternRoot.Children.Count);
    }

    [Theory]
    [InlineData("ab", "bc", "abc")]
    [InlineData("ca", "an", "can")]
    public void OverlappingStoredMatches_DoNotStopDiscovery(string left, string right, string input)
    {
        var module = CreateInitializedSegmenter(CreateUKS());
        SeedPattern(module, left, 100);
        SeedPattern(module, right, 187);
        ModuleSequenceSegmenterDlg.SubmitLine(module, input);
        Assert.Null(module.theUKS.Labeled("w:" + input));
        ModuleSequenceSegmenterDlg.SubmitLine(module, input);
        Assert.Equal(1, module.theUKS.Labeled("w:" + input).Weight);
    }

    [Fact]
    public void PhraseFile_PreservesLinesStripsPunctuationAndDoesNotAddEmphasis()
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, "“Birds, fly!” Wait... here?\n\n!!!\nGo-go, birds!\tmetadata");
            var uks = CreateUKS();
            var module = CreateInitializedSegmenter(uks);
            module.MaximumProposalsPerEpisode = 0;
            Thought pattern = SeedPattern(module, "birdsflywaithere", 1);
            using var reader = new ModuleSequenceSegmenterDlg.CorpusReader();
            Assert.Equal(1, reader.LoadTextFromFile(module, path, 1));
            Assert.Equal(2, pattern.Weight); // Punctuation is removed before SubmitLine can interpret emphasis.
            var expected = WithPause(module, ModuleSequenceSegmenterDlg.InputSymbols(module, "birdsflywaithere"));
            expected.Insert(0, module.InputPause);
            Assert.Equal(expected, uks.FlattenSequence(module.InputQueueHead));
            Assert.Equal(1, reader.LoadTextFromFile(module, path, 1));
            expected.AddRange(WithPause(module, ModuleSequenceSegmenterDlg.InputSymbols(module, "gogobirds")));
            Assert.Equal(expected, uks.FlattenSequence(module.InputQueueHead));
        }
        finally { System.IO.File.Delete(path); }
    }

    [Fact]
    public void FileVocabulary_MergesNormalizedWordsAndSelectsTopUsageWeights()
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, "cat\t2%\nTo\t3%\nto\t4%\ndog\t5%\nmiddle-class\t1%\n");
            var vocabulary = ModuleSequenceSegmenterDlg.ReadVocabulary(path, 2);
            Assert.Equal(2, vocabulary.Count);
            Assert.Equal("to", vocabulary[0].Key);
            Assert.Equal(7, vocabulary[0].Value);
            Assert.Equal("dog", vocabulary[1].Key);
            vocabulary = ModuleSequenceSegmenterDlg.ReadVocabulary(path, 0);
            Assert.Equal(4, vocabulary.Count);
            Assert.Equal("middleclass", vocabulary[3].Key);
        }
        finally { System.IO.File.Delete(path); }
    }

    [Fact]
    public void FileVocabulary_PlainListHasEqualWeightsAndPreservesFileOrder()
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, "zebra\n\ncat\ndog\n");
            var vocabulary = ModuleSequenceSegmenterDlg.ReadVocabulary(path, 2);
            Assert.Equal("zebra", vocabulary[0].Key);
            Assert.Equal("cat", vocabulary[1].Key);
            foreach (var entry in vocabulary) Assert.Equal(1, entry.Value);
        }
        finally { System.IO.File.Delete(path); }
    }

    [Fact]
    public void RandomPhrases_FirstWordUsesUsageWeights()
    {
        var vocabulary = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, double>>
        {
            new("a", 1), new("b", 3)
        };
        System.Random random = new(42);
        int bCount = 0;
        for (int cycle = 0; cycle < 6000; cycle++)
        {
            string phrase = ModuleSequenceSegmenterDlg.BuildRandomPhrase(vocabulary, 1, random);
            Assert.Equal(1, phrase.Length);
            foreach (char character in phrase)
            {
                Assert.True(character == 'a' || character == 'b');
                if (character == 'b') bCount++;
            }
        }
        Assert.InRange(bCount, 4350, 4650);
    }

    [Fact]
    public void RandomPhrases_AllowNonAdjacentRepeatsButNeverAdjacentRepeats()
    {
        var vocabulary = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, double>>
        {
            new("a", 1), new("b", 1000)
        };
        System.Random random = new(42);
        for (int cycle = 0; cycle < 100; cycle++)
        {
            string phrase = ModuleSequenceSegmenterDlg.BuildRandomPhrase(vocabulary, 6, random);
            Assert.True(phrase == "ababab" || phrase == "bababa");
        }
    }

    [Fact]
    public void RandomPhrases_WeightTheChoicesRemainingAfterExcludingPreviousWord()
    {
        var vocabulary = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, double>>
        {
            new("a", 9), new("b", 3), new("c", 1)
        };
        System.Random random = new(42);
        int ab = 0, ac = 0;
        for (int cycle = 0; cycle < 1000; cycle++)
        {
            string phrase = ModuleSequenceSegmenterDlg.BuildRandomPhrase(vocabulary, 6, random);
            Assert.Equal(6, phrase.Length);
            for (int index = 1; index < phrase.Length; index++)
            {
                Assert.NotEqual(phrase[index - 1], phrase[index]);
                if (phrase[index - 1] != 'a') continue;
                if (phrase[index] == 'b') ab++;
                else ac++;
            }
        }
        Assert.True(ab + ac > 1000);
        Assert.InRange((double)ab / (ab + ac), 0.70, 0.80);
    }

    [Fact]
    public void RandomPhrases_RejectSingleWordVocabularyForMultipleWords()
    {
        var vocabulary = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, double>>
        {
            new("a", 1)
        };
        Assert.Throws<System.ArgumentException>(() => ModuleSequenceSegmenterDlg.BuildRandomPhrase(vocabulary, 6, new System.Random(42)));
        Assert.Equal("a", ModuleSequenceSegmenterDlg.BuildRandomPhrase(vocabulary, 1, new System.Random(42)));
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(0.5, 10)]
    [InlineData(0, 1)]
    public void FileVocabulary_FrequencyBiasFlattensWeightsAfterTopNSelection(double power, double expected)
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, "rare 1\ncommon 100\nunselected 0.01");
            var vocabulary = ModuleSequenceSegmenterDlg.ReadVocabulary(path, 2, power);
            Assert.Equal(2, vocabulary.Count);
            Assert.Equal("common", vocabulary[0].Key);
            Assert.Equal(expected, vocabulary[0].Value);
            Assert.Equal("rare", vocabulary[1].Key);
            Assert.Equal(1, vocabulary[1].Value);
        }
        finally { System.IO.File.Delete(path); }
    }

    [Theory]
    [InlineData("-1%")]
    [InlineData("not-a-number")]
    public void FileVocabulary_RejectsInvalidUsageWeights(string weight)
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, "cat\t" + weight);
            Assert.Throws<System.FormatException>(() => ModuleSequenceSegmenterDlg.ReadVocabulary(path, 10));
        }
        finally { System.IO.File.Delete(path); }
    }

    [Fact]
    public void FileVocabulary_AttachedUsageFileSupportsTopNAndWeightedSampling()
    {
        string path = System.IO.Path.Combine(System.AppContext.BaseDirectory,
            "SegmentationCorpora", "top_5000_words_stress_test.txt");
        var vocabulary = ModuleSequenceSegmenterDlg.ReadVocabulary(path, 14);
        Assert.Equal(14, vocabulary.Count);
        Assert.Equal("the", vocabulary[0].Key);
        for (int index = 1; index < vocabulary.Count; index++)
            Assert.True(vocabulary[index - 1].Value >= vocabulary[index].Value);
        string phrase = ModuleSequenceSegmenterDlg.BuildRandomPhrase(vocabulary, 6, new System.Random(42));
        Assert.NotEmpty(phrase);
        foreach (char character in phrase) Assert.True(char.IsLetter(character));
        _output.WriteLine("Example weighted phrase: " + phrase);
    }

    private static ModuleSequenceSegmenter CreateInitializedSegmenter(UKS.UKS uks)
    {
        // Existing lifecycle regressions explicitly retain their original three-reward threshold.
        var module = new ModuleSequenceSegmenter { theUKS = uks, ConsolidationStrength = 3 };
        module.UKSInitializedNotification();
        module.PatternRoot = uks.Labeled("Word");
        module.PatternPrefix = "w:";
        return module;
    }

    private static System.Collections.Generic.List<Thought> WithPause(
        ModuleSequenceSegmenter module, System.Collections.Generic.IEnumerable<Thought> input)
    {
        System.Collections.Generic.List<Thought> observations = new(input);
        observations.Add(module.InputPause);
        return observations;
    }

    private static UKS.UKS CreateUKS()
    {
        UKS.UKS uks = new(clear: true);
        uks.CreateInitialStructure();
        return uks;
    }
}

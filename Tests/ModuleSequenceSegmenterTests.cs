using BrainSimulator.Modules;
using UKS;
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
    public void RepeatedDog_CreatesOneCandidateThenConsolidatesIt()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        Thought dog = uks.Labeled("w:dog");
        Assert.NotNull(dog);
        Assert.True(dog.isPlastic);
        Assert.Equal(1, dog.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        Assert.Same(dog, uks.Labeled("w:dog"));
        Assert.True(dog.isPlastic);
        Assert.Equal(2, dog.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        Assert.False(dog.isPlastic);
        Assert.Equal(3, dog.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        Assert.Equal(4, dog.Weight);
        Assert.Single(uks.Labeled("Word").Children);
    }

    [Fact]
    public void LongerInput_RequiresRepetitionInRecentMemory()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "elephant");
        Assert.Null(uks.Labeled("w:elephant"));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "elephant");
        Assert.True(uks.Labeled("w:elephant").isPlastic);
        Assert.Equal(1, uks.Labeled("w:elephant").Weight);
    }

    [Fact]
    public void ExpiredInput_DoesNotSupportAdmissionButExistingCandidatesStillStrengthen()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "elephant");
        module.TrimInputQueue(System.DateTime.Now.AddSeconds(6));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "elephant");
        Assert.Null(uks.Labeled("w:elephant"));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        module.TrimInputQueue(System.DateTime.Now.AddSeconds(6));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        Assert.Equal(2, uks.Labeled("w:dog").Weight);
    }

    [Fact]
    public void RepetitionWindow_CanExcludeOccurrencesStillRetainedInTheQueue()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.RepetitionWindow = System.TimeSpan.Zero;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "elephant");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "elephant");
        Assert.Null(uks.Labeled("w:elephant"));
        Assert.Equal(2, module.FindInputOccurrences(ModuleSequenceSegmenterDlg.InputSymbols(module, "elephant")).Count);
    }

    [Fact]
    public void CandidatePool_ConsolidationFreesASlot()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.CandidateCapacity = 1;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "cat");
        Assert.Null(uks.Labeled("w:cat"));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "cat");
        Assert.False(uks.Labeled("w:dog").isPlastic);
        Assert.True(uks.Labeled("w:cat").isPlastic);
    }

    [Fact]
    public void ExplicitPause_DoesNotDuplicateReinforcementOrJoinEpisodes()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        var dog = ModuleSequenceSegmenterDlg.InputSymbols(module, "dog");
        module.InputReceived(dog);
        Assert.Null(uks.Labeled("w:dog"));
        module.InputReceived(new[] { module.InputPause });
        Assert.Equal(1, uks.Labeled("w:dog").Weight);
        module.InputReceived(new[] { module.InputPause });
        Assert.Equal(1, uks.Labeled("w:dog").Weight);
    }

    [Fact]
    public void GenericThoughtInput_PreservesIdentityAndUsesTheSameLifecycle()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought note = uks.GetOrAddThought("pitch:G4");
        var input = new[] { note };
        module.InputReceived(WithPause(module, input));
        Thought candidate = Assert.Single(uks.Labeled("Word").Children);
        Assert.Equal(input, uks.FlattenSequence((SeqElement)candidate.GetTargetOfFirstLinkOfType(uks.Labeled("spelled"))));
        module.InputReceived(WithPause(module, input));
        module.InputReceived(WithPause(module, input));
        Assert.False(candidate.isPlastic);
        Assert.Single(uks.Labeled("Word").Children);
    }

    [Fact]
    public void Decay_WeakensCandidatesAndFreesTheirSlots()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.CandidateDecayPerSecond = 0.5f;
        module.LearnedDecayPerSecond = 0;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "cat");
        Thought dog = uks.Labeled("w:dog");
        Thought cat = uks.Labeled("w:cat");
        var symbols = ModuleSequenceSegmenterDlg.InputSymbols(module, "cat");
        module.DecayCandidates(System.TimeSpan.FromSeconds(1));
        Assert.Equal(0.5f, cat.Weight);
        Assert.Equal(3, dog.Weight);
        module.DecayCandidates(System.TimeSpan.FromSeconds(1));
        Assert.Null(uks.Labeled("w:cat"));
        Assert.Same(dog, Assert.Single(module.PatternRoot.Children));
        Assert.NotEmpty(module.FindInputOccurrences(symbols));
        module.CandidateCapacity = 1;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "cat");
        Thought replacement = uks.Labeled("w:cat");
        Assert.NotSame(cat, replacement);
        Assert.Equal(1, replacement.Weight);
    }

    [Fact]
    public void LearnedDecay_WeakensOnlyBelowTheThresholdThenReturnsLosersToTheCandidatePool()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.LearnedDecayPerSecond = 0.1f;
        module.DecayThreshold = 11;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog!!!");
        Thought dog = uks.Labeled("w:dog");
        dog.Weight = 10;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "x!!!");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "y!!!");
        uks.Labeled("w:x").Weight = 100;
        uks.Labeled("w:y").Weight = 90;
        module.DecayCandidates(System.TimeSpan.FromSeconds(10));
        Assert.Equal(11, module.DecayThreshold);
        Assert.Equal(100, uks.Labeled("w:x").Weight);
        Assert.Equal(90, uks.Labeled("w:y").Weight);
        Assert.InRange(dog.Weight, 3.678f, 3.680f);
        Assert.False(dog.isPlastic);
        module.DecayCandidates(System.TimeSpan.FromSeconds(20));
        Assert.InRange(dog.Weight, 0.497f, 0.499f);
        Assert.True(dog.isPlastic);
        module.DecayCandidates(System.TimeSpan.FromSeconds(20));
        Assert.Null(uks.Labeled("w:dog"));
    }

    [Theory]
    [InlineData(10, 8, 6)]
    [InlineData(100, 10, 8)]
    [InlineData(3.5f, 4, 5)]
    public void LearnedDecay_AtOrAboveThresholdLeavesPatternsUnchanged(float first, float second, float third)
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.CandidateDecayPerSecond = 0.05f;
        foreach (string text in new[] { "x", "y", "z" })
            ModuleSequenceSegmenterDlg.SubmitLine(module, text + "!!!");
        uks.Labeled("w:x").Weight = first;
        uks.Labeled("w:y").Weight = second;
        uks.Labeled("w:z").Weight = third;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "q");
        module.DecayCandidates(System.TimeSpan.FromSeconds(10));
        Assert.Equal(3.5f, module.DecayThreshold);
        Assert.Equal(first, uks.Labeled("w:x").Weight);
        Assert.Equal(second, uks.Labeled("w:y").Weight);
        Assert.Equal(third, uks.Labeled("w:z").Weight);
        Assert.Equal(0.5f, uks.Labeled("w:q").Weight);
    }

    [Fact]
    public void LearnedDecay_ThresholdPreservesWordsAndWeakensTheLowerGroup()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        foreach (string text in new[] { "x", "y", "z", "q" })
            ModuleSequenceSegmenterDlg.SubmitLine(module, text + "!!!");
        uks.Labeled("w:x").Weight = 74;
        uks.Labeled("w:y").Weight = 49.4f;
        uks.Labeled("w:z").Weight = 3.4f;
        uks.Labeled("w:q").Weight = 2.15f;
        module.DecayCandidates(System.TimeSpan.FromSeconds(10));
        Assert.Equal(3.5f, module.DecayThreshold);
        Assert.Equal(74, uks.Labeled("w:x").Weight);
        Assert.Equal(49.4f, uks.Labeled("w:y").Weight);
        Assert.InRange(uks.Labeled("w:z").Weight, 2.06f, 2.07f);
        Assert.InRange(uks.Labeled("w:q").Weight, 1.30f, 1.31f);
    }

    [Fact]
    public void LearnedDecay_BelowThresholdDecaysWithoutAPopulationGap()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog!!!");
        Thought pattern = uks.Labeled("w:dog");
        pattern.Weight = 3.4f;
        module.DecayCandidates(System.TimeSpan.FromSeconds(10));
        Assert.InRange(pattern.Weight, 2.06f, 2.07f);
        Assert.False(pattern.isPlastic);
    }

    [Fact]
    public void Decay_ThresholdAlsoProtectsStrongPlasticPatterns()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.ConsolidationStrength = 7;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog!!!");
        Thought pattern = uks.Labeled("w:dog");
        module.DecayCandidates(System.TimeSpan.FromSeconds(10));
        Assert.Equal(4, pattern.Weight);
        Assert.True(pattern.isPlastic);
    }

    [Fact]
    public void SplitCompetition_PrefersWholeCartAndStrongerHasCart()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        foreach (string text in new[] { "car", "t", "rt", "cart", "has", "hasca" })
            ModuleSequenceSegmenterDlg.SubmitLine(module, text + "!!!");
        var parts = module.FindExistingSubPatterns(ModuleSequenceSegmenterDlg.InputSymbols(module, "cart"));
        Assert.Equal("w:cart", Assert.Single(parts).pattern.Label);
        parts = module.FindExistingSubPatterns(ModuleSequenceSegmenterDlg.InputSymbols(module, "hascart"));
        Assert.Equal(2, parts.Count);
        Assert.Equal("w:has", parts[0].pattern.Label);
        Assert.Equal("w:cart", parts[1].pattern.Label);
    }

    [Fact]
    public void SplitCompetition_RewardsHasCatAndWeakensHasCaTButLeavesUnrelatedPatternsAlone()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        foreach (string text in new[] { "has", "cat", "hasca", "t", "dog" })
            ModuleSequenceSegmenterDlg.SubmitLine(module, text + "!!!");
        Thought has = uks.Labeled("w:has"), cat = uks.Labeled("w:cat");
        Thought hasca = uks.Labeled("w:hasca"), t = uks.Labeled("w:t"), dog = uks.Labeled("w:dog");
        has.Weight = 92;
        cat.Weight = 32;
        hasca.Weight = 18;
        t.Weight = 74;
        dog.Weight = 30;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "hascat");
        Assert.Equal(93, has.Weight);
        Assert.Equal(33, cat.Weight);
        Assert.Equal(17, hasca.Weight);
        Assert.Equal(74, t.Weight);
        Assert.Equal(30, dog.Weight);
    }

    [Fact]
    public void SplitCompetition_UsesOnlyOverlappingSupportAndPreservesEqualAlternatives()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        foreach (string text in new[] { "dog", "can", "dogca", "n", "fur" })
            ModuleSequenceSegmenterDlg.SubmitLine(module, text + "!!!");
        uks.Labeled("w:dog").Weight = 100;
        uks.Labeled("w:can").Weight = 100;
        uks.Labeled("w:dogca").Weight = 6;
        uks.Labeled("w:n").Weight = 6;
        uks.Labeled("w:fur").Weight = 1000;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dogcanfur");
        Assert.Equal(5, uks.Labeled("w:dogca").Weight);
        Assert.Equal(5, uks.Labeled("w:n").Weight);
        uks.Labeled("w:dog").Weight = 6;
        uks.Labeled("w:can").Weight = 6;
        uks.Labeled("w:dogca").Weight = 6;
        uks.Labeled("w:n").Weight = 6;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dogcanfur");
        Assert.Equal(6, uks.Labeled("w:dogca").Weight);
        Assert.Equal(6, uks.Labeled("w:n").Weight);
    }

    [Theory]
    [InlineData(73)]
    public void CompetitionVocabularyExperiment_ComparesFixedAndStrengthPenalties(int seed)
    {
        string[] vocabulary = { "dog", "has", "fur", "cat", "hat", "car", "a", "an", "the", "cam", "can", "cap", "cart", "camp" };
        var weighted = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, double>>();
        foreach (string text in vocabulary) weighted.Add(new(text, 1));
        foreach (float scale in new[] { 0f, 0.1f })
        {
            var uks = CreateUKS();
            var module = CreateInitializedSegmenter(uks);
            module.CompetitionStrengthScale = scale;
            module.RepetitionWindow = System.TimeSpan.FromHours(1);
            var history = new System.Collections.Generic.Queue<System.DateTime>();
            System.Random random = new(seed);
            for (int cycle = 1; cycle <= 200; cycle++)
            {
                string phrase = ModuleSequenceSegmenterDlg.BuildRandomPhrase(weighted, 6, random);
                history.Enqueue(System.DateTime.Now);
                ModuleSequenceSegmenterDlg.SubmitLine(module, phrase);
                if (history.Count > 100) module.TrimInputQueue(history.Dequeue() + module.InputQueueLifetime);
                module.DecayCandidates(System.TimeSpan.FromMilliseconds(10));
                if (cycle != 100 && cycle != 200) continue;
                int learned = 0, noise = 0, correct = 0;
                var ranked = new System.Collections.Generic.List<Thought>(module.PatternRoot.Children);
                ranked.Sort((left, right) => { int comparison = right.Weight.CompareTo(left.Weight); return comparison; });
                foreach (string text in vocabulary)
                    if (uks.Labeled("w:" + text) is Thought pattern && !pattern.isPlastic) learned++;
                foreach (Thought pattern in ranked)
                    if (System.Array.IndexOf(vocabulary, pattern.Label[2..]) < 0 && pattern.Weight >= module.DecayThreshold) noise++;
                for (int index = 0; index < System.Math.Min(14, ranked.Count); index++)
                    if (System.Array.IndexOf(vocabulary, ranked[index].Label[2..]) >= 0) correct++;
                _output.WriteLine($"seed={seed}, scale={scale}, cycles={cycle}: learned={learned}/14, top14={correct}/14, noiseAboveThreshold={noise}");
            }
        }
    }

    [Fact]
    public void Fire_DecaysCandidatesUsingElapsedTime()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.initialized = true;
        module.InitialStrength = 100;
        module.ConsolidationStrength = 200;
        module.DecayThreshold = 200;
        module.CandidateDecayPerSecond = 1;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        System.Threading.Thread.Sleep(30);
        module.Fire();
        Assert.True(uks.Labeled("w:dog").Weight < 100);
    }

    [Fact]
    public void CallerConfiguredRootAndPrefix_SupportGenericThoughtLabels()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.PatternRoot = uks.GetOrAddThought("SoundPattern", "Thought");
        module.PatternPrefix = "s:";
        Thought note = uks.GetOrAddThought("pitch:G4");
        module.InputReceived(WithPause(module, new[] { note }));
        Thought candidate = Assert.Single(module.PatternRoot.Children);
        Assert.Equal("s:pitch:G4", candidate.Label);
        Assert.Empty(uks.Labeled("Word").Children);
        module.InputReceived(WithPause(module, new[] { note }));
        Assert.Equal(2, candidate.Weight);
    }

    [Fact]
    public void LabelCollision_DoesNotMergeDifferentSequences()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought a = uks.GetOrAddThought("a"), bc = uks.GetOrAddThought("bc");
        Thought ab = uks.GetOrAddThought("ab"), c = uks.GetOrAddThought("c");
        module.InputReceived(WithPause(module, new[] { a, bc }));
        module.InputReceived(WithPause(module, new[] { ab, c }));
        Assert.Equal(2, module.PatternRoot.Children.Count);
        module.InputReceived(WithPause(module, new[] { a, bc }));
        Assert.Equal(2, uks.Labeled("w:abc").Weight);
        foreach (Thought pattern in module.PatternRoot.Children)
            if (pattern.Label != "w:abc") Assert.Equal(1, pattern.Weight);
    }

    [Fact]
    public void KnownPatterns_LeaveTheMiddleGapAsACandidate()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "cat");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dogandcat");
        Assert.Equal(1, uks.Labeled("w:dog").Weight);
        Assert.Equal(1, uks.Labeled("w:cat").Weight);
        Assert.Equal(1, uks.Labeled("w:and").Weight);
        Assert.Null(uks.Labeled("w:dogandcat"));
        var parts = module.FindExistingSubPatterns(ModuleSequenceSegmenterDlg.InputSymbols(module, "dogandcat"));
        Assert.Equal(3, parts.Count);
        Assert.Same(uks.Labeled("w:and"), parts[1].pattern);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "doghascat");
        Assert.Equal(2, uks.Labeled("w:dog").Weight);
        Assert.Equal(2, uks.Labeled("w:cat").Weight);
    }

    [Fact]
    public void FamiliarIntroduction_DoesNotPreventLearningAnUnknownName()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "mynameis");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "mynameis");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "mynameisjohn");
        Assert.Null(uks.Labeled("w:john"));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "mynameisjohn");
        Assert.Equal(1, uks.Labeled("w:john").Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "john");
        Assert.Equal(2, uks.Labeled("w:john").Weight);
    }

    [Fact]
    public void SplitSelection_ContinuesPastShortMatchesAndChoosesBestTotalCoverage()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        foreach (string text in new[] { "a", "ado", "do", "ab", "abc", "cde" })
        {
            Thought pattern = uks.GetOrAddThought("w:" + text, module.PatternRoot);
            uks.AddSequenceAndLink(pattern, "spelled", ModuleSequenceSegmenterDlg.InputSymbols(module, text), allowCompression: false);
        }
        var parts = module.FindExistingSubPatterns(ModuleSequenceSegmenterDlg.InputSymbols(module, "ado"));
        Assert.Same(uks.Labeled("w:ado"), Assert.Single(parts).pattern);
        parts = module.FindExistingSubPatterns(ModuleSequenceSegmenterDlg.InputSymbols(module, "abcde"));
        Assert.Equal(2, parts.Count);
        Assert.Same(uks.Labeled("w:ab"), parts[0].pattern);
        Assert.Same(uks.Labeled("w:cde"), parts[1].pattern);
    }

    [Fact]
    public void Segmentation_PreservesAllLeadingMiddleAndTrailingMisses()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "cat");
        var input = ModuleSequenceSegmenterDlg.InputSymbols(module, "xxdogyycatzz");
        var parts = module.FindExistingSubPatterns(input);
        Assert.Equal(5, parts.Count);
        Assert.Null(parts[0].pattern);
        Assert.Null(parts[2].pattern);
        Assert.Null(parts[4].pattern);
        System.Collections.Generic.List<Thought> reconstructed = new();
        foreach (var part in parts) reconstructed.AddRange(part.input);
        Assert.Equal(input, reconstructed);
        module.InputReceived(WithPause(module, input));
        foreach (string label in new[] { "w:xx", "w:yy", "w:zz" })
            Assert.Equal(1, uks.Labeled(label).Weight);
    }

    [Fact]
    public void RepeatedGapInOneEpisode_CreatesOneCandidateWithoutDoubleReward()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "xxdogxx");
        Assert.Equal(1, uks.Labeled("w:xx").Weight);
        Assert.Equal(2, module.PatternRoot.Children.Count);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "xx");
        Assert.Equal(2, uks.Labeled("w:xx").Weight);
    }

    [Fact]
    public void RejectedGap_DoesNotStopActivationOfLaterMatches()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.CandidateCapacity = 1;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "anddog");
        Assert.Null(uks.Labeled("w:and"));
        Assert.Equal(1, uks.Labeled("w:dog").Weight);
        Assert.Contains("w:dog", module.LastStatus);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        Assert.Equal(2, uks.Labeled("w:dog").Weight);
    }

    [Theory]
    [InlineData("dog", 1, true)]
    [InlineData("dog!", 2, true)]
    [InlineData("dog!!!  ", 4, false)]
    public void TrailingExclamationMarks_SetEmphasisWithoutEnteringTheSequence(string text, float strength, bool plastic)
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, text);
        Thought dog = uks.Labeled("w:dog");
        Assert.Equal(strength, dog.Weight);
        Assert.Equal(plastic, dog.isPlastic);
        var symbols = ModuleSequenceSegmenterDlg.InputSymbols(module, "dog");
        Assert.Equal(symbols, uks.FlattenSequence((SeqElement)dog.GetTargetOfFirstLinkOfType(uks.Labeled("spelled"))));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "cat");
        Assert.Equal(1, uks.Labeled("w:cat").Weight);
    }

    [Fact]
    public void Emphasis_AdmitsLongInputAndSubsequentPlainInputReinforcesNormally()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "elephant!");
        Assert.Equal(2, uks.Labeled("w:elephant").Weight);
        Assert.True(uks.Labeled("w:elephant").isPlastic);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "elephant");
        Assert.Equal(3, uks.Labeled("w:elephant").Weight);
        Assert.False(uks.Labeled("w:elephant").isPlastic);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "elephant!!!");
        Assert.Equal(7, uks.Labeled("w:elephant").Weight);
    }

    [Fact]
    public void Emphasis_AppliesToExistingMatchesAndUnknownGaps()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "cat");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dogandcat!");
        Assert.Equal(3, uks.Labeled("w:dog").Weight);
        Assert.Equal(2, uks.Labeled("w:and").Weight);
        Assert.Equal(3, uks.Labeled("w:cat").Weight);
        Assert.Equal(2, uks.Labeled("w:dogandcat").Weight);
    }

    [Fact]
    public void Emphasis_DoesNotExceedCandidateCapacity()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.CandidateCapacity = 1;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "elephant!!!");
        Assert.Null(uks.Labeled("w:elephant"));
        Assert.Single(module.PatternRoot.Children);
    }

    [Fact]
    public void GenericInput_CanSupplyEmphasisWithoutPunctuation()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        Thought note = uks.GetOrAddThought("pitch:G4");
        module.InputReceived(WithPause(module, new[] { note }), emphasis: 1);
        Thought pattern = Assert.Single(module.PatternRoot.Children);
        Assert.Equal(2, pattern.Weight);
        Assert.True(pattern.isPlastic);
    }

    [Fact]
    public void RepeatedPhraseEdges_ReinforceAfterBoundaryVariation()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "doghascat");
        Assert.Empty(module.PatternRoot.Children);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dogandcat");
        Assert.Equal(1, uks.Labeled("w:dog").Weight);
        Assert.Equal(1, uks.Labeled("w:cat").Weight);
        Assert.Equal(2, module.PatternRoot.Children.Count);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dogandcat");
        Assert.Equal(2, uks.Labeled("w:dog").Weight);
        Assert.Equal(2, uks.Labeled("w:cat").Weight);
        Assert.Equal(1, uks.Labeled("w:and").Weight);
    }

    [Theory]
    [InlineData("elephantxxq", "elephantzzy", "elephant")]
    [InlineData("doghasgiraffe", "catandgiraffe", "giraffe")]
    public void RepeatedEdges_AreNotLimitedToThreeInputs(string first, string second, string edge)
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, first);
        ModuleSequenceSegmenterDlg.SubmitLine(module, second);
        Thought candidate = uks.Labeled("w:" + edge);
        Assert.NotNull(candidate);
        Assert.Equal(1, candidate.Weight);
        Assert.True(candidate.isPlastic);
    }

    [Fact]
    public void RepeatedInterior_WithChangingSurroundingsBecomesACandidate()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "xxdogyy");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "zzdogqq");
        Assert.Equal("w:dog", Assert.Single(module.PatternRoot.Children).Label);
    }

    [Fact]
    public void ExpiredPhraseEdges_DoNotSupportDiscovery()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "doghascat");
        module.TrimInputQueue(System.DateTime.Now.AddSeconds(6));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dogandcat");
        Assert.Empty(module.PatternRoot.Children);
    }

    [Fact]
    public void BoundaryDiscovery_RespectsCandidateCapacity()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.CandidateCapacity = 1;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "doghascat");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "dogandcat");
        Assert.Equal("w:dog", Assert.Single(module.PatternRoot.Children).Label);
        Assert.Null(uks.Labeled("w:cat"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedFrog_IsLearnedAcrossDifferentBackgroundsEvenWithKnownLetters(bool teachAlphabet)
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        if (teachAlphabet)
            foreach (char character in "abcdefghijklmnopqrstuvwxyz")
                ModuleSequenceSegmenterDlg.SubmitLine(module, character + "!!!");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "xxfrogabc");
        Assert.Null(uks.Labeled("w:frog"));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "deffrogzz");
        Thought frog = uks.Labeled("w:frog");
        Assert.NotNull(frog);
        Assert.Equal(1, frog.Weight);
        Assert.True(frog.isPlastic);
        Assert.Null(uks.Labeled("w:fro"));
        Assert.Null(uks.Labeled("w:rog"));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "qfrogtt");
        Assert.Equal(2, frog.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "pfrogvv");
        Assert.Equal(3, frog.Weight);
        Assert.False(frog.isPlastic);
        if (teachAlphabet) Assert.False(uks.Labeled("w:r").isPlastic);
    }

    [Fact]
    public void RepeatedInterior_WithUnchangedNeighborDoesNotInferThatBoundary()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "xxafrogabc");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "defafrogzz");
        Assert.Null(uks.Labeled("w:rog"));
        Assert.Null(uks.Labeled("w:frog"));
        Assert.Equal(1, uks.Labeled("w:afrog").Weight);
    }

    [Fact]
    public void InteriorDiscovery_RespectsRepetitionWindow()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.RepetitionWindow = System.TimeSpan.Zero;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "xxfrogabc");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "deffrogzz");
        Assert.Null(uks.Labeled("w:frog"));
    }

    [Fact]
    public void InteriorDiscovery_RespectsCandidateCapacity()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.CandidateCapacity = 0;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "xxfrogabc");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "deffrogzz");
        Assert.Empty(module.PatternRoot.Children);
    }

    [Fact]
    public void RecognizedPrefix_DoesNotGainStrengthFromRepetitionOfTheLargerWord()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "ca");
        Thought fragment = uks.Labeled("w:ca");
        for (int presentation = 0; presentation < 4; presentation++)
        {
            ModuleSequenceSegmenterDlg.SubmitLine(module, "cat");
            Assert.InRange(fragment.Weight, 0.79f, 1);
            Assert.True(fragment.isPlastic);
        }
        Thought cat = uks.Labeled("w:cat");
        Assert.Equal(4, cat.Weight);
        Assert.False(cat.isPlastic);
        float previous = fragment.Weight;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "ca");
        Assert.Equal(previous + 1, fragment.Weight);
    }

    [Fact]
    public void RecognizedInteriorFragment_StillDecaysWithoutBoundaryEvidence()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.CandidateDecayPerSecond = 0.05f;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "r");
        Thought fragment = uks.Labeled("w:r");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "frog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "frog");
        Assert.Equal(1, fragment.Weight);
        module.DecayCandidates(System.TimeSpan.FromSeconds(10));
        Assert.Equal(0.5f, fragment.Weight);
    }

    [Fact]
    public void WholePausedCar_IsAdmittedDespiteAnEstablishedPrefixAndExistingRemainder()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "ca!!!");
        Thought ca = uks.Labeled("w:ca");
        ca.Weight = 3.87f;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "r");
        Thought remainder = uks.Labeled("w:r");
        remainder.Weight = 0.7f;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "car");
        Thought car = uks.Labeled("w:car");
        Assert.NotNull(car);
        Assert.Equal(1, car.Weight);
        Assert.True(car.isPlastic);
        Assert.Equal(3.87f, ca.Weight);
        Assert.Equal(0.7f, remainder.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "car");
        Assert.Equal(2, car.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "car");
        Assert.Equal(3, car.Weight);
        Assert.False(car.isPlastic);
    }

    [Fact]
    public void WholePausedInput_StillRequiresRepetitionAboveTheSingleUseLimit()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        foreach (char character in "frog")
            ModuleSequenceSegmenterDlg.SubmitLine(module, character + "!!!");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "frog");
        Assert.Null(uks.Labeled("w:frog"));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "frog");
        Assert.Equal(1, uks.Labeled("w:frog").Weight);
    }

    [Fact]
    public void WholePausedInput_AdmissionStillRespectsCandidateCapacity()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "ca");
        module.CandidateCapacity = 1;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "car");
        Assert.Null(uks.Labeled("w:car"));
        Assert.Equal(1, uks.Labeled("w:ca").Weight);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ContextSupport_PrioritizesDogOverLongerDogCa(int maximumProposals)
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.MaximumProposalsPerEpisode = maximumProposals;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "zdoghas");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "xdogcat");
        Assert.Equal(1, uks.Labeled("w:dog").Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "ydogcar");
        if (maximumProposals == 1) Assert.Null(uks.Labeled("w:dogca"));
        else Assert.Equal(1, uks.Labeled("w:dogca").Weight);
        Assert.Equal(2, uks.Labeled("w:dog").Weight);
    }

    [Fact]
    public void EmbeddedReinforcement_RewardsRepetitionAfterBoundaryVariation()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.ConsolidationStrength = 5;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "xxfrogabc");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "deffrogzz");
        Thought frog = uks.Labeled("w:frog");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "qfrogtt");
        Assert.Equal(2, frog.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "qfrogtt");
        Assert.Equal(3, frog.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "qfrogzz");
        Assert.Equal(4, frog.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "qfrogvv");
        Assert.Equal(5, frog.Weight);
        Assert.False(frog.isPlastic);
    }

    [Fact]
    public void SingleInputPattern_LearnsAcrossChangingSurroundings()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        foreach (string text in new[] { "xx", "yy", "pp", "zz", "rr", "ss", "tt", "uu" })
            ModuleSequenceSegmenterDlg.SubmitLine(module, text + "!!!");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "xxayy");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "ppazz");
        Thought candidate = uks.Labeled("w:a");
        Assert.NotNull(candidate);
        Assert.Equal(2, candidate.Weight);
        ModuleSequenceSegmenterDlg.SubmitLine(module, "rrass");
        Assert.Equal(3, candidate.Weight);
        Assert.False(candidate.isPlastic);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleInputPattern_DoesNotStrengthenInsideLargerPattern(bool established)
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        foreach (string text in new[] { "cat", "car", "hat", "has" })
            ModuleSequenceSegmenterDlg.SubmitLine(module, text + (established ? "!!!" : ""));
        ModuleSequenceSegmenterDlg.SubmitLine(module, "a");
        Thought candidate = uks.Labeled("w:a");
        for (int repeat = 0; repeat < 4; repeat++)
            foreach (string text in new[] { "xcaty", "pcarq", "rhats", "thasu" })
                ModuleSequenceSegmenterDlg.SubmitLine(module, text);
        Assert.Equal(1, candidate.Weight);
        Assert.True(candidate.isPlastic);
    }

    [Fact]
    public void ConfiguredConsolidation_CanRequireFiveRewards()
    {
        Assert.Equal(3, new ModuleSequenceSegmenter().ConsolidationStrength);
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.ConsolidationStrength = 5;
        for (int presentation = 1; presentation <= 5; presentation++)
        {
            ModuleSequenceSegmenterDlg.SubmitLine(module, "dog");
            Assert.Equal(presentation, uks.Labeled("w:dog").Weight);
            Assert.Equal(presentation < 5, uks.Labeled("w:dog").isPlastic);
        }
    }

    [Theory]
    [InlineData(17)]
    [InlineData(42)]
    [InlineData(73)]
    public void VocabularyExperiment_ReportsFixedSeedProposalComparison(int seed)
    {
        string[] vocabulary = { "dog", "has", "fur", "cat", "hat", "car", "a", "an", "the", "cam", "can", "cap", "cart", "camp" };
        foreach (int maximumProposals in new[] { 1, 3 })
        {
            var uks = CreateUKS();
            var module = CreateInitializedSegmenter(uks);
            module.ConsolidationStrength = 5;
            module.MaximumProposalsPerEpisode = maximumProposals;
            // Keep the same 100 episodes available independent of test-machine speed.
            module.RepetitionWindow = System.TimeSpan.FromHours(1);
            System.Random random = new(seed);
            for (int episode = 0; episode < 100; episode++)
            {
                string input = string.Empty;
                for (int index = 0; index < 6; index++) input += vocabulary[random.Next(vocabulary.Length)];
                ModuleSequenceSegmenterDlg.SubmitLine(module, input);
                module.DecayCandidates(System.TimeSpan.FromMilliseconds(10));
                if (episode != 19 && episode != 99) continue;
                int learned = 0, candidates = 0;
                string missing = string.Empty;
                foreach (string word in vocabulary)
                {
                    Thought pattern = uks.Labeled("w:" + word);
                    if (pattern is not null && !pattern.isPlastic) learned++;
                    else missing += word + " ";
                }
                foreach (Thought pattern in module.PatternRoot.Children)
                    if (pattern.isPlastic) candidates++;
                Assert.InRange(candidates, 0, module.CandidateCapacity);
                _output.WriteLine($"seed={seed}, proposals={maximumProposals}, episodes={episode + 1}, learned={learned}/14, plastic={candidates}, missing={missing}");
            }
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void FixedVocabularyExperiment_ReportsWordsAndCombinations(float consolidationStrength)
    {
        string[] vocabulary = { "dog", "has", "fur", "cat", "hat", "car", "a", "an", "the", "cam", "can", "cap", "cart", "camp" };
        string[] phrases =
        {
            "cap cat dog has fur hat",
            "dog car cap can the camp",
            "the cam cap cart dog cat",
            "cap car has fur dog hat",
            "dog cat has cap the can",
            "the cart dog camp has cam",
            "fur hat cap cam dog can",
            "hat fur dog cart the cat",
            "car fur the cat cap camp",
            "can hat cap cat the car",
            "camp fur dog cam cap car",
            "cart hat the camp dog can",
            "a dog has fur an hat",
            "an cat has a fur hat",
        };
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.ConsolidationStrength = consolidationStrength;
        module.RepetitionWindow = System.TimeSpan.FromHours(1);
        for (int pass = 1; pass <= 3; pass++)
        {
            foreach (string phrase in phrases)
            {
                Assert.Equal(6, phrase.Split(' ').Length);
                ModuleSequenceSegmenterDlg.SubmitLine(module, phrase);
                module.DecayCandidates(System.TimeSpan.FromMilliseconds(10));
            }
            int learned = 0, discovered = 0, plastic = 0;
            string weights = string.Empty;
            foreach (string text in vocabulary)
            {
                Thought pattern = uks.Labeled("w:" + text);
                if (pattern is null) { weights += text + ":absent; "; continue; }
                discovered++;
                if (!pattern.isPlastic) learned++;
                weights += $"{text}:{pattern.Weight:0.000}/{(pattern.isPlastic ? "plastic" : "learned")}; ";
            }
            string otherPatterns = string.Empty;
            foreach (Thought pattern in module.PatternRoot.Children)
            {
                if (pattern.isPlastic) plastic++;
                if (System.Array.IndexOf(vocabulary, pattern.Label[2..]) >= 0) continue;
                otherPatterns += $"{pattern.Label}:{pattern.Weight:0.000}/{(pattern.isPlastic ? "plastic" : "learned")}; ";
            }
            Assert.InRange(plastic, 0, module.CandidateCapacity);
            _output.WriteLine($"threshold={consolidationStrength}, episodes={pass * phrases.Length}, discovered={discovered}/14, learned={learned}/14, plastic={plastic}");
            _output.WriteLine("Vocabulary: " + weights);
            _output.WriteLine("Other patterns: " + otherPatterns);
            var ranked = new System.Collections.Generic.List<Thought>(module.PatternRoot.Children);
            ranked.Sort((left, right) =>
            {
                int comparison = right.Weight.CompareTo(left.Weight);
                return comparison;
            });
            string ranking = string.Empty;
            foreach (Thought pattern in ranked) ranking += $"{pattern.Label}:{pattern.Weight:0.000}; ";
            _output.WriteLine("Ranked patterns: " + ranking);
        }
    }

    [Fact]
    public void FixedDogContexts_ReusedNeighborsAllowConsolidation()
    {
        var uks = CreateUKS();
        var module = CreateInitializedSegmenter(uks);
        module.ConsolidationStrength = 5;
        ModuleSequenceSegmenterDlg.SubmitLine(module, "fur dog cat");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "hat dog has");
        ModuleSequenceSegmenterDlg.SubmitLine(module, "cam dog the");
        Thought dog = uks.Labeled("w:dog");
        Assert.NotNull(dog);
        Assert.Equal(2, dog.Weight);
        for (int presentation = 0; presentation < 4; presentation++)
            ModuleSequenceSegmenterDlg.SubmitLine(module, "fur dog has");
        Assert.Equal(5, dog.Weight);
        Assert.False(dog.isPlastic);
        _output.WriteLine("dog reaches strength 5: boundary variation supports rewards when familiar neighbors recur.");
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

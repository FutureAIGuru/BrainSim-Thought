using System;
using System.IO;
using System.Linq;
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
    public ModuleSequenceSegmenterTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("a cat", "the cat", "cat", "[the] cat")]
    [InlineData("thecat", "acat", "cat", "[a] cat")]
    [InlineData("cat runs", "cat walked", "cat", "cat [walked]")]
    [InlineData("a elephant", "the elephant", "elephant", "[the] elephant")]
    public void DifferentContexts_DiscoverLongestSharedEdge(
        string first, string second, string word, string segmentation)
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        module.SubmitLine(first);
        Assert.DoesNotContain(uks.Labeled("CandidateSequence").Children,
            candidate => Evidence(candidate, ModuleSequenceSegmenter.MaximalEvidence) > 0);
        module.SubmitLine(second);
        Thought winner = uks.Labeled("candidate:" + word);
        Assert.True(winner.Weight >= ModuleSequenceSegmenter.RecognitionThreshold);
        Assert.True(winner.isPlastic); // Recognition does not freeze learning.
        Assert.Equal(1, Evidence(winner, ModuleSequenceSegmenter.MaximalEvidence));
        Assert.Equal(segmentation, module.LastSegmentation);
        Assert.Contains("varying inward context", module.LastTrace);
    }

    [Fact]
    public void CatWins_WhileItsSuffixesLoseWeight()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        module.SubmitLine("acat");
        float atBefore = uks.Labeled("candidate:at").Weight;
        float tBefore = uks.Labeled("candidate:t").Weight;
        module.SubmitLine("thecat");
        Assert.True(uks.Labeled("candidate:at").Weight < atBefore);
        Assert.True(uks.Labeled("candidate:t").Weight < tBefore);
        Assert.Equal(0, Evidence(uks.Labeled("candidate:at"), ModuleSequenceSegmenter.MaximalEvidence));
        Assert.Equal(0, Evidence(uks.Labeled("candidate:t"), ModuleSequenceSegmenter.MaximalEvidence));
        for (int i = 0; i < 50; i++) module.SubmitLine(i % 2 == 0 ? "acat" : "thecat");
        foreach (string fragment in new[] { "at", "t" })
        {
            Thought candidate = uks.Labeled("candidate:" + fragment);
            // Edge hypotheses can be recreated after forgetting, but never win.
            if (candidate is not null)
            {
                Assert.True(candidate.Weight < ModuleSequenceSegmenter.RecognitionThreshold);
                Assert.Equal(0, Evidence(candidate, ModuleSequenceSegmenter.MaximalEvidence));
            }
        }
        Assert.Contains("cat", module.LastSegmentation);
        Assert.All(uks.Labeled("CandidateSequence").Children, candidate =>
            Assert.Equal(candidate.Label["candidate:".Length..], string.Concat(
                uks.FlattenSequence((SeqElement)candidate.GetTargetOfFirstLinkOfType("hasSymbols"))
                    .Select(symbol => symbol.Label[UKS.UKS.CharacterPrefix.Length..].ToLowerInvariant()))));
    }

    [Fact]
    public void SharedInflection_IsAnExplicitAmbiguityOfTheBoundaryRule()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        module.SubmitLine("catruns");
        module.SubmitLine("catsleeps");
        // These observations alone do not distinguish a suffix from a word.
        Assert.Equal("cat s [leep] s", module.LastSegmentation);
    }

    [Fact]
    public void IndependentlySupportedShortWord_CanAlsoSurvive()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        module.SubmitLine("acat");
        module.SubmitLine("thecat");
        module.SubmitLine("lookat");
        module.SubmitLine("stayat");
        Assert.True(Evidence(uks.Labeled("candidate:at"), ModuleSequenceSegmenter.MaximalEvidence) > 0);
        Assert.EndsWith(" at", module.LastSegmentation);
        Assert.True(uks.Labeled("candidate:cat").Weight >= ModuleSequenceSegmenter.RecognitionThreshold);
    }

    [Fact]
    public void RepeatedWholeInput_ReceivesOneRewardAndDoesNotRewardFragments()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        module.SubmitLine("cat");
        float before = uks.Labeled("candidate:cat").Weight;
        module.SubmitLine("cat");
        Assert.Equal(before + ModuleSequenceSegmenter.RecognitionIncrement,
            uks.Labeled("candidate:cat").Weight, 3);
        Assert.Equal(1, Evidence(uks.Labeled("candidate:cat"), ModuleSequenceSegmenter.MaximalEvidence));
        Assert.Equal("cat", module.LastSegmentation);
        foreach (string fragment in new[] { "c", "ca", "at", "t" })
            Assert.Equal(0, Evidence(uks.Labeled("candidate:" + fragment), ModuleSequenceSegmenter.MaximalEvidence));
    }

    [Fact]
    public void NewContext_CorrectsEarlierWholePhraseGuess()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        module.SubmitLine("acat");
        module.SubmitLine("acat");
        module.SubmitLine("thecat");
        float catBefore = uks.Labeled("candidate:cat").Weight;
        module.SubmitLine("acat");
        Assert.True(uks.Labeled("candidate:cat").Weight > catBefore);
        Assert.Contains("end: winner candidate:cat", module.LastTrace);
    }

    [Fact]
    public void WholeInputPause_IsARealContrastingContext()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        module.SubmitLine("cat");
        module.SubmitLine("thecat");
        Assert.Equal("[the] cat", module.LastSegmentation);
    }

    [Fact]
    public void LengthCap_DoesNotInventAPauseOrWordBoundary()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks, MaximumChunkSize = 3 };
        module.SubmitLine("abcdefgh");
        module.SubmitLine("abcdefgh");
        Assert.All(uks.Labeled("CandidateSequence").Children, candidate =>
            Assert.Equal(0, Evidence(candidate, ModuleSequenceSegmenter.MaximalEvidence)));
        Assert.Equal("[abcdefgh]", module.LastSegmentation);
        Assert.Null(uks.Labeled("candidate:abcd"));
    }

    [Fact]
    public void BoundaryContexts_PersistAcrossModuleRecreation()
    {
        var uks = CreateUKS();
        new ModuleSequenceSegmenter { theUKS = uks }.SubmitLine("acat");
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        module.UKSInitializedNotification();
        module.SubmitLine("thecat");
        Assert.Equal("[the] cat", module.LastSegmentation);
        Assert.Equal(2, uks.Labeled("candidate:cat").LinksTo.Count(link =>
            link.LinkType.Label == ModuleSequenceSegmenter.EndContextLinkType));
    }

    [Fact]
    public void BoundaryContexts_SurviveXmlRoundTrip()
    {
        var uks = CreateUKS();
        new ModuleSequenceSegmenter { theUKS = uks }.SubmitLine("acat");
        string path = Path.GetTempFileName();
        try
        {
            Assert.True(uks.SaveUKStoXMLFile(path));
            var restored = CreateUKS();
            Assert.True(restored.LoadUKSfromXMLFile(path));
            var module = new ModuleSequenceSegmenter { theUKS = restored };
            module.SubmitLine("thecat");
            Assert.Equal("[the] cat", module.LastSegmentation);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InteriorRecognition_UsesLearnedPlasticCandidatesWithoutCreatingInteriorWords()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        module.SubmitLine("acat");
        module.SubmitLine("thecat");
        module.SubmitLine("xxcatyy");
        Assert.Equal("[xx] cat [yy]", module.LastSegmentation);
        Assert.Null(uks.Labeled("candidate:xc"));
        Assert.Null(uks.Labeled("candidate:aty"));
        Assert.True(uks.Labeled("candidate:cat").isPlastic);
    }

    [Fact]
    public void HighLegacyWeight_AloneDoesNotEstablishAWord()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        module.SubmitLine("cat");
        uks.Labeled("candidate:cat").Weight = 100;
        module.SubmitLine("xxcatyy");
        Assert.Equal("[xxcatyy]", module.LastSegmentation);
    }

    [Fact]
    public void UnrewardedCandidates_DecayAndAreDeleted()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        module.SubmitLine("cat");
        module.SubmitLine("cat");
        for (int i = 0; i < 45; i++) module.SubmitLine("z");
        Assert.Null(uks.Labeled("candidate:cat"));
        Assert.NotNull(uks.Labeled("candidate:z"));
    }

    [Fact]
    public void SeparateInputs_NeverCreateCrossPauseCandidates()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        module.SubmitLine("ab");
        module.SubmitLine("cd");
        Assert.Null(uks.Labeled("candidate:bc"));
        Assert.Null(uks.Labeled("candidate:abcd"));
        Assert.Null(uks.Labeled("UnsegmentedUtterance"));
    }

    [Fact]
    public void EmptyInput_DoesNotDecayLearning()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        module.SubmitLine("cat");
        float before = uks.Labeled("candidate:cat").Weight;
        module.SubmitLine("123 !");
        Assert.Equal(before, uks.Labeled("candidate:cat").Weight);
    }

    [Fact]
    public void CorpusLoader_StripsMetadataAndUsesOrdinaryLearning()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, new[] { "a cat\tzzz", "the cat\tzzz" });
            Assert.Equal(2, module.LoadTextFromFile(path));
            Assert.Equal("[the] cat", module.LastSegmentation);
            Assert.Null(uks.Labeled("candidate:z"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(17)]
    [InlineData(42)]
    [InlineData(73)]
    public void SeededVocabularyExperiment_ConvergesAfterRepeatedExposure(int seed)
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        string[] vocabulary = { "dog", "has", "fur", "cat", "hat", "car",
            "a", "an", "the", "cam", "can", "cap", "cart", "camp" };
        Random training = new(seed);
        for (int sequence = 0; sequence < 150; sequence++)
            module.SubmitLine(string.Concat(Enumerable.Range(0, 6)
                .Select(_ => vocabulary[training.Next(vocabulary.Length)])));
        foreach (string word in vocabulary)
        {
            Thought candidate = uks.Labeled("candidate:" + word);
            Assert.NotNull(candidate);
            Assert.True(candidate.Weight >= ModuleSequenceSegmenter.RecognitionThreshold, word);
            Assert.True(Evidence(candidate, ModuleSequenceSegmenter.MaximalEvidence) > 0, word);
        }
        var weights = uks.Labeled("CandidateSequence").Children.ToDictionary(c => c, c => c.Weight);
        Random evaluation = new(seed + 20000);
        int exact = 0, correct = 0, predicted = 0, actual = 0;
        for (int line = 0; line < 200; line++)
        {
            string[] words = Enumerable.Range(0, 6).Select(_ => vocabulary[evaluation.Next(vocabulary.Length)]).ToArray();
            string result = module.RecognizeLine(string.Concat(words));
            if (result == string.Join(" ", words)) exact++;
            var expected = InternalBoundaries(words);
            var found = InternalBoundaries(result.Replace("[", "").Replace("]", "").Split(' '));
            correct += expected.Intersect(found).Count();
            predicted += found.Count;
            actual += expected.Count;
        }
        _output.WriteLine($"Boundary learner seed={seed}: exact={exact}/200; F1={2.0 * correct / (predicted + actual):0.000}; candidates={weights.Count}");
        Assert.InRange(exact, 190, 200);
        Assert.All(weights, pair => Assert.Equal(pair.Value, pair.Key.Weight));
    }

    private static float Evidence(Thought candidate, string source) =>
        candidate.LinksTo.FirstOrDefault(link =>
            link.LinkType?.Label == ModuleSequenceSegmenter.EvidenceLinkType &&
            link.To?.Label == source)?.Weight ?? 0;

    private static Thought TeachWord(ModuleSequenceSegmenter module, string word)
    {
        for (int i = 0; i < 5; i++) module.SubmitLine(word);
        return module.theUKS.Labeled("candidate:" + word);
    }

    [Fact]
    public void EstablishedWords_SolidifyNewEdgeWordAfterOnePhrase()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        foreach (string word in new[] { "fido", "plays", "fetch" }) TeachWord(module, word);
        module.SubmitLine("fidoplaysfetch");
        Assert.Equal("fido plays fetch", module.LastSegmentation);
        Assert.Null(uks.Labeled("candidate:rover"));

        module.SubmitLine("roverplaysfetch");
        Thought rover = uks.Labeled("candidate:rover");
        Assert.NotNull(rover);
        Assert.Equal(1, Evidence(rover, ModuleSequenceSegmenter.InferredBoundaryEvidence));
        Assert.Equal("rover plays fetch", module.LastSegmentation);
        Assert.Equal("rover plays fetch", module.RecognizeLine("roverplaysfetch"));
        Assert.Contains("start rover: inferred", module.LastTrace);

        module.SubmitLine("fidoplaysball");
        Assert.Equal("fido plays ball", module.LastSegmentation);
        Assert.Equal(1, Evidence(uks.Labeled("candidate:ball"),
            ModuleSequenceSegmenter.InferredBoundaryEvidence));

        string path = Path.GetTempFileName();
        try
        {
            Assert.True(uks.SaveUKStoXMLFile(path));
            var restored = CreateUKS();
            Assert.True(restored.LoadUKSfromXMLFile(path));
            var reloadedModule = new ModuleSequenceSegmenter { theUKS = restored };
            reloadedModule.SubmitLine("spotplaysfetch");
            Assert.Equal("spot plays fetch", reloadedModule.LastSegmentation);
            Assert.Equal(1, Evidence(restored.Labeled("candidate:spot"),
                ModuleSequenceSegmenter.InferredBoundaryEvidence));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CompleteSplit_SkipsDiscoveryReinforcesWordsAndForgetsLosers()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        Thought cat = TeachWord(module, "cat");
        Thought dog = TeachWord(module, "dog");
        uks.Labeled("candidate:at").Delete();
        var before = uks.Labeled("CandidateSequence").Children.ToHashSet();
        float catWeight = cat.Weight, dogWeight = dog.Weight;
        float wins = Evidence(cat, ModuleSequenceSegmenter.MaximalEvidence);
        for (int i = 0; i < 25; i++) module.SubmitLine("catdogcat");
        Assert.Equal("cat dog cat", module.LastSegmentation);
        Assert.Contains("candidate discovery skipped", module.LastTrace);
        Assert.Null(uks.Labeled("candidate:catdog"));
        Assert.Null(uks.Labeled("candidate:at"));
        Assert.Null(uks.Labeled("candidate:ca"));
        Assert.All(uks.Labeled("CandidateSequence").Children, candidate => Assert.Contains(candidate, before));
        Assert.Equal(catWeight + 25, cat.Weight, 3); // Once per candidate per input.
        Assert.Equal(dogWeight + 25, dog.Weight, 3);
        Assert.Equal(wins, Evidence(cat, ModuleSequenceSegmenter.MaximalEvidence));
    }

    [Fact]
    public void WholePhraseSearch_AvoidsGreedyDeadEnd()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        Thought ab = TeachWord(module, "ab");
        TeachWord(module, "abc");
        TeachWord(module, "de");
        ab.Weight = 100; // Greedy recognition would strand c.
        Assert.Equal("abc de", module.RecognizeLine("abcde"));
        module.SubmitLine("abcde");
        Assert.Equal("abc de", module.LastSegmentation);
        Assert.Contains("candidate discovery skipped", module.LastTrace);
        Assert.Null(uks.Labeled("candidate:abcde"));
    }

    [Fact]
    public void ProvisionalCoverage_DoesNotDisableDiscovery()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        module.SubmitLine("cat");
        module.SubmitLine("cat");
        module.SubmitLine("catcat");
        Assert.NotNull(uks.Labeled("candidate:catcat"));
        Assert.Contains("boundary discovery enabled", module.LastTrace);
    }

    [Fact]
    public void LetterCoverage_DoesNotDisableDiscovery()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        TeachWord(module, "a");
        TeachWord(module, "b");
        module.SubmitLine("abba");
        Assert.NotNull(uks.Labeled("candidate:abba"));
        Assert.Contains("letters-only", module.LastTrace);
    }

    [Fact]
    public void EquallySupportedCompleteSplits_KeepDiscoveryEnabled()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        foreach (string word in new[] { "ab", "cd", "abc", "d" }) TeachWord(module, word);
        foreach (string word in new[] { "ab", "cd", "abc", "d" }) uks.Labeled("candidate:" + word).Weight = 10;
        module.SubmitLine("abcd");
        Assert.NotNull(uks.Labeled("candidate:abcd"));
        Assert.Contains("Alternative:", module.LastTrace);
        Assert.Contains("boundary discovery enabled", module.LastTrace);
    }

    [Fact]
    public void IncompleteInput_StillCreatesBoundaryHypotheses()
    {
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        TeachWord(module, "cat");
        module.SubmitLine("catz");
        Assert.NotNull(uks.Labeled("candidate:catz"));
        Assert.Contains("No complete split", module.LastTrace);
    }

    private static UKS.UKS CreateUKS()
    {
        UKS.UKS uks = new(clear: true);
        uks.CreateInitialStructure();
        return uks;
    }
}

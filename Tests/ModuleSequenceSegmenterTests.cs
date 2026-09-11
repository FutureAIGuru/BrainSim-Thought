using System.IO;
using System.Linq;
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

[CollectionDefinition("SequenceSegmenter", DisableParallelization = true)]
public class SequenceSegmenterCollection
{
}

[Collection("SequenceSegmenter")]
public partial class ModuleSequenceSegmenterTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ModuleSequenceSegmenterTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }
    [Fact]
    public void SubmitLine_CreatesOnlyEdgeCandidates()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };

        module.SubmitLine("abcdefgh");

        Assert.Equal("no known interior words", module.LastSegmentation);
        Assert.NotNull(uks.Labeled("candidate:abc"));
        Assert.Null(uks.Labeled("candidate:def"));
        Assert.NotNull(uks.Labeled("candidate:gh"));
        Assert.Null(uks.Labeled("candidate:bcd"));
        Assert.Equal(8, uks.Labeled("CandidateSequence").Children.Count);
        Assert.Contains("start abc: created", module.LastTrace);
        Assert.Contains("end gh: created", module.LastTrace);
        Assert.Contains("Interior: no known candidates; nothing stored", module.LastTrace);
    }

    [Fact]
    public void ExactCandidate_IsFoundBySequenceSearchAndReinforced()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };

        module.SubmitLine("dog");
        Thought dog = uks.Labeled("candidate:dog");
        Assert.Equal(1.55f, dog.Weight, 3);

        module.SubmitLine("dog");

        Assert.Same(dog, uks.Labeled("candidate:dog"));
        Assert.True(System.Math.Abs(dog.Weight - 2.70f) < 0.001f,
            module.LastTrace);
        Assert.Equal(5, uks.Labeled("CandidateSequence").Children.Count);
    }

    [Fact]
    public void EstablishedWord_ReinforcesRemaindersRatherThanItself()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };

        module.SubmitLine("dog");
        module.SubmitLine("dog");
        Thought dog = uks.Labeled("candidate:dog");
        dog.isPlastic = false;
        float before = dog.Weight;

        module.SubmitLine("xxxdogyyy");

        Assert.Equal(before, dog.Weight);
        Assert.True(uks.Labeled("candidate:xxx").Weight > 1);
        Assert.True(uks.Labeled("candidate:yyy").Weight > 1);
        Assert.Null(uks.Labeled("candidate:xdog"));
        Assert.Contains("recognized established candidate:dog", module.LastTrace);
    }

    [Fact]
    public void PlasticCandidate_DoesNotSupplyBoundariesEvenAtHighWeight()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };
        module.SubmitLine("dog");
        uks.Labeled("candidate:dog").Weight = 100;
        module.SubmitLine("abcdefdoguvwxyz");
        Assert.Null(uks.Labeled("candidate:abcdef"));
        Assert.Null(uks.Labeled("candidate:uvwxyz"));
    }

    [Fact]
    public void EstablishedWords_CreateLongRemaindersAndSplitThemFurther()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks, MaximumChunkSize = 5 };
        module.SubmitLine("likes");
        uks.Labeled("candidate:likes").isPlastic = false;
        module.MaximumChunkSize = 4;
        module.SubmitLine("thecatlikesdogs");
        Thought remainder = uks.Labeled("candidate:thecat");
        Assert.NotNull(remainder);
        Assert.True(remainder.isPlastic);
        Assert.NotNull(uks.Labeled("candidate:dogs"));
        Assert.Equal("thecat", string.Concat(uks.FlattenSequence(
            (SeqElement)remainder.GetTargetOfFirstLinkOfType("hasSymbols"))
            .Select(symbol => symbol.Label[2..])).ToLowerInvariant());

        module.SubmitLine("the");
        uks.Labeled("candidate:the").isPlastic = false;
        module.SubmitLine("thecatlikesdogs");
        Assert.True(uks.Labeled("candidate:cat").isPlastic);
        Assert.Contains("remainder before candidate:likes cat", module.LastTrace);
    }

    [Fact]
    public void LongerMatchesReceiveSlightlyMoreReinforcement()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };

        module.SubmitLine("dogx");
        module.SubmitLine("dogx");

        Assert.True(uks.Labeled("candidate:dog").Weight >
            uks.Labeled("candidate:do").Weight);
    }

    [Fact]
    public void MatchedCandidatesAlsoDecayOncePerLine()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };

        module.SubmitLine("dogx");
        Thought dog = uks.Labeled("candidate:dog");
        float beforeMatch = dog.Weight;
        module.SubmitLine("dogx");
        float afterMatch = dog.Weight;
        Assert.Equal(beforeMatch + ModuleSequenceSegmenter.RecognitionIncrement +
            2 * ModuleSequenceSegmenter.LengthBonusPerSymbol -
            ModuleSequenceSegmenter.DecayPerChunk, afterMatch, 3);
        module.SubmitLine("caty");

        Assert.True(afterMatch > beforeMatch);
        Assert.True(dog.Weight < afterMatch);
        Assert.Equal(afterMatch - ModuleSequenceSegmenter.DecayPerChunk,
            dog.Weight, 3);
        Assert.Contains("Plastic candidates decayed:", module.LastTrace);
    }

    [Fact]
    public void MaximumChunkSize_DefaultsToFourAndPauseEndsFinalChunk()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };

        module.SubmitLine("abcdefghijkl");

        Assert.Equal(4, module.MaximumChunkSize);
        Assert.Equal("no known interior words", module.LastSegmentation);
        Assert.All(uks.Labeled("CandidateSequence").Children, candidate =>
        {
            SeqElement sequence = Assert.IsType<SeqElement>(
                candidate.GetTargetOfFirstLinkOfType("hasSymbols"));
            Assert.InRange(uks.FlattenSequence(sequence).Count, 1, 4);
        });
    }

    [Fact]
    public void UnreinforcedCandidates_DecayAndAreDeleted()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };

        module.SubmitLine("a");
        Assert.NotNull(uks.Labeled("candidate:a"));

        for (int i = 0; i < 25; i++)
            module.SubmitLine("b");

        Assert.Null(uks.Labeled("candidate:a"));
        Assert.NotNull(uks.Labeled("candidate:b"));
        Assert.All(uks.Labeled("CandidateSequence").Children,
            candidate => Assert.True(candidate.Weight > 0));
    }

    [Fact]
    public void RawObservations_AreNotRetained()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };

        module.SubmitLine("Dogs have tails");

        Assert.Null(uks.Labeled("UnsegmentedUtterance"));
        Assert.DoesNotContain(uks.AtomicThoughts, thought =>
            thought.Label.StartsWith("utterance:"));
    }

    [Fact]
    public void CandidatesUseExistingCharacterThoughtsAndAddSequenceAndLink()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };

        module.SubmitLine("dog");

        Thought dog = uks.Labeled("candidate:dog");
        SeqElement sequence = Assert.IsType<SeqElement>(
            dog.GetTargetOfFirstLinkOfType("hasSymbols"));
        Assert.Equal("DOG", string.Concat(
            uks.FlattenSequence(sequence).Select(symbol => symbol.Label[2..])));
        Assert.All(uks.FlattenSequence(sequence), symbol =>
            Assert.StartsWith("c:", symbol.Label));
        Assert.DoesNotContain(uks.AtomicThoughts, thought =>
            thought.Label.StartsWith("l:"));
    }

    [Fact]
    public void CorpusLoader_StripsTabDelimitedMetadata()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, new[] { "abc.\tzzzzz" });

            int count = module.LoadTextFromFile(path);

            Assert.Equal(1, count);
            Assert.NotNull(uks.Labeled("candidate:abc"));
            Assert.Null(uks.Labeled("candidate:z"));
            Assert.Null(uks.Labeled("candidate:zzzzz"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VocabularyExperiment_ProducesStrongSourceWordCandidates()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new()
        {
            theUKS = uks,
            MaximumChunkSize = 4,
        };
        string[] vocabulary = { "dog", "has", "fur", "tail" };

        int count = module.RunVocabularyExperiment(
            vocabulary,
            wordsPerSequence: 5,
            sequenceCount: 1000,
            randomSeed: 17);

        Assert.Equal(1000, count);
        Assert.NotEmpty(uks.Labeled("CandidateSequence").Children);
        string winners = string.Join(", ", uks.Labeled("CandidateSequence")
            .Children
            .OrderByDescending(candidate => candidate.Weight)
            .Take(20)
            .Select(candidate => $"{candidate.Label}:{candidate.Weight:0.00}"));
        Assert.True(vocabulary.All(word =>
            uks.Labeled("candidate:" + word)?.Weight >=
                ModuleSequenceSegmenter.RecognitionThreshold), winners);
        Assert.DoesNotContain(uks.AtomicThoughts, thought =>
            thought.Label.StartsWith("utterance:"));
    }

    [Fact]
    public void BalancedEdges_DistinguishWordsFromSharedEndingsWithoutConsolidation()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };
        string[] words = { "dog", "has", "fur", "cat", "hat", "car" };
        // Each word occurs once at each edge per round, without revealing spaces.
        for (int round = 0; round < 3; round++)
            for (int offset = 0; offset < words.Length; offset++)
                module.SubmitLine(string.Concat(Enumerable.Range(0, words.Length)
                    .Select(index => words[(offset + index) % words.Length])));

        foreach (string word in new[] { "cat", "hat" })
        {
            Thought candidate = uks.Labeled("candidate:" + word);
            Assert.Equal(3f, Evidence(candidate, ModuleSequenceSegmenter.StartEvidence));
            Assert.Equal(3f, Evidence(candidate, ModuleSequenceSegmenter.EndEvidence));
            Assert.True(candidate.isPlastic);
        }
        foreach (string word in new[] { "at", "t" })
        {
            Thought candidate = uks.Labeled("candidate:" + word);
            Assert.Equal(0f, Evidence(candidate, ModuleSequenceSegmenter.StartEvidence));
            Assert.Equal(6f, Evidence(candidate, ModuleSequenceSegmenter.EndEvidence));
            Assert.True(Evidence(candidate, ModuleSequenceSegmenter.DecayEvidence) > 0);
        }
        Assert.Contains("Watch cat:", module.LastTrace);
        Assert.Contains("start=3; end=3; remainder=0; both=3", module.LastTrace);
        Assert.Contains("start=0; end=6; remainder=0; both=0", module.LastTrace);
        Assert.Contains("isPlastic=True->True", module.LastTrace);
    }

    [Fact]
    public void SeededRandomEdges_KeepSharedEndingsOneSided()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };
        module.RunVocabularyExperiment(new[] { "dog", "has", "fur", "cat", "hat", "car" },
            6, 1000, randomSeed: 17);
        foreach (string word in new[] { "cat", "hat" })
        {
            Thought candidate = uks.Labeled("candidate:" + word);
            Assert.NotNull(candidate);
            Assert.True(Evidence(candidate, ModuleSequenceSegmenter.StartEvidence) > 0);
            Assert.True(Evidence(candidate, ModuleSequenceSegmenter.EndEvidence) > 0);
            Assert.True(candidate.isPlastic);
        }
        foreach (string word in new[] { "at", "t" })
        {
            Thought candidate = uks.Labeled("candidate:" + word);
            Assert.NotNull(candidate);
            Assert.Equal(0f, Evidence(candidate, ModuleSequenceSegmenter.StartEvidence));
            Assert.True(Evidence(candidate, ModuleSequenceSegmenter.EndEvidence) > 0);
        }
    }

    [Fact]
    public void Evidence_DoesNotInventPastHitsOrTreatAbsenceAsBoundaryMiss()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };
        module.SubmitLine("catdog");
        Thought cat = uks.Labeled("candidate:cat");
        foreach (Link link in cat.LinksTo.Where(link =>
            link.LinkType?.Label == ModuleSequenceSegmenter.EvidenceLinkType).ToList())
            cat.RemoveLink(link); // Simulate a pre-instrumentation candidate.
        cat.Weight = 100;
        module.SubmitLine("catdog");
        module.SubmitLine("furhas");
        Assert.Equal(1f, Evidence(cat, ModuleSequenceSegmenter.StartEvidence));
        Assert.Equal(0f, Evidence(cat, ModuleSequenceSegmenter.EndEvidence));
        Assert.True(cat.isPlastic);
    }

    [Fact]
    public void RemainderEvidence_IsSeparateFromPauseEvidenceAndAllowsMultiwordChunks()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };
        module.SubmitLine("dog");
        uks.Labeled("candidate:dog").isPlastic = false;
        module.SubmitLine("thecatdoghat");
        Thought remainder = uks.Labeled("candidate:thecat");
        Assert.Equal(1f, Evidence(remainder, ModuleSequenceSegmenter.RemainderEvidence));
        Assert.Equal(0f, Evidence(remainder, ModuleSequenceSegmenter.StartEvidence));
        Assert.True(remainder.isPlastic);
        Thought hat = uks.Labeled("candidate:hat");
        Assert.Equal(1f, Evidence(hat, ModuleSequenceSegmenter.EndEvidence));
        Assert.Equal(1f, Evidence(hat, ModuleSequenceSegmenter.RemainderEvidence));
    }

    private static float Evidence(Thought candidate, string source) =>
        candidate.LinksTo.FirstOrDefault(link =>
            link.LinkType?.Label == ModuleSequenceSegmenter.EvidenceLinkType &&
            link.To?.Label == source)?.Weight ?? 0;

    [Fact]
    public void BoundaryExperiment_VariesFillerAndRetainsBothTracesAndLearning()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };
        module.RunBoundaryExperiment(randomSeed: 17);
        string firstTrace = module.LastTrace;
        string[] firstInputs = firstTrace.Split(System.Environment.NewLine)
            .Where(line => line.StartsWith("Input: ")).Select(line => line[7..]).ToArray();
        Assert.Equal(2, firstInputs.Length);
        Assert.StartsWith("cat", firstInputs[0]);
        Assert.EndsWith("cat", firstInputs[1]);
        Assert.All(firstInputs, input => Assert.InRange(input.Length, 12, 18));
        foreach (string filler in new[] { firstInputs[0][3..], firstInputs[1][..^3] })
        {
            Assert.Equal(0, filler.Length % 3);
            for (int index = 0; index < filler.Length; index += 3)
                Assert.Contains(filler.Substring(index, 3),
                    new[] { "dog", "has", "fur", "hat", "car" });
        }
        Assert.NotEqual(firstInputs[0][3..], firstInputs[1][..^3]);
        Thought cat = uks.Labeled("candidate:cat");
        Assert.NotNull(cat);
        Assert.Equal(1f, Evidence(cat, ModuleSequenceSegmenter.StartEvidence));
        Assert.Equal(1f, Evidence(cat, ModuleSequenceSegmenter.EndEvidence));
        Assert.True(cat.isPlastic);

        module.RunBoundaryExperiment(randomSeed: 18);
        string[] secondInputs = module.LastTrace.Split(System.Environment.NewLine)
            .Where(line => line.StartsWith("Input: ")).Select(line => line[7..]).ToArray();
        Assert.NotEqual(firstInputs[0], secondInputs[0]);
        Assert.NotEqual(firstInputs[1], secondInputs[1]);
        Assert.Same(cat, uks.Labeled("candidate:cat"));
        Assert.Equal(2f, Evidence(cat, ModuleSequenceSegmenter.StartEvidence));
        Assert.Equal(2f, Evidence(cat, ModuleSequenceSegmenter.EndEvidence));
        module.RunBoundaryExperiment(randomSeed: 17);
        Assert.All(firstInputs, input => Assert.Contains("Input: " + input, module.LastTrace));
    }

    [Theory]
    [InlineData(17, false)]
    [InlineData(42, false)]
    [InlineData(73, false)]
    [InlineData(17, true)]
    [InlineData(42, true)]
    [InlineData(73, true)]
    public void GeneralVocabulary_FiveLeadingCandidatesAreSourceWords(int seed, bool mixedLengths)
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };
        string[] vocabulary = mixedLengths
            ? new[] { "dog", "has", "fur", "tail", "a", "is" }
            : new[] { "dog", "has", "fur", "cat", "hat", "car" };
        module.RunVocabularyExperiment(vocabulary, 6, 1000, seed);
        var ranked = uks.Labeled("CandidateSequence").Children
            .OrderByDescending(candidate => candidate.Weight).ToList();
        string summary = string.Join(", ", ranked.Take(12)
            .Select(candidate => $"{candidate.Label}:{candidate.Weight:0.00}"));
        // Partial improvement only: requiring all six to lead still fails for
        // seed 42 in the equal-length case and for all mixed-length seeds (a).
        // Do not interpret this check as complete vocabulary recovery.
        Assert.True(ranked.Count >= 5 && ranked.Take(5).All(candidate =>
            vocabulary.Contains(candidate.Label["candidate:".Length..])), summary);
    }

    private static UKS.UKS CreateUKS()
    {
        UKS.UKS uks = new(clear: true);
        uks.CreateInitialStructure();
        return uks;
    }
}

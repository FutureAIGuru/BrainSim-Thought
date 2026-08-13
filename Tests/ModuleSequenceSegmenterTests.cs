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
public class ModuleSequenceSegmenterTests
{
    private sealed class FixedChunkSegmenter : ModuleSequenceSegmenter
    {
        public int FixedSize { get; set; } = 5;

        protected override int SelectChunkSize(int maximum)
        {
            return System.Math.Min(FixedSize, maximum);
        }
    }

    private sealed class SeededChunkSegmenter : ModuleSequenceSegmenter
    {
        private readonly System.Random random = new(23);

        protected override int SelectChunkSize(int maximum)
        {
            return random.Next(1, maximum + 1);
        }
    }

    [Fact]
    public void SubmitLine_ConsumesContinuousNonOverlappingChunks()
    {
        UKS.UKS uks = CreateUKS();
        FixedChunkSegmenter module = new() { theUKS = uks, FixedSize = 3 };

        module.SubmitLine("abcdefgh");

        Assert.Equal("abc def gh", module.LastSegmentation);
        Assert.NotNull(uks.Labeled("candidate:abc"));
        Assert.NotNull(uks.Labeled("candidate:def"));
        Assert.NotNull(uks.Labeled("candidate:gh"));
        Assert.Null(uks.Labeled("candidate:bcd"));
        Assert.Equal(3, uks.Labeled("CandidateSequence").Children.Count);
    }

    [Fact]
    public void ExactCandidate_IsFoundBySequenceSearchAndReinforced()
    {
        UKS.UKS uks = CreateUKS();
        FixedChunkSegmenter module = new() { theUKS = uks, FixedSize = 3 };

        module.SubmitLine("dog");
        Thought dog = uks.Labeled("candidate:dog");
        Assert.Equal(0.99f, dog.Weight, 3);

        module.SubmitLine("dog");

        Assert.Same(dog, uks.Labeled("candidate:dog"));
        Assert.Equal(1.48f, dog.Weight, 3);
        Assert.Single(uks.Labeled("CandidateSequence").Children);
    }

    [Fact]
    public void MaximumChunkSize_DefaultsToThreeAndPauseEndsFinalChunk()
    {
        UKS.UKS uks = CreateUKS();
        FixedChunkSegmenter module = new() { theUKS = uks };

        module.SubmitLine("abcdefghijkl");

        Assert.Equal(3, module.MaximumChunkSize);
        Assert.Equal("abc def ghi jkl", module.LastSegmentation);
        Assert.All(uks.Labeled("CandidateSequence").Children, candidate =>
        {
            SeqElement sequence = Assert.IsType<SeqElement>(
                candidate.GetTargetOfFirstLinkOfType("hasSymbols"));
            Assert.InRange(uks.FlattenSequence(sequence).Count, 1, 3);
        });
    }

    [Fact]
    public void UnreinforcedCandidates_DecayAndAreDeleted()
    {
        UKS.UKS uks = CreateUKS();
        FixedChunkSegmenter module = new() { theUKS = uks, FixedSize = 1 };

        module.SubmitLine("a");
        Assert.NotNull(uks.Labeled("candidate:a"));

        for (int i = 0; i < 101; i++)
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
        FixedChunkSegmenter module = new() { theUKS = uks };

        module.SubmitLine("Dogs have tails");

        Assert.Null(uks.Labeled("UnsegmentedUtterance"));
        Assert.DoesNotContain(uks.AtomicThoughts, thought =>
            thought.Label.StartsWith("utterance:"));
    }

    [Fact]
    public void CandidatesUseExistingLetterThoughtsAndAddSequenceAndLink()
    {
        UKS.UKS uks = CreateUKS();
        FixedChunkSegmenter module = new() { theUKS = uks, FixedSize = 3 };

        module.SubmitLine("dog");

        Thought dog = uks.Labeled("candidate:dog");
        SeqElement sequence = Assert.IsType<SeqElement>(
            dog.GetTargetOfFirstLinkOfType("hasSymbols"));
        Assert.Equal("DOG", string.Concat(
            uks.FlattenSequence(sequence).Select(symbol => symbol.Label[2..])));
        Assert.All(uks.FlattenSequence(sequence), symbol =>
            Assert.StartsWith("l:", symbol.Label));
        Assert.DoesNotContain(uks.AtomicThoughts, thought =>
            thought.Label.StartsWith("c:"));
    }

    [Fact]
    public void CorpusLoader_StripsTabDelimitedMetadata()
    {
        UKS.UKS uks = CreateUKS();
        FixedChunkSegmenter module = new() { theUKS = uks };
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
        SeededChunkSegmenter module = new()
        {
            theUKS = uks,
            MaximumChunkSize = 3,
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
        int recognizedWords = vocabulary.Count(word =>
            uks.Labeled("candidate:" + word)?.Weight >=
                ModuleSequenceSegmenter.RecognitionThreshold);
        Assert.True(recognizedWords >= 3, winners);
        Assert.DoesNotContain(uks.AtomicThoughts, thought =>
            thought.Label.StartsWith("utterance:"));
    }

    private static UKS.UKS CreateUKS()
    {
        UKS.UKS uks = new(clear: true);
        uks.CreateInitialStructure();
        return uks;
    }
}

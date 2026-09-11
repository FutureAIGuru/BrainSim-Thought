using System;
using System.Collections.Generic;
using System.Linq;
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public partial class ModuleSequenceSegmenterTests
{
    // Isolated hypothesis test, NOT an alternative production segmenter.
    // Persistent learned state consists only of Thoughts, weights and sequences.
    // Complete paths are sampled during learning so an arbitrary first split is
    // not permanently selected by deterministic tie breaking. Evaluation uses
    // the best complete path under exactly the same scoring rule.
    [Theory]
    [InlineData(17, false)]
    [InlineData(42, false)]
    [InlineData(73, false)]
    [InlineData(17, true)]
    [InlineData(42, true)]
    [InlineData(73, true)]
    public void CompleteSegmentationExperiment_ReportsQualityWithoutAssumingSuccess(
        int seed, bool mixedLengths)
    {
        string[] vocabulary = mixedLengths
            ? new[] { "dog", "has", "fur", "tail", "a", "is" }
            : new[] { "dog", "has", "fur", "cat", "hat", "car", "cam", "can", "cap", "cart", "camp" };

        // Run separately: UKS initialization has shared state. Regenerate the
        // identical stream with the same seed instead of retaining a corpus.
        double baselineF1 = 0;
        foreach (bool competitive in new[] { false, true })
        {
            UKS.UKS uks = CreateUKS();
            ModuleSequenceSegmenter baseline = new() { theUKS = uks };
            baseline.Initialize();
            Thought root = uks.Labeled("CandidateSequence");
            Random inputRandom = new(seed);
            Random selectionRandom = new(seed + 10000);
            for (int line = 0; line < 1000; line++)
            {
                string input = string.Concat(RandomExperimentWords(vocabulary, inputRandom));
                if (competitive)
                {
                    List<string> chosen = ChooseCompleteSegmentation(input, root, selectionRandom);
                    Assert.Equal(input, string.Concat(chosen));
                    ReinforceSelectedSegmentation(uks, root, chosen);
                }
                else baseline.SubmitLine(input);
            }

            var ranked = root.Children.OrderByDescending(candidate => candidate.Weight).ToList();
            int recovered = ranked.Take(vocabulary.Length).Count(candidate =>
                vocabulary.Contains(candidate.Label["candidate:".Length..]));
            int correct = 0, predicted = 0, actual = 0, exact = 0;
            Random evaluationRandom = new(seed + 20000);
            for (int line = 0; line < 200; line++)
            {
                string[] words = RandomExperimentWords(vocabulary, evaluationRandom);
                List<string> chosen = ChooseCompleteSegmentation(string.Concat(words), root, null);
                var expectedBoundaries = InternalBoundaries(words);
                var foundBoundaries = InternalBoundaries(chosen);
                correct += foundBoundaries.Intersect(expectedBoundaries).Count();
                predicted += foundBoundaries.Count;
                actual += expectedBoundaries.Count;
                if (chosen.SequenceEqual(words)) exact++;
            }
            double f1 = 2.0 * correct / (predicted + actual);
            _output.WriteLine($"{(competitive ? "COMPETITION" : "BASELINE")}; " +
                $"seed={seed}; mixed={mixedLengths}; top-{vocabulary.Length} words={recovered}; " +
                $"boundary F1={f1:0.000}; exact={exact}/200; candidates={ranked.Count}");
            _output.WriteLine(string.Join(", ", ranked.Take(15).Select(candidate =>
                $"{candidate.Label["candidate:".Length..]}:{candidate.Weight:0.00}")));
            if (!competitive) baselineF1 = f1;
            else _output.WriteLine("Learning hypothesis: " +
                (f1 > baselineF1 && recovered == vocabulary.Length ? "ACCEPT" : "REJECT") +
                " (requires better held-out F1 and full top-N recovery).");
            // These assertions check experimental integrity, not convergence.
            // Acceptance requires improved held-out F1 AND all source words in
            // the top N, for both vocabularies and all seeds. Report failures.
            Assert.All(root.Children, candidate => Assert.True(candidate.isPlastic));
            Assert.InRange(f1, 0, 1);
        }
    }

    private static string[] RandomExperimentWords(string[] vocabulary, Random random) =>
        Enumerable.Range(0, 6).Select(_ => vocabulary[random.Next(vocabulary.Length)]).ToArray();

    private static HashSet<int> InternalBoundaries(IEnumerable<string> words)
    {
        HashSet<int> boundaries = new();
        int position = 0;
        foreach (string word in words) boundaries.Add(position += word.Length);
        boundaries.Remove(position); // The given final pause is not a prediction.
        return boundaries;
    }

    private static List<string> ChooseCompleteSegmentation(
        string input, Thought root, Random random)
    {
        const int maximum = 4; // Same maximum as the existing segmenter.
        double totalWeight = root.Children.Sum(candidate => (double)candidate.Weight);
        // Uniform letters and a geometric length prior, truncated to 1..4.
        // This is an explicit experimental assumption, not a learned word list.
        double Score(int position, int length)
        {
            string label = "candidate:" + input.Substring(position, length);
            double weight = root.Children.FirstOrDefault(candidate => candidate.Label == label)?.Weight ?? 0;
            double novel = Math.Pow(0.5 / 26, length) / (1 - Math.Pow(0.5, maximum));
            return Math.Log((weight + novel) / (totalWeight + 1));
        }

        double[] suffix = new double[input.Length + 1];
        for (int position = input.Length - 1; position >= 0; position--)
        {
            double[] scores = Enumerable.Range(1, Math.Min(maximum, input.Length - position))
                .Select(length => Score(position, length) + suffix[position + length]).ToArray();
            double best = scores.Max();
            suffix[position] = random is null ? best :
                best + Math.Log(scores.Sum(score => Math.Exp(score - best)));
        }

        List<string> result = new();
        for (int position = 0; position < input.Length;)
        {
            int count = Math.Min(maximum, input.Length - position);
            double[] scores = Enumerable.Range(1, count)
                .Select(length => Score(position, length) + suffix[position + length]).ToArray();
            int selected = 0;
            if (random is null) selected = Array.IndexOf(scores, scores.Max());
            else
            {
                double draw = random.NextDouble();
                selected = count - 1;
                for (int index = 0; index < count; index++)
                {
                    draw -= Math.Exp(scores[index] - suffix[position]);
                    if (draw <= 0) { selected = index; break; }
                }
            }
            int length = selected + 1;
            result.Add(input.Substring(position, length));
            position += length;
        }
        return result;
    }

    private static void ReinforceSelectedSegmentation(
        UKS.UKS uks, Thought root, IEnumerable<string> chosen)
    {
        Thought hasSymbols = uks.Labeled("hasSymbols");
        foreach (string chunk in chosen)
        {
            Thought candidate = uks.Labeled("candidate:" + chunk);
            if (candidate is null)
            {
                candidate = uks.GetOrAddThought("candidate:" + chunk, root);
                candidate.isPlastic = true;
                candidate.Weight = ModuleSequenceSegmenter.NewCandidateWeight;
                uks.AddSequenceAndLink(candidate, hasSymbols, chunk.Select(letter =>
                    uks.Labeled(UKS.UKS.GetCharacterLabel(letter))).ToList());
            }
            else candidate.Weight += ModuleSequenceSegmenter.RecognitionIncrement +
                ModuleSequenceSegmenter.LengthBonusPerSymbol * (chunk.Length - 1);
        }
        HashSet<char> symbols = string.Concat(chosen).ToHashSet();
        HashSet<string> selected = chosen.ToHashSet();
        foreach (Thought candidate in root.Children.ToList())
        {
            string chunk = candidate.Label["candidate:".Length..];
            candidate.Weight = Math.Max(0, candidate.Weight - ModuleSequenceSegmenter.DecayPerChunk -
                (!selected.Contains(chunk) && chunk.Any(symbols.Contains)
                    ? ModuleSequenceSegmenter.InterferencePerChunk : 0));
            if (candidate.Weight <= 0.0001f) candidate.Delete();
        }
    }

    [Fact]
    public void SelectedSegmentation_RewardsCatButNotItsUnselectedFragments()
    {
        UKS.UKS uks = CreateUKS();
        ModuleSequenceSegmenter module = new() { theUKS = uks };
        module.SubmitLine("cat");
        Thought root = uks.Labeled("CandidateSequence");
        float catBefore = uks.Labeled("candidate:cat").Weight;
        float atBefore = uks.Labeled("candidate:at").Weight;
        float cBefore = uks.Labeled("candidate:c").Weight;
        ReinforceSelectedSegmentation(uks, root, new[] { "cat" });
        Assert.True(uks.Labeled("candidate:cat").Weight > catBefore);
        Assert.True(uks.Labeled("candidate:at").Weight < atBefore);
        Assert.True(uks.Labeled("candidate:c").Weight < cBefore);
        // The same fragment can still be supported by an independent occurrence.
        float atAfter = uks.Labeled("candidate:at").Weight;
        ReinforceSelectedSegmentation(uks, root, new[] { "at" });
        Assert.True(uks.Labeled("candidate:at").Weight > atAfter);
    }
}

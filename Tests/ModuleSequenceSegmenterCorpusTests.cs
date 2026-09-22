using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public partial class ModuleSequenceSegmenterTests
{
    private sealed record CorpusPhrase(string[] Words)
    {
        public string Input => string.Concat(Words);
        public string Expected => string.Join(" ", Words);
    }

    private static IEnumerable<CorpusPhrase> ReadCorpus(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            string text = line.Split('\t')[0].Trim();
            if (text.StartsWith('#')) continue;
            // Match the production file loader's pause convention. Spaces are
            // ground truth for the scorer only; the learner receives no spaces.
            foreach (string sentence in Regex.Split(text, @"(?<=[.!?])\s+"))
            {
                string[] words = Regex.Split(sentence, @"\s+")
                    .Select(word => string.Concat(word.Where(char.IsLetter)).ToLowerInvariant())
                    .Where(word => word.Length > 0).ToArray();
                if (words.Length > 0) yield return new CorpusPhrase(words);
            }
        }
    }

    private sealed class CorpusScore
    {
        public int Count, Exact, Complete, Characters, UnknownCharacters;
        public int CorrectBoundaries, PredictedBoundaries, ExpectedBoundaries;
        public double Precision => PredictedBoundaries == 0 ? 0 : (double)CorrectBoundaries / PredictedBoundaries;
        public double Recall => ExpectedBoundaries == 0 ? 0 : (double)CorrectBoundaries / ExpectedBoundaries;
        public double F1 => PredictedBoundaries + ExpectedBoundaries == 0 ? 0 :
            2.0 * CorrectBoundaries / (PredictedBoundaries + ExpectedBoundaries);

        public void Add(CorpusPhrase phrase, string result)
        {
            string[] chunks = result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] plain = chunks.Select(chunk => chunk.Trim('[', ']')).ToArray();
            Assert.Equal(phrase.Input, string.Concat(plain)); // No dropped/duplicated characters.
            var expected = InternalBoundaries(phrase.Words);
            var predicted = InternalBoundaries(plain);
            Count++;
            if (result == phrase.Expected) Exact++;
            if (!result.Contains('[')) Complete++;
            Characters += phrase.Input.Length;
            UnknownCharacters += chunks.Where(chunk => chunk.StartsWith('[')).Sum(chunk => chunk.Length - 2);
            CorrectBoundaries += expected.Intersect(predicted).Count();
            PredictedBoundaries += predicted.Count;
            ExpectedBoundaries += expected.Count;
        }

        public override string ToString() =>
            $"n={Count}; exact={Exact}; complete={Complete}; precision={Precision:0.000}; recall={Recall:0.000}; " +
            $"F1={F1:0.000}; unknownChars={UnknownCharacters}/{Characters}";
    }

    [Fact]
    public void CorpusScorer_ChecksGoldBoundariesAndUnknownCoverage()
    {
        var phrases = ReadCorpus(new[] { "A cat.\t[metadata should not leak]", "The dog! A cat?" }).ToArray();
        Assert.Equal(new[] { "acat", "thedog", "acat" }, phrases.Select(p => p.Input));
        var score = new CorpusScore();
        score.Add(phrases[0], "a cat");
        score.Add(phrases[0], "[ac] at");
        Assert.Equal(1, score.Exact);
        Assert.Equal(1, score.Complete);
        Assert.Equal(2, score.UnknownCharacters);
        Assert.Equal(0.5, score.F1);
    }

    [Theory]
    [InlineData("bst_true_template_corpus.txt", 17)]
    [InlineData("bst_true_template_corpus.txt", 42)]
    [InlineData("bst_word_durability_corpus.txt", 17)]
    [InlineData("bst_word_durability_corpus.txt", 42)]
    [Trait("Category", "CorpusEvaluation")]
    public void ExistingCorpus_ReportsHeldOutSegmentationAndGateErrors(string file, int seed)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "SegmentationCorpora", file);
        var all = ReadCorpus(File.ReadLines(path)).ToArray();
        var groups = all.GroupBy(p => p.Input, StringComparer.Ordinal).ToArray();
        // Same unspaced phrase must not leak across training/evaluation. Reject
        // conflicting gold splits rather than choosing one silently.
        Assert.All(groups, group => Assert.Single(group.Select(p => p.Expected).Distinct()));
        var unique = groups.Select(group => group.First()).OrderBy(p => p.Input, StringComparer.Ordinal).ToArray();
        Random random = new(seed);
        random.Shuffle(unique);
        var heldOut = unique.Where((_, i) => i % 5 == 0).ToArray();
        var training = unique.Where((_, i) => i % 5 != 0).ToArray();
        Assert.NotEmpty(heldOut);
        Assert.NotEmpty(training);
        Assert.Empty(training.Select(p => p.Input).Intersect(heldOut.Select(p => p.Input)));
        var vocabulary = training.SelectMany(p => p.Words).ToHashSet();
        var edgeWords = training.SelectMany(p => new[] { p.Words[0], p.Words[^1] }).ToHashSet();
        var uks = CreateUKS();
        var module = new ModuleSequenceSegmenter { theUKS = uks };
        _output.WriteLine($"CORPUS {file}; seed={seed}; raw={all.Length}; unique={unique.Length}; train={training.Length}; heldout={heldOut.Length}; vocabulary={vocabulary.Count}; edgeVocabulary={edgeWords.Count}");

        for (int epoch = 1; epoch <= 3; epoch++)
        {
            random.Shuffle(training);
            int skipped = 0, wrongSkips = 0;
            foreach (var phrase in training)
            {
                module.SubmitLine(phrase.Input);
                if (module.LastTrace.Contains("candidate discovery skipped"))
                {
                    skipped++;
                    if (module.LastSegmentation != phrase.Expected) wrongSkips++;
                }
            }
            var candidates = uks.Labeled("CandidateSequence").Children.ToArray();
            // Snapshot all candidate link evidence as well as weights to detect
            // accidental evaluation-time learning or candidate creation.
            string Snapshot() => string.Join("\n", uks.Labeled("CandidateSequence").Children
                .OrderBy(c => c.Label).Select(c => c.Label + ":" + c.Weight.ToString("R") + ":" + c.isPlastic + ":" +
                    string.Join(";", c.LinksTo.Select(l => l.LinkType.Label + "/" + l.To.Label + "/" + l.Weight.ToString("R")).OrderBy(s => s))));
            string before = Snapshot();
            var score = new CorpusScore();
            var edgeSupported = new CorpusScore();
            var seenInterior = new CorpusScore();
            var unseen = new CorpusScore();
            var errors = new List<string>();
            foreach (var phrase in heldOut)
            {
                string result = module.RecognizeLine(phrase.Input);
                score.Add(phrase, result);
                (phrase.Words.All(edgeWords.Contains) ? edgeSupported :
                    phrase.Words.All(vocabulary.Contains) ? seenInterior : unseen).Add(phrase, result);
                if (result != phrase.Expected && errors.Count < 5)
                    errors.Add($"expected: {phrase.Expected} | got: {result}");
            }
            var trainScore = new CorpusScore();
            foreach (var phrase in training) trainScore.Add(phrase, module.RecognizeLine(phrase.Input));
            Assert.Equal(before, Snapshot());
            Assert.All(candidates, c => Assert.True(float.IsFinite(c.Weight) && c.Weight > 0));
            var recognized = candidates.Where(c => c.Weight >= ModuleSequenceSegmenter.RecognitionThreshold &&
                Evidence(c, ModuleSequenceSegmenter.MaximalEvidence) > 0).ToArray();
            int sourceWords = recognized.Count(c => vocabulary.Contains(c.Label["candidate:".Length..]));
            _output.WriteLine($"epoch={epoch}; candidates={candidates.Length}; recognized={recognized.Length}; recognizedTrainingWords={sourceWords}; gateSkips={skipped}/{training.Length}; wrongGateSkips={wrongSkips}");
            _output.WriteLine("train: " + trainScore);
            _output.WriteLine("heldout: " + score);
            _output.WriteLine("all-words-edge-exposed: " + edgeSupported);
            _output.WriteLine("all-words-seen-but-some-interior-only: " + seenInterior);
            _output.WriteLine("contains-unseen-word: " + unseen);
            foreach (string error in errors) _output.WriteLine(error);
        }
        // Passing means measurement integrity, NOT successful word discovery.
        // Do not hide poor accuracy behind a threshold selected after the run.
    }
}

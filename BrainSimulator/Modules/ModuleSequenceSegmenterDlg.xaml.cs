/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License. You may use, copy, modify, merge, publish, distribute,
 * sublicense, and/or sell copies of the Software, subject to the terms of
 * the MIT License.
 *
 * See the LICENSE file in the project root for full license information.
 */

using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Text.RegularExpressions;
using UKS;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BrainSimulator.Modules;

public partial class ModuleSequenceSegmenterDlg : ModuleBaseDlg
{
    private readonly CorpusReader _corpusReader = new();

    public ModuleSequenceSegmenterDlg()
    {
        InitializeComponent();
        Loaded += (_, _) => InputBox.Focus();
        Closed += (_, _) => _corpusReader.Dispose();
    }

    private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        if (ParentModule is not ModuleSequenceSegmenter module) return;
        if (InputBox.Text == "exp")
        {
            if (!TryGetExperimentParameters(out int topN, out int cycles, out double probabilityPower, out int randomSeed)) return;
            string[] words = { "dog", "has", "fur", "cat", "hat", "car",
                "a", "an", "the", "cam", "can", "cap", "cart", "camp","camembert" }; //there are 15 entries, the test default to 14
            List<KeyValuePair<string, double>> vocabulary = new();
            foreach (string word in words) vocabulary.Add(new(word, 1));
            if (topN > 0 && topN < vocabulary.Count) vocabulary.RemoveRange(topN, vocabulary.Count - topN);
            SetExperimentControlsEnabled(false);
            try { await RunVocabularyExperiment(module, vocabulary, cycles, randomSeed); }
            catch (Exception ex) { SetStatus($"Experiment failed: {ex.Message}"); }
            finally { SetExperimentControlsEnabled(true); }
            InputBox.SelectAll();
            InputBox.Focus();
            return;
        }
        else
        {
            module.ExplainLearning = ExplainLearningBox.IsChecked == true;
            string status = SubmitLine(module, InputBox.Text);
            SetStatus(status, Colors.Black);
        }
        SegmentationBox.Text = module.LastStatus;
        if (module.ExplainLearning) SegmentationBox.AppendText(Environment.NewLine + module.LastLearningDetails);
        SegmentationBox.ScrollToEnd();
        InputBox.SelectAll();
        InputBox.Focus();
    }

    private async Task RunVocabularyExperiment(ModuleSequenceSegmenter module,
        List<KeyValuePair<string, double>> vocabulary, int sequenceCount, int randomSeed)
    {
        const int wordsPerSequence = 10;
        Random random = new(randomSeed);
        module.ExplainLearning = ExplainLearningBox.IsChecked == true;
        SegmentationBox.AppendText($"Random seed: {randomSeed}; frequency bias: {FrequencyBiasBox.Text}. Same starting UKS and settings required for comparison.{Environment.NewLine}");
        await Task.Run(async () =>
        {
            for (int sequence = 0; sequence < sequenceCount; sequence++)
            {
                string input = BuildRandomPhrase(vocabulary, wordsPerSequence, random);
                string status = SubmitLine(module, input);
                string details = module.LastLearningDetails;
                string missing = "Missing: ";
                int missingCount = 0;
                foreach (var entry in vocabulary)
                {
                    Thought t = module.theUKS.Labeled("w:" + entry.Key);
                    if (t is not null && !t.isPlastic) continue;
                    missingCount++;
                    if (missingCount <= 20) missing += entry.Key + " ";
                    else if (missingCount == 21) missing += "...";
                }
                await Dispatcher.InvokeAsync(() =>
                {
                    SetStatus($"Experiment {sequence + 1}/{sequenceCount}, {vocabulary.Count} words, {missingCount} {missing}", Colors.Black);
                    SegmentationBox.AppendText($"{input}: {status}{Environment.NewLine}");
                    if (module.ExplainLearning) SegmentationBox.AppendText(details + Environment.NewLine);
                    SegmentationBox.ScrollToEnd();
                });
                await Task.Delay(10);
            }
        });
    }

    private void btnBrowse_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFileDialog openFileDialog = new()
        {
            Title = "Select Text File",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (openFileDialog.ShowDialog() != true) return;

        txtFilePath.Text = openFileDialog.FileName;
        _corpusReader.Dispose();
    }

    private void btnPreload_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is not ModuleSequenceSegmenter module) return;
        string filePath = txtFilePath.Text?.Trim();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            SetStatus("Select a vocabulary text file first.");
            return;
        }
        if (!int.TryParse(TopNWordsBox.Text, out int topN) || topN < 0)
        {
            SetStatus("Top N must be zero or greater.");
            return;
        }
        SetExperimentControlsEnabled(false);
        try
        {
            var seeded = SeedWordsFromFile(module, filePath, topN, wordList: true);
            module.ClearInputQueue();
            SeedWordsBox.IsChecked = false;
            string status = $"Preloaded {seeded.Count} non-plastic words from {Path.GetFileName(filePath)}. Select a phrase file, check One phrase per line, and press Load.";
            SetStatus(status, Colors.Black);
            SegmentationBox.AppendText(status + Environment.NewLine);
        }
        catch (Exception ex) { SetStatus($"Preload failed: {ex.Message}"); }
        finally { SetExperimentControlsEnabled(true); }
    }

    private async void btnLoad_Click(object sender, RoutedEventArgs e)
    {
        string filePath = txtFilePath.Text?.Trim();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            SetStatus("Select an existing text file first.");
            return;
        }
        if (ParentModule is not ModuleSequenceSegmenter module) return;
        if (!TryGetExperimentParameters(out int topN, out int cycles, out double probabilityPower, out int randomSeed)) return;

        SetExperimentControlsEnabled(false);
        bool phrasesFromFile = PhrasesFromFileBox.IsChecked == true;
        module.ExplainLearning = ExplainLearningBox.IsChecked == true;
        SetStatus(phrasesFromFile ? "Reading phrases..." : "Reading words...");
        try
        {
            if (phrasesFromFile)
            {
                // Source boundaries are used only for the report, never supplied to the learner.
                Dictionary<string, int> expectedCounts = new(StringComparer.Ordinal);
                string sourceText = Regex.Replace(File.ReadAllText(filePath).ToLowerInvariant(), @"\p{P}", "");
                foreach (Match token in Regex.Matches(sourceText, @"\p{L}+"))
                {
                    expectedCounts.TryGetValue(token.Value, out int occurrences);
                    expectedCounts[token.Value] = occurrences + 1;
                }
                module.PatternRoot = module.theUKS.Labeled("Word");
                module.PatternPrefix = "w:";
                if (SeedWordsBox.IsChecked == true)
                {
                    module.ClearInputQueue();
                    var seeded = SeedWordsFromFile(module, filePath);
                    SegmentationBox.AppendText($"Seeded {seeded.Count} words from the phrase file.{Environment.NewLine}");
                }
                HashSet<Thought> startingPatterns = new(module.PatternRoot.Children);
                module.RecognitionCounts = new Dictionary<Thought, int>();
                int knownWords = 0, knownOccurrences = 0, totalOccurrences = 0;
                string missing = string.Empty;
                foreach (var entry in expectedCounts)
                {
                    Thought thought = module.theUKS.Labeled("w:" + entry.Key);
                    totalOccurrences += entry.Value;
                    if (thought is not null && !thought.isPlastic)
                    {
                        knownWords++;
                        knownOccurrences += entry.Value;
                    }
                    else missing += entry.Key + " ";
                }
                SegmentationBox.AppendText($"Before reading: {knownWords}/{expectedCounts.Count} source words established, representing {knownOccurrences}/{totalOccurrences} source occurrences.{Environment.NewLine}");
                SegmentationBox.AppendText($"Not initially established: {missing}{Environment.NewLine}");
                Stopwatch elapsed = Stopwatch.StartNew();
                double learnerMs = 0, slowestMs = 0;
                string slowestPhrase = string.Empty;
                await Task.Run(async () =>
                {
                    int count = 0;
                    foreach (string line in File.ReadLines(filePath))
                    {
                        string phrase = Regex.Replace(line, @"\p{P}", "").Trim();
                        phrase = phrase.Replace(" ", "");
                        if (!Regex.IsMatch(phrase, @"\p{L}")) continue;
                        long started = Stopwatch.GetTimestamp();
                        string status = SubmitLine(module, phrase);
                        string details = module.LastLearningDetails;
                        double milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        learnerMs += milliseconds;
                        if (milliseconds > slowestMs) { slowestMs = milliseconds; slowestPhrase = phrase; }
                        await Dispatcher.InvokeAsync(() =>
                        {
                            SetStatus($"Phrase {count + 1}: learner {milliseconds:0} ms", Colors.Black);
                            SegmentationBox.AppendText($"{phrase}: {status}{Environment.NewLine}");
                            if (module.ExplainLearning) SegmentationBox.AppendText(details + Environment.NewLine);
                            SegmentationBox.ScrollToEnd();
                        });
                        count++;
                        //if (count >= cycles) break;
                        await Task.Delay(10);
                    }
                    await Dispatcher.InvokeAsync(() =>
                    {
                        elapsed.Stop();
                        double averageMs = count == 0 ? 0 : learnerMs / count;
                        SegmentationBox.AppendText($"{Environment.NewLine}Timing: {elapsed.Elapsed.TotalSeconds:0.0}s elapsed, {learnerMs / 1000:0.0}s in learner, average {averageMs:0.0} ms/phrase, maximum {slowestMs:0.0} ms.{Environment.NewLine}");
                        SegmentationBox.AppendText($"Slowest phrase: {slowestPhrase}{Environment.NewLine}");
                        SetStatus($"Read {count} phrases.", Colors.Black);
                        SegmentationBox.AppendText($"{Environment.NewLine}Word counts (source / recognized), current state:{Environment.NewLine}");
                        foreach (var entry in expectedCounts)
                        {
                            Thought thought = module.theUKS.Labeled("w:" + entry.Key);
                            int recognized = 0;
                            if (thought is not null) module.RecognitionCounts.TryGetValue(thought, out recognized);
                            string state = thought is null ? "not created" : thought.isPlastic ? "candidate" : "pattern";
                            SegmentationBox.AppendText($"w:{entry.Key}: {entry.Value} / {recognized}, {state}{Environment.NewLine}");
                        }
                        SegmentationBox.AppendText($"New patterns/candidates:{Environment.NewLine}");
                        int newCount = 0;
                        foreach (Thought thought in module.PatternRoot.Children)
                        {
                            if (startingPatterns.Contains(thought)) continue;
                            SegmentationBox.AppendText($"{thought.Label}: weight {thought.Weight:0.##}{Environment.NewLine}");
                            newCount++;
                        }
                        SetStatus($"Read {count} phrases in {elapsed.Elapsed.TotalSeconds:0.0}s ({learnerMs / 1000:0.0}s learner); {knownWords}/{expectedCounts.Count} source words initially established; {newCount} surviving new patterns/candidates.", Colors.Black);
                        SegmentationBox.ScrollToEnd();
                    });
                });
                return;
            }
            var vocabulary = await Task.Run(() => ReadVocabulary(filePath, topN, probabilityPower));
            if (vocabulary.Count == 0) { SetStatus("The file contains no words."); return; }
            await RunVocabularyExperiment(module, vocabulary, cycles, randomSeed);
        }
        catch (Exception ex)
        {
            SetStatus($"Error loading file: {ex.Message}");
        }
        finally
        {
            module.RecognitionCounts = null;
            SetExperimentControlsEnabled(true);
        }
    }

    private bool TryGetExperimentParameters(out int topN, out int cycles, out double probabilityPower, out int randomSeed)
    {
        bool validTop = int.TryParse(TopNWordsBox.Text, out topN) && topN >= 0;
        bool validCycles = int.TryParse(CycleCountBox.Text, out cycles) && cycles > 0;
        bool validPower = double.TryParse(FrequencyBiasBox.Text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out probabilityPower) && probabilityPower >= 0 && probabilityPower <= 1;
        bool validSeed = int.TryParse(RandomSeedBox.Text, out randomSeed);
        bool valid = validTop && validCycles && validPower && validSeed;
        if (!valid) SetStatus("Top N must be zero or greater; cycles greater than zero; frequency bias between 0 and 1; random seed an integer.");
        return valid;
    }

    private void SetExperimentControlsEnabled(bool enabled)
    {
        InputBox.IsEnabled = btnLoad.IsEnabled = btnBrowse.IsEnabled = btnPreload.IsEnabled = enabled;
        TopNWordsBox.IsEnabled = CycleCountBox.IsEnabled = FrequencyBiasBox.IsEnabled = PhrasesFromFileBox.IsEnabled = SeedWordsBox.IsEnabled = enabled;
        RandomSeedBox.IsEnabled = ExplainLearningBox.IsEnabled = enabled;
    }

    /// <summary>Preload ranked vocabulary or seed phrase tokens as complete non-plastic words.</summary>
    public static Dictionary<Thought, int> SeedWordsFromFile(ModuleSequenceSegmenter module, string filePath, int topN = 0, bool wordList = false)
    {
        module.PatternRoot = module.theUKS.Labeled("Word");
        module.PatternPrefix = "w:";
        List<string> words = new();
        if (wordList)
            foreach (var entry in ReadVocabulary(filePath, topN)) words.Add(entry.Key);
        else
            foreach (Match token in Regex.Matches(File.ReadAllText(filePath).ToLowerInvariant(), @"\p{L}+")) words.Add(token.Value);
        Dictionary<Thought, int> counts = new();
        foreach (string word in words)
        {
            string label = "w:" + word;
            Thought pattern = module.theUKS.GetOrAddThought(label, module.PatternRoot);
            if (pattern.GetTargetOfFirstLinkOfType("spelled") is null)
                module.theUKS.AddSequenceAndLink(pattern, "spelled", InputSymbols(module, word), allowCompression: false);
            pattern.Weight = Math.Max(pattern.Weight, module.ConsolidationStrength);
            pattern.isPlastic = false;
            counts.TryGetValue(pattern, out int count);
            counts[pattern] = count + 1;
        }
        return counts;
    }

    public static List<KeyValuePair<string, double>> ReadVocabulary(string filePath, int topN, double probabilityPower = 1)
    {
        Dictionary<string, double> weights = new(StringComparer.Ordinal);
        Dictionary<string, int> fileOrder = new(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(filePath))
        {
            string[] fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0) continue;
            string word = string.Empty;
            foreach (char character in fields[0])
                if (char.IsLetter(character)) word += char.ToLowerInvariant(character);
            if (word.Length == 0) continue;
            double weight = 1;
            if (fields.Length > 1)
            {
                bool valid = double.TryParse(fields[1].TrimEnd('%'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out weight);
                if (!valid || !double.IsFinite(weight) || weight <= 0)
                    throw new FormatException($"Invalid usage weight for '{fields[0]}'.");
            }
            weights.TryGetValue(word, out double previous);
            if (previous == 0) fileOrder[word] = weights.Count;
            weights[word] = previous + weight;
        }
        List<KeyValuePair<string, double>> vocabulary = new(weights);
        vocabulary.Sort((left, right) =>
        {
            int comparison = right.Value.CompareTo(left.Value);
            if (comparison == 0) comparison = fileOrder[left.Key].CompareTo(fileOrder[right.Key]);
            return comparison;
        });
        if (topN > 0 && topN < vocabulary.Count) vocabulary.RemoveRange(topN, vocabulary.Count - topN);
        // Flatten sampling probabilities after selecting the top words by their original usage.
        for (int index = 0; index < vocabulary.Count; index++)
            vocabulary[index] = new(vocabulary[index].Key, Math.Pow(vocabulary[index].Value, probabilityPower));
        return vocabulary;
    }

    public static string BuildRandomPhrase(IReadOnlyList<KeyValuePair<string, double>> vocabulary,
        int wordsPerSequence, Random random)
    {
        if (wordsPerSequence > 1 && vocabulary.Count < 2)
            throw new ArgumentException("At least two words are needed to avoid adjacent repeats.");
        double total = 0;
        foreach (var entry in vocabulary) total += entry.Value;
        string input = string.Empty;
        string previous = null;
        double previousWeight = 0;
        for (int index = 0; index < wordsPerSequence; index++)
        {
            double target = random.NextDouble() * (total - previousWeight);
            string selected = null;
            double selectedWeight = 0;
            foreach (var entry in vocabulary)
            {
                if (entry.Key == previous) continue;
                selected = entry.Key;
                selectedWeight = entry.Value;
                target -= entry.Value;
                if (target < 0) break;
            }
            input += selected;
            previous = selected;
            previousWeight = selectedWeight;
        }
        return input;
    }
    /// <summary>Text entry supplies character ATs and an explicit trailing pause.</summary>
    public static string SubmitLine(ModuleSequenceSegmenter module, string input)
    {
        input = (input ?? string.Empty).TrimEnd();
        int marks = 0;
        while (input.EndsWith('!'))
        {
            marks++;
            input = input[..^1];
        }
        float emphasis = marks switch
        {
            0 => 0,
            1 => 1,
            2 => module.ConsolidationStrength / 2,
            _ => module.ConsolidationStrength,
        };
        List<Thought> observations = InputSymbols(module, input);
        observations.Add(module.InputPause);
        string result = module.InputReceived(observations, emphasis);
        return result;
    }

    public static List<Thought> InputSymbols(ModuleSequenceSegmenter module, string input)
    {
        module.PatternRoot = module.theUKS.Labeled("Word");
        module.PatternPrefix = "w:";
        List<Thought> observations = new();
        foreach (char character in input ?? string.Empty)
        {
            if (!char.IsLetter(character)) continue;
            Thought observation = module.theUKS.Labeled(UKS.UKS.GetCharacterLabel(character));
            if (observation is not null) observations.Add(observation);
        }
        return observations;
    }

    // File and text handling belong to the dialog adapter, not the learner.
    public sealed class CorpusReader : IDisposable
    {
        private StreamReader _sequenceReader;
        private string _sequenceReaderPath;
        /// <summary>
        /// Presents one phrase per nonempty line, stripping punctuation before submission.
        /// Text beginning with the first tab on each line is metadata and is not
        /// presented to the learner.
        /// </summary>
        public int LoadTextFromFile(ModuleSequenceSegmenter module, string filePath, int sequencesPerCall = 1000)
        {
            if (sequencesPerCall <= 0) sequencesPerCall = 1;
            if (!File.Exists(filePath))
            {
                ResetSequenceReader();
                return 0;
            }

            try
            {
                if (_sequenceReader is null || !string.Equals(
                    _sequenceReaderPath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    ResetSequenceReader();
                    _sequenceReader = new StreamReader(File.Open(
                        filePath, FileMode.Open, FileAccess.Read, FileShare.Read));
                    _sequenceReaderPath = filePath;
                }

                int count = 0;
                while (count < sequencesPerCall && _sequenceReader is not null)
                {
                    string line = _sequenceReader.ReadLine();
                    if (line is null) break;

                    int tab = line.IndexOf('\t');
                    string phrase = (tab < 0 ? line : line[..tab]).Trim();
                    if (phrase.Length == 0) continue;

                    phrase = Regex.Replace(phrase, @"\p{P}", "").Trim();
                    if (!Regex.IsMatch(phrase, @"\p{L}")) continue;
                    SubmitLine(module, phrase);
                    count++;
                }

                if (_sequenceReader is not null && _sequenceReader.EndOfStream)
                    ResetSequenceReader();
                return count;
            }
            catch
            {
                ResetSequenceReader();
                throw;
            }
        }

        public void Dispose()
        {
            ResetSequenceReader();
        }

        private void ResetSequenceReader()
        {
            _sequenceReader?.Dispose();
            _sequenceReader = null;
            _sequenceReaderPath = null;
        }

    }
}

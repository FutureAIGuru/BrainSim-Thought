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
            if (!TryGetExperimentParameters(out int topN, out int cycles, out double probabilityPower)) return;
            string[] words = { "dog", "has", "fur", "cat", "hat", "car",
                "a", "an", "the", "cam", "can", "cap", "cart", "camp","camembert" }; //there are 15 entries, the test default to 14
            List<KeyValuePair<string, double>> vocabulary = new();
            foreach (string word in words) vocabulary.Add(new(word, 1));
            if (topN > 0 && topN < vocabulary.Count) vocabulary.RemoveRange(topN, vocabulary.Count - topN);
            SetExperimentControlsEnabled(false);
            try { await RunVocabularyExperiment(module, vocabulary, cycles); }
            catch (Exception ex) { SetStatus($"Experiment failed: {ex.Message}"); }
            finally { SetExperimentControlsEnabled(true); }
            InputBox.SelectAll();
            InputBox.Focus();
            return;
        }
        else
        {
            string status = SubmitLine(module, InputBox.Text);
            SetStatus(status, Colors.Black);
        }
        SegmentationBox.Text = module.LastStatus;
        SegmentationBox.ScrollToEnd();
        InputBox.SelectAll();
        InputBox.Focus();
    }

    private async Task RunVocabularyExperiment(ModuleSequenceSegmenter module,
        List<KeyValuePair<string, double>> vocabulary, int sequenceCount)
    {
        const int wordsPerSequence = 6;
        await Task.Run(async () =>
        {
            for (int sequence = 0; sequence < sequenceCount; sequence++)
            {
                string input = BuildRandomPhrase(vocabulary, wordsPerSequence, Random.Shared);
                await Dispatcher.InvokeAsync(() =>
                {
                    string status = SubmitLine(module, input);
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
                    SetStatus($"Experiment {sequence + 1}/{sequenceCount}, {vocabulary.Count} words, {missingCount} {missing}", Colors.Black);
                    SegmentationBox.AppendText($"{input}: {status}{Environment.NewLine}");
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

    private async void btnLoad_Click(object sender, RoutedEventArgs e)
    {
        string filePath = txtFilePath.Text?.Trim();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            SetStatus("Select an existing text file first.");
            return;
        }
        if (ParentModule is not ModuleSequenceSegmenter module) return;
        if (!TryGetExperimentParameters(out int topN, out int cycles, out double probabilityPower)) return;

        SetExperimentControlsEnabled(false);
        bool phrasesFromFile = PhrasesFromFileBox.IsChecked == true;
        SetStatus(phrasesFromFile ? "Reading phrases..." : "Reading words...");
        try
        {
            if (phrasesFromFile)
            {
                // Deliberately synchronous: the complete vocabulary exists before any phrase is processed.
                Dictionary<Thought, int> expectedCounts = null;
                HashSet<Thought> startingPatterns = null;
                if (SeedWordsBox.IsChecked == true)
                {
                    module.ClearInputQueue();
                    expectedCounts = SeedWordsFromFile(module, filePath);
                    startingPatterns = new HashSet<Thought>(module.PatternRoot.Children);
                    module.RecognitionCounts = new Dictionary<Thought, int>();
                    SegmentationBox.Clear();
                    SegmentationBox.AppendText($"Seeded {expectedCounts.Count} words.{Environment.NewLine}");
                }
                await Task.Run(async () =>
                {
                    int count = 0;
                    foreach (string line in File.ReadLines(filePath))
                    {
                        string phrase = Regex.Replace(line, @"\p{P}", "").Trim();
                        phrase = phrase.Replace(" ", "");
                        if (!Regex.IsMatch(phrase, @"\p{L}")) continue;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            string status = SubmitLine(module, phrase);
                            SetStatus($"Phrase {count + 1}", Colors.Black);
                            SegmentationBox.AppendText($"{phrase}: {status}{Environment.NewLine}");
                            SegmentationBox.ScrollToEnd();
                        });
                        count++;
                        //if (count >= cycles) break;
                        await Task.Delay(10);
                    }
                    await Dispatcher.InvokeAsync(() =>
                    {
                        SetStatus($"Read {count} phrases.", Colors.Black);
                        if (expectedCounts is null) return;
                        SegmentationBox.AppendText($"{Environment.NewLine}Word counts (source / recognized):{Environment.NewLine}");
                        foreach (var entry in expectedCounts)
                        {
                            module.RecognitionCounts.TryGetValue(entry.Key, out int recognized);
                            SegmentationBox.AppendText($"{entry.Key.Label}: {entry.Value} / {recognized}{Environment.NewLine}");
                        }
                        SegmentationBox.AppendText($"New patterns/candidates:{Environment.NewLine}");
                        int newCount = 0;
                        foreach (Thought thought in module.PatternRoot.Children)
                        {
                            if (startingPatterns.Contains(thought)) continue;
                            SegmentationBox.AppendText($"{thought.Label}: weight {thought.Weight:0.##}{Environment.NewLine}");
                            newCount++;
                        }
                        SetStatus($"Read {count} phrases; {expectedCounts.Count} seeded words; {newCount} new patterns/candidates.", Colors.Black);
                        SegmentationBox.ScrollToEnd();
                    });
                });
                return;
            }
            var vocabulary = await Task.Run(() => ReadVocabulary(filePath, topN, probabilityPower));
            if (vocabulary.Count == 0) { SetStatus("The file contains no words."); return; }
            await RunVocabularyExperiment(module, vocabulary, cycles);
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

    private bool TryGetExperimentParameters(out int topN, out int cycles, out double probabilityPower)
    {
        bool validTop = int.TryParse(TopNWordsBox.Text, out topN) && topN >= 0;
        bool validCycles = int.TryParse(CycleCountBox.Text, out cycles) && cycles > 0;
        bool validPower = double.TryParse(FrequencyBiasBox.Text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out probabilityPower) && probabilityPower >= 0 && probabilityPower <= 1;
        bool valid = validTop && validCycles && validPower;
        if (!valid) SetStatus("Top N must be zero or greater; cycles greater than zero; frequency bias between 0 and 1.");
        return valid;
    }

    private void SetExperimentControlsEnabled(bool enabled)
    {
        InputBox.IsEnabled = btnLoad.IsEnabled = btnBrowse.IsEnabled = enabled;
        TopNWordsBox.IsEnabled = CycleCountBox.IsEnabled = FrequencyBiasBox.IsEnabled = PhrasesFromFileBox.IsEnabled = SeedWordsBox.IsEnabled = enabled;
    }

    /// <summary>Seed complete non-plastic words, returning their source occurrence counts for comparison.</summary>
    public static Dictionary<Thought, int> SeedWordsFromFile(ModuleSequenceSegmenter module, string filePath)
    {
        module.PatternRoot = module.theUKS.Labeled("Word");
        module.PatternPrefix = "w:";
        Dictionary<Thought, int> counts = new();
        foreach (Match token in Regex.Matches(File.ReadAllText(filePath).ToLowerInvariant(), @"\p{L}+"))
        {
            string label = "w:" + token.Value;
            Thought pattern = module.theUKS.GetOrAddThought(label, module.PatternRoot);
            if (pattern.GetTargetOfFirstLinkOfType("spelled") is null)
                module.theUKS.AddSequenceAndLink(pattern, "spelled", InputSymbols(module, token.Value), allowCompression: false);
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

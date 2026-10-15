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
            if (!TryGetExperimentParameters(out int topN, out int cycles)) return;
            string[] words = { "dog", "has", "fur", "cat", "hat", "car",
                "a", "an", "the", "cam", "can", "cap", "cart", "camp" };
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
                    SetStatus($"Experiment {sequence + 1}/{sequenceCount}, {vocabulary.Count} words, {missingCount} missing. {missing}", Colors.Black);
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
            Title = "Select Word List",
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
            SetStatus("Select an existing word-list file first.");
            return;
        }
        if (ParentModule is not ModuleSequenceSegmenter module) return;
        if (!TryGetExperimentParameters(out int topN, out int cycles)) return;

        SetExperimentControlsEnabled(false);
        SetStatus("Reading words...");
        try
        {
            var vocabulary = await Task.Run(() => ReadVocabulary(filePath, topN));
            if (vocabulary.Count == 0) { SetStatus("The file contains no words."); return; }
            await RunVocabularyExperiment(module, vocabulary, cycles);
        }
        catch (Exception ex)
        {
            SetStatus($"Error loading file: {ex.Message}");
        }
        finally { SetExperimentControlsEnabled(true); }
    }

    private bool TryGetExperimentParameters(out int topN, out int cycles)
    {
        bool validTop = int.TryParse(TopNWordsBox.Text, out topN) && topN >= 0;
        bool validCycles = int.TryParse(CycleCountBox.Text, out cycles) && cycles > 0;
        bool valid = validTop && validCycles;
        if (!valid) SetStatus("Top N must be zero or greater; cycles must be greater than zero.");
        return valid;
    }

    private void SetExperimentControlsEnabled(bool enabled)
    {
        InputBox.IsEnabled = btnLoad.IsEnabled = btnBrowse.IsEnabled = enabled;
        TopNWordsBox.IsEnabled = CycleCountBox.IsEnabled = enabled;
    }

    public static List<KeyValuePair<string, double>> ReadVocabulary(string filePath, int topN)
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
        int emphasis = 0;
        while (input.EndsWith('!'))
        {
            emphasis++;
            input = input[..^1];
        }
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
        /// Incrementally presents pause-delimited sequences from a corpus file.
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

                    foreach (string sentence in Regex.Split(
                        phrase, @"(?<=[\.!\?])\s+"))
                    {
                        if (count >= sequencesPerCall) break;
                        string trimmed = sentence.Trim();
                        if (trimmed.Length == 0) continue;
                        SubmitLine(module, trimmed);
                        count++;
                    }
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

/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License. You may use, copy, modify, merge, publish, distribute,
 * sublicense, and/or sell copies of this software under the terms of
 * the MIT License.
 */

using Pluralize.NET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UKS;

namespace BrainSimulator.Modules;

public partial class ModuleTextDlg : ModuleBaseDlg
{
    private readonly List<string> phraseHistory = new();
    private readonly Dictionary<string, string> parsedInputHistory = new();
    private readonly Dictionary<string, Thought> parsedThoughts = new();
    private int phraseHistoryIndex;
    private string phraseHistoryDraft = string.Empty;

    public ModuleTextDlg()
    {
        InitializeComponent();
    }

    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;
        UpdateAttributesOutput();
        return true;
    }

    private void TheGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Draw(false);
    }

    private void BtnSubmit_Click(object sender, RoutedEventArgs e)
    {
        SubmitCurrentText();
    }

    private void SubmitCurrentText()
    {
        string phrase = InputBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(phrase)) return;

        phraseHistory.Remove(phrase);
        phraseHistory.Add(phrase);
        phraseHistoryIndex = phraseHistory.Count;
        phraseHistoryDraft = string.Empty;

        if (ParentModule is not ModuleText module) return;
        string answer = module.SubmitText(phrase);
        OutputBox.Text = string.IsNullOrWhiteSpace(answer)
            ? module.LastStatus
            : answer;
        AddParsedOutput(module.LastRelationship, phrase);
        SetStatus(module.LastStatus);
        InputBox.Focus();
    }

    private void AddParsedOutput(Link relationship, string input)
    {
        if (relationship is null) return;
        string entry = relationship.ToString();
        if (string.IsNullOrWhiteSpace(entry)) return;

        parsedInputHistory[entry] = input;
        parsedThoughts[entry] = relationship.From;
        if (ParsedInputBox.Items.Count == 0 ||
            ParsedInputBox.Items[0]?.ToString() != entry)
            ParsedInputBox.Items.Insert(0, entry);
        ParsedInputBox.SelectedIndex = 0;

        int historyLimit = 6;
        while (ParsedInputBox.Items.Count > historyLimit)
        {
            string removed = ParsedInputBox.Items[^1]?.ToString();
            ParsedInputBox.Items.RemoveAt(ParsedInputBox.Items.Count - 1);
            if (string.IsNullOrWhiteSpace(removed)) continue;
            parsedInputHistory.Remove(removed);
            parsedThoughts.Remove(removed);
        }
    }

    private void ParsedInputBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        string entry = ParsedInputBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(entry)) return;

        if (parsedInputHistory.TryGetValue(entry, out string original))
            InputBox.Text = original;
        if (parsedThoughts.TryGetValue(entry, out Thought thought) &&
            thought is not null)
            AttributesOfBoxInputBox.Text = thought.Label;
        UpdateAttributesOutput();
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up)
        {
            ShowPreviousPhrase();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            ShowNextPhrase();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            SubmitCurrentText();
            e.Handled = true;
        }
    }

    private void AttributeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        UpdateAttributesOutput();
        e.Handled = true;
    }

    private void ShowPreviousPhrase()
    {
        if (phraseHistory.Count == 0) return;
        if (phraseHistoryIndex == phraseHistory.Count)
            phraseHistoryDraft = InputBox.Text ?? string.Empty;
        if (phraseHistoryIndex > 0) phraseHistoryIndex--;
        ShowPhraseHistoryEntry(phraseHistory[phraseHistoryIndex]);
    }

    private void ShowNextPhrase()
    {
        if (phraseHistory.Count == 0 ||
            phraseHistoryIndex == phraseHistory.Count)
            return;
        phraseHistoryIndex++;
        ShowPhraseHistoryEntry(phraseHistoryIndex < phraseHistory.Count
            ? phraseHistory[phraseHistoryIndex]
            : phraseHistoryDraft);
    }

    private void ShowPhraseHistoryEntry(string phrase)
    {
        InputBox.Text = phrase;
        InputBox.CaretIndex = InputBox.Text.Length;
    }

    private void UpdateAttributesOutput()
    {
        AttributesOutputBox.Items.Clear();
        UKS.UKS uks = ParentModule?.theUKS;
        if (uks is null) return;

        string raw = AttributesOfBoxInputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;
        Thought word = uks.Labeled("w:" + raw.ToLowerInvariant());
        Thought thought = ModuleText.GetBestMeaning(word)?.To ?? uks.Labeled(raw);
        if (thought is null) return;

        IPluralize pluralizer = new Pluralizer();
        foreach (Link attribute in uks.GetAttributes(thought))
        {
            string typeText = attribute.LinkType?.Label ?? string.Empty;
            bool isPlural = false;
            int dot = typeText.IndexOf('.');
            if (dot >= 0)
            {
                if (int.TryParse(typeText[(dot + 1)..], out int count) && count > 1)
                    isPlural = true;
                typeText = typeText.Replace('.', ' ');
            }

            string targetText = ModuleText.GetBestWordFor(attribute.To);
            if (string.IsNullOrWhiteSpace(targetText))
                targetText = attribute.To?.Label ?? string.Empty;
            if (isPlural) targetText = pluralizer.Pluralize(targetText);

            float confidence = float.IsNaN(attribute.Weight)
                ? 0
                : Math.Clamp(attribute.Weight, 0, 1);
            Color barColor = Color.FromArgb(80, 0, 240, 0);
            LinearGradientBrush brush = new()
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                GradientStops = new GradientStopCollection
                {
                    new(barColor, 0),
                    new(barColor, confidence),
                    new(Color.FromArgb(0, barColor.R, barColor.G, barColor.B),
                        confidence),
                    new(Color.FromArgb(0, barColor.R, barColor.G, barColor.B), 1),
                }
            };
            AttributesOutputBox.Items.Add(new ListBoxItem
            {
                Content = $"{typeText} {targetText}",
                Background = brush,
                Padding = new Thickness(4, 1, 4, 1),
            });
        }
    }

    private void btnBrowse_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFileDialog openFileDialog = new()
        {
            Title = "Select Phrase File",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (openFileDialog.ShowDialog() != true) return;

        txtFilePath.Text = openFileDialog.FileName;
        (ParentModule as ModuleText)?.CancelIncrementalLoad();
    }

    private async void btnLoad_Click(object sender, RoutedEventArgs e)
    {
        string filePath = txtFilePath.Text?.Trim();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            SetStatus("Select an existing phrase file first.");
            return;
        }
        if (ParentModule is not ModuleText module) return;

        SetStatus("Loading phrases...");
        try
        {
            int count = await Task.Run(() => module.LoadTextFromFile(filePath,1000));
            string status = module.LastStatus.StartsWith("Error:", StringComparison.Ordinal)
                ? $"Loaded {count} phrase(s). {module.LastStatus}"
                : $"Loaded and processed {count} phrase(s) from the file.";
            SetStatus(status);
        }
        catch (Exception ex)
        {
            SetStatus($"Error loading file: {ex.Message}");
        }
    }
}

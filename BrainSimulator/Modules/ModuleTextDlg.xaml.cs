/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License. You may use, copy, modify, merge, publish, distribute,
 * sublicense, and/or sell copies of this software under the terms of
 * the MIT License.
 *
 * See the LICENSE file in the project root for full license information.
 */


using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace BrainSimulator.Modules;

public partial class ModuleTextDlg : ModuleBaseDlg
{
    private readonly List<string> phraseHistory = new();
    private int phraseHistoryIndex;
    private string phraseHistoryDraft = string.Empty;

    public ModuleTextDlg()
    {
        InitializeComponent();
    }

    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;
        //this has a timer so that no matter how often you might call draw, the dialog
        //only updates 10x per second
        ModuleText parent = (ModuleText)base.ParentModule;
        return true;
    }

    private void TheGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Draw(false);
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        string phrase = tbPhrase.Text ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(phrase))
        {
            phraseHistory.Remove(phrase);
            phraseHistory.Add(phrase);
        }
        phraseHistoryIndex = phraseHistory.Count;
        phraseHistoryDraft = string.Empty;

        tbPhrase.Text = string.Empty;
        tbPhrase.Focus();
        string message = ModuleText.AddText(phrase, learnIncrementally: true);
        SetStatus(message);
    }

    private void btnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Word List File",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (openFileDialog.ShowDialog() == true)
        {
            txtFilePath.Text = openFileDialog.FileName;
            var module = ParentModule as ModuleText;
            module.CancelIncrementalLoad();
        }
    }

    private async void btnLoad_Click(object sender, RoutedEventArgs e)
    {
        string filePath = txtFilePath.Text?.Trim();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            SetStatus("Please select a file first.");
            return;
        }

        if (!File.Exists(filePath))
        {
            SetStatus("File not found.");
            return;
        }

        var module = ParentModule as ModuleText;
        if (module != null)
        {
            SetStatus("Loading phrases...");
            try
            {
                int count = await Task.Run(() => module.LoadTextFromFile(filePath));
                SetStatus($"Successfully loaded {count} phases(s) from file.");
            }
            catch (Exception ex)
            {
                SetStatus($"Error loading file: {ex.Message}");
            }
        }
        else
        {
            SetStatus("Error: Module not found.");
        }
    }

    private void btnTrigram_Click(object sender, RoutedEventArgs e)
    {
        var module = ParentModule as ModuleText;
        if (module != null)
        {
            SetStatus("Discovering common phrase structures...");
            int count = ModuleText.ProcessTheExistingText();
            SetStatus($"Found {count} learned phrase templates. You can rerun this after adding text.");
        }
        else
        {
            SetStatus("Error: Module not found.");
        }
    }

    private void tbPhrase_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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
            BtnAdd_Click(null, null);
            e.Handled = true;
        }
    }

    private void ShowPreviousPhrase()
    {
        if (phraseHistory.Count == 0) return;

        if (phraseHistoryIndex == phraseHistory.Count)
            phraseHistoryDraft = tbPhrase.Text ?? string.Empty;
        if (phraseHistoryIndex > 0)
            phraseHistoryIndex--;

        ShowPhraseHistoryEntry(phraseHistory[phraseHistoryIndex]);
    }

    private void ShowNextPhrase()
    {
        if (phraseHistory.Count == 0 || phraseHistoryIndex == phraseHistory.Count) return;

        phraseHistoryIndex++;
        string phrase = phraseHistoryIndex < phraseHistory.Count
            ? phraseHistory[phraseHistoryIndex]
            : phraseHistoryDraft;
        ShowPhraseHistoryEntry(phrase);
    }

    private void ShowPhraseHistoryEntry(string phrase)
    {
        tbPhrase.Text = phrase;
        tbPhrase.CaretIndex = tbPhrase.Text.Length;
    }
}

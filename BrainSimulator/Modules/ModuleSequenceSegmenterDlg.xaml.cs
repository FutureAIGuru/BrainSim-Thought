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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BrainSimulator.Modules;

public partial class ModuleSequenceSegmenterDlg : ModuleBaseDlg
{
    public ModuleSequenceSegmenterDlg()
    {
        InitializeComponent();
        Loaded += (_, _) => InputBox.Focus();
    }

    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;
        return true;
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        if (ParentModule is not ModuleSequenceSegmenter module) return;
        if (InputBox.Text == "exp")
        {
            module.RunVocabularyExperiment(new[] { "dog", "has", "fur", "cat"}, 5, 1000);
        }
        else
        {
            string status = module.SubmitLine(InputBox.Text);
            SetStatus(status, Colors.Black);
        }
        InputBox.SelectAll();
        InputBox.Focus();
    }

    private void btnBrowse_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFileDialog openFileDialog = new()
        {
            Title = "Select Corpus File",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (openFileDialog.ShowDialog() != true) return;

        txtFilePath.Text = openFileDialog.FileName;
        (ParentModule as ModuleSequenceSegmenter)?.CancelIncrementalLoad();
    }

    private async void btnLoad_Click(object sender, RoutedEventArgs e)
    {
        string filePath = txtFilePath.Text?.Trim();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            SetStatus("Select an existing corpus file first.");
            return;
        }
        if (ParentModule is not ModuleSequenceSegmenter module) return;

        SetStatus("Loading sequences...");
        try
        {
            int count = await Task.Run(() =>
                module.LoadTextFromFile(filePath, 1000));
            SetStatus($"Loaded and processed {count} sequence(s) from the file.");
        }
        catch (Exception ex)
        {
            SetStatus($"Error loading file: {ex.Message}");
        }
    }
}

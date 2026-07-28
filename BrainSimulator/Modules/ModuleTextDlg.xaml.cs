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
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BrainSimulator.Modules;

public partial class ModuleTextDlg : ModuleBaseDlg
{
    /// <summary>Phrases read so far from the file currently selected.</summary>
    private int totalLoaded;

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
        tbPhrase.Text = string.Empty;
        tbPhrase.Focus();

        // A question, or the name of one thing, is not an observation to learn
        // from. Either is answered instead, so that pressing Enter does the
        // sensible thing with whatever was typed.
        if (IsAskingSomething(phrase))
        {
            Say(phrase);
            return;
        }

        SetStatus(ModuleText.AddText(phrase));
    }

    private void BtnDescribe_Click(object sender, RoutedEventArgs e)
    {
        Say(tbPhrase.Text ?? string.Empty);
        tbPhrase.Focus();
    }

    private static bool IsAskingSomething(string text)
    {
        string trimmed = text.Trim().TrimEnd('.', '!');
        return trimmed.EndsWith("?", StringComparison.Ordinal) ||
            (trimmed.Length > 0 && !trimmed.Contains(' '));
    }

    /// <summary>
    /// Says what is known, either as the answer to a question or as an account
    /// of one thing. One phrase per line, because an account usually has
    /// several and running them together makes them hard to read.
    /// </summary>
    private void Say(string text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            SetStatus("Type a question, or the name of one thing.");
            return;
        }

        List<string> said;
        if (trimmed.EndsWith("?", StringComparison.Ordinal))
        {
            said = ModuleText.AnswerQuestionInEnglish(trimmed);
            // Nothing learned can phrase it, but the bare answer is still worth
            // more than silence.
            if (said.Count == 0)
                said = ModuleText.AnswerQuestion(trimmed);
        }
        else
        {
            said = ModuleText.DescribeThought(trimmed);
        }

        if (said.Count == 0)
        {
            txtSaid.Text = "";
            SetStatus(ModuleText.ExplainNothingSaid(trimmed));
            return;
        }

        txtSaid.Text = string.Join(Environment.NewLine,
            said.Select(phrase => phrase.EndsWith(".") ? phrase : phrase + "."));
        SetStatus($"{said.Count} said.", Colors.Black);
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
            totalLoaded = 0;
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
                // One press reads a fixed number of phrases so the window stays
                // responsive on a large file. Saying so avoids the trap of
                // pressing Process on half a corpus and wondering why what was
                // learned is patchy.
                const int perPress = 500;
                int count = await Task.Run(() => module.LoadTextFromFile(filePath, perPress));
                totalLoaded += count;
                if (count == perPress)
                    SetStatus($"Loaded {totalLoaded} phrases so far — press Load " +
                        "again to continue, then Process.");
                else if (count > 0)
                    SetStatus($"Loaded {totalLoaded} phrases; the file is complete. " +
                        "Now press Process.");
                else
                    SetStatus($"Nothing more to load ({totalLoaded} phrases in total).");
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
            SetStatus($"Found {count} learned phrase templates, " +
                $"{ModuleText.DescribeGrammarSummary()}. You can rerun this after adding text.");
        }
        else
        {
            SetStatus("Error: Module not found.");
        }
    }

    private void tbPhrase_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Enter)
        {
            BtnAdd_Click(null, null);
        }
    }
}

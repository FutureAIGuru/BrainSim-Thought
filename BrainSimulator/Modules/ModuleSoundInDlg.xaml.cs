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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UKS;

namespace BrainSimulator.Modules;

public partial class ModuleSoundInDlg : ModuleBaseDlg
{
    private bool _isTextChangingInternally = false;

    public ModuleSoundInDlg()
    {
        InitializeComponent();
    }

    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;
        //this has a timer so that no matter how often you might call draw, the dialog
        //only updates 10x per second
        ModuleSoundIn parent = (ModuleSoundIn)base.ParentModule;
        return true;
    }

    private void TheGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Draw(false);
    }

    private void PlayPhrase_KeyDown(object sender, KeyEventArgs e)
    {
        // Handle backspace and delete for manual editing
        if (e.Key == Key.Back || e.Key == Key.Delete)
        {
            _isTextChangingInternally = true;
            int caretIndex = playPhrase.CaretIndex;
            if (e.Key == Key.Back) caretIndex--;
            if (caretIndex < 0) caretIndex = 0;
            playPhrase.Text = playPhrase.Text.Substring(0, caretIndex);
            playPhrase.CaretIndex = caretIndex;
            e.Handled = true;
            _isTextChangingInternally = false;
            if (e.Key == Key.Back)
                PlayPhrase_TextChanged(null, null);
        }
        
        // Handle Enter key to play the phrase
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            playPhrase.SelectionLength = 0;
            PlaySelectedPhrase();
        }
    }

    private void PlayPhrase_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isTextChangingInternally) return;

        string searchText = playPhrase.Text;
        if (string.IsNullOrEmpty(searchText)) return;

        // Get the musicalPhrase thought
        var musicalPhraseThought = ThoughtLabels.GetThought("musicalPhrase");
        if (musicalPhraseThought is null) return;

        // Find children of musicalPhrase that match the search text
        var suggestion = musicalPhraseThought.Children
            .Where(child => child.Label.StartsWith(searchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(child => child.Label)
            .FirstOrDefault();

        if (suggestion is not null && !suggestion.Label.Equals(searchText, StringComparison.OrdinalIgnoreCase))
        {
            int caretIndex = playPhrase.CaretIndex;
            _isTextChangingInternally = true;
            playPhrase.Text = suggestion.Label;
            playPhrase.CaretIndex = caretIndex;
            playPhrase.SelectionStart = caretIndex;
            playPhrase.SelectionLength = suggestion.Label.Length - caretIndex;
            playPhrase.SelectionOpacity = .4;
            _isTextChangingInternally = false;
        }
    }

    private void PlaySelectedPhrase()
    {
        string phraseLabel = playPhrase.Text?.Trim();
        if (string.IsNullOrWhiteSpace(phraseLabel)) return;

        var phraseThought = ThoughtLabels.GetThought(phraseLabel);
        if (phraseThought is null)
        {
            SetStatus($"Phrase '{phraseLabel}' not found.");
            return;
        }

        // Find ModuleSoundOut to play the phrase
        var soundOutModule = MainWindow.theWindow?.activeModules.OfType<ModuleSoundOut>().FirstOrDefault();
        if (soundOutModule is null)
        {
            SetStatus("SoundOut module not found.");
            return;
        }
 
        // Play the phrase directly through SoundOut
        soundOutModule.PlayThePhrase(phraseThought,true);
        SetStatus($"Playing: {phraseLabel}");
    }

    private void SetStatus(string message)
    {
        if (statusLabel is not null)
        {
            statusLabel.Content = message;
        }
    }

    private void Dlg_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Don't handle keyboard if focus is on playPhrase textbox
        if (playPhrase.IsFocused) return;

        if (e.IsRepeat) return;
        if (sender is not ModuleSoundInDlg dlg) return;
        if (ParentModule is not ModuleSoundIn module) return;
        switch (e.Key)
        {
            case Key.Z: module.StartNote(60); break; // C4
            case Key.S: module.StartNote(61); break; // C#4
            case Key.X: module.StartNote(62); break; // D4
            case Key.D: module.StartNote(63); break; // D#4
            case Key.C: module.StartNote(64); break; // E4
            case Key.V: module.StartNote(65); break; // F4
            case Key.G: module.StartNote(66); break; // F#4
            case Key.B: module.StartNote(67); break; // G4
            case Key.H: module.StartNote(68); break; // G#4
            case Key.N: module.StartNote(69); break; // A4
            case Key.J: module.StartNote(70); break; // A#4
            case Key.M: module.StartNote(71); break; // B4
            case Key.OemComma: module.StartNote(72); break; // C5
            case Key.L: module.StartNote(73); break; // C#5
            case Key.OemPeriod: module.StartNote(74); break; // D5
            case Key.OemSemicolon: module.StartNote(75); break; // D#5
            case Key.OemQuestion: module.StartNote(76); break; // E5
        }
    }

    private void Dlg_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        // Don't handle keyboard if focus is on playPhrase textbox
        if (playPhrase.IsFocused) return;

        if (e.IsRepeat) return;
        if (sender is not ModuleSoundInDlg dlg) return;
        if (ParentModule is not ModuleSoundIn module) return;
        switch (e.Key)
        {
            case Key.Z: module.StopNote(60); break;
            case Key.S: module.StopNote(61); break;
            case Key.X: module.StopNote(62); break;
            case Key.D: module.StopNote(63); break;
            case Key.C: module.StopNote(64); break;
            case Key.V: module.StopNote(65); break;
            case Key.G: module.StopNote(66); break;
            case Key.B: module.StopNote(67); break;
            case Key.H: module.StopNote(68); break;
            case Key.N: module.StopNote(69); break;
            case Key.J: module.StopNote(70); break;
            case Key.M: module.StopNote(71); break;
            case Key.OemComma: module.StopNote(72); break;
            case Key.L: module.StopNote(73); break;
            case Key.OemPeriod: module.StopNote(74); break;
            case Key.OemSemicolon: module.StopNote(75); break;
            case Key.OemQuestion: module.StopNote(76); break;
        }
    }

    private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // If clicking on the grid (not on a control), move focus away from textbox
        //if (e.OriginalSource == sender)
        {
            Keyboard.ClearFocus();
            ((Grid)sender).Focus();
        }
    }
}


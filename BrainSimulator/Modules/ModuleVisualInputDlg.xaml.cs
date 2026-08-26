/*
 * Brain Simulator Thought
 * Copyright (c) 2026 Charles Simon
 * Licensed under the MIT License.
 */

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrainSimulator.Modules;

public partial class ModuleVisualInputDlg : ModuleBaseDlg
{
    private bool _updating;
    private List<string> _displayedLessonSteps = new();

    public ModuleVisualInputDlg()
    {
        InitializeComponent();
    }

    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;
        if (ParentModule is not ModuleVisualInput module) return false;

        _updating = true;
        var files = module.GetObservationFiles().ToList();
        ObservationBox.ItemsSource = files;
        ObservationBox.SelectedItem = files.FirstOrDefault(file =>
            file.Equals(module.SelectedObservationFile,
                System.StringComparison.OrdinalIgnoreCase)) ?? files.FirstOrDefault();

        var lessonFiles = module.GetLessonFiles().ToList();
        if (string.IsNullOrWhiteSpace(module.SelectedLessonFile) && lessonFiles.Count > 0)
            module.SelectLessonFile(lessonFiles[1]);
        LessonBox.ItemsSource = lessonFiles;
        LessonBox.SelectedItem = lessonFiles.FirstOrDefault(file => file.Equals(module.SelectedLessonFile,
                System.StringComparison.OrdinalIgnoreCase)) ?? lessonFiles.FirstOrDefault();
        LessonBox.SelectedIndex = 1;

        List<string> lessonSteps = module.LessonSteps.ToList();
        if (!_displayedLessonSteps.SequenceEqual(lessonSteps))
        {
            _displayedLessonSteps = lessonSteps;
            LessonStepsList.ItemsSource = _displayedLessonSteps;
        }
        if (_displayedLessonSteps.Count > 0)
        {
            LessonStepsList.SelectedIndex = System.Math.Min(
                module.NextLessonStepIndex,
                _displayedLessonSteps.Count - 1);
            LessonStepsList.ScrollIntoView(LessonStepsList.SelectedItem);
        }
        SelectNumericItem(IntervalBox, module.LessonStepIntervalSeconds);
        if (!WordBox.IsKeyboardFocusWithin)
            WordBox.Text = module.CurrentWord;
        if (statusLabel is not null)
            SetStatus(module.Status, Colors.Black);
        _updating = false;
        return true;
    }

    private void LessonBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updating || ParentModule is not ModuleVisualInput module) return;
        module.SelectLessonFile(LessonBox.SelectedItem as string);
        Draw(false);
    }

    private void ObservationBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updating || ParentModule is not ModuleVisualInput module) return;
        module.SelectObservationFile(ObservationBox.SelectedItem as string);
        module.PresentSelected(GetSelectedNumber(DistanceBox, double.NaN));
        Draw(false);
    }

    private void IntervalBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updating || ParentModule is not ModuleVisualInput module) return;
        double interval = GetSelectedNumber(
            IntervalBox, module.LessonStepIntervalSeconds);
        if (double.IsFinite(interval) && interval > 0)
            module.LessonStepIntervalSeconds = interval;
    }

    private void Present_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is ModuleVisualInput module)
            module.PresentSelected(GetSelectedNumber(DistanceBox, double.NaN));
    }

    private void Imagine_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is ModuleVisualInput module)
            module.ImagineWord(
                WordBox.Text,
                GetSelectedNumber(DistanceBox, double.NaN));
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is ModuleVisualInput module)
            module.ClearPresentation();
    }

    private void StepLesson_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is ModuleVisualInput module)
            module.StepLesson();
    }

    private void RunLesson_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is not ModuleVisualInput module) return;
        double interval = GetSelectedNumber(
            IntervalBox, module.LessonStepIntervalSeconds);
        module.StartLesson(interval);
    }

    private void PauseLesson_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is ModuleVisualInput module)
            module.PauseLesson();
    }

    private void ResetLesson_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is ModuleVisualInput module)
            module.ResetLesson();
    }

    private static double GetSelectedNumber(ComboBox comboBox, double fallback)
    {
        string text = comboBox.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString()
            : comboBox.Text;
        return double.TryParse(text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;
    }

    private static void SelectNumericItem(ComboBox comboBox, double value)
    {
        ComboBoxItem match = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => double.TryParse(
                    item.Content?.ToString(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double itemValue) &&
                System.Math.Abs(itemValue - value) < 0.001);
        if (match is not null)
            comboBox.SelectedItem = match;
    }
}

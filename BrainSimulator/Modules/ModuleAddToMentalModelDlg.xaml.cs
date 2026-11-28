using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using UKS;

namespace BrainSimulator.Modules;

public partial class ModuleAddToMentalModelDlg : ModuleBaseDlg
{
    private const int HistoryLimit = 8;
    private readonly List<string> _history = new();
    private bool _isTextChangingInternally;

    public ModuleAddToMentalModelDlg()
    {
        InitializeComponent();
    }

    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;
        return true;
    }

    private void ThoughtBox_Loaded(object sender, RoutedEventArgs e)
    {
        LoadHistory((ParentModule as ModuleBase)?.GetSavedDlgAttribute("ThoughtHistory"));
        AttachTextEvents();
        UpdateHistoryItems();
    }

    private void AttachTextEvents()
    {
        if (ThoughtBox.Template.FindName("PART_EditableTextBox", ThoughtBox) is TextBox tb)
        {
            tb.PreviewKeyDown -= ThoughtBox_PreviewKeyDown;
            tb.TextChanged    -= ThoughtBox_TextChanged;

            tb.PreviewKeyDown += ThoughtBox_PreviewKeyDown;
            tb.TextChanged    += ThoughtBox_TextChanged;
        }
    }

    private void ThoughtBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;

        if (e.Key == System.Windows.Input.Key.Enter)
        {
            AddToHistory(tb.Text);
            tb.Select(tb.Text.Length, 0);
            e.Handled = true;
        }
    }

    private void ThoughtBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isTextChangingInternally) return;
        if (sender is not TextBox tb) return;

        string searchText = tb.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(searchText))
        {
            string suggestion = MainWindow.theUKS.LabeledStartingWith(searchText).FirstOrDefault()?.Label;

            if (suggestion is not null && !suggestion.Equals(searchText, StringComparison.OrdinalIgnoreCase))
            {
                int caret = tb.CaretIndex;
                _isTextChangingInternally = true;
                tb.Text = suggestion;
                tb.CaretIndex = caret;
                tb.SelectionStart = caret;
                tb.SelectionLength = suggestion.Length - caret;
                tb.SelectionOpacity = 0.4;
                _isTextChangingInternally = false;
            }
        }
    }

    private void AddToHistory(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        int existing = _history.FindIndex(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _history.RemoveAt(existing);
        _history.Insert(0, value);
        if (_history.Count > HistoryLimit)
            _history.RemoveRange(HistoryLimit, _history.Count - HistoryLimit);

        UpdateHistoryItems();
        (ParentModule as ModuleBase)?.SetSavedDlgAttribute("ThoughtHistory", string.Join("|", _history));
    }

    private void LoadHistory(string raw)
    {
        _history.Clear();
        if (!string.IsNullOrWhiteSpace(raw))
            _history.AddRange(raw.Split('|').Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private void UpdateHistoryItems()
    {
        _isTextChangingInternally = true;
        ThoughtBox.ItemsSource = null;
        ThoughtBox.ItemsSource = _history.ToList();
        _isTextChangingInternally = false;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is not ModuleAddToMentalModel mod) return;

        string label = ThoughtBox.Text?.Trim();
        if (!double.TryParse(AzimuthBox.Text, out double az)) az = 0;
        if (!double.TryParse(ElevationBox.Text, out double el)) el = 0;
        if (!double.TryParse(DistanceBox.Text, out double dist)) dist = 1;

        mod.AddThoughtToMentalModel(label, az, el, dist);
        AddToHistory(label);
    }
}
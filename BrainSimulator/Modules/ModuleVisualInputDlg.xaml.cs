/*
 * Brain Simulator Thought
 * Copyright (c) 2026 Charles Simon
 * Licensed under the MIT License.
 */

using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BrainSimulator.Modules;

public partial class ModuleVisualInputDlg : ModuleBaseDlg
{
    private bool _updating;

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
        StatusText.Text = module.Status;
        _updating = false;
        return true;
    }

    private void ObservationBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_updating || ParentModule is not ModuleVisualInput module) return;
        module.SelectObservationFile(ObservationBox.SelectedItem as string);
        Draw(false);
    }

    private void Present_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is not ModuleVisualInput module) return;
        double distance = double.TryParse(DistanceBox.Text, out double value)
            ? value
            : double.NaN;
        module.PresentSelected(distance);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is ModuleVisualInput module)
            module.ClearPresentation();
    }
}

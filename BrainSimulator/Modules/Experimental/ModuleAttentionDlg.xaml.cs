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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using UKS;

namespace BrainSimulator.Modules;

public partial class ModuleAttentionDlg : ModuleBaseDlg
{
    public ModuleAttentionDlg()
    {
        InitializeComponent();
    }

    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;

        if (ParentModule is ModuleAttention parent)
        {
            HorizontalSlider.Value = parent.CenterAzimuthDeg.Degrees;
            VerticalSlider.Value = parent.CenterElevationDeg.Degrees;
            UpdateValueLabels();
            if (FocusValue is not null)
                FocusValue.Text = parent.CurrentFocus?.Label ?? "(none)";
        }

        return true;
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ParentModule is not ModuleAttention parent) return;

        parent.SetCenterOfAttention(Angle.FromDegrees((float)HorizontalSlider.Value), Angle.FromDegrees((float)VerticalSlider.Value));
        UpdateValueLabels();
    }

    private void UpdateValueLabels()
    {
        if (HorizontalValue is not null)
            HorizontalValue.Text = $"{HorizontalSlider.Value:0.0}°";
        if (VerticalValue is not null)
            VerticalValue.Text = $"{VerticalSlider.Value:0.0}°";
    }

    private void TheGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Draw(false);
    }
}
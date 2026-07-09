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
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BrainSimulator.Modules;

public partial class ModuleLearnMelodyDlg : ModuleBaseDlg
{
    public ModuleLearnMelodyDlg()
    {
        InitializeComponent();
    }

    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;
        // this has a timer so that no matter how often you might call draw, the dialog
        // only updates 10x per second
        ModuleLearnMelody parent = (ModuleLearnMelody)base.ParentModule;
        return true;
    }

    private void btnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Melody File",
            Filter = "Text Files (*.txti)|*.txt|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (openFileDialog.ShowDialog() == true)
        {
            txtFilePath.Text = openFileDialog.FileName;
        }
    }

    private void btnLoad_Click(object sender, RoutedEventArgs e)
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

        var module = ParentModule as ModuleLearnMelody;
        if (module != null)
        {
            SetStatus("Loading melody...");

            int count = module.LoadMelodiesFromFile(filePath);

            SetStatus($"Melody loaded from file: {Path.GetFileName(filePath)}");
        }
        else
        {
            SetStatus("Error: Module not found.");
        }
    }

    private void TheGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Draw(false);
    }

    private new void SetStatus(string message, Color? color = null)
    {
        if (statusLabel != null)
        {
            statusLabel.Content = message;
            statusLabel.Foreground = new SolidColorBrush(color ?? Colors.Black);
        }
    }
}
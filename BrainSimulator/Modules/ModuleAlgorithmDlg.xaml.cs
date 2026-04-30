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

using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UKS;

namespace BrainSimulator.Modules;

public partial class ModuleAlgorithmDlg : ModuleBaseDlg
{
    private bool _isTextChangingInternally = false;
    private int _previousTextLength = 0;

    public ModuleAlgorithmDlg()
    {
        InitializeComponent();
        executeButton.Click += ExecuteButton_Click;
        parameter1Input.KeyDown += Param1_KeyDown;
        parameter2Input.KeyDown += Param2_KeyDown;
        taskInput.TextChanged += TaskInput_TextChanged;
        taskInput.PreviewKeyDown += TaskInput_PreviewKeyDown;
    }

    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;
        //this has a timer so that no matter how often you might call draw, the dialog
        //only updates 10x per second
        ModuleAlgorithm parent = (ModuleAlgorithm)base.ParentModule;
        return true;
    }

    private void TheGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Draw(false);
    }

    private void TaskInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
       var tb = taskInput as TextBox;
        if (tb is null) return;

        // Allow text changes when keys like backspace, delete are pressed
        if (e.Key == Key.Back || e.Key == Key.Delete)
        {
            _isTextChangingInternally = true;
            int caretIndex = tb.CaretIndex;
            if (e.Key == Key.Back) caretIndex--;
            if (caretIndex < 0) caretIndex = 0;
            tb.Text = tb.Text.Substring(0, caretIndex);
            tb.CaretIndex = caretIndex;
            e.Handled = true;
            _isTextChangingInternally = false;
            if (e.Key == Key.Back)
                TaskInput_TextChanged(null, null);
        }

        if (e.Key == Key.Enter)
        {
            parameter1Input.Focus();
        }
    }

    private void TaskInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isTextChangingInternally)
            return;

        var tb = taskInput as TextBox;
        if (tb == null) return;

        string searchText = taskInput.Text;
        
        // Track only the unselected (typed) portion
        int actualTypedLength = tb.SelectionStart;
        
        // Check if we're deleting text
        bool isDeleting = actualTypedLength < _previousTextLength;
        
        // Only autocomplete if text is being added (not deleted)
        if (string.IsNullOrEmpty(searchText) || isDeleting)
        {
            _previousTextLength = actualTypedLength;
            return;
        }

        // Update previous length before autocomplete might change it
        _previousTextLength = actualTypedLength;

        ModuleAlgorithm parent = (ModuleAlgorithm)base.ParentModule;
        if (parent?.theUKS == null)
            return;

        // Get all children of "Task" excluding "Variable"
        Thought taskThought = parent.theUKS.Labeled("Task");
        if (taskThought == null)
            return;

        // Get the text that was actually typed (without selection)
        string typedText = searchText.Substring(0, actualTypedLength);

        var suggestion = taskThought.Children
            .Where(t => t.Label.ToLower() != "variable" &&
                       t.Label.StartsWith(typedText, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Label)
            .Select(t => t.Label)
            .FirstOrDefault();

        if (suggestion != null && !suggestion.Equals(searchText, System.StringComparison.OrdinalIgnoreCase))
        {
            int caretIndex = tb.CaretIndex;
            _isTextChangingInternally = true;
            taskInput.Text = suggestion;
            tb.CaretIndex = caretIndex;
            tb.SelectionStart = caretIndex;
            tb.SelectionLength = suggestion.Length - caretIndex;
            tb.SelectionOpacity = .4;
            _isTextChangingInternally = false;
        }
    }

    private void Param2_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteButton_Click(sender, e);
            e.Handled = true;
        }
    }
    private void Param1_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            parameter2Input.Focus();
        }
    }

    private void ExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        ModuleAlgorithm parent = (ModuleAlgorithm)base.ParentModule;

        if (parent?.theUKS == null) return;

        string taskName = taskInput.Text?.Trim();
        if (string.IsNullOrEmpty(taskName))
        {
            SetStatus("No task specified");
            return;
        }

        string param1 = parameter1Input.Text?.Trim() ?? "";
        string param2 = parameter2Input.Text?.Trim() ?? "";

        // Execute the task using the module's execution engine
        bool success = parent.ExecuteTask(taskName, param1, param2);

        if (success)
        {
            if (parent.LastLinkWritten != null)
            {
                SetStatus($"Task complete: {parent.LastLinkWritten}");
            }
            else
            {
                SetStatus("Task execution complete");
            }
        }
        else
        {
            SetStatus("Task execution failed");
        }
    }
}
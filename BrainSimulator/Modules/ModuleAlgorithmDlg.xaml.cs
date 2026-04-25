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
using System.Windows;
using System.Windows.Input;
using UKS;

namespace BrainSimulator.Modules;

public partial class ModuleAlgorithmDlg : ModuleBaseDlg
{
    public ModuleAlgorithmDlg()
    {
        InitializeComponent();
        addStepButton.Click += AddStepButton_Click;
        executeButton.Click += ExecuteButton_Click;
        newStepInput.KeyDown += NewStepInput_KeyDown;
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

    private void NewStepInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddStepButton_Click(sender, e);
            e.Handled = true;
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
    

    private void AddStepButton_Click(object sender, RoutedEventArgs e)
    {
        ModuleAlgorithm parent = (ModuleAlgorithm)base.ParentModule;
        if (parent?.theUKS == null) return;

        string taskName = taskInput.Text?.Trim();
        string newStepText = newStepInput.Text?.Trim();

        if (string.IsNullOrEmpty(taskName) || string.IsNullOrEmpty(newStepText)) return;
        if (!newStepText.StartsWith("[")) newStepText = "[" + newStepText;
        if (!newStepText.EndsWith("]")) newStepText = newStepText + "]";

        // Parse the new step text using UKS.TextFile parser
        Thought stepThought = parent.theUKS.ProcessSingleLine(newStepText);
        if (stepThought == null)
        {
            SetStatus("Could not parse step");
            return;
        }

        // Get or create the task thought
        Thought taskThought = parent.theUKS.Labeled(taskName);
        SeqElement lastStep = null;
        if (taskThought is not null && taskThought.HasLink("steps") is not null)
        {
            lastStep = (SeqElement)taskThought.GetTargetOfFirstLinkOfType("steps");
            if (lastStep is not null)
            {
                lastStep = parent.theUKS.GetLastlement(lastStep);
                parent.theUKS.AddElement(lastStep, stepThought);
            }
            else
            {
                throw new InvalidDataException("Missing sequence on task");
            }
        }
        else
        {
            parent.theUKS.GetOrAddThought("Task", "Thought");
            parent.theUKS.GetOrAddThought("steps", "Task");
            taskThought = parent.theUKS.GetOrAddThought(taskName, "Task");
            taskThought.AddParent("Task");
            // Createthe sequence and link to it
            Thought firstStep = parent.theUKS.CreateFirstElement(taskName, stepThought);
            taskThought.AddLink("steps", firstStep);
        }

        // Clear the new step input
        newStepInput.Text = "";
    }
}
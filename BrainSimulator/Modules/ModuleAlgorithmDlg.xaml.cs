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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
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

        // Get the task thought
        Thought taskThought = parent.theUKS.Labeled(taskName);
        if (taskThought is null)
        {
            SetStatus("Task not found");
            return;
        }

        // Get the first step of the task's sequence
        Thought pc = parent.theUKS.GetOrAddThought("PC", "Task");
        SeqElement currentStep = (SeqElement)taskThought.GetTargetOfFirstLinkOfType("steps");
        pc.RemoveLinks("is");
        pc.AddLink("is", currentStep);
        if (currentStep is null)
        {
            SetStatus("Task has no steps");
            return;
        }

        // Loop through all steps
        while (currentStep is not null)
        {
            // Get the action from the current step
            Link action = (Link)currentStep.VLU;
            Link newLink = new(action.From, action.LinkType, action.To);
            string toLabel = action.To?.Label.ToLower();
            Thought newTarget = null;
            if (toLabel == "param1")
                newTarget = parent.theUKS.Labeled(parameter1Input.Text);
            if (toLabel == "param2")
                newTarget = parent.theUKS.Labeled(parameter2Input.Text);
            string[] parts = toLabel.Split(".");
            if (parts.Length > 1)
            {
                newTarget = parent.theUKS.Labeled(parts[0]);
                for (int i = 1; i < parts.Length; i++)
                {
                    Thought linkType = parent.theUKS.Labeled(parts[i]);
                    newTarget = newTarget?.GetTargetOfFirstLinkOfType(linkType);
                }
            }
            if (newTarget is not null)
                newLink.To = newTarget; //this changes the value not the programp1

            //handle goto statements
            if (newLink.From.Label.ToLower() == "pc")
            {
                string[] parts1 = currentStep.Label.Split("-");
                string newPCLabel = parts1[0] + "-" + newLink.To.Label;
                Thought newStep = parent.theUKS.Labeled(newPCLabel);
                if (newStep is not null)
                {
                    currentStep = newStep as SeqElement;
                    pc.RemoveLinks("is");
                    pc.AddLink("is", currentStep);
                }
            }
            else
            { // perhaps this is the only type we support??
                if (newLink.LinkType.Label.Trim() == "set")
                {
                    newLink.LinkType = parent.theUKS.GetOrAddThought("is");
                    newLink.From.RemoveLinks("is");
                    newLink.From.AddLink("is", newLink.To);
                }
                // Move to the next step
                currentStep = currentStep?.NXT;
                pc.RemoveLinks("is");
                pc.AddLink("is", currentStep);
            }
        }

        SetStatus("Task execution complete");
    }

    bool ConditionIsTrue(Link r1)
    {
        return false;
    }

    private void AddStepButton_Click(object sender, RoutedEventArgs e)
    {
        ModuleAlgorithm parent = (ModuleAlgorithm)base.ParentModule;
        if (parent?.theUKS == null) return;

        string taskName = taskInput.Text?.Trim();
        string newStepText = newStepInput.Text?.Trim();

        if (string.IsNullOrEmpty(taskName) || string.IsNullOrEmpty(newStepText)) return;

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
        if (taskThought is not null)
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
            // Createthe sequence and link to it
            Thought firstStep = parent.theUKS.CreateFirstElement(taskName, stepThought);
            taskThought.AddLink("steps", firstStep);
        }

        // Clear the new step input
        newStepInput.Text = "";
    }
}
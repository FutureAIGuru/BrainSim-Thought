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
        SeqElement currentStep = (SeqElement)taskThought.GetTargetOfFirstLinkOfType("steps");
        if (currentStep is null)
        {
            SetStatus("Task has no steps");
            return;
        }

        // Loop through all steps
        bool flowControl = ExecuteTask(ref currentStep);
        if (!flowControl)
        {
            return;
        }

        //SetStatus("Task execution complete");
    }

    private bool ExecuteTask(ref SeqElement currentStep)
    {
        ModuleAlgorithm parent = (ModuleAlgorithm)base.ParentModule;
        parent.theUKS.GetOrAddThought("retVal", "Task");
        while (currentStep is not null)
        {
            // Get the action from the current step
            if (currentStep.VLU is Link action)
            {
                Link newLink = new(action.From, action.LinkType, action.To);
                string toLabel = action.To?.Label.ToLower();
                string fromLabel = action.From?.Label.ToLower();
                string linkLabel = action.LinkType?.Label.ToLower();
                Thought newTarget = null;
                newTarget = ParseIndirection(toLabel);
                if (newTarget is not null)
                    newLink.To = newTarget; //this changes the value not the programp1
                Thought newFrom = ParseIndirection(fromLabel);

                if (action.LinkType.HasAncestor("write"))
                {
                    Thought newLinkType = action.LinkType.GetTargetOfFirstLinkOfType("is");
                    newFrom.RemoveLinks(newLinkType);
                    newLink = newFrom.AddLink(newLinkType, newTarget);
                    //newLink.TimeToLive = TimeSpan.FromSeconds(5);
                    SetStatus("Link Written: " + newLink.ToString());
                }
                else
                {
                    SetStatus("Invalid Operator: " + action.LinkType.ToString());
                }
                // Move to the next step
                //if currentStep is null, we are done with this task...check for a return addded to the task and if so, return to it
                if (currentStep.NXT is null)
                { //RETURN
                    Thought retVal = GetReturn(currentStep.FRST);
                    currentStep = (SeqElement)retVal;
                }
                currentStep = currentStep?.NXT;
            }
            else
            {
                //it's not a link...must be a context to evaluate  (or a call???)
                Thought action1 = currentStep.VLU as Thought;
                if (action1.HasLink("steps") is not null) //CALL
                {  //CALL
                    Thought retVal = currentStep;
                    // jump to the new task
                    currentStep = (SeqElement)action1.GetTargetOfFirstLinkOfType("steps");
                    // write the return address (currentStep.NXT) in action.retval  
                    SetReturn(currentStep, retVal);
                }
                else  //CONTEXT Test
                { //JUMP (computed)
                    Thought response = EvaluateContext(action1);
                    Thought retVal = GetReturn(currentStep.FRST);
                    if (response is null)
                    {
                        currentStep = null;
                    }
                    else
                    {
                        currentStep = (SeqElement)response.GetTargetOfFirstLinkOfType("steps");
                        SetReturn(currentStep, retVal);
                    }
                }
            }
        }
        return true;
    }
    private void SetReturn(Thought current, Thought retVal)
    {
        current.RemoveLinks("retVal");
        Link l = current.AddLink("retVal", retVal);
        //l.TimeToLive = TimeSpan.FromSeconds(30);
    }
    private Thought GetReturn(Thought current)
    {
        Thought retVal = current.GetTargetOfFirstLinkOfType("retVal");
        current.RemoveLinks("retVal");
        return retVal;
    }

    private Thought EvaluateContext(Thought contextRoot)
    {
        ModuleAlgorithm parent = (ModuleAlgorithm)base.ParentModule;
        Thought bestResponse = null;
        float bestWeight = 0;
        foreach (Thought t in contextRoot.Children)
        {
            float weight = 0;
            foreach (Link l in t.LinksTo.Where(x => x.LinkType.Label == "has"))
            {
                Link test = (Link)l.To;
                if (test.LinkType.HasAncestor("exist"))
                {
                    bool not = false;
                    if (test.LinkType.HasAncestor("not")) not = true;
                    Thought testType = test.LinkType.GetTargetOfFirstLinkOfType("is");
                    var src = ParseIndirection(test.From.Label);
                    if (src is null) continue;
                    if (test.To.Label == "??")
                    {
                        if (!not && src.HasLink(testType) is not null) weight++;
                        if (not && src.HasLink(testType) is null) weight++;
                    }
                    else
                    {
                        Thought target = ParseIndirection(test.To.Label);
                        if (!not && src.HasLink(testType, target) is not null) weight++;
                        if (not && src.HasLink(testType, target) is null) weight++;
                    }
                }
            }
            if (weight > bestWeight)
            {
                bestResponse = t.LinksTo.FindFirst(x => x.LinkType.Label == "response")?.To;
                bestWeight = weight;
            }
        }

        return bestResponse;
    }


    private Thought ParseIndirection(string toLabel)
    {
        if (string.IsNullOrEmpty(toLabel)) return null;
        ModuleAlgorithm parent = (ModuleAlgorithm)base.ParentModule;
        if (toLabel == "param1")
            return parent.theUKS.Labeled(parameter1Input.Text);
        if (toLabel == "param2")
            return parent.theUKS.Labeled(parameter2Input.Text);

        string[] parts = toLabel.Split(".");
        Thought newTarget = parent.theUKS.Labeled(parts[0]);
        for (int i = 1; i < parts.Length; i++)
        {
            Thought linkType = parent.theUKS.Labeled(parts[i]);
            newTarget = newTarget?.GetTargetOfFirstLinkOfType(linkType);
        }
        return newTarget;
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
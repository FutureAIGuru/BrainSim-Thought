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
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

public class ModuleAlgorithm : ModuleBase
{
    private string param1;
    private string param2;
    private TimeSpan linkTimeToLive = TimeSpan.FromSeconds(5);
    
    /// <summary>
    /// The last link written during task execution, used as a return value
    /// </summary>
    public Link LastLinkWritten { get; private set; }

    // Fill this method in with code which will execute
    // once for each cycle of the engine
    public override void Fire()
    {
        Init();

        UpdateDialog();
    }

    // Fill this method in with code which will execute once
    // when the module is added, when "initialize" is selected from the context menu,
    // or when the engine restart button is pressed
    public override void Initialize()
    {
    }

    // called whenever the UKS performs an Initialize()
    public override void UKSInitializedNotification()
    {
        //initialization stuff  MOVE
        theUKS.GetOrAddThought("Task", "Thought");
        theUKS.GetOrAddThought("Context", "Task");
        theUKS.GetOrAddThought("EXIST", "LinkType");
        theUKS.GetOrAddThought("WRITE", "LinkType");
        theUKS.GetOrAddThought("EQ", "Comparison");
        theUKS.GetOrAddThought("GT", "Comparison");

        theUKS.CreateThoughtFromMultipleAttributes("write EQ", true);
        theUKS.CreateThoughtFromMultipleAttributes("write GT", true);
        theUKS.CreateThoughtFromMultipleAttributes("write is", true);
    }

    /// <summary>
    /// Execute a task with the given parameters
    /// </summary>
    /// <param name="taskName">Name of the task to execute</param>
    /// <param name="param1">First parameter value</param>
    /// <param name="param2">Second parameter value</param>
    /// <returns>True if execution succeeded, false otherwise</returns>
    public bool ExecuteTask(string taskName, string param1 = "", string param2 = "")
    {
        linkTimeToLive = TimeSpan.FromSeconds(60);
        LastLinkWritten = null;
        
        if (theUKS == null) return false;

        this.param1 = param1;
        this.param2 = param2;

        // Get the task thought
        Thought taskThought = theUKS.Labeled(taskName);
        if (taskThought is null)
        {
            Debug.WriteLine($"Task not found: {taskName}");
            return false;
        }

        // Get the first step of the task's sequence
        SeqElement currentStep = taskThought.GetTargetOfFirstLinkOfType("steps") as SeqElement;
        if (currentStep is null)
        {
            Debug.WriteLine($"Task has no steps: {taskName}");
            return false;
        }

        theUKS.GetOrAddThought("retVal", "Task");

        // Execute the task
        return ExecuteSteps(ref currentStep);
    }

    private bool ExecuteSteps(ref SeqElement currentStep)
    {
        while (currentStep is not null)
        {
            // Get the action from the current step
            if (currentStep.VLU is Link action)
            {
                Debug.Write("Instruction: " + action.ToString() + "   ");
                Link newLink = new(action.From, action.LinkType, action.To);
                string toLabel = action.To?.Label.ToLower();
                string fromLabel = action.From?.Label.ToLower();
                string linkLabel = action.LinkType?.Label.ToLower();
                Thought newTarget = null;
                newTarget = ParseIndirection(toLabel);
                if (newTarget is not null)
                    newLink.To = newTarget; //this changes the value not the programp1
                Thought newFrom = ParseIndirection(fromLabel);

                if (action.LinkType.HasAncestor("write") && newFrom is not null)
                {
                    Thought newLinkType = action.LinkType.GetTargetOfFirstLinkOfType("is");
                    newFrom.RemoveLinks(newLinkType);
                    newLink = newFrom.AddLink(newLinkType, newTarget);
                    newLink.TimeToLive = linkTimeToLive;
                    Debug.WriteLine("Link Written: " + newLink.ToString());
                    LastLinkWritten = newLink; // <-- Save the last link written
                }
                else
                {
                    Debug.WriteLine("Invalid Operator: " + action.LinkType.ToString());
                }
                // Move to the next step
                //if currentStep is null, we are done with this task...check for a return added to the task and if so, return to it
                if (currentStep.NXT is null)
                { //RETURN
                    Thought retVal = GetReturn(currentStep.FRST);
                    currentStep = (SeqElement)retVal;
                    Debug.WriteLine("Returning to: " + currentStep?.ToString());
                }
                currentStep = currentStep?.NXT;
            }
            else if (currentStep.VLU is Thought action1)
            {
                //it's not a link...must be a context to evaluate  (or a call or end)
                Debug.Write("Instruction: " + action1.ToString() + "   ");
                if (action1.HasLink("steps") is not null) //CALL
                {  //CALL
                    Thought retVal = currentStep;
                    // jump to the new task
                    currentStep = (SeqElement)action1.GetTargetOfFirstLinkOfType("steps");
                    // write the return address (currentStep.NXT) in action.retval  
                    SetReturn(currentStep, retVal);
                    Debug.WriteLine("Calling: " + currentStep.ToString());
                }
                else if (action1.Label.ToLower() == "end")
                {
                    SetReturn(currentStep, null);
                    currentStep = null;
                    Debug.WriteLine("End of Task");
                }
                else  //CONTEXT Test
                { //JUMP (computed)
                    Thought response = EvaluateContext(action1);
                    Thought retVal = GetReturn(currentStep.FRST);
                    Debug.WriteLine("Evaluating Context: " + action1?.ToString());
                    Debug.WriteLine("Jumping to: " + currentStep?.ToString());
                    if (response is null)
                    {
                        //TODO check for return
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
        l.TimeToLive = linkTimeToLive;
    }

    private Thought GetReturn(Thought current)
    {
        Thought retVal = current.GetTargetOfFirstLinkOfType("retVal");
        current.RemoveLinks("retVal");
        return retVal;
    }

    private Thought EvaluateContext(Thought contextRoot)
    {
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
        
        if (toLabel == "param1")
            return theUKS.Labeled(param1);
        if (toLabel == "param2")
            return theUKS.Labeled(param2);

        string[] parts = toLabel.Split(".");
        Thought newTarget = theUKS.Labeled(parts[0]);
        for (int i = 1; i < parts.Length; i++)
        {
            Thought linkType = theUKS.Labeled(parts[i]);
            newTarget = newTarget?.GetTargetOfFirstLinkOfType(linkType);
        }
        return newTarget;
    }
}
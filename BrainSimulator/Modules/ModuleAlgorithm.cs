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
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;
    
public class ModuleAlgorithm : ModuleBase
{
    private TimeSpan linkTimeToLive = TimeSpan.FromSeconds(5);
    private SeqElement currentStep = null;

    /// <summary>
    /// Controls whether execution happens one step at a time (true) or at full speed (false)
    /// </summary>
    public bool IsSingleStepMode { get; set; } = false;

    /// <summary>
    /// Expose current step for UI to check execution state
    /// </summary>
    public SeqElement CurrentStep => currentStep;

    /// <summary>
    /// The last link written during task execution, used as a return value
    /// </summary>
    public Link LastLinkWritten { get; private set; }
    public string LastAction = "";
    public int CycleCount = 0;

    // Fill this method in with code which will execute
    // once for each cycle of the engine
    public override void Fire()
    {
        Init();

        // Only execute steps if not in single-step mode
        if (currentStep is not null && !IsSingleStepMode)
        {
            ExecuteSingleStep();
            CycleCount++;
            if (currentStep is null)
                LastAction = $"TASK COMPLETE ({CycleCount} cycles): {LastLinkWritten.ToString()}";
        }

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
    /// <param name="param1Value">First parameter value (can be any string)</param>
    /// <param name="param2Value">Second parameter value (can be any string)</param>
    /// <param name="executeImmediately">If true, executes all steps immediately (for testing). If false, 
    /// lets the polling loop or single-step execute steps.</param>
    /// <returns>True if execution succeeded, false otherwise</returns>
    public bool ExecuteTask(string taskName, string param1Value = "", string param2Value = "", bool executeImmediately = true)
    {
        // Set link time-to-live based on execution mode
        // Single-step mode gets longer TTL since user is manually stepping through
        linkTimeToLive = IsSingleStepMode ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(10);
        LastLinkWritten = null;
        CycleCount = 0;

        if (theUKS == null) return false;

        // Get or create param1 and param2 thoughts
        Thought param1Thought = theUKS.GetOrAddThought("param1", "Variable");
        Thought param2Thought = theUKS.GetOrAddThought("param2", "Variable");
        Thought isType = theUKS.GetOrAddThought("is", "LinkType");

        // Process param1
        if (!string.IsNullOrEmpty(param1Value))
        {
            Thought param1ValueThought = theUKS.Labeled(param1Value);
            if (param1ValueThought == null)
            {
                // Create new thought with spelling sequence
                param1ValueThought = theUKS.GetOrAddThought(param1Value, "Thing");
                CreateSpellingSequence(param1Value, param1ValueThought);
            }
            // Link: param1 -> is -> param1ValueThought
            param1Thought.RemoveLinks(isType);
            param1Thought.AddLink(isType, param1ValueThought);
        }

        // Process param2
        if (!string.IsNullOrEmpty(param2Value))
        {
            Thought param2ValueThought = theUKS.Labeled(param2Value);
            if (param2ValueThought == null)
            {
                // Create new thought with spelling sequence
                param2ValueThought = theUKS.GetOrAddThought(param2Value, "Thing");
                CreateSpellingSequence(param2Value, param2ValueThought);
            }
            // Link: param2 -> is -> param2ValueThought
            param2Thought.RemoveLinks(isType);
            param2Thought.AddLink(isType, param2ValueThought);
        }

        // Get the task thought
        Thought taskThought = theUKS.Labeled(taskName);
        if (taskThought is null)
        {
            Debug.WriteLine($"Task not found: {taskName}");
            return false;
        }

        // Get the first step of the task's sequence
        currentStep = taskThought.GetTargetOfFirstLinkOfType("steps") as SeqElement;
        
        // If no steps found, try appending "_main" to the task name
        if (currentStep is null)
        {
            string mainTaskName = taskName + "_main";
            Thought mainTaskThought = theUKS.Labeled(mainTaskName);
            if (mainTaskThought != null)
            {
                currentStep = mainTaskThought.GetTargetOfFirstLinkOfType("steps") as SeqElement;
                if (currentStep != null)
                {
                    Debug.WriteLine($"Using main task: {mainTaskName}");
                    taskThought = mainTaskThought; // Use the _main task
                }
            }
        }
        
        if (currentStep is null)
        {
            Debug.WriteLine($"Task has no steps: {taskName}");
            return false;
        }

        theUKS.GetOrAddThought("retVal", "Variable");

        // Execute immediately for tests, or let the polling loop handle it
        if (executeImmediately)
            return ExecuteSteps();
        
        return true;
    }

    private void CreateSpellingSequence(string word, Thought wordThought)
    {
        List<Thought> letters = new List<Thought>();
        foreach (char c in word.ToUpper())
        {
            string letterName = c.ToString();
            Thought letterThought = theUKS.GetOrAddThought(letterName, "Letter");
            letters.Add(letterThought);
        }
        
        if (letters.Count > 0)
        {
            SeqElement spellingSeq = theUKS.AddSequence(word + "-spelling", letters);
            if (spellingSeq != null)
            {
                spellingSeq.Label = word + "-seq0";
                Thought spelledType = theUKS.GetOrAddThought("spelled", "LinkType");
                wordThought.AddLink(spelledType, spellingSeq);
            }
        }
    }

    private bool ExecuteSteps()
    {
        while (currentStep is not null)
        {
            ExecuteSingleStep();
        }
        return true;
    }
    
    public void ExecuteSingleStep()
    {
        if (currentStep is null) return;

        // Get the action from the current step
        if (currentStep.VLU is Link action)
        {
            Thought newTarget = ParseIndirection(action.To);
            Thought newFrom = ParseIndirection(action.From);

            if (action.LinkType.HasAncestor("write") && newFrom is not null)
            {
                Thought newLinkType = action.LinkType.GetTargetOfFirstLinkOfType("is");
                newFrom.RemoveLinks(newLinkType);
                Link newLink = newFrom.AddLink(newLinkType, newTarget);
                newLink.TimeToLive = linkTimeToLive;
                LastLinkWritten = newLink; // <-- Save the last link written
                LastAction = $"Step {CycleCount}: {newLink.ToString()}";
            }
            else
            {
                Debug.WriteLine("Invalid Operator: " + action.LinkType.ToString());
                LastAction = $"Invalid Operator: {action.LinkType.ToString()}";
            }
            // Move to the next step
            //if currentStep is null, we are done with this task...check for a return added to the task and if so, return to it
            if (currentStep.NXT is null)
            { //RETURN
                Thought retVal = GetReturn(currentStep.FRST);
                currentStep = (SeqElement)retVal;
            }
            currentStep = currentStep?.NXT;
        }
        else if (currentStep.VLU is null)
        {
            currentStep = null;
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
                LastAction = $"Calling: {action1.Label}";
            }
            else if (action1.Label.ToLower() == "end")
            {
                SetReturn(currentStep, null);
                currentStep = null;
                Debug.WriteLine("End of Task");
                LastAction = "End of Task";
            }
            else  //CONTEXT 
            { //JUMP (computed)
                Thought response = EvaluateContext(action1);
                Thought retVal = GetReturn(currentStep.FRST);
                if (response is null)
                {
                    //TODO check for return
                    currentStep = null;
                    LastAction = $"Context {action1.Label}: No response";
                }
                else
                {
                    currentStep = (SeqElement)response.GetTargetOfFirstLinkOfType("steps");
                    if (currentStep is not null)
                        SetReturn(currentStep, retVal);
                    LastAction = $"Context {action1.Label}: Jump to {response.Label}";
                }
            }
        }
        
        CycleCount++;
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
                    //Thought testType = test.LinkType.GetTargetOfFirstLinkOfType("is");
                    Thought testType = test.LinkType.LinksTo.FindFirst(x => x.LinkType.Label.ToLower() == "is" && x.To.Label != "EXIST")?.To;
                    var src = ParseIndirection(test.From);
                    if (src is null) continue;
                    if (test.LinkType.HasAncestor("same") || test.LinkType.Label.ToLower().Contains("same")) //hack if ancestor not set properly
                    {
                        Thought target = ParseIndirection(test.To);
                        if (!not && src == target) weight += l.Weight;
                        if (not && src != target) weight += l.Weight;
                    }
                    else if (test.To.Label == "??")
                    {
                        if (!not && src.HasLink(testType) is not null) weight+=l.Weight;
                        if (not && src.HasLink(testType) is null) weight += l.Weight;
                    }
                    else
                    {
                        Thought target = ParseIndirection(test.To);
                        if (!not && src.HasLink(testType, target) is not null) weight += l.Weight;
                        if (not && src.HasLink(testType, target) is null) weight += l.Weight;
                    }
                    //Debug.WriteLine($"Compared: {test.ToString()}  Weight: {weight}  Src: {src} Type: {testType} NOT: {not}");
                }
            }
            Debug.WriteLine($"Case: {t.Label}  Weight: {weight}");
            if (weight > bestWeight)
            {
                bestResponse = t.LinksTo.FindFirst(x => x.LinkType.Label == "response")?.To;
                bestWeight = weight;
            }
        }

        Debug.WriteLine($"Context: {contextRoot} returned {bestResponse}");
        return bestResponse;
    }
    private Thought ParseIndirection(Thought to)
    {
        if (to is null) return null;
        string toLabel = to.Label;
        if (string.IsNullOrEmpty(toLabel)) return null;

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
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
    //private SeqElement currentStep = null;
    private DateTime lastCycle = DateTime.Now;

    /// <summary>
    /// Controls whether execution happens one step at a time (true) or at full speed (false)
    /// </summary>
    public bool IsSingleStepMode { get; set; } = false;
    private bool takeStep = false;
    public void Step() { takeStep = true; }

    /// <summary>
    /// The last step that was executed (for highlighting in UI)
    /// </summary>
    public SeqElement LastExecutedStep { get; private set; }

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
        if (DateTime.Now < lastCycle + TimeSpan.FromMilliseconds(250))
            return;
        lastCycle = DateTime.Now;

        //handle single-step process
        if (IsSingleStepMode && !takeStep) return;
        takeStep = false;

        HandleFiringNeurons();
        UpdateDialog();
    }

    private bool HandleFiringNeurons()
    {
        var activeSteps = Thought.GetRecentlyFiredThoughts(TimeSpan.MaxValue); //for debug, no timeout
        foreach (var activeStep in activeSteps)
        {
            Debug.WriteLine($"activeStep: {activeStep}");
            Thought.DeleteFromRecentlyFired(activeStep); //ensure we detect thought firing only once
            CycleCount++;
            //Cases: EntryPoint, Call, Context, Assignment
            FireNextStatement(activeStep);
            if (HandledEntryPoint(activeStep)) continue;  //program start
            if (HandledCall(activeStep)) continue;  //program start or call
            if (HandledAssignment(activeStep)) continue;
            if (HandledContext(activeStep)) continue;
            //if we get here...there was nothing to process
        }
        if (activeSteps.Count == 0)
        {
            LastAction = $"TASK COMPLETE ({CycleCount} cycles): {LastLinkWritten?.ToString()}";
            return false;
        }
        return true;
    }

    //WE COULD REPLACE THESE WITH PROPERTIES
    // Entry point: Thought with a "steps" link to a SeqElement
    private bool IsEntryPoint(Thought t) { return t.GetTargetOfFirstLinkOfType("steps") is not null; }
    // Call: SeqElement with a VLU that has a "steps" link
    private bool IsCall(Thought t) { return t is not null && t is not Link && t.GetTargetOfFirstLinkOfType("steps") is not null; }
    // Context: Thought with no "steps" link and not a call or assignment
    private bool IsContext(Thought t) { return t is not null && t is not Link && t is not SeqElement s && t.GetTargetOfFirstLinkOfType("steps") is null; }
    // Assignment: Link with a "write" ancestor
    private bool IsAssignment(Thought t) { return t is Link link && link.LinkType?.HasAncestor("write") == true; }

    private void FireNextStatement(Thought activeStep)
    {
        if (IsEntryPoint(activeStep)) { activeStep.GetTargetOfFirstLinkOfType("steps").Fire(); return; }
        if (activeStep is SeqElement s)
        {
            //do NOT step next if this is a CALL or a CONTEXT
            if (IsCall(s.VLU)) { SetReturn(s.VLU.GetTargetOfFirstLinkOfType("steps"), s.NXT); s.VLU.Fire(); return; }
            if (IsContext(s.VLU)) s.VLU.Fire();
            if (IsAssignment(s.VLU)) s.VLU.Fire();
            Thought retVal = s.NXT; //get the next statement in the sequence
            if (retVal is null) //if we're at the end of a sequence, check for a return value to jump to
                retVal = GetReturn(s.FRST);
            retVal?.Fire();
            Debug.WriteLine($"Next Statement: {retVal}");
        }
    }
    private bool HandledEntryPoint(Thought t)
    {
        SeqElement IsEntryPoint = (SeqElement)t.GetTargetOfFirstLinkOfType("steps");
        if (IsEntryPoint is not null)
        {
            //Debug.WriteLine($"Handle entry point: {IsEntryPoint}");
            LastAction = $"STEP {CycleCount} CALL: {t}";
            return true;
        }
        return false;
    }
    private bool HandledCall(Thought t)
    {
        if (t is SeqElement s)
        {
            Thought IsEntryPoint = s.VLU;
            if (IsCall(IsEntryPoint))
            {
                SetReturn(s.VLU, s.NXT);
                return true;
            }
        }
        return false;
    }
    private bool HandledAssignment(Thought t)
    {
        // Get the action from the current step
        if (t is Link action)
        {
            Debug.WriteLine($"Handle assignment: {action}");
            Thought newTarget = HandleIndirection(action.To);
            Thought newFrom = HandleIndirection(action.From);
            if (action.LinkType.HasAncestor("write") && newFrom is not null)
            {
                Thought newLinkType = action.LinkType.GetTargetOfFirstLinkOfType("is");
                if (newLinkType is null) return false;
                Link existingLink = theUKS.GetLink(newFrom, newLinkType, newTarget);
                if (existingLink is null)
                {
                    newFrom.RemoveLinks(newLinkType);
                    Link newLink = newFrom.AddLink(newLinkType, newTarget);
                    newLink.TimeToLive = linkTimeToLive;
                    LastLinkWritten = newLink; // <-- Save the last link written for UI
                    LastAction = $"STEP {CycleCount}: {newLink.ToString()}"; //also for UI
                }
                else //extend the TLL of an existing link.  This should become an inherent property of Thoughts
                {
                    existingLink.Fire();
                    if (existingLink.TimeToLive < TimeSpan.MaxValue / 2)
                        existingLink.TimeToLive *= 2;
                    Debug.WriteLine($"Existing link extended: {existingLink.ToString()}  TTL: {existingLink.TimeToLive}");
                    LastLinkWritten = existingLink; // <-- Save as the last link written
                    LastAction = $"STEP {CycleCount}: {existingLink.ToString()}";
                }
                return true;
            }
        }
        return false;
    }

    private bool HandledContext(Thought t)
    {
        //Debug.WriteLine($"Handle context: {t}");
        if (IsContext(t))
        {
            EvaluateContext(t);
            return true;
        }
        return false;
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
        linkTimeToLive = IsSingleStepMode ? TimeSpan.FromSeconds(20) : TimeSpan.FromSeconds(10);
        LastLinkWritten = null;
        CycleCount = 0;
        LastAction = "";
        Thought.ClearRecentlyFiredQueue();

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
            return false;
        }

        // Get the first step of the task's sequence
        SeqElement firstStep = taskThought.GetTargetOfFirstLinkOfType("steps") as SeqElement;
        firstStep?.Fire();
        // If no steps found, try appending "_main" to the task name
        if (firstStep is null)
        {
            string mainTaskName = taskName + "_main";
            Thought mainTaskThought = theUKS.Labeled(mainTaskName);
            if (mainTaskThought is not null)
            {
                SeqElement mainSteps = mainTaskThought.GetTargetOfFirstLinkOfType("steps") as SeqElement;
                if (mainSteps is not null)
                    mainSteps.Fire();
                else
                    mainTaskThought.Fire();
            }
        }

        // Execute immediately for tests, or let the polling loop handle it
        if (executeImmediately)
            return ExecuteAllSteps();

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

    private bool ExecuteAllSteps()
    {
        const int maxCycles = 50_000;
        while (HandleFiringNeurons())
        {
            if (CycleCount > maxCycles)
            {
                LastAction = $"TASK ABORTED (>{maxCycles} cycles): possible interpreter loop";
                return false;
            }
        }

        LastAction = $"TASK COMPLETE ({CycleCount} cycles): {LastLinkWritten?.ToString()}";
        LastExecutedStep = null;
        return true;
    }

    private void SetReturn(Thought current, Thought retVal)
    {
        current.RemoveLinks("retVal");  //retval is the next action to take when done
        if (retVal is null) return;
        Link l = current.AddLink("retVal", retVal);
        //l.TimeToLive = linkTimeToLive;
    }

    private SeqElement GetReturn(Thought current)
    {
        SeqElement retVal = (SeqElement)current.GetTargetOfFirstLinkOfType("retVal");
        current.RemoveLinks("retVal");
        return retVal;
    }

    private Thought EvaluateContext(Thought contextRoot)
    {
        // Ch.4 AND-gate: activate context root + has relationship before attribute matching.
        theUKS.CurrentTraversal.Activate(contextRoot);
        Thought? hasType = theUKS.Labeled("has");
        if (hasType is not null)
            theUKS.CurrentTraversal.ActivateRelationship(hasType);

        ContextCaseResult? selected = theUKS.SelectBestContextCase(contextRoot, HandleIndirection);
        Thought bestResponse = selected?.Response;

        Debug.WriteLine($"Context: {contextRoot} returned {bestResponse}");
        bestResponse?.Fire();
        LastAction = $"CONTEXT: {contextRoot.Label} JMP: {bestResponse?.Label}";
        return bestResponse;
    }
    private Thought HandleIndirection(Thought to)
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
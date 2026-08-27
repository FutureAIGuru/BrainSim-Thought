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
    private TimeSpan linkTimeToLive = TimeSpan.FromSeconds(30);
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
        //return false;
        var activeSteps = Thought.GetRecentlyFiredThoughts(TimeSpan.MaxValue); //for debug, no timeout
        Thought.ClearRecentlyFiredQueue();
        foreach (var activeStep in activeSteps)
        {
            if (activeStep is SeqElement step && !IsExecutableStep(step))
            {
                Debug.WriteLine($"Data sequence ignored: {step}");
                continue;
            }

            Debug.WriteLine($"activeStep: {activeStep}");

            if (activeStep is SeqElement executableStep)
                LastExecutedStep = executableStep;

            CycleCount++;
            FireNextStatement(activeStep);
            //Cases: EntryPoint, Call, Context, Assignment
            if (HandledEntryPoint(activeStep)) continue;  //program start
            if (HandledCall(activeStep)) continue;  //program start or call
            if (HandledAssignment(activeStep)) continue;
            if (HandledContext(activeStep)) continue;
            //if we get here...there was nothing to process
            Debug.WriteLine($"activeStep ignored: {activeStep}");
        }
        if (activeSteps.Count == 0)
        {
            LastAction = $"TASK COMPLETE ({CycleCount} cycles): {LastLinkWritten?.ToString()}";
            return false;
        }
        return true;
    }

    // Entry point: Thought with a "steps" link to a SeqElement
    private bool IsEntryPoint(Thought t)
    {
        bool retVal = t.GetTargetOfFirstLinkOfType("steps") is not null;
        return retVal;
    }
    // Call: SeqElement with a VLU that has a "steps" link
    private bool IsCall(Thought t)
    {
        bool retVal = t is not null && t is not Link &&
            t.GetTargetOfFirstLinkOfType("steps") is not null && t.HasAncestor("Task");
        return retVal;
    }
    // Context: Thought with no "steps" link and not a call or assignment
    private bool IsContext(Thought value)
    {
        return value is not null &&
            value.Children.Any(caseThought =>
                caseThought.GetTargetOfFirstLinkOfType("response") is not null);
    }
    
    // Assignment: Link with a "set" ancestor
    private bool IsAssignment(Thought t)
    {
        bool retVal = t is Link link && link.LinkType?.HasAncestor("set") == true;
        return retVal;
    }

    private bool IsExecutableStep(SeqElement step)
    {
        SeqElement first = step.FRST ?? step;
        bool retVal = first.LinksFrom.Any(link =>link.LinkType?.Label == "steps");
        return retVal;
    }

    private void FireNextStatement(Thought activeStep)
    {
        if (IsEntryPoint(activeStep)) { activeStep.GetTargetOfFirstLinkOfType("steps").Fire(); return; }
        if (activeStep is SeqElement step && !IsExecutableStep(step)) return;
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
            if (action.LinkType.HasAncestor("set") && newFrom is not null)
            {
                Thought newLinkType = action.LinkType.GetTargetOfFirstLinkOfType("is");
                if (newLinkType is null) return false;
                Link existingLink = theUKS.GetLink(newFrom, newLinkType, newTarget);
                Link fullStatement = new(newFrom, action.LinkType, newTarget);
                Link newLink = theUKS.ApplyTestOrSetAction(fullStatement).FirstOrDefault();
                if (newLink is null) return false;
                if (existingLink is null)
                {
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
        theUKS.GetOrAddThought("TEST", "LinkType");
        theUKS.GetOrAddThought("SET", "LinkType");
        theUKS.GetOrAddThought("EQ", "Comparison");
        theUKS.GetOrAddThought("GT", "Comparison");
        Thought refType = theUKS.GetOrAddThought("ref", "LinkType");
        refType.AddProperty(theUKS.GetOrAddThought("isExclusive"));

        theUKS.CreateThoughtFromMultipleAttributes("set EQ", true);
        theUKS.CreateThoughtFromMultipleAttributes("set GT", true);
        theUKS.CreateThoughtFromMultipleAttributes("set ref", true);
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
        bool retVal = false;
        // Set link time-to-live based on execution mode
        // Single-step mode gets longer TTL since user is manually stepping through
        linkTimeToLive = IsSingleStepMode ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(30);
        LastLinkWritten = null;
        CycleCount = 0;
        LastAction = "";
        Thought.ClearRecentlyFiredQueue();

        if (theUKS == null) return retVal;

        // Get or create param1 and param2 thoughts
        Thought param1Thought = theUKS.GetOrAddThought("param1", "Variable");
        Thought param2Thought = theUKS.GetOrAddThought("param2", "Variable");
        Thought setRefType = theUKS.GetOrAddThought("SET.ref", "LinkType");

        // Process param1
        if (!string.IsNullOrEmpty(param1Value))
        {
            Thought param1ValueThought = GetOrCreateParameterValue(param1Value);
            // Link: param1 -> ref -> param1ValueThought
            Link param1Action = new(param1Thought, setRefType, param1ValueThought);
            theUKS.ApplyTestOrSetAction(param1Action);
        }

        // Process param2
        if (!string.IsNullOrEmpty(param2Value))
        {
            Thought param2ValueThought = GetOrCreateParameterValue(param2Value);
            // Link: param2 -> ref -> param2ValueThought
            Link param2Action = new(param2Thought, setRefType, param2ValueThought);
            theUKS.ApplyTestOrSetAction(param2Action);
        }

        // Get the task thought
        Thought taskThought = theUKS.Labeled(taskName);
        if (taskThought is null)
        {
            return retVal;
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
        retVal = executeImmediately ? ExecuteAllSteps() : true;
        return retVal;
    }

    private Thought GetOrCreateParameterValue(string value)
    {
        if (value.Length == 1)
            return theUKS.GetOrAddCharacter(value[0]);

        Thought valueThought = theUKS.Labeled(value);
        if (valueThought is not null) return valueThought;

        valueThought = theUKS.GetOrAddThought(value, "Thing");
        theUKS.CreateSpellingSequence(value, valueThought);
        return valueThought;
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
        Thought bestResponse = SelectBestContextResponse(contextRoot);

        Debug.WriteLine($"Context: {contextRoot} returned {bestResponse}");
        bestResponse?.Fire();
        LastAction = $"CONTEXT: {contextRoot.Label} JMP: {bestResponse?.Label}";
        return bestResponse;
    }

    private Thought SelectBestContextResponse(Thought contextRoot)
    {
        Thought bestResponse = null;
        float bestWeight = 0;

        foreach (Thought caseThought in contextRoot.Children)
        {
            float weight = ScoreContextCase(caseThought);
            if (weight <= bestWeight) continue;

            bestResponse = caseThought.GetTargetOfFirstLinkOfType("response");
            bestWeight = weight;
        }

        return bestResponse;
    }

    private float ScoreContextCase(Thought caseThought)
    {
        float weight = 0;

        foreach (Link l in caseThought.LinksTo.Where(x => x.LinkType?.Label == "has"))
        {
            if (l.To is not Link test) continue;
            if (test.LinkType?.HasAncestor("test") != true) continue;

            bool not = test.LinkType.HasAncestor("not");
            Thought testType = test.LinkType.LinksTo.FindFirst(x =>
                string.Equals(x.LinkType?.Label, "is", StringComparison.OrdinalIgnoreCase) &&
                x.To?.Label != "TEST")?.To;
            Thought src = HandleIndirection(test.From);
            if (src is null) continue;

            if (test.LinkType.HasAncestor("same") ||
                test.LinkType.Label.Contains("same", StringComparison.OrdinalIgnoreCase))
            {
                Thought target = HandleIndirection(test.To);
                if (!not && src == target) weight += l.Weight;
                if (not && src != target) weight += l.Weight;
            }
            else if (test.To?.Label == "??")
            {
                if (!not && src.HasLink(testType) is not null) weight += l.Weight * test.Weight;
                if (not && src.HasLink(testType) is null) weight += l.Weight * test.Weight;
            }
            else
            {
                Thought target = HandleIndirection(test.To);
                if (!not && src.HasLink(testType, target) is not null) weight += l.Weight * test.Weight;
                if (not && src.HasLink(testType, target) is null) weight += l.Weight * test.Weight;
            }
        }

        return weight;
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

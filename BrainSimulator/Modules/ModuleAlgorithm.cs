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
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

public partial class ModuleAlgorithm : ModuleBase
{
    private TimeSpan linkTimeToLive = TimeSpan.FromSeconds(5);
    // Working Relationships must survive engine delays and pauses in single-step mode.
    // Their normal lifetimes resume once execution ends.
    private readonly Dictionary<Link, TimeSpan> taskLinkLifetimes = new(ReferenceEqualityComparer.Instance);
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
            Thought.DeleteFromRecentlyFired(activeStep); //ensure we detect thought firing only once
            // Data sequences (including Paths) are not Instruction Blocks.
            if (activeStep is SeqElement step)
            {
                SeqElement first = step.FRST ?? step;
                Thought stepsType = theUKS.Labeled("steps");
                if (!first.LinksFrom.Any(link => ReferenceEquals(link.LinkType, stepsType)))
                    continue;
                LastExecutedStep = step;
            }
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
            foreach (var entry in taskLinkLifetimes)
            {
                entry.Key.LastFiredTime = DateTime.Now;
                entry.Key.TimeToLive = entry.Value;
            }
            taskLinkLifetimes.Clear();
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
    // A Context Block has candidate children with response Relationships.
    private bool IsContext(Thought t) { return t is not null && t is not Link && t is not SeqElement && t.Children.Any(candidate => candidate.GetTargetOfFirstLinkOfType("response") is not null); }
    // Assignment capability is inherited, independently of the action type's label.
    private bool IsAssignment(Thought t) { return t is Link link && link.LinkType?.HasProperty("setRelationship") == true; }

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
        }
    }
    private bool HandledEntryPoint(Thought t)
    {
        SeqElement IsEntryPoint = (SeqElement)t.GetTargetOfFirstLinkOfType("steps");
        if (IsEntryPoint is not null)
        {
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
        if (t is Link action && IsAssignment(action))
        {
            Thought newTarget = HandleIndirection(action.To);
            Thought newFrom = HandleIndirection(action.From);
            if (newFrom is not null && newTarget is not null)
            {
                // New compounds inherit their ordinary type. Legacy WRITE compounds
                // also retain their original attribute links until migration is complete.
                Thought newLinkType = null;
                foreach (Thought actionType in action.LinkType.AncestorsWithSelf)
                {
                    if (!actionType.HasProperty("setRelationship")) continue;
                    newLinkType = actionType.LinksTo.LastOrDefault(link =>
                        ReferenceEquals(link.LinkType, theUKS.Labeled("is")) && link.To is not null &&
                        !link.To.HasProperty("setRelationship") && !link.To.HasProperty("isPossible"))?.To;
                    if (newLinkType is not null) break;
                    List<Thought> ordinaryParents = actionType.Parents.Where(parent =>
                        !parent.HasProperty("setRelationship") && !parent.HasProperty("isPossible")).ToList();
                    newLinkType = ordinaryParents.FirstOrDefault(parent =>
                        ordinaryParents.All(other => parent.HasAncestor(other))) ?? ordinaryParents.FirstOrDefault();
                    if (newLinkType is not null) break;
                }
                if (newLinkType is null) return false;
                Link existingLink = theUKS.GetLink(newFrom, newLinkType, newTarget);
                // All operands have resolved before any existing Relationship is removed.
                if (newLinkType.HasProperty("isExclusive"))
                    foreach (Link oldLink in newFrom.LinksTo.Where(link =>
                        ReferenceEquals(link.LinkType, newLinkType) &&
                        !ReferenceEquals(link, existingLink)).ToList())
                    {
                        newFrom.RemoveLink(oldLink);
                        taskLinkLifetimes.Remove(oldLink);
                    }
                if (existingLink is null)
                {
                    Link newLink = theUKS.AddStatement(newFrom, newLinkType, newTarget);
                    if (newLink is null) return false;
                    taskLinkLifetimes[newLink] = linkTimeToLive;
                    newLink.TimeToLive = TimeSpan.MaxValue;
                    LastLinkWritten = newLink; // <-- Save the last link written for UI
                    LastAction = $"STEP {CycleCount}: {newLink.ToString()}"; //also for UI
                }
                else //extend the TLL of an existing link.  This should become an inherent property of Thoughts
                {
                    if (taskLinkLifetimes.TryGetValue(existingLink, out TimeSpan savedLifetime))
                    {
                        existingLink.LastFiredTime = DateTime.Now;
                        existingLink.TimeToLive = savedLifetime;
                    }
                    existingLink.Fire();
                    if (existingLink.TimeToLive < TimeSpan.MaxValue / 2)
                        existingLink.TimeToLive *= 2;
                    if (existingLink.TimeToLive != TimeSpan.MaxValue)
                    {
                        taskLinkLifetimes[existingLink] = existingLink.TimeToLive;
                        existingLink.TimeToLive = TimeSpan.MaxValue;
                    }
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
        // Also install capabilities when an older saved knowledge structure is loaded.
        foreach (string property in new[] { "followPath", "isPossible", "setRelationship", "isEqual", "isWildcard" })
            theUKS.GetOrAddThought(property, "Property");
        theUKS.GetOrAddThought("@", "Thought").AddLink("hasProperty", theUKS.Labeled("followPath"));
        theUKS.GetOrAddThought("?", "Thought").AddLink("hasProperty", theUKS.Labeled("isPossible"));
        theUKS.GetOrAddThought("!", "Thought").AddLink("hasProperty", theUKS.Labeled("setRelationship"));
        theUKS.GetOrAddThought("same", "Comparison").AddLink("hasProperty", theUKS.Labeled("isEqual"));
        theUKS.GetOrAddThought("Wildcard", "Thought");
        Thought unrestrictedWildcard = theUKS.GetOrAddThought("??", "Wildcard");
        unrestrictedWildcard.AddLink("hasProperty", theUKS.Labeled("isWildcard"));
        // Old examples classified the unrestricted wildcard as a Boolean. The old
        // matcher ignored that category; remove obsolete constraints during migration.
        foreach (Link membership in unrestrictedWildcard.LinksTo.Where(link =>
            ReferenceEquals(link.LinkType, theUKS.Labeled("is-a")) &&
            !ReferenceEquals(link.To, theUKS.Labeled("Wildcard")) &&
            !ReferenceEquals(link.To, theUKS.Labeled("Thought"))).ToList())
            unrestrictedWildcard.RemoveLink(membership);
        //initialization stuff  MOVE
        theUKS.GetOrAddThought("Task", "Thought");
        theUKS.GetOrAddThought("EXIST", "LinkType");
        theUKS.GetOrAddThought("WRITE", "LinkType");
        theUKS.GetOrAddThought("EQ", "Comparison");
        theUKS.GetOrAddThought("GT", "Comparison");
        theUKS.GetOrAddThought("REF", "LinkType").AddLink("hasProperty", theUKS.GetOrAddThought("isExclusive", "Property"));
        theUKS.Labeled("EXIST").AddLink("hasProperty", theUKS.Labeled("isPossible"));
        theUKS.Labeled("WRITE").AddLink("hasProperty", theUKS.Labeled("setRelationship"));

        // Saved WRITE actions used replacement unconditionally. Preserve that behavior
        // explicitly during migration; newly created ordinary types have no such default.
        foreach (Thought legacyWrite in theUKS.Labeled("WRITE").Descendants.ToList())
        {
            Thought ordinaryType = legacyWrite.GetTargetOfFirstLinkOfType("is");
            if (ordinaryType is null || legacyWrite.Parents.Any(parent => ReferenceEquals(parent, ordinaryType)))
                continue;
            ordinaryType.AddLink("hasProperty", theUKS.GetOrAddThought("isExclusive", "Property"));
            legacyWrite.AddParent(ordinaryType);
        }

        theUKS.CreateThoughtFromMultipleAttributes("write EQ", true);
        theUKS.CreateThoughtFromMultipleAttributes("write GT", true);
        theUKS.CreateThoughtFromMultipleAttributes("write REF", true);
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
        // Starting another task also releases the previous task's protected lifetimes.
        Thought.ClearRecentlyFiredQueue();
        HandleFiringNeurons();
        // Set link time-to-live based on execution mode
        // Single-step mode gets longer TTL since user is manually stepping through
        linkTimeToLive = IsSingleStepMode ? TimeSpan.FromSeconds(20) : TimeSpan.FromSeconds(10);
        LastLinkWritten = null;
        CycleCount = 0;
        LastAction = "";

        if (theUKS == null) return false;
        UKSInitializedNotification();
        Thought.ClearRecentlyFiredQueue();

        // Get or create param1 and param2 thoughts
        Thought param1Thought = theUKS.GetOrAddThought("param1", "Variable");
        Thought param2Thought = theUKS.GetOrAddThought("param2", "Variable");
        Thought refType = theUKS.GetOrAddThought("REF", "LinkType");

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
            // Link: param1 -> REF -> param1ValueThought
            param1Thought.RemoveLinks(refType);
            param1Thought.AddLink(refType, param1ValueThought);
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
            // Link: param2 -> REF -> param2ValueThought
            param2Thought.RemoveLinks(refType);
            param2Thought.AddLink(refType, param2ValueThought);
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
                Thought.ClearRecentlyFiredQueue();
                HandleFiringNeurons();
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

        ContextCaseResult? selected = SelectBestContextCase(contextRoot, HandleIndirection);
        Thought bestResponse = selected?.Response;

        bestResponse?.Fire();
        LastAction = $"CONTEXT: {contextRoot.Label} JMP: {bestResponse?.Label}";
        return bestResponse;
    }
    private Thought HandleIndirection(Thought to)
    {
        if (to is null) return null;
        if (to.HasProperty("followPath"))
        {
            // Path traversal uses sequence values and exact Relationship-type identities.
            // Do not flatten: a sequence used as an element is itself an ordinary Thought.
            if (to is not SeqElement path || path.VLU is null) return null;
            Thought current = path.VLU;
            HashSet<Thought> visited = new(ReferenceEqualityComparer.Instance) { path };
            for (SeqElement element = path.NXT; element is not null; element = element.NXT)
            {
                if (!visited.Add(element) || element.VLU is null) return null;
                current = current.LinksTo.FirstOrDefault(link =>ReferenceEquals(link.LinkType, element.VLU))?.To;
                if (current is null) return null;
            }
            return current;
        }
        // Temporary compatibility for saved dotted paths. Ordinary compound
        // Thoughts remain literal; new Paths above never depend on label content.
        if (to is SeqElement) return to;
        string toLabel = to.Label;
        if (string.IsNullOrEmpty(toLabel) || !toLabel.Contains('.')) return to;

        string[] parts = toLabel.Split(".");
        Thought newTarget = theUKS.Labeled(parts[0]);
        if (newTarget is null || parts.Skip(1).Any(part =>
            to.Parents.Any(parent => ReferenceEquals(parent, theUKS.Labeled(part)))))
            return to;
        for (int i = 1; i < parts.Length; i++)
        {
            Thought linkType = theUKS.Labeled(parts[i]);
            newTarget = newTarget?.GetTargetOfFirstLinkOfType(linkType);
        }

        return newTarget;
    }
}

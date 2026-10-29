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

using UKS;

namespace BrainSimulator.Modules;

public class ModuleAttributeBubble : ModuleBase, IManualAgent
{
    public string AgentName => "Attribute Bubble";
    public string DebugLog => debugString;

    public void RunOnce(UKS.UKS uks)
    {
        theUKS = uks;
        DoTheWork();
    }
    public string debugString = "Initialized\n";
    public override void Fire()
    {
        Init();
        UpdateDialog();
    }

    public void DoTheWork()
    {
        debugString = "Bubbler Started\n";
        foreach (Thought thought in theUKS.AtomicThoughts)
        {
            if ((thought.HasAncestor("Object") || thought.HasAncestor("Unknown")) &&
                theUKS.BubbleSharedAttributes(thought))
                debugString += $"Bubbled attributes on {thought.Label}\n";
        }
        debugString += "Bubbler Finished\n";
        UpdateDialog();
    }

    public override void Initialize()
    {
    }

    public override void UKSInitializedNotification()
    {
    }
}

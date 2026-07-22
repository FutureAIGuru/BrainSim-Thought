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
using System.Threading;

namespace BrainSimulator.Modules;

public class ModuleAttributeBubble : ModuleBase
{
    public string debugString = "Initialized\n";
    public new bool isEnabled { get; set; }
    private Timer? timer;

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
        Setup();
    }

    private void Setup()
    {
        timer ??= new Timer(_ =>
        {
            if (isEnabled)
                DoTheWork();
        }, null, 0, 10000);
    }

    public override void SetUpAfterLoad()
    {
        Setup();
    }

    public override void UKSInitializedNotification()
    {
    }
}

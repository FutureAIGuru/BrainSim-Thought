/*
 * Brain Simulator Through
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Through and is licensed under
 * the MIT License. You may use, copy, modify, merge, publish, distribute,
 * sublicense, and/or sell copies of this software under the terms of
 * the MIT License.
 *
 * See the LICENSE file in the project root for full license information.
 */
 

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using UKS;
using static BrainSimulator.Modules.ModuleAttributeBubble;

namespace BrainSimulator.Modules;

public class ModuleBalanceTree : ModuleBase, IManualAgent
{
    public string AgentName => "Balance Tree";
    public string DebugLog => debugString;

    public void RunOnce(UKS.UKS uks)
    {
        theUKS = uks;
        DoTheWork();
    }
    // Fill this method in with code which will execute
    // once for each cycle of the engine
    public override void Fire()
    {
        Init();
        UpdateDialog();
    }
    public string debugString = "Initialized\n";
    private int maxChildren = 6;
    public int MaxChildren { get => maxChildren; set => maxChildren = value; }

    public void DoTheWork()
    {
        debugString = "Agent Started\n";
        foreach (Thought t in theUKS.AtomicThoughts.ToList())
        {
            if (t.HasAncestor("Object") && !t.Label.Contains("."))
            {
                HandleExcessiveChildren(t);
            }
        }
        debugString += "Agent  Finished\n";
        UpdateDialog();
    }
    void HandleExcessiveChildren(Thought t)
    {
        while (t.Children.Count > MaxChildren)
        {
            Thought newParent = theUKS.AddThought(t.Label, t);
            debugString += $"Created new class:  {newParent.Label} \n";
            while (newParent.Children.Count < MaxChildren)
            {
                Thought theChild = t.Children[0];
                theChild.RemoveParent(t);
                theChild.AddParent(newParent);
            }
        }
    }


    // Fill this method in with code which will execute once
    // when the module is added, when "initialize" is selected from the context menu,
    // or when the engine restart button is pressed
    public override void Initialize()
    {
    }

    // The following can be used to massage public data to be different in the xml file
    // delete if not needed
    public override void SetUpBeforeSave()
    {
    }
    public override void SetUpAfterLoad()
    {
    }

    // called whenever the UKS performs an Initialize()
    public override void UKSInitializedNotification()
    {

    }
}

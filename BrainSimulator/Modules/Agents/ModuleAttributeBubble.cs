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
using System.Text.RegularExpressions;
using System.Threading;
using UKS;

namespace BrainSimulator.Modules;

public class ModuleAttributeBubble : ModuleBase
{
    // Fill this method in with code which will execute
    // once for each cycle of the engine
    public override void Fire()
    {
        //This agent works on a timer and "Fire" is not used

        Init();

        UpdateDialog();
    }

    public new bool isEnabled { get; set; }

    private Timer timer;
    private Timer? debounceTimer;
    private readonly HashSet<Thought> pendingBubbleParents = new();
    private readonly object debounceLock = new();
    public string debugString = "Initialized\n";
    private void Setup()
    {
        if (timer is null)
        {
            timer = new Timer(SameThreadCallback, null, 0, 10000);
            debounceTimer = new Timer(FlushDebouncedBubbles, null, Timeout.Infinite, Timeout.Infinite);
            theUKS.LinkAdded += OnLinkAdded;
        }
    }

    private void OnLinkAdded(Link lnk)
    {
        if (!isEnabled || lnk.From is null) return;
        lock (debounceLock)
        {
            foreach (Thought parent in lnk.From.Parents)
                pendingBubbleParents.Add(parent);
        }
        debounceTimer?.Change(250, Timeout.Infinite);
    }

    private void FlushDebouncedBubbles(object? _)
    {
        HashSet<Thought> batch;
        lock (debounceLock)
        {
            batch = new HashSet<Thought>(pendingBubbleParents);
            pendingBubbleParents.Clear();
        }
        foreach (Thought parent in batch)
        {
            if (parent.HasAncestor("Object") || parent.HasAncestor("Unknown"))
                BubbleChildAttributes(parent);
        }
    }
    private void SameThreadCallback(object state)
    {
        if (!isEnabled) return;
        new Thread(() =>
        {
            DoTheWork();
        }).Start();
    }

    public class LinkDest
    {
        public Thought linkType;
        public Thought target;
        public List<Link> links = new();
        public LinkDest()
        { }
        public LinkDest(Link r)
        {
            linkType = r.LinkType;
            target = r.To;
            links.Add(r);
        }
        public override string ToString()
        {
            return $"{linkType.Label} -> {target.Label}  :  {links.Count}";
        }
    }

    public void DoTheWork()
    {
        debugString = "Bubbler Started\n";
        foreach (Thought t in theUKS.AtomicThoughts)
        {
            if (t.Label == "Animal")
            { }
            if (t.HasAncestor("Object") || t.HasAncestor("Unknown"))
                BubbleChildAttributes(t);
        }
        debugString += "Bubbler Finished\n";
        UpdateDialog();
    }
    void BubbleChildAttributes(Thought t)
    {
        if (theUKS.BubbleSharedAttributes(t))
            debugString += $"Bubbled attributes on {t.Label}\n";
    }


    //If some links are exceptions, we can still bubble the 
    //Links are exceptions if they conflict AND numbers are one are small relative to the other.
    //a conflicting Thought is:
    //  linktypes are the same AND targets are different but have a common parent w/ isexclusive (colors)
    //  targets are the same AND linkTypes are different and have attributes with acommon parent which has the IsExslucive property (counts) (have 3, have 4)
    // Modified from UKS.CS line 181.  This does not includ AllowMultiples as these should not be bubbled
    private bool LinksConflict(LinkDest r1, LinkDest r2)
    {
        if (r1.linkType == r2.linkType && r1.target == r2.target) return false;
        if (r1.linkType == r2.linkType)
        {
            var parents = FindCommonParents(r1.target, r2.target);
            foreach (var parent in parents)
                if (parent.HasProperty("isExclusive") || parent.HasProperty("allowMultiple")) return true;
        }
        if (r1.target == r2.target)
        {
            var parents = FindCommonParents(r1.target, r2.target);
            foreach (var parent in parents)
                if (parent.HasProperty("isExclusive")) return true;

            //get the attributes of the links
            IReadOnlyList<Thought> r1RelAttribs = r1.linkType.GetAttributes();
            IReadOnlyList<Thought> r2RelAttribs = r2.linkType.GetAttributes();

            Thought r1Not = r1RelAttribs.FindFirst(x => x.Label == "not" || x.Label == "no");
            Thought r2Not = r2RelAttribs.FindFirst(x => x.Label == "not" || x.Label == "no");
            if (r1Not is null && r2Not is not null || r1Not is not null && r2Not is null)
                return true;

            //are any of the attrbutes which are exclusive?
            foreach (Thought t1 in r1RelAttribs)
                foreach (Thought t2 in r2RelAttribs)
                {
                    if (t1 == t2) continue;
                    List<Thought> commonParents = FindCommonParents(t1, t2);
                    foreach (Thought t3 in commonParents)
                    {
                        if (t3.HasProperty("isexclusive") || t3.HasProperty("allowMultiple"))
                            return true;
                    }
                }
            // handle special case where one linktype has is numberic and the other is not
            bool hasNumber1 = (r1RelAttribs.FindFirst(x => x.HasAncestor("number")) is not null);
            bool hasNumber2 = (r2RelAttribs.FindFirst(x => x.HasAncestor("number")) is not null);
            if (hasNumber1 || hasNumber2) return true;

        }
        return false;
    }


    private static List<Thought> FindCommonParents(Thought t, Thought t1)
    {
        //BORROWED from UKSStatement.cs line 323
        List<Thought> commonParents = new List<Thought>();
        foreach (Thought p in t.Parents)
            if (t1.Parents.Contains(p))
                commonParents.Add(p);
        return commonParents;
    }


    bool BubbleNeeded()
    {
        return true;
    }



    //if the given thought is an instance of its parent, get the parent
    public static Thought GetInstanceType(Thought t) => UKS.UKS.GetBubbleInstanceType(t);

    // Fill this method in with code which will execute once
    // when the module is added, when "initialize" is selected from the context menu,
    // or when the engine restart button is pressed
    public override void Initialize()
    {
        Setup();
    }

    // The following can be used to massage public data to be different in the xml file
    // delete if not needed
    public override void SetUpBeforeSave()
    {
    }
    public override void SetUpAfterLoad()
    {
        Setup();
    }

    // called whenever the UKS performs an Initialize()
    public override void UKSInitializedNotification()
    {

    }
}
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
 

using System.Collections.Generic;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

public class ModuleClassCreate : ModuleBase, IManualAgent
{
    public string AgentName => "Class Create";
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
    private int maxChildren = 12;
    private int minClassMembers = 3;
    public int MaxChildren { get => maxChildren; set => maxChildren = value; }
    public int MinClassMembers { get => minClassMembers; set => minClassMembers = value; }

    public void DoTheWork()
    {
        debugString = "Agent Started\n";
        foreach (Thought t in new List<Thought>(theUKS.AtomicThoughts))
        {
            if (t.HasAncestor("Object") &&
                !HasDirectProperty(t, "isAnonymousClass") &&
                !t.Label.Contains("unknown", System.StringComparison.OrdinalIgnoreCase))
            {
                HandleClassWithCommonAttributes(t);
            }
        }
        debugString += "Agent  Finished\n";
        UpdateDialog();
    }

    void HandleClassWithCommonAttributes(Thought t)
    {
        // Work one taxonomy level at a time. Every parent is visited by the
        // agent, so scanning descendants here would repeatedly rediscover the
        // same population and create nested duplicate classes.
        List<Thought> candidates = t.Children
            .Where(child => child is not Link && !HasDirectProperty(child, "isAnonymousClass"))
            .ToList();
        if (candidates.Count < minClassMembers) return;

        // Record the member set supporting each semantic attribute. Attributes
        // with exactly the same supporting population describe one discovered
        // class, rather than one class being created per attribute.
        Dictionary<(Thought linkType, Thought target), HashSet<Thought>> attributes = new();
        foreach (Thought child in candidates)
        {
            foreach (Link link in child.LinksTo)
            {
                if (link.LinkType is null || link.To is null || link.LinkType == Thought.IsA)
                    continue;
                if (link.LinkType.Label.Equals("hasProperty", System.StringComparison.OrdinalIgnoreCase) ||
                    link.LinkType.Label.Equals("hasImage", System.StringComparison.OrdinalIgnoreCase) ||
                    link.LinkType.HasProperty("isGrounding"))
                    continue;

                var key = (link.LinkType, link.To);
                if (!attributes.TryGetValue(key, out HashSet<Thought> members))
                {
                    members = new HashSet<Thought>();
                    attributes.Add(key, members);
                }
                members.Add(child);
            }
        }

        List<(HashSet<Thought> members, List<(Thought linkType, Thought target)> attributes)> groups = new();
        foreach (var item in attributes.Where(item =>
            item.Value.Count >= minClassMembers && item.Value.Count <= maxChildren))
        {
            var group = groups.FirstOrDefault(existing => existing.members.SetEquals(item.Value));
            if (group.members is null)
            {
                group = (new HashSet<Thought>(item.Value), new List<(Thought, Thought)>());
                groups.Add(group);
            }
            group.attributes.Add(item.Key);
        }

        foreach (var group in groups.OrderByDescending(group => group.members.Count))
        {
            Thought existingClass = t.Children.FirstOrDefault(child =>
                child.Children.ToHashSet().SetEquals(group.members));
            Thought newParent = theUKS.GetOrCreateThoughtClass(t, group.members, "class*");
            if (existingClass is null)
                newParent.AddProperty(theUKS.GetOrAddThought("isAnonymousClass", "Property"));

            foreach (Thought child in group.members)
                child.RemoveParent(t);

            string sharedAttributes = string.Join(", ", group.attributes.Select(attribute =>
                $"{attribute.linkType.Label} {attribute.target.Label}"));
            debugString += $"Created {newParent.Label} for " +
                $"{string.Join(", ", group.members.Select(member => member.Label))}" +
                $" (shared: {sharedAttributes})\n";
        }
    }

    private static bool HasDirectProperty(Thought thought, string propertyLabel)
    {
        return thought.LinksTo.Any(link =>
            link.LinkType?.Label.Equals(
                "hasProperty",
                System.StringComparison.OrdinalIgnoreCase) == true &&
            link.To?.Label.Equals(
                propertyLabel,
                System.StringComparison.OrdinalIgnoreCase) == true);
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

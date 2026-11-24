/*
 * Brain Simulator Thought
 * Copyright (c) 2026 Charles Simon
 * Licensed under the MIT License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

/// <summary>
/// The control surface for the UKS agents (UKS.Agents.cs): runs a selected agent once.
/// </summary>
public class ModuleAgents : ModuleBase
{
    private static readonly (string name, Func<UKS.UKS, List<Thought>> run)[] agents =
    {
        ("Add Counts", uks => uks.AddCounts()),
        ("Attribute Bubble", uks => uks.BubbleAttributes()),
        ("Balance Tree", uks => uks.BalanceTree()),
        ("Class Create", uks => uks.CreateClasses()),
    };

    public string Status { get; private set; } = "Choose an agent to run once.";
    public IReadOnlyList<string> AgentNames => agents.Select(agent => agent.name).ToList();

    public override void Fire()
    {
        Init();
        UpdateDialog();
    }

    public override void Initialize()
    {
    }

    public override void UKSInitializedNotification()
    {
        GetUKS();
        Status = "Choose an agent to run once.";
    }

    public bool RunAgent(string agentName)
    {
        var agent = agents.FirstOrDefault(candidate => candidate.name.Equals(agentName, StringComparison.OrdinalIgnoreCase));
        if (agent.run is null || theUKS is null)
        {
            Status = $"Agent not found: {agentName}";
            UpdateDialog();
            return false;
        }

        List<Thought> changed = agent.run(theUKS);
        Status = $"{agent.name}: {changed.Count} changed\n" + string.Join("\n", changed.Select(thought => thought.ToString()));
        UpdateDialog();
        return true;
    }
}

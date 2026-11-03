/*
 * Brain Simulator Thought
 * Copyright (c) 2026 Charles Simon
 * Licensed under the MIT License.
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace BrainSimulator.Modules;

/// <summary>
/// A single, manual control surface for all IManualAgent implementations.
/// No operation runs on a timer from this module.
/// </summary>
public class ModuleAgents : ModuleBase
{
    private readonly List<IManualAgent> _agents = new();

    public string Status { get; private set; } = "Choose an agent to run once.";
    public IReadOnlyList<IManualAgent> Agents
    {
        get
        {
            DiscoverAgents();
            return _agents;
        }
    }

    public override void Fire()
    {
        Init();
        UpdateDialog();
    }

    public override void Initialize()
    {
        DiscoverAgents();
    }

    public override void UKSInitializedNotification()
    {
        GetUKS();
        Status = "Choose an agent to run once.";
    }

    public bool RunAgent(string agentName)
    {
        DiscoverAgents();
        IManualAgent agent = _agents.FirstOrDefault(candidate =>
            candidate.AgentName.Equals(agentName, StringComparison.OrdinalIgnoreCase) ||
            candidate.GetType().Name.Equals(agentName, StringComparison.OrdinalIgnoreCase));
        if (agent is null || theUKS is null)
        {
            Status = $"Agent not found: {agentName}";
            UpdateDialog();
            return false;
        }

        agent.RunOnce(theUKS);
        Status = agent.DebugLog;
        UpdateDialog();
        return true;
    }

    private void DiscoverAgents()
    {
        if (_agents.Count > 0) return;

        IEnumerable<Type> types = GetType().Assembly.GetTypes()
            .Where(type => !type.IsAbstract &&
                typeof(IManualAgent).IsAssignableFrom(type) &&
                type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase);

        foreach (Type type in types)
        {
            if (Activator.CreateInstance(type) is IManualAgent agent)
                _agents.Add(agent);
        }
    }
}

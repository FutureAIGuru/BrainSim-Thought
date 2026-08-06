/*
 * Brain Simulator Thought
 * Copyright (c) 2026 Charles Simon
 * Licensed under the MIT License.
 */

namespace BrainSimulator.Modules;

/// <summary>An agent operation which is run only when the user requests it.</summary>
public interface IManualAgent
{
    string AgentName { get; }
    string DebugLog { get; }
    void RunOnce(UKS.UKS uks);
}

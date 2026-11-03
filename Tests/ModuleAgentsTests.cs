using BrainSimulator;
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public class ModuleAgentsTests
{
    [Fact]
    public void UnifiedModule_DiscoversAgentsAndRunsSelectedAgentOnce()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks;
        Thought objectRoot = uks.Labeled("Object")!;
        Thought fur = uks.GetOrAddThought("fur", objectRoot);
        Thought has = uks.Labeled("has")!;

        foreach (string label in new[] { "Fido", "Rover", "Spot" })
            uks.GetOrAddThought(label, objectRoot).AddLink(has, fur);

        var agents = new ModuleAgents { theUKS = uks };

        Assert.Contains(agents.Agents, agent => agent.AgentName == "Class Create");
        Assert.Contains(agents.Agents, agent => agent.AgentName == "Attribute Bubble");
        Assert.Contains(Utils.GetListOfExistingCSharpModuleTypes(), type =>
            type == typeof(ModuleAgents));
        Assert.DoesNotContain(Utils.GetListOfExistingCSharpModuleTypes(), type =>
            typeof(IManualAgent).IsAssignableFrom(type));
        Assert.True(agents.RunAgent("Class Create"));
        Assert.Contains(objectRoot.Children, child => child.HasProperty("isAnonymousClass"));
    }
}

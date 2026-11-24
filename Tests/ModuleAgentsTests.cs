using BrainSimulator;
using BrainSimulator.Modules;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public class ModuleAgentsTests
{
    [Fact]
    public void UnifiedModule_ListsAgentsAndRunsSelectedAgentOnce()
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

        Assert.Contains("Class Create", agents.AgentNames);
        Assert.Contains("Attribute Bubble", agents.AgentNames);
        Assert.Contains(Utils.GetListOfExistingCSharpModuleTypes(), type =>
            type == typeof(ModuleAgents));
        Assert.True(agents.RunAgent("Class Create"));
        Assert.Contains(objectRoot.Children, child => child.HasProperty("isAnonymousClass"));
    }
}

/*
 * Regression tests for P1 non-UKS fixes (2026-07-07).
 */

using BrainSimulator;
using UKS;
using Xunit;

namespace UKS.Tests;

public class NonUksP1FixesRegressionTests
{
    [Fact]
    public void Broadcast_does_not_throw_when_ipv4_unavailable()
    {
        Exception ex = Record.Exception(() => Network.Broadcast("brainsim-test"));
        Assert.Null(ex);
    }

    [Fact]
    public void DeactivateModule_preserves_shared_link_targets()
    {
        var uks = new global::UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        var handler = new ModuleHandler();

        Thought shared = uks.GetOrAddThought("SharedConfig", "Object");
        Thought moduleType = uks.GetOrAddThought("ModuleTest", uks.Labeled("AvailableModule"));
        Thought instance = uks.CreateInstanceOf(moduleType);
        uks.AddStatement(instance, "is", shared);

        string instanceLabel = instance.Label;
        handler.DeactivateModule(instanceLabel);

        Assert.NotNull(uks.Labeled("SharedConfig"));
        Assert.NotNull(uks.Labeled(moduleType.Label));
        Assert.Null(uks.Labeled(instanceLabel));
    }
}
/*
 * Regression tests for Vision / BrainSim3 UKS compatibility layer.
 */

using UKS;
using Xunit;

namespace UKS.Tests;

public class VisionCompatRegressionTests
{
    [Fact]
    public void GetOrAddThing_alias_matches_GetOrAddThought()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        Thought t = uks.GetOrAddThing("VisionTest", "Object");
        Assert.Same(t, uks.Labeled("VisionTest"));
    }

    [Fact]
    public void SetAttribute_and_GetAttribute_round_trip()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        uks.AddStatement("Color", "is-a", "Attribute");
        Thought shape = uks.GetOrAddThought("shape1", "Object");
        Thought color = uks.GetOrAddThought("color1", "Color");
        shape.SetAttribute(color);
        Assert.Same(color, shape.GetAttribute("Color"));
    }

    [Fact]
    public void SearchForClosestMatch_ref_returns_ranked_results()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        Thought root = uks.GetOrAddThought("Stored", "Object");
        Thought a = uks.GetOrAddThought("a", root);
        Thought b = uks.GetOrAddThought("b", root);
        Thought target = uks.GetOrAddThought("target", "Object");
        uks.AddStatement(target, "is", a);
        uks.AddStatement(a, "is", b);

        float best = 0;
        Thought? first = uks.SearchForClosestMatch(target, root, ref best);
        Assert.NotNull(first);
        float best2 = 0;
        Thought? second = uks.GetNextClosestMatch(ref best2);
        Assert.True(first is not null);
    }
}
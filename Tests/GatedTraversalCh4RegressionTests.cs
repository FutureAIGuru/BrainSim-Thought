/*
 * Ch.4 relationship-gated traversal regression tests (Simon Atomic Thoughts).
 * Fido+has vs Fido-only; is-a category traversal.
 */

using System.Linq;
using UKS;
using Xunit;

namespace UKS.Tests;

public class GatedTraversalCh4RegressionTests
{
    private static UKS CreateUks()
    {
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        return uks;
    }

    [Fact]
    public void Gated_has_traversal_empty_when_only_Fido_active()
    {
        var uks = CreateUks();
        var ctx = new TraversalContext();
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("fur", "is-a", "Object");
        uks.AddStatement("dog", "has", "fur");
        uks.AddStatement("Fido", "is-a", "dog");

        Thought fido = uks.Labeled("Fido");
        Thought has = uks.Labeled("has");

        ctx.Activate(fido);

        Assert.Empty(uks.GetGatedLinks(fido, has, ctx));
        Assert.Empty(uks.Traverse(fido, has, ctx));
    }

    [Fact]
    public void Gated_has_traversal_returns_fur_when_Fido_and_has_active()
    {
        var uks = CreateUks();
        var ctx = new TraversalContext();
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("fur", "is-a", "Object");
        uks.AddStatement("dog", "has", "fur");
        uks.AddStatement("Fido", "is-a", "dog");

        Thought fido = uks.Labeled("Fido");
        Thought has = uks.Labeled("has");
        Thought fur = uks.Labeled("fur");

        ctx.Activate(fido);
        ctx.ActivateRelationship(has);

        var links = uks.GetGatedLinks(fido, has, ctx);
        Assert.Contains(links, l => l.To == fur);

        var targets = uks.Traverse(fido, has, ctx);
        Assert.Contains(fur, targets);
    }

    [Fact]
    public void Gated_is_a_traversal_returns_dog_when_Fido_and_is_a_active()
    {
        var uks = CreateUks();
        var ctx = new TraversalContext();
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("Fido", "is-a", "dog");

        Thought fido = uks.Labeled("Fido");
        Thought isA = uks.Labeled("is-a");
        Thought dog = uks.Labeled("dog");

        ctx.Activate(fido);
        Assert.Empty(uks.Traverse(fido, isA, ctx));

        ctx.ActivateRelationship(isA);
        var targets = uks.Traverse(fido, isA, ctx);
        Assert.Contains(dog, targets);
    }

    [Fact]
    public void BeginTraversalCycle_clears_active_context()
    {
        var uks = CreateUks();
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("Fido", "is-a", "dog");

        Thought fido = uks.Labeled("Fido");
        Thought isA = uks.Labeled("is-a");

        uks.CurrentTraversal.Activate(fido);
        uks.CurrentTraversal.ActivateRelationship(isA);
        Assert.NotEmpty(uks.Traverse(fido, isA));

        uks.BeginTraversalCycle();
        Assert.Empty(uks.Traverse(fido, isA));
    }
}
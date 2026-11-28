using System.Collections.Generic;
using System.Linq;
using UKS;
using Xunit;

namespace UKS.Tests;

public class UKSClassCoalescingTests
{
    [Fact]
    public void ReplaceThoughtReferencesPreservesCanonicalClassAndRedirectsEveryReference()
    {
        UKS uks = CreateUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "Thought");
        Thought classA = uks.GetOrAddThought("classA", classRoot);
        Thought classB = uks.GetOrAddThought("classB", classRoot);
        Thought pig = uks.GetOrAddThought("pig", "Word");
        pig.AddParent(classB);
        Thought wildcardB = uks.CreateWildcard("??classB", new List<Thought> { classB });
        Thought template = uks.GetOrAddThought("template", "Thought");
        uks.AddSequenceAndLink(template, "hasWords", new List<Thought>
        {
            wildcardB, uks.GetOrAddThought("runs", "Word")
        });
        Thought marker = uks.GetOrAddThought("marker", "Property");
        classB.AddLink("hasProperty", marker);
        Thought referrer = uks.GetOrAddThought("referrer", "Thought");
        Thought describes = uks.GetOrAddThought("describes", "LinkType");
        referrer.AddLink(describes, classB);

        int replaced = uks.ReplaceThoughtReferences(classB, classA);

        Assert.True(replaced >= 4);
        Assert.Null(uks.Labeled("classB"));
        Assert.Contains(classA, pig.Parents);
        Assert.Contains(classA, wildcardB.Parents);
        Assert.DoesNotContain(classB, wildcardB.Parents);
        Assert.NotNull(classA.HasLink(uks.Labeled("hasProperty"), marker));
        Assert.NotNull(referrer.HasLink(describes, classA));
        Assert.DoesNotContain(uks.AtomicThoughts.SelectMany(thought => thought.LinksTo), link =>
            link.From == classB || link.LinkType == classB || link.To == classB);
    }

    [Fact]
    public void ReplaceThoughtReferencesRedirectsLinksUsingTheThoughtAsTheirType()
    {
        UKS uks = CreateUKS();
        Thought owns = uks.GetOrAddThought("owns", "LinkType");
        Thought possesses = uks.GetOrAddThought("possesses", "LinkType");
        Thought fido = uks.GetOrAddThought("Fido", "Object");
        Thought bone = uks.GetOrAddThought("bone", "Object");
        fido.AddLink(possesses, bone);

        uks.ReplaceThoughtReferences(possesses, owns);

        Assert.Null(uks.Labeled("possesses"));
        Assert.NotNull(fido.HasLink(owns, bone));
        Assert.DoesNotContain(fido.LinksTo, link => ReferenceEquals(link.LinkType, possesses));
    }

    [Fact]
    public void CoalesceSimilarClassesMergesStrongReciprocalOverlapButLeavesDistinctClass()
    {
        UKS uks = CreateUKS();
        Thought root = uks.GetOrAddThought("LearnedClass", "Thought");
        Thought classA = uks.GetOrAddThought("classA", root);
        Thought classB = uks.GetOrAddThought("classB", root);
        Thought classC = uks.GetOrAddThought("classC", root);
        foreach (string label in new[] { "a", "b", "c", "d", "e" })
            uks.GetOrAddThought(label, "Word").AddParent(classA);
        foreach (string label in new[] { "b", "c", "d", "e", "f" })
            uks.GetOrAddThought(label, "Word").AddParent(classB);
        foreach (string label in new[] { "u", "v", "w", "x" })
            uks.GetOrAddThought(label, "Word").AddParent(classC);

        int merged = BrainSimulator.Modules.ModuleText.CoalesceSimilarClasses(uks, root);

        Assert.Equal(1, merged);
        Assert.Null(uks.Labeled("classB"));
        Assert.Contains(classA, root.Children);
        Assert.Contains(classC, root.Children);
        Assert.Equal(6, classA.Children.Count(child => !child.HasAncestor("Wildcard")));
    }

    private static UKS CreateUKS()
    {
        UKS uks = new();
        uks.CreateInitialStructure();
        return uks;
    }
}

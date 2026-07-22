/*
 * Ch.5 Phase E — bubble on learn, ExplainLink, Fido demo content.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UKS;
using Xunit;

namespace UKS.Tests;

public class InheritanceCh5PhaseERegressionTests
{
    private static UKS CreateUks()
    {
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        return uks;
    }

    private static string DemoPath()
    {
        string fromTest = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..",
            "BrainSimulator", "UKSContent", "Ch4Ch5-FidoDemo.txt");
        if (File.Exists(fromTest)) return Path.GetFullPath(fromTest);
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",// "..",
            "BrainSimulator", "UKSContent", "Ch4Ch5-FidoDemo.txt"));
    }

    [Fact]
    public void BubbleSharedAttributes_majority_bubbles_to_parent()
    {
        var uks = CreateUks();
        Thought animal = uks.GetOrAddThought("Animal", "Object");
        Thought dog = uks.GetOrAddThought("Dog", animal);
        Thought cat = uks.GetOrAddThought("Cat", animal);
        Thought bird = uks.GetOrAddThought("Bird", animal);
        Thought fur = uks.GetOrAddThought("fur", "Object");
        Thought has = uks.Labeled("has");

        dog.AddLink(has, fur).Weight = 1f;
        cat.AddLink(has, fur).Weight = 1f;
        bird.AddLink(has, fur).Weight = 1f;

        Assert.True(uks.BubbleSharedAttributes(animal));
        Assert.NotNull(animal.HasLink(has, fur));
        Assert.NotNull(dog.HasLink(has, fur));
    }

    [Fact]
    public void ExplainLink_traces_Fido_dog_fur()
    {
        var uks = CreateUks();
        uks.AddStatement("dog", "is-a", "Object");
        uks.AddStatement("fur", "is-a", "Object");
        uks.AddStatement("dog", "has", "fur");
        uks.AddStatement("Fido", "is-a", "dog");

        Thought fido = uks.Labeled("Fido");
        Link inherited = uks.GetAttributes(fido)
            .First(l => l.To == uks.Labeled("fur"));

        var trace = uks.ExplainLink(inherited, fido);
        Assert.Equal(new[] { "Fido", "dog", "fur" }, trace.Select(t => t.Label).ToArray());
    }

    [Fact]
    public void Ch4Ch5_FidoDemo_txt_loads_and_validates_patterns()
    {
        var uks = CreateUks();
        string path = DemoPath();
        //"C:\Users\C_SIM\source\repos\BrainSim Thought\BrainSimulator\UKSContent\ch4-ch5FidoDemo.txt"
        Assert.True(File.Exists(path), $"Demo not found at {path}");
        uks.ImportTextFile(path);

        Thought fido = uks.Labeled("Fido");
        Thought tripper = uks.Labeled("Tripper");
        Thought has = uks.Labeled("has");
        Assert.NotNull(fido);
        Assert.NotNull(tripper);

        var fidoLinks = uks.GetAttributes(fido);
        Assert.Contains(fidoLinks, l => l.LinkType == has && l.To == uks.Labeled("fur"));
        Assert.Contains(fidoLinks, l => l.To == uks.Labeled("owner"));

        var tripperLinks = uks.GetAttributes(tripper);
        Assert.Contains(tripperLinks, l => l.LinkType?.Label == "has.3");
        Assert.DoesNotContain(tripperLinks, l => l.LinkType?.Label == "has.4");

        var ctx = new TraversalContext();
        ctx.Activate(fido);
        Assert.Empty(uks.GetGatedLinks(fido, has, ctx));
        ctx.ActivateRelationship(has);
        Assert.Contains(uks.GetGatedLinks(fido, has, ctx), l => l.To == uks.Labeled("fur"));
    }
}

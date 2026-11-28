using System.Collections.Generic;
using System.Linq;
using UKS;
using UKS.Agents;
using Xunit;

namespace UKS.Tests;

public class UKSSequenceLinkTests
{
    private static UKS CreateUKS()
    {
        var uks = new UKS();
        uks.CreateInitialStructure();
        return uks;
    }

    private static List<Thought> Thoughts(UKS uks, params string[] labels) =>
        labels.Select(label => uks.GetOrAddThought(label, "Thought")).ToList();

    [Fact]
    public void SequenceLinksDoNotDependOnLinkTypeNames()
    {
        UKS uks = CreateUKS();
        Thought phrase = uks.GetOrAddThought("phrase1", "Phrase");
        Thought word = uks.GetOrAddThought("w:cats", "Word");
        Thought learnedClass = uks.GetOrAddThought("class0", "LearnedClass");

        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Thought spelled = uks.GetOrAddThought("spelled", "LinkType");
        Thought commonSequence = uks.GetOrAddThought("commonSequence", "LinkType");
        uks.AddSequenceAndLink(phrase, hasWords, Thoughts(uks, "cats", "sleep"));
        uks.AddSequenceAndLink(word, spelled, Thoughts(uks, "C", "A", "T", "S"));
        uks.AddSequenceAndLink(learnedClass, commonSequence, Thoughts(uks, "??noun", "sleep"));

        List<Link> links = uks.GetSequenceLinks(new[] { phrase, word, learnedClass }).ToList();

        Assert.Equal(3, links.Count);
        Assert.Contains(links, link => link.From == phrase && link.LinkType == hasWords);
        Assert.Contains(links, link => link.From == word && link.LinkType == spelled);
        Assert.Contains(links, link => link.From == learnedClass && link.LinkType == commonSequence);
    }

    [Fact]
    public void SequenceLinkLeadsToTheFlattenedElements()
    {
        UKS uks = CreateUKS();
        uks.AddSequence("first", Thoughts(uks, "A", "B", "C"));
        Thought owner = uks.GetOrAddThought("second", "Phrase");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        SeqElement root = uks.AddSequenceAndLink(owner, hasWords, Thoughts(uks, "A", "B", "C", "D"));

        Link link = Assert.Single(uks.GetSequenceLinks(owner));

        Assert.Same(root, link.To);
        Assert.Equal(new[] { "A", "B", "C", "D" }, uks.FlattenSequence(link.To).Select(element => element.Label));
    }

    [Fact]
    public void ThoughtClassContainsOnlyTheSuppliedOrdinaryThoughts()
    {
        UKS uks = CreateUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "Thought");
        Thought dog = uks.GetOrAddThought("dog", "Word");
        Thought cat = uks.GetOrAddThought("cat", "Word");
        SeqElement sequence = uks.AddSequence("dogcat", new List<Thought> { dog, cat });

        Thought learnedClass = uks.GetOrCreateThoughtClass(classRoot, new[] { dog, cat, sequence });

        Assert.Equal(new HashSet<Thought> { dog, cat }, learnedClass.Children.ToHashSet());
    }

    [Fact]
    public void RepeatingTheSameMemberSetReusesTheClass()
    {
        UKS uks = CreateUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "Thought");
        Thought first = uks.GetOrAddThought("first", "Thought");
        Thought second = uks.GetOrAddThought("second", "Thought");

        Thought initial = uks.GetOrCreateThoughtClass(classRoot, new[] { first, second });
        Thought repeated = uks.GetOrCreateThoughtClass(classRoot, new[] { second, first });

        Assert.Same(initial, repeated);
        Assert.Single(classRoot.Children);
    }
}

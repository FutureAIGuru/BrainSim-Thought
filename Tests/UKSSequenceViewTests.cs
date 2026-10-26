using System.Collections.Generic;
using System.Linq;
using UKS;
using Xunit;

namespace UKS.Tests;

public class UKSSequenceViewTests
{
    private static UKS CreateUKS()
    {
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        return uks;
    }

    private static List<Thought> Thoughts(UKS uks, params string[] labels) =>
        labels.Select(label => uks.GetOrAddThought(label, "Thought")).ToList();

    [Fact]
    public void SequenceViewsDoNotDependOnGrammarSpecificRelationshipNames()
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

        List<SequenceView> views = uks.GetSequenceViews(new[] { phrase, word, learnedClass });

        Assert.Equal(3, views.Count);
        Assert.Contains(views, view => view.Owner == phrase && view.LinkType?.Label == "hasWords");
        Assert.Contains(views, view => view.Owner == word && view.LinkType?.Label == "spelled");
        Assert.Contains(views, view => view.Owner == learnedClass && view.LinkType?.Label == "commonSequence");
    }

    [Fact]
    public void SequenceViewPreservesOwnerLinkRootAndFlattenedElements()
    {
        UKS uks = CreateUKS();
        Thought owner = uks.GetOrAddThought("observation", "Phrase");
        Thought relationship = uks.GetOrAddThought("observedAs", "LinkType");
        SeqElement root = uks.AddSequenceAndLink(
            owner,
            relationship,
            Thoughts(uks, "one", "two", "three"));

        SequenceView view = Assert.Single(uks.GetSequenceViews(owner));

        Assert.Same(owner, view.Owner);
        Assert.Same(relationship, view.LinkType);
        Assert.Same(root, view.Sequence);
        Assert.Same(view.OwnerLink, owner.LinksTo.Single(link => link.To == root));
        Assert.Equal(new[] { "one", "two", "three" }, view.Elements.Select(element => element.Label));
    }

    [Fact]
    public void SequenceViewFlattensCompressedNestedStorage()
    {
        UKS uks = CreateUKS();
        uks.AddSequence("first", Thoughts(uks, "A", "B", "C"));
        Thought owner = uks.GetOrAddThought("second", "Phrase");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        uks.AddSequenceAndLink(owner, hasWords, Thoughts(uks, "A", "B", "C", "D"));

        SequenceView view = Assert.Single(uks.GetSequenceViews(owner));

        Assert.Equal(new[] { "A", "B", "C", "D" }, view.Elements.Select(element => element.Label));
    }

    [Fact]
    public void ThoughtClassContainsOnlyTheSuppliedThoughts()
    {
        UKS uks = CreateUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "Thought");
        Thought dog = uks.GetOrAddThought("dog", "Word");
        Thought cat = uks.GetOrAddThought("cat", "Word");

        Thought learnedClass = uks.GetOrCreateThoughtClass(classRoot, new[] { dog, cat });

        Assert.Equal(new HashSet<Thought> { dog, cat }, learnedClass.Children.ToHashSet());
    }

    [Fact]
    public void SequenceClassContainsOwnersNotSequenceImplementationNodes()
    {
        UKS uks = CreateUKS();
        Thought classRoot = uks.GetOrAddThought("LearnedClass", "Thought");
        Thought firstPhrase = uks.GetOrAddThought("phrase1", "Phrase");
        Thought secondPhrase = uks.GetOrAddThought("phrase2", "Phrase");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        uks.AddSequenceAndLink(firstPhrase, hasWords, Thoughts(uks, "dogs", "bark"));
        uks.AddSequenceAndLink(secondPhrase, hasWords, Thoughts(uks, "cats", "sleep"));
        List<SequenceView> observations = uks.GetSequenceViews(new[] { firstPhrase, secondPhrase });

        Thought learnedClass = uks.GetOrCreateSequenceClass(classRoot, observations);

        Assert.Equal(new HashSet<Thought> { firstPhrase, secondPhrase }, learnedClass.Children.ToHashSet());
        Assert.DoesNotContain(learnedClass.Children, member => member is SeqElement);
        Assert.DoesNotContain(uks.AtomicThoughts.OfType<SeqElement>(), element => element.Parents.Contains(learnedClass));
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

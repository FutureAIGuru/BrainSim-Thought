using System.Linq;
using UKS;
using Xunit;

namespace BrainSimulator.Tests;

public class UKSDeletionTests
{
    private static UKS.UKS CreateUKS()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateMinimumStructureForTests();
        UKS.UKS.theUKS = uks;
        return uks;
    }

    [Fact]
    public void DeleteThought_RemovesThoughtAndLabel()
    {
        var uks = CreateUKS();
        Thought t = uks.GetOrAddThought("temp");

        Assert.Contains(t, uks.AtomicThoughts);
        Assert.Same(t, ThoughtLabels.GetThought("temp"));

        t.Delete();

        Assert.DoesNotContain(t, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought("temp"));
    }

    [Fact]
    public void DeleteThought_RemovesOutgoingLinksToo()
    {
        var uks = CreateUKS();
        Thought a = uks.GetOrAddThought("a");
        Thought b = uks.GetOrAddThought("b");
        Thought linkType = uks.GetOrAddThought("likes","LinkType");
        Link likes = a.AddLink("likes", b);

        Assert.DoesNotContain(likes, uks.AtomicThoughts);

        likes.Label = "TheLink";

        Assert.NotNull(ThoughtLabels.GetThought("TheLink")); // label removed
        Assert.DoesNotContain(likes, uks.AtomicThoughts); //linkds do not show in allThoughts
        Assert.Contains(a, uks.AtomicThoughts);
        Assert.Contains(b, uks.AtomicThoughts);

        a.Delete();
        //uks.DeleteThought(a);

        Assert.DoesNotContain(a, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought("a")); // label removed
        Assert.Null(ThoughtLabels.GetThought("TheLink")); // label removed
        Assert.DoesNotContain(likes, uks.AtomicThoughts);
        Assert.Same(b, ThoughtLabels.GetThought("b")); // b survives
    }

    [Fact]
    public void DeleteOwner_RemovesOwnedSequenceButLeavesLetters()
    {
        var uks = CreateUKS();
        Thought word = uks.GetOrAddThought("word");
        Thought spelled = uks.GetOrAddThought("spelled", "LinkType");
        Thought lA = uks.GetOrAddThought("A", "symbol");
        Thought lB = uks.GetOrAddThought("B", "symbol");

        var seqStart = uks.AddSequence(word, spelled, new() { lA, lB });

        Assert.DoesNotContain(seqStart, uks.AtomicThoughts); //sequences are not in allThoughts
        Assert.Same(seqStart, ThoughtLabels.GetThought($"{word.Label.ToLower()}-seq0"));

        word.Delete();

        Assert.DoesNotContain(word, uks.AtomicThoughts);
        Assert.DoesNotContain(seqStart, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought($"{word.Label.ToLower()}-seq0"));

        // Letters remain
        Assert.Contains(lA, uks.AtomicThoughts);
        Assert.Contains(lB, uks.AtomicThoughts);
    }
    [Fact]
    public void Deleting_items_removes_dependents_but_preserves_shared_targets()
    {
        var uks = CreateUKS();

        // Core thoughts
        Thought animal = uks.GetOrAddThought("animal");
        uks.GetOrAddThought("likes","LinkType");
        Thought dog = uks.GetOrAddThought("dog");
        Thought cat = uks.GetOrAddThought("cat");
        Thought attrFurry = uks.GetOrAddThought("furry");
        Thought attrSharedColor = uks.GetOrAddThought("brown");

        // Hierarchy and attributes
        dog.AddParent(animal);
        cat.AddParent(animal);
        dog.AddLink("hasAttribute", attrFurry);
        dog.AddLink("hasAttribute", attrSharedColor);
        cat.AddLink("hasAttribute", attrSharedColor);

        // A link between animals (label it so the cache can be checked)
        Link likes = dog.AddLink("likes", cat);
        likes.Label = "dog-likes-cat";
        Assert.Same(likes, ThoughtLabels.GetThought("dog-likes-cat"));

        // Two words with shared letters; sequences should be deleted with their owner
        Thought spelled = uks.GetOrAddThought("spelled", "LinkType");
        Thought letterA = uks.GetOrAddThought("A", "symbol");
        Thought letterT = uks.GetOrAddThought("T", "symbol");
        Thought letterC = uks.GetOrAddThought("C", "symbol");
        Thought letterB = uks.GetOrAddThought("B", "symbol");

        var catSeq = uks.AddSequence(cat, spelled, new() { letterC, letterA, letterT });
        string catSeqLabel = $"{cat.Label.ToLower()}-seq0";
        Assert.Same(catSeq, ThoughtLabels.GetThought(catSeqLabel));

        var batSeq = uks.AddSequence(uks.GetOrAddThought("bat"), spelled, new() { letterB, letterA, letterT });

        // Delete cat: its sequence elements should be gone; shared letters remain
        cat.Delete();

        Assert.DoesNotContain(cat, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought(cat.Label));
        Assert.Null(ThoughtLabels.GetThought(catSeqLabel)); // sequence removed

        Assert.Contains(letterA, uks.AtomicThoughts); // shared value survives
        Assert.Contains(letterT, uks.AtomicThoughts);
        Assert.Same(batSeq, ThoughtLabels.GetThought("bat-seq0"));
        Assert.Contains(attrSharedColor, uks.AtomicThoughts); // shared attr survives
        Assert.Contains(dog, uks.AtomicThoughts); // other peer survives

        // The "likes" link should be gone (cat target was deleted)
        Assert.Null(ThoughtLabels.GetThought("dog-likes-cat"));
        Assert.Null(dog.LinksTo.FirstOrDefault(l => l.LinkType?.Label == "likes"));

        // Delete shared attribute that still has another ref (dog); after delete, dog remains
        attrSharedColor.Delete();
        Assert.DoesNotContain(attrSharedColor, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought("brown"));
        Assert.Contains(dog, uks.AtomicThoughts);

        // Delete animal parent; child remains but loses parent link
        animal.Delete();
        Assert.DoesNotContain(animal, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought("animal"));
        Assert.Contains(((Thought)"Unknown"),dog.Parents);

        // Finally delete dog and ensure its links are gone
        dog.Delete();
        Assert.DoesNotContain(dog, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought("dog"));
        Assert.True(uks.AtomicThoughts.All(t => t is not Link l || (l.From != dog && l.To != dog)));
    }

    [Fact]
    public void DeleteThought_RemovesLabelAndReparentsChildrenToUnknown()
    {
        var uks = CreateUKS();
        Thought animal = uks.GetOrAddThought("animal");
        Thought dog = uks.GetOrAddThought("dog");
        dog.AddParent(animal);

        // Sanity: present in label cache
        Assert.Same(animal, ThoughtLabels.GetThought("animal"));
        Thought unknown = ThoughtLabels.GetThought("Unknown");
        Assert.NotNull(unknown);

        // Act
        animal.Delete();

        // Assert: removed from storage and label cache
        Assert.DoesNotContain(animal, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought("animal"));

        // Dog survives but now has Unknown as parent, not animal
        Assert.Contains(unknown, dog.Parents);
        Assert.DoesNotContain(dog.Parents, p => p?.Label == "animal");
    }

    [Fact]
    public void DeleteThought_AllowsSnapshotEnumerationToComplete()
    {
        var uks = CreateUKS();
        Thought a = uks.GetOrAddThought("a");
        Thought b = uks.GetOrAddThought("b");
        Thought c = uks.GetOrAddThought("c");
        uks.GetOrAddThought("likes", "LinkType");

        a.AddLink("likes", b);
        a.AddLink("likes", c);

        // Take a snapshot before deletion
        var snapshot = a.LinksTo.ToList();

        // Delete the owner while still holding the snapshot
        a.Delete();

        // We can still enumerate the snapshot and see both targets
        var targets = snapshot.Select(l => l.To).ToList();
        Assert.Contains(b, targets);
        Assert.Contains(c, targets);
        Assert.Equal(3, targets.Count);

        // Owner is gone from storage and labels
        Assert.DoesNotContain(a, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought("a"));
    }
}
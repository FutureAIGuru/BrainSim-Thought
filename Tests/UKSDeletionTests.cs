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

        var seqStart = uks.AddSequenceAndLink(word, spelled, new() { lA, lB });

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

        var catSeq = uks.AddSequenceAndLink(cat, spelled, new() { letterC, letterA, letterT });
        string catSeqLabel = $"{cat.Label.ToLower()}-seq0";
        Assert.Same(catSeq, ThoughtLabels.GetThought(catSeqLabel));

        var batSeq = uks.AddSequenceAndLink(uks.GetOrAddThought("bat"), spelled, new() { letterB, letterA, letterT });

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
    public void DeleteThought_WithNoParents_ReparentsChildrenToUnknown()
    {
        var uks = CreateUKS();
        Thought temporaryClass = uks.GetOrAddThought("temporaryClass");
        Thought member = uks.GetOrAddThought("member");
        temporaryClass.RemoveParent("Unknown");
        member.AddParent(temporaryClass);
        member.RemoveParent("Unknown");

        // Sanity: present in label cache
        Assert.Same(temporaryClass, ThoughtLabels.GetThought("temporaryClass"));
        Thought unknown = ThoughtLabels.GetThought("Unknown");
        Assert.NotNull(unknown);

        // A child whose only parent is deleted must remain reachable under Unknown.
        temporaryClass.Delete();

        // Assert: removed from storage and label cache
        Assert.DoesNotContain(temporaryClass, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought("temporaryClass"));

        Assert.Contains(unknown, member.Parents);
        Assert.DoesNotContain(member.Parents, parent => parent?.Label == "temporaryClass");
    }

    [Fact]
    public void DeleteThought_ReparentsChildrenToItsParents()
    {
        var uks = CreateUKS();
        Thought animal = uks.GetOrAddThought("animal");
        Thought dog = uks.GetOrAddThought("dog");
        Thought fido = uks.GetOrAddThought("Fido");
        dog.AddParent(animal);
        dog.RemoveParent("Unknown");
        fido.AddParent(dog);
        fido.RemoveParent("Unknown");

        // Fido was known to be an animal through dog. Deleting the intermediate
        // classification must preserve that already-established implication.
        dog.Delete();

        Assert.Contains(animal, fido.Parents);
        Assert.DoesNotContain(dog, fido.Parents);
        Assert.DoesNotContain(fido.Parents, parent => parent.Label == "Unknown");
    }

    [Fact]
    public void RemoveParentLink_DoesNotPromoteTheChildToGrandparent()
    {
        var uks = CreateUKS();
        Thought animal = uks.GetOrAddThought("animal");
        Thought dog = uks.GetOrAddThought("dog");
        Thought fido = uks.GetOrAddThought("Fido");
        dog.AddParent(animal);
        dog.RemoveParent("Unknown");
        fido.AddParent(dog);
        fido.RemoveParent("Unknown");

        // Removing an assertion is different from deleting its target Thought.
        // It must not manufacture the transitive relationship Fido is-a animal.
        fido.RemoveParent(dog);

        Assert.DoesNotContain(dog, fido.Parents);
        Assert.DoesNotContain(animal, fido.Parents);
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

    [Fact]
    public void DeleteThought_UsedAsSequenceValue_PreservesSequenceWithUnknownValue()
    {
        var uks = CreateUKS();
        Thought phrase = uks.GetOrAddThought("phrase");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Thought pigs = uks.GetOrAddThought("pigs", "word");
        Thought are = uks.GetOrAddThought("are", "word");
        Thought animals = uks.GetOrAddThought("animals", "word");
        SeqElement sequence = uks.AddSequenceAndLink(phrase, hasWords, new() { pigs, are, animals });

        // Partial recall preserves the phrase structure. The forgotten position is
        // represented by -- rather than turning the observation into a wildcard.
        pigs.Delete();

        Assert.Contains(phrase, uks.AtomicThoughts);
        Assert.Same(sequence, ThoughtLabels.GetThought(sequence.Label));
        Assert.Contains(phrase.LinksTo, link => link.To == sequence);
        Assert.Equal(new[] { "--", "are", "animals" },
            uks.FlattenSequence(sequence).Select(value => value.Label));
        Assert.Same(uks.Labeled("--"), uks.FlattenSequence(sequence)[0]);
    }

    [Fact]
    public void DeleteThought_UsedAsLinkType_RemovesLinksOfThatType()
    {
        var uks = CreateUKS();
        Thought dog = uks.GetOrAddThought("dog");
        Thought cat = uks.GetOrAddThought("cat");
        Thought likes = uks.GetOrAddThought("likes", "LinkType");
        Link relationship = dog.AddLink(likes, cat);

        // No surviving relationship may refer to a deleted link type.
        likes.Delete();

        Assert.DoesNotContain(relationship, dog.LinksTo);
        Assert.DoesNotContain(relationship, cat.LinksFrom);
    }

    [Fact]
    public void DeleteLinkThought_DetachesItFromItsEndpointsAndDeletesNestedLinks()
    {
        var uks = CreateUKS();
        Thought fido = uks.GetOrAddThought("Fido");
        Thought wet = uks.GetOrAddThought("wet");
        Thought outside = uks.GetOrAddThought("outside");
        Thought isType = uks.GetOrAddThought("is", "LinkType");
        Thought ifType = uks.GetOrAddThought("if", "LinkType");
        Link assertion = fido.AddLink(isType, wet);
        Link condition = assertion.AddLink(ifType, outside);

        // A Link is also a Thought. Deleting it directly must remove both the base
        // relationship and relationships which use that Link as their source.
        assertion.Delete();

        Assert.DoesNotContain(assertion, fido.LinksTo);
        Assert.DoesNotContain(assertion, wet.LinksFrom);
        Assert.DoesNotContain(condition, assertion.LinksTo);
        Assert.DoesNotContain(condition, outside.LinksFrom);
    }

    [Fact]
    public void DeleteOneOwner_LeavesASequenceUsedByAnotherOwner()
    {
        var uks = CreateUKS();
        Thought firstPhrase = uks.GetOrAddThought("firstPhrase");
        Thought secondPhrase = uks.GetOrAddThought("secondPhrase");
        Thought hasWords = uks.GetOrAddThought("hasWords", "LinkType");
        Thought dogs = uks.GetOrAddThought("dogs", "word");
        Thought bark = uks.GetOrAddThought("bark", "word");
        SeqElement sharedSequence = uks.AddSequenceAndLink(firstPhrase, hasWords, new() { dogs, bark });
        secondPhrase.AddLink(hasWords, sharedSequence);

        // Deleting one owner must not delete sequence data still owned elsewhere.
        firstPhrase.Delete();

        Assert.Same(sharedSequence, ThoughtLabels.GetThought(sharedSequence.Label));
        Assert.Contains(secondPhrase.LinksTo, link => link.To == sharedSequence);
    }

    [Fact]
    public void DeleteThought_CanBeCalledTwice()
    {
        var uks = CreateUKS();
        Thought temporary = uks.GetOrAddThought("temporary");

        // Forgetting and cleanup can converge on the same Thought. Repeated deletion
        // should be harmless rather than corrupting neighboring data or throwing.
        temporary.Delete();
        temporary.Delete();

        Assert.DoesNotContain(temporary, uks.AtomicThoughts);
        Assert.Null(ThoughtLabels.GetThought("temporary"));
    }

    [Fact]
    public void AtomicThoughts_UsesReferenceIdentityWhenAThoughtIsRenamed()
    {
        var uks = CreateUKS();
        Thought renamed = uks.GetOrAddThought("beforeRename");

        // Atomic membership must remain valid when a mutable label changes.
        renamed.Label = "afterRename";

        Assert.Contains(renamed, uks.AtomicThoughts);
        renamed.Delete();
        Assert.DoesNotContain(renamed, uks.AtomicThoughts);
    }
}

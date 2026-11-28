using Xunit;
using UKS.Helpers;

namespace UKS.Tests;

public class UKSInstanceTests
{
    [Fact]
    public void Two_instances_store_and_retrieve_their_own_data()
    {
        {
            // Start stepping here: this UKS knows Fido as a dog with fur.
            var firstUKS = new UKS();
            firstUKS.CreateInitialStructure();
            firstUKS.AddStatement("Fido", "is-a", "dog");
            Link firstRelationship = firstUKS.AddStatement("Fido", "has", "fur")!;
            Thought firstFido = firstUKS.Labeled("Fido")!;

            // A second UKS leaves the first intact. This Fido is a bird with feathers.
            var secondUKS = new UKS();
            secondUKS.CreateInitialStructure();
            secondUKS.AddStatement("Fido", "is-a", "bird");
            Link secondRelationship = secondUKS.AddStatement("Fido", "has", "feathers")!;
            Thought secondFido = secondUKS.Labeled("Fido")!;

            // Each store resolves a label to its own Thought.
            Thought retrievedFirstFido = firstUKS.Labeled("Fido")!;
            Thought retrievedSecondFido = secondUKS.Labeled("Fido")!;
            Thought firstCovering = retrievedFirstFido.GetTargetOfFirstLinkOfType("has")!;
            Thought secondCovering = retrievedSecondFido.GetTargetOfFirstLinkOfType("has")!;

            Assert.Same(firstFido, retrievedFirstFido);
            Assert.Same(secondFido, retrievedSecondFido);
            Assert.Equal("fur", firstCovering.Label);
            Assert.Equal("feathers", secondCovering.Label);
            Assert.True(firstFido.HasAncestor("dog"));
            Assert.True(secondFido.HasAncestor("bird"));
            Assert.Null(firstUKS.Labeled("feathers"));
            Assert.Null(secondUKS.Labeled("fur"));
            Assert.NotSame(firstFido, secondFido);
            Assert.False(firstFido == secondFido);
            Assert.Same(firstUKS, firstFido.Store);
            Assert.Same(secondUKS, secondFido.Store);
            Assert.Same(firstUKS, firstRelationship.Store);
            Assert.Same(secondUKS, secondRelationship.Store);

            // Deleting the first Fido leaves the second Fido and its data intact.
            firstFido.Delete();

            Assert.Null(firstUKS.Labeled("Fido"));
            Assert.Same(secondFido, secondUKS.Labeled("Fido"));
            Assert.Same(secondCovering, secondFido.GetTargetOfFirstLinkOfType("has"));
        }
    }
}

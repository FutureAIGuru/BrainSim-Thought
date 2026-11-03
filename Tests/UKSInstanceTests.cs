using Xunit;

namespace UKS.Tests;

public class UKSInstanceTests
{
    [Fact]
    public void Two_instances_store_and_retrieve_their_own_data()
    {
        UKS previousUKS = UKS.theUKS;
        try
        {
            // Start stepping here: this UKS knows Fido as a dog with fur.
            var firstUKS = new UKS();
            firstUKS.CreateInitialStructure();
            firstUKS.AddStatement("Fido", "is-a", "dog");
            Link firstRelationship = firstUKS.AddStatement("Fido", "has", "fur")!;
            Thought firstFido = firstUKS.Labeled("Fido")!;

            // Creating another UKS selects it for string conversion, but leaves
            // the first UKS intact. This Fido is a bird with feathers.
            var secondUKS = new UKS();
            secondUKS.CreateInitialStructure();
            secondUKS.AddStatement("Fido", "is-a", "bird");
            Link secondRelationship = secondUKS.AddStatement("Fido", "has", "feathers")!;
            Thought secondFido = secondUKS.Labeled("Fido")!;

            // Explicit instance lookups work regardless of which UKS is selected.
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

            // The shorthand needs a selection because "Fido" alone cannot
            // specify which UKS to search. Watch these two variables differ.
            UKS.theUKS = firstUKS;
            Thought firstFidoFromString = "Fido";
            UKS.theUKS = secondUKS;
            Thought secondFidoFromString = "Fido";

            Assert.Same(firstFido, firstFidoFromString);
            Assert.Same(secondFido, secondFidoFromString);

            // A Thought remembers its UKS even while another one is selected.
            // Deleting the first Fido leaves the second Fido and its data intact.
            firstFido.Delete();

            Assert.Null(firstUKS.Labeled("Fido"));
            Assert.Same(secondFido, secondUKS.Labeled("Fido"));
            Assert.Same(secondCovering, secondFido.GetTargetOfFirstLinkOfType("has"));
        }
        finally
        {
            UKS.theUKS = previousUKS;
        }
    }
}

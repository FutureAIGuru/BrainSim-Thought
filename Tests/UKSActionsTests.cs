using Xunit;

namespace UKS.Tests;

public class UKSActionsTests
{
    [Fact]
    public void SetActionsReplaceOnlyExclusiveRelationships()
    {
        // English intent: setting another non-exclusive possession adds a fact;
        // setting an exclusive location replaces the previous location.
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        Thought fido = uks.GetOrAddThought("Fido");
        Thought fur = uks.GetOrAddThought("fur");
        Thought tail = uks.GetOrAddThought("tail");
        Thought inside = uks.GetOrAddThought("inside");
        Thought outside = uks.GetOrAddThought("outside");
        Thought has = uks.GetOrAddThought("has", "LinkType");
        Thought location = uks.GetOrAddThought("location", "LinkType");
        location.AddProperty(uks.GetOrAddThought("isExclusive"));
        uks.GetOrAddThought("SET", "LinkType");
        Thought setHas = uks.GetOrAddThought("SET.has", "LinkType");
        Thought setLocation = uks.GetOrAddThought("SET.location", "LinkType");
        Assert.Contains(location, setLocation.Parents);

        // Applying SET.location must use inheritance, not its optional dotted
        // metadata link. Removing that metadata should not affect execution.
        setLocation.RemoveLinks("is");

        uks.ApplySetAction(new Link(fido, setHas, fur));
        uks.ApplySetAction(new Link(fido, setHas, tail));

        Assert.NotNull(uks.GetLink(fido, has, fur));
        Assert.NotNull(uks.GetLink(fido, has, tail));

        uks.ApplySetAction(new Link(fido, setLocation, outside));
        uks.ApplySetAction(new Link(fido, setLocation, inside));

        Assert.Null(uks.GetLink(fido, location, outside));
        Assert.NotNull(uks.GetLink(fido, location, inside));
    }
}

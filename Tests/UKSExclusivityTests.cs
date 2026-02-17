using UKS;
using Xunit;

namespace UKS.Tests;

public class UKSExclusivityTests
{
    private UKS CreateUKS() { var uks = new UKS(clear: true); uks.CreateInitialStructure(); return uks; }

    private void SeedProvidedLinks(UKS uks)
    {
        // gender exclusivity
        uks.AddStatement("gender", "hasProperty", "isexclusive");
        uks.AddStatement("female", "is-a", "gender"); // L03
        uks.AddStatement("male", "is-a", "gender");   // L04
        uks.AddStatement("Mary", "is-a", "female");   // L01
        uks.AddStatement("Mary", "is-a", "male");     // L02

        // legs/ears/head/body
        uks.AddStatement("dog", "is-a", "animal");
        uks.AddStatement("Fido", "is-a", "dog");      // L05
        uks.AddStatement("Tripper", "is-a", "dog");   // L26

        // numbers already exist; tag link types with numeric attributes
        uks.AddStatement("has.4", "is", "4");
        uks.AddStatement("has.3", "is", "3");
        uks.AddStatement("has.2", "is", "2");

        uks.AddStatement("Fido", "has.4", "legs");    // L06
        uks.AddStatement("Fido", "has.3", "legs");    // L07
        uks.AddStatement("Tripper", "has.4", "legs"); // L27
        uks.AddStatement("Tripper", "has.3", "legs"); // L28

        uks.AddStatement("Fido", "has.2", "ears");    // L09
        uks.AddStatement("ears", "is-part-of", "head");   // L10
        uks.AddStatement("legs", "is-part-of", "body");   // L11

        // colors exclusive
        uks.AddStatement("color", "hasProperty", "isexclusive");
        uks.AddStatement("brown", "is-a", "color");   // L13
        uks.AddStatement("black", "is-a", "color");   // L14
        uks.AddStatement("Fido", "is", "brown");      // L12
        uks.AddStatement("Fido", "is", "black");      // L15

        // negation link type
        uks.AddStatement("NOT has.4", "hasAttribute", "not");
        uks.AddStatement("Fido", "NOT has.4", "legs"); // L08

        // misc car/engine/wheels (non-exclusive baseline)
        uks.AddStatement("car", "has", "engine");     // L16
        uks.AddStatement("engine", "is-part-of", "car"); // L17
        uks.AddStatement("car", "has.4", "wheels");   // L18
        uks.AddStatement("wheels", "is-a", "part");   // L19

        // gender conflict via parents
        uks.AddStatement("father", "is-a", "man");    // L21
        uks.AddStatement("man", "is-a", "gender");    // L22
        uks.AddStatement("hairdresser", "is-a", "woman"); // L24
        uks.AddStatement("woman", "is-a", "gender");  // L25
        uks.AddStatement("Sam", "is-a", "father");    // L20
        uks.AddStatement("Sam", "is-a", "hairdresser"); // L23

        uks.AddStatement("injured", "is-a", "condition"); // L30
        uks.AddStatement("Tripper", "is", "injured");     // L29
    }

    [Fact]
    public void LinksAreExclusive_FemaleVsMale_IsExclusiveByGenderProperty()
    {
        var uks = CreateUKS();
        SeedProvidedLinks(uks);

        var l1 = uks.GetLink(uks.AddStatement("Mary", "is-a", "female"));
        var l2 = uks.GetLink(uks.AddStatement("Mary", "is-a", "male"));

        Assert.True(uks.LinksAreExclusive_ForTests(l1, l2));
    }

    [Fact]
    public void LinksAreExclusive_DifferentNumbersSameTarget_AreExclusive()
    {
        var uks = CreateUKS();
        SeedProvidedLinks(uks);

        var fourLegs = uks.GetLink(uks.AddStatement("Fido", "has.4", "legs"));
        var threeLegs = uks.GetLink(uks.AddStatement("Fido", "has.3", "legs"));

        Assert.True(uks.LinksAreExclusive_ForTests(fourLegs, threeLegs));
    }

    [Fact]
    public void LinksAreExclusive_PositiveVsNotSameLinkType_AreExclusive()
    {
        var uks = CreateUKS();
        SeedProvidedLinks(uks);

        var positive = uks.GetLink(uks.AddStatement("Fido", "has.4", "legs"));
        var negative = uks.GetLink(uks.AddStatement("Fido", "NOT has.4", "legs"));

        Assert.True(uks.LinksAreExclusive_ForTests(positive, negative));
    }

    [Fact]
    public void LinksAreExclusive_ColorsExclusiveOnSameSubject_AreExclusive()
    {
        var uks = CreateUKS();
        SeedProvidedLinks(uks);

        var brown = uks.GetLink(uks.AddStatement("Fido", "is", "brown"));
        var black = uks.GetLink(uks.AddStatement("Fido", "is", "black"));

        Assert.True(uks.LinksAreExclusive_ForTests(brown, black));
    }

    [Fact]
    public void LinksAreExclusive_UnrelatedLinks_NotExclusive()
    {
        var uks = CreateUKS();
        SeedProvidedLinks(uks);

        var carHasEngine = uks.GetLink(uks.AddStatement("car", "has", "engine"));
        var fidoHasEars = uks.GetLink(uks.AddStatement("Fido", "has.2", "ears"));

        Assert.False(uks.LinksAreExclusive_ForTests(carHasEngine, fidoHasEars));
    }
}
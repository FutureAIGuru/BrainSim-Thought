using BrainSimulator;
using BrainSimulator.Modules;
using UKS;
using Xunit;
using System.Linq;

namespace BrainSimulator.Tests;

public class ModuleAttributeBubbleTests
{
    private static UKS.UKS CreateUKS()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        MainWindow.theUKS = uks;
        return uks;
    }

    [Fact]
    public void BubbleChildAttributes_AllChildrenHaveSameAttribute_BubblesUp()
    {
        // Arrange
        var uks = CreateUKS();
        var module = new ModuleAttributeBubble { theUKS = uks };
        
        var animal = uks.GetOrAddThought("Animal", "Object");
        var dog = uks.GetOrAddThought("Dog", animal);
        var cat = uks.GetOrAddThought("Cat", animal);
        var bird = uks.GetOrAddThought("Bird", animal);

        var breathes = uks.GetOrAddThought("breathes", "LinkType");
        var air = uks.GetOrAddThought("air", "Object");
        
        dog.AddLink(breathes, air).Weight = 1.0f;
        cat.AddLink(breathes, air).Weight = 1.0f;
        bird.AddLink(breathes, air).Weight = 1.0f;

        // Act
        module.DoTheWork();

        // Assert
        var animalBreathes = animal.LinksTo.FirstOrDefault(l => 
            l.LinkType == breathes && l.To == air);
        Assert.NotNull(animalBreathes);
        Assert.True(animalBreathes.Weight > 0.5f);
    }

    [Fact]
    public void BubbleChildAttributes_SingleChild_DoesNotBubble()
    {
        var uks = CreateUKS();
        var parent = uks.GetOrAddThought("SingleChildParent", "Object");
        var fido = uks.GetOrAddThought("Fido", parent);
        var has = uks.GetOrAddThought("has", "LinkType");
        var fur = uks.GetOrAddThought("fur", "Object");
        fido.AddLink(has, fur).Weight = 1.0f;

        bool changed = uks.BubbleSharedAttributes(parent);

        Assert.False(changed);
        Assert.Null(parent.HasLink(has, fur));
        Assert.NotNull(fido.HasLink(has, fur));
    }

    [Fact]
    public void BubbleChildAttributes_MajorityHaveAttribute_BubblesUp()
    {
        // Arrange
        var uks = CreateUKS();
        var module = new ModuleAttributeBubble { theUKS = uks };
        
        var vehicle = uks.GetOrAddThought("Vehicle", "Object");
        var car = uks.GetOrAddThought("Car", vehicle);
        var truck = uks.GetOrAddThought("Truck", vehicle);
        var boat = uks.GetOrAddThought("Boat", vehicle);
        var bicycle = uks.GetOrAddThought("Bicycle", vehicle);

        var hasEngine = uks.GetOrAddThought("has", "LinkType");
        var engine = uks.GetOrAddThought("engine", "Object");

        car.AddLink(hasEngine, engine).Weight = 1.0f;
        truck.AddLink(hasEngine, engine).Weight = 1.0f;
        boat.AddLink(hasEngine, engine).Weight = 1.0f;

        // Act
        module.DoTheWork();

        // Assert
        var vehicleHasEngine = vehicle.LinksTo.FirstOrDefault(l => 
            l.LinkType == hasEngine && l.To == engine);
        Assert.NotNull(vehicleHasEngine);
    }

    [Fact]
    public void BubbleChildAttributes_ConflictingAttributes_DoesNotBubble()
    {
        // Arrange
        var uks = CreateUKS();
        var module = new ModuleAttributeBubble { theUKS = uks };
        
        var shape = uks.GetOrAddThought("Shape", "Object");
        var circle = uks.GetOrAddThought("Circle", shape);
        var square = uks.GetOrAddThought("Square", shape);

        var hasCorners = uks.GetOrAddThought("hasCorners", "LinkType");
        var zero = uks.GetOrAddThought("0", "number");
        var four = uks.GetOrAddThought("4", "number");
        
        var number = uks.GetOrAddThought("number", "Object");
        number.AddLink("hasProperty", uks.GetOrAddThought("isExclusive"));

        circle.AddLink(hasCorners, zero).Weight = 1.0f;
        square.AddLink(hasCorners, four).Weight = 1.0f;

        // Act
        module.DoTheWork();

        // Assert
        var shapeCorners = shape.LinksTo.Where(l => l.LinkType == hasCorners).ToList();
        Assert.True(shapeCorners.Count <= 1);
    }

    [Fact]
    public void BubbleChildAttributes_RemovesRedundantDirectChildAttributes()
    {
        // Arrange
        var uks = CreateUKS();
        var module = new ModuleAttributeBubble { theUKS = uks };
        
        var animal = uks.GetOrAddThought("Animal", "Object");
        var dog = uks.GetOrAddThought("Dog", animal);
        var cat = uks.GetOrAddThought("Cat", animal);

        var breathes = uks.GetOrAddThought("breathes", "LinkType");
        var air = uks.GetOrAddThought("air", "Object");
        
        dog.AddLink(breathes, air).Weight = 1.0f;
        cat.AddLink(breathes, air).Weight = 1.0f;

        // Act
        module.DoTheWork();

        // Assert
        Assert.DoesNotContain(dog.LinksTo, l => l.LinkType == breathes && l.To == air);
        Assert.DoesNotContain(cat.LinksTo, l => l.LinkType == breathes && l.To == air);
        Assert.NotNull(animal.HasLink(breathes, air));
        Assert.Contains(uks.GetAttributes(dog), l => l.LinkType == breathes && l.To == air);
        Assert.Contains(uks.GetAttributes(cat), l => l.LinkType == breathes && l.To == air);
    }

    [Fact]
    public void BubbleChildAttributes_ExistingParentAttributeStillRemovesChildCopies()
    {
        var uks = CreateUKS();
        var module = new ModuleAttributeBubble { theUKS = uks };
        var animal = uks.GetOrAddThought("Animal", "Object");
        var dog = uks.GetOrAddThought("Dog", animal);
        var cat = uks.GetOrAddThought("Cat", animal);
        var breathes = uks.GetOrAddThought("breathes", "LinkType");
        var air = uks.GetOrAddThought("air", "Object");

        animal.AddLink(breathes, air).Weight = 0.99f;
        dog.AddLink(breathes, air).Weight = 1.0f;
        cat.AddLink(breathes, air).Weight = 1.0f;

        module.DoTheWork();

        Assert.NotNull(animal.HasLink(breathes, air));
        Assert.Null(dog.HasLink(breathes, air));
        Assert.Null(cat.HasLink(breathes, air));
    }

}

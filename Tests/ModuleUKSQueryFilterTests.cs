using System.Linq;
using System.IO;
using BrainSimulator.Modules;
using Xunit;

namespace BrainSimulator.Tests;

public class ModuleUKSQueryFilterTests
{
    [Fact]
    public void Full_attribute_list_returns_successful_rules_as_assertions()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        string demoPath = Path.Combine(FindRepositoryRoot(),
            "BrainSimulator", "UKSContent", "ch4ch5-fidodemo.txt");
        uks.ImportTextFile(demoPath);
        BrainSimulator.MainWindow.theUKS = uks;
        var module = new ModuleUKSQuery { theUKS = uks };

        module.GetAttributes("Fido", "", "", "", out _, out var links);

        Assert.Contains(links, link => link.LinkType?.Label == "is" && link.To?.Label == "wet");
        Assert.DoesNotContain(links, link => link.LinkType?.Label == "is.?" && link.To?.Label == "wet");
    }

    [Fact]
    public void Single_filter_thought_filters_the_target()
    {
        var module = CreateModule();

        module.GetAttributes("Fido", "", "", "color", out _, out var links);

        Assert.Equal(new[] { "blue", "red" }, links.Select(link => link.To.Label).OrderBy(label => label));
    }

    [Fact]
    public void Multiple_filter_thoughts_filter_the_link_type()
    {
        var module = CreateModule();

        module.GetAttributes("Fido", "", "", "can color", out _, out var links);

        Assert.Single(links);
        Assert.Equal("can", links[0].LinkType.Label);
        Assert.Equal("blue", links[0].To.Label);
    }

    private static ModuleUKSQuery CreateModule()
    {
        var uks = new UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        uks.AddStatement("animal", "is-a", "Object");
        uks.AddStatement("color", "is-a", "Object");
        uks.AddStatement("red", "is-a", "color");
        uks.AddStatement("blue", "is-a", "color");
        uks.AddStatement("Fido", "is-a", "animal");
        uks.AddStatement("animal", "has", "red");
        uks.AddStatement("animal", "can", "blue");
        BrainSimulator.MainWindow.theUKS = uks;
        return new ModuleUKSQuery { theUKS = uks };
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BrainSim Thought.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("BrainSim Thought repository root not found.");
    }
}

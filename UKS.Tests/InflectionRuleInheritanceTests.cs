using System.Collections.Generic;
using System.Linq;
using BrainSimulator.Modules;
using Xunit;
using Xunit.Abstractions;

namespace UKS.Tests;

/// <summary>
/// Step 1 of rule prioritization by inheritance: can inflection rules arranged as an is-a tree take
/// their properties from UKS inheritance? A child rule inherits what its parent forms
/// (formsInflection, several values) and overrides an exclusive property (its backward reading
/// preference) the way Tripper's three legs override a dog's four; a grandchild overrides again.
/// Only the rules' own short ancestry is read (GetAttributes on a rule, never on a word).
/// </summary>
public class InflectionRuleInheritanceTests
{
    private readonly ITestOutputHelper output;
    public InflectionRuleInheritanceTests(ITestOutputHelper output) => this.output = output;

    private static UKS Store(bool withVocabulary)
    {
        var store = new UKS();
        store.CreateInitialStructure();
        if (withVocabulary) new ModuleInflect { theUKS = store, KnowledgeFiles = ModuleInflect.CoreFiles }.Initialize();
        foreach (string line in new[]
        {
            "[ruleUnderTest->is-a->languageElement]",
            "[readsBackward->is-a->LinkType]",
            "[backwardPreference->is-a->languageElement]",
            "[backwardPreference->hasProperty->isExclusive]",
            "[preferred->is-a->backwardPreference]",
            "[avoided->is-a->backwardPreference]",
            "[testEdDefault->is-a->ruleUnderTest]",
            "[testEdDefault->formsInflection->pastTense]",
            "[testEdDefault->formsInflection->pastParticiple]",
            "[testEdDefault->readsBackward->preferred]",
            "[testEdDoubling->is-a->testEdDefault]",
            "[testEdAfterE->is-a->testEdDefault]",
            "[testEdAfterE->readsBackward->avoided]",
            "[testEdAfterVowelConsonantE->is-a->testEdAfterE]",
            "[testEdAfterVowelConsonantE->readsBackward->preferred]",
        })
            store.ProcessSingleLine(line);
        return store;
    }

    private static List<string> Values(UKS store, string rule, string linkType)
    {
        List<Link> attributes = store.GetAttributes(store.Labeled(rule)).ToList();
        return attributes.Where(link => link.LinkType?.Label == linkType).Select(link => link.To.Label).OrderBy(label => label).ToList();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AChildRuleInheritsWhatItFormsAndOverridesItsPreference(bool withVocabulary)
    {
        UKS store = Store(withVocabulary);
        foreach (string rule in new[] { "testEdDefault", "testEdDoubling", "testEdAfterE", "testEdAfterVowelConsonantE" })
            output.WriteLine($"{rule}: forms [{string.Join(", ", Values(store, rule, "formsInflection"))}], " +
                $"readsBackward [{string.Join(", ", Values(store, rule, "readsBackward"))}]");

        foreach (string rule in new[] { "testEdDefault", "testEdDoubling", "testEdAfterE", "testEdAfterVowelConsonantE" })
            Assert.Equal(["pastParticiple", "pastTense"], Values(store, rule, "formsInflection"));
        Assert.Equal(["preferred"], Values(store, "testEdDefault", "readsBackward"));
        Assert.Equal(["preferred"], Values(store, "testEdDoubling", "readsBackward"));        // inherited
        Assert.Equal(["avoided"], Values(store, "testEdAfterE", "readsBackward"));            // overrides
        Assert.Equal(["preferred"], Values(store, "testEdAfterVowelConsonantE", "readsBackward")); // overrides again
    }
}

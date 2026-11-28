using BrainSimulator.Modules;
using Xunit;
using UKS.Helpers;

namespace UKS.Tests;

/// <summary>
/// Template MEANS operands refer to template elements by role (the element's value Thought),
/// and sentence words are bound to the elements they fill through FILLS links. No step uses
/// a numbered position.
/// </summary>
public class ModuleInterpretRoleTests
{
    private static (UKS store, ModuleInterpret module) FreshInterpreter()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        var module = new ModuleInterpret { theUKS = store };
        module.Initialize();
        return (store, module);
    }

    private static IEnumerable<Thought> Operands(Link definition) => definition.To is Link pattern
        ? new[] { pattern.From, pattern.LinkType, pattern.To }
        : new[] { definition.To };

    [Fact]
    public void EveryMeansOperandNamesAtMostOneTemplateElementByRole()
    {
        var (store, _) = FreshInterpreter();
        Thought means = store.Labeled("means");
        Thought wildcard = store.Labeled("??");
        var templates = store.Labeled("languagePattern").Descendants
            .Where(template => template.LinksTo.Any(link => link.LinkType == means)).ToList();
        Assert.Contains(store.Labeled("sentenceTemplate"), templates);
        Assert.Contains(store.Labeled("quantityPossession4"), templates);

        foreach (Thought template in templates)
        {
            var elements = store.EnumerateSequenceElements((SeqElement)template.GetTargetOfFirstLinkOfType("hasWords")).ToList();
            foreach (Link definition in template.LinksTo.Where(link => link.LinkType == means))
                foreach (Thought operand in Operands(definition))
                {
                    Assert.IsNotType<SeqElement>(operand);
                    if (!operand.HasProperty("isWildcard")) continue;
                    var named = elements.Where(element => ReferenceEquals(element.VLU, operand)).ToList();
                    // The bare ?? is an open query variable, never a template element.
                    if (ReferenceEquals(operand, wildcard)) Assert.Empty(named);
                    else Assert.True(named.Count == 1, $"{template.Label}: {operand.Label} names {named.Count} elements.");
                }
        }
    }

    [Fact]
    public void RolesConstrainOnlyByTheirClass()
    {
        var (store, _) = FreshInterpreter();
        Thought subject = store.Labeled("??.nounPhrase.subjectRole");

        Assert.True(subject.HasProperty("isWildcard"));
        Assert.True(store.Labeled("subjectRole").HasProperty("isWildcard"));
        Assert.Equal(new[] { store.Labeled("nounPhrase") },
            subject.Parents.Where(parent => !parent.HasProperty("isWildcard")));
        Assert.True(store.Labeled("??.noun.complementRole").HasAncestor("noun"));
    }

    [Fact]
    public void SubjectAndComplementWordsFillTheirRoles()
    {
        var (store, module) = FreshInterpreter();
        Thought sentence = module.GetSequenceFromText("The black dog is chasing the cat");
        module.GetTemplates(sentence);
        module.GetMeanings(sentence);

        var templateElements = store.EnumerateSequenceElements(
            (SeqElement)store.Labeled("sentenceTemplate").GetTargetOfFirstLinkOfType("hasWords")).ToList();
        SeqElement subjectElement = Assert.Single(templateElements, element => element.VLU == store.Labeled("??.nounPhrase.subjectRole"));
        SeqElement complementElement = Assert.Single(templateElements, element => element.VLU == store.Labeled("??.nounPhrase.complementRole"));

        Thought fills = store.Labeled("fills");
        var words = store.EnumerateSequenceElements((SeqElement)sentence.GetTargetOfFirstLinkOfType("hasWords")).ToList();
        bool Fills(SeqElement word, SeqElement element) => word.LinksTo.Any(link => link.LinkType == fills && link.To == element);
        Assert.Equal(new[] { "w:the", "w:black", "w:dog" }, words.Where(word => Fills(word, subjectElement)).Select(word => word.VLU.Label));
        Assert.Equal(new[] { "w:the", "w:cat" }, words.Where(word => Fills(word, complementElement)).Select(word => word.VLU.Label));
    }

    [Fact]
    public void InitializationReplacesTheWordSequencesOfANetworkSavedByAnEarlierVersion()
    {
        var store = new UKS();
        store.CreateInitialStructure();
        // A saved network already holds the earlier sequences, without roles.
        store.ProcessSingleLine("[sentenceTemplate->hasWords->^??.nounPhrase ??.verbPhrase ??.nounPhrase]");
        store.ProcessSingleLine("[copulaClassificationTemplate->hasWords->^??.nounPhrase ??.copula ??.nounPhrase]");
        var module = new ModuleInterpret { theUKS = store };

        module.UKSInitializedNotification();

        Thought hasWords = store.Labeled("hasWords");
        foreach (string name in new[] { "sentenceTemplate", "copulaClassificationTemplate" })
        {
            Link sequence = Assert.Single(store.Labeled(name).LinksTo, link => link.LinkType == hasWords);
            Assert.Same(store.Labeled("??.nounPhrase.subjectRole"), ((SeqElement)sequence.To).VLU);
        }
        string output = module.Input("Dogs are animals.", rememberUnknownWords: true);
        Assert.Contains("[dog→is-a.!→animal]", output);
        Assert.DoesNotContain("subjectRole→", output);
        Assert.Contains("[dog→chases.ongoing.!→cat]", module.Input("The dog is chasing the cat.", rememberUnknownWords: true));
    }

    [Fact]
    public void AnUnboundRoleIsNeverExecuted()
    {
        var (store, module) = FreshInterpreter();
        Thought subject = store.Labeled("??.nounPhrase.subjectRole");
        // This template has no subjectRole element, so the role cannot be bound.
        store.ProcessSingleLine("[copulaAttributeTemplate->means->[??.nounPhrase.subjectRole->is.!->??.adjective]]");

        string output = module.Input("Fido is green.", rememberUnknownWords: true);

        Assert.Contains("[fido→is.!→green]", output);
        Assert.DoesNotContain("subjectRole→", output);
        Assert.DoesNotContain(subject.LinksTo, link => link.LinkType == store.Labeled("is"));
    }

    [Fact]
    public void InitializationRemovesPositionalMeansFromOlderNetworks()
    {
        var (store, module) = FreshInterpreter();
        Thought means = store.Labeled("means");
        Thought template = store.Labeled("possessionTemplate");
        SeqElement head = (SeqElement)template.GetTargetOfFirstLinkOfType("hasWords");
        // The form earlier versions built: operands are the template's own sequence elements.
        template.AddLink(means, new Link(head, head.NXT, head.NXT.NXT));

        module.Initialize();

        Assert.DoesNotContain(template.LinksTo, link => link.LinkType == means && link.To is Link pattern &&
            (pattern.From is SeqElement || pattern.LinkType is SeqElement || pattern.To is SeqElement));
        Assert.Contains("[fido→has.!→fur]", module.Input("Fido has fur."));
    }
}

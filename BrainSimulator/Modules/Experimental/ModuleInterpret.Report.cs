#nullable enable

using System.Collections.Generic;
using System.Linq;
using UKS;
using UKS.Helpers;

namespace BrainSimulator.Modules;

// The diagnostic report Interpret returns (the dialog's Details box), kept apart from the pipeline so
// Interpret reads as its stages. Tests assert on this text.
public sealed partial class ModuleInterpret
{
    private const string StillLearningNote = " (I'm still learning sentences like this one.)";

    private const string NotUnderstoodReport =
        "\n\nNot understood\nNo template covers the whole sentence, so nothing was stored or learned." +
        "\nThe next input is taken as saying it another way, unless it is a question or unrelated.";

    /// <summary>
    /// What recognition found: the sentence's words, the templates matched, each word's classes and
    /// templates, the meanings bound, and the competing whole-sentence readings if there are several.
    /// </summary>
    private string RecognitionReport(Thought sentenceThought, List<Thought> templates, List<Thought> meanings, List<Thought> readings)
    {
        SeqElement sequence = (SeqElement)sentenceThought.GetTargetOfFirstLinkOfType(hasWords)!;
        string words = "Sentence input: " + sequence;
        string templateText = templates.Count == 0
            ? "No matching templates."
            : "Templates\n" + string.Join("\n", templates.Select(
                template => template.Label + "    " + template.GetTargetOfFirstLinkOfType(hasWords)));
        string attachments = string.Join("\n", theUKS.EnumerateSequenceElements(sequence).Select(element =>
            element.VLU.Label + " (" + string.Join(", ", WordProperties(element)) + ")" +
            "    " + string.Join(", ", element.LinksTo
                .Where(link => link.LinkType == matchesTemplate).Select(link => link.To.Label))));
        string meaningText = string.Join("\n", meanings.Select(meaning => meaning.ToString()));
        string report = words + "\n\n" + templateText + "\n\nWord attachments\n" + attachments +
            "\n\nMeanings\n" + meaningText;
        if (readings.Count > 1)
            report += "\n\nCompeting readings (the strongest is acted on)\n" +
                string.Join("\n", readings.Select(reading => $"{TemplateOf(reading).Label}  weight {TemplateOf(reading).Weight:0.00}"));
        return report;
    }

    private static string WeightReport(Thought template, float before, bool wasPlastic) =>
        $"\n\nTemplate {template.Label}: weight {before:0.00}, now {template.Weight:0.00}" +
        (wasPlastic && !template.IsPlastic ? " (consolidated)" : template.IsPlastic ? " (plastic)" : "");

    private static string StatementReport(List<Link> executed) =>
        executed.Count == 0 ? "" : "\n\nExecuted relationships\n" + string.Join("\n", executed);

    private static string QueryReport(List<Link> results, string answer) =>
        "\n\nQuery results\n" + (results.Count == 0 ? "No matching relationships." : string.Join("\n", results)) +
        "\n\nAnswer\n" + answer;

    private string UncertainSpellingReport(UncertainSpellingException question) =>
        $"\"{question.Typed}\" is not a known word, but is close to \"{Spelling(question.Suggestion)}\"." +
        "\nAnswer yes to read it that way, or no to take it as a new word.";
}

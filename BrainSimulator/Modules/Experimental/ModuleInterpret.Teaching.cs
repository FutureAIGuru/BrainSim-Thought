#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UKS;

namespace BrainSimulator.Modules;

// Teaching: a sentence that was not understood, said another way in sentences that are, becomes a
// new template. The explanation's meanings are lined up with the sentence by reference: each Thought
// they mention is the meaning of a word or phrase of the sentence, which becomes an element with a
// new role. The template is captured weak and plastic, and competes like any other (Learning).
public sealed partial class ModuleInterpret
{
    private const string NotUnderstood = "I don't understand. Can you say it another way?";

    // One stretch of a sentence that means one Thought: a recognized phrase ("the cat") or a word.
    private sealed record Unit(SeqElement First, SeqElement Last, Thought Class, Thought? Meaning, Thought? Word);

    /// <summary>
    /// Takes an input as the explanation of a sentence that was not understood. It is one only if
    /// each of its sentences is an understood statement and it shares something with the sentence;
    /// otherwise nothing changes and false is returned, so the input is read as new.
    /// </summary>
    /// <returns>True if the input was taken as the explanation (learned from, or found not to fit).</returns>
    private bool TryToLearnFromExplanation(string unexplained, string explanation, bool rememberUnknownWords, out string report)
    {
        report = "";
        // The same words again, only punctuated differently, are a correction of the input, not an
        // explanation of it ("the cat chased fido the cat chased rover", then with a period).
        if (WordsOnly(explanation) == WordsOnly(unexplained)) return false;
        UKSTransaction transaction = theUKS.BeginTransaction();
        bool keep = false;
        try
        {
            // The explanation: one or more statements, each understood.
            var statements = new List<Link>();
            foreach (string part in SplitSentences(explanation))
            {
                if (Read(part, rememberUnknownWords) is not Link covering) return false;
                List<Link> said = covering.LinksTo.Where(link => link.LinkType == means)
                    .Select(link => link.To).OfType<Link>().ToList();
                if (said.Count == 0 || said.Any(statement => statement.LinkType?.HasProperty("isPossible") == true)) return false;
                statements.AddRange(said);
            }
            if (statements.Count == 0) return false;

            // The sentence it explains, read again for its words and phrases.
            if (ReadSentence(unexplained, rememberUnknownWords) is not Thought sentence) return false;
            List<Unit> units = Units(sentence);
            // An input that shares nothing with the sentence is not an explanation of it.
            if (!units.Any(unit => unit.Meaning is not null && statements.Any(statement => Mentions(statement, unit.Meaning))))
                return false;

            string header = $"Explaining \"{unexplained}\" as \"{explanation}\"";
            if (Align(units, statements, out Dictionary<Unit, string> elements) is string problem)
            {
                pendingSentence = unexplained;
                Answer = problem + " Can you say it another way?";
                report = header + "\n\nNot learned\n" + problem;
                return true;
            }

            List<string> definition = DefineLearnedTemplate(units, elements, statements, out Thought template);

            // The new template now covers the sentence: read it again and state what it says.
            if (ReadSentence(unexplained, rememberUnknownWords) is not Thought reread ||
                Covering(reread) is not Link match || match.To != template)
            {
                Answer = "I still can't read it that way.";
                report = header + "\n\nLearned template (not kept)\n" + string.Join("\n", definition);
                return true;
            }
            List<Link> executed = Execute(GetMeanings(reread));
            RecordSource(executed, template);
            keep = executed.Count > 0;
            Answer = keep ? "Thanks, I understand now. (I'm still learning sentences like this one.)" : "I still can't read it that way.";
            report = header + "\n\nLearned template\n" + string.Join("\n", definition) +
                $"\n\nWeight {template.Weight:0.00} (plastic); each successful use strengthens it." +
                "\n\nExecuted relationships\n" + string.Join("\n", executed);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        finally
        {
            theUKS.EndTransaction(transaction);
            if (keep) theUKS.RememberForUndo($"{unexplained} (explained as: {explanation})", transaction);
            else theUKS.Undo(transaction);
        }
    }

    // Sentences end with a period, question mark or exclamation mark.
    private static IEnumerable<string> SplitSentences(string text) =>
        Regex.Split(text ?? "", @"(?<=[.!?])\s+").Select(part => part.Trim()).Where(part => part.Length > 0);

    private static string WordsOnly(string text) =>
        string.Join(" ", Regex.Split((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}']+").Where(word => word.Length > 0));

    // Reads a sentence (without executing it); the match of the template covering it, or null.
    private Link? Read(string text, bool rememberUnknownWords) =>
        ReadSentence(text, rememberUnknownWords) is Thought sentence ? Covering(sentence) : null;

    private Thought? ReadSentence(string text, bool rememberUnknownWords)
    {
        Thought? sentence = GetSequenceFromText(text, rememberUnknownWords);
        if (sentence is null) return null;
        GetTemplates(sentence);
        GetMeanings(sentence);
        return sentence;
    }

    /// <summary>
    /// The stretches of a sentence, in order: each recognized phrase (its class is the phrase's class,
    /// such as nounPhrase, and its meaning the phrase's), and each word outside one (its word class,
    /// and its meaning as inflected: "chased" means chases.past.!).
    /// </summary>
    private List<Unit> Units(Thought sentence)
    {
        var units = new List<Unit>();
        for (SeqElement? element = sentence.GetTargetOfFirstLinkOfType(hasWords) as SeqElement; element is not null;)
        {
            Thought? phrase = theUKS.GetElementValue(element, patternRoot);
            SeqElement last = element;
            if (phrase is not null)
            {
                while (last.NXT is SeqElement next && ReferenceEquals(theUKS.GetElementValue(next, patternRoot), phrase)) last = next;
                Thought? meaning = element.LinksTo.FirstOrDefault(link => link.LinkType == matchesTemplate && link.To == phrase)?
                    .LinksTo.FirstOrDefault(link => link.LinkType == means && link.To is not Link)?.To;
                Thought phraseClass = phrase.Parents.FirstOrDefault(parent => parent != templateRoot && parent.HasAncestor(patternRoot)) ?? phrase;
                units.Add(new Unit(element, last, phraseClass, meaning, null));
            }
            else
            {
                Thought word = element.VLU;
                Thought wordClass = word.Parents.FirstOrDefault(parent => parent.HasAncestor(wordType)) ?? word;
                units.Add(new Unit(element, last, wordClass,
                    WithInflection(word.GetTargetOfFirstLinkOfType(means), InflectionOf(element)), word));
            }
            element = last.NXT;
        }
        return units;
    }

    // Whether a statement mentions a Thought, as source, type or target, at any depth.
    private static bool Mentions(Thought? pattern, Thought thought) => pattern is Link relationship
        ? Mentions(relationship.From, thought) || Mentions(relationship.LinkType, thought) || Mentions(relationship.To, thought)
        : ReferenceEquals(pattern, thought);

    /// <summary>
    /// Lines the explanation up with the sentence. Each source or target the statements mention must
    /// be the meaning of exactly one stretch, which becomes a role element; a relationship type is
    /// one too if a stretch means it ("chased"), and otherwise is part of what the template says
    /// (is-a.!). A stretch nothing mentions stays a literal word, but only a function word (closed
    /// class): content the explanation leaves out means it does not explain the sentence.
    /// </summary>
    /// <returns>Why the explanation does not fit, or null.</returns>
    private string? Align(List<Unit> units, List<Link> statements, out Dictionary<Unit, string> elements)
    {
        elements = new Dictionary<Unit, string>();
        var found = elements;
        string? Place(Thought? operand, bool isType)
        {
            if (operand is null) return null;
            if (operand is Link nested)
                return Place(nested.From, false) ?? Place(nested.LinkType, true) ?? Place(nested.To, false);
            List<Unit> matching = units.Where(unit => ReferenceEquals(unit.Meaning, operand)).ToList();
            if (matching.Count > 1) return $"I can't tell which \"{Text(matching[0])}\" you mean.";
            if (matching.Count == 0)
                return isType ? null : $"Your explanation mentions {operand.Label}, which isn't in the sentence.";
            if (!found.ContainsKey(matching[0])) found[matching[0]] = RoleElement(matching[0].Class);
            return null;
        }
        foreach (Link statement in statements)
            if (Place(statement, false) is string problem) return problem;
        foreach (Unit unit in units.Where(unit => !found.ContainsKey(unit)))
            if (unit.Word is null || !unit.Class.HasProperty(closedClass))
                return $"Your explanation doesn't mention \"{Text(unit)}\".";
        return null;
    }

    // A new role, and the template element of a class with that role: ??.nounPhrase.learnedRole3.
    private string RoleElement(Thought elementClass)
    {
        Thought role = theUKS.GetOrAddThought("learnedRole*", theUKS.Labeled("templateRole"));
        return $"??.{elementClass.Label}.{role.Label}";
    }

    private string Text(Unit unit)
    {
        var words = new List<string>();
        for (SeqElement? element = unit.First; element is not null; element = element.NXT)
        {
            words.Add(Spelling(element.VLU));
            if (ReferenceEquals(element, unit.Last)) break;
        }
        return string.Join(" ", words);
    }

    /// <summary>
    /// Writes the learned template as text definitions, like those in LoadLanguageKnowledge: its words
    /// (role elements and literal words) and one MEANS per statement, the sentence's Thoughts replaced
    /// by their elements. Statements made together in one sentence are related (probably at the same
    /// time), so each is conjoinedWith the next. The template is marked learned, and captured weak.
    /// </summary>
    private List<string> DefineLearnedTemplate(List<Unit> units, Dictionary<Unit, string> elements, List<Link> statements, out Thought template)
    {
        template = theUKS.GetOrAddThought("learnedTemplate*", templateRoot);
        string name = template.Label;
        string Operand(Thought? operand)
        {
            if (operand is Link nested) return Pattern(nested);
            Unit? unit = units.FirstOrDefault(candidate => elements.ContainsKey(candidate) && ReferenceEquals(candidate.Meaning, operand));
            return unit is not null ? elements[unit] : operand?.Label ?? "";
        }
        string Pattern(Link statement) => $"[{Operand(statement.From)}->{Operand(statement.LinkType)}->{Operand(statement.To)}]";

        var definition = new List<string>
        {
            $"[{name}->hasWords->^" + string.Join(" ", units.Select(unit => elements.TryGetValue(unit, out string? element) ? element : unit.Word!.Label)) + "]",
        };
        definition.AddRange(statements.Select(statement => $"[{name}->means->{Pattern(statement)}]"));
        definition.AddRange(statements.Zip(statements.Skip(1)).Select(pair =>
            $"[{name}->means->[{Pattern(pair.First)}->conjoinedWith.!->{Pattern(pair.Second)}]]"));
        foreach (string line in definition) Define(line);

        template.AddLink(theUKS.GetOrAddThought("hasProperty", "LinkType"), theUKS.GetOrAddThought("isLearned", "Property"));
        Capture(template);
        return definition;
    }
}

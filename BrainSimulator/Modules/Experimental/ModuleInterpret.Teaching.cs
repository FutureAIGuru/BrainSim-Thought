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
    private bool TryToLearnFromExplanation(string unexplained, string explanation, bool rememberUnknownWords, out string report,
        bool asRelationships = false)
    {
        report = "";
        // The same words again, only punctuated differently, are a correction of the input, not an
        // explanation of it ("the cat chased fido the cat chased rover", then with a period).
        if (WordsOnly(explanation) == WordsOnly(unexplained)) return false;
        bool taken = false;
        string said = "";
        theUKS.TryChange($"{unexplained} (explained as: {explanation})", () =>
        {
            taken = LearnFromExplanation(unexplained, explanation, rememberUnknownWords, asRelationships, out said, out bool keep);
            return keep;
        });
        report = said;
        return taken;
    }

    // The body of TryToLearnFromExplanation; keep is true if something was learned and stated.
    private bool LearnFromExplanation(string unexplained, string explanation, bool rememberUnknownWords, bool asRelationships,
        out string report, out bool keep)
    {
        report = "";
        keep = false;
        try
        {
            // The explanation: one or more statements, each understood, or typed as relationships.
            var statements = new List<Link>();
            if (asRelationships)
            {
                statements = ParseRelationships(explanation, out string? notParsed);
                if (notParsed is not null)
                {
                    pendingSentence = unexplained;
                    Answer = notParsed;
                    report = $"Explaining \"{unexplained}\" as \"{explanation}\"\n\nNot learned\n{notParsed}";
                    return true;
                }
            }
            else foreach (string part in SplitSentences(explanation))
            {
                if (Read(part, rememberUnknownWords) is not Thought covering) return false;
                List<Link> said = covering.LinksTo.Where(link => link.LinkType == means)
                    .Select(link => link.To).OfType<Link>().ToList();
                if (said.Count == 0 || said.Any(statement => statement.LinkType?.HasProperty("isPossible") == true)) return false;
                statements.AddRange(said);
            }
            if (statements.Count == 0) return false;

            // The sentence it explains, read again for its words and phrases. Not for clauses: a clause
            // ("the cat chased fido" in "...and rover") would hide the phrases the explanation names.
            if (ReadSentence(unexplained, rememberUnknownWords, lookForClauses: false) is not Thought sentence) return false;
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
                Covering(reread) is not Thought match || TemplateOf(match) != template)
            {
                Answer = "I still can't read it that way.";
                report = header + "\n\nLearned template (not kept)\n" + string.Join("\n", definition);
                return true;
            }
            List<Link> executed = Execute(GetMeanings(reread));
            RecordSource(executed, template);
            keep = executed.Count > 0;
            Answer = keep ? "Thanks, I understand now." + StillLearningNote : "I still can't read it that way.";
            report = header + "\n\nLearned template\n" + string.Join("\n", definition) +
                $"\n\nWeight {template.Weight:0.00} (plastic); each successful use strengthens it." +
                "\n\nExecuted relationships\n" + string.Join("\n", executed);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // Input written as relationships rather than English: brackets or arrows.
    private static bool IsRelationshipText(string? text) =>
        text is not null && (text.TrimStart().StartsWith('[') || text.Contains("->") || text.Contains('→'));

    /// <summary>
    /// The answer to "Can you say it another way?" given as relationships ([cat->chases.past->fido])
    /// instead of sentences: they are the explanation's statements, taken as they are written.
    /// </summary>
    /// <returns>The report; the reply is in Answer.</returns>
    private string ExplainWithRelationships(string relationships, bool rememberUnknownWords)
    {
        if (pendingSentence is not string unexplained)
            return Answer = "Relationships explain a sentence I didn't understand, and none is waiting.";
        pendingSentence = null;
        if (TryToLearnFromExplanation(unexplained, relationships, rememberUnknownWords, out string report, asRelationships: true))
            return report;
        pendingSentence = unexplained;
        Answer = $"Those relationships don't mention anything in \"{unexplained}\". Can you say it another way?";
        return "Not learned\n" + Answer;
    }

    /// <summary>
    /// Relationships typed as in a UKS text file, [from->type->to] (or →), several in one input, an
    /// operand possibly itself a relationship. Operands name Thoughts by label. A type is taken as
    /// stated (is-a becomes is-a.!, chases.past chases.past.!), as a sentence's meaning would be.
    /// Nothing is written: these are only the statements an explanation makes.
    /// </summary>
    private List<Link> ParseRelationships(string text, out string? problem)
    {
        problem = null;
        string source = text.Replace("→", "->");
        var statements = new List<Link>();
        for (int at = SkipSpaces(source, 0); at < source.Length; at = SkipSpaces(source, at))
        {
            if (source[at] != '[' || ParseOperand(source, ref at, ref problem) is not Link statement)
            {
                problem ??= $"Expected a relationship like [fido->is-a->dog] at \"{source[at..]}\".";
                return new();
            }
            statements.Add(statement);
        }
        if (statements.Count == 0) problem = "Enter relationships like [fido->is-a->dog].";
        return statements;
    }

    // One operand at a position in the text: a nested [from->type->to], or the label of a known Thought.
    private Thought? ParseOperand(string source, ref int at, ref string? problem)
    {
        at = SkipSpaces(source, at);
        if (at < source.Length && source[at] == '[')
        {
            at++;
            Thought? from = ParseOperand(source, ref at, ref problem);
            string? typeLabel = Expect(source, ref at, "->", ref problem) ? ReadLabel(source, ref at) : null;
            Thought? to = typeLabel is not null && Expect(source, ref at, "->", ref problem) ? ParseOperand(source, ref at, ref problem) : null;
            if (from is null || string.IsNullOrEmpty(typeLabel) || to is null || !Expect(source, ref at, "]", ref problem)) return null;
            Thought type = typeLabel.EndsWith(".!") || typeLabel.EndsWith(".?")
                ? theUKS.GetOrAddThought(typeLabel) : theUKS.GetOrAddThought(typeLabel + ".!");
            return new Link(from, type, to);
        }
        // A Thought not known yet ("rover", whose word the sentence would teach) is made; if it is not
        // in the sentence, lining up says so, and the transaction is undone.
        string label = ReadLabel(source, ref at);
        return label.Length == 0 ? null : theUKS.Labeled(label) ?? theUKS.GetOrAddThought(label);
    }

    private static string ReadLabel(string source, ref int at)
    {
        int start = at;
        while (at < source.Length && source[at] != '[' && source[at] != ']' &&
            !source.AsSpan(at).StartsWith("->")) at++;
        return source[start..at].Trim();
    }

    private static bool Expect(string source, ref int at, string token, ref string? problem)
    {
        at = SkipSpaces(source, at);
        if (source.AsSpan(at).StartsWith(token)) { at += token.Length; return true; }
        problem ??= $"Expected \"{token}\" at \"{source[at..]}\".";
        return false;
    }

    private static int SkipSpaces(string source, int at)
    {
        while (at < source.Length && char.IsWhiteSpace(source[at])) at++;
        return at;
    }

    // Sentences end with a period, question mark or exclamation mark.
    private static IEnumerable<string> SplitSentences(string text) =>
        Regex.Split(text ?? "", @"(?<=[.!?])\s+").Select(part => part.Trim()).Where(part => part.Length > 0);

    private static string WordsOnly(string text) =>
        string.Join(" ", Regex.Split((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}']+").Where(word => word.Length > 0));

    // Reads a sentence (without executing it); the match of the template covering it, or null.
    private Thought? Read(string text, bool rememberUnknownWords) =>
        ReadSentence(text, rememberUnknownWords) is Thought sentence ? Covering(sentence) : null;

    private Thought? ReadSentence(string text, bool rememberUnknownWords, bool lookForClauses = true)
    {
        Thought? sentence = GetSequenceFromText(text, rememberUnknownWords);
        if (sentence is null) return null;
        GetTemplates(sentence, lookForClauses);
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
                Thought? meaning = MatchesAt(element).FirstOrDefault(match => TemplateOf(match) == phrase && ReferenceEquals(EndOf(match), last))?
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

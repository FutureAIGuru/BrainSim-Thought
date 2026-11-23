#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

// ModuleInterpret is split by stage: this file holds the vocabulary fields, Input, the text
// boundary and Execute; ModuleInterpret.Knowledge.cs the template definitions;
// ModuleInterpret.Recognition.cs GetTemplates and GetMeanings; ModuleInterpret.Generation.cs GenerateSentence.
public sealed partial class ModuleInterpret : ModuleBase
{
    #region Initialization

    private Thought? hasWords;
    private Thought? templateRoot;
    private Thought? phraseRoot;
    private Thought? patternRoot;
    private Thought? vlu;
    private Thought? means;
    private Thought? templateSequenceSearch;
    private Thought? matchesTemplate;
    private Thought? isWildcard;
    private Thought? wordType;
    private Thought? unknownWordType;
    private Thought? wordRoot;
    private Thought? fills;
    private Thought? hasInflection;
    private Thought? marksInflection;
    private Thought? inflectedAs;
    private Thought? inflectionRoot;
    private Thought? addsMeaning;
    private Thought? asksQuestion;
    private Thought? article;
    private Thought? noun;
    private Thought? plural;
    private Thought? singular;
    private Thought? agreesInNumber;
    private Thought? setRelationship;
    private Thought? precedesWord;
    private Thought? precedesWordStartingWith;
    private Thought? followsWord;
    private Thought? closedClass;
    private Thought? isA;
    private Thought? spelled;

    public override void Fire() => Init();

    public override void Initialize() => LoadLanguageKnowledge();

    public override void UKSInitializedNotification() => LoadLanguageKnowledge();

    #endregion

    /// <summary>
    /// The reply to the last Input: the English answer to a query ("I don't know." when nothing
    /// matched), "OK." when a statement was executed, or why the sentence could not be used.
    /// </summary>
    public string Answer { get; private set; } = "";

    // The last sentence that was not understood, while the system waits for it to be explained.
    private string? pendingSentence;

    /// <summary>
    /// Undoes the most recent change to the UKS (a statement here, or an edit in another dialog):
    /// what it stated, the words it taught, and any history it recorded. Earlier ones in turn.
    /// </summary>
    /// <returns>The reply, also in Answer.</returns>
    public string UndoLastInput()
    {
        return Answer = theUKS.UndoLast() is string undone ? $"Undone: {undone}" : "Nothing to undo.";
    }

    /// <summary>
    /// Interprets one input. After a sentence was not understood (and the system asked to have it said
    /// another way), the input is taken as that explanation unless it says otherwise: a question, an
    /// input that is not understood itself, or one sharing nothing with the sentence is read as new.
    /// After "Did you mean...?", a lone yes or no is the answer, and the sentence is read again.
    /// </summary>
    /// <returns>A diagnostic report of each stage. The reply itself is in Answer.</returns>
    public string Input(string sentence, bool rememberUnknownWords = false)
    {
        corrections.Clear();
        if (TryToAnswerCorrection(sentence, rememberUnknownWords, out string answered)) return answered;
        string report = ReadInput(sentence, rememberUnknownWords);
        Answer += CorrectionNote();
        return report;
    }

    private string ReadInput(string sentence, bool rememberUnknownWords)
    {
        string? unexplained = pendingSentence;
        pendingSentence = null;
        if (unexplained is not null && TryToLearnFromExplanation(unexplained, sentence, rememberUnknownWords, out string report))
            return report;
        // Several sentences are read one after another, each as its own input.
        List<string> sentences = SplitSentences(sentence).ToList();
        if (sentences.Count <= 1) return Interpret(sentence, rememberUnknownWords);
        var reports = new List<string>();
        var answers = new List<string>();
        foreach (string part in sentences)
        {
            reports.Add($"=== {part} ===\n" + Interpret(part, rememberUnknownWords));
            answers.Add(Answer);
        }
        Answer = string.Join(" ", answers);
        return string.Join("\n\n", reports);
    }

    /// <summary>
    /// Interprets one sentence as one UKS transaction. A sentence that is not understood (no template
    /// covers it from its first word to its last) changes nothing: what reading it recorded, and any
    /// word it would have taught, is undone; the system asks for it to be said another way. A question
    /// changes nothing either. A statement that stored something can be undone later (UndoLastInput).
    /// </summary>
    private string Interpret(string sentence, bool rememberUnknownWords)
    {
        UKSTransaction transaction = theUKS.BeginTransaction();
        bool keep = false;
        try
        {
            Thought? sentenceThought = GetSequenceFromText(sentence, rememberUnknownWords);
            if (sentenceThought is null) return Answer = "Enter a sentence.";

            List<Thought> templates = GetTemplates(sentenceThought);
            List<Thought> meanings = GetMeanings(sentenceThought);
            // Readings of the whole sentence compete: only the strongest template's reading is acted on.
            List<Link> readings = CoveringMatches(sentenceThought);
            Link? covering = readings.FirstOrDefault();
            bool understood = covering is not null;
            if (covering is not null)
            {
                HashSet<Thought> losing = LosingMeanings(readings, covering);
                meanings = meanings.Where(meaning => !losing.Contains(meaning)).ToList();
            }
            bool isQuery = meanings.OfType<Link>().Any(action => action.LinkType?.HasProperty("isPossible") == true);
            // A question asks; it does not state what its noun phrases describe ("the cat in the kitchen").
            List<Link> executed = !understood ? new List<Link>() : Execute(isQuery
                ? meanings.Where(meaning => meaning is Link action && action.LinkType?.HasProperty("isPossible") == true).ToList()
                : meanings);
            // An input that is only a noun phrase ("The hungry dog") introduces, or finds, what it describes.
            if (understood && !isQuery) executed.AddRange(Introduced(covering!));
            keep = understood && !isQuery && executed.Count > 0;
            string words = "Sentence input: "+ sentenceThought.GetTargetOfFirstLinkOfType("hasWords");
            string templateText = templates.Count == 0
                ? "No matching templates."
                : "Templates\n" + string.Join("\n", templates.Select(
                    template => template.Label + "    " + template.GetTargetOfFirstLinkOfType("hasWords").ToString()));
            SeqElement sequence = (SeqElement)sentenceThought.GetTargetOfFirstLinkOfType("hasWords");
            string attachments = string.Join("\n", theUKS.EnumerateSequenceElements(sequence).Select(element =>
                element.VLU.Label + " (" + string.Join(", ", WordProperties(element)) + ")" +
                "    " + string.Join(", ", element.LinksTo
                    .Where(link => link.LinkType == matchesTemplate).Select(link => link.To.Label))));
            string meaningText = string.Join("\n", meanings.Select(meaning => meaning.ToString()));
            string output = words + "\n\n" + templateText + "\n\nWord attachments\n" + attachments +
                "\n\nMeanings\n" + meaningText;
            if (readings.Count > 1)
                output += "\n\nCompeting readings (the strongest is acted on)\n" +
                    string.Join("\n", readings.Select(reading => $"{reading.To.Label}  weight {reading.To.Weight:0.00}"));
            if (!understood)
            {
                pendingSentence = sentence;
                Answer = NotUnderstood;
                return output + "\n\nNot understood\nNo template covers the whole sentence, so nothing was stored or learned." +
                    "\nThe next input is taken as saying it another way, unless it is a question or unrelated.";
            }
            // The winning template succeeded if its statement stored something or its question found an
            // answer: it is strengthened, and records what it stated.
            string learning = "";
            if (executed.Count > 0 && covering!.To is Thought winner)
            {
                bool wasPlastic = winner.isPlastic;
                float before = winner.Weight;
                if (!isQuery) RecordSource(executed, winner);
                Strengthen(winner);
                if (wasPlastic && !isQuery) learning = " (I'm still learning sentences like this one.)";
                if (before < 1)
                    output += $"\n\nTemplate {winner.Label}: weight {before:0.00}, now {winner.Weight:0.00}" +
                        (wasPlastic && !winner.isPlastic ? " (consolidated)" : winner.isPlastic ? " (plastic)" : "");
            }
            if (executed.Count > 0 && !isQuery)
                output += "\n\nExecuted relationships\n" + string.Join("\n", executed);
            if (isQuery)
            {
                output += "\n\nQuery results\n" + (executed.Count == 0
                    ? "No matching relationships." : string.Join("\n", executed));
                // No match is not proof of falsity, so an empty result is not answered "no".
                Answer = executed.Count == 0 ? "I don't know." : string.Join("\n",
                    executed.Select(result => GenerateSentence(result, sentenceThought) ?? result.ToString()).Distinct());
                output += "\n\nAnswer\n" + Answer;
            }
            else Answer = executed.Count > 0 ? "OK." + learning : "I don't understand.";
            return output;
        }
        catch (UncertainSpellingException question)
        {
            // Nothing is read or learned until the spelling is settled.
            pendingCorrection = (sentence, question.Typed, question.Suggestion);
            Answer = $"Did you mean \"{Spelling(question.Suggestion)}\"?";
            return $"\"{question.Typed}\" is not a known word, but is close to \"{Spelling(question.Suggestion)}\"." +
                "\nAnswer yes to read it that way, or no to take it as a new word.";
        }
        catch (FormatException error)
        {
            return Answer = error.Message;
        }
        finally
        {
            theUKS.EndTransaction(transaction);
            if (keep) theUKS.RememberForUndo(sentence, transaction);
            else theUKS.Undo(transaction);
        }
    }

    /// <summary>
    /// A sentence is understood when one recognized template covers it, from its first word to its
    /// last, and that template has a meaning.
    /// </summary>
    /// <returns>The winning covering template's match (its MEANS results attached), or null.</returns>
    private Link? Covering(Thought sentenceThought) => CoveringMatches(sentenceThought).FirstOrDefault();

    // Every reading of the whole sentence: the matches of templates with a meaning that cover it from
    // its first word to its last, strongest template first (they compete by weight).
    private List<Link> CoveringMatches(Thought sentenceThought)
    {
        var covering = new List<Link>();
        if (sentenceThought.GetTargetOfFirstLinkOfType(hasWords) is not SeqElement first) return covering;
        foreach (Link match in first.LinksTo.Where(link => link.LinkType == matchesTemplate))
        {
            if (!match.LinksTo.Any(link => link.LinkType == means)) continue;
            bool covers = true;
            for (SeqElement? element = first; element is not null && covers; element = element.NXT)
                covers = element.LinksTo.Any(link => link.LinkType == matchesTemplate && link.To == match.To);
            if (covers) covering.Add(match);
        }
        return covering.OrderByDescending(match => match.To.Weight).ToList();
    }

    #region GetSequenceFromText

    // Each successful input is retained under a new SentenceIn Thought for inspection.
    public Thought? GetSequenceFromText(string sentence, bool rememberUnknownWords = false)
    {
        var words = (sentence ?? "").Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim('.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}'))
            .Where(token => token.Length > 0)
            .Select(token => token.ToLowerInvariant());
        var wordThoughts = new List<Thought>();
        var inflections = new List<Thought?>(); // The inflection each word was read with, in sentence order.
        var unknown = new List<string>();

        foreach (string spelling in words)
        {
            // No exact match: perhaps a misspelling of a known word ("fdio").
            Thought? word = ModuleWord.FindWordsBySpelling(theUKS, spelling).FirstOrDefault() ?? CorrectedSpelling(spelling);
            // A stored inflected form is read as its base word, marked with the inflection it adds:
            // meanings belong to the singular, present-tense base word.
            Thought? inflection = null;
            if (word is not null && BaseWordOf(word, out inflection) is Thought baseWord) word = baseWord;
            // Remember the spelling now. Grammatical classification is a separate action.
            if (word is null && rememberUnknownWords) word = ModuleWord.RememberSpelling(theUKS, spelling, out _);

            if (word is not null && !word.Parents.Any(parent => parent != wordType &&
                parent != unknownWordType && parent.HasAncestor(wordType)))
            {
                word.AddParent(unknownWordType!);
                word.RemoveParent(wordRoot!);
                word.RemoveParent(wordType!);
            }

            if (word is null) unknown.Add(spelling);
            else
            {
                // Resolve spelling to a named concept at the text boundary. Recognition
                // below works entirely with Thoughts, including a newly heard word.1
                if (word.GetTargetOfFirstLinkOfType(means) is null)
                    word.AddLink(means!, theUKS.GetOrAddThought(spelling));
                wordThoughts.Add(word);
                inflections.Add(inflection);
            }
        }

        if (unknown.Count > 0) throw new FormatException($"Unknown words: {string.Join(", ", unknown.Distinct())}.");
        if (wordThoughts.Count == 0) return null;

        theUKS.GetOrAddThought("Sentence", "LanguageElement");
        Thought sentenceThought = theUKS.GetOrAddThought("SentenceIn*", "Sentence");
        SeqElement sequence = theUKS.AddSequenceAndLink(sentenceThought, hasWords, wordThoughts, allowCompression: false);
        // The UKS stores each word sequence once, so an identical sequence is shared with earlier input,
        // including input with other inflections ("Was Fido black?" and "Is Fido black?" hold the same
        // base words). Each reading starts afresh: clear what an earlier reading recorded on it.
        for (SeqElement? shared = sequence; shared is not null; shared = shared.NXT)
            foreach (Link reading in shared.LinksTo.Where(link => link.LinkType == inflectedAs || link.LinkType == matchesTemplate ||
                link.LinkType == fills || link.LinkType == vlu && link.To?.HasAncestor(patternRoot) == true).ToList())
                shared.RemoveLink(reading);
        // Mark each sentence element with its inflection, walking the elements and the words together.
        SeqElement? element = sequence;
        foreach (Thought? inflection in inflections)
        {
            if (inflection is not null) element!.AddLink(inflectedAs!, inflection);
            element = element!.NXT;
        }

        return sentenceThought;
    }

    #endregion

    #region Inflected forms

    /// <summary>
    /// The base word an inflected form is linked from (w:dog for w:dogs), and the inflection that link
    /// type marks ([hasPlural->marksInflection->plural]). Null for a word that is not a stored form.
    /// </summary>
    private Thought? BaseWordOf(Thought form, out Thought? inflection)
    {
        Link? link = form.LinksFrom.FirstOrDefault(link => link.LinkType?.HasAncestor(hasInflection) == true);
        inflection = link?.LinkType.GetTargetOfFirstLinkOfType(marksInflection);
        return link?.From;
    }

    /// <summary>
    /// The stored forms of a base word with the given inflection (w:chasing for w:chases and the
    /// present participle). With no inflection given, the forms whose inflection adds no meaning
    /// (w:dogs for w:dog): those express the base word's own meaning.
    /// </summary>
    private IEnumerable<Thought> FormsOf(Thought word, Thought? inflection = null) => word.LinksTo
        .Where(link => link.LinkType?.HasAncestor(hasInflection) == true && link.To is not null)
        .Where(link => link.LinkType.GetTargetOfFirstLinkOfType(marksInflection) is Thought marked &&
            (inflection is null ? AddedMeaning(marked) is null : ReferenceEquals(marked, inflection)))
        .Select(link => link.To);

    /// <summary>
    /// For the report: what a sentence word is known to be. Its word classes (more than one when the
    /// word is ambiguous), the properties it has, and the inflection it was read with.
    /// </summary>
    private IEnumerable<string> WordProperties(SeqElement element)
    {
        Thought word = element.VLU;
        foreach (Thought wordClass in word.Parents) yield return wordClass.Label;
        foreach (Link property in word.LinksTo.Where(link => link.LinkType?.Label == "hasProperty" && link.To is not null))
            yield return property.To.Label;
        if (InflectionOf(element) is Thought inflection) yield return inflection.Label;
    }

    // The inflection a sentence element was read with, if any.
    private Thought? InflectionOf(SeqElement element) => element.GetTargetOfFirstLinkOfType(inflectedAs);

    // The meaning an inflection adds to its base word's meaning (past, ongoing), if any.
    private Thought? AddedMeaning(Thought? inflection) => inflection?.GetTargetOfFirstLinkOfType(addsMeaning);

    // The inflection a template element requires (??.actionVerb.presentParticiple), if any.
    private Thought? RequiredInflection(Thought templateValue) =>
        templateValue.Parents.FirstOrDefault(parent => parent.HasAncestor(inflectionRoot));

    // A sentence word carries the inflection its template element requires; an element requiring
    // none accepts any ("ate" fills ??.actionVerb, and means eats.past.!).
    private bool HasRequiredInflection(SeqElement input, Thought templateValue) =>
        RequiredInflection(templateValue) is not Thought required || ReferenceEquals(InflectionOf(input), required);

    // The classes constraining a template element: its parents other than wildcards, roles and inflections.
    private IEnumerable<Thought> ElementClasses(Thought element) =>
        element.Parents.Where(parent => !parent.HasProperty(isWildcard) && !parent.HasAncestor(inflectionRoot));

    /// <summary>
    /// A base word's meaning as modified by an inflection, composed as a dotted label like every
    /// modified relationship type: chases.! read in the past tense is chases.past.!. A meaning is
    /// returned unchanged by an inflection that adds none (the plural).
    /// </summary>
    private Thought? WithInflection(Thought? meaning, Thought? inflection)
    {
        if (meaning is null || AddedMeaning(inflection) is not Thought added) return meaning;
        if (meaning.HasProperty(setRelationship) && theUKS.GetActionRelationshipType(meaning) is Thought type)
            return theUKS.GetOrAddThought($"{type.Label}.{added.Label}.!");
        return theUKS.GetOrAddThought($"{meaning.Label}.{added.Label}");
    }

    #endregion

    /// <summary>
    /// Applies each Link meaning: .! writes, .? queries. A relationship nested as an operand (an event
    /// placed somewhere) is resolved first: stated when the meaning is a statement, looked up when it
    /// is a question. A present-tense question does not ask about history, so historical results
    /// (was.located) are left out unless the question itself asks about the past.
    /// </summary>
    public List<Link> Execute(List<Thought> meanings)
    {
        var results = new List<Link>();
        foreach (Thought meaning in meanings)
        {
            // A described referent ("the black dog") is resolved where it is used, not on its own.
            if (meaning is not Link action || action.LinkType is null || IsReferentDescriptor(action)) continue;
            bool question = action.LinkType.HasProperty("isPossible");
            // "Rover is a small dog": joining a described kind is joining its class with its description.
            if (IsJoiningAKind(action))
            {
                if (Resolved(action.From, question) is Thought member) results.AddRange(JoinKind(member, (Link)action.To));
                continue;
            }
            Thought? from = Resolved(action.From, question), to = Resolved(action.To, question);
            if (from is null || to is null) continue;
            Link executed = ReferenceEquals(from, action.From) && ReferenceEquals(to, action.To) ? action : new Link(from, action.LinkType, to);
            IEnumerable<Link> applied = theUKS.ApplyTestOrSetAction(executed);
            if (question && !action.LinkType.HasProperty("isHistorical"))
                applied = applied.Where(result => result.LinkType?.HasProperty("isHistorical") != true);
            results.AddRange(applied);
        }
        return results;
    }

    // An operand as executed: a described referent is found (or made); a nested write is stated (or,
    // in a question, the stored relationship it describes is looked up); anything else is used as it
    // is. Null when a looked-up one is not known.
    private Thought? Resolved(Thought? operand, bool question)
    {
        if (IsReferentDescriptor(operand)) return Referent((Link)operand!, question);
        if (operand is not Link written || written.LinkType?.HasProperty(setRelationship) != true) return operand;
        Thought? from = Resolved(written.From, question), to = Resolved(written.To, question);
        if (from is null || to is null) return null;
        Link nested = ReferenceEquals(from, written.From) && ReferenceEquals(to, written.To) ? written : new Link(from, written.LinkType, to);
        if (question)
            return theUKS.GetActionRelationshipType(nested.LinkType) is Thought type ? theUKS.GetLink(nested.From, type, nested.To) : null;
        return theUKS.ApplyTestOrSetAction(nested).FirstOrDefault();
    }
}

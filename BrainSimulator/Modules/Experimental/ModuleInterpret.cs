#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

// ModuleInterpret is split by stage: this file holds the vocabulary fields, Input and Interpret (the
// pipeline, stage by stage), the text boundary and Execute; ModuleInterpret.Knowledge.cs the template
// definitions; .Recognition.cs GetTemplates and GetMeanings; .Generation.cs GenerateSentence;
// .Reference.cs described noun phrases; .Teaching.cs, .Learning.cs, .Spelling.cs; .Report.cs the
// diagnostic report text.
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
    private Thought? joinsClauses;
    private Thought? clause;
    private Thought? matchRoot;
    private Thought? matchOf;
    private Thought? startsMatch;
    private Thought? endsAt;
    private Thought? inMatch;
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
    /// An input written as relationships ([fido->is-a->dog]) explains the sentence that was not
    /// understood, in place of sentences that say it another way.
    /// </summary>
    /// <returns>A diagnostic report of each stage. The reply itself is in Answer.</returns>
    public string Input(string sentence, bool rememberUnknownWords = false)
    {
        corrections.Clear();
        if (IsRelationshipText(sentence)) return ExplainWithRelationships(sentence, rememberUnknownWords);
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
            // 1. Words: the sentence as a sequence of base words (inflections marked on its elements).
            Thought? sentenceThought = GetSequenceFromText(sentence, rememberUnknownWords);
            if (sentenceThought is null) return Answer = "Enter a sentence.";

            // 2. Recognition: the templates the words match, and what each match means.
            List<Thought> templates = GetTemplates(sentenceThought);
            List<Thought> meanings = GetMeanings(sentenceThought);

            // 3. Competition: readings of the whole sentence compete; only the strongest is acted on.
            List<Thought> readings = CoveringMatches(sentenceThought);
            Thought? winner = readings.FirstOrDefault();
            if (winner is not null) meanings = meanings.Except(LosingMeanings(readings, winner)).ToList();
            string report = RecognitionReport(sentenceThought, templates, meanings, readings);
            if (winner is null)
            {
                pendingSentence = sentence;  // the next input may explain it (Teaching)
                Answer = NotUnderstood;
                return report + NotUnderstoodReport;
            }

            // 4. Action on the winner's own meanings; its parts reach the UKS only through them (the
            //    clauses of "A and B" are stated as parts of the conjunction). A question is asked (its
            //    noun phrases describe, they do not state); a statement is stored, and a sentence that
            //    is only a noun phrase introduces its referent.
            List<Thought> said = WithAntecedents(MeaningsOf(winner), out Thought? antecedent);
            if (antecedent is not null) report += PronounReport(antecedent);
            bool isQuery = said.Any(IsQuery);
            evidence.Clear();
            assumption = null;
            List<Link> executed = isQuery
                ? Execute(said.Where(IsQuery).ToList())
                : [.. Execute(said), .. Introduced(winner)];
            keep = !isQuery && executed.Count > 0;

            // 5. Learning: a winning template whose reading succeeded is strengthened.
            bool stillLearning = false;
            if (executed.Count > 0) report += Reinforce(TemplateOf(winner), executed, isQuery, out stillLearning);

            // 6. Reply.
            if (isQuery)
            {
                // No match is not proof of falsity, so an empty result is not answered "no". Rules
                // that bear on the question explain the answer (Explanation).
                Answer = executed.Count == 0 ? ("I don't know. " + WhyNotKnown()).TrimEnd() : string.Join("\n",
                    executed.Select(result => Answered(result, sentenceThought)).Distinct());
                return report + QueryReport(executed, Answer);
            }
            Answer = executed.Count == 0 ? "I don't understand." : "OK." + (stillLearning ? StillLearningNote : "");
            return report + StatementReport(executed);
        }
        catch (UncertainSpellingException question)
        {
            // Nothing is read or learned until the spelling is settled.
            pendingCorrection = (sentence, question.Typed, question.Suggestion);
            Answer = $"Did you mean \"{Spelling(question.Suggestion)}\"?";
            return UncertainSpellingReport(question);
        }
        catch (FormatException error)
        {
            return Answer = error.Message;  // unknown words
        }
        finally
        {
            // Keep only a statement that stored something; everything else leaves the UKS unchanged.
            theUKS.EndTransaction(transaction);
            if (keep) theUKS.RememberForUndo(sentence, transaction);
            else theUKS.Undo(transaction);
        }
    }

    // The meanings bound to one template match (its MEANS results).
    private List<Thought> MeaningsOf(Thought match) =>
        match.LinksTo.Where(link => link.LinkType == means).Select(link => link.To).ToList();

    // A meaning that asks rather than states (is.?).
    private static bool IsQuery(Thought meaning) => meaning is Link action && action.LinkType?.HasProperty("isPossible") == true;

    /// <summary>
    /// The winning template's reading succeeded (a statement stored something, a question found an
    /// answer): the template is strengthened, and records the facts it stated (provenance).
    /// </summary>
    /// <returns>A report line when the template is still below full strength.</returns>
    private string Reinforce(Thought winner, List<Link> executed, bool isQuery, out bool stillLearning)
    {
        bool wasPlastic = winner.isPlastic;
        float before = winner.Weight;
        if (!isQuery) RecordSource(executed, winner);
        Strengthen(winner);
        stillLearning = wasPlastic && !isQuery;
        return before < 1 ? WeightReport(winner, before, wasPlastic) : "";
    }

    /// <summary>
    /// A sentence is understood when one recognized template covers it, from its first word to its
    /// last, and that template has a meaning.
    /// </summary>
    /// <returns>The winning covering match (its MEANS results attached), or null.</returns>
    private Thought? Covering(Thought sentenceThought) => CoveringMatches(sentenceThought).FirstOrDefault();

    // Every reading of the whole sentence: the matches with a meaning that start at its first word and
    // end at its last, strongest template first (they compete by weight).
    private List<Thought> CoveringMatches(Thought sentenceThought)
    {
        if (sentenceThought.GetTargetOfFirstLinkOfType(hasWords) is not SeqElement first) return new();
        SeqElement end = first;
        while (end.NXT is SeqElement next) end = next;
        return MatchesAt(first)
            .Where(match => ReferenceEquals(EndOf(match), end) && match.LinksTo.Any(link => link.LinkType == means))
            .OrderByDescending(match => TemplateOf(match).Weight).ToList();
    }

    #region GetSequenceFromText

    // Each successful input is retained under a new SentenceIn Thought for inspection.
    public Thought? GetSequenceFromText(string sentence, bool rememberUnknownWords = false)
    {
        IEnumerable<string> words = Tokens(sentence);
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
                link.LinkType == startsMatch ||
                link.LinkType == fills || link.LinkType == vlu && link.To?.HasAncestor(patternRoot) == true).ToList())
            {
                shared.RemoveLink(reading);
                if (reading.LinkType == startsMatch) reading.To?.Delete();  // the earlier reading's match
            }
        // Mark each sentence element with its inflection, walking the elements and the words together.
        SeqElement? element = sequence;
        foreach (Thought? inflection in inflections)
        {
            if (inflection is not null) element!.AddLink(inflectedAs!, inflection);
            element = element!.NXT;
        }

        return sentenceThought;
    }

    // A sentence's words, lowercased and without punctuation, except that a comma is a word of its own:
    // it can join clauses ("If Fido is outside, Fido is cold").
    private static IEnumerable<string> Tokens(string? sentence)
    {
        foreach (string token in (sentence ?? "").Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string word = token.Trim('.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}').ToLowerInvariant();
            if (word.Length > 0) yield return word;
            if (token.TrimEnd('"', '\'', ')', ']', '}').EndsWith(',')) yield return ",";
        }
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
            // "Fido is cold if Fido is outside": a rule, stored and not stated (Rules).
            if (!question && IsRule(action))
            {
                results.AddRange(WriteRules(action));
                continue;
            }
            // "Is Fido cold if Fido is outside?": the condition is stated, then the question asked. A
            // question's changes are undone (Interpret), so the assumption does not stay.
            if (question && IsAssuming(action) && action.From is Link asked)
            {
                Resolved(action.To, question: false);
                assumption = action.To as Link;
                results.AddRange(Execute([asked]));
                continue;
            }
            Thought? from = Resolved(action.From, question), to = Resolved(action.To, question);
            if (from is null || to is null) continue;
            // A statement nested as an operand (a clause of "A and B", an event placed somewhere) was
            // stated by resolving it: it is among what this sentence stored.
            if (!question)
                results.AddRange(new[] { (action.From, from), (action.To, to) }
                    .Where(pair => IsNestedWrite(pair.Item1) && pair.Item2 is Link).Select(pair => (Link)pair.Item2));
            Link executed = ReferenceEquals(from, action.From) && ReferenceEquals(to, action.To) ? action : new Link(from, action.LinkType, to);
            IEnumerable<Link> applied = theUKS.ApplyTestOrSetAction(executed).ToList();
            // A yes/no question: keep what the UKS evaluated to answer it, before anything else queries.
            if (question && !IsOpen(executed.From) && !IsOpen(executed.To)) RecordEvidence(executed);
            if (question && !action.LinkType.HasProperty("isHistorical"))
                applied = applied.Where(result => result.LinkType?.HasProperty("isHistorical") != true);
            results.AddRange(applied);
        }
        return results;
    }

    // An operand as executed: a described referent is found (or made); a nested write is stated (or,
    // in a question, the stored relationship it describes is looked up); anything else is used as it
    // is. Null when a looked-up one is not known.
    // A question asked on an assumption (assuming.?).
    private bool IsAssuming(Link action) =>
        ReferenceEquals(theUKS.GetActionRelationshipType(action.LinkType), theUKS.Labeled("assuming"));

    // The condition the question in this sentence assumed ("if Fido is outside"), if any.
    private Link? assumption;

    // A query variable (??), which a yes/no question does not have.
    private bool IsOpen(Thought? operand) => operand?.HasProperty(isWildcard) == true;

    #region Rules

    // A statement that one clause holds on condition of another (conditionalOn.!).
    private bool IsRule(Link action) =>
        ReferenceEquals(theUKS.GetActionRelationshipType(action.LinkType), theUKS.Labeled("conditionalOn"));

    /// <summary>
    /// Stores a rule the way the UKS represents one: its clauses are possibilities, never stated, and
    /// a conjunction inside it is AND: [[fido->is.?->cold]->IF->[[fido->located.?->outside]->AND->...]].
    /// GetAttributes then answers from it when its conditions are met. The UKS evaluates a result that
    /// is one relationship, so "A and B if C" is two rules, A if C and B if C. The UKS text form is
    /// written with ProcessSingleLineWithNesting, which makes the operands of IF and AND into patterns.
    /// </summary>
    /// <returns>The stored IF relationships.</returns>
    private List<Link> WriteRules(Link rule) => RuleText(rule.To) is not string condition ? new() :
        Conjuncts(rule.From).Select(RuleText).OfType<string>()
            .Select(result => theUKS.ProcessSingleLineWithNesting($"[{result}->IF->{condition}]")).OfType<Link>().ToList();

    // The statements a conjunction joins ("A and B": A, B), or the statement itself.
    private IEnumerable<Thought?> Conjuncts(Thought? statement) =>
        statement is Link joined && ReferenceEquals(theUKS.GetActionRelationshipType(joined.LinkType), theUKS.Labeled("conjoinedWith"))
            ? Conjuncts(joined.From).Concat(Conjuncts(joined.To)) : [statement];

    // A rule's part in UKS text: a relationship by its ordinary type (conditionalOn is IF, conjoinedWith
    // AND), a described referent by the thing it picks out, anything else by its label. Null if a
    // referent is unclear.
    private string? RuleText(Thought? part)
    {
        if (IsReferentDescriptor(part)) return Referent((Link)part!, question: false)?.Label;
        if (part is not Link relationship) return part?.Label;
        Thought? type = theUKS.GetActionRelationshipType(relationship.LinkType) ?? relationship.LinkType;
        string? typeText = ReferenceEquals(type, theUKS.Labeled("conditionalOn")) ? "IF"
            : ReferenceEquals(type, theUKS.Labeled("conjoinedWith")) ? "AND" : type?.Label;
        string? from = RuleText(relationship.From), to = RuleText(relationship.To);
        return from is null || typeText is null || to is null ? null : $"[{from}->{typeText}->{to}]";
    }

    #endregion

    private bool IsNestedWrite(Thought? operand) =>
        operand is Link written && written.LinkType?.HasProperty(setRelationship) == true && !IsReferentDescriptor(operand);

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

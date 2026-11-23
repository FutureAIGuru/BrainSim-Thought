#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

// Recognition: GetTemplates finds the templates a sentence matches; GetMeanings binds their MEANS.
public sealed partial class ModuleInterpret
{
    /// <summary>
    /// Search from the sentence itself. Words retain their original VLU links; recognized
    /// phrases add a languagePattern VLU facet covering the same sentence elements.
    /// Search can therefore use the word or its current recognized phrase without a copied input.
    /// When nothing covers the sentence, sentence templates are also matched as its clauses (between
    /// words that join clauses), unless lookForClauses is false.
    /// </summary>
    public List<Thought> GetTemplates(Thought sentenceThought, bool lookForClauses = true)
    {
        var templates = new List<Thought>(); // Diagnostic summary only, assembled from the UKS below.
        if (patternRoot is null || vlu is null || matchesTemplate is null || templateSequenceSearch is null)
            return templates;
        if (sentenceThought.GetTargetOfFirstLinkOfType(hasWords) is not SeqElement first) return templates;

        bool added;
        bool allowLoneUnknownWords = false;
        // Clauses are looked for only when nothing reads the whole sentence: otherwise "Rover are dogs"
        // would be read inside "Fido and Rover are dogs."
        bool allowClauses = false;
        // A lone unknown word right after a word that joins clauses starts a phrase ("and Rover"), so it
        // is guessed before clauses are looked for; one inside a clause ("is hungry") is guessed after.
        bool allowLoneUnknownWordsAtClauseStarts = false;
        HashSet<SeqElement> clauseStarts = ClauseStarts(first);
        do
        {
            added = false;
            bool deferred = false, deferredAtClauseStart = false;
            HashSet<SeqElement> sentenceStarts = allowClauses ? clauseStarts : [first];
            for (SeqElement? start = first; start is not null;)
            {
                // Keep the unprocessed search result visible in the debugger.
                var sequenceMatches = theUKS.FindSequencesByActivation(start, templateSequenceSearch,
                    alternativeClass: patternRoot, matchClass: patternRoot);
                var matches = new List<Thought>();
                int longest = 0;
                foreach (var sequenceMatch in sequenceMatches)
                {
                    int length = theUKS.GetSequenceLength(sequenceMatch.seqNode);
                    if (longest != 0 && length != longest) break;
                    var referringThoughts = theUKS.GetReferringThoughts(sequenceMatch.seqNode, hasWords!);
                    foreach (Thought template in referringThoughts)
                        // A sentence template (a direct child of template) describes a whole sentence, so
                        // it starts at the first word, or a clause's; phrases can start anywhere. Otherwise
                        // "the kitchen is brown" would be read inside "The dog in the kitchen is brown."
                        if (template.HasAncestor(patternRoot) && !matches.Contains(template) &&
                            Admits(template, start, allowClauses) &&
                            (!template.Parents.Contains(templateRoot) || ReferenceEquals(start, first) ||
                                sentenceStarts.Contains(start) && IsClause(template)))
                        {
                            matches.Add(template);
                            longest = length;
                        }
                }
                // Equally long matches compete by weight: the strongest becomes the phrase facet below.
                matches = ByWeight(matches);
                // A single unknown word on its own is only a guess. If words precede it, a template
                // starting earlier may still cover it, so wait until nothing else can be recognized.
                bool atClauseStart = clauseStarts.Contains(start);
                if (matches.Count > 0 && !allowLoneUnknownWords && !(allowLoneUnknownWordsAtClauseStarts && atClauseStart) &&
                    !ReferenceEquals(start, first) && IsLoneUnknownWord(start, matches))
                {
                    deferred = true;
                    deferredAtClauseStart |= atClauseStart;
                    matches.Clear();
                }
                if (matches.Count == 0)
                {
                    // Skip the complete phrase already recognized here, rather than entering its middle.
                    Thought? phrase = theUKS.GetElementValue(start, patternRoot);
                    while (phrase is not null && start.NXT is SeqElement next &&
                        ReferenceEquals(theUKS.GetElementValue(next, patternRoot), phrase)) start = next;
                    start = start.NXT;
                    continue;
                }

                bool changed = false;
                SeqElement last = start;
                foreach (Thought template in matches)
                {
                    SeqElement? input = start;
                    SeqElement? position = template.GetTargetOfFirstLinkOfType(hasWords) as SeqElement;
                    for (; position is not null && input is not null; position = position.NXT)
                    {
                        Thought? phrase = theUKS.GetElementValue(input, patternRoot);
                        Thought value = phrase ?? input.VLU;
                        last = input;
                        while (phrase is not null && last.NXT is SeqElement next &&
                            ReferenceEquals(theUKS.GetElementValue(next, patternRoot), phrase)) last = next;
                        if (value.HasAncestor(unknownWordType) && position.VLU.HasProperty(isWildcard))
                            foreach (Thought type in position.VLU.Parents.Where(parent => parent.HasAncestor(wordType)))
                                value.AddLink(isA!, type);
                        // Preserve every winning match on every covered original word.
                        for (; input is not null; input = input.NXT)
                        {
                            int count = input.LinksTo.Count;
                            input.AddLink(matchesTemplate, template);
                            changed |= input.LinksTo.Count > count;
                            if (ReferenceEquals(input, last)) { input = input.NXT; break; }
                        }
                    }
                    // And the match itself, with its extent: "(A and B) and C" is a second match of
                    // compoundTemplate at the same first word, ending later.
                    changed |= RecordMatch(start, template, last);
                }
                // Preserve the word facet, and select one tied winner as the current phrase facet.
                for (SeqElement? element = start; element is not null; element = element.NXT)
                {
                    Thought word = element.VLU;
                    if (word.Parents.Any(parent => parent != unknownWordType && parent.HasAncestor(wordType)))
                    {
                        word.RemoveParent(unknownWordType!);
                        word.RemoveParent(wordRoot!);
                    }
                    if (!ReferenceEquals(theUKS.GetElementValue(element, patternRoot), matches[0]))
                    {
                        foreach (Link link in element.LinksTo.Where(link => link.LinkType == vlu && link.To.HasAncestor(patternRoot)))
                            element.RemoveLink(link);
                        element.AddLink(vlu, matches[0]);
                        changed = true;
                    }
                    if (ReferenceEquals(element, last)) break;
                }
                added |= changed;
                // Retry here immediately: the recognized subject may enable a whole sentence.
                if (!changed) start = last.NXT;
            }
            // Once nothing else can be recognized: guess a waiting lone unknown word that starts a phrase
            // after a joining word; then, if nothing covers the sentence, look for its clauses; then guess
            // the remaining lone unknown words. Clauses before those: within its clause, "hungry" in
            // "Fido is green and Fido is hungry" is read as it is alone (an adjective), not as a noun.
            if (added) allowLoneUnknownWords = false;
            else if (deferredAtClauseStart && !allowLoneUnknownWordsAtClauseStarts)
                allowLoneUnknownWordsAtClauseStarts = added = true;
            else if (lookForClauses && !allowClauses && !IsCovered(first) && clauseStarts.Count > 1)
                allowClauses = added = true;
            else if (deferred && !allowLoneUnknownWords) allowLoneUnknownWords = added = true;
        } while (added);
        for (SeqElement? element = first; element is not null; element = element.NXT)
            foreach (Link link in element.LinksTo)
                if (link.LinkType == matchesTemplate && !templates.Contains(link.To)) templates.Add(link.To);
        return templates;
    }

    /// <summary>
    /// A template may be matched only if no unknown word would be learned into a closed class
    /// (articles, auxiliaries, ...): those are fixed sets of function words. And a word must carry
    /// the inflection its element requires ("is chasing", not "is chases"); sequence search compares
    /// only classes. A word whose inflection adds meaning (past, ongoing) must fill an element whose
    /// meaning the template uses. A sentence template must reach the end of the sentence (or, while
    /// clauses are looked for, a word that joins clauses). Walks the template and the sentence
    /// together as the classification step does.
    /// </summary>
    private bool Admits(Thought template, SeqElement start, bool allowClauses)
    {
        SeqElement? input = start;
        SeqElement? position = template.GetTargetOfFirstLinkOfType(hasWords) as SeqElement;
        for (; position is not null && input is not null; position = position.NXT)
        {
            Thought? phrase = theUKS.GetElementValue(input, patternRoot);
            Thought value = phrase ?? input.VLU;
            SeqElement last = input;
            while (phrase is not null && last.NXT is SeqElement next &&
                ReferenceEquals(theUKS.GetElementValue(next, patternRoot), phrase)) last = next;
            // A recognized phrase fills only an element of its class: a clause ("Fido is a dog") is not
            // the noun phrase of "Fido and Rover are green". Sequence search ORs a wildcard's parents.
            if (phrase is not null && position.VLU.HasProperty(isWildcard) &&
                ElementClasses(position.VLU).ToList() is { Count: > 0 } classes && !classes.Any(phrase.HasAncestor))
                return false;
            if (value.HasAncestor(unknownWordType) && position.VLU.HasProperty(isWildcard) &&
                position.VLU.Parents.Any(parent => parent.HasAncestor(wordType) && parent.HasProperty(closedClass)))
                return false;
            if (phrase is null && !HasRequiredInflection(input, position.VLU)) return false;
            // What an inflection adds must reach a meaning: "Fido was a dog" must not be read as the
            // present-tense is-a of a template whose copula supplies nothing.
            // An auxiliary that lends its inflection passes it to the verb instead ("did go": goes.past).
            if (phrase is null && AddedMeaning(InflectionOf(input)) is not null && !IsMeansOperand(template, position.VLU) &&
                !LendsInflection(input))
                return false;
            input = last.NXT;
        }
        // The sentence ran out before the template did.
        if (position is not null) return false;
        // A sentence template describes the whole sentence: it must also end at the last word. In
        // "Is the cat in the kitchen black?" the locative question would leave "black" over; the
        // attribute question about "the cat in the kitchen" covers it all. A clause ends where the
        // next one is joined on ("Fido is a dog | and Fido is hungry"); only a clause can.
        return input is null || !template.Parents.Contains(templateRoot) ||
            allowClauses && JoinsClauses(input) && IsClause(template);
    }

    // Where a clause can start: the first word, and each word right after one that joins clauses.
    private HashSet<SeqElement> ClauseStarts(SeqElement first)
    {
        var starts = new HashSet<SeqElement> { first };
        for (SeqElement? element = first; element?.NXT is SeqElement next; element = next)
            if (JoinsClauses(element)) starts.Add(next);
        return starts;
    }

    private bool JoinsClauses(SeqElement element) => element.VLU.HasProperty(joinsClauses);

    // A template that can be part of a sentence: a statement clause, or a yes/no question clause
    // ("Is Fido green" in "Is Fido green if Fido is outside?"). Its meaning is one relationship.
    private bool IsClause(Thought template) => template.HasAncestor(clause) || template.HasAncestor(theUKS.Labeled("questionClause"));

    // Whether some recognized template covers the sentence from its first word to its last.
    private bool IsCovered(SeqElement first) =>
        first.LinksTo.Where(link => link.LinkType == matchesTemplate).Any(match =>
        {
            for (SeqElement? element = first.NXT; element is not null; element = element.NXT)
                if (!element.LinksTo.Any(link => link.LinkType == matchesTemplate && link.To == match.To)) return false;
            return true;
        });

    // An unknown, unrecognized word matched only by single-element templates (e.g. a bare noun phrase).
    private bool IsLoneUnknownWord(SeqElement start, List<Thought> matches) =>
        theUKS.GetElementValue(start, patternRoot) is null && start.VLU.HasAncestor(unknownWordType) &&
        matches.All(template => (template.GetTargetOfFirstLinkOfType(hasWords) as SeqElement)?.NXT is null);
    // Resolve each match separately (RecordMatch): it carries its MEANS results.
    public List<Thought> GetMeanings(Thought sentenceThought)
    {
        var meanings = new List<Thought>();
        if (means is null || matchesTemplate is null || isWildcard is null || fills is null) return meanings;
        if (sentenceThought.GetTargetOfFirstLinkOfType("hasWords") is not SeqElement first) return meanings;

        bool added;
        do
        {
            added = false;
            for (SeqElement? start = first; start is not null; start = start.NXT)
                foreach (Thought match in MatchesAt(start).ToList())
                {
                    Thought template = TemplateOf(match);
                    SeqElement last = EndOf(match);
                    if (template.GetTargetOfFirstLinkOfType(hasWords) is not SeqElement pattern) continue;

                    // The template must account for exactly the match's words: walk both together,
                    // binding each covered word to the template element it fills in this match.
                    SeqElement? input = start;
                    SeqElement? position = pattern;
                    for (; position is not null && input is not null && input != last.NXT; position = position.NXT)
                    {
                        SeqElement? filled = FilledThrough(input, position, match, last, out _);
                        if (filled is null) break;
                        for (SeqElement word = input; ; word = word.NXT!)
                        {
                            word.AddLink(fills, position)?.AddLink(inMatch!, match);
                            if (ReferenceEquals(word, filled)) break;
                        }
                        input = filled.NXT;
                    }
                    if (position is not null || !ReferenceEquals(input, last.NXT)) continue;

                    // Substitute the meaning bound to each role or class wildcard in each MEANS pattern.
                    var bound = new List<Thought>();
                    foreach (Link definition in template.LinksTo.Where(link => link.LinkType == means))
                    {
                        Thought? resolved = BindPattern(definition.To, pattern, start, last, match);
                        // A question asks about the relationship its words supply (the outer one).
                        if (resolved is Link relationship && template.HasProperty(asksQuestion))
                            resolved = AsQuery(relationship.LinkType) is Thought query ? new Link(relationship.From, query, relationship.To) : null;
                        if (resolved is not null) bound.Add(resolved);
                    }
                    // A phrase describing its referent means that referent, not facts about its class.
                    if (template.HasProperty("describesReferent")) bound = DescribedReferent(bound, match, pattern, start, last);
                    foreach (Thought resolved in bound)
                    {
                        int count = match.LinksTo.Count;
                        Link? result = match.AddLink(means, resolved);
                        if (match.LinksTo.Count > count) added = true;
                        if (result?.To is Thought meaning && !meanings.Any(existing => existing == meaning))
                            meanings.Add(meaning);
                    }
                }
        } while (added);

        return meanings;
    }

    /// <summary>
    /// Fills one template element starting at a sentence word. A word the element accepts supplies
    /// its own meaning; otherwise an already recognized smaller phrase starting here supplies its
    /// meaning and the element covers that whole phrase. A word's meaning is its base word's meaning
    /// as modified by the inflection it was read with ("chased": chases.past.!).
    /// </summary>
    /// <param name="match">The match being filled; its own last word bounds the element.</param>
    /// <returns>The last sentence word the element covers, or null if it cannot be filled here.</returns>
    private SeqElement? FilledThrough(SeqElement input, SeqElement element, Thought match, SeqElement last, out Thought? value)
    {
        Thought expected = element.VLU;
        var classes = ElementClasses(expected).ToList();
        bool acceptsWord = expected.HasProperty(isWildcard)
            ? (classes.Count == 0 || classes.Any(input.VLU.HasAncestor)) && HasRequiredInflection(input, expected)
            : ReferenceEquals(expected, input.VLU);
        if (acceptsWord)
        {
            value = WithInflection(input.VLU.LinksTo.FirstOrDefault(link => link.LinkType == means)?.To, InflectionOf(input));
            return input;
        }

        // A larger template consumes the meaning of an already resolved phrase: another match starting
        // here, of the element's class, that ends within this match. Where several are nested ("the dog"
        // within "the dog in the kitchen", "A and B" within "(A and B) and C"), the longest one is the
        // element; it may be an earlier match of the same template. A phrase supplies what it names (a
        // noun, a described referent); a clause supplies its statement.
        value = null;
        SeqElement? end = null;
        foreach (Thought child in MatchesAt(input).Where(child => !ReferenceEquals(child, match) &&
            classes.Any(TemplateOf(child).HasAncestor)))
        {
            bool isClause = IsClause(TemplateOf(child));
            if (child.LinksTo.FirstOrDefault(link => link.LinkType == means &&
                (link.To is not Link || IsReferentDescriptor(link.To)) != isClause)?.To is not Thought meaning) continue;
            SeqElement childEnd = EndOf(child);
            if (!ReferenceEquals(childEnd, last) && !Follows(last, childEnd)) continue;
            if (end is null || Follows(childEnd, end))
            {
                value = meaning;
                end = childEnd;
            }
        }
        return end;
    }

    // Whether a sentence word comes after another, found by following NXT from the earlier one.
    private static bool Follows(SeqElement later, SeqElement earlier)
    {
        for (SeqElement? element = earlier.NXT; element is not null; element = element.NXT)
            if (ReferenceEquals(element, later)) return true;
        return false;
    }

    /// <summary>
    /// Resolves a MEANS pattern: an operand by reference (Bind), and a relationship pattern part by
    /// part, including a relationship nested as an operand ([[subject->verb->object]->located.!->place]).
    /// </summary>
    /// <returns>The resolved meaning, or null if any operand cannot be resolved.</returns>
    private Thought? BindPattern(Thought? operand, SeqElement pattern, SeqElement start, SeqElement last, Thought match)
    {
        if (operand is not Link relationship) return Bind(operand, pattern, start, last, match);
        Thought? from = BindPattern(relationship.From, pattern, start, last, match);
        Thought? type = BindPattern(relationship.LinkType, pattern, start, last, match);
        Thought? to = BindPattern(relationship.To, pattern, start, last, match);
        return from is null || type is null || to is null ? null : new Link(from, type, to);
    }

    /// <summary>
    /// The query form of a relationship type, for a template that asksQuestion: is.! becomes is.?,
    /// chases.ongoing.! becomes chases.ongoing.?. A query type is returned unchanged.
    /// </summary>
    private Thought? AsQuery(Thought? type)
    {
        if (type is null || type.HasProperty("isPossible")) return type;
        Thought relationship = type.HasProperty(setRelationship) ? theUKS.GetActionRelationshipType(type) ?? type : type;
        return theUKS.GetOrAddThought(relationship.Label + ".?");
    }

    /// <summary>
    /// Resolves one MEANS operand by reference. An operand that is the value of one of this
    /// template's elements (a role or class wildcard) is replaced by the meaning of the sentence
    /// word bound to that element through FILLS in this match. The bare ?? is an open query variable
    /// and any other non-wildcard operand is used as written. A role or class wildcard that names no
    /// element of this template cannot be resolved; it must never reach Execute as written.
    /// </summary>
    private Thought? Bind(Thought? operand, SeqElement pattern, SeqElement start, SeqElement last, Thought match)
    {
        for (SeqElement? element = pattern; element is not null; element = element.NXT)
        {
            if (!ReferenceEquals(operand, element.VLU)) continue;
            for (SeqElement? word = start; word is not null && word != last.NXT; word = word.NXT)
                if (Fills(word, element, match))
                {
                    FilledThrough(word, element, match, last, out Thought? value);
                    // A verb in its plain form takes the tense an auxiliary lends it: "did ... go" is goes.past.
                    if (value is not null && value.HasProperty(setRelationship) && AddedMeaning(InflectionOf(word)) is null)
                        value = WithInflection(value, LentInflection(start, last, match));
                    return value;
                }
            return null;
        }
        if (operand is not null && operand.HasProperty(isWildcard) && !ReferenceEquals(operand, theUKS.Labeled("??")))
            return null;
        return operand;
    }

    private static bool LendsInflection(SeqElement word) => word.VLU.HasProperty("lendsInflection");

    // The inflection an auxiliary in this match lends its verb ("did": past), if any.
    private Thought? LentInflection(SeqElement start, SeqElement last, Thought match)
    {
        for (SeqElement? word = start; word is not null && word != last.NXT; word = word.NXT)
            if (LendsInflection(word) && InflectionOf(word) is Thought inflection &&
                word.LinksTo.Any(link => link.LinkType == fills && link.LinksTo.Any(tag => tag.LinkType == inMatch && tag.To == match)))
                return inflection;
        return null;
    }

    #region Matches

    // A match is a Thought of its own: the first word starts it (startsMatch), it is of a template
    // (matchOf) and ends at a word (endsAt); its meanings are attached to it. Matches of one template
    // over different words stay distinct, so a template can take an earlier match of itself as an
    // element, and the loop in GetTemplates can pile "(A and B) and C" on "A and B". The words it
    // covers also link to its template (matchesTemplate), for the phrase facets and the report.

    /// <summary>Records a match unless one of the same template over the same words exists.</summary>
    /// <returns>True if it is new.</returns>
    private bool RecordMatch(SeqElement first, Thought template, SeqElement last)
    {
        if (MatchesAt(first).Any(match => ReferenceEquals(TemplateOf(match), template) && ReferenceEquals(EndOf(match), last)))
            return false;
        Thought match = theUKS.GetOrAddThought("templateMatch*", matchRoot);
        match.AddLink(matchOf!, template);
        match.AddLink(endsAt!, last);
        first.AddLink(startsMatch!, match);
        return true;
    }

    // The matches that start at a sentence word.
    private IEnumerable<Thought> MatchesAt(SeqElement word) =>
        word.LinksTo.Where(link => link.LinkType == startsMatch && link.To is not null).Select(link => link.To);

    private Thought TemplateOf(Thought match) => match.GetTargetOfFirstLinkOfType(matchOf)!;

    private SeqElement EndOf(Thought match) => (SeqElement)match.GetTargetOfFirstLinkOfType(endsAt)!;

    // Whether a word fills a template element in a given match (a word can fill elements of the same
    // template in an inner and an outer match).
    private bool Fills(SeqElement word, SeqElement element, Thought match) =>
        word.LinksTo.Any(link => link.LinkType == fills && link.To == element &&
            link.LinksTo.Any(tag => tag.LinkType == inMatch && tag.To == match));

    #endregion
}

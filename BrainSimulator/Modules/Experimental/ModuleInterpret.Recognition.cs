#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UKS;
using UKS.Helpers;

namespace BrainSimulator.Modules;

// Recognition: GetTemplates finds the templates a sentence matches; GetMeanings binds their MEANS.
public sealed partial class ModuleInterpret
{
    /// <summary>
    /// Finds every match of every template in the sentence: every reading is kept, and only whole
    /// sentence readings compete (CoveringMatches). A template element can be filled by a word of its
    /// class or by any match already recorded at that word whose template is of its class (Walks), so
    /// a wrong local reading ("in" as a bare noun phrase) no longer hides the word's other classes.
    /// Matches are only added, so repeating until nothing changes gives the same set whatever order
    /// they are found in. When nothing covers the sentence, sentence templates are also matched as its
    /// clauses (between words that join clauses), unless lookForClauses is false. Afterwards each word
    /// gets one phrase facet (PhraseFacets), for teaching and display.
    /// </summary>
    public List<Thought> GetTemplates(Thought sentenceThought, bool lookForClauses = true)
    {
        var templates = new List<Thought>(); // Diagnostic summary only, assembled from the UKS below.
        if (patternRoot is null || vlu is null || matchesTemplate is null)
            return templates;
        if (sentenceThought.GetTargetOfFirstLinkOfType(hasWords) is not SeqElement first) return templates;
        List<Thought> patterns = ByWeight(patternRoot.Descendants.Where(pattern =>
            pattern.GetTargetOfFirstLinkOfType(hasWords) is SeqElement).Distinct());

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
            for (SeqElement? start = first; start is not null; start = start.NXT)
            {
                // Every template that can be walked from here, each distinct end with its first walk.
                var found = new List<(Thought template, List<Step> walk)>();
                foreach (Thought template in patterns)
                {
                    // A sentence template (a direct child of template) describes a whole sentence, so it
                    // starts at the first word, or a clause's; phrases can start anywhere. Otherwise "the
                    // kitchen is brown" would be read inside "The dog in the kitchen is brown."
                    bool isSentence = template.Parents.Contains(templateRoot);
                    if (isSentence && !ReferenceEquals(start, first) && !(sentenceStarts.Contains(start) && IsClause(template)))
                        continue;
                    foreach (List<Step> walk in Walks(template, start, null, null))
                    {
                        SeqElement end = walk[^1].Last;
                        // A sentence template must also end at the last word; a clause may end where the
                        // next one is joined on ("Fido is a dog | and Fido is hungry").
                        if (isSentence && end.NXT is SeqElement after && !(allowClauses && JoinsClauses(after) && IsClause(template)))
                            continue;
                        if (!found.Any(entry => entry.template == template && ReferenceEquals(entry.walk[^1].Last, end)))
                            found.Add((template, walk));
                    }
                }
                // A single unknown word on its own is only a guess. If words precede it, a template
                // starting earlier may still cover it, so wait until nothing else can be recognized; one
                // a larger match already reads ("hungry" in "Fido is hungry") is not guessed alone.
                if (found.Count > 0 && !ReferenceEquals(start, first) && IsLoneUnknownWord(start, found))
                {
                    bool atClauseStart = clauseStarts.Contains(start);
                    if (IsReadInAMatch(start)) found.Clear();
                    else if (!allowLoneUnknownWords && !(allowLoneUnknownWordsAtClauseStarts && atClauseStart))
                    {
                        deferred = true;
                        deferredAtClauseStart |= atClauseStart;
                        found.Clear();
                    }
                }

                foreach (var (template, walk) in found)
                {
                    SeqElement last = walk[^1].Last;
                    for (SeqElement? input = start; input is not null; input = input.NXT)
                    {
                        int count = input.LinksTo.Count;
                        input.AddLink(matchesTemplate, template);
                        added |= input.LinksTo.Count > count;
                        if (ReferenceEquals(input, last)) break;
                    }
                    // And the match itself, with its extent: "(A and B) and C" is a second match of
                    // compoundTemplate at the same first word, ending later.
                    added |= RecordMatch(start, template, last);
                }
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
        ClassifyUnknownWords(first);
        PhraseFacets(first);

        for (SeqElement? element = first; element is not null; element = element.NXT)
            foreach (Link link in element.LinksTo)
                if (link.LinkType == matchesTemplate && !templates.Contains(link.To)) templates.Add(link.To);
        return templates;
    }

    // One element of a walk: a template element filled by the words First..Last, either one word
    // (Child null) or the recorded match Child, a smaller phrase starting at First.
    private sealed record Step(SeqElement Element, SeqElement First, SeqElement Last, Thought? Child);

    /// <summary>
    /// The ways a template can be walked from a sentence word, most preferred first: at each element
    /// the word itself if the element takes it, then each recorded match starting there whose template
    /// is of the element's class, longest first. A walk ends where the template does (the sentence may
    /// go on); it never passes bound, if given. usable, if given, says which recorded matches may fill
    /// an element. Recognition and meaning walk the same way.
    /// </summary>
    private IEnumerable<List<Step>> Walks(Thought template, SeqElement start, SeqElement? bound, Func<Thought, bool>? usable) =>
        template.GetTargetOfFirstLinkOfType(hasWords) is SeqElement pattern ? WalksFrom(template, pattern, start, bound, usable) : [];

    private IEnumerable<List<Step>> WalksFrom(Thought template, SeqElement position, SeqElement? input, SeqElement? bound, Func<Thought, bool>? usable)
    {
        if (input is null) yield break;  // the sentence ran out before the template did
        foreach (Step step in Fillers(template, position, input, bound, usable))
        {
            if (position.NXT is not SeqElement next) { yield return [step]; continue; }
            if (ReferenceEquals(step.Last, bound)) continue;
            foreach (List<Step> rest in WalksFrom(template, next, step.Last.NXT, bound, usable))
                yield return [step, .. rest];
        }
    }

    /// <summary>
    /// What can fill one template element at a sentence word. The word, if the element takes it: a
    /// literal element only that word; a class wildcard a word of its class (sequence search ORs a
    /// wildcard's parents) with the inflection it requires ("is chasing", not "is chases"), or an
    /// unknown word if all its classes are word classes, but never into a closed class (articles,
    /// auxiliaries); a word whose inflection adds meaning (past, ongoing) only where the template uses
    /// the element's meaning ("Fido was a dog" must not be the present is-a), unless it lends it to a
    /// verb ("did go"). Then the recorded matches starting at the word whose template is of the
    /// element's class (a clause is not the noun phrase of "Fido and Rover are green"), longest first.
    /// </summary>
    private IEnumerable<Step> Fillers(Thought template, SeqElement element, SeqElement input, SeqElement? bound, Func<Thought, bool>? usable)
    {
        Thought expected = element.VLU;
        bool isWildcardElement = expected.HasProperty(isWildcard);
        bool takesWord = isWildcardElement
            ? TakesWord(expected, input) && !(input.VLU.HasAncestor(unknownWordType) &&
                expected.Parents.Any(parent => parent.HasAncestor(wordType) && parent.HasProperty(closedClass)))
            : ReferenceEquals(expected, input.VLU) && HasRequiredInflection(input, expected);
        if (takesWord && (AddedMeaning(InflectionOf(input)) is null || IsMeansOperand(template, expected) || LendsInflection(input)) &&
            MayStandBare(template, expected, input.VLU, ReferenceEquals(InflectionOf(input), plural)))
            yield return new Step(element, input, input, null);
        var classes = ElementClasses(expected).ToList();
        if (!isWildcardElement || classes.Count == 0) yield break;
        var children = MatchesAt(input).Where(child => classes.Any(a => TemplateOf(child).HasAncestor(a)) &&
            (bound is null || ReferenceEquals(EndOf(child), bound) || Follows(bound, EndOf(child))) &&
            (usable is null || usable(child)));
        // Longest first: each child before every child it ends after; readings of the same words best first.
        foreach (Thought child in Preferred(children.OrderBy(EndOf, LaterFirst)))
            yield return new Step(element, input, EndOf(child), child);
    }

    // Whether a class wildcard takes a sentence word itself: one of its classes (sequence search ORs a
    // wildcard's parents) with the inflection it requires, or an unknown word if all its classes are
    // word classes.
    private bool TakesWord(Thought expected, SeqElement input)
    {
        var classes = ElementClasses(expected).ToList();
        bool takes = input.VLU.HasAncestor(unknownWordType)
            ? classes.Count > 0 && expected.Parents.Where(parent => !parent.HasProperty(isWildcard)).All(parent => parent.HasAncestor(wordType))
            : classes.Count == 0 || classes.Any(a => input.VLU.HasAncestor(a));
        return takes && HasRequiredInflection(input, expected);
    }

    /// <summary>
    /// A template that lacksDeterminer (a noun phrase with no article: "dogs", "brown dogs") takes a
    /// noun only if it may stand bare: a plural, or a noun that doesn't need a determiner (a name, a
    /// mass noun, a learned noun). "Fido is green" can't mean "Fido is a green", so a singular common
    /// noun ("Fido is dog") is not read, or said, there. BasicWords: commonNoun needsDeterminer,
    /// massNoun standsBare. Recognition and generation both ask.
    /// </summary>
    private bool MayStandBare(Thought template, Thought element, Thought baseWord, bool isPlural) =>
        isPlural || !template.HasProperty("lacksDeterminer") ||
        !ElementClasses(element).Any(wordClass => ReferenceEquals(wordClass, noun) || wordClass.HasAncestor(noun)) ||
        !baseWord.HasProperty("needsDeterminer") || baseWord.HasProperty("standsBare");

    /// <summary>
    /// An unknown word joins the class of the wildcard it fills in the reading that wins, not in every
    /// reading ("the hungry" alone would make hungry a noun in "the hungry dog ate dinner"): the
    /// strongest matches covering the whole sentence (all of them if equally strong), else, from the
    /// first word, the longest matches at each word and the word after them. Inner matches count
    /// through the walk that reads them. Recognition itself lets an unknown word fill any word class.
    /// </summary>
    private void ClassifyUnknownWords(SeqElement first)
    {
        SeqElement end = first;
        while (end.NXT is SeqElement next) end = next;
        var readings = new List<Thought>();
        List<Thought> covering = MatchesAt(first).Where(match => ReferenceEquals(EndOf(match), end)).ToList();
        if (covering.Count > 0)
            readings.AddRange(covering.Where(match => TemplateOf(match).Weight == covering.Max(other => TemplateOf(other).Weight)));
        else
            for (SeqElement? start = first; start is not null;)
            {
                List<Thought> here = MatchesAt(start).ToList();
                if (here.Count == 0) { start = start.NXT; continue; }
                SeqElement longest = here.Select(EndOf).Aggregate((a, b) => Follows(b, a) ? b : a);
                readings.AddRange(here.Where(match => ReferenceEquals(EndOf(match), longest)));
                start = longest.NXT;
            }
        var classified = new HashSet<Thought>();
        var pending = new Stack<Thought>(readings);
        var visited = new HashSet<Thought>();
        while (pending.Count > 0)
        {
            Thought match = pending.Pop();
            if (!visited.Add(match) || MatchStart(match) is not SeqElement start) continue;
            List<Step>? walk = Walks(TemplateOf(match), start, EndOf(match), child => !ReferenceEquals(child, match))
                .FirstOrDefault(steps => ReferenceEquals(steps[^1].Last, EndOf(match)));
            foreach (Step step in walk ?? [])
            {
                if (step.Child is not null) { pending.Push(step.Child); continue; }
                Thought word = step.First.VLU;
                if (!word.HasAncestor(unknownWordType) || !step.Element.VLU.HasProperty(isWildcard)) continue;
                foreach (Thought type in step.Element.VLU.Parents.Where(parent => parent.HasAncestor(wordType)))
                    word.AddLink(isA!, type);
                classified.Add(word);
            }
        }
        foreach (Thought word in classified)
            if (word.Parents.Any(parent => parent != unknownWordType && parent.HasAncestor(wordType)))
            {
                word.RemoveParent(unknownWordType!);
                word.RemoveParent(wordRoot!);
            }
    }

    // The sentence word a match starts at.
    private SeqElement? MatchStart(Thought match) =>
        match.LinksFrom.FirstOrDefault(link => link.LinkType == startsMatch)?.From as SeqElement;

    /// <summary>
    /// Gives each sentence word one phrase facet (a languagePattern VLU), for teaching (Units) and
    /// display: from the first word, the longest match starting there (the strongest of equally long
    /// ones) covers its words, then the next word after it, and so on. Recognition does not use them.
    /// </summary>
    private void PhraseFacets(SeqElement first)
    {
        for (SeqElement? element = first; element is not null; element = element.NXT)
            foreach (Link link in element.LinksTo.Where(link => link.LinkType == vlu && link.To.HasAncestor(patternRoot)).ToList())
                element.RemoveLink(link);
        for (SeqElement? start = first; start is not null;)
        {
            Thought? longest = null;
            foreach (Thought match in MatchesAt(start).OrderByDescending(match => TemplateOf(match).Weight))
                if (longest is null || Follows(EndOf(match), EndOf(longest))) longest = match;
            if (longest is null) { start = start.NXT; continue; }
            for (SeqElement? element = start; element is not null; element = element.NXT)
            {
                element.AddLink(vlu, TemplateOf(longest));
                if (ReferenceEquals(element, EndOf(longest))) break;
            }
            start = EndOf(longest).NXT;
        }
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

    // An unknown word that no match starts at, found only by single-element templates (a bare noun phrase).
    private bool IsLoneUnknownWord(SeqElement start, List<(Thought template, List<Step> walk)> found) =>
        start.VLU.HasAncestor(unknownWordType) && !MatchesAt(start).Any() && found.All(entry => entry.walk.Count == 1);

    // Whether some recorded match covers a word.
    private bool IsReadInAMatch(SeqElement word) => word.LinksTo.Any(link => link.LinkType == matchesTemplate);

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

                    // The template must account for exactly the match's words: find a walk that ends at
                    // its last word (smaller phrases inside it already have their meanings), binding each
                    // covered word to the template element it fills in this match.
                    List<Step>? walk = Walks(template, start, last, child => !ReferenceEquals(child, match) && HasUsableMeaning(child))
                        .FirstOrDefault(steps => ReferenceEquals(steps[^1].Last, last));
                    if (walk is null) continue;
                    foreach (Step step in walk)
                        for (SeqElement word = step.First; ; word = word.NXT!)
                        {
                            word.AddLink(fills, step.Element)?.AddLink(inMatch!, match);
                            if (ReferenceEquals(word, step.Last)) break;
                        }

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
    /// The meaning that fills a template element in a match, starting at a sentence word that fills
    /// it: the word's own meaning, as modified by the inflection it was read with ("chased":
    /// chases.past.!), or the meaning of the smaller phrase covering the same words. A phrase supplies
    /// what it names (a noun, a described referent); a clause supplies its statement.
    /// </summary>
    private Thought? FilledValue(SeqElement word, SeqElement element, Thought match)
    {
        SeqElement end = word;
        while (end.NXT is SeqElement next && Fills(next, element, match)) end = next;
        if (ReferenceEquals(end, word) && (element.VLU.HasProperty(isWildcard) ? TakesWord(element.VLU, word) : ReferenceEquals(element.VLU, word.VLU)))
            return WithInflection(word.VLU.LinksTo.FirstOrDefault(link => link.LinkType == means)?.To, InflectionOf(word));
        var classes = ElementClasses(element.VLU).ToList();
        Thought? child = Preferred(MatchesAt(word).Where(child => !ReferenceEquals(child, match) && ReferenceEquals(EndOf(child), end) &&
            classes.Any(a => TemplateOf(child).HasAncestor(a)) && HasUsableMeaning(child))).FirstOrDefault();
        return child is null ? null : UsableMeaning(child);
    }

    // A recorded match's meaning as a phrase supplies it: a clause its statement, a phrase what it names.
    private Thought? UsableMeaning(Thought match)
    {
        bool isClause = IsClause(TemplateOf(match));
        return match.LinksTo.FirstOrDefault(link => link.LinkType == means &&
            (link.To is not Link || IsReferentDescriptor(link.To)) != isClause)?.To;
    }

    private bool HasUsableMeaning(Thought match) => UsableMeaning(match) is not null;

    /// <summary>
    /// Readings of the same words, best first: those whose relationships meet what their types
    /// expect of their targets (Fit), then the stronger template (weight). Otherwise the order is
    /// kept (stable). The whole sentence's readings (CoveringMatches), the inner reading a larger
    /// one is built from (Fillers, FilledValue) all compete this way. A reading has a Fit only once
    /// it has meanings (GetMeanings), so during recognition only weight counts.
    /// </summary>
    private IEnumerable<Thought> Preferred(IEnumerable<Thought> readings)
    {
        Thought? expectsTarget = theUKS.Labeled("expectsTarget");
        if (readings is IOrderedEnumerable<Thought> ordered)
            return ordered.ThenByDescending(reading => Fit(reading, expectsTarget)).ThenByDescending(reading => TemplateOf(reading).Weight);
        return readings.OrderByDescending(reading => Fit(reading, expectsTarget)).ThenByDescending(reading => TemplateOf(reading).Weight);
    }

    /// <summary>
    /// How well a reading's meanings agree with what is known: each relationship (nested ones too)
    /// whose type, or a type it inherits from, expectsTarget a class (BasicWords: located expects a
    /// location) counts +1 if its target is of that class and -1 if not. "Fido is outside" as
    /// located.! scores 1 (outside is a location); as the attribute is.! it scores 0.
    /// </summary>
    private int Fit(Thought reading, Thought? expectsTarget) =>
        expectsTarget is null ? 0 : MeaningsOf(reading).Sum(meaning => Fit(meaning as Link, expectsTarget));

    private int Fit(Link? relationship, Thought expectsTarget)
    {
        if (relationship is null || IsReferentDescriptor(relationship)) return 0;
        int fit = Fit(relationship.From as Link, expectsTarget) + Fit(relationship.To as Link, expectsTarget);
        Thought? expected = relationship.LinkType?.AncestorsWithSelf
            .Select(type => type.GetTargetOfFirstLinkOfType(expectsTarget)).FirstOrDefault(target => target is not null);
        if (expected is null || relationship.To is null || relationship.To is Link) return fit;
        return fit + (relationship.To.AncestorsWithSelf.Contains(expected) ? 1 : -1);
    }

    // Sentence words, later first (for longest-first orderings).
    private static readonly IComparer<SeqElement> LaterFirst = Comparer<SeqElement>.Create((a, b) =>
        ReferenceEquals(a, b) ? 0 : Follows(a, b) ? -1 : 1);

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
                    Thought? value = FilledValue(word, element, match);
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UKS;
using UKS.Helpers;

namespace BrainSimulator.Modules;

// Generation: GenerateSentence runs an assertion template in reverse to state a relationship.
public sealed partial class ModuleInterpret
{
    #region GenerateSentence

    /// <summary>
    /// Expresses a relationship in English by running an assertion template in reverse. A sentence
    /// template is a candidate when its MEANS pattern unifies with the fact: an operand that is one of
    /// the template's elements (a role or class wildcard) is bound to the fact's corresponding Thought,
    /// and any other operand must express the fact's Thought itself. Each template offers its word
    /// sequences in order of preference (no free choices first, then singular words, then spelling
    /// order); the first sequence that satisfies every word order constraint and agrees in number
    /// is the sentence. Answering a question, each noun the question used is said as it was there,
    /// with the same article (or none) and number: "Do dogs have tails?" "Dogs have tails."
    /// Other nouns are singular where they can be, with "a"/"an" where the noun takes an article.
    /// </summary>
    /// <param name="question">The sentence being answered, if any.</param>
    /// <returns>The sentence, or null if no template can express the relationship.</returns>
    public string? GenerateSentence(Link fact, Thought? question = null)
    {
        if (templateRoot is null || means is null || hasWords is null ||
            fact.From is null || fact.LinkType is null || fact.To is null) return null;
        // Only ordinary relationships are stated. A query (.?) or write (.!) action is not a fact.
        if (fact.LinkType.HasProperty(setRelationship) || fact.LinkType.HasProperty("isPossible")) return null;

        questionNouns = NounsOf(question);
        try { return Generate(fact); }
        finally { questionNouns = null; }
    }

    // What the question being answered said about each noun concept: its article (null for none)
    // and whether it was plural.
    private Dictionary<Thought, (Thought? Article, bool Plural)>? questionNouns;

    private Dictionary<Thought, (Thought? Article, bool Plural)> NounsOf(Thought? sentence)
    {
        var nouns = new Dictionary<Thought, (Thought? Article, bool Plural)>(ReferenceEqualityComparer.Instance);
        if (sentence?.GetTargetOfFirstLinkOfType(hasWords) is not SeqElement first) return nouns;
        Thought? pending = null; // The article of the noun phrase being read, if it has one.
        for (SeqElement? element = first; element is not null; element = element.NXT)
        {
            Thought word = element.VLU;
            if (word.HasAncestor(article)) pending = word.GetTargetOfFirstLinkOfType(means);
            else if (word.HasAncestor(noun))
            {
                if (word.GetTargetOfFirstLinkOfType(means) is Thought concept)
                    nouns.TryAdd(concept, (pending, ReferenceEquals(InflectionOf(element), plural)));
                pending = null;
            }
            else if (!word.HasAncestor(theUKS.Labeled("adjective"))) pending = null;
        }
        return nouns;
    }

    private string? Generate(Link fact)
    {
        foreach (bool allowChoices in new[] { false, true })
            foreach (Thought template in templateRoot.Children)
            {
                if (template.GetTargetOfFirstLinkOfType(hasWords) is not SeqElement pattern) continue;
                // A question template states nothing, even where its MEANS pattern unifies with the fact.
                if (template.HasProperty(asksQuestion)) continue;
                foreach (Link definition in template.LinksTo.Where(link => link.LinkType == means))
                {
                    if (definition.To is not Link relationship) continue;
                    var bindings = new Dictionary<Thought, Thought>(ReferenceEqualityComparer.Instance);
                    if (!Unify(relationship, fact, pattern, bindings, isType: false)) continue;

                    Thought? Bound(Thought value) => bindings.TryGetValue(value, out Thought? bound) ? bound : null;
                    foreach (List<Thought> words in ExpressTemplate(pattern, template, Bound, allowChoices))
                        if (FollowsWordOrder(words)) return Format(words);
                }
            }
        return null;
    }

    /// <summary>
    /// Unifies a MEANS pattern with a fact. An operand that is one of the template's elements is bound
    /// to the fact's Thought (consistently, if it occurs twice); a relationship pattern unifies part by
    /// part with a relationship, including one nested as an operand (an event placed somewhere); any
    /// other operand is part of what the template says, so it must agree: a fixed relationship type
    /// must express the fact's type (is-a.! expresses is-a), anything else must be the same Thought.
    /// </summary>
    private bool Unify(Thought? operand, Thought? value, SeqElement pattern, Dictionary<Thought, Thought> bindings, bool isType)
    {
        if (operand is null || value is null) return operand is null && value is null;
        if (IsElementValue(pattern, operand))
        {
            if (bindings.TryGetValue(operand, out Thought? bound)) return ReferenceEquals(bound, value);
            bindings[operand] = value;
            return true;
        }
        if (operand is Link relationship && value is Link fact)
            return Unify(relationship.From, fact.From, pattern, bindings, isType: false) &&
                Unify(relationship.LinkType, fact.LinkType, pattern, bindings, isType: true) &&
                Unify(relationship.To, fact.To, pattern, bindings, isType: false);
        return isType ? Expresses(operand, value) : ReferenceEquals(operand, value);
    }

    /// <summary>
    /// Offers a template's word sequences, from this element on, in order of preference. A literal word
    /// is used as written; an element bound to a concept is expressed by a word or phrase of its class
    /// with that meaning; an unbound element is a free choice of its class, permitted only when allowed
    /// and when the element carries no meaning (an arbitrary filler for a MEANS operand would assert
    /// something the fact does not say). Alternatives are produced only as they are needed.
    /// </summary>
    private IEnumerable<List<Thought>> ExpressTemplate(SeqElement? element, Thought template, Func<Thought, Thought?> bound, bool allowChoices)
    {
        if (element is null)
        {
            yield return new List<Thought>();
            yield break;
        }
        Thought value = element.VLU;
        IEnumerable<List<Thought>> parts =
            !value.HasProperty(isWildcard) ? new[] { new List<Thought> { value } } :
            bound(value) is Thought concept ? Express(value, concept) :
            !allowChoices || IsMeansOperand(template, value) ? Enumerable.Empty<List<Thought>>() :
            Choose(value);
        foreach (List<Thought> part in parts)
        {
            // A one-word part is the element's word: it may have to stand bare (MayStandBare).
            if (part.Count == 1 && !MayStandBare(template, value, BaseWordOf(part[0], out _) ?? part[0], ReferenceEquals(InflectionOfForm(part[0]), plural)))
                continue;
            foreach (List<Thought> rest in ExpressTemplate(element.NXT, template, bound, allowChoices))
                yield return part.Concat(rest).ToList();
        }
    }

    /// <summary>
    /// Offers the words, or phrases, of the element's class that express a concept. A noun the
    /// question used keeps its number, and its noun phrase keeps its article (or has none). Another
    /// noun's phrase offers "a" first; the word order constraints let it stand only before a singular
    /// count noun, so a name, a mass noun, or a plural is said bare.
    /// </summary>
    private IEnumerable<List<Thought>> Express(Thought element, Thought concept)
    {
        bool asked = questionNouns?.ContainsKey(concept) == true;
        var (askedArticle, askedPlural) = asked ? questionNouns![concept] : (null, false);
        foreach (Thought wordClass in ElementClasses(element))
        {
            if (wordClass.HasAncestor(wordType))
            {
                foreach (Thought word in WordsExpressing(wordClass, concept, RequiredInflection(element)))
                    if (!asked || !WordClasses(word).Contains(noun!) || ReferenceEquals(InflectionOfForm(word), askedPlural ? plural : null))
                        yield return new List<Thought> { word };
                continue;
            }
            // A referent with no word of its own ("the black dog") is said by its class and description.
            if (IsMadeReferent(concept))
            {
                foreach (List<Thought> words in ExpressReferent(wordClass, concept)) yield return words;
                continue;
            }
            // A phrase class: use a phrase whose own MEANS is the element bound to the concept.
            // A phrase needing free choices is offered only if no phrase can do without them.
            Thought? articleChoice = asked ? askedArticle : theUKS.Labeled("a");
            bool offered = false;
            foreach (bool allowChoices in new[] { false, true })
            {
                if (offered) break;
                foreach (Thought phrase in wordClass.Children)
                {
                    if (phrase.GetTargetOfFirstLinkOfType(hasWords) is not SeqElement pattern || ContainsItsClass(pattern, wordClass)) continue;
                    Thought? slot = phrase.LinksTo.FirstOrDefault(link => link.LinkType == means && link.To is not Link)?.To;
                    if (slot is null) continue;
                    // The question's noun phrase had an article, or had none: say it the same way.
                    if (asked && HasArticleElement(pattern) != askedArticle is not null) continue;
                    Thought? Bound(Thought value) => ReferenceEquals(value, slot) ? concept :
                        articleChoice is not null && IsArticleElement(value) ? articleChoice : null;
                    foreach (List<Thought> words in ExpressTemplate(pattern, phrase, Bound, allowChoices))
                    {
                        offered = true;
                        yield return words;
                    }
                }
            }
        }
    }

    // An unbound element for an article (??.article in nounPhrase2), and whether a phrase has one.
    private bool IsArticleElement(Thought value) =>
        value.HasProperty(isWildcard) && ElementClasses(value).Any(wordClass => wordClass.HasAncestor(article));

    private bool HasArticleElement(SeqElement pattern)
    {
        for (SeqElement? element = pattern; element is not null; element = element.NXT)
            if (IsArticleElement(element.VLU)) return true;
        return false;
    }

    /// <summary>
    /// A phrase containing an element of its own class ("the cat in the kitchen" is a noun phrase that
    /// begins with one) is not offered when expressing that class: it would offer itself without end,
    /// and the meaning of its other elements (the place) is not part of the concept being expressed.
    /// </summary>
    private bool ContainsItsClass(SeqElement pattern, Thought phraseClass)
    {
        for (SeqElement? element = pattern; element is not null; element = element.NXT)
            if (element.VLU.HasProperty(isWildcard) && ElementClasses(element.VLU).Any(wordClass => wordClass.HasAncestor(phraseClass)))
                return true;
        return false;
    }

    /// <summary>Offers the free choices for an unbound element: the words, or phrases, of its class.</summary>
    private IEnumerable<List<Thought>> Choose(Thought element)
    {
        foreach (Thought wordClass in ElementClasses(element))
        {
            if (wordClass.HasAncestor(wordType))
            {
                foreach (Thought word in WordsOf(wordClass, _ => true))
                    yield return new List<Thought> { word };
                continue;
            }
            bool offered = false;
            foreach (bool allowChoices in new[] { false, true })
            {
                if (offered) break;
                foreach (Thought phrase in wordClass.Children)
                    if (phrase.GetTargetOfFirstLinkOfType(hasWords) is SeqElement pattern && !ContainsItsClass(pattern, wordClass))
                        foreach (List<Thought> words in ExpressTemplate(pattern, phrase, _ => null, allowChoices))
                        {
                            offered = true;
                            yield return words;
                        }
            }
        }
    }

    /// <summary>
    /// Word order constraints are UKS knowledge (BasicWords.txt), held by a word or inherited from its
    /// class: precedesWord and precedesWordStartingWith restrict the next word, by its class or by the
    /// class of its first letter ("an" precedes a vowel); followsWord restricts the previous word
    /// (an ongoing verb follows a copula). An inflected form has its base word's classes and rules,
    /// plus its inflection (w:legs is a plural noun, so it may follow w:4).
    /// </summary>
    private bool FollowsWordOrder(List<Thought> words) =>
        words.Select((word, index) => index + 1 >= words.Count || MayPrecede(word, words[index + 1], Heads(words, index + 1))).All(ok => ok) &&
        AgreesInNumber(words);

    // A word of a class that isModifier (an adjective) stands between a word and the one precedesWord
    // is about: "the" precedes a common noun in "the black dog". The heads are the words from here
    // through the first that is not a modifier: a word that can also be an adjective (w:animal, in
    // WordNet) may itself be the head ("an animal").
    private List<Thought> Heads(List<Thought> words, int from)
    {
        var heads = new List<Thought>();
        foreach (Thought word in words.Skip(from))
        {
            heads.Add(word);
            if (!IsModifier(word)) break;
        }
        return heads;
    }
    private bool IsModifier(Thought word) => WordClasses(word).Any(wordClass => wordClass.HasProperty("isModifier"));

    /// <summary>
    /// A word whose class agreesInNumber (a verb) is plural exactly when the subject, the sentence's
    /// first noun, is: "a dog has", "dogs have". A verb with no plural form, or in another form
    /// (a tense), has nothing to agree.
    /// </summary>
    private bool AgreesInNumber(List<Thought> words)
    {
        if (noun is null || plural is null || agreesInNumber is null) return true;
        Thought? subject = words.FirstOrDefault(word => WordClasses(word).Contains(noun));
        if (subject is null) return true;
        bool pluralSubject = WordClasses(subject).Contains(plural);
        foreach (Thought word in words.Where(word => WordClasses(word).Any(wordClass => wordClass.HasProperty(agreesInNumber))))
        {
            Thought baseWord = BaseWordOf(word, out Thought? inflection) ?? word;
            if (inflection is not null && !ReferenceEquals(inflection, plural)) continue;
            if (!FormsOf(baseWord, plural).Any()) continue;
            if (ReferenceEquals(inflection, plural) != pluralSubject) return false;
        }
        return true;
    }

    // precedesWordStartingWith and followsWord are about the adjacent word; precedesWord about a head
    // (Heads), one meeting all of them ("a": a singular count noun).
    private bool MayPrecede(Thought left, Thought right, List<Thought> heads)
    {
        List<Thought> leftClasses = WordClasses(left), rightClasses = WordClasses(right);
        var precedes = new List<Thought>();
        foreach (Link rule in leftClasses.SelectMany(thought => thought.LinksTo))
        {
            if (rule.LinkType is null || rule.To is null) continue;
            if (ReferenceEquals(rule.LinkType, precedesWord)) precedes.Add(rule.To);
            if (ReferenceEquals(rule.LinkType, precedesWordStartingWith) && FirstLetter(right)?.HasAncestor(rule.To) != true) return false;
        }
        if (precedes.Count > 0 && !heads.Any(head => precedes.All(WordClasses(head).Contains))) return false;
        foreach (Link rule in rightClasses.SelectMany(thought => thought.LinksTo))
            if (rule.LinkType is not null && rule.To is not null &&
                ReferenceEquals(rule.LinkType, followsWord) && !leftClasses.Contains(rule.To)) return false;
        return true;
    }

    // A word with its ancestors; for an inflected form, its base word's and its inflection's as well.
    private List<Thought> WordClasses(Thought word)
    {
        var classes = word.AncestorsWithSelf.ToList();
        if (BaseWordOf(word, out Thought? inflection) is Thought baseWord)
        {
            classes.AddRange(baseWord.AncestorsWithSelf);
            if (inflection is not null) classes.AddRange(inflection.AncestorsWithSelf);
        }
        // A base word is singular; a form carries its own inflection.
        else if (singular is not null) classes.Add(singular);
        return classes;
    }

    private Thought? FirstLetter(Thought word) => (word.GetTargetOfFirstLinkOfType(spelled) as SeqElement)?.VLU;

    /// <summary>
    /// A word meaning expresses a concept when it is that concept, or when it is a write action
    /// whose ordinary relationship type is that concept (w:has means has.!, which expresses has).
    /// </summary>
    private bool Expresses(Thought? meaning, Thought concept) => meaning is not null &&
        (ReferenceEquals(meaning, concept) || meaning.HasProperty(setRelationship) &&
            ReferenceEquals(theUKS.GetPlainLinkType(meaning), concept));

    private static bool IsElementValue(SeqElement pattern, Thought? value)
    {
        for (SeqElement? element = pattern; element is not null; element = element.NXT)
            if (ReferenceEquals(element.VLU, value)) return true;
        return false;
    }

    // An element value is used by one of the template's MEANS patterns, at any depth.
    private bool IsMeansOperand(Thought template, Thought value) =>
        template.LinksTo.Where(link => link.LinkType == means).Any(link => Uses(link.To, value));

    private static bool Uses(Thought? pattern, Thought value) => pattern is Link relationship
        ? Uses(relationship.From, value) || Uses(relationship.LinkType, value) || Uses(relationship.To, value)
        : ReferenceEquals(pattern, value);

    /// <summary>
    /// The words of a class that express a concept: base words with that meaning and their forms that
    /// add no meaning; then forms whose inflection adds what a base word's meaning lacks. The concept
    /// chases.ongoing is chases plus ongoing, which the present participle adds, so w:chasing.
    /// An element that requires an inflection accepts only words with it.
    /// </summary>
    private IEnumerable<Thought> WordsExpressing(Thought wordClass, Thought concept, Thought? required)
    {
        IEnumerable<Thought> plain = WordsOf(wordClass, word => Expresses(word.GetTargetOfFirstLinkOfType(means), concept));
        IEnumerable<Thought> inflected = concept.Parents.SelectMany(added => inflectionRoot!.Descendants
            .Where(inflection => ReferenceEquals(AddedMeaning(inflection), added))
            .SelectMany(inflection => WordsOf(wordClass, word => concept.Parents.Any(rest => !ReferenceEquals(rest, added) &&
                Expresses(word.GetTargetOfFirstLinkOfType(means), rest)), inflection)));
        return plain.Concat(inflected).Where(word => required is null || ReferenceEquals(InflectionOfForm(word), required));
    }

    private Thought? InflectionOfForm(Thought word)
    {
        BaseWordOf(word, out Thought? inflection);
        return inflection;
    }

    /// <summary>
    /// Vocabulary of a word class: its base words (those with a meaning) that pass the filter, then their
    /// forms that add no meaning, each in spelling order, so choices are repeatable and base forms are
    /// preferred. Given an inflection, only the base words' forms with that inflection.
    /// </summary>
    private IEnumerable<Thought> WordsOf(Thought wordClass, Func<Thought, bool> filter, Thought? inflection = null)
    {
        var baseWords = wordClass.Descendants
            .Where(word => word.GetTargetOfFirstLinkOfType(spelled) is SeqElement && word.GetTargetOfFirstLinkOfType(means) is not null)
            .Where(filter)
            .OrderBy(Spelling, StringComparer.Ordinal).ToList();
        IEnumerable<Thought> forms = baseWords.SelectMany(word => FormsOf(word, inflection))
            .Where(form => form.GetTargetOfFirstLinkOfType(spelled) is SeqElement)
            .OrderBy(Spelling, StringComparer.Ordinal);
        return inflection is null ? baseWords.Concat(forms) : forms;
    }

    // A word's text comes from its spelling sequence, not from its label.
    private string Spelling(Thought word) =>
        word.GetTargetOfFirstLinkOfType(spelled) is SeqElement spelling
            ? string.Concat(theUKS.EnumerateSequenceElements(spelling)
                .Select(letter => letter.VLU.Label[UKS.UKS.CharacterPrefix.Length..])).ToLowerInvariant()
            : word.Label;

    // A sentence starts with a capital and ends with a period; a clause inside a sentence does neither.
    // A proper noun is capitalized anywhere ("...if Fido is outside").
    private string Format(List<Thought> words)
    {
        string text = string.Join(" ", words.Select(word =>
            word.HasAncestor(theUKS.Labeled("properNoun")) ? Capitalized(Spelling(word)) : Spelling(word)));
        return formatAsClause ? text : Capitalized(text) + ".";
    }

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private bool formatAsClause;

    /// <summary>A relationship said as a clause of a larger sentence ("Fido is outside"), or null.</summary>
    private string? GenerateClause(Link fact)
    {
        formatAsClause = true;
        try { return GenerateSentence(fact); }
        finally { formatAsClause = false; }
    }

    #endregion
}

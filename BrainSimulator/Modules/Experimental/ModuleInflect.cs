#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UKS;
using UKS.Helpers;

namespace BrainSimulator.Modules;

/// <summary>
/// Inflection by rule (experimental, standalone). A regular word is stored once, in its base form
/// (a noun singular, a verb its bare stem, an adjective plain); its regular forms come from spelling
/// rules, and only exceptions are stored as forms ([w:goes->hasPastTense->w:went]). Rules and
/// inflections are UKS data (UKSContent/Inflections.txt). Each rule works both ways: forward it makes
/// a form from a base word, backward it finds the base words a typed form can come from. The dialog
/// shows everything the stored forms and the rules make of a typed word.
/// </summary>
/// <remarks>
/// Transitional: BasicWords still stores a verb under its third-person form (w:chases), with the bare
/// stem as its plural form (w:chase). Here such a verb's stem is that plural form, and its third
/// person singular is the entry itself.
/// </remarks>
public sealed class ModuleInflect : ModuleBase
{
    private Thought? spelled;
    private Thought? wordRoot;
    private Thought? wordForm;
    private Thought? inflectionRoot;
    private Thought? hasInflection;
    private Thought? marksInflection;
    private Thought? inflects;
    private Thought? lacksInflection;
    private Thought? ruleRoot;
    private Thought? formsInflection;
    private Thought? baseEnding;
    private Thought? formEnding;
    private Thought? closedClass;
    private Thought? isWildcard;
    private Thought? plural;
    private Thought? thirdPersonSingular;
    private Thought? verb;
    private Thought? readsBackward;
    private Thought? avoided;
    private readonly Dictionary<Thought, List<Link>> ruleAttributes = new();

    public override void Fire() => Init();

    public override void Initialize() => LoadInflectionKnowledge();

    public override void UKSInitializedNotification() => LoadInflectionKnowledge();

    /// <summary>The vocabulary and rules without WordNet: loads in well under a second.</summary>
    public static readonly string[] CoreFiles = ["BasicWords.txt", "Inflections.txt"];

    /// <summary>
    /// The knowledge files loaded, in order, from UKSContent. WordNetWords.txt (common WordNet words:
    /// spellings and classes, no meanings) takes the UKS a minute or more to load.
    /// </summary>
    public string[] KnowledgeFiles = [.. CoreFiles, "WordNetWords.txt"];

    // Called both after a network loads (UKSInitializedNotification) and on the first Fire
    // (Initialize). Each file is imported once per store: a marker Thought (loaded_WordNetWords,
    // saved with the network) records it, so a reloaded network skips the import. To bring an
    // edited file into a saved network, delete its marker.
    private void LoadInflectionKnowledge()
    {
        GetUKS();
        foreach (string file in KnowledgeFiles)
        {
            string marker = "loaded_" + Path.GetFileNameWithoutExtension(file);
            if (theUKS.Labeled(marker) is not null) continue;
            theUKS.ImportTextFile(Path.Combine(AppContext.BaseDirectory, "UKSContent", file));
            theUKS.GetOrAddThought(marker, theUKS.GetOrAddThought("loadedKnowledgeFile", "Abstract"));
        }
        spelled = theUKS.Labeled("spelled");
        wordRoot = theUKS.Labeled("word");
        wordForm = theUKS.Labeled("wordForm");
        inflectionRoot = theUKS.Labeled("inflection");
        hasInflection = theUKS.Labeled("hasInflection");
        marksInflection = theUKS.Labeled("marksInflection");
        inflects = theUKS.Labeled("inflects");
        lacksInflection = theUKS.Labeled("lacksInflection");
        ruleRoot = theUKS.Labeled("inflectionRule");
        formsInflection = theUKS.Labeled("formsInflection");
        baseEnding = theUKS.Labeled("baseEnding");
        formEnding = theUKS.Labeled("formEnding");
        closedClass = theUKS.Labeled("closedClass");
        isWildcard = theUKS.Labeled("isWildcard");
        plural = theUKS.Labeled("plural");
        thirdPersonSingular = theUKS.Labeled("thirdPersonSingular");
        verb = theUKS.Labeled("verb");
        readsBackward = theUKS.Labeled("readsBackward");
        avoided = theUKS.Labeled("avoided");
        ruleAttributes.Clear();
    }

    #region Report

    /// <summary>
    /// Everything known about a typed word: what it can be (a stored word or form, or a form the
    /// rules make of a known word), then the forms of each base word found. A word nothing explains
    /// gets the default rules' guesses and its forms as a new noun, verb or adjective.
    /// </summary>
    public string Report(string input)
    {
        if (spelled is null) Initialize();
        string spelling = input.Trim().ToLowerInvariant();
        if (spelling.Length == 0 || !spelling.All(char.IsLetter)) return "Type one word.";

        var report = new StringBuilder();
        List<Analysis> analyses = Analyze(spelling);
        report.AppendLine($"\"{spelling}\" can be:");
        foreach (Analysis analysis in analyses.Where(analysis => analysis.Base is not null))
            report.AppendLine("  " + analysis);
        // Rejected readings, only of words the spelling isn't otherwise a form of.
        List<Analysis> blocked = Blocked(spelling)
            .Where(rejected => !analyses.Any(analysis => ReferenceEquals(analysis.Base, rejected.Base))).ToList();
        foreach (Analysis analysis in blocked) report.AppendLine("  (not " + analysis + ")");

        var bases = analyses.Select(analysis => analysis.Base).OfType<Thought>().Distinct().ToList();
        if (bases.Count == 0)
        {
            report.AppendLine(blocked.Count == 0 ? "  nothing known." : "  no form of a known word.");
            List<Analysis> guesses = Guesses(spelling);
            if (guesses.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("If it is a form of a new word (most likely first, per inflection):");
                foreach (Analysis guess in guesses) report.AppendLine("  " + guess);
            }
            // The forms each default guess would have as a new word of the class its inflection
            // inflects; with no guess, the typed word's own forms as a new noun, verb or adjective.
            var newWords = guesses.GroupBy(guess => guess.Inflection).Select(group => group.First())
                .SelectMany(guess => ClassesInflected(guess.Inflection!).Select(wordClass => (guess.Stem, wordClass)))
                .Distinct().ToList();
            if (newWords.Count == 0)
                newWords = new[] { "noun", "verb", "adjective" }.Select(theUKS.Labeled).OfType<Thought>()
                    .Select(wordClass => (spelling, wordClass)).ToList();
            report.AppendLine();
            report.AppendLine("As a new word (or a new class of a known one):");
            foreach (var (stem, wordClass) in newWords)
                report.AppendLine($"  \"{stem}\" ({wordClass.Label}): " + string.Join(", ",
                    InflectionsOf(wordClass).Select(inflection => $"{inflection.Label} {ApplyRules(stem, inflection, out _) ?? "-"}")));
            return report.ToString();
        }

        foreach (Thought word in bases)
        {
            report.AppendLine();
            report.AppendLine($"Forms of {word.Label} ({string.Join(", ", word.Parents.Select(parent => parent.Label))}), base form \"{StemSpelling(word)}\":");
            if (InflectionsOf(word).Count == 0) report.AppendLine("  no inflections (its class takes none, or lacks them)");
            foreach (Thought inflection in InflectionsOf(word))
            {
                List<(string form, string source)> forms = Forms(word, inflection);
                report.AppendLine($"  {inflection.Label}{Derived(inflection)}: " + (forms.Count == 0 ? "none (closed class, no stored form)" :
                    string.Join(", ", forms.Select(form => $"{form.form} ({form.source})"))));
            }
        }
        return report.ToString();
    }

    /// <summary>One reading of a spelling: a base word (null for a guess) with an inflection (null for the base form).</summary>
    public sealed record Analysis(Thought? Base, string Stem, Thought? Inflection, string Source)
    {
        public override string ToString() =>
            (Inflection is null ? "the base form" : Inflection.Label) + " of " +
            (Base is null ? $"\"{Stem}\"" : Base.Label) + Derived(Inflection) + $"  ({Source})";
    }

    /// <summary>" → mannerAdverb" for a derivation (an inflection that derives a word of another class), else "".</summary>
    public static string Derived(Thought? inflection) =>
        inflection?.LinksTo.FirstOrDefault(link => link.LinkType?.Label.Equals("derives", StringComparison.OrdinalIgnoreCase) == true) is Link link
            ? " → " + link.To.Label : "";

    #endregion

    #region Forward: base word and inflection to form

    /// <summary>
    /// The forms of a base word for an inflection: its stored forms if it has any (an exception
    /// blocks the rules); else, unless its class is closed, what the most specific rule makes.
    /// </summary>
    public List<(string form, string source)> Forms(Thought word, Thought inflection)
    {
        var stored = StoredForms(word, inflection).Select(form => (Spelling(form), "stored")).ToList();
        if (stored.Count > 0) return stored;
        // Transitional: an old-style verb entry is spelled as its third person singular.
        if (ReferenceEquals(inflection, thirdPersonSingular) && StoredForms(word, plural).Any())
            return [(Spelling(word), "stored as the entry")];
        if (word.HasProperty(closedClass)) return [];
        return ApplyRules(StemSpelling(word), inflection, out Thought? rule) is string form ? [(form, rule!.Label)] : [];
    }

    /// <summary>
    /// The most specific rule's form: the deepest rule in the tree forming the inflection whose
    /// base ending the spelling ends with replaces that ending with its form ending.
    /// </summary>
    public string? ApplyRules(string spelling, Thought inflection, out Thought? rule)
    {
        List<Thought> letters = Letters(spelling);
        rule = MostSpecificRule(letters, inflection);
        if (rule is null || MatchEnd(letters, Ending(rule, baseEnding), out List<Thought> rest, out var bindings) is null) return null;
        return Fill(rest, Ending(rule, formEnding), bindings!);
    }

    /// <summary>
    /// Descends the rule tree from the top: at each level, the child rule forming the inflection whose
    /// base ending the spelling has (if siblings both match, the longer ending); stops where no
    /// child matches. A child is an exception to its parent, so the deepest match wins.
    /// </summary>
    private Thought? MostSpecificRule(List<Thought> letters, Thought inflection)
    {
        Thought? rule = null;
        IEnumerable<Thought> candidates = ruleRoot?.Children ?? [];
        while (true)
        {
            Thought? next = null;
            List<Thought>? best = null;
            foreach (Thought candidate in candidates)
            {
                if (!FormsInflection(candidate, inflection)) continue;
                List<Thought> ending = Ending(candidate, baseEnding);
                if (MatchEnd(letters, ending, out _, out _) is null) continue;
                if (best is not null && !IsLonger(ending, best)) continue;
                next = candidate;
                best = ending;
            }
            if (next is null) return rule;
            rule = next;
            candidates = next.Children;
        }
    }

    #endregion

    #region Backward: form to base words

    /// <summary>
    /// What a spelling can be: a stored word (a base word, or a stored form of one), or a form a
    /// rule makes of a known base word. A rule reading counts only if applying the rules forward to
    /// that base gives the spelling back, so a word with a stored form for the inflection, or a
    /// more specific rule, does not take this one ("goed" is not the past of go).
    /// </summary>
    public List<Analysis> Analyze(string spelling)
    {
        var analyses = new List<Analysis>();
        // One spelling is one word: it can be a base word in its own right (w:better: adjective, verb,
        // ...) and a stored form of others (good's comparative) at once.
        foreach (Thought word in ModuleWord.FindWordsBySpelling(theUKS, spelling).Distinct())
        {
            foreach (var (baseWord, inflection) in BasesOf(word))
            {
                // Transitional: an old-style verb's plural form is its stem.
                bool isStem = ReferenceEquals(inflection, plural) && baseWord.HasAncestor(verb);
                Add(new Analysis(baseWord, StemSpelling(baseWord), isStem ? null : inflection, "stored"));
            }
            if (IsBaseWord(word))
            {
                bool isOldStyleVerb = StoredForms(word, plural).Any() && word.HasAncestor(verb);
                Add(new Analysis(word, StemSpelling(word), isOldStyleVerb ? thirdPersonSingular : null, "stored"));
            }
        }
        // Credited to the rule that makes the form forward.
        foreach (var (stem, _, inflection) in Unapplied(spelling))
            foreach (Thought word in BaseWordsSpelled(stem))
                if (InflectionsOf(word).Contains(inflection) &&
                    Forms(word, inflection).FirstOrDefault(form => form.form == spelling) is { form: not null } made)
                    Add(new Analysis(word, stem, inflection, made.source));
        return analyses;

        void Add(Analysis analysis)
        {
            if (!analyses.Any(existing => ReferenceEquals(existing.Base, analysis.Base) && ReferenceEquals(existing.Inflection, analysis.Inflection)))
                analyses.Add(analysis);
        }
    }

    /// <summary>
    /// Rule readings the forward check rejects: the rule would make this spelling from a known word,
    /// but that word has a stored form instead ("goed": go has went) or a more specific rule applies.
    /// </summary>
    public List<Analysis> Blocked(string spelling)
    {
        var blocked = new List<Analysis>();
        foreach (var (stem, rule, inflection) in Unapplied(spelling))
            foreach (Thought word in BaseWordsSpelled(stem))
            {
                if (blocked.Any(existing => ReferenceEquals(existing.Base, word) && ReferenceEquals(existing.Inflection, inflection)))
                    continue;
                // The word is known, but not as a class that takes this inflection ("fetched": fetch is a mass noun).
                if (!InflectionsOf(word).Contains(inflection))
                    blocked.Add(new Analysis(word, stem, inflection,
                        $"{rule.Label}; {string.Join("/", word.Parents.Select(parent => parent.Label))} doesn't take {inflection.Label}"));
                else if (Forms(word, inflection) is { Count: > 0 } forms && !forms.Any(form => form.form == spelling))
                    blocked.Add(new Analysis(word, stem, inflection, $"{rule.Label}; it is {string.Join(" or ", forms.Select(form => form.form))}"));
            }
        return blocked;
    }

    /// <summary>
    /// For a spelling no known word explains: the base it could have, per inflection, most likely
    /// first. A candidate base counts only if the rules run forward from it give the spelling back,
    /// and it is credited to the rule that does (kisses: kiss by sAfterSS, kisse by sDefault). Then a
    /// preferred rule's candidate comes before an avoided one's (readsBackward, inherited), a rule's
    /// before its ancestor's (washes: wash by sAfterSH before washe by sDefault), and of unrelated
    /// rules, the one with the longer form ending.
    /// </summary>
    public List<Analysis> Guesses(string spelling)
    {
        var guesses = new List<(Analysis guess, Thought rule)>();
        foreach (var (stem, _, inflection) in Unapplied(spelling))
        {
            if (guesses.Any(other => other.guess.Stem == stem && ReferenceEquals(other.guess.Inflection, inflection))) continue;
            if (ApplyRules(stem, inflection, out Thought? rule) != spelling) continue;
            var candidate = (new Analysis(null, stem, inflection, rule!.Label + (IsAvoided(rule) ? ", avoided" : "")), rule);
            int before = guesses.FindIndex(other => Precedes(candidate.rule, other.rule));
            guesses.Insert(before < 0 ? guesses.Count : before, candidate);
        }
        return guesses.GroupBy(other => other.guess.Inflection).SelectMany(group => group.Select(other => other.guess)).ToList();

        // Preferred before avoided; then a rule before its ancestor; then, between unrelated rules,
        // the one whose form ending explains more of the spelling (basically: basic by lyAfterIC,
        // not basicall by lyAfterLL).
        bool Precedes(Thought rule, Thought other)
        {
            if (IsAvoided(rule) != IsAvoided(other)) return IsAvoided(other);
            if (rule == other || other.HasAncestor(rule)) return false;
            return rule.HasAncestor(other) || IsLonger(Ending(rule, formEnding), Ending(other, formEnding));
        }
    }

    /// <summary>
    /// Every rule run backward: where the spelling ends with a rule's form ending, the base spelling
    /// is the rest followed by the rule's base ending, with the same letters bound.
    /// </summary>
    private IEnumerable<(string stem, Thought rule, Thought inflection)> Unapplied(string spelling)
    {
        List<Thought> letters = Letters(spelling);
        if (ruleRoot is null) yield break;
        foreach (Thought rule in ruleRoot.Descendants)
        {
            if (MatchEnd(letters, Ending(rule, formEnding), out List<Thought> rest, out var bindings) is null) continue;
            if (Fill(rest, Ending(rule, baseEnding), bindings!) is not string stem || stem.Length == 0) continue;
            foreach (Thought inflection in InflectionsFormed(rule))
                yield return (stem, rule, inflection);
        }
    }

    // The base words whose base form is spelled so: base words spelled so, and base words with a
    // stored form spelled so that is their stem (transitional: w:chase is the stem of w:chases).
    private IEnumerable<Thought> BaseWordsSpelled(string stem) =>
        ModuleWord.FindWordsBySpelling(theUKS, stem)
            .SelectMany(word => BasesOf(word).Select(entry => entry.baseWord).Prepend(word))
            .Where(word => IsBaseWord(word) && StemSpelling(word) == stem)
            .Distinct();

    #endregion

    #region Letters and endings

    /// <summary>
    /// Binds a rule ending to the end of a spelling, last letter first. A letter element must be that
    /// letter; a letter-class wildcard (??.consonant) takes a letter of its classes, and the same
    /// letter wherever it recurs.
    /// </summary>
    /// <returns>The bindings, or null if the spelling does not end so; rest is what precedes the ending.</returns>
    private Dictionary<Thought, Thought>? MatchEnd(List<Thought> letters, List<Thought> ending,
        out List<Thought> rest, out Dictionary<Thought, Thought>? bindings)
    {
        rest = [];
        bindings = new Dictionary<Thought, Thought>();
        using IEnumerator<Thought> letter = Enumerable.Reverse(letters).GetEnumerator();
        foreach (Thought element in Enumerable.Reverse(ending))
        {
            if (!letter.MoveNext() || !Accepts(element, letter.Current, bindings))
            {
                bindings = null;
                return null;
            }
        }
        while (letter.MoveNext()) rest.Add(letter.Current);
        rest.Reverse();
        return bindings;
    }

    private bool Accepts(Thought element, Thought letter, Dictionary<Thought, Thought> bindings)
    {
        if (!element.HasProperty(isWildcard)) return ReferenceEquals(element, letter);
        if (bindings.TryGetValue(element, out Thought? bound)) return ReferenceEquals(bound, letter);
        if (!element.Parents.Where(parent => !parent.HasProperty(isWildcard)).All(a => letter.HasAncestor(a))) return false;
        bindings[element] = letter;
        return true;
    }

    // The letters before an ending followed by another ending, its wildcards replaced by the letters
    // they were bound to. Null if one is unbound.
    private static string? Fill(List<Thought> rest, List<Thought> ending, Dictionary<Thought, Thought> bindings)
    {
        var letters = new List<Thought>(rest);
        foreach (Thought element in ending)
        {
            if (element.Label.StartsWith(UKS.UKS.CharacterPrefix)) letters.Add(element);
            else if (bindings.TryGetValue(element, out Thought? letter)) letters.Add(letter);
            else return null;
        }
        return string.Concat(letters.Select(Character));
    }

    // Whether one ending is longer than another, found by walking both together.
    private static bool IsLonger(List<Thought> ending, List<Thought> other)
    {
        using IEnumerator<Thought> mine = ending.GetEnumerator(), theirs = other.GetEnumerator();
        while (true)
        {
            bool hasMine = mine.MoveNext(), hasTheirs = theirs.MoveNext();
            if (!hasTheirs) return hasMine;
            if (!hasMine) return false;
        }
    }

    // A rule's base or form ending; no base ending is the empty ending (nothing is replaced).
    private List<Thought> Ending(Thought rule, Thought? endingType) =>
        rule.GetTargetOfFirstLinkOfType(endingType!) is SeqElement head
            ? theUKS.EnumerateSequenceElements(head).Select(element => element.VLU).ToList()
            : [];

    private List<Thought> Letters(string spelling) => spelling.Select(theUKS.GetOrAddCharacter).ToList();

    private static char Character(Thought letter) => char.ToLowerInvariant(letter.Label[^1]);

    private string Spelling(Thought word) =>
        word.GetTargetOfFirstLinkOfType(spelled!) is SeqElement head
            ? string.Concat(theUKS.EnumerateSequenceElements(head).Select(element => Character(element.VLU)))
            : word.Label;

    #endregion

    #region Words, inflections and rules

    // A rule's attributes with inheritance: what it forms (an ancestor's, unless overridden by
    // formsInflection.not) and its backward preference (exclusive: its own overrides). Only the
    // rule's short ancestry is read, once per rule; cleared when the knowledge is reloaded.
    private List<Link> RuleAttributes(Thought rule)
    {
        if (!ruleAttributes.TryGetValue(rule, out List<Link>? attributes))
            ruleAttributes[rule] = attributes = theUKS.GetAttributes(rule).ToList();
        return attributes;
    }

    private IEnumerable<Thought> InflectionsFormed(Thought rule) =>
        RuleAttributes(rule).Where(link => link.LinkType == formsInflection).Select(link => link.To);

    private bool FormsInflection(Thought rule, Thought inflection) => InflectionsFormed(rule).Contains(inflection);

    private bool IsAvoided(Thought rule) =>
        RuleAttributes(rule).Any(link => link.LinkType == readsBackward && link.To == avoided);

    /// <summary>
    /// The inflections a word (or word class) takes: those that one of its classes takes, a class
    /// taking an inflection that inflects it unless that class lacks it. A word of several classes
    /// (w:good: adjective and noun; w:china: commonNoun and properNoun) takes the inflections of
    /// each, so one class lacking an inflection doesn't stop another taking it.
    /// </summary>
    public List<Thought> InflectionsOf(Thought word)
    {
        // A word has a spelling and its parents are its classes; a class (noun) is taken as itself.
        List<Thought> classes = word.GetTargetOfFirstLinkOfType(spelled!) is not null ? word.Parents.ToList() : [word];
        return inflectionRoot?.Descendants
            .Where(inflection => classes.Any(wordClass =>
                inflection.LinksTo.Any(link => link.LinkType == inflects && wordClass.HasAncestor(link.To)) &&
                !inflection.LinksFrom.Any(link => link.LinkType == lacksInflection && wordClass.HasAncestor(link.From))))
            .ToList() ?? [];
    }

    // The word classes an inflection inflects (plural: noun).
    private IEnumerable<Thought> ClassesInflected(Thought inflection) =>
        inflection.LinksTo.Where(link => link.LinkType == inflects).Select(link => link.To);

    // A word's stored forms for an inflection ([w:goes->hasPastTense->w:went]).
    private IEnumerable<Thought> StoredForms(Thought word, Thought? inflection) =>
        word.LinksTo.Where(link => link.LinkType?.HasAncestor(hasInflection) == true &&
            link.LinkType.GetTargetOfFirstLinkOfType(marksInflection!) == inflection).Select(link => link.To);

    // The base words a stored form belongs to, each with the inflection it is (axes: of axe and of
    // axis; better: good's comparative).
    private IEnumerable<(Thought baseWord, Thought inflection)> BasesOf(Thought form) =>
        form.LinksFrom.Where(link => link.LinkType?.HasAncestor(hasInflection) == true)
            .Select(link => (link.From, inflection: link.LinkType.GetTargetOfFirstLinkOfType(marksInflection!)))
            .Where(entry => entry.inflection is not null)!;

    // A base word: a word with a class of its own besides wordForm (w:better is one; w:mice is not).
    private bool IsBaseWord(Thought word) => word.Parents.Any(parent => !ReferenceEquals(parent, wordForm));

    // The spelling rules start from. Transitional: an old-style verb's stem is its stored plural form.
    private string StemSpelling(Thought word) =>
        word.HasAncestor(verb) && StoredForms(word, plural).FirstOrDefault() is Thought stem ? Spelling(stem) : Spelling(word);

    #endregion
}

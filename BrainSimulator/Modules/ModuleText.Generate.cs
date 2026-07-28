/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License. You may use, copy, modify, merge, publish, distribute,
 * sublicense, and/or sell copies of this software under the terms of
 * the MIT License.
 *
 * See the LICENSE file in the project root for full license information.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

/// <summary>
/// Says in English what the UKS knows.
///
/// This is the same operation as understanding a phrase, run in the other
/// direction. Understanding takes a template and a phrase and reads a
/// relationship out of it; saying takes a template and a relationship and writes
/// the phrase. Both use the learned templates and the actions those templates
/// perform, so nothing here needs its own grammar.
///
/// In particular there is no rule anywhere about "a" before a consonant and "an"
/// before a vowel, and none about singular and plural agreement. Those come out
/// right because the corpus taught them as separate templates whose positions
/// accept different words, and a template is chosen by which words it accepts.
/// A rule of that kind appearing in this file would mean the approach had been
/// abandoned.
/// </summary>
public partial class ModuleText
{
    /// <summary>
    /// One way of saying a relationship, with the evidence for preferring it.
    /// </summary>
    private sealed class Rendering
    {
        public List<string> Words;
        public int OpenPositions;
        public int WordsInPlace;
        public int BaseForms;
        public int Evidence;
        public string TemplateLabel;
    }

    /// <summary>
    /// Expresses one relationship as an English phrase, or returns null when
    /// nothing learned can say it.
    /// </summary>
    public static string DescribeRelationship(Link relationship)
    {
        var theUKS = MainWindow.theUKS;
        if (relationship?.From is null || relationship.LinkType is null || relationship.To is null)
            return null;

        Thought hasWords = theUKS.Labeled("hasWords");
        Thought templateRoot = theUKS.Labeled("LearnedTemplate");
        if (hasWords is null || templateRoot is null) return null;

        // "Unknown" is where the UKS files a Thought whose parent it does not
        // know. Saying "birds are Unknown" would report the absence of knowledge
        // as though it were knowledge.
        if (IsPlaceholder(relationship.From) || IsPlaceholder(relationship.To)) return null;

        // A meaning may be sayable as more than one word -- "dog" and "dogs"
        // both denote the same thing once their number relation is known -- and
        // which one is right depends on the template that ends up being used.
        List<Thought> subjectWords = WordsForMeaning(relationship.From);
        List<Thought> objectWords = WordsForMeaning(relationship.To);
        if (subjectWords.Count == 0 || objectWords.Count == 0) return null;

        List<Rendering> renderings = new();
        foreach (Thought template in templateRoot.Children)
        {
            Link action = template.LinksTo
                .Where(link => link.LinkType?.Label == "means")
                .Select(link => link.To)
                .OfType<Link>()
                .FirstOrDefault(candidate => candidate.LinkType?.HasAncestor("SET") == true &&
                    candidate.LinkType.HasAncestor(relationship.LinkType));
            if (action?.From is null || action.To is null) continue;

            SequenceView sequence = theUKS.GetSequenceViews(template)
                .FirstOrDefault(view => view.LinkType == hasWords);
            if (sequence is null) continue;

            List<Thought> elements = sequence.Elements.ToList();
            int sourcePosition = elements.IndexOf(action.From);
            int targetPosition = elements.IndexOf(action.To);
            if (sourcePosition < 0 || targetPosition < 0) continue;

            if (!TryChooseWord(elements[sourcePosition], subjectWords,
                out Thought subjectWord, out bool subjectInPlace)) continue;
            if (!TryChooseWord(elements[targetPosition], objectWords,
                out Thought objectWord, out bool objectInPlace)) continue;

            Rendering rendering = Render(
                elements, sourcePosition, targetPosition, subjectWord, objectWord);
            if (rendering is null) continue;

            rendering.WordsInPlace = (subjectInPlace ? 1 : 0) + (objectInPlace ? 1 : 0);
            rendering.BaseForms = (IsBaseForm(subjectWord) ? 1 : 0) + (IsBaseForm(objectWord) ? 1 : 0);
            rendering.Evidence = template.LinksTo.Count(link => link.LinkType?.Label == "evidence");
            rendering.TemplateLabel = template.Label;
            renderings.Add(rendering);
        }

        // A template which keeps both ends open says something about the two
        // Thoughts given to it. One which has frozen an end says something about
        // whatever was frozen there, however often it was observed, so openness
        // is weighed before anything else.
        // Openness comes first, then whether the template has actually been seen
        // to accept these words -- that is what keeps a template whose positions
        // hold consonant-initial words from being used to say "a animal". Only
        // once those agree does the form the other forms were derived from win,
        // which is why a fact is stated in the singular where either would do.
        Rendering best = renderings
            .OrderByDescending(candidate => candidate.OpenPositions)
            .ThenByDescending(candidate => candidate.WordsInPlace)
            .ThenByDescending(candidate => candidate.BaseForms)
            .ThenByDescending(candidate => candidate.Evidence)
            .ThenBy(candidate => candidate.TemplateLabel, StringComparer.Ordinal)
            .FirstOrDefault();
        return best is null ? null : string.Join(" ", best.Words);
    }

    /// <summary>
    /// Reads the phrases already observed and asserts what they say.
    ///
    /// Loading a corpus stores phrases so their structure can be learned, but
    /// storing a phrase is not the same as believing it. Until the templates
    /// exist there is nothing to understand a phrase with; once they do, every
    /// phrase already seen can be read again and its relationship asserted.
    /// That is what makes the corpus knowledge rather than a pile of sentences.
    ///
    /// Only templates which perform an action can do this, and a phrase is only
    /// read by a template whose positions accept its words, so nothing is
    /// asserted on the strength of a frame that merely happens to be the right
    /// length.
    /// </summary>
    /// <returns>The number of phrases which were understood.</returns>
    public static int UnderstandStoredPhrases() => UnderstandStoredPhrases(null);

    /// <summary>
    /// As above, reporting each phrase read and what it was taken to assert.
    /// </summary>
    public static int UnderstandStoredPhrases(Action<string> report)
    {
        var theUKS = MainWindow.theUKS;
        Thought templateRoot = theUKS.Labeled("LearnedTemplate");
        Thought hasWords = theUKS.Labeled("hasWords");
        if (templateRoot is null || hasWords is null) return 0;

        // Each template with an action, kept with the words each position takes.
        List<(Thought template, List<Thought> elements, List<HashSet<Thought>> accepted)>
            readers = new();
        foreach (Thought template in templateRoot.Children)
        {
            bool performsAction = template.LinksTo.Any(link => link.LinkType?.Label == "means" &&
                link.To is Link action && action.LinkType?.HasAncestor("SET") == true);
            if (!performsAction) continue;

            SequenceView sequence = theUKS.GetSequenceViews(template)
                .FirstOrDefault(view => view.LinkType == hasWords);
            if (sequence is null) continue;

            List<Thought> elements = sequence.Elements.ToList();
            readers.Add((template, elements, elements
                .Select(element => IsOpenPosition(element)
                    ? SlotMembers(element).ToHashSet()
                    : new HashSet<Thought> { element })
                .ToList()));
        }
        if (readers.Count == 0) return 0;

        int understood = 0;
        foreach (Thought phrase in GetPhrasesOfKind("Statement"))
        {
            SequenceView spoken = theUKS.GetSequenceViews(phrase)
                .FirstOrDefault(view => view.LinkType == hasWords);
            if (spoken is null) continue;

            foreach (var reader in readers)
            {
                if (reader.elements.Count != spoken.Elements.Count) continue;
                bool fits = true;
                for (int position = 0; position < reader.elements.Count && fits; position++)
                    fits = reader.accepted[position].Contains(spoken.Elements[position]);
                if (!fits) continue;

                Link asserted = ApplyLearnedTemplateAction(reader.template, phrase);
                if (asserted is not null) understood++;
                report?.Invoke(string.Join(' ', spoken.Elements.Select(WordLabel)) +
                    $"   --{reader.template.Label}-->   " +
                    (asserted is null
                        ? "(nothing)"
                        : $"[{asserted.From?.Label} -{asserted.LinkType?.Label}-> {asserted.To?.Label}]"));
                break;
            }
        }
        return understood;
    }

    /// <summary>
    /// Says everything known about one Thought, a phrase for each relationship.
    ///
    /// A relationship which no learned template can say is passed over rather
    /// than forced into words. That also keeps the machinery of the UKS out of
    /// the account: the links which hold a phrase together, or record where a
    /// template found its evidence, match no template and so are never said.
    /// </summary>
    public static List<string> DescribeThought(Thought subject)
    {
        List<string> retVal = new();
        if (subject is null) return retVal;

        foreach (Link relationship in subject.LinksTo
            .Where(link => link.From is not null && link.LinkType is not null && link.To is not null)
            .OrderBy(link => link.LinkType.Label, StringComparer.Ordinal)
            .ThenBy(link => link.To.Label, StringComparer.Ordinal))
        {
            // The same fact may be recorded both as an action and as the
            // relationship that action wrote, and both say the same phrase.
            string said = DescribeRelationship(relationship);
            if (said is not null && !retVal.Contains(said)) retVal.Add(said);
        }
        return retVal;
    }

    /// <summary>
    /// Explains why nothing was said about a word, so that silence can be told
    /// apart from a corpus which was never loaded, a word which was never seen,
    /// and a Thought which is known but which no learned phrase can express.
    /// </summary>
    public static string ExplainNothingSaid(string label)
    {
        var theUKS = MainWindow.theUKS;
        if (string.IsNullOrWhiteSpace(label))
            return "Type a question, or the name of one thing.";

        Thought templateRoot = theUKS.Labeled("LearnedTemplate");
        int actionTemplates = templateRoot?.Children.Count(template =>
            template.LinksTo.Any(link => link.LinkType?.Label == "means" &&
                link.To is Link action && action.LinkType?.HasAncestor("SET") == true)) ?? 0;
        if (actionTemplates == 0)
            return "Nothing has been learned to say things with yet. " +
                "Load a corpus -- pressing Load until it reports the file is complete -- " +
                "then press Process.";

        string name = label.Trim().ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
            ?.Trim('.', ',', '!', '?', '"', '\'') ?? "";
        Thought subject = theUKS.Labeled(name);
        if (theUKS.Labeled("w:" + name)?.GetTargetOfFirstLinkOfType("means") is Thought meant)
            subject = meant;
        if (subject is null)
            return $"'{name}' has never been seen.";

        List<Link> relationships = subject.LinksTo
            .Where(link => link.To is not null && !IsPlaceholder(link.To))
            .ToList();
        if (relationships.Count == 0)
            return $"'{name}' has been read but nothing has been understood about it. " +
                "Press Process after loading the whole file.";

        return $"'{name}' has {relationships.Count} relationships, but none of them " +
            "fits a phrase which has been learned. " +
            string.Join("  ", relationships.Take(2).Select(DiagnoseRelationship));
    }

    /// <summary>
    /// Reports why one relationship can or cannot be put into words, naming the
    /// position which refused it. Written for the case where the knowledge is
    /// plainly there but nothing comes out.
    /// </summary>
    public static string DiagnoseRelationship(Link relationship)
    {
        var theUKS = MainWindow.theUKS;
        if (relationship?.From is null || relationship.LinkType is null || relationship.To is null)
            return "(incomplete relationship)";

        string what = $"[{relationship.From.Label} -{relationship.LinkType.Label}-> " +
            $"{relationship.To.Label}]";
        Thought hasWords = theUKS.Labeled("hasWords");
        Thought templateRoot = theUKS.Labeled("LearnedTemplate");
        if (hasWords is null || templateRoot is null) return $"{what}: nothing has been learned.";

        List<Thought> subjectWords = WordsForMeaning(relationship.From);
        List<Thought> objectWords = WordsForMeaning(relationship.To);

        // Falling back to the meaning itself means nothing was found to say it
        // with, which silences every relationship at once and is worth naming
        // rather than leaving to be inferred from a missing prefix.
        string unnamed = string.Join(" and ", new[]
        {
            subjectWords.Contains(relationship.From) ? relationship.From.Label : null,
            objectWords.Contains(relationship.To) ? relationship.To.Label : null,
        }.Where(name => name is not null));
        if (unnamed.Length > 0)
            return $"{what}: no word denotes {unnamed}, so there is nothing to say it with. " +
                "The 'means' links between words and what they denote are missing.";

        string saying = $"{what} would be said with " +
            $"{string.Join("/", subjectWords.Select(w => w.Label))} and " +
            $"{string.Join("/", objectWords.Select(w => w.Label))}";

        int matchingActions = 0;
        List<string> refusals = new();
        foreach (Thought template in templateRoot.Children)
        {
            Link action = template.LinksTo
                .Where(link => link.LinkType?.Label == "means")
                .Select(link => link.To).OfType<Link>()
                .FirstOrDefault(candidate => candidate.LinkType?.HasAncestor("SET") == true &&
                    candidate.LinkType.HasAncestor(relationship.LinkType));
            if (action?.From is null || action.To is null) continue;
            matchingActions++;

            SequenceView sequence = theUKS.GetSequenceViews(template)
                .FirstOrDefault(view => view.LinkType == hasWords);
            if (sequence is null) continue;
            List<Thought> elements = sequence.Elements.ToList();
            int sourcePosition = elements.IndexOf(action.From);
            int targetPosition = elements.IndexOf(action.To);
            if (sourcePosition < 0 || targetPosition < 0)
            {
                refusals.Add($"{template.Label}: its action is not in its own words");
                continue;
            }

            if (!TryChooseWord(elements[sourcePosition], subjectWords, out _, out _))
                refusals.Add($"{template.Label} position {sourcePosition} accepts " +
                    Accepts(elements[sourcePosition]));
            else if (!TryChooseWord(elements[targetPosition], objectWords, out _, out _))
                refusals.Add($"{template.Label} position {targetPosition} accepts " +
                    Accepts(elements[targetPosition]));
        }

        if (matchingActions == 0)
            return $"{what}: no learned phrase performs '{relationship.LinkType.Label}'.";
        if (refusals.Count == 0)
            return $"{what}: can be said, as \"{DescribeRelationship(relationship)}\".";
        return $"{saying}; {string.Join("; ", refusals.Take(2))}.";

        static string Accepts(Thought element)
        {
            if (!IsOpenPosition(element)) return $"only '{element.Label}'";
            List<Thought> members = SlotMembers(element);
            return members.Count == 0
                ? "nothing (its class is empty)"
                : string.Join("/", members.Take(5).Select(m => m.Label)) +
                    (members.Count > 5 ? $" and {members.Count - 5} more" : "");
        }
    }

    /// <summary>
    /// Says everything known about the Thought a word denotes. The word may be
    /// written as it would be spoken -- "a dog", "Dogs." -- since what is wanted
    /// is an account of the thing, not of the phrasing.
    /// </summary>
    public static List<string> DescribeThought(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return new List<string>();

        char[] trimChars = { '.', ',', ';', ':', '!', '?', '"', '\'' };
        string[] spoken = label.ToLowerInvariant()
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim(trimChars))
            .Where(part => part.Length > 0)
            .ToArray();
        if (spoken.Length == 0) return new List<string>();

        // The thing named is the last word: an article or a quality in front of
        // it describes it rather than replacing it.
        for (int start = 0; start < spoken.Length; start++)
        {
            List<string> account = DescribeNamedThought(spoken[^(start + 1)]);
            if (account.Count > 0) return account;
        }
        return new List<string>();
    }

    /// <summary>
    /// Says everything known about the Thought one word names, whether the word
    /// given is the thing itself or the word which denotes it.
    /// </summary>
    private static List<string> DescribeNamedThought(string name)
    {
        var theUKS = MainWindow.theUKS;
        Thought subject = theUKS.Labeled(name);

        // What was typed may be the word rather than what it denotes, and a
        // plural denotes the same thing as its singular.
        if (theUKS.Labeled("w:" + name)?.GetTargetOfFirstLinkOfType("means") is Thought meant)
            subject = meant;
        return DescribeThought(subject);
    }

    /// <summary>
    /// Writes out one template with the two Thoughts placed in it. Returns null
    /// when the template holds a position which this relationship says nothing
    /// about, since there would be no word to put there.
    /// </summary>
    private static Rendering Render(
        List<Thought> elements,
        int sourcePosition,
        int targetPosition,
        Thought subjectWord,
        Thought objectWord)
    {
        List<string> words = new();
        int openPositions = 0;
        for (int position = 0; position < elements.Count; position++)
        {
            if (position == sourcePosition)
            {
                words.Add(WordLabel(subjectWord));
                if (IsOpenPosition(elements[position])) openPositions++;
                continue;
            }
            if (position == targetPosition)
            {
                words.Add(WordLabel(objectWord));
                if (IsOpenPosition(elements[position])) openPositions++;
                continue;
            }
            // "the quiet dog can run" has a position for a quality which the
            // relationship being said does not supply.
            if (IsOpenPosition(elements[position])) return null;
            words.Add(WordLabel(elements[position]));
        }
        return new Rendering { Words = words, OpenPositions = openPositions };
    }

    /// <summary>
    /// Picks the word to put at one position. An open position takes whichever
    /// form of the meaning it has been seen to accept; a position frozen to a
    /// single value can only be used when that value is itself a way of saying
    /// the meaning.
    ///
    /// A position which accepts none of the forms available makes its template
    /// unusable rather than taking a form anyway. Putting an unaccepted word in
    /// a position is what produces "bird are animal": a plural frame filled with
    /// singular words. Saying nothing is better than saying it wrongly.
    /// </summary>
    private static bool TryChooseWord(
        Thought element,
        List<Thought> candidates,
        out Thought chosen,
        out bool accepted)
    {
        chosen = null;
        accepted = false;
        if (!IsOpenPosition(element))
        {
            if (!candidates.Contains(element)) return false;
            chosen = element;
            accepted = true;
            return true;
        }

        HashSet<Thought> members = SlotMembers(element).ToHashSet();
        chosen = candidates.FirstOrDefault(members.Contains);
        if (chosen is null) return false;

        accepted = true;
        return true;
    }

    private static bool IsOpenPosition(Thought element) => element.HasAncestor("Wildcard");

    /// <summary>
    /// Whether a Thought is one the UKS keeps for its own bookkeeping rather
    /// than something there is anything to say about.
    /// </summary>
    private static bool IsPlaceholder(Thought thought) =>
        thought is null || thought.Label.Equals("Unknown", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a word is the form others were derived from, rather than one
    /// derived from it.
    /// </summary>
    private static bool IsBaseForm(Thought word) =>
        word.GetTargetOfFirstLinkOfType("pluralOf") is null;

    /// <summary>
    /// Whether a Thought is a word of the language.
    ///
    /// Membership of the Word class is the usual sign, but a Thought which
    /// carries the spelling prefix is a word whatever else has become of its
    /// parentage. Requiring the class alone left a UKS where every word had been
    /// reparented unable to say anything at all, while insisting the knowledge
    /// was there -- which it was.
    /// </summary>
    private static bool IsWord(Thought candidate) =>
        candidate.HasAncestor("Word") ||
        candidate.Label.StartsWith("w:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The words which denote a meaning, the form the other forms were derived
    /// from first. When nothing denotes it, the meaning stands for itself so
    /// that something can still be said.
    /// </summary>
    private static List<Thought> WordsForMeaning(Thought meaning)
    {
        List<Thought> words = meaning.LinksFrom
            .Where(link => link.LinkType?.Label == "means" && link.From is not null)
            .Select(link => link.From)
            .Where(IsWord)
            .Distinct()
            .OrderBy(candidate => candidate.GetTargetOfFirstLinkOfType("pluralOf") is null ? 0 : 1)
            .ThenBy(candidate => candidate.Label, StringComparer.Ordinal)
            .ToList();
        if (words.Count == 0) words.Add(meaning);
        return words;
    }
}

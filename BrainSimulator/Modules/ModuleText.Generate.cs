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
    /// Says everything known about the Thought a word denotes.
    /// </summary>
    public static List<string> DescribeThought(string label)
    {
        var theUKS = MainWindow.theUKS;
        if (string.IsNullOrWhiteSpace(label)) return new List<string>();

        string trimmed = label.Trim().ToLowerInvariant();
        Thought subject = theUKS.Labeled(trimmed);

        // What was typed may be the word rather than what it denotes.
        if (theUKS.Labeled("w:" + trimmed)?.GetTargetOfFirstLinkOfType("means") is Thought meant)
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
        if (chosen is not null)
        {
            accepted = true;
            return true;
        }

        // Nothing observed here matches, so the phrase would be a guess. Say it
        // anyway with the plainest form, but rank it below any template which
        // has actually accepted these words.
        chosen = candidates[0];
        return true;
    }

    private static bool IsOpenPosition(Thought element) => element.HasAncestor("Wildcard");

    /// <summary>
    /// Whether a word is the form others were derived from, rather than one
    /// derived from it.
    /// </summary>
    private static bool IsBaseForm(Thought word) =>
        word.GetTargetOfFirstLinkOfType("pluralOf") is null;

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
            .Where(candidate => candidate.HasAncestor("Word"))
            .Distinct()
            .OrderBy(candidate => candidate.GetTargetOfFirstLinkOfType("pluralOf") is null ? 0 : 1)
            .ThenBy(candidate => candidate.Label, StringComparer.Ordinal)
            .ToList();
        if (words.Count == 0) words.Add(meaning);
        return words;
    }
}

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



using Microsoft.Windows.Themes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

public class ModuleTextNEW : ModuleBase
{
    // Module organization:

    private const float PhraseObservationDecayFactor = 0.9999f;
    private const float PhraseObservationIncrease = 1f;
    private const int PlasticPhraseCapacity = 50;


    public static ISet<string> BlockedMeaningLabels { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Object",
            "Thought",
            "Unknown",
            "mentalModel",
            "Abstract",
            "activeThought",
            "imaginedThought",
            "inActiveThought",
            "attention",
        };

    public string LastAnswer { get; private set; } = string.Empty;
    public string LastStatus { get; private set; } = "Ready";
    public Link LastRelationship { get; private set; }
    public Thought LastTemplate { get; private set; }
    public Thought LastActionExemplar { get; private set; }
    public int LastLoadedActionExemplarCount { get; private set; }
    public int LastRetainedActionExemplarCount { get; private set; }
    public string LastMissingActionExemplars { get; private set; } = string.Empty;
    private int _trainingPhrasesPerConsolidation = 10;
    private int _trainingPhrasesAwaitingConsolidation;
    private readonly object _consolidationStateLock = new();


    /// <summary>Initializes the module when first fired and refreshes its dialog on subsequent engine cycles.</summary>
    public override void Fire()
    {
        Init();

        UpdateDialog();
    }

    /// <summary>Provides the module initialization hook; language state is currently created on demand.</summary>
    public override void Initialize()
    {
    }

    /// <summary>Creates the fixed language structure whenever the UKS is initialized.</summary>
    public override void UKSInitializedNotification()
    {
        (string label, string parent)[] fixedThoughts =
        {
            ("LanguageElement", "Thought"),
            ("Language", "LanguageElement"),
            ("Word", "LanguageElement"),
            ("Phrase", "LanguageElement"),
            ("LearnedClass", "LanguageElement"),
            ("LearnedTemplate", "LanguageElement"),
            ("ActionExemplar", "LanguageElement"),
            ("SpellingPattern", "LanguageElement"),
            ("TEST", "LinkType"),
            ("SET", "LinkType"),
            ("hasWords", "LinkType"),
            ("hasAction", "LinkType"),
            ("means", "LinkType"),
            ("usedInLanguage", "LinkType"),
            ("evidence", "LinkType"),
            ("demonstrates", "LinkType"),
            ("linkTypeParameter", "LinkType"),
            ("position", "LinkType"),
            ("adds", "LinkType"),
            ("classEvidence", "LinkType"),
            ("correspondsTo", "LinkType"),
            ("sourceClass", "LinkType"),
            ("targetClass", "LinkType"),
            ("filterBy", "LinkType"),
            ("beginning", "Unknown"),
            ("end", "Unknown"),
        };
        foreach ((string label, string parent) in fixedThoughts)
            theUKS.GetOrAddThought(label, parent);
    }

    // 1. Reading Language In
    /// <summary>
    /// Stores and interprets one raw line from typed or file input. Embedded
    /// actions and comments are separated before the command is processed.
    /// </summary>
    public string SubmitText(string text, bool answerQueries = true)
    {
        LastAnswer = string.Empty;
        LastRelationship = null;
        LastTemplate = null;
        LastActionExemplar = null;
        ParseInputLine(text, out string command, out Link action, out string comment);
        if (string.IsNullOrEmpty(command)) return "The phrase is empty.";
        theUKS.GetOrAddThought("hasPossibleAction", "LinkType");

        //Save the phrase (and its word sequence) in the UKS. This is the only step that is always performed.
        //The phrase (and words) may be forgotten later if they are not reinforced by a meaning or exemplar.
        string ingestStatus = AddPhrase(command, out Thought phrase);
        if (ingestStatus.StartsWith("Error:", StringComparison.Ordinal))
        {
            LastStatus = ingestStatus;
            return null;
        }
        //if there's an action, link it to the phrase.
        if (action is not null) phrase.AddLink("hasAction", action);

        //Does this input match an existing template?
        SeqElement phraseContent = (SeqElement) phrase.GetTargetOfFirstLinkOfType("hasWords");
        var matchedTemplateStruct = theUKS.FindSequencesByActivation(phraseContent, "TemplateLearningSearch");
        matchedTemplateStruct.RemoveAll(match => match.seqNode == phraseContent);  //remove any matches which are the phrase itself.
        Thought templateContent = null;
        if (matchedTemplateStruct.Count > 0) templateContent = matchedTemplateStruct[0].seqNode;
        Thought theTemplate = templateContent?.GetSourceOfFirstLinkOfType("hasWords") as Thought;

        //If we found a template, be sure to add any found words to wildcard classes
        if (theTemplate is not null)
        {
            var wordBindings = theUKS.GetSequenceBindings(templateContent as SeqElement, phraseContent);
            foreach (var kvp in wordBindings)
            {
                kvp.Value.AddParent(kvp.Key.Parents.First(x=>x.Label != "Wildcard"));
            }
        }

        //There is no matching template.  Create a new one if possible
        if (templateContent is null) //it just found itself.
        {
            Thought phraseRoot = theUKS.Labeled("Phrase");
            SeqElement phraseSequence = phrase.GetTargetOfFirstLinkOfType("hasWords") as SeqElement;
            foreach (Thought child in phraseRoot.Children)
            {
                if (child == phrase) continue;
                theTemplate = theUKS.CreateNewTemplate(phraseSequence, child.GetTargetOfFirstLinkOfType("hasWords") as SeqElement);
                if (theTemplate is not null)
                {
                    templateContent = theTemplate.GetTargetOfFirstLinkOfType("hasWords");  //get the template itself, not the exemplar.
                    if (action is not null) theTemplate.AddLink("hasPossibleAction", action);
                    Thought childAction = child.GetTargetOfFirstLinkOfType("hasAction");
                    if (childAction is not null) theTemplate.AddLink("hasPossibleAction", childAction);
                    break;
                }
            }
        }

        //If a template was found, get the linked action and perform it.
        if (templateContent is not null)
        {
            var wordBindings = theUKS.GetSequenceBindings(templateContent as SeqElement, phrase.GetTargetOfFirstLinkOfType("hasWords") as SeqElement);
            if (action is not null)
            {
                Link mappedAction = CreateMappedLinkForTemplate(action, wordBindings);
                if (mappedAction is not null)
                    theTemplate?.AddLink("hasAction", mappedAction);
                else
                {//we couldn't build a mapped action so we'll just link the original action to the template as a possible action.
                    theTemplate?.AddLink("hasPossibleAction", action);
                    ConsolidateMeanings(theTemplate);
                }
            }

            if (theTemplate is not null)
            {            //Link any action to the template (and perform the action)
                Link matchedTemplateAction = (Link)theTemplate.GetTargetOfFirstLinkOfType("hasAction");
                if (matchedTemplateAction is not null)
                {
                    var specificAction = CreateMappedLink(matchedTemplateAction, wordBindings);
                    if (specificAction is not null)
                        theUKS.ApplyTestOrSetAction(specificAction);
                    //if the method was a query, generate an answer
                }
            }
        }

        return "OK";

        void ConsolidateMeanings(Thought template)
        {
            //count the number of HasPossibleAction links to the template.
            //If there are more than 1, we need to consolidate them into a single action with a filterBy link.
            int count = template.LinksTo.Count(link => link.LinkType?.Label == "hasPossibleAction");
            if (count <= 1) return;

            List<(Thought thePhrase,Dictionary<Thought,Thought> wordBindings,Link theAction)> phrases = new();
            //For each hasPossibleAction target under the template, recover its originating phrase by following the existing hasAction / reverse link path.
            foreach (Link action1 in template.LinksTo.Where(link => link.LinkType?.Label == "hasPossibleAction"))
            {
                Thought phrase = action1.To.GetSourceOfFirstLinkOfType("hasAction");
                if (phrase is not null)
                {
                    var wordBindings = theUKS.GetSequenceBindings(template.GetTargetOfFirstLinkOfType("hasWords") as SeqElement, phrase.GetTargetOfFirstLinkOfType("hasWords") as SeqElement);
                    phrases.Add((phrase, wordBindings, (Link)action1.To));
                }
            }
            //special case for 2
            if (count == 2  && phrases[0].wordBindings.Count == 1) 
            {
                if (phrases[0].theAction.From != phrases[1].theAction.From)
                {
                    phrases[0].wordBindings.First().Value.AddLink("means", phrases[0].theAction.From);
                    phrases[1].wordBindings.First().Value.AddLink("means", phrases[1].theAction.From);
                }
            }


            //Compare all possible actions recursively.
            //For each varying action component,
            //inspect the corresponding phrases against the phrase template and find which wildcard position varies in the same exemplar - by - exemplar pattern.
            //Create the means links from each wildcard filler word to the corresponding concrete action component as needed.
            //Replace that varying action component with the wildcard - derived parameter.
            //Leave invariant action components unchanged.
            //Attach the resulting generalized action to the template as the executable action template.
        }

        Link CreateMappedLink(Link action, Dictionary<Thought, Thought> wordBindings)
        {
            // GENERALIZE to complex Links (e.g., with filterBy) if needed.  For now, just map the From and To.
            if (action is null || wordBindings is null) return null;

            Thought mappedFrom = wordBindings.ContainsKey(action.From) ? wordBindings[action.From].GetTargetOfFirstLinkOfType("means") : action.From;
            Thought mappedTo = wordBindings.ContainsKey(action.To) ? wordBindings[action.To].GetTargetOfFirstLinkOfType("means") : action.To;

            Link mappedLink = new Link(mappedFrom, action.LinkType, mappedTo);
            return mappedLink;
        }
        Link CreateMappedLinkForTemplate(Link action, Dictionary<Thought, Thought> wordBindings)
        {
            if (action is null || wordBindings is null) return null;
            Dictionary<Thought, Thought> thoughtBindings = new Dictionary<Thought, Thought>();
            foreach (var kvp in wordBindings) thoughtBindings[kvp.Value] = kvp.Key;

            Thought from = action.From.LinksFrom.FirstOrDefault(link => link.LinkType?.Label == "means")?.From;
            if (from is null) from = theUKS.Labeled("w:" + action.From.Label);
            Thought to = action.To.LinksFrom.FirstOrDefault(link => link.LinkType?.Label == "means")?.From;
            if (to is null) to = theUKS.Labeled("w:" + action.To.Label);
            if (from is null || to is null) return null;

            Thought mappedFrom = thoughtBindings.ContainsKey(from) ? thoughtBindings[from] : from;
            Thought mappedTo = thoughtBindings.ContainsKey(to) ? thoughtBindings[to] : to;
            Link mappedLink = new Link(mappedFrom, action.LinkType, mappedTo);
            return mappedLink;
        }
    }

    /// <summary>
    /// Separates a raw input line into its command, optional bracketed action,
    /// and optional // comment. Returned strings are always trimmed.
    /// </summary>
    internal static void ParseInputLine(string textIn, out string command, out Link action, out string comment)
    {
        //Find trailing comment
        string remainingText = textIn ?? string.Empty;
        int commentIndex = remainingText.IndexOf("//", StringComparison.Ordinal);
        if (commentIndex < 0)
            commentIndex = remainingText.IndexOf("#", StringComparison.Ordinal);
        if (commentIndex >= 0)
        {
            comment = remainingText[(commentIndex + 2)..].Trim();
            remainingText = remainingText[..commentIndex];
        }
        else comment = string.Empty;

        int actionIndex = remainingText.IndexOf('[', StringComparison.Ordinal);
        if (actionIndex >= 0)
        {
            //Found trailing action specifier
            var theUKS = MainWindow.theUKS;
            string actionString = remainingText[actionIndex..].Trim();
            action = (Link)theUKS.ProcessSingleLineWithNesting(actionString);
            command = remainingText[..actionIndex].Trim();
        }
        else
        {
            command = remainingText.Trim();
            action = null;
        }
    }

    // Incremental file-load state
    private StreamReader _phraseReader;
    private string _phraseReaderPath;

    /// <summary>
    /// Loads phrases incrementally
    /// </summary>
    public int LoadTextFromFile(string filePath, int phrasesPerCall = 20)
    {
        LastLoadedActionExemplarCount = 0;
        LastRetainedActionExemplarCount = 0;
        LastMissingActionExemplars = string.Empty;
        if (phrasesPerCall <= 0) phrasesPerCall = 1;
        if (!File.Exists(filePath))
        {
            ResetPhraseReader();
            return 0;
        }

        int count = 0;
        List<(Thought exemplar, string phrase)> loadedExemplars = new();
        // (Re)open reader if this is a new file or we haven't started yet
        if (_phraseReader == null || !string.Equals(
            _phraseReaderPath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            ResetPhraseReader();
            _phraseReader = new StreamReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read));
            _phraseReaderPath = filePath;
        }

        //read up to phrasesPerCall lines from the file, skipping empty lines
        while (count < phrasesPerCall && _phraseReader != null)
        {
            string line = _phraseReader.ReadLine();
            if (line == null) break; // EOF
            if (string.IsNullOrEmpty(line)) continue;

            SubmitText(line, answerQueries: false);
            if (LastStatus.StartsWith("Error:", StringComparison.Ordinal))
                throw new InvalidOperationException(LastStatus);
            if (LastActionExemplar is not null)
            {
                LastLoadedActionExemplarCount++;
                loadedExemplars.Add((LastActionExemplar, line));
            }
            count++;
        }
        bool reachedEndOfFile = _phraseReader != null && _phraseReader.EndOfStream;
        if (reachedEndOfFile)
        {
        }

        return count;
    }

    /// <summary>
    /// Cancels any in-progress incremental load.
    /// </summary>
    public void CancelIncrementalLoad()
    {
        ResetPhraseReader();
    }

    /// <summary>Closes the current corpus reader and clears its incremental-load state.</summary>
    private void ResetPhraseReader()
    {
        _phraseReader?.Dispose();
        _phraseReader = null;
        _phraseReaderPath = null;
    }



    /// <summary>Tokenizes one phrase and returns the observation which owns the resulting word sequence.</summary>
    public static string AddPhrase(string phrase, out Thought ingestedPhrase)
    {
        phrase = phrase.Replace(".", "");
        ingestedPhrase = null;
        var theUKS = MainWindow.theUKS;
        Thought wordRoot = theUKS.Labeled("Word");
        if (string.IsNullOrEmpty(phrase)) return "Null input";

        int attempted = 0;
        int ingested = 0;
        try
        {
            //first, add the individual words to the UKS. Each word is stored as a Thought with a "w:" prefix.
            List<Thought> wordsInPhrase = new();
            foreach (string token in phrase.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string clean = token;
                if (string.IsNullOrEmpty(clean)) continue;
                if (clean.Any(ch => !char.IsLetterOrDigit(ch))) continue;
                if (clean.Count(char.IsDigit) > 2) continue;
                attempted++;
                Thought wordThought = null;
                if (MainWindow.theWindow?.GetModuleByLabel("ModuleWord0") is ModuleWord mw)
                    wordThought = mw.AddWordSpelling(clean);

                // ModuleWord returns null while its attention/mental-model
                // streaming path is active. ModuleText still needs a stable
                // word value now so the observed phrase can be learned.
                wordThought ??= theUKS.Labeled("w:" + clean) ?? theUKS.GetOrAddThought("w:" + clean, "Word");
                if (wordThought is not null)
                {
                    wordsInPhrase.Add(wordThought);
                    ingested++;
                }
            }
            //now create and add the phrase observation which owns the sequence of words. The phrase is stored as a Thought with a "p*" prefix.
            Thought phraseRoot = theUKS.Labeled("Phrase");
            Thought hasWords = theUKS.Labeled("hasWords");
            if (wordsInPhrase.Count > 0)
            {
                Thought thePhrase = FindPhraseWithWords(theUKS, phraseRoot, hasWords, wordsInPhrase);
                bool isNewPhrase = thePhrase is null;
                if (thePhrase is null)
                {
                    //    PruneStoredPhrases(phraseRoot);
                    thePhrase = theUKS.GetOrAddThought("p*", phraseRoot);
                    theUKS.AddSequenceAndLink(thePhrase, hasWords, wordsInPhrase);
                }
                ingestedPhrase = thePhrase;
                //ObservePhrase(phraseRoot, thePhrase, isNewPhrase);
            }
            string retVal = $"Processed {attempted} tokens; ingested {ingested} words.";
            return retVal;
        }
        catch (Exception ex)
        {
            string retVal = $"Error: {ex.Message}";
            return retVal;
        }
    }
    /// <summary>
    /// Finds an existing phrase of the requested kind whose hasWords sequence exactly matches the supplied words.
    /// </summary>
    private static Thought FindPhraseWithWords(UKS.UKS theUKS, Thought phraseRoot, Thought hasWords, List<Thought> words)
    {
        List<(SeqElement seqNode, float confidence)> exactSequences =
            theUKS.FindSequencesByActivation(words, "ExactSequenceSearch");
        foreach ((SeqElement sequence, float _) in exactSequences)
        {
            Link ownerLink = sequence.LinksFrom.FirstOrDefault(link =>
                ReferenceEquals(link.LinkType, hasWords) &&
                link.From?.Parents.Any(parent => ReferenceEquals(parent, phraseRoot)) == true);
            if (ownerLink?.From is Thought existingPhrase)
            {
                Thought retVal = existingPhrase;
                return retVal;
            }
        }

        Thought noPhraseFound = null;
        return noPhraseFound;
    }




    /*******************************************************************************
    // 3. REWRITE to e contextSensitive
    *******************************************************************************/
    static float MeaningResolutionTieTolerance = 0.0001f;

    /// <summary>Selects a word's strongest unblocked meaning and leaves genuinely unrelated ties unresolved.</summary>
    public static Link GetBestMeaning(Thought word)
    {
        Thought means = MainWindow.theUKS?.Labeled("means");
        if (word is null || means is null) return null;

        List<Link> candidates = word.LinksTo
            .Where(link => link.LinkType == means && link.To is not null &&
                !BlockedMeaningLabels.Contains(link.To.Label))
            .OrderByDescending(link => link.Weight)
            .ToList();
        if (candidates.Count == 0) return null;

        float highestWeight = candidates[0].Weight;
        float tolerance = Math.Max(0, MeaningResolutionTieTolerance);
        List<Link> tied = candidates.Where(link => Math.Abs(link.Weight - highestWeight) <= tolerance).ToList();
        //    List<Link> groundedTies = tied.Where(link => !IsIdentityMeaning(word, link.To)).ToList();
        //
        //if (groundedTies.Count > 0) tied = groundedTies;
        if (tied.Count == 1)
        {
            Link onlyMeaning = tied[0];
            return onlyMeaning;
        }

        // A category shared by every tied instance is a stable resolution. An
        // unrelated tie remains unresolved instead of choosing arbitrarily.
        Link retVal = tied.FirstOrDefault(candidate => tied.All(other =>
            other == candidate || other.To.HasAncestor(candidate.To)));
        return retVal;
    }

    /// <summary>
    /// Returns the strongest word which currently denotes a meaning, optionally restricted to one language.
    /// </summary>
    public static string GetBestWordFor(Thought meaning, Thought language = null)
    {
        var theUKS = MainWindow.theUKS;
        Thought means = theUKS?.Labeled("means");
        if (meaning is null || means is null)
        {
            string noWord = string.Empty;
            return noWord;
        }

        Thought usedInLanguage = language is null ? null : theUKS.Labeled("usedInLanguage");
        List<Link> directWords = meaning.LinksFrom
            .Where(link => link.LinkType == means && link.From?.Label.StartsWith("w:", StringComparison.OrdinalIgnoreCase) == true &&
                (language is null || usedInLanguage is null || link.From.HasLink(usedInLanguage, language) is not null))
            .ToList();
        Link best = directWords
            .Where(link => GetBestMeaning(link.From)?.To == meaning)
            .OrderByDescending(link => link.Weight)
            .FirstOrDefault() ?? directWords
            .OrderByDescending(link => link.Weight)
            .FirstOrDefault();
        string retVal = best?.From?.Label.StartsWith("w:",
            StringComparison.OrdinalIgnoreCase) == true
            ? best.From.Label[2..]
            : string.Empty;
        return retVal;
    }
}

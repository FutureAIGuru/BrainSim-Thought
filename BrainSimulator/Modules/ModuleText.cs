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

/* French test phrases
Un chien est un animal.
Les chiens sont des animaux.
Un chien a une queue.
Les chiens ont des queues.
Un chien peut aboyer.
Les chiens peuvent aboyer.

Un corbeau est un oiseau.
Un corbeau peut voler.

Un poisson est un animal.
Un poisson peut nager.

Une grenouille est un animal.
Une grenouille peut sauter.

Une voiture est un objet.
Un vÃÂÃÂ©lo est un vÃÂÃÂ©hicule.
Une pomme est un fruit.

 
Un corbeau est un animal.
Un canard peut voler.
Une grenouille peut nager.
Un terrier est un animal.

Un corbeau peut voler
Une grenouille peut sauter.
Un canard est un oiseau.

Quel animal est le chien ?
Que possÃÂÃÂ¨de le chien ?
Que peut faire le chien ?
Quels animaux sont les chiens ?
Que possÃÂÃÂ¨dent les chiens ?
Que peuvent faire les chiens ?

 */



using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Pluralize.NET;
using UKS;

namespace BrainSimulator.Modules;

public class ModuleText : ModuleBase
{
    // Module organization:

    private const float PhraseObservationDecayFactor = 0.9999f;
    private const float PhraseObservationIncrease = 1f;
    private const int PlasticPhraseCapacity = 50;

    // Mutable so the grounding behavior can be tuned while demonstrating and
    // debugging without rebuilding the application.
    public static float MeaningInitialWeight { get; set; } = 0.1f;
    public static float MeaningReinforcement { get; set; } = 0.1f;
    public static float MeaningDecayFactor { get; set; } = 0.9f;
    public static float MeaningPruneThreshold { get; set; } = 0.05f;
    public static float MeaningMaximumWeight { get; set; } = 1f;
    public static float MeaningResolutionTieTolerance { get; set; } = 0.001f;
    public static float MeaningConsolidationThreshold { get; set; } = 0.9f;
    public static float MeaningConsolidationDiscardThreshold { get; set; } = 0.5f;

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

    public sealed class MeaningLearningResult
    {
        public IReadOnlyList<Link> ChangedLinks { get; init; } = Array.Empty<Link>();
        public IReadOnlyList<string> Changes { get; init; } = Array.Empty<string>();
        public string TextStatus { get; init; } = string.Empty;
    }

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

    /// <summary>Provides the UKS reinitialization hook; no cached language state currently needs resetting.</summary>
    public override void UKSInitializedNotification()
    {

    }

    // 1. Reading Language In
    /// <summary>
    /// Stores and interprets one utterance from typed or file input. A supplied
    /// action provides supervised meaning, while answerQueries controls whether
    /// a recognized TEST operation is searched and realized as language output.
    /// </summary>
    public string SubmitText(string text, bool answerQueries = true, string providedAction = null)
    {
        LastAnswer = string.Empty;
        LastRelationship = null;
        LastTemplate = null;
        LastActionExemplar = null;
        string trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            LastStatus = "The phrase is empty.";
            return null;
        }

        UKS.UKS uks = theUKS ?? MainWindow.theUKS;
        if (uks is null)
        {
            LastStatus = "The UKS is not available.";
            return null;
        }

        string ingestStatus = AddText(trimmed, out Thought phraseThought);
        if (ingestStatus.StartsWith("Error:", StringComparison.Ordinal))
        {
            LastStatus = ingestStatus;
            return null;
        }
        if (!string.IsNullOrWhiteSpace(providedAction)) LastActionExemplar = AddActionExemplar(trimmed, providedAction);
        if (!answerQueries) ObserveTrainingPhrase();
        List<Thought> words = GetPhraseWords(trimmed);
        Thought phraseRoot = GetPhraseKind(trimmed);
        bool explicitQuestion = phraseRoot?.Label.Equals("Question", StringComparison.OrdinalIgnoreCase) == true;
        bool matched = TryResolveInput(
            phraseThought, words, explicitQuestion, out Thought matchedTemplate,
            out Link matchedRelationship, out bool matchedQuery, out string matchError);
        if (matched)
        {
            LastTemplate = matchedTemplate;
            LastRelationship = matchedRelationship;
            if (!string.IsNullOrEmpty(matchError))
            {
                LastStatus = matchError;
                return null;
            }
            string templateLabel = matchedTemplate?.Label ?? "seed template";
            if (!matchedQuery)
            {
                LastStatus = $"Understood with {templateLabel}.";
                return null;
            }
            if (!answerQueries)
            {
                LastStatus = $"Matched query exemplar with {templateLabel}.";
                return null;
            }

            string retVal = GenerateQueryAnswer(matchedRelationship, words, matchedTemplate);
            return retVal;
        }

        if (explicitQuestion)
        {
            LastStatus = "No learned query template matched the question.";
            return null;
        }

        LastStatus = ingestStatus;
        return null;
    }

    /// <summary>Matches learned templates first, then maps the legacy seed-template representation.</summary>
    private bool TryResolveInput(
        Thought phrase,
        List<Thought> words,
        bool explicitQuestion,
        out Thought template,
        out Link relationship,
        out bool query,
        out string error)
    {
        template = null;
        relationship = null;
        query = false;
        error = string.Empty;

        // A learned TEST template identifies a query even when its punctuation
        // is omitted, which also makes direct SubmitText tests possible.
        Thought learnedQuestionTemplate = ApplyExistingTemplatesToPhrase(phrase, useQuestionTemplates: true);
        bool learnedQuery = GetParameterizedTemplateOperation(learnedQuestionTemplate)?
            .LinkType?.HasAncestor("TEST") == true;
        Thought learnedTemplate = learnedQuery
            ? learnedQuestionTemplate
            : explicitQuestion
                ? null
                : ApplyExistingTemplatesToPhrase(phrase, useQuestionTemplates: false);
        Link learnedRelationship = ApplyLearnedTemplateAction(learnedTemplate, phrase);
        if (learnedRelationship is not null)
        {
            template = learnedTemplate;
            relationship = learnedRelationship;
            query = learnedQuery;
            return true;
        }

        bool exactExemplarMatched = TryResolveExactActionExemplar(
            phrase, out Thought exactExemplar, out Link exactRelationship, out bool exactQuery);
        if (exactExemplarMatched)
        {
            template = exactExemplar;
            relationship = exactRelationship;
            query = exactQuery;
            return true;
        }

        List<Thought> mapped = FindAndMapSeedTemplate(words, out Thought seedTemplate);
        bool seedQuery = mapped?.Any(parameter =>
            parameter?.Label.Contains(":??", StringComparison.Ordinal) == true) == true;
        if (seedQuery)
        {
            template = seedTemplate;
            relationship = BuildResolvedLink(mapped, createMissingMeanings: false);
            query = true;
            if (relationship?.LinkType is null) error = "The query contains an unresolved word.";
            return true;
        }

        if (!explicitQuestion && mapped?.Count >= 3)
        {
            Link seedRelationship = BuildResolvedLink(mapped, createMissingMeanings: true);
            if (seedRelationship?.From is not null && seedRelationship.LinkType is not null &&
                seedRelationship.To is not null)
            {
                UKS.UKS uks = theUKS ?? MainWindow.theUKS;
                template = seedTemplate;
                relationship = uks.AddStatement(
                    seedRelationship.From, seedRelationship.LinkType, seedRelationship.To);
                return true;
            }
        }

        bool retVal = false;
        return retVal;
    }

    /// <summary>
    /// Uses a memorized supervised exemplar when its generalized template is unavailable. This guarantees that a
    /// phrase explicitly demonstrated by the corpus remains usable without turning that phrase into a special rule.
    /// </summary>
    private static bool TryResolveExactActionExemplar(
        Thought phrase, out Thought exemplar, out Link relationship, out bool query)
    {
        exemplar = null;
        relationship = null;
        query = false;
        UKS.UKS uks = MainWindow.theUKS;
        Thought exemplarRoot = uks?.Labeled("ActionExemplar");
        SequenceView phraseSequence = uks?.GetSequenceViews(phrase)
            .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
        if (exemplarRoot is null || phraseSequence is null) return false;

        exemplar = exemplarRoot.Children.FirstOrDefault(candidate =>
        {
            SequenceView exemplarSequence = uks.GetSequenceViews(candidate)
                .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
            bool retVal = exemplarSequence is not null &&
                exemplarSequence.Elements.SequenceEqual(phraseSequence.Elements);
            return retVal;
        });
        Link demonstratedAction = exemplar?.GetTargetOfFirstLinkOfType("demonstrates") as Link;
        if (demonstratedAction?.LinkType is null) return false;

        query = demonstratedAction.LinkType.HasAncestor("TEST");
        if (query)
        {
            Thought relationshipType = GetOperationRelationshipType(demonstratedAction.LinkType);
            relationship = new Link(demonstratedAction.From, relationshipType, demonstratedAction.To);
        }
        else relationship = uks.ApplySetAction(demonstratedAction);
        Thought filterTarget = demonstratedAction.GetTargetOfFirstLinkOfType("filterBy");
        if (relationship is not null && query && filterTarget is not null)
            relationship.AddLink(GetFilterByRelationship(), filterTarget);
        bool retVal = relationship is not null;
        return retVal;
    }

    // Incremental file-load state
    private StreamReader _phraseReader;
    private string _phraseReaderPath;

    /// <summary>
    /// Loads phrases incrementally and retains tab-delimited SET or TEST actions as supervised evidence.
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
        try
        {
            // (Re)open reader if this is a new file or we haven't started yet
            if (_phraseReader == null || !string.Equals(
                _phraseReaderPath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                ResetPhraseReader();
                _phraseReader = new StreamReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read));
                _phraseReaderPath = filePath;
            }

            while (count < phrasesPerCall && _phraseReader != null)
            {
                string line = _phraseReader.ReadLine();
                if (line == null) break; // EOF

                string lineText = line.Trim();
                TrySplitActionAnnotation(lineText, out string phrase, out string actionText);
                if (phrase.Length == 0) continue;

                if (phrase.Length == 0) continue;

                // Split into sentences so we cap by phrases, not by lines
                string[] sentences = Regex.Split(phrase, @"(?<=[\.!\?])\s+");
                foreach (string sentence in sentences)
                {
                    if (count >= phrasesPerCall) break;

                    string trimmed = sentence.Trim();
                    if (trimmed.Length == 0) continue;

                    string sentenceAction = sentences.Length == 1 ? actionText : null;
                    SubmitText(trimmed, answerQueries: false, providedAction: sentenceAction);
                    if (LastStatus.StartsWith("Error:", StringComparison.Ordinal))
                        throw new InvalidOperationException(LastStatus);
                    if (!string.IsNullOrWhiteSpace(sentenceAction))
                    {
                        LastLoadedActionExemplarCount++;
                        loadedExemplars.Add((LastActionExemplar, trimmed));
                    }
                    count++;
                }

                // An annotation describes its complete line. Corpus lines normally
                // contain one sentence; preserve the old whole-line behavior if a
                // future annotated line contains more than one sentence.
                if (sentences.Length > 1 && !string.IsNullOrWhiteSpace(actionText))
                {
                    Thought exemplar = AddActionExemplar(phrase, actionText);
                    LastLoadedActionExemplarCount++;
                    loadedExemplars.Add((exemplar, phrase));
                }
            }

            bool reachedEndOfFile = _phraseReader != null && _phraseReader.EndOfStream;
            if (reachedEndOfFile)
            {
                ResetPhraseReader();
                QueuePendingTrainingConsolidation(force: true);
                ConsolidateGrammarAndMeanings();
            }

            List<string> missingExemplars = loadedExemplars
                .Where(item => !ActionExemplarHasWords(item.exemplar, item.phrase))
                .Select(item => item.phrase)
                .ToList();
            LastRetainedActionExemplarCount = loadedExemplars.Count - missingExemplars.Count;
            LastMissingActionExemplars = string.Join("; ", missingExemplars);

            return count;
        }
        catch (Exception ex)
        {
            LastStatus = $"Error loading phrases from file: {ex.Message}";
            Debug.WriteLine(ex);
            ResetPhraseReader();
            return count;
        }
    }

    /// <summary>Separates a corpus phrase from a trailing tab- or space-delimited SET or TEST annotation.</summary>
    internal static bool TrySplitActionAnnotation(string line, out string phrase, out string actionText)
    {
        phrase = line?.Trim() ?? string.Empty;
        actionText = null;
        int tabIndex = phrase.IndexOf('\t');
        if (tabIndex >= 0)
        {
            actionText = phrase[(tabIndex + 1)..].Trim();
            phrase = phrase[..tabIndex].Trim();
        }
        else
        {
            Match annotation = Regex.Match(
                phrase,
                @"^(?<phrase>.+?\S)\s+(?<action>\[.*->\s*(?:SET|TEST)\.[^\r\n]*\])\s*$",
                RegexOptions.IgnoreCase);
            if (annotation.Success)
            {
                phrase = annotation.Groups["phrase"].Value.Trim();
                actionText = annotation.Groups["action"].Value.Trim();
            }
        }
        bool retVal = !string.IsNullOrWhiteSpace(actionText);
        return retVal;
    }

    /// <summary>
    /// Checks that a newly loaded exemplar still owns the exact phrase sequence supplied by the corpus.
    /// </summary>
    private static bool ActionExemplarHasWords(Thought exemplar, string phrase)
    {
        if (exemplar is null) return false;
        List<Thought> expected = GetPhraseWords(phrase);
        bool retVal = MainWindow.theUKS.GetSequenceViews(exemplar)
            .Any(view => view.LinkType?.Label == "hasWords" && view.Elements.SequenceEqual(expected));
        return retVal;
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

    /// <summary>Splits text into sentences and sends each nonempty sentence through phrase ingestion.</summary>
    public static string AddText(string text)
    {
        string retVal = AddText(text, out Thought _);
        return retVal;
    }

    /// <summary>Splits text into sentences and also returns the final phrase observation created or reused.</summary>
    private static string AddText(string text, out Thought ingestedPhrase)
    {
        ingestedPhrase = null;
        var theUKS = MainWindow.theUKS;
        Thought wordRoot = theUKS.GetOrAddThought("Word", "LanguageElement");
        wordRoot.RemoveParent("Thought");
        wordRoot.RemoveParent("Object");
        if (string.IsNullOrWhiteSpace(text))
        {
            string emptyResult = "Null input";
            return emptyResult;
        }

        string[] sentences = Regex.Split(text, @"(?<=[\.!\?])\s+");
        string retVal = "OK";
        foreach (string sentence in sentences)
        {
            string trimmed = sentence.Trim();
            if (trimmed.Length == 0) continue;

            retVal = AddPhrase(trimmed, out ingestedPhrase);
            if (sentences.Length == 1) return retVal;
        }
        return retVal;
    }

    /// <summary>Tokenizes one phrase and stores or reinforces its word sequence.</summary>
    public static string AddPhrase(string phrase)
    {
        string retVal = AddPhrase(phrase, out Thought _);
        return retVal;
    }

    /// <summary>Tokenizes one phrase and returns the observation which owns the resulting word sequence.</summary>
    private static string AddPhrase(string phrase, out Thought ingestedPhrase)
    {
        ingestedPhrase = null;
        var theUKS = MainWindow.theUKS;
        char[] trimChars = { '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}' };

        int attempted = 0;
        int ingested = 0;
        try
        {
            List<Thought> wordsInPhrase = new();
            foreach (string token in phrase.Split(
                new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string clean = token.Trim(trimChars).ToLowerInvariant();
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

            Thought phraseRoot = GetPhraseKind(phrase);
            Thought hasWords = theUKS.GetOrAddThought("hasWords", "LinkType");
            if (wordsInPhrase.Count > 0)
            {
                Thought thePhrase = FindPhraseWithWords(theUKS, phraseRoot, hasWords, wordsInPhrase);
                bool isNewPhrase = thePhrase is null;
                if (thePhrase is null)
                {
                    thePhrase = theUKS.GetOrAddThought("p*", phraseRoot);
                    theUKS.AddSequenceAndLink(thePhrase, hasWords, wordsInPhrase);
                }
                ingestedPhrase = thePhrase;
                ObservePhrase(phraseRoot, thePhrase, isNewPhrase);

                PruneStoredPhrases(phraseRoot);
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
    /// Tokenizes a phrase into existing or newly created word Thoughts without storing a phrase observation.
    /// </summary>
    public static List<Thought> GetPhraseWords(string phrase)
    {
        char[] trimChars = { '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}' };
        var theUKS = MainWindow.theUKS;
        List<Thought> retVal = phrase.Split(new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim(trimChars).ToLowerInvariant())
            .Where(token => token.Length > 0 && token.All(char.IsLetterOrDigit))
            .Select(token => theUKS.Labeled("w:" + token) ??
                theUKS.GetOrAddThought("w:" + token, "Word"))
            .Where(word => word is not null)
            .ToList();
        return retVal;
    }

    /// <summary>
    /// Keeps observations of statements and questions in separate populations.
    /// The punctuation supplies only the observation kind; it supplies no
    /// grammatical or semantic interpretation.
    /// </summary>
    private static Thought GetPhraseKind(string phrase)
    {
        var theUKS = MainWindow.theUKS;
        theUKS.GetOrAddThought("Phrase");
        bool interrogative = phrase.TrimEnd().EndsWith("?", StringComparison.Ordinal);
        Thought retVal = theUKS.GetOrAddThought(interrogative ? "Question" : "Statement", "Phrase");
        return retVal;
    }

    /// <summary>
    /// Returns observations of one utterance kind. Older phrases directly under
    /// Phrase remain readable as statements for saved-network compatibility.
    /// </summary>
    public static List<Thought> GetPhrasesOfKind(string kindLabel)
    {
        var theUKS = MainWindow.theUKS;
        List<Thought> retVal = new();
        if (theUKS.Labeled(kindLabel) is Thought kind) retVal.AddRange(kind.Children.Where(HasWords));
        if (kindLabel.Equals("Statement", StringComparison.OrdinalIgnoreCase) &&
            theUKS.Labeled("Phrase") is Thought phraseRoot)
            retVal.AddRange(phraseRoot.Children.Where(HasWords));
        retVal = retVal.Distinct().ToList();
        return retVal;

        static bool HasWords(Thought phrase) => phrase.GetTargetOfFirstLinkOfType("hasWords") is not null;
    }

    /// <summary>
    /// Finds an existing phrase of the requested kind whose hasWords sequence exactly matches the supplied words.
    /// </summary>
    private static Thought FindPhraseWithWords(
        UKS.UKS theUKS, Thought phraseRoot, Thought hasWords, List<Thought> words)
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

    /// <summary>
    /// Reinforces an observed phrase and slowly decays other disposable observations of the same kind.
    /// </summary>
    private static void ObservePhrase(Thought phraseRoot, Thought observedPhrase, bool isNewPhrase)
    {
        foreach (Thought storedPhrase in phraseRoot.Children)
        {
            if (!storedPhrase.isPlastic || HasMeaning(storedPhrase)) continue;
            storedPhrase.Weight *= PhraseObservationDecayFactor;
        }

        observedPhrase.isPlastic = true;
        if (isNewPhrase) observedPhrase.Weight = PhraseObservationIncrease;
        else observedPhrase.Weight += PhraseObservationIncrease;
        observedPhrase.Fire();
    }

    /// <summary>
    /// Limits ungrounded plastic phrase observations while retaining phrases which have acquired meanings.
    /// </summary>
    private static void PruneStoredPhrases(Thought phraseRoot)
    {
        List<Thought> deletablePhrases = phraseRoot.Children
            .Where(phrase => phrase.isPlastic && !HasMeaning(phrase))
            .OrderBy(phrase => phrase.Weight)
            .ThenBy(phrase => phrase.LastFiredTime)
            .ToList();

        int deleteCount = deletablePhrases.Count - PlasticPhraseCapacity;
        for (int index = 0; index < deleteCount; index++) deletablePhrases[index].Delete();
    }

    /// <summary>Reports whether a phrase participates in a means relationship in either direction.</summary>
    private static bool HasMeaning(Thought phrase)
    {
        bool retVal = phrase.LinksTo.Any(link => link.LinkType?.Label == "means") ||
            phrase.LinksFrom.Any(link => link.LinkType?.Label == "means");
        return retVal;
    }

    // Legacy seed-template compatibility. QueryTemplates.txt is still useful
    // when a UKS has not learned enough templates from annotated exemplars.

    /// <summary>Matches the legacy input/output template format and substitutes its positional parameters.</summary>
    private List<Thought> FindAndMapSeedTemplate(List<Thought> words, out Thought foundTemplate)
    {
        foundTemplate = null;
        UKS.UKS uks = theUKS ?? MainWindow.theUKS;
        if (uks is null || words is null) return null;

        for (int length = words.Count; length >= 2; length--)
        for (int start = 0; start <= words.Count - length; start++)
        {
            List<Thought> subsequence = words.GetRange(start, length);
            List<(SeqElement seqNode, float confidence)> matches =
                uks.FindSequencesByActivation(subsequence, "TemplateSequenceSearch");
            foreach ((SeqElement seqNode, float _) in matches)
            foreach (Thought template in seqNode.LinksFrom
                .Where(link => link.LinkType?.Label == "hasWords" &&
                    link.From is not null)
                .Select(link => link.From)
                .Distinct())
            {
                SeqElement inputSequence = template?.GetTargetOfFirstLinkOfType("hasWords") as SeqElement;
                SeqElement outputSequence = template?.GetTargetOfFirstLinkOfType("outputs") as SeqElement;
                if (template is null || inputSequence is null || outputSequence is null) continue;

                List<Thought> inputParameters = uks.FlattenSequence(inputSequence);
                List<Thought> outputParameters = uks.FlattenSequence(outputSequence).ToList();
                for (int outputIndex = 0;
                    outputIndex < outputParameters.Count;
                    outputIndex++)
                {
                    string label = outputParameters[outputIndex].Label;
                    if (!label.Contains("??", StringComparison.Ordinal) ||
                        !int.TryParse(label[4..], out int parameterNumber))
                        continue;

                    int inputPosition = inputParameters
                        .Select((word, position) => (word, position))
                        .Where(item => item.word.Label == "w:??")
                        .ElementAt(parameterNumber - 1)
                        .position;
                    outputParameters[outputIndex] = subsequence[inputPosition];
                }

                foundTemplate = template;
                return outputParameters;
            }
        }
        return null;
    }

    /// <summary>Resolves three template parameters into an ordinary semantic relationship.</summary>
    private Link BuildResolvedLink(IReadOnlyList<Thought> parameters, bool createMissingMeanings)
    {
        UKS.UKS uks = theUKS ?? MainWindow.theUKS;
        if (uks is null || parameters?.Count < 3) return null;

        Thought Resolve(Thought parameter)
        {
            if (parameter is null || parameter.Label.Contains(":??", StringComparison.Ordinal)) return null;
            bool isWord = parameter.Label.StartsWith(
                "w:", StringComparison.OrdinalIgnoreCase) ||
                parameter.HasAncestor("word") ||
                parameter.HasAncestor("Word");
            if (!isWord) return parameter;

            Thought meaning = GetBestMeaning(parameter)?.To;
            if (meaning is not null || !createMissingMeanings) return meaning;
            string label = parameter.Label.StartsWith(
                "w:", StringComparison.OrdinalIgnoreCase)
                ? parameter.Label[2..]
                : parameter.Label;
            meaning = uks.GetOrAddThought(label);
            uks.AddStatement(parameter, uks.GetOrAddThought("means", "LinkType"), meaning);
            return meaning;
        }

        Link retVal = new(Resolve(parameters[0]), Resolve(parameters[1]), Resolve(parameters[2]));
        return retVal;
    }

    // 2. Generating Language Out

    /// <summary>Searches a resolved TEST relationship and converts its results into language.</summary>
    private string GenerateQueryAnswer(Link query, IReadOnlyList<Thought> inputWords, Thought template)
    {
        UKS.UKS uks = theUKS ?? MainWindow.theUKS;
        List<Link> results = uks.SearchForRelationships(query);
        Thought preferredSurfaceWord = GetPreferredSurfaceThought(inputWords, query.From);
        LastAnswer = ConvertRelationshipsToText(
            results, query.From, GetSurfaceText(preferredSurfaceWord), preferredSurfaceWord, template);
        LastStatus = results.Count == 0
            ? "No matching relationship was found."
            : $"Answered with {results.Count} relationship" + (results.Count == 1 ? "." : "s.");
        string retVal = LastAnswer;
        return retVal;
    }

    /// <summary>
    /// Converts query results to clauses using learned assertion templates, with a direct-label fallback.
    /// </summary>
    private string ConvertRelationshipsToText(
        IReadOnlyList<Link> relationships,
        Thought preferredMeaning = null,
        string preferredWord = null,
        Thought preferredSurfaceWord = null,
        Thought queryTemplate = null)
    {
        if (relationships is null || relationships.Count == 0)
        {
            string noAnswer = string.Empty;
            return noAnswer;
        }

        List<string> clauses = new();
        foreach (Link relationship in relationships)
        {
            string learnedClause = ConvertRelationshipWithLearnedAssertion(
                relationship, preferredSurfaceWord, queryTemplate);
            if (!string.IsNullOrWhiteSpace(learnedClause))
            {
                clauses.Add(learnedClause);
                continue;
            }

            List<string> words = new();
            AddSurfaceWords(words, relationship.From, preferredMeaning, preferredWord);
            AddSurfaceWords(words, relationship.LinkType, preferredMeaning, preferredWord);
            AddSurfaceWords(words, relationship.To, preferredMeaning, preferredWord);
            string fallbackClause = string.Join(" ", words.Where(word => !string.IsNullOrWhiteSpace(word)));
            if (!string.IsNullOrWhiteSpace(fallbackClause)) clauses.Add(fallbackClause);
        }

        string answer = string.Join("; ", clauses);
        string retVal = answer.Length == 0 ? answer : char.ToUpperInvariant(answer[0]) + answer[1..];
        return retVal;
    }

    /// <summary>
    /// Realizes one relationship with the learned assertion template most compatible with the query surface form.
    /// </summary>
    private string ConvertRelationshipWithLearnedAssertion(
        Link relationship,
        Thought sourceSurfaceWord,
        Thought queryTemplate)
    {
        if (relationship?.From is null || relationship.LinkType is null ||
            relationship.To is null || sourceSurfaceWord is null)
        {
            string noClause = string.Empty;
            return noClause;
        }

        Thought assertionRoot = GetLearnedTemplateCategory(query: false);
        SequenceView querySequence = queryTemplate is null
            ? null
            : MainWindow.theUKS.GetSequenceViews(queryTemplate)
                .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
        HashSet<Thought> queryFixedWords = querySequence?.Elements
            .Where(element => !element.HasAncestor("Wildcard"))
            .ToHashSet() ?? new HashSet<Thought>();

        var candidates = assertionRoot.Children
            .Select(template =>
            {
                Link operation = GetParameterizedTemplateOperation(template);
                Thought operationRelationship = GetOperationRelationshipType(
                    operation?.LinkType);
                SequenceView sequence = MainWindow.theUKS.GetSequenceViews(template)
                    .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
                if (operation?.From is null || operation.To is null ||
                    operationRelationship != relationship.LinkType || sequence is null)
                    return default;

                int sourcePosition = sequence.Elements.ToList().FindIndex(element => element == operation.From);
                int targetPosition = sequence.Elements.ToList().FindIndex(element => element == operation.To);
                if (sourcePosition < 0 || targetPosition <= sourcePosition) return default;

                bool sourceCompatible = SurfaceWordMatchesTemplateElement(
                    sourceSurfaceWord, sequence.Elements[sourcePosition]);
                int sharedFixedWords = sequence.Elements.Count(element =>
                    !element.HasAncestor("Wildcard") &&
                    queryFixedWords.Contains(element));
                int evidence = template.LinksTo.Count(link => link.LinkType?.Label == "evidence");
                Thought targetWord = GetSurfaceWordForTemplateElement(
                    relationship.To, sequence.Elements[targetPosition]);
                bool targetCompatible = targetWord is not null &&
                    SurfaceWordMatchesTemplateElement(
                        targetWord, sequence.Elements[targetPosition]);
                AnswerTemplateCandidate retVal = new(
                    template, sequence, sourcePosition, targetPosition,
                    sourceCompatible, targetCompatible, sharedFixedWords, evidence,
                    targetWord);
                return retVal;
            })
            .Where(candidate => candidate is not null)
            .OrderByDescending(candidate => candidate.SourceCompatible)
            .ThenByDescending(candidate => candidate.SharedFixedWords)
            .ThenByDescending(candidate => candidate.TargetCompatible)
            .ThenByDescending(candidate => candidate.Evidence)
            .ThenBy(candidate => candidate.Template.Label, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0)
        {
            string noClause = string.Empty;
            return noClause;
        }

        AnswerTemplateCandidate selected = candidates[0];
        string sourceText = GetSurfaceText(sourceSurfaceWord);
        Thought targetSurfaceWord = selected.TargetWord;
        string targetText = GetSurfaceText(targetSurfaceWord);
        if (string.IsNullOrWhiteSpace(targetText))
        {
            targetText = GetBestWordFor(relationship.To);
            if (string.IsNullOrWhiteSpace(targetText)) targetText = relationship.To.Label;
        }
        if (!selected.TargetCompatible)
        {
            IPluralize pluralizer = new Pluralizer();
            targetText = IsPluralSurfaceForm(sourceText, pluralizer)
                ? pluralizer.Pluralize(targetText)
                : pluralizer.Singularize(targetText);
        }

        List<string> words = new() { sourceText };
        foreach (Thought connector in selected.Sequence.Elements
            .Skip(selected.SourcePosition + 1)
            .Take(selected.TargetPosition - selected.SourcePosition - 1))
        {
            if (connector.HasAncestor("Wildcard")) continue;
            string connectorText = GetSurfaceText(connector);
            if (connectorText.Equals("a", StringComparison.OrdinalIgnoreCase) ||
                connectorText.Equals("an", StringComparison.OrdinalIgnoreCase))
                connectorText = SelectIndefiniteArticle(targetText);
            if (!string.IsNullOrWhiteSpace(connectorText)) words.Add(connectorText);
        }
        words.Add(targetText);
        string retVal = string.Join(" ", words.Where(word => !string.IsNullOrWhiteSpace(word)));
        return retVal;
    }

    /// <summary>Appends a preferred word, learned word, meaning phrase, or label for one semantic value.</summary>
    private void AddSurfaceWords(
        ICollection<string> words,
        Thought meaning,
        Thought preferredMeaning = null,
        string preferredWord = null)
    {
        UKS.UKS uks = theUKS ?? MainWindow.theUKS;
        if (meaning is null || uks is null) return;

        if (meaning == preferredMeaning && !string.IsNullOrWhiteSpace(preferredWord))
        {
            words.Add(preferredWord);
            return;
        }

        string directWord = GetBestWordFor(meaning);
        if (!string.IsNullOrWhiteSpace(directWord))
        {
            words.Add(directWord);
            return;
        }

        Thought phrase = meaning.LinksFrom.FirstOrDefault(link =>
            link.LinkType?.Label == "means" &&
            (link.From?.HasAncestor("phrase") == true ||
                link.From?.HasAncestor("Phrase") == true))?.From;
        SeqElement phraseSequence = phrase?.GetTargetOfFirstLinkOfType("contains") as SeqElement ??
            phrase?.GetTargetOfFirstLinkOfType("hasWords") as SeqElement;
        if (phraseSequence is not null)
        {
            foreach (Thought phraseWord in uks.FlattenSequence(phraseSequence))
            {
                words.Add(phraseWord.Label.StartsWith(
                    "w:", StringComparison.OrdinalIgnoreCase)
                    ? phraseWord.Label[2..]
                    : phraseWord.Label);
            }
            return;
        }

        words.Add(meaning.Label);
    }

    /// <summary>
    /// Finds the input word which expressed a requested meaning, preferring the last matching occurrence.
    /// </summary>
    private static Thought GetPreferredSurfaceThought(IEnumerable<Thought> phraseWords, Thought meaning)
    {
        Thought retVal = phraseWords?.Reverse().FirstOrDefault(candidate => GetBestMeaning(candidate)?.To == meaning);
        return retVal;
    }

    /// <summary>Returns the display text of the input word which expressed a requested meaning.</summary>
    private static string GetPreferredSurfaceWord(IEnumerable<Thought> phraseWords, Thought meaning)
    {
        string retVal = GetSurfaceText(GetPreferredSurfaceThought(phraseWords, meaning));
        return retVal;
    }

    /// <summary>Reports whether a surface word is recognized as the plural member of a spelling pair.</summary>
    private static bool IsPluralSurfaceForm(string surfaceWord, IPluralize pluralizer)
    {
        if (string.IsNullOrWhiteSpace(surfaceWord) || pluralizer is null) return false;
        string singular = pluralizer.Singularize(surfaceWord);
        bool retVal = !singular.Equals(surfaceWord, StringComparison.OrdinalIgnoreCase) &&
            pluralizer.Pluralize(singular).Equals(
                surfaceWord, StringComparison.OrdinalIgnoreCase);
        return retVal;
    }

    /// <summary>Selects a word for a meaning which also satisfies a learned template slot.</summary>
    private static Thought GetSurfaceWordForTemplateElement(Thought meaning, Thought templateElement)
    {
        Thought means = MainWindow.theUKS?.Labeled("means");
        if (meaning is null || templateElement is null || means is null) return null;

        Thought retVal = meaning.LinksFrom
            .Where(link => link.LinkType == means &&
                link.From?.Label.StartsWith(
                    "w:", StringComparison.OrdinalIgnoreCase) == true)
            .OrderByDescending(link =>
                SurfaceWordMatchesTemplateElement(link.From, templateElement))
            .ThenByDescending(link => GetBestMeaning(link.From)?.To == meaning)
            .ThenByDescending(link => link.Weight)
            .Select(link => link.From)
            .FirstOrDefault();
        return retVal;
    }

    /// <summary>
    /// Tests a surface word against either a fixed template word or the classes constraining a wildcard.
    /// </summary>
    private static bool SurfaceWordMatchesTemplateElement(Thought surfaceWord, Thought templateElement)
    {
        if (surfaceWord is null || templateElement is null) return false;
        if (!templateElement.HasAncestor("Wildcard")) return surfaceWord == templateElement;

        Thought learnedClassRoot = MainWindow.theUKS?.Labeled("LearnedClass");
        List<Thought> constraints = learnedClassRoot is null
            ? new List<Thought>()
            : templateElement.Parents
                .Where(learnedClassRoot.Children.Contains)
                .ToList();
        bool retVal = constraints.Count == 0 || constraints.Any(surfaceWord.HasAncestor);
        return retVal;
    }

    /// <summary>Selects the hard-coded English indefinite article for the following written word.</summary>
    private static string SelectIndefiniteArticle(string followingWord)
    {
        char firstLetter = followingWord?.FirstOrDefault(char.IsLetter) ?? '\0';
        string retVal = "aeiou".Contains(
            char.ToLowerInvariant(firstLetter), StringComparison.Ordinal)
            ? "an"
            : "a";
        return retVal;
    }

    /// <summary>Removes the internal w: prefix from a word Thought for display.</summary>
    private static string GetSurfaceText(Thought word)
    {
        if (word is null)
        {
            string noText = string.Empty;
            return noText;
        }
        string retVal = word.Label.StartsWith("w:", StringComparison.OrdinalIgnoreCase) ? word.Label[2..] : word.Label;
        return retVal;
    }

    private sealed record AnswerTemplateCandidate(
        Thought Template,
        SequenceView Sequence,
        int SourcePosition,
        int TargetPosition,
        bool SourceCompatible,
        bool TargetCompatible,
        int SharedFixedWords,
        int Evidence,
        Thought TargetWord);

    // 3. Attaching Meanings

    /// <summary>
    /// Learns weighted word meanings from a phrase and a set of meanings which
    /// are simultaneously active in the current sensory context.
    /// </summary>
    public static MeaningLearningResult ObservePhraseMeanings(
        string phrase, IEnumerable<Thought> activeMeanings, string languageLabel = null)
    {
        var theUKS = MainWindow.theUKS;
        if (theUKS is null || string.IsNullOrWhiteSpace(phrase))
        {
            MeaningLearningResult emptyResult = new() { TextStatus = "The phrase is empty." };
            return emptyResult;
        }

        List<Thought> candidates = (activeMeanings ?? Enumerable.Empty<Thought>())
            .Where(thought => thought is not null &&
                !BlockedMeaningLabels.Contains(thought.Label))
            .Distinct()
            .ToList();
        if (candidates.Count == 0)
        {
            MeaningLearningResult noAttentionResult = new()
            {
                TextStatus = "Nothing currently occupies the Attention location."
            };
            return noAttentionResult;
        }

        string textStatus = AddText(phrase);
        Thought means = theUKS.GetOrAddThought("means", "LinkType");
        HashSet<Thought> heardWords = GetPhraseWords(phrase).ToHashSet();
        Thought currentLanguage = EnsureLanguage(languageLabel);
        Thought usedInLanguage = currentLanguage is null ? null : theUKS.GetOrAddThought("usedInLanguage", "LinkType");
        if (currentLanguage is not null)
        {
            foreach (Thought heardWord in heardWords) heardWord.AddLink(usedInLanguage, currentLanguage);
        }

        HashSet<Thought> activeCandidates = candidates.ToHashSet();
        HashSet<Link> newLinks = new();
        List<string> changes = new();
        foreach (Thought word in heardWords)
        foreach (Thought candidate in activeCandidates)
        {
            Link meaning = word.HasLink(means, candidate);
            if (meaning is not null) continue;

            meaning = word.AddLink(means, candidate);
            if (meaning is null) continue;
            meaning.isPlastic = true;
            meaning.maxWeight = Math.Max(MeaningMaximumWeight, 0.01f);
            meaning.Weight = Math.Clamp(MeaningInitialWeight, 0, meaning.maxWeight);
            meaning.Fire();
            newLinks.Add(meaning);
        }

        List<Link> plasticMeanings = theUKS.AtomicThoughts
            .SelectMany(thought => thought.LinksTo)
            .Where(link => link.LinkType == means && link.isPlastic)
            .Distinct()
            .ToList();
        List<Link> changedLinks = new();
        foreach (Link meaning in plasticMeanings)
        {
            bool wordObserved = meaning.From is not null && heardWords.Contains(meaning.From);
            bool targetActive = meaning.To is not null && activeCandidates.Contains(meaning.To);
            bool targetBlocked = meaning.To is null || BlockedMeaningLabels.Contains(meaning.To.Label);
            bool supported = wordObserved && targetActive && !targetBlocked;
            bool wordUsesCurrentLanguage = currentLanguage is null ||
                meaning.From?.HasLink(usedInLanguage, currentLanguage) is not null;
            float oldWeight = newLinks.Contains(meaning) ? 0 : meaning.Weight;

            if (supported)
            {
                if (!newLinks.Contains(meaning))
                {
                    meaning.maxWeight = Math.Max(MeaningMaximumWeight, 0.01f);
                    meaning.Weight = Math.Clamp(
                        meaning.Weight + Math.Max(0, MeaningReinforcement),
                        0,
                        meaning.maxWeight);
                    meaning.Fire();
                }
            }
            else if (wordObserved ||
                (targetActive && wordUsesCurrentLanguage) || targetBlocked)
            {
                meaning.Weight *= Math.Clamp(MeaningDecayFactor, 0, 1);
            }

            if (meaning.Weight != oldWeight)
            {
                changedLinks.Add(meaning);
                changes.Add($"{meaning.From?.Label} -> {meaning.To?.Label}: {oldWeight:0.00} -> {meaning.Weight:0.00}");
            }
        }

        foreach (Link weakMeaning in plasticMeanings
            .Where(link => link.Weight < Math.Max(0, MeaningPruneThreshold))
            .ToList())
        {
            changes.Add($"{weakMeaning.From?.Label} -> {weakMeaning.To?.Label}: removed");
            weakMeaning.From?.RemoveLink(weakMeaning);
        }

        ConsolidateMeanings(plasticMeanings, changes);
        MeaningLearningResult retVal = new()
        {
            ChangedLinks = changedLinks.Distinct().ToList(),
            Changes = changes,
            TextStatus = textStatus,
        };
        return retVal;
    }

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
        List<Link> groundedTies = tied.Where(link => !IsIdentityMeaning(word, link.To)).ToList();
        if (groundedTies.Count > 0) tied = groundedTies;
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
            .Where(link => link.LinkType == means &&
                link.From?.Label.StartsWith("w:",
                    StringComparison.OrdinalIgnoreCase) == true &&
                (language is null || usedInLanguage is null ||
                    link.From.HasLink(usedInLanguage, language) is not null))
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

    /// <summary>Finds or creates a named language beneath LanguageElement.</summary>
    public static Thought EnsureLanguage(string languageLabel)
    {
        var theUKS = MainWindow.theUKS;
        if (theUKS is null || string.IsNullOrWhiteSpace(languageLabel))
            return null;

        Thought languageElement = theUKS.GetOrAddThought("LanguageElement", "Thought");
        Thought languageRoot = theUKS.GetOrAddThought("Language", languageElement);
        Thought retVal = theUKS.GetOrAddThought(languageLabel.Trim(), languageRoot);
        return retVal;
    }

    /// <summary>
    /// Removes weak competing meanings after one meaning for a word has exceeded the consolidation threshold.
    /// </summary>
    private static void ConsolidateMeanings(IEnumerable<Link> plasticMeanings, ICollection<string> changes)
    {
        float winnerThreshold = Math.Max(0, MeaningConsolidationThreshold);
        float discardThreshold = Math.Max(0, MeaningConsolidationDiscardThreshold);
        foreach (IGrouping<Thought, Link> wordMeanings in plasticMeanings
            .Where(link => link.From is not null)
            .GroupBy(link => link.From))
        {
            List<Link> currentMeanings = wordMeanings
                .Where(link => link.From?.LinksTo.Contains(link) == true)
                .ToList();
            if (!currentMeanings.Any(link => link.Weight >= winnerThreshold)) continue;

            foreach (Link weakMeaning in currentMeanings
                .Where(link => link.Weight < discardThreshold)
                .ToList())
            {
                changes?.Add($"{weakMeaning.From?.Label} -> {weakMeaning.To?.Label}: removed after consolidation");
                weakMeaning.From?.RemoveLink(weakMeaning);
            }
        }
    }

    /// <summary>
    /// Resolves a word meaning or creates a provisional identity meaning when no evidence is available.
    /// </summary>
    private static Thought GetOrCreateMeaning(Thought word)
    {
        var theUKS = MainWindow.theUKS;
        Thought meansType = theUKS.GetOrAddThought("means", "LinkType");
        string label = word.Label.StartsWith("w:", StringComparison.OrdinalIgnoreCase)
            ? word.Label[2..]
            : word.Label;
        Thought numericMeaning = GetCanonicalNumberMeaning(label);
        Link meaningLink = GetBestMeaning(word);
        Thought meaning = meaningLink?.To;
        if (numericMeaning is not null)
        {
            if (meaning != numericMeaning)
            {
                word.RemoveLinks(meansType);
                theUKS.AddStatement(word, meansType, numericMeaning);
            }
            return numericMeaning;
        }
        if (meaning is not null) return meaning;
        List<Link> existingMeanings = word.LinksTo
            .Where(link => link.LinkType == meansType && link.To is not null)
            .ToList();
        // Structural nodes are excluded from ordinary co-occurrence grounding,
        // but an action exemplar may explicitly use one as a semantic endpoint
        // (for example, w:object -> Object). Preserve that supervised identity
        // instead of treating the excluded candidate as an unresolved meaning.
        Thought explicitIdentity = existingMeanings
            .Where(link => BlockedMeaningLabels.Contains(link.To.Label))
            .Select(link => link.To)
            .FirstOrDefault(candidate => IsIdentityMeaning(word, candidate));
        if (explicitIdentity is not null) return explicitIdentity;
        if (existingMeanings.Count > 0)
            return null;

        meaning = InferMeaningFromSpellingPattern(word);
        if (meaning is not null)
        {
            theUKS.AddStatement(word, meansType, meaning);
            return meaning;
        }

        meaning = theUKS.GetOrAddThought(label);
        theUKS.AddStatement(word, meansType, meaning);
        return meaning;
    }

    /// <summary>Returns the canonical number Thought represented by a numeric surface form.</summary>
    private static Thought GetCanonicalNumberMeaning(string surfaceForm)
    {
        string numberLabel = null;
        if (int.TryParse(surfaceForm, out int numericValue))
            numberLabel = numericValue.ToString();

        Thought retVal = null;
        if (numberLabel is not null)
        {
            var theUKS = MainWindow.theUKS;
            retVal = theUKS.Labeled(numberLabel) ??
                theUKS.GetOrAddThought(numberLabel, "number");
        }
        return retVal;
    }

    /// <summary>
    /// Infers a word meaning from a learned correspondence between source and target spelling classes.
    /// </summary>
    private static Thought InferMeaningFromSpellingPattern(Thought word)
    {
        var theUKS = MainWindow.theUKS;
        Thought patternRoot = theUKS.Labeled("SpellingPattern");
        if (patternRoot is null)
        {
            Thought noMeaning = null;
            return noMeaning;
        }

        string surfaceSpelling = word.Label.StartsWith(
            "w:", StringComparison.OrdinalIgnoreCase)
            ? word.Label[2..]
            : word.Label;
        List<(Thought pattern, Thought sourceClass, string baseSpelling)> candidates = new();
        foreach (Thought pattern in patternRoot.Children)
        {
            Thought position = pattern.GetTargetOfFirstLinkOfType("position");
            string addition = GetSpellingPatternAddition(pattern);
            if (string.IsNullOrEmpty(addition))
                continue;

            string baseSpelling = null;
            if (position?.Label == "end" &&
                surfaceSpelling.EndsWith(addition, StringComparison.OrdinalIgnoreCase))
            {
                baseSpelling = surfaceSpelling[..^addition.Length];
            }
            else if (position?.Label == "beginning" &&
                surfaceSpelling.StartsWith(addition, StringComparison.OrdinalIgnoreCase))
            {
                baseSpelling = surfaceSpelling[addition.Length..];
            }
            if (string.IsNullOrEmpty(baseSpelling))
                continue;

            foreach ((Thought sourceClass, Thought targetClass) in
                GetSpellingPatternClassPairs(pattern))
            {
                if (word.Parents.Contains(targetClass))
                    candidates.Add((pattern, sourceClass, baseSpelling));
            }
        }

        List<string> possibleMeanings = candidates
            .Select(candidate => candidate.baseSpelling)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (possibleMeanings.Count != 1)
        {
            Thought noMeaning = null;
            return noMeaning;
        }

        string inferredLabel = possibleMeanings[0];
        Thought knownSourceWord = candidates
            .SelectMany(candidate => candidate.sourceClass.Children)
            .FirstOrDefault(candidate =>
                GetSpelling(candidate).Equals(
                    inferredLabel, StringComparison.OrdinalIgnoreCase));
        Thought retVal = knownSourceWord is not null
            ? GetOrCreateMeaning(knownSourceWord)
            : theUKS.GetOrAddThought(inferredLabel.ToLowerInvariant());
        return retVal;
    }

    /// <summary>Returns the text added by a learned spelling pattern.</summary>
    private static string GetSpellingPatternAddition(Thought pattern)
    {
        var theUKS = MainWindow.theUKS;
        Link addition = pattern.LinksTo.FirstOrDefault(
            link => link.LinkType?.Label == "adds");
        if (addition?.To is null)
        {
            string noAddition = "";
            return noAddition;
        }
        if (addition.To is not SeqElement)
        {
            string retVal = addition.To.Label.StartsWith(
                "c:", StringComparison.OrdinalIgnoreCase)
                ? addition.To.Label[2..]
                : addition.To.Label;
            return retVal;
        }

        SequenceView additionSequence = theUKS.GetSequenceViews(pattern)
            .FirstOrDefault(view => view.LinkType?.Label == "adds");
        string sequenceText = additionSequence is null
            ? ""
            : string.Concat(additionSequence.Elements.Select(element =>
                element.Label.StartsWith("c:", StringComparison.OrdinalIgnoreCase)
                    ? element.Label[2..]
                    : element.Label));
        return sequenceText;
    }

    private static List<(Thought sourceClass, Thought targetClass)> GetSpellingPatternClassPairs(Thought pattern)
    {
        List<(Thought sourceClass, Thought targetClass)> retVal =
            pattern.LinksTo
                .Where(link => link.LinkType?.Label == "classEvidence")
                .Select(link => link.To)
                .OfType<Link>()
                .Where(link => link.LinkType?.Label == "correspondsTo" &&
                    link.From is not null && link.To is not null)
                .Select(link => (link.From, link.To))
                .Distinct()
                .ToList();
        if (retVal.Count > 0)
            return retVal;

        // Compatibility with spelling patterns created before class-pair
        // evidence was introduced. Consolidation converts these direct links.
        List<Thought> sourceClasses = pattern.LinksTo
            .Where(link => link.LinkType?.Label == "sourceClass")
            .Select(link => link.To)
            .Where(sourceClass => sourceClass is not null)
            .ToList();
        List<Thought> targetClasses = pattern.LinksTo
            .Where(link => link.LinkType?.Label == "targetClass")
            .Select(link => link.To)
            .Where(targetClass => targetClass is not null)
            .ToList();
        int pairCount = Math.Min(sourceClasses.Count, targetClasses.Count);
        for (int index = 0; index < pairCount; index++)
            retVal.Add((sourceClasses[index], targetClasses[index]));
        return retVal;
    }

    /// <summary>Reports whether a provisional meaning simply repeats the spelling of its word.</summary>
    private static bool IsIdentityMeaning(Thought word, Thought meaning)
    {
        string wordLabel = word.Label.StartsWith("w:", StringComparison.OrdinalIgnoreCase)
            ? word.Label[2..]
            : word.Label;
        bool retVal = meaning.Label.Equals(
            wordLabel, StringComparison.OrdinalIgnoreCase);
        return retVal;
    }

    /// <summary>Returns the non-wildcard word members of a learned class.</summary>
    private static List<Thought> GetOrdinaryWordMembers(Thought learnedClass)
    {
        List<Thought> retVal = learnedClass.Children
            .Where(member => member is not SeqElement &&
                !member.HasAncestor("Wildcard") &&
                (member.HasAncestor("Word") ||
                    member.Label.StartsWith("w:", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return retVal;
    }

    /// <summary>Returns a word's spelling sequence, falling back to its label when no sequence exists.</summary>
    private static string GetSpelling(Thought word)
    {
        var theUKS = MainWindow.theUKS;
        SequenceView spelling = theUKS.GetSequenceViews(word)
            .FirstOrDefault(view => view.LinkType?.Label == "spelled");
        string retVal;
        if (spelling is not null)
        {
            retVal = string.Concat(spelling.Elements.Select(element =>
                element.Label.StartsWith("c:", StringComparison.OrdinalIgnoreCase)
                    ? element.Label[2..]
                    : element.Label));
        }
        else
        {
            retVal = word.Label.StartsWith("w:", StringComparison.OrdinalIgnoreCase)
                ? word.Label[2..]
                : word.Label;
        }
        return retVal;
    }

    // 4. Building Phrase Templates

    /// <summary>
    /// Applies already learned templates to one manually entered phrase. An
    /// unclassified word may occupy a class wildcard; a successful match then
    /// adds that word to the wildcard's learned class.
    /// </summary>
    /// <returns>The selected learned template, or null when none matches.</returns>
    public static Thought ApplyExistingTemplatesToPhrase(Thought phrase)
    {
        bool isQuestion = phrase?.Parents.Any(parent =>
            parent.Label.Equals("Question", StringComparison.OrdinalIgnoreCase)) == true;
        Thought retVal = ApplyExistingTemplatesToPhrase(phrase, isQuestion);
        return retVal;
    }

    /// <summary>
    /// Matches a phrase against one template population. The caller can test
    /// question templates independently of punctuation and inspect the answer
    /// returned by SubmitText.
    /// </summary>
    public static Thought ApplyExistingTemplatesToPhrase(Thought phrase, bool useQuestionTemplates)
    {
        var theUKS = MainWindow.theUKS;
        Thought templateRoot = GetLearnedTemplateCategory(useQuestionTemplates);
        Thought learnedClassRoot = theUKS.Labeled("LearnedClass");
        if (phrase is null || templateRoot is null || learnedClassRoot is null)
            return null;

        SequenceView phraseSequence = theUKS.GetSequenceViews(phrase)
            .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
        if (phraseSequence is null) return null;

        Thought searchOptions = theUKS.Labeled("TemplateLearningSearch");
        if (searchOptions is null) return null;

        Dictionary<Thought, float> matchingTemplates = new();
        foreach ((SeqElement seqNode, float confidence) in
            theUKS.FindSequencesByActivation(
                phraseSequence.Elements.ToList(), searchOptions))
        {
            foreach (Thought owner in seqNode.FRST.LinksFrom
                .Where(link => link.LinkType?.Label == "hasWords" &&
                    link.From is not null &&
                    templateRoot.Children.Contains(link.From))
                .Select(link => link.From))
            {
                if (!matchingTemplates.TryGetValue(owner, out float previous) ||
                    confidence > previous)
                    matchingTemplates[owner] = confidence;
            }
        }

        // Prefer a template whose classes already explain the largest number
        // of phrase elements. Fixed elements and accumulated evidence break
        // ties. This keeps a known noun/noun frame ahead of a noun/adjective
        // frame once those classes have begun to separate.
        Thought foundTemplate = matchingTemplates
            .Select(candidate =>
            {
                SequenceView sequence = theUKS.GetSequenceViews(candidate.Key)
                    .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
                int classifiedMatches = sequence is null ? 0 : sequence.Elements
                    .Select((element, position) => (element, position))
                    .Count(item => item.position < phraseSequence.Elements.Count &&
                        item.element.HasAncestor("Wildcard") &&
                        item.element.Parents.Any(parent =>
                            parent != theUKS.Labeled("Wildcard") &&
                            phraseSequence.Elements[item.position].HasAncestor(parent)));
                int fixedElements = sequence?.Elements.Count(element =>
                    !element.HasAncestor("Wildcard")) ?? 0;
                int evidence = candidate.Key.LinksTo.Count(link =>
                    link.LinkType?.Label == "evidence");
                bool hasAction = GetParameterizedTemplateOperation(candidate.Key) is not null;
                var retVal = (template: candidate.Key, confidence: candidate.Value,
                    classifiedMatches, fixedElements, evidence, hasAction);
                return retVal;
            })
            .OrderByDescending(candidate => candidate.hasAction)
            .ThenByDescending(candidate => candidate.classifiedMatches)
            .ThenByDescending(candidate => candidate.fixedElements)
            .ThenByDescending(candidate => candidate.confidence)
            .ThenByDescending(candidate => candidate.evidence)
            .ThenBy(candidate => candidate.template.Label, StringComparer.Ordinal)
            .Select(candidate => candidate.template)
            .FirstOrDefault();
        if (foundTemplate is null) return null;

        Thought evidenceType = theUKS.GetOrAddThought("evidence", "LinkType");
        SequenceView templateSequence = theUKS.GetSequenceViews(foundTemplate)
            .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
        if (templateSequence is null ||
            templateSequence.Elements.Count != phraseSequence.Elements.Count)
            return null;

        for (int position = 0; position < templateSequence.Elements.Count; position++)
        {
            Thought wildcard = templateSequence.Elements[position];
            if (!wildcard.HasAncestor("Wildcard") || !wildcard.HasProperty("isWildcard"))
                continue;
            Thought valueClass = wildcard.Parents.FirstOrDefault(
                parent => learnedClassRoot.Children.Contains(parent));
            if (valueClass is not null)
                phraseSequence.Elements[position].AddParent(valueClass);
        }

        theUKS.AddStatement(foundTemplate, evidenceType, phrase);
        foundTemplate.Weight = Math.Max(foundTemplate.Weight,
            foundTemplate.LinksTo.Count(link => link.LinkType == evidenceType));
        return foundTemplate;
    }
    /// <summary>
    /// Discovers learned templates from every Phrase hasWords sequence currently
    /// in the UKS. Phrase owners become evidence; wildcard fillers become
    /// ordinary learned classes.
    /// </summary>
    public static List<Thought> DiscoverPhraseTemplates(int minMembers = 30, int minFixedElements = 2)
    {
        List<Thought> retVal = DiscoverTemplatesForPhraseKind(
            "Statement", false, "LearnedClass",
            minMembers, minFixedElements);
        return retVal;
    }

    /// <summary>Discovers query templates from stored Question observations.</summary>
    public static List<Thought> DiscoverQuestionTemplates(int minMembers = 5, int minFixedElements = 2)
    {
        List<Thought> retVal = DiscoverTemplatesForPhraseKind(
            "Question", true, "LearnedClass",
            minMembers, minFixedElements);
        return retVal;
    }

    /// <summary>Runs common sequence-template discovery for one stored phrase population.</summary>
    private static List<Thought> DiscoverTemplatesForPhraseKind(
        string phraseKind,
        bool query,
        string classRootLabel,
        int minMembers,
        int minFixedElements)
    {
        var theUKS = MainWindow.theUKS;
        List<Thought> phrases = GetPhrasesOfKind(phraseKind);
        if (phrases.Count == 0)
        {
            List<Thought> noTemplates = new();
            return noTemplates;
        }

        Thought templateRoot = GetLearnedTemplateCategory(query);
        Thought fillerClassRoot = theUKS.GetOrAddThought(classRootLabel, "LanguageElement");
        List<SequenceView> phraseObservations = theUKS.GetSequenceViews(phrases)
            .Where(view => view.LinkType?.Label == "hasWords")
            .ToList();
        List<Thought> retVal = theUKS.DiscoverSequenceTemplates(
            phraseObservations,
            templateRoot,
            fillerClassRoot,
            minMembers,
            minFixedElements);
        return retVal;
    }

    /// <summary>
    /// Creates classes of fixed word Thoughts from the structural positions
    /// they occupy in learned templates. Words immediately before a class slot
    /// form a boundary-token population; the first fixed word after a class
    /// slot forms a connector-token population. The names "article" and
    /// "verb" are interpretations of the resulting anonymous classes, not
    /// assumptions made by this method.
    /// </summary>
    public static List<Thought> DiscoverTemplateTokenClasses(int minDistinctMembers = 2)
    {
        if (minDistinctMembers < 1)
            throw new ArgumentOutOfRangeException(nameof(minDistinctMembers));

        var theUKS = MainWindow.theUKS;
        List<Thought> templates = GetLearnedTemplateCategories()
            .SelectMany(category => category.Children)
            .ToList();
        if (templates.Count == 0)
        {
            List<Thought> noClasses = new();
            return noClasses;
        }

        Thought classRoot = theUKS.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought evidenceType = theUKS.GetOrAddThought("evidence", "LinkType");
        HashSet<Thought> boundaryMembers = new();
        HashSet<Thought> connectorMembers = new();
        HashSet<Thought> boundaryEvidence = new();
        HashSet<Thought> connectorEvidence = new();

        foreach (Thought template in templates)
        {
            SequenceView sequence = theUKS.GetSequenceViews(template)
                .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
            if (sequence is null) continue;

            List<int> classSlots = sequence.Elements
                .Select((element, position) => (element, position))
                .Where(item => GetWildcardClass(item.element, classRoot) is not null)
                .Select(item => item.position)
                .ToList();
            if (classSlots.Count == 0) continue;

            // A fixed token immediately before the first class slot is a
            // boundary filler. With the current corpus this population starts
            // with "a" and "the".
            int firstSlot = classSlots[0];
            if (firstSlot > 0)
            {
                Thought boundary = sequence.Elements[firstSlot - 1];
                if (!boundary.HasAncestor("Wildcard"))
                {
                    boundaryMembers.Add(boundary);
                    boundaryEvidence.Add(template);
                }
            }

            for (int slotIndex = 0; slotIndex < classSlots.Count; slotIndex++)
            {
                int segmentStart = classSlots[slotIndex] + 1;
                int segmentEnd = slotIndex + 1 < classSlots.Count
                    ? classSlots[slotIndex + 1]
                    : sequence.Elements.Count;
                List<Thought> fixedSegment = sequence.Elements
                    .Skip(segmentStart)
                    .Take(segmentEnd - segmentStart)
                    .Where(element => !element.HasAncestor("Wildcard"))
                    .ToList();
                if (fixedSegment.Count == 0) continue;

                // The first fixed value after a class slot connects that slot
                // to what follows. In the English corpus this gathers words
                // such as is/are, has/have, can, eat/eats, and play/plays.
                connectorMembers.Add(fixedSegment[0]);
                connectorEvidence.Add(template);

                // When another class slot follows, any final fixed value after
                // the connector also occupies a class boundary. This lets "a"
                // link the leading {a,the} population with the internal
                // {a,an} population without spelling out either word.
                if (slotIndex + 1 < classSlots.Count && fixedSegment.Count > 1)
                {
                    boundaryMembers.Add(fixedSegment[^1]);
                    boundaryEvidence.Add(template);
                }
            }
        }

        List<Thought> results = new();
        AddTokenClass(boundaryMembers, boundaryEvidence);
        AddTokenClass(connectorMembers, connectorEvidence);
        return results;

        void AddTokenClass(HashSet<Thought> members, HashSet<Thought> evidence)
        {
            if (members.Count < minDistinctMembers) return;
            Thought learnedClass = theUKS.GetOrCreateThoughtClass(classRoot, members);
            foreach (Thought template in evidence)
                theUKS.AddStatement(learnedClass, evidenceType, template);
            learnedClass.Weight = Math.Max(learnedClass.Weight, evidence.Count);
            if (!results.Contains(learnedClass)) results.Add(learnedClass);
        }
    }

    /// <summary>Finds or creates the Assertion or Query branch beneath the common LearnedTemplate root.</summary>
    private static Thought GetLearnedTemplateCategory(bool query)
    {
        var theUKS = MainWindow.theUKS;
        Thought root = theUKS.GetOrAddThought("LearnedTemplate", "LanguageElement");
        Thought assertionRoot = theUKS.GetOrAddThought("Assertion", root);
        Thought queryRoot = theUKS.GetOrAddThought("Query", root);

        // Migrate templates created by the previous two-root layout.
        foreach (Thought template in root.Children
            .Where(child => child != assertionRoot && child != queryRoot)
            .ToList())
        {
            template.AddParent(assertionRoot);
            template.RemoveParent(root);
        }

        Thought oldQueryRoot = theUKS.Labeled("LearnedQuestionTemplate");
        if (oldQueryRoot is not null && oldQueryRoot != queryRoot)
        {
            foreach (Thought template in oldQueryRoot.Children.ToList())
            {
                template.AddParent(queryRoot);
                template.RemoveParent(oldQueryRoot);
            }
            oldQueryRoot.Delete();
        }

        Thought retVal = query ? queryRoot : assertionRoot;
        return retVal;
    }

    /// <summary>Enumerates the assertion and query template populations.</summary>
    private static IEnumerable<Thought> GetLearnedTemplateCategories()
    {
        Thought assertionRoot = GetLearnedTemplateCategory(query: false);
        yield return assertionRoot;
        Thought queryRoot = GetLearnedTemplateCategory(query: true);
        yield return queryRoot;
    }

    /// <summary>Returns the learned class constraining a wildcard template element.</summary>
    private static Thought GetWildcardClass(Thought value, Thought learnedClassRoot)
    {
        if (value is null || learnedClassRoot is null || !value.HasAncestor("Wildcard")) return null;
        Thought retVal = value.Parents.FirstOrDefault(learnedClassRoot.Children.Contains);
        return retVal;
    }

    // 5. Building Grammar Rules from Exemplars

    /// <summary>
    /// Records a supervised example which pairs an observed phrase with the
    /// concrete SET relationship or TEST query it should produce. The action
    /// text uses the ordinary algorithm notation, for example
    /// [dog-&gt;SET.can-&gt;bark] or [dog-&gt;TEST.is-a-&gt;??].
    /// </summary>
    public static Thought AddActionExemplar(string phrase, string actionText)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            throw new ArgumentException("An action exemplar requires a phrase.", nameof(phrase));
        if (string.IsNullOrWhiteSpace(actionText))
            throw new ArgumentException("An action exemplar requires an action.", nameof(actionText));

        string trimmedAction = actionText.Trim();
        Match filteredActionParts = Regex.Match(trimmedAction,
            @"^\[\s*\[\s*(.*?)\s*->\s*(.*?)\s*->\s*(.*?)\s*\]\s*->\s*filterBy\s*->\s*(.*?)\s*\]$",
            RegexOptions.IgnoreCase);
        Match actionParts = filteredActionParts.Success
            ? filteredActionParts
            : Regex.Match(trimmedAction, @"^\[\s*(.*?)\s*->\s*(.*?)\s*->\s*(.*?)\s*\]$");
        if (!actionParts.Success)
            throw new FormatException(
                $"Action exemplar '{actionText}' must use " +
                "[source->SET.type->target], [source->TEST.type->target], or a filtered TEST relationship.");

        string sourceLabel = actionParts.Groups[1].Value.Trim();
        string setTypeLabel = actionParts.Groups[2].Value.Trim();
        string targetLabel = actionParts.Groups[3].Value.Trim();
        string filterTargetLabel = filteredActionParts.Success ? filteredActionParts.Groups[4].Value.Trim() : null;
        bool isSet = setTypeLabel.StartsWith("SET.", StringComparison.OrdinalIgnoreCase);
        bool isTest = setTypeLabel.StartsWith("TEST.", StringComparison.OrdinalIgnoreCase);
        if (sourceLabel.Length == 0 || targetLabel.Length == 0 ||
            (!isSet && !isTest) || (filteredActionParts.Success && (!isTest || filterTargetLabel.Length == 0)))
            throw new FormatException(
                $"Action exemplar '{actionText}' must use " +
                "[source->SET.type->target], [source->TEST.type->target], or a filtered TEST relationship.");

        var theUKS = MainWindow.theUKS;
        Thought exemplarRoot = theUKS.GetOrAddThought("ActionExemplar", "LanguageElement");
        Thought exemplar = theUKS.GetOrAddThought("actionExemplar*", exemplarRoot);
        List<Thought> words = GetPhraseWords(phrase);
        if (words.Count < 2) throw new FormatException("An action exemplar phrase requires at least two words.");
        // Supervised evidence must remain reproducible. A plastic word can otherwise be pruned by ModuleWord and
        // every sequence position which referenced it is then repaired to "--".
        foreach (Thought word in words) word.isPlastic = false;
        Thought hasWords = theUKS.GetOrAddThought("hasWords", "LinkType");
        theUKS.AddSequenceAndLink(exemplar, hasWords, words);

        // Creating the operation root first ensures dotted operation types
        // inherit from LinkType through SET or TEST and from the relationship
        // type they operate on.
        theUKS.GetOrAddThought(isSet ? "SET" : "TEST", "LinkType");
        int operationSeparator = setTypeLabel.IndexOf('.');
        string relationshipLabel = setTypeLabel[(operationSeparator + 1)..];
        theUKS.GetOrAddThought(relationshipLabel, "LinkType");
        Thought setType = theUKS.GetOrAddThought(setTypeLabel, "LinkType");
        Thought source = ResolveActionEndpoint(sourceLabel);
        Thought target = ResolveActionEndpoint(targetLabel);
        Link action = new(source, setType, target);
        if (filterTargetLabel is not null)
        {
            Thought filterBy = GetFilterByRelationship();
            Thought filterTarget = ResolveActionEndpoint(filterTargetLabel);
            action.AddLink(filterBy, filterTarget);
        }
        theUKS.AddStatement(exemplar, theUKS.GetOrAddThought("demonstrates", "LinkType"), action);
        if (isSet) theUKS.ApplySetAction(action);
        return exemplar;
    }

    /// <summary>
    /// Learns phrase-to-action mappings from ActionExemplar observations.
    /// Exemplars are separated by SET type before sequence discovery so that
    /// the same surface connector can acquire different syntactic meanings.
    /// </summary>
    /// <returns>The number of learned templates which now produce an action.</returns>
    public static int LearnActionsFromExemplars(int minExamples = 2)
    {
        if (minExamples < 2)
            throw new ArgumentOutOfRangeException(nameof(minExamples));

        var theUKS = MainWindow.theUKS;
        Thought exemplarRoot = theUKS.Labeled("ActionExemplar");
        if (exemplarRoot is null) return 0;

        Thought meansType = theUKS.GetOrAddThought("means", "LinkType");
        Thought evidenceType = theUKS.GetOrAddThought("evidence", "LinkType");
        Dictionary<Thought, Dictionary<Thought, HashSet<Thought>>> relationshipPhraseEvidence = new();
        HashSet<(Thought relationship, Thought exemplar, Thought phrase)> recordedEvidence = new();
        int learnedCount = 0;

        var actionExamples = exemplarRoot.Children
            .Select(exemplar => (
                exemplar,
                action: exemplar.GetTargetOfFirstLinkOfType("demonstrates") as Link))
            .Where(item =>
                item.action?.LinkType?.HasAncestor("SET") == true ||
                item.action?.LinkType?.HasAncestor("TEST") == true)
            .Select(item =>
            {
                Thought modifier = GetNumericActionModifier(item.action.LinkType);
                Thought actionType = modifier is null
                    ? item.action.LinkType
                    : GetNumericActionBaseType(item.action.LinkType);
                Thought filterTarget = item.action.GetTargetOfFirstLinkOfType("filterBy");
                var retVal = (item.exemplar, item.action, actionType, modifier, filterTarget);
                return retVal;
            })
            .ToList();
        foreach (var example in actionExamples)
        {
            SequenceView sequence = theUKS.GetSequenceViews(example.exemplar)
                .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
            RecordRelationshipPhraseEvidence(
                sequence,
                example.exemplar,
                example.action,
                relationshipPhraseEvidence,
                recordedEvidence);
        }

        var actionGroups = actionExamples.GroupBy(item =>
            (item.actionType, hasModifier: item.modifier is not null, hasFilter: item.filterTarget is not null));

        foreach (var actionGroup in actionGroups)
        {
            bool isQuestionAction = actionGroup.Key.actionType.HasAncestor("TEST");
            Thought templateRoot = GetLearnedTemplateCategory(isQuestionAction);
            // Questions and statements may share lexical populations even
            // though their templates must never compete.
            Thought classRoot = theUKS.GetOrAddThought("LearnedClass", "LanguageElement");
            HashSet<Thought> groupExemplars = actionGroup
                .Select(item => item.exemplar)
                .ToHashSet();
            List<SequenceView> observations = theUKS.GetSequenceViews(groupExemplars)
                .Where(view => view.LinkType?.Label == "hasWords")
                .ToList();
            if (observations.Count < minExamples) continue;

            List<Thought> templates = theUKS.DiscoverSequenceTemplates(
                observations,
                templateRoot,
                classRoot,
                minExamples,
                minFixedElements: 1,
                templateLabel: "actionTemplate*",
                classLabel: "actionClass*");

            foreach (Thought template in templates)
            {
                SequenceView templateSequence = theUKS.GetSequenceViews(template)
                    .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
                if (templateSequence is null) continue;

                List<int> parameterPositions = templateSequence.Elements
                    .Select((element, position) => (element, position))
                    .Where(item => item.element.HasProperty("isWildcard"))
                    .Select(item => item.position)
                    .ToList();
                List<Thought> templateExemplars = template.LinksTo
                    .Where(link => link.LinkType?.Label == "evidence" &&
                        groupExemplars.Contains(link.To))
                    .Select(link => link.To)
                    .ToList();
                int modifierPosition = actionGroup.Key.hasModifier
                    ? FindNumericActionModifierPosition(templateExemplars)
                    : -1;
                List<int> relationshipParameterPositions = parameterPositions
                    .Where(position => position != modifierPosition)
                    .ToList();
                if (relationshipParameterPositions.Count == 0) continue;

                int sourcePosition = FindActionEndpointPosition(
                    templateSequence,
                    templateExemplars,
                    useSource: true,
                    relationshipParameterPositions);
                if (sourcePosition < 0)
                    sourcePosition = relationshipParameterPositions[0];
                int targetPosition = FindActionEndpointPosition(
                    templateSequence,
                    templateExemplars,
                    useSource: false,
                    relationshipParameterPositions);
                if (targetPosition < 0 && relationshipParameterPositions.Count > 1)
                    targetPosition = relationshipParameterPositions[^1];
                Thought sourceParameter = templateSequence.Elements[sourcePosition];
                Thought targetParameter = targetPosition >= 0
                    ? templateSequence.Elements[targetPosition]
                    : GetConstantActionTarget(templateExemplars);
                if (targetParameter is null) continue;
                Thought modifierParameter = modifierPosition >= 0
                    ? templateSequence.Elements[modifierPosition]
                    : null;
                Thought filterParameter = actionGroup.Key.hasFilter
                    ? GetConstantActionFilterTarget(templateExemplars)
                    : null;
                if (actionGroup.Key.hasFilter && filterParameter is null) continue;
                Link parameterizedAction = template.LinksTo
                    .Where(link => link.LinkType == meansType)
                    .Select(link => link.To)
                    .OfType<Link>()
                    .FirstOrDefault(action =>
                        action.From == sourceParameter &&
                        action.LinkType == actionGroup.Key.actionType &&
                        action.To == targetParameter &&
                        action.GetTargetOfFirstLinkOfType("linkTypeParameter") ==
                            modifierParameter &&
                        action.GetTargetOfFirstLinkOfType("filterBy") == filterParameter);
                parameterizedAction ??= new Link(
                    sourceParameter, actionGroup.Key.actionType, targetParameter);
                theUKS.AddStatement(template, meansType, parameterizedAction);
                if (modifierParameter is not null)
                {
                    theUKS.AddStatement(parameterizedAction,
                        theUKS.GetOrAddThought("linkTypeParameter", "LinkType"),
                        modifierParameter);
                }
                if (filterParameter is not null)
                    theUKS.AddStatement(parameterizedAction, GetFilterByRelationship(), filterParameter);

                foreach (Thought exemplar in templateExemplars)
                {
                    Link concreteAction =
                        exemplar.GetTargetOfFirstLinkOfType("demonstrates") as Link;
                    SequenceView exemplarSequence = theUKS.GetSequenceViews(exemplar)
                        .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
                    if (concreteAction?.From is null || concreteAction.To is null ||
                        exemplarSequence is null)
                        continue;

                    // These are the ordinary lexical meaning links used by
                    // interactive text input: w:X means X. Different exemplars may later
                    // teach multiple meanings without changing this structure.
                    AddEndpointMeaning(
                        exemplarSequence.Elements[sourcePosition],
                        concreteAction.From,
                        meansType);
                    if (targetPosition >= 0)
                    {
                        AddEndpointMeaning(
                            exemplarSequence.Elements[targetPosition],
                            concreteAction.To,
                            meansType);
                    }
                    if (modifierPosition >= 0)
                    {
                        Thought modifier = GetNumericActionModifier(concreteAction.LinkType);
                        if (modifier is not null)
                        {
                            theUKS.AddStatement(
                                exemplarSequence.Elements[modifierPosition], meansType, modifier);
                        }
                    }
                    theUKS.AddStatement(parameterizedAction, evidenceType, exemplar);
                }
                learnedCount++;
            }
        }
        ApplyRelationshipPhraseEvidence(relationshipPhraseEvidence, meansType);
        return learnedCount;
    }

    /// <summary>
    /// Instantiates the SET action or TEST query learned for a matched template.
    /// </summary>
    /// <returns>The asserted ordinary relationship, or null if the template has no action.</returns>
    public static Link ApplyLearnedTemplateAction(Thought template, Thought phrase)
    {
        if (template is null || phrase is null) return null;

        var theUKS = MainWindow.theUKS;
        Link parameterizedAction = GetParameterizedTemplateOperation(template);
        if (parameterizedAction?.From is null || parameterizedAction.To is null)
            return null;

        SequenceView templateSequence = theUKS.GetSequenceViews(template)
            .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
        SequenceView phraseSequence = theUKS.GetSequenceViews(phrase)
            .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
        if (templateSequence is null || phraseSequence is null ||
            templateSequence.Elements.Count != phraseSequence.Elements.Count)
            return null;

        List<Thought> templateElements = templateSequence.Elements.ToList();
        int sourcePosition = templateElements.FindIndex(
            element => element == parameterizedAction.From);
        int targetPosition = templateElements.FindIndex(
            element => element == parameterizedAction.To);
        if (sourcePosition < 0) return null;

        Thought source = GetOrCreateMeaning(phraseSequence.Elements[sourcePosition]);
        Thought target = targetPosition >= 0
            ? GetOrCreateMeaning(phraseSequence.Elements[targetPosition])
            : parameterizedAction.To;
        if (source is null || target is null) return null;
        Thought actionType = parameterizedAction.LinkType;
        Thought linkTypeParameter =
            parameterizedAction.GetTargetOfFirstLinkOfType("linkTypeParameter");
        if (linkTypeParameter is not null)
        {
            int modifierPosition = templateElements.FindIndex(
                element => element == linkTypeParameter);
            if (modifierPosition < 0) return null;
            Thought modifier = GetOrCreateMeaning(
                phraseSequence.Elements[modifierPosition]);
            if (modifier is null) return null;
            actionType = theUKS.GetOrAddThought(
                parameterizedAction.LinkType.Label + "." + modifier.Label,
                "LinkType");
        }

        Link action = new(source, actionType, target);
        Link retVal = actionType.HasAncestor("TEST")
            ? new Link(source, GetOperationRelationshipType(actionType), target)
            : theUKS.ApplySetAction(action);
        Thought filterTarget = parameterizedAction.GetTargetOfFirstLinkOfType("filterBy");
        if (retVal is not null && actionType.HasAncestor("TEST") && filterTarget is not null)
            retVal.AddLink(GetFilterByRelationship(), filterTarget);
        return retVal;
    }

    /// <summary>Attaches a semantic meaning to a complete sequence of words.</summary>
    public static Thought AddPhraseMeaning(IReadOnlyList<Thought> words, Thought meaning)
    {
        if (meaning is null) return null;
        Thought phrase = GetOrCreateMeaningPhrase(words);
        if (phrase is null) return null;
        MainWindow.theUKS.AddStatement(
            phrase,
            MainWindow.theUKS.GetOrAddThought("means", "LinkType"),
            meaning);
        return phrase;
    }

    /// <summary>
    /// Compares pairs of learned word classes and records repeated spelling
    /// correspondences. This is deliberately a discovery step only: it does
    /// not assign word meanings or decide that a spelling pattern is a rule.
    /// </summary>
    /// <param name="minMatchedPairs">
    /// Minimum number of distinct word pairs supporting the same pattern.
    /// </param>
    /// <param name="minCoverage">
    /// Minimum fraction of the smaller class which must participate.
    /// </param>
    /// <param name="minCommonLetters">
    /// Minimum unchanged spelling required in each supporting word pair.
    /// </param>
    /// <returns>The useful spelling-pattern Thoughts which were found.</returns>
    public static List<Thought> DiscoverClassSpellingPatterns(
        int minMatchedPairs = 3,
        float minCoverage = 0.5f,
        int minCommonLetters = 3)
    {
        if (minMatchedPairs < 1)
            throw new ArgumentOutOfRangeException(nameof(minMatchedPairs));
        if (minCoverage <= 0 || minCoverage > 1)
            throw new ArgumentOutOfRangeException(nameof(minCoverage));
        if (minCommonLetters < 1)
            throw new ArgumentOutOfRangeException(nameof(minCommonLetters));

        var theUKS = MainWindow.theUKS;
        Thought classRoot = theUKS.Labeled("LearnedClass");
        if (classRoot is null)
        {
            List<Thought> noPatterns = new();
            return noPatterns;
        }

        Thought patternRoot = theUKS.GetOrAddThought("SpellingPattern", "LanguageElement");
        Thought positionType = theUKS.GetOrAddThought("position", "LinkType");
        Thought addsType = theUKS.GetOrAddThought("adds", "LinkType");
        Thought evidenceType = theUKS.GetOrAddThought("evidence", "LinkType");
        Thought classEvidenceType = theUKS.GetOrAddThought("classEvidence", "LinkType");
        Thought correspondsToType = theUKS.GetOrAddThought("correspondsTo", "LinkType");
        Thought beginning = theUKS.GetOrAddThought("beginning");
        Thought end = theUKS.GetOrAddThought("end");
        ConsolidateSpellingPatterns();

        List<Thought> classes = classRoot.Children
            .Where(learnedClass => GetOrdinaryWordMembers(learnedClass).Count > 0)
            .ToList();
        List<Thought> retVal = new();

        for (int firstIndex = 0; firstIndex < classes.Count - 1; firstIndex++)
        {
            Thought firstClass = classes[firstIndex];
            List<Thought> firstWords = GetOrdinaryWordMembers(firstClass);
            for (int secondIndex = firstIndex + 1; secondIndex < classes.Count; secondIndex++)
            {
                Thought secondClass = classes[secondIndex];
                List<Thought> secondWords = GetOrdinaryWordMembers(secondClass);
                Dictionary<SpellingPatternKey, HashSet<WordCorrespondence>> candidates = new();

                foreach (Thought firstWord in firstWords)
                {
                    string firstSpelling = GetSpelling(firstWord);
                    foreach (Thought secondWord in secondWords)
                    {
                        string secondSpelling = GetSpelling(secondWord);
                        AffixDifference difference = FindAffixDifference(
                            firstClass, firstWord, firstSpelling,
                            secondClass, secondWord, secondSpelling,
                            minCommonLetters);
                        if (difference is null) continue;

                        SpellingPatternKey key = new(
                            difference.SourceClass,
                            difference.TargetClass,
                            difference.Position,
                            difference.AddedText);
                        if (!candidates.TryGetValue(key, out HashSet<WordCorrespondence> pairs))
                        {
                            pairs = new();
                            candidates.Add(key, pairs);
                        }
                        pairs.Add(new WordCorrespondence(
                            difference.SourceWord, difference.TargetWord));
                    }
                }

                foreach ((SpellingPatternKey key, HashSet<WordCorrespondence> pairs) in candidates)
                {
                    int sourcePopulation = GetOrdinaryWordMembers(key.SourceClass).Count;
                    int targetPopulation = GetOrdinaryWordMembers(key.TargetClass).Count;
                    float coverage = pairs.Count /
                        (float)Math.Min(sourcePopulation, targetPopulation);
                    if (pairs.Count < minMatchedPairs || coverage < minCoverage)
                        continue;

                    Thought position = key.Position == AffixPosition.Beginning
                        ? beginning
                        : end;
                    Thought pattern = FindExistingSpellingPattern(
                        patternRoot, position, key.AddedText);
                    pattern ??= theUKS.GetOrAddThought("spellingPattern*", patternRoot);
                    theUKS.AddStatement(pattern, positionType, position);
                    Link classCorrespondence = theUKS.AddStatement(
                        key.SourceClass, correspondsToType, key.TargetClass);
                    theUKS.AddStatement(
                        pattern, classEvidenceType, classCorrespondence);

                    bool alreadyHasAddition = pattern.LinksTo
                        .Any(link => link.LinkType?.Label == "adds");
                    if (!alreadyHasAddition)
                    {
                        List<Thought> addedLetters = key.AddedText
                            .Select(letter => theUKS.GetOrAddThought(
                                "c:" + char.ToUpperInvariant(letter), "letter"))
                            .ToList();
                        if (addedLetters.Count == 1)
                            theUKS.AddStatement(pattern, addsType, addedLetters[0]);
                        else
                            theUKS.AddSequenceAndLink(pattern, addsType, addedLetters);
                    }

                    foreach (WordCorrespondence pair in pairs)
                    {
                        Link correspondence = theUKS.AddStatement(
                            pair.Source, correspondsToType, pair.Target);
                        theUKS.AddStatement(pattern, evidenceType, correspondence);
                    }
                    int evidenceCount = pattern.LinksTo.Count(link =>
                        link.LinkType == evidenceType);
                    pattern.Weight = Math.Max(pattern.Weight, evidenceCount);
                    if (!retVal.Contains(pattern))
                        retVal.Add(pattern);
                }
            }
        }
        return retVal;

        Thought FindExistingSpellingPattern(
            Thought root,
            Thought position,
            string addedText)
        {
            Thought existingPattern = root.Children.FirstOrDefault(candidate =>
                candidate.GetTargetOfFirstLinkOfType("position") == position &&
                GetSpellingPatternAddition(candidate).Equals(
                    addedText, StringComparison.OrdinalIgnoreCase));
            return existingPattern;
        }
    }

    /// <summary>
    /// Merges spelling-pattern nodes which describe the same operation.
    /// Supporting class pairs and word pairs remain as evidence on the one
    /// canonical pattern.
    /// </summary>
    /// <returns>The number of redundant spelling-pattern nodes removed.</returns>
    public static int ConsolidateSpellingPatterns()
    {
        var theUKS = MainWindow.theUKS;
        Thought patternRoot = theUKS.Labeled("SpellingPattern");
        if (patternRoot is null)
        {
            int noMerges = 0;
            return noMerges;
        }

        Thought classEvidenceType = theUKS.GetOrAddThought(
            "classEvidence", "LinkType");
        Thought evidenceType = theUKS.GetOrAddThought("evidence", "LinkType");
        Thought correspondsToType = theUKS.GetOrAddThought(
            "correspondsTo", "LinkType");
        Thought sourceClassType = theUKS.GetOrAddThought(
            "sourceClass", "LinkType");
        Thought targetClassType = theUKS.GetOrAddThought(
            "targetClass", "LinkType");
        int retVal = 0;

        var groups = patternRoot.Children
            .Select(pattern => (
                pattern,
                position: pattern.GetTargetOfFirstLinkOfType("position"),
                addition: GetSpellingPatternAddition(pattern).ToUpperInvariant()))
            .Where(item => item.position is not null &&
                !string.IsNullOrEmpty(item.addition))
            .GroupBy(item => (item.position, item.addition))
            .ToList();
        foreach (var group in groups)
        {
            Thought canonical = group.First().pattern;
            foreach (Thought pattern in group.Select(item => item.pattern).ToList())
            {
                foreach ((Thought sourceClass, Thought targetClass) in
                    GetSpellingPatternClassPairs(pattern))
                {
                    Link classCorrespondence = theUKS.AddStatement(
                        sourceClass, correspondsToType, targetClass);
                    theUKS.AddStatement(
                        canonical, classEvidenceType, classCorrespondence);
                }
                foreach (Thought evidence in pattern.LinksTo
                    .Where(link => link.LinkType == evidenceType)
                    .Select(link => link.To)
                    .Where(evidence => evidence is not null)
                    .ToList())
                {
                    theUKS.AddStatement(canonical, evidenceType, evidence);
                }

                canonical.Weight = Math.Max(canonical.Weight, pattern.Weight);
                if (pattern == canonical) continue;
                theUKS.ReplaceThoughtReferences(pattern, canonical);
                retVal++;
            }
            canonical.RemoveLinks(sourceClassType);
            canonical.RemoveLinks(targetClassType);
        }
        return retVal;
    }

    /// <summary>
    /// Gives the corresponding word forms in discovered spelling patterns the
    /// same semantic meaning. Only a missing meaning or an automatically
    /// created identity meaning is changed; an established different meaning
    /// is preserved.
    /// </summary>
    /// <returns>The number of word meanings assigned or normalized.</returns>
    public static int NormalizeMeaningsFromSpellingPatterns()
    {
        ConsolidateSpellingPatterns();
        int retVal = NormalizeSpellingPatternMeanings();
        return retVal;
    }

    /// <summary>Normalizes word meanings after spelling-pattern nodes are known to be canonical.</summary>
    private static int NormalizeSpellingPatternMeanings()
    {
        var theUKS = MainWindow.theUKS;
        Thought patternRoot = theUKS.Labeled("SpellingPattern");
        if (patternRoot is null)
        {
            int noMeanings = 0;
            return noMeanings;
        }

        Thought meansType = theUKS.GetOrAddThought("means", "LinkType");
        int retVal = 0;
        foreach (Thought pattern in patternRoot.Children)
        {
            List<Link> correspondences = pattern.LinksTo
                .Where(link => link.LinkType?.Label == "evidence")
                .Select(link => link.To)
                .OfType<Link>()
                .Where(link => link.LinkType?.Label == "correspondsTo" &&
                    link.From is not null && link.To is not null)
                .ToList();

            foreach (Link correspondence in correspondences)
            {
                Thought sourceWord = correspondence.From;
                Thought targetWord = correspondence.To;
                Thought canonicalMeaning = GetOrCreateMeaning(sourceWord);
                List<Link> currentMeaningLinks = targetWord.LinksTo
                    .Where(link => link.LinkType == meansType && link.To is not null)
                    .ToList();
                if (currentMeaningLinks.Any(link => link.To == canonicalMeaning))
                    continue;

                // GetOrCreateMeaning uses the word spelling as a provisional
                // identity meaning. It is safe to supersede that placeholder,
                // but a genuinely different learned meaning remains untouched.
                bool onlyIdentityMeanings = currentMeaningLinks.All(link =>
                    IsIdentityMeaning(targetWord, link.To));
                if (!onlyIdentityMeanings)
                    continue;

                foreach (Link currentMeaning in currentMeaningLinks)
                    targetWord.RemoveLink(currentMeaning);
                theUKS.AddStatement(targetWord, meansType, canonicalMeaning);
                retVal++;
            }
        }
        return retVal;
    }

    /// <summary>
    /// Resolves an annotated endpoint label to its established semantic Thought or a new identity meaning.
    /// </summary>
    private static Thought ResolveActionEndpoint(string label)
    {
        var theUKS = MainWindow.theUKS;
        if (theUKS is null || string.IsNullOrWhiteSpace(label)) return null;

        Thought word = theUKS.Labeled("w:" + label.ToLowerInvariant());
        Link groundedMeaning = GetBestMeaning(word);
        Thought exactMeaning = theUKS.Labeled(label) ??
            theUKS.AtomicThoughts.FirstOrDefault(thought =>
                thought is not Link && thought.Label.Equals(
                    label, StringComparison.OrdinalIgnoreCase));
        if (exactMeaning is null)
        {
            Thought retVal = groundedMeaning?.To ?? theUKS.GetOrAddThought(label);
            return retVal;
        }

        // Bracketed action text names semantic Thoughts. A consolidated
        // grounding may deliberately alias that name to a differently labelled
        // internal representation (dog -> class0, Fido -> O1), but a weak
        // co-occurrence such as bark -> O2 must not override an existing bark.
        if (groundedMeaning?.To is not null &&
            groundedMeaning.Weight >= Math.Max(0, MeaningConsolidationThreshold))
        {
            Thought retVal = groundedMeaning.To;
            return retVal;
        }
        return exactMeaning;
    }

    /// <summary>Returns the relationship used to constrain a query target and keeps it beneath Property.</summary>
    private static Thought GetFilterByRelationship()
    {
        var theUKS = MainWindow.theUKS;
        Thought filterBy = theUKS.GetOrAddThought("filterBy", "Property");
        Thought linkType = theUKS.Labeled("LinkType");
        if (linkType is not null && !filterBy.HasAncestor(linkType)) filterBy.AddParent(linkType);
        return filterBy;
    }

    /// <summary>
    /// Aligns a template position with a demonstrated action endpoint using the
    /// endpoint meanings already present in the exemplars. Position order is a
    /// fallback only when the evidence has not grounded either surface form.
    /// </summary>
    private static int FindActionEndpointPosition(
        SequenceView templateSequence,
        IReadOnlyCollection<Thought> exemplars,
        bool useSource,
        IReadOnlyCollection<int> candidatePositions)
    {
        int bestPosition = -1;
        int bestScore = 0;
        foreach (int position in candidatePositions)
        {
            int score = 0;
            foreach (Thought exemplar in exemplars)
            {
                Link action = exemplar.GetTargetOfFirstLinkOfType("demonstrates") as Link;
                Thought endpoint = useSource ? action?.From : action?.To;
                SequenceView sequence = MainWindow.theUKS.GetSequenceViews(exemplar)
                    .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
                if (endpoint is null || sequence is null || position >= sequence.Elements.Count)
                    continue;

                Thought word = sequence.Elements[position];
                string surface = word.Label.StartsWith(
                    "w:", StringComparison.OrdinalIgnoreCase)
                    ? word.Label[2..]
                    : word.Label;
                if (surface.Equals(endpoint.Label, StringComparison.OrdinalIgnoreCase))
                    score += 2;
                else if (GetBestMeaning(word)?.To == endpoint)
                    score++;
            }
            if (score > bestScore)
            {
                bestScore = score;
                bestPosition = position;
            }
        }
        return bestPosition;
    }

    /// <summary>
    /// Records what a demonstrated phrase position supplied to an action. If an
    /// earlier pass used a word label as a temporary concept, and that label is
    /// now itself grounded, the grounded endpoint replaces the temporary alias.
    /// This is how two surface forms demonstrated at the same endpoint converge
    /// without declaring either one singular or plural.
    /// </summary>
    private static void AddEndpointMeaning(Thought word, Thought endpoint, Thought meansType)
    {
        if (word is null || endpoint is null || meansType is null) return;
        var theUKS = MainWindow.theUKS;
        foreach (Link existing in word.LinksTo
            .Where(link => link.LinkType == meansType && link.To is not null &&
                link.To != endpoint)
            .ToList())
        {
            Thought aliasWord = theUKS.Labeled("w:" + existing.To.Label);
            bool temporaryIdentity = IsIdentityMeaning(word, existing.To);
            bool groundedAlias = aliasWord is not null && aliasWord != word &&
                GetBestMeaning(aliasWord)?.To == endpoint;
            if (temporaryIdentity || groundedAlias)
                word.RemoveLink(existing);
        }
        theUKS.AddStatement(word, meansType, endpoint);
    }

    /// <summary>
    /// Treats all fixed words between demonstrated action endpoints as one
    /// phrase which expresses the action's relationship. For example,
    /// "Fido is a dog" paired with Fido--is-a--dog teaches that "is a"
    /// means is-a without assigning grammatical roles to either word.
    /// </summary>
    private static void RecordRelationshipPhraseEvidence(
        SequenceView exemplarSequence,
        Thought exemplar,
        Link concreteAction,
        IDictionary<Thought, Dictionary<Thought, HashSet<Thought>>> evidence,
        ISet<(Thought relationship, Thought exemplar, Thought phrase)> recorded)
    {
        if (exemplarSequence is null || concreteAction?.From is null ||
            concreteAction.To is null || concreteAction.LinkType is null)
            return;

        int sourcePosition = FindExemplarEndpointPosition(
            exemplarSequence, concreteAction.From);
        int targetPosition = FindExemplarEndpointPosition(
            exemplarSequence, concreteAction.To);
        if (sourcePosition < 0 || targetPosition < 0 ||
            sourcePosition == targetPosition)
            return;

        Thought relationship = GetOperationRelationshipType(concreteAction.LinkType);
        if (relationship is null) return;

        int first = Math.Min(sourcePosition, targetPosition) + 1;
        int last = Math.Max(sourcePosition, targetPosition);
        List<Thought> connectorWords = new();
        for (int position = first; position < last; position++)
        {
            Thought word = exemplarSequence.Elements[position];
            if (word is not null) connectorWords.Add(word);
        }
        if (connectorWords.Count == 0) return;

        Thought phrase = GetOrCreateMeaningPhrase(connectorWords);
        if (phrase is null || !recorded.Add((relationship, exemplar, phrase))) return;
        if (!evidence.TryGetValue(
            relationship,
            out Dictionary<Thought, HashSet<Thought>> phraseObservations))
            evidence[relationship] = phraseObservations =
                new Dictionary<Thought, HashSet<Thought>>();
        if (!phraseObservations.TryGetValue(phrase, out HashSet<Thought> exemplars))
            phraseObservations[phrase] = exemplars = new HashSet<Thought>();
        exemplars.Add(exemplar);
    }

    /// <summary>Finds the exemplar word position which most directly expresses one concrete action endpoint.</summary>
    private static int FindExemplarEndpointPosition(SequenceView sequence, Thought endpoint)
    {
        if (sequence is null || endpoint is null) return -1;
        Thought means = MainWindow.theUKS.Labeled("means");
        for (int position = 0; position < sequence.Elements.Count; position++)
        {
            Thought word = sequence.Elements[position];
            string surface = word.Label.StartsWith(
                "w:", StringComparison.OrdinalIgnoreCase)
                ? word.Label[2..]
                : word.Label;
            if (surface.Equals(endpoint.Label, StringComparison.OrdinalIgnoreCase) ||
                (means is not null && word.HasLink(means, endpoint) is not null) ||
                GetBestMeaning(word)?.To == endpoint)
                return position;
        }
        return -1;
    }

    /// <summary>Finds or creates a reusable phrase whose complete word sequence can carry a meaning.</summary>
    private static Thought GetOrCreateMeaningPhrase(IReadOnlyList<Thought> words)
    {
        if (words is null || words.Count == 0) return null;
        var theUKS = MainWindow.theUKS;
        Thought phrase = theUKS.GetOrAddThought("Phrase", "LanguageElement");
        Thought phraseRoot = theUKS.GetOrAddThought("MeaningPhrase", phrase);
        Thought hasWords = theUKS.GetOrAddThought("hasWords", "LinkType");
        Thought meaningPhrase = FindPhraseWithWords(
            theUKS, phraseRoot, hasWords, words.ToList());
        if (meaningPhrase is not null) return meaningPhrase;

        meaningPhrase = theUKS.GetOrAddThought("p*", phraseRoot);
        theUKS.AddSequenceAndLink(meaningPhrase, hasWords, words.ToList());
        return meaningPhrase;
    }

    /// <summary>
    /// Attaches a relationship meaning to connector phrases supported by enough independent exemplars.
    /// </summary>
    private static void ApplyRelationshipPhraseEvidence(
        IDictionary<Thought, Dictionary<Thought, HashSet<Thought>>> evidence,
        Thought meansType)
    {
        var theUKS = MainWindow.theUKS;
        Thought evidenceType = theUKS.GetOrAddThought("evidence", "LinkType");

        foreach ((Thought relationship,
            Dictionary<Thought, HashSet<Thought>> phraseObservations) in evidence)
        {
            int strongest = phraseObservations.Values
                .Select(exemplars => exemplars.Count)
                .DefaultIfEmpty(0)
                .Max();
            if (strongest == 0) continue;

            foreach ((Thought phrase, HashSet<Thought> exemplars) in phraseObservations)
            {
                Link meaning = theUKS.AddStatement(phrase, meansType, relationship);
                if (meaning is null) continue;
                meaning.Weight = Math.Max(
                    meaning.Weight, exemplars.Count / (float)strongest);
                phrase.Weight = Math.Max(phrase.Weight, exemplars.Count);
                foreach (Thought exemplar in exemplars)
                    theUKS.AddStatement(phrase, evidenceType, exemplar);

                SequenceView sequence = theUKS.GetSequenceViews(phrase)
                    .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
                foreach (Thought word in sequence?.Elements ?? Enumerable.Empty<Thought>())
                {
                    Link oldWordMeaning = word.HasLink(meansType, relationship);
                    if (oldWordMeaning is not null)
                        word.RemoveLink(oldWordMeaning);
                }
            }
        }
    }

    /// <summary>Returns the SET or TEST operation attached as the meaning of a learned template.</summary>
    private static Link GetParameterizedTemplateOperation(Thought template)
    {
        if (template is null) return null;
        Link retVal = template.LinksTo
            .Where(link => link.LinkType?.Label == "means")
            .Select(link => link.To)
            .OfType<Link>()
            .FirstOrDefault(operation =>
                operation.LinkType?.HasAncestor("SET") == true ||
                operation.LinkType?.HasAncestor("TEST") == true);
        return retVal;
    }

    /// <summary>Resolves the ordinary relationship type represented by a dotted SET or TEST operation type.</summary>
    private static Thought GetOperationRelationshipType(Thought operationType)
    {
        if (operationType is null) return null;
        int separator = operationType.Label.IndexOf('.');
        if (separator < 0 || separator == operationType.Label.Length - 1)
            return null;
        string relationshipLabel = operationType.Label[(separator + 1)..];
        Thought retVal = MainWindow.theUKS.GetOrAddThought(relationshipLabel, "LinkType");
        return retVal;
    }

    /// <summary>Returns the numeric suffix represented by a specialized action type such as SET.has.4.</summary>
    private static Thought GetNumericActionModifier(Thought actionType)
    {
        Thought retVal = null;
        if (actionType is null) return retVal;

        string[] parts = actionType.Label.Split('.');
        if (parts.Length != 3 ||
            !parts[0].Equals("SET", StringComparison.OrdinalIgnoreCase))
            return retVal;

        var theUKS = MainWindow.theUKS;
        Thought candidate = theUKS.Labeled(parts[2]);
        if (candidate is null && int.TryParse(parts[2], out int numericValue))
            candidate = theUKS.GetOrAddThought(numericValue.ToString(), "number");
        if (candidate?.HasAncestor("number") == true)
            retVal = candidate;
        return retVal;
    }

    /// <summary>Returns the unspecialized action type for a numerically modified action.</summary>
    private static Thought GetNumericActionBaseType(Thought actionType)
    {
        Thought retVal = actionType;
        Thought modifier = GetNumericActionModifier(actionType);
        if (modifier is null) return retVal;

        string[] parts = actionType.Label.Split('.');
        string baseLabel = parts[0] + "." + parts[1];
        retVal = MainWindow.theUKS.GetOrAddThought(baseLabel, "LinkType");
        return retVal;
    }

    /// <summary>Finds the phrase position which consistently supplies an action's numeric modifier.</summary>
    private static int FindNumericActionModifierPosition(IReadOnlyCollection<Thought> exemplars)
    {
        int retVal = -1;
        foreach (Thought exemplar in exemplars)
        {
            Link concreteAction =
                exemplar.GetTargetOfFirstLinkOfType("demonstrates") as Link;
            Thought modifier = GetNumericActionModifier(concreteAction?.LinkType);
            SequenceView sequence = MainWindow.theUKS.GetSequenceViews(exemplar)
                .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
            if (modifier is null || sequence is null) return -1;

            List<int> positions = sequence.Elements
                .Select((word, position) => (word, position))
                .Where(item => GetObservedNumberMeaning(item.word) == modifier)
                .Select(item => item.position)
                .ToList();
            if (positions.Count == 0) continue;
            if (positions.Count != 1) return -1;
            if (retVal < 0)
                retVal = positions[0];
            else if (retVal != positions[0])
                return -1;
        }

        if (retVal < 0) return retVal;

        // A written numeral anchors the parameter position. The supervised
        // actions then teach other surface forms at that position: if "4" and
        // "four" both accompany SET.has.4, both words acquire means -> 4.
        Thought meansType = MainWindow.theUKS.GetOrAddThought("means", "LinkType");
        foreach (Thought exemplar in exemplars)
        {
            Link concreteAction =
                exemplar.GetTargetOfFirstLinkOfType("demonstrates") as Link;
            Thought modifier = GetNumericActionModifier(concreteAction?.LinkType);
            SequenceView sequence = MainWindow.theUKS.GetSequenceViews(exemplar)
                .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
            if (modifier is null || sequence is null ||
                retVal >= sequence.Elements.Count)
                return -1;

            Thought word = sequence.Elements[retVal];
            Thought existingMeaning = word.GetTargetOfFirstLinkOfType("means");
            if (existingMeaning != modifier)
            {
                word.RemoveLinks(meansType);
                MainWindow.theUKS.AddStatement(word, meansType, modifier);
            }
        }
        return retVal;
    }

    /// <summary>Returns a canonical number already expressed by a written numeral or a learned number word.</summary>
    private static Thought GetObservedNumberMeaning(Thought word)
    {
        Thought retVal = word?.GetTargetOfFirstLinkOfType("means");
        if (retVal?.HasAncestor("number") == true) return retVal;
        if (word is null) return null;

        string surfaceForm = word.Label.StartsWith(
            "w:", StringComparison.OrdinalIgnoreCase)
            ? word.Label[2..]
            : word.Label;
        retVal = GetCanonicalNumberMeaning(surfaceForm);
        return retVal;
    }

    /// <summary>
    /// Returns a common action target when every exemplar uses the same target outside the phrase parameters.
    /// </summary>
    private static Thought GetConstantActionTarget(IReadOnlyCollection<Thought> exemplars)
    {
        List<Thought> targets = exemplars
            .Select(exemplar =>
                exemplar.GetTargetOfFirstLinkOfType("demonstrates") as Link)
            .Where(action => action?.To is not null)
            .Select(action => action.To)
            .Distinct()
            .ToList();
        Thought retVal = targets.Count == 1 ? targets[0] : null;
        return retVal;
    }

    /// <summary>Returns the common filter target demonstrated by every exemplar in a learned template.</summary>
    private static Thought GetConstantActionFilterTarget(IReadOnlyCollection<Thought> exemplars)
    {
        List<Thought> targets = exemplars
            .Select(exemplar => exemplar.GetTargetOfFirstLinkOfType("demonstrates") as Link)
            .Where(action => action is not null)
            .Select(action => action.GetTargetOfFirstLinkOfType("filterBy"))
            .Where(target => target is not null)
            .Distinct()
            .ToList();
        Thought retVal = targets.Count == 1 ? targets[0] : null;
        return retVal;
    }

    /// <summary>
    /// Finds a prefix or suffix insertion relating two observed spellings without assigning grammatical names.
    /// </summary>
    private static AffixDifference FindAffixDifference(
        Thought firstClass,
        Thought firstWord,
        string firstSpelling,
        Thought secondClass,
        Thought secondWord,
        string secondSpelling,
        int minCommonLetters)
    {
        if (string.IsNullOrEmpty(firstSpelling) ||
            string.IsNullOrEmpty(secondSpelling) ||
            firstSpelling.Equals(secondSpelling, StringComparison.OrdinalIgnoreCase))
            return null;

        // Orient the observation from the shorter spelling to the longer one.
        // This describes an insertion without assuming which grammatical form
        // is primary. Equal-length replacements are intentionally deferred.
        Thought sourceClass = firstClass;
        Thought sourceWord = firstWord;
        string sourceSpelling = firstSpelling;
        Thought targetClass = secondClass;
        Thought targetWord = secondWord;
        string targetSpelling = secondSpelling;
        if (sourceSpelling.Length > targetSpelling.Length)
        {
            (sourceClass, targetClass) = (targetClass, sourceClass);
            (sourceWord, targetWord) = (targetWord, sourceWord);
            (sourceSpelling, targetSpelling) = (targetSpelling, sourceSpelling);
        }
        if (sourceSpelling.Length < minCommonLetters)
            return null;

        if (targetSpelling.StartsWith(sourceSpelling, StringComparison.OrdinalIgnoreCase))
        {
            string addedText = targetSpelling[sourceSpelling.Length..];
            AffixDifference retVal = new(
                sourceClass, targetClass, sourceWord, targetWord,
                AffixPosition.End, addedText);
            return retVal;
        }
        if (targetSpelling.EndsWith(sourceSpelling, StringComparison.OrdinalIgnoreCase))
        {
            string addedText = targetSpelling[..^sourceSpelling.Length];
            AffixDifference retVal = new(
                sourceClass, targetClass, sourceWord, targetWord,
                AffixPosition.Beginning, addedText);
            return retVal;
        }
        return null;
    }

    private enum AffixPosition
    {
        Beginning,
        End
    }

    private sealed record AffixDifference(
        Thought SourceClass,
        Thought TargetClass,
        Thought SourceWord,
        Thought TargetWord,
        AffixPosition Position,
        string AddedText);

    private sealed record SpellingPatternKey(
        Thought SourceClass,
        Thought TargetClass,
        AffixPosition Position,
        string AddedText);

    private sealed record WordCorrespondence(Thought Source, Thought Target);

    // 6. Other: Consolidation Orchestration

    /// <summary>
    /// Runs the complete template, action, spelling-pattern, and meaning-normalization learning pass.
    /// </summary>
    public static int ConsolidateLanguageLearning()
    {
        List<Thought> learnedTemplates = ConsolidatePhrasePatterns();
        ConsolidateGrammarAndMeanings();
        int retVal = learnedTemplates.Count;
        return retVal;
    }

    /// <summary>
    /// Consolidates annotated actions, spelling evidence, and the meanings inferred from that evidence.
    /// </summary>
    private static void ConsolidateGrammarAndMeanings()
    {
        LearnActionsFromExemplars();
        DiscoverClassSpellingPatterns();
        NormalizeSpellingPatternMeanings();
    }

    /// <summary>
    /// Consolidates phrase and question patterns without repeating global grammar and spelling analysis.
    /// </summary>
    private static List<Thought> ConsolidatePhrasePatterns()
    {
        List<Thought> learnedTemplates = DiscoverPhraseTemplates(40,1);
        Thought learnedClassRoot = MainWindow.theUKS.Labeled("LearnedClass");
        if (learnedClassRoot is not null)
            MainWindow.theUKS.CoalesceSimilarClasses(learnedClassRoot);
        DiscoverTemplateTokenClasses();
        DiscoverQuestionTemplates();
        List<Thought> retVal = learnedTemplates;
        return retVal;
    }

    /// <summary>Counts one training phrase and queues consolidation when the interval is reached.</summary>
    private void ObserveTrainingPhrase()
    {
        bool consolidate = false;
        lock (_consolidationStateLock)
        {
            _trainingPhrasesAwaitingConsolidation++;
            int interval = Math.Max(1, _trainingPhrasesPerConsolidation);
            if (_trainingPhrasesAwaitingConsolidation < interval) return;

            _trainingPhrasesAwaitingConsolidation = 0;
            consolidate = true;
        }
        if (consolidate) ConsolidatePhrasePatterns();
    }

    /// <summary>Queues consolidation after enough new training phrases have accumulated.</summary>
    private void QueuePendingTrainingConsolidation(bool force = false)
    {
        bool consolidate;
        lock (_consolidationStateLock)
        {
            int interval = Math.Max(1, _trainingPhrasesPerConsolidation);
            if (_trainingPhrasesAwaitingConsolidation == 0) return;
            if (!force && _trainingPhrasesAwaitingConsolidation < interval) return;

            _trainingPhrasesAwaitingConsolidation = 0;
            consolidate = true;
        }
        if (consolidate) ConsolidatePhrasePatterns();
    }

}

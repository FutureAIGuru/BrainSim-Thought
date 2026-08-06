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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UKS;

namespace BrainSimulator.Modules;

public class ModuleText : ModuleBase
{
    private const float PhraseObservationDecayFactor = 0.9999f;
    private const float PhraseObservationIncrease = 1f;
    private const int PlasticPhraseCapacity = 50;

    // Fill this method in with code which will execute
    // once for each cycle of the engine
    public override void Fire()
    {
        Init();

        UpdateDialog();
    }

    // Fill this method in with code which will execute once
    // when the module is added, when "initialize" is selected from the context menu,
    // or when the engine restart button is pressed
    public override void Initialize()
    {
    }

    // called whenever the UKS performs an Initialize()
    public override void UKSInitializedNotification()
    {

    }

    public static string AddPhrase(
        string phrase,
        bool applyExistingTemplates = false,
        bool learnIncrementally = false)
    {
        var theUKS = MainWindow.theUKS;
        char[] trimChars = { '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}' };

        int attempted = 0;
        int ingested = 0;
        try
        {
            List<Thought> wordsInPhrase = new();
            foreach (string token in phrase.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
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
                wordThought ??= theUKS.Labeled("w:" + clean) ??
                    theUKS.GetOrAddThought("w:" + clean, "Word");
                if (wordThought is not null)
                {
                    wordsInPhrase.Add(wordThought);
                    ingested++;
                }
            }

            Thought phraseRoot = theUKS.GetOrAddThought("Phrase");
            Thought hasWords = theUKS.GetOrAddThought("hasWords", "LinkType");
            if (wordsInPhrase.Count > 0)
            {
                Thought thePhrase = FindPhraseWithWords(
                    theUKS, phraseRoot, hasWords, wordsInPhrase);
                bool isNewPhrase = thePhrase is null;
                if (thePhrase is null)
                {
                    thePhrase = theUKS.GetOrAddThought("p*", phraseRoot);
                    theUKS.AddSequenceAndLink(thePhrase, hasWords, wordsInPhrase);
                }
                ObservePhrase(phraseRoot, thePhrase, isNewPhrase);

                Thought theTemplate = null;
                if (applyExistingTemplates)
                {
                    theTemplate = ApplyExistingTemplatesToPhrase(thePhrase);
                    if (theTemplate is not null)
                        ApplyLearnedTemplateAction(theTemplate, thePhrase);
                }

                Thought incrementalTemplate = null;
                if (learnIncrementally)
                    incrementalTemplate = theUKS.IncrementalSequenceBubble(thePhrase);

                PruneStoredPhrases(phraseRoot);

                if (theTemplate is not null)
                {
                    string retVal = $"Template: {theTemplate?.Label ?? "unknown"}: {theTemplate.GetTargetOfFirstLinkOfType("hasWords")}.";
                    return retVal;
                }
                if (incrementalTemplate is not null)
                {
                    string retVal = $"Learned template: {incrementalTemplate.Label}: {incrementalTemplate.GetTargetOfFirstLinkOfType("hasWords")}.";
                    return retVal;
                }
            }
            return $"Processed {attempted} tokens; ingested {ingested} words.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static Thought FindPhraseWithWords(
        UKS.UKS theUKS,
        Thought phraseRoot,
        Thought hasWords,
        List<Thought> words)
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

    private static void ObservePhrase(
        Thought phraseRoot,
        Thought observedPhrase,
        bool isNewPhrase)
    {
        foreach (Thought storedPhrase in phraseRoot.Children)
        {
            if (!storedPhrase.isPlastic || HasMeaning(storedPhrase)) continue;
            storedPhrase.Weight *= PhraseObservationDecayFactor;
        }

        observedPhrase.isPlastic = true;
        if (isNewPhrase)
            observedPhrase.Weight = PhraseObservationIncrease;
        else
            observedPhrase.Weight += PhraseObservationIncrease;
        observedPhrase.Fire();
    }

    private static void PruneStoredPhrases(Thought phraseRoot)
    {
        List<Thought> deletablePhrases = phraseRoot.Children
            .Where(phrase => phrase.isPlastic && !HasMeaning(phrase))
            .OrderBy(phrase => phrase.Weight)
            .ThenBy(phrase => phrase.LastFiredTime)
            .ToList();

        int deleteCount = deletablePhrases.Count - PlasticPhraseCapacity;
        for (int index = 0; index < deleteCount; index++)
            deletablePhrases[index].Delete();
    }

    private static bool HasMeaning(Thought phrase)
    {
        bool retVal = phrase.LinksTo.Any(link => link.LinkType?.Label == "means") ||
            phrase.LinksFrom.Any(link => link.LinkType?.Label == "means");
        return retVal;
    }

    public static string AddText(
        string text,
        bool applyExistingTemplates = true,
        bool learnIncrementally = false)
    {
        var theUKS = MainWindow.theUKS;
        Thought wordRoot = theUKS.GetOrAddThought("Word", "LanguageElement");
        wordRoot.RemoveParent("Thought");
        wordRoot.RemoveParent("Object");
        if (string.IsNullOrWhiteSpace(text)) return "Null input";

        string[] sentences = Regex.Split(text, @"(?<=[\.!\?])\s+");
        foreach (string sentence in sentences)
        {
            string trimmed = sentence.Trim();
            if (trimmed.Length == 0) continue;

            string retVal = AddPhrase(trimmed, applyExistingTemplates, learnIncrementally);
            if (sentences.Length == 1)
                return retVal;  
        }
        return "OK";
    }

    /// <summary>
    /// Records a supervised example which pairs an observed phrase with the
    /// concrete SET relationship it should produce. The action text uses the
    /// ordinary algorithm notation, for example [dog-&gt;SET.can-&gt;bark].
    /// </summary>
    public static Thought AddActionExemplar(string phrase, string actionText)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            throw new ArgumentException("An action exemplar requires a phrase.", nameof(phrase));
        if (string.IsNullOrWhiteSpace(actionText))
            throw new ArgumentException("An action exemplar requires an action.", nameof(actionText));

        Match actionParts = Regex.Match(actionText.Trim(),
            @"^\[\s*(.*?)\s*->\s*(.*?)\s*->\s*(.*?)\s*\]$");
        if (!actionParts.Success)
            throw new FormatException(
                $"Action exemplar '{actionText}' must use [source->SET.type->target].");

        string sourceLabel = actionParts.Groups[1].Value.Trim().ToLowerInvariant();
        string setTypeLabel = actionParts.Groups[2].Value.Trim();
        string targetLabel = actionParts.Groups[3].Value.Trim().ToLowerInvariant();
        if (sourceLabel.Length == 0 || targetLabel.Length == 0 ||
            !setTypeLabel.StartsWith("SET.", StringComparison.OrdinalIgnoreCase))
            throw new FormatException(
                $"Action exemplar '{actionText}' must use [source->SET.type->target].");

        var theUKS = MainWindow.theUKS;
        Thought exemplarRoot = theUKS.GetOrAddThought("ActionExemplar", "LanguageElement");
        Thought exemplar = theUKS.GetOrAddThought("actionExemplar*", exemplarRoot);
        List<Thought> words = GetPhraseWords(phrase);
        if (words.Count < 2)
            throw new FormatException("An action exemplar phrase requires at least two words.");
        theUKS.AddSequenceAndLink(exemplar, "hasWords", words);

        // Creating SET first ensures dotted action types inherit from LinkType
        // through SET. GetOrAddThought then represents SET.can as both a child
        // of SET and [SET.can -> is -> can].
        theUKS.GetOrAddThought("SET", "LinkType");
        theUKS.GetOrAddThought(setTypeLabel[4..], "LinkType");
        Thought setType = theUKS.GetOrAddThought(setTypeLabel, "LinkType");
        Thought source = theUKS.GetOrAddThought(sourceLabel);
        Thought target = theUKS.GetOrAddThought(targetLabel);
        Link action = new(source, setType, target);
        theUKS.AddStatement(exemplar,
            theUKS.GetOrAddThought("demonstrates", "LinkType"), action);
        //TODO:  do this after figuring out what the template is.
        theUKS.ApplySetAction(action);
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

        Thought templateRoot = theUKS.GetOrAddThought("LearnedTemplate", "LanguageElement");
        Thought classRoot = theUKS.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought meansType = theUKS.GetOrAddThought("means", "LinkType");
        Thought evidenceType = theUKS.GetOrAddThought("evidence", "LinkType");
        int learnedCount = 0;

        var actionGroups = exemplarRoot.Children
            .Select(exemplar => (
                exemplar,
                action: exemplar.GetTargetOfFirstLinkOfType("demonstrates") as Link))
            .Where(item => item.action?.LinkType?.HasAncestor("SET") == true)
            .Select(item =>
            {
                Thought modifier = GetNumericActionModifier(item.action.LinkType);
                Thought actionType = modifier is null
                    ? item.action.LinkType
                    : GetNumericActionBaseType(item.action.LinkType);
                var retVal = (item.exemplar, item.action, actionType, modifier);
                return retVal;
            })
            .GroupBy(item => (item.actionType, hasModifier: item.modifier is not null));

        foreach (var actionGroup in actionGroups)
        {
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

                int sourcePosition = relationshipParameterPositions[0];
                int targetPosition = relationshipParameterPositions.Count > 1
                    ? relationshipParameterPositions[^1]
                    : -1;
                Thought sourceParameter = templateSequence.Elements[sourcePosition];
                Thought targetParameter = targetPosition >= 0
                    ? templateSequence.Elements[targetPosition]
                    : GetConstantActionTarget(templateExemplars);
                if (targetParameter is null) continue;
                Thought modifierParameter = modifierPosition >= 0
                    ? templateSequence.Elements[modifierPosition]
                    : null;
                Link parameterizedAction = template.LinksTo
                    .Where(link => link.LinkType == meansType)
                    .Select(link => link.To)
                    .OfType<Link>()
                    .FirstOrDefault(action =>
                        action.From == sourceParameter &&
                        action.LinkType == actionGroup.Key.actionType &&
                        action.To == targetParameter &&
                        action.GetTargetOfFirstLinkOfType("linkTypeParameter") ==
                            modifierParameter);
                parameterizedAction ??= new Link(
                    sourceParameter, actionGroup.Key.actionType, targetParameter);
                theUKS.AddStatement(template, meansType, parameterizedAction);
                if (modifierParameter is not null)
                {
                    theUKS.AddStatement(parameterizedAction,
                        theUKS.GetOrAddThought("linkTypeParameter", "LinkType"),
                        modifierParameter);
                }

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
                    // ModuleTextIn: w:X means X. Different exemplars may later
                    // teach multiple meanings without changing this structure.
                    theUKS.AddStatement(
                        exemplarSequence.Elements[sourcePosition], meansType, concreteAction.From);
                    if (targetPosition >= 0)
                    {
                        theUKS.AddStatement(
                            exemplarSequence.Elements[targetPosition], meansType, concreteAction.To);
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
        return learnedCount;
    }

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

    private static int FindNumericActionModifierPosition(
        IReadOnlyCollection<Thought> exemplars)
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

    private static Thought GetConstantActionTarget(
        IReadOnlyCollection<Thought> exemplars)
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

    /// <summary>
    /// Instantiates and applies the SET action learned for a matched template.
    /// </summary>
    /// <returns>The asserted ordinary relationship, or null if the template has no action.</returns>
    public static Link ApplyLearnedTemplateAction(Thought template, Thought phrase)
    {
        if (template is null || phrase is null) return null;

        var theUKS = MainWindow.theUKS;
        Link parameterizedAction = template.LinksTo
            .Where(link => link.LinkType?.Label == "means")
            .Select(link => link.To)
            .OfType<Link>()
            .FirstOrDefault(action => action.LinkType?.HasAncestor("SET") == true);
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
            actionType = theUKS.GetOrAddThought(
                parameterizedAction.LinkType.Label + "." + modifier.Label,
                "LinkType");
        }

        Link action = new(source, actionType, target);
        Link retVal = theUKS.ApplySetAction(action);
        return retVal;
    }

    /// <summary>
    /// Applies already learned templates to one manually entered phrase. An
    /// unclassified word may occupy a class wildcard; a successful match then
    /// adds that word to the wildcard's learned class.
    /// </summary>
    /// <returns>The selected learned template, or null when none matches.</returns>
    public static Thought ApplyExistingTemplatesToPhrase(Thought phrase)
    {
        var theUKS = MainWindow.theUKS;
        Thought templateRoot = theUKS.Labeled("LearnedTemplate");
        Thought learnedClassRoot = theUKS.Labeled("LearnedClass");
        Thought searchOptions = theUKS.Labeled("TemplateLearningSearch");
        if (phrase is null || templateRoot is null || learnedClassRoot is null || searchOptions is null)
            return null;

        SequenceView phraseSequence = theUKS.GetSequenceViews(phrase)
            .FirstOrDefault(view => view.LinkType?.Label == "hasWords");
        if (phraseSequence is null) return null;

        Dictionary<Thought, float> matchingTemplates = new();
        foreach (var match in theUKS.FindSequencesByActivation(
            phraseSequence.Elements.ToList(), searchOptions))
        {
            foreach (Link ownerLink in match.seqNode.FRST.LinksFrom.Where(link =>
                link.LinkType?.Label == "hasWords" && link.From is not null &&
                templateRoot.Children.Contains(link.From)))
            {
                if (!matchingTemplates.TryGetValue(ownerLink.From, out float previousConfidence) ||
                    match.confidence > previousConfidence)
                    matchingTemplates[ownerLink.From] = match.confidence;
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
                        item.element.Parents.Any(parent => parent != theUKS.Labeled("Wildcard") &&
                            phraseSequence.Elements[item.position].HasAncestor(parent)));
                int fixedElements = sequence?.Elements.Count(element =>
                    !element.HasAncestor("Wildcard")) ?? 0;
                int evidence = candidate.Key.LinksTo.Count(link => link.LinkType?.Label == "evidence");
                bool hasAction = candidate.Key.LinksTo.Any(link =>
                    link.LinkType?.Label == "means" && link.To is Link action &&
                    action.LinkType?.HasAncestor("SET") == true);
                var retVal = (template: candidate.Key, candidate.Value, classifiedMatches,
                    fixedElements, evidence, hasAction);
                return retVal;
            })
            .OrderByDescending(candidate => candidate.hasAction)
            .ThenByDescending(candidate => candidate.classifiedMatches)
            .ThenByDescending(candidate => candidate.fixedElements)
            .ThenByDescending(candidate => candidate.Value)
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
    /// Incrementally loads up to <paramref name="phrasesPerCall"/> phrases from <paramref name="filePath"/>.
    /// Each line is passed through AddText and the incremental sequence bubbler.
    /// Tab-delimited grounding/action text is ignored for now.
    /// Returns phrases ingested this call.
    /// When it returns 0, the file is finished or unreadable.
    /// </summary>
    /// 
    // Incremental file-load state
    private StreamReader _phraseReader;
    private string _phraseReaderPath;
    public int LoadTextFromFile(string filePath, int phrasesPerCall = 20)
    {
        if (phrasesPerCall <= 0) phrasesPerCall = 1;
        if (!File.Exists(filePath))
        {
            ResetPhraseReader();
            return 0;
        }

        try
        {
            // (Re)open reader if this is a new file or we haven't started yet
            if (_phraseReader == null || !string.Equals(_phraseReaderPath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                ResetPhraseReader();
                _phraseReader = new StreamReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read));
                _phraseReaderPath = filePath;
            }

            int count = 0;
            while (count < phrasesPerCall && _phraseReader != null)
            {
                string line = _phraseReader.ReadLine();
                if (line == null) break; // EOF

                string phrase = line.Trim();
                if (phrase.Length == 0) continue;

                //TEMPORARY ignore questions
                if (phrase.ToLower().Contains("what")) continue;


                int tabIdx = phrase.IndexOf('\t');
                if (tabIdx >= 0)
                {
                    phrase = phrase[..tabIdx].Trim();
                }

                if (phrase.Length == 0) continue;

                // Split into sentences so we cap by phrases, not by lines
                string[] sentences = Regex.Split(phrase, @"(?<=[\.!\?])\s+");
                foreach (string sentence in sentences)
                {
                    if (count >= phrasesPerCall) break;

                    string trimmed = sentence.Trim();
                    if (trimmed.Length == 0) continue;

                    //string result = AddPhrase(trimmed);
                    string result = AddText(trimmed,
                        applyExistingTemplates: false,
                        learnIncrementally: true);
                    if (result.StartsWith("Error:", StringComparison.Ordinal))
                        throw new InvalidOperationException(result);
                    count++;
                }
            }

            if (_phraseReader != null && _phraseReader.EndOfStream)
            {
                ResetPhraseReader();
            }

            return count;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading phrases from file: {ex.Message}");
            ResetPhraseReader();
            return 0;
        }
    }

    /// <summary>
    /// Cancels any in-progress incremental load.
    /// </summary>
    public void CancelIncrementalLoad()
    {
        ResetPhraseReader();
    }

    private void ResetPhraseReader()
    {
        _phraseReader?.Dispose();
        _phraseReader = null;
        _phraseReaderPath = null;
    }

    private static Thought GetWildcardClass(Thought value, Thought learnedClassRoot)
    {
        if (value is null || learnedClassRoot is null || !value.HasAncestor("Wildcard")) return null;
        Thought retVal = value.Parents.FirstOrDefault(learnedClassRoot.Children.Contains);
        return retVal;
    }

    private static List<Thought> GetPhraseWords(string phrase)
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

    private static Thought GetOrCreateMeaning(Thought word)
    {
        var theUKS = MainWindow.theUKS;
        Thought meansType = theUKS.GetOrAddThought("means", "LinkType");
        string label = word.Label.StartsWith("w:", StringComparison.OrdinalIgnoreCase)
            ? word.Label[2..]
            : word.Label;
        Thought numericMeaning = GetCanonicalNumberMeaning(label);
        Thought meaning = word.GetTargetOfFirstLinkOfType("means");
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

    private static List<(Thought sourceClass, Thought targetClass)>
        GetSpellingPatternClassPairs(Thought pattern)
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

    /// <summary>
    /// Discovers learned templates from every Phrase hasWords sequence currently
    /// in the UKS. Phrase owners become evidence; wildcard fillers become
    /// ordinary learned classes.
    /// </summary>
    public static List<Thought> DiscoverPhraseTemplates(
        int minMembers = 30,
        int minFixedElements = 2)
    {
        var theUKS = MainWindow.theUKS;
        Thought phraseRoot = theUKS.Labeled("Phrase");
        if (phraseRoot is null) return new List<Thought>();

        Thought templateRoot = theUKS.GetOrAddThought("LearnedTemplate", "LanguageElement");
        Thought fillerClassRoot = theUKS.GetOrAddThought("LearnedClass", "LanguageElement");
        List<SequenceView> phraseObservations = theUKS.GetSequenceViews(phraseRoot.Children)
            .Where(view => view.LinkType?.Label == "hasWords")
            .ToList();
        return theUKS.DiscoverSequenceTemplates(
            phraseObservations,
            templateRoot,
            fillerClassRoot,
            minMembers,
            minFixedElements);
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
        Thought templateRoot = theUKS.Labeled("LearnedTemplate");
        if (templateRoot is null) return new List<Thought>();

        Thought classRoot = theUKS.GetOrAddThought("LearnedClass", "LanguageElement");
        Thought evidenceType = theUKS.GetOrAddThought("evidence", "LinkType");
        HashSet<Thought> boundaryMembers = new();
        HashSet<Thought> connectorMembers = new();
        HashSet<Thought> boundaryEvidence = new();
        HashSet<Thought> connectorEvidence = new();

        foreach (Thought template in templateRoot.Children)
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
        var theUKS = MainWindow.theUKS;
        ConsolidateSpellingPatterns();
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

    private static bool IsIdentityMeaning(Thought word, Thought meaning)
    {
        string wordLabel = word.Label.StartsWith("w:", StringComparison.OrdinalIgnoreCase)
            ? word.Label[2..]
            : word.Label;
        bool retVal = meaning.Label.Equals(
            wordLabel, StringComparison.OrdinalIgnoreCase);
        return retVal;
    }

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


    public static int ProcessTheExistingText()
    {
        List<Thought> learnedTemplates = DiscoverPhraseTemplates(40,1);
        Thought learnedClassRoot = MainWindow.theUKS.Labeled("LearnedClass");
        if (learnedClassRoot is not null)
            MainWindow.theUKS.CoalesceSimilarClasses(learnedClassRoot);
        DiscoverTemplateTokenClasses();
        LearnActionsFromExemplars();
        DiscoverClassSpellingPatterns();
        NormalizeMeaningsFromSpellingPatterns();
        return learnedTemplates.Count;
    }

}

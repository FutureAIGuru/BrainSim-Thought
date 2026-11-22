#nullable enable

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Pluralize.NET;
using UKS;

namespace BrainSimulator.Modules;

public sealed class ModuleInterpret : ModuleBase
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
    private Thought? noun;
    private Thought? fills;
    private Thought? plural;
    private Thought? setRelationship;
    private Thought? precedesWord;
    private Thought? precedesWordStartingWith;
    private Thought? followsWord;
    private Thought? closedClass;
    private Thought? isA;
    private Thought? spelled;

    public override void Fire() => Init();

    public override void Initialize() => LoadLanguageKnowledge();

    public override void UKSInitializedNotification() => LoadLanguageKnowledge();

    private void LoadLanguageKnowledge()
    {
        GetUKS();
        string vocabularyFile = Path.Combine(AppContext.BaseDirectory, "UKSContent", "BasicWords.txt");
        theUKS.ImportTextFile(vocabularyFile);
        // theUKS.ImportTextFile(ContentPath("InterpretLanguage.txt"));

        hasWords = theUKS.GetOrAddThought("hasWords", "LinkType");
        means = theUKS.GetOrAddThought("means", "LinkType");
        templateRoot = theUKS.GetOrAddThought("template", "LanguageElement");
        phraseRoot = theUKS.GetOrAddThought("phrase", "LanguageElement");
        // One selectable class for both fixed phrases and wildcard templates, separate from words.
        patternRoot = theUKS.GetOrAddThought("languagePattern", "LanguageElement");
        templateRoot.AddParent(patternRoot);
        phraseRoot.AddParent(patternRoot);
        vlu = theUKS.Labeled("VLU");
        templateSequenceSearch = theUKS.GetOrAddThought("InterpretTemplateSearch", theUKS.Labeled("TemplateSequenceSearch"));
        // A loaded network may predate this option. Initialization must create it, not just look it up.
        templateSequenceSearch.AddLink("hasProperty", theUKS.GetOrAddThought("allowTrailingInput", "SearchOption"));
        matchesTemplate = theUKS.GetOrAddThought("matchesTemplate", "LinkType");
        isWildcard = theUKS.Labeled("isWildcard");
        wordType = theUKS.Labeled("wordType");
        unknownWordType = theUKS.Labeled("unknownWordType");
        wordRoot = theUKS.Labeled("word");
        noun = theUKS.Labeled("noun");
        // A sentence word fills a template element: the binding a MEANS role is resolved through.
        // GetMeanings records it while confirming that a template covers its run of words.
        fills = theUKS.GetOrAddThought("fills", "LinkType");
        isA = theUKS.Labeled("is-a");
        spelled = theUKS.Labeled("spelled");
        plural = theUKS.Labeled("plural");
        setRelationship = theUKS.Labeled("setRelationship");
        precedesWord = theUKS.Labeled("precedesWord");
        precedesWordStartingWith = theUKS.Labeled("precedesWordStartingWith");
        followsWord = theUKS.Labeled("followsWord");
        closedClass = theUKS.Labeled("closedClass");

        // Repair older vocabulary entries with a redundant direct word parent.
        foreach (Thought word in wordRoot!.Children.Where(word => word.GetTargetOfFirstLinkOfType(spelled) is not null).ToList())
        {
            if (word == wordType) continue;
            if (!word.HasAncestor(wordType)) word.AddParent(unknownWordType!);
            word.RemoveParent(wordRoot);
        }

        // Move the previous experimental grouping when a saved network is reused.
        foreach (string name in new[] { "nounPhrase", "verbPhrase", "sentenceTemplate" })
            theUKS.Labeled(name)?.RemoveParent("phrase");

        // Older networks gave some MEANS operands as particular template sequence elements.
        // Templates now name those elements by role, so the element references are obsolete.
        foreach (Thought template in patternRoot.Descendants.ToList())
            foreach (Link definition in template.LinksTo.Where(link => link.LinkType == means && link.To is Link pattern &&
                (pattern.From is SeqElement || pattern.LinkType is SeqElement || pattern.To is SeqElement)).ToList())
                template.RemoveLink(definition);

        // Phrase patterns, followed by a sentence composed of those phrases.
        // A MEANS operand refers to the template element whose value is the same Thought. Where a
        // template uses one class twice, a role tells the elements apart: ??.nounPhrase.subjectRole
        // inherits ??, its class nounPhrase, and subjectRole. Roles are wildcards, so only the
        // class constrains which words or phrases can fill the element.
        foreach (string definition in new[]
        {
            "[!->is-a->LinkType]",
            "[!->hasProperty->setRelationship]",
            "[templateRole->hasProperty->isWildcard]",
            "[subjectRole->is-a->templateRole]",
            "[complementRole->is-a->templateRole]",
            "[nounPhrase->is-a->template]",
            "[verbPhrase->is-a->template]",
            "[nounPhrase1->is-a->nounPhrase]",
            "[nounPhrase1->hasWords->^??.article ??.adjective ??.noun]",
            "[nounPhrase1->means->??.noun]",
            "[nounPhrase1->means->[??.noun->is.!->??.adjective]]",
            "[nounPhrase2->is-a->nounPhrase]",
            "[nounPhrase2->hasWords->^??.article ??.noun]",
            "[nounPhrase2->means->??.noun]",
            "[nounPhrase3->is-a->nounPhrase]",
            "[nounPhrase3->hasWords->^??.noun]",
            "[nounPhrase3->means->??.noun]",
            "[verbPhrase1->is-a->verbPhrase]",
            "[verbPhrase1->hasWords->^??.auxiliary ??.ongoingVerb]",
            "[verbPhrase1->means->??.ongoingVerb]",
            "[sentenceTemplate->is-a->template]",
            "[sentenceTemplate->hasWords->^??.nounPhrase.subjectRole ??.verbPhrase ??.nounPhrase.complementRole]",
            "[sentenceTemplate->means->[??.nounPhrase.subjectRole->??.verbPhrase->??.nounPhrase.complementRole]]",
            "[classificationTemplate->is-a->template]",
            "[classificationTemplate->hasWords->^??.nounPhrase.subjectRole ??.classificationPhrase ??.nounPhrase.complementRole]",
            "[classificationTemplate->means->[??.nounPhrase.subjectRole->is-a.!->??.nounPhrase.complementRole]]",
            "[copulaClassificationTemplate->is-a->template]",
            "[copulaClassificationTemplate->hasWords->^??.nounPhrase.subjectRole ??.copula ??.nounPhrase.complementRole]",
            "[copulaClassificationTemplate->means->[??.nounPhrase.subjectRole->is-a.!->??.nounPhrase.complementRole]]",
            // "Rover is a small dog." / "Dogs are small animals." The adjective describes the subject,
            // so it belongs to the sentence, not to the complement's noun phrase (dogs are not all small).
            "[classificationAttributeTemplate->is-a->template]",
            "[classificationAttributeTemplate->hasWords->^??.nounPhrase.subjectRole ??.classificationPhrase ??.adjective ??.nounPhrase.complementRole]",
            "[classificationAttributeTemplate->means->[??.nounPhrase.subjectRole->is-a.!->??.nounPhrase.complementRole]]",
            "[classificationAttributeTemplate->means->[??.nounPhrase.subjectRole->is.!->??.adjective]]",
            "[copulaClassificationAttributeTemplate->is-a->template]",
            "[copulaClassificationAttributeTemplate->hasWords->^??.nounPhrase.subjectRole ??.copula ??.adjective ??.nounPhrase.complementRole]",
            "[copulaClassificationAttributeTemplate->means->[??.nounPhrase.subjectRole->is-a.!->??.nounPhrase.complementRole]]",
            "[copulaClassificationAttributeTemplate->means->[??.nounPhrase.subjectRole->is.!->??.adjective]]",
            "[copulaAttributeTemplate->is-a->template]",
            "[copulaAttributeTemplate->hasWords->^??.nounPhrase ??.copula ??.adjective]",
            "[copulaAttributeTemplate->means->[??.nounPhrase->is.!->??.adjective]]",
            // These verbs supply their own executable relationship type through MEANS.
            "[possessionTemplate->is-a->template]",
            "[possessionTemplate->hasWords->^??.nounPhrase.subjectRole ??.possessionVerb ??.nounPhrase.complementRole]",
            "[possessionTemplate->means->[??.nounPhrase.subjectRole->??.possessionVerb->??.nounPhrase.complementRole]]",
            "[actionTemplate->is-a->template]",
            "[actionTemplate->hasWords->^??.nounPhrase.subjectRole ??.actionVerb ??.nounPhrase.complementRole]",
            "[actionTemplate->means->[??.nounPhrase.subjectRole->??.actionVerb->??.nounPhrase.complementRole]]",
            "[negativeAttributeTemplate->is-a->template]",
            "[negativeAttributeTemplate->hasWords->^??.nounPhrase ??.copula ??.negationWord ??.adjective]",
            "[negativeAttributeTemplate->means->[??.nounPhrase->is.not.!->??.adjective]]",
            "[negativeClassificationTemplate->is-a->template]",
            "[negativeClassificationTemplate->hasWords->^??.nounPhrase.subjectRole ??.copula ??.negationWord ??.nounPhrase.complementRole]",
            "[negativeClassificationTemplate->means->[??.nounPhrase.subjectRole->is-a.not.!->??.nounPhrase.complementRole]]",
            "[attributeQueryTemplate->is-a->template]",
            "[attributeQueryTemplate->hasWords->^??.copula ??.nounPhrase ??.adjective]",
            "[attributeQueryTemplate->means->[??.nounPhrase->is.?->??.adjective]]",
            "[classificationQueryTemplate->is-a->template]",
            "[classificationQueryTemplate->hasWords->^??.copula ??.nounPhrase.subjectRole ??.nounPhrase.complementRole]",
            "[classificationQueryTemplate->means->[??.nounPhrase.subjectRole->is-a.?->??.nounPhrase.complementRole]]",
            // Handles adjacent bare nouns without merging their phrases.
            "[bareClassificationQueryTemplate->is-a->template]",
            "[bareClassificationQueryTemplate->hasWords->^??.copula ??.noun.subjectRole ??.noun.complementRole]",
            "[bareClassificationQueryTemplate->means->[??.noun.subjectRole->is-a.?->??.noun.complementRole]]",
            "[wildcardAttributeQueryTemplate->is-a->template]",
            "[wildcardAttributeQueryTemplate->hasWords->^w:what ??.copula ??.adjective]",
            "[wildcardAttributeQueryTemplate->means->[??->is.?->??.adjective]]",
            // "What is Fido?" A bare ?? operand is the open query variable, not a template element.
            "[wildcardClassificationQueryTemplate->is-a->template]",
            "[wildcardClassificationQueryTemplate->hasWords->^w:what ??.copula ??.nounPhrase]",
            "[wildcardClassificationQueryTemplate->means->[??.nounPhrase->is-a.?->??]]",
            // "What is a dog?" The fixed phrase "is a" is recognized before the copula can be.
            "[wildcardClassificationPhraseQueryTemplate->is-a->template]",
            "[wildcardClassificationPhraseQueryTemplate->hasWords->^w:what ??.classificationPhrase ??.nounPhrase]",
            "[wildcardClassificationPhraseQueryTemplate->means->[??.nounPhrase->is-a.?->??]]",
            // "What does Fido have?" / "Do dogs have tails?"
            "[wildcardPossessionQueryTemplate->is-a->template]",
            "[wildcardPossessionQueryTemplate->hasWords->^w:what ??.doAuxiliary ??.nounPhrase ??.possessionVerb]",
            "[wildcardPossessionQueryTemplate->means->[??.nounPhrase->has.?->??]]",
            "[possessionQueryTemplate->is-a->template]",
            "[possessionQueryTemplate->hasWords->^??.doAuxiliary ??.nounPhrase.subjectRole ??.possessionVerb ??.nounPhrase.complementRole]",
            "[possessionQueryTemplate->means->[??.nounPhrase.subjectRole->has.?->??.nounPhrase.complementRole]]",
            // "Is Fido not black?" / "Are dogs not cats?" match only an explicit negative relationship.
            "[negativeAttributeQueryTemplate->is-a->template]",
            "[negativeAttributeQueryTemplate->hasWords->^??.copula ??.nounPhrase ??.negationWord ??.adjective]",
            "[negativeAttributeQueryTemplate->means->[??.nounPhrase->is.not.?->??.adjective]]",
            "[negativeClassificationQueryTemplate->is-a->template]",
            "[negativeClassificationQueryTemplate->hasWords->^??.copula ??.nounPhrase.subjectRole ??.negationWord ??.nounPhrase.complementRole]",
            "[negativeClassificationQueryTemplate->means->[??.nounPhrase.subjectRole->is-a.not.?->??.nounPhrase.complementRole]]"
        })
            Define(definition);

        // Initially teach the ten digit quantities explicitly. Each is ordinary UKS knowledge.
        // This keeps quantity composition out of the interpretation code while we review it.
        foreach (char digit in "0123456789")
        {
            string name = "quantityPossession" + digit;
            Define($"[{name}->is-a->template]");
            Define($"[{name}->hasWords->^??.nounPhrase.subjectRole ??.possessionVerb w:{digit} ??.nounPhrase.complementRole]");
            Define($"[{name}->means->[??.nounPhrase.subjectRole->has.{digit}.!->??.nounPhrase.complementRole]]");
        }
    }

    /// <summary>
    /// Adds one template definition. A saved network keeps the word sequence an earlier version
    /// defined, and adding hasWords again would give the template a second sequence, so a word
    /// sequence definition replaces the template's existing ones.
    /// </summary>
    private void Define(string definition)
    {
        string[] parts = definition.TrimStart('[').Split("->");
        if (parts.Length > 2 && parts[1] == "hasWords" && theUKS.Labeled(parts[0]) is Thought template)
            template.RemoveLinks(hasWords!);
        theUKS.ProcessSingleLine(definition);
    }

    #endregion

    /// <summary>
    /// The reply to the last Input: the English answer to a query ("I don't know." when nothing
    /// matched), "OK." when a statement was executed, or why the sentence could not be used.
    /// </summary>
    public string Answer { get; private set; } = "";

    /// <returns>A diagnostic report of each pipeline stage. The reply itself is in Answer.</returns>
    public string Input(string sentence, bool rememberUnknownWords = false)
    {
        try
        {
            Thought? sentenceThought = GetSequenceFromText(sentence, rememberUnknownWords);
            if (sentenceThought is null) return Answer = "Enter a sentence.";

            List<Thought> templates = GetTemplates(sentenceThought);
            List<Thought> meanings = GetMeanings(sentenceThought);
            List<Link> executed = Execute(meanings);
            string words = "Sentence input: "+ sentenceThought.GetTargetOfFirstLinkOfType("hasWords");
            string templateText = templates.Count == 0
                ? "No matching templates."
                : "Templates\n" + string.Join("\n", templates.Select(
                    template => template.Label + "    " + template.GetTargetOfFirstLinkOfType("hasWords").ToString()));
            SeqElement sequence = (SeqElement)sentenceThought.GetTargetOfFirstLinkOfType("hasWords");
            string attachments = string.Join("\n", theUKS.EnumerateSequenceElements(sequence).Select(element =>
                element.VLU.Label + "    " + string.Join(", ", element.LinksTo
                    .Where(link => link.LinkType == matchesTemplate).Select(link => link.To.Label))));
            string meaningText = string.Join("\n", meanings.Select(meaning => meaning.ToString()));
            string output = words + "\n\n" + templateText + "\n\nWord attachments\n" + attachments +
                "\n\nMeanings\n" + meaningText;
            bool isQuery = meanings.OfType<Link>().Any(action => action.LinkType?.HasProperty("isPossible") == true);
            if (executed.Count > 0 && !isQuery)
                output += "\n\nExecuted relationships\n" + string.Join("\n", executed);
            if (isQuery)
            {
                output += "\n\nQuery results\n" + (executed.Count == 0
                    ? "No matching relationships." : string.Join("\n", executed));
                // No match is not proof of falsity, so an empty result is not answered "no".
                Answer = executed.Count == 0 ? "I don't know." : string.Join("\n",
                    executed.Select(result => GenerateSentence(result) ?? result.ToString()).Distinct());
                output += "\n\nAnswer\n" + Answer;
            }
            else Answer = executed.Count > 0 ? "OK." : "I don't understand.";
            return output;
        }
        catch (FormatException error)
        {
            return Answer = error.Message;
        }
    }

    #region GetSequenceFromText

    // Each successful input is retained under a new SentenceIn Thought for inspection.
    public Thought? GetSequenceFromText(string sentence, bool rememberUnknownWords = false)
    {
        var pluralizer = new Pluralizer();
        var words = (sentence ?? "").Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim('.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}'))
            .Where(token => token.Length > 0)
            .Select(token => token.ToLowerInvariant());
        var wordThoughts = new List<Thought>();
        var singularizedPositions = new List<int>();
        var unknown = new List<string>();

        foreach (string spelling in words)
        {
            Thought? word = ModuleWord.FindWordsBySpelling(theUKS, spelling).FirstOrDefault();
            string rememberedSpelling = spelling;
            bool singularized = false;
            if (word is null || !word.Parents.Any(parent => parent != wordType &&
                parent != unknownWordType && parent.HasAncestor(wordType)))
            { // Try singularizing the word and looking again.
                string singular = pluralizer.Singularize(spelling);
                if (singular != spelling)
                {
                    word = ModuleWord.FindWordsBySpelling(theUKS, singular)
                        .FirstOrDefault(candidate => candidate.HasAncestor(noun) ||
                            !candidate.Parents.Any(parent => parent != wordType &&
                                parent != unknownWordType && parent.HasAncestor(wordType)));
                    rememberedSpelling = singular;
                    singularized = true;
                }
            }
            // Remember the spelling now. Grammatical classification is a separate action.
            if (word is null && rememberUnknownWords) word = ModuleWord.RememberSpelling(theUKS, rememberedSpelling, out _);

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
                    word.AddLink(means!, theUKS.GetOrAddThought(rememberedSpelling));
                if (singularized) singularizedPositions.Add(wordThoughts.Count);
                wordThoughts.Add(word);
            }
        }

        if (unknown.Count > 0) throw new FormatException($"Unknown words: {string.Join(", ", unknown.Distinct())}.");
        if (wordThoughts.Count == 0) return null;

        theUKS.GetOrAddThought("Sentence", "LanguageElement");
        Thought sentenceThought = theUKS.GetOrAddThought("SentenceIn*", "Sentence");
        SeqElement sequence = theUKS.AddSequenceAndLink(sentenceThought, hasWords, wordThoughts, allowCompression: false);
        SeqElement element = sequence;
        for (int position = 0; element is not null; position++, element = element.NXT)
            if (singularizedPositions.Contains(position))
                element.AddLink("hasProperty", theUKS.GetOrAddThought("singularized", "property"));

        return sentenceThought;
    }

    #endregion

    /// <summary>
    /// Search from the sentence itself. Words retain their original VLU links; recognized
    /// phrases add a languagePattern VLU facet covering the same sentence elements.
    /// Search can therefore use the word or its current recognized phrase without a copied input.
    /// </summary>
    public List<Thought> GetTemplates(Thought sentenceThought)
    {
        var templates = new List<Thought>(); // Diagnostic summary only, assembled from the UKS below.
        if (patternRoot is null || vlu is null || matchesTemplate is null || templateSequenceSearch is null)
            return templates;
        if (sentenceThought.GetTargetOfFirstLinkOfType(hasWords) is not SeqElement first) return templates;

        bool added;
        bool allowLoneUnknownWords = false;
        do
        {
            added = false;
            bool deferred = false;
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
                        if (template.HasAncestor(patternRoot) && !matches.Contains(template) && AdmitsItsNewWords(template, start))
                        {
                            matches.Add(template);
                            longest = length;
                        }
                }
                // A single unknown word on its own is only a guess. If words precede it, a template
                // starting earlier may still cover it, so wait until nothing else can be recognized.
                if (matches.Count > 0 && !allowLoneUnknownWords && !ReferenceEquals(start, first) &&
                    IsLoneUnknownWord(start, matches))
                {
                    deferred = true;
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
            // Once nothing else can be recognized, a waiting lone unknown word may be guessed.
            if (added) allowLoneUnknownWords = false;
            else if (deferred && !allowLoneUnknownWords) allowLoneUnknownWords = added = true;
        } while (added);
        for (SeqElement? element = first; element is not null; element = element.NXT)
            foreach (Link link in element.LinksTo)
                if (link.LinkType == matchesTemplate && !templates.Contains(link.To)) templates.Add(link.To);
        return templates;
    }

    /// <summary>
    /// A template may be matched only if no unknown word would be learned into a closed class
    /// (articles, auxiliaries, ...): those are fixed sets of function words. Walks the template and
    /// the sentence together as the classification step does.
    /// </summary>
    private bool AdmitsItsNewWords(Thought template, SeqElement start)
    {
        SeqElement? input = start;
        for (SeqElement? position = template.GetTargetOfFirstLinkOfType(hasWords) as SeqElement;
            position is not null && input is not null; position = position.NXT)
        {
            Thought? phrase = theUKS.GetElementValue(input, patternRoot);
            Thought value = phrase ?? input.VLU;
            SeqElement last = input;
            while (phrase is not null && last.NXT is SeqElement next &&
                ReferenceEquals(theUKS.GetElementValue(next, patternRoot), phrase)) last = next;
            if (value.HasAncestor(unknownWordType) && position.VLU.HasProperty(isWildcard) &&
                position.VLU.Parents.Any(parent => parent.HasAncestor(wordType) && parent.HasProperty(closedClass)))
                return false;
            input = last.NXT;
        }
        return true;
    }

    // An unknown, unrecognized word matched only by single-element templates (e.g. a bare noun phrase).
    private bool IsLoneUnknownWord(SeqElement start, List<Thought> matches) =>
        theUKS.GetElementValue(start, patternRoot) is null && start.VLU.HasAncestor(unknownWordType) &&
        matches.All(template => (template.GetTargetOfFirstLinkOfType(hasWords) as SeqElement)?.NXT is null);
    // Resolve each occurrence separately. Its matchesTemplate Relationship carries its MEANS results.
    public List<Thought> GetMeanings(Thought sentenceThought)
    {
        var meanings = new List<Thought>();
        if (means is null || matchesTemplate is null || isWildcard is null || fills is null) return meanings;
        if (sentenceThought.GetTargetOfFirstLinkOfType("hasWords") is not SeqElement first) return meanings;

        bool added;
        do
        {
            added = false;
            SeqElement? previous = null;
            for (SeqElement? start = first; start is not null; previous = start, start = start.NXT)
                foreach (Link match in start.LinksTo.Where(link => link.LinkType == matchesTemplate))
                {
                    // Work at the first word of this template's contiguous run.
                    if (previous?.LinksTo.Any(link => link.LinkType == matchesTemplate && link.To == match.To) == true)
                        continue;
                    if (match.To.GetTargetOfFirstLinkOfType("hasWords") is not SeqElement pattern) continue;
                    SeqElement last = start;
                    while (last.NXT is SeqElement next &&
                        next.LinksTo.Any(link => link.LinkType == matchesTemplate && link.To == match.To))
                        last = next;

                    // The template must account for exactly this run of words: walk both together,
                    // binding each covered word to the template element it fills.
                    SeqElement? input = start;
                    SeqElement? position = pattern;
                    for (; position is not null && input is not null && input != last.NXT; position = position.NXT)
                    {
                        SeqElement? filled = FilledThrough(input, position, match.To, last, out _);
                        if (filled is null) break;
                        for (SeqElement word = input; ; word = word.NXT!)
                        {
                            word.AddLink(fills, position);
                            if (ReferenceEquals(word, filled)) break;
                        }
                        input = filled.NXT;
                    }
                    if (position is not null || !ReferenceEquals(input, last.NXT)) continue;

                    // Substitute the meaning bound to each role or class wildcard in each MEANS pattern.
                    foreach (Link definition in match.To.LinksTo.Where(link => link.LinkType == means))
                    {
                        Thought? resolved;
                        if (definition.To is Link relationship)
                        {
                            Thought? from = Bind(relationship.From, pattern, start, last, match.To);
                            Thought? type = Bind(relationship.LinkType, pattern, start, last, match.To);
                            Thought? to = Bind(relationship.To, pattern, start, last, match.To);
                            resolved = from is null || type is null || to is null ? null : new Link(from, type, to);
                        }
                        else resolved = Bind(definition.To, pattern, start, last, match.To);
                        if (resolved is null) continue;
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
    /// meaning and the element covers that whole phrase.
    /// </summary>
    /// <returns>The last sentence word the element covers, or null if it cannot be filled here.</returns>
    private SeqElement? FilledThrough(SeqElement input, SeqElement element, Thought template, SeqElement last, out Thought? value)
    {
        Thought expected = element.VLU;
        var classes = expected.Parents.Where(parent => !parent.HasProperty(isWildcard)).ToList();
        bool acceptsWord = expected.HasProperty(isWildcard)
            ? classes.Count == 0 || classes.Any(input.VLU.HasAncestor)
            : ReferenceEquals(expected, input.VLU);
        if (acceptsWord)
        {
            value = input.VLU.LinksTo.FirstOrDefault(link => link.LinkType == means)?.To;
            return input;
        }

        // A larger template consumes the meaning of an already resolved phrase.
        Link? child = input.LinksTo.FirstOrDefault(link => link.LinkType == matchesTemplate &&
            link.To != template && classes.Any(link.To.HasAncestor));
        value = child?.LinksTo.FirstOrDefault(link => link.LinkType == means && link.To is not Link)?.To;
        if (value is null) return null;
        SeqElement end = input;
        while (end != last && end.NXT is SeqElement next &&
            next.LinksTo.Any(link => link.LinkType == matchesTemplate && link.To == child!.To))
            end = next;
        return end;
    }

    /// <summary>
    /// Resolves one MEANS operand by reference. An operand that is the value of one of this
    /// template's elements (a role or class wildcard) is replaced by the meaning of the sentence
    /// word bound to that element through FILLS. The bare ?? is an open query variable and any
    /// other non-wildcard operand is used as written. A role or class wildcard that names no
    /// element of this template cannot be resolved; it must never reach Execute as written.
    /// </summary>
    private Thought? Bind(Thought? operand, SeqElement pattern, SeqElement start, SeqElement last, Thought template)
    {
        for (SeqElement? element = pattern; element is not null; element = element.NXT)
        {
            if (!ReferenceEquals(operand, element.VLU)) continue;
            for (SeqElement? word = start; word is not null && word != last.NXT; word = word.NXT)
                if (word.LinksTo.Any(link => link.LinkType == fills && link.To == element))
                {
                    FilledThrough(word, element, template, last, out Thought? value);
                    return value;
                }
            return null;
        }
        if (operand is not null && operand.HasProperty(isWildcard) && !ReferenceEquals(operand, theUKS.Labeled("??")))
            return null;
        return operand;
    }

    public List<Link> Execute(List<Thought> meanings)
    {
        var results = new List<Link>();
        foreach (Thought meaning in meanings)
            if (meaning is Link action)
                results.AddRange(theUKS.ApplyTestOrSetAction(action));
        return results;
    }

    #region GenerateSentence

    /// <summary>
    /// Expresses a relationship in English by running an assertion template in reverse. A sentence
    /// template is a candidate when its MEANS pattern unifies with the fact: an operand that is one of
    /// the template's elements (a role or class wildcard) is bound to the fact's corresponding Thought,
    /// and any other operand must express the fact's Thought itself. Each template offers its word
    /// sequences in order of preference (no free choices first, then singular words, then spelling
    /// order); the first sequence that satisfies every word order constraint is the sentence.
    /// </summary>
    /// <returns>The sentence, or null if no template can express the relationship.</returns>
    public string? GenerateSentence(Link fact)
    {
        if (templateRoot is null || means is null || hasWords is null ||
            fact.From is null || fact.LinkType is null || fact.To is null) return null;
        // Only ordinary relationships are stated. A query (.?) or write (.!) action is not a fact.
        if (fact.LinkType.HasProperty(setRelationship) || fact.LinkType.HasProperty("isPossible")) return null;

        foreach (bool allowChoices in new[] { false, true })
            foreach (Thought template in templateRoot.Children)
            {
                if (template.GetTargetOfFirstLinkOfType(hasWords) is not SeqElement pattern) continue;
                foreach (Link definition in template.LinksTo.Where(link => link.LinkType == means))
                {
                    if (definition.To is not Link relationship) continue;
                    // A fixed operand is part of what the template says, so it must agree with the fact.
                    bool fromAgrees = IsElementValue(pattern, relationship.From) || ReferenceEquals(relationship.From, fact.From);
                    bool typeAgrees = IsElementValue(pattern, relationship.LinkType) || Expresses(relationship.LinkType, fact.LinkType);
                    bool toAgrees = IsElementValue(pattern, relationship.To) || ReferenceEquals(relationship.To, fact.To);
                    if (!fromAgrees || !typeAgrees || !toAgrees) continue;

                    Thought? Bound(Thought value) =>
                        ReferenceEquals(value, relationship.From) ? fact.From :
                        ReferenceEquals(value, relationship.LinkType) ? fact.LinkType :
                        ReferenceEquals(value, relationship.To) ? fact.To : null;
                    foreach (List<Thought> words in ExpressTemplate(pattern, template, Bound, allowChoices))
                        if (FollowsWordOrder(words)) return Format(words);
                }
            }
        return null;
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
            foreach (List<Thought> rest in ExpressTemplate(element.NXT, template, bound, allowChoices))
                yield return part.Concat(rest).ToList();
    }

    /// <summary>Offers the words, or phrases, of the element's class that express a concept.</summary>
    private IEnumerable<List<Thought>> Express(Thought element, Thought concept)
    {
        foreach (Thought wordClass in ElementClasses(element))
        {
            if (wordClass.HasAncestor(wordType))
            {
                foreach (Thought word in WordsOf(wordClass).Where(word => Expresses(word.GetTargetOfFirstLinkOfType(means), concept)))
                    yield return new List<Thought> { word };
                continue;
            }
            // A phrase class: use a phrase whose own MEANS is the element bound to the concept.
            // A phrase needing free choices is offered only if no phrase can do without them.
            bool offered = false;
            foreach (bool allowChoices in new[] { false, true })
            {
                if (offered) break;
                foreach (Thought phrase in wordClass.Children)
                {
                    if (phrase.GetTargetOfFirstLinkOfType(hasWords) is not SeqElement pattern) continue;
                    Thought? slot = phrase.LinksTo.FirstOrDefault(link => link.LinkType == means && link.To is not Link)?.To;
                    if (slot is null) continue;
                    foreach (List<Thought> words in ExpressTemplate(pattern, phrase, value => ReferenceEquals(value, slot) ? concept : null, allowChoices))
                    {
                        offered = true;
                        yield return words;
                    }
                }
            }
        }
    }

    /// <summary>Offers the free choices for an unbound element: the words, or phrases, of its class.</summary>
    private IEnumerable<List<Thought>> Choose(Thought element)
    {
        foreach (Thought wordClass in ElementClasses(element))
        {
            if (wordClass.HasAncestor(wordType))
            {
                foreach (Thought word in WordsOf(wordClass))
                    yield return new List<Thought> { word };
                continue;
            }
            bool offered = false;
            foreach (bool allowChoices in new[] { false, true })
            {
                if (offered) break;
                foreach (Thought phrase in wordClass.Children)
                    if (phrase.GetTargetOfFirstLinkOfType(hasWords) is SeqElement pattern)
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
    /// (an ongoing verb follows a copula).
    /// </summary>
    private bool FollowsWordOrder(List<Thought> words) =>
        words.Zip(words.Skip(1)).All(pair => MayPrecede(pair.First, pair.Second));

    private bool MayPrecede(Thought left, Thought right)
    {
        foreach (Link rule in left.AncestorsWithSelf.SelectMany(thought => thought.LinksTo))
        {
            if (rule.LinkType is null || rule.To is null) continue;
            if (ReferenceEquals(rule.LinkType, precedesWord) && !right.HasAncestor(rule.To)) return false;
            if (ReferenceEquals(rule.LinkType, precedesWordStartingWith) && FirstLetter(right)?.HasAncestor(rule.To) != true) return false;
        }
        foreach (Link rule in right.AncestorsWithSelf.SelectMany(thought => thought.LinksTo))
            if (rule.LinkType is not null && rule.To is not null &&
                ReferenceEquals(rule.LinkType, followsWord) && !left.HasAncestor(rule.To)) return false;
        return true;
    }

    private Thought? FirstLetter(Thought word) => (word.GetTargetOfFirstLinkOfType(spelled) as SeqElement)?.VLU;

    /// <summary>
    /// A word meaning expresses a concept when it is that concept, or when it is a write action
    /// whose ordinary relationship type is that concept (w:has means has.!, which expresses has).
    /// </summary>
    private bool Expresses(Thought? meaning, Thought concept) => meaning is not null &&
        (ReferenceEquals(meaning, concept) || meaning.HasProperty(setRelationship) &&
            ReferenceEquals(theUKS.GetActionRelationshipType(meaning), concept));

    private static bool IsElementValue(SeqElement pattern, Thought? value)
    {
        for (SeqElement? element = pattern; element is not null; element = element.NXT)
            if (ReferenceEquals(element.VLU, value)) return true;
        return false;
    }

    private bool IsMeansOperand(Thought template, Thought value) =>
        template.LinksTo.Where(link => link.LinkType == means).Any(link => link.To is Link relationship
            ? ReferenceEquals(relationship.From, value) || ReferenceEquals(relationship.LinkType, value) || ReferenceEquals(relationship.To, value)
            : ReferenceEquals(link.To, value));

    // The classes constraining an element, as in FilledThrough: its parents other than wildcards and roles.
    private IEnumerable<Thought> ElementClasses(Thought element) =>
        element.Parents.Where(parent => !parent.HasProperty(isWildcard));

    // Vocabulary of a word class: singular forms first, then in spelling order, so choices are repeatable.
    private IEnumerable<Thought> WordsOf(Thought wordClass) => wordClass.Descendants
        .Where(word => word.GetTargetOfFirstLinkOfType(spelled) is SeqElement && word.GetTargetOfFirstLinkOfType(means) is not null)
        .OrderBy(word => plural is not null && word.HasAncestor(plural))
        .ThenBy(Spelling, StringComparer.Ordinal);

    // A word's text comes from its spelling sequence, not from its label.
    private string Spelling(Thought word) =>
        word.GetTargetOfFirstLinkOfType(spelled) is SeqElement spelling
            ? string.Concat(theUKS.EnumerateSequenceElements(spelling)
                .Select(letter => letter.VLU.Label[UKS.UKS.CharacterPrefix.Length..])).ToLowerInvariant()
            : word.Label;

    private string Format(List<Thought> words)
    {
        string text = string.Join(" ", words.Select(Spelling));
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    #endregion
}

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
    private Thought? classificationWrite;
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
        classificationWrite = theUKS.GetOrAddThought("is-a.!");
        isA = theUKS.Labeled("is-a");
        spelled = theUKS.Labeled("spelled");

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

        // Phrase patterns, followed by a sentence composed of those phrases.
        foreach (string definition in new[]
        {
            "[!->is-a->LinkType]",
            "[!->hasProperty->setRelationship]",
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
            "[sentenceTemplate->hasWords->^??.nounPhrase ??.verbPhrase ??.nounPhrase]",
            "[classificationTemplate->is-a->template]",
            "[classificationTemplate->hasWords->^??.nounPhrase ??.classificationPhrase ??.nounPhrase]",
            "[copulaClassificationTemplate->is-a->template]",
            "[copulaClassificationTemplate->hasWords->^??.nounPhrase ??.copula ??.nounPhrase]",
            "[copulaAttributeTemplate->is-a->template]",
            "[copulaAttributeTemplate->hasWords->^??.nounPhrase ??.copula ??.adjective]",
            "[copulaAttributeTemplate->means->[??.nounPhrase->is.!->??.adjective]]",
            "[possessionTemplate->is-a->template]",
            "[possessionTemplate->hasWords->^??.nounPhrase ??.possessionVerb ??.nounPhrase]",
            "[actionTemplate->is-a->template]",
            "[actionTemplate->hasWords->^??.nounPhrase ??.actionVerb ??.nounPhrase]",
            "[negativeAttributeTemplate->is-a->template]",
            "[negativeAttributeTemplate->hasWords->^??.nounPhrase ??.copula ??.negationWord ??.adjective]",
            "[negativeClassificationTemplate->is-a->template]",
            "[negativeClassificationTemplate->hasWords->^??.nounPhrase ??.copula ??.negationWord ??.nounPhrase]",
            "[attributeQueryTemplate->is-a->template]",
            "[attributeQueryTemplate->hasWords->^??.copula ??.nounPhrase ??.adjective]",
            "[classificationQueryTemplate->is-a->template]",
            "[classificationQueryTemplate->hasWords->^??.copula ??.nounPhrase ??.nounPhrase]",
            "[bareClassificationQueryTemplate->is-a->template]",
            "[bareClassificationQueryTemplate->hasWords->^??.copula ??.noun ??.noun]",
            "[wildcardAttributeQueryTemplate->is-a->template]",
            "[wildcardAttributeQueryTemplate->hasWords->^w:what ??.copula ??.adjective]",
            "[wildcardAttributeQueryTemplate->means->[??->is.?->??.adjective]]"
        })
            theUKS.ProcessSingleLine(definition);

        // Positions distinguish subject and object even though both use ??.nounPhrase.
        Thought sentenceTemplate = theUKS.Labeled("sentenceTemplate");
        SeqElement pattern = (SeqElement)sentenceTemplate.GetTargetOfFirstLinkOfType("hasWords");
        var meaning = new Link(pattern, pattern.NXT, pattern.NXT.NXT);
        sentenceTemplate.AddLink(means, meaning);

        foreach (string name in new[] { "classificationTemplate", "copulaClassificationTemplate" })
        {
            Thought classificationTemplate = theUKS.Labeled(name);
            SeqElement classificationPattern = (SeqElement)classificationTemplate.GetTargetOfFirstLinkOfType("hasWords");
            var classificationMeaning = new Link(classificationPattern, classificationWrite!, classificationPattern.NXT.NXT);
            classificationTemplate.AddLink(means, classificationMeaning);
        }

        // These verbs supply their own executable relationship type through MEANS.
        foreach (string name in new[] { "possessionTemplate", "actionTemplate" })
        {
            Thought template = theUKS.Labeled(name);
            SeqElement head = (SeqElement)template.GetTargetOfFirstLinkOfType("hasWords");
            template.AddLink(means, new Link(head, head.NXT, head.NXT.NXT));
        }
        foreach (string name in new[] { "negativeAttributeTemplate", "negativeClassificationTemplate",
            "attributeQueryTemplate", "classificationQueryTemplate", "bareClassificationQueryTemplate" })
        {
            Thought template = theUKS.Labeled(name);
            SeqElement head = (SeqElement)template.GetTargetOfFirstLinkOfType("hasWords");
            bool negative = name.StartsWith("negative", StringComparison.Ordinal);
            bool classification = name.Contains("Classification") || name == "classificationQueryTemplate";
            Thought type = theUKS.GetOrAddThought((classification ? "is-a" : "is") + (negative ? ".not.!" : ".?"));
            template.AddLink(means, negative
                ? new Link(head, type, head.NXT.NXT.NXT)
                : new Link(head.NXT, type, head.NXT.NXT));
        }

        // Initially teach the ten digit quantities explicitly. Each is ordinary UKS knowledge.
        // This keeps quantity composition out of the interpretation code while we review it.
        for (int quantity = 0; quantity <= 9; quantity++)
        {
            string name = "quantityPossession" + quantity;
            theUKS.ProcessSingleLine($"[{name}->is-a->template]");
            theUKS.ProcessSingleLine($"[{name}->hasWords->^??.nounPhrase ??.possessionVerb w:{quantity} ??.nounPhrase]");
            Thought template = theUKS.Labeled(name);
            SeqElement head = (SeqElement)template.GetTargetOfFirstLinkOfType("hasWords");
            Thought type = theUKS.GetOrAddThought($"has.{quantity}.!");
            template.AddLink(means, new Link(head, type, head.NXT.NXT.NXT));
        }
    }

    #endregion

    public string Input(string sentence, bool rememberUnknownWords = false)
    {
        try
        {
            Thought? sentenceThought = GetSequenceFromText(sentence, rememberUnknownWords);
            if (sentenceThought is null) return "Enter a sentence.";

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
                output += "\n\nQuery results\n" + (executed.Count == 0
                    ? "No matching relationships." : string.Join("\n", executed));
            return output;
        }
        catch (FormatException error)
        {
            return error.Message;
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
                // below works entirely with Thoughts, including a newly heard word.
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
        do
        {
            added = false;
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
                    longest = length;
                    var referringThoughts = theUKS.GetReferringThoughts(sequenceMatch.seqNode, hasWords!);
                    foreach (Thought template in referringThoughts)
                        if (template.HasAncestor(patternRoot) && !matches.Contains(template)) matches.Add(template);
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
        } while (added);
        for (SeqElement? element = first; element is not null; element = element.NXT)
            foreach (Link link in element.LinksTo)
                if (link.LinkType == matchesTemplate && !templates.Contains(link.To)) templates.Add(link.To);
        return templates;
    }
    // Resolve each occurrence separately. Its matchesTemplate Relationship carries its MEANS results.
    public List<Thought> GetMeanings(Thought sentenceThought)
    {
        var meanings = new List<Thought>();
        if (means is null || matchesTemplate is null || isWildcard is null) return meanings;
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

                    var replacements = new List<Thought?>();
                    SeqElement? input = start;
                    SeqElement? position = pattern;
                    for (; position is not null && input is not null && input != last.NXT; position = position.NXT)
                    {
                        Thought expected = position.VLU;
                        var classes = expected.Parents.Where(parent => !parent.HasProperty(isWildcard)).ToList();
                        bool acceptsWord = expected.HasProperty(isWildcard)
                            ? classes.Count == 0 || classes.Any(input.VLU.HasAncestor)
                            : ReferenceEquals(expected, input.VLU);
                        Thought? value;
                        if (acceptsWord)
                        {
                            value = input.VLU.LinksTo.FirstOrDefault(link => link.LinkType == means)?.To;
                        }
                        else
                        {
                            // A larger template consumes the meaning of an already resolved phrase.
                            Link? child = input.LinksTo.FirstOrDefault(link => link.LinkType == matchesTemplate &&
                                link.To != match.To && classes.Any(link.To.HasAncestor));
                            value = child?.LinksTo.FirstOrDefault(link => link.LinkType == means && link.To is not Link)?.To;
                            if (value is null) break;
                            while (input != last && input.NXT is SeqElement phraseNext &&
                                phraseNext.LinksTo.Any(link => link.LinkType == matchesTemplate && link.To == child!.To))
                                input = phraseNext;
                        }
                        replacements.Add(value);
                        input = input.NXT;
                    }
                    if (position is not null || !ReferenceEquals(input, last.NXT)) continue;

                    // Substitute either a wildcard or an explicit template position in each MEANS pattern.
                    foreach (Link definition in match.To.LinksTo.Where(link => link.LinkType == means))
                    {
                        var operands = definition.To is Link relationship
                            ? new List<Thought?> { relationship.From, relationship.LinkType, relationship.To }
                            : new List<Thought?> { definition.To };
                        for (int operand = 0; operand < operands.Count; operand++)
                        {
                            int index = 0;
                            for (SeqElement? templatePosition = pattern; templatePosition is not null;
                                templatePosition = templatePosition.NXT, index++)
                                if (ReferenceEquals(operands[operand], templatePosition) ||
                                    ReferenceEquals(operands[operand], templatePosition.VLU))
                                {
                                    operands[operand] = index < replacements.Count ? replacements[index] : null;
                                    break;
                                }
                        }
                        if (operands.Any(value => value is null)) continue;
                        Thought resolved = definition.To is Link
                            ? new Link(operands[0]!, operands[1]!, operands[2]!)
                            : operands[0]!;
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

    public List<Link> Execute(List<Thought> meanings)
    {
        var results = new List<Link>();
        foreach (Thought meaning in meanings)
            if (meaning is Link action)
                results.AddRange(theUKS.ApplyTestOrSetAction(action));
        return results;
    }
}

#nullable enable

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

// Language knowledge: the vocabulary file and the template definitions, all ordinary UKS data.
public sealed partial class ModuleInterpret
{
    #region Language knowledge

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
        clause = theUKS.GetOrAddThought("clause", templateRoot);
        vlu = theUKS.Labeled("VLU");
        templateSequenceSearch = theUKS.GetOrAddThought("InterpretTemplateSearch", theUKS.Labeled("TemplateSequenceSearch"));
        // A loaded network may predate this option. Initialization must create it, not just look it up.
        templateSequenceSearch.AddLink("hasProperty", theUKS.GetOrAddThought("allowTrailingInput", "SearchOption"));
        matchesTemplate = theUKS.GetOrAddThought("matchesTemplate", "LinkType");
        isWildcard = theUKS.Labeled("isWildcard");
        wordType = theUKS.Labeled("wordType");
        unknownWordType = theUKS.Labeled("unknownWordType");
        wordRoot = theUKS.Labeled("word");
        // A sentence word fills a template element: the binding a MEANS role is resolved through.
        // GetMeanings records it while confirming that a template covers its run of words.
        fills = theUKS.GetOrAddThought("fills", "LinkType");
        // Each match of a template is a Thought of its own (Recognition: RecordMatch).
        matchRoot = theUKS.GetOrAddThought("templateMatches", "LanguageElement");
        matchOf = theUKS.GetOrAddThought("matchOf", "LinkType");
        startsMatch = theUKS.GetOrAddThought("startsMatch", "LinkType");
        endsAt = theUKS.GetOrAddThought("endsAt", "LinkType");
        inMatch = theUKS.GetOrAddThought("inMatch", "LinkType");
        isA = theUKS.Labeled("is-a");
        spelled = theUKS.Labeled("spelled");
        hasInflection = theUKS.Labeled("hasInflection");
        marksInflection = theUKS.Labeled("marksInflection");
        inflectedAs = theUKS.Labeled("inflectedAs");
        inflectionRoot = theUKS.Labeled("inflection");
        addsMeaning = theUKS.Labeled("addsMeaning");
        asksQuestion = theUKS.GetOrAddThought("asksQuestion", "Property");
        article = theUKS.Labeled("article");
        noun = theUKS.Labeled("noun");
        plural = theUKS.Labeled("plural");
        singular = theUKS.Labeled("singular");
        agreesInNumber = theUKS.Labeled("agreesInNumber");
        setRelationship = theUKS.Labeled("setRelationship");
        precedesWord = theUKS.Labeled("precedesWord");
        precedesWordStartingWith = theUKS.Labeled("precedesWordStartingWith");
        followsWord = theUKS.Labeled("followsWord");
        closedClass = theUKS.Labeled("closedClass");
        joinsClauses = theUKS.Labeled("joinsClauses");

        // Repair older vocabulary entries with a redundant direct word parent.
        foreach (Thought word in wordRoot!.Children.Where(word => word.GetTargetOfFirstLinkOfType(spelled) is not null).ToList())
        {
            if (word == wordType) continue;
            if (!word.HasAncestor(wordType)) word.AddParent(unknownWordType!);
            word.RemoveParent(wordRoot);
        }

        // Older networks gave inflected forms their own classes and meanings. A form now carries only
        // its spelling; its class and meaning are its base word's.
        foreach (Link inflected in theUKS.Labeled("word")!.Descendants.SelectMany(word => word.LinksTo)
            .Where(link => link.LinkType?.HasAncestor(hasInflection) == true).ToList())
        {
            Thought form = inflected.To;
            foreach (Link meaning in form.LinksTo.Where(link => link.LinkType == means).ToList()) form.RemoveLink(meaning);
            foreach (Thought type in form.Parents.Where(parent => parent.HasAncestor(wordType) || parent == theUKS.Labeled("plural")).ToList())
                form.RemoveParent(type);
        }
        // w:is meant the plain relationship type before the copula supplied it to templates (is.!).
        if (theUKS.Labeled("is") is Thought plainIs) theUKS.Labeled("w:is")?.RemoveLink(means!, plainIs);
        // location was briefly a kind of relationship, with w:in meaning in.!. A location is a place;
        // things are located at it.
        theUKS.Labeled("location")?.RemoveParent(theUKS.Labeled("LinkType"));
        if (theUKS.Labeled("in.!") is Thought inAction) theUKS.Labeled("w:in")?.RemoveLink(means!, inAction);
        if (theUKS.Labeled("on.!") is Thought onAction) theUKS.Labeled("w:on")?.RemoveLink(means!, onAction);

        // "Rover is a small dog" had templates of its own; the complement is now a described kind,
        // which an is-a statement reads as the class plus the description (Execute).
        foreach (string name in new[] { "classificationAttributeTemplate", "copulaClassificationAttributeTemplate" })
            theUKS.Labeled(name)?.Delete();
        // Three clauses had a template of their own until a compound could contain a compound.
        theUKS.Labeled("compound3Template")?.Delete();

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
            // A phrase that describesReferent means the thing its description picks out, not its
            // class: "the black dog" is a dog that is black, and nothing is said about dogs (Reference).
            "[nounPhrase1->hasProperty->describesReferent]",
            // "brown dogs": the same description without an article.
            "[nounPhrase5->is-a->nounPhrase]",
            "[nounPhrase5->hasWords->^??.adjective ??.noun]",
            "[nounPhrase5->means->??.noun]",
            "[nounPhrase5->means->[??.noun->is.!->??.adjective]]",
            "[nounPhrase5->hasProperty->describesReferent]",
            "[nounPhrase2->is-a->nounPhrase]",
            "[nounPhrase2->hasWords->^??.article ??.noun]",
            "[nounPhrase2->means->??.noun]",
            "[nounPhrase3->is-a->nounPhrase]",
            "[nounPhrase3->hasWords->^??.noun]",
            "[nounPhrase3->means->??.noun]",
            // "the cat in the kitchen": a prepositional phrase places the noun, as an adjective
            // describes it in nounPhrase1. The phrase means the noun.
            "[placeRole->is-a->templateRole]",
            "[nounPhrase4->is-a->nounPhrase]",
            "[nounPhrase4->hasWords->^??.nounPhrase.subjectRole ??.preposition ??.nounPhrase.placeRole]",
            "[nounPhrase4->means->??.nounPhrase.subjectRole]",
            "[nounPhrase4->means->[??.nounPhrase.subjectRole->??.preposition->??.nounPhrase.placeRole]]",
            "[nounPhrase4->hasProperty->describesReferent]",
            // How a described referent is found: "the" with a singular picks out one existing thing;
            // "a" or a plural names a kind (a subclass).
            "[referentDescriptor->is-a->LinkType]",
            "[oneDescribedAs->is-a->referentDescriptor]",
            "[kindDescribedAs->is-a->referentDescriptor]",
            "[verbPhrase1->is-a->verbPhrase]",
            // "is chasing": the element requires the present participle, which adds "ongoing" to the
            // verb's meaning (chases.! becomes chases.ongoing.!).
            "[verbPhrase1->hasWords->^??.auxiliary ??.actionVerb.presentParticiple]",
            "[verbPhrase1->means->??.actionVerb.presentParticiple]",
            "[sentenceTemplate->is-a->template]",
            "[sentenceTemplate->hasWords->^??.nounPhrase.subjectRole ??.verbPhrase ??.nounPhrase.complementRole]",
            "[sentenceTemplate->means->[??.nounPhrase.subjectRole->??.verbPhrase->??.nounPhrase.complementRole]]",
            "[classificationTemplate->is-a->template]",
            "[classificationTemplate->hasWords->^??.nounPhrase.subjectRole ??.classificationPhrase ??.nounPhrase.complementRole]",
            "[classificationTemplate->means->[??.nounPhrase.subjectRole->is-a.!->??.nounPhrase.complementRole]]",
            "[copulaClassificationTemplate->is-a->template]",
            "[copulaClassificationTemplate->hasWords->^??.nounPhrase.subjectRole ??.copula ??.nounPhrase.complementRole]",
            "[copulaClassificationTemplate->means->[??.nounPhrase.subjectRole->is-a.!->??.nounPhrase.complementRole]]",
            // The copula supplies the relationship type, so its tense reaches the meaning:
            // "Fido is brown" means is.!, "Fido was brown" is.past.!.
            "[copulaAttributeTemplate->is-a->template]",
            "[copulaAttributeTemplate->hasWords->^??.nounPhrase ??.copula ??.adjective]",
            "[copulaAttributeTemplate->means->[??.nounPhrase->??.copula->??.adjective]]",
            // "Fido and Rover are dogs." / "Fido and Rover are brown." Each of two subjects joined by
            // "and" gets the meaning: one MEANS pattern per subject.
            "[secondSubjectRole->is-a->templateRole]",
            "[conjoinedClassificationTemplate->is-a->template]",
            "[conjoinedClassificationTemplate->hasWords->^??.nounPhrase.subjectRole w:and ??.nounPhrase.secondSubjectRole ??.copula ??.nounPhrase.complementRole]",
            "[conjoinedClassificationTemplate->means->[??.nounPhrase.subjectRole->is-a.!->??.nounPhrase.complementRole]]",
            "[conjoinedClassificationTemplate->means->[??.nounPhrase.secondSubjectRole->is-a.!->??.nounPhrase.complementRole]]",
            "[conjoinedAttributeTemplate->is-a->template]",
            "[conjoinedAttributeTemplate->hasWords->^??.nounPhrase.subjectRole w:and ??.nounPhrase.secondSubjectRole ??.copula ??.adjective]",
            "[conjoinedAttributeTemplate->means->[??.nounPhrase.subjectRole->??.copula->??.adjective]]",
            "[conjoinedAttributeTemplate->means->[??.nounPhrase.secondSubjectRole->??.copula->??.adjective]]",
            // "Fido is in the yard." The preposition supplies the relationship type (w:in means located.!).
            "[locativeTemplate->is-a->template]",
            "[locativeTemplate->hasWords->^??.nounPhrase.subjectRole ??.copula ??.preposition ??.nounPhrase.complementRole]",
            "[locativeTemplate->means->[??.nounPhrase.subjectRole->??.preposition->??.nounPhrase.complementRole]]",
            // "Fido is outside."
            "[locativeAdverbTemplate->is-a->template]",
            "[locativeAdverbTemplate->hasWords->^??.nounPhrase ??.copula ??.locativeAdverb]",
            "[locativeAdverbTemplate->means->[??.nounPhrase->located.!->??.locativeAdverb]]",
            // These verbs supply their own executable relationship type through MEANS.
            "[possessionTemplate->is-a->template]",
            "[possessionTemplate->hasWords->^??.nounPhrase.subjectRole ??.possessionVerb ??.nounPhrase.complementRole]",
            "[possessionTemplate->means->[??.nounPhrase.subjectRole->??.possessionVerb->??.nounPhrase.complementRole]]",
            "[actionTemplate->is-a->template]",
            "[actionTemplate->hasWords->^??.nounPhrase.subjectRole ??.actionVerb ??.nounPhrase.complementRole]",
            "[actionTemplate->means->[??.nounPhrase.subjectRole->??.actionVerb->??.nounPhrase.complementRole]]",
            // "Fido chased the cat in the yard." / "Fido plays fetch outside." A prepositional phrase
            // (or locative adverb) after an action places the event: the action is itself the source,
            // as in UKSWeb Lab 4 ([[Fido->plays->fetch]->located->outside]). Executing it also states
            // the action.
            "[eventLocationTemplate->is-a->template]",
            "[eventLocationTemplate->hasWords->^??.nounPhrase.subjectRole ??.actionVerb ??.nounPhrase.complementRole ??.preposition ??.nounPhrase.placeRole]",
            "[eventLocationTemplate->means->[[??.nounPhrase.subjectRole->??.actionVerb->??.nounPhrase.complementRole]->??.preposition->??.nounPhrase.placeRole]]",
            "[eventLocationAdverbTemplate->is-a->template]",
            "[eventLocationAdverbTemplate->hasWords->^??.nounPhrase.subjectRole ??.actionVerb ??.nounPhrase.complementRole ??.locativeAdverb]",
            "[eventLocationAdverbTemplate->means->[[??.nounPhrase.subjectRole->??.actionVerb->??.nounPhrase.complementRole]->located.!->??.locativeAdverb]]",
            // "Fido went outside." A motion verb relates the subject to where it went.
            "[motionTemplate->is-a->template]",
            "[motionTemplate->hasWords->^??.nounPhrase.subjectRole ??.motionVerb ??.locativeAdverb]",
            "[motionTemplate->means->[??.nounPhrase.subjectRole->??.motionVerb->??.locativeAdverb]]",
            "[negativeAttributeTemplate->is-a->template]",
            "[negativeAttributeTemplate->hasWords->^??.nounPhrase ??.copula ??.negationWord ??.adjective]",
            "[negativeAttributeTemplate->means->[??.nounPhrase->is.not.!->??.adjective]]",
            "[negativeClassificationTemplate->is-a->template]",
            "[negativeClassificationTemplate->hasWords->^??.nounPhrase.subjectRole ??.copula ??.negationWord ??.nounPhrase.complementRole]",
            "[negativeClassificationTemplate->means->[??.nounPhrase.subjectRole->is-a.not.!->??.nounPhrase.complementRole]]",
            // A template that asksQuestion turns the relationship its words supply into a query: the
            // same template asks "Is Fido brown?" (is.?) and "Was Fido brown?" (is.past.?).
            "[asksQuestion->is-a->Property]",
            "[attributeQueryTemplate->is-a->template]",
            "[attributeQueryTemplate->hasWords->^??.copula ??.nounPhrase ??.adjective]",
            "[attributeQueryTemplate->means->[??.nounPhrase->??.copula->??.adjective]]",
            "[attributeQueryTemplate->hasProperty->asksQuestion]",
            // "Is Fido in the yard?"
            "[locativeQueryTemplate->is-a->template]",
            "[locativeQueryTemplate->hasWords->^??.copula ??.nounPhrase.subjectRole ??.preposition ??.nounPhrase.complementRole]",
            "[locativeQueryTemplate->means->[??.nounPhrase.subjectRole->??.preposition->??.nounPhrase.complementRole]]",
            "[locativeQueryTemplate->hasProperty->asksQuestion]",
            // "Is Fido chasing the cat?" (chases.ongoing.?)
            "[ongoingActionQueryTemplate->is-a->template]",
            "[ongoingActionQueryTemplate->hasWords->^??.copula ??.nounPhrase.subjectRole ??.actionVerb.presentParticiple ??.nounPhrase.complementRole]",
            "[ongoingActionQueryTemplate->means->[??.nounPhrase.subjectRole->??.actionVerb.presentParticiple->??.nounPhrase.complementRole]]",
            "[ongoingActionQueryTemplate->hasProperty->asksQuestion]",
            // "Does Fido chase cats?" (chases.?)
            "[actionQueryTemplate->is-a->template]",
            "[actionQueryTemplate->hasWords->^??.doAuxiliary ??.nounPhrase.subjectRole ??.actionVerb ??.nounPhrase.complementRole]",
            "[actionQueryTemplate->means->[??.nounPhrase.subjectRole->??.actionVerb->??.nounPhrase.complementRole]]",
            "[actionQueryTemplate->hasProperty->asksQuestion]",
            // "What do dogs do?" asks for every relationship whose type is an action.
            "[wildcardActionQueryTemplate->is-a->template]",
            "[wildcardActionQueryTemplate->hasWords->^w:what ??.doAuxiliary ??.nounPhrase w:does]",
            "[wildcardActionQueryTemplate->means->[??.nounPhrase->action.?->??]]",
            // "Where is Fido?" asks where it is located.
            "[whereQueryTemplate->is-a->template]",
            "[whereQueryTemplate->hasWords->^w:where ??.copula ??.nounPhrase]",
            "[whereQueryTemplate->means->[??.nounPhrase->located.?->??]]",
            // "Where does Fido play fetch?" asks where the event is located. In a question the
            // action is looked up, not stated.
            "[whereEventQueryTemplate->is-a->template]",
            "[whereEventQueryTemplate->hasWords->^w:where ??.doAuxiliary ??.nounPhrase.subjectRole ??.actionVerb ??.nounPhrase.complementRole]",
            "[whereEventQueryTemplate->means->[[??.nounPhrase.subjectRole->??.actionVerb->??.nounPhrase.complementRole]->located.?->??]]",
            // "Where did Fido go?" (did lends the past: goes.past.?) / "Where has Fido gone?"
            "[whereMotionQueryTemplate->is-a->template]",
            "[whereMotionQueryTemplate->hasWords->^w:where ??.doAuxiliary ??.nounPhrase ??.motionVerb]",
            "[whereMotionQueryTemplate->means->[??.nounPhrase->??.motionVerb->??]]",
            "[whereMotionQueryTemplate->hasProperty->asksQuestion]",
            "[wherePerfectMotionQueryTemplate->is-a->template]",
            "[wherePerfectMotionQueryTemplate->hasWords->^w:where ??.perfectAuxiliary ??.nounPhrase ??.motionVerb.pastParticiple]",
            "[wherePerfectMotionQueryTemplate->means->[??.nounPhrase->??.motionVerb.pastParticiple->??]]",
            "[wherePerfectMotionQueryTemplate->hasProperty->asksQuestion]",
            // "Did Fido go outside?" / "Has Fido gone outside?"
            "[motionQueryTemplate->is-a->template]",
            "[motionQueryTemplate->hasWords->^??.doAuxiliary ??.nounPhrase.subjectRole ??.motionVerb ??.locativeAdverb]",
            "[motionQueryTemplate->means->[??.nounPhrase.subjectRole->??.motionVerb->??.locativeAdverb]]",
            "[motionQueryTemplate->hasProperty->asksQuestion]",
            "[perfectMotionQueryTemplate->is-a->template]",
            "[perfectMotionQueryTemplate->hasWords->^??.perfectAuxiliary ??.nounPhrase.subjectRole ??.motionVerb.pastParticiple ??.locativeAdverb]",
            "[perfectMotionQueryTemplate->means->[??.nounPhrase.subjectRole->??.motionVerb.pastParticiple->??.locativeAdverb]]",
            "[perfectMotionQueryTemplate->hasProperty->asksQuestion]",
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

        // Clauses: a statement template whose meaning is one relationship can also be a clause of a
        // larger sentence, joined by a word that joinsClauses (BasicWords). A ??.clause element takes
        // the clause's statement as its value.
        foreach (string definition in new[]
        {
            "[firstClauseRole->is-a->templateRole]",
            "[secondClauseRole->is-a->templateRole]",
            "[resultRole->is-a->templateRole]",
            "[conditionRole->is-a->templateRole]",
            // "Fido is a dog and Fido is green." Both clauses are stated, and related: said together,
            // they probably hold at the same time (the conjoinedWith that teaching also writes).
            "[compoundTemplate->is-a->template]",
            "[compoundTemplate->hasWords->^??.clause.firstClauseRole w:and ??.clause.secondClauseRole]",
            "[compoundTemplate->means->[??.clause.firstClauseRole->conjoinedWith.!->??.clause.secondClauseRole]]",
            // "A and B and C" is "(A and B) and C": a compound is a clause, so it can be the first clause
            // of the next one (each match is recorded on its own; Recognition: RecordMatch).
            // "Fido is cold if Fido is outside." A rule: neither clause is stated (Execute stores them
            // as possibilities joined by IF, which the UKS evaluates when asked). conditionalOn is an
            // ordinary relationship type here: in a definition, IF's operands would be read as patterns.
            "[conditionalOn->is-a->LinkType]",
            "[conditionalTemplate->is-a->template]",
            "[conditionalTemplate->hasWords->^??.clause.resultRole w:if ??.clause.conditionRole]",
            "[conditionalTemplate->means->[??.clause.resultRole->conditionalOn.!->??.clause.conditionRole]]",
            // "If Fido is outside, Fido is cold." The comma is read as a word that joins clauses.
            "[conditionFirstTemplate->is-a->template]",
            "[conditionFirstTemplate->hasWords->^w:if ??.clause.conditionRole w:comma ??.clause.resultRole]",
            "[conditionFirstTemplate->means->[??.clause.resultRole->conditionalOn.!->??.clause.conditionRole]]",
        })
            Define(definition);
        // The clauses. compoundTemplate is one, so "A and B and C" nests, and in "A if B and C" the
        // condition is "B and C". A rule is not a clause: "if" joins most loosely. The conjoined-subject
        // templates mean two relationships, so they are not clauses yet; questions are not either.
        foreach (string name in new[]
        {
            "sentenceTemplate", "classificationTemplate", "copulaClassificationTemplate", "copulaAttributeTemplate",
            "locativeTemplate", "locativeAdverbTemplate", "possessionTemplate", "actionTemplate",
            "eventLocationTemplate", "eventLocationAdverbTemplate", "motionTemplate", "negativeAttributeTemplate", "negativeClassificationTemplate",
            "compoundTemplate",
        }.Concat("0123456789".Select(digit => "quantityPossession" + digit)))
            Define($"[{name}->is-a->clause]");
        // "Is Fido green if Fido is outside?" A question asked on an assumption: Execute states the
        // condition, then asks (a question's changes are always undone). The yes/no questions are
        // question clauses, a class of their own so that "and" and a rule's result take statements only.
        foreach (string definition in new[]
        {
            "[questionClause->is-a->template]",
            "[assuming->is-a->LinkType]",
            "[hypotheticalQueryTemplate->is-a->template]",
            "[hypotheticalQueryTemplate->hasWords->^??.questionClause.resultRole w:if ??.clause.conditionRole]",
            "[hypotheticalQueryTemplate->means->[??.questionClause.resultRole->assuming.?->??.clause.conditionRole]]",
            "[hypotheticalQueryTemplate->hasProperty->asksQuestion]",
        })
            Define(definition);
        foreach (string name in new[]
        {
            "attributeQueryTemplate", "locativeQueryTemplate", "ongoingActionQueryTemplate", "actionQueryTemplate", "motionQueryTemplate", "perfectMotionQueryTemplate",
            "classificationQueryTemplate", "bareClassificationQueryTemplate", "possessionQueryTemplate",
            "negativeAttributeQueryTemplate", "negativeClassificationQueryTemplate",
        })
            Define($"[{name}->is-a->questionClause]");
        // A comma is read as a word; a text file can't spell it, so its spelling is made here.
        if (theUKS.Labeled("w:comma") is Thought comma && comma.GetTargetOfFirstLinkOfType(spelled) is null)
            theUKS.CreateSpellingSequence(",", comma);
    }

    /// <summary>
    /// Adds one template definition. A saved network keeps the definition an earlier version gave,
    /// and adding hasWords again would give the template a second sequence, so a word sequence
    /// definition replaces the template's existing words and meanings (each template's MEANS
    /// definitions follow its hasWords definition, and so are added again).
    /// </summary>
    private void Define(string definition)
    {
        string[] parts = definition.TrimStart('[').Split("->");
        if (parts.Length > 2 && parts[1] == "hasWords" && theUKS.Labeled(parts[0]) is Thought template)
        {
            template.RemoveLinks(hasWords!);
            template.RemoveLinks(means!);
        }
        theUKS.ProcessSingleLine(definition);
    }

    #endregion
}

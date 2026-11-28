#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using UKS;
using UKS.Helpers;

namespace BrainSimulator.Modules;

// Reference: a noun phrase that describesReferent ("the black dog", "brown dogs", "the cat in the
// kitchen") means the thing its description picks out, not its class, so reading it says nothing
// about dogs in general. Recognition only records the description, as a descriptor Link
// [head -> oneDescribedAs/kindDescribedAs -> description]; nothing is searched or made until the
// winning reading is executed, so a losing reading leaves no trace.
//  - "the" with a singular (oneDescribedAs): an existing thing of the class that satisfies the
//    description, named or not (Fido, if Fido is the brown dog). More than one: ask which. None: a
//    new instance is made in a statement; a question has no answer.
//  - "a", or a plural (kindDescribedAs): a kind, an anonymous subclass with the description, found
//    or made.
//  - A description that the class itself satisfies tells nothing apart: if dogs are brown, "the
//    brown dog" is just the dog.
// Context that might settle which of several is meant is not considered yet.
public sealed partial class ModuleInterpret
{
    private sealed class UnclearReferentException(string message) : FormatException(message);

    private bool IsReferentDescriptor(Thought? meaning) =>
        meaning is Link { LinkType: Thought type } && theUKS.Labeled("referentDescriptor") is Thought descriptor && type.HasAncestor(descriptor);

    /// <summary>
    /// A describing phrase's meanings, as one descriptor: its head (the class, or a descriptor of an
    /// inner phrase) and the description about that head. Whether it picks out one thing or a kind
    /// depends on its words: a first word that isDefinite, and a head that is not plural, pick out one.
    /// </summary>
    private List<Thought> DescribedReferent(List<Thought> meanings, Thought match, SeqElement pattern, SeqElement start, SeqElement last)
    {
        Thought? head = meanings.FirstOrDefault(meaning => meaning is not Link || IsReferentDescriptor(meaning));
        Link? description = meanings.OfType<Link>().FirstOrDefault(meaning => !IsReferentDescriptor(meaning) && ReferenceEquals(meaning.From, head));
        if (head is null || description is null) return meanings;

        // The element the head comes from, and whether any word filling it is plural.
        Thought? headValue = TemplateOf(match).LinksTo.FirstOrDefault(link => link.LinkType == means && link.To is not Link)?.To;
        SeqElement? headElement = pattern;
        while (headElement is not null && !ReferenceEquals(headElement.VLU, headValue)) headElement = headElement.NXT;
        bool isPlural = false;
        for (SeqElement? word = start; word is not null && word != last.NXT; word = word.NXT)
            isPlural |= headElement is not null && Fills(word, headElement, match) &&
                ReferenceEquals(InflectionOf(word), plural);
        bool picksOne = start.VLU.HasProperty("isDefinite") && !isPlural;

        Thought type = theUKS.GetOrAddThought(picksOne ? "oneDescribedAs" : "kindDescribedAs", "referentDescriptor");
        var result = new List<Thought> { new Link(head, type, description) };
        result.AddRange(meanings.Where(meaning => !ReferenceEquals(meaning, head) && !ReferenceEquals(meaning, description)));
        return result;
    }

    /// <summary>
    /// The Thought a descriptor picks out, found or (in a statement) made. A descriptor nested in the
    /// head ("the black dog in the kitchen") adds its description; the outermost decides how it is found.
    /// </summary>
    /// <returns>The referent, or null in a question when nothing fits.</returns>
    /// <exception cref="UnclearReferentException">Several existing things fit.</exception>
    private Thought? Referent(Link descriptor, bool question)
    {
        if (Description(descriptor, question, out Thought? kindOf) is not { } described || kindOf is null) return null;
        var constraints = described.Select(part => (Type: Ordinary(part.Type), part.Target)).ToList();

        bool Satisfies(Thought thought)
        {
            List<Link> attributes = theUKS.GetAttributes(thought);
            return constraints.All(constraint => attributes.Any(attribute =>
                attribute.LinkType == constraint.Type && attribute.To == constraint.Target));
        }
        if (Satisfies(kindOf)) return kindOf;

        Thought anonymousClass = theUKS.GetOrAddThought("isAnonymousClass", "Property");
        if (descriptor.LinkType == theUKS.Labeled("kindDescribedAs"))
        {
            Thought? kind = kindOf.Children.FirstOrDefault(child => HasOwnProperty(child, anonymousClass) && Satisfies(child));
            return kind ?? (question ? null : Described(kindOf, anonymousClass, constraints));
        }

        var candidates = kindOf.Descendants.Where(thought => thought is not Link && !thought.Children.Any() &&
            !HasOwnProperty(thought, anonymousClass) && Satisfies(thought)).Distinct().ToList();
        if (candidates.Count > 1)
            throw new UnclearReferentException($"I can't tell which {Name(kindOf)} you mean: {string.Join(" or ", candidates.Select(Name))}.");
        if (candidates.Count == 1) return candidates[0];
        return question ? null : Described(kindOf, theUKS.GetOrAddThought("isInstance", "Property"), constraints);
    }

    /// <summary>
    /// What a descriptor says, outermost first: each description's relationship type as written (is.!)
    /// and its resolved target, and the class it is all about (kindOf).
    /// </summary>
    /// <returns>Null if a target cannot be resolved or there is no class.</returns>
    private List<(Thought Type, Thought Target)>? Description(Link descriptor, bool question, out Thought? kindOf)
    {
        var described = new List<(Thought Type, Thought Target)>();
        Thought? current = descriptor;
        kindOf = null;
        while (IsReferentDescriptor(current))
        {
            Link layer = (Link)current!;
            if (layer.To is not Link { LinkType: Thought type } description || Resolved(description.To, question) is not Thought target)
                return null;
            described.Add((type, target));
            current = layer.From;
        }
        if (current is null or Link) return null;
        kindOf = current;
        return described;
    }

    private Thought Ordinary(Thought type) => type.HasProperty(setRelationship) ? theUKS.GetActionRelationshipType(type) ?? type : type;

    // A statement that something is-a a described kind ("Rover is a small dog").
    private bool IsJoiningAKind(Link action) =>
        action.LinkType is Thought type && type.HasProperty(setRelationship) && Ordinary(type) == isA &&
        IsReferentDescriptor(action.To) && ((Link)action.To).LinkType == theUKS.Labeled("kindDescribedAs");

    /// <summary>
    /// "Rover is a small dog": Rover is-a dog and is small. No subclass is made for the kind; an agent
    /// may later gather members with attributes in common into one (the UKS CreateClasses agent).
    /// </summary>
    private List<Link> JoinKind(Thought member, Link kind)
    {
        var results = new List<Link>();
        if (Description(kind, question: false, out Thought? kindOf) is not { } described || kindOf is null) return results;
        results.AddRange(theUKS.ApplyTestOrSetAction(new Link(member, theUKS.GetOrAddThought("is-a.!"), kindOf)));
        foreach (var (type, target) in described)
            results.AddRange(type.HasProperty(setRelationship)
                ? theUKS.ApplyTestOrSetAction(new Link(member, type, target))
                : theUKS.AddStatement(member, type, target) is Link stated ? new[] { stated } : Array.Empty<Link>());
        return results;
    }

    // When the winning reading is itself a describing phrase (the input is "The hungry dog"), the thing
    // it describes is found or introduced, and its description is what was stated.
    private IEnumerable<Link> Introduced(Thought covering) =>
        TemplateOf(covering).HasProperty("describesReferent") &&
        covering.LinksTo.FirstOrDefault(link => link.LinkType == means && IsReferentDescriptor(link.To))?.To is Link descriptor &&
        Referent(descriptor, question: false) is Thought referent
            ? DescriptionOf(referent, descriptor).ToList() : Enumerable.Empty<Link>();

    // The stored relationships of a referent that its descriptor describes ([dog0->is->hungry]).
    private IEnumerable<Link> DescriptionOf(Thought referent, Link descriptor) =>
        (Description(descriptor, question: false, out _) ?? new())
            .Select(part => theUKS.GetLink(referent, Ordinary(part.Type), part.Target)).OfType<Link>();

    // A new unnamed child of a class (an instance, or an anonymous subclass) with the description.
    private Thought Described(Thought kindOf, Thought property, List<(Thought Type, Thought Target)> constraints)
    {
        Thought made = theUKS.GetOrAddThought(kindOf.Label + "*", kindOf);
        made.AddProperty(property);
        foreach (var (type, target) in constraints) theUKS.AddStatement(made, type, target);
        return made;
    }

    private static bool HasOwnProperty(Thought thought, Thought property) =>
        thought.LinksTo.Any(link => link.LinkType?.Label == "hasProperty" && link.To == property);

    // A Thought as the user would name it: the spelling of a word that means it, else its label.
    private string Name(Thought thought) =>
        thought.LinksFrom.FirstOrDefault(link => link.LinkType == means && link.From.HasAncestor(wordRoot))?.From is Thought word
            ? Spelling(word) : thought.Label;

    // A referent this module made has no word of its own; it is said by its class and description.
    private bool IsMadeReferent(Thought concept) =>
        !concept.LinksFrom.Any(link => link.LinkType == means) &&
        (HasOwnProperty(concept, theUKS.GetOrAddThought("isInstance", "Property")) ||
         HasOwnProperty(concept, theUKS.GetOrAddThought("isAnonymousClass", "Property")));

    /// <summary>
    /// Expresses a made referent with a describing phrase run in reverse: the head is its class, and
    /// the description one of its own facts (the first that a phrase can say, which is the one it was
    /// made with): "the black dog". An instance takes "the"; a kind takes "a".
    /// </summary>
    private IEnumerable<List<Thought>> ExpressReferent(Thought phraseClass, Thought concept)
    {
        if (concept.Parents.FirstOrDefault() is not Thought kindOf) yield break;
        Thought? articleChoice = theUKS.Labeled(HasOwnProperty(concept, theUKS.GetOrAddThought("isInstance", "Property")) ? "the" : "a");
        // Phrases with an article first: "the black dog", "a black dog", before "black dog".
        var phrases = phraseClass.Children.Where(phrase => phrase.HasProperty("describesReferent") &&
            phrase.GetTargetOfFirstLinkOfType(hasWords) is SeqElement pattern && !ContainsItsClass(pattern, phraseClass))
            .OrderBy(phrase => !HasArticleElement((SeqElement)phrase.GetTargetOfFirstLinkOfType(hasWords)));
        foreach (Thought phrase in phrases)
        {
            var pattern = (SeqElement)phrase.GetTargetOfFirstLinkOfType(hasWords);
            Thought? slot = phrase.LinksTo.FirstOrDefault(link => link.LinkType == means && link.To is not Link)?.To;
            Link? description = phrase.LinksTo.Where(link => link.LinkType == means).Select(link => link.To).OfType<Link>()
                .FirstOrDefault(meaning => ReferenceEquals(meaning.From, slot));
            if (slot is null || description is null) continue;
            foreach (Link fact in concept.LinksTo.Where(link => link.LinkType != isA && link.LinkType?.Label != "hasProperty"))
            {
                var bindings = new Dictionary<Thought, Thought>(ReferenceEqualityComparer.Instance);
                if (!Unify(description, new Link(kindOf, fact.LinkType, fact.To), pattern, bindings, isType: false)) continue;
                Thought? Bound(Thought value) => bindings.TryGetValue(value, out Thought? bound) ? bound :
                    articleChoice is not null && IsArticleElement(value) ? articleChoice : null;
                foreach (List<Thought> words in ExpressTemplate(pattern, phrase, Bound, allowChoices: false))
                    yield return words;
            }
        }
    }
}

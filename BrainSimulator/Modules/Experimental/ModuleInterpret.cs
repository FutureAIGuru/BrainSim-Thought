#nullable enable

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Pluralize.NET;
using UKS;
using KnowledgeStore = UKS.UKS;

namespace BrainSimulator.Modules;

public sealed class ModuleInterpret : ModuleBase
{
    public override void Fire() => Init();

    public override void Initialize() => GetUKS();

    public Interpretation Interpret(string text)
    {
        GetUKS();
        Thought? classify = theUKS.Labeled("interpret:classify");
        Thought? writeMarker = theUKS.Labeled("interpretWrite");
        if (writeMarker is null || classify?.LinksTo.Any(link => link.LinkType == theUKS.Labeled("means") &&
            link.To is Link output && output.LinkType?.HasAncestor(writeMarker) == true) != true)
        {
            // Reload the supplied meanings if an older build installed action
            // wrappers or parsed a nested MEANS target as an ordinary Thought.
            if (classify is not null && theUKS.Labeled("means") is Thought means)
                foreach (var category in new[] { "NounPhrase", "VerbPhrase", "Assertion", "Query", "Command" })
                    foreach (var template in theUKS.Labeled(category)?.Descendants
                        .Where(thought => thought.Label.StartsWith("interpret:", StringComparison.OrdinalIgnoreCase))
                        .ToArray() ?? Array.Empty<Thought>())
                        template.RemoveLinks(means);
            if (classify is null) theUKS.ImportTextFile(ContentPath("BasicWords.txt"));
            theUKS.ImportTextFile(ContentPath("InterpretLanguage.txt"));
        }
        return InterpretEnglish(theUKS, text);
    }

    public IReadOnlyList<Link> Execute(Interpretation interpretation)
    {
        GetUKS();
        return ExecuteInterpretation(theUKS, interpretation);
    }

    public void LoadExamples()
    {
        GetUKS();
        theUKS.ImportTextFile(ContentPath("InterpretExamples.txt"));
    }

    private static string ContentPath(string name) => Path.Combine(AppContext.BaseDirectory, "UKSContent", name);

    public enum InterpretationIntent { Assertion, Query, Command }

    public sealed record Interpretation(
        InterpretationIntent? Intent, IReadOnlyList<Link> Relationships,
        IReadOnlyList<string> Trace, string? Diagnostic);

    private static readonly char[] LanguagePunctuation = ".,;:!?\"'()[]{}".ToCharArray();
    private static readonly char[] LanguageWordSeparators = { ' ', '\t', '\r', '\n' };

    /// <summary>Extracts the surface words used by the UKS language modules.</summary>
    private static IEnumerable<string> TokenizeLanguageInput(string text) =>
        text.Split(LanguageWordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim(LanguagePunctuation).ToLowerInvariant())
            .Where(token => token.Length > 0);

    // A reading only lives during interpretation. Its symbol is a stored word
    // or template Thought, so higher-level templates can match its ancestry.
    private sealed record Reading(
        Thought Symbol, Thought? Meaning, IReadOnlyList<Link> Relationships,
        IReadOnlySet<Thought> CaptureSlots, IReadOnlyList<string> Trace);

    private static bool AcceptsWildcard(Thought slot, Thought value)
    {
        var store = slot.Store;
        // The interpreter uses singleton slots. The legacy Wildcard category
        // groups the slots themselves and does not constrain their fillers.
        var constraints = slot.AncestorsWithSelf.Where(item => item.HasProperty("isWildcard"))
            .SelectMany(item => item.Parents)
            .Where(parent => !parent.HasProperty("isWildcard") &&
                !ReferenceEquals(parent, store.Labeled("Wildcard")) &&
                !ReferenceEquals(parent, store.Labeled("Property")) &&
                !ReferenceEquals(parent, store.Labeled("Thought")) &&
                !ReferenceEquals(parent, store.Labeled("Unknown"))).ToArray();
        return constraints.Length == 0 || constraints.Any(value.HasAncestor);
    }

    /// <summary>Recognizes hasWords templates and binds their means Relationships without executing them.</summary>
    internal static Interpretation InterpretEnglish(KnowledgeStore store, string text)
    {
        var words = TokenizeLanguageInput(text ?? "").ToArray();
        var trace = new List<string>();
        Interpretation Failure(string message) => new(null, Array.Empty<Link>(), trace, message);
        if (words.Length == 0) return Failure("Enter an English sentence.");
        if (words.Length > 24) return Failure("This experiment supports up to 24 words per input.");
        if (store.Labeled("hasWords") is not Thought hasWords || store.Labeled("means") is not Thought means)
            return Failure("Load the language template file first.");

        SequenceView[] Templates(string category) => store.Labeled(category) is Thought root
            ? store.GetSequenceViews(root.Descendants).Where(view =>
                view.LinkType == hasWords && view.Elements.Count > 0 &&
                view.Owner.LinksTo.Any(link => link.LinkType == means)).ToArray()
            : Array.Empty<SequenceView>();

        var phraseTemplates = Templates("NounPhrase").Concat(Templates("VerbPhrase")).ToArray();
        var sentences = new[]
        {
            (Intent: InterpretationIntent.Query, Templates: Templates("Query")),
            (Intent: InterpretationIntent.Command, Templates: Templates("Command")),
            (Intent: InterpretationIntent.Assertion, Templates: Templates("Assertion"))
        };
        var noCaptures = new HashSet<Thought>();
        var chart = new List<Reading>[words.Length, words.Length + 1];
        var pluralizer = new Pluralizer();
        var unknown = new List<string>();
        for (int i = 0; i < words.Length; i++)
        {
            string surface = words[i];
            Thought? word = store.Labeled("w:" + surface);
            // An explicit surface form takes precedence. Only use singularization
            // to fall back to a known noun, so verb and modifier forms stay data-driven.
            if (word is null && store.Labeled("w:" + pluralizer.Singularize(surface)) is Thought noun &&
                noun.HasAncestor("noun")) word = noun;
            if (word is null)
            {
                unknown.Add(surface);
                trace.Add($"Unknown word  {surface}");
                continue;
            }
            var meaning = word.GetTargetOfHighestWeightLinkOfType(means.Label);
            trace.Add(meaning is null ? $"Word  {surface} → {word.Label}" :
                $"Word  {surface} → {word.Label} → {meaning.Label}");
            chart[i, i + 1] = new() { new Reading(word, meaning, Array.Empty<Link>(), noCaptures, Array.Empty<string>()) };
        }
        if (unknown.Count > 0) return Failure($"Unknown words: {string.Join(", ", unknown.Distinct())}.");

        IEnumerable<Reading[]> Match(SequenceView template, int start, int end, bool phrase)
        {
            var elements = template.Elements.ToArray();
            var input = new Reading[elements.Length];
            IEnumerable<Reading[]> Align(int slot, int position)
            {
                if (slot == input.Length)
                {
                    if (position == end) yield return input.ToArray();
                    yield break;
                }
                Thought element = elements[slot];
                bool wildcard = element.HasProperty("isWildcard");
                int firstUse = wildcard ? Array.IndexOf(elements, element, 0, slot) : -1;
                int lastCut = end - (input.Length - slot - 1);
                for (int cut = position + 1; cut <= lastCut; cut++)
                {
                    if (chart[position, cut] is not { } readings) continue;
                    foreach (var reading in readings)
                    {
                        // Unary phrase templates consume words. Multi-part templates
                        // consume shorter spans, so reductions cannot loop.
                        if (phrase && input.Length == 1 && reading.Trace.Count != 0) continue;
                        if (wildcard ? !AcceptsWildcard(element, reading.Symbol) : !ReferenceEquals(element, reading.Symbol))
                            continue;
                        if (firstUse >= 0 && (!ReferenceEquals(input[firstUse].Symbol, reading.Symbol) ||
                            !ReferenceEquals(input[firstUse].Meaning, reading.Meaning))) continue;
                        input[slot] = reading;
                        foreach (var match in Align(slot + 1, cut)) yield return match;
                    }
                }
            }
            return Align(0, start);
        }

        Reading Compose(SequenceView template, Reading[] input, string description)
        {
            var bindings = new Dictionary<Thought, Thought>();
            for (int i = 0; i < input.Length; i++)
                if (input[i].Meaning is { } meaning && template.Elements[i].HasProperty("isWildcard"))
                    bindings.TryAdd(template.Elements[i], meaning);
            Thought? Resolve(Thought? thought) => thought is null ? null : bindings.GetValueOrDefault(thought, thought);
            Link Bind(Link prototype, Thought? source = null)
            {
                var type = Resolve(prototype.LinkType);
                if (type is not null && (type.HasProperty("setRelationship") || type.HasProperty("isPossible")))
                    type = store.GetActionRelationshipType(type);
                return new Link(store) { From = source ?? Resolve(prototype.From), LinkType = type, To = Resolve(prototype.To) };
            }

            var meanings = template.Owner.LinksTo.Where(link => link.LinkType == means).Select(link => link.To).ToArray();
            var relationships = meanings.OfType<Link>().Select(link => Bind(link)).ToList();
            var classification = relationships.FirstOrDefault(link => link.LinkType == store.Labeled("is-a"));
            foreach (var child in input)
            {
                foreach (var detail in child.Relationships)
                {
                    // In "Fido is a brown dog", the description of the class
                    // phrase constrains Fido, the instance being classified.
                    var source = classification is not null && child.Meaning is not null &&
                        ReferenceEquals(classification.To, child.Meaning) && ReferenceEquals(detail.From, child.Meaning)
                        ? classification.From : null;
                    relationships.Add(Bind(detail, source));
                }
            }
            var value = meanings.FirstOrDefault(thought => thought is not null && thought is not Link);
            // Only slots in the chosen parse can be unfilled captures. Another
            // learned template may use an answer wildcard as an input slot.
            var captures = input.SelectMany(item => item.CaptureSlots)
                .Concat(template.Elements.Where(thought => thought.HasProperty("isWildcard"))).ToHashSet();
            return new Reading(template.Owner, Resolve(value), relationships, captures,
                input.SelectMany(item => item.Trace).Append(description).ToArray());
        }

        for (int length = 1; length <= words.Length; length++)
            for (int start = 0; start + length <= words.Length; start++)
            {
                int end = start + length;
                var readings = chart[start, end] ??= new();
                foreach (var template in phraseTemplates)
                {
                    var matches = Match(template, start, end, phrase: true)
                        .Select(input => Compose(template, input,
                            $"Phrase  {string.Join(" ", words[start..end])} → {template.Owner.Label}")).ToArray();
                    readings.AddRange(matches);
                }
            }

        string? incomplete = null;
        IReadOnlyList<string>? incompleteTrace = null;
        bool question = text?.TrimEnd().EndsWith('?') == true;
        foreach (var group in sentences)
        {
            if (question && group.Intent != InterpretationIntent.Query) continue;
            foreach (var template in group.Templates.OrderByDescending(template =>
                template.Elements.Count(item => !item.HasProperty("isWildcard"))))
            foreach (var input in Match(template, 0, words.Length, phrase: false))
            {
                var reading = Compose(template, input, $"Template  {template.Owner.Label}");
                string? Unresolved(Thought? thought, string role)
                {
                    if (thought is null) return $"{role} has no meaning";
                    if (reading.CaptureSlots.Contains(thought) ||
                        group.Intent == InterpretationIntent.Assertion && thought.HasProperty("isWildcard"))
                        return $"{role} {thought.Label}";
                    return null;
                }
                var missing = reading.Relationships.SelectMany(link => new[]
                    {
                        Unresolved(link.From, "Source"), Unresolved(link.LinkType, "Relationship type"),
                        Unresolved(link.To, "Target")
                    }).OfType<string>().Distinct().ToArray();
                if (reading.Relationships.Count == 0 || missing.Length != 0)
                {
                    if (incomplete is null)
                    {
                        incomplete = reading.Relationships.Count == 0
                            ? $"Template {template.Owner.Label} matched but produced no Relationships."
                            : $"Template {template.Owner.Label} matched with unresolved slots. {string.Join(", ", missing)}.";
                        incompleteTrace = reading.Trace;
                    }
                    continue;
                }
                trace.AddRange(reading.Trace);
                return new Interpretation(group.Intent, reading.Relationships
                    .DistinctBy(link => (link.From, link.LinkType, link.To)).ToArray(), trace, null);
            }
        }
        if (incomplete is not null)
        {
            trace.AddRange(incompleteTrace!);
            return Failure(incomplete);
        }
        var longest = Enumerable.Range(0, words.Length)
            .SelectMany(start => Enumerable.Range(start + 1, words.Length - start)
                .SelectMany(end => chart[start, end].Select(reading => (Length: end - start, reading.Trace))))
            .Where(match => match.Trace.Count > 0).OrderByDescending(match => match.Length).FirstOrDefault();
        if (longest.Trace is not null) trace.AddRange(longest.Trace);
        return Failure("Known words were recognized, but no sentence template matched the full input.");
    }

    /// <summary>Applies assertions or joins UKS query results on shared wildcard Thoughts.</summary>
    internal static IReadOnlyList<Link> ExecuteInterpretation(KnowledgeStore store, Interpretation interpretation)
    {
        if (interpretation.Intent is null || interpretation.Diagnostic is not null) return Array.Empty<Link>();
        if (interpretation.Relationships.Any(link => !ReferenceEquals(link.Store, store) ||
            new[] { link.From, link.LinkType, link.To }.Any(thought =>
                thought is null || !ReferenceEquals(store.Labeled(thought.Label), thought))))
            throw new ArgumentException("The interpretation references Thoughts that are unavailable in the current UKS.",
                nameof(interpretation));
        if (interpretation.Intent == InterpretationIntent.Assertion)
            return interpretation.Relationships.Select(link => store.AddStatement(link.From!, link.LinkType!, link.To))
                .OfType<Link>().ToArray();

        // SearchForRelationships supplies each result set. The join preserves the
        // identity of a wildcard reused by more than one Relationship.
        var rows = new List<(Dictionary<Thought, Thought> Bindings, List<Link> Links)> { (new(), new()) };
        foreach (var pattern in interpretation.Relationships)
        {
            var next = new List<(Dictionary<Thought, Thought>, List<Link>)>();
            foreach (var row in rows)
            {
                Thought Bound(Thought thought) => row.Bindings.GetValueOrDefault(thought, thought);
                var query = new Link(Bound(pattern.From!), Bound(pattern.LinkType!), Bound(pattern.To!));
                foreach (var found in store.SearchForRelationships(query))
                {
                    var values = new Dictionary<Thought, Thought>(row.Bindings);
                    bool MatchPart(Thought slot, Thought? value, bool type = false)
                    {
                        if (value is null || value.HasProperty("isWildcard")) return false;
                        if (!slot.HasProperty("isWildcard"))
                            return type ? value.HasAncestor(slot) &&
                                value.HasProperty("isNegative") == slot.HasProperty("isNegative") : value == slot;
                        if (!AcceptsWildcard(slot, value)) return false;
                        if (values.TryGetValue(slot, out var previous)) return previous == value;
                        values.Add(slot, value);
                        return true;
                    }
                    if (found.LinkType?.HasProperty("isPossible") == true || found.LinkType?.HasProperty("setRelationship") == true)
                        continue;
                    if (MatchPart(pattern.From!, found.From) && MatchPart(pattern.LinkType!, found.LinkType, type: true) &&
                        MatchPart(pattern.To!, found.To))
                        next.Add((values, new List<Link>(row.Links) { found }));
                }
            }
            rows = next;
        }
        return rows.SelectMany(row => row.Links).DistinctBy(link => (link.From, link.LinkType, link.To)).ToArray();
    }
}

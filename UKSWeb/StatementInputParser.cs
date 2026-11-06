using UKS;
using KnowledgeStore = UKS.UKS;

namespace UKSWeb;

/// <summary>Interprets the three demo fields using the UKS statement-dialog conventions.</summary>
internal static class StatementInputParser
{
    public static Link Add(KnowledgeStore store, string source, string relationshipType, string target)
    {
        // Saved XML can predate these definitions. Match the desktop dialog's
        // bootstrap, then let inherited properties select conditional behavior.
        store.GetOrAddThought("?", "Property")!.AddLink("hasProperty",
            store.GetOrAddThought("isPossible", "Property"));
        store.GetOrAddThought("IF", "ClauseType")!.AddLink("hasProperty",
            store.GetOrAddThought("isConditional", "Property"));
        store.GetOrAddThought("AND", "ClauseType")!.AddLink("hasProperty",
            store.GetOrAddThought("isConjunction", "Property"));

        Thought type = ParseField(store, relationshipType, attributesFollow: true);
        bool conditional = type.HasProperty("isConditional") || type.HasProperty("isConjunction");
        Thought from = ParseField(store, source, attributesFollow: false, asPattern: conditional);
        Thought to = ParseField(store, target, attributesFollow: false, asPattern: conditional);
        if (conditional && (from is not Link || to is not Link))
            throw new FormatException("IF and AND require Relationships or condition expressions in both Source and Target.");

        return store.AddStatement(from, type, to)
            ?? throw new FormatException("UKS could not add the relationship.");
    }

    private static Thought ParseField(KnowledgeStore store, string input, bool attributesFollow, bool asPattern = false)
    {
        string text = NormalizeHave(input.Trim(), attributesFollow);
        if (text.Length == 0) throw new FormatException("Enter a source, relationship type, and target.");
        if (text.StartsWith('[') && !text.EndsWith(']'))
            throw new FormatException("A bracketed relationship must end with ].");

        // The existing parser handles brackets, arrows, nesting, AND, and the
        // possible relationship types needed for conditional operands.
        if (asPattern)
        {
            Thought? pattern = store.ProcessSingleLineWithNesting(text, asPattern: true);
            if (pattern is not null) return pattern;
        }
        // Unquoted nouns must be normalized before lookup, even when a plural
        // label already exists. Quoted menu identities are handled below.
        else if (attributesFollow && store.Labeled(text) is Thought existing) return existing;

        if (text.StartsWith('['))
        {
            return store.ProcessSingleLineWithNesting(text) as Link
                ?? throw new FormatException($"Could not parse relationship: {text}");
        }
        if (text.StartsWith('"'))
        {
            string label = System.Text.Json.JsonSerializer.Deserialize<string>(text)
                ?? throw new FormatException("Enter a Thought label between the quotes.");
            return store.GetOrAddThought(label) ?? throw new FormatException("Enter a nonempty Thought label.");
        }

        // Nouns take preceding attributes (big dog -> dog.big); relationship
        // types take following attributes (has 3 -> has.3). Preserve other verb forms.
        return store.CreateThoughtFromMultipleAttributes(text, attributesFollow, singularize: !attributesFollow)
            ?? throw new FormatException($"Could not parse Thought: {text}");
    }

    private static string NormalizeHave(string text, bool relationshipType)
    {
        // "Have" and "has" use the same relationship identity. Normalize only
        // type positions, including nested triples, never noun labels or quotes.
        var positions = new List<int>();
        void CheckType(int start)
        {
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
            if (!text.AsSpan(start).StartsWith("have", StringComparison.OrdinalIgnoreCase)) return;
            int end = start + 4;
            if (end == text.Length || char.IsWhiteSpace(text[end]) || text[end] is '.' or ']' or '→' ||
                text.AsSpan(end).StartsWith("->"))
                positions.Add(start);
        }

        if (relationshipType) CheckType(0);
        var arrows = new Stack<int>();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (quoted && text[i] == '\\') { i++; continue; }
            if (text[i] == '"') { quoted = !quoted; continue; }
            if (quoted) continue;
            if (text[i] == '[') { arrows.Push(0); continue; }
            if (text[i] == ']') { if (arrows.Count > 0) arrows.Pop(); continue; }
            int arrowLength = text[i] == '→' ? 1 : text.AsSpan(i).StartsWith("->") ? 2 : 0;
            if (arrowLength == 0 || arrows.Count == 0) continue;
            int count = arrows.Pop() + 1;
            arrows.Push(count);
            if (count == 1) CheckType(i + arrowLength);
            i += arrowLength - 1;
        }

        var result = new System.Text.StringBuilder(text);
        for (int i = positions.Count - 1; i >= 0; i--)
            result.Remove(positions[i], 4).Insert(positions[i], "has");
        return result.ToString();
    }

    public static string Format(Thought? thought, int depth = 0)
    {
        if (thought is not Link link || depth >= 16) return thought?.Label ?? "(no target)";
        string from = Format(link.From, depth + 1);
        string type = Format(link.LinkType, depth + 1);
        string to = Format(link.To, depth + 1);
        return link.LinkType?.HasProperty("isConjunction") == true
            ? $"[{from} {type} {to}]" : $"[{from} → {type} → {to}]";
    }
}

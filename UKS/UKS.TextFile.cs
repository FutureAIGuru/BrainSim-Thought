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
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace UKS;

public partial class UKS
{
    /// <summary>
    /// Export a neighborhood starting from <paramref name="root"/> to the bracketed txt file format.
    /// Emits facts as [S,R,O] (or [S,R,O,N] when R is a numeric specialization like "has.4").
    /// Optionally emits simple clause pairs if Thought exposes a Clauses collection.
    /// </summary>
    /// <param name="root">Label of the starting thought to export.</param>
    /// <param name="path">Destination file path for the exported text.</param>
    /// <param name="maxDepth">Optional maximum traversal depth (currently unused).</param>
    public void ExportTextFile(string root, string path, int maxDepth = 12)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Start label is required.", nameof(root));
        Thought? Root = theUKS.Labeled(root);
        if (Root is null) return;

        HashSet<string> alreadyWritten = new();
        try
        {
            using (var writer = new StreamWriter(path))
            {
                if (writer is null) throw new ArgumentNullException(nameof(writer));
                List<Thought> thoughtsToWrite = new();
                HashSet<Thought> collectedThoughts = new(ReferenceEqualityComparer.Instance);

                void Collect(Thought? thought)
                {
                    if (thought is not null && collectedThoughts.Add(thought))
                        thoughtsToWrite.Add(thought);
                }

                Collect(Root);
                foreach (Thought thought in Root.EnumerateSubThoughts())
                {
                    Collect(thought);
                    if (thought is Link link)
                    {
                        Collect(link.From);
                        Collect(link.LinkType);
                        Collect(link.To);
                    }
                }

                // Include the complete outgoing structure of referenced Thoughts, including
                // source-only paths whose values are not reached by the root's child traversal.
                for (int i = 0; i < thoughtsToWrite.Count; i++)
                    foreach (Link dependency in thoughtsToWrite[i].LinksTo)
                    {
                        Collect(dependency);
                        Collect(dependency.From);
                        Collect(dependency.LinkType);
                        Collect(dependency.To);
                    }

                foreach (Thought t in thoughtsToWrite)
                {
                    string s = FormatThought(t) + " " + t.Weight.ToString("F2") +
                        " " + t.maxWeight.ToString("F2") + " " + t.isPlastic;
                    if (!alreadyWritten.Contains(s))
                    {
                        writer.WriteLine(s);
                        alreadyWritten.Add(s);
                    }
                }
                writer.Flush();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ExportTextFile failed: {ex}");
            throw;
        }
        RemoveTempLabels(Root);
    }

    private void RemoveTempLabels(Thought Root)
    {
        if (Root is null) return;
        //remove unnecessary "unl_..."  labels
        foreach (var t in Root.EnumerateSubThoughts())
        {
            if (t.Label.StartsWith("unl_"))
                t.Label = "";
        }
    }

    string EnsureLabel(Thought t)
    {
        if (string.IsNullOrWhiteSpace(t.Label))
            // Put the GUID into the label only when it's unlabeled
            t.Label = $"unl_{Guid.NewGuid().ToString("N")[..8]}";
        return t.Label.Any(char.IsWhiteSpace) || t.Label.IndexOfAny(new[] { '[', ']', '"' }) >= 0 ||
            t.Label.Contains("->") || t.Label.Contains('→')
            ? System.Text.Json.JsonSerializer.Serialize(t.Label) : t.Label;
    }

    string FormatThought(Thought t)
    {
        string retVal = EnsureLabel(t).PadRight(15);
        if (t.V is not null)
            retVal += " V: " + t.V.ToString();
        if (t is Link r)
        {
            if (r.From is Link r1) EnsureLabel(r1);
            if (r.To is Link r2) EnsureLabel(r2);
            retVal += "[";
            if (r.From is not null)
                retVal += EnsureLabel(r.From);
            if (r.LinkType is not null)
                retVal += ((retVal == "") ? "" : "->") + EnsureLabel(r.LinkType);
            if (r.To is not null)
                retVal += ((retVal == "") ? "" : "->") + EnsureLabel(r.To);
            retVal += "]";
        }
        return retVal;
    }

    // int or decimal, optional leading minus
    private static readonly Regex NumericRegex = new(@"^-?\d+(\.\d+)?$", RegexOptions.Compiled);

    /// <summary>
    /// Imports UKS content from a bracketed text file.
    /// </summary>
    /// <param name="filePath">Path of the text file to import.</param>
    public void ImportTextFile(string filePath)
    {
        if (filePath is null) throw new ArgumentNullException(nameof(filePath));
        var lines = File.ReadAllLines(filePath);

        lock (this)
        {
            bool wasSuppressingFiring = SuppressFiring;
            SuppressFiring = true;
            try
            {
                // The 3-pass is needed to handle forward label references
                // FIRST PASS: Find labels which define links. Standalone Thought metadata
                // lines must remain ordinary Thoughts rather than unwired Link placeholders.
                List<string> definedLinkLabels = new();
                foreach (var line in lines)
                {
                    string code = StripEolComment(line);
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    var tokens = TokenizeTopLevel(code);
                    if (tokens.Count < 2 || !tokens[1].TrimStart().StartsWith("[")) continue;
                    string label = tokens[0].Trim();
                    if (!string.IsNullOrEmpty(label))
                        definedLinkLabels.Add(label);
                }

                // SECOND PASS: Find all referenced labels (in bracket parts)
                HashSet<string> referencedLabels = new();
                foreach (var line in lines)
                {
                    string code = StripEolComment(line);
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    var tokens = TokenizeTopLevel(code);
                    if (tokens.Count < 2 || !tokens[1].TrimStart().StartsWith("[")) continue;

                    var stmt = ParseBracketStmt(tokens[1], 0);
                    foreach (var part in stmt)
                    {
                        string trimmed = part.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            referencedLabels.Add(trimmed);
                    }
                }

                // Keep only labels that are referenced
                List<string> labelsToPreAllocate = definedLinkLabels
                    .Where(label => referencedLabels.Contains(label))
                    .ToList();

                // Pre-allocate unwired Link placeholders only for forward references (label not yet defined)
                foreach (var label in labelsToPreAllocate)
                {
                    Thought? existing = Labeled(label);
                    if (existing is not null)
                        continue;

                    Link placeholder = new Link();
                    placeholder.Label = label;
                    AtomicThoughts.Add(placeholder);
                }

                // THIRD PASS: Actually process the lines
                foreach (var line in lines)
                {
                    ProcessSingleLine(line);  //without the possibility of compound or sequence statements, this is straightforward
                }

                // Corpus instructions remain exemplar data when a saved network is imported.
                foreach (Link operation in AtomicThoughts.SelectMany(thought => thought.LinksTo)
                    .Where(link => ReferenceEquals(link.LinkType, Labeled("demonstrates")))
                    .Select(link => link.To).OfType<Link>().ToList())
                {
                    if (operation.From is null || operation.To is null || operation.LinkType is null ||
                        (!operation.LinkType.HasProperty("setRelationship") && !operation.LinkType.HasProperty("isPossible"))) continue;
                    lock (operation.From.LinksToWriteable)
                        operation.From.LinksToWriteable.RemoveAll(link => ReferenceEquals(link, operation));
                    lock (operation.To.LinksFromWriteable)
                        operation.To.LinksFromWriteable.RemoveAll(link => ReferenceEquals(link, operation));
                    lock (operation.LinkType.LinksAsTypeWriteable)
                        operation.LinkType.LinksAsTypeWriteable.RemoveAll(link => ReferenceEquals(link, operation));
                }

                // Remove unnecessary "unl_..." labels
                Thought? thoughtRoot = Labeled("Thought");
                if (thoughtRoot is not null)
                {
                    foreach (var t in thoughtRoot.EnumerateSubThoughts())
                    {
                        if (t.Label.StartsWith("unl_"))
                            t.Label = "";
                    }
                }
            }
            finally
            {
                SuppressFiring = wasSuppressingFiring;
            }
        }
    }

    public Thought? ProcessSingleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        string code = StripEolComment(line);
        if (string.IsNullOrWhiteSpace(code)) return null;

        code = code.Trim().Replace("→", "->");

        var tokens = TokenizeTopLevel(code);
        if (tokens.Count < 2) return null;

        if (!tokens[1].TrimStart().StartsWith("["))
        {
            Thought? thought = Labeled(tokens[0]) ?? GetOrAddThought(tokens[0]);
            ApplyTrailingPersistenceMetadata(thought, tokens, 1);
            Thought? thoughtRetVal = thought;
            return thoughtRetVal;
        }

        var stmt = ParseBracketStmt(tokens[1], 0);
        if (stmt.Count < 2) return null;

        if (stmt.Any(part => part.TrimStart().StartsWith("[")) ||
            Labeled(stmt[1])?.HasProperty("isConditional") == true ||
            Labeled(stmt[1])?.HasProperty("isConjunction") == true)
        {
            return AddLinkStmtWithNesting(
                tokens[0], stmt,
                tokens.Count > 2 ? tokens[2] : null,
                tokens.Count > 3 ? tokens[3] : null,
                tokens.Count > 4 ? tokens[4] : null);
        }
        //return AddLinkStmt(tokens[0], stmt, tokens.Count > 2 ? tokens[2] : null);
        Thought? linkRetVal = AddLinkStmt(
            tokens[0], stmt,
            tokens.Count > 2 ? tokens[2] : null,
            tokens.Count > 3 ? tokens[3] : null,
            tokens.Count > 4 ? tokens[4] : null);
        return linkRetVal;
    }
    // Adds a link, handling nested links in src, type, or target
    private Thought? AddLinkStmt(
        string label,
        List<string> linkParts,
        string? sWeight,
        string? sMaxWeight,
        string? sIsPlastic)
    {
        if (linkParts.Count < 2) return null;

        // Get value strings (used in config - OBSOLETE)
        string value = "";
        if (linkParts[0].Contains("_V:"))
        {
            int index = linkParts[0].IndexOf("_V:");
            value = linkParts[0][(index + 3)..];
            linkParts[0] = linkParts[0][..index];
            Thought? t1 = Labeled(linkParts[0]);
            t1?.Delete();
        }

        // Process 'from'
        Thought? from = ProcessThoughtOrLink(linkParts[0]) ?? GetOrAddThought(linkParts[0]);

        // Process 'linkType'
        Thought? linkType = ProcessThoughtOrLink(linkParts[1]) ?? GetOrAddThought(linkParts[1]);

        // Process 'to'
        //this might be a sequence
        Thought? to = ProcessThoughtOrLink(linkParts[2]) ?? CreateThoughtFromMultipleAttributes(linkParts[2], false, false);
        //Thought to = GetOrAddThought(linkParts[2]);

        if (from is null || linkType is null) return null;

        Link? r = AddStatement(from, linkType, to, label);
        if (r is null) return null;
        //Link r = from.AddLink(linkType, to);
        if (!string.IsNullOrEmpty(label))
            r.Label = label;

        if (value != "")
            r.From!.V = value;
        if (label != "" && !label.StartsWith("unl_"))
        {
            r.Label = label.Trim();
            if (!AtomicThoughts.Contains(r))
                AtomicThoughts.Add(r);
        }
        if (linkType.Label == "VLU")
        {
            // This must be a sequence element, promote it to one
            PromoteToSeqElement(from);
        }
        ApplyPersistenceMetadata(r, sWeight, sMaxWeight, sIsPlastic);
        return r;
    }


    /// <summary>
    /// Processes a single line of UKS text format and creates the corresponding thought/link.
    /// Handles nested links where src, type, or target can themselves be links.
    /// </summary>
    /// <param name="line">The line to process in UKS text format (e.g., "label[from->linkType->to] weight")</param>
    /// <returns>The created Thought/Link, or null if parsing failed.</returns>
    public Thought? ProcessSingleLineWithNesting(string line, bool asPattern = false, bool asActionPrototype = false)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        string code = StripEolComment(line);
        if (string.IsNullOrWhiteSpace(code)) return null;

        code = code.Trim().Replace("→", "->");

        if (asPattern) return ProcessThoughtOrLink(code, true);

        var tokens = TokenizeTopLevel(code);
        if (tokens.Count < 2) return null;

        if (!tokens[1].TrimStart().StartsWith("["))
        {
            Thought? thought = Labeled(tokens[0]) ?? GetOrAddThought(tokens[0]);
            ApplyTrailingPersistenceMetadata(thought, tokens, 1);
            Thought? thoughtRetVal = thought;
            return thoughtRetVal;
        }

        var stmt = ParseBracketStmt(tokens[1], 0);
        if (stmt.Count < 2) return null;

        Thought? linkRetVal = AddLinkStmtWithNesting(
            tokens[0], stmt,
            tokens.Count > 2 ? tokens[2] : null,
            tokens.Count > 3 ? tokens[3] : null,
            tokens.Count > 4 ? tokens[4] : null);
        if (asActionPrototype && linkRetVal is Link parsed)
        {
            Link? operation = parsed.LinkType?.HasProperty("setRelationship") == true ||
                parsed.LinkType?.HasProperty("isPossible") == true ? parsed : parsed.From as Link;
            if (operation?.From is not null && operation.To is not null && operation.LinkType is not null)
            {
                lock (operation.From.LinksToWriteable)
                    operation.From.LinksToWriteable.RemoveAll(link => ReferenceEquals(link, operation));
                lock (operation.To.LinksFromWriteable)
                    operation.To.LinksFromWriteable.RemoveAll(link => ReferenceEquals(link, operation));
                lock (operation.LinkType.LinksAsTypeWriteable)
                    operation.LinkType.LinksAsTypeWriteable.RemoveAll(link => ReferenceEquals(link, operation));
            }
        }
        return linkRetVal;
    }

    // Adds a link, handling nested links in src, type, or target
    private Thought? AddLinkStmtWithNesting(
        string label,
        List<string> linkParts,
        string? sWeight,
        string? sMaxWeight,
        string? sIsPlastic, bool asPattern = false)
    {
        if (linkParts.Count < 2) return null;

        // Get value strings (used in config - OBSOLETE)
        string value = "";
        if (linkParts[0].Contains("_V:"))
        {
            int index = linkParts[0].IndexOf("_V:");
            value = linkParts[0][(index + 3)..];
            linkParts[0] = linkParts[0][..index];
            Thought? t1 = Labeled(linkParts[0]);
            t1?.Delete();
        }

        Thought? linkType = ProcessThoughtOrLink(linkParts[1]);
        if (linkType is null) linkType = CreateThoughtFromMultipleAttributes(linkParts[1], true, false);
        if (linkType is null) return null;
        bool structural = linkType.HasProperty("isConditional") || linkType.HasProperty("isConjunction");

        // Conditional and conjunction operands are patterns, never new assertions.
        Thought? from = ProcessThoughtOrLink(linkParts[0], asPattern || structural);
        if (from is null) from = CreateThoughtFromMultipleAttributes(linkParts[0], false);

        // Process 'to' - may be a nested link
        Thought? to = null;
        if (linkParts.Count > 2)
        {
            to = ProcessThoughtOrLink(linkParts[2], asPattern || structural);
            if (to is null && linkParts[2].StartsWith("unl_"))
            {
                to = new Link();
                to.Label = linkParts[2];
            }
            if (to is null) to = CreateThoughtFromMultipleAttributes(linkParts[2], false);
        }

        if (from is null || linkType is null) return null;
        if (structural && (from is not Link || to is not Link)) return null;

        //        Link r = AddStatement(from, linkType, to, label);
        Link? r;
        if (asPattern || structural)
        {
            if (asPattern && !structural && !linkType.HasProperty("isPossible"))
            {
                Thought ordinaryType = linkType;
                linkType = GetOrAddThought(ordinaryType.Label + ".?");
                linkType.AddParent(ordinaryType);
                linkType.AddParent(GetOrAddThought("?", "Property"));
            }
            // Reuse preallocated named links so forward references retain their identity.
            r = !string.IsNullOrEmpty(label) && Labeled(label) is Link placeholder && placeholder.From is null
                ? placeholder : new Link();
            r.From = from;
            r.LinkType = linkType;
            r.To = to;
            if (string.IsNullOrEmpty(label)) r.AddDefaultLabel();
            WriteTheLink(r);
        }
        else r = from.AddLink(linkType, to);
        if (r is null) return null;
        if (!string.IsNullOrEmpty(label))
            r.Label = label;

        if (value != "")
            r.From!.V = value;
        if (label != "" && !label.StartsWith("unl_"))
        {
            r.Label = label.Trim();
            if (!AtomicThoughts.Contains(r))
                AtomicThoughts.Add(r);
        }
        if (linkType.Label == "VLU")
        {
            // This must be a sequence element, promote it to one
            PromoteToSeqElement(from);
        }
        ApplyPersistenceMetadata(r, sWeight, sMaxWeight, sIsPlastic);
        return r;
    }

    /// <summary>
    /// Process a string that may be either a label or a nested link [src->type->target]
    /// </summary>
    /// <param name="part">The string to process</param>
    /// <returns>Thought if found/created, Link if nested, null if not found</returns>
    private Thought? ProcessThoughtOrLink(string part, bool asPattern = false)
    {
        if (string.IsNullOrWhiteSpace(part)) return null;


        string trimmed = part.Trim().Replace("→", "->");
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            string? label = System.Text.Json.JsonSerializer.Deserialize<string>(trimmed);
            return label is null ? null : Labeled(label) ?? GetOrAddThought(label);
        }

        // Check if this is a nested link (starts with '[')
        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
        {
            int depth = 0;
            for (int i = 0; i < trimmed.Length; i++)
            {
                if (trimmed[i] == '[') depth++;
                if (trimmed[i] == ']') depth--;
                if (depth == 0 && i < trimmed.Length - 1)
                {
                    trimmed = "[" + trimmed + "]";
                    break;
                }
            }
            // Parse the nested link
            var nestedStmt = ParseBracketStmt(trimmed, 0);
            if (nestedStmt.Count >= 2)
            {
                // Recursively create the nested link (with empty label)
                Thought? retVal = AddLinkStmtWithNesting("", nestedStmt, null, null, null, asPattern);
                return retVal;
            }
            if (asPattern && nestedStmt.Count == 1)
                return ProcessThoughtOrLink(nestedStmt[0], true);
            return null;
        }

        // Not a nested link, try to find existing thought
        Thought? existing = Labeled(trimmed);
        if (asPattern && existing is Link relationship && relationship.LinkType is not null &&
            !relationship.LinkType.HasProperty("isConditional") && !relationship.LinkType.HasProperty("isConjunction"))
        {
            Thought patternType = relationship.LinkType;
            if (!patternType.HasProperty("isPossible"))
            {
                patternType = GetOrAddThought(relationship.LinkType.Label + ".?");
                patternType.AddParent(relationship.LinkType);
                patternType.AddParent(GetOrAddThought("?", "Property"));
            }
            Link pattern = new(relationship.From, patternType, relationship.To);
            pattern.AddDefaultLabel();
            WriteTheLink(pattern);
            return pattern;
        }
        if (existing is not null) return existing;
        if (asPattern)
        {
            if (trimmed.Contains("->"))
            {
                var nestedStmt = ParseBracketStmt("[" + trimmed + "]", 0);
                if (nestedStmt.Count == 3)
                    return AddLinkStmtWithNesting("", nestedStmt, null, null, null, true);
            }
            else
            {
                var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length >= 3 && !trimmed.StartsWith("^"))
                    return AddLinkStmtWithNesting("", new List<string> {
                        words[0], string.Join(" ", words.Skip(1).Take(words.Length - 2)), words[^1] }, null, null, null, true);
            }
        }
        return null;
    }

    private static void ApplyPersistenceMetadata(
        Thought? thought,
        string? sWeight,
        string? sMaxWeight,
        string? sIsPlastic)
    {
        if (thought is null) return;

        if (sWeight is not null && float.TryParse(sWeight, out float weight))
            thought.Weight = weight;
        if (sMaxWeight is not null && float.TryParse(sMaxWeight, out float maxWeight))
            thought.maxWeight = maxWeight;
        if (sIsPlastic is not null && bool.TryParse(sIsPlastic, out bool isPlastic))
            thought.isPlastic = isPlastic;
    }

    private static void ApplyTrailingPersistenceMetadata(
        Thought? thought,
        List<string> tokens,
        int metadataStart)
    {
        if (thought is null || tokens.Count <= metadataStart) return;

        string? sWeight;
        string? sMaxWeight = null;
        string? sIsPlastic = null;
        bool hasExtendedMetadata = tokens.Count - metadataStart >= 3 &&
            bool.TryParse(tokens[^1], out _);
        if (hasExtendedMetadata)
        {
            sWeight = tokens[^3];
            sMaxWeight = tokens[^2];
            sIsPlastic = tokens[^1];
        }
        else
        {
            // Backward compatibility with text files containing only Weight.
            sWeight = tokens[^1];
        }

        ApplyPersistenceMetadata(thought, sWeight, sMaxWeight, sIsPlastic);
    }

    // Parse "[F->L->T]" or "[S,R,O,N]" (comma separated, quotes allowed around items)
    private static List<string> ParseBracketStmt(string s, int lineNo)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(s)) return result;
        s = s.Substring(1, s.Length - 2); // drop initialFinal [ ]
        var sb = new StringBuilder();
        int bracketDepth = 0;
        bool quoted = false;

        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && quoted && i + 1 < s.Length)
            {
                sb.Append(s[i]);
                sb.Append(s[++i]);
                continue;
            }
            if (s[i] == '"') quoted = !quoted;
            if (quoted || s[i] == '"') { sb.Append(s[i]); continue; }
            if (s[i] == '[') bracketDepth++;
            if (s[i] == ']') bracketDepth--;

            if (bracketDepth == 0 &&
                i + 1 < s.Length &&
                s[i] == '-' &&
                s[i + 1] == '>')
            {
                result.Add(sb.ToString().Trim());
                sb.Clear();
                i++; // skip '>'
                continue;
            }

            sb.Append(s[i]);
        }

        result.Add(sb.ToString().Trim());
        // Accept [condition] AND [condition] as readable input for a nested triple.
        if (result.Count == 1 && s.TrimStart().StartsWith("["))
        {
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '[') depth++;
                if (s[i] != ']') continue;
                if (--depth != 0) continue;
                string remainder = s[(i + 1)..].Trim();
                int separator = remainder.IndexOf(' ');
                if (separator > 0 && remainder[(separator + 1)..].TrimStart().StartsWith("["))
                    return new List<string> { s[..(i + 1)].Trim(), remainder[..separator], remainder[(separator + 1)..].Trim() };
                break;
            }
        }
        return result;
    }

    // Strip EOL comments outside of quotes and brackets
    private static string StripEolComment(string line)
    {
        if (line is null) return string.Empty;

        var sb = new StringBuilder(line.Length);
        bool inQuotes = false;
        int bracketDepth = 0;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (!inQuotes)
            {
                if (c == '[') { bracketDepth++; sb.Append(c); continue; }
                if (c == ']' && bracketDepth > 0) { bracketDepth--; sb.Append(c); continue; }
            }

            if (c == '"' && bracketDepth == 0)
            {
                inQuotes = !inQuotes;
                sb.Append(c);
                continue;
            }

            if (!inQuotes && bracketDepth == 0)
            {
                if (c == '#') break;
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') break;
            }

            sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    // Tokenize top-level into label, optional [ ... ], and metadata tokens.
    private static List<string> TokenizeTopLevel(string code)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(code)) return tokens;

        int leftBracketPos = -1;
        bool quoted = false;
        for (int i = 0; i < code.Length; i++)
        {
            if (code[i] == '\\' && quoted) { i++; continue; }
            if (code[i] == '"') quoted = !quoted;
            if (!quoted && code[i] == '[') { leftBracketPos = i; break; }
        }
        if (leftBracketPos == -1)
        {
            var metadataMatch = Regex.Match(code, @"^(.*?)\s+(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)\s+(True|False)$", RegexOptions.IgnoreCase);
            if (metadataMatch.Success)
            {
                string thoughtLabel = metadataMatch.Groups[1].Value.Trim();
                if (thoughtLabel.StartsWith('"')) thoughtLabel = System.Text.Json.JsonSerializer.Deserialize<string>(thoughtLabel) ?? "";
                tokens.Add(thoughtLabel);
                for (int i = 2; i <= 4; i++) tokens.Add(metadataMatch.Groups[i].Value);
                return tokens;
            }
            tokens.AddRange(code.Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            return tokens;
        }

        int rightBrackedPos = code.LastIndexOf("]") + 1;
        if (rightBrackedPos <= 0) return tokens;

        string label = code[..leftBracketPos].Trim();
        if (label.StartsWith('"')) label = System.Text.Json.JsonSerializer.Deserialize<string>(label) ?? "";
        string metadata = code[rightBrackedPos..].Trim();
        string body = code[leftBracketPos..rightBrackedPos].Trim();

        tokens.Add(label);
        tokens.Add(body);
        if (!string.IsNullOrWhiteSpace(metadata))
            tokens.AddRange(metadata.Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));

        return tokens;
    }
}



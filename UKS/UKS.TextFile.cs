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
                HashSet<Thought> collectedThoughts = new();

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

    void EnsureLabel(Thought t)
    {
        if (string.IsNullOrWhiteSpace(t.Label))
            // Put the GUID into the label only when it's unlabeled
            t.Label = $"unl_{Guid.NewGuid().ToString("N")[..8]}";
    }

    string FormatThought(Thought t)
    {
        EnsureLabel(t);
        string retVal = t.Label.PadRight(15);
        if (t.V is not null)
            retVal += " V: " + t.V.ToString();
        if (t is Link r)
        {
            if (r.From is Link r1) EnsureLabel(r1);
            if (r.To is Link r2) EnsureLabel(r2);
            retVal += "[";
            if (r.From is not null)
                retVal += r.From?.Label.Trim();
            if (r.LinkType is not null)
                retVal += ((retVal == "") ? "" : "->") + r.LinkType?.Label.Trim();
            if (r.To is not null)
                retVal += ((retVal == "") ? "" : "->") + ((r.To.Label == "") ? r.To.ToString() : r.To.Label.Trim());
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

    public Thought? ProcessSingleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        string code = StripEolComment(line);
        if (string.IsNullOrWhiteSpace(code)) return null;

        code = code.Trim();

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
        Thought? from = GetOrAddThought(linkParts[0]);

        // Process 'linkType' 
        Thought? linkType = GetOrAddThought(linkParts[1]);

        // Process 'to'
        //this might be a sequence
        Thought? to = CreateThoughtFromMultipleAttributes(linkParts[2], false, false);
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
    public Thought? ProcessSingleLineWithNesting(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        string code = StripEolComment(line);
        if (string.IsNullOrWhiteSpace(code)) return null;

        code = code.Trim();

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
        return linkRetVal;
    }

    // Adds a link, handling nested links in src, type, or target
    private Thought? AddLinkStmtWithNesting(
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

        // Process 'from' - may be a nested link
        Thought? from = ProcessThoughtOrLink(linkParts[0]);
        if (from is null) from = GetOrAddThought(linkParts[0]);

        // Process 'linkType' - may be a nested link
        Thought? linkType = ProcessThoughtOrLink(linkParts[1]);
        if (linkType is null) linkType = GetOrAddThought(linkParts[1]);

        // Process 'to' - may be a nested link
        Thought? to = null;
        if (linkParts.Count > 2)
        {
            to = ProcessThoughtOrLink(linkParts[2]);
            if (to is null && linkParts[2].StartsWith("unl_")) 
            { 
                to = new Link(); 
                to.Label = linkParts[2]; 
            }
            if (to is null) to = GetOrAddThought(linkParts[2]);
        }

        if (from is null || linkType is null) return null;

        //        Link r = AddStatement(from, linkType, to, label);
        Link? r = from.AddLink(linkType, to);
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
    private Thought? ProcessThoughtOrLink(string part)
    {
        if (string.IsNullOrWhiteSpace(part)) return null;


        var tokens = TokenizeTopLevel(part);
        if (tokens.Count < 2) return null;
        string trimmed = tokens[1].Trim();

        // Check if this is a nested link (starts with '[')
        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
        {
            // Parse the nested link
            var nestedStmt = ParseBracketStmt(trimmed, 0);
            if (nestedStmt.Count >= 2)
            {
                // Recursively create the nested link (with empty label)
                Thought? retVal = AddLinkStmtWithNesting("", nestedStmt, null, null, null);
                return retVal;
            }
            return null;
        }

        // Not a nested link, try to find existing thought
        return Labeled(trimmed);
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

        for (int i = 0; i < s.Length; i++)
        {
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

        int leftBracketPos = code.IndexOf("[");
        if (leftBracketPos == -1)
        {
            tokens.AddRange(code.Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            return tokens;
        }

        int rightBrackedPos = code.LastIndexOf("]") + 1;
        if (rightBrackedPos <= 0) return tokens;

        string label = code[..leftBracketPos].Trim();
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



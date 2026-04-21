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
        Thought Root = theUKS.Labeled(root);
        if (Root is null) return;

        HashSet<string> alreadyWritten = new();
        try
        {
            using (var writer = new StreamWriter(path))
            {
                if (writer is null) throw new ArgumentNullException(nameof(writer));
                foreach (var t in Root.EnumerateSubThoughts())
                {
                    string s = FormatThought(t) + " " + t.Weight.ToString("F2");
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
        { }
        RemoveTempLabels(Root);
    }

    private void RemoveTempLabels(Thought Root)
    {
        if (Root is null) return;
        var v = AtomicThoughts;

        //remove unnecessary "unl_..."  labels
        foreach (var t in Root.EnumerateSubThoughts())
        {
            if (t.Label.ToLower() == "fido")
            { }
            int i = AtomicThoughts.IndexOf(t);

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

        int lineNo = 0;
        foreach (var raw in File.ReadLines(filePath))
        {
            lineNo++;
            string code = StripEolComment(raw);
            if (string.IsNullOrWhiteSpace(code)) continue;

            var tokens = TokenizeTopLevel(code); // bracket tokens + connector tokens
            if (tokens.Count == 0) continue;

            var stmt = ParseBracketStmt(tokens[1], lineNo);
            Thought r = AddLinkStmt(tokens[0], stmt, tokens[2]);
        }

        //remove unnecessary "unl_..."  labels
        foreach (var t in ((Thought)"Thought").EnumerateSubThoughts())
        {
            if (t.Label.StartsWith("unl_"))
                t.Label = "";
        }
    }

    /// <summary>
    /// Processes a single line of UKS text format and creates the corresponding thought/link.
    /// </summary>
    /// <param name="line">The line to process in UKS text format (e.g., "label[from->linkType->to] weight")</param>
    /// <returns>The created Thought/Link, or null if parsing failed.</returns>
    public Thought ProcessSingleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        string code = StripEolComment(line);
        if (string.IsNullOrWhiteSpace(code)) return null;

        var tokens = TokenizeTopLevel(code);
        if (tokens.Count < 2) return null;

        var stmt = ParseBracketStmt(tokens[1], 0);
        if (stmt.Count < 2) return null;

        Thought r = AddLinkStmt(tokens[0], stmt, tokens.Count > 2 ? tokens[2] : null);
        return r;
    }

    // Adds a link, 
    private Thought AddLinkStmt(string label, List<string> linkParts, string sWeight)
    {
        if (linkParts[0].Contains("seq0"))
        { }
        Link r = null;
        if (linkParts.Count < 2) return null;
        if (r is null)
        {
            //get value strings (used in config
            string value = "";
            if (linkParts[0].Contains("_V:"))
            {
                int index = linkParts[0].IndexOf("_V:");
                value = linkParts[0][(index + 3)..];
                linkParts[0] = linkParts[0][..index];
                Thought t1 = Labeled(linkParts[0]);
                t1?.Delete();
            }
            //if (r1 or r2 are set, use them instead here
            Thought from = Labeled(linkParts[0]);
            if (from is null) from = AddThought(linkParts[0], null);
            Thought linkType = Labeled(linkParts[1]);
            if (linkType is null) linkType = AddThought(linkParts[1], null);
            Thought to = null;
            if (linkParts.Count > 2)
            {
                to = Labeled(linkParts[2]);
                if (to is null && linkParts[2].StartsWith("unl_")) { to = new Link(); to.Label = linkParts[2]; }
                if (to is null) to = AddThought(linkParts[2], null);
            }

            r = AddStatement(from, linkType, to, label);

            if (value != "")
                r.From.V = value;
            if (label != "" && !label.StartsWith("unl_"))
            {
                r.Label = label.Trim();
                if (!AtomicThoughts.Contains(r))
                    AtomicThoughts.Add(r);
            }
            if (linkType.Label == "VLU")
            {//this must a a sequence element, promote it to one.
                var newfrom = PromoteToSeqElement(from);
            }
        }
        if (sWeight is { } n)
        {
            if (float.TryParse(n, out float weight))
                r.Weight = weight;
        }
        return r;
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

    // Tokenize top-level into: [ ... ]  or  connector tokens (whitespace separated)
    private static List<string> TokenizeTopLevel(string code)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(code)) return tokens;

        int leftBracketPos = code.IndexOf("[");
        int rightBrackedPos = code.LastIndexOf("]") + 1;
        if (leftBracketPos == -1 || rightBrackedPos == -1) return tokens;

        string label = code[..leftBracketPos].Trim();
        string weight = code[rightBrackedPos..].Trim();
        string body = code[leftBracketPos..rightBrackedPos].Trim();

        tokens.Add(label);
        tokens.Add(body);
        tokens.Add(weight);

        return tokens;
    }
}



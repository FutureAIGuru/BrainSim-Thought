using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pluralize.NET;
using UKS;

namespace UKSNative;

// ---- DTOs returned to Swift as JSON ----

public sealed class LinkDto
{
    public string Label { get; set; } = "";
    public string From { get; set; } = "";
    public string LinkType { get; set; } = "";
    public string To { get; set; } = "";
    // Labels of the endpoints when they are plain, labeled Thoughts (navigable in the UI)
    public string FromLabel { get; set; }
    public string LinkTypeLabel { get; set; }
    public string ToLabel { get; set; }
    public float Weight { get; set; }
    public int InheritanceDepth { get; set; }
    public string InheritedFrom { get; set; }
    public bool IsConditional { get; set; }
    public string Text { get; set; } = "";
    /// <summary>Seconds until the link is forgotten, or null when it is permanent.</summary>
    public double? TimeToLive { get; set; }
}

public sealed class ThoughtDto
{
    public string Label { get; set; } = "";
    public string Value { get; set; }
    public float Weight { get; set; }
    public bool IsLink { get; set; }
    public bool IsSequence { get; set; }
    public string Text { get; set; } = "";
    public List<string> Parents { get; set; } = new();
    public List<string> Children { get; set; } = new();
    public List<LinkDto> LinksTo { get; set; } = new();
    public List<LinkDto> LinksFrom { get; set; } = new();
}

public sealed class ScoredDto
{
    public string Label { get; set; } = "";
    public float Confidence { get; set; }
}

public sealed class QueryResultDto
{
    public List<string> Thoughts { get; set; } = new();
    public List<LinkDto> Links { get; set; } = new();
}

public sealed class GraphNodeDto
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public int Depth { get; set; }
    public int ChildCount { get; set; }
    public bool IsLinkType { get; set; }
    public bool IsLink { get; set; }
}

public sealed class GraphEdgeDto
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public string LinkType { get; set; } = "";
    public bool IsA { get; set; }
    public float Weight { get; set; }
}

public sealed class GraphDto
{
    public string Root { get; set; }
    public List<GraphNodeDto> Nodes { get; set; } = new();
    public List<GraphEdgeDto> Edges { get; set; } = new();
    public bool Truncated { get; set; }
}

public sealed class ResultDto
{
    public bool Ok { get; set; }
    public string Error { get; set; }
    public string Message { get; set; }
    public LinkDto Link { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ThoughtDto))]
[JsonSerializable(typeof(QueryResultDto))]
[JsonSerializable(typeof(ResultDto))]
[JsonSerializable(typeof(GraphDto))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<ScoredDto>))]
internal partial class JsonCtx : JsonSerializerContext { }

/// <summary>
/// C ABI over the Brain Simulator Thought UKS. Every export takes/returns UTF-8 C strings;
/// returned strings are allocated here and must be released with uks_free.
/// All calls are serialized through a single lock since the UKS is a process-wide singleton.
/// </summary>
public static partial class Exports
{
    static readonly object gate = new();
    static readonly Pluralizer pluralizer = new();

    // The app has one UKS. UKS.theUKS (which Thought.Delete(), labels and the modules use) is
    // per execution context, and the app calls in from several native threads (main, engine,
    // background saves), each of which would otherwise get a new, empty UKS. So every entry
    // point selects the shared instance when it takes the lock (UseShared).
    static UKS.UKS shared;
    static UKS.UKS Store => UKS.UKS.theUKS;

    /// <summary>Makes the app's UKS the current one on this thread (call with the lock held).</summary>
    internal static void UseShared() => UKS.UKS.theUKS = shared ??= new UKS.UKS();

    // ---------- marshalling helpers ----------

    static string In(IntPtr p) => p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";
    static IntPtr Out(string s) => Marshal.StringToCoTaskMemUTF8(s ?? "");
    static IntPtr Json<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) =>
        Out(JsonSerializer.Serialize(value, info));
    static IntPtr Error(Exception e) =>
        Json(new ResultDto { Ok = false, Error = e.Message }, JsonCtx.Default.ResultDto);
    static IntPtr Ok(string message = null, Link l = null) =>
        Json(new ResultDto { Ok = true, Message = message, Link = l == null ? null : ToDto(l) }, JsonCtx.Default.ResultDto);

    static IntPtr Guard(Func<IntPtr> body)
    {
        lock (gate)
        {
            try { UseShared(); return body(); }
            catch (Exception e) { return Error(e); }
        }
    }

    /// <summary>What <see cref="PollGuard"/> returns when the UKS stays busy.</summary>
    public const string BusyError = "busy";

    /// <summary>
    /// For the calls the app makes on a timer from the main thread (module list, notices,
    /// dialogs, canvases). They only wait briefly for the lock: if the engine or a long module
    /// operation holds it, they return <see cref="BusyError"/> and the app keeps showing what
    /// it had, instead of freezing until the work is done.
    /// </summary>
    static IntPtr PollGuard(Func<IntPtr> body, int waitMs = 15)
    {
        if (!Monitor.TryEnter(gate, waitMs))
            return Json(new ResultDto { Ok = false, Error = BusyError }, JsonCtx.Default.ResultDto);
        try { UseShared(); return body(); }
        catch (Exception e) { return Error(e); }
        finally { Monitor.Exit(gate); }
    }

    /// <summary>
    /// Runs long module work (the Vision dialog's "100", file loaders) on a background thread.
    /// Each step takes the lock, so it doesn't race the engine or the UI, and releases it
    /// in between, so both keep running while the work goes on.
    /// </summary>
    internal static Task RunLocked(Action step, int count = 1) => Task.Run(() =>
    {
        for (int i = 0; i < count; i++)
        {
            lock (gate) { UseShared(); step(); }
            // Monitor isn't fair: without a pause this thread takes the lock straight back
            // and the engine and the app's polls rarely get it.
            if (i < count - 1) Thread.Sleep(2);
        }
    });

    /// <summary>Like <see cref="RunLocked"/>, for work of unknown length: repeats `step` until it returns false.</summary>
    internal static Task RunLockedWhile(Func<bool> step) => Task.Run(() =>
    {
        while (true)
        {
            bool more;
            lock (gate) { UseShared(); more = step(); }
            if (!more) break;
            Thread.Sleep(2);
        }
    });

    // Same rendering as the desktop Query dialog: nested links print as [from → type → to]
    static string Format(Thought t) => t switch
    {
        null => "?",
        Link l => $"[{Format(l.From)} → {l.LinkType?.Label ?? "?"} → {Format(l.To)}]",
        _ => string.IsNullOrEmpty(t.Label) ? t.ToString() : t.Label,
    };

    static string NavLabel(Thought t) => t is null || t is Link || string.IsNullOrEmpty(t.Label) ? null : t.Label;

    static LinkDto ToDto(Link l) => new()
    {
        Label = l.Label ?? "",
        From = Format(l.From),
        LinkType = Format(l.LinkType),
        To = l.To == null ? "" : Format(l.To),
        FromLabel = NavLabel(l.From),
        LinkTypeLabel = NavLabel(l.LinkType),
        ToLabel = NavLabel(l.To),
        Weight = l.Weight,
        InheritanceDepth = l.InheritanceDepth,
        InheritedFrom = l.InheritedFromCategory?.Label,
        IsConditional = UKS.UKS.IsConditionalLinkType(l.LinkType),
        Text = Format(l),
        TimeToLive = l.TimeToLive == TimeSpan.MaxValue || l.TimeToLive > DateTime.MaxValue - l.LastFiredTime
            ? null : Math.Max(0, (l.LastFiredTime + l.TimeToLive - DateTime.Now).TotalSeconds),
    };

    // Same rules as the desktop ModuleUKSStatement
    static string[] Singular(string[] s)
    {
        for (int i = 0; i < s.Length; i++)
            if (!char.IsUpper(s[i][0]) && s[i].Length > 2)
                s[i] = pluralizer.Singularize(s[i]);
        return s;
    }

    static List<Thought> ThoughtListFromString(string source)
    {
        List<Thought> retVal = new();
        foreach (string raw in source.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string word = raw;
            if (word.Length == 1) word = UKS.UKS.GetCharacterLabel(word[0]);
            if (!char.IsUpper(word[0]) && word.Length > 2) word = pluralizer.Singularize(word);
            Thought t = ThoughtLabels.GetThought(word);
            if (t == null) return retVal;
            retVal.Add(t);
        }
        return retVal;
    }

    static string ConditionalLinkType(string outerLinkType, string nestedLinkType)
    {
        if (!string.Equals(outerLinkType, "IF", StringComparison.OrdinalIgnoreCase)) return nestedLinkType;
        if (Array.IndexOf(nestedLinkType.Split('.'), "?") >= 0) return nestedLinkType;
        return nestedLinkType + ".?";
    }

    // A three-word source/target ("Fido play outside") is itself a Link; reuse one if it exists.
    static Thought NestedOrPlain(string text, string linkType, bool allowSequence)
    {
        var parts = Singular(text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (parts.Length == 3 && (allowSequence || !parts[0].StartsWith("^")))
        {
            var uks = Store;
            parts[1] = ConditionalLinkType(linkType, parts[1]);
            var probe = new Link
            {
                From = uks.GetOrAddThought(parts[0]),
                LinkType = uks.GetOrAddThought(parts[1]),
                To = uks.GetOrAddThought(parts[2]),
            };
            var existing = uks.GetLinks(probe);
            return existing.Count > 0 ? existing[0] : uks.AddStatement(parts[0], parts[1], parts[2]);
        }
        return Store.CreateThoughtFromMultipleAttributes(text, false);
    }

    static Thought Require(string label) =>
        Store.Labeled(label) ?? throw new ArgumentException($"No Thought labeled '{label}'.");

    // ---------- lifecycle ----------

    [UnmanagedCallersOnly(EntryPoint = "uks_free")]
    public static void Free(IntPtr p)
    {
        if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
    }

    /// <summary>Builds the initial structure (or resets to it when clear != 0). Returns the Thought count.</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_initialize")]
    public static int Initialize(int clear)
    {
        lock (gate)
        {
            try
            {
                UseShared();
                if (clear != 0 || Store.Labeled("Thought") == null)
                {
                    // a fresh, empty UKS replaces the shared one; CreateEmptyUKS rebuilds the modules on it
                    if (clear != 0) shared = new UKS.UKS(true);
                    CreateEmptyUKS();
                }
                return Store.AtomicThoughts.Count;
            }
            catch { return -1; }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "uks_thought_count")]
    public static int ThoughtCount()
    {
        // Lock-free: reading a count is safe, and the app asks for it from the main thread.
        return (shared ?? Store).AtomicThoughts.Count;
    }

    // ---------- statements ----------

    /// <summary>Adds "from linkType to", following the desktop Statement dialog's rules
    /// (multi-word phrases become attributed subclasses, three-word phrases become nested
    /// Links, "IF" marks nested link types conditional, "^a b c" is a sequence).</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_add_statement")]
    public static IntPtr AddStatement(IntPtr pFrom, IntPtr pLinkType, IntPtr pTo, float weight) => Guard(() =>
    {
        string from = In(pFrom).Trim(), linkType = In(pLinkType).Trim(), to = In(pTo).Trim();
        if (from == "" || linkType == "") throw new ArgumentException("From and link type are required.");

        var uks = Store;
        if (pluralizer.IsPlural(from) && pluralizer.IsPlural(to) && linkType == "are")
            linkType = "is-a";

        Thought tFrom = NestedOrPlain(from, linkType, true) ?? throw new ArgumentException($"Could not create '{from}'.");
        Thought tTo = to == "" ? null : NestedOrPlain(to, linkType, false);
        Thought tLinkType = uks.CreateThoughtFromMultipleAttributes(linkType, true)
            ?? throw new ArgumentException($"Could not create link type '{linkType}'.");

        bool isNew = uks.GetLink(tFrom, tLinkType, tTo) == null;
        Link l = uks.AddStatement(tFrom, tLinkType, tTo) ?? throw new InvalidOperationException("Statement was not added.");
        if (isNew && weight > 0 && weight < 1) l.Weight = weight;
        return Ok(null, l);
    });

    /// <summary>Processes one line of the UKS text format, e.g. "[Fido->is-a->dog] 1.00".</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_parse_line")]
    public static IntPtr ParseLine(IntPtr pLine) => Guard(() =>
    {
        Thought t = Store.ProcessSingleLine(In(pLine)) ?? throw new FormatException("Nothing to add. Use [from->linkType->to].");
        return t is Link l ? Ok(null, l) : Ok($"Added '{t.Label}'");
    });

    [UnmanagedCallersOnly(EntryPoint = "uks_delete_thought")]
    public static IntPtr DeleteThought(IntPtr pLabel, int withChildren) => Guard(() =>
    {
        Thought t = Require(In(pLabel));
        if (withChildren != 0) Store.DeleteAllChildrenAndLinks(t);
        t.Delete();
        return Ok($"Deleted '{t.Label}'");
    });

    /// <summary>Removes the outgoing link of `from` whose rendered text is `text`.</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_delete_link")]
    public static IntPtr DeleteLink(IntPtr pFrom, IntPtr pText) => Guard(() =>
    {
        Thought from = Require(In(pFrom));
        string text = In(pText);
        Link l = from.LinksTo.FirstOrDefault(x => Format(x) == text) ?? throw new ArgumentException("Link not found.");
        from.RemoveLink(l);
        return Ok("Removed");
    });

    /// <summary>UKS dialog "Rename": fails if another Thought already has the label.</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_rename_thought")]
    public static IntPtr RenameThought(IntPtr pLabel, IntPtr pNewLabel) => Guard(() =>
    {
        Thought t = Require(In(pLabel));
        string newLabel = In(pNewLabel).Trim();
        if (newLabel == "") throw new ArgumentException("A label is required.");
        Thought existing = ThoughtLabels.GetThought(newLabel);
        if (existing is not null && !ReferenceEquals(existing, t)) throw new ArgumentException($"'{newLabel}' is already used.");
        t.Label = newLabel;
        // clear any time-to-live on this newly named Thought
        if (t.LinksFrom.FirstOrDefault(x => x.LinkType?.Label == "is-a") is Link isALink) isALink.TimeToLive = TimeSpan.MaxValue;
        return Ok(t.Label);
    });

    /// <summary>UKS dialog "Fire": fires the Thought and lets ModuleAction take it as an action.</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_fire_thought")]
    public static IntPtr FireThought(IntPtr pLabel) => Guard(() =>
    {
        Thought t = Require(In(pLabel));
        t.Fire();
        BrainSimulator.MainWindow.theWindow.activeModules.OfType<BrainSimulator.Modules.ModuleAction>().FirstOrDefault()?.TakeActrion(t);
        return Ok("Fired");
    });

    /// <summary>UKS dialog "Delete Child": removes the is-a link between parent and child.</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_remove_parent")]
    public static IntPtr RemoveParent(IntPtr pChild, IntPtr pParent) => Guard(() =>
    {
        Require(In(pParent)).RemoveChild(Require(In(pChild)));
        return Ok("Removed");
    });

    // ---------- browsing ----------

    [UnmanagedCallersOnly(EntryPoint = "uks_get_thought")]
    public static IntPtr GetThought(IntPtr pLabel) => Guard(() =>
    {
        Thought t = Store.Labeled(In(pLabel));
        if (t == null) return Out("null");
        var dto = new ThoughtDto
        {
            Label = t.Label,
            Value = t.V?.ToString(),
            Weight = t.Weight,
            IsLink = t is Link,
            IsSequence = t is SeqElement,
            Text = Format(t),
            Parents = t.Parents.Select(Format).ToList(),
            Children = t.Children.Select(Format).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            LinksTo = t.LinksTo.Where(l => l.LinkType != Thought.IsA).Select(ToDto).ToList(),
            LinksFrom = t.LinksFrom.Where(l => l.LinkType != Thought.IsA).Select(ToDto).ToList(),
        };
        return Json(dto, JsonCtx.Default.ThoughtDto);
    });

    /// <summary>Thoughts with no parents (the tree roots, e.g. Thought, BrainSim).</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_roots")]
    public static IntPtr Roots() => Guard(() =>
        Json(Store.AtomicThoughts
            .Where(t => t is not Link && t.Label != "" && t.Parents.Count == 0)
            .Select(t => t.Label)
            .OrderBy(l => l != "Thought")
            .ThenBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList(), JsonCtx.Default.ListString));

    /// <summary>Case-insensitive substring search over labels.</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_search")]
    public static IntPtr Search(IntPtr pText, int limit) => Guard(() =>
    {
        string text = In(pText);
        var hits = ThoughtLabels.LabelList.Values
            .Select(t => t.Label)
            .Where(l => l != "" && l.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .OrderBy(l => !l.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .ThenBy(l => l.Length)
            .Take(limit <= 0 ? 100 : limit)
            .ToList();
        return Json(hits, JsonCtx.Default.ListString);
    });

    /// <summary>
    /// The neighborhood of a Thought for graph display: breadth-first over is-a links and
    /// links in both directions, up to `depth` hops and `maxNodes` Thoughts.
    /// Nested links (links whose From/To is itself a Link) appear as their own nodes.
    /// An empty root means the whole UKS (still capped at maxNodes).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_graph")]
    public static IntPtr Graph(IntPtr pRoot, int depth, int maxNodes) => Guard(() =>
    {
        var uks = Store;
        string rootLabel = In(pRoot).Trim();
        if (maxNodes <= 0) maxNodes = 150;
        var graph = new GraphDto();
        var depthOf = new Dictionary<Thought, int>(ReferenceEqualityComparer.Instance);
        Thought linkTypeRoot = uks.Labeled("LinkType");

        if (rootLabel == "")
        {
            foreach (Thought t in uks.AtomicThoughts.Where(t => t is not Link))
            {
                if (depthOf.Count >= maxNodes) { graph.Truncated = true; break; }
                depthOf[t] = 0;
            }
        }
        else
        {
            Thought root = Require(rootLabel);
            graph.Root = Format(root);
            var queue = new Queue<Thought>();
            depthOf[root] = 0;
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                Thought t = queue.Dequeue();
                int d = depthOf[t];
                if (d >= depth) continue;
                var neighbors = t.LinksTo.Select(l => l.To).Concat(t.LinksFrom.Select(l => l.From));
                if (t is Link self) neighbors = neighbors.Append(self.From).Append(self.To);
                foreach (Thought n in neighbors)
                {
                    if (n == null || depthOf.ContainsKey(n)) continue;
                    if (depthOf.Count >= maxNodes) { graph.Truncated = true; break; }
                    depthOf[n] = d + 1;
                    queue.Enqueue(n);
                }
            }
        }

        var ids = new Dictionary<Thought, string>(ReferenceEqualityComparer.Instance);
        var used = new HashSet<string>();
        string IdOf(Thought t)
        {
            if (ids.TryGetValue(t, out var id)) return id;
            string baseId = Format(t), candidate = baseId;
            for (int i = 2; !used.Add(candidate); i++) candidate = $"{baseId}#{i}";
            return ids[t] = candidate;
        }

        foreach (var (t, d) in depthOf)
        {
            graph.Nodes.Add(new GraphNodeDto
            {
                Id = IdOf(t),
                Label = Format(t),
                Depth = d,
                ChildCount = t.Children.Count,
                IsLinkType = linkTypeRoot != null && t != linkTypeRoot && t is not Link && t.HasAncestor(linkTypeRoot),
                IsLink = t is Link,
            });
        }
        foreach (var t in depthOf.Keys)
        {
            foreach (Link l in t.LinksTo)
            {
                if (l.To == null || l.LinkType == null || !depthOf.ContainsKey(l.To)) continue;
                graph.Edges.Add(new GraphEdgeDto
                {
                    Source = IdOf(t),
                    Target = IdOf(l.To),
                    LinkType = Format(l.LinkType),
                    IsA = l.LinkType == Thought.IsA,
                    Weight = l.Weight,
                });
            }
            // A nested link node connects to its own endpoints
            if (t is Link self)
            {
                if (self.From != null && depthOf.ContainsKey(self.From))
                    graph.Edges.Add(new GraphEdgeDto { Source = IdOf(self.From), Target = IdOf(t), LinkType = "from", Weight = self.Weight });
                if (self.To != null && depthOf.ContainsKey(self.To))
                    graph.Edges.Add(new GraphEdgeDto { Source = IdOf(t), Target = IdOf(self.To), LinkType = self.LinkType?.Label ?? "", Weight = self.Weight });
            }
        }
        return Json(graph, JsonCtx.Default.GraphDto);
    });

    // ---------- query (mirrors desktop ModuleUKSQuery.GetAttributes) ----------

    [UnmanagedCallersOnly(EntryPoint = "uks_query")]
    public static IntPtr Query(IntPtr pSource, IntPtr pLinkType, IntPtr pTarget, IntPtr pFilter) => Guard(() =>
    {
        var uks = Store;
        var result = new QueryResultDto();
        string source = In(pSource).Trim(), linkType = In(pLinkType).Trim(), target = In(pTarget).Trim(), filter = In(pFilter).Trim();

        bool reverse = false;
        if (source == "")
        {
            (source, target) = (target, source);
            reverse = true;
        }

        List<Thought> sourceList = ThoughtListFromString(source);
        List<Thought> linkTypeList = ThoughtListFromString(linkType);
        List<Thought> targetList = ThoughtListFromString(target);
        List<Thought> filterThoughts = ThoughtListFromString(filter);

        // is-a queries are answered from the hierarchy directly
        if (linkType.Contains("is-a") && reverse || linkType.Contains("has-child") && !reverse)
        {
            if (sourceList.Count > 0) result.Thoughts = sourceList[0].Children.Select(Format).ToList();
            return Json(result, JsonCtx.Default.QueryResultDto);
        }
        if (linkType.Contains("is-a") && !reverse || linkType.Contains("has-child") && reverse)
        {
            if (sourceList.Count > 0) result.Thoughts = sourceList[0].AncestorsWithSelf.Select(Format).ToList();
            return Json(result, JsonCtx.Default.QueryResultDto);
        }
        if (sourceList.Count == 0)
            throw new ArgumentException($"'{source}' is not in the UKS.");

        // Filter: the Type/Target fields narrow by ancestor; the Filter field follows the
        // desktop rule (one Thought filters the target, several filter the link type).
        Link filterLink = null;
        if (filterThoughts.Count == 1) filterLink = new Link { To = filterThoughts[0] };
        else if (filterThoughts.Count > 1) filterLink = new Link { LinkType = filterThoughts[0] };
        Thought typeFilter = linkTypeList.FirstOrDefault();
        Thought targetFilter = reverse ? null : targetList.FirstOrDefault();
        if (typeFilter != null || targetFilter != null)
        {
            filterLink ??= new Link();
            filterLink.LinkType ??= typeFilter;
            filterLink.To ??= targetFilter;
        }

        List<Link> links = uks.GetAttributes(sourceList[0], filterLink);
        result.Links = links.Select(ToDto).ToList();
        return Json(result, JsonCtx.Default.QueryResultDto);
    });

    /// <summary>Explains the latest query of `source` like the desktop Query dialog's Why? button.</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_why")]
    public static IntPtr Why(IntPtr pSource, IntPtr pLinkType, IntPtr pTarget) => Guard(() =>
    {
        var uks = Store;
        string linkType = In(pLinkType).Trim(), target = In(pTarget).Trim();
        Thought source = uks.Labeled(In(pSource).Trim()) ?? throw new ArgumentException("Why? — source not found");
        List<Link> links = uks.GetAttributes(source);

        List<Link> whyNot = uks.WhyNot().Where(l => l != null).ToList();
        if (whyNot.Count > 0) return Ok("Why not?\n" + Explain(whyNot, false));
        List<Link> why = uks.Why().Where(l => l != null).ToList();
        if (why.Count > 0) return Ok("Why?\n" + Explain(why, true));

        Link match = null;
        if (target != "") match = links.FirstOrDefault(l => l.To?.Label == target);
        if (match == null && linkType != "")
            match = links.FirstOrDefault(l => string.Equals(l.LinkType?.Label, linkType, StringComparison.OrdinalIgnoreCase));
        match ??= links.FirstOrDefault(l => l.InheritanceDepth > 0);
        if (match == null) return Ok("Why? — no explainable inherited link");

        List<Thought> trace = new() { source };
        if (match.InheritedFromCategory != null) trace.Add(match.InheritedFromCategory);
        if (match.To != null) trace.Add(match.To);
        return Ok("Why? " + string.Join(" → ", trace.Select(Format)));
    });

    static string Explain(List<Link> links, bool verified)
    {
        Link ifLink = links.FirstOrDefault(l =>
            string.Equals(l.LinkType?.Label, "IF", StringComparison.OrdinalIgnoreCase) && l.From is Link && l.To is Link);
        if (ifLink?.From is not Link result || ifLink.To is not Link condition)
            return string.Join("\n", links.Select(Format).Distinct());
        string clause = Clause(result);
        return $"{char.ToUpperInvariant(clause[0])}{clause[1..]} {(verified ? "included" : "not included")} because " +
               $"{Clause(condition)} was {(verified ? "verified" : "not verified")}.";
    }

    static string Clause(Link l)
    {
        if (string.Equals(l.LinkType?.Label, "AND", StringComparison.OrdinalIgnoreCase) && l.From is Link a && l.To is Link b)
            return $"{Clause(a)} and {Clause(b)}";
        string predicate = l.LinkType?.Label ?? "?";
        if (UKS.UKS.IsConditionalLinkType(l.LinkType))
            predicate = string.Join(" ", predicate.Split('.').Where(p => p != "?"));
        if (predicate.Equals("is-a", StringComparison.OrdinalIgnoreCase)) predicate = "is a";
        return $"{Format(l.From)} {predicate} {Format(l.To)}";
    }

    /// <summary>
    /// Finds the Thoughts under `ancestor` that best match a list of attributes, one
    /// "linkType,target[,weight]" per line (desktop Query dialog's attribute search).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_search_by_attributes")]
    public static IntPtr SearchByAttributes(IntPtr pAttributes, IntPtr pAncestor) => Guard(() =>
    {
        var uks = Store;
        Thought ancestor = uks.Labeled(In(pAncestor).Trim()) ?? uks.Labeled("Thought");
        var query = new Thought { Label = "theQuery" };
        try
        {
            foreach (string line in In(pAttributes).Split('\n'))
            {
                string[] parts = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length < 2) continue;
                Thought linkType = uks.CreateThoughtFromMultipleAttributes(parts[0], true) ?? throw new ArgumentException($"{parts[0]} not found");
                Thought target = uks.CreateThoughtFromMultipleAttributes(parts[1], false) ?? throw new ArgumentException($"{parts[1]} not found");
                if (linkType.Label == "can")
                {
                    target.AddParent("Action");
                    target.RemoveParent("Unknown");
                }
                float conf = .9f;
                if (parts.Length > 2) float.TryParse(parts[2], out conf);
                Link l = query.AddLink(linkType, target);
                if (l != null) l.Weight = conf;
            }
            var hits = uks.SearchByAttributes(query, ancestor)
                .Select(h => new ScoredDto { Label = Format(h.t), Confidence = h.conf }).ToList();
            return Json(hits, JsonCtx.Default.ListScoredDto);
        }
        finally
        {
            query.Delete();
        }
    });

    // ---------- files ----------

    // UKS save/load uses XmlSerializer, which under NativeAOT runs in reflection mode and
    // needs these closed generic collections (Add, indexer, ctor) compiled and reflectable.
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<UKS.UKS.sThought>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UKS.UKS.sThought))]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Serialized types are preserved above.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Serialized types are preserved above.")]
    static void PreserveXmlTypes() { }

    /// <summary>
    /// Only the snapshot is taken under the lock; writing the XML (most of the time for a
    /// large UKS) happens outside it, so the engine and the UI keep running meanwhile.
    /// Callers should still call this off the main thread.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_save_xml")]
    public static IntPtr SaveXml(IntPtr pPath)
    {
        string path = In(pPath);
        List<UKS.UKS.sThought> snapshot = null;
        IntPtr failure = Guard(() =>
        {
            PreserveXmlTypes();
            BrainSimulator.MainWindow.theWindow.SetupBeforeSave();
            snapshot = Store.CaptureSaveSnapshot();
            return IntPtr.Zero;
        });
        if (snapshot is null) return failure;
        lock (saveGate)   // two saves to the same file must not interleave
        {
            try
            {
                return UKS.UKS.WriteSaveSnapshot(snapshot, path) ? Ok("Saved") : Error(new IOException("Save failed."));
            }
            catch (Exception e) { return Error(e); }
        }
    }

    static readonly object saveGate = new();

    [UnmanagedCallersOnly(EntryPoint = "uks_load_xml")]
    public static IntPtr LoadXml(IntPtr pPath) => Guard(() =>
    {
        PreserveXmlTypes();
        BeforeLoad();
        bool loaded = Store.LoadUKSfromXMLFile(In(pPath));
        AfterLoad();
        return loaded
            ? Ok($"Loaded {Store.AtomicThoughts.Count} Thoughts")
            : throw new IOException("Load failed (file missing or not a UKS XML file).");
    });

    [UnmanagedCallersOnly(EntryPoint = "uks_import_text")]
    public static IntPtr ImportText(IntPtr pPath) => Guard(() =>
    {
        Store.ImportTextFile(In(pPath));
        return Ok($"Imported; {Store.AtomicThoughts.Count} Thoughts");
    });

    [UnmanagedCallersOnly(EntryPoint = "uks_export_text")]
    public static IntPtr ExportText(IntPtr pRoot, IntPtr pPath) => Guard(() =>
    {
        Store.ExportTextFile(In(pRoot), In(pPath));
        return Ok("Exported");
    });
}

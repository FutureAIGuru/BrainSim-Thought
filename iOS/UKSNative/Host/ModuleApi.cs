// The logic side of the module dialogs. Each desktop *Dlg.xaml.cs mixed two things: what a
// dialog shows and does (layout, which is SwiftUI's job on iOS) and the work behind it
// (calling the module, querying the UKS, background jobs). A ModuleApi is only the second
// part: it reports a module's state as plain data and performs named operations on it. It
// knows nothing about labels, controls, layout, colours or coordinates; each module's
// SwiftUI view (BrainSimThought/Modules/Dialogs) decides how to present and drive it.
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace BrainSimulator.Modules;

public sealed class ModuleInfoDto
{
    public string Label { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Enabled { get; set; }
    public int Revision { get; set; }
}

public sealed class NoticeDto
{
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>The result of an operation that only reports back a message.</summary>
public sealed class MessageDto
{
    public string Message { get; set; }
}

/// <summary>The arguments of an operation: a JSON object from the app (missing or empty means none).</summary>
public readonly struct ApiArgs
{
    readonly JsonElement root;

    public ApiArgs(string json)
    {
        root = string.IsNullOrWhiteSpace(json) ? default : JsonDocument.Parse(json).RootElement.Clone();
    }

    bool TryGet(string name, out JsonElement value)
    {
        value = default;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null;
    }

    public string Str(string name, string fallback = "") =>
        !TryGet(name, out var v) ? fallback : v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : v.GetRawText();

    public double Num(string name, double fallback = 0)
    {
        if (!TryGet(name, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        return double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : fallback;
    }

    public int Int(string name, int fallback = 0) => (int)Math.Round(Num(name, fallback));

    public bool Bool(string name, bool fallback = false)
    {
        if (!TryGet(name, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => v.GetString() is "1" or "true",
            JsonValueKind.Number => v.GetDouble() != 0,
            _ => fallback,
        };
    }
}

/// <summary>A module's logic interface for the app. One instance per active module.</summary>
public abstract class ModuleApi
{
    /// <summary>The module's state as JSON (read-only; called on a timer).</summary>
    public abstract string State(ModuleBase module);

    /// <summary>Other read-only questions ("picture", "attributes", "suggest"...), as JSON.</summary>
    public virtual string Query(ModuleBase module, string op, ApiArgs args) =>
        op == "state" ? State(module) : throw new ArgumentException($"Unknown query '{op}'.");

    /// <summary>Performs an operation; returns its result as JSON.</summary>
    public virtual string Call(ModuleBase module, string op, ApiArgs args) =>
        throw new ArgumentException($"Unknown operation '{op}'.");

    // ---- helpers ----

    protected static string Json<T>(T value, JsonTypeInfo<T> info) => JsonSerializer.Serialize(value, info);
    protected static string Message(string message) => Json(new MessageDto { Message = message }, ApiJson.Default.MessageDto);
    protected static string Done() => Message(null);
    protected static string Unknown(string op) => throw new ArgumentException($"Unknown operation '{op}'.");

    /// <summary>Thought labels starting with `prefix` (case-insensitive), for autocomplete.</summary>
    protected static List<string> Complete(string prefix, IEnumerable<string> candidates, int max = 6)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return new();
        return candidates
            .Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !c.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    protected static string Format(UKS.Thought t) => t switch
    {
        null => null,
        UKS.Link l => $"[{Format(l.From) ?? "?"} → {l.LinkType?.Label ?? "?"} → {Format(l.To) ?? "?"}]",
        _ => string.IsNullOrWhiteSpace(t.Label) ? t.ToString() : t.Label,
    };

    /// <summary>Text of the last background job, and whether one is running (see <see cref="Background"/>).</summary>
    protected volatile string status;
    protected volatile bool busy;

    /// <summary>
    /// Runs long module work on a background thread, like the desktop's Task.Run handlers.
    /// It holds the UKS lock, so it can't race the engine; the app's polling doesn't wait for
    /// the lock, so the screen stays responsive meanwhile. `status` carries the outcome.
    /// </summary>
    protected void Background(Func<string> work)
    {
        busy = true;
        status = null;
        UKSNative.Exports.RunLocked(() =>
        {
            try { status = work(); }
            catch (Exception e) { status = "Error: " + e.Message; }
            finally { busy = false; }
        });
    }

    /// <summary>
    /// The desktop fires async module calls and forgets them, so failures vanish; here they
    /// are reported through `report` (and as an alert) instead.
    /// </summary>
    protected static void Watch(Task task, Action<string> report = null) =>
        task.ContinueWith(t =>
        {
            string message = t.Exception?.GetBaseException().Message ?? "failed";
            report?.Invoke("Error: " + message);
            HostNotices.Post("Module error", message);
        }, TaskContinuationOptions.OnlyOnFaulted);
}

/// <summary>Modules whose desktop dialog has nothing to show or do (or that have none).</summary>
public sealed class NoApi : ModuleApi
{
    public override string State(ModuleBase module) => "{}";
}

public static class ModuleApis
{
    public static ModuleApi For(ModuleBase module) => module switch
    {
        ModuleTextIn or ModuleText => new TextApi<ModuleText>(
            (m, s) => (m.SubmitText(s), m.LastRelationship, m.LastStatus), ModuleText.GetBestMeaning, t => ModuleText.GetBestWordFor(t),
            (m, path) => { int n = m.LoadTextFromFile(path, 1000); return TextApi<ModuleText>.LoadStatus(n, m.LastStatus, m.LastRetainedActionExemplarCount, m.LastLoadedActionExemplarCount, m.LastMissingActionExemplars); },
            m => m.CancelIncrementalLoad()),
        ModuleTextNEW => new TextApi<ModuleTextNEW>(
            (m, s) => (m.SubmitText(s), m.LastRelationship, m.LastStatus), ModuleTextNEW.GetBestMeaning, t => ModuleTextNEW.GetBestWordFor(t),
            (m, path) => { int n = m.LoadTextFromFile(path, 1000); return TextApi<ModuleTextNEW>.LoadStatus(n, m.LastStatus, m.LastRetainedActionExemplarCount, m.LastLoadedActionExemplarCount, m.LastMissingActionExemplars); },
            m => m.CancelIncrementalLoad()),
        ModuleAction => new ActionApi(),
        ModuleAgents => new AgentsApi(),
        ModuleAddCounts or ModuleAttributeBubble or ModuleBalanceTree or ModuleClassCreate or ModuleRemoveRedundancy => new RunOnceAgentApi(),
        ModuleWellBeing => new WellBeingApi(),
        ModuleAttention => new AttentionApi(),
        ModuleSoundOut => new SoundOutApi(),
        ModuleSoundIn => new SoundInApi(),
        ModuleStressTest => new StressTestApi(),
        ModuleLearnMelody => new LearnMelodyApi(),
        ModuleSequenceSegmenter => new SequenceSegmenterApi(),
        ModuleAddToMentalModel => new AddToMentalModelApi(),
        ModuleOnlineInfo => new OnlineInfoApi(),
        ModuleVisualInput => new VisualInputApi(),
        ModuleWord => new WordApi(),
        ModuleAlgorithm => new AlgorithmApi(),
        ModuleGPTInfo => new GPTInfoApi(),
        ModuleSequenceEditor => new SequenceEditorApi(),
        ModuleMentalModel => new MentalModelApi(),
        ModuleShowGraph => new ShowGraphApi(),
        ModuleVision => new VisionApi(),
        ModuleObjectRecognizer => new ObjectRecognizerApi(),
        _ => new NoApi(),
    };

    internal static T Active<T>() where T : ModuleBase => MainWindow.theWindow.activeModules.OfType<T>().FirstOrDefault();
}

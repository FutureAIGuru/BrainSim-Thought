using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrainSimulator;
using BrainSimulator.Modules;

namespace UKSNative;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ModuleInfoDto>))]
[JsonSerializable(typeof(List<NoticeDto>))]
internal partial class HostJsonCtx : JsonSerializerContext { }

/// <summary>
/// C ABI for the module engine: the desktop MainWindow's active-module list, its
/// engine timer, and each module's logic interface (a ModuleApi: state and operations;
/// the app's SwiftUI views are the dialogs).
/// </summary>
public static partial class Exports
{
    static readonly Dictionary<string, ModuleApi> apis = new();
    static Thread engineThread;
    static volatile int engineIntervalMs;
    static long engineTicks;

    static MainWindow Host => MainWindow.theWindow;

    static ModuleApi ApiFor(ModuleBase module)
    {
        if (!apis.TryGetValue(module.Label, out var api))
            apis[module.Label] = api = ModuleApis.For(module);
        return api;
    }

    static ModuleBase RequireModule(string label) =>
        Host.GetModuleByLabel(label) ?? throw new ArgumentException($"No active module '{label}'.");

    /// <summary>Desktop MainWindow.CreateEmptyUKS: initial structure plus the mandatory UKS modules.</summary>
    static Exports() => AotContractResolver.Install();

    internal static void CreateEmptyUKS()
    {
        foreach (ModuleBase m in Host.activeModules) m.Closing();
        Host.activeModules.Clear();
        apis.Clear();
        Store.CreateInitialStructure();
        Host.UpdateModuleListsInUKS();
        Host.InsertMandatoryModules();
        foreach (ModuleBase m in Host.activeModules) m.SetUpAfterLoad();
    }

    /// <summary>Desktop MainWindow.LoadFile, after the XML is read.</summary>
    internal static void AfterLoad()
    {
        apis.Clear();
        if (Store.Labeled("BrainSim") is null) { CreateEmptyUKS(); return; }
        Host.UpdateModuleListsInUKS();
        Host.LoadActiveModules();
    }

    /// <summary>MainWindow.UnloadActiveModules: drop the current module Thoughts so the file's take over.</summary>
    internal static void BeforeLoad()
    {
        foreach (ModuleBase m in Host.activeModules) m.Closing();
        Host.activeModules.Clear();
        var parent = Store.Labeled("ActiveModule");
        if (parent is null) return;
        foreach (var t in parent.Children.ToList())
        {
            foreach (var r in t.LinksTo.ToList())
            {
                t.RemoveLink(r);
                r.To?.Delete();
            }
            t.Delete();
        }
    }

    // ---------- host setup ----------

    /// <summary>Where bundled content (UKSContent, WordFIles, Resources) lives; relative paths resolve here.</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_set_content_root")]
    public static void SetContentRoot(IntPtr pPath)
    {
        string path = In(pPath);
        if (Directory.Exists(path)) Directory.SetCurrentDirectory(path);
    }

    /// <summary>app.config appSettings (e.g. APIKey for the OpenAI-backed modules).</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_set_setting")]
    public static void SetSetting(IntPtr pKey, IntPtr pValue)
    {
        string key = In(pKey), value = In(pValue);
        if (value == "") System.Configuration.ConfigurationManager.AppSettings.Remove(key);
        else System.Configuration.ConfigurationManager.AppSettings[key] = value;
    }

    /// <summary>Receives NAudio-style short MIDI messages (status | data1 &lt;&lt; 8 | data2 &lt;&lt; 16).</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_set_midi_callback")]
    public static unsafe void SetMidiCallback(delegate* unmanaged<int, void> callback) =>
        NAudio.Midi.MidiOut.Sink = callback;

    /// <summary>
    /// Image decoding for ModuleVision (System.Drawing.Bitmap): decode(path, rgbaBuffer, capacity, &amp;width, &amp;height).
    /// Called first with a null buffer for the size. Returns 1 on success.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_set_image_decoder")]
    public static unsafe void SetImageDecoder(delegate* unmanaged<IntPtr, byte*, int, int*, int*, int> decoder) =>
        System.Drawing.Bitmap.Decoder = decoder;

    /// <summary>MessageBox.Show calls and similar notices raised since the last call.</summary>
    /// <remarks>Lock-free: the queue is concurrent, and the app polls this from the main thread.</remarks>
    [UnmanagedCallersOnly(EntryPoint = "uks_notices")]
    public static IntPtr Notices()
    {
        var list = new List<NoticeDto>();
        while (HostNotices.Pending.TryDequeue(out var n)) list.Add(new NoticeDto { Title = n.Title, Text = n.Text });
        return Json(list, HostJsonCtx.Default.ListNoticeDto);
    }

    // ---------- engine ----------

    /// <summary>Starts (or retunes) the engine thread that fires every active module, like the desktop's 10 ms timer. 0 stops it.</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_engine_run")]
    public static void EngineRun(int intervalMs)
    {
        engineIntervalMs = Math.Max(0, intervalMs);
        if (engineIntervalMs == 0 || engineThread is not null) return;
        engineThread = new Thread(() =>
        {
            while (engineIntervalMs > 0)
            {
                lock (gate)
                {
                    try { UseShared(); Host.Tick(); }
                    catch (Exception e) { HostNotices.Post("Engine", e.Message); }
                }
                Interlocked.Increment(ref engineTicks);
                Thread.Sleep(engineIntervalMs);
            }
            engineThread = null;
        }) { IsBackground = true, Name = "BrainSim engine" };
        engineThread.Start();
    }

    /// <summary>Engine cycles so far; the UI refreshes when it changes.</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_engine_ticks")]
    public static long EngineTicks() => Interlocked.Read(ref engineTicks);

    // ---------- modules ----------

    [UnmanagedCallersOnly(EntryPoint = "uks_modules_available")]
    public static IntPtr ModulesAvailable() => Guard(() =>
        Json(ModuleRegistry.Names.OrderBy(n => n).ToList(), JsonCtx.Default.ListString));

    [UnmanagedCallersOnly(EntryPoint = "uks_modules_active")]
    public static IntPtr ModulesActive() => PollGuard(() =>
        Json(Host.activeModules.OrderBy(m => m.Label).Select(m => new ModuleInfoDto
        {
            Label = m.Label,
            Type = ModuleRegistry.TypeOf(m),
            Enabled = m.isEnabled,
            Revision = m.DialogRevision,
        }).ToList(), HostJsonCtx.Default.ListModuleInfoDto));

    [UnmanagedCallersOnly(EntryPoint = "uks_module_activate")]
    public static IntPtr ModuleActivate(IntPtr pType) => Guard(() => Ok(Host.ActivateModule(In(pType))));

    [UnmanagedCallersOnly(EntryPoint = "uks_module_remove")]
    public static IntPtr ModuleRemove(IntPtr pLabel) => Guard(() =>
    {
        string label = In(pLabel);
        Host.DeactivateModule(label);
        apis.Remove(label);
        return Ok($"Removed {label}");
    });

    [UnmanagedCallersOnly(EntryPoint = "uks_module_set_enabled")]
    public static IntPtr ModuleSetEnabled(IntPtr pLabel, int enabled) => Guard(() =>
    {
        RequireModule(In(pLabel)).isEnabled = enabled != 0;
        return Ok();
    });

    /// <summary>Re-runs the module's Initialize(), like the desktop context menu's "Initialize".</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_module_initialize")]
    public static IntPtr ModuleInitialize(IntPtr pLabel) => Guard(() =>
    {
        var m = RequireModule(In(pLabel));
        m.Initialize();
        m.UpdateDialog();
        return Ok("Initialized");
    });

    /// <summary>
    /// A read-only question to a module: "state" (polled by its view), or another its API
    /// answers ("picture", "attributes"…). `args` is a JSON object, or empty. Doesn't wait
    /// for long work: returns "busy" instead, and the app keeps what it has.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_module_query")]
    public static IntPtr ModuleQuery(IntPtr pLabel, IntPtr pOp, IntPtr pArgs) => PollGuard(() =>
    {
        var m = RequireModule(In(pLabel));
        return Out(ApiFor(m).Query(m, In(pOp), new ApiArgs(In(pArgs))));
    });

    /// <summary>Performs a module operation ("present", "turn", "teach"…) with JSON arguments; returns its JSON result.</summary>
    [UnmanagedCallersOnly(EntryPoint = "uks_module_call")]
    public static IntPtr ModuleCall(IntPtr pLabel, IntPtr pOp, IntPtr pArgs) => Guard(() =>
    {
        var m = RequireModule(In(pLabel));
        return Out(ApiFor(m).Call(m, In(pOp), new ApiArgs(In(pArgs))));
    });
}

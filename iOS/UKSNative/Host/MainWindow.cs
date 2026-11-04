// Headless stand-in for the desktop MainWindow: owns the active module instances and
// runs the engine tick. Mirrors MainWindow.xaml.cs / MainWindowFiles.cs without WPF.
using BrainSimulator.Modules;
using UKS;

namespace BrainSimulator;

public partial class MainWindow
{
    public List<ModuleBase> activeModules = new();
    // like the desktop's: setting it selects that UKS as the current one
    public static UKS.UKS theUKS
    {
        get => UKS.UKS.theUKS;
        set => UKS.UKS.theUKS = value;
    }
    public static MainWindow theWindow = new();
    public static string currentFileName = "";

    static int suspended;
    public static void SuspendEngine() => Interlocked.Increment(ref suspended);
    public static void ResumeEngine() => Interlocked.Decrement(ref suspended);
    public static bool EngineSuspended => Volatile.Read(ref suspended) > 0;

    public ModuleBase GetModuleByLabel(string label) => activeModules.FirstOrDefault(x => x.Label == label);

    /// <summary>Mirrors MainWindow.UpdateModuleListsInUKS for the modules compiled into this app.</summary>
    public void UpdateModuleListsInUKS()
    {
        theUKS.GetOrAddThought("BrainSim", null);
        theUKS.GetOrAddThought("AvailableModule", "BrainSim");
        theUKS.GetOrAddThought("ActiveModule", "BrainSim");
        foreach (string name in ModuleRegistry.Names)
            theUKS.GetOrAddThought(name, "AvailableModule");
        foreach (Thought t in theUKS.Labeled("AvailableModule").Children.ToList())
            if (!ModuleRegistry.Names.Contains(t.Label)) { theUKS.DeleteAllChildrenAndLinks(t); t.Delete(); }
    }

    /// <summary>Recreates module instances from the ActiveModule Thoughts (after a file load).</summary>
    public void LoadActiveModules()
    {
        foreach (ModuleBase m in activeModules) m.Closing();
        activeModules.Clear();
        Thought parent = theUKS.Labeled("ActiveModule");
        if (parent is null) return;
        foreach (Thought t in parent.Children.OrderBy(x => x.Label).ToList())
        {
            Thought type = t.Parents.FirstOrDefault(x => x.HasAncestor("AvailableModule") && x.Label != "ActiveModule");
            ModuleBase mod = type is null ? null : ModuleRegistry.Create(type.Label);
            if (mod is null) { parent.RemoveChild(t); continue; }
            mod.Label = t.Label;
            activeModules.Add(mod);
        }
        foreach (ModuleBase m in activeModules) m.SetUpAfterLoad();
    }

    public string ActivateModule(string moduleType)
    {
        ModuleBase newModule = ModuleRegistry.Create(moduleType) ?? throw new ArgumentException($"Unknown module '{moduleType}'.");
        Thought t = theUKS.GetOrAddThought(moduleType, "AvailableModule");
        t = theUKS.CreateInstanceOf(theUKS.Labeled(moduleType));
        t.AddParent(theUKS.Labeled("ActiveModule"));
        newModule.Label = t.Label;
        activeModules.Add(newModule);
        newModule.OpenDlg();
        // ModuleUKS broadcasts UKSInitialized once, on its first Fire. A module added after
        // that (e.g. ModuleMentalModel, which builds its cells there) gets it now instead.
        if (activeModules.Any(m => m is ModuleUKS && m.initialized))
            newModule.UKSInitializedNotification();
        return t.Label;
    }

    public void DeactivateModule(string label)
    {
        ModuleBase mod = GetModuleByLabel(label);
        if (mod is not null) { mod.Closing(); activeModules.Remove(mod); }
        Thought t = theUKS.Labeled(label);
        if (t is not null) { theUKS.DeleteAllChildrenAndLinks(t); t.Delete(); }
    }

    public void InsertMandatoryModules()
    {
        ActivateModule("ModuleUKS");
        ActivateModule("ModuleUKSStatement");
        ActivateModule("ModuleUKSQuery");
    }

    public void SetupBeforeSave()
    {
        foreach (ModuleBase m in activeModules) m.SetUpBeforeSave();
    }

    readonly Dictionary<string, string> lastError = new();

    /// <summary>THE MAIN ENGINE LOOP: fires every active, enabled module once.</summary>
    /// <remarks>
    /// On the desktop an exception in Fire() escapes to the WPF dispatcher. Here one failing
    /// module does not stop the others, and each distinct error is reported once.
    /// </remarks>
    public void Tick()
    {
        if (EngineSuspended) return;
        Thought parent = theUKS.Labeled("ActiveModule");
        if (parent is null) return;
        foreach (Thought module in parent.Children)
        {
            ModuleBase mb = GetModuleByLabel(module.Label);
            if (mb is null || !mb.isEnabled) continue;
            try
            {
                mb.Fire();
                lastError.Remove(mb.Label);
            }
            catch (Exception e)
            {
                string message = $"{e.GetType().Name}: {e.Message}";
                if (lastError.TryGetValue(mb.Label, out var previous) && previous == message) continue;
                lastError[mb.Label] = message;
                HostNotices.Post(mb.Label, message + "\n" + e.StackTrace?.Split('\n').FirstOrDefault()?.Trim());
            }
        }
    }
}

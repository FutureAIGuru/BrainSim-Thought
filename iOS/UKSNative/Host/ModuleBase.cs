// Headless replacement for BrainSimulator/Modules/ModuleBase.cs.
// The desktop base class owns a WPF dialog; on iOS the dialog is a SwiftUI view that
// reads module state through the C ABI, so everything window-related is dropped and
// UpdateDialog() just bumps a revision counter the UI polls.
using UKS;

namespace BrainSimulator.Modules;

public abstract class ModuleBase
{
    public bool initialized = false;
    public bool isEnabled = true;
    public string Label = "";
    public bool dlgIsOpen = false;
    public UKS.UKS theUKS = null;

    /// <summary>Stand-in for the desktop dialog reference; Draw() just asks the SwiftUI view to refresh.</summary>
    protected readonly DialogHandle dlg;
    public sealed class DialogHandle
    {
        readonly ModuleBase owner;
        internal DialogHandle(ModuleBase owner) => this.owner = owner;
        public bool Draw(bool checkDrawTimer) { owner.UpdateDialog(); return true; }
    }

    /// <summary>Incremented whenever the module asks its dialog to redraw.</summary>
    public int DialogRevision;

    protected ModuleBase()
    {
        dlg = new DialogHandle(this);
        string moduleName = GetType().Name;
        if (moduleName.StartsWith("Module")) Label = moduleName[6..];
        theUKS = MainWindow.theUKS;
    }

    public abstract void Fire();
    public abstract void Initialize();

    public virtual void UKSInitializedNotification() { }
    public void UKSInitialized()
    {
        foreach (ModuleBase module in MainWindow.theWindow.activeModules)
        {
            module.theUKS = MainWindow.theUKS;
            if (module.isEnabled) module.UKSInitializedNotification();
        }
    }

    public virtual void UKSReloadedNotification() { }
    public void UKSReloaded()
    {
        foreach (ModuleBase module in MainWindow.theWindow.activeModules)
        {
            module.theUKS = MainWindow.theUKS;
            if (module.isEnabled) module.UKSReloadedNotification();
        }
    }

    // As on the desktop: a module keeps the UKS it was given, and makes it the current one.
    public void GetUKS()
    {
        theUKS ??= MainWindow.theUKS;
        UKS.UKS.theUKS = theUKS;
    }

    protected void Init(bool forceInit = false)
    {
        if (initialized && !forceInit) return;
        initialized = true;
        Initialize();
        UpdateDialog();
    }

    public void OpenDlg() => SetSavedDlgAttribute("Open", "True");
    public void CloseDlg() => SetSavedDlgAttribute("Open", "");
    public virtual void ShowDialog() { }

    public string GetSavedDlgAttribute(string attribName)
    {
        Thought thisDlg = theUKS?.Labeled(Label);
        if (thisDlg is null) return null;
        foreach (var r in thisDlg.LinksTo)
            if (r.LinkType?.Label == "hasAttribute" && r.To is not null && r.To.Label.StartsWith(attribName))
                return r.To.V as string;
        return null;
    }

    public void SetSavedDlgAttribute(string attribName, string attribValue)
    {
        if (string.IsNullOrEmpty(attribName)) return;
        Thought thisDlg = theUKS?.Labeled(Label);
        if (thisDlg is null) return;
        foreach (var r in thisDlg.LinksTo)
        {
            if (r.LinkType?.Label == "hasAttribute" && r.To is not null && r.To.Label.StartsWith(attribName))
            {
                if (attribValue is null) { r.To.Delete(); return; }
                r.To.V = attribValue;
                return;
            }
        }
        Thought dlgAttribParent = theUKS.GetOrAddThought("DlgAttrib", "BrainSim");
        Thought dlgInfo = theUKS.AddThought(attribName, dlgAttribParent);
        Thought hasAttribute = theUKS.GetOrAddThought("hasAttribute", "LinkType");
        thisDlg.AddLink(hasAttribute, dlgInfo);
        dlgInfo.V = attribValue;
        dlgInfo.Fire();
    }

    public TimeSpan DialogLockSpan = TimeSpan.FromMilliseconds(500);
    public void UpdateDialog() => Interlocked.Increment(ref DialogRevision);

    public virtual void SetUpBeforeSave() { }
    public virtual void SetUpAfterLoad() { }
    public virtual void Closing() { }
    public virtual void SizeChanged() { }
}

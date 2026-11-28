// UKS.Tests compiles ModuleInterpret.cs (and ModuleWord.Spelling.cs) without the WPF host.
// The real ModuleBase depends on MainWindow and WPF dialogs; this supplies only the members
// those modules use. Tests always assign theUKS explicitly.
namespace BrainSimulator.Modules;

public abstract class ModuleBase
{
    public bool initialized;
    public string Label = "";
    public UKS.UKS theUKS;

    public abstract void Fire();

    public abstract void Initialize();

    public virtual void UKSInitializedNotification()
    {
    }

    public void GetUKS()
    {
    }

    protected void Init(bool forceInit = false)
    {
        if (initialized && !forceInit) return;
        initialized = true;
        Initialize();
    }
}

// UKS.Tests compiles ModuleAlgorithm.ContextResolver.cs on its own, without the rest of the
// module (ModuleBase and the WPF dialog). This supplies the one member it uses.
namespace BrainSimulator.Modules;

public partial class ModuleAlgorithm
{
    public UKS.UKS theUKS { get; set; }
}

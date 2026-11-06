// Explicit module factory. The desktop app finds module types by reflection
// (Utils.GetListOfExistingCSharpModuleTypes); NativeAOT needs them spelled out.
namespace BrainSimulator.Modules;

public static class ModuleRegistry
{
    static readonly Dictionary<string, Func<ModuleBase>> factories = new()
    {
        ["ModuleAction"] = () => new ModuleAction(),
        ["ModuleAddCounts"] = () => new ModuleAddCounts(),
        ["ModuleAddToMentalModel"] = () => new ModuleAddToMentalModel(),
        ["ModuleAgents"] = () => new ModuleAgents(),
        ["ModuleAlgorithm"] = () => new ModuleAlgorithm(),
        ["ModuleAttention"] = () => new ModuleAttention(),
        ["ModuleAttentionS"] = () => new ModuleAttentionS(),
        ["ModuleAttributeBubble"] = () => new ModuleAttributeBubble(),
        ["ModuleBalanceTree"] = () => new ModuleBalanceTree(),
        ["ModuleClassCreate"] = () => new ModuleClassCreate(),
        ["ModuleEmpty"] = () => new ModuleEmpty(),
        ["ModuleGPTInfo"] = () => new ModuleGPTInfo(),
        ["ModuleLearnMelody"] = () => new ModuleLearnMelody(),
        ["ModuleMentalModel"] = () => new ModuleMentalModel(),
        ["ModuleObjectRecognizer"] = () => new ModuleObjectRecognizer(),
        ["ModuleOnlineInfo"] = () => new ModuleOnlineInfo(),
        ["ModuleRemoveRedundancy"] = () => new ModuleRemoveRedundancy(),
        ["ModuleSequenceEditor"] = () => new ModuleSequenceEditor(),
        ["ModuleSequenceSegmenter"] = () => new ModuleSequenceSegmenter(),
        ["ModuleShowGraph"] = () => new ModuleShowGraph(),
        ["ModuleSoundIn"] = () => new ModuleSoundIn(),
        ["ModuleSoundOut"] = () => new ModuleSoundOut(),
        ["ModuleStressTest"] = () => new ModuleStressTest(),
        ["ModuleText"] = () => new ModuleText(),
        ["ModuleTextIn"] = () => new ModuleTextIn(),
        ["ModuleTextNEW"] = () => new ModuleTextNEW(),
        ["ModuleUKS"] = () => new ModuleUKS(),
        ["ModuleUKSQuery"] = () => new ModuleUKSQuery(),
        ["ModuleUKSStatement"] = () => new ModuleUKSStatement(),
        ["ModuleVision"] = () => new ModuleVision(),
        ["ModuleVisualInput"] = () => new ModuleVisualInput(),
        ["ModuleWellBeing"] = () => new ModuleWellBeing(),
        ["ModuleWord"] = () => new ModuleWord(),
    };

    public static IReadOnlyCollection<string> Names => factories.Keys;
    /// <summary>The module classes (each made once to learn its type).</summary>
    public static IReadOnlyList<Type> Types => types ??= factories.Values.Select(f => f().GetType()).ToList();
    static List<Type> types;
    public static string TypeOf(ModuleBase module) => module.GetType().Name;
    public static ModuleBase Create(string type) => factories.TryGetValue(type, out var f) ? f() : null;
}

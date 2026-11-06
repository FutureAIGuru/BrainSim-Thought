// Newtonsoft.Json decides whether a type is "string-like" through TypeDescriptor, which
// under NativeAOT reports a string converter for plain classes, so API responses in
// ModuleOnlineInfo / GPT failed with "Unexpected character ... {". This resolver keeps
// plain classes as objects; it is installed as JsonConvert.DefaultSettings at startup.
using System.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BrainSimulator;

sealed class AotContractResolver : DefaultContractResolver
{
    protected override JsonContract CreateContract(Type objectType)
    {
        JsonContract contract = base.CreateContract(objectType);
        if (contract is JsonStringContract && IsPlainClass(objectType))
            return CreateObjectContract(objectType);
        return contract;
    }

    static bool IsPlainClass(Type t) =>
        t.IsClass && t != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(t) &&
        t.GetConstructor(Type.EmptyTypes) is not null;

    internal static void Install() =>
        JsonConvert.DefaultSettings = () => new JsonSerializerSettings { ContractResolver = new AotContractResolver() };
}

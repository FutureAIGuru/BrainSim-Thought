using System.IO;
using System.Runtime.CompilerServices;
using BrainSimulator.Modules;

namespace UKS.Tests;

/// <summary>
/// Experiment A (ambiguity): with the environment variable INTERPRET_AMBIGUITY=1, every
/// ModuleInterpret in the test run also loads the classes WordNet gives BasicWords' own words
/// (w:dog is also a verb, w:black a noun, w:in an adverb), taken from their section of
/// WordNetWords.txt. Run the ModuleInterpret tests with and without it and compare.
/// </summary>
internal static class InterpretAmbiguityExperiment
{
    private const string Start = "# Classes WordNet gives words BasicWords defines (w:dog is also a verb). BEGIN";
    private const string End = "# Classes WordNet gives words BasicWords defines. END";

    [ModuleInitializer]
    internal static void Enable()
    {
        if (System.Environment.GetEnvironmentVariable("INTERPRET_AMBIGUITY") != "1") return;
        string source = Path.Combine(System.AppContext.BaseDirectory, "UKSContent", "WordNetWords.txt");
        string[] lines = File.ReadAllLines(source);
        int start = System.Array.IndexOf(lines, Start), end = System.Array.IndexOf(lines, End);
        string section = Path.Combine(System.AppContext.BaseDirectory, "UKSContent", "WordNetAmbiguity.generated.txt");
        File.WriteAllLines(section, lines[(start + 1)..end]);
        ModuleInterpret.ExtraVocabularyFiles.Add(section);
    }
}

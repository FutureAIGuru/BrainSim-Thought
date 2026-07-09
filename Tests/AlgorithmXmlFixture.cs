/*
 * Shared Algorithm.xml snapshot for ModuleAlgorithmTests (loads once per test class).
 */

using System;
using System.Collections.Generic;
using System.IO;
using BrainSimulator.Modules;
using UKS;

namespace UKS.Tests;

public sealed class AlgorithmXmlFixture
{
    private readonly List<UKS.sThought> snapshot;

    public AlgorithmXmlFixture()
    {
        string xmlPath = FindAlgorithmXmlPath();
        if (!File.Exists(xmlPath))
        {
            throw new FileNotFoundException($"Algorithm.xml not found at {xmlPath}");
        }

        snapshot = UKS.DeserializeUkTempFromXmlFile(xmlPath);
    }

    public (UKS uks, ModuleAlgorithm module) CreateHarness()
    {
        var uks = new UKS(clear: true);
        UKS.theUKS = uks;
        Thought.ClearRecentlyFiredQueue();

        uks.RestoreFromUkTempSnapshot(CloneSnapshot(snapshot));

        var module = new ModuleAlgorithm
        {
            theUKS = uks
        };
        module.UKSInitializedNotification();

        return (uks, module);
    }

    private static List<UKS.sThought> CloneSnapshot(IReadOnlyList<UKS.sThought> source)
    {
        var clone = new List<UKS.sThought>(source.Count);
        foreach (UKS.sThought st in source)
        {
            clone.Add(new UKS.sThought
            {
                index = st.index,
                label = st.label,
                source = st.source,
                linkType = st.linkType,
                target = st.target,
                weight = st.weight,
                V = st.V,
            });
        }

        return clone;
    }

    private static string FindAlgorithmXmlPath()
    {
        foreach (string candidate in CandidatePaths())
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(
            "Algorithm.xml not found. Expected next to test output under UKSContent/Algorithm.xml " +
            "or in BrainSimulator/UKSContent under the repository root.");
    }

    private static IEnumerable<string> CandidatePaths()
    {
        string outputDir = AppContext.BaseDirectory;
        yield return Path.Combine(outputDir, "UKSContent", "Algorithm.xml");
        yield return Path.Combine(outputDir, "UKSContent", "algorithm.xml");

        string? repoRoot = FindRepositoryRoot();
        if (repoRoot is not null)
        {
            string contentDir = Path.Combine(repoRoot, "BrainSimulator", "UKSContent");
            yield return Path.Combine(contentDir, "Algorithm.xml");
            yield return Path.Combine(contentDir, "algorithm.xml");
        }
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BrainSim Thought.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
/*
 * Regression tests for P2 non-UKS fixes (2026-07-07).
 */

using System.IO;
using UKS;
using Xunit;

namespace UKS.Tests;

public class NonUksP2FixesRegressionTests
{
    [Fact]
    public void Thought_GetTargetOfFirstLinkOfType_string_overload()
    {
        var uks = new global::UKS.UKS(clear: true);
        uks.CreateInitialStructure();
        Thought parent = uks.GetOrAddThought("phrase1", "Object");
        Thought linkType = uks.GetOrAddThought("soundAs", "LinkType");
        var seq = uks.AddSequenceAndLink(parent, linkType, new List<Thought>
        {
            uks.GetOrAddThought("note1", "Object"),
            uks.GetOrAddThought("note2", "Object"),
        });
        Assert.NotNull(seq);
        Thought target = parent.GetTargetOfFirstLinkOfType("soundAs");
        Assert.Same(seq, target);
    }

    [Fact]
    public void Bundled_wordlist_resource_exists()
    {
        string repoRoot = FindRepoRoot();
        string wordlist = Path.Combine(repoRoot, "BrainSimulator", "Resources", "wordlist.txt");
        Assert.True(File.Exists(wordlist));
    }

    //[Fact]
    //public void Legacy_sources_moved_to_archive()
    //{
    //    string repoRoot = FindRepoRoot();
    //    Assert.True(File.Exists(Path.Combine(repoRoot, "BrainSimulator", "archive", "legacy", "XmlFile.cs")));
    //    Assert.False(File.Exists(Path.Combine(repoRoot, "BrainSimulator", "XmlFile.cs")));
    //}

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BrainSim Thought.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("BrainSim Thought repo root not found.");
    }
}
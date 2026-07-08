/*
 * Regression tests for remaining P1 (#9-10) and P3 (#19-23) non-UKS fixes (2026-07-07).
 */

using System.Reflection;
using System.Threading.Tasks;
using BrainSimulator;
using BrainSimulator.Modules;
using Xunit;

namespace UKS.Tests;

public class NonUksRemainingFixesRegressionTests
{
    [Fact]
    public void GetStringFromThoughtLabel_empty_returns_empty()
    {
        var method = typeof(ModuleGPTInfo).GetMethod(
            "GetStringFromThoughtLabel",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = (string)method!.Invoke(null, new object[] { "" })!;
        Assert.Equal("", result);
    }

    [Fact]
    public void HSLColor_ToColor_hue345_returns_defined_color()
    {
        var hsl = new HSLColor(345f, 0.8f, 0.5f);
        var color = hsl.ToColor();
        Assert.InRange(color.R + color.G + color.B, 1, 765);
    }

    [Theory]
    [InlineData(nameof(ModuleOnlineInfo.GetKidsWordsmythNet))]
    [InlineData(nameof(ModuleOnlineInfo.GetWiktionaryData))]
    [InlineData(nameof(ModuleOnlineInfo.GetChatGPTResult))]
    [InlineData(nameof(ModuleOnlineInfo.GetWikidataData))]
    [InlineData(nameof(ModuleOnlineInfo.GetFreeDictionaryAPIData))]
    [InlineData(nameof(ModuleOnlineInfo.GetWebstersDictionaryAPIData))]
    public void ModuleOnlineInfo_public_fetch_methods_return_task(string methodName)
    {
        var method = typeof(ModuleOnlineInfo).GetMethod(methodName);
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
    }

    [Fact]
    public void ModuleBalanceTreeDlg_code_behind_uses_xaml_cs_suffix()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var path = Path.Combine(repoRoot, "BrainSimulator", "Modules", "Agents", "ModuleBalanceTreeDlg.xaml.cs");
        Assert.True(File.Exists(path), $"Expected code-behind at {path}");
        Assert.False(File.Exists(path.Replace(".xaml.cs", ".xml.cs")));
    }
}
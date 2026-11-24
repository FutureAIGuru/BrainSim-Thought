/*
 * Regression tests for remaining P1 (#9-10) and P3 (#19-23) non-UKS fixes (2026-07-07).
 */

using BrainSimulator;
using BrainSimulator.Modules;
using Xunit;

namespace UKS.Tests;

public class NonUksRemainingFixesRegressionTests
{
    [Fact]
    public void HSLColor_ToColor_hue345_returns_defined_color()
    {
        var hsl = new HSLColor(345f, 0.8f, 0.5f);
        var color = hsl.ToColor();
        Assert.InRange(color.R + color.G + color.B, 1, 765);
    }

}

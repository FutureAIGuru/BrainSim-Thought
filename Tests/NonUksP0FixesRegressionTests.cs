/*
 * Regression tests for P0 non-UKS fixes (2026-07-07).
 */

using BrainSimulator;
using Xunit;

namespace UKS.Tests;

public class NonUksP0FixesRegressionTests
{
    [Fact]
    public void HSLColor_Equals_does_not_merge_hues_beyond_threshold_across_wrap()
    {
        var a = new HSLColor(350f, 0.8f, 0.5f);
        var b = new HSLColor(10f, 0.8f, 0.5f);

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void HSLColor_Equals_distinguishes_visually_different_hues()
    {
        var a = new HSLColor(30f, 0.8f, 0.5f);
        var b = new HSLColor(200f, 0.8f, 0.5f);

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void HSLColor_Equals_treats_near_wrap_pairs_as_equal()
    {
        var a = new HSLColor(2f, 0.8f, 0.5f);
        var b = new HSLColor(358f, 0.8f, 0.5f);

        Assert.True(a.Equals(b));
    }
}
/*
 * Ch.4 discrete sensory ingress regression tests (Simon Atomic Thoughts Fig 4.2).
 */

using System.Linq;
using UKS;
using Xunit;

namespace UKS.Tests;

public class DiscreteAttributeCh4RegressionTests
{
    private static int LevelNumber(string label) =>
        int.Parse(label.Split('-')[^1]);

    private static UKS CreateUks()
    {
        var uks = new UKS(clear: true);
        uks.CreateInitialStructure();
        return uks;
    }

    [Fact]
    public void Orange_rgb_quantizes_to_high_red_medium_green_low_blue()
    {
        var uks = CreateUks();
        var decoded = DiscreteAttributeDecoder.DecodeRgb(255, 165, 0, uks);

        Assert.Equal("red-level-8", decoded.RedLevel.Label);
        Assert.InRange(LevelNumber(decoded.GreenLevel.Label), 5, 7);
        Assert.Equal("blue-level-1", decoded.BlueLevel.Label);
        Assert.InRange(LevelNumber(decoded.BrightnessLevel.Label), 5, 7);
    }

    [Fact]
    public void Zero_rgb_emits_affirmative_black_and_low_brightness()
    {
        var uks = CreateUks();
        var decoded = DiscreteAttributeDecoder.DecodeRgb(0, 0, 0, uks);

        Assert.Equal("red-level-1", decoded.RedLevel.Label);
        Assert.Equal("green-level-1", decoded.GreenLevel.Label);
        Assert.Equal("blue-level-1", decoded.BlueLevel.Label);
        Assert.Equal("brightness-level-1", decoded.BrightnessLevel.Label);
        Assert.NotNull(decoded.AffirmativeBlack);
        Assert.Equal("black", decoded.AffirmativeBlack.Label);
        Assert.NotNull(decoded.AffirmativeLowBrightness);
        Assert.Equal("low-brightness", decoded.AffirmativeLowBrightness.Label);
    }

    [Fact]
    public void DecodeAndApply_writes_has_links_discoverable_via_gated_traverse()
    {
        var uks = CreateUks();
        var ctx = new TraversalContext();
        Thought outline = uks.GetOrAddThought("Outline-test", "Object");
        Thought has = uks.Labeled("has");

        DiscreteAttributeDecoder.DecodeAndApply(outline, 255, 165, 0, uks, ctx);

        var links = uks.GetGatedLinks(outline, has, ctx);
        Assert.Contains(links, l => l.To?.Label == "red-level-8");
        Assert.Contains(links, l => l.To?.Label == "blue-level-1");
        Assert.DoesNotContain(links, l => l.To?.Label == "black");
    }

    [Fact]
    public void Zero_input_has_links_include_black_not_empty_result()
    {
        var uks = CreateUks();
        var ctx = new TraversalContext();
        Thought region = uks.GetOrAddThought("dark-region", "Object");
        Thought has = uks.Labeled("has");

        DiscreteAttributeDecoder.DecodeAndApply(region, 0, 0, 0, uks, ctx);

        var links = uks.GetGatedLinks(region, has, ctx);
        Assert.NotEmpty(links);
        Assert.Contains(links, l => l.To?.Label == "black");
        Assert.Contains(links, l => l.To?.Label == "low-brightness");
    }

    [Fact]
    public void Discrete_level_thoughts_carry_isDiscreteLevel_property()
    {
        var uks = CreateUks();
        Thought level = uks.Labeled("red-level-4");
        Thought? isDiscrete = uks.Labeled("isDiscreteLevel");
        Assert.NotNull(level);
        Assert.NotNull(isDiscrete);
        Assert.True(level.HasProperty(isDiscrete));
    }
}
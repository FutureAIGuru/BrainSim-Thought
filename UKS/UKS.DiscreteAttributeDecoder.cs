/*
 * Brain Simulator Thought — Ch.4 analog→discrete sensory decode (Fig 4.2).
 * Quantizes RGBI channels to level Thoughts; black uses affirmative low-brightness.
 */

namespace UKS;

/// <summary>Discrete RGBI level Thoughts selected from analog input.</summary>
public sealed class DiscreteColorDecodeResult
{
    public Thought RedLevel { get; set; } = null!;
    public Thought GreenLevel { get; set; } = null!;
    public Thought BlueLevel { get; set; } = null!;
    public Thought BrightnessLevel { get; set; } = null!;
    public Thought? AffirmativeBlack { get; set; }
    public Thought? AffirmativeLowBrightness { get; set; }

    public IEnumerable<Thought> AllLevels
    {
        get
        {
            yield return RedLevel;
            yield return GreenLevel;
            yield return BlueLevel;
            yield return BrightnessLevel;
            if (AffirmativeBlack is not null) yield return AffirmativeBlack;
            if (AffirmativeLowBrightness is not null) yield return AffirmativeLowBrightness;
        }
    }
}

/// <summary>Maps normalized/byte color channels to discrete UKS level Thoughts.</summary>
public static class DiscreteAttributeDecoder
{
    public const int LevelCount = 8;

    /// <summary>Map 0–255 channel to level 1 (min) … 8 (max).</summary>
    public static int QuantizeToLevel(int channelValue0to255)
    {
        if (channelValue0to255 <= 0) return 1;
        if (channelValue0to255 >= 255) return LevelCount;
        return (int)Math.Clamp((channelValue0to255 * LevelCount) / 256 + 1, 1, LevelCount);
    }

    public static Thought GetLevelThought(UKS uks, string channel, int level) =>
        uks.Labeled($"{channel}-level-{level}")!;

    public static DiscreteColorDecodeResult DecodeRgb(byte r, byte g, byte b, UKS uks)
    {
        int luminance = (int)(0.299 * r + 0.587 * g + 0.114 * b);
        luminance = Math.Clamp(luminance, 0, 255);

        var result = new DiscreteColorDecodeResult
        {
            RedLevel = GetLevelThought(uks, "red", QuantizeToLevel(r)),
            GreenLevel = GetLevelThought(uks, "green", QuantizeToLevel(g)),
            BlueLevel = GetLevelThought(uks, "blue", QuantizeToLevel(b)),
            BrightnessLevel = GetLevelThought(uks, "brightness", QuantizeToLevel(luminance)),
        };

        if (r == 0 && g == 0 && b == 0)
        {
            result.AffirmativeBlack = uks.Labeled("black");
            result.AffirmativeLowBrightness = uks.Labeled("low-brightness");
        }

        return result;
    }

    public static void FireLevels(DiscreteColorDecodeResult decoded)
    {
        foreach (Thought level in decoded.AllLevels)
            level.Fire();
    }

    public static void ApplyHasLinks(Thought objectThought, DiscreteColorDecodeResult decoded, UKS uks)
    {
        Thought? has = uks.Labeled("has");
        if (has is null) return;

        foreach (Thought level in decoded.AllLevels)
            uks.AddStatement(objectThought, has, level);
    }

    /// <summary>Decode, fire level Thoughts, write has-links, optionally seed TraversalContext.</summary>
    public static DiscreteColorDecodeResult DecodeAndApply(
        Thought objectThought,
        byte r,
        byte g,
        byte b,
        UKS uks,
        TraversalContext? ctx = null)
    {
        DiscreteColorDecodeResult decoded = DecodeRgb(r, g, b, uks);
        FireLevels(decoded);
        ApplyHasLinks(objectThought, decoded, uks);

        if (ctx is not null)
        {
            ctx.Activate(objectThought);
            Thought? has = uks.Labeled("has");
            if (has is not null)
                ctx.ActivateRelationship(has);
        }

        return decoded;
    }
}
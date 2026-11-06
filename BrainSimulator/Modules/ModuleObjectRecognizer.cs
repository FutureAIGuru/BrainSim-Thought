/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License. You may use, copy, modify, merge, publish, distribute,
 * sublicense, and/or sell copies of this software under the terms of
 * the MIT License.
 *
 * See the LICENSE file in the project root for full license information.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using UKS;

namespace BrainSimulator.Modules;

/// <summary>
/// Looks at an object in a single image (a camera frame on iOS, any image file on the
/// desktop), runs it through ModuleVision's pixel array and boundary finder, and
/// recognizes the object or learns it.
///
/// Every look is stored in the UKS as an Appearance: a Thought whose links are what the
/// vision pipeline saw — where the edges are (hasEdgeAt), the tones (hasTone), the colours
/// (hasColor) and the outline's proportions (hasAspect). An object is any Thought with
/// hasAppearance links, so learning is adding an Appearance and recognition is finding
/// the object whose Appearances best match a new look. Unknown objects are remembered as
/// Thing1, Thing2… until they are named.
/// </summary>
public class ModuleObjectRecognizer : ModuleBase
{
    public const string AppearanceLabel = "Appearance";
    public const string HasAppearanceLabel = "hasAppearance";
    public const string HasEdgeAtLabel = "hasEdgeAt";
    public const string HasToneLabel = "hasTone";
    public const string HasColorLabel = "hasColor";
    public const string HasAspectLabel = "hasAspect";

    /// <summary>At or above this score a look is the best-matching object.</summary>
    public float RecognizeThreshold { get; set; } = 0.80f;
    /// <summary>Below the recognize threshold but at or above this, the best match is offered as a guess.</summary>
    public float GuessThreshold { get; set; } = 0.65f;
    /// <summary>A recognized look scoring below this is also kept as a new Appearance (a new view).</summary>
    public float NewViewThreshold { get; set; } = 0.95f;
    public int MaximumAppearances { get; set; } = 12;
    /// <summary>Unrecognized looks become Thing1, Thing2… (otherwise they wait to be named).</summary>
    public bool LearnUnknownObjects { get; set; } = true;
    public string AnonymousObjectPrefix { get; set; } = "Thing";
    public TimeSpan PresentationLifetime { get; set; } = TimeSpan.FromSeconds(10);

    public const int ShapeGrid = 6;
    public const int ToneGrid = 8;
    static readonly string[] ColorNames =
        { "red", "orange", "yellow", "green", "cyan", "blue", "purple", "pink", "white", "gray", "black" };

    /// <summary>What the vision pipeline reported for one look.</summary>
    public sealed class Observation
    {
        public float[] Edges = new float[ShapeGrid * ShapeGrid];
        public float[] Tones = new float[ToneGrid * ToneGrid];
        public float[] Colors = new float[ColorNames.Length];
        public float Aspect = 1;
        public int BoundaryCount;
        /// <summary>The outline in the pixel array: x0, y0, x1, y1 (inclusive).</summary>
        public (int x0, int y0, int x1, int y1) Box;
        public string DominantColor => Colors.Max() > 0 ? ColorNames[Array.IndexOf(Colors, Colors.Max())] : null;
    }

    public sealed record Match(Thought Object, Thought Appearance, float Score);

    // The last look, for dialogs.
    public string LastImagePath { get; private set; }
    public Color[,] LastPixels { get; private set; }
    public List<PointPlus> LastBoundary { get; private set; } = new();
    /// <summary>Object outline found in the pixel array: x0, y0, x1, y1 (inclusive).</summary>
    public (int x0, int y0, int x1, int y1) LastBox { get; private set; }
    public Observation LastObservation { get; private set; }
    public List<Match> LastMatches { get; private set; } = new();
    /// <summary>The object the last look was recognized as or learned as.</summary>
    public Thought LastObject { get; private set; }
    /// <summary>The Appearance the last look added, or null when it only matched.</summary>
    public Thought LastAppearance { get; private set; }
    public bool LastRecognized { get; private set; }
    public string Status { get; private set; } = "Take a picture of an object.";

    ModuleVision privateVision;
    readonly HashSet<string> registeredImageRoots = new(StringComparer.OrdinalIgnoreCase);
    bool restoredImageRoot;

    public override void Fire()
    {
        Init();  //be sure to leave this here
        RestoreImageRoot();
    }

    /// <summary>Where earlier photos were saved, so their Appearances can find them after a restart.</summary>
    void RestoreImageRoot()
    {
        if (restoredImageRoot) return;
        restoredImageRoot = true;
        RegisterImageRoot(GetSavedDlgAttribute("ImageRoot"));
    }

    public override void Initialize()
    {
        LastObject = null;
        LastAppearance = null;
        LastMatches = new();
        Status = "Take a picture of an object.";
    }

    public override void UKSInitializedNotification()
    {
        EnsureVocabulary();
    }

    public void EnsureVocabulary()
    {
        if (theUKS is null) return;
        theUKS.GetOrAddThought("VisualElement", "Thought");
        theUKS.GetOrAddThought("Image", "VisualElement");
        Thought hasImage = theUKS.GetOrAddThought("hasImage", "LinkType");
        hasImage.AddProperty(theUKS.GetOrAddThought("isGrounding", "Property"));
        theUKS.GetOrAddThought("Object", "Thought");
        theUKS.GetOrAddThought(AppearanceLabel, "VisualElement");
        theUKS.GetOrAddThought("VisualFeature", "VisualElement");
        theUKS.GetOrAddThought("EdgeCell", "VisualFeature");
        theUKS.GetOrAddThought("ToneCell", "VisualFeature");
        theUKS.GetOrAddThought("Color", "VisualFeature");
        theUKS.GetOrAddThought("Proportion", "VisualFeature");
        foreach (string label in new[] { HasAppearanceLabel, HasEdgeAtLabel, HasToneLabel, HasColorLabel, HasAspectLabel })
            theUKS.GetOrAddThought(label, "LinkType");
    }

    // ------------------------------------------------------------------
    // Looking
    // ------------------------------------------------------------------

    /// <summary>
    /// Runs one image through the vision pipeline and recognizes or learns the object in it.
    /// </summary>
    /// <returns>The object recognized or learned, or null.</returns>
    public Thought Look(string imagePath)
    {
        EnsureVocabulary();
        RestoreImageRoot();
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            Status = "Image not found.";
            UpdateDialog();
            return null;
        }

        ModuleVision vision = MainWindow.theWindow?.activeModules.OfType<ModuleVision>().FirstOrDefault()
            ?? (privateVision ??= new ModuleVision());
        (Color[,] pixels, List<PointPlus> boundary) = vision.AnalyzeImageFile(imagePath);

        LastImagePath = imagePath;
        LastPixels = pixels;
        LastBoundary = boundary;
        LastObject = null;
        LastAppearance = null;
        LastRecognized = false;
        LastObservation = Describe(pixels, boundary);
        if (LastObservation is not null) LastBox = LastObservation.Box;
        if (LastObservation is null)
        {
            LastMatches = new();
            Status = "No object found — try a plainer background or move closer.";
            UpdateDialog();
            return null;
        }

        RegisterImageRoot(ImageRootFor(imagePath));
        LastMatches = Compare(LastObservation);
        Match best = LastMatches.FirstOrDefault();

        if (best is not null && best.Score >= RecognizeThreshold)
        {
            LastRecognized = true;
            LastObject = best.Object;
            if (best.Score < NewViewThreshold && AppearancesOf(best.Object).Count < MaximumAppearances)
                LastAppearance = AddAppearance(best.Object, LastObservation, imagePath);
            Status = $"This is {best.Object.Label} ({best.Score:P0})" +
                (LastAppearance is not null ? " — learned a new view." : ".");
        }
        else if (LearnUnknownObjects)
        {
            LastObject = theUKS.GetOrAddThought(NextAnonymousLabel(), "Object");
            LastAppearance = AddAppearance(LastObject, LastObservation, imagePath);
            Status = best is not null && best.Score >= GuessThreshold
                ? $"New object {LastObject.Label}. It looks a little like {best.Object.Label} ({best.Score:P0}) — is it?"
                : $"New object — remembered as {LastObject.Label}. What is it?";
        }
        else
        {
            Status = best is not null && best.Score >= GuessThreshold
                ? $"Is it {best.Object.Label}? ({best.Score:P0})"
                : "I don't know this object. What is it?";
        }

        if (LastObject is not null) Perceive(LastObject, imagePath);
        UpdateDialog();
        return LastObject;
    }

    /// <summary>
    /// The last look was of `name`: re-home its Appearance there (merging an anonymous
    /// Thing into it) or, if the look added nothing, learn it as a new view of `name`.
    /// </summary>
    public Thought Teach(string name)
    {
        name = CleanLabel(name);
        if (name is null || LastObservation is null)
        {
            Status = LastObservation is null ? "Take a picture first." : "Type a name.";
            UpdateDialog();
            return null;
        }
        EnsureVocabulary();

        Thought target = theUKS.Labeled(name);
        Thought previous = LastObject;
        // Labels ignore case, so "thing3" finds the anonymous Thing3 itself: that is naming it too.
        if ((target is null || target == previous) && previous is not null && IsAnonymous(previous))
        {
            // just name the anonymous Thing
            previous.Label = name;
            target = previous;
        }
        target ??= theUKS.GetOrAddThought(name, "Object");
        if (target is Link || target.HasAncestor("VisualElement"))
        {
            Status = $"{name} can't be used as an object name.";
            UpdateDialog();
            return null;
        }

        if (target != previous) AssignLastLookTo(target);
        LastObject = target;
        LastRecognized = true;
        int views = AppearancesOf(target).Count;
        Status = $"Learned: this is {target.Label} ({views} {(views == 1 ? "view" : "views")}).";
        Perceive(target, LastImagePath);
        UpdateDialog();
        return target;
    }

    /// <summary>The guess was right: the last look is the best-matching object.</summary>
    public Thought ConfirmGuess()
    {
        Match best = LastMatches.FirstOrDefault(m => m.Object != LastObject);
        if (best is null) return null;
        return Teach(best.Object.Label);
    }

    /// <summary>Forgets everything learned about how `obj` looks.</summary>
    public void Forget(Thought obj)
    {
        if (obj is null) return;
        foreach (Thought appearance in AppearancesOf(obj))
            appearance.Delete();
        obj.RemoveLinks(theUKS.Labeled(HasAppearanceLabel));
        if (IsAnonymous(obj)) obj.Delete();
        if (LastObject == obj) { LastObject = null; LastAppearance = null; }
        LastMatches.RemoveAll(m => m.Object == obj);
        Status = $"Forgot what {obj.Label} looks like.";
        UpdateDialog();
    }

    // ------------------------------------------------------------------
    // Known objects
    // ------------------------------------------------------------------

    /// <summary>Every object with at least one Appearance, with its Appearances.</summary>
    public List<(Thought obj, List<Thought> appearances)> KnownObjects()
    {
        Thought root = theUKS?.Labeled(AppearanceLabel);
        if (root is null) return new();
        return root.Children
            .Select(a => (obj: OwnerOf(a), appearance: a))
            .Where(x => x.obj is not null)
            .GroupBy(x => x.obj)
            .Select(g => (g.Key, g.Select(x => x.appearance).ToList()))
            .OrderBy(x => x.Key.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<Thought> AppearancesOf(Thought obj) =>
        obj.LinksTo.Where(l => l.LinkType?.Label == HasAppearanceLabel && l.To is not null)
            .Select(l => l.To).ToList();

    static Thought OwnerOf(Thought appearance) =>
        appearance.LinksFrom.FirstOrDefault(l => l.LinkType?.Label == HasAppearanceLabel)?.From;

    /// <summary>Thing1, Thing2… (the prefix followed only by digits, any case).</summary>
    bool IsAnonymous(Thought t) => AnonymousNumber(t) is not null;

    int? AnonymousNumber(Thought t)
    {
        string label = t?.Label;
        int start = AnonymousObjectPrefix.Length;
        if (label is null || label.Length <= start ||
            !label.StartsWith(AnonymousObjectPrefix, StringComparison.OrdinalIgnoreCase))
            return null;
        for (int i = start; i < label.Length; i++)
            if (!char.IsAsciiDigit(label[i])) return null;
        return int.TryParse(label.AsSpan(start), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;
    }

    string NextAnonymousLabel()
    {
        int highest = 0;
        foreach (Thought t in theUKS.AtomicThoughts)
            if (AnonymousNumber(t) is int n && n > highest) highest = n;
        return AnonymousObjectPrefix + (highest + 1).ToString(CultureInfo.InvariantCulture);
    }

    static string CleanLabel(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return Regex.Replace(name.Trim(), @"\s+", "_");
    }

    // ------------------------------------------------------------------
    // Features: what the vision pipeline saw
    // ------------------------------------------------------------------

    /// <summary>
    /// Turns the pipeline's pixel array and boundary points into an Observation. The object
    /// is taken to be the region the boundary points enclose (the 5th–95th percentile, so
    /// stray edges in the background don't stretch it).
    /// </summary>
    public Observation Describe(Color[,] pixels, List<PointPlus> boundary)
    {
        if (pixels is null) return null;
        int width = pixels.GetLength(0), height = pixels.GetLength(1);
        var points = boundary.Where(p => p.X >= 0 && p.Y >= 0 && p.X < width && p.Y < height).ToList();
        if (points.Count < 6 || width < 4 || height < 4) return null;

        static float Percentile(List<float> sorted, float q) => sorted[(int)Math.Clamp(Math.Round(q * (sorted.Count - 1)), 0, sorted.Count - 1)];
        var xs = points.Select(p => p.X).OrderBy(v => v).ToList();
        var ys = points.Select(p => p.Y).OrderBy(v => v).ToList();
        int x0 = Math.Max(0, (int)Math.Floor(Percentile(xs, 0.05f)) - 1);
        int x1 = Math.Min(width - 1, (int)Math.Ceiling(Percentile(xs, 0.95f)) + 1);
        int y0 = Math.Max(0, (int)Math.Floor(Percentile(ys, 0.05f)) - 1);
        int y1 = Math.Min(height - 1, (int)Math.Ceiling(Percentile(ys, 0.95f)) + 1);
        if (x1 - x0 < 3 || y1 - y0 < 3) return null;
        float boxW = x1 - x0 + 1, boxH = y1 - y0 + 1;

        var obs = new Observation { Aspect = boxW / boxH, BoundaryCount = points.Count, Box = (x0, y0, x1, y1) };

        // where the edges are, relative to the outline
        foreach (PointPlus p in points)
        {
            if (p.X < x0 || p.X > x1 + 1 || p.Y < y0 || p.Y > y1 + 1) continue;
            int gx = Math.Clamp((int)((p.X - x0) / boxW * ShapeGrid), 0, ShapeGrid - 1);
            int gy = Math.Clamp((int)((p.Y - y0) / boxH * ShapeGrid), 0, ShapeGrid - 1);
            obs.Edges[gy * ShapeGrid + gx]++;
        }
        Normalize(obs.Edges);

        // Tones inside the outline. Colours: the object's own pixels, i.e. the ones that differ
        // from the background (estimated from the picture's border); if too few do, as on a
        // busy background, the middle of the outline, where the object most likely is.
        (int r, int g, int b) background = BorderColor(pixels);
        var toneCount = new int[obs.Tones.Length];
        var foreground = new float[ColorNames.Length];
        var middle = new float[ColorNames.Length];
        int foregroundCount = 0, boxCount = 0;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            {
                Color pixel = pixels[x, y];
                HSLColor hsl = new(pixel);
                int gx = Math.Clamp((int)((x - x0) / boxW * ToneGrid), 0, ToneGrid - 1);
                int gy = Math.Clamp((int)((y - y0) / boxH * ToneGrid), 0, ToneGrid - 1);
                obs.Tones[gy * ToneGrid + gx] += hsl.luminance;
                toneCount[gy * ToneGrid + gx]++;

                boxCount++;
                int bin = ColorBin(hsl);
                if (Math.Abs(pixel.R - background.r) + Math.Abs(pixel.G - background.g) + Math.Abs(pixel.B - background.b) > ForegroundDistance)
                {
                    foreground[bin]++;
                    foregroundCount++;
                }
                float fx = (x - x0) / boxW, fy = (y - y0) / boxH;
                if (fx >= 0.15f && fx <= 0.85f && fy >= 0.15f && fy <= 0.85f)
                    middle[bin]++;
            }
        for (int i = 0; i < obs.Tones.Length; i++)
            if (toneCount[i] > 0) obs.Tones[i] /= toneCount[i];
        obs.Colors = foregroundCount >= 0.05f * boxCount ? foreground : middle;
        float colorTotal = obs.Colors.Sum();
        if (colorTotal > 0)
            for (int i = 0; i < obs.Colors.Length; i++) obs.Colors[i] /= colorTotal;
        return obs;
    }

    /// <summary>How far (sum of R, G, B differences) a pixel must be from the background to count as the object.</summary>
    const int ForegroundDistance = 60;

    /// <summary>The average colour of the picture's outermost pixels: the background, for an object in the middle.</summary>
    static (int r, int g, int b) BorderColor(Color[,] pixels)
    {
        int w = pixels.GetLength(0), h = pixels.GetLength(1);
        long r = 0, g = 0, b = 0, n = 0;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (x > 0 && y > 0 && x < w - 1 && y < h - 1) continue;
                Color c = pixels[x, y];
                r += c.R; g += c.G; b += c.B; n++;
            }
        return n == 0 ? (0, 0, 0) : ((int)(r / n), (int)(g / n), (int)(b / n));
    }

    static int ColorBin(HSLColor c)
    {
        if (c.saturation < 0.2f || c.luminance < 0.12f || c.luminance > 0.92f)
            return c.luminance < 0.3f ? 10 : c.luminance > 0.75f ? 8 : 9;   // black, white, gray
        float h = c.hue;
        if (h < 15 || h >= 345) return 0;
        if (h < 45) return 1;
        if (h < 70) return 2;
        if (h < 165) return 3;
        if (h < 195) return 4;
        if (h < 255) return 5;
        if (h < 290) return 6;
        return 7;
    }

    static void Normalize(float[] v)
    {
        float len = (float)Math.Sqrt(v.Sum(x => x * x));
        if (len > 0) for (int i = 0; i < v.Length; i++) v[i] /= len;
    }

    /// <summary>
    /// Similarity of two looks, 0..1: a weighted geometric mean of how well the edges,
    /// colours, tones and proportions agree. Unlike a sum, one clear disagreement (the same
    /// outline in a different colour) pulls the score down, while a real match, where all
    /// four agree, is barely affected.
    /// </summary>
    public static float Similarity(Observation a, Observation b)
    {
        float edges = Cosine(a.Edges, b.Edges);
        float colors = Cosine(a.Colors, b.Colors);
        float tones = Math.Max(0, Correlation(a.Tones, b.Tones));
        float aspect = (float)Math.Exp(-2 * Math.Abs(Math.Log(Math.Max(0.01, a.Aspect) / Math.Max(0.01, b.Aspect))));
        static double Log(float v) => Math.Log(Math.Max(0.01f, v));
        return (float)Math.Exp(0.35 * Log(edges) + 0.30 * Log(colors) + 0.25 * Log(tones) + 0.10 * Log(aspect));
    }

    static float Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : (float)(dot / Math.Sqrt(na * nb));
    }

    static float Correlation(float[] a, float[] b)
    {
        float ma = a.Average(), mb = b.Average();
        return Cosine(a.Select(x => x - ma).ToArray(), b.Select(x => x - mb).ToArray());
    }

    /// <summary>Every known object scored by its best-matching Appearance, best first.</summary>
    public List<Match> Compare(Observation obs)
    {
        var matches = new List<Match>();
        foreach (var (obj, appearances) in KnownObjects())
        {
            Match best = null;
            foreach (Thought appearance in appearances)
            {
                float score = Similarity(obs, Read(appearance));
                if (best is null || score > best.Score) best = new Match(obj, appearance, score);
            }
            if (best is not null) matches.Add(best);
        }
        return matches.OrderByDescending(m => m.Score).ToList();
    }

    // ------------------------------------------------------------------
    // Appearances in the UKS
    // ------------------------------------------------------------------

    Thought AddAppearance(Thought obj, Observation obs, string imagePath)
    {
        int next = theUKS.Labeled(AppearanceLabel).Children
            .Select(t => int.TryParse(t.Label.StartsWith("look") ? t.Label[4..] : "", out int n) ? n : 0)
            .DefaultIfEmpty(0).Max() + 1;
        Thought appearance = theUKS.GetOrAddThought("look" + next.ToString(CultureInfo.InvariantCulture), AppearanceLabel);
        Write(appearance, obs);
        AttachImage(appearance, imagePath);
        obj.AddLink(theUKS.Labeled(HasAppearanceLabel), appearance);

        // what the object looks like, in words: its main colour
        string main = obs.DominantColor;
        if (main is not null && obs.Colors.Max() >= 0.4f &&
            !obj.LinksTo.Any(l => l.LinkType?.Label == HasColorLabel))
            obj.AddLink(theUKS.Labeled(HasColorLabel), ColorThought(main));
        return appearance;
    }

    void AssignLastLookTo(Thought target)
    {
        Thought hasAppearance = theUKS.Labeled(HasAppearanceLabel);
        if (LastAppearance is null)
        {
            LastAppearance = AddAppearance(target, LastObservation, LastImagePath);
            return;
        }
        Thought owner = OwnerOf(LastAppearance);
        if (owner == target) return;
        owner?.RemoveLink(hasAppearance, LastAppearance);
        target.AddLink(hasAppearance, LastAppearance);
        if (owner is not null && IsAnonymous(owner) && AppearancesOf(owner).Count == 0)
            owner.Delete();
    }

    void Write(Thought appearance, Observation obs)
    {
        appearance.V = Encode(obs);
        Thought hasEdgeAt = theUKS.Labeled(HasEdgeAtLabel);
        Thought hasTone = theUKS.Labeled(HasToneLabel);
        Thought hasColor = theUKS.Labeled(HasColorLabel);
        for (int i = 0; i < obs.Edges.Length; i++)
            if (obs.Edges[i] > 0.001f)
                SetLink(appearance, hasEdgeAt, theUKS.GetOrAddThought($"edge_{i / ShapeGrid}_{i % ShapeGrid}", "EdgeCell"), obs.Edges[i]);
        for (int i = 0; i < obs.Tones.Length; i++)
            SetLink(appearance, hasTone, theUKS.GetOrAddThought($"tone_{i / ToneGrid}_{i % ToneGrid}", "ToneCell"), Math.Max(0.001f, obs.Tones[i]));
        for (int i = 0; i < obs.Colors.Length; i++)
            if (obs.Colors[i] > 0.001f)
                SetLink(appearance, hasColor, ColorThought(ColorNames[i]), obs.Colors[i]);
        string shape = obs.Aspect > 1.25f ? "wide" : obs.Aspect < 0.8f ? "tall" : "square";
        SetLink(appearance, theUKS.Labeled(HasAspectLabel), theUKS.GetOrAddThought(shape, "Proportion"), obs.Aspect);
    }

    static void SetLink(Thought from, Thought linkType, Thought to, float weight)
    {
        Link l = from.AddLink(linkType, to);
        if (l is not null) l.Weight = weight;
    }

    Thought ColorThought(string name) => theUKS.Labeled(name) ?? theUKS.GetOrAddThought(name, "Color");

    // ------------------------------------------------------------------
    // Reading Appearances back
    //
    // The features are kept in two places: as link weights, which make them part of the UKS
    // (hasColor red 0.4…), and as a compact string in the Appearance's V, which is what is
    // read back. Link weights are not safe storage: other modules reset them (ModuleVision's
    // ClearBoundaryArray sets the weight of every Thought and Link to 0).
    // ------------------------------------------------------------------

    const string FeatureFormat = "v2";

    static string Encode(Observation obs)
    {
        static string Floats(float[] v) => string.Join(",", v.Select(x => x.ToString("G6", CultureInfo.InvariantCulture)));
        return string.Join("|", FeatureFormat, Floats(obs.Edges), Floats(obs.Tones), Floats(obs.Colors),
            obs.Aspect.ToString("G6", CultureInfo.InvariantCulture));
    }

    static Observation Decode(object v)
    {
        if (v is not string s || !s.StartsWith(FeatureFormat + "|", StringComparison.Ordinal)) return null;
        string[] parts = s.Split('|');
        if (parts.Length != 5) return null;
        var obs = new Observation();
        bool Fill(string csv, float[] into)
        {
            string[] items = csv.Split(',');
            if (items.Length != into.Length) return false;
            for (int i = 0; i < items.Length; i++)
                if (!float.TryParse(items[i], NumberStyles.Float, CultureInfo.InvariantCulture, out into[i])) return false;
            return true;
        }
        if (!Fill(parts[1], obs.Edges) || !Fill(parts[2], obs.Tones) || !Fill(parts[3], obs.Colors) ||
            !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out obs.Aspect))
            return null;
        return obs;
    }

    /// <summary>
    /// The features of an Appearance. Ones saved before features were kept in V are read from
    /// the links, unless those were reset or the look's photo is available: then the photo is
    /// looked at again (once; the result is kept in V).
    /// </summary>
    Observation Read(Thought appearance)
    {
        if (Decode(appearance.V) is { } stored) return stored;
        Observation obs = ReadLinks(appearance);
        if (Reanalyze(appearance) is { } fresh) obs = fresh;
        else if (obs.Edges.All(x => x == 0) && obs.Tones.All(x => x == 0)) return obs;   // nothing to go on
        appearance.V = Encode(obs);
        return obs;
    }

    /// <summary>Runs an Appearance's photo through the pipeline again (with a private ModuleVision, so the user's is left alone).</summary>
    Observation Reanalyze(Thought appearance)
    {
        string path = GroundedImageResolver.ResolveImagePath(appearance);
        if (path is null) return null;
        try
        {
            (Color[,] pixels, List<PointPlus> boundary) = (privateVision ??= new ModuleVision()).AnalyzeImageFile(path);
            return Describe(pixels, boundary);
        }
        catch (Exception)
        {
            return null;   // photo unreadable: fall back to the links
        }
    }

    Observation ReadLinks(Thought appearance)
    {
        var obs = new Observation();
        foreach (Link l in appearance.LinksTo)
        {
            string type = l.LinkType?.Label, to = l.To?.Label;
            if (type is null || to is null) continue;
            if (type == HasEdgeAtLabel && TryCell(to, "edge_", ShapeGrid, out int e)) obs.Edges[e] = l.Weight;
            else if (type == HasToneLabel && TryCell(to, "tone_", ToneGrid, out int t)) obs.Tones[t] = l.Weight;
            else if (type == HasColorLabel)
            {
                int c = Array.FindIndex(ColorNames, n => n.Equals(to, StringComparison.OrdinalIgnoreCase));
                if (c >= 0) obs.Colors[c] = l.Weight;
            }
            else if (type == HasAspectLabel) obs.Aspect = l.Weight;
        }
        return obs;
    }

    static bool TryCell(string label, string prefix, int grid, out int index)
    {
        index = -1;
        if (!label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        string[] parts = label[prefix.Length..].Split('_');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int r) || !int.TryParse(parts[1], out int c)) return false;
        if (r < 0 || r >= grid || c < 0 || c >= grid) return false;
        index = r * grid + c;
        return true;
    }

    // ------------------------------------------------------------------
    // Grounding: the photo, the Mental Model
    // ------------------------------------------------------------------

    /// <summary>Links the photo so the Mental Model (via GroundedImageResolver) can show it.</summary>
    void AttachImage(Thought t, string imagePath)
    {
        string file = Path.GetFileName(imagePath ?? "");
        if (!GroundedImageResolver.IsSafeImageFileName(file) || ImageRootFor(imagePath) is null) return;
        Thought hasImage = theUKS.Labeled("hasImage");
        Thought image = theUKS.Labeled(file) ?? theUKS.GetOrAddThought(file, "Image");
        t.RemoveLinks(hasImage);
        Link l = t.AddLink(hasImage, image);
        if (l is not null) l.Weight = 1;
    }

    /// <summary>The object was just seen: fire it, show its latest photo, and place it at the Mental Model's attention.</summary>
    void Perceive(Thought obj, string imagePath)
    {
        AttachImage(obj, imagePath);
        obj.Fire();
        ModuleMentalModel mentalModel = MainWindow.theWindow?.activeModules.OfType<ModuleMentalModel>().FirstOrDefault();
        if (mentalModel?.Center is null) return;
        Thought cell = mentalModel.AttentionCell ?? mentalModel.SetAttentionCell(mentalModel.Center);
        if (cell is null) return;
        Link binding = mentalModel.BindThoughtToMentalModel(obj, cell);
        if (binding is not null) binding.TimeToLive = PresentationLifetime;
    }

    /// <summary>Grounded images live in &lt;root&gt;/Images; the photo's root, if it is in such a folder.</summary>
    static string ImageRootFor(string imagePath)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(imagePath ?? "."));
        return dir is not null && Path.GetFileName(dir).Equals("Images", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(dir)
            : null;
    }

    void RegisterImageRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root) || !registeredImageRoots.Add(root)) return;
        if (!GroundedContentLocator.RelativeContentRoots.Contains(root))
            GroundedContentLocator.RelativeContentRoots.Add(root);
        if (GetSavedDlgAttribute("ImageRoot") != root)
            SetSavedDlgAttribute("ImageRoot", root);
    }
}

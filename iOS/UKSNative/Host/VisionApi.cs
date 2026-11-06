// The logic of Vision/ModuleVisionDlg.xaml.cs: the module's arrays and the patch and corner
// Thoughts that fired, as data, and the dialog's operations. The app draws them.
using System.Windows.Media;
using UKS;

namespace BrainSimulator.Modules;

public sealed class WeightedPointDto
{
    public int X { get; set; }
    public int Y { get; set; }
    public float Weight { get; set; }
    public float MaxWeight { get; set; }
}

public sealed class PatchDto
{
    public string Label { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public float Weight { get; set; }
    public double FiredAgo { get; set; }
    /// <summary>Its links to boundary points, with their learned weights.</summary>
    public List<WeightedPointDto> Boundary { get; set; } = new();
    /// <summary>Neighbouring patches it has learned are collinear with it (weight at least 0.2).</summary>
    public List<WeightedPointDto> Collinear { get; set; } = new();
    /// <summary>The stroke direction its boundary weights describe (radians), if it has a centre point.</summary>
    public double? Orientation { get; set; }
    /// <summary>The centre point the stroke is drawn through.</summary>
    public int OrientationX { get; set; }
    public int OrientationY { get; set; }
}

public sealed class CornerDto
{
    public string Label { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public float Weight { get; set; }
}

public sealed class VisionState
{
    public string ImagePath { get; set; }
    /// <summary>The pixel array (Width×Height RGB bytes, row by row, base64), when there is one.</summary>
    public int Width { get; set; }
    public int Height { get; set; }
    public string Pixels { get; set; }
    /// <summary>The boundary array, row by row, '1' where a boundary point is (empty before Init).</summary>
    public int BoundaryWidth { get; set; }
    public int BoundaryHeight { get; set; }
    public string Boundary { get; set; }
    public int PatchSize { get; set; }
    public int Outlines { get; set; }
    /// <summary>Patches that fired in the last ten seconds.</summary>
    public List<PatchDto> Patches { get; set; } = new();
    /// <summary>Corners that have fired.</summary>
    public List<CornerDto> Corners { get; set; } = new();
    public bool Busy { get; set; }
    public string Status { get; set; }
}

sealed class VisionApi : ModuleApi
{
    public override string State(ModuleBase m)
    {
        var v = (ModuleVision)m;
        var uks = v.theUKS;
        var state = new VisionState
        {
            ImagePath = v.CurrentFilePath,
            PatchSize = v.patchSize,
            Outlines = uks.Labeled("Outline")?.Children.Count ?? 0,
            Busy = busy,
            Status = status,
        };
        if (v.imageArray is { } pixels)
        {
            state.Width = pixels.GetLength(0);
            state.Height = pixels.GetLength(1);
            state.Pixels = EncodePixels(pixels);
        }
        if (v.boundaryArray is { } boundary)
        {
            state.BoundaryWidth = boundary.GetLength(0);
            state.BoundaryHeight = boundary.GetLength(1);
            var bits = new char[state.BoundaryWidth * state.BoundaryHeight];
            for (int y = 0; y < state.BoundaryHeight; y++)
                for (int x = 0; x < state.BoundaryWidth; x++)
                    bits[y * state.BoundaryWidth + x] = boundary[x, y] ? '1' : '0';
            state.Boundary = new string(bits);

            DateTime now = DateTime.Now;
            foreach (Thought t in Fired(uks, "patch", now - TimeSpan.FromSeconds(10), StringComparison.OrdinalIgnoreCase))
                if (Patch(t, now) is { } patch) state.Patches.Add(patch);
            foreach (Thought t in Fired(uks, "corner", new DateTime(1), StringComparison.Ordinal))
                if (Position(t.Label) is var (x, y))
                    state.Corners.Add(new CornerDto { Label = t.Label, X = x, Y = y, Weight = t.Weight });
        }
        return Json(state, ApiJson.Default.VisionState);
    }

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        var v = (ModuleVision)m;
        switch (op)
        {
            case "setImage": v.CurrentFilePath = args.Str("path"); break;
            case "test": v.SingteTestPattern(); break;
            case "testMany":
                // one pattern per lock, so the engine and the app keep running between them
                int count = Math.Max(1, args.Int("count", 1000));
                busy = true;
                status = $"Running {count} test patterns…";
                Watch(UKSNative.Exports.RunLocked(v.SingteTestPattern, count)
                    .ContinueWith(t => { busy = false; status = t.IsFaulted ? null : $"Ran {count} test patterns."; }), e => status = e);
                break;
            case "refresh": v.Refresh(); break;
            case "show": v.Show(); break;
            case "init": v.InitArray(); break;
            case "clear": v.ClearBoundaryArray(); break;
            case "inspect":
                // E_MouseRightButtonDown: the Thought tapped becomes the UKS dialog's root
                MainWindow.theWindow.GetModuleByLabel("ModuleUKS0")?.SetSavedDlgAttribute("Root", args.Str("thought"));
                break;
            default: return Unknown(op);
        }
        return Done();
    }

    /// <summary>
    /// Thoughts whose label starts with `prefix` that fired at or after `since`. Checks the
    /// time first: this runs on every poll over the whole UKS, and few Thoughts fired.
    /// </summary>
    static List<Thought> Fired(UKS.UKS uks, string prefix, DateTime since, StringComparison comparison) =>
        uks.AtomicThoughts.Where(x => x.LastFiredTime >= since && x.Label.StartsWith(prefix, comparison)).ToList();

    /// <summary>"patch_07_03_2" or "Pt_07_03" → (7, 3).</summary>
    static (int x, int y)? Position(string label)
    {
        string[] parts = label.Split('_');
        return parts.Length >= 3 && int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int y) ? (x, y) : null;
    }

    static PatchDto Patch(Thought t, DateTime now)
    {
        if (t.Label.Split('_').Length < 4 || Position(t.Label) is not var (x, y)) return null;
        var patch = new PatchDto { Label = t.Label, X = x, Y = y, Weight = t.Weight, FiredAgo = (now - t.LastFiredTime).TotalSeconds };
        foreach (Link pt in t.LinksTo.Where(l => l.LinkType?.Label == "hasBoundary"))
            if (pt.To is not null && Position(pt.To.Label) is var (bx, by))
                patch.Boundary.Add(new WeightedPointDto { X = bx, Y = by, Weight = pt.Weight, MaxWeight = pt.maxWeight });
        foreach (Link pt in t.LinksTo.Where(l => l.LinkType?.Label == "collinearWith" && l.Weight >= .2f))
            if (pt.To is not null && Position(pt.To.Label) is var (cx, cy))
                patch.Collinear.Add(new WeightedPointDto { X = cx, Y = cy, Weight = pt.Weight, MaxWeight = pt.maxWeight });
        if (Orientation(t) is var (theta, ox, oy)) { patch.Orientation = theta; patch.OrientationX = ox; patch.OrientationY = oy; }
        return patch;
    }

    /// <summary>The principal direction of a patch's weighted boundary points around its centre point.</summary>
    static (double theta, int x, int y)? Orientation(Thought patch)
    {
        Thought centerT = patch.LinksFrom.FirstOrDefault(x => x.Weight == 1 && x.LinkType?.Label == "hasBoundary")?.To;
        if (centerT is null || Position(centerT.Label) is not var (cx, cy)) return null;
        float Sxx = 0f, Syy = 0f, Sxy = 0f;
        foreach (var rel in patch.LinksFrom.Where(x => x.LinkType?.Label == "hasBoundary"))
        {
            if (rel.To is null || Position(rel.To.Label) is not var (px, py)) continue;
            float dx = px - cx, dy = py - cy, w = rel.Weight;
            Sxx += w * dx * dx; Syy += w * dy * dy; Sxy += w * dx * dy;
        }
        return (0.5 * Math.Atan2(2 * Sxy, Sxx - Syy), cx, cy);
    }

    /// <summary>Width×height RGB bytes, row by row, base64.</summary>
    internal static string EncodePixels(Color[,] pixels)
    {
        int w = pixels.GetLength(0), h = pixels.GetLength(1);
        var rgb = new byte[w * h * 3];
        for (int y = 0, i = 0; y < h; y++)
            for (int x = 0; x < w; x++, i += 3)
            {
                var c = pixels[x, y];
                rgb[i] = c.R; rgb[i + 1] = c.G; rgb[i + 2] = c.B;
            }
        return Convert.ToBase64String(rgb);
    }
}

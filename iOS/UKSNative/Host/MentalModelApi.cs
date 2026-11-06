// The logic of ModuleMentalModelDlg.xaml.cs: the spatial sheet as data (every cell's
// angular extent and what it holds) and the dialog's operations (turn, move, attend).
// The app draws the sheet; nothing here is screen geometry.
using UKS;

namespace BrainSimulator.Modules;

public sealed class MentalContentDto
{
    public string Label { get; set; } = "";
    /// <summary>The Thought's grounded image, if it has one.</summary>
    public string ImagePath { get; set; }
    /// <summary>What is known about it ([relation, target] pairs), when it has no image.</summary>
    public List<string[]> Attributes { get; set; }
    public bool Imagined { get; set; }
    /// <summary>Its distance, when one was given (0 = unknown).</summary>
    public double Distance { get; set; }
    /// <summary>Seconds until this placement is forgotten, or null if it lasts.</summary>
    public double? SecondsLeft { get; set; }
}

public sealed class MentalCellDto
{
    public string Label { get; set; } = "";
    public double AzimuthFrom { get; set; }
    public double AzimuthTo { get; set; }
    public bool InView { get; set; }
    public bool IsCenter { get; set; }
    public bool Attended { get; set; }
    /// <summary>Seconds since the cell last fired, if within the last few.</summary>
    public double? FiredAgo { get; set; }
    public List<MentalContentDto> Contents { get; set; }
}

public sealed class MentalRingDto
{
    public double ElevationFrom { get; set; }
    public double ElevationTo { get; set; }
    public List<MentalCellDto> Cells { get; set; } = new();
}

public sealed class MentalModelState
{
    public string AttentionCell { get; set; }
    public double AttentionAzimuth { get; set; }
    public double AttentionElevation { get; set; }
    /// <summary>How visible imagined Thoughts are drawn (the module's setting).</summary>
    public double ImaginedOpacity { get; set; }
    /// <summary>How many attributes a concept card lists (the module's setting).</summary>
    public int CardAttributes { get; set; }
    public List<MentalRingDto> Rings { get; set; } = new();
}

sealed class MentalModelApi : ModuleApi
{
    public override string State(ModuleBase m) => Json(Sheet((ModuleMentalModel)m), ApiJson.Default.MentalModelState);

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        var mm = (ModuleMentalModel)m;
        switch (op)
        {
            // turning the head left moves the scene right (positive azimuth)
            case "turn": mm.RotateMentalModel(Angle.FromDegrees((float)args.Num("degrees")), Angle.FromDegrees(0)); break;
            case "move": mm.MoveMentalModel((float)args.Num("distance")); break;
            case "attend": Attend(mm, args.Str("cell")); break;
            default: return Unknown(op);
        }
        return Done();
    }

    /// <summary>Rect_MouseLeftButtonDown: move attention to a cell, and fire it and its neighbours.</summary>
    static void Attend(ModuleMentalModel module, string label)
    {
        Thought t = module.theUKS.Labeled(label) ?? throw new ArgumentException($"No cell '{label}'.");
        var attention = ModuleApis.Active<ModuleAttention>();
        if (attention is not null) attention.SetCenterOfAttention(t);
        else module.SetAttentionCell(t);
        t.Fire();
        t.LinksTo.FirstOrDefault(x => x.LinkType?.Label == "above")?.To?.Fire();
        t.LinksTo.FirstOrDefault(x => x.LinkType?.Label == "rightOf")?.To?.Fire();
    }

    static MentalModelState Sheet(ModuleMentalModel mm)
    {
        var state = new MentalModelState
        {
            AttentionCell = mm.AttentionCell?.Label, ImaginedOpacity = mm.ImaginedOpacity, CardAttributes = mm.ConceptCardMaximumAttributes,
        };
        if (mm.AttentionCell is not null)
        {
            var (az, el) = mm.GetAnglesFromCell(mm.AttentionCell);
            state.AttentionAzimuth = az.Degrees;
            state.AttentionElevation = el.Degrees;
        }
        var cells = mm._cells;
        if (cells is null || cells.Length == 0) return state;

        List<double> edges = ElevationEdges(mm, cells.Length);
        DateTime now = DateTime.Now;
        for (int r = 0; r < cells.Length; r++)
        {
            if (cells[r] is null || cells[r].Length == 0) continue;
            var ring = new MentalRingDto { ElevationFrom = edges[r], ElevationTo = edges[r + 1] };
            int rays = cells[r].Length;
            for (int k = 0; k < rays; k++)
            {
                Thought t = cells[r][k];
                var cell = new MentalCellDto
                {
                    Label = t.Label,
                    AzimuthFrom = mm.InvertWarp((double)k / rays * 2 - 1) * 180,
                    AzimuthTo = mm.InvertWarp((double)(k + 1) / rays * 2 - 1) * 180,
                    InView = mm.IsInVisualField(t),
                    IsCenter = t == mm.Center,
                };
                double firedAgo = (now - t.LastFiredTime).TotalSeconds;
                if (firedAgo >= 0 && firedAgo < 5) cell.FiredAgo = firedAgo;
                var contains = t.LinksTo.Where(x => x.LinkType?.Label == "_mm:contains").ToList();
                cell.Attended = contains.Any(l => l.To?.Label == "attention");
                var contents = contains.Where(l => l.To is not null && !l.To.Label.Equals("attention", StringComparison.OrdinalIgnoreCase))
                    .Select(l => Content(mm, l, now)).ToList();
                if (contents.Count > 0) cell.Contents = contents;
                ring.Cells.Add(cell);
            }
            state.Rings.Add(ring);
        }
        return state;
    }

    static MentalContentDto Content(ModuleMentalModel parent, Link containsLink, DateTime now)
    {
        Thought content = containsLink.To;
        string imagePath = GroundedImageResolver.ResolveImagePath(content);
        double? secondsLeft = null;
        if (containsLink.TimeToLive < TimeSpan.MaxValue && containsLink.TimeToLive <= DateTime.MaxValue - containsLink.LastFiredTime)
            secondsLeft = Math.Max(0, (containsLink.LastFiredTime + containsLink.TimeToLive - now).TotalSeconds);
        return new MentalContentDto
        {
            Label = content.Label,
            ImagePath = imagePath,
            Attributes = imagePath is null ? KnownAttributes(parent.theUKS, content) : null,
            Imagined = ModuleMentalModel.IsImaginedThought(content),
            Distance = Distance(containsLink),
            SecondsLeft = secondsLeft,
        };
    }

    /// <summary>The attributes a concept card lists: not grounding links, properties or the sheet's own.</summary>
    static List<string[]> KnownAttributes(UKS.UKS uks, Thought thought) =>
        uks.GetAttributes(thought)
            .Where(link => link.LinkType is not null && link.To is not null)
            .Where(link => !link.LinkType.HasProperty("isGrounding"))
            .Where(link => !link.LinkType.Label.Equals("hasProperty", StringComparison.OrdinalIgnoreCase))
            .Where(link => !link.LinkType.Label.StartsWith("_mm:", StringComparison.OrdinalIgnoreCase))
            .Select(link => new[] { link.LinkType.Label, link.To.Label })
            .DistinctBy(pair => (pair[0].ToLowerInvariant(), pair[1].ToLowerInvariant()))
            .OrderBy(pair => pair[0], StringComparer.OrdinalIgnoreCase).ThenBy(pair => pair[1], StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The elevation bands of the rings, bottom to top, from the module's own binning.</summary>
    static List<double> ElevationEdges(ModuleMentalModel parent, int ringCount)
    {
        List<double> edges = new() { -90 };
        int lastBin = parent.GetBinFromElevation(-90);
        for (double deg = -89.5; deg <= 90.0; deg += 0.5)
        {
            int bin = parent.GetBinFromElevation((float)deg);
            if (bin != lastBin) { edges.Add(deg); lastBin = bin; }
        }
        edges.Add(90);
        if (edges.Count != ringCount + 1)
            edges = Enumerable.Range(0, ringCount + 1).Select(i => -90 + i * (180.0 / ringCount)).ToList();
        return edges;
    }

    static double Distance(Link l)
    {
        var dLink = l.LinksTo.FirstOrDefault(x => x.LinkType?.Label == "distance");
        return dLink?.To?.Label?.StartsWith("distance:") == true &&
               double.TryParse(dLink.To.Label["distance:".Length..], out double val) ? val : 0;
    }
}

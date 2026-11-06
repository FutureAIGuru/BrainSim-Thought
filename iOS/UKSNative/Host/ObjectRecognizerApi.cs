// The logic behind the Object Recognizer's dialog (new on iOS; the module has no desktop
// dialog): what the last look saw, what it was matched to, what is known, and the
// operations (look, teach, confirm, forget, settings). The app presents and draws them.
using UKS;

namespace BrainSimulator.Modules;

public sealed class ColorShareDto
{
    public string Name { get; set; } = "";
    public float Fraction { get; set; }
}

public sealed class RecognizerMatchDto
{
    public string Label { get; set; } = "";
    public float Score { get; set; }
}

public sealed class KnownObjectDto
{
    public string Label { get; set; } = "";
    public string Color { get; set; }
    public int Views { get; set; }
    /// <summary>Named automatically (Thing1, Thing2…), not by the user.</summary>
    public bool Anonymous { get; set; }
}

public sealed class RecognizerObservationDto
{
    public int BoundaryCount { get; set; }
    /// <summary>Width over height of the outline.</summary>
    public float Aspect { get; set; }
    public List<ColorShareDto> Colors { get; set; } = new();
}

public sealed class ObjectRecognizerState
{
    public string ImagePath { get; set; }
    public string Status { get; set; }
    public bool Looking { get; set; }
    /// <summary>Changes whenever a new picture is looked at (see the "picture" query).</summary>
    public int PictureRevision { get; set; }
    public RecognizerObservationDto Observation { get; set; }
    public List<RecognizerMatchDto> Matches { get; set; } = new();
    /// <summary>What the last look was taken to be, and whether it was recognized (rather than learned).</summary>
    public string Object { get; set; }
    public bool Recognized { get; set; }
    public bool ObjectIsAnonymous { get; set; }
    /// <summary>A close match worth asking about ("Is it a …?"), if any.</summary>
    public string Guess { get; set; }
    public List<KnownObjectDto> Known { get; set; } = new();
    public bool LearnUnknownObjects { get; set; }
    public float RecognizeThreshold { get; set; }
}

public sealed class RecognizerPictureDto
{
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Width×Height RGB bytes, row by row, base64.</summary>
    public string Pixels { get; set; }
    /// <summary>Boundary points, as [x, y] pairs.</summary>
    public List<float[]> Boundary { get; set; } = new();
    /// <summary>The object's box [x0, y0, x1, y1], when an object was seen.</summary>
    public int[] Box { get; set; }
}

sealed class ObjectRecognizerApi : ModuleApi
{
    static readonly string[] ColorNames = { "red", "orange", "yellow", "green", "cyan", "blue", "purple", "pink", "white", "gray", "black" };

    volatile bool looking;
    object lastPixels;
    int pictureRevision;

    static bool IsAnonymous(ModuleObjectRecognizer r, Thought t) =>
        t.Label.StartsWith(r.AnonymousObjectPrefix, StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(t.Label[r.AnonymousObjectPrefix.Length..], out _);

    public override string State(ModuleBase m)
    {
        var r = (ModuleObjectRecognizer)m;
        if (!ReferenceEquals(lastPixels, r.LastPixels)) { lastPixels = r.LastPixels; pictureRevision++; }
        var state = new ObjectRecognizerState
        {
            ImagePath = r.LastImagePath,
            Status = r.Status,
            Looking = looking,
            PictureRevision = pictureRevision,
            Matches = r.LastMatches.Select(x => new RecognizerMatchDto { Label = x.Object.Label, Score = x.Score }).ToList(),
            Object = r.LastObject?.Label,
            Recognized = r.LastRecognized,
            ObjectIsAnonymous = r.LastObject is not null && IsAnonymous(r, r.LastObject),
            Known = r.KnownObjects().Select(x => new KnownObjectDto
            {
                Label = x.obj.Label,
                Color = x.obj.LinksTo.FirstOrDefault(l => l.LinkType?.Label == ModuleObjectRecognizer.HasColorLabel)?.To?.Label,
                Views = x.appearances.Count,
                Anonymous = IsAnonymous(r, x.obj),
            }).ToList(),
            LearnUnknownObjects = r.LearnUnknownObjects,
            RecognizeThreshold = r.RecognizeThreshold,
        };
        if (r.LastObservation is { } obs)
            state.Observation = new RecognizerObservationDto
            {
                BoundaryCount = obs.BoundaryCount,
                Aspect = obs.Aspect,
                Colors = obs.Colors.Select((f, i) => new ColorShareDto { Name = ColorNames[i], Fraction = f }).ToList(),
            };
        // worth asking about: the best other match above the guess threshold, unless a named object was recognized
        var guess = r.LastMatches.FirstOrDefault(x => x.Object != r.LastObject && x.Score >= r.GuessThreshold);
        if (guess is not null && !(r.LastRecognized && r.LastObject is not null && !IsAnonymous(r, r.LastObject)))
            state.Guess = guess.Object.Label;
        return Json(state, ApiJson.Default.ObjectRecognizerState);
    }

    public override string Query(ModuleBase m, string op, ApiArgs args)
    {
        if (op != "picture") return base.Query(m, op, args);
        var r = (ModuleObjectRecognizer)m;
        var picture = new RecognizerPictureDto();
        if (r.LastPixels is { } pixels)
        {
            picture.Width = pixels.GetLength(0);
            picture.Height = pixels.GetLength(1);
            picture.Pixels = VisionApi.EncodePixels(pixels);
            picture.Boundary = r.LastBoundary.Select(p => new[] { p.X, p.Y }).ToList();
            if (r.LastObservation is not null)
            {
                var (x0, y0, x1, y1) = r.LastBox;
                picture.Box = new[] { x0, y0, x1, y1 };
            }
        }
        return Json(picture, ApiJson.Default.RecognizerPictureDto);
    }

    public override string Call(ModuleBase m, string op, ApiArgs args)
    {
        var r = (ModuleObjectRecognizer)m;
        switch (op)
        {
            case "look":
                // off the main thread; the state says Looking until it is done
                string path = args.Str("path");
                looking = true;
                Watch(UKSNative.Exports.RunLocked(() =>
                {
                    try { r.Look(path); }
                    finally { looking = false; r.UpdateDialog(); }
                }));
                break;
            case "confirm": r.ConfirmGuess(); break;
            case "teach": r.Teach(args.Str("name")); break;
            case "forget": r.Forget(m.theUKS.Labeled(args.Str("object")) ?? throw new ArgumentException("No such object.")); break;
            case "setLearnUnknown": r.LearnUnknownObjects = args.Bool("value"); break;
            case "setRecognizeThreshold":
                r.RecognizeThreshold = (float)args.Num("value", r.RecognizeThreshold);
                r.GuessThreshold = Math.Min(r.GuessThreshold, r.RecognizeThreshold - 0.05f);
                break;
            default: return Unknown(op);
        }
        return Done();
    }
}

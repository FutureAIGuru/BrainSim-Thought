/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UKS;

namespace BrainSimulator.Modules;

/// <summary>
/// Presents external visual-observation files to the Mental Model. Listing a
/// file does not alter the UKS; its subject Thought is created only when the
/// observation is presented.
/// </summary>
public class ModuleVisualInput : ModuleBase
{
    private enum LessonEventKind
    {
        Show,
        FillAbove,
        FillBelow,
        Hear,
        Clear,
        Attention,
    }

    private sealed record LessonEvent(
        LessonEventKind Kind,
        string Content,
        double Distance,
        double? Horizontal,
        double? Vertical,
        int SourceLine,
        bool AdditionalAppearance = false)
    {
        public string Description => Kind switch
        {
            LessonEventKind.Show => $"Show {Content}" +
                (AdditionalAppearance ? " as another appearance" : string.Empty) +
                $" at distance {Distance:0.##}" +
                FormatLocation(Horizontal, Vertical),
            LessonEventKind.FillAbove =>
                $"Fill visible cells above Vert {Vertical:0.##} with {Content}",
            LessonEventKind.FillBelow =>
                $"Fill visible cells below Vert {Vertical:0.##} with {Content}",
            LessonEventKind.Hear => $"Hear: {Content}",
            LessonEventKind.Clear => "Clear the Mental Model",
            LessonEventKind.Attention => "Set Attention" +
                FormatLocation(Horizontal, Vertical),
            _ => Content,
        };

        private static string FormatLocation(double? horizontal, double? vertical)
        {
            if (!horizontal.HasValue && !vertical.HasValue) return string.Empty;
            return $" at Horiz {horizontal?.ToString("0.##") ?? "current"}, " +
                $"Vert {vertical?.ToString("0.##") ?? "current"}";
        }
    }

    private sealed record ObservationAttribute(
        string LinkTypeLabel,
        string TargetLabel,
        float Weight);

    private sealed record ObservationDescription(
        string ImageFile,
        IReadOnlyList<ObservationAttribute> Attributes);

    public static string VisualElementLabel { get; set; } = "VisualElement";
    public static string ImageLabel { get; set; } = "Image";
    public static string HasImageLabel { get; set; } = "hasImage";

    public TimeSpan PresentationLifetime { get; set; } = TimeSpan.FromSeconds(10);
    public double LessonStepIntervalSeconds { get; set; } = 2;
    public float MeaningInitialWeight { get; set; } = 0.1f;
    public float MeaningReinforcement { get; set; } = 0.1f;
    public float MeaningDecayFactor { get; set; } = 0.9f;
    public float MeaningPruneThreshold { get; set; } = 0.05f;
    public float MeaningMaximumWeight { get; set; } = 1f;
    public float MeaningResolutionTieTolerance { get; set; } = 0.001f;
    public float MeaningConsolidationThreshold { get; set; } = 0.9f;
    public float MeaningConsolidationDiscardThreshold { get; set; } = 0.5f;
    public string AnonymousObjectPrefix { get; set; } = "O";
    public TimeSpan ImaginationLifetime { get; set; } = TimeSpan.FromSeconds(10);
    public ISet<string> BlockedMeaningLabels { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Object",
            "Thought",
            "Unknown",
            "mentalModel",
            "Abstract",
            "activeThought",
            "imaginedThought",
            "inActiveThought",
            "attention",
        };
    private static string SubjectHeader => "# subject:";
    private readonly List<LessonEvent> _lessonEvents = new();
    private readonly List<string> _lastGroundingChanges = new();
    private DateTime _nextLessonStepTime = DateTime.MaxValue;

    public string CurrentVisualLabel { get; private set; } = string.Empty;
    public string CurrentWord { get; private set; } = string.Empty;
    public string CurrentLessonLanguage { get; private set; } = string.Empty;
    public string SelectedObservationFile { get; private set; } = string.Empty;
    public string SelectedLessonFile { get; private set; } = string.Empty;
    public int NextLessonStepIndex { get; private set; }
    public bool IsLessonRunning { get; private set; }
    public string Status { get; private set; } = "Ready";
    public IReadOnlyList<string> LessonSteps =>
        _lessonEvents.Select(item => item.Description).ToList();
    public IReadOnlyList<string> LastGroundingChanges => _lastGroundingChanges;

    public override void Fire()
    {
        Init();
        if (IsLessonRunning && DateTime.Now >= _nextLessonStepTime)
        {
            bool advanced = StepLesson();
            if (advanced && IsLessonRunning)
                _nextLessonStepTime = DateTime.Now + TimeSpan.FromSeconds(
                    Math.Max(0.1, LessonStepIntervalSeconds));
        }
        UpdateDialog();
    }

    public override void Initialize()
    {
    }

    public override void UKSInitializedNotification()
    {
        GetUKS();
        EnsureVocabulary();
        CurrentVisualLabel = string.Empty;
        CurrentWord = string.Empty;
        CurrentLessonLanguage = string.Empty;
        SelectedLessonFile = string.Empty;
        _lessonEvents.Clear();
        NextLessonStepIndex = 0;
        IsLessonRunning = false;
        Status = "Ready";
    }

    public void EnsureVocabulary()
    {
        if (theUKS is null) return;

        theUKS.GetOrAddThought(VisualElementLabel, "Thought");
        theUKS.GetOrAddThought(ImageLabel, VisualElementLabel);
        Thought hasImage = theUKS.GetOrAddThought(HasImageLabel, "LinkType");
        hasImage.AddProperty(theUKS.GetOrAddThought("isGrounding", "Property"));
        theUKS.GetOrAddThought("distance", "LinkType");
    }

    public IReadOnlyList<string> GetObservationFiles()
    {
        return CandidateObservationDirectories()
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.txt"))
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetLessonFiles()
    {
        return GroundedContentLocator.CandidateDirectories("Lessons")
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.txt"))
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void SelectObservationFile(string fileName)
    {
        SelectedObservationFile = IsSafeObservationFileName(fileName)
            ? fileName.Trim()
            : string.Empty;
        Status = "Ready";
    }

    public bool SelectLessonFile(string fileName)
    {
        PauseLesson(updateStatus: false);
        _lessonEvents.Clear();
        NextLessonStepIndex = 0;
        CurrentLessonLanguage = string.Empty;
        SelectedLessonFile = IsSafeLessonFileName(fileName)
            ? fileName.Trim()
            : string.Empty;

        string path = ResolveContentPath(
            SelectedLessonFile,
            "Lessons",
            IsSafeLessonFileName);
        if (path is null)
        {
            Status = "Select a valid lesson file.";
            UpdateDialog();
            return false;
        }

        try
        {
            int sourceLine = 0;
            foreach (string rawLine in File.ReadLines(path))
            {
                sourceLine++;
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                List<string> tokens = Regex.Matches(line, @"""[^""\r\n]*""|\S+")
                    .Select(match => match.Value.Trim('"'))
                    .ToList();
                string command = tokens.FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;

                if (command == "language")
                {
                    if (tokens.Count != 2 || _lessonEvents.Count > 0 ||
                        !Regex.IsMatch(tokens[1], @"^[\p{L}\p{Nd}_-]+$"))
                    {
                        Status = $"Invalid language declaration on lesson line {sourceLine}.";
                        _lessonEvents.Clear();
                        UpdateDialog();
                        return false;
                    }
                    CurrentLessonLanguage = tokens[1];
                    EnsureLanguage(CurrentLessonLanguage);
                    continue;
                }

                if (command is "show" or "present")
                {
                    if (tokens.Count < 2 || !IsSafeObservationFileName(tokens[1]))
                    {
                        Status = $"Invalid observation on lesson line {sourceLine}.";
                        _lessonEvents.Clear();
                        UpdateDialog();
                        return false;
                    }

                    string observation = tokens[1];
                    double distance = 1;
                    double? horizontal = null;
                    double? vertical = null;
                    if (!TryParseShowOptions(tokens, 2,
                        ref distance, ref horizontal, ref vertical,
                        out bool additionalAppearance, out string error))
                    {
                        Status = $"{error} on lesson line {sourceLine}.";
                        _lessonEvents.Clear();
                        UpdateDialog();
                        return false;
                    }
                    _lessonEvents.Add(new LessonEvent(
                        LessonEventKind.Show, observation, distance,
                        horizontal, vertical, sourceLine,
                        additionalAppearance));
                    continue;
                }

                if (command == "fill")
                {
                    string fillError = string.Empty;
                    double fillDistance = 1;
                    bool fillAbove = true;
                    double threshold = 0;
                    if (tokens.Count < 4 || !IsSafeObservationFileName(tokens[1]) ||
                        !TryParseFillOptions(tokens, 2, out fillDistance,
                            out fillAbove, out threshold, out fillError))
                    {
                        Status = $"{(string.IsNullOrWhiteSpace(fillError)
                            ? "Invalid fill command" : fillError)} " +
                            $"on lesson line {sourceLine}.";
                        _lessonEvents.Clear();
                        UpdateDialog();
                        return false;
                    }
                    _lessonEvents.Add(new LessonEvent(
                        fillAbove ? LessonEventKind.FillAbove : LessonEventKind.FillBelow,
                        tokens[1], fillDistance, null, threshold, sourceLine));
                    continue;
                }

                if (command == "hear")
                {
                    string phrase = line.Length > tokens[0].Length
                        ? line[tokens[0].Length..].Trim()
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(phrase))
                    {
                        Status = $"Missing phrase on lesson line {sourceLine}.";
                        _lessonEvents.Clear();
                        UpdateDialog();
                        return false;
                    }
                    _lessonEvents.Add(new LessonEvent(
                        LessonEventKind.Hear,
                        phrase, 0, null, null, sourceLine));
                    continue;
                }

                if (command == "clear" && tokens.Count == 1)
                {
                    _lessonEvents.Add(new LessonEvent(
                        LessonEventKind.Clear, string.Empty, 0,
                        null, null, sourceLine));
                    continue;
                }

                if (command == "attention")
                {
                    double unusedDistance = 1;
                    double? horizontal = null;
                    double? vertical = null;
                    if (!TryParseLessonOptions(tokens, 1, allowDistance: false,
                            ref unusedDistance, ref horizontal, ref vertical,
                            out string error) ||
                        (!horizontal.HasValue && !vertical.HasValue))
                    {
                        Status = $"{(string.IsNullOrWhiteSpace(error) ?
                            "Attention needs horiz and/or vert" : error)} " +
                            $"on lesson line {sourceLine}.";
                        _lessonEvents.Clear();
                        UpdateDialog();
                        return false;
                    }
                    _lessonEvents.Add(new LessonEvent(
                        LessonEventKind.Attention, string.Empty, 0,
                        horizontal, vertical, sourceLine));
                    continue;
                }

                Status = $"Unknown command on lesson line {sourceLine}: {line}";
                _lessonEvents.Clear();
                UpdateDialog();
                return false;
            }
        }
        catch (IOException ex)
        {
            Status = $"Could not read lesson: {ex.Message}";
            _lessonEvents.Clear();
            UpdateDialog();
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Status = $"Could not read lesson: {ex.Message}";
            _lessonEvents.Clear();
            UpdateDialog();
            return false;
        }

        Status = _lessonEvents.Count == 0
            ? $"{SelectedLessonFile} contains no lesson events."
            : $"Loaded {SelectedLessonFile}: {_lessonEvents.Count} events" +
                (string.IsNullOrWhiteSpace(CurrentLessonLanguage)
                    ? "."
                    : $", language {CurrentLessonLanguage}.");
        UpdateDialog();
        return _lessonEvents.Count > 0;
    }

    public bool StepLesson(ModuleMentalModel mentalModel = null)
    {
        if (_lessonEvents.Count == 0)
        {
            string firstLesson = GetLessonFiles().FirstOrDefault() ?? string.Empty;
            if (!SelectLessonFile(string.IsNullOrWhiteSpace(SelectedLessonFile)
                    ? firstLesson
                    : SelectedLessonFile))
                return false;
        }

        if (NextLessonStepIndex >= _lessonEvents.Count)
            NextLessonStepIndex = 0;

        mentalModel ??= GetMentalModel();
        if (mentalModel is null)
        {
            IsLessonRunning = false;
            Status = "Add a Mental Model module first.";
            UpdateDialog();
            return false;
        }

        LessonEvent lessonEvent = _lessonEvents[NextLessonStepIndex];
        bool succeeded = lessonEvent.Kind switch
        {
            LessonEventKind.Show => PresentLessonObservation(lessonEvent, mentalModel),
            LessonEventKind.FillAbove or LessonEventKind.FillBelow =>
                FillLessonRegion(lessonEvent, mentalModel),
            LessonEventKind.Hear => HearLessonPhrase(lessonEvent.Content, mentalModel),
            LessonEventKind.Clear => ClearLessonPresentation(mentalModel),
            LessonEventKind.Attention => SetLessonAttention(
                mentalModel, lessonEvent.Horizontal, lessonEvent.Vertical),
            _ => false,
        };

        if (!succeeded)
        {
            IsLessonRunning = false;
            UpdateDialog();
            return false;
        }

        NextLessonStepIndex++;
        string eventStatus = Status;
        int completedStep = NextLessonStepIndex;
        if (NextLessonStepIndex >= _lessonEvents.Count)
        {
            NextLessonStepIndex = 0;
            Status = $"Step {completedStep}/{_lessonEvents.Count}: {eventStatus} " +
                "Lesson rewound to the beginning.";
        }
        else
        {
            Status = $"Step {completedStep}/{_lessonEvents.Count}: {eventStatus}";
        }
        UpdateDialog();
        return true;
    }

    public bool StartLesson(double intervalSeconds)
    {
        if (double.IsFinite(intervalSeconds) && intervalSeconds > 0)
            LessonStepIntervalSeconds = intervalSeconds;

        if (_lessonEvents.Count == 0)
        {
            string lesson = string.IsNullOrWhiteSpace(SelectedLessonFile)
                ? GetLessonFiles().FirstOrDefault()
                : SelectedLessonFile;
            if (!SelectLessonFile(lesson)) return false;
        }
        if (NextLessonStepIndex >= _lessonEvents.Count)
            NextLessonStepIndex = 0;

        IsLessonRunning = true;
        _nextLessonStepTime = DateTime.Now;
        Status = $"Running {SelectedLessonFile}.";
        UpdateDialog();
        return true;
    }

    public void PauseLesson(bool updateStatus = true)
    {
        IsLessonRunning = false;
        _nextLessonStepTime = DateTime.MaxValue;
        if (updateStatus)
        {
            Status = $"Paused before step {NextLessonStepIndex + 1}.";
            UpdateDialog();
        }
    }

    public void ResetLesson()
    {
        PauseLesson(updateStatus: false);
        NextLessonStepIndex = 0;
        Status = string.IsNullOrWhiteSpace(SelectedLessonFile)
            ? "Ready"
            : $"Reset {SelectedLessonFile}.";
        UpdateDialog();
    }

    private bool PresentLessonObservation(
        LessonEvent lessonEvent,
        ModuleMentalModel mentalModel)
    {
        SelectObservationFile(lessonEvent.Content);
        Thought location = null;
        if (lessonEvent.Horizontal.HasValue || lessonEvent.Vertical.HasValue)
            location = GetCellAtLessonPosition(
                mentalModel, lessonEvent.Horizontal, lessonEvent.Vertical);
        return PresentSelected(
            lessonEvent.Distance,
            mentalModel,
            location,
            lessonEvent.AdditionalAppearance) is not null;
    }

    private bool FillLessonRegion(
        LessonEvent lessonEvent,
        ModuleMentalModel mentalModel)
    {
        double threshold = lessonEvent.Vertical ?? 0;
        List<Thought> cells = mentalModel._cells
            .SelectMany(ring => ring ?? Array.Empty<Thought>())
            .Where(mentalModel.IsInVisualField)
            .Where(cell =>
            {
                double vertical = mentalModel.GetAnglesFromCell(cell)
                    .elevation.Degrees;
                return lessonEvent.Kind == LessonEventKind.FillAbove
                    ? vertical > threshold
                    : vertical < threshold;
            })
            .OrderBy(cell => mentalModel.GetAnglesFromCell(cell).azimuth.Degrees)
            .ToList();
        if (cells.Count == 0)
        {
            Status = $"No visible Mental Model cells match the fill region.";
            return false;
        }

        SelectObservationFile(lessonEvent.Content);
        Thought subject = null;
        for (int index = 0; index < cells.Count; index++)
        {
            subject = PresentSelected(
                lessonEvent.Distance,
                mentalModel,
                cells[index],
                additionalAppearance: index > 0);
            if (subject is null) return false;
        }

        Status = $"Filled {cells.Count} visible cells with {subject.Label}.";
        return true;
    }

    private bool HearLessonPhrase(string phrase, ModuleMentalModel mentalModel)
    {
        if (mentalModel.GetAttendedContents().Count == 0)
        {
            Status = $"Skipped \"{phrase}\": nothing occupies Attention.";
            return true;
        }

        HearPhrase(phrase, mentalModel, CurrentLessonLanguage);
        return true;
    }

    private bool ClearLessonPresentation(ModuleMentalModel mentalModel)
    {
        ClearPresentation(mentalModel);
        Status = "Mental Model cleared.";
        return true;
    }

    private bool SetLessonAttention(
        ModuleMentalModel mentalModel,
        double? horizontal,
        double? vertical)
    {
        Thought targetCell = GetCellAtLessonPosition(
            mentalModel, horizontal, vertical);
        ModuleAttention attention = MainWindow.theWindow?.activeModules
            .OfType<ModuleAttention>()
            .FirstOrDefault();
        mentalModel.SetAttentionCell(targetCell);
        attention?.SetCenterOfAttention(targetCell);

        var position = mentalModel.GetAnglesFromCell(targetCell);
        Status = $"Attention - Horiz: {position.azimuth.Degrees:0.#}\u00B0, " +
            $"Vert: {position.elevation.Degrees:0.#}\u00B0";
        return true;
    }

    private static Thought GetCellAtLessonPosition(
        ModuleMentalModel mentalModel,
        double? horizontal,
        double? vertical)
    {
        Thought currentCell = mentalModel.AttentionCell ?? mentalModel.Center;
        var current = mentalModel.GetAnglesFromCell(currentCell);
        double azimuth = horizontal ?? current.azimuth.Degrees;
        double elevation = vertical ?? current.elevation.Degrees;
        return mentalModel.GetCell(
            Angle.FromDegrees((float)azimuth),
            Angle.FromDegrees((float)elevation));
    }

    private static bool TryParseLessonOptions(
        IReadOnlyList<string> tokens,
        int firstOption,
        bool allowDistance,
        ref double distance,
        ref double? horizontal,
        ref double? vertical,
        out string error)
    {
        error = string.Empty;
        if ((tokens.Count - firstOption) % 2 != 0)
        {
            error = "Each lesson option needs a numeric value";
            return false;
        }

        for (int index = firstOption; index < tokens.Count; index += 2)
        {
            string option = tokens[index].ToLowerInvariant();
            if (!double.TryParse(tokens[index + 1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value) ||
                !double.IsFinite(value))
            {
                error = $"Invalid value for {tokens[index]}";
                return false;
            }

            switch (option)
            {
                case "distance" when allowDistance && value >= 1:
                    distance = value;
                    break;
                case "horiz":
                case "horizontal":
                    horizontal = value;
                    break;
                case "vert":
                case "vertical":
                    vertical = value;
                    break;
                case "distance" when allowDistance:
                    error = "Distance must be at least 1";
                    return false;
                default:
                    error = $"Unknown option {tokens[index]}";
                    return false;
            }
        }
        return true;
    }

    private static bool TryParseShowOptions(
        IReadOnlyList<string> tokens,
        int firstOption,
        ref double distance,
        ref double? horizontal,
        ref double? vertical,
        out bool additionalAppearance,
        out string error)
    {
        additionalAppearance = false;
        List<string> numericOptions = tokens.Take(firstOption).ToList();
        for (int index = firstOption; index < tokens.Count;)
        {
            if (tokens[index].Equals(
                "appearance", StringComparison.OrdinalIgnoreCase))
            {
                additionalAppearance = true;
                index++;
                continue;
            }

            if (index + 1 >= tokens.Count)
            {
                error = $"Option {tokens[index]} needs a numeric value";
                return false;
            }

            numericOptions.Add(tokens[index]);
            numericOptions.Add(tokens[index + 1]);
            index += 2;
        }

        return TryParseLessonOptions(
            numericOptions,
            firstOption,
            allowDistance: true,
            ref distance,
            ref horizontal,
            ref vertical,
            out error);
    }

    private static bool TryParseFillOptions(
        IReadOnlyList<string> tokens,
        int firstOption,
        out double distance,
        out bool fillAbove,
        out double threshold,
        out string error)
    {
        distance = 1;
        fillAbove = true;
        threshold = 0;
        error = string.Empty;
        bool hasRegion = false;

        if ((tokens.Count - firstOption) % 2 != 0)
        {
            error = "Each fill option needs a numeric value";
            return false;
        }

        for (int index = firstOption; index < tokens.Count; index += 2)
        {
            if (!double.TryParse(tokens[index + 1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value) ||
                !double.IsFinite(value))
            {
                error = $"Invalid value for {tokens[index]}";
                return false;
            }

            switch (tokens[index].ToLowerInvariant())
            {
                case "distance" when value >= 1:
                    distance = value;
                    break;
                case "above" when !hasRegion:
                    fillAbove = true;
                    threshold = value;
                    hasRegion = true;
                    break;
                case "below" when !hasRegion:
                    fillAbove = false;
                    threshold = value;
                    hasRegion = true;
                    break;
                case "distance":
                    error = "Distance must be at least 1";
                    return false;
                default:
                    error = $"Unknown or duplicate fill option {tokens[index]}";
                    return false;
            }
        }

        if (!hasRegion)
        {
            error = "Fill needs an above or below threshold";
            return false;
        }
        return true;
    }

    public Thought PresentSelected(double distance)
    {
        ModuleMentalModel mentalModel = GetMentalModel();
        if (mentalModel is null)
        {
            Status = "Add a Mental Model module first.";
            UpdateDialog();
            return null;
        }

        return PresentSelected(distance, mentalModel);
    }

    public Thought PresentSelected(double distance, ModuleMentalModel mentalModel)
    {
        return PresentSelected(distance, mentalModel, null);
    }

    public Thought PresentSelected(
        double distance,
        ModuleMentalModel mentalModel,
        Thought location,
        bool additionalAppearance = false)
    {
        if (theUKS is null || mentalModel is null) return null;
        EnsureVocabulary();

        if (!double.IsFinite(distance) || distance < 1)
        {
            Status = "Distance must be at least 1.";
            UpdateDialog();
            return null;
        }

        if (string.IsNullOrWhiteSpace(SelectedObservationFile))
            SelectedObservationFile = GetObservationFiles().FirstOrDefault() ?? string.Empty;

        string path = ResolveObservationPath(SelectedObservationFile);
        if (path is null)
        {
            Status = "Select a valid observation file.";
            UpdateDialog();
            return null;
        }

        ObservationDescription observation = ReadObservation(path);
        if (observation is null || observation.Attributes.Count == 0)
        {
            Status = $"{SelectedObservationFile} has no usable attributes.";
            UpdateDialog();
            return null;
        }

        Thought subject = FindMatchingObject(observation);
        bool recognized = subject is not null;
        subject ??= theUKS.GetOrAddThought(
            GetNextAnonymousObjectLabel(), "Object");
        if (subject is null)
        {
            Status = $"{SelectedObservationFile} could not create an object.";
            UpdateDialog();
            return null;
        }

        ApplyObservation(subject, observation);
        if (recognized)
            subject.Fire();

        Thought targetCell = location ?? mentalModel.AttentionCell ??
            mentalModel.SetAttentionCell(mentalModel.Center);
        Link binding = additionalAppearance
            ? mentalModel.BindThoughtAppearanceToMentalModel(subject, targetCell)
            : mentalModel.BindThoughtToMentalModel(subject, targetCell);
        if (binding is null) return null;
        binding.TimeToLive = PresentationLifetime;
        SetBindingDistance(binding, distance);

        var position = mentalModel.GetAnglesFromCell(targetCell);
        CurrentVisualLabel = subject.Label;
        CurrentWord = GetBestWordFor(subject);
        string recognition = recognized ? "Recognized" : "Created";
        string wordStatus = string.IsNullOrWhiteSpace(CurrentWord)
            ? string.Empty
            : $", Word: {CurrentWord}";
        Status = $"{recognition} {subject.Label} - " +
            $"Horiz: {position.azimuth.Degrees:0.#}\u00B0, " +
            $"Vert: {position.elevation.Degrees:0.#}\u00B0, Distance: {distance:0.##}";
        Status += wordStatus;
        UpdateDialog();
        return subject;
    }

    public Thought ImagineWord(string text, ModuleMentalModel mentalModel = null)
    {
        return ImagineWord(text, 1, mentalModel);
    }

    public Thought ImagineWord(
        string text,
        double distance,
        ModuleMentalModel mentalModel = null)
    {
        mentalModel ??= GetMentalModel();
        if (theUKS is null || mentalModel is null)
        {
            Status = "Add a Mental Model module first.";
            UpdateDialog();
            return null;
        }

        if (!double.IsFinite(distance) || distance < 1)
        {
            Status = "Distance must be at least 1.";
            UpdateDialog();
            return null;
        }

        string token = Regex.Match(text ?? string.Empty, @"[\p{L}\p{Nd}]+")
            .Value.ToLowerInvariant();
        Thought word = string.IsNullOrWhiteSpace(token)
            ? null
            : theUKS.Labeled("w:" + token);
        Link meaning = GetBestMeaning(word);
        if (meaning?.To is null)
        {
            Status = $"No known meaning for \"{text?.Trim()}\".";
            UpdateDialog();
            return null;
        }

        Thought targetCell = mentalModel.AttentionCell ?? mentalModel.Center;
        Link binding = mentalModel.ImagineThought(meaning.To, targetCell);
        if (binding is null) return null;
        binding.TimeToLive = ImaginationLifetime;
        SetBindingDistance(binding, distance);
        CurrentWord = token;
        CurrentVisualLabel = meaning.To.Label;
        Status = $"Imagining {meaning.To.Label} from \"{token}\" " +
            $"at distance {distance:0.##}.";
        UpdateDialog();
        return meaning.To;
    }

    /// <summary>
    /// Simulates hearing a phrase in the current visual context. Every word
    /// initially retains all plausible attended meanings; repetition and decay
    /// determine which candidates survive.
    /// </summary>
    public IReadOnlyList<Link> HearPhrase(
        string phrase,
        ModuleMentalModel mentalModel = null,
        string languageLabel = null)
    {
        _lastGroundingChanges.Clear();
        mentalModel ??= GetMentalModel();
        if (theUKS is null || mentalModel is null)
        {
            Status = "Add a Mental Model module first.";
            UpdateDialog();
            return Array.Empty<Link>();
        }
        if (string.IsNullOrWhiteSpace(phrase))
        {
            Status = "The phrase is empty.";
            UpdateDialog();
            return Array.Empty<Link>();
        }

        List<Thought> candidates = GetMeaningCandidates(mentalModel);
        if (candidates.Count == 0)
        {
            Status = "Nothing currently occupies the Attention location.";
            UpdateDialog();
            return Array.Empty<Link>();
        }

        string textStatus = ModuleText.AddText(
            phrase,
            applyExistingTemplates: false,
            learnIncrementally: false);
        Thought means = theUKS.GetOrAddThought("means", "LinkType");
        HashSet<Thought> heardWords = GetPhraseWords(phrase).ToHashSet();
        Thought currentLanguage = EnsureLanguage(languageLabel);
        Thought usedInLanguage = currentLanguage is null
            ? null
            : theUKS.GetOrAddThought("usedInLanguage", "LinkType");
        if (currentLanguage is not null)
        {
            foreach (Thought heardWord in heardWords)
                heardWord.AddLink(usedInLanguage, currentLanguage);
        }
        HashSet<Thought> activeCandidates = candidates.ToHashSet();
        HashSet<Link> newLinks = new();

        foreach (Thought word in heardWords)
        foreach (Thought candidate in activeCandidates)
        {
            Link meaning = word.HasLink(means, candidate);
            if (meaning is not null) continue;

            meaning = word.AddLink(means, candidate);
            if (meaning is null) continue;
            meaning.isPlastic = true;
            meaning.maxWeight = Math.Max(MeaningMaximumWeight, 0.01f);
            meaning.Weight = Math.Clamp(
                MeaningInitialWeight, 0, meaning.maxWeight);
            meaning.Fire();
            newLinks.Add(meaning);
        }

        List<Link> plasticMeanings = theUKS.AtomicThoughts
            .SelectMany(thought => thought.LinksTo)
            .Where(link => link.LinkType == means && link.isPlastic)
            .Distinct()
            .ToList();
        List<Link> changedLinks = new();

        foreach (Link meaning in plasticMeanings)
        {
            bool wordObserved = meaning.From is not null &&
                heardWords.Contains(meaning.From);
            bool targetActive = meaning.To is not null &&
                activeCandidates.Contains(meaning.To);
            bool targetBlocked = meaning.To is null ||
                BlockedMeaningLabels.Contains(meaning.To.Label);
            bool supported = wordObserved && targetActive && !targetBlocked;
            bool wordUsesCurrentLanguage = currentLanguage is null ||
                meaning.From?.HasLink(usedInLanguage, currentLanguage) is not null;
            float oldWeight = newLinks.Contains(meaning) ? 0 : meaning.Weight;

            if (supported)
            {
                if (!newLinks.Contains(meaning))
                {
                    meaning.maxWeight = Math.Max(MeaningMaximumWeight, 0.01f);
                    meaning.Weight = Math.Clamp(
                        meaning.Weight + Math.Max(0, MeaningReinforcement),
                        0,
                        meaning.maxWeight);
                    meaning.Fire();
                }
            }
            else if (wordObserved ||
                (targetActive && wordUsesCurrentLanguage) || targetBlocked)
            {
                meaning.Weight *= Math.Clamp(MeaningDecayFactor, 0, 1);
            }

            if (meaning.Weight != oldWeight)
            {
                changedLinks.Add(meaning);
                _lastGroundingChanges.Add(
                    $"{meaning.From?.Label} -> {meaning.To?.Label}: " +
                    $"{oldWeight:0.00} -> {meaning.Weight:0.00}");
            }
        }

        foreach (Link weakMeaning in plasticMeanings
            .Where(link => link.Weight < Math.Max(0, MeaningPruneThreshold))
            .ToList())
        {
            _lastGroundingChanges.Add(
                $"{weakMeaning.From?.Label} -> {weakMeaning.To?.Label}: removed");
            weakMeaning.From?.RemoveLink(weakMeaning);
        }

        ConsolidateMeanings(plasticMeanings);

        Thought attendedObject = mentalModel.GetAttendedContents()
            .FirstOrDefault(IsAnonymousObject);
        if (attendedObject is not null)
            CurrentWord = GetBestWordFor(attendedObject);

        Status = $"Heard \"{phrase}\"; updated " +
            $"{changedLinks.Distinct().Count()} candidate meanings" +
            (currentLanguage is null ? ". " : $" in {currentLanguage.Label}. ") +
            textStatus;
        UpdateDialog();
        return changedLinks.Distinct().ToList();
    }

    private void ConsolidateMeanings(IEnumerable<Link> plasticMeanings)
    {
        float winnerThreshold = Math.Max(0, MeaningConsolidationThreshold);
        float discardThreshold = Math.Max(0, MeaningConsolidationDiscardThreshold);

        foreach (IGrouping<Thought, Link> wordMeanings in plasticMeanings
            .Where(link => link.From is not null)
            .GroupBy(link => link.From))
        {
            List<Link> currentMeanings = wordMeanings
                .Where(link => link.From?.LinksTo.Contains(link) == true)
                .ToList();
            if (!currentMeanings.Any(link => link.Weight >= winnerThreshold))
                continue;

            foreach (Link weakMeaning in currentMeanings
                .Where(link => link.Weight < discardThreshold)
                .ToList())
            {
                _lastGroundingChanges.Add(
                    $"{weakMeaning.From?.Label} -> {weakMeaning.To?.Label}: " +
                    "removed after consolidation");
                weakMeaning.From?.RemoveLink(weakMeaning);
            }
        }
    }

    public void ClearPresentation()
    {
        ModuleMentalModel mentalModel = GetMentalModel();
        ClearPresentation(mentalModel);
    }

    public void ClearPresentation(ModuleMentalModel mentalModel)
    {
        mentalModel?.ReplaceCurrentContents(Array.Empty<Thought>());
        CurrentVisualLabel = string.Empty;
        Status = "Ready";
        UpdateDialog();
    }

    private void SetBindingDistance(Link binding, double distance)
    {
        foreach (Link oldDistance in binding.LinksTo
            .Where(link => link.LinkType?.Label == "distance")
            .ToList())
            binding.RemoveLink(oldDistance);

        Thought distanceThought = theUKS.GetOrAddThought(
            $"distance:{distance}", "distance");
        binding.AddLink("distance", distanceThought);
    }

    public static bool IsSafeObservationFileName(string fileName)
    {
        return !string.IsNullOrWhiteSpace(fileName) &&
            !Path.IsPathRooted(fileName) &&
            string.Equals(Path.GetFileName(fileName), fileName,
                StringComparison.Ordinal) &&
            Path.GetExtension(fileName).Equals(".txt",
                StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSafeLessonFileName(string fileName)
    {
        return IsSafeObservationFileName(fileName);
    }

    private static string ResolveObservationPath(string fileName)
    {
        return ResolveContentPath(
            fileName,
            "Observations",
            IsSafeObservationFileName);
    }

    private static string ResolveContentPath(
        string fileName,
        string contentType,
        Func<string, bool> validator)
    {
        if (!validator(fileName)) return null;

        foreach (string directory in GroundedContentLocator
            .CandidateDirectories(contentType))
        {
            string root = Path.GetFullPath(directory);
            string path = Path.GetFullPath(Path.Combine(root, fileName));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private ObservationDescription ReadObservation(string path)
    {
        try
        {
            string subjectLabel = string.Empty;
            string imageFile = string.Empty;
            List<ObservationAttribute> attributes = new();
            string[] lines = File.ReadAllLines(path);

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith(SubjectHeader,
                        StringComparison.OrdinalIgnoreCase))
                    subjectLabel = line[SubjectHeader.Length..].Trim();
            }
            if (string.IsNullOrWhiteSpace(subjectLabel)) return null;

            foreach (string rawLine in lines)
            {
                Match match = Regex.Match(rawLine.Trim(),
                    @"^\[(?<from>.+?)->(?<type>.+?)->(?<to>.+?)\](?:\s+(?<weight>[0-9]+(?:\.[0-9]+)?))?$",
                    RegexOptions.IgnoreCase);
                if (!match.Success) continue;

                string from = match.Groups["from"].Value.Trim();
                string linkType = match.Groups["type"].Value.Trim();
                string target = match.Groups["to"].Value.Trim();
                float weight = 1;
                if (match.Groups["weight"].Success)
                    float.TryParse(match.Groups["weight"].Value,
                        NumberStyles.Float, CultureInfo.InvariantCulture,
                        out weight);

                if (from.Equals(subjectLabel, StringComparison.OrdinalIgnoreCase))
                {
                    if (linkType.Equals(HasImageLabel,
                            StringComparison.OrdinalIgnoreCase))
                        imageFile = target;
                    else if (!linkType.Equals("is-a",
                            StringComparison.OrdinalIgnoreCase))
                        attributes.Add(new ObservationAttribute(
                            linkType, target, weight));
                }
                else if (linkType.Equals("is-a",
                        StringComparison.OrdinalIgnoreCase) &&
                    target.Equals(ImageLabel, StringComparison.OrdinalIgnoreCase))
                {
                    imageFile = from;
                }
            }

            return new ObservationDescription(imageFile, attributes);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private Thought FindMatchingObject(ObservationDescription observation)
    {
        Thought objectRoot = theUKS.Labeled("Object");
        if (objectRoot is null || observation.Attributes.Count == 0)
            return null;

        Thought query = new();
        foreach (ObservationAttribute attribute in observation.Attributes)
        {
            Thought linkType = GetOrCreateLinkType(attribute.LinkTypeLabel);
            Thought target = GetOrCreateAttributeTarget(attribute.TargetLabel);
            Link link = query.AddLink(linkType, target);
            if (link is not null) link.Weight = attribute.Weight;
        }

        List<Thought> exactMatches = new();
        try
        {
            foreach ((Thought candidate, _) in theUKS
                .SearchByAttributes(query, objectRoot))
            {
                if (!IsAnonymousObject(candidate)) continue;
                List<Link> candidateAttributes = theUKS.GetAttributes(candidate);
                bool allMatch = query.LinksTo.All(queryLink =>
                    candidateAttributes.Any(candidateLink =>
                        candidateLink.LinkType == queryLink.LinkType &&
                        candidateLink.To == queryLink.To));
                if (allMatch) exactMatches.Add(candidate);
            }
        }
        finally
        {
            query.Delete();
        }

        return exactMatches.Distinct().Count() == 1
            ? exactMatches[0]
            : null;
    }

    private void ApplyObservation(
        Thought subject,
        ObservationDescription observation)
    {
        if (!string.IsNullOrWhiteSpace(observation.ImageFile))
        {
            Thought image = theUKS.Labeled(observation.ImageFile) ??
                theUKS.GetOrAddThought(observation.ImageFile, ImageLabel);
            Link imageLink = subject.AddLink(
                theUKS.Labeled(HasImageLabel), image);
            if (imageLink is not null) imageLink.Weight = 1;
        }

        List<Link> availableAttributes = theUKS.GetAttributes(subject);
        foreach (ObservationAttribute attribute in observation.Attributes)
        {
            Thought linkType = GetOrCreateLinkType(attribute.LinkTypeLabel);
            Thought target = GetOrCreateAttributeTarget(attribute.TargetLabel);
            if (availableAttributes.Any(link =>
                link.LinkType == linkType && link.To == target))
                continue;

            Link added = subject.AddLink(linkType, target);
            if (added is not null) added.Weight = attribute.Weight;
        }
    }

    private Thought GetOrCreateLinkType(string label)
    {
        return theUKS.Labeled(label) ??
            theUKS.GetOrAddThought(label, "LinkType");
    }

    private Thought GetOrCreateAttributeTarget(string label)
    {
        return theUKS.Labeled(label) ??
            theUKS.GetOrAddThought(label, "Unknown");
    }

    private string GetNextAnonymousObjectLabel()
    {
        int highest = theUKS.AtomicThoughts
            .Where(IsAnonymousObject)
            .Select(thought => int.TryParse(
                    thought.Label[AnonymousObjectPrefix.Length..],
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int value)
                ? value
                : 0)
            .DefaultIfEmpty(0)
            .Max();
        return AnonymousObjectPrefix + (highest + 1).ToString(
            CultureInfo.InvariantCulture);
    }

    private bool IsAnonymousObject(Thought thought)
    {
        return thought is not null && Regex.IsMatch(
            thought.Label,
            "^" + Regex.Escape(AnonymousObjectPrefix) + @"\d+$",
            RegexOptions.IgnoreCase);
    }

    private string GetBestWordFor(Thought subject)
    {
        Thought means = theUKS.Labeled("means");
        if (means is null) return string.Empty;

        List<Link> directWords = subject.LinksFrom
            .Where(link => link.LinkType == means &&
                link.From?.Label.StartsWith("w:",
                    StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        Link best = directWords
            .Where(link => GetBestMeaning(link.From)?.To == subject)
            .OrderByDescending(link => link.Weight)
            .FirstOrDefault() ?? directWords
            .OrderByDescending(link => link.Weight)
            .FirstOrDefault();
        return best?.From?.Label.StartsWith("w:",
            StringComparison.OrdinalIgnoreCase) == true
            ? best.From.Label[2..]
            : string.Empty;
    }

    private Link GetBestMeaning(Thought word)
    {
        Thought means = theUKS.Labeled("means");
        if (word is null || means is null) return null;

        List<Link> candidates = word.LinksTo
            .Where(link => link.LinkType == means && link.To is not null &&
                !BlockedMeaningLabels.Contains(link.To.Label))
            .OrderByDescending(link => link.Weight)
            .ToList();
        if (candidates.Count == 0) return null;

        float highestWeight = candidates[0].Weight;
        float tolerance = Math.Max(0, MeaningResolutionTieTolerance);
        List<Link> tied = candidates
            .Where(link => Math.Abs(link.Weight - highestWeight) <= tolerance)
            .ToList();
        if (tied.Count == 1) return tied[0];

        // When a category and one or more of its instances tie, the category
        // is the meaning shared by all tied candidates. Unrelated ties remain
        // unresolved rather than selecting an arbitrary individual.
        return tied.FirstOrDefault(candidate => tied.All(other =>
            other == candidate ||
            other.To.HasAncestor(candidate.To)));
    }

    private List<Thought> GetMeaningCandidates(ModuleMentalModel mentalModel)
    {
        return mentalModel.GetAttendedContents()
            .SelectMany(thought => thought.AncestorsWithSelf)
            .Where(thought => thought is not null &&
                !BlockedMeaningLabels.Contains(thought.Label))
            .Distinct()
            .ToList();
    }

    private Thought EnsureLanguage(string languageLabel)
    {
        if (theUKS is null || string.IsNullOrWhiteSpace(languageLabel))
            return null;

        Thought languageElement = theUKS.GetOrAddThought(
            "LanguageElement", "Thought");
        Thought languageRoot = theUKS.GetOrAddThought(
            "Language", languageElement);
        return theUKS.GetOrAddThought(languageLabel.Trim(), languageRoot);
    }

    private List<Thought> GetPhraseWords(string phrase)
    {
        if (theUKS is null) return new List<Thought>();

        return Regex.Matches(phrase.ToLowerInvariant(), @"[\p{L}\p{Nd}]+")
            .Select(match => theUKS.Labeled("w:" + match.Value))
            .Where(word => word is not null)
            .ToList();
    }

    private static ModuleMentalModel GetMentalModel()
    {
        return MainWindow.theWindow?.activeModules
            .OfType<ModuleMentalModel>()
            .FirstOrDefault();
    }

    private static IEnumerable<string> CandidateObservationDirectories()
    {
        return GroundedContentLocator.CandidateDirectories("Observations");
    }
}

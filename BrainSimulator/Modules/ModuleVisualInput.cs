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
using System.IO;
using System.Linq;
using UKS;

namespace BrainSimulator.Modules;

/// <summary>
/// Presents external visual-observation files to the Mental Model. Listing a
/// file does not alter the UKS; its subject Thought is created only when the
/// observation is presented.
/// </summary>
public class ModuleVisualInput : ModuleBase
{
    public const string VisualElementLabel = "VisualElement";
    public const string ImageLabel = "Image";
    public const string HasImageLabel = "hasImage";
    public static readonly string RelativeObservationDirectory =
        Path.Combine("UKSContent", "GroundedDogs", "Observations");
    private static readonly TimeSpan PresentationLifetime = TimeSpan.FromSeconds(10);

    private const string SubjectHeader = "# subject:";

    public string CurrentVisualLabel { get; private set; } = string.Empty;
    public string SelectedObservationFile { get; private set; } = string.Empty;
    public string Status { get; private set; } = "Ready";

    public override void Fire()
    {
        Init();
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

    public void SelectObservationFile(string fileName)
    {
        SelectedObservationFile = IsSafeObservationFileName(fileName)
            ? fileName.Trim()
            : string.Empty;
        Status = "Ready";
    }

    public Thought PresentSelected(double distance)
    {
        ModuleMentalModel mentalModel = MainWindow.theWindow?.activeModules
            .OfType<ModuleMentalModel>()
            .FirstOrDefault();
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
        if (theUKS is null || mentalModel is null) return null;
        EnsureVocabulary();

        if (!double.IsFinite(distance) || distance <= 0)
        {
            Status = "Distance must be greater than zero.";
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

        string subjectLabel = ReadSubjectLabel(path);
        if (string.IsNullOrWhiteSpace(subjectLabel))
        {
            Status = $"{SelectedObservationFile} has no '# subject:' header.";
            UpdateDialog();
            return null;
        }

        bool subjectAlreadyExisted = theUKS.Labeled(subjectLabel) is not null;
        theUKS.ImportTextFile(path);
        Thought subject = subjectAlreadyExisted
            ? theUKS.Labeled(subjectLabel)
            : theUKS.GetOrAddThought(subjectLabel, "Object");
        if (subject is null)
        {
            Status = $"{SelectedObservationFile} did not create {subjectLabel}.";
            UpdateDialog();
            return null;
        }

        Thought attentionCell = mentalModel.AttentionCell ??
            mentalModel.SetAttentionCell(mentalModel.Center);
        Link binding = mentalModel.ReplaceCurrentContents(
                new[] { subject },
                PresentationLifetime,
                attentionCell)
            .FirstOrDefault();
        if (binding is null) return null;

        foreach (Link oldDistance in binding.LinksTo
            .Where(link => link.LinkType?.Label == "distance")
            .ToList())
            binding.RemoveLink(oldDistance);

        Thought distanceThought = theUKS.GetOrAddThought(
            $"distance:{distance}", "distance");
        binding.AddLink("distance", distanceThought);

        var position = mentalModel.GetAnglesFromCell(attentionCell);
        CurrentVisualLabel = subject.Label;
        Status = $"{subject.Label} - Horiz: {position.azimuth.Degrees:0.#}\u00B0, " +
            $"Vert: {position.elevation.Degrees:0.#}\u00B0, Distance: {distance:0.##}";
        UpdateDialog();
        return subject;
    }

    public void ClearPresentation()
    {
        ModuleMentalModel mentalModel = MainWindow.theWindow?.activeModules
            .OfType<ModuleMentalModel>()
            .FirstOrDefault();
        ClearPresentation(mentalModel);
    }

    public void ClearPresentation(ModuleMentalModel mentalModel)
    {
        mentalModel?.ReplaceCurrentContents(Array.Empty<Thought>());
        CurrentVisualLabel = string.Empty;
        Status = "Ready";
        UpdateDialog();
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

    private static string ResolveObservationPath(string fileName)
    {
        if (!IsSafeObservationFileName(fileName)) return null;

        foreach (string directory in CandidateObservationDirectories())
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

    private static string ReadSubjectLabel(string path)
    {
        try
        {
            foreach (string line in File.ReadLines(path))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(SubjectHeader,
                        StringComparison.OrdinalIgnoreCase))
                    return trimmed[SubjectHeader.Length..].Trim();
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return string.Empty;
    }

    private static IEnumerable<string> CandidateObservationDirectories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, RelativeObservationDirectory);
        yield return Path.Combine(Directory.GetCurrentDirectory(), RelativeObservationDirectory);
        yield return Path.Combine(
            Directory.GetCurrentDirectory(),
            "BrainSimulator",
            RelativeObservationDirectory);
    }
}

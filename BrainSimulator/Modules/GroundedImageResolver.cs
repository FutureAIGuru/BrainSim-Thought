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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UKS;

namespace BrainSimulator.Modules;

/// <summary>
/// Resolves a portable hasImage target to an image shipped with the grounded-
/// dogs demo. The target Thought's label is the literal filename.
/// </summary>
public static class GroundedImageResolver
{
    public static string GroundingLinkType { get; set; } = "hasImage";
    public static string VisualElementRootLabel { get; set; } = "VisualElement";
    public static string ImageRootLabel { get; set; } = "Image";

    private static readonly HashSet<string> SupportedExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp",
    };

    private static readonly Dictionary<string, ImageSource> ImageCache = new(
        StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public static Thought GetGroundingImageThought(Thought thought)
    {
        if (thought is null || thought is Link) return null;

        return thought.LinksTo
            .FirstOrDefault(link =>
                link.LinkType?.Label.Equals(
                    GroundingLinkType,
                    StringComparison.OrdinalIgnoreCase) == true &&
                link.To?.HasAncestor(ImageRootLabel) == true)
            ?.To;
    }

    public static string ResolveImagePath(Thought thought)
    {
        Thought imageThought = GetGroundingImageThought(thought);
        if (imageThought is null || !IsSafeImageFileName(imageThought.Label))
            return null;

        foreach (string directory in CandidateImageDirectories())
        {
            string root = Path.GetFullPath(directory);
            string path = Path.GetFullPath(Path.Combine(root, imageThought.Label));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                continue;
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    public static ImageSource LoadImage(Thought thought)
    {
        string path = ResolveImagePath(thought);
        if (path is null) return null;

        lock (CacheLock)
        {
            if (ImageCache.TryGetValue(path, out ImageSource cached))
                return cached;

            try
            {
                using FileStream stream = File.OpenRead(path);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 256;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                ImageCache[path] = bitmap;
                return bitmap;
            }
            catch (IOException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }

    public static bool IsSafeImageFileName(string label)
    {
        if (string.IsNullOrWhiteSpace(label) || Path.IsPathRooted(label))
            return false;
        if (!string.Equals(Path.GetFileName(label), label, StringComparison.Ordinal))
            return false;
        return SupportedExtensions.Contains(Path.GetExtension(label));
    }

    private static IEnumerable<string> CandidateImageDirectories()
    {
        return GroundedContentLocator.CandidateDirectories("Images");
    }
}

/// <summary>
/// Shared, extensible locations for grounded observation, image, and lesson
/// content. The original dog directory remains valid while broader demos can
/// be added beneath GroundedExperiences.
/// </summary>
public static class GroundedContentLocator
{
    public static IList<string> RelativeContentRoots { get; } = new List<string>
    {
        Path.Combine("UKSContent", "GroundedDogs"),
        Path.Combine("UKSContent", "GroundedExperiences"),
    };

    public static IEnumerable<string> CandidateDirectories(string contentType)
    {
        foreach (string configuredRoot in RelativeContentRoots
            .Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            string relativeDirectory = Path.Combine(configuredRoot, contentType);
            if (Path.IsPathRooted(relativeDirectory))
            {
                yield return relativeDirectory;
                continue;
            }

            yield return Path.Combine(AppContext.BaseDirectory, relativeDirectory);
            yield return Path.Combine(Directory.GetCurrentDirectory(), relativeDirectory);
            yield return Path.Combine(
                Directory.GetCurrentDirectory(), "BrainSimulator", relativeDirectory);
        }
    }
}

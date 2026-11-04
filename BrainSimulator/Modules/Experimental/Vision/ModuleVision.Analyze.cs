/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License.
 */

using System.Collections.Generic;
using System.Windows.Media;

namespace BrainSimulator.Modules;

public partial class ModuleVision
{
    /// <summary>
    /// Runs the front of the vision pipeline on one image file: load it into the
    /// (at most 50x50) pixel array and find its boundary points. Used by modules
    /// that need the pipeline's output directly (e.g. ModuleObjectRecognizer)
    /// rather than waiting for Fire to notice a new CurrentFilePath.
    /// </summary>
    /// <returns>The pixel array and the boundary points found in it.</returns>
    public (Color[,] pixels, List<PointPlus> boundary) AnalyzeImageFile(string filePath)
    {
        CurrentFilePath = filePath;
        previousFilePath = filePath;   //Fire has nothing left to do for this file
        LoadImageFileToPixelArray(filePath);
        FindBoundaries(imageArray);
        if (boundaryArray is not null)
            SetBoundaryArrayFromImage();
        return (imageArray, new List<PointPlus>(boundaryPoints));
    }
}

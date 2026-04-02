/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License.
 * You may use, copy, modify, merge, publish, distribute,
 * sublicense, and/or sell copies of this software under the terms of
 * the MIT License.
 *
 * See the LICENSE file in the project root for full license information.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UKS;

namespace BrainSimulator.Modules;

public class ModuleMentalModel : ModuleBase
{
    // Root marker (so you can find all mental-model nodes quickly)
    public Thought Root { get; private set; }

    // Spatial sheet (fixed)
    public SpatialSheetConfig cfg;

    // Cells indexed by (ring, rayIndexWithinRing)
    public Thought[][] _cells = Array.Empty<Thought[]>();
    public Thought Center { get; private set; } = null!;

    // Internal link types (Thoughts used as LinkType identifiers, or however you model link types)
    private Thought _ltContains;
    private Thought _ltCenter;

    public override void Fire()
    {
        Init();
        UpdateDialog();
    }

    public override void Initialize()
    {
    }

    // called whenever the UKS performs an Initialize()
    public override void UKSInitializedNotification()
    {
        theUKS = MainWindow.theUKS;
        theUKS.GetOrAddThought("mentalModel", "Abstract");

        Root = theUKS.GetOrAddThought("_mm:root","mentalModel");
        _ltContains = theUKS.GetOrAddThought("_mm:contains", "mentalModel");
        _ltCenter = theUKS.GetOrAddThought("_mm:center", "mentalModel");

        theUKS.GetOrAddThought("activeThought", "mentalModel");
        theUKS.GetOrAddThought("imaginedThought", "mentalModel");
        theUKS.GetOrAddThought("inActiveThought", "mentalModel");


        cfg = new SpatialSheetConfig();
        BuildOrLoad(cfg);
    }


    /// <summary>
    /// Bind an object Thought into one cell.
    /// If it is already bound, bind it to a (potentially) new location
    /// Volatile links should decay quickly elsewhere in your tick/decay mechanism.
    /// </summary>
    public Link BindThoughtToMentalModel(Thought t, Thought mmPosition, float weight = 1f,bool imagined = false)
    {
        //this rebuilds the link instead of strengthening
        var existingLink = t.LinksFrom.FindFirst(x => x.LinkType == _ltContains);
        if (existingLink is not null && mmPosition == existingLink.From)
        {
            existingLink.TimeToLive += TimeSpan.FromSeconds(5);
            return existingLink;
        }
        if (existingLink is not null)
            existingLink.From.RemoveLink(_ltContains, t);
        Link l = mmPosition.AddLink(_ltContains, t);
        l.Weight = weight;
        l.TimeToLive = TimeSpan.FromSeconds(100);
        if (t.Label == "attention")
            l.TimeToLive = TimeSpan.FromSeconds(3);
        if (!imagined)
            t.AddLink("is-a", "activeThought");
        else
            t.AddLink("is-a", "imaginedThought");
        return l;
    }
    public Link ImagineThought(Thought t, Thought mmPosition, float weight = 1f)
    {
        return BindThoughtToMentalModel(t, mmPosition, weight, true);
    }
    public void UnbindThought(Thought t)
    {
        var existingLink = t.LinksFrom.FindFirst(x => x.LinkType == _ltContains);
        if (existingLink is not null)
            existingLink.From.RemoveLink(_ltContains, t);
        t.RemoveLink("is-a", "activeThought");
        t.RemoveLink("is-a", "imaginedThought");
        Link l = t.AddLink("is-a", "inActiveThought");
        l.TimeToLive = TimeSpan.FromSeconds(1);
    }

    internal double ComputeProximity(Thought t)
    {
        if (t is null || _cells.Length == 0 || Center is null) return 0;
        double best = 0;
        var centerAngles = GetAnglesFromCell(Center);

        foreach (var link in t.LinksFrom.Where(x => x.LinkType == _ltContains))
        {
            Thought cell = link.From;
            if (cell is null) continue;

            var pos = GetAnglesFromCell(cell);
            double dAz = AngularDistanceDegrees(pos.azimuth.Degrees, centerAngles.azimuth.Degrees);
            double dEl = Math.Abs(pos.elevation.Degrees - centerAngles.elevation.Degrees);
            double norm = Math.Sqrt((dAz / 180.0) * (dAz / 180.0) + (dEl / 180.0) * (dEl / 180.0));
            double proximity = 1.0 / (1.0 + norm * 4.0);
            if (proximity > best) best = proximity;
        }
        return best;
    }

    private static double AngularDistanceDegrees(double a, double b)
    {
        double diff = Math.Abs(a - b) % 360.0;
        if (diff > 180.0) diff = 360.0 - diff;
        return diff;
    }

    public Thought GetCell(Angle azimuth, Angle elevation)
    {
        float el = (float)Math.Clamp(elevation.Degrees, -90.0, 90.0);
        int ring = GetBinFromElevation(el);
        int idx = GetBinFromAzimuth(azimuth, ring);
        return _cells[ring][idx];
    }

    private int GetBinFromAzimuth(Angle azimuth, int ring)
    {
        double x = azimuth.Degrees % 360.0;
        if (x >= 180.0) x -= 360.0;
        if (x < -180.0) x += 360.0;
        x = x / 180.0;

        float alpha = cfg.FrontBias;
        double u = Math.Tanh(alpha * x) / Math.Tanh(alpha);
        int bins = _cells[ring].Length;
        double t = (u + 1.0) * 0.5;
        int idx = (int)Math.Floor(t * bins);
        if (idx < 0) idx = 0;
        if (idx >= bins) idx = bins - 1;
        return idx;
    }

    public Angle GetAzimuthFromBin(int bin, int ring)
    {
        float halfWidth = _cells[ring].Length / 2;
        float offset = bin - halfWidth;
        float u = offset / halfWidth;
        Angle a = Angle.FromDegrees((float)InvertWarp(u) * 180);
        return a;
    }

    public double InvertWarp(double u)
    {
        float alpha = cfg.FrontBias;
        u = Math.Clamp(u, -1.0, 1.0);
        double v = u * Math.Tanh(alpha);
        double x = Math.Atanh(v) / alpha;
        return Math.Clamp(x, -1.0, 1.0);
    }

    public int GetBinFromElevation(float el)
    {
        float absEl = Math.Abs(el);
        var edges = cfg.ElevationEdges;
        int absRing = edges.Count - 2;

        for (int i = 0; i < edges.Count - 1; i++)
        {
            if (absEl < edges[i + 1])
            {
                absRing = i;
                break;
            }
        }

        int signedRing = el < 0 ? -absRing : absRing;
        return signedRing + cfg.Rings;
    }


    /// <summary>
    /// Inverse of GetBinFromElevation: returns the band center elevation for a bin.
    /// Uses midpoints of the same absolute thresholds, mirrored for lower bins.
    /// </summary>
    public Angle GetElevationFromBin(int bin)
    {
        int clamped = Math.Clamp(bin, 0, _cells.Length - 1);
        int signedRing = clamped - cfg.Rings;
        int absRing = Math.Abs(signedRing);

        var edges = cfg.ElevationEdges;
        absRing = Math.Min(absRing, edges.Count - 2);

        float center = absRing == 0 ? 0f : (edges[absRing] + edges[absRing + 1]) * 0.5f;
        float deg = signedRing < 0 ? -center : center;
        return Angle.FromDegrees(deg);
    }

    (Angle azimuth, Angle elevation) GetAnglesFromCell(Thought t)
    {
        var m = Regex.Match(t.Label, @"^_mm:cell:r(?<r>-?\d+):k(?<k>\d+)$");
        int r = m.Success ? int.Parse(m.Groups["r"].Value) : -1;
        int k = m.Success ? int.Parse(m.Groups["k"].Value) : -1;
        Angle elevation = GetElevationFromBin(r);
        Angle azimuth = GetAzimuthFromBin(k, r);
        return (azimuth, elevation);
    }

    public void BuildOrLoad(SpatialSheetConfig cfg)
    {
        Build(cfg);
        LinkNeighbors(cfg);
    }

    private void Build(SpatialSheetConfig cfg)
    {
        int totalRings = cfg.Rings * 2 + 1;
        int centerIndex = cfg.Rings;

        _cells = new Thought[totalRings][];
        Thought rootCells = theUKS.GetOrAddThought("_mm:cell", "_mm:root");

        for (int r = 0; r < totalRings; r++)
        {
            int signedRing = r;
            if (signedRing > 6)
                signedRing = signedRing - 2 * Math.Abs(6 - signedRing);
            int radialDistance = Math.Abs(signedRing);

            int rays = cfg.RaysPerRing(radialDistance);
            _cells[r] = new Thought[rays];

            for (int k = 0; k < rays; k++)
            {
                string label = $"_mm:cell:r{r}:k{k}";
                Thought cell = theUKS.GetOrAddThought(label, "_mm:cell");

                rootCells.AddLink("_mm:hasCell", cell);
                _cells[r][k] = cell;
            }
        }

        Thought[] centerRing = _cells[centerIndex];
        int centerRay = centerRing.Length > 0 ? centerRing.Length / 2 : 0;
        Center = centerRing[centerRay];

        rootCells.AddLink(_ltCenter, Center);
        Center.AddLink(_ltCenter, rootCells);
    }

    private void LinkNeighbors(SpatialSheetConfig cfg)
    {
        Thought rt = theUKS.GetOrAddThought("rightOf", "comparison");
        Thought above = theUKS.GetOrAddThought("above", "comparison");
        for (int r = 0; r < _cells.Length; r++)
        {
            int rays = _cells[r].Length;
            for (int k = 0; k < rays; k++)
            {
                var a = _cells[r][k];
                var b = _cells[r][(k + 1) % rays];
                b.AddLink(rt, a);
            }
        }

        for (int r = 0; r < cfg.Rings; r++)
        {
            int denseRow1 = cfg.Rings - r;
            int denseRow2 = cfg.Rings + r;

            float numCellsInDenseRow = _cells[denseRow2].Length;
            float numCellsInLessDenseRow = _cells[denseRow2 + 1].Length;

            for (int rowIndex = 0; rowIndex < numCellsInDenseRow; rowIndex++)
            {
                Thought cell1 = _cells[denseRow1][rowIndex];
                float pos2 = (rowIndex * numCellsInLessDenseRow) / numCellsInDenseRow;
                Thought cell2 = _cells[denseRow1 - 1][(int)Math.Round(pos2)];
                cell2.AddLink(above, cell1);

                Thought cell3 = _cells[denseRow2][rowIndex];
                Thought cell4 = _cells[denseRow2 + 1][(int)Math.Round(pos2)];
                cell3.AddLink(above, cell4);
            }
        }
    }

    public void RotateMentalModel(Angle azimuth, Angle elevation)
    {
        Thought allCells = theUKS.Labeled("_mm:cell");
        if (allCells is null) return;

        var moves = new List<(Thought fromCell, Thought obj, TimeSpan ttl, float weight, Thought distThought)>();

        foreach (var cell in allCells.Children)
        {
            foreach (Link l in cell.LinksTo.Where(x => x.LinkType == _ltContains).ToList())
            {
                var dist = l.LinksTo.FirstOrDefault(x => x.LinkType.Label == "distance")?.To;
                if (l.To?.Label != "attention" || cell.LinksTo.Count(x => x.LinkType == _ltContains) > 1)
                    moves.Add((fromCell: cell, obj: l.To, ttl: l.TimeToLive, weight: l.Weight,dist));
            }
        }

        foreach (var move in moves)
        {
            var angles = GetAnglesFromCell(move.fromCell);
            angles.azimuth += azimuth;
            angles.elevation += elevation;

            Thought newPosition = GetCell(angles.azimuth, angles.elevation);
            if (newPosition == move.fromCell) continue;

            move.fromCell.RemoveLink(_ltContains, move.obj);
            Link newLink = newPosition.AddLink(_ltContains, move.obj);
            newLink.TimeToLive = move.ttl;
            newLink.Weight = move.weight;
            newLink.AddLink("distance", move.distThought);
        }
    }
    public void MoveMentalModel(float distanceForward)
    {
        // Treat forward as +Z. When the center moves forward by +distanceForward,
        // objects appear to move by -distanceForward in Z.
        Thought allCells = theUKS.Labeled("_mm:cell");
        if (allCells is null || distanceForward == 0f) return;

        var moves = new List<(Thought fromCell, Thought obj, TimeSpan ttl, float weight, double dist)>();

        foreach (var cell in allCells.Children)
        {
            foreach (Link l in cell.LinksTo.Where(x => x.LinkType == _ltContains).ToList())
            {
                // Skip the attention singleton unless the cell contains more than one object
                if (l.To?.Label == "attention" && cell.LinksTo.Count(x => x.LinkType == _ltContains) == 1)
                    continue;

                double d = GetDistanceFromLink(l);
                moves.Add((cell, l.To, l.TimeToLive, l.Weight, d));
            }
        }

        foreach (var move in moves)
        {
            var angles = GetAnglesFromCell(move.fromCell);
            // Spherical (r = distance, az = XZ-plane, el = Y elevation)
            double r = move.dist > 1 ? move.dist : 1.0; // default to 1 if no stored distance
            double azRad = angles.azimuth.Degrees * Math.PI / 180.0;
            double elRad = angles.elevation.Degrees * Math.PI / 180.0;

            double cosEl = Math.Cos(elRad);
            double x = r * cosEl * Math.Sin(azRad);
            double y = r * Math.Sin(elRad);
            double z = r * cosEl * Math.Cos(azRad);

            // Move center forward => objects move backward along +Z
            z -= distanceForward;

            double newR = Math.Sqrt(x * x + y * y + z * z);
            if (newR < 1e-6) newR = 1e-6; // avoid zero; keep direction

            double newAz = Math.Atan2(x, z) * 180.0 / Math.PI;
            double newEl = Math.Atan2(y, Math.Sqrt(x * x + z * z)) * 180.0 / Math.PI;

            Thought newPosition = GetCell(Angle.FromDegrees((float)newAz), Angle.FromDegrees((float)newEl));
            if (newPosition is null) continue;

            move.fromCell.RemoveLink(_ltContains, move.obj);
            Link newLink = newPosition.AddLink(_ltContains, move.obj);
            newLink.TimeToLive = move.ttl;
            newLink.Weight = move.weight;

            // Update distance link
            Thought distThought = theUKS.GetOrAddThought($"distance:{newR}", "distance");
            // remove old distance links
            foreach (var dl in newLink.LinksTo.Where(x => x.LinkType.Label == "distance").ToList())
                newLink.RemoveLink(dl);
            newLink.AddLink("distance", distThought);
        }
    }

    private double GetDistanceFromLink(Link l)
    {
        // Distance is stored as a link from the binding thought to a distance:* thought
        var dLink = l.LinksTo.FirstOrDefault(x => x.LinkType.Label == "distance");
        if (dLink?.To?.Label?.StartsWith("distance:") == true)
        {
            if (double.TryParse(dLink.To.Label["distance:".Length..], out double val))
                return val;
        }
        return 0;
    }
}

//non-linear spatial sheet settings
public sealed class SpatialSheetConfig
{
    public int Rings { get; init; } = 6;
    public int BaseRays { get; init; } = 16;
    public float Growth { get; init; } = 1.35f;
    public float FrontBias { get; init; } = 1.5f;
    public List<float> ElevationEdges { get; init; } = new() { 0f, 1.5f, 5f, 10f, 17f, 30f, 50f, 90f };

    public int RaysPerRing(int ring)
    {
        double rays = BaseRays * Math.Pow(Growth, ring);
        int r = (int)Math.Round(rays);
        return Math.Clamp(r, 8, 128);
    }
}

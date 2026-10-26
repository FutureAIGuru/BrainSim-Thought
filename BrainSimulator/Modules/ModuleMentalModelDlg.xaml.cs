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
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using UKS;

namespace BrainSimulator.Modules;

public partial class ModuleMentalModelDlg : ModuleBaseDlg
{
    private Point _panStart;
    private bool _isPanning;
    private bool _suppressZoom;
    private Point _lastMousePos;

    public ModuleMentalModelDlg()
    {
        InitializeComponent();
        Loaded += (_, _) => ZoomSlider.Value = 1.0; // ensure initial scale
    }

    public override bool Draw(bool checkDrawTimer)
    {
        if (!base.Draw(checkDrawTimer)) return false;
        //this has a timer so that no matter how often you might call draw, the dialog
        //only updates 10x per second
        ModuleMentalModel parent = (ModuleMentalModel)base.ParentModule;
        DrawCells(parent);
        if (parent.AttentionCell is not null)
        {
            var position = parent.GetAnglesFromCell(parent.AttentionCell);
            SetStatus(
                $"Attn: Horiz {position.azimuth.Degrees:0.#}° Vert {position.elevation.Degrees:0.#}°",
                Colors.Black);
        }
        return true;
    }
    private void DrawCells(ModuleMentalModel parent, bool ignoreMousePause = false)
    {
        if (theCanvas is null) return;
        if (!ignoreMousePause && theCanvas.IsMouseOver &&
            Mouse.RightButton != MouseButtonState.Pressed)
            return;
        theCanvas.Children.Clear();

        var cells = parent._cells;
        if (cells is null || cells.Length == 0) return;

        double canvasWidth = Math.Max(1, theCanvas.ActualWidth);
        double canvasHeight = Math.Max(1, theCanvas.ActualHeight);

        int ringCount = cells.Length;
        // Build elevation band edges from the binning function; fallback to uniform if mismatch
        List<double> edges = BuildElevationEdges(parent, ringCount);
        double yAcc = 0;

        for (int r = ringCount-1; r >= 0; r--)
        {
            if (cells[r] is null || cells[r].Length == 0) continue;

            double ringHeight = ((edges[r + 1] - edges[r]) / 180.0) * canvasHeight;
            int rays = cells[r].Length;
            //double cellWidth = canvasWidth / rays;
            double y = yAcc;
            yAcc += ringHeight;

            // Precompute warped x-edges for this ring
            double[] xEdges = new double[rays + 1];
            for (int i = 0; i <= rays; i++)
            {
                double t = (double)i / rays;          // [0,1]
                double x = (t * 2.0) - 1.0;           // [-1,1]
                double u = parent.InvertWarp (x); // warped [-1,1]
                xEdges[i] = (u + 1.0) * 0.5 * canvasWidth;          // [0,width]
            }

            for (int k = 0; k < rays; k++)
            {
                double xLeft = xEdges[k];

                //double x = k * cellWidth;
                double cellWidth = Math.Max(1e-3, xEdges[k + 1] - xLeft);

                var rect = new Rectangle
                {
                    Width = cellWidth,
                    Height = ringHeight,
                    Fill = Brushes.DarkBlue,
                    Stroke = Brushes.DarkGray,
                    StrokeThickness = 1,
                };
                Thought t = cells[r][k];
                rect.Tag = t.Label;
                rect.MouseLeftButtonDown += Rect_MouseLeftButtonDown;
                ToolTipService.SetHorizontalOffset(rect, 22);
                ToolTipService.SetVerticalOffset(rect, 22);

                if (parent.IsInVisualField(t))
                    rect.Fill = Brushes.SteelBlue;
                if (cells[r][k] == parent.Center)
                    rect.Fill = Brushes.Pink;
                if (t.LastFiredTime > DateTime.Now - TimeSpan.FromSeconds(1))
                    rect.Fill = Brushes.AliceBlue;
                var containsLinks = t.LinksTo
                    .Where(x => x.LinkType.Label == "_mm:contains")
                    .ToList();
                if (containsLinks.Count > 0)
                {
                    rect.Fill = Brushes.Yellow;
                    if (containsLinks.Any(link => link.To?.Label == "attention"))
                        rect.Fill = Brushes.Green;

                    rect.ToolTip = string.Join("\r\n", containsLinks.Select(FormatContainsTooltip));
                }
                Canvas.SetLeft(rect, xLeft);
                Canvas.SetTop(rect, y);
                theCanvas.Children.Add(rect);

                int markerIndex = 0;
                foreach (Link containsLink in t.LinksTo
                    .Where(link => link.LinkType?.Label == "_mm:contains")
                    .OrderByDescending(GetDistanceFromLink))
                {
                    Thought content = containsLink.To;
                    if (content is null) continue;
                    if (content.Label.Equals(
                        "attention", StringComparison.OrdinalIgnoreCase))
                        continue;

                    double markerDistance = GetDistanceFromLink(containsLink);
                    double markerScale = MarkerScaleForDistance(markerDistance);
                    bool imagined = ModuleMentalModel.IsImaginedThought(content);
                    ImageSource imageSource = GroundedImageResolver.LoadImage(content);
                    bool hasImage = imageSource is not null;
                    double conceptScale = Math.Clamp(markerScale, 0.75, 1);
                    double markerWidth = hasImage
                        ? 108 * markerScale
                        : 210 * conceptScale;
                    double markerHeight = hasImage
                        ? 124 * markerScale
                        : 92 * conceptScale;
                    UIElement markerContent = hasImage
                        ? CreateImageMarkerContent(
                            content, imageSource, markerScale, imagined)
                        : CreateConceptMarkerContent(
                            parent.theUKS, content, conceptScale, imagined,
                            parent.ConceptCardMaximumAttributes);
                    var marker = new Border
                    {
                        Width = markerWidth,
                        Height = markerHeight,
                        Padding = hasImage ? new Thickness(2) : new Thickness(7, 5, 7, 5),
                        Background = hasImage ? Brushes.White : Brushes.LightGoldenrodYellow,
                        BorderBrush = imagined ? Brushes.SlateGray : Brushes.DimGray,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        IsHitTestVisible = false,
                        Opacity = GetMarkerOpacity(
                            containsLink, imagined, parent.ImaginedOpacity),
                        Child = markerContent,
                    };

                    double markerLeft = xLeft + (cellWidth - markerWidth) / 2 + markerIndex * 14;
                    double markerTop = y + (ringHeight - markerHeight) / 2 + markerIndex * 8;
                    Canvas.SetLeft(marker, Math.Clamp(markerLeft, 0, Math.Max(0, canvasWidth - markerWidth)));
                    Canvas.SetTop(marker, Math.Clamp(markerTop, 0, Math.Max(0, canvasHeight - markerHeight)));
                    Panel.SetZIndex(marker,
                        MarkerZIndexForDistance(markerDistance, markerIndex));
                    theCanvas.Children.Add(marker);
                    markerIndex++;
                }
            }
        }
    }

    private static UIElement CreateImageMarkerContent(
        Thought content,
        ImageSource imageSource,
        double scale,
        bool imagined)
    {
        var panel = new StackPanel();
        panel.Children.Add(new Image
        {
            Source = imageSource,
            Width = 100 * scale,
            Height = 100 * scale,
            Stretch = Stretch.UniformToFill,
        });
        panel.Children.Add(new TextBlock
        {
            Text = content.Label + (imagined ? " (imagined)" : string.Empty),
            Foreground = Brushes.Black,
            FontSize = Math.Clamp(14 * scale, 7, 14),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return panel;
    }

    private static UIElement CreateConceptMarkerContent(
        UKS.UKS uks,
        Thought content,
        double scale,
        bool imagined,
        int maximumAttributes)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = content.Label + (imagined ? " (imagined)" : string.Empty),
            Foreground = Brushes.Black,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13 * scale,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        panel.Children.Add(new TextBlock
        {
            Text = FormatKnownAttributes(uks, content, maximumAttributes),
            Foreground = Brushes.Black,
            FontSize = 11 * scale,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return panel;
    }

    internal static string FormatKnownAttributes(
        UKS.UKS uks,
        Thought thought,
        int maximumAttributes = 8)
    {
        if (uks is null || thought is null)
            return "No known attributes";

        List<string> attributes = uks.GetAttributes(thought)
            .Where(link => link.LinkType is not null && link.To is not null)
            .Where(link => link.LinkType.HasProperty("isGrounding") != true)
            .Where(link => !link.LinkType.Label.Equals(
                "hasProperty", StringComparison.OrdinalIgnoreCase))
            .Where(link => !link.LinkType.Label.StartsWith(
                "_mm:", StringComparison.OrdinalIgnoreCase))
            .Select(link => $"[{link.LinkType.Label}->{link.To.Label}]")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(text => text, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (attributes.Count == 0)
            return "No known attributes";

        int take = Math.Max(1, maximumAttributes);
        string summary = string.Join(" ", attributes.Take(take));
        return attributes.Count > take ? summary + " ..." : summary;
    }

    private static double GetMarkerOpacity(
        Link containsLink,
        bool imagined,
        double imaginedOpacity)
    {
        double opacity = imagined ? Math.Clamp(imaginedOpacity, 0, 1) : 1;
        if (containsLink.TimeToLive >= TimeSpan.MaxValue)
            return opacity;

        TimeSpan remaining = containsLink.LastFiredTime +
            containsLink.TimeToLive - DateTime.Now;
        if (remaining < TimeSpan.FromSeconds(5))
            opacity *= Math.Clamp(remaining.TotalSeconds / 5.0, 0, 1);
        return opacity;
    }

    private static List<double> BuildElevationEdges(ModuleMentalModel parent, int ringCount)
    {
        List<double> edges = new() { -90 };
        int lastBin = parent.GetBinFromElevation(-90);

        for (double deg = -89.5; deg <= 90.0; deg += 0.5)
        {
            int bin = parent.GetBinFromElevation((float)deg);
            if (bin != lastBin)
            {
                edges.Add(deg);
                lastBin = bin;
            }
        }
        edges.Add(90);

        // Fallback to uniform spacing if the binning did not produce the expected count
        if (edges.Count != ringCount + 1)
        {
            edges = Enumerable.Range(0, ringCount + 1)
                              .Select(i => -90 + i * (180.0 / ringCount))
                              .ToList();
        }
        return edges;
    }

    private void Rect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ParentModule is not ModuleMentalModel module) return;
        if (sender is not Rectangle r) return;
        Thought t = module.theUKS.Labeled(r.Tag.ToString());
        if (t != null)
        {
            ModuleAttention attention = MainWindow.theWindow?.activeModules
                .OfType<ModuleAttention>()
                .FirstOrDefault();
            if (attention is not null)
                attention.SetCenterOfAttention(t);
            else
                module.SetAttentionCell(t);

            var position = module.GetAnglesFromCell(t);
            SetStatus(
                $"Horiz: {position.azimuth.Degrees:0.#}°   Vert: {position.elevation.Degrees:0.#}°",
                Colors.Black);
            DrawCells(module, ignoreMousePause: true);
            e.Handled = true;
            t.Fire();
            t.LinksTo.FindFirst(x => x.LinkType.Label == "above")?.To.Fire();
            t.LinksTo.FindFirst(x => x.LinkType.Label == "rightOf")?.To.Fire();
        }
    }

    private void TheGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Draw(false);
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressZoom) return;

        Point focus = Mouse.GetPosition(theCanvas);
        if (double.IsNaN(focus.X) || double.IsNaN(focus.Y) || focus == default)
            focus = new Point(theCanvas.ActualWidth * 0.5, theCanvas.ActualHeight * 0.5);

        ApplyZoom(ZoomSlider.Value, focus);
    }

    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        _suppressZoom = true;
        ZoomSlider.Value = 1.0;
        _suppressZoom = false;

        CanvasTranslate.X = 0;
        CanvasTranslate.Y = 0;
        CanvasScale.ScaleX = 1;
        CanvasScale.ScaleY = 1;
    }

    private void RotateLeft_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is not ModuleMentalModel module) return;
        module.RotateMentalModel(Angle.FromDegrees(20), Angle.FromDegrees(0));
        Draw(false);
    }

    private void RotateRight_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is not ModuleMentalModel module) return;
        module.RotateMentalModel(Angle.FromDegrees(-20), Angle.FromDegrees(0));
        Draw(false);
    }

    private void RotateUp_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is not ModuleMentalModel module) return;
        module.MoveMentalModel(5f);
        Draw(false);
    }

    private void RotateDown_Click(object sender, RoutedEventArgs e)
    {
        if (ParentModule is not ModuleMentalModel module) return;
        module.MoveMentalModel(-5f);
        Draw(false);
    }

    private void TheCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double delta = e.Delta > 0 ? 0.1 : -0.1;
        double target = Math.Clamp(ZoomSlider.Value + delta, ZoomSlider.Minimum, ZoomSlider.Maximum);
        Point focus = e.GetPosition(theCanvas);
        ApplyZoom(target, focus);

        _suppressZoom = true;
        ZoomSlider.Value = target;
        _suppressZoom = false;
    }

    private void TheCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        _lastMousePos = e.GetPosition(theCanvas);
        if (!_isPanning) return;

        // use parent (untransformed) space to avoid twitch when the canvas moves
        var parent = theCanvas.Parent as IInputElement;
        Point current = e.GetPosition(parent);
        Vector delta = current - _panStart;
        _panStart = current;

        CanvasTranslate.X += delta.X;
        CanvasTranslate.Y += delta.Y;
    }

    private void TheCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        var parent = theCanvas.Parent as IInputElement;
        _panStart = e.GetPosition(parent);
        theCanvas.CaptureMouse();
    }

    private void TheCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        theCanvas.ReleaseMouseCapture();
    }

    private void TheCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        _isPanning = false;
        theCanvas.ReleaseMouseCapture();
    }

    private void ApplyZoom(double newScale, Point focus)
    {
        double oldScale = CanvasScale.ScaleX;
        newScale = Math.Clamp(newScale, ZoomSlider.Minimum, ZoomSlider.Maximum);
        if (Math.Abs(newScale - oldScale) < 1e-6) return;

        double ratio = newScale / oldScale;

        // Adjust translate so the focus point stays under the cursor
        CanvasTranslate.X = focus.X * (1 - ratio) + CanvasTranslate.X * ratio;
        CanvasTranslate.Y = focus.Y * (1 - ratio) + CanvasTranslate.Y * ratio;

        CanvasScale.ScaleX = newScale;
        CanvasScale.ScaleY = newScale;
    }

    private string FormatContainsTooltip(Link l)
    {
        string label = l.To?.Label ?? "(null)";
        double d = GetDistanceFromLink(l);
        if (d > 0)
            return $"{label} (d={d:0.})";
        return label;
    }

    private double GetDistanceFromLink(Link l)
    {
        var dLink = l.LinksTo.FirstOrDefault(x => x.LinkType?.Label == "distance");
        if (dLink?.To?.Label?.StartsWith("distance:") == true &&
            double.TryParse(dLink.To.Label["distance:".Length..], out double val))
            return val;
        return 0;
    }

    internal static double MarkerScaleForDistance(double distance)
    {
        if (distance <= 0) return 1;
        return Math.Clamp(1.0 / Math.Max(1, distance), 0.25, 1.0);
    }

    internal static int MarkerZIndexForDistance(
        double distance,
        int sameCellIndex = 0)
    {
        double effectiveDistance = distance <= 0 ? 1 : distance;
        effectiveDistance = Math.Clamp(effectiveDistance, 1, 10000);
        return 10 + (int)Math.Round(100000 / effectiveDistance) + sameCellIndex;
    }
}

// The parts of BrainSimulator/Utils.cs the module sources use: geometry for PointPlus and the
// module type list. The rest (colors, WPF control lookup, P/Invoke timing) is desktop-only.
using System.Windows;
using BrainSimulator.Modules;
using static System.Math;

namespace BrainSimulator;

public static class Utils
{
    /// <summary>
    /// The module types that can be added, like the desktop's (which finds them by reflection;
    /// NativeAOT can't, so they come from <see cref="ModuleRegistry"/>). Manual agents are left out.
    /// </summary>
    public static List<Type> GetListOfExistingCSharpModuleTypes() =>
        ModuleRegistry.Types.Where(t => !typeof(IManualAgent).IsAssignableFrom(t)).OrderBy(t => t.Name).ToList();

    public static int ColorToInt(System.Windows.Media.Color c) => c.A << 24 | c.R << 16 | c.G << 8 | c.B;
    public static System.Windows.Media.Color IntToColor(int i) =>
        System.Windows.Media.Color.FromArgb((byte)(i >> 24), (byte)(i >> 16), (byte)(i >> 8), (byte)i);

    public static float Rad(float degrees) => (float)(degrees * PI / 180);

    public static double FindDistanceToSegment(Segment s) =>
        s is null ? 0 : FindDistanceToSegment(new Point(0, 0), s.P1.P, s.P2.P, out _);

    public static double FindDistanceToSegment(Segment s, out Point closest) =>
        FindDistanceToSegment(new Point(0, 0), s.P1.P, s.P2.P, out closest);

    public static float FindDistanceToSegment(Point pt, Segment s) =>
        (float)FindDistanceToSegment(pt, s.P1, s.P2, out _);

    public static double FindDistanceToSegment(Point pt, Point p1, Point p2, out Point closest)
    {
        double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
        if (dx == 0 && dy == 0)
        {
            closest = p1;
            dx = pt.X - p1.X; dy = pt.Y - p1.Y;
            return Sqrt(dx * dx + dy * dy);
        }
        double t = ((pt.X - p1.X) * dx + (pt.Y - p1.Y) * dy) / (dx * dx + dy * dy);
        if (t < 0) closest = new Point(p1.X, p1.Y);
        else if (t > 1) closest = new Point(p2.X, p2.Y);
        else closest = new Point(p1.X + t * dx, p1.Y + t * dy);
        dx = pt.X - closest.X; dy = pt.Y - closest.Y;
        return Sqrt(dx * dx + dy * dy);
    }
}

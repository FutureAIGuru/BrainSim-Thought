// Minimal stand-ins for the handful of WPF / NAudio types the desktop module sources
// reference, so those sources compile unchanged for iOS. Only what the modules use.
using System.Runtime.InteropServices;

namespace System.Windows
{
    public struct Point
    {
        public double X { get; set; }
        public double Y { get; set; }
        public Point(double x, double y) { X = x; Y = y; }
        public static Vector operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
        public static Point operator +(Point a, Vector b) => new(a.X + b.X, a.Y + b.Y);
        public static Point operator -(Point a, Vector b) => new(a.X - b.X, a.Y - b.Y);
        public static explicit operator Vector(Point p) => new(p.X, p.Y);
        public static bool operator ==(Point a, Point b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Point a, Point b) => !(a == b);
        public override bool Equals(object o) => o is Point p && p == this;
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"{X},{Y}";
    }

    public struct Vector
    {
        public double X { get; set; }
        public double Y { get; set; }
        public Vector(double x, double y) { X = x; Y = y; }
        public double Length => Math.Sqrt(X * X + Y * Y);
        public double LengthSquared => X * X + Y * Y;
        public void Normalize() { double l = Length; if (l != 0) { X /= l; Y /= l; } }
        public static double Multiply(Vector a, Vector b) => a.X * b.X + a.Y * b.Y;
        public static double AngleBetween(Vector a, Vector b) =>
            (Math.Atan2(b.Y, b.X) - Math.Atan2(a.Y, a.X)) * 180 / Math.PI;
        public static double CrossProduct(Vector a, Vector b) => a.X * b.Y - a.Y * b.X;
        public static Vector operator +(Vector a, Vector b) => new(a.X + b.X, a.Y + b.Y);
        public static Vector operator -(Vector a, Vector b) => new(a.X - b.X, a.Y - b.Y);
        public static Vector operator -(Vector a) => new(-a.X, -a.Y);
        public static Vector operator *(Vector a, double d) => new(a.X * d, a.Y * d);
        public static Vector operator *(double d, Vector a) => new(a.X * d, a.Y * d);
        public static double operator *(Vector a, Vector b) => Multiply(a, b);
        public static Vector operator /(Vector a, double d) => new(a.X / d, a.Y / d);
        public static explicit operator Point(Vector v) => new(v.X, v.Y);
    }
}

namespace System.Windows.Media.Media3D
{
    public struct Vector3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public Vector3D(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public void Normalize() { double l = Length; if (l != 0) { X /= l; Y /= l; Z /= l; } }
        public static double DotProduct(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vector3D CrossProduct(Vector3D a, Vector3D b) =>
            new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public static double AngleBetween(Vector3D a, Vector3D b) =>
            Math.Acos(Math.Clamp(DotProduct(a, b) / (a.Length * b.Length), -1, 1)) * 180 / Math.PI;
        public static Vector3D operator +(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3D operator *(Vector3D a, double d) => new(a.X * d, a.Y * d, a.Z * d);
        public static Vector3D operator /(Vector3D a, double d) => new(a.X / d, a.Y / d, a.Z / d);
        public static explicit operator Point3D(Vector3D v) => new(v.X, v.Y, v.Z);
    }

    public struct Point3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vector3D operator -(Point3D a, Point3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Point3D operator +(Point3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static explicit operator Vector3D(Point3D p) => new(p.X, p.Y, p.Z);
    }
}

namespace System.Windows.Media
{
    public struct Color
    {
        public byte A { get; set; }
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public static Color FromArgb(byte a, byte r, byte g, byte b) => new() { A = a, R = r, G = g, B = b };
        public static Color FromRgb(byte r, byte g, byte b) => FromArgb(255, r, g, b);
        public static bool operator ==(Color a, Color b) => a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B;
        public static bool operator !=(Color a, Color b) => !(a == b);
        public override bool Equals(object o) => o is Color c && c == this;
        public override int GetHashCode() => HashCode.Combine(A, R, G, B);
        public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
    }

    public static class Colors
    {
        public static Color Black => Color.FromRgb(0, 0, 0);
        public static Color White => Color.FromRgb(255, 255, 255);
        public static Color Gray => Color.FromRgb(128, 128, 128);
        public static Color Red => Color.FromRgb(255, 0, 0);
        public static Color Green => Color.FromRgb(0, 128, 0);
        public static Color Blue => Color.FromRgb(0, 0, 255);
        public static Color Yellow => Color.FromRgb(255, 255, 0);
        public static Color Orange => Color.FromRgb(255, 165, 0);
        public static Color Pink => Color.FromRgb(255, 192, 203);
        public static Color Transparent => Color.FromArgb(0, 255, 255, 255);
    }

    /// <summary>On iOS images are drawn by SwiftUI from the resolved file path.</summary>
    public abstract class ImageSource
    {
        public void Freeze() { }
    }
}

namespace System.Windows.Media.Imaging
{
    public enum BitmapCacheOption { Default, OnDemand, OnLoad, None }

    public sealed class BitmapImage : System.Windows.Media.ImageSource
    {
        public BitmapCacheOption CacheOption { get; set; }
        public int DecodePixelWidth { get; set; }
        public IO.Stream StreamSource { get; set; }
        public void BeginInit() { }
        public void EndInit() { }
    }
}

// Namespaces some module files import without using anything from them.
namespace System.Windows.Xps { internal static class _Unused { } }
namespace System.Windows.Documents { internal static class _Unused { } }
namespace System.Windows.Forms { internal static class _Unused { } }
namespace System.Windows.Forms.Design { internal static class _Unused { } }
namespace Microsoft.Windows.Themes { internal static class _Unused { } }

namespace NAudio.Midi
{
    /// <summary>
    /// NAudio's MIDI output, redirected to the iOS app: raw short messages are handed to a
    /// callback Swift registers (uks_set_midi_callback), which plays them on an AVAudioUnitSampler.
    /// </summary>
    public sealed class MidiOut : IDisposable
    {
        internal static unsafe delegate* unmanaged<int, void> Sink;
        public MidiOut(int deviceNo) { }
        public unsafe void Send(int message)
        {
            var sink = Sink;
            if (sink != null) sink(message);
        }
        public void Dispose() { }
        public void Close() { }
        public static int NumberOfDevices => 1;
    }

    public sealed class MidiMessage
    {
        public int RawData { get; }
        MidiMessage(int status, int data1, int data2) => RawData = status | (data1 << 8) | (data2 << 16);
        // NAudio channels are 1-based
        public static MidiMessage StartNote(int note, int volume, int channel) => new(0x90 | (channel - 1), note, volume);
        public static MidiMessage StopNote(int note, int volume, int channel) => new(0x80 | (channel - 1), note, volume);
        public static MidiMessage ChangePatch(int patch, int channel) => new(0xC0 | (channel - 1), patch, 0);
        public static MidiMessage ChangeControl(int controller, int value, int channel) => new(0xB0 | (channel - 1), controller, value);
    }
}

namespace System.Drawing.Drawing2D
{
    public enum InterpolationMode { Default, Low, High, Bilinear, Bicubic, NearestNeighbor, HighQualityBilinear, HighQualityBicubic }
}

namespace System.Drawing
{
    /// <summary>
    /// System.Drawing.Common is Windows-only. This keeps ModuleVision's image loading intact:
    /// files are decoded by the iOS app (ImageIO) through uks_set_image_decoder into RGBA pixels.
    /// </summary>
    public class Bitmap : IDisposable
    {
        internal static unsafe delegate* unmanaged<IntPtr, byte*, int, int*, int*, int> Decoder;

        readonly byte[] rgba;
        public int Width { get; }
        public int Height { get; }

        public Bitmap(int width, int height)
        {
            Width = width; Height = height;
            rgba = new byte[Math.Max(0, width * height * 4)];
        }

        public unsafe Bitmap(string path)
        {
            var decoder = Decoder;
            if (decoder == null) throw new PlatformNotSupportedException("No image decoder registered.");
            IntPtr p = Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8(path);
            try
            {
                int w, h;
                if (decoder(p, null, 0, &w, &h) == 0) throw new IO.FileNotFoundException("Could not read image", path);
                Width = w; Height = h;
                rgba = new byte[w * h * 4];
                fixed (byte* buffer = rgba)
                    if (decoder(p, buffer, rgba.Length, &w, &h) == 0) throw new IO.IOException("Could not decode image " + path);
            }
            finally { Runtime.InteropServices.Marshal.FreeCoTaskMem(p); }
        }

        public Color GetPixel(int x, int y)
        {
            int i = (y * Width + x) * 4;
            return Color.FromArgb(rgba[i + 3], rgba[i], rgba[i + 1], rgba[i + 2]);
        }

        public void SetPixel(int x, int y, Color c)
        {
            int i = (y * Width + x) * 4;
            rgba[i] = c.R; rgba[i + 1] = c.G; rgba[i + 2] = c.B; rgba[i + 3] = c.A;
        }

        public void Dispose() { }
    }

    public sealed class Graphics : IDisposable
    {
        readonly Bitmap target;
        Graphics(Bitmap target) => this.target = target;
        public static Graphics FromImage(Bitmap image) => new(image);
        public Drawing2D.InterpolationMode InterpolationMode { get; set; }

        /// <summary>Draws `image` scaled into the rectangle (x, y, width, height), bilinearly sampled.</summary>
        public void DrawImage(Bitmap image, float x, float y, float width, float height)
        {
            if (width <= 0 || height <= 0) return;
            for (int ty = 0; ty < target.Height; ty++)
                for (int tx = 0; tx < target.Width; tx++)
                {
                    float sx = (tx + 0.5f - x) / width * image.Width - 0.5f;
                    float sy = (ty + 0.5f - y) / height * image.Height - 0.5f;
                    if (sx < -0.5f || sy < -0.5f || sx > image.Width - 0.5f || sy > image.Height - 0.5f) continue;
                    target.SetPixel(tx, ty, Sample(image, sx, sy));
                }
        }

        static Color Sample(Bitmap img, float sx, float sy)
        {
            int x0 = Math.Clamp((int)MathF.Floor(sx), 0, img.Width - 1), y0 = Math.Clamp((int)MathF.Floor(sy), 0, img.Height - 1);
            int x1 = Math.Min(x0 + 1, img.Width - 1), y1 = Math.Min(y0 + 1, img.Height - 1);
            float fx = Math.Clamp(sx - x0, 0, 1), fy = Math.Clamp(sy - y0, 0, 1);
            Color a = img.GetPixel(x0, y0), b = img.GetPixel(x1, y0), c = img.GetPixel(x0, y1), d = img.GetPixel(x1, y1);
            int Mix(byte p, byte q, byte r, byte s) =>
                (int)Math.Round((p * (1 - fx) + q * fx) * (1 - fy) + (r * (1 - fx) + s * fx) * fy);
            return Color.FromArgb(Mix(a.A, b.A, c.A, d.A), Mix(a.R, b.R, c.R, d.R), Mix(a.G, b.G, c.G, d.G), Mix(a.B, b.B, c.B, d.B));
        }

        public void Dispose() { }
    }
}

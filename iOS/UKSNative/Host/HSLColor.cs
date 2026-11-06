// HSLColor from BrainSimulator/Utils.cs, unchanged (Utils.cs itself is desktop-only).
using System.Diagnostics;
using System.Windows.Media;
using static System.Math;

namespace BrainSimulator
{
    public class HSLColor
    {
        public float hue; //[0359]
        public float saturation; //[0,1]
        public float luminance;//[0,1]
        public HSLColor() { }
        public HSLColor(float h, float s, float l)
        {
            hue = h;
            saturation = s;
            luminance = l;
        }
        public HSLColor(Dictionary<string, float> values)
        {
            hue = 0;
            saturation = 0;
            luminance = 0;
            try
            {
                hue = values["Hue+"];
                saturation = values["Sat+"];
                luminance = values["Lum+"];
            }
            catch (Exception ex) { Debug.WriteLine($"HSLColor dictionary parse failed: {ex.Message}"); }
        }
        public HSLColor(byte a, byte r, byte g, byte b)
        {
            System.Drawing.Color c1 = System.Drawing.Color.FromArgb(255, r, g, b);
            hue = c1.GetHue();
            saturation = c1.GetSaturation();
            luminance = c1.GetBrightness();
        }
        public HSLColor(Color c)
        {
            System.Drawing.Color c1 = System.Drawing.Color.FromArgb(255, c.R, c.G, c.B);
            hue = c1.GetHue();
            saturation = c1.GetSaturation();
            luminance = c1.GetBrightness();
        }

        public HSLColor(System.Drawing.Color c)
        {
            hue = c.GetHue();
            saturation = c.GetSaturation();
            luminance = c.GetBrightness();
        }
        public HSLColor(HSLColor c)
        {
            if (c is null) return;
            hue = c.hue;
            saturation = c.saturation;
            luminance = c.luminance;
        }

        public override string ToString()
        { return "H:" + hue.ToString("f2") + " S:" + saturation.ToString("f2") + " L:" + luminance.ToString("f2"); }

        public static float operator -(HSLColor c1, HSLColor c2)
        {
            //any lum > .95 is white  \
            //any lum < .15 is black    -set hue to .5
            //any sat  < .1 is gray   /
            if (c1.luminance > 0.95) c1.hue = 0.5f;
            else if (c1.luminance < .1) c1.hue = 0.5f;
            else if (c1.saturation < .1) c1.hue = 0.5f;
            if (c2.luminance > 0.95) c2.hue = 0.5f;
            else if (c2.luminance < .1) c2.hue = 0.5f;
            else if (c2.saturation < .1) c2.hue = 0.5f;
            float diff = Abs(c1.hue - c2.hue) * 5 + Abs(c1.saturation - c2.saturation) + Abs(c1.luminance - c2.luminance);
            diff /= 7;
            return diff;
        }
        public Color ToColor()
        {
            Color c1 = ColorFromHSL2();
            return c1;
        }
        // the Color Converter
        Color ColorFromHSL()
        {
            if (saturation == 0)
            {
                byte L = (byte)(luminance * 255);
                return Color.FromArgb(255, L, L, L);
            }

            double min, max;

            max = luminance < 0.5d ? luminance * (1 + saturation) : (luminance + saturation) - (luminance * saturation);
            min = (luminance * 2d) - max;

            Color c = Color.FromArgb(255, (byte)(255 * RGBChannelFromHue(min, max, ((int)hue) + 1 / 3d)),
                                          (byte)(255 * RGBChannelFromHue(min, max, ((int)hue))),
                                          (byte)(255 * RGBChannelFromHue(min, max, ((int)hue) - 1 / 3d)));
            //Debug.WriteLine(this + "\t" + c);
            return c;
        }

        Color ColorFromHSL2()
        {
            double C = (1 - Abs(2 * luminance - 1)) * saturation;
            double X = C * (1 - Abs((hue / 60) % 2 - 1));
            double m = luminance - C / 2;

            Color c;

            if (hue < 60) c = Color.FromArgb(255, (byte)((C + m) * 255), (byte)((X + m) * 255), (byte)((0 + m) * 255));
            else if (hue < 120) c = Color.FromArgb(255, (byte)((X + m) * 255), (byte)((C + m) * 255), (byte)((0 + m) * 255));
            else if (hue < 180) c = Color.FromArgb(255, (byte)((0 + m) * 255), (byte)((C + m) * 255), (byte)((X + m) * 255));
            else if (hue < 240) c = Color.FromArgb(255, (byte)((0 + m) * 255), (byte)((X + m) * 255), (byte)((C + m) * 255));
            else if (hue < 300) c = Color.FromArgb(255, (byte)((X + m) * 255), (byte)((0 + m) * 255), (byte)((C + m) * 255));
            else c = Color.FromArgb(255, (byte)((C + m) * 255), (byte)((0 + m) * 255), (byte)((X + m) * 255));

            return c;
        }

        static double RGBChannelFromHue(double m1, double m2, double h)
        {
            h = (h + 1d) % 1d;
            if (h < 0) h += 1;
            if (h * 6 < 1) return m1 + (m2 - m1) * 6 * h;
            else if (h * 2 < 1) return m2;
            else if (h * 3 < 2) return m1 + (m2 - m1) * 6 * (2d / 3d - h);
            else return m1;

        }

        public bool Equals(HSLColor c1)
        {
            if (c1 is null) return false;
            if (luminance < .05)
            {
                if (c1.luminance < .05) return true;
            }
            if (luminance > .95)
            {
                if (c1.luminance > .95) return true;
            }
            float absHueDiff = Abs(hue - c1.hue);
            float circularHueDiff = Min(absHueDiff, 360f - absHueDiff);
            return circularHueDiff < 5;
        }
    }
}

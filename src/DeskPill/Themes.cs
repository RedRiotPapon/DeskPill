using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DeskPill
{
    // Curated dark skins and an editable personal palette. Semantic aliases keep WPF brush identity so
    // switching palettes repaints live controls smoothly without rebuilding windows.
    public static class Themes
    {
        public static readonly string[] Names = Settings.ThemeNames;
        public static readonly string[] Descriptions = {
            "Classic OLED notch black, crisp mint green & subtle glass rim.",
            "Blue-hour ink, airy periwinkle and a soft rose accent.",
            "Smoked plum, porcelain lettering and cherry-blossom rose.",
            "Carbon navy, electric cyan and precise magenta accents.",
            "Woodland charcoal, warm ivory and muted botanical greens.",
            "Deep violet, acid lime and small amber highlights.",
            "Ember charcoal, brushed copper and warm apricot lettering.",
            "Midnight indigo, pale moon gold and silver lavender.",
            "Your own palette, surface finish and outline."
        };

        // Semantic keys referenced across DeskPill UI components:
        // [0]Shell [1]Text [2]Muted [3]PrimaryAccent [4]Card [5]Line [6]DockIdle [7]DockActive [8]Rim [9]Crimson [10]Violet [11]Sky [12]Gold [13]Mint
        static readonly string[] Keys = { "#000000", "#F4F4F4", "#929292", "#48E572", "#151515", "#222222", "#1A1A1A", "#1B3B26", "#30FFFFFF", "#FF375F", "#9160FF", "#279DFF", "#E7BE54", "#56DE9A" };
        static readonly string[][] Colors = {
            // 0: Classic (Default)
            new[] { "#000000", "#F4F4F4", "#929292", "#48E572", "#151515", "#222222", "#1A1A1A", "#1B3B26", "#30FFFFFF", "#FF375F", "#9160FF", "#279DFF", "#E7BE54", "#56DE9A" },
            // 1: Shinkai Twilight
            new[] { "#090F1C", "#EDF2FC", "#A9B5CF", "#F19EBB", "#152033", "#2C3A50", "#19253B", "#3B2940", "#344F729D", "#F19EBB", "#B6B8F1", "#8DAFE8", "#EAC993", "#AACFC5" },
            // 2: Sakura Shrine
            new[] { "#160D14", "#FFF1F3", "#CBB1BF", "#EDA5BE", "#281922", "#49333F", "#30202A", "#523141", "#38C796AB", "#EDA5BE", "#C5AAD9", "#D8A4BF", "#EBC59B", "#BBCBB3" },
            // 3: Cyberpunk Akihabara
            new[] { "#070C15", "#E9F7FA", "#A1B7C2", "#55D9E8", "#101B28", "#293A4B", "#162333", "#163D49", "#3055D9E8", "#F184BA", "#A7A7E3", "#55D9E8", "#DCEB9D", "#8ED6CB" },
            // 4: Ghibli Meadow
            new[] { "#10170F", "#F2EBD9", "#B4BEA6", "#B2CA8B", "#202A1C", "#3B4932", "#283322", "#3E5132", "#359DAE80", "#D3AB91", "#B9B59B", "#A8C4AC", "#D8BF83", "#B2CA8B" },
            // 5: EVA Unit-01
            new[] { "#0F0B19", "#F3EEFD", "#B6A9CC", "#B0E866", "#21172E", "#413051", "#281C36", "#3A4628", "#3A9F83C5", "#E7AF79", "#BC9DE4", "#B2A4D9", "#E2C286", "#B0E866" },
            // 6: Nichirin Flame
            new[] { "#180F0D", "#FFF1E5", "#D0B4A5", "#EFB083", "#2B1D18", "#4B352B", "#34221B", "#563B29", "#38C69A79", "#EBAC88", "#D2B4A5", "#B7B4AC", "#E5C294", "#C3C3A1" },
            // 7: Moonlit Sailor
            new[] { "#100E20", "#F5F0FF", "#B9B1CE", "#E8D29A", "#211D36", "#3D3655", "#29233F", "#4A4030", "#3CABA0CF", "#DAB5D0", "#C2B6EA", "#B1B7E3", "#E8D29A", "#B9D3CD" },
            new[] { "#0C111B", "#F3F6FC", "#A7B3C5", "#8EC9FF", "#1A2332", "#374256", "#1A2332", "#2A3A4B", "#2E8EC9FF", "#8EC9FF", "#C2B4F3", "#C2B4F3", "#8EC9FF", "#8EC9FF" }
        };

        // Top atmospheric sheen for the notch shell (top color -> bottom color)
        static readonly string[] TopTintHex = { "#000000", "#111B2C", "#21141D", "#101A29", "#1A2417", "#1C1429", "#271912", "#1E1931", "#121927" };

        sealed class Tone : DependencyObject
        {
            public static readonly DependencyProperty ColorProperty = DependencyProperty.Register("Color", typeof(Color), typeof(Tone));
            public Color Color { get { return (Color)GetValue(ColorProperty); } set { SetValue(ColorProperty, value); } }
            public readonly SolidColorBrush Brush = new SolidColorBrush();
            public Tone(Color color)
            {
                Color = color;
                BindingOperations.SetBinding(Brush, SolidColorBrush.ColorProperty, new Binding("Color") { Source = this, Mode = BindingMode.OneWay });
            }
        }

        static readonly Dictionary<string, Tone> brushes = new Dictionary<string, Tone>(StringComparer.OrdinalIgnoreCase);
        static readonly GradientStop shellTopStop = new GradientStop((Color)ColorConverter.ConvertFromString(TopTintHex[0]), 0.0);
        static readonly GradientStop shellBottomStop = new GradientStop((Color)ColorConverter.ConvertFromString(Colors[0][0]), 1.0);
        static readonly LinearGradientBrush shellGradient = CreateShellGradient();
        static int selected;

        static LinearGradientBrush CreateShellGradient()
        {
            var g = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0.0),
                EndPoint = new Point(0.5, 1.0)
            };
            g.GradientStops.Add(shellTopStop);
            g.GradientStops.Add(shellBottomStop);
            return g;
        }

        public static Brush ShellFill { get { return shellGradient; } }
        public static string Current { get { return Names[selected]; } }
        public static string Normalize(string name) { return Settings.NormalizeTheme(name); }

        public static Color AccentColor
        {
            get { return (Color)ColorConverter.ConvertFromString(Colors[selected][3]); }
        }

        static double Linear(byte value) { double c = value / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        static Color ColorsForText(bool light) { return light ? System.Windows.Media.Colors.White : System.Windows.Media.Colors.Black; }

        static double Luminance(Color c) { return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B); }
        public static double ContrastRatio(Color a, Color b) { double x=Luminance(a), y=Luminance(b); return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05); }
        public static Brush PrimaryInk { get { return Brush("#000001"); } }

        static Color Resolve(string key)
        {
            // Private semantic alias for text on primary action fills.
            if (key == "#000001") return Luminance(Resolve("#48E572")) > .179 ? ColorsForText(false) : ColorsForText(true);
            if (selected != 0 && key == "#18FFFFFF") return Color.FromArgb(12, 255, 255, 255);
            if (selected != 0 && key == "#26FFFFFF") { var color = Resolve("#48E572"); color.A = 38; return color; }
            if (key == "#FDFDFD")
            {
                var background = Resolve("#9160FF");
                double l = .2126 * Linear(background.R) + .7152 * Linear(background.G) + .0722 * Linear(background.B);
                return l > .179 ? ColorsForText(false) : ColorsForText(true);
            }
            int index = Array.IndexOf(Keys, key.ToUpperInvariant());
            if (index < 0 && (key == "#1D1D1D" || key == "#202020" || key == "#252525" || key == "#090909" || key == "#141414" || key == "#262626")) index = 4;
            if (index < 0 && (key == "#363636" || key == "#444444" || key == "#182838" || key == "#333333" || key == "#414141" || key == "#424242" || key == "#454545" || key == "#555555")) index = 5;
            if (index < 0 && (key == "#173422" || key == "#286A3B")) index = 7;
            if (key == "#8E8E8E" || key == "#858585" || key == "#787878" || key == "#B5B5B5") index = 2;
            if (key == "#C8C8C8" || key == "#CCCCCC") index = 1;
            if (key == "#379CF8" || key == "#9CD4FF") index = 11;
            if (key == "#9BBAFF") index = 10;
            if (key == "#124B7C")
            {
                var accent = Resolve("#279DFF");
                if (Current == "Custom") return Mix(Resolve("#151515"),accent,.20);
                return Color.FromRgb((byte)(accent.R * .32), (byte)(accent.G * .32), (byte)(accent.B * .32));
            }
            if (key == "#5548E572")
            {
                var c = Resolve("#48E572");
                c.A = 95;
                return c;
            }
            if (key == "#4DFFFFFF")
            {
                var c = Resolve("#30FFFFFF");
                c.A = 135;
                return c;
            }
            return (Color)ColorConverter.ConvertFromString(index < 0 ? key : Colors[selected][index]);
        }

        public static Brush Brush(string key)
        {
            Tone tone;
            if (!brushes.TryGetValue(key, out tone)) { tone = new Tone(Resolve(key)); brushes.Add(key, tone); }
            return tone.Brush;
        }

        static Color Mix(Color a, Color b, double weight)
        {
            return Color.FromRgb((byte)(a.R + (b.R-a.R)*weight), (byte)(a.G + (b.G-a.G)*weight), (byte)(a.B + (b.B-a.B)*weight));
        }
        public static void ConfigureCustom(Settings settings)
        {
            int i = Names.Length - 1;
            var shell = (Color)ColorConverter.ConvertFromString(Settings.ColorOrDefault(settings.CustomShell,"#0C111B"));
            var surface = (Color)ColorConverter.ConvertFromString(Settings.ColorOrDefault(settings.CustomSurface,"#1A2332"));
            var accent = (Color)ColorConverter.ConvertFromString(Settings.ColorOrDefault(settings.CustomAccent,"#8EC9FF"));
            var text = (Color)ColorConverter.ConvertFromString(Settings.ColorOrDefault(settings.CustomText,"#F3F6FC"));
            var rim = accent; rim.A = (byte)(Math.Max(0,Math.Min(60,settings.CustomOutline)) * 255 / 100);
            Colors[i] = new[] { shell.ToString(), text.ToString(), Settings.ColorOrDefault(settings.CustomMuted,"#A7B3C5"), accent.ToString(), surface.ToString(), Mix(surface,text,.18).ToString(), surface.ToString(), Mix(surface,accent,.18).ToString(), rim.ToString(), accent.ToString(), Settings.ColorOrDefault(settings.CustomSecondary,"#C2B4F3"), Settings.ColorOrDefault(settings.CustomSecondary,"#C2B4F3"), accent.ToString(), accent.ToString() };
            TopTintHex[i] = (settings.CustomFinish == "Matte" ? shell : Mix(shell,surface,.5)).ToString();
        }

        public static void Apply(string name)
        {
            selected = Array.IndexOf(Names, Normalize(name));
            shellTopStop.Color = (Color)ColorConverter.ConvertFromString(TopTintHex[selected]);
            shellBottomStop.Color = (Color)ColorConverter.ConvertFromString(Colors[selected][0]);
            foreach (var pair in brushes) pair.Value.Color = Resolve(pair.Key);
        }

        public static Brush Swatch(int index) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Colors[index][3])); }
        public static Brush SecondarySwatch(int index) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Colors[index][11])); }
        public static Brush ShellSwatch(int index) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(TopTintHex[index])); }
    }
}

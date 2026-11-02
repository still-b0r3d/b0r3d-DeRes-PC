using System;
using System.Collections.Generic;
using System.Linq;

namespace B0r3dDeRes.Engine
{
    // A machine's whole look, not just its colours: its own resolution, the shape of its pixels on
    // a TV, its palette, and the dither and screen that suit it. (The website's core/profiles.ts,
    // by the same ids, names and words.)
    sealed class Profile
    {
        public string Id, Name;
        public string Origin;   // who made it and when, on its card ("Nintendo · 1989")
        public string About;    // one plain sentence about the look
        public int Width;
        public int Height;      // ignored when AutoHeight
        public bool AutoHeight; // the height follows the picture (the receipt printer's paper is endless)
        public int ParX = 1, ParY = 1;  // pixel width : height as shown on its screen
        public string[] Colours;        // its fixed palette, "#rrggbb"
        // Grey levels: the picture is turned to grey and spread over the levels, darkest to
        // lightest, whatever their tint (the Game Boy's greens).
        public bool Mono;
        public string Dither;   // the dither it starts with
        public string Screen;   // the screen effect that suits it: crt, lcd, vhs or off

        public bool SquarePixels => ParX == ParY;

        // "1989 · 160×144", under the name on its card
        public string CardMeta => Origin.Split(new[] { " · " }, StringSplitOptions.None).Last() + " · " + Width + "×" + (AutoHeight ? "…" : Height.ToString());
    }

    static class Profiles
    {
        static readonly string[] Ega =
        {
            "#000000", "#0000aa", "#00aa00", "#00aaaa", "#aa0000", "#aa00aa", "#aa5500", "#aaaaaa",
            "#555555", "#5555ff", "#55ff55", "#55ffff", "#ff5555", "#ff55ff", "#ffff55", "#ffffff",
        };

        static readonly string[] C64 =
        {
            "#000000", "#ffffff", "#813338", "#75cec8", "#8e3c97", "#56ac4d", "#2e2c9b", "#edf171",
            "#8e5029", "#553800", "#c46c71", "#4a4a4a", "#7b7b7b", "#a9ff9f", "#706deb", "#b2b2b2",
        };

        // the NES's 64 entries less the repeated blacks (55 colours)
        static readonly string[] Nes =
        {
            "#7c7c7c", "#0000fc", "#0000bc", "#4428bc", "#940084", "#a80020", "#a81000", "#881400",
            "#503000", "#007800", "#006800", "#005800", "#004058", "#000000",
            "#bcbcbc", "#0078f8", "#0058f8", "#6844fc", "#d800cc", "#e40058", "#f83800", "#e45c10",
            "#ac7c00", "#00b800", "#00a800", "#00a844", "#008888",
            "#f8f8f8", "#3cbcfc", "#6888fc", "#9878f8", "#f878f8", "#f85898", "#f87858", "#fca044",
            "#f8b800", "#b8f818", "#58d854", "#58f898", "#00e8d8", "#787878",
            "#fcfcfc", "#a4e4fc", "#b8b8f8", "#d8b8f8", "#f8b8f8", "#f8a4c0", "#f0d0b0", "#fce0a8",
            "#f8d878", "#d8f878", "#b8f8b8", "#b8f8d8", "#00fcfc", "#f8d8f8",
        };

        static readonly string[] Pico8 =
        {
            "#000000", "#1d2b53", "#7e2553", "#008751", "#ab5236", "#5f574f", "#c2c3c7", "#fff1e8",
            "#ff004d", "#ffa300", "#ffec27", "#00e436", "#29adff", "#83769c", "#ff77a8", "#ffccaa",
        };

        // 16 hues × 8 brightness steps, NTSC
        static readonly string[] Atari2600 =
        {
            "#000000", "#4a4a4a", "#6f6f6f", "#8e8e8e", "#aaaaaa", "#c0c0c0", "#d6d6d6", "#ececec",
            "#484800", "#69690f", "#86861d", "#a2a22a", "#bbbb35", "#d2d240", "#e8e84a", "#fcfc54",
            "#7c2c00", "#904811", "#a26221", "#b47a30", "#c3903d", "#d2a44a", "#dfb755", "#ecc860",
            "#901c00", "#a33915", "#b55328", "#c66c3a", "#d5824a", "#e39759", "#f0aa67", "#fcbc74",
            "#940000", "#a71a1a", "#b83232", "#c84848", "#d65c5c", "#e46f6f", "#f08080", "#fc9090",
            "#840064", "#97197a", "#a8308f", "#b846a2", "#c659b3", "#d46cc3", "#e07cd2", "#ec8ce0",
            "#500084", "#68199a", "#7d30ad", "#9246c0", "#a459d0", "#b56ce0", "#c57cee", "#d48cfc",
            "#140090", "#331aa3", "#4e32b5", "#6848c6", "#7f5cd5", "#956fe3", "#a980f0", "#bc90fc",
            "#000094", "#181aa7", "#2d32b8", "#4248c8", "#545cd6", "#656fe4", "#7580f0", "#8490fc",
            "#001c88", "#183b9d", "#2d57b0", "#4272c2", "#548ad2", "#65a0e1", "#75b5ef", "#84c8fc",
            "#003064", "#185080", "#2d6d98", "#4288b0", "#54a0c5", "#65b7d9", "#75cceb", "#84e0fc",
            "#004030", "#18624e", "#2d8169", "#429e82", "#54b899", "#65d1ae", "#75e7c2", "#84fcd4",
            "#004400", "#1a661a", "#32842e", "#48a048", "#5cba5c", "#6fd26f", "#80e880", "#90fc90",
            "#143c00", "#355f18", "#527e2d", "#6e9c42", "#87b754", "#9ed065", "#b4e775", "#c8fc84",
            "#303800", "#505916", "#6d762b", "#88923e", "#a0ab4f", "#b7c25f", "#ccd86e", "#e0ec7c",
            "#482c00", "#694d14", "#866a26", "#a28638", "#bb9f47", "#d2b656", "#e8cc63", "#fce070",
        };

        // lo-res colours 5 and 10 are the same grey, so 15 entries
        static readonly string[] AppleII =
        {
            "#000000", "#e31e60", "#604ebd", "#ff44fd", "#00a360", "#9c9c9c", "#14cffd", "#d0c3ff",
            "#607203", "#ff6a3c", "#ffa0d0", "#14f53c", "#d0dd8d", "#72ffd0", "#ffffff",
        };

        static string[] WebSafe()
        {
            string[] steps = { "00", "33", "66", "99", "cc", "ff" };
            var all = new List<string>();
            foreach (string r in steps) foreach (string g in steps) foreach (string b in steps) all.Add("#" + r + g + b);
            return all.ToArray();
        }

        // The machines of this version, in the website's order: its fixed palettes from the Game
        // Boy to b0r3d, then the Nokia 3310 and the Virtual Boy. The rest (the Game Boy Camera,
        // the ranges, the cell machines and the others) come with the next version.
        public static readonly Profile[] All =
        {
            new Profile { Id = "gameboy", Name = "Game Boy", Origin = "Nintendo · 1989", About = "Four shades of green on a 160×144 screen.",
                Width = 160, Height = 144, Colours = new[] { "#081820", "#346856", "#88c070", "#e0f8d0" }, Mono = true, Dither = DitherIds.Bayer4, Screen = "lcd" },
            new Profile { Id = "gameboy-pocket", Name = "Game Boy Pocket", Origin = "Nintendo · 1996", About = "The sharper grey screen, still four shades.",
                Width = 160, Height = 144, Colours = new[] { "#1f1f1f", "#4d533c", "#8b956d", "#c4cfa1" }, Mono = true, Dither = DitherIds.Floyd, Screen = "lcd" },
            new Profile { Id = "cga-1", Name = "CGA palette 1", Origin = "IBM PC · 1981", About = "The 320×200 mode: black, cyan, magenta and white, in tall pixels.",
                Width = 320, Height = 200, ParX = 5, ParY = 6, Colours = new[] { "#000000", "#55ffff", "#ff55ff", "#ffffff" }, Dither = DitherIds.Bayer4, Screen = "crt" },
            new Profile { Id = "cga-0", Name = "CGA palette 0", Origin = "IBM PC · 1981", About = "The other 320×200 palette: green, red and yellow.",
                Width = 320, Height = 200, ParX = 5, ParY = 6, Colours = new[] { "#000000", "#55ff55", "#ff5555", "#ffff55" }, Dither = DitherIds.Bayer4, Screen = "crt" },
            new Profile { Id = "ega", Name = "EGA", Origin = "IBM PC · 1984", About = "Sixteen colours at 320×200: early DOS games.",
                Width = 320, Height = 200, ParX = 5, ParY = 6, Colours = Ega, Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "c64-hires", Name = "C64 hi-res", Origin = "Commodore · 1982", About = "The C64’s sixteen muted colours at 320×200.",
                Width = 320, Height = 200, ParX = 15, ParY = 16, Colours = C64, Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "nes", Name = "NES", Origin = "Nintendo · 1983", About = "The full NES colour set at 256×240, pixels a little wide.",
                Width = 256, Height = 240, ParX = 8, ParY = 7, Colours = Nes, Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "pico8", Name = "PICO-8", Origin = "Lexaloffle · 2015", About = "The fantasy console: 128×128, sixteen colours.",
                Width = 128, Height = 128, Colours = Pico8, Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "atari-2600", Name = "Atari 2600", Origin = "Atari · 1977", About = "128 NTSC colours at 160×192, pixels almost twice as wide as tall.",
                Width = 160, Height = 192, ParX = 12, ParY = 7, Colours = Atari2600, Dither = DitherIds.None, Screen = "crt" },
            new Profile { Id = "apple2-lores", Name = "Apple II lo-res", Origin = "Apple · 1977", About = "40×48 blocks of sixteen colours.",
                Width = 40, Height = 48, ParX = 8, ParY = 5, Colours = AppleII, Dither = DitherIds.None, Screen = "crt" },
            new Profile { Id = "mac", Name = "Mac 1-bit", Origin = "Apple · 1984", About = "Black and white at 512×342: the MacPaint look.",
                Width = 512, Height = 342, Colours = new[] { "#000000", "#ffffff" }, Mono = true, Dither = DitherIds.Atkinson, Screen = "crt" },
            new Profile { Id = "web-safe", Name = "Web-safe 216", Origin = "Windows 95 era · 1996", About = "The 216 colours every 90s browser agreed on, at 640×480.",
                Width = 640, Height = 480, Colours = WebSafe(), Dither = DitherIds.Bayer4, Screen = "crt" },
            new Profile { Id = "receipt", Name = "Receipt printer", Origin = "Thermal · 58 mm", About = "Black dots on a 384-dot strip of paper.",
                Width = 384, AutoHeight = true, Colours = new[] { "#232323", "#f3f0e8" }, Mono = true, Dither = DitherIds.Floyd, Screen = "lcd" },
            new Profile { Id = "b0r3d", Name = "b0r3d", Origin = "b0r3d.org", About = "Cyan, magenta and mint on near-black.",
                Width = 320, Height = 240, Colours = new[] { "#131313", "#00ffff", "#ff00ff", "#00ff9d" }, Dither = DitherIds.Bayer4, Screen = "crt" },
            new Profile { Id = "nokia-3310", Name = "Nokia 3310", Origin = "Nokia · 2000", About = "84×48 monochrome graphics on a green reflective backlight LCD.",
                Width = 84, Height = 48, Colours = new[] { "#1c281a", "#879b6f" }, Mono = true, Dither = DitherIds.Bayer4, Screen = "lcd" },
            new Profile { Id = "virtualboy", Name = "Virtual Boy", Origin = "Nintendo · 1995", About = "384×224 red linear array LEDs reflected by oscillating scanning mirrors.",
                Width = 384, Height = 224, Colours = new[] { "#000000", "#460000", "#8c0000", "#ff0000" }, Mono = true, Dither = DitherIds.Bayer4, Screen = "crt" },
        };

        public const string DefaultId = "gameboy";

        public static Profile ById(string id) => All.FirstOrDefault(p => p.Id == id);

        public static Profile Default => ById(DefaultId);

        // The colours as bytes (r, g, b an entry). Grey-level machines are sorted darkest first.
        public static byte[] PaletteBytes(Profile p)
        {
            List<byte[]> colours = p.Colours.Select(Colour.HexToRgb).ToList();
            // (a stable sort, as the browser's: equal greys keep their order)
            if (p.Mono) colours = colours.OrderBy(Lum).ToList();
            return colours.SelectMany(c => c).ToArray();
        }

        internal static double Lum(byte[] c) => Colour.Luminance(Colour.ByteToLinear[c[0]], Colour.ByteToLinear[c[1]], Colour.ByteToLinear[c[2]]);
    }
}

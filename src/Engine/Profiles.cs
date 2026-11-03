using System;
using System.Collections.Generic;
using System.Linq;

namespace B0r3dDeRes.Engine
{
    // How a machine gets its colours.
    enum PaletteKind
    {
        Fixed,      // its own list
        Range,      // picked from the picture, out of a range (bits a channel)
        Direct,     // every colour of its range, straight (the PlayStation's 15 bits)
        Custom,     // picked from the picture, or loaded from a palette file
        Pool,       // picked from the picture, out of a fixed pool (the Amstrad CPC's 27)
        Cell,       // a few colours a cell, from its list
        Ham,        // the Amiga's Hold-And-Modify
        N64,        // the Nintendo 64's filter
        Analog,     // the analog, print and video looks: they make their own colours
    }

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

        public PaletteKind Kind = PaletteKind.Fixed;
        public string[] Colours;        // fixed and cell: its colours, "#rrggbb"
        public int BitsPerChannel;      // range, direct and HAM: its range
        public int Count;               // range, pool and Custom: how many colours it picks
        public string[] Pool;           // pool: what it picks from
        public string Mode;             // cell: spectrum, c64-mc, nes-attr, apple2-hires, msx-screen2 or gbc-tile; HAM: ham6;
                                        // analog: ccd, deepfry, newspaper, risograph or fax

        public int? MaxTiles, MaxColours;   // what the hardware could hold (the tile meter)
        public string[] EmulatorFormats = new string[0];   // its own files: scr, koa, chr
        public string ExportNote;       // a line under the export buttons, e.g. why a file type is missing

        // Grey levels: the picture is turned to grey and spread over the levels, darkest to
        // lightest, whatever their tint (the Game Boy's greens).
        public bool Mono;
        public string Dither;           // the dither it starts with
        public string NativeDither;     // a dither only this machine has, offered when it's picked
        public double EdgeBoost;        // sharpening before the dither (the Game Boy Camera's)
        public string Screen;           // the screen effect that suits it: crt, lcd, vhs or off

        public bool SquarePixels => ParX == ParY;
        public bool HasLimits => MaxTiles != null || MaxColours != null || EmulatorFormats.Length > 0;

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

        public static readonly string[] AmstradCpcPalette =
        {
            "#000000", "#000080", "#0000ff", "#008000", "#008080", "#0080ff", "#00ff00", "#00ff80", "#00ffff",
            "#800000", "#800080", "#8000ff", "#808000", "#808080", "#8080ff", "#80ff00", "#80ff80", "#80ffff",
            "#ff0000", "#ff0080", "#ff00ff", "#ff8000", "#ff8080", "#ff80ff", "#ffff00", "#ffff80", "#ffffff",
        };

        public static readonly string[] CgaCompositePalette =
        {
            "#000000", "#1e3b60", "#2e581e", "#3e766e",
            "#70281e", "#5e585e", "#7e761e", "#9e987e",
            "#5e287e", "#4e58be", "#7e764e", "#6e98be",
            "#be383e", "#be589e", "#ce983e", "#ffffff",
        };

        static string[] WebSafe()
        {
            string[] steps = { "00", "33", "66", "99", "cc", "ff" };
            var all = new List<string>();
            foreach (string r in steps) foreach (string g in steps) foreach (string b in steps) all.Add("#" + r + g + b);
            return all.ToArray();
        }

        static readonly string[] Chr = { "chr" };

        // The machines, in the website's order.
        public static readonly Profile[] All =
        {
            new Profile { Id = "gameboy", Name = "Game Boy", Origin = "Nintendo · 1989", About = "Four shades of green on a 160×144 screen.",
                Width = 160, Height = 144, Colours = new[] { "#081820", "#346856", "#88c070", "#e0f8d0" }, MaxTiles = 256, MaxColours = 4, EmulatorFormats = Chr,
                Mono = true, Dither = DitherIds.Bayer4, Screen = "lcd" },
            new Profile { Id = "gameboy-pocket", Name = "Game Boy Pocket", Origin = "Nintendo · 1996", About = "The sharper grey screen, still four shades.",
                Width = 160, Height = 144, Colours = new[] { "#1f1f1f", "#4d533c", "#8b956d", "#c4cfa1" }, MaxTiles = 256, MaxColours = 4, EmulatorFormats = Chr,
                Mono = true, Dither = DitherIds.Floyd, Screen = "lcd" },
            new Profile { Id = "gameboy-camera", Name = "Game Boy Camera", Origin = "Nintendo · 1998", About = "128×112, four greys, sharpened edges and its own dither pattern.",
                Width = 128, Height = 112, Colours = new[] { "#000000", "#555555", "#aaaaaa", "#ffffff" }, MaxTiles = 224, MaxColours = 4, EmulatorFormats = Chr,
                Mono = true, Dither = DitherIds.GameBoyCamera, NativeDither = DitherIds.GameBoyCamera, EdgeBoost = 0.6, Screen = "lcd" },
            new Profile { Id = "cga-1", Name = "CGA palette 1", Origin = "IBM PC · 1981", About = "The 320×200 mode: black, cyan, magenta and white, in tall pixels.",
                Width = 320, Height = 200, ParX = 5, ParY = 6, Colours = new[] { "#000000", "#55ffff", "#ff55ff", "#ffffff" }, Dither = DitherIds.Bayer4, Screen = "crt" },
            new Profile { Id = "cga-0", Name = "CGA palette 0", Origin = "IBM PC · 1981", About = "The other 320×200 palette: green, red and yellow.",
                Width = 320, Height = 200, ParX = 5, ParY = 6, Colours = new[] { "#000000", "#55ff55", "#ff5555", "#ffff55" }, Dither = DitherIds.Bayer4, Screen = "crt" },
            new Profile { Id = "ega", Name = "EGA", Origin = "IBM PC · 1984", About = "Sixteen colours at 320×200: early DOS games.",
                Width = 320, Height = 200, ParX = 5, ParY = 6, Colours = Ega, Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "c64-hires", Name = "C64 hi-res", Origin = "Commodore · 1982", About = "The C64’s sixteen muted colours at 320×200.",
                Width = 320, Height = 200, ParX = 15, ParY = 16, Colours = C64, MaxTiles = 256, MaxColours = 16, Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "c64-mc", Name = "C64 multicolour", Origin = "Commodore · 1982", About = "160×200 wide pixels with 4×8 double cells: 1 shared backdrop and 3 cell colours.",
                Width = 160, Height = 200, ParX = 15, ParY = 8, Kind = PaletteKind.Cell, Mode = "c64-mc", Colours = Cell.C64Palette,
                MaxTiles = 256, MaxColours = 16, EmulatorFormats = new[] { "koa" }, Dither = DitherIds.None, Screen = "crt" },
            new Profile { Id = "zx-spectrum", Name = "ZX Spectrum", Origin = "Sinclair · 1982", About = "256×192 with 8×8 cell attribute clash: 1 ink and 1 paper colour per cell.",
                Width = 256, Height = 192, Kind = PaletteKind.Cell, Mode = "spectrum", Colours = Cell.SpectrumPalette,
                MaxTiles = 768, MaxColours = 15, EmulatorFormats = new[] { "scr" }, Dither = DitherIds.None, Screen = "crt" },
            new Profile { Id = "nes", Name = "NES", Origin = "Nintendo · 1983", About = "The full NES colour set at 256×240, pixels a little wide.",
                Width = 256, Height = 240, ParX = 8, ParY = 7, Colours = Nes, MaxTiles = 256, MaxColours = 25,
                ExportNote = "No .chr here: this look uses any colour anywhere, which a real NES can't show. NES (attributes) makes one.",
                Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "nes-attr", Name = "NES attributes", Origin = "Nintendo · 1983", About = "256×240 with 16×16 attribute blocks: 4 hardware sub-palettes + 1 backdrop.",
                Width = 256, Height = 240, ParX = 8, ParY = 7, Kind = PaletteKind.Cell, Mode = "nes-attr", Colours = Cell.NesPalette,
                MaxTiles = 256, MaxColours = 25, EmulatorFormats = Chr, Dither = DitherIds.None, Screen = "crt" },
            new Profile { Id = "pico8", Name = "PICO-8", Origin = "Lexaloffle · 2015", About = "The fantasy console: 128×128, sixteen colours.",
                Width = 128, Height = 128, Colours = Pico8, MaxTiles = 256, MaxColours = 16, Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "atari-2600", Name = "Atari 2600", Origin = "Atari · 1977", About = "128 NTSC colours at 160×192, pixels almost twice as wide as tall.",
                Width = 160, Height = 192, ParX = 12, ParY = 7, Colours = Atari2600, Dither = DitherIds.None, Screen = "crt" },
            new Profile { Id = "apple2-lores", Name = "Apple II lo-res", Origin = "Apple · 1977", About = "40×48 blocks of sixteen colours.",
                Width = 40, Height = 48, ParX = 8, ParY = 5, Colours = AppleII, MaxColours = 16, Dither = DitherIds.None, Screen = "crt" },
            new Profile { Id = "apple2-hires", Name = "Apple II hi-res", Origin = "Apple · 1977", About = "280×192 with 7-pixel byte rule: high bit selects Green/Violet or Orange/Blue.",
                Width = 280, Height = 192, ParX = 6, ParY = 7, Kind = PaletteKind.Cell, Mode = "apple2-hires", Colours = Cell.Apple2HiresPalette,
                MaxColours = 6, Dither = DitherIds.None, Screen = "crt" },
            new Profile { Id = "mac", Name = "Mac 1-bit", Origin = "Apple · 1984", About = "Black and white at 512×342: the MacPaint look.",
                Width = 512, Height = 342, Colours = new[] { "#000000", "#ffffff" }, Mono = true, Dither = DitherIds.Atkinson, Screen = "crt" },
            new Profile { Id = "web-safe", Name = "Web-safe 216", Origin = "Windows 95 era · 1996", About = "The 216 colours every 90s browser agreed on, at 640×480.",
                Width = 640, Height = 480, Colours = WebSafe(), MaxColours = 216, Dither = DitherIds.Bayer4, Screen = "crt" },
            new Profile { Id = "receipt", Name = "Receipt printer", Origin = "Thermal · 58 mm", About = "Black dots on a 384-dot strip of paper.",
                Width = 384, AutoHeight = true, Colours = new[] { "#232323", "#f3f0e8" }, Mono = true, Dither = DitherIds.Floyd, Screen = "lcd" },
            new Profile { Id = "b0r3d", Name = "b0r3d", Origin = "b0r3d.org", About = "Cyan, magenta and mint on near-black.",
                Width = 320, Height = 240, Colours = new[] { "#131313", "#00ffff", "#ff00ff", "#00ff9d" }, Dither = DitherIds.Bayer4, Screen = "crt" },
            new Profile { Id = "amiga-ham6", Name = "Amiga HAM6", Origin = "Commodore · 1985", About = "320×200 Hold-And-Modify: 16 base palette colours with horizontal channel hold-and-modify fringing.",
                Width = 320, Height = 200, ParX = 5, ParY = 6, Kind = PaletteKind.Ham, Mode = "ham6", BitsPerChannel = 4, MaxColours = 4096, Dither = DitherIds.None, Screen = "crt" },
            new Profile { Id = "snes", Name = "Super Nintendo", Origin = "Nintendo · 1990", About = "32,768-colour range (5 bits per channel), 256 on screen at 256×224.",
                Width = 256, Height = 224, ParX = 8, ParY = 7, Kind = PaletteKind.Range, BitsPerChannel = 5, Count = 256, MaxTiles = 1024, MaxColours = 256, Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "genesis", Name = "Sega Genesis", Origin = "Sega · 1988", About = "512-colour range (3 bits per channel), 61 on screen at 320×224.",
                Width = 320, Height = 224, ParX = 10, ParY = 11, Kind = PaletteKind.Range, BitsPerChannel = 3, Count = 61, MaxTiles = 2048, MaxColours = 64, Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "mastersystem", Name = "Master System", Origin = "Sega · 1985", About = "64-colour range (2 bits per channel), 32 on screen at 256×192.",
                Width = 256, Height = 192, ParX = 8, ParY = 7, Kind = PaletteKind.Range, BitsPerChannel = 2, Count = 32, MaxTiles = 512, MaxColours = 32, Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "pc98", Name = "PC-9801", Origin = "NEC · 1982", About = "16 colours picked from 4,096 (4 bits per channel) at 640×400.",
                Width = 640, Height = 400, Kind = PaletteKind.Range, BitsPerChannel = 4, Count = 16, MaxColours = 16, Dither = DitherIds.Bayer4, Screen = "crt" },
            new Profile { Id = "vga", Name = "VGA 256", Origin = "IBM · 1987", About = "256 colours from 262,144 (6 bits per channel) at 320×200.",
                Width = 320, Height = 200, ParX = 5, ParY = 6, Kind = PaletteKind.Range, BitsPerChannel = 6, Count = 256, MaxColours = 256, Dither = DitherIds.None, Screen = "crt" },
            new Profile { Id = "ps1", Name = "PlayStation", Origin = "Sony · 1994", About = "15-bit colour with the GPU’s hardware dither table at 320×240.",
                Width = 320, Height = 240, Kind = PaletteKind.Direct, BitsPerChannel = 5, Dither = DitherIds.Ps1, NativeDither = DitherIds.Ps1, Screen = "crt" },
            new Profile { Id = "n64", Name = "Nintendo 64", Origin = "Nintendo · 1996", About = "320×240 at 16-bit colour with RDP 3-point sample filter and VI anti-aliasing blur.",
                Width = 320, Height = 240, Kind = PaletteKind.N64, MaxColours = 32768, Dither = DitherIds.None, Screen = "crt" },
            new Profile { Id = "vhs", Name = "VHS Tape", Origin = "JVC · 1976", About = "Analog magnetic tape, 240 lines, horizontal chroma smear and tracking noise.",
                Width = 320, Height = 240, ParX = 10, ParY = 11, Kind = PaletteKind.Range, BitsPerChannel = 6, Count = 256, MaxColours = 256, Dither = DitherIds.None, Screen = "vhs" },
            new Profile { Id = "ccd-early", Name = "Early CCD Camera", Origin = "VGA Digital · 1998", About = "640×480 sensor, CCD photon noise, and burnt-orange digital date stamp.",
                Width = 640, Height = 480, Kind = PaletteKind.Analog, Mode = Analog.Ccd, Dither = DitherIds.None, Screen = "off" },
            new Profile { Id = "deepfry", Name = "JPEG Deep Fry", Origin = "IJG JPEG · 1992", About = "8×8 DCT block quantization crunch, high-frequency ringing, and hyper-saturated contrast.",
                Width = 480, Height = 360, Kind = PaletteKind.Analog, Mode = Analog.DeepFry, Dither = DitherIds.None, Screen = "off" },
            new Profile { Id = "newspaper", Name = "Newspaper Halftone", Origin = "Rotary Press · 1890", About = "CMYK angled screen rosettes (15°, 75°, 0°, 45°) on unbleached newsprint.",
                Width = 400, Height = 500, Kind = PaletteKind.Analog, Mode = Analog.Newspaper, Dither = DitherIds.None, Screen = "off" },
            new Profile { Id = "risograph", Name = "Two-Ink Risograph", Origin = "Riso Kagaku · 1980", About = "Dual spot-color ink layering (Fluo Pink & Teal) with mechanical drum misregistration.",
                Width = 400, Height = 500, Kind = PaletteKind.Analog, Mode = Analog.Risograph, Dither = DitherIds.None, Screen = "off" },
            new Profile { Id = "fax", Name = "1-Bit Fax Machine", Origin = "ITU-T Group 3 · 1980", About = "High-contrast 1-bit thermal print with paper feed streaks and transmission header.",
                Width = 408, Height = 392, Kind = PaletteKind.Analog, Mode = Analog.Fax, Dither = DitherIds.Floyd, Screen = "off" },
            new Profile { Id = "nokia-3310", Name = "Nokia 3310", Origin = "Nokia · 2000", About = "84×48 monochrome graphics on a green reflective backlight LCD.",
                Width = 84, Height = 48, Colours = new[] { "#1c281a", "#879b6f" }, Mono = true, Dither = DitherIds.Bayer4, Screen = "lcd" },
            new Profile { Id = "gbc", Name = "Game Boy Color", Origin = "Nintendo · 1998", About = "160×144 with 8 background palettes of 4 colours (32 max) from 32,768.",
                Width = 160, Height = 144, Kind = PaletteKind.Cell, Mode = "gbc-tile", Colours = Cell.GbcRepresentativePalette,
                MaxTiles = 360, MaxColours = 32, EmulatorFormats = Chr, Dither = DitherIds.Bayer4, Screen = "lcd" },
            new Profile { Id = "virtualboy", Name = "Virtual Boy", Origin = "Nintendo · 1995", About = "384×224 red linear array LEDs reflected by oscillating scanning mirrors.",
                Width = 384, Height = 224, Colours = new[] { "#000000", "#460000", "#8c0000", "#ff0000" }, Mono = true, Dither = DitherIds.Bayer4, Screen = "crt" },
            new Profile { Id = "atari-st", Name = "Atari ST", Origin = "Atari · 1985", About = "16 colours picked from 512 (3 bits per channel) at 320×200.",
                Width = 320, Height = 200, ParX = 5, ParY = 6, Kind = PaletteKind.Range, BitsPerChannel = 3, Count = 16, MaxColours = 16, Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "amstrad-cpc-0", Name = "Amstrad CPC (Mode 0)", Origin = "Amstrad · 1984", About = "160×200 with 16 colours picked from 27, 2:1 wide pixels.",
                Width = 160, Height = 200, ParX = 2, ParY = 1, Kind = PaletteKind.Pool, Pool = AmstradCpcPalette, Count = 16, MaxColours = 16, Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "amstrad-cpc-1", Name = "Amstrad CPC (Mode 1)", Origin = "Amstrad · 1984", About = "320×200 with 4 colours picked from 27.",
                Width = 320, Height = 200, Kind = PaletteKind.Pool, Pool = AmstradCpcPalette, Count = 4, MaxColours = 4, Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "msx-screen2", Name = "MSX (Screen 2)", Origin = "ASCII / Microsoft · 1983", About = "256×192 with TMS9918: 2 colours per 8×1 horizontal pixel slice from 15.",
                Width = 256, Height = 192, ParX = 8, ParY = 7, Kind = PaletteKind.Cell, Mode = "msx-screen2", Colours = Cell.Tms9918Palette,
                MaxColours = 15, Dither = DitherIds.None, Screen = "crt" },
            new Profile { Id = "cga-composite", Name = "CGA Composite", Origin = "IBM · 1981", About = "16 colours produced by NTSC composite color burst subcarrier crosstalk.",
                Width = 320, Height = 200, ParX = 5, ParY = 6, Colours = CgaCompositePalette, MaxColours = 16, Dither = DitherIds.Floyd, Screen = "crt" },
            new Profile { Id = "custom", Name = "Custom palette", Origin = "User custom", About = "Quantized to N colours or loaded from a palette file.",
                Width = 320, Height = 240, Kind = PaletteKind.Custom, Count = 16, Dither = DitherIds.Floyd, Screen = "crt" },
        };

        public const string DefaultId = "gameboy";
        public const string CustomId = "custom";

        public static Profile ById(string id) => All.FirstOrDefault(p => p.Id == id);

        public static Profile Default => ById(DefaultId);

        // HSL to sRGB bytes, as the website draws the cards of the machines that pick their colours
        static int[] HslToRgb(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
            double m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (0 <= h && h < 60) { r = c; g = x; }
            else if (60 <= h && h < 120) { r = x; g = c; }
            else if (120 <= h && h < 180) { g = c; b = x; }
            else if (180 <= h && h < 240) { g = x; b = c; }
            else if (240 <= h && h < 300) { r = x; b = c; }
            else if (300 <= h && h < 360) { r = c; b = x; }
            return new[] { (int)JsMath.Round((r + m) * 255), (int)JsMath.Round((g + m) * 255), (int)JsMath.Round((b + m) * 255) };
        }

        static string Snapped(int[] rgb, int bits) => Colour.RgbToHex(Quantize.SnapToBits(rgb[0], bits), Quantize.SnapToBits(rgb[1], bits), Quantize.SnapToBits(rgb[2], bits));

        // The colours a machine shows on its card (and uses when a picture gives none): its own
        // list, or a spread of its range.
        public static string[] RepresentativePalette(Profile p)
        {
            switch (p.Kind)
            {
                case PaletteKind.Fixed:
                case PaletteKind.Cell:
                    return (string[])p.Colours.Clone();
                case PaletteKind.Ham:
                    return Enumerable.Range(0, 16).Select(i => Snapped(HslToRgb(i * (360.0 / 16), 0.8, 0.5), 4)).ToArray();
                case PaletteKind.N64:
                    return Enumerable.Range(0, 32).Select(i => Snapped(HslToRgb(i * (360.0 / 32), 0.75, 0.5), 5)).ToArray();
                case PaletteKind.Range:
                {
                    int count = Math.Min(256, Math.Max(1, p.Count));
                    double step = 360.0 / count;
                    return Enumerable.Range(0, count).Select(i => Snapped(HslToRgb(i * step, 0.85, 0.2 + 0.6 * (i % 8 + 1) / 8), p.BitsPerChannel)).ToArray();
                }
                case PaletteKind.Direct:
                    return Enumerable.Range(0, 32).Select(i => Snapped(HslToRgb(i * (360.0 / 32), 0.8, 0.5), p.BitsPerChannel)).ToArray();
                case PaletteKind.Analog:
                    switch (p.Mode)
                    {
                        case Analog.Fax: return new[] { "#ffffff", "#000000" };
                        case Analog.Risograph: return new[] { "#fbf9f4", "#ff48b0", "#00838a", "#20124d" };
                        case Analog.Newspaper: return new[] { "#f2ede4", "#009ee0", "#e5007d", "#ffe600", "#1a171b" };
                        case Analog.DeepFry: return new[] { "#000000", "#ff0000", "#ffff00", "#00ff00", "#00ffff", "#0000ff", "#ff00ff", "#ffffff" };
                        default: return Enumerable.Range(0, 32).Select(i => HslToRgb(i * (360.0 / 32), 0.8, 0.5)).Select(c => Colour.RgbToHex(c[0], c[1], c[2])).ToArray();
                    }
                case PaletteKind.Pool:
                {
                    double step = (double)p.Pool.Length / p.Count;
                    return Enumerable.Range(0, p.Count).Select(i => p.Pool[(int)Math.Floor(i * step)]).ToArray();
                }
                default:
                    return new[]
                    {
                        "#000000", "#1d2b53", "#7e2553", "#008751", "#ab5236", "#5f574f", "#c2c3c7", "#fff1e8",
                        "#ff004d", "#ffa300", "#ffec27", "#00e436", "#29adff", "#83769c", "#ff77a8", "#ffffff",
                    };
            }
        }

        // The colours as bytes (r, g, b an entry): Custom's loaded or grabbed palette when it has
        // one, else the card's. Grey-level machines are sorted darkest first.
        public static byte[] PaletteBytes(Profile p, string[] customPalette = null)
        {
            string[] hexes = p.Kind == PaletteKind.Custom && customPalette != null && customPalette.Length > 0 ? customPalette : RepresentativePalette(p);
            List<byte[]> colours = hexes.Select(Colour.HexToRgb).ToList();
            // (a stable sort, as the browser's: equal greys keep their order)
            if (p.Mono) colours = colours.OrderBy(Lum).ToList();
            return colours.SelectMany(c => c).ToArray();
        }

        internal static double Lum(byte[] c) => Colour.Luminance(Colour.ByteToLinear[c[0]], Colour.ByteToLinear[c[1]], Colour.ByteToLinear[c[2]]);

        // ---- what the controls offer on a machine (the website's pipeline.ts helpers) ----

        // The most colours the Colours slider can ask for (null: no slider).
        public static int? MaxColourCount(Profile p) =>
            p.Kind == PaletteKind.Range || p.Kind == PaletteKind.Pool ? p.Count : p.Kind == PaletteKind.Custom ? 256 : (int?)null;

        // How many colours a range, pool or Custom machine picks: its own count, or fewer when the
        // slider asks, never more than it had and never fewer than 2. Null for the rest.
        public static int? ColourCount(Profile p, int? customCount)
        {
            int? max = MaxColourCount(p);
            if (max == null) return null;
            int want = customCount ?? p.Count;
            return Math.Min(max.Value, Math.Max(2, want));
        }

        // The "of N" in "N of M colours used" (null for machines with no list: they make their own).
        public static int? ColourTotal(Profile p, byte[] palette) =>
            p.Kind == PaletteKind.Direct || p.Kind == PaletteKind.Ham || p.Kind == PaletteKind.N64 || p.Kind == PaletteKind.Analog ? (int?)null : palette.Length / 3;

        // Whether each pixel is one of the palette's entries (not for the machines that make their own colours).
        public static bool PixelsArePalette(Profile p) =>
            p.Kind == PaletteKind.Fixed || p.Kind == PaletteKind.Range || p.Kind == PaletteKind.Pool || p.Kind == PaletteKind.Custom || p.Kind == PaletteKind.Cell;

        // Whether a dither changes anything on a machine: the cell machines, HAM, the N64 and the
        // analog looks make their own colours, and the PlayStation has only its own dither.
        public static bool DitherApplies(Profile p, string id)
        {
            if (p.Kind == PaletteKind.Cell || p.Kind == PaletteKind.Ham || p.Kind == PaletteKind.N64 || p.Kind == PaletteKind.Analog) return false;
            if (p.Kind == PaletteKind.Direct) return id == DitherIds.None || id == DitherIds.Ps1;
            return true;
        }

        // One line saying why some dithers are greyed out on a machine, or null.
        public static string DitherNote(Profile p)
        {
            if (p.Kind == PaletteKind.Cell) return "This machine picks its colours cell by cell, so dithering doesn’t apply.";
            if (p.Kind == PaletteKind.Ham || p.Kind == PaletteKind.N64 || p.Kind == PaletteKind.Analog) return "This look makes its own colours, so dithering doesn’t apply.";
            if (p.Kind == PaletteKind.Direct) return "The PlayStation has only its own hardware dither (or none).";
            return null;
        }
    }
}

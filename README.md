# b0r3d DeRes for Windows

**b0r3d DeRes** turns photos into the pictures of old computers and consoles: their
resolution, their colours, their dithers. It's the Windows program of
[img.b0r3d.org](https://img.b0r3d.org), made by [b0r3d.org](https://b0r3d.org), and it makes
the same pictures, pixel for pixel (photos over 2048 pixels aside: see below). Your pictures
stay on your PC: it never goes online, and it never keeps them.

It's one small exe that runs on 64-bit Windows 10 and 11 with nothing to install.

**Being built.** This is version 0.2, the second of nine stages: the program itself, opening
pictures, every machine of the website but its analog looks, Resolution, Adjust, the dithers,
the preview, saving, copying and dragging out PNGs, the machines' own files for emulators,
palette files, look files and share links. The analog looks and the screen effects come next,
then the Signal, GIFs, video, the ASCII / ANSI studio and the EXIF tools.

## Make a picture

Put `b0r3dDeRes.exe` in a folder of its own and double-click it. It opens with the Rukus
sample on the Game Boy, as the website does. Open a picture of your own with **File → Open
picture…** (Ctrl+O), drop one anywhere on the window, paste one (Ctrl+V), or drop it on
`b0r3dDeRes.exe` itself. PNG, JPEG, GIF (its first picture, until GIFs play in a later version),
BMP and TIFF always open, and WebP, HEIC, AVIF,
JPEG XL and camera RAW photos where Windows has Microsoft's free extensions for them. Photos
come out the right way up, and in their own colours when they carry a colour profile. The
whole photo is used, however big (up to 150 megapixels).

The settings on the left fold out like the website's:

- **Image:** the samples, the picture's name and size, **Resolution** (the machine's own, or
  a custom width from 8 to 1024 pixels), **Framing** (crop to fill, fit with bars, or
  stretch), **Detail** (brings back the thin lines that shrinking blurs away) and, for a
  picture with see-through parts, what they sit on: white, black, or **Keep** (they stay
  see-through, over a checkerboard in the preview and in the PNG). A line under Resolution says
  what the machine's size means for your photo.
- **Hardware:** 36 machines, each with its colours, and **Surprise me** for a random one.
  - Fixed colours: Game Boy, Game Boy Pocket, the Game Boy Camera (its own sharpening and
    dither), CGA palettes 1 and 0, EGA, C64 hi-res, NES, PICO-8, Atari 2600, Apple II lo-res,
    Mac 1-bit, Web-safe 216, the receipt printer, b0r3d, Nokia 3310, Virtual Boy and CGA
    composite.
  - Colours picked from the picture: Super Nintendo, Sega Genesis, Master System, PC-9801,
    VGA 256, Atari ST, the Amstrad CPC in modes 0 and 1, and **Custom**. The **Colours**
    slider asks for fewer of them. **Load palette…** takes a `.hex`, JASC `.pal`, GIMP `.gpl`
    or a palette picture, and **Grab palette** takes the picture's own colours; either becomes
    the Custom machine's.
  - Colours picked cell by cell, with the clash that came with it: the ZX Spectrum, C64
    multicolour, NES attributes, Apple II hi-res, MSX Screen 2 and the Game Boy Color.
  - Colours of their own: Amiga HAM6, the PlayStation's 15-bit colour (with its GPU's dither)
    and the Nintendo 64's filter.

  Under them, the **tile meter**: how many different 8×8 tiles the picture has, against what
  the machine could hold.
- **Adjust:** brightness, contrast, saturation and gamma.
- **Dither:** none, Bayer 2×2, 4×4 and 8×8, Floyd–Steinberg, Atkinson and Yliluoma, plus the
  Game Boy Camera's and the PlayStation's own on those machines. The ones that change nothing
  on a machine are greyed out, and a line says why.
- **Export:** the size (1× to 16×), and for machines whose pixels weren't square, raw pixels
  or as on a TV.

**Reset settings** at the bottom puts everything back as a first start has it (the picture
stays).

The picture on the right is drawn at your screen's own pixels: every machine pixel is a sharp
block of whole screen pixels, on a 150 % screen too. **Fit** shows it all; the mouse wheel,
Ctrl+plus and Ctrl+minus zoom in whole steps; drag to move around a picture bigger than the
window. Hold the picture (or Space) to see the original, or turn **Compare** on and drag the
divider. **Full screen** (F11) shows just the picture; Esc comes back. Under it, the website's
line: how many colours it uses, its size, the shape of its pixels and the tile meter.

## Save it

**File → Save PNG…** (Ctrl+S) saves the picture at the size you chose, named the way the
website names it (`photo-nes-256x240-4x.png`), as an indexed PNG holding the machine's palette,
and nothing else: no metadata, no program name, at any size. The looks that make their own
colours (HAM, the PlayStation, the N64) hold exactly the colours they use, or go full colour
past 256 of them. Parts kept see-through stay see-through. **Edit → Copy picture** (Ctrl+C)
copies it, and the small picture under the preview drags it into a folder or another program,
as a PNG and as a plain picture; those two go at most 16 megapixels, the website's own limit,
so a bigger Size goes at the biggest one within it, and the bottom line says so.

Machines with a file of their own save it too, byte for byte as the website makes it, for
emulators and the old paint programs: the ZX Spectrum's `.scr`, the C64 multicolour's Koala
`.koa`, and the tiles (`.chr`) of the Game Boys and the NES with attributes. **File → Save
palette…** keeps the picture's colours as a `.hex`, `.pal`, `.gpl` or palette picture.

## Looks and share links

A look is every setting that makes the picture, without the picture. **File → Save look…**
keeps it in a small `.json` file (the website's own names for its settings); **File → Open
look…**, or dropping the file on the window, brings it back. **Copy share link** (in Export and
the Edit menu) copies a link that opens the same look on img.b0r3d.org; a share link made on the
website, pasted here with Ctrl+V, opens its look on the PC. The links are the website's very
own, letter for letter. A link with a screen effect, the Signal or an analog look says so: those
come with later versions.

**View → Look** follows Windows' light or dark setting, or stays light or dark.

## Your pictures stay yours

- b0r3d DeRes never goes online. It has no network code at all, and a test fails the build if
  any shows up. The links in Help open your browser only when you click them; a share link is
  only copied, as text.
- Pictures are never kept: no list of recent files, and the settings file never holds a
  picture or its name. Windows' own Open and Save dialogs are told not to add files to
  Windows' Recent items.
- Copied pictures and links are kept off Windows' cloud clipboard.

## Portable

Everything b0r3d DeRes keeps is in one folder, `b0r3dDeRes data` next to the exe: its settings
(`b0r3dDeRes.ini`) and the `b0r3dDeRes drag` folder, where a picture you drag out waits as a PNG
for the program it's dropped on until b0r3d DeRes closes (or starts again, after a crash), then
goes. Nothing goes in AppData, the registry or Temp. To keep the settings somewhere else, start it with
`b0r3dDeRes.exe --data "D:\Somewhere"`. If its folder can't be written to (a zip, a CD, a
download folder inside AppData), it still works and says settings won't be kept.

## The same pictures as the website

The engine is the website's own, translated from its TypeScript into C# file by file, and the
tests check it against the website's engine itself: 645 pictures (every machine with every
dither that changes it, the framings, custom widths, Detail, the adjustments, see-through parts
kept and laid on white and black, the Colours slider and loaded palettes) come out the same,
pixel for pixel, with the same tile meter and the same machine files, and 243 PNGs hold the same
palette, see-through entries and pixel rows. Where .NET and browsers work out a sum differently
in its last bit, the engine has copies of the routines: cube roots are the browser's own
(fdlibm's), and powers fdlibm's, which give every value the website keeps (as floats) exactly.
Share links and look files write and read numbers exactly as JavaScript does. The test files
are made from the website at the version named at the top of `tests/WebsiteCases.cs`.

The tests start from the decoded picture. Windows reads the sample photos to the same pixels
as Chromium, and an sRGB colour profile leaves them as they are, as in browsers. Two things can
still differ from a browser by a level here and there: a photo over 2048 pixels, which the
website shrinks first and b0r3d DeRes uses whole (a little more exact; the grabber takes the
whole photo's colours too), and the part-see-through edges of a picture, which browsers keep
premultiplied. One more on purpose: a Game Boy Color picture too small for one 8×8 tile stops
the website with an error; here it comes out black.

## Building it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) to build it (`global.json`
takes any 10.0 one); the program itself runs on .NET Framework 4.8, which comes with Windows 10
and 11. Double-click
`build.cmd`, or run `build.cmd test` to build and run the tests. The exe lands in
`bin\b0r3dDeRes.exe`. The tests print a PASS or FAIL line each;
`bin\tests\b0r3dDeResTests.exe --shots <folder>` draws the windows to PNG files.

## Licence

MIT with the Commons Clause, licensor b0r3d.org: free to use, change and share, not to sell.
See [LICENSE](LICENSE). The cube root and power routines come from fdlibm, through the V8
JavaScript engine; their notices are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) and
in Help → About. The licence covers the code, not the b0r3d name or the cup.

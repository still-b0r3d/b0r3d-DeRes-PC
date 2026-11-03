# b0r3d DeRes for Windows

**b0r3d DeRes** turns photos into the pictures of old computers and consoles: their
resolution, their colours, their dithers. It's the Windows program of
[img.b0r3d.org](https://img.b0r3d.org), made by [b0r3d.org](https://b0r3d.org), and it makes
the same pictures, pixel for pixel (photos over 2048 pixels aside: see below). Your pictures
stay on your PC: it never goes online, and it never keeps them.

It's one small exe that runs on 64-bit Windows 10 and 11 with nothing to install.

**Being built.** This is version 0.5, the fifth of nine stages: the program itself, opening
pictures, every machine and analog look of the website, Resolution, Adjust, the dithers, the
Signal, the screen effects, the preview, saving, copying and dragging out PNGs, wallpapers,
animated GIFs playing, GIF and APNG loops, batches, DeRes the screen, the machines' own files for
emulators, palette files, look files and share links. Video comes next, then the ASCII / ANSI
studio and the EXIF tools.

## Make a picture

Put `b0r3dDeRes.exe` in a folder of its own and double-click it. It opens with the Rukus
sample on the Game Boy, as the website does. Open a picture of your own with **File → Open
picture…** (Ctrl+O), drop one anywhere on the window, paste one (Ctrl+V), or drop it on
`b0r3dDeRes.exe` itself. PNG, JPEG, GIF (an animated one plays: see below), BMP and TIFF always
open, and WebP, HEIC, AVIF,
JPEG XL and camera RAW photos where Windows has Microsoft's free extensions for them. Photos
come out the right way up, and in their own colours when they carry a colour profile. The
whole photo is used, however big (up to 150 megapixels). **DeRes the screen** (Ctrl+Shift+D)
steps aside and lets you drag a box on the screen, click for a whole screen, or press Enter for
all of them: that becomes the picture.

The settings on the left fold out like the website's:

- **Image:** the samples, the picture's name and size, **Resolution** (the machine's own, or
  a custom width from 8 to 1024 pixels), **Framing** (crop to fill, fit with bars, or
  stretch), **Detail** (brings back the thin lines that shrinking blurs away) and, for a
  picture with see-through parts, what they sit on: white, black, or **Keep** (they stay
  see-through, over a checkerboard in the preview and in the PNG). A line under Resolution says
  what the machine's size means for your photo.
- **Hardware:** 42 machines and looks, each with its colours, and **Surprise me** for a random
  one.
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
  - The analog looks: the VHS tape, the early CCD camera (its sensor noise and date stamp), JPEG
    deep fry, newspaper halftone, the two-ink risograph and the 1-bit fax.

  Under them, the **tile meter**: how many different 8×8 tiles the picture has, against what
  the machine could hold.
- **Adjust:** brightness, contrast, saturation and gamma.
- **Dither:** none, Bayer 2×2, 4×4 and 8×8, Floyd–Steinberg, Atkinson and Yliluoma, plus the
  Game Boy Camera's and the PlayStation's own on those machines. The ones that change nothing
  on a machine are greyed out, and a line says why.
- **Signal:** the picture goes out of the machine as an analog TV signal and comes back the way
  a TV decoded it, with the colour bleed, fringes and noise: over the machine's own connection,
  RF, composite, or a VHS tape at SP, LP or EP. **Wear** goes from a clean signal to twice the
  connection's usual noise; **Advanced** has the colour, the sharpening and the TV's decoder
  (simple, or a comb filter, which cleans up the fringes and the colours that only exist in the
  signal). Each machine goes out at its own pixel clock, and those with 228 colour cycles a line
  keep their fringes still; CGA composite's colours are made by the signal itself, and its
  picture is dithered to them. It's ntsc-rs, the engine the website runs, with every frame the
  same as the website's.
- **Screen:** what the machine's screen made of it: a CRT (no mask, a shadow mask or an aperture
  grille, with scanlines, curve and bloom), an LCD (its pixel grid and ghosting), a VHS tape
  (tracking jitter and noise, and the on-screen display), composite, and the green, amber and
  white phosphors. The preview makes it at the zoom shown, every screen pixel its own, on every
  core. Picking a machine while a screen is on brings that machine's own.
- **Export:** the size (1× to 16×), for machines whose pixels weren't square raw pixels or as
  on a TV, **With the screen effect** (from 3× up), the **Animation loop** for GIF and APNG, and
  the **wallpapers**: the website's phone, b0r3d 95 and Full HD, and one for each of your screens
  at its real size.

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
past 256 of them. With the screen effect it's full colour, named `-fx`; through the signal it's
full colour too, named for the connection (`photo-nes-256x240-4x-rf.png`). Parts kept
see-through stay see-through. **Edit → Copy picture** (Ctrl+C)
copies it, and the small picture under the preview drags it into a folder or another program,
as a PNG and as a plain picture; those two go at most 16 megapixels, the website's own limit,
so a bigger Size goes at the biggest one within it, and the bottom line says so.

Machines with a file of their own save it too, byte for byte as the website makes it, for
emulators and the old paint programs: the ZX Spectrum's `.scr`, the C64 multicolour's Koala
`.koa`, and the tiles (`.chr`) of the Game Boys and the NES with attributes. **File → Save
palette…** keeps the picture's colours as a `.hex`, `.pal`, `.gpl` or palette picture.

A wallpaper (Export, or **File → Save wallpaper**) is the picture enlarged by the biggest whole
step that fits, centred on its first colour, through the signal when it's on and with the
screen effect from 3× up.

## GIFs and loops

An animated GIF opens through DeRes's own GIF decoder, as on the website, at its own size (up to
128 megapixels of frames in all; past that it's shrunk to fit). **Try a GIF** opens the GIF
sample, Rukus breaking out. A play bar comes under the picture: **Play** / **Pause**, the frame
slider and its counter. Every frame goes through all the settings, the Signal (its noise moves
at the GIF's own speed) and the screen effect; once each frame has been made it's kept, so it
loops at the GIF's speed. Paused, the frame on screen is the picture: Compare, Save PNG, copies,
wallpapers and the machine's own file take it, and a name says which frame
(`photo-frame12-nes-256x240-4x.png`).

**Save GIF…** and **Save APNG…** (under Export, and in the File menu) make an animation, as the
website does, of what **Animation loop** says: an animated GIF's own frames (its delays and loop
kept), the **Screen FX** loop (the CRT's beam, the VHS tape's tracking, the composite's crawl; with
the signal on, the signal's own moving noise, 16, 32 or 48 frames at 15 or 30 a second), the
**A/B Wipe** between the original and the picture, or the dither **Shimmer**. Full-colour GIFs get
one shared palette, so still parts don't shimmer. A frame can be 4096 × 4096, and 128 megapixels
of frames can be held at once (the website: 1024 × 1024 and 16); an animated GIF's own frames past
that are made twice instead, so any number fit. When the Size chosen is too big, they come out at
the biggest that fits and a line by the buttons says so. Frames are made on every core, the
bottom line counts them, and **Stop** leaves no file.

## Batches

**File → Batch** (Ctrl+B), or a folder or several pictures dropped on the window, runs them all
through the look you have (the signal too) into a folder: a PNG each (at the Size, Pixel shape
and screen effect set under Export), a wallpaper each, or a GIF or an APNG each (an animated GIF
as its own frames, any other picture as the loop picked under Export), and the machine's own file
if you ask. A folder gives its
own pictures, not those in the folders inside it, nor the ones OneDrive keeps only online. You
choose once what happens to a file that's already there: numbered, replaced or skipped. Several
pictures are made at once, on every core; every file is written whole and then read back from
the disk; a picture that won't open is listed with why. **Stop**, or closing b0r3d DeRes,
finishes the pictures under way first.

## Looks and share links

A look is every setting that makes the picture, without the picture. **File → Save look…**
keeps it in a small `.json` file (the website's own names for its settings); **File → Open
look…**, or dropping the file on the window, brings it back. **Copy share link** (in Export and
the Edit menu) copies a link that opens the same look on img.b0r3d.org; a share link made on the
website, pasted here with Ctrl+V, opens its look on the PC. The links are the website's very
own, letter for letter, screen effect and signal and all (the signal loop's length and speed
too). A look file also keeps the Animation loop picked.

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
tests check it against the website's engine itself: 797 pictures (every machine and analog look
with every dither that changes it, its pattern started elsewhere as the shimmer does, the
framings, custom widths, Detail, the adjustments,
see-through parts kept and laid on white and black, the Colours slider and loaded palettes) come
out the same, pixel for pixel, with the same tile meter and the same machine files; 253 PNGs
hold the same palette, see-through entries and pixel rows; 215 screen effects at many sizes and
16 wallpapers come out the same pixel for pixel. Where .NET and JavaScript work out a sum
differently in its last bit, the engine has copies of the routines: cube roots, sines, cosines,
exponentials and powers are fdlibm's, as V8 has them in Node (where the test files are made),
which give every value the website keeps (as floats) exactly. Today's Chrome rounds some sines,
cosines, exponentials and cube roots differently in a double's last bit; once kept as floats,
that hasn't changed a pixel in any case checked.
The Signal is ntsc-rs, which the website runs as WebAssembly (built from Rust), translated
into C#: 104 frames (every machine as it went out, every connection, the wear, colour,
sharpening and decoder, later frames and huge frame numbers, interlaced, see-through and tiny
pictures), 13 PNGs and wallpapers through it, CGA composite's colours and the engine's
settings files come out the same as the website's, byte for byte. That holds on any PC: like
the WebAssembly, C# here does every multiply and add on its own, rounded each time (no
processor's fused shortcuts), and the signal's sines, powers and the like are copies of the
routines Rust's maths library (libm) has. The self-test checks one of those frames on the PC
it runs on.
GIFs and loops: hand-made GIFs for every rule of the decoder (and the GIF sample) read as the
website reads them, frame by frame; the GIF and APNG writers make the website's files (GIFs
byte for byte, APNGs chunk for chunk, as with PNGs); and every loop style, with the screen
effect, the signal and see-through pixels, and animated GIFs' own frames through many machines,
come out as the website's own files. The self-test makes one of those GIFs on the PC it runs on.
Share links and look files write and read numbers exactly as JavaScript does. The test files
are made from the website at the version named at the top of `tests/WebsiteCases.cs`.

The tests start from the decoded picture. Windows reads the sample photos to the same pixels
as Chromium, and an sRGB colour profile leaves them as they are, as in browsers. Two things can
still differ from a browser by a level here and there: a photo over 2048 pixels, which the
website shrinks first and b0r3d DeRes uses whole (a little more exact; the grabber takes the
whole photo's colours too), and the part-see-through edges of a picture, which browsers keep
premultiplied. Two more on purpose: a Game Boy Color picture too small for one 8×8 tile stops
the website with an error; here it comes out black. And a GIF or APNG with a side past 65,535
pixels, which the website writes as a broken GIF, is too big here: it comes out at a smaller
Size.

## Building it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) to build it (`global.json`
takes any 10.0 one); the program itself runs on .NET Framework 4.8, which comes with Windows 10
and 11. Double-click
`build.cmd`, or run `build.cmd test` to build and run the tests. The exe lands in
`bin\b0r3dDeRes.exe`. The tests print a PASS or FAIL line each, and a run stops itself after 90
seconds; `build.cmd test` runs them in five parts (`--part engine`, `animation`, `gifs`,
`frames`, then `window`) so each stays within that on a two-core machine too.
`bin\tests\b0r3dDeResTests.exe --shots <folder>` draws the windows to PNG files.

## Licence

MIT with the Commons Clause, licensor b0r3d.org: free to use, change and share, not to sell.
See [LICENSE](LICENSE). The cube root, power, sine, cosine and exponential routines come from
fdlibm, through the V8 JavaScript engine; the Signal is ntsc-rs by valadaptive (MIT), with
maths routines from Rust's libm (MIT, from musl and FreeBSD); their notices are in
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) and in Help → About. The licence covers the
code, not the b0r3d name or the cup.

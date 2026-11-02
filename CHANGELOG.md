# Changelog

## 0.2 (not released yet)

The second stage: every machine of the website but its analog looks, and looks that travel.

- 20 more machines, the same pictures as the website's pixel for pixel: the Game Boy Camera
  (its sharpening and its own dither), the machines that pick their colours from the picture
  (Super Nintendo, Sega Genesis, Master System, PC-9801, VGA 256, Atari ST, the Amstrad CPC in
  modes 0 and 1, and Custom), the ones that pick them cell by cell (ZX Spectrum, C64
  multicolour, NES attributes, Apple II hi-res, MSX Screen 2, Game Boy Color), Amiga HAM6, the
  PlayStation with its GPU's dither, the Nintendo 64's filter, and CGA composite. 36 in all.
- The Colours slider for the machines that pick their colours, and Custom's palette: loaded
  from a `.hex`, `.pal` or `.gpl` (opened, or dropped on the window) or a palette picture
  (opened), or grabbed from the picture; File → Save palette… keeps a picture's colours in any
  of the four.
- Transparent areas: Keep. They stay see-through, over a checkerboard in the preview, and in
  the PNG (one more palette entry, or alpha).
- The tile meter: how many different 8×8 tiles, against what the machine could hold, under the
  machines and in the line under the picture.
- The dithers each machine offers: the machines' own on them only, the ones that change
  nothing greyed out, and a line saying why.
- The machines' own files, byte for byte as the website makes them: the Spectrum's `.scr`, the
  C64's Koala `.koa`, Game Boy and NES tiles (`.chr`).
- Looks: File → Save look… and Open look… (`.json`, the website's own names), a look file
  dropped on the window, Copy share link, and a website share link pasted with Ctrl+V. Links
  are the website's own, letter for letter, and read as it reads them; what comes with a
  later version (a screen effect, the Signal, an analog look) is said.
- Surprise me, and Reset settings (asks first).
- PNGs of the looks that make their own colours hold exactly those colours, or go full colour.
- The line under the picture takes two lines when it needs them.
- 65 tests: 645 pictures and 243 PNGs against the website's own engine, its links, the numbers
  in them, palette files and the grabber.

## 0.1 (not released yet)

The first stage: the program, and the machines with fixed palettes.

- The window: menus with access keys and shortcuts, the b0r3d cup, light, dark or like
  Windows (View → Look), sharp at any screen scaling, and Help → About with the version,
  the build, the settings folder and the licences.
- Opening pictures: File → Open picture…, a drop anywhere on the window or on the exe,
  Ctrl+V, and the samples inside the exe (Rukus, the crystal forest and the cat nap). PNG,
  JPEG, GIF (its first picture), BMP and TIFF, plus WebP, HEIC, AVIF, JPEG XL and camera RAW
  where Windows has Microsoft's extensions. The whole photo, the right way up, in its own
  colours when it has a colour profile (sRGB ones leave the pixels as they are, as browsers
  do). Up to 150 megapixels; a 48 MP photo opens in a fraction of a second.
- The website's engine in C#, the same pictures pixel for pixel: the 16 machines with fixed
  palettes (Game Boy to b0r3d, the Nokia 3310 and the Virtual Boy), Resolution (native or a
  custom width), Framing, Detail, see-through parts on white or black, Adjust and the seven
  dithers. Checked against the website's own engine (v0.9.4) on 271 pictures and 111 PNGs.
- The preview at the screen's own pixels, every machine pixel a sharp block of whole screen
  pixels: Fit, zoom in whole steps, drag to move around, hold for the original or Compare
  with a divider, full screen, and the website's line under it.
- Save PNG at 1× to 16×, raw pixels or as on a TV, as the website's indexed PNG and named as
  it names them, any size; copy the picture, or drag it out into a folder or another program,
  as a PNG and a plain picture of at most 16 megapixels (the biggest Size within that).
- Settings in `b0r3dDeRes.ini` in the `b0r3dDeRes data` folder next to the exe (or `--data`);
  nothing else written there but a dragged-out PNG, which goes when the program closes. Pictures
  never kept, and it never goes online.
- `build.cmd`, 49 tests, and CI that drafts a release from a `v*` tag.

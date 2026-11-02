# Changelog

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

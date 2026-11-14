# Changelog

What changed in each version of b0r3d DeRes for Windows, the newest first. The versions before
1.0 are its stages, built one after the other; 0.8 is the first one released.

## 1.0 (released 8 November 2026)

The first full release, with the live camera, matching img.b0r3d.org v1.0.

- **Use camera**, under Open picture or video… (and File → Use the camera): the preview becomes
  a live view through the look, as on the website, with every control working on it. Each of the
  camera's pictures is grabbed small (about 3 camera pixels for every machine pixel, at most 960
  across, 1280 for wide custom sizes), made through the look, and shown; pictures that come
  meanwhile are skipped, so a slow look never queues up. The line under the picture adds the frames
  a second, and a look slower than 8 a second for 2 seconds says so. Screen effects and the
  Signal are drawn smaller while live (at most 300,000 pixels, never below 2×).
  - **Snap**: the camera's picture as it's shown becomes the picture, as if it had been opened.
  - **Record 3 s**: 30 pictures, ten a second, open like an animated GIF (the play bar, every
    look, GIF, APNG and the ASCII studio's animations). The camera's own pictures are kept, so
    the look can change afterwards; held as the PC holds an animated GIF's frames, so a camera's
    pictures are usually kept whole.
  - **Flip** (with two cameras or more: the next one), **Mirror** (on at first: a PC's camera
    faces you, as a phone's selfie camera does), **Stop**. The camera used last and Mirror are
    kept; Reset settings forgets them.
  - The camera turns off on Snap, Stop, a finished clip, another tab, opening anything, DeRes the
    screen, and the window minimized or closed. Saving waits for a snap or a clip. Its pictures
    stay in memory, and nothing leaves the PC.
  - Through Windows' own Media Foundation (b0r3d QR's camera code): of the kinds of picture a
    camera sends, the nearest to what the website asks a browser for (1920 × 1440), at 15 a
    second or more; turned into colour by Windows' advanced video processor, as videos are. When
    Windows won't let b0r3d DeRes use the camera, it says where to allow it; under any trouble's
    words goes Windows' own side of it (b0r3d QR's way).
- The website's three fixes, so two deliberate differences go: a Game Boy Color picture with no
  whole 8×8 tile is black on both now, a GIF or APNG never has a side past 65,535 px on either,
  and the fax header's slash leans the right way (here too). The tests' website files are made
  again from it.
- The release step refuses a mistyped tag such as `vv0.9` (it took off every leading v before).
- The self-test also runs the live camera on a pretend camera and snaps it (`camera=ok`), and
  counts the cameras Windows lists without starting any (`cameras_seen=`).

## 0.8 (released 6 November 2026)

The eighth stage: the EXIF tools.

- Two more tabs, as on the website: **EXIF Scrubber** and **EXIF Injector**, with the website's
  sections, and its inspector on the right of either. It reads the file open (the same one as on
  the other tabs) as the website reads it, field for field and tag for tag: the picture, the
  website's line about it, the place it was taken (with **Copy coordinates**, kept off Windows'
  cloud clipboard and its history), an embedded thumbnail (shown: it may be the uncropped
  original), bytes after the picture (a motion photo's video, Samsung's trailer, a gain map), AI
  markers and Content Credentials, and the Summary or **All Tags Tree** with its filter. JPEG, PNG,
  WebP and GIF are cleaned; HEIC and AVIF are read (their pictures too where Windows has the
  extensions).
- The Scrubber: remove all metadata or the location only, keep the orientation tag or not.
  **Scrub & save a clean copy…** saves a new file, never over the original, and checks it as
  it's read back from the disk: **Verified Clean**, or what's left. The clean copies are the
  website's byte for byte.
- A batch of any number of photos and videos, and whole folders (their own files, not those in
  folders inside, nor ones OneDrive keeps only online): added with the buttons, File → Add…,
  Ctrl+B, or dropped on the tab. Its table says each file's format, size, GPS and trailer, and
  how cleaning it went; any row can be inspected (and edited in the Injector). It's cleaned into
  a ZIP (the website's very ZIP, names and all) or into a folder (photo-clean.jpg, numbered:
  nothing there is written over), every file checked. A file that can't be cleaned is said, on
  its row and in the words at the end. **Stop** keeps what's cleaned and checked and leaves
  nothing half-written or unchecked; a stopped ZIP leaves no ZIP.
- Videos (the PC's own): MP4 and MOV from phones (Apple, Android, Samsung, GoPro) and ffmpeg.
  Where they were filmed and on what (QuickTime's keys, user data, 3GPP's place, XMP, timed
  metadata tracks, Content Credentials, bytes after the video) is shown and taken out byte by
  byte, or the location only: the picture and sound are left untouched (checked byte for byte;
  Windows plays the very same frames and sound), still turned the way the phone held it.
  Removing all keeps only the picture and sound: every other track goes (metadata, a drone's
  captions, the timecode), with the names and makers in the index and every byte of the picture
  data no track uses (a dashcam's GPS between frames). A place written anywhere else (an unknown
  box, padding) goes in location-only mode too, and the check reads the copy on its own for any
  place left outside the picture and sound. Old QuickTime sound (PCM) is sized as players size
  it. A video it can't be sure of is refused, with why: two indexes, made in pieces
  (fragmented), its picture kept in another file, or damaged.
- The Injector: the form starts with what the photo has, with the website's fields, checks and
  messages (a field with a problem is marked), its device presets (filled in the way that device
  writes them; injecting then drops the old camera's serial numbers and maker data), Reset to
  photo, b0r3d Defaults (this program as the software), Clear fields, Now, the places, and the
  XMP block taken out when it would contradict the new values. Only the fields changed are
  written, into a new copy, the website's very file; the edited photo is then the one shown. It
  writes into JPEG and PNG; a GIF, a HEIC or a video says why not.
- The File, Edit and View menus follow the tab: Ctrl+S saves the tab's copy, Ctrl+C copies the
  coordinates, Reset settings puts back the Scrubber's options (kept with the other settings).
- An original is never written over, under any spelling of its path (a network drive's, a
  junction's, a hard link's), nor by a batch's ZIP; a save is for the file it was started for (one
  opened while the Save dialog was open isn't saved by mistake).
- Saving over a file that's there (the Save dialog asks first) no longer carries the old file's
  hidden streams onto the new one, a download's web address among them. This is every save in
  the program.
- Reset settings now puts a video's frame rate and Keep the sound back too, on the DeRes tab and
  in the studio (the two share them).
- Alt+B opens the EXIF Scrubber and Alt+J the Injector; the Hardware section's Grab palette
  button has no access key now (the Edit menu's Grab palette has one).
- The self-test reads the Ruckus sample's EXIF, cleans it and writes into it: the website's very
  files (`exif=ok`).
- The tests run in eleven parts (`exif` is new); `--shots <folder> exif` draws the two tabs.
- The README shows the program in pictures. The licence notices (THIRD-PARTY-NOTICES.txt, and
  Help → About) now also have Unicode's (the case mappings the EXIF tools follow), SciPy's (the
  Signal's filters, through ntsc-rs) and Clatter's zlib licence in full. LICENSE says the
  third-party parts stay under their own licences, without the Commons Clause.

## 0.7 (not released yet)

The seventh stage: the ASCII / ANSI studio.

- A second tab, ASCII / ANSI Studio, with the website's sections: Image, Mode & Columns,
  Character Ramp, Teletext Options, Colour Profile, Adjustments, and Export & Clipboard. It
  takes the same picture as the DeRes tab, on the same frame of a GIF or a video.
- Every mode: half blocks, character ramps (Standard, Detailed, Blocks, Katakana, Binary, or
  your own, inverted too), PETSCII on a C64's 40 × 25 screen, and Teletext mosaics with the
  Ceefax header and separated mosaics; 40, 80 or 132 columns or 20 to 200 in steps of 2, 2:1
  cells or square; every colour profile (ANSI 16, C64, Teletext, green, amber, mono,
  truecolour); Contrast, Brightness and Gamma. The same art as the website's, character for
  character and colour for colour (162 cases checked against its engine), its readout in the
  website's words.
- The art shows as its picture, from the VGA font's own 8 × 16 letters (the website's font file,
  inside the exe), with zoom, full screen and the website's scanlines; the dial-up modem types it
  out at 300 to 28.8k baud, a cursor blinking where the next character goes.
- Copy as plain text, Markdown, ANSI escapes or HTML (Ctrl+C on the tab: the text). Save .ANS
  with its SAUCE record, PETSCII as a C64 .SEQ stream or a .PRG that shows it on a C64, Teletext
  as a .TXT, and the picture as a PNG at 1×, 2× or 4×, named as the website names them. The
  text files are the website's byte for byte; the pictures of half blocks and Teletext are its
  pixel for pixel, while a ramp's or PETSCII's are drawn from the font's own letters, crisp,
  where the browser smooths them.
- An animated GIF plays as art (its play bar under the art); its every frame goes out as an
  ANSImation, a GIF or an APNG (exactly the art's colours when there are 256 or fewer): the
  website's very ANSImation, and its very GIF and APNG of half blocks and Teletext. A video
  plays as art on its timeline, and its every frame from its start to its stop goes out as an
  ANSImation, a GIF, an APNG, or an MP4 with its sound in step, at its own frame rate or a
  slower one. Frames are made on several cores, with the time left and Stop.
- See-through pictures are read as the website's canvas holds them, so their art is the website's
  too; Teletext's pictures are drawn as Chromium's canvas draws them, pixel for pixel.
- The File, Edit and View menus follow the tab: the studio's saves, copies and scanlines show on
  its tab; Save PNG, GIF, APNG and MP4, Copy, the zoom, full screen and Play act on it. Reset
  settings (the button, or the Edit menu) puts back the studio's own settings only.
- The studio's settings are kept with the others (a typed-in ramp too, any character, and the
  PNG's size). A ramp pasted with line breaks comes in on one line, as the website's box takes it.
- The Hardware section's Surprise me button's access key is now M (U is the studio's tab).
- The self-test makes the website's art of the Ruckus sample, its Teletext picture and the font's
  every letter (`ascii=ok`).
- The tests run in ten parts (`ascii` is new); `--shots <folder>` takes a set: `light`, `dark`
  or `ascii`.
- Opening a video, scrubbing its timeline, a jump while it plays and a save from a later start no
  longer stick now and then: once a reader has read frames, a seek makes Windows' reader again at
  the new time. Sent back or on in place, Windows' reader sometimes never came back from its next
  read (seen on GitHub's 2-core runners in about one test run in seven, and on this PC on two
  cores, more rarely).
- When a test is stopped by its watchdog, it names the Windows video calls that hadn't come back.

## 0.6 (not released yet)

The sixth stage: video.

- Videos open as pictures do (File → Open picture or video…, a drop, the command line): MP4,
  M4V, MOV, MKV, WebM, AVI, WMV, ASF, MPEG, TS, MTS, M2TS and 3GP, when Windows has a decoder
  for what's in them, through Windows' own video system, the right way up as the phone took
  them. An iPhone's HDR video (HLG) comes out
  in normal colour, converted by DeRes itself; other HDR through Windows' own conversion.
- The timeline under the picture: Play / Pause with the sound, a frame back and on, the slider
  to any moment, Start here, Stop here and All of it. Every frame goes through the settings, the
  screen effect and the Signal (its noise at the video's own speed). Paused, the frame on screen
  is the picture: every control works on it, and Compare, saves, copies and wallpapers take it.
  A video that reads slower than it plays (4K HDR on a modest PC) still moves, leaving frames
  out to keep up with its sound; one whose sound stops first plays on to its end.
- Save MP4… (Export, and the File menu): the stretch through the look as H.264 and AAC, with its
  sound (Keep the sound), at the Size chosen up to 4096 × 2304 and the video's own frame rate or
  a slower one (30, 25, 24, 15, 12, 10). It plays in Windows' Media Player, browsers and phones,
  the sound in step: Windows passes over an MP4's edit lists, which phones and ffmpeg write, so
  DeRes reads them itself.
- GIF and APNG of a video's frames (Animation loop: Video), at the Size or the biggest that
  fits, a GIF at 50 frames a second at most and an APNG at 60 (faster, browsers slow them down:
  60 becomes 30 for a GIF), and Save PNG frames… (`clip-nes-rf-00001.png` on). Frames are made
  on several cores, the bottom line says how far it's got and the time left, and Stop leaves no
  file (Save PNG frames… keeps the frames made so far).
- Batches take videos: an MP4 each with its sound, at the Size or the biggest an MP4 holds, or a
  GIF or APNG of its frames; each read back from the disk, an MP4 frame by frame.
- The Signal's engine, all of it: Every setting… (Signal → Advanced) has every one of ntsc-rs's
  settings for the picture on screen, changed on top of the connection's own until Reset all;
  ntsc-rs's settings files save there, and load as what they have otherwise than the connection
  (the scale, which fits the engine to each picture, stays DeRes's). 69 frames made with them
  changed (every field mode too) come out as the website's own engine makes them from the same
  settings, byte for byte.
- Dub it: the tape copied up to nine times, each copy through it all again.
- Presets: nine built in and your own (Save as preset…), kept in the settings folder as
  `b0r3dDeRes presets.json`. What that file holds that this version can't read stays in it, and
  a file that can't be read at all is never written over (the list says so).
- Share links, look files and the settings file keep the engine settings changed and the copies
  (the website's own links and files have none).
- A Windows without its video parts (the "N" editions) says what to install; pictures work there
  all the same.
- The Signal's line: "The noise moves when a GIF or a video plays, and in the animations and
  videos under Export."
- The self-test writes an MP4 beside its report and reads it back. The tests run in nine parts.

## 0.5 (not released yet)

The fifth stage: GIFs and loops.

- Animated GIFs open through DeRes's own GIF decoder, as the website's do: every frame as it
  shows (frames that keep, clear or put back what was under them, interlaced ones, frames off
  the screen's edge, damaged files up to the damage), at the GIF's own size (up to 128 MP of
  frames in all; past that they're shrunk to fit, as on the website, which keeps 16 MP).
- The play bar under the picture: Play / Pause, the frame slider and its counter. Each frame
  goes through every setting and the Signal (the noise moves at the GIF's own speed) and the
  screen effect (its moving parts too), made a little ahead and kept while the settings stay
  the same, so it loops at the GIF's speed. Paused, the frame on screen is the picture: Compare,
  saves, copies, wallpapers and the machine's own file take it, named `-frame12`.
- GIF and APNG (Export, and File → Save GIF… and Save APNG…): the GIF's own frames, the Screen FX
  loop, the A/B wipe or the dither shimmer, as the website makes them; with the signal on, the
  Screen FX loop is the signal's own moving noise, 16, 32 or 48 frames at 15 or 30 a second.
  Full-colour GIFs get the website's one shared palette. The same files as the website's: GIFs
  byte for byte, APNGs chunk for chunk.
- Bigger than the website's: a frame up to 4096 × 4096, and 128 MP of frames held at once (a
  48-frame signal loop up to 6× on the NES); an animated GIF's own frames past that are made
  twice instead of held, so any number fit. A line by the buttons says when they come out at a
  smaller Size than the one chosen, and why. Frames are made on every core; Stop leaves no file.
  No side past 65,535 pixels (the most a GIF can say; the website writes a broken GIF there).
- A GIF shrunk to be held still goes by its own size, as on the website: Custom's height, Detail
  and the resolution line.
- Batches can make a GIF or an APNG each: an animated GIF as its own frames, any other picture
  as the loop picked under Export, each made straight into its file (however big) and read back
  through to its end.
- The GIF sample, Ruckus breaking out, is inside the exe ("Try a GIF").
- The Signal's line: "The noise moves when a GIF plays, and in the animations under Export."
- The settings file and look files keep the loop picked (the GIF's own frames are picked again
  for every GIF opened).
- The self-test makes the website's very GIF of the GIF sample's frames on the Game Boy.
- The tests run in five parts on a two-core machine, each within its 90 seconds.

## 0.4 (not released yet)

The fourth stage: the Signal.

- The Signal: the picture goes out of the machine as an analog TV signal and comes back the
  way a TV decoded it, with the colour bleed, fringes and noise. Off, the machine's own
  connection, RF, composite, or a VHS tape at SP, LP or EP; Wear (none of the noise to twice
  the connection's own), and under Advanced the colour, the sharpening and the TV's decoder
  (simple, or a comb filter).
- It's ntsc-rs, the engine the website runs as WebAssembly, translated into C# with the maths
  routines it uses (Rust's libm): every frame comes out the same as the website's, byte for
  byte, on any PC (no processor's shortcuts change a sum).
- Each machine as it went out: its pixel clock (how many samples of the colour carrier a pixel
  lasts), and for the machines with 228 colour cycles a line, fringes that stand still. CGA
  composite's colours come from the signal itself: its picture is dithered to the colours the
  signal makes of its patterns.
- Pictures over 288 lines are interlaced, as the website's are.
- The preview shows it at the zoom shown, with the screen effect over it when one is on, and
  follows a slider while it moves (it used to wait for the slider to stop when the slider made
  the picture again: the screen effect's too). PNGs, copies, dragged-out pictures, wallpapers
  and batches keep it, full colour; a PNG's name says the connection
  (`photo-nes-256x240-4x-rf.png`), and a wallpaper's doesn't, as on the website.
- The Signal's and the Screen's Off buttons are named for Voice Access with the words on them.
- Share links and look files carry the signal, and the loop's length and speed, as the
  website's do; the settings file keeps them.
- The self-test makes the website's very frame of the Ruckus sample over RF.
- 79 tests: 104 signal frames, 13 PNGs and wallpapers through it, 40 of the website's engine
  settings files and CGA composite's colours against the website's own engine, as well as
  761 pictures, 253 PNGs, 215 screen effects and 16 wallpapers.

## 0.3 (not released yet)

The third stage: the screens, the analog looks, wallpapers and batches.

- The screen effects, the same pixels as the website's: the CRT (no mask, a shadow mask or an
  aperture grille; scanlines, curve and bloom), the LCD (its pixel grid and ghosting), the VHS
  tape (tracking jitter and noise, and the PLAY ▶ on-screen display), composite, and the P1
  green, P3 amber and P4 white phosphors. In the preview they're made at the zoom shown, every
  screen pixel its own, on every core; full screen on a 4K TV too.
- Picking a machine while a screen is on brings that machine's screen; the VHS tape always
  brings its own. Surprise me rolls a screen half the time, as the website's does.
- Export → With the screen effect: PNGs from 3× up keep it (full colour, named `-fx`), and so do
  copies and dragged-out pictures.
- The analog looks: the VHS tape, the early CCD camera (sensor noise and the date stamp), JPEG
  deep fry, newspaper halftone, the two-ink risograph and the 1-bit fax. 42 machines in all.
- Wallpapers (Export, and File → Save wallpaper): the website's phone, b0r3d 95 and Full HD,
  and one for each of your screens at its real size. They keep the screen effect from 3× up.
- Batch (File → Batch, Ctrl+B, or a folder or several pictures dropped on the window): a folder
  or a pile of pictures through the look into a folder, a PNG or a wallpaper each and the
  machine's own file if asked, several at once on every core. A folder gives its own pictures
  (not those in folders inside it, nor ones kept only online by OneDrive). A file that's already
  there is numbered, replaced or skipped, as you choose once (a picture's files are numbered
  together); every file is written whole and read back from the disk; a picture that won't open
  is listed; Stop, or closing b0r3d DeRes, finishes the ones under way first.
- DeRes the screen (File, Ctrl+Shift+D): b0r3d DeRes steps aside, and a box you drag, a whole
  screen (a click) or all of them (Enter) becomes the picture.
- Share links and look files carry the screen effect and the export's box, as the website's
  do; only the Signal still comes later.
- Sines, cosines and exponentials are fdlibm's, as V8 works them out in Node, where the
  website's engine makes the test files.
- 73 tests: 746 pictures, 253 PNGs, 215 screen effects and 16 wallpapers against the website's
  own engine, and 41,146 sines, cosines and exponentials against Node's.

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
  Ctrl+V, and the samples inside the exe (Ruckus, the crystal forest and the cat nap). PNG,
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

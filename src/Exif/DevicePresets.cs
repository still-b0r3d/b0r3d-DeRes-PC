using System.Linq;

namespace B0r3dDeRes.Exif
{
    // What a few phones and cameras write about themselves (the website's device-presets.ts):
    // make, model, lens, software and a typical daylight exposure. Picking one fills those fields;
    // the date, place, creator and orientation stay the photo's. A field a device doesn't write is
    // left empty, so injecting removes the photo's own value. The Game Boy Camera never wrote EXIF
    // at all; it's here for DeRes's Game Boy pictures.
    sealed class DevicePreset
    {
        public string Id, Label, Group;
        public string Make, Model, Lens, Software, Aperture, Shutter, Iso, Focal;
        public string LensMake;      // LensMake, when the device writes one
        public double? Focal35;      // FocalLengthIn35mmFilm, when the device writes one

        public string this[string key]
        {
            get
            {
                switch (key)
                {
                    case "make": return Make;
                    case "model": return Model;
                    case "lens": return Lens;
                    case "software": return Software;
                    case "aperture": return Aperture;
                    case "shutter": return Shutter;
                    case "iso": return Iso;
                    default: return Focal;
                }
            }
        }
    }

    static class DevicePresets
    {
        public static readonly string[] FieldKeys = { "make", "model", "lens", "software", "aperture", "shutter", "iso", "focal" };

        static DevicePreset P(string id, string label, string group, string make, string model, string lens, string software, string aperture, string shutter, string iso, string focal,
            string lensMake = null, double? focal35 = null) =>
            new DevicePreset { Id = id, Label = label, Group = group, Make = make, Model = model, Lens = lens, Software = software, Aperture = aperture, Shutter = shutter, Iso = iso, Focal = focal, LensMake = lensMake, Focal35 = focal35 };

        public static readonly DevicePreset[] All =
        {
            P("iphone-15-pro", "iPhone 15 Pro (2023)", "Phones", "Apple", "iPhone 15 Pro", "iPhone 15 Pro back triple camera 6.765mm f/1.78", "17.4.1", "1.78", "1/120", "64", "6.765", "Apple", 24),
            P("pixel-8-pro", "Pixel 8 Pro (2023)", "Phones", "Google", "Pixel 8 Pro", "Pixel 8 Pro back camera 6.9mm f/1.68", "", "1.68", "1/125", "50", "6.9", "Google", 24),
            P("galaxy-s23-ultra", "Galaxy S23 Ultra (2023)", "Phones", "samsung", "Galaxy S23 Ultra", "", "", "1.7", "1/100", "50", "6.3", null, 24),
            P("iphone-4", "iPhone 4 (2010)", "Phones", "Apple", "iPhone 4", "", "4.3.5", "2.8", "1/120", "80", "3.85"),
            P("canon-5d-mark-ii", "Canon EOS 5D Mark II (2008)", "Cameras", "Canon", "Canon EOS 5D Mark II", "EF24-105mm f/4L IS USM", "", "8", "1/200", "100", "50"),
            P("canon-sd1000", "Canon PowerShot SD1000 (2007)", "Cameras", "Canon", "Canon PowerShot SD1000", "", "", "2.8", "1/60", "80", "5.8"),
            P("kodak-c613", "Kodak EasyShare C613 (2006)", "Cameras", "EASTMAN KODAK COMPANY", "KODAK EASYSHARE C613 ZOOM DIGITAL CAMERA", "", "", "2.7", "1/160", "80", "6"),
            P("coolpix-990", "Nikon Coolpix 990 (1999)", "Cameras", "NIKON", "E990", "", "E990v1.0", "2.5", "1/250", "100", "8.2"),
            P("nintendo-3ds", "Nintendo 3DS (2011)", "Handhelds", "Nintendo", "Nintendo 3DS", "", "00204", "", "", "", ""),
            P("game-boy-camera", "Game Boy Camera (1998, just for fun)", "Handhelds", "Nintendo", "Game Boy Camera", "", "", "", "", "", ""),
        };

        public static DevicePreset ById(string id) => All.FirstOrDefault(p => p.Id == id);

        // The tags a preset writes that the form doesn't show, added when injecting while the form
        // still names the preset's camera: its LensMake and 35 mm figure (or none), and nothing
        // left that names the old camera.
        public static void AddExtras(DevicePreset p, ExifEdits edits)
        {
            edits.LensMake = p.LensMake != null ? Edit<string>.To(p.LensMake) : Edit<string>.Remove;
            edits.FocalLength35 = p.Focal35.HasValue ? Edit<double>.To(p.Focal35.Value) : Edit<double>.Remove;
            edits.ForgetCamera = true;
        }

        // whether the form still shows the preset's camera (make and model as it set them)
        public static bool FormShows(DevicePreset p, InjectorFields fields) => JsText.Trim(fields["make"]) == p.Make && JsText.Trim(fields["model"]) == p.Model;

        // a preset's name without its year (label.replace(/ \(.*\)$/, ''): from the first " (")
        public static string ShortLabel(DevicePreset p)
        {
            int at = p.Label.IndexOf(" (", System.StringComparison.Ordinal);
            return at >= 0 && p.Label.EndsWith(")", System.StringComparison.Ordinal) ? p.Label.Substring(0, at) : p.Label;
        }
    }
}

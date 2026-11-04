using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace B0r3dDeRes.Engine
{
    // ntsc-rs's settings (crates/ntscrs/src/settings/standard.rs at 450c6ee), every one of them, by
    // its own names and numbers, so its settings files read and write here as they do there.
    enum UseField { Alternating, Upper, Lower, Both, InterleavedUpper, InterleavedLower }
    enum FilterType { ConstantK, Butterworth }
    enum LumaLowpass { None, Box, Notch }
    enum TapeSpeed { None, SP, LP, EP }
    enum ChromaLowpass { None, Light, Full }
    enum ChromaDemodulation { Box, Notch, OneLineComb, TwoLineComb, TwoD, TwoDAdaptive }

    enum SettingKind { Choice, Float, Percent, Int, Bool, Group }

    // One of the engine's settings: its key in a settings file, what it is, its range, and words for it.
    sealed class NtscSetting
    {
        public string Key, Label, Tip;
        public SettingKind Kind;
        public double Min, Max;
        public bool Logarithmic;
        public (string Label, int Value)[] Choices;   // the menu's order
        public NtscSetting Group;                     // the switch it sits under, or null
        public int Depth => Group == null ? 0 : Group.Depth + 1;
    }

    // A whole set of the engine's settings. Numbers are the floats the engine works with: the
    // website hands it a settings file of JavaScript numbers, which it reads to the nearest float
    // (Signal.JsonFloat) and holds to the setting's range, as here.
    sealed class NtscSettings
    {
        public int RandomSeed;
        public UseField UseField = UseField.InterleavedUpper;
        public FilterType FilterType = FilterType.Butterworth;
        public LumaLowpass InputLumaFilter = LumaLowpass.Notch;
        public ChromaLowpass ChromaLowpassIn = ChromaLowpass.Full;
        public float CompositePreemphasis = 1;
        public bool CompositeNoise = true;
        public float CompositeNoiseIntensity = 0.05f, CompositeNoiseFrequency = 0.5f;
        public int CompositeNoiseDetail = 1;
        public float Snow = 0.00025f, SnowAnisotropy = 0.5f;
        public ChromaDemodulation ChromaDemodulation = ChromaDemodulation.Notch;
        public float LumaSmear = 0.5f;
        public bool HeadSwitching = true;
        public int HeadSwitchingHeight = 8, HeadSwitchingOffset = 3;
        public float HeadSwitchingShift = 72;
        public bool HeadSwitchingMidLine = true;
        public float MidLinePosition = 0.95f, MidLineJitter = 0.03f;
        public bool TrackingNoise = true;
        public int TrackingNoiseHeight = 12;
        public float TrackingWave = 15, TrackingSnow = 0.025f, TrackingSnowAnisotropy = 0.25f, TrackingNoiseIntensity = 0.25f;
        public bool Ringing = true;
        public float RingingFrequency = 0.45f, RingingPower = 4, RingingScale = 4;
        public bool LumaNoise = true;
        public float LumaNoiseIntensity = 0.01f, LumaNoiseFrequency = 0.5f;
        public int LumaNoiseDetail = 1;
        public bool ChromaNoise = true;
        public float ChromaNoiseIntensity = 0.1f, ChromaNoiseFrequency = 0.05f;
        public int ChromaNoiseDetail = 2;
        public float ChromaPhaseError, ChromaPhaseNoise = 0.001f, ChromaDelayHorizontal;
        public int ChromaDelayVertical;
        public bool Vhs = true;
        public TapeSpeed TapeSpeed = TapeSpeed.LP;
        public float ChromaLoss = 0.000025f;
        public bool Sharpen = true;
        public float SharpenIntensity = 0.25f, SharpenFrequency = 1;
        public bool EdgeWave = true;
        public float EdgeWaveIntensity = 0.5f, EdgeWaveSpeed = 4, EdgeWaveFrequency = 0.05f;
        public int EdgeWaveDetail = 2;
        public bool ChromaVertBlend;
        public ChromaLowpass ChromaLowpassOut = ChromaLowpass.Full;
        public bool Scale = true;
        public float HorizontalScale = 1, VerticalScale = 1;
        public bool ScaleWithVideoSize;

        public NtscSettings Clone() => (NtscSettings)MemberwiseClone();

        // ---- the settings, in the order of the engine's own list (its settings files' order)

        static NtscSetting S(string key, SettingKind kind, string label, string tip, double min = 0, double max = 1, bool log = false, params (string, int)[] choices) =>
            new NtscSetting { Key = key, Kind = kind, Label = label, Tip = tip, Min = min, Max = max, Logarithmic = log, Choices = choices };

        public static readonly NtscSetting[] All = Build();

        static NtscSetting[] Build()
        {
            var list = new List<NtscSetting>();
            NtscSetting group = null;
            void Add(NtscSetting s)
            {
                s.Group = group;
                list.Add(s);
            }
            void Group(NtscSetting g, Action inside)
            {
                Add(g);
                NtscSetting outer = group;
                group = g;
                inside();
                group = outer;
            }
            Add(S("random_seed", SettingKind.Int, "Noise seed", "Picks the noise: the same seed and frame always give the same picture.", int.MinValue, int.MaxValue));
            Add(S("use_field", SettingKind.Choice, "Fields", "Which lines of the picture go out: every other line alternating (a real interlaced picture), one field only, both fields one after the other, or every line at once.", 0, 0, false,
                ("Alternating", 0), ("Upper only", 1), ("Lower only", 2), ("Interleaved, upper first", 4), ("Interleaved, lower first", 5), ("Both at once", 3)));
            Add(S("filter_type", SettingKind.Choice, "Filters", "The filters used all through: soft (constant-k, blurrier) or sharp (Butterworth).", 0, 0, false, ("Soft", 0), ("Sharp", 1)));
            Add(S("input_luma_filter", SettingKind.Choice, "Brightness filter going in", "Filters the brightness before it goes out, for fewer rainbow fringes.", 0, 0, false, ("Notch", 2), ("Box", 1), ("None", 0)));
            Add(S("chroma_lowpass_in", SettingKind.Choice, "Colour blur going in", "Blurs the colour before it goes out, as broadcast TV did.", 0, 0, false, ("Full", 2), ("Light", 1), ("None", 0)));
            Add(S("composite_preemphasis", SettingKind.Float, "Signal sharpening", "Boosts the signal's fine detail, sharpening the picture and strengthening its colours.", -1, 2));
            Group(S("composite_noise", SettingKind.Group, "Signal noise", "Noise on the signal itself."), () =>
            {
                Add(S("composite_noise_intensity", SettingKind.Percent, "Strength", "How strong the noise is.", 0, 1, true));
                Add(S("composite_noise_frequency", SettingKind.Float, "Frequency", "The size of the noise's ripples.", 0, 1));
                Add(S("composite_noise_detail", SettingKind.Int, "Detail", "Layers of finer and finer noise.", 1, 5));
            });
            Add(S("snow_intensity", SettingKind.Float, "Snow", "How often white speckles flash across the picture.", 0, 100, true));
            Add(S("snow_anisotropy", SettingKind.Percent, "Snow in lines", "Whether the speckles fall anywhere or bunch up on some lines.", 0, 1));
            Add(S("chroma_demodulation", SettingKind.Choice, "Colour decoder", "How the TV took the colour back out of the signal.", 0, 0, false,
                ("Box", 0), ("Notch", 1), ("2D", 4), ("2D adaptive", 5), ("1-line comb", 2), ("2-line comb", 3)));
            Add(S("luma_smear", SettingKind.Float, "Brightness smear", "Smears the brightness to the right.", 0, 1));
            Group(S("head_switching", SettingKind.Group, "Head switching", "The VHS heads switching over at the bottom of the picture."), () =>
            {
                Add(S("head_switching_height", SettingKind.Int, "Height", "Lines it reaches up.", 0, 24));
                Add(S("head_switching_offset", SettingKind.Int, "Below the picture", "How many of those lines are off the bottom of the screen.", 0, 24));
                Add(S("head_switching_horizontal_shift", SettingKind.Float, "Shift", "How far it pulls the lines sideways.", -100, 100));
                Group(S("head_switching_start_mid_line", SettingKind.Group, "Starts mid-line", "It starts part of the way along a line, with a flash where it starts."), () =>
                {
                    Add(S("head_switching_mid_line_position", SettingKind.Percent, "Where", "How far along the line it starts.", 0, 1));
                    Add(S("head_switching_mid_line_jitter", SettingKind.Percent, "Jitter", "How much that moves from frame to frame.", 0, 1, true));
                });
            });
            Group(S("tracking_noise", SettingKind.Group, "Tracking noise", "A VHS tape out of tracking: the bottom lines waver, with noise and snow."), () =>
            {
                Add(S("tracking_noise_height", SettingKind.Int, "Height", "Lines it reaches up.", 0, 120));
                Add(S("tracking_noise_wave_intensity", SettingKind.Float, "Waver", "How far the lines waver sideways.", -50, 50));
                Add(S("tracking_noise_snow_intensity", SettingKind.Float, "Snow", "How often speckles flash in it.", 0, 1, true));
                Add(S("tracking_noise_snow_anisotropy", SettingKind.Percent, "Snow in lines", "Whether its speckles bunch up on some lines.", 0, 1));
                Add(S("tracking_noise_noise_intensity", SettingKind.Percent, "Noise", "How strong its noise is.", 0, 1, true));
            });
            Group(S("ringing", SettingKind.Group, "Ringing", "Ripples beside sharp edges."), () =>
            {
                Add(S("ringing_frequency", SettingKind.Percent, "Frequency", "How close together the ripples are.", 0, 1));
                Add(S("ringing_power", SettingKind.Float, "Reach", "How far out the ripples reach.", 1, 10));
                Add(S("ringing_scale", SettingKind.Float, "Strength", "How strong the ripples are.", 0, 10));
            });
            Group(S("luma_noise", SettingKind.Group, "Brightness noise", "Fine noise in the brightness."), () =>
            {
                Add(S("luma_noise_intensity", SettingKind.Percent, "Strength", "How strong the noise is.", 0, 1, true));
                Add(S("luma_noise_frequency", SettingKind.Float, "Frequency", "The size of the noise's ripples.", 0, 1));
                Add(S("luma_noise_detail", SettingKind.Int, "Detail", "Layers of finer and finer noise.", 1, 5));
            });
            Group(S("chroma_noise", SettingKind.Group, "Colour noise", "Noise in the colour."), () =>
            {
                Add(S("chroma_noise_intensity", SettingKind.Percent, "Strength", "How strong the noise is.", 0, 1, true));
                Add(S("chroma_noise_frequency", SettingKind.Float, "Frequency", "The size of the noise's ripples.", 0, 0.5));
                Add(S("chroma_noise_detail", SettingKind.Int, "Detail", "Layers of finer and finer noise.", 1, 5));
            });
            Add(S("chroma_phase_error", SettingKind.Percent, "Hue error", "Turns every colour's hue round by the same amount.", 0, 1));
            Add(S("chroma_phase_noise_intensity", SettingKind.Percent, "Hue noise", "Turns the hue a little, differently on each line.", 0, 1, true));
            Add(S("chroma_delay_horizontal", SettingKind.Float, "Colour shift across", "Moves the colour sideways from the picture.", -40, 40));
            Add(S("chroma_delay_vertical", SettingKind.Int, "Colour shift down", "Moves the colour up or down by whole lines (worse with each copy of a tape).", -20, 20));
            Group(S("vhs_settings", SettingKind.Group, "VHS tape", "The picture recorded on a VHS tape and played back."), () =>
            {
                Add(S("vhs_tape_speed", SettingKind.Choice, "Tape speed", "Slower speeds fit more on a tape and lose more detail.", 0, 0, false, ("SP", 1), ("LP", 2), ("EP", 3), ("None", 0)));
                Add(S("vhs_chroma_loss", SettingKind.Percent, "Colour dropouts", "The chance a line loses its colour.", 0, 1, true));
                Group(S("vhs_sharpen_enabled", SettingKind.Group, "Sharpen", "The sharpening some VHS decks did."), () =>
                {
                    Add(S("vhs_sharpen", SettingKind.Float, "Strength", "How much it sharpens.", 0, 5));
                    Add(S("vhs_sharpen_frequency", SettingKind.Float, "Frequency", "How fine a detail it sharpens, against the tape speed's own.", 0.5, 4));
                });
                Group(S("vhs_edge_wave_enabled", SettingKind.Group, "Edge wave", "The picture waving sideways."), () =>
                {
                    Add(S("vhs_edge_wave", SettingKind.Float, "Strength", "How far it waves, in pixels.", 0, 20));
                    Add(S("vhs_edge_wave_speed", SettingKind.Float, "Speed", "How fast the waves move.", 0, 10));
                    Add(S("vhs_edge_wave_frequency", SettingKind.Float, "Frequency", "How close together the waves are.", 0, 0.5));
                    Add(S("vhs_edge_wave_detail", SettingKind.Int, "Detail", "Layers of finer and finer waves.", 1, 5));
                });
            });
            Add(S("vhs_chroma_vert_blend", SettingKind.Bool, "Blend colour down", "Mixes each line's colour with the line above, as VHS does."));
            Add(S("chroma_lowpass_out", SettingKind.Choice, "Colour blur coming out", "Blurs the colour as the TV decodes it.", 0, 0, false, ("Full", 2), ("Light", 1), ("None", 0)));
            Group(S("scale_settings", SettingKind.Group, "Scale", "Sizes the effect to the picture."), () =>
            {
                Add(S("bandwidth_scale", SettingKind.Float, "Across", "Stretches the effect across. DeRes sends the picture at the signal's own rate, so 1 is true to it.", 0.125, 8));
                Add(S("vertical_scale", SettingKind.Float, "Down", "Stretches the effect down. DeRes sets it from the picture's lines.", 0.125, 8.8));
                Add(S("scale_with_video_size", SettingKind.Bool, "With the picture's height", "Multiplies both by the picture's height over 480 lines."));
            });
            return list.ToArray();
        }

        static readonly Dictionary<string, NtscSetting> byKey = BuildIndex();

        static Dictionary<string, NtscSetting> BuildIndex()
        {
            var index = new Dictionary<string, NtscSetting>();
            foreach (NtscSetting s in All) index[s.Key] = s;
            return index;
        }

        public static NtscSetting Find(string key) => key != null && byKey.TryGetValue(key, out NtscSetting s) ? s : null;

        // ---- reading and writing one setting by its key (numbers as doubles, switches as bools)

        public object Get(string key)
        {
            switch (key)
            {
                case "random_seed": return (double)RandomSeed;
                case "use_field": return (double)(int)UseField;
                case "filter_type": return (double)(int)FilterType;
                case "input_luma_filter": return (double)(int)InputLumaFilter;
                case "chroma_lowpass_in": return (double)(int)ChromaLowpassIn;
                case "composite_preemphasis": return (double)CompositePreemphasis;
                case "composite_noise": return CompositeNoise;
                case "composite_noise_intensity": return (double)CompositeNoiseIntensity;
                case "composite_noise_frequency": return (double)CompositeNoiseFrequency;
                case "composite_noise_detail": return (double)CompositeNoiseDetail;
                case "snow_intensity": return (double)Snow;
                case "snow_anisotropy": return (double)SnowAnisotropy;
                case "chroma_demodulation": return (double)(int)ChromaDemodulation;
                case "luma_smear": return (double)LumaSmear;
                case "head_switching": return HeadSwitching;
                case "head_switching_height": return (double)HeadSwitchingHeight;
                case "head_switching_offset": return (double)HeadSwitchingOffset;
                case "head_switching_horizontal_shift": return (double)HeadSwitchingShift;
                case "head_switching_start_mid_line": return HeadSwitchingMidLine;
                case "head_switching_mid_line_position": return (double)MidLinePosition;
                case "head_switching_mid_line_jitter": return (double)MidLineJitter;
                case "tracking_noise": return TrackingNoise;
                case "tracking_noise_height": return (double)TrackingNoiseHeight;
                case "tracking_noise_wave_intensity": return (double)TrackingWave;
                case "tracking_noise_snow_intensity": return (double)TrackingSnow;
                case "tracking_noise_snow_anisotropy": return (double)TrackingSnowAnisotropy;
                case "tracking_noise_noise_intensity": return (double)TrackingNoiseIntensity;
                case "ringing": return Ringing;
                case "ringing_frequency": return (double)RingingFrequency;
                case "ringing_power": return (double)RingingPower;
                case "ringing_scale": return (double)RingingScale;
                case "luma_noise": return LumaNoise;
                case "luma_noise_intensity": return (double)LumaNoiseIntensity;
                case "luma_noise_frequency": return (double)LumaNoiseFrequency;
                case "luma_noise_detail": return (double)LumaNoiseDetail;
                case "chroma_noise": return ChromaNoise;
                case "chroma_noise_intensity": return (double)ChromaNoiseIntensity;
                case "chroma_noise_frequency": return (double)ChromaNoiseFrequency;
                case "chroma_noise_detail": return (double)ChromaNoiseDetail;
                case "chroma_phase_error": return (double)ChromaPhaseError;
                case "chroma_phase_noise_intensity": return (double)ChromaPhaseNoise;
                case "chroma_delay_horizontal": return (double)ChromaDelayHorizontal;
                case "chroma_delay_vertical": return (double)ChromaDelayVertical;
                case "vhs_settings": return Vhs;
                case "vhs_tape_speed": return (double)(int)TapeSpeed;
                case "vhs_chroma_loss": return (double)ChromaLoss;
                case "vhs_sharpen_enabled": return Sharpen;
                case "vhs_sharpen": return (double)SharpenIntensity;
                case "vhs_sharpen_frequency": return (double)SharpenFrequency;
                case "vhs_edge_wave_enabled": return EdgeWave;
                case "vhs_edge_wave": return (double)EdgeWaveIntensity;
                case "vhs_edge_wave_speed": return (double)EdgeWaveSpeed;
                case "vhs_edge_wave_frequency": return (double)EdgeWaveFrequency;
                case "vhs_edge_wave_detail": return (double)EdgeWaveDetail;
                case "vhs_chroma_vert_blend": return ChromaVertBlend;
                case "chroma_lowpass_out": return (double)(int)ChromaLowpassOut;
                case "scale_settings": return Scale;
                case "bandwidth_scale": return (double)HorizontalScale;
                case "vertical_scale": return (double)VerticalScale;
                case "scale_with_video_size": return ScaleWithVideoSize;
                default: return null;
            }
        }

        // A setting from a settings file's value, as the engine reads it (settings_from_json): a number
        // to the nearest float and held to the setting's range, a whole number cut to an integer
        // (JSON's 3.7 is 3), a menu item by its number, a switch by true or false. Throws
        // FormatException for a value of the wrong kind, or a menu number that isn't one of its items.
        public void Set(string key, object value)
        {
            NtscSetting s = Find(key) ?? throw new FormatException("there's no setting called " + key);
            bool isGroup = s.Kind == SettingKind.Group || s.Kind == SettingKind.Bool;
            if (isGroup)
            {
                if (!(value is bool on)) throw new FormatException(key + " should be true or false");
                SetSwitch(key, on);
                return;
            }
            if (!(value is double d)) throw new FormatException(key + " should be a number");
            switch (s.Kind)
            {
                case SettingKind.Choice:
                    {
                        // read as a u32 (a fraction or a negative number saturates), then it must be one of the items
                        uint n = RustMath.ToU32(Math.Truncate(d));
                        bool valid = false;
                        foreach (var c in s.Choices) valid |= c.Value == n;
                        if (!valid) throw new FormatException(key + " has no item " + n.ToString(CultureInfo.InvariantCulture));
                        SetChoice(key, (int)n);
                        break;
                    }
                case SettingKind.Int:
                    {
                        int n = RustMath.ToI32(Math.Truncate(d));
                        SetInt(key, Math.Max((int)s.Min, Math.Min((int)s.Max, n)));
                        break;
                    }
                default:
                    {
                        float f = Signal.JsonFloat(d);
                        float lo = (float)s.Min, hi = (float)s.Max;
                        // Rust's f32::clamp: NaN stays NaN
                        SetFloat(key, f < lo ? lo : f > hi ? hi : f);
                        break;
                    }
            }
        }

        void SetSwitch(string key, bool on)
        {
            switch (key)
            {
                case "composite_noise": CompositeNoise = on; break;
                case "head_switching": HeadSwitching = on; break;
                case "head_switching_start_mid_line": HeadSwitchingMidLine = on; break;
                case "tracking_noise": TrackingNoise = on; break;
                case "ringing": Ringing = on; break;
                case "luma_noise": LumaNoise = on; break;
                case "chroma_noise": ChromaNoise = on; break;
                case "vhs_settings": Vhs = on; break;
                case "vhs_sharpen_enabled": Sharpen = on; break;
                case "vhs_edge_wave_enabled": EdgeWave = on; break;
                case "vhs_chroma_vert_blend": ChromaVertBlend = on; break;
                case "scale_settings": Scale = on; break;
                case "scale_with_video_size": ScaleWithVideoSize = on; break;
            }
        }

        void SetChoice(string key, int n)
        {
            switch (key)
            {
                case "use_field": UseField = (UseField)n; break;
                case "filter_type": FilterType = (FilterType)n; break;
                case "input_luma_filter": InputLumaFilter = (LumaLowpass)n; break;
                case "chroma_lowpass_in": ChromaLowpassIn = (ChromaLowpass)n; break;
                case "chroma_demodulation": ChromaDemodulation = (ChromaDemodulation)n; break;
                case "vhs_tape_speed": TapeSpeed = (TapeSpeed)n; break;
                case "chroma_lowpass_out": ChromaLowpassOut = (ChromaLowpass)n; break;
            }
        }

        void SetInt(string key, int n)
        {
            switch (key)
            {
                case "random_seed": RandomSeed = n; break;
                case "composite_noise_detail": CompositeNoiseDetail = n; break;
                case "head_switching_height": HeadSwitchingHeight = n; break;
                case "head_switching_offset": HeadSwitchingOffset = n; break;
                case "tracking_noise_height": TrackingNoiseHeight = n; break;
                case "luma_noise_detail": LumaNoiseDetail = n; break;
                case "chroma_noise_detail": ChromaNoiseDetail = n; break;
                case "chroma_delay_vertical": ChromaDelayVertical = n; break;
                case "vhs_edge_wave_detail": EdgeWaveDetail = n; break;
            }
        }

        void SetFloat(string key, float f)
        {
            switch (key)
            {
                case "composite_preemphasis": CompositePreemphasis = f; break;
                case "composite_noise_intensity": CompositeNoiseIntensity = f; break;
                case "composite_noise_frequency": CompositeNoiseFrequency = f; break;
                case "snow_intensity": Snow = f; break;
                case "snow_anisotropy": SnowAnisotropy = f; break;
                case "luma_smear": LumaSmear = f; break;
                case "head_switching_horizontal_shift": HeadSwitchingShift = f; break;
                case "head_switching_mid_line_position": MidLinePosition = f; break;
                case "head_switching_mid_line_jitter": MidLineJitter = f; break;
                case "tracking_noise_wave_intensity": TrackingWave = f; break;
                case "tracking_noise_snow_intensity": TrackingSnow = f; break;
                case "tracking_noise_snow_anisotropy": TrackingSnowAnisotropy = f; break;
                case "tracking_noise_noise_intensity": TrackingNoiseIntensity = f; break;
                case "ringing_frequency": RingingFrequency = f; break;
                case "ringing_power": RingingPower = f; break;
                case "ringing_scale": RingingScale = f; break;
                case "luma_noise_intensity": LumaNoiseIntensity = f; break;
                case "luma_noise_frequency": LumaNoiseFrequency = f; break;
                case "chroma_noise_intensity": ChromaNoiseIntensity = f; break;
                case "chroma_noise_frequency": ChromaNoiseFrequency = f; break;
                case "chroma_phase_error": ChromaPhaseError = f; break;
                case "chroma_phase_noise_intensity": ChromaPhaseNoise = f; break;
                case "chroma_delay_horizontal": ChromaDelayHorizontal = f; break;
                case "vhs_chroma_loss": ChromaLoss = f; break;
                case "vhs_sharpen": SharpenIntensity = f; break;
                case "vhs_sharpen_frequency": SharpenFrequency = f; break;
                case "vhs_edge_wave": EdgeWaveIntensity = f; break;
                case "vhs_edge_wave_speed": EdgeWaveSpeed = f; break;
                case "vhs_edge_wave_frequency": EdgeWaveFrequency = f; break;
                case "bandwidth_scale": HorizontalScale = f; break;
                case "vertical_scale": VerticalScale = f; break;
            }
        }

        // ---- whole sets

        // The engine's defaults (NtscEffect::default()) as its settings file has them, which is what
        // the website starts every set from (JavaScript reads those digits to doubles).
        public static Dictionary<string, object> DefaultValues() => new Dictionary<string, object>
        {
            { "random_seed", 0.0 }, { "use_field", 4.0 }, { "filter_type", 1.0 }, { "input_luma_filter", 2.0 }, { "chroma_lowpass_in", 2.0 },
            { "composite_preemphasis", 1.0 }, { "composite_noise", true }, { "composite_noise_intensity", 0.05 }, { "composite_noise_frequency", 0.5 },
            { "composite_noise_detail", 1.0 }, { "snow_intensity", 0.00025 }, { "snow_anisotropy", 0.5 }, { "chroma_demodulation", 1.0 }, { "luma_smear", 0.5 },
            { "head_switching", true }, { "head_switching_height", 8.0 }, { "head_switching_offset", 3.0 }, { "head_switching_horizontal_shift", 72.0 },
            { "head_switching_start_mid_line", true }, { "head_switching_mid_line_position", 0.95 }, { "head_switching_mid_line_jitter", 0.03 },
            { "tracking_noise", true }, { "tracking_noise_height", 12.0 }, { "tracking_noise_wave_intensity", 15.0 }, { "tracking_noise_snow_intensity", 0.025 },
            { "tracking_noise_snow_anisotropy", 0.25 }, { "tracking_noise_noise_intensity", 0.25 }, { "ringing", true }, { "ringing_frequency", 0.45 },
            { "ringing_power", 4.0 }, { "ringing_scale", 4.0 }, { "luma_noise", true }, { "luma_noise_intensity", 0.01 }, { "luma_noise_frequency", 0.5 },
            { "luma_noise_detail", 1.0 }, { "chroma_noise", true }, { "chroma_noise_intensity", 0.1 }, { "chroma_noise_frequency", 0.05 }, { "chroma_noise_detail", 2.0 },
            { "chroma_phase_error", 0.0 }, { "chroma_phase_noise_intensity", 0.001 }, { "chroma_delay_horizontal", 0.0 }, { "chroma_delay_vertical", 0.0 },
            { "vhs_settings", true }, { "vhs_tape_speed", 2.0 }, { "vhs_chroma_loss", 0.000025 }, { "vhs_sharpen_enabled", true }, { "vhs_sharpen", 0.25 },
            { "vhs_sharpen_frequency", 1.0 }, { "vhs_edge_wave_enabled", true }, { "vhs_edge_wave", 0.5 }, { "vhs_edge_wave_speed", 4.0 },
            { "vhs_edge_wave_frequency", 0.05 }, { "vhs_edge_wave_detail", 2.0 }, { "vhs_chroma_vert_blend", false }, { "chroma_lowpass_out", 2.0 },
            { "scale_settings", true }, { "bandwidth_scale", 1.0 }, { "vertical_scale", 1.0 }, { "scale_with_video_size", false },
        };

        // What a settings file leaves out reads as (the engine's legacy_value(): the defaults of
        // before each setting was added).
        public static NtscSettings Legacy() => new NtscSettings
        {
            FilterType = FilterType.ConstantK,
            LumaSmear = 0,
            HeadSwitchingMidLine = false,
            CompositeNoiseFrequency = 0.25f,
            LumaNoise = false,
            ChromaNoiseDetail = 1,
            EdgeWaveDetail = 1,
        };

        // A set from a settings file's values (key: number or true/false; other keys and other
        // kinds of value are passed over, as the engine passes them): each one present, read as the
        // engine reads it, over the legacy defaults. Throws FormatException as the engine refuses one.
        public static NtscSettings FromValues(IDictionary<string, object> values)
        {
            var s = Legacy();
            foreach (NtscSetting setting in All)
                if (values.TryGetValue(setting.Key, out object v) && (v is double || v is bool)) s.Set(setting.Key, v);
            return s;
        }

        // An ntsc-rs settings file (version 1) read the same way. Throws FormatException for one the
        // engine wouldn't take.
        public static NtscSettings FromFile(string json)
        {
            if (!(Json.Parse(json) is Dictionary<string, object> values)) throw new FormatException("it isn't an ntsc-rs settings file");
            if (values.ContainsKey("_composite_preemphasis")) throw new FormatException("it's an ntscQT preset, not an ntsc-rs settings file");
            if (!values.TryGetValue("version", out object version) || !(version is double v)) throw new FormatException("it has no version, so it isn't an ntsc-rs settings file");
            if (Signal.JsonFloat(v) != 1f) throw new FormatException("it's settings version " + JsNumber.Text(v) + ", and only version 1 is known");
            return FromValues(values);
        }

        // These settings as an ntsc-rs settings file: every setting in the engine's own order and
        // way of writing them, then "version":1.
        public string ToFile()
        {
            var text = new StringBuilder("{");
            foreach (NtscSetting s in All)
            {
                object v = Get(s.Key);
                text.Append('"').Append(s.Key).Append("\":");
                if (v is bool b) text.Append(b ? "true" : "false");
                else if (s.Kind == SettingKind.Float || s.Kind == SettingKind.Percent) text.Append(F32Text((float)(double)v));
                else text.Append(((long)(double)v).ToString(CultureInfo.InvariantCulture));
                text.Append(',');
            }
            return text.Append("\"version\":1}").ToString();
        }

        // The settings that differ from another set, by key (for a short description of what changed).
        public List<string> Differences(NtscSettings other)
        {
            var keys = new List<string>();
            foreach (NtscSetting s in All)
            {
                object a = Get(s.Key), b = other.Get(s.Key);
                if (a is bool x ? !(b is bool y && x == y) : !(b is double q && BitConverter.DoubleToInt64Bits((double)a) == BitConverter.DoubleToInt64Bits(q))) keys.Add(s.Key);
            }
            return keys;
        }

        public bool SameAs(NtscSettings other) => Differences(other).Count == 0;

        // ---- a float as Rust writes one: the fewest digits that read back as it, never with an exponent

        public static string F32Text(float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) return f.ToString(CultureInfo.InvariantCulture);
            if (f == 0) return BitConverter.ToInt32(BitConverter.GetBytes(f), 0) < 0 ? "-0" : "0";
            if (f < 0) return "-" + F32Text(-f);
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
            int biased = (bits >> 23) & 0xFF;
            BigInteger mantissa = biased == 0 ? (bits & 0x7FFFFF) : (bits & 0x7FFFFF) | (1 << 23);
            int exp2 = biased == 0 ? -149 : biased - 150;
            // f exactly, as decimal digits: f = 0.all × 10^n
            BigInteger exact = exp2 >= 0 ? mantissa << exp2 : mantissa * BigInteger.Pow(5, -exp2);
            string full = exact.ToString(CultureInfo.InvariantCulture);
            string all = full.TrimEnd('0');
            int n = full.Length + (exp2 >= 0 ? 0 : exp2);
            string digits = all;
            int point = n;
            for (int k = 1; k < all.Length; k++)
            {
                string down = all.Substring(0, k);
                string up = (BigInteger.Parse(down, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
                int upPoint = n;
                if (up.Length > k)
                {
                    up = up.Substring(0, k);
                    upPoint = n + 1;
                }
                bool downOk = NearestF32(down, n) == f, upOk = NearestF32(up, upPoint) == f;
                if (!downOk && !upOk) continue;
                bool takeDown;
                if (downOk && upOk)
                {
                    string rest = all.Substring(k);
                    int cmp = (BigInteger.Parse(rest, CultureInfo.InvariantCulture) * 2).CompareTo(BigInteger.Pow(10, rest.Length));
                    takeDown = cmp < 0 || (cmp == 0 && (down[k - 1] - '0') % 2 == 0);
                }
                else takeDown = downOk;
                digits = (takeDown ? down : up).TrimEnd('0');
                point = takeDown ? n : upPoint;
                break;
            }
            // plain digits with the point where it goes
            if (point <= 0) return "0." + new string('0', -point) + digits;
            if (point >= digits.Length) return digits + new string('0', point - digits.Length);
            return digits.Substring(0, point) + "." + digits.Substring(point);
        }

        // 0.digits × 10^point to the nearest float (halves to even)
        public static float NearestF32(string digits, int point)
        {
            BigInteger d = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
            if (d.IsZero) return 0;
            int exp10 = point - digits.Length;
            BigInteger num = exp10 >= 0 ? d * BigInteger.Pow(10, exp10) : d, den = exp10 >= 0 ? BigInteger.One : BigInteger.Pow(10, -exp10);
            int e = (int)Math.Floor(BigInteger.Log(num, 2) - BigInteger.Log(den, 2)) - 23;
            BigInteger q, rem, d2;
            while (true)
            {
                if (e < -149) e = -149;
                BigInteger n2 = e < 0 ? num << -e : num;
                d2 = e > 0 ? den << e : den;
                q = BigInteger.DivRem(n2, d2, out rem);
                if (q >= (BigInteger.One << 24)) e++;
                else if (q < (BigInteger.One << 23) && e > -149) e--;
                else break;
            }
            int half = (rem * 2).CompareTo(d2);
            if (half > 0 || half == 0 && !q.IsEven) q += 1;
            return (float)((double)q * Math.Pow(2, e));
        }
    }
}

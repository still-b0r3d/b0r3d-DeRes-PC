using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace B0r3dDeRes.Exif
{
    // Content Credentials (C2PA): a signed record of where a picture came from. Cameras, phones and
    // editing apps add them as well as AI tools, so finding them isn't a sign of AI by itself; only
    // what they say inside counts. The signature isn't checked.
    sealed class ContentCredentials
    {
        public string Where;       // "JPEG APP11 segments", "PNG caBX chunk", "WebP C2PA chunk"
        public string Generator;   // the app or device that wrote them, when it could be read
        public string AiClaim;     // "made" or "edited", when they say so; else null
        public long Bytes;
    }

    sealed class AiMarkerReport
    {
        public bool Detected;      // the file says it was made or edited with AI
        public string Kind;        // "made", "edited" or null
        public string Tool;
        public string Confidence;  // "explicit" or "none"
        public string Summary;
        public List<string> Evidence = new List<string>();
        public string PromptOrParams;
        public ContentCredentials Credentials;
        public string SynthIdNote;
    }

    // The website's ai-detect.ts: what a file says about being made or edited with AI (the IPTC
    // digital source type in XMP or inside Content Credentials; Stable Diffusion and ComfyUI
    // settings, Midjourney prompts, DALL-E's name).
    static class AiMarkers
    {
        const string SynthIdNote = "Google SynthID embeds imperceptible pixel-level watermarks that cannot be verified offline without Google's proprietary verification service.";

        // Content Credentials bigger than this are only partly searched
        const int MaxScanBytes = 16 * 1024 * 1024;

        static readonly Regex made = new Regex("(^|[^A-Za-z])trainedAlgorithmicMedia", RegexOptions.CultureInvariant);
        static readonly Regex composite = new Regex("compositeWithTrainedAlgorithmicMedia|compositeSynthetic", RegexOptions.CultureInvariant);

        // What an IPTC digital source type in this text says: made by AI (trainedAlgorithmicMedia),
        // or a picture with AI-made parts (compositeWithTrainedAlgorithmicMedia, compositeSynthetic).
        // Ordinary phone photos may say computationalCapture, which isn't AI.
        public static (string Kind, string Code)? DigitalSourceType(string text)
        {
            // the "t" is lower case only when the code stands alone
            if (made.IsMatch(text)) return ("made", "trainedAlgorithmicMedia");
            Match m = composite.Match(text);
            if (m.Success) return ("edited", m.Value);
            return null;
        }

        // where `text` (one byte a character) first comes in `bytes` from `from` on, or -1
        static int Has(byte[] bytes, int length, string text, int from = 0)
        {
            int first = text[0];
            if (from >= length) return -1;
            for (int i = Array.IndexOf(bytes, (byte)first, Math.Max(0, from), length - Math.Max(0, from)); i >= 0 && i + text.Length <= length;
                 i = i + 1 < length ? Array.IndexOf(bytes, (byte)first, i + 1, length - i - 1) : -1)
            {
                bool all = true;
                for (int j = 1; j < text.Length && all; j++) all = bytes[i + j] == text[j];
                if (all) return i;
            }
            return -1;
        }

        // a CBOR text string starting at `at`, or null
        static string CborText(byte[] b, int length, int at)
        {
            if (at < 0 || at >= length) return null;
            int head = b[at];
            if (head >> 5 != 3) return null;
            int info = head & 0x1f;
            long len, start;
            if (info < 24)
            {
                len = info;
                start = at + 1;
            }
            else if (info == 24)
            {
                // (no byte for the length: JavaScript's NaN length reads as nothing)
                if (at + 1 >= length) return "";
                len = b[at + 1];
                start = at + 2;
            }
            else if (info == 25)
            {
                len = ((at + 1 < length ? b[at + 1] : 0) << 8) | (at + 2 < length ? b[at + 2] : 0);
                start = at + 3;
            }
            else return null;
            if (start + len > length) return null;
            return JsText.Utf8(b, (int)start, (int)Math.Min(len, 200));
        }

        // where the CBOR text key `key` ends (its last match), or -1
        static int AfterKey(byte[] b, int length, string key)
        {
            string pattern = (char)(0x60 + key.Length) + key;
            int found = -1;
            for (int i = Has(b, length, pattern); i >= 0; i = Has(b, length, pattern, i + 1)) found = i;
            return found < 0 ? -1 : found + pattern.Length;
        }

        // The app that wrote the credentials: several manifests can sit in one file (the
        // picture's sources come first); the last one is the picture's own.
        static string ClaimGenerator(byte[] b, int length)
        {
            int info = AfterKey(b, length, "claim_generator_info");
            if (info >= 0)
            {
                // has(b.subarray(info, info + 300), 'dname')
                int window = Math.Min(length, info + 300) - info;
                var part = new byte[Math.Max(0, window)];
                Array.Copy(b, info, part, 0, part.Length);
                // (0x64: CBOR's "a text of 4 bytes", then "name")
                int nameAt = Has(part, part.Length, "dname");
                if (nameAt >= 0)
                {
                    string name = CborText(b, length, info + nameAt + 5);
                    if (!string.IsNullOrEmpty(name)) return JsText.Trim(name);
                }
            }
            int plain = AfterKey(b, length, "claim_generator");
            if (plain >= 0)
            {
                string text = CborText(b, length, plain);
                if (text != null)
                {
                    text = JsText.Trim(text);
                    return text.Length > 0 ? text : null;
                }
            }
            return null;
        }

        // Content Credentials in a JPEG, PNG or WebP, or null when there are none.
        public static ContentCredentials FindContentCredentials(Bytes data)
        {
            var parts = new List<(long Start, long End)>();
            string where = "";
            long n = data.Length;
            if (data.At(0) == 0x89 && data.At(1) == 0x50 && data.At(2) == 0x4e && data.At(3) == 0x47)
            {
                where = "PNG caBX chunk";
                long pos = 8;
                while (pos + 8 <= n)
                {
                    long len = (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
                    string type = JsText.Chars(data.Array, (int)pos + 4, 4);
                    if (type == "caBX") parts.Add((pos + 8, Math.Min(n, pos + 8 + len)));
                    if (type == "IEND") break;
                    pos += 12 + len;
                }
            }
            else if (data.At(0) == 0xff && data.At(1) == 0xd8)
            {
                where = "JPEG APP11 segments";
                long pos = 2;
                while (pos + 4 <= n)
                {
                    if (data[pos] != 0xff)
                    {
                        pos++;
                        continue;
                    }
                    int marker = data[pos + 1];
                    if (marker == 0xd9 || marker == 0xda) break;
                    if (marker == 0xff || marker == 0x00 || marker == 0xd8 || (marker >= 0xd0 && marker <= 0xd7))
                    {
                        pos += marker == 0xff ? 1 : 2;
                        continue;
                    }
                    long len = (data[pos + 2] << 8) | data[pos + 3];
                    if (len < 2) break;
                    // APP11 JUMBF: "JP", a box instance (2 bytes), a packet number (4), then the box;
                    // later packets repeat the box's 8-byte header
                    if (marker == 0xeb && len >= 10 && data[pos + 4] == 0x4a && data[pos + 5] == 0x50)
                    {
                        long packet = (uint)((data[pos + 8] << 24) | (data[pos + 9] << 16) | (data[pos + 10] << 8) | data[pos + 11]);
                        long skip = packet > 1 ? 16 : 8;
                        long start = Math.Min(pos + 4 + skip, pos + 2 + len), end = Math.Min(n, pos + 2 + len);
                        parts.Add((Math.Min(start, n), end));
                    }
                    pos += 2 + len;
                }
            }
            else if (data.At(0) == 0x52 && data.At(1) == 0x49 && data.At(8) == 0x57 && data.At(9) == 0x45)
            {
                where = "WebP C2PA chunk";
                long pos = 12;
                while (pos + 8 <= n)
                {
                    long size = (uint)(data[pos + 4] | (data[pos + 5] << 8) | (data[pos + 6] << 16) | (data[pos + 7] << 24));
                    if (JsText.Chars(data.Array, (int)pos, 4) == "C2PA") parts.Add((pos + 8, Math.Min(n, pos + 8 + size)));
                    pos += 8 + size + size % 2;
                }
            }
            if (parts.Count == 0) return null;
            long total = 0;
            foreach (var p in parts) total += Math.Max(0, p.End - p.Start);
            var all = new byte[total];
            long at = 0;
            foreach (var p in parts)
            {
                long count = Math.Max(0, p.End - p.Start);
                Array.Copy(data.Array, p.Start, all, at, count);
                at += count;
            }
            return FromJumbf(all, where);
        }

        // what the JUMBF boxes of Content Credentials say (a video's C2PA box goes this way too)
        public static ContentCredentials FromJumbf(byte[] all, string where)
        {
            // JUMBF carries other things too (JPEG XT extensions); C2PA labels its boxes "c2pa"
            if (Has(all, all.Length, "c2pa") < 0) return null;
            int scan = (int)Math.Min(all.Length, MaxScanBytes);
            var claim = DigitalSourceType(JsText.Latin1(all, 0, scan));
            return new ContentCredentials { Where = where, Generator = ClaimGenerator(all, scan), AiClaim = claim?.Kind, Bytes = all.Length };
        }

        // What the file says about AI. `textBlocks` are its XMP, PNG text and EXIF comments;
        // `software` the EXIF Software tag.
        public static AiMarkerReport Detect(List<string> textBlocks, string software, ContentCredentials credentials)
        {
            var r = new AiMarkerReport();
            string kind = null, tool = null, prompt = null;
            void Found(string k, string why, string by = null)
            {
                if (kind != "made") kind = k;
                if (!string.IsNullOrEmpty(by) && string.IsNullOrEmpty(tool)) tool = by;
                r.Evidence.Add(why);
            }

            // 1. Content Credentials, by what they say inside
            if (credentials?.AiClaim != null)
            {
                string signer = !string.IsNullOrEmpty(credentials.Generator) ? ", written by " + credentials.Generator : "";
                Found(credentials.AiClaim, "Content Credentials (" + credentials.Where + signer + ") say " + (credentials.AiClaim == "made" ? "it was made by AI" : "it has AI-made parts"), credentials.Generator);
            }

            // 2. the EXIF Software tag
            if (!string.IsNullOrEmpty(software))
            {
                string sw = JsText.Lower(software);
                string says = "EXIF Software tag says \"" + software + "\"";
                if (sw.Contains("dall-e") || sw.Contains("dall" + (char)0xB7 + "e")) Found("made", says, "OpenAI DALL-E");
                else if (sw.Contains("midjourney")) Found("made", says, "Midjourney");
                else if (sw.Contains("stable diffusion") || sw.Contains("automatic1111") || sw.Contains("comfyui")) Found("made", says, "Stable Diffusion");
            }

            // 3. XMP, PNG text and EXIF comments
            bool sourceTypeSeen = false;
            foreach (string block in textBlocks)
            {
                var source = DigitalSourceType(block);
                if (source != null && !sourceTypeSeen)
                {
                    sourceTypeSeen = true;
                    Found(source.Value.Kind, "IPTC digital source type: " + source.Value.Code + " (" + (source.Value.Kind == "made" ? "made by AI" : "has AI-made or synthetic parts") + ")");
                }
                if ((block.Contains("--v 5") || block.Contains("--v 6") || block.Contains("--ar ") || block.Contains("--stylize")) &&
                    (block.Contains("Job ID:") || JsText.Lower(block).Contains("midjourney") || block.Contains("--v ")))
                {
                    tool = "Midjourney";
                    Found("made", "Midjourney prompt and settings");
                    if (prompt == null) prompt = Cut(block, 500);
                }
                if (block.Contains("Steps: ") && block.Contains("Sampler: ") && block.Contains("CFG scale: "))
                {
                    tool = "Stable Diffusion (Automatic1111 / WebUI)";
                    Found("made", "Stable Diffusion generation settings");
                    if (prompt == null) prompt = Cut(block, 500);
                }
                if (block.Contains("\"class_type\"") && (block.Contains("KSampler") || block.Contains("CLIPTextEncode")))
                {
                    tool = "ComfyUI / Stable Diffusion";
                    Found("made", "ComfyUI workflow");
                    if (prompt == null) prompt = Cut(block, 500);
                }
            }

            r.Detected = kind != null;
            r.Kind = kind;
            r.Tool = tool;
            r.Confidence = r.Detected ? "explicit" : "none";
            string by_ = !string.IsNullOrEmpty(tool) ? " (" + tool + ")" : "";
            r.Summary = kind == "made" ? "The file says it was made with AI" + by_ + "." :
                kind == "edited" ? "The file says parts of it were made with AI or are synthetic" + by_ + "." :
                "No standard AI provenance markers found in metadata.";
            r.PromptOrParams = prompt;
            r.Credentials = credentials;
            r.SynthIdNote = SynthIdNote;
            return r;
        }

        static string Cut(string s, int n) => s.Length > n ? s.Substring(0, n) : s;
    }
}

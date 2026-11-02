using System;
using System.Collections.Generic;
using System.Linq;

namespace B0r3dDeRes.Engine
{
    // Joel Yliluoma's arbitrary-palette positional dithering ("algorithm 2"). (The website's
    // core/yliluoma.ts.)
    //
    // Ordered dithering normally adds a threshold to the pixel and takes the nearest colour,
    // which only works when the palette is evenly spaced. This instead builds a "mixing plan" for
    // each colour: a list of PLAN palette entries whose average is as close as can be to it,
    // sorted by brightness. Each pixel then takes the entry its 8×8 Bayer threshold points at, so
    // a patch of that colour shows the whole mix. Mixes are averaged in linear light and judged in
    // sRGB with Yliluoma's luma-weighted distance; plans are kept by the colour rounded to 16 steps
    // a channel, and big palettes only mix from the 16 entries nearest the colour.
    sealed class YliluomaPlanner
    {
        public const int Plan = 64;
        const int Candidates = 16;

        // linear light to sRGB (0-1) as a plain table: the plan search reads it hundreds of times
        const int TableSteps = 4096;
        static readonly float[] linToSrgb = new float[TableSteps + 1];

        static YliluomaPlanner()
        {
            for (int i = 0; i <= TableSteps; i++) linToSrgb[i] = (float)Colour.LinearToSrgb(i / (double)TableSteps);
        }

        internal static float[] LinToSrgbTable => linToSrgb;

        // plans are kept by the colour rounded to KeyLevels steps a channel
        const int KeyBits = 4;
        const int KeyLevels = (1 << KeyBits) - 1;
        const int Keys = 1 << (3 * KeyBits);

        // a byte's step (rounded to the nearest)
        static int Step(int v8) => (v8 * KeyLevels + 127) / 255;

        // the byte a step stands for (0 -> 0, top -> 255)
        static int StepByte(int k) => (int)JsMath.Round(k * 255 / (double)KeyLevels);

        static int KeyOf(int r8, int g8, int b8) => (Step(r8) << (2 * KeyBits)) | (Step(g8) << KeyBits) | Step(b8);

        readonly PaletteMatcher m;
        readonly byte[] plans = new byte[Keys * Plan];
        readonly bool[] ready = new bool[Keys];
        readonly float[] srgb;
        readonly int[] order;
        readonly int[] all;
        readonly ushort[] sortScratch = new ushort[Plan];

        public YliluomaPlanner(PaletteMatcher matcher)
        {
            m = matcher;
            all = Enumerable.Range(0, m.Size).ToArray();
            srgb = new float[m.Size * 3];
            for (int i = 0; i < m.Size * 3; i++) srgb[i] = (float)(m.Bytes[i] / 255.0);
            // entries by brightness, so a plan can be sorted by rank
            order = new int[m.Size];
            int[] ranked = Enumerable.Range(0, m.Size).ToArray();
            Array.Sort(ranked, (a, b) =>
            {
                int c = ((double)m.Luma[a]).CompareTo(m.Luma[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
            for (int rank = 0; rank < ranked.Length; rank++) order[ranked[rank]] = rank;
        }

        // the palette entry for a linear-light colour at Bayer threshold t (0 … Plan-1)
        public int Pick(double r, double g, double b, int t)
        {
            int r8 = Colour.LinearToByte(r), g8 = Colour.LinearToByte(g), b8 = Colour.LinearToByte(b);
            // A palette colour is shown as itself. (Its plan would be built for the rounded cache
            // step, which can mix in a neighbour.)
            int near = m.NearestByte(r8, g8, b8);
            byte[] nb = m.Bytes;
            if (nb[near * 3] == r8 && nb[near * 3 + 1] == g8 && nb[near * 3 + 2] == b8) return near;
            int key = KeyOf(r8, g8, b8);
            if (!ready[key])
            {
                Build(key);
                ready[key] = true;
            }
            return plans[key * Plan + t];
        }

        void Build(int key)
        {
            const int mask = (1 << KeyBits) - 1;
            int r8 = StepByte(key >> (2 * KeyBits)), g8 = StepByte((key >> KeyBits) & mask), b8 = StepByte(key & mask);
            double tr = r8 / 255.0, tg = g8 / 255.0, tb = b8 / 255.0;
            double tl = tr * 0.299 + tg * 0.587 + tb * 0.114;
            int[] cands = CandidatesFor(r8, g8, b8);
            float[] lin = m.Linear;
            int at = key * Plan;

            int count = 0;
            double sr = 0, sg = 0, sb = 0;
            while (count < Plan)
            {
                // Try adding each candidate 1, 2, 4 … times (never more than doubles the plan so
                // far) and keep the addition whose average lands closest.
                int maxAdd = Math.Min(count == 0 ? 1 : count, Plan - count);
                double best = double.PositiveInfinity;
                int chosen = cands[0];
                int amount = 1;
                for (int ci = 0; ci < cands.Length; ci++)
                {
                    int c = cands[ci];
                    double cr = lin[c * 3], cg = lin[c * 3 + 1], cb = lin[c * 3 + 2];
                    if (count == 0)
                    {
                        // a plan's first entry: plain nearest in this metric
                        double d = Penalty(tr, tg, tb, tl, srgb[c * 3], srgb[c * 3 + 1], srgb[c * 3 + 2]);
                        if (d < best)
                        {
                            best = d;
                            chosen = c;
                        }
                        continue;
                    }
                    for (int p = 1; p <= maxAdd; p *= 2)
                    {
                        // the mix's average, linear -> sRGB through the table
                        double k = TableSteps / (double)(count + p);
                        double mr = linToSrgb[(int)((sr + cr * p) * k + 0.5)];
                        double mg = linToSrgb[(int)((sg + cg * p) * k + 0.5)];
                        double mb = linToSrgb[(int)((sb + cb * p) * k + 0.5)];
                        double d = Penalty(tr, tg, tb, tl, mr, mg, mb);
                        if (d < best)
                        {
                            best = d;
                            chosen = c;
                            amount = p;
                        }
                    }
                }
                for (int i = 0; i < amount; i++) plans[at + count + i] = (byte)chosen;
                count += amount;
                sr += lin[chosen * 3] * (double)amount;
                sg += lin[chosen * 3 + 1] * (double)amount;
                sb += lin[chosen * 3 + 2] * (double)amount;
            }
            // dark to light, so low thresholds take the dark entries: sorted as (rank << 8 | entry)
            ushort[] keyed = sortScratch;
            for (int i = 0; i < Plan; i++) keyed[i] = (ushort)((order[plans[at + i]] << 8) | plans[at + i]);
            Array.Sort(keyed);
            for (int i = 0; i < Plan; i++) plans[at + i] = (byte)(keyed[i] & 255);
        }

        int[] CandidatesFor(int r8, int g8, int b8)
        {
            if (m.Size <= Candidates) return all;
            Colour.LinearToOklab(Colour.ByteToLinear[r8], Colour.ByteToLinear[g8], Colour.ByteToLinear[b8], out double t0, out double t1, out double t2);
            float[] lab = m.Lab;
            var distances = new List<KeyValuePair<int, double>>(m.Size);
            foreach (int i in all)
            {
                double dl = lab[i * 3] - t0, da = lab[i * 3 + 1] - t1, db = lab[i * 3 + 2] - t2;
                // the website's (x) ** 2, which the browser works out as x * x
                distances.Add(new KeyValuePair<int, double>(i, dl * dl + da * da + db * db));
            }
            distances.Sort((a, b) =>
            {
                int c = a.Value.CompareTo(b.Value);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            return distances.Take(Candidates).Select(p => p.Key).ToArray();
        }

        // Yliluoma's colour distance: RGB differences weighted by how bright each channel looks,
        // plus the difference in luma.
        static double Penalty(double r1, double g1, double b1, double l1, double r2, double g2, double b2)
        {
            double dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
            double dl = l1 - (r2 * 0.299 + g2 * 0.587 + b2 * 0.114);
            return (dr * dr * 0.299 + dg * dg * 0.587 + db * db * 0.114) * 0.75 + dl * dl;
        }
    }
}

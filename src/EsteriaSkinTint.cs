using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CoAInstaller
{
    /// <summary>
    /// The Sethrak head follows the skin color. CoA Custom draws a male Sethrak with two textures: the body from a
    /// skin of five colors (layers in EsteriaSethrakTextures.bin, chosen by the skin color), the head from
    /// custom\sethrak\equipment\skin-male-0.blp to skin-male-4.blp, chosen by the same skin color. The package ships
    /// those five head files as one and the same picture, so the head keeps the dark grey of color 1 while the body
    /// turns tan, pink, green or beige.
    ///
    /// When the five files are still identical, the patch gets four new ones: skin-male-0 recolored, per skin color,
    /// with a gradient map taken from that color's body layer (brightness by quantiles, color by brightness, in Lab).
    /// The glowing cyan marks and the eyes keep their colors. A package with real head colors is left alone.
    /// </summary>
    static class EsteriaSkinTint
    {
        const int RecordSize = 156;

        struct Record { public uint Gender, Kind, Target, Value; public uint[] Choices; public string Path; }

        static List<Record> ReadRecords(string file)
        {
            var list = new List<Record>();
            byte[] b = File.ReadAllBytes(file);
            if (b.Length < 12 || BitConverter.ToUInt32(b, 0) != 0x314D4845 || BitConverter.ToUInt32(b, 4) != 1) return list;
            uint count = BitConverter.ToUInt32(b, 8);
            for (int i = 0; i < count && 12 + (i + 1) * RecordSize <= b.Length; i++)
            {
                int o = 12 + i * RecordSize;
                int end = o + 28;
                while (end < o + RecordSize && b[end] != 0) end++;
                list.Add(new Record
                {
                    Gender = BitConverter.ToUInt32(b, o), Kind = BitConverter.ToUInt32(b, o + 4),
                    Target = BitConverter.ToUInt32(b, o + 8), Value = BitConverter.ToUInt32(b, o + 12),
                    Choices = new[] { BitConverter.ToUInt32(b, o + 16), BitConverter.ToUInt32(b, o + 20), BitConverter.ToUInt32(b, o + 24) },
                    Path = Encoding.ASCII.GetString(b, o + 28, end - o - 28)
                });
            }
            return list;
        }

        /// <summary>The value of option 0 (the skin color) a record is chosen by, or -1.</summary>
        static int SkinChoice(Record r)
        {
            int found = -1;
            foreach (var c in r.Choices)
            {
                if (c == 0xffffffff) continue;
                if ((c & 0xffff) != 0) return -1;
                found = (int)(c >> 16);
            }
            return found;
        }

        public static void Apply(string client, SortedDictionary<string, byte[]> files, Func<string, byte[]> load, List<string> log)
        {
            string selections = Path.Combine(client, "EsteriaSethrak.bin"), bank = Path.Combine(client, "EsteriaSethrakTextures.bin");
            if (!File.Exists(selections) || !File.Exists(bank)) return;
            var records = ReadRecords(selections);
            // head textures (replaceable texture 11) and body layers (group 0) of the male, by skin color
            var heads = new SortedDictionary<int, string>();
            var layers = new SortedDictionary<int, uint>();
            foreach (var r in records.Where(r => r.Gender == 0))
            {
                int skin = SkinChoice(r);
                if (skin < 0) continue;
                if (r.Kind == 1 && r.Target == 11 && r.Path.EndsWith(".blp", StringComparison.OrdinalIgnoreCase) && !heads.ContainsKey(skin)) heads[skin] = r.Path;
                if (r.Kind == 3 && r.Target == 0 && !layers.ContainsKey(skin)) layers[skin] = r.Value;
            }
            if (!heads.ContainsKey(0) || !layers.ContainsKey(0) || heads.Count < 2) return;
            byte[] baseBlp = load(heads[0]);
            if (baseBlp == null) { log.Add("- Sethrak heads: " + heads[0] + " not found"); return; }
            foreach (var h in heads.Values.Skip(1))
            {
                byte[] other = load(h);
                if (other == null || !other.SequenceEqual(baseBlp)) { log.Add("= Sethrak heads: the package has its own head colors, left as they are"); return; }
            }
            int w, hgt;
            byte[] head = Blp.Decode(baseBlp, out w, out hgt);
            if (head == null || w != 512 || hgt != 512) { log.Add("- Sethrak heads: " + heads[0] + " is a texture format AFK cannot read"); return; }
            byte[] bankBytes = File.ReadAllBytes(bank);
            byte[] layer0 = Layer(bankBytes, layers[0]);
            if (layer0 == null) { log.Add("- Sethrak heads: body layer of color 1 not read"); return; }

            int n = w * hgt;
            var lab = new double[n * 3];
            var keep = new bool[n];
            for (int i = 0; i < n; i++)
            {
                double r = head[i * 4] / 255.0, g = head[i * 4 + 1] / 255.0, b = head[i * 4 + 2] / 255.0;
                double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                double sat = max > 0 ? (max - min) / max : 0;
                // glowing marks (cyan) and eyes (green): their own color, whatever the skin
                keep[i] = sat > 0.35 && max > 0.3 && g > r * 1.3;
                Lab(r, g, b, out lab[i * 3], out lab[i * 3 + 1], out lab[i * 3 + 2]);
            }
            // a gradient map: the head's brightness takes the brightness spread of the body layer (by quantiles), and
            // each brightness the mean color the body layer has at that brightness; half of the head's own color
            // detail stays on top
            var srcL = new List<double>(); var srcA = new List<double>(); var srcB = new List<double>();
            for (int i = 0; i < n; i++)
                if (!keep[i] && head[i * 4 + 3] > 128) { srcL.Add(lab[i * 3]); srcA.Add(lab[i * 3 + 1]); srcB.Add(lab[i * 3 + 2]); }
            if (srcL.Count < 1000) return;
            double[] srcQ = Quantiles(srcL, 65);
            double medA = Quantiles(srcA, 3)[1], medB = Quantiles(srcB, 3)[1];
            foreach (var pair in heads.Where(p => p.Key > 0))
            {
                uint offset;
                if (!layers.TryGetValue(pair.Key, out offset)) continue;
                byte[] layer = Layer(bankBytes, offset);
                if (layer == null) { log.Add("- Sethrak heads: body layer of color " + (pair.Key + 1) + " not read"); continue; }
                var tL = new List<double>(); var tA = new List<double>(); var tB = new List<double>();
                for (int i = 0; i < n; i++)
                {
                    if (layer[i * 4 + 3] <= 200) continue;
                    double l, a, b;
                    Lab(layer[i * 4] / 255.0, layer[i * 4 + 1] / 255.0, layer[i * 4 + 2] / 255.0, out l, out a, out b);
                    tL.Add(l); tA.Add(a); tB.Add(b);
                }
                if (tL.Count < 1000) continue;
                double[] dstQ = Quantiles(tL, 65);
                // mean a, b of the layer per brightness band
                var bandL = new List<double>(); var bandA = new List<double>(); var bandB = new List<double>();
                for (int k = 0; k + 1 < dstQ.Length; k++)
                {
                    double sl = 0, sa = 0, sb = 0; int c = 0;
                    for (int i = 0; i < tL.Count; i++)
                        if (tL[i] >= dstQ[k] && tL[i] <= dstQ[k + 1]) { sl += tL[i]; sa += tA[i]; sb += tB[i]; c++; }
                    if (c > 20 && (bandL.Count == 0 || sl / c > bandL[bandL.Count - 1])) { bandL.Add(sl / c); bandA.Add(sa / c); bandB.Add(sb / c); }
                }
                if (bandL.Count < 2) continue;
                var pixels = (byte[])head.Clone();
                for (int i = 0; i < n; i++)
                {
                    if (keep[i]) continue;
                    double L = Interp(lab[i * 3], srcQ, dstQ);
                    double A = Interp(L, bandL.ToArray(), bandA.ToArray()) + 0.5 * (lab[i * 3 + 1] - medA);
                    double B = Interp(L, bandL.ToArray(), bandB.ToArray()) + 0.5 * (lab[i * 3 + 2] - medB);
                    double r, g, b;
                    Rgb(L, A, B, out r, out g, out b);
                    pixels[i * 4] = ToByte(r); pixels[i * 4 + 1] = ToByte(g); pixels[i * 4 + 2] = ToByte(b);
                }
                files[pair.Value] = Blp.EncodeDxt5(pixels, w, hgt);
                log.Add("+ " + pair.Value + " <- " + heads[0] + " in the colors of skin color " + (pair.Key + 1) + " (Sethrak head)");
            }
        }

        static double[] Quantiles(List<double> values, int count)
        {
            var sorted = values.ToArray();
            Array.Sort(sorted);
            var q = new double[count];
            for (int k = 0; k < count; k++)
            {
                double pos = (sorted.Length - 1) * (double)k / (count - 1);
                int lo = (int)Math.Floor(pos), hi = Math.Min(sorted.Length - 1, lo + 1);
                q[k] = sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
            }
            return q;
        }

        /// <summary>Linear interpolation of x on the rising points xs -> ys, held at the ends.</summary>
        static double Interp(double x, double[] xs, double[] ys)
        {
            if (x <= xs[0]) return ys[0];
            for (int k = 1; k < xs.Length; k++)
                if (x <= xs[k]) return xs[k] > xs[k - 1] ? ys[k - 1] + (ys[k] - ys[k - 1]) * (x - xs[k - 1]) / (xs[k] - xs[k - 1]) : ys[k];
            return ys[ys.Length - 1];
        }

        static byte ToByte(double v) { return (byte)Math.Max(0, Math.Min(255, Math.Round(v * 255))); }

        /// <summary>A layer of the texture bank: width, height, LZNT1 size, then RGBA pixels packed with LZNT1.</summary>
        static byte[] Layer(byte[] bank, uint offset)
        {
            if (offset + 12 > bank.Length) return null;
            int w = BitConverter.ToInt32(bank, (int)offset), h = BitConverter.ToInt32(bank, (int)offset + 4), size = BitConverter.ToInt32(bank, (int)offset + 8);
            if (w != 512 || h != 512 || size <= 0 || offset + 12 + size > bank.Length) return null;
            byte[] px = Lznt1(bank, (int)offset + 12, size, w * h * 4);
            return px.Length == w * h * 4 ? px : null;
        }

        static byte[] Lznt1(byte[] data, int start, int length, int outSize)
        {
            var output = new byte[outSize];
            int o = 0, p = start, end = start + length;
            while (p + 2 <= end && o < outSize)
            {
                int header = data[p] | data[p + 1] << 8;
                p += 2;
                if (header == 0) break;
                int chunkEnd = Math.Min(end, p + (header & 0xfff) + 1);
                if ((header & 0x8000) == 0)
                {
                    int copy = Math.Min(chunkEnd - p, outSize - o);
                    Array.Copy(data, p, output, o, copy);
                    o += copy; p = chunkEnd;
                    continue;
                }
                int chunkStart = o;
                while (p < chunkEnd && o < outSize)
                {
                    int flags = data[p++];
                    for (int bit = 0; bit < 8 && p < chunkEnd && o < outSize; bit++)
                    {
                        if ((flags >> bit & 1) == 0) { output[o++] = data[p++]; continue; }
                        if (p + 2 > chunkEnd) { p = chunkEnd; break; }
                        int token = data[p] | data[p + 1] << 8;
                        p += 2;
                        int done = o - chunkStart, bits = 4;
                        while ((1 << bits) < done) bits++;
                        bits = Math.Min(12, bits);
                        int lenBits = 16 - bits;
                        int back = (token >> lenBits) + 1, count = (token & ((1 << lenBits) - 1)) + 3;
                        if (back > done) return new byte[0];
                        for (int k = 0; k < count && o < outSize; k++, o++) output[o] = output[o - back];
                    }
                }
                p = chunkEnd;
            }
            if (o == outSize) return output;
            return new byte[0];
        }

        // sRGB <-> CIE Lab (D65)
        static double Lin(double c) { return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        static double Gam(double c) { return c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(Math.Max(c, 0), 1 / 2.4) - 0.055; }
        static double F(double t) { return t > 216.0 / 24389 ? Math.Pow(t, 1.0 / 3) : (24389.0 / 27 * t + 16) / 116; }
        static double Fi(double t) { double t3 = t * t * t; return t3 > 216.0 / 24389 ? t3 : (116 * t - 16) / (24389.0 / 27); }
        const double Xn = 0.95047, Yn = 1.0, Zn = 1.08883;

        static void Lab(double r, double g, double b, out double L, out double A, out double B)
        {
            r = Lin(r); g = Lin(g); b = Lin(b);
            double x = 0.4124564 * r + 0.3575761 * g + 0.1804375 * b, y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b, z = 0.0193339 * r + 0.1191920 * g + 0.9503041 * b;
            double fx = F(x / Xn), fy = F(y / Yn), fz = F(z / Zn);
            L = 116 * fy - 16; A = 500 * (fx - fy); B = 200 * (fy - fz);
        }

        static void Rgb(double L, double A, double B, out double r, out double g, out double b)
        {
            double fy = (L + 16) / 116, fx = fy + A / 500, fz = fy - B / 200;
            double x = Fi(fx) * Xn, y = Fi(fy) * Yn, z = Math.Max(0, Fi(fz)) * Zn;
            r = Gam(3.2404542 * x - 1.5371385 * y - 0.4985314 * z);
            g = Gam(-0.9692660 * x + 1.8760108 * y + 0.0415560 * z);
            b = Gam(0.0556434 * x - 0.2040259 * y + 1.0572252 * z);
        }
    }

    /// <summary>Reads BLP2 textures (palette, DXT1/3/5, raw) to RGBA and writes DXT5 ones.</summary>
    static class Blp
    {
        public static byte[] Decode(byte[] b, out int w, out int h)
        {
            w = h = 0;
            if (b == null || b.Length < 1172 || b[0] != 'B' || b[1] != 'L' || b[2] != 'P' || b[3] != '2') return null;
            int type = b[8], alphaDepth = b[9], alphaType = b[10];
            w = BitConverter.ToInt32(b, 12); h = BitConverter.ToInt32(b, 16);
            int off = BitConverter.ToInt32(b, 20), size = BitConverter.ToInt32(b, 84);
            if (w <= 0 || h <= 0 || w > 4096 || h > 4096 || off <= 0 || off + size > b.Length) return null;
            var px = new byte[w * h * 4];
            if (type == 1)
            {
                for (int i = 0; i < w * h; i++)
                {
                    int c = 148 + b[off + i] * 4;
                    px[i * 4] = b[c + 2]; px[i * 4 + 1] = b[c + 1]; px[i * 4 + 2] = b[c];
                    int a = 255;
                    if (alphaDepth == 8) a = b[off + w * h + i];
                    else if (alphaDepth == 1) a = (b[off + w * h + i / 8] >> (i % 8) & 1) * 255;
                    else if (alphaDepth == 4) a = (b[off + w * h + i / 2] >> (i % 2 * 4) & 15) * 17;
                    px[i * 4 + 3] = (byte)a;
                }
                return px;
            }
            if (type == 3)
            {
                for (int i = 0; i < w * h; i++) { px[i * 4] = b[off + i * 4 + 2]; px[i * 4 + 1] = b[off + i * 4 + 1]; px[i * 4 + 2] = b[off + i * 4]; px[i * 4 + 3] = b[off + i * 4 + 3]; }
                return px;
            }
            if (type != 2) return null;
            int format = alphaDepth == 0 || (alphaDepth == 1 && alphaType == 0) ? 1 : alphaType == 7 ? 5 : alphaType == 1 ? 3 : alphaDepth == 1 ? 1 : 3;
            int blockSize = format == 1 ? 8 : 16, p = off;
            var colors = new int[16];
            for (int by = 0; by < (h + 3) / 4; by++)
                for (int bx = 0; bx < (w + 3) / 4; bx++, p += blockSize)
                {
                    if (p + blockSize > b.Length) return null;
                    int cp = format == 1 ? p : p + 8;
                    int c0 = b[cp] | b[cp + 1] << 8, c1 = b[cp + 2] | b[cp + 3] << 8;
                    var pal = new byte[4, 4];
                    Rgb565(c0, pal, 0); Rgb565(c1, pal, 1);
                    bool four = c0 > c1 || format != 1;
                    for (int k = 0; k < 3; k++)
                    {
                        pal[2, k] = (byte)(four ? (2 * pal[0, k] + pal[1, k]) / 3 : (pal[0, k] + pal[1, k]) / 2);
                        pal[3, k] = (byte)(four ? (pal[0, k] + 2 * pal[1, k]) / 3 : 0);
                    }
                    pal[0, 3] = pal[1, 3] = pal[2, 3] = 255; pal[3, 3] = (byte)(four ? 255 : 0);
                    uint bits = BitConverter.ToUInt32(b, cp + 4);
                    var alpha = new byte[16];
                    if (format == 3)
                        for (int k = 0; k < 16; k++) alpha[k] = (byte)((b[p + k / 2] >> (k % 2 * 4) & 15) * 17);
                    else if (format == 5)
                    {
                        int a0 = b[p], a1 = b[p + 1];
                        ulong abits = 0;
                        for (int k = 0; k < 6; k++) abits |= (ulong)b[p + 2 + k] << (8 * k);
                        var ap = new int[8];
                        ap[0] = a0; ap[1] = a1;
                        if (a0 > a1) for (int k = 1; k < 7; k++) ap[k + 1] = ((7 - k) * a0 + k * a1) / 7;
                        else { for (int k = 1; k < 5; k++) ap[k + 1] = ((5 - k) * a0 + k * a1) / 5; ap[6] = 0; ap[7] = 255; }
                        for (int k = 0; k < 16; k++) alpha[k] = (byte)ap[(int)(abits >> (3 * k) & 7)];
                    }
                    for (int k = 0; k < 16; k++)
                    {
                        int x = bx * 4 + k % 4, y = by * 4 + k / 4;
                        if (x >= w || y >= h) continue;
                        int idx = (int)(bits >> (2 * k) & 3), o = (y * w + x) * 4;
                        px[o] = pal[idx, 0]; px[o + 1] = pal[idx, 1]; px[o + 2] = pal[idx, 2];
                        px[o + 3] = format == 1 ? pal[idx, 3] : alpha[k];
                    }
                }
            return px;
        }

        static void Rgb565(int c, byte[,] pal, int i)
        {
            pal[i, 0] = (byte)((c >> 11 & 31) * 255 / 31); pal[i, 1] = (byte)((c >> 5 & 63) * 255 / 63); pal[i, 2] = (byte)((c & 31) * 255 / 31);
        }

        /// <summary>A BLP2 in DXT5 (the format of CoA Custom's own skins) with every mip level.</summary>
        public static byte[] EncodeDxt5(byte[] rgba, int w, int h)
        {
            var head = new byte[1172];
            head[0] = (byte)'B'; head[1] = (byte)'L'; head[2] = (byte)'P'; head[3] = (byte)'2';
            BitConverter.GetBytes(1).CopyTo(head, 4);
            head[8] = 2; head[9] = 8; head[10] = 7; head[11] = 1;
            BitConverter.GetBytes(w).CopyTo(head, 12); BitConverter.GetBytes(h).CopyTo(head, 16);
            var body = new MemoryStream();
            byte[] level = rgba;
            int lw = w, lh = h;
            for (int mip = 0; mip < 16; mip++)
            {
                byte[] blocks = Dxt5(level, lw, lh);
                BitConverter.GetBytes((int)(1172 + body.Length)).CopyTo(head, 20 + mip * 4);
                BitConverter.GetBytes(blocks.Length).CopyTo(head, 84 + mip * 4);
                body.Write(blocks, 0, blocks.Length);
                if (lw == 1 && lh == 1) break;
                int nw = Math.Max(1, lw / 2), nh = Math.Max(1, lh / 2);
                var next = new byte[nw * nh * 4];
                for (int y = 0; y < nh; y++)
                    for (int x = 0; x < nw; x++)
                        for (int c = 0; c < 4; c++)
                        {
                            int x0 = Math.Min(2 * x, lw - 1), x1 = Math.Min(2 * x + 1, lw - 1), y0 = Math.Min(2 * y, lh - 1), y1 = Math.Min(2 * y + 1, lh - 1);
                            next[(y * nw + x) * 4 + c] = (byte)((level[(y0 * lw + x0) * 4 + c] + level[(y0 * lw + x1) * 4 + c]
                                + level[(y1 * lw + x0) * 4 + c] + level[(y1 * lw + x1) * 4 + c] + 2) / 4);
                        }
                level = next; lw = nw; lh = nh;
            }
            var result = new byte[1172 + body.Length];
            head.CopyTo(result, 0);
            body.ToArray().CopyTo(result, 1172);
            return result;
        }

        static byte[] Dxt5(byte[] px, int w, int h)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            var output = new byte[bw * bh * 16];
            var block = new int[16, 4];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    for (int k = 0; k < 16; k++)
                    {
                        int x = Math.Min(bx * 4 + k % 4, w - 1), y = Math.Min(by * 4 + k / 4, h - 1);
                        for (int c = 0; c < 4; c++) block[k, c] = px[(y * w + x) * 4 + c];
                    }
                    int o = (by * bw + bx) * 16;
                    // alpha: eight steps between the largest and the smallest
                    int amax = 0, amin = 255;
                    for (int k = 0; k < 16; k++) { amax = Math.Max(amax, block[k, 3]); amin = Math.Min(amin, block[k, 3]); }
                    output[o] = (byte)amax; output[o + 1] = (byte)amin;
                    ulong abits = 0;
                    if (amax > amin)
                        for (int k = 0; k < 16; k++)
                        {
                            int step = (int)Math.Round((double)(amax - block[k, 3]) * 7 / (amax - amin));
                            int index = step == 0 ? 0 : step == 7 ? 1 : step + 1;
                            abits |= (ulong)index << (3 * k);
                        }
                    for (int k = 0; k < 6; k++) output[o + 2 + k] = (byte)(abits >> (8 * k));
                    // color: the ends of the box around the block's colors, a little inside, then the nearest of four
                    int[] lo = { 255, 255, 255 }, hi = { 0, 0, 0 };
                    for (int k = 0; k < 16; k++) for (int c = 0; c < 3; c++) { lo[c] = Math.Min(lo[c], block[k, c]); hi[c] = Math.Max(hi[c], block[k, c]); }
                    for (int c = 0; c < 3; c++) { int inset = (hi[c] - lo[c]) / 16; lo[c] += inset; hi[c] -= inset; }
                    int c0 = To565(hi), c1 = To565(lo);
                    if (c0 < c1) { int t = c0; c0 = c1; c1 = t; }
                    var pal = new int[4, 3];
                    From565(c0, pal, 0); From565(c1, pal, 1);
                    for (int c = 0; c < 3; c++) { pal[2, c] = (2 * pal[0, c] + pal[1, c]) / 3; pal[3, c] = (pal[0, c] + 2 * pal[1, c]) / 3; }
                    uint bits = 0;
                    for (int k = 0; k < 16; k++)
                    {
                        int best = 0, bestDist = int.MaxValue;
                        for (int i = 0; i < 4; i++)
                        {
                            int d = 0;
                            for (int c = 0; c < 3; c++) { int e = block[k, c] - pal[i, c]; d += e * e; }
                            if (d < bestDist) { bestDist = d; best = i; }
                        }
                        bits |= (uint)best << (2 * k);
                    }
                    output[o + 8] = (byte)c0; output[o + 9] = (byte)(c0 >> 8); output[o + 10] = (byte)c1; output[o + 11] = (byte)(c1 >> 8);
                    BitConverter.GetBytes(bits).CopyTo(output, o + 12);
                }
            return output;
        }

        static int To565(int[] c) { return (c[0] * 31 + 127) / 255 << 11 | (c[1] * 63 + 127) / 255 << 5 | (c[2] * 31 + 127) / 255; }

        static void From565(int v, int[,] pal, int i)
        {
            pal[i, 0] = (v >> 11 & 31) * 255 / 31; pal[i, 1] = (v >> 5 & 63) * 255 / 63; pal[i, 2] = (v & 31) * 255 / 31;
        }
    }
}

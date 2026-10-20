// PNG reading and writing without external libraries (8 and 16 bits per channel; grey,
// grey+alpha, RGB, RGBA and palette images, interlaced or not). System.Drawing is not used,
// because it converts 16-bit images to 8 bits.
using System;
using System.IO;
using System.IO.Compression;

namespace InterfaceTools
{
    /// <summary>Image with 1-4 interleaved channels of 8 or 16 bits (rows from the top).</summary>
    public class Img
    {
        public int W, H, C;
        public bool Is16;
        public ushort[] Px;
        public Img(int w, int h, int c, bool is16) { W = w; H = h; C = c; Is16 = is16; Px = new ushort[w * h * c]; }
        public int Max => Is16 ? 65535 : 255;
    }

    public class ImageError : Exception { public ImageError(string m) : base(m) { } }

    public static class Png
    {
        static readonly byte[] Sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static bool IsPng(byte[] d) => d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47;

        static uint BE32(byte[] d, int o) => (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3]);

        /// <summary>zlib stream -> raw data (System.IO.Compression only knows raw deflate).</summary>
        public static byte[] Inflate(byte[] z)
        {
            if (z.Length < 2) throw new ImageError("invalid zlib data");
            if ((z[0] & 0x0F) != 8 || ((z[0] << 8) | z[1]) % 31 != 0) throw new ImageError("invalid zlib header");
            int start = (z[1] & 0x20) != 0 ? 6 : 2;
            using (var ms = new MemoryStream(z, start, z.Length - start))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (var o = new MemoryStream())
            {
                ds.CopyTo(o);
                return o.ToArray();
            }
        }

        public static byte[] Deflate(byte[] raw)
        {
            using (var o = new MemoryStream())
            {
                o.WriteByte(0x78); o.WriteByte(0xDA);
                using (var ds = new DeflateStream(o, CompressionLevel.Optimal, true)) ds.Write(raw, 0, raw.Length);
                uint a = 1, b = 0;
                foreach (byte x in raw) { a = (a + x) % 65521; b = (b + a) % 65521; }
                uint adler = (b << 16) | a;
                o.WriteByte((byte)(adler >> 24)); o.WriteByte((byte)(adler >> 16)); o.WriteByte((byte)(adler >> 8)); o.WriteByte((byte)adler);
                return o.ToArray();
            }
        }

        public static Img Load(string path) => Decode(File.ReadAllBytes(path), Path.GetFileName(path));

        public static Img Decode(byte[] d, string name = "PNG")
        {
            if (!IsPng(d)) throw new ImageError($"{name}: not a PNG file");
            int p = 8;
            int w = 0, h = 0, depth = 0, ctype = -1, interlace = 0;
            byte[] palette = null, trns = null;
            var idat = new MemoryStream();
            while (p + 8 <= d.Length)
            {
                int ln = (int)BE32(d, p);
                string typ = System.Text.Encoding.ASCII.GetString(d, p + 4, 4);
                if (ln < 0 || p + 12 + ln > d.Length) throw new ImageError($"{name}: the file is truncated");
                int c = p + 8;
                switch (typ)
                {
                    case "IHDR":
                        w = (int)BE32(d, c); h = (int)BE32(d, c + 4); depth = d[c + 8]; ctype = d[c + 9]; interlace = d[c + 12];
                        break;
                    case "PLTE": palette = new byte[ln]; Buffer.BlockCopy(d, c, palette, 0, ln); break;
                    case "tRNS": trns = new byte[ln]; Buffer.BlockCopy(d, c, trns, 0, ln); break;
                    case "IDAT": idat.Write(d, c, ln); break;
                }
                p += 12 + ln;
                if (typ == "IEND") break;
            }
            if (w <= 0 || h <= 0 || ctype < 0) throw new ImageError($"{name}: missing image header");
            int spp;
            switch (ctype) { case 0: spp = 1; break; case 2: spp = 3; break; case 3: spp = 1; break; case 4: spp = 2; break; case 6: spp = 4; break; default: throw new ImageError($"{name}: unknown PNG colour type {ctype}"); }
            if (!(depth == 1 || depth == 2 || depth == 4 || depth == 8 || depth == 16) || (depth > 8 && ctype == 3) || (depth < 8 && ctype != 0 && ctype != 3))
                throw new ImageError($"{name}: unsupported PNG bit depth {depth}");
            byte[] raw = Inflate(idat.ToArray());
            // output channels
            bool hasKey = trns != null && (ctype == 0 || ctype == 2);
            int outC = ctype == 3 ? (trns != null ? 4 : 3) : (hasKey ? spp + 1 : spp);
            bool out16 = depth == 16;
            var img = new Img(w, h, outC, out16);
            int bpp = Math.Max(1, spp * depth / 8);   // bytes per pixel for filtering
            int pos = 0;
            void Pass(int x0, int y0, int dx, int dy)
            {
                int pw = (w - x0 + dx - 1) / dx, ph = (h - y0 + dy - 1) / dy;
                if (pw <= 0 || ph <= 0) return;
                int stride = (pw * spp * depth + 7) / 8;
                var prev = new byte[stride];
                var line = new byte[stride];
                for (int yy = 0; yy < ph; yy++)
                {
                    if (pos + 1 + stride > raw.Length) throw new ImageError($"{name}: image data is truncated");
                    int f = raw[pos++];
                    Buffer.BlockCopy(raw, pos, line, 0, stride);
                    pos += stride;
                    Unfilter(f, line, prev, bpp);
                    int y = y0 + yy * dy;
                    for (int xx = 0; xx < pw; xx++)
                    {
                        int x = x0 + xx * dx;
                        int o = (y * w + x) * outC;
                        var s = new int[spp];
                        for (int k = 0; k < spp; k++)
                        {
                            if (depth == 16) { int q = (xx * spp + k) * 2; s[k] = line[q] << 8 | line[q + 1]; }
                            else if (depth == 8) s[k] = line[xx * spp + k];
                            else
                            {
                                int bit = (xx * spp + k) * depth;
                                s[k] = (line[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
                            }
                        }
                        if (ctype == 3)
                        {
                            int i = s[0];
                            if (palette == null || i * 3 + 2 >= palette.Length) { img.Px[o] = img.Px[o + 1] = img.Px[o + 2] = 0; }
                            else { img.Px[o] = palette[i * 3]; img.Px[o + 1] = palette[i * 3 + 1]; img.Px[o + 2] = palette[i * 3 + 2]; }
                            if (outC == 4) img.Px[o + 3] = (ushort)(i < trns.Length ? trns[i] : 255);
                        }
                        else
                        {
                            for (int k = 0; k < spp; k++)
                            {
                                int v = s[k];
                                if (depth < 8) v = v * 255 / ((1 << depth) - 1);
                                img.Px[o + k] = (ushort)v;
                            }
                            if (hasKey)
                            {
                                bool match;
                                if (ctype == 0) match = s[0] == (trns[0] << 8 | trns[1]);
                                else match = s[0] == (trns[0] << 8 | trns[1]) && s[1] == (trns[2] << 8 | trns[3]) && s[2] == (trns[4] << 8 | trns[5]);
                                img.Px[o + spp] = (ushort)(match ? 0 : img.Max);
                            }
                        }
                    }
                    var t = prev; prev = line; line = t;
                }
            }
            if (interlace == 0) Pass(0, 0, 1, 1);
            else
            {
                Pass(0, 0, 8, 8); Pass(4, 0, 8, 8); Pass(0, 4, 4, 8); Pass(2, 0, 4, 4);
                Pass(0, 2, 2, 4); Pass(1, 0, 2, 2); Pass(0, 1, 1, 2);
            }
            return img;
        }

        static void Unfilter(int f, byte[] line, byte[] prev, int bpp)
        {
            int n = line.Length;
            switch (f)
            {
                case 0: return;
                case 1: for (int i = bpp; i < n; i++) line[i] = (byte)(line[i] + line[i - bpp]); return;
                case 2: for (int i = 0; i < n; i++) line[i] = (byte)(line[i] + prev[i]); return;
                case 3:
                    for (int i = 0; i < n; i++) line[i] = (byte)(line[i] + (((i >= bpp ? line[i - bpp] : 0) + prev[i]) >> 1));
                    return;
                case 4:
                    for (int i = 0; i < n; i++)
                    {
                        int a = i >= bpp ? line[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                        int pa = Math.Abs(b - c), pb = Math.Abs(a - c), pc = Math.Abs(a + b - 2 * c);
                        int pr = pa <= pb && pa <= pc ? a : (pb <= pc ? b : c);
                        line[i] = (byte)(line[i] + pr);
                    }
                    return;
                default: throw new ImageError($"invalid PNG filter type {f}");
            }
        }

        /// <summary>Encodes 1 (grey), 2 (grey+alpha), 3 (RGB) or 4 (RGBA) channels, 8 or 16 bits.</summary>
        public static byte[] Encode(Img img)
        {
            int ctype = img.C == 1 ? 0 : img.C == 2 ? 4 : img.C == 3 ? 2 : 6;
            int bytesPer = img.Is16 ? 2 : 1;
            int bpp = img.C * bytesPer, stride = img.W * bpp;
            var raw = new byte[(stride + 1) * img.H];
            var prev = new byte[stride];
            var cur = new byte[stride];
            var cand = new byte[5][];
            for (int k = 0; k < 5; k++) cand[k] = new byte[stride];
            for (int y = 0; y < img.H; y++)
            {
                int o = y * img.W * img.C;
                for (int i = 0; i < img.W * img.C; i++)
                {
                    ushort v = img.Px[o + i];
                    if (img.Is16) { cur[i * 2] = (byte)(v >> 8); cur[i * 2 + 1] = (byte)v; } else cur[i] = (byte)v;
                }
                // adaptive filter: smallest sum of absolute values
                int best = 0; long bestSum = long.MaxValue;
                for (int f = 0; f < 5; f++)
                {
                    var c = cand[f];
                    long sum = 0;
                    for (int i = 0; i < stride; i++)
                    {
                        int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], cc = i >= bpp ? prev[i - bpp] : 0;
                        int pr;
                        switch (f)
                        {
                            case 0: pr = 0; break;
                            case 1: pr = a; break;
                            case 2: pr = b; break;
                            case 3: pr = (a + b) >> 1; break;
                            default:
                                int pa = Math.Abs(b - cc), pb = Math.Abs(a - cc), pc = Math.Abs(a + b - 2 * cc);
                                pr = pa <= pb && pa <= pc ? a : (pb <= pc ? b : cc);
                                break;
                        }
                        byte v = (byte)(cur[i] - pr);
                        c[i] = v;
                        sum += v < 128 ? v : 256 - v;
                    }
                    if (sum < bestSum) { bestSum = sum; best = f; }
                }
                int ro = y * (stride + 1);
                raw[ro] = (byte)best;
                Buffer.BlockCopy(cand[best], 0, raw, ro + 1, stride);
                var t = prev; prev = cur; cur = t;
            }
            var o2 = new MemoryStream();
            o2.Write(Sig, 0, 8);
            var ihdr = new byte[13];
            PutBE(ihdr, 0, (uint)img.W); PutBE(ihdr, 4, (uint)img.H);
            ihdr[8] = (byte)(img.Is16 ? 16 : 8); ihdr[9] = (byte)ctype;
            Chunk(o2, "IHDR", ihdr);
            Chunk(o2, "IDAT", Deflate(raw));
            Chunk(o2, "IEND", new byte[0]);
            return o2.ToArray();
        }

        static void PutBE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; PutBE(len, 0, (uint)data.Length);
            s.Write(len, 0, 4);
            var td = new byte[4 + data.Length];
            System.Text.Encoding.ASCII.GetBytes(type, 0, 4, td, 0);
            Buffer.BlockCopy(data, 0, td, 4, data.Length);
            s.Write(td, 0, td.Length);
            var crc = new byte[4]; PutBE(crc, 0, Text.Crc32(td));
            s.Write(crc, 0, 4);
        }

        public static void Save(string path, Img img) => File.WriteAllBytes(path, Encode(img));
    }
}

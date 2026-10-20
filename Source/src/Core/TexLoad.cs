// Texture loading for the editor: DDS (every format the DDS tool can decode), BMP (8/24/32-bit),
// TGA (24/32-bit, plain and RLE) and PNG. Port of texload.py, extended with the DDS formats of
// the DDS tool.
using System;
using System.IO;
using System.Linq;

namespace InterfaceTools
{
    public class TextureError : Exception { public TextureError(string m) : base(m) { } }

    /// <summary>RGBA pixels, 4 bytes per pixel, rows from the top.</summary>
    public class Texture
    {
        public int Width, Height;
        public byte[] Rgba;
        public Texture(int w, int h) { Width = w; Height = h; Rgba = new byte[w * h * 4]; }
    }

    public static class TexLoad
    {
        public static Texture Load(string path) => Decode(File.ReadAllBytes(path), path);

        public static Texture Decode(byte[] data, string path)
        {
            if (data.Length >= 4 && data[0] == 'D' && data[1] == 'D' && data[2] == 'S' && data[3] == ' ') return LoadDds(data, path);
            if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M') return LoadBmp(data);
            if (Png.IsPng(data))
            {
                try { return FromImg(Png.Decode(data, Path.GetFileName(path))); }
                catch (ImageError e) { throw new TextureError(e.Message); }
            }
            if (Path.GetExtension(path).ToLowerInvariant() == ".tga") return LoadTga(data);
            throw new TextureError($"unsupported image format: {Path.GetFileName(path)}");
        }

        public static Texture FromImg(Img img)
        {
            var t = new Texture(img.W, img.H);
            int n = img.W * img.H;
            for (int i = 0; i < n; i++)
            {
                int o = i * img.C;
                byte V(int k) => (byte)(img.Is16 ? img.Px[o + k] >> 8 : img.Px[o + k]);
                byte r, g, b, a;
                switch (img.C)
                {
                    case 1: r = g = b = V(0); a = 255; break;
                    case 2: r = g = b = V(0); a = V(1); break;
                    case 3: r = V(0); g = V(1); b = V(2); a = 255; break;
                    default: r = V(0); g = V(1); b = V(2); a = V(3); break;
                }
                t.Rgba[i * 4] = r; t.Rgba[i * 4 + 1] = g; t.Rgba[i * 4 + 2] = b; t.Rgba[i * 4 + 3] = a;
            }
            return t;
        }

        static Texture LoadDds(byte[] data, string path)
        {
            try
            {
                var dds = Dds.ParseDds(data, path);
                if (!dds.Fmt.Supported) throw new TextureError($"unsupported DDS format {dds.FmtDesc}");
                var vals = Dds.DecodeSurface(dds, 0, dds.W, dds.H);
                var t = new Texture(dds.W, dds.H);
                var info = dds.Fmt.ValueChannels().ToDictionary(c => c.n, c => c.t);
                byte B(string c, int i, byte def)
                {
                    if (!vals.TryGetValue(c, out var a)) return def;
                    double v = a[i];
                    if (info[c] == "snorm") v = (v + 1) / 2;
                    else if (info[c] == "uint" || info[c] == "sint") v = v / 255.0;
                    return (byte)Math.Round(Math.Max(0, Math.Min(1, v)) * 255);
                }
                bool alphaOnly = vals.Count == 1 && vals.ContainsKey("a");
                for (int i = 0; i < dds.W * dds.H; i++)
                {
                    byte r, g, b, a;
                    if (vals.ContainsKey("l")) { r = g = b = B("l", i, 0); a = B("a", i, 255); }
                    else if (alphaOnly) { r = g = b = 255; a = B("a", i, 255); }
                    else { r = B("r", i, 0); g = B("g", i, 0); b = B("b", i, 0); a = B("a", i, 255); }
                    t.Rgba[i * 4] = r; t.Rgba[i * 4 + 1] = g; t.Rgba[i * 4 + 2] = b; t.Rgba[i * 4 + 3] = a;
                }
                return t;
            }
            catch (DdsError e) { throw new TextureError(e.Message); }
        }

        static Texture LoadBmp(byte[] d)
        {
            if (d.Length < 54) throw new TextureError("the BMP file is truncated");
            int offBits = BitConverter.ToInt32(d, 10), hsize = BitConverter.ToInt32(d, 14);
            int w = BitConverter.ToInt32(d, 18), hh = BitConverter.ToInt32(d, 22);
            int bpp = BitConverter.ToUInt16(d, 28), comp = BitConverter.ToInt32(d, 30);
            if ((comp != 0 && comp != 3) || (bpp != 8 && bpp != 24 && bpp != 32)) throw new TextureError($"unsupported BMP ({bpp}-bit, compression {comp})");
            bool topDown = hh < 0;
            int h = Math.Abs(hh);
            byte[][] pal = null;
            if (bpp == 8)
            {
                int ncol = BitConverter.ToInt32(d, 46);
                if (ncol == 0) ncol = 256;
                int po = 14 + hsize;
                pal = Enumerable.Range(0, ncol).Select(i => po + 4 * i + 2 < d.Length ? new[] { d[po + 4 * i + 2], d[po + 4 * i + 1], d[po + 4 * i] } : new byte[3]).ToArray();
            }
            int stride = ((w * bpp + 31) / 32) * 4;
            var t = new Texture(w, h);
            for (int row = 0; row < h; row++)
            {
                int y = topDown ? row : h - 1 - row;
                int p = offBits + row * stride;
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    if (bpp == 8)
                    {
                        int c = p + x < d.Length ? d[p + x] : 0;
                        var rgb = c < pal.Length ? pal[c] : new byte[3];
                        t.Rgba[o] = rgb[0]; t.Rgba[o + 1] = rgb[1]; t.Rgba[o + 2] = rgb[2]; t.Rgba[o + 3] = 255;
                    }
                    else
                    {
                        int q = p + x * (bpp / 8);
                        if (q + (bpp / 8) > d.Length) throw new TextureError("the BMP file is truncated");
                        t.Rgba[o] = d[q + 2]; t.Rgba[o + 1] = d[q + 1]; t.Rgba[o + 2] = d[q]; t.Rgba[o + 3] = bpp == 32 ? d[q + 3] : (byte)255;
                    }
                }
            }
            return t;
        }

        static Texture LoadTga(byte[] d)
        {
            int idlen = d[0], cmapType = d[1], itype = d[2];
            int w = BitConverter.ToUInt16(d, 12), h = BitConverter.ToUInt16(d, 14), bpp = d[16], desc = d[17];
            if ((itype != 2 && itype != 10) || (bpp != 24 && bpp != 32)) throw new TextureError("only true-colour 24/32-bit TGA files are supported");
            int p = 18 + idlen + (cmapType != 0 ? BitConverter.ToUInt16(d, 5) * ((d[7] + 7) / 8) : 0);
            int n = w * h, px = bpp / 8;
            var raw = new byte[n * px];
            if (itype == 2) Buffer.BlockCopy(d, p, raw, 0, Math.Min(raw.Length, d.Length - p));
            else
            {
                int q = 0;
                while (q < raw.Length && p < d.Length)
                {
                    int hdr = d[p++];
                    int cnt = (hdr & 0x7F) + 1;
                    if ((hdr & 0x80) != 0)
                    {
                        for (int k = 0; k < cnt && q < raw.Length; k++) { Buffer.BlockCopy(d, p, raw, q, px); q += px; }
                        p += px;
                    }
                    else
                    {
                        int len = Math.Min(px * cnt, raw.Length - q);
                        Buffer.BlockCopy(d, p, raw, q, len); q += len; p += px * cnt;
                    }
                }
            }
            var t = new Texture(w, h);
            bool top = (desc & 0x20) != 0;
            for (int y = 0; y < h; y++)
            {
                int sy = top ? y : h - 1 - y;
                for (int x = 0; x < w; x++)
                {
                    int q = (sy * w + x) * px, o = (y * w + x) * 4;
                    t.Rgba[o] = raw[q + 2]; t.Rgba[o + 1] = raw[q + 1]; t.Rgba[o + 2] = raw[q]; t.Rgba[o + 3] = px == 4 ? raw[q + 3] : (byte)255;
                }
            }
            return t;
        }

        /// <summary>Grey checkerboard used when the real texture cannot be found.</summary>
        public static Texture Placeholder(int w, int h, int cell = 16)
        {
            var t = new Texture(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte v = (byte)((((x / cell) + (y / cell)) & 1) != 0 ? 70 : 55);
                    int o = (y * w + x) * 4;
                    t.Rgba[o] = v; t.Rgba[o + 1] = v; t.Rgba[o + 2] = (byte)(v + 10); t.Rgba[o + 3] = 255;
                }
            return t;
        }
    }
}

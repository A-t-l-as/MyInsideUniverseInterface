// DDS texture converter (DDS <-> PNG) with the full metadata stored in JSON, compatible with
// dds_tool.py: files exported by one can be imported by the other. Supports the formats of the
// Reality Pump games: DXT1/DXT3/DXT5 (BC1-BC3), BC4, BC5 and uncompressed formats up to 16 bits
// per channel (legacy bit-mask headers and DX10 headers), with arrays, cubemaps and 3D textures.
// BC6H, BC7 and floating point / 32-bit formats are recognized but not converted.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace InterfaceTools
{
    public class DdsError : Exception { public DdsError(string m) : base(m) { } }

    /// <summary>Channel of an uncompressed format: name, bit offset, bits, type (unorm, snorm, uint, sint, float, uf11, uf10, x).</summary>
    public struct Chan
    {
        public string N; public int Off, Bits; public string T;
        public Chan(string n, int off, int bits, string t) { N = n; Off = off; Bits = bits; T = t; }
    }

    public class DdsFormat
    {
        public string Name, Kind;   // Kind: raw / bc
        public int ESize, Bc;
        public List<Chan> Channels = new List<Chan>();
        public bool Srgb, Signed;
        public string Special;

        public int BlockBytes => Bc == 1 || Bc == 4 ? 8 : 16;
        public long SurfaceSize(long w, long h) => Kind == "bc" ? Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * BlockBytes : w * h * ESize;
        public long RowPitch(long w) => Kind == "bc" ? Math.Max(1, (w + 3) / 4) * BlockBytes : w * ESize;

        /// <summary>Logical channels (without padding): (name, bits, type).</summary>
        public List<(string n, int b, string t)> ValueChannels()
        {
            if (Kind == "bc")
            {
                if (Bc == 1 || Bc == 2 || Bc == 3 || Bc == 7) return "rgba".Select(c => (c.ToString(), 8, "unorm")).ToList();
                string t = Signed ? "snorm" : "unorm";
                if (Bc == 4) return new List<(string, int, string)> { ("r", 8, t) };
                if (Bc == 5) return new List<(string, int, string)> { ("r", 8, t), ("g", 8, t) };
                return "rgb".Select(c => (c.ToString(), 16, "float")).ToList();
            }
            if (Special == "rgb9e5") return "rgb".Select(c => (c.ToString(), 9, "float")).ToList();
            return Channels.Where(c => c.T != "x").Select(c => (c.N, c.Bits, c.T)).ToList();
        }

        /// <summary>Whether this program can convert the format (see the header comment).</summary>
        public bool Supported
        {
            get
            {
                if (Kind == "bc") return Bc >= 1 && Bc <= 5;
                if (Special != null || ESize > 8) return false;
                return Channels.All(c => c.T == "x" || ((c.T == "unorm" || c.T == "snorm" || c.T == "uint" || c.T == "sint") && c.Bits <= 16));
            }
        }
    }

    public static class Dds
    {
        public const int ToolVersion = 3;
        const long DDSD_CAPS = 0x1, DDSD_HEIGHT = 0x2, DDSD_WIDTH = 0x4, DDSD_PITCH = 0x8, DDSD_PIXELFORMAT = 0x1000,
                   DDSD_MIPMAPCOUNT = 0x20000, DDSD_LINEARSIZE = 0x80000, DDSD_DEPTH = 0x800000;
        const long DDSCAPS_COMPLEX = 0x8, DDSCAPS_TEXTURE = 0x1000, DDSCAPS_MIPMAP = 0x400000;
        const long DDSCAPS2_CUBEMAP = 0x200, DDSCAPS2_VOLUME = 0x200000;
        static readonly long[] CubeFaceFlags = { 0x400, 0x800, 0x1000, 0x2000, 0x4000, 0x8000 };
        public static readonly string[] CubeFaceNames = { "posx", "negx", "posy", "negy", "posz", "negz" };
        const long DDPF_ALPHAPIXELS = 0x1, DDPF_ALPHA = 0x2, DDPF_FOURCC = 0x4, DDPF_PAL8 = 0x20, DDPF_RGB = 0x40, DDPF_YUV = 0x200,
                   DDPF_LUMINANCE = 0x20000, DDPF_BUMPLUMINANCE = 0x40000, DDPF_BUMPDUDV = 0x80000;
        const int DIM_1D = 2, DIM_2D = 3, DIM_3D = 4;
        const long MISC_TEXTURECUBE = 0x4;

        // ----------------------------------------------------------------------- formats
        public static readonly SortedDictionary<int, DdsFormat> Dxgi = BuildDxgi();

        static List<Chan> Seq(int bits, string t, string names) => names.Select((n, i) => new Chan(n.ToString(), i * bits, bits, t)).ToList();

        static SortedDictionary<int, DdsFormat> BuildDxgi()
        {
            var T = new SortedDictionary<int, DdsFormat>();
            void Raw(int num, string name, int es, List<Chan> ch, bool srgb = false, string special = null) =>
                T[num] = new DdsFormat { Name = name, Kind = "raw", ESize = es, Channels = ch, Srgb = srgb, Special = special };
            void Bc(int num, string name, int n, bool srgb = false, bool signed = false) =>
                T[num] = new DdsFormat { Name = name, Kind = "bc", Bc = n, Srgb = srgb, Signed = signed };
            foreach (var (n, t, s) in new[] { (1, "uint", "TYPELESS"), (2, "float", "FLOAT"), (3, "uint", "UINT"), (4, "sint", "SINT") }) Raw(n, "R32G32B32A32_" + s, 16, Seq(32, t, "rgba"));
            foreach (var (n, t, s) in new[] { (5, "uint", "TYPELESS"), (6, "float", "FLOAT"), (7, "uint", "UINT"), (8, "sint", "SINT") }) Raw(n, "R32G32B32_" + s, 12, Seq(32, t, "rgb"));
            foreach (var (n, t, s) in new[] { (9, "unorm", "TYPELESS"), (10, "float", "FLOAT"), (11, "unorm", "UNORM"), (12, "uint", "UINT"), (13, "snorm", "SNORM"), (14, "sint", "SINT") }) Raw(n, "R16G16B16A16_" + s, 8, Seq(16, t, "rgba"));
            foreach (var (n, t, s) in new[] { (15, "uint", "TYPELESS"), (16, "float", "FLOAT"), (17, "uint", "UINT"), (18, "sint", "SINT") }) Raw(n, "R32G32_" + s, 8, Seq(32, t, "rg"));
            Raw(19, "R32G8X24_TYPELESS", 8, new List<Chan> { new Chan("r", 0, 32, "float"), new Chan("g", 32, 8, "uint") });
            Raw(20, "D32_FLOAT_S8X24_UINT", 8, new List<Chan> { new Chan("r", 0, 32, "float"), new Chan("g", 32, 8, "uint") });
            Raw(21, "R32_FLOAT_X8X24_TYPELESS", 8, new List<Chan> { new Chan("r", 0, 32, "float") });
            Raw(22, "X32_TYPELESS_G8X24_UINT", 8, new List<Chan> { new Chan("g", 32, 8, "uint") });
            List<Chan> Rgb10a2(string t) => new List<Chan> { new Chan("r", 0, 10, t), new Chan("g", 10, 10, t), new Chan("b", 20, 10, t), new Chan("a", 30, 2, t) };
            Raw(23, "R10G10B10A2_TYPELESS", 4, Rgb10a2("unorm"));
            Raw(24, "R10G10B10A2_UNORM", 4, Rgb10a2("unorm"));
            Raw(25, "R10G10B10A2_UINT", 4, Rgb10a2("uint"));
            Raw(26, "R11G11B10_FLOAT", 4, new List<Chan> { new Chan("r", 0, 11, "uf11"), new Chan("g", 11, 11, "uf11"), new Chan("b", 22, 10, "uf10") });
            foreach (var (n, t, s, sr) in new[] { (27, "unorm", "TYPELESS", false), (28, "unorm", "UNORM", false), (29, "unorm", "UNORM_SRGB", true), (30, "uint", "UINT", false), (31, "snorm", "SNORM", false), (32, "sint", "SINT", false) })
                Raw(n, "R8G8B8A8_" + s, 4, Seq(8, t, "rgba"), sr);
            foreach (var (n, t, s) in new[] { (33, "unorm", "TYPELESS"), (34, "float", "FLOAT"), (35, "unorm", "UNORM"), (36, "uint", "UINT"), (37, "snorm", "SNORM"), (38, "sint", "SINT") }) Raw(n, "R16G16_" + s, 4, Seq(16, t, "rg"));
            foreach (var (n, t, s) in new[] { (39, "uint", "R32_TYPELESS"), (40, "float", "D32_FLOAT"), (41, "float", "R32_FLOAT"), (42, "uint", "R32_UINT"), (43, "sint", "R32_SINT") }) Raw(n, s, 4, new List<Chan> { new Chan("r", 0, 32, t) });
            Raw(44, "R24G8_TYPELESS", 4, new List<Chan> { new Chan("r", 0, 24, "unorm"), new Chan("g", 24, 8, "uint") });
            Raw(45, "D24_UNORM_S8_UINT", 4, new List<Chan> { new Chan("r", 0, 24, "unorm"), new Chan("g", 24, 8, "uint") });
            Raw(46, "R24_UNORM_X8_TYPELESS", 4, new List<Chan> { new Chan("r", 0, 24, "unorm") });
            Raw(47, "X24_TYPELESS_G8_UINT", 4, new List<Chan> { new Chan("g", 24, 8, "uint") });
            foreach (var (n, t, s) in new[] { (48, "unorm", "TYPELESS"), (49, "unorm", "UNORM"), (50, "uint", "UINT"), (51, "snorm", "SNORM"), (52, "sint", "SINT") }) Raw(n, "R8G8_" + s, 2, Seq(8, t, "rg"));
            foreach (var (n, t, s) in new[] { (53, "unorm", "R16_TYPELESS"), (54, "float", "R16_FLOAT"), (55, "unorm", "D16_UNORM"), (56, "unorm", "R16_UNORM"), (57, "uint", "R16_UINT"), (58, "snorm", "R16_SNORM"), (59, "sint", "R16_SINT") })
                Raw(n, s, 2, new List<Chan> { new Chan("r", 0, 16, t) });
            foreach (var (n, t, s) in new[] { (60, "unorm", "TYPELESS"), (61, "unorm", "UNORM"), (62, "uint", "UINT"), (63, "snorm", "SNORM"), (64, "sint", "SINT") }) Raw(n, "R8_" + s, 1, new List<Chan> { new Chan("r", 0, 8, t) });
            Raw(65, "A8_UNORM", 1, new List<Chan> { new Chan("a", 0, 8, "unorm") });
            Raw(67, "R9G9B9E5_SHAREDEXP", 4, new List<Chan>(), special: "rgb9e5");
            Bc(70, "BC1_TYPELESS", 1); Bc(71, "BC1_UNORM", 1); Bc(72, "BC1_UNORM_SRGB", 1, srgb: true);
            Bc(73, "BC2_TYPELESS", 2); Bc(74, "BC2_UNORM", 2); Bc(75, "BC2_UNORM_SRGB", 2, srgb: true);
            Bc(76, "BC3_TYPELESS", 3); Bc(77, "BC3_UNORM", 3); Bc(78, "BC3_UNORM_SRGB", 3, srgb: true);
            Bc(79, "BC4_TYPELESS", 4); Bc(80, "BC4_UNORM", 4); Bc(81, "BC4_SNORM", 4, signed: true);
            Bc(82, "BC5_TYPELESS", 5); Bc(83, "BC5_UNORM", 5); Bc(84, "BC5_SNORM", 5, signed: true);
            Raw(85, "B5G6R5_UNORM", 2, new List<Chan> { new Chan("b", 0, 5, "unorm"), new Chan("g", 5, 6, "unorm"), new Chan("r", 11, 5, "unorm") });
            Raw(86, "B5G5R5A1_UNORM", 2, new List<Chan> { new Chan("b", 0, 5, "unorm"), new Chan("g", 5, 5, "unorm"), new Chan("r", 10, 5, "unorm"), new Chan("a", 15, 1, "unorm") });
            List<Chan> Bgra() => new List<Chan> { new Chan("b", 0, 8, "unorm"), new Chan("g", 8, 8, "unorm"), new Chan("r", 16, 8, "unorm"), new Chan("a", 24, 8, "unorm") };
            List<Chan> Bgrx() => new List<Chan> { new Chan("b", 0, 8, "unorm"), new Chan("g", 8, 8, "unorm"), new Chan("r", 16, 8, "unorm"), new Chan("x", 24, 8, "x") };
            Raw(87, "B8G8R8A8_UNORM", 4, Bgra());
            Raw(88, "B8G8R8X8_UNORM", 4, Bgrx());
            Raw(89, "R10G10B10_XR_BIAS_A2_UNORM", 4, Rgb10a2("unorm"));
            Raw(90, "B8G8R8A8_TYPELESS", 4, Bgra());
            Raw(91, "B8G8R8A8_UNORM_SRGB", 4, Bgra(), true);
            Raw(92, "B8G8R8X8_TYPELESS", 4, Bgrx());
            Raw(93, "B8G8R8X8_UNORM_SRGB", 4, Bgrx(), true);
            Bc(94, "BC6H_TYPELESS", 6); Bc(95, "BC6H_UF16", 6); Bc(96, "BC6H_SF16", 6, signed: true);
            Bc(97, "BC7_TYPELESS", 7); Bc(98, "BC7_UNORM", 7); Bc(99, "BC7_UNORM_SRGB", 7, srgb: true);
            Raw(115, "B4G4R4A4_UNORM", 2, new List<Chan> { new Chan("b", 0, 4, "unorm"), new Chan("g", 4, 4, "unorm"), new Chan("r", 8, 4, "unorm"), new Chan("a", 12, 4, "unorm") });
            Raw(191, "A4B4G4R4_UNORM", 2, new List<Chan> { new Chan("a", 0, 4, "unorm"), new Chan("b", 4, 4, "unorm"), new Chan("g", 8, 4, "unorm"), new Chan("r", 12, 4, "unorm") });
            return T;
        }

        static readonly Dictionary<int, string> UnsupportedDxgi = new Dictionary<int, string>
        {
            { 66, "R1_UNORM" }, { 68, "R8G8_B8G8_UNORM" }, { 69, "G8R8_G8B8_UNORM" }, { 100, "AYUV" }, { 101, "Y410" },
            { 102, "Y416" }, { 103, "NV12" }, { 104, "P010" }, { 105, "P016" }, { 106, "420_OPAQUE" }, { 107, "YUY2" },
            { 108, "Y210" }, { 109, "Y216" }, { 110, "NV11" }, { 111, "AI44" }, { 112, "IA44" }, { 113, "P8" }, { 114, "A8P8" },
        };

        static uint FourCC(string s) => (uint)(s[0] | s[1] << 8 | s[2] << 16 | s[3] << 24);
        static string FourCCText(long v) => new string(new[] { (char)(v & 0xFF), (char)((v >> 8) & 0xFF), (char)((v >> 16) & 0xFF), (char)((v >> 24) & 0xFF) });

        static readonly Dictionary<string, int> FourCCToDxgi = new Dictionary<string, int>
        {
            { "DXT1", 71 }, { "DXT2", 74 }, { "DXT3", 74 }, { "DXT4", 77 }, { "DXT5", 77 }, { "RXGB", 77 },
            { "ATI1", 80 }, { "BC4U", 80 }, { "BC4S", 81 }, { "ATI2", 83 }, { "BC5U", 83 }, { "BC5S", 84 },
            { "BC6H", 95 }, { "BC7L", 98 }, { "BC7\0", 98 },
        };
        static readonly Dictionary<long, int> D3dfmtToDxgi = new Dictionary<long, int> { { 36, 11 }, { 110, 13 }, { 111, 54 }, { 112, 34 }, { 113, 10 }, { 114, 41 }, { 115, 16 }, { 116, 2 } };

        static (int off, int bits)? MaskInfo(long m)
        {
            if (m == 0) return null;
            int off = 0;
            while (((m >> off) & 1) == 0) off++;
            int bits = 0;
            long v = m >> off;
            while ((v & 1) != 0) { bits++; v >>= 1; }
            if (v != 0) throw new DdsError($"Non-contiguous bit mask 0x{m:X8} is not supported");
            return (off, bits);
        }

        public static (DdsFormat, string) FmtFromLegacy(JObj pf)
        {
            long flags = pf.Long("flags"), fourcc = pf.Long("fourcc");
            if ((flags & DDPF_FOURCC) != 0)
            {
                string fcc = FourCCText(fourcc);
                if (FourCCToDxgi.TryGetValue(fcc, out int dx)) return (Dxgi[dx], fcc.TrimEnd('\0'));
                if (D3dfmtToDxgi.TryGetValue(fourcc, out int dx2)) { var f = Dxgi[dx2]; return (f, $"D3DFMT_{fourcc} ({f.Name})"); }
                throw new DdsError($"Unsupported FourCC: {Text.PyRepr(fcc)} ({fourcc})");
            }
            if ((flags & (DDPF_YUV | DDPF_PAL8)) != 0) throw new DdsError("YUV / palettized formats are not supported");
            int bpp = (int)pf.Long("bitcount");
            if (bpp != 8 && bpp != 16 && bpp != 24 && bpp != 32) throw new DdsError($"Unsupported bits per pixel: {bpp}");
            long[] masks = { pf.Long("rmask"), pf.Long("gmask"), pf.Long("bmask"), pf.Long("amask") };
            string[] types, names;
            if ((flags & DDPF_BUMPDUDV) != 0)
            {
                var ai = MaskInfo(masks[3]);
                types = new[] { "snorm", "snorm", "snorm", bpp == 32 && ai != null && ai.Value.bits == 8 ? "snorm" : "unorm" };
                names = new[] { "r", "g", "b", "a" };
            }
            else if ((flags & DDPF_BUMPLUMINANCE) != 0) { types = new[] { "snorm", "snorm", "unorm", "unorm" }; names = new[] { "r", "g", "b", "a" }; }
            else if ((flags & DDPF_LUMINANCE) != 0) { types = new[] { "unorm", "unorm", "unorm", "unorm" }; names = new[] { "l", null, null, "a" }; }
            else if ((flags & DDPF_ALPHA) != 0 && (flags & DDPF_RGB) == 0) { types = new[] { "unorm", "unorm", "unorm", "unorm" }; names = new string[] { null, null, null, "a" }; }
            else { types = new[] { "unorm", "unorm", "unorm", "unorm" }; names = new[] { "r", "g", "b", "a" }; }
            if ((flags & (DDPF_ALPHAPIXELS | DDPF_ALPHA)) == 0 && (flags & DDPF_BUMPDUDV) == 0) names[3] = null;
            long full = (1L << bpp) - 1;
            // some tools write masks shifted outside the pixel bit range
            masks = masks.Select(m => (m & ~full) != 0 && bpp < 32 ? m >> (32 - bpp) : m).ToArray();
            var chans = new List<Chan>();
            long used = 0;
            for (int k = 0; k < 4; k++)
            {
                if (names[k] == null || masks[k] == 0) continue;
                var mi = MaskInfo(masks[k]).Value;
                chans.Add(new Chan(names[k], mi.off, mi.bits, types[k]));
                used |= masks[k];
            }
            if (chans.Count == 0) throw new DdsError("Pixel format has no channels");
            long free = full & ~used;
            if (free != 0 && !chans.Any(c => c.N == "a"))
            {
                int off = 0;
                while (((free >> off) & 1) == 0) off++;
                int bits = 0; long v = free >> off;
                while ((v & 1) != 0) { bits++; v >>= 1; }
                if (v == 0) chans.Add(new Chan("x", off, bits, "x"));
            }
            string desc = $"legacy {bpp}bpp " + string.Concat(chans.OrderByDescending(c => c.Off).Select(c => c.N.ToUpperInvariant() + c.Bits));
            return (new DdsFormat { Name = desc, Kind = "raw", ESize = bpp / 8, Channels = chans }, desc);
        }

        // ----------------------------------------------------------------------- DDS file
        public class DdsFile
        {
            public JObj Header, Dx10;
            public DdsFormat Fmt;
            public string FmtDesc;
            public int W, H, D, Mips, ArraySize, Dim;
            public List<int> Faces;
            public bool Volume, Cubemap;
            public byte[] Data;
            public int Offset;
            public long DataLength => Data.Length - Offset;
        }

        static long U32(byte[] d, int o) => (uint)(d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24);

        public static DdsFile ReadDds(string path) => ParseDds(File.ReadAllBytes(path), path);

        public static DdsFile ParseDds(byte[] data, string path = "")
        {
            if (data.Length < 128 || data[0] != 'D' || data[1] != 'D' || data[2] != 'S' || data[3] != ' ') throw new DdsError($"Not a DDS file: {path}");
            long flags = U32(data, 8), height = U32(data, 12), width = U32(data, 16), pitch = U32(data, 20), depth = U32(data, 24), mips = U32(data, 28);
            var reserved1 = Enumerable.Range(0, 11).Select(i => (object)U32(data, 32 + 4 * i)).ToList();
            var pf = new JObj();
            string[] pfn = { "size", "flags", "fourcc", "bitcount", "rmask", "gmask", "bmask", "amask" };
            for (int i = 0; i < 8; i++) pf[pfn[i]] = U32(data, 76 + 4 * i);
            long caps = U32(data, 108), caps2 = U32(data, 112), caps3 = U32(data, 116), caps4 = U32(data, 120), reserved2 = U32(data, 124);
            var hdr = new JObj();
            hdr["flags"] = flags; hdr["pitch_or_linear_size"] = pitch; hdr["reserved1"] = reserved1; hdr["pixel_format"] = pf;
            hdr["caps"] = caps; hdr["caps2"] = caps2; hdr["caps3"] = caps3; hdr["caps4"] = caps4; hdr["reserved2"] = reserved2;
            hdr["mip_map_count"] = mips; hdr["depth_field"] = depth;
            int offset = 128;
            JObj dx10 = null;
            DdsFormat fmt; string desc;
            if ((pf.Long("flags") & DDPF_FOURCC) != 0 && pf.Long("fourcc") == FourCC("DX10"))
            {
                if (data.Length < 148) throw new DdsError($"The DDS file is truncated: {path}");
                dx10 = new JObj();
                dx10["dxgi_format"] = U32(data, 128); dx10["resource_dimension"] = U32(data, 132); dx10["misc_flag"] = U32(data, 136);
                dx10["array_size"] = U32(data, 140); dx10["misc_flags2"] = U32(data, 144);
                offset = 148;
                int dxgi = (int)dx10.Long("dxgi_format");
                if (UnsupportedDxgi.TryGetValue(dxgi, out string un)) throw new DdsError($"DXGI format {un} is not supported");
                if (!Dxgi.TryGetValue(dxgi, out fmt)) throw new DdsError($"Unknown DXGI format: {dxgi}");
                desc = fmt.Name;
            }
            else (fmt, desc) = FmtFromLegacy(pf);
            var t = new DdsFile { Header = hdr, Dx10 = dx10, Fmt = fmt, FmtDesc = desc, Data = data, Offset = offset };
            t.W = (int)Math.Max(1, width); t.H = (int)Math.Max(1, height); t.Mips = (int)Math.Max(1, mips);
            t.ArraySize = 1; t.Faces = new List<int> { 0 }; t.Volume = false; t.Dim = DIM_2D;
            int dpt;
            if (dx10 != null)
            {
                t.Dim = (int)dx10.Long("resource_dimension");
                t.ArraySize = (int)Math.Max(1, dx10.Long("array_size"));
                if (t.Dim == DIM_3D) { t.Volume = true; dpt = (int)Math.Max(1, depth); t.ArraySize = 1; }
                else
                {
                    dpt = 1;
                    if (t.Dim == DIM_1D) t.H = 1;
                    if ((dx10.Long("misc_flag") & MISC_TEXTURECUBE) != 0) t.Faces = Enumerable.Range(0, 6).ToList();
                }
            }
            else
            {
                if ((caps2 & DDSCAPS2_VOLUME) != 0) { t.Volume = true; t.Dim = DIM_3D; dpt = (int)Math.Max(1, depth); }
                else
                {
                    dpt = 1;
                    if ((caps2 & DDSCAPS2_CUBEMAP) != 0)
                    {
                        t.Faces = Enumerable.Range(0, 6).Where(i => (caps2 & CubeFaceFlags[i]) != 0).ToList();
                        if (t.Faces.Count == 0) t.Faces = Enumerable.Range(0, 6).ToList();
                    }
                }
            }
            t.D = dpt;
            t.Cubemap = t.Faces.Count > 1 || (dx10 != null && (dx10.Long("misc_flag") & MISC_TEXTURECUBE) != 0) || (caps2 & DDSCAPS2_CUBEMAP) != 0;
            return t;
        }

        static (int, int, int) MipDims(int w, int h, int d, int level) => (Math.Max(1, w >> level), Math.Max(1, h >> level), Math.Max(1, d >> level));

        public static int FullMipCount(int w, int h, int d = 1) => (int)Math.Floor(Math.Log(Math.Max(w, Math.Max(h, d)), 2) + 1e-9) + 1;

        public struct Surface { public int Array, Face, Mip, Slice, W, H; public long Off; }

        public static List<Surface> IterateSurfaces(DdsFile dds, out long total)
        {
            var o = new List<Surface>();
            long off = 0;
            if (dds.Volume)
            {
                for (int m = 0; m < dds.Mips; m++)
                {
                    var (w, h, d) = MipDims(dds.W, dds.H, dds.D, m);
                    long sz = dds.Fmt.SurfaceSize(w, h);
                    for (int z = 0; z < d; z++) { o.Add(new Surface { Mip = m, Slice = z, Off = off, W = w, H = h }); off += sz; }
                }
            }
            else
            {
                for (int a = 0; a < dds.ArraySize; a++)
                    foreach (int face in dds.Faces)
                        for (int m = 0; m < dds.Mips; m++)
                        {
                            var (w, h, _) = MipDims(dds.W, dds.H, 1, m);
                            o.Add(new Surface { Array = a, Face = face, Mip = m, Off = off, W = w, H = h });
                            off += dds.Fmt.SurfaceSize(w, h);
                        }
            }
            total = off;
            return o;
        }

        // ----------------------------------------------------------------------- uncompressed
        static ulong Extract(byte[] buf, long po, int es, int off, int bits)
        {
            ulong word = 0;
            for (int k = 0; k < es && k < 8; k++) word |= (ulong)buf[po + k] << (8 * k);
            return (word >> off) & (bits == 64 ? ulong.MaxValue : ((1UL << bits) - 1));
        }

        static long SignExtend(ulong raw, int bits) { long v = (long)raw; return v - (((v >> (bits - 1)) & 1) << bits); }

        static double RawToValue(ulong raw, int bits, string ct)
        {
            switch (ct)
            {
                case "unorm": return (float)((double)raw / ((1L << bits) - 1));
                case "snorm": return (float)Math.Max((double)SignExtend(raw, bits) / ((1L << (bits - 1)) - 1), -1.0);
                case "uint": return raw;
                case "sint": return SignExtend(raw, bits);
            }
            throw new DdsError($"channel type {ct} is not supported");
        }

        static ulong ValueToRaw(double v, int bits, string ct)
        {
            ulong mask = (1UL << bits) - 1;
            switch (ct)
            {
                case "unorm": return (ulong)Math.Round(Clip(v, 0, 1) * mask, MidpointRounding.ToEven);
                case "snorm":
                    long s = (long)Math.Round(Clip(v, -1, 1) * ((1L << (bits - 1)) - 1), MidpointRounding.ToEven);
                    return (ulong)s & mask;
                case "uint": return (ulong)Clip(Math.Round(v, MidpointRounding.ToEven), 0, mask);
                case "sint":
                    long si = (long)Clip(Math.Round(v, MidpointRounding.ToEven), -(1L << (bits - 1)), (1L << (bits - 1)) - 1);
                    return (ulong)si & mask;
                case "x": return mask;
            }
            throw new DdsError($"channel type {ct} is not supported");
        }

        static double Clip(double v, double lo, double hi) => double.IsNaN(v) ? lo : (v < lo ? lo : (v > hi ? hi : v));

        /// <summary>Channel values of one surface: name -> w*h values (unorm 0..1, snorm -1..1, integers as is).</summary>
        public static Dictionary<string, double[]> RawDecode(byte[] buf, long off, DdsFormat fmt, int w, int h)
        {
            if (!fmt.Supported) throw new DdsError($"format {fmt.Name} is not supported by this program");
            int n = w * h;
            var o = new Dictionary<string, double[]>();
            foreach (var c in fmt.Channels)
            {
                if (c.T == "x") continue;
                var a = new double[n];
                for (int i = 0; i < n; i++) a[i] = RawToValue(Extract(buf, off + (long)i * fmt.ESize, fmt.ESize, c.Off, c.Bits), c.Bits, c.T);
                o[c.N] = a;
            }
            return o;
        }

        public static byte[] RawEncode(Dictionary<string, double[]> vals, DdsFormat fmt, int w, int h)
        {
            int n = w * h, es = fmt.ESize;
            var o = new byte[n * es];
            for (int i = 0; i < n; i++)
            {
                ulong word = 0;
                foreach (var c in fmt.Channels)
                {
                    double v = c.T != "x" ? vals[c.N][i] : 0;
                    ulong raw = ValueToRaw(v, c.Bits, c.T) & ((1UL << c.Bits) - 1);
                    word |= raw << c.Off;
                }
                for (int k = 0; k < es; k++) o[i * es + k] = (byte)(word >> (8 * k));
            }
            return o;
        }

        // ----------------------------------------------------------------------- BC decoding
        static int[] Expand565(int c)
        {
            int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
            return new[] { (r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2) };
        }

        static double Rint(double v) => Math.Round(v, MidpointRounding.ToEven);

        /// <summary>BC1 palette (4 entries, RGBA 0..255).</summary>
        static double[][] Bc1Palette(int c0, int c1, bool fourOnly)
        {
            var e0 = Expand565(c0); var e1 = Expand565(c1);
            bool four = c0 > c1 || fourOnly;
            var pal = new double[4][];
            pal[0] = new double[] { e0[0], e0[1], e0[2], 255 };
            pal[1] = new double[] { e1[0], e1[1], e1[2], 255 };
            pal[2] = new double[4]; pal[3] = new double[4];
            for (int k = 0; k < 3; k++)
            {
                pal[2][k] = Rint(four ? (2.0f * e0[k] + e1[k]) / 3.0f : (e0[k] + e1[k]) / 2.0f);
                pal[3][k] = Rint(four ? (e0[k] + 2.0f * e1[k]) / 3.0f : 0);
            }
            pal[2][3] = 255; pal[3][3] = four ? 255 : 0;
            return pal;
        }

        static void Bc1DecodeBlock(byte[] b, long o, bool fourOnly, double[][] px16)
        {
            int c0 = b[o] | b[o + 1] << 8, c1 = b[o + 2] | b[o + 3] << 8;
            long idx = U32(b, (int)o + 4);
            var pal = Bc1Palette(c0, c1, fourOnly);
            for (int i = 0; i < 16; i++)
            {
                var p = pal[(idx >> (2 * i)) & 3];
                for (int k = 0; k < 4; k++) px16[i][k] = p[k] / 255.0;
            }
        }

        static double[] Bc4Palette(double r0, double r1, bool signed)
        {
            double lo = signed ? -127.0 : 0.0, hi = signed ? 127.0 : 255.0;
            bool eight = r0 > r1;
            var pal = new double[8];
            pal[0] = r0; pal[1] = r1;
            for (int i = 2; i < 8; i++)
            {
                double p8 = (float)(((8 - i) * (float)r0 + (i - 1) * (float)r1) / 7.0f);
                double p6 = i <= 5 ? (float)(((6 - i) * (float)r0 + (i - 1) * (float)r1) / 5.0f) : (i == 6 ? lo : hi);
                pal[i] = eight ? p8 : p6;
            }
            return pal;
        }

        static void Bc4DecodeBlock(byte[] b, long o, bool signed, double[] out16)
        {
            double r0, r1;
            if (signed) { r0 = Math.Max((int)(sbyte)b[o], -127); r1 = Math.Max((int)(sbyte)b[o + 1], -127); }
            else { r0 = b[o]; r1 = b[o + 1]; }
            ulong w = 0;
            for (int k = 2; k < 8; k++) w |= (ulong)b[o + k] << (8 * (k - 2));
            var pal = Bc4Palette(r0, r1, signed);
            for (int i = 0; i < 16; i++) out16[i] = (float)((float)pal[(w >> (3 * i)) & 7] / (signed ? 127.0f : 255.0f));
        }

        public static Dictionary<string, double[]> BcDecode(byte[] buf, long off, DdsFormat fmt, int w, int h)
        {
            if (!fmt.Supported) throw new DdsError($"format {fmt.Name} is not supported by this program (BC6H / BC7 need another tool)");
            int bh = Math.Max(1, (h + 3) / 4), bw = Math.Max(1, (w + 3) / 4);
            int bs = fmt.BlockBytes, n = fmt.Bc;
            int nch = n <= 3 ? 4 : (n == 4 ? 1 : 2);
            var planes = new double[nch][];
            for (int k = 0; k < nch; k++) planes[k] = new double[w * h];
            var px = new double[16][];
            for (int i = 0; i < 16; i++) px[i] = new double[4];
            var a16 = new double[16]; var g16 = new double[16];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    long o = off + ((long)by * bw + bx) * bs;
                    if (n == 1) Bc1DecodeBlock(buf, o, false, px);
                    else if (n == 2)
                    {
                        Bc1DecodeBlock(buf, o + 8, true, px);
                        ulong a = 0;
                        for (int k = 0; k < 8; k++) a |= (ulong)buf[o + k] << (8 * k);
                        for (int i = 0; i < 16; i++) px[i][3] = ((a >> (4 * i)) & 15) / 15.0;
                    }
                    else if (n == 3)
                    {
                        Bc1DecodeBlock(buf, o + 8, true, px);
                        Bc4DecodeBlock(buf, o, false, a16);
                        for (int i = 0; i < 16; i++) px[i][3] = a16[i];
                    }
                    else if (n == 4) { Bc4DecodeBlock(buf, o, fmt.Signed, a16); for (int i = 0; i < 16; i++) px[i][0] = a16[i]; }
                    else
                    {
                        Bc4DecodeBlock(buf, o, fmt.Signed, a16); Bc4DecodeBlock(buf, o + 8, fmt.Signed, g16);
                        for (int i = 0; i < 16; i++) { px[i][0] = a16[i]; px[i][1] = g16[i]; }
                    }
                    for (int i = 0; i < 16; i++)
                    {
                        int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
                        if (x >= w || y >= h) continue;
                        for (int k = 0; k < nch; k++) planes[k][y * w + x] = (float)px[i][k];
                    }
                }
            var res = new Dictionary<string, double[]>();
            for (int k = 0; k < nch; k++) res["rgba"[k].ToString()] = planes[k];
            return res;
        }

        public static Dictionary<string, double[]> DecodeSurface(DdsFile dds, long off, int w, int h)
        {
            long sz = dds.Fmt.SurfaceSize(w, h);
            if (dds.Offset + off + sz > dds.Data.Length) throw new DdsError("The DDS file is truncated (texture data missing)");
            return dds.Fmt.Kind == "bc" ? BcDecode(dds.Data, dds.Offset + off, dds.Fmt, w, h) : RawDecode(dds.Data, dds.Offset + off, dds.Fmt, w, h);
        }

        // ----------------------------------------------------------------------- BC encoding
        // Per-block versions of the encoders of dds_tool.py: principal-axis endpoints refined
        // twice by least squares (BC1), plus the 6-value mode for BC4 (alpha masks).
        static void PcaEndpoints(double[][] px, bool[] mask, int dim, out double[] e0, out double[] e1)
        {
            double cnt = 0;
            var mean = new double[dim];
            for (int i = 0; i < 16; i++) if (mask[i]) { cnt++; for (int k = 0; k < dim; k++) mean[k] += px[i][k]; }
            for (int k = 0; k < dim; k++) mean[k] /= Math.Max(cnt, 1);
            var cov = new double[dim, dim];
            for (int i = 0; i < 16; i++)
            {
                if (!mask[i]) continue;
                for (int a = 0; a < dim; a++) for (int b = 0; b < dim; b++) cov[a, b] += (px[i][a] - mean[a]) * (px[i][b] - mean[b]);
            }
            int kmax = 0;
            for (int k = 1; k < dim; k++) if (cov[k, k] > cov[kmax, kmax]) kmax = k;
            var axis = new double[dim];
            for (int k = 0; k < dim; k++) axis[k] = cov[kmax, k];
            for (int it = 0; it < 6; it++)
            {
                var na = new double[dim];
                for (int a = 0; a < dim; a++) for (int b = 0; b < dim; b++) na[a] += cov[a, b] * axis[b];
                double nrm = Math.Sqrt(na.Sum(v => v * v));
                for (int a = 0; a < dim; a++) axis[a] = na[a] / Math.Max(nrm, 1e-12);
            }
            double tmin = double.PositiveInfinity, tmax = double.NegativeInfinity;
            for (int i = 0; i < 16; i++)
            {
                if (!mask[i]) continue;
                double t = 0;
                for (int k = 0; k < dim; k++) t += (px[i][k] - mean[k]) * axis[k];
                tmin = Math.Min(tmin, t); tmax = Math.Max(tmax, t);
            }
            if (double.IsInfinity(tmin)) tmin = 0;
            if (double.IsInfinity(tmax)) tmax = 0;
            e0 = new double[dim]; e1 = new double[dim];
            for (int k = 0; k < dim; k++) { e0[k] = mean[k] + tmin * axis[k]; e1[k] = mean[k] + tmax * axis[k]; }
        }

        static void LsEndpoints(double[][] px, double[] t, bool[] mask, int dim, double[] e0, double[] e1, out double[] n0, out double[] n1)
        {
            double a = 0, b = 0, c = 0;
            var r0 = new double[dim]; var r1 = new double[dim];
            for (int i = 0; i < 16; i++)
            {
                if (!mask[i]) continue;
                double s = 1 - t[i];
                a += s * s; b += s * t[i]; c += t[i] * t[i];
                for (int k = 0; k < dim; k++) { r0[k] += s * px[i][k]; r1[k] += t[i] * px[i][k]; }
            }
            double det = a * c - b * b;
            n0 = new double[dim]; n1 = new double[dim];
            if (Math.Abs(det) > 1e-8)
                for (int k = 0; k < dim; k++) { n0[k] = (c * r0[k] - b * r1[k]) / det; n1[k] = (a * r1[k] - b * r0[k]) / det; }
            else { Array.Copy(e0, n0, dim); Array.Copy(e1, n1, dim); }
        }

        static int Q565(double[] e)
        {
            int r = (int)Rint(Clip(e[0], 0, 255) * 31 / 255), g = (int)Rint(Clip(e[1], 0, 255) * 63 / 255), b = (int)Rint(Clip(e[2], 0, 255) * 31 / 255);
            return (r << 11) | (g << 5) | b;
        }

        static void Assign(double[][] px, double[][] pal, int npal, bool[] mask, int[] idx, out double err)
        {
            err = 0;
            for (int i = 0; i < 16; i++)
            {
                int best = 0; double bd = double.MaxValue;
                for (int p = 0; p < npal; p++)
                {
                    double d = 0;
                    for (int k = 0; k < 3; k++) { double x = px[i][k] - pal[p][k]; d += x * x; }
                    if (d < bd) { bd = d; best = p; }
                }
                idx[i] = best;
                if (mask == null || mask[i]) err += bd;
            }
        }

        /// <summary>px: 16 pixels RGB 0..255; transparent: 16 flags or null. Writes 8 bytes.</summary>
        static void Bc1Color(double[][] px, bool[] transparent, byte[] dst, int dpos)
        {
            var mask = new bool[16];
            bool three = false;
            for (int i = 0; i < 16; i++) { mask[i] = transparent == null || !transparent[i]; if (transparent != null && transparent[i]) three = true; }
            double[] T4 = { 0, 1, 1.0 / 3, 2.0 / 3 }, T3 = { 0, 1, 0.5, 0 };
            int bc0 = 0, bc1 = 0; var bidx = new int[16]; double berr = double.MaxValue;
            void Evaluate(double[] e0, double[] e1, bool first)
            {
                int c0 = Q565(e0), c1 = Q565(e1);
                int a, b; var idx = new int[16]; double err;
                if (three)
                {
                    var pal3 = Bc1Palette(Math.Min(c0, c1), Math.Max(c0, c1), false);
                    Assign(px, pal3, 3, mask, idx, out err);
                    a = Math.Min(c0, c1); b = Math.Max(c0, c1);
                }
                else
                {
                    var pal4 = Bc1Palette(Math.Max(c0, c1), Math.Min(c0, c1), true);
                    Assign(px, pal4, 4, mask, idx, out err);
                    a = Math.Max(c0, c1); b = Math.Min(c0, c1);
                }
                if (first || err < berr) { bc0 = a; bc1 = b; bidx = idx; berr = err; }
            }
            PcaEndpoints(px, mask, 3, out var p0, out var p1);
            Evaluate(p0, p1, true);
            for (int it = 0; it < 2; it++)
            {
                var t = bidx.Select(k => three ? T3[k] : T4[k]).ToArray();
                var ex0 = Expand565(bc0).Select(v => (double)v).ToArray(); var ex1 = Expand565(bc1).Select(v => (double)v).ToArray();
                LsEndpoints(px, t, mask, 3, ex0, ex1, out var n0, out var n1);
                Evaluate(n0, n1, false);
            }
            var fin = (int[])bidx.Clone();
            if (transparent != null) for (int i = 0; i < 16; i++) if (transparent[i]) fin[i] = 3;
            if (bc0 == bc1 && !three) for (int i = 0; i < 16; i++) fin[i] = 0;
            uint word = 0;
            for (int i = 0; i < 16; i++) word |= (uint)fin[i] << (2 * i);
            dst[dpos] = (byte)bc0; dst[dpos + 1] = (byte)(bc0 >> 8); dst[dpos + 2] = (byte)bc1; dst[dpos + 3] = (byte)(bc1 >> 8);
            dst[dpos + 4] = (byte)word; dst[dpos + 5] = (byte)(word >> 8); dst[dpos + 6] = (byte)(word >> 16); dst[dpos + 7] = (byte)(word >> 24);
        }

        /// <summary>px: 16 values 0..255 (unorm) or -127..127 (snorm). Writes 8 bytes.</summary>
        static void Bc4(double[] pxIn, bool signed, byte[] dst, int dpos)
        {
            double lo = signed ? -127 : 0, hi = signed ? 127 : 255;
            var px = pxIn.Select(v => Clip(v, lo, hi)).ToArray();
            double[] TT = { 0, 1, 1 / 7.0, 2 / 7.0, 3 / 7.0, 4 / 7.0, 5 / 7.0, 6 / 7.0 };
            long br0 = 0, br1 = 0; var bidx = new int[16]; double berr = double.MaxValue;
            void Evaluate(double ea, double eb, bool six, bool first)
            {
                long a = (long)Clip(Rint(ea), lo, hi), b = (long)Clip(Rint(eb), lo, hi);
                long r0 = six ? Math.Min(a, b) : Math.Max(a, b), r1 = six ? Math.Max(a, b) : Math.Min(a, b);
                var pal = Bc4Palette(r0, r1, signed);
                var idx = new int[16]; double err = 0;
                for (int i = 0; i < 16; i++)
                {
                    int best = 0; double bd = double.MaxValue;
                    for (int p = 0; p < 8; p++) { double d = (px[i] - pal[p]) * (px[i] - pal[p]); if (d < bd) { bd = d; best = p; } }
                    idx[i] = best; err += bd;
                }
                if (first || err < berr) { br0 = r0; br1 = r1; bidx = idx; berr = err; }
            }
            double mn = px.Min(), mx = px.Max();
            Evaluate(mx, mn, false, true);
            var t = bidx.Select(k => TT[k]).ToArray();
            var pxs = px.Select(v => new[] { v }).ToArray();
            LsEndpoints(pxs, t, Enumerable.Repeat(true, 16).ToArray(), 1, new double[] { br0 }, new double[] { br1 }, out var n0, out var n1);
            Evaluate(n0[0], n1[0], false, false);
            // 6-value mode (with exact extremes) - useful for alpha masks
            var inner = px.Where(v => v > lo && v < hi).ToList();
            double imn = inner.Count > 0 ? inner.Min() : mn, imx = inner.Count > 0 ? inner.Max() : mx;
            Evaluate(imn, imx, true, false);
            ulong word = 0;
            for (int i = 0; i < 16; i++) word |= (ulong)bidx[i] << (3 * i);
            word = (word << 16) | (((ulong)br1 & 0xFF) << 8) | ((ulong)br0 & 0xFF);
            for (int k = 0; k < 8; k++) dst[dpos + k] = (byte)(word >> (8 * k));
        }

        public static byte[] BcEncode(Dictionary<string, double[]> vals, DdsFormat fmt, int w, int h, Dictionary<string, object> opts)
        {
            if (!fmt.Supported) throw new DdsError($"format {fmt.Name} is not supported by this program (BC6H / BC7 need another tool)");
            int n = fmt.Bc;
            int bh = (h + 3) / 4, bw = (w + 3) / 4;
            int bs = fmt.BlockBytes;
            var o = new byte[Math.Max(1, bh) * Math.Max(1, bw) * bs];
            double Get(string c, int x, int y, double def)
            {
                x = Math.Min(x, w - 1); y = Math.Min(y, h - 1);   // edge padding
                return vals.TryGetValue(c, out var a) ? a[y * w + x] : def;
            }
            bool bc1Alpha = opts != null && opts.TryGetValue("bc1_alpha", out var ba) && ba is bool bb && bb;
            var px = new double[16][];
            for (int i = 0; i < 16; i++) px[i] = new double[4];
            var ch = new double[16];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int dpos = (by * bw + bx) * bs;
                    for (int i = 0; i < 16; i++)
                    {
                        int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
                        if (n <= 3)
                        {
                            for (int k = 0; k < 4; k++) px[i][k] = Clip(Get("rgba"[k].ToString(), x, y, k == 3 ? 1.0 : 0.0), 0, 1) * 255;
                        }
                        else
                        {
                            double sc = fmt.Signed ? 127.0 : 255.0;
                            px[i][0] = Get("r", x, y, 0) * sc;
                            if (n == 5) px[i][1] = Get("g", x, y, 0) * sc;
                        }
                    }
                    if (n == 1)
                    {
                        bool[] tr = null;
                        if (bc1Alpha) tr = px.Select(p => p[3] < 128).ToArray();
                        Bc1Color(px, tr, o, dpos);
                    }
                    else if (n == 2)
                    {
                        ulong word = 0;
                        for (int i = 0; i < 16; i++) word |= (ulong)Clip(Rint(px[i][3] * 15 / 255), 0, 15) << (4 * i);
                        for (int k = 0; k < 8; k++) o[dpos + k] = (byte)(word >> (8 * k));
                        Bc1Color(px, null, o, dpos + 8);
                    }
                    else if (n == 3)
                    {
                        for (int i = 0; i < 16; i++) ch[i] = px[i][3];
                        Bc4(ch, false, o, dpos);
                        Bc1Color(px, null, o, dpos + 8);
                    }
                    else if (n == 4) { for (int i = 0; i < 16; i++) ch[i] = px[i][0]; Bc4(ch, fmt.Signed, o, dpos); }
                    else
                    {
                        for (int i = 0; i < 16; i++) ch[i] = px[i][0];
                        Bc4(ch, fmt.Signed, o, dpos);
                        for (int i = 0; i < 16; i++) ch[i] = px[i][1];
                        Bc4(ch, fmt.Signed, o, dpos + 8);
                    }
                }
            return o;
        }

        // ----------------------------------------------------------------------- image <-> values
        public static (string kind, List<string> layout) ImagePlan(DdsFormat fmt, bool bc1Alpha = true)
        {
            var vch = fmt.ValueChannels();
            if (fmt.Kind == "bc" && fmt.Bc == 1 && !bc1Alpha) vch = vch.Take(3).ToList();
            var names = vch.Select(c => c.n).ToList();
            List<string> layout;
            if (names.Count == 1) layout = new List<string> { names[0] };
            else if (names.Count == 2 && names.Contains("l") && names.Contains("a")) layout = new List<string> { "l", "a" };
            else
            {
                layout = new List<string> { "r", "g", "b" };
                if (names.Contains("a")) layout.Add("a");
                layout = layout.Select(c => names.Contains(c) ? c : null).ToList();
            }
            bool floaty = vch.Any(c => c.t == "float" || c.t == "uf11" || c.t == "uf10" || ((c.t == "unorm" || c.t == "snorm") && c.b > 16));
            if (fmt.Special == "rgb9e5" || (fmt.Kind == "bc" && fmt.Bc == 6)) floaty = true;
            string kind;
            if (floaty) kind = "exr_float";
            else if (vch.Any(c => c.b > 16)) kind = "exr_uint";
            else if (vch.Max(c => c.b) > 8) kind = "png16";
            else kind = "png8";
            return (kind, layout);
        }

        static Dictionary<string, (int b, string t)> ChanInfo(DdsFormat fmt) => fmt.ValueChannels().ToDictionary(c => c.n, c => (c.b, c.t));

        public static Img ValuesToImage(Dictionary<string, double[]> vals, DdsFormat fmt, string kind, List<string> layout, int h, int w)
        {
            if (kind != "png8" && kind != "png16") throw new DdsError("EXR images (floating point / 32-bit formats) are not supported by this program");
            var info = ChanInfo(fmt);
            var img = new Img(w, h, layout.Count, kind == "png16");
            int max = img.Max;
            for (int c = 0; c < layout.Count; c++)
            {
                string name = layout[c];
                if (name == null || !vals.ContainsKey(name)) continue;
                var v = vals[name];
                var (b, t) = info[name];
                for (int i = 0; i < w * h; i++)
                {
                    double p;
                    switch (t)
                    {
                        case "unorm": p = v[i]; break;
                        case "snorm":
                            double s = Rint(Clip(v[i], -1, 1) * ((1L << (b - 1)) - 1));
                            p = (s + (1L << (b - 1))) / ((1L << b) - 1);
                            break;
                        case "uint": p = v[i] / ((1L << b) - 1); break;
                        default: p = (v[i] + (1L << (b - 1))) / ((1L << b) - 1); break;
                    }
                    img.Px[i * layout.Count + c] = (ushort)Rint(Clip(p, 0, 1) * max);
                }
            }
            return img;
        }

        public static Dictionary<string, double[]> ImageToValues(Img img, DdsFormat fmt, string kind, List<string> layout)
        {
            var info = ChanInfo(fmt);
            int C = img.C, n = img.W * img.H;
            double P(int ch, int i) => (double)img.Px[i * C + ch] / img.Max;
            var planes = new List<Func<int, double>>();
            int want = layout.Count;
            if (C == want) for (int k = 0; k < want; k++) { int kk = k; planes.Add(i => P(kk, i)); }
            else if (want == 1) planes.Add(i => P(0, i));
            else if (want == 2) { planes.Add(i => P(0, i)); planes.Add(C == 2 || C == 4 ? (Func<int, double>)(i => P(C - 1, i)) : (i => 1.0)); }
            else if (C <= 2)
            {
                planes.Add(i => P(0, i)); planes.Add(i => P(0, i)); planes.Add(i => P(0, i));
                if (want == 4) planes.Add(C == 2 ? (Func<int, double>)(i => P(1, i)) : (i => 1.0));
            }
            else
            {
                planes.Add(i => P(0, i)); planes.Add(i => P(1, i)); planes.Add(i => P(2, i));
                if (want == 4) planes.Add(C >= 4 ? (Func<int, double>)(i => P(3, i)) : (i => 1.0));
            }
            var vals = new Dictionary<string, double[]>();
            for (int k = 0; k < layout.Count; k++)
            {
                string name = layout[k];
                if (name == null) continue;
                var (b, t) = info[name];
                var f = planes[k];
                var a = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double p = Clip(f(i), 0, 1);
                    switch (t)
                    {
                        case "unorm": a[i] = p; break;
                        case "snorm": a[i] = Math.Max((Rint(p * ((1L << b) - 1)) - (1L << (b - 1))) / ((1L << (b - 1)) - 1), -1); break;
                        case "uint": a[i] = Rint(p * ((1L << b) - 1)); break;
                        default: a[i] = Rint(p * ((1L << b) - 1)) - (1L << (b - 1)); break;
                    }
                }
                vals[name] = a;
            }
            return vals;
        }

        public static Img LoadImage(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            byte[] data = File.ReadAllBytes(path);
            if (Png.IsPng(data)) return Png.Decode(data, Path.GetFileName(path));
            if (ext == ".exr" || ext == ".tif" || ext == ".tiff") throw new DdsError($"{Path.GetFileName(path)}: EXR and TIFF images are not supported by this program (use PNG)");
            try
            {
                var tex = TexLoad.Decode(data, path);
                var img = new Img(tex.Width, tex.Height, 4, false);
                for (int i = 0; i < tex.Rgba.Length; i++) img.Px[i] = tex.Rgba[i];
                return img;
            }
            catch (TextureError e) { throw new DdsError(e.Message); }
        }

        // ----------------------------------------------------------------------- mipmaps
        static double SrgbToLin(double c) { c = Clip(c, 0, 1); return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        static double LinToSrgb(double c) { c = Clip(c, 0, 1); return c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055; }

        /// <summary>A level: [slice][channel][y*w+x].</summary>
        public class Level
        {
            public int W, H, D, K;
            public double[][][] V;
            public Level(int w, int h, int d, int k)
            {
                W = w; H = h; D = d; K = k;
                V = new double[d][][];
                for (int z = 0; z < d; z++) { V[z] = new double[k][]; for (int c = 0; c < k; c++) V[z][c] = new double[w * h]; }
            }
            public Level Copy()
            {
                var l = new Level(W, H, D, K);
                for (int z = 0; z < D; z++) for (int c = 0; c < K; c++) Array.Copy(V[z][c], l.V[z][c], W * H);
                return l;
            }
        }

        /// <summary>Area-weighted box filter along one axis (0 = depth, 1 = height, 2 = width).</summary>
        static Level DownAxis(Level a, int axis)
        {
            int n = axis == 0 ? a.D : axis == 1 ? a.H : a.W;
            if (n == 1) return a;
            int m = n / 2;
            double[,] wt = null;
            if (n % 2 != 0)
            {
                double s = (double)n / m;
                wt = new double[m, n];
                for (int j = 0; j < m; j++)
                {
                    double x0 = j * s, x1 = (j + 1) * s;
                    for (int i = (int)Math.Floor(x0); i < Math.Min(n, (int)Math.Ceiling(x1)); i++)
                        wt[j, i] = (Math.Min(x1, i + 1) - Math.Max(x0, i)) / s;
                }
            }
            int nw = axis == 2 ? m : a.W, nh = axis == 1 ? m : a.H, nd = axis == 0 ? m : a.D;
            var r = new Level(nw, nh, nd, a.K);
            for (int z = 0; z < nd; z++)
                for (int c = 0; c < a.K; c++)
                    for (int y = 0; y < nh; y++)
                        for (int x = 0; x < nw; x++)
                        {
                            double Src(int i) =>
                                axis == 0 ? a.V[i][c][y * a.W + x] : axis == 1 ? a.V[z][c][i * a.W + x] : a.V[z][c][y * a.W + i];
                            int j = axis == 0 ? z : axis == 1 ? y : x;
                            double v;
                            if (wt == null) v = (Src(2 * j) + Src(2 * j + 1)) * 0.5;
                            else { v = 0; for (int i = 0; i < n; i++) if (wt[j, i] != 0) v += wt[j, i] * Src(i); }
                            r.V[z][c][y * nw + x] = v;
                        }
            return r;
        }

        public static List<Level> BuildMips(Level b, int levels, List<int> srgbIdx, bool volume)
        {
            var o = new List<Level> { b };
            var cur = b;
            if (srgbIdx.Count > 0)
            {
                cur = cur.Copy();
                foreach (var arr in cur.V) foreach (int k in srgbIdx) for (int i = 0; i < arr[k].Length; i++) arr[k][i] = SrgbToLin(arr[k][i]);
            }
            for (int l = 1; l < levels; l++)
            {
                foreach (int ax in volume ? new[] { 0, 1, 2 } : new[] { 1, 2 }) cur = DownAxis(cur, ax);
                var lvl = cur;
                if (srgbIdx.Count > 0)
                {
                    lvl = cur.Copy();
                    foreach (var arr in lvl.V) foreach (int k in srgbIdx) for (int i = 0; i < arr[k].Length; i++) arr[k][i] = LinToSrgb(arr[k][i]);
                }
                o.Add(lvl);
            }
            return o;
        }

        // ----------------------------------------------------------------------- writing
        static void PutU32(MemoryStream s, long v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24)); }

        public static void WriteDds(string path, JObj meta, DdsFormat fmt, List<byte[]> surfaces, int w, int h, int d, int mips, int arraySize)
        {
            var hd = meta.Obj("header");
            var pf = hd.Obj("pixel_format");
            long flags = hd.Long("flags") | DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT;
            long caps = hd.Long("caps") | DDSCAPS_TEXTURE;
            bool volume = meta["volume"] is bool vb && vb;
            if (mips > 1) { flags |= DDSD_MIPMAPCOUNT; caps |= DDSCAPS_MIPMAP | DDSCAPS_COMPLEX; }
            else caps &= ~DDSCAPS_MIPMAP;
            if (volume) flags |= DDSD_DEPTH;
            long pitch;
            if ((flags & DDSD_LINEARSIZE) != 0) pitch = fmt.SurfaceSize(w, h);
            else if ((flags & DDSD_PITCH) != 0) pitch = fmt.RowPitch(w);
            else pitch = hd.Long("pitch_or_linear_size");
            long mipField = mips == 1 && hd.Long("mip_map_count", 1) == 0 ? 0 : mips;
            long depthField = volume ? d : hd.Long("depth_field");
            var o = new MemoryStream();
            o.Write(new byte[] { (byte)'D', (byte)'D', (byte)'S', (byte)' ' }, 0, 4);
            foreach (var v in new[] { 124, flags, h, w, pitch, depthField, mipField }) PutU32(o, v);
            var r1 = hd.Arr("reserved1");
            for (int i = 0; i < 11; i++) PutU32(o, r1 != null && i < r1.Count ? Json.ToLong(r1[i]) : 0);
            PutU32(o, 32);
            foreach (var k in new[] { "flags", "fourcc", "bitcount", "rmask", "gmask", "bmask", "amask" }) PutU32(o, pf.Long(k));
            foreach (var v in new[] { caps, hd.Long("caps2"), hd.Long("caps3"), hd.Long("caps4"), hd.Long("reserved2") }) PutU32(o, v);
            var x = meta.Obj("dx10");
            if (x != null)
            {
                long asz = (arraySize == 1 && x.Long("array_size") == 0) || volume ? x.Long("array_size") : arraySize;
                foreach (var v in new[] { x.Long("dxgi_format"), x.Long("resource_dimension"), x.Long("misc_flag"), asz, x.Long("misc_flags2") }) PutU32(o, v);
            }
            foreach (var s in surfaces) o.Write(s, 0, s.Length);
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, o.ToArray());
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        // ----------------------------------------------------------------------- JSON
        const string SecEdit = "editable", SecInfo = "info", SecTech = "do_not_edit";
        static readonly Dictionary<string, string> V2Sections = new Dictionary<string, string> { { "edytowalne", SecEdit }, { "informacje", SecInfo }, { "nie_zmieniaj", SecTech } };
        static readonly string[] FullWords = { "full", "max", "", "pelny", "pełny" };
        static readonly string[] Readme =
        {
            "Section 'editable' - safe to change.",
            "Section 'info' - for reference only, ignored on import (dimensions are taken from the images).",
            "Section 'do_not_edit' - technical data needed to reproduce the exact format. To change the format use: InterfaceTools dds create image.png -f FORMAT",
        };
        static readonly string[] InfoKeys = { "source", "format", "container", "width", "height", "depth", "array_size", "srgb" };
        static readonly string[] TechKeys = { "dimension", "volume", "cubemap", "cube_faces", "image_kind", "image_layout", "header", "dx10" };

        /// <summary>Flat metadata -> readable JSON split into sections (as dds_tool.py writes it).</summary>
        public static JObj StructureMeta(JObj flat)
        {
            var edit = new JObj(); var help = new JObj();
            edit["mipmaps"] = flat["full_mip_chain"] is bool f && f ? (object)"full" : flat.Long("mip_levels", 1);
            help["mipmaps"] = "\"full\" = full chain computed from the image size, a number = exactly that many levels (1 = no mipmaps)";
            if (flat["bc1_alpha"] != null)
            {
                edit["bc1_alpha"] = flat["bc1_alpha"];
                help["bc1_alpha"] = "BC1/DXT1 only: true = 1-bit transparency (alpha < 50% -> transparent)";
            }
            edit["images"] = flat["images"];
            help["images"] = "image files; \"file\" may be renamed. The order of entries matters (cubemap: +X -X +Y -Y +Z -Z), for 3D textures the \"slice\" field";
            edit["_help"] = help;
            var info = new JObj();
            foreach (var k in InfoKeys) info[k] = flat[k];
            info["original_mip_levels"] = flat["mip_levels"];
            var tech = new JObj();
            foreach (var k in TechKeys) tech[k] = flat[k];
            var o = new JObj();
            o["_readme"] = Readme.Cast<object>().ToList();
            o["tool"] = "dds_tool"; o["tool_version"] = (long)ToolVersion;
            o[SecEdit] = edit; o[SecInfo] = info; o[SecTech] = tech;
            return o;
        }

        /// <summary>Readable JSON (or an old flat one) -> flat metadata used by import.</summary>
        public static JObj FlattenMeta(JObj m0)
        {
            var m = new JObj();
            foreach (var kv in m0) m[V2Sections.TryGetValue(kv.Key, out string nk) ? nk : kv.Key] = kv.Value;
            if (!m.Has(SecTech)) return m;
            var flat = (m.Obj(SecInfo) ?? new JObj()).Copy();
            foreach (var kv in m.Obj(SecTech)) flat[kv.Key] = kv.Value;
            var e = new JObj();
            foreach (var kv in m.Obj(SecEdit) ?? new JObj()) e[kv.Key == "mipmapy" ? "mipmaps" : kv.Key] = kv.Value;
            object mips = e.Has("mipmaps") ? e["mipmaps"] : "full";
            if (mips is string ms && FullWords.Contains(ms.Trim().ToLowerInvariant())) { flat["full_mip_chain"] = true; flat["mip_levels"] = 1L; }
            else
            {
                long nm;
                try { nm = mips is string s2 ? long.Parse(s2.Trim(), CultureInfo.InvariantCulture) : Json.ToLong(mips); }
                catch (Exception) { throw new DdsError($"Invalid \"mipmaps\" value: {mips} (use \"full\" or a number)"); }
                flat["full_mip_chain"] = nm <= 0; flat["mip_levels"] = Math.Max(1, nm);
            }
            flat["bc1_alpha"] = e["bc1_alpha"];
            flat["images"] = e["images"] ?? new List<object>();
            flat["tool"] = m["tool"]; flat["tool_version"] = m["tool_version"];
            return flat;
        }

        public static JObj LoadMeta(string path)
        {
            object o;
            try { o = Json.Parse(File.ReadAllText(path, System.Text.Encoding.UTF8)); }
            catch (JsonError e) { throw new DdsError($"Syntax error in {Path.GetFileName(path)} (line {e.Line}): {e.Message}"); }
            var m = o as JObj;
            if (m == null || m.Str("tool") != "dds_tool") throw new DdsError($"{path} is not a dds_tool metadata file");
            return FlattenMeta(m);
        }

        public static void SaveMeta(string path, JObj flat) => File.WriteAllText(path, Json.Dump(StructureMeta(flat), 2), Text.Utf8NoBom);

        // ----------------------------------------------------------------------- export
        static readonly Dictionary<int, string> DimNames = new Dictionary<int, string> { { DIM_1D, "1D" }, { DIM_2D, "2D" }, { DIM_3D, "3D" } };

        public static string ExportDds(string path, string outdir = null)
        {
            var dds = ReadDds(path);
            var fmt = dds.Fmt;
            if (!fmt.Supported) throw new DdsError($"format {dds.FmtDesc} is not supported by this program (supported: DXT1/3/5, BC4, BC5 and uncompressed formats up to 16 bits per channel)");
            var surfaces = IterateSurfaces(dds, out long total);
            if (dds.DataLength < total)
                Log.Line($"  warning: the file has less data than the header implies ({dds.DataLength} < {total} B) - using mip 0 only");
            var top = surfaces.Where(s => s.Mip == 0).ToList();
            var decoded = top.Select(s => (s, DecodeSurface(dds, s.Off, s.W, s.H))).ToList();
            bool? bc1Alpha = null;
            if (fmt.Kind == "bc" && fmt.Bc == 1) bc1Alpha = decoded.Any(t => t.Item2["a"].Any(v => v < 0.5));
            var (kind, layout) = ImagePlan(fmt, bc1Alpha ?? true);
            string ext = kind.StartsWith("png") ? ".png" : ".exr";
            string stem = Path.GetFileNameWithoutExtension(path);
            outdir = outdir ?? Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(outdir);
            var images = new List<object>();
            foreach (var (s, vals) in decoded)
            {
                string name = stem;
                if (dds.ArraySize > 1) name += $"_a{s.Array}";
                if (dds.Cubemap) name += "_" + CubeFaceNames[s.Face];
                if (dds.Volume && dds.D > 1) name += $"_z{s.Slice:000}";
                name += ext;
                Png.Save(Path.Combine(outdir, name), ValuesToImage(vals, fmt, kind, layout, s.H, s.W));
                var im = new JObj();
                im["file"] = name; im["array_index"] = (long)s.Array;
                im["face"] = dds.Cubemap ? CubeFaceNames[s.Face] : null;
                im["slice"] = dds.Volume ? (object)(long)s.Slice : null;
                images.Add(im);
            }
            var meta = new JObj();
            meta["tool"] = "dds_tool"; meta["tool_version"] = (long)ToolVersion;
            meta["source"] = Path.GetFileName(path);
            meta["format"] = dds.FmtDesc;
            meta["container"] = dds.Dx10 != null ? "DX10" : "legacy";
            meta["width"] = (long)dds.W; meta["height"] = (long)dds.H; meta["depth"] = (long)dds.D;
            meta["mip_levels"] = (long)dds.Mips;
            meta["full_mip_chain"] = dds.Mips == FullMipCount(dds.W, dds.H, dds.D);
            meta["dimension"] = DimNames.TryGetValue(dds.Dim, out string dn) ? dn : "2D";
            meta["array_size"] = (long)dds.ArraySize;
            meta["cubemap"] = dds.Cubemap;
            meta["cube_faces"] = dds.Cubemap ? dds.Faces.Select(f => (object)CubeFaceNames[f]).ToList() : null;
            meta["volume"] = dds.Volume;
            meta["srgb"] = fmt.Srgb;
            meta["bc1_alpha"] = bc1Alpha;
            meta["image_kind"] = kind;
            meta["image_layout"] = layout.Cast<object>().ToList();
            meta["images"] = images;
            meta["header"] = dds.Header;
            meta["dx10"] = dds.Dx10;
            string jpath = Path.Combine(outdir, stem + ".json");
            SaveMeta(jpath, meta);
            var fileNames = images.Take(3).Select(i => ((JObj)i).Str("file"));
            Log.Line($"  {Path.GetFileName(path)} -> {string.Join(", ", fileNames)}{(images.Count > 3 ? " ..." : "")} ({dds.FmtDesc}, {dds.W}x{dds.H}, mip: {dds.Mips}) + {Path.GetFileName(jpath)}");
            return jpath;
        }

        // ----------------------------------------------------------------------- import
        public static DdsFormat FmtFromMeta(JObj meta)
        {
            var x = meta.Obj("dx10");
            if (x != null)
            {
                int n = (int)x.Long("dxgi_format");
                if (!Dxgi.TryGetValue(n, out var f)) throw new DdsError($"Unsupported DXGI format {n}");
                return f;
            }
            return FmtFromLegacy(meta.Obj("header").Obj("pixel_format")).Item1;
        }

        public static string ImportJson(string jpath, string outp = null, int? mips = null, Dictionary<string, object> opts = null)
        {
            var meta = LoadMeta(jpath);
            string bse = Path.GetDirectoryName(Path.GetFullPath(jpath));
            string ddsName = Path.GetFileNameWithoutExtension(jpath) + ".dds";
            return ImportMeta(meta, bse, ddsName, outp, mips, opts, Path.GetFileName(jpath));
        }

        public static string ImportMeta(JObj meta, string bse, string ddsName, string outp = null, int? mips = null, Dictionary<string, object> opts = null, string label = "")
        {
            opts = opts != null ? new Dictionary<string, object>(opts) : new Dictionary<string, object>();
            var fmt = FmtFromMeta(meta);
            if (!fmt.Supported) throw new DdsError($"format {meta.Str("format") ?? fmt.Name} is not supported by this program");
            string kind = meta.Str("image_kind");
            var layout = (meta.Arr("image_layout") ?? new List<object>()).Select(o => o as string).ToList();
            if (fmt.Kind == "bc" && fmt.Bc == 1) opts["bc1_alpha"] = meta["bc1_alpha"] is bool ba && ba;
            var chans = fmt.ValueChannels().Select(c => c.n).ToList();
            bool volume = meta["volume"] is bool vv && vv;
            Level LoadVals(JObj im)
            {
                string p = Path.Combine(bse, im.Str("file"));
                if (!File.Exists(p)) throw new DdsError($"Image file not found: {p}");
                var img = LoadImage(p);
                var vals = ImageToValues(img, fmt, kind, layout);
                var l = new Level(img.W, img.H, 1, chans.Count);
                for (int c = 0; c < chans.Count; c++)
                {
                    if (vals.TryGetValue(chans[c], out var v)) Array.Copy(v, l.V[0][c], v.Length);
                    else for (int i = 0; i < img.W * img.H; i++) l.V[0][c][i] = chans[c] == "a" ? 1.0 : 0.0;
                }
                return l;
            }
            var images = (meta.Arr("images") ?? new List<object>()).OfType<JObj>().ToList();
            if (images.Count == 0) throw new DdsError("No images listed in the metadata");
            var groups = new List<Level>();
            if (volume)
            {
                var imgs = images.OrderBy(i => i["slice"] == null ? 0 : Json.ToLong(i["slice"])).ToList();
                var slices = imgs.Select(LoadVals).ToList();
                var g = new Level(slices[0].W, slices[0].H, slices.Count, chans.Count);
                for (int z = 0; z < slices.Count; z++)
                {
                    if (slices[z].W != g.W || slices[z].H != g.H) throw new DdsError("All images (faces/layers) must have the same size");
                    g.V[z] = slices[z].V[0];
                }
                groups.Add(g);
            }
            else groups = images.Select(LoadVals).ToList();
            int d = groups[0].D, h = groups[0].H, w = groups[0].W;
            foreach (var g in groups) if (g.W != w || g.H != h) throw new DdsError("All images (faces/layers) must have the same size");
            if (meta.Str("dimension") == "1D" && h != 1) throw new DdsError("A 1D texture must be 1 pixel high");
            int maxm = FullMipCount(w, h, volume ? d : 1);
            int levels;
            if (mips != null) levels = mips <= 0 ? maxm : Math.Min(mips.Value, maxm);
            else if (meta["full_mip_chain"] is bool fm && fm) levels = maxm;
            else levels = (int)Math.Min(meta.Long("mip_levels", 1), maxm);
            var srgbIdx = fmt.Srgb ? Enumerable.Range(0, chans.Count).Where(k => "rgb".Contains(chans[k])).ToList() : new List<int>();
            List<byte[]> Enc(Level lvl)
            {
                var o = new List<byte[]>();
                for (int z = 0; z < lvl.D; z++)
                {
                    var vals = new Dictionary<string, double[]>();
                    for (int k = 0; k < chans.Count; k++) vals[chans[k]] = lvl.V[z][k];
                    o.Add(fmt.Kind == "bc" ? BcEncode(vals, fmt, lvl.W, lvl.H, opts) : RawEncode(vals, fmt, lvl.W, lvl.H));
                }
                return o;
            }
            var surfaces = new List<byte[]>();
            if (volume) foreach (var lvl in BuildMips(groups[0], levels, srgbIdx, true)) surfaces.AddRange(Enc(lvl));
            else foreach (var g in groups) foreach (var lvl in BuildMips(g, levels, srgbIdx, false)) surfaces.AddRange(Enc(lvl));
            bool cube = meta["cubemap"] is bool cb && cb;
            int nfaces = cube ? (meta.Arr("cube_faces")?.Count ?? 0) : 1;
            int arraySize = !volume ? Math.Max(1, groups.Count / Math.Max(1, nfaces)) : 1;
            if (outp == null) outp = Path.Combine(bse, ddsName);
            else if (Directory.Exists(outp) || !outp.ToLowerInvariant().EndsWith(".dds"))
            {
                Directory.CreateDirectory(outp);
                outp = Path.Combine(outp, ddsName);
            }
            WriteDds(outp, meta, fmt, surfaces, w, h, volume ? d : 1, levels, arraySize);
            Log.Line($"  {label} -> {Path.GetFileName(outp)} ({meta.Str("format")}, {w}x{h}, mip: {levels})");
            return outp;
        }

        // ----------------------------------------------------------------------- create
        static readonly Dictionary<string, int> LegacyAliases = new Dictionary<string, int>
            { { "DXT1", 71 }, { "DXT2", 74 }, { "DXT3", 74 }, { "DXT4", 77 }, { "DXT5", 77 }, { "ATI1", 80 }, { "ATI2", 83 }, { "BC4U", 80 }, { "BC4S", 81 }, { "BC5U", 83 }, { "BC5S", 84 } };

        public static (object key, DdsFormat fmt) FindFormat(string name)
        {
            string key = name.Trim().ToUpperInvariant().Replace("DXGI_FORMAT_", "");
            if (LegacyAliases.TryGetValue(key, out int dx)) return (key, Dxgi[dx]);
            foreach (var kv in Dxgi) if (kv.Value.Name == key) return (kv.Key, kv.Value);
            throw new DdsError($"Unknown format \"{name}\". List: InterfaceTools dds formats");
        }

        static JObj NewMeta(string fmtName, out DdsFormat fmt)
        {
            var (key, f) = FindFormat(fmtName);
            fmt = f;
            if (!f.Supported) throw new DdsError($"format {f.Name} is not supported by this program");
            long flags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT | (f.Kind == "bc" ? DDSD_LINEARSIZE : DDSD_PITCH);
            var pf = new JObj();
            JObj dx10 = null; string desc;
            pf["size"] = 32L; pf["flags"] = DDPF_FOURCC;
            if (key is string ks)
            {
                pf["fourcc"] = (long)FourCC(ks); desc = ks;
            }
            else
            {
                pf["fourcc"] = (long)FourCC("DX10");
                dx10 = new JObj();
                dx10["dxgi_format"] = (long)(int)key; dx10["resource_dimension"] = (long)DIM_2D; dx10["misc_flag"] = 0L; dx10["array_size"] = 1L; dx10["misc_flags2"] = 0L;
                desc = f.Name;
            }
            pf["bitcount"] = 0L; pf["rmask"] = 0L; pf["gmask"] = 0L; pf["bmask"] = 0L; pf["amask"] = 0L;
            var hdr = new JObj();
            hdr["flags"] = flags; hdr["pitch_or_linear_size"] = 0L; hdr["reserved1"] = Enumerable.Repeat((object)0L, 11).ToList(); hdr["pixel_format"] = pf;
            hdr["caps"] = DDSCAPS_TEXTURE; hdr["caps2"] = 0L; hdr["caps3"] = 0L; hdr["caps4"] = 0L; hdr["reserved2"] = 0L; hdr["mip_map_count"] = 1L; hdr["depth_field"] = 0L;
            var m = new JObj();
            m["tool"] = "dds_tool"; m["tool_version"] = (long)ToolVersion; m["source"] = null; m["format"] = desc;
            m["container"] = dx10 != null ? "DX10" : "legacy"; m["dimension"] = "2D"; m["volume"] = false; m["cubemap"] = false; m["cube_faces"] = null;
            m["array_size"] = 1L; m["full_mip_chain"] = true; m["mip_levels"] = 1L; m["srgb"] = f.Srgb; m["bc1_alpha"] = null;
            m["header"] = hdr; m["dx10"] = dx10;
            return m;
        }

        public static string CreateDds(List<string> imagePaths, string fmtName = null, string like = null, string outp = null, int? mips = null,
                                       bool cube = false, bool writeJson = false, Dictionary<string, object> opts = null)
        {
            var paths = imagePaths.Select(Path.GetFullPath).ToList();
            foreach (var p in paths) if (!File.Exists(p)) throw new DdsError($"File not found: {p}");
            JObj meta; DdsFormat fmt;
            if (like != null)
            {
                if (like.ToLowerInvariant().EndsWith(".dds"))
                {
                    var src = ReadDds(like);
                    fmt = src.Fmt;
                    meta = new JObj();
                    meta["tool"] = "dds_tool"; meta["tool_version"] = (long)ToolVersion; meta["format"] = src.FmtDesc;
                    meta["header"] = src.Header; meta["dx10"] = src.Dx10?.Copy();
                    meta["mip_levels"] = (long)src.Mips; meta["full_mip_chain"] = src.Mips == FullMipCount(src.W, src.H, src.D);
                }
                else { meta = LoadMeta(like); fmt = FmtFromMeta(meta); }
                if (!fmt.Supported) throw new DdsError($"format {fmt.Name} is not supported by this program");
                meta["dimension"] = "2D"; meta["volume"] = false; meta["cubemap"] = false; meta["cube_faces"] = null; meta["array_size"] = 1L; meta["srgb"] = fmt.Srgb;
                var h2 = meta.Obj("header").Copy(); h2["caps2"] = 0L; meta["header"] = h2;
                if (meta.Obj("dx10") != null)
                {
                    var x = meta.Obj("dx10").Copy(); x["resource_dimension"] = (long)DIM_2D; x["misc_flag"] = 0L; x["array_size"] = 1L; meta["dx10"] = x;
                }
            }
            else if (fmtName != null) meta = NewMeta(fmtName, out fmt);
            else throw new DdsError("Specify a format (--format) or a template (--like)");
            if (cube)
            {
                if (paths.Count % 6 != 0) throw new DdsError("A cubemap needs 6 images (or a multiple of 6) in the order +X -X +Y -Y +Z -Z");
                meta["cubemap"] = true;
                meta["cube_faces"] = CubeFaceNames.Cast<object>().ToList();
                var hh = meta.Obj("header").Copy();
                hh["caps2"] = DDSCAPS2_CUBEMAP | CubeFaceFlags.Sum();
                hh["caps"] = hh.Long("caps") | DDSCAPS_COMPLEX;
                meta["header"] = hh;
                if (meta.Obj("dx10") != null) { var x = meta.Obj("dx10").Copy(); x["misc_flag"] = x.Long("misc_flag") | MISC_TEXTURECUBE; meta["dx10"] = x; }
                else if (paths.Count > 6) throw new DdsError("Cubemap arrays require a DXGI format name (e.g. BC1_UNORM instead of DXT1, BC3_UNORM instead of DXT5)");
            }
            else if (paths.Count > 1 && meta.Obj("dx10") == null)
                throw new DdsError("Texture arrays require a DXGI format name (e.g. BC1_UNORM instead of DXT1, BC3_UNORM instead of DXT5)");
            bool? bc1Alpha = null;
            if (fmt.Kind == "bc" && fmt.Bc == 1)
            {
                bc1Alpha = false;
                foreach (var p in paths)
                {
                    var a = LoadImage(p);
                    if (a.C == 2 || a.C == 4)
                        for (int i = 0; i < a.W * a.H; i++) if ((double)a.Px[i * a.C + a.C - 1] / a.Max < 0.5) { bc1Alpha = true; break; }
                }
            }
            var (kind, layout) = ImagePlan(fmt, bc1Alpha ?? true);
            meta["bc1_alpha"] = bc1Alpha; meta["image_kind"] = kind; meta["image_layout"] = layout.Cast<object>().ToList();
            var faces = cube ? CubeFaceNames : new string[] { null };
            meta["images"] = paths.Select((p, i) =>
            {
                var im = new JObj();
                im["file"] = p; im["array_index"] = (long)(i / faces.Length); im["face"] = faces[i % faces.Length]; im["slice"] = null;
                return (object)im;
            }).ToList();
            if (mips == null && !(meta["full_mip_chain"] is bool fc && fc)) mips = (int)meta.Long("mip_levels", 1);
            string stem = Path.GetFileNameWithoutExtension(paths[0]);
            string bse = Path.GetDirectoryName(paths[0]);
            string res = ImportMeta(meta, bse, stem + ".dds", outp, mips, opts, Path.GetFileName(paths[0]) + (paths.Count > 1 ? $" (+{paths.Count - 1})" : ""));
            if (writeJson)
            {
                var jm = meta.Copy();
                jm["source"] = Path.GetFileName(res);
                jm["images"] = meta.Arr("images").OfType<JObj>().Select(i =>
                {
                    var c = i.Copy();
                    c["file"] = RelPath(i.Str("file"), Path.GetDirectoryName(res));
                    return (object)c;
                }).ToList();
                var d = ReadDds(res);
                jm["width"] = (long)d.W; jm["height"] = (long)d.H; jm["depth"] = 1L; jm["mip_levels"] = (long)d.Mips; jm["array_size"] = (long)d.ArraySize;
                jm["header"] = d.Header; jm["dx10"] = d.Dx10;
                SaveMeta(Path.Combine(Path.GetDirectoryName(res), Path.GetFileNameWithoutExtension(res) + ".json"), jm);
            }
            return res;
        }

        static string RelPath(string path, string dir)
        {
            var p = Path.GetFullPath(path); var d = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (p.StartsWith(d, StringComparison.OrdinalIgnoreCase)) return p.Substring(d.Length);
            var u1 = new Uri(d); var u2 = new Uri(p);
            return Uri.UnescapeDataString(u1.MakeRelativeUri(u2).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        public static void ListFormats()
        {
            Log.Line("Legacy headers (FourCC): " + string.Join(", ", LegacyAliases.Keys));
            Log.Line("DXGI formats supported by this program:");
            var names = Dxgi.Values.Where(f => !f.Name.Contains("TYPELESS") && f.Supported).Select(f => f.Name).ToList();
            for (int i = 0; i < names.Count; i += 3) Log.Line("  " + string.Concat(names.Skip(i).Take(3).Select(n => n.PadRight(30))).TrimEnd());
            Log.Line("Typical: colour -> BC1_UNORM (DXT1) / BC3_UNORM (DXT5), single-channel mask -> BC4_UNORM,");
            Log.Line("         normal map -> BC5_UNORM, uncompressed -> R8G8B8A8_UNORM or B8G8R8A8_UNORM");
            Log.Line("Not supported (recognized only): BC6H, BC7 and floating point / 32-bit formats.");
        }

        public static void InfoDds(string path)
        {
            var dds = ReadDds(path);
            IterateSurfaces(dds, out long total);
            Log.Line(path);
            Log.Line($"  format:     {dds.FmtDesc} ({(dds.Dx10 != null ? "DX10" : "legacy")}){(dds.Fmt.Supported ? "" : " - not supported for conversion")}");
            Log.Line($"  size:       {dds.W} x {dds.H}{(dds.Volume ? $" x {dds.D}" : "")}");
            Log.Line($"  type:       {(DimNames.TryGetValue(dds.Dim, out string dn) ? dn : "2D")}{(dds.Cubemap ? ", cubemap" : "")}{(dds.ArraySize > 1 ? $", array x{dds.ArraySize}" : "")}");
            Log.Line($"  mipmaps:    {dds.Mips}{(dds.Mips == FullMipCount(dds.W, dds.H, dds.D) ? " (full chain)" : "")}");
            Log.Line($"  sRGB:       {(dds.Fmt.Srgb ? "yes" : "no")}");
            Log.Line($"  data:       {dds.DataLength} B (expected {total} B)");
        }

        // ----------------------------------------------------------------------- CLI
        static readonly string[] ImageExts = { ".png", ".exr", ".tif", ".tiff" };

        public static string FindJsonForImage(string path)
        {
            string d = Path.GetDirectoryName(Path.GetFullPath(path));
            string name = Path.GetFileName(path);
            string cand = Path.Combine(d, Path.GetFileNameWithoutExtension(name) + ".json");
            if (File.Exists(cand)) return cand;
            foreach (var fn in Directory.GetFiles(d, "*.json"))
            {
                try
                {
                    var m = LoadMeta(fn);
                    if ((m.Arr("images") ?? new List<object>()).OfType<JObj>().Any(i => i.Str("file") == name)) return fn;
                }
                catch (Exception) { }
            }
            throw new DdsError($"No .json file found for {path}");
        }

        public static List<string> Collect(IEnumerable<string> paths, string[] exts, bool recursive)
        {
            var o = new List<string>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    var stack = new List<string> { p };
                    while (stack.Count > 0)
                    {
                        string dir = stack[0]; stack.RemoveAt(0);
                        o.AddRange(Directory.GetFiles(dir).OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                            .Where(f => exts.Any(e => f.ToLowerInvariant().EndsWith(e))));
                        if (!recursive) break;
                        stack.InsertRange(0, Directory.GetDirectories(dir).OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal));
                    }
                }
                else o.Add(p);
            }
            return o;
        }

        const string Usage =
            "Usage: InterfaceTools dds <command> ...\n" +
            "  export FILES... [-o OUTDIR] [-r]          DDS -> image(s) + JSON (largest mipmap only)\n" +
            "  import FILES... [-o OUT] [-r] [--mips N]  JSON (+ image) -> DDS with generated mipmaps\n" +
            "  create IMAGES... (-f FORMAT | --like FILE) [-o OUT] [--mips N] [--cube] [--json]\n" +
            "  info FILES...                             information about DDS files\n" +
            "  formats                                   list of supported formats\n" +
            "  FILES...                                  .dds -> export, .json / .png -> import";

        public static int Run(IList<string> argv)
        {
            var a = argv.ToList();
            if (a.Count == 0 || a[0] == "-h" || a[0] == "--help") { Log.Line(Usage); return a.Count == 0 ? 1 : 0; }
            string cmd = a[0];
            bool auto = !new[] { "export", "import", "info", "create", "formats" }.Contains(cmd);
            var files = new List<string>();
            string outp = null, format = null, like = null;
            bool recursive = false, cube = false, json = false;
            int? mips = null;
            for (int i = auto ? 0 : 1; i < a.Count; i++)
            {
                string x = a[i];
                if ((x == "-o" || x == "--out" || x == "--outdir") && i + 1 < a.Count) outp = a[++i];
                else if (x == "-r" || x == "--recursive") recursive = true;
                else if (x == "--mips" && i + 1 < a.Count)
                {
                    if (!int.TryParse(a[++i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int mv)) { Log.Line($"invalid --mips value: {a[i]}"); return 1; }
                    mips = mv;
                }
                else if ((x == "-f" || x == "--format") && i + 1 < a.Count) format = a[++i];
                else if (x == "--like" && i + 1 < a.Count) like = a[++i];
                else if (x == "--cube") cube = true;
                else if (x == "--json") json = true;
                else if (x == "--bc7-builtin" || x == "--bc7-level") { if (x == "--bc7-level") i++; }
                else files.Add(x);
            }
            int errors = 0;
            void Do(Action f)
            {
                try { f(); }
                catch (DdsError e) { errors++; Log.Line($"  ERROR: {e.Message}"); }
                catch (Exception e) when (!(e is OutOfMemoryException)) { errors++; Log.Line($"  ERROR ({e.GetType().Name}): {e.Message}"); }
            }
            if (cmd == "formats") { ListFormats(); return 0; }
            if (files.Count == 0) { Log.Line(Usage); return 1; }
            if (cmd == "export") foreach (var p in Collect(files, new[] { ".dds" }, recursive)) Do(() => ExportDds(p, outp));
            else if (cmd == "import")
            {
                foreach (var p0 in Collect(files, new[] { ".json" }, recursive))
                {
                    string p = p0;
                    if (ImageExts.Any(e => p.ToLowerInvariant().EndsWith(e)))
                    {
                        try { p = FindJsonForImage(p); } catch (DdsError e) { errors++; Log.Line($"  ERROR: {e.Message}"); continue; }
                    }
                    Do(() => ImportJson(p, outp, mips));
                }
            }
            else if (cmd == "create") Do(() => CreateDds(files, format, like, outp, mips, cube, json));
            else if (cmd == "info") foreach (var p in Collect(files, new[] { ".dds" }, false)) Do(() => InfoDds(p));
            else
            {
                var done = new HashSet<string>();
                foreach (var p in Collect(files, new[] { ".dds", ".json" }, false))
                {
                    string low = p.ToLowerInvariant();
                    if (low.EndsWith(".dds")) { Do(() => ExportDds(p)); continue; }
                    string j;
                    try { j = ImageExts.Any(e => low.EndsWith(e)) ? FindJsonForImage(p) : p; }
                    catch (DdsError e) { errors++; Log.Line($"  ERROR: {e.Message}"); continue; }
                    if (done.Add(j)) Do(() => ImportJson(j));
                }
            }
            return errors > 0 ? 1 : 0;
        }
    }
}

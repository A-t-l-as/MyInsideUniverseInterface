// Decompiler: the inverse of InterfaceMap. Rebuilds the definition file (.txt), the
// "<name>@.bmp" bitmaps and the resource header from a .dat file; compiled again they give
// the same .dat byte for byte (verified right away). Port of dat2txt.py.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace InterfaceTools
{
    /// <summary>Identifier names for DialogTemplates (resource.h / DialogsResource.h).
    /// Missing names are created as _ID_&lt;number&gt;.</summary>
    public class ResourceNames
    {
        public string Path;
        public Dictionary<string, long> Values;
        public Dictionary<string, string> OrigNames = new Dictionary<string, string>();
        public List<string> Order = new List<string>();
        public List<KeyValuePair<string, long>> Extra = new List<KeyValuePair<string, long>>();

        public ResourceNames(string path)
        {
            Path = path;
            Values = Resources.Load(path);
            foreach (var (n, _) in Resources.Builtin) { OrigNames[n.ToLowerInvariant()] = n; Order.Add(n.ToLowerInvariant()); }
            if (path != null)
            {
                foreach (var raw in Text.ReadLatin1(path).Split('\n'))
                {
                    if (!raw.StartsWith("#define", StringComparison.Ordinal)) continue;
                    string nm = Text.ScanToken(raw, 7, out int p);
                    if (nm != null && Text.ScanInt(raw, p, out _) != null)
                    {
                        string low = Text.AsciiLower(nm);
                        if (!OrigNames.ContainsKey(low)) { OrigNames[low] = nm; Order.Add(low); }
                    }
                }
            }
        }

        public string NameFor(long value, params string[] prefer)
        {
            var cands = Order.Where(n => Values.TryGetValue(n, out long v) && v == value).ToList();
            foreach (var pref in prefer)
                foreach (var n in cands)
                    if (n.StartsWith(pref.ToLowerInvariant(), StringComparison.Ordinal)) return OrigNames[n];
            if (cands.Count > 0) return OrigNames[cands[0]];
            return Synthetic(value >= 0 ? $"_ID_{value}" : $"_ID_M{-value}", value);
        }

        /// <summary>Name only if the identifier is defined (no synthetic name is created).</summary>
        public string Lookup(long value)
        {
            foreach (var n in Order) if (Values.TryGetValue(n, out long v) && v == value) return OrigNames[n];
            return "";
        }

        public string Synthetic(string name, long value)
        {
            int k = Extra.FindIndex(kv => kv.Key == name);
            if (k >= 0) Extra[k] = new KeyValuePair<string, long>(name, value); else Extra.Add(new KeyValuePair<string, long>(name, value));
            Values[Text.AsciiLower(name)] = value;
            return name;
        }

        public void Write(string outPath)
        {
            string text = "";
            if (Path != null)
            {
                text = Text.ReadLatin1(Path);
                if (text.Length > 0 && !text.EndsWith("\n", StringComparison.Ordinal)) text += "\r\n";
            }
            if (Extra.Count > 0)
            {
                text += "\r\n// added by the decompiler (identifiers without a name in resource.h)\r\n";
                foreach (var kv in Extra) text += $"#define {kv.Key} {kv.Value}\r\n";
            }
            File.WriteAllBytes(outPath, Text.ToLatin1(text));
        }
    }

    /// <summary>A bitmap written by the decompiler (the pipeline uses the tile layout directly).</summary>
    public class BitmapInfo
    {
        public string Material, Path;
        public int Tile, TilesPerRow, Rows, Width, Height, Base;
    }

    public class DecompileResult
    {
        public bool Ok;
        public string Format, TxtPath;
        public List<BitmapInfo> Bitmaps = new List<BitmapInfo>();
        public List<string> Warnings = new List<string>();
    }

    public static class Decompiler
    {
        public const string ToolName = "InterfaceTools";
        public static readonly int[] TileCandidates = { 8, 16, 32, 64, 128, 256 };
        static readonly long[] NoBits = { Flags.NoLeft, Flags.NoRight, Flags.NoTop, Flags.NoBottom, Flags.NoBackground };

        static long S32(long v) => v >= 0x80000000L ? v - 0x100000000L : v;

        public static string FmtValues(int key, IList<long> vals) =>
            key == RecordKey.Color ? string.Join(",", vals.Select(v => $"0x{v:X8}"))
                                   : string.Join(",", vals.Select(v => v >= 0x10000 ? $"0x{v:X8}" : v.ToString(CultureInfo.InvariantCulture)));

        public static string RecordLine(int key, IList<long> vals)
        {
            if (RecordKey.Names.ContainsKey(key) && vals.Count > 0) return $"{RecordKey.Names[key]} {FmtValues(key, vals)}";
            return $"Effect:{key}" + (vals.Count > 0 ? " " + FmtValues(key, vals) : "");
        }

        /// <summary>Shortest notation that gives exactly v after strtod and rounding to float32.</summary>
        public static string FmtFloat(float v)
        {
            for (int prec = 1; prec < 10; prec++)
            {
                string s = Text.FormatG(v, prec);
                double back;
                try { back = Text.ParseDouble(s); } catch (FormatException) { continue; }
                if ((float)back == v || (float.IsNaN(v) && double.IsNaN(back)))
                {
                    if (!s.Contains("e") && !s.Contains(".") && !s.Contains("n")) s += ".0";
                    return s;
                }
            }
            return Text.FormatG(v, 9);
        }

        /// <summary>Repeats the second pass of the compiler: filling in empty rectangles.</summary>
        public static long[][] SimulateRects(long flags, DatState st, long[] margins)
        {
            var rects = st.Slots.Select(s => s.Mt != 0xFFFF ? (long[])s.R.Clone() : new long[] { 0, 0, -1, 0 }).ToArray();
            for (int i = 0; i < 9; i++)
            {
                var r = rects[i];
                if (st.Particle.Length > 0) rects[i] = new long[] { 0, 0, 0, 0 };
                else if (r[2] <= r[0] || r[3] <= r[1])
                {
                    var d = Compiler.DeriveRect(i, flags, rects);
                    if (d != null) rects[i] = d;
                }
            }
            if (st.Particle.Length > 0 && margins != null) Compiler.ApplyMargins(margins, rects);
            return rects.Select(r => r.Select(v => v & 0xFFFF).ToArray()).ToArray();
        }

        /// <summary>Earth 2160: item margins read from the rectangles of particle states.</summary>
        public static long[] InferMargins(DatItem item)
        {
            foreach (var kv in item.InFileOrder())
            {
                var st = kv.Value;
                if (st.Particle.Length == 0) continue;
                var r = st.Slots.Select(s => s.R).ToArray();
                long L = r[3][2], T = r[1][3], R = r[4][2];
                long B = R != 0 ? r[2][3] : (L != 0 ? r[7][3] : 0);
                return new[] { L, T, R, B };
            }
            return new long[] { 0, 0, 0, 0 };
        }

        static int PopCount(int v) { int c = 0; while (v != 0) { c += v & 1; v >>= 1; } return c; }

        /// <summary>Finds the smallest set of No* flags that reproduces all rectangles of the item.</summary>
        public static long? InferNoFlags(DatItem item, long[] margins)
        {
            long low = item.Flags & 0xFF;
            foreach (int combo in Enumerable.Range(0, 32).OrderBy(c => PopCount(c)).ThenBy(c => c))
            {
                long flags = low;
                for (int k = 0; k < 5; k++) if (((combo >> k) & 1) != 0) flags += NoBits[k];
                bool ok = item.States.Values.All(st =>
                {
                    var sim = SimulateRects(flags, st, margins);
                    for (int i = 0; i < 9; i++) if (!sim[i].SequenceEqual(st.Slots[i].R)) return false;
                    return true;
                });
                if (ok) return flags;
            }
            return null;
        }

        public static List<string> FlagLines(long flags, long ext = 0, string fmt = "")
        {
            var o = new List<string>();
            foreach (var (bit, name) in new[] { ((long)Flags.ConstX, "ConstX"), ((long)Flags.ConstY, "ConstY"), ((long)Flags.StretchX, "StretchX"), ((long)Flags.StretchY, "StretchY") })
                if ((flags & bit) != 0) o.Add($"{name} 1");
            long ax = (flags >> 4) & 3, ay = (flags >> 6) & 3;
            if (ax != 0) o.Add(new[] { "LeftX", "CenterX", "RandomX", "RightX" }[ax]);
            if (ay != 0) o.Add(new[] { "LeftY", "CenterY", "RandomY", "RightY" }[ay]);
            // In the original NoLeftBorder sets the background bit (0x1000), so the "no left border"
            // bit alone (0x100) can only be obtained via NoBorderX with NoRightBorder cleared.
            if ((flags & Flags.NoLeft) != 0)
            {
                o.Add("NoBorderX 1");
                if ((flags & Flags.NoRight) == 0) o.Add("NoRightBorder 0");
            }
            else if ((flags & Flags.NoRight) != 0) o.Add("NoRightBorder 1");
            if ((flags & Flags.NoTop) != 0 && (flags & Flags.NoBottom) != 0) o.Add("NoBorderY 1");
            else if ((flags & Flags.NoTop) != 0) o.Add("NoTopBorder 1");
            else if ((flags & Flags.NoBottom) != 0) o.Add("NoBottomBorder 1");
            if ((flags & Flags.NoBackground) != 0) o.Add("NoBackground 1");
            if ((ext & Flags.ScaleBorder) != 0) o.Add("ScaleBorder 1");
            if ((ext & Flags.ScaleBackground) != 0) o.Add("ScaleBackground 1");
            ext &= ~(long)(Flags.ScaleBorder | Flags.ScaleBackground);
            if (fmt == "tw2" && (ext & Flags.Tw2InterfaceSounds) != 0) { o.Add("InterfaceSounds 1"); ext &= ~(long)Flags.Tw2InterfaceSounds; }
            if (fmt == "tw2" && (ext & Flags.Tw2DissTexture) != 0) { o.Add("DissTexture 1"); ext &= ~(long)Flags.Tw2DissTexture; }
            if (ext != 0) o.Add($"ExtFlags 0x{ext:X}");
            return o;
        }

        public static int ChooseTile(string name, long maxExtent, Dictionary<string, int> overrides)
        {
            if (overrides.TryGetValue(name.ToLowerInvariant(), out int t)) return t;
            foreach (int c in TileCandidates.OrderByDescending(x => x))
                if (name.Contains(c.ToString(CultureInfo.InvariantCulture)) && c >= maxExtent) return c;
            foreach (int c in TileCandidates) if (c >= maxExtent) return c;
            return (int)maxExtent;
        }

        static void HsvToRgb(double h, double s, double v, out double r, out double g, out double b)
        {
            if (s == 0.0) { r = g = b = v; return; }
            int i = (int)(h * 6.0);
            double f = (h * 6.0) - i;
            double p = v * (1.0 - s), q = v * (1.0 - s * f), t = v * (1.0 - s * (1.0 - f));
            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; return;
                case 1: r = q; g = v; b = p; return;
                case 2: r = p; g = v; b = t; return;
                case 3: r = p; g = q; b = v; return;
                case 4: r = t; g = p; b = v; return;
                default: r = v; g = p; b = q; return;
            }
        }

        /// <summary>Colour-map palette (colour 0 = black, others well distinguishable).</summary>
        public static byte[] MakePalette()
        {
            var pal = new byte[1024];
            for (int i = 1; i < 256; i++)
            {
                double h = (i * 0.618033988749895) % 1.0;
                HsvToRgb(h, 0.55 + 0.45 * ((i * 7) % 3) / 2, 0.65 + 0.35 * ((i * 5) % 2), out double r, out double g, out double b);
                pal[i * 4] = (byte)(int)(b * 255); pal[i * 4 + 1] = (byte)(int)(g * 255); pal[i * 4 + 2] = (byte)(int)(r * 255);
            }
            return pal;
        }

        public static void WriteBmp(string path, int w, int h, byte[] pixels)
        {
            byte[] pal = MakePalette();
            int stride = w;   // only exact multiples of 4 are produced, as in the Python version
            var rows = new byte[w * h];
            for (int y = h - 1, o = 0; y >= 0; y--, o += w) Buffer.BlockCopy(pixels, y * w, rows, o, w);
            int off = 14 + 40 + pal.Length;
            var o2 = new ByteWriter();
            o2.U8('B'); o2.U8('M'); o2.U32(off + rows.Length); o2.U16(0); o2.U16(0); o2.U32(off);
            o2.U32(40); o2.U32(w); o2.U32(h); o2.U16(1); o2.U16(8); o2.U32(0); o2.U32(rows.Length); o2.U32(2835); o2.U32(2835); o2.U32(256); o2.U32(0);
            o2.Bytes(pal); o2.Bytes(rows);
            File.WriteAllBytes(path, o2.ToArray());
            _ = stride;
        }

        /// <summary>colors: (palette index, (L,T,R,B) in bitmap pixels). Draws the rectangles so that the
        /// bounding rectangle of every colour is exactly as in the .dat.</summary>
        public static byte[] PaintBitmap(List<(int c, long[] r)> colors, int w, int h, Action<string> warn)
        {
            var px = new byte[w * h];
            long Area(long[] r) => (r[2] - r[0]) * (r[3] - r[1]);
            if (colors.Any(c => c.r[2] > w || c.r[3] > h))
            {
                warn("some rectangles extend beyond the bitmap - they are clipped");
                colors = colors.Select(c => (c.c, new[] { Math.Min(c.r[0], w), Math.Min(c.r[1], h), Math.Min(c.r[2], w), Math.Min(c.r[3], h) })).ToList();
            }
            // 1) fills: largest first, smaller ones on top
            foreach (var (c, r) in colors.OrderBy(x => -Area(x.r)))
                for (long y = r[1]; y < r[3]; y++)
                    for (long x = r[0]; x < r[2]; x++) px[y * w + x] = (byte)c;
            // 2) extreme pixels (they define the bounding rectangle) - each pixel used only once
            var taken = new HashSet<long>();
            long P(long x, long y) => (y << 20) | x;
            foreach (var (c, r) in colors.OrderBy(x => Area(x.r)))
            {
                long L = r[0], T = r[1], R = r[2], B = r[3];
                var options = new[] { new[] { (L, T), (R - 1, B - 1) }, new[] { (R - 1, T), (L, B - 1) } };
                List<(long, long)> chosen = null;
                foreach (var opt in options)
                    if (!opt.Any(q => taken.Contains(P(q.Item1, q.Item2)))) { chosen = opt.ToList(); break; }
                if (chosen == null)
                {
                    chosen = new List<(long, long)>();
                    var edges = new List<List<(long, long)>>
                    {
                        Enumerable.Range(0, (int)Math.Max(0, B - T)).Select(k => (L, T + k)).ToList(),
                        Enumerable.Range(0, (int)Math.Max(0, B - T)).Select(k => (R - 1, T + k)).ToList(),
                        Enumerable.Range(0, (int)Math.Max(0, R - L)).Select(k => (L + k, T)).ToList(),
                        Enumerable.Range(0, (int)Math.Max(0, R - L)).Select(k => (L + k, B - 1)).ToList(),
                    };
                    foreach (var e in edges)
                    {
                        (long, long)? q = null;
                        foreach (var cand in e)
                            if (chosen.Contains(cand) || !taken.Contains(P(cand.Item1, cand.Item2))) { q = cand; break; }
                        if (q == null) { warn($"colour {c}: the rectangle cannot be reproduced (too many overlapping colours)"); break; }
                        chosen.Add(q.Value);
                    }
                }
                foreach (var (x, y) in chosen)
                {
                    taken.Add(P(x, y));
                    if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = (byte)c;
                }
            }
            return px;
        }

        /// <summary>Splits a long CustomData line into lines ending with '\' (the compiler joins them back,
        /// inserting a space). Splits only where this is lossless.</summary>
        public static List<string> SplitLong(string s, int limit = 200)
        {
            var parts = new List<string>();
            while (s.Length > limit)
            {
                int k = -1;
                for (int j = limit; j > 0; j--)
                    if (s[j] == ' ' && j + 1 < s.Length && s[j + 1] != ' ' && s[j + 1] != '\t' && s[0] != ' ' && s[0] != '\t') { k = j; break; }
                if (k < 0) break;
                parts.Add(s.Substring(0, k) + "\\");
                s = s.Substring(k + 1);
            }
            parts.Add(s);
            return parts;
        }

        static string JoinRel(string dir, string rel) =>
            System.IO.Path.Combine(new[] { dir }.Concat(rel.Replace('/', '\\').Split('\\')).ToArray());

        public static string FmtName(string fmt)
        {
            switch (fmt)
            {
                case "e2150": return "Earth 2150";
                case "ks": return $"KnightShift (compile with: {ToolName} compile --ks)";
                case "e2160": return $"Earth 2160 (compile with: {ToolName} compile --e2160)";
                case "tw": return $"TwoWorlds (compile with: {ToolName} compile --tw)";
                default: return $"Two Worlds II (compile with: {ToolName} compile --tw2)";
            }
        }

        static readonly Regex FormatLineRe = new Regex(@"^//\s*Format:\s*(.*)$");

        /// <summary>The format written in the header of a decompiled definition file ("// Format: ..."),
        /// or null. Works for files of the Python tools and of this program.</summary>
        public static string FormatFromTxt(string path)
        {
            try
            {
                string head;
                using (var f = File.OpenRead(path))
                {
                    var buf = new byte[4096];
                    int n = f.Read(buf, 0, buf.Length);
                    head = Text.Latin1.GetString(buf, 0, n);
                }
                foreach (var raw in head.Split('\n').Take(20))
                {
                    var m = FormatLineRe.Match(raw.TrimEnd('\r'));
                    if (!m.Success) continue;
                    string t = m.Groups[1].Value;
                    var sw = Regex.Match(t, @"--([a-zA-Z0-9_]+)");
                    if (sw.Success && Fmt.FromSwitch(sw.Groups[1].Value) != null) return Fmt.FromSwitch(sw.Groups[1].Value);
                    foreach (var kv in Fmt.Names.OrderByDescending(k => k.Value.Length))
                        if (t.StartsWith(kv.Value, StringComparison.OrdinalIgnoreCase)) return kv.Key;
                }
            }
            catch (IOException) { }
            return null;
        }

        /// <summary>tileOverride / widthOverride: {material name in lower case: pixels}; width: default bitmap
        /// width (256 if null). names: "auto" = embedded names, a path = names from an original
        /// definition file, null = no names.</summary>
        public static DecompileResult Decompile(string datPath, string outDir, Dictionary<string, int> tileOverride,
            int? width = null, string resourcePath = null, string fmt = null, Dictionary<string, int> widthOverride = null,
            string names = "auto", byte[] datBytes = null)
        {
            var result = new DecompileResult();
            var warnings = result.Warnings;
            void Warn(string m) => warnings.Add(m);
            tileOverride = tileOverride ?? new Dictionary<string, int>();
            byte[] dat = datBytes ?? File.ReadAllBytes(datPath);
            var d = DatFile.Read(dat, fmt);
            foreach (var a in d.ItemAnomalies) Warn(a + " - cannot be reproduced");
            foreach (var n in d.ItemRepeatOrder) Warn($"item {n} is written {d.ItemRepeats[n] + 1} times in the file (identical copies) - reproduced with RepeatItem");
            NameDb db = null;
            if (names != null)
            {
                db = new NameDb();
                if (names != "auto")
                {
                    try { db.LoadUserNames(names); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Warn($"cannot read names from {names}: {e.Message}"); }
                }
            }
            if (db != null)
            {
                string vname = db.SelectFor(d.Fmt, d.Items.Keys);
                if (vname.Length > 0) Warn($"item names: using the names of '{vname}' (chosen by the item numbers of this file)");
            }
            string ItemLabel(long n) { string s = db?.ItemName(d.Fmt, n) ?? ""; return s.Length > 0 ? " " + NameDb.TextSafe(s) : ""; }
            string StateLabel(long n, long sn) { string s = db?.StateName(d.Fmt, n, sn) ?? ""; return s.Length > 0 ? " " + NameDb.TextSafe(s) : ""; }
            bool e2160 = Fmt.IsNew(d.Fmt);
            string resFile = Fmt.ResourceFile(d.Fmt);

            // --- header
            var headerLines = new List<string>();
            if (d.Fmt == "e2160" || !(d.BaseW == 640 && d.BaseH == 480 && d.ScaleX == 1.0f && d.ScaleY == 1.0f))
                headerLines.Add($"Scale {d.BaseW}, {d.BaseH}, {FmtFloat(d.ScaleX)}, {FmtFloat(d.ScaleY)}");
            long g = d.GlobalFlags;
            if ((g & Flags.ScaleBorder) != 0) headerLines.Add("DefaultScaleBorder 1");
            if ((g & Flags.ScaleBackground) != 0) headerLines.Add("DefaultScaleBackground 1");
            if ((g & ~(long)(Flags.ScaleBorder | Flags.ScaleBackground)) != 0) headerLines.Add($"GlobalFlags 0x{g:X}");

            // --- fonts: every unique font = one number, the colour stays in the font or goes to Color
            var fontLines = new List<string>();
            for (int i = 0; i < d.Fonts.Count; i++)
            {
                var f = d.Fonts[i];
                long sx = f.Sx > 127 ? f.Sx - 256 : f.Sx, sy = f.Sy > 127 ? f.Sy - 256 : f.Sy;
                if (Parser.SplitFontColor(f.Color, f.SColor, sx, sy) != (f.Color, f.SColor, 0xFFFFFFFFL))
                    Warn($"font {i} ({f.Name}) may not be reproducible exactly");
                fontLines.Add($"{i}: {f.Name}, {f.Size}, {f.Weight}, {f.Italic}, 0x{f.Color:X8}, {sx}, {sy}, 0x{f.SColor:X8}" + (d.Fmt == "tw2" ? $", {f.Extra}" : ""));
            }

            // --- materials and colours
            List<(int t, long[] r)> SlotsOf(int m) =>
                d.Items.Values.SelectMany(it => it.States.Values).SelectMany(st => st.Slots)
                    .Where(s => s.Mt != 0xFFFF && (s.Mt & 0xFF) == m).Select(s => (s.Mt >> 8, s.R)).ToList();
            var distinct = new HashSet<string>(d.Items.Values.SelectMany(it => it.States.Values).SelectMany(st => st.Slots)
                .Where(s => s.Mt != 0xFFFF).Select(s => $"{s.Mt & 0xFF}|{(e2160 ? 0 : s.Mt >> 8)}|{string.Join(",", s.R)}"));
            bool perMaterial = distinct.Count > 255;
            var wOverride = new Dictionary<string, int>();
            foreach (var kv in widthOverride ?? new Dictionary<string, int>()) wOverride[kv.Key.ToLowerInvariant()] = kv.Value;
            if (e2160 && (width != null || wOverride.Count > 0 || tileOverride.Count > 0))
                Warn("--tile and --width apply only to Earth 2150 / KnightShift (no tiles in this format) - ignored");
            else
            {
                var known = new HashSet<string>(d.Materials.Select(n => n.ToLowerInvariant()));
                foreach (var (opt, nm) in new[] { ("--tile", tileOverride), ("--width", wOverride) })
                    foreach (var n in nm.Keys)
                        if (!known.Contains(n)) Warn($"{opt} {n}=...: there is no such material (materials: {string.Join(", ", d.Materials)})");
            }
            var matInfo = new List<BitmapInfo>();
            for (int m = 0; m < d.Materials.Count; m++)
            {
                string name = d.Materials[m];
                var sl = SlotsOf(m);
                int bas = (e2160 && perMaterial) ? m + 1 : 0;
                if (e2160)
                {
                    long mw = sl.Count > 0 ? sl.Max(x => x.r[2]) : 1, mh = sl.Count > 0 ? sl.Max(x => x.r[3]) : 1;
                    int W = Compiler.E2160Sizes.FirstOrDefault(s => s >= mw), H = Compiler.E2160Sizes.FirstOrDefault(s => s >= mh);
                    if (W == 0 || H == 0)
                    {
                        Warn($"material {name}: rectangles extend beyond 512 px - the bitmap will not be valid");
                        if (W == 0) W = 512; if (H == 0) H = 512;
                    }
                    matInfo.Add(new BitmapInfo { Material = name, Width = W, Height = H, Base = bas });
                }
                else
                {
                    long ext = sl.Count > 0 ? sl.Max(x => Math.Max(x.r[2], x.r[3])) : 1;
                    int tile = ChooseTile(name, ext, tileOverride);
                    if (tile < ext)
                    {
                        int guess = ChooseTile(name, ext, new Dictionary<string, int>());
                        Warn($"material {name}: --tile {tile} is smaller than its largest rectangle ({ext} px) - ignored, using {guess}");
                        tile = guess;
                    }
                    int req = wOverride.TryGetValue(name.ToLowerInvariant(), out int wr) ? wr : (width ?? 256);
                    if (req < tile) Warn($"material {name}: width {req} px is smaller than the tile size {tile} - using 1 tile per row");
                    else if (req % tile != 0) Warn($"material {name}: width {req} px is not a multiple of the tile size {tile} - using {req / tile * tile} px");
                    int perRow = Math.Max(1, req / tile);
                    int rows = (sl.Count > 0 ? sl.Max(x => x.t) : 0) / perRow + 1;
                    matInfo.Add(new BitmapInfo { Material = name, Tile = tile, TilesPerRow = perRow, Rows = rows, Width = perRow * tile, Height = rows * tile, Base = bas });
                }
            }

            var bmpNames = d.Materials.Select(n => Compiler.BitmapName(n, d.Fmt)).ToList();
            foreach (var n in new HashSet<string>(bmpNames))
                if (bmpNames.Count(x => x == n) > 1) Warn($"several materials use the same bitmap {n} - the result will not be valid");

            var colorOf = new Dictionary<string, int>();
            var perMatColors = Enumerable.Range(0, d.Materials.Count).ToDictionary(m => m, m => new List<(int, long[])>());
            string CKey(int m, int t, long[] r) => $"{m}|{t}|{string.Join(",", r)}";
            foreach (var ikv in d.Items)
                foreach (var skv in ikv.Value.States)
                    foreach (var s in skv.Value.Slots)
                    {
                        if (s.Mt == 0xFFFF) continue;
                        int m = s.Mt & 0xFF;
                        string key = CKey(m, e2160 ? 0 : s.Mt >> 8, s.R);
                        if (colorOf.ContainsKey(key)) continue;
                        var r = s.R;
                        if (r[2] <= r[0] || r[3] <= r[1]) Warn($"Item {ikv.Key} State {skv.Key}: empty rectangle with a material - cannot be reproduced");
                        if (!perMatColors.ContainsKey(m)) throw new ToolError($"Item {ikv.Key} State {skv.Key}: material number {m} does not exist");
                        colorOf[key] = (perMaterial ? perMatColors[m].Count : colorOf.Count) + 1;
                        var mi = matInfo[m];
                        long[] absr;
                        if (e2160) absr = r;
                        else
                        {
                            int t = s.Mt >> 8;
                            long ox = (t % mi.TilesPerRow) * mi.Tile, oy = (t / mi.TilesPerRow) * mi.Tile;
                            absr = new[] { ox + r[0], oy + r[1], ox + r[2], oy + r[3] };
                        }
                        perMatColors[m].Add((colorOf[key], absr));
                    }
            foreach (var kv in perMatColors)
                if (kv.Value.Count > 255)
                    throw new ToolError($"Bitmap {d.Materials[kv.Key]}: too many different rectangles ({kv.Value.Count}) - the palette has only 255 colours");

            string SlotLine(int i, int mt, long[] r)
            {
                int m = mt & 0xFF;
                int c = colorOf[CKey(m, e2160 ? 0 : mt >> 8, r)];
                if (e2160)
                {
                    int bs = matInfo[m].Base;
                    return bs == 0 ? $"{i + 1}: {bs * 256 + c}" : $"{i + 1}: 0x{bs:X2}{c:X2}";
                }
                return perMaterial ? $"{i + 1}: {d.Materials[m]}/{c}" : $"{i + 1}: {c}";
            }

            Directory.CreateDirectory(outDir);
            // The bitmap names come from the material names, so two .dat files with the same materials
            // exported into one folder overwrite each other's bitmaps.
            string myTxt = System.IO.Path.GetFileNameWithoutExtension(datPath).ToLowerInvariant() + ".txt";
            var mine = new HashSet<string>(bmpNames.Select(n => n.ToLowerInvariant()));
            foreach (var other in Directory.GetFiles(outDir).Select(System.IO.Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!other.ToLowerInvariant().EndsWith(".txt", StringComparison.Ordinal) || other.ToLowerInvariant() == myTxt) continue;
                string head;
                try
                {
                    using (var fs = File.OpenRead(System.IO.Path.Combine(outDir, other)))
                    {
                        var buf = new byte[4096];
                        int n = fs.Read(buf, 0, buf.Length);
                        head = Text.Latin1.GetString(buf, 0, n);
                    }
                }
                catch (IOException) { continue; }
                var shared = Regex.Matches(head, @"//\s*Bitmap\s+(.+?)\s+crc32").Cast<Match>().Select(x => x.Groups[1].Value)
                    .Where(n => mine.Contains(n.ToLowerInvariant())).OrderBy(x => x, StringComparer.Ordinal).ToList();
                if (shared.Count > 0)
                    Warn($"{other} in this folder uses the same bitmaps ({string.Join(", ", shared)}) - they are overwritten now" +
                         (e2160 ? $" (and {resFile})" : "") + $", so {other} will no longer compile correctly. Export every .dat file into its own folder.");
            }
            var bitmapCrc = new List<string>();
            for (int m = 0; m < matInfo.Count; m++)
            {
                var mi = matInfo[m];
                byte[] px = PaintBitmap(perMatColors[m], mi.Width, mi.Height, Warn);
                string bpath = JoinRel(outDir, bmpNames[m]);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(bpath));
                WriteBmp(bpath, mi.Width, mi.Height, px);
                mi.Path = bpath;
                bitmapCrc.Add($"// Bitmap {bmpNames[m]} crc32 0x{Text.Crc32(File.ReadAllBytes(bpath)):X8}");
            }

            // --- particles (Earth 2160): common path prefix -> ParticlesPrefixPath
            string prefix = "";
            var particles = new SortedSet<string>(d.Items.Values.SelectMany(it => it.States.Values).Select(st => st.Particle).Where(p => p.Length > 0), StringComparer.Ordinal).ToList();
            if (particles.Count > 0)
            {
                prefix = Text.CommonPrefix(particles);
                prefix = prefix.Contains("\\") ? prefix.Substring(0, prefix.LastIndexOf('\\') + 1) : "";
                if (prefix.Contains(" ") || prefix.Contains("\t")) prefix = "";
                foreach (var p in particles)
                {
                    string rest = p.Substring(prefix.Length);
                    if (!rest.Contains(".prt") || Text.NormalizeLine(rest) != rest || Text.CTrim(prefix + rest) != p)
                        Warn($"particle path '{p}' cannot be written exactly");
                }
            }

            // --- items
            var itemBlocks = new List<(long num, List<string> flags, List<string> props, List<(long, List<string>)> states)>();
            var usedFonts = new HashSet<long>();
            List<string> firstFontList = null;
            var soundOrder = new List<long>();
            foreach (var ikv in d.Items)
            {
                long num = ikv.Key; var item = ikv.Value;
                long[] margins = e2160 ? InferMargins(item) : null;
                long? fl = InferNoFlags(item, margins);
                if (fl == null) { Warn($"Item {num}: could not find matching No* flags - filled-in rectangles may differ"); fl = item.Flags & 0xFF; }
                long ext = d.Fmt != "e2150" ? (item.Flags & ~0xFFL & ~g) : 0;
                var itemLines = FlagLines(fl.Value, ext, d.Fmt);
                if (d.Fmt != "e2150" && d.Fmt != "tw" && d.Fmt != "tw2" && item.Scale != 1.0f) itemLines.Add($"SizeScale {FmtFloat(item.Scale)}");
                if (e2160)
                {
                    var mn = new[] { "LeftMargin", "TopMargin", "RightMargin", "BottomMargin" };
                    for (int k = 0; k < 4; k++) if (margins[k] != 0) itemLines.Add($"{mn[k]} {margins[k]}");
                    if (item.ShowStartTime != -1.0f) itemLines.Add($"ShowStartTime {FmtFloat(item.ShowStartTime)}");
                }
                var states = item.States;
                // properties shared by all states -> item level
                bool allBlob = states.Values.All(s => s.HasBlob);
                long? commonFont = null;
                if (allBlob)
                {
                    var fonts = new HashSet<long>(states.Values.Select(s => s.Font));
                    if (fonts.Count == 1 && !fonts.Contains(-1)) commonFont = fonts.First();
                }
                var commonRecs = new List<Record>();
                if (allBlob && states.Count > 0)
                {
                    var first = item.InFileOrder().First().Value;
                    foreach (var rec in first.Records)
                        if (states.Values.All(s => s.Records.Any(x => x.SameAs(rec)))) commonRecs.Add(rec);
                }
                var itemProps = new List<string>();
                if (commonFont != null) { itemProps.Add($"Font {commonFont}"); usedFonts.Add(commonFont.Value); }
                itemProps.AddRange(commonRecs.Select(r => RecordLine(r.Key, r.Values)));
                var stateBlocks = new List<(long, List<string>)>();
                foreach (var skv in states)
                {
                    long sn = skv.Key; var st = skv.Value;
                    var lines = new List<string>();
                    if (st.Font != -1 && st.Font != commonFont) { lines.Add($"Font {st.Font}"); usedFonts.Add(st.Font); }
                    if (st.Sound != -1)
                    {
                        if (st.Sound >= d.Sounds.Count) throw new ToolError($"Item {num} State {sn}: sound number {st.Sound} does not exist");
                        lines.Add($"Sound {d.Sounds[(int)st.Sound]}");
                        if (!soundOrder.Contains(st.Sound)) soundOrder.Add(st.Sound);
                    }
                    lines.AddRange(st.Records.Where(r => !commonRecs.Any(c => c.SameAs(r))).Select(r => RecordLine(r.Key, r.Values)));
                    if (st.HasBlob && st.Font == -1 && st.Sound == -1 && st.Records.Count == 0)
                        Warn($"Item {num} State {sn}: empty property block - cannot be reproduced");
                    if (st.FaceColor != 0xFFFFFFFFL) lines.Add($"FaceColor 0x{st.FaceColor:X8}");
                    if (d.Fmt == "tw2")
                    {
                        if (st.FaceColor2 != 0xFFFFFFFFL) lines.Add($"TextColor 0x{st.FaceColor2:X8}");
                        if (st.Texture.Length > 0)
                        {
                            string t = "TextTexture " + st.Texture;
                            if (Text.NormalizeLine(t) != t || st.Texture.Contains(".prt")) Warn($"texture '{st.Texture}' cannot be written exactly");
                            lines.Add(t);
                        }
                        if (st.TextureMargin != 0) lines.Add($"TextTextureParam {S32(st.TextureMargin)}");
                    }
                    if (st.Particle.Length > 0) lines.Add(st.Particle.Substring(prefix.Length));
                    for (int i = 0; i < st.Slots.Count; i++)
                        if (st.Slots[i].Mt != 0xFFFF && st.Particle.Length == 0) lines.Add(SlotLine(i, st.Slots[i].Mt, st.Slots[i].R));
                    stateBlocks.Add((sn, lines));
                }
                itemBlocks.Add((num, itemLines, itemProps, stateBlocks));
                if (firstFontList == null)
                {
                    if (commonFont != null) firstFontList = itemProps;
                    else
                        foreach (var (_, lines) in stateBlocks)
                            if (lines.Count > 0 && lines[0].StartsWith("Font ", StringComparison.Ordinal)) { firstFontList = lines; break; }
                }
            }

            // fonts not used in any state: "Font X" right before the first real "Font"
            var unused = Enumerable.Range(0, d.Fonts.Count).Where(i => !usedFonts.Contains(i)).ToList();
            if (unused.Count > 0 && !e2160)
            {
                if (firstFontList == null) Warn("some fonts are not used in any state - compilation will report 'Some fonts remain unused'");
                else firstFontList.InsertRange(0, unused.Select(i => $"Font {i}"));
            }

            // sound order: if the natural order does not match, register them in the first item
            if (!soundOrder.SequenceEqual(Enumerable.Range(0, d.Sounds.Count).Select(i => (long)i)) && itemBlocks.Count > 0)
                itemBlocks[0].props.InsertRange(0, d.Sounds.Select(s => $"Sound {s}"));

            // --- CustomData and DialogTemplates (Earth 2160)
            var customLines = new List<string>();
            foreach (var kv in d.CustomData)
            {
                var lst = kv.Value;
                if (lst.Count == 1 && lst[0].Length > 0 && Text.ScanToken(lst[0], 0, out _) == lst[0])
                {
                    customLines.Add($"CustomData {kv.Key} {lst[0]}");
                    continue;
                }
                customLines.Add($"CustomData {kv.Key}"); customLines.Add("{");
                foreach (var s in lst)
                {
                    if (s.Length == 0 || s.StartsWith("//", StringComparison.Ordinal) || s.EndsWith("\\", StringComparison.Ordinal) || Text.CTrim(s) != s)
                        Warn($"CustomData {kv.Key}: line '{s}' cannot be written exactly");
                    customLines.AddRange(SplitLong(s).Select(part => "\t" + part));
                }
                customLines.Add("}");
            }

            ResourceNames res = null;
            var dialogLines = new List<string>();
            if (e2160)
            {
                if (resourcePath == null)
                {
                    foreach (var cand in new[] { System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(datPath)), resFile),
                                                 System.IO.Path.Combine(Directory.GetCurrentDirectory(), resFile) })
                        if (File.Exists(cand)) { resourcePath = cand; break; }
                }
                if (d.Dialogs.Count > 0 && resourcePath == null) Warn($"no {resFile} - identifiers will be named _ID_<number>");
                res = new ResourceNames(resourcePath);
                var forceNames = new[] { "ForceMessageBoxBackground", "ForceNoMessageBoxBackground", "ForceFullScreenTransparentBackground", "ForceNoFullScreenTransparentBackground" };
                foreach (var dlg in d.Dialogs)
                {
                    string dname = res.NameFor(dlg.Id, "IDD_", "MMD_");
                    string Note(string c) => db != null ? NameDb.TextSafe(db.ControlNote(d.Fmt, dname, c)) : "";
                    if (Note(null).Length > 0) dialogLines.Add($"// {Note(null)}");
                    dialogLines.Add($"DialogTemplates {dname}"); dialogLines.Add("{");
                    if (dlg.Item >= 0) dialogLines.Add($"\t{dname} = {dlg.Item}");
                    else if (dlg.Item != -1) Warn($"DialogTemplates {dname}: item number {dlg.Item} cannot be reproduced");
                    for (int k = 0; k < 4; k++)
                    {
                        if (dlg.Force[k] == 1) dialogLines.Add($"\t{forceNames[k]}");
                        else if (dlg.Force[k] != 0) Warn($"DialogTemplates {dname}: value {dlg.Force[k]} of flag {forceNames[k]} cannot be reproduced");
                    }
                    foreach (var c in dlg.Controls)
                    {
                        long rid = c[0], a = c[1], b = c[2], cc = c[3];
                        string vals = $"{a}" + (cc != 0 ? $" {b} {cc}" : (b != 0 ? $" {b}" : ""));
                        if (rid == dlg.Id)
                        {
                            string nm = res.Synthetic($"_DUP{(rid >= 0 ? rid.ToString(CultureInfo.InvariantCulture) : "M" + (-rid))}_0", rid);
                            dialogLines.Add($"\t{nm.Substring(0, nm.Length - 1)}[0-0] = {vals}");
                        }
                        else
                        {
                            string cname = res.NameFor(rid, "IDC_", "ID_");
                            if (Note(cname).Length > 0) dialogLines.Add($"\t// {Note(cname)}");
                            dialogLines.Add($"\t{cname} = {vals}");
                        }
                    }
                    dialogLines.Add("}"); dialogLines.Add("");
                }
                res.Write(System.IO.Path.Combine(outDir, resFile));
            }

            // --- assembling the text
            string fmtName = FmtName(d.Fmt);
            var o = new List<string> { $"// Rebuilt from {System.IO.Path.GetFileName(datPath)} by {ToolName}", $"// Format: {fmtName}", "" };
            if (e2160)
            {
                o.Add("Materials"); o.Add("{");
                for (int m = 0; m < d.Materials.Count; m++) o.Add("\t" + (matInfo[m].Base != 0 ? $"{d.Materials[m]}:{matInfo[m].Base}" : d.Materials[m]));
                o.Add("}"); o.Add("");
            }
            o.AddRange(headerLines); if (headerLines.Count > 0) o.Add("");
            if (prefix.Length > 0) { o.Add($"ParticlesPrefixPath {prefix}"); o.Add(""); }
            o.AddRange(customLines); if (customLines.Count > 0) o.Add("");
            o.Add("Fonts"); o.Add("{");
            o.Add("//nr: font, size, weight, italic, font color, shadow x, shadow y, shadow color" + (d.Fmt == "tw2" ? ", lazy (1 = font created on first use)" : ""));
            o.AddRange(fontLines); o.Add("}"); o.Add("");
            foreach (var bl in itemBlocks)
            {
                if (db != null && db.ItemNote(d.Fmt, bl.num).Length > 0) o.Add($"// {NameDb.TextSafe(db.ItemNote(d.Fmt, bl.num))}");
                o.Add($"Item:{bl.num}{ItemLabel(bl.num)}");
                o.Add("{");
                o.AddRange(bl.flags); o.AddRange(bl.props);
                foreach (var (sn, lines) in bl.states)
                {
                    o.Add($"State:0x{sn:X2}{StateLabel(bl.num, sn)}");
                    o.Add("\t{");
                    o.AddRange(lines.Select(l => "\t" + l));
                    o.Add("\t}");
                }
                o.Add("}");
                int reps = d.ItemRepeats.TryGetValue(bl.num, out int rr) ? rr : 0;
                for (int k = 0; k < reps; k++) { o.Add("// in the original file this item was written once more"); o.Add($"RepeatItem {bl.num}"); }
            }
            o.Add("");
            o.AddRange(dialogLines);
            if (!e2160)
            {
                o.Add("Materials"); o.Add("{");
                o.AddRange(matInfo.Select(mi => $"{mi.Material}:{mi.Tile}"));
                o.Add("}"); o.Add("");
            }
            // checksums of the bitmaps: the compiler warns when a bitmap was replaced or changed
            o.InsertRange(2, bitmapCrc.Concat(new[] { "// (the compiler checks these: they show whether the bitmaps belong to this file)" }));
            string bse = System.IO.Path.GetFileNameWithoutExtension(datPath);
            string txtPath = System.IO.Path.Combine(outDir, bse + ".txt");
            File.WriteAllBytes(txtPath, Text.ToLatin1(string.Join("\r\n", o)));

            // --- verification: compile back
            byte[] rebuilt = null;
            try { rebuilt = Compiler.Build(txtPath, false, d.Fmt, outDir); }
            catch (Exception e) when (e is DefinitionError || e is BitmapError) { Warn($"recompilation failed: {e.Message}"); }

            Log.Line($"Written: {txtPath}");
            foreach (var mi in matInfo)
            {
                string extra = mi.Tile != 0 ? $"tile {mi.Tile}, {mi.TilesPerRow}x{mi.Rows} tiles, {mi.Width}x{mi.Height} px" : $"{mi.Width}x{mi.Height} px";
                Log.Line($"Written: {mi.Path}  ({extra})");
            }
            if (res != null)
                Log.Line($"Written: {System.IO.Path.Combine(outDir, resFile)}" + (res.Extra.Count > 0 ? $"  ({res.Extra.Count} identifiers added)" : ""));
            Log.Line($"Format: {fmtName}");
            Log.Line($"Colours: {colorOf.Count}, items: {d.Items.Count}, fonts: {d.Fonts.Count}, sounds: {d.Sounds.Count}" +
                     (e2160 ? $", CustomData: {d.CustomData.Count}, DialogTemplates: {d.Dialogs.Count}" : ""));
            foreach (var w in warnings) Log.Line("Warning: " + w);
            result.Format = d.Fmt; result.TxtPath = txtPath; result.Bitmaps = matInfo;
            if (rebuilt != null && rebuilt.SequenceEqual(dat))
            {
                Log.Line("Verification: recompilation gives an identical .dat file");
                result.Ok = true;
                return result;
            }
            if (rebuilt != null && d.Fmt == "e2150" && rebuilt.Length == dat.Length + 1 && rebuilt.Take(dat.Length).SequenceEqual(dat) && rebuilt[dat.Length] == 0xFF)
            {
                Log.Line("Verification: identical except for the last byte - the input file has a shorter end marker\n" +
                         "(written by another tool); the game reads both variants the same way");
                result.Ok = true;
                return result;
            }
            if (rebuilt != null)
            {
                int n = Math.Min(rebuilt.Length, dat.Length), diff = n;
                for (int i = 0; i < n; i++) if (rebuilt[i] != dat[i]) { diff = i; break; }
                Log.Line($"Verification: DIFFERENCE from byte {diff} (0x{diff:x})");
            }
            return result;
        }

        /// <summary>Command "decompile".</summary>
        public static int Run(IList<string> argv)
        {
            var tiles = new Dictionary<string, int>();
            var widths = new Dictionary<string, int>();
            int? width = null;
            string resource = null, fmt = null, names = "auto";
            var pos = new List<string>();
            for (int i = 0; i < argv.Count; i++)
            {
                string a = argv[i];
                if ((a == "--tile" || a == "--width") && i + 1 < argv.Count)
                {
                    string val = argv[++i];
                    int eq = val.LastIndexOf('=');
                    string n = eq >= 0 ? val.Substring(0, eq) : "", v = eq >= 0 ? val.Substring(eq + 1) : val;
                    if (!int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int px) || px <= 0)
                    {
                        Log.Line($"Invalid value for {a}: '{val}' (expected " + (a == "--tile" ? "name=pixels" : "pixels or name=pixels") + ")");
                        return 1;
                    }
                    if (a == "--tile")
                    {
                        if (eq < 0 || n.Length == 0) { Log.Line($"Invalid value for --tile: '{val}' (expected name=pixels)"); return 1; }
                        tiles[n.ToLowerInvariant()] = px;
                    }
                    else if (eq >= 0) widths[n.ToLowerInvariant()] = px;
                    else width = px;
                }
                else if (a == "--names" && i + 1 < argv.Count) names = argv[++i];
                else if (a == "--no-names") names = null;
                else if (a == "--format" && i + 1 < argv.Count)
                {
                    fmt = argv[++i].ToLowerInvariant();
                    if (!Fmt.All.Contains(fmt)) { Log.Line($"Unknown format '{fmt}' (available: {string.Join(", ", Fmt.All)})"); return 1; }
                }
                else if (a == "--resource" && i + 1 < argv.Count) resource = argv[++i];
                else pos.Add(a);
            }
            if (pos.Count == 0)
            {
                Log.Line("Usage: InterfaceTools decompile file.dat [output_folder] [--resource file.h] [--names original.txt | --no-names]\n" +
                         "                    [--tile name=pixels ...] [--width pixels] [--width name=pixels ...] [--format f]");
                return 1;
            }
            string outDir = pos.Count > 1 ? pos[1] : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(pos[0])),
                                                                           System.IO.Path.GetFileNameWithoutExtension(pos[0]) + "_src");
            return Decompile(pos[0], outDir, tiles, width, resource, fmt, widths, names).Ok ? 0 : 1;
        }
    }
}

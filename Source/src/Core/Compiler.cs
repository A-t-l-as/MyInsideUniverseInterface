// InterfaceMap compiler: definition file (.txt) + "<name>@.bmp" bitmaps -> interface .dat.
// Port of interfacemap.py (behaviour reconstructed from the original Earth 2150 / Earth 2160
// programs and verified byte for byte against the original .dat files). See FORMAT.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace InterfaceTools
{
    public static class Fmt
    {
        public const string E2150 = "e2150", KS = "ks", E2160 = "e2160", TW = "tw", TW2 = "tw2";
        public static readonly string[] All = { E2150, KS, E2160, TW, TW2 };
        /// <summary>Other Reality Pump games whose interface files use the Earth 2150 format.</summary>
        public const string E2150Family = "Heli Heroes, World War III: Black Gold, World War II: Panzer Claws / Frontline Attack: War over Europe";

        public static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { E2150, "Earth 2150" }, { KS, "KnightShift" }, { E2160, "Earth 2160" }, { TW, "TwoWorlds" }, { TW2, "Two Worlds II" }
        };
        /// <summary>Shared syntax: CustomData, DialogTemplates, particles...</summary>
        public static bool IsNew(string f) => f == E2160 || f == TW || f == TW2;
        /// <summary>No resolution / scale in the header, no SizeScale.</summary>
        public static bool IsTw(string f) => f == TW || f == TW2;
        public static string ResourceFile(string f) => f == E2160 ? "resource.h" : "DialogsResource.h";

        /// <summary>Format from a command-line switch such as --tw / --twoworlds; null if not a switch.</summary>
        public static string FromSwitch(string a)
        {
            switch (a.ToLowerInvariant().TrimStart('-'))
            {
                case "ks": case "knightshift": return KS;
                case "e2160": case "earth2160": return E2160;
                case "tw": case "twoworlds": return TW;
                case "tw2": case "twoworlds2": return TW2;
                case "e2150": case "earth": case "earth2150": return E2150;
            }
            return null;
        }
    }

    public class Font
    {
        public string Name; public long Size, Weight, Italic, Color, ShadowX, ShadowY, ShadowColor;
        /// <summary>Two Worlds II: 1 = font created only on first use.</summary>
        public long Extra;
        public bool Used;
        public string Key() => string.Join("|", Text.AsciiLower(Name), Size, Weight, Italic, Color,
            ShadowX & 0xFF, ShadowY & 0xFF, ShadowColor, Extra & 0xFF);
    }

    /// <summary>Property block (stored in the .dat as an array of u32).
    /// Header: [font, sound], then records (key&lt;&lt;16 | length) + values, sorted by key.</summary>
    public class Properties
    {
        public bool Allocated;
        public long Font = -1, Sound = -1;
        public SortedDictionary<int, List<long>> Records = new SortedDictionary<int, List<long>>();

        public void SetRecord(int key, List<long> values)
        {
            Allocated = true;
            Records[key] = values.Select(v => v & 0xFFFFFFFFL).ToList();
        }

        /// <summary>Inheritance (from the item or via Identity): the font is copied if the target has none;
        /// records are copied if the target lacks that key; the sound is NOT copied.</summary>
        public void MergeFrom(Properties src)
        {
            if (!src.Allocated) return;
            if (Font == -1 && src.Font != -1) { Font = src.Font; Allocated = true; }
            foreach (var kv in src.Records)
                if (!Records.ContainsKey(kv.Key)) { Records[kv.Key] = new List<long>(kv.Value); Allocated = true; }
        }

        public List<long> ToDwords()
        {
            var o = new List<long>();
            if (!Allocated) return o;
            o.Add(Font & 0xFFFFFFFFL); o.Add(Sound & 0xFFFFFFFFL);
            foreach (var kv in Records) { o.Add(((long)kv.Key << 16) | (long)kv.Value.Count); o.AddRange(kv.Value); }
            return o;
        }
    }

    public class CState
    {
        public const int Slots = 9;   // 1 background, 2 top, 3 bottom, 4 left, 5 right, 6 TL, 7 TR, 8 BL, 9 BR
        public long[] Colors = new long[Slots];
        /// <summary>Extension "N: material/colour": the colour is looked up only on that bitmap.</summary>
        public string[] ColorMat = new string[Slots];
        public int[] Material = Enumerable.Repeat(-1, Slots).ToArray();
        public int[] Tile = new int[Slots];
        /// <summary>[L, T, R, B]; initial value from the original state constructor: L=T=B=0, R=-1.</summary>
        public long[][] Rects = Enumerable.Range(0, Slots).Select(_ => new long[] { 0, 0, -1, 0 }).ToArray();
        public Properties Props = new Properties();
        public long FaceColor = 0xFFFFFFFFL;
        public bool FaceColorSet;
        public string Particle = "";
        // Two Worlds II (keyword names invented, no original compiler exists)
        public long FaceColor2 = 0xFFFFFFFFL;
        public string Texture = "";
        public long TextureMargin;
    }

    public static class Flags
    {
        public const int ConstX = 0x1, ConstY = 0x2, StretchX = 0x4, StretchY = 0x8;
        public const int NoLeft = 0x100, NoRight = 0x200, NoTop = 0x400, NoBottom = 0x800, NoBackground = 0x1000;
        public const int ScaleBorder = 0x100, ScaleBackground = 0x200;     // KnightShift and later (stored)
        /// <summary>Two Worlds II: the game plays the standard interface sounds on control events.</summary>
        public const int Tw2InterfaceSounds = 0x800;
        /// <summary>Two Worlds II: slots of the item use the "_DISS" variant of the material texture.</summary>
        public const int Tw2DissTexture = 0x1000;
    }

    public static class RecordKey
    {
        public const int Color = 0, Selection = 1, Uses = 2, Moving = 3, Light = 4;
        public static readonly Dictionary<int, string> Names = new Dictionary<int, string>
            { { 0, "Color" }, { 1, "Selection" }, { 2, "Uses" }, { 3, "Moving" }, { 4, "Light" } };
    }

    public class CItem
    {
        public long Number;
        public long Flags, ExtFlags;
        public float SizeScale = 1.0f;
        public float ShowStartTime = -1.0f;
        public long[] Margins = new long[4];
        public Properties Props = new Properties();
        public SortedDictionary<long, CState> States = new SortedDictionary<long, CState>();
        public CItem(long n) { Number = n; }
    }

    public class Material { public string Name; public long Tile = 64; public long Base; }

    public class CDialog
    {
        public long Id, Item = -1;
        public long[] Force = new long[4];
        /// <summary>(id, item, x offset, y offset), ascending by id.</summary>
        public List<long[]> Controls = new List<long[]>();
    }

    public class Project
    {
        public string Fmt = InterfaceTools.Fmt.E2150;
        public List<Material> Materials = new List<Material>();
        public List<Font> Fonts = new List<Font>();
        /// <summary>nr -> (unique index, colour); extended with (0, 0) like the original.</summary>
        public List<long[]> FontMap = new List<long[]>();
        public HashSet<long> FontDefined = new HashSet<long>();
        public List<string> Sounds = new List<string>();
        public SortedDictionary<long, CItem> Items = new SortedDictionary<long, CItem>();
        public long BaseW = 640, BaseH = 480;
        public float ScaleX = 1.0f, ScaleY = 1.0f;
        public long GlobalFlags;
        public string ParticlesRoot = "", ParticlesPrefix = "";
        public List<KeyValuePair<string, List<string>>> CustomData = new List<KeyValuePair<string, List<string>>>();
        public List<CDialog> Dialogs = new List<CDialog>();
        /// <summary>"// Bitmap name crc32 0x..." comments written by the decompiler.</summary>
        public Dictionary<string, uint> BitmapCrc = new Dictionary<string, uint>();
        public Dictionary<string, long> Resources;
        /// <summary>Extension "RepeatItem N".</summary>
        public Dictionary<long, int> ItemRepeats = new Dictionary<long, int>();
        public List<long> ItemRepeatOrder = new List<long>();

        public long SoundIndex(string name)
        {
            for (int i = 0; i < Sounds.Count; i++)
                if (Text.AsciiLower(Sounds[i]) == Text.AsciiLower(name)) return i;
            Sounds.Add(name);
            return Sounds.Count - 1;
        }
    }

    public static class Resources
    {
        public static readonly (string, long)[] Builtin =
        {
            ("IDOK", 1), ("IDCANCEL", 2), ("IDABORT", 3), ("IDRETRY", 4), ("IDIGNORE", 5),
            ("IDYES", 6), ("IDNO", 7), ("IDCLOSE", 8), ("IDHELP", 9)
        };

        /// <summary>Built-in IDOK..IDHELP, then lines matching sscanf("#define %s %d"); names case-insensitive.</summary>
        public static Dictionary<string, long> Load(string path)
        {
            var res = new Dictionary<string, long>();
            foreach (var (n, v) in Builtin) res[Text.AsciiLower(n)] = v;
            if (path == null) return res;
            foreach (var raw in Text.ReadLatin1(path).Split('\n'))
            {
                if (!raw.StartsWith("#define", StringComparison.Ordinal)) continue;
                string name = Text.ScanToken(raw, 7, out int p);
                if (name == null) continue;
                long? v = Text.ScanInt(raw, p, out _);
                if (v != null) res[Text.AsciiLower(name)] = v.Value;
            }
            return res;
        }
    }

    public class Parser
    {
        static readonly string[] ItemKeywords =
        {
            "State",
            "ConstX", "ConstY", "StretchX", "StretchY",
            "LeftX", "CenterX", "RandomX", "RightX",
            "LeftY", "CenterY", "RandomY", "RightY",
            "NoLeftBorder", "NoRightBorder", "NoTopBorder", "NoBottomBorder", "NoBackground",
            "NoBorderX", "NoBorderY",
            "ScaleBorder", "ScaleBackground", "SizeScale",
            "LeftMargin", "TopMargin", "RightMargin", "BottomMargin", "ShowStartTime",
        };
        static readonly Dictionary<string, int> ItemKeywordCount = new Dictionary<string, int> { { "e2150", 20 }, { "ks", 23 }, { "e2160", 28 } };
        static readonly string[] ItemKeywordsTw = ItemKeywords.Where(k => k != "SizeScale").ToArray();

        static string[] KeywordsFor(string fmt) =>
            InterfaceTools.Fmt.IsTw(fmt) ? ItemKeywordsTw : ItemKeywords.Take(ItemKeywordCount[fmt]).ToArray();

        // Note: the original computes the bit as 1 << (index-1) and for indices > 13 subtracts 4,
        // so NoLeftBorder (13) sets 0x1000 like NoBackground. Reproduced faithfully.
        static readonly Dictionary<int, long> BoolFlags = new Dictionary<int, long>
        {
            { 1, Flags.ConstX }, { 2, Flags.ConstY }, { 3, Flags.StretchX }, { 4, Flags.StretchY },
            { 13, 0x1000 }, { 14, Flags.NoRight }, { 15, Flags.NoTop }, { 16, Flags.NoBottom }, { 17, Flags.NoBackground },
            { 18, Flags.NoLeft | Flags.NoRight }, { 19, Flags.NoTop | Flags.NoBottom }
        };

        static readonly Dictionary<string, int> ListProperties = new Dictionary<string, int>
            { { "Selection", RecordKey.Selection }, { "Uses", RecordKey.Uses }, { "Moving", RecordKey.Moving },
              { "Light", RecordKey.Light }, { "Color", RecordKey.Color } };
        static readonly string[] PropertyKeywords = { "Light", "Moving", "Uses", "Selection", "Color", "Sound", "Font", "Effect" };

        readonly Project p;
        readonly string fmt;
        readonly bool verbose;
        public readonly List<KeyValuePair<int, string>> Raw = new List<KeyValuePair<int, string>>();
        int pos;
        readonly HashSet<string> missingParticles = new HashSet<string>();

        public Parser(Project project, bool verbose)
        {
            p = project; fmt = project.Fmt; this.verbose = verbose;
        }

        public void Load(string path)
        {
            string data = Text.ReadLatin1(path);
            int no = 0;
            foreach (var line in data.Split('\n'))
            {
                no++;
                if (line.Length > 0x103)
                    Log.Line($"Warning: line {no} is longer than 259 characters (the original would split it)");
                Raw.Add(new KeyValuePair<int, string>(no, line));
            }
        }

        bool NextLine(string context, bool trimOnly, out int no, out string text)
        {
            while (pos < Raw.Count)
            {
                var r = Raw[pos++];
                string t = trimOnly ? Text.CTrim(r.Value) : Text.NormalizeLine(r.Value);
                if (t.Length > 0 && !t.StartsWith("//", StringComparison.Ordinal)) { no = r.Key; text = t; return true; }
            }
            if (context == null) { no = 0; text = null; return false; }
            throw new DefinitionError($"unexpected end of file ({context})");
        }

        (int, string) Next(string context, bool trimOnly = false)
        {
            NextLine(context, trimOnly, out int no, out string t);
            return (no, t);
        }

        void ExpectOpen(string context)
        {
            var (no, line) = Next(context);
            if (line != "{") throw new DefinitionError($"line {no}: expected '{{' ({context}), got '{line}'");
        }

        long? Resource(string name)
        {
            if (p.Resources == null) return null;
            return p.Resources.TryGetValue(Text.AsciiLower(name), out long v) ? v : (long?)null;
        }

        public void Parse()
        {
            while (true)
            {
                if (!NextLine(null, false, out int no, out string line)) return;
                string low = Text.AsciiLower(line);
                if (low == "materials") { ExpectOpen("Materials"); ParseMaterials(); }
                else if (low == "fonts") { ExpectOpen("Fonts"); ParseFonts(); }
                else if (ParseDirective(no, line)) { }
                else if (Text.StartsKw(line, "Item") && line.Length > 4 && line[4] == ':')
                {
                    long num = Text.StrToL(line, 5, out int end);
                    if (end == 5 || (end < line.Length && line[end] != ' '))
                        throw new DefinitionError($"line {no}: invalid item number: '{line}'");
                    ExpectOpen($"Item:{num}");
                    var item = ParseItem(num);
                    if (p.Items.ContainsKey(num))
                        Log.Line($"Warning: Item:{num} defined again - ignored (as in the original)");
                    else p.Items[num] = item;
                }
                else throw new DefinitionError($"line {no}: unknown section '{line}'");
            }
        }

        bool ParseDirective(int no, string line)
        {
            bool e2160 = InterfaceTools.Fmt.IsNew(fmt);
            if (Text.StartsKw(line, "Scale") && InterfaceTools.Fmt.IsTw(fmt))
                throw new DefinitionError($"line {no}: the 'Scale' directive does not exist in the TwoWorlds format");
            if (Text.StartsKw(line, "Scale"))
            {
                // sscanf(" %d, %d, %f, %f")
                string s = line.Substring(5);
                long? w = Text.ScanInt(s, 0, out int pp);
                var vals = new List<double>();
                if (w != null)
                {
                    vals.Add(w.Value);
                    foreach (char kind in "dff")
                    {
                        while (pp < s.Length && (s[pp] == ' ' || s[pp] == '\t')) pp++;
                        if (pp >= s.Length || s[pp] != ',') break;
                        pp++;
                        double? v; int p2;
                        if (kind == 'd') { long? iv = Text.ScanInt(s, pp, out p2); v = iv; }
                        else { double dv = Text.StrToD(s, pp, out p2); v = p2 == pp ? (double?)null : dv; }
                        if (v == null) break;
                        vals.Add(v.Value);
                        pp = p2;
                    }
                }
                if (vals.Count != 4) throw new DefinitionError($"line {no}: expected 'Scale width, height, scaleX, scaleY'");
                p.BaseW = (((long)vals[0] + 0x8000) & 0xFFFF) - 0x8000;
                p.BaseH = (((long)vals[1] + 0x8000) & 0xFFFF) - 0x8000;
                p.ScaleX = (float)vals[2]; p.ScaleY = (float)vals[3];
                return true;
            }
            foreach (var (kw, bit) in new[] { ("DefaultScaleBorder", (long)Flags.ScaleBorder), ("DefaultScaleBackground", (long)Flags.ScaleBackground) })
            {
                if (Text.StartsKw(line, kw))
                {
                    long? v = Text.ScanInt(line, kw.Length, out _);
                    if (v == null) throw new DefinitionError($"line {no}: invalid value in '{line}'");
                    p.GlobalFlags = v != 0 ? (p.GlobalFlags | bit) : (p.GlobalFlags & ~bit);
                    return true;
                }
            }
            if (Text.StartsKw(line, "CustomData"))
            {
                Require(e2160, no, "CustomData");
                string name = Text.ScanToken(line, 10, out int pp);
                if (name == null) throw new DefinitionError($"line {no}: missing name in '{line}'");
                string value = Text.ScanToken(line, pp, out _);
                if (value != null)
                    p.CustomData.Add(new KeyValuePair<string, List<string>>(Text.AsciiLower(name), new List<string> { Text.AsciiLower(Text.CTrim(value)) }));
                else
                {
                    p.CustomData.Add(new KeyValuePair<string, List<string>>(Text.AsciiLower(name), new List<string>()));
                    var (no2, l2) = Next("CustomData");
                    if (l2 != "{") throw new DefinitionError($"line {no2}: expected '{{' (CustomData)");
                    ParseCustomBlock();
                }
                return true;
            }
            if (Text.StartsKw(line, "DialogTemplates"))
            {
                Require(e2160, no, "DialogTemplates");
                string name = Text.ScanToken(line, 15, out _);
                if (name == null) throw new DefinitionError($"line {no}: missing name in '{line}'");
                long? did = Resource(name);
                if (did == null) throw new DefinitionError($"line {no}: dialog identifier '{name}' not found in resource.h");
                if (p.Dialogs.Any(d => d.Id == did)) throw new DefinitionError($"line {no}: DialogTemplates '{name}' already defined");
                p.Dialogs.Add(new CDialog { Id = did.Value });
                var (no2, l2) = Next("DialogTemplates");
                if (l2 != "{") throw new DefinitionError($"line {no2}: expected '{{' (DialogTemplates)");
                ParseDialogBlock();
                return true;
            }
            foreach (var kw in new[] { "ParticlesRoot", "ParticlesPrefixPath" })
            {
                if (Text.StartsKw(line, kw))
                {
                    Require(e2160, no, kw);
                    string v = Text.ScanToken(line, kw.Length, out _);
                    if (v == null && kw == "ParticlesRoot") throw new DefinitionError($"line {no}: missing path in '{line}'");
                    if (kw == "ParticlesRoot") p.ParticlesRoot = v; else p.ParticlesPrefix = v ?? "";
                    return true;
                }
            }
            // --- extensions
            if (Text.StartsKw(line, "RepeatItem "))
            {
                long v = Text.StrToL(line, 11, out int end);
                if (end == 11 || end != line.Length || v < 0) throw new DefinitionError($"line {no}: invalid item number in '{line}'");
                p.ItemRepeats[v] = (p.ItemRepeats.TryGetValue(v, out int c) ? c : 0) + 1;
                if (!p.ItemRepeatOrder.Contains(v)) p.ItemRepeatOrder.Add(v);
                return true;
            }
            int sp = line.IndexOf(' ');
            string k = Text.AsciiLower(sp >= 0 ? line.Substring(0, sp) : line);
            string arg = sp >= 0 ? line.Substring(sp + 1) : "";
            if (k != "basesize" && k != "basescale" && k != "globalflags") return false;
            string[] parts = arg.Split(' ');
            try
            {
                if (k == "basesize" && parts.Length == 2)
                {
                    p.BaseW = PyInt(parts[0]); p.BaseH = PyInt(parts[1]);
                    if (!(p.BaseW > 0 && p.BaseW < 0x8000 && p.BaseH > 0 && p.BaseH < 0x8000)) throw new FormatException();
                    return true;
                }
                if (k == "basescale" && parts.Length == 2)
                {
                    p.ScaleX = (float)Text.ParseDouble(parts[0]); p.ScaleY = (float)Text.ParseDouble(parts[1]);
                    return true;
                }
                if (k == "globalflags" && parts.Length == 1)
                {
                    long v = Text.StrToUL(parts[0], out int end);
                    if (end == parts[0].Length && end > 0) { p.GlobalFlags = v; return true; }
                }
            }
            catch (FormatException) { }
            throw new DefinitionError($"line {no}: invalid directive '{line}'");
        }

        /// <summary>Python int(s, 0).</summary>
        static long PyInt(string s)
        {
            string t = s.Trim().Replace("_", "");
            bool neg = false;
            if (t.StartsWith("+") || t.StartsWith("-")) { neg = t[0] == '-'; t = t.Substring(1); }
            long v;
            string tl = t.ToLowerInvariant();
            if (tl.StartsWith("0x")) v = Convert.ToInt64(t.Substring(2), 16);
            else if (tl.StartsWith("0o")) v = Convert.ToInt64(t.Substring(2), 8);
            else if (tl.StartsWith("0b")) v = Convert.ToInt64(t.Substring(2), 2);
            else
            {
                if (t.Length == 0 || !t.All(char.IsDigit) || (t.Length > 1 && t[0] == '0' && t.Trim('0').Length > 0)) throw new FormatException();
                v = long.Parse(t);
            }
            return neg ? -v : v;
        }

        void Require(bool ok, int no, string what)
        {
            if (!ok) throw new DefinitionError($"line {no}: '{what}' exists only in the Earth 2160 and TwoWorlds formats");
        }

        void ParseCustomBlock()
        {
            var lst = p.CustomData[p.CustomData.Count - 1].Value;
            int depth = 0;
            while (true)
            {
                var (no, line) = Next("CustomData", true);
                if (line[0] == '}') { if (depth == 0) return; depth--; }
                else if (line[0] == '{') depth++;
                line = Text.AsciiLower(line);
                if (lst.Count > 0 && lst[lst.Count - 1].EndsWith("\\", StringComparison.Ordinal))
                    lst[lst.Count - 1] = lst[lst.Count - 1].Substring(0, lst[lst.Count - 1].Length - 1) + " " + line;
                else lst.Add(line);
            }
        }

        static readonly Dictionary<string, int> ForceNames = new Dictionary<string, int>
        {
            { "forcemessageboxbackground", 0 }, { "forcenomessageboxbackground", 1 },
            { "forcefullscreentransparentbackground", 2 }, { "forcenofullscreentransparentbackground", 3 }
        };

        void ParseDialogBlock()
        {
            var dlg = p.Dialogs[p.Dialogs.Count - 1];
            int depth = 0;
            while (true)
            {
                var (no, line) = Next("DialogTemplates", true);
                if (line[0] == '}') { if (depth == 0) return; depth--; }
                else if (line[0] == '{') depth++;
                if (ForceNames.TryGetValue(Text.AsciiLower(line), out int fi)) { dlg.Force[fi] = 1; continue; }
                // sscanf("%s = %d %d %d")
                string name = Text.ScanToken(line, 0, out int pp);
                var vals = new List<long>();
                while (pp < line.Length && (line[pp] == ' ' || line[pp] == '\t')) pp++;
                if (pp < line.Length && line[pp] == '=')
                {
                    pp++;
                    for (int k = 0; k < 3; k++)
                    {
                        long? v = Text.ScanInt(line, pp, out int p2);
                        if (v == null) break;
                        vals.Add(v.Value);
                        pp = p2;
                    }
                }
                if (name == null || vals.Count == 0) throw new DefinitionError($"line {no}: Error in DialogTemplate: '{line}'");
                while (vals.Count < 3) vals.Add(0);
                long a = vals[0], b = vals[1], c = vals[2];
                int lb = name.IndexOf('[');
                int rb = lb >= 0 ? name.IndexOf(']', lb) : -1;
                long[] rng = null;
                if (lb >= 0 && rb >= 0)
                {
                    long? lo = Text.ScanInt(name, lb + 1, out int q);
                    if (lo != null && q < name.Length && name[q] == '-')
                    {
                        long? hi = Text.ScanInt(name, q + 1, out _);
                        if (hi != null) rng = new[] { lo.Value, hi.Value };
                    }
                }
                if (rng != null)
                {
                    string prefix = name.Substring(0, lb), suffix = name.Substring(rb + 1);
                    for (long i = rng[0]; i <= rng[1]; i++)
                    {
                        string nm = prefix + i + suffix;
                        long? rid = Resource(nm);
                        if (rid == null) throw new DefinitionError($"line {no}: resource identifier '{nm}' not found in resource.h");
                        AddControl(no, dlg, nm, new[] { rid.Value, a, b, c });
                    }
                }
                else
                {
                    long? rid = Resource(name);
                    if (rid == null) throw new DefinitionError($"line {no}: resource identifier '{name}' not found in resource.h");
                    if (rid == dlg.Id)
                    {
                        if (dlg.Item >= 0) throw new DefinitionError($"line {no}: dialog identifier '{name}' already defined in this dialog");
                        dlg.Item = a;
                    }
                    else AddControl(no, dlg, name, new[] { rid.Value, a, b, c });
                }
            }
        }

        static void AddControl(int no, CDialog dlg, string name, long[] rec)
        {
            if (dlg.Controls.Any(r => r[0] == rec[0]))
                throw new DefinitionError($"line {no}: resource identifier '{name}' already defined in this dialog");
            int k = 0;
            while (k < dlg.Controls.Count && dlg.Controls[k][0] < rec[0]) k++;
            dlg.Controls.Insert(k, rec);
        }

        void ParseMaterials()
        {
            while (true)
            {
                var (no, line) = Next("Materials");
                if (line.StartsWith("}", StringComparison.Ordinal)) return;
                if (InterfaceTools.Fmt.IsNew(fmt))
                {
                    // "path[:base]" - base of the colour numbering (colour = base*256 + palette index)
                    int ci = line.IndexOf(':');
                    if (ci >= 0)
                    {
                        long? bs = Text.ScanInt(line.Substring(ci + 1), 0, out _);
                        p.Materials.Add(new Material { Name = line.Substring(0, ci), Base = bs ?? 0 });
                    }
                    else p.Materials.Add(new Material { Name = line, Base = 0 });
                    continue;
                }
                int c2 = line.IndexOf(':');
                if (c2 >= 0)
                {
                    string rest = line.Substring(c2 + 1);
                    long tile = Text.StrToL(rest, out int end);
                    if (end == 0 || end != rest.Length) throw new DefinitionError($"line {no}: invalid tile size in '{line}'");
                    p.Materials.Add(new Material { Name = line.Substring(0, c2), Tile = tile });
                }
                else p.Materials.Add(new Material { Name = line, Tile = 64 });
            }
        }

        void ParseFonts()
        {
            while (true)
            {
                var (no, line) = Next("Fonts");
                if (line.StartsWith("}", StringComparison.Ordinal)) return;
                ParseFontLine(no, line);
            }
        }

        /// <summary>Equivalent of FUN_00404290: whether the text colour stays in the font or moves to
        /// the state (Color record). Returns (font colour, shadow colour, state colour).</summary>
        public static (long, long, long) SplitFontColor(long color, long scolor, long sx, long sy)
        {
            color &= 0xFFFFFFFFL; scolor &= 0xFFFFFFFFL;
            if (((sx & 0xFF) != 0 || (sy & 0xFF) != 0) && color != scolor)
            {
                if ((color & 0xFF000000L) == 0xFF000000L && scolor == 0xFF000000L) return (0xFFFFFFFFL, scolor, color);
                return (color, scolor, 0xFFFFFFFFL);
            }
            return (0xFFFFFFFFL, 0xFFFFFFFFL, color);
        }

        void ParseFontLine(int no, string line)
        {
            long nr = Text.StrToL(line, out int end);
            if (end == 0 || end >= line.Length || line[end] != ':') throw new DefinitionError($"line {no}: invalid font definition '{line}'");
            string rest = line.Substring(end + 1);
            var vals = new Dictionary<string, long>
                { { "size", 7 }, { "weight", 400 }, { "italic", 0 }, { "color", 0xFFFFFFFFL }, { "sx", 0 }, { "sy", 0 }, { "scolor", 0xFF0F0F0FL }, { "extra", 0 } };
            string name;
            int comma = rest.IndexOf(',');
            if (comma >= 0)
            {
                name = rest.Substring(0, comma);
                string tail = rest.Substring(comma + 1);
                var order = new List<(string, bool)> { ("size", false), ("weight", false), ("italic", false), ("color", true), ("sx", false), ("sy", false), ("scolor", true) };
                if (fmt == "tw2") order.Add(("extra", false));
                int pp = 0;
                for (int i = 0; i < order.Count; i++)
                {
                    var (k, uns) = order[i];
                    long v = uns ? Text.StrToUL(tail, pp, out int e) : Text.StrToL(tail, pp, out e);
                    if (e == pp) throw new DefinitionError($"line {no}: invalid value '{k}' in font definition");
                    vals[k] = v;
                    if (e == tail.Length) break;
                    if (tail[e] != ',' || i == order.Count - 1) throw new DefinitionError($"line {no}: extra characters in font definition");
                    pp = e + 1;
                }
            }
            else name = rest;
            var (fc, sc, color) = SplitFontColor(vals["color"], vals["scolor"], vals["sx"], vals["sy"]);
            var font = new Font
            {
                Name = name, Size = vals["size"], Weight = vals["weight"], Italic = vals["italic"], Color = fc,
                ShadowX = vals["sx"], ShadowY = vals["sy"], ShadowColor = sc, Extra = vals["extra"]
            };
            int idx = -1;
            string key = font.Key();
            for (int i = p.Fonts.Count - 1; i >= 0; i--) if (p.Fonts[i].Key() == key) { idx = i; break; }
            if (idx < 0) { p.Fonts.Add(font); idx = p.Fonts.Count - 1; }
            if (nr < 0) throw new DefinitionError($"line {no}: negative font number");
            while (p.FontMap.Count <= nr) p.FontMap.Add(new long[] { 0, 0 });
            p.FontMap[(int)nr] = new long[] { idx, color };
            p.FontDefined.Add(nr);
            if (verbose) Log.Line($"Font {nr} became 0x{idx:x8} 0x{color:x8}");
        }

        bool ParseProperty(int no, string line, Properties props)
        {
            string kw = PropertyKeywords.FirstOrDefault(k => Text.StartsKw(line, k));
            if (kw == null) return false;
            string sep = kw.Length < line.Length ? line[kw.Length].ToString() : "";
            string arg = line.Length > kw.Length + 1 ? line.Substring(kw.Length + 1) : "";
            if (kw == "Effect")
            {
                if (sep != ":") throw new DefinitionError($"line {no}: expected 'Effect:' in '{line}'");
                long key = Text.StrToL(arg, out int end);
                if (end == 0 || (end < arg.Length && arg[end] != ' ')) throw new DefinitionError($"line {no}: invalid key in '{line}'");
                if (!(key >= 0 && key < 0xFFFF)) throw new DefinitionError($"line {no}: the Effect key must be in the range 0..65534");
                var values = end < arg.Length ? ParseValues(no, line, arg.Substring(end + 1)) : new List<long>();
                props.SetRecord((int)key, values);
                return true;
            }
            if (sep != " ") throw new DefinitionError($"line {no}: expected a space after '{kw}': '{line}'");
            if (kw == "Font")
            {
                long nr = Text.StrToL(arg, out int end);
                if (end == 0 || end != arg.Length) throw new DefinitionError($"line {no}: invalid font number '{line}'");
                if (!(nr >= 0 && nr < p.FontMap.Count))
                    throw new DefinitionError($"line {no}: font {nr} is not defined (out of range 0..{p.FontMap.Count - 1})");
                if (!p.FontDefined.Contains(nr))
                    Log.Line($"Warning: line {no}: font {nr} is not defined - using font 0 with colour 0 (as the original does)");
                long idx = p.FontMap[(int)nr][0], color = p.FontMap[(int)nr][1];
                if (p.Fonts.Count > 0) p.Fonts[(int)idx].Used = true;
                props.Allocated = true;
                props.Font = idx;
                if (color != 0xFFFFFFFFL) props.SetRecord(RecordKey.Color, new List<long> { color });
                return true;
            }
            if (kw == "Sound")
            {
                if (fmt == "tw2") throw new DefinitionError($"line {no}: the Two Worlds II format has no sound list");
                if (arg.Length == 0) throw new DefinitionError($"line {no}: missing sound name");
                props.Allocated = true;
                props.Sound = p.SoundIndex(arg);
                return true;
            }
            props.SetRecord(ListProperties[kw], ParseValues(no, line, arg));
            return true;
        }

        /// <summary>Comma-separated numbers (strtoul, base 0); values after the 64th are ignored.</summary>
        static List<long> ParseValues(int no, string line, string arg)
        {
            var values = new List<long>();
            int pp = 0;
            while (true)
            {
                if (values.Count >= 64) { Log.Line($"Warning: line {no}: more than 64 values - the rest is skipped (as in the original)"); break; }
                long v = Text.StrToUL(arg, pp, out int e);
                if (e == pp) throw new DefinitionError($"line {no}: invalid value in '{line}'");
                values.Add(v);
                if (e == arg.Length) break;
                if (arg[e] != ',') throw new DefinitionError($"line {no}: extra characters in '{line}'");
                pp = e + 1;
            }
            return values;
        }

        CItem ParseItem(long num)
        {
            var item = new CItem(num);
            var kws = KeywordsFor(fmt);
            while (true)
            {
                var (no, line) = Next($"Item:{num}");
                if (line.StartsWith("}", StringComparison.Ordinal))
                {
                    if (line != "}" || item.States.Count == 0) throw new DefinitionError($"line {no}: Item:{num} has no states or an invalid '}}'");
                    foreach (var sn in item.States.Keys.Reverse()) item.States[sn].Props.MergeFrom(item.Props);
                    return item;
                }
                if (fmt == "ks" && ParseOldKsAlias(no, line, item)) continue;
                if (fmt == "tw2")
                {
                    var bk = new[] { ("InterfaceSounds ", (long)Flags.Tw2InterfaceSounds), ("DissTexture ", (long)Flags.Tw2DissTexture), ("SecondTexture ", (long)Flags.Tw2DissTexture) }
                        .FirstOrDefault(t => Text.StartsKw(line, t.Item1));
                    if (bk.Item1 != null)
                    {
                        long v = Text.StrToL(line, bk.Item1.Length, out int end);
                        if (end == bk.Item1.Length || end != line.Length) throw new DefinitionError($"line {no}: invalid value in '{line}'");
                        item.ExtFlags = v != 0 ? (item.ExtFlags | bk.Item2) : (item.ExtFlags & ~bk.Item2);
                        continue;
                    }
                }
                if (fmt != "e2150" && Text.StartsKw(line, "ExtFlags "))
                {
                    long v = Text.StrToUL(line, 9, out int end);
                    if (end == 9 || end != line.Length || (v & 0xFF) != 0) throw new DefinitionError($"line {no}: ExtFlags must be a number without bits 0..7");
                    item.ExtFlags |= v;
                    continue;
                }
                int idx = -1;
                for (int i = kws.Length - 1; i >= 0; i--) if (Text.StartsKw(line, kws[i])) { idx = i; break; }
                if (idx >= 0) idx = Array.IndexOf(ItemKeywords, kws[idx]);
                if (idx < 0)
                {
                    if (ParseProperty(no, line, item.Props)) continue;
                    throw new DefinitionError($"line {no}: unknown command '{line}'");
                }
                int kl = ItemKeywords[idx].Length;
                string nxt = kl < line.Length ? line[kl].ToString() : "";
                if (!(nxt == " " || idx == 0 || (idx >= 5 && idx <= 12 && nxt == ""))) throw new DefinitionError($"line {no}: unknown command '{line}'");
                if (idx == 0)
                {
                    if (nxt != ":") throw new DefinitionError($"line {no}: expected 'State:N'");
                    long sn = Text.StrToL(line, kl + 1, out int end);
                    if (end == kl + 1 || (end < line.Length && line[end] != ' ')) throw new DefinitionError($"line {no}: invalid state number");
                    if (sn < 0 || item.States.ContainsKey(sn)) throw new DefinitionError($"line {no}: state {sn} defined again");
                    item.States[sn] = new CState();
                    ExpectOpen($"State:{sn}");
                    ParseState(item, sn);
                }
                else if (idx >= 5 && idx <= 12)
                {
                    if (idx < 9) item.Flags = (item.Flags & ~0x30L) | ((long)(idx - 5) << 4);
                    else item.Flags = (item.Flags & ~0xC0L) | ((long)(idx - 9) << 6);
                }
                else if (BoolFlags.TryGetValue(idx, out long bit))
                {
                    long v = Text.StrToL(line, kl + 1, out int end);
                    if (idx < 18 && (end == kl + 1 || end != line.Length)) throw new DefinitionError($"line {no}: invalid value in '{line}'");
                    item.Flags = v != 0 ? (item.Flags | bit) : (item.Flags & ~bit);
                }
                else if (idx == 20 || idx == 21)
                {
                    long v = Text.StrToL(line, kl + 1, out int end);
                    if (end == kl + 1 || end != line.Length) throw new DefinitionError($"line {no}: invalid value in '{line}'");
                    long b2 = idx == 20 ? Flags.ScaleBorder : Flags.ScaleBackground;
                    item.ExtFlags = v != 0 ? (item.ExtFlags | b2) : (item.ExtFlags & ~b2);
                }
                else if (idx == 22 || idx == 27)
                {
                    double v = Text.StrToD(line, kl + 1, out int end);
                    if (end == kl + 1 || end != line.Length) throw new DefinitionError($"line {no}: invalid value in '{line}'");
                    if (idx == 22) item.SizeScale = (float)v; else item.ShowStartTime = (float)v;
                }
                else
                {
                    long v = Text.StrToL(line, kl + 1, out int end);
                    if (end == kl + 1 || end != line.Length) throw new DefinitionError($"line {no}: invalid value in '{line}'");
                    item.Margins[idx - 23] = v;
                }
            }
        }

        bool ParseOldKsAlias(int no, string line, CItem item)
        {
            int sp = line.IndexOf(' ');
            string kw = Text.AsciiLower(sp >= 0 ? line.Substring(0, sp) : line);
            string arg = sp >= 0 ? line.Substring(sp + 1) : "";
            if (kw == "scale" && arg.Length > 0)
            {
                double v = Text.StrToD(arg, out int end);
                if (end == 0 || end != arg.Length) throw new DefinitionError($"line {no}: invalid value in '{line}'");
                item.SizeScale = (float)v;
                return true;
            }
            if (kw == "scalecenter" && arg.Length > 0)
            {
                long v = Text.StrToL(arg, out int end);
                if (end == 0 || end != arg.Length) throw new DefinitionError($"line {no}: invalid value in '{line}'");
                item.ExtFlags = v != 0 ? (item.ExtFlags | Flags.ScaleBackground) : (item.ExtFlags & ~(long)Flags.ScaleBackground);
                return true;
            }
            return false;
        }

        void ParseState(CItem item, long sn)
        {
            if (InterfaceTools.Fmt.IsNew(fmt)) { ParseState2160(item, sn); return; }
            var st = item.States[sn];
            while (true)
            {
                var (no, line) = Next($"Item:{item.Number} State:{sn}");
                if (line == "}") return;
                if (ParseSlotColor(no, line, st)) continue;
                if (ParseProperty(no, line, st.Props)) continue;
                if (Text.StartsKw(line, "Identity") && line.Length > 8 && line[8] == ' ') { Identity(no, item, st, line.Substring(9)); continue; }
                throw new DefinitionError($"line {no}: unknown command '{line}'");
            }
        }

        static bool ParseSlotColor(int no, string line, CState st)
        {
            if (!(line.Length >= 2 && line[0] >= '1' && line[0] <= '9' && line[1] == ':')) return false;
            int slot = line[0] - '1';
            if (line.Contains("/"))
            {
                string body = line.Substring(2);
                int k = body.LastIndexOf('/');
                string mat = body.Substring(0, k), col = body.Substring(k + 1);
                long v = Text.StrToL(col, out int end);
                if (mat.Length == 0 || end == 0 || end != col.Length || !(v > 0 && v < 256))
                    throw new DefinitionError($"line {no}: invalid colour '{line}' (expected material/1..255)");
                st.Colors[slot] = v;
                st.ColorMat[slot] = Text.AsciiLower(mat);
                return true;
            }
            long c = Text.StrToL(line, 2, out int e2);
            if (e2 == 2 || e2 != line.Length) throw new DefinitionError($"line {no}: invalid colour number '{line}'");
            st.Colors[slot] = c;
            st.ColorMat[slot] = null;
            return true;
        }

        void Identity(int no, CItem item, CState st, string arg)
        {
            long srcN = Text.StrToL(arg, out int end);
            if (end == 0 || end != arg.Length) throw new DefinitionError($"line {no}: invalid state number in 'Identity {arg}'");
            if (!item.States.TryGetValue(srcN, out CState src) || src == st)
                throw new DefinitionError($"line {no}: Identity refers to undefined state {srcN}");
            st.Colors = (long[])src.Colors.Clone();
            st.ColorMat = (string[])src.ColorMat.Clone();
            st.Props.MergeFrom(src.Props);
            if (InterfaceTools.Fmt.IsNew(fmt))
            {
                st.Particle = src.Particle;
                if (!st.FaceColorSet) st.FaceColor = src.FaceColor;
                if (fmt == "tw2") { st.FaceColor2 = src.FaceColor2; st.Texture = src.Texture; st.TextureMargin = src.TextureMargin; }
            }
        }

        static readonly Dictionary<string, string> Tw2StateWords = new Dictionary<string, string>
        {
            { "textcolor", "color" }, { "facecolor2", "color" },
            { "texttexture", "texture" }, { "textbackground", "texture" }, { "texture", "texture" },
            { "texttextureparam", "margin" }, { "textbackgroundmargin", "margin" }, { "texturemargin", "margin" }
        };

        static bool ParseTw2StateLine(int no, string line, CState st)
        {
            int sp = line.IndexOf(' ');
            string kw = sp >= 0 ? line.Substring(0, sp) : line;
            string arg = sp >= 0 ? line.Substring(sp + 1) : "";
            if (!Tw2StateWords.TryGetValue(Text.AsciiLower(kw), out string what)) return false;
            if (what == "texture")
            {
                if (arg.Length == 0) throw new DefinitionError($"line {no}: missing path in '{line}'");
                st.Texture = arg;
                return true;
            }
            long v = what == "color" ? Text.StrToUL(arg, out int end) : Text.StrToL(arg, out end);
            if (end == 0 || end != arg.Length) throw new DefinitionError($"line {no}: invalid value in '{line}'");
            if (what == "color") st.FaceColor2 = v; else st.TextureMargin = v;
            return true;
        }

        void ParseState2160(CItem item, long sn)
        {
            var st = item.States[sn];
            while (true)
            {
                var (no, line) = Next($"Item:{item.Number} State:{sn}");
                if (line == "}") return;
                if (line.Contains(".prt"))
                {
                    string path = Text.CTrim(p.ParticlesPrefix + line);
                    bool exists;
                    try
                    {
                        string full = Path.Combine(p.ParticlesRoot ?? "", path.Replace('\\', Path.DirectorySeparatorChar));
                        exists = File.Exists(full) || Directory.Exists(full);
                    }
                    catch (Exception) { exists = false; }
                    if (verbose && !exists && !missingParticles.Contains(path))
                    {
                        missingParticles.Add(path);
                        Log.Line($"WARNING: particle '{path}' not found in '{p.ParticlesRoot}'");
                    }
                    if (Text.StartsKw(line, "Identity") || Text.StartsKw(line, "FaceColor")) throw new DefinitionError($"line {no}: ambiguous line '{line}'");
                    st.Particle = path;
                    continue;
                }
                if (ParseSlotColor(no, line, st)) continue;
                if (ParseProperty(no, line, st.Props)) continue;
                if (fmt == "tw2" && ParseTw2StateLine(no, line, st)) continue;
                if (Text.StartsKw(line, "FaceColor") && line.Length > 9 && line[9] == ' ')
                {
                    long v = Text.StrToUL(line, 10, out int end);
                    if (end == 10 || end != line.Length) throw new DefinitionError($"line {no}: invalid value in '{line}'");
                    st.FaceColor = v; st.FaceColorSet = true;
                    continue;
                }
                if (Text.StartsKw(line, "Identity") && line.Length > 8 && line[8] == ' ') { Identity(no, item, st, line.Substring(9)); continue; }
                throw new DefinitionError($"line {no}: unknown command '{line}'");
            }
        }
    }

    // -----------------------------------------------------------------------------------------
    //  Bitmaps, rectangles, writing
    // -----------------------------------------------------------------------------------------
    public static class Compiler
    {
        public const long FileMagic = 0x10D0A1FF;   // stream header (Earth 2150, KnightShift)
        public const long FileId = 0x01004449;      // Earth 2150 ('ID' + version 1)
        public const long FileIdKs = 0x02004449;    // KnightShift ('ID' + version 2)
        public static readonly int[] E2160Sizes = { 32, 64, 128, 256, 512 };
        const long IntMax = 0x7FFFFFFF;

        /// <summary>Name after the last '\' (or '/'), cut before the last dot.</summary>
        public static string StripPath(string material)
        {
            int k = material.LastIndexOf('\\');
            if (k < 0) k = material.LastIndexOf('/');
            string name = material.Substring(k + 1);
            int d = name.LastIndexOf('.');
            if (d >= 0) name = name.Substring(0, d);
            return name;
        }

        /// <summary>Bitmap file name the way the original looks for it.</summary>
        public static string BitmapName(string material, string fmt) =>
            Fmt.IsNew(fmt) ? StripPath(material) + "@.bmp" : material + "@.bmp";

        static string JoinRel(string dir, string rel) =>
            Path.Combine(new[] { dir }.Concat(rel.Replace('/', '\\').Split('\\')).ToArray());

        public static string FindBitmap(string material, IList<string> baseDirs, string fmt)
        {
            string name = BitmapName(material, fmt);
            foreach (var d in baseDirs)
            {
                string pth = JoinRel(d, name);
                if (File.Exists(pth)) return pth;
            }
            throw new BitmapError($"bitmap '{name}' not found");
        }

        /// <summary>Returns rects[256] as [L,T,R,B] (R, B exclusive); a colour without pixels has
        /// [INT_MAX, INT_MAX, -1, -1].</summary>
        public static long[][] ReadBitmap(string path, out int w, out int h)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 54 || data[0] != 'B' || data[1] != 'M') throw new BitmapError($"{path}: not a BMP file");
            int offBits = BitConverter.ToInt32(data, 10);
            w = BitConverter.ToInt32(data, 18);
            int hh = BitConverter.ToInt32(data, 22);
            int planes = BitConverter.ToUInt16(data, 26), bpp = BitConverter.ToUInt16(data, 28);
            int comp = BitConverter.ToInt32(data, 30);
            if (planes != 1 || bpp != 8 || comp != 0) throw new BitmapError($"{path}: an uncompressed 8-bit palettized bitmap is required");
            bool topDown = hh < 0;
            h = Math.Abs(hh);
            int stride = (w + 3) & ~3;
            var rects = Enumerable.Range(0, 256).Select(_ => new long[] { IntMax, IntMax, -1, -1 }).ToArray();
            for (int row = 0; row < h; row++)
            {
                int y = topDown ? row : h - 1 - row;
                int o = offBits + row * stride;
                if (o + w > data.Length) throw new BitmapError($"{path}: the file is truncated");
                for (int x = 0; x < w; x++)
                {
                    var r = rects[data[o + x]];
                    if (x < r[0]) r[0] = x;
                    if (y < r[1]) r[1] = y;
                    if (x + 1 > r[2]) r[2] = x + 1;
                    if (y + 1 > r[3]) r[3] = y + 1;
                }
            }
            return rects;
        }

        static string Ranges(List<int> nums)
        {
            var o = new List<string>();
            int? start = null;
            for (int k = 0; k < nums.Count; k++)
            {
                int n = nums[k];
                if (start == null) start = n;
                if (k + 1 == nums.Count || nums[k + 1] != n + 1) { o.Add(n != start ? $"{start}-{n}" : $"{n}"); start = null; }
            }
            return string.Join(", ", o);
        }

        static uint Crc(string path) => Text.Crc32(File.ReadAllBytes(path));

        static string NormDir(string d)
        {
            string full = Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Length == 0) full = Path.GetFullPath(d);
            return Path.DirectorySeparatorChar == '\\' ? full.ToLowerInvariant() : full;
        }

        /// <summary>Looks for the bitmap written together with the definition file (same checksum) near
        /// the given folders: the folders, their subfolders, their parent folder and its subfolders.</summary>
        static string FindMatchingBitmap(string name, uint crc, IList<string> baseDirs)
        {
            var seen = new HashSet<string>();
            var cands = new List<string>();
            void Add(string d) { if (seen.Add(NormDir(d))) cands.Add(Path.GetFullPath(d)); }
            foreach (var b in baseDirs) Add(b);
            foreach (var b in baseDirs)
            {
                string parent = Path.GetDirectoryName(Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar)) ?? b;
                foreach (var root in new[] { b, parent })
                {
                    Add(root);
                    try
                    {
                        foreach (var e in Directory.GetDirectories(root).OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal)) Add(e);
                    }
                    catch (Exception) { }
                }
            }
            foreach (var d in cands)
            {
                string pth = JoinRel(d, name);
                try { if (File.Exists(pth) && Crc(pth) == crc) return pth; } catch (Exception) { }
            }
            return null;
        }

        /// <summary>Assigns bitmap rectangles to state slots and fills in the missing ones.
        /// Returns the list of used materials and the highest used colour.</summary>
        public static List<string> Resolve(Project project, IList<string> baseDirs, out int maxColor)
        {
            string fmt = project.Fmt;
            var used = new List<string>();
            maxColor = 0;
            var items = project.Items.Values.ToList();
            var known = new HashSet<string>(project.Materials.Select(m => Text.AsciiLower(m.Name)));
            foreach (var item in items)
                foreach (var kv in item.States)
                    foreach (var cm in kv.Value.ColorMat)
                        if (cm != null && !known.Contains(cm))
                            throw new DefinitionError($"Item {item.Number} State {kv.Key}: material '{cm}' is not listed in the Materials section");

            var found = new List<KeyValuePair<string, List<int>>>();
            foreach (var mat in project.Materials)
            {
                string path = FindBitmap(mat.Name, baseDirs, fmt);
                string bname = BitmapName(mat.Name, fmt);
                if (project.BitmapCrc.TryGetValue(Text.AsciiLower(bname), out uint want) && Crc(path) != want)
                {
                    string other = FindMatchingBitmap(bname, want, baseDirs);
                    if (other != null)
                    {
                        Log.Line($"Note: {path} is not the bitmap written together with this definition file - using {other} (it matches the checksum in the definition file)");
                        path = other;
                    }
                    else
                        Log.Line($"WARNING: bitmap {path} is not the one written together with this definition file (it was edited, or overwritten - e.g. by exporting another .dat file with the same materials into the same folder - or the .txt file was copied without its bitmaps), and no matching bitmap was found in the nearby folders. Colours may be missing or point to wrong rectangles.");
                }
                long[][] rects; long t; int perRow;
                if (Fmt.IsNew(fmt))
                {
                    rects = ReadBitmap(path, out int w, out int h);
                    if (!E2160Sizes.Contains(w) || !E2160Sizes.Contains(h)) throw new BitmapError($"Invalid bitmap format {Path.GetFileName(path)} ({w}x{h})");
                    t = 0; perRow = 0;
                }
                else
                {
                    rects = ReadBitmap(path, out int w, out int h);
                    t = mat.Tile;
                    if (t <= 0 || w % t != 0 || h % t != 0) throw new BitmapError($"{path}: Invalid bitmap format (dimensions {w}x{h} are not a multiple of {mat.Tile})");
                    perRow = (int)(w / t);
                }
                found.Add(new KeyValuePair<string, List<int>>(path, Enumerable.Range(1, 255).Where(c => rects[c][0] <= rects[c][2] && rects[c][1] <= rects[c][3]).ToList()));
                string matLow = Text.AsciiLower(mat.Name);
                for (int c = 1; c < 256; c++)
                {
                    long L = rects[c][0], T = rects[c][1], R = rects[c][2], B = rects[c][3];
                    if (!(L <= R && T <= B)) continue;
                    maxColor = Math.Max(maxColor, c);
                    if (t != 0 && ((FloorDiv(L, t) != FloorDiv(R - 1, t)) || (FloorDiv(T, t) != FloorDiv(B - 1, t))))
                        Log.Line($"Warning: colour {c} on bitmap {mat.Name} extends beyond a single tile");
                    long key = Fmt.IsNew(fmt) ? mat.Base * 256 + c : c;
                    foreach (var item in items)
                        foreach (var skv in item.States)
                        {
                            var st = skv.Value;
                            for (int i = 0; i < CState.Slots; i++)
                            {
                                if (st.ColorMat[i] != null) { if (st.ColorMat[i] != matLow || st.Colors[i] != c) continue; }
                                else if (st.Colors[i] != key) continue;
                                if (st.Material[i] != -1) throw new DefinitionError($"Color {c} duplicated on bitmap {mat.Name}");
                                int mi = used.FindIndex(n => Text.AsciiLower(n) == matLow);
                                if (mi < 0) { used.Add(mat.Name); mi = used.Count - 1; }
                                st.Material[i] = mi;
                                if (Fmt.IsNew(fmt)) st.Rects[i] = new[] { L, T, R, B };
                                else
                                {
                                    st.Tile[i] = (int)(FloorDiv(T, t) * perRow + FloorDiv(L, t));
                                    st.Rects[i] = new[] { Mod(L, t), Mod(T, t), Mod(R - 1, t) + 1, Mod(B - 1, t) + 1 };
                                }
                            }
                        }
                }
            }

            foreach (var item in items)
                foreach (var skv in item.States)
                {
                    long sn = skv.Key; var st = skv.Value;
                    for (int i = 0; i < CState.Slots; i++)
                    {
                        var r = st.Rects[i];
                        if (st.Colors[i] != 0 && r[2] < r[0])
                        {
                            long col = st.Colors[i];
                            string shown = !Fmt.IsNew(fmt) || col < 256 ? col.ToString() : $"0x{col:X4}";
                            string where = st.ColorMat[i] ?? "any bitmap";
                            string lst = string.Join("\n", found.Select(f => $"  {f.Key}: {f.Value.Count} colours" + (f.Value.Count > 0 ? $" ({Ranges(f.Value)})" : "")));
                            throw new DefinitionError(
                                $"Some rects remain undefined - Item {item.Number} - State {sn} - Rect {i}\n" +
                                $"(slot {i + 1}: colour {shown} is not on {where}). Bitmaps used:\n{lst}\n" +
                                "The bitmaps probably do not belong to this definition file - use the bitmaps written together with it (export every .dat file into its own folder).");
                        }
                        if (st.Particle.Length > 0) st.Rects[i] = new long[] { 0, 0, 0, 0 };
                        else if (r[2] <= r[0] || r[3] <= r[1])
                        {
                            var d = DeriveRect(i, item.Flags, st.Rects);
                            if (d != null) st.Rects[i] = d;
                        }
                    }
                    if (st.Particle.Length > 0) ApplyMargins(item.Margins, st.Rects);
                }
            return used;
        }

        static long FloorDiv(long a, long b) { long q = a / b; if ((a % b != 0) && ((a < 0) != (b < 0))) q--; return q; }
        static long Mod(long a, long b) { long m = a % b; if (m != 0 && ((m < 0) != (b < 0))) m += b; return m; }

        /// <summary>Computes the missing rectangle of slot i from its neighbours (as the original does).
        /// Slots: 0 background, 1 top, 2 bottom, 3 left, 4 right, 5 TL, 6 TR, 7 BL, 8 BR.</summary>
        public static long[] DeriveRect(int i, long flags, long[][] rc)
        {
            long[] s0 = rc[0], s1 = rc[1], s2 = rc[2], s3 = rc[3], s4 = rc[4];
            bool nl = (flags & Flags.NoLeft) != 0, nr = (flags & Flags.NoRight) != 0, nt = (flags & Flags.NoTop) != 0,
                 nb = (flags & Flags.NoBottom) != 0, nbg = (flags & Flags.NoBackground) != 0;
            switch (i)
            {
                case 0:
                    if (!nbg) return null;
                    if ((nl && nr) || (nt && nb)) return new long[] { 0, 0, 0, 0 };
                    long x = nl ? s4[0] : s3[2];
                    long y = nt ? s2[1] : s1[3];
                    return new[] { x, y, x, y };
                case 1: return new[] { s0[0], s0[1], s0[2], s0[1] };
                case 2: return new[] { s0[0], s0[3], s0[2], s0[3] };
                case 3: return new[] { s0[0], s0[1], s0[0], s0[3] };
                case 4: return new[] { s0[2], s0[1], s0[2], s0[3] };
            }
            long[] r = null;
            if (i == 5) { if (nt) r = new[] { s3[0], s3[1], s3[2], s3[1] }; if (nl) r = new[] { s1[0], s1[1], s1[0], s1[3] }; }
            else if (i == 6) { if (nt) r = new[] { s4[0], s4[1], s4[2], s4[1] }; if (nr) r = new[] { s1[2], s1[1], s1[2], s1[3] }; }
            else if (i == 7) { if (nb) r = new[] { s3[0], s3[3], s3[2], s3[3] }; if (nl) r = new[] { s2[0], s2[1], s2[0], s2[3] }; }
            else if (i == 8) { if (nb) r = new[] { s4[0], s4[3], s4[2], s4[3] }; if (nr) r = new[] { s2[2], s2[1], s2[2], s2[3] }; }
            return r;
        }

        /// <summary>Earth 2160: a state with a particle file has zero rectangles, and the item margins go
        /// into the right/bottom coordinate of the edges and corners. As in the original, the bottom
        /// edge gets BottomMargin only when RightMargin is non-zero.</summary>
        public static void ApplyMargins(long[] m, long[][] rc)
        {
            long L = m[0], T = m[1], R = m[2], B = m[3];
            if (L == 0 && R == 0 && T == 0) return;
            if (L != 0) rc[3][2] = L;
            if (R != 0) rc[4][2] = R;
            if (T != 0) rc[1][3] = T;
            if (R != 0) rc[2][3] = B;
            if (L != 0 && T != 0) { rc[5][2] = L; rc[5][3] = T; }
            if (R != 0 && T != 0) { rc[6][2] = R; rc[6][3] = T; }
            if (L != 0 && B != 0) { rc[7][2] = L; rc[7][3] = B; }
            if (R != 0 && B != 0) { rc[8][2] = R; rc[8][3] = B; }
        }

        static byte[] PStr(string s)
        {
            byte[] b = Text.ToLatin1(s);
            if (b.Length > 255) throw new DefinitionError($"name too long: '{s}'");
            var o = new byte[b.Length + 1];
            o[0] = (byte)b.Length;
            Buffer.BlockCopy(b, 0, o, 1, b.Length);
            return o;
        }

        static void LStr(ByteWriter w, string s) { byte[] b = Text.ToLatin1(s); w.U32(b.Length); w.Bytes(b); }

        public static byte[] WriteDat(Project project, List<string> materials, bool verbose, string fmt = null)
        {
            fmt = fmt ?? project.Fmt;
            bool ks = fmt == "ks", e2160 = Fmt.IsNew(fmt), tw = Fmt.IsTw(fmt), tw2 = fmt == "tw2";
            var o = new ByteWriter();
            if (!e2160) { o.U32(FileMagic); o.U32(ks ? FileIdKs : FileId); }
            if (tw)
            {
                if (!(project.BaseW == 640 && project.BaseH == 480 && project.ScaleX == 1.0f && project.ScaleY == 1.0f))
                    Log.Line("Warning: the TwoWorlds format does not store the resolution or scale (Scale) - skipped");
            }
            else { o.I16(project.BaseW); o.I16(project.BaseH); o.F32(project.ScaleX); o.F32(project.ScaleY); }
            if (ks || e2160) o.U32(project.GlobalFlags & 0xFFFFFFFFL);
            else if (project.GlobalFlags != 0) Log.Line("Warning: global flags (DefaultScale*/GlobalFlags) do not exist in the Earth 2150 format - skipped");
            foreach (var m in materials) o.Bytes(PStr(m));
            o.U8(0);
            if (!tw2) { foreach (var s in project.Sounds) o.Bytes(PStr(s)); o.U8(0); }
            foreach (var f in project.Fonts)
            {
                o.Bytes(PStr(f.Name));
                o.U16(f.Size & 0xFFFF); o.U16(f.Weight & 0xFFFF); o.U8(f.Italic & 0xFF); o.U8(f.ShadowX & 0xFF); o.U8(f.ShadowY & 0xFF);
                o.U32(f.Color & 0xFFFFFFFFL); o.U32(f.ShadowColor & 0xFFFFFFFFL);
                if (tw2) o.U8(f.Extra & 0xFF);
                if (verbose) Log.Line($"{f.Name} {f.Size} {f.Weight} {f.Italic} {f.ShadowX} {f.ShadowY} 0x{f.Color:x8} 0x{f.ShadowColor:x8}");
            }
            o.U8(0xFF);
            foreach (var num in project.ItemRepeatOrder)
                if (!project.Items.ContainsKey(num)) throw new DefinitionError($"RepeatItem {num}: item {num} is not defined");
            foreach (var kvi in project.Items)
            {
                long num = kvi.Key;
                int times = 1 + (project.ItemRepeats.TryGetValue(num, out int rep) ? rep : 0);
                for (int t = 0; t < times; t++)
                {
                    var item = kvi.Value;
                    long flags = (item.Flags & 0xFF) | item.ExtFlags | project.GlobalFlags;
                    if (e2160 || ks)
                    {
                        if (!(num >= 0 && num < 0xFFFF)) throw new DefinitionError($"item number {num} out of range");
                        if (tw)
                        {
                            if (item.SizeScale != 1.0f) Log.Line($"Warning: Item {num}: SizeScale does not exist in the TwoWorlds format - skipped");
                            o.U16(num); o.U32(flags & 0xFFFFFFFFL); o.F32(item.ShowStartTime);
                        }
                        else
                        {
                            o.U16(num); o.U32(flags & 0xFFFFFFFFL); o.F32(item.SizeScale);
                            if (e2160) o.F32(item.ShowStartTime);
                        }
                    }
                    else
                    {
                        if (!(num >= 0 && num < 255)) throw new DefinitionError($"item number {num} out of range 0..254 (Earth 2150 format)");
                        if (item.SizeScale != 1.0f || item.ExtFlags != 0)
                            Log.Line($"Warning: Item {num}: SizeScale/ScaleBorder/ScaleBackground do not exist in the Earth 2150 format - skipped");
                        o.U8(num); o.U32(item.Flags & 0xFF);
                    }
                    foreach (var skv in item.States)
                    {
                        var st = skv.Value;
                        var dw = st.Props.ToDwords();
                        if (dw.Count > 255) throw new DefinitionError($"Item {num} State {skv.Key}: too many properties");
                        o.U16(skv.Key & 0xFFFF); o.U8(dw.Count);
                        foreach (var v in dw) o.U32(v);
                        for (int i = 0; i < CState.Slots; i++)
                        {
                            long mt;
                            if (st.Material[i] < 0) mt = 0xFFFF;
                            else if (e2160) mt = st.Material[i] & 0xFF;
                            else mt = ((st.Tile[i] & 0xFF) << 8) | (st.Material[i] & 0xFF);
                            o.U16(mt);
                            foreach (var v in st.Rects[i]) o.U16(v & 0xFFFF);
                        }
                        if (e2160)
                        {
                            o.U32(st.FaceColor & 0xFFFFFFFFL);
                            if (tw2) o.U32(st.FaceColor2 & 0xFFFFFFFFL);
                            o.Bytes(PStr(st.Particle));
                            if (tw2) { o.Bytes(PStr(st.Texture)); o.U32(st.TextureMargin & 0xFFFFFFFFL); }
                        }
                    }
                    o.U16(0xFFFF);
                }
            }
            o.U16(0xFFFF);
            if (e2160)
            {
                o.U32(project.CustomData.Count);
                foreach (var kv in project.CustomData)
                {
                    LStr(o, kv.Key); o.U32(kv.Value.Count);
                    foreach (var s in kv.Value) LStr(o, s);
                }
                o.U32(project.Dialogs.Count);
                foreach (var d in project.Dialogs)
                {
                    foreach (var v in new[] { d.Id, d.Item }.Concat(d.Force)) o.U32(v & 0xFFFFFFFFL);
                    o.U32(d.Controls.Count);
                    foreach (var rec in d.Controls) foreach (var v in rec) o.U32(v & 0xFFFFFFFFL);
                }
            }
            return o.ToArray();
        }

        public static string FindResourceH(string descPath, string fname, string workDir = null)
        {
            foreach (var d in new[] { Path.GetDirectoryName(Path.GetFullPath(descPath)), workDir ?? Directory.GetCurrentDirectory() })
            {
                string pth = Path.Combine(d, fname);
                if (File.Exists(pth)) return pth;
            }
            return null;
        }

        static readonly Regex CrcRe = new Regex(@"^\s*//\s*Bitmap\s+(.+?)\s+crc32\s+0x([0-9A-Fa-f]{8})\b");

        /// <summary>Compiles a definition file. Bitmaps and the resource header are looked up next to
        /// the .txt first, then in the working directory (the original only used the latter).</summary>
        public static byte[] Build(string descPath, bool verbose = true, string fmt = "e2150", string workDir = null)
        {
            workDir = workDir ?? Directory.GetCurrentDirectory();
            var project = new Project { Fmt = fmt };
            if (Fmt.IsNew(fmt))
            {
                string rh = FindResourceH(descPath, Fmt.ResourceFile(fmt), workDir);
                if (rh == null) Log.Line($"WARNING: '{Fmt.ResourceFile(fmt)}' not found");
                project.Resources = Resources.Load(rh);
            }
            var parser = new Parser(project, verbose);
            parser.Load(descPath);
            foreach (var r in parser.Raw)
            {
                var m = CrcRe.Match(r.Value);
                if (m.Success) project.BitmapCrc[Text.AsciiLower(m.Groups[1].Value)] = Convert.ToUInt32(m.Groups[2].Value, 16);
            }
            parser.Parse();
            var unused = Enumerable.Range(0, project.Fonts.Count).Where(i => !project.Fonts[i].Used).ToList();
            if (unused.Count > 0)
            {
                if (Fmt.IsNew(fmt)) { if (verbose) foreach (var i in unused) Log.Line($"!!! --->>> font unused: {i}"); }
                else throw new DefinitionError("Some fonts remain unused");
            }
            var baseDirs = new List<string> { Path.GetDirectoryName(Path.GetFullPath(descPath)), workDir };
            var materials = Resolve(project, baseDirs, out int maxColor);
            byte[] data = WriteDat(project, materials, verbose, fmt);
            if (verbose)
            {
                Log.Line($"First free entry is {maxColor + 1}\nOk");
                Log.Line($"Materials used {materials.Count}");
                Log.Line($"Fonts  used {project.Fonts.Count}");
                Log.Line($"Sounds used {project.Sounds.Count}");
            }
            return data;
        }

        /// <summary>Command "compile": [--ks|--e2160|--tw|--tw2] description.txt output.dat</summary>
        public static int Run(IList<string> argv)
        {
            string fmt = "e2150";
            var args = new List<string>();
            foreach (var a in argv)
            {
                string f = a.StartsWith("--") ? Fmt.FromSwitch(a) : null;
                if (f != null) fmt = f; else args.Add(a);
            }
            if (args.Count < 2)
            {
                Log.Line("Usage: InterfaceTools compile [--ks | --e2160 | --tw | --tw2] <descriptionFile> <outputFile>\n" +
                         "  (no switch)         Earth 2150 (also " + Fmt.E2150Family + ")\n  --ks                KnightShift\n  --e2160             Earth 2160\n" +
                         "  --tw                TwoWorlds\n  --tw2               Two Worlds II");
                return 1;
            }
            byte[] data;
            try { data = Build(args[0], true, fmt); }
            catch (DefinitionError e) { Log.Line($"Error in the definition file: {e.Message}"); return 1; }
            catch (Exception e) when (e is BitmapError || e is IOException || e is UnauthorizedAccessException)
            { Log.Line($"Error: {e.Message}"); return 1; }
            File.WriteAllBytes(args[1], data);
            return 0;
        }
    }
}

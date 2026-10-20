// Names and descriptions for items and states, presets and developers' notes.
// Port of ifacedb.py; the database (interfacemap_names.json) is embedded in the program.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace InterfaceTools
{
    public class NameDb
    {
        /// <summary>Which set of game names fits which .dat format (KnightShift and Two Worlds II:
        /// only the core item types 0-22 are known to match).</summary>
        public static readonly Dictionary<string, string> GameForFormat = new Dictionary<string, string>
            { { "e2150", "e2150" }, { "ks", null }, { "e2160", "e2160" }, { "tw", "tw" }, { "tw2", null } };

        public JObj Data = new JObj();
        public Dictionary<long, string> UserItems = new Dictionary<long, string>();
        public Dictionary<long, Dictionary<long, string>> UserStates = new Dictionary<long, Dictionary<long, string>>();
        public string UserSource;
        /// <summary>format -> chosen file version (or null).</summary>
        public Dictionary<string, JObj> Variant = new Dictionary<string, JObj>();

        static JObj embedded;

        public static JObj LoadEmbedded()
        {
            if (embedded != null) return embedded;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("interfacemap_names.json", StringComparison.OrdinalIgnoreCase));
                if (name == null) return embedded = new JObj();
                using (var s = asm.GetManifestResourceStream(name))
                using (var r = new StreamReader(s, Encoding.UTF8))
                    embedded = Json.Parse(r.ReadToEnd()) as JObj ?? new JObj();
            }
            catch (Exception) { embedded = new JObj(); }
            return embedded;
        }

        public NameDb() { Data = LoadEmbedded(); }

        public NameDb ShallowCopy()
        {
            var n = (NameDb)MemberwiseClone();
            n.Variant = new Dictionary<string, JObj>(Variant);
            return n;
        }

        public static string Key(long n) => n.ToString(CultureInfo.InvariantCulture);

        JObj Game(string fmt)
        {
            if (fmt == null || !GameForFormat.TryGetValue(fmt, out string g) || g == null) return new JObj();
            return Data.Obj("games")?.Obj(g) ?? new JObj();
        }

        static string Get(JObj o, string k) => o?[k] as string;

        /// <summary>Chooses the file version (e.g. Earth 2160 release vs. the developers' newer file, TwoWorlds
        /// PC vs. Xbox) whose item numbers match the opened file best. Returns its description.</summary>
        public string SelectFor(string fmt, IEnumerable<long> itemNumbers)
        {
            var game = Game(fmt);
            Variant[fmt] = null;
            if (game.Count == 0) return "";
            var nums = new HashSet<long>(itemNumbers);
            double Score(IEnumerable<long> present)
            {
                var p = new HashSet<long>(present);
                int inter = nums.Count(p.Contains);
                var uni = new HashSet<long>(nums); uni.UnionWith(p);
                return (double)inter / Math.Max(1, uni.Count);
            }
            IEnumerable<long> basePresent = game.Has("present")
                ? game.Arr("present").Select(Json.ToLong)
                : (game.Obj("items")?.Keys.Select(k => long.Parse(k, CultureInfo.InvariantCulture)) ?? Enumerable.Empty<long>());
            JObj best = null;
            double bestScore = Score(basePresent);
            foreach (var vo in game.Arr("variants") ?? new List<object>())
            {
                var v = vo as JObj;
                double sc = Score(v.Arr("present").Select(Json.ToLong));
                if (sc > bestScore) { best = v; bestScore = sc; }
            }
            Variant[fmt] = best;
            return best != null ? best.Str("name") : "";
        }

        JObj VariantFor(string fmt) => fmt != null && Variant.TryGetValue(fmt, out var v) ? v : null;

        // ------------------------------------------------------------------ user names
        public int LoadUserNames(string path)
        {
            ParseNamesTxt(path, out UserItems, out UserStates);
            UserSource = path;
            return UserItems.Count;
        }

        public void ClearUserNames()
        {
            UserItems = new Dictionary<long, string>();
            UserStates = new Dictionary<long, Dictionary<long, string>>();
            UserSource = null;
        }

        static readonly Regex ItemRe = new Regex(@"^\s*Item\s*:\s*(\d+)\s*(.*?)\s*$", RegexOptions.IgnoreCase);
        static readonly Regex StateRe = new Regex(@"^\s*State\s*:\s*(0x[0-9A-Fa-f]+|\d+)\s*(.*?)\s*$", RegexOptions.IgnoreCase);

        /// <summary>Reads the 'Item:N name' and 'State:0xNN name' lines of a definition file.</summary>
        public static void ParseNamesTxt(string path, out Dictionary<long, string> items, out Dictionary<long, Dictionary<long, string>> states)
        {
            items = new Dictionary<long, string>();
            states = new Dictionary<long, Dictionary<long, string>>();
            long? item = null;
            string text = Cp1250().GetString(File.ReadAllBytes(path));
            foreach (var line in text.Split('\n'))
            {
                var m = ItemRe.Match(line);
                if (m.Success)
                {
                    item = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (m.Groups[2].Value.Length > 0) items[item.Value] = m.Groups[2].Value;
                    continue;
                }
                m = StateRe.Match(line);
                if (m.Success && item != null && m.Groups[2].Value.Length > 0)
                {
                    string v = m.Groups[1].Value;
                    long sn = v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(v.Substring(2), 16) : long.Parse(v, CultureInfo.InvariantCulture);
                    if (!states.TryGetValue(item.Value, out var d)) states[item.Value] = d = new Dictionary<long, string>();
                    d[sn] = m.Groups[2].Value;
                }
            }
        }

        static Encoding Cp1250()
        {
            try { return Encoding.GetEncoding(1250, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback); }
            catch (Exception) { return Text.Latin1; }
        }

        /// <summary>Name for a definition file: Latin-1 only, no braces or comment markers.</summary>
        public static string TextSafe(string s)
        {
            s = s.Replace("ł", "l").Replace("Ł", "L");
            try { s = s.Normalize(NormalizationForm.FormKD); } catch (Exception) { }
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.EnclosingMark) continue;
                sb.Append(c > 'ÿ' ? '?' : c);
            }
            s = sb.ToString().Replace("{", "(").Replace("}", ")").Replace("//", "/");
            return string.Join(" ", s.Split(new char[0], StringSplitOptions.RemoveEmptyEntries));
        }

        // ------------------------------------------------------------------ names
        public string ItemName(string fmt, long num)
        {
            if (UserItems.TryGetValue(num, out string u)) return u;
            if (UserSource != null) return "";
            var v = VariantFor(fmt);
            string k = Key(num);
            if (v != null && v.Obj("items").Has(k)) return v.Obj("items").Str(k);
            string n = Get(Game(fmt).Obj("items"), k);
            if (!string.IsNullOrEmpty(n)) return n;
            return Get(Data.Obj("core_items"), k) ?? "";
        }

        public string ItemGroup(string fmt, long num)
        {
            if (UserSource != null) return "";
            var v = VariantFor(fmt);
            string k = Key(num);
            if (v != null && v.Obj("items").Has(k)) return Get(v.Obj("groups"), k) ?? "";
            return Get(Game(fmt).Obj("groups"), k) ?? "";
        }

        /// <summary>Developers' note on an item (e.g. "possibly used directly by the game code").</summary>
        public string ItemNote(string fmt, long num)
        {
            if (UserSource != null) return "";
            return Get(Game(fmt).Obj("item_notes"), Key(num)) ?? "";
        }

        /// <summary>Developers' note on a control of a DialogTemplate (control null = the dialog itself).</summary>
        public string ControlNote(string fmt, string dialog, string control)
        {
            if (string.IsNullOrEmpty(dialog)) return "";
            dialog = dialog.ToUpperInvariant();
            control = string.IsNullOrEmpty(control) ? null : control.ToUpperInvariant();
            foreach (var o in Game(fmt).Arr("control_notes") ?? new List<object>())
            {
                var n = o as JObj;
                string nd = (n.Str("dialog") ?? "").ToUpperInvariant();
                string nc = string.IsNullOrEmpty(n.Str("control")) ? null : n.Str("control");
                if (nd == dialog && nc == control) return n.Str("note") ?? "";
            }
            return "";
        }

        public string GenericStateName(long sn)
        {
            var gs = Data.Obj("generic_states");
            string g = Get(gs, Key(sn));
            if (g != null) return g;
            long special = sn & 0x60, bas = sn & ~0x60L;
            string tag = Get(Data.Obj("special_state_bits"), Key(special));
            string b = Get(gs, Key(bas));
            if (tag != null && b != null && sn < 0x100) return $"{tag}: {b}";
            return "";
        }

        public string StateName(string fmt, long num, long sn)
        {
            if (UserStates.TryGetValue(num, out var us) && us.TryGetValue(sn, out string u)) return u;
            var v = VariantFor(fmt);
            if (UserSource == null)
            {
                var src = v != null && v.Obj("items").Has(Key(num)) ? v : Game(fmt);
                string n = Get(src.Obj("states")?.Obj(Key(num)), Key(sn));
                if (!string.IsNullOrEmpty(n)) return n;
            }
            return GenericStateName(sn);
        }

        // ------------------------------------------------------------------ presets
        /// <summary>A game may have its own presets (TwoWorlds: the Earth 2160 Light names with other
        /// values); otherwise the Earth 2160 spreadsheet presets are offered.</summary>
        List<JObj> Presets(string key, string fmt)
        {
            var own = fmt != null ? Game(fmt).Arr(key) : null;
            var lst = own ?? Data.Arr(key) ?? new List<object>();
            return lst.OfType<JObj>().ToList();
        }

        public List<JObj> FlagPresets(string fmt = null) => Presets("flag_presets", fmt);
        public List<JObj> LightPresets(string fmt = null) => Presets("light_presets", fmt);
        public List<JObj> FaceColorPresets(string fmt = null) => Presets("facecolor_presets", fmt);

        public string LightName(IList<long> values, string fmt = null)
        {
            var want = values.Select(v => v & 0xFFFFFFFFL).ToList();
            var names = LightPresets(fmt)
                .Where(p => p.Arr("values").Select(x => Json.ToLong(x) & 0xFFFFFFFFL).SequenceEqual(want))
                .Select(p => $"{p.Str("name")} {p.Str("desc")}".Trim());
            return string.Join(" / ", names);
        }

        /// <summary>Sets of extra states ($Special_Skill, ...): [new state, source state, FaceColor].</summary>
        public List<JObj> StateSets(string fmt) => (Game(fmt).Arr("state_sets") ?? new List<object>()).OfType<JObj>().ToList();

        /// <summary>Font definitions of the developers' spreadsheet (Earth 2160 and TwoWorlds).</summary>
        public List<JObj> SheetFonts(string fmt)
        {
            if (fmt == null || !GameForFormat.TryGetValue(fmt, out string g) || g == null) return new List<JObj>();
            return (Data.Obj("fonts")?.Arr(g) ?? new List<object>()).OfType<JObj>().ToList();
        }

        public string CustomDataDesc(string name)
        {
            var cd = Data.Obj("customdata");
            if (cd == null) return "";
            string n = name.ToLowerInvariant();
            string e = Get(cd.Obj("exact"), n);
            if (e != null) return e;
            foreach (var o in cd.Arr("prefix") ?? new List<object>())
            {
                var pair = o as List<object>;
                if (pair != null && pair.Count == 2 && n.StartsWith((string)pair[0], StringComparison.Ordinal)) return (string)pair[1];
            }
            return "";
        }
    }
}

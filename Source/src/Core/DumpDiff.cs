// "dump": prints the contents of a .dat file (port of datdump.py).
// "diff": compares two .dat files item by item and state by state (port of datdiff.py).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace InterfaceTools
{
    public static class PyFmt
    {
        public static string ListRepr(IEnumerable<string> l) => "[" + string.Join(", ", l.Select(Text.PyRepr)) + "]";
        public static string ListRepr(IEnumerable<long> l) => "[" + string.Join(", ", l) + "]";
        public static string G(float v) => Text.FormatG(v, 6);
        public static string PadRight(string s, int n) => s.Length >= n ? s : s + new string(' ', n - s.Length);
    }

    public static class DatDump
    {
        static readonly string[] SlotNames = { "bg", "top", "bottom", "left", "right", "TL", "TR", "BL", "BR" };

        public static void Dump(byte[] data)
        {
            var d = DatFile.Read(data);
            Log.Line($"format {Fmt.Names[d.Fmt]}, base resolution {d.BaseW}x{d.BaseH}, scale {PyFmt.G(d.ScaleX)} {PyFmt.G(d.ScaleY)}, global flags 0x{d.GlobalFlags:x}");
            Log.Line("materials: " + PyFmt.ListRepr(d.Materials));
            Log.Line("sounds: " + PyFmt.ListRepr(d.Sounds));
            for (int i = 0; i < d.Fonts.Count; i++)
            {
                var f = d.Fonts[i];
                Log.Line($"font {i}: {f.Name} size={f.Size} weight={f.Weight} italic={f.Italic} shadow=({f.Sx},{f.Sy}) colour=0x{f.Color:x8} shadow_colour=0x{f.SColor:x8}"
                         + (d.Fmt == "tw2" ? $" extra={f.Extra}" : ""));
            }
            foreach (var ikv in d.Items)
            {
                var it = ikv.Value;
                string extra = "";
                if (d.Fmt != "e2150" && d.Fmt != "tw") extra += $"  SizeScale={PyFmt.G(it.Scale)}";
                if (Fmt.IsNew(d.Fmt)) extra += $"  ShowStartTime={PyFmt.G(it.ShowStartTime)}";
                Log.Line();
                Log.Line($"Item {ikv.Key}  flags=0x{it.Flags:x}{extra}");
                foreach (var skv in it.States)
                {
                    var st = skv.Value;
                    string desc = "";
                    if (st.HasBlob)
                    {
                        desc = $"font={st.Font} sound={(st.Sound >= 0 ? d.Sounds[(int)st.Sound] : "-")}";
                        foreach (var r in st.Records)
                            desc += $" {(RecordKey.Names.TryGetValue(r.Key, out string kn) ? kn : r.Key.ToString(CultureInfo.InvariantCulture))}=" + string.Join(",", r.Values.Select(v => $"0x{v:x}"));
                    }
                    if (Fmt.IsNew(d.Fmt))
                    {
                        if (st.FaceColor != 0xFFFFFFFFL) desc += $" FaceColor=0x{st.FaceColor:x8}";
                        if (st.Particle.Length > 0) desc += $" particles={st.Particle}";
                        if (d.Fmt == "tw2")
                        {
                            if (st.FaceColor2 != 0xFFFFFFFFL) desc += $" TextColor=0x{st.FaceColor2:x8}";
                            if (st.Texture.Length > 0) desc += $" TextTexture={st.Texture} TextTextureParam={st.TextureMargin}";
                        }
                    }
                    Log.Line($"  State 0x{skv.Key:x}: {desc}");
                    for (int s = 0; s < st.Slots.Count; s++)
                    {
                        int mt = st.Slots[s].Mt; var r = st.Slots[s].R;
                        string src;
                        if (mt == 0xFFFF) src = "-";
                        else if (Fmt.IsNew(d.Fmt)) src = d.Materials[mt & 0xFF].Split('\\').Last();
                        else src = $"{d.Materials[mt & 0xFF]} tile {mt >> 8}";
                        Log.Line($"    {s + 1} {PyFmt.PadRight(SlotNames[s], 6)} {PyFmt.PadRight(src, 24)} L={r[0]} T={r[1]} R={r[2]} B={r[3]}");
                    }
                }
            }
            foreach (var kv in d.CustomData) { Log.Line(); Log.Line($"CustomData {kv.Key}: {PyFmt.ListRepr(kv.Value)}"); }
            foreach (var dl in d.Dialogs)
            {
                Log.Line(); Log.Line($"DialogTemplate id={dl.Id} item={dl.Item} force={PyFmt.ListRepr(dl.Force)}");
                foreach (var c in dl.Controls) Log.Line($"  id={c[0]} item={c[1]} {c[2]} {c[3]}");
            }
            Log.Line(); Log.Line("end");
        }

        public static int Run(IList<string> argv)
        {
            if (argv.Count < 1) { Log.Line("Usage: InterfaceTools dump file.dat"); return 1; }
            Dump(File.ReadAllBytes(argv[0]));
            return 0;
        }
    }

    public class DatDiff
    {
        static readonly string[] SlotNames = { "background", "top", "bottom", "left", "right", "top-left", "top-right", "bottom-left", "bottom-right" };
        readonly DatFile a, b;
        readonly NameDb db, dbA;
        readonly ResourceNames res;
        readonly List<string> notes = new List<string>();
        readonly List<string> output = new List<string>();

        static string H(long v) => Text.Hex32(v);

        public DatDiff(DatFile a, DatFile b, NameDb db, ResourceNames res)
        {
            this.a = a; this.b = b; this.db = db; this.res = res;
            dbA = db.ShallowCopy();
            foreach (var (which, dbx, d) in new[] { ("A", dbA, a), ("B", db, b) })
            {
                if (dbx.UserSource == null)
                {
                    string v = dbx.SelectFor(d.Fmt, d.Items.Keys);
                    if (v.Length > 0) notes.Add($"Names for {which}: {v}");
                }
            }
        }

        void P(string t = "") => output.Add(t);

        string ItemText(long n, string side = "")
        {
            string na = side != "b" ? dbA.ItemName(a.Fmt, n) : "";
            string nb = side != "a" ? db.ItemName(b.Fmt, n) : "";
            string name = na.Length > 0 && nb.Length > 0 && na != nb ? $"A: {na} / B: {nb}" : (nb.Length > 0 ? nb : na);
            return $"Item {n}" + (name.Length > 0 ? $" ({name})" : "");
        }

        string StateText(long n, long s)
        {
            string name = db.StateName(b.Fmt, n, s);
            return $"State 0x{s:X2}" + (name.Length > 0 ? $" ({name})" : "");
        }

        string Rid(long v)
        {
            if (res != null)
                foreach (var low in res.Order)
                    if (res.Values.TryGetValue(low, out long x) && x == v) return $"{v} {res.OrigNames[low]}";
            return v.ToString(CultureInfo.InvariantCulture);
        }

        static string Mat(DatFile d, int mt)
        {
            if (mt == 0xFFFF) return "-";
            int m = mt & 0xFF;
            string name = m < d.Materials.Count ? d.Materials[m].Replace("\\", "/").Split('/').Last() : $"#{m}";
            return Fmt.IsNew(d.Fmt) ? name : $"{name} tile {mt >> 8}";
        }

        static string Tuple2(long x, long y) => $"({x}, {y})";
        static string Tuple2(float x, float y) => $"({Text.PyReprFloat(x)}, {Text.PyReprFloat(y)})";

        public List<string> Run(bool brief)
        {
            P($"A: {Fmt.Names[a.Fmt]}, {a.Items.Count} items, {a.Items.Values.Sum(i => i.States.Count)} states");
            P($"B: {Fmt.Names[b.Fmt]}, {b.Items.Count} items, {b.Items.Values.Sum(i => i.States.Count)} states");
            if (a.Fmt != b.Fmt) P("Note: the files have different formats; only comparable parts are compared.");
            foreach (var n in notes) P(n);
            Header();
            Lists("Materials", a.Materials, b.Materials);
            Lists("Sounds", a.Sounds, b.Sounds);
            Fonts();
            var added = b.Items.Keys.Where(k => !a.Items.ContainsKey(k)).OrderBy(k => k).ToList();
            var removed = a.Items.Keys.Where(k => !b.Items.ContainsKey(k)).OrderBy(k => k).ToList();
            var common = a.Items.Keys.Where(b.Items.ContainsKey).OrderBy(k => k).ToList();
            var changed = common.Where(n => ItemLines(n).Count > 0).ToList();
            P();
            P($"Items: {added.Count} only in B, {removed.Count} only in A, {changed.Count} changed, {common.Count - changed.Count} identical");
            foreach (var (title, lst) in new[] { ("Only in A", removed), ("Only in B", added), ("Changed", changed) })
            {
                if (lst.Count == 0) continue;
                P($"  {title}:");
                foreach (var n in lst)
                {
                    var it = (title == "Only in A" ? a : b).Items[n];
                    string side = title == "Only in A" ? "a" : title == "Only in B" ? "b" : "";
                    P($"    {ItemText(n, side)}  [{it.States.Count} states]");
                }
            }
            if (!brief)
                foreach (var n in changed)
                {
                    P(); P($"{ItemText(n)}:");
                    foreach (var line in ItemLines(n)) P("  " + line);
                }
            if (Fmt.IsNew(a.Fmt) || Fmt.IsNew(b.Fmt)) { Custom(brief); Dialogs(brief); }
            return output;
        }

        void Header()
        {
            var diffs = new List<string>();
            if (a.BaseW != b.BaseW || a.BaseH != b.BaseH) diffs.Add($"  base resolution: {Tuple2(a.BaseW, a.BaseH)} -> {Tuple2(b.BaseW, b.BaseH)}");
            if (a.ScaleX != b.ScaleX || a.ScaleY != b.ScaleY) diffs.Add($"  aspect ratio: {Tuple2(a.ScaleX, a.ScaleY)} -> {Tuple2(b.ScaleX, b.ScaleY)}");
            if (H(a.GlobalFlags) != H(b.GlobalFlags)) diffs.Add($"  global flags: {H(a.GlobalFlags)} -> {H(b.GlobalFlags)}");
            if (diffs.Count > 0) { P(); P("Header:"); output.AddRange(diffs); }
        }

        void Lists(string title, List<string> x, List<string> y)
        {
            if (x.SequenceEqual(y)) return;
            P(); P($"{title}: {x.Count} -> {y.Count}");
            for (int i = 0; i < Math.Max(x.Count, y.Count); i++)
            {
                string u = i < x.Count ? x[i] : null, v = i < y.Count ? y[i] : null;
                if (u != v) P($"  #{i}: {(string.IsNullOrEmpty(u) ? "(none)" : u)} -> {(string.IsNullOrEmpty(v) ? "(none)" : v)}");
            }
        }

        void Fonts()
        {
            var fa = a.Fonts; var fb = b.Fonts;
            if (fa.Select(f => f.Key()).SequenceEqual(fb.Select(f => f.Key()))) return;
            P(); P($"Fonts: {fa.Count} -> {fb.Count}");
            string F(DatFont f) => f == null ? "(none)" : $"{f.Name} {f.Size}/{f.Weight} italic {f.Italic} shadow ({f.Sx},{f.Sy}) {H(f.Color)} {H(f.SColor)}";
            for (int i = 0; i < Math.Max(fa.Count, fb.Count); i++)
            {
                var u = i < fa.Count ? fa[i] : null; var v = i < fb.Count ? fb[i] : null;
                if (u == null || v == null || u.Key() != v.Key()) P($"  #{i}: {F(u)} -> {F(v)}");
            }
        }

        List<string> ItemLines(long n)
        {
            var x = a.Items[n]; var y = b.Items[n];
            var o = new List<string>();
            if (x.Flags != y.Flags) o.Add($"flags: {H(x.Flags)} -> {H(y.Flags)}");
            if (x.Scale != y.Scale) o.Add($"SizeScale: {PyFmt.G(x.Scale)} -> {PyFmt.G(y.Scale)}");
            if (x.ShowStartTime != y.ShowStartTime) o.Add($"ShowStartTime: {PyFmt.G(x.ShowStartTime)} -> {PyFmt.G(y.ShowStartTime)}");
            foreach (var s in x.States.Keys.Where(k => !y.States.ContainsKey(k))) o.Add($"{StateText(n, s)}: only in A");
            foreach (var s in y.States.Keys.Where(k => !x.States.ContainsKey(k))) o.Add($"{StateText(n, s)}: only in B");
            foreach (var s in x.States.Keys.Where(y.States.ContainsKey))
            {
                var lines = StateLines(x.States[s], y.States[s]);
                if (lines.Count > 0) { o.Add($"{StateText(n, s)}:"); o.AddRange(lines.Select(l => "  " + l)); }
            }
            return o;
        }

        static string RectRepr(long[] r) => "[" + string.Join(", ", r) + "]";

        List<string> StateLines(DatState x, DatState y)
        {
            var o = new List<string>();
            string FName(DatFile d, long i) => i >= 0 && i < d.Fonts.Count ? $"{i}: {d.Fonts[(int)i].Name} {d.Fonts[(int)i].Size}" : i.ToString(CultureInfo.InvariantCulture);
            if (x.Font != y.Font) o.Add($"font: {FName(a, x.Font)} -> {FName(b, y.Font)}");
            if (x.Sound != y.Sound)
            {
                string SName(DatFile d, long i) => i >= 0 && i < d.Sounds.Count ? d.Sounds[(int)i] : "(none)";
                o.Add($"sound: {SName(a, x.Sound)} -> {SName(b, y.Sound)}");
            }
            var ra = new Dictionary<int, List<long>>(); foreach (var r in x.Records) ra[r.Key] = r.Values;
            var rb = new Dictionary<int, List<long>>(); foreach (var r in y.Records) rb[r.Key] = r.Values;
            foreach (var k in ra.Keys.Union(rb.Keys).OrderBy(k => k))
            {
                ra.TryGetValue(k, out var va); rb.TryGetValue(k, out var vb);
                bool same = va != null && vb != null ? va.SequenceEqual(vb) : va == vb;
                if (same) continue;
                string name = RecordKey.Names.TryGetValue(k, out string kn) ? kn : $"Effect:{k}";
                string FR(List<long> v) => v == null ? "(none)" : string.Join(",", v.Select(e => e >= 0x10000 ? H(e) : e.ToString(CultureInfo.InvariantCulture)));
                o.Add($"{name}: {FR(va)} -> {FR(vb)}");
            }
            for (int i = 0; i < Math.Min(x.Slots.Count, y.Slots.Count); i++)
            {
                var sa = x.Slots[i]; var sb = y.Slots[i];
                if (sa.Mt != sb.Mt || !sa.R.SequenceEqual(sb.R))
                    o.Add($"slot {i + 1} {SlotNames[i]}: {Mat(a, sa.Mt)} {RectRepr(sa.R)} -> {Mat(b, sb.Mt)} {RectRepr(sb.R)}");
            }
            if (x.FaceColor != y.FaceColor) o.Add($"FaceColor: {H(x.FaceColor)} -> {H(y.FaceColor)}");
            if (x.Particle != y.Particle) o.Add($"particle: '{x.Particle}' -> '{y.Particle}'");
            if (x.FaceColor2 != y.FaceColor2 || x.Texture != y.Texture || x.TextureMargin != y.TextureMargin)
                o.Add($"TextColor/TextTexture/Param: {H(x.FaceColor2)} '{x.Texture}' {x.TextureMargin} -> {H(y.FaceColor2)} '{y.Texture}' {y.TextureMargin}");
            return o;
        }

        void Custom(bool brief)
        {
            var ca = new Dictionary<string, List<string>>(); foreach (var kv in a.CustomData) ca[kv.Key] = kv.Value;
            var cb = new Dictionary<string, List<string>>(); foreach (var kv in b.CustomData) cb[kv.Key] = kv.Value;
            var names = a.CustomData.Select(kv => kv.Key).Concat(b.CustomData.Select(kv => kv.Key).Where(n => !ca.ContainsKey(n))).ToList();
            bool Same(string n) => ca.TryGetValue(n, out var x) && cb.TryGetValue(n, out var y) ? x.SequenceEqual(y) : (ca.ContainsKey(n) == cb.ContainsKey(n));
            var diffs = names.Where(n => !Same(n)).ToList();
            if (diffs.Count == 0) return;
            P(); P($"CustomData: {diffs.Count} entries differ");
            foreach (var n in diffs)
            {
                if (!ca.ContainsKey(n)) P($"  {n}: only in B");
                else if (!cb.ContainsKey(n)) P($"  {n}: only in A");
                else
                {
                    P($"  {n}: changed");
                    if (brief) continue;
                    for (int i = 0; i < Math.Max(ca[n].Count, cb[n].Count); i++)
                    {
                        string u = i < ca[n].Count ? ca[n][i] : null, v = i < cb[n].Count ? cb[n][i] : null;
                        if (u != v) P($"    line {i + 1}: {(u == null ? "None" : Text.PyRepr(u))} -> {(v == null ? "None" : Text.PyRepr(v))}");
                    }
                }
            }
        }

        void Dialogs(bool brief)
        {
            var da = new Dictionary<long, DatDialog>(); var oa = new List<long>();
            foreach (var d in a.Dialogs) { if (!da.ContainsKey(d.Id)) oa.Add(d.Id); da[d.Id] = d; }
            var dbb = new Dictionary<long, DatDialog>(); var ob = new List<long>();
            foreach (var d in b.Dialogs) { if (!dbb.ContainsKey(d.Id)) ob.Add(d.Id); dbb[d.Id] = d; }
            string Sig(DatDialog d) => $"{d.Item}|{string.Join(",", d.Force)}|" + string.Join(";", d.Controls.Select(c => string.Join(",", c)).OrderBy(s => s, StringComparer.Ordinal));
            var onlyA = oa.Where(i => !dbb.ContainsKey(i)).ToList();
            var onlyB = ob.Where(i => !da.ContainsKey(i)).ToList();
            var changed = oa.Where(i => dbb.ContainsKey(i) && Sig(da[i]) != Sig(dbb[i])).ToList();
            if (onlyA.Count == 0 && onlyB.Count == 0 && changed.Count == 0) return;
            P(); P($"DialogTemplates: {onlyB.Count} only in B, {onlyA.Count} only in A, {changed.Count} changed");
            foreach (var i in onlyA) P($"  {Rid(i)}: only in A");
            foreach (var i in onlyB) P($"  {Rid(i)}: only in B");
            foreach (var i in changed)
            {
                var x = da[i]; var y = dbb[i];
                P($"  {Rid(i)}:");
                if (brief) continue;
                if (x.Item != y.Item) P($"    item: {x.Item} -> {y.Item}");
                if (!x.Force.SequenceEqual(y.Force)) P($"    force flags: {PyFmt.ListRepr(x.Force)} -> {PyFmt.ListRepr(y.Force)}");
                var ca = new Dictionary<long, long[]>(); foreach (var c in x.Controls) ca[c[0]] = c;
                var cb = new Dictionary<long, long[]>(); foreach (var c in y.Controls) cb[c[0]] = c;
                foreach (var cid in ca.Keys.Union(cb.Keys).OrderBy(k => k))
                {
                    ca.TryGetValue(cid, out var u); cb.TryGetValue(cid, out var v);
                    bool same = u != null && v != null ? u.SequenceEqual(v) : u == v;
                    if (same) continue;
                    string F(long[] c) => c == null ? "(none)" : $"item {c[1]}, offset {c[2]},{c[3]}";
                    P($"    control {Rid(cid)}: {F(u)} -> {F(v)}");
                }
            }
        }

        public static int Run(IList<string> argv)
        {
            var pos = new List<string>();
            string resource = null, names = null;
            bool brief = false;
            for (int i = 0; i < argv.Count; i++)
            {
                if (argv[i] == "--resource" && i + 1 < argv.Count) resource = argv[++i];
                else if (argv[i] == "--names" && i + 1 < argv.Count) names = argv[++i];
                else if (argv[i] == "--brief") brief = true;
                else pos.Add(argv[i]);
            }
            if (pos.Count != 2) { Log.Line("Usage: InterfaceTools diff old.dat new.dat [--resource resource.h] [--names original.txt] [--brief]"); return 1; }
            DatFile a, b;
            try { a = DatFile.ReadFile(pos[0]); b = DatFile.ReadFile(pos[1]); }
            catch (Exception e) when (e is IOException || e is ToolError || e is UnauthorizedAccessException) { Log.Line($"Error: {e.Message}"); return 1; }
            var db = new NameDb();
            if (names != null) db.LoadUserNames(names);
            var res = resource != null ? new ResourceNames(resource) : null;
            Log.Line(string.Join("\n", new DatDiff(a, b, db, res).Run(brief)));
            return 0;
        }
    }
}

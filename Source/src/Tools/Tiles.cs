// Number of tiles per row of the KnightShift .tex interface textures, from the definitions in
// the (text) .dat files: tile 256x256 px, adjacent tiles overlap by 4 px, so step = 252 px,
// columns = ceil(width / 252), rows = ceil(height / 252). Port of CheckNumberOfTiles.py; the
// export pipeline uses the result directly (no DialogsTiles.csv any more).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace InterfaceTools
{
    public static class Tiles
    {
        public const int Tile = 256, Overlap = 4, Step = Tile - Overlap;

        // dialog Name = Interface\File.tex 1008,756 ...   /   messagebox = Interface\File.tex 1008,756 ...
        static readonly Regex DialogRe = new Regex(@"^\s*(?:dialog\s+(?<name>\S+)|(?<mb>messagebox))\s*=\s*(?<path>\S+\.tex)\s+(?<w>\d+)\s*,\s*(?<h>\d+)", RegexOptions.IgnoreCase);
        // Bkglinks.dat:  Interface\link.tex Interface\realfile.tex
        static readonly Regex LinkRe = new Regex(@"^\s*(\S+\.tex)\s+(\S+\.tex)\s*$", RegexOptions.IgnoreCase);

        public class Entry { public string Key, Path, Dat, Name; public int W, H, Line; }

        /// <summary>Comparison key: file name only, lower case, without .tex.</summary>
        public static string Key(string path)
        {
            string name = path.Replace("\\", "/").Split('/').Last().ToLowerInvariant();
            return name.EndsWith(".tex") ? name.Substring(0, name.Length - 4) : name;
        }

        static Encoding Cp1250()
        {
            try { return Encoding.GetEncoding(1250, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback); }
            catch (Exception) { return Text.Latin1; }
        }

        static IEnumerable<string> FilesRecursive(string dir)
        {
            var all = new List<string>();
            try { all.AddRange(Directory.GetFiles(dir, "*", SearchOption.AllDirectories)); } catch (Exception) { }
            return all.OrderBy(p => p, StringComparer.Ordinal);
        }

        public static void Scan(string directory, out List<Entry> entries, out Dictionary<string, string> links)
        {
            entries = new List<Entry>();
            links = new Dictionary<string, string>();
            var enc = Cp1250();
            foreach (var dat in FilesRecursive(directory))
            {
                if (Path.GetExtension(dat).ToLowerInvariant() != ".dat") continue;
                string text;
                try { text = enc.GetString(File.ReadAllBytes(dat)); } catch (Exception) { continue; }
                int nr = 0;
                foreach (var line0 in Regex.Split(text, "\r\n|\r|\n"))
                {
                    nr++;
                    if (line0.TrimStart().StartsWith("//")) continue;
                    var m = DialogRe.Match(line0);
                    if (m.Success)
                    {
                        entries.Add(new Entry
                        {
                            Key = Key(m.Groups["path"].Value), Path = m.Groups["path"].Value,
                            W = int.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture), H = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture),
                            Dat = Path.GetFileName(dat), Line = nr, Name = m.Groups["name"].Success ? m.Groups["name"].Value : "messagebox"
                        });
                        continue;
                    }
                    m = LinkRe.Match(line0);
                    if (m.Success) links[Key(m.Groups[1].Value)] = Key(m.Groups[2].Value);
                }
            }
        }

        // texture paths such as Interface\DlgGoalsNoTabs.tex (like the script dialogs.py)
        static readonly Regex TexPathRe = new Regex(@"(?:[A-Za-z0-9_.-]+[\\/]+)+[A-Za-z0-9_.-]+\.tex", RegexOptions.IgnoreCase);

        /// <summary>Text .dat files (dialog / background definitions): binary .dat files contain zero bytes.</summary>
        public static bool IsTextDat(byte[] data)
        {
            int n = Math.Min(data.Length, 65536);
            for (int i = 0; i < n; i++) if (data[i] == 0) return false;
            return true;
        }

        /// <summary>The .tex textures named in the text .dat files of a folder and its subfolders (comments
        /// after // are ignored). Key: file name in lower case without '@' (e.g. "dlggoalsnotabs.tex");
        /// value: the path as written in the first file that names it. These textures are exported and
        /// imported with the Overlap variants of the TexEnv tileset commands.</summary>
        public static SortedDictionary<string, string> TextDatTextures(string directory)
        {
            var o = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var enc = Cp1250();
            foreach (var dat in FilesRecursive(directory))
            {
                if (Path.GetExtension(dat).ToLowerInvariant() != ".dat") continue;
                byte[] data;
                try { data = File.ReadAllBytes(dat); } catch (Exception) { continue; }
                if (!IsTextDat(data)) continue;
                foreach (var line in Regex.Split(enc.GetString(data), "\r\n|\r|\n"))
                {
                    int c = line.IndexOf("//", StringComparison.Ordinal);
                    string code = c >= 0 ? line.Substring(0, c) : line;
                    foreach (Match m in TexPathRe.Matches(code))
                    {
                        string path = m.Value.Replace('/', '\\');
                        string key = path.Split('\\').Last().Replace("@", "").ToLowerInvariant();
                        if (!o.ContainsKey(key)) o[key] = path;
                    }
                }
            }
            return o;
        }

        public static (int cols, int rows) Layout(int w, int h) =>
            (Math.Max(1, (w + Step - 1) / Step), Math.Max(1, (h + Step - 1) / Step));

        /// <summary>Texture file name (e.g. "DialogMM_Main.tex", lower case key) -> tiles per row,
        /// for all textures defined in the .dat files of the folder and its subfolders. When the
        /// definitions disagree, the largest value wins (as the CSV of the Python pipeline did).</summary>
        public static Dictionary<string, int> TilesPerRow(string directory, Action<string> log = null)
        {
            Scan(directory, out var entries, out _);
            var rows = new SortedDictionary<string, (string name, SortedSet<int> cols)>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                string name = e.Path.Replace("\\", "/").Split('/').Last();
                string k = name.ToLowerInvariant();
                if (!rows.TryGetValue(k, out var v)) rows[k] = v = (name, new SortedSet<int>());
                v.cols.Add(Layout(e.W, e.H).cols);
            }
            var o = new Dictionary<string, int>();
            foreach (var kv in rows)
            {
                if (kv.Value.cols.Count > 1) log?.Invoke($"WARNING: {kv.Value.name} has different widths in .dat files - using {kv.Value.cols.Max} tiles per row");
                string fn = kv.Value.name.Replace("@", "");
                fn = (Path.GetExtension(fn).Length > 0 ? fn.Substring(0, fn.Length - Path.GetExtension(fn).Length) : fn) + ".tex";
                o[fn.ToLowerInvariant()] = kv.Value.cols.Max;
            }
            return o;
        }

        /// <summary>(image count, tile width, tile height) from the .tex header.</summary>
        public static (long total, long tw, long th) TexTileCount(string path)
        {
            using (var f = File.OpenRead(path))
            using (var r = new BinaryReader(f))
            {
                var magic = r.ReadBytes(4);
                if (magic.Length < 4 || magic[0] != 'T' || magic[1] != 'E' || magic[2] != 'X' || magic[3] != 0) throw new ToolError("not a TEX file");
                r.ReadUInt32();
                uint flags = r.ReadUInt32();
                long total = 1;
                foreach (uint bit in new uint[] { 0x80000000, 0x40000000, 0x20000000, 0x10000000 })
                    if ((flags & bit) != 0) total *= r.ReadUInt32();
                if (total > 1) { r.ReadBytes(8); flags = r.ReadUInt32(); }
                r.ReadUInt32();
                long tw = (flags & 0x01000000) != 0 ? r.ReadUInt32() : 1;
                long th = (flags & 0x02000000) != 0 ? r.ReadUInt32() : 1;
                return (total, tw, th);
            }
        }

        const string Usage =
            "Usage: InterfaceTools tiles DAT_DIR [TEXTURE_NAME] [--tex FILE.tex] [--csv FILE]\n" +
            "       InterfaceTools tiles DAT_DIR --textures   (the .tex files named in the text .dat files)\n" +
            "  DAT_DIR        folder with .dat files (searched recursively)\n" +
            "  TEXTURE_NAME   e.g. DialogMM_Main or Interface\\DialogMM_Main.tex; without it all textures are listed\n" +
            "  --tex FILE     .tex file whose header is checked against the computed tile count\n" +
            "  --csv FILE     also write texture_name;tiles_per_row lines";

        public static int Run(IList<string> argv)
        {
            string dir = null, texture = null, tex = null, csv = null;
            bool textures = false;
            for (int i = 0; i < argv.Count; i++)
            {
                if (argv[i] == "--textures") { textures = true; continue; }
                if (argv[i] == "--tex" && i + 1 < argv.Count) tex = argv[++i];
                else if (argv[i] == "--csv" && i + 1 < argv.Count) csv = argv[++i];
                else if (dir == null) dir = argv[i];
                else if (texture == null) texture = argv[i];
            }
            if (dir == null) { Log.Line(Usage); return 1; }
            if (!Directory.Exists(dir)) { Log.Line($"No such directory: {dir}"); return 1; }
            if (textures)
            {
                // the .tex files named in the text .dat files (exported / imported with the Overlap commands)
                foreach (var p in Tiles.TextDatTextures(dir).Values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) Log.Line(p);
                return 0;
            }
            Scan(dir, out var entries, out var links);
            List<Entry> found;
            if (texture != null)
            {
                string wanted = Key(texture);
                var seen = new HashSet<string> { wanted };
                while (links.TryGetValue(wanted, out string to) && !seen.Contains(to))
                {
                    Log.Line($"Alias from Bkglinks: {wanted}.tex -> {to}.tex");
                    wanted = to;
                    seen.Add(wanted);
                }
                found = entries.Where(e => seen.Contains(e.Key)).ToList();
            }
            else found = entries;
            if (found.Count == 0) Log.Line("This texture was not found in any .dat file.");
            else
            {
                var groups = new List<(string key, int w, int h, string path, List<string> src)>();
                foreach (var e in found)
                {
                    int gi = groups.FindIndex(g => g.key == e.Key && g.w == e.W && g.h == e.H);
                    if (gi < 0) { groups.Add((e.Key, e.W, e.H, e.Path, new List<string>())); gi = groups.Count - 1; }
                    groups[gi].src.Add($"{e.Dat}:{e.Line} {e.Name}");
                }
                foreach (var g in groups)
                {
                    var (cols, rws) = Layout(g.w, g.h);
                    Log.Line($"{g.path,-40} {g.w,4}x{g.h,-4} -> columns: {cols}, rows: {rws}, tiles: {cols * rws}");
                    if (texture != null) foreach (var s in g.src) Log.Line($"      {s}");
                }
                if (texture != null && groups.Select(g => (g.w, g.h)).Distinct().Count() > 1)
                    Log.Line("WARNING: different entries specify different dimensions for this texture!");
            }
            if (csv != null)
            {
                var map = TilesPerRow(dir, Log.Line);
                var sb = new StringBuilder();
                foreach (var kv in map.OrderBy(k => k.Key, StringComparer.Ordinal)) sb.Append($"{kv.Key};{kv.Value}\r\n");
                File.WriteAllText(csv, sb.ToString(), Text.Utf8NoBom);
                Log.Line($"Saved {map.Count} textures to {csv}");
            }
            if (tex != null)
            {
                var (total, tw, th) = TexTileCount(tex);
                Log.Line($"\n{Path.GetFileName(tex)}: header contains {total} image(s), tile {tw}x{th}");
                if (found.Count > 0)
                    foreach (var (c, r) in found.Select(e => Layout(e.W, e.H)).Distinct().OrderBy(x => x.cols).ThenBy(x => x.rows))
                        Log.Line($"  {c}x{r} = {c * r} -> {(c * r == total ? "MATCHES" : "DOES NOT MATCH")}");
                else
                    Log.Line("  No entry in .dat files - possible layouts (columns x rows): " +
                             string.Join(", ", Enumerable.Range(1, (int)Math.Max(1, total)).Where(c => total % c == 0).Select(c => $"{c}x{total / c}")));
            }
            return 0;
        }
    }
}

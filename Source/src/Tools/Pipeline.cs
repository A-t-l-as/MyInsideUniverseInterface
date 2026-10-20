// Export / import of everything in the Work folder (the former EXPORT_ALL_INTERFACES.py and
// IMPORT_ALL_INTERFACES.py). Everything runs in this process; only TexEnv.exe (the .tex
// texture tool, a separate program) is started for .tex files, through a temporary script file.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace InterfaceTools
{
    /// <summary>Runs TexEnv.exe with a script ("socket") file.</summary>
    public static class TexEnv
    {
        public const string ExeName = "TexEnv.exe";

        public static string ExePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ExeName);
        public static bool Available => File.Exists(ExePath);

        public class Result
        {
            public bool Started, TimedOut;
            public string Stdout = "", Stderr = "";
            public List<string> Lines => Stdout.Replace("\r\n", "\n").Split('\n').ToList();
            public bool HasError =>
                !Started || TimedOut ||
                Stdout.Replace("\r\n", "\n").Split('\n').Concat(Stderr.Replace("\r\n", "\n").Split('\n')).Any(l => l.Trim().StartsWith("Error: "));
            /// <summary>"Error:" anywhere in the output (the import checked it this way).</summary>
            public bool HasErrorAnywhere => !Started || TimedOut || (Stdout + Stderr).Contains("Error:");
        }

        /// <summary>Writes the commands into a temporary script, runs "TexEnv.exe -s script" in the program
        /// folder and returns its output. The script is deleted afterwards.</summary>
        public static Result Run(IEnumerable<string> commands, int timeoutSeconds = 300)
        {
            var res = new Result();
            if (!Available) { res.Stderr = $"Error: {ExeName} not found in {AppDomain.CurrentDomain.BaseDirectory}"; return res; }
            string script = Path.Combine(Path.GetTempPath(), $"InterfaceTools_TexEnv_{Process.GetCurrentProcess().Id}_{Thread.CurrentThread.ManagedThreadId}_{Guid.NewGuid():N}.txt");
            try
            {
                // system ANSI code page and CRLF, as the Python scripts wrote it on Windows
                File.WriteAllText(script, string.Join("\r\n", commands) + "\r\n", Encoding.Default);
                var psi = new ProcessStartInfo(ExePath, $"-s \"{script}\"")
                {
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                };
                using (var p = new Process { StartInfo = psi })
                {
                    var so = new StringBuilder(); var se = new StringBuilder();
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (so) so.Append(e.Data).Append('\n'); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (se) se.Append(e.Data).Append('\n'); };
                    try { p.Start(); }
                    catch (Exception e) { res.Stderr = $"Error: failed to start {ExeName}: {e.Message}"; return res; }
                    res.Started = true;
                    p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutSeconds * 1000))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        res.TimedOut = true;
                        res.Stderr = $"Error: timed out ({timeoutSeconds} s)\n";
                    }
                    p.WaitForExit();
                    lock (so) res.Stdout = so.ToString();
                    lock (se) res.Stderr += se.ToString();
                }
            }
            finally { try { File.Delete(script); } catch (Exception) { } }
            return res;
        }
    }

    public class PipelineOptions
    {
        public string Root;
        public bool DryRun, NoTex, NoMap, NoDds, NoInt;
        public string Mode = "text";
        public int TexTimeout = 300;
        public Func<bool> Cancelled = () => false;
    }

    public static class Pipeline
    {
        /// <summary>A path in a TexEnv script, in quotes, so that TexEnv reads it whole (spaces included).</summary>
        static string Q(string path) => "\"" + path + "\"";

        public static string DefaultWorkDir => Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Work"));

        static void Header(string t, char c = '=', int n = 50) { Log.Line(""); Log.Line(new string(c, n)); Log.Line(t); Log.Line(new string(c, n)); }

        public const string MissingDatsFolderName = "MISSING_DATS_FOR_OVERLAP";
        /// <summary>Bin\MISSING_DATS_FOR_OVERLAP: the user's own text .dat files for textures that no
        /// .dat of the game names (edge cases). Searched with its subfolders for every folder.</summary>
        public static string MissingDatsDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MissingDatsFolderName);

        /// <summary>Textures named in the text .dat files (dialog and background definitions) of this folder and
        /// its subfolders - the same files the tiles per row are taken from - and in the text .dat files of
        /// Bin\MISSING_DATS_FOR_OVERLAP. They are exported / imported with the Overlap variants of the tileset
        /// commands. Other folders of the Work folder are not searched: they may hold the interface of another game.</summary>
        static HashSet<string> OverlapTextures(string dir)
        {
            var found = Tiles.TextDatTextures(dir);
            string Few(IEnumerable<string> v) { var l = v.ToList(); return l.Count > 0 ? ": " + string.Join(", ", l.Take(12)) + (l.Count > 12 ? ", ..." : "") : ""; }
            Log.Line($"[+] {found.Count} texture(s) named in the text .dat files of this folder - exported / imported with the Overlap tileset commands{Few(found.Values)}");
            var keys = new HashSet<string>(found.Keys);
            if (Directory.Exists(MissingDatsDir))
            {
                var extra = Tiles.TextDatTextures(MissingDatsDir);
                var added = extra.Where(kv => keys.Add(kv.Key)).Select(kv => kv.Value).ToList();
                Log.Line($"[+] {extra.Count} texture(s) named in {MissingDatsFolderName} ({added.Count} not named above){Few(added)}");
            }
            return keys;
        }

        static List<string> Glob(string dir, string ext) =>
            Directory.GetFiles(dir).Where(f => string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();

        static List<string> SubdirsRecursive(string root) =>
            Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();

        // ====================================================================== export
        /// <summary>Exports every folder below the Work folder: .tex (TexEnv), .dat (decompiler),
        /// .int (AddFileInfo), and finally all .dds files (DDS tool).</summary>
        public static int ExportAll(PipelineOptions o)
        {
            string root = o.Root ?? DefaultWorkDir;
            if (!Directory.Exists(root)) { Log.Line($"Error: Directory {root} does not exist!"); return 2; }
            var dirs = SubdirsRecursive(root);
            if (dirs.Count == 0) { Log.Line($"No subdirectories found in {root}."); }
            else Log.Line($"Found {dirs.Count} subdirectories in {root}.\n");
            int failures = 0;
            for (int i = 0; i < dirs.Count; i++)
            {
                if (o.Cancelled()) { Log.Line("Cancelled."); return 1; }
                Log.Line(""); Log.Line(new string('#', 70));
                Log.Line($"# [{i + 1}/{dirs.Count}] DIRECTORY: {dirs[i]}");
                Log.Line(new string('#', 70)); Log.Line("");
                try { failures += ExportDirectory(dirs[i], o); }
                catch (Exception e) when (!(e is OutOfMemoryException)) { failures++; Log.Line($"[-] Unexpected error in directory {dirs[i]}: {e.Message}"); }
            }
            if (!o.NoDds && !o.Cancelled())
            {
                Header($"DDS EXPORT ({root}, recursive)", '=', 70);
                if (!o.DryRun && Dds.Run(new[] { "export", root, "-r" }) != 0) { failures++; Log.Line("[-] Some .dds files could not be exported (see above)."); }
                else Log.Line("[+] DDS export finished.");
            }
            Log.Line("");
            Log.Line(failures == 0 ? "Export finished successfully." : $"Export finished with {failures} problem(s) - see the log above.");
            return failures == 0 ? 0 : 1;
        }

        static int ExportDirectory(string dir, PipelineOptions o)
        {
            int failures = 0;
            var tex = Glob(dir, ".tex"); var dat = Glob(dir, ".dat"); var ints = Glob(dir, ".int");
            if (tex.Count == 0 && dat.Count == 0 && ints.Count == 0)
            {
                Log.Line($"Nothing to process in {dir} (no .tex, .dat or .int files). Skipping.");
                return 0;
            }
            Log.Line($"Found: {tex.Count} .tex | {dat.Count} .dat | {ints.Count} .int\n");

            // 1. best tileset widths of the .tex files (width hints for the decompiler)
            var widthHints = new List<string>();
            bool texOk = !o.NoTex && TexEnv.Available;
            if (tex.Count > 0 && !o.NoTex && !TexEnv.Available) Log.Line($"[-] {TexEnv.ExeName} not found next to the program - .tex files are skipped.");
            if (tex.Count == 0) Log.Line($"[-] No .tex files found in directory {dir}.");
            else if (texOk)
                foreach (var t in tex)
                {
                    if (o.DryRun) { Log.Line($"    > TexEnv: loadFrom {Q(t)} / getBestWidthFromInterfaceTileset"); continue; }
                    var r = TexEnv.Run(new[] { $"loadFrom {Q(t)}", "getBestWidthFromInterfaceTileset", "quit" }, o.TexTimeout);
                    var lines = r.Lines;
                    if (lines.Count >= 2 && lines[1].Trim().Length > 0)
                    {
                        string hint = lines[1].Trim();
                        widthHints.Add(hint);
                        Log.Line($"[+] Processed '{Path.GetFileName(t)}': {hint}");
                    }
                    else Log.Line($"[-] Warning: Not enough output lines for file '{Path.GetFileName(t)}'");
                }

            // 2. decompile the .dat files (tile layout of the bitmaps goes straight to step 4)
            int? width = null;
            var widths = new Dictionary<string, int>();
            foreach (var hint in widthHints)
            {
                int eq = hint.LastIndexOf('=');
                string n = eq >= 0 ? hint.Substring(0, eq) : "", v = eq >= 0 ? hint.Substring(eq + 1) : hint;
                if (!int.TryParse(v.Trim(), out int px) || px <= 0) { Log.Line($"[-] Ignoring the width hint '{hint}' (expected pixels or name=pixels)"); continue; }
                if (eq >= 0 && n.Length > 0) widths[n.Trim().ToLowerInvariant()] = px; else width = px;
            }
            var tileList = new List<(string file, int tiles, string source)>();
            if (dat.Count == 0) Log.Line($"\n[-] No .dat files found in directory {dir}.");
            else
            {
                Header($"DECOMPILING {dat.Count} .DAT FILES...");
                foreach (var d in dat)
                {
                    if (o.Cancelled()) return failures;
                    Log.Line($"\n---> Processing file: {Path.GetFileName(d)}");
                    if (o.DryRun) continue;
                    try
                    {
                        var res = Decompiler.Decompile(d, dir, new Dictionary<string, int>(), width, null, null, widths);
                        foreach (var b in res.Bitmaps.Where(b => b.Tile != 0))
                        {
                            string name = Path.GetFileName(b.Path).Replace("@", "");
                            tileList.Add((Path.ChangeExtension(name, ".tex"), b.TilesPerRow, "decompiler"));
                        }
                        if (!res.Ok) { failures++; Log.Line($"[-] {Path.GetFileName(d)}: the decompiled files do not compile back to an identical .dat (see above)."); }
                    }
                    catch (Exception e) when (e is ToolError || e is EndOfStreamException || e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
                    {
                        Log.Line($"[-] {Path.GetFileName(d)} is not an interface layout file or cannot be read: {e.Message}");
                    }
                }
            }

            // 3. tiles per row from the text .dat files (KnightShift dialog definitions)
            if (tex.Count > 0)
            {
                Header("COUNTING TILES PER ROW...");
                var map = Tiles.TilesPerRow(dir, Log.Line);
                foreach (var kv in map) tileList.Add((kv.Key, kv.Value, "dialog definitions"));
                Log.Line($"[+] {map.Count} textures found in the dialog definitions.");
            }

            if (tileList.Count > 0)
            {
                Header("COLLECTED LIST OF TEXTURES AND TILES PER ROW:");
                foreach (var e in tileList) Log.Line($"- File: {e.file} | Tiles per row: {e.tiles} [Source: {e.source}]");
            }

            // 4. export the .tex textures with TexEnv
            if (tex.Count > 0 && texOk)
            {
                Header("PROCESSING TEX FILES WITH TEXENV...");
                var tilesMap = new Dictionary<string, int>();
                foreach (var e in tileList) tilesMap[e.file.ToLowerInvariant()] = e.tiles;   // later entries win
                var overlap = OverlapTextures(dir);
                foreach (var t in tex)
                {
                    if (o.Cancelled()) return failures;
                    string key = Path.GetFileName(t).Replace("@", "").ToLowerInvariant();
                    string png = Path.ChangeExtension(t, ".png");
                    int n = tilesMap.TryGetValue(key, out int v) ? v : 0;
                    string cmd = overlap.Contains(key) ? "exportTilesetFromInterfaceOverlap" : "exportTilesetFromInterface";
                    Log.Line((n > 0 ? $"---> {Path.GetFileName(t)} ({n} tiles/row)" : $"---> {Path.GetFileName(t)} not in the tile list (0 tiles/row)") + $": {cmd}...");
                    if (o.DryRun) continue;
                    var r = TexEnv.Run(new[] { $"loadFrom {Q(t)}", $"{cmd} {n} {Q(png)}", "quit" }, o.TexTimeout);
                    if (r.HasError)
                    {
                        Log.Line($"[-] TexEnv returned an error for '{Path.GetFileName(t)}'. Trying exportTexToImg...");
                        var r2 = TexEnv.Run(new[] { $"loadFrom {Q(t)}", $"exportTexToImg 0 {Q(png)}", "quit" }, o.TexTimeout);
                        if (r2.HasError)
                        {
                            failures++;
                            Log.Line($"[-] '{Path.GetFileName(t)}' could not be exported:");
                            foreach (var l in (r2.Stdout + r2.Stderr).Split('\n').Where(l => l.Contains("Error:"))) Log.Line("      " + l.Trim());
                        }
                        else Log.Line($"[+] exportTexToImg: '{Path.GetFileName(t)}' -> '{Path.GetFileName(png)}'");
                    }
                    else Log.Line($"[+] '{Path.GetFileName(t)}' -> '{Path.GetFileName(png)}'");
                }
            }

            // 5. .int files: header -> .addfileinfo, content -> .def
            if (ints.Count == 0) Log.Line($"\n[-] No .int files found in directory {dir}.");
            else if (!o.NoInt)
            {
                Header($"EXTRACTING {ints.Count} .INT FILES...");
                foreach (var f in ints)
                {
                    string def = Path.ChangeExtension(f, ".def"), info = Path.ChangeExtension(f, ".addfileinfo");
                    Log.Line($"---> Processing file: {Path.GetFileName(f)}");
                    if (o.DryRun) continue;
                    try
                    {
                        var h = AddFileInfo.Decode(File.ReadAllBytes(f), out byte[] content, true);
                        File.WriteAllBytes(def, content);
                        File.WriteAllText(info, AddFileInfo.InfoText(h), Text.Utf8NoBom);
                        Log.Line($"    name: {h.NameText}  attrib: {h.AttribText}  guid: {h.GuidString}");
                        Log.Line($"[+] Written: {Path.GetFileName(def)} + {Path.GetFileName(info)}");
                    }
                    catch (Exception e) when (e is ToolError || e is IOException || e is UnauthorizedAccessException)
                    {
                        failures++;
                        Log.Line($"[-] Error while processing {Path.GetFileName(f)}: {e.Message}");
                    }
                }
            }
            return failures;
        }

        // ====================================================================== import
        const string FormatFileName = "format_info.format";

        /// <summary>Format of a definition file: the "// Format:" line in the header of the .txt file,
        /// otherwise format_info.format of the folder (written by the old Python export).</summary>
        static string FormatFor(string txt, out bool fromHeader)
        {
            string hf = Decompiler.FormatFromTxt(txt);
            fromHeader = hf != null;
            if (hf != null) return hf;
            string ff = Path.Combine(Path.GetDirectoryName(txt), FormatFileName);
            if (File.Exists(ff))
                foreach (var line in File.ReadAllText(ff).Split('\n'))
                {
                    string l = line.Trim().TrimStart('-');
                    if (l.Length == 0) continue;
                    string f = Fmt.FromSwitch(l);
                    if (f != null) return f;
                }
            return null;
        }

        public static int ImportAll(PipelineOptions o)
        {
            string root = o.Root ?? DefaultWorkDir;
            if (!Directory.Exists(root)) { Log.Line($"Error: {root} is not a directory."); return 2; }
            root = Path.GetFullPath(root);
            bool HasWork(string d) => new[] { "*.def", "*.tex", "*.txt" }.Any(p => Directory.GetFiles(d, p).Length > 0);
            var dirs = new[] { root }.Concat(SubdirsRecursive(root)).Where(HasWork).ToList();
            if (dirs.Count == 0) Log.Line($"No .def/.tex/.txt files in {root} or its subdirectories.");
            int ok = 0;
            var failed = new List<string>(); var skipped = new List<string>();
            string parent = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar)) ?? root;
            string Rel(string p) => p.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ? p.Substring(parent.Length).TrimStart(Path.DirectorySeparatorChar) : p;
            foreach (var d in dirs)
            {
                if (o.Cancelled()) { Log.Line("Cancelled."); return 1; }
                Log.Line(""); Log.Line(new string('#', 60)); Log.Line($"# {Rel(d)}"); Log.Line(new string('#', 60));

                // 1. .def + .addfileinfo -> .int
                var defs = Glob(d, ".def");
                if (defs.Count > 0 && !o.NoInt)
                {
                    Log.Line("\n  -- compilation (.def -> .int) --");
                    foreach (var def in defs)
                    {
                        Log.Line($"  [{Path.GetFileName(def)}]");
                        string infoFile = Path.ChangeExtension(def, ".addfileinfo");
                        if (!File.Exists(infoFile)) { Log.Line($"    ! missing {Path.GetFileName(infoFile)} - skipping"); skipped.Add(def); continue; }
                        try
                        {
                            var h = AddFileInfo.HeaderFromInfo(AddFileInfo.ParseInfo(infoFile), o.Mode, out bool textMode);
                            string dest = Path.ChangeExtension(def, ".int");
                            Log.Line($"    > {(textMode ? "text" : "binary")}, name {h.NameText}, attrib {h.AttribText}, guid {h.GuidString}");
                            if (!o.DryRun) File.WriteAllBytes(dest, AddFileInfo.Encode(File.ReadAllBytes(def), h, textMode));
                            Log.Line($"    OK -> {Path.GetFileName(dest)}");
                            ok++;
                        }
                        catch (Exception e) when (e is ToolError || e is IOException || e is UnauthorizedAccessException)
                        {
                            Log.Line($"    ! {Path.GetFileName(infoFile)}: {e.Message}");
                            failed.Add(def);
                        }
                    }
                }

                // 2. .tex + .png -> .tex (TexEnv)
                var texs = Glob(d, ".tex");
                if (texs.Count > 0 && !o.NoTex)
                {
                    Log.Line("\n  -- textures (.tex) --");
                    var overlap = OverlapTextures(d);
                    if (!TexEnv.Available && !o.DryRun) { Log.Line($"  ! {TexEnv.ExePath} not found"); failed.AddRange(texs); }
                    else
                        foreach (var t in texs)
                        {
                            string tileCmd = overlap.Contains(Path.GetFileName(t).Replace("@", "").ToLowerInvariant()) ? "importTilesetToInterfaceOverlap" : "importTilesetToInterface";
                            if (o.Cancelled()) return 1;
                            string png = Path.ChangeExtension(t, ".png");
                            Log.Line($"  [{Path.GetFileName(t)}]");
                            if (!File.Exists(png)) { Log.Line($"    ! missing {Path.GetFileName(png)} - skipping"); skipped.Add(t); continue; }
                            bool done = false;
                            int attempt = 0;
                            foreach (var (label, line) in new[] { (tileCmd, $"{tileCmd} {Q(png)}"), ("importImgToTex 0", $"importImgToTex 0 {Q(png)}") })
                            {
                                attempt++;
                                Log.Line($"    > attempt {attempt}: {label}");
                                var cmds = new[] { $"loadFrom {Q(t)}", line, $"saveTo {Q(t)}", "quit" };
                                if (o.DryRun) { foreach (var c in cmds) Log.Line($"        {c}"); done = true; break; }
                                var r = TexEnv.Run(cmds, o.TexTimeout);
                                if (!r.HasErrorAnywhere) { Log.Line("      OK"); done = true; break; }
                                foreach (var l in (r.Stdout + r.Stderr).Split('\n').Where(l => l.Contains("Error:"))) Log.Line("      " + l.Trim());
                            }
                            if (done) ok++; else { Log.Line("    ! all variants failed"); failed.Add(t); }
                        }
                }

                // 3. .txt -> .dat (compiler)
                var txts = Glob(d, ".txt").Where(f => !string.Equals(Path.GetFileName(f), "Socket.txt", StringComparison.OrdinalIgnoreCase)).ToList();
                if (txts.Count > 0 && !o.NoMap)
                {
                    Log.Line("\n  -- maps (.txt -> .dat) --");
                    foreach (var txt in txts)
                    {
                        if (o.Cancelled()) return 1;
                        string fmt = FormatFor(txt, out bool fromHeader);
                        Log.Line($"  [{Path.GetFileName(txt)}]");
                        if (fmt == null) { Log.Line("    - not an interface definition file (no format line in its header) - skipping"); skipped.Add(txt); continue; }
                        string dest = Path.ChangeExtension(txt, ".dat");
                        Log.Line($"    > compile --{fmt} {Path.GetFileName(txt)} {Path.GetFileName(dest)}");
                        if (o.DryRun) { ok++; continue; }
                        var log = new StringBuilder();
                        try
                        {
                            byte[] data;
                            using (Log.Capture(log)) data = Compiler.Build(txt, false, fmt, d);
                            File.WriteAllBytes(dest, data);
                            foreach (var l in log.ToString().Split('\n').Where(l => l.StartsWith("WARNING: bitmap") || l.StartsWith("Note:"))) Log.Line("      " + l);
                            Log.Line($"    OK -> {Path.GetFileName(dest)}");
                            ok++;
                        }
                        catch (Exception e) when (e is DefinitionError || e is BitmapError || e is IOException || e is UnauthorizedAccessException)
                        {
                            Log.Line("    ! error");
                            foreach (var l in log.ToString().Split('\n').Where(l => l.Length > 0)) Log.Line("      " + l);
                            foreach (var l in ((e is DefinitionError ? "Error in the definition file: " : "Error: ") + e.Message).Split('\n')) Log.Line("      " + l);
                            if (fromHeader) failed.Add(txt);
                            else { Log.Line($"    (the file has no format line in its header - maybe it is not an interface definition file)"); skipped.Add(txt); }
                        }
                    }
                }
            }
            Log.Line(""); Log.Line(new string('=', 60));
            Log.Line($"Directories: {dirs.Count}   OK: {ok}   Failed: {failed.Count}   Skipped: {skipped.Count}");
            foreach (var n in failed) Log.Line($"  failed:  {Rel(n)}");
            foreach (var n in skipped) Log.Line($"  skipped: {Rel(n)}");
            bool ddsOk = true;
            if (!o.NoDds && !o.Cancelled())
            {
                Log.Line(""); Log.Line(new string('#', 60)); Log.Line("# DDS import"); Log.Line(new string('#', 60));
                if (!o.DryRun) ddsOk = Dds.Run(new[] { "import", root, "-r" }) == 0;
                Log.Line(ddsOk ? "  OK" : "  ! some .dds files could not be created (see above)");
            }
            return failed.Count > 0 || !ddsOk ? 1 : 0;
        }

        // ====================================================================== commands
        static void Pause(IList<string> argv)
        {
            if (argv.Contains("--no-pause")) return;
            try
            {
                if (Console.IsInputRedirected) return;
                Console.WriteLine();
                Console.WriteLine("Press Enter to exit...");
                Console.ReadLine();
            }
            catch (Exception) { }
        }

        static PipelineOptions ParseOptions(IList<string> argv, out string error)
        {
            var o = new PipelineOptions();
            error = null;
            for (int i = 0; i < argv.Count; i++)
            {
                string a = argv[i];
                switch (a)
                {
                    case "--dry-run": o.DryRun = true; break;
                    case "--no-tex": o.NoTex = true; break;
                    case "--no-map": o.NoMap = true; break;
                    case "--no-dds": o.NoDds = true; break;
                    case "--no-int": o.NoInt = true; break;
                    case "--no-pause": break;
                    case "--mode":
                        if (i + 1 < argv.Count && (argv[i + 1] == "text" || argv[i + 1] == "binary")) o.Mode = argv[++i];
                        else error = "--mode needs text or binary";
                        break;
                    case "--tex-timeout":
                        if (i + 1 < argv.Count && int.TryParse(argv[i + 1], out int t)) { o.TexTimeout = t; i++; } else error = "--tex-timeout needs a number of seconds";
                        break;
                    case "--tool": i++; break;   // accepted for compatibility (AddFileInfo is built in)
                    default:
                        if (a.StartsWith("--")) error = $"unknown option {a}";
                        else o.Root = Path.GetFullPath(a.Trim('"'));
                        break;
                }
            }
            return o;
        }

        const string ExportUsage =
            "Usage: InterfaceTools export-all [WorkFolder] [--no-tex] [--no-dds] [--no-int] [--dry-run] [--tex-timeout S] [--no-pause]\n" +
            "  Exports every subfolder of the Work folder (default: ..\\Work next to the program):\n" +
            "  .dat -> .txt + bitmaps, .tex -> .png (TexEnv.exe), .int -> .def + .addfileinfo, .dds -> .png + .json";
        const string ImportUsage =
            "Usage: InterfaceTools import-all [WorkFolder] [--mode text|binary] [--no-tex] [--no-map] [--no-dds] [--no-int]\n" +
            "                                 [--dry-run] [--tex-timeout S] [--no-pause]\n" +
            "  Packs everything exported by export-all back: .def -> .int, .png -> .tex (TexEnv.exe),\n" +
            "  .txt -> .dat (compiler), .json + .png -> .dds";

        public static int RunExport(IList<string> argv)
        {
            if (argv.Contains("-h") || argv.Contains("--help")) { Log.Line(ExportUsage); return 0; }
            var o = ParseOptions(argv, out string err);
            if (err != null) { Log.Line(err); Log.Line(ExportUsage); return 2; }
            int r = 1;
            try { r = ExportAll(o); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { Log.Line($"Unexpected error: {e}"); }
            Pause(argv);
            return r;
        }

        public static int RunImport(IList<string> argv)
        {
            if (argv.Contains("-h") || argv.Contains("--help")) { Log.Line(ImportUsage); return 0; }
            var o = ParseOptions(argv, out string err);
            if (err != null) { Log.Line(err); Log.Line(ImportUsage); return 2; }
            int r = 1;
            try { r = ImportAll(o); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { Log.Line($"Unexpected error: {e}"); }
            Pause(argv);
            return r;
        }
    }

    public static partial class Extra
    {
        static partial void RegisterPipeline()
        {
            Register("export-all", Pipeline.RunExport);
            Register("import-all", Pipeline.RunImport);
            Register("addfileinfo", AddFileInfo.Run);
            Register("tiles", Tiles.Run);
        }
    }
}

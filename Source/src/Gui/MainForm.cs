// Main window of the editor: menu, tree of the file, panels for the header, materials, sounds,
// fonts, CustomData, DialogTemplates and items. The state panel is in StatePanel.cs.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace InterfaceTools.Gui
{
    using Font = System.Drawing.Font;
    using TextUtil = InterfaceTools.Text;

    public partial class MainForm : Form
    {
        public readonly Settings Settings = new Settings();
        public DatFile D;
        public string FilePath;
        public bool Dirty;
        readonly List<byte[]> undo = new List<byte[]>(), redo = new List<byte[]>();
        Dictionary<long, string> resNames = new Dictionary<long, string>();
        Dictionary<string, long> resValues = new Dictionary<string, long>();
        public string ResPath;
        readonly Dictionary<string, (Texture tex, string path)> texCache = new Dictionary<string, (Texture, string)>();
        readonly Dictionary<string, Bitmap> bmpCache = new Dictionary<string, Bitmap>();
        public string CurrentNode;
        string sourceName = "interface";
        string sourceTxt;
        public readonly NameDb Names = new NameDb();
        string namesVariant = "";
        public bool ShowNames = true;
        public bool PreferMaps;

        readonly TreeView tree;
        readonly Panel content;
        readonly ToolStripStatusLabel status;
        readonly TextBox gotoBox;
        readonly Dictionary<string, TreeNode> nodes = new Dictionary<string, TreeNode>();
        ToolStripMenuItem showNamesItem, preferMapsItem;
        readonly List<ToolStripMenuItem> colourOrderItems = new List<ToolStripMenuItem>();
        bool rebuildPending, suppressSelect;

        public MainForm(string path)
        {
            Text = G.AppName;
            Size = new Size(1400, 860);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            Controls.Add(content = new Panel { Dock = DockStyle.Fill, AutoScroll = true });
            var split = new Splitter { Dock = DockStyle.Left, Width = 5 };
            var left = new Panel { Dock = DockStyle.Left, Width = 330 };
            tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true, ShowLines = true };
            tree.AfterSelect += (s, e) => { if (!suppressSelect && e.Node != null && (string)e.Node.Tag != CurrentNode) ShowNode((string)e.Node.Tag); };
            gotoBox = new TextBox { Width = 70 };
            gotoBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; GotoItem(); } };
            var gotoRow = G.Row(G.Lbl("Go to item:"), gotoBox, G.Btn("Go", GotoItem));
            left.Controls.Add(tree);
            left.Controls.Add(gotoRow);
            Controls.Add(split);
            Controls.Add(left);
            var ss = new StatusStrip();
            ss.Items.Add(status = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft });
            Controls.Add(ss);
            var menu = BuildMenu();
            MainMenuStrip = menu;
            Controls.Add(menu);
            FormClosing += (s, e) => { if (!ConfirmDiscard()) e.Cancel = true; else Settings.Save(); };
            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0 && ConfirmDiscard())
                {
                    if (f[0].EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) OpenTxt(f[0]); else OpenDat(f[0]);
                }
            };
            if (path != null) Shown += (s, e) => BeginInvoke((Action)(() =>
            {
                if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) OpenTxt(path); else OpenDat(path);
            }));
            else ShowWelcome();
        }

        public static int Run(string file)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(file));
            return 0;
        }

        // ================================================================== menu
        MenuStrip BuildMenu()
        {
            var ms = new MenuStrip();
            ToolStripMenuItem M(string text, params ToolStripItem[] items) { var m = new ToolStripMenuItem(text); m.DropDownItems.AddRange(items); return m; }
            ToolStripMenuItem I(string text, Action a, Keys keys = Keys.None) { var m = new ToolStripMenuItem(text, null, (s, e) => a()); if (keys != Keys.None) m.ShortcutKeys = keys; return m; }
            ToolStripSeparator S() => new ToolStripSeparator();

            ms.Items.Add(M("&File",
                I("Open .dat...", CmdOpen, Keys.Control | Keys.O),
                I("Open .txt (compile)...", CmdOpenTxt),
                S(),
                I("Save", () => CmdSave(), Keys.Control | Keys.S),
                I("Save as...", () => CmdSaveAs()),
                I("Export as .txt project...", CmdExportTxt),
                S(),
                I("Load identifier names (resource.h)...", CmdLoadResources),
                I("Load item/state names from .txt...", CmdLoadNames),
                I("Use built-in item/state names", CmdClearNames),
                S(),
                I("Exit", Close)));
            ms.Items.Add(M("&Edit",
                I("Undo", CmdUndo, Keys.Control | Keys.Z),
                I("Redo", CmdRedo, Keys.Control | Keys.Y),
                S(),
                I("Add item", CmdAddItem), I("Duplicate item", CmdDupItem), I("Delete item", CmdDelItem),
                S(),
                I("Add state", CmdAddState), I("Duplicate state", CmdDupState), I("Delete state", CmdDelState)));
            preferMapsItem = I("Show colour maps (name@.bmp) instead of textures", () => { PreferMaps = !PreferMaps; preferMapsItem.Checked = PreferMaps; CmdReloadTextures(); });
            showNamesItem = I("Show item and state names", () => { ShowNames = !ShowNames; showNamesItem.Checked = ShowNames; RefreshView(true); });
            showNamesItem.Checked = true;
            var co = new ToolStripMenuItem("Colour byte order");
            foreach (var (val, lab) in new[] { ("auto", "Automatic (Earth 2150, KnightShift: AABBGGRR; later games: AARRGGBB)"), ("ARGB", "AARRGGBB"), ("ABGR", "AABBGGRR (blue = 0xFFFF0000)") })
            {
                var it = new ToolStripMenuItem(lab) { Tag = val, Checked = Settings.ColourOrder == val };
                it.Click += (s, e) => SetColourOrder(val);
                colourOrderItems.Add(it);
                co.DropDownItems.Add(it);
            }
            ms.Items.Add(M("&View",
                I("Texture folders...", CmdTextureDirs),
                I("Reload textures", CmdReloadTextures),
                preferMapsItem,
                I("Check file", CmdCheck),
                showNamesItem,
                S(),
                co));
            ms.Items.Add(M("&Tools",
                I("Export all (Work folder)...", () => PipelineDialog.Show(this, true)),
                I("Import all (Work folder)...", () => PipelineDialog.Show(this, false))));
            ms.Items.Add(M("&Help",
                I("About", () => G.Info(this, $"{G.AppName} (InterfaceTools {Program.Version})\nEditor for InterfaceMap .dat files\n" +
                    "(Earth 2150, KnightShift, Earth 2160, TwoWorlds, Two Worlds II).\n\nThe Earth 2150 format is also used by " + Fmt.E2150Family + ".\n\nFile format: see FORMAT.md"))));
            return ms;
        }

        public void SetStatus(string text) => status.Text = text;

        void UpdateTitle()
        {
            string name = FilePath != null ? Path.GetFileName(FilePath) : "(unsaved)";
            string fmt = D != null ? Fmt.Names[D.Fmt] : "";
            Text = $"{G.AppName} - {name} [{fmt}]{(Dirty ? " *" : "")}";
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        const int WM_SETREDRAW = 0x000B;
        static readonly bool IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

        /// <summary>Stops / restarts the painting of the content area, so a new panel appears at once
        /// instead of control by control.</summary>
        void Redraw(bool on)
        {
            if (!IsWindows || !content.IsHandleCreated) return;
            try
            {
                SendMessage(content.Handle, WM_SETREDRAW, on ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
                if (on) content.Invalidate(true);
            }
            catch (Exception) { }
        }

        void ClearContent()
        {
            var old = content.Controls.Cast<Control>().ToList();
            content.Controls.Clear();
            DisposeLater(old);
            content.AutoScrollMinSize = Size.Empty;
            content.AutoScrollPosition = Point.Empty;
        }

        /// <summary>The old panel is destroyed after the new one is shown (destroying hundreds of
        /// windows takes a moment, and the user should not wait for it).</summary>
        void DisposeLater(List<Control> old)
        {
            if (old.Count == 0) return;
            foreach (var c in old) c.Visible = false;
            if (!IsHandleCreated) { foreach (var c in old) c.Dispose(); return; }
            BeginInvoke((Action)(() => { foreach (var c in old) c.Dispose(); }));
        }

        void SetContent(Control c)
        {
            Redraw(false);
            try
            {
                ClearContent();
                c.Dock = DockStyle.Fill;
                Lazy.ResumeAll(c);
                content.SuspendLayout();
                content.Controls.Add(c);
                content.ResumeLayout(true);
                var pref = c.GetPreferredSize(new Size(content.ClientSize.Width, 0));
                content.AutoScrollMinSize = new Size(Math.Min(pref.Width, 1200), pref.Height);
            }
            finally { Redraw(true); }
        }

        void ShowWelcome()
        {
            var v = new VStack { Padding = new Padding(30) };
            v.Add(G.Lbl(G.AppName, null, true, 16));
            v.Add(G.Lbl("Open a .dat file (File -> Open .dat) or a .txt definition file\n(File -> Open .txt) to start editing.\n\n" +
                        "Tools -> Export all / Import all process the whole Work folder (like EXPORT_ALL_INTERFACES.bat)."));
            v.Add(G.Row(G.Btn("Open .dat...", CmdOpen), G.Btn("Open .txt...", CmdOpenTxt)));
            SetContent(v);
        }

        // ================================================================== document
        public byte[] Snapshot() => D.Write();

        /// <summary>Applies an edit with undo support and validation (the file must stay writable).</summary>
        public bool Modify(Action fn, bool tree = false, bool refresh = false)
        {
            byte[] before = Snapshot();
            try
            {
                fn();
                D.Write();
            }
            catch (Exception e) when (G.IsUserError(e))
            {
                D = DatFile.Read(before, D.Fmt);
                G.Error(this, "Invalid change", e.Message);
                RefreshView(true);
                return false;
            }
            undo.Add(before);
            if (undo.Count > 100) undo.RemoveRange(0, undo.Count - 100);
            redo.Clear();
            Dirty = true;
            UpdateTitle();
            if (tree || refresh) RefreshView(tree);
            return true;
        }

        /// <summary>Rebuilds the tree (optionally) and the current panel. Deferred, so it is safe to call
        /// from an event handler of a control that is replaced by the rebuild.</summary>
        public void RefreshView(bool tree = true, string node = null)
        {
            if (node != null) CurrentNode = node;
            if (tree) BuildTree();
            ScheduleRebuild();
        }

        void ScheduleRebuild()
        {
            if (rebuildPending || !IsHandleCreated) { if (!IsHandleCreated) RebuildNow(); return; }
            rebuildPending = true;
            BeginInvoke((Action)RebuildNow);
        }

        void RebuildNow()
        {
            rebuildPending = false;
            if (D == null) return;
            string node = CurrentNode != null && nodes.ContainsKey(CurrentNode) ? CurrentNode : "hdr";
            SelectInTree(node);
            BuildPanel(node);
        }

        bool ConfirmDiscard()
        {
            if (!Dirty) return true;
            var r = MessageBox.Show(this, "Save changes to the current file?", G.AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return CmdSave();
            return true;
        }

        public void OpenDat(string path)
        {
            sourceTxt = null;
            byte[] data;
            DatFile d;
            try
            {
                data = File.ReadAllBytes(path);
                d = DatFile.Read(data);
            }
            catch (Exception e) when (G.IsUserError(e) || e is IOException || e is UnauthorizedAccessException || e is EndOfStreamException)
            {
                G.Error(this, "Cannot open", $"{path}\n\n{e.Message}");
                return;
            }
            SetDocument(d, path);
            bool same;
            try { same = d.Write().SequenceEqual(data); } catch (Exception) { same = false; }
            if (!same) G.Warn(this, "This file cannot be written back byte for byte (see View -> Check file). Saving will normalize it.");
        }

        void SetDocument(DatFile d, string path)
        {
            D = d; FilePath = path;
            Dirty = false;
            undo.Clear(); redo.Clear();
            ClearTextures();
            CurrentNode = null;
            if (path != null)
            {
                Settings.LastDir = Path.GetDirectoryName(Path.GetFullPath(path));
                Settings.Save();
            }
            AutoLoadResources();
            Names.ClearUserNames();
            namesVariant = Names.SelectFor(d.Fmt, d.Items.Keys);
            if (path != null && Settings.NamesFiles.TryGetValue(Path.GetFullPath(path), out string nf) && File.Exists(nf))
            {
                try { Names.LoadUserNames(nf); } catch (IOException) { }
            }
            UpdateTitle();
            BuildTree();
            CurrentNode = "hdr";
            RebuildNow();
            SetStatus($"Loaded {path ?? "compiled text file"}: {d.Items.Count} items, {d.Items.Values.Sum(i => i.States.Count)} states");
            TextureHint();
        }

        // ================================================================== File menu
        string AskOpen(string title, string filter)
        {
            using (var dlg = new OpenFileDialog { Title = title, Filter = filter })
            {
                if (Directory.Exists(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
            }
        }

        void CmdOpen()
        {
            if (!ConfirmDiscard()) return;
            string p = AskOpen("Open .dat", "Interface files (*.dat)|*.dat|All files (*.*)|*.*");
            if (p != null) OpenDat(p);
        }

        void CmdOpenTxt()
        {
            if (!ConfirmDiscard()) return;
            string p = AskOpen("Open definition file (.txt)", "Definition files (*.txt)|*.txt|All files (*.*)|*.*");
            if (p != null) OpenTxt(p);
        }

        public void OpenTxt(string p)
        {
            string fmt = FormatDialog.Ask(this, Decompiler.FormatFromTxt(p) ?? "e2150");
            if (fmt == null) return;
            var log = new StringBuilder();
            byte[] data;
            try
            {
                using (Log.Capture(log)) data = Compiler.Build(Path.GetFullPath(p), false, fmt, Path.GetDirectoryName(Path.GetFullPath(p)));
            }
            catch (Exception e) when (e is DefinitionError || e is BitmapError || e is IOException || e is UnauthorizedAccessException)
            {
                G.Error(this, "Compilation failed", $"{e.Message}\n\n{log}");
                return;
            }
            var warnings = log.ToString().Split('\n').Where(l => l.StartsWith("WARNING: bitmap") || l.StartsWith("Note: ")).ToList();
            if (warnings.Count > 0) G.Warn(this, string.Join("\n\n", warnings));
            sourceTxt = Path.GetFullPath(p);
            SetDocument(DatFile.Read(data, fmt), null);
            sourceName = Path.GetFileNameWithoutExtension(p);
            Settings.LastDir = Path.GetDirectoryName(Path.GetFullPath(p));
            Dirty = true;
            UpdateTitle();
            SetStatus($"Compiled {Path.GetFileName(p)} ({Fmt.Names[fmt]}). Use Save as to write the .dat.");
            TextureHint();
        }

        public bool CmdSave()
        {
            if (D == null) return false;
            if (FilePath == null) return CmdSaveAs();
            return WriteTo(FilePath);
        }

        bool CmdSaveAs()
        {
            if (D == null) return false;
            using (var dlg = new SaveFileDialog { Title = "Save .dat", DefaultExt = "dat", Filter = "Interface files (*.dat)|*.dat|All files (*.*)|*.*" })
            {
                if (Directory.Exists(Settings.LastDir)) dlg.InitialDirectory = Settings.LastDir;
                if (FilePath != null) dlg.FileName = Path.GetFileName(FilePath);
                else dlg.FileName = sourceName + ".dat";
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                if (WriteTo(dlg.FileName)) { FilePath = dlg.FileName; UpdateTitle(); return true; }
                return false;
            }
        }

        public bool WriteTo(string path)
        {
            byte[] data;
            try
            {
                data = Snapshot();
                if (!DatFile.Read(data, D.Fmt).Write().SequenceEqual(data)) throw new UserError("internal check failed: the written data does not read back identically");
                if (File.Exists(path))
                {
                    string bak = path + ".bak";
                    if (!File.Exists(bak)) File.Copy(path, bak);
                }
                File.WriteAllBytes(path, data);
            }
            catch (Exception e) when (G.IsUserError(e) || e is IOException || e is UnauthorizedAccessException || e is EndOfStreamException)
            {
                G.Error(this, "Save failed", e.Message);
                return false;
            }
            Dirty = false;
            UpdateTitle();
            SetStatus($"Saved {path} ({data.Length} bytes)");
            return true;
        }

        void CmdExportTxt()
        {
            if (D == null) return;
            string outDir;
            using (var dlg = new FolderBrowserDialog { Description = "Export into folder" })
            {
                if (Directory.Exists(Settings.LastDir)) dlg.SelectedPath = Settings.LastDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                outDir = dlg.SelectedPath;
            }
            string bas = FilePath != null ? Path.GetFileNameWithoutExtension(FilePath) : sourceName;
            var log = new StringBuilder();
            bool ok;
            try
            {
                using (Log.Capture(log))
                    ok = Decompiler.Decompile(Path.Combine(outDir, bas + ".dat"), outDir, new Dictionary<string, int>(), null, ResPath, D.Fmt, null, "auto", Snapshot()).Ok;
            }
            catch (Exception e) when (G.IsUserError(e) || e is IOException || e is UnauthorizedAccessException || e is EndOfStreamException)
            {
                ok = false;
                log.Append(e.Message);
            }
            ShowLog("Export as .txt project", log.ToString(), ok);
        }

        public void ShowLog(string title, string text, bool ok = true)
        {
            var w = new LogWindow(title, text, ok);
            w.Show(this);
        }

        void CmdLoadResources()
        {
            string p = AskOpen("Identifier names", "Header files (*.h)|*.h|All files (*.*)|*.*");
            if (p != null) { LoadResources(p); RefreshView(true); }
        }

        void AutoLoadResources()
        {
            resNames = new Dictionary<long, string>(); resValues = new Dictionary<string, long>(); ResPath = null;
            if (D == null || !Fmt.IsNew(D.Fmt)) return;
            string fname = Fmt.ResourceFile(D.Fmt);
            var dirs = new[] { FilePath != null ? Path.GetDirectoryName(Path.GetFullPath(FilePath)) : null, sourceTxt != null ? Path.GetDirectoryName(sourceTxt) : null, Settings.LastDir, Environment.CurrentDirectory };
            foreach (var d in dirs.Where(x => !string.IsNullOrEmpty(x)))
            {
                string p = G.FindFileCi(d, fname);
                if (p != null) { LoadResources(p); return; }
            }
        }

        void LoadResources(string path)
        {
            ResourceNames rn;
            try { rn = new ResourceNames(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || G.IsUserError(e)) { G.Error(this, "Cannot read", e.Message); return; }
            ResPath = path;
            resValues = new Dictionary<string, long>(rn.Values);
            resNames = new Dictionary<long, string>();
            string[] preferred = { "idd_", "mmd_", "idc_", "id_" };
            bool Pref(string s) => preferred.Any(p => s.ToLowerInvariant().StartsWith(p, StringComparison.Ordinal));
            foreach (var low in rn.Order)
            {
                if (!rn.Values.TryGetValue(low, out long v)) continue;
                if (!resNames.TryGetValue(v, out string cur) || (!Pref(cur) && Pref(low))) resNames[v] = rn.OrigNames[low];
            }
            SetStatus($"Identifier names loaded from {path}");
        }

        public string ResName(long v) => resNames.TryGetValue(v, out string n) ? n : "";

        public long ParseRes(string text)
        {
            string t = text.Trim();
            try { return G.ParseInt(t); }
            catch (UserError)
            {
                if (resValues.TryGetValue(TextUtil.AsciiLower(t), out long v)) return v;
                throw new UserError($"unknown identifier '{t}' (load resource.h via File menu)");
            }
        }

        void CmdLoadNames()
        {
            if (D == null) return;
            string p = AskOpen("Original definition file with names", "Definition files (*.txt)|*.txt|All files (*.*)|*.*");
            if (p == null) return;
            int n;
            try { n = Names.LoadUserNames(p); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { G.Error(this, "Cannot read", e.Message); return; }
            if (FilePath != null) { Settings.NamesFiles[Path.GetFullPath(FilePath)] = p; Settings.Save(); }
            RefreshView(true);
            SetStatus($"Names of {n} items loaded from {p}");
        }

        void CmdClearNames()
        {
            Names.ClearUserNames();
            if (FilePath != null) { Settings.NamesFiles.Remove(Path.GetFullPath(FilePath)); Settings.Save(); }
            RefreshView(true);
        }

        /// <summary>Unique font index -> descriptions of the matching fonts of the developers' spreadsheet.</summary>
        public Dictionary<int, string> FontDescriptions()
        {
            var o = new Dictionary<int, string>();
            var sheet = Names.SheetFonts(D.Fmt);
            if (sheet.Count == 0) return o;
            for (int i = 0; i < D.Fonts.Count; i++)
            {
                var f = D.Fonts[i];
                var hits = new List<string>();
                foreach (var sf in sheet)
                {
                    long sx = sf.Long("sx"), sy = sf.Long("sy");
                    var (fc, sc, _) = Parser.SplitFontColor(sf.Long("color"), sf.Long("scolor"), sx, sy);
                    if ((sf.Str("name") ?? "").ToLowerInvariant() == (f.Name ?? "").ToLowerInvariant() && sf.Long("size") == f.Size &&
                        sf.Long("weight") == f.Weight && sf.Long("italic") == f.Italic && fc == f.Color && (sx & 255) == f.Sx &&
                        (sy & 255) == f.Sy && sc == f.SColor)
                    {
                        var tagObj = sf["tag"];
                        var tags = tagObj is List<object> tl ? tl.Select(Json.ToLong).ToList() : tagObj != null && !(tagObj is string) ? new List<long> { Json.ToLong(tagObj) } : new List<long>();
                        string desc = sf.Str("desc") ?? "";
                        hits.Add($"#{sf["nr"]}" + (desc.Length > 0 ? " " + desc : "") + string.Concat(tags.Select(t => $" <F{t}>")));
                    }
                }
                if (hits.Count > 0) o[i] = string.Join(", ", hits);
            }
            return o;
        }

        public string ItemName(long num) => ShowNames ? Names.ItemName(D.Fmt, num) : "";
        public string StateName(long num, long sn) => ShowNames ? Names.StateName(D.Fmt, num, sn) : "";
        public string ItemText(long num) { string n = ItemName(num); return $"Item {num}" + (n.Length > 0 ? $" - {n}" : ""); }
        public string StateText(long num, long sn) { string n = StateName(num, sn); return $"State 0x{sn:X2}" + (n.Length > 0 ? $" - {n}" : ""); }

        // filled-in rectangles: the compiler computes the rectangles of empty slots from the No* flags
        // (and margins); they are stored in the .dat, the flags themselves are not
        public (long? flags, long[] margins) FillSettings(long num)
        {
            var it = D.Items[num];
            var margins = Fmt.IsNew(D.Fmt) ? Decompiler.InferMargins(it) : null;
            return (Decompiler.InferNoFlags(it, margins), margins);
        }

        public void RederiveItem(long num, long flags, long[] margins, long? only = null)
        {
            var it = D.Items[num];
            foreach (var kv in it.States)
            {
                if (only != null && kv.Key != only.Value) continue;
                var rects = Decompiler.SimulateRects(flags, kv.Value, margins);
                for (int i = 0; i < kv.Value.Slots.Count; i++) kv.Value.Slots[i] = new Slot(kv.Value.Slots[i].Mt, rects[i]);
            }
        }

        public void CmdUndo()
        {
            if (D == null || undo.Count == 0) return;
            redo.Add(Snapshot());
            D = DatFile.Read(undo[undo.Count - 1], D.Fmt);
            undo.RemoveAt(undo.Count - 1);
            Dirty = true;
            UpdateTitle();
            RefreshView(true);
            SetStatus("Undo");
        }

        public void CmdRedo()
        {
            if (D == null || redo.Count == 0) return;
            undo.Add(Snapshot());
            D = DatFile.Read(redo[redo.Count - 1], D.Fmt);
            redo.RemoveAt(redo.Count - 1);
            Dirty = true;
            UpdateTitle();
            RefreshView(true);
            SetStatus("Redo");
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Ctrl+Z / Ctrl+Y in a text box edit the text, not the file
            if ((keyData == (Keys.Control | Keys.Z) || keyData == (Keys.Control | Keys.Y)) && ActiveControlDeep() is TextBoxBase) return false;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        Control ActiveControlDeep()
        {
            Control c = ActiveControl;
            while (c is ContainerControl cc && cc.ActiveControl != null) c = cc.ActiveControl;
            return c;
        }

        public void CmdCheck()
        {
            if (D == null) return;
            var lines = new List<string> { $"Format: {Fmt.Names[D.Fmt]}", $"Items: {D.Items.Count}, states: {D.Items.Values.Sum(i => i.States.Count)}" };
            lines.AddRange(D.ItemAnomalies.Select(a => $"Warning: {a}"));
            foreach (var n in D.ItemRepeatOrder.Where(D.ItemRepeats.ContainsKey).Distinct()) lines.Add($"Item {n} is stored {D.ItemRepeats[n] + 1} times (identical copies)");
            foreach (var ikv in D.Items)
                foreach (var skv in ikv.Value.States)
                {
                    var st = skv.Value;
                    for (int i = 0; i < st.Slots.Count; i++)
                        if (st.Slots[i].Mt != 0xFFFF && (st.Slots[i].Mt & 0xFF) >= D.Materials.Count) lines.Add($"Item {ikv.Key} State {skv.Key}: slot {i + 1} uses a missing material");
                    if (st.Font >= D.Fonts.Count) lines.Add($"Item {ikv.Key} State {skv.Key}: uses a missing font {st.Font}");
                    if (st.Sound >= D.Sounds.Count && D.Fmt != "tw2") lines.Add($"Item {ikv.Key} State {skv.Key}: uses a missing sound {st.Sound}");
                }
            try
            {
                var data = Snapshot();
                lines.Add(DatFile.Read(data, D.Fmt).Write().SequenceEqual(data) ? "Write/read check: OK" : "Write/read check: FAILED");
            }
            catch (Exception e) when (G.IsUserError(e) || e is EndOfStreamException) { lines.Add($"The file cannot be written: {e.Message}"); }
            ShowLog("Check file", string.Join("\n", lines));
        }

        // ================================================================== tree
        void BuildTree()
        {
            var open = new HashSet<string>(nodes.Where(kv => kv.Value.IsExpanded).Select(kv => kv.Key));
            bool itemsKnown = nodes.ContainsKey("items");
            suppressSelect = true;
            tree.BeginUpdate();
            tree.Nodes.Clear();
            nodes.Clear();
            if (D != null)
            {
                TreeNode Add(TreeNodeCollection parent, string key, string text)
                {
                    var n = new TreeNode(text) { Tag = key, Name = key };
                    parent.Add(n);
                    nodes[key] = n;
                    return n;
                }
                Add(tree.Nodes, "hdr", $"Header ({Fmt.Names[D.Fmt]})");
                Add(tree.Nodes, "mat", $"Materials ({D.Materials.Count})");
                if (D.Fmt != "tw2") Add(tree.Nodes, "snd", $"Sounds ({D.Sounds.Count})");
                Add(tree.Nodes, "fnt", $"Fonts ({D.Fonts.Count})");
                if (Fmt.IsNew(D.Fmt))
                {
                    Add(tree.Nodes, "cd", $"CustomData ({D.CustomData.Count})");
                    Add(tree.Nodes, "dlg", $"DialogTemplates ({D.Dialogs.Count})");
                }
                var items = Add(tree.Nodes, "items", $"Items ({D.Items.Count})");
                foreach (var ikv in D.Items)
                {
                    string key = $"item:{ikv.Key}";
                    var n = Add(items.Nodes, key, $"{ItemText(ikv.Key)}   ({ikv.Value.States.Count} states)");
                    foreach (var skv in ikv.Value.States)
                        Add(n.Nodes, $"state:{ikv.Key}:{skv.Key}", StateLabel(ikv.Key, skv.Key));
                    if (open.Contains(key)) n.Expand();
                }
                if (!itemsKnown || open.Contains("items")) items.Expand();
            }
            tree.EndUpdate();
            suppressSelect = false;
        }

        public string StateLabel(long num, long sn)
        {
            var st = D.Items[num].States[sn];
            int used = st.Slots.Count(s => s.Mt != 0xFFFF);
            return $"{StateText(num, sn)}   ({used} slots{(st.Particle.Length > 0 ? ", particles" : "")})";
        }

        public void SetTreeText(string key, string text) { if (nodes.TryGetValue(key, out var n)) n.Text = text; }

        void SelectInTree(string node)
        {
            if (!nodes.TryGetValue(node, out var n)) return;
            suppressSelect = true;
            for (var p = n.Parent; p != null; p = p.Parent) p.Expand();
            tree.SelectedNode = n;
            n.EnsureVisible();
            suppressSelect = false;
        }

        public void SelectNode(string node)
        {
            if (!nodes.ContainsKey(node)) return;
            ShowNode(node);
        }

        public void GotoItem()
        {
            long n;
            try { n = G.ParseInt(gotoBox.Text); } catch (UserError) { return; }
            if (D != null && D.Items.ContainsKey(n)) SelectNode($"item:{n}");
            else SetStatus($"Item {gotoBox.Text} does not exist");
        }

        public void ShowNode(string node)
        {
            if (D == null) return;
            CurrentNode = node;
            ScheduleRebuild();
        }

        void BuildPanel(string node)
        {
            CurrentNode = node;
            Control c;
            if (node == "hdr") c = PanelHeader();
            else if (node == "mat") c = PanelMaterials();
            else if (node == "snd") c = PanelSounds();
            else if (node == "fnt") c = PanelFonts();
            else if (node == "cd") c = PanelCustom();
            else if (node == "dlg") c = PanelDialogs();
            else if (node == "items") c = PanelItems();
            else if (node.StartsWith("item:")) c = PanelItem(long.Parse(node.Split(':')[1], CultureInfo.InvariantCulture));
            else if (node.StartsWith("state:"))
            {
                var p = node.Split(':');
                c = new StatePanel(this, long.Parse(p[1], CultureInfo.InvariantCulture), long.Parse(p[2], CultureInfo.InvariantCulture));
            }
            else c = PanelHeader();
            SetContent(c);
        }

        // ================================================================== form helpers
        /// <summary>Label + text box; the value is committed with Enter or when the box loses focus.
        /// An invalid value restores the previous text and shows the error in the status bar.</summary>
        public TextBox Entry(Grid parent, int row, string label, string value, Action<string> commit, int width = 14, int col = 0, string tip = "")
        {
            parent.Put(G.Lbl(label), row, col);
            var box = new TextBox { Text = value, Width = Math.Max(50, width * 8), Margin = new Padding(4, 2, 4, 2) };
            parent.Put(box, row, col + 1);
            if (tip.Length > 0) parent.Put(G.Lbl(tip, G.Grey), row, col + 2);
            BindCommit(box, value, commit, label);
            return box;
        }

        public void BindCommit(TextBox box, string value, Action<string> commit, string label)
        {
            string last = value;
            void Do()
            {
                if (box.IsDisposed) return;
                string txt = box.Text;
                if (txt == last) return;
                try { commit(txt); last = txt; }
                catch (Exception e) when (G.IsUserError(e))
                {
                    SetStatus($"Invalid value for {label.TrimEnd(':')}: {e.Message}");
                    box.Text = last;
                }
            }
            box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Do(); } };
            box.Leave += (s, e) => Do();
        }

        public CheckBox Check(string text, bool value, Action<bool> commit)
        {
            var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(4, 3, 8, 3) };
            c.CheckedChanged += (s, e) => commit(c.Checked);
            return c;
        }

        public string ColourOrder()
        {
            string o = Settings.ColourOrder;
            if (o == "ARGB" || o == "ABGR") return o;
            return D != null && G.ColourOrderDefault.TryGetValue(D.Fmt, out string d) ? d : "ARGB";
        }

        void SetColourOrder(string val)
        {
            Settings.ColourOrder = val;
            Settings.Save();
            foreach (var it in colourOrderItems) it.Checked = (string)it.Tag == val;
            if (D != null) RefreshView(false);
            SetStatus($"Colours shown as {G.ColourOrderLabel[ColourOrder()]}");
        }

        /// <summary>Hex entry + swatch (click it to pick a colour) + the colour as #RRGGBB.</summary>
        public void ColourEntry(Grid parent, int row, int col, string label, long value, Action<long> commit)
        {
            parent.Put(G.Lbl(label), row, col);
            string order = ColourOrder();
            var box = new TextBox { Text = G.Hex32(value), Width = 96, Margin = new Padding(4, 2, 2, 2) };
            var sw = new Panel { Width = 26, Height = 20, BackColor = G.ToColor(value, order), BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand, Margin = new Padding(3, 2, 3, 2) };
            var rgb = G.Lbl($"{G.ColourOrderLabel[order]} = {G.ToHtml(value, order)}", G.Grey);
            string last = G.Hex32(value);
            void Do()
            {
                if (box.IsDisposed || box.Text == last) return;
                long v;
                try { v = G.ParseInt(box.Text) & 0xFFFFFFFFL; } catch (UserError) { box.Text = last; return; }
                last = G.Hex32(v);
                box.Text = last;
                sw.BackColor = G.ToColor(v, order);
                rgb.Text = $"{G.ColourOrderLabel[order]} = {G.ToHtml(v, order)}";
                commit(v);
            }
            box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Do(); } };
            box.Leave += (s, e) => Do();
            sw.Click += (s, e) =>
            {
                long cur = G.ParseInt(last) & 0xFFFFFFFFL;
                using (var cd = new ColorDialog { Color = G.ToColor(cur, order), FullOpen = true })
                {
                    if (cd.ShowDialog(this) != DialogResult.OK) return;
                    box.Text = G.Hex32(G.FromRgb(cur, cd.Color.R, cd.Color.G, cd.Color.B, order));
                    Do();
                }
            };
            var fr = G.Row(box, sw, rgb);
            fr.Margin = new Padding(0);
            fr.Dock = DockStyle.None;
            parent.Put(fr, row, col + 1);
        }
    }
}

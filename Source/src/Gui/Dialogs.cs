// Dialogs of the editor: property record, format choice, and the Export all / Import all window.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace InterfaceTools.Gui
{
    using Font = System.Drawing.Font;
    using TextUtil = InterfaceTools.Text;

    /// <summary>Edits one property record (key + list of values).</summary>
    public class RecordDialog : OkCancelDialog
    {
        static readonly Dictionary<int, (int pos, string name)[]> ColourPositions = new Dictionary<int, (int, string)[]>
        {
            { 0, new[] { (0, "colour") } },
            { 4, new[] { (0, "diffuse"), (1, "ambient"), (2, "border"), (4, "sepia") } },
        };

        readonly ComboBox kind;
        readonly TextBox knum, vals;
        readonly FlowLayoutPanel swatches;
        readonly List<JObj> presets;
        readonly string order;
        Record result;

        RecordDialog(int key, List<long> values, List<JObj> presets, string order) : base("Property record")
        {
            this.presets = presets ?? new List<JObj>();
            this.order = order;
            var g = Body.Add(new Grid());
            g.Put(G.Lbl("Key:"), 0, 0);
            var choices = G.RecordKeys.Select(kv => $"{kv.Key} {kv.Value}").Concat(new[] { "other (Effect:N)" }).ToList();
            kind = G.Combo(choices, 180);
            kind.SelectedIndex = G.RecordKeys.ContainsKey(key) ? key : choices.Count - 1;
            g.Put(kind, 0, 1);
            g.Put(G.Lbl("Key number:"), 1, 0);
            knum = new TextBox { Text = key.ToString(), Width = 70 };
            g.Put(knum, 1, 1);
            Body.Add(G.Lbl("Values (comma separated, decimal or 0x hex):"));
            vals = Body.Add(new TextBox { Text = string.Join(",", values.Select(G.ValueText)), Width = 520 });
            swatches = Body.Add(G.Row());
            kind.SelectionChangeCommitted += (s, e) =>
            {
                string t = (string)kind.SelectedItem;
                if (!t.StartsWith("other")) knum.Text = t.Split(' ')[0];
                UpdateSwatches();
            };
            vals.TextChanged += (s, e) => UpdateSwatches();
            knum.TextChanged += (s, e) => UpdateSwatches();
            if (this.presets.Count > 0)
            {
                Body.Add(G.Lbl("Light presets (developers' spreadsheet):"));
                var pcb = Body.Add(G.Combo(this.presets.Select(p => $"{p.Str("name")}  {p.Str("desc")}"), 520));
                pcb.SelectionChangeCommitted += (s, e) =>
                {
                    var p = this.presets[pcb.SelectedIndex];
                    kind.SelectedIndex = 4;
                    knum.Text = "4";
                    vals.Text = string.Join(",", p.Arr("values").Select(Json.ToLong).Select(G.ValueText));
                    UpdateSwatches();
                };
                Body.Add(G.Lbl("Light values: diffuse, ambient, ... colours, then numbers", G.Grey));
            }
            UpdateSwatches();
            Shown += (s, e) => vals.Focus();
        }

        static List<long> ParseValues(string raw) =>
            raw.Trim().Length == 0 ? new List<long>() : raw.Split(',').Select(v => G.ParseInt(v) & 0xFFFFFFFFL).ToList();

        void UpdateSwatches()
        {
            foreach (Control c in swatches.Controls.Cast<Control>().ToList()) { swatches.Controls.Remove(c); c.Dispose(); }
            long key; List<long> v;
            try { key = G.ParseInt(knum.Text); v = vals.Text.Split(',').Where(x => x.Trim().Length > 0).Select(x => G.ParseInt(x) & 0xFFFFFFFFL).ToList(); }
            catch (UserError) { return; }
            if (!ColourPositions.TryGetValue((int)Math.Min(int.MaxValue, Math.Max(int.MinValue, key)), out var pos)) return;
            var used = pos.Where(p => p.pos < v.Count).ToList();
            if (used.Count == 0) return;
            swatches.Controls.Add(G.Lbl($"Colours ({G.ColourOrderLabel[order]}):", G.Grey));
            foreach (var (i, n) in used)
            {
                swatches.Controls.Add(G.Lbl(n));
                swatches.Controls.Add(new Panel { Width = 24, Height = 18, BackColor = G.ToColor(v[i], order), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(2, 4, 6, 2) });
            }
        }

        protected override bool Validate2()
        {
            try
            {
                long key = G.ParseInt(knum.Text);
                if (key < 0 || key >= 0xFFFF) throw new UserError("key must be 0..65534");
                var v = ParseValues(vals.Text);
                if (v.Count > 64) throw new UserError("at most 64 values");
                result = new Record((int)key, v);
                return true;
            }
            catch (UserError e) { G.Error(this, "Invalid record", e.Message); return false; }
        }

        public static Record Ask(IWin32Window owner, int key, List<long> values, List<JObj> presets, string order)
        {
            using (var d = new RecordDialog(key, values, presets, order))
                return d.ShowDialog(owner) == DialogResult.OK ? d.result : null;
        }
    }

    public class FormatDialog : OkCancelDialog
    {
        readonly List<RadioButton> radios = new List<RadioButton>();

        FormatDialog(string def) : base("Choose the format")
        {
            Body.Add(G.Lbl("Compile the text file for:"));
            foreach (var f in Fmt.All)
            {
                var r = new RadioButton { Text = f == Fmt.E2150 ? $"{Fmt.Names[f]} (also {Fmt.E2150Family})" : Fmt.Names[f], Tag = f, Checked = f == def, AutoSize = true, Margin = new Padding(16, 2, 4, 2) };
                radios.Add(r);
                Body.Add(r);
            }
            if (!radios.Any(r => r.Checked)) radios[0].Checked = true;
        }

        public static string Ask(IWin32Window owner, string def)
        {
            if (G.TestHook != null) return def;
            using (var d = new FormatDialog(def))
                return d.ShowDialog(owner) == DialogResult.OK ? (string)d.radios.First(r => r.Checked).Tag : null;
        }
    }

    /// <summary>Tools -> Export all / Import all: runs the Work folder pipeline in a background thread
    /// and shows its log.</summary>
    public class PipelineDialog : Form
    {
        readonly bool export;
        readonly MainForm app;
        readonly TextBox folder;
        readonly CheckBox noTex, noMap, noDds, noInt, binary;
        readonly TextBox log;
        readonly Button run, cancel, close;
        readonly Label state;
        readonly List<string> pending = new List<string>();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
        Thread worker;
        volatile bool cancelled;
        bool closeRequested;

        public static void Show(MainForm owner, bool export)
        {
            var d = new PipelineDialog(owner, export);
            d.Show(owner);
        }

        PipelineDialog(MainForm app, bool export)
        {
            this.app = app; this.export = export;
            Text = export ? "Export all (Work folder)" : "Import all (Work folder)";
            Size = new Size(960, 640);
            MinimumSize = new Size(640, 400);
            StartPosition = FormStartPosition.CenterParent;
            var top = new VStack { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6) };
            string def = !string.IsNullOrEmpty(app.Settings.WorkDir) && Directory.Exists(app.Settings.WorkDir) ? app.Settings.WorkDir : Pipeline.DefaultWorkDir;
            folder = new TextBox { Text = def, Width = 620 };
            top.Add(G.Row(G.Lbl("Work folder:"), folder, G.Btn("Browse...", Browse)));
            noTex = new CheckBox { Text = "Skip .tex (TexEnv)", AutoSize = true };
            noMap = new CheckBox { Text = "Skip maps (.txt -> .dat)", AutoSize = true };
            noDds = new CheckBox { Text = "Skip .dds", AutoSize = true };
            noInt = new CheckBox { Text = "Skip .int", AutoSize = true };
            binary = new CheckBox { Text = "Binary mode for .def without 'mode:' (default: text)", AutoSize = true };
            var opts = G.WrapRow(noTex, noDds, noInt);
            if (!export) { opts.Controls.Add(noMap); opts.Controls.Add(binary); }
            top.Add(opts);
            string texInfo = TexEnv.Available ? $"TexEnv: {TexEnv.ExePath}" : $"TexEnv.exe not found in {AppDomain.CurrentDomain.BaseDirectory} - .tex files will be skipped";
            top.Add(G.Lbl(texInfo, TexEnv.Available ? G.Grey : G.WarnColour));
            top.Add(G.Wrapped(export
                ? "Every subfolder of the Work folder: .dat -> .txt + bitmaps (the tile layout goes straight to TexEnv), .tex -> .png, .int -> .def + .addfileinfo; then all .dds -> .png + .json."
                : "The Work folder and its subfolders: .def -> .int, .png -> .tex (TexEnv), .txt -> .dat (compiler); then all .json + .png -> .dds.", 900, G.Grey));
            log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9f), BackColor = SystemColors.Window };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
            run = G.Btn(export ? "Export" : "Import", Start, 90);
            cancel = G.Btn("Cancel", () => { cancelled = true; state.Text = "Cancelling after the current file..."; }, 90);
            cancel.Enabled = false;
            close = G.Btn("Close", Close, 90);
            state = G.Lbl("");
            bar.Controls.AddRange(new Control[] { run, cancel, close, G.Btn("Copy log", () => { try { Clipboard.SetText(log.Text.Length > 0 ? log.Text : " "); } catch (Exception) { } }), state });
            Controls.Add(log);
            Controls.Add(top);
            Controls.Add(bar);
            timer.Tick += (s, e) => Flush();
            FormClosing += (s, e) =>
            {
                if (worker != null && worker.IsAlive)
                {
                    if (!G.YesNo(this, "The task is still running. Cancel it after the current file?")) { e.Cancel = true; return; }
                    cancelled = true;
                    closeRequested = true;
                    e.Cancel = true;
                }
            };
        }

        void Browse()
        {
            using (var d = new FolderBrowserDialog { Description = "Work folder" })
            {
                if (Directory.Exists(folder.Text)) d.SelectedPath = folder.Text;
                if (d.ShowDialog(this) == DialogResult.OK) folder.Text = d.SelectedPath;
            }
        }

        void Start()
        {
            string root = folder.Text.Trim().Trim('"');
            if (!Directory.Exists(root)) { G.Error(this, Text, $"The folder does not exist:\n{root}"); return; }
            app.Settings.WorkDir = root;
            app.Settings.Save();
            var o = new PipelineOptions { Root = Path.GetFullPath(root), NoTex = noTex.Checked, NoMap = noMap.Checked, NoDds = noDds.Checked, NoInt = noInt.Checked, Mode = binary.Checked ? "binary" : "text", Cancelled = () => cancelled };
            log.Clear();
            cancelled = false;
            run.Enabled = false; cancel.Enabled = true;
            state.Text = "Running...";
            UseWaitCursor = true;
            timer.Start();
            worker = new Thread(() =>
            {
                int r = 1;
                using (Log.Redirect(s => { lock (pending) pending.Add(s); }))
                {
                    try { r = export ? Pipeline.ExportAll(o) : Pipeline.ImportAll(o); }
                    catch (Exception e) { Log.Line($"Unexpected error: {e}"); }
                }
                try { BeginInvoke((Action)(() => Finished(r))); } catch (InvalidOperationException) { }
            }) { IsBackground = true, Name = "pipeline" };
            worker.Start();
        }

        void Flush()
        {
            List<string> lines;
            lock (pending) { if (pending.Count == 0) return; lines = new List<string>(pending); pending.Clear(); }
            log.AppendText(string.Join("\r\n", lines.Select(l => l.Replace("\n", "\r\n"))) + "\r\n");
        }

        void Finished(int r)
        {
            timer.Stop();
            Flush();
            UseWaitCursor = false;
            run.Enabled = true; cancel.Enabled = false;
            state.Text = cancelled ? "Cancelled." : r == 0 ? "Finished successfully." : "Finished with problems - see the log.";
            state.ForeColor = r == 0 && !cancelled ? Color.DarkGreen : G.WarnColour;
            if (closeRequested) BeginInvoke((Action)Close);
        }
    }
}

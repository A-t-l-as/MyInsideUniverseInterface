// Shared pieces of the graphical editor: value parsing, colours, settings, layout helpers and
// small dialogs. (Port of interfacemap_gui.py, written for Windows Forms.)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InterfaceTools.Gui
{
    using Font = System.Drawing.Font;
    using TextUtil = InterfaceTools.Text;

    /// <summary>An invalid value typed by the user (Python: ValueError).</summary>
    public class UserError : Exception { public UserError(string m) : base(m) { } }

    public static class G
    {
        public const string AppName = "InterfaceMap Editor";
        public static readonly string[] SlotNames = { "background", "top", "bottom", "left", "right", "top-left", "top-right", "bottom-left", "bottom-right" };
        public static readonly Color[] SlotColours = { Hex("#ffffff"), Hex("#ff5050"), Hex("#ff9a30"), Hex("#50ff50"), Hex("#40c0ff"), Hex("#ff50ff"), Hex("#ffff40"), Hex("#40ffd0"), Hex("#a080ff") };
        public static readonly Dictionary<int, string> RecordKeys = new Dictionary<int, string> { { 0, "Color" }, { 1, "Selection" }, { 2, "Uses" }, { 3, "Moving" }, { 4, "Light" } };
        public const string NoMaterial = "(none)";
        public static readonly string[] AlignX = { "Left", "Center", "Random", "Right" };
        public static readonly string[] AlignY = { "Top (LeftY)", "Center", "Random", "Bottom (RightY)" };
        public static readonly string[] ForceNames = { "ForceMessageBoxBackground", "ForceNoMessageBoxBackground", "ForceFullScreenTransparentBackground", "ForceNoFullScreenTransparentBackground" };
        public static readonly string[] TextureExts = { ".dds", ".tga", ".png", ".bmp" };
        public static readonly Color Grey = Color.FromArgb(0x66, 0x66, 0x66);
        public static readonly Color NoteColour = Color.FromArgb(0xa0, 0x50, 0x00);
        public static readonly Color WarnColour = Color.FromArgb(0xb0, 0x50, 0x00);

        public static Color Hex(string s) => Color.FromArgb(Convert.ToInt32(s.Substring(1, 2), 16), Convert.ToInt32(s.Substring(3, 2), 16), Convert.ToInt32(s.Substring(5, 2), 16));

        // ------------------------------------------------------------------ values
        /// <summary>Decimal or 0x hex, optional minus sign.</summary>
        public static long ParseInt(string text)
        {
            string t = text.Trim().ToLowerInvariant();
            bool neg = t.StartsWith("-");
            if (neg) t = t.Substring(1);
            long v;
            try
            {
                if (t.StartsWith("0x")) v = Convert.ToInt64(t.Substring(2).Replace("_", ""), 16);
                else
                {
                    if (t.Length == 0 || !t.All(c => (c >= '0' && c <= '9') || c == '_')) throw new FormatException();
                    v = long.Parse(t.Replace("_", ""), CultureInfo.InvariantCulture);
                }
            }
            catch (Exception e) when (e is FormatException || e is OverflowException || e is ArgumentException)
            {
                throw new UserError($"invalid literal for int(): '{text.Trim()}'");
            }
            return neg ? -v : v;
        }

        public static float ParseFloat(string text)
        {
            try { return (float)Text.ParseDouble(text.Trim()); }
            catch (Exception e) when (e is FormatException || e is OverflowException) { throw new UserError($"could not convert string to float: '{text.Trim()}'"); }
        }

        public static string Hex32(long v) => $"0x{v & 0xFFFFFFFFL:X8}";
        public static string ValueText(long v) => v >= 0x10000 ? Hex32(v) : v.ToString(CultureInfo.InvariantCulture);

        /// <summary>Errors that mean "this change is not possible" rather than a bug.</summary>
        public static bool IsUserError(Exception e) =>
            e is UserError || e is ArgumentException || e is FormatException || e is OverflowException ||
            e is KeyNotFoundException || e is IndexOutOfRangeException || e is InvalidOperationException || e is ToolError;

        // ------------------------------------------------------------------ colours
        // The old engine (Earth 2150, KnightShift) stores colours as 0xAABBGGRR ("blue is 0xFFFF0000",
        // InsideEarth wiki); Earth 2160, TwoWorlds and Two Worlds II use the usual 0xAARRGGBB.
        public static readonly Dictionary<string, string> ColourOrderDefault = new Dictionary<string, string> { { "e2150", "ABGR" }, { "ks", "ABGR" } };
        public static readonly Dictionary<string, string> ColourOrderLabel = new Dictionary<string, string> { { "ARGB", "AARRGGBB" }, { "ABGR", "AABBGGRR" } };

        public static (int r, int g, int b) ColourRgb(long v, string order)
        {
            int a = (int)((v >> 16) & 255), b = (int)((v >> 8) & 255), c = (int)(v & 255);
            return order == "ABGR" ? (c, b, a) : (a, b, c);
        }

        public static Color ToColor(long v, string order) { var (r, g, b) = ColourRgb(v, order); return Color.FromArgb(r, g, b); }
        public static string ToHtml(long v, string order) { var (r, g, b) = ColourRgb(v, order); return $"#{r:X2}{g:X2}{b:X2}"; }

        public static long FromRgb(long alphaFrom, int r, int g, int b, string order)
        {
            long lo = order == "ABGR" ? ((long)b << 16) | ((long)g << 8) | (long)r : ((long)r << 16) | ((long)g << 8) | (long)b;
            return (alphaFrom & 0xFF000000L) | lo;
        }

        public static int NextPow2(long v, int lo = 32, int hi = 4096)
        {
            int p = lo;
            while (p < v && p < hi) p *= 2;
            return p;
        }

        public static string BaseName(string material) => material.Replace("\\", "/").Split('/').Last();

        /// <summary>Case-insensitive lookup of a relative path (with \ or / separators).</summary>
        public static string FindFileCi(string directory, string rel)
        {
            var parts = rel.Replace("\\", "/").Split('/').Where(p => p.Length > 0).ToList();
            string cur = directory;
            foreach (var part in parts)
            {
                if (!Directory.Exists(cur)) return null;
                string direct = Path.Combine(cur, part);
                if (File.Exists(direct) || Directory.Exists(direct)) { cur = direct; continue; }
                string match;
                try { match = Directory.GetFileSystemEntries(cur).Select(Path.GetFileName).FirstOrDefault(e => string.Equals(e, part, StringComparison.OrdinalIgnoreCase)); }
                catch (Exception) { return null; }
                if (match == null) return null;
                cur = Path.Combine(cur, match);
            }
            return File.Exists(cur) ? cur : null;
        }

        /// <summary>Texture as a bitmap; transparent pixels blended over a grey checkerboard.</summary>
        public static Bitmap ToBitmap(Texture tex, int checker = 8)
        {
            int w = Math.Max(1, tex.Width), h = Math.Max(1, tex.Height);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var row = new byte[w * 4];
            for (int y = 0; y < tex.Height; y++)
            {
                for (int x = 0; x < tex.Width; x++)
                {
                    int i = (y * tex.Width + x) * 4;
                    int a = tex.Rgba[i + 3];
                    int bg = (((x / checker) + (y / checker)) & 1) != 0 ? 150 : 110;
                    int r = tex.Rgba[i], g = tex.Rgba[i + 1], b = tex.Rgba[i + 2];
                    if (a != 255) { r = (r * a + bg * (255 - a)) / 255; g = (g * a + bg * (255 - a)) / 255; b = (b * a + bg * (255 - a)) / 255; }
                    row[x * 4] = (byte)b; row[x * 4 + 1] = (byte)g; row[x * 4 + 2] = (byte)r; row[x * 4 + 3] = 255;
                }
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, w * 4);
            }
            bmp.UnlockBits(data);
            return bmp;
        }

        // ------------------------------------------------------------------ layout helpers
        public static Label Lbl(string text, Color? fore = null, bool bold = false, float size = 0)
        {
            var l = new Label { Text = text, AutoSize = true, Margin = new Padding(4, 5, 4, 3) };
            if (fore != null) l.ForeColor = fore.Value;
            if (bold || size > 0) l.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, size > 0 ? size : SystemFonts.MessageBoxFont.Size, bold ? FontStyle.Bold : FontStyle.Regular);
            return l;
        }

        public static Label Wrapped(string text, int width, Color? fore = null)
        {
            var l = Lbl(text, fore);
            l.MaximumSize = new Size(width, 0);
            return l;
        }

        public static Label Heading(string text) { var l = Lbl(text, null, true, 12); l.Margin = new Padding(8, 8, 8, 4); return l; }

        public static Button Btn(string text, Action click, int minWidth = 0)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(2), UseVisualStyleBackColor = true, Padding = new Padding(4, 0, 4, 0) };
            if (minWidth > 0) b.MinimumSize = new Size(minWidth, 0);
            b.Click += (s, e) => click();
            return b;
        }

        public static FlowLayoutPanel Row(params Control[] items)
        {
            var f = new LazyFlow { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(4), Dock = DockStyle.Top };
            f.Controls.AddRange(items);
            return f;
        }

        public static FlowLayoutPanel WrapRow(params Control[] items)
        {
            var f = Row(items);
            f.WrapContents = true;
            f.AutoSize = true;
            f.Dock = DockStyle.Fill;
            return f;
        }

        public static Section Group(string text, Control inner) => new Section(text, inner);

        public static ListView Table(params (string name, int width)[] cols)
        {
            var lv = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = false, Dock = DockStyle.Fill };
            foreach (var (n, w) in cols) lv.Columns.Add(n, w);
            return lv;
        }

        public static ComboBox Combo(IEnumerable<string> items, int width)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Margin = new Padding(4, 2, 4, 2), MaxDropDownItems = 25 };
            c.Items.AddRange(items.Cast<object>().ToArray());
            return c;
        }

        /// <summary>Automated tests: message boxes are printed instead of shown, questions answered with Yes.</summary>
        public static Action<string> TestHook;

        public static void Error(IWin32Window owner, string title, string text) { if (TestHook != null) { TestHook($"ERROR {title}: {text}"); return; } MessageBox.Show(owner, text, title, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        public static void Warn(IWin32Window owner, string text) { if (TestHook != null) { TestHook($"WARN {text}"); return; } MessageBox.Show(owner, text, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        public static void Info(IWin32Window owner, string text) { if (TestHook != null) { TestHook($"INFO {text}"); return; } MessageBox.Show(owner, text, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information); }
        public static bool YesNo(IWin32Window owner, string text) { if (TestHook != null) { TestHook($"ASK {text}"); return true; } return MessageBox.Show(owner, text, AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }
    }

    /// <summary>Layout containers of the editor start with their layout suspended: a panel is built
    /// completely (hundreds of controls) and laid out once, instead of after every added control.
    /// Lazy.ResumeAll is called before the panel is shown; a container created later resumes itself
    /// when its window is created.</summary>
    public interface ILazyLayout { void ResumeNow(); }

    public static class Lazy
    {
        public static void ResumeAll(Control root)
        {
            foreach (Control c in root.Controls) ResumeAll(c);
            if (root is ILazyLayout l) l.ResumeNow();
        }
    }

    public class LazyTable : TableLayoutPanel, ILazyLayout
    {
        bool suspended = true;
        public LazyTable() { SuspendLayout(); }
        public void ResumeNow() { if (suspended) { suspended = false; ResumeLayout(true); } }
        protected override void OnHandleCreated(EventArgs e)
        {
            if (suspended) { Lazy.ResumeAll(this); PerformLayout(); }
            base.OnHandleCreated(e);
        }
    }

    public class LazyFlow : FlowLayoutPanel, ILazyLayout
    {
        bool suspended = true;
        public LazyFlow() { SuspendLayout(); }
        public void ResumeNow() { if (suspended) { suspended = false; ResumeLayout(true); } }
        protected override void OnHandleCreated(EventArgs e)
        {
            if (suspended) { Lazy.ResumeAll(this); PerformLayout(); }
            base.OnHandleCreated(e);
        }
    }

    /// <summary>Titled frame around a control (like a GroupBox, but it sizes itself reliably: to its content,
    /// or - with AutoSize = false - it gives all the space to the content).</summary>
    public class Section : LazyTable
    {
        public readonly Label Title;
        public Section(string text, Control inner)
        {
            ColumnCount = 1;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            RowCount = 2;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Fill;
            Margin = new Padding(8, 4, 8, 4);
            Padding = new Padding(6, 2, 6, 6);
            Title = G.Lbl(text, null, true);
            Title.Margin = new Padding(0, 2, 0, 4);
            Controls.Add(Title, 0, 0);
            inner.Dock = DockStyle.Fill;
            inner.Margin = new Padding(0);
            Controls.Add(inner, 0, 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int y = Title.Bottom - Title.Height / 2;
            using (var pen = new Pen(SystemColors.ControlDark))
            {
                var r = new Rectangle(0, Math.Max(0, Title.Top + Title.Height / 2), Width - 1, Height - 1 - Math.Max(0, Title.Top + Title.Height / 2));
                e.Graphics.DrawLine(pen, r.Left, r.Top, Title.Left - 2, r.Top);
                e.Graphics.DrawLine(pen, Title.Right + 2, r.Top, r.Right, r.Top);
                e.Graphics.DrawLine(pen, r.Left, r.Top, r.Left, r.Bottom);
                e.Graphics.DrawLine(pen, r.Right, r.Top, r.Right, r.Bottom);
                e.Graphics.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Bottom);
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }

    /// <summary>Vertical stack of controls: rows sized to their content, one optional row that fills the rest.</summary>
    public class VStack : LazyTable
    {
        public VStack()
        {
            ColumnCount = 1;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Dock = DockStyle.Fill;
            RowCount = 0;
            Margin = new Padding(0);
            GrowStyle = TableLayoutPanelGrowStyle.AddRows;
        }

        public T Add<T>(T c) where T : Control
        {
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(c, 0, RowCount++);
            if (c.Dock == DockStyle.None) c.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            return c;
        }

        public T Fill<T>(T c) where T : Control
        {
            RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            c.Dock = DockStyle.Fill;
            Controls.Add(c, 0, RowCount++);
            return c;
        }
    }

    /// <summary>Label / field grid (Tk's grid geometry manager).</summary>
    public class Grid : LazyTable
    {
        public Grid()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = new Padding(4);
            GrowStyle = TableLayoutPanelGrowStyle.AddRows;
            Dock = DockStyle.Top;
        }

        public T Put<T>(T c, int row, int col, int colspan = 1) where T : Control
        {
            while (ColumnCount < col + colspan) { ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); ColumnCount++; }
            while (RowCount <= row) { RowStyles.Add(new RowStyle(SizeType.AutoSize)); RowCount++; }
            Controls.Add(c, col, row);
            if (colspan > 1)
            {
                SetColumnSpan(c, colspan);
                // a control spanning columns is not always given its full width: reserve it
                if (c.AutoSize && (c is Label || c is CheckBox)) c.MinimumSize = new Size(c.PreferredSize.Width, 0);
            }
            c.Anchor = AnchorStyles.Left;
            return c;
        }
    }

    /// <summary>Editor settings, stored in the user profile (the same file as the Python editor used).</summary>
    public class Settings
    {
        public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".interfacemap_gui.json");

        public List<string> TextureDirs = new List<string>();
        public Dictionary<string, string> MaterialFiles = new Dictionary<string, string>();
        public Dictionary<string, int> TileSizes = new Dictionary<string, int>();
        public Dictionary<string, string> NamesFiles = new Dictionary<string, string>();
        public string LastDir = "";
        public string ColourOrder = "auto";
        public string WorkDir = "";

        public Settings()
        {
            try
            {
                if (!(Json.Parse(File.ReadAllText(FilePath, TextUtil.Utf8NoBom)) is JObj o)) return;
                TextureDirs = (o.Arr("texture_dirs") ?? new List<object>()).OfType<string>().ToList();
                foreach (var kv in o.Obj("material_files") ?? new JObj()) if (kv.Value is string s) MaterialFiles[kv.Key] = s;
                foreach (var kv in o.Obj("tile_sizes") ?? new JObj()) try { TileSizes[kv.Key] = (int)Json.ToLong(kv.Value); } catch (Exception) { }
                foreach (var kv in o.Obj("names_files") ?? new JObj()) if (kv.Value is string s) NamesFiles[kv.Key] = s;
                LastDir = o.Str("last_dir") ?? "";
                ColourOrder = o.Str("colour_order") ?? "auto";
                WorkDir = o.Str("work_dir") ?? "";
            }
            catch (Exception) { }
        }

        public void Save()
        {
            try
            {
                var o = new JObj();
                o["texture_dirs"] = TextureDirs.Cast<object>().ToList();
                var mf = new JObj(); foreach (var kv in MaterialFiles) mf[kv.Key] = kv.Value; o["material_files"] = mf;
                var ts = new JObj(); foreach (var kv in TileSizes) ts[kv.Key] = (long)kv.Value; o["tile_sizes"] = ts;
                o["last_dir"] = LastDir;
                var nf = new JObj(); foreach (var kv in NamesFiles) nf[kv.Key] = kv.Value; o["names_files"] = nf;
                o["colour_order"] = ColourOrder;
                o["work_dir"] = WorkDir;
                File.WriteAllText(FilePath, Json.Dump(o), TextUtil.Utf8NoBom);
            }
            catch (Exception) { }
        }
    }

    /// <summary>Simple modal dialog base: content on top, OK / Cancel below.</summary>
    public class OkCancelDialog : Form
    {
        protected readonly VStack Body = new VStack { Dock = DockStyle.Fill, Padding = new Padding(8) };
        protected readonly FlowLayoutPanel Buttons;

        public OkCancelDialog(string title)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Body.AutoSize = true;
            Body.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var ok = new Button { Text = "OK", Width = 80 };
            var cancel = new Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => { if (Validate2()) { DialogResult = DialogResult.OK; Close(); } };
            AcceptButton = ok; CancelButton = cancel;
            Buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Bottom, Padding = new Padding(6) };
            Buttons.Controls.Add(cancel); Buttons.Controls.Add(ok);
            Controls.Add(Body);
            Controls.Add(Buttons);
        }

        protected virtual bool Validate2() => true;
    }

    /// <summary>One line of text input (tkinter simpledialog.askstring / askinteger).</summary>
    public class InputDialog : OkCancelDialog
    {
        readonly TextBox box = new TextBox { Width = 380, Margin = new Padding(4) };
        readonly Func<string, string> check;

        InputDialog(string title, string prompt, string initial, Func<string, string> check) : base(title)
        {
            this.check = check;
            Body.Add(G.Wrapped(prompt, 380));
            box.Text = initial ?? "";
            Body.Add(box);
        }

        protected override bool Validate2()
        {
            string err = check?.Invoke(box.Text);
            if (err != null) { G.Error(this, "Invalid value", err); return false; }
            return true;
        }

        public static string Ask(IWin32Window owner, string title, string prompt, string initial = "", Func<string, string> check = null)
        {
            using (var d = new InputDialog(title, prompt, initial, check))
                return d.ShowDialog(owner) == DialogResult.OK ? d.box.Text : null;
        }

        public static int? AskInt(IWin32Window owner, string title, string prompt, int initial, int min, int max)
        {
            string r = Ask(owner, title, prompt, initial.ToString(CultureInfo.InvariantCulture), t =>
            {
                if (!int.TryParse(t.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return "Not an integer.";
                if (v < min || v > max) return $"The allowed range is {min}..{max}.";
                return null;
            });
            return r == null ? (int?)null : int.Parse(r.Trim(), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Read-only text window (log of a check or of an export).</summary>
    public class LogWindow : Form
    {
        public readonly TextBox Box;
        public readonly Label Status;
        public readonly FlowLayoutPanel Bar;

        public LogWindow(string title, string text = "", bool? ok = null)
        {
            Text = title;
            Size = new Size(860, 520);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9f), BackColor = SystemColors.Window };
            Box.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            Status = G.Lbl(ok == null ? "" : ok.Value ? "Finished successfully." : "Finished with problems - see the log.");
            Bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4) };
            Bar.Controls.Add(G.Btn("Close", Close));
            Bar.Controls.Add(G.Btn("Copy all", () => { try { Clipboard.SetText(Box.Text.Length > 0 ? Box.Text : " "); } catch (Exception) { } }));
            Bar.Controls.Add(Status);
            Controls.Add(Box);
            Controls.Add(Bar);
        }

        public void AppendLines(IEnumerable<string> lines)
        {
            var s = string.Join("\r\n", lines) + "\r\n";
            Box.AppendText(s);
        }
    }
}

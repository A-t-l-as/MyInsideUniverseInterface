// State panel: properties, property records, the 9 slots, the texture view with mouse editing
// and the 9-slice preview.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InterfaceTools.Gui
{
    using Font = System.Drawing.Font;
    using TextUtil = InterfaceTools.Text;

    public class StatePanel : VStack
    {
        const int HandleSize = 5;
        readonly MainForm app;
        readonly long num, sn;
        int selSlot;
        int zoom = 2;
        int? viewMat;
        Drag drag;
        public ListBox recList;
        ListView slotTv;
        ComboBox matCb, viewCb;
        readonly Dictionary<string, TextBox> vars = new Dictionary<string, TextBox>();
        Label zoomLbl, texLbl;
        TexCanvas canvas;
        Panel canvasHost;
        TextBox pw, ph;
        PictureBox preview;

        class Drag { public int Slot; public string Mode; public bool[] Edges; public PointF Start; public long[] Orig, Rect; }

        public StatePanel(MainForm app, long num, long sn)
        {
            this.app = app; this.num = num; this.sn = sn;
            Build();
        }

        DatFile D => app.D;
        DatState St => app.D.Items[num].States[sn];
        bool Old => D.Fmt == "e2150" || D.Fmt == "ks";

        void Build()
        {
            var head = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.Controls.Add(G.Heading($"{app.ItemText(num)}  /  {app.StateText(num, sn)}"), 0, 0);
            var hb = G.Row(G.Btn("Item settings", () => app.SelectNode($"item:{num}")), G.Btn("Delete state", app.CmdDelState), G.Btn("Duplicate state", app.CmdDupState));
            hb.Dock = DockStyle.None;
            hb.Anchor = AnchorStyles.Right;
            head.Controls.Add(hb, 1, 0);
            Add(head);
            var body = new SplitContainer { Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel1 };
            Fill(body);
            var left = new VStack { AutoScroll = true };
            body.Panel1.Controls.Add(left);
            var right = new VStack();
            body.Panel2.Controls.Add(right);
            bool placed = false;
            body.SizeChanged += (s, e) =>
            {
                if (placed || body.Width < 700) return;
                placed = true;
                try { body.SplitterDistance = 480; } catch (Exception) { }
            };
            BuildProps(left);
            BuildSlots(left);
            BuildView(right);
            RefreshSlots();
            SelectSlot(FirstUsedSlot());
        }

        int FirstUsedSlot()
        {
            for (int i = 0; i < St.Slots.Count; i++) if (St.Slots[i].Mt != 0xFFFF) return i;
            return 0;
        }

        // ================================================================== properties
        void SetSt(Action<DatState> set, bool tree = false) => app.Modify(() => set(app.D.Items[num].States[sn]), tree: tree);

        void BuildProps(VStack parent)
        {
            var d = D; var st = St;
            var pf = new Grid();
            parent.Add(G.Group("Properties", pf));
            app.Entry(pf, 0, "State number:", $"0x{sn:X2}", Renumber, 8);
            pf.Put(G.Lbl("Font:"), 1, 0);
            var descs = app.FontDescriptions();
            var fonts = new List<string> { "(none)" };
            for (int i = 0; i < d.Fonts.Count; i++)
                fonts.Add($"{i}: {d.Fonts[i].Name} {d.Fonts[i].Size} / {d.Fonts[i].Weight}" + (descs.TryGetValue(i, out string ds) && ds.Length > 0 ? $"  - {ds}" : ""));
            var fc = G.Combo(fonts, 340);
            fc.DropDownWidth = 520;
            fc.SelectedIndex = st.Font >= 0 && st.Font < d.Fonts.Count ? (int)st.Font + 1 : 0;
            fc.SelectionChangeCommitted += (s, e) => { long v = fc.SelectedIndex - 1; SetSt(x => x.Font = v); };
            pf.Put(fc, 1, 1, 3);
            int r = 2;
            if (d.Fmt != "tw2")
            {
                pf.Put(G.Lbl("Sound:"), r, 0);
                var sounds = new List<string> { "(none)" };
                for (int i = 0; i < d.Sounds.Count; i++) sounds.Add($"{i}: {d.Sounds[i]}");
                var sc = G.Combo(sounds, 260);
                sc.SelectedIndex = st.Sound >= 0 && st.Sound < d.Sounds.Count ? (int)st.Sound + 1 : 0;
                sc.SelectionChangeCommitted += (s, e) => { long v = sc.SelectedIndex - 1; SetSt(x => x.Sound = v); };
                pf.Put(sc, r, 1, 3);
                r++;
            }
            if (Fmt.IsNew(d.Fmt))
            {
                app.ColourEntry(pf, r, 0, "FaceColor:", st.FaceColor, v => SetSt(x => x.FaceColor = v));
                var pb = G.Row();
                pb.Dock = DockStyle.None;
                foreach (var p in app.Names.FaceColorPresets(d.Fmt))
                {
                    long val = p.Long("value");
                    var b = G.Btn(p.Str("name"), () => { SetSt(x => x.FaceColor = val & 0xFFFFFFFFL); app.ShowNode(app.CurrentNode); });
                    string tip = $"{p.Str("name")}: {p.Str("desc")} ({G.Hex32(val)})";
                    b.MouseEnter += (s, e) => app.SetStatus(tip);
                    pb.Controls.Add(b);
                }
                pf.Put(pb, r + 1, 1, 3);
                r += 2;
                app.Entry(pf, r++, "Particle file:", st.Particle, t => SetSt(x => x.Particle = t.Trim(), tree: true), 40);
            }
            if (d.Fmt == "tw2")
            {
                app.ColourEntry(pf, r++, 0, "TextColor:", st.FaceColor2, v => SetSt(x => x.FaceColor2 = v));
                app.Entry(pf, r++, "TextTexture:", st.Texture, t => SetSt(x => x.Texture = t.Trim()), 40);
                app.Entry(pf, r++, "TextTextureParam:", st.TextureMargin.ToString(CultureInfo.InvariantCulture), t => { long v = G.ParseInt(t) & 0xFFFFFFFFL; SetSt(x => x.TextureMargin = v); });
            }
            var rv = new VStack { AutoSize = true, Dock = DockStyle.Top };
            recList = new ListBox { IntegralHeight = false, Height = 80, Dock = DockStyle.Top, HorizontalScrollbar = true };
            rv.Add(recList);
            RefreshRecords();
            rv.Add(G.Row(G.Btn("Add", AddRecord), G.Btn("Edit", EditRecord), G.Btn("Delete", DelRecord)));
            recList.DoubleClick += (s, e) => EditRecord();
            parent.Add(G.Group("Property records", rv));
        }

        void Renumber(string txt)
        {
            long m = G.ParseInt(txt);
            var it = D.Items[num];
            if (m == sn) return;
            if (m < 0 || m > 0xFFFE) throw new UserError("state number must be 0..65534");
            if (it.States.ContainsKey(m)) throw new UserError($"state {m} already exists");
            long n = num, s0 = sn;
            if (app.Modify(() =>
            {
                var itm = app.D.Items[n];
                var st = itm.States[s0];
                itm.States.Remove(s0);
                itm.States[m] = st;
                for (int k = 0; k < itm.FileOrder.Count; k++) if (itm.FileOrder[k] == s0) itm.FileOrder[k] = m;
            }, tree: true)) app.SelectNode($"state:{n}:{m}");
        }

        void RefreshRecords()
        {
            recList.Items.Clear();
            foreach (var rec in St.Records)
            {
                string name = G.RecordKeys.TryGetValue(rec.Key, out string nm) ? nm : $"Effect:{rec.Key}";
                string txt = string.Join(",", rec.Values.Select(v => v >= 0x10000 || rec.Key == 0 ? G.Hex32(v) : v.ToString(CultureInfo.InvariantCulture)));
                string preset;
                if (rec.Key == 4) preset = app.Names.LightName(rec.Values, D.Fmt);
                else if (rec.Key == 1 || rec.Key == 2)   // Selection / Uses: numbers of other items
                    preset = string.Join(", ", rec.Values.Select(v => D.Items.ContainsKey(v) ? $"{v} {app.ItemName(v)}".Trim() : $"{v} (missing item)"));
                else if (rec.Key == 0 && rec.Values.Count > 0) preset = G.ToHtml(rec.Values[0], app.ColourOrder());
                else preset = "";
                recList.Items.Add($"{name} = {txt}" + (preset.Length > 0 ? $"   [{preset}]" : ""));
            }
        }

        void SetRecords(List<Record> recs)
        {
            long n = num, s0 = sn;
            if (app.Modify(() =>
            {
                var st = app.D.Items[n].States[s0];
                st.Records = recs.OrderBy(x => x.Key).ToList();
                st.HasBlob = true;
            })) RefreshRecords();
        }

        void AddRecord()
        {
            var res = RecordDialog.Ask(app, 0, new List<long>(), app.Names.LightPresets(D.Fmt), app.ColourOrder());
            if (res == null) return;
            var recs = St.Records.Where(x => x.Key != res.Key).Select(x => x.Clone()).ToList();
            recs.Add(res);
            SetRecords(recs);
        }

        void EditRecord()
        {
            int s = recList.SelectedIndex;
            if (s < 0) return;
            var cur = St.Records[s];
            var res = RecordDialog.Ask(app, cur.Key, new List<long>(cur.Values), app.Names.LightPresets(D.Fmt), app.ColourOrder());
            if (res == null) return;
            var recs = St.Records.Where((x, k) => k != s && x.Key != res.Key).Select(x => x.Clone()).ToList();
            recs.Add(res);
            SetRecords(recs);
        }

        void DelRecord()
        {
            int s = recList.SelectedIndex;
            if (s >= 0) SetRecords(St.Records.Where((x, k) => k != s).Select(x => x.Clone()).ToList());
        }

        // ================================================================== slots
        void BuildSlots(VStack parent)
        {
            var d = D;
            var sv = new VStack { AutoSize = true, Dock = DockStyle.Top };
            var cols = new List<(string, int)> { ("slot", 112), ("material", 150) };
            if (Old) cols.Add(("tile", 40));
            cols.AddRange(new[] { ("L", 46), ("T", 46), ("R", 46), ("B", 46) });
            slotTv = G.Table(cols.ToArray());
            slotTv.Height = 200;
            slotTv.Dock = DockStyle.Top;
            slotTv.SelectedIndexChanged += (s, e) => { if (slotTv.SelectedIndices.Count > 0 && slotTv.SelectedIndices[0] != selSlot) SelectSlot(slotTv.SelectedIndices[0]); };
            sv.Add(slotTv);
            var ef = new Grid();
            ef.Put(G.Lbl("Material:"), 0, 0);
            matCb = G.Combo(new[] { G.NoMaterial }.Concat(d.Materials.Select((m, i) => $"{i}: {G.BaseName(m)}")), 300);
            matCb.DropDownWidth = 420;
            ef.Put(matCb, 0, 1, 9);
            int c = 0;
            foreach (var k in new[] { "tile", "L", "T", "R", "B" }) vars[k] = new TextBox { Width = 46, Margin = new Padding(2) };
            if (Old) { ef.Put(G.Lbl("Tile:"), 1, c); ef.Put(vars["tile"], 1, c + 1); c += 2; }
            foreach (var k in new[] { "L", "T", "R", "B" })
            {
                ef.Put(G.Lbl($"{k}:"), 1, c);
                ef.Put(vars[k], 1, c + 1);
                vars[k].KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplySlot(); } };
                c += 2;
            }
            vars["tile"].KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplySlot(); } };
            sv.Add(ef);
            sv.Add(G.Row(G.Btn("Apply", ApplySlot), G.Btn("Clear slot", ClearSlot), G.Btn("Copy slots from...", CopySlotsFrom)));
            string note = Old ? "Rectangles are in tile coordinates (Earth 2150 / KnightShift)." : "Rectangles are in texture pixels.";
            if (St.Particle.Length > 0) note += " This state has a particle file: the game ignores its rectangles.";
            sv.Add(G.Wrapped(note + " Slots without a material keep the filled-in rectangle.", 440, G.Grey));
            parent.Add(G.Group("Slots (9-slice)", sv));
        }

        void RefreshSlots()
        {
            var d = D;
            slotTv.BeginUpdate();
            slotTv.Items.Clear();
            for (int i = 0; i < St.Slots.Count; i++)
            {
                var s = St.Slots[i];
                string mat, tile;
                if (s.Mt == 0xFFFF) { mat = "-"; tile = ""; }
                else
                {
                    int m = s.Mt & 0xFF;
                    mat = m < d.Materials.Count ? G.BaseName(d.Materials[m]) : $"#{m}?";
                    tile = (s.Mt >> 8).ToString();
                }
                var vals = new List<string> { $"{i + 1} {G.SlotNames[i]}", mat };
                if (Old) vals.Add(tile);
                vals.AddRange(s.R.Select(v => v.ToString(CultureInfo.InvariantCulture)));
                var li = new ListViewItem(vals.ToArray()) { UseItemStyleForSubItems = true };
                slotTv.Items.Add(li);
            }
            slotTv.EndUpdate();
            if (selSlot < slotTv.Items.Count) { slotTv.Items[selSlot].Selected = true; }
        }

        void SelectSlot(int i)
        {
            selSlot = i;
            if (i < slotTv.Items.Count && !slotTv.Items[i].Selected) { slotTv.Items[i].Selected = true; slotTv.Items[i].Focused = true; }
            var s = St.Slots[i];
            matCb.SelectedIndex = s.Mt == 0xFFFF ? 0 : Math.Min(matCb.Items.Count - 1, (s.Mt & 0xFF) + 1);
            vars["tile"].Text = s.Mt == 0xFFFF ? "" : (s.Mt >> 8).ToString();
            var k = "LTRB";
            for (int j = 0; j < 4; j++) vars[k[j].ToString()].Text = s.R[j].ToString(CultureInfo.InvariantCulture);
            if (s.Mt != 0xFFFF && (s.Mt & 0xFF) != viewMat) SetViewMaterial(s.Mt & 0xFF);
            else canvas?.Invalidate();
        }

        void ApplySlot()
        {
            long[] r;
            int mt;
            try
            {
                r = "LTRB".Select(k => G.ParseInt(vars[k.ToString()].Text)).ToArray();
                if (r.Any(v => v < 0 || v > 0xFFFF)) throw new UserError("coordinates must be 0..65535");
                int idx = matCb.SelectedIndex;
                if (idx <= 0) mt = 0xFFFF;
                else
                {
                    long tile = Old ? G.ParseInt(vars["tile"].Text.Length > 0 ? vars["tile"].Text : "0") : 0;
                    if (tile < 0 || tile > 255) throw new UserError("tile must be 0..255");
                    mt = Old ? ((int)tile << 8) | (idx - 1) : idx - 1;
                }
            }
            catch (UserError e) { G.Error(app, "Invalid slot", e.Message); return; }
            SetSlot(selSlot, mt, r);
        }

        public void SetSlot(int i, int mt, long[] r)
        {
            long n = num, s0 = sn;
            int oldMt = St.Slots[i].Mt;
            // like the compiler: the rectangles of empty slots follow the slots with a material. Not in
            // states with a particle file: the compiler would zero all their rectangles (the game ignores
            // them), which would undo the edit the user has just made.
            var (flags, margins) = (mt != 0xFFFF || oldMt != 0xFFFF) && St.Particle.Length == 0 ? app.FillSettings(num) : (null, null);
            if (app.Modify(() =>
            {
                var st = app.D.Items[n].States[s0];
                st.Slots[i] = new Slot(mt, (long[])r.Clone());
                if (flags != null) app.RederiveItem(n, flags.Value, margins, s0);
            }))
            {
                RefreshSlots();
                SelectSlot(i);
                app.SetTreeText($"state:{num}:{sn}", app.StateLabel(num, sn));
                UpdatePreview();
            }
        }

        void ClearSlot() => SetSlot(selSlot, 0xFFFF, new long[] { 0, 0, 0, 0 });

        void CopySlotsFrom()
        {
            string t = InputDialog.Ask(app, "Copy slots", "Copy all 9 slots from (item:state, e.g. 12:0x04 or 0x04):");
            if (string.IsNullOrEmpty(t)) return;
            DatState src;
            try
            {
                long n, s;
                int k = t.IndexOf(':');
                if (k >= 0) { n = G.ParseInt(t.Substring(0, k)); s = G.ParseInt(t.Substring(k + 1)); }
                else { n = num; s = G.ParseInt(t); }
                src = app.D.Items[n].States[s];
            }
            catch (Exception e) when (e is UserError || e is KeyNotFoundException) { G.Error(app, G.AppName, "No such item/state."); return; }
            var slots = src.Slots.Select(x => x.Clone()).ToList();
            long nn = num, s0 = sn;
            if (app.Modify(() => app.D.Items[nn].States[s0].Slots = slots))
            {
                RefreshSlots();
                SelectSlot(FirstUsedSlot());
            }
        }

        // ================================================================== texture view
        void BuildView(VStack parent)
        {
            var d = D;
            viewCb = G.Combo(d.Materials.Select((m, i) => $"{i}: {m}"), 260);
            viewCb.DropDownWidth = 520;
            viewCb.SelectionChangeCommitted += (s, e) => SetViewMaterial(viewCb.SelectedIndex);
            zoomLbl = G.Lbl("x2");
            texLbl = G.Lbl("", G.Grey);
            var tb = G.Row(G.Lbl("Texture:"), viewCb, G.Btn("-", () => SetZoom(zoom - 1), 28), zoomLbl, G.Btn("+", () => SetZoom(zoom + 1), 28), texLbl);
            parent.Add(tb);
            canvasHost = new NoJumpPanel { AutoScroll = true, BackColor = Color.FromArgb(0x20, 0x20, 0x20), BorderStyle = BorderStyle.FixedSingle, MinimumSize = new Size(100, 200) };
            canvas = new TexCanvas(this) { Location = Point.Empty, Cursor = Cursors.Cross, TabStop = true };
            canvasHost.Controls.Add(canvas);
            parent.Fill(canvasHost);
            parent.Add(G.Wrapped("Click a rectangle to select its slot, drag to move it, drag a handle to resize. Arrow keys move by 1 px, Shift+arrows resize. Ctrl+wheel zooms.", 640, G.Grey));
            var pv = new VStack { AutoSize = true, Dock = DockStyle.Top };
            pw = new TextBox { Text = "200", Width = 54 };
            ph = new TextBox { Text = "80", Width = 54 };
            pw.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UpdatePreview(); } };
            ph.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UpdatePreview(); } };
            pv.Add(G.Row(G.Lbl("Width:"), pw, G.Lbl("Height:"), ph, G.Btn("Render", UpdatePreview)));
            preview = new PictureBox { SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.FromArgb(0x30, 0x30, 0x30), Height = 124, Dock = DockStyle.Top };
            pv.Add(preview);
            parent.Add(G.Group("9-slice preview (approximate)", pv));
            var mats = St.Slots.Where(x => x.Mt != 0xFFFF).Select(x => x.Mt & 0xFF).ToList();
            SetViewMaterial(mats.Count > 0 ? mats[0] : 0);
            UpdatePreview();
        }

        void SetZoom(int z)
        {
            zoom = Math.Max(1, Math.Min(8, z));
            zoomLbl.Text = $"x{zoom}";
            Redraw();
        }

        void SetViewMaterial(int m)
        {
            var d = D;
            if (d.Materials.Count == 0) { viewMat = null; Redraw(); return; }
            m = Math.Max(0, Math.Min(m, d.Materials.Count - 1));
            viewMat = m;
            if (viewCb.SelectedIndex != m) viewCb.SelectedIndex = m;
            var (tex, src) = app.TextureFor(m);
            while (zoom > 1 && tex.Width * zoom > 4096) zoom--;
            zoomLbl.Text = $"x{zoom}";
            string info;
            if (src == null) info = "texture not found - placeholder";
            else if (MainForm.IsColourMap(src)) info = $"{System.IO.Path.GetFileName(src)}  (COLOUR MAP - texture not found or View option on)";
            else info = System.IO.Path.GetFileName(src);
            texLbl.Text = $"{tex.Width}x{tex.Height}  {info}";
            texLbl.ForeColor = src == null || MainForm.IsColourMap(src) ? G.WarnColour : G.Grey;
            Redraw();
        }

        void Redraw()
        {
            if (canvas == null) return;
            if (viewMat == null) { canvas.Size = new Size(1, 1); canvas.Invalidate(); return; }
            var tex = app.TextureFor(viewMat.Value).tex;
            canvas.Size = new Size(tex.Width * zoom, tex.Height * zoom);
            canvas.Invalidate();
        }

        Dictionary<int, long[]> SlotRects()
        {
            var o = new Dictionary<int, long[]>();
            for (int i = 0; i < St.Slots.Count; i++)
            {
                var s = St.Slots[i];
                if (s.Mt != 0xFFFF && (s.Mt & 0xFF) == viewMat)
                    o[i] = drag != null && drag.Slot == i ? drag.Rect : app.SlotAbs(s.Mt, s.R);
            }
            return o;
        }

        List<PointF> Handles(long[] a)
        {
            float z = zoom;
            var xs = new[] { a[0] * z, (a[0] + a[2]) * z / 2, a[2] * z };
            var ys = new[] { a[1] * z, (a[1] + a[3]) * z / 2, a[3] * z };
            var o = new List<PointF>();
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) if (!(i == 1 && j == 1)) o.Add(new PointF(xs[i], ys[j]));
            return o;
        }

        internal void PaintView(Graphics g, Rectangle clip)
        {
            if (viewMat == null || app.D == null) return;
            int m = viewMat.Value;
            var tex = app.TextureFor(m).tex;
            var bmp = app.BitmapFor(m);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            // draw only the visible part (large textures at a high zoom)
            int sx0 = Math.Max(0, clip.Left / zoom), sy0 = Math.Max(0, clip.Top / zoom);
            int sx1 = Math.Min(tex.Width, clip.Right / zoom + 1), sy1 = Math.Min(tex.Height, clip.Bottom / zoom + 1);
            if (sx1 > sx0 && sy1 > sy0)
                g.DrawImage(bmp, new Rectangle(sx0 * zoom, sy0 * zoom, (sx1 - sx0) * zoom, (sy1 - sy0) * zoom), new Rectangle(sx0, sy0, sx1 - sx0, sy1 - sy0), GraphicsUnit.Pixel);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            if (Old)
            {
                int t = app.TileSize(m) * zoom;
                using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255)) { DashPattern = new float[] { 2, 4 } })
                {
                    for (int x = t; x < tex.Width * zoom; x += t) g.DrawLine(pen, x, 0, x, tex.Height * zoom);
                    for (int y = t; y < tex.Height * zoom; y += t) g.DrawLine(pen, 0, y, tex.Width * zoom, y);
                }
            }
            g.SmoothingMode = SmoothingMode.None;
            using (var font = new Font(SystemFonts.MessageBoxFont.FontFamily, 8.5f, FontStyle.Bold))
                foreach (var kv in SlotRects())
                {
                    var a = kv.Value;
                    bool sel = kv.Key == selSlot;
                    var col = G.SlotColours[kv.Key];
                    using (var pen = new Pen(col, sel ? 3 : 1))
                        g.DrawRectangle(pen, a[0] * zoom, a[1] * zoom, Math.Max(0, (a[2] - a[0]) * zoom), Math.Max(0, (a[3] - a[1]) * zoom));
                    using (var br = new SolidBrush(col)) g.DrawString((kv.Key + 1).ToString(), font, br, a[0] * zoom + 3, a[1] * zoom + 2);
                    if (sel)
                        using (var br = new SolidBrush(col))
                            foreach (var h in Handles(a))
                            {
                                g.FillRectangle(br, h.X - 3, h.Y - 3, 6, 6);
                                g.DrawRectangle(Pens.Black, h.X - 3, h.Y - 3, 6, 6);
                            }
                }
        }

        internal void OnMove(MouseEventArgs e)
        {
            if (drag != null && (e.Button & MouseButtons.Left) != 0) { OnDrag(e); return; }
            app.SetStatus($"Texture x={e.X / zoom} y={e.Y / zoom}");
        }

        internal void OnPress(MouseEventArgs e)
        {
            canvas.Focus();
            if (e.Button != MouseButtons.Left) return;
            float x = e.X, y = e.Y;
            var rects = SlotRects();
            float z = zoom;
            string mode = null; bool[] edges = null; int? slot = null;
            if (rects.TryGetValue(selSlot, out var a))
                foreach (var h in Handles(a))
                    if (Math.Abs(h.X - x) <= HandleSize + 1 && Math.Abs(h.Y - y) <= HandleSize + 1)
                    {
                        edges = new[] { Math.Abs(h.X - a[0] * z) < 1, Math.Abs(h.Y - a[1] * z) < 1, Math.Abs(h.X - a[2] * z) < 1, Math.Abs(h.Y - a[3] * z) < 1 };
                        mode = "resize"; slot = selSlot;
                        break;
                    }
            if (mode == null)
            {
                var inside = rects.Where(kv => kv.Value[0] * z <= x && x <= kv.Value[2] * z && kv.Value[1] * z <= y && y <= kv.Value[3] * z).Select(kv => kv.Key).ToList();
                if (inside.Contains(selSlot)) slot = selSlot;
                else if (inside.Count > 0) slot = inside.OrderBy(i => (rects[i][2] - rects[i][0]) * (rects[i][3] - rects[i][1])).First();
                if (slot != null)
                {
                    mode = "move";
                    if (slot != selSlot) { SelectSlot(slot.Value); rects = SlotRects(); }
                }
            }
            if (mode == null) { drag = null; return; }
            drag = new Drag { Slot = slot.Value, Mode = mode, Edges = edges, Start = new PointF(x, y), Orig = (long[])rects[slot.Value].Clone(), Rect = (long[])rects[slot.Value].Clone() };
        }

        static long PyRound(double v) => (long)Math.Round(v, MidpointRounding.ToEven);

        void OnDrag(MouseEventArgs e)
        {
            long dx = PyRound((e.X - drag.Start.X) / zoom), dy = PyRound((e.Y - drag.Start.Y) / zoom);
            var o = drag.Orig;
            var tex = app.TextureFor(viewMat.Value).tex;
            long[] r;
            if (drag.Mode == "move")
            {
                long w = o[2] - o[0], h = o[3] - o[1];
                long nx = Math.Max(0, Math.Min(o[0] + dx, tex.Width - w)), ny = Math.Max(0, Math.Min(o[1] + dy, tex.Height - h));
                r = new[] { nx, ny, nx + w, ny + h };
            }
            else
            {
                r = (long[])o.Clone();
                if (drag.Edges[0]) r[0] = Math.Max(0, Math.Min(o[0] + dx, o[2]));
                if (drag.Edges[1]) r[1] = Math.Max(0, Math.Min(o[1] + dy, o[3]));
                if (drag.Edges[2]) r[2] = Math.Min(tex.Width, Math.Max(o[2] + dx, r[0]));
                if (drag.Edges[3]) r[3] = Math.Min(tex.Height, Math.Max(o[3] + dy, r[1]));
            }
            drag.Rect = r;
            canvas.Invalidate();
            app.SetStatus($"Slot {drag.Slot + 1}: L={r[0]} T={r[1]} R={r[2]} B={r[3]} ({r[2] - r[0]}x{r[3] - r[1]})");
        }

        internal void OnRelease(MouseEventArgs e)
        {
            if (drag == null) return;
            var dr = drag; drag = null;
            if (dr.Rect.SequenceEqual(dr.Orig)) { canvas.Invalidate(); return; }
            CommitAbs(dr.Slot, dr.Rect);
        }

        void CommitAbs(int slot, long[] a)
        {
            int mt; long[] r;
            try { (mt, r) = app.AbsToSlot(viewMat.Value, a); }
            catch (UserError e) { G.Error(app, G.AppName, e.Message); canvas.Invalidate(); return; }
            SetSlot(slot, mt, r);
            canvas.Focus();
        }

        internal bool OnKey(Keys key)
        {
            int dx = 0, dy = 0;
            switch (key & Keys.KeyCode)
            {
                case Keys.Left: dx = -1; break;
                case Keys.Right: dx = 1; break;
                case Keys.Up: dy = -1; break;
                case Keys.Down: dy = 1; break;
                default: return false;
            }
            Nudge(dx, dy, (key & Keys.Shift) != 0);
            return true;
        }

        void Nudge(int dx, int dy, bool resize)
        {
            var rects = SlotRects();
            if (!rects.TryGetValue(selSlot, out var a0)) return;
            var a = (long[])a0.Clone();
            if (resize) { a[2] = Math.Max(a[0], a[2] + dx); a[3] = Math.Max(a[1], a[3] + dy); }
            else
            {
                a = new[] { a[0] + dx, a[1] + dy, a[2] + dx, a[3] + dy };
                if (a[0] < 0 || a[1] < 0) return;
            }
            CommitAbs(selSlot, a);
        }

        internal void OnWheelZoom(int delta) => SetZoom(zoom + (delta > 0 ? 1 : -1));

        // ================================================================== preview
        void UpdatePreview()
        {
            if (IsDisposed || preview == null) return;
            long W, H;
            try { W = Math.Max(1, Math.Min(1024, G.ParseInt(pw.Text))); H = Math.Max(1, Math.Min(1024, G.ParseInt(ph.Text))); }
            catch (UserError) { return; }
            var slots = new (Texture tex, long[] a)?[9];
            for (int i = 0; i < 9 && i < St.Slots.Count; i++)
            {
                var s = St.Slots[i];
                var a = app.SlotAbs(s.Mt, s.R);
                if (a != null && a[2] > a[0] && a[3] > a[1]) slots[i] = (app.TextureFor(s.Mt & 0xFF).tex, a);
            }
            long Sz(int i, int k) => slots[i] != null ? slots[i].Value.a[k + 2] - slots[i].Value.a[k] : 0;
            long lw = Math.Max(Sz(5, 0), Math.Max(Sz(3, 0), Sz(7, 0)));
            long rw = Math.Max(Sz(6, 0), Math.Max(Sz(4, 0), Sz(8, 0)));
            long th = Math.Max(Sz(5, 1), Math.Max(Sz(1, 1), Sz(6, 1)));
            long bh = Math.Max(Sz(7, 1), Math.Max(Sz(2, 1), Sz(8, 1)));
            W = Math.Max(W, lw + rw + 1); H = Math.Max(H, th + bh + 1);
            int w = (int)W, h = (int)H;
            var regions = new Dictionary<int, long[]>
            {
                { 0, new[] { lw, th, W - rw, H - bh } }, { 1, new[] { lw, 0, W - rw, th } }, { 2, new[] { lw, H - bh, W - rw, H } },
                { 3, new[] { 0, th, lw, H - bh } }, { 4, new[] { W - rw, th, W, H - bh } }, { 5, new[] { 0, 0, lw, th } },
                { 6, new[] { W - rw, 0, W, th } }, { 7, new[] { 0, H - bh, lw, H } }, { 8, new[] { W - rw, H - bh, W, H } },
            };
            var buf = new byte[w * h * 3];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte v = (byte)((((x / 8) + (y / 8)) & 1) != 0 ? 70 : 50);
                    int o = (y * w + x) * 3;
                    buf[o] = buf[o + 1] = buf[o + 2] = v;
                }
            foreach (var kv in regions)
            {
                long x0 = kv.Value[0], y0 = kv.Value[1], x1 = kv.Value[2], y1 = kv.Value[3];
                if (slots[kv.Key] == null || x1 <= x0 || y1 <= y0) continue;
                var (tex, a) = slots[kv.Key].Value;
                long sw = a[2] - a[0], sh = a[3] - a[1], rw2 = x1 - x0, rh2 = y1 - y0;
                for (long y = y0; y < y1; y++)
                {
                    long sy = Math.Min(tex.Height - 1, a[1] + (y - y0) * sh / rh2);
                    for (long x = x0; x < x1; x++)
                    {
                        long sx = Math.Min(tex.Width - 1, a[0] + (x - x0) * sw / rw2);
                        if (sx < 0 || sy < 0) continue;
                        long s = (sy * tex.Width + sx) * 4;
                        int al = tex.Rgba[s + 3];
                        long o = (y * w + x) * 3;
                        for (int k = 0; k < 3; k++) buf[o + k] = (byte)((tex.Rgba[s + k] * al + buf[o + k] * (255 - al)) / 255);
                    }
                }
            }
            int scale = w <= 400 && h <= 150 ? 2 : 1;
            var bmp = new Bitmap(w * scale, h * scale, PixelFormat.Format24bppRgb);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var row = new byte[data.Stride];
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    int o = ((y / scale) * w + x / scale) * 3;
                    row[x * 3] = buf[o + 2]; row[x * 3 + 1] = buf[o + 1]; row[x * 3 + 2] = buf[o];
                }
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, data.Stride);
            }
            bmp.UnlockBits(data);
            var old = preview.Image;
            preview.Image = bmp;
            old?.Dispose();
            preview.Height = Math.Min(bmp.Height, 300) + 4;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) preview?.Image?.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Scrollable host that does not jump back to the top-left corner when the canvas gets the focus.</summary>
        class NoJumpPanel : Panel
        {
            protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;
        }

        /// <summary>The texture view (drawn by the panel).</summary>
        class TexCanvas : Control
        {
            readonly StatePanel p;
            public TexCanvas(StatePanel p)
            {
                this.p = p;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
                BackColor = Color.FromArgb(0x20, 0x20, 0x20);
            }
            protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); p.PaintView(e.Graphics, e.ClipRectangle); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); p.OnPress(e); }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); p.OnMove(e); }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) p.OnRelease(e); }
            protected override bool IsInputKey(Keys k) => (k & Keys.KeyCode) is Keys.Left || (k & Keys.KeyCode) is Keys.Right || (k & Keys.KeyCode) is Keys.Up || (k & Keys.KeyCode) is Keys.Down || base.IsInputKey(k);
            protected override void OnKeyDown(KeyEventArgs e) { if (p.OnKey(e.KeyData)) e.Handled = true; else base.OnKeyDown(e); }
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if ((ModifierKeys & Keys.Control) != 0) { p.OnWheelZoom(e.Delta); if (e is HandledMouseEventArgs h) h.Handled = true; return; }
                base.OnMouseWheel(e);
            }
            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        }
    }
}

// Panels of the main window (everything except the state panel) and the texture lookup.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace InterfaceTools.Gui
{
    using Font = System.Drawing.Font;
    using TextUtil = InterfaceTools.Text;

    public partial class MainForm
    {
        static int? Sel(ListView lv) => lv.SelectedIndices.Count > 0 ? lv.SelectedIndices[0] : (int?)null;

        static void SelectRow(ListView lv, int i)
        {
            if (i < 0 || i >= lv.Items.Count) return;
            lv.Items[i].Selected = true;
            lv.Items[i].Focused = true;
            lv.EnsureVisible(i);
        }

        // ================================================================== header
        Control PanelHeader()
        {
            var d = D;
            var v = new VStack();
            v.Add(G.Heading("Header"));
            var f = v.Add(new Grid { Padding = new Padding(8) });
            f.Put(G.Lbl("Format:"), 0, 0); f.Put(G.Lbl(d.Fmt == Fmt.E2150 ? $"{Fmt.Names[d.Fmt]} (also {Fmt.E2150Family})" : Fmt.Names[d.Fmt]), 0, 1);
            f.Put(G.Lbl("File:"), 1, 0); f.Put(G.Lbl(FilePath ?? "(not saved yet)"), 1, 1, 3);
            int r = 2;
            if (!Fmt.IsTw(d.Fmt))
            {
                Entry(f, r++, "Base width:", d.BaseW.ToString(CultureInfo.InvariantCulture), t => { long x = G.ParseInt(t); Modify(() => D.BaseW = x); });
                Entry(f, r++, "Base height:", d.BaseH.ToString(CultureInfo.InvariantCulture), t => { long x = G.ParseInt(t); Modify(() => D.BaseH = x); });
                Entry(f, r++, "Aspect ratio X:", Decompiler.FmtFloat(d.ScaleX), t => { float x = G.ParseFloat(t); Modify(() => D.ScaleX = x); }, tip: "'Scale' directive, 3rd value");
                Entry(f, r++, "Aspect ratio Y:", Decompiler.FmtFloat(d.ScaleY), t => { float x = G.ParseFloat(t); Modify(() => D.ScaleY = x); }, tip: "'Scale' directive, 4th value");
            }
            if (d.Fmt != "e2150")
            {
                Action<bool> SetBit(long bit) => on => Modify(() => D.GlobalFlags = on ? (D.GlobalFlags | bit) : (D.GlobalFlags & ~bit), refresh: true);
                f.Put(Check("DefaultScaleBorder (global flag 0x100)", (d.GlobalFlags & 0x100) != 0, SetBit(0x100)), r++, 0, 3);
                f.Put(Check("DefaultScaleBackground (global flag 0x200)", (d.GlobalFlags & 0x200) != 0, SetBit(0x200)), r++, 0, 3);
                Entry(f, r++, "Global flags (raw):", G.Hex32(d.GlobalFlags), t => { long x = G.ParseInt(t) & 0xFFFFFFFFL; Modify(() => D.GlobalFlags = x, refresh: true); });
                f.Put(G.Lbl("Global flags are also stored in the flags of every item (they are OR-ed in by the compiler).", G.Grey), r++, 0, 4);
            }
            int states = d.Items.Values.Sum(i => i.States.Count);
            var lines = new List<string> { $"Items: {d.Items.Count}   States: {states}   Materials: {d.Materials.Count}   Fonts: {d.Fonts.Count}   Sounds: {d.Sounds.Count}" };
            string namesSrc = Names.UserSource != null ? $"from {Names.UserSource}" : (namesVariant.Length > 0 ? $"built-in, version: {namesVariant}" : "built-in");
            lines.Add($"Item/state names: {namesSrc}");
            if (Fmt.IsNew(d.Fmt))
            {
                lines.Add($"CustomData entries: {d.CustomData.Count}   DialogTemplates: {d.Dialogs.Count}");
                lines.Add($"Identifier names: {ResPath ?? "not loaded (File -> Load identifier names)"}");
            }
            foreach (var n in d.ItemRepeatOrder.Where(d.ItemRepeats.ContainsKey).Distinct()) lines.Add($"Item {n} is stored {d.ItemRepeats[n] + 1} times in the file (identical copies, kept on save).");
            lines.AddRange(d.ItemAnomalies.Select(a => $"Warning: {a}"));
            var info = G.Lbl(string.Join("\n", lines));
            info.Margin = new Padding(12, 8, 8, 8);
            v.Add(info);
            return v;
        }

        // ================================================================== materials
        int[] MaterialUsage()
        {
            var use = new int[D.Materials.Count];
            foreach (var it in D.Items.Values)
                foreach (var st in it.States.Values)
                    foreach (var s in st.Slots)
                        if (s.Mt != 0xFFFF && (s.Mt & 0xFF) < use.Length) use[s.Mt & 0xFF]++;
            return use;
        }

        public void RemapMaterials(Dictionary<int, int> mapping)
        {
            foreach (var it in D.Items.Values)
                foreach (var st in it.States.Values)
                    for (int i = 0; i < st.Slots.Count; i++)
                        if (st.Slots[i].Mt != 0xFFFF) st.Slots[i] = new Slot((st.Slots[i].Mt & 0xFF00) | mapping[st.Slots[i].Mt & 0xFF], st.Slots[i].R);
        }

        Control PanelMaterials()
        {
            var d = D;
            bool old = d.Fmt == "e2150" || d.Fmt == "ks";
            var v = new VStack();
            v.Add(G.Heading("Materials (textures)"));
            var tv = old ? G.Table(("Name", 300), ("Used", 60), ("Tile", 50), ("Texture", 600)) : G.Table(("Name", 380), ("Used", 60), ("Texture", 640));
            tv.Margin = new Padding(8);
            v.Fill(tv);
            var use = MaterialUsage();
            for (int i = 0; i < d.Materials.Count; i++)
            {
                var (_, src) = TextureFor(i);
                var vals = new List<string> { d.Materials[i], use[i].ToString(CultureInfo.InvariantCulture) };
                if (old) vals.Add(TileSize(i).ToString(CultureInfo.InvariantCulture));
                vals.Add(src ?? "not found (placeholder)");
                tv.Items.Add(new ListViewItem(vals.ToArray()));
            }
            void Add()
            {
                string name = InputDialog.Ask(this, "Add material", "Material name / texture path:");
                if (!string.IsNullOrEmpty(name)) Modify(() => D.Materials.Add(name.Trim()), tree: true);
            }
            void Rename()
            {
                var i = Sel(tv); if (i == null) return;
                string name = InputDialog.Ask(this, "Rename material", "New name:", d.Materials[i.Value]);
                if (!string.IsNullOrEmpty(name)) Modify(() => D.Materials[i.Value] = name.Trim(), tree: true);
            }
            void Delete()
            {
                var i = Sel(tv); if (i == null) return;
                if (MaterialUsage()[i.Value] > 0) { G.Warn(this, "This material is used by slots and cannot be deleted."); return; }
                var mapping = Enumerable.Range(0, d.Materials.Count).Where(k => k != i.Value).ToDictionary(k => k, k => k < i.Value ? k : k - 1);
                Modify(() => { RemapMaterials(mapping); D.Materials.RemoveAt(i.Value); }, tree: true);
            }
            void Move(int delta)
            {
                var i = Sel(tv); if (i == null || i + delta < 0 || i + delta >= d.Materials.Count) return;
                int a = i.Value, j = a + delta;
                var mapping = Enumerable.Range(0, d.Materials.Count).ToDictionary(k => k, k => k);
                mapping[a] = j; mapping[j] = a;
                if (Modify(() => { RemapMaterials(mapping); var m = D.Materials; var t = m[a]; m[a] = m[j]; m[j] = t; }, tree: true))
                    AfterRebuild(c => { if (c is VStack vs) { var lv = vs.Controls.OfType<ListView>().FirstOrDefault(); if (lv != null) SelectRow(lv, j); } });
            }
            void SetTexture()
            {
                var i = Sel(tv); if (i == null) return;
                using (var dlg = new OpenFileDialog { Title = $"Texture for {d.Materials[i.Value]}", Filter = "Images (*.dds;*.tga;*.png;*.bmp)|*.dds;*.tga;*.png;*.bmp|All files (*.*)|*.*" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    Settings.MaterialFiles[d.Materials[i.Value].ToLowerInvariant()] = dlg.FileName;
                    Settings.Save();
                    CmdReloadTextures();
                }
            }
            void SetTile()
            {
                var i = Sel(tv); if (i == null) return;
                var t = InputDialog.AskInt(this, "Tile size", "Tile size in pixels (editor setting, not stored in the .dat):", TileSize(i.Value), 8, 1024);
                if (t == null) return;
                Settings.TileSizes[d.Materials[i.Value].ToLowerInvariant()] = t.Value;
                Settings.Save();
                ClearBitmaps();
                ShowNode("mat");
            }
            tv.DoubleClick += (s, e) => SetTexture();
            var bf = G.WrapRow(G.Btn("Add", Add), G.Btn("Rename", Rename), G.Btn("Delete", Delete), G.Btn("Move up", () => Move(-1)),
                               G.Btn("Move down", () => Move(1)), G.Btn("Set texture file...", SetTexture), G.Btn("Texture folders...", CmdTextureDirs));
            if (old) bf.Controls.Add(G.Btn("Tile size...", SetTile));
            bf.Margin = new Padding(8, 0, 8, 0);
            v.Add(bf);
            v.Add(G.Lbl("The order of materials matters: slots refer to materials by their position. Moving or deleting updates all slots automatically.", G.Grey));
            return v;
        }

        /// <summary>Runs an action on the panel after the next rebuild.</summary>
        void AfterRebuild(Action<Control> a)
        {
            BeginInvoke((Action)(() => { if (content.Controls.Count > 0) a(content.Controls[0]); }));
        }

        // ================================================================== sounds
        Control PanelSounds()
        {
            var d = D;
            var v = new VStack();
            v.Add(G.Heading("Sounds"));
            var lb = new ListBox { IntegralHeight = false, Margin = new Padding(8) };
            v.Fill(lb);
            var use = new int[d.Sounds.Count];
            foreach (var it in d.Items.Values) foreach (var st in it.States.Values) if (st.Sound >= 0 && st.Sound < use.Length) use[st.Sound]++;
            for (int i = 0; i < d.Sounds.Count; i++) lb.Items.Add($"{i}: {d.Sounds[i]}   (used by {use[i]} states)");
            int? Sl() => lb.SelectedIndex >= 0 ? lb.SelectedIndex : (int?)null;
            void Remap(Dictionary<int, int> mapping)
            {
                foreach (var it in D.Items.Values) foreach (var st in it.States.Values) if (st.Sound >= 0) st.Sound = mapping[(int)st.Sound];
            }
            void Add()
            {
                string n = InputDialog.Ask(this, "Add sound", "Sound name:");
                if (!string.IsNullOrEmpty(n)) Modify(() => D.Sounds.Add(n.Trim()), tree: true);
            }
            void Rename()
            {
                var i = Sl(); if (i == null) return;
                string n = InputDialog.Ask(this, "Rename sound", "New name:", d.Sounds[i.Value]);
                if (!string.IsNullOrEmpty(n)) Modify(() => D.Sounds[i.Value] = n.Trim(), tree: true);
            }
            void Delete()
            {
                var i = Sl(); if (i == null) return;
                if (use[i.Value] > 0) { G.Warn(this, "This sound is used by states and cannot be deleted."); return; }
                Modify(() => { Remap(Enumerable.Range(0, d.Sounds.Count).Where(k => k != i.Value).ToDictionary(k => k, k => k < i.Value ? k : k - 1)); D.Sounds.RemoveAt(i.Value); }, tree: true);
            }
            void Move(int delta)
            {
                var i = Sl(); if (i == null || i + delta < 0 || i + delta >= d.Sounds.Count) return;
                int a = i.Value, j = a + delta;
                var mapping = Enumerable.Range(0, d.Sounds.Count).ToDictionary(k => k, k => k);
                mapping[a] = j; mapping[j] = a;
                Modify(() => { Remap(mapping); var s = D.Sounds; var t = s[a]; s[a] = s[j]; s[j] = t; }, tree: true);
            }
            v.Add(G.WrapRow(G.Btn("Add", Add), G.Btn("Rename", Rename), G.Btn("Delete", Delete), G.Btn("Move up", () => Move(-1)), G.Btn("Move down", () => Move(1))));
            return v;
        }

        // ================================================================== fonts
        static long Signed8(long v) => v > 127 ? v - 256 : v;

        Control PanelFonts()
        {
            var d = D;
            bool tw2 = d.Fmt == "tw2";
            var v = new VStack();
            v.Add(G.Heading("Fonts"));
            var cols = new List<(string, int)> { ("#", 40), ("name", 130), ("size", 56), ("weight", 60), ("italic", 56), ("shadow x", 70), ("shadow y", 70), ("colour", 110), ("shadow colour", 110) };
            if (tw2) cols.Add(("lazy", 50));
            var descs = FontDescriptions();
            if (descs.Count > 0) cols.Add(("spreadsheet fonts", 320));
            var tv = G.Table(cols.ToArray());
            tv.Margin = new Padding(8);
            v.Fill(tv);
            var use = new int[d.Fonts.Count];
            foreach (var it in d.Items.Values) foreach (var st in it.States.Values) if (st.Font >= 0 && st.Font < use.Length) use[st.Font]++;
            for (int i = 0; i < d.Fonts.Count; i++)
            {
                var fo = d.Fonts[i];
                var vals = new List<string> { i.ToString(), fo.Name, fo.Size.ToString(), fo.Weight.ToString(), fo.Italic.ToString(), Signed8(fo.Sx).ToString(), Signed8(fo.Sy).ToString(), G.Hex32(fo.Color), G.Hex32(fo.SColor) };
                if (tw2) vals.Add(fo.Extra.ToString());
                if (descs.Count > 0) vals.Add(descs.TryGetValue(i, out string ds) ? ds : "");
                tv.Items.Add(new ListViewItem(vals.ToArray()));
            }
            var form = new VStack { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            v.Add(G.Group("Selected font", form));
            int? holder = null;
            void FillForm()
            {
                foreach (Control c in form.Controls.Cast<Control>().ToList()) { form.Controls.Remove(c); c.Dispose(); }
                form.RowStyles.Clear(); form.RowCount = 0;
                var g = new Grid();
                try
                {
                var s = Sel(tv);
                if (s == null) { g.Put(G.Lbl("Select a font in the table."), 0, 0); return; }
                int i = s.Value;
                holder = i;
                var fo = D.Fonts[i];
                Action<string> Setter(Action<DatFont, long> set) => t => { long x = G.ParseInt(t); Modify(() => set(D.Fonts[i], x), refresh: true); };
                Entry(g, 0, "Name:", fo.Name, t => Modify(() => D.Fonts[i].Name = t, refresh: true), 20);
                Entry(g, 1, "Size:", fo.Size.ToString(), Setter((f, x) => f.Size = x));
                Entry(g, 2, "Weight:", fo.Weight.ToString(), Setter((f, x) => f.Weight = x), tip: "400 = normal, 700 = bold");
                Entry(g, 3, "Italic:", fo.Italic.ToString(), Setter((f, x) => f.Italic = x));
                Entry(g, 0, "Shadow X:", Signed8(fo.Sx).ToString(), Setter((f, x) => f.Sx = x & 0xFF), col: 3);
                Entry(g, 1, "Shadow Y:", Signed8(fo.Sy).ToString(), Setter((f, x) => f.Sy = x & 0xFF), col: 3);
                ColourEntry(g, 2, 3, "Colour:", fo.Color, x => Modify(() => D.Fonts[i].Color = x, refresh: true));
                ColourEntry(g, 3, 3, "Shadow colour:", fo.SColor, x => Modify(() => D.Fonts[i].SColor = x, refresh: true));
                if (tw2) Entry(g, 4, "Lazy (0/1):", fo.Extra.ToString(), Setter((f, x) => f.Extra = x), tip: "1 = created on first use");
                g.Put(G.Lbl($"Used by {use[i]} states", G.Grey), 5, 0, 4);
                }
                finally { Lazy.ResumeAll(g); form.Add(g); }
            }
            tv.SelectedIndexChanged += (s, e) => { if (tv.SelectedIndices.Count > 0) FillForm(); };
            FillForm();
            void Add(bool copySel)
            {
                var src = copySel && holder != null ? D.Fonts[holder.Value].Clone() : new DatFont { Name = "Arial", Size = 10, Weight = 400, Color = 0xFFFFFFFFL, SColor = 0xFFFFFFFFL };
                Modify(() => D.Fonts.Add(src), tree: true);
            }
            void Delete()
            {
                if (holder == null) return;
                int i = holder.Value;
                if (use[i] > 0) { G.Warn(this, "This font is used by states and cannot be deleted."); return; }
                Modify(() =>
                {
                    foreach (var it in D.Items.Values) foreach (var st in it.States.Values) if (st.Font > i) st.Font--;
                    D.Fonts.RemoveAt(i);
                }, tree: true);
            }
            v.Add(G.WrapRow(G.Btn("Add", () => Add(false)), G.Btn("Duplicate", () => Add(true)), G.Btn("Delete", Delete)));
            string note = tw2 ? "Two Worlds II: name, size, weight and italic are overridden by Interface\\FontsConfig.txt when it has a row with the same number."
                : "Colour -1 (0xFFFFFFFF) means: the colour is taken from the state (Color record)." +
                  (descs.Count > 0 ? " 'Spreadsheet fonts': fonts of the developers' spreadsheet with these settings (they differ only in colour, which is stored in the states)." : "");
            v.Add(G.Wrapped(note, 1000, G.Grey));
            if (holder == null && d.Fonts.Count > 0) BeginInvoke((Action)(() => { if (!tv.IsDisposed) SelectRow(tv, 0); }));
            return v;
        }

        // ================================================================== CustomData
        Control PanelCustom()
        {
            var d = D;
            var v = new VStack();
            v.Add(G.Heading("CustomData"));
            var split = new TableLayoutPanel { ColumnCount = 2, Margin = new Padding(8) };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            v.Fill(split);
            var lb = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            foreach (var kv in d.CustomData) lb.Items.Add($"{kv.Key}   ({kv.Value.Count} lines)");
            split.Controls.Add(lb, 0, 0);
            var right = new VStack();
            split.Controls.Add(right, 1, 0);
            var nameBox = new TextBox { Width = 320 };
            right.Add(G.Row(G.Lbl("Name:"), nameBox));
            right.Add(G.Lbl("Lines (one value per line):"));
            var txt = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, AcceptsReturn = true, Font = new Font(FontFamily.GenericMonospace, 9f) };
            right.Fill(txt);
            var desc = right.Add(G.Wrapped("", 600, G.Grey));
            int? cur = null;
            lb.SelectedIndexChanged += (s, e) =>
            {
                if (lb.SelectedIndex < 0) return;
                int i = lb.SelectedIndex;
                cur = i;
                nameBox.Text = D.CustomData[i].Key;
                string dsc = Names.CustomDataDesc(D.CustomData[i].Key);
                desc.Text = dsc.Length > 0 ? $"Description: {dsc}" : "";
                txt.Text = string.Join("\r\n", D.CustomData[i].Value);
            };
            void Apply()
            {
                if (cur == null) return;
                int i = cur.Value;
                string name = TextUtil.AsciiLower(nameBox.Text.Trim());
                var lines = txt.Text.Replace("\r\n", "\n").Split('\n').Select(l => TextUtil.AsciiLower(l.Trim())).ToList();
                while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
                if (name.Length == 0) { G.Warn(this, "The name cannot be empty."); return; }
                Modify(() => D.CustomData[i] = new KeyValuePair<string, List<string>>(name, lines), tree: true);
            }
            void Add()
            {
                string n = InputDialog.Ask(this, "Add CustomData", "Name:");
                if (!string.IsNullOrEmpty(n)) Modify(() => D.CustomData.Add(new KeyValuePair<string, List<string>>(TextUtil.AsciiLower(n.Trim()), new List<string>())), tree: true);
            }
            void Delete()
            {
                if (cur != null && G.YesNo(this, $"Delete '{d.CustomData[cur.Value].Key}'?")) { int i = cur.Value; Modify(() => D.CustomData.RemoveAt(i), tree: true); }
            }
            void Move(int delta)
            {
                if (cur == null || cur + delta < 0 || cur + delta >= d.CustomData.Count) return;
                int i = cur.Value, j = i + delta;
                if (Modify(() => { var c = D.CustomData; var t = c[i]; c[i] = c[j]; c[j] = t; }, tree: true))
                    AfterRebuild(c => { var l = c.Controls.OfType<TableLayoutPanel>().SelectMany(x => x.Controls.OfType<ListBox>()).FirstOrDefault(); if (l != null && j < l.Items.Count) l.SelectedIndex = j; });
            }
            v.Add(G.WrapRow(G.Btn("Apply changes", Apply), G.Btn("Add", Add), G.Btn("Delete", Delete), G.Btn("Move up", () => Move(-1)), G.Btn("Move down", () => Move(1))));
            v.Add(G.Lbl("Like the original compiler, names and lines are stored in lower case.", G.Grey));
            return v;
        }

        // ================================================================== DialogTemplates
        int dialogSel = -1;

        Control PanelDialogs()
        {
            var d = D;
            var v = new VStack();
            v.Add(G.Heading("DialogTemplates"));
            var split = new TableLayoutPanel { ColumnCount = 2, Margin = new Padding(8) };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            v.Fill(split);
            var lb = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            foreach (var dl in d.Dialogs) lb.Items.Add($"{dl.Id}  {ResName(dl.Id)}");
            split.Controls.Add(lb, 0, 0);
            var right = new Panel { Dock = DockStyle.Fill };
            split.Controls.Add(right, 1, 0);
            void Load()
            {
                foreach (Control c in right.Controls.Cast<Control>().ToList()) { right.Controls.Remove(c); c.Dispose(); }
                var rv = new VStack();
                try
                {
                if (lb.SelectedIndex < 0) { rv.Add(G.Lbl("Select a dialog on the left.")); return; }
                int i = lb.SelectedIndex;
                dialogSel = i;
                var dl = D.Dialogs[i];
                var top = rv.Add(new Grid());
                Entry(top, 0, "Identifier:", ResName(dl.Id).Length > 0 ? ResName(dl.Id) : dl.Id.ToString(), t =>
                {
                    long x = ParseRes(t);
                    if (D.Dialogs.Where((o, k) => k != i).Any(o => o.Id == x)) throw new UserError("another dialog already has this identifier");
                    Modify(() => D.Dialogs[i].Id = x, tree: true);
                }, 30, tip: $"= {dl.Id}");
                string itn = ItemName(dl.Item);
                Entry(top, 1, "Item of the dialog:", dl.Item.ToString(), t => { long x = G.ParseInt(t); Modify(() => D.Dialogs[i].Item = x); }, tip: "-1 = none" + (itn.Length > 0 ? $"   ({itn})" : ""));
                for (int k = 0; k < G.ForceNames.Length; k++)
                {
                    int kk = k;
                    top.Put(Check(G.ForceNames[k], dl.Force[k] != 0, on => Modify(() => D.Dialogs[i].Force[kk] = on ? 1 : 0)), 2 + k, 0, 3);
                }
                string dname = ResName(dl.Id);
                string dnote = ShowNames ? Names.ControlNote(d.Fmt, dname, null) : "";
                if (dnote.Length > 0) rv.Add(G.Wrapped($"Developers' note: {dnote}", 800, G.NoteColour));
                var notes = dl.Controls.Select(c => ShowNames ? Names.ControlNote(d.Fmt, dname, ResName(c[0])) : "").ToList();
                bool anyNote = notes.Any(n => n.Length > 0);
                var cols = new List<(string, int)> { ("id", 70), ("name", 200), ("item", 60), ("item name", 180), ("x offset", 70), ("y offset", 70) };
                if (anyNote) cols.Add(("note", 260));
                var tv = G.Table(cols.ToArray());
                tv.Margin = new Padding(0, 6, 0, 6);
                rv.Fill(tv);
                for (int k = 0; k < dl.Controls.Count; k++)
                {
                    var c = dl.Controls[k];
                    var vals = new List<string> { c[0].ToString(), ResName(c[0]), c[1].ToString(), ItemName(c[1]), c[2].ToString(), c[3].ToString() };
                    if (anyNote) vals.Add(notes[k]);
                    tv.Items.Add(new ListViewItem(vals.ToArray()));
                }
                var ev = Enumerable.Range(0, 4).Select(k => new TextBox { Width = k == 0 ? 220 : 64 }).ToArray();
                rv.Add(G.WrapRow(G.Lbl("Control id/name:"), ev[0], G.Lbl("Item:"), ev[1], G.Lbl("X offset:"), ev[2], G.Lbl("Y offset:"), ev[3]));
                tv.SelectedIndexChanged += (s, e) =>
                {
                    var si = Sel(tv); if (si == null) return;
                    var c = D.Dialogs[i].Controls[si.Value];
                    ev[0].Text = ResName(c[0]).Length > 0 ? ResName(c[0]) : c[0].ToString();
                    for (int k = 1; k < 4; k++) ev[k].Text = c[k].ToString();
                };
                tv.DoubleClick += (s, e) =>
                {
                    var si = Sel(tv); if (si == null) return;
                    long target = D.Dialogs[i].Controls[si.Value][1];
                    if (D.Items.ContainsKey(target)) SelectNode($"item:{target}");
                };
                void SetCtrl(bool replace)
                {
                    long[] rec;
                    try
                    {
                        rec = new[] { ParseRes(ev[0].Text), G.ParseInt(ev[1].Text.Length > 0 ? ev[1].Text : "0"), G.ParseInt(ev[2].Text.Length > 0 ? ev[2].Text : "0"), G.ParseInt(ev[3].Text.Length > 0 ? ev[3].Text : "0") };
                    }
                    catch (UserError e) { G.Error(this, "Invalid control", e.Message); return; }
                    var si = replace ? Sel(tv) : null;
                    if (Modify(() =>
                    {
                        var ctr = D.Dialogs[i].Controls;
                        if (si != null) ctr.RemoveAt(si.Value);
                        if (ctr.Any(c => c[0] == rec[0])) throw new UserError("this control identifier is already in the dialog");
                        ctr.Add(rec);
                        var sorted = ctr.OrderBy(c => c[0]).ToList();
                        ctr.Clear(); ctr.AddRange(sorted);
                    })) Load();
                }
                void DelCtrl()
                {
                    var si = Sel(tv);
                    if (si != null && Modify(() => D.Dialogs[i].Controls.RemoveAt(si.Value))) Load();
                }
                rv.Add(G.WrapRow(G.Btn("Add control", () => SetCtrl(false)), G.Btn("Update selected", () => SetCtrl(true)), G.Btn("Delete selected", DelCtrl)));
                rv.Add(G.Lbl("Double-click a control to open its item.", G.Grey));
                }
                finally { Lazy.ResumeAll(rv); right.Controls.Add(rv); }
            }
            lb.SelectedIndexChanged += (s, e) => Load();
            Load();
            void Add()
            {
                string t = InputDialog.Ask(this, "Add dialog", "Dialog identifier (name from resource.h or number):");
                if (string.IsNullOrEmpty(t)) return;
                long x;
                try { x = ParseRes(t); } catch (UserError e) { G.Error(this, G.AppName, e.Message); return; }
                if (D.Dialogs.Any(o => o.Id == x)) { G.Error(this, G.AppName, "A dialog with this identifier already exists."); return; }
                Modify(() => D.Dialogs.Add(new DatDialog { Id = x, Item = -1 }), tree: true);
            }
            void Delete()
            {
                int i = lb.SelectedIndex;
                if (i >= 0 && G.YesNo(this, "Delete the selected dialog?")) Modify(() => D.Dialogs.RemoveAt(i), tree: true);
            }
            v.Add(G.WrapRow(G.Btn("Add dialog", Add), G.Btn("Delete dialog", Delete), G.Lbl($"Identifier names: {ResPath ?? "not loaded"}", G.Grey)));
            // keep the selected dialog across rebuilds (an edit rebuilds the whole panel)
            if (dialogSel >= 0 && dialogSel < lb.Items.Count) lb.SelectedIndex = dialogSel;
            return v;
        }

        // ================================================================== items
        long MaxItem() => D.Fmt == "e2150" ? 254 : 0xFFFE;

        long FreeItemNumber(long start = 0)
        {
            long n = start;
            while (D.Items.ContainsKey(n)) n++;
            if (n > MaxItem())
            {
                long? f = null;
                for (long k = 0; k <= MaxItem(); k++) if (!D.Items.ContainsKey(k)) { f = k; break; }
                if (f == null) throw new UserError("no free item number");
                n = f.Value;
            }
            return n;
        }

        long? SelectedItem()
        {
            string node = CurrentNode ?? "";
            if (node.StartsWith("item:") || node.StartsWith("state:")) return long.Parse(node.Split(':')[1], CultureInfo.InvariantCulture);
            return null;
        }

        public static DatState NewState()
        {
            var st = new DatState();
            for (int i = 0; i < 9; i++) st.Slots.Add(new Slot(0xFFFF, new long[] { 0, 0, 0, 0 }));
            return st;
        }

        public void CmdAddItem()
        {
            if (D == null) return;
            long n;
            try { n = FreeItemNumber(D.Items.Count > 0 ? D.Items.Keys.Max() + 1 : 0); } catch (UserError e) { G.Error(this, G.AppName, e.Message); return; }
            var it = new DatItem { Flags = D.Fmt != "e2150" ? D.GlobalFlags : 0 };
            it.States[0] = NewState();
            it.FileOrder.Add(0);
            if (Modify(() => D.Items[n] = it, tree: true)) SelectNode($"item:{n}");
        }

        public void CmdDupItem()
        {
            var n = SelectedItem(); if (n == null) return;
            long m;
            try { m = FreeItemNumber(n.Value + 1); } catch (UserError e) { G.Error(this, G.AppName, e.Message); return; }
            if (Modify(() => D.Items[m] = D.Items[n.Value].Clone(), tree: true))
            {
                SelectNode($"item:{m}");
                SetStatus($"Item {n} duplicated as item {m}");
            }
        }

        int DialogRefs(long n) => D.Dialogs.Sum(dl => (dl.Item == n ? 1 : 0) + dl.Controls.Count(c => c[1] == n));

        public void CmdDelItem()
        {
            var n = SelectedItem(); if (n == null) return;
            int refs = DialogRefs(n.Value);
            string msg = $"Delete item {n}?" + (refs > 0 ? $"\n\nIt is referenced {refs} times in DialogTemplates." : "");
            if (G.YesNo(this, msg))
            {
                Modify(() => { D.Items.Remove(n.Value); D.ItemRepeats.Remove(n.Value); D.ItemRepeatOrder.RemoveAll(x => x == n.Value); }, tree: true);
                SelectNode("items");
            }
        }

        public void RenumberItem(long n, long m)
        {
            if (m == n) return;
            if (m < 0 || m > MaxItem()) throw new UserError($"item number must be 0..{MaxItem()}");
            if (D.Items.ContainsKey(m)) throw new UserError($"item {m} already exists");
            int refs = DialogRefs(n);
            bool upd = refs > 0 && G.YesNo(this, $"Update {refs} references to item {n} in DialogTemplates?");
            if (Modify(() =>
            {
                var it = D.Items[n]; D.Items.Remove(n); D.Items[m] = it;
                if (D.ItemRepeats.TryGetValue(n, out int rep)) { D.ItemRepeats.Remove(n); D.ItemRepeats[m] = rep; }
                for (int k = 0; k < D.ItemRepeatOrder.Count; k++) if (D.ItemRepeatOrder[k] == n) D.ItemRepeatOrder[k] = m;
                if (upd)
                    foreach (var dl in D.Dialogs)
                    {
                        if (dl.Item == n) dl.Item = m;
                        foreach (var c in dl.Controls) if (c[1] == n) c[1] = m;
                    }
            }, tree: true)) SelectNode($"item:{m}");
        }

        public void CmdAddState()
        {
            var n = SelectedItem(); if (n == null) return;
            var it = D.Items[n.Value];
            long sn = 0;
            while (it.States.ContainsKey(sn)) sn++;
            if (Modify(() => { it.States[sn] = NewState(); it.FileOrder.Add(sn); }, tree: true)) SelectNode($"state:{n}:{sn}");
        }

        public void CmdDupState()
        {
            string node = CurrentNode ?? "";
            if (!node.StartsWith("state:")) return;
            var p = node.Split(':');
            long n = long.Parse(p[1]), s = long.Parse(p[2]);
            var it = D.Items[n];
            long sn = s + 1;
            while (it.States.ContainsKey(sn)) sn++;
            if (sn > 0xFFFE) return;
            if (Modify(() => { var itm = D.Items[n]; itm.States[sn] = itm.States[s].Clone(); itm.FileOrder.Add(sn); }, tree: true)) SelectNode($"state:{n}:{sn}");
        }

        public void CmdDelState()
        {
            string node = CurrentNode ?? "";
            if (!node.StartsWith("state:")) return;
            var p = node.Split(':');
            long n = long.Parse(p[1]), s = long.Parse(p[2]);
            if (D.Items[n].States.Count == 1) { G.Warn(this, "An item must have at least one state."); return; }
            if (G.YesNo(this, $"Delete state 0x{s:X2} of item {n}?"))
            {
                Modify(() => { var itm = D.Items[n]; itm.States.Remove(s); itm.FileOrder.RemoveAll(x => x == s); }, tree: true);
                SelectNode($"item:{n}");
            }
        }

        Control PanelItems()
        {
            var v = new VStack();
            v.Add(G.Heading($"Items ({D.Items.Count})"));
            var tv = G.Table(("item", 60), ("name", 280), ("states", 60), ("flags", 100), ("materials", 600));
            tv.Margin = new Padding(8);
            v.Fill(tv);
            var keys = new List<long>();
            foreach (var kv in D.Items)
            {
                var mats = kv.Value.States.Values.SelectMany(st => st.Slots).Where(s => s.Mt != 0xFFFF).Select(s => s.Mt & 0xFF).Distinct().OrderBy(x => x);
                string names = string.Join(", ", mats.Where(m => m < D.Materials.Count).Select(m => G.BaseName(D.Materials[m])));
                tv.Items.Add(new ListViewItem(new[] { kv.Key.ToString(), ItemName(kv.Key), kv.Value.States.Count.ToString(), G.Hex32(kv.Value.Flags), names }));
                keys.Add(kv.Key);
            }
            tv.DoubleClick += (s, e) => { var i = Sel(tv); if (i != null) SelectNode($"item:{keys[i.Value]}"); };
            tv.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { var i = Sel(tv); if (i != null) SelectNode($"item:{keys[i.Value]}"); } };
            v.Add(G.WrapRow(G.Btn("Add item", CmdAddItem), G.Lbl("Double-click an item to edit it.", G.Grey)));
            return v;
        }

        Control PanelItem(long n)
        {
            var d = D;
            var it = d.Items[n];
            var v = new VStack();
            v.Add(G.Heading(ItemText(n)));
            string group = ShowNames ? Names.ItemGroup(d.Fmt, n) : "";
            if (group.Length > 0) v.Add(G.Lbl($"Group: {group}", G.Grey)).Margin = new Padding(10, 0, 4, 2);
            string note = ShowNames ? Names.ItemNote(d.Fmt, n) : "";
            if (note.Length > 0) v.Add(G.Wrapped(note, 1000, G.NoteColour)).Margin = new Padding(10, 0, 4, 2);
            var cols = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            cols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            v.Fill(cols);
            var left = new VStack { AutoSize = true, Dock = DockStyle.Top };
            var right = new VStack { Dock = DockStyle.Fill };
            cols.Controls.Add(left, 0, 0);
            cols.Controls.Add(right, 1, 0);

            Action<bool> SetFlag(long mask, Func<bool, long> val) => on => Modify(() => { var i = D.Items[n]; i.Flags = (i.Flags & ~mask) | val(on); }, refresh: true);
            var f = left.Add(new Grid { Padding = new Padding(8, 0, 0, 0) });
            Entry(f, 0, "Item number:", n.ToString(), t => RenumberItem(n, G.ParseInt(t)));
            if (d.ItemRepeats.ContainsKey(n))
                Entry(f, 1, "Extra copies:", d.ItemRepeats[n].ToString(), t => { int x = (int)Math.Max(0, G.ParseInt(t)); Modify(() => D.ItemRepeats[n] = x); }, tip: "written this many extra times (RepeatItem)");

            var fl = new Grid();
            left.Add(G.Group("Flags (stored in the file)", fl));
            var low = new[] { ("ConstX", 1L), ("ConstY", 2L), ("StretchX", 4L), ("StretchY", 8L) };
            for (int k = 0; k < low.Length; k++) { long bit = low[k].Item2; fl.Put(Check(low[k].Item1, (it.Flags & bit) != 0, SetFlag(bit, on => on ? bit : 0)), 0, k); }
            fl.Put(G.Lbl("Horizontal alignment:"), 1, 0, 2);
            var ax = G.Combo(G.AlignX, 130); ax.SelectedIndex = (int)((it.Flags >> 4) & 3);
            ax.SelectionChangeCommitted += (s, e) => SetFlag(0x30, _ => (long)ax.SelectedIndex << 4)(true);
            fl.Put(ax, 1, 2, 2);
            fl.Put(G.Lbl("Vertical alignment:"), 2, 0, 2);
            var ay = G.Combo(G.AlignY, 150); ay.SelectedIndex = (int)((it.Flags >> 6) & 3);
            ay.SelectionChangeCommitted += (s, e) => SetFlag(0xC0, _ => (long)ay.SelectedIndex << 6)(true);
            fl.Put(ay, 2, 2, 2);
            int r = 3;
            if (d.Fmt != "e2150")
            {
                var ext = new List<(string, long)> { ("ScaleBorder", 0x100), ("ScaleBackground", 0x200) };
                if (d.Fmt == "tw2") { ext.Add(("InterfaceSounds", 0x800)); ext.Add(("DissTexture", 0x1000)); }
                for (int k = 0; k < ext.Count; k++)
                {
                    long bit = ext[k].Item2;
                    string g = (d.GlobalFlags & bit) != 0 ? " (global)" : "";
                    fl.Put(Check(ext[k].Item1 + g, (it.Flags & bit) != 0, SetFlag(bit, on => on ? bit : 0)), r + k / 2, (k % 2) * 2, 2);
                }
                r += (ext.Count + 1) / 2;
            }
            Entry(fl, r, "Raw flags:", G.Hex32(it.Flags), t => { long x = G.ParseInt(t) & 0xFFFFFFFFL; Modify(() => D.Items[n].Flags = x, refresh: true); }, tip: "bits as stored in the file");

            var vf = new Grid();
            left.Add(G.Group("Values", vf));
            r = 0;
            if (d.Fmt == "ks" || d.Fmt == "e2160")
                Entry(vf, r++, "SizeScale:", Decompiler.FmtFloat(it.Scale), t => { float x = G.ParseFloat(t); Modify(() => D.Items[n].Scale = x); }, tip: "0.625 = scale 640x480 -> 1024x768");
            if (d.Fmt == "e2160" || d.Fmt == "tw" || d.Fmt == "tw2")
                Entry(vf, r++, "ShowStartTime:", Decompiler.FmtFloat(it.ShowStartTime), t => { float x = G.ParseFloat(t); Modify(() => D.Items[n].ShowStartTime = x); }, tip: "-1 = not used");
            if (r == 0) vf.Put(G.Lbl("(no extra values in this format)", G.Grey), 0, 0);
            PresetsFrame(left, n);
            FillFrame(right, n);

            var lb = new ListBox { IntegralHeight = false, Height = 160 };
            var order = it.States.Keys.ToList();
            foreach (var sn in order) lb.Items.Add($"{StateText(n, sn)}   ({it.States[sn].Slots.Count(s => s.Mt != 0xFFFF)} slots)");
            lb.DoubleClick += (s, e) => { if (lb.SelectedIndex >= 0) SelectNode($"state:{n}:{order[lb.SelectedIndex]}"); };
            lb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && lb.SelectedIndex >= 0) SelectNode($"state:{n}:{order[lb.SelectedIndex]}"); };
            var sg = G.Group("States (double-click to edit)", lb);
            sg.AutoSize = false;
            sg.MinimumSize = new Size(0, 200);
            right.Fill(sg);

            var bf = G.WrapRow(G.Btn("Add state", CmdAddState), G.Btn("Duplicate item", CmdDupItem), G.Btn("Delete item", CmdDelItem));
            var sets = Names.StateSets(d.Fmt);
            if (sets.Count > 0)
            {
                bf.Controls.Add(G.Lbl("Add special states:"));
                foreach (var p in sets)
                {
                    var pp = p;
                    var b = G.Btn(p.Str("name"), () => ApplyStateSet(n, pp));
                    string tip = $"{p.Str("name")}: {p.Str("desc")} - " + string.Join(", ", SetStates(p).Select(x => $"0x{x.sn:X2} = copy of 0x{x.src:X2}"));
                    b.MouseEnter += (s, e) => SetStatus(tip);
                    bf.Controls.Add(b);
                }
            }
            bf.Margin = new Padding(8);
            v.Add(bf);
            return v;
        }

        static List<(long sn, long src, long fc)> SetStates(JObj p) =>
            (p.Arr("states") ?? new List<object>()).OfType<List<object>>().Select(x => (Json.ToLong(x[0]), Json.ToLong(x[1]), Json.ToLong(x[2]))).ToList();

        /// <summary>Adds a set of special states ($Special_Skill, ...): each one is a copy of a base state, like
        /// 'Identity' in the definition file (slots, font, records, particle file; not the sound), with its own FaceColor.</summary>
        public void ApplyStateSet(long n, JObj p)
        {
            var it = D.Items[n];
            var all = SetStates(p);
            var todo = all.Where(x => it.States.ContainsKey(x.src)).ToList();
            var missing = all.Where(x => !it.States.ContainsKey(x.src)).Select(x => x.src).Distinct().OrderBy(x => x).ToList();
            string Hexes(IEnumerable<long> xs) => string.Join(", ", xs.Select(x => $"0x{x:X2}"));
            if (todo.Count == 0) { G.Info(this, $"{p.Str("name")}: the item has none of the base states ({Hexes(missing)})."); return; }
            var exist = todo.Where(x => it.States.ContainsKey(x.sn)).Select(x => x.sn).ToList();
            if (exist.Count > 0 && !G.YesNo(this, "These states already exist and will be replaced:\n" + Hexes(exist) + "\n\nContinue?")) return;
            if (Modify(() =>
            {
                var itm = D.Items[n];
                foreach (var (sn, src, fc) in todo)
                {
                    var st = itm.States[src].Clone();
                    st.Sound = -1;                                     // Identity does not copy the sound
                    if (st.Font == -1 && st.Records.Count == 0) st.HasBlob = false;   // nothing left in the property block
                    st.FaceColor = fc & 0xFFFFFFFFL;
                    if (!itm.States.ContainsKey(sn)) itm.FileOrder.Add(sn);
                    itm.States[sn] = st;
                }
            }, tree: true))
            {
                string msg = $"{p.Str("name")}: {todo.Count} states added";
                if (missing.Count > 0) msg += $" (skipped - no base state {Hexes(missing)})";
                SetStatus(msg);
            }
        }

        static readonly Dictionary<string, long> PresetLow = new Dictionary<string, long> { { "ConstX", 1 }, { "ConstY", 2 }, { "StretchX", 4 }, { "StretchY", 8 } };
        static readonly Dictionary<string, long> PresetExt = new Dictionary<string, long> { { "ScaleBorder", 0x100 }, { "ScaleBackground", 0x200 } };
        static readonly Dictionary<string, long> PresetNo = new Dictionary<string, long> { { "NoBorderX", 0x300 }, { "NoBorderY", 0xC00 }, { "NoBackground", 0x1000 } };
        static readonly string[] MarginKeys = { "LeftMargin", "TopMargin", "RightMargin", "BottomMargin" };

        static bool Truthy(object o) => o is bool b ? b : o is string s ? s.Length > 0 : o != null && Convert.ToDouble(o, CultureInfo.InvariantCulture) != 0;

        bool PresetApplies(JObj p)
        {
            foreach (var k in (p.Obj("set") ?? new JObj()).Keys)
            {
                if (PresetExt.ContainsKey(k) && D.Fmt == "e2150") return false;
                if (MarginKeys.Contains(k) && !Fmt.IsNew(D.Fmt)) return false;
                if (k == "SizeScale" && D.Fmt != "ks" && D.Fmt != "e2160") return false;
            }
            return true;
        }

        static string PyValue(object o) => o is bool b ? (b ? "True" : "False") : o is double dd ? TextUtil.PyReprFloat(dd) : Convert.ToString(o, CultureInfo.InvariantCulture);

        void PresetsFrame(VStack parent, long n)
        {
            var presets = Names.FlagPresets(D.Fmt).Where(PresetApplies).ToList();
            if (presets.Count == 0) return;
            var pf = new Grid();
            parent.Add(G.Group("Presets (from the developers' spreadsheet)", pf));
            for (int k = 0; k < presets.Count; k++)
            {
                var p = presets[k];
                var b = G.Btn(p.Str("name"), () => ApplyPreset(n, p), 110);
                string tip = $"{p.Str("name")}: {p.Str("desc")} - " + string.Join(", ", (p.Obj("set") ?? new JObj()).Select(kv => $"{kv.Key} {PyValue(kv.Value)}"));
                b.MouseEnter += (s, e) => SetStatus(tip);
                pf.Put(b, k / 3, k % 3);
            }
        }

        public void ApplyPreset(long n, JObj p)
        {
            var (flags, margins0) = FillSettings(n);
            long nob = (flags ?? 0) & 0x1F00;
            var margins = margins0 != null ? (long[])margins0.Clone() : new long[4];
            bool refill = false;
            if (Modify(() =>
            {
                var it = D.Items[n];
                foreach (var kv in p.Obj("set") ?? new JObj())
                {
                    string k = kv.Key; bool on = Truthy(kv.Value);
                    if (PresetLow.TryGetValue(k, out long b1)) it.Flags = on ? it.Flags | b1 : it.Flags & ~b1;
                    else if (PresetExt.TryGetValue(k, out long b2)) it.Flags = on ? it.Flags | b2 : it.Flags & ~b2;
                    else if (PresetNo.TryGetValue(k, out long b3)) { nob = on ? nob | b3 : nob & ~b3; refill = true; }
                    else if (MarginKeys.Contains(k)) { margins[Array.IndexOf(MarginKeys, k)] = (long)Convert.ToDouble(kv.Value, CultureInfo.InvariantCulture); refill = true; }
                    else if (k == "SizeScale") it.Scale = (float)Convert.ToDouble(kv.Value, CultureInfo.InvariantCulture);
                }
                if (refill) RederiveItem(n, (it.Flags & 0xFF) | nob, Fmt.IsNew(D.Fmt) ? margins : null);
            }, refresh: true)) SetStatus($"Preset {p.Str("name")} applied to item {n}");
        }

        void FillFrame(VStack parent, long n)
        {
            var d = D;
            var (flags, margins) = FillSettings(n);
            var ff = new Grid();
            parent.Add(G.Group("Filled-in rectangles of empty slots (not stored in the file)", ff));
            ff.Put(G.Wrapped("The compiler computes the rectangles of slots without a material from these settings. Changing them recalculates the rectangles of all states of this item. " +
                             "The boxes show the smallest set that gives the current rectangles (e.g. 'No background' has no effect when the background slot has a material)." +
                             (flags != null ? "" : "\nThe current rectangles do not follow these rules (edited by hand?)."), 460, G.Grey), 0, 0, 4);
            long cur = flags ?? 0;
            var mg = margins != null ? (long[])margins.Clone() : new long[4];
            void Apply(long? newBits = null, long[] newMargins = null)
            {
                Modify(() =>
                {
                    var it = D.Items[n];
                    long bits = newBits ?? (cur & 0x1F00);
                    RederiveItem(n, (it.Flags & 0xFF) | bits, Fmt.IsNew(d.Fmt) ? (newMargins ?? mg) : null);
                }, refresh: true);
            }
            var labels = new[] { ("No left border", 0x100L), ("No right border", 0x200L), ("No top border", 0x400L), ("No bottom border", 0x800L), ("No background", 0x1000L) };
            for (int k = 0; k < labels.Length; k++)
            {
                long bit = labels[k].Item2;
                ff.Put(Check(labels[k].Item1, (cur & bit) != 0, on => Apply(on ? (cur & 0x1F00) | bit : (cur & 0x1F00) & ~bit)), 1 + k / 3, k % 3);
            }
            if (Fmt.IsNew(d.Fmt))
            {
                var mf = G.Row(G.Lbl("Margins of particle states (L T R B):"));
                mf.Dock = DockStyle.None;
                for (int k = 0; k < 4; k++)
                {
                    int kk = k;
                    var box = new TextBox { Text = mg[k].ToString(), Width = 44 };
                    string last = box.Text;
                    void Commit()
                    {
                        if (box.IsDisposed) return;
                        long x;
                        try { x = G.ParseInt(box.Text); } catch (UserError) { box.Text = mg[kk].ToString(); return; }
                        if (x != mg[kk]) { var m2 = (long[])mg.Clone(); m2[kk] = x; Apply(newMargins: m2); }
                    }
                    box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Commit(); } };
                    box.Leave += (s, e) => Commit();
                    mf.Controls.Add(box);
                }
                ff.Put(mf, 3, 0, 4);
            }
        }

        // ================================================================== textures
        void CmdTextureDirs()
        {
            using (var f = new Form { Text = "Texture folders", Size = new Size(680, 360), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false })
            {
                var v = new VStack { Padding = new Padding(6) };
                v.Add(G.Wrapped("Folders searched for material textures (.dds, .tga, .png, .bmp).\nAdd the game data folder (the one containing 'Interface'). " +
                                "The folder of the opened .dat and its parent folders are always searched too.", 640));
                var lb = new ListBox { IntegralHeight = false };
                foreach (var p in Settings.TextureDirs) lb.Items.Add(p);
                v.Fill(lb);
                void Add()
                {
                    using (var dlg = new FolderBrowserDialog())
                        if (dlg.ShowDialog(f) == DialogResult.OK) { Settings.TextureDirs.Add(dlg.SelectedPath); lb.Items.Add(dlg.SelectedPath); }
                }
                void Remove()
                {
                    int i = lb.SelectedIndex;
                    if (i >= 0) { Settings.TextureDirs.RemoveAt(i); lb.Items.RemoveAt(i); }
                }
                v.Add(G.Row(G.Btn("Add folder...", Add), G.Btn("Remove", Remove), G.Btn("Close", f.Close)));
                f.Controls.Add(v);
                f.ShowDialog(this);
            }
            Settings.Save();
            CmdReloadTextures();
        }

        void ClearBitmaps()
        {
            foreach (var b in bmpCache.Values) b.Dispose();
            bmpCache.Clear();
        }

        void ClearTextures() { texCache.Clear(); ClearBitmaps(); }

        public void CmdReloadTextures()
        {
            ClearTextures();
            if (D != null && CurrentNode != null) ShowNode(CurrentNode);
            TextureHint();
        }

        /// <summary>Texture folders: the configured ones, then the folder of the opened .dat and of the compiled
        /// .txt, each with up to three parent folders (material paths such as 'Interface\X\Y.dds' are relative
        /// to the game data folder, which is usually a parent).</summary>
        List<string> SearchDirs()
        {
            var dirs = new List<string>(Settings.TextureDirs);
            foreach (var src in new[] { FilePath, sourceTxt })
            {
                if (src == null) continue;
                string p = Path.GetDirectoryName(Path.GetFullPath(src));
                for (int k = 0; k < 4 && p != null; k++)
                {
                    dirs.Add(p);
                    string np = Path.GetDirectoryName(p);
                    if (np == null || np == p) break;
                    p = np;
                }
            }
            if (!string.IsNullOrEmpty(Settings.LastDir)) dirs.Add(Settings.LastDir);
            return dirs.Distinct().ToList();
        }

        public static bool IsColourMap(string path) => !string.IsNullOrEmpty(path) && path.ToLowerInvariant().EndsWith("@.bmp");

        /// <summary>Tells the user when colour maps are shown because the real textures were not found.</summary>
        void TextureHint()
        {
            if (D == null || PreferMaps || D.Materials.Count == 0) return;
            var srcs = Enumerable.Range(0, D.Materials.Count).Select(m => TextureFor(m).path).ToList();
            int maps = srcs.Count(IsColourMap), missing = srcs.Count(p => p == null);
            if (maps > 0 || missing > 0)
            {
                var parts = new List<string>();
                if (maps > 0) parts.Add($"{maps} material(s) shown as colour maps (name@.bmp)");
                if (missing > 0) parts.Add($"{missing} material(s) not found (placeholder)");
                SetStatus("Textures not found: " + string.Join(", ", parts) + ". Add the game data folder (the one containing 'Interface') in View -> Texture folders.");
            }
        }

        string FindTextureFile(string name)
        {
            if (Settings.MaterialFiles.TryGetValue(name.ToLowerInvariant(), out string ov) && File.Exists(ov)) return ov;
            string rel = name.Replace("\\", "/");
            string bas = rel.Split('/').Last();
            var textures = new List<string>();
            // TwoWorlds (and other games of the engine) keep textures under 'Textures\': the material
            // 'Interface\GameInterface\X.dds' is unpacked from Interface.wd as 'Textures\Interface\GameInterface\X.dds'
            foreach (var r in new[] { rel, "Textures/" + rel, bas })
            {
                string ext = Path.GetExtension(r);
                string stem = ext.Length > 0 ? r.Substring(0, r.Length - ext.Length) : r;
                if (ext.Length > 0) textures.Add(r);
                textures.AddRange(G.TextureExts.Select(x => stem + x));
            }
            var maps = new List<string> { Compiler.BitmapName(name, D.Fmt) };
            // Real textures are searched in ALL folders before any colour map (name@.bmp) is used
            var groups = PreferMaps ? new[] { maps, textures } : new[] { textures, maps };
            var dirs = SearchDirs();
            foreach (var cands in groups)
                foreach (var d in dirs)
                    foreach (var c in cands)
                    {
                        string p = G.FindFileCi(d, c);
                        if (p != null) return p;
                    }
            return null;
        }

        (long w, long h) MaterialExtent(int m)
        {
            long mw = 1, mh = 1;
            foreach (var it in D.Items.Values)
                foreach (var st in it.States.Values)
                    foreach (var s in st.Slots)
                        if (s.Mt != 0xFFFF && (s.Mt & 0xFF) == m)
                        {
                            if (D.Fmt == "e2150" || D.Fmt == "ks")
                            {
                                var (ox, oy) = TileOrigin(m, s.Mt >> 8, null);
                                mw = Math.Max(mw, ox + s.R[2]); mh = Math.Max(mh, oy + s.R[3]);
                            }
                            else { mw = Math.Max(mw, s.R[2]); mh = Math.Max(mh, s.R[3]); }
                        }
            return (mw, mh);
        }

        public (Texture tex, string path) TextureFor(int m)
        {
            string name = m < D.Materials.Count ? D.Materials[m] : $"#{m}";
            string key = name.ToLowerInvariant();
            if (texCache.TryGetValue(key, out var c)) return c;
            string path = FindTextureFile(name);
            Texture tex = null;
            if (path != null)
            {
                try { tex = TexLoad.Load(path); }
                catch (Exception e) when (e is TextureError || e is IOException || e is UnauthorizedAccessException || G.IsUserError(e) || e is EndOfStreamException || e is ImageError || e is DdsError)
                {
                    SetStatus($"Cannot read texture {path}: {e.Message}");
                    path = null;
                }
            }
            if (tex == null)
            {
                texCache[key] = (TexLoad.Placeholder(1, 1), null);   // temporary, for MaterialExtent
                var (w, h) = MaterialExtent(m);
                tex = TexLoad.Placeholder(G.NextPow2(w), G.NextPow2(h));
            }
            texCache[key] = (tex, path);
            return (tex, path);
        }

        public Bitmap BitmapFor(int m)
        {
            string name = m < D.Materials.Count ? D.Materials[m].ToLowerInvariant() : $"#{m}";
            if (!bmpCache.TryGetValue(name, out var b)) bmpCache[name] = b = G.ToBitmap(TextureFor(m).tex);
            return b;
        }

        public int TileSize(int m)
        {
            string name = m < D.Materials.Count ? D.Materials[m] : "";
            if (Settings.TileSizes.TryGetValue(name.ToLowerInvariant(), out int t)) return t;
            long ext = 1;
            foreach (var it in D.Items.Values)
                foreach (var st in it.States.Values)
                    foreach (var s in st.Slots)
                        if (s.Mt != 0xFFFF && (s.Mt & 0xFF) == m) ext = Math.Max(ext, Math.Max(s.R[2], s.R[3]));
            return Decompiler.ChooseTile(name, ext, new Dictionary<string, int>());
        }

        public int TilesPerRow(int m, Texture tex)
        {
            int t = TileSize(m);
            if (tex != null && tex.Width >= t) return Math.Max(1, tex.Width / t);
            return Math.Max(1, 256 / t);
        }

        public (long x, long y) TileOrigin(int m, int tile, Texture tex)
        {
            int t = TileSize(m), pr = TilesPerRow(m, tex);
            return ((tile % pr) * t, (tile / pr) * t);
        }

        /// <summary>Rectangle of a slot in texture pixels (null if the slot has no material).</summary>
        public long[] SlotAbs(int mt, long[] r)
        {
            if (mt == 0xFFFF) return null;
            if (D.Fmt == "e2150" || D.Fmt == "ks")
            {
                int m = mt & 0xFF;
                var (ox, oy) = TileOrigin(m, mt >> 8, TextureFor(m).tex);
                return new[] { ox + r[0], oy + r[1], ox + r[2], oy + r[3] };
            }
            return (long[])r.Clone();
        }

        /// <summary>Inverse of SlotAbs: returns (u16 material value, stored rectangle).</summary>
        public (int mt, long[] r) AbsToSlot(int m, long[] a)
        {
            if (D.Fmt == "e2150" || D.Fmt == "ks")
            {
                var tex = TextureFor(m).tex;
                int t = TileSize(m), pr = TilesPerRow(m, tex);
                long tile = (a[1] / t) * pr + a[0] / t;
                if (tile > 255) throw new UserError("tile number above 255");
                var (ox, oy) = TileOrigin(m, (int)tile, tex);
                return (((int)tile << 8) | m, new[] { a[0] - ox, a[1] - oy, Math.Min(a[2] - ox, t), Math.Min(a[3] - oy, t) });
            }
            return (m, (long[])a.Clone());
        }
    }
}

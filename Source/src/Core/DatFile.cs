// The .dat model used by the decompiler, dump, diff and the editor: reading (with format
// detection) and writing back byte for byte. Port of read_dat / write_datfile of dat2txt.py.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace InterfaceTools
{
    public class DatFont
    {
        public string Name; public long Size, Weight, Italic, Sx, Sy, Color, SColor, Extra;
        public DatFont Clone() => (DatFont)MemberwiseClone();
        public string Key() => string.Join("|", Name, Size, Weight, Italic, Sx, Sy, Color, SColor, Extra);
    }

    public class Record
    {
        public int Key; public List<long> Values;
        public Record(int key, List<long> values) { Key = key; Values = values; }
        public Record Clone() => new Record(Key, new List<long>(Values));
        public bool SameAs(Record o) => o != null && Key == o.Key && Values.SequenceEqual(o.Values);
    }

    public class Slot
    {
        /// <summary>u16: Earth 2150 / KS (tile &lt;&lt; 8) | material; later formats material; 0xFFFF = none.</summary>
        public int Mt;
        public long[] R;   // L, T, R, B
        public Slot(int mt, long[] r) { Mt = mt; R = r; }
        public Slot Clone() => new Slot(Mt, (long[])R.Clone());
    }

    public class DatState
    {
        public long Font = -1, Sound = -1;
        public bool HasBlob;
        public List<Record> Records = new List<Record>();
        public List<Slot> Slots = new List<Slot>();
        public long FaceColor = 0xFFFFFFFFL;
        public string Particle = "";
        public long FaceColor2 = 0xFFFFFFFFL;
        public string Texture = "";
        public long TextureMargin;

        public DatState Clone()
        {
            var s = (DatState)MemberwiseClone();
            s.Records = Records.Select(r => r.Clone()).ToList();
            s.Slots = Slots.Select(x => x.Clone()).ToList();
            return s;
        }

        public bool SameAs(DatState o) =>
            Font == o.Font && Sound == o.Sound && HasBlob == o.HasBlob && FaceColor == o.FaceColor &&
            Particle == o.Particle && FaceColor2 == o.FaceColor2 && Texture == o.Texture && TextureMargin == o.TextureMargin &&
            Records.Count == o.Records.Count && Records.Zip(o.Records, (a, b) => a.SameAs(b)).All(x => x) &&
            Slots.Count == o.Slots.Count && Slots.Zip(o.Slots, (a, b) => a.Mt == b.Mt && a.R.SequenceEqual(b.R)).All(x => x);
    }

    public class DatItem
    {
        public long Flags;
        public float Scale = 1.0f;
        public float ShowStartTime = -1.0f;
        public SortedDictionary<long, DatState> States = new SortedDictionary<long, DatState>();
        /// <summary>State numbers in the order they appear in the file (the decompiler uses the
        /// first state in file order, like the Python version).</summary>
        public List<long> FileOrder = new List<long>();

        public IEnumerable<KeyValuePair<long, DatState>> InFileOrder()
        {
            var seen = new HashSet<long>();
            foreach (var sn in FileOrder)
                if (States.TryGetValue(sn, out var st) && seen.Add(sn)) yield return new KeyValuePair<long, DatState>(sn, st);
            foreach (var kv in States)
                if (seen.Add(kv.Key)) yield return kv;
        }

        public DatItem Clone()
        {
            var i = (DatItem)MemberwiseClone();
            i.States = new SortedDictionary<long, DatState>(States.ToDictionary(k => k.Key, k => k.Value.Clone()));
            i.FileOrder = new List<long>(FileOrder);
            return i;
        }

        public bool SameAs(DatItem o) =>
            Flags == o.Flags && Scale.Equals(o.Scale) && ShowStartTime.Equals(o.ShowStartTime) &&
            States.Count == o.States.Count && States.All(kv => o.States.TryGetValue(kv.Key, out var s) && kv.Value.SameAs(s));
    }

    public class DatDialog
    {
        public long Id, Item;
        public long[] Force = new long[4];
        public List<long[]> Controls = new List<long[]>();
        public DatDialog Clone() => new DatDialog { Id = Id, Item = Item, Force = (long[])Force.Clone(), Controls = Controls.Select(c => (long[])c.Clone()).ToList() };
    }

    public class DatFile
    {
        public string Fmt;
        public long BaseW = 640, BaseH = 480;
        public float ScaleX = 1.0f, ScaleY = 1.0f;
        public long GlobalFlags;
        public List<string> Materials = new List<string>();
        public List<string> Sounds = new List<string>();
        public List<DatFont> Fonts = new List<DatFont>();
        public SortedDictionary<long, DatItem> Items = new SortedDictionary<long, DatItem>();
        public List<KeyValuePair<string, List<string>>> CustomData = new List<KeyValuePair<string, List<string>>>();
        public List<DatDialog> Dialogs = new List<DatDialog>();
        public int End;
        /// <summary>Identical repeated items (item -> number of extra copies), in file order.</summary>
        public Dictionary<long, int> ItemRepeats = new Dictionary<long, int>();
        public List<long> ItemRepeatOrder = new List<long>();
        public List<string> ItemAnomalies = new List<string>();
        /// <summary>Earth 2150: the original appends a second 0xFF at the end.</summary>
        public bool E2150DoubleEnd = true;

        static long S32(long v) => v >= 0x80000000L ? v - 0x100000000L : v;

        /// <summary>Detects the format: Earth 2150 and KS have a stream header; Earth 2160 and
        /// TwoWorlds do not, so all readers are tried and the one that consumes the whole file wins.</summary>
        public static DatFile Read(byte[] data, string fmt = null)
        {
            if (fmt == null)
            {
                if (data.Length >= 4 && BitConverter.ToUInt32(data, 0) == Compiler.FileMagic) return ReadAs(data, null);
                var errors = new List<string>();
                foreach (var cand in new[] { "e2160", "tw", "tw2" })
                {
                    try
                    {
                        var d = ReadAs(data, cand);
                        if (d.End == data.Length) return d;
                        errors.Add($"{cand}: {data.Length - d.End} bytes left over");
                    }
                    catch (Exception e) when (e is EndOfStreamException || e is IndexOutOfRangeException || e is ArgumentException || e is OverflowException)
                    {
                        errors.Add($"{cand}: {e.Message}");
                    }
                }
                throw new ToolError("Could not detect the file format (" + string.Join("; ", errors) + ")");
            }
            return ReadAs(data, fmt);
        }

        public static DatFile ReadFile(string path, string fmt = null) => Read(File.ReadAllBytes(path), fmt);

        static DatFile ReadAs(byte[] data, string fmt)
        {
            var r = new ByteReader(data);
            if (data.Length >= 8 && BitConverter.ToUInt32(data, 0) == Compiler.FileMagic)
            {
                long fid = BitConverter.ToUInt32(data, 4);
                if (fid == Compiler.FileIdKs) fmt = "ks";
                else if (fid == Compiler.FileId) fmt = "e2150";
                else throw new ToolError($"Unknown file version: 0x{fid:x8}");
                r.P = 8;
            }
            var d = new DatFile { Fmt = fmt };
            if (!InterfaceTools.Fmt.IsTw(fmt))
            {
                d.BaseW = r.I16(); d.BaseH = r.I16(); d.ScaleX = r.F32(); d.ScaleY = r.F32();
            }
            d.GlobalFlags = fmt != "e2150" ? r.U32() : 0;
            List<string> StrList()
            {
                var o = new List<string>();
                while (r[r.P] != 0) o.Add(r.PStr());
                r.P++;
                return o;
            }
            d.Materials = StrList();
            d.Sounds = fmt != "tw2" ? StrList() : new List<string>();
            while (r[r.P] != 0xFF)
            {
                var f = new DatFont { Name = r.PStr() };
                f.Size = r.U16(); f.Weight = r.U16(); f.Italic = r.U8(); f.Sx = r.U8(); f.Sy = r.U8(); f.Color = r.U32(); f.SColor = r.U32();
                f.Extra = fmt == "tw2" ? r.U8() : 0;
                d.Fonts.Add(f);
            }
            r.P++;
            long lastNum = -1;
            bool doubleEnd = false;
            while (true)
            {
                if (fmt == "e2150" ? r[r.P] == 0xFF : (r[r.P] == 0xFF && r[r.P + 1] == 0xFF))
                {
                    r.P += fmt == "e2150" ? 1 : 2;
                    doubleEnd = false;
                    if (fmt == "e2150" && r.P < data.Length && data[r.P] == 0xFF) { r.P++; doubleEnd = true; }
                    break;
                }
                long num; DatItem item;
                if (fmt == "e2150") { num = r.U8(); item = new DatItem { Flags = r.U32() }; }
                else
                {
                    num = r.U16(); long flags = r.U32(); float scale = r.F32();
                    item = InterfaceTools.Fmt.IsTw(fmt) ? new DatItem { Flags = flags, Scale = 1.0f, ShowStartTime = scale } : new DatItem { Flags = flags, Scale = scale };
                    if (fmt == "e2160") item.ShowStartTime = r.F32();
                }
                while (true)
                {
                    long sn = r.U16();
                    if (sn == 0xFFFF) break;
                    int n = r.U8();
                    var dw = new long[n];
                    for (int i = 0; i < n; i++) dw[i] = r.U32();
                    var st = new DatState();
                    if (n > 0)
                    {
                        st.HasBlob = true;
                        st.Font = S32(dw[0]) >= 0 ? S32(dw[0]) : -1;
                        st.Sound = n > 1 && S32(dw[1]) >= 0 ? S32(dw[1]) : -1;
                        if (n < 2) throw new EndOfStreamException("property block too short");
                        int k = 2;
                        while (k < n)
                        {
                            int key = (int)(dw[k] >> 16), ln = (int)(dw[k] & 0xFFFF);
                            var vals = new List<long>();
                            for (int j = k + 1; j < Math.Min(n, k + 1 + ln); j++) vals.Add(dw[j]);
                            st.Records.Add(new Record(key, vals));
                            k += 1 + ln;
                        }
                    }
                    for (int s = 0; s < 9; s++)
                    {
                        int mt = r.U16();
                        st.Slots.Add(new Slot(mt, new long[] { r.U16(), r.U16(), r.U16(), r.U16() }));
                    }
                    if (InterfaceTools.Fmt.IsNew(fmt))
                    {
                        st.FaceColor = r.U32();
                        if (fmt == "tw2") st.FaceColor2 = r.U32();
                        st.Particle = r.PStr();
                        if (fmt == "tw2") { st.Texture = r.PStr(); st.TextureMargin = r.U32(); }
                    }
                    if (!item.States.ContainsKey(sn)) item.FileOrder.Add(sn);
                    item.States[sn] = st;
                }
                if (d.Items.TryGetValue(num, out var prev))
                {
                    if (prev.SameAs(item) && num == lastNum)
                    {
                        d.ItemRepeats[num] = (d.ItemRepeats.TryGetValue(num, out int c) ? c : 0) + 1;
                        if (!d.ItemRepeatOrder.Contains(num)) d.ItemRepeatOrder.Add(num);
                    }
                    else d.ItemAnomalies.Add($"item {num} appears in the file several times with different content (the game will use the last version)");
                }
                else if (num < lastNum) d.ItemAnomalies.Add($"item {num} written after item {lastNum} (non-ascending order)");
                d.Items[num] = item;
                lastNum = num;
            }
            d.E2150DoubleEnd = fmt == "e2150" ? doubleEnd : true;
            if (InterfaceTools.Fmt.IsNew(fmt))
            {
                long n = r.U32();
                if (n > data.Length) throw new EndOfStreamException("invalid CustomData count");
                for (long i = 0; i < n; i++)
                {
                    string name = r.LStr();
                    long c = r.U32();
                    if (c > data.Length) throw new EndOfStreamException("invalid CustomData length");
                    var lst = new List<string>();
                    for (long j = 0; j < c; j++) lst.Add(r.LStr());
                    d.CustomData.Add(new KeyValuePair<string, List<string>>(name, lst));
                }
                n = r.U32();
                if (n > data.Length) throw new EndOfStreamException("invalid DialogTemplates count");
                for (long i = 0; i < n; i++)
                {
                    var hdr = new long[6];
                    for (int j = 0; j < 6; j++) hdr[j] = S32(r.U32());
                    long c = r.U32();
                    if (c > data.Length) throw new EndOfStreamException("invalid control count");
                    var dl = new DatDialog { Id = hdr[0], Item = hdr[1], Force = hdr.Skip(2).ToArray() };
                    for (long j = 0; j < c; j++) dl.Controls.Add(new[] { S32(r.U32()), S32(r.U32()), S32(r.U32()), S32(r.U32()) });
                    d.Dialogs.Add(dl);
                }
            }
            d.End = r.P;
            return d;
        }

        static void PStr(ByteWriter o, string s)
        {
            byte[] b = Text.ToLatin1(s);
            if (b.Length > 255) throw new ArgumentException($"text too long (max 255 bytes): '{s}'");
            o.U8(b.Length); o.Bytes(b);
        }

        static void LStr(ByteWriter o, string s) { byte[] b = Text.ToLatin1(s); o.U32(b.Length); o.Bytes(b); }

        /// <summary>Serializes the model back into .dat bytes (the inverse of Read).</summary>
        public byte[] Write()
        {
            bool nw = InterfaceTools.Fmt.IsNew(Fmt), tw = InterfaceTools.Fmt.IsTw(Fmt);
            var o = new ByteWriter();
            if (Fmt == "e2150" || Fmt == "ks") { o.U32(Compiler.FileMagic); o.U32(Fmt == "ks" ? Compiler.FileIdKs : Compiler.FileId); }
            if (!tw)
            {
                if (BaseW < -32768 || BaseW > 32767 || BaseH < -32768 || BaseH > 32767) throw new ArgumentException("base resolution out of range");
                o.I16(BaseW); o.I16(BaseH); o.F32(ScaleX); o.F32(ScaleY);
            }
            if (Fmt != "e2150") o.U32(GlobalFlags & 0xFFFFFFFFL);
            foreach (var m in Materials) PStr(o, m);
            o.U8(0);
            if (Fmt != "tw2") { foreach (var s in Sounds) PStr(o, s); o.U8(0); }
            foreach (var f in Fonts)
            {
                PStr(o, f.Name);
                o.U16(f.Size & 0xFFFF); o.U16(f.Weight & 0xFFFF); o.U8(f.Italic & 0xFF); o.U8(f.Sx & 0xFF); o.U8(f.Sy & 0xFF);
                o.U32(f.Color); o.U32(f.SColor);
                if (Fmt == "tw2") o.U8(f.Extra & 0xFF);
            }
            o.U8(0xFF);
            foreach (var kv in Items)
            {
                long num = kv.Key; var it = kv.Value;
                int times = 1 + (ItemRepeats.TryGetValue(num, out int rep) ? rep : 0);
                for (int t = 0; t < times; t++)
                {
                    if (Fmt == "e2150")
                    {
                        if (!(num >= 0 && num < 255)) throw new ArgumentException($"item number {num} out of range 0..254 (Earth 2150)");
                        o.U8(num); o.U32(it.Flags);
                    }
                    else
                    {
                        if (!(num >= 0 && num < 0xFFFF)) throw new ArgumentException($"item number {num} out of range");
                        o.U16(num); o.U32(it.Flags); o.F32(tw ? it.ShowStartTime : it.Scale);
                        if (Fmt == "e2160") o.F32(it.ShowStartTime);
                    }
                    foreach (var skv in it.States)
                    {
                        var st = skv.Value;
                        var dw = new List<long>();
                        if (st.HasBlob || st.Font != -1 || st.Sound != -1 || st.Records.Count > 0)
                        {
                            dw.Add(st.Font & 0xFFFFFFFFL); dw.Add(st.Sound & 0xFFFFFFFFL);
                            foreach (var rec in st.Records.OrderBy(x => x.Key))
                            {
                                dw.Add(((long)(rec.Key & 0xFFFF) << 16) | (long)rec.Values.Count);
                                dw.AddRange(rec.Values.Select(v => v & 0xFFFFFFFFL));
                            }
                        }
                        if (dw.Count > 255) throw new ArgumentException($"Item {num} State {skv.Key}: too many properties");
                        if (!(skv.Key >= 0 && skv.Key <= 0xFFFF)) throw new ArgumentException($"state number {skv.Key} out of range");
                        o.U16(skv.Key & 0xFFFF); o.U8(dw.Count);
                        foreach (var v in dw) o.U32(v);
                        foreach (var s in st.Slots) { o.U16(s.Mt & 0xFFFF); foreach (var v in s.R) o.U16(v & 0xFFFF); }
                        if (nw)
                        {
                            o.U32(st.FaceColor & 0xFFFFFFFFL);
                            if (Fmt == "tw2") o.U32(st.FaceColor2 & 0xFFFFFFFFL);
                            PStr(o, st.Particle);
                            if (Fmt == "tw2") { PStr(o, st.Texture); o.U32(st.TextureMargin & 0xFFFFFFFFL); }
                        }
                    }
                    o.U16(0xFFFF);
                }
            }
            if (Fmt == "e2150") { o.U8(0xFF); if (E2150DoubleEnd) o.U8(0xFF); }
            else o.U16(0xFFFF);
            if (nw)
            {
                o.U32(CustomData.Count);
                foreach (var kv in CustomData)
                {
                    LStr(o, kv.Key); o.U32(kv.Value.Count);
                    foreach (var s in kv.Value) LStr(o, s);
                }
                o.U32(Dialogs.Count);
                foreach (var dl in Dialogs)
                {
                    foreach (var v in new[] { dl.Id, dl.Item }.Concat(dl.Force)) o.U32(v & 0xFFFFFFFFL);
                    o.U32(dl.Controls.Count);
                    foreach (var c in dl.Controls.OrderBy(c => c[0])) foreach (var v in c) o.U32(v & 0xFFFFFFFFL);
                }
            }
            return o.ToArray();
        }

        public DatFile Clone()
        {
            var d = (DatFile)MemberwiseClone();
            d.Materials = new List<string>(Materials);
            d.Sounds = new List<string>(Sounds);
            d.Fonts = Fonts.Select(f => f.Clone()).ToList();
            d.Items = new SortedDictionary<long, DatItem>(Items.ToDictionary(k => k.Key, k => k.Value.Clone()));
            d.CustomData = CustomData.Select(kv => new KeyValuePair<string, List<string>>(kv.Key, new List<string>(kv.Value))).ToList();
            d.Dialogs = Dialogs.Select(x => x.Clone()).ToList();
            d.ItemRepeats = new Dictionary<long, int>(ItemRepeats);
            d.ItemRepeatOrder = new List<long>(ItemRepeatOrder);
            d.ItemAnomalies = new List<string>(ItemAnomalies);
            return d;
        }
    }
}

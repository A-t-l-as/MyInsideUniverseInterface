// AddFileInfo: adds (or reads) the small header of the game's .int files.
// Port of addfileinfo.py (itself a rewrite of the original C++ tool).
//
//   uint32 magicAndFlags  lower 24 bits = 0x00D0A1FF; 0x08000000 name, 0x10000000 attrib, 0x20000000 guid
//   [u8 nameLen, name]    [u32 attrib]    [16 bytes GUID: Data1/2/3 little-endian, Data4 raw]
//   then the content of the source file (1:1, or CRLF -> LF in text mode)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace InterfaceTools
{
    public static class AddFileInfo
    {
        public const uint MagicBase = 0x00D0A1FF;

        public class Header
        {
            public string Name;          // null = no name section
            public uint? Attrib;         // null = no attrib section
            public byte[] Guid;          // null = no GUID section (16 raw bytes)

            public static string GuidText(byte[] g)
            {
                uint d1 = BitConverter.ToUInt32(g, 0);
                ushort d2 = BitConverter.ToUInt16(g, 4), d3 = BitConverter.ToUInt16(g, 6);
                string d4 = string.Concat(g.Skip(8).Select(b => b.ToString("X2")));
                return $"{d1:X8}-{d2:X4}-{d3:X4}-{d4.Substring(0, 4)}-{d4.Substring(4)}";
            }
            public string NameText => Name ?? "(none)";
            public string AttribText => Attrib != null ? $"0x{Attrib:X8}" : "(none)";
            public string GuidString => Guid != null ? GuidText(Guid) : "(none)";
        }

        static readonly Regex GuidRe = new Regex(@"^([0-9a-fA-F]{1,8})-([0-9a-fA-F]{1,4})-([0-9a-fA-F]{1,4})-([0-9a-fA-F]{4})-([0-9a-fA-F]{12})$");

        /// <summary>GUID text (XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX, braces allowed) -> 16 raw bytes, or null.</summary>
        public static byte[] ParseGuid(string text)
        {
            var m = GuidRe.Match(text.Trim().Trim('{', '}'));
            if (!m.Success) return null;
            var o = new byte[16];
            BitConverter.GetBytes(Convert.ToUInt32(m.Groups[1].Value, 16)).CopyTo(o, 0);
            BitConverter.GetBytes(Convert.ToUInt16(m.Groups[2].Value, 16)).CopyTo(o, 4);
            BitConverter.GetBytes(Convert.ToUInt16(m.Groups[3].Value, 16)).CopyTo(o, 6);
            string d4 = m.Groups[4].Value + m.Groups[5].Value;
            for (int i = 0; i < 8; i++) o[8 + i] = Convert.ToByte(d4.Substring(2 * i, 2), 16);
            return o;
        }

        public static byte[] RandomGuid()
        {
            // System.Guid stores Data1-3 little-endian, like the Win32 GUID structure
            return System.Guid.NewGuid().ToByteArray();
        }

        public static byte[] Encode(byte[] content, Header h, bool textMode)
        {
            uint flags = 0;
            if (h.Name != null) flags |= 0x08;
            if (h.Attrib != null) flags |= 0x10;
            if (h.Guid != null) flags |= 0x20;
            var o = new MemoryStream();
            var w = new BinaryWriter(o);
            w.Write((flags << 24) | MagicBase);
            if (h.Name != null)
            {
                byte[] nb = Text.Latin1.GetBytes(h.Name.Select(c => c > 'ÿ' ? '?' : c).ToArray());
                if (nb.Length > 255) throw new ToolError("name is too long (max 255 characters)");
                w.Write((byte)nb.Length); w.Write(nb);
            }
            if (h.Attrib != null) w.Write(h.Attrib.Value);
            if (h.Guid != null) w.Write(h.Guid);
            if (textMode) content = CrlfToLf(content);
            w.Write(content);
            w.Flush();
            return o.ToArray();
        }

        static byte[] CrlfToLf(byte[] d)
        {
            var o = new List<byte>(d.Length);
            for (int i = 0; i < d.Length; i++)
            {
                if (d[i] == '\r' && i + 1 < d.Length && d[i + 1] == '\n') continue;
                o.Add(d[i]);
            }
            return o.ToArray();
        }

        static byte[] LfToCrlf(byte[] d)
        {
            var o = new List<byte>(d.Length + d.Length / 20);
            foreach (byte b in d) { if (b == '\n') o.Add((byte)'\r'); o.Add(b); }
            return o.ToArray();
        }

        /// <summary>Reads the header; returns the content after it (LF -> CRLF with expandCrlf).</summary>
        public static Header Decode(byte[] data, out byte[] content, bool expandCrlf)
        {
            if (data.Length < 4) throw new ToolError("cannot read the header (file too short?)");
            uint mf = BitConverter.ToUInt32(data, 0);
            if ((mf & 0x00FFFFFF) != MagicBase)
                throw new ToolError($"missing a valid AddFileInfo signature (expected lower 24 bits 0x{MagicBase:X6}, found 0x{mf & 0x00FFFFFF:X6})");
            int fb = (int)(mf >> 24);
            var h = new Header();
            int p = 4;
            if ((fb & 0x08) != 0)
            {
                if (p >= data.Length) throw new ToolError("cannot read the name length");
                int n = data[p++];
                if (p + n > data.Length) throw new ToolError("cannot read the name");
                h.Name = Text.Latin1.GetString(data, p, n); p += n;
            }
            if ((fb & 0x10) != 0)
            {
                if (p + 4 > data.Length) throw new ToolError("cannot read the attribute");
                h.Attrib = BitConverter.ToUInt32(data, p); p += 4;
            }
            if ((fb & 0x20) != 0)
            {
                if (p + 16 > data.Length) throw new ToolError("cannot read the GUID");
                h.Guid = data.Skip(p).Take(16).ToArray(); p += 16;
            }
            content = data.Skip(p).ToArray();
            if (expandCrlf) content = LfToCrlf(content);
            return h;
        }

        // ------------------------------------------------------------------ .addfileinfo files
        static readonly string[] Empty = { "", "(none)", "none" };

        /// <summary>The .addfileinfo text written next to the extracted .def file.</summary>
        public static string InfoText(Header h) => $"name: {h.NameText}\nattrib: {h.AttribText}\nguid: {h.GuidString}\n";

        /// <summary>Reads 'key: value' lines of an .addfileinfo file.</summary>
        public static Dictionary<string, string> ParseInfo(string path)
        {
            var info = new Dictionary<string, string>();
            foreach (var line in Text.ReadLatin1(path).Split('\n'))
            {
                int k = line.IndexOf(':');
                if (k < 0) continue;
                info[Text.PyStrip(line.Substring(0, k)).ToLowerInvariant()] = Text.PyStrip(line.Substring(k + 1));
            }
            return info;
        }

        /// <summary>.addfileinfo contents -> header and mode; throws ToolError on invalid data.</summary>
        public static Header HeaderFromInfo(Dictionary<string, string> info, string defaultMode, out bool textMode)
        {
            string mode = (info.TryGetValue("mode", out string m) ? m : defaultMode).ToLowerInvariant();
            if (mode != "text" && mode != "binary") throw new ToolError($"unknown mode: '{mode}' (allowed: text, binary)");
            textMode = mode == "text";
            var h = new Header();
            string name = info.TryGetValue("name", out string n) ? n : "";
            if (!Empty.Contains(name.ToLowerInvariant()))
            {
                if (name.Length > 255) throw new ToolError("name is longer than 255 characters");
                h.Name = name;
            }
            string attrib = info.TryGetValue("attrib", out string a) ? a : "";
            if (!Empty.Contains(attrib.ToLowerInvariant()))
            {
                long v;
                try { v = ParseInt0(attrib); } catch (FormatException) { throw new ToolError($"invalid attrib: '{attrib}'"); }
                if (v < 0 || v > 0xFFFFFFFFL) throw new ToolError($"attrib out of 32-bit range: '{attrib}'");
                if (v != 0) h.Attrib = (uint)v;   // attrib == 0 is omitted anyway
            }
            string guid = info.TryGetValue("guid", out string g) ? g : "";
            if (Empty.Contains(guid.ToLowerInvariant())) h.Guid = null;
            else if (guid.ToLowerInvariant() == "create" || guid.ToLowerInvariant() == "new") h.Guid = RandomGuid();
            else
            {
                h.Guid = ParseGuid(guid);
                if (h.Guid == null) throw new ToolError($"invalid GUID: '{guid.Trim('{', '}')}'");
            }
            return h;
        }

        /// <summary>Python int(text, 0).</summary>
        public static long ParseInt0(string t)
        {
            t = t.Trim().Replace("_", "");
            bool neg = t.StartsWith("-");
            if (t.StartsWith("+") || t.StartsWith("-")) t = t.Substring(1);
            string l = t.ToLowerInvariant();
            long v;
            if (l.StartsWith("0x")) v = Convert.ToInt64(t.Substring(2), 16);
            else if (l.StartsWith("0o")) v = Convert.ToInt64(t.Substring(2), 8);
            else if (l.StartsWith("0b")) v = Convert.ToInt64(t.Substring(2), 2);
            else
            {
                if (t.Length == 0 || !t.All(c => c >= '0' && c <= '9') || (t.Length > 1 && t[0] == '0' && t.Trim('0').Length > 0)) throw new FormatException();
                v = long.Parse(t, CultureInfo.InvariantCulture);
            }
            return neg ? -v : v;
        }

        // ------------------------------------------------------------------ command
        const string Usage =
            "Usage: InterfaceTools addfileinfo <source_file> <dest_file> [options]\n\n" +
            "Options:\n" +
            "  -text                text mode for reading the source (CRLF -> LF)\n" +
            "  -binary              binary mode for reading the source (default)\n" +
            "  -name <name>         append a name section\n" +
            "  -guid <GUID>         use the given GUID (format: XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX)\n" +
            "  -guidCreate          generate a new random GUID\n" +
            "  -guidNone            do not include a GUID section (default)\n" +
            "  -attrib <value>      append an attribute value (decimal or 0x..)\n\n" +
            "Reverse mode (read a file already processed by AddFileInfo):\n" +
            "  InterfaceTools addfileinfo -extract <binary_file> <output_file> [-crlf]\n" +
            "    Prints the header (name/attrib/guid) and writes the rest of the file to <output_file>.\n" +
            "    -crlf   inserts a CR before every LF (restoring CRLF).";

        public static int Run(IList<string> argv)
        {
            if (argv.Count >= 1 && argv[0].ToLowerInvariant() == "-extract")
            {
                if (argv.Count < 3) { Log.Line("Error: syntax: InterfaceTools addfileinfo -extract <binary_file> <output_file> [-crlf]"); return 1; }
                bool crlf = false;
                foreach (var a in argv.Skip(3))
                {
                    if (a.ToLowerInvariant() == "-crlf") crlf = true;
                    else { Log.Line($"Error: unknown option in -extract mode: {a}"); return 1; }
                }
                byte[] data;
                try { data = File.ReadAllBytes(argv[1]); } catch (Exception) { Log.Line($"Error: cannot open the source file: {argv[1]}"); return 1; }
                Header h;
                byte[] content;
                try { h = Decode(data, out content, crlf); } catch (ToolError e) { Log.Line("Error: " + e.Message); return 1; }
                Log.Line("AddFileInfo header:");
                Log.Line($"  name  : {h.NameText}");
                Log.Line($"  attrib: {h.AttribText}");
                Log.Line($"  guid  : {h.GuidString}");
                try { File.WriteAllBytes(argv[2], content); } catch (Exception) { Log.Line($"Error: cannot create the destination file: {argv[2]}"); return 1; }
                Log.Line($"Source content successfully exported to: {argv[2]}");
                return 0;
            }
            if (argv.Count < 2) { Log.Line(Usage); return 1; }
            var hd = new Header();
            bool text = false;
            for (int i = 2; i < argv.Count; i++)
            {
                string low = argv[i].ToLowerInvariant();
                if (low == "-text") text = true;
                else if (low == "-binary") text = false;
                else if (low == "-guidcreate") hd.Guid = RandomGuid();
                else if (low == "-guidnone") hd.Guid = null;
                else if (low == "-name" || low == "-guid" || low == "-attrib")
                {
                    if (i + 1 >= argv.Count) { Log.Line($"Error: missing value for {argv[i]}"); return 1; }
                    string v = argv[++i];
                    if (low == "-name")
                    {
                        if (v.Length > 255) { Log.Line("Error: name is too long (max 255 characters)"); return 1; }
                        hd.Name = v.Length > 0 ? v : null;
                    }
                    else if (low == "-guid")
                    {
                        var g = ParseGuid(v);
                        if (g == null) { Log.Line($"Error: invalid GUID format: {v}"); return 1; }
                        hd.Guid = g;
                    }
                    else
                    {
                        long a;
                        try { a = ParseInt0(v); if (a < 0) throw new FormatException(); }
                        catch (FormatException) { Log.Line($"Error: invalid value for -attrib: {v}"); return 1; }
                        uint u = (uint)(a & 0xFFFFFFFFL);
                        hd.Attrib = u != 0 ? u : (uint?)null;
                    }
                }
                else { Log.Line($"Error: unknown option: {argv[i]}"); Log.Line(Usage); return 1; }
            }
            byte[] src;
            try { src = File.ReadAllBytes(argv[0]); } catch (Exception) { Log.Line($"Error: cannot open the source file: {argv[0]}"); return 1; }
            byte[] o;
            try { o = Encode(src, hd, text); } catch (ToolError e) { Log.Line("Error: " + e.Message); return 1; }
            try { File.WriteAllBytes(argv[1], o); } catch (Exception) { Log.Line($"Error: cannot create the destination file: {argv[1]}"); return 1; }
            Log.Line("File successfully saved.");
            return 0;
        }
    }
}

// Common helpers: C-style string parsing (strtol, strtoul, sscanf), line normalization,
// Latin-1 text, exact float parsing / formatting (identical results on every runtime),
// and the log used by all tools.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace InterfaceTools
{
    /// <summary>Error in the definition file (original message: "Blad w pliku definicyjnym").</summary>
    public class DefinitionError : Exception { public DefinitionError(string m) : base(m) { } }

    public class BitmapError : Exception { public BitmapError(string m) : base(m) { } }

    /// <summary>Fatal error of a tool (the Python versions used SystemExit for these).</summary>
    public class ToolError : Exception { public ToolError(string m) : base(m) { } }

    /// <summary>Output of all tools. Console by default; the GUI redirects it per thread.</summary>
    public static class Log
    {
        [ThreadStatic] static Action<string> writer;
        static readonly object consoleLock = new object();

        public static void Line(string s = "")
        {
            var w = writer;
            if (w != null) { w(s); return; }
            lock (consoleLock) { try { Console.WriteLine(s); } catch (IOException) { } }   // e.g. a closed pipe
        }

        /// <summary>Redirects the log of the current thread until the returned object is disposed.</summary>
        public static IDisposable Redirect(Action<string> w)
        {
            var old = writer;
            writer = w;
            return new Restore(() => writer = old);
        }

        public static IDisposable Capture(StringBuilder sb) => Redirect(s => sb.Append(s).Append('\n'));

        sealed class Restore : IDisposable
        {
            readonly Action a; bool done;
            public Restore(Action a) { this.a = a; }
            public void Dispose() { if (!done) { done = true; a(); } }
        }
    }

    public static class Text
    {
        public static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
        public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string ReadLatin1(string path) => Latin1.GetString(File.ReadAllBytes(path));
        public static byte[] ToLatin1(string s)
        {
            // like Python's str.encode("latin-1"): characters above U+00FF are an error
            foreach (char c in s)
                if (c > 'ÿ') throw new ArgumentException($"character '{c}' cannot be stored (Latin-1 only): '{s}'");
            return Latin1.GetBytes(s);
        }

        const string WS = " \t\r\n\v\f";
        public static bool IsWs(char c) => WS.IndexOf(c) >= 0;

        /// <summary>C _strlwr / _stricmp: letters A-Z only.</summary>
        public static string AsciiLower(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(c >= 'A' && c <= 'Z' ? (char)(c + 32) : c);
            return sb.ToString();
        }

        /// <summary>_strnicmp: case-insensitive (A-Z) prefix comparison.</summary>
        public static bool StartsKw(string line, string kw) =>
            AsciiLower(line.Length > kw.Length ? line.Substring(0, kw.Length) : line) == AsciiLower(kw);

        /// <summary>CString::TrimRight + TrimLeft.</summary>
        public static string CTrim(string s) => s.Trim(' ', '\t', '\r', '\n', '\v', '\f');

        /// <summary>Python str.strip() for ASCII whitespace (used by the pipeline).</summary>
        public static string PyStrip(string s) => s.Trim(' ', '\t', '\r', '\n', '\v', '\f', '\x1c', '\x1d', '\x1e', '\x1f', '\x85', '\xa0');

        /// <summary>Line normalization exactly as in the original InterfaceMap:
        /// whitespace runs collapse into one space, removed at the start and after ',' ':';
        /// a space right before ',' or ':' is removed; a trailing space is removed.</summary>
        public static string NormalizeLine(string raw)
        {
            var o = new StringBuilder(raw.Length);
            foreach (char ch in raw)
            {
                if ((ch == ',' || ch == ':') && o.Length > 0 && o[o.Length - 1] == ' ')
                    o[o.Length - 1] = ch;
                else if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n')
                {
                    if (o.Length > 0 && " ,:".IndexOf(o[o.Length - 1]) < 0) o.Append(' ');
                }
                else o.Append(ch);
            }
            if (o.Length > 0 && o[o.Length - 1] == ' ') o.Length--;
            return o.ToString();
        }

        static bool PyIsDigit(char c) => char.IsDigit(c) || c == '²' || c == '³' || c == '¹';

        /// <summary>C strtol / strtoul with base 0 (0x.. hex, 0.. octal). Returns the value and the
        /// end index; if nothing was parsed, end == pos.</summary>
        static long CStrTo(string s, int pos, bool unsigned, out int end)
        {
            int i = pos, n = s.Length;
            while (i < n && IsWs(s[i])) i++;
            bool neg = false;
            if (i < n && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }
            int bas = 10;
            if (i + 1 < n && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X') && i + 2 < n &&
                "0123456789abcdefABCDEF".IndexOf(s[i + 2]) >= 0) { bas = 16; i += 2; }
            else if (i < n && s[i] == '0') bas = 8;
            int start = i;
            BigInteger val = BigInteger.Zero;
            while (i < n)
            {
                char c = char.ToLowerInvariant(s[i]);
                int d = PyIsDigit(c) ? c - 48 : (c >= 'a' && c <= 'f' ? c - 87 : 99);
                if (d < 0 || d >= bas) break;
                val = val * bas + d;
                i++;
            }
            if (i == start) { end = pos; return 0; }
            end = i;
            if (unsigned)
            {
                if (val > 0xFFFFFFFFL) return 0xFFFFFFFFL;
                long v = (long)val;
                return neg ? ((-v) & 0xFFFFFFFFL) : v;
            }
            if (neg) val = -val;
            if (val > int.MaxValue) return int.MaxValue;
            if (val < int.MinValue) return int.MinValue;
            return (long)val;
        }

        public static long StrToL(string s, int pos, out int end) => CStrTo(s, pos, false, out end);
        public static long StrToUL(string s, int pos, out int end) => CStrTo(s, pos, true, out end);
        public static long StrToL(string s, out int end) => CStrTo(s, 0, false, out end);
        public static long StrToUL(string s, out int end) => CStrTo(s, 0, true, out end);

        /// <summary>sscanf "%s": skips whitespace, reads a run of non-whitespace characters.</summary>
        public static string ScanToken(string s, int pos, out int end)
        {
            int n = s.Length;
            while (pos < n && IsWs(s[pos])) pos++;
            int start = pos;
            while (pos < n && !IsWs(s[pos])) pos++;
            end = pos;
            return pos > start ? s.Substring(start, pos - start) : null;
        }

        /// <summary>sscanf "%d": skips whitespace, optional sign, decimal digits (wraps to 32 bits).</summary>
        public static long? ScanInt(string s, int pos, out int end)
        {
            int n = s.Length;
            while (pos < n && IsWs(s[pos])) pos++;
            int i = pos;
            if (i < n && (s[i] == '+' || s[i] == '-')) i++;
            int j = i;
            while (j < n && s[j] >= '0' && s[j] <= '9') j++;
            if (j == i) { end = pos; return null; }
            BigInteger v = BigInteger.Parse(s.Substring(i, j - i), CultureInfo.InvariantCulture);
            if (s[pos] == '-') v = -v;
            v = ((v + 0x80000000L) % 0x100000000L + 0x100000000L) % 0x100000000L - 0x80000000L;
            end = j;
            return (long)v;
        }

        /// <summary>C strtod (decimal only). Returns the value and the end index (end == pos if nothing).</summary>
        public static double StrToD(string s, int pos, out int end)
        {
            int i = pos, n = s.Length;
            while (i < n && IsWs(s[i])) i++;
            int st = i;
            if (i < n && (s[i] == '+' || s[i] == '-')) i++;
            int digStart = i, intDigits = 0, fracDigits = 0;
            while (i < n && s[i] >= '0' && s[i] <= '9') { i++; intDigits++; }
            if (i < n && s[i] == '.')
            {
                int k = i + 1;
                while (k < n && s[k] >= '0' && s[k] <= '9') { k++; fracDigits++; }
                if (intDigits > 0 || fracDigits > 0) i = k;
            }
            if (intDigits == 0 && fracDigits == 0) { end = pos; return 0.0; }
            if (i < n && (s[i] == 'e' || s[i] == 'E'))
            {
                int k = i + 1;
                if (k < n && (s[k] == '+' || s[k] == '-')) k++;
                int e0 = k;
                while (k < n && s[k] >= '0' && s[k] <= '9') k++;
                if (k > e0) i = k;
            }
            end = i;
            return ParseDouble(s.Substring(st, i - st));
        }

        public static double StrToD(string s, out int end) => StrToD(s, 0, out end);

        public static float F32(double v) => (float)v;

        // -------------------------------------------------------------------------------------
        //  Exact decimal <-> binary conversion (the .NET Framework's own routines are not always
        //  correctly rounded; these give the same results as Python / C on every platform)
        // -------------------------------------------------------------------------------------

        /// <summary>Correctly rounded conversion of a decimal string ([+-]digits[.digits][e[+-]digits]).</summary>
        public static double ParseDouble(string t)
        {
            t = t.Trim();
            bool neg = false;
            int i = 0;
            if (i < t.Length && (t[i] == '+' || t[i] == '-')) { neg = t[i] == '-'; i++; }
            var digits = new StringBuilder();
            int exp10 = 0;
            bool point = false;
            for (; i < t.Length; i++)
            {
                char c = t[i];
                if (c >= '0' && c <= '9') { digits.Append(c); if (point) exp10--; }
                else if (c == '.' && !point) point = true;
                else break;
            }
            if (i < t.Length && (t[i] == 'e' || t[i] == 'E'))
            {
                string e = t.Substring(i + 1);
                if (!int.TryParse(e, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int ev))
                    ev = e.StartsWith("-") ? -100000 : 100000;
                exp10 += ev;
            }
            if (digits.Length == 0) throw new FormatException("invalid number: " + t);
            BigInteger N = BigInteger.Parse(digits.ToString(), CultureInfo.InvariantCulture);
            double r = BigToDouble(N, exp10);
            return neg ? -r : r;
        }

        static double BigToDouble(BigInteger N, int exp10)
        {
            if (N.IsZero) return 0.0;
            if (exp10 > 400) return double.PositiveInfinity;
            if (exp10 < -400 - N.ToString().Length) return 0.0;
            BigInteger num = N, den = BigInteger.One;
            if (exp10 >= 0) num *= BigInteger.Pow(10, exp10); else den = BigInteger.Pow(10, -exp10);
            // find e2 so that q = num / den * 2^-e2 is in [2^52, 2^53)
            long bn = BitLength(num), bd = BitLength(den);
            long e2 = bn - bd - 53;
            BigInteger q, r, d2;
            while (true)
            {
                BigInteger nn = num, dd = den;
                if (e2 > 0) dd <<= (int)e2; else nn <<= (int)(-e2);
                q = BigInteger.DivRem(nn, dd, out r);
                d2 = dd;
                if (q < (BigInteger.One << 52)) { e2--; continue; }
                if (q >= (BigInteger.One << 53)) { e2++; continue; }
                break;
            }
            // denormals
            if (e2 < -1074)
            {
                long shift = -1074 - e2;
                if (shift > 60) return 0.0;
                BigInteger dd = d2 << (int)shift;
                BigInteger nn = q * d2 + r;
                q = BigInteger.DivRem(nn, dd, out r);
                d2 = dd;
                e2 = -1074;
            }
            int cmp = (r * 2).CompareTo(d2);
            if (cmp > 0 || (cmp == 0 && !q.IsEven)) q += 1;
            if (q == (BigInteger.One << 53)) { q >>= 1; e2++; }
            long mant = (long)q;
            if (e2 + 52 > 1023) return double.PositiveInfinity;
            if (mant < (1L << 52))   // denormal
                return BitConverter.Int64BitsToDouble(mant);
            long bits = ((e2 + 52 + 1023) << 52) | (mant & ((1L << 52) - 1));
            return BitConverter.Int64BitsToDouble(bits);
        }

        static long BitLength(BigInteger v)
        {
            if (v.IsZero) return 0;
            byte[] b = v.ToByteArray();
            int top = b.Length - 1;
            while (top > 0 && b[top] == 0) top--;
            int bits = 0;
            int x = b[top];
            while (x > 0) { bits++; x >>= 1; }
            return top * 8L + bits;
        }

        /// <summary>Exact decimal digits of |v| rounded (half-even) to prec significant digits.
        /// Returns the digit string (length prec) and the decimal exponent of the first digit.</summary>
        static void Digits(double v, int prec, out string digits, out int exp)
        {
            long bits = BitConverter.DoubleToInt64Bits(Math.Abs(v));
            int be = (int)((bits >> 52) & 0x7FF);
            long frac = bits & ((1L << 52) - 1);
            BigInteger m; int e2;
            if (be == 0) { m = frac; e2 = -1074; } else { m = frac | (1L << 52); e2 = be - 1075; }
            BigInteger num = m, den = BigInteger.One;
            if (e2 >= 0) num <<= e2; else den <<= -e2;
            // estimate the decimal exponent
            int k = (int)Math.Floor(Math.Log10(Math.Abs(v)));
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int sh = prec - 1 - k;   // scale so that the result has prec digits
                BigInteger nn = num, dd = den;
                if (sh >= 0) nn *= BigInteger.Pow(10, sh); else dd *= BigInteger.Pow(10, -sh);
                BigInteger q = BigInteger.DivRem(nn, dd, out BigInteger r);
                int cmp = (r * 2).CompareTo(dd);
                if (cmp > 0 || (cmp == 0 && !q.IsEven)) q += 1;
                BigInteger lo = BigInteger.Pow(10, prec - 1), hi = BigInteger.Pow(10, prec);
                if (q >= hi)
                {
                    // either the estimate was too low or rounding carried
                    BigInteger q0 = BigInteger.Divide(nn, dd);
                    if (q0 >= hi) { k++; continue; }
                    digits = (q / 10).ToString(CultureInfo.InvariantCulture);
                    exp = k + 1;
                    return;
                }
                if (q < lo) { k--; continue; }
                digits = q.ToString(CultureInfo.InvariantCulture);
                exp = k;
                return;
            }
            throw new InvalidOperationException("digit generation failed");
        }

        /// <summary>Python / C "%.{prec}g".</summary>
        public static string FormatG(double v, int prec = 6)
        {
            if (prec == 0) prec = 1;
            if (double.IsNaN(v)) return "nan";
            if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
            string sign = (v < 0 || (v == 0 && 1 / v < 0)) ? "-" : "";
            if (v == 0) return sign + "0";
            Digits(v, prec, out string d, out int x);
            if (x < -4 || x >= prec)
            {
                string mant = d.Substring(0, 1);
                string rest = d.Substring(1).TrimEnd('0');
                if (rest.Length > 0) mant += "." + rest;
                return sign + mant + "e" + (x < 0 ? "-" : "+") + Math.Abs(x).ToString("00", CultureInfo.InvariantCulture);
            }
            string s;
            if (x >= 0)
            {
                string ip = d.Substring(0, x + 1);
                string fp = d.Substring(x + 1).TrimEnd('0');
                s = fp.Length > 0 ? ip + "." + fp : ip;
            }
            else
            {
                string fp = (new string('0', -x - 1) + d).TrimEnd('0');
                s = "0." + fp;
            }
            return sign + s;
        }

        /// <summary>Python repr(float).</summary>
        public static string PyReprFloat(double v)
        {
            if (double.IsNaN(v)) return "nan";
            if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
            if (v == 0) return (1 / v < 0) ? "-0.0" : "0.0";
            string sign = v < 0 ? "-" : "";
            string d = null; int x = 0;
            for (int p = 1; p <= 17; p++)
            {
                Digits(v, p, out d, out x);
                string sci = d.Substring(0, 1) + (d.Length > 1 ? "." + d.Substring(1) : "") + "e" + x;
                if (ParseDouble(sci) == Math.Abs(v)) break;
            }
            d = d.TrimEnd('0');
            if (d.Length == 0) d = "0";
            if (x >= -5 && x < 16)
            {
                if (x >= 0)
                {
                    string ip = d.Length > x + 1 ? d.Substring(0, x + 1) : d + new string('0', x + 1 - d.Length);
                    string fp = d.Length > x + 1 ? d.Substring(x + 1) : "";
                    return sign + ip + "." + (fp.Length > 0 ? fp : "0");
                }
                return sign + "0." + new string('0', -x - 1) + d;
            }
            string m = d.Substring(0, 1) + (d.Length > 1 ? "." + d.Substring(1) : "");
            return sign + m + "e" + (x < 0 ? "-" : "+") + Math.Abs(x).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Python repr(str) (single quotes unless the text contains ' and no ").</summary>
        public static string PyRepr(string s)
        {
            char q = s.Contains("'") && !s.Contains("\"") ? '"' : '\'';
            var sb = new StringBuilder();
            sb.Append(q);
            foreach (char c in s)
            {
                if (c == q || c == '\\') { sb.Append('\\').Append(c); }
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20 || (c >= 0x7F && c <= 0xA0) || c == 0xAD) sb.Append("\\x").Append(((int)c).ToString("x2"));
                else sb.Append(c);
            }
            sb.Append(q);
            return sb.ToString();
        }

        public static string Hex32(long v) => "0x" + (v & 0xFFFFFFFFL).ToString("X8");

        public static string Join<T>(string sep, IEnumerable<T> items) => string.Join(sep, items);

        /// <summary>Python os.path.commonprefix (character by character).</summary>
        public static string CommonPrefix(IList<string> items)
        {
            if (items.Count == 0) return "";
            string a = items[0];
            foreach (var s in items) if (string.CompareOrdinal(s, a) < 0) a = s;
            string b = items[0];
            foreach (var s in items) if (string.CompareOrdinal(s, b) > 0) b = s;
            int i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
            return a.Substring(0, i);
        }

        /// <summary>CRC-32 (zlib.crc32).</summary>
        public static uint Crc32(byte[] data, int off = 0, int len = -1, uint crc = 0)
        {
            if (crcTable == null)
            {
                var t = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    t[n] = c;
                }
                crcTable = t;
            }
            if (len < 0) len = data.Length - off;
            uint x = crc ^ 0xFFFFFFFFu;
            for (int i = off; i < off + len; i++) x = crcTable[(x ^ data[i]) & 0xFF] ^ (x >> 8);
            return x ^ 0xFFFFFFFFu;
        }
        static uint[] crcTable;
    }

    /// <summary>Little-endian binary writer for the .dat formats.</summary>
    public sealed class ByteWriter
    {
        readonly MemoryStream ms = new MemoryStream();
        public void U8(long v) => ms.WriteByte((byte)v);
        public void U16(long v) { ms.WriteByte((byte)v); ms.WriteByte((byte)(v >> 8)); }
        public void U32(long v) { U16(v & 0xFFFF); U16((v >> 16) & 0xFFFF); }
        public void I16(long v) => U16(v & 0xFFFF);
        public void F32(float f) { var b = BitConverter.GetBytes(f); ms.Write(b, 0, 4); }
        public void Bytes(byte[] b) => ms.Write(b, 0, b.Length);
        public int Length => (int)ms.Length;
        public byte[] ToArray() => ms.ToArray();
    }

    /// <summary>Little-endian reader; throws IndexOutOfRange-like errors at the end of data.</summary>
    public sealed class ByteReader
    {
        readonly byte[] d;
        public int P;
        public ByteReader(byte[] data, int pos = 0) { d = data; P = pos; }
        public int Length => d.Length;
        public byte this[int i] { get { if (i < 0 || i >= d.Length) throw new EndOfStreamException("unexpected end of data"); return d[i]; } }
        void Need(int n) { if (P + n > d.Length) throw new EndOfStreamException("unexpected end of data"); }
        public int U8() { Need(1); return d[P++]; }
        public int U16() { Need(2); int v = d[P] | d[P + 1] << 8; P += 2; return v; }
        public short I16() => (short)U16();
        public long U32() { Need(4); long v = (uint)(d[P] | d[P + 1] << 8 | d[P + 2] << 16 | d[P + 3] << 24); P += 4; return v; }
        public float F32() { Need(4); float f = BitConverter.ToSingle(d, P); P += 4; return f; }
        public string PStr() { int n = U8(); Need(n); string s = Text.Latin1.GetString(d, P, n); P += n; return s; }
        public string LStr() { long n = U32(); if (n > d.Length) throw new EndOfStreamException("unexpected end of data"); Need((int)n); string s = Text.Latin1.GetString(d, P, (int)n); P += (int)n; return s; }
    }
}

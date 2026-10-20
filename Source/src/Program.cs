// InterfaceTools: one program for the interface files of the Reality Pump games
// (Earth 2150, KnightShift, Earth 2160, TwoWorlds, Two Worlds II; the Earth 2150 format is also used by
// Heli Heroes, World War III: Black Gold and World War II: Panzer Claws / Frontline Attack: War over Europe).
// Without arguments it starts the graphical editor; with a command it works as a console tool.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace InterfaceTools
{
    public static class Program
    {
        public const string Version = "1.0";

        static readonly string Usage =
            "InterfaceTools " + Version + " - interface files of Earth 2150, KnightShift, Earth 2160, TwoWorlds and Two Worlds II\n" +
            "(the Earth 2150 format is also used by " + Fmt.E2150Family + ")\n" +
            "\n" +
            "Usage:\n" +
            "  InterfaceTools                         graphical editor\n" +
            "  InterfaceTools edit [file.dat]         graphical editor with a file\n" +
            "  InterfaceTools compile [--ks|--e2160|--tw|--tw2] dialogs.txt dialogs.dat\n" +
            "  InterfaceTools decompile file.dat [output_folder] [--resource file.h] [--names original.txt | --no-names]\n" +
            "                 [--tile name=pixels ...] [--width pixels] [--width name=pixels ...] [--format f]\n" +
            "  InterfaceTools dump file.dat\n" +
            "  InterfaceTools diff old.dat new.dat [--resource file.h] [--names original.txt] [--brief]\n" +
            "  InterfaceTools export-all [WorkFolder] [--no-pause]   export everything in the Work folder\n" +
            "  InterfaceTools import-all [WorkFolder] [options]      pack everything back (see import-all --help)\n" +
            "  InterfaceTools dds export|import|create|info|formats ... (see dds --help)\n" +
            "  InterfaceTools addfileinfo <source> <dest> [options]  /  addfileinfo -extract <file> <out> [-crlf]\n" +
            "  InterfaceTools tiles DAT_DIR [TEXTURE_NAME] [--tex FILE.tex] | --textures\n";

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length == 0 || args[0].Equals("edit", StringComparison.OrdinalIgnoreCase))
            {
                string file = args.Length > 1 ? args[1] : null;
                return RunGui(file);
            }
            if (args.Length == 1 && File.Exists(args[0]) && !args[0].StartsWith("-"))
                return RunGui(args[0]);   // a file dropped onto the program
            AttachConsole();
            string cmd = args[0].ToLowerInvariant();
            var rest = args.Skip(1).ToList();
            try
            {
                switch (cmd)
                {
                    case "compile": return Compiler.Run(rest);
                    case "decompile": return Decompiler.Run(rest);
                    case "dump": return DatDump.Run(rest);
                    case "diff": return DatDiff.Run(rest);
                    case "-h": case "--help": case "help": case "/?": Log.Line(Usage); return 0;
                    default:
                        int r;
                        if (Extra.TryRun(cmd, rest, out r)) return r;
                        Log.Line($"Unknown command '{args[0]}'.\n");
                        Log.Line(Usage);
                        return 1;
                }
            }
            catch (ToolError e) { Log.Line(e.Message); return 1; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Log.Line($"Error: {e.Message}");
                return 1;
            }
        }

        static int RunGui(string file)
        {
            Extra.Init();
            if (Extra.Gui == null) { AttachConsole(); Log.Line("The graphical editor is not available in this build."); return 1; }
            return Extra.Gui(file);
        }

        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int n);
        [DllImport("kernel32.dll")] static extern uint GetFileType(IntPtr h);

        /// <summary>The program is a Windows application (no console window for the editor). Its output goes
        /// to a file or pipe when it is redirected (InterfaceTools dump x.dat > report.txt); otherwise, when
        /// it is run from cmd / PowerShell / a .bat file, it attaches to that console so the output is visible.</summary>
        static void AttachConsole()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            try
            {
                IntPtr h = GetStdHandle(-11);
                bool valid = h != IntPtr.Zero && h != new IntPtr(-1) && GetFileType(h) != 0;
                if (!valid && !AttachConsole(-1)) return;
                var o = new StreamWriter(Console.OpenStandardOutput(), Console.OutputEncoding) { AutoFlush = true };
                Console.SetOut(o);
                IntPtr he = GetStdHandle(-12);
                bool errValid = he != IntPtr.Zero && he != new IntPtr(-1) && GetFileType(he) != 0;
                Console.SetError(errValid ? new StreamWriter(Console.OpenStandardError(), Console.OutputEncoding) { AutoFlush = true } : o);
                if (!valid) Console.WriteLine();
            }
            catch (Exception) { }
        }
    }

    /// <summary>Hooks for the parts that are compiled in separate files (GUI, pipeline, DDS tool).</summary>
    public static partial class Extra
    {
        public static Func<string, int> Gui;
        static readonly Dictionary<string, Func<IList<string>, int>> commands = new Dictionary<string, Func<IList<string>, int>>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string name, Func<IList<string>, int> fn) => commands[name] = fn;

        static bool registered;
        static partial void RegisterAll();

        public static void Init() { if (!registered) { registered = true; RegisterAll(); } }

        public static bool TryRun(string cmd, IList<string> args, out int result)
        {
            Init();
            if (commands.TryGetValue(cmd, out var fn)) { result = fn(args); return true; }
            result = 1;
            return false;
        }
    }
}

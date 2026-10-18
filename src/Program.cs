using System;
using System.Threading;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TigEditor
{
    static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("winmm.dll")]
        private static extern int timeBeginPeriod(int ms);
        [DllImport("winmm.dll")]
        private static extern int timeEndPeriod(int ms);

        // One application name for every window: the editor gets translated and
        // keeping it in one place is simpler.
        public const string AppName = "The Impossible Editor";

        [STAThread]
        static void Main(string[] args)
        {
            // Interface text is loaded first, everything else wants it. The
            // language comes from the Language setting, English by default.
            Settings prefs = new Settings();
            string lang = prefs.Get("Language", "");
            S.Load(lang.Length > 0 ? lang : S.SystemLanguage());

            // A crash is written next to the editor: without that nothing can be
            // said about it, especially if the window closed quietly.
            AppDomain.CurrentDomain.UnhandledException += delegate(object s2, UnhandledExceptionEventArgs e2)
            {
                WriteCrash(e2.ExceptionObject as Exception);
            };
            Application.ThreadException += delegate(object s2, ThreadExceptionEventArgs e2)
            {
                WriteCrash(e2.Exception);
            };

            // The decoder libraries sit as resources inside the exe and are taken
            // out only when they are really needed, on opening music.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;

            // The manifest already declares the app DPI aware, but a run without
            // the manifest is covered by the call. XP has no such function, hence
            // the try.
            try { if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware(); }
            catch { }

            // System timer accuracy of 1 ms: without it animation frames come out
            // about 64 times a second, which shows on a fast monitor.
            try { timeBeginPeriod(1); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                // A level folder can be dropped on the editor, and its path then
                // arrives as the first command line argument. If it is not a
                // level we say so at once, without opening the editor.
                string open = null;
                if (args != null && args.Length > 0)
                {
                    string why = LevelFiles.WhyNotLevel(args[0]);
                    if (why != null)
                    {
                        MessageBox.Show(why, AppName, MessageBoxButtons.OK,
                                        MessageBoxIcon.Information);
                        return;
                    }
                    open = args[0];
                }

                MainForm form = new MainForm();
                if (open != null)
                    form.Shown += delegate { form.OpenFromCommandLine(open); };
                Application.Run(form);
            }
            finally
            {
                try { timeEndPeriod(1); } catch { }
            }
        }

        // The sound libraries sit inside compressed, halving the build size.
        private static Assembly ResolveEmbedded(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name + ".dll.gz";
            using (Stream packed = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (packed == null) return null;
                using (GZipStream unpacked = new GZipStream(packed, CompressionMode.Decompress))
                using (MemoryStream all = new MemoryStream())
                {
                    byte[] chunk = new byte[16384];
                    int n;
                    while ((n = unpacked.Read(chunk, 0, chunk.Length)) > 0) all.Write(chunk, 0, n);
                    return Assembly.Load(all.ToArray());
                }
            }
        }

        private static void WriteCrash(Exception ex)
        {
            try
            {
                string dir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                File.AppendAllText(Path.Combine(dir, "editor-crash.log"),
                    DateTime.Now + Environment.NewLine + (ex == null ? S.Unknown : ex.ToString())
                    + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }
}

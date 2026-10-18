using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace TigEditor
{
    // Everything the editor does with a level folder. Dialogs and messages stay
    // in the window, only file work lives here.
    public static class LevelFiles
    {
        public const string LevelFileName = "level.dat";

        // Why a path will not do as a level, empty when it will. The answer is
        // short because it is shown before the editor opens.
        public static string WhyNotLevel(string path)
        {
            if (string.IsNullOrEmpty(path)) return S.ErrNoPath;

            string folder = null;
            if (Directory.Exists(path)) folder = path;
            else if (File.Exists(path))
            {
                if (Path.GetFileName(path).ToLowerInvariant() != LevelFileName)
                    return S.ErrNotLevelFile + LevelFileName + ".";
                folder = Path.GetDirectoryName(path);
            }
            else return S.ErrNoSuchPath;

            string dat = Path.Combine(folder, LevelFileName);
            if (!File.Exists(dat))
                return string.Format(S.ErrFolderNotLevel, LevelFileName);

            try
            {
                Level level = Level.Load(dat);
                if (level == null) return S.ErrLevelRead;
            }
            catch (Exception ex)
            {
                return S.ErrLevelBroken + ex.Message;
            }
            return null;
        }

        public static string FindMusic(string folder)
        {
            if (folder == null) return null;
            string mp3 = Path.Combine(folder, "music.mp3");
            if (File.Exists(mp3)) return mp3;
            string ogg = Path.Combine(folder, "music.ogg");
            if (File.Exists(ogg)) return ogg;
            return null;
        }

        public static bool IsLevelFolder(string folder)
        {
            return folder != null && File.Exists(Path.Combine(folder, LevelFileName));
        }

        // Saving under a new name carries the whole folder: music, custom art and
        // any other files that lie there. level.dat is left alone, the editor
        // writes that itself.
        public static void CopyExtras(string from, string to)
        {
            if (from == null || to == null) return;
            if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase)) return;
            if (!Directory.Exists(from)) return;

            foreach (string file in Directory.GetFiles(from))
            {
                string name = Path.GetFileName(file);
                if (name.ToLowerInvariant() == "level.dat") continue;
                File.Copy(file, Path.Combine(to, name), true);
            }
        }

        // Where to open the game search dialog: next to a game we already know,
        // next to the levels folder, since the game usually sits a level above,
        // or, knowing nothing, where the editor itself lies.
        public static string GuessGameFolder(Settings settings)
        {
            string game = settings.Get("GamePath", "");
            if (game.Length > 0 && File.Exists(game)) return Path.GetDirectoryName(game);

            string levels = settings.Get("LevelsRoot", "");
            if (levels.Length > 0 && Directory.Exists(levels))
            {
                string found = FindGameAbove(levels);
                if (found != null) return Path.GetDirectoryName(found);
            }
            return Path.GetDirectoryName(Application.ExecutablePath);
        }

        // We look for ImpossibleGame.exe in this folder and above: levels usually
        // sit in levels next to the game, and a level one folder deeper.
        public static string FindGameAbove(string folder)
        {
            for (int i = 0; i < 4 && folder != null; i++)
            {
                try
                {
                    string exe = Path.Combine(folder, "ImpossibleGame.exe");
                    if (File.Exists(exe)) return exe;
                    folder = Path.GetDirectoryName(folder);
                }
                catch { return null; }
            }
            return null;
        }

        public static string LevelsFolder(Settings settings)
        {
            string game = settings.Get("GamePath", "");
            if (game.Length > 0 && File.Exists(game))
            {
                // The game ships with an editor and levels usually lie in its
                // folder, so that one is checked first.
                string gameRoot = Path.GetDirectoryName(game);
                string inEditor = Path.Combine(Path.Combine(gameRoot, "editor"), "levels");
                if (Directory.Exists(inEditor)) return inEditor;
                string levels = Path.Combine(gameRoot, "levels");
                if (Directory.Exists(levels)) return levels;
            }
            string root = settings.Get("LevelsRoot", "");
            if (root.Length > 0 && Directory.Exists(root)) return root;
            return null;
        }
    }

    // The level settings file next to level.dat, read by a patched game.
    public static class LevelSettings
    {
        public const string FileName = "level.ini";

        public static string PathFor(string levelFolder)
        {
            return levelFolder == null ? null : Path.Combine(levelFolder, FileName);
        }

        // We write impulse and gravity and leave the other lines alone.
        public static void WritePhysics(string levelFolder, float jump, double gravity)
        {
            Write(levelFolder, Lines(levelFolder, jump, gravity, true));
        }

        // Only physics is removed, and if that leaves the file empty it goes too.
        public static void ClearPhysics(string levelFolder)
        {
            Write(levelFolder, Lines(levelFolder, 0, 0, false));
        }


        private static List<string> Lines(string levelFolder, float jump, double gravity, bool add)
        {
            List<string> lines = new List<string>();
            foreach (string line in Read(levelFolder))
                if (!IsPhysics(line)) lines.Add(line);

            if (add)
            {
                CultureInfo ci = CultureInfo.InvariantCulture;
                // The game wants impulse and gravity in its own units. Height and
                // length are converted by the editor and never written.
                lines.Add("jump = " + jump.ToString("0.###", ci));
                lines.Add("gravity = " + gravity.ToString("0.###", ci));
            }
            return lines;
        }

        private static bool IsPhysics(string line)
        {
            string key = line.Trim().ToLowerInvariant();
            return key.StartsWith("jump") || key.StartsWith("gravity")
                || key.StartsWith("height") || key.StartsWith("length");
        }

        private static string[] Read(string levelFolder)
        {
            string path = PathFor(levelFolder);
            try
            {
                if (path != null && File.Exists(path)) return File.ReadAllLines(path);
            }
            catch { }
            return new string[0];
        }

        private static void Write(string levelFolder, List<string> lines)
        {
            string path = PathFor(levelFolder);
            if (path == null) return;

            bool empty = true;
            foreach (string line in lines) if (line.Trim().Length > 0) empty = false;

            try
            {
                if (empty)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                File.WriteAllLines(path, lines.ToArray());
            }
            catch { }
        }
    }

    // Starting the game to test a level, exactly as the Java editor does it: the
    // working folder is the game folder, the level goes to editorcache/test, and
    // the command line reads log level editorcache/test.
    public static class GameLauncher
    {
        // Returns the started process, so its window can be fixed up.
        public static Process Run(string gamePath, Level level, string musicPath, string levelFolder,
                                  bool noClip, GamePhysics.Preset physics)
        {
            string gameDir = Path.GetDirectoryName(gamePath);
            string cacheDir = Path.Combine(Path.Combine(gameDir, "editorcache"), "test");
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
            Directory.CreateDirectory(cacheDir);

            level.Save(Path.Combine(cacheDir, LevelFiles.LevelFileName));

            // The game looks for a file named music, whatever the user called it.
            if (musicPath != null && File.Exists(musicPath))
                File.Copy(musicPath,
                          Path.Combine(cacheDir, "music" + Path.GetExtension(musicPath).ToLowerInvariant()),
                          true);

            // Everything the game can read goes into the test folder: physics
            // settings and custom atlases. Otherwise the test plays without them.
            if (levelFolder != null && Directory.Exists(levelFolder))
            {
                string ini = Path.Combine(levelFolder, LevelSettings.FileName);
                if (File.Exists(ini)) File.Copy(ini, Path.Combine(cacheDir, LevelSettings.FileName), true);

                // Everything the game can replace with its own: level art and
                // sounds. The names match the files in res, so we take the list.
                foreach (string file in Directory.GetFiles(levelFolder))
                {
                    string name = Path.GetFileName(file);
                    string ext = Path.GetExtension(name).ToLowerInvariant();
                    if (name.ToLowerInvariant() == "level.dat") continue;
                    if (ext == ".png" || ext == ".wav" || ext == ".bin" || ext == ".dat"
                        || ext == ".ogg" || ext == ".mp3")
                        File.Copy(file, Path.Combine(cacheDir, name), true);
                }
            }

            // Physics comes from the editor, not from the level folder, so it
            // takes effect at once even on an unsaved level.
            if (physics == null) LevelSettings.ClearPhysics(cacheDir);
            else LevelSettings.WritePhysics(cacheDir, physics.Jump, physics.Gravity);

            // Noclip is turned on by the game itself when patched, the editor
            // only passes the flag on the command line.
            string args = "log level editorcache/test" + (noClip ? " noclip" : "");
            ProcessStartInfo psi = new ProcessStartInfo(gamePath, args);
            psi.WorkingDirectory = gameDir;
            psi.UseShellExecute = false;
            return Process.Start(psi);
        }
    }
}

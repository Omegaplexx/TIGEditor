using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TigEditor
{
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
			string args = "log level editorcache/test" + (noClip ? " noclip" : string.Empty);
			ProcessStartInfo psi = new ProcessStartInfo(gamePath, args);
			psi.WorkingDirectory = gameDir;
			psi.UseShellExecute = false;
			return Process.Start(psi);
		}
	}

	// The game ignores Windows interface scaling and its window drifts off
	// centre above 100%, so the editor centres it in real pixels.
	public static class GameWindow
	{
		private const uint SwpNoSize = 0x0001;
		private const uint SwpNoZOrder = 0x0004;
		private const uint SwpNoActivate = 0x0010;
		private const int SwShowMinNoActive = 7;
		private const uint GaRootOwner = 3;

		[StructLayout(LayoutKind.Sequential)]
		private struct Rect { public int Left, Top, Right, Bottom; }

		[DllImport("user32.dll")]
		private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
		[DllImport("user32.dll")]
		private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter,
			int x, int y, int cx, int cy, uint flags);
		[DllImport("user32.dll")]
		private static extern bool ShowWindow(IntPtr hWnd, int cmdShow);
		[DllImport("user32.dll")]
		private static extern bool IsWindowVisible(IntPtr hWnd);
		[DllImport("user32.dll")]
		private static extern bool IsIconic(IntPtr hWnd);
		[DllImport("user32.dll")]
		private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
		[DllImport("kernel32.dll")]
		private static extern bool AttachConsole(uint processId);
		[DllImport("kernel32.dll")]
		private static extern bool FreeConsole();
		[DllImport("kernel32.dll")]
		private static extern IntPtr GetConsoleWindow();

		// The game writes its log to a console of its own. It is minimised, or
		// it sits over the editor and the game window. The editor has no console,
		// so it borrows the game's for a moment to reach the window.
		public static void MinimizeConsole(Process game, int seconds)
		{
			if (game == null) return;
			if (TryMinimizeConsole(game)) return;

			Timer timer = new Timer();
			timer.Interval = 25;
			int ticks = 0, limit = Math.Max(1, seconds * 1000 / timer.Interval);
			timer.Tick += delegate
			{
				ticks++;
				bool done = ticks >= limit;
				try { if (TryMinimizeConsole(game)) done = true; }
				catch { done = true; }
				if (done)
				{
					timer.Stop();
					timer.Dispose();
				}
			};
			timer.Start();
		}

		// False while the console does not exist yet.
		private static bool TryMinimizeConsole(Process game)
		{
			if (game.HasExited) return true;
			if (!AttachConsole((uint)game.Id)) return false;
			try
			{
				IntPtr hwnd = GetConsoleWindow();
				if (hwnd == IntPtr.Zero) return false;

				// A terminal that hosts the console keeps it as a hidden window
				// owned by the terminal window, which is what must be minimised.
				if (!IsWindowVisible(hwnd))
				{
					IntPtr owner = GetAncestor(hwnd, GaRootOwner);
					if (owner != IntPtr.Zero) hwnd = owner;
				}
				ShowWindow(hwnd, SwShowMinNoActive);
				return true;
			}
			finally { FreeConsole(); }
		}

		// The window appears late and may resize, so it is watched for a while.
		public static void CenterWhenReady(Process game, int seconds)
		{
			if (game == null) return;

			Timer timer = new Timer();
			timer.Interval = 150;
			int ticks = 0, limit = Math.Max(1, seconds * 1000 / timer.Interval);
			Size lastSize = Size.Empty;

			timer.Tick += delegate
			{
				ticks++;
				bool done = ticks >= limit;
				try
				{
					if (game.HasExited) done = true;
					else
					{
						game.Refresh();
						IntPtr hwnd = game.MainWindowHandle;
						Rect r;
						if (hwnd != IntPtr.Zero && !IsIconic(hwnd) && GetWindowRect(hwnd, out r))
						{
							Size size = new Size(r.Right - r.Left, r.Bottom - r.Top);
							if (size.Width > 0 && size.Height > 0 && size != lastSize)
							{
								lastSize = size;
								Center(hwnd, size);
							}
						}
					}
				}
				catch { done = true; }

				if (done)
				{
					timer.Stop();
					timer.Dispose();
				}
			};
			timer.Start();
		}

		private static void Center(IntPtr hwnd, Size size)
		{
			Rectangle area = Screen.FromHandle(hwnd).WorkingArea;
			int x = area.Left + (area.Width - size.Width) / 2;
			int y = area.Top + (area.Height - size.Height) / 2;
			SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
		}
	}
}

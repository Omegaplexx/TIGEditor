using System;
using System.IO;

namespace TigEditor
{
	// Patching the game file. Every feature is independent: the code section is
	// appended once and each item is turned on by a jump of its own into it.
	// Before the first patch the original file is saved next to it.
	public static class GamePatcher
	{
		public const string BackupSuffix = ".original";

		public class State
		{
			public bool Valid;
			public string Error;
			public bool HasBackup;
			public bool NoClipKey;          // F1 in the game and the noclip argument
			public bool LevelExtras;        // level.ini and custom level graphics
			public bool MedalFix;           // the false Medal Unlocked banner
			public bool RememberFolder;     // the level choosing dialog
			public bool MusicSync;          // music shifted to the level start
			public int HoldMs;              // level hold, milliseconds
			public bool DpiAware;           // our own screen resolution
		}

		public static string BackupPath(string exePath) { return exePath + BackupSuffix; }

		// The state is remembered while the file is unchanged: the dialog is
		// opened often and parsing the same file every time serves nothing.
		private static string lastPath;
		private static DateTime lastStamp;
		private static long lastSize;
		private static State lastState;

		public static State Read(string exePath)
		{
			try
			{
				FileInfo info = new FileInfo(exePath);
				if (lastState != null && lastPath == exePath
					&& lastStamp == info.LastWriteTimeUtc && lastSize == info.Length)
					return lastState;
				lastPath = exePath;
				lastStamp = info.LastWriteTimeUtc;
				lastSize = info.Length;
			}
			catch { lastState = null; }

			State state = new State();
			byte[] file;
			try { file = File.ReadAllBytes(exePath); }
			catch (Exception ex) { state.Error = ex.Message; return state; }

			if (!Check(file, out state.Error)) return state;

			state.HasBackup = File.Exists(BackupPath(exePath));
			if (GameCodePatch.HasSection(file))
			{
				state.NoClipKey = GameCodePatch.NoClipReady(file);
				state.LevelExtras = GameCodePatch.LevelHookOn(file) && GameCodePatch.TextureHookOn(file);
				state.MedalFix = GameCodePatch.MedalHookOn(file);
				state.RememberFolder = GameCodePatch.FolderHookOn(file);
				state.HoldMs = GameCodePatch.HoldExtraMs(file);
				state.MusicSync = GameCodePatch.PinOn(file);
			}
			state.DpiAware = GameCodePatch.IsDpiAware(file);
			state.Valid = true;
			lastState = state;
			return state;
		}

		// We make sure this is a build we know: every patch site must look either
		// like the original game or like our patch.
		private static bool Check(byte[] file, out string error)
		{
			error = null;
			if (file.Length < GameCodePatch.ManifestOffset + GameCodePatch.ManifestLength)
			{ error = S.Get("ErrFileTooSmall"); return false; }

			if (                !Known(file, GameCodePatch.ImageHookOffset, GameCodePatch.ImageOriginal) ||
				!Known(file, GameCodePatch.TextureHookOffset, GameCodePatch.TextureOriginal) ||
				!Known(file, GameCodePatch.MedalHookOffset, GameCodePatch.MedalOriginal) ||
				!Known(file, GameCodePatch.KeyHookOffset, GameCodePatch.KeyOriginal) ||
				!Known(file, GameCodePatch.LevelHookOffset, GameCodePatch.LevelOriginal))
			{ error = S.Get("ErrUnknownBuild"); return false; }

			return true;
		}

		private static bool Known(byte[] file, int offset, byte[] original)
		{
			if (file[offset] == 0xE9 || file[offset] == 0xE8) return true;   // already our jump
			for (int i = 0; i < original.Length; i++)
				if (file[offset + i] != original[i]) return false;
			return true;
		}

		public static bool Apply(string exePath, State wanted, out string error)
		{
			error = null;
			byte[] file;
			try { file = File.ReadAllBytes(exePath); }
			catch (Exception ex) { error = ex.Message; return false; }

			if (!Check(file, out error)) return false;

			string backup = BackupPath(exePath);
			try
			{
				if (!File.Exists(backup)) File.WriteAllBytes(backup, file);
			}
			catch (Exception ex) { error = S.Get("ErrBackupFailed") + ex.Message; return false; }

			bool needSection = wanted.NoClipKey || wanted.LevelExtras ||
							   wanted.MedalFix || wanted.RememberFolder ||
							   wanted.MusicSync;
			if (needSection)
			{
				byte[] extended;
				if (!GameCodePatch.EnsureSection(file, out extended, out error)) return false;
				file = extended;
			}

			if (GameCodePatch.HasSection(file))
			{

				GameCodePatch.SetLevelHook(file, wanted.LevelExtras);
				GameCodePatch.SetTextureHook(file, wanted.LevelExtras);
				// The levels folder is prepared inside the event poll hook, so
				// the folder patch wants it too, not just noclip.
				GameCodePatch.SetKeyHook(file, wanted.NoClipKey || wanted.RememberFolder
												|| wanted.LevelExtras);
				GameCodePatch.SetNoClip(file, wanted.NoClipKey);
				GameCodePatch.SetKeyFlags(file, wanted.NoClipKey, wanted.LevelExtras);
				GameCodePatch.SetMedalHook(file, wanted.MedalFix);
				GameCodePatch.SetFolderHook(file, wanted.RememberFolder);
				GameCodePatch.SetSyncHook(file, wanted.MusicSync || wanted.LevelExtras);
				GameCodePatch.SetSyncOptions(file, wanted.MusicSync,
											 wanted.MusicSync ? wanted.HoldMs : 0);

				// The art restore stubs lie in the section back to back, and if
				// their layout does not add up a jump lands in someone else's
				// code. Better to write no patch at all than to crash the game.
				if (wanted.LevelExtras && !GameCodePatch.ResetStubsLookRight(file))
				{
					error = S.Get("ErrPatchCheck");
					return false;
				}
			}

			byte[] original = null;
			try { if (File.Exists(backup)) original = File.ReadAllBytes(backup); }
			catch { }
			GameCodePatch.SetDpiAware(file, wanted.DpiAware, original);

			lastState = null;   // the file changes, the old state cannot be kept
			try { File.WriteAllBytes(exePath, file); }
			catch (Exception ex)
			{
				error = S.Get("ErrWriteGame") + ex.Message +
						S.Get("ErrCloseGameHint");
				return false;
			}
			return true;
		}

		public static bool Restore(string exePath, out string error)
		{
			lastState = null;   // the file goes back to the original
			error = null;
			string backup = BackupPath(exePath);
			if (!File.Exists(backup)) { error = S.Get("ErrNoBackup"); return false; }

			try
			{
				File.Copy(backup, exePath, true);
				File.Delete(backup);      // the backup is not needed, the file is original again
			}
			catch (Exception ex)
			{
				error = S.Get("ErrRestoreFailed") + ex.Message +
						S.Get("ErrCloseGameHint");
				return false;
			}
			return true;
		}
	}
}

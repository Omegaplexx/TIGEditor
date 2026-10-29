using System;
using System.IO;
using TigEditor;

// Every check of the game patches in one place: addresses, the shape of each
// patch, stub sizes, built in levels, key permissions and mutual overwriting.
class PatchGuard
{
	static bool ok = true;

	static void Say(bool good, string what)
	{
		ok &= good;
		Console.WriteLine("{0,-46} {1}", what, good ? "yes" : "NO");
	}

	static void Main(string[] a)
	{
		if (a.Length == 0) { Console.WriteLine("give a path to ImpossibleGame.exe"); return; }
		byte[] file = File.ReadAllBytes(a[0]);

		Known(file, GameCodePatch.ImageHookOffset, GameCodePatch.ImageOriginal, "image loading");
		Known(file, GameCodePatch.TextureHookOffset, GameCodePatch.TextureOriginal, "level atlas");
		Known(file, GameCodePatch.MedalHookOffset, GameCodePatch.MedalOriginal, "medal");
		Known(file, GameCodePatch.KeyHookOffset, GameCodePatch.KeyOriginal, "event poll");
		Known(file, GameCodePatch.LevelHookOffset, GameCodePatch.LevelOriginal, "level path");
		Known(file, GameCodePatch.DecodeCallOffset, GameCodePatch.DecodeOriginal, "track decoding");
		Known(file, GameCodePatch.PlayCallOffset, GameCodePatch.PlayOriginal, "sound start");
		Known(file, GameCodePatch.MemoryCallOffset, GameCodePatch.MemoryOriginal, "decoding from memory");
		Known(file, GameCodePatch.HoldHookOffset, GameCodePatch.HoldOriginal, "level hold");
		for (int i = 0; i < GameCodePatch.OpenOffsets.Length; i++)
			Known(file, GameCodePatch.OpenOffsets[i], GameCodePatch.OpenOriginal,
				  "file opening " + (i + 1));
		// A clean file holds the original store instruction, a patched one a jump.
		foreach (int off in GameCodePatch.ResetOffsets)
			Say(file[off] == 0xA3, "original bytes: level stub " + off.ToString("X"));

		string tmp = Path.GetTempFileName();
		string err;

		// Each patch on its own first, then all of them together.
		foreach (string one in new string[] { "immortal", "extras", "medal", "folder", "sync", "dpi" })
		{
			File.Copy(a[0], tmp, true);
			GamePatcher.State w = new GamePatcher.State();
			if (one == "immortal") w.NoClipKey = true;
			if (one == "extras") w.LevelExtras = true;
			if (one == "medal") w.MedalFix = true;
			if (one == "folder") w.RememberFolder = true;
			if (one == "sync") { w.MusicSync = true; w.HoldMs = 150; }
			if (one == "dpi") w.DpiAware = true;
			Say(GamePatcher.Apply(tmp, w, out err), "applies on its own: " + one);
		}

		// The extras stand alone: they need the event poll hook themselves.
		File.Copy(a[0], tmp, true);
		GamePatcher.State only = new GamePatcher.State();
		only.LevelExtras = true;
		GamePatcher.Apply(tmp, only, out err);
		byte[] one2 = File.ReadAllBytes(tmp);
		Say(one2[GameCodePatch.KeyHookOffset] == 0xE9, "the extras install the event poll hook");

		// All at once, and the patches do not overwrite each other.
		File.Copy(a[0], tmp, true);
		GamePatcher.State all = new GamePatcher.State();
		all.NoClipKey = all.LevelExtras = all.MedalFix = true;
		all.RememberFolder = all.MusicSync = all.DpiAware = true;
		all.HoldMs = 150;
		Say(GamePatcher.Apply(tmp, all, out err), "all of them apply together");
		byte[] every = File.ReadAllBytes(tmp);
		bool intact = true;
		foreach (int off in GameCodePatch.ResetOffsets) intact &= every[off] == 0xE9;
		Say(intact, "the level stubs survived");

		GamePatcher.State back = GamePatcher.Read(tmp);
		Say(back.NoClipKey && back.LevelExtras && back.MedalFix
			&& back.RememberFolder && back.MusicSync && back.DpiAware, "the state reads back");

		// Restoring gives the original file back byte for byte.
		GamePatcher.Restore(tmp, out err);
		Say(Same(File.ReadAllBytes(tmp), file), "restoring gives the original file");

		File.Delete(tmp);
		Console.WriteLine(ok ? "\nall patch checks passed" : "\nTHERE ARE FAILURES");
	}

	static void Known(byte[] file, int off, byte[] want, string what)
	{
		bool same = true;
		for (int i = 0; i < want.Length; i++) same &= file[off + i] == want[i];
		Say(same, "original bytes: " + what);
	}

	static bool Same(byte[] x, byte[] y)
	{
		if (x.Length != y.Length) return false;
		for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
		return true;
	}
}

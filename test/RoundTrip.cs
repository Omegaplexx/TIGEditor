using System;
using System.IO;
using TigEditor;

class RoundTrip
{
	static int Main(string[] args)
	{
		int bad = 0;
		foreach (string p in Directory.GetFiles(args[0], "*.dat"))
		{
			byte[] orig = File.ReadAllBytes(p);
			Level lv = Level.Load(p);
			byte[] again = lv.ToBytes();
			bool same = orig.Length == again.Length;
			if (same) for (int i = 0; i < orig.Length; i++) if (orig[i] != again[i]) { same = false; break; }
			if (!same) bad++;
			Console.WriteLine("{0,-26} obj={1,-5} end={2,-6} bg={3,-4} spec={4} grav={5} fall={6} rise={7} {8}",
				Path.GetFileName(p), lv.ObjectCount, lv.LevelEnd, lv.Backgrounds.Count,
				lv.UsesSpecialGraphics ? 1 : 0, lv.GravityChanges.Count, lv.BlocksFall.Count,
				lv.BlocksRise.Count, same ? "OK" : "MISMATCH");
		}
		Console.WriteLine(bad == 0 ? "every file matched byte for byte" : bad + " files did not match");
		return bad;
	}
}

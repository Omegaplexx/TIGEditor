using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using TigEditor;

// Jump physics from end to end: the presets, custom numbers that match none of
// them, and the trip through level.ini. All three used to be separate tests
// asking the same question, which is whether the numbers come back unchanged.
class PhysicsTest
{
	static int bad;

	static void Main()
	{
		Presets();
		Shape();
		Accuracy();
		IniRoundTrip();

		Console.WriteLine();
		Console.WriteLine(bad == 0 ? "all physics checks passed" : bad + " CHECKS FAILED");
	}

	static void Say(string what, bool good)
	{
		Console.WriteLine("{0,-44} {1}", what, good ? "yes" : "NO");
		if (!good) bad++;
	}

	// Every preset must recognise itself, and custom numbers must match none.
	static void Presets()
	{
		bool ok = true;
		for (int i = 0; i < GamePhysics.Presets.Length; i++)
		{
			GamePhysics.Preset p = GamePhysics.Presets[i];
			int found = GamePhysics.IndexOf(p.Jump, p.Gravity);
			Console.WriteLine("{0,-52} number {1}", p.Name, found);
			ok &= found == i;
		}
		Say("every preset recognises itself", ok);

		GamePhysics.Preset own = GamePhysics.Custom(24.5f, 2.15);
		int index = GamePhysics.IndexOf(own.Jump, own.Gravity);
		Console.WriteLine("custom 24.5 / 2.15: number {0}", index);
		Say("custom numbers match no preset", index < 0);
	}

	// Height and length must survive the trip into impulse and gravity and back,
	// and the drawn arc must agree with them.
	static void Shape()
	{
		Console.WriteLine();
		GamePhysics.Preset p = GamePhysics.FromShape(2.02, 4.8);
		double h, l;
		GamePhysics.Shape(p, out h, out l);
		Console.WriteLine("2.02 x 4.80 gives impulse {0:0.00}, gravity {1:0.000}", p.Jump, p.Gravity);
		Say("height and length come back", Math.Abs(h - 2.02) < 0.01 && Math.Abs(l - 4.8) < 0.01);

		List<PointF> path = GamePhysics.Trajectory(p, 0, 0, 240);
		float peak = 0;
		foreach (PointF q in path) if (q.Y > peak) peak = q.Y;
		double arcH = peak / Level.Grid;
		double arcL = path[path.Count - 1].X / Level.Grid;
		Console.WriteLine("the arc gives height {0:0.00}, length {1:0.00}, {2} frames",
			arcH, arcL, path.Count - 1);
		Say("the arc agrees with the numbers",
			Math.Abs(arcH - h) < 0.05 && Math.Abs(arcL - l) < 0.05);
	}

	// How exact the shape conversion is across the field, and whether every
	// shape fits the limits of hand entry. Without that last one, switching the
	// way of entry would quietly change the jump.
	static void Accuracy()
	{
		Console.WriteLine();
		Area(0.4, 4.0, 3.0, 12.0, "ordinary jumps: height to 4, length from 3");
		Area(0.4, 6.0, 2.0, 12.0, "the whole field");
		Area(2.0, 6.0, 2.0, 3.0, "the steep corner: high and short");

		int outside = 0;
		for (double h = GamePhysics.MinHeight; h <= GamePhysics.MaxHeight; h += 0.05)
			for (double l = GamePhysics.MinLength; l <= GamePhysics.MaxLength; l += 0.2)
			{
				GamePhysics.Preset p = GamePhysics.FromShape(h, l);
				if (p.Jump > GamePhysics.MaxJump || p.Gravity > GamePhysics.MaxGravity
					|| p.Jump < GamePhysics.MinJump || p.Gravity < GamePhysics.MinGravity)
					outside++;
			}
		Say("every shape fits the hand entry limits", outside == 0);

		double maxG = 0, maxJ = 0;
		for (double h = 0.4; h <= 4.0; h += 0.05)
			for (double l = 3.0; l <= 12.0; l += 0.2)
			{
				GamePhysics.Preset p = GamePhysics.FromShape(h, l);
				if (p.Gravity > maxG) maxG = p.Gravity;
				if (p.Jump > maxJ) maxJ = p.Jump;
			}
		Console.WriteLine("largest in the ordinary area: impulse {0:0.0}, gravity {1:0.00}",
			maxJ, maxG);
	}

	// One area of the field: how many height and length pairs come back changed.
	static void Area(double h0, double h1, double l0, double l1, string what)
	{
		int total = 0, off = 0;
		for (double H = h0; H <= h1; H += 0.05)
			for (double L = l0; L <= l1; L += 0.2)
			{
				double h, l;
				GamePhysics.Shape(GamePhysics.FromShape(H, L), out h, out l);
				total++;
				if (Math.Abs(h - H) > 0.006 || Math.Abs(l - L) > 0.006) off++;
			}
		Console.WriteLine("{0,-44} {1,5} pairs, {2,4} off", what, total, off);
	}

	// Custom numbers written to level.ini must be read back as they were.
	static void IniRoundTrip()
	{
		Console.WriteLine();
		string dir = Path.Combine(Path.GetTempPath(), "physics-ini");
		if (Directory.Exists(dir)) Directory.Delete(dir, true);
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, "level.ini"), "jump = 24.5\r\ngravity = 2.15\r\n");

		float jump;
		double gravity;
		bool got = Read(dir, out jump, out gravity);
		Console.WriteLine("read: {0} / {1}, physics present: {2}", jump, gravity, got);
		Say("the file was read", got);

		GamePhysics.Preset own = GamePhysics.Custom(jump, gravity);
		LevelSettings.WritePhysics(dir, own.Jump, own.Gravity);
		Console.WriteLine("in the file after writing:");
		Console.WriteLine(File.ReadAllText(Path.Combine(dir, "level.ini")).Trim());

		float j2;
		double g2;
		Read(dir, out j2, out g2);
		Say("custom numbers survived",
			Math.Abs(j2 - 24.5f) < 0.001 && Math.Abs(g2 - 2.15) < 0.001);
		Directory.Delete(dir, true);
	}

	// The same way the editor reads the file.
	static bool Read(string folder, out float jump, out double gravity)
	{
		jump = 0;
		gravity = 0;
		string path = Path.Combine(folder, "level.ini");
		if (!File.Exists(path)) return false;
		foreach (string line in File.ReadAllLines(path))
		{
			string[] parts = line.Split('=', ':');
			if (parts.Length < 2) continue;
			string key = parts[0].Trim().ToLowerInvariant();
			string value = parts[1].Trim().Replace(',', '.');
			if (key == "jump")
				float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out jump);
			else if (key == "gravity")
				double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out gravity);
		}
		return jump > 0 && gravity > 0;
	}
}

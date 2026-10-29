using System;

namespace TigEditor
{
	// Jump physics. The game keeps a pair of constants (impulse, gravity) for
	// user levels, built in levels and Phazd. Module offsets:
	//   impulse  float   0x4DCD8 user, 0x4DCDC built in, 0x4DCE0 Phazd
	//   gravity  double  0x4C8F8 user, 0x4DCF8 built in, 0x4DD00 Phazd
	public static class GamePhysics
	{
		// A game height unit is 2.5 times smaller than a level unit.
		public const double HeightScale = 2.5;

		// Horizontal speed, level units per frame.
		public const double StepX = 6.0;

		// Terminal fall speed, level units per frame.
		public const double TerminalFall = 26.666666;

		public class Preset
		{
			public readonly string Name;
			public readonly float Jump;       // game units
			public readonly double Gravity;

			public Preset(string name, float jump, double gravity)
			{
				Name = name;
				Jump = jump;
				Gravity = gravity;
			}

			// In level units, for the jump guide.
			public double JumpLevel { get { return Jump / HeightScale; } }
			public double GravityLevel { get { return Gravity / HeightScale; } }
		}

		public static readonly Preset[] Presets =
		{
			new Preset(S.Get("PhysPresetNormal"), 22.0f, 2.0),
			new Preset(S.Get("PhysPresetFar"), 20.0f, 1.7),
			new Preset(S.Get("PhysPresetHigh"), 26.0f, 2.3)
		};

		public static Preset Default { get { return Presets[0]; } }

		// Numbers from level.ini that match no preset.
		public static Preset Custom(float jump, double gravity)
		{
			return new Preset(S.Get("PhysPresetCustom"), jump, gravity);
		}

		// Which preset these numbers answer to, or -1 for none.
		public static int IndexOf(float jump, double gravity)
		{
			for (int i = 0; i < Presets.Length; i++)
				if (Math.Abs(Presets[i].Jump - jump) < 0.0005f
					&& Math.Abs(Presets[i].Gravity - gravity) < 0.0005) return i;
			return -1;
		}

		// Jump height and length in cells into impulse and gravity. The same
		// formula lives in the patch, so the guide and the game agree.
		//   flight frames   N = length * Grid / StepX, rounded
		//   ratio           r = (N - 1.5) / 2, impulse over gravity
		//   apex frame      n = round(r + 0.5)
		//   height share    f = n - n(n-1)/(2r), height at unit impulse
		//   impulse         v = height * Grid / f
		//   gravity         g = v / r
		// Then both go into game units by HeightScale.
		public static Preset FromShape(double heightCells, double lengthCells)
		{
			double frames = Math.Round(lengthCells * Level.Grid / StepX);
			double r = (frames - 1.5) / 2.0;
			if (heightCells <= 0 || r <= 0.25) return ByIndex(0);

			double peak = Math.Round(r + 0.5);
			double f = peak - peak * (peak - 1) / (2 * r);
			double v = heightCells * Level.Grid / f;

			return Custom((float)(v * HeightScale), (v / r) * HeightScale);
		}

		// Jump height and length in cells for a ready set of physics.
		public static void Shape(Preset preset, out double heightCells, out double lengthCells)
		{
			System.Collections.Generic.List<System.Drawing.PointF> path =
				Trajectory(preset, 0, 0, 600);
			double peak = 0;
			foreach (System.Drawing.PointF q in path) if (q.Y > peak) peak = q.Y;
			heightCells = peak / Level.Grid;
			lengthCells = path[path.Count - 1].X / Level.Grid;
		}

		// Limits for hand entry; every shape below must fit them.
		public const double MinJump = 0.2, MaxJump = 200;
		public const double MinGravity = 0.001, MaxGravity = 50;

		// Limits for the jump shape.
		public const double MinHeight = 0.2, MaxHeight = 6;
		public const double MinLength = 2, MaxLength = 30;

		public static Preset ByIndex(int index)
		{
			return index >= 0 && index < Presets.Length ? Presets[index] : Default;
		}

		// The path with no jump: the cube rides forward and falls.
		public static System.Collections.Generic.List<System.Drawing.PointF>
			Fall(Preset preset, double startX, double startY, int maxFrames)
		{
			System.Collections.Generic.List<System.Drawing.PointF> path =
				new System.Collections.Generic.List<System.Drawing.PointF>();
			double x = startX, y = startY, vy = 0;
			path.Add(new System.Drawing.PointF((float)x, (float)y));
			for (int i = 0; i < maxFrames; i++)
			{
				x += StepX;
				y += vy;
				vy -= preset.GravityLevel;
				if (vy < -TerminalFall) vy = -TerminalFall;
				path.Add(new System.Drawing.PointF((float)x, (float)y));
				if (y < 0) break;
			}
			return path;
		}

		public static System.Collections.Generic.List<System.Drawing.PointF>
			Trajectory(Preset preset, double startX, double startY, int maxFrames)
		{
			System.Collections.Generic.List<System.Drawing.PointF> path =
				new System.Collections.Generic.List<System.Drawing.PointF>();

			double x = startX, y = startY;
			double vy = preset.JumpLevel;
			path.Add(new System.Drawing.PointF((float)x, (float)y));

			for (int i = 0; i < maxFrames; i++)
			{
				x += StepX;
				y += vy;
				vy -= preset.GravityLevel;
				if (vy < -TerminalFall) vy = -TerminalFall;

				path.Add(new System.Drawing.PointF((float)x, (float)y));
				if (y < 0) break;
			}
			return path;
		}
	}
}

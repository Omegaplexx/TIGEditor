using System;

namespace TigEditor
{
    // Jump physics. The game holds three pairs of constants and picks them by
    // level number: one for user levels, one for the built in ones and a
    // separate pair for Phazd.
    //   impulse    float,  module offset 0x4DCD8 (user levels)
    //                                    0x4DCDC (built in)
    //                                        0x4DCE0 (Phazd)
    //   gravity    double, module offset 0x4C8F8 (user levels)
    //                                    0x4DCF8 (built in)
    //                                        0x4DD00 (Phazd)
    // The editor never writes to the game memory: these numbers serve the jump
    // guide, and they reach the game through the file patch and level.ini.
    public static class GamePhysics
    {

        // A height unit in the game is two and a half times smaller than a level
        // unit: a cell of 30 level units is 75 game height units.
        public const double HeightScale = 2.5;

        // The horizontal speed of the cube, level units per frame.
        public const double StepX = 6.0;

        // The terminal fall speed, in level units per frame.
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

            // The same in level units, for the jump guide.
            public double JumpLevel { get { return Jump / HeightScale; } }
            public double GravityLevel { get { return Gravity / HeightScale; } }

        }

        public static readonly Preset[] Presets =
        {
            new Preset(S.PhysPresetNormal, 22.0f, 2.0),
            new Preset(S.PhysPresetFar, 20.0f, 1.7),
            new Preset(S.PhysPresetHigh, 26.0f, 2.3)
        };

        public static Preset Default { get { return Presets[0]; } }

        // Numbers from level.ini that match no preset. For the jump guide they
        // are just as good: everything is worked out by the same rules, the set
        // simply has no name.
        public static Preset Custom(float jump, double gravity)
        {
            return new Preset(S.PhysPresetCustom, jump, gravity);
        }

        // Which preset these numbers answer to, or -1 for none.
        public static int IndexOf(float jump, double gravity)
        {
            for (int i = 0; i < Presets.Length; i++)
                if (Math.Abs(Presets[i].Jump - jump) < 0.0005f
                    && Math.Abs(Presets[i].Gravity - gravity) < 0.0005) return i;
            return -1;
        }

        // Jump height and length in cells into impulse and gravity.
        //
        // A closed formula, no fitting, and exactly the same one lives in the
        // patch: the editor and the game must work it out alike or the guide lies.
        //   flight frames   N = length * Grid / StepX, rounded
        //   ratio           r = (N - 1.5) / 2, that is impulse over gravity
        //   apex            n = rounded r + 1/2, a frame number
        //   height share    f = n - n(n-1)/(2r), the height at unit impulse
        //   impulse         v = height * Grid / f
        //   gravity         g = v / r
        // Both are then taken into game units by HeightScale.
        //
        // Flight lasts a whole number of frames, so the length is always a
        // multiple of StepX. Hence the rounding of N, and the halves put r in the
        // middle of the range that gives the wanted frame count.
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

        // Limits for hand entry, taken with room to spare: every jump shape that
        // height and length can ask for has to fit them, or switching the way of
        // entry would quietly change the jump.
        public const double MinJump = 0.2, MaxJump = 200;
        public const double MinGravity = 0.001, MaxGravity = 50;

        // Limits for the jump shape, chosen so that any shape fits the limits
        // above.
        public const double MinHeight = 0.2, MaxHeight = 6;
        public const double MinLength = 2, MaxLength = 30;

        public static Preset ByIndex(int index)
        {
            return index >= 0 && index < Presets.Length ? Presets[index] : Default;
        }

        // ---------- the jump model behind the guide ----------

        // The path with no jump: the cube simply rides forward and falls. The
        // same maths as the jump, only the starting speed is zero.
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

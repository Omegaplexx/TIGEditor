using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TigEditor;

// Everything about pits in one place. A pit is a span rather than a set of
// cells, so the checks are about its length surviving: a move, an undo, a copy
// and paste, and being drawn from the level atlas.
//
// Optional arguments: a path to game_1.png and a path to res/game1.bin. Without
// them the atlas part is skipped and the rest still runs.
class PitTest
{
	static int bad;

	static void Main(string[] a)
	{
		MoveAndUndo();
		CopyAndPaste();
		if (a.Length >= 2) FromAtlas(a[0], a[1]);
		else Console.WriteLine("atlas: skipped, no paths given");

		Console.WriteLine();
		Console.WriteLine(bad == 0 ? "all pit checks passed" : bad + " CHECKS FAILED");
	}

	static void Say(string what, bool good)
	{
		Console.WriteLine("{0,-44} {1}", what, good ? "yes" : "NO");
		if (!good) bad++;
	}

	// A move must carry both edges by the same amount, and an undo must put
	// them back exactly.
	static void MoveAndUndo()
	{
		Level lv = new Level();
		lv.Add(new LevelObj(ObjType.Pit, 300, 420));
		LevelObj pit = lv.Pits[0];
		int x0 = pit.X, y0 = pit.Y;

		UndoStack undo = new UndoStack();
		List<LevelObj> moved = new List<LevelObj>();
		moved.Add(pit);
		undo.Do(lv, new MoveCommand(moved, 60, 30));
		Console.WriteLine("after the move: X {0}, Y {1}, length {2}", pit.X, pit.Y, pit.Y - pit.X);
		Say("length kept", (pit.Y - pit.X) == (y0 - x0));
		Say("moved right by 60", pit.X == x0 + 60);

		undo.Undo(lv);
		Console.WriteLine("after the undo: X {0}, Y {1}", pit.X, pit.Y);
		Say("undo restored it exactly", pit.X == x0 && pit.Y == y0);
	}

	// A pasted pit keeps its length: both edges shift together.
	static void CopyAndPaste()
	{
		GridCanvas c = new GridCanvas();
		Level lv = new Level();
		lv.Add(new LevelObj(ObjType.Pit, 1200, 1320));   // length 150
		lv.Add(new LevelObj(ObjType.Block, 900, 0));
		c.SetLevel(lv);
		Form f = new Form();
		f.Controls.Add(c);
		c.Dock = DockStyle.Fill;
		f.Show();
		Application.DoEvents();

		int len = lv.Pits[0].Y - lv.Pits[0].X;
		c.ClearSelection();
		c.SelectAll();
		c.CopySelection();
		c.Paste();

		Console.WriteLine();
		Console.WriteLine("pits after the paste: {0}", lv.Pits.Count);
		bool ok = true;
		foreach (LevelObj p in lv.Pits)
		{
			Console.WriteLine("   X={0} Y={1} length={2}", p.X, p.Y, p.Y - p.X);
			ok &= (p.Y - p.X) == len;
		}
		Say("lengths kept", ok);
		f.Dispose();
	}

	// With a level atlas loaded the pit must be drawn from it, so the picture
	// has to change.
	static void FromAtlas(string png, string bin)
	{
		GridCanvas c = new GridCanvas();
		Level lv = new Level();
		lv.Add(new LevelObj(ObjType.Pit, 900, 1020));
		c.SetLevel(lv);
		Form f = new Form();
		f.Width = 800;
		f.Height = 400;
		f.Controls.Add(c);
		c.Dock = DockStyle.Fill;
		f.Show();
		Application.DoEvents();

		Bitmap before = Shot(c);
		GameAtlas atlas = GameAtlas.TryLoad(png, bin);
		Console.WriteLine();
		Console.WriteLine("atlas read: block {0}, spike {1}, pit {2}",
			atlas.Block != null, atlas.Spike != null, atlas.Pit != null);
		c.Atlas = atlas;
		Application.DoEvents();
		Bitmap after = Shot(c);

		int diff = 0;
		for (int y = 0; y < before.Height; y += 2)
			for (int x = 0; x < before.Width; x += 2)
				if (before.GetPixel(x, y) != after.GetPixel(x, y)) diff++;
		Say("the picture changed, the pit came from the atlas", diff > 0);
		f.Dispose();
	}

	static Bitmap Shot(Control c)
	{
		Bitmap b = new Bitmap(c.Width, c.Height);
		c.DrawToBitmap(b, new Rectangle(0, 0, c.Width, c.Height));
		return b;
	}
}

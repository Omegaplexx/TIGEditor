using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TigEditor
{
	// Everything a tool needs to know about a press.
	public struct ToolEvent
	{
		public MouseButtons Button;
		public Point Screen;
		public int WorldX;
		public int WorldY;
		public bool DragMode;         // Shift, with the draw inversion setting applied
		public bool AddToSelection;   // Shift: add to the selection
		public bool MoveModifier;     // Ctrl: selection mode
		public bool CopyModifier;     // Alt: drag a copy and leave the original
		public bool BelowFloor;
	}

	// A tool gets three mouse events and draws its own preview. There are no
	// flags left in the canvas: the state lives inside the tool.
	public abstract class EditorTool
	{
		protected readonly GridCanvas C;

		protected EditorTool(GridCanvas canvas) { C = canvas; }

		// The right button erases objects by default; tools with their own use
		// for it override this property.
		public virtual bool HandlesRightButton { get { return false; } }

		public virtual void OnDown(ToolEvent e) { }
		public virtual void OnDrag(ToolEvent e) { }
		public virtual void OnUp(ToolEvent e) { }
		public virtual void DrawPreview(Graphics g) { }

		protected Level Level { get { return C.Level; } }
		protected UndoStack Undo { get { return C.Undo; } }
	}

	// ---------- block and spike ----------

	public class PlaceTool : EditorTool
	{
		private readonly ObjType type;
		private List<LevelObj> added, replaced;
		private long lastKey = long.MinValue;
		private bool painting;

		public PlaceTool(GridCanvas canvas, ObjType type) : base(canvas) { this.type = type; }

		public override void OnDown(ToolEvent e)
		{
			added = new List<LevelObj>();
			replaced = new List<LevelObj>();
			lastKey = long.MinValue;
			painting = e.DragMode;      // without Shift exactly one object is placed
			PlaceAt(e);
			if (!painting) Commit();
		}

		public override void OnDrag(ToolEvent e) { if (painting) PlaceAt(e); }

		public override void OnUp(ToolEvent e)
		{
			painting = false;
			Commit();
		}

		private void PlaceAt(ToolEvent e)
		{
			Point w = C.SnapToGrid
				? C.SnapWorld(e.WorldX, e.WorldY)
				: new Point(e.WorldX - Level.Grid / 2, Math.Max(0, e.WorldY - Level.Grid / 2));
			if (w.Y < 0) return;

			long key = TigEditor.Level.Key(type, w.X, w.Y);
			if (key == lastKey) return;
			lastKey = key;

			LevelObj existing = Level.FindAt(w.X, w.Y);
			if (existing != null && existing.Type == type) return;
			if (existing == null && Level.ObjectCount >= TigEditor.Level.MaxObjects) { C.RaiseLimit(); return; }

			if (existing != null)
			{
				replaced.Add(existing);
				Level.Remove(existing);
				C.IndexRemove(existing);
			}
			LevelObj o = new LevelObj(type, w.X, w.Y);
			added.Add(o);
			Level.Add(o);
			C.IndexAdd(o);
			C.TilesTouched(o.X, o.X);
			C.Invalidate();
		}

		private void Commit()
		{
			if (added != null && added.Count > 0)
			{
				Undo.Record(new AddCommand(added, replaced));
				C.RaiseLevel();
			}
			added = null;
			replaced = null;
		}
	}

	// ---------- erasing blocks and spikes ----------

	public class EraseTool : EditorTool
	{
		private List<LevelObj> removed;
		private bool erasing;

		public EraseTool(GridCanvas canvas) : base(canvas) { }

		public override void OnDown(ToolEvent e)
		{
			removed = new List<LevelObj>();
			erasing = e.DragMode;
			EraseAt(e);
			if (!erasing) Commit();
		}

		public override void OnDrag(ToolEvent e) { if (erasing) EraseAt(e); }

		public override void OnUp(ToolEvent e)
		{
			erasing = false;
			Commit();
		}

		private void EraseAt(ToolEvent e)
		{
			// Pits lie below the floor and the left button removes them too.
			LevelObj o = C.FindObjectAt(e.WorldX, e.WorldY) ?? C.FindPitAt(e.WorldX, e.WorldY);
			if (o == null) return;
			Level.Remove(o);
			C.IndexRemove(o);
			C.Selection.Remove(o);
			removed.Add(o);
			if (o.Type == ObjType.Pit) C.PitsChanged();
			else C.TilesTouched(o.X, o.X);
			C.Invalidate();
		}

		private void Commit()
		{
			if (removed != null && removed.Count > 0)
			{
				Undo.Record(new RemoveCommand(removed));
				C.RaiseLevel();
			}
			removed = null;
		}
	}

	// Pits and effect stripes are placed the same way: drag the mouse, get a
	// range. The shared code is here; only what goes into the level and where
	// the preview is drawn differ.
	public abstract class RangeDragTool : EditorTool
	{
		private bool dragging, erasing;
		private int startX, currentX;

		protected RangeDragTool(GridCanvas canvas) : base(canvas) { }

		public override bool HandlesRightButton { get { return true; } }

		// How far the display is shifted from the record in the file.
		protected virtual int DisplayShift { get { return 0; } }

		protected abstract void Apply(int from, int toExclusive, bool erase);
		protected abstract Rectangle PreviewRect(int from, int toExclusive);
		protected abstract Color PreviewColor(bool erase);

		public override void OnDown(ToolEvent e)
		{
			dragging = true;
			erasing = e.Button == MouseButtons.Right;
			startX = currentX = Anchor(e);
			C.Invalidate();
		}

		public override void OnDrag(ToolEvent e)
		{
			if (!dragging) return;
			currentX = Anchor(e);
			C.Invalidate();
		}

		public override void OnUp(ToolEvent e)
		{
			if (!dragging) return;
			dragging = false;
			int from, to;
			Range(out from, out to);
			Apply(from, to, erasing);
		}

		// The cell under the pointer. With snapping it is a grid cell, without
		// it the cell is centred on the pointer, as in the original editor.
		// The range is never shorter than one cell.
		private int Anchor(ToolEvent e)
		{
			int wx = e.WorldX - DisplayShift;
			return C.SnapToGrid ? GridCanvas.SnapDown(wx) : wx - Level.Grid / 2;
		}

		private void Range(out int from, out int toExclusive)
		{
			from = Math.Min(startX, currentX);
			toExclusive = Math.Max(startX, currentX) + Level.Grid;
		}

		public override void DrawPreview(Graphics g)
		{
			if (!dragging) return;
			int from, to;
			Range(out from, out to);

			Rectangle r = PreviewRect(from, to);
			using (Brush br = new SolidBrush(PreviewColor(erasing)))
				g.FillRectangle(br, r);
			using (Pen p = new Pen(Color.White, C.Dip(1)))
			{
				p.DashStyle = DashStyle.Dash;
				g.DrawRectangle(p, r);
			}
		}
	}

	// ---------- pits ----------

	public class PitTool : RangeDragTool
	{
		public PitTool(GridCanvas canvas) : base(canvas) { }

		protected override void Apply(int from, int toExclusive, bool erase)
		{
			if (erase) RemoveRange(from, toExclusive - Level.Grid);
			else AddRange(from, toExclusive - Level.Grid);
		}

		protected override Rectangle PreviewRect(int from, int toExclusive)
		{
			int sx = C.WorldToScreenX(from), sx2 = C.WorldToScreenX(toExclusive);
			return new Rectangle(sx, C.BaselineY, Math.Max(1, sx2 - sx),
								 C.PitDepth - 1);
		}

		protected override Color PreviewColor(bool erase)
		{
			return erase ? Color.FromArgb(110, 255, 80, 80) : Color.FromArgb(140, 220, 0, 0);
		}

		// A pit is a span, not a set of cells: it is placed in one piece from
		// press to release. Spans that touch or overlap the new one merge into
		// it. The original editor does exactly this, which is why there are no
		// gaps or overlaps even without snapping.
		private void AddRange(int a, int b)
		{
			List<LevelObj> added = new List<LevelObj>();
			List<LevelObj> replaced = new List<LevelObj>();

			int start = a, end = b;
			foreach (LevelObj p in Level.Pits)
			{
				// touching follows the game rule: closer than one cell
				if (p.Y + Level.Grid < start || end + Level.Grid < p.X) continue;
				replaced.Add(p);
				if (p.X < start) start = p.X;
				if (p.Y > end) end = p.Y;
			}

			if (replaced.Count == 0 && Level.ObjectCount >= TigEditor.Level.MaxObjects)
			{ C.RaiseLimit(); return; }

			// A pit over a pit changes nothing: if the merge gave back exactly
			// the same span, there is nothing for history to hold.
			if (replaced.Count == 1 && replaced[0].X == start && replaced[0].Y == end)
			{
				C.Invalidate();
				return;
			}

			added.Add(new LevelObj(ObjType.Pit, start, end));
			Undo.Do(Level, new AddCommand(added, replaced));
			C.PitsChanged();
			C.RaiseLevel();
		}

		// Erasing cuts the band [a, b + 30) out of the spans. A stub is kept only
		// if a whole cell fits it: the game does not draw pits narrower than a
		// cell.
		private void RemoveRange(int a, int b)
		{
			int from = a, to = b + Level.Grid;
			List<LevelObj> added = new List<LevelObj>();
			List<LevelObj> replaced = new List<LevelObj>();

			foreach (LevelObj p in Level.Pits)
			{
				if (p.Y + Level.Grid <= from || p.X >= to) continue;
				replaced.Add(p);
				if (from - Level.Grid >= p.X) added.Add(new LevelObj(ObjType.Pit, p.X, from - Level.Grid));
				if (to <= p.Y) added.Add(new LevelObj(ObjType.Pit, to, p.Y));
			}

			if (replaced.Count == 0) { C.Invalidate(); return; }

			Undo.Do(Level, new AddCommand(added, replaced));
			C.PitsChanged();
			C.RaiseLevel();
		}
	}

	// ---------- finish ----------

	public class LevelEndTool : EditorTool
	{
		public LevelEndTool(GridCanvas canvas) : base(canvas) { }

		public override void OnDown(ToolEvent e)
		{
			int wx = GridCanvas.SnapDown(e.WorldX) + Level.Grid / 2;
			if (wx == Level.LevelEnd) return;
			Undo.Do(Level, new SetLevelEndCommand(Level.LevelEnd, wx));
			C.Invalidate();
			C.RaiseLevel();
		}
	}

	// ---------- selection ----------

	public class SelectTool : EditorTool
	{
		private bool selecting;
		private bool moving;
		private bool cleared;            // whether the old selection was cleared
		private LevelObj pending;        // the object under the press point
		private Point start, current;
		private int pressX, pressY;      // the world point of the press
		private int appliedX, appliedY;  // how far it has moved already

		public SelectTool(GridCanvas canvas) : base(canvas) { }

		public override void OnDown(ToolEvent e)
		{
			pressX = e.WorldX;
			pressY = e.WorldY;
			appliedX = appliedY = 0;
			cleared = false;
			// A lifted selection can be grabbed again: it stays in hand until it
			// is put down and erases nothing on the way.
			pending = C.FindSelectableAt(e.WorldX, e.WorldY);

			// Dragging happens only when an already selected object is grabbed.
			// Ctrl with Shift always means a frame, even over a selected object,
			// so a frame can be pulled straight from it.
			bool forceRect = e.MoveModifier && e.AddToSelection;
			if (!forceRect && pending != null && C.Selection.Contains(pending))
			{
				if (e.CopyModifier) C.DuplicateSelectionForMove();   // Alt drags a copy
				moving = true;
				C.Invalidate();
				return;
			}

			// A frame can start anywhere, an object included: until the mouse
			// moves it is just a click, and once it moves it is a frame.
			selecting = true;
			start = current = e.Screen;
			C.Invalidate();
		}

		private bool Moved
		{
			get { return Math.Abs(current.X - start.X) > 2 || Math.Abs(current.Y - start.Y) > 2; }
		}

		public override void OnDrag(ToolEvent e)
		{
			if (moving)
			{
				// Worked out by the cell under the pointer, not by the distance
				// travelled: an object moves as soon as a cell border is crossed.
				int wantX, wantY;
				if (C.SnapToGrid)
				{
					wantX = GridCanvas.SnapDown(e.WorldX) - GridCanvas.SnapDown(pressX);
					wantY = GridCanvas.SnapDown(e.WorldY) - GridCanvas.SnapDown(pressY);
				}
				else
				{
					wantX = e.WorldX - pressX;
					wantY = e.WorldY - pressY;
				}
				int dx = wantX - appliedX, dy = wantY - appliedY;
				if (dx == 0 && dy == 0) return;
				C.MoveSelection(dx, dy);
				appliedX = wantX;
				appliedY = wantY;
				return;
			}
			if (!selecting) return;

			current = e.Screen;
			if (Moved && !cleared)
			{
				if (!e.AddToSelection) C.ClearSelection();
				cleared = true;
			}
			C.Invalidate();
		}

		public override void OnUp(ToolEvent e)
		{
			moving = false;
			if (!selecting) return;
			selecting = false;

			if (Moved)
			{
				C.SelectInRect(C.MakeRect(start, current));
			}
			else
			{
				// a plain click selects an object, or clears everything on empty space
				if (!e.AddToSelection) C.ClearSelection();
				if (pending != null) C.Selection.Add(pending);
			}
			pending = null;
			C.Invalidate();
		}

		public override void DrawPreview(Graphics g)
		{
			if (!selecting || !Moved) return;
			using (Pen p = new Pen(Color.Yellow))
			{
				p.DashStyle = DashStyle.Dash;
				g.DrawRectangle(p, C.MakeRect(start, current));
			}
		}
	}

	// ---------- gravity ----------

	public class GravityTool : EditorTool
	{
		private List<int> added, removed;
		private bool painting, erasing;
		private int lastX = int.MinValue;

		public GravityTool(GridCanvas canvas) : base(canvas) { }

		public override bool HandlesRightButton { get { return true; } }

		public override void OnDown(ToolEvent e)
		{
			added = new List<int>();
			removed = new List<int>();
			lastX = int.MinValue;
			erasing = e.Button == MouseButtons.Right;
			painting = e.DragMode;

			if (erasing) RemoveNear(e); else AddAt(e);
			if (!painting) Commit();
		}

		public override void OnDrag(ToolEvent e)
		{
			if (!painting) return;
			if (erasing) RemoveNear(e); else AddAt(e);
		}

		public override void OnUp(ToolEvent e)
		{
			painting = false;
			Commit();
		}

		// Marks are shown shifted, so a click is translated back.
		private int WorldFrom(ToolEvent e)
		{
			int wx = e.WorldX - TigEditor.Level.GravityDrawShift;
			if (C.SnapToGrid)
			{
				int half = Level.Grid / 2;
				wx = (int)Math.Round(wx / (double)half) * half;
			}
			// Left of the ninth and tenth cell seam the game shows no flip.
			return wx < 0 ? 0 : wx;
		}

		private void AddAt(ToolEvent e)
		{
			int x = WorldFrom(e);
			if (x == lastX) return;
			lastX = x;
			if (Level.GravityChanges.Contains(x)) return;

			Level.GravityChanges.Add(x);
			Level.GravityChanges.Sort();
			Level.Touch();
			added.Add(x);
			C.Invalidate();
			C.RaiseLevel();
		}

		private void RemoveNear(ToolEvent e)
		{
			int x = WorldFrom(e);
			int best = 0, bestDist = int.MaxValue;
			foreach (int g in Level.GravityChanges)
			{
				int d = Math.Abs(g - x);
				if (d < bestDist) { bestDist = d; best = g; }
			}
			if (bestDist > Level.Grid) return;

			Level.GravityChanges.Remove(best);
			Level.Touch();
			removed.Add(best);
			C.Invalidate();
			C.RaiseLevel();
		}

		// The whole stroke goes into history as a single step.
		private void Commit()
		{
			if (added == null) return;
			if (added.Count > 0 || removed.Count > 0)
				Undo.Record(new GravityCommand(added, removed));
			added = null;
			removed = null;
			lastX = int.MinValue;
		}
	}

	// ---------- background changes ----------

	public class BackgroundTool : EditorTool
	{
		private List<BackgroundChange> strokeRemoved;
		private bool sweeping;

		public BackgroundTool(GridCanvas canvas) : base(canvas) { }

		public override bool HandlesRightButton { get { return true; } }

		public override void OnDown(ToolEvent e)
		{
			if (e.Button == MouseButtons.Right)
			{
				sweeping = e.DragMode;
				strokeRemoved = sweeping ? new List<BackgroundChange>() : null;
				RemoveNear(e);
				return;
			}
			AddAt(e);
		}

		public override void OnDrag(ToolEvent e)
		{
			if (sweeping) RemoveNear(e);
		}

		public override void OnUp(ToolEvent e)
		{
			if (!sweeping) return;
			sweeping = false;

			List<BackgroundChange> removed = strokeRemoved;
			strokeRemoved = null;
			if (removed == null || removed.Count == 0) return;

			// put it back and go through one command, so undo works
			foreach (BackgroundChange b in removed) Level.Backgrounds.Add(b);
			SortBackgrounds();
			Commit(new List<BackgroundChange>(), removed);
		}

		// Background borders land on a cell edge or its middle, as in the original.
		private int WorldFrom(ToolEvent e)
		{
			int wx = e.WorldX - GridCanvas.BackgroundDrawShift;
			if (C.SnapToGrid)
			{
				// The nearest border, not the one on the left: the pointer
				// decides which way is closer.
				int half = Level.Grid / 2;
				wx = (int)Math.Floor((wx + half / 2.0) / half) * half;
			}
			// Before the tenth and eleventh cell seam the game shows no
			// background change, so anything left of it is pinned there.
			return wx < 0 ? 0 : wx;
		}

		private void SortBackgrounds()
		{
			Level.Backgrounds.Sort(delegate(BackgroundChange a, BackgroundChange b) { return a.X.CompareTo(b.X); });
			Level.Touch();
		}

		private void AddAt(ToolEvent e)
		{
			int x = WorldFrom(e);
			List<BackgroundChange> added = new List<BackgroundChange>();
			List<BackgroundChange> removed = new List<BackgroundChange>();

			// A background change at the same spot replaces the old one instead
			// of joining it: otherwise which of the two wins would depend on
			// the sort order.
			foreach (BackgroundChange b in Level.Backgrounds)
				if (b.X == x)
				{
					// The same background at the same spot is a no op and has
					// nothing to do in history.
					if (b.CustomName == null && b.BgId == C.CurrentBgId) return;
					removed.Add(b);
				}

			added.Add(new BackgroundChange(x, C.CurrentBgId));
			Commit(added, removed);
		}

		private void RemoveNear(ToolEvent e)
		{
			int x = WorldFrom(e);
			BackgroundChange best = null;
			int bestDist = int.MaxValue;
			foreach (BackgroundChange b in Level.Backgrounds)
			{
				int d = Math.Abs(b.X - x);
				if (d < bestDist) { bestDist = d; best = b; }
			}
			if (best == null || bestDist > Level.Grid * 2) return;

			if (strokeRemoved != null)
			{
				Level.Backgrounds.Remove(best);
				Level.Touch();
				strokeRemoved.Add(best);
				C.Invalidate();
				C.RaiseLevel();
				return;
			}

			List<BackgroundChange> removed = new List<BackgroundChange>();
			removed.Add(best);
			Commit(new List<BackgroundChange>(), removed);
		}

		// Back to back changes to the same colour are pointless, the first stays.
		private void Commit(List<BackgroundChange> added, List<BackgroundChange> removed)
		{
			List<BackgroundChange> result = new List<BackgroundChange>();
			foreach (BackgroundChange b in Level.Backgrounds)
				if (!removed.Contains(b)) result.Add(b);
			result.AddRange(added);
			result.Sort(delegate(BackgroundChange a, BackgroundChange b) { return a.X.CompareTo(b.X); });

			int prev = 0;    // before the first change the background is blue
			foreach (BackgroundChange b in result)
			{
				if (b.CustomName != null) { prev = -1; continue; }
				if (b.BgId == prev)
				{
					if (!added.Remove(b)) removed.Add(b);
				}
				else prev = b.BgId;
			}

			if (added.Count == 0 && removed.Count == 0) { C.Invalidate(); return; }
			Undo.Do(Level, new BackgroundCommand(added, removed));
			C.Invalidate();
			C.RaiseLevel();
		}
	}

	// ---------- effect stripes ----------

	public class EffectTool : RangeDragTool
	{
		public EffectTool(GridCanvas canvas) : base(canvas) { }

		// The two effects are shown with different shifts, so it follows the choice.
		protected override int DisplayShift
		{
			get
			{
				return C.EffectIsRise
					? TigEditor.Level.EffectRiseDrawShift
					: TigEditor.Level.EffectFallDrawShift;
			}
		}

		protected override Rectangle PreviewRect(int from, int toExclusive)
		{
			int sx = C.WorldToScreenX(from + DisplayShift);
			int sx2 = C.WorldToScreenX(toExclusive + DisplayShift);
			int top = C.Dip(5) + (C.EffectIsRise ? C.Dip(9) : 0);
			return new Rectangle(sx, top, Math.Max(2, sx2 - sx), C.Dip(7));
		}

		protected override Color PreviewColor(bool erase)
		{
			if (erase) return Color.FromArgb(120, 255, 80, 80);
			return Color.FromArgb(160, C.EffectIsRise ? Color.DeepSkyBlue : Color.Orange);
		}

		protected override void Apply(int a, int b, bool erase)
		{
			List<Interval> target = C.EffectIsRise ? Level.BlocksRise : Level.BlocksFall;
			List<Interval> added = new List<Interval>();
			List<Interval> removed = new List<Interval>();

			if (erase)
			{
				// We cut a band out instead of removing the whole span: pieces
				// on the left and right stay if at least a cell fits them.
				foreach (Interval iv in target)
				{
					if (iv.StartX > b || iv.EndX < a) continue;
					removed.Add(iv);
					if (a - iv.StartX >= Level.Grid) added.Add(new Interval(iv.StartX, a));
					if (iv.EndX - b >= Level.Grid) added.Add(new Interval(b, iv.EndX));
				}
				if (removed.Count == 0) { C.Invalidate(); return; }
			}
			else
			{
				// overlapping spans merge into one, same as pits
				int start = a, end = b;
				foreach (Interval iv in target)
					if (iv.StartX <= end && iv.EndX >= start)
					{
						removed.Add(iv);
						if (iv.StartX < start) start = iv.StartX;
						if (iv.EndX > end) end = iv.EndX;
					}
				added.Add(new Interval(start, end));
			}

			Undo.Do(Level, new IntervalCommand(C.EffectIsRise, added, removed));
			C.Invalidate();
			C.RaiseLevel();
		}
	}
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TigEditor
{
	public enum Tool { Block, Spike, Pit, Delete, LevelEnd, Select, Gravity, Background, Effect }

	public class GridCanvas : Control
	{
		public static string[] BgNames
		{
			get { return new string[] { S.Get("BgBlue"), S.Get("BgYellow"), S.Get("BgGreen"), S.Get("BgViolet"), S.Get("BgPink"), S.Get("BgBlack") }; }
		}

		// Palette icon colour, taken from the renderer.
		public static Color BackgroundSample(int bgId) { return LevelRenderer.BackgroundSample(bgId); }

		public GameAtlas Atlas
		{
			get { return renderer.Atlas; }
			set { renderer.Atlas = value; TilesAllDirty(); Invalidate(); }
		}

		public Color BackgroundSampleFor(int bgId) { return renderer.SampleFor(bgId); }

		// Backgrounds fire further right in the game than they are written; see LevelFormat.cs.
		public const int BackgroundDrawShift = 2 * 150;

		public const int MinRows = 8;
		public const int MaxRows = 60;
		public const int DefaultRows = 20;

		// Gap under the bottom row that keeps pits visible.
		public const double FloorGapCells = 1.75;

		private bool invertScroll;
		private bool invertZoom;
		private int currentBgId;
		private bool effectIsRise;

		private Level level = new Level();
		private readonly UndoStack undo = new UndoStack();
		private readonly LevelIndex index = new LevelIndex();
		private readonly LevelRenderer renderer;

		private double rowsCurrent = DefaultRows, rowsTarget = DefaultRows, rowsVel;
		private double viewX, targetX;          // current and target horizontal scroll
		private double viewY, targetY;          // the same vertically
		private Tool tool = Tool.Block;
		private bool snapToGrid = true;
		private bool showGrid = true;

		// Frames run off a separate 5 ms timer: the WinForms timer is too slow for fast monitors.
		private System.Threading.Timer anim;
		private volatile bool framePending;
		private readonly System.Diagnostics.Stopwatch animClock = new System.Diagnostics.Stopwatch();

		// Smoothing time constant.
		public const double ScrollSeconds = 0.09;
		public const double ZoomSeconds = 0.09;

		private double velX, velY;              // speeds used for smoothing
		private double lastFrameSeconds;

		private bool panning;
		private bool seeking;         // dragging on the canvas, seeking the music
		private bool playbackRunning; // music is playing, the line must travel
		private Point panMouseStart;
		private double panViewXStart, panViewYStart;
		private bool invertDrawMode;

		// The jump guide: the point the arc is drawn from, in level coordinates.
		private Point jumpGuide = new Point(int.MinValue, 0);
		private GamePhysics.Preset physics;

		// One object per tool; the active one gets the mouse events.
		private readonly Dictionary<Tool, EditorTool> tools = new Dictionary<Tool, EditorTool>();
		private EditorTool activeTool;
		private EditorTool dragTool;      // the tool currently running a drag
		private readonly HashSet<LevelObj> selection = new HashSet<LevelObj>();

		// Dragged objects are lifted out of the level and drawn on top.
		private readonly List<LevelObj> floating = new List<LevelObj>();
		private int floatDx, floatDy;
		private bool floatIsCopy;
		private bool spaceDown;
		private bool mouseInside;
		private Point mouseClient;

		private double playbackX = -1;
		private bool followPlayback;

		// Asked every frame so the line follows the monitor, not the interface timer.
		public Func<double> PlaybackSource;
		private double dpiScale = 1.0;

		public event EventHandler LevelChanged;
		public event EventHandler ViewChanged;
		public event EventHandler LimitReached;

		// A click on the time strip on top asks to seek to that spot.
		public event Action<double> SeekRequested;

		public GridCanvas()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
					 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
					 ControlStyles.Selectable, true);
			TabStop = true;
			BackColor = Color.Black;

			renderer = new LevelRenderer(index);
			animClock.Start();

			tools[Tool.Block] = new PlaceTool(this, ObjType.Block);
			tools[Tool.Spike] = new PlaceTool(this, ObjType.Spike);
			tools[Tool.Delete] = new EraseTool(this);
			tools[Tool.Pit] = new PitTool(this);
			tools[Tool.LevelEnd] = new LevelEndTool(this);
			tools[Tool.Select] = new SelectTool(this);
			tools[Tool.Gravity] = new GravityTool(this);
			tools[Tool.Background] = new BackgroundTool(this);
			tools[Tool.Effect] = new EffectTool(this);
			activeTool = tools[Tool.Block];
			anim = new System.Threading.Timer(delegate { RequestFrame(); }, null,
											  System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
		}

		public Level Level { get { return level; } }
		public UndoStack Undo { get { return undo; } }
		public ICollection<LevelObj> Selection { get { return selection; } }

		// State for the edit menu: what makes sense to do right now.
		public bool HasSelection { get { return selection.Count > 0; } }

		public bool HasAnyContent
		{
			get
			{
				return level.Cells.Count > 0 || level.Pits.Count > 0
					|| level.Backgrounds.Count > 0 || level.GravityChanges.Count > 0
					|| level.BlocksFall.Count > 0 || level.BlocksRise.Count > 0;
			}
		}

		// Blocks, spikes and pits; not backgrounds, stripes or gravity.
		public bool HasSelectable
		{
			get
			{
				if (level.Pits.Count > 0) return true;
				foreach (LevelObj o in level.Cells.Values)
					if (o.Type == ObjType.Block || o.Type == ObjType.Spike) return true;
				return false;
			}
		}
		public bool HasClipboard { get { return clipboard.Count > 0; } }
		public bool CanUndo { get { return Undo.CanUndo; } }
		public bool CanRedo { get { return Undo.CanRedo; } }

		public Tool CurrentTool
		{
			get { return tool; }
			set
			{
				tool = value;
				activeTool = tools[value];
				if (value != Tool.Select) ClearSelection();
				Invalidate();
			}
		}

		public int RowsVisible
		{
			get { return (int)Math.Round(rowsTarget); }
			set
			{
				double v = Math.Max(MinRows, Math.Min(MaxRows, value));
				if (Math.Abs(v - rowsTarget) < 0.01) return;
				rowsTarget = v;
				StartAnim();
				RaiseView();
			}
		}

		public void SetRowsInstant(int rows)
		{
			rowsTarget = rowsCurrent = Math.Max(MinRows, Math.Min(MaxRows, rows));
			rowsVel = 0;
			Invalidate();
			RaiseView();
		}

		public bool ShowGrid { get { return showGrid; } set { showGrid = value; Invalidate(); } }
		public bool InvertScroll { get { return invertScroll; } set { invertScroll = value; } }

		// Normally Shift drags; with this setting it places one object instead.
		public bool InvertDrawMode { get { return invertDrawMode; } set { invertDrawMode = value; } }
		public bool InvertZoom { get { return invertZoom; } set { invertZoom = value; } }
		public int CurrentBgId { get { return currentBgId; } set { currentBgId = value; } }
		public bool EffectIsRise { get { return effectIsRise; } set { effectIsRise = value; } }

		// Physics of the level (preset or level.ini numbers), used by the jump guide.
		public GamePhysics.Preset Physics
		{
			get { return physics ?? GamePhysics.ByIndex(0); }
			set { physics = value; Invalidate(); }
		}

		public bool PureBlackBackground
		{
			get { return renderer.PureBlackBackground; }
			set { renderer.PureBlackBackground = value; Invalidate(); }
		}
		public bool SnapToGrid { get { return snapToGrid; } set { snapToGrid = value; } }

		public int OffsetX
		{
			get { return (int)Math.Round(viewX); }
			set { viewX = targetX = Math.Max(0, value); velX = 0; Invalidate(); RaiseView(); }
		}

		public int OffsetY
		{
			get { return (int)Math.Round(viewY); }
			set { viewY = targetY = Math.Max(0, value); velY = 0; Invalidate(); RaiseView(); }
		}

		// Fractional cell size, so smooth zoom does not jump by whole pixels.
		// Zoom is the row count, floor gap included.
		public double CellSizeF
		{
			get { return Math.Max(4.0, ClientSize.Height / rowsCurrent); }
		}

		public int CellSize { get { return Math.Max(4, (int)Math.Round(CellSizeF)); } }

		public double ViewScale { get { return CellSizeF / Level.Grid; } }

		// Tiles are drawn at the target zoom and stretched while it animates.
		public double TargetScale
		{
			get { return Math.Max(4.0, ClientSize.Height / rowsTarget) / Level.Grid; }
		}

		private int TargetCell { get { return Math.Max(4, (int)Math.Round(TargetScale * Level.Grid)); } }

		public int ViewWidthWorld { get { return (int)(ClientSize.Width / ViewScale); } }

		internal int BaselineY
		{
			get
			{
				return (int)Math.Round(ClientSize.Height - CellSizeF * FloorGapCells + viewY * ViewScale);
			}
		}

		// Green line playback start; below zero means the level start.
		private double startX = -1;
		public double StartX
		{
			get { return startX; }
			set { startX = value; Invalidate(); }
		}

		// Green line placing mode: a click places it, the right button removes it.
		public bool PlacingStart { get; set; }

		// The main window decides: it drives the music.
		public event Action<double> StartRequested;

		public bool SeekOnClick
		{
			get { return playbackRunning; }
			set
			{
				if (playbackRunning == value) return;
				playbackRunning = value;
				if (value) StartAnim(); else Invalidate();
			}
		}

		// Playback position in world pixels, -1 when nothing has played.
		public double PlaybackX
		{
			get { return playbackX; }
			set { playbackX = value; Invalidate(); }
		}

		// While the music plays the camera rides the red line at its speed.
		public bool FollowPlayback
		{
			get { return followPlayback; }
			set
			{
				followPlayback = value;
				if (value) StartAnim();
			}
		}

		public void SetLevel(Level lv)
		{
			// The start mark belongs to listening, not to the level.
			startX = -1;
			PlacingStart = false;
			jumpGuide = new Point(int.MinValue, 0);
			level = lv;
			floating.Clear();
			selection.Clear();
			undo.Clear();
			viewX = targetX = 0;
			viewY = targetY = 0;
			playbackX = -1;
			Invalidate();
			RaiseLevel();
			RaiseView();
		}

		public void ScrollBy(double dx, double dy)
		{
			SetTarget(targetX + dx, targetY + dy);
		}

		public void ScrollToWorldX(int worldX)
		{
			SetTarget(worldX - ViewWidthWorld / 2.0, targetY);
		}

		public void SetTarget(double x, double y)
		{
			targetX = Math.Max(0, x);
			targetY = Math.Max(0, y);
			StartAnim();
		}

		private void StartAnim()
		{
			lastFrameSeconds = animClock.Elapsed.TotalSeconds;
			anim.Change(0, 5);
		}

		private void StopAnim()
		{
			anim.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
		}

		private void RequestFrame()
		{
			if (framePending || !IsHandleCreated) return;
			framePending = true;
			try { BeginInvoke((MethodInvoker)AnimStep); }
			catch { framePending = false; }
		}

		// Decaying smoothing: gentle start, no braking at the end, quick scrolls add up.
		private static double SmoothDamp(double current, double target, ref double velocity,
										 double smoothTime, double dt)
		{
			double omega = 2.0 / smoothTime;
			double x = omega * dt;
			double exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
			double change = current - target;
			double temp = (velocity + omega * change) * dt;
			velocity = (velocity - omega * temp) * exp;
			return target + (change + temp) * exp;
		}

		private void AnimStep()
		{
			framePending = false;

			double now = animClock.Elapsed.TotalSeconds;
			double dt = now - lastFrameSeconds;
			lastFrameSeconds = now;
			if (dt <= 0 || dt > 0.2) dt = 0.006;

			bool busy = playbackRunning;

			if (playbackRunning && PlaybackSource != null) playbackX = PlaybackSource();

			if (followPlayback && playbackRunning && playbackX >= 0)
			{
				// behind the red line the camera moves at exactly its speed
				viewX = targetX = Math.Max(0, playbackX - ViewWidthWorld / 3.0);
				velX = 0;
				busy = true;
			}
			else if (Math.Abs(targetX - viewX) > 0.05 || Math.Abs(velX) > 0.05)
			{
				viewX = SmoothDamp(viewX, targetX, ref velX, ScrollSeconds, dt);
				busy = true;
			}
			else { viewX = targetX; velX = 0; }

			if (Math.Abs(targetY - viewY) > 0.05 || Math.Abs(velY) > 0.05)
			{
				viewY = SmoothDamp(viewY, targetY, ref velY, ScrollSeconds, dt);
				busy = true;
			}
			else { viewY = targetY; velY = 0; }

			if (Math.Abs(rowsTarget - rowsCurrent) > 0.005 || Math.Abs(rowsVel) > 0.005)
			{
				rowsCurrent = SmoothDamp(rowsCurrent, rowsTarget, ref rowsVel, ZoomSeconds, dt);
				busy = true;
			}
			else { rowsCurrent = rowsTarget; rowsVel = 0; }

			Invalidate();
			if (busy) RaiseView();
			else { StopAnim(); RaiseView(); }
		}

		public int ScreenToWorldX(int px) { return (int)Math.Floor(viewX + px / ViewScale); }
		public int ScreenToWorldY(int py) { return (int)Math.Floor((BaselineY - py) / ViewScale); }
		public int WorldToScreenX(double wx) { return (int)Math.Floor((wx - viewX) * ViewScale); }
		public int WorldToScreenY(double wy) { return BaselineY - (int)Math.Floor(wy * ViewScale); }

		internal static int SnapDown(int v)
		{
			int r = v / Level.Grid;
			if (v < 0 && v % Level.Grid != 0) r--;
			return r * Level.Grid;
		}

		public Point SnapWorld(int wx, int wy)
		{
			if (!snapToGrid) return new Point(wx, Math.Max(0, wy));
			return new Point(SnapDown(wx), Math.Max(0, SnapDown(wy)));
		}

		// The world coordinate of the pointer, or int.MinValue when off grid.
		public int CursorWorldX
		{
			get
			{
				if (!mouseInside || !ClientRectangle.Contains(mouseClient)) return int.MinValue;
				return ScreenToWorldX(mouseClient.X);
			}
		}

		// Pointer coordinates, but only while it really is over the grid.
		public bool TryGetCursorCell(out int cellX, out int row)
		{
			cellX = 0;
			row = 0;
			if (!mouseInside) return false;
			if (!ClientRectangle.Contains(mouseClient)) return false;
			int wx = ScreenToWorldX(mouseClient.X);
			int wy = ScreenToWorldY(mouseClient.Y);
			if (wy < 0 || wx < 0) return false;
			cellX = LevelIndex.FloorDiv(wx, Level.Grid) + 1;        // columns and rows are numbered from one
			row = LevelIndex.FloorDiv(wy, Level.Grid) + 1;
			return true;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			RenderView v = new RenderView();
			v.Level = level;
			v.Width = ClientSize.Width;
			v.Height = ClientSize.Height;
			v.ViewX = viewX;
			v.ViewY = viewY;
			v.Scale = ViewScale;
			v.TargetScale = TargetScale;
			v.CellF = CellSizeF;
			v.Cell = CellSize;
			v.TargetCell = TargetCell;
			v.Baseline = BaselineY;
			v.DpiScale = dpiScale;
			v.ShowGrid = showGrid;
			v.Selection = selection;
			v.Floating = floating;
			v.PlaybackX = playbackX;
			v.StartX = startX;

			renderer.Draw(e.Graphics, v);
			DrawJumpGuide(e.Graphics);
			(dragTool ?? activeTool).DrawPreview(e.Graphics);

		}

		// Jump arc: four cube corners, as in the original editor.
		private void DrawJumpGuide(Graphics g)
		{
			if (jumpGuide.X == int.MinValue) return;

			// Two arcs: jumping from this point, and running past it.
			List<PointF> jump = Trim(GamePhysics.Trajectory(Physics, jumpGuide.X, jumpGuide.Y, 240));
			List<PointF> fall = FallPath(jumpGuide.X, jumpGuide.Y);

			int side = Level.Grid;
			int[] dx = { 0, side, 0, side };
			int[] dy = { 0, 0, side, side };
			Color[] colors = { Color.Magenta, Color.Magenta, Color.DeepSkyBlue, Color.DeepSkyBlue };

			for (int corner = 0; corner < 4; corner++)
			{
				using (Pen pen = new Pen(Color.FromArgb(200, colors[corner]), Dip(1)))
				{
					Draw(g, pen, jump, dx[corner], dy[corner]);
					// The fall is fainter: it happens by itself.
					using (Pen soft = new Pen(Color.FromArgb(110, colors[corner]), Dip(1)))
					{
						soft.DashPattern = new float[] { 7f, 5f };
						Draw(g, soft, fall, dx[corner], dy[corner]);
					}
				}
			}

			// the cube at the start point
			using (Pen pen = new Pen(Color.White, Dip(1)))
			{
				int sx = WorldToScreenX(jumpGuide.X);
				int sy = WorldToScreenY(jumpGuide.Y + Level.Grid);
				g.DrawRectangle(pen, sx, sy, CellSize, CellSize);
			}
		}

		private void Draw(Graphics g, Pen pen, List<PointF> path, int dx, int dy)
		{
			for (int i = 1; i < path.Count; i++)
				g.DrawLine(pen,
					WorldToScreenX(path[i - 1].X + dx), WorldToScreenY(path[i - 1].Y + dy),
					WorldToScreenX(path[i].X + dx), WorldToScreenY(path[i].Y + dy));
		}

		// The path without a jump: rolls on support, falls past the edge.
		private List<PointF> FallPath(double startX, double startY)
		{
			List<PointF> path = new List<PointF>();
			if (startY <= 0) return path;

			double x = startX, y = startY, vy = 0;
			bool rolling = Supported(x, y);
			path.Add(new PointF((float)x, (float)y));

			for (int i = 0; i < 240; i++)
			{
				if (rolling)
				{
					x += GamePhysics.StepX;
					if (!Supported(x, y)) rolling = false;
				}
				else
				{
					x += GamePhysics.StepX;
					y += vy;
					vy -= Physics.GravityLevel;
					if (vy < -GamePhysics.TerminalFall) vy = -GamePhysics.TerminalFall;
					if (HitsBlock(x, y)) break;
				}
				path.Add(new PointF((float)x, (float)y));
				if (y <= 0) break;
			}
			return path;
		}

		// Whether there is a block under the cube for it to stand on.
		private bool Supported(double x, double y)
		{
			return HitsBlock(x, y - 1);
		}

		// The arc breaks where the cube lands on a block.
		private List<PointF> Trim(List<PointF> path)
		{
			for (int i = 1; i < path.Count; i++)
				if (HitsBlock(path[i].X, path[i].Y))
					return path.GetRange(0, i);
			return path;
		}

		// The cube against real block rectangles: blocks need not sit on the grid.
		private bool HitsBlock(double x, double y)
		{
			if (level == null) return false;
			index.Ensure(level);
			int side = Level.Grid;
			int first = LevelIndex.FloorDiv((int)Math.Floor(x) - side + 1, side);
			int last = LevelIndex.FloorDiv((int)Math.Ceiling(x) + side - 1, side);
			for (int col = first; col <= last; col++)
			{
				List<LevelObj> list;
				if (!index.Columns.TryGetValue(col, out list)) continue;
				foreach (LevelObj o in list)
					if (o.Type == ObjType.Block
						&& o.X < x + side && o.X + side > x
						&& o.Y < y + side && o.Y + side > y) return true;
			}
			return false;
		}

		// Overlay sizes depend on screen DPI only, not on the grid zoom.
		internal int Dip(int px)
		{
			return Math.Max(1, (int)Math.Round(px * dpiScale));
		}

		// The guide is centred on the pointer without snapping; it stands on a block under the pointer.
		private void SetJumpGuide(int worldX, int worldY)
		{
			int x = worldX - Level.Grid / 2;
			int y = Math.Max(0, worldY - Level.Grid / 2);

			LevelObj under = FindObjectAt(worldX, worldY);
			if (under != null && under.Type == ObjType.Block) y = under.Y + Level.Grid;
			jumpGuide = new Point(x, y);
		}

		internal void IndexAdd(LevelObj o) { index.Add(level, o); }
		internal void IndexRemove(LevelObj o) { index.Remove(level, o); }
		// Pit strip depth on screen; custom graphics may differ.
		internal int PitDepth
		{
			get { return Math.Max(2, (int)Math.Round(CellSize * renderer.PitRatio)) + Dip(1); }
		}

		internal void PitsChanged() { index.Invalidate(); Invalidate(); }
		internal LevelObj FindObjectAt(int wx, int wy) { return index.FindObjectAt(level, wx, wy); }

		// A pit lies below the floor and its Y is the span's right edge, so the usual search misses it.
		internal LevelObj FindPitAt(int wx, int wy)
		{
			if (wy > 0) return null;
			foreach (LevelObj p in level.Pits)
				if (wx >= p.X && wx < p.Y + Level.Grid) return p;
			return null;
		}

		// Same, lifted objects first: a held selection is out of the level.
		internal LevelObj FindSelectableAt(int wx, int wy)
		{
			LevelObj pit = FindPitAt(wx, wy);
			if (pit != null) return pit;

			for (int i = floating.Count - 1; i >= 0; i--)
			{
				LevelObj o = floating[i];
				if (wx >= o.X && wx < o.X + Level.Grid && wy >= o.Y && wy < o.Y + Level.Grid)
					return o;
			}
			return index.FindObjectAt(level, wx, wy);
		}
		internal void TilesTouched(int a, int b) { renderer.TilesTouched(level, a, b); }
		private void TilesAllDirty() { renderer.TilesAllDirty(); index.Invalidate(); }

		internal Rectangle MakeRect(Point a, Point b)
		{
			// The frame reaches below the floor by the pit depth.
			int floorDepth = PitDepth;
			int bottom = Math.Min(BaselineY + floorDepth, Math.Max(a.Y, b.Y));
			int top = Math.Min(Math.Min(a.Y, b.Y), bottom);
			return new Rectangle(Math.Min(a.X, b.X), top,
								 Math.Abs(a.X - b.X), Math.Max(0, bottom - top));
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			using (Graphics g = CreateGraphics()) dpiScale = g.DpiX / 96.0;
		}

		protected override void OnMouseEnter(EventArgs e) { mouseInside = true; base.OnMouseEnter(e); }

		protected override void OnMouseLeave(EventArgs e)
		{
			mouseInside = false;
			RaiseView();
			base.OnMouseLeave(e);
		}

		// An event for a tool: screen and world point plus the modifiers.
		private ToolEvent MakeEvent(MouseEventArgs e)
		{
			ToolEvent te = new ToolEvent();
			te.Button = e.Button;
			te.Screen = e.Location;
			te.WorldX = ScreenToWorldX(e.X);
			te.WorldY = ScreenToWorldY(e.Y);
			te.DragMode = ((ModifierKeys & Keys.Shift) != 0) != invertDrawMode;
			// Shift keeps the old selection; Ctrl only turns selection mode on.
			te.AddToSelection = (ModifierKeys & Keys.Shift) != 0;
			te.MoveModifier = (ModifierKeys & Keys.Control) != 0;
			te.CopyModifier = (ModifierKeys & Keys.Alt) != 0;
			te.BelowFloor = e.Y > BaselineY;
			return te;
		}

		// Who gets the press: the right button erases (pits below the floor), some tools decide themselves.
		private EditorTool RouteTool(ToolEvent te)
		{
			// Ctrl with the left button is always selection.
			if (te.Button == MouseButtons.Left && te.MoveModifier) return tools[Tool.Select];

			if (te.Button != MouseButtons.Right) return activeTool;
			if (te.BelowFloor) return tools[Tool.Pit];
			return activeTool.HandlesRightButton ? activeTool : tools[Tool.Delete];
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			Focus();
			mouseClient = e.Location;

			// Placing the green line comes first.
			if (PlacingStart && StartRequested != null)
			{
				if (e.Button == MouseButtons.Left) StartRequested(ScreenToWorldX(e.X));
				else if (e.Button == MouseButtons.Right) StartRequested(-1);
				return;
			}

			// While the music plays, a click seeks to the pointer.
			if (e.Button == MouseButtons.Left && SeekOnClick && SeekRequested != null
				&& (ModifierKeys & (Keys.Control | Keys.Alt | Keys.Shift)) == 0 && !spaceDown)
			{
				seeking = true;
				playbackX = ScreenToWorldX(e.X);
				SeekRequested(playbackX);
				Invalidate();
				return;
			}

			// Alt sets the jump guide point alone; with Ctrl it drags a copy.
			if ((ModifierKeys & Keys.Alt) != 0 && (ModifierKeys & Keys.Control) == 0
				&& e.Button != MouseButtons.Middle)
			{
				if (e.Button == MouseButtons.Right) jumpGuide = new Point(int.MinValue, 0);
				else SetJumpGuide(ScreenToWorldX(e.X), ScreenToWorldY(e.Y));
				Invalidate();
				return;
			}

			// A click on the level ends the drag and puts the selection down; grabbing the same selection continues it.
			ToolEvent down = MakeEvent(e);
			if (!GrabbingSelection(down)) DropFloating();

			if (e.Button == MouseButtons.Middle || (spaceDown && e.Button == MouseButtons.Left))
			{
				panning = true;
				panMouseStart = e.Location;
				panViewXStart = viewX;
				panViewYStart = viewY;
				Cursor = Cursors.SizeAll;
				return;
			}

			if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;

			dragTool = RouteTool(down);

			// Without Ctrl, another tool leaves selection mode and drops the selection.
			if (dragTool != tools[Tool.Select] && !down.AddToSelection) ClearSelection();

			dragTool.OnDown(down);
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			mouseInside = true;
			mouseClient = e.Location;

			if (seeking) return;

			if (panning)
			{
				viewX = targetX = Math.Max(0, panViewXStart - (e.X - panMouseStart.X) / ViewScale);
				viewY = targetY = Math.Max(0, panViewYStart + (e.Y - panMouseStart.Y) / ViewScale);
				velX = velY = 0;
				Invalidate();
				RaiseView();
				return;
			}

			if (dragTool != null) dragTool.OnDrag(MakeEvent(e));
			RaiseView();
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			if (seeking) { seeking = false; return; }
			if (panning)
			{
				panning = false;
				Cursor = spaceDown ? Cursors.SizeAll : Cursors.Default;
				return;
			}

			if (dragTool == null) return;
			EditorTool t = dragTool;
			dragTool = null;
			t.OnUp(MakeEvent(e));
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			HandleWheel(e.Delta);
		}

		// Exposed so the wheel works even when the grid does not have focus.
		public void HandleWheel(int delta)
		{
			double notches = delta / 120.0;
			Keys mods = ModifierKeys;

			if ((mods & Keys.Control) != 0)
			{
				// Wheel up raises the zoom number; the setting reverses it.
				double z = invertZoom ? -notches : notches;
				RowsVisible = RowsVisible + (int)Math.Round(z);
				return;
			}
			if ((mods & Keys.Shift) != 0)
			{
				ScrollBy(0, notches * Level.Grid * 2);
				return;
			}
			// By default the wheel up drives forward along the level, down back.
			double h = invertScroll ? -notches : notches;
			double step = Level.Grid * ((mods & Keys.Alt) != 0 ? 12 : 3);
			ScrollBy(h * step, 0);
		}

		internal void SelectInRect(Rectangle r)
		{
			if (r.Width < 3 && r.Height < 3) return;

			int cell = CellSize;
			foreach (LevelObj o in level.Cells.Values)
			{
				int sx = WorldToScreenX(o.X);
				int sy = WorldToScreenY(o.Y + Level.Grid);
				if (r.IntersectsWith(new Rectangle(sx, sy, cell, cell))) selection.Add(o);
			}

			// Pits lie in the floor: their rectangle is the whole span.
			int depth = PitDepth;
			foreach (LevelObj p in level.Pits)
			{
				int px = WorldToScreenX(p.X);
				int pw = Math.Max(2, WorldToScreenX(p.Y + Level.Grid) - px);
				if (r.IntersectsWith(new Rectangle(px, BaselineY - Dip(1), pw, depth)))
					selection.Add(p);
			}
		}

		public void DeleteSelection()
		{
			DropFloating();
			if (selection.Count == 0) return;
			List<LevelObj> list = new List<LevelObj>(selection);
			undo.Do(level, new RemoveCommand(list));
			selection.Clear();
			TilesAllDirty();
			Invalidate();
			RaiseLevel();
		}

		// A lifted selection still counts as objects.
		public int ObjectCount { get { return level.ObjectCount + floating.Count; } }

		// Whether the mouse grabbed an already selected object, so the drag goes on.
		private bool GrabbingSelection(ToolEvent te)
		{
			if (selection.Count == 0) return false;
			if (RouteTool(te) != tools[Tool.Select]) return false;

			// Ctrl with Shift means a frame, not a drag.
			if (te.MoveModifier && te.AddToSelection) return false;

			LevelObj under = FindSelectableAt(te.WorldX, te.WorldY);
			return under != null && selection.Contains(under);
		}

		public void MoveSelection(int dx, int dy)
		{
			if (selection.Count == 0) return;
			if (floating.Count == 0) BeginFloat();

			foreach (LevelObj o in floating)
			{
				int oy = o.Type == ObjType.Pit ? o.Y : o.Y + dy;
				if (o.X + dx < 0 || oy < 0) return;
			}

			foreach (LevelObj o in floating) Level.Shift(o, dx, dy);
			floatDx += dx;
			floatDy += dy;
			Invalidate();
			RaiseLevel();
		}

		// A copy travels while the original stays put.
		public void DuplicateSelectionForMove()
		{
			// A selection in hand is put down first; each Alt grab leaves another imprint.
			if (floating.Count > 0) DropFloating();
			if (selection.Count == 0) return;

			List<LevelObj> copies = new List<LevelObj>();
			foreach (LevelObj o in selection) copies.Add(new LevelObj(o.Type, o.X, o.Y));
			if (copies.Count == 0) return;

			selection.Clear();
			floating.Clear();
			floating.AddRange(copies);
			foreach (LevelObj c in copies) selection.Add(c);
			floatDx = floatDy = 0;
			floatIsCopy = true;
			TilesAllDirty();
			Invalidate();
			RaiseLevel();
		}

		// Lift the selection out of the level: from here it moves on its own.
		private void BeginFloat()
		{
			floating.AddRange(selection);
			foreach (LevelObj o in floating)
			{
				level.Remove(o);
				index.Remove(level, o);
			}
			floatDx = 0;
			floatDy = 0;
			floatIsCopy = false;
			TilesAllDirty();
		}

		// Put the dragged objects back; the drag becomes one history step.
		public void DropFloating()
		{
			if (floating.Count == 0) return;

			List<LevelObj> moved = new List<LevelObj>(floating);
			List<LevelObj> displaced = new List<LevelObj>();
			floating.Clear();

			foreach (LevelObj o in moved)
			{
				LevelObj under = o.Type == ObjType.Pit ? FindSamePit(o) : level.FindSame(o.Type, o.X, o.Y);
				if (under != null && !moved.Contains(under))
				{
					displaced.Add(under);
					level.Remove(under);
				}
				level.Add(o);
			}

			bool changed = floatIsCopy || floatDx != 0 || floatDy != 0;
			ICommand cmd = null;
			if (floatIsCopy)
				cmd = new AddCommand(moved, displaced);
			else if (changed)
				cmd = new FloatMoveCommand(moved, floatDx, floatDy, displaced);
			if (changed)
			{
				ICommand merge = MergePits(moved);
				if (merge != null) cmd = new CompositeCommand(cmd, merge);
			}
			if (cmd != null) undo.Record(cmd);

			floatDx = 0;
			floatDy = 0;
			floatIsCopy = false;
			TilesAllDirty();
			Invalidate();

			// Dropped without moving: nothing changed.
			if (changed) RaiseLevel();
		}

		// Pits that touch or overlap become one span. Only groups that contain
		// one of the fresh pits are merged. The change is applied here; the
		// command is returned for history, or null when nothing merged.
		private ICommand MergePits(List<LevelObj> fresh)
		{
			List<LevelObj> sorted = new List<LevelObj>(level.Pits);
			sorted.Sort(delegate(LevelObj a, LevelObj b) { return a.X.CompareTo(b.X); });

			List<LevelObj> added = new List<LevelObj>();
			List<LevelObj> replaced = new List<LevelObj>();
			int i = 0;
			while (i < sorted.Count)
			{
				int j = i, end = sorted[i].Y;
				bool hasFresh = fresh.Contains(sorted[i]);
				while (j + 1 < sorted.Count && sorted[j + 1].X <= end + Level.Grid)
				{
					j++;
					if (sorted[j].Y > end) end = sorted[j].Y;
					if (fresh.Contains(sorted[j])) hasFresh = true;
				}
				if (j > i && hasFresh)
				{
					for (int k = i; k <= j; k++) replaced.Add(sorted[k]);
					added.Add(new LevelObj(ObjType.Pit, sorted[i].X, end));
				}
				i = j + 1;
			}
			if (replaced.Count == 0) return null;

			bool selected = false;
			foreach (LevelObj o in replaced) if (selection.Remove(o)) selected = true;
			if (selected) foreach (LevelObj o in added) selection.Add(o);

			AddCommand cmd = new AddCommand(added, replaced);
			cmd.Apply(level);
			return cmd;
		}

		// A pit with exactly the same span, so a copy dropped in place adds nothing.
		private LevelObj FindSamePit(LevelObj pit)
		{
			foreach (LevelObj p in level.Pits)
				if (p != pit && p.X == pit.X && p.Y == pit.Y) return p;
			return null;
		}

		public void SelectAll()
		{
			DropFloating();
			selection.Clear();
			foreach (LevelObj o in level.Cells.Values) selection.Add(o);
			foreach (LevelObj o in level.Pits) selection.Add(o);
			Invalidate();
		}

		public void ClearSelection()
		{
			DropFloating();
			selection.Clear();
			Invalidate();
		}

		private readonly List<LevelObj> clipboard = new List<LevelObj>();
		private int clipAnchorX, clipAnchorY;

		public void CopySelection()
		{
			if (selection.Count == 0) return;
			clipboard.Clear();
			int minX = int.MaxValue, minY = int.MaxValue;
			foreach (LevelObj o in selection)
			{
				if (o.X < minX) minX = o.X;
				if (o.Type != ObjType.Pit && o.Y < minY) minY = o.Y;
			}
			if (minY == int.MaxValue) minY = 0;    // only pits are selected
			clipAnchorX = minX;
			clipAnchorY = minY;
			foreach (LevelObj o in selection)
				clipboard.Add(o.Type == ObjType.Pit
					? new LevelObj(ObjType.Pit, o.X - minX, o.Y - minX)
					: new LevelObj(o.Type, o.X - minX, o.Y - minY));
		}

		public void CutSelection()
		{
			if (selection.Count == 0) return;
			CopySelection();
			DeleteSelection();
		}

		public void Paste()
		{
			if (clipboard.Count == 0) return;
			DropFloating();
			// Paste so the centre of what was copied lands under the pointer.
			int clipW = 0, clipH = 0;
			foreach (LevelObj c in clipboard)
			{
				int right = c.Type == ObjType.Pit ? c.Y + Level.Grid : c.X + Level.Grid;
				if (right > clipW) clipW = right;
				if (c.Type != ObjType.Pit && c.Y + Level.Grid > clipH) clipH = c.Y + Level.Grid;
			}

			int baseX = clipAnchorX, baseY = clipAnchorY;
			if (mouseInside)
			{
				int wx = ScreenToWorldX(mouseClient.X) - clipW / 2;
				int wy = ScreenToWorldY(mouseClient.Y) - clipH / 2;
				Point w = SnapWorld(wx, wy);
				baseX = w.X;
				baseY = Math.Max(0, w.Y);
			}

			List<LevelObj> added = new List<LevelObj>();
			List<LevelObj> replaced = new List<LevelObj>();
			foreach (LevelObj c in clipboard)
			{
				if (c.Type == ObjType.Pit)
				{
					// Both pit edges shift equally to keep the length; no vertical move.
					int px = baseX + c.X, pend = baseX + c.Y;
					if (px < 0) continue;
					added.Add(new LevelObj(ObjType.Pit, px, pend));
					continue;
				}

				int x = baseX + c.X, y = baseY + c.Y;
				if (x < 0 || y < 0) continue;
				LevelObj existing = level.FindSame(c.Type, x, y);   // only the same shape is covered over
				if (existing != null) replaced.Add(existing);
				added.Add(new LevelObj(c.Type, x, y));
			}
			if (added.Count == 0) return;
			if (level.ObjectCount + added.Count - replaced.Count > Level.MaxObjects) { RaiseLimit(); return; }

			AddCommand paste = new AddCommand(added, replaced);
			paste.Apply(level);
			selection.Clear();
			foreach (LevelObj o in added) selection.Add(o);
			ICommand merge = MergePits(added);
			undo.Record(merge == null ? (ICommand)paste : new CompositeCommand(paste, merge));
			TilesAllDirty();
			// The tool stays: pasting is a one off action.
			Invalidate();
			RaiseLevel();
		}

		public void DoUndo() { DropFloating(); undo.Undo(level); selection.Clear(); TilesAllDirty(); Invalidate(); RaiseLevel(); }
		public void DoRedo() { DropFloating(); undo.Redo(level); selection.Clear(); TilesAllDirty(); Invalidate(); RaiseLevel(); }

		private int levelWidthCache, levelWidthVersion = -1;

		private int levelHeightCache, levelHeightVersion = -1;

		// How high the objects reach, for the vertical scroll bar; cached per edit.
		public int LevelHeight()
		{
			if (levelHeightVersion != level.Version)
			{
				int max = 0;
				foreach (LevelObj o in level.Cells.Values) if (o.Y > max) max = o.Y;
				// Three cells of margin above the highest object.
				levelHeightCache = max + Level.Grid * 3;
				levelHeightVersion = level.Version;
			}
			return levelHeightCache;
		}

		// Asked every frame, so cached per edit.
		public int LevelWidth()
		{
			if (levelWidthVersion != level.Version)
			{
				int max = level.LevelEnd;
				foreach (LevelObj o in level.Cells.Values) if (o.X > max) max = o.X;
				foreach (LevelObj p in level.Pits) if (p.Y > max) max = p.Y;
				levelWidthCache = max + Level.Grid * 20;
				levelWidthVersion = level.Version;
			}
			return levelWidthCache;
		}

		protected override bool IsInputKey(Keys keyData)
		{
			switch (keyData & Keys.KeyCode)
			{
				case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
				case Keys.Space: case Keys.Home: case Keys.End:
					return true;
			}
			return base.IsInputKey(keyData);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Space) { spaceDown = true; Cursor = Cursors.SizeAll; }
			base.OnKeyDown(e);
		}

		protected override void OnKeyUp(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Space) { spaceDown = false; if (!panning) Cursor = Cursors.Default; }
			base.OnKeyUp(e);
		}

		protected override void OnLostFocus(EventArgs e)
		{
			spaceDown = false;
			base.OnLostFocus(e);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				StopAnim();
				anim.Dispose();
				renderer.Dispose();
			}
			base.Dispose(disposing);
		}

		internal void RaiseLevel() { if (LevelChanged != null) LevelChanged(this, EventArgs.Empty); }

		// Cheap, so the event goes as it is.
		private void RaiseView()
		{
			if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
		}
		internal void RaiseLimit() { if (LimitReached != null) LimitReached(this, EventArgs.Empty); }
	}
}

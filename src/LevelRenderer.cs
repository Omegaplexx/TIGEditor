using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;

namespace TigEditor
{
	// What to draw and where: a snapshot of the view state for one frame.
	public struct RenderView
	{
		public Level Level;
		public int Width, Height;
		public double ViewX, ViewY;
		public double Scale;          // pixels per level unit
		public double TargetScale;    // the zoom the animation is heading for
		public double CellF;          // cell size, fractional
		public int Cell;              // the same rounded, for sprites
		public int TargetCell;
		public int Baseline;          // the screen coordinate of the floor
		public double DpiScale;
		public bool ShowGrid;
		public ICollection<LevelObj> Selection;
		public ICollection<LevelObj> Floating;   // a dragged selection, drawn on top
		public double PlaybackX;
		public double StartX;
	}

	// All level drawing: background, grid, objects, overlays.
	public class LevelRenderer : IDisposable
	{
		private readonly LevelIndex index;
		private RenderView V;

		private bool pureBlack;

		public bool PureBlackBackground
		{
			get { return pureBlack; }
			set { pureBlack = value; DisposeBgBrushes(); }
		}

		private GameAtlas atlas;

		public GameAtlas Atlas
		{
			get { return atlas; }
			set { atlas = value; spriteCell = -1; DisposeBgCache(); DisposeBgBrushes(); }
		}

		// Palette icon colour, custom graphics included.
		public Color SampleFor(int bgId)
		{
			if (bgId == 5 && !pureBlack) return BackgroundSample(bgId);
			if (atlas != null && atlas.BackgroundSamples != null)
			{
				if (bgId < 0 || bgId >= atlas.BackgroundSamples.Length) bgId = 0;
				return atlas.BackgroundSamples[bgId];
			}
			return BackgroundSample(bgId);
		}

		// Palette icon colour: the brightest point of the gradient.
		public static Color BackgroundSample(int bgId)
		{
			if (bgId < 0 || bgId >= BgStops.Length) bgId = 0;
			// black is shown dark gray, or the icon blurs into a blob
			return bgId == 5 ? Color.FromArgb(70, 70, 76) : BgStops[bgId][BgStops[bgId].Length - 1];
		}

		public LevelRenderer(LevelIndex index) { this.index = index; }

		private int ScreenX(double wx) { return (int)Math.Floor((wx - V.ViewX) * V.Scale); }
		private int ScreenY(double wy) { return V.Baseline - (int)Math.Floor(wy * V.Scale); }

		private int Dip(int px) { return Math.Max(1, (int)Math.Round(px * V.DpiScale)); }

		public void Draw(Graphics g, RenderView view)
		{
			V = view;
			EnsureTextures();
			EnsureSprites(V.TargetCell);
			index.Ensure(V.Level);

			g.SmoothingMode = SmoothingMode.None;
			g.InterpolationMode = InterpolationMode.HighQualityBilinear;
			g.PixelOffsetMode = PixelOffsetMode.Half;

			int worldLeft = (int)V.ViewX;
			int worldRight = worldLeft + (int)(V.Width / V.Scale) + Level.Grid;

			DrawBackground(g, V.Width, V.Height, worldRight);
			if (V.ShowGrid) DrawGrid(g, V.Width, V.Cell, V.Baseline);
			DrawSafeZone(g, V.Width, V.Baseline);

			floorPen.Width = Math.Max(1, Dip(2));
			g.DrawLine(floorPen, 0, V.Baseline, V.Width, V.Baseline);

			DrawObjects(g, V.Cell, V.Baseline, worldLeft, worldRight);
			DrawPits(g, worldLeft, worldRight);
			DrawStartLine(g, V.Baseline);
			DrawLevelEnd(g, V.Baseline);
			DrawEffectBars(g, V.Width, V.Cell);
			DrawPlayback(g, V.Height);
		}

		public void Dispose()
		{
			foreach (Bitmap b in pool) b.Dispose();
			pool.Clear();
			DisposeSprites();
			DropTiles();
			gridBrush.Dispose();
			if (gridImage != null) gridImage.Dispose();
			selPen.Dispose();
			playPen.Dispose();
			whiteBrush.Dispose();
			floorPen.Dispose();
			startPen.Dispose();
			endPen.Dispose();
			fallBrush.Dispose();
			riseBrush.Dispose();
		}

		// Gradient stops read off the game background strips, in file order:
		// blue, yellow, green, violet, pink, black.
		private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

		private static readonly Color[][] BgStops = new Color[][]
		{
			new Color[] { C(12,35,37), C(14,40,43), C(17,47,50), C(20,57,60), C(22,66,70), C(26,77,82), C(30,88,94), C(34,99,106), C(38,111,119), C(42,123,131), C(46,134,144), C(49,145,156), C(54,156,168), C(57,166,178), C(59,174,187), C(62,181,195), C(64,187,201) },
			new Color[] { C(43,40,9), C(50,47,11), C(60,55,12), C(71,65,15), C(82,77,17), C(96,89,21), C(111,102,24), C(126,116,27), C(141,130,30), C(156,143,33), C(171,157,37), C(185,170,39), C(198,183,43), C(211,194,45), C(221,204,47), C(231,212,49), C(238,218,51) },
			new Color[] { C(15,43,15), C(17,50,18), C(20,59,21), C(24,69,25), C(29,81,29), C(34,94,34), C(39,108,39), C(45,123,44), C(50,138,49), C(55,152,55), C(60,166,60), C(66,180,66), C(70,192,70), C(74,205,74), C(78,215,78), C(82,224,81), C(84,231,84) },
			new Color[] { C(28,10,46), C(33,12,53), C(38,14,63), C(46,17,74), C(53,20,87), C(62,23,102), C(71,27,117), C(80,30,131), C(91,34,147), C(100,37,163), C(109,40,179), C(118,44,194), C(128,47,208), C(135,51,221), C(143,53,232), C(149,55,241), C(153,57,249) },
			new Color[] { C(44,3,44), C(51,4,52), C(60,5,61), C(72,6,73), C(84,6,85), C(99,7,99), C(113,9,113), C(128,10,128), C(143,12,144), C(159,12,159), C(174,13,174), C(189,15,189), C(203,15,203), C(215,17,216), C(226,18,227), C(235,19,237), C(243,19,244) },
			new Color[] { C(0,0,0), C(0,0,0), C(0,0,0), C(0,0,0), C(0,0,0), C(0,0,0), C(0,0,0), C(0,0,0), C(0,0,0), C(0,0,0), C(0,0,0), C(0,0,0), C(0,0,0), C(0,0,0), C(0,0,0), C(0,0,0), C(0,0,0) },
		};

		private static readonly Color[] SoftBlack = new Color[]
		{
			Color.FromArgb(18, 18, 20), Color.FromArgb(26, 26, 29), Color.FromArgb(36, 36, 40),
			Color.FromArgb(46, 46, 51), Color.FromArgb(56, 56, 62)
		};

		private static Bitmap texBlock, texSpike, texPit;

		private Bitmap PitTexture
		{
			get { return atlas != null && atlas.Pit != null ? atlas.Pit : texPit; }
		}

		internal double PitRatio { get { return PitHeightRatio; } }

		private double PitHeightRatio
		{
			get { return atlas != null && atlas.Pit != null ? atlas.PitRatio : 37.0 / 75.0; }
		}

		private static bool texturesTried;

		// Sprites scaled to the current cell size, rebuilt on zoom change.
		private Bitmap sprBlock, sprSpike, sprPit;

		private int spriteCell = -1;

		private readonly SolidBrush gridBrush = new SolidBrush(Color.FromArgb(48, Color.Black));

		private readonly Pen selPen = new Pen(Color.Yellow, 2f);

		private readonly Pen playPen = new Pen(Color.Red, 2f);

		private readonly SolidBrush whiteBrush = new SolidBrush(Color.White);
		private readonly Pen floorPen = new Pen(Color.White, 2f);
		private readonly Pen startPen = new Pen(Color.FromArgb(200, 255, 255, 255), 2f);
		private readonly Pen endPen = new Pen(Color.White, 2f);
		private readonly SolidBrush fallBrush = new SolidBrush(Color.FromArgb(190, 255, 170, 40));
		private readonly SolidBrush riseBrush = new SolidBrush(Color.FromArgb(190, 0, 190, 255));
		private static readonly SolidBrush arrowBrush = new SolidBrush(Color.White);
		private static readonly Pen arrowPen = new Pen(Color.FromArgb(200, Color.Black), 1f);

		// Tile cache: the level is drawn in tiles that are copied to the screen,
		// so scrolling does not depend on the object count. Tile width is chosen
		// in pixels, not cells.
		private const int TilePixelsWanted = 640;
		private const int MinTileCells = 8;
		private const int MaxTileCells = 256;

		private int tileCells = 16;

		private readonly Dictionary<int, Bitmap> tiles = new Dictionary<int, Bitmap>();

		private readonly Dictionary<int, int> tileUsed = new Dictionary<int, int>();
		// Spare tile images, reused so the collector does not stall.
		private readonly List<Bitmap> pool = new List<Bitmap>();

		private int tileClock;

		private int tileCell = -1, tileVersion = -1, tileHeight = -1;

		private double tileViewY, tileScale = 1;

		private int tileBaselineLocal, tileBmpHeight, tileBmpWidth;

		// Vertical margin so panning does not rebuild tiles.
		private const int TileMargin = 128;

		// Memory budget of the tile cache.
		private const long TileMemoryBudget = 24L * 1024 * 1024;
		private int maxTiles = 8;

		private static void EnsureTextures()
		{
			if (texturesTried) return;
			texturesTried = true;
			texBlock = LoadTexture("block.png");
			texSpike = LoadTexture("spike.png");
			texPit = LoadTexture("pit.png");
		}

		private void EnsureSprites(int cell)
		{
			if (spriteCell == cell) return;
			spriteCell = cell;
			DisposeSprites();
			// Proportions come from the image in use: custom graphics may differ.
			Bitmap block = atlas != null && atlas.Block != null ? atlas.Block : texBlock;
			Bitmap spike = atlas != null && atlas.Spike != null ? atlas.Spike : texSpike;
			Bitmap pit = atlas != null && atlas.Pit != null ? atlas.Pit : texPit;

			double blockRatio = atlas != null && atlas.Block != null ? atlas.BlockRatio : 1.0;
			double spikeRatio = atlas != null && atlas.Spike != null ? atlas.SpikeRatio : 1.0;
			double pitRatio = atlas != null && atlas.Pit != null ? atlas.PitRatio : 37.0 / 75.0;

			sprBlock = Scale(block, cell, Math.Max(2, (int)Math.Round(cell * blockRatio)));
			sprSpike = Scale(spike, cell, Math.Max(2, (int)Math.Round(cell * spikeRatio)));
			sprPit = Scale(pit, cell, Math.Max(2, (int)Math.Round(cell * pitRatio)));
		}

		private static Bitmap Scale(Bitmap src, int w, int h)
		{
			if (src == null || w < 1 || h < 1) return null;
			Bitmap b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
			using (Graphics g = Graphics.FromImage(b))
			using (System.Drawing.Imaging.ImageAttributes attr = new System.Drawing.Imaging.ImageAttributes())
			{
				// Stops light seams where a strip repeats.
				attr.SetWrapMode(WrapMode.TileFlipXY);
				g.Clear(Color.Transparent);
				g.InterpolationMode = InterpolationMode.HighQualityBicubic;
				g.PixelOffsetMode = PixelOffsetMode.HighQuality;
				g.DrawImage(src, new Rectangle(0, 0, w, h),
							0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attr);
			}
			return b;
		}

		private void DisposeSprites()
		{
			if (sprBlock != null) { sprBlock.Dispose(); sprBlock = null; }
			if (sprSpike != null) { sprSpike.Dispose(); sprSpike = null; }
			if (sprPit != null) { sprPit.Dispose(); sprPit = null; }
		}

		private static Bitmap LoadTexture(string name)
		{
			try
			{
				using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
				{
					if (s == null) return null;
					return new Bitmap(s);
				}
			}
			catch { return null; }
		}

		// Background pieces of one colour are gathered and filled in one call.
		// The arrays live between frames to keep the collector quiet.
		private readonly Rectangle[][] bgRuns = new Rectangle[8][];
		private readonly int[] bgRunCount = new int[8];

		private void AddRun(int id, Rectangle r)
		{
			if (bgRuns[id] == null) bgRuns[id] = new Rectangle[16];
			else if (bgRunCount[id] == bgRuns[id].Length)
			{
				Rectangle[] bigger = new Rectangle[bgRuns[id].Length * 2];
				Array.Copy(bgRuns[id], bigger, bgRunCount[id]);
				bgRuns[id] = bigger;
			}
			bgRuns[id][bgRunCount[id]++] = r;
		}

		private void DrawBackground(Graphics g, int w, int h, int worldRight)
		{
			for (int i = 0; i < bgRunCount.Length; i++) bgRunCount[i] = 0;

			int current = 0, segStart = 0;
			foreach (BackgroundChange b in V.Level.Backgrounds)
			{
				int drawX = b.X + GridCanvas.BackgroundDrawShift;
				if (drawX > worldRight) break;
				int sx = Math.Max(0, ScreenX(drawX));
				if (sx > segStart)
				{
					AddRun(current, new Rectangle(segStart, 0, sx - segStart, h));
					segStart = sx;
				}
				current = (b.BgId >= 0 && b.BgId < BgStops.Length) ? b.BgId : 0;
			}
			if (segStart < w) AddRun(current, new Rectangle(segStart, 0, w - segStart, h));

			for (int id = 0; id < bgRunCount.Length; id++)
				if (bgRunCount[id] > 0) FillRuns(g, id, bgRuns[id], bgRunCount[id], h);
		}

		private void FillRuns(Graphics g, int bgId, Rectangle[] runs, int count, int h)
		{
			CompositingMode oldComposite = g.CompositingMode;
			g.CompositingMode = CompositingMode.SourceCopy;

			Bitmap strip = StripFor(bgId, h);
			if (strip != null)
			{
				InterpolationMode oldMode = g.InterpolationMode;
				g.InterpolationMode = InterpolationMode.NearestNeighbor;

				// Whole copies of the strip, pinned to the screen as in the game.
				{
					for (int i = 0; i < count; i++)
					{
						Rectangle r = runs[i];
						int drawn = r.X - (r.X % strip.Width);
						while (drawn < r.Right)
						{
							int left = Math.Max(drawn, r.X);
							int right = Math.Min(drawn + strip.Width, r.Right);
							if (right > left)
							{
								if (left == drawn && right == drawn + strip.Width)
									g.DrawImageUnscaled(strip, drawn, 0);
								else
									g.DrawImage(strip, new Rectangle(left, 0, right - left, r.Height),
												new Rectangle(left - drawn, 0, right - left, r.Height),
												GraphicsUnit.Pixel);
							}
							drawn += strip.Width;
						}
					}
				}

				g.InterpolationMode = oldMode;
			}
			else
			{
				for (int i = 0; i < count; i++)
					g.FillRectangle(BrushFor(bgId, h), runs[i]);
			}

			g.CompositingMode = oldComposite;
		}

		// Atlas strips cached stretched to the window height, as in the game.
		private Bitmap[] bgCache;
		private int bgCacheHeight = -1;

		// Built in gradient brushes.
		private LinearGradientBrush[] bgBrush;
		private int bgBrushHeight = -1;

		private TextureBrush[] stripBrush;

		private Bitmap StripFor(int bgId, int h)
		{
			if (atlas == null || !atlas.HasBackgrounds) return null;
			if (bgId == 5 && !pureBlack) return null;
			if (bgCacheHeight != h) DisposeBgCache();
			if (bgCache == null)
			{
				bgCache = new Bitmap[atlas.BackgroundStrips.Length];
				bgCacheHeight = h;
			}
			if (bgId < 0 || bgId >= bgCache.Length) bgId = 0;
			if (bgCache[bgId] == null)
			{
				// Stretched to the window height, the width follows.
				Bitmap strip = atlas.BackgroundStrips[bgId];
				int w = Math.Max(1, (int)Math.Round(strip.Width * (double)h / strip.Height));
				bgCache[bgId] = Scale(strip, w, h);
			}
			return bgCache[bgId];
		}

		private LinearGradientBrush BrushFor(int bgId, int h)
		{
			if (bgBrushHeight != h) DisposeBgBrushes();
			if (bgBrush == null)
			{
				bgBrush = new LinearGradientBrush[BgStops.Length];
				bgBrushHeight = h;
			}
			if (bgId < 0 || bgId >= bgBrush.Length) bgId = 0;
			if (bgBrush[bgId] != null) return bgBrush[bgId];

			Color[] stops = (bgId == 5 && !pureBlack) ? SoftBlack : BgStops[bgId];
			LinearGradientBrush br = new LinearGradientBrush(
				new Rectangle(0, -1, 1, h + 2), stops[0], stops[stops.Length - 1],
				LinearGradientMode.Vertical);

			ColorBlend blend = new ColorBlend(stops.Length);
			blend.Colors = stops;
			float[] at = new float[stops.Length];
			for (int i = 0; i < at.Length; i++) at[i] = i / (float)(at.Length - 1);
			blend.Positions = at;
			br.InterpolationColors = blend;

			bgBrush[bgId] = br;
			return br;
		}

		private void DisposeBgBrushes()
		{
			if (bgBrush == null) return;
			foreach (LinearGradientBrush b in bgBrush) if (b != null) b.Dispose();
			bgBrush = null;
			bgBrushHeight = -1;
		}


		private void DisposeStripBrushes()
		{
			if (stripBrush == null) return;
			foreach (TextureBrush b in stripBrush) if (b != null) b.Dispose();
			stripBrush = null;
		}

		private void DisposeBgCache()
		{
			DisposeStripBrushes();
			if (bgCache == null) return;
			foreach (Bitmap b in bgCache) if (b != null) b.Dispose();
			bgCache = null;
			bgCacheHeight = -1;
		}


		private void DrawGrid(Graphics g, int w, int cell, int baseline)
		{
			int bottom = Math.Min(V.Height, baseline);
			if (bottom > 0) DrawGridImage(g, w, bottom, baseline);
		}

		// The grid is prepared once as an image and drawn in one copy: much
		// cheaper than translucent fills. Rows are counted from the bottom of
		// the image, which meets the level floor.
		private Bitmap gridImage;
		private double gridImageCell = -1;
		private int gridImageW = -1, gridImageH = -1;

		// Pixel buffer for writing the grid lines directly.
		private int[] gridPixels;

		private void BuildGridPixels(Bitmap image, double cellF)
		{
			int w = image.Width, h = image.Height;
			System.Drawing.Imaging.BitmapData data = image.LockBits(
				new Rectangle(0, 0, w, h),
				System.Drawing.Imaging.ImageLockMode.WriteOnly,
				System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
			try
			{
				int stride = data.Stride / 4;
				int need = stride * h;
				// Kept between rebuilds, allocating it per zoom step is costly.
				if (gridPixels == null || gridPixels.Length < need) gridPixels = new int[need];
				int[] row = gridPixels;
				Array.Clear(row, 0, need);

				// Premultiplied alpha: black with alpha 48.
				const int line = 48 << 24;

				// Truncated like the columns on screen, or lines drift off objects.
				for (int i = 0; ; i++)
				{
					int x = (int)Math.Floor(i * cellF);
					if (x >= w) break;
					for (int y = 0; y < h; y++) row[y * stride + x] = line;
				}
				for (int i = 0; ; i++)
				{
					int y = h - 1 - (int)Math.Round(i * cellF);
					if (y < 0) break;
					int at = y * stride;
					for (int x = 0; x < w; x++) row[at + x] = line;
				}

				System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0, need);
			}
			finally { image.UnlockBits(data); }
		}

		private void DrawGridImage(Graphics g, int w, int bottom, int baseline)
		{
			// While zooming the image is stretched; it is rebuilt once the zoom
			// drifts too far, and exactly when the animation ends.
			double nowCell = Math.Max(2.0, Level.Grid * V.Scale);

			// The zoom counts as arrived within two percent of the target.
			bool moving = V.TargetScale > 0
				&& Math.Abs(V.Scale / V.TargetScale - 1.0) > 0.02;
			double buildCell = moving ? nowCell : Math.Max(2.0, Level.Grid * V.TargetScale);

			// Margin in steps of 128 pixels, so the size does not change every frame.
			int margin = ((int)Math.Ceiling(buildCell) + 130) / 128 * 128;
			int needW = w + margin;

			// Counted from the floor to the screen top: with the view raised the
			// window height alone is not enough.
			int needH = Math.Max(V.Height, baseline + 1) + margin;
			needH = (needH + 127) / 128 * 128;

			// Shrinking is lazy, or scrolling up and down would rebuild it.
			bool stale = gridImage == null || gridImageW != needW
						 || gridImageH < needH || gridImageH > needH * 2;
			if (!stale) needH = gridImageH;
			if (!stale)
				stale = moving
					? (gridImageCell < buildCell * 0.9 || gridImageCell > buildCell * 1.1)
					: Math.Abs(gridImageCell - buildCell) > 0.01;

			if (stale)
			{
				if (gridImage == null || gridImage.Width != needW || gridImage.Height != needH)
				{
					if (gridImage != null) gridImage.Dispose();
					gridImage = new Bitmap(needW, needH,
										   System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
				}
				BuildGridPixels(gridImage, buildCell);
				gridImageCell = buildCell;
				gridImageW = needW;
				gridImageH = needH;
			}

			int firstCol = (int)(V.ViewX / Level.Grid);
			int phaseX = -ScreenX(firstCol * Level.Grid);
			if (phaseX < 0) phaseX = 0;

			double ratio = nowCell / gridImageCell;
			bool exact = Math.Abs(ratio - 1.0) < 0.001;

			int srcX = exact ? phaseX : (int)Math.Round(phaseX / ratio);
			int srcW = exact ? w : (int)Math.Ceiling(w / ratio);
			int srcFloor = exact ? baseline : (int)Math.Round(baseline / ratio);
			int srcH = exact ? bottom : (int)Math.Ceiling(bottom / ratio);

			int srcY = gridImage.Height - 1 - srcFloor;
			int destY = 0;
			int destH = bottom;
			if (srcY < 0)
			{
				int cut = (int)Math.Round(-srcY * ratio);
				destY = cut;
				destH -= cut;
				srcH += srcY;
				srcY = 0;
			}
			if (destH <= 0 || srcH <= 0) return;
			if (srcX + srcW > gridImage.Width) srcW = gridImage.Width - srcX;
			if (srcY + srcH > gridImage.Height) srcH = gridImage.Height - srcY;
			if (srcW <= 0 || srcH <= 0) return;

			InterpolationMode oldMode = g.InterpolationMode;
			if (!exact) g.InterpolationMode = InterpolationMode.NearestNeighbor;
			g.DrawImage(gridImage, new Rectangle(0, destY, w, destH),
						new Rectangle(srcX, srcY, srcW, srcH), GraphicsUnit.Pixel);
			g.InterpolationMode = oldMode;
		}

		private void DrawSafeZone(Graphics g, int w, int baseline)
		{
			if (V.ViewX >= Level.StartOffset) return;
			int x2 = Math.Min(ScreenX(Level.StartOffset), w);
			int bottom = Math.Min(V.Height, baseline);
			if (x2 <= 0 || bottom <= 0) return;
			using (Brush b = new HatchBrush(HatchStyle.ForwardDiagonal,
					   Color.FromArgb(70, Color.Red), Color.Transparent))
				g.FillRectangle(b, 0, 0, x2, bottom);
		}

		// A tile is a piece of the level drawn in advance.
		private void EnsureTileCache(int cell, int height)
		{
			int wanted = RoundToPower(TilePixelsWanted / Math.Max(1.0, cell));

			// Cover the screen and a few tiles around it within the memory budget.
			int perScreen = (int)Math.Ceiling(V.Width / Math.Max(1.0, wanted * (double)cell)) + 1;
			long tileBytes = (long)(wanted * Level.Grid * V.TargetScale + cell)
							 * (height + TileMargin * 2) * 4;
			int affordable = (int)Math.Max(4, TileMemoryBudget / Math.Max(1, tileBytes));
			maxTiles = Math.Max(4, Math.Min(perScreen + 3, affordable));

			bool shifted = Math.Abs((V.ViewY - tileViewY) * V.Scale) > TileMargin;
			bool rescaled = Math.Abs(V.TargetScale - tileScale) > 0.0005;
			if (tileCell == cell && tileVersion == V.Level.Version && tileHeight == height
				&& tileCells == wanted && !shifted && !rescaled)
				return;

			tileCells = wanted;
			DropTiles();
			tileCell = cell;
			tileVersion = V.Level.Version;
			tileHeight = height;
			tileViewY = V.ViewY;
			tileScale = V.TargetScale;
			tileBmpHeight = height + TileMargin * 2;
			tileBmpWidth = Math.Max(1, (int)Math.Ceiling(tileCells * Level.Grid * tileScale) + cell);
			// floor line at the tile's own zoom
			tileBaselineLocal = (int)Math.Round(height - tileScale * Level.Grid * GridCanvas.FloorGapCells
												+ tileViewY * tileScale) + TileMargin;
		}

		private Bitmap TakeBitmap()
		{
			if (pool.Count > 0)
			{
				Bitmap reused = pool[pool.Count - 1];
				pool.RemoveAt(pool.Count - 1);
				if (reused.Width == tileBmpWidth && reused.Height == tileBmpHeight)
				{
					using (Graphics g = Graphics.FromImage(reused))
					{
						g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
						g.Clear(Color.Transparent);
					}
					return reused;
				}
				reused.Dispose();
			}
			return new Bitmap(tileBmpWidth, tileBmpHeight,
							  System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
		}

		private void Recycle(Bitmap bmp)
		{
			if (bmp == null) return;
			if (bmp.Width == tileBmpWidth && bmp.Height == tileBmpHeight && pool.Count < 6)
				pool.Add(bmp);
			else bmp.Dispose();
		}

		private void DropTiles()
		{
			foreach (Bitmap b in tiles.Values) Recycle(b);
			tiles.Clear();
			tileUsed.Clear();
		}

		// Tile width is rounded to a power of two, so it changes rarely.
		private static int RoundToPower(double cells)
		{
			if (cells < MinTileCells) return MinTileCells;
			int best = MinTileCells;
			for (int p = MinTileCells; p <= MaxTileCells; p *= 2)
				if (Math.Abs(Math.Log(p / cells)) < Math.Abs(Math.Log(best / cells))) best = p;
			return best;
		}

		private void EvictTiles()
		{
			while (tiles.Count > maxTiles)
			{
				int oldest = 0, oldestUse = int.MaxValue;
				foreach (KeyValuePair<int, int> kv in tileUsed)
					if (kv.Value < oldestUse) { oldestUse = kv.Value; oldest = kv.Key; }

				Bitmap bmp;
				if (tiles.TryGetValue(oldest, out bmp)) Recycle(bmp);
				tiles.Remove(oldest);
				tileUsed.Remove(oldest);
			}
		}

		// An edit rebuilds only the tiles it touched.
		public void TilesTouched(Level level, int worldFrom, int worldTo)
		{
			int tileWorld = tileCells * Level.Grid;
			int a = LevelIndex.FloorDiv(Math.Min(worldFrom, worldTo), tileWorld) - 1;
			int b = LevelIndex.FloorDiv(Math.Max(worldFrom, worldTo), tileWorld) + 1;
			for (int t = a; t <= b; t++)
			{
				Bitmap bmp;
				if (!tiles.TryGetValue(t, out bmp)) continue;
				Recycle(bmp);
				tiles.Remove(t);
				tileUsed.Remove(t);
			}
			tileVersion = level.Version;
		}

		public void TilesAllDirty()
		{
			DropTiles();
			tileVersion = -1;
		}


		private Bitmap GetTile(int tileIndex, int cell)
		{
			Bitmap bmp;
			if (tiles.TryGetValue(tileIndex, out bmp))
			{
				tileUsed[tileIndex] = ++tileClock;
				return bmp;
			}

			if (!index.ColumnsHaveContent(tileIndex * tileCells, tileCells))
			{
				tiles[tileIndex] = null;
				tileUsed[tileIndex] = ++tileClock;
				return null;
			}

			bmp = TakeBitmap();

			int firstCol = tileIndex * tileCells;
			int worldOrigin = firstCol * Level.Grid;
			int baseline = tileBaselineLocal;
			using (Graphics g = Graphics.FromImage(bmp))
			{
				g.Clear(Color.Transparent);
				g.InterpolationMode = InterpolationMode.NearestNeighbor;
				g.PixelOffsetMode = PixelOffsetMode.Half;

				// Own columns only, or objects at tile seams are drawn twice.
				for (int col = firstCol; col < firstCol + tileCells; col++)
				{
					List<LevelObj> list;
					if (!index.Columns.TryGetValue(col, out list)) continue;
					for (int i = 0; i < list.Count; i++)
					{
						LevelObj o = list[i];
						int sx = (int)Math.Floor((o.X - worldOrigin) * tileScale);

						// Fractional zoom keeps objects on grid lines in tall levels.
						int sy = baseline - (int)Math.Floor((o.Y + Level.Grid) * tileScale);
						if (sy > tileBmpHeight || sy + cell < 0) continue;
						Bitmap spr = o.Type == ObjType.Block ? sprBlock : sprSpike;
						if (spr != null) g.DrawImageUnscaled(spr, sx, sy);
						else DrawFallback(g, o.Type, sx, sy, cell);
					}
				}
			}

			tiles[tileIndex] = bmp;
			tileUsed[tileIndex] = ++tileClock;
			EvictTiles();
			return bmp;
		}

		private void DrawObjects(Graphics g, int cell, int baseline, int worldLeft, int worldRight)
		{
			// The cache first: tile width follows zoom, and tile numbers depend on it.
			EnsureTileCache(V.TargetCell, V.Height);

			int tileWorld = tileCells * Level.Grid;
			int firstTile = LevelIndex.FloorDiv(worldLeft, tileWorld);
			int lastTile = LevelIndex.FloorDiv(worldRight, tileWorld);

			double ratio = V.Scale / tileScale;
			bool exact = Math.Abs(ratio - 1.0) < 0.001;
			int destY = baseline - (int)Math.Round(tileBaselineLocal * ratio);
			int destW = (int)Math.Round(tileBmpWidth * ratio);
			int destH = (int)Math.Round(tileBmpHeight * ratio);

			InterpolationMode old = g.InterpolationMode;
			if (!exact) g.InterpolationMode = InterpolationMode.NearestNeighbor;

			for (int t = firstTile; t <= lastTile; t++)
			{
				Bitmap tile = GetTile(t, V.TargetCell);
				if (tile == null) continue;
				int x = ScreenX(t * tileWorld);
				if (exact) g.DrawImageUnscaled(tile, x, destY);
				else g.DrawImage(tile, x, destY, destW, destH);
			}
			g.InterpolationMode = old;

			DrawSelectionMarks(g, cell, worldLeft, worldRight);
		}

		// Selection is drawn over the tiles.
		private void DrawSelectionMarks(Graphics g, int cell, int worldLeft, int worldRight)
		{
			// Dragged objects are not in the tiles, so they are drawn on top.
			if (V.Floating != null && V.Floating.Count > 0)
				foreach (LevelObj o in V.Floating)
				{
					if (o.Type == ObjType.Pit)
					{
						if (o.Y + Level.Grid < worldLeft || o.X > worldRight) continue;
						DrawPitSpan(g, o.X, o.Y + Level.Grid);
						continue;
					}
					if (o.X + Level.Grid < worldLeft || o.X > worldRight) continue;
					Bitmap spr = o.Type == ObjType.Block ? sprBlock : sprSpike;
					int fx = ScreenX(o.X), fy = ScreenY(o.Y + Level.Grid);
					if (spr != null) g.DrawImageUnscaled(spr, fx, fy);
					else DrawFallback(g, o.Type, fx, fy, cell);
				}

			if (V.Selection.Count == 0) return;
			foreach (LevelObj o in V.Selection)
			{
				if (o.Type == ObjType.Pit)
				{
					// One frame for the whole pit span.
					if (o.Y + Level.Grid < worldLeft || o.X > worldRight) continue;
					int px = ScreenX(o.X);
					int pw = Math.Max(2, ScreenX(o.Y + Level.Grid) - px);
					int lift = Dip(1);
					int ph = Math.Max(2, (int)Math.Round(V.Cell * PitHeightRatio)) + lift;
					g.DrawRectangle(selPen, px, V.Baseline - lift, pw, ph);
					continue;
				}
				if (o.X + Level.Grid < worldLeft || o.X > worldRight) continue;
				g.DrawRectangle(selPen, ScreenX(o.X), ScreenY(o.Y + Level.Grid), cell, cell);
			}
		}


		private static readonly SolidBrush blackBrush = new SolidBrush(Color.Black);

		private static void DrawFallback(Graphics g, ObjType type, int sx, int sy, int cell)
		{
			{
				Brush b = blackBrush;
				if (type == ObjType.Block) g.FillRectangle(b, sx, sy, cell, cell);
				else
					g.FillPolygon(b, new Point[]
					{
						new Point(sx, sy + cell),
						new Point(sx + cell / 2, sy),
						new Point(sx + cell, sy + cell)
					});
			}
		}

		// Pits are drawn as in the game: texture tiles from the start of the span.
		private void DrawPits(Graphics g, int worldLeft, int worldRight)
		{
			if (PitTexture == null) return;
			foreach (int[] span in index.PitSpans)
			{
				if (span[1] < worldLeft || span[0] > worldRight) continue;
				DrawPitSpan(g, span[0], span[1]);
			}
		}

		// The last texture tile is stretched to the end of the span.
		private void DrawPitSpan(Graphics g, int from, int toExclusive)
		{
			Bitmap tex = PitTexture;
			if (tex == null) return;
			int lift = Dip(1);
			int top = V.Baseline - lift;
			int height = Math.Max(2, (int)Math.Round(V.Cell * PitHeightRatio)) + lift;

			int x = from;
			while (x + Level.Grid < toExclusive)
			{
				int a = ScreenX(x), b = ScreenX(x + Level.Grid);
				if (b >= 0 && a <= V.Width)
					g.DrawImage(tex, a, top, Math.Max(1, b - a), height);
				x += Level.Grid;
			}

			int lastA = ScreenX(x), lastB = ScreenX(toExclusive);
			if (lastB >= 0 && lastA <= V.Width)
				g.DrawImage(tex, lastA, top, Math.Max(1, lastB - lastA), height);
		}

		private void DrawStartLine(Graphics g, int baseline)
		{
			int sx = ScreenX(Level.PlayerOffset);
			if (sx < -4 || sx > V.Width + 4) return;
			startPen.Width = Dip(2);
			startPen.DashStyle = DashStyle.Dot;
			g.DrawLine(startPen, sx, 0, sx, baseline);
		}

		private void DrawLevelEnd(Graphics g, int baseline)
		{
			int sx = ScreenX(V.Level.LevelEnd);
			if (sx < -Dip(30) || sx > V.Width + Dip(30)) return;
			endPen.Width = Dip(2);
			g.DrawLine(endPen, sx, 0, sx, baseline);
			g.FillRectangle(whiteBrush, sx, Dip(4), Dip(16), Dip(11));
		}

		private void DrawEffectBars(Graphics g, int w, int cell)
		{
			int barH = Dip(7);
			int band = Dip(5);

			// Gravity: every record flips it, so odd points flip up and even
			// ones flip back down.
			DrawIntervals(g, V.Level.BlocksFall, fallBrush, band, barH, Level.EffectFallDrawShift);
			DrawIntervals(g, V.Level.BlocksRise, riseBrush, band + barH + Dip(2), barH, Level.EffectRiseDrawShift);

			bool up = true;
			SmoothingMode old = g.SmoothingMode;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			for (int i = 0; i < V.Level.GravityChanges.Count; i++)
			{
				int sx = ScreenX(V.Level.GravityChanges[i] + Level.GravityDrawShift);
				if (sx >= -Dip(20) && sx <= w + Dip(20))
					DrawGravityArrow(g, sx, band, Dip(16), up);
				up = !up;
			}
			g.SmoothingMode = old;
		}

		private static void DrawGravityArrow(Graphics g, int sx, int top, int size, bool up)
		{
			Color fill = up ? Color.FromArgb(240, 255, 190, 40) : Color.FromArgb(240, 90, 170, 255);
			int y1 = top, y2 = top + size;
			Point[] arrow = up
				? new Point[] { new Point(sx, y1), new Point(sx - size / 2, y2), new Point(sx + size / 2, y2) }
				: new Point[] { new Point(sx, y2), new Point(sx - size / 2, y1), new Point(sx + size / 2, y1) };
			arrowBrush.Color = fill;
			{
				Brush b = arrowBrush;
				Pen p = arrowPen;
				g.FillPolygon(b, arrow);
				g.DrawPolygon(p, arrow);
			}
		}

		private void DrawIntervals(Graphics g, List<Interval> list, Brush brush, int top, int height, int shift)
		{
			foreach (Interval iv in list)
			{
				int sx = ScreenX(iv.StartX + shift);
				int sx2 = ScreenX(iv.EndX + shift);
				if (sx2 < 0 || sx > V.Width) continue;
				g.FillRectangle(brush, sx, top, Math.Max(2, sx2 - sx), height);
			}
		}

		private void DrawPlayback(Graphics g, int h)
		{
			// Green line under the red one.
			if (V.StartX >= 0)
			{
				int gx = ScreenX(V.StartX);
				if (gx >= 0 && gx <= V.Width)
					using (Pen p = new Pen(Color.FromArgb(60, 200, 90), Dip(2)))
						g.DrawLine(p, gx, 0, gx, h);
			}

			if (V.PlaybackX < 0) return;
			int sx = ScreenX(V.PlaybackX);
			if (sx < 0 || sx > V.Width) return;
			playPen.Width = Dip(2);
			g.DrawLine(playPen, sx, 0, sx, h);
		}
	}
}

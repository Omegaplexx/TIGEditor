using System;
using System.Collections.Generic;
using System.IO;

namespace TigEditor
{
	// Object types, as in the level file
	public enum ObjType : byte { Block = 0, Spike = 1, Pit = 2 }

	// One level object. For a block and a spike X and Y are the bottom left
	// corner. For a pit X is the left edge and Y the right one, that is how the
	// format works.
	public class LevelObj
	{
		public ObjType Type;
		public int X;
		public int Y;

		// A sequence number. In the file objects are sorted by X only, and within
		// one X the order they were added is kept. The number keeps a resaved
		// file from being shuffled, so it matches the original byte for byte.
		public int Seq;

		public LevelObj(ObjType type, int x, int y) { Type = type; X = x; Y = y; }
	}

	public class BackgroundChange
	{
		public int X;
		public int BgId;      // 0 blue, 1 yellow, 2 green, 3 violet, 4 pink, 5 black
		public string CustomName;   // when not null this is an image background, type 1

		public BackgroundChange(int x, int bgId) { X = x; BgId = bgId; }
	}

	public class Interval
	{
		public int StartX;
		public int EndX;
		public Interval(int a, int b) { StartX = a; EndX = b; }
	}

	public class Level
	{
		// IMPORTANT, ABOUT COORDINATES.
		//
		// The numbers in a level file are not where things stand in the game.
		// The format comes from the original editor, and the game acts on some
		// objects with an offset: effect stripes and gravity marks fire well to
		// the right of where they are written.
		//
		// The editor shows everything the way the game will, so the shifts are
		// added on display and taken off on write. The author sees the truth and
		// the file stays exactly as the original editor would leave it.
		//
		// Display shifts, for the screen only:
		//   gravity        9 cells to the right  (GravityDrawShift)
		//   sink stripes   8 cells to the right  (EffectFallDrawShift)
		//   rise stripes   none                  (EffectRiseDrawShift)
		//   background     300 units, two cells and a bit
		//                       (GridCanvas.BackgroundDrawShift)
		//   blocks, spikes, pits   none, they stand where they are written
		//
		// The one shift inside the file itself:
		//   sink stripes   EffectShift, half a cell and five more units. Taken
		//   off on read and added on write, so an opened and resaved level keeps
		//   the same numbers. Rise stripes are not affected.

		public const int Grid = 30;              // cell size in pixels
		public const int StartOffset = 810;      // the original editor builds nothing left of this
		public const int MaxObjects = 32767;     // in the file the object count is a signed short
		public const int UnitsPerSecond = 360;   // scroll speed, from the editor
		public const int PlayerOffset = 150;     // the cube stands here, and the music is counted from here too
		public const int GravityDrawShift = 9 * Grid;    // gravity marks are shown right of where they are written x
		public const int EffectFallDrawShift = 8 * Grid;   // sinking is shown right of its record
		public const int EffectRiseDrawShift = 0;          // rising is shown where it is written
		public const int DefaultLevelEnd = 100 * Grid + Grid / 2;   // 3015

		public bool UsesSpecialGraphics;
		public int LevelEnd = DefaultLevelEnd;

		// Blocks and spikes are kept by exact coordinate, not by cell number:
		// original levels hold objects off the grid and they must not be lost.
		public readonly Dictionary<long, LevelObj> Cells = new Dictionary<long, LevelObj>();

		// A pit is a continuous span, exactly as in the file: X is the start,
		// Y is the start of the last cell, and it covers X to Y + 30. No cells
		// and no grid, a span can be placed anywhere.
		public readonly List<LevelObj> Pits = new List<LevelObj>();
		public readonly List<BackgroundChange> Backgrounds = new List<BackgroundChange>();
		public readonly List<int> GravityChanges = new List<int>();
		// Orange sink stripes sit in the file ahead of what the editor shows, by
		// half a cell and five more units. The shift lives on read and write only
		// and is the same both ways, so an opened and resaved level keeps its
		// numbers. Blue rise stripes are not affected.
		public const int EffectShift = Grid / 2 + 5;

		public readonly List<Interval> BlocksFall = new List<Interval>();
		public readonly List<Interval> BlocksRise = new List<Interval>();

		private int nextSeq;

		// An edit counter. The renderer reads it to know when to rebuild its
		// index of objects by column.
		public int Version { get; private set; }
		public void Touch() { Version++; }

		// The key includes the type: the format allows a block and a spike in one cell.
		public static long Key(ObjType type, int x, int y)
		{
			return (((long)x << 32) ^ (uint)y) * 3 + (byte)type;
		}

		public int ObjectCount { get { return Cells.Count + Pits.Count; } }

		// Whether a point is covered by any pit.

		public void Clear()
		{
			Cells.Clear();
			Pits.Clear();
			Backgrounds.Clear();
			GravityChanges.Clear();
			BlocksFall.Clear();
			BlocksRise.Clear();
			UsesSpecialGraphics = false;
			LevelEnd = DefaultLevelEnd;
			nextSeq = 0;
			Touch();
		}

		// An exact match of type and coordinates.
		public LevelObj FindSame(ObjType type, int x, int y)
		{
			LevelObj o;
			return Cells.TryGetValue(Key(type, x, y), out o) ? o : null;
		}

		// Finds an object in a cell whatever its type: drawing and erasing work by cell.
		public LevelObj FindAt(int x, int y)
		{
			LevelObj o;
			if (Cells.TryGetValue(Key(ObjType.Block, x, y), out o)) return o;
			if (Cells.TryGetValue(Key(ObjType.Spike, x, y), out o)) return o;
			return null;
		}

		public List<LevelObj> AllObjects()
		{
			List<LevelObj> all = new List<LevelObj>();
			all.AddRange(Cells.Values);
			all.AddRange(Pits);
			// as in the original editor: sorted by X only, and objects sharing
			// an X keep the order they were added in
			all.Sort(delegate(LevelObj a, LevelObj b)
			{
				if (a.X != b.X) return a.X.CompareTo(b.X);
				return a.Seq.CompareTo(b.Seq);
			});
			return all;
		}

		public void Add(LevelObj o)
		{
			if (o.Seq == 0) o.Seq = ++nextSeq;
			else if (o.Seq > nextSeq) nextSeq = o.Seq;
			if (o.Type == ObjType.Pit) Pits.Add(o);
			else Cells[Key(o.Type, o.X, o.Y)] = o;
			Touch();
		}

		// Moving an object. For a pit X is the start and Y the end of the span,
		// so both edges move horizontally and it does not move vertically at all.
		// Every move goes through here, so undo puts back exactly what shifted.
		public static void Shift(LevelObj o, int dx, int dy)
		{
			o.X += dx;
			if (o.Type == ObjType.Pit) o.Y += dx;
			else o.Y += dy;
		}

		public void Remove(LevelObj o)
		{
			if (o.Type == ObjType.Pit) Pits.Remove(o);
			else
			{
				LevelObj cur;
				long k = Key(o.Type, o.X, o.Y);
				if (Cells.TryGetValue(k, out cur) && cur == o) Cells.Remove(k);
			}
			Touch();
		}


		// ---------- reading ----------

		public static Level Load(string path)
		{
			using (FileStream fs = File.OpenRead(path))
			using (BigEndianReader r = new BigEndianReader(fs))
			{
				int version = r.ReadInt32();
				if (version != 0)
					throw new IOException(S.Get("ErrFormatVersion") + version);

				Level lv = new Level();
				lv.UsesSpecialGraphics = r.ReadByte() != 0;

				int count = r.ReadInt16();
				for (int i = 0; i < count; i++)
				{
					byte type = r.ReadByte();
					int x = r.ReadInt32();
					int y = r.ReadInt32();
					if (type > 2) throw new IOException(S.Get("ErrObjectType") + type);
					LevelObj o = new LevelObj((ObjType)type, x, y);
					o.Seq = i + 1;
					lv.Add(o);
				}

				lv.LevelEnd = r.ReadInt32();

				int bgCount = r.ReadInt32();
				for (int i = 0; i < bgCount; i++)
				{
					int x = r.ReadInt32();
					byte type = r.ReadByte();
					if (type == 0)
					{
						lv.Backgrounds.Add(new BackgroundChange(x, r.ReadInt32()));
					}
					else if (type == 1)
					{
						// An image background. This branch is broken in the Java
						// editor: it writes with writeChars and reads with
						// readUTF. We only read it, as readUTF.
						BackgroundChange bc = new BackgroundChange(x, 0);
						bc.CustomName = r.ReadJavaUtf();
						lv.Backgrounds.Add(bc);
					}
					else throw new IOException(S.Get("ErrBackgroundType") + type);
				}

				int gravCount = r.ReadInt32();
				for (int i = 0; i < gravCount; i++) lv.GravityChanges.Add(r.ReadInt32());

				int fallCount = r.ReadInt32();
				for (int i = 0; i < fallCount; i++)
					lv.BlocksFall.Add(new Interval(r.ReadInt32() - EffectShift,
												   r.ReadInt32() - EffectShift));

				int riseCount = r.ReadInt32();
				for (int i = 0; i < riseCount; i++)
					lv.BlocksRise.Add(new Interval(r.ReadInt32(), r.ReadInt32()));

				return lv;
			}
		}

		// ---------- writing ----------

		public byte[] ToBytes()
		{
			List<LevelObj> objs = AllObjects();
			if (objs.Count > MaxObjects)
				throw new InvalidOperationException(string.Format(S.Get("ErrTooManyObjects"), MaxObjects));

			using (MemoryStream ms = new MemoryStream())
			using (BigEndianWriter w = new BigEndianWriter(ms))
			{
				w.WriteInt32(0);
				w.WriteByte((byte)(UsesSpecialGraphics ? 1 : 0));
				w.WriteInt16((short)objs.Count);
				foreach (LevelObj o in objs)
				{
					w.WriteByte((byte)o.Type);
					w.WriteInt32(o.X);
					w.WriteInt32(o.Y);
				}
				w.WriteInt32(LevelEnd);

				w.WriteInt32(Backgrounds.Count);
				foreach (BackgroundChange b in Backgrounds)
				{
					w.WriteInt32(b.X);
					if (b.CustomName == null)
					{
						w.WriteByte(0);
						w.WriteInt32(b.BgId);
					}
					else
					{
						w.WriteByte(1);
						w.WriteJavaUtf(b.CustomName);
					}
				}

				w.WriteInt32(GravityChanges.Count);
				foreach (int x in GravityChanges) w.WriteInt32(x);

				w.WriteInt32(BlocksFall.Count);
				foreach (Interval iv in BlocksFall)
				{ w.WriteInt32(iv.StartX + EffectShift); w.WriteInt32(iv.EndX + EffectShift); }

				w.WriteInt32(BlocksRise.Count);
				foreach (Interval iv in BlocksRise) { w.WriteInt32(iv.StartX); w.WriteInt32(iv.EndX); }

				w.Flush();
				return ms.ToArray();
			}
		}

		// Written through a temporary file, so a failure mid write loses nothing.
		public void Save(string path)
		{
			byte[] data = ToBytes();
			string tmp = path + ".tmp";
			File.WriteAllBytes(tmp, data);
			if (File.Exists(path)) File.Delete(path);
			File.Move(tmp, path);
		}
	}

	// The format is big endian throughout, as Java DataOutputStream writes it.
	public class BigEndianReader : IDisposable
	{
		private readonly Stream s;
		public BigEndianReader(Stream stream) { s = stream; }

		public byte ReadByte()
		{
			int b = s.ReadByte();
			if (b < 0) throw new EndOfStreamException();
			return (byte)b;
		}

		public short ReadInt16() { return (short)((ReadByte() << 8) | ReadByte()); }

		public int ReadInt32()
		{
			int a = ReadByte(), b = ReadByte(), c = ReadByte(), d = ReadByte();
			return (a << 24) | (b << 16) | (c << 8) | d;
		}

		public string ReadJavaUtf()
		{
			int len = (ushort)ReadInt16();
			byte[] buf = new byte[len];
			int read = 0;
			while (read < len)
			{
				int n = s.Read(buf, read, len - read);
				if (n <= 0) throw new EndOfStreamException();
				read += n;
			}
			return System.Text.Encoding.UTF8.GetString(buf);
		}

		public void Dispose() { }
	}

	public class BigEndianWriter : IDisposable
	{
		private readonly Stream s;
		public BigEndianWriter(Stream stream) { s = stream; }

		public void WriteByte(byte v) { s.WriteByte(v); }
		public void WriteInt16(short v) { WriteByte((byte)(v >> 8)); WriteByte((byte)v); }

		public void WriteInt32(int v)
		{
			WriteByte((byte)(v >> 24));
			WriteByte((byte)(v >> 16));
			WriteByte((byte)(v >> 8));
			WriteByte((byte)v);
		}

		public void WriteJavaUtf(string text)
		{
			byte[] buf = System.Text.Encoding.UTF8.GetBytes(text);
			WriteInt16((short)buf.Length);
			s.Write(buf, 0, buf.Length);
		}

		public void Flush() { s.Flush(); }
		public void Dispose() { }
	}
}

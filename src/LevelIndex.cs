using System;
using System.Collections.Generic;

namespace TigEditor
{
    // Level objects laid out by grid column. Wanted by the renderer, which draws
    // visible columns only, and by the tools, which look for the object under the
    // pointer.
    public class LevelIndex
    {
        // The index by grid column: only visible columns are drawn, so the speed
        // does not depend on how many objects the level holds.
        public readonly Dictionary<int, List<LevelObj>> Columns = new Dictionary<int, List<LevelObj>>();


        // Merged pit spans: neighbouring and overlapping cells are joined into
        // continuous ranges. Drawing goes by these, so the texture is never laid
        // down twice.
        public readonly List<int[]> PitSpans = new List<int[]>();

        private int indexedVersion = -1;

        // Objects are laid out by column. Rebuilt after an edit only, not on
        // every repaint.
        public void Ensure(Level level)
        {
            if (indexedVersion == level.Version) return;
            indexedVersion = level.Version;
            Columns.Clear();
            BuildSpans(level);
            foreach (LevelObj o in level.Cells.Values) AddToColumn(o);
        }

        // A pinpoint update: there is no reason to rebuild thirty thousand
        // objects because of one placed block.
        public void Add(Level level, LevelObj o)
        {
            if (indexedVersion < 0) return;          // the index will be rebuilt anyway
            int col = FloorDiv(o.X, Level.Grid);
            List<LevelObj> list;
            if (!Columns.TryGetValue(col, out list))
            {
                list = new List<LevelObj>(2);
                Columns[col] = list;
            }
            list.Add(o);
            indexedVersion = level.Version;
        }


        public void Remove(Level level, LevelObj o)
        {
            if (indexedVersion < 0) return;
            int col = FloorDiv(o.X, Level.Grid);
            List<LevelObj> list;
            if (Columns.TryGetValue(col, out list)) list.Remove(o);
            indexedVersion = level.Version;
        }

        public static int FloorDiv(int v, int d)
        {
            int q = v / d;
            if (v < 0 && v % d != 0) q--;
            return q;
        }

        // The object under the pointer is found by its real rectangle, not by
        // cell: without snapping objects stand anywhere and a cell hit fails.
        private void AddToColumn(LevelObj o)
        {
            int col = FloorDiv(o.X, Level.Grid);
            List<LevelObj> list;
            if (!Columns.TryGetValue(col, out list))
            {
                list = new List<LevelObj>(2);
                Columns[col] = list;
            }
            list.Add(o);
        }

        public LevelObj FindObjectAt(Level level, int wx, int wy)
        {
            Ensure(level);
            int col = FloorDiv(wx, Level.Grid);
            for (int c = col - 1; c <= col + 1; c++)
            {
                List<LevelObj> list;
                if (!Columns.TryGetValue(c, out list)) continue;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    LevelObj o = list[i];
                    if (wx >= o.X && wx < o.X + Level.Grid && wy >= o.Y && wy < o.Y + Level.Grid)
                        return o;
                }
            }
            return null;
        }

        // Whether a pit already takes any part of the cell starting at x.

        public void Invalidate() { indexedVersion = -1; }

        // Pit spans for drawing: a span covers X to Y + 30.
        private void BuildSpans(Level level)
        {
            PitSpans.Clear();
            foreach (LevelObj p in level.Pits)
                PitSpans.Add(new int[] { p.X, p.Y + Level.Grid });
            PitSpans.Sort(delegate(int[] a, int[] b) { return a[0].CompareTo(b[0]); });
        }


        public bool ColumnsHaveContent(int firstCol, int count)
        {
            for (int col = firstCol; col < firstCol + count; col++)
                if (Columns.ContainsKey(col)) return true;
            return false;
        }
    }
}

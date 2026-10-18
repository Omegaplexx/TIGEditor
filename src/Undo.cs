using System;
using System.Collections.Generic;

namespace TigEditor
{
    public interface ICommand
    {
        void Apply(Level level);
        void Revert(Level level);
        string Name { get; }
    }

    // Adding a batch of objects. Dragging puts everything into one command, so
    // Ctrl+Z undoes the whole stroke instead of one block at a time.
    public class AddCommand : ICommand
    {
        private readonly List<LevelObj> added;
        private readonly List<LevelObj> replaced;   // what lay in the same cells

        public AddCommand(List<LevelObj> added, List<LevelObj> replaced)
        {
            this.added = added;
            this.replaced = replaced;
        }

        public string Name { get { return S.UndoAdd; } }

        public void Apply(Level level)
        {
            foreach (LevelObj o in replaced) level.Remove(o);
            foreach (LevelObj o in added) level.Add(o);
        }

        public void Revert(Level level)
        {
            foreach (LevelObj o in added) level.Remove(o);
            foreach (LevelObj o in replaced) level.Add(o);
        }
    }

    public class RemoveCommand : ICommand
    {
        private readonly List<LevelObj> removed;

        public RemoveCommand(List<LevelObj> removed) { this.removed = removed; }

        public string Name { get { return S.UndoDelete; } }

        public void Apply(Level level) { foreach (LevelObj o in removed) level.Remove(o); }
        public void Revert(Level level) { foreach (LevelObj o in removed) level.Add(o); }
    }

    public class MoveCommand : ICommand
    {
        private readonly List<LevelObj> moved;
        private readonly int dx, dy;

        // Objects that a drag covered over. The format cannot tell two identical
        // shapes in one cell apart, so the covered ones go away, are remembered
        // here and come back on undo.
        private readonly List<LevelObj> displaced = new List<LevelObj>();

        public MoveCommand(List<LevelObj> moved, int dx, int dy)
        {
            this.moved = moved; this.dx = dx; this.dy = dy;
        }

        public string Name { get { return S.UndoMove; } }

        public void Apply(Level level)
        {
            displaced.Clear();
            foreach (LevelObj o in moved) level.Remove(o);
            foreach (LevelObj o in moved) Level.Shift(o, dx, dy);
            foreach (LevelObj o in moved)
            {
                LevelObj under = level.FindSame(o.Type, o.X, o.Y);
                if (under != null) { displaced.Add(under); level.Remove(under); }
                level.Add(o);
            }
        }

        public void Revert(Level level)
        {
            foreach (LevelObj o in moved) level.Remove(o);
            foreach (LevelObj o in moved) Level.Shift(o, -dx, -dy);
            foreach (LevelObj o in displaced) level.Add(o);
            foreach (LevelObj o in moved) level.Add(o);
            displaced.Clear();
        }
    }

    // Moving a selection. Until it is dropped the objects hang apart and erase
    // nothing. On dropping they land on the level, and only then does whatever
    // ended up under them go away.
    public class FloatMoveCommand : ICommand
    {
        private readonly List<LevelObj> moved;
        private readonly List<LevelObj> displaced;
        private readonly int dx, dy;

        public FloatMoveCommand(List<LevelObj> moved, int dx, int dy, List<LevelObj> displaced)
        {
            this.moved = moved; this.dx = dx; this.dy = dy; this.displaced = displaced;
        }

        public string Name { get { return S.UndoMoveSelection; } }

        public void Apply(Level level)
        {
            foreach (LevelObj o in moved) level.Remove(o);
            foreach (LevelObj o in moved) Level.Shift(o, dx, dy);
            foreach (LevelObj o in displaced) level.Remove(o);
            foreach (LevelObj o in moved) level.Add(o);
        }

        public void Revert(Level level)
        {
            foreach (LevelObj o in moved) level.Remove(o);
            foreach (LevelObj o in moved) Level.Shift(o, -dx, -dy);
            foreach (LevelObj o in displaced) level.Add(o);
            foreach (LevelObj o in moved) level.Add(o);
        }
    }

    // Gravity marks, background changes and effect stripes are edited alike:
    // something was added, something was taken away.
    public class GravityCommand : ICommand
    {
        private readonly List<int> added, removed;
        public GravityCommand(List<int> added, List<int> removed) { this.added = added; this.removed = removed; }
        public string Name { get { return S.UndoGravity; } }

        public void Apply(Level level)
        {
            foreach (int x in removed) level.GravityChanges.Remove(x);
            foreach (int x in added) level.GravityChanges.Add(x);
            level.GravityChanges.Sort();
            level.Touch();
        }

        public void Revert(Level level)
        {
            foreach (int x in added) level.GravityChanges.Remove(x);
            foreach (int x in removed) level.GravityChanges.Add(x);
            level.GravityChanges.Sort();
            level.Touch();
        }
    }

    public class BackgroundCommand : ICommand
    {
        private readonly List<BackgroundChange> added, removed;
        public BackgroundCommand(List<BackgroundChange> added, List<BackgroundChange> removed)
        {
            this.added = added; this.removed = removed;
        }
        public string Name { get { return S.UndoBackground; } }

        private static void Sort(Level level)
        {
            level.Backgrounds.Sort(delegate(BackgroundChange a, BackgroundChange b) { return a.X.CompareTo(b.X); });
            level.Touch();
        }

        public void Apply(Level level)
        {
            foreach (BackgroundChange b in removed) level.Backgrounds.Remove(b);
            foreach (BackgroundChange b in added) level.Backgrounds.Add(b);
            Sort(level);
        }

        public void Revert(Level level)
        {
            foreach (BackgroundChange b in added) level.Backgrounds.Remove(b);
            foreach (BackgroundChange b in removed) level.Backgrounds.Add(b);
            Sort(level);
        }
    }

    public class IntervalCommand : ICommand
    {
        private readonly bool rise;
        private readonly List<Interval> added, removed;

        public IntervalCommand(bool rise, List<Interval> added, List<Interval> removed)
        {
            this.rise = rise; this.added = added; this.removed = removed;
        }

        public string Name { get { return S.UndoEffect; } }

        private List<Interval> Target(Level level) { return rise ? level.BlocksRise : level.BlocksFall; }

        public void Apply(Level level)
        {
            List<Interval> t = Target(level);
            foreach (Interval iv in removed) t.Remove(iv);
            foreach (Interval iv in added) t.Add(iv);
            t.Sort(delegate(Interval a, Interval b) { return a.StartX.CompareTo(b.StartX); });
            level.Touch();
        }

        public void Revert(Level level)
        {
            List<Interval> t = Target(level);
            foreach (Interval iv in added) t.Remove(iv);
            foreach (Interval iv in removed) t.Add(iv);
            t.Sort(delegate(Interval a, Interval b) { return a.StartX.CompareTo(b.StartX); });
            level.Touch();
        }
    }

    public class SetLevelEndCommand : ICommand
    {
        private readonly int oldValue, newValue;
        public SetLevelEndCommand(int oldValue, int newValue) { this.oldValue = oldValue; this.newValue = newValue; }
        public string Name { get { return S.UndoFinish; } }
        public void Apply(Level level) { level.LevelEnd = newValue; level.Touch(); }
        public void Revert(Level level) { level.LevelEnd = oldValue; level.Touch(); }
    }

    public class UndoStack
    {
        private readonly Stack<ICommand> undo = new Stack<ICommand>();
        private readonly Stack<ICommand> redo = new Stack<ICommand>();

        // Which step of history the level was saved at. A negative value means
        // the saved state cannot be reached any more: something new was done
        // after an undo and the steps beyond were wiped.
        private int savedAt;


        // Whether the level right now matches what was saved to disk.
        public bool AtSavedPoint { get { return undo.Count == savedAt; } }

        public void MarkSaved() { savedAt = undo.Count; }

        public void Clear()
        {
            undo.Clear();
            redo.Clear();
            savedAt = 0;
        }

        public void Do(Level level, ICommand cmd)
        {
            cmd.Apply(level);
            Record(cmd);
        }

        // The change is already applied to the level, as with a drag stroke, and
        // only has to be put into history.
        public void Record(ICommand cmd)
        {
            // A new step after an undo wipes what came after. If the saved state
            // lay there, it cannot be reached again.
            if (savedAt > undo.Count) savedAt = -1;
            undo.Push(cmd);
            redo.Clear();
        }

        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }

        public void Undo(Level level)
        {
            if (undo.Count == 0) return;
            ICommand c = undo.Pop();
            c.Revert(level);
            redo.Push(c);
        }

        public void Redo(Level level)
        {
            if (redo.Count == 0) return;
            ICommand c = redo.Pop();
            c.Apply(level);
            undo.Push(c);
        }
    }
}

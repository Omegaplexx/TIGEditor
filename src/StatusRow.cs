using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TigEditor
{
    // The status row is drawn whole, with no child labels. A changed value does
    // not redo the layout or touch its neighbours: only the piece holding the
    // value is repainted, so it can be updated as often as wanted.
    public class StatusRow : Control
    {
        private const int Gap = 3;        // the distance between values
        private const int Edge = 8;       // the margin from the row edges
        private const int GripSize = 14;  // the resize grip

        private string time = "";         // pointer time and duration
        private string level = "";        // the level name
        private string track = "";        // track time and its duration
        private string size = "";         // length and height under the pointer
        private string zoom = "";
        private string count = "";
        private Color countColor = SystemColors.ControlText;

        // A music button: an icon, a tip and a pressed flag.
        public class Btn
        {
            public IconKind Icon;
            public string Tip;
            public bool Visible = true;
            public bool Checked;
            public object Tag;
            internal Rectangle Area;
        }

        private readonly System.Collections.Generic.List<Btn> buttons =
            new System.Collections.Generic.List<Btn>();
        private int hover = -1;
        private bool shortSize;   // the short form of the label is shown

        public event Action<object> ButtonClick;
        private Rectangle timeRect, levelRect, trackRect, sizeRect, zoomRect, countRect;

        public StatusRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Bottom;
        }

        // Music buttons sit between the level name and the track time.
        public void SetButtons(params Btn[] items)
        {
            buttons.Clear();
            buttons.AddRange(items);
            Relayout();
        }

        public void UpdateButtons() { Relayout(); }

        // Buttons take almost the whole row height without touching its edges.
        private int ButtonSize { get { return Math.Max(12, Height - 4); } }
        private int ButtonWidth { get { return ButtonSize + 4; } }

        public string TimeText { get { return time; } set { Set(ref time, value, timeRect); } }
        public string LevelText { get { return level; } set { Set(ref level, value, levelRect); } }
        public string TrackText { get { return track; } set { Set(ref track, value, trackRect); } }
        public string SizeText { get { return size; } set { Set(ref size, value, sizeRect); } }

        // A short stand in for when the full label does not fit.
        public string SizeShort
        {
            get { return sizeShort; }
            set
            {
                if (sizeShort == value) return;
                bool sameWidth = TextWidth(sizeShort) == TextWidth(value);
                sizeShort = value;
                if (shortSize && !sameWidth) Relayout();
                else if (shortSize) Invalidate(sizeRect);
            }
        }

        private string sizeShort = "";
        public string ZoomText { get { return zoom; } set { Set(ref zoom, value, zoomRect); } }
        public string CountText { get { return count; } set { Set(ref count, value, countRect); } }

        public Color CountColor
        {
            get { return countColor; }
            set { if (countColor != value) { countColor = value; Invalidate(countRect); } }
        }

        // Values change often, so where possible only their own piece is
        // repainted. Positions are redone when a label changed width and the
        // neighbours have to move.
        private void Set(ref string field, string value, Rectangle area)
        {
            if (value == null) value = "";
            if (field == value) return;
            bool sameWidth = TextWidth(field) == TextWidth(value);
            field = value;
            if (sameWidth) Invalidate(area);
            else Relayout();
        }

        private readonly System.Collections.Generic.Dictionary<string, int> widths =
            new System.Collections.Generic.Dictionary<string, int>();

        // The width of a given label is worked out once: updates are frequent
        // and the strings repeat.
        private int TextWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w;
            if (widths.TryGetValue(s, out w)) return w;
            w = TextRenderer.MeasureText(s, Font, new Size(int.MaxValue, Height),
                                         TextFormatFlags.NoPadding).Width;
            if (widths.Count > 512) widths.Clear();
            widths[s] = w;
            return w;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            widths.Clear();
            Relayout();
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Relayout(); }

        // Positions are worked out on a resize or a width change, not on every
        // number. When room runs short we give up in order: the middle goes
        // first, then the level name is squeezed.
        private void Relayout()
        {
            int h = Height;
            // The grip takes its place and the counter stands right against it:
            // a margin here only ate width for nothing.
            int gripRoom = GripVisible ? GripSize : Edge;

            int timeW = TextWidth(time);
            int levelW = TextWidth(level);
            int playW = 0;
            foreach (Btn b in buttons) if (b.Visible) playW += ButtonWidth;
            int zoomW = TextWidth(zoom);
            int countW = TextWidth(count);
            // The right part is pinned to the grip.
            int rightEnd = Width - gripRoom;
            countRect = new Rectangle(rightEnd - countW, 0, countW, h);
            zoomRect = new Rectangle(countRect.Left - Gap - zoomW, 0, zoomW, h);

            // The left part starts at the edge.
            int x = Edge;
            timeRect = new Rectangle(x, 0, timeW, h);
            x += timeW + (timeW > 0 ? Gap : 0);

            // The level name takes whatever is left before the music buttons, the
            // track time and the right part. If it does not fit whole it is not
            // shown at all: a fragment only confuses.
            int leftLimit = zoomRect.Left - Gap - playW - (playW > 0 ? Gap : 0) - TextWidth(track);
            int room = Math.Max(0, leftLimit - x);
            levelRect = levelW <= room ? new Rectangle(x, 0, levelW, h) : Rectangle.Empty;
            x += levelRect.Width + (levelRect.Width > 0 ? Gap : 0);

            int by = Math.Max(0, (h - ButtonSize) / 2);
            foreach (Btn b in buttons)
            {
                if (!b.Visible) { b.Area = Rectangle.Empty; continue; }
                b.Area = new Rectangle(x, by, ButtonWidth, ButtonSize);
                x += ButtonWidth;
            }
            if (playW > 0) x += Gap * 2;   // the timer does not stick to the last button

            int trackW = TextWidth(track);
            int trackRoom = Math.Max(0, zoomRect.Left - Gap - x);
            trackRect = new Rectangle(x, 0, Math.Min(trackW, trackRoom), h);
            x += trackRect.Width + (trackRect.Width > 0 ? Gap : 0);

            // The middle is placed last and centres on what is left between the
            // left and right parts, not on the window. That way it survives a
            // narrow row and still fills the empty space.
            int free = zoomRect.Left - Gap - x;
            shortSize = false;
            int sizeW = TextWidth(size);
            if (sizeW > 0 && free < sizeW && !string.IsNullOrEmpty(SizeShort))
            {
                int shortW = TextWidth(SizeShort);
                if (shortW > 0 && free >= shortW) { shortSize = true; sizeW = shortW; }
            }
            sizeRect = sizeW > 0 && free >= sizeW
                     ? new Rectangle(x + (free - sizeW) / 2, 0, sizeW, h)
                     : Rectangle.Empty;

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Draw(e.Graphics, time, timeRect, ForeColor);
            Draw(e.Graphics, level, levelRect, ForeColor);
            Draw(e.Graphics, track, trackRect, ForeColor);
            Draw(e.Graphics, shortSize ? SizeShort : size, sizeRect, ForeColor);
            Draw(e.Graphics, zoom, zoomRect, ForeColor);
            Draw(e.Graphics, count, countRect, countColor);
            DrawButtons(e.Graphics);
            DrawGrip(e.Graphics);
        }

        // Buttons are drawn like the tool strip: a highlight under the pointer
        // and a pressed look for the ones that are on.
        private void DrawButtons(Graphics g)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                Btn b = buttons[i];
                if (!b.Visible || b.Area.Width == 0) continue;

                bool lit = i == hover;
                if (lit || b.Checked)
                {
                    Color fill = b.Checked ? Color.FromArgb(205, 226, 246)
                                           : Color.FromArgb(229, 241, 251);
                    using (Brush br = new SolidBrush(fill))
                    using (Pen pen = new Pen(Color.FromArgb(153, 209, 255)))
                    {
                        Rectangle r = b.Area;
                        r.Width -= 1; r.Height -= 1;
                        g.FillRectangle(br, r);
                        g.DrawRectangle(pen, r);
                    }
                }

                // Icons live in a shared cache and must not be freed.
                Image icon = Icons.Make(b.Icon, Math.Max(8, ButtonSize - 3));
                g.DrawImage(icon, b.Area.Left + (b.Area.Width - icon.Width) / 2,
                            b.Area.Top + (b.Area.Height - icon.Height) / 2);
            }
        }

        // A label is always one line. If it does not fit it is cut on the left,
        // so the right part, where the important bit usually is, stays visible.
        private void Draw(Graphics g, string text, Rectangle area, Color color)
        {
            if (string.IsNullOrEmpty(text) || area.Width <= 0) return;
            int want = TextWidth(text);
            Rectangle at = area;
            if (want > area.Width) at = new Rectangle(area.Right - want, 0, want, area.Height);

            Region old = g.Clip;
            g.SetClip(area);
            TextRenderer.DrawText(g, text, Font, at, color,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            g.Clip = old;
        }

        // A maximised window cannot be dragged, so there is no grip.
        private bool GripVisible
        {
            get
            {
                Form form = FindForm();
                return form == null || form.WindowState == FormWindowState.Normal;
            }
        }

        // The grip in the bottom right corner hints that the window can be resized.
        private void DrawGrip(Graphics g)
        {
            if (!GripVisible) return;
            int x0 = Width - GripSize - 2, y0 = Height - GripSize - 1;
            using (Brush dark = new SolidBrush(Color.FromArgb(120, 128, 136)))
            using (Brush light = new SolidBrush(Color.FromArgb(250, 250, 250)))
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col <= row; col++)
                    {
                        int x = x0 + GripSize - 4 - col * 4;
                        int y = y0 + GripSize - 4 - (2 - row) * 4;
                        g.FillRectangle(light, x + 1, y + 1, 2, 2);
                        g.FillRectangle(dark, x, y, 2, 2);
                    }
        }

        // The wheel over the zoom changes it, so the editor needs to know where it is.
        public bool ZoomHit(Point clientPoint)
        {
            return zoomRect.Contains(clientPoint);
        }

        // The full level folder path shows as a tip on the level name.
        public string LevelHint;

        // A double click on the zoom puts back the default value.
        public event EventHandler ZoomReset;

        // A double click on the counter shows what it is made of.
        public event EventHandler CountDetails;

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            Point at = PointToClient(Cursor.Position);
            if (zoomRect.Contains(at) && ZoomReset != null) ZoomReset(this, EventArgs.Empty);
            else if (countRect.Contains(at) && CountDetails != null)
            {
                Tips.Hide();
                CountDetails(this, EventArgs.Empty);
            }
        }

        private Rectangle GripArea
        {
            get
            {
                return GripVisible ? new Rectangle(Width - GripSize - 4, 0, GripSize + 4, Height)
                                   : Rectangle.Empty;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = GripArea.Contains(e.Location) ? Cursors.SizeNWSE : Cursors.Default;

            int was = hover;
            hover = -1;
            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Visible && buttons[i].Area.Contains(e.Location)) hover = i;
            if (hover != was)
            {
                Invalidate();
                if (hover >= 0) Tips.Track(this, buttons[hover].Tip,
                                           new Point(buttons[hover].Area.Left, buttons[hover].Area.Top));
                else Tips.Hide();
            }
            if (hover >= 0) { }
            else if (levelRect.Contains(e.Location) && !string.IsNullOrEmpty(LevelHint))
                Tips.Track(this, LevelHint, new Point(levelRect.Left, 0));
            else Tips.Hide();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover >= 0) { hover = -1; Invalidate(); }
            Tips.Hide();
        }

        // Dragging the grip must resize the window, same as dragging its edge.
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
                foreach (Btn b in buttons)
                    if (b.Visible && b.Area.Contains(e.Location))
                    {
                        Tips.Hide();
                        if (ButtonClick != null) ButtonClick(b.Tag);
                        return;
                    }

            if (e.Button != MouseButtons.Left || !GripArea.Contains(e.Location)) return;
            Form form = FindForm();
            if (form == null || form.WindowState != FormWindowState.Normal) return;
            ReleaseCapture();
            SendMessage(form.Handle, WmNcLButtonDown, (IntPtr)HtBottomRight, IntPtr.Zero);
        }

        private const int WmNcLButtonDown = 0x00A1;
        private const int HtBottomRight = 17;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}

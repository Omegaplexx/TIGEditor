using System;
using System.Drawing;
using System.Windows.Forms;

namespace TigEditor
{
    // We show tips ourselves because the system one sits right at the pointer
    // and swallows the click: while it hangs there the button under it cannot
    // be pressed. The look is the system tooltip, only the place is ours,
    // above whatever the tip belongs to.
    public static class Tips
    {
        private static readonly ToolTip tip = new ToolTip();
        private static readonly Timer wait = new Timer();

        private static Control owner;
        private static string text;
        private static Point above;      // the point the tip will stand above
        private static bool visible;

        static Tips()
        {
            tip.ShowAlways = true;
            wait.Interval = Math.Max(300, SystemInformation.MouseHoverTime);
            wait.Tick += delegate
            {
                wait.Stop();
                if (owner == null || !owner.IsHandleCreated) return;
                int h = TextRenderer.MeasureText(text, SystemFonts.DefaultFont).Height + 8;
                tip.Show(text, owner, above.X, above.Y - h - 4);
                visible = true;
            };
        }

        // Start watching a spot: the tip shows up after the usual delay. The point
        // is in control coordinates and means the top of what to stand above.
        public static void Track(Control control, string what, Point topLeft)
        {
            if (control == null || string.IsNullOrEmpty(what)) { Hide(); return; }
            if (visible && owner == control && text == what) return;
            if (owner == control && text == what && wait.Enabled) return;

            Hide();
            owner = control;
            text = what;
            above = topLeft;
            wait.Stop();
            wait.Start();
        }

        public static void Hide()
        {
            wait.Stop();
            if (visible && owner != null && owner.IsHandleCreated) tip.Hide(owner);
            visible = false;
            owner = null;
            text = null;
        }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TigEditor
{
    // The game knows nothing about Windows interface scaling and works out the
    // screen centre by its own measure, so above 100% its window drifts down
    // and to the right. The editor does know, so it centres the window itself
    // in real pixels.
    public static class GameWindow
    {
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter,
            int x, int y, int cx, int cy, uint flags);

        // The window does not appear at once and may resize after it does, so we
        // watch it for a few seconds and fix it on every change.
        public static void CenterWhenReady(Process game, int seconds)
        {
            if (game == null) return;

            Timer timer = new Timer();
            timer.Interval = 150;
            int ticks = 0, limit = Math.Max(1, seconds * 1000 / timer.Interval);
            Size lastSize = Size.Empty;

            timer.Tick += delegate
            {
                ticks++;
                bool done = ticks >= limit;
                try
                {
                    if (game.HasExited) done = true;
                    else
                    {
                        game.Refresh();
                        IntPtr hwnd = game.MainWindowHandle;
                        Rect r;
                        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out r))
                        {
                            Size size = new Size(r.Right - r.Left, r.Bottom - r.Top);
                            if (size.Width > 0 && size.Height > 0 && size != lastSize)
                            {
                                lastSize = size;
                                Center(hwnd, size);
                            }
                        }
                    }
                }
                catch { done = true; }

                if (done)
                {
                    timer.Stop();
                    timer.Dispose();
                }
            };
            timer.Start();
        }

        private static void Center(IntPtr hwnd, Size size)
        {
            Rectangle area = Screen.FromHandle(hwnd).WorkingArea;
            int x = area.Left + (area.Width - size.Width) / 2;
            int y = area.Top + (area.Height - size.Height) / 2;
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }
    }
}

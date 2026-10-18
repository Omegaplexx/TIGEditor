using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TigEditor
{
    // A small screen about the program.
    public class AboutDialog : Form
    {
        private const string Repo = "https://github.com/Omegaplexx/TIGEditor";

        public AboutDialog()
        {
            Text = S.AboutTitle;
            if (SystemFonts.MessageBoxFont != null) Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16, 14, 16, 12);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.ColumnCount = 2;
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            PictureBox icon = new PictureBox();
            icon.Image = AppIcon(48);
            icon.Size = new Size(48, 48);
            icon.SizeMode = PictureBoxSizeMode.CenterImage;
            icon.Margin = new Padding(0, 0, 16, 0);
            grid.Controls.Add(icon, 0, 0);
            grid.SetRowSpan(icon, 4);

            Label name = new Label();
            name.Text = MainForm.AppName;
            name.Font = new Font(Font.FontFamily, Font.Size + 3, FontStyle.Bold);
            name.AutoSize = true;
            name.Margin = new Padding(0, 0, 0, 2);
            grid.Controls.Add(name, 1, 0);

            grid.Controls.Add(Line(S.AboutVersion), 1, 1);
            grid.Controls.Add(Line(S.AboutAuthor), 1, 2);

            LinkLabel link = new LinkLabel();
            link.Text = Repo;
            link.AutoSize = true;
            link.Margin = new Padding(0, 2, 0, 0);
            link.LinkClicked += delegate
            {
                try { Process.Start(Repo); } catch { }
            };
            grid.Controls.Add(link, 1, 3);

            Label about = new Label();
            about.Text = S.AboutSummary;
            about.AutoSize = true;
            about.MaximumSize = new Size(340, 0);
            about.Margin = new Padding(0, 12, 0, 0);
            grid.Controls.Add(about, 0, 4);
            grid.SetColumnSpan(about, 2);

            Button close = Wide(S.BtnClose);
            close.DialogResult = DialogResult.OK;
            close.Anchor = AnchorStyles.Right;
            close.Margin = new Padding(0, 12, 0, 0);
            grid.Controls.Add(close, 0, 5);
            grid.SetColumnSpan(close, 2);

            Controls.Add(grid);
            AcceptButton = close;
            CancelButton = close;
        }

        // A button shaped like the ones in the other dialogs.
        private static Button Wide(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(SystemInformation.HorizontalScrollBarHeight * 4,
                                     SystemInformation.HorizontalScrollBarHeight + 9);
            b.Margin = new Padding(6, 0, 0, 0);
            return b;
        }

        // The window icon scaled to the size we need.
        private static Image AppIcon(int size)
        {
            try
            {
                Icon ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ico != null) return new Icon(ico, size, size).ToBitmap();
            }
            catch { }
            return new Bitmap(size, size);
        }

        private static Label Line(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Margin = new Padding(0, 1, 0, 1);
            return l;
        }
    }
}

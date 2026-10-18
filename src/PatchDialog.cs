using System;
using System.Drawing;
using System.Windows.Forms;

namespace TigEditor
{
    // The features of a patched game. Every item is independent and everything
    // goes into the game file itself, so it works without the editor.
    //
    // The layout is worked out by hand, without nested auto sizing panels: those
    // redo themselves every time the window is shown, and with seven items of
    // wrapped text that took about half a second.
    public class PatchDialog : Form
    {
        private const int ColumnWidth = 390;   // width of the description column
        private const int Pad = 12;            // window margins
        private const int ColumnGap = 24;

        private readonly string exePath;


        private readonly CheckBox extras = new CheckBox();
        private readonly CheckBox noClipKey = new CheckBox();
        private readonly CheckBox medalFix = new CheckBox();
        private readonly CheckBox folder = new CheckBox();
        private readonly CheckBox musicSync = new CheckBox();
        private readonly CheckBox dpiAware = new CheckBox();
        private readonly NumericUpDown holdExtra = new NumericUpDown();
        private readonly Button restore = new Button();
        private Label holdCaption;

        public PatchDialog(string exePath)
        {
            this.exePath = exePath;

            Text = S.PatchTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            if (SystemFonts.MessageBoxFont != null) Font = SystemFonts.MessageBoxFont;

            holdExtra.Minimum = 0;
            holdExtra.Maximum = 500;
            holdExtra.Increment = 10;
            holdExtra.Value = 150;
            holdExtra.Width = 70;

            Label intro = Para(
                S.PatchIntro,
                ColumnWidth * 2 + ColumnGap, SystemColors.ControlText);
            intro.Location = new Point(Pad, Pad);
            Controls.Add(intro);

            int top = intro.Bottom + 14;

            // Items follow one another and move into the second column once the
            // first has taken half of the total height.
            Item[] items =
            {
                new Item(noClipKey, S.MenuNoClip,
                    S.PatchNoClipText),
                new Item(extras, S.PatchResourcesName,
                    S.PatchResourcesText),
                new Item(medalFix, S.PatchMedalName,
                    S.PatchMedalText),
                new Item(folder, S.PatchFolderName,
                    S.PatchFolderText),
                new Item(musicSync, S.PatchSyncName,
                    S.PatchSyncText),
                new Item(dpiAware, S.PatchDpiName,
                    S.PatchDpiText),
            };

            foreach (Item item in items) item.Measure(this, ColumnWidth);
            items[4].Extra = holdExtra;   // the hold sits under the sync item

            int total = 0;
            foreach (Item item in items) total += item.Height + 10;

            int y = top, filled = 0, column = 0;
            foreach (Item item in items)
            {
                int own = item.Height + 10;
                if (column == 0 && filled > 0 && filled + own / 2 > total / 2)
                {
                    column = 1;
                    y = top;
                }
                item.Place(this, Pad + column * (ColumnWidth + ColumnGap), y);
                y += own;
                if (column == 0) filled += own;
                else if (item.Extra != null) y += item.Extra.Height + 8;
            }

            holdCaption = items[4].Caption;

            int listBottom = 0;
            foreach (Item item in items) listBottom = Math.Max(listBottom, item.Bottom);

            Button close = MakeButton(S.BtnClose, 96);
            close.DialogResult = DialogResult.Cancel;

            Button apply = MakeButton(S.BtnApply, 96);
            apply.Click += delegate { DoApply(); };

            restore.Text = S.BtnRestore;
            SizeButton(restore, 170);
            restore.Click += delegate { DoRestore(); };

            int width = Pad * 2 + ColumnWidth * 2 + ColumnGap;
            int buttonsTop = listBottom + 14;
            // The system gap between buttons, not edges glued together.
            int gap = SystemInformation.MenuCheckSize.Width * 2 / 3;
            close.Location = new Point(width - Pad - close.Width, buttonsTop);
            apply.Location = new Point(close.Left - gap - apply.Width, buttonsTop);
            restore.Location = new Point(apply.Left - gap - restore.Width, buttonsTop);
            Controls.Add(close);
            Controls.Add(apply);
            Controls.Add(restore);

            ClientSize = new Size(width, buttonsTop + close.Height + Pad);
            CancelButton = close;
            ShowState();
        }

        // One item: a check box, a description under it and, if needed, a field.
        private class Item
        {
            private readonly CheckBox box;
            private readonly Label note;
            public Control Extra;
            public Label Caption;
            public int Height;
            public int Bottom;

            public Item(CheckBox box, string title, string text)
            {
                this.box = box;
                box.Text = title;
                note = new Label();
                note.Text = text;
                note.ForeColor = SystemColors.GrayText;
            }

            public void Measure(Control owner, int width)
            {
                // Windows works out the size, we only give the width it has to
                // fit into.
                box.AutoSize = false;
                box.Size = new Size(width, TextRenderer.MeasureText(box.Text, owner.Font,
                    new Size(width - 20, 0), TextFormatFlags.WordBreak).Height + 6);

                note.AutoSize = false;
                note.Size = new Size(width - Indent, TextRenderer.MeasureText(note.Text, owner.Font,
                    new Size(width - Indent, 0), TextFormatFlags.WordBreak).Height + 2);

                Height = box.Height + note.Height + 2;
            }

            // The description lines up with the check box text, not the box.
            private static int Indent { get { return SystemInformation.MenuCheckSize.Width + 4; } }

            public void Place(Control owner, int x, int y)
            {
                box.Location = new Point(x, y);
                note.Location = new Point(x + Indent, box.Bottom + 2);
                owner.Controls.Add(box);
                owner.Controls.Add(note);
                Bottom = note.Bottom;

                if (Extra != null)
                {
                    Label caption = new Label();
                    caption.Text = S.PatchHoldLabel;
                    caption.AutoSize = false;
                    caption.Size = TextRenderer.MeasureText(caption.Text, owner.Font);
                    caption.Location = new Point(x + Indent, note.Bottom + 10);
                    Caption = caption;
                    Extra.Location = new Point(caption.Right + 8, note.Bottom + 7);
                    owner.Controls.Add(caption);
                    owner.Controls.Add(Extra);
                    Bottom = Math.Max(Extra.Bottom, caption.Bottom);
                }
            }
        }

        // A button as wide as its text, but no narrower than usual.
        private Button MakeButton(string text, int least)
        {
            Button b = new Button();
            b.Text = text;
            SizeButton(b, least);
            return b;
        }

        private void SizeButton(Button b, int least)
        {
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(least, 26);
            b.Size = b.PreferredSize;
        }

        private Label Para(string text, int width, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = color;
            label.AutoSize = true;
            label.MaximumSize = new Size(width, 0);
            label.Size = label.PreferredSize;
            return label;
        }

        private void ShowState()
        {
            GamePatcher.State state = GamePatcher.Read(exePath);
            if (!state.Valid)
            {
                MessageBox.Show(this, S.ErrReadGame + state.Error,
                                Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                noClipKey.Enabled = extras.Enabled = medalFix.Enabled = false;
                folder.Enabled = musicSync.Enabled = holdExtra.Enabled = false;
                dpiAware.Enabled = restore.Enabled = false;
                return;
            }

            // What is applied cannot be undone one by one, that is what restoring
            // the original file is for. Such items go gray while everything else
            // is offered to be turned on.
            Mark(noClipKey, state.NoClipKey);
            Mark(extras, state.LevelExtras);
            Mark(medalFix, state.MedalFix);
            Mark(folder, state.RememberFolder);
            musicSync.Checked = true;
            musicSync.Enabled = !state.MusicSync;
            holdExtra.Enabled = !state.MusicSync;
            if (holdCaption != null) holdCaption.Enabled = holdExtra.Enabled;
            if (state.HoldMs > 0) holdExtra.Value = Math.Min(holdExtra.Maximum, state.HoldMs);
            Mark(dpiAware, state.DpiAware);
            restore.Enabled = state.HasBackup;

        }

        // An item for investigation: it never turns itself on.

        // A label and a field side by side, indented: a setting of the item above.
        private Control Indent(Control field, string caption)
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.FlowDirection = FlowDirection.LeftToRight;
            row.WrapContents = false;
            row.Margin = new Padding(24, 0, 0, 8);

            Label text = new Label();
            text.Text = caption;
            text.AutoSize = true;
            text.Margin = new Padding(0, 5, 8, 0);

            row.Controls.Add(text);
            row.Controls.Add(field);
            return row;
        }


        private static void Mark(CheckBox box, bool applied)
        {
            box.Checked = true;
            box.Enabled = !applied;
        }


        private void DoApply()
        {
            // A gray item means already applied, and those stay checked too.
            GamePatcher.State wanted = new GamePatcher.State();
            wanted.NoClipKey = noClipKey.Checked;
            wanted.LevelExtras = extras.Checked;
            wanted.MedalFix = medalFix.Checked;
            wanted.RememberFolder = folder.Checked;
            wanted.MusicSync = musicSync.Checked;
            wanted.HoldMs = (int)holdExtra.Value;
            wanted.DpiAware = dpiAware.Checked;

            string error;
            if (!GamePatcher.Apply(exePath, wanted, out error))
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ShowState();
        }

        private void DoRestore()
        {
            string error;
            if (!GamePatcher.Restore(exePath, out error))
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ShowState();
        }
    }
}

using System;
using System.Drawing;
using System.Windows.Forms;

namespace TigEditor
{
    // What the object counter is made of. The layout is explicit: names start
    // at the left margin, numbers end at the right one and share a width, so
    // they do not jump from row to row.
    public class CountsDialog : Form
    {
        public CountsDialog(Level level)
        {
            Text = S.CountsTitle;
            if (SystemFonts.MessageBoxFont != null) Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            int blocks = 0, spikes = 0;
            foreach (LevelObj o in level.Cells.Values)
            {
                if (o.Type == ObjType.Block) blocks++;
                else if (o.Type == ObjType.Spike) spikes++;
            }
            int total = blocks + spikes + level.Pits.Count;

            int pad = 12;           // one margin on every side
            int line = TextRenderer.MeasureText(S.MeasureSample, Font).Height;
            int row = line + 7;
            int width = 210;                       // a narrow window, as it was
            int right = pad + width;
            int numW = TextRenderer.MeasureText("00000", Font).Width;

            int y = pad;
            y = Row(S.CountsBlocks, blocks, pad, y, row, right, numW);
            y = Row(S.CountsSpikes, spikes, pad, y, row, right, numW);
            y = Row(S.CountsPits, level.Pits.Count, pad, y, row, right, numW);
            y = Row(S.CountsTotal, total, pad, y, row, right, numW);

            // The rule separates what counts against the game limit
            // from what does not.
            Label rule = new Label();
            rule.BorderStyle = BorderStyle.Fixed3D;
            rule.AutoSize = false;
            rule.SetBounds(pad, y + 6, width, 2);
            Controls.Add(rule);
            y += 6 + 2 + 10;

            y = Row(S.CountsBackgrounds, level.Backgrounds.Count, pad, y, row, right, numW);
            y = Row(S.CountsGravity, level.GravityChanges.Count, pad, y, row, right, numW);
            y = Row(S.CountsFallStripes, level.BlocksFall.Count, pad, y, row, right, numW);
            y = Row(S.CountsRiseStripes, level.BlocksRise.Count, pad, y, row, right, numW);

            Button close = new Button();
            close.Text = S.BtnClose;
            close.AutoSize = false;
            close.Size = new Size(SystemInformation.HorizontalScrollBarHeight * 4,
                                  SystemInformation.HorizontalScrollBarHeight + 9);
            close.Location = new Point(right - close.Width, y + 10);
            close.DialogResult = DialogResult.OK;
            Controls.Add(close);

            ClientSize = new Size(right + pad, close.Bottom + pad);
            AcceptButton = close;
            CancelButton = close;
        }

        private int Row(string name, int value, int x, int y, int row, int right, int numW)
        {
            Label left = new Label();
            left.Text = name;
            left.AutoSize = false;
            left.SetBounds(x, y, right - x - numW - 12, row);
            left.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(left);

            Label num = new Label();
            num.Text = value.ToString();
            num.AutoSize = false;
            num.SetBounds(right - numW, y, numW, row);
            num.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(num);

            return y + row;
        }
    }
}

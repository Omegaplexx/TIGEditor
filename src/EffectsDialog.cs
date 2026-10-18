using System;
using System.Drawing;
using System.Windows.Forms;

namespace TigEditor
{
    // Details about effect stripes. A full width summary on top, the two
    // effects in columns below: their rules are independent, and side by side
    // they are easier to compare than one under the other.
    public class EffectsDialog : Form
    {
        private const int Col = 430;     // column width
        private const int Gap = 26;      // gap between the columns
        private const int Pad = 12;

        // The blank line between paragraphs. The strings do not carry it, so
        // each one stays whole and wraps by words.
        private const string Break = "\r\n\r\n";

        public EffectsDialog()
        {
            Text = S.EffectsTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            if (SystemFonts.MessageBoxFont != null) Font = SystemFonts.MessageBoxFont;

            int width = Pad * 2 + Col * 2 + Gap;

            Label intro = Para(
                S.EffectsIntro,
                Pad, Pad, width - Pad * 2);

            int top = intro.Bottom + 16;

            Label leftHead = Head(S.EffectsFallHead, Pad, top);
            Label left = Para(
                S.EffectsFallText + Break +
                S.EffectsFallWhy + Break +
                S.EffectsFallExample,
                Pad, leftHead.Bottom + 6, Col);

            int rightX = Pad + Col + Gap;
            Label rightHead = Head(S.EffectsRiseHead, rightX, top);
            Label right = Para(
                S.EffectsRiseLength + Break +
                S.EffectsRiseRules + Break +
                S.EffectsRiseExample,
                rightX, rightHead.Bottom + 6, Col);

            Controls.Add(intro);
            Controls.Add(leftHead);
            Controls.Add(left);
            Controls.Add(rightHead);
            Controls.Add(right);

            Button close = new Button();
            close.Text = S.BtnClose;
            close.AutoSize = false;
            close.Size = new Size(SystemInformation.HorizontalScrollBarHeight * 4,
                                  SystemInformation.HorizontalScrollBarHeight + 9);
            close.DialogResult = DialogResult.Cancel;
            close.Location = new Point(width - Pad - close.Width,
                                       Math.Max(left.Bottom, right.Bottom) + 16);
            Controls.Add(close);

            ClientSize = new Size(width, close.Bottom + Pad);
            CancelButton = close;
            AcceptButton = close;
        }

        private Label Head(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = new Font(Font, FontStyle.Bold);
            l.AutoSize = true;
            l.Location = new Point(x, y);
            return l;
        }

        // We measure the height ourselves: an auto sized label reports less
        // than the real height and the text gets cut in the middle.
        private Label Para(string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Location = new Point(x, y);
            l.Size = new Size(w, TextRenderer.MeasureText(text, Font,
                new Size(w, int.MaxValue), TextFormatFlags.WordBreak).Height + 4);
            return l;
        }
    }
}

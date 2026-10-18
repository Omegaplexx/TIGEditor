using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TigEditor
{
    // The layout is explicit instead of nested auto sizing panels. Those used
    // to argue with each other, one growing by content and the other by space,
    // and the controls drifted apart. Here every position comes from one margin
    // and the font height.
    public class SettingsDialog : Form
    {
        private readonly CheckBox chkInvertScroll = new CheckBox();
        private readonly CheckBox chkInvertZoom = new CheckBox();
        private readonly CheckBox chkInvertDraw = new CheckBox();
        private readonly CheckBox chkBlack = new CheckBox();
        private readonly TextBox txtGame = new TextBox();
        private readonly ComboBox cbLang = new ComboBox();
        private readonly string[] langCodes;

        public bool InvertScroll { get { return chkInvertScroll.Checked; } }
        public bool InvertZoom { get { return chkInvertZoom.Checked; } }
        public bool InvertDrawMode { get { return chkInvertDraw.Checked; } }
        public bool PureBlack { get { return chkBlack.Checked; } }
        public string GamePath { get { return txtGame.Text.Trim(); } }

        // An empty string means "same as the system", so the choice survives
        // a move to a machine with another language.
        public string Language
        {
            get
            {
                int i = cbLang.SelectedIndex;
                return i <= 0 ? "" : langCodes[i];
            }
        }

        public SettingsDialog(bool invertScroll, bool invertZoom, bool invertDraw,
                              bool pureBlack, string gamePath, string language)
        {
            Text = S.SettingsTitle;
            if (SystemFonts.MessageBoxFont != null) Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            int pad = 14;                                   // the margin around the content
            int line = TextRenderer.MeasureText(S.MeasureSample, Font).Height;
            int row = line + 6;                             // the step between rows
            int gap = line;                                 // the gap between blocks
            int indent = SystemInformation.MenuCheckSize.Width;
            int width = 373;                                // width of the working area
            int right = pad + width;

            int y = pad;

            // Language comes first: it decides what language the rest is read in.
            Label langLabel = Label(S.SettingsLanguage, pad, y);
            Controls.Add(langLabel);

            string[] codes = S.Available();
            langCodes = new string[codes.Length + 1];
            cbLang.DropDownStyle = ComboBoxStyle.DropDownList;
            cbLang.Items.Add(S.SettingsLanguageSystem);
            langCodes[0] = "";
            for (int i = 0; i < codes.Length; i++)
            {
                cbLang.Items.Add(S.LanguageName(codes[i]));
                langCodes[i + 1] = codes[i];
            }
            cbLang.SelectedIndex = 0;
            for (int i = 1; i < langCodes.Length; i++)
                if (langCodes[i] == language) cbLang.SelectedIndex = i;

            // Width by the longest name, so there is no empty tail.
            int widest = 0;
            foreach (object item in cbLang.Items)
                widest = Math.Max(widest,
                    TextRenderer.MeasureText(item.ToString(), Font).Width);
            cbLang.SetBounds(pad + langLabel.PreferredWidth + 8, y,
                             widest + SystemInformation.VerticalScrollBarWidth + 12, row);
            Controls.Add(cbLang);
            y += Math.Max(row, cbLang.Height) + gap - 4;

            Controls.Add(Label(S.SettingsInvert, pad, y));
            y += row;

            y = Add(Check(chkInvertScroll, S.SettingsInvertScroll, invertScroll),
                    pad + indent, y, row);
            y = Add(Check(chkInvertZoom, S.SettingsInvertZoom, invertZoom),
                    pad + indent, y, row);
            y = Add(Check(chkInvertDraw, S.SettingsInvertDraw, invertDraw),
                    pad + indent, y, row);

            y += 6;                                         // a setting of its own, not a list item
            y = Add(Check(chkBlack, S.SettingsBlack, pureBlack), pad, y, row);

            Controls.Add(Label(S.SettingsGamePath, pad, y));
            y += row;

            Button browse = Wide(S.BtnBrowse);
            txtGame.Text = gamePath ?? "";
            txtGame.Left = pad;
            txtGame.Top = y;
            txtGame.Width = width - browse.Width - 8;
            Controls.Add(txtGame);

            browse.Left = right - browse.Width;
            browseButton = browse;
            browse.Click += delegate
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Filter = S.GameFilter;
                    if (File.Exists(txtGame.Text))
                        dlg.InitialDirectory = Path.GetDirectoryName(txtGame.Text);
                    if (dlg.ShowDialog(this) == DialogResult.OK) txtGame.Text = dlg.FileName;
                }
            };
            Controls.Add(browse);
            y += txtGame.Height + gap + 4;

            Button about = Wide(S.AboutTitle);
            Button cancel = Wide(S.BtnCancel);
            Button ok = Wide(S.BtnOk);
            cancel.DialogResult = DialogResult.Cancel;
            ok.DialogResult = DialogResult.OK;
            about.Click += delegate
            {
                using (AboutDialog dlg = new AboutDialog()) dlg.ShowDialog(this);
            };

            about.Left = pad;
            cancel.Left = right - cancel.Width;
            ok.Left = cancel.Left - ok.Width - 8;
            about.Top = cancel.Top = ok.Top = y;
            Controls.Add(about);
            Controls.Add(ok);
            Controls.Add(cancel);
            y += cancel.Height + pad;

            ClientSize = new Size(right + pad, y);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private Button browseButton;

        // The button takes its height and top from the field only after
        // layout: before that the field does not know its final height.
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (browseButton != null)
            {
                browseButton.Height = txtGame.Height;
                browseButton.Top = txtGame.Top;
            }
        }

        private static Label Label(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Left = x;
            l.Top = y;
            return l;
        }

        private static CheckBox Check(CheckBox box, string text, bool value)
        {
            box.Text = text;
            box.Checked = value;
            box.AutoSize = true;
            return box;
        }

        private int Add(Control c, int x, int y, int row)
        {
            c.Left = x;
            c.Top = y;
            Controls.Add(c);
            return y + row;
        }

        // Buttons share one height and are no narrower than usual: the height
        // comes from the system scroll bar metric.
        private Button Wide(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = false;
            int h = SystemInformation.HorizontalScrollBarHeight + 9;
            int w = TextRenderer.MeasureText(text, Font).Width + 24;
            b.Size = new Size(Math.Max(SystemInformation.HorizontalScrollBarHeight * 4, w), h);
            return b;
        }
    }
}

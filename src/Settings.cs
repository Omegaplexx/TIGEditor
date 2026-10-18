using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TigEditor
{
    // Settings live in an ini next to the exe. If the folder is read only they
    // simply are not saved: the program is fully portable and never writes to
    // the user profile or the registry.
    public class Settings
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>();
        private readonly string path;

        public Settings()
        {
            path = ChoosePath();
            Load();
        }

        private static string ChoosePath()
        {
            try
            {
                // The settings file is named after the editor itself, so a
                // renamed exe gets an ini with the matching name.
                string exe = Application.ExecutablePath;
                return Path.Combine(Path.GetDirectoryName(exe),
                                    Path.GetFileNameWithoutExtension(exe) + ".ini");
            }
            catch
            {
                return null;   // a read only folder, no settings are kept
            }
        }

        private void Load()
        {
            try
            {
                if (path == null || !File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch { }
        }

        public void Save()
        {
            if (path == null) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, string> kv in values)
                    sb.AppendLine(kv.Key + "=" + kv.Value);
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public string Get(string key, string def)
        {
            string v;
            return values.TryGetValue(key, out v) ? v : def;
        }

        public int GetInt(string key, int def)
        {
            int v;
            return int.TryParse(Get(key, ""), out v) ? v : def;
        }

        public void Set(string key, string value) { values[key] = value ?? ""; }
        public void SetInt(string key, int value) { values[key] = value.ToString(); }
    }

    // Asks for the level name and the folder to keep levels in.
    public class NameDialog : Form
    {
        private readonly TextBox txtName = new TextBox();
        private readonly TextBox txtRoot = new TextBox();

        public string LevelName { get { return txtName.Text.Trim(); } }
        public string RootFolder { get { return txtRoot.Text.Trim(); } }

        public NameDialog(string defaultName, string defaultRoot)
        {
            Text = S.SaveTitle;
            if (SystemFonts.MessageBoxFont != null) Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            // An auto sizing layout: labels stay whole under another font
            // and under system scaling.
            TableLayoutPanel rows = new TableLayoutPanel();
            rows.Dock = DockStyle.Fill;
            rows.AutoSize = true;
            rows.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            rows.ColumnCount = 2;
            rows.Padding = new Padding(12, 12, 12, 12);

            rows.Controls.Add(Caption(S.SaveNameLabel), 0, 0);
            txtName.Text = defaultName;
            txtName.Width = 320;
            txtName.Margin = new Padding(0, 2, 0, 6);
            rows.Controls.Add(txtName, 1, 0);

            rows.Controls.Add(Caption(S.SaveFolderLabel), 0, 1);

            FlowLayoutPanel rootRow = new FlowLayoutPanel();
            rootRow.AutoSize = true;
            rootRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            rootRow.WrapContents = false;
            rootRow.Margin = new Padding(0, 0, 0, 6);

            txtRoot.Text = defaultRoot;
            txtRoot.Width = 230;
            txtRoot.Margin = new Padding(0, 2, 8, 0);

            // The button matches the field height, both come from the font.
            Button browse = Wide(S.BtnBrowse, 84);
            browse.AutoSize = false;
            browse.Size = new Size(Math.Max(84, browse.PreferredSize.Width), txtRoot.PreferredHeight);
            browse.Margin = new Padding(0, 2, 0, 0);
            browse.Click += delegate
            {
                string picked = FolderPicker.Show(this, S.SaveFolderPrompt, txtRoot.Text);
                if (picked != null) txtRoot.Text = picked;
            };

            rootRow.Controls.Add(txtRoot);
            rootRow.Controls.Add(browse);
            rows.Controls.Add(rootRow, 1, 1);

            Label hint = Caption(S.SaveHint);
            hint.ForeColor = SystemColors.GrayText;
            hint.Margin = new Padding(0, 6, 0, 10);
            rows.Controls.Add(hint, 0, 2);
            rows.SetColumnSpan(hint, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Dock = DockStyle.Fill;

            Button cancel = Wide(S.BtnCancel, 96);
            cancel.DialogResult = DialogResult.Cancel;

            Button ok = Wide(S.MenuSave, 96);
            ok.DialogResult = DialogResult.OK;
            ok.Click += delegate
            {
                if (LevelName.Length == 0 || LevelName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    MessageBox.Show(this, S.ErrBadName,
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
                if (RootFolder.Length == 0 || !Directory.Exists(RootFolder))
                {
                    MessageBox.Show(this, S.ErrNoLevelsFolder,
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                }
            };

            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            rows.Controls.Add(buttons, 0, 3);
            rows.SetColumnSpan(buttons, 2);

            Controls.Add(rows);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static Label Caption(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.MaximumSize = new Size(460, 0);
            l.Margin = new Padding(0, 5, 10, 0);
            return l;
        }

        private static Button Wide(string text, int minWidth)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(minWidth, 26);
            b.Margin = new Padding(6, 0, 0, 0);
            return b;
        }
    }
}

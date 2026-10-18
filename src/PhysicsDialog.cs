using System;
using System.Drawing;
using System.Windows.Forms;

namespace TigEditor
{
    // Choosing the jump physics. The layout is an auto sizing table, so the
    // window follows the text and labels are not cut under another font or
    // under system scaling.
    public class PhysicsDialog : Form
    {
        private const int TextWidth = 520;

        private readonly RadioButton[] choices;
        private readonly RadioButton own = new RadioButton();
        private readonly RadioButton shapeMode = new RadioButton();
        private readonly RadioButton gameMode = new RadioButton();
        private Label leftCaption, rightCaption, flight;
        private readonly NumericUpDown jumpBox = new NumericUpDown();
        private readonly NumericUpDown gravityBox = new NumericUpDown();


        // The chosen preset number, or a number past them for custom values.
        // Zero means the normal jump, and only then is physics taken out of
        // level.ini.
        public int Selected
        {
            get
            {
                for (int i = 0; i < choices.Length; i++)
                    if (choices[i].Checked) return i;
                return choices.Length;
            }
        }

        // The current choice while the dialog is open. The guide follows it.
        public event Action<GamePhysics.Preset> Preview;

        // The physics the dialog was closed with.
        public GamePhysics.Preset Result
        {
            get
            {
                if (Selected < choices.Length) return GamePhysics.ByIndex(Selected);
                return shapeMode.Checked
                    ? GamePhysics.FromShape((double)jumpBox.Value, (double)gravityBox.Value)
                    : GamePhysics.Custom((float)jumpBox.Value, (double)gravityBox.Value);
            }
        }

        public PhysicsDialog(GamePhysics.Preset current)
        {
            int selected = GamePhysics.IndexOf(current.Jump, current.Gravity);
            Text = S.PhysicsTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (SystemFonts.MessageBoxFont != null) Font = SystemFonts.MessageBoxFont;

            TableLayoutPanel rows = new TableLayoutPanel();
            rows.Dock = DockStyle.Fill;
            rows.AutoSize = true;
            rows.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            rows.ColumnCount = 1;
            rows.Padding = new Padding(12, 12, 12, 12);

            rows.Controls.Add(Line(
                S.PhysicsIntro,
                0, 0, 0, 12));

            rows.Controls.Add(Line(S.PhysicsJumpLabel, 0, 0, 0, 4));

            choices = new RadioButton[GamePhysics.Presets.Length];
            for (int i = 0; i < choices.Length; i++)
            {
                RadioButton r = new RadioButton();
                r.Text = GamePhysics.Presets[i].Name;
                r.AutoSize = true;
                r.Margin = new Padding(12, 2, 0, 2);
                r.Checked = i == selected;
                r.Tag = i;
                r.CheckedChanged += delegate(object sender, EventArgs e)
                {
                    RadioButton box = (RadioButton)sender;
                    if (box.Checked) ShowPreset(GamePhysics.ByIndex((int)box.Tag));
                    ShowNumbers();
                };
                choices[i] = r;
                rows.Controls.Add(r);
            }

            own.Text = S.PhysicsCustom;
            own.AutoSize = true;
            own.Margin = new Padding(12, 2, 0, 2);
            own.Checked = selected < 0;
            own.CheckedChanged += delegate { UpdateOwn(); };
            rows.Controls.Add(own);

            FlowLayoutPanel ownRow = new FlowLayoutPanel();
            ownRow.AutoSize = true;
            ownRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ownRow.Margin = new Padding(12 + SystemInformation.MenuCheckSize.Width, 2, 0, 4);
            ownRow.WrapContents = false;

            leftCaption = Caption(S.PhysicsHeight);
            rightCaption = Caption(S.PhysicsLength);


            ownRow.Controls.Add(leftCaption);
            jumpBox.Width = BoxWidth();
            jumpBox.Margin = new Padding(4, 0, 10, 0);
            jumpBox.DecimalPlaces = 2;
            jumpBox.ValueChanged += delegate { if (!filling) ShowNumbers(); };
            ownRow.Controls.Add(jumpBox);

            ownRow.Controls.Add(rightCaption);
            gravityBox.Width = BoxWidth();
            gravityBox.Margin = new Padding(4, 0, 10, 0);
            gravityBox.DecimalPlaces = 2;
            gravityBox.ValueChanged += delegate { if (!filling) ShowNumbers(); };
            ownRow.Controls.Add(gravityBox);

            flight = Caption(S.PhysicsFlightSample);
            ownRow.Controls.Add(flight);

            TableLayoutPanel modeBox = new TableLayoutPanel();
            modeBox.AutoSize = true;
            modeBox.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            modeBox.ColumnCount = 1;
            modeBox.Margin = new Padding(12 + SystemInformation.MenuCheckSize.Width, 2, 0, 6);

            shapeMode.Text = S.PhysicsModeShape;
            shapeMode.Checked = true;
            shapeMode.AutoSize = true;
            shapeMode.Margin = new Padding(0, 0, 0, 0);
            shapeMode.CheckedChanged += delegate { if (shapeMode.Checked) SwitchMode(); };
            modeBox.Controls.Add(shapeMode);

            gameMode.Text = S.PhysicsModeGame;
            gameMode.AutoSize = true;
            gameMode.Margin = new Padding(0, 0, 0, 0);
            gameMode.CheckedChanged += delegate { if (gameMode.Checked) SwitchMode(); };
            modeBox.Controls.Add(gameMode);

            rows.Controls.Add(modeBox);

            SetFields(current);
            rows.Controls.Add(ownRow);


            rows.Controls.Add(Line(
                S.PhysicsHint, 0, 14, 0, 10));

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Dock = DockStyle.Fill;
            buttons.Margin = new Padding(0);

            Button cancel = new Button();
            cancel.Text = S.BtnCancel;
            cancel.DialogResult = DialogResult.Cancel;
            cancel.AutoSize = true;
            cancel.MinimumSize = new Size(90, 26);

            Button ok = new Button();
            ok.Text = S.BtnOk;
            ok.DialogResult = DialogResult.OK;
            ok.AutoSize = true;
            ok.MinimumSize = new Size(90, 26);

            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            rows.Controls.Add(buttons);

            Controls.Add(rows);
            AcceptButton = ok;
            CancelButton = cancel;
            UpdateOwn();
        }

        // The numbers of the chosen preset go into the fields, so what the jump
        // is made of is visible and there is something to start from.
        private void ShowPreset(GamePhysics.Preset preset) { SetFields(preset); }

        // The fields show the same physics in the units of the chosen mode.
        private void SetFields(GamePhysics.Preset preset)
        {
            live = preset;
            filling = true;
            if (shapeMode.Checked)
            {
                leftCaption.Text = S.PhysicsHeight;
                rightCaption.Text = S.PhysicsLength;
                Limits(jumpBox, (decimal)GamePhysics.MinHeight, (decimal)GamePhysics.MaxHeight, 0.05m);
                Limits(gravityBox, (decimal)GamePhysics.MinLength, (decimal)GamePhysics.MaxLength, 0.2m);
                double h, l;
                GamePhysics.Shape(preset, out h, out l);
                jumpBox.Value = Clamp(jumpBox, (decimal)h);
                gravityBox.Value = Clamp(gravityBox, (decimal)l);
            }
            else
            {
                leftCaption.Text = S.PhysicsJumpField;
                rightCaption.Text = S.PhysicsGravityField;
                Limits(jumpBox, (decimal)GamePhysics.MinJump, (decimal)GamePhysics.MaxJump, 0.5m);
                Limits(gravityBox, (decimal)GamePhysics.MinGravity, (decimal)GamePhysics.MaxGravity, 0.05m);
                jumpBox.Value = Clamp(jumpBox, (decimal)preset.Jump);
                gravityBox.Value = Clamp(gravityBox, (decimal)preset.Gravity);
            }
            filling = false;
        }

        // Switching the mode changes nothing about the jump itself, only the
        // units the same numbers are shown in.
        private void SwitchMode()
        {
            if (filling) return;
            SetFields(live ?? Result);
            ShowNumbers();
        }

        // Field width by the longest value it can hold, plus room for the
        // spinner arrows.
        private int BoxWidth()
        {
            // The widest values allowed: length 30.00 and impulse 60.00.
            return TextRenderer.MeasureText("60.00", Font).Width
                 + SystemInformation.VerticalScrollBarWidth + 8;
        }

        // The bounds are widened first and narrowed after: otherwise a new value
        // may not fit between the old ones and would be clipped.
        private static void Limits(NumericUpDown box, decimal min, decimal max, decimal step)
        {
            box.Minimum = Math.Min(min, box.Minimum);
            box.Maximum = Math.Max(max, box.Maximum);
            box.Increment = step;
            box.Minimum = min;
            box.Maximum = max;
        }

        private static decimal Clamp(NumericUpDown box, decimal v)
        {
            return v < box.Minimum ? box.Minimum : (v > box.Maximum ? box.Maximum : v);
        }

        private bool filling;
        private GamePhysics.Preset live;

        // The custom value fields are alive only while that choice is picked.
        private void UpdateOwn()
        {
            jumpBox.Enabled = own.Checked;
            gravityBox.Enabled = own.Checked;
            shapeMode.Enabled = own.Checked;
            gameMode.Enabled = own.Checked;
            if (leftCaption != null) leftCaption.Enabled = own.Checked;
            if (rightCaption != null) rightCaption.Enabled = own.Checked;
            if (flight != null) flight.Enabled = own.Checked;
            ShowNumbers();
        }

        private static Label Caption(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Margin = new Padding(0, 4, 0, 0);
            return l;
        }

        private Label Line(string text, int left, int top, int right, int bottom)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.MaximumSize = new Size(TextWidth, 0);
            label.Margin = new Padding(left, top, right, bottom);
            return label;
        }

        // Numbers of the current choice, worked out by the model the guide uses.
        private void ShowNumbers()
        {
            GamePhysics.Preset preset = Result;
            System.Collections.Generic.List<PointF> path =
                GamePhysics.Trajectory(preset, 0, 0, 240);

            float peak = 0;
            foreach (PointF p in path) if (p.Y > peak) peak = p.Y;
            int frames = path.Count - 1;
            float length = path[path.Count - 1].X;

            live = preset;
            if (Preview != null) Preview(preset);

            if (flight != null)
                flight.Text = string.Format(S.PhysicsFlight, (frames / 60.0).ToString("0.00"));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TigEditor
{
	// What the small dialogs share: the system font, margins that follow the
	// screen scale and labels measured by hand. Content is placed by explicit
	// coordinates, so it stays centred and nothing is clipped at any scale.
	internal static class DialogKit
	{
		private static float scale;

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy,
			IntPtr[] icons, uint[] ids, uint count, uint flags);
		[DllImport("user32.dll")]
		private static extern bool DestroyIcon(IntPtr icon);

		// Pixels for a size given for 100% scale.
		public static int Dip(int value)
		{
			if (scale == 0)
			{
				using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
			}
			return (int)Math.Round(value * scale);
		}

		public static void Setup(Form form, string title)
		{
			form.Text = title;
			if (SystemFonts.MessageBoxFont != null) form.Font = SystemFonts.MessageBoxFont;
			form.FormBorderStyle = FormBorderStyle.FixedDialog;
			form.AutoScaleMode = AutoScaleMode.None;
			form.MaximizeBox = false;
			form.MinimizeBox = false;
			form.ShowInTaskbar = false;
			form.StartPosition = FormStartPosition.CenterParent;
		}

		// The margin around the content.
		public static int Margin { get { return Dip(12); } }

		// How far the buttons stand from the right and bottom edges.
		public static int Corner { get { return Dip(8); } }

		// The gap between buttons in a row, as between OK and Cancel in the
		// jump physics dialog.
		public const int ButtonGap = 6;

		private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

		private static Size Measure(string text, Font font, int width)
		{
			return TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), Wrap);
		}

		// The widest word: a wrapped text cannot be narrower than that.
		private static int Longest(string text, Font font)
		{
			int widest = 0;
			foreach (string word in text.Split(' ', '\r', '\n', '\t'))
				if (word.Length > 0) widest = Math.Max(widest, Measure(word, font, int.MaxValue).Width);
			return widest;
		}

		// Gives the label the size of its text, wrapped no wider than maxWidth.
		// A wrapped text is measured as wide as the limit, which leaves an empty
		// strip at the side, so the box is narrowed to what keeps the line count.
		public static void Fit(Label label, Font font, int maxWidth, ContentAlignment align)
		{
			label.Font = font;
			label.AutoSize = false;
			label.UseMnemonic = false;
			label.TextAlign = align;

			Size size = Measure(label.Text, font, int.MaxValue);
			int width = size.Width, height = size.Height;
			if (width > maxWidth)
			{
				height = Measure(label.Text, font, maxWidth).Height;
				int low = Math.Min(Longest(label.Text, font), maxWidth), high = maxWidth;
				while (low < high)
				{
					int mid = (low + high) / 2;
					if (Measure(label.Text, font, mid).Height <= height) high = mid;
					else low = mid + 1;
				}
				width = high;
			}
			label.Size = new Size(width + Dip(6), height + Dip(2));
		}

		public static Label Text(string text, Font font, int maxWidth, ContentAlignment align)
		{
			Label label = new Label();
			label.Text = text;
			Fit(label, font, maxWidth, align);
			return label;
		}

		// Windows leaves this much blank around a button's frame, so what is
		// seen is smaller than its bounds. Fields beside buttons allow for it.
		public static int FrameInset { get { return Math.Max(1, Dip(1)); } }

		public static int ButtonHeight { get { return SystemInformation.HorizontalScrollBarHeight + 9; } }

		// A field that stands beside a button is as tall as the button looks.
		public static int FieldHeight { get { return ButtonHeight - FrameInset * 2; } }

		// All buttons of a dialog share one height; the width follows the text
		// and is no less than the usual one or the least given, in pixels.
		public static Button MakeButton(Form form, string text, DialogResult result)
		{
			return MakeButton(form, text, result, 0);
		}

		public static Button MakeButton(Form form, string text, DialogResult result, int least)
		{
			int step = SystemInformation.HorizontalScrollBarHeight;
			Button b = new Button();
			b.Text = text;
			b.DialogResult = result;
			b.AutoSize = false;
			int width = TextRenderer.MeasureText(text, form.Font).Width + Dip(24);
			b.Size = new Size(Math.Max(Math.Max(step * 4, least), width), ButtonHeight);
			return b;
		}

		public static int RowWidth(Button[] buttons)
		{
			int width = ButtonGap * (buttons.Length - 1);
			foreach (Button b in buttons) width += b.Width;
			return width;
		}

		public static void PlaceRow(Button[] buttons, int x, int y)
		{
			foreach (Button b in buttons)
			{
				b.Location = new Point(x, y);
				x += b.Width + ButtonGap;
			}
		}

		// The application icon at the size asked for. The exe holds one large
		// image, so it is scaled by the system instead of stretching a small icon.
		public static Image AppIcon(int size)
		{
			string path = Application.ExecutablePath;
			try
			{
				IntPtr[] handle = new IntPtr[1];
				uint[] id = new uint[1];
				if (PrivateExtractIcons(path, 0, size, size, handle, id, 1, 0) > 0
					&& handle[0] != IntPtr.Zero)
				{
					try
					{
						using (Icon icon = Icon.FromHandle(handle[0]))
							return new Bitmap(icon.ToBitmap(), size, size);
					}
					finally { DestroyIcon(handle[0]); }
				}
			}
			catch { }

			try
			{
				Icon ico = Icon.ExtractAssociatedIcon(path);
				if (ico != null) return new Bitmap(ico.ToBitmap(), size, size);
			}
			catch { }
			return new Bitmap(size, size);
		}
	}

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

		private const int FadeMs = 120;
		private readonly Timer fader = new Timer();
		private readonly Stopwatch fadeClock = new Stopwatch();
		private double fadeFrom, fadeTo;

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
				return i <= 0 ? string.Empty : langCodes[i];
			}
		}

		public SettingsDialog(bool invertScroll, bool invertZoom, bool invertDraw,
							  bool pureBlack, string gamePath, string language)
		{
			DialogKit.Setup(this, S.Get("SettingsTitle"));
			fader.Interval = 10;
			fader.Tick += delegate { FadeStep(); };

			int pad = 14;                                   // the margin around the content
			int line = TextRenderer.MeasureText(S.Get("MeasureSample"), Font).Height;
			int row = line + 6;                             // the step between rows
			int gap = line;                                 // the gap between blocks
			int indent = SystemInformation.MenuCheckSize.Width;
			int width = 373;                                // width of the working area
			int right = pad + width;
			int inset = DialogKit.FrameInset;
			int corner = Math.Min(pad, DialogKit.Corner);
			int edge = right;                               // frames end as far from the edge as they start

			int y = pad;

			// Language comes first: it decides what language the rest is read in.
			Label langLabel = Label(S.Get("SettingsLanguage"), pad, y);
			Controls.Add(langLabel);

			string[] codes = S.Available();
			langCodes = new string[codes.Length + 1];
			cbLang.DropDownStyle = ComboBoxStyle.DropDownList;
			cbLang.Items.Add(S.Get("SettingsLanguageSystem"));
			langCodes[0] = string.Empty;
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
			cbLang.Font = Font;                             // the height follows the font
			cbLang.SetBounds(pad + langLabel.PreferredWidth + 8, y,
							 widest + SystemInformation.VerticalScrollBarWidth + 12, row);
			Controls.Add(cbLang);
			langLabel.Top = y + (cbLang.Height - langLabel.PreferredHeight) / 2;
			y += Math.Max(row, cbLang.Height) + gap - 4;

			Controls.Add(Label(S.Get("SettingsInvert"), pad, y));
			y += row;

			y = Add(Check(chkInvertScroll, S.Get("SettingsInvertScroll"), invertScroll),
					pad + indent, y, row);
			y = Add(Check(chkInvertZoom, S.Get("SettingsInvertZoom"), invertZoom),
					pad + indent, y, row);
			y = Add(Check(chkInvertDraw, S.Get("SettingsInvertDraw"), invertDraw),
					pad + indent, y, row);

			y += 10;                                        // a setting of its own, not a list item
			y = Add(Check(chkBlack, S.Get("SettingsBlack"), pureBlack), pad, y, row);
			y += 6;

			Controls.Add(Label(S.Get("SettingsGamePath"), pad, y));
			y += row;

			Button browse = DialogKit.MakeButton(this, S.Get("BtnBrowse"), DialogResult.None);
			Button about = DialogKit.MakeButton(this, S.Get("AboutTitle"), DialogResult.None);
			Button cancel = DialogKit.MakeButton(this, S.Get("BtnCancel"), DialogResult.Cancel);
			Button ok = DialogKit.MakeButton(this, S.Get("BtnOk"), DialogResult.OK);

			// Browse is as wide as Cancel and the field ends where OK ends, so
			// the gap beside the field stands right above the gap between the
			// buttons. Frames of everything lie on the same two lines.
			browse.Width = cancel.Width = Math.Max(browse.Width, cancel.Width);
			cancel.Left = edge + inset - cancel.Width;
			ok.Left = cancel.Left - ok.Width - DialogKit.ButtonGap;
			browse.Left = cancel.Left;

			// The field is as tall as the buttons look, its text stays centred.
			txtGame.Text = gamePath ?? string.Empty;
			txtGame.AutoSize = false;
			txtGame.SetBounds(pad, y + inset, ok.Right - inset - pad, DialogKit.FieldHeight);
			Controls.Add(txtGame);

			browse.Top = y;
			browse.Click += delegate
			{
				using (OpenFileDialog dlg = new OpenFileDialog())
				{
					dlg.Filter = S.Get("GameFilter");
					if (File.Exists(txtGame.Text))
						dlg.InitialDirectory = Path.GetDirectoryName(txtGame.Text);
					if (dlg.ShowDialog(this) == DialogResult.OK) txtGame.Text = dlg.FileName;
				}
			};
			Controls.Add(browse);
			y += browse.Height + gap + 4;

			about.Click += delegate { ShowAbout(); };
			about.Left = pad - inset;
			about.Top = cancel.Top = ok.Top = y;
			Controls.Add(about);
			Controls.Add(ok);
			Controls.Add(cancel);
			y += cancel.Height + corner;

			ClientSize = new Size(right + pad, y);
			AcceptButton = ok;
			CancelButton = cancel;
		}

		// The settings fade out while the about box opens and back in as it
		// closes, so the two overlap the way system dialogs do. Hide() would end
		// this modal dialog, so the window is made transparent instead; it stays
		// alive and nothing in it is reset.
		private void ShowAbout()
		{
			using (AboutDialog dlg = new AboutDialog())
			{
				dlg.FormClosing += delegate { FadeTo(1); };
				FadeTo(0);
				dlg.ShowDialog(this);
			}
		}

		private void FadeTo(double to)
		{
			fadeFrom = Opacity;
			fadeTo = to;
			fadeClock.Reset();
			fadeClock.Start();
			fader.Start();
		}

		private void FadeStep()
		{
			double done = Math.Min(1.0, fadeClock.ElapsedMilliseconds / (double)FadeMs);
			Opacity = fadeFrom + (fadeTo - fadeFrom) * done;
			if (done >= 1.0) fader.Stop();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing) fader.Dispose();
			base.Dispose(disposing);
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
			DialogKit.Setup(this, S.Get("SaveTitle"));
			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			// An auto sizing layout: labels stay whole under another font
			// and under system scaling.
			TableLayoutPanel rows = new TableLayoutPanel();
			rows.Dock = DockStyle.Fill;
			rows.AutoSize = true;
			rows.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			rows.ColumnCount = 2;

			// The buttons sit in the corner, and the fields keep the usual
			// margin from the right edge.
			int corner = Math.Min(12, DialogKit.Corner);
			rows.Padding = new Padding(12, 12, corner, corner);
			int fieldRight = 12 - corner;

			rows.Controls.Add(Caption(S.Get("SaveNameLabel")), 0, 0);
			txtName.Text = defaultName;
			txtName.AutoSize = false;
			txtName.Size = new Size(320, DialogKit.FieldHeight);
			txtName.Margin = new Padding(0, 2 + DialogKit.FrameInset, fieldRight, 6 + DialogKit.FrameInset);
			rows.Controls.Add(txtName, 1, 0);

			rows.Controls.Add(Caption(S.Get("SaveFolderLabel")), 0, 1);

			FlowLayoutPanel rootRow = new FlowLayoutPanel();
			rootRow.AutoSize = true;
			rootRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			rootRow.WrapContents = false;
			rootRow.Margin = new Padding(0, 0, fieldRight, 6);

			// The fields are as tall as the buttons, their text stays centred.
			txtRoot.Text = defaultRoot;
			txtRoot.AutoSize = false;
			txtRoot.Size = new Size(230, DialogKit.FieldHeight);
			txtRoot.Margin = new Padding(0, 2 + DialogKit.FrameInset, 8, 0);

			Button browse = Wide(S.Get("BtnBrowse"), DialogResult.None, 84);
			browse.Margin = new Padding(0, 2, 0, 0);
			browse.Click += delegate
			{
				string picked = FolderPicker.Show(this, S.Get("SaveFolderPrompt"), txtRoot.Text);
				if (picked != null) txtRoot.Text = picked;
			};

			rootRow.Controls.Add(txtRoot);
			rootRow.Controls.Add(browse);
			rows.Controls.Add(rootRow, 1, 1);

			Label hint = Caption(S.Get("SaveHint"));
			hint.ForeColor = SystemColors.GrayText;
			hint.Margin = new Padding(0, 6, 0, 10);
			rows.Controls.Add(hint, 0, 2);
			rows.SetColumnSpan(hint, 2);

			FlowLayoutPanel buttons = new FlowLayoutPanel();
			buttons.AutoSize = true;
			buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			buttons.FlowDirection = FlowDirection.RightToLeft;
			buttons.Dock = DockStyle.Fill;
			buttons.Margin = new Padding(0, 3, 0, 0);

			Button cancel = Wide(S.Get("BtnCancel"), DialogResult.Cancel, 96);
			Button ok = Wide(S.Get("MenuSave"), DialogResult.OK, 96);
			ok.Click += delegate
			{
				if (LevelName.Length == 0 || LevelName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
				{
					MessageBox.Show(this, S.Get("ErrBadName"),
						Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
					DialogResult = DialogResult.None;
					return;
				}
				if (RootFolder.Length == 0 || !Directory.Exists(RootFolder))
				{
					MessageBox.Show(this, S.Get("ErrNoLevelsFolder"),
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

		// The text is centred against a field beside it.
		private Label Caption(string text)
		{
			Label l = new Label();
			l.Text = text;
			l.AutoSize = true;
			l.MaximumSize = new Size(460, 0);
			l.Margin = new Padding(0, 2 + (DialogKit.ButtonHeight - Font.Height) / 2, 10, 0);
			return l;
		}

		private Button Wide(string text, DialogResult result, int least)
		{
			Button b = DialogKit.MakeButton(this, text, result, least);
			b.Margin = new Padding(DialogKit.ButtonGap, 0, 0, 0);
			return b;
		}
	}

	// What the object counter is made of. The layout is explicit: names start
	// at the left margin, numbers end at the right one and share a width, so
	// they do not jump from row to row.
	public class CountsDialog : Form
	{
		public CountsDialog(Level level)
		{
			DialogKit.Setup(this, S.Get("CountsTitle"));

			int blocks = 0, spikes = 0;
			foreach (LevelObj o in level.Cells.Values)
			{
				if (o.Type == ObjType.Block) blocks++;
				else if (o.Type == ObjType.Spike) spikes++;
			}
			int total = blocks + spikes + level.Pits.Count;

			int pad = 12;           // one margin on every side
			int line = TextRenderer.MeasureText(S.Get("MeasureSample"), Font).Height;
			int row = line + 7;
			int width = 210;                       // a narrow window, as it was
			int right = pad + width;
			int numW = TextRenderer.MeasureText("00000", Font).Width;

			int y = pad;
			y = Row(S.Get("CountsBlocks"), blocks, pad, y, row, right, numW);
			y = Row(S.Get("CountsSpikes"), spikes, pad, y, row, right, numW);
			y = Row(S.Get("CountsPits"), level.Pits.Count, pad, y, row, right, numW);
			y = Row(S.Get("CountsTotal"), total, pad, y, row, right, numW);

			// The rule separates what counts against the game limit
			// from what does not.
			Label rule = new Label();
			rule.BorderStyle = BorderStyle.Fixed3D;
			rule.AutoSize = false;
			rule.SetBounds(pad, y + 6, width, 2);
			Controls.Add(rule);
			y += 6 + 2 + 10;

			y = Row(S.Get("CountsBackgrounds"), level.Backgrounds.Count, pad, y, row, right, numW);
			y = Row(S.Get("CountsGravity"), level.GravityChanges.Count, pad, y, row, right, numW);
			y = Row(S.Get("CountsFallStripes"), level.BlocksFall.Count, pad, y, row, right, numW);
			y = Row(S.Get("CountsRiseStripes"), level.BlocksRise.Count, pad, y, row, right, numW);

			Button close = DialogKit.MakeButton(this, S.Get("BtnClose"), DialogResult.OK);
			int corner = Math.Min(pad, DialogKit.Corner);
			close.Location = new Point(right + pad - corner - close.Width, y + 10);
			Controls.Add(close);

			ClientSize = new Size(right + pad, close.Bottom + corner);
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
			DialogKit.Setup(this, S.Get("EffectsTitle"));

			int width = Pad * 2 + Col * 2 + Gap;

			Label intro = Para(
				S.Get("EffectsIntro"),
				Pad, Pad, width - Pad * 2);

			int top = intro.Bottom + 16;

			Label leftHead = Head(S.Get("EffectsFallHead"), Pad, top);
			Label left = Para(
				S.Get("EffectsFallText") + Break +
				S.Get("EffectsFallWhy") + Break +
				S.Get("EffectsFallExample"),
				Pad, leftHead.Bottom + 6, Col);

			int rightX = Pad + Col + Gap;
			Label rightHead = Head(S.Get("EffectsRiseHead"), rightX, top);
			Label right = Para(
				S.Get("EffectsRiseLength") + Break +
				S.Get("EffectsRiseRules") + Break +
				S.Get("EffectsRiseExample"),
				rightX, rightHead.Bottom + 6, Col);

			Controls.Add(intro);
			Controls.Add(leftHead);
			Controls.Add(left);
			Controls.Add(rightHead);
			Controls.Add(right);

			Button close = DialogKit.MakeButton(this, S.Get("BtnClose"), DialogResult.Cancel);
			int corner = Math.Min(Pad, DialogKit.Corner);
			close.Location = new Point(width - corner - close.Width,
									   Math.Max(left.Bottom, right.Bottom) + 16);
			Controls.Add(close);

			ClientSize = new Size(width, close.Bottom + corner);
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

	// A small screen about the program: everything stands in one centred column.
	public class AboutDialog : Form
	{
		private const string Repo = "https://github.com/Omegaplexx/TIGEditor";
		private const string LinkWord = "GitHub";    // the part of the line that is the link

		public AboutDialog()
		{
			DialogKit.Setup(this, S.Get("AboutTitle"));

			int margin = DialogKit.Margin;
			int wrap = DialogKit.Dip(320);
			ContentAlignment middle = ContentAlignment.TopCenter;

			Image image = DialogKit.AppIcon(DialogKit.Dip(48));
			PictureBox icon = new PictureBox();
			icon.Image = image;
			icon.Size = image.Size;

			Font title = new Font(Font.FontFamily, Font.Size + 3, FontStyle.Bold);
			Label name = DialogKit.Text(MainForm.AppName, title, wrap, middle);
			Label version = DialogKit.Text(S.Get("AboutVersion"), Font, wrap, middle);
			Label author = DialogKit.Text(S.Get("AboutAuthor"), Font, wrap, middle);

			LinkLabel link = new LinkLabel();
			link.Text = S.Get("AboutSource");
			int at = link.Text.IndexOf(LinkWord, StringComparison.Ordinal);
			if (at >= 0) link.LinkArea = new LinkArea(at, LinkWord.Length);
			DialogKit.Fit(link, Font, wrap, middle);
			link.LinkClicked += delegate
			{
				try { Process.Start(Repo); } catch { }
			};

			Label summary = DialogKit.Text(S.Get("AboutSummary"), Font, wrap, middle);
			Button close = DialogKit.MakeButton(this, S.Get("BtnClose"), DialogResult.OK);

			Control[] column = { icon, name, version, author, link, summary, close };
			int width = 0;
			foreach (Control c in column) width = Math.Max(width, c.Width);

			// Blocks are spaced by the text height: the icon and the title, the
			// facts, the summary and the button each form a group.
			int line = Font.Height;
			int y = margin;
			y = Place(icon, width, margin, y, line * 3 / 4);
			y = Place(name, width, margin, y, line / 4);
			y = Place(version, width, margin, y, 0);
			y = Place(author, width, margin, y, 0);
			y = Place(link, width, margin, y, line);
			y = Place(summary, width, margin, y, line * 3 / 2);
			y = Place(close, width, margin, y, 0);

			Controls.AddRange(column);
			ClientSize = new Size(width + margin * 2, y + DialogKit.Corner);

			AcceptButton = close;
			CancelButton = close;
			ActiveControl = close;
		}

		// Puts the control in the middle of the column and returns the next top.
		private static int Place(Control c, int width, int margin, int y, int after)
		{
			c.Location = new Point(margin + (width - c.Width) / 2, y);
			return y + c.Height + after;
		}
	}

	// Yes recodes the sounds and goes on, No goes on as is, Cancel stops.
	// No is the default button and holds the focus.
	public class SoundWarningDialog : Form
	{
		public SoundWarningDialog(string text)
		{
			DialogKit.Setup(this, MainForm.AppName);

			int margin = DialogKit.Margin;
			int gap = DialogKit.Dip(14);       // edge to icon, and icon to text
			int bearing = DialogKit.Dip(3);    // the blank a label keeps before its text

			// The icon is as large as the system draws it, so nothing is cut off.
			Image image = SystemIcons.Warning.ToBitmap();
			PictureBox icon = new PictureBox();
			icon.Image = image;
			icon.Size = image.Size;

			Label label = DialogKit.Text(text, Font, DialogKit.Dip(380), ContentAlignment.TopLeft);

			Button recode = DialogKit.MakeButton(this, S.Get("BtnRecode"), DialogResult.Yes);
			Button start = DialogKit.MakeButton(this, S.Get("BtnStart"), DialogResult.No);
			Button cancel = DialogKit.MakeButton(this, S.Get("BtnCancel"), DialogResult.Cancel);
			Button[] buttons = { recode, start, cancel };

			// The buttons sit in the corner, as far from the right and bottom
			// edges as the margin. The icon with the text is centred if they
			// are narrower than the buttons.
			int bodyWidth = gap + icon.Width + gap - bearing + label.Width - DialogKit.Dip(6) + margin;
			int corner = DialogKit.Corner;
			int row = DialogKit.RowWidth(buttons);
			int width = Math.Max(bodyWidth, row + margin + corner);
			int bodyHeight = Math.Max(icon.Height, label.Height);

			int x = (width - bodyWidth) / 2 + gap;
			icon.Location = new Point(x, margin + (bodyHeight - icon.Height) / 2);
			label.Location = new Point(x + icon.Width + gap - bearing,
									   margin + (bodyHeight - label.Height) / 2);

			int y = margin + bodyHeight + gap;
			DialogKit.PlaceRow(buttons, width - corner - row, y);

			Controls.Add(icon);
			Controls.Add(label);
			Controls.AddRange(buttons);
			ClientSize = new Size(width, y + start.Height + corner);

			AcceptButton = start;
			CancelButton = cancel;
			ActiveControl = start;
		}
	}

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
		private readonly Button apply;
		private readonly Button restore;
		private Label holdCaption;

		public PatchDialog(string exePath)
		{
			this.exePath = exePath;

			DialogKit.Setup(this, S.Get("PatchTitle"));

			holdExtra.Minimum = 0;
			holdExtra.Maximum = 500;
			holdExtra.Increment = 10;
			holdExtra.Value = 150;
			holdExtra.Width = 70;

			Label intro = Para(
				S.Get("PatchIntro"),
				ColumnWidth * 2 + ColumnGap, SystemColors.ControlText);
			intro.Location = new Point(Pad, Pad);
			Controls.Add(intro);

			int top = intro.Bottom + 14;

			// Items follow one another and move into the second column once the
			// first has taken half of the total height.
			Item[] items =
			{
				new Item(noClipKey, S.Get("MenuNoClip"),
					S.Get("PatchNoClipText")),
				new Item(extras, S.Get("PatchResourcesName"),
					S.Get("PatchResourcesText")),
				new Item(medalFix, S.Get("PatchMedalName"),
					S.Get("PatchMedalText")),
				new Item(folder, S.Get("PatchFolderName"),
					S.Get("PatchFolderText")),
				new Item(musicSync, S.Get("PatchSyncName"),
					S.Get("PatchSyncText")),
				new Item(dpiAware, S.Get("PatchDpiName"),
					S.Get("PatchDpiText")),
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

			Button close = DialogKit.MakeButton(this, S.Get("BtnClose"), DialogResult.Cancel, 96);

			apply = DialogKit.MakeButton(this, S.Get("BtnApply"), DialogResult.None, 96);
			apply.Click += delegate { DoApply(); };
			foreach (CheckBox box in Boxes()) box.CheckedChanged += delegate { UpdateApply(); };

			restore = DialogKit.MakeButton(this, S.Get("BtnRestore"), DialogResult.None, 170);
			restore.Click += delegate { DoRestore(); };

			int width = Pad * 2 + ColumnWidth * 2 + ColumnGap;
			int buttonsTop = listBottom + 14;
			int gap = DialogKit.ButtonGap;
			int corner = Math.Min(Pad, DialogKit.Corner);
			close.Location = new Point(width - corner - close.Width, buttonsTop);
			apply.Location = new Point(close.Left - gap - apply.Width, buttonsTop);
			restore.Location = new Point(apply.Left - gap - restore.Width, buttonsTop);
			Controls.Add(close);
			Controls.Add(apply);
			Controls.Add(restore);

			ClientSize = new Size(width, buttonsTop + close.Height + corner);
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
					caption.Text = S.Get("PatchHoldLabel");
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
				MessageBox.Show(this, S.Get("ErrReadGame") + state.Error,
								Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				noClipKey.Enabled = extras.Enabled = medalFix.Enabled = false;
				folder.Enabled = musicSync.Enabled = holdExtra.Enabled = false;
				dpiAware.Enabled = restore.Enabled = false;
				UpdateApply();
				return;
			}

			// What is applied cannot be undone one by one, that is what restoring
			// the original file is for. Such items go gray and checked, everything
			// else is left for the person to turn on.
			Mark(noClipKey, state.NoClipKey);
			Mark(extras, state.LevelExtras);
			Mark(medalFix, state.MedalFix);
			Mark(folder, state.RememberFolder);
			Mark(musicSync, state.MusicSync);
			holdExtra.Enabled = !state.MusicSync;
			if (holdCaption != null) holdCaption.Enabled = holdExtra.Enabled;
			if (state.HoldMs > 0) holdExtra.Value = Math.Min(holdExtra.Maximum, state.HoldMs);
			Mark(dpiAware, state.DpiAware);
			restore.Enabled = state.HasBackup;
			UpdateApply();
		}

		private CheckBox[] Boxes()
		{
			return new CheckBox[] { noClipKey, extras, medalFix, folder, musicSync, dpiAware };
		}

		// Applying makes sense only with a checked item that is not applied yet.
		// Applied ones are gray, so a check box that is both checked and
		// available is exactly such an item.
		private void UpdateApply()
		{
			bool any = false;
			foreach (CheckBox box in Boxes()) any |= box.Checked && box.Enabled;
			apply.Enabled = any;
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


		// A choice made but not yet applied is kept; a gray item that has just
		// been restored goes back to unchecked.
		private static void Mark(CheckBox box, bool applied)
		{
			if (applied) box.Checked = true;
			else if (!box.Enabled) box.Checked = false;
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
			DialogKit.Setup(this, S.Get("PhysicsTitle"));
			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			TableLayoutPanel rows = new TableLayoutPanel();
			rows.Dock = DockStyle.Fill;
			rows.AutoSize = true;
			rows.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			rows.ColumnCount = 1;
			int corner = Math.Min(12, DialogKit.Corner);
			rows.Padding = new Padding(12, 12, corner, corner);

			rows.Controls.Add(Line(
				S.Get("PhysicsIntro"),
				0, 0, 0, 12));

			rows.Controls.Add(Line(S.Get("PhysicsJumpLabel"), 0, 0, 0, 4));

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

			own.Text = S.Get("PhysicsCustom");
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

			leftCaption = Caption(S.Get("PhysicsHeight"));
			rightCaption = Caption(S.Get("PhysicsLength"));


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

			flight = Caption(S.Get("PhysicsFlightSample"));
			ownRow.Controls.Add(flight);

			TableLayoutPanel modeBox = new TableLayoutPanel();
			modeBox.AutoSize = true;
			modeBox.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			modeBox.ColumnCount = 1;
			modeBox.Margin = new Padding(12 + SystemInformation.MenuCheckSize.Width, 2, 0, 6);

			shapeMode.Text = S.Get("PhysicsModeShape");
			shapeMode.Checked = true;
			shapeMode.AutoSize = true;
			shapeMode.Margin = new Padding(0, 0, 0, 0);
			shapeMode.CheckedChanged += delegate { if (shapeMode.Checked) SwitchMode(); };
			modeBox.Controls.Add(shapeMode);

			gameMode.Text = S.Get("PhysicsModeGame");
			gameMode.AutoSize = true;
			gameMode.Margin = new Padding(0, 0, 0, 0);
			gameMode.CheckedChanged += delegate { if (gameMode.Checked) SwitchMode(); };
			modeBox.Controls.Add(gameMode);

			rows.Controls.Add(modeBox);

			SetFields(current);
			rows.Controls.Add(ownRow);


			rows.Controls.Add(Line(
				S.Get("PhysicsHint"), 0, 14, 0, 10));

			FlowLayoutPanel buttons = new FlowLayoutPanel();
			buttons.AutoSize = true;
			buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			buttons.FlowDirection = FlowDirection.RightToLeft;
			buttons.Dock = DockStyle.Fill;
			buttons.Margin = new Padding(0, 3, 0, 0);

			Button cancel = DialogKit.MakeButton(this, S.Get("BtnCancel"), DialogResult.Cancel, 90);
			Button ok = DialogKit.MakeButton(this, S.Get("BtnOk"), DialogResult.OK, 90);
			cancel.Margin = new Padding(DialogKit.ButtonGap, 0, 0, 0);
			ok.Margin = new Padding(0);

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
				leftCaption.Text = S.Get("PhysicsHeight");
				rightCaption.Text = S.Get("PhysicsLength");
				Limits(jumpBox, (decimal)GamePhysics.MinHeight, (decimal)GamePhysics.MaxHeight, 0.05m);
				Limits(gravityBox, (decimal)GamePhysics.MinLength, (decimal)GamePhysics.MaxLength, 0.2m);
				double h, l;
				GamePhysics.Shape(preset, out h, out l);
				jumpBox.Value = Clamp(jumpBox, (decimal)h);
				gravityBox.Value = Clamp(gravityBox, (decimal)l);
			}
			else
			{
				leftCaption.Text = S.Get("PhysicsJumpField");
				rightCaption.Text = S.Get("PhysicsGravityField");
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
				flight.Text = string.Format(S.Get("PhysicsFlight"), (frames / 60.0).ToString("0.00"));
		}
	}

	// Folder picking. Vista and newer get the modern dialog, the same one as
	// for files but in folder mode. XP keeps the old folder tree because
	// nothing else is available there.
	public static class FolderPicker
	{
		public static string Show(IWin32Window owner, string title, string initialPath)
		{
			if (Environment.OSVersion.Version.Major >= 6)
			{
				try
				{
					string result = ShowVista(owner, title, initialPath);
					return result;
				}
				catch
				{
					// did not work, fall back
				}
			}
			return ShowLegacy(owner, title, initialPath);
		}

		private static string ShowLegacy(IWin32Window owner, string title, string initialPath)
		{
			using (FolderBrowserDialog dlg = new FolderBrowserDialog())
			{
				dlg.Description = title;
				dlg.ShowNewFolderButton = true;
				if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
					dlg.SelectedPath = initialPath;
				return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.SelectedPath : null;
			}
		}

		private const uint FosPickFolders = 0x00000020;
		private const uint FosForceFileSystem = 0x00000040;
		private const uint SigdnFileSysPath = 0x80058000;

		private static string ShowVista(IWin32Window owner, string title, string initialPath)
		{
			IFileDialog dialog = (IFileDialog)new FileOpenDialogRcw();
			try
			{
				uint options;
				dialog.GetOptions(out options);
				dialog.SetOptions(options | FosPickFolders | FosForceFileSystem);
				if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);

				if (!string.IsNullOrEmpty(initialPath) && Directory.Exists(initialPath))
				{
					// The same IShellItem identifier: here it says what kind of
					// item we want back for the given path.
					Guid shellItemGuid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
					IShellItem start;
					if (SHCreateItemFromParsingName(initialPath, IntPtr.Zero, ref shellItemGuid, out start) == 0)
						dialog.SetFolder(start);
				}

				IntPtr handle = owner == null ? IntPtr.Zero : owner.Handle;
				if (dialog.Show(handle) != 0) return null;      // cancelled

				IShellItem item;
				dialog.GetResult(out item);
				IntPtr namePtr;
				item.GetDisplayName(SigdnFileSysPath, out namePtr);
				string path = Marshal.PtrToStringUni(namePtr);
				Marshal.FreeCoTaskMem(namePtr);
				return path;
			}
			finally
			{
				Marshal.ReleaseComObject(dialog);
			}
		}

		[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
		private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc,
			ref Guid riid, out IShellItem item);

		// The numbers below are COM identifiers fixed by Windows itself. COM has
		// no names: a class or an interface is asked for by its identifier, and
		// these are published by Microsoft. They must be copied exactly, and they
		// never change, which is why they are written here as constants.
		//
		// DC1C5A9C is the FileOpenDialog class, the modern open dialog.
		[ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
		private class FileOpenDialogRcw { }

		// 42f85136 is IFileDialog, what that class is talked to through.
		[ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"),
		 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IFileDialog
		{
			[PreserveSig] int Show(IntPtr parent);
			void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
			void SetFileTypeIndex(uint iFileType);
			void GetFileTypeIndex(out uint piFileType);
			void Advise(IntPtr pfde, out uint pdwCookie);
			void Unadvise(uint dwCookie);
			void SetOptions(uint fos);
			void GetOptions(out uint fos);
			void SetDefaultFolder(IShellItem psi);
			void SetFolder(IShellItem psi);
			void GetFolder(out IShellItem ppsi);
			void GetCurrentSelection(out IShellItem ppsi);
			void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
			void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
			void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
			void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
			void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
			void GetResult(out IShellItem ppsi);
			void AddPlace(IShellItem psi, int fdap);
			void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
			void Close(int hr);
			void SetClientGuid(ref Guid guid);
			void ClearClientData();
			void SetFilter(IntPtr pFilter);
		}

		// 43826d1e is IShellItem, one item of the shell: a folder or a file.
		[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),
		 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IShellItem
		{
			void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
			void GetParent(out IShellItem ppsi);
			void GetDisplayName(uint sigdnName, out IntPtr ppszName);
			void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
			void Compare(IShellItem psi, uint hint, out int piOrder);
		}
	}
}

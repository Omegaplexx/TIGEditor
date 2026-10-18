using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TigEditor
{
    public class MainForm : Form, IMessageFilter
    {
        internal const string AppName = "The Impossible Editor";

        private readonly GridCanvas canvas = new GridCanvas();
        private readonly MenuStrip menu = new MenuStrip();

        // A Windows menu works without taking focus: it grabs keys itself while
        // focus stays put. So we ask the menu whether it is open, not the focus.
        private bool menuActive;
        private readonly ToolStrip palette = new ToolStrip();     // tools, on the left

        // Which buttons have their own options, opened by the right arrow.
        private readonly Dictionary<ToolStripButton, ContextMenuStrip> toolMenus =
            new Dictionary<ToolStripButton, ContextMenuStrip>();
        // The status row is a plain three column table, not a StatusStrip.
        // A StatusStrip silently drops edge items when width runs short, and
        // the object counter kept appearing and vanishing.
        private readonly StatusRow statusBar = new StatusRow();
        private readonly HScrollBar hScroll = new HScrollBar();
        private readonly VScrollBar vScroll = new VScrollBar();

        private StatusRow.Btn bMusic, bFollow, bPlay, bStart, bStop;   // music buttons
        private bool followLine = true;   // the view follows the line while playing

        private ToolStripButton btnBlock, btnSpike, btnPit, btnDelete, btnFinish, btnSelect;
        private ToolStripButton btnGravity, btnBackground, btnEffect;
        private ToolStripMenuItem miGrid, miSnap;
        private ToolStripMenuItem miUndo, miRedo, miCut, miCopy, miPaste, miDelete;
        private ToolStripMenuItem miLevelFolder, miSelectAll;
        private ContextMenuStrip bgMenu, effectMenu;
        private readonly List<ToolStripButton> toolButtons = new List<ToolStripButton>();
        private readonly List<Tool> toolOrder = new List<Tool>();
        private ToolStripMenuItem miNoClip, miPhysics, miCustomArt;
        private GameAtlas levelAtlas;      // level graphics, even while switched off
        private bool customArtOn = true;
        private int paletteIconSize = 32;

        private readonly Settings settings = new Settings();
        private readonly AudioPlayer player = new AudioPlayer();
        private readonly Timer playTimer = new Timer();

        private string levelFolder;      // level folder, null until it is saved
        private string musicPath;        // music.mp3 or music.ogg inside the folder
        private bool dirty;
        private bool extraDirty;      // changes outside history, music for one
        private bool limitWarned;
        private bool noClipWanted;
        private int iconSize = 16;
        private int lastScrollValue = -1, lastScrollMax = -1;
        private int lastVScrollValue = -1, lastVScrollMax = -1, vScrollTop = 1;

        public MainForm()
        {
            Text = AppName;
            Width = 1100;
            Height = 640;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            // The same font as the menu and strips, or the bottom looks foreign.
            if (SystemFonts.MessageBoxFont != null) Font = SystemFonts.MessageBoxFont;
            KeyPreview = true;
            MinimumSize = new Size(640, 400);

            // Strips are drawn by the system theme, not the classic gray renderer.
            ToolStripManager.VisualStylesEnabled = true;
            ToolStripManager.RenderMode = ToolStripManagerRenderMode.Professional;

            using (Graphics g = CreateGraphics())
            {
                iconSize = (int)Math.Round(16 * g.DpiX / 96.0);
                paletteIconSize = (int)Math.Round(iconSize * 1.75);   // icons in the column are larger, or they cannot be made out
            }

            // The icon comes from the exe itself, so it is not stored twice.
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            BuildMenu();
            BuildPalette();
            BuildStatusBar();

            canvas.Dock = DockStyle.Fill;
            // Undoing back to the saved step makes the level saved again, so the
            // mark is worked out from history instead of being latched.
            canvas.LevelChanged += delegate
            {
                dirty = extraDirty || !canvas.Undo.AtSavedPoint;
                UpdateStatus();
            };
            canvas.ViewChanged += delegate { SyncScrollBar(); UpdateStatus(); };
            canvas.LimitReached += delegate { WarnLimit(); };
            player.Ready += delegate
            {
                // Readiness arrives on another thread, and the window may only
                // be touched from its own.
                try { BeginInvoke((MethodInvoker)delegate { UpdateStatus(); }); }
                catch { }
            };
            canvas.SeekRequested += delegate(double worldX)
            {
                SeekTo(worldX);
            };
            canvas.PlaybackSource = delegate
            {
                return Level.PlayerOffset + player.PredictedPosition.TotalSeconds * Level.UnitsPerSecond;
            };

            vScroll.Dock = DockStyle.Right;
            vScroll.Scroll += delegate(object sender, ScrollEventArgs se)
            {
                // The scroll value grows downwards while the view rises, so the
                // value is flipped. Zero on the bar is the top of the level.
                int offset = Math.Max(0, vScrollTop - se.NewValue);
                if (se.Type == ScrollEventType.ThumbTrack || se.Type == ScrollEventType.ThumbPosition)
                    canvas.OffsetY = offset;
                else
                    canvas.SetTarget(canvas.OffsetX, offset);
            };

            hScroll.Dock = DockStyle.Bottom;
            hScroll.Scroll += delegate(object sender, ScrollEventArgs se)
            {
                // dragging the thumb is instant, arrows and clicks are smooth
                if (se.Type == ScrollEventType.ThumbTrack || se.Type == ScrollEventType.ThumbPosition)
                    canvas.OffsetX = se.NewValue;
                else
                    canvas.SetTarget(se.NewValue, canvas.OffsetY);
            };

            // Order matters: layout runs from the end of the list, so the menu
            // ends up on top, the palette on the left and the canvas takes the rest.
            Controls.Add(canvas);
            Controls.Add(vScroll);
            Controls.Add(hScroll);
            Controls.Add(statusBar);
            Controls.Add(palette);
            Controls.Add(menu);
            MainMenuStrip = menu;

            Application.AddMessageFilter(this);


            playTimer.Interval = 16;
            playTimer.Tick += delegate { OnPlayTick(); };

            canvas.SetRowsInstant(settings.GetInt("Rows", GridCanvas.DefaultRows));
            canvas.InvertScroll = settings.GetInt("InvertScroll", 0) != 0;
            canvas.InvertZoom = settings.GetInt("InvertZoom", 0) != 0;
            followLine = settings.GetInt("FollowLine", 1) != 0;
            canvas.InvertDrawMode = settings.GetInt("InvertDrawMode", 0) != 0;
            canvas.PureBlackBackground = settings.GetInt("PureBlackBackground", 0) != 0;
            noClipWanted = settings.GetInt("NoClip", 0) != 0;

            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            int w = settings.GetInt("WindowWidth", 0), hgt = settings.GetInt("WindowHeight", 0);
            if (w > 400 && hgt > 300) { Width = w; Height = hgt; }

            BuildHotkeys();
            miNoClip.Checked = noClipWanted;
            SetBackgroundColor(settings.GetInt("BackgroundColor", 0));
            SetEffect(0);
            SelectTool(Tool.Block);
            UpdateTitle();
            UpdateStatus();
        }

        // ---------- interface ----------

        private ToolStripButton AddTool(IconKind icon, string tip, Tool tool, ContextMenuStrip menu)
        {
            ToolStripButton b = new ToolStripButton();
            b.Image = Icons.Make(icon, paletteIconSize);
            b.ToolTipText = tip;
            b.DisplayStyle = ToolStripItemDisplayStyle.Image;
            // Without this the button stretches to the column height, icon and all.
            b.ImageScaling = ToolStripItemImageScaling.None;
            b.AutoSize = false;
            b.Size = new Size(paletteIconSize + 8, paletteIconSize + 8);
            b.Margin = new Padding(0, 1, 0, 1);
            // The tip likes to pop over the options dropdown, so a press
            // hides it at once.
            b.MouseDown += delegate { HideToolTip(palette); };
            b.Click += delegate
            {
                bool fromKeyboard = palette.ContainsFocus;
                SelectTool(tool);

                // Pressed with the mouse: work goes on in the canvas, so focus
                // goes back there.
                if (!fromKeyboard) { canvas.Focus(); return; }

                // From the keyboard we stay on the strip and on the same button:
                // after a press the strip moves the choice to the first one by
                // itself, and it would have to be walked through again.
                BeginInvoke((MethodInvoker)delegate { b.Select(); });
            };
            if (menu != null)
                b.MouseDown += delegate(object sender, MouseEventArgs e)
                {
                    // The right button on a tool opens its options.
                    if (e.Button != MouseButtons.Right) return;
                    SelectTool(tool);
                    menu.Show(palette, new Point(b.Bounds.Right, b.Bounds.Top));
                };
            if (menu != null) toolMenus[b] = menu;
            palette.Items.Add(b);
            toolButtons.Add(b);
            toolOrder.Add(tool);
            return b;
        }

        // ToolStrip has no public way to hide a tip that is already showing,
        // so we reach for its internal ToolTip.
        private static void HideToolTip(ToolStrip strip)
        {
            try
            {
                System.Reflection.PropertyInfo prop = typeof(ToolStrip).GetProperty("ToolTip",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (prop == null) return;
                ToolTip tip = prop.GetValue(strip, null) as ToolTip;
                if (tip == null) return;
                tip.Hide(strip);
                tip.Active = false;
                tip.Active = true;
            }
            catch { }
        }

        private ToolStripMenuItem AddMenuItem(ToolStripMenuItem parent, string text,
                                              string shortcut, MethodInvoker action)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            if (shortcut != null)
            {
                item.ShowShortcutKeys = true;
                item.ShortcutKeyDisplayString = shortcut;   // a label only, the handling is ours
            }
            if (action != null) item.Click += delegate { action(); };
            parent.DropDownItems.Add(item);
            return item;
        }

        private void BuildMenu()
        {
            menu.Dock = DockStyle.Top;
            // The professional renderer with system theme colours: the system
            // one paints a harsh bright blue highlight, this one a soft glow.
            ProfessionalColorTable colors = new ProfessionalColorTable();
            colors.UseSystemColors = true;
            menu.Renderer = new ToolStripProfessionalRenderer(colors);

            ToolStripMenuItem file = new ToolStripMenuItem(S.MenuFile);
            AddMenuItem(file, S.MenuNew, "Ctrl+N", delegate { NewLevel(); });
            AddMenuItem(file, S.MenuOpen, "Ctrl+O", delegate { OpenLevel(); });
            AddMenuItem(file, S.MenuSave, "Ctrl+S", delegate { SaveLevel(false); });
            AddMenuItem(file, S.MenuSaveAs, "Ctrl+Shift+S", delegate { SaveLevel(true); });
            file.DropDownItems.Add(new ToolStripSeparator());
            AddMenuItem(file, S.MenuSettings, null, delegate { ShowSettings(); });
            AddMenuItem(file, S.MenuExit, "Escape", delegate { Close(); });

            ToolStripMenuItem edit = new ToolStripMenuItem(S.MenuEdit);
            miUndo = AddMenuItem(edit, S.MenuUndo, "Ctrl+Z", delegate { canvas.DoUndo(); });
            miRedo = AddMenuItem(edit, S.MenuRedo, "Ctrl+Y", delegate { canvas.DoRedo(); });
            edit.DropDownItems.Add(new ToolStripSeparator());
            miCut = AddMenuItem(edit, S.MenuCut, "Ctrl+X", delegate { canvas.CutSelection(); });
            miCopy = AddMenuItem(edit, S.MenuCopy, "Ctrl+C", delegate { canvas.CopySelection(); });
            miPaste = AddMenuItem(edit, S.MenuPaste, "Ctrl+V", delegate { canvas.Paste(); });
            edit.DropDownItems.Add(new ToolStripSeparator());
            miSelectAll = AddMenuItem(edit, S.MenuSelectAll, "Ctrl+A",
                                      delegate { canvas.SelectAll(); canvas.Focus(); });
            miDelete = AddMenuItem(edit, S.MenuDeleteSelected, "Delete",
                                   delegate { canvas.DeleteSelection(); });

            // Edit items are alive only when they make sense.
            edit.DropDownOpening += delegate
            {
                miUndo.Enabled = canvas.CanUndo;
                miRedo.Enabled = canvas.CanRedo;
                miCut.Enabled = canvas.HasSelection;
                miCopy.Enabled = canvas.HasSelection;
                miDelete.Enabled = canvas.HasSelection;
                miPaste.Enabled = canvas.HasClipboard;
                miSelectAll.Enabled = canvas.HasSelectable;
            };

            ToolStripMenuItem view = new ToolStripMenuItem(S.MenuView);
            miGrid = AddMenuItem(view, S.MenuGrid, "Ctrl+G", delegate { ToggleGrid(); });
            miSnap = AddMenuItem(view, S.MenuSnap, "Ctrl+Shift+G", delegate { ToggleSnap(); });
            miGrid.Checked = true;
            miSnap.Checked = true;
            view.DropDownItems.Add(new ToolStripSeparator());
            AddMenuItem(view, S.MenuZoomReset, null,
                        delegate { canvas.RowsVisible = GridCanvas.DefaultRows; UpdateStatus(); });
            AddMenuItem(view, S.MenuGoStart, "Home", delegate { canvas.SetTarget(0, canvas.OffsetY); });
            AddMenuItem(view, S.MenuGoFinish, "End", delegate { canvas.ScrollToWorldX(canvas.Level.LevelEnd); });

            ToolStripMenuItem level = new ToolStripMenuItem(S.MenuLevel);
            AddMenuItem(level, S.MenuTest, "Enter", delegate { RunTest(); });
            AddMenuItem(level, S.MenuRunGame, "Ctrl+Enter", delegate { RunGame(); });
            level.DropDownItems.Add(new ToolStripSeparator());
            miNoClip = AddMenuItem(level, S.MenuNoClip, "F1", delegate { ToggleNoClip(); });
            miCustomArt = AddMenuItem(level, S.MenuCustomRes, "F2",
                                      delegate { ToggleCustomArt(); });
            miCustomArt.Visible = false;

            miPhysics = AddMenuItem(level, S.MenuPhysics, null, delegate { ShowPhysics(); });
            AddMenuItem(level, S.MenuPatches, null, delegate { ShowPatch(); });
            level.DropDownItems.Add(new ToolStripSeparator());
            miLevelFolder = AddMenuItem(level, S.MenuLevelsFolder, "",
                                        delegate { OpenLevelsFolder(); });
            AddMenuItem(level, S.MenuChooseMusic, "Ctrl+M", delegate { ChooseMusic(); });
            level.DropDownOpening += delegate
            {
                miLevelFolder.Text = levelFolder == null ? S.MenuLevelsFolder : S.MenuLevelFolder;
            };

            // Noclip state is read from another process, so we check it only
            // when the menu is actually opened. Noclip and level physics work
            // in a patched game only, so the items follow the patch state.
            level.DropDownOpening += delegate { SyncPatchItems(); };

            menu.Items.AddRange(new ToolStripItem[] { file, edit, view, level });
            menu.MenuActivate += delegate { menuActive = true; };
            menu.MenuDeactivate += delegate { menuActive = false; };
        }

        // Tools sit on the left in a column: levels are long and narrow, so the
        // width is hardly missed while the height is saved.
        private void BuildPalette()
        {
            palette.Dock = DockStyle.Left;
            palette.LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow;
            palette.GripStyle = ToolStripGripStyle.Hidden;
            palette.ImageScalingSize = new Size(paletteIconSize, paletteIconSize);
            palette.Padding = new Padding(2, 3, 2, 3);
            // The system renderer fills the chosen button whole, unlike the
            // professional one, which draws a thin frame.
            palette.RenderMode = ToolStripRenderMode.System;
            palette.AutoSize = false;
            palette.Width = paletteIconSize + 14;
            // The strip is a single tab stop, and inside it the buttons are
            // walked with arrows, as is usual in Windows.
            palette.TabStop = true;

            // The right arrow opens tool options, same as the right mouse
            // button: colours for the background, kinds for the effects.
            palette.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Right) return;
                foreach (ToolStripItem item in palette.Items)
                {
                    ToolStripButton b = item as ToolStripButton;
                    if (b == null || !b.Selected) continue;
                    ContextMenuStrip m;
                    if (!toolMenus.TryGetValue(b, out m) || m == null) return;
                    SelectTool(canvas.CurrentTool);
                    m.Show(palette, new Point(b.Bounds.Right, b.Bounds.Top));
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }
            };

            BuildToolMenus();

            btnBlock = AddTool(IconKind.Block, S.ToolBlock, Tool.Block, null);
            btnSpike = AddTool(IconKind.Spike, S.ToolSpike, Tool.Spike, null);
            btnPit = AddTool(IconKind.Pit, S.ToolPit, Tool.Pit, null);
            palette.Items.Add(new ToolStripSeparator());
            btnDelete = AddTool(IconKind.Delete, S.ToolErase, Tool.Delete, null);
            btnSelect = AddTool(IconKind.Select, S.ToolSelect, Tool.Select, null);
            palette.Items.Add(new ToolStripSeparator());
            btnBackground = AddTool(IconKind.Settings, S.ToolBackground,
                                    Tool.Background, bgMenu);
            btnGravity = AddTool(IconKind.Gravity, S.ToolGravity, Tool.Gravity, null);
            btnEffect = AddTool(IconKind.Effect, S.ToolEffect,
                                Tool.Effect, effectMenu);
            palette.Items.Add(new ToolStripSeparator());
            btnFinish = AddTool(IconKind.Finish, S.ToolFinish, Tool.LevelEnd, null);
        }

        private void BuildToolMenus()
        {
            bgMenu = new ContextMenuStrip();
            for (int i = 0; i < GridCanvas.BgNames.Length; i++)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(GridCanvas.BgNames[i]);
                item.Image = Icons.MakeBackground(iconSize, canvas.BackgroundSampleFor(i));
                item.Tag = i;
                item.Click += delegate(object sender, EventArgs e)
                {
                    SetBackgroundColor((int)((ToolStripMenuItem)sender).Tag);
                };
                bgMenu.Items.Add(item);
            }

            effectMenu = new ContextMenuStrip();
            string[] names = { S.EffectFallName, S.EffectRiseName };
            for (int i = 0; i < names.Length; i++)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(names[i]);
                item.Image = Icons.Make(i == 1 ? IconKind.EffectRise : IconKind.Effect, iconSize);
                item.Tag = i;
                item.Click += delegate(object sender, EventArgs e)
                {
                    SetEffect((int)((ToolStripMenuItem)sender).Tag);
                };
                effectMenu.Items.Add(item);
            }

            // The details item sits past the rule and does not count as a
            // choice: Z and X do not walk onto it.
            effectMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem more = new ToolStripMenuItem(S.EffectMore);
            more.Click += delegate
            {
                using (EffectsDialog dlg = new EffectsDialog()) dlg.ShowDialog(this);
            };
            effectMenu.Items.Add(more);
        }

        private void SetEffect(int index)
        {
            canvas.EffectIsRise = index == 1;
            foreach (ToolStripItem item in effectMenu.Items)
            {
                ToolStripMenuItem mi = item as ToolStripMenuItem;
                if (mi != null && mi.Tag != null) mi.Checked = (int)mi.Tag == index;
            }
            btnEffect.ToolTipText = string.Format(S.ToolEffectNamed, (canvas.EffectIsRise ? S.EffectRiseName : S.EffectFallName));

            // The button icon shows the chosen effect. The old one must not be
            // freed: icons live in a shared cache and the same one may be
            // drawn in the menu.
            btnEffect.Image = Icons.Make(
                canvas.EffectIsRise ? IconKind.EffectRise : IconKind.Effect, paletteIconSize);
        }

        // The wheel over the tool column walks them from top to bottom.
        private void ScrollTools(int delta)
        {
            int index = toolOrder.IndexOf(canvas.CurrentTool);
            if (index < 0) index = 0;
            index += delta > 0 ? -1 : 1;
            if (index < 0) index = 0;
            if (index >= toolOrder.Count) index = toolOrder.Count - 1;
            SelectTool(toolOrder[index]);
        }


        private void BuildStatusBar()
        {
            int rowHeight = Font.Height + 10;   // a text line plus five points above and below
            statusBar.Height = rowHeight;
            statusBar.BackColor = SystemColors.Control;
            statusBar.ForeColor = SystemColors.ControlText;

            bMusic = new StatusRow.Btn { Icon = IconKind.Music, Tag = "music",
                                         Tip = S.BtnMusicTip };
            bFollow = new StatusRow.Btn { Icon = IconKind.Follow, Tag = "follow",
                                          Tip = S.BtnFollowTip, Visible = false };
            bPlay = new StatusRow.Btn { Icon = IconKind.Play, Tag = "play",
                                        Tip = S.BtnPlayTip, Visible = false };
            bStart = new StatusRow.Btn { Icon = IconKind.StartFlag, Tag = "start",
                                         Tip = S.BtnStartTip, Visible = false };
            bStop = new StatusRow.Btn { Icon = IconKind.Stop, Tag = "stop",
                                        Tip = S.BtnStopTip, Visible = false };
            statusBar.SetButtons(bMusic, bFollow, bPlay, bStart, bStop);
            statusBar.ButtonClick += delegate(object tag)
            {
                switch ((string)tag)
                {
                    case "music": ChooseMusic(); break;
                    case "follow": ToggleFollow(); break;
                    case "play": TogglePlay(); break;
                    case "start": ToggleStartPlacing(); break;
                    case "stop": StopMusic(); break;
                }
            };

            statusBar.CountDetails += delegate
            {
                using (CountsDialog dlg = new CountsDialog(canvas.Level)) dlg.ShowDialog(this);
            };
            statusBar.ZoomReset += delegate
            {
                canvas.RowsVisible = GridCanvas.DefaultRows;
                UpdateStatus();
            };
            statusBar.MouseDown += delegate { canvas.Focus(); };

            canvas.StartRequested += delegate(double worldX)
            {
                canvas.StartX = worldX;
                // The mode lasts for one placement: after that the grid works
                // as usual and there is nothing to switch off by hand.
                SetStartPlacing(false);
                UpdateStatus();
            };
        }

        private void SetBackgroundColor(int id)
        {
            canvas.CurrentBgId = id;
            btnBackground.Image = Icons.MakeBackground(paletteIconSize, canvas.BackgroundSampleFor(id));
            btnBackground.ToolTipText = string.Format(S.ToolBackgroundNamed, GridCanvas.BgNames[id]);
            foreach (ToolStripItem item in bgMenu.Items)
            {
                ToolStripMenuItem mi = item as ToolStripMenuItem;
                if (mi != null) mi.Checked = (int)mi.Tag == id;
            }
        }

        private void SelectTool(Tool t)
        {
            canvas.CurrentTool = t;
            btnBlock.Checked = t == Tool.Block;
            btnSpike.Checked = t == Tool.Spike;
            btnPit.Checked = t == Tool.Pit;
            btnDelete.Checked = t == Tool.Delete;
            btnFinish.Checked = t == Tool.LevelEnd;
            btnSelect.Checked = t == Tool.Select;
            btnBackground.Checked = t == Tool.Background;
            btnGravity.Checked = t == Tool.Gravity;
            btnEffect.Checked = t == Tool.Effect;
        }

        private void UpdateTitle()
        {
            if (levelFolder == null)
            {
                Text = AppName + (dirty ? " *" : "");
                return;
            }
            string name = Path.GetFileNameWithoutExtension(Path.GetFileName(levelFolder.TrimEnd('\\', '/')));
            Text = name + (dirty ? " *" : "") + " - " + AppName;
        }

        // Updating the row is cheap: changing labels keep a constant width, so
        // the layout is not redone, and SetText leaves quietly when the text is
        // the same. There is nothing to rate limit.
        private void UpdateStatus()
        {
            UpdateStatusNow();
        }

        private void UpdateStatusNow()
        {
            int n = canvas.ObjectCount;
            statusBar.CountText = S.StatusObjects + n + " / " + Level.MaxObjects;
            statusBar.CountColor = n > 32000 ? Color.Firebrick
                                 : n > 30000 ? Color.DarkGoldenrod : SystemColors.ControlText;

            statusBar.ZoomText = S.StatusZoom + canvas.RowsVisible;

            int cellX, row;
            bool overGrid = canvas.TryGetCursorCell(out cellX, out row);
            statusBar.SizeShort = overGrid ? cellX + ", " + row : "";
            statusBar.SizeText = overGrid ? string.Format(S.StatusSize, cellX, row) : "";

            // On the left in one line: the time under the pointer and the level
            // duration, that is the way from the cube to the finish.
            string total = FormatSeconds(
                (canvas.Level.LevelEnd - Level.PlayerOffset) / (double)Level.UnitsPerSecond);
            string at = overGrid ? TimeAt(canvas.CursorWorldX) : "0:00.0";
            statusBar.TimeText = at + " / " + total;

            statusBar.TrackText = player.IsLoaded
                ? FormatTime(player.Position) + " / " + FormatTime(player.Duration)
                : player.IsPreparing ? S.StatusMusicLoading : "";

            statusBar.LevelHint = levelFolder;
            statusBar.LevelText = levelFolder == null
                ? S.TitleUnsaved
                : Path.GetFileName(levelFolder.TrimEnd('\\', '/'));

            // With no music there is a single note in its place, and a press
            // opens the file chooser. While playing the button shows a pause,
            // otherwise a triangle. Following shows as a pressed button.
            IconKind want = player.IsPlaying ? IconKind.Pause : IconKind.Play;
            if (bPlay.Visible != player.IsLoaded || bPlay.Icon != want
                || bFollow.Checked != followLine || bStart.Checked != canvas.PlacingStart)
            {
                bMusic.Visible = !player.IsLoaded;
                bPlay.Visible = player.IsLoaded;
                bStop.Visible = player.IsLoaded;
                bStart.Visible = player.IsLoaded;
                bFollow.Visible = player.IsLoaded;
                bPlay.Icon = want;
                bFollow.Checked = followLine;
                bStart.Checked = canvas.PlacingStart;
                statusBar.UpdateButtons();
            }
            UpdateTitle();
        }



        private void SyncScrollBar()
        {
            int width = canvas.LevelWidth();
            int large = Math.Max(1, (int)(canvas.ClientSize.Width / canvas.ViewScale));
            int max = Math.Max(1, width);
            if (max != lastScrollMax || hScroll.LargeChange != large)
            {
                hScroll.Minimum = 0;
                hScroll.Maximum = max;
                hScroll.LargeChange = large;
                hScroll.SmallChange = Level.Grid;
                lastScrollMax = max;
            }
            int v = Math.Max(0, Math.Min(canvas.OffsetX, hScroll.Maximum - hScroll.LargeChange + 1));
            if (v != lastScrollValue)
            {
                hScroll.Value = v;
                lastScrollValue = v;
            }

            SyncVScrollBar();
        }

        private void SyncVScrollBar()
        {
            int content = Math.Max(1, canvas.LevelHeight());
            int large = Math.Max(1, (int)(canvas.ClientSize.Height / canvas.ViewScale));

            // Nothing to scroll: the level is empty or fits the window whole.
            bool needed = content > large;
            if (vScroll.Visible != needed)
            {
                vScroll.Visible = needed;
                // While the bar was hidden its position went stale: show it
                // where we actually are, not where we were.
                lastVScrollValue = -1;
                lastVScrollMax = -1;
            }
            if (!needed) return;

            // The thumb must reach both the floor and the ceiling. The value a
            // bar can reach is Maximum minus the page plus one, hence the limit.
            int top = Math.Max(0, content - large);
            int max = top + large - 1;
            if (max != lastVScrollMax || vScroll.LargeChange != large)
            {
                vScroll.Minimum = 0;
                vScroll.LargeChange = large;
                vScroll.Maximum = max;
                vScroll.SmallChange = Level.Grid;
                lastVScrollMax = max;
                vScrollTop = top;
            }

            int v = Math.Max(0, Math.Min(top - canvas.OffsetY, top));
            if (v > vScroll.Maximum) v = vScroll.Maximum;
            if (v != lastVScrollValue)
            {
                vScroll.Value = v;
                lastVScrollValue = v;
            }
        }

        private void WarnLimit()
        {
            if (limitWarned) return;
            limitWarned = true;
            MessageBox.Show(this,
                string.Format(S.LimitReached, Level.MaxObjects),
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }


        // ---------- mouse wheel ----------

        private const int WmMouseWheel = 0x020A;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WmMouseWheel) return false;

            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            Point screen = Cursor.Position;

            if (palette.Visible && palette.RectangleToScreen(palette.ClientRectangle).Contains(screen))
            {
                ScrollTools(delta);
                return true;
            }
            if (statusBar.ZoomHit(statusBar.PointToClient(screen)))
            {
                // Same as on the grid: up means a larger number, and the invert
                // setting turns that around.
                int step = delta / 120;
                if (canvas.InvertZoom) step = -step;
                canvas.RowsVisible = canvas.RowsVisible + step;
                UpdateStatus();
                return true;
            }
            if (canvas.RectangleToScreen(canvas.ClientRectangle).Contains(screen))
            {
                canvas.HandleWheel(delta);
                return true;
            }
            return false;
        }

        // ---------- files ----------

        private bool ConfirmDiscard()
        {
            if (!dirty) return true;
            // An empty level has nothing to lose, so there is nothing to ask.
            if (levelFolder == null && !canvas.HasAnyContent) return true;
            DialogResult r = MessageBox.Show(this,
                S.AskSaveChanges,
                AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return SaveLevel(false);
            return true;
        }

        private void NewLevel()
        {
            canvas.DropFloating();
            if (!ConfirmDiscard()) return;
            canvas.SetLevel(new Level());
            levelFolder = null;
            musicPath = null;
            player.Unload();          // the previous level music is no longer wanted
            LoadLevelAtlas(null);
            extraDirty = false;
            dirty = false;
            limitWarned = false;
            UpdateStatus();
        }

        // Opens the level folder if there is one, otherwise the levels folder.
        private void OpenLevelsFolder()
        {
            string folder = levelFolder ?? LevelFiles.LevelsFolder(settings);
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(folder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, S.ErrOpenFolder + ex.Message,
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenLevel()
        {
            canvas.DropFloating();
            if (!ConfirmDiscard()) return;
            string folder = FolderPicker.Show(this, S.PickLevelFolder, LevelFiles.LevelsFolder(settings));
            if (folder != null) LoadFromFolder(folder);
        }


        private void LoadFromFolder(string folder)
        {
            string datPath = Path.Combine(folder, LevelFiles.LevelFileName);
            if (!File.Exists(datPath))
            {
                MessageBox.Show(this, S.ErrNoLevelDat,
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                Level lv = Level.Load(datPath);
                canvas.SetLevel(lv);
                levelFolder = folder;
                musicPath = LevelFiles.FindMusic(folder);
                DetectGameNear(folder);
                LoadLevelAtlas(folder);
                LoadPhysicsFor(folder);
                LoadMusic(musicPath);
                extraDirty = false;
                dirty = false;
                limitWarned = false;
                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, S.ErrOpenLevel + ex.Message,
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }



        private bool SaveLevel(bool askName)
        {
            if (levelFolder == null || askName)
            {
                string defRoot = LevelFiles.LevelsFolder(settings);
                if (defRoot == null)
                    defRoot = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string defName = levelFolder == null
                    ? S.NewLevelName
                    : Path.GetFileNameWithoutExtension(Path.GetFileName(levelFolder.TrimEnd('\\', '/')));

                using (NameDialog dlg = new NameDialog(defName, defRoot))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                    string folder = Path.Combine(dlg.RootFolder, dlg.LevelName + ".lvl");
                    try
                    {
                        Directory.CreateDirectory(folder);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, S.ErrCreateFolder + ex.Message,
                            AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                    settings.Set("LevelsRoot", dlg.RootFolder);
                    try
                    {
                        LevelFiles.CopyExtras(levelFolder, folder);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, S.ErrCopyFiles + ex.Message,
                            AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    levelFolder = folder;
                    musicPath = LevelFiles.FindMusic(levelFolder);
                }
            }
            canvas.DropFloating();
            MoveMusicToFolder();
            SavePhysicsToLevel();
            try
            {
                canvas.Level.Save(Path.Combine(levelFolder, LevelFiles.LevelFileName));
                canvas.Undo.MarkSaved();
                extraDirty = false;
                dirty = false;
                LoadLevelAtlas(levelFolder);
                UpdateStatus();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, S.ErrSaveLevel + ex.Message,
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void ChooseMusic()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = S.MusicFilter;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                if (ext != ".mp3" && ext != ".ogg")
                {
                    MessageBox.Show(this, S.ErrMusicFormat,
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                try
                {
                    // The chosen file is only remembered. It moves into the level
                    // folder on save, and the test run copies it itself, so the
                    // music is heard there right away.
                    musicPath = dlg.FileName;
                    LoadMusic(musicPath);
                    extraDirty = true;      // music takes no part in history
                    dirty = true;
                    UpdateStatus();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, S.ErrCopyMusic + ex.Message,
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ---------- music ----------

        // In a level folder music is always named music, the other format goes.
        private string CopyMusicInto(string folder, string source)
        {
            string ext = Path.GetExtension(source).ToLowerInvariant();
            string other = Path.Combine(folder, ext == ".mp3" ? "music.ogg" : "music.mp3");
            if (File.Exists(other)) File.Delete(other);

            string target = Path.Combine(folder, "music" + ext);
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target),
                    StringComparison.OrdinalIgnoreCase))
                File.Copy(source, target, true);
            return target;
        }

        // Music chosen before saving moves into the level folder.
        private void MoveMusicToFolder()
        {
            if (musicPath == null || levelFolder == null) return;
            if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(musicPath)),
                    Path.GetFullPath(levelFolder), StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                string was = player.IsLoaded ? musicPath : null;
                player.Unload();
                musicPath = CopyMusicInto(levelFolder, musicPath);
                if (was != null) LoadMusic(musicPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, S.ErrMoveMusic + ex.Message,
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void LoadMusic(string path)
        {
            try
            {
                playTimer.Stop();
                canvas.PlaybackX = -1;
                if (path == null) { player.Unload(); return; }
                player.Load(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, S.ErrOpenMusic + ex.Message,
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void TogglePlay()
        {
            if (!player.IsLoaded)
            {
                MessageBox.Show(this, S.ErrNoMusicInFolder,
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // The green line sets the start: both the first run and the one
            // after a pause go from it.
            if (!player.IsPlaying && canvas.StartX >= 0) SeekTo(canvas.StartX);

            player.TogglePlay();
            canvas.FollowPlayback = followLine && player.IsPlaying;
            canvas.SeekOnClick = player.IsPlaying;
            if (player.IsPlaying) playTimer.Start(); else playTimer.Stop();
            UpdateStatus();
        }

        // Stop: the music halts and the line goes back to the level start.
        // The camera is let go so the view does not jump after the line.
        private void StopMusic()
        {
            if (!player.IsLoaded) return;
            if (player.IsPlaying) player.TogglePlay();
            canvas.SeekOnClick = false;
            playTimer.Stop();
            canvas.FollowPlayback = false;
            player.Rewind();
            canvas.PlaybackX = -1;      // the line goes until the next start
            canvas.StartX = -1;         // and the start mark goes with it
            SetStartPlacing(false);
            UpdateStatus();
        }

        // Seeking to a spot: the line jumps to the click at once, and the music
        // goes on from the same place if it was playing.
        private void SeekTo(double worldX)
        {
            if (!player.IsLoaded) return;
            double seconds = (worldX - Level.PlayerOffset) / Level.UnitsPerSecond;
            if (seconds < 0) seconds = 0;
            player.Seek(TimeSpan.FromSeconds(seconds));
            canvas.PlaybackX = Level.PlayerOffset + seconds * Level.UnitsPerSecond;
            canvas.SeekOnClick = player.IsPlaying;
            UpdateStatus();
        }

        // The green line by key: it lands where the pointer is.
        private void SetStartAtCursor()
        {
            Point at = canvas.PointToClient(Cursor.Position);
            if (!canvas.ClientRectangle.Contains(at)) { ToggleStartPlacing(); return; }
            canvas.StartX = canvas.ScreenToWorldX(at.X);
            SetStartPlacing(false);     // the line is placed, the mode is done
            UpdateStatus();
        }

        // The flag button turns on the mode where a click on the grid places
        // the line and the right button removes it.
        private void ToggleStartPlacing()
        {
            SetStartPlacing(!canvas.PlacingStart);
        }

        private void SetStartPlacing(bool on)
        {
            canvas.PlacingStart = on;
            if (bStart != null) bStart.Checked = on;
            statusBar.Invalidate();
        }

        // Follow the line: while on, the view travels with the music.
        private void ToggleFollow()
        {
            followLine = !followLine;
            settings.SetInt("FollowLine", followLine ? 1 : 0);
            canvas.FollowPlayback = followLine && player.IsPlaying;
            UpdateStatus();
        }

        private void OnPlayTick()
        {
            if (!player.IsPlaying)
            {
                // the music ended or was paused: the camera is let go
                playTimer.Stop();
                canvas.FollowPlayback = false;
                canvas.SeekOnClick = false;
                UpdateStatus();
                return;
            }
            UpdateStatus();   // it decides for itself whether to repaint
        }

        // The time at which the cube reaches this spot of the level.
        private static string TimeAt(int worldX)
        {
            if (worldX == int.MinValue) return "";
            double seconds = (worldX - Level.PlayerOffset) / (double)Level.UnitsPerSecond;
            return FormatSeconds(seconds < 0 ? 0 : seconds);
        }

        private static string FormatSeconds(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int minutes = (int)(seconds / 60);
            return minutes + ":" + (seconds - minutes * 60).ToString("00.0",
                System.Globalization.CultureInfo.InvariantCulture);
        }


        private static string FormatTime(TimeSpan t)
        {
            return ((int)t.TotalMinutes).ToString("0") + ":" + t.Seconds.ToString("00");
        }

        // ---------- noclip ----------

        // Noclip is editor state: it never touches the game memory and only
        // passes a flag on start. Inside the game the same mode is driven by
        // F1, which the patch adds.
        private void ToggleNoClip()
        {
            if (!NoClipPatched()) { noClipWanted = false; miNoClip.Checked = false; return; }
            noClipWanted = !noClipWanted;
            miNoClip.Checked = noClipWanted;
            settings.SetInt("NoClip", noClipWanted ? 1 : 0);
            UpdateStatus();
        }

        // ---------- jump physics ----------

        private void SavePhysicsToLevel()
        {
            if (levelFolder == null) return;
            if (PhysicsToApply == null) LevelSettings.ClearPhysics(levelFolder);
            else LevelSettings.WritePhysics(levelFolder, PhysicsToApply.Jump, PhysicsToApply.Gravity);
        }

        // The numbers from level.ini as they are. Returns false when the file
        // is missing or holds no physics: the level then jumps normally.
        private static bool PhysicsFromLevel(string folder, out float jump, out double gravity)
        {
            jump = 0;
            gravity = 0;
            string path = LevelSettings.PathFor(folder);
            if (path == null || !File.Exists(path)) return false;

            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] parts = line.Split('=', ':');
                    if (parts.Length < 2) continue;
                    string key = parts[0].Trim().ToLowerInvariant();
                    string value = parts[1].Trim().Replace(',', '.');
                    if (key == "jump") float.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out jump);
                    else if (key == "gravity") double.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out gravity);
                }
            }
            catch { }

            return jump > 0 && gravity > 0;
        }

        // Physics of the open level: its own from level.ini, or the normal one.
        private void LoadPhysicsFor(string folder)
        {
            float jump;
            double gravity;
            if (folder != null && PhysicsFromLevel(folder, out jump, out gravity))
            {
                int found = GamePhysics.IndexOf(jump, gravity);
                physicsPreset = found >= 0 ? found : GamePhysics.Presets.Length;
                canvas.Physics = found >= 0
                    ? GamePhysics.ByIndex(found)
                    : GamePhysics.Custom(jump, gravity);
            }
            else
            {
                physicsPreset = 0;
                canvas.Physics = GamePhysics.ByIndex(0);
            }
        }

        // Whether the noclip patch is applied: without it we pass no argument.
        private bool NoClipPatched()
        {
            string gamePath = settings.Get("GamePath", "");
            if (gamePath.Length == 0 || !File.Exists(gamePath)) return false;
            GamePatcher.State state = GamePatcher.Read(gamePath);
            return state.Valid && state.NoClipKey;
        }

        private void SyncPatchItems()
        {
            bool noclip = false, physics = false;
            string gamePath = settings.Get("GamePath", "");
            if (gamePath.Length > 0 && File.Exists(gamePath))
            {
                GamePatcher.State state = GamePatcher.Read(gamePath);
                if (state.Valid)
                {
                    noclip = state.NoClipKey;
                    physics = state.LevelExtras;
                }
            }

            // The patch may have been removed, so the mode goes off with it:
            // otherwise we would pass an argument the game cannot read.
            if (!noclip && noClipWanted)
            {
                noClipWanted = false;
                settings.SetInt("NoClip", 0);
                UpdateStatus();
            }
            miNoClip.Checked = noClipWanted;
            miNoClip.Enabled = noclip;
            miPhysics.Enabled = physics;
            miNoClip.ToolTipText = noclip ? "" : S.NeedPatchNoClip;
            miPhysics.ToolTipText = physics ? "" : S.NeedPatchResources;
        }


        private int physicsPreset;

        // The physics to write for the level. The normal one means "write
        // nothing"; everything else, custom values included, goes as it is.
        private GamePhysics.Preset PhysicsToApply
        {
            get { return physicsPreset == 0 ? null : canvas.Physics; }
        }

        // Physics belongs to the open level: the choice goes into level.ini
        // next to it, and the normal one takes it out of there.
        private void ShowPhysics()
        {
            GamePhysics.Preset before = canvas.Physics;
            int beforePreset = physicsPreset;

            using (PhysicsDialog dlg = new PhysicsDialog(canvas.Physics))
            {
                // The jump guide follows the dialog without waiting for OK.
                dlg.Preview += delegate(GamePhysics.Preset p) { canvas.Physics = p; };

                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    canvas.Physics = before;
                    physicsPreset = beforePreset;
                    return;
                }

                physicsPreset = dlg.Selected;
                canvas.Physics = dlg.Result;
                // This reaches the disk with the level: the setting belongs to
                // the level, not to the editor.
                extraDirty = true;
                dirty = true;
                UpdateStatus();
            }
        }

        private void ShowPatch()
        {
            string gamePath = AskGamePath();
            if (gamePath == null) return;

            using (PatchDialog dlg = new PatchDialog(gamePath))
                dlg.ShowDialog(this);
        }


        // Import runs off a global key, so at that moment the person is inside
        // the game. That is why nothing here asks anything: the level is read,
        // saved into its own folder and opened, all quietly.

        // ---------- settings, test run, drag and drop ----------

        private void ShowSettings()
        {
            using (SettingsDialog dlg = new SettingsDialog(canvas.InvertScroll, canvas.InvertZoom,
                       canvas.InvertDrawMode, canvas.PureBlackBackground,
                       settings.Get("GamePath", ""), settings.Get("Language", "")))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                canvas.InvertScroll = dlg.InvertScroll;
                canvas.InvertZoom = dlg.InvertZoom;
                canvas.InvertDrawMode = dlg.InvertDrawMode;
                canvas.PureBlackBackground = dlg.PureBlack;
                settings.SetInt("InvertScroll", dlg.InvertScroll ? 1 : 0);
                settings.SetInt("InvertDrawMode", dlg.InvertDrawMode ? 1 : 0);
                settings.SetInt("InvertZoom", dlg.InvertZoom ? 1 : 0);
                settings.SetInt("PureBlackBackground", dlg.PureBlack ? 1 : 0);
                settings.Set("GamePath", dlg.GamePath);

                // The language changes in place: the window rebuilds its labels
                // while the open level and the undo history stay untouched.
                if (dlg.Language != settings.Get("Language", ""))
                {
                    settings.Set("Language", dlg.Language);
                    S.Load(dlg.Language.Length > 0 ? dlg.Language : S.SystemLanguage());
                    ApplyLanguage();
                }
                settings.Save();
            }
        }

        // Rebuilds everything that shows text. Dialogs take their strings when
        // they open, so they need no attention.
        private void ApplyLanguage()
        {
            menu.Items.Clear();
            BuildMenu();
            palette.Items.Clear();
            BuildPalette();

            bMusic.Tip = S.BtnMusicTip;
            bFollow.Tip = S.BtnFollowTip;
            bPlay.Tip = S.BtnPlayTip;
            bStart.Tip = S.BtnStartTip;
            bStop.Tip = S.BtnStopTip;
            statusBar.UpdateButtons();

            SelectTool(canvas.CurrentTool);
            UpdateStatus();
            UpdateTitle();
        }

        private string AskGamePath()
        {
            string saved = settings.Get("GamePath", "");
            if (saved.Length > 0 && File.Exists(saved)) return saved;

            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = S.AskGamePath;
                dlg.Filter = S.GameFilter;
                dlg.InitialDirectory = LevelFiles.GuessGameFolder(settings);
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                RememberGame(dlg.FileName);
                return dlg.FileName;
            }
        }

        // Background colour icons are drawn from the same colours as the
        // background, so custom level graphics means redrawing them.
        private void RefreshBackgroundIcons()
        {
            if (bgMenu == null) return;
            foreach (ToolStripItem item in bgMenu.Items)
            {
                ToolStripMenuItem mi = item as ToolStripMenuItem;
                if (mi == null) continue;
                int id = (int)mi.Tag;
                Image old = mi.Image;
                mi.Image = Icons.MakeBackground(iconSize, canvas.BackgroundSampleFor(id));
                if (old != null) old.Dispose();
            }
            if (btnBackground != null)
            {
                Image old = btnBackground.Image;
                btnBackground.Image = Icons.MakeBackground(paletteIconSize,
                    canvas.BackgroundSampleFor(canvas.CurrentBgId));
                if (old != null) old.Dispose();
            }
        }

        // Custom level graphics: game_1.png next to the level, cut by the
        // description in res/game1.bin next to the game. No file or no known
        // game means we draw as usual.
        private void LoadLevelAtlas(string folder)
        {
            GameAtlas old = levelAtlas;
            string game = settings.Get("GamePath", "");
            levelAtlas = GameAtlas.TryLoad(folder, game.Length > 0 && File.Exists(game) ? game : null);

            // A level with its own graphics turns them on by itself, even if
            // they were off on the previous level.
            customArtOn = true;
            ApplyCustomArt();
            if (old != null) old.Dispose();
        }

        private void ToggleCustomArt()
        {
            if (levelAtlas == null) return;
            customArtOn = !customArtOn;
            ApplyCustomArt();
        }

        private void ApplyCustomArt()
        {
            canvas.Atlas = customArtOn ? levelAtlas : null;
            if (miCustomArt != null)
            {
                miCustomArt.Visible = levelAtlas != null;
                miCustomArt.Checked = customArtOn && levelAtlas != null;
            }
            RefreshBackgroundIcons();
        }

        // A level was opened: if the game is not known yet, look up the tree.
        // A level usually sits in levels next to ImpossibleGame.exe.
        private void DetectGameNear(string levelFolder)
        {
            string known = settings.Get("GamePath", "");
            if (known.Length > 0 && File.Exists(known)) return;

            string found = LevelFiles.FindGameAbove(levelFolder);
            if (found != null) RememberGame(found);
        }

        // The game was chosen: a levels folder usually sits next to it.
        private void RememberGame(string gamePath)
        {
            settings.Set("GamePath", gamePath);
            string found = LevelFiles.LevelsFolder(settings);
            if (found != null) settings.Set("LevelsRoot", found);
            settings.Save();
        }

        // The game starts the way the Java editor starts it: the working folder
        // is the game folder, the level goes to editorcache/test, and the command
        // line reads log level editorcache/test.
        private void RunGame()
        {
            string gamePath = AskGamePath();
            if (gamePath == null) return;
            try
            {
                System.Diagnostics.ProcessStartInfo psi =
                    new System.Diagnostics.ProcessStartInfo(gamePath,
                        noClipWanted && NoClipPatched() ? "noclip" : "");
                psi.WorkingDirectory = Path.GetDirectoryName(gamePath);
                psi.UseShellExecute = false;
                System.Diagnostics.Process game = System.Diagnostics.Process.Start(psi);
                GameWindow.CenterWhenReady(game, 6);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, S.ErrRunGame + ex.Message,
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RunTest()
        {
            string gamePath = AskGamePath();
            if (gamePath == null) return;
            canvas.DropFloating();

            try
            {
                System.Diagnostics.Process game =
                    GameLauncher.Run(gamePath, canvas.Level, musicPath, levelFolder,
                                     noClipWanted && NoClipPatched(), PhysicsToApply);
                GameWindow.CenterWhenReady(game, 6);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, S.ErrRunGame + ex.Message,
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // A level folder dropped on the editor or given on the command line.
        // Either the folder or a level file inside it will do.
        public static string LevelFolderFromPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            if (Directory.Exists(p))
                return LevelFiles.IsLevelFolder(p) ? p : null;
            if (File.Exists(p) && Path.GetFileName(p).ToLowerInvariant() == LevelFiles.LevelFileName)
                return Path.GetDirectoryName(p);
            return null;
        }

        public void OpenFromCommandLine(string path)
        {
            string folder = LevelFolderFromPath(path);
            if (folder != null) LoadFromFolder(folder);
        }

        private static string DroppedLevelFolder(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return null;
            string[] paths = (string[])data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0) return null;

            return LevelFolderFromPath(paths[0]);
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DroppedLevelFolder(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            string folder = DroppedLevelFolder(e.Data);
            if (folder == null) return;

            // We ask exactly as on a normal open: a refusal keeps the current
            // level and leaves the dropped one alone.
            canvas.DropFloating();
            if (!ConfirmDiscard()) return;
            LoadFromFolder(folder);
        }

        // ---------- hotkeys ----------

        // Every hotkey lives in one table, a key combination and an action.
        // They are visible as a list, they do not clash, and a key settings
        // screen can later be built from the same place.
        private readonly Dictionary<Keys, MethodInvoker> hotkeys = new Dictionary<Keys, MethodInvoker>();

        private void BuildHotkeys()
        {
            hotkeys[Keys.Control | Keys.N] = delegate { NewLevel(); };
            hotkeys[Keys.Control | Keys.O] = delegate { OpenLevel(); };
            hotkeys[Keys.Control | Keys.S] = delegate { SaveLevel(false); };
            hotkeys[Keys.Control | Keys.Shift | Keys.S] = delegate { SaveLevel(true); };
            hotkeys[Keys.Control | Keys.M] = delegate { ChooseMusic(); };
            hotkeys[Keys.Control | Keys.A] = delegate { canvas.SelectAll(); canvas.Focus(); };
            hotkeys[Keys.Control | Keys.Z] = delegate { canvas.DoUndo(); };
            hotkeys[Keys.Control | Keys.Shift | Keys.Z] = delegate { canvas.DoRedo(); };
            hotkeys[Keys.Control | Keys.Y] = delegate { canvas.DoRedo(); };
            hotkeys[Keys.Control | Keys.C] = delegate { canvas.CopySelection(); };
            hotkeys[Keys.Control | Keys.X] = delegate { canvas.CutSelection(); };
            hotkeys[Keys.Control | Keys.V] = delegate { canvas.Paste(); };
            hotkeys[Keys.Control | Keys.G] = delegate { ToggleGrid(); };
            hotkeys[Keys.Control | Keys.Shift | Keys.G] = delegate { ToggleSnap(); };

            // The digits follow the order of the buttons in the left column.
            hotkeys[Keys.D1] = delegate { SelectTool(Tool.Block); };
            hotkeys[Keys.D2] = delegate { SelectTool(Tool.Spike); };
            hotkeys[Keys.D3] = delegate { SelectTool(Tool.Pit); };
            hotkeys[Keys.D4] = delegate { SelectTool(Tool.Delete); };
            hotkeys[Keys.Z] = delegate { CycleToolChoice(-1); };
            hotkeys[Keys.X] = delegate { CycleToolChoice(1); };
            hotkeys[Keys.D5] = delegate { SelectTool(Tool.Select); };
            hotkeys[Keys.D6] = delegate { SelectTool(Tool.Background); };
            hotkeys[Keys.D7] = delegate { SelectTool(Tool.Gravity); };
            hotkeys[Keys.D8] = delegate { SelectTool(Tool.Effect); };
            hotkeys[Keys.D9] = delegate { SelectTool(Tool.LevelEnd); };
            hotkeys[Keys.S] = delegate { SelectTool(Tool.Select); };

            hotkeys[Keys.F1] = delegate { ToggleNoClip(); };
            hotkeys[Keys.F2] = delegate { ToggleCustomArt(); };
            hotkeys[Keys.Enter] = delegate { RunTest(); };
            hotkeys[Keys.Control | Keys.Enter] = delegate { RunGame(); };
            // Four keys in a row, in the order of the buttons on the row.
            hotkeys[Keys.Q] = delegate { ToggleFollow(); };
            hotkeys[Keys.W] = delegate { TogglePlay(); };
            hotkeys[Keys.E] = delegate { SetStartAtCursor(); };
            hotkeys[Keys.R] = delegate { StopMusic(); };

            hotkeys[Keys.Delete] = delegate { canvas.DeleteSelection(); };
            hotkeys[Keys.Home] = delegate { canvas.SetTarget(0, canvas.OffsetY); };
            hotkeys[Keys.End] = delegate { canvas.ScrollToWorldX(canvas.Level.LevelEnd); };
            hotkeys[Keys.Escape] = delegate { Close(); };

            // arrows: plain, a large step with Shift, a pixel with Ctrl
            Keys[] arrows = { Keys.Left, Keys.Right, Keys.Up, Keys.Down };
            foreach (Keys a in arrows)
            {
                Keys arrow = a;
                hotkeys[arrow] = delegate { HandleArrow(arrow, false); };
                hotkeys[arrow | Keys.Shift] = delegate { HandleArrow(arrow, true); };
                hotkeys[arrow | Keys.Control] = delegate { HandleArrow(arrow, false); };
                // Alt with an arrow drags a copy and leaves the original
                hotkeys[arrow | Keys.Alt] = delegate { canvas.DuplicateSelectionForMove(); HandleArrow(arrow, false); };
                hotkeys[arrow | Keys.Alt | Keys.Shift] = delegate { canvas.DuplicateSelectionForMove(); HandleArrow(arrow, true); };
            }
        }

        private void ToggleGrid()
        {
            canvas.ShowGrid = !canvas.ShowGrid;
            miGrid.Checked = canvas.ShowGrid;
        }

        private void ToggleSnap()
        {
            canvas.SnapToGrid = !canvas.SnapToGrid;
            miSnap.Checked = canvas.SnapToGrid;
        }

        // Arrows, space and the like are wanted by the menu and the strips too,
        // so the editor takes them only while the canvas is in use. Otherwise
        // the menu could not be walked: scrolling would swallow everything.
        private static bool NavigationKey(Keys key)
        {
            switch (key & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.Home: case Keys.End: case Keys.Space:
                case Keys.Enter: case Keys.Tab: case Keys.Escape:
                    return true;
            }
            return false;
        }

        // An open menu does not take focus, so asking canvas.Focused is not
        // enough: while the menu is open the arrows belong to it.

        // Every keyboard oddity had one cause: keys were grabbed by trusting
        // focus and menu events, which say different things in different states.
        // So we ask plainly whether any dropdown is open. While one is, the keys
        // are not ours.
        private static bool AnyDropDownOpen(ToolStrip strip)
        {
            if (strip == null) return false;
            foreach (ToolStripItem item in strip.Items)
            {
                ToolStripDropDownItem dd = item as ToolStripDropDownItem;
                if (dd != null && dd.DropDown != null && dd.DropDown.Visible) return true;
            }
            return false;
        }

        private bool CanvasHasKeys
        {
            get
            {
                if (!canvas.Focused) return false;
                if (menuActive || menu.ContainsFocus || palette.ContainsFocus) return false;
                if (AnyDropDownOpen(menu) || AnyDropDownOpen(palette)) return false;
                if (bgMenu != null && bgMenu.Visible) return false;
                if (effectMenu != null && effectMenu.Visible) return false;
                return true;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (NavigationKey(keyData) && !CanvasHasKeys)
                return base.ProcessCmdKey(ref msg, keyData);

            MethodInvoker action;
            if (hotkeys.TryGetValue(keyData, out action))
            {
                action();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // X and Z walk whatever the current tool draws, background colours or
        // stripe kinds. For the other tools the keys stay silent.
        private bool CycleToolChoice(int delta)
        {
            if (canvas.CurrentTool == Tool.Background)
            {
                int count = GridCanvas.BgNames.Length;
                SetBackgroundColor(((canvas.CurrentBgId + delta) % count + count) % count);
                return true;
            }
            if (canvas.CurrentTool == Tool.Effect)
            {
                SetEffect(canvas.EffectIsRise ? 0 : 1);
                return true;
            }
            return false;
        }

        private void HandleArrow(Keys key, bool shift)
        {
            bool ctrl = (ModifierKeys & Keys.Control) != 0;
            if (canvas.Selection.Count > 0)
            {
                // Ctrl moves by a pixel, Shift by five cells, otherwise half a cell.
                int step = ctrl ? 1 : (shift ? Level.Grid * 5 : Level.Grid / 2);
                if (key == Keys.Left) canvas.MoveSelection(-step, 0);
                else if (key == Keys.Right) canvas.MoveSelection(step, 0);
                else if (key == Keys.Up) canvas.MoveSelection(0, step);
                else canvas.MoveSelection(0, -step);
                return;
            }

            // With nothing selected the arrows drive the camera, the same on
            // both axes, and Shift doubles the step.
            int scroll = Level.Grid * 9 / 2 * (shift ? 2 : 1);
            if (key == Keys.Left) canvas.ScrollBy(-scroll, 0);
            else if (key == Keys.Right) canvas.ScrollBy(scroll, 0);
            else if (key == Keys.Up) canvas.ScrollBy(0, scroll);
            else canvas.ScrollBy(0, -scroll);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ConfirmDiscard()) { e.Cancel = true; base.OnFormClosing(e); return; }
            settings.SetInt("Rows", canvas.RowsVisible);
            settings.SetInt("BackgroundColor", canvas.CurrentBgId);
            settings.SetInt("WindowWidth", WindowState == FormWindowState.Normal ? Width : RestoreBounds.Width);
            settings.SetInt("WindowHeight", WindowState == FormWindowState.Normal ? Height : RestoreBounds.Height);
            settings.Save();
            Application.RemoveMessageFilter(this);
            canvas.FollowPlayback = false;
            player.Dispose();
            base.OnFormClosing(e);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            canvas.Focus();
            SyncScrollBar();
        }
    }
}

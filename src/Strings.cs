using System;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Windows.Forms;

// The interface text lives in lang/en.xml and is loaded at start up. Only the
// names are here: the code refers to them, while a translation edits the XML
// and needs no rebuild.
//
// The English file is embedded in the program, so it works without a lang
// folder. A lang folder next to it with the wanted language overrides that.

namespace TigEditor
{
    public static class S
    {

        // Every visible string lives here, so translating the editor takes no changes

        // anywhere else. Keys on the left are what the code refers to; change only

        // the text inside the quotes.

        // Strings are grouped by window and keep the order they appear in, so related

        // labels sit together. Repeated text shares one key: "Close" in three windows

        // is a single entry.

        // About window
        public static string AboutTitle = "";
        public static string AboutVersion = "";
        public static string AboutAuthor = "";
        public static string AboutSummary = "";
        public static string BtnClose = "";

        // Sound
        public static string ErrAudioFormat = "";
        public static string ErrAudioDevice = "";

        // Level objects window
        public static string CountsTitle = "";
        public static string MeasureSample = "";
        public static string CountsBlocks = "";
        public static string CountsSpikes = "";
        public static string CountsPits = "";
        public static string CountsTotal = "";
        public static string CountsBackgrounds = "";
        public static string CountsGravity = "";
        public static string CountsFallStripes = "";
        public static string CountsRiseStripes = "";

        // Effects window
        public static string EffectsTitle = "";
        public static string EffectsIntro = "";
        public static string EffectsFallHead = "";
        public static string EffectsFallText = "";
        public static string EffectsFallWhy = "";
        public static string EffectsFallExample = "";
        public static string EffectsRiseHead = "";
        public static string EffectsRiseLength = "";
        public static string EffectsRiseRules = "";
        public static string EffectsRiseExample = "";

        // Game code patching
        public static string ErrBadHeaders = "";
        public static string ErrNoSectionRoom = "";

        // Patch errors
        public static string ErrFileTooSmall = "";
        public static string ErrUnknownBuild = "";
        public static string ErrBackupFailed = "";
        public static string ErrPatchCheck = "";
        public static string ErrWriteGame = "";
        public static string ErrCloseGameHint = "";
        public static string ErrNoBackup = "";
        public static string ErrRestoreFailed = "";

        // Jump physics presets
        public static string PhysPresetNormal = "";
        public static string PhysPresetFar = "";
        public static string PhysPresetHigh = "";
        public static string PhysPresetCustom = "";

        // Background colours
        public static string BgBlue = "";
        public static string BgYellow = "";
        public static string BgGreen = "";
        public static string BgViolet = "";
        public static string BgPink = "";
        public static string BgBlack = "";

        // Opening level files
        public static string ErrNoPath = "";
        public static string ErrNotLevelFile = "";
        public static string ErrNoSuchPath = "";
        public static string ErrFolderNotLevel = "";
        public static string ErrLevelRead = "";
        public static string ErrLevelBroken = "";

        // Level format reading and writing
        public static string ErrFormatVersion = "";
        public static string ErrObjectType = "";
        public static string ErrBackgroundType = "";
        public static string ErrTooManyObjects = "";

        // Main window: menu, tools, status row and messages
        public static string MenuFile = "";
        public static string MenuNew = "";
        public static string MenuOpen = "";
        public static string MenuSave = "";
        public static string MenuSaveAs = "";
        public static string MenuSettings = "";
        public static string MenuExit = "";
        public static string MenuEdit = "";
        public static string MenuUndo = "";
        public static string MenuRedo = "";
        public static string MenuCut = "";
        public static string MenuCopy = "";
        public static string MenuPaste = "";
        public static string MenuSelectAll = "";
        public static string MenuDeleteSelected = "";
        public static string MenuView = "";
        public static string MenuGrid = "";
        public static string MenuSnap = "";
        public static string MenuZoomReset = "";
        public static string MenuGoStart = "";
        public static string MenuGoFinish = "";
        public static string MenuLevel = "";
        public static string MenuTest = "";
        public static string MenuRunGame = "";
        public static string MenuNoClip = "";
        public static string MenuCustomRes = "";
        public static string MenuPhysics = "";
        public static string MenuPatches = "";
        public static string MenuLevelsFolder = "";
        public static string MenuChooseMusic = "";
        public static string MenuLevelFolder = "";
        public static string ToolBlock = "";
        public static string ToolSpike = "";
        public static string ToolPit = "";
        public static string ToolErase = "";
        public static string ToolSelect = "";
        public static string ToolBackground = "";
        public static string ToolGravity = "";
        public static string ToolEffect = "";
        public static string ToolFinish = "";
        public static string EffectFallName = "";
        public static string EffectRiseName = "";
        public static string EffectMore = "";
        public static string ToolEffectNamed = "";
        public static string BtnMusicTip = "";
        public static string BtnFollowTip = "";
        public static string BtnPlayTip = "";
        public static string BtnStartTip = "";
        public static string BtnStopTip = "";
        public static string ToolBackgroundNamed = "";
        public static string StatusObjects = "";
        public static string StatusZoom = "";
        public static string StatusSize = "";
        public static string StatusMusicLoading = "";
        public static string TitleUnsaved = "";
        public static string LimitReached = "";
        public static string AskSaveChanges = "";
        public static string ErrOpenFolder = "";
        public static string PickLevelFolder = "";
        public static string ErrNoLevelDat = "";
        public static string ErrOpenLevel = "";
        public static string NewLevelName = "";
        public static string ErrCreateFolder = "";
        public static string ErrCopyFiles = "";
        public static string ErrSaveLevel = "";
        public static string MusicFilter = "";
        public static string ErrMusicFormat = "";
        public static string ErrCopyMusic = "";
        public static string ErrMoveMusic = "";
        public static string ErrOpenMusic = "";
        public static string ErrNoMusicInFolder = "";
        public static string NeedPatchNoClip = "";
        public static string NeedPatchResources = "";
        public static string AskGamePath = "";
        public static string GameFilter = "";
        public static string ErrRunGame = "";

        // Game patches window
        public static string PatchTitle = "";
        public static string PatchIntro = "";
        public static string PatchNoClipText = "";
        public static string PatchResourcesName = "";
        public static string PatchResourcesText = "";
        public static string PatchMedalName = "";
        public static string PatchMedalText = "";
        public static string PatchFolderName = "";
        public static string PatchFolderText = "";
        public static string PatchSyncName = "";
        public static string PatchSyncText = "";
        public static string PatchDpiName = "";
        public static string PatchDpiText = "";
        public static string BtnApply = "";
        public static string BtnRestore = "";
        public static string PatchHoldLabel = "";
        public static string ErrReadGame = "";

        // Jump physics window
        public static string PhysicsTitle = "";
        public static string PhysicsIntro = "";
        public static string PhysicsJumpLabel = "";
        public static string PhysicsCustom = "";
        public static string PhysicsHeight = "";
        public static string PhysicsLength = "";
        public static string PhysicsFlightSample = "";
        public static string PhysicsModeShape = "";
        public static string PhysicsModeGame = "";
        public static string PhysicsHint = "";
        public static string BtnCancel = "";
        public static string BtnOk = "";
        public static string PhysicsJumpField = "";
        public static string PhysicsGravityField = "";
        public static string PhysicsFlight = "";

        // Other
        public static string Unknown = "";

        // Save level window
        public static string SaveTitle = "";
        public static string SaveNameLabel = "";
        public static string SaveFolderLabel = "";
        public static string BtnBrowse = "";
        public static string SaveFolderPrompt = "";
        public static string SaveHint = "";
        public static string ErrBadName = "";
        public static string ErrNoLevelsFolder = "";

        // Settings window
        public static string SettingsTitle = "";
        public static string SettingsInvert = "";
        public static string SettingsInvertScroll = "";
        public static string SettingsInvertZoom = "";
        public static string SettingsInvertDraw = "";
        public static string SettingsBlack = "";
        public static string SettingsGamePath = "";
        public static string SettingsLanguage = "";
        public static string SettingsLanguageSystem = "";

        // A language is named in itself, so it is recognised by people who do
        // not read the current language of the window.
        public static string LanguageName(string code)
        {
            switch (code)
            {
                case "en": return "English";
                case "ru": return "\u0420\u0443\u0441\u0441\u043a\u0438\u0439";
                default: return code;
            }
        }

        // Undo action names
        public static string UndoAdd = "";
        public static string UndoDelete = "";
        public static string UndoMove = "";
        public static string UndoMoveSelection = "";
        public static string UndoGravity = "";
        public static string UndoBackground = "";
        public static string UndoEffect = "";
        public static string UndoFinish = "";

        // The languages that can be chosen: the embedded English and whatever
        // lies in the lang folder next to the program.
        public static string[] Available()
        {
            System.Collections.Generic.List<string> list =
                new System.Collections.Generic.List<string>();
            list.Add("en");
            try
            {
                string dir = Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath), "lang");
                if (Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir, "*.xml"))
                    {
                        string code = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        if (!list.Contains(code)) list.Add(code);
                    }
            }
            catch { }
            list.Sort();
            return list.ToArray();
        }

        // The system language when there is a file for it, English otherwise.
        public static string SystemLanguage()
        {
            string code = System.Globalization.CultureInfo
                .CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            foreach (string have in Available())
                if (have == code) return code;
            return "en";
        }

        // Read the embedded English, then the file next to the program if any.
        public static void Load(string language)
        {
            using (Stream s = Assembly.GetExecutingAssembly()
                       .GetManifestResourceStream("en.xml"))
                if (s != null) Apply(s);

            if (string.IsNullOrEmpty(language)) return;
            string path = Path.Combine(Path.Combine(
                Path.GetDirectoryName(Application.ExecutablePath), "lang"),
                language + ".xml");
            if (!File.Exists(path)) return;
            try
            {
                using (Stream s = File.OpenRead(path)) Apply(s);
            }
            catch { }     // a broken translation must not get in the way
        }

        private static void Apply(Stream stream)
        {
            XmlDocument doc = new XmlDocument();
            try { doc.Load(stream); }
            catch { return; }

            Type type = typeof(S);
            foreach (XmlNode node in doc.SelectNodes("/strings/string"))
            {
                XmlAttribute key = node.Attributes["key"];
                if (key == null) continue;
                FieldInfo f = type.GetField(key.Value,
                    BindingFlags.Public | BindingFlags.Static);
                if (f != null && f.FieldType == typeof(string))
                    f.SetValue(null, node.InnerText);
            }
        }
    }
}

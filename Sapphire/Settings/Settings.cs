using System.Collections.Generic;
using UnityModManagerNet;

namespace Sapphire
{
    public enum OverlayPosition { Left, Right }
    public enum TextAlign { Left, Center, Right }

    public class KvColor
    {
        public float R, G, B, A;
        public UnityEngine.Color ToColor() => new UnityEngine.Color(R, G, B, A);
    }

    public class KeyViewerCell
    {
        public string Token = "";    // "Tab" / "KPS" / "Total" / "A" / "[" / etc.
        public string Label = null;  // optional display override; null = auto from Token
        public float WidthMul = 1f;  // relative width within its row (default 1.0)
    }

    public class KeyViewerRow
    {
        public List<KeyViewerCell> Cells = new List<KeyViewerCell>();
        public float Height = 60f;
        public KvColor RainColor = null;
        public bool ShowRain = true;

        public void EnsureDefaults()
        {
            if (Cells == null) Cells = new List<KeyViewerCell>();
            foreach (var c in Cells) if (c.WidthMul <= 0f) c.WidthMul = 1f;
        }
    }

    public class KeyViewerPreset
    {
        public string Name              = "Preset";
        public bool   PersistCounts     = true;
        public List<KeyViewerRow> Rows  = null;
        public float  KeyWidth          = 60f;
        public int    Radius            = 8;
        public float  BorderWidth       = 0f;
        public KvColor BorderIdle       = null;
        public KvColor BorderHeld       = null;
        public float  Gap               = 4f;
        public float  X                 = 0.01f;
        public float  Y                 = 0.01f;
        public float  Scale             = 1.0f;
        public float  RainDistance      = 300f;
        public float  RainTrackLength    = 390f;
        public float  RainSpeed         = 500f;
        public float  RainWidthStep     = 14f;
        public float  RainShadowSize    = 0f;
        public KvColor RainShadowColor  = null;
        public KvColor BgIdle           = null;
        public KvColor BgHeld           = null;
        public KvColor TxtIdle          = null;
        public KvColor TxtHeld          = null;
        public int     LabelSize        = 16;
        public bool    ShowLabel        = true;
        public KvColor CountIdle        = null;
        public KvColor CountHeld        = null;
        public int     CountSize        = 13;
        public bool    ShowCount        = true;

        // Ghost keys: keys whose press only spawns rain (no key cell, no count, no tile-hit).
        // Indexed against the top row's non-stat cells. Token "None" or empty = unassigned slot.
        public bool         GhostKeysEnabled = false;
        public List<string> GhostKeys        = null;
        public KvColor      GhostRainColor   = null;

        public void EnsureDefaults()
        {
            if (BgIdle    == null) BgIdle    = new KvColor { R = 0f,   G = 0f,   B = 0f,   A = 0.70f };
            if (BgHeld    == null) BgHeld    = new KvColor { R = 1f,   G = 1f,   B = 1f,   A = 1f };
            if (TxtIdle   == null) TxtIdle   = new KvColor { R = 1f,   G = 1f,   B = 1f,   A = 1f };
            if (TxtHeld   == null) TxtHeld   = new KvColor { R = 0f,   G = 0f,   B = 0f,   A = 1f };
            if (CountIdle == null) CountIdle = new KvColor { R = 0.7f, G = 0.7f, B = 0.7f, A = 1f };
            if (CountHeld == null) CountHeld = new KvColor { R = 0f,   G = 0f,   B = 0f,   A = 1f };
            if (RainShadowColor == null) RainShadowColor = new KvColor { R = 0f, G = 0f, B = 0f, A = 0.05f };
            if (BorderIdle      == null) BorderIdle      = new KvColor { R = 1f, G = 1f, B = 1f, A = 1f };
            if (BorderHeld      == null) BorderHeld      = new KvColor { R = 1f, G = 1f, B = 1f, A = 1f };
            if (Rows != null) foreach (var r in Rows) r?.EnsureDefaults();
        }
    }

    // Per-element font-weight override for one of the game's own HUD elements
    // (GameFontApplier). "" / absent = the automatic bold/regular heuristic.
    public class GameUiTextWeight
    {
        public string Key;
        public string Weight = "";
    }

    // Saved layout override for one of the game's own HUD elements (GameUiLayout).
    // Offsets are in the element's parent-canvas units. Scale multiplies localScale.
    public class GameUiOverride
    {
        public string Key;
        public float OffX;
        public float OffY;
        public float Scale = 1f;
        // Horizontal text alignment for text-bearing elements (currently the autoplay
        // label). -1 = inherit the game's alignment; 0/1/2 = Left/Center/Right.
        public int Align = -1;
        // Hide this element (every game HUD element now renders through Sapphire's wrapper, so
        // a CanvasGroup on the wrapper hides it + its shadow without fighting game show/hide).
        public bool Hidden = false;
    }

    // One event inside a saved preset: the LevelEventType id + type-tagged "key\u001Fvalue"
    // pairs (i:int f:float b:bool s:string v:x|y vector e:enum-name) — decoded against a fresh
    // default event of the same type, so unknown keys/newer game versions degrade gracefully.
    public class PresetEvent
    {
        public int Type;
        public List<string> Pairs = new List<string>();
    }

    public class EventPreset
    {
        public string Name = "Preset";
        public List<PresetEvent> Events = new List<PresetEvent>();
    }

    public class Settings : UnityModManager.ModSettings
    {
        // Inspector-tool event presets (user-named bundles of events applied per tile).
        public List<EventPreset> EventPresets = new List<EventPreset>();

        public bool ShowProgress = true;
        public bool ShowAcc = false;
        public bool ShowXAcc = true;
        public bool ShowBpm = true;
        public bool ShowTileBpm = true;
        // KPS / Best % / Progress Bar ship enabled (Default profile); Azure disables them.
        public bool ShowKps = true;
        public bool ShowSongDuration = false;
        public bool ShowLevelDuration = false;
        // Furthest progress reached during full (from-0%) attempts; renders in the
        // attempts block and persists per level alongside the attempt counts.
        public bool ShowBestProgress = true;
        // Master switch for the whole editor suite — the top-right corner button flips it, so
        // everything can be killed/restored in-game without the Ctrl+E panel.
        public bool EditorSuiteOn = true;
        // UI language: 0 = auto (follow the game), 1 = English, 2 = Korean.
        public int UiLanguage = 0;
        // Flips the wheel direction on Sapphire's scroll surfaces (timeline, graph, docks).
        public bool InvertScroll = false;
        // Tweaks tab → Editor: on-screen angle readout for the selected editor tile.
        public bool EditorTileAngle = false;
        // Tweaks tab → Editor: chips listing the selected tile's events (hover = details).
        public bool EditorShowEvents = false;
        // Tweaks tab → Editor: event timeline strip across the top of the editor.
        public bool EditorTimeline = false;
        // Tweaks tab → Editor: dark reskin of the game's own editor menus.
        public bool EditorDarkTheme = false;
        // Editor tab → Editor UI: Sapphire-native event inspector window replacing the
        // game's event settings panel (the game panel stays alive invisibly as the model).
        public bool EditorNativeInspector = false;
        // Editor tab → Editor UI: Sapphire file chip + menu replacing the game's file bar.
        public bool EditorFileChip = false;
        // Editor tab → Editor UI: play/rewind + clock docked in the timeline strip.
        public bool EditorTransport = false;
        // Editor tab → Editor UI: Sapphire rail replacing the inspector's tab strip.
        public bool EditorPanelRail = false;
        // Editor tab → Editor UI: Sapphire event palette replacing the game's bottom bar.
        public bool EditorEventDock = false;
        // Editor tab → Editor UI: Sapphire rail replacing the event inspector's tab column.
        public bool EditorEventInspector = false;
        // Editor tab → Editor UI: Sapphire messagebox replacing the editor's popups.
        public bool EditorPopupBox = false;
        // Editor tab → Editor UI: top tool toolbar (circular-path generator, free angle).
        public bool EditorTopToolbar = false;
        // Editor tab → Editor UI: right-click tile menu + free-angle rebind (hold right-Alt
        // instead of right-mouse, freeing right-click for the menu). Off = vanilla right-drag.
        public bool EditorTileActions = false;
        // Editor tab → Editor UI: pitch modifier overlay (bottom-left, above the timeline).
        public bool EditorPitchOverlay = false;
        // Editor tab: clean-screen charting mode — while in the editor, Sapphire overlays
        // and the key viewer stand down, and the game's difficulty/no-fail/autoplay
        // icons, autoplay text and hit error meter hide (see EditorModeActive / the OR'd
        // Active* accessors). Doesn't touch any of the underlying settings.
        public bool EditorModeEnabled = false;
        // Tweaks tab → Custom levels: CLS song-preview volume (0..1; the game plays
        // previews at full volume).
        public float ClsPreviewVolume = 0.7f;
        // Progress bar: thin top-edge bar filled by percentComplete. Style 1 (default):
        // white fill, progress-gradient perfect color at 100%; in theme mode the fill is
        // the theme's 100% color as a solid. Styles 2/3 planned.
        public bool ShowProgressBar = true;
        public int ProgressBarStyle = 1;
        public float ProgressBarHeight = 6f;
        // Vertical gap between stat rows in the left/right panels (VLG spacing).
        public float StatRowSpacing = 4f;
        public bool ShowTimingScale = true;
        public bool ShowJudgements = true;
        public float JudgementsSize = 0.9f;
        public float JudgementsY = 0f;
        public float JudgementsGap = 12f;
        public float Scale = 1.0f;
        public int Precision = 2;
        // Between a stat row's label and value; empty falls back to " | ".
        public string StatSeparator = " | ";
        // Per-part weight overrides ("" = match the overlay font's weight). Only
        // honored when the overlay font's family actually has the named weight.
        public string StatLabelWeight = "Medium";
        public string StatValueWeight = "";
        public string ComboLabelWeight = "";
        public string ComboValueWeight = FontLoader.WeightHeaviest;
        // Level-name weight now lives in GameUiTextWeights under the "levelname" key
        // (Game UI → Element weights), drawn from the game font family.
        public string KeyViewerLabelWeight = "";
        public string KeyViewerCountWeight = "";
        // Repaint the game's song title/artist text with the overlay font.
        public bool LevelNameUseOverlayFont = true;
        // Repaint ALL of the game's own text (legacy Text, TMP, 3D TextMesh) with the game
        // font below. On by default (the Game UI tab's "Game default" entry turns it off).
        // Name kept for settings compat; the font is no longer tied to the overlay font.
        public bool GameTextUseOverlayFont = true;
        // Game-text font (family + weight entry name), independent of FontName.
        public string GameFontName = "Paperlogy-4Regular";
        // Weight used for title/bold game text (scrHUDText titles, level select).
        public string GameTextTitleWeight = FontLoader.WeightHeaviest;
        // Separate size multiplier for the level-select per-level stats panels
        // (attempts, max x-acc, …) under "StatsText Container", applied on top of
        // GameFontApplier's baked base (0.8×). 1.0 = the tuned default.
        public float GameStatsScale = 1f;
        // Size multiplier for the hit-judgement popups (Perfect/완벽 …), applied ONLY to
        // them on top of the global game-text scale. 1.0 = follow the global size.
        public float GameJudgementScale = 1f;
        // Extra size multiplier on top of per-font metric scaling: Pretendard reads
        // larger than the game's fonts even after normalization, esp. world-space text.
        public float GameTextScale = 1f;
        // Line-advance multiplier for repainted game text, applied on top of
        // GameFontApplier's baked base (1.5×). 1.0 = the tuned default.
        public float GameTextLineSpacing = 1f;
        // Master switch + color for every overlay text shadow. Combo label/count keep
        // their dedicated shadow colors but obey the switch.
        public bool OverlayShadowEnabled = true;
        public KvColor OverlayShadowColor = new KvColor { R = 0f, G = 0f, B = 0f, A = 0.5f };
        // Default to Paperlogy (has CJK/kana): the level name resolves through this font when
        // the game font isn't ready, and 에이투지체 lacks kana, so Japanese titles rendered small.
        // FontName is the MASTER overlay font: it applies to every part while
        // OverlayMasterFontEnabled is on, and is the fallback for missing part fonts.
        public string FontName = "Paperlogy-4Regular";

        // Per-part overlay fonts (used while the master toggle is off). Stats also covers
        // judgements / timing scale / attempts / FPS. Weight overrides resolve against
        // each part's own family.
        public bool OverlayMasterFontEnabled = false;
        public string StatsFontName = "Paperlogy-4Regular";
        public string ComboFontName = "에이투지체-4Regular";
        public string KeyViewerFontName = "Paperlogy-4Regular";

        [System.Xml.Serialization.XmlIgnore]
        public string EffectiveStatsFont => OverlayMasterFontEnabled ? FontName : StatsFontName;
        [System.Xml.Serialization.XmlIgnore]
        public string EffectiveComboFont => OverlayMasterFontEnabled ? FontName : ComboFontName;
        [System.Xml.Serialization.XmlIgnore]
        public string EffectiveKeyViewerFont => OverlayMasterFontEnabled ? FontName : KeyViewerFontName;
        public bool ShowOverlay = true;
        public bool ShowFps = false;
        // Master switch for the whole Optimizations group; each flag below only takes
        // effect while this is on (the Misc → Optimizations dropdown header toggle).
        public bool OptimizationsEnabled = true;
        public bool OptSpectrumThrottle = true;
        public bool OptTextureNonReadable = true;
        public bool OptTextureDXT = false;
        public bool OptPhysicsNonAlloc = true;
        public bool OptUnloadAssets = true;
        public bool OptVolumeTrackDOTween = true;

        // Developer mode: reveals the Misc → Debug tools and shows [dbg] lines in the log viewer.
        public bool DebugMode = false;
        public bool ShowAttempts = false;
        public bool ShowFullAttempts = false;

        public List<KeyViewerPreset> KvHandPresets = null;
        public List<KeyViewerPreset> KvFootPresets = null;
        public int  KvActiveHand   = 0;
        public int  KvActiveFoot   = 0;
        public bool ShowKeyViewer  = true;
        public bool ShowHandViewer = true;
        public bool ShowFootViewer = false;
        // Scene-based suppression of the key viewer. Editor defaults on (the viewer just
        // clutters the chart editor); main menu off.
        public bool HideKeyViewerInEditor   = true;
        public bool HideKeyViewerInMainMenu = false;

        [System.Xml.Serialization.XmlIgnore]
        public KeyViewerPreset Hand =>
            (KvHandPresets != null && KvActiveHand >= 0 && KvActiveHand < KvHandPresets.Count)
                ? KvHandPresets[KvActiveHand] : null;

        [System.Xml.Serialization.XmlIgnore]
        public KeyViewerPreset Foot =>
            (KvFootPresets != null && KvActiveFoot >= 0 && KvActiveFoot < KvFootPresets.Count)
                ? KvFootPresets[KvActiveFoot] : null;

        public bool ShowComboDisplay = true;
        public float ComboDisplayY = 0f;
        public float ComboDisplaySize = 1.0f;
        public string ComboDisplayText = "Perfect Combo";
        public float ComboPulseDuration = 0.2f;
        public float ComboPulseOffsetY = 8f;
        public float ComboPulseScale   = 0.2f;
        public float ComboLabelY = 75f;
        public float ComboLabelSize = 1.0f;
        public float ComboCountSize = 1.0f;
        // Count (value) shadow.
        public float ComboShadowOffsetX = 4f;
        public float ComboShadowOffsetY = -4f;
        public KvColor ComboShadowColor = new KvColor { R = 0f, G = 0f, B = 0f, A = 0.5f };

        // Label shadow — independent. Defaults match the previously-derived proportional size
        // (sqrt(30/80) ≈ 0.612 × 4 ≈ 2.45 → rounded to 2.5).
        public float ComboLabelShadowOffsetX = 2.5f;
        public float ComboLabelShadowOffsetY = -2.5f;
        public KvColor ComboLabelShadowColor = new KvColor { R = 0f, G = 0f, B = 0f, A = 0.5f };
        public bool ComboCountAuto = false;

        public bool BlockInputsWhileMenuOpen = true;

        // Tweaks — key that pauses/resumes autoplay while play-testing in the editor
        // (the game hardcodes Space; rebindable + disableable from the Tweaks tab).
        public bool AutoplayPauseEnabled = false;
        public UnityEngine.KeyCode AutoplayPauseKey = UnityEngine.KeyCode.Space;

        // User chose "Keep both" in the duplicate-install prompt (Mods/ + UMMMods/).
        // Don't nag again.
        public bool IgnoreDuplicateInstall = false;

        public bool KeyLimiterEnabled = true;
        public bool KeyLimiterUseKvKeys = true;
        public string KeyLimiterCustomKeys = "";

        public bool ChatterBlockerEnabled = false;
        public int  ChatterThresholdMs = 50;

        public bool HideUiEnabled = true;
        public bool HideAllUI = false;
        public bool HideHitmeter = false;
        public bool HideAutoplayText = false;
        public bool HideAutoplayIcon = false;
        public bool HideNoFail = false;
        public bool HideDifficulty = false;
        // Judgement hit-text hiding. HideJudgementsEnabled is the feature on/off (the
        // collapsible's inline toggle). HideJudgementsAll is the in-body master that
        // hides every popup; otherwise the per-category flags apply (see ShouldHideJudgement).
        public bool HideJudgementsEnabled = false;
        public bool HideJudgementsAll = false;
        public bool HideJudgementsPerfect = false;    // Perfect
        public bool HideJudgementsELPerfect = false;  // EarlyPerfect, LatePerfect
        public bool HideJudgementsEarlyLate = false;  // VeryEarly, VeryLate
        public bool HideJudgementsMiss = false;       // TooEarly, TooLate
        public bool HideJudgementsDeath = false;      // FailMiss, FailOverload, Multipress, OverPress
        // Legacy single toggle (pre-judgement-categories). Migrated in EnsureDefaults
        // into HideJudgementsPerfect, then cleared. Kept so old Settings.xml deserializes.
        public bool HidePerfectJudgements = false;
        public bool HideLevelName = false;
        public bool HideBetaBuild = false;

        // Editor Mode only bites inside the editor scene (play-testing included), so
        // normal play is never affected by leaving it on.
        [System.Xml.Serialization.XmlIgnore] public bool EditorModeActive
        {
            get
            {
                if (!EditorModeEnabled) return false;
                try { return scnEditor.instance != null; } catch { return false; }
            }
        }

        // Effective Hide* — each Hide flag is gated by the section's master toggle so consumers
        // never need to repeat the && HideUiEnabled check. Editor Mode ORs into the flags
        // it covers, so every existing consumer picks it up for free.
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideAllUI             => HideUiEnabled && HideAllUI;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideHitmeter          => (HideUiEnabled && HideHitmeter) || EditorModeActive;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideAutoplayText      => (HideUiEnabled && HideAutoplayText) || EditorModeActive;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideAutoplayIcon      => (HideUiEnabled && HideAutoplayIcon) || EditorModeActive;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideNoFail            => (HideUiEnabled && HideNoFail) || EditorModeActive;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideDifficulty        => (HideUiEnabled && HideDifficulty) || EditorModeActive;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideLevelName         => HideUiEnabled && HideLevelName;
        [System.Xml.Serialization.XmlIgnore] public bool ActiveHideBetaBuild         => HideUiEnabled && HideBetaBuild;

        /* Whether a judgement hit-text popup of margin m should be suppressed. Gated by
           the section master (HideUiEnabled) like the Active* flags; HideJudgements hides
           every margin, otherwise the per-category flag for that margin's bucket applies. */
        public bool ShouldHideJudgement(HitMargin m)
        {
            if (!HideUiEnabled || !HideJudgementsEnabled) return false;
            if (HideJudgementsAll) return true;
            switch (m)
            {
                case HitMargin.Perfect:                            return HideJudgementsPerfect;
                case HitMargin.EarlyPerfect: case HitMargin.LatePerfect: return HideJudgementsELPerfect;
                case HitMargin.VeryEarly:    case HitMargin.VeryLate:    return HideJudgementsEarlyLate;
                case HitMargin.TooEarly:     case HitMargin.TooLate:     return HideJudgementsMiss;
                case HitMargin.FailMiss: case HitMargin.FailOverload:
                case HitMargin.Multipress: case HitMargin.OverPress:     return HideJudgementsDeath;
                default:                                           return false;  // Auto, etc.
            }
        }

        public OverlayPosition ProgressPosition  = OverlayPosition.Left;
        public OverlayPosition AccPosition       = OverlayPosition.Left;
        public OverlayPosition XAccPosition      = OverlayPosition.Left;
        public OverlayPosition BpmPosition       = OverlayPosition.Right;
        public OverlayPosition TileBpmPosition   = OverlayPosition.Right;
        public OverlayPosition KpsPosition       = OverlayPosition.Right;
        public OverlayPosition SongDurationPosition  = OverlayPosition.Left;
        public OverlayPosition LevelDurationPosition = OverlayPosition.Left;
        // Best % renders as a normal stat row unless parked in the attempts block.
        public OverlayPosition BestPosition = OverlayPosition.Left;
        public bool BestInAttempts = false;
        public string BestLabel = "";

        // Per-stat label overrides ("" = built-in: Progress/Accuracy/XAccuracy/BPM/TBPM/
        // KPS/Song/Length).
        public string ProgressLabel = "";
        public string AccLabel = "";
        public string XAccLabel = "";
        public string BpmLabel = "";
        public string TileBpmLabel = "";
        public string KpsLabel = "";
        public string SongDurationLabel = "";
        public string LevelDurationLabel = "";
        public float TimingScaleY    = 0f;
        public float TimingScaleSize = 0.75f;
        public float AttemptsX = 0.77f;
        public float AttemptsY = 0.05f;
        // Horizontal alignment of the attempts block relative to its anchor. Left pairs
        // with the 0.77 X anchor so the block grows rightward from there.
        public TextAlign AttemptsAlign = TextAlign.Left;
        public float LevelNameScale = 0.3f;
        public float LevelNameX     = 0f;
        public float LevelNameY     = 30f;

        // Normalized screen anchors for the draggable elements (Locations tab). Defaults
        // approximate the historical fixed placements at the 1920×1080 reference.
        public float StatusLeftX  = 0.005f;
        public float StatusLeftY  = 0.99f;
        public float StatusRightX = 0.995f;
        public float StatusRightY = 0.99f;
        public float ComboDisplayX       = 0.5f;
        public float ComboDisplayAnchorY = 0.85f;
        public float JudgementsX         = 0.5f;
        public float JudgementsAnchorY   = 0f;
        public float TimingScaleX        = 0.5f;
        public float TimingScaleAnchorY  = 0.12f;

        // Game HUD layout overrides (GameUiLayout / GameUiEditor). The hit error meter
        // is positioned by the game itself via a normalized anchor + size scale, so its
        // override is absolute. Everything else is an offset entry in GameUiOverrides.
        public bool  GameErrorMeterOverride = false;
        public float GameErrorMeterX = 0.5f;
        public float GameErrorMeterY = 0.03f;
        public float GameErrorMeterScale = 1f; // multiplier on the in-game size setting
        public List<GameUiOverride> GameUiOverrides = new List<GameUiOverride>();
        // Same override shape for the game EDITOR's own UI chrome (file bar, panel tabs…);
        // applied by EditorUiLayout, edited via the Editor tab's drag editor.
        public List<GameUiOverride> EditorUiOverrides = new List<GameUiOverride>();
        // Per-element game-text weight overrides (Game UI tab → Element weights).
        public List<GameUiTextWeight> GameUiTextWeights = new List<GameUiTextWeight>();

        // One-time seeding gate for the curated default layout: an EMPTY list is also a
        // legitimate user state (all reset to vanilla), so emptiness alone can't re-seed.
        public bool GameUiDefaultsSeeded = false;
        // One-time guard: nudge already-seeded configs off the old "Black" judgement
        // weight default onto the new "Light" one.
        public bool JudgementWeightLightMigrated = false;

        public ColorGradient ProgressGradient;
        public ColorGradient AccGradient;
        public ColorGradient BpmGradient;
        public ColorGradient ComboGradient;
        public float ComboGradientMax = 1000f;

        // X-Accuracy and Tile BPM inherit from Acc/Bpm by default. Toggle off to give them
        // their own gradient. The standalone gradients persist either way so users don't
        // lose work when toggling back and forth.
        public bool XAccUseAccGradient = true;
        public ColorGradient XAccGradient;
        public bool TileBpmUseBpmGradient = true;
        public ColorGradient TileBpmGradient;
        public bool KpsUseTileBpmGradient = true;
        public ColorGradient KpsGradient;

        // Accent-as-theme: when on, one gradient generated from the UI accent replaces
        // every stat/combo gradient (and the key viewer's default rain color) at evaluate
        // time. The saved per-stat gradients are untouched, so toggling off restores them.
        // ON out of the box — the "Default" profile IS the class defaults (red theme);
        // the "Azure" built-in restores the classic periwinkle, theme off.
        public bool AccentAsTheme = true;

        private ColorGradient _themeGradient;          // generic (BPM/TBPM/KPS): near-white pop at t=1
        private ColorGradient _themeProgressGradient;  // perfect = brightened accent
        private ColorGradient _themeAccGradient;       // acc/xacc: perfect = classic gold
        private ColorGradient _themeComboGradient;
        private float _themeKeyR = -1f, _themeKeyG = -1f, _themeKeyB = -1f;

        private void EnsureThemeGradients()
        {
            if (_themeGradient != null && _themeKeyR == UiAccentR && _themeKeyG == UiAccentG && _themeKeyB == UiAccentB)
                return;
            _themeKeyR = UiAccentR; _themeKeyG = UiAccentG; _themeKeyB = UiAccentB;

            // Shared ramp: soft accent tint → full accent (fresh stop list per gradient).
            List<ColorStop> Ramp() => new List<ColorStop>
            {
                new ColorStop
                {
                    Progress = 0f,
                    R = 1f - (1f - UiAccentR) * 0.45f,
                    G = 1f - (1f - UiAccentG) * 0.45f,
                    B = 1f - (1f - UiAccentB) * 0.45f,
                    A = 1f,
                },
                new ColorStop { Progress = 1f, R = UiAccentR, G = UiAccentG, B = UiAccentB, A = 1f },
            };

            // Brightened accent — full value, boosted saturation (bright red for the red
            // accent). Shared by the progress perfect and the combo's max end.
            UnityEngine.Color.RGBToHSV(
                new UnityEngine.Color(UiAccentR, UiAccentG, UiAccentB),
                out float th, out float ts, out float tv);
            var bright = UnityEngine.Color.HSVToRGB(th, UnityEngine.Mathf.Min(1f, ts * 1.25f), 1f);

            // Generic stats: near-white pop at t = 1.
            _themeGradient = new ColorGradient
            {
                HasPerfectColor = true,
                PR = 1f - (1f - UiAccentR) * 0.35f,
                PG = 1f - (1f - UiAccentG) * 0.35f,
                PB = 1f - (1f - UiAccentB) * 0.35f,
                PA = 1f,
                Stops = Ramp(),
            };

            // Progress: 100% flashes the brightened accent.
            _themeProgressGradient = new ColorGradient
            {
                HasPerfectColor = true,
                PR = bright.r, PG = bright.g, PB = bright.b, PA = 1f,
                Stops = Ramp(),
            };

            // Accuracy / X-Accuracy: 100% keeps the classic gold.
            _themeAccGradient = new ColorGradient
            {
                HasPerfectColor = true,
                PR = 1f, PG = 0.855f, PB = 0f, PA = 1f,
                Stops = Ramp(),
            };

            // Combo: same ramp but the max end RAMPS INTO the brightened accent.
            _themeComboGradient = new ColorGradient
            {
                HasPerfectColor = true,
                PR = bright.r,
                PG = bright.g,
                PB = bright.b,
                PA = 1f,
                Stops = new List<ColorStop>
                {
                    new ColorStop
                    {
                        Progress = 0f,
                        R = 1f - (1f - UiAccentR) * 0.45f,
                        G = 1f - (1f - UiAccentG) * 0.45f,
                        B = 1f - (1f - UiAccentB) * 0.45f,
                        A = 1f,
                    },
                    new ColorStop { Progress = 0.55f, R = UiAccentR, G = UiAccentG, B = UiAccentB, A = 1f },
                    new ColorStop { Progress = 1f, R = bright.r, G = bright.g, B = bright.b, A = 1f },
                },
            };
        }

        [System.Xml.Serialization.XmlIgnore]
        public ColorGradient ThemeGradient
        {
            get { EnsureThemeGradients(); return _themeGradient; }
        }

        [System.Xml.Serialization.XmlIgnore]
        public ColorGradient ThemeComboGradient
        {
            get { EnsureThemeGradients(); return _themeComboGradient; }
        }

        [System.Xml.Serialization.XmlIgnore]
        public ColorGradient ThemeProgressGradient
        {
            get { EnsureThemeGradients(); return _themeProgressGradient; }
        }

        [System.Xml.Serialization.XmlIgnore]
        public ColorGradient ThemeAccGradient
        {
            get { EnsureThemeGradients(); return _themeAccGradient; }
        }

        [System.Xml.Serialization.XmlIgnore]
        public ColorGradient ActiveProgressGradient => AccentAsTheme ? ThemeProgressGradient : ProgressGradient;
        [System.Xml.Serialization.XmlIgnore]
        public ColorGradient ActiveAccGradient => AccentAsTheme ? ThemeAccGradient : AccGradient;
        [System.Xml.Serialization.XmlIgnore]
        public ColorGradient ActiveBpmGradient => AccentAsTheme ? ThemeGradient : BpmGradient;
        [System.Xml.Serialization.XmlIgnore]
        public ColorGradient ActiveComboGradient => AccentAsTheme ? ThemeComboGradient : ComboGradient;
        [System.Xml.Serialization.XmlIgnore]
        public ColorGradient ActiveXAccGradient => AccentAsTheme ? ThemeAccGradient : XAccUseAccGradient ? AccGradient : XAccGradient;
        [System.Xml.Serialization.XmlIgnore]
        public ColorGradient ActiveTileBpmGradient => AccentAsTheme ? ThemeGradient : TileBpmUseBpmGradient ? BpmGradient : TileBpmGradient;
        [System.Xml.Serialization.XmlIgnore]
        public ColorGradient ActiveKpsGradient => KpsUseTileBpmGradient ? ActiveTileBpmGradient : AccentAsTheme ? ThemeGradient : KpsGradient;

        // UGUI settings panel preferences (new shell — see UI/).
        public float UiScale = 1.0f;
        public string UiFontName = "Paperlogy-4Regular";
        public bool UiAccentCustom = false;
        // Soft-red preset out of the box (the "Default" profile = these class defaults);
        // the "Azure" built-in profile restores the classic periwinkle.
        public float UiAccentR = 0.231f;   // sapphire blue (the mod's namesake)
        public float UiAccentG = 0.451f;
        public float UiAccentB = 0.949f;
        // Panel dimensions are saved across sessions; position is not (always re-centered).
        public float UiPanelWidth = 840f;
        public float UiPanelHeight = 540f;

        public void EnsureDefaults()
        {
            if (EventPresets == null) EventPresets = new List<EventPreset>();

            // July 11: per-feature toggles removed from the panel — the master switch is the
            // only gate now, so every suite feature is forced on (stale saved falses migrate).
            EditorTileAngle = EditorShowEvents = EditorTimeline = EditorDarkTheme = true;
            EditorFileChip = EditorTransport = EditorPanelRail = EditorEventDock = true;
            EditorNativeInspector = true;
            EditorEventInspector = EditorPopupBox = EditorTopToolbar = EditorTileActions = true;
            EditorPitchOverlay = true;

            // July 11: Sapphire's accent is blue now — migrate settings still on the old
            // Bismuth-red default.
            if (System.Math.Abs(UiAccentR - 0.886f) < 0.005f
                && System.Math.Abs(UiAccentG - 0.404f) < 0.005f
                && System.Math.Abs(UiAccentB - 0.427f) < 0.005f)
            { UiAccentR = 0.231f; UiAccentG = 0.451f; UiAccentB = 0.949f; }

            if (ProgressGradient == null || ProgressGradient.Stops.Count == 0)
                ProgressGradient = MakeProgressDefault();
            if (AccGradient == null || AccGradient.Stops.Count == 0)
                AccGradient = MakeAccDefault();
            if (BpmGradient == null || BpmGradient.Stops.Count == 0)
                BpmGradient = MakeBpmDefault();
            if (ComboGradient == null || ComboGradient.Stops.Count == 0)
                ComboGradient = MakeComboDefault();
            if (XAccGradient == null || XAccGradient.Stops.Count == 0)
                XAccGradient = MakeAccDefault();
            if (TileBpmGradient == null || TileBpmGradient.Stops.Count == 0)
                TileBpmGradient = MakeBpmDefault();
            if (KpsGradient == null || KpsGradient.Stops.Count == 0)
                KpsGradient = MakeBpmDefault();
            if (KvHandPresets == null || KvHandPresets.Count == 0)
            {
                KvHandPresets = new List<KeyViewerPreset>
                {
                    Make10kHandPreset(),
                    Make12kHandPreset(),
                    Make16kHandPreset(),
                };
                KvActiveHand = 1;
            }
            if (KvFootPresets == null || KvFootPresets.Count == 0)
            {
                KvFootPresets = new List<KeyViewerPreset>
                {
                    Make2kFootPreset(),
                    Make4kFootPreset(),
                    Make8kFootPreset(),
                    Make16kFootPreset(),
                };
                KvActiveFoot = 0;
            }
            foreach (var p in KvHandPresets) p.EnsureDefaults();
            foreach (var p in KvFootPresets) p.EnsureDefaults();

            // Curated default game-HUD layout: win/death texts pulled toward center and
            // scaled down from stock. Seeded once on fresh installs, never re-applied over
            // an existing (or deliberately emptied) layout.
            // Migrate the legacy single "hide perfect judgements" toggle into the new
            // per-category flag, then clear it so it doesn't re-trigger on later loads.
            if (HidePerfectJudgements)
            {
                HideJudgementsEnabled = true;
                HideJudgementsPerfect = true;
                HidePerfectJudgements = false;
            }

            if (!GameUiDefaultsSeeded)
            {
                GameUiDefaultsSeeded = true;
                if (GameUiOverrides == null || GameUiOverrides.Count == 0)
                    GameUiOverrides = MakeGameUiDefaults();
                if (GameUiTextWeights == null || GameUiTextWeights.Count == 0)
                    GameUiTextWeights = MakeGameUiWeightDefaults();
            }
            // The pause-button layout target was removed (July 2026) — drop stale saved
            // overrides so an old Hidden flag can't linger with no UI to clear it.
            GameUiOverrides?.RemoveAll(o => o != null && string.Equals(o.Key, "pause", System.StringComparison.OrdinalIgnoreCase));

            // The judgement default changed Black → Light; carry already-seeded configs
            // still on the old default over, once.
            if (!JudgementWeightLightMigrated)
            {
                JudgementWeightLightMigrated = true;
                if (string.Equals(GameUiWeightFor("judgement"), "Black", System.StringComparison.OrdinalIgnoreCase))
                    SetGameUiWeight("judgement", "Light");
            }
        }

        internal string GameUiWeightFor(string key)
        {
            if (GameUiTextWeights == null) return "";
            foreach (var w in GameUiTextWeights)
                if (w != null && w.Key == key) return w.Weight ?? "";
            return "";
        }

        internal void SetGameUiWeight(string key, string weight)
        {
            if (GameUiTextWeights == null) GameUiTextWeights = new List<GameUiTextWeight>();
            GameUiTextWeights.RemoveAll(w => w == null || w.Key == key);
            if (!string.IsNullOrEmpty(weight))
                GameUiTextWeights.Add(new GameUiTextWeight { Key = key, Weight = weight });
        }

        // Sapphire default for one game-HUD element. null = vanilla is the default.
        internal static GameUiOverride DefaultGameUiOverride(string key)
        {
            foreach (var o in MakeGameUiDefaults())
                if (o.Key == key) return o;
            return null;
        }

        internal static List<GameUiOverride> MakeGameUiDefaults() => new List<GameUiOverride>
        {
            new GameUiOverride { Key = "presstostart", OffY = -400f, Scale = 0.4f },
            new GameUiOverride { Key = "congrats",     OffY = 25f,   Scale = 0.85f },
            new GameUiOverride { Key = "countdown",    OffY = 70f,   Scale = 0.75f },
            new GameUiOverride { Key = "percent",      OffY = -35f,  Scale = 0.3f },
            new GameUiOverride { Key = "results",      OffY = -30f,  Scale = 0.8f },
            new GameUiOverride { Key = "strictclear",  OffY = 300f,  Scale = 0.4f },
            new GameUiOverride { Key = "autoplay",     OffX = 650f, OffY = -750f, Scale = 1f, Align = (int)TextAlign.Center },
        };

        // Sapphire default for one game-EDITOR chrome element. null = vanilla is default
        // (the timeline lives at the bottom now, so nothing needs to dodge it up top).
        internal static GameUiOverride DefaultEditorUiOverride(string key)
        {
            return null;
        }

        // Curated per-element game-text weights, baked alongside the layout.
        internal static List<GameUiTextWeight> MakeGameUiWeightDefaults() => new List<GameUiTextWeight>
        {
            new GameUiTextWeight { Key = "presstostart", Weight = "Thin" },
            new GameUiTextWeight { Key = "congrats",     Weight = "Black" },
            new GameUiTextWeight { Key = "countdown",    Weight = "Bold" },
            new GameUiTextWeight { Key = "results",      Weight = "Light" },
            new GameUiTextWeight { Key = "strictclear",  Weight = "Bold" },
            new GameUiTextWeight { Key = "autoplay",     Weight = "Light" },
            new GameUiTextWeight { Key = "judgement",    Weight = "Light" },
            new GameUiTextWeight { Key = "levelname",    Weight = "Medium" },
        };

        // Shorthand builder for default-preset rows. Tokens are space-separated; append `:Label` to
        // override the displayed text (e.g. `Backspace:Back`).
        private static KeyViewerRow Row(string tokens, float height, KvColor rain = null)
        {
            var cells = new List<KeyViewerCell>();
            foreach (var raw in tokens.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                string tok = raw;
                string lbl = null;
                int c = raw.IndexOf(':');
                if (c > 0 && c < raw.Length - 1) { tok = raw.Substring(0, c); lbl = raw.Substring(c + 1); }
                cells.Add(new KeyViewerCell { Token = tok, Label = lbl });
            }
            return new KeyViewerRow { Cells = cells, Height = height, RainColor = rain };
        }

        private static readonly KvColor BlueRain = new KvColor { R = 0.20f, G = 0.60f, B = 1.00f, A = 1f };

        private static KeyViewerPreset Make10kHandPreset() => new KeyViewerPreset
        {
            Name = "10k",
            Rows = new List<KeyViewerRow>
            {
                Row("Tab 1 2 E P = Backspace \\", 60f),
                Row("KPS Space ,:RAlt Total", 45f, BlueRain),
            }
        };

        private static KeyViewerPreset Make12kHandPreset() => new KeyViewerPreset
        {
            Name = "12k",
            Rows = new List<KeyViewerRow>
            {
                Row("Tab 1 2 E P = Backspace \\", 60f),
                Row("KPS C Space ,:RAlt . Total", 45f, BlueRain),
            }
        };

        private static KeyViewerPreset Make16kHandPreset() => new KeyViewerPreset
        {
            Name = "16k",
            Rows = new List<KeyViewerRow>
            {
                Row("Tab 1 2 E P = Backspace \\", 60f),
                Row("`:Caps LShift C Space ,:RAlt . RShift Enter", 60f, BlueRain),
                Row("KPS Total", 35f),
            }
        };

        private static KeyViewerPreset Make2kFootPreset() => new KeyViewerPreset
        {
            Name = "2k",
            KeyWidth = 30f, Gap = 2f, X = 0.3f, Y = 0.01f,
            LabelSize = 14, CountSize = 8, ShowCount = false,
            Rows = new List<KeyViewerRow> { Row("F8 F3", 30f) }
        };

        private static KeyViewerPreset Make4kFootPreset() => new KeyViewerPreset
        {
            Name = "4k",
            KeyWidth = 30f, Gap = 2f, X = 0.3f, Y = 0.01f,
            LabelSize = 14, CountSize = 8, ShowCount = false,
            Rows = new List<KeyViewerRow> { Row("F8 F7 F3 F2", 30f) }
        };

        private static KeyViewerPreset Make8kFootPreset() => new KeyViewerPreset
        {
            Name = "8k",
            KeyWidth = 30f, Gap = 2f, X = 0.3f, Y = 0.01f,
            LabelSize = 14, CountSize = 8, ShowCount = false,
            Rows = new List<KeyViewerRow> { Row("F8 F7 F6 F5 F4 F3 F2 F1", 30f) }
        };

        private static KeyViewerPreset Make16kFootPreset() => new KeyViewerPreset
        {
            Name = "16k",
            KeyWidth = 30f, Gap = 2f, X = 0.3f, Y = 0.01f,
            LabelSize = 14, CountSize = 8, ShowCount = false,
            Rows = new List<KeyViewerRow>
            {
                Row("F8 F7 F6 F5 F4 F3 F2 F1", 30f),
                Row("0 9 8 7 6 5 4 3", 30f),
            }
        };

        private static ColorGradient MakeProgressDefault() => new ColorGradient
        {
            HasPerfectColor = true,
            PR = 1f, PG = 0.855f, PB = 0f, PA = 1f,
            Stops = new List<ColorStop>
            {
                new ColorStop { Progress = 0.0f, R = 0.9f, G = 0.9f, B = 0.9f, A = 1f },
                new ColorStop { Progress = 1.0f, R = 0.20f, G = 0.70f, B = 1.00f, A = 1f },
            }
        };

        private static ColorGradient MakeAccDefault() => new ColorGradient
        {
            HasPerfectColor = true,
            PR = 1f, PG = 0.855f, PB = 0f, PA = 1f,
            Stops = new List<ColorStop>
            {
                new ColorStop { Progress = 0.90f, R = 1.00f, G = 0.00f, B = 0.03f, A = 1f },
                new ColorStop { Progress = 0.95f, R = 1.00f, G = 0.40f, B = 0.15f, A = 1f },
                new ColorStop { Progress = 0.97f, R = 0.40f, G = 0.70f, B = 0.30f, A = 1f },
                new ColorStop { Progress = 0.99f, R = 0.40f, G = 0.70f, B = 1.00f, A = 1f },
                new ColorStop { Progress = 1.00f, R = 0.20f, G = 0.60f, B = 1.00f, A = 1f },
            }
        };

        private static ColorGradient MakeComboDefault() => new ColorGradient
        {
            Stops = new List<ColorStop>
            {
                new ColorStop { Progress = 0f,   R = 0.50f, G = 0.75f, B = 1.00f, A = 1f },
                new ColorStop { Progress = 1.0f, R = 0.20f, G = 0.60f, B = 1.00f, A = 1f },
            }
        };

        private static ColorGradient MakeBpmDefault() => new ColorGradient
        {
            Stops = new List<ColorStop>
            {
                new ColorStop { Progress = 0.15f, R = 1.00f, G = 1.00f, B = 1.00f, A = 1f },
                new ColorStop { Progress = 1.00f, R = 0.20f, G = 0.60f, B = 1.00f, A = 1f },
            }
        };

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}

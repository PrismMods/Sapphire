using System.Collections.Generic;
using UnityModManagerNet;

namespace Sapphire
{
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

    // Shape-library custom shapes (persisted). A shape is a base angle with key-count variants;
    // each variant is relative charters + a per-tile twirl flag + a default repeat count.
    public class ShapeVariantDto
    {
        public int K;
        public List<double> Angles = new List<double>();
        public List<bool> Twirls = new List<bool>();
        public int N = 3;
    }

    public class CustomShapeDto
    {
        public string Name = "Shape";
        public string Category = "My shapes";
        public double Base;
        public List<ShapeVariantDto> Variants = new List<ShapeVariantDto>();
    }

    /* Sapphire's settings. Everything here has a consumer — the Bismuth overlay / key-viewer /
       combo / judgement / game-HUD model that came across in the July 2026 split (~170 fields
       and five DTO classes, nothing read them) was stripped on Sept 8 2026. Old settings files
       still load: XmlSerializer ignores elements it has no member for. */
    public class Settings : UnityModManager.ModSettings
    {
        // Inspector-tool event presets (user-named bundles of events applied per tile).
        public List<EventPreset> EventPresets = new List<EventPreset>();

        // Shape library: user-created shapes + their category names (built-in category is implicit).
        public List<string> ShapeCategories = new List<string>();
        public List<CustomShapeDto> CustomShapes = new List<CustomShapeDto>();

        // Master switch for the whole editor suite — the top-right corner button flips it, so
        // everything can be killed/restored in-game without the Ctrl+E panel.
        public bool EditorSuiteOn = true;
        // UI language: 0 = auto (follow the game), 1 = English, 2 = Korean.
        public int UiLanguage = 0;
        // Flips the wheel direction on Sapphire's scroll surfaces (timeline, graph, docks).
        public bool InvertScroll = false;

        // ── updater ──
        // Check GitHub releases once per session and toast when something newer exists.
        public bool AutoCheckUpdates = true;
        // Sapphire's own releases are prereleases today (1.0.0-aN), so defaulting this off
        // would mean the updater never finds anything for current users.
        public bool UpdateIncludePrerelease = true;
        // Tag the user pressed "skip" on; that exact release stays hidden.
        public string SkippedUpdateTag = "";

        /* ── Feature categories (July 18) ──────────────────────────────────────────────
           The settings model is now four categories, all default ON and all gated behind
           the in-editor master switch (EditorSuiteOn). The many granular Editor* flags
           below are kept as READ-ONLY FACADES over these categories, so every module's
           existing gate keeps working unchanged. To turn a feature group off, flip its
           category here (the Ctrl+E panel exposes exactly these). */
        public bool FeatTimeline = true;      // event timeline strip + transport + pitch + chips
        public bool FeatEventPanels = true;   // native event inspector + selector
        public bool FeatToolsSapphire = true; // toolbar's native tools + tile actions + presets
        public bool FeatToolsMods = true;     // MSM & MH tools (magic shape, track, deco)
        public bool FeatFileBar = true;       // Sapphire file chip / menu bar
        public bool FeatQuickChart = false;   // quick-chart mode (toolbar Q): keybinds + angle pad, hides timeline
        public bool EditorKeyHints = true;    // bottom-right on-screen keybind card
        // Presets window follows the Inspector tool by default; on, it stays up on its own.
        public bool EventPresetsFloating = false;

        // ── granular facades over the categories (do not assign; read only) ──
        // Passive tile-angle readout: NOT a tool — stays up whenever the suite is on (its
        // caller already gates on EditorSuiteOn), so disabling Sapphire tools keeps it.
        [System.Xml.Serialization.XmlIgnore] public bool EditorTileAngle => true;
        [System.Xml.Serialization.XmlIgnore] public bool EditorShowEvents => FeatTimeline;
        [System.Xml.Serialization.XmlIgnore] public bool EditorTimeline => FeatTimeline;
        [System.Xml.Serialization.XmlIgnore] public bool EditorTransport => FeatTimeline;
        [System.Xml.Serialization.XmlIgnore] public bool EditorPitchOverlay => FeatTimeline;
        [System.Xml.Serialization.XmlIgnore] public bool EditorNativeInspector => FeatEventPanels;
        [System.Xml.Serialization.XmlIgnore] public bool EditorEventDock => FeatEventPanels;
        [System.Xml.Serialization.XmlIgnore] public bool EditorEventInspector => FeatEventPanels;
        [System.Xml.Serialization.XmlIgnore] public bool EditorTopToolbar => FeatToolsSapphire;
        [System.Xml.Serialization.XmlIgnore] public bool EditorTileActions => FeatToolsSapphire;
        [System.Xml.Serialization.XmlIgnore] public bool EditorPopupBox => FeatToolsSapphire;
        [System.Xml.Serialization.XmlIgnore] public bool EditorFileChip => FeatFileBar;
        [System.Xml.Serialization.XmlIgnore] public bool EditorPanelRail => FeatFileBar;
        // Features tab › Editor mode: clean-screen charting mode — while in the editor, Sapphire overlays
        // and the key viewer stand down, and the game's difficulty/no-fail/autoplay
        // icons, autoplay text and hit error meter hide (see EditorModeActive / the OR'd
        // Active* accessors). Doesn't touch any of the underlying settings.
        public bool EditorModeEnabled = false;
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
        // Developer mode: shows [dbg] lines in the log viewer.
        public bool DebugMode = false;

        // Tweaks — key that pauses/resumes autoplay while play-testing in the editor
        // (the game hardcodes Space; rebindable + disableable from the Tweaks tab).
        public bool AutoplayPauseEnabled = false;
        public UnityEngine.KeyCode AutoplayPauseKey = UnityEngine.KeyCode.Space;

        // Rebindable editor hotkeys (Keybinds.All). Stored by id, defaults filled in on load —
        // an empty list means "everything default", so an old settings file needs no migration.
        public List<KeyBindDto> Keybinds = new List<KeyBindDto>();

        // OFF = a relaunch starts with every panel closed at its default spot (panel positions
        // already survive within a session on their own). ON = PanelLayout snapshots them.
        public bool PersistPanelLayout = false;
        public List<PanelStateDto> PanelStates = new List<PanelStateDto>();

        // Settings panel (Ctrl+E) preferences. Scale + accent are exposed on the Misc tab; the
        // font name is kept for the loader but has no picker — Sapphire ships one font.
        public float UiScale = 1.0f;
        public string UiFontName = "Paperlogy-4Regular";
        public float UiAccentR = 0.231f;   // sapphire blue (the mod's namesake)
        public float UiAccentG = 0.451f;
        public float UiAccentB = 0.949f;
        // Panel dimensions are saved across sessions; position is not (always re-centered).
        public float UiPanelWidth = 840f;
        public float UiPanelHeight = 540f;

        public void EnsureDefaults()
        {
            if (EventPresets == null) EventPresets = new List<EventPreset>();
            if (ShapeCategories == null) ShapeCategories = new List<string>();
            if (CustomShapes == null) CustomShapes = new List<CustomShapeDto>();
            if (Keybinds == null) Keybinds = new List<KeyBindDto>();
            if (PanelStates == null) PanelStates = new List<PanelStateDto>();

            // July 11: Sapphire's accent is blue now — migrate settings still on the old
            // Bismuth-red default.
            if (System.Math.Abs(UiAccentR - 0.886f) < 0.005f
                && System.Math.Abs(UiAccentG - 0.404f) < 0.005f
                && System.Math.Abs(UiAccentB - 0.427f) < 0.005f)
            { UiAccentR = 0.231f; UiAccentG = 0.451f; UiAccentB = 0.949f; }
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}

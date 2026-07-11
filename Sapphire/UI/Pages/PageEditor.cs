using TMPro;
using UnityEngine;

namespace Sapphire.UI.Pages
{
    // "Editor" tab — level-editor helpers: autoplay pause key, tile readouts, the event
    // timeline, and the experimental dark reskin of the game's editor menus.
    internal static class PageEditor
    {
        public static void Build(PageStack stack)
        {
            var content = stack.Root;
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;

            UIBuilder.SectionHeaderWithHelp(content, "Editor mode",
                "Clean screen for charting: while in the editor\n(play-testing included), " +
                "Sapphire overlays and the key\nviewer stand down, and the game's difficulty,\n" +
                "no-fail and autoplay icons, autoplay text and hit\nerror meter hide. " +
                "None of your settings change —\neverything returns when you leave the editor\nor turn this off.");
            UIBuilder.Collapsible(content, "Editor mode", s.EditorModeEnabled,
                v => { s.EditorModeEnabled = v; notify?.Invoke(); }, null);

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeaderWithHelp(content, "Autoplay",
                "Pauses/resumes autoplay while play-testing a level in the editor\n(the game " +
                "hardcodes Space). Turn it off entirely, or rebind:\nclick the button, then press a key.");

            UIBuilder.Collapsible(content, "Enable autoplay pause", s.AutoplayPauseEnabled,
                v => { s.AutoplayPauseEnabled = v; notify?.Invoke(); }, null);

            // A hidden per-frame key listener drives the rebind; it reads input directly
            // (exempt from the menu-open input block), so it works with the panel open.
            var listener = UIBuilder.Rect("AutoPauseKeyListener", content).AddComponent<KeyListener>();

            TextMeshProUGUI btnLabel = null; // set after the button is built; captured by the closures
            var btn = UIBuilder.Button(content, KeyLabel(s.AutoplayPauseKey), () =>
            {
                listener.Active = true;
                if (btnLabel != null) btnLabel.text = "Press a key…";
            });
            btnLabel = btn.GetComponentInChildren<TextMeshProUGUI>();

            listener.OnKey = kc =>
            {
                listener.Active = false;
                s.AutoplayPauseKey = kc;
                notify?.Invoke();
                if (btnLabel != null) btnLabel.text = KeyLabel(s.AutoplayPauseKey);
            };

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeaderWithHelp(content, "Tile info",
                "Readouts at the top of the editor for the selected tile:\nits angle (180° = straight) " +
                "and its events as chips —\nhover a chip for that event's settings.");
            UIBuilder.Collapsible(content, "Show selected tile angle", s.EditorTileAngle,
                v => { s.EditorTileAngle = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, "Show selected tile events", s.EditorShowEvents,
                v => { s.EditorShowEvents = v; notify?.Invoke(); }, null);

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeaderWithHelp(content, "Timeline",
                "Every event in the level on one strip, one lane per\ncategory (twirls are hidden). " +
                "Hover a marker for details,\nclick it to jump there, drag the playhead to scrub.\n" +
                "+/- buttons zoom, the wheel pans, and the view\nfollows the run during play-testing.\n" +
                "Measure lines assume 4/4 from the first input tile.");
            UIBuilder.Collapsible(content, "Show event timeline", s.EditorTimeline,
                v => { s.EditorTimeline = v; notify?.Invoke(); }, null);

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeaderWithHelp(content, "Editor UI",
                "Restyles the game's own editor menus to match\nSapphire's look. Experimental — " +
                "if a panel looks broken,\nturn it off and it restores the original colors.\n\n" +
                "Layout: drag the editor's own elements (file bar,\npanel tabs) to new positions. " +
                "Drag to move, scroll\nto scale, right-click to reset one element.");
            UIBuilder.Collapsible(content, "Dark editor theme", s.EditorDarkTheme,
                v => { s.EditorDarkTheme = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, "Sapphire file menu", s.EditorFileChip,
                v => { s.EditorFileChip = v; notify?.Invoke(); }, null);
            // Lives inside the timeline strip, so it needs the timeline on.
            UIBuilder.Collapsible(content, "Transport in timeline", s.EditorTransport,
                v => { s.EditorTransport = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, "Sapphire panel rail", s.EditorPanelRail,
                v => { s.EditorPanelRail = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, "Sapphire event palette", s.EditorEventDock,
                v => { s.EditorEventDock = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, "Sapphire event tabs", s.EditorEventInspector,
                v => { s.EditorEventInspector = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, "Sapphire popups", s.EditorPopupBox,
                v => { s.EditorPopupBox = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, "Top tool toolbar", s.EditorTopToolbar,
                v => { s.EditorTopToolbar = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, "Right-click tile menu + free angle (right-Alt)", s.EditorTileActions,
                v => { s.EditorTileActions = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, "Pitch modifier overlay", s.EditorPitchOverlay,
                v => { s.EditorPitchOverlay = v; notify?.Invoke(); }, null);
            UIBuilder.Button(content, "Edit editor UI on screen", EditorUiEditor.Open);
            UIBuilder.DangerButton(content, "Reset editor layout to Sapphire defaults", () =>
            {
                EditorUiLayout.ResetAllToDefaults();
                notify?.Invoke();
            });
            UIBuilder.DangerButton(content, "Reset editor layout to game defaults", () =>
            {
                EditorUiLayout.ResetAllToGame();
                notify?.Invoke();
            });
        }

        private static string KeyLabel(KeyCode kc) =>
            "Pause key: " + KeyTokens.PrettyTokenLabel(KeyTokens.TokenFromKeyCode(kc));
    }
}

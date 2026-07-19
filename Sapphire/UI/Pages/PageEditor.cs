using TMPro;
using UnityEngine;

namespace Sapphire.UI.Pages
{
    /* "Editor" tab — language, feature-category toggles, Editor mode, the autoplay pause
       key, and the layout tools. Feature toggles (July 18) are four categories, all ON by
       default and all gated behind the in-editor master switch (top-right power button):
       Timeline, Event panels, Tools (Sapphire + MSM/MH), File bar. They're facades that
       drive the granular Editor* flags via Settings. */
    internal static class PageEditor
    {
        private static readonly string[] LangLabels = { "Auto (follow game)", "English", "한국어" };

        public static void Build(PageStack stack)
        {
            var content = stack.Root;
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;

            UIBuilder.SectionHeaderWithHelp(content, "Language",
                "Language for Sapphire's editor UI and help.\nAuto follows the game's language setting.");
            TextMeshProUGUI langLabel = null;
            var langBtn = UIBuilder.Button(content, "Language: " + LangLabels[Mathf.Clamp(s.UiLanguage, 0, 2)], () =>
            {
                s.UiLanguage = (s.UiLanguage + 1) % 3;
                notify?.Invoke();
                if (langLabel != null) langLabel.text = "Language: " + LangLabels[s.UiLanguage];
            });
            langLabel = langBtn.GetComponentInChildren<TextMeshProUGUI>();

            UIBuilder.Collapsible(content, "Invert scroll direction", s.InvertScroll,
                v => { s.InvertScroll = v; notify?.Invoke(); }, null);

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeaderWithHelp(content, "Features",
                "Turn whole feature groups on or off. The in-editor master\nswitch (top-right power " +
                "button in the level editor) gates all\nof them together; these choose which groups it enables.");
            UIBuilder.Collapsible(content, "Event timeline", s.FeatTimeline,
                v => { s.FeatTimeline = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, "Event panels (inspector + selector)", s.FeatEventPanels,
                v => { s.FeatEventPanels = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, "Sapphire tools", s.FeatToolsSapphire,
                v => { s.FeatToolsSapphire = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, "MSM & MH tools", s.FeatToolsMods,
                v => { s.FeatToolsMods = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, "File menu bar", s.FeatFileBar,
                v => { s.FeatFileBar = v; notify?.Invoke(); }, null);

            UIBuilder.Spacer(content);
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
            UIBuilder.SectionHeaderWithHelp(content, "Editor UI layout",
                "All Sapphire editor features are on — the switch in the\neditor's top-right corner " +
                "turns the whole suite on/off.\n\nLayout: drag the editor's own elements (file bar,\n" +
                "panel tabs) to new positions. Drag to move, scroll\nto scale, right-click to reset one element.");
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

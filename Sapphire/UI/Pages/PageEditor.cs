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
            UIBuilder.Button(content, "Language: " + LangLabels[Mathf.Clamp(s.UiLanguage, 0, 2)], () =>
            {
                s.UiLanguage = (s.UiLanguage + 1) % 3;
                notify?.Invoke();
                // Rebuild the panel body so this page (and its language button label) re-localize
                // to the new language; RebuildBody keeps the panel open on this same tab.
                UICore.RebuildBody();
            });

            UIBuilder.Collapsible(content, "Invert scroll direction", s.InvertScroll,
                v => { s.InvertScroll = v; notify?.Invoke(); }, null);

            UIBuilder.Spacer(content);
            BuildUpdates(content, s, notify);

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
            UIBuilder.SectionHeaderWithHelp(content, "Quick chart",
                "A charting mode (toggle with the Q button in the editor\ntoolbar). While on, the " +
                "event timeline hides for a clean\nscreen and these hotkeys are live:\n" +
                "  I        swirl on/off on the selected tile(s)\n" +
                "  Shift+P  Pause event (prompts for beats)\n" +
                "  Shift+L  tile-location event (prompts X / Y)\n" +
                "  Shift+G  angle pad — type space-separated RELATIVE\n" +
                "           angles (180 = straight; math ok: 180-30,\n" +
                "           360/8) and Place a whole run; duplicate it\n" +
                "           for saved presets.\n" +
                "These keys are chosen to never clash with the game's tile placement.");
            UIBuilder.Collapsible(content, "Quick chart mode", s.FeatQuickChart,
                v => { s.FeatQuickChart = v; notify?.Invoke(); }, null);

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

        /* Updates section. The status line is rebuilt from UpdateService on a poll rather than
           an event, matching how UpdateToast reads it — the service runs on a worker thread and
           must not call back into Unity. */
        private static void BuildUpdates(Transform content, Settings s, System.Action notify)
        {
            UIBuilder.SectionHeaderWithHelp(content, "Updates",
                "Sapphire checks its GitHub releases once per session and\nshows a toast when a " +
                "newer build exists. Clicking the toast\ndownloads and installs it over this copy; " +
                "the new version\nloads after you restart the game.\n\nDownloads are verified " +
                "against the checksum GitHub\npublishes for the release asset.");

            UIBuilder.Label(content, "Installed: " + MainClass.ModVersion);
            var status = UIBuilder.Label(content, StatusLine());

            UIBuilder.Collapsible(content, "Check for updates automatically", s.AutoCheckUpdates,
                v => { s.AutoCheckUpdates = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, "Include pre-release builds", s.UpdateIncludePrerelease,
                v => { s.UpdateIncludePrerelease = v; notify?.Invoke(); }, null);

            // A poller keeps the status line live across the check's worker thread without the
            // page needing a rebuild (which would collapse the section under the user).
            var poll = UIBuilder.Rect("UpdateStatusPoll", content).AddComponent<StatusPoller>();
            poll.Label = status;

            UIBuilder.Button(content, "Check now", () => UpdateService.Check(true));
            UIBuilder.Button(content, "Preview the update toast", UpdateToast.ShowPreview);
            if (!string.IsNullOrEmpty(s.SkippedUpdateTag))
                UIBuilder.Button(content, "Un-skip " + s.SkippedUpdateTag, UpdateService.ClearSkip);
        }

        private static string StatusLine()
        {
            switch (UpdateService.Status)
            {
                case UpdateStatus.Checking:  return "Checking…";
                case UpdateStatus.UpToDate:  return "Up to date";
                case UpdateStatus.Available:
                    return "Available: " + (UpdateService.Available != null ? UpdateService.Available.Tag : "?");
                case UpdateStatus.Installing:
                    float p = UpdateService.Progress;
                    return p >= 0f ? "Downloading… " + Mathf.RoundToInt(p * 100f) + "%" : "Downloading…";
                case UpdateStatus.Installed: return "Installed — restart the game to apply";
                case UpdateStatus.Failed:    return "Failed: " + UpdateService.Message;
                default:                     return "Not checked yet";
            }
        }

        private class StatusPoller : MonoBehaviour
        {
            public TextMeshProUGUI Label;
            private string _last;
            private int _cd;

            private void Update()
            {
                if (Label == null) return;
                if (--_cd > 0) return;
                _cd = 10;   // ~6/s: enough for a progress readout, far below a per-frame cost
                string now = StatusLine();
                if (now == _last) return;
                _last = now;
                Label.text = now;
            }
        }

        private static string KeyLabel(KeyCode kc) =>
            "Pause key: " + KeyTokens.PrettyTokenLabel(KeyTokens.TokenFromKeyCode(kc));
    }
}

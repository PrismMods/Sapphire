using System;
using TMPro;
using UnityEngine;

namespace Sapphire.UI.Pages
{
    /* "Editor" tab. The root is a one-screen overview — a card grid of the feature categories,
       then a row per area that drills into its own page. Everything that needs a paragraph of
       explanation (quick chart's hotkeys, what editor mode hides, how updates install) lives on
       its subpage instead of as a wall of help text on the root, and each NavRow registers with
       the settings search so the inner controls are still findable by name.

       Feature toggles are four categories, all ON by default and all gated behind the in-editor
       master switch (top-right power button): Timeline, Event panels, Tools (Sapphire + MSM/MH),
       File bar. They're facades that drive the granular Editor* flags via Settings. */
    internal static class PageEditor
    {
        private static readonly string[] LangLabels = { "Auto (follow game)", "English", "한국어" };

        // Hotkey lines for the quick-chart page; each is a Loc key in its own right.
        private static readonly string[] QuickChartKeys =
        {
            "I — swirl on/off on the selected tile(s)",
            "O — set speed on the selected tile",
            "[ / ] — halve / double that tile's speed",
            "Shift+P — Pause event (prompts for beats)",
            "Shift+L — tile-location event (prompts X / Y)",
            "Shift+G — angle pad: space-separated RELATIVE angles (180 = straight; math ok), Place a whole run",
            "These keys never clash with the game's tile placement.",
        };

        public static void Build(PageStack stack)
        {
            var content = stack.Root;
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;

            UIBuilder.SectionHeaderWithHelp(content, Loc.T("Features"), Loc.T("FeaturesHelp"));
            var grid = UIBuilder.CardGrid(content).transform;
            UIBuilder.ToggleCard(grid, Loc.T("Event timeline"), s.FeatTimeline,
                v => { s.FeatTimeline = v; notify?.Invoke(); });
            UIBuilder.ToggleCard(grid, Loc.T("Event panels"), s.FeatEventPanels,
                v => { s.FeatEventPanels = v; notify?.Invoke(); });
            UIBuilder.ToggleCard(grid, Loc.T("Sapphire tools"), s.FeatToolsSapphire,
                v => { s.FeatToolsSapphire = v; notify?.Invoke(); });
            UIBuilder.ToggleCard(grid, Loc.T("MSM & MH tools"), s.FeatToolsMods,
                v => { s.FeatToolsMods = v; notify?.Invoke(); });
            UIBuilder.ToggleCard(grid, Loc.T("File menu bar"), s.FeatFileBar,
                v => { s.FeatFileBar = v; notify?.Invoke(); });
            UIBuilder.ToggleCard(grid, Loc.T("Key hints"), s.EditorKeyHints,
                v => { s.EditorKeyHints = v; notify?.Invoke(); });

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, Loc.T("Modes"));
            UIBuilder.NavRow(content, Loc.T("Quick chart"), s.FeatQuickChart,
                v => { s.FeatQuickChart = v; notify?.Invoke(); },
                () => stack.Push(Loc.T("Quick chart"), QuickChartPage),
                "swirl, set speed, pause event, tile location, angle pad, hotkeys");
            UIBuilder.NavRow(content, Loc.T("Editor mode"), s.EditorModeEnabled,
                v => { s.EditorModeEnabled = v; notify?.Invoke(); },
                () => stack.Push(Loc.T("Editor mode"), EditorModePage),
                "clean screen, hide key viewer, hide autoplay, hit error meter");
            UIBuilder.NavRow(content, Loc.T("Autoplay pause"), s.AutoplayPauseEnabled,
                v => { s.AutoplayPauseEnabled = v; notify?.Invoke(); },
                () => stack.Push(Loc.T("Autoplay pause"), AutoplayPage),
                "pause key, rebind, space");

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, Loc.T("General"));
            UIBuilder.NavRow(content, Loc.T("Language"), () => stack.Push(Loc.T("Language"), LanguagePage),
                "english, korean, 한국어, auto");
            UIBuilder.Collapsible(content, Loc.T("Invert scroll direction"), s.InvertScroll,
                v => { s.InvertScroll = v; notify?.Invoke(); }, null);
            UIBuilder.NavRow(content, Loc.T("Updates"), () => stack.Push(Loc.T("Updates"), UpdatesPage),
                "check for updates, pre-release, install, version");
        }

        // ── subpages ─────────────────────────────────────────────────────────

        private static void LanguagePage(Transform body)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;
            UIBuilder.Label(body, Loc.T("LanguageHelp"));
            UIBuilder.Button(body, Loc.T("Language") + ": " + Loc.T(LangLabels[Mathf.Clamp(s.UiLanguage, 0, 2)]), () =>
            {
                s.UiLanguage = (s.UiLanguage + 1) % 3;
                notify?.Invoke();
                // Rebuild the panel body so every built string re-localizes; this pops back to
                // the tab root, which is the only way to re-run the whole page tree.
                UICore.RebuildBody();
            });
        }

        private static void QuickChartPage(Transform body)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;
            UIBuilder.Collapsible(body, Loc.T("Quick chart mode"), s.FeatQuickChart,
                v => { s.FeatQuickChart = v; notify?.Invoke(); }, null);
            UIBuilder.Label(body, Loc.T("QuickChartHelp"));
            foreach (var line in QuickChartKeys) UIBuilder.Label(body, Loc.T(line));
        }

        private static void EditorModePage(Transform body)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;
            UIBuilder.Collapsible(body, Loc.T("Editor mode"), s.EditorModeEnabled,
                v => { s.EditorModeEnabled = v; notify?.Invoke(); }, null);
            UIBuilder.Label(body, Loc.T("EditorModeHelp1"));
            UIBuilder.Label(body, Loc.T("EditorModeHelp2"));
        }

        private static void AutoplayPage(Transform body)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;
            UIBuilder.Collapsible(body, Loc.T("Enable autoplay pause"), s.AutoplayPauseEnabled,
                v => { s.AutoplayPauseEnabled = v; notify?.Invoke(); }, null);
            UIBuilder.Label(body, Loc.T("AutoplayHelp"));

            // A hidden per-frame key listener drives the rebind; it reads input directly
            // (exempt from the menu-open input block), so it works with the panel open.
            var listener = UIBuilder.Rect("AutoPauseKeyListener", body).AddComponent<KeyListener>();

            TextMeshProUGUI btnLabel = null; // set after the button is built; captured by the closures
            var btn = UIBuilder.Button(body, KeyLabel(s.AutoplayPauseKey), () =>
            {
                listener.Active = true;
                if (btnLabel != null) btnLabel.text = Loc.T("Press a key…");
            });
            btnLabel = btn.GetComponentInChildren<TextMeshProUGUI>();

            listener.OnKey = kc =>
            {
                listener.Active = false;
                s.AutoplayPauseKey = kc;
                notify?.Invoke();
                if (btnLabel != null) btnLabel.text = KeyLabel(s.AutoplayPauseKey);
            };
        }

        /* Updates page. The status line is rebuilt from UpdateService on a poll rather than an
           event, matching how UpdateToast reads it — the service runs on a worker thread and must
           not call back into Unity. */
        private static void UpdatesPage(Transform body)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;
            UIBuilder.Label(body, Loc.T("UpdatesHelp"));

            UIBuilder.Label(body, Loc.T("Installed") + ": " + MainClass.ModVersion);
            var status = UIBuilder.Label(body, StatusLine());

            UIBuilder.Collapsible(body, Loc.T("Check for updates automatically"), s.AutoCheckUpdates,
                v => { s.AutoCheckUpdates = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(body, Loc.T("Include pre-release builds"), s.UpdateIncludePrerelease,
                v => { s.UpdateIncludePrerelease = v; notify?.Invoke(); }, null);

            var poll = UIBuilder.Rect("UpdateStatusPoll", body).AddComponent<StatusPoller>();
            poll.Label = status;

            UIBuilder.Button(body, Loc.T("Check now"), () => UpdateService.Check(true));
            if (!string.IsNullOrEmpty(s.SkippedUpdateTag))
                UIBuilder.Button(body, Loc.T("Un-skip") + " " + s.SkippedUpdateTag, UpdateService.ClearSkip);
        }

        private static string StatusLine()
        {
            switch (UpdateService.Status)
            {
                case UpdateStatus.Checking:  return Loc.T("Checking…");
                case UpdateStatus.UpToDate:  return Loc.T("Up to date");
                case UpdateStatus.Available:
                    return Loc.T("Available") + ": " + (UpdateService.Available != null ? UpdateService.Available.Tag : "?");
                case UpdateStatus.Installing:
                    float p = UpdateService.Progress;
                    return p >= 0f ? Loc.T("Downloading…") + " " + Mathf.RoundToInt(p * 100f) + "%" : Loc.T("Downloading…");
                case UpdateStatus.Installed: return Loc.T("Installed — restart the game to apply");
                case UpdateStatus.Failed:    return Loc.T("Failed") + ": " + UpdateService.Message;
                default:                     return Loc.T("Not checked yet");
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
            Loc.T("Pause key") + ": " + KeyTokens.PrettyTokenLabel(KeyTokens.TokenFromKeyCode(kc));
    }
}

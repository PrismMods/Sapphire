using UnityEngine;

namespace Sapphire.UI.Pages
{
    /* "Features" tab: what Sapphire adds, as toggles. A card per feature category (all default
       ON, all gated behind the in-editor master switch), then the three MODES, each with its own
       drill-in page for the paragraph of explanation it needs. Feature-level settings live with
       their feature; anything that is a KEY lives on the Keybinds tab, so a mode's page
       describes what it does and never repeats the rebind rows. */
    internal static class PageFeatures
    {
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
            UIBuilder.ToggleCard(grid, Loc.T("Pin event presets"), s.EventPresetsFloating,
                v => { s.EventPresetsFloating = v; notify?.Invoke(); });

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, Loc.T("Modes"));
            UIBuilder.NavRow(content, Loc.T("Quick chart"), s.FeatQuickChart,
                v => { s.FeatQuickChart = v; notify?.Invoke(); },
                () => stack.Push(Loc.T("Quick chart"), QuickChartPage),
                "swirl, set speed, pause event, tile location, angle pad, hz tool");
            UIBuilder.NavRow(content, Loc.T("Editor mode"), s.EditorModeEnabled,
                v => { s.EditorModeEnabled = v; if (v) s.PlayModeEnabled = false; notify?.Invoke(); },
                () => stack.Push(Loc.T("Editor mode"), EditorModePage),
                "clean screen, hide key viewer, hide autoplay, hit error meter");
            UIBuilder.NavRow(content, Loc.T("Play mode"), s.PlayModeEnabled,
                v => { s.PlayModeEnabled = v; if (v) s.EditorModeEnabled = false; notify?.Invoke(); },
                () => stack.Push(Loc.T("Play mode"), PlayModePage),
                "playtest, autoplay off, no fail, hide ui, clean screen");
            UIBuilder.NavRow(content, Loc.T("Autoplay pause"), s.AutoplayPauseEnabled,
                v => { s.AutoplayPauseEnabled = v; notify?.Invoke(); },
                () => stack.Push(Loc.T("Autoplay pause"), AutoplayPage),
                "pause, autoplay, space");
        }

        private static void QuickChartPage(Transform body)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;
            UIBuilder.Collapsible(body, Loc.T("Quick chart mode"), s.FeatQuickChart,
                v => { s.FeatQuickChart = v; notify?.Invoke(); }, null);
            UIBuilder.Label(body, Loc.T("QuickChartHelp"));
            UIBuilder.Label(body, Loc.T("Its keys are on the Keybinds tab."));
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

        /* The two modes are opposites — Editor mode turns autoplay ON for charting, Play mode
           turns it OFF to actually play — so each clears the other rather than letting a user
           hold both and wonder which one won. */
        private static void PlayModePage(Transform body)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;
            UIBuilder.Collapsible(body, Loc.T("Play mode"), s.PlayModeEnabled,
                v => { s.PlayModeEnabled = v; if (v) s.EditorModeEnabled = false; notify?.Invoke(); }, null);
            UIBuilder.Label(body, Loc.T("PlayModeHelp1"));
            UIBuilder.Collapsible(body, Loc.T("Enable no-fail"), s.PlayModeNoFail,
                v => { s.PlayModeNoFail = v; notify?.Invoke(); },
                b => UIBuilder.Label(b, Loc.T("PlayModeNoFailHelp")));
            UIBuilder.Label(body, Loc.T("PlayModeHelp2"));
        }

        private static void AutoplayPage(Transform body)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;
            UIBuilder.Collapsible(body, Loc.T("Enable autoplay pause"), s.AutoplayPauseEnabled,
                v => { s.AutoplayPauseEnabled = v; notify?.Invoke(); }, null);
            UIBuilder.Label(body, Loc.T("AutoplayHelp"));
            UIBuilder.Label(body, Loc.T("Its key is on the Keybinds tab."));
        }
    }
}

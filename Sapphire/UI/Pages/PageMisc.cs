using UnityEngine;

namespace Sapphire.UI.Pages
{
    /* "Misc" tab: everything that is neither a feature, a key nor the updater. Language and the
       two behaviour switches, then APPEARANCE — UI scale and the accent colour. Those two were
       live settings with working appliers (UICore.ApplyScale / ApplyAccent) and no control
       anywhere: they were Bismuth's UI-tab rows and did not cross over in the split. */
    internal static class PageMisc
    {
        private static readonly string[] LangLabels = { "Auto (follow game)", "English", "한국어" };

        public static void Build(PageStack stack)
        {
            var content = stack.Root;
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;

            UIBuilder.SectionHeader(content, Loc.T("General"));
            UIBuilder.NavRow(content, Loc.T("Language"), () => stack.Push(Loc.T("Language"), LanguagePage),
                "english, korean, 한국어, auto");
            UIBuilder.Collapsible(content, Loc.T("Invert scroll direction"), s.InvertScroll,
                v => { s.InvertScroll = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, Loc.T("Keep panel layout after restart"), s.PersistPanelLayout,
                v => { s.PersistPanelLayout = v; PanelLayout.OnOptionChanged(); notify?.Invoke(); },
                body => UIBuilder.Label(body, Loc.T("Which panels are open, and where, is kept for the rest of the session. With this on it is restored after the game restarts too; with it off it resets on each launch.")));

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, Loc.T("Appearance"));
            // Scale re-applies live while dragging — the panel counter-resizes so it stays put
            // on screen while its contents grow; see UICore.ApplyScale.
            UIBuilder.Slider(content, Loc.T("UI scale"), s.UiScale, 0.5f, 2f,
                v => { UICore.ApplyScale(v); notify?.Invoke(); }, "0.00", 0.05f);
            UIBuilder.Collapsible(content, Loc.T("Panel animations"), s.UiAnimations,
                v => { s.UiAnimations = v; notify?.Invoke(); },
                body =>
                {
                    UIBuilder.Label(body, Loc.T("Panels fade briefly and grow slightly as they open and close. Off makes them appear and disappear instantly."));
                    // Instant at the left, slow at the right. Applies live — the next open reads it.
                    UIBuilder.Slider(body, Loc.T("Animation duration (s)"), s.UiAnimSeconds, 0f, UiAnim.MaxSec,
                        v => { s.UiAnimSeconds = v; notify?.Invoke(); }, "0.00", 0.01f);
                });
            UIBuilder.AccentSwatches(content, Loc.T("Accent colour"), Theme.AccentPresets,
                new Color(s.UiAccentR, s.UiAccentG, s.UiAccentB),
                c => { UICore.ApplyAccent(c); notify?.Invoke(); });

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, Loc.T("Developer"));
            UIBuilder.Collapsible(content, Loc.T("Debug mode"), s.DebugMode,
                v => { s.DebugMode = v; notify?.Invoke(); },
                body => UIBuilder.Label(body, Loc.T("Shows [dbg] lines in the log viewer. Only needed when reporting a problem.")));
        }

        private static void LanguagePage(Transform body)
        {
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;
            UIBuilder.Label(body, Loc.T("Language for the Sapphire editor UI and help. Auto follows the game's setting."));
            UIBuilder.Button(body, Loc.T("Language") + ": " + Loc.T(LangLabels[Mathf.Clamp(s.UiLanguage, 0, 2)]), () =>
            {
                s.UiLanguage = (s.UiLanguage + 1) % 3;
                notify?.Invoke();
                // Rebuild the panel body so every built string re-localizes; this pops back to
                // the tab root, which is the only way to re-run the whole page tree.
                UICore.RebuildBody();
            });
        }
    }
}

using System;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire.UI.Pages
{
    /* "Updates" tab. Two halves: a STATIC half (the two behaviour toggles) built once, and a
       DYNAMIC half rebuilt whenever the updater's state changes — installed vs latest, the
       status line, a real progress bar while a download runs, the Install / Skip buttons that
       only exist while a release is available, and that release's notes rendered in-panel.

       UpdateService runs on a worker thread and touches no Unity API, so this page POLLS it
       (~6/s) exactly as UpdateToast does; a change in its signature tears down and rebuilds
       the dynamic block. Rebuilding a dozen rows a handful of times per session is nothing,
       and it keeps every state transition in one place instead of a web of SetActive calls. */
    internal static class PageUpdates
    {
        public static void Build(PageStack stack)
        {
            var content = stack.Root;
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;

            UIBuilder.Label(content, Loc.T("UpdatesHelp"));

            var dyn = UIBuilder.VGroup(content, "UpdateState", 4f).transform;
            var poll = dyn.gameObject.AddComponent<StatePoller>();
            poll.Host = dyn;

            UIBuilder.Spacer(content);
            UIBuilder.SectionHeader(content, Loc.T("Behaviour"));
            UIBuilder.Collapsible(content, Loc.T("Check for updates automatically"), s.AutoCheckUpdates,
                v => { s.AutoCheckUpdates = v; notify?.Invoke(); }, null);
            UIBuilder.Collapsible(content, Loc.T("Include pre-release builds"), s.UpdateIncludePrerelease,
                v => { s.UpdateIncludePrerelease = v; notify?.Invoke(); }, null);
        }

        // ── dynamic block ────────────────────────────────────────────────────

        private static void BuildState(Transform host)
        {
            var s = UICore.Settings;
            var info = UpdateService.Available;
            var st = UpdateService.Status;

            // Installed vs latest, side by side — the one question this page exists to answer.
            string latest;
            switch (st)
            {
                case UpdateStatus.Available: latest = info != null ? info.Tag : "?"; break;
                case UpdateStatus.UpToDate:  latest = Loc.T("Up to date"); break;
                case UpdateStatus.Checking:  latest = Loc.T("Checking…"); break;
                case UpdateStatus.Installed: latest = Loc.T("Installed — restart the game to apply"); break;
                case UpdateStatus.Failed:    latest = Loc.T("Failed") + ": " + UpdateService.Message; break;
                default:                     latest = Loc.T("Not checked yet"); break;
            }
            UIBuilder.Label(host, Muted(Loc.T("Installed")) + "  v" + MainClass.ModVersion
                                  + "      " + Muted(Loc.T("Latest")) + "  " + latest, 13);

            if (st == UpdateStatus.Installing)
            {
                float p = UpdateService.Progress;
                ProgressBar(host, p);
                UIBuilder.Label(host, p >= 0f
                    ? Loc.T("Downloading…") + " " + Mathf.RoundToInt(p * 100f) + "%"
                    : Loc.T("Downloading…"), 12);
            }

            if (st == UpdateStatus.Available && info != null)
            {
                string title = string.IsNullOrEmpty(info.Name) ? info.Tag : info.Tag + " — " + info.Name;
                UIBuilder.Label(host, "<b>" + title + "</b>", 14);
                UIBuilder.Button(host, Loc.T("Install") + " " + info.Tag, UpdateService.Install);
                UIBuilder.Button(host, Loc.T("Skip this version"), UpdateService.Skip);
                if (!string.IsNullOrEmpty(info.Notes))
                {
                    UIBuilder.SectionHeader(host, Loc.T("Release notes"));
                    UIBuilder.Label(host, MarkdownLite(info.Notes), 12);
                }
                if (!string.IsNullOrEmpty(info.PageUrl))
                    UIBuilder.Button(host, Loc.T("Open release page"), () => Application.OpenURL(info.PageUrl));
            }
            else if (!UpdateService.Busy)
            {
                UIBuilder.Button(host, Loc.T("Check now"), () => UpdateService.Check(true));
            }

            if (!string.IsNullOrEmpty(s.SkippedUpdateTag))
                UIBuilder.Button(host, Loc.T("Un-skip") + " " + s.SkippedUpdateTag, UpdateService.ClearSkip);
        }

        private static string Muted(string t)
            => "<color=#" + ColorUtility.ToHtmlStringRGB(Theme.TextMuted) + ">" + t + "</color>";

        // A bar, not a percentage: a number reads as done-ness, a bar reads as motion.
        private static void ProgressBar(Transform host, float p)
        {
            var row = UIBuilder.Row(host, 10f);
            var track = UIBuilder.SolidImage(row, new Color(1f, 1f, 1f, 0.08f));
            track.raycastTarget = false;
            var fillGo = UIBuilder.Rect("Fill", row.transform);
            var fr = (RectTransform)fillGo.transform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = new Vector2(Mathf.Clamp01(p < 0f ? 0f : p), 1f);
            fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = Theme.White;
            fill.color = Theme.Accent;
            fill.raycastTarget = false;
        }

        /* GitHub release bodies are markdown; the panel speaks TMP rich text. This is the
           subset our own CHANGELOG uses, nothing more: headings and bold become <b>, list
           markers become bullets, links keep their text, code spans lose their ticks. Anything
           else passes through as plain text, which is always readable. */
        internal static string MarkdownLite(string md)
        {
            if (string.IsNullOrEmpty(md)) return "";
            string t = md.Replace("\r\n", "\n");
            t = Regex.Replace(t, @"^#{1,6}\s*(.+?)\s*#*\s*$", "<b>$1</b>", RegexOptions.Multiline);
            t = Regex.Replace(t, @"\*\*(.+?)\*\*", "<b>$1</b>");
            t = Regex.Replace(t, @"^\s*[-*+]\s+", "• ", RegexOptions.Multiline);
            t = Regex.Replace(t, @"\[([^\]]+)\]\([^)]*\)", "$1");
            t = t.Replace("`", "");
            t = Regex.Replace(t, @"\n{3,}", "\n\n");
            return t.Trim();
        }

        // One runnable check for the only logic on this page.
        internal static bool SelfCheck()
        {
            bool ok = MarkdownLite("## Title\n\n- **Bold** item\n* [link](http://x) `code`\n\n\n\nend")
                      == "<b>Title</b>\n\n• <b>Bold</b> item\n• link code\n\nend"
                   && MarkdownLite("") == "" && MarkdownLite(null) == "";
            SapphireLog.Log("PageUpdates.SelfCheck: " + (ok ? "PASS" : "FAIL"));
            return ok;
        }

        private class StatePoller : MonoBehaviour
        {
            public Transform Host;
            private string _sig;
            private int _cd;
            private bool _checked;

            private void Update()
            {
                if (Host == null) return;
                if (!_checked) { _checked = true; SelfCheck(); }
                if (--_cd > 0) return;
                _cd = 10;                                   // ~6/s: enough for a progress readout
                string sig = UpdateService.Status + "|" + UpdateService.Message + "|"
                           + (UpdateService.Available != null ? UpdateService.Available.Tag : "")
                           + "|" + (UpdateService.Status == UpdateStatus.Installing
                                        ? Mathf.RoundToInt(UpdateService.Progress * 50f).ToString() : "")
                           + "|" + (UICore.Settings != null ? UICore.Settings.SkippedUpdateTag : "");
                if (sig == _sig) return;
                _sig = sig;
                for (int i = Host.childCount - 1; i >= 0; i--) Destroy(Host.GetChild(i).gameObject);
                BuildState(Host);
            }
        }
    }
}

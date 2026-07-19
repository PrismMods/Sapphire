using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Sapphire-native LEVEL SETTINGS panel — replaces the game's settings inspector VISUALS
       the same way EditorEventPanel replaces the event inspector. The level settings are just
       LevelEvents on LevelData (songSettings / levelSettings / trackSettings / … one per tab,
       eventTypes SongSettings..DecorationSettings); each is rendered by the shared EventRows
       engine from the game's own PropertyInfo registry. The game's settingsPanel stays alive
       INVISIBLY as the model — commits write its LevelEvent data + call
       UpdateSongAndLevelSettings(), so save/load and every other consumer stay in sync.

       This removes the old approach of REHOSTING the game panel inside a Sapphire card (moving
       its transform, fighting its per-frame relayout) — no more game-UI override, less lag. */
    internal static class EditorLevelMenu
    {
        private static readonly PanelKit K = new PanelKit("SapphireLevelMenu", 902, PanelW);
        private const float PanelW = 560f, RailW = 170f, HeaderH = 28f;
        private const float Pad = PanelKit.Pad, RowH = PanelKit.RowH, Gap = PanelKit.Gap;

        private static Vector2 _size = new Vector2(PanelW, 720f);
        private static RectTransform _viewport, _content, _railHost;
        private static float _scroll;
        private static int _tab;
        private static long _sig;
        private static int _scanCd;
        private static bool _open;
        private static bool _dockInited;
        private static CanvasGroup _panelCg;   // the hidden game settings panel

        private struct TabDef { public string Field; public ADOFAI.LevelEventType Type; }
        private static readonly TabDef[] Tabs =
        {
            new TabDef { Field = "songSettings",       Type = ADOFAI.LevelEventType.SongSettings },
            new TabDef { Field = "levelSettings",      Type = ADOFAI.LevelEventType.LevelSettings },
            new TabDef { Field = "trackSettings",      Type = ADOFAI.LevelEventType.TrackSettings },
            new TabDef { Field = "backgroundSettings", Type = ADOFAI.LevelEventType.BackgroundSettings },
            new TabDef { Field = "cameraSettings",     Type = ADOFAI.LevelEventType.CameraSettings },
            new TabDef { Field = "miscSettings",       Type = ADOFAI.LevelEventType.MiscSettings },
            new TabDef { Field = "decorationSettings", Type = ADOFAI.LevelEventType.DecorationSettings },
        };

        internal static bool IsOpen => _open;
        // EditorChrome suppresses its own settings rail while we manage the game panel.
        internal static bool ManagesPanel { get; private set; }

        internal static void Toggle() { _open = !_open; if (!_open) _sig = 0; }
        internal static void Open() { _open = true; }
        internal static void Close() { _open = false; _sig = 0; }

        private static readonly EventRows.Ctx _ctx = new EventRows.Ctx
        {
            PanelW = PanelW - RailW,
            MarkDirty = () => _sig = 0,
            AfterCommit = SettingsAfterCommit,
        };

        private static void SettingsAfterCommit(scnEditor ed, ADOFAI.LevelEvent evt, ADOFAI.PropertyInfo pi)
        {
            try { if (pi != null && pi.affectsPath) ed.RemakePath(true, true); } catch { }
            try { ed.UpdateSongAndLevelSettings(); } catch { } // settings' ApplyEventsToFloors analog
        }

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool inEditor = ed != null && !ed.playMode && s != null && MainClass.EditorSuiteOn;

            // The game's settings panel is redundant now — keep it off-screen whenever we're in
            // the editor (ESC-raising it would flash phantom UI). We own it → ManagesPanel.
            if (inEditor && ed.settingsPanel != null)
            {
                HideGamePanel(ed);
                ManagesPanel = true;
            }
            else
            {
                ShowGamePanel();
                ManagesPanel = false;
            }

            if (!_open || !inEditor)
            {
                K.Show(false);
                if (!inEditor) _sig = 0;
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

            // same per-frame throttle as the event panel — Sig hashes the tab's settings data,
            // so only recompute on a tab click (_sig=0), first build, or a periodic rescan
            bool dirty = _sig == 0 || !K.Built;
            if (--_scanCd <= 0) { _scanCd = 12; dirty = true; }
            if (dirty)
            {
                bool rebuild = false;
                if (!K.Built) { BuildShell(); rebuild = true; }
                long sig = Sig(ed);
                if (sig != _sig) { _sig = sig; rebuild = true; }
                if (rebuild) BuildContent(ed);
            }

            K.Show(true);
            ClampIntoView();
            TickScroll();
            TickResize();
        }

        internal static void Dispose()
        {
            ShowGamePanel();
            ManagesPanel = false;
            K.Dispose();
            _viewport = null; _content = null; _railHost = null; _panelCg = null;
            _open = false; _sig = 0;
        }

        // ── game panel hide (visuals only — it stays the model) ──────────────

        private static void HideGamePanel(scnEditor ed)
        {
            try
            {
                var go = ed.settingsPanel.gameObject;
                if (_panelCg == null || _panelCg.gameObject != go)
                    _panelCg = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
                if (_panelCg.alpha != 0f) { _panelCg.alpha = 0f; _panelCg.blocksRaycasts = false; }
                if (ed.settingsPanel.showInspector) ed.settingsPanel.ShowInspector(false, true);
            }
            catch { }
        }

        private static void ShowGamePanel()
        {
            if (_panelCg != null) { try { _panelCg.alpha = 1f; _panelCg.blocksRaycasts = true; } catch { } }
        }

        // ── data ─────────────────────────────────────────────────────────────

        private static ADOFAI.LevelEvent SettingsEvent(scnEditor ed, string field)
        {
            try
            {
                var ld = ed.levelData;
                if (ld == null) return null;
                var fi = ld.GetType().GetField(field);
                return fi != null ? fi.GetValue(ld) as ADOFAI.LevelEvent : null;
            }
            catch { return null; }
        }

        private static ADOFAI.LevelEventInfo InfoOf(ADOFAI.LevelEventType type)
        {
            try
            {
                ADOFAI.LevelEventInfo info;
                var key = type.ToString();
                if (GCS.levelEventsInfo != null && GCS.levelEventsInfo.TryGetValue(key, out info)) return info;
                if (GCS.settingsInfo != null && GCS.settingsInfo.TryGetValue(key, out info)) return info;
            }
            catch { }
            return null;
        }

        private static string TabLabel(ADOFAI.LevelEventType type)
        {
            try
            {
                bool ex;
                var loc = RDString.GetWithCheck("editor." + type, out ex, null);
                if (ex && !string.IsNullOrEmpty(loc)) return loc;
            }
            catch { }
            return type.ToString();
        }

        private static Sprite TabIcon(ADOFAI.LevelEventType type)
        {
            try
            {
                Sprite sp;
                if (GCS.levelEventIcons.TryGetValue(type, out sp)) return sp;
            }
            catch { }
            return null;
        }

        private static long Sig(scnEditor ed)
        {
            long h = 17;
            h = h * 31 + _tab;
            var evt = SettingsEvent(ed, Tabs[_tab].Field);
            // showIf gating depends on other values → hash the visible data so toggling a
            // parent setting redraws dependent rows
            try
            {
                var d = EditorEvents.EventData(evt);
                if (d != null) foreach (var kv in d) h = h * 31 + (kv.Key?.GetHashCode() ?? 0)
                                                        + (kv.Value?.GetHashCode() ?? 0);
            }
            catch { }
            return h;
        }

        // ── UI: shell built once, content rebuilds on tab/value change ───────

        private static void BuildShell()
        {
            K.Rebuild(Loc.T("Level settings"), Close, new Vector2(760f, -40f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            ResizeHandle.AttachAll(panel, true, 440f, 320f);
            K.OnDragEnd = () => K.SnapDockOnDragEnd();
            if (!_dockInited) { _dockInited = true; }

            // left tab rail
            var railGo = new GameObject("Rail", typeof(RectTransform));
            railGo.transform.SetParent(K.PanelGo.transform, false);
            _railHost = (RectTransform)railGo.transform;
            _railHost.anchorMin = new Vector2(0f, 0f); _railHost.anchorMax = new Vector2(0f, 1f);
            _railHost.pivot = new Vector2(0f, 1f);
            _railHost.offsetMin = new Vector2(Pad, Pad);
            _railHost.offsetMax = new Vector2(Pad + RailW, -HeaderH - 2f);

            // right scroll viewport
            var vpGo = new GameObject("View", typeof(RectTransform));
            vpGo.transform.SetParent(K.PanelGo.transform, false);
            _viewport = (RectTransform)vpGo.transform;
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            _viewport.offsetMin = new Vector2(Pad + RailW + Pad, Pad);
            _viewport.offsetMax = new Vector2(-Pad, -HeaderH - 2f);
            vpGo.AddComponent<RectMask2D>();
            var vpImg = vpGo.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.01f);
            vpImg.raycastTarget = true;

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vpGo.transform, false);
            _content = (RectTransform)cGo.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static void BuildContent(scnEditor ed)
        {
            BuildRail();
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);

            _ctx.Content = _content;
            _ctx.PanelW = _viewport != null ? _viewport.rect.width : PanelW - RailW;

            var evt = SettingsEvent(ed, Tabs[_tab].Field);
            var info = InfoOf(Tabs[_tab].Type);
            float y = -2f;
            if (evt == null || info == null)
            {
                EventRows.Label(_content, Loc.T("(settings unavailable)"), Pad, y, _ctx.PanelW - Pad * 2f, RowH, Theme.TextMuted);
                y -= RowH + Gap;
            }
            else y = EventRows.Render(_ctx, ed, info, evt, y);
            _content.sizeDelta = new Vector2(0f, -y + 6f);
            ClampScroll();
        }

        private static void BuildRail()
        {
            if (_railHost == null) return;
            for (int i = _railHost.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_railHost.GetChild(i).gameObject);

            const float rowH = 34f, gap = 4f;
            float y = 0f;
            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                var go = new GameObject("Tab", typeof(RectTransform));
                go.transform.SetParent(_railHost, false);
                var r = (RectTransform)go.transform;
                r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
                r.pivot = new Vector2(0.5f, 1f);
                r.anchoredPosition = new Vector2(0f, y);
                r.sizeDelta = new Vector2(0f, rowH);
                var bg = go.AddComponent<RoundedRectGraphic>();
                bg.Radius = 6f;
                bg.color = i == _tab
                    ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f)
                    : new Color(1f, 1f, 1f, 0.05f);
                bg.raycastTarget = true;

                var icon = TabIcon(Tabs[i].Type);
                if (icon != null)
                {
                    var iGo = new GameObject("I", typeof(RectTransform));
                    iGo.transform.SetParent(go.transform, false);
                    var ir = (RectTransform)iGo.transform;
                    ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f);
                    ir.pivot = new Vector2(0f, 0.5f);
                    ir.anchoredPosition = new Vector2(8f, 0f);
                    ir.sizeDelta = new Vector2(20f, 20f);
                    var img = iGo.AddComponent<Image>();
                    img.sprite = icon; img.preserveAspect = true; img.raycastTarget = false;
                }
                var lGo = new GameObject("L", typeof(RectTransform));
                lGo.transform.SetParent(go.transform, false);
                var lr = (RectTransform)lGo.transform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = new Vector2(icon != null ? 34f : 10f, 0f); lr.offsetMax = new Vector2(-6f, 0f);
                var lt = UIBuilder.Tmp(lGo, TabLabel(Tabs[i].Type), 12.5f, TextAnchor.MiddleLeft, Theme.Text);
                lt.textWrappingMode = TextWrappingModes.NoWrap;
                lt.overflowMode = TextOverflowModes.Ellipsis;
                lt.raycastTarget = false;

                UI.ClickHandler.Attach(go, () => { _tab = idx; _scroll = 0f; _sig = 0; });
                y -= rowH + gap;
            }
        }

        // ── scroll / resize / clamp ─────────────────────────────────────────

        private static void TickScroll()
        {
            if (_viewport == null || _content == null) return;
            float wheel = MainClass.WheelY;
            if (Mathf.Abs(wheel) < 0.01f) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_viewport, Input.mousePosition, null)) return;
            _scroll = Mathf.Clamp(_scroll + wheel * 60f, 0f,
                Mathf.Max(0f, _content.sizeDelta.y - _viewport.rect.height));
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static void ClampScroll()
        {
            if (_viewport == null || _content == null) return;
            _scroll = Mathf.Clamp(_scroll, 0f, Mathf.Max(0f, _content.sizeDelta.y - _viewport.rect.height));
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        internal static bool Hovered
        {
            get
            {
                try
                {
                    return K.Visible && RectTransformUtility.RectangleContainsScreenPoint(
                        (RectTransform)K.PanelGo.transform, Input.mousePosition, null);
                }
                catch { return false; }
            }
        }

        private static void TickResize()
        {
            if (!K.Built) return;
            var r = (RectTransform)K.PanelGo.transform;
            if ((r.sizeDelta - _size).sqrMagnitude > 1f)
            {
                _size = r.sizeDelta;
                _sig = 0;  // width changed → rows re-lay to the new content width
            }
        }

        private static void ClampIntoView()
        {
            if (!K.Built) return;
            var r = (RectTransform)K.PanelGo.transform;
            var canvas = (RectTransform)K.CanvasGo.transform;
            var p = r.anchoredPosition;
            p.x = Mathf.Clamp(p.x, 0f, Mathf.Max(0f, canvas.rect.width - 80f));
            p.y = Mathf.Clamp(p.y, -(canvas.rect.height - 40f), 0f);
            if ((p - r.anchoredPosition).sqrMagnitude > 0.01f) r.anchoredPosition = p;
        }
    }
}

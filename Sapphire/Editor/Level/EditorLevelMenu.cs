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
        private static readonly PanelKit K = new PanelKit("SapphireLevelMenu", 902, PanelW, focusable: true);
        private const float PanelW = 560f, RailW = 146f, HeaderH = 28f + Gap; // Gap: breathing room under the title
        // Narrow panels collapse the tab rail to just icons.
        private const float IconRailW = 34f, CollapseW = 430f;
        private static float CurRailW() => _size.x < CollapseW ? IconRailW : RailW;
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
        internal static PanelKit Kit => K;
        internal static void SetOpen(bool v) { if (v) Open(); else Close(); }
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

        internal static bool TabAvailable()
        {
            if (MainClass.Settings == null || !MainClass.EditorSuiteOn) return false;
            try { var ed = scnEditor.instance; return ed != null && !ed.playMode; } catch { return false; }
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
            // ESC dismisses this as a POPUP only. Docked, it is workspace furniture, and ESC is
            // the editor's constantly-pressed deselect key — it kept knocking the panel out of
            // the dock, forcing a re-click on the rail chip to get it back.
            if (K.DockSide == 0 && Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

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
                // blocksRaycasts every frame, not only when alpha flips: the game can re-assert
                // raycasts on its (invisible) settings panel, and a stuck-true blocker eats clicks.
                if (_panelCg.alpha != 0f) _panelCg.alpha = 0f;
                if (_panelCg.blocksRaycasts) _panelCg.blocksRaycasts = false;
                if (ed.settingsPanel.showInspector) ed.settingsPanel.ShowInspector(false, true);
            }
            catch { }
        }

        private static void ShowGamePanel()
        {
            if (_panelCg != null) { try { _panelCg.alpha = 1f; _panelCg.blocksRaycasts = true; } catch { } }
        }

        // ── data ─────────────────────────────────────────────────────────────

        private static readonly System.Collections.Generic.Dictionary<string, System.Reflection.FieldInfo>
            _settingsFields = new System.Collections.Generic.Dictionary<string, System.Reflection.FieldInfo>();

        private static ADOFAI.LevelEvent SettingsEvent(scnEditor ed, string field)
        {
            try
            {
                var ld = ed.levelData;
                if (ld == null) return null;
                // Type.GetField is a name-based lookup; this is reached from Sig() on a timer
                // while the panel is open, and the field set is fixed. Resolve each one once.
                System.Reflection.FieldInfo fi;
                if (!_settingsFields.TryGetValue(field, out fi))
                {
                    fi = ld.GetType().GetField(field);
                    _settingsFields[field] = fi;   // cache misses too — a null stays null
                }
                return fi != null ? fi.GetValue(ld) as ADOFAI.LevelEvent : null;
            }
            catch { return null; }
        }

        internal static ADOFAI.LevelEventInfo InfoOf(ADOFAI.LevelEventType type)
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
            h = h * 31 + (_approvalOpen ? 1 : 0);
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
            K.Rebuild(Loc.T("Level settings"), Close, new Vector2(760f, -72f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            ResizeHandle.AttachAll(panel, true, 300f, 320f); // allow narrow → rail collapses to icons
            K.OnDragEnd = () => K.SnapDockOnDragEnd();
            if (!_dockInited) { _dockInited = true; K.SetDock(1); } // left-dock tab, same as Events

            // left tab rail
            var railGo = new GameObject("Rail", typeof(RectTransform));
            railGo.transform.SetParent(K.PanelGo.transform, false);
            _railHost = (RectTransform)railGo.transform;
            _railHost.anchorMin = new Vector2(0f, 0f); _railHost.anchorMax = new Vector2(0f, 1f);
            _railHost.pivot = new Vector2(0f, 1f);
            _railHost.offsetMin = new Vector2(Pad, 3f);
            _railHost.offsetMax = new Vector2(Pad + RailW, -HeaderH);

            // right scroll viewport
            var vpGo = new GameObject("View", typeof(RectTransform));
            vpGo.transform.SetParent(K.PanelGo.transform, false);
            _viewport = (RectTransform)vpGo.transform;
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            _viewport.offsetMin = new Vector2(Pad + RailW + Pad, 3f);
            _viewport.offsetMax = new Vector2(-Pad, -HeaderH);
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
            // Re-fit the rail + viewport to the current width (collapsed rail on narrow panels),
            // BEFORE laying out rows. PanelW is derived from _size (viewport.rect lags a resize
            // frame), so rows always use the correct content width.
            float railW = CurRailW();
            bool collapsed = railW < 100f;
            if (_railHost != null) _railHost.offsetMax = new Vector2(Pad + railW, -HeaderH);
            if (_viewport != null) _viewport.offsetMin = new Vector2(Pad + railW + Pad, 3f);
            BuildRail(collapsed);
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);

            _ctx.Content = _content;
            _ctx.PanelW = Mathf.Max(120f, _size.x - railW - Pad * 3f);

            float y = -2f;
            if (Tabs[_tab].Type == ADOFAI.LevelEventType.LevelSettings)
                y = ArtistApprovalChip(ed, y);

            var evt = SettingsEvent(ed, Tabs[_tab].Field);
            var info = InfoOf(Tabs[_tab].Type);
            if (evt == null || info == null)
            {
                EventRows.Label(_content, Loc.T("(settings unavailable)"), Pad, y, _ctx.PanelW - Pad * 2f, RowH, Theme.TextMuted);
                y -= RowH + Gap;
            }
            else y = EventRows.Render(_ctx, ed, info, evt, y);
            _content.sizeDelta = new Vector2(0f, -y + 6f);
            ClampScroll();
        }

        // ── artist approval chip (Level tab) ──────────────────────────────────
        private static bool _approvalOpen;   // chip expanded to show the condition text

        // The game evaluates artist permission OUTSIDE the settings registry (ApprovalLevel +
        // its disclaimer strings), so the registry-driven rows can't show it. Chip at the top
        // of the Level tab; click expands the condition text.
        private static float ArtistApprovalChip(scnEditor ed, float y)
        {
            // The verified-artist list is fetched by the GAME's inspector, which Sapphire never
            // opens — without this the array stays null and every lookup below fails silently.
            EditorArtistPicker.EnsureLoaded(() => _sig = 0);
            string artist = "";
            try { artist = (ed.levelData.artist ?? "").Trim(); } catch { }
            if (artist.Length == 0) return y;   // nothing to evaluate
            ApprovalLevel lvl;
            try { lvl = ed.ApprovalLevelForArtist(artist); } catch { return y; }

            float w = _ctx.PanelW - Pad * 2f;
            var bg = EventRows.Cell(_content,
                Loc.T("Artist permission") + ": " + ApprovalText(lvl) + "   " + (_approvalOpen ? "‹" : "›"),
                Pad, y, w, RowH,
                () => { _approvalOpen = !_approvalOpen; _sig = 0; }, false, TextAnchor.MiddleLeft);
            bg.color = ApprovalTint(lvl);
            y -= RowH + Gap;

            if (_approvalOpen)
            {
                string detail = ApprovalDetail(lvl, artist);
                var tGo = new GameObject("D", typeof(RectTransform));
                var tmp = UIBuilder.Tmp(tGo, detail, 11f, TextAnchor.UpperLeft, Theme.TextMuted);
                tmp.enableWordWrapping = true;
                tmp.raycastTarget = false;
                // Measured, not estimated: the string is game-localized (CJK is ~2x the width per
                // character) and the panel font is user-swappable, so a chars-per-line guess
                // under-estimates and TMP has no clip here — the overflow lands on the rows below.
                float textW = w - 16f;                       // the cell's 8f horizontal insets
                float h = Mathf.Max(RowH, tmp.GetPreferredValues(detail, textW, 0f).y + 8f);
                var lbl = EventRows.Cell(_content, "", Pad, y, w, h, () => { }, false);
                lbl.color = new Color(1f, 1f, 1f, 0.03f);
                tGo.transform.SetParent(lbl.transform, false);
                var tr = (RectTransform)tGo.transform;
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
                tr.offsetMin = new Vector2(8f, 4f); tr.offsetMax = new Vector2(-8f, -4f);
                y -= h + Gap;
            }
            return y - Gap;
        }

        private static string ApprovalText(ApprovalLevel lvl)
        {
            try
            {
                bool ex;
                var s = RDString.GetWithCheck("editor.artistDisclaimer." + lvl, out ex, null);
                if (ex && !string.IsNullOrEmpty(s)) return s;
            }
            catch { }
            return lvl.ToString();
        }

        private static string ApprovalDetail(ApprovalLevel lvl, string artist)
        {
            string key = lvl == ApprovalLevel.Declined
                ? "editor.artistDisclaimer.conditionDeclinedDescription"
                : lvl == ApprovalLevel.ListingRejected
                    ? "editor.artistDisclaimer.conditionListingRejectedDescription"
                    : "editor.artistDisclaimer.conditionDescription";
            try
            {
                // The game's own ArtistUIDisclaimer always passes the artist name for these keys;
                // with a null dict RDString leaves the [artist] token unreplaced.
                var args = new System.Collections.Generic.Dictionary<string, object> { ["artist"] = artist };
                bool ex;
                var s = RDString.GetWithCheck(key, out ex, args);
                if (ex && !string.IsNullOrEmpty(s)) return s;
            }
            catch { }
            return "";
        }

        private static Color ApprovalTint(ApprovalLevel lvl)
        {
            switch (lvl)
            {
                case ApprovalLevel.Allowed:           return new Color(0.42f, 0.78f, 0.48f, 0.28f);
                case ApprovalLevel.MostlyAllowed:
                case ApprovalLevel.PartiallyDeclined: return new Color(0.95f, 0.72f, 0.32f, 0.28f);
                case ApprovalLevel.Declined:
                case ApprovalLevel.ListingRejected:   return new Color(0.89f, 0.40f, 0.43f, 0.28f);
            }
            return new Color(1f, 1f, 1f, 0.06f);
        }

        private static void BuildRail(bool collapsed)
        {
            if (_railHost == null) return;
            for (int i = _railHost.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_railHost.GetChild(i).gameObject);

            const float rowH = 26f, gap = 3f; // match the compact row scale of the other panels
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
                // Same shape and fill as the palette buttons (PanelKit.Cell) — this rail used to
                // be the one control in the suite with its own radius and its own border-less fill.
                var bg = go.AddComponent<RoundedRectGraphic>();
                bg.Radius = 5f;
                bg.color = i == _tab
                    ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.35f)
                    : new Color(1f, 1f, 1f, 0.08f);
                bg.BorderWidth = 1f;
                bg.BorderColor = new Color(1f, 1f, 1f, 0.1f);
                bg.raycastTarget = true;

                var icon = TabIcon(Tabs[i].Type);
                if (icon != null)
                {
                    var iGo = new GameObject("I", typeof(RectTransform));
                    iGo.transform.SetParent(go.transform, false);
                    var ir = (RectTransform)iGo.transform;
                    // collapsed: icon centred (no label); expanded: icon left, label beside it
                    ir.anchorMin = ir.anchorMax = new Vector2(collapsed ? 0.5f : 0f, 0.5f);
                    ir.pivot = new Vector2(0.5f, 0.5f);
                    ir.anchoredPosition = new Vector2(collapsed ? 0f : 16f, 0f);
                    ir.sizeDelta = new Vector2(18f, 18f);
                    var img = iGo.AddComponent<Image>();
                    img.sprite = icon; img.preserveAspect = true; img.raycastTarget = false;
                }
                if (!collapsed)
                {
                    var lGo = new GameObject("L", typeof(RectTransform));
                    lGo.transform.SetParent(go.transform, false);
                    var lr = (RectTransform)lGo.transform;
                    lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                    lr.offsetMin = new Vector2(icon != null ? 29f : 8f, 0f); lr.offsetMax = new Vector2(-6f, 0f);
                    var lt = UIBuilder.Tmp(lGo, TabLabel(Tabs[i].Type), 12f, TextAnchor.MiddleLeft, Theme.Text);
                    lt.textWrappingMode = TextWrappingModes.NoWrap;
                    lt.overflowMode = TextOverflowModes.Ellipsis;
                    lt.raycastTarget = false;
                }

                UI.ClickHandler.Attach(go, () =>
                {
                    // Decorations is a browser, not a settings object — it has its own window.
                    if (Tabs[idx].Type == ADOFAI.LevelEventType.DecorationSettings)
                    { EditorDecoMenu.Open(); EditorDecoMenu.Kit.BringToFront(); return; }
                    _tab = idx; _scroll = 0f; _sig = 0;
                });
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

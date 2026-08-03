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
        private const float PanelW = 560f, RailW = 146f, HeaderH = 28f;
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
            if (Tabs[_tab].Type == ADOFAI.LevelEventType.DecorationSettings)
            {
                h = h * 31 + _decoSel;
                try
                {
                    var decos = DecoList(ed);
                    h = h * 31 + (decos != null ? decos.Count : 0);
                    foreach (var t in _decoExpanded) h = h * 31 + t.GetHashCode();
                    if (decos != null) for (int i = 0; i < decos.Count; i++) h = h * 31 + DecoTag(decos[i]).GetHashCode();
                    if (_decoSel >= 0 && decos != null && _decoSel < decos.Count)
                    {
                        var dd = EditorEvents.EventData(decos[_decoSel]);
                        if (dd != null) foreach (var kv in dd) h = h * 31 + (kv.Key?.GetHashCode() ?? 0) + (kv.Value?.GetHashCode() ?? 0);
                    }
                }
                catch { }
                return h;
            }
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
            ResizeHandle.AttachAll(panel, true, 300f, 320f); // allow narrow → rail collapses to icons
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
            // Re-fit the rail + viewport to the current width (collapsed rail on narrow panels),
            // BEFORE laying out rows. PanelW is derived from _size (viewport.rect lags a resize
            // frame), so rows always use the correct content width.
            float railW = CurRailW();
            bool collapsed = railW < 100f;
            if (_railHost != null) _railHost.offsetMax = new Vector2(Pad + railW, -HeaderH - 2f);
            if (_viewport != null) _viewport.offsetMin = new Vector2(Pad + railW + Pad, Pad);
            BuildRail(collapsed);
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);

            _ctx.Content = _content;
            _ctx.PanelW = Mathf.Max(120f, _size.x - railW - Pad * 3f);

            float y = -2f;
            // Decorations aren't a flat settings object — they're a LIST of AddDecoration events.
            // Sapphire-native browser (list + tag folders + per-item EventRows inspector).
            if (Tabs[_tab].Type == ADOFAI.LevelEventType.DecorationSettings)
            {
                y = RenderDecoBrowser(ed, y);
                _content.sizeDelta = new Vector2(0f, -y + 6f);
                ClampScroll();
                return;
            }

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

        // ── decoration browser (Sapphire-native; replaces the game's deco panel) ──
        private static int _decoSel = -1;   // index into ed.decorations, -1 = none
        private static bool _decoGrid;       // false = grouped list, true = thumbnail grid
        // Folders are COLLAPSED by default — track the ones the user has expanded.
        private static readonly System.Collections.Generic.HashSet<string> _decoExpanded =
            new System.Collections.Generic.HashSet<string>();
        private static readonly EventRows.Ctx _decoCtx = new EventRows.Ctx
        {
            MarkDirty = () => _sig = 0,
            AfterCommit = (ed, evt, pi) => { try { ed.UpdateDecorationObjects(); } catch { } },
        };

        private static System.Collections.Generic.List<ADOFAI.LevelEvent> DecoList(scnEditor ed)
        {
            try { return ed.decorations; } catch { return null; }   // DecorationsArray<LevelEvent> : List<LevelEvent>
        }

        private static string DecoDataStr(ADOFAI.LevelEvent evt, string key)
        {
            try
            {
                var d = EditorEvents.EventData(evt);
                object v;
                if (d != null && d.TryGetValue(key, out v) && v is string s) return s;
            }
            catch { }
            return "";
        }

        private static string DecoTag(ADOFAI.LevelEvent evt) => DecoDataStr(evt, "tag");

        private static string DecoType(ADOFAI.LevelEvent evt)
        {
            try
            {
                switch (evt.eventType)
                {
                    case ADOFAI.LevelEventType.AddDecoration: return Loc.T("image");
                    case ADOFAI.LevelEventType.AddText: return Loc.T("text");
                    case ADOFAI.LevelEventType.AddObject: return Loc.T("object");
                    case ADOFAI.LevelEventType.AddParticle: return Loc.T("particle");
                    case ADOFAI.LevelEventType.AddComponent: return Loc.T("component");
                }
                return evt.eventType.ToString();
            }
            catch { return "?"; }
        }

        // Tile the deco is anchored to, if any: data["relativeTo"] is a Tuple<int, TileRelativeTo>
        // whose Item1 is the tile number. Null when not tile-relative.
        private static int? DecoTile(ADOFAI.LevelEvent evt)
        {
            try
            {
                var d = EditorEvents.EventData(evt);
                object v;
                if (d != null && d.TryGetValue("relativeTo", out v) && v != null)
                {
                    var p = v.GetType().GetProperty("Item1");
                    if (p != null) { object o = p.GetValue(v, null); if (o is int) return (int)o; }
                }
            }
            catch { }
            return null;
        }

        private static string DecoLabel(ADOFAI.LevelEvent evt, int i)
        {
            string name;
            string img = DecoDataStr(evt, "decorationImage");
            string txt = DecoDataStr(evt, "decText");
            if (!string.IsNullOrEmpty(img)) name = System.IO.Path.GetFileName(img);
            else if (!string.IsNullOrEmpty(txt)) name = "“" + (txt.Length > 16 ? txt.Substring(0, 16) : txt) + "”";
            else name = "#" + i;
            int? tile = DecoTile(evt);
            return DecoType(evt) + "  " + name + (tile.HasValue ? "   · T" + tile.Value : "");
        }

        private static void SelectDeco(scnEditor ed, int i)
        {
            _decoSel = (_decoSel == i) ? -1 : i;
            try
            {
                if (_decoSel >= 0) ed.SelectDecoration(_decoSel, false, false, false, false); // no camera jump, no game panel
                else ed.DeselectAllDecorations();
            }
            catch { }
            _sig = 0;
        }

        private static void AddDeco(scnEditor ed)
        {
            try
            {
                ed.AddDecoration(ADOFAI.LevelEventType.AddDecoration);   // append at end, returns the new event
                ed.UpdateDecorationObjects();
                var decos = DecoList(ed);
                _decoSel = decos != null ? decos.Count - 1 : -1;
            }
            catch (Exception ex) { SapphireLog.Log("Deco add failed: " + ex.Message); }
            _sig = 0;
        }

        private static float RenderDecoBrowser(scnEditor ed, float y)
        {
            float w = _ctx.PanelW - Pad * 2f;
            float bw = (w - Gap) * 0.5f;
            EventRows.Cell(_content, Loc.T("+ Decoration"), Pad, y, bw, RowH, () => AddDeco(ed), true);
            EventRows.Cell(_content, Loc.T("Duplicate"), Pad + bw + Gap, y, bw, RowH,
                () => { try { ed.DuplicateDecorations(); } catch { } _sig = 0; }, true);
            y -= RowH + Gap;
            EventRows.Cell(_content, _decoGrid ? Loc.T("View: Grid (thumbnails)") : Loc.T("View: List"), Pad, y, w, RowH,
                () => { _decoGrid = !_decoGrid; _sig = 0; }, true);
            y -= RowH + Gap * 2f;

            var decos = DecoList(ed);
            if (decos == null || decos.Count == 0)
            {
                EventRows.Label(_content, Loc.T("(no decorations)"), Pad, y, w, RowH, Theme.TextMuted);
                return y - RowH - Gap;
            }
            if (_decoSel >= decos.Count) _decoSel = -1;
            if (_decoGrid) return RenderDecoGridBody(ed, decos, y);

            // group indices by tag, preserving first-seen order
            var order = new System.Collections.Generic.List<string>();
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>();
            for (int i = 0; i < decos.Count; i++)
            {
                string tag = DecoTag(decos[i]);
                System.Collections.Generic.List<int> lst;
                if (!groups.TryGetValue(tag, out lst)) { lst = new System.Collections.Generic.List<int>(); groups[tag] = lst; order.Add(tag); }
                lst.Add(i);
            }

            foreach (var tag in order)
            {
                var lst = groups[tag];
                string tagCopy = tag;
                bool expanded = _decoExpanded.Contains(tag);   // collapsed by default
                string hdr = (expanded ? "‹  " : "›  ")
                    + (tag.Length == 0 ? Loc.T("(untagged)") : tag) + "   (" + lst.Count + ")";
                var hbg = EventRows.Cell(_content, hdr, Pad, y, w, RowH,
                    () => { if (!_decoExpanded.Remove(tagCopy)) _decoExpanded.Add(tagCopy); _sig = 0; },
                    false, TextAnchor.MiddleLeft);
                hbg.color = new Color(1f, 1f, 1f, 0.06f);
                y -= RowH + Gap;
                if (!expanded) continue;

                foreach (int idx in lst)
                {
                    int i = idx;
                    bool sel = i == _decoSel;
                    var rbg = EventRows.Cell(_content, "    " + DecoLabel(decos[i], i), Pad + 10f, y, w - 10f, RowH,
                        () => SelectDeco(ed, i), false, TextAnchor.MiddleLeft);
                    if (sel) rbg.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f);
                    y -= RowH + Gap;
                    if (sel) y = DecoInspector(ed, decos[i], y);
                }
            }
            return y;
        }

        // Inline property inspector for one decoration (its OWN event type), shared by list + grid.
        private static float DecoInspector(scnEditor ed, ADOFAI.LevelEvent evt, float y)
        {
            var info = InfoOf(evt.eventType);   // image/text/object/particle differ
            if (info == null) return y;
            _decoCtx.Content = _content;
            _decoCtx.PanelW = _ctx.PanelW;
            y = EventRows.Render(_decoCtx, ed, info, evt, y);
            return y - Gap;
        }

        // The game already loaded each decoration's sprite — reuse it (no file IO / path resolution).
        private static Sprite DecoSprite(ADOFAI.LevelEvent evt)
        {
            try
            {
                var dec = scrDecorationManager.GetDecoration(evt);
                if (dec == null) return null;
                var sr = dec.GetComponentInChildren<SpriteRenderer>();
                return sr != null ? sr.sprite : null;
            }
            catch { return null; }
        }

        // Thumbnail gallery of every decoration; image decos show their sprite, others a typed
        // placeholder. Click a cell = same select+inspect as the list. Selected cell's inspector
        // renders full-width below the whole grid.
        private static float RenderDecoGridBody(scnEditor ed, System.Collections.Generic.List<ADOFAI.LevelEvent> decos, float y)
        {
            float w = _ctx.PanelW - Pad * 2f;
            const float gap = 6f, lblH = 16f, target = 76f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((w + gap) / (target + gap)));
            float cw = (w - gap * (cols - 1)) / cols;
            float cellH = cw + lblH;   // square thumb + label strip
            float gridTop = y;

            for (int i = 0; i < decos.Count; i++)
            {
                int col = i % cols, row = i / cols;
                float cx = Pad + col * (cw + gap);
                float cy = gridTop - row * (cellH + gap);
                int idx = i;
                bool sel = i == _decoSel;
                DecoGridCell(ed, decos[i], idx, cx, cy, cw, cellH, lblH, sel);
            }
            int rows = (decos.Count + cols - 1) / cols;
            y = gridTop - rows * (cellH + gap) - Gap;

            if (_decoSel >= 0 && _decoSel < decos.Count) y = DecoInspector(ed, decos[_decoSel], y);
            return y;
        }

        private static void DecoGridCell(scnEditor ed, ADOFAI.LevelEvent evt, int idx,
            float x, float yTop, float cw, float cellH, float lblH, bool sel)
        {
            var cellGo = new GameObject("DecoCell", typeof(RectTransform));
            cellGo.transform.SetParent(_content, false);
            var cr = (RectTransform)cellGo.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(0f, 1f);
            cr.pivot = new Vector2(0f, 1f);
            cr.anchoredPosition = new Vector2(x, yTop);
            cr.sizeDelta = new Vector2(cw, cellH);
            var bg = cellGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = sel ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f)
                           : new Color(1f, 1f, 1f, 0.05f);
            UI.ClickHandler.Attach(cellGo, () => SelectDeco(ed, idx));

            // thumbnail (square, top of the cell)
            float pad = 5f, thumb = cw - pad * 2f;
            var sp = DecoSprite(evt);
            var imgGo = new GameObject("Thumb", typeof(RectTransform));
            imgGo.transform.SetParent(cellGo.transform, false);
            var ir = (RectTransform)imgGo.transform;
            ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 1f);
            ir.pivot = new Vector2(0.5f, 1f);
            ir.anchoredPosition = new Vector2(0f, -pad);
            ir.sizeDelta = new Vector2(thumb, thumb);
            var img = imgGo.AddComponent<UnityEngine.UI.Image>();
            img.raycastTarget = false;
            if (sp != null) { img.sprite = sp; img.preserveAspect = true; img.color = Color.white; }
            else { img.color = new Color(1f, 1f, 1f, 0.08f); }   // no sprite → faint placeholder plate

            // label strip (type + name), one line, ellipsis-ish via truncation in DecoLabel
            var lblGo = new GameObject("Lbl", typeof(RectTransform));
            lblGo.transform.SetParent(cellGo.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(1f, 0f);
            lr.pivot = new Vector2(0.5f, 0f);
            lr.offsetMin = new Vector2(3f, 1f); lr.offsetMax = new Vector2(-3f, lblH);
            var lbl = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.font = UI.Theme.TmpFont;
            lbl.fontSize = 9.5f;
            lbl.color = sp != null ? new Color(0.85f, 0.85f, 0.88f, 1f) : Theme.TextMuted;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.enableWordWrapping = false;
            lbl.overflowMode = TextOverflowModes.Ellipsis;
            lbl.raycastTarget = false;
            lbl.text = sp != null ? System.IO.Path.GetFileName(DecoDataStr(evt, "decorationImage")) : DecoType(evt);
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

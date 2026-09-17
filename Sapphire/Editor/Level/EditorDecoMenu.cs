using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Standalone DECORATIONS window — the browser that used to be the level-settings panel's
       seventh tab. Decorations are not a flat settings object like the other six (they are a
       LIST of AddDecoration events with their own selection, tag folders, grid view and add /
       duplicate / delete verbs), so sharing that panel's tab body meant a list view competing
       with six inspector views for the same width and scroll state. It gets its own window; the
       level panel's Decorations rail entry opens it.

       Per-item editing still belongs to EditorDecoInspector — this is the browser only. */
    internal static class EditorDecoMenu
    {
        private static readonly PanelKit K = new PanelKit("SapphireDecoMenu", 903, PanelW, focusable: true);
        private const float PanelW = 380f, HeaderH = 28f + Gap;
        private const float Pad = PanelKit.Pad, RowH = PanelKit.RowH, Gap = PanelKit.Gap;

        private static Vector2 _size = new Vector2(PanelW, 560f);
        private static RectTransform _viewport, _content;
        private static float _scroll;
        private static bool _open;
        private static long _sig;
        private static int _scanCd;
        private static string _search = "";

        internal static bool IsOpen => _open;
        internal static PanelKit Kit => K;
        internal static void SetOpen(bool v) { if (v) Open(); else Close(); }
        internal static void Open() { _open = true; _sig = 0; }
        internal static void Close() { _open = false; _sig = 0; }
        internal static void Toggle() { if (_open) Close(); else Open(); }
        internal static bool TabAvailable()
        {
            if (MainClass.Settings == null || !MainClass.EditorSuiteOn) return false;
            try { var ed = scnEditor.instance; return ed != null && !ed.playMode; } catch { return false; }
        }

        private static readonly EventRows.Ctx _ctx = new EventRows.Ctx
        {
            PanelW = PanelW,
            MarkDirty = () => _sig = 0,
        };

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool inEditor = ed != null && !ed.playMode && s != null && MainClass.EditorSuiteOn;
            if (!_open || !inEditor)
            {
                K.Show(false);
                if (!inEditor) _sig = 0;
                return;
            }
            if (K.DockSide == 0 && Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

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
            K.Dispose();
            _viewport = null; _content = null;
            _open = false; _sig = 0;
        }

        /* Same throttle as the level panel: hash what is on screen (selection, folder state,
           tags, and the selected item's own data) so a rebuild only happens on a real change. */
        private static long Sig(scnEditor ed)
        {
            long h = 17;
            h = h * 31 + (_decoGrid ? 1 : 0);
            h = h * 31 + _search.GetHashCode();
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
                    if (dd != null) foreach (var kv in dd) h = h * 31 + (kv.Value != null ? kv.Value.GetHashCode() : 0);
                }
            }
            catch { }
            return h;
        }

        private static void BuildShell()
        {
            K.Rebuild(Loc.T("Decorations"), Close, new Vector2(560f, -96f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            ResizeHandle.AttachAll(panel, true, 280f, 260f);
            K.OnDragEnd = () => K.SnapDockOnDragEnd();

            var vpGo = new GameObject("View", typeof(RectTransform));
            vpGo.transform.SetParent(K.PanelGo.transform, false);
            _viewport = (RectTransform)vpGo.transform;
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            _viewport.offsetMin = new Vector2(Pad, 3f);
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
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);
            _ctx.Content = _content;
            _ctx.PanelW = Mathf.Max(120f, _size.x - Pad * 2f);
            float y = RenderDecoBrowser(ed, -2f);
            _content.sizeDelta = new Vector2(0f, -y + 6f);
            ClampScroll();
        }

        // ── decoration browser (Sapphire-native; replaces the game's deco panel) ──
        private static int _decoSel = -1;   // index into ed.decorations, -1 = none
        private static bool _decoGrid;       // false = grouped list, true = thumbnail grid
        // Folders are COLLAPSED by default — track the ones the user has expanded.
        private static readonly System.Collections.Generic.HashSet<string> _decoExpanded =
            new System.Collections.Generic.HashSet<string>();

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
        internal static string DecoTagOf(ADOFAI.LevelEvent evt) => DecoTag(evt);

        // The inspector's × clears the browser's selection too, so "which decoration is
        // selected" never has two answers.
        internal static void ClearDecoSelection()
        {
            _decoSel = -1;
            _sig = 0;
        }

        internal static string DecoTypeName(ADOFAI.LevelEventType type)
        {
            switch (type)
            {
                case ADOFAI.LevelEventType.AddDecoration: return Loc.T("image");
                case ADOFAI.LevelEventType.AddText: return Loc.T("text");
                case ADOFAI.LevelEventType.AddObject: return Loc.T("object");
                case ADOFAI.LevelEventType.AddParticle: return Loc.T("particle");
                case ADOFAI.LevelEventType.AddComponent: return Loc.T("component");
            }
            return type.ToString();
        }

        private static string DecoType(ADOFAI.LevelEvent evt)
        {
            try { return DecoTypeName(evt.eventType); }
            catch { return "?"; }
        }

        // Every event type AddDecoration accepts. Filtered through the game's own
        // LevelEventInfo.isDecoration so a game version that drops or renames one just
        // drops it from the menu instead of producing an event nothing can render.
        private static readonly ADOFAI.LevelEventType[] DecoAddTypes =
        {
            ADOFAI.LevelEventType.AddDecoration,
            ADOFAI.LevelEventType.AddText,
            ADOFAI.LevelEventType.AddObject,
            ADOFAI.LevelEventType.AddParticle,
            ADOFAI.LevelEventType.AddComponent,
        };

        private static System.Collections.Generic.List<ADOFAI.LevelEventType> AddableDecoTypes()
        {
            var list = new System.Collections.Generic.List<ADOFAI.LevelEventType>(DecoAddTypes.Length);
            foreach (var t in DecoAddTypes)
            {
                var info = EditorLevelMenu.InfoOf(t);
                if (info == null) continue;
                bool isDeco;
                try { isDeco = info.isDecoration; } catch { isDeco = false; }
                if (isDeco) list.Add(t);
            }
            if (list.Count == 0) list.Add(ADOFAI.LevelEventType.AddDecoration);
            return list;
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

        // Select by LevelEvent, never by index: the int overload indexes
        // scrDecorationManager.allDecorations, which is a DIFFERENT list from
        // levelData.decorations (our _decoSel space) and drifts whenever a deco fails to
        // instantiate — an off-by-N there means acting on the wrong decoration.
        private static void SelectDecoEvent(scnEditor ed, ADOFAI.LevelEvent evt)
        {
            ed.SelectDecoration(evt, false, false, false, false); // no camera jump, no game panel
        }

        private static void SelectDeco(scnEditor ed, int i)
        {
            _decoSel = (_decoSel == i) ? -1 : i;
            try
            {
                var decos = DecoList(ed);
                if (_decoSel >= 0 && decos != null && _decoSel < decos.Count) SelectDecoEvent(ed, decos[_decoSel]);
                else ed.DeselectAllDecorations();
            }
            catch { }
            try
            {
                var decos2 = DecoList(ed);
                if (_decoSel >= 0 && decos2 != null && _decoSel < decos2.Count)
                    EditorDecoInspector.Show(decos2[_decoSel]);
                else EditorDecoInspector.Close();
            }
            catch { }
            _sig = 0;
        }

        private static void AddDeco(scnEditor ed, ADOFAI.LevelEventType type)
        {
            try
            {
                var dec = ed.AddDecoration(type);   // append at end, returns the new event
                ed.UpdateDecorationObjects();
                var decos = DecoList(ed);
                _decoSel = decos != null && dec != null ? decos.IndexOf(dec) : -1;
                // A fresh deco is untagged and tag folders start COLLAPSED — without this the
                // new item (and its inspector) is invisible and the add looks like a no-op.
                if (dec != null) _decoExpanded.Add(DecoTag(dec));
                // Keep the game's selectedDecorations in sync so Delete/gizmos act on this one.
                // Same guard for both: an IndexOf miss must leave the browser and the inspector
                // agreeing on "unselected", not just one of them.
                if (_decoSel >= 0) { SelectDecoEvent(ed, dec); EditorDecoInspector.Show(dec); }
            }
            catch (Exception ex) { SapphireLog.Log("Deco add failed: " + ex.Message); }
            _sig = 0;
        }

        // DuplicateDecorations() -> MultiCopyDecorations() + PasteDecorations(true), which ends
        // with the GAME's selectedDecorations pointing at the copies while _decoSel/inspector
        // still point at the original. Resync to the copy (last of the game's selection) the
        // same way AddDeco resyncs a freshly-added row — same guard shape, leave selection alone
        // on any miss rather than guessing.
        private static void DuplicateDeco(scnEditor ed)
        {
            try
            {
                ed.DuplicateDecorations();
                var sel = ed.selectedDecorations;
                if (sel == null || sel.Count == 0) { _sig = 0; return; }
                var copy = sel[sel.Count - 1];
                var decos = DecoList(ed);
                int idx = decos != null && copy != null ? decos.IndexOf(copy) : -1;
                if (idx >= 0)
                {
                    _decoSel = idx;
                    _decoExpanded.Add(DecoTag(copy));
                    EditorDecoInspector.Show(copy);
                }
            }
            catch (Exception ex) { SapphireLog.Log("Deco duplicate failed: " + ex.Message); }
            _sig = 0;
        }

        private static void DeleteDeco(scnEditor ed)
        {
            var decos = DecoList(ed);
            if (decos == null || _decoSel < 0 || _decoSel >= decos.Count) return;
            try
            {
                // The game deletes whatever sits in selectedDecorations, so re-assert our row as
                // THE selection first (ignoreDeselection:false clears the rest) — otherwise a
                // stale viewport multi-selection would get taken out with it.
                SelectDecoEvent(ed, decos[_decoSel]);
                ed.DeleteMultiSelectionDecorations();   // same path the game's Delete keybind uses
            }
            catch (Exception ex) { SapphireLog.Log("Deco delete failed: " + ex.Message); }
            _decoSel = -1;
            EditorDecoInspector.Close();
            _sig = 0;
        }

        private static float RenderDecoBrowser(scnEditor ed, float y)
        {
            var decos = DecoList(ed);
            if (decos != null && _decoSel >= decos.Count) _decoSel = -1;
            bool hasSel = _decoSel >= 0;

            float w = _ctx.PanelW - Pad * 2f;
            float bw = (w - Gap * 2f) / 3f;
            var sf = EventRows.InputRow(_content, Pad, y, w, _search,
                sv => { _search = (sv ?? "").Trim(); _sig = 0; });
            if (sf != null && string.IsNullOrEmpty(_search))
            {
                var ph = UIBuilder.Tmp(new GameObject("PH", typeof(RectTransform)), Loc.T("Search decorations"),
                    12f, TextAnchor.MiddleLeft, new Color(0.5f, 0.5f, 0.55f, 1f));
                var pr = ph.rectTransform;
                pr.SetParent(sf.transform, false);
                pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one;
                pr.offsetMin = new Vector2(7f, 0f); pr.offsetMax = new Vector2(-7f, 0f);
                ph.raycastTarget = false;
            }
            y -= RowH + Gap;
            // Add is a type picker, not an image-only button — image/text/object/particle all
            // route through the same scnEditor.AddDecoration(type).
            var types = AddableDecoTypes();
            var typeLabels = new System.Collections.Generic.List<string>(types.Count);
            foreach (var t in types) typeLabels.Add(DecoTypeName(t));
            RoundedRectGraphic addBg = null;
            addBg = EventRows.Cell(_content, Loc.T("+ Decoration") + "  ▾", Pad, y, bw, RowH,
                () => UI.EditorDropdown.Open((RectTransform)addBg.transform, typeLabels, 0,
                    i => AddDeco(ed, types[i])), true);
            EventRows.Cell(_content, Loc.T("Duplicate"), Pad + bw + Gap, y, bw, RowH,
                () => { DuplicateDeco(ed); }, true);
            var delBg = EventRows.Cell(_content, Loc.T("Delete"), Pad + (bw + Gap) * 2f, y, bw, RowH,
                () => DeleteDeco(ed), true);
            if (!hasSel) delBg.color = new Color(1f, 1f, 1f, 0.03f);   // nothing selected → inert
            y -= RowH + Gap;
            EventRows.Cell(_content, _decoGrid ? Loc.T("View: Grid (thumbnails)") : Loc.T("View: List"), Pad, y, w, RowH,
                () => { _decoGrid = !_decoGrid; _sig = 0; }, true);
            y -= RowH + Gap * 2f;

            if (decos == null || decos.Count == 0)
            {
                EventRows.Label(_content, Loc.T("(no decorations)"), Pad, y, w, RowH, Theme.TextMuted);
                return y - RowH - Gap;
            }
            if (_decoGrid) return RenderDecoGridBody(ed, decos, y);

            // group indices by tag, preserving first-seen order
            var order = new System.Collections.Generic.List<string>();
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>();
            for (int i = 0; i < decos.Count; i++)
            {
                if (!Matches(decos[i], i)) continue;
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
                }
            }
            return y;
        }

        // The game already loaded each decoration's sprite — reuse it (no file IO / path resolution).
        /* Search matches the label the row shows (type, file, tile) plus the tag, so what you
           type is what you see rather than a hidden field nobody knows about. */
        private static bool Matches(ADOFAI.LevelEvent evt, int i)
        {
            if (_search.Length == 0) return true;
            string q = _search.ToLowerInvariant();
            if (DecoTag(evt).ToLowerInvariant().Contains(q)) return true;
            if (DecoLabel(evt, i).ToLowerInvariant().Contains(q)) return true;
            return DecoDataStr(evt, "decorationImage").ToLowerInvariant().Contains(q);
        }

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

            int shown = 0;
            for (int i = 0; i < decos.Count; i++)
            {
                if (!Matches(decos[i], i)) continue;   // the grid filters on the same terms
                int col = shown % cols, row = shown / cols;
                float cx = Pad + col * (cw + gap);
                float cy = gridTop - row * (cellH + gap);
                DecoGridCell(ed, decos[i], i, cx, cy, cw, cellH, lblH, i == _decoSel);
                shown++;
            }
            int rows = (shown + cols - 1) / cols;
            y = gridTop - rows * (cellH + gap) - Gap;
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
            if ((r.sizeDelta - _size).sqrMagnitude > 1f) { _size = r.sizeDelta; _sig = 0; }
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

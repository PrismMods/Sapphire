using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Multi-select overlay (top-right), shown while >1 tiles are selected and no editor tool owns
       the click. Two sections:
       • MIRROR — flip the selection horizontally/vertically. Unlike the game's flip (angleData
         only) this also mirrors "position"/"positionOffset" (boxed Vector2) on the selection's
         events + attached decorations. "Preserve beats" adds a twirl on the section's first tile
         so the mirror doesn't desync the ball's spin (first cut — parity rule TBD).
       • COPY — a tree of the event/decoration TYPES present in the selection, grouped by
         LevelEventCategory (Gameplay/Track/Decorations/VFX/…). Copy = MultiCopyFloors then filter
         the clipboard FloorData's levelEventData + attachedDecorations to the checked types (fresh
         lists, live chart untouched — see game-il-facts). */
    internal static class EditorCopyPanel
    {
        private static GameObject _canvasGo, _panelGo;

        // mirror options
        private static bool _preserveBeats;
        private static RoundedRectGraphic _pbBg;

        // copy: unified event+decoration type filter
        private static bool _eventsMaster = true;
        private static readonly HashSet<int> _on = new HashSet<int>();    // included types
        private static readonly HashSet<int> _seen = new HashSet<int>();  // types already defaulted
        private static readonly List<int> _types = new List<int>();       // present, sorted
        private static Dictionary<int, int> _typeCat;                     // type -> category id

        private static RoundedRectGraphic _masterBg;
        private static readonly List<KeyValuePair<int, RoundedRectGraphic>> _typeRows = new List<KeyValuePair<int, RoundedRectGraphic>>();
        private static readonly List<KeyValuePair<List<int>, RoundedRectGraphic>> _catRows = new List<KeyValuePair<List<int>, RoundedRectGraphic>>();

        private static long _selSig = long.MinValue;
        private const long LayoutSigDirty = long.MinValue;
        private static long _layoutSig = LayoutSigDirty;
        private static bool _inspMode;         // inspector tool active: panel = its PASTE FILTER
        private static int _inspVerSeen = -1;

        // The inspector tool pastes only checked types (master off = paste nothing).
        internal static bool TypeChecked(int t) => _eventsMaster && _on.Contains(t);

        internal static void Tick()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            var s = MainClass.Settings;
            int sel = 0;
            try { sel = ed != null && ed.selectedFloors != null ? ed.selectedFloors.Count : 0; } catch { }
            bool inspector = EditorToolbar.InspectorActive;
            bool baseOk = ed != null && !ed.playMode
                        && s != null && MainClass.EditorSuiteOn && s.EditorTileActions;
            bool want = baseOk && ((sel > 1 && !EditorToolbar.AnyToolActive)
                        || (inspector && EditorToolbar.InspectorVersion > 0));
            if (!want) { if (_panelGo != null && _panelGo.activeSelf) _panelGo.SetActive(false); _inspMode = inspector; return; }

            if (inspector != _inspMode) { _inspMode = inspector; _layoutSig = LayoutSigDirty; }
            if (_inspMode)
            {
                if (_inspVerSeen != EditorToolbar.InspectorVersion)
                {
                    _inspVerSeen = EditorToolbar.InspectorVersion;
                    _types.Clear(); _types.AddRange(EditorToolbar.InspectorTypes());
                    foreach (var t in _types) if (_seen.Add(t)) _on.Add(t);
                    _layoutSig = LayoutSigDirty;
                }
            }
            else
            {
                long selSig = SelectionSig(ed);
                if (selSig != _selSig) { _selSig = selSig; GatherTypes(ed); }
            }

            long layoutSig = LayoutSig();
            if (_canvasGo == null || _panelGo == null || layoutSig != _layoutSig)
            {
                _layoutSig = layoutSig;
                Build(ed);
            }
            if (!_panelGo.activeSelf) _panelGo.SetActive(true);
            SyncTints();
        }

        internal static void Dispose()
        {
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _panelGo = null; _masterBg = null; _pbBg = null;
            _typeRows.Clear(); _catRows.Clear();
        }

        // ── data ────────────────────────────────────────────────────────────
        private static long SelectionSig(scnEditor ed)
        {
            long h = 17;
            try { foreach (var f in ed.selectedFloors) if (f != null) h = h * 31 + f.seqID; } catch { }
            return h;
        }

        private static void GatherTypes(scnEditor ed)
        {
            var sel = new HashSet<int>();
            try { foreach (var f in ed.selectedFloors) if (f != null) sel.Add(f.seqID); } catch { }
            var set = new SortedSet<int>();
            try { foreach (var e in ed.events) if (e != null && sel.Contains(e.floor)) set.Add((int)e.eventType); } catch { }
            try { foreach (var d in ed.selectedDecorations) if (d != null) set.Add((int)d.eventType); } catch { }
            _types.Clear(); _types.AddRange(set);
            foreach (var t in _types) if (_seen.Add(t)) _on.Add(t); // new types default included
        }

        // Integer signature (was a per-frame StringBuilder+ToString while multi-select is active).
        private static long LayoutSig()
        {
            long h = 17;
            h = h * 31 + (_inspMode ? 1 : 0);
            h = h * 31 + (_eventsMaster ? 1 : 0);
            if (_eventsMaster) foreach (var t in _types) h = h * 31 + (t + 1);
            return h;
        }

        private static void EnsureTypeCat(scnEditor ed)
        {
            if (_typeCat != null && _typeCat.Count > 0) return;
            _typeCat = new Dictionary<int, int>();
            try
            {
                foreach (var kv in ed.eventButtons)
                {
                    int cat = (int)kv.Key;
                    if (cat == 7) continue; // Favorites — skip (a user-curated duplicate view)
                    foreach (var leb in kv.Value)
                        if (leb != null && !_typeCat.ContainsKey((int)leb.type)) _typeCat[(int)leb.type] = cat;
                }
            }
            catch { }
        }

        private static int CatOf(int type) => _typeCat != null && _typeCat.TryGetValue(type, out var c) ? c : 99;

        private static string CatName(int cat)
        {
            switch (cat)
            {
                case 0: return Loc.T("Gameplay");
                case 1: return Loc.T("Track");
                case 2: return Loc.T("Decorations");
                case 3: return Loc.T("VFX");
                case 4: return Loc.T("Modifiers");
                case 5: return Loc.T("Conveniences");
                case 6: return Loc.T("DLC");
                default: return Loc.T("Other");
            }
        }

        private static string Name(int type)
        {
            try { return ((ADOFAI.LevelEventType)type).ToString(); } catch { return type.ToString(); }
        }

        // grouped present types, ordered by category id, types sorted within
        private static List<KeyValuePair<int, List<int>>> Grouped()
        {
            var byCat = new SortedDictionary<int, List<int>>();
            foreach (var t in _types)
            {
                int c = CatOf(t);
                if (!byCat.TryGetValue(c, out var list)) { list = new List<int>(); byCat[c] = list; }
                list.Add(t);
            }
            var outp = new List<KeyValuePair<int, List<int>>>();
            foreach (var kv in byCat) outp.Add(kv);
            return outp;
        }

        // ── actions ─────────────────────────────────────────────────────────
        private static void DoCopy()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;
            try
            {
                ed.MultiCopyFloors(false);
                var clip = ed.clipboard;
                if (clip == null) return;
                foreach (var o in clip)
                {
                    if (o == null) continue;
                    var t = o.GetType();
                    if (t.Name != "FloorData") continue;
                    var evList = t.GetField("levelEventData")?.GetValue(o) as List<ADOFAI.LevelEvent>;
                    var decList = t.GetField("attachedDecorations")?.GetValue(o) as List<ADOFAI.LevelEvent>;
                    if (!_eventsMaster)
                    {
                        evList?.Clear(); decList?.Clear();
                    }
                    else
                    {
                        evList?.RemoveAll(e => e == null || !_on.Contains((int)e.eventType));
                        decList?.RemoveAll(d => d == null || !_on.Contains((int)d.eventType));
                    }
                }
            }
            catch (Exception ex) { SapphireLog.Log("CopyPanel: copy failed: " + ex.Message); }
        }

        private static readonly string[] PosKeys = { "position", "positionOffset" };

        private static void MirrorSelection(bool horizontal)
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;
            var sel = new HashSet<int>();
            try { foreach (var f in ed.selectedFloors) if (f != null) sel.Add(f.seqID); } catch { }
            try
            {
                using (new SaveStateScope(ed))
                {
                    ed.FlipSelection(horizontal);
                    try { foreach (var e in ed.events) if (e != null && sel.Contains(e.floor)) MirrorPositions(e, horizontal); } catch { }
                    try { foreach (var d in ed.selectedDecorations) if (d != null) MirrorPositions(d, horizontal); } catch { }
                    if (_preserveBeats) PreserveBeats(ed, sel);
                    try { ed.RemakePath(true, true); } catch { }
                }
            }
            catch (Exception ex) { SapphireLog.Log("CopyPanel: mirror failed: " + ex.Message); }
        }

        private static void MirrorPositions(ADOFAI.LevelEvent e, bool horizontal)
        {
            foreach (var k in PosKeys)
            {
                try
                {
                    if (!e.ContainsKey(k)) continue;
                    if (e[k] is Vector2 v)
                    {
                        if (horizontal) v.x = -v.x; else v.y = -v.y;
                        e[k] = v;
                    }
                }
                catch { }
            }
        }

        // FIRST CUT: mirroring flips the ball's spin handedness through the section; a twirl on the
        // section's first tile reconciles it so the rhythm carries through. (The exact "first tile of
        // the first pseudo" parity condition still needs the user's reference — for now unconditional.)
        private static void PreserveBeats(scnEditor ed, HashSet<int> sel)
        {
            int first = int.MaxValue;
            foreach (var seq in sel) if (seq < first) first = seq;
            if (first <= 0 || first == int.MaxValue) return;
            try { ed.events.Add(new ADOFAI.LevelEvent(first, ADOFAI.LevelEventType.Twirl)); } catch { }
        }

        // ── UI ──────────────────────────────────────────────────────────────
        private const float PanelW = 236f, Pad = 8f, RowH = 22f, Gap = 4f, Indent = 14f;

        private static void Build(scnEditor ed)
        {
            EnsureCanvas();
            EnsureTypeCat(ed);
            if (_panelGo != null) UnityEngine.Object.Destroy(_panelGo);
            _typeRows.Clear(); _catRows.Clear();

            _panelGo = new GameObject("Panel", typeof(RectTransform));
            _panelGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_panelGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(1f, 1f);
            r.anchoredPosition = new Vector2(-12f, -58f); // top-right, below the master switch
            var bg = _panelGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 10f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.94f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;

            float y = -Pad;

            if (!_inspMode)
            {
                // Mirror section (multi-select mode only)
                Label(Loc.T("Mirror"), Pad, y, 120f, 16f, Theme.TextMuted, TextAnchor.MiddleLeft); y -= 18f;
                float halfW = (PanelW - Pad * 2f - Gap) * 0.5f;
                Cell(Loc.T("Horizontal"), Pad, y, halfW, RowH, () => MirrorSelection(true), true);
                Cell(Loc.T("Vertical"), Pad + halfW + Gap, y, halfW, RowH, () => MirrorSelection(false), true);
                y -= RowH + Gap;
                _pbBg = Cell(Loc.T("Preserve beats"), Pad, y, PanelW - Pad * 2f, RowH, () => { _preserveBeats = !_preserveBeats; SyncTints(); }, false, TextAnchor.MiddleLeft);
                y -= RowH + 8f;
            }

            // Copy section (inspector mode: this tree IS the paste filter)
            Label(Loc.T(_inspMode ? "Paste filter" : "Copy"), Pad, y, 120f, 16f, Theme.TextMuted, TextAnchor.MiddleLeft); y -= 18f;
            const float allW = 40f, noneW = 46f;
            float togW = PanelW - Pad * 2f - allW - noneW - Gap * 2f;
            _masterBg = Cell(Loc.T("Events"), Pad, y, togW, RowH, () => { _eventsMaster = !_eventsMaster; _layoutSig = LayoutSigDirty; }, false, TextAnchor.MiddleLeft);
            Cell(Loc.T("All"), Pad + togW + Gap, y, allW, RowH, () => SetAll(true), true);
            Cell(Loc.T("None"), Pad + togW + Gap + allW + Gap, y, noneW, RowH, () => SetAll(false), true);
            y -= RowH + Gap;

            if (_eventsMaster)
            {
                var groups = Grouped();
                if (groups.Count == 0) { Label(Loc.T("(no events)"), Pad + Indent, y, 160f, RowH, Theme.TextMuted, TextAnchor.MiddleLeft); y -= RowH + Gap; }
                foreach (var g in groups)
                {
                    var catTypes = g.Value;
                    var catBg = Cell(CatName(g.Key), Pad + Indent, y, PanelW - Pad * 2f - Indent, RowH,
                        () => ToggleCategory(catTypes), false, TextAnchor.MiddleLeft);
                    _catRows.Add(new KeyValuePair<List<int>, RoundedRectGraphic>(catTypes, catBg));
                    y -= RowH + Gap;
                    foreach (int type in catTypes)
                    {
                        int t = type;
                        var cell = Cell(Name(t), Pad + Indent * 2f, y, PanelW - Pad * 2f - Indent * 2f, RowH,
                            () => Toggle(t), false, TextAnchor.MiddleLeft);
                        _typeRows.Add(new KeyValuePair<int, RoundedRectGraphic>(t, cell));
                        y -= RowH + Gap;
                    }
                }
            }

            y -= 4f;
            if (!_inspMode)
            {
                const float copyW = 90f;
                Cell(Loc.T("Copy"), (PanelW - copyW) * 0.5f, y, copyW, RowH + 2f, DoCopy, true);
                y -= RowH + 2f + Pad;
            }
            else y -= Pad - 4f;

            r.sizeDelta = new Vector2(PanelW, -y);
            SyncTints();
        }

        private static void SetAll(bool on)
        {
            foreach (var t in _types) { if (on) _on.Add(t); else _on.Remove(t); }
            SyncTints();
        }

        private static void ToggleCategory(List<int> types)
        {
            bool allOn = true;
            foreach (var t in types) if (!_on.Contains(t)) { allOn = false; break; }
            foreach (var t in types) { if (allOn) _on.Remove(t); else _on.Add(t); }
            SyncTints();
        }

        private static void Toggle(int t)
        {
            if (!_on.Remove(t)) _on.Add(t);
            SyncTints();
        }

        private static void SyncTints()
        {
            if (_pbBg != null) _pbBg.color = Tint(_preserveBeats);
            if (_masterBg != null) _masterBg.color = Tint(_eventsMaster);
            foreach (var kv in _typeRows) if (kv.Value != null) kv.Value.color = Tint(_on.Contains(kv.Key));
            foreach (var kv in _catRows)
            {
                if (kv.Value == null) continue;
                bool allOn = kv.Key.Count > 0;
                foreach (var t in kv.Key) if (!_on.Contains(t)) { allOn = false; break; }
                kv.Value.color = Tint(allOn);
            }
        }

        private static Color Tint(bool on) => on
            ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f)
            : new Color(1f, 1f, 1f, 0.05f);

        private static void EnsureCanvas()
        {
            if (_canvasGo != null) return;
            _canvasGo = new GameObject("SapphireCopyPanel", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 906;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
        }

        private static void Label(string text, float x, float y, float w, float h, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Lbl", typeof(RectTransform));
            go.transform.SetParent(_panelGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            UIBuilder.Tmp(go, text, 12.5f, anchor, color);
        }

        private static RoundedRectGraphic Cell(string text, float x, float y, float w, float h,
            Action onClick, bool button, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Cell", typeof(RectTransform));
            go.transform.SetParent(_panelGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = button ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.05f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.1f);
            bg.raycastTarget = true;
            var lblGo = new GameObject("L", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(anchor == TextAnchor.MiddleLeft ? 8f : 0f, 0f);
            lr.offsetMax = Vector2.zero;
            UIBuilder.Tmp(lblGo, text, 12.5f, anchor, Theme.Text);
            UI.ClickHandler.Attach(go, onClick);
            return bg;
        }
    }
}

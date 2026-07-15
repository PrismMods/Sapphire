using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* AE-style graph editor for camera properties: value over song time, plotted through
       each tween's REAL runtime easing, keyframes as draggable diamonds.

       Tabs: 위치 (X and Y overlaid — components are independent keyframe streams), X, Y,
       회전, 확대. Vertical drag = value, horizontal = retime; retiming a component of an
       event that carries other properties SPLITS it (EditorEvents.SplitForRetime), so
       component keyframes move freely — new events appear as needed, by design.

       View: wheel over the plot zooms time around the cursor; wheel over the LEFT margin
       zooms the value axis (switches it from auto-scale to manual); dragging empty plot
       pans; selecting a keyframe from the timeline focuses the window on its tween. */
    internal static class EditorGraph
    {
        private static GameObject _canvasGo;
        private static RectTransform _canvasRect;
        private static GameObject _panelGo;
        private static RectTransform _plotArea;
        private static RawImage _plotImg;
        private static Texture2D _plotTex;
        private static TextMeshProUGUI _infoText;
        private static readonly List<RoundedRectGraphic> _tabBgs = new List<RoundedRectGraphic>();
        private static readonly List<TextMeshProUGUI> _yLabels = new List<TextMeshProUGUI>();
        private static readonly List<TextMeshProUGUI> _xLabels = new List<TextMeshProUGUI>();

        private const int TexW = 1400, TexH = 380;
        private const float PanelW = 1480f, PanelH = 500f, PlotH = 398f;
        // user-adjusted geometry survives reopen (drag the header / resize the edges)
        private static Vector2 _userPos = Vector2.zero;
        private static Vector2 _userSize = Vector2.zero;
        private static bool _userMoved;

        private static int _tab;                 // 0=pos(X+Y) 1=X 2=Y 3=rotation 4=zoom
        private static float _vs, _ve;           // view window (frac space)
        private static float _yMin, _yMax;       // effective y-range this repaint
        private static bool _yAuto = true;
        private static float _yMinM, _yMaxM;     // manual range once the axis is zoomed
        private static int _repaint;
        private static ADOFAI.LevelEvent _lastCamSel;
        private static bool _panning, _panMoved;
        private static float _panLastNx, _panLastNy;

        // keyframe cache: one entry per (event, component)
        private struct Key { public ADOFAI.LevelEvent E; public int Comp; public float Frac; public float Val; }
        private static readonly List<Key> _keys = new List<Key>();
        private static ADOFAI.LevelEvent _sel;
        private static int _selComp;
        private static ADOFAI.LevelEvent _drag;
        private static int _dragComp;
        private static bool _dragMoved;
        private static Vector2 _dragStartLocal;
        private static float _dragOrigVal;
        private static int _dragOrigFloor;

        internal static bool IsOpen => _panelGo != null;

        // Blocks the editor's own scroll-zoom while the cursor is over the graph.
        internal static bool PanelHovered
        {
            get
            {
                if (_panelGo == null) return false;
                try
                {
                    return RectTransformUtility.RectangleContainsScreenPoint(
                        (RectTransform)_panelGo.transform, Input.mousePosition, null);
                }
                catch { return false; }
            }
        }

        // Position tab: linked X/Y (default) drags the coordinate PAIR together — retiming
        // keeps both components on one event, no split. Unlinked = independent streams.
        private static bool _linkXY = true;
        private static RoundedRectGraphic _linkBg;

        internal static void Open(int laneHint)
        {
            _tab = laneHint == 1 ? 3 : laneHint == 2 ? 4 : 0; // timeline lanes: pos/rot/zoom
            _vs = EditorEvents.GraphViewStart;
            _ve = _vs + 1f / Mathf.Max(1f, EditorEvents.GraphViewZoom);
            _sel = _lastCamSel = EditorEvents._camSel;
            _selComp = FirstCompOfTab();
            _yAuto = true;
            Build();
            if (_sel != null) FocusOnSelection();
            Repaint();
        }

        private static int FirstCompOfTab() => _tab == 0 || _tab == 1 ? 0 : _tab == 2 ? 1 : _tab == 3 ? 2 : 3;

        private static int[] CompsOfTab() =>
            _tab == 0 ? new[] { 0, 1 } : _tab == 1 ? new[] { 0 } : _tab == 2 ? new[] { 1 } : _tab == 3 ? new[] { 2 } : new[] { 3 };

        private static void FocusOnSelection()
        {
            if (_sel == null) return;
            float s = EditorEvents.GraphFracOfFloor(_sel.floor);
            float e = EditorEvents.GraphFracEnd(_sel, s);
            float span = Mathf.Max(e - s, 0.004f);
            _vs = Mathf.Clamp01(s - span * 0.75f);
            _ve = Mathf.Clamp01(e + span * 0.75f);
            if (_ve - _vs < 0.0005f) _ve = Mathf.Clamp01(_vs + 0.0005f);
        }

        internal static void Tick()
        {
            if (_panelGo == null) return;
            var ed = scnEditor.instance;
            bool want = false;
            try { want = ed != null && !ed.playMode && MainClass.EditorSuiteOn && EditorEvents.CamMode; }
            catch { }
            if (!want || Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

            if (!ReferenceEquals(EditorEvents._camSel, _lastCamSel))
            {
                _lastCamSel = EditorEvents._camSel;
                if (_lastCamSel != null) { _sel = _lastCamSel; FocusOnSelection(); }
                Repaint();
            }
            TickMouse(ed);
            if (_panelGo != null)
            {
                var pr = (RectTransform)_panelGo.transform;
                _userPos = pr.anchoredPosition; _userSize = pr.sizeDelta; _userMoved = true;
            }
            if (--_repaint <= 0) { _repaint = 30; Repaint(); } // catch external edits
        }

        internal static void Dispose()
        {
            Close();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _canvasRect = null;
        }

        internal static void Close()
        {
            if (_panelGo != null) UnityEngine.Object.Destroy(_panelGo);
            if (_plotTex != null) UnityEngine.Object.Destroy(_plotTex);
            _panelGo = null; _plotTex = null; _plotImg = null; _plotArea = null;
            _infoText = null; _tabBgs.Clear(); _keys.Clear(); _linkBg = null;
            _yLabels.Clear(); _xLabels.Clear();
            _sel = null; _drag = null; _panning = false;
        }

        // ── per-component access (0=posX 1=posY 2=rotation 3=zoom) ──────────

        private static readonly string[] CompKey = { "position", "position", "rotation", "zoom" };
        private static readonly string[] CompSplit = { "posx", "posy", "rotation", "zoom" };

        private static bool TouchesC(ADOFAI.LevelEvent e, int comp)
        {
            if (comp <= 1) return EditorEvents.PosCompTouched(e, comp == 0);
            return EditorEvents.CamTouchesNumber(e, CompKey[comp]);
        }

        private static float TargetC(ADOFAI.LevelEvent e, int comp)
        {
            var d = EditorEvents.EventData(e);
            if (d == null) return 0f;
            if (comp <= 1)
                return d.TryGetValue("position", out var v) && v is Vector2 p ? (comp == 0 ? p.x : p.y) : 0f;
            if (!d.TryGetValue(CompKey[comp], out var nv) || nv == null) return 0f;
            try { return Convert.ToSingle(nv); } catch { return 0f; }
        }

        private static void SetC(ADOFAI.LevelEvent e, int comp, float val)
        {
            if (comp <= 1)
            {
                Vector2 p = new Vector2(float.NaN, float.NaN);
                var d = EditorEvents.EventData(e);
                if (d != null && d.TryGetValue("position", out var v) && v is Vector2 cur) p = cur;
                e["position"] = comp == 0 ? new Vector2(val, p.y) : new Vector2(p.x, val);
                try { if (e.disabled != null && e.disabled.ContainsKey("position")) e.disabled["position"] = false; } catch { }
            }
            else
            {
                e[CompKey[comp]] = val;
                try { if (e.disabled != null && e.disabled.ContainsKey(CompKey[comp])) e.disabled[CompKey[comp]] = false; } catch { }
            }
        }

        private static DG.Tweening.Ease EaseOf(ADOFAI.LevelEvent e)
        {
            try { if (e.ContainsKey("ease") && e["ease"] is DG.Tweening.Ease es) return es; }
            catch { }
            return DG.Tweening.Ease.Linear;
        }

        private static float InitialValue(int comp)
        {
            try
            {
                var ld = scnGame.instance != null ? scnGame.instance.levelData : null;
                if (ld != null)
                {
                    if (comp == 3) return ld.camZoom;
                    if (comp == 2) return ld.camRotation;
                    return comp == 0 ? ld.camPosition.x : ld.camPosition.y;
                }
            }
            catch { }
            return comp == 3 ? 100f : 0f;
        }

        private static Color32 CompColor(int comp)
        {
            switch (comp)
            {
                case 1: return new Color32(120, 235, 214, 235);
                case 2: return new Color32(255, 181, 71, 235);
                case 3: return new Color32(99, 227, 166, 235);
                default: return new Color32(89, 194, 255, 235);
            }
        }

        private static string Unit() => _tab == 4 ? "%" : _tab == 3 ? "°" : "";

        // ── plotting ────────────────────────────────────────────────────────

        private static void Repaint()
        {
            if (_plotTex == null) return;
            var ed = scnEditor.instance;
            if (ed == null) return;

            var comps = CompsOfTab();
            _keys.Clear();
            var evs = new List<ADOFAI.LevelEvent>();
            foreach (var e in ed.events)
            {
                if (e == null || !e.active || e.eventType != ADOFAI.LevelEventType.MoveCamera) continue;
                evs.Add(e);
            }
            evs.Sort((a, b) =>
            {
                int c = a.floor.CompareTo(b.floor);
                return c != 0 ? c : NumOf(a, "angleOffset").CompareTo(NumOf(b, "angleOffset"));
            });
            foreach (var comp in comps)
                foreach (var e in evs)
                    if (TouchesC(e, comp))
                        _keys.Add(new Key { E = e, Comp = comp, Frac = EditorEvents.GraphFracOfFloor(e.floor), Val = TargetC(e, comp) });

            // y-range: auto from data unless the axis was zoomed manually
            if (_yAuto)
            {
                float init0 = InitialValue(comps[0]);
                _yMin = init0; _yMax = init0;
                foreach (var comp in comps)
                {
                    float ini = InitialValue(comp);
                    _yMin = Mathf.Min(_yMin, ini); _yMax = Mathf.Max(_yMax, ini);
                }
                foreach (var k in _keys) { _yMin = Mathf.Min(_yMin, k.Val); _yMax = Mathf.Max(_yMax, k.Val); }
                if (_yMax - _yMin < 1e-3f) { _yMin -= 1f; _yMax += 1f; }
                float pad = (_yMax - _yMin) * 0.12f;
                _yMin -= pad; _yMax += pad;
            }
            else { _yMin = _yMinM; _yMax = _yMaxM; }

            var px = new Color32[TexW * TexH];
            var grid = new Color32(255, 255, 255, 18);
            for (int gy = 1; gy < 4; gy++)
            {
                int y = gy * TexH / 4;
                for (int x = 0; x < TexW; x++) px[y * TexW + x] = grid;
            }
            for (int gx = 1; gx < 8; gx++)
            {
                int x = gx * TexW / 8;
                for (int y = 0; y < TexH; y++) px[y * TexW + x] = grid;
            }

            foreach (var comp in comps)
            {
                var col = CompColor(comp);
                float init = InitialValue(comp);
                int prevY = -1;
                for (int gx = 0; gx < TexW; gx++)
                {
                    float frac = _vs + (gx / (float)(TexW - 1)) * (_ve - _vs);
                    float v = ValueAt(frac, init, comp);
                    int gy = ValY(v);
                    if (prevY >= 0)
                    {
                        int lo = Mathf.Min(prevY, gy), hi = Mathf.Max(prevY, gy);
                        for (int yy = lo; yy <= hi; yy++) px[yy * TexW + gx] = col;
                    }
                    prevY = gy;
                }
            }

            foreach (var k in _keys)
            {
                if (k.Frac < _vs || k.Frac > _ve) continue;
                int cx = FracX(k.Frac);
                int cy = ValY(k.Val);
                bool selK = ReferenceEquals(k.E, _sel) && k.Comp == _selComp;
                var cc = CompColor(k.Comp);
                var kc = selK ? new Color32(255, 255, 255, 255) : new Color32(cc.r, cc.g, cc.b, 255);
                int rad = selK ? 7 : 5;
                for (int dy = -rad; dy <= rad; dy++)
                {
                    int w = rad - Mathf.Abs(dy);
                    int yy = Mathf.Clamp(cy + dy, 0, TexH - 1);
                    for (int dx = -w; dx <= w; dx++)
                        px[yy * TexW + Mathf.Clamp(cx + dx, 0, TexW - 1)] = kc;
                }
            }

            _plotTex.SetPixels32(px);
            _plotTex.Apply(false);
            SyncInfo();
            SyncTabs();
            SyncAxisLabels();
        }

        private static float NumOf(ADOFAI.LevelEvent e, string key)
        {
            var d = EditorEvents.EventData(e);
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return 0f;
            try { float f = Convert.ToSingle(v); return float.IsNaN(f) ? 0f : f; } catch { return 0f; }
        }

        private static float ValueAt(float frac, float init, int comp)
        {
            float cur = init;
            for (int i = 0; i < _keys.Count; i++)
            {
                var k = _keys[i];
                if (k.Comp != comp) continue;
                float end = EditorEvents.GraphFracEnd(k.E, k.Frac);
                if (frac < k.Frac) break;
                if (frac >= end) { cur = k.Val; continue; }
                float dur = Mathf.Max(1e-5f, end - k.Frac);
                float t = (frac - k.Frac) / dur;
                float y;
                try { y = DG.Tweening.Core.Easing.EaseManager.Evaluate(EaseOf(k.E), null, t, 1f, 1.70158f, 0f); }
                catch { y = t; }
                return cur + (k.Val - cur) * y;
            }
            return cur;
        }

        private static int ValY(float v) =>
            Mathf.Clamp(Mathf.RoundToInt((v - _yMin) / (_yMax - _yMin) * (TexH - 1)), 0, TexH - 1);

        private static float YVal(float ny) => _yMin + ny * (_yMax - _yMin);

        private static int FracX(float frac) =>
            Mathf.Clamp(Mathf.RoundToInt((frac - _vs) / Mathf.Max(1e-6f, _ve - _vs) * (TexW - 1)), 0, TexW - 1);

        private static void SyncAxisLabels()
        {
            for (int i = 0; i < _yLabels.Count; i++)
            {
                if (_yLabels[i] == null) continue;
                float v = _yMin + (i / (float)(_yLabels.Count - 1)) * (_yMax - _yMin);
                _yLabels[i].text = v.ToString("0.##") + Unit();
            }
            for (int i = 0; i < _xLabels.Count; i++)
            {
                if (_xLabels[i] == null) continue;
                float frac = _vs + (i / (float)(_xLabels.Count - 1)) * (_ve - _vs);
                double beat = EditorEvents.GraphBeatAtFrac(frac);
                _xLabels[i].text = beat.ToString("0.#");
            }
        }

        // ── mouse ───────────────────────────────────────────────────────────

        private static void TickMouse(scnEditor ed)
        {
            if (_plotArea == null) return;
            Vector2 mouse = Input.mousePosition;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_plotArea, mouse, null, out local))
            { if (_drag != null && Input.GetMouseButtonUp(0)) EndDrag(ed); return; }
            var rect = _plotArea.rect;
            bool insidePlot = rect.Contains(local);
            // margins still belong to the graph: left = value axis, bottom = time axis
            bool onYAxis = !insidePlot && local.x < rect.xMin && local.x > rect.xMin - 76f
                           && local.y >= rect.yMin && local.y <= rect.yMax;
            bool onXAxis = !insidePlot && local.y < rect.yMin && local.y > rect.yMin - 26f
                           && local.x >= rect.xMin && local.x <= rect.xMax;
            float nx = Mathf.Clamp01((local.x - rect.xMin) / rect.width);
            float ny = Mathf.Clamp01((local.y - rect.yMin) / rect.height);
            float frac = _vs + nx * (_ve - _vs);

            float wheel = MainClass.WheelY;
            if (Mathf.Abs(wheel) > 0.01f && (insidePlot || onXAxis || onYAxis))
            {
                if (onYAxis)
                {
                    // value-axis zoom around the cursor's value → manual scale
                    float range = _yMax - _yMin;
                    float newRange = Mathf.Max(1e-3f, range * Mathf.Pow(1.25f, -wheel));
                    float anchor = YVal(ny);
                    _yMinM = anchor - ny * newRange;
                    _yMaxM = _yMinM + newRange;
                    _yAuto = false;
                }
                else
                {
                    float span = _ve - _vs;
                    float newSpan = Mathf.Clamp(span * Mathf.Pow(1.25f, -wheel), 0.0005f, 1f);
                    _vs = Mathf.Clamp(frac - nx * newSpan, 0f, 1f - newSpan);
                    _ve = _vs + newSpan;
                }
                Repaint();
                return;
            }
            if (!insidePlot)
            {
                if (_drag != null && Input.GetMouseButtonUp(0)) EndDrag(ed);
                return;
            }

            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                ADOFAI.LevelEvent hit = null;
                int hitComp = 0;
                float best = 18f * 18f;
                foreach (var k in _keys)
                {
                    float kx = (k.Frac - _vs) / Mathf.Max(1e-6f, _ve - _vs) * rect.width + rect.xMin;
                    float ky = (k.Val - _yMin) / (_yMax - _yMin) * rect.height + rect.yMin;
                    float dx = kx - local.x, dy = ky - local.y;
                    float dd = dx * dx + dy * dy;
                    if (dd < best) { best = dd; hit = k.E; hitComp = k.Comp; }
                }
                if (Input.GetMouseButtonDown(1))
                {
                    if (hit != null) EditorEasePicker.Open(hit, mouse);
                    return;
                }
                if (hit != null)
                {
                    _sel = hit; _selComp = hitComp;
                    EditorEvents._camSel = hit; EditorEvents._camSelDirty = true;
                    _lastCamSel = hit; // our own selection — don't re-focus on it
                    _drag = hit; _dragComp = hitComp; _dragMoved = false; _dragStartLocal = local;
                    _dragOrigVal = TargetC(hit, hitComp); _dragOrigFloor = hit.floor;
                    Repaint();
                }
                else { _panning = true; _panMoved = false; _panLastNx = nx; _panLastNy = ny; }
            }

            if (_panning && Input.GetMouseButton(0))
            {
                float dNx = nx - _panLastNx;
                float dNy = ny - _panLastNy;
                if (Mathf.Abs(dNx) > 0.002f || Mathf.Abs(dNy) > 0.002f) _panMoved = true;
                float span = _ve - _vs;
                _vs = Mathf.Clamp(_vs - dNx * span, 0f, 1f - span);
                _ve = _vs + span;
                if (Mathf.Abs(dNy) > 0.0001f)
                {
                    // vertical pan shifts the value axis (implies manual scale)
                    if (_yAuto) { _yMinM = _yMin; _yMaxM = _yMax; _yAuto = false; }
                    float dv = dNy * (_yMaxM - _yMinM);
                    _yMinM -= dv; _yMaxM -= dv;
                }
                _panLastNx = nx;
                _panLastNy = ny;
                if (_panMoved) Repaint();
            }
            if (_panning && Input.GetMouseButtonUp(0))
            {
                _panning = false;
                if (!_panMoved)
                {
                    _sel = null;
                    EditorEvents._camSel = null; EditorEvents._camSelDirty = true;
                    _lastCamSel = null;
                    Repaint();
                }
            }

            if (_drag != null && Input.GetMouseButton(0))
            {
                if (!_dragMoved && (local - _dragStartLocal).sqrMagnitude > 16f) _dragMoved = true;
                if (_dragMoved)
                {
                    SetC(_drag, _dragComp, YVal(ny));   // live preview
                    int nf = EditorEvents.GraphFloorAtFrac(frac);
                    if (nf > 0) _drag.floor = nf;
                    Repaint();
                }
                return;
            }
            if (_drag != null && Input.GetMouseButtonUp(0)) EndDrag(ed);
        }

        private static void EndDrag(scnEditor ed)
        {
            var evt = _drag; _drag = null;
            if (evt == null || !_dragMoved) return;
            float finalVal = TargetC(evt, _dragComp);
            int finalFloor = evt.floor;
            // restore pre-drag state, then ONE SaveStateScope re-applies (splits if the
            // event carries other properties and only this component is being retimed)
            SetC(evt, _dragComp, _dragOrigVal);
            evt.floor = _dragOrigFloor;
            try
            {
                using (new SaveStateScope(ed))
                {
                    string splitComp = _tab == 0 && _linkXY && _dragComp <= 1 ? "position" : CompSplit[_dragComp];
                    var carrier = finalFloor != _dragOrigFloor
                        ? EditorEvents.SplitForRetime(ed, evt, splitComp, finalFloor)
                        : evt;
                    SetC(carrier, _dragComp, finalVal);
                    if (!ReferenceEquals(carrier, evt))
                    {
                        _sel = carrier;
                        EditorEvents._camSel = carrier; EditorEvents._camSelDirty = true;
                        _lastCamSel = carrier;
                    }
                }
            }
            catch (Exception ex) { SapphireLog.Log("Graph: drag commit failed: " + ex.Message); }
            Repaint();
        }

        // ── UI shell ────────────────────────────────────────────────────────

        private static void SyncInfo()
        {
            if (_infoText == null) return;
            string range = _yMin.ToString("0.##") + " … " + _yMax.ToString("0.##") + Unit() + (_yAuto ? "" : " ·M");
            if (_sel == null) { _infoText.text = range; return; }
            _infoText.text = "#" + _sel.floor + "  " + TargetC(_sel, _selComp).ToString("0.###") + Unit()
                + "  " + EaseOf(_sel) + "  ·  " + range;
        }

        private static void SyncLink()
        {
            if (_linkBg == null) return;
            _linkBg.color = _linkXY
                ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.35f)
                : new Color(1f, 1f, 1f, 0.06f);
        }

        private static void SyncTabs()
        {
            for (int i = 0; i < _tabBgs.Count; i++)
            {
                if (_tabBgs[i] == null) continue;
                _tabBgs[i].color = i == _tab
                    ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.5f)
                    : new Color(1f, 1f, 1f, 0.08f);
            }
        }

        private static void Build()
        {
            Close();
            if (_canvasGo == null) BuildCanvas();

            _panelGo = new GameObject("GraphPanel", typeof(RectTransform));
            _panelGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_panelGo.transform;
            r.anchorMin = new Vector2(0.5f, 0f);
            r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0.5f); // centered pivot = exact edge-resize math
            r.sizeDelta = _userMoved && _userSize.x > 100f ? _userSize : new Vector2(PanelW, PanelH);
            r.anchoredPosition = _userMoved
                ? _userPos
                : new Vector2(0f, EditorEvents.GraphStripTop + 10f + r.sizeDelta.y * 0.5f);
            var bg = _panelGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 12f;
            bg.color = new Color(0.05f, 0.05f, 0.07f, 0.97f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;

            // drag header: full-width band behind the tab row; "Graph View" title center
            var headGo = new GameObject("Header", typeof(RectTransform));
            headGo.transform.SetParent(_panelGo.transform, false);
            var hr = (RectTransform)headGo.transform;
            hr.anchorMin = new Vector2(0f, 1f); hr.anchorMax = new Vector2(1f, 1f);
            hr.pivot = new Vector2(0.5f, 1f);
            hr.offsetMin = new Vector2(4f, -40f); hr.offsetMax = new Vector2(-4f, -2f);
            var hImg = headGo.AddComponent<Image>();
            hImg.color = new Color(1f, 1f, 1f, 0.02f);
            hImg.raycastTarget = true;
            headGo.AddComponent<DragHandle>();
            var htGo = new GameObject("T", typeof(RectTransform));
            htGo.transform.SetParent(headGo.transform, false);
            var htr = (RectTransform)htGo.transform;
            htr.anchorMin = new Vector2(0.5f, 0.5f); htr.anchorMax = new Vector2(0.5f, 0.5f);
            htr.pivot = new Vector2(0.5f, 0.5f);
            htr.sizeDelta = new Vector2(240f, 22f);
            var ht = UIBuilder.Tmp(htGo, Loc.T("Graph View"), 13.5f, TextAnchor.MiddleCenter, Theme.TextMuted);
            ht.raycastTarget = false;

            _tabBgs.Clear();
            string[] labels = { Loc.T("Position"), "X", "Y", Loc.T("Rotation"), Loc.T("Zoom") };
            float tx = 12f;
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                float w = i == 1 || i == 2 ? 34f : 76f;
                _tabBgs.Add(MakeTab(labels[i], tx, w, () =>
                {
                    _tab = idx; _sel = null; _selComp = FirstCompOfTab(); _yAuto = true;
                    Repaint();
                }));
                tx += w + 6f;
            }

            _linkBg = MakeTab("X·Y", tx + 10f, 56f, () =>
            {
                _linkXY = !_linkXY;
                SyncLink();
            });
            SyncLink();

            var infoGo = new GameObject("Info", typeof(RectTransform));
            infoGo.transform.SetParent(_panelGo.transform, false);
            var ir = (RectTransform)infoGo.transform;
            ir.anchorMin = new Vector2(1f, 1f); ir.anchorMax = new Vector2(1f, 1f);
            ir.pivot = new Vector2(1f, 1f);
            ir.anchoredPosition = new Vector2(-44f, -8f);
            ir.sizeDelta = new Vector2(560f, 22f);
            _infoText = UIBuilder.Tmp(infoGo, "", 12.5f, TextAnchor.MiddleRight, Theme.TextMuted);
            _infoText.raycastTarget = false;

            var xGo = new GameObject("X", typeof(RectTransform));
            xGo.transform.SetParent(_panelGo.transform, false);
            var xr = (RectTransform)xGo.transform;
            xr.anchorMin = xr.anchorMax = new Vector2(1f, 1f);
            xr.pivot = new Vector2(1f, 1f);
            xr.anchoredPosition = new Vector2(-10f, -7f);
            xr.sizeDelta = new Vector2(24f, 24f);
            var xbg = xGo.AddComponent<RoundedRectGraphic>();
            xbg.Radius = 6f;
            xbg.color = new Color(1f, 1f, 1f, 0.07f);
            xbg.raycastTarget = true;
            var xlGo = new GameObject("L", typeof(RectTransform));
            xlGo.transform.SetParent(xGo.transform, false);
            var xlr = (RectTransform)xlGo.transform;
            xlr.anchorMin = Vector2.zero; xlr.anchorMax = Vector2.one;
            xlr.offsetMin = xlr.offsetMax = Vector2.zero;
            var xl = UIBuilder.Tmp(xlGo, "×", 15f, TextAnchor.MiddleCenter, Theme.TextMuted);
            xl.raycastTarget = false;
            UI.ClickHandler.Attach(xGo, Close);

            var plotGo = new GameObject("Plot", typeof(RectTransform));
            plotGo.transform.SetParent(_panelGo.transform, false);
            _plotArea = (RectTransform)plotGo.transform;
            // full-stretch: the plot follows the panel through resizes (fixed height left
            // dead space when the window grew)
            _plotArea.anchorMin = new Vector2(0f, 0f);
            _plotArea.anchorMax = new Vector2(1f, 1f);
            _plotArea.pivot = new Vector2(0.5f, 0f);
            _plotArea.offsetMin = new Vector2(78f, 30f);
            _plotArea.offsetMax = new Vector2(-28f, -48f); // clears the header/tab row
            _plotImg = plotGo.AddComponent<RawImage>();
            _plotTex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false);
            _plotTex.filterMode = FilterMode.Bilinear;
            _plotImg.texture = _plotTex;
            _plotImg.raycastTarget = false;

            _yLabels.Clear();
            for (int i = 0; i < 5; i++)
            {
                var go = new GameObject("YL" + i, typeof(RectTransform));
                go.transform.SetParent(plotGo.transform, false);
                var yr = (RectTransform)go.transform;
                yr.anchorMin = new Vector2(0f, i / 4f);
                yr.anchorMax = new Vector2(0f, i / 4f);
                yr.pivot = new Vector2(1f, 0.5f);
                yr.anchoredPosition = new Vector2(-6f, 0f);
                yr.sizeDelta = new Vector2(70f, 16f);
                var t = UIBuilder.Tmp(go, "", 12f, TextAnchor.MiddleRight, Theme.TextMuted);
                t.raycastTarget = false;
                _yLabels.Add(t);
            }
            _xLabels.Clear();
            for (int i = 0; i < 5; i++)
            {
                var go = new GameObject("XL" + i, typeof(RectTransform));
                go.transform.SetParent(plotGo.transform, false);
                var xr2 = (RectTransform)go.transform;
                xr2.anchorMin = new Vector2(i / 4f, 0f);
                xr2.anchorMax = new Vector2(i / 4f, 0f);
                xr2.pivot = new Vector2(0.5f, 1f);
                xr2.anchoredPosition = new Vector2(0f, -4f);
                xr2.sizeDelta = new Vector2(84f, 16f);
                var t = UIBuilder.Tmp(go, "", 12f, TextAnchor.MiddleCenter, Theme.TextMuted);
                t.raycastTarget = false;
                _xLabels.Add(t);
            }
            var capGo = new GameObject("Cap", typeof(RectTransform));
            capGo.transform.SetParent(_panelGo.transform, false);
            var cr2 = (RectTransform)capGo.transform;
            cr2.anchorMin = cr2.anchorMax = new Vector2(0f, 0f);
            cr2.pivot = new Vector2(0f, 0f);
            cr2.anchoredPosition = new Vector2(14f, 8f);
            cr2.sizeDelta = new Vector2(90f, 16f);
            var cap = UIBuilder.Tmp(capGo, Loc.T("Beats"), 12f, TextAnchor.MiddleLeft, Theme.TextMuted);
            cap.raycastTarget = false;

            ResizeHandle.AttachAll(r); // all 8 edges/corners

            _repaint = 30;
        }

        private static RoundedRectGraphic MakeTab(string label, float x, float w, Action onClick)
        {
            var go = new GameObject("Tab_" + label, typeof(RectTransform));
            go.transform.SetParent(_panelGo.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -8f);
            r.sizeDelta = new Vector2(w, 26f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(1f, 1f, 1f, 0.08f);
            bg.raycastTarget = true;
            var lblGo = new GameObject("L", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            var lbl = UIBuilder.Tmp(lblGo, label, 13f, TextAnchor.MiddleCenter, Theme.Text);
            lbl.raycastTarget = false;
            UI.ClickHandler.Attach(go, onClick);
            return bg;
        }

        private static void BuildCanvas()
        {
            _canvasGo = new GameObject("SapphireGraph", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 909; // above chrome, below popups
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
            _canvasRect = (RectTransform)_canvasGo.transform;
        }
    }
}

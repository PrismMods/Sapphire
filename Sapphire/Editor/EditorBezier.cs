using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Custom bezier curve for a camera tween — entirely Sapphire-side logic, since the
       game only plays DOTween's fixed eases. A cubic bezier (endpoints pinned at (0,0) and
       (1,1), two DRAGGABLE control points) is APPLIED by decomposing the tween into N
       short Linear segments on the same tile: segment i fires at angleOffset
       a0 + 180·D·i/N, lasts D/N beats, and targets value(t=(i+1)/N) along the curve. The
       original event is replaced (one SaveStateScope = one undo).

       Interpolation needs the tween's START value, which MoveCamera doesn't carry — it's
       recovered from the nearest earlier MoveCamera touching the same property (the
       duration-0 "set" pair partner, or the previous tween's target). Properties with no
       discoverable start abort the whole apply, so a half-decomposed event never ships. */
    internal static class EditorBezier
    {
        private static GameObject _canvasGo;
        private static RectTransform _canvasRect;
        private static GameObject _popupGo;
        private static ADOFAI.LevelEvent _target;
        private static RawImage _curveImg;
        private static Texture2D _curveTex;
        private static RectTransform _curveArea, _h1, _h2;
        private static TMP_InputField _segField;
        private static TextMeshProUGUI _status;

        // control points, normalized: x ∈ [0,1] (time, keeps x(t) monotonic), y ∈ [-0.5, 1.5]
        private static Vector2 _p1 = new Vector2(0.35f, 0f);
        private static Vector2 _p2 = new Vector2(0.65f, 1f);

        private const float CurveW = 340f, CurveH = 230f;
        private const int TexW = 340, TexH = 230;

        private static FieldInfo _dataFi;

        internal static bool IsOpen => _popupGo != null;

        internal static void Tick()
        {
            if (_popupGo == null) return;
            bool want = false;
            try
            {
                var ed = scnEditor.instance;
                want = ed != null && !ed.playMode && MainClass.EditorSuiteOn;
            }
            catch { }
            if (!want || Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        internal static void Dispose()
        {
            Close();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _canvasRect = null;
        }

        private static Dictionary<string, object> DataOf(ADOFAI.LevelEvent e)
        {
            if (_dataFi == null)
                _dataFi = typeof(ADOFAI.LevelEvent).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance);
            return _dataFi?.GetValue(e) as Dictionary<string, object>;
        }

        // ── bezier math ─────────────────────────────────────────────────────
        private static float BezAxis(float a, float b, float t) // component with endpoints 0,1
            => 3f * (1f - t) * (1f - t) * t * a + 3f * (1f - t) * t * t * b + t * t * t;

        // y at TIME x: solve x(t)=x by bisection (x is monotonic since handle x ∈ [0,1])
        internal static float YAtTime(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (BezAxis(_p1.x, _p2.x, mid) < x) lo = mid; else hi = mid;
            }
            float t = (lo + hi) * 0.5f;
            return BezAxis(_p1.y, _p2.y, t);
        }

        // ── UI ──────────────────────────────────────────────────────────────
        internal static void Open(ADOFAI.LevelEvent evt)
        {
            if (evt == null) return;
            Close();
            _target = evt;
            if (_canvasGo == null) BuildCanvas();
            else if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);

            const float w = CurveW + 40f, h = CurveH + 150f;
            _popupGo = new GameObject("BezierPopup", typeof(RectTransform));
            _popupGo.transform.SetParent(_canvasGo.transform, false);
            var blocker = (RectTransform)_popupGo.transform;
            blocker.anchorMin = Vector2.zero; blocker.anchorMax = Vector2.one;
            blocker.offsetMin = Vector2.zero; blocker.offsetMax = Vector2.zero;
            var blockImg = _popupGo.AddComponent<Image>();
            blockImg.color = new Color(0f, 0f, 0f, 0.35f);
            blockImg.raycastTarget = true;
            UI.ClickHandler.Attach(_popupGo, Close);

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(_popupGo.transform, false);
            var panel = (RectTransform)panelGo.transform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(w, h);
            var bg = panelGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 12f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.98f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true; // swallow — the blocker behind closes

            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(panelGo.transform, false);
            var tr = (RectTransform)titleGo.transform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.sizeDelta = new Vector2(0f, 22f);
            tr.anchoredPosition = new Vector2(0f, -10f);
            var title = UIBuilder.Tmp(titleGo, Loc.T("Custom bezier") + " · " + evt.eventType + " #" + evt.floor,
                14f, TextAnchor.MiddleCenter, Theme.Text);
            title.raycastTarget = false;

            // curve area
            var areaGo = new GameObject("Curve", typeof(RectTransform));
            areaGo.transform.SetParent(panelGo.transform, false);
            _curveArea = (RectTransform)areaGo.transform;
            _curveArea.anchorMin = new Vector2(0.5f, 1f); _curveArea.anchorMax = new Vector2(0.5f, 1f);
            _curveArea.pivot = new Vector2(0.5f, 1f);
            _curveArea.anchoredPosition = new Vector2(0f, -40f);
            _curveArea.sizeDelta = new Vector2(CurveW, CurveH);
            _curveImg = areaGo.AddComponent<RawImage>();
            _curveTex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false);
            _curveTex.filterMode = FilterMode.Bilinear;
            _curveImg.texture = _curveTex;
            _curveImg.raycastTarget = false;

            _h1 = MakeHandle(areaGo.transform, 1);
            _h2 = MakeHandle(areaGo.transform, 2);

            // segments field
            float rowY = -(40f + CurveH + 12f);
            var segLbl = new GameObject("SL", typeof(RectTransform));
            segLbl.transform.SetParent(panelGo.transform, false);
            var slr = (RectTransform)segLbl.transform;
            slr.anchorMin = slr.anchorMax = new Vector2(0f, 1f);
            slr.pivot = new Vector2(0f, 0.5f);
            slr.anchoredPosition = new Vector2(20f, rowY - 12f);
            slr.sizeDelta = new Vector2(110f, 22f);
            var slt = UIBuilder.Tmp(segLbl, Loc.T("Segments"), 12.5f, TextAnchor.MiddleLeft, Theme.TextMuted);
            slt.raycastTarget = false;

            var segGo = new GameObject("Seg", typeof(RectTransform));
            segGo.transform.SetParent(panelGo.transform, false);
            var sgr = (RectTransform)segGo.transform;
            sgr.anchorMin = sgr.anchorMax = new Vector2(0f, 1f);
            sgr.pivot = new Vector2(0f, 0.5f);
            sgr.anchoredPosition = new Vector2(120f, rowY - 12f);
            sgr.sizeDelta = new Vector2(56f, 24f);
            var sbg = segGo.AddComponent<RoundedRectGraphic>();
            sbg.Radius = 5f;
            sbg.color = new Color(1f, 1f, 1f, 0.08f);
            sbg.raycastTarget = true;
            var stGo = new GameObject("T", typeof(RectTransform));
            stGo.transform.SetParent(segGo.transform, false);
            var str2 = (RectTransform)stGo.transform;
            str2.anchorMin = Vector2.zero; str2.anchorMax = Vector2.one;
            str2.offsetMin = new Vector2(6f, 0f); str2.offsetMax = new Vector2(-6f, 0f);
            var stxt = UIBuilder.Tmp(stGo, "10", 12.5f, TextAnchor.MiddleLeft, Theme.Text);
            stxt.richText = false;
            _segField = UIBuilder.BuildInputField(segGo, stxt);
            _segField.lineType = TMP_InputField.LineType.SingleLine;
            _segField.text = "10";

            MakeButton(panelGo.transform, Loc.T("Cancel"), new Vector2(-96f, 22f), 84f, false, Close);
            MakeButton(panelGo.transform, Loc.T("Apply"), new Vector2(-8f, 22f), 84f, true, Apply);

            // Apply feedback: aborts must SAY why, not vanish into the log.
            var stGo2 = new GameObject("Status", typeof(RectTransform));
            stGo2.transform.SetParent(panelGo.transform, false);
            var str3 = (RectTransform)stGo2.transform;
            str3.anchorMin = new Vector2(0f, 0f); str3.anchorMax = new Vector2(1f, 0f);
            str3.pivot = new Vector2(0.5f, 0f);
            str3.anchoredPosition = new Vector2(0f, 44f);
            str3.sizeDelta = new Vector2(-40f, 20f);
            _status = UIBuilder.Tmp(stGo2, "", 12f, TextAnchor.MiddleLeft, new Color(1f, 0.62f, 0.45f));
            _status.raycastTarget = false;

            SyncHandles();
            Redraw();
        }

        private static RectTransform MakeHandle(Transform parent, int which)
        {
            var go = new GameObject("H" + which, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 0f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(18f, 18f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 9f;
            bg.color = which == 1
                ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 1f)
                : new Color(1f, 0.71f, 0.28f, 1f);
            bg.raycastTarget = true;
            var drag = go.AddComponent<HandleDrag>();
            drag.Which = which;
            return r;
        }

        private class HandleDrag : MonoBehaviour, IDragHandler, IBeginDragHandler
        {
            public int Which;
            public void OnBeginDrag(PointerEventData e) { }
            public void OnDrag(PointerEventData e)
            {
                if (_curveArea == null) return;
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_curveArea, e.position, e.pressEventCamera, out local))
                    return;
                var rect = _curveArea.rect;
                float nx = Mathf.Clamp01((local.x - rect.xMin) / rect.width);
                float nyRaw = (local.y - rect.yMin) / rect.height;      // 0..1 over the drawn range
                float ny = Mathf.Clamp(nyRaw * 2f - 0.5f, -0.5f, 1.5f); // texture maps y ∈ [-0.5, 1.5]
                if (Which == 1) _p1 = new Vector2(nx, ny); else _p2 = new Vector2(nx, ny);
                SyncHandles();
                Redraw();
            }
        }

        private static void SyncHandles()
        {
            if (_curveArea == null) return;
            var rect = _curveArea.rect;
            _h1.anchoredPosition = HandlePos(_p1, rect);
            _h2.anchoredPosition = HandlePos(_p2, rect);
        }

        private static Vector2 HandlePos(Vector2 p, Rect rect) =>
            new Vector2(p.x * rect.width, (p.y + 0.5f) / 2f * rect.height);

        private static int TexY(float v) => Mathf.Clamp(Mathf.RoundToInt((v + 0.5f) / 2f * (TexH - 1)), 0, TexH - 1);

        private static void Redraw()
        {
            if (_curveTex == null) return;
            var px = new Color32[TexW * TexH];
            var bgc = new Color32(255, 255, 255, 10);
            var baseline = new Color32(255, 255, 255, 45);
            int y0 = TexY(0f), y1 = TexY(1f);
            for (int x = 0; x < TexW; x++)
            {
                px[y0 * TexW + x] = baseline;
                px[y1 * TexW + x] = baseline;
                if (x % (TexW / 4) == 0) for (int y = 0; y < TexH; y++) px[y * TexW + x] = bgc;
            }
            // stems endpoint→control point
            DrawSeg(px, new Vector2(0f, 0f), _p1, new Color32(120, 160, 255, 90));
            DrawSeg(px, new Vector2(1f, 1f), _p2, new Color32(255, 190, 90, 90));
            // the curve itself
            var col = new Color32(255, 255, 255, 240);
            int prevY = -1;
            for (int gx = 0; gx < TexW; gx++)
            {
                float t = gx / (float)(TexW - 1);
                int gy = TexY(YAtTime(t));
                if (prevY >= 0)
                {
                    int lo = Mathf.Min(prevY, gy), hi = Mathf.Max(prevY, gy);
                    for (int yy = lo; yy <= hi; yy++) px[yy * TexW + gx] = col;
                }
                else px[gy * TexW + gx] = col;
                prevY = gy;
            }
            _curveTex.SetPixels32(px);
            _curveTex.Apply(false);
        }

        private static void DrawSeg(Color32[] px, Vector2 a, Vector2 b, Color32 c)
        {
            int steps = 60;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float x = Mathf.Lerp(a.x, b.x, t);
                float y = Mathf.Lerp(a.y, b.y, t);
                int gx = Mathf.Clamp(Mathf.RoundToInt(x * (TexW - 1)), 0, TexW - 1);
                int gy = TexY(y);
                px[gy * TexW + gx] = c;
            }
        }

        private static void MakeButton(Transform parent, string label, Vector2 fromBottomRight, float w, bool accent, Action onClick)
        {
            var go = new GameObject("Btn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(1f, 0f);
            r.anchoredPosition = new Vector2(fromBottomRight.x, fromBottomRight.y - 8f);
            r.sizeDelta = new Vector2(w, 28f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = accent
                ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.55f)
                : new Color(1f, 1f, 1f, 0.09f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;
            var txtGo = new GameObject("T", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            var txt = UIBuilder.Tmp(txtGo, label, 13f, TextAnchor.MiddleCenter, Theme.Text);
            txt.raycastTarget = false;
            UI.ClickHandler.Attach(go, onClick);
        }

        // ── apply: decompose into N linear segments ─────────────────────────

        private static float NumOf(ADOFAI.LevelEvent e, string key, float def)
        {
            try
            {
                var d = DataOf(e);
                if (d == null || !d.TryGetValue(key, out var v) || v == null) return def;
                float f = Convert.ToSingle(v);
                return float.IsNaN(f) ? def : f;
            }
            catch { return def; }
        }

        private static bool PropOff(ADOFAI.LevelEvent e, string key)
        {
            try { return e.disabled != null && e.disabled.TryGetValue(key, out bool d) && d; }
            catch { return false; }
        }

        private static bool TouchesNum(ADOFAI.LevelEvent e, string key)
        {
            if (PropOff(e, key)) return false;
            var d = DataOf(e);
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return false;
            try { float f = Convert.ToSingle(v); return !float.IsNaN(f); } catch { return false; }
        }

        // order key: floor first, angleOffset breaks ties on the same tile
        private static bool Before(ADOFAI.LevelEvent a, ADOFAI.LevelEvent b) =>
            a.floor < b.floor || (a.floor == b.floor && NumOf(a, "angleOffset", 0f) < NumOf(b, "angleOffset", 0f));

        // start value = the target of the nearest earlier MoveCamera touching the property
        private static bool TryStart(scnEditor ed, ADOFAI.LevelEvent evt, string key, out float start)
        {
            start = 0f;
            ADOFAI.LevelEvent best = null;
            foreach (var e in ed.events)
            {
                if (e == null || ReferenceEquals(e, evt) || e.eventType != ADOFAI.LevelEventType.MoveCamera) continue;
                if (!e.active || !TouchesNum(e, key) || !Before(e, evt)) continue;
                if (best == null || Before(best, e)) best = e;
            }
            if (best == null) return false;
            start = NumOf(best, key, 0f);
            return true;
        }

        private static bool TryStartPosComp(scnEditor ed, ADOFAI.LevelEvent evt, bool isX, out float start)
        {
            start = 0f;
            ADOFAI.LevelEvent best = null;
            foreach (var e in ed.events)
            {
                if (e == null || ReferenceEquals(e, evt) || e.eventType != ADOFAI.LevelEventType.MoveCamera) continue;
                if (!e.active || PropOff(e, "position") || !Before(e, evt)) continue;
                var d = DataOf(e);
                if (d == null || !d.TryGetValue("position", out var v) || !(v is Vector2 p)) continue;
                float comp = isX ? p.x : p.y;
                if (float.IsNaN(comp)) continue;
                if (best == null || Before(best, e)) best = e;
            }
            if (best == null) return false;
            var bd = DataOf(best);
            if (bd == null || !bd.TryGetValue("position", out var bv) || !(bv is Vector2 bp)) return false;
            start = isX ? bp.x : bp.y;
            return true;
        }

        private static void Apply()
        {
            var evt = _target;
            var ed = scnEditor.instance;
            if (evt == null || ed == null) { Close(); return; }

            int n = 10;
            try { int.TryParse(_segField.text, out n); } catch { }
            n = Mathf.Clamp(n, 4, 32);

            float dur = NumOf(evt, "duration", 0f);
            if (dur <= 0.01f) { Status(Loc.T("This keyframe has no duration to decompose")); return; }
            float a0 = NumOf(evt, "angleOffset", 0f);

            // gather animated properties + their start values; abort if any start is unknown
            bool doRot = TouchesNum(evt, "rotation"), doZoom = TouchesNum(evt, "zoom");
            var d0 = DataOf(evt);
            Vector2 posEnd = Vector2.zero;
            bool hasPosVec = false;
            if (!PropOff(evt, "position") && d0 != null && d0.TryGetValue("position", out var pv) && pv is Vector2 pvv)
            { posEnd = pvv; hasPosVec = true; }
            bool doPosX = hasPosVec && !float.IsNaN(posEnd.x);
            bool doPosY = hasPosVec && !float.IsNaN(posEnd.y);

            float rotS = 0f, zoomS = 0f, posXS = 0f, posYS = 0f;
            if (doRot && !TryStart(ed, evt, "rotation", out rotS))
            { Status(Loc.T("Needs an earlier keyframe for the start value") + " (" + Loc.T("Rotation") + ")"); return; }
            if (doZoom && !TryStart(ed, evt, "zoom", out zoomS))
            { Status(Loc.T("Needs an earlier keyframe for the start value") + " (" + Loc.T("Zoom") + ")"); return; }
            if (doPosX && !TryStartPosComp(ed, evt, true, out posXS))
            { Status(Loc.T("Needs an earlier keyframe for the start value") + " (X)"); return; }
            if (doPosY && !TryStartPosComp(ed, evt, false, out posYS))
            { Status(Loc.T("Needs an earlier keyframe for the start value") + " (Y)"); return; }
            if (!doRot && !doZoom && !doPosX && !doPosY)
            { Status(Loc.T("This keyframe animates nothing decomposable")); return; }

            float rotE = NumOf(evt, "rotation", 0f), zoomE = NumOf(evt, "zoom", 100f);
            try
            {
                using (new SaveStateScope(ed))
                {
                    ed.events.Remove(evt);
                    for (int i = 0; i < n; i++)
                    {
                        float t1 = (i + 1) / (float)n;
                        float y = YAtTime(t1);
                        var seg = evt.Copy();
                        seg.floor = evt.floor;
                        seg["duration"] = dur / n;
                        seg["angleOffset"] = a0 + 180f * dur * (i / (float)n);
                        seg["ease"] = DG.Tweening.Ease.Linear;
                        if (doRot) seg["rotation"] = rotS + (rotE - rotS) * y;
                        if (doZoom) seg["zoom"] = zoomS + (zoomE - zoomS) * y;
                        if (doPosX || doPosY)
                            seg["position"] = new Vector2(
                                doPosX ? posXS + (posEnd.x - posXS) * y : float.NaN,
                                doPosY ? posYS + (posEnd.y - posYS) * y : float.NaN);
                        ed.events.Add(seg);
                    }
                    ed.ApplyEventsToFloors();
                }
                SapphireLog.Log("Bezier: decomposed into " + n + " segments");
            }
            catch (Exception ex) { SapphireLog.Log("Bezier: apply failed: " + ex.Message); }
            Close();
        }

        private static void Status(string msg)
        {
            if (_status != null) _status.text = msg;
        }

        private static void Close()
        {
            if (_popupGo != null) UnityEngine.Object.Destroy(_popupGo);
            if (_curveTex != null) UnityEngine.Object.Destroy(_curveTex);
            _popupGo = null; _target = null; _curveTex = null;
            _curveImg = null; _curveArea = null; _h1 = null; _h2 = null; _segField = null; _status = null;
            if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
        }

        private static void BuildCanvas()
        {
            _canvasGo = new GameObject("SapphireBezier", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 947; // above the ease picker
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

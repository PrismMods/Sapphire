using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sapphire.UI
{
    /* Shared scaffolding for Sapphire's floating tool panels (Magic Shape, Track Tools,
       Deco Tools…): own overlay canvas + draggable rounded panel with title/close, plus
       the flat cell/label/input primitives, so each module only writes its rows. One
       instance per module; Rebuild() keeps the panel's dragged position across rebuilds. */
    internal class PanelKit
    {
        internal const float RowH = 24f, Gap = 5f, Pad = 10f;

        private readonly string _canvasName;
        private readonly int _sortingOrder;
        internal readonly float W;
        internal GameObject CanvasGo, PanelGo;
        private Canvas _canvas;
        private GraphicRaycaster _raycaster;

        /* Set by owners that keep a widget (e.g. the selector's reopen chip) parented to the
           canvas while the panel itself is hidden — keeps the canvas rendering/raycasting for
           that widget even though PanelGo is inactive. */
        internal bool ChipAlive;

        // Focusable panels take part in DE-style window z-ordering (see the focus block below).
        internal readonly bool Focusable;

        internal PanelKit(string canvasName, int sortingOrder, float width, bool focusable = false)
        {
            _canvasName = canvasName;
            _sortingOrder = sortingOrder;
            W = width;
            Focusable = focusable;
        }

        internal bool Built => CanvasGo != null && PanelGo != null;
        internal bool Visible => PanelGo != null && PanelGo.activeSelf;

        // fired when a header drag releases — hook SnapDockOnDragEnd for edge docking
        internal Action OnDragEnd;

        /* Adobe-style edge docking. DockSide is a persistent STATE (0 float · 1 left ·
           2 right): the owner calls TickDock each frame with the chrome insets (below the
           file row/toolbar, above the timeline) and the docked panel keeps following them
           — timeline grows, panel shrinks. Releasing a header drag near an edge docks;
           releasing anywhere else undocks. Width stays user-resizable while docked. */
        internal int DockSide { get; private set; }   // 0 float · 1 left · 2 right — set via SetDock
        private float _dockWeight = 1f;                // vertical share within its side's stack
        private DragHandle _drag;

        internal bool HeaderDragging => _drag != null && _drag.Dragging;

        internal void SnapDockOnDragEnd(float threshold = 28f)
        {
            if (PanelGo == null || CanvasGo == null) return;
            var r = (RectTransform)PanelGo.transform;
            var c = (RectTransform)CanvasGo.transform;
            float cw = c.rect.width;
            float w = r.sizeDelta.x;
            var p = r.anchoredPosition;
            int side;
            if (p.x <= threshold) side = 1;
            else if (p.x + w >= cw - threshold) side = 2;
            else side = 0;
            SetDock(side);
        }

        /* DE-style window z-ordering. Focusable panels split into two sub-bands: DOCKED panels
           sit low (a backdrop) and FLOATING panels sit high, each ordered by focus recency, so
           the last-dragged/-clicked floating window is on top and a floating panel (e.g. the
           level-settings window) always renders above docked ones. The floating band stays
           below the fixed popups (filter 945 / ease 946 / bezier 947 / help 949). */
        private const int DockedZBase = 901;   // docked windows: 901.. (below the timeline chrome)
        private const int FloatZBase = 936;    // floating windows: 936.. (below filter popup at 945)
        private static int _focusClock;
        private int _focusStamp;
        private static readonly System.Collections.Generic.List<PanelKit> _focusReg =
            new System.Collections.Generic.List<PanelKit>();

        internal void BringToFront()
        {
            if (!Focusable) return;
            _focusStamp = ++_focusClock;
            ReRankFocus();
        }

        private static void ReRankFocus()
        {
            _focusReg.Sort((a, b) =>
            {
                bool af = a.DockSide == 0, bf = b.DockSide == 0; // floating?
                if (af != bf) return af ? 1 : -1;                // docked below floating
                return a._focusStamp.CompareTo(b._focusStamp);   // older below newer
            });
            int di = 0, fi = 0;
            for (int i = 0; i < _focusReg.Count; i++)
            {
                var p = _focusReg[i];
                if (p._canvas == null) continue;
                p._canvas.sortingOrder = p.DockSide == 0 ? FloatZBase + fi++ : DockedZBase + di++;
            }
        }

        // Called once per frame (MainClass ticker): a pointer-down over the top-most focusable
        // panel raises it — catches header drags, resizes, and clicks anywhere on the body.
        internal static void TickFocus()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            Vector2 m = Input.mousePosition;
            PanelKit hit = null; int best = int.MinValue;
            for (int i = 0; i < _focusReg.Count; i++)
            {
                var p = _focusReg[i];
                if (!p.Visible || p.PanelGo == null || p._canvas == null || !p._canvas.enabled) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint((RectTransform)p.PanelGo.transform, m, null)) continue;
                if (p._canvas.sortingOrder > best) { best = p._canvas.sortingOrder; hit = p; }
            }
            if (hit != null) hit.BringToFront();
        }

        // ── multi-window sidebar docking ────────────────────────────────────────
        /* Focusable panels dock to either sidebar. Panels on the same side stack vertically,
           SHARE that side's width, and split its height by per-panel weights. A divider between
           two stacked panels retimes the split; a divider on the sidebar's inner edge sets the
           shared width. While a header drag hovers an edge a drop indicator previews the target.
           A central tick (MainClass) lays this out AFTER the modules' own Show/clamp, so docked
           geometry always wins. All dock chrome shares one overlay canvas whose full-screen
           DockRoot uses the SAME top-left coordinate space as the panels' own canvases. */
        private static readonly System.Collections.Generic.List<PanelKit> _dockL = new System.Collections.Generic.List<PanelKit>();
        private static readonly System.Collections.Generic.List<PanelKit> _dockR = new System.Collections.Generic.List<PanelKit>();
        private static readonly System.Collections.Generic.List<PanelKit> _dockTmp = new System.Collections.Generic.List<PanelKit>();
        private static readonly System.Collections.Generic.List<PanelKit> _dockTmp2 = new System.Collections.Generic.List<PanelKit>();
        private static float _sideWL = 360f, _sideWR = 360f;
        internal static bool DockDragActive; // a dock divider is being dragged
        private const float SideMin = 220f, DockGap = 6f, DockMargin = 8f, DockPanelMinH = 120f, EdgeSnap = 28f;
        private static float _lastTop, _lastBottom, _lastCh, _lastCw; // for divider-drag geometry
        private static int _lastScreenW, _lastScreenH;                // ClampFloating runs only on resize
        // Divider components resolved once — GetComponent per divider per frame was pure overhead.
        private static DockDivider _wDivCompL, _wDivCompR;

        private static System.Collections.Generic.List<PanelKit> SideList(int side) => side == 1 ? _dockL : _dockR;

        /* Dragging a panel toward an edge must still raise the drop indicator even when nothing
           is docked yet — that is exactly the dock-your-first-panel case, so the dock early-out
           cannot key on the dock lists alone. */
        private static bool AnyHeaderDragging()
        {
            for (int i = 0; i < _focusReg.Count; i++)
                if (_focusReg[i].Focusable && _focusReg[i].HeaderDragging) return true;
            return false;
        }

        /* Floating focusable windows are user-placed by drag; nothing repositions them when the
           game window resizes, so a shrink can strand them off-screen. Clamp each so a usable
           slice (≥80px + its header) always stays reachable. Only writes when actually out of
           bounds (no per-frame canvas re-batch). Docked windows are handled by the dock layout. */
        private static void ClampFloating()
        {
            for (int i = 0; i < _focusReg.Count; i++)
            {
                var p = _focusReg[i];
                if (p.DockSide != 0 || !p.Visible || p.PanelGo == null || p.CanvasGo == null || p.HeaderDragging) continue;
                var c = (RectTransform)p.CanvasGo.transform;
                float cw = c.rect.width, ch = c.rect.height;
                if (cw < 1f || ch < 1f) continue;
                var r = (RectTransform)p.PanelGo.transform;
                float w = r.sizeDelta.x;
                var pos = r.anchoredPosition;
                float nx = Mathf.Clamp(pos.x, -w + 80f, cw - 80f);
                float ny = Mathf.Clamp(pos.y, -(ch - 28f), 0f); // keep the 28px header on-screen
                if (!Mathf.Approximately(nx, pos.x) || !Mathf.Approximately(ny, pos.y))
                    r.anchoredPosition = new Vector2(nx, ny);
            }
        }

        internal void SetDock(int side)
        {
            _dockL.Remove(this); _dockR.Remove(this);
            DockSide = side;
            if (side == 1) _dockL.Add(this);
            else if (side == 2) _dockR.Add(this);
            ReRankFocus();
        }

        // Central per-frame dock layout (topMargin/bottomInset clear the top chrome + timeline).
        internal static void TickDocks(float topMargin, float bottomInset)
        {
            /* Only recovers floating windows from a window resize, so it only needs to run when
               the screen actually changed size — it was walking every registered panel with two
               native RectTransform.rect reads apiece, every frame. */
            if (Screen.width != _lastScreenW || Screen.height != _lastScreenH)
            {
                _lastScreenW = Screen.width; _lastScreenH = Screen.height;
                ClampFloating();
            }
            /* EnsureDockChrome creates _dockChromeGo once and never destroys it, so keying the
               early-out on it meant that after ANY panel had ever docked, the whole layout path
               ran every frame forever — even with nothing docked. Key on the dock lists and
               idle the chrome canvas instead. */
            if (_dockL.Count == 0 && _dockR.Count == 0 && !AnyHeaderDragging())
            {
                if (_dockCanvas != null && _dockCanvas.enabled) _dockCanvas.enabled = false;
                return;
            }
            EnsureDockChrome();
            if (_dockRoot == null) return;
            float cw = _dockRoot.rect.width, ch = _dockRoot.rect.height;
            _lastTop = topMargin; _lastBottom = bottomInset; _lastCh = ch; _lastCw = cw;
            _hDivUsed = 0;
            LayoutSide(_dockL, 1, ref _sideWL, cw, ch, topMargin, bottomInset);
            LayoutSide(_dockR, 2, ref _sideWR, cw, ch, topMargin, bottomInset);
            for (int i = _hDivUsed; i < _hDivPool.Count; i++)
                if (_hDivPool[i].gameObject.activeSelf) _hDivPool[i].gameObject.SetActive(false);
            UpdateDropIndicator(cw, ch, topMargin, bottomInset);
            // Idle the chrome canvas (its raycaster) whenever nothing on it is showing.
            bool need = _hDivUsed > 0
                        || (_wDivL != null && _wDivL.activeSelf) || (_wDivR != null && _wDivR.activeSelf)
                        || (_dropInd != null && _dropInd.activeSelf);
            if (_dockCanvas != null && _dockCanvas.enabled != need) _dockCanvas.enabled = need;
        }

        private static void LayoutSide(System.Collections.Generic.List<PanelKit> list, int side,
            ref float sideW, float cw, float ch, float topMargin, float bottomInset)
        {
            /* RemoveAll's lambda captures `side`, so the compiler cannot cache it: a display
               class plus a Predicate allocated on every call, twice per frame. Manual reverse
               sweep instead — same semantics, no allocation. */
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var q = list[i];
                if (q == null || q.DockSide != side || q.CanvasGo == null) list.RemoveAt(i);
            }
            _dockTmp.Clear();
            for (int i = 0; i < list.Count; i++)
                if (list[i].Visible && !list[i].HeaderDragging) _dockTmp.Add(list[i]);
            int n = _dockTmp.Count;
            var wDiv = side == 1 ? _wDivL : _wDivR;
            if (wDiv != null && wDiv.activeSelf != (n > 0)) wDiv.SetActive(n > 0);
            if (n == 0) return;

            sideW = Mathf.Clamp(sideW, SideMin, cw * 0.6f);
            float H = Mathf.Max(DockPanelMinH, ch - topMargin - bottomInset);
            float avail = H - (n - 1) * DockGap;
            float totW = 0f;
            for (int i = 0; i < n; i++) totW += _dockTmp[i]._dockWeight;
            if (totW <= 0.01f) totW = n;
            float x = side == 1 ? DockMargin : cw - sideW - DockMargin;
            float y = -topMargin;
            for (int i = 0; i < n; i++)
            {
                var p = _dockTmp[i];
                float h = avail * (p._dockWeight / totW);
                var r = (RectTransform)p.PanelGo.transform;
                r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
                r.pivot = new Vector2(0f, 1f);
                var wantPos = new Vector2(x, y);
                var wantSize = new Vector2(sideW, h);
                if ((r.anchoredPosition - wantPos).sqrMagnitude > 1f) r.anchoredPosition = wantPos;
                if ((r.sizeDelta - wantSize).sqrMagnitude > 1f) r.sizeDelta = wantSize;
                if (i < n - 1)
                {
                    var div = GetHDiv();
                    div.Set(side, i, false);
                    var dr = (RectTransform)div.transform;
                    dr.sizeDelta = new Vector2(sideW, DockGap + 6f);
                    dr.anchoredPosition = new Vector2(x + sideW * 0.5f, y - h - DockGap * 0.5f);
                }
                y -= h + DockGap;
            }
            if (wDiv != null)
            {
                var wComp = side == 1 ? _wDivCompL : _wDivCompR;
                if (wComp == null)
                {
                    wComp = wDiv.GetComponent<DockDivider>();
                    if (side == 1) _wDivCompL = wComp; else _wDivCompR = wComp;
                }
                wComp.Set(side, -1, true);
                var dr = (RectTransform)wDiv.transform;
                float dx = side == 1 ? x + sideW + 3f : x - 3f;
                dr.sizeDelta = new Vector2(12f, H); // wide hit target; the visible pill stays thin
                dr.anchoredPosition = new Vector2(dx, -topMargin - H * 0.5f);
            }
        }

        internal static void OnDockDivider(int side, int index, bool width, Vector2 local)
        {
            if (width)
            {
                float sw = side == 1 ? local.x - DockMargin : _lastCw - DockMargin - local.x;
                sw = Mathf.Clamp(sw, SideMin, _lastCw * 0.6f);
                if (side == 1) _sideWL = sw; else _sideWR = sw;
                return;
            }
            _dockTmp2.Clear();
            var list = SideList(side);
            for (int i = 0; i < list.Count; i++)
                if (list[i].Visible && !list[i].HeaderDragging) _dockTmp2.Add(list[i]);
            int n = _dockTmp2.Count;
            if (index < 0 || index + 1 >= n) return;
            var a = _dockTmp2[index]; var b = _dockTmp2[index + 1];
            float H = Mathf.Max(DockPanelMinH, _lastCh - _lastTop - _lastBottom);
            float avail = H - (n - 1) * DockGap;
            float totW = 0f; for (int i = 0; i < n; i++) totW += _dockTmp2[i]._dockWeight;
            if (totW <= 0.01f) totW = n;
            float yTop = -_lastTop;
            for (int i = 0; i < index; i++) yTop -= avail * (_dockTmp2[i]._dockWeight / totW) + DockGap;
            float combW = a._dockWeight + b._dockWeight;
            float combH = avail * (combW / totW);
            float botOfB = yTop - combH - DockGap;
            float ly = Mathf.Clamp(local.y, botOfB + DockPanelMinH, yTop - DockPanelMinH);
            float hA = yTop - ly, hB = ly - botOfB;
            if (hA + hB <= 0.01f) return;
            a._dockWeight = Mathf.Max(0.05f, combW * hA / (hA + hB));
            b._dockWeight = Mathf.Max(0.05f, combW - a._dockWeight);
        }

        private static void UpdateDropIndicator(float cw, float ch, float topMargin, float bottomInset)
        {
            PanelKit drag = null;
            for (int i = 0; i < _focusReg.Count; i++)
                if (_focusReg[i].Focusable && _focusReg[i].HeaderDragging && _focusReg[i].PanelGo != null)
                { drag = _focusReg[i]; break; }
            int side = 0;
            if (drag != null)
            {
                var pr = (RectTransform)drag.PanelGo.transform;
                var pos = pr.anchoredPosition; float w = pr.sizeDelta.x;
                if (pos.x <= EdgeSnap) side = 1;
                else if (pos.x + w >= cw - EdgeSnap) side = 2;
            }
            if (side == 0)
            {
                if (_dropInd != null && _dropInd.activeSelf) _dropInd.SetActive(false);
                return;
            }
            float sideW = side == 1 ? _sideWL : _sideWR;
            float H = Mathf.Max(DockPanelMinH, ch - topMargin - bottomInset);
            float x = side == 1 ? DockMargin : cw - sideW - DockMargin;
            var dr = (RectTransform)_dropInd.transform;
            dr.sizeDelta = new Vector2(sideW, H);
            dr.anchoredPosition = new Vector2(x + sideW * 0.5f, -topMargin - H * 0.5f);
            if (!_dropInd.activeSelf) _dropInd.SetActive(true);
        }

        // ── dock chrome (shared canvas hosting dividers + drop indicator) ────────
        private static GameObject _dockChromeGo, _dropInd, _wDivL, _wDivR;
        private static Canvas _dockCanvas;
        private static RectTransform _dockRoot;
        private static readonly System.Collections.Generic.List<DockDivider> _hDivPool = new System.Collections.Generic.List<DockDivider>();
        private static int _hDivUsed;

        private static void EnsureDockChrome()
        {
            if (_dockChromeGo != null) return;
            _dockChromeGo = new GameObject("SapphireDockChrome", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_dockChromeGo);
            _dockCanvas = _dockChromeGo.AddComponent<Canvas>();
            var canvas = _dockCanvas;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 948; // above floating windows, below top chrome/popups
            var scaler = _dockChromeGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _dockChromeGo.AddComponent<GraphicRaycaster>();
            // full-screen top-left-pivot root so dividers share the panels' coordinate space
            var rootGo = new GameObject("DockRoot", typeof(RectTransform));
            rootGo.transform.SetParent(_dockChromeGo.transform, false);
            _dockRoot = (RectTransform)rootGo.transform;
            _dockRoot.anchorMin = Vector2.zero; _dockRoot.anchorMax = Vector2.one;
            _dockRoot.offsetMin = Vector2.zero; _dockRoot.offsetMax = Vector2.zero;
            _dockRoot.pivot = new Vector2(0f, 1f);
            _wDivL = MakeDivider(false); _wDivL.SetActive(false); // sidebar-edge width handle → vertical grip
            _wDivR = MakeDivider(false); _wDivR.SetActive(false);
            _dropInd = new GameObject("DropZone", typeof(RectTransform));
            _dropInd.transform.SetParent(_dockRoot, false);
            var dr = (RectTransform)_dropInd.transform;
            dr.anchorMin = dr.anchorMax = new Vector2(0f, 1f);
            dr.pivot = new Vector2(0.5f, 0.5f);
            var di = _dropInd.AddComponent<Image>();
            di.sprite = Theme.White;
            di.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.20f);
            di.raycastTarget = false;
            _dropInd.SetActive(false);
        }

        // Transparent hit strip with a small centered grip pill (short bar oriented across the
        // drag axis) — reads as a splitter handle without the heavy solid bar.
        private static GameObject MakeDivider(bool horizontalGrip)
        {
            var go = new GameObject("DockDivider", typeof(RectTransform));
            go.transform.SetParent(_dockRoot, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0.5f, 0.5f);
            var img = go.AddComponent<Image>();
            img.sprite = Theme.White;
            img.color = Color.clear;    // invisible; the pill is the only visible part
            img.raycastTarget = true;

            var pillGo = new GameObject("Grip", typeof(RectTransform));
            pillGo.transform.SetParent(go.transform, false);
            var pr = (RectTransform)pillGo.transform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.pivot = new Vector2(0.5f, 0.5f);
            pr.anchoredPosition = Vector2.zero;
            pr.sizeDelta = horizontalGrip ? new Vector2(36f, 4f) : new Vector2(4f, 36f);
            var pill = pillGo.AddComponent<RoundedRectGraphic>();
            pill.Radius = 2f;
            pill.color = new Color(1f, 1f, 1f, 0.22f);
            pill.raycastTarget = false;

            go.AddComponent<DockDivider>();
            return go;
        }

        // Pools the DockDivider itself, not just its GameObject — the caller needed the
        // component every frame and was re-resolving it with GetComponent each time.
        private static DockDivider GetHDiv()
        {
            DockDivider div;
            if (_hDivUsed < _hDivPool.Count) div = _hDivPool[_hDivUsed];
            else // between stacked panels → horizontal grip
            {
                div = MakeDivider(true).GetComponent<DockDivider>();
                _hDivPool.Add(div);
            }
            _hDivUsed++;
            if (!div.gameObject.activeSelf) div.gameObject.SetActive(true);
            return div;
        }

        internal void Show(bool on)
        {
            bool was = PanelGo != null && PanelGo.activeSelf;
            if (PanelGo != null && PanelGo.activeSelf != on) PanelGo.SetActive(on);
            SyncCanvasActive();
            if (on && !was && Focusable) BringToFront(); // newly shown → raise to front
        }

        /* Disable the Canvas + GraphicRaycaster while nothing on this canvas is visible: an
           idle-but-active overlay canvas is still a render batch and a raycaster the EventSystem
           walks every pointer frame. Disabling the components (vs SetActive) skips rendering and
           raycasting without tearing down the built hierarchy, so re-showing is churn-free. */
        internal void SyncCanvasActive()
        {
            if (CanvasGo == null) return;
            bool need = (PanelGo != null && PanelGo.activeSelf) || ChipAlive;
            if (_canvas != null && _canvas.enabled != need) _canvas.enabled = need;
            if (_raycaster != null && _raycaster.enabled != need) _raycaster.enabled = need;
        }

        internal void Dispose()
        {
            _focusReg.Remove(this);
            _dockL.Remove(this); _dockR.Remove(this);
            if (CanvasGo != null) UnityEngine.Object.Destroy(CanvasGo);
            CanvasGo = null; PanelGo = null;
        }

        internal void Rebuild(string title, Action onClose, Vector2 defaultPos)
        {
            EnsureCanvas();
            Vector2 keepPos = defaultPos;
            if (PanelGo != null)
            {
                keepPos = ((RectTransform)PanelGo.transform).anchoredPosition;
                UnityEngine.Object.Destroy(PanelGo);
            }

            PanelGo = new GameObject("Panel", typeof(RectTransform));
            PanelGo.transform.SetParent(CanvasGo.transform, false);
            var r = (RectTransform)PanelGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = keepPos;
            var bg = PanelGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 10f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.94f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;

            var headGo = new GameObject("Head", typeof(RectTransform));
            headGo.transform.SetParent(PanelGo.transform, false);
            var hr = (RectTransform)headGo.transform;
            hr.anchorMin = new Vector2(0f, 1f); hr.anchorMax = new Vector2(1f, 1f);
            hr.pivot = new Vector2(0.5f, 1f);
            hr.anchoredPosition = Vector2.zero;
            hr.sizeDelta = new Vector2(0f, 28f);
            var headBg = headGo.AddComponent<RoundedRectGraphic>();
            headBg.Radius = 10f;
            headBg.color = new Color(1f, 1f, 1f, 0.04f);
            headBg.raycastTarget = true;
            _drag = headGo.AddComponent<DragHandle>();
            // focusable windows are dockable by default: releasing a header drag near an edge docks
            if (Focusable && OnDragEnd == null) OnDragEnd = () => SnapDockOnDragEnd();
            _drag.DragEnd = () => OnDragEnd?.Invoke();
            var titleGo = new GameObject("T", typeof(RectTransform));
            titleGo.transform.SetParent(headGo.transform, false);
            var tr = (RectTransform)titleGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(Pad, 0f); tr.offsetMax = new Vector2(-30f, 0f);
            var titleTmp = UIBuilder.Tmp(titleGo, title, 13.5f, TextAnchor.MiddleLeft, Theme.Text);
            titleTmp.raycastTarget = false;

            // Close × anchored to the panel's TOP-RIGHT so it rides the right edge when the
            // panel is resized wider than the template width W (Cell anchors top-left, which
            // left the × stranded mid-header on resizable panels).
            var xGo = new GameObject("Close", typeof(RectTransform));
            xGo.transform.SetParent(PanelGo.transform, false);
            var xr = (RectTransform)xGo.transform;
            xr.anchorMin = xr.anchorMax = new Vector2(1f, 1f);
            xr.pivot = new Vector2(1f, 1f);
            xr.anchoredPosition = new Vector2(-6f, -4f);
            xr.sizeDelta = new Vector2(20f, 20f);
            var xbg = xGo.AddComponent<RoundedRectGraphic>();
            xbg.Radius = 5f;
            xbg.color = new Color(1f, 1f, 1f, 0.08f);
            xbg.BorderWidth = 1f;
            xbg.BorderColor = new Color(1f, 1f, 1f, 0.1f);
            xbg.raycastTarget = true;
            var xlGo = new GameObject("L", typeof(RectTransform));
            xlGo.transform.SetParent(xGo.transform, false);
            var xlr = (RectTransform)xlGo.transform;
            xlr.anchorMin = Vector2.zero; xlr.anchorMax = Vector2.one;
            xlr.offsetMin = xlr.offsetMax = Vector2.zero;
            var xtmp = UIBuilder.Tmp(xlGo, "×", 12f, TextAnchor.MiddleCenter, Theme.Text);
            xtmp.raycastTarget = false;
            ClickHandler.Attach(xGo, onClose);
        }

        internal void SetHeight(float yEnd)
        {
            if (PanelGo != null) ((RectTransform)PanelGo.transform).sizeDelta = new Vector2(W, -yEnd + Pad);
        }

        internal RoundedRectGraphic Cell(string text, float x, float y, float w, float h,
            Action onClick, bool button, bool accent = false, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Cell", typeof(RectTransform));
            go.transform.SetParent(PanelGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = accent
                ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.35f)
                : button ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.05f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.1f);
            bg.raycastTarget = true;
            var lblGo = new GameObject("L", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(anchor == TextAnchor.MiddleLeft ? 8f : 0f, 0f);
            lr.offsetMax = Vector2.zero;
            var tmp = UIBuilder.Tmp(lblGo, text, 12f, anchor, Theme.Text);
            tmp.raycastTarget = false;
            ClickHandler.Attach(go, onClick);
            return bg;
        }

        internal TextMeshProUGUI Label(string text, float x, float y, float w, float h, Color color, float size = 12.5f)
        {
            var go = new GameObject("Lbl", typeof(RectTransform));
            go.transform.SetParent(PanelGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            var tmp = UIBuilder.Tmp(go, text, size, TextAnchor.MiddleLeft, color);
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false;
            return tmp;
        }

        internal void InputField(float x, float y, float w, string value, Action<string> commit)
        {
            var go = new GameObject("F", typeof(RectTransform));
            go.transform.SetParent(PanelGo.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, RowH);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = new Color(1f, 1f, 1f, 0.08f);
            bg.raycastTarget = true;
            var txtGo = new GameObject("T", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(7f, 0f); tr.offsetMax = new Vector2(-7f, 0f);
            var txt = UIBuilder.Tmp(txtGo, value, 12.5f, TextAnchor.MiddleLeft, Theme.Text);
            txt.richText = false;
            var field = UIBuilder.BuildInputField(go, txt);
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.text = value;
            field.onEndEdit.AddListener(v => commit(v));
        }

        // muted multi-line status block; caller keeps the returned TMP to update it live
        internal TextMeshProUGUI Status(string text, float y)
        {
            var go = new GameObject("Status", typeof(RectTransform));
            go.transform.SetParent(PanelGo.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(Pad, y);
            r.sizeDelta = new Vector2(W - Pad * 2f, 30f);
            var tmp = UIBuilder.Tmp(go, text, 11.5f, TextAnchor.UpperLeft, Theme.TextMuted);
            tmp.enableWordWrapping = true;
            return tmp;
        }

        internal void Footer(string text, float y)
            => Label(text, Pad, y, W - Pad * 2f, 12f, new Color(0.42f, 0.42f, 0.47f, 1f), 9.5f);

        internal static Color Tint(bool on) => on
            ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f)
            : new Color(1f, 1f, 1f, 0.05f);

        // ── level helpers shared by the tool panels ──────────────────────────

        internal static int LevelLen()
        {
            try { return ADOBase.lm.floorAngles.Length; } catch { return 0; }
        }

        internal static int ResolveTile(int v) => v < 0 ? LevelLen() : v; // -1 = end of level

        internal static bool SelectionRange(out int min, out int max)
        {
            min = int.MaxValue; max = int.MinValue;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null || ed.selectedFloors == null) return false;
            foreach (var f in ed.selectedFloors)
            {
                if (f == null) continue;
                if (f.seqID < min) min = f.seqID;
                if (f.seqID > max) max = f.seqID;
            }
            return min != int.MaxValue;
        }

        // ── shared row builders (all return the next y) ──────────────────────

        internal float LblW = 104f;

        internal float FieldRow(float y, string label, string value, Action<string> commit)
        {
            Label(label, Pad, y, LblW, RowH, Theme.TextMuted);
            InputField(Pad + LblW + 4f, y, W - Pad * 2f - LblW - 4f, value, commit);
            return y - (RowH + Gap);
        }

        internal float FloatRow(float y, string label, float value, Action<float> set, string fmt = "0.###")
            => FieldRow(y, label, value.ToString(fmt), v => { float f; if (float.TryParse(v, out f)) set(f); });

        internal float IntRow(float y, string label, int value, Action<int> set)
            => FieldRow(y, label, value.ToString(), v => { int n; if (int.TryParse(v, out n)) set(n); });

        // [label] [a] [b] — two int fields
        internal float PairRow(float y, string label, Func<int> getA, Func<int> getB, Action<int, int> set)
        {
            Label(label, Pad, y, LblW, RowH, Theme.TextMuted);
            float x = Pad + LblW + 4f;
            const float fw = 56f;
            InputField(x, y, fw, getA().ToString(), v => { int n; if (int.TryParse(v, out n)) set(n, getB()); });
            InputField(x + fw + Gap, y, fw, getB().ToString(), v => { int n; if (int.TryParse(v, out n)) set(getA(), n); });
            return y - (RowH + Gap);
        }

        // [label] [a] [b] — two float fields, no toggle
        internal float FloatPairRow(float y, string label, Func<float> getA, Func<float> getB, Action<float, float> set)
        {
            Label(label, Pad, y, LblW, RowH, Theme.TextMuted);
            float x = Pad + LblW + 4f;
            float fw = (W - Pad * 2f - LblW - 4f - Gap) * 0.5f;
            InputField(x, y, fw, getA().ToString("0.###"), v => { float f; if (float.TryParse(v, out f)) set(f, getB()); });
            InputField(x + fw + Gap, y, fw, getB().ToString("0.###"), v => { float f; if (float.TryParse(v, out f)) set(getA(), f); });
            return y - (RowH + Gap);
        }

        // [on-toggle label] [min] [max] — a disableable random range / from-to pair
        internal float RandRow(float y, string label, Func<bool> getOn, Action<bool> setOn,
            Func<float> getA, Func<float> getB, Action<float, float> set)
        {
            RoundedRectGraphic bgRef = null;
            bgRef = Cell(label, Pad, y, LblW, RowH, () =>
            {
                setOn(!getOn());
                if (bgRef != null) bgRef.color = Tint(getOn());
            }, false, false, TextAnchor.MiddleLeft);
            bgRef.color = Tint(getOn());
            float x = Pad + LblW + 4f;
            float fw = (W - Pad * 2f - LblW - 4f - Gap) * 0.5f;
            InputField(x, y, fw, getA().ToString("0.###"), v => { float f; if (float.TryParse(v, out f)) set(f, getB()); });
            InputField(x + fw + Gap, y, fw, getB().ToString("0.###"), v => { float f; if (float.TryParse(v, out f)) set(getA(), f); });
            return y - (RowH + Gap);
        }

        // [on-toggle label] [value]
        internal float ToggleFieldRow(float y, string label, Func<bool> getOn, Action<bool> setOn,
            Func<float> get, Action<float> set)
        {
            RoundedRectGraphic bgRef = null;
            bgRef = Cell(label, Pad, y, LblW, RowH, () =>
            {
                setOn(!getOn());
                if (bgRef != null) bgRef.color = Tint(getOn());
            }, false, false, TextAnchor.MiddleLeft);
            bgRef.color = Tint(getOn());
            InputField(Pad + LblW + 4f, y, W - Pad * 2f - LblW - 4f, get().ToString("0.###"),
                v => { float f; if (float.TryParse(v, out f)) set(f); });
            return y - (RowH + Gap);
        }

        // full-width toggle cell
        internal float ToggleRow(float y, string label, bool value, Action<bool> set)
        {
            RoundedRectGraphic bgRef = null;
            bool cur = value;
            bgRef = Cell(label, Pad, y, W - Pad * 2f, RowH, () =>
            {
                cur = !cur;
                set(cur);
                if (bgRef != null) bgRef.color = Tint(cur);
            }, false, false, TextAnchor.MiddleLeft);
            bgRef.color = Tint(cur);
            return y - (RowH + Gap);
        }

        // [label] [opt0|opt1|…] — exclusive choice cells
        internal float SegRow(float y, string label, string[] options, Func<int> get, Action<int> set)
        {
            Label(label, Pad, y, LblW, RowH, Theme.TextMuted);
            float x = Pad + LblW + 4f;
            float w = (W - Pad * 2f - LblW - 4f - Gap * (options.Length - 1)) / options.Length;
            var cells = new RoundedRectGraphic[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                cells[i] = Cell(options[i], x, y, w, RowH, () =>
                {
                    set(idx);
                    for (int k = 0; k < cells.Length; k++) if (cells[k] != null) cells[k].color = Tint(get() == k);
                }, false);
                cells[i].color = Tint(get() == i);
                x += w + Gap;
            }
            return y - (RowH + Gap);
        }

        // [Tiles] [from] [to] [Sel] [All] — tile range with selection/whole-level fills
        internal float RangeRow(float y, Func<int> getA, Func<int> getB, Action<int, int> set,
            Action refresh, Action<string> status)
        {
            Label(Loc.T("Tiles"), Pad, y, 40f, RowH, Theme.TextMuted);
            float x = Pad + 44f;
            const float fw = 56f;
            InputField(x, y, fw, Mathf.Clamp(ResolveTile(getA()), 0, LevelLen()).ToString(), v =>
            { int n; if (int.TryParse(v, out n)) set(Math.Max(0, n), getB()); });
            x += fw + Gap;
            InputField(x, y, fw, Mathf.Clamp(ResolveTile(getB()), 0, LevelLen()).ToString(), v =>
            { int n; if (int.TryParse(v, out n)) set(getA(), Math.Max(0, n)); });
            x += fw + Gap;
            Cell(Loc.T("Sel"), x, y, 42f, RowH, () =>
            {
                if (SelectionRange(out int min, out int max)) { set(min, max); refresh(); }
                else status(Loc.T("Nothing selected"));
            }, true);
            x += 42f + Gap;
            Cell(Loc.T("All"), x, y, 42f, RowH, () => { set(0, -1); refresh(); }, true);
            return y - (RowH + Gap);
        }

        // [Ease] [name] — opens the shared curve-grid picker
        internal float EaseRow(float y, Func<DG.Tweening.Ease> get, Action<DG.Tweening.Ease> set)
        {
            Label(Loc.T("Ease"), Pad, y, LblW, RowH, Theme.TextMuted);
            float x = Pad + LblW + 4f;
            float w = W - Pad * 2f - LblW - 4f;
            RoundedRectGraphic nameBg = null;
            TextMeshProUGUI nameTmp = null;
            nameBg = Cell(get().ToString(), x, y, w, RowH, () =>
            {
                Vector2 pos = nameBg != null ? (Vector2)nameBg.transform.position : (Vector2)Input.mousePosition;
                EditorEasePicker.Open(Loc.T("Ease"), get(), pos, e =>
                {
                    set(e);
                    if (nameTmp != null) nameTmp.text = e.ToString();
                });
            }, true);
            nameTmp = nameBg.GetComponentInChildren<TextMeshProUGUI>();
            return y - (RowH + Gap);
        }

        private void EnsureCanvas()
        {
            if (CanvasGo != null) return;
            CanvasGo = new GameObject(_canvasName, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(CanvasGo);
            _canvas = CanvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = _sortingOrder;
            var scaler = CanvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _raycaster = CanvasGo.AddComponent<GraphicRaycaster>();
            if (Focusable && !_focusReg.Contains(this)) { _focusReg.Add(this); ReRankFocus(); }
        }
    }

    /* Drag handle for the dock dividers. Reports the pointer in DockRoot's top-left space
       (the same space the dock layout uses) back to PanelKit, which resizes the shared sidebar
       width (inner-edge divider) or retimes a stacked pair's height split (between-panel divider). */
    internal class DockDivider : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
    {
        private int _side, _index;
        private bool _width;
        private Canvas _canvas;

        internal void Set(int side, int index, bool width) { _side = side; _index = index; _width = width; }

        private void Awake() { _canvas = GetComponentInParent<Canvas>(); }

        public void OnBeginDrag(PointerEventData e) { PanelKit.DockDragActive = true; }
        public void OnEndDrag(PointerEventData e) { PanelKit.DockDragActive = false; }

        public void OnDrag(PointerEventData e)
        {
            var parent = transform.parent as RectTransform;
            if (parent == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, e.position, _canvas != null ? _canvas.worldCamera : null, out Vector2 local);
            PanelKit.OnDockDivider(_side, _index, _width, local);
        }
    }
}

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

        /* Shared TOOL-PALETTE geometry. Magic Shape, Track, Deco and the Hz tool were built at
           different times and drifted apart — 292 / 306 / 332 wide with 92 or 104 label columns —
           which is invisible alone and obvious the moment two are docked side by side and their
           label columns don't line up. One set of numbers, adopted by all four. */
        internal const float PaletteW = 320f, PaletteMinW = 240f, PaletteMinH = 180f;
        internal const float PaletteDefaultH = 400f, PaletteLblW = 104f;

        /* Trailing gap under a palette's commit button. Every one of them hand-wrote
           `y - (RowH + 2f) - 10f`; it lives here now so PrimaryRow and its callers agree. */
        private const float ActionH = RowH + 2f, ActionGap = 10f;

        private readonly string _canvasName;
        private readonly int _sortingOrder;
        /* LIVE template width every row helper lays out against. Not readonly: a panel can be
           resized by its width grips or stretched by the dock, and rows have to follow — a fixed
           W left the tool palettes drawing 292-wide rows inside a 700-wide window. */
        internal float W;
        private bool _wDirty;   // width moved; rebuild owed once the drag releases
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

        /* Saved geometry, for PanelLayout. Reading is only meaningful once built; writing before
           that stashes the rect until the first Rebuild (panels build lazily, on first show). */
        private Rect? _pendingRect;

        internal bool TryGetRect(out Rect rect)
        {
            rect = default(Rect);
            if (PanelGo == null) { if (!_pendingRect.HasValue) return false; rect = _pendingRect.Value; return true; }
            var r = (RectTransform)PanelGo.transform;
            var p = r.anchoredPosition; var sz = r.sizeDelta;
            rect = new Rect(p.x, p.y, sz.x, sz.y);
            return true;
        }

        internal void SetRect(Rect rect)
        {
            if (PanelGo == null) { _pendingRect = rect; return; }
            var r = (RectTransform)PanelGo.transform;
            r.anchoredPosition = new Vector2(rect.x, rect.y);
            if (rect.width > 40f && rect.height > 40f) { r.sizeDelta = new Vector2(rect.width, rect.height); W = rect.width; }
        }
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
        private RoundedRectGraphic _panelBg, _headBg;  // for square-corners-when-docked
        private GameObject _dockAppliedGo; private int _dockAppliedSide = -1; // dock-visual cache
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

        /* Screen-bottom clearance the LEFT sidebar must leave, in the same units as TickDocks'
           bottomInset. Bottom-left screen chrome (the pitch bar) publishes its own top here each
           frame so the dock ends above it instead of covering it; 0 when that chrome is hidden. */
        internal static float LeftBottomFloor;

        private static float BottomFor(int side, float bottomInset) =>
            side == 1 ? Mathf.Max(bottomInset, LeftBottomFloor) : bottomInset;

        /* Is any floating Sapphire window under the pointer right now? Overlays that draw at the
           cursor (the timeline's event tooltip) ask this so they don't print on top of a window
           the user is actually working in. Focusable panels only — that's every real window. */
        internal static bool AnyPanelHovered()
        {
            Vector2 m = Input.mousePosition;
            for (int i = 0; i < _focusReg.Count; i++)
            {
                var p = _focusReg[i];
                if (!p.Visible || p.PanelGo == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(
                        (RectTransform)p.PanelGo.transform, m, null)) return true;
            }
            return false;
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
        // DockGap 0: stacked docked panels butt FLUSH (no transparent seam bleeding the game
        // through). Their borders + the grip pill read as the divider, Photoshop-style.
        private const float SideMin = 220f, DockGap = 0f, DockMargin = 0f, DockPanelMinH = 120f, EdgeSnap = 28f;
        private const float DividerHit = 10f; // grab strip height on the flush seam (independent of DockGap)
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
            EnsureDockVisuals();
        }

        /* Docked panels sit FLUSH to the screen edge, so square their corners and drop the
           user-resize grips (the dock divider handles their size instead); floating panels get
           the rounded corners + grips back. Cached on (PanelGo, DockSide) so it only reworks the
           mesh / toggles on an actual change — a Rebuild (new PanelGo, re-added grips) re-applies. */
        private void EnsureDockVisuals()
        {
            if (_dockAppliedGo == PanelGo && _dockAppliedSide == DockSide) return;
            _dockAppliedGo = PanelGo; _dockAppliedSide = DockSide;
            bool docked = DockSide != 0;
            float rad = docked ? 0f : 10f;
            if (_panelBg != null && _panelBg.Radius != rad) _panelBg.Radius = rad;
            if (_headBg != null && _headBg.Radius != rad) _headBg.Radius = rad;
            if (PanelGo != null)
            {
                var t = PanelGo.transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    var ch = t.GetChild(i);
                    if (ch.name.StartsWith("Resize") && ch.gameObject.activeSelf == docked)
                        ch.gameObject.SetActive(!docked);
                }
            }
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
            float H = Mathf.Max(DockPanelMinH, ch - topMargin - BottomFor(side, bottomInset));
            float avail = H - (n - 1) * DockGap;
            float totW = 0f;
            for (int i = 0; i < n; i++) totW += _dockTmp[i]._dockWeight;
            if (totW <= 0.01f) totW = n;
            float x = side == 1 ? DockMargin : cw - sideW - DockMargin;
            float y = -topMargin;
            for (int i = 0; i < n; i++)
            {
                var p = _dockTmp[i];
                p.EnsureDockVisuals(); // re-square corners / re-hide grips after a Rebuild while docked
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
                    dr.sizeDelta = new Vector2(sideW, DividerHit);
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
            float H = Mathf.Max(DockPanelMinH, ch - topMargin - BottomFor(side, bottomInset));
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

        // Full teardown (UMM disable/unload): the dock chrome is a static not owned by any
        // module's Dispose, so it must be torn down explicitly or it survives on screen.
        internal static void DisposeDockChrome()
        {
            if (_dockChromeGo != null) UnityEngine.Object.Destroy(_dockChromeGo);
            _dockChromeGo = null; _dockRoot = null; _dockCanvas = null;
            _dropInd = null; _wDivL = null; _wDivR = null;
            _hDivPool.Clear(); _hDivUsed = 0;
            _dockL.Clear(); _dockR.Clear(); _focusReg.Clear();
            _sideWL = 360f; _sideWR = 360f; DockDragActive = false;
        }

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

        /* Rebuild REUSES PanelGo, clearing its children instead of destroying it. Destroying it
           took the resize handles (its children) down with it, so a rebuild triggered mid-drag —
           which is exactly what a live relayout-while-resizing needs — killed the drag on its
           first frame. Handles are named "Resize*" and are the one thing kept. */
        internal void Rebuild(string title, Action onClose, Vector2 defaultPos)
        {
            EnsureCanvas();
            Vector2 keepPos = defaultPos;
            bool fresh = PanelGo == null;
            if (fresh)
            {
                PanelGo = new GameObject("Panel", typeof(RectTransform));
                PanelGo.transform.SetParent(CanvasGo.transform, false);
            }
            else
            {
                keepPos = ((RectTransform)PanelGo.transform).anchoredPosition;
                var t = PanelGo.transform;
                for (int i = t.childCount - 1; i >= 0; i--)
                {
                    var ch = t.GetChild(i);
                    if (ch.name.StartsWith("Resize")) continue;
                    ch.SetParent(null, false);   // out of the hierarchy NOW; Destroy is deferred
                    UnityEngine.Object.Destroy(ch.gameObject);
                }
            }
            var r = (RectTransform)PanelGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = keepPos;
            if (fresh && DefaultH > 0f) r.sizeDelta = new Vector2(W, DefaultH);
            // A restored layout arrives before the panel is ever built, so it waits here.
            if (fresh && _pendingRect.HasValue)
            {
                var pr = _pendingRect.Value;
                r.anchoredPosition = new Vector2(pr.x, pr.y);
                if (pr.width > 40f && pr.height > 40f) r.sizeDelta = new Vector2(pr.width, pr.height);
                W = r.sizeDelta.x;
                _pendingRect = null;
            }
            var bg = PanelGo.GetComponent<RoundedRectGraphic>() ?? PanelGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 10f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.94f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;
            _panelBg = bg;
            _dockAppliedGo = null; // re-apply dock visuals (square corners / grips)
            _view = null; _content = null;

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
            _headBg = headBg;
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

            if (Scrollable) BuildViewport();
        }

        /* Scrolling body, for palettes the user sizes vertically: rows go in a clipped viewport
           and the panel keeps the height it was dragged to, instead of SetHeight resizing the
           window to fit the content on every rebuild. */
        internal bool Scrollable;
        internal float DefaultH;         // height for the FIRST build only; the drag owns it after
        private RectTransform _view, _content;
        private float _scroll;

        // Rows parent here, so the same row helpers serve scrolling and content-sized panels.
        private Transform RowParent => _content != null ? (Transform)_content : PanelGo.transform;

        private void BuildViewport()
        {
            var vpGo = new GameObject("View", typeof(RectTransform));
            vpGo.transform.SetParent(PanelGo.transform, false);
            _view = (RectTransform)vpGo.transform;
            _view.anchorMin = new Vector2(0f, 0f);
            _view.anchorMax = new Vector2(1f, 1f);
            _view.offsetMin = new Vector2(0f, 2f);
            _view.offsetMax = new Vector2(0f, -28f);   // clear the header
            vpGo.AddComponent<RectMask2D>();
            var img = vpGo.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.01f);
            img.raycastTarget = true;                  // wheel target

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vpGo.transform, false);
            _content = (RectTransform)cGo.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            /* Rows are laid out from the panel's top edge (y = -34 clears the header), but the
               viewport already starts below it — shift the content up by that much so the first
               row isn't pushed a header's worth further down. */
            _content.anchoredPosition = new Vector2(0f, 28f + _scroll);
        }

        internal void TickScroll()
        {
            if (_view == null || _content == null) return;
            float wheel = MainClass.WheelY;
            if (Mathf.Abs(wheel) < 0.01f) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_view, Input.mousePosition, null)) return;
            _scroll = Mathf.Clamp(_scroll + wheel * 60f, 0f, MaxScroll());
            _content.anchoredPosition = new Vector2(0f, 28f + _scroll);
        }

        private float MaxScroll() =>
            Mathf.Max(0f, _content.sizeDelta.y - 28f - _view.rect.height);

        /* Adopt the panel's real width as the layout width. True when it changed, which is the
           caller's cue to rebuild its rows (these palettes are built imperatively, so a resize
           only shows up on the next build). Reports DURING a drag too — Rebuild reuses PanelGo,
           so the handle survives its own relayout. */
        internal bool SyncWidth()
        {
            if (PanelGo == null) return false;
            float w = ((RectTransform)PanelGo.transform).sizeDelta.x;
            if (w >= 1f && Mathf.Abs(w - W) >= 0.5f) { W = w; _wDirty = true; }
            if (!_wDirty) return false;
            _wDirty = false;
            return true;
        }

        internal void SetHeight(float yEnd)
        {
            if (PanelGo == null) return;
            float need = -yEnd + Pad;
            if (_content != null)
            {
                _content.sizeDelta = new Vector2(0f, need);
                _scroll = Mathf.Clamp(_scroll, 0f, MaxScroll());
                _content.anchoredPosition = new Vector2(0f, 28f + _scroll);
                return;
            }
            ((RectTransform)PanelGo.transform).sizeDelta = new Vector2(W, need);
        }

        internal RoundedRectGraphic Cell(string text, float x, float y, float w, float h,
            Action onClick, bool button, bool accent = false, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Cell", typeof(RectTransform));
            go.transform.SetParent(RowParent, false);
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
            go.transform.SetParent(RowParent, false);
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
            go.transform.SetParent(RowParent, false);
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
            go.transform.SetParent(RowParent, false);
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

        /* Tab strip across the palette's width. `perRow` wraps (Track has six tabs in two rows
           of three); 0 means one row. Was copy-pasted three times with three slightly different
           width formulas, so a fourth palette got them subtly wrong by construction. */
        internal float TabRow(float y, string[] names, int current, Action<int> select, int perRow = 0)
        {
            if (names == null || names.Length == 0) return y;
            if (perRow <= 0) perRow = names.Length;
            float tabW = (W - Pad * 2f - Gap * (perRow - 1)) / perRow;
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var bg = Cell(names[i], Pad + (i % perRow) * (tabW + Gap),
                              y - (i / perRow) * (RowH + Gap), tabW, RowH,
                              () => select(idx), false);
                bg.color = Tint(current == i);
            }
            int rows = (names.Length + perRow - 1) / perRow;
            return y - (RowH + Gap) * rows - 4f;
        }

        // The panel's commit button: full width, accent, one per tab.
        internal float PrimaryRow(float y, string label, Action action)
        {
            Cell(label, Pad, y, W - Pad * 2f, ActionH, action, true, true);
            return y - ActionH - ActionGap;
        }

        // Commit plus a secondary action beside it (the Hz tool's Place / To angle pad).
        internal float PrimaryRow(float y, string primary, Action onPrimary, string secondary, Action onSecondary)
        {
            float w = (W - Pad * 2f - Gap) * 0.5f;
            Cell(primary, Pad, y, w, ActionH, onPrimary, true, true);
            Cell(secondary, Pad + w + Gap, y, w, ActionH, onSecondary, true);
            return y - ActionH - ActionGap;
        }

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
            => FieldRow(y, label, value.ToString(fmt), v => { float f; if (ExprEval.TryParseFloat(v, out f)) set(f); });

        internal float IntRow(float y, string label, int value, Action<int> set)
            => FieldRow(y, label, value.ToString(), v => { int n; if (ExprEval.TryParseInt(v, out n)) set(n); });

        // [label] [a] [b] — two int fields
        internal float PairRow(float y, string label, Func<int> getA, Func<int> getB, Action<int, int> set)
        {
            Label(label, Pad, y, LblW, RowH, Theme.TextMuted);
            float x = Pad + LblW + 4f;
            const float fw = 56f;
            InputField(x, y, fw, getA().ToString(), v => { int n; if (ExprEval.TryParseInt(v, out n)) set(n, getB()); });
            InputField(x + fw + Gap, y, fw, getB().ToString(), v => { int n; if (ExprEval.TryParseInt(v, out n)) set(getA(), n); });
            return y - (RowH + Gap);
        }

        // [label] [a] [b] — two float fields, no toggle
        internal float FloatPairRow(float y, string label, Func<float> getA, Func<float> getB, Action<float, float> set)
        {
            Label(label, Pad, y, LblW, RowH, Theme.TextMuted);
            float x = Pad + LblW + 4f;
            float fw = (W - Pad * 2f - LblW - 4f - Gap) * 0.5f;
            InputField(x, y, fw, getA().ToString("0.###"), v => { float f; if (ExprEval.TryParseFloat(v, out f)) set(f, getB()); });
            InputField(x + fw + Gap, y, fw, getB().ToString("0.###"), v => { float f; if (ExprEval.TryParseFloat(v, out f)) set(getA(), f); });
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
            InputField(x, y, fw, getA().ToString("0.###"), v => { float f; if (ExprEval.TryParseFloat(v, out f)) set(f, getB()); });
            InputField(x + fw + Gap, y, fw, getB().ToString("0.###"), v => { float f; if (ExprEval.TryParseFloat(v, out f)) set(getA(), f); });
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
                v => { float f; if (ExprEval.TryParseFloat(v, out f)) set(f); });
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
            { int n; if (ExprEval.TryParseInt(v, out n)) set(Math.Max(0, n), getB()); });
            x += fw + Gap;
            InputField(x, y, fw, Mathf.Clamp(ResolveTile(getB()), 0, LevelLen()).ToString(), v =>
            { int n; if (ExprEval.TryParseInt(v, out n)) set(getA(), Math.Max(0, n)); });
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

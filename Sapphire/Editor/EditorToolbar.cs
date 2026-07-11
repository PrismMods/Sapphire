using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Sapphire top tool toolbar (After-Effects style): a compact strip of editor tools
       docked top-centre. Tools so far:
         • Circular path — a magicshape/pseudo-circle generator (dialog form).
         • Free angle    — switches the game's floor buttons into arbitrary-angle input.
       Tiles are created through the game's OWN primitives (CreateFloorWithCharOrAngle /
       CreateArbitraryMidspin geometry) so undo, path rebuild and the .adofai format come
       for free. A whole generated run is wrapped in one SaveStateScope: the game's
       `changingState` counter nests, so the batch collapses to a single undo step. */
    internal static class EditorToolbar
    {
        private static GameObject _canvasGo;
        private static RectTransform _canvasRect;
        private static GameObject _barGo;
        private static GameObject _dialogGo;
        private static bool _freeAngleTool;
        private static RoundedRectGraphic _freeAngleCellBg;
        private static System.Reflection.FieldInfo _freeAngleModeField;
        // free-angle session: revert the live visual preview if the user exits without committing.
        private static bool _faActive;
        private static bool _faClicked;
        // pseudo tool click arbitration (see TickPseudoTool).
        private static int _selPrevFrame = -1;   // single selection at the end of last frame
        private static bool _pendingConvert;     // a click is waiting for the game to resolve it
        private static int _preClickSel = -1;    // selection just before the pending click
        private static Vector3 _clickWorld;      // world point of the pending click

        // Predicate the freeAngleMode transpiler calls in place of Input.GetMouseButton(1):
        // free-angle placement is driven by right-Alt (or the toolbar tool) when the tile
        // tools are enabled; otherwise it falls back to the real right button (vanilla).
        internal static bool FreeAngleActive()
        {
            try
            {
                var s = MainClass.Settings;
                if (s == null || !s.EditorTileActions) return Input.GetMouseButton(1);
                if (!(_freeAngleTool || Input.GetKey(KeyCode.LeftAlt))) return false;
                // While the cursor is over Sapphire UI, stand down — this frame-delays
                // freeAngleMode to false before a click on the UI can place a tile on exit.
                if (PointerOverSapphireUI()) return false;
                return true;
            }
            catch { return Input.GetMouseButton(1); }
        }

        // The arbitrary-angle sentinel char the editor itself passes for float floors
        // (GetAngleFromFloorCharDirectionWithCheck returns false for it, so the passed
        // angle is used verbatim). '!' (AngleMidspin) + angle 999 makes a midspin.
        private const char ArbitraryChar = (char)163;
        private const float MidspinAngle = 999f;

        // dialog state
        private static TMP_InputField _fPerRound, _fInterval, _fPseudoAngle; // star fields
        private static TMP_InputField _fDegrees, _fTileCount;                // circle fields
        private static TMP_InputField _fPseudoBatchInterval;                 // multi-tile pseudo
        private static int _pseudoBatchStyle;   // 0 = Upwards, 1 = Sideways, 2 = Flat
        private static int _pseudoSidewaysVariant; // 0 = up staircase, 1 = inline, 2 = downwards
        private static int _pseudoBatchTileCount;  // remembered so the dialog can rebuild in place
        private static string _pseudoBatchIntervalStr = "4"; // survives a rebuild
        private static long _lastMultiSig;       // dedups the auto-opened batch dialog
        private static bool _pseudoMode = true;  // Pseudo checkbox: true = star UI, false = circle
        private static bool _midspinPseudos;
        private static bool _keepBpm = true;
        private static bool _innerAngle;         // Reverse: pseudos point inward vs outward

        // pseudo tool
        private static bool _pseudoTool;
        private static int _pseudoN = 2;
        private static RoundedRectGraphic _pseudoCellBg;
        private static RoundedRectGraphic _cameraCellBg;   // camera-path overlay toggle (passive)
        private static GameObject _cameraMenuGo;           // camera submenu (Play all)
        private static GameObject _pseudoMenuGo;
        private static readonly int[] PseudoNumbers = { 2, 3, 4, 5, 6, 8, 10, 12, 16 };
        private static readonly string[] PseudoAngles = { "1", "15", "22.5", "30", "90" };
        private static readonly RoundedRectGraphic[] _pseudoBtnBgs = new RoundedRectGraphic[9];
        private static TMP_InputField _fPseudoN;         // custom key-count input (Keys row)
        private static double _pseudoTapAngle = 30.0;   // tap angle for a converted pseudo
        private static bool _pseudoMidspin;          // add a trailing midspin (reverse direction)
        private static RoundedRectGraphic _pseudoMidspinBg;
        private static TMPro.TextMeshProUGUI _pseudoCounterLbl;   // level midspin count
        private static TMP_InputField _fPseudoTap;               // custom tap-angle input
        private static readonly RoundedRectGraphic[] _pseudoAngleBtnBgs = new RoundedRectGraphic[5];
        private static TMP_InputField _fPseudoCustom;            // optional per-tile angles (space-sep, >2 keys)
        private static bool _pseudoCustomAngles;                 // custom-angle mode (swaps the angle row for the text field)
        private static RoundedRectGraphic _pseudoCustomBg;       // the "Custom" toggle highlight
        private static GameObject _pseudoCustomFieldGo;          // custom-angle text field (shown only in custom mode)
        private static readonly System.Collections.Generic.List<GameObject> _pseudoPresetObjs
            = new System.Collections.Generic.List<GameObject>();  // preset buttons + tap box (hidden in custom mode)

        // Selected-tool preview (top-right). Other modules (e.g. the event palette) set ExternalTool
        // to show their active tool here; the built-in tools take precedence when on.
        internal static string ExternalTool;
        private static GameObject _toolLabelGo;
        private static TextMeshProUGUI _toolLabelText;

        // Event palette tool: a LevelEventType (as int, −1 = none). RIGHT-click rapid-stamps the tile
        // under the cursor; LEFT-click uses the single-click pseudo guard (a click that moves the
        // selection just navigates; only a re-click on the already-selected tile stamps).
        private static int _eventTool = -1;
        private static bool _evtPending;
        private static int _evtPreSel = -1;    // selection before the pending left-click
        private static int _evtPrevSel = -1;   // selection at the end of the previous frame
        private static Vector3 _evtClickWorld;

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            bool want = false;
            try
            {
                ed = scnEditor.instance;
                want = ed != null && !ed.playMode && s != null && MainClass.EditorSuiteOn && s.EditorTopToolbar;
            }
            catch { }
            if (!want)
            {
                if (_canvasGo != null && _canvasGo.activeSelf) { _canvasGo.SetActive(false); CloseDialog(); }
                return;
            }
            if (_canvasGo == null) Build();
            if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);

            // ESC deselects an active tool (and closes the dialog).
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_dialogGo != null) CloseDialog();
                if (_freeAngleTool) DeactivateFreeAngle();
                if (_pseudoTool) DeactivatePseudo();
                if (_eventTool >= 0) ClearEventTool();
                if (_inspectorTool) DeactivateInspector();
            }

            TickFreeAngle(ed);
            if (_pseudoTool) TickPseudoTool(ed);
            if (_eventTool >= 0) TickEventTool(ed);
            if (_inspectorTool) TickInspectorTool(ed);
            TickToolHotkeys(ed);
            TickToolLabel();
        }

        // Free-angle preview is purely visual: while freeAngleMode is on the game only calls
        // FloorRenderer.SetAngle on the selected tile (+ next) and shifts startPos — angleData is
        // never touched, and nothing redraws when you leave the mode without committing. So on
        // exit (no left-click commit) we rebuild the path from the real data to drop the preview.
        private static void TickFreeAngle(scnEditor ed)
        {
            bool active = false;
            try
            {
                var s = MainClass.Settings;
                if (s != null && s.EditorTileActions
                    && (_freeAngleTool || Input.GetKey(KeyCode.LeftAlt))
                    && !PointerOverSapphireUI() && ed.SelectionIsSingle())
                    active = true;
            }
            catch { }

            if (active)
            {
                if (!_faActive) _faClicked = false;                 // rising edge
                if (Input.GetMouseButtonDown(0)) _faClicked = true; // committed a placement
            }
            else if (_faActive && !_faClicked)                      // exited without committing
            {
                try { ed.RemakePath(true, true); } catch { }
            }
            _faActive = active;
        }

        // Keyboard select (single-digit numbers) + left-click a tile to convert it.
        private static void TickPseudoTool(scnEditor ed)
        {
            for (int i = 0; i < PseudoNumbers.Length; i++)
            {
                int num = PseudoNumbers[i];
                if (num < 10 && Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + num)))
                { _pseudoN = num; SyncPseudoMenuHighlight(); }
            }
            int selCount = 0;
            try { selCount = ed.selectedFloors != null ? ed.selectedFloors.Count : 0; } catch { }

            if (selCount > 1)
            {
                // Multiple tiles → batch-pseudo dialog (interval + style). Open once per distinct
                // multi-selection so it doesn't reopen every frame or fight click-to-navigate.
                long sig = MultiSelSignature(ed);
                if (sig != _lastMultiSig && _dialogGo == null)
                {
                    _lastMultiSig = sig;
                    OpenPseudoBatchDialog(selCount);
                }
                _selPrevFrame = -1;
                _pendingConvert = false;
            }
            else
            {
                _lastMultiSig = 0;

                // Convert only the ALREADY-selected tile — a click that moves the selection to a
                // different tile just navigates. We can't tell which happened until the game has
                // processed the click, so a click is resolved on the NEXT frame: if the selection
                // is unchanged (and the click landed on that tile), it was a re-click → convert.
                int curSel = -1;
                try { if (ed.SelectionIsSingle()) curSel = ed.selectedFloors[0].seqID; } catch { }

                if (_pendingConvert)
                {
                    _pendingConvert = false;
                    if (curSel >= 0 && curSel == _preClickSel)
                    {
                        scrFloor tile = null;
                        try { tile = ed.selectedFloors[0]; } catch { }
                        if (tile != null && NearClick(tile, _clickWorld))
                            ConvertToPseudo(ed, tile, _pseudoN, _pseudoMidspin);
                    }
                }

                if (Input.GetMouseButtonDown(0) && _dialogGo == null && !PointerOverSapphireUI())
                {
                    _pendingConvert = true;
                    _preClickSel = _selPrevFrame;      // selection from before this click
                    _clickWorld = ClickWorld(ed);
                }
                _selPrevFrame = curSel;
            }

            // Live midspin count for the level (999s in angleData).
            if (_pseudoCounterLbl != null)
            {
                int spins = 0;
                try
                {
                    var ad = ed.levelData != null ? ed.levelData.angleData : null;
                    if (ad != null) for (int i = 0; i < ad.Count; i++) if (ad[i] == 999f) spins++;
                }
                catch { }
                _pseudoCounterLbl.text = "Total: " + spins;
            }
        }

        internal static void Dispose()
        {
            CloseDialog();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _canvasRect = null; _barGo = null; _dialogGo = null;
            _fPerRound = _fInterval = _fPseudoAngle = null; _freeAngleCellBg = null;
            _pseudoCellBg = null; _cameraCellBg = null; _cameraMenuGo = null; _cameraGapsBg = null; _pseudoMenuGo = null; _pseudoMidspinBg = null;
            _pseudoCounterLbl = null; _fPseudoTap = null; _fPseudoCustom = null;
            _pseudoCustomBg = null; _pseudoCustomFieldGo = null; _pseudoPresetObjs.Clear(); _fPseudoN = null;
            _toolLabelGo = null; _toolLabelText = null; _tipGo = null; _tipText = null;
            for (int i = 0; i < _pseudoAngleBtnBgs.Length; i++) _pseudoAngleBtnBgs[i] = null;
            for (int i = 0; i < _pseudoBtnBgs.Length; i++) _pseudoBtnBgs[i] = null;
        }

        // ── construction ────────────────────────────────────────────────────

        private static void Build()
        {
            _canvasGo = new GameObject("SapphireToolbar", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 906; // above chrome (905), below popups (907)
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
            _canvasRect = (RectTransform)_canvasGo.transform;

            const float cell = 32f, gap = 4f, pad = 5f;
            const int tools = 6;
            _barGo = new GameObject("ToolBar", typeof(RectTransform));
            _barGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_barGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -10f);
            r.sizeDelta = new Vector2(tools * (cell + gap) - gap + pad * 2f, cell + pad * 2f);
            var bg = _barGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 7f;   // flatter, Adobe-suite look
            bg.color = new Color(0.10f, 0.10f, 0.12f, 0.96f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.09f);
            bg.raycastTarget = true; // toolbar swallows clicks under it

            MakeToolCell(0, "Circular path", cell, pad, DrawCircleIcon, OpenDialog);
            _freeAngleCellBg = MakeToolCell(1, "Free angle", cell, pad, DrawAngleIcon, ToggleFreeAngle);
            _pseudoCellBg = MakeToolCell(2, "Pseudo", cell, pad, DrawPseudoIcon, TogglePseudo);
            _cameraCellBg = MakeToolCell(3, "Camera path", cell, pad, DrawCameraIcon, ToggleCameraPath);
            MakeToolCell(4, "VFX preview (ESC exits)", cell, pad, DrawEyeOffIcon, EditorVfxPreview.Toggle);
            _inspCellBg = MakeToolCell(5, "Inspector (copy tile events)", cell, pad, DrawDropperIcon, ToggleInspector);
            SyncInspectorHighlight();
            SyncFreeAngleHighlight();
            SyncPseudoHighlight();
            SyncCameraHighlight();
            BuildToolLabel();
            BuildToolTip();
            if (_pseudoTool) ShowPseudoMenu();
        }

        // With NO tile selected (and no tool owning digits), number keys pick the nth toolbar
        // tool. With a tile selected the digits belong to the event dock instead (EditorChrome).
        private static void TickToolHotkeys(scnEditor ed)
        {
            try
            {
                if (_pseudoTool) return; // its digits set the key count; every other tool can be
                                         // switched away directly (gating on AnyToolActive made
                                         // the digits "work once, then die")
                if (ed.selectedFloors != null && ed.selectedFloors.Count > 0) return;
                if (ed.userIsEditingAnInputField) return;
            }
            catch { return; }
            if (Input.GetKeyDown(KeyCode.Alpha1)) OpenDialog();
            else if (Input.GetKeyDown(KeyCode.Alpha2)) ToggleFreeAngle();
            else if (Input.GetKeyDown(KeyCode.Alpha3)) TogglePseudo();
            else if (Input.GetKeyDown(KeyCode.Alpha4)) ToggleCameraPath();
            else if (Input.GetKeyDown(KeyCode.Alpha5)) EditorVfxPreview.Toggle();
            else if (Input.GetKeyDown(KeyCode.Alpha6)) ToggleInspector();
        }

        // Small hover-hint text just below the toolbar (Adobe-style).
        private static GameObject _tipGo;
        private static TextMeshProUGUI _tipText;

        private static void BuildToolTip()
        {
            _tipGo = new GameObject("ToolTip", typeof(RectTransform));
            _tipGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_tipGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -64f); // just under the bar
            r.sizeDelta = new Vector2(320f, 16f);
            _tipText = UIBuilder.Tmp(_tipGo, "", 11.5f, TextAnchor.MiddleCenter, Theme.TextMuted);
            _tipText.raycastTarget = false;
            _tipGo.SetActive(false);
        }

        private static void ShowToolTip(string tip)
        {
            if (_tipGo == null || _tipText == null) return;
            _tipText.text = tip;
            if (!_tipGo.activeSelf) _tipGo.SetActive(true);
        }

        private static void HideToolTip()
        {
            if (_tipGo != null && _tipGo.activeSelf) _tipGo.SetActive(false);
        }

        // Top-right "selected tool" chip. Hidden when no tool is active.
        private static void BuildToolLabel()
        {
            _toolLabelGo = new GameObject("ToolLabel", typeof(RectTransform));
            _toolLabelGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_toolLabelGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(1f, 1f);
            r.anchoredPosition = new Vector2(-66f, -12f); // corner belongs to the master switch
            r.sizeDelta = new Vector2(150f, 30f);
            var bg = _toolLabelGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 8f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.9f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.5f);
            bg.raycastTarget = false;
            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(_toolLabelGo.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(10f, 0f); tr.offsetMax = new Vector2(-10f, 0f);
            _toolLabelText = UIBuilder.Tmp(txtGo, "", 13f, TextAnchor.MiddleCenter, Theme.Text);
            BuildHelpButton();
            _toolLabelGo.SetActive(false);
        }

        // "?" button, top-right corner beside the tool label → opens the instruction manual
        // (a placeholder popup for now; the real manual content comes later).
        private static void BuildHelpButton()
        {
            // Sits just LEFT of the tool label (a child, so it shows/hides + moves with it).
            var go = new GameObject("Help", typeof(RectTransform));
            go.transform.SetParent(_toolLabelGo.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(1f, 0.5f);
            r.anchoredPosition = new Vector2(-6f, 0f);
            r.sizeDelta = new Vector2(24f, 24f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 12f;
            bg.color = new Color(0.10f, 0.10f, 0.12f, 0.92f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;
            var glyphGo = new GameObject("G", typeof(RectTransform));
            glyphGo.transform.SetParent(go.transform, false);
            var gr = (RectTransform)glyphGo.transform;
            gr.anchorMin = Vector2.zero; gr.anchorMax = Vector2.one;
            gr.offsetMin = Vector2.zero; gr.offsetMax = Vector2.zero;
            var g = UIBuilder.Tmp(glyphGo, "?", 13f, TextAnchor.MiddleCenter, Theme.Text);
            g.raycastTarget = false;
            var hov = go.AddComponent<CellHover>();
            hov.Bg = bg; hov.Base = bg.color; hov.Tip = "Instruction manual";
            UI.ClickHandler.Attach(go, EditorHelp.Toggle);
        }

        // Reflect the active tool (built-in tools win; else whatever ExternalTool a palette set).
        private static void TickToolLabel()
        {
            if (_toolLabelGo == null) return;
            string tool = _pseudoTool ? "Pseudo · " + _pseudoN + "k"
                        : _freeAngleTool ? "Free angle"
                        : _inspectorTool ? (_inspEvents.Count > 0
                            ? "Inspector · " + _inspEvents.Count + " ev" : "Inspector · pick a tile")
                        : ExternalTool;
            bool show = !string.IsNullOrEmpty(tool);
            if (_toolLabelGo.activeSelf != show) _toolLabelGo.SetActive(show);
            if (show && _toolLabelText != null && _toolLabelText.text != tool)
            {
                _toolLabelText.text = tool;
                float w = Mathf.Max(90f, _toolLabelText.preferredWidth + 24f);
                ((RectTransform)_toolLabelGo.transform).sizeDelta = new Vector2(w, 30f);
            }
        }

        private static RoundedRectGraphic MakeToolCell(int index, string tip, float cell, float pad,
            Action<GameObject> drawIcon, Action onClick)
        {
            var go = new GameObject("Tool" + index, typeof(RectTransform));
            go.transform.SetParent(_barGo.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(pad + index * (cell + 4f), 0f);
            r.sizeDelta = new Vector2(cell, cell);
            var bgc = go.AddComponent<RoundedRectGraphic>();
            bgc.Radius = 5f;
            bgc.color = new Color(1f, 1f, 1f, 0.05f);
            bgc.raycastTarget = true;
            var hover = go.AddComponent<CellHover>();
            hover.Bg = bgc;
            hover.Base = bgc.color;
            hover.Tip = tip;
            drawIcon(go);
            UI.ClickHandler.Attach(go, onClick);
            return bgc;
        }

        private static readonly Color IconCol = new Color(0.82f, 0.82f, 0.86f, 1f);

        private static void MakeDot(GameObject parent, Vector2 pos, float size)
        {
            var g = new GameObject("Dot", typeof(RectTransform));
            g.transform.SetParent(parent.transform, false);
            var rr = (RectTransform)g.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f);
            rr.pivot = new Vector2(0.5f, 0.5f);
            rr.anchoredPosition = pos;
            rr.sizeDelta = new Vector2(size, size);
            var dot = g.AddComponent<RoundedRectGraphic>();
            dot.Radius = size * 0.5f;
            dot.color = IconCol;
            dot.raycastTarget = false;
        }

        // Circular path: a faint ring with tile-dots sitting ON it — a circle of tiles.
        private static void DrawCircleIcon(GameObject cell)
        {
            var g = new GameObject("Ring", typeof(RectTransform));
            g.transform.SetParent(cell.transform, false);
            var rr = (RectTransform)g.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f);
            rr.pivot = new Vector2(0.5f, 0.5f);
            rr.sizeDelta = new Vector2(16f, 16f);
            var ring = g.AddComponent<RoundedRectGraphic>();
            ring.Radius = 8f;
            ring.color = new Color(0f, 0f, 0f, 0f);
            ring.BorderWidth = 1.4f;
            ring.BorderColor = new Color(IconCol.r, IconCol.g, IconCol.b, 0.5f);
            ring.raycastTarget = false;
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                MakeDot(cell, new Vector2(Mathf.Sin(a) * 8f, Mathf.Cos(a) * 8f), 4f);
            }
        }

        // Free angle: a plain "∠".
        private static void DrawAngleIcon(GameObject cell)
        {
            MakeBar(cell, new Vector2(0f, -6f), new Vector2(18f, 2.4f), 0f);
            MakeBar(cell, new Vector2(-2.5f, 0.5f), new Vector2(16f, 2.4f), 38f);
        }

        // Camera icon: body outline + lens dot + top bump.
        private static void DrawCameraIcon(GameObject cell)
        {
            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(cell.transform, false);
            var br = (RectTransform)bodyGo.transform;
            br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f);
            br.pivot = new Vector2(0.5f, 0.5f);
            br.anchoredPosition = new Vector2(0f, -1.5f);
            br.sizeDelta = new Vector2(18f, 13f);
            var body = bodyGo.AddComponent<RoundedRectGraphic>();
            body.Radius = 3f;
            body.color = new Color(0f, 0f, 0f, 0f);
            body.BorderWidth = 1.8f;
            body.BorderColor = new Color(0.82f, 0.82f, 0.86f, 1f);
            body.raycastTarget = false;
            MakeBar(cell, new Vector2(-3f, 6.5f), new Vector2(7f, 3f), 0f);      // top bump
            var lensGo = new GameObject("Lens", typeof(RectTransform));
            lensGo.transform.SetParent(cell.transform, false);
            var lr = (RectTransform)lensGo.transform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f);
            lr.pivot = new Vector2(0.5f, 0.5f);
            lr.anchoredPosition = new Vector2(0f, -1.5f);
            lr.sizeDelta = new Vector2(6.5f, 6.5f);
            var lens = lensGo.AddComponent<RoundedRectGraphic>();
            lens.Radius = 3.25f;
            lens.color = new Color(0f, 0f, 0f, 0f);
            lens.BorderWidth = 1.8f;
            lens.BorderColor = IconCol;
            lens.raycastTarget = false;
        }

        // Crossed-out eye: capsule outline + pupil + a slash across.
        private static void DrawEyeOffIcon(GameObject cellGo)
        {
            var eyeGo = new GameObject("Eye", typeof(RectTransform));
            eyeGo.transform.SetParent(cellGo.transform, false);
            var er = (RectTransform)eyeGo.transform;
            er.anchorMin = er.anchorMax = new Vector2(0.5f, 0.5f);
            er.pivot = new Vector2(0.5f, 0.5f);
            er.sizeDelta = new Vector2(17f, 10f);
            var eye = eyeGo.AddComponent<RoundedRectGraphic>();
            eye.Radius = 5f;
            eye.color = new Color(0f, 0f, 0f, 0f);
            eye.BorderWidth = 1.8f;
            eye.BorderColor = new Color(0.82f, 0.82f, 0.86f, 1f);
            eye.raycastTarget = false;
            var pupilGo = new GameObject("Pupil", typeof(RectTransform));
            pupilGo.transform.SetParent(cellGo.transform, false);
            var pr = (RectTransform)pupilGo.transform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.pivot = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(4f, 4f);
            var pupil = pupilGo.AddComponent<RoundedRectGraphic>();
            pupil.Radius = 2f;
            pupil.color = new Color(0.82f, 0.82f, 0.86f, 1f);
            pupil.raycastTarget = false;
            MakeBar(cellGo, Vector2.zero, new Vector2(21f, 2.8f), 45f); // the cross-out
        }

        // Passive overlay toggle — doesn't own clicks, so it doesn't deactivate the other tools.
        private static void ToggleCameraPath()
        {
            EditorCameraPath.Toggle();
            SyncCameraHighlight();
            if (EditorCameraPath.IsOn) ShowCameraMenu();
            else if (_cameraMenuGo != null) _cameraMenuGo.SetActive(false);
        }

        // Camera submenu: sits beside the toolbar (the pseudo submenu owns the space below it).
        // ▶ Play all runs the whole keyframe sequence; "Gaps" makes it wait out the real beat
        // gaps between events (time-accurate) instead of playing back-to-back.
        private static RoundedRectGraphic _cameraGapsBg;

        private static void ShowCameraMenu()
        {
            if (_cameraMenuGo != null) { _cameraMenuGo.SetActive(true); SyncCameraGaps(); return; }
            const float pad = 7f, bh = 34f, gap = 5f, playW = 84f, selW = 62f, gapsW = 56f;
            _cameraMenuGo = new GameObject("CameraMenu", typeof(RectTransform));
            _cameraMenuGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_cameraMenuGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(120f, -10f); // clear of the 6-cell bar's right edge
            r.sizeDelta = new Vector2(pad * 2f + playW + gap + selW + gap + gapsW, bh + pad * 2f);
            var bg = _cameraMenuGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 12f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.9f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;

            MakeCameraMenuBtn("PlayAll", "▶ Play all", pad, playW, bh, EditorCameraPath.PlayAll);
            MakeCameraMenuBtn("PlaySel", "▶ Sel", pad + playW + gap, selW, bh,
                EditorCameraPath.PlayAllFromSelection);
            _cameraGapsBg = MakeCameraMenuBtn("Gaps", "Gaps", pad + playW + gap + selW + gap, gapsW, bh,
                () => { EditorCameraPath.UseGaps = !EditorCameraPath.UseGaps; SyncCameraGaps(); });
            SyncCameraGaps();
        }

        private static RoundedRectGraphic MakeCameraMenuBtn(string name, string text, float x,
            float w, float h, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_cameraMenuGo.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 0.5f); r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f);
            r.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 8f;
            bg.color = new Color(1f, 1f, 1f, 0.06f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.1f);
            bg.raycastTarget = true;
            var lblGo = new GameObject("L", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;
            var lbl = UIBuilder.Tmp(lblGo, text, 13f, TextAnchor.MiddleCenter, Theme.Text);
            lbl.raycastTarget = false;
            UI.ClickHandler.Attach(go, onClick);
            return bg;
        }

        private static void SyncCameraGaps()
        {
            if (_cameraGapsBg == null) return;
            _cameraGapsBg.color = EditorCameraPath.UseGaps
                ? new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.45f)
                : new Color(1f, 1f, 1f, 0.06f);
        }

        private static void SyncCameraHighlight()
        {
            if (_cameraCellBg == null) return;
            var rest = EditorCameraPath.IsOn
                ? new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.45f)
                : new Color(1f, 1f, 1f, 0.05f);
            _cameraCellBg.color = rest;
            var hover = _cameraCellBg.GetComponent<CellHover>();
            if (hover != null) hover.Base = rest;
        }

        private static void MakeBar(GameObject parent, Vector2 pos, Vector2 size, float rot)
        {
            var g = new GameObject("Bar", typeof(RectTransform));
            g.transform.SetParent(parent.transform, false);
            var r = (RectTransform)g.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            r.localRotation = Quaternion.Euler(0f, 0f, rot);
            var bar = g.AddComponent<RoundedRectGraphic>();
            bar.Radius = 1.25f;
            bar.color = new Color(0.82f, 0.82f, 0.86f, 1f);
            bar.raycastTarget = false;
        }

        // ── free-angle tool ─────────────────────────────────────────────────
        // Toggles persistent free-angle mode (equivalent to holding right-Alt): the
        // selected tile follows the mouse and left-click places it — the game's own
        // freeAngleMode, which the transpiler now gates on FreeAngleActive(). Needs the tile
        // tools enabled; without them the transpiler predicate falls back to right-mouse.
        private static void ToggleFreeAngle()
        {
            var s = MainClass.Settings;
            if (s == null || !s.EditorTileActions)
            { SapphireLog.Log("Toolbar: enable 'Right-click tile menu' to use the free-angle tool"); return; }
            if (_freeAngleTool) DeactivateFreeAngle();
            else { DeactivatePseudo(); ClearEventTool(); _freeAngleTool = true; SyncFreeAngleHighlight(); } // one tool at a time
        }

        private static void DeactivateFreeAngle()
        {
            _freeAngleTool = false;
            SyncFreeAngleHighlight();
            ForceFreeAngleModeOff(); // so the exit click doesn't place a tile
        }

        // Clear the game's freeAngleMode immediately (runs in the UI click handler, before the
        // editor's HandleMouseActions), so deselecting the tool doesn't drop a tile on the click.
        private static void ForceFreeAngleModeOff()
        {
            try
            {
                var ed = scnEditor.instance;
                if (ed == null) return;
                if (_freeAngleModeField == null)
                    _freeAngleModeField = typeof(scnEditor).GetField("freeAngleMode",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                _freeAngleModeField?.SetValue(ed, false);
            }
            catch { }
        }

        private static void SyncFreeAngleHighlight()
        {
            if (_freeAngleCellBg == null) return;
            var rest = _freeAngleTool
                ? new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.45f)
                : new Color(1f, 1f, 1f, 0.05f);
            _freeAngleCellBg.color = rest;
            var hover = _freeAngleCellBg.GetComponent<CellHover>();
            if (hover != null) hover.Base = rest;
        }

        // ── pseudo tool ─────────────────────────────────────────────────────
        // Select the tool, then left-click a tile to convert it into an N-hit pseudo (N
        // chosen in the submenu or with the matching number key). N in {2,3,4,5,6,8,10,12,16}.
        // Pseudo: two overlapping diamond tiles (a 2-key pseudo pair).
        private static void DrawPseudoIcon(GameObject cell)
        {
            MakeTile(cell, new Vector2(-4.5f, -2.5f), 9.5f, false);
            MakeTile(cell, new Vector2(4.5f, 2.5f), 9.5f, true);
        }

        private static void MakeTile(GameObject parent, Vector2 pos, float size, bool filled)
        {
            var g = new GameObject("Tile", typeof(RectTransform));
            g.transform.SetParent(parent.transform, false);
            var rr = (RectTransform)g.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f);
            rr.pivot = new Vector2(0.5f, 0.5f);
            rr.anchoredPosition = pos;
            rr.sizeDelta = new Vector2(size, size);
            rr.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var t = g.AddComponent<RoundedRectGraphic>();
            t.Radius = 2f;
            if (filled) { t.color = IconCol; }
            else
            {
                t.color = new Color(0f, 0f, 0f, 0f);
                t.BorderWidth = 1.8f;
                t.BorderColor = IconCol;
            }
            t.raycastTarget = false;
        }

        private static void TogglePseudo()
        {
            if (_pseudoTool) DeactivatePseudo();
            else { DeactivateFreeAngle(); ClearEventTool(); _pseudoTool = true; SyncPseudoHighlight(); ShowPseudoMenu(); } // one tool at a time
        }

        private static void DeactivatePseudo()
        {
            if (!_pseudoTool) return;
            _pseudoTool = false;
            SyncPseudoHighlight();
            HidePseudoMenu();
        }

        // ── inspector tool: copy one tile's events, paste onto others ───────
        // LEFT re-click on the selected tile = CAPTURE its events (LevelEvent.Copy, public);
        // RIGHT-click any tile = PASTE the captured set, filtered by the copy panel's checked
        // types (the panel doubles as the paste filter while this tool is active).
        private static bool _inspectorTool;
        private static RoundedRectGraphic _inspCellBg;
        private static readonly System.Collections.Generic.List<ADOFAI.LevelEvent> _inspEvents
            = new System.Collections.Generic.List<ADOFAI.LevelEvent>();
        private static int _inspVersion;
        private static bool _inspPending;
        private static int _inspPreSel = -1, _inspPrevSel = -1;
        private static Vector3 _inspClickWorld;

        internal static bool InspectorActive => _inspectorTool;
        internal static bool PseudoToolOn => _pseudoTool;   // its digit shortcuts win over others
        internal static int InspectorVersion => _inspVersion;

        internal static System.Collections.Generic.List<int> InspectorTypes()
        {
            var set = new System.Collections.Generic.SortedSet<int>();
            foreach (var e in _inspEvents) if (e != null) set.Add((int)e.eventType);
            return new System.Collections.Generic.List<int>(set);
        }

        private static void ToggleInspector()
        {
            if (_inspectorTool) { DeactivateInspector(); return; }
            DeactivatePseudo(); DeactivateFreeAngle(); ClearEventTool();
            _inspectorTool = true;
            _inspPending = false; _inspPrevSel = -1;
            SyncInspectorHighlight();
        }

        private static void DeactivateInspector()
        {
            _inspectorTool = false;
            _inspPending = false;
            SyncInspectorHighlight();
        }

        private static void SyncInspectorHighlight()
        {
            if (_inspCellBg == null) return;
            var rest = _inspectorTool
                ? new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.45f)
                : new Color(1f, 1f, 1f, 0.05f);
            _inspCellBg.color = rest;
            var hover = _inspCellBg.GetComponent<CellHover>();
            if (hover != null) hover.Base = rest;
        }

        private static void TickInspectorTool(scnEditor ed)
        {
            bool gate = _dialogGo == null && !PointerOverSapphireUI();

            // Right-click: paste onto the tile under the cursor.
            if (gate && Input.GetMouseButtonDown(1))
            {
                var floor = FloorUnderCursor(ed);
                if (floor != null) PasteInspector(ed, floor);
            }

            // Left-click with the two-click guard: re-click the selected tile = capture.
            int curSel = -1;
            try { if (ed.SelectionIsSingle()) curSel = ed.selectedFloors[0].seqID; } catch { }
            if (_inspPending)
            {
                _inspPending = false;
                if (curSel >= 0 && curSel == _inspPreSel)
                {
                    scrFloor tile = null;
                    try { tile = ed.selectedFloors[0]; } catch { }
                    if (tile != null && NearClick(tile, _inspClickWorld)) CaptureInspector(ed, tile);
                }
            }
            if (gate && Input.GetMouseButtonDown(0))
            {
                _inspPending = true;
                _inspPreSel = _inspPrevSel;
                _inspClickWorld = ClickWorld(ed);
            }
            _inspPrevSel = curSel;
        }

        private static void CaptureInspector(scnEditor ed, scrFloor tile)
        {
            _inspEvents.Clear();
            try
            {
                foreach (var e in ed.events)
                    if (e != null && e.floor == tile.seqID) _inspEvents.Add(e.Copy());
            }
            catch (Exception ex) { SapphireLog.Log("Inspector: capture failed: " + ex.Message); }
            _inspVersion++;
            SapphireLog.Log("Inspector: captured " + _inspEvents.Count + " events from floor " + tile.seqID);
        }

        private static void PasteInspector(scnEditor ed, scrFloor floor)
        {
            if (_inspEvents.Count == 0) return;
            try
            {
                using (new SaveStateScope(ed))
                {
                    int n = 0;
                    foreach (var e in _inspEvents)
                    {
                        if (e == null || !EditorCopyPanel.TypeChecked((int)e.eventType)) continue;
                        var c = e.Copy();
                        c.floor = floor.seqID;
                        ed.events.Add(c);
                        n++;
                    }
                    if (n > 0)
                    {
                        try { ed.ApplyEventsToFloors(); } catch { }
                        try { ed.RemakePath(true, true); } catch { }
                    }
                }
            }
            catch (Exception ex) { SapphireLog.Log("Inspector: paste failed: " + ex.Message); }
        }

        // Eyedropper: small bulb, short barrel, long thin needle.
        private static void DrawDropperIcon(GameObject cellGo)
        {
            MakeDot(cellGo, new Vector2(6.5f, 6.5f), 5f);                        // bulb
            MakeBar(cellGo, new Vector2(3.5f, 3.5f), new Vector2(6.5f, 3.6f), 45f);  // barrel
            MakeBar(cellGo, new Vector2(-2.5f, -2.5f), new Vector2(10f, 1.7f), 45f); // needle
        }

        // ── event palette tool ──────────────────────────────────────────────
        internal static int EventTool => _eventTool;
        // True while any editor tool owns the click (so passive overlays like the copy panel yield).
        internal static bool AnyToolActive => _pseudoTool || _freeAngleTool || _eventTool >= 0 || _inspectorTool;
        internal static bool DialogOpen => _dialogGo != null;

        // The event dock (EditorChrome) calls this when a palette event is picked: it becomes the
        // active tool and keeps inserting on each tile you click until another tool/ESC.
        internal static void SelectEventTool(int eventType, string name)
        {
            DeactivatePseudo();
            DeactivateFreeAngle();
            DeactivateInspector();
            _eventTool = eventType;
            ExternalTool = name;
            _evtPending = false;
        }

        internal static void ClearEventTool()
        {
            if (_eventTool < 0) return;
            _eventTool = -1;
            if (ExternalTool != null) ExternalTool = null;
        }

        private static void TickEventTool(scnEditor ed)
        {
            bool gate = _dialogGo == null && !PointerOverSapphireUI();

            // Right-click: rapid stamp on the tile under the cursor.
            if (gate && Input.GetMouseButtonDown(1))
            {
                scrFloor floor = FloorUnderCursor(ed);
                if (floor != null) StampEvent(ed, floor);
            }

            // Left-click: same guard as the single-click pseudo — a click that MOVES the selection to
            // another tile just navigates; only a re-click on the already-selected tile stamps. We
            // can't tell which until the game has processed the click, so it resolves one frame late.
            int curSel = -1;
            try { if (ed.SelectionIsSingle()) curSel = ed.selectedFloors[0].seqID; } catch { }
            if (_evtPending)
            {
                _evtPending = false;
                if (curSel >= 0 && curSel == _evtPreSel)
                {
                    scrFloor tile = null;
                    try { tile = ed.selectedFloors[0]; } catch { }
                    if (tile != null && NearClick(tile, _evtClickWorld)) StampEvent(ed, tile);
                }
            }
            if (gate && Input.GetMouseButtonDown(0))
            {
                _evtPending = true;
                _evtPreSel = _evtPrevSel;
                _evtClickWorld = ClickWorld(ed);
            }
            _evtPrevSel = curSel;
        }

        // Select the tile, add the event (game's own AddEventAtSelected applies it + draws its
        // indicator), then deselect so the inspector doesn't linger. One undo step.
        private static void StampEvent(scnEditor ed, scrFloor floor)
        {
            try
            {
                using (new SaveStateScope(ed))
                {
                    ed.DeselectFloors();
                    ed.SelectFloor(floor, false);
                    ed.AddEventAtSelected((ADOFAI.LevelEventType)_eventTool);
                    ed.DeselectFloors();
                }
            }
            catch (Exception ex) { SapphireLog.Log("Event tool: place failed: " + ex.Message); }
        }

        // Enter-key placement: stamp the selected event tool onto the currently selected tile.
        internal static void StampOnSelectedTile()
        {
            if (_eventTool < 0) return;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;
            scrFloor tile = null;
            try { if (ed.SelectionIsSingle()) tile = ed.selectedFloors[0]; } catch { }
            if (tile != null) StampEvent(ed, tile);
        }

        // Nearest floor to the cursor within ~a tile radius. GetFloorAtPosition (Physics2D) doesn't
        // hit editor tiles here, so match the pseudo tool's proven world-distance approach.
        private static scrFloor FloorUnderCursor(scnEditor ed)
        {
            Vector2 w = ClickWorld(ed);
            scrFloor best = null;
            float bestD = 0.7f;
            try
            {
                var floors = ed.floors;
                if (floors != null)
                    for (int i = 0; i < floors.Count; i++)
                    {
                        var f = floors[i];
                        if (f == null) continue;
                        float d = Vector2.Distance((Vector2)f.transform.position, w);
                        if (d < bestD) { bestD = d; best = f; }
                    }
            }
            catch { }
            return best;
        }

        private static void SyncPseudoHighlight()
        {
            if (_pseudoCellBg == null) return;
            var rest = _pseudoTool
                ? new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.45f)
                : new Color(1f, 1f, 1f, 0.05f);
            _pseudoCellBg.color = rest;
            var hover = _pseudoCellBg.GetComponent<CellHover>();
            if (hover != null) hover.Base = rest;
        }

        private static void ShowPseudoMenu()
        {
            if (_pseudoMenuGo != null) { _pseudoMenuGo.SetActive(true); SyncPseudoMenuHighlight(); SyncPseudoMidspin(); return; }
            const float bw = 30f, bgap = 5f, bpad = 7f, bh = 28f, rowGap = 6f;
            const float labW = 42f, mspinW = 62f, cntW = 64f, presetW = 46f, presetGap = 5f;
            const float custTogW = 66f, tapW = 56f, nBoxW = 44f;
            int nNum = PseudoNumbers.Length, nAng = PseudoAngles.Length;

            float numX = bpad + labW;                          // buttons start after the row label
            float numRowW = nNum * (bw + bgap) - bgap;
            float nBoxX = numX + numRowW + 8f;                 // custom key-count field after the buttons
            float mspinX = nBoxX + nBoxW + 8f;
            float cntX = mspinX + mspinW + 8f;
            float row1W = cntX + cntW + bpad;
            float presetRowW = nAng * (presetW + presetGap) - presetGap;
            float tbX = numX + presetRowW + 8f;   // tap-angle textbox after the presets
            float custTogX = tbX + tapW + 8f;     // Custom toggle sits next to the tap/custom field
            float row2W = custTogX + custTogW + bpad;

            float width = Mathf.Max(row1W, row2W);
            float height = bpad + bh + rowGap + bh + bpad;   // 2 rows (angle row swaps for custom field)
            float row1Y = -bpad, row2Y = -bpad - bh - rowGap;
            float angX = numX;                    // angle content aligns under the key buttons

            _pseudoMenuGo = new GameObject("PseudoMenu", typeof(RectTransform));
            _pseudoMenuGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_pseudoMenuGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -64f); // below the tool bar
            r.sizeDelta = new Vector2(width, height);
            var bg = _pseudoMenuGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 10f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.92f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;

            // Row 1: "keys" label + key-count buttons + custom-N field + midspin toggle + counter.
            MakeRowLabel("Keys", bpad, row1Y, labW, bh);
            for (int i = 0; i < nNum; i++)
            {
                int num = PseudoNumbers[i];
                _pseudoBtnBgs[i] = MakeMiniBtn("N" + num, numX + i * (bw + bgap), row1Y, bw, bh,
                    num.ToString(), () => { _pseudoN = num; SyncPseudoMenuHighlight(); });
            }
            _fPseudoN = MakeMiniField("PseudoN", nBoxX, row1Y, nBoxW, bh, _pseudoN.ToString(),
                TMP_InputField.ContentType.IntegerNumber, t =>
                {
                    if (int.TryParse(t, out var v) && v >= 2) { _pseudoN = v; SyncPseudoMenuHighlight(); }
                });
            _pseudoMidspinBg = MakeMiniBtn("Midspin", mspinX, row1Y, mspinW, bh, "Midspin",
                () => { _pseudoMidspin = !_pseudoMidspin; SyncPseudoMidspin(); });
            SyncPseudoMidspin();
            _pseudoCounterLbl = MakeInlineLabel("Counter", cntX, row1Y, cntW, bh, "spins: 0");

            // Row 2: "Angle" label + preset buttons + tap box. The Custom toggle sits at the right,
            // next to the field, and swaps the presets/tap box for the per-tile custom field.
            MakeRowLabel("Angle", bpad, row2Y, labW, bh);
            _pseudoPresetObjs.Clear();
            for (int i = 0; i < nAng; i++)
            {
                string ang = PseudoAngles[i];
                _pseudoAngleBtnBgs[i] = MakeMiniBtn("A" + ang, angX + i * (presetW + presetGap), row2Y, presetW, bh,
                    ang, () => { if (_fPseudoTap != null) _fPseudoTap.text = ang; });
                _pseudoPresetObjs.Add(_pseudoAngleBtnBgs[i].gameObject);
            }
            // tap-angle textbox (drives _pseudoTapAngle; presets fill it)
            _fPseudoTap = MakeMiniField("TapAngle", tbX, row2Y, tapW, bh,
                _pseudoTapAngle.ToString("0.##", CultureInfo.InvariantCulture),
                TMP_InputField.ContentType.DecimalNumber, t =>
                {
                    if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        _pseudoTapAngle = v;
                    SyncPseudoAngle();
                });
            _pseudoPresetObjs.Add(_fPseudoTap.gameObject);
            SyncPseudoAngle();

            _pseudoCustomBg = MakeMiniBtn("Custom", custTogX, row2Y, custTogW, bh, "Custom",
                () => { _pseudoCustomAngles = !_pseudoCustomAngles; SyncPseudoCustomMode(); });

            // Custom-mode field: per-tile angles (space-separated), one per tap — e.g. "30 60 90"
            // is a 3-tap pseudo. Overlays the preset row (ends just before the Custom toggle); shown
            // only when the Custom toggle is on.
            _pseudoCustomFieldGo = new GameObject("CustomAngles", typeof(RectTransform));
            _pseudoCustomFieldGo.transform.SetParent(_pseudoMenuGo.transform, false);
            var cuR = (RectTransform)_pseudoCustomFieldGo.transform;
            cuR.anchorMin = cuR.anchorMax = new Vector2(0f, 1f);
            cuR.pivot = new Vector2(0f, 1f);
            cuR.anchoredPosition = new Vector2(angX, row2Y);
            cuR.sizeDelta = new Vector2(custTogX - 8f - angX, bh);
            var cuBg = _pseudoCustomFieldGo.AddComponent<RoundedRectGraphic>();
            cuBg.Radius = 5f;
            cuBg.color = new Color(1f, 1f, 1f, 0.07f);
            cuBg.BorderWidth = 1f;
            cuBg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            cuBg.raycastTarget = true;
            var cuTxtGo = new GameObject("Text", typeof(RectTransform));
            cuTxtGo.transform.SetParent(_pseudoCustomFieldGo.transform, false);
            var cuTr = (RectTransform)cuTxtGo.transform;
            cuTr.anchorMin = Vector2.zero; cuTr.anchorMax = Vector2.one;
            cuTr.offsetMin = new Vector2(6f, 0f); cuTr.offsetMax = new Vector2(-6f, 0f);
            var cuTxt = UIBuilder.Tmp(cuTxtGo, "", 13f, TextAnchor.MiddleLeft, Theme.Text);
            cuTxt.richText = false;
            var cuPhGo = new GameObject("Placeholder", typeof(RectTransform));
            cuPhGo.transform.SetParent(_pseudoCustomFieldGo.transform, false);
            var cuPhR = (RectTransform)cuPhGo.transform;
            cuPhR.anchorMin = Vector2.zero; cuPhR.anchorMax = Vector2.one;
            cuPhR.offsetMin = new Vector2(6f, 0f); cuPhR.offsetMax = new Vector2(-6f, 0f);
            var cuPh = UIBuilder.Tmp(cuPhGo, "per-tile angles, e.g. 30 60 90", 12f, TextAnchor.MiddleLeft, Theme.TextMuted);
            _fPseudoCustom = UIBuilder.BuildInputField(_pseudoCustomFieldGo, cuTxt);
            _fPseudoCustom.placeholder = cuPh;
            _fPseudoCustom.lineType = TMP_InputField.LineType.SingleLine;

            SyncPseudoCustomMode();
            SyncPseudoMenuHighlight();
        }

        // A muted section label at the left of a submenu row.
        private static void MakeRowLabel(string text, float x, float y, float w, float h)
        {
            MakeInlineLabel("Lbl" + text, x, y, w, h, text);
        }

        private static TMPro.TextMeshProUGUI MakeInlineLabel(string name, float x, float y, float w, float h, string text)
        {
            var go = UIBuilder.Rect(name, _pseudoMenuGo.transform);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y - (h - 16f) * 0.5f);
            rt.sizeDelta = new Vector2(w, 16f);
            return UIBuilder.Tmp(go, text, 12f, TextAnchor.MiddleLeft, Theme.TextMuted);
        }

        private static void SyncPseudoAngle()
        {
            for (int i = 0; i < _pseudoAngleBtnBgs.Length; i++)
            {
                if (_pseudoAngleBtnBgs[i] == null) continue;
                bool match = double.TryParse(PseudoAngles[i], System.Globalization.NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var val) && System.Math.Abs(val - _pseudoTapAngle) < 0.001;
                _pseudoAngleBtnBgs[i].color = match
                    ? new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.45f)
                    : new Color(1f, 1f, 1f, 0.05f);
            }
        }

        // Small labelled button in the pseudo submenu; returns its background for highlighting.
        private static RoundedRectGraphic MakeMiniBtn(string name, float x, float y, float w, float h,
            string text, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_pseudoMenuGo.transform, false);
            var br = (RectTransform)go.transform;
            br.anchorMin = br.anchorMax = new Vector2(0f, 1f);
            br.pivot = new Vector2(0f, 1f);
            br.anchoredPosition = new Vector2(x, y);
            br.sizeDelta = new Vector2(w, h);
            var bbg = go.AddComponent<RoundedRectGraphic>();
            bbg.Radius = 6f;
            bbg.color = new Color(1f, 1f, 1f, 0.05f);
            bbg.BorderWidth = 1f;
            bbg.BorderColor = new Color(1f, 1f, 1f, 0.1f);
            bbg.raycastTarget = true;
            var lblGo = new GameObject("L", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;
            UIBuilder.Tmp(lblGo, text, 13f, TextAnchor.MiddleCenter, Theme.Text);
            UI.ClickHandler.Attach(go, onClick);
            return bbg;
        }

        // Small numeric input box in the pseudo submenu (matches MakeMiniBtn's frame).
        private static TMP_InputField MakeMiniField(string name, float x, float y, float w, float h,
            string initial, TMP_InputField.ContentType ctype, UnityEngine.Events.UnityAction<string> onChanged)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_pseudoMenuGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = new Color(1f, 1f, 1f, 0.07f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;
            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(6f, 0f); tr.offsetMax = new Vector2(-6f, 0f);
            var txt = UIBuilder.Tmp(txtGo, initial, 13f, TextAnchor.MiddleCenter, Theme.Text);
            txt.richText = false;
            var input = UIBuilder.BuildInputField(go, txt);
            input.contentType = ctype;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.text = initial;
            if (onChanged != null) input.onValueChanged.AddListener(onChanged);
            return input;
        }

        private static void SyncPseudoMidspin()
        {
            if (_pseudoMidspinBg == null) return;
            _pseudoMidspinBg.color = _pseudoMidspin
                ? new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.45f)
                : new Color(1f, 1f, 1f, 0.05f);
        }

        // Custom-angle mode: highlight the toggle, hide the preset row, show the text field (or vice
        // versa). The two share the same space on row 2.
        private static void SyncPseudoCustomMode()
        {
            if (_pseudoCustomBg != null)
                _pseudoCustomBg.color = _pseudoCustomAngles
                    ? new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.45f)
                    : new Color(1f, 1f, 1f, 0.05f);
            foreach (var go in _pseudoPresetObjs)
                if (go != null) go.SetActive(!_pseudoCustomAngles);
            if (_pseudoCustomFieldGo != null) _pseudoCustomFieldGo.SetActive(_pseudoCustomAngles);
        }

        private static void HidePseudoMenu()
        {
            if (_pseudoMenuGo != null) _pseudoMenuGo.SetActive(false);
        }

        private static void SyncPseudoMenuHighlight()
        {
            for (int i = 0; i < _pseudoBtnBgs.Length; i++)
            {
                if (_pseudoBtnBgs[i] == null) continue;
                bool sel = PseudoNumbers[i] == _pseudoN;
                _pseudoBtnBgs[i].color = sel
                    ? new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.45f)
                    : new Color(1f, 1f, 1f, 0.05f);
            }
            // Keep the custom-N field in sync when a preset button sets the count (no re-notify).
            if (_fPseudoN != null && _fPseudoN.text != _pseudoN.ToString())
                _fPseudoN.SetTextWithoutNotify(_pseudoN.ToString());
        }

        private static readonly System.Collections.Generic.List<RaycastResult> _rayHits =
            new System.Collections.Generic.List<RaycastResult>();

        // True if the cursor is over ANY Sapphire canvas (name starts with "Sapphire"): the
        // toolbar, its submenu, chrome, timeline, pitch overlay, popups, panel, etc.
        internal static bool PointerOverSapphireUI()
        {
            try
            {
                var es = EventSystem.current;
                if (es == null) return false;
                var ped = new PointerEventData(es) { position = Input.mousePosition };
                _rayHits.Clear();
                es.RaycastAll(ped, _rayHits);
                for (int i = 0; i < _rayHits.Count; i++)
                {
                    var go = _rayHits[i].gameObject;
                    if (go == null) continue;
                    var c = go.GetComponentInParent<Canvas>();
                    var rc = c != null ? c.rootCanvas : null;
                    if (rc != null && rc.name.StartsWith("Sapphire")) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool PointerOverToolbar()
        {
            try
            {
                var mp = (Vector2)Input.mousePosition;
                if (_barGo != null &&
                    RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_barGo.transform, mp, null)) return true;
                if (_pseudoMenuGo != null && _pseudoMenuGo.activeSelf &&
                    RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_pseudoMenuGo.transform, mp, null)) return true;
            }
            catch { }
            return false;
        }

        // The mouse's world position through the editor camera (z discarded by callers).
        private static Vector3 ClickWorld(scnEditor ed)
        {
            try
            {
                Camera cam = null;
                try { cam = ed.camera; } catch { }
                if (cam == null) cam = Camera.main;
                if (cam == null) return Vector3.zero;
                return cam.ScreenToWorldPoint(Input.mousePosition);
            }
            catch { return Vector3.zero; }
        }

        // Was the click on this tile? Guards the "selection unchanged" test against clicks on
        // empty space (which can also leave the selection untouched).
        private static bool NearClick(scrFloor tile, Vector3 world)
        {
            try
            {
                Vector2 a = tile.transform.position;
                Vector2 b = world;
                return Vector2.Distance(a, b) < 0.7f; // ~tile radius in world units
            }
            catch { return true; }
        }

        // Single-click = a LOCAL pseudo on the clicked tile (N−1 taps + even-key midspin, appended
        // in place) — it makes the pseudo and stops, leaving the rest of the chart untouched. (The
        // redirecting/"zip" construction lives in BuildSwirlRedirect, kept for a future zip tool.)
        private static void ConvertToPseudo(scnEditor ed, scrFloor tile, int n, bool midspin)
        {
            if (ed == null || tile == null) return;
            try
            {
                if (ed.lockPathEditing) { SapphireLog.Log("Pseudo: path editing locked"); return; }
                using (new SaveStateScope(ed))
                {
                    try { ed.DeselectFloors(); } catch { }
                    ed.SelectFloor(tile, false);
                    if (!ed.SelectionIsSingle()) { SapphireLog.Log("Pseudo: selection not single"); return; }
                    int taps = Mathf.Max(1, n - 1);
                    // Custom mode: the field's space-separated values are the per-tap charters;
                    // tap count = how many you typed. Otherwise the uniform tap angle drives it.
                    if (_pseudoCustomAngles)
                    {
                        string custom = PseudoCustomText();
                        if (string.IsNullOrEmpty(custom)) { SapphireLog.Log("Pseudo: no custom angles"); return; }
                        // Midspin → absolute facings (baseline+180−charter) with a midspin per tap;
                        // the midspins fold it back. No midspin → beat-neutral fold: charters padded
                        // to sum 180° (turns sum 360°) so the ball exits on the baseline again
                        // instead of climbing off as a disruptive staircase.
                        if (midspin) ApplyPseudoAbs(ed, tile, ParsePseudoCustom(), true);
                        else ApplyPseudo(ed, tile, PseudoCharters(_pseudoN, _pseudoTapAngle, custom), false, false);
                    }
                    else if (midspin) ApplyMidspinPseudo(ed, tile, taps, _pseudoTapAngle);
                    else ApplyInlinePseudo(ed, tile, taps, _pseudoTapAngle, false);
                }
            }
            catch (Exception ex) { SapphireLog.Log("Toolbar: pseudo convert failed: " + ex.Message); }
        }

        // Replace one tile with a swirl-only turn pseudo that REDIRECTS the ball (absolute facings +
        // swirl rules). Not used by single-click anymore — reserved for the future ZIP TOOL (large
        // key counts make a "zip": a run of tiny angles that zip by; a bug as a pseudo, a feature as
        // its own tool). Caller wraps in a SaveStateScope. turnSign +1 turns one way, −1 the other.
        private static void BuildSwirlRedirect(scnEditor ed, scrFloor tile, double[] charters, int turnSign)
        {
            if (charters == null || charters.Length == 0) return;
            int i = tile.seqID;
            if (i <= 0) { SapphireLog.Log("Pseudo: needs a tile before it"); return; }
            double dir = tile.floatDirection;

            try { ed.DeselectFloors(); } catch { }
            ed.SelectFloor(tile, false);
            if (!ed.SelectionIsSingle()) { SapphireLog.Log("Pseudo: selection not single"); return; }
            ed.DeleteSingleSelection(false);

            scrFloor prev = null;
            try { var fl = ed.floors; if (i - 1 < fl.Count) prev = fl[i - 1]; } catch { }
            if (prev == null) { SapphireLog.Log("Pseudo: lost predecessor"); return; }
            int spin = 1; try { spin = prev.isCCW ? 1 : -1; } catch { }
            try { ed.DeselectFloors(); } catch { }
            ed.SelectFloor(prev, false);

            var twirlSeqs = new System.Collections.Generic.List<int>();
            double facing = dir; int localSign = turnSign; int seq = i - 1;
            for (int k = 0; k < charters.Length; k++)
            {
                facing = Norm360(facing + localSign * (180.0 - charters[k]));
                AppendAbs(ed, facing); seq++;
                localSign = -localSign;
            }
            PseudoSwirls(twirlSeqs, i, charters.Length, turnSign, spin, true, 0); // one pseudo → parity on tile 0
            foreach (var s in twirlSeqs) if (s >= i - 1 && s <= seq) AddTwirl(ed, s);
            try { ed.RemakePath(true, true); } catch { }
        }

        // Swirl-only-pseudo swirl rule for a K-tile pseudo at seqIDs [firstSeq .. firstSeq+K-1]:
        // every tile after the first gets a swirl; the first tile → odd K yes / even K no, EXCEPT
        // the first pseudo of a run where it's parity (swirl iff the ball's spin ≠ the turn sign).
        // Events sit one tile earlier (the ball twirls leaving the prior tile).
        private static void PseudoSwirls(System.Collections.Generic.List<int> twirlSeqs,
            int firstSeq, int keys, int turnSign, int spinAtFirst, bool isFirstPseudo, int extraShift)
        {
            // extraShift lands the swirl on the right tile: 0 for the angled turns / single-click,
            // -1 for Upwards + square-wave steps (they render a tile later, so shift onto tile 1).
            bool oddKeys = (keys % 2 == 1);
            for (int k = 0; k < keys; k++)
            {
                bool swirl = (k >= 1) || (isFirstPseudo ? (spinAtFirst != turnSign) : oddKeys);
                if (swirl) twirlSeqs.Add(firstSeq + k - 1 + extraShift);
            }
        }

        private static string PseudoCustomText()
        {
            try { return _fPseudoCustom != null ? _fPseudoCustom.text : null; } catch { return null; }
        }

        // The charter angles of a pseudo, which MUST sum to 180° (= 1 beat) to be beat-neutral.
        // Custom "a b c" (space-separated) wins when given; a compensate tile is appended if the
        // custom angles fall short of 180. Otherwise the tap angle is repeated with a final
        // compensate: [X, X, …, 180−(N−1)X] (e.g. 2-key/30 = 30,150; 2-key/90 = 90,90).
        private static double[] PseudoCharters(int n, double tapAngle, string custom)
        {
            var list = new System.Collections.Generic.List<double>();
            if (!string.IsNullOrEmpty(custom))
            {
                foreach (var tok in custom.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0.0)
                        list.Add(v);
                double sum = 0; foreach (var a in list) sum += a;
                if (sum < 179.99 && 180.0 - sum > 0.01) list.Add(180.0 - sum); // compensate to 1 beat
            }
            else
            {
                if (n < 2) n = 2;
                if (tapAngle <= 0 || (n - 1) * tapAngle >= 180.0)
                {
                    // tap angle too steep to leave a compensate — fall back to an even split
                    for (int i = 0; i < n; i++) list.Add(180.0 / n);
                }
                else
                {
                    for (int i = 0; i < n - 1; i++) list.Add(tapAngle);
                    list.Add(180.0 - (n - 1) * tapAngle);
                }
            }
            return list.ToArray();
        }

        // Beat-neutral conversion: replace the selected straight tile's single beat with tiles
        // whose charter angles sum to 180°. We delete the tile and re-append the pseudo tiles in
        // its place (from the tile before it), so the run keeps the same rhythm and the downstream
        // reconnects to the pseudo's exit. Caller wraps this in a SaveStateScope.
        private static void ApplyPseudo(scnEditor ed, scrFloor tile, double[] charters, bool midspin, bool twirlFirst)
        {
            if (charters == null || charters.Length == 0) return;
            int i = tile.seqID;
            if (i <= 0) { SapphireLog.Log("Pseudo: needs a tile before it"); return; }

            double dir = tile.floatDirection;            // ball heading through the tile
            try { ed.DeselectFloors(); } catch { }
            ed.SelectFloor(tile, false);
            if (!ed.SelectionIsSingle()) { SapphireLog.Log("Pseudo: selection not single"); return; }
            ed.DeleteSingleSelection(false);             // drop the straight tile's 1 beat

            scrFloor prev = null;
            try { var fl = ed.floors; if (i - 1 < fl.Count) prev = fl[i - 1]; } catch { }
            if (prev == null) { SapphireLog.Log("Pseudo: lost predecessor after delete"); return; }
            try { ed.DeselectFloors(); } catch { }
            ed.SelectFloor(prev, false);

            for (int k = 0; k < charters.Length; k++)
            {
                dir = AppendRel(ed, charters[k], dir);
                // Upwards: a twirl on the first pseudo tile flips handedness so the excursion
                // CLIMBS (staircase) instead of tabbing back to the line (first cut — the exact
                // twirl-parity across a run still needs validation).
                if (twirlFirst && k == 0)
                    try { AddTwirl(ed, ed.selectedFloors[0].seqID); } catch { }
            }
            if (midspin) AppendMidspin(ed);
        }

        // A Twirl event on a floor (reverses the ball's spin handedness). No data payload.
        private static void AddTwirl(scnEditor ed, int floorSeq)
        {
            try
            {
                var ev = new ADOFAI.LevelEvent(floorSeq, ADOFAI.LevelEventType.Twirl);
                ed.events.Add(ev);
            }
            catch (Exception ex) { SapphireLog.Log("Toolbar: add twirl failed: " + ex.Message); }
        }

        // Flat batch: replace every `interval`-th selected tile with a beat-neutral pseudo (keys
        // from the submenu). Processed high-seqID first so a replacement never shifts an as-yet-
        // unprocessed (lower) tile; the whole run is ONE undo.
        // "Inline" style (formerly "Flat"): a beat-neutral pseudo on every interval-th tile that
        // keeps the run level. Even key-counts get ≥1 midspin so their taps don't flip direction.
        private static void GeneratePseudoBatchFlat(scnEditor ed, int interval, int keys)
        {
            if (ed.lockPathEditing) { SapphireLog.Log("Batch pseudo: path editing locked"); return; }
            if (interval < 1) interval = 1;

            var seqs = new System.Collections.Generic.List<int>();
            try { foreach (var f in ed.selectedFloors) seqs.Add(f.seqID); } catch { }
            if (seqs.Count == 0) { SapphireLog.Log("Batch pseudo: no selection"); return; }
            seqs.Sort();

            int taps = Mathf.Max(1, keys - 1);
            bool midspin = (keys % 2 == 0);   // even keys need a trailing midspin to keep direction
            using (new SaveStateScope(ed))
            {
                for (int i = seqs.Count - 1; i >= 0; i--)
                {
                    if ((i % interval) != 0) continue;   // spacing between pseudos
                    scrFloor tile = null;
                    try
                    {
                        var fl = ed.floors;
                        if (fl != null && seqs[i] >= 0 && seqs[i] < fl.Count) tile = fl[seqs[i]];
                    }
                    catch { }
                    if (tile != null) ApplyInlinePseudo(ed, tile, taps, _pseudoTapAngle, midspin);
                }
            }
        }

        // Inline/Flat "battlements": append N−1 taps at the tap angle AFTER the tile (the tile
        // stays, so the line stays flat and the taps grow an up-tab off it), then a trailing
        // midspin for even keys so the ball comes back and keeps its direction.
        private static void ApplyInlinePseudo(scnEditor ed, scrFloor tile, int taps, double tapAngle, bool midspin)
        {
            try { ed.DeselectFloors(); } catch { }
            ed.SelectFloor(tile, false);
            if (!ed.SelectionIsSingle()) { SapphireLog.Log("Inline: selection not single"); return; }
            double dir = tile.floatDirection;
            for (int k = 0; k < taps; k++) dir = AppendRel(ed, tapAngle, dir);
            if (midspin) AppendMidspin(ed);
        }

        // Midspin pseudo — reverse-engineered from the user's reference .adofai. Each tap is
        // followed by its OWN midspin (INTERLEAVED, not one trailing), and the tap facings step
        // DOWN by the tap angle every hit because each midspin reverses the ball: on an east
        // baseline a 30° pseudo is facings 150,120,90,… (2k=[150,999], 3k=[150,999,120,999], …).
        // Absolute facings only — relative/charter math is invalid across a midspin. The clicked
        // tile stays as the baseline before the excursion; the next existing tile resumes it.
        private static void ApplyMidspinPseudo(scnEditor ed, scrFloor tile, int taps, double tapAngle,
            int sign = 1)
        {
            // Uniform 30° → charters 30,60,90,… (facings 150,120,90,…).
            var charters = new double[Mathf.Max(0, taps)];
            for (int k = 0; k < charters.Length; k++) charters[k] = tapAngle * (k + 1);
            ApplyPseudoAbs(ed, tile, charters, true, sign);
        }

        // General absolute-facing pseudo: one tile per charter at facing = baseline + 180 − charter,
        // each optionally followed by its own midspin. Drives both the uniform midspin pseudo (above)
        // and custom-angle mode (charters = the field's per-tap values, e.g. "30 60 90").
        private static void ApplyPseudoAbs(scnEditor ed, scrFloor tile, double[] charters, bool midspin,
            int sign = 1)
        {
            if (charters == null || charters.Length == 0) return;
            try { ed.DeselectFloors(); } catch { }
            ed.SelectFloor(tile, false);
            if (!ed.SelectionIsSingle()) { SapphireLog.Log("Pseudo: selection not single"); return; }
            double d = tile.floatDirection;
            foreach (var c in charters)
            {
                AppendAbs(ed, Norm360(d + sign * (180.0 - c)));
                if (midspin) AppendMidspin(ed);
            }
        }

        // Custom-angle field → per-tap charter values (space/comma separated). Empty when off/blank.
        private static double[] ParsePseudoCustom()
        {
            var list = new System.Collections.Generic.List<double>();
            string s = null;
            try { s = _fPseudoCustom != null ? _fPseudoCustom.text : null; } catch { }
            if (!string.IsNullOrEmpty(s))
                foreach (var tok in s.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) list.Add(v);
            return list.ToArray();
        }

        // Upwards batch = a SERPENTINE (ㄹ): fold the selected straight run back and forth while
        // climbing. Every `interval` beats it does a beat-neutral U-turn pseudo (charter [X,180−X]
        // with SAME-sign turns → 180° reverse) that flips the run direction and steps up. The turn
        // sign alternates each U-turn so the climb always goes UP regardless of east/west run
        // (that's the "detect left or right"). The whole selection is rebuilt in absolute facings,
        // so the shape is deterministic (twirls for playability can come later). One undo.
        // Batch overlay (July 11 — user: "the existing selected path IS the base beat"): the
        // selection's layout stays untouched; every interval-th tile gets a LOCAL midspin pseudo
        // (interleaved tap+999, exact return to course — the on-layout construction from the
        // reference .adofai) added ON TOP. `mode`: 0 = Upwards (world-up excursions), 1 =
        // Sideways-up, 2 = Sideways-alternate (square-wave feel), 3 = Sideways-down.
        // (The delete+rebuild serpentine/zigzag builders below are kept unreferenced — verified
        // constructions that may return as an explicit "rebuild" style.)
        private static void GeneratePseudoBatchOverlay(scnEditor ed, int interval, int keys, int mode)
        {
            if (ed.lockPathEditing) { SapphireLog.Log("Batch pseudo: path editing locked"); return; }
            if (interval < 1) interval = 1;

            var seqs = new System.Collections.Generic.List<int>();
            try { foreach (var f in ed.selectedFloors) seqs.Add(f.seqID); } catch { }
            if (seqs.Count == 0) { SapphireLog.Log("Batch pseudo: no selection"); return; }
            seqs.Sort();

            // per-tap charters: custom field wins, else uniform midspin steps X·k
            double[] charters = _pseudoCustomAngles ? ParsePseudoCustom() : null;
            if (charters == null || charters.Length == 0)
            {
                int taps = Mathf.Max(1, keys - 1);
                charters = new double[taps];
                for (int k = 0; k < taps; k++) charters[k] = _pseudoTapAngle * (k + 1);
            }

            // Keep-beats: the midspin toggle picks the construction. ON = the exact-return
            // midspin pseudo (interleaved tap+999). OFF = "normal keep beat" — NOT built yet
            // (user: implement later); fall back to plain taps so it still does something.
            bool midspin = _pseudoMidspin;
            if (!midspin) SapphireLog.Log("Batch overlay: normal keep-beat (non-midspin) is WIP — using plain taps");
            using (new SaveStateScope(ed))
            {
                for (int i = seqs.Count - 1; i >= 0; i--)   // high→low: inserts don't shift lower seqs
                {
                    if ((i % interval) != 0) continue;      // spacing between pseudos
                    scrFloor tile = null;
                    try
                    {
                        var fl = ed.floors;
                        if (fl != null && seqs[i] >= 0 && seqs[i] < fl.Count) tile = fl[seqs[i]];
                    }
                    catch { }
                    if (tile == null) continue;

                    // world-up for THIS tile's heading (a west run needs the flipped sign)
                    int worldUp = 1;
                    try { worldUp = Mathf.Cos((float)(tile.floatDirection * Mathf.Deg2Rad)) >= 0f ? 1 : -1; }
                    catch { }
                    int ordinal = i / interval;             // pseudo index from the run's start
                    int sign = mode == 3 ? -worldUp
                             : mode == 2 ? ((ordinal % 2 == 0) ? worldUp : -worldUp)
                             : worldUp;                     // 0/1 = up
                    ApplyPseudoAbs(ed, tile, charters, midspin, sign);
                }
            }
        }

        // (unreferenced since the July 11 overlay model)
        private static void GeneratePseudoBatchUpwards(scnEditor ed, int interval, int keys)
        {
            BuildSerpentine(ed, interval, +1);
        }

        // Non-90 Sideways = a climbing/descending ZIGZAG (not a fold): alternate a straight run and
        // a diagonal, joined by a beat-neutral [X,180−X] turn pseudo. Facings (matched to the user's
        // reference, 30° section): transition tile1 = cur + sign·(180−X), tile2 = cur + sign·(180−2X)
        // = the new diagonal; sign flips each turn. Twirls go on tile2 and are added AFTER all
        // facings are written (a twirl mid-build corrupts the arbitrary-angle writes). One undo.
        private static void BuildAngledSideways(scnEditor ed, int interval, int climbSign)
        {
            if (ed.lockPathEditing) { SapphireLog.Log("Batch pseudo: path editing locked"); return; }
            if (interval < 2) interval = 2;

            var seqs = new System.Collections.Generic.List<int>();
            try { foreach (var f in ed.selectedFloors) seqs.Add(f.seqID); } catch { }
            if (seqs.Count == 0) { SapphireLog.Log("Batch pseudo: no selection"); return; }
            seqs.Sort();
            int n = seqs.Count, first = seqs[0];
            if (first <= 0) { SapphireLog.Log("Sideways: need a tile before the selection"); return; }

            double x = _pseudoTapAngle;
            if (x <= 0.0 || x >= 90.0) x = 30.0;   // non-90 turn angle
            // Turn pseudo can be a double/triple/quadruple… (keys from the submenu) — charters sum
            // to 180 exactly like a normal pseudo. Facings accumulate sign·(180−charter) with the
            // sign flipping each tile (verified vs the ref: triple 150,0,60; quad 165,0,165,120).
            var charters = PseudoCharters(_pseudoN, x, PseudoCustomText());
            SapphireLog.Log("Angled sideways: n=" + n + " interval=" + interval + " x=" + x
                + " keys=" + charters.Length + " climb=" + climbSign);

            using (new SaveStateScope(ed))
            {
                double dir = 0.0;
                try { dir = ed.floors[first].floatDirection; } catch { }

                for (int i = n - 1; i >= 0; i--)   // delete the whole run, high→low
                {
                    scrFloor t = null;
                    try { var fl = ed.floors; if (seqs[i] >= 0 && seqs[i] < fl.Count) t = fl[seqs[i]]; } catch { }
                    if (t == null) continue;
                    try { ed.DeselectFloors(); ed.SelectFloor(t, false); if (ed.SelectionIsSingle()) ed.DeleteSingleSelection(false); } catch { }
                }

                scrFloor prev = null;
                try { prev = ed.floors[first - 1]; } catch { }
                if (prev == null) { SapphireLog.Log("Sideways: lost predecessor"); return; }
                try { ed.DeselectFloors(); ed.SelectFloor(prev, false); } catch { }

                // Swirl rule: every pseudo tile AFTER the first always needs a swirl. The FIRST
                // tile needs one on odd-key pseudos (even-key: skip it) — EXCEPT the very first
                // pseudo, where it depends on the ball's spin coming in: if the tap can be made in
                // the current spin, no swirl; if it would wrap the long way (e.g. 30°→330°), swirl.
                int initialSpin = 1;
                try { initialSpin = prev.isCCW ? 1 : -1; } catch { }
                // spin flips once per tile; the first pseudo sits after (interval-1) straights
                int spinAtFirst = initialSpin * (((interval - 1) % 2 == 0) ? 1 : -1);

                var twirlSeqs = new System.Collections.Generic.List<int>();
                double curFacing = dir;
                int sign = climbSign;
                int seq = first - 1;   // predecessor's seqID; each AppendAbs adds the next one
                int pseudoIndex = 0;
                for (int beat = 0; beat < n; beat++)
                {
                    if ((beat + 1) % interval == 0)   // turn pseudo (K tiles = 1 beat)
                    {
                        int firstSeq = seq + 1;
                        double facing = curFacing;
                        int localSign = sign;
                        for (int k = 0; k < charters.Length; k++)
                        {
                            facing = Norm360(facing + localSign * (180.0 - charters[k]));
                            AppendAbs(ed, facing);
                            seq++;
                            localSign = -localSign;   // the ball's turn flips each tile
                        }
                        PseudoSwirls(twirlSeqs, firstSeq, charters.Length, sign, spinAtFirst, pseudoIndex == 0, 0);
                        curFacing = facing;       // last tile = the new diagonal
                        sign = -sign;
                        pseudoIndex++;
                    }
                    else
                    {
                        AppendAbs(ed, Norm360(curFacing));   // straight in the current heading
                        seq++;
                    }
                }
                foreach (var s in twirlSeqs) if (s >= first && s <= seq) AddTwirl(ed, s); // keep in-range
                try { ed.RemakePath(true, true); } catch { }
            }
        }

        // Rebuild the selected straight run as a folding serpentine (ㄹ) that climbs (climbSign +1)
        // or descends (−1). Every `interval` beats: a beat-neutral U-turn pseudo (charter [X,180−X],
        // same-sign turns → 180° reverse) flips the run direction and steps up/down; the turn sign
        // alternates each U-turn so it keeps going the SAME vertical way regardless of the run's
        // east/west direction. Absolute facings → deterministic shape. One undo.
        private static void BuildSerpentine(scnEditor ed, int interval, int climbSign)
        {
            if (ed.lockPathEditing) { SapphireLog.Log("Batch pseudo: path editing locked"); return; }
            if (interval < 2) interval = 2;

            var seqs = new System.Collections.Generic.List<int>();
            try { foreach (var f in ed.selectedFloors) seqs.Add(f.seqID); } catch { }
            if (seqs.Count == 0) { SapphireLog.Log("Batch pseudo: no selection"); return; }
            seqs.Sort();
            int n = seqs.Count, first = seqs[0];
            if (first <= 0) { SapphireLog.Log("Serpentine: need a tile before the selection"); return; }

            double x = _pseudoTapAngle;
            if (x <= 0.0 || x >= 180.0) x = 90.0;   // turn-shape angle (tap angle)
            SapphireLog.Log("Serpentine: n=" + n + " interval=" + interval + " x=" + x + " climb=" + climbSign);

            using (new SaveStateScope(ed))
            {
                double dir = 0.0;
                try { dir = ed.floors[first].floatDirection; } catch { }

                for (int i = n - 1; i >= 0; i--)   // delete the whole selected run, high→low
                {
                    scrFloor t = null;
                    try { var fl = ed.floors; if (seqs[i] >= 0 && seqs[i] < fl.Count) t = fl[seqs[i]]; } catch { }
                    if (t == null) continue;
                    try { ed.DeselectFloors(); ed.SelectFloor(t, false); if (ed.SelectionIsSingle()) ed.DeleteSingleSelection(false); } catch { }
                }

                scrFloor prev = null;
                try { prev = ed.floors[first - 1]; } catch { }
                if (prev == null) { SapphireLog.Log("Serpentine: lost predecessor"); return; }
                try { ed.DeselectFloors(); ed.SelectFloor(prev, false); } catch { }

                double curFacing = dir;
                int turnSign = climbSign;   // +1 climbs, −1 descends
                int seq = first - 1;
                int initialSpin = 1; try { initialSpin = prev.isCCW ? 1 : -1; } catch { }
                int spinAtFirst = initialSpin * (((interval - 1) % 2 == 0) ? 1 : -1);
                var twirlSeqs = new System.Collections.Generic.List<int>();
                int pseudoIndex = 0;
                for (int beat = 0; beat < n; beat++)
                {
                    if ((beat + 1) % interval == 0)
                    {
                        int firstSeq = seq + 1;
                        AppendAbs(ed, Norm360(curFacing + turnSign * (180.0 - x))); seq++; // up/down tile
                        double rev = Norm360(curFacing + 180.0);
                        AppendAbs(ed, rev); seq++;                                          // reversed tile
                        // one swirl on the pseudo's first tile; the FIRST pseudo only if the spin
                        // needs it (parity), the rest always.
                        if (pseudoIndex != 0 || spinAtFirst != turnSign) twirlSeqs.Add(firstSeq - 1);
                        curFacing = rev;
                        turnSign = -turnSign;
                        pseudoIndex++;
                    }
                    else
                    {
                        AppendAbs(ed, Norm360(curFacing)); seq++;   // straight tile in the current direction
                    }
                }
                foreach (var s in twirlSeqs) if (s >= first && s <= seq) AddTwirl(ed, s);
                try { ed.RemakePath(true, true); } catch { }
            }
        }

        private static double Norm360(double a)
        {
            a %= 360.0;
            if (a < 0.0) a += 360.0;
            return a;
        }

        // ── circular-path dialog ────────────────────────────────────────────

        private static void OpenDialog()
        {
            if (_dialogGo != null) { CloseDialog(); return; }
            DeactivateFreeAngle(); DeactivatePseudo(); // one tool at a time

            _dialogGo = new GameObject("CircleDialog", typeof(RectTransform));
            _dialogGo.transform.SetParent(_canvasGo.transform, false);
            var blocker = (RectTransform)_dialogGo.transform;
            blocker.anchorMin = Vector2.zero; blocker.anchorMax = Vector2.one;
            blocker.offsetMin = Vector2.zero; blocker.offsetMax = Vector2.zero;
            var blockImg = _dialogGo.AddComponent<Image>();
            blockImg.color = new Color(0f, 0f, 0f, 0.35f);
            blockImg.raycastTarget = true;
            UI.ClickHandler.Attach(_dialogGo, CloseDialog); // click outside closes

            const float w = 320f, padX = 22f;
            var cardGo = new GameObject("Card", typeof(RectTransform));
            cardGo.transform.SetParent(_dialogGo.transform, false);
            var card = (RectTransform)cardGo.transform;
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = new Vector2(0f, 30f);
            var cardBg = cardGo.AddComponent<RoundedRectGraphic>();
            cardBg.Radius = 14f;
            cardBg.color = new Color(0.07f, 0.07f, 0.09f, 0.99f);
            cardBg.BorderWidth = 1f;
            cardBg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            cardBg.raycastTarget = true; // clicks on the card must not close it

            float y = -20f;
            var title = MakeLabel(card, "Curved path", padX, y, 16f, Theme.Text, TextAnchor.UpperLeft);
            title.fontStyle = FontStyles.Bold;
            y -= 34f;

            // Pseudo checkbox: on = star generator UI, off = plain circle-path fields.
            MakeCheckbox(card, "Pseudo (star)", padX, y, w,
                () => _pseudoMode, v => { _pseudoMode = v; RebuildDialog(); });
            y -= 32f;

            if (_pseudoMode)
            {
                _fPerRound = MakeLabeledField(card, "Pseudo per round", y, "6", w, padX);
                y -= 34f;
                _fInterval = MakeLabeledField(card, "Pseudo interval", y, "4", w, padX);
                y -= 34f;
                _fPseudoAngle = MakeLabeledField(card, "Pseudo angle", y, "30", w, padX);
                y -= 38f;
                MakeSegmented(card, "Reverse", "Outer angle", "Inner angle", padX, y, w,
                    () => _innerAngle, v => _innerAngle = v);
                y -= 52f;
                MakeCheckbox(card, "Keep BPM", padX, y, w,
                    () => _keepBpm, v => _keepBpm = v);
                y -= 28f;
                MakeCheckbox(card, "Pseudos as mid-spin tiles", padX, y, w,
                    () => _midspinPseudos, v => _midspinPseudos = v);
                y -= 40f;
            }
            else
            {
                _fDegrees = MakeLabeledField(card, "Circle degrees", y, "360", w, padX);
                y -= 34f;
                _fTileCount = MakeLabeledField(card, "Tile count", y, "24", w, padX);
                y -= 38f;
                MakeSegmented(card, "Reverse", "Outer angle", "Inner angle", padX, y, w,
                    () => _innerAngle, v => _innerAngle = v);
                y -= 52f;
                MakeCheckbox(card, "Keep BPM", padX, y, w,
                    () => _keepBpm, v => _keepBpm = v);
                y -= 40f;
            }

            // Buttons: Generate (positive, right), Cancel (neutral, left of it).
            float bx = -padX;
            bx -= MakeButton(card, "Generate", BtnKind.Positive, new Vector2(bx, y), () =>
            {
                Generate();
                CloseDialog();
            }) + 10f;
            MakeButton(card, "Cancel", BtnKind.Neutral, new Vector2(bx, y), CloseDialog);
            y -= 34f + 18f;

            card.sizeDelta = new Vector2(w, -y);
        }

        private static void CloseDialog()
        {
            if (_dialogGo != null) UnityEngine.Object.Destroy(_dialogGo);
            _dialogGo = null;
            _fPerRound = _fInterval = _fPseudoAngle = _fDegrees = _fTileCount = null;
            _fPseudoBatchInterval = null;
        }

        // A stable-ish fingerprint of the current multi-selection (count + span) so the batch
        // dialog auto-opens once per distinct selection.
        // A straight run = all selected floors share (nearly) one heading and none is a midspin.
        private static bool SelectionIsStraight()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return true;
            try
            {
                var sel = ed.selectedFloors;
                if (sel == null || sel.Count == 0) return true;
                float d0 = sel[0].floatDirection;
                foreach (var f in sel)
                {
                    if (f == null) continue;
                    if (f.midSpin) return false;
                    float diff = Mathf.Abs(Mathf.DeltaAngle(d0, f.floatDirection));
                    if (diff > 1f) return false;
                }
            }
            catch { }
            return true;
        }

        private static long MultiSelSignature(scnEditor ed)
        {
            try
            {
                var sf = ed.selectedFloors;
                int count = sf.Count, mn = int.MaxValue, mx = int.MinValue;
                for (int i = 0; i < count; i++)
                {
                    int id = sf[i].seqID;
                    if (id < mn) mn = id;
                    if (id > mx) mx = id;
                }
                return ((long)count << 40) ^ ((long)mn << 20) ^ (long)mx;
            }
            catch { return 0; }
        }

        // Multi-tile pseudo: ask interval + style, then (later) lay pseudos across the selection.
        // Generation is NOT wired yet — Apply just records the request. Auto-opened from
        // TickPseudoTool when >1 tiles are selected with the pseudo tool active.
        private static void OpenPseudoBatchDialog(int tileCount)
        {
            if (_dialogGo != null) return;

            _dialogGo = new GameObject("PseudoBatchDialog", typeof(RectTransform));
            _dialogGo.transform.SetParent(_canvasGo.transform, false);
            var blocker = (RectTransform)_dialogGo.transform;
            blocker.anchorMin = Vector2.zero; blocker.anchorMax = Vector2.one;
            blocker.offsetMin = Vector2.zero; blocker.offsetMax = Vector2.zero;
            var blockImg = _dialogGo.AddComponent<Image>();
            blockImg.color = new Color(0f, 0f, 0f, 0.35f);
            blockImg.raycastTarget = true;
            UI.ClickHandler.Attach(_dialogGo, CloseDialog);

            const float w = 320f, padX = 22f;
            var cardGo = new GameObject("Card", typeof(RectTransform));
            cardGo.transform.SetParent(_dialogGo.transform, false);
            var card = (RectTransform)cardGo.transform;
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = new Vector2(0f, 30f);
            var cardBg = cardGo.AddComponent<RoundedRectGraphic>();
            cardBg.Radius = 14f;
            cardBg.color = new Color(0.07f, 0.07f, 0.09f, 0.99f);
            cardBg.BorderWidth = 1f;
            cardBg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            cardBg.raycastTarget = true;

            _pseudoBatchTileCount = tileCount;
            float y = -20f;
            var title = MakeLabel(card, "Pseudo across " + tileCount + " tiles", padX, y, 16f, Theme.Text, TextAnchor.UpperLeft);
            title.fontStyle = FontStyles.Bold;
            y -= 34f;

            _fPseudoBatchInterval = MakeLabeledField(card, "Pseudo interval", y, _pseudoBatchIntervalStr, w, padX);
            y -= 40f;

            // Upwards/Sideways (directed excursions) only make sense on a STRAIGHT run — a tile
            // that already turns has no clean up/down. Off a straight selection, only Inline.
            if (SelectionIsStraight())
            {
                MakeSegmented3(card, "Style", "Upwards", "Sideways", "Inline", padX, y, w,
                    () => _pseudoBatchStyle, v => { _pseudoBatchStyle = v; RebuildPseudoBatchDialog(); });
                y -= 52f;
            }
            else
            {
                if (_pseudoBatchStyle != 2) _pseudoBatchStyle = 2; // force Inline
                MakeLabel(card, "Style: Inline (selection isn't straight)", padX, y, 13f, Theme.TextMuted, TextAnchor.UpperLeft);
                y -= 30f;
            }

            if (_pseudoBatchStyle == 1) // Sideways — pick the vertical drift
            {
                bool is90 = Mathf.Abs((float)_pseudoTapAngle - 90f) < 0.01f;
                if (is90)   // 90° square pseudos → up / inline / down
                {
                    MakeSegmented3(card, "Sideways", "Upwards", "Inline", "Downwards", padX, y, w,
                        () => _pseudoSidewaysVariant, v => _pseudoSidewaysVariant = v);
                    y -= 52f;
                }
                else        // non-90 → angled serpentine, inline not available
                {
                    if (_pseudoSidewaysVariant == 1) _pseudoSidewaysVariant = 0;
                    MakeSegmented(card, "Sideways", "Upwards", "Downwards", padX, y, w,
                        () => _pseudoSidewaysVariant == 2, v => _pseudoSidewaysVariant = v ? 2 : 0);
                    y -= 52f;
                    MakeLabel(card, "Angled at " + _pseudoTapAngle.ToString("0.##", CultureInfo.InvariantCulture)
                        + "° (inline needs 90°).", padX, y, 11f, Theme.TextMuted, TextAnchor.UpperLeft)
                        .rectTransform.sizeDelta = new Vector2(w - padX * 2f, 16f);
                    y -= 30f;
                }
            }

            float bx = -padX;
            bx -= MakeButton(card, "Apply", BtnKind.Positive, new Vector2(bx, y), () =>
            {
                ApplyPseudoBatch(tileCount);
                CloseDialog();
            }) + 10f;
            MakeButton(card, "Cancel", BtnKind.Neutral, new Vector2(bx, y), CloseDialog);
            y -= 34f + 18f;

            card.sizeDelta = new Vector2(w, -y);
        }

        // Rebuild the batch dialog in place (style toggle swaps the Sideways sub-selector in/out),
        // preserving the interval the user typed.
        private static void RebuildPseudoBatchDialog()
        {
            try { if (_fPseudoBatchInterval != null) _pseudoBatchIntervalStr = _fPseudoBatchInterval.text; } catch { }
            CloseDialog();
            OpenPseudoBatchDialog(_pseudoBatchTileCount);
        }

        private static readonly string[] BatchStyleNames = { "Upwards", "Sideways", "Inline" };

        private static void ApplyPseudoBatch(int tileCount)
        {
            int interval = Mathf.Max(1, ParseInt(_fPseudoBatchInterval, 4));
            int keys = _pseudoN;               // key-count from the pseudo submenu
            string style = (_pseudoBatchStyle >= 0 && _pseudoBatchStyle < BatchStyleNames.Length)
                ? BatchStyleNames[_pseudoBatchStyle] : "Upwards";

            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;

            if (_pseudoBatchStyle == 2)      // Inline (battlements — already layout-preserving)
                GeneratePseudoBatchFlat(ed, interval, keys);
            else if (_pseudoBatchStyle == 0) // Upwards — overlay on the existing layout
                GeneratePseudoBatchOverlay(ed, interval, keys, 0);
            else                             // Sideways up / alternate / down — overlay
                GeneratePseudoBatchOverlay(ed, interval, keys,
                    _pseudoSidewaysVariant == 1 ? 2 : _pseudoSidewaysVariant == 2 ? 3 : 1);
        }

        // Sideways batch: the run CONTINUES its direction (no fold), with 90° pseudos drifting
        // up (staircase), staying inline, or drifting down. Up/down are deterministic absolute-
        // facing steps ([D±90, D], +1/−1 row, continue D); inline reuses the flat tab (90° only).
        private static void GeneratePseudoBatchSideways(scnEditor ed, int interval, int keys)
        {
            if (ed.lockPathEditing) { SapphireLog.Log("Batch pseudo: path editing locked"); return; }
            if (interval < 1) interval = 1;
            bool is90 = Mathf.Abs((float)_pseudoTapAngle - 90f) < 0.01f;

            // Non-90 up/down can't be a clean square staircase, so it uses the angled serpentine
            // (fold). Inline isn't possible for non-90 (and the dialog hides it there).
            if (!is90)
            {
                if (_pseudoSidewaysVariant == 1) { SapphireLog.Log("Sideways inline needs a 90° tap angle"); return; }
                BuildAngledSideways(ed, interval, _pseudoSidewaysVariant == 2 ? -1 : +1);
                return;
            }

            // 90°: square steps that keep the run direction — up = [D+90,D], down = [D−90,D],
            // inline = alternate up/down each pseudo (a level square wave). Rebuilt FORWARD.
            var seqs = new System.Collections.Generic.List<int>();
            try { foreach (var f in ed.selectedFloors) seqs.Add(f.seqID); } catch { }
            if (seqs.Count == 0) { SapphireLog.Log("Batch pseudo: no selection"); return; }
            seqs.Sort();
            int n = seqs.Count, first = seqs[0];
            if (first <= 0) { SapphireLog.Log("Sideways: need a tile before the selection"); return; }

            SapphireLog.Log("Sideways square: n=" + n + " interval=" + interval + " variant=" + _pseudoSidewaysVariant);
            using (new SaveStateScope(ed))
            {
                double d = 0.0;
                try { d = ed.floors[first].floatDirection; } catch { }

                for (int i = n - 1; i >= 0; i--)   // delete the whole run, high→low
                {
                    scrFloor t = null;
                    try { var fl = ed.floors; if (seqs[i] >= 0 && seqs[i] < fl.Count) t = fl[seqs[i]]; } catch { }
                    if (t == null) continue;
                    try { ed.DeselectFloors(); ed.SelectFloor(t, false); if (ed.SelectionIsSingle()) ed.DeleteSingleSelection(false); } catch { }
                }
                scrFloor prev = null;
                try { prev = ed.floors[first - 1]; } catch { }
                if (prev == null) { SapphireLog.Log("Sideways: lost predecessor"); return; }
                try { ed.DeselectFloors(); ed.SelectFloor(prev, false); } catch { }

                int seq = first - 1;
                int initialSpin = 1; try { initialSpin = prev.isCCW ? 1 : -1; } catch { }
                int spinAtFirst = initialSpin * (((interval - 1) % 2 == 0) ? 1 : -1);
                var twirlSeqs = new System.Collections.Generic.List<int>();
                int pseudoIndex = 0;
                for (int beat = 0; beat < n; beat++)
                {
                    if ((beat + 1) % interval == 0)
                    {
                        int stepSign = (_pseudoSidewaysVariant == 0) ? +1
                            : (_pseudoSidewaysVariant == 2) ? -1
                            : ((pseudoIndex % 2 == 0) ? +1 : -1);   // inline alternates up/down
                        int firstSeq = seq + 1;
                        AppendAbs(ed, Norm360(d + stepSign * 90.0)); seq++;   // step up/down tile
                        AppendAbs(ed, Norm360(d)); seq++;                     // continue the run
                        // up/down staircases need a swirl on BOTH tiles; inline (square wave) needs
                        // just the first. First tile of the first pseudo is parity-gated.
                        if (_pseudoSidewaysVariant != 1) twirlSeqs.Add(firstSeq);   // 2nd tile (up/down)
                        if (pseudoIndex != 0 || spinAtFirst != stepSign) twirlSeqs.Add(firstSeq - 1); // 1st tile
                        pseudoIndex++;
                    }
                    else
                    {
                        AppendAbs(ed, Norm360(d)); seq++;
                    }
                }
                foreach (var s in twirlSeqs) if (s >= first && s <= seq) AddTwirl(ed, s);
                try { ed.RemakePath(true, true); } catch { }
            }
        }

        // Three-way segmented selector (int-indexed), sibling of MakeSegmented.
        private static void MakeSegmented3(Transform card, string label, string a, string b, string c,
            float x, float y, float cardW, Func<int> getI, Action<int> setI)
        {
            MakeLabel(card, label, x, y, 12f, Theme.TextMuted, TextAnchor.UpperLeft)
                .rectTransform.sizeDelta = new Vector2(cardW - x * 2f, 16f);

            float rowY = y - 18f, gap = 6f;
            float btnW = (cardW - x * 2f - gap * 2f) / 3f;
            var bgs = new RoundedRectGraphic[3];
            bgs[0] = MakeSegButton(card, new Vector2(x, rowY), btnW, a);
            bgs[1] = MakeSegButton(card, new Vector2(x + (btnW + gap), rowY), btnW, b);
            bgs[2] = MakeSegButton(card, new Vector2(x + (btnW + gap) * 2f, rowY), btnW, c);
            Action refresh = () =>
            {
                int sel = getI();
                var on = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f);
                var off = new Color(1f, 1f, 1f, 0.05f);
                for (int i = 0; i < 3; i++) if (bgs[i] != null) bgs[i].color = (i == sel) ? on : off;
            };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                UI.ClickHandler.Attach(bgs[i].gameObject, () => { setI(idx); refresh(); });
            }
            refresh();
        }

        // Rebuild the dialog in place (used when the Pseudo checkbox swaps the field set).
        private static void RebuildDialog()
        {
            CloseDialog();
            OpenDialog();
        }

        private static void Generate()
        {
            if (_pseudoMode)
            {
                int perRound = ParseInt(_fPerRound, 6);
                int interval = ParseInt(_fInterval, 4);
                double pseudoAngle = ParseDouble(_fPseudoAngle, 22.5);
                GenerateMagicshape(perRound, interval, pseudoAngle, _midspinPseudos, _keepBpm, _innerAngle);
            }
            else
            {
                double degrees = ParseDouble(_fDegrees, 360.0);
                int tileCount = ParseInt(_fTileCount, 24);
                GenerateCircle(degrees, tileCount, _keepBpm, _innerAngle);
            }
        }

        // Plain circle path: tileCount tiles trace `degrees` as a monotonic facing sequence
        // (each ±step, step = degrees/tileCount). Outer (Reverse off) = -step, Inner = +step
        // (same convention as the mid-spin circle). KeepBpm makes each tile one base beat.
        private static void GenerateCircle(double degrees, int tileCount, bool keepBpm, bool innerAngle)
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;
            int buildSeq;
            try
            {
                if (ed.lockPathEditing) { SapphireLog.Log("Toolbar: path editing is locked"); return; }
                if (!ed.SelectionIsSingle())
                { SapphireLog.Log("Toolbar: select exactly one tile to build the shape from"); return; }
                buildSeq = ed.selectedFloors[0].seqID;
            }
            catch { return; }

            tileCount = Mathf.Clamp(tileCount, 1, 20000);
            if (System.Math.Abs(degrees) < 0.01) degrees = 360.0;
            double step = degrees / tileCount;
            double signedStep = innerAngle ? step : -step;
            double dir; try { dir = ed.selectedFloors[0].floatDirection; } catch { dir = 0.0; }

            try
            {
                using (new SaveStateScope(ed))
                {
                    double a = 0.0;
                    for (int i = 0; i < tileCount; i++) { a += signedStep; AppendAbs(ed, dir + a); }
                    double m = (180.0 - signedStep) / 180.0;
                    if (keepBpm && m > 0.01)
                    {
                        int lastSeq = ed.selectedFloors.Count > 0 ? ed.selectedFloors[0].seqID : buildSeq;
                        AddSetSpeed(ed, buildSeq, m);
                        if (lastSeq > buildSeq) AddSetSpeed(ed, lastSeq, 1.0 / m);
                        try { ed.RemakePath(true, true); } catch { }
                    }
                }
            }
            catch (Exception ex) { SapphireLog.Log("Toolbar: generate circle failed: " + ex); }
        }

        // ── generation ──────────────────────────────────────────────────────
        // Reproduces teoookr's "Star Calculator" magicshape from its own inputs: `perRound`
        // pseudo points (P), `interval` tiles between them (I), pseudo angle A. Regular-tile
        // relative angle R = 180 + (180 - 360/P)/I; each point is [A, R-A, R × (I-1)] — a pseudo
        // of angle A, its completion R-A, and I-1 base tiles. P points per generate. Emitting a
        // relative angle `rel` advances the absolute heading by 180-rel, reproducing the tool's
        // .adofai output exactly (P=5,I=4,A=30 → 30,177,207,207,207 × 5). Reverse (inner) uses
        // A -> 360-A. Mid-spin makes [A,R-A] a single 999. KeepBpm scales the run so each
        // regular tile / pseudo pair (both = R) is one base beat: SetSpeed ×(R/180) on the
        // anchor tile, inverse on the last tile.
        private static void GenerateMagicshape(int perRound, int interval, double pseudoAngle,
            bool midspin, bool keepBpm, bool innerAngle)
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;
            int buildSeq;
            try
            {
                if (ed.lockPathEditing) { SapphireLog.Log("Toolbar: path editing is locked"); return; }
                if (!ed.SelectionIsSingle())
                { SapphireLog.Log("Toolbar: select exactly one tile to build the shape from"); return; }
                buildSeq = ed.selectedFloors[0].seqID;
            }
            catch { return; }

            perRound = Mathf.Clamp(perRound, 2, 720);
            interval = Mathf.Clamp(interval, 1, 200);

            // Regular-tile relative angle: solved so each point nets 360/perRound (verified vs
            // the tool: P=5,I=4→207; P=4→202.5; P=3→195). Old 180+180/P ignored the interval.
            double R = 180.0 + (180.0 - 360.0 / perRound) / interval;
            // Reverse flips the spike inward while keeping the point's net advance (so the base
            // circle is unchanged): pseudo angle A -> 360-A, completion stays R-A.
            double A = innerAngle ? 360.0 - pseudoAngle : pseudoAngle;

            // Track the heading ourselves for the star (AppendRel); the midspin path writes
            // absolute facings, so it only needs the selected tile's starting facing.
            double dir; try { dir = ed.selectedFloors[0].floatDirection; } catch { dir = 0.0; }

            try
            {
                using (new SaveStateScope(ed)) // whole batch = one undo (changingState nests)
                {
                    if (midspin)
                    {
                        // Reference mid-spin circle (matches a real .adofai): a smooth circle of
                        // perRound×interval tiles whose facing rises by `step` each, with a
                        // [tab, midspin] pair before every group of `interval`. The tab follows
                        // the pseudo angle — its facing = running + (180 - pseudoAngle) — so it
                        // spikes out at that angle, not perpendicular.
                        double step = 360.0 / (perRound * interval);
                        // Outer (Reverse off) = -step (facing decreases, per the outermidspin
                        // reference); Inner = +step. The tab facing (+180-pseudoAngle) is the same.
                        double signedStep = innerAngle ? step : -step;
                        double a = 0.0;
                        for (int p = 0; p < perRound; p++)
                        {
                            AppendAbs(ed, dir + a + (180.0 - pseudoAngle)); // tab
                            AppendMidspin(ed);                              // spin on its tip
                            for (int g = 0; g < interval; g++) { a += signedStep; AppendAbs(ed, dir + a); }
                        }
                        if (keepBpm)
                        {
                            double m = (180.0 - signedStep) / 180.0; // each smooth tile = one base beat
                            if (m > 0.01)
                            {
                                int lastSeq = ed.selectedFloors.Count > 0 ? ed.selectedFloors[0].seqID : buildSeq;
                                AddSetSpeed(ed, buildSeq, m);   // the SELECTED tile (not the midspin/first tab)
                                if (lastSeq > buildSeq) AddSetSpeed(ed, lastSeq, 1.0 / m);
                                try { ed.RemakePath(true, true); } catch { }
                            }
                        }
                    }
                    else
                    {
                        for (int g = 0; g < perRound; g++)
                        {
                            dir = AppendRel(ed, A, dir);       // sharp star point (in/out)
                            dir = AppendRel(ed, R - A, dir);   // its completion
                            for (int k = 0; k < interval - 1; k++)
                                dir = AppendRel(ed, R, dir);   // base-arc tiles
                        }
                        if (keepBpm && R > 1.0)
                        {
                            int lastSeq = ed.selectedFloors.Count > 0 ? ed.selectedFloors[0].seqID : buildSeq;
                            double m = R / 180.0;
                            AddSetSpeed(ed, buildSeq, m);                        // anchor → each tile one base beat
                            if (lastSeq > buildSeq) AddSetSpeed(ed, lastSeq, 1.0 / m); // last shape tile → restore
                            try { ed.RemakePath(true, true); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { SapphireLog.Log("Toolbar: generate failed: " + ex); }
        }

        // Append a tile at ADOFAI relative (charter) angle `rel` given the tracked absolute
        // heading `dir`: new absolute = dir ± (180-rel), sign from the current floor's isCCW
        // (same math as CreateArbitraryFloor). Returns the new heading. `dir` is tracked by the
        // caller so a midspin — whose floatDirection reads back as 999 — stays transparent.
        private static double AppendRel(scnEditor ed, double rel, double dir)
        {
            var sel = ed.selectedFloors;
            bool ccw = sel != null && sel.Count > 0 && sel[0].isCCW;
            double a = dir + (ccw ? -(180.0 - rel) : (180.0 - rel));
            ed.CreateFloorWithCharOrAngle((float)a, ArbitraryChar, false, false);
            return a;
        }

        // Insert a tile at an ABSOLUTE facing (angleData value), bypassing the relative/isCCW
        // math — used by the reference-matched mid-spin circle, whose facings are a monotonic
        // sequence the game reads directly.
        private static void AppendAbs(scnEditor ed, double abs)
        {
            ed.CreateFloorWithCharOrAngle((float)abs, ArbitraryChar, false, false);
        }

        // A SetSpeed (multiplier) event on the given floor, added straight to the editor's
        // event array; the caller RemakePaths once to apply. The game reads speedType via
        // get_Item + `unbox.any SpeedType`, so it MUST be the boxed SpeedType enum (a string
        // throws / falls back to Bpm mode, which just recolours the tiles by a broken speed —
        // the "changes the track colour instead of a SetSpeed" symptom).
        private static void AddSetSpeed(scnEditor ed, int floorSeq, double multiplier)
        {
            try
            {
                var ev = new ADOFAI.LevelEvent(floorSeq, ADOFAI.LevelEventType.SetSpeed);
                ev["speedType"] = SpeedType.Multiplier;
                ev["bpmMultiplier"] = (float)multiplier;
                ed.events.Add(ev);
            }
            catch (Exception ex) { SapphireLog.Log("Toolbar: add SetSpeed failed: " + ex.Message); }
        }

        private static void AppendMidspin(scnEditor ed)
        {
            ed.CreateFloorWithCharOrAngle(MidspinAngle, '!', false, true);
        }

        // ── small UI helpers ────────────────────────────────────────────────

        private static TextMeshProUGUI MakeLabel(Transform card, string text, float x, float y,
            float size, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(card, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(200f, 24f);
            return UIBuilder.Tmp(go, text, size, anchor, color);
        }

        private static TMP_InputField MakeLabeledField(Transform card, string label, float y,
            string initial, float cardW, float padX)
        {
            MakeLabel(card, label, padX, y, 14f, Theme.Text, TextAnchor.MiddleLeft)
                .rectTransform.sizeDelta = new Vector2(cardW - padX * 2f - 120f, 24f);

            var boxGo = new GameObject("Field", typeof(RectTransform));
            boxGo.transform.SetParent(card, false);
            var box = (RectTransform)boxGo.transform;
            box.anchorMin = box.anchorMax = new Vector2(1f, 1f);
            box.pivot = new Vector2(1f, 1f);
            box.anchoredPosition = new Vector2(-padX, y - 1f);
            box.sizeDelta = new Vector2(110f, 26f);
            var bg = boxGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = new Color(1f, 1f, 1f, 0.07f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;

            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(boxGo.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(8f, 0f); tr.offsetMax = new Vector2(-8f, 0f);
            var txt = UIBuilder.Tmp(txtGo, initial, 14f, TextAnchor.MiddleLeft, Theme.Text);
            txt.richText = false;

            var input = UIBuilder.BuildInputField(boxGo, txt);
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.text = initial;
            return input;
        }

        private static void MakeCheckbox(Transform card, string label, float x, float y, float cardW,
            Func<bool> get, Action<bool> set)
        {
            var rowGo = new GameObject("CheckRow", typeof(RectTransform));
            rowGo.transform.SetParent(card, false);
            var rr = (RectTransform)rowGo.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(0f, 1f);
            rr.pivot = new Vector2(0f, 1f);
            rr.anchoredPosition = new Vector2(x, y);
            rr.sizeDelta = new Vector2(cardW - x * 2f, 22f);
            // Invisible hit area over box + label.
            var hit = rowGo.AddComponent<RoundedRectGraphic>();
            hit.Radius = 4f;
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            var boxGo = new GameObject("Box", typeof(RectTransform));
            boxGo.transform.SetParent(rowGo.transform, false);
            var br = (RectTransform)boxGo.transform;
            br.anchorMin = br.anchorMax = new Vector2(0f, 0.5f);
            br.pivot = new Vector2(0f, 0.5f);
            br.anchoredPosition = new Vector2(0f, 0f);
            br.sizeDelta = new Vector2(18f, 18f);
            var boxBg = boxGo.AddComponent<RoundedRectGraphic>();
            boxBg.Radius = 4f;
            boxBg.color = new Color(1f, 1f, 1f, 0.07f);
            boxBg.BorderWidth = 1f;
            boxBg.BorderColor = new Color(1f, 1f, 1f, 0.2f);
            boxBg.raycastTarget = false;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(boxGo.transform, false);
            var fr = (RectTransform)fillGo.transform;
            fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one;
            fr.offsetMin = new Vector2(3f, 3f); fr.offsetMax = new Vector2(-3f, -3f);
            var fill = fillGo.AddComponent<RoundedRectGraphic>();
            fill.Radius = 2f;
            fill.color = get() ? Theme.Accent : new Color(0f, 0f, 0f, 0f);
            fill.raycastTarget = false;

            var lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(rowGo.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(1f, 1f);
            lr.offsetMin = new Vector2(26f, 0f); lr.offsetMax = new Vector2(0f, 0f);
            UIBuilder.Tmp(lblGo, label, 14f, TextAnchor.MiddleLeft, Theme.Text);

            UI.ClickHandler.Attach(rowGo, () =>
            {
                bool nv = !get();
                // Update the fill BEFORE set(): a set() that rebuilds the dialog destroys this
                // graphic, and touching it afterward would throw.
                fill.color = nv ? Theme.Accent : new Color(0f, 0f, 0f, 0f);
                set(nv);
            });
        }

        // The Star Calculator's "Reverse" control: a labelled two-option segmented toggle.
        // `getB` true = the second option (Inner) is selected.
        private static void MakeSegmented(Transform card, string label, string optA, string optB,
            float x, float y, float cardW, Func<bool> getB, Action<bool> setB)
        {
            MakeLabel(card, label, x, y, 12f, Theme.TextMuted, TextAnchor.UpperLeft)
                .rectTransform.sizeDelta = new Vector2(cardW - x * 2f, 16f);

            float rowY = y - 18f, gap = 8f;
            float btnW = (cardW - x * 2f - gap) / 2f;
            RoundedRectGraphic bgA = MakeSegButton(card, new Vector2(x, rowY), btnW, optA);
            RoundedRectGraphic bgB = MakeSegButton(card, new Vector2(x + btnW + gap, rowY), btnW, optB);
            Action refresh = () =>
            {
                bool b = getB();
                var on = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f);
                var off = new Color(1f, 1f, 1f, 0.05f);
                if (bgA != null) bgA.color = b ? off : on;
                if (bgB != null) bgB.color = b ? on : off;
            };
            UI.ClickHandler.Attach(bgA.gameObject, () => { setB(false); refresh(); });
            UI.ClickHandler.Attach(bgB.gameObject, () => { setB(true); refresh(); });
            refresh();
        }

        private static RoundedRectGraphic MakeSegButton(Transform card, Vector2 pos, float width, string text)
        {
            var go = new GameObject("Seg", typeof(RectTransform));
            go.transform.SetParent(card, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = pos;
            r.sizeDelta = new Vector2(width, 30f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 7f;
            bg.color = new Color(1f, 1f, 1f, 0.05f);
            bg.raycastTarget = true;
            var lblGo = new GameObject("L", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;
            UIBuilder.Tmp(lblGo, text, 13f, TextAnchor.MiddleCenter, Theme.Text);
            return bg;
        }

        private enum BtnKind { Neutral, Positive }

        private static float MakeButton(Transform card, string label, BtnKind kind, Vector2 topRight, Action onClick)
        {
            var go = new GameObject("Btn", typeof(RectTransform));
            go.transform.SetParent(card, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(1f, 1f);
            r.anchoredPosition = topRight;

            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 9f;
            bg.BorderWidth = 1f;
            if (kind == BtnKind.Positive)
            {
                bg.color = new Color(0.35f, 0.8f, 0.5f, 0.18f);
                bg.BorderColor = new Color(0.45f, 0.85f, 0.58f, 0.8f);
            }
            else
            {
                bg.color = new Color(1f, 1f, 1f, 0.06f);
                bg.BorderColor = new Color(1f, 1f, 1f, 0.16f);
            }
            bg.raycastTarget = true;

            var lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;
            var t = UIBuilder.Tmp(lblGo, label, 14f, TextAnchor.MiddleCenter, Theme.Text);

            float wBtn = Mathf.Max(90f, Mathf.Ceil(t.GetPreferredValues(label).x) + 34f);
            r.sizeDelta = new Vector2(wBtn, 32f);
            UI.ClickHandler.Attach(go, onClick);
            return wBtn;
        }

        private static int ParseInt(TMP_InputField f, int fallback)
        {
            if (f == null) return fallback;
            return int.TryParse(f.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static double ParseDouble(TMP_InputField f, double fallback)
        {
            if (f == null) return fallback;
            return double.TryParse(f.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private class CellHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public RoundedRectGraphic Bg;
            public Color Base = new Color(1f, 1f, 1f, 0.05f);
            public string Tip;
            public void OnPointerEnter(PointerEventData e)
            {
                if (Bg != null) Bg.color = new Color(
                    UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.22f);
                if (!string.IsNullOrEmpty(Tip)) ShowToolTip(Tip);
            }
            public void OnPointerExit(PointerEventData e)
            {
                if (Bg != null) Bg.color = Base;
                HideToolTip();
            }
        }
    }
}

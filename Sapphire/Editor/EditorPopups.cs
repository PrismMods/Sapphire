using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sapphire
{
    /* Sapphire messagebox replacing the game's editor popups (unsaved changes, confirm,
       ok…). scnEditor.popupWindow is one shared card whose children are the individual
       popups; ShowPopup activates exactly one. We mirror the ACTIVE child's message text
       and buttons into a Sapphire card and fade the game's window underneath — labels,
       localization, and keyboard shortcuts (Enter/Esc hit the game's own buttons) come
       free. Popups with interactive content (URL input, copyright scroller) are left to
       the dark skin: mirroring only fits message+buttons dialogs. */
    internal static class EditorPopups
    {
        private static GameObject _canvasGo;
        private static GameObject _cardGo;
        private static CanvasGroup _fadedWindow;
        private static int _sig;
        private static int _cooldown;
        private static readonly List<Button> _btns = new List<Button>();
        private static readonly StringBuilder _msg = new StringBuilder();

        private const float CardW = 460f, PadX = 24f, PadTop = 26f, BtnH = 34f, BtnGap = 10f;

        internal static void Tick()
        {
            var s = MainClass.Settings;
            bool enabled = s != null && MainClass.EditorSuiteOn && s.EditorPopupBox;
            scnEditor ed = null;
            bool inEd = false;
            try
            {
                ed = scnEditor.instance;
                inEd = ed != null && !ed.playMode;
            }
            catch { }
            if (!inEd || !enabled)
            {
                HideBox();
                if (ed == null || !enabled) RestoreWindow();
                return;
            }

            GameObject win = null;
            try { win = ed.popupWindow; } catch { }
            if (win == null || !win.activeInHierarchy)
            {
                HideBox();
                RestoreWindow();
                return;
            }

            Transform panel = null;
            var winTr = win.transform;
            for (int i = 0; i < winTr.childCount; i++)
                if (winTr.GetChild(i).gameObject.activeSelf) { panel = winTr.GetChild(i); break; }
            if (panel == null) { HideBox(); return; }

            // Interactive popups keep the game's own UI (mirroring would strand the input).
            if (HasInteractiveContent(panel)) { HideBox(); RestoreWindow(); return; }

            FadeWindow(win);
            if (--_cooldown <= 0)
            {
                _cooldown = 10;
                SyncContent(panel);
            }
        }

        internal static void Dispose()
        {
            RestoreWindow();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _cardGo = null; _btns.Clear(); _sig = 0; _cooldown = 0;
        }

        private static void HideBox()
        {
            if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
        }

        private static int _hicId, _hicChildren = -1;
        private static bool _hicResult;

        /* Five whole-subtree walks, and this ran every frame the popup was visible even though
           a popup's composition is fixed once it is up. The game reuses one popup object, so
           the cache keys on identity AND child count — swapping the popup's content changes
           one or the other. */
        private static bool HasInteractiveContent(Transform panel)
        {
            try
            {
                int id = panel.GetInstanceID(), cc = panel.childCount;
                if (id == _hicId && cc == _hicChildren) return _hicResult;
                _hicId = id; _hicChildren = cc;
                _hicResult = panel.GetComponentInChildren<TMP_InputField>(false) != null
                    || panel.GetComponentInChildren<InputField>(false) != null
                    || panel.GetComponentInChildren<ScrollRect>(false) != null
                    || panel.GetComponentInChildren<Toggle>(false) != null
                    || panel.GetComponentInChildren<Slider>(false) != null;
                return _hicResult;
            }
            catch { return true; } // when unsure, leave the game's popup alone
        }

        // ── content mirror ──────────────────────────────────────────────────

        private static void SyncContent(Transform panel)
        {
            _btns.Clear();
            foreach (var b in panel.GetComponentsInChildren<Button>(false))
                if (b != null && b.gameObject.activeInHierarchy) _btns.Add(b);

            _msg.Length = 0;
            foreach (var t in panel.GetComponentsInChildren<TMP_Text>(false))
            {
                if (t == null || string.IsNullOrEmpty(t.text)) continue;
                if (t.GetComponentInParent<Button>() != null) continue; // button labels
                if (_msg.Length > 0) _msg.Append('\n');
                _msg.Append(t.text.Trim());
            }
            string message = _msg.ToString();
            if (_btns.Count == 0 || message.Length == 0) { HideBox(); return; }

            int sig = panel.GetInstanceID() * 397 ^ message.GetHashCode();
            for (int i = 0; i < _btns.Count; i++)
            {
                sig = sig * 31 + _btns[i].GetInstanceID();
                sig = sig * 31 + GameLabel(_btns[i]).GetHashCode();
            }
            if (sig != _sig || _cardGo == null)
            {
                _sig = sig;
                BuildCard(message);
            }
            if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);
        }

        private static void BuildCard(string message)
        {
            if (_canvasGo == null) BuildCanvas();
            if (_cardGo != null) UnityEngine.Object.Destroy(_cardGo);

            _cardGo = new GameObject("Card", typeof(RectTransform));
            _cardGo.transform.SetParent(_canvasGo.transform, false);
            var card = (RectTransform)_cardGo.transform;
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = new Vector2(0f, 30f); // slightly above center, like the game

            var bg = _cardGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 14f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.98f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;

            // Message, wrapped to the card width.
            var msgGo = new GameObject("Message", typeof(RectTransform));
            msgGo.transform.SetParent(_cardGo.transform, false);
            var mr = (RectTransform)msgGo.transform;
            mr.anchorMin = new Vector2(0f, 1f);
            mr.anchorMax = new Vector2(1f, 1f);
            mr.pivot = new Vector2(0.5f, 1f);
            mr.offsetMin = new Vector2(PadX, 0f);
            mr.offsetMax = new Vector2(-PadX, 0f);
            mr.anchoredPosition = new Vector2(0f, -PadTop);
            var msg = msgGo.AddComponent<TextMeshProUGUI>();
            msg.font = UI.Theme.TmpFont;
            msg.fontSize = 15;
            msg.color = new Color(0.93f, 0.93f, 0.94f, 1f);
            msg.alignment = TextAlignmentOptions.TopLeft;
            msg.textWrappingMode = TextWrappingModes.Normal;
            msg.raycastTarget = false;
            msg.text = message;
            float msgH = Mathf.Ceil(msg.GetPreferredValues(message, CardW - PadX * 2f, 0f).y);
            mr.sizeDelta = new Vector2(mr.sizeDelta.x, msgH);

            // Buttons right-to-left so the game's right-most (primary) stays right-most.
            float x = -PadX;
            float btnY = -(PadTop + msgH + 22f);
            for (int i = _btns.Count - 1; i >= 0; i--)
            {
                var gameBtn = _btns[i];
                string label = GameLabel(gameBtn);
                var kind = Classify(gameBtn);
                float w = MakeButton(label, kind, new Vector2(x, btnY), gameBtn);
                x -= w + BtnGap;
            }

            card.sizeDelta = new Vector2(CardW, PadTop + msgH + 22f + BtnH + 22f);
        }

        private enum BtnKind { Neutral, Positive, Danger }

        // Semantic color from the game's own button tint (pastel green = confirm,
        // pastel pink = destructive, grey — possibly skin-darkened — = neutral).
        private static BtnKind Classify(Button b)
        {
            try
            {
                var img = b.targetGraphic as Image;
                if (img == null) return BtnKind.Neutral;
                // Effective resting tint = graphic × normal state (the skin, and possibly
                // the game, keep the hue in either factor).
                var c = img.color;
                if (b.transition == Selectable.Transition.ColorTint) c *= b.colors.normalColor;
                float h, sv, v;
                Color.RGBToHSV(c, out h, out sv, out v);
                if (sv < 0.15f) return BtnKind.Neutral;
                if (h > 0.2f && h < 0.5f) return BtnKind.Positive;
                return BtnKind.Danger;
            }
            catch { return BtnKind.Neutral; }
        }

        private static float MakeButton(string label, BtnKind kind, Vector2 topRight, Button gameBtn)
        {
            var go = new GameObject("Btn", typeof(RectTransform));
            go.transform.SetParent(_cardGo.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(1f, 1f);
            r.anchoredPosition = topRight;

            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 9f;
            bg.BorderWidth = 1f;
            switch (kind)
            {
                case BtnKind.Positive:
                    bg.color = new Color(0.35f, 0.8f, 0.5f, 0.18f);
                    bg.BorderColor = new Color(0.45f, 0.85f, 0.58f, 0.8f);
                    break;
                case BtnKind.Danger:
                    bg.color = new Color(1f, 0.35f, 0.4f, 0.13f);
                    bg.BorderColor = new Color(1f, 0.45f, 0.5f, 0.75f);
                    break;
                default:
                    bg.color = new Color(1f, 1f, 1f, 0.06f);
                    bg.BorderColor = new Color(1f, 1f, 1f, 0.16f);
                    break;
            }
            bg.raycastTarget = true;

            var lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;
            var t = lblGo.AddComponent<TextMeshProUGUI>();
            t.font = UI.Theme.TmpFont;
            t.fontSize = 14;
            t.color = new Color(0.93f, 0.93f, 0.94f, 1f);
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.text = label;

            float w = Mathf.Max(92f, Mathf.Ceil(t.GetPreferredValues(label).x) + 34f);
            r.sizeDelta = new Vector2(w, BtnH);

            UI.ClickHandler.Attach(go, () => EditorChrome.ProxyClick(gameBtn.gameObject));
            return w;
        }

        private static string GameLabel(Button b)
        {
            try
            {
                var tmp = b.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null && !string.IsNullOrEmpty(tmp.text)) return tmp.text.Trim();
                var txt = b.GetComponentInChildren<Text>(true);
                if (txt != null && !string.IsNullOrEmpty(txt.text)) return txt.text.Trim();
            }
            catch { }
            return "OK";
        }

        // ── fade / construction ─────────────────────────────────────────────

        private static void FadeWindow(GameObject win)
        {
            try
            {
                var cg = win.GetComponent<CanvasGroup>();
                if (cg == null) cg = win.AddComponent<CanvasGroup>();
                if (cg.alpha != 0f) { cg.alpha = 0f; cg.blocksRaycasts = false; } // interactable stays true
                _fadedWindow = cg;
            }
            catch { }
        }

        private static void RestoreWindow()
        {
            if (_fadedWindow == null) return;
            try { _fadedWindow.alpha = 1f; _fadedWindow.blocksRaycasts = true; _fadedWindow.interactable = true; }
            catch { }
            _fadedWindow = null;
        }

        private static void BuildCanvas()
        {
            _canvasGo = new GameObject("SapphirePopup", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 955; // confirm/prompt dialogs above windows/toolbar
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
        }
    }
}

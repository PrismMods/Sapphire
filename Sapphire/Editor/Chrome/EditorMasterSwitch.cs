using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Master on/off switch for the whole editor suite — an iOS-style slide toggle pinned to the
       editor's top-right corner, ALWAYS visible there (it's the way back on, so it must not gate
       on the flag it toggles). Flips Settings.EditorSuiteOn; every editor module's Tick gates on
       MainClass.EditorSuiteOn and restores the game UI when it goes false — no Ctrl+E needed. */
    internal static class EditorMasterSwitch
    {
        private const float TrackW = 46f, TrackH = 26f, Knob = 20f;
        private static readonly float KnobX = (TrackW - Knob) * 0.5f - 3f; // knob travel from centre

        private static GameObject _canvasGo;
        private static RoundedRectGraphic _track;
        private static RectTransform _knobRect;
        private static RoundedRectGraphic _knob;
        private static bool _shownOn = true;

        internal static void Tick()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool want = ed != null && !ed.playMode;
            if (!want)
            {
                if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
                return;
            }
            if (_canvasGo == null) Build();
            if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);
            if (_shownOn != MainClass.EditorSuiteOn) Sync();

            // iOS feel: the knob slides to its side instead of teleporting.
            if (_knobRect != null)
            {
                float target = _shownOn ? KnobX : -KnobX;
                float cur = _knobRect.anchoredPosition.x;
                // The knob is at rest almost always; skip the managed→native write once it has
                // snapped, instead of re-assigning the same position every frame forever.
                if (cur != target)
                {
                    float x = Mathf.Lerp(cur, target, Time.unscaledDeltaTime * 16f);
                    if (Mathf.Abs(x - target) < 0.25f) x = target;
                    _knobRect.anchoredPosition = new Vector2(x, 0f);
                }
            }
        }

        internal static void Dispose()
        {
            if (_canvasGo != null) Object.Destroy(_canvasGo);
            _canvasGo = null; _track = null; _knobRect = null; _knob = null;
        }

        private static void Build()
        {
            _canvasGo = new GameObject("SapphireMasterSwitch", typeof(RectTransform));
            Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 958; // above everything of Sapphire's (incl. top chrome)
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            var trackGo = new GameObject("Switch", typeof(RectTransform));
            trackGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)trackGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(1f, 1f);
            r.anchoredPosition = new Vector2(-12f, -12f);
            r.sizeDelta = new Vector2(TrackW, TrackH);

            var lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(_canvasGo.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = lr.anchorMax = new Vector2(1f, 1f);
            lr.pivot = new Vector2(1f, 1f);
            lr.anchoredPosition = new Vector2(-12f, -41f); // just below the track
            lr.sizeDelta = new Vector2(70f, 12f);
            var lbl = lblGo.AddComponent<TMPro.TextMeshProUGUI>();
            lbl.font = UI.Theme.TmpFont;
            lbl.fontSize = 10f;
            lbl.color = new Color(0.65f, 0.65f, 0.7f, 0.9f);
            lbl.alignment = TMPro.TextAlignmentOptions.MidlineRight;
            lbl.raycastTarget = false;
            lbl.text = "Sapphire";
            _track = trackGo.AddComponent<RoundedRectGraphic>();
            _track.Radius = TrackH * 0.5f;
            _track.BorderWidth = 1f;
            _track.raycastTarget = true;

            var knobGo = new GameObject("Knob", typeof(RectTransform));
            knobGo.transform.SetParent(trackGo.transform, false);
            _knobRect = (RectTransform)knobGo.transform;
            _knobRect.anchorMin = _knobRect.anchorMax = new Vector2(0.5f, 0.5f);
            _knobRect.pivot = new Vector2(0.5f, 0.5f);
            _knobRect.sizeDelta = new Vector2(Knob, Knob);
            _knob = knobGo.AddComponent<RoundedRectGraphic>();
            _knob.Radius = Knob * 0.5f;
            _knob.raycastTarget = false;

            UI.ClickHandler.Attach(trackGo, () =>
            {
                var s = MainClass.Settings;
                if (s == null) return;
                s.EditorSuiteOn = !s.EditorSuiteOn;
                Sync();
            });
            Sync();
            _knobRect.anchoredPosition = new Vector2(_shownOn ? KnobX : -KnobX, 0f); // no first-show slide
        }

        private static void Sync()
        {
            _shownOn = MainClass.EditorSuiteOn;
            if (_track != null)
            {
                _track.color = _shownOn
                    ? new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.9f)
                    : new Color(0.16f, 0.16f, 0.19f, 0.95f);
                _track.BorderColor = _shownOn
                    ? new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 1f)
                    : new Color(1f, 1f, 1f, 0.18f);
            }
            if (_knob != null)
                _knob.color = _shownOn
                    ? new Color(0.98f, 0.98f, 1f, 1f)
                    : new Color(0.62f, 0.62f, 0.66f, 1f);
        }
    }
}

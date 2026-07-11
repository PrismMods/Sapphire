using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Pitch modifier overlay, bottom-left above the timeline: Pitch  < [value] >  [Reset].
       NON-DESTRUCTIVE practice speed: overrides the live song audio pitch each frame WITHOUT
       touching the saved level (songSettings["pitch"] is left alone). Reset removes the
       override so playback returns to the level's own pitch. */
    internal static class EditorPitch
    {
        private static GameObject _canvasGo;
        private static RectTransform _rootRect;
        private static TMP_InputField _field;
        private static int _shown = int.MinValue;
        private static bool _override;        // practice override active?
        private static int _practicePitch = 100;

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            bool want = false;
            try { ed = scnEditor.instance; want = ed != null && s != null && MainClass.EditorSuiteOn && s.EditorPitchOverlay; }
            catch { }
            if (!want)
            {
                if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
                return;
            }
            if (_canvasGo == null) Build();
            if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);

            // Ride above the timeline strip (or hug the bottom when it's off).
            try
            {
                float y = 12f;
                float top = EditorEvents.BottomStripTop;
                if (top > 0f) y = top + 8f;
                var pos = new Vector2(12f, y);
                if (_rootRect.anchoredPosition != pos) _rootRect.anchoredPosition = pos;
            }
            catch { }

            // The override rides scnEditor.playbackSpeed — the game's OWN practice-speed lever.
            // The conductor multiplies it into song.pitch AND schedules hitsounds by it, so both
            // stay in sync natively (per-frame song.pitch writes missed the hitsounds entirely).
            if (_override)
            {
                try { if (ed != null && ed.playbackSpeed != _practicePitch / 100f) ed.playbackSpeed = _practicePitch / 100f; }
                catch { }
            }

            // Reflect the effective pitch unless the user is typing.
            try
            {
                int p = Effective(ed);
                if (p != _shown && _field != null && !_field.isFocused)
                {
                    _shown = p;
                    _field.text = p.ToString();
                }
            }
            catch { }
        }

        internal static void Dispose()
        {
            // Drop the override so the level's own pitch is restored on the way out.
            if (_override)
            {
                try { var ed = scnEditor.instance; if (ed != null) ed.playbackSpeed = 1f; } catch { }
            }
            _override = false;
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _rootRect = null; _field = null; _shown = int.MinValue;
        }

        // Called by the SetupConductorWithLevelData prefix — the one moment the game actually
        // reads playbackSpeed (it resets the field from its own control before that, which is
        // why per-frame writes never took).
        internal static void ImposePlaybackSpeed()
        {
            if (!_override) return;
            try
            {
                var ed = scnEditor.instance;
                if (ed != null) ed.playbackSpeed = _practicePitch / 100f;
            }
            catch { }
        }

        private static int LevelPitch()
        {
            try { return scnEditor.instance.levelData.pitch; } catch { return 100; }
        }

        // The practice speed currently in effect (as % — playbackSpeed multiplies the level pitch).
        private static int Effective(scnEditor ed)
        {
            if (_override) return _practicePitch;
            try { return Mathf.RoundToInt(ed.playbackSpeed * 100f); } catch { return 100; }
        }

        // Set a practice-speed override (does NOT modify the saved level pitch). Takes effect on
        // the next play start (the game schedules song + hitsounds from playbackSpeed then).
        private static void Apply(int p)
        {
            try
            {
                _practicePitch = Mathf.Clamp(p, 1, 1000);
                _override = true;
                var ed = scnEditor.instance;
                if (ed != null) ed.playbackSpeed = _practicePitch / 100f;
                _shown = _practicePitch;
                if (_field != null) _field.text = _practicePitch.ToString();
            }
            catch (Exception ex) { SapphireLog.Log("Pitch: apply failed: " + ex.Message); }
        }

        // Remove the override — playback returns to the level's own speed.
        private static void ResetPractice()
        {
            _override = false;
            try { var ed = scnEditor.instance; if (ed != null) ed.playbackSpeed = 1f; } catch { }
            _shown = int.MinValue; // force the field to refresh
        }

        private static void Nudge(int delta)
        {
            var ed = scnEditor.instance;
            Apply((ed != null ? Effective(ed) : _practicePitch) + delta);
        }

        // ── construction ────────────────────────────────────────────────────

        private static void Build()
        {
            _canvasGo = new GameObject("SapphirePitch", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 905;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            const float h = 30f, pad = 7f, btn = 26f, fieldW = 52f, gap = 5f, lblW = 38f, resetW = 44f;
            float width = pad * 2 + lblW + gap + btn + gap + fieldW + gap + btn + gap + resetW;

            var rootGo = new GameObject("PitchBar", typeof(RectTransform));
            rootGo.transform.SetParent(_canvasGo.transform, false);
            _rootRect = (RectTransform)rootGo.transform;
            _rootRect.anchorMin = _rootRect.anchorMax = new Vector2(0f, 0f);
            _rootRect.pivot = new Vector2(0f, 0f);
            _rootRect.anchoredPosition = new Vector2(12f, 12f);
            _rootRect.sizeDelta = new Vector2(width, h + pad * 2);
            var bg = rootGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 10f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.9f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;

            float x = pad;
            MakeLabel("Pitch", x, lblW, h);          x += lblW + gap;
            MakeButton("<", x, btn, h, () => Nudge(-10)); x += btn + gap;
            _field = MakeField(x, fieldW, h);        x += fieldW + gap;
            MakeButton(">", x, btn, h, () => Nudge(10));  x += btn + gap;
            MakeButton("Reset", x, resetW, h, ResetPractice);
        }

        private static void MakeLabel(string text, float x, float w, float h)
        {
            var go = UIBuilder.Rect("Lbl", _rootRect);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f);
            r.sizeDelta = new Vector2(w, h);
            UIBuilder.Tmp(go, text, 13f, TextAnchor.MiddleLeft, Theme.TextMuted);
        }

        private static void MakeButton(string text, float x, float w, float h, Action onClick)
        {
            var go = new GameObject("Btn", typeof(RectTransform));
            go.transform.SetParent(_rootRect, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f);
            r.sizeDelta = new Vector2(w, h);
            var b = go.AddComponent<RoundedRectGraphic>();
            b.Radius = 6f;
            b.color = new Color(1f, 1f, 1f, 0.06f);
            b.BorderWidth = 1f;
            b.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            b.raycastTarget = true;
            var lblGo = new GameObject("L", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;
            UIBuilder.Tmp(lblGo, text, 14f, TextAnchor.MiddleCenter, Theme.Text);
            UI.ClickHandler.Attach(go, onClick);
        }

        private static TMP_InputField MakeField(float x, float w, float h)
        {
            var go = new GameObject("Field", typeof(RectTransform));
            go.transform.SetParent(_rootRect, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f);
            r.sizeDelta = new Vector2(w, h);
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
            var txt = UIBuilder.Tmp(txtGo, "100", 13f, TextAnchor.MiddleCenter, Theme.Text);
            txt.richText = false;
            var input = UIBuilder.BuildInputField(go, txt);
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.text = "100";
            input.onEndEdit.AddListener(t =>
            {
                if (int.TryParse(t, out var v)) Apply(v);
            });
            return input;
        }
    }
}

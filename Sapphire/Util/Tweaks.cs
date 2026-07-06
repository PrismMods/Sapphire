using TMPro;
using UnityEngine;

namespace Sapphire
{
    // Editor behaviour helpers: rebindable autoplay-pause key, Editor Mode, tile angle.
    internal static class Tweaks
    {
        /* KeyCode (as int) the editor's autoplay-pause check should read. A transpiler in
           Patches.cs swaps the hardcoded KeyCode.Space (32) in scnEditor.Update for a call
           to this, so the pause key is rebindable. Falls back to Space so behaviour is
           unchanged when settings aren't loaded yet. */
        internal static int AutoPauseKeyCode()
        {
            try
            {
                var s = MainClass.Settings;
                if (s == null) return (int)KeyCode.Space;
                if (!s.AutoplayPauseEnabled) return (int)KeyCode.None;
                return (int)s.AutoplayPauseKey;
            }
            catch { return (int)KeyCode.Space; }
        }

        // ── Editor Mode ─────────────────────────────────────────────────────
        // Clean-screen charting: force autoplay on at play start (rising edge only) and
        // fade the editor's corner icons plus the play-test HUD icons/error meter. The
        // Sapphire mode cluster covers those toggles.
        private static bool _wasEditorPlay;
        private static bool _cornersFaded;
        private static int _cornerCooldown;

        internal static void TickEditorMode()
        {
            bool playing = false;
            scnEditor ed = null;
            bool active = false;
            try
            {
                ed = scnEditor.instance;
                playing = ed != null && ed.playMode;
                var s = MainClass.Settings;
                active = ed != null && s != null && s.EditorModeActive;
                if (playing && !_wasEditorPlay && active)
                    RDC.auto = true;
            }
            catch { }
            _wasEditorPlay = playing;

            if (active)
            {
                // Re-assert periodically: the editor recreates these on scene reloads.
                if (--_cornerCooldown <= 0)
                {
                    _cornerCooldown = 30;
                    FadeCorners(ed, true);
                }
                _cornersFaded = true;
            }
            else if (_cornersFaded)
            {
                _cornersFaded = false;
                _cornerCooldown = 0;
                FadeCorners(ed, false);
            }
        }

        private static void FadeCorners(scnEditor ed, bool fade)
        {
            if (ed != null)
            {
                try { FadeCorner(ed.editorDifficultySelector, fade); } catch { }
                try { FadeCorner(ed.speedIndicator, fade); } catch { }
                try { FadeCorner(ed.buttonNoFail, fade); } catch { }
            }
            // Play-test HUD: difficulty/no-fail/autoplay icon containers + hit error meter.
            try
            {
                var uic = scrUIController.instance;
                if (uic != null)
                {
                    FadeCorner(uic.difficultyContainer, fade);
                    FadeCorner(uic.modifiersContainer, fade);
                }
            }
            catch { }
            try
            {
                var c = scrController.instance;
                if (c != null && c.errorMeter != null) FadeCorner(c.errorMeter, fade);
            }
            catch { }
        }

        private static void FadeCorner(Component c, bool fade)
        {
            if (c == null) return;
            var cg = c.GetComponent<CanvasGroup>();
            if (cg == null)
            {
                if (!fade) return;
                cg = c.gameObject.AddComponent<CanvasGroup>();
            }
            float a = fade ? 0f : 1f;
            if (cg.alpha != a) { cg.alpha = a; cg.blocksRaycasts = !fade; }
        }

        // ── Editor: selected tile angle readout ────────────────────────────
        // Small own-canvas text near the top of the editor showing the angle of the last
        // selected tile (angleLength → degrees; 180° = straight).

        private static GameObject _angleGo;
        private static TextMeshProUGUI _angleText;

        internal static void TickTileAngle()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            bool want = false;
            try
            {
                if (s != null && s.EditorTileAngle)
                {
                    ed = scnEditor.instance;
                    want = ed != null && !ed.playMode
                        && ed.selectedFloors != null && ed.selectedFloors.Count > 0;
                }
            }
            catch { want = false; }

            if (!want)
            {
                if (_angleGo != null && _angleGo.activeSelf) _angleGo.SetActive(false);
                return;
            }
            if (_angleGo == null) BuildAngleDisplay();
            if (!_angleGo.activeSelf) _angleGo.SetActive(true);

            var fl = ed.selectedFloors[ed.selectedFloors.Count - 1];
            if (fl == null) return;
            float deg = (float)(fl.angleLength * Mathf.Rad2Deg);
            string txt = ed.selectedFloors.Count > 1
                ? $"Angle: {deg:0.##}°  ({ed.selectedFloors.Count} tiles)"
                : $"Angle: {deg:0.##}°";
            if (_angleText != null && _angleText.text != txt) _angleText.text = txt;
        }

        private static void BuildAngleDisplay()
        {
            _angleGo = new GameObject("SapphireTileAngle", typeof(RectTransform));
            Object.DontDestroyOnLoad(_angleGo);
            var canvas = _angleGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = _angleGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(_angleGo.transform, false);
            var rect = (RectTransform)txtGo.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.93f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(700f, 40f);
            _angleText = txtGo.AddComponent<TextMeshProUGUI>();
            _angleText.font = UI.Theme.TmpFont;
            _angleText.fontSize = 26;
            _angleText.color = Color.white;
            _angleText.alignment = TextAlignmentOptions.Center;
            _angleText.textWrappingMode = TextWrappingModes.NoWrap;
            _angleText.overflowMode = TextOverflowModes.Overflow;
            _angleText.raycastTarget = false;
            var sh = txtGo.AddComponent<TmpShadow>();
            sh.OffsetPx = new Vector2(2f, -2f);
            sh.Apply();
        }

        internal static void DisposeTileAngle()
        {
            if (_angleGo != null) Object.Destroy(_angleGo);
            _angleGo = null;
            _angleText = null;
        }
    }
}

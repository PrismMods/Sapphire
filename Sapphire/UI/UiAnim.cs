using UnityEngine;

namespace Sapphire.UI
{
    /* Open/close motion for surfaces that are not PanelKit windows — dropdowns, the timeline
       strip, anything that used to appear with a bare SetActive.

       PanelKit animates itself because Show is already called every frame by its owner, which
       gives it a clock for free. Everything else needs one, and the cheapest correct one is the
       caller: pass the state you want each frame and get back whether the object should still
       exist. No coroutines, nothing to cancel, and a surface that stops being ticked stops
       animating rather than hanging half-faded.

       Alpha and localScale only. Several of these live inside layouts that rewrite size and
       position every frame, and anything touching the rect would be overwritten or, worse, fight
       for it. */
    internal static class UiAnim
    {
        internal const float Sec = 0.11f;

        internal static bool Enabled
        {
            get
            {
                var s = MainClass.Settings;
                return s == null || s.UiAnimations;
            }
        }

        /* Advance `t` (0 hidden … 1 shown) toward `want` and paint it. Returns whether the object
           should remain active — false only once a close has finished, so the caller can keep
           deactivating it exactly where it used to. */
        internal static bool Step(GameObject go, bool want, ref float t, bool scale = true)
        {
            if (go == null) return want;
            if (!Enabled)
            {
                t = want ? 1f : 0f;
                Paint(go, 1f, scale);
                return want;
            }
            if (want && !go.activeSelf) { go.SetActive(true); t = 0f; }
            float d = Time.unscaledDeltaTime / Sec;
            t = Mathf.Clamp01(t + (want ? d : -d));
            // Ease out on the way in, in on the way out — the arrival is what reads as fluid.
            Paint(go, want ? 1f - (1f - t) * (1f - t) : t * t, scale);
            return want || t > 0f;
        }

        /* Scale is optional. On a small floating thing it reads as the thing arriving; on a
           full-width bar it reads as the whole strip lurching, because 1.5% of 1900px is 28px of
           travel against 1.5% of a 200px menu being three. Wide surfaces fade only. */
        private static void Paint(GameObject go, float k, bool scale)
        {
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            if (!Mathf.Approximately(cg.alpha, k)) cg.alpha = k;
            if (!scale) return;
            var t = go.transform;
            float sc = 0.985f + 0.015f * k;
            if (!Mathf.Approximately(t.localScale.x, sc)) t.localScale = new Vector3(sc, sc, 1f);
        }
    }
}

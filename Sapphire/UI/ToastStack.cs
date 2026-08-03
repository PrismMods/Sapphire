using System;
using UnityEngine;

namespace Sapphire.UI
{
    /* Top-right toast slotting across MODS.

       Quartz parks its update toast at the top-right corner (canvas "QuartzUpdateToastCanvas",
       card "UpdateToast", 24px inset). Sapphire's sits in exactly the same place, so with both
       mods installed the two cards drew on top of each other. Neither mod can reference the
       other, so they agree by CONVENTION instead:

         • the toast's own Canvas GameObject is named "<Mod>UpdateToastCanvas"
         • the moving card inside it is named "UpdateToast"

       Sapphire follows that shape both to FIND Quartz and to BE findable — a future mod applying
       the same rule will see us without either side taking a dependency.

       Measurement is in SCREEN PIXELS via GetWorldCorners (world == screen px for a
       ScreenSpaceOverlay canvas), so it doesn't matter what reference resolution or scaler the
       other mod's canvas uses — a 360×64 card at a different CanvasScaler still reports the
       screen box it actually covers.

       Ordering is by canvas name, and we only ever yield to names sorting BEFORE ours. If the
       other mod never implements this it simply stays put and we stack under it; if it adopts
       the identical rule the ordering is total and consistent, so the two can't both claim the
       same slot or chase each other down the screen forever. */
    internal static class ToastStack
    {
        internal const string CanvasSuffix = "UpdateToastCanvas";
        internal const string CardName = "UpdateToast";
        internal const float Gap = 8f;          // screen px between stacked cards

        // Scanning every Canvas is not free, so results are cached for a few frames. Toasts
        // appear on a human timescale; a ~0.2s lag in re-slotting is invisible.
        private const int RescanFrames = 12;
        private static int _lastScanFrame = -999;
        private static float _cached;
        private static string _cachedFor;

        /// Screen-px height already claimed by foreign toasts above us. Add it to the normal
        /// top inset to get where our card should sit.
        internal static float OffsetFor(string selfCanvasName)
        {
            if (_cachedFor == selfCanvasName && Time.frameCount - _lastScanFrame < RescanFrames)
                return _cached;
            _lastScanFrame = Time.frameCount;
            _cachedFor = selfCanvasName;
            _cached = Measure(selfCanvasName);
            return _cached;
        }

        private static float Measure(string selfCanvasName)
        {
            float used = 0f;
            Canvas[] canvases;
            try { canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(); }
            catch { return 0f; }

            var corners = new Vector3[4];
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas c = canvases[i];
                if (c == null) continue;
                GameObject go = c.gameObject;
                string name = go.name;
                if (!name.EndsWith(CanvasSuffix, StringComparison.Ordinal)) continue;
                if (name == selfCanvasName) continue;
                // Deterministic total order — see the class comment on why this must be a
                // strict one-way test rather than "anything that isn't me".
                if (string.CompareOrdinal(name, selfCanvasName) >= 0) continue;

                RectTransform card = FindCard(go.transform);
                if (card == null || !card.gameObject.activeInHierarchy) continue;
                if (FadedOut(card)) continue;

                card.GetWorldCorners(corners);   // 0 BL, 1 TL, 2 TR, 3 BR
                float left = corners[0].x, height = corners[1].y - corners[0].y;
                if (height <= 1f) continue;
                // Cards slide off the right edge to hide and stay active through the tween;
                // one that has left the screen is not occupying a slot.
                if (left >= Screen.width - 1f) continue;
                used += height + Gap;
            }
            return used;
        }

        private static bool FadedOut(RectTransform card)
        {
            var cg = card.GetComponent<CanvasGroup>();
            return cg != null && cg.alpha < 0.05f;
        }

        private static RectTransform FindCard(Transform root)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform t = root.GetChild(i);
                if (t.name == CardName) return t as RectTransform;
            }
            // Fall back to the canvas itself if a mod put the card at the canvas root.
            return root as RectTransform;
        }
    }
}

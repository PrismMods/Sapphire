using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sapphire.UI
{
    /* Floating option list for the editor overlay panels (event inspector / level settings).
       Replaces click-to-cycle value cells: clicking opens the FULL list (current value marked)
       so you can see and pick, instead of stepping blind. Own high-order canvas above the
       panels; a full-screen blocker closes it on click-outside. Anchored under the trigger cell,
       flips above it when there's no room, scrolls past ~12 rows. */
    internal static class EditorDropdown
    {
        private const float RowH = 24f, Pad = 4f, MaxRows = 12f;
        private static GameObject _canvasGo, _rootGo;
        private static RectTransform _dropRoot;
        private static RectTransform _trigger;   // the cell it dropped from; drives auto-close

        internal static bool IsOpen => _rootGo != null;

        // The blocker is full-screen and raycast-catching (order 957), so a stranded dropdown eats
        // the next world/tile click. It self-closes only on pick or blocker-click — so also close it
        // here the moment its trigger goes away: panel hidden (SetActive false → not activeInHierarchy),
        // selection changed / content rebuilt (cell destroyed → == null), or Esc. One backstop covers
        // every opener (event inspector, level settings) without each needing to remember to close it.
        internal static void Tick()
        {
            if (_rootGo == null) return;
            bool gone = _trigger == null || !_trigger.gameObject.activeInHierarchy;
            if (gone || Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        internal static void Close()
        {
            if (_rootGo != null) UnityEngine.Object.Destroy(_rootGo);
            _rootGo = null;
            _trigger = null;
        }

        internal static void Dispose()
        {
            Close();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _dropRoot = null;
        }

        // trigger = the value cell the dropdown drops from. options/optionLabels are 1:1.
        internal static void Open(RectTransform trigger, IList<string> options, int current, Action<int> onPick)
        {
            Close();
            if (trigger == null || options == null || options.Count == 0) return;
            EnsureCanvas();
            _trigger = trigger;

            _rootGo = new GameObject("Dropdown", typeof(RectTransform));
            _rootGo.transform.SetParent(_dropRoot, false);
            var rr = (RectTransform)_rootGo.transform;
            rr.anchorMin = Vector2.zero; rr.anchorMax = Vector2.one;
            rr.offsetMin = Vector2.zero; rr.offsetMax = Vector2.zero;
            var blk = _rootGo.AddComponent<Image>();
            blk.sprite = Theme.White; blk.color = new Color(0f, 0f, 0f, 0f); blk.raycastTarget = true;
            ClickHandler.Attach(_rootGo, Close);

            // Trigger geometry in the dropRoot's top-left space (SSO world corners == screen px).
            var corners = new Vector3[4];
            trigger.GetWorldCorners(corners); // 0 BL, 1 TL, 2 TR, 3 BR
            Vector2 bl, tl;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_dropRoot, corners[0], null, out bl);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_dropRoot, corners[1], null, out tl);
            float width = Mathf.Abs(tl.x - bl.x) < 1f ? trigger.rect.width : Mathf.Max(120f, corners[3].x - corners[0].x);
            // width from screen px → local units via the difference of two screen X mapped to local
            Vector2 br;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_dropRoot, corners[3], null, out br);
            width = Mathf.Max(120f, br.x - bl.x);

            int n = options.Count;
            float natural = n * RowH + Pad * 2f;
            float h = Mathf.Min(natural, MaxRows * RowH + Pad * 2f);
            float rootH = _dropRoot.rect.height;
            bool openUp = (bl.y - h) < -rootH + 4f;   // no room below → flip above
            float topY = openUp ? tl.y + h : bl.y;    // panel top edge (y negative-down space)

            var panelGo = new GameObject("List", typeof(RectTransform));
            panelGo.transform.SetParent(_rootGo.transform, false);
            var pr = (RectTransform)panelGo.transform;
            pr.anchorMin = pr.anchorMax = new Vector2(0f, 1f);
            pr.pivot = new Vector2(0f, 1f);
            pr.anchoredPosition = new Vector2(bl.x, topY);
            pr.sizeDelta = new Vector2(width, h);
            var pbg = panelGo.AddComponent<RoundedRectGraphic>();
            pbg.Radius = 6f;
            pbg.color = new Color(0.10f, 0.10f, 0.12f, 0.98f);
            pbg.BorderWidth = 1f;
            pbg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            pbg.raycastTarget = true; // swallow clicks on the panel chrome

            // viewport (clips) + content (scrolls)
            var vpGo = new GameObject("VP", typeof(RectTransform));
            vpGo.transform.SetParent(panelGo.transform, false);
            var vp = (RectTransform)vpGo.transform;
            vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one;
            vp.offsetMin = new Vector2(1f, 1f); vp.offsetMax = new Vector2(-1f, -1f);
            var vpImg = vpGo.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0f); vpImg.raycastTarget = true;
            vpGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(vpGo.transform, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, 0f); content.offsetMax = new Vector2(0f, 0f);
            content.sizeDelta = new Vector2(0f, natural);

            for (int i = 0; i < n; i++)
            {
                int oi = i;
                var row = new GameObject("Opt", typeof(RectTransform));
                row.transform.SetParent(contentGo.transform, false);
                var rt = (RectTransform)row.transform;
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(0f, 0f); rt.offsetMax = new Vector2(0f, 0f);
                rt.anchoredPosition = new Vector2(0f, -Pad - oi * RowH);
                rt.sizeDelta = new Vector2(0f, RowH);
                var rbg = row.AddComponent<RoundedRectGraphic>();
                rbg.Radius = 4f;
                rbg.color = oi == current ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.30f)
                                          : new Color(0f, 0f, 0f, 0f);
                rbg.raycastTarget = true;
                row.AddComponent<HoverTint>().Init(rbg, oi == current);

                var lGo = new GameObject("L", typeof(RectTransform));
                lGo.transform.SetParent(row.transform, false);
                var lr = (RectTransform)lGo.transform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = new Vector2(10f, 0f); lr.offsetMax = new Vector2(-8f, 0f);
                var lt = UIBuilder.Tmp(lGo, options[oi], 12f, TextAnchor.MiddleLeft,
                    oi == current ? Theme.Text : Theme.TextMuted);
                lt.raycastTarget = false;

                ClickHandler.Attach(row, () => { onPick?.Invoke(oi); Close(); });
            }

            if (natural > h)
            {
                var sr = panelGo.AddComponent<ScrollRect>();
                sr.viewport = vp; sr.content = content;
                sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped;
                sr.scrollSensitivity = 22f;
                // start scrolled so the current option is in view
                float maxScroll = Mathf.Max(0f, natural - h);
                float want = Mathf.Clamp(current * RowH - h * 0.5f, 0f, maxScroll);
                content.anchoredPosition = new Vector2(content.anchoredPosition.x, want);
            }
        }

        private static void EnsureCanvas()
        {
            if (_canvasGo != null) return;
            _canvasGo = new GameObject("SapphireDropdown", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 957; // above panels/popups/top chrome, below the master switch
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
            var rootGo = new GameObject("Root", typeof(RectTransform));
            rootGo.transform.SetParent(_canvasGo.transform, false);
            _dropRoot = (RectTransform)rootGo.transform;
            _dropRoot.anchorMin = Vector2.zero; _dropRoot.anchorMax = Vector2.one;
            _dropRoot.offsetMin = Vector2.zero; _dropRoot.offsetMax = Vector2.zero;
            _dropRoot.pivot = new Vector2(0f, 1f); // top-left space, matching anchoredPosition math
            // A just-created ScreenSpaceOverlay canvas hasn't been sized yet, so _dropRoot.rect is
            // (0,0) until the next canvas update — the FIRST Open would then read a zero rect and
            // place the list off-screen (looked like "the dropdown doesn't open"). Force the layout
            // now so the very first positioning math sees the real screen-sized root.
            Canvas.ForceUpdateCanvases();
        }

        // Row hover highlight: brightens on pointer-enter unless it's the selected row.
        private class HoverTint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            private RoundedRectGraphic _bg;
            private Color _rest;
            private static readonly Color Hover = new Color(1f, 1f, 1f, 0.10f);

            internal void Init(RoundedRectGraphic bg, bool selected)
            {
                _bg = bg;
                _rest = bg.color;
                if (selected) return; // keep the accent tint at rest
            }

            public void OnPointerEnter(PointerEventData e) { if (_bg != null) _bg.color = Hover; }
            public void OnPointerExit(PointerEventData e) { if (_bg != null) _bg.color = _rest; }
        }
    }
}

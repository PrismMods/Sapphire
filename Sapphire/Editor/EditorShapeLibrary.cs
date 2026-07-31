using System;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Shape Library panel: left list of ShapeLibrary.All, right pane previews the selected
       shape's simple form + each pseudo variant via ShapePathGraphic. Insert/Convert actions
       land in Tasks 4-5. Dockable focusable PanelKit, same scaffolding as EditorLevelMenu
       (shell/viewport/content + TickScroll/ClampScroll) minus the resize handle — this panel
       has no per-tab settings width dependency, so a fixed size is enough for now. */
    internal static class EditorShapeLibrary
    {
        private static readonly PanelKit K = new PanelKit("SapphireShapeLib", 903, 620f, focusable: true);
        private const float PanelW = 620f, RailW = 150f, HeaderH = 28f;
        private const float Pad = PanelKit.Pad;

        private static Vector2 _size = new Vector2(PanelW, 520f);
        private static RectTransform _viewport, _content, _railHost;
        private static float _scroll;
        private static int _sel;              // selected shape index
        private static bool _open;
        private static bool _selfChecked;

        internal static bool IsOpen => _open;
        internal static void Toggle() { _open = !_open; EditorToolbar.SyncShapeLibHighlight(); }
        internal static void Open()   { if (!_open) { _open = true; EditorToolbar.SyncShapeLibHighlight(); } }
        internal static void Close()  { if (_open) { _open = false; EditorToolbar.SyncShapeLibHighlight(); } }

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool inEditor = ed != null && !ed.playMode && s != null && MainClass.EditorSuiteOn;
            if (!_open || !inEditor) { K.Show(false); return; }
            if (!K.Built) { BuildShell(); BuildBody(); }
            if (!_selfChecked) { _selfChecked = true; ShapePathGraphic.SelfCheck(); }
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            K.Show(true);
            TickScroll();
        }

        internal static void Dispose()
        {
            K.Dispose();
            _viewport = null; _content = null; _railHost = null;
            _open = false; _selfChecked = false; _scroll = 0f;
        }

        // ── shell (rail + scroll viewport, geometry lifted from EditorLevelMenu) ─────────────

        private static void BuildShell()
        {
            K.Rebuild(Loc.T("Shape library"), Close, new Vector2(700f, -40f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            K.OnDragEnd = () => K.SnapDockOnDragEnd();

            // left rail: one row per ShapeLibrary.All[i]
            var railGo = new GameObject("Rail", typeof(RectTransform));
            railGo.transform.SetParent(K.PanelGo.transform, false);
            _railHost = (RectTransform)railGo.transform;
            _railHost.anchorMin = new Vector2(0f, 0f); _railHost.anchorMax = new Vector2(0f, 1f);
            _railHost.pivot = new Vector2(0f, 1f);
            _railHost.offsetMin = new Vector2(Pad, Pad);
            _railHost.offsetMax = new Vector2(Pad + RailW, -HeaderH - 2f);

            // right scroll viewport: preview pane
            var vpGo = new GameObject("View", typeof(RectTransform));
            vpGo.transform.SetParent(K.PanelGo.transform, false);
            _viewport = (RectTransform)vpGo.transform;
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            _viewport.offsetMin = new Vector2(Pad + RailW + Pad, Pad);
            _viewport.offsetMax = new Vector2(-Pad, -HeaderH - 2f);
            vpGo.AddComponent<RectMask2D>();
            var vpImg = vpGo.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.01f);
            vpImg.raycastTarget = true;

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vpGo.transform, false);
            _content = (RectTransform)cGo.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static void BuildBody()
        {
            BuildRail();
            BuildPreview();
        }

        // ── left: shape list ──────────────────────────────────────────────────

        private static void BuildRail()
        {
            if (_railHost == null) return;
            for (int i = _railHost.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_railHost.GetChild(i).gameObject);

            const float rowH = 26f, gap = 3f;
            var shapes = ShapeLibrary.All;
            float y = 0f;
            for (int i = 0; i < shapes.Length; i++)
            {
                int idx = i;
                var go = new GameObject("Shape", typeof(RectTransform));
                go.transform.SetParent(_railHost, false);
                var r = (RectTransform)go.transform;
                r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
                r.pivot = new Vector2(0.5f, 1f);
                r.anchoredPosition = new Vector2(0f, y);
                r.sizeDelta = new Vector2(0f, rowH);
                var bg = go.AddComponent<RoundedRectGraphic>();
                bg.Radius = 6f;
                bg.color = idx == _sel
                    ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f)
                    : new Color(1f, 1f, 1f, 0.05f);
                bg.raycastTarget = true;

                var lGo = new GameObject("L", typeof(RectTransform));
                lGo.transform.SetParent(go.transform, false);
                var lr = (RectTransform)lGo.transform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = new Vector2(8f, 0f); lr.offsetMax = new Vector2(-6f, 0f);
                var lt = UIBuilder.Tmp(lGo, shapes[idx].Name, 12.5f, TextAnchor.MiddleLeft, Theme.Text);
                lt.raycastTarget = false;

                UI.ClickHandler.Attach(go, () =>
                {
                    if (_sel == idx) return;
                    _sel = idx; _scroll = 0f;
                    BuildRail(); BuildPreview();
                });
                y -= rowH + gap;
            }
        }

        // ── right: simple + pseudo previews for the selected shape ───────────

        private static void BuildPreview()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);

            var shapes = ShapeLibrary.All;
            if (shapes.Length == 0) { _content.sizeDelta = Vector2.zero; return; }
            if (_sel < 0 || _sel >= shapes.Length) _sel = 0;
            var def = shapes[_sel];
            float w = Mathf.Max(160f, _viewport != null ? _viewport.rect.width : _size.x - Pad * 3f - RailW);

            float y = -2f;
            y = AddPreviewBlock(y, w, def.Name + " — " + Loc.T("Simple"), def.SimpleMeta,
                g => g.SetSimple(def.Simple));
            if (def.Pseudo != null)
                foreach (var pf in def.Pseudo)
                    y = AddPreviewBlock(y, w, pf.Name, pf.Meta, g => g.SetPath(pf.Steps));

            _content.sizeDelta = new Vector2(0f, -y + 6f);
            ClampScroll();
        }

        private const float PreviewH = 130f, CapH = 16f, MetaH = 14f, BlockGap = 10f;

        private static float AddPreviewBlock(float y, float w, string title, string meta,
            Action<ShapePathGraphic> fill)
        {
            EventRows.Label(_content, title, 0f, y, w, CapH, Theme.Text);
            y -= CapH;
            if (!string.IsNullOrEmpty(meta))
            {
                EventRows.Label(_content, meta, 0f, y, w, MetaH, Theme.TextMuted);
                y -= MetaH;
            }
            y -= 4f;
            var g = MakePreview(_content, 0f, y, w, PreviewH);
            fill(g);
            y -= PreviewH + BlockGap;
            return y;
        }

        // Make a preview cell: a bg rect hosting a ShapePathGraphic child that fills it.
        private static ShapePathGraphic MakePreview(RectTransform parent, float x, float y, float w, float h)
        {
            var go = new GameObject("Preview", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f; bg.color = new Color(0f, 0f, 0f, 0.35f);
            var pgo = new GameObject("Path", typeof(RectTransform));
            pgo.transform.SetParent(go.transform, false);
            var pr = (RectTransform)pgo.transform;
            pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one;
            pr.offsetMin = Vector2.zero; pr.offsetMax = Vector2.zero;
            return pgo.AddComponent<ShapePathGraphic>();
        }

        // ── scroll (verbatim from EditorLevelMenu) ────────────────────────────

        private static void TickScroll()
        {
            if (_viewport == null || _content == null) return;
            float wheel = MainClass.WheelY;
            if (Mathf.Abs(wheel) < 0.01f) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_viewport, Input.mousePosition, null)) return;
            _scroll = Mathf.Clamp(_scroll + wheel * 60f, 0f,
                Mathf.Max(0f, _content.sizeDelta.y - _viewport.rect.height));
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static void ClampScroll()
        {
            if (_viewport == null || _content == null) return;
            _scroll = Mathf.Clamp(_scroll, 0f, Mathf.Max(0f, _content.sizeDelta.y - _viewport.rect.height));
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }
    }
}

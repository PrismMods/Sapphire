using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Property inspector for ONE decoration, in its own floating window. The deco browser
       (EditorLevelMenu's Decorations tab) owns the selection and hands the event here; this
       panel renders it and nothing else. It used to render INLINE under the browser's list
       row, which reflowed the list on every select and buried it entirely for a particle
       (35+ properties). ShowHidden is on: the game marks most AddParticle properties
       "control": "Hidden" because it edits them in a dedicated panel — this IS that panel. */
    internal static class EditorDecoInspector
    {
        private static readonly PanelKit K = new PanelKit("SapphireDecoInspector", 903, PanelW, focusable: true);
        private const float PanelW = 360f, HeaderH = 28f;
        private const float Pad = PanelKit.Pad, RowH = PanelKit.RowH, Gap = PanelKit.Gap;

        private static Vector2 _size = new Vector2(PanelW, 560f);
        private static RectTransform _viewport, _content;
        private static float _scroll;
        private static ADOFAI.LevelEvent _evt;
        private static ADOFAI.LevelEvent _built;   // what the shell's title was built for
        private static long _sig;
        private static int _scanCd;
        private static bool _dockInited;

        internal static bool IsOpen => _evt != null;

        internal static void Show(ADOFAI.LevelEvent evt)
        {
            _evt = evt;
            _sig = 0;
            _scroll = 0f;
        }

        internal static void Close() { _evt = null; _sig = 0; }

        private static readonly EventRows.Ctx _ctx = new EventRows.Ctx
        {
            PanelW = PanelW,
            ShowHidden = true,
            MarkDirty = () => _sig = 0,
            AfterCommit = (ed, evt, pi) => { try { ed.UpdateDecorationObjects(); } catch { } },
        };

        internal static void Tick()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool live = _evt != null && ed != null && !ed.playMode && MainClass.EditorSuiteOn;
            // A delete or an undo can drop our event out of the level; don't render a ghost.
            if (live)
            {
                bool present = false;
                try { present = ed.decorations != null && ed.decorations.Contains(_evt); } catch { }
                if (!present) { Close(); live = false; }
            }
            if (!live) { K.Show(false); return; }

            bool dirty = _sig == 0 || !K.Built || _built != _evt;
            if (--_scanCd <= 0) { _scanCd = 12; dirty = true; }
            if (dirty)
            {
                if (!K.Built || _built != _evt) { BuildShell(); _built = _evt; }
                long sig = Sig();
                if (sig != _sig || _content == null || _content.childCount == 0)
                {
                    _sig = sig;
                    BuildContent(ed);
                }
            }

            K.Show(true);
            ClampIntoView();
            TickScroll();
            TickResize();
        }

        internal static void Dispose()
        {
            K.Dispose();
            _viewport = null; _content = null;
            _evt = null; _built = null; _sig = 0;
        }

        private static long Sig()
        {
            long h = 17;
            try
            {
                h = h * 31 + (int)_evt.eventType;
                var d = EditorEvents.EventData(_evt);
                if (d != null)
                    foreach (var kv in d)
                        h = h * 31 + (kv.Key?.GetHashCode() ?? 0) + (kv.Value?.GetHashCode() ?? 0);
            }
            catch { }
            return h;
        }

        private static string Title()
        {
            string type = "?";
            try { type = EditorLevelMenu.DecoTypeName(_evt.eventType); } catch { }
            string tag = "";
            try { tag = EditorLevelMenu.DecoTagOf(_evt); } catch { }
            return Loc.T("Decoration") + " · " + type + (tag.Length > 0 ? " · " + tag : "");
        }

        private static void BuildShell()
        {
            K.LblW = 118f;
            K.Rebuild(Title(), () =>
            {
                Close();
                try { scnEditor.instance.DeselectAllDecorations(); } catch { }
                EditorLevelMenu.ClearDecoSelection();
            }, new Vector2(1120f, -80f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            ResizeHandle.AttachAll(panel, true, 280f, 240f);
            K.OnDragEnd = () => K.SnapDockOnDragEnd();
            if (!_dockInited) { _dockInited = true; }

            var vpGo = new GameObject("View", typeof(RectTransform));
            vpGo.transform.SetParent(K.PanelGo.transform, false);
            _viewport = (RectTransform)vpGo.transform;
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            _viewport.offsetMin = new Vector2(0f, 8f);
            _viewport.offsetMax = new Vector2(0f, -HeaderH - 2f);
            vpGo.AddComponent<RectMask2D>();
            var vpImg = vpGo.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.01f);
            vpImg.raycastTarget = true;   // wheel target

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vpGo.transform, false);
            _content = (RectTransform)cGo.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static void BuildContent(scnEditor ed)
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);

            _ctx.Content = _content;
            _ctx.PanelW = _size.x;

            ADOFAI.LevelEventInfo info = null;
            try { GCS.levelEventsInfo.TryGetValue(_evt.eventType.ToString(), out info); } catch { }
            float y = -2f;
            if (info == null)
            {
                EventRows.Label(_content, Loc.T("(settings unavailable)"), Pad, y,
                    _size.x - Pad * 2f, RowH, Theme.TextMuted);
                y -= RowH + Gap;
            }
            else y = EventRows.Render(_ctx, ed, info, _evt, y);

            _content.sizeDelta = new Vector2(0f, -y + 6f);
            ClampScroll();
        }

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

        private static void TickResize()
        {
            if (!K.Built) return;
            var r = (RectTransform)K.PanelGo.transform;
            if ((r.sizeDelta - _size).sqrMagnitude > 1f)
            {
                _size = r.sizeDelta;
                _sig = 0;   // width changed → rows re-lay at the new content width
                ClampScroll();
            }
        }

        private static void ClampIntoView()
        {
            if (!K.Built) return;
            var r = (RectTransform)K.PanelGo.transform;
            var canvas = (RectTransform)K.CanvasGo.transform;
            var p = r.anchoredPosition;
            p.x = Mathf.Clamp(p.x, 0f, Mathf.Max(0f, canvas.rect.width - 80f));
            p.y = Mathf.Clamp(p.y, -(canvas.rect.height - 40f), 0f);
            if ((p - r.anchoredPosition).sqrMagnitude > 0.01f) r.anchoredPosition = p;
        }

        internal static bool Hovered
        {
            get
            {
                try
                {
                    return K.Visible && RectTransformUtility.RectangleContainsScreenPoint(
                        (RectTransform)K.PanelGo.transform, Input.mousePosition, null);
                }
                catch { return false; }
            }
        }
    }
}

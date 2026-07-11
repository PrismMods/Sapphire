using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sapphire.UI
{
    // Drag-editor plumbing shared by the on-screen editors (extracted from Sapphire's
    // LocationEditor): LocHandle (drag/scale/reset handle), ScaleGrip, EditorUndo,
    // UndoPoller.
    // One draggable handle. Tracks its target's screen rect each frame (expanded to a
    // grabbable minimum), hides while the target is hidden/empty, and converts drags into
    // normalized-anchor writes with edge/center snapping. Optional extras (GameUiEditor):
    // corner-grip / scroll-wheel scaling, right-click reset, dimmed inactive-target handles.
    internal class LocHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IScrollHandler, IPointerClickHandler
    {
        public Func<RectTransform> GetTarget;
        public Action BeginDragCapture;
        public Action<Vector2> DragBy;   // screen-pixel delta from drag start
        public Func<float> GetScale;     // current scale, with SetScale enables scaling
        public Action<float> SetScale;   // absolute scale write (callee clamps)
        public Action ResetTarget;       // right-click, null = no reset
        public Func<Action> CaptureUndo; // snapshot current state, returns a restore closure (null = not undoable)
        public bool ShowInactive;        // keep a dimmed handle when the target is inactive
        public bool TightBounds;         // size to visible child Graphics, not the target rect
        public bool LockX;
        public Canvas EditorCanvas;
        public CanvasGroup Group;

        private RectTransform _rt;
        private bool _dragging;
        private Vector2 _screenStart;
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Vector3[] _cornersStart = new Vector3[4];

        private const float SnapPx = 14f;       // canvas units; scaled to screen px below
        private const float MarginFrac = 0.01f; // inset snap line per axis, matches default 0.01 anchors
        private const float MinW = 56f;      // grabbable minimum, canvas units
        private const float MinH = 30f;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            _rt.anchorMin = _rt.anchorMax = Vector2.zero; // bottom-left of editor canvas
            _rt.pivot = Vector2.zero;
        }

        private void LateUpdate()
        {
            var target = GetTarget?.Invoke();
            bool active = target != null && target.gameObject.activeInHierarchy;
            bool show = target != null && (active ? HasContent(target) : ShowInactive);
            // Visibility via CanvasGroup, not SetActive — a disabled GameObject would stop
            // receiving LateUpdate and never come back.
            Group.alpha = show ? (active ? 1f : 0.45f) : 0f;
            Group.blocksRaycasts = show;
            Group.interactable = show;
            if (!show) return;

            // SSO canvases share the screen-pixel world space; both canvases use the same
            // scaler config so a single scaleFactor converts to editor-canvas units.
            if (!(TightBounds && active && TryTightCorners(target)))
                target.GetWorldCorners(_corners);
            float sf = EditorCanvas.scaleFactor;
            Vector2 min = _corners[0] / sf;
            Vector2 max = _corners[2] / sf;
            Vector2 size = max - min;
            if (size.x < MinW) { float d = (MinW - size.x) * 0.5f; min.x -= d; size.x = MinW; }
            if (size.y < MinH) { float d = (MinH - size.y) * 0.5f; min.y -= d; size.y = MinH; }
            _rt.anchoredPosition = min;
            _rt.sizeDelta = size;

            if (SetScale != null && !_gripsMade) MakeGrips();
        }

        // Handle center in screen pixels (SSO canvas world units are screen px).
        internal Vector2 ScreenCenter()
        {
            float sf = EditorCanvas != null ? EditorCanvas.scaleFactor : 1f;
            return (Vector2)_rt.position + _rt.sizeDelta * (0.5f * sf);
        }

        // Photoshop-style corner grips: drag toward/away from the handle center to
        // scale. Children with their own drag handlers, so grip drags don't bubble
        // into the move-drag on this handle.
        private bool _gripsMade;

        private void MakeGrips()
        {
            _gripsMade = true;
            var corners = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            foreach (var c in corners)
            {
                var go = new GameObject("Grip", typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                rt.anchorMin = rt.anchorMax = c;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(12f, 12f);

                var bg = go.AddComponent<RoundedRectGraphic>();
                bg.Radius = 2f;
                bg.AAFringe = 0.5f;
                bg.BorderWidth = 1f;
                bg.BorderColor = new Color(0f, 0f, 0f, 0.6f);
                bg.color = Theme.Accent;
                bg.raycastTarget = true;

                go.AddComponent<ScaleGrip>().Owner = this;
            }
        }

        // Union of the visible child Graphics' rects, for targets whose own rect has dead
        // space (the error meter wrapper extends below its content). Writes _corners[0]/[2]
        // and reports whether it found anything to measure.
        private static readonly Vector3[] _tightTmp = new Vector3[4];

        private bool TryTightCorners(RectTransform target)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            foreach (var g in target.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.isActiveAndEnabled || g.color.a < 0.05f) continue;
                g.rectTransform.GetWorldCorners(_tightTmp);
                min = Vector2.Min(min, _tightTmp[0]);
                max = Vector2.Max(max, _tightTmp[2]);
                any = true;
            }
            if (!any) return false;
            _corners[0] = min;
            _corners[2] = max;
            return true;
        }

        // An empty container still has padding-driven size, so only offer a handle when
        // something inside is visible (a target drawing its own Graphic counts). The death
        // % text carries inactive aux labels that hid its handle when the message showed.
        private static bool HasContent(RectTransform target)
        {
            var g = target.GetComponent<Graphic>();
            if (g != null && g.enabled) return true;
            if (target.childCount == 0) return true;
            for (int i = 0; i < target.childCount; i++)
                if (target.GetChild(i).gameObject.activeSelf) return true;
            return false;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            var target = GetTarget?.Invoke();
            if (target == null || e.button != PointerEventData.InputButton.Left) return;
            _dragging = true;
            _screenStart = e.position;
            target.GetWorldCorners(_cornersStart);
            EditorUndo.Capture(this);
            BeginDragCapture?.Invoke();
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragging) return;
            Vector2 delta = e.position - _screenStart; // screen px

            // Hold Shift to lock the drag to its dominant axis (1-D move). LockX is a
            // permanent vertical-only constraint for certain targets (e.g. combo label).
            bool lockX = LockX;
            bool lockY = false;
            if (!LockX && ShiftHeld())
            {
                if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)) lockY = true;
                else lockX = true;
            }
            if (lockX) delta.x = 0f;
            if (lockY) delta.y = 0f;

            float sf = EditorCanvas.scaleFactor;
            float snap = SnapPx * sf;

            // Snap the element's would-be screen rect per axis: edge flush to the screen
            // edge, edge to the 1%-inset margin line (the default anchor positions), or
            // center to the screen center.
            if (!lockX)
                delta.x += AxisSnap(_cornersStart[0].x + delta.x, _cornersStart[2].x + delta.x,
                    Screen.width, snap, Screen.width * MarginFrac);
            if (!lockY)
                delta.y += AxisSnap(_cornersStart[0].y + delta.y, _cornersStart[2].y + delta.y,
                    Screen.height, snap, Screen.height * MarginFrac);

            DragBy?.Invoke(delta);
        }

        private static bool ShiftHeld() =>
            Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // Returns the adjustment that aligns the rect [lo..hi] with its nearest snap line
        // on one screen axis, or 0 when none is within `snap`.
        private static float AxisSnap(float lo, float hi, float size, float snap, float margin)
        {
            float best = float.MaxValue, adj = 0f;
            Consider(-lo, ref best, ref adj);                          // lo edge → 0
            Consider(margin - lo, ref best, ref adj);                  // lo edge → inset line
            Consider(size - hi, ref best, ref adj);                    // hi edge → size
            Consider(size - margin - hi, ref best, ref adj);           // hi edge → inset line
            Consider(size * 0.5f - (lo + hi) * 0.5f, ref best, ref adj); // center → center
            return best <= snap ? adj : 0f;
        }

        private static void Consider(float candidate, ref float best, ref float adj)
        {
            float d = Mathf.Abs(candidate);
            if (d < best) { best = d; adj = candidate; }
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;
            // Push through the full apply chain once per drop (per-frame would also run
            // KeyLimiter etc. needlessly).
            UICore.OnSettingsChanged?.Invoke();
        }

        public void OnScroll(PointerEventData e)
        {
            if (SetScale == null || GetScale == null || Mathf.Approximately(e.scrollDelta.y, 0f)) return;
            EditorUndo.Capture(this);
            SetScale(GetScale() * (1f + 0.1f * Mathf.Sign(e.scrollDelta.y)));
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (ResetTarget == null || e.button != PointerEventData.InputButton.Right) return;
            EditorUndo.Capture(this);
            ResetTarget();
            UICore.OnSettingsChanged?.Invoke();
        }
    }

    // Corner grip on a LocHandle: dragging scales the target around the handle center
    // (uniform, the ratio of the pointer's current to initial distance from center).
    internal class ScaleGrip : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public LocHandle Owner;

        private bool _scaling;
        private Vector2 _center;
        private float _startDist;
        private float _startScale;

        public void OnBeginDrag(PointerEventData e)
        {
            if (Owner == null || Owner.GetScale == null || Owner.SetScale == null ||
                e.button != PointerEventData.InputButton.Left) return;
            _center = Owner.ScreenCenter();
            _startDist = (e.position - _center).magnitude;
            if (_startDist < 2f) return;
            EditorUndo.Capture(Owner);
            _startScale = Owner.GetScale();
            _scaling = true;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_scaling) return;
            float f = (e.position - _center).magnitude / _startDist;
            Owner.SetScale(_startScale * f);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_scaling) return;
            _scaling = false;
            UICore.OnSettingsChanged?.Invoke();
        }
    }

    // Per-editor-session undo stack. Each handle gesture (drag/scale/reset) pushes a
    // restore closure captured just before it mutates settings; Ctrl/Cmd+Z pops one.
    internal static class EditorUndo
    {
        private static readonly Stack<Action> _stack = new Stack<Action>();

        public static void Reset() => _stack.Clear();

        public static void Capture(LocHandle h)
        {
            var restore = h?.CaptureUndo?.Invoke();
            if (restore != null) _stack.Push(restore);
        }

        public static bool Undo()
        {
            if (_stack.Count == 0) return false;
            _stack.Pop().Invoke();
            UICore.OnSettingsChanged?.Invoke();
            return true;
        }
    }

    // Polls Ctrl/Cmd+Z to undo the last edit. Uses GetKey edge detection because
    // KeyLimiter blocks Input.GetKeyDown (but not GetKey) while the panel is open.
    internal class UndoPoller : MonoBehaviour
    {
        private bool _zPrev;

        private void Update()
        {
            bool z = Input.GetKey(KeyCode.Z);
            bool mod = Input.GetKey(KeyCode.LeftControl)  || Input.GetKey(KeyCode.RightControl)
                    || Input.GetKey(KeyCode.LeftCommand)  || Input.GetKey(KeyCode.RightCommand);
            if (z && !_zPrev && mod) EditorUndo.Undo();
            _zPrev = z;
        }
    }
}

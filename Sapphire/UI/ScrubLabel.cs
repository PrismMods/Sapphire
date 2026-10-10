using System;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sapphire.UI
{
    /* Drag a numeric field's label to scrub its value; middle-click resets it. Shift is fine
       (x0.1), Ctrl/Cmd coarse (x10). Ported idea from O5Kit (Overlayer v5).

       It drives the FIELD, not the value: the text updates live and the field's own onEndEdit
       runs once on release. Every row already commits through that path (ExprEval, formulas,
       SaveStateScope), so there is one undo step per drag and no second commit path. Committing
       per frame is not an option here: every inspector rebuilds its rows on the tick after a
       commit, which would destroy this label mid-drag.

       The infinite part (cursor warps back at the window edge) is PrismLib.UI.InfiniteDrag, new
       in the copy that ships with this build. Only one PrismLib.UI loads per session and it may be
       an older one from another mod's folder, so every touch of it sits in a NoInlining method
       behind a try; without it the drag still works and simply stops at the screen edge. */
    internal sealed class ScrubLabel : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private TMP_InputField _field;
        private bool _int;
        private double? _reset;
        private object _drag;           // PrismLib.UI.InfiniteDrag, held loosely (see above)
        private bool _dragging;
        private double _acc, _step;
        private static bool _noPrism;

        internal static void Attach(GameObject label, TMP_InputField field, bool isInt, double? reset = null)
        {
            if (label == null || field == null) return;
            var g = label.GetComponent<UnityEngine.UI.Graphic>();
            if (g != null) g.raycastTarget = true;
            var s = label.GetComponent<ScrubLabel>() ?? label.AddComponent<ScrubLabel>();
            s._field = field;
            s._int = isInt;
            s._reset = reset;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Middle || !_reset.HasValue || _field == null) return;
            _field.text = Format(_reset.Value);
            _field.onEndEdit.Invoke(_field.text);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            // A formula or a half-typed value has no number to start from; leave it alone.
            if (e.button != PointerEventData.InputButton.Left || _field == null
                || !ExprEval.TryParseDouble(_field.text, out _acc)) return;
            if (_field.isFocused) _field.DeactivateInputField();
            _step = StepFor(_acc, _int);
            _dragging = true;
            DragBegin();
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_dragging) return;
            float dx = DragStep(e.delta.x);
            double k = Held(KeyCode.LeftShift, KeyCode.RightShift) ? 0.1
                     : Held(KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftCommand, KeyCode.RightCommand) ? 10.0
                     : 1.0;
            _acc += dx * _step * k;
            _field.text = Format(_acc);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!_dragging) return;
            Finish();
            _field.onEndEdit.Invoke(_field.text);
        }

        public void OnPointerEnter(PointerEventData e) => SetCursor(true);
        public void OnPointerExit(PointerEventData e) { if (!_dragging) SetCursor(false); }

        // A panel hidden or rebuilt mid-drag must still hand the cursor back.
        private void OnDisable()
        {
            if (_dragging) Finish();
            SetCursor(false);
        }

        private void Finish()
        {
            _dragging = false;
            DragEnd();
        }

        /* Pixels per unit scale with the value's size, frozen at drag start: 1/px at 180°, 0.01/px
           for a 1-beat duration, 0.1/px from zero. Ints move at least one per 5px. */
        internal static double StepFor(double v, bool isInt)
        {
            double a = Math.Abs(v);
            double s = a < 1e-9 ? 0.1 : Math.Max(0.01, Math.Pow(10, Math.Floor(Math.Log10(a)) - 2));
            return isInt ? Math.Max(s, 0.2) : s;
        }

        private string Format(double v) => _int
            ? Math.Round(v).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        private static bool Held(params KeyCode[] keys)
        {
            foreach (var k in keys) if (Input.GetKey(k)) return true;
            return false;
        }

        // ── PrismLib.UI, guarded ─────────────────────────────────────────

        private void DragBegin()
        {
            if (_noPrism) return;
            try { _drag = NewDrag(); Begin(_drag); }
            catch (Exception ex) { _noPrism = true; _drag = null; SapphireLog.Log("ScrubLabel: no InfiniteDrag (" + ex.GetType().Name + ")"); }
        }

        private float DragStep(float fallback)
        {
            if (_drag == null) return fallback;
            try { return Step(_drag); }
            catch { _drag = null; return fallback; }
        }

        private void DragEnd()
        {
            if (_drag == null) return;
            try { End(_drag); } catch { }
            _drag = null;
        }

        private static void SetCursor(bool scrub)
        {
            if (_noPrism) return;
            try { ApplyCursor(scrub); } catch { _noPrism = true; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)] private static object NewDrag() => new PrismLib.UI.InfiniteDrag();
        [MethodImpl(MethodImplOptions.NoInlining)] private static void Begin(object d) => ((PrismLib.UI.InfiniteDrag)d).Begin();
        [MethodImpl(MethodImplOptions.NoInlining)] private static float Step(object d) => ((PrismLib.UI.InfiniteDrag)d).Step();
        [MethodImpl(MethodImplOptions.NoInlining)] private static void End(object d) => ((PrismLib.UI.InfiniteDrag)d).End();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyCursor(bool scrub) => PrismLib.UI.Toolkit.Cursors.Apply(scrub
            ? PrismLib.UI.Toolkit.Cursors.Kind.ResizeHorizontal : PrismLib.UI.Toolkit.Cursors.Kind.Default);
    }
}

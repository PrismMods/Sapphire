using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sapphire.UI
{
    /* Keyboard ownership for text fields: the one "is the user typing" answer every bare-key
       hotkey reads, and Tab / Shift+Tab between the fields of a Sapphire window.

       Typing latches for a frame. A field commits and releases focus on the frame Enter lands,
       and whether a module sees that frame before or after the EventSystem is not defined — so
       "focused now" alone let Enter stamp, and digits typed into an event field arm palette tools
       in whichever module happened to tick after the release. */
    internal static class FieldNav
    {
        private static int _focusFrame = -10;
        private static GameObject _polledSel;

        // Called first thing in the ticker's Update, before any module reads keys.
        internal static void Poll()
        {
            _polledSel = Selected();
            if (FocusedNow()) _focusFrame = Time.frameCount;
        }

        // A Tab session counts as typing even on a frame where focus is between fields.
        internal static bool Typing => _hopIndex >= 0 || FocusedNow() || Time.frameCount - _focusFrame <= 1;

        private static bool FocusedNow()
        {
            try { var ed = scnEditor.instance; if (ed != null && ed.userIsEditingAnInputField) return true; } catch { }
            var go = Selected();
            return go != null && (go.GetComponent<TMP_InputField>() != null
                               || go.GetComponent<UnityEngine.UI.InputField>() != null);
        }

        private static GameObject Selected()
        {
            try { var es = EventSystem.current; return es != null ? es.currentSelectedGameObject : null; }
            catch { return null; }
        }

        // ── Tab ────────────────────────────────────────────────────────────────

        /* A hop commits the field it leaves, and a commit usually rebuilds its panel next frame,
           destroying the field just focused. So a hop opens a SESSION that remembers where it went
           (window + index) and puts focus back there whenever it slips off a field, until the user
           leaves on purpose: a click, Esc or Enter. A fixed few-frame window was not enough —
           digits typed after a Tab still armed palette tools. */
        private static Transform _hopRoot;
        private static int _hopIndex = -1;
        // ponytail: diagnostic trace of focus after a hop (SapphireLog [dbg]); drop once the Tab leak is confirmed gone.
        private static int _traceUntil = -1;
        private static GameObject _traceSel;

        /* LateUpdate, not Update: bare Tab is the game's "create midspin", and its keybinds only
           stand down while a field is focused. Hopping after they ran keeps the old field focused
           through the whole of this frame's Update. */
        internal static void LateTick()
        {
            Trace();
            if (Input.GetKeyDown(KeyCode.Tab)) { Hop(); return; }
            if (_hopIndex < 0) return;
            if (_hopRoot == null || !_hopRoot.gameObject.activeInHierarchy
                || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)
                || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            { End("left"); return; }
            if (SelectedField() != null) return;
            var list = Ordered(_hopRoot);
            if (_hopIndex >= list.Count) { End("field gone"); return; }
            SapphireLog.Debug("FieldNav f" + Time.frameCount + " refocus #" + _hopIndex + " (was " + PathOf(Selected()) + ")");
            Focus(list[_hopIndex]);
        }

        private static void End(string why)
        {
            SapphireLog.Debug("FieldNav f" + Time.frameCount + " session end: " + why);
            _hopIndex = -1; _hopRoot = null;
        }

        private static void Trace()
        {
            if (Time.frameCount > _traceUntil) return;
            var sel = Selected();
            if (sel == _traceSel) return;
            _traceSel = sel;
            var f = sel != null ? sel.GetComponent<TMP_InputField>() : null;
            SapphireLog.Debug("FieldNav f" + Time.frameCount + " sel=" + PathOf(sel)
                + (f != null ? " focused=" + f.isFocused : "") + " typing=" + Typing);
        }

        private static string PathOf(GameObject go)
        {
            if (go == null) return "null";
            var t = go.transform;
            string p = t.name;
            for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        private static void Hop()
        {
            var cur = SelectedField();
            // Something else already moved focus on this Tab (the quick-chart prompt cycles its own).
            if (cur == null || cur.gameObject != _polledSel) return;
            var root = WindowOf(cur);
            if (root == null) return;
            var list = Ordered(root);
            int i = list.IndexOf(cur);
            if (i < 0 || list.Count < 2) return;
            bool back = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int j = (i + (back ? -1 : 1) + list.Count) % list.Count;
            _hopRoot = root; _hopIndex = j;
            _traceUntil = Time.frameCount + 120; _traceSel = null;
            SapphireLog.Debug("FieldNav f" + Time.frameCount + " hop " + i + "->" + j + " of " + list.Count + " in " + root.name);
            Focus(list[j]);
        }

        private static TMP_InputField SelectedField()
        {
            var go = Selected();
            return go != null ? go.GetComponent<TMP_InputField>() : null;
        }

        // The field's top-level window: the root canvas's child that holds it. Scoping there keeps
        // Tab inside one panel when several share a canvas. Only Sapphire canvases — the game's
        // own fields keep whatever the game does with Tab.
        private static Transform WindowOf(TMP_InputField f)
        {
            var c = f.GetComponentInParent<Canvas>();
            var root = c != null ? c.rootCanvas : null;
            if (root == null || !root.name.StartsWith("Sapphire")) return null;
            var t = f.transform;
            while (t.parent != null && t.parent != root.transform) t = t.parent;
            return t.parent == root.transform ? t : null;
        }

        // Reading order: top to bottom, then left to right on a row.
        private static List<TMP_InputField> Ordered(Transform root)
        {
            var list = new List<TMP_InputField>();
            foreach (var f in root.GetComponentsInChildren<TMP_InputField>(false))
                if (f != null && f.isActiveAndEnabled && f.interactable) list.Add(f);
            var corners = new Vector3[4];
            var key = new Dictionary<TMP_InputField, Vector2>();
            foreach (var f in list)
            {
                ((RectTransform)f.transform).GetWorldCorners(corners);
                key[f] = new Vector2(Mathf.Round(-corners[1].y / 4f), corners[1].x);   // 4-unit row band
            }
            list.Sort((a, b) =>
            {
                int r = key[a].x.CompareTo(key[b].x);
                return r != 0 ? r : key[a].y.CompareTo(key[b].y);
            });
            return list;
        }

        private static void Focus(TMP_InputField f)
        {
            try
            {
                var es = EventSystem.current;
                if (es != null) es.SetSelectedGameObject(f.gameObject);   // the old field's deselect commits it
                f.ActivateInputField();
            }
            catch { }
        }
    }
}

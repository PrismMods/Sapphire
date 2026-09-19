using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* The level's variable table: one row per variable — name, formula, the value it has now,
       how many fields use it. Every edit is one undo step that re-evaluates every formula in the
       level (LevelVars.Apply), so changing `bpm` here rewrites every field written as `$bpm…`. */
    internal static class EditorVariables
    {
        private static readonly PanelKit K = new PanelKit("SapphireVariables", 908, PanelW, focusable: true);
        private const float PanelW = 340f, HeaderH = 28f, RowH = 26f, Pad = 8f, Gap = 4f;
        private const float NameW = 90f, ValueW = 64f, DelW = 22f;
        private static Vector2 _size = new Vector2(PanelW, 300f);
        private static RectTransform _viewport, _content;
        private static float _scroll;
        private static bool _open, _dirty = true, _selfChecked;
        private static object _shownTable;   // rebuild when undo swaps the level's table

        internal static PanelKit Kit => K;
        internal static bool IsOpen => _open;
        internal static void SetOpen(bool v) { _open = v; if (v) _dirty = true; }
        internal static void Toggle() => SetOpen(!_open);
        internal static bool TabAvailable() => EditorEventTray.TabAvailable();

        internal static void Tick()
        {
            if (!_open || !TabAvailable()) { K.Show(false); return; }
            if (!K.Built) { BuildShell(); _dirty = true; }
            if (!_selfChecked) { _selfChecked = true; LevelVars.SelfCheck(); }
            var t = LevelVars.Current;
            if (!ReferenceEquals(t, _shownTable)) _dirty = true;
            if (_dirty && !FieldNav.Typing) { _dirty = false; _shownTable = t; Rebuild(t); }
            K.Show(true);
            TickResize();
            TickScroll();
        }

        internal static void Dispose()
        {
            K.Dispose();
            _viewport = null; _content = null; _shownTable = null;
            _dirty = true;
        }

        // ── edits: one undo step each, then every formula re-evaluates ─────────────

        private static void Edit(System.Action<LevelVars.Table> change)
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;
            int failed = 0;
            using (new SaveStateScope(ed))
            {
                change(LevelVars.TableOf(ed.levelData));
                LevelVars.Apply(ed, out failed);
            }
            if (failed > 0)
                try { ed.ShowNotification(Loc.T("Formulas that no longer evaluate") + " · " + failed, null, 0f); } catch { }
            EditorEventPanel.Refresh();
            EditorLevelMenu.Refresh();
            _dirty = true;
        }

        private static void Rename(LevelVars.Var v, string name)
        {
            name = (name ?? "").Trim().TrimStart('$');
            if (name == v.Name) return;
            var t = LevelVars.Current;
            bool taken = false;
            if (t != null) foreach (var o in t.Vars) if (o != v && o.Name == name) taken = true;
            if (!LevelVars.ValidName(name) || taken)
            {
                try { scnEditor.instance.ShowNotification(Loc.T("Names use letters, digits and _, and must be unique"), null, 0f); } catch { }
                _dirty = true;
                return;
            }
            string old = v.Name;
            Edit(_ =>
            {
                var ld = scnEditor.instance.levelData;
                if (LevelVars.ValidName(old)) LevelVars.RenameRefs(ld, old, name);
                v.Name = name;
            });
        }

        private static void AddVar()
        {
            var t = LevelVars.Current;
            int i = 1;
            string name;
            do { name = "v" + i++; } while (t != null && t.Vars.Exists(o => o.Name == name));
            Edit(tab => tab.Vars.Add(new LevelVars.Var { Name = name, Expr = "0" }));
        }

        // ── UI ────────────────────────────────────────────────────────────────

        private static void BuildShell()
        {
            K.Rebuild(Loc.T("Variables"), () => _open = false, new Vector2(420f, -150f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            K.OnDragEnd = () => K.SnapDockOnDragEnd();
            ResizeHandle.AttachAll(panel, true, 260f, 160f);

            var vpGo = new GameObject("View", typeof(RectTransform));
            vpGo.transform.SetParent(K.PanelGo.transform, false);
            _viewport = (RectTransform)vpGo.transform;
            _viewport.anchorMin = Vector2.zero; _viewport.anchorMax = Vector2.one;
            _viewport.offsetMin = new Vector2(0f, 3f); _viewport.offsetMax = new Vector2(0f, -HeaderH);
            vpGo.AddComponent<RectMask2D>();
            var img = vpGo.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.01f);
            img.raycastTarget = true;

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vpGo.transform, false);
            _content = (RectTransform)cGo.transform;
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static void Rebuild(LevelVars.Table t)
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Object.Destroy(_content.GetChild(i).gameObject);
            float w = _size.x - Pad * 2f, y = -Pad;

            var hint = MakeText(Loc.T("Write $name in any number field. The level saves the numbers, so it plays without Sapphire; the formulas are kept for editing."),
                Pad, y, w, RowH * 2f, 11f, Theme.TextMuted);
            hint.textWrappingMode = TextWrappingModes.Normal;
            y -= RowH * 2f + Gap;

            var vals = LevelVars.Values(t);
            Dictionary<string, int> use = null;
            try { use = LevelVars.Usage(scnEditor.instance.levelData); } catch { }
            if (t != null)
                foreach (var v in t.Vars) { VarRow(v, vals, use, Pad, y, w); y -= RowH + Gap; }

            EventRows.Cell(_content, "+ " + Loc.T("Variable"), Pad, y, w, RowH, AddVar, true);
            y -= RowH + Gap;
            _content.sizeDelta = new Vector2(0f, -y + Pad);
            ClampScroll();
        }

        private static void VarRow(LevelVars.Var v, Dictionary<string, double> vals, Dictionary<string, int> use,
            float x, float y, float w)
        {
            var nameField = EventRows.InputRow(_content, x, y, NameW, v.Name, s => Rename(v, s));
            float ex = x + NameW + Gap;
            MakeText("=", ex, y, 12f, RowH, 13f, Theme.TextMuted).alignment = TextAlignmentOptions.Center;
            ex += 12f + Gap;
            float exprW = w - (ex - x) - ValueW - DelW - Gap * 2f;
            EventRows.InputRow(_content, ex, y, exprW, v.Expr, s =>
            {
                s = (s ?? "").Trim();
                if (s.Length == 0 || s == v.Expr) return;
                Edit(_ => v.Expr = s);
            });
            double d;
            bool ok = vals.TryGetValue(v.Name, out d);
            var val = MakeText(ok ? d.ToString("0.####") : Loc.T("error"), ex + exprW + Gap, y, ValueW, RowH, 12f,
                ok ? Theme.Text : new Color(1f, 0.45f, 0.45f, 1f));
            val.alignment = TextAlignmentOptions.MidlineRight;
            int n = 0;
            if (use != null) use.TryGetValue(v.Name, out n);
            HoverTip.Attach(nameField.gameObject, "$" + v.Name + " · " + n + " " + Loc.T(n == 1 ? "field" : "fields"));
            var del = EventRows.Cell(_content, "×", x + w - DelW, y, DelW, RowH, () => Edit(tab => tab.Vars.Remove(v)), true);
            HoverTip.Attach(del.gameObject, Loc.T("Remove"));
        }

        private static TextMeshProUGUI MakeText(string text, float x, float y, float w, float h, float size, Color col)
        {
            var go = new GameObject("T", typeof(RectTransform));
            go.transform.SetParent(_content, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
            var t = UIBuilder.Tmp(go, text, size, TextAnchor.MiddleLeft, col);
            t.raycastTarget = false;
            return t;
        }

        private static void TickResize()
        {
            var r = (RectTransform)K.PanelGo.transform;
            if ((r.sizeDelta - _size).sqrMagnitude <= 1f) return;
            _size = r.sizeDelta;
            _dirty = true;
        }

        private static void TickScroll()
        {
            if (_viewport == null || _content == null) return;
            float wheel = MainClass.WheelY;
            if (Mathf.Abs(wheel) < 0.01f) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_viewport, Input.mousePosition, null)) return;
            _scroll += wheel * 60f;
            ClampScroll();
        }

        private static void ClampScroll()
        {
            if (_viewport == null || _content == null) return;
            _scroll = Mathf.Clamp(_scroll, 0f, Mathf.Max(0f, _content.sizeDelta.y - _viewport.rect.height));
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Shape Library panel: left list of ShapeLibrary.All, right pane previews the selected
       shape's simple form + each pseudo variant via ShapePathGraphic, each with an Insert
       button that appends tiles onto the end of selectedFloors (or the level, if nothing is
       selected) via the game's own CreateFloorWithCharOrAngle. Convert lands in Task 5.
       Dockable focusable PanelKit, same scaffolding as EditorLevelMenu (shell/viewport/content
       + TickScroll/ClampScroll) minus the resize handle — this panel has no per-tab settings
       width dependency, so a fixed size is enough for now. */
    internal static class EditorShapeLibrary
    {
        private static readonly PanelKit K = new PanelKit("SapphireShapeLib", 903, 620f, focusable: true);
        private const float PanelW = 620f, RailW = 150f, HeaderH = 28f;
        private const float Pad = PanelKit.Pad;

        private static Vector2 _size = new Vector2(PanelW, 520f);
        private static RectTransform _viewport, _content, _railHost;
        private static float _scroll;
        private static int _sel;              // selected shape index
        private static int _matchIdx = -1;    // ShapeLibrary.All index the CURRENT editor selection matches, -1 = none
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
            TickSelectionMatch(ed);
        }

        // Auto-surface: every ~12 frames, check whether the current editor selection matches
        // some shape's Simple run; if the matched shape changes (incl. becoming/un-becoming a
        // match), switch to it and rebuild so Convert buttons re-gate. Skip while typing so a
        // text field's keystrokes don't fight a rebuild.
        private static void TickSelectionMatch(scnEditor ed)
        {
            if (Time.frameCount % 12 != 0) return;
            try { if (ed.userIsEditingAnInputField) return; } catch { }
            var shapes = ShapeLibrary.All;
            int found = -1;
            for (int i = 0; i < shapes.Length; i++)
                if (SelectionMatches(shapes[i], out _, out _)) { found = i; break; }
            if (found == _matchIdx) return;
            _matchIdx = found;
            if (found >= 0) _sel = found;
            BuildRail(); BuildPreview();
        }

        internal static void Dispose()
        {
            K.Dispose();
            _viewport = null; _content = null; _railHost = null;
            _open = false; _selfChecked = false; _scroll = 0f; _matchIdx = -1;
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
                g => g.SetSimple(def.Simple), Loc.T("Insert simple"), () => InsertSimple(def));
            bool selMatches = SelectionMatches(def, out _, out _);
            if (def.Pseudo != null)
                foreach (var pf in def.Pseudo)
                    y = AddPreviewBlock(y, w, pf.Name, pf.Meta, g => g.SetPath(pf.Steps),
                        Loc.T("Insert pseudo"), () => InsertPseudo(pf),
                        Loc.T("Convert"), selMatches ? () => Convert(def, pf) : null);

            _content.sizeDelta = new Vector2(0f, -y + 6f);
            ClampScroll();
        }

        private const float PreviewH = 130f, CapH = 16f, MetaH = 14f, BlockGap = 10f;
        private const float BtnH = 24f, BtnW = 130f;

        // title/meta + preview graphic, then an action button (Insert simple/pseudo) below it.
        // convertLabel/onConvert add a second button (pseudo blocks only); onConvert == null
        // renders it disabled (dim, unclickable) — the Task 5 selection-match gate.
        private static float AddPreviewBlock(float y, float w, string title, string meta,
            Action<ShapePathGraphic> fill, string btnLabel, Action onInsert,
            string convertLabel = null, Action onConvert = null)
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
            y -= PreviewH + 6f;
            MakeButton(_content, btnLabel, 0f, y, BtnW, BtnH, onInsert);
            if (convertLabel != null)
                MakeButton(_content, convertLabel, BtnW + 8f, y, BtnW, BtnH, onConvert);
            y -= BtnH + BlockGap;
            return y;
        }

        // Absolute-position action button matching this panel's layout (top-left anchored,
        // anchoredPosition/sizeDelta — same convention as MakePreview/EventRows.Label, not the
        // LayoutElement-driven UIBuilder.Button). Deselect after click: clicked Buttons stay
        // selected and Space re-submits them (repo-wide uGUI gotcha). onClick == null renders a
        // disabled button: dim + raycastTarget off (no handler attached) — the Convert gate.
        private static void MakeButton(RectTransform parent, string label, float x, float y,
            float w, float h, Action onClick)
        {
            bool enabled = onClick != null;
            var go = new GameObject("Btn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, enabled ? 0.4f : 0.12f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, enabled ? 0.14f : 0.05f);
            bg.raycastTarget = enabled;   // no raycast target = OnPointerClick never fires

            var txtGo = new GameObject("T", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            var txt = UIBuilder.Tmp(txtGo, label, 12.5f, TextAnchor.MiddleCenter, enabled ? Theme.Text : Theme.TextMuted);
            txt.raycastTarget = false;

            if (enabled) UI.ClickHandler.Attach(go, () => { Deselect(); onClick(); });
        }

        private static void Deselect()
        {
            try { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); } catch { }
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

        // ── insert actions (tiles via the game's own CreateFloorWithCharOrAngle, one
        //    SaveStateScope per insert = one undo step) ────────────────────────────

        private const char ArbitraryChar = (char)163;
        private const float MidspinAngle = 999f;

        // append one relative-charter tile; returns the new tracked facing
        private static double AppendRel(scnEditor ed, double rel, double dir)
        {
            var sel = ed.selectedFloors;
            bool ccw = sel != null && sel.Count > 0 && sel[0].isCCW;
            double a = dir + (ccw ? -(180.0 - rel) : (180.0 - rel));
            ed.CreateFloorWithCharOrAngle((float)a, ArbitraryChar, false, false);
            return a;
        }
        private static void AppendAbs(scnEditor ed, double abs)
            => ed.CreateFloorWithCharOrAngle((float)abs, ArbitraryChar, false, false);
        private static void AppendMidspin(scnEditor ed)
            => ed.CreateFloorWithCharOrAngle(MidspinAngle, '!', false, true);
        private static void AddTwirl(scnEditor ed, int seq)
        {
            try { ed.events.Add(new ADOFAI.LevelEvent(seq, ADOFAI.LevelEventType.Twirl)); }
            catch (Exception ex) { SapphireLog.Log("ShapeLib: twirl failed: " + ex.Message); }
        }

        // starting facing = the facing of the current (append-from) tile; -1 = end of level
        private static double StartDir(scnEditor ed)
        {
            try
            {
                var af = ADOBase.lm.floorAngles;   // absolute facings
                int at = ed.selectedFloors != null && ed.selectedFloors.Count > 0
                    ? ed.selectedFloors[ed.selectedFloors.Count - 1].seqID : af.Length - 1;
                return af[Mathf.Clamp(at, 0, af.Length - 1)];
            }
            catch { return 0.0; }
        }

        private static void InsertSimple(ShapeDef def)
        {
            var ed = scnEditor.instance; if (ed == null || def == null) return;
            if (ed.lockPathEditing) { SapphireLog.Log("ShapeLib: insert - path editing locked"); return; }
            using (new SaveStateScope(ed))
            {
                double dir = StartDir(ed);
                foreach (var rel in def.Simple) dir = AppendRel(ed, rel, dir);
                ed.RemakePath(true, true);
            }
        }

        private static void InsertPseudo(PseudoForm pf)
        {
            var ed = scnEditor.instance; if (ed == null || pf == null) return;
            if (ed.lockPathEditing) { SapphireLog.Log("ShapeLib: insert - path editing locked"); return; }
            using (new SaveStateScope(ed)) InsertPseudoInner(ed, pf);
        }

        // Body of InsertPseudo, scope-less so Convert can run delete+insert as ONE undo step
        // (its own SaveStateScope wraps both the run-delete and this call).
        private static void InsertPseudoInner(scnEditor ed, PseudoForm pf)
        {
            double dir = StartDir(ed);
            // Swirl Twirls attach by seqID. New tiles land right AFTER the append-from tile
            // (the last selected floor, or the level end when nothing is selected) — NOT at
            // floorAngles.Length, which is only the end-append case. Convert reselects the
            // pre-run tile before calling this, so its anchor = startSeq-1 → firstNewSeq = startSeq.
            int firstNewSeq = 0;
            try
            {
                int anchorSeq = ed.selectedFloors != null && ed.selectedFloors.Count > 0
                    ? ed.selectedFloors[ed.selectedFloors.Count - 1].seqID
                    : ADOBase.lm.floorAngles.Length - 1;
                firstNewSeq = anchorSeq + 1;
            }
            catch { }
            var swirlSeqs = new System.Collections.Generic.List<int>();
            int placed = 0;
            foreach (var st in pf.Steps)
            {
                if (st.Kind == StepKind.Midspin) { AppendMidspin(ed); }
                // fix: an Abs step also sets the tracked facing, else a later relative Tap
                // continues from the pre-Abs direction instead of where the ball actually is.
                else if (st.Kind == StepKind.Abs) { AppendAbs(ed, st.Angle); dir = st.Angle; }
                else { dir = AppendRel(ed, st.Angle, dir); }
                if (st.Swirl) swirlSeqs.Add(firstNewSeq + placed);
                placed++;
            }
            foreach (int seq in swirlSeqs) AddTwirl(ed, seq);   // twirls AFTER build
            ed.RemakePath(true, true);
        }

        // ── selection detection + convert (Task 5) ────────────────────────────

        // charter of each selected tile, derived from absolute facings (180 - turn). Emits the
        // sorted seqIDs themselves (not just the first) so callers delete exactly what's selected.
        private static bool SelectedCharters(scnEditor ed, out System.Collections.Generic.List<double> ch, out System.Collections.Generic.List<int> seqs)
        {
            ch = new System.Collections.Generic.List<double>();
            seqs = new System.Collections.Generic.List<int>();
            var sel = ed.selectedFloors;
            if (sel == null || sel.Count == 0) return false;
            foreach (var f in sel) if (f != null) seqs.Add(f.seqID);
            if (seqs.Count == 0) return false;
            seqs.Sort();
            var af = ADOBase.lm.floorAngles;
            foreach (int seq in seqs)
            {
                if (seq <= 0 || seq >= af.Length) return false;
                double turn = Norm180(af[seq] - af[seq - 1]);   // signed turn at this tile
                ch.Add(180.0 - System.Math.Abs(turn));
            }
            return true;
        }

        private static double Norm180(double a) { a %= 360.0; if (a > 180) a -= 360; if (a < -180) a += 360; return a; }

        // whole-selection match only: selected charter count == def.Simple.Length, each within
        // tolerance, AND the selected seqs form a contiguous run (else Convert would delete tiles
        // the user never selected — a scattered selection whose per-tile charters happen to line
        // up must NOT match). Emits the actual seqs so Convert deletes exactly those, never a
        // startSeq+i-derived span.
        private static bool SelectionMatches(ShapeDef def, out int startSeq, out int count, out System.Collections.Generic.List<int> seqs)
        {
            startSeq = -1; count = 0; seqs = null;
            var ed = scnEditor.instance; if (ed == null || def == null || def.Simple == null) return false;
            if (!SelectedCharters(ed, out var ch, out seqs)) return false;
            if (ch.Count != def.Simple.Length) return false;
            for (int i = 1; i < seqs.Count; i++)
                if (seqs[i] != seqs[i - 1] + 1) return false;   // contiguity guard
            for (int i = 0; i < ch.Count; i++)
                if (System.Math.Abs(ch[i] - def.Simple[i]) > 1.5) return false;   // ~1.5deg tolerance
            startSeq = seqs[0];
            count = ch.Count;
            return true;
        }

        private static bool SelectionMatches(ShapeDef def, out int startSeq, out int count)
            => SelectionMatches(def, out startSeq, out count, out _);

        // replace the matched selection (its actual selected tiles, `seqs`) with the pseudo form
        // at the same spot. Delete-then-insert in ONE SaveStateScope = one undo step.
        private static void Convert(ShapeDef def, PseudoForm pf)
        {
            var ed = scnEditor.instance; if (ed == null || def == null || pf == null) return;
            if (!SelectionMatches(def, out int startSeq, out _, out var seqs)) return;
            if (ed.lockPathEditing) { SapphireLog.Log("ShapeLib: convert - path editing locked"); return; }
            using (new SaveStateScope(ed))
            {
                // delete by the ACTUAL selected seqIDs, high→low (mirrors EditorToolbar's proven
                // multi-tile delete: BuildAngledSideways @ EditorToolbar.cs ~2255). Never derive the
                // span from startSeq+i — that deletes whatever sits at that index, not what the user
                // selected, if a future change ever loosens the contiguity guard above.
                for (int i = seqs.Count - 1; i >= 0; i--)
                {
                    int seq = seqs[i];
                    scrFloor t = null;
                    try { var fl = ed.floors; if (seq >= 0 && seq < fl.Count) t = fl[seq]; } catch { }
                    if (t == null) continue;
                    try { ed.DeselectFloors(); ed.SelectFloor(t, false); if (ed.SelectionIsSingle()) ed.DeleteSingleSelection(false); } catch { }
                }
                scrFloor prev = null;
                try { if (startSeq > 0) prev = ed.floors[startSeq - 1]; } catch { }
                if (prev == null) { SapphireLog.Log("ShapeLib: convert lost predecessor"); return; }
                try { ed.DeselectFloors(); ed.SelectFloor(prev, false); } catch { }
                InsertPseudoInner(ed, pf);   // appends from the reselected prev tile; does its own RemakePath
            }
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

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Shape Library panel. Left rail = collapsible CATEGORIES (Built-in + user categories) each
       holding shapes; right pane previews the selected shape's key-count variants (editable
       angles + repeat n) with Insert/Rotate, or the New-shape form. Custom shapes/categories live
       in ShapeStore (persisted to Settings). Dockable focusable PanelKit; scroll/shell scaffolding
       lifted from EditorLevelMenu. */
    internal static class EditorShapeLibrary
    {
        private static readonly PanelKit K = new PanelKit("SapphireShapeLib", 903, PanelW, focusable: true);
        private const float PanelW = 620f, HeaderH = 28f;
        private const float Pad = PanelKit.Pad;

        // Rail column = a fraction of the panel width (clamped), so both columns flex on resize/dock.
        private static float RailWidth()
        {
            float pw = K.Built ? ((RectTransform)K.PanelGo.transform).sizeDelta.x : PanelW;
            return Mathf.Clamp(pw * 0.28f, 120f, 240f);
        }

        private static Vector2 _lastSize;

        private static Vector2 _size = new Vector2(PanelW, 520f);
        private static RectTransform _viewport, _content, _railHost;
        // per (shapeId ":" keyCount) session overrides; absent = the variant's stored values.
        private static readonly Dictionary<string, double[]> _relOverride = new Dictionary<string, double[]>();
        private static readonly Dictionary<string, int> _nOverride = new Dictionary<string, int>();
        private static readonly Dictionary<string, bool> _rotate = new Dictionary<string, bool>();
        private static readonly HashSet<string> _collapsed = new HashSet<string>();
        private static string _selId;
        private static bool _formOpen;
        private static TMP_InputField _fName, _fCat, _fExpr;
        private static TextMeshProUGUI _formHint;
        private static float _scroll;
        private static bool _open, _selfChecked;

        internal static bool IsOpen => _open;
        internal static void Toggle() { _open = !_open; EditorToolbar.SyncShapeLibHighlight(); }
        internal static void Open()   { if (!_open) { _open = true; EditorToolbar.SyncShapeLibHighlight(); } }
        internal static void Close()  { if (_open) { _open = false; HideConfirm(); EditorToolbar.SyncShapeLibHighlight(); } }

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool inEditor = ed != null && !ed.playMode && s != null && MainClass.EditorSuiteOn;
            if (!_open || !inEditor) { K.Show(false); return; }
            if (!K.Built) { BuildShell(); BuildBody(); }
            if (!_selfChecked) { _selfChecked = true; ShapePathGraphic.SelfCheck(); }
            if (Input.GetKeyDown(KeyCode.Escape) && !IsTyping(ed)) { Close(); return; }
            K.Show(true);
            TickResize();
            TickScroll();
        }

        private static bool IsTyping(scnEditor ed) { try { return ed.userIsEditingAnInputField; } catch { return false; } }

        internal static void Dispose()
        {
            HideConfirm();
            K.Dispose();
            _viewport = null; _content = null; _railHost = null;
            _fName = _fCat = _fExpr = null; _formHint = null;
            _relOverride.Clear(); _nOverride.Clear(); _rotate.Clear();
            _open = false; _selfChecked = false; _scroll = 0f; _formOpen = false;
        }

        // ── shell ────────────────────────────────────────────────────────────

        private static void BuildShell()
        {
            K.Rebuild(Loc.T("Shape library"), Close, new Vector2(700f, -40f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            K.OnDragEnd = () => K.SnapDockOnDragEnd();

            var railGo = new GameObject("Rail", typeof(RectTransform));
            railGo.transform.SetParent(K.PanelGo.transform, false);
            _railHost = (RectTransform)railGo.transform;
            _railHost.anchorMin = new Vector2(0f, 0f); _railHost.anchorMax = new Vector2(0f, 1f);
            _railHost.pivot = new Vector2(0f, 1f);

            var vpGo = new GameObject("View", typeof(RectTransform));
            vpGo.transform.SetParent(K.PanelGo.transform, false);
            _viewport = (RectTransform)vpGo.transform;
            _viewport.anchorMin = new Vector2(0f, 0f); _viewport.anchorMax = new Vector2(1f, 1f);
            vpGo.AddComponent<RectMask2D>();
            var vpImg = vpGo.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.01f); vpImg.raycastTarget = true;

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vpGo.transform, false);
            _content = (RectTransform)cGo.transform;
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = new Vector2(0f, _scroll);

            Relayout();
            _lastSize = panel.sizeDelta;
            ResizeHandle.AttachAll(panel, true, 380f, 320f);   // 8 edges/corners + BR grip
        }

        // Column offsets from the current rail width — re-run on every resize/dock.
        private static void Relayout()
        {
            if (_railHost == null || _viewport == null) return;
            float rw = RailWidth();
            _railHost.offsetMin = new Vector2(Pad, Pad);
            _railHost.offsetMax = new Vector2(Pad + rw, -HeaderH - 2f);
            _viewport.offsetMin = new Vector2(Pad + rw + Pad, Pad);
            _viewport.offsetMax = new Vector2(-Pad, -HeaderH - 2f);
        }

        // Detect a panel size change (resize handle drag or docking) and re-lay both columns.
        private static void TickResize()
        {
            if (!K.Built) return;
            var r = (RectTransform)K.PanelGo.transform;
            if ((r.sizeDelta - _lastSize).sqrMagnitude > 1f)
            {
                _lastSize = r.sizeDelta;
                Relayout();
                BuildRail();
                BuildPreview();
            }
        }

        private static void BuildBody() { BuildRail(); BuildPreview(); }

        private static void ClearChildren(RectTransform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        // ── left rail: categories (collapsible) + shapes ──────────────────────

        private static ShapeEntry SelShape()
        {
            var e = ShapeStore.FindById(_selId);
            if (e == null)
            {
                foreach (var c in ShapeStore.Categories)
                {
                    var list = ShapeStore.InCategory(c);
                    if (list.Count > 0) { e = list[0]; _selId = e.Id; break; }
                }
            }
            return e;
        }

        private static void BuildRail()
        {
            if (_railHost == null) return;
            ClearChildren(_railHost);
            const float rowH = 24f, gap = 2f;
            float rw = RailWidth();
            float y = 0f;

            foreach (var cat in ShapeStore.Categories)
            {
                string c = cat;
                bool collapsed = _collapsed.Contains(c);
                bool builtin = c == ShapeStore.BuiltInCat;

                var hgo = RailRow(y, rowH, new Color(1f, 1f, 1f, 0.06f));
                // collapse arrow (+ = expand, − = collapse) — plain ASCII, always available
                var arrow = SmallBtn(hgo, collapsed ? "+" : "−", 2f, 18f, () => { if (collapsed) _collapsed.Remove(c); else _collapsed.Add(c); BuildRail(); });
                if (builtin)
                    RailLabel(hgo, c, 22f, rw - 30f, Theme.Text, 12f);
                else
                {
                    MakeMiniFieldIn(hgo, 22f, 2f, rw - 46f, rowH - 4f, c, txt => { ShapeStore.RenameCategory(c, txt); BuildRail(); });
                    SmallBtn(hgo, "×", rw - 22f, 18f, () => ShowConfirm(Loc.T("Delete category") + " \"" + c + "\"?\n" + Loc.T("(its shapes are removed too)"),
                        () => { ShapeStore.DeleteCategory(c); _selId = null; BuildRail(); BuildPreview(); }));
                }
                y -= rowH + gap;

                if (collapsed) continue;
                foreach (var sh in ShapeStore.InCategory(c))
                {
                    ShapeEntry s = sh;
                    var rgo = RailRow(y, rowH, PanelKit.Tint(s.Id == _selId));
                    var lbl = RailLabel(rgo, s.Name, 18f, rw - (s.BuiltIn ? 24f : 44f), Theme.Text, 12f);
                    lbl.raycastTarget = false;
                    UI.ClickHandler.Attach(rgo.gameObject, () => { _selId = s.Id; _formOpen = false; _scroll = 0f; BuildRail(); BuildPreview(); });
                    if (!s.BuiltIn)
                        SmallBtn(rgo, "×", rw - 22f, 18f, () => ShowConfirm(Loc.T("Delete shape") + " \"" + s.Name + "\"?",
                            () => { ShapeStore.DeleteShape(s); _selId = null; BuildRail(); BuildPreview(); }));
                    y -= rowH + gap;
                }
            }

            y -= 4f;
            SmallTextBtn(y, 0f, rw * 0.5f - 3f, rowH, Loc.T("+ Category"), () => { ShapeStore.AddCategory(NextCatName()); BuildRail(); });
            SmallTextBtn(y, rw * 0.5f + 3f, rw * 0.5f - 3f, rowH, Loc.T("+ Shape"), () => { _formOpen = true; BuildPreview(); });
        }

        private static string NextCatName()
        {
            for (int i = 1; i < 999; i++)
            {
                string n = "Category " + i;
                if (!ShapeStore.Categories.Contains(n)) return n;
            }
            return "Category";
        }

        // ── right pane: shape variants OR the new-shape form ──────────────────

        private static void BuildPreview()
        {
            if (_content == null) return;
            ClearChildren(_content);
            float w = Mathf.Max(160f, _viewport != null ? _viewport.rect.width : _size.x - Pad * 3f - RailWidth());

            if (_formOpen) { BuildForm(w); return; }

            var sh = SelShape();
            if (sh == null) { _content.sizeDelta = Vector2.zero; return; }
            float y = -2f;
            foreach (var v in sh.Variants) y = AddShapeBlock(y, w, sh, v);
            _content.sizeDelta = new Vector2(0f, -y + 6f);
            ClampScroll();
        }

        private const float PreviewH = 130f, CapH = 16f, MetaH = 14f, BlockGap = 10f;
        private const float BtnH = 24f, BtnW = 130f, FieldW = 60f, FieldGap = 6f, FieldH = 22f;

        private static string Key(ShapeEntry sh, ShapeVariant v) => sh.Id + ":" + v.K;

        private static double[] CurRel(ShapeEntry sh, ShapeVariant v)
        {
            if (_relOverride.TryGetValue(Key(sh, v), out var o) && o != null && o.Length == v.Angles.Length) return o;
            return v.Angles;
        }

        private static int ReadN(ShapeEntry sh, ShapeVariant v)
        {
            int n = _nOverride.TryGetValue(Key(sh, v), out int o) ? o : v.N;
            return Mathf.Clamp(n, 1, 999);
        }

        private static float AddShapeBlock(float y, float w, ShapeEntry sh, ShapeVariant v)
        {
            EventRows.Label(_content, sh.Name + " · " + v.K + "-key", 0f, y, w, CapH, Theme.Text);
            y -= CapH + 2f;

            double[] rel = CurRel(sh, v);
            int n = ReadN(sh, v);
            bool[] mask = v.Twirls;
            float fx = 0f;
            for (int i = 0; i < rel.Length; i++)
            {
                int ii = i;
                MakeMiniField(_content, fx, y, FieldW, FieldH, Trim(rel[i]) + (i < mask.Length && mask[i] ? "t" : ""),
                    s => EditAngle(sh, v, ii, s));
                fx += FieldW + FieldGap;
            }
            EventRows.Label(_content, "(sum " + Trim(sh.Base) + ")", fx + 2f, y - 3f, 70f, MetaH, Theme.TextMuted);
            EventRows.Label(_content, "×", w - 96f, y - 2f, 14f, FieldH, Theme.TextMuted);
            MakeMiniField(_content, w - 80f, y, 52f, FieldH, n.ToString(), s => EditN(sh, v, s), true);
            y -= FieldH + 6f;

            bool rotate = _rotate.TryGetValue(Key(sh, v), out var rv) && rv;
            var g = MakePreview(_content, 0f, y, w, PreviewH);
            g.SetPath(BuildSteps(sh, v, rel, n, rotate));
            y -= PreviewH + 6f;

            MakeButton(_content, Loc.T("Insert"), 0f, y, BtnW, BtnH, () => InsertShape(sh, v, ReadN(sh, v)));
            bool anyTwirl = false; foreach (var m in mask) anyTwirl |= m;
            if (anyTwirl)
                MakeButton(_content, Loc.T("Rotate"), BtnW + 8f, y, 96f, BtnH, () => { _rotate[Key(sh, v)] = !rotate; BuildPreview(); });
            y -= BtnH + BlockGap;
            return y;
        }

        private static void EditAngle(ShapeEntry sh, ShapeVariant v, int idx, string raw)
        {
            string s = (raw ?? "").Trim();
            if (s.EndsWith("t") || s.EndsWith("T")) s = s.Substring(0, s.Length - 1);
            if (!ExprEval.TryEval(s, out double val)) { BuildPreview(); return; }
            var rel = (double[])CurRel(sh, v).Clone();
            if (idx < 0 || idx >= rel.Length) return;
            rel[idx] = val;
            int a = idx == rel.Length - 1 ? 0 : rel.Length - 1;   // absorber keeps the sum = base
            double sum = 0.0; for (int t = 0; t < rel.Length; t++) if (t != a) sum += rel[t];
            rel[a] = sh.Base - sum;
            _relOverride[Key(sh, v)] = rel;
            BuildPreview();
        }

        private static void EditN(ShapeEntry sh, ShapeVariant v, string raw)
        {
            if (int.TryParse((raw ?? "").Trim(), out int val)) _nOverride[Key(sh, v)] = Mathf.Clamp(val, 1, 999);
            BuildPreview();
        }

        // ── new-shape form ────────────────────────────────────────────────────

        private static void BuildForm(float w)
        {
            float y = -2f;
            EventRows.Label(_content, Loc.T("New shape"), 0f, y, w, CapH, Theme.Text); y -= CapH + 6f;

            EventRows.Label(_content, Loc.T("Name"), 0f, y, 70f, FieldH, Theme.TextMuted);
            _fName = MakeMiniField(_content, 70f, y, w - 70f, FieldH, "", null); y -= FieldH + 6f;

            EventRows.Label(_content, Loc.T("Category"), 0f, y, 70f, FieldH, Theme.TextMuted);
            _fCat = MakeMiniField(_content, 70f, y, w - 70f, FieldH, "My shapes", null); y -= FieldH + 6f;

            EventRows.Label(_content, Loc.T("Angles"), 0f, y, 70f, FieldH, Theme.TextMuted);
            _fExpr = MakeMiniField(_content, 70f, y, w - 70f, FieldH, "", null); y -= FieldH + 4f;
            EventRows.Label(_content, Loc.T("e.g. 30t 30t 180  (t = twirl · sum = base)"), 70f, y, w - 70f, MetaH, Theme.TextMuted);
            y -= MetaH + 6f;

            MakeButton(_content, Loc.T("From selection"), 0f, y, 130f, BtnH, FillFromSelection); y -= BtnH + 8f;

            _formHint = ContentLabel("", 0f, y, w, MetaH, Theme.TextMuted); y -= MetaH + 6f;

            MakeButton(_content, Loc.T("Create"), 0f, y, 110f, BtnH, CreateFromForm);
            MakeButton(_content, Loc.T("Cancel"), 118f, y, 90f, BtnH, () => { _formOpen = false; BuildPreview(); });
            y -= BtnH + BlockGap;

            _content.sizeDelta = new Vector2(0f, -y + 6f);
            ClampScroll();
        }

        private static void FillFromSelection()
        {
            var v = ShapeStore.CaptureSelection();
            if (v == null) { if (_formHint != null) { _formHint.text = Loc.T("select a run of tiles first"); _formHint.color = Theme.DangerHover; } return; }
            if (_fExpr != null) _fExpr.text = FormatUnit(v);
            if (_formHint != null) { _formHint.color = Theme.TextMuted; _formHint.text = v.Angles.Length + Loc.T(" tiles captured"); }
        }

        private static void CreateFromForm()
        {
            var v = ShapeStore.ParseUnit(_fExpr != null ? _fExpr.text : null);
            if (v == null) { if (_formHint != null) { _formHint.text = Loc.T("check the angles"); _formHint.color = Theme.DangerHover; } return; }
            double baseAngle = ShapeStore.SumOf(v);
            var e = ShapeStore.AddShape(_fName != null ? _fName.text : null, _fCat != null ? _fCat.text : null, baseAngle, v);
            if (e != null) { _selId = e.Id; _formOpen = false; _collapsed.Remove(e.Category); BuildRail(); BuildPreview(); }
        }

        private static TextMeshProUGUI ContentLabel(string text, float x, float y, float w, float h, Color col)
        {
            var go = new GameObject("L", typeof(RectTransform));
            go.transform.SetParent(_content, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
            var t = UIBuilder.Tmp(go, text, 11f, TextAnchor.MiddleLeft, col); t.raycastTarget = false;
            return t;
        }

        private static string FormatUnit(ShapeVariant v)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.Angles.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(Trim(v.Angles[i]));
                if (i < v.Twirls.Length && v.Twirls[i]) sb.Append('t');
            }
            return sb.ToString();
        }

        // ── build / preview geometry (fixed turn sign; mask flips after twirled tiles) ────

        private const char ArbitraryChar = (char)163;
        private const int TurnSign = 1;

        private static void AppendAbs(scnEditor ed, double abs) => ed.CreateFloorWithCharOrAngle((float)abs, ArbitraryChar, false, false);

        private static int AnchorSpin(scnEditor ed)
        {
            try
            {
                var sel = ed.selectedFloors;
                scrFloor a = sel != null && sel.Count > 0 ? sel[sel.Count - 1] : null;
                if (a == null) { var fl = ed.floors; if (fl != null && fl.Count > 0) a = fl[fl.Count - 1]; }
                if (a != null) return a.isCCW ? 1 : -1;
            }
            catch { }
            return 1;
        }

        private static int AnchorSeq(scnEditor ed)
        {
            try
            {
                return ed.selectedFloors != null && ed.selectedFloors.Count > 0
                    ? ed.selectedFloors[ed.selectedFloors.Count - 1].seqID : ADOBase.lm.floorAngles.Length - 1;
            }
            catch { return -1; }
        }

        private static double StartDir(scnEditor ed)
        {
            try { var af = ADOBase.lm.floorAngles; return af[Mathf.Clamp(AnchorSeq(ed), 0, af.Length - 1)]; }
            catch { return 0.0; }
        }

        private static void AddTwirl(scnEditor ed, int seq)
        {
            try { ed.events.Add(new ADOFAI.LevelEvent(seq, ADOFAI.LevelEventType.Twirl)); }
            catch (Exception ex) { SapphireLog.Log("ShapeLib: twirl failed: " + ex.Message); }
        }

        private static void InsertShape(ShapeEntry sh, ShapeVariant v, int n)
        {
            var ed = scnEditor.instance; if (ed == null || sh == null || v == null) return;
            if (ed.lockPathEditing) { SapphireLog.Log("ShapeLib: insert - path editing locked"); return; }
            double[] rel = CurRel(sh, v);
            bool[] mask = v.Twirls;
            bool rotate = _rotate.TryGetValue(Key(sh, v), out var rv) && rv;
            if (n < 1) n = 1;
            using (new SaveStateScope(ed))
            {
                // FIXED geometry (localSign starts at the turn sign, not the anchor) so the shape
                // looks the same wherever it lands; flips after each twirled tile. Twirls sit ONE
                // TILE BEFORE their tile; the first twirled tile is conditional on the incoming spin
                // for correct colour, the rest always fire. Rotate negates the turn sign (mirror).
                double dir = StartDir(ed);
                int firstNewSeq = AnchorSeq(ed) + 1;
                int ts = rotate ? -TurnSign : TurnSign;
                int spin = AnchorSpin(ed);
                int localSign = ts;
                int total = rel.Length * n, placed = 0;
                bool firstMasked = true;
                var swirlSeqs = new List<int>();
                for (int idx = 0; idx < total; idx++)
                {
                    int i = idx % rel.Length;
                    dir = Norm360(dir + localSign * (180.0 - rel[i]));
                    AppendAbs(ed, dir);
                    if (i < mask.Length && mask[i])
                    {
                        if (!firstMasked || spin == ts) swirlSeqs.Add(firstNewSeq + placed - 1);
                        firstMasked = false;
                        localSign = -localSign;
                    }
                    placed++;
                }
                foreach (int seq in swirlSeqs) if (seq >= firstNewSeq - 1) AddTwirl(ed, seq);
                ed.RemakePath(true, true);
            }
        }

        private static double Norm360(double a) { a %= 360.0; if (a < 0) a += 360.0; return a; }

        // Charter + swirl steps for the PREVIEW: same fixed-geometry walk; swirl rides the tile ONE
        // BEFORE the twirled tile; colour BLUE when that tile's resulting angle ≥ 180 (ADOFAI rule).
        private static PseudoStep[] BuildSteps(ShapeEntry sh, ShapeVariant v, double[] rel, int n, bool rotate)
        {
            bool[] mask = v.Twirls;
            if (n < 1) n = 1;
            int total = rel.Length * n;
            var steps = new PseudoStep[total];
            int localSign = rotate ? -TurnSign : TurnSign;
            for (int idx = 0; idx < total; idx++)
            {
                int i = idx % rel.Length;
                steps[idx] = new PseudoStep(Norm360(180.0 - localSign * (180.0 - rel[i])));
                if (i < mask.Length && mask[i])
                {
                    localSign = -localSign;
                    if (idx - 1 >= 0)
                    {
                        // ADOFAI: twirl is BLUE when its tile's resulting angle ≥ 180, else RED —
                        // the tile's own charter, independent of rotate (rotate mirrors, not recolours).
                        steps[idx - 1].Swirl = true;
                        steps[idx - 1].SwirlBlue = Norm360(rel[i]) >= 180.0;
                    }
                }
            }
            return steps;
        }

        // ── shared widgets ────────────────────────────────────────────────────

        private static RectTransform RailRow(float y, float h, Color bg)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(_railHost, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f); r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, y); r.sizeDelta = new Vector2(0f, h);
            var g = go.AddComponent<RoundedRectGraphic>(); g.Radius = 5f; g.color = bg; g.raycastTarget = true;
            return r;
        }

        private static TextMeshProUGUI RailLabel(RectTransform parent, string text, float x, float w, Color col, float size)
        {
            var go = new GameObject("L", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f); r.sizeDelta = new Vector2(w, 0f);
            return UIBuilder.Tmp(go, text, size, TextAnchor.MiddleLeft, col);
        }

        private static void SmallTextBtn(float y, float x, float w, float h, string label, Action onClick)
        {
            var go = new GameObject("Btn", typeof(RectTransform));
            go.transform.SetParent(_railHost, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f; bg.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.35f);
            bg.BorderWidth = 1f; bg.BorderColor = new Color(1f, 1f, 1f, 0.12f); bg.raycastTarget = true;
            var t = RailLabel(r, label, 0f, w, Theme.Text, 11.5f); t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
            UI.ClickHandler.Attach(go, () => { Deselect(); onClick(); });
        }

        private static GameObject SmallBtn(RectTransform parent, string label, float x, float sz, Action onClick)
        {
            var go = new GameObject("B", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 0.5f); r.anchorMax = new Vector2(0f, 0.5f); r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f); r.sizeDelta = new Vector2(sz, sz);
            var bg = go.AddComponent<RoundedRectGraphic>(); bg.Radius = 4f; bg.color = new Color(1f, 1f, 1f, 0.06f); bg.raycastTarget = true;
            var t = RailLabel(r, label, 0f, sz, Theme.TextMuted, 13f); t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
            UI.ClickHandler.Attach(go, () => { Deselect(); onClick(); });
            return go;
        }

        private static void MakeButton(RectTransform parent, string label, float x, float y, float w, float h, Action onClick, bool danger = false)
        {
            var go = new GameObject("Btn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = danger ? new Color(0.80f, 0.22f, 0.26f, 0.55f)
                              : new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f);
            bg.BorderWidth = 1f; bg.BorderColor = new Color(1f, 1f, 1f, 0.14f); bg.raycastTarget = true;
            var txtGo = new GameObject("T", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            var txt = UIBuilder.Tmp(txtGo, label, 12.5f, TextAnchor.MiddleCenter, Theme.Text); txt.raycastTarget = false;
            UI.ClickHandler.Attach(go, () => { Deselect(); onClick(); });
        }

        private static void Deselect() { try { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); } catch { } }

        // ── delete confirmation popup (modal over the panel) ──────────────────

        private static GameObject _confirmGo;

        private static void HideConfirm()
        {
            if (_confirmGo != null) { UnityEngine.Object.Destroy(_confirmGo); _confirmGo = null; }
        }

        private static void ShowConfirm(string message, Action onYes)
        {
            HideConfirm();
            _confirmGo = new GameObject("Confirm", typeof(RectTransform));
            _confirmGo.transform.SetParent(K.PanelGo.transform, false);
            var r = (RectTransform)_confirmGo.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            r.SetAsLastSibling();
            var dim = _confirmGo.AddComponent<RoundedRectGraphic>();
            dim.Radius = 0f; dim.color = new Color(0f, 0f, 0f, 0.55f); dim.raycastTarget = true;  // blocks clicks behind

            var card = new GameObject("Card", typeof(RectTransform));
            card.transform.SetParent(_confirmGo.transform, false);
            var cr = (RectTransform)card.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f); cr.pivot = new Vector2(0.5f, 0.5f);
            cr.sizeDelta = new Vector2(340f, 130f); cr.anchoredPosition = Vector2.zero;
            var cbg = card.AddComponent<RoundedRectGraphic>();
            cbg.Radius = 10f; cbg.color = new Color(0.12f, 0.12f, 0.14f, 0.99f);
            cbg.BorderWidth = 1f; cbg.BorderColor = new Color(1f, 1f, 1f, 0.16f); cbg.raycastTarget = true;

            var mGo = new GameObject("Msg", typeof(RectTransform));
            mGo.transform.SetParent(card.transform, false);
            var mr = (RectTransform)mGo.transform;
            mr.anchorMin = new Vector2(0f, 1f); mr.anchorMax = new Vector2(1f, 1f); mr.pivot = new Vector2(0.5f, 1f);
            mr.offsetMin = new Vector2(14f, -74f); mr.offsetMax = new Vector2(-14f, -14f);
            UIBuilder.Tmp(mGo, message, 13f, TextAnchor.UpperCenter, Theme.Text).raycastTarget = false;

            MakeButton(cr, Loc.T("Delete"), 340f - 14f - 100f, -130f + 14f + BtnH, 100f, BtnH, () => { HideConfirm(); onYes(); }, danger: true);
            MakeButton(cr, Loc.T("Cancel"), 14f, -130f + 14f + BtnH, 100f, BtnH, HideConfirm);
        }

        private static ShapePathGraphic MakePreview(RectTransform parent, float x, float y, float w, float h)
        {
            var go = new GameObject("Preview", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>(); bg.Radius = 6f; bg.color = new Color(0f, 0f, 0f, 0.35f);
            var pgo = new GameObject("Path", typeof(RectTransform));
            pgo.transform.SetParent(go.transform, false);
            var pr = (RectTransform)pgo.transform;
            pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one; pr.offsetMin = pr.offsetMax = Vector2.zero;
            return pgo.AddComponent<ShapePathGraphic>();
        }

        private static TMP_InputField MakeMiniField(RectTransform parent, float x, float y, float w, float h, string text, Action<string> onEnd, bool integer = false)
        {
            var go = new GameObject("Field", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
            return MakeFieldInner(go, text, onEnd, integer);
        }

        // Same as MakeMiniField but hosted in a vertically-centred rail row (pivot 0,0.5).
        private static TMP_InputField MakeMiniFieldIn(RectTransform parent, float x, float y, float w, float h, string text, Action<string> onEnd)
        {
            var go = new GameObject("Field", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f); r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f); r.sizeDelta = new Vector2(w, h);
            return MakeFieldInner(go, text, onEnd, false);
        }

        private static TMP_InputField MakeFieldInner(GameObject go, string text, Action<string> onEnd, bool integer)
        {
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f; bg.color = new Color(0f, 0f, 0f, 0.35f);
            bg.BorderWidth = 1f; bg.BorderColor = new Color(1f, 1f, 1f, 0.14f); bg.raycastTarget = true;
            var tGo = new GameObject("T", typeof(RectTransform));
            tGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)tGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(6f, 0f); tr.offsetMax = new Vector2(-4f, 0f);
            var txt = UIBuilder.Tmp(tGo, text ?? "", 12f, TextAnchor.MiddleLeft, Theme.Text); txt.richText = false;
            var field = UIBuilder.BuildInputField(go, txt);
            field.lineType = TMP_InputField.LineType.SingleLine;
            if (integer) field.contentType = TMP_InputField.ContentType.IntegerNumber;
            field.text = text ?? "";
            if (onEnd != null) field.onEndEdit.AddListener(s => onEnd(s));
            return field;
        }

        private static string Trim(double v) =>
            v.ToString(Math.Abs(v - Math.Round(v)) < 1e-9 ? "0" : "0.##", System.Globalization.CultureInfo.InvariantCulture);

        // ── scroll ─────────────────────────────────────────────────────────────

        private static void TickScroll()
        {
            if (_viewport == null || _content == null) return;
            float wheel = MainClass.WheelY;
            if (Mathf.Abs(wheel) < 0.01f) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_viewport, Input.mousePosition, null)) return;
            _scroll = Mathf.Clamp(_scroll + wheel * 60f, 0f, Mathf.Max(0f, _content.sizeDelta.y - _viewport.rect.height));
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.Script;
using Sapphire.UI;

namespace Sapphire
{
    /* The script editor: a code field with line numbers and syntax colouring, errors checked as you
       type, and Compile — which turns the script (ScriptLang) into ordinary events on the level so
       it plays unmodded. Compiled events all carry an `sphx` tag, so Compile first removes the
       previous output and Remove clears it.

       Colouring is a second TMP text laid exactly over the input field's own (transparent) text:
       the field keeps plain text, so the caret, selection and clipboard behave normally, and the
       overlay follows the field's internal scroll every frame. */
    internal static class EditorScript
    {
        private static readonly PanelKit K = new PanelKit("SapphireScript", 909, PanelW, focusable: true);
        private const float PanelW = 580f, HeaderH = 28f, BarH = 30f, DiagH = 120f, Pad = 8f, GutterW = 34f, FontSz = 13f;
        private static Vector2 _size = new Vector2(PanelW, 540f);
        private static bool _open, _help;
        private static TMP_InputField _field;
        private static TextMeshProUGUI _txt, _hl, _gutter, _diag, _status;
        private static LevelVars.ScriptBox _shown;
        private static float _lintAt = -1f;
        private static List<Diag> _diags = new List<Diag>();
        private static Output _out;

        internal static PanelKit Kit => K;
        internal static bool IsOpen => _open;
        internal static void SetOpen(bool v) { _open = v; }
        internal static void Toggle() => SetOpen(!_open);
        internal static bool TabAvailable() => EditorEventTray.TabAvailable();

        internal static void Tick()
        {
            if (!_open || !TabAvailable()) { K.Show(false); return; }
            if (!K.Built) BuildShell();
            var t = LevelVars.Current;
            var box = t != null ? t.Script : null;
            if (box != _shown)
            {
                _shown = box;
                if (_field != null) _field.SetTextWithoutNotify(box != null ? box.Text ?? "" : "");
                Relint();
            }
            if (_lintAt > 0f && Time.unscaledTime >= _lintAt) Relint();
            K.Show(true);
            SyncOverlay();
        }

        internal static void Dispose()
        {
            K.Dispose();
            _field = null; _txt = _hl = _gutter = _diag = _status = null;
            _shown = null;
        }

        // ── editing ─────────────────────────────────────────────────────────────

        private static void OnEdit(string text)
        {
            if (_shown != null) _shown.Text = text;
            Paint(text);
            _lintAt = Time.unscaledTime + 0.25f;   // re-check after a short pause, not per keystroke
        }

        private static Dictionary<string, int> TemplateCounts()
        {
            var counts = new Dictionary<string, int>();
            try
            {
                foreach (var e in scnEditor.instance.events)
                {
                    if (e == null || IsGenerated(e)) continue;
                    foreach (var tag in Tags(e)) { int n; counts.TryGetValue(tag, out n); counts[tag] = n + 1; }
                }
            }
            catch { }
            return counts;
        }

        private static void Relint()
        {
            _lintAt = -1f;
            _diags = new List<Diag>();
            var src = _field != null ? _field.text : (_shown != null ? _shown.Text : "");
            var counts = TemplateCounts();
            var prog = Parser.Parse(src, _diags);
            _out = Compiler.Compile(prog, tag => { int n; return counts.TryGetValue(tag, out n) ? n : 0; }, _diags);
            _hostTile = prog.Host;
            Paint(src);
            ShowDiags();
        }

        private static int _hostTile;

        private static void ShowDiags()
        {
            if (_diag == null) return;
            if (_help) { _diag.text = Loc.T(HelpText); return; }
            var sb = new StringBuilder();
            foreach (var d in _diags.OrderBy(x => x.Line))
                sb.Append(d.Warning ? "<color=#E8C167>" : "<color=#FF7A7A>")
                  .Append(Loc.T("Line")).Append(' ').Append(d.Line).Append(" · ").Append(Esc(d.Msg)).Append("</color>\n");
            int errs = _diags.Count(x => !x.Warning);
            if (errs == 0 && _out != null && _out.Slots + _out.Events.Count > 0)
                sb.Append("<color=#8FD19E>").Append(Loc.T("Ready")).Append(" · ").Append(_out.States).Append(' ').Append(Loc.T("states"))
                  .Append(" · ").Append(_out.Slots).Append(' ').Append(Loc.T("keys")).Append(" · ").Append(_out.Events.Count).Append(' ')
                  .Append(Loc.T("events")).Append(" · ").Append(Loc.T("host tile")).Append(" #").Append(_hostTile).Append("</color>");
            _diag.text = sb.ToString();
        }

        // ── compile into the level ──────────────────────────────────────────────

        /* Path and timing are fixed when the level loads, so these can't be fired by a key or a
           judgment — a copy would just sit on the host tile and change the chart there. */
        private static readonly HashSet<string> NotTriggerable = new HashSet<string>
        {
            "SetSpeed", "Twirl", "Pause", "Hold", "MultiPlanet", "FreeRoam", "FreeRoamTwirl", "FreeRoamRemove",
            "PositionTrack", "AutoPlayTiles", "ScaleMargin", "ScaleRadius", "Checkpoint", "SetInputEvent",
            "SetConditionalEvents", "RepeatEvents", "EditorComment", "Bookmark",
        };

        private static void Compile()
        {
            Relint();
            var ed = scnEditor.instance;
            if (ed == null) return;
            if (_diags.Any(d => !d.Warning)) { Notify(ed, Loc.T("Fix the errors first")); return; }
            string why;
            if (!VanillaLogic.Registry(out why)) { Notify(ed, why); return; }
            int maxFloor = _out.Events.Count > 0 ? _out.Events.Max(e => e.Floor) : 0;
            if (maxFloor >= ed.floors.Count) { Notify(ed, Loc.T("A tile in the script is past the end of the level") + " (#" + maxFloor + ")"); return; }

            int made = 0, skipped = 0;
            var used = new HashSet<ADOFAI.LevelEvent>();
            using (new SaveStateScope(ed))
            {
                RemoveGenerated(ed);
                var byTag = new Dictionary<string, List<ADOFAI.LevelEvent>>();
                foreach (var e in ed.events.ToList())
                {
                    if (e == null) continue;
                    foreach (var tag in Tags(e))
                    {
                        List<ADOFAI.LevelEvent> l;
                        if (!byTag.TryGetValue(tag, out l)) byTag[tag] = l = new List<ADOFAI.LevelEvent>();
                        l.Add(e);
                    }
                }
                foreach (var spec in _out.Events)
                {
                    if (spec.Type == "copy")
                    {
                        List<ADOFAI.LevelEvent> l;
                        if (!byTag.TryGetValue(spec.Template, out l)) continue;
                        foreach (var tmpl in l)
                        {
                            if (NotTriggerable.Contains(tmpl.eventType.ToString()) || !tmpl.ContainsKey("eventTag")) { skipped++; continue; }
                            var c = tmpl.Copy();
                            c.floor = spec.Floor;
                            c["eventTag"] = spec.Props["eventTag"];
                            c.active = true;
                            ed.events.Add(c);
                            used.Add(tmpl);
                            made++;
                        }
                        continue;
                    }
                    var ev = new ADOFAI.LevelEvent(spec.Floor, (ADOFAI.LevelEventType)Enum.Parse(typeof(ADOFAI.LevelEventType), spec.Type));
                    foreach (var kv in spec.Props) VanillaLogic.Set(ev, kv.Key, kv.Value);
                    ed.events.Add(ev);
                    made++;
                }
                // Templates are definitions: they only run as the compiled copies.
                foreach (var tmpl in used) tmpl.active = false;
                try { ed.ApplyEventsToFloors(); } catch { }
                try { ed.RemakePath(true, true); } catch { }
            }
            EditorEventPanel.Refresh();
            Notify(ed, Loc.T("Compiled") + " · " + made + " " + Loc.T("events") + " · " + Loc.T("host tile") + " #" + _hostTile
                + (skipped > 0 ? " · " + skipped + " " + Loc.T("templates skipped (path/timing events can't be triggered)") : ""));
            Relint();
        }

        private static void Remove()
        {
            var ed = scnEditor.instance;
            if (ed == null) return;
            int n;
            using (new SaveStateScope(ed))
            {
                n = RemoveGenerated(ed);
                // Re-enable the templates this script runs — compile switched them off.
                var tags = new HashSet<string>(_out != null ? _out.Events.Where(e => e.Type == "copy").Select(e => e.Template) : Enumerable.Empty<string>());
                foreach (var e in ed.events) if (e != null && Tags(e).Any(tags.Contains)) e.active = true;
                try { ed.ApplyEventsToFloors(); } catch { }
                try { ed.RemakePath(true, true); } catch { }
            }
            EditorEventPanel.Refresh();
            Notify(ed, Loc.T("Removed compiled events") + " · " + n);
            Relint();
        }

        private static int RemoveGenerated(scnEditor ed)
        {
            int n = 0;
            for (int i = ed.events.Count - 1; i >= 0; i--)
                if (ed.events[i] != null && IsGenerated(ed.events[i])) { ed.events.RemoveAt(i); n++; }
            return n;
        }

        private static IEnumerable<string> Tags(ADOFAI.LevelEvent e)
        {
            string t = null;
            try { if (e.ContainsKey("eventTag")) t = e["eventTag"] as string; } catch { }
            return string.IsNullOrEmpty(t) ? Enumerable.Empty<string>() : t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        // Anything the compiler wrote: its own tag, or a trigger pointing at a compiled group.
        private static bool IsGenerated(ADOFAI.LevelEvent e)
        {
            if (Tags(e).Any(t => t.StartsWith(Compiler.Prefix))) return true;
            string type = e.eventType.ToString();
            if (type != "SetInputEvent" && type != "SetConditionalEvents") return false;
            try
            {
                var d = e.GetData();
                foreach (var kv in d) if (kv.Key.EndsWith("Tag") && kv.Value is string && ((string)kv.Value).StartsWith(Compiler.Prefix)) return true;
            }
            catch { }
            return false;
        }

        private static void Notify(scnEditor ed, string msg)
        {
            try { ed.ShowNotification(msg, null, 0f); } catch { }
            SapphireLog.Log("Script: " + msg);
        }

        // ── colouring ───────────────────────────────────────────────────────────

        private static void Paint(string src)
        {
            if (_hl == null) return;
            var errLines = new HashSet<int>(_diags.Where(d => !d.Warning).Select(d => d.Line));
            var lines = (src ?? "").Replace("\r", "").Split('\n');
            var sb = new StringBuilder();
            var nums = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) { sb.Append('\n'); nums.Append('\n'); }
                nums.Append(i + 1);
                bool err = errLines.Contains(i + 1);
                if (err) sb.Append("<mark=#FF404038>");
                PaintLine(lines[i], sb);
                if (err) sb.Append("</mark>");
            }
            _hl.text = sb.ToString();
            if (_gutter != null) _gutter.text = nums.ToString();
        }

        private static void PaintLine(string line, StringBuilder sb)
        {
            int hash = line.IndexOf('#');
            string code = hash >= 0 ? line.Substring(0, hash) : line;
            int i = 0;
            bool afterRun = false;
            while (i < code.Length)
            {
                char c = code[i];
                if (char.IsLetter(c) || c == '_')
                {
                    int st = i;
                    while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '_' || (afterRun && code[i] == '-'))) i++;
                    string w = code.Substring(st, i - st);
                    string col = afterRun ? "#8FD19E"
                        : Lang.Keywords.Contains(w) ? "#8AB4FF"
                        : Lang.Keys.Contains(w) || Lang.Judgments.ContainsKey(w) ? "#D79BFF"
                        : w == "on" || w == "off" || w == "true" || w == "false" ? "#E8C167"
                        : null;
                    afterRun = w == "run";
                    if (col != null) sb.Append("<color=").Append(col).Append('>').Append(w).Append("</color>");
                    else sb.Append(w);
                    continue;
                }
                if (char.IsDigit(c))
                {
                    int st = i;
                    while (i < code.Length && char.IsDigit(code[i])) i++;
                    sb.Append("<color=#F2A36B>").Append(code, st, i - st).Append("</color>");
                    continue;
                }
                sb.Append(c == '<' ? "<noparse><</noparse>" : c.ToString());
                i++;
            }
            if (hash >= 0) sb.Append("<color=#7C8595>").Append(Esc(line.Substring(hash))).Append("</color>");
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "<noparse><</noparse>");

        // The overlay and line numbers ride the field's own scroll.
        private static void SyncOverlay()
        {
            if (_txt == null || _hl == null) return;
            var tr = _txt.rectTransform;
            var hr = _hl.rectTransform;
            if (hr.anchoredPosition != tr.anchoredPosition) hr.anchoredPosition = tr.anchoredPosition;
            if (hr.sizeDelta != tr.sizeDelta) hr.sizeDelta = tr.sizeDelta;
            if (_gutter != null)
            {
                var gr = _gutter.rectTransform;
                var gp = new Vector2(gr.anchoredPosition.x, tr.anchoredPosition.y);
                if (gr.anchoredPosition != gp) gr.anchoredPosition = gp;
            }
        }

        // ── shell ───────────────────────────────────────────────────────────────

        private static void BuildShell()
        {
            K.Rebuild(Loc.T("Script"), () => _open = false, new Vector2(460f, -110f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            K.OnDragEnd = () => K.SnapDockOnDragEnd();
            ResizeHandle.AttachAll(panel, true, 380f, 300f);

            // toolbar
            var bar = Rect("Bar", K.PanelGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(Pad, -(HeaderH + BarH)), new Vector2(-Pad, -HeaderH));
            float x = 0f;
            x = BarButton(bar, Loc.T("Compile"), x, 84f, Compile, Loc.T("Write the script into the level as ordinary events (replaces the previous output)"), true);
            x = BarButton(bar, Loc.T("Remove"), x, 74f, Remove, Loc.T("Delete the compiled events and switch the templates back on"), false);
            x = BarButton(bar, Loc.T("Help"), x, 60f, () => { _help = !_help; ShowDiags(); }, Loc.T("Show the language reference"), false);
            var stGo = Rect("S", bar, Vector2.zero, Vector2.one, new Vector2(x + 6f, 0f), Vector2.zero);
            _status = UIBuilder.Tmp(stGo.gameObject, Loc.T("Runs in the unmodded game: compiles to SetInputEvent / SetConditionalEvents"), 11f,
                TextAnchor.MiddleLeft, Theme.TextMuted);
            _status.textWrappingMode = TextWrappingModes.NoWrap;
            _status.overflowMode = TextOverflowModes.Ellipsis;
            _status.raycastTarget = false;

            // editor
            var box = Rect("Editor", K.PanelGo.transform, Vector2.zero, Vector2.one,
                new Vector2(Pad, DiagH + Pad * 2f), new Vector2(-Pad, -(HeaderH + BarH + Pad)));
            var bg = box.gameObject.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f; bg.color = new Color(0.04f, 0.045f, 0.06f, 1f);
            bg.BorderWidth = 1f; bg.BorderColor = new Color(1f, 1f, 1f, 0.10f);
            box.gameObject.AddComponent<RectMask2D>();

            var gut = Rect("Gutter", box, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 6f), new Vector2(GutterW, -6f));
            _gutter = UIBuilder.Tmp(gut.gameObject, "1", FontSz, TextAnchor.UpperRight, new Color(1f, 1f, 1f, 0.28f));
            _gutter.textWrappingMode = TextWrappingModes.NoWrap;
            _gutter.raycastTarget = false;

            var fGo = Rect("Code", box, Vector2.zero, Vector2.one, new Vector2(GutterW + 8f, 0f), Vector2.zero);
            var txtGo = Rect("T", fGo, Vector2.zero, Vector2.one, new Vector2(0f, 6f), new Vector2(-6f, -6f));
            var hlGo = Rect("H", fGo, Vector2.zero, Vector2.one, new Vector2(0f, 6f), new Vector2(-6f, -6f));
            hlGo.SetSiblingIndex(0);   // under the (transparent) field text and its caret
            _hl = UIBuilder.Tmp(hlGo.gameObject, "", FontSz, TextAnchor.UpperLeft, Theme.Text);
            _hl.textWrappingMode = TextWrappingModes.NoWrap;
            _hl.richText = true;
            _hl.raycastTarget = false;
            _txt = UIBuilder.Tmp(txtGo.gameObject, "", FontSz, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0f));
            _txt.textWrappingMode = TextWrappingModes.NoWrap;
            _txt.richText = false;
            fGo.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);   // click target
            _field = UIBuilder.BuildInputField(fGo.gameObject, _txt);
            _field.lineType = TMP_InputField.LineType.MultiLineNewline;
            _field.richText = false;
            _field.onFocusSelectAll = false;
            _field.scrollSensitivity = 3f;
            _field.onValueChanged.AddListener(OnEdit);

            // diagnostics / help
            var dGo = Rect("Diag", K.PanelGo.transform, Vector2.zero, new Vector2(1f, 0f), new Vector2(Pad, Pad), new Vector2(-Pad, Pad + DiagH));
            var dbg = dGo.gameObject.AddComponent<RoundedRectGraphic>();
            dbg.Radius = 6f; dbg.color = new Color(1f, 1f, 1f, 0.04f);
            dGo.gameObject.AddComponent<RectMask2D>();
            var dtGo = Rect("T", dGo, Vector2.zero, Vector2.one, new Vector2(8f, 6f), new Vector2(-8f, -6f));
            _diag = UIBuilder.Tmp(dtGo.gameObject, "", 11.5f, TextAnchor.UpperLeft, Theme.Text);
            _diag.textWrappingMode = TextWrappingModes.Normal;
            _diag.richText = true;
            _diag.raycastTarget = false;

            _shown = null;   // Tick loads the level's script into the fresh field
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 amin, Vector2 amax, Vector2 omin, Vector2 omax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = amin; r.anchorMax = amax; r.offsetMin = omin; r.offsetMax = omax;
            return r;
        }

        private static float BarButton(RectTransform bar, string label, float x, float w, Action onClick, string tip, bool accent)
        {
            var r = Rect("B", bar, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(x, 3f), new Vector2(x + w, -3f));
            var bg = r.gameObject.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = accent ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.35f) : new Color(1f, 1f, 1f, 0.08f);
            bg.raycastTarget = true;
            var l = Rect("L", r, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            UIBuilder.Tmp(l.gameObject, label, 12f, TextAnchor.MiddleCenter, Theme.Text).raycastTarget = false;
            ClickHandler.Attach(r.gameObject, onClick);
            HoverTip.Attach(r.gameObject, tip);
            return x + w + 6f;
        }

        private const string HelpText =
            "<b>Variables</b>   flag lights   ·   state mode = calm, wild   ·   counter hits 0..3 wrap   (optional  = start)\n" +
            "<b>Keys</b>   on key Up:   (Up Down Left Right Action1 Action2 Confirm Any · add release / nohit)\n" +
            "<b>Statements</b>   toggle lights   ·   mode = wild   ·   hits += 1   ·   run flash   ·   if hits == 3 and not lights:  …  else:\n" +
            "<b>When</b>   when mode == wild:   run the tagged events each time the condition becomes true\n" +
            "<b>Judgments</b>   on miss at 40:   run shake   (perfect earlyPerfect latePerfect veryEarly veryLate tooEarly tooLate barely hit miss loss checkpoint)\n" +
            "<b>run tag</b> copies every event tagged `tag` onto the host tile (host 0 by default); those templates are switched off. Keys are also hits unless `nohit`. Test from the level's start.";
    }
}

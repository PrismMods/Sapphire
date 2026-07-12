using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Event presets for the Inspector tool: save the current capture as a named preset, load one
       back into the capture buffer with a click (then stamp tiles as usual), rename (right-click
       a row → inline edit, Enter commits), delete (×). Persisted in Settings as type-tagged
       key/value pairs; decoding builds a fresh default event of the type and overwrites only the
       keys it knows, so presets survive game updates that add/remove fields. The menu sits below
       the toolbar while the Inspector tool is active. */
    internal static class EditorPresets
    {
        private const char Sep = '\u001F';

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static int _shownCount = -1;
        private static int _renameIdx = -1;
        private static TMPro.TMP_InputField _renameField;
        // which preset is currently LOADED as the capture (accent row); a fresh manual capture
        // (buffer version moved without us) clears it
        private static int _loadedIdx = -1;
        private static int _loadedVersion = -1;
        private static readonly List<RoundedRectGraphic> _rowBgs = new List<RoundedRectGraphic>();

        internal static void Tick()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            var s = MainClass.Settings;
            bool want = ed != null && !ed.playMode && s != null && MainClass.EditorSuiteOn
                        && EditorToolbar.InspectorActive;
            if (!want)
            {
                if (_panelGo != null && _panelGo.activeSelf) { _panelGo.SetActive(false); _renameIdx = -1; }
                return;
            }
            if (_canvasGo == null) EnsureCanvas();
            int count = s.EventPresets.Count;
            if (_panelGo == null || count != _shownCount) Rebuild(s);
            if (!_panelGo.activeSelf) _panelGo.SetActive(true);
            if (EditorToolbar.InspectorVersion != _loadedVersion && _loadedIdx >= 0)
            { _loadedIdx = -1; SyncTints(); }
        }

        internal static void Dispose()
        {
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _panelGo = null; _shownCount = -1; _renameIdx = -1; _renameField = null;
        }

        // ── serialization ───────────────────────────────────────────────────
        private static System.Reflection.FieldInfo _dataFi;

        private static Dictionary<string, object> DataOf(ADOFAI.LevelEvent e)
        {
            if (_dataFi == null)
                _dataFi = typeof(ADOFAI.LevelEvent).GetField("data",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return _dataFi?.GetValue(e) as Dictionary<string, object>;
        }

        private static PresetEvent Encode(ADOFAI.LevelEvent e)
        {
            var p = new PresetEvent { Type = (int)e.eventType };
            var data = DataOf(e);
            if (data == null) return p;
            foreach (var kv in data)
            {
                string tag = null, val = null;
                var v = kv.Value;
                if (v is bool b) { tag = "b"; val = b ? "1" : "0"; }
                else if (v is int i) { tag = "i"; val = i.ToString(CultureInfo.InvariantCulture); }
                else if (v is long l) { tag = "i"; val = l.ToString(CultureInfo.InvariantCulture); }
                else if (v is float f) { tag = "f"; val = f.ToString("R", CultureInfo.InvariantCulture); }
                else if (v is double d) { tag = "f"; val = d.ToString("R", CultureInfo.InvariantCulture); }
                else if (v is string str) { tag = "s"; val = str; }
                else if (v is Vector2 v2)
                { tag = "v"; val = v2.x.ToString("R", CultureInfo.InvariantCulture) + "|" + v2.y.ToString("R", CultureInfo.InvariantCulture); }
                else if (v != null && v.GetType().IsEnum) { tag = "e"; val = v.ToString(); }
                if (tag != null) p.Pairs.Add(kv.Key + Sep + tag + Sep + val);
            }
            return p;
        }

        private static ADOFAI.LevelEvent Decode(PresetEvent p)
        {
            var ev = new ADOFAI.LevelEvent(0, (ADOFAI.LevelEventType)p.Type); // default-populated
            var data = DataOf(ev);
            if (data == null) return ev;
            foreach (var pair in p.Pairs)
            {
                var parts = pair.Split(Sep);
                if (parts.Length != 3) continue;
                string key = parts[0], tag = parts[1], val = parts[2];
                if (!data.TryGetValue(key, out var cur)) continue; // key gone in this game version
                try
                {
                    switch (tag)
                    {
                        case "b": ev[key] = val == "1"; break;
                        case "i": ev[key] = int.Parse(val, CultureInfo.InvariantCulture); break;
                        case "f": ev[key] = float.Parse(val, CultureInfo.InvariantCulture); break;
                        case "s": ev[key] = val; break;
                        case "v":
                            var xy = val.Split('|');
                            ev[key] = new Vector2(
                                float.Parse(xy[0], CultureInfo.InvariantCulture),
                                float.Parse(xy[1], CultureInfo.InvariantCulture));
                            break;
                        case "e":
                            if (cur != null && cur.GetType().IsEnum)
                                ev[key] = Enum.Parse(cur.GetType(), val);
                            break;
                    }
                }
                catch { } // one bad field shouldn't kill the preset
            }
            return ev;
        }

        // ── actions ─────────────────────────────────────────────────────────
        private static void SaveCurrent()
        {
            var s = MainClass.Settings;
            var buf = EditorToolbar.InspectorBuffer;
            if (s == null || buf == null || buf.Count == 0) return;
            var preset = new EventPreset { Name = Loc.T("Preset") + " " + (s.EventPresets.Count + 1) };
            foreach (var e in buf) if (e != null) preset.Events.Add(Encode(e));
            s.EventPresets.Add(preset);
            _shownCount = -1; // rebuild
        }

        private static void LoadPreset(EventPreset p, int idx)
        {
            var evs = new List<ADOFAI.LevelEvent>();
            foreach (var pe in p.Events)
            {
                try { evs.Add(Decode(pe)); }
                catch (Exception ex) { SapphireLog.Log("Presets: decode failed: " + ex.Message); }
            }
            EditorToolbar.LoadInspectorBuffer(evs);
            _loadedIdx = idx;
            _loadedVersion = EditorToolbar.InspectorVersion;
            SyncTints();
        }

        private static void SyncTints()
        {
            for (int i = 0; i < _rowBgs.Count; i++)
            {
                if (_rowBgs[i] == null) continue;
                _rowBgs[i].color = i == _loadedIdx
                    ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f)
                    : new Color(1f, 1f, 1f, 0.05f);
            }
        }

        // ── UI ──────────────────────────────────────────────────────────────
        private static void EnsureCanvas()
        {
            _canvasGo = new GameObject("SapphirePresets", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 906;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
        }

        private static void Rebuild(Settings s)
        {
            if (_panelGo != null) UnityEngine.Object.Destroy(_panelGo);
            _renameField = null; // _renameIdx survives: the rebuild is what RENDERS rename mode
            _rowBgs.Clear();
            _shownCount = s.EventPresets.Count;

            const float w = 240f, rowH = 28f, pad = 8f, gap = 4f;
            int rows = Mathf.Max(1, s.EventPresets.Count) + 1; // list (or empty hint) + save row
            float h = pad * 2f + rows * (rowH + gap) - gap + 20f;

            _panelGo = new GameObject("Panel", typeof(RectTransform));
            _panelGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_panelGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -64f); // submenu slot below the toolbar
            r.sizeDelta = new Vector2(w, h);
            var bg = _panelGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 10f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.94f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;

            float y = -pad;
            MakeLabel(Loc.T("Presets"), pad, y, w - pad * 2f, 16f); y -= 20f;

            if (s.EventPresets.Count == 0)
            {
                MakeLabel(Loc.T("(none — capture a tile, then Save)"), pad, y, w - pad * 2f, rowH);
                y -= rowH + gap;
            }
            for (int i = 0; i < s.EventPresets.Count; i++)
            {
                int idx = i;
                var preset = s.EventPresets[i];
                MakePresetRow(preset, idx, pad, y, w - pad * 2f, rowH);
                y -= rowH + gap;
            }

            MakeButton(Loc.T("+ Save capture"), pad, y, w - pad * 2f, rowH, SaveCurrent);
            SyncTints();
        }

        private static void MakePresetRow(EventPreset preset, int idx, float x, float y, float w, float h)
        {
            var rowGo = new GameObject("Row" + idx, typeof(RectTransform));
            rowGo.transform.SetParent(_panelGo.transform, false);
            var rr = (RectTransform)rowGo.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(0f, 1f);
            rr.pivot = new Vector2(0f, 1f);
            rr.anchoredPosition = new Vector2(x, y);
            rr.sizeDelta = new Vector2(w, h);
            var bg = rowGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(1f, 1f, 1f, 0.05f);
            bg.raycastTarget = true;
            while (_rowBgs.Count <= idx) _rowBgs.Add(null);
            _rowBgs[idx] = bg;

            if (_renameIdx == idx)
            {
                // inline rename: field replaces the label; Enter/typing-end commits
                var txtGo = new GameObject("Text", typeof(RectTransform));
                txtGo.transform.SetParent(rowGo.transform, false);
                var tr = (RectTransform)txtGo.transform;
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
                tr.offsetMin = new Vector2(8f, 0f); tr.offsetMax = new Vector2(-30f, 0f);
                var txt = UIBuilder.Tmp(txtGo, preset.Name, 13f, TextAnchor.MiddleLeft, Theme.Text);
                txt.richText = false;
                _renameField = UIBuilder.BuildInputField(rowGo, txt);
                _renameField.lineType = TMPro.TMP_InputField.LineType.SingleLine;
                _renameField.text = preset.Name;
                _renameField.onEndEdit.AddListener(t =>
                {
                    if (!string.IsNullOrEmpty(t)) preset.Name = t;
                    _renameIdx = -1;
                    _shownCount = -1; // rebuild with the new name
                });
                _renameField.Select();
                _renameField.ActivateInputField();
            }
            else
            {
                var lblGo = new GameObject("L", typeof(RectTransform));
                lblGo.transform.SetParent(rowGo.transform, false);
                var lr = (RectTransform)lblGo.transform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = new Vector2(8f, 0f); lr.offsetMax = new Vector2(-30f, 0f);
                var lbl = UIBuilder.Tmp(lblGo,
                    preset.Name + "  ·  " + preset.Events.Count, 13f, TextAnchor.MiddleLeft, Theme.Text);
                lbl.raycastTarget = false;

                var click = UI.ClickHandler.Attach(rowGo, () => LoadPreset(preset, idx));
                click.OnRightClick = () => { _renameIdx = idx; _shownCount = -1; };
            }

            // delete ×
            var delGo = new GameObject("Del", typeof(RectTransform));
            delGo.transform.SetParent(rowGo.transform, false);
            var dr = (RectTransform)delGo.transform;
            dr.anchorMin = dr.anchorMax = new Vector2(1f, 0.5f);
            dr.pivot = new Vector2(1f, 0.5f);
            dr.anchoredPosition = new Vector2(-4f, 0f);
            dr.sizeDelta = new Vector2(22f, 22f);
            var dbg = delGo.AddComponent<RoundedRectGraphic>();
            dbg.Radius = 5f;
            dbg.color = new Color(1f, 1f, 1f, 0.04f);
            dbg.raycastTarget = true;
            var dlGo = new GameObject("L", typeof(RectTransform));
            dlGo.transform.SetParent(delGo.transform, false);
            var dlr = (RectTransform)dlGo.transform;
            dlr.anchorMin = Vector2.zero; dlr.anchorMax = Vector2.one;
            dlr.offsetMin = Vector2.zero; dlr.offsetMax = Vector2.zero;
            var dl = UIBuilder.Tmp(dlGo, "×", 13f, TextAnchor.MiddleCenter, Theme.TextMuted);
            dl.raycastTarget = false;
            UI.ClickHandler.Attach(delGo, () =>
            {
                var s = MainClass.Settings;
                if (s != null && idx < s.EventPresets.Count) s.EventPresets.RemoveAt(idx);
                _loadedIdx = -1; // indices shifted; the capture buffer itself is untouched
                _shownCount = -1;
            });
        }

        private static void MakeLabel(string text, float x, float y, float w, float h)
        {
            var go = new GameObject("Lbl", typeof(RectTransform));
            go.transform.SetParent(_panelGo.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            var lbl = UIBuilder.Tmp(go, text, 12f, TextAnchor.MiddleLeft, Theme.TextMuted);
            lbl.raycastTarget = false;
        }

        private static void MakeButton(string text, float x, float y, float w, float h, Action onClick)
        {
            var go = new GameObject("Btn", typeof(RectTransform));
            go.transform.SetParent(_panelGo.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(1f, 1f, 1f, 0.08f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;
            var lblGo = new GameObject("L", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;
            var lbl = UIBuilder.Tmp(lblGo, text, 13f, TextAnchor.MiddleCenter, Theme.Text);
            lbl.raycastTarget = false;
            UI.ClickHandler.Attach(go, onClick);
        }
    }
}

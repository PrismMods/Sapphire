using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Sapphire.UI;

namespace Sapphire
{
    /* Magic Shape panel (toolbar cell 8) — Sapphire-native front end for MagicShapeEngine.
       Tabs: MULTIPLY (retime selection to a BPM/multiplier, optional auto-twirls or angle
       reshape) · CREATE (sweep a tile range into an N-vertex magic circle, fake-floor
       preview) · ROTATE (offset tile angles across a range).
       Based on MagicShapeMultiply (tjwogud, cont. JofoDuh) and AdofaiMappingHelper
       (Sprout34), integrated with permission. */
    internal static class EditorMagicShape
    {
        private static readonly PanelKit K = new PanelKit("SapphireMagicShape", 943, PanelW, focusable: true);
        private static bool _open;
        private static string _status = "";
        private static TextMeshProUGUI _statusTmp;
        private static string _layoutSig;

        // multiply state
        private static int _mMode;          // 0 target BPM, 1 multiplier
        private static float _mBpm = 100f, _mMult = 1f;
        private static int _mWriteAs;       // 0 BPM, 1 multiplier
        private static int _mTwirl;         // 0 keep, 1 strip, 2 internal, 3 external
        private static int _mIcon;          // 0 SetSpeed on top, 1 Twirl on top
        private static bool _mReshape;
        private static int _mCorr;          // 0 off, 1 → -1, 2 → 0, 3 → +1

        // create/rotate state (shared range fields per tab)
        private static int _cStart, _cEnd = -1;          // -1 = end of level (resolved lazily)
        private static int _cVerts = 4;
        private static bool _cInverse, _cPreview = true;
        private static int _rStart, _rEnd = -1;
        private static float _rDeg;
        private static int _tab;            // 0 multiply, 1 create, 2 rotate

        internal static bool IsOpen => _open;

        // fake-floor preview is live only while the CREATE tab is visible with Preview on
        internal static bool PreviewActive => _open && _tab == 1 && _cPreview && K.Visible;
        internal static bool Playing;       // set by Play/SwitchToEditMode patches
        private static bool _lastPreview;

        internal static void Toggle()
        {
            _open = !_open;
            _status = "";
            if (_open && SelectionRange(out int min, out int max) && min != max)
            { _cStart = min; _cEnd = max; _rStart = min; _rEnd = max; _layoutSig = null; }
            EditorToolbar.SyncMagicShapeHighlight();
        }

        internal static void Tick()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool want = _open && ed != null && !ed.playMode && MainClass.EditorSuiteOn
                       && MainClass.Settings != null && MainClass.Settings.FeatToolsMods;
            if (!want)
            {
                K.Show(false);
                SyncPreviewTransition(ed);
                return;
            }
            string sig = _tab + "|" + _mMode + "|" + (_mReshape ? 1 : 0);
            if (!K.Built || sig != _layoutSig)
            {
                _layoutSig = sig;
                Build();
            }
            K.Show(true);
            SyncPreviewTransition(ed);
        }

        internal static void Dispose()
        {
            K.Dispose();
            _statusTmp = null; _layoutSig = null;
            ClearFakeFloors();
        }

        // fake floors are (re)built inside MakeLevel — entering/leaving preview needs one RemakePath
        private static void SyncPreviewTransition(scnEditor ed)
        {
            bool pv = PreviewActive;
            if (pv == _lastPreview) return;
            _lastPreview = pv;
            try { if (ed != null && !ed.playMode) ed.RemakePath(); } catch { }
        }

        // ── actions ─────────────────────────────────────────────────────────

        private static void DoMultiply()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;
            Tuple<int, Dictionary<string, object>> result;
            try
            {
                using (new SaveStateScope(ed, false, false, false))
                {
                    MagicShapeEngine.TwirlDir? dir =
                        _mTwirl == 1 ? MagicShapeEngine.TwirlDir.None
                        : _mTwirl == 2 ? MagicShapeEngine.TwirlDir.Internal
                        : _mTwirl == 3 ? MagicShapeEngine.TwirlDir.External
                        : (MagicShapeEngine.TwirlDir?)null;
                    var writeAs = _mWriteAs == 0 ? MagicShapeEngine.MultiplyType.Bpm : MagicShapeEngine.MultiplyType.Multiplier;
                    var icon = _mIcon == 0 ? MagicShapeEngine.ShowEvent.SetSpeed : MagicShapeEngine.ShowEvent.Twirl;
                    if (_mMode == 0)
                        result = MagicShapeEngine.MultiplyWithBPM(_mBpm, writeAs, icon, dir);
                    else if (!_mReshape)
                        result = MagicShapeEngine.MultiplyWithMultiplier(_mMult, writeAs, icon, dir);
                    else
                        result = MagicShapeEngine.MultiplyWithAngle(_mMult, _mCorr == 0 ? (int?)null : _mCorr - 2);
                }
            }
            catch (Exception ex)
            {
                SapphireLog.Log("MagicShape: multiply failed: " + ex);
                SetStatus(Loc.T("Multiply failed — see log"));
                return;
            }
            SetStatus(ResultMessage(result));
        }

        private static string ResultMessage(Tuple<int, Dictionary<string, object>> r)
        {
            switch (r.Item1)
            {
                case 1: return Loc.T("Applied");
                case -2: return Loc.T("Select at least two tiles");
                case -3:
                    var types = r.Item2["eventTypes"] as List<ADOFAI.LevelEventType>;
                    string names = string.Join(", ", types.Select(t =>
                    { try { return RDString.Get("editor." + t); } catch { return t.ToString(); } }).ToArray());
                    return string.Format(Loc.T("Range contains unsupported events: {0}"), names);
                case -4: return string.Format(Loc.T("Angle exceeds 360° at tile {0} — set Angle fix"), r.Item2["floor"]);
                case -5: return string.Format(Loc.T("Tile {0}: old levels need 15° multiples"), r.Item2["floor"]);
                default: return Loc.T("Multiply failed — see log");
            }
        }

        private static void DoCreate()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;
            if (ADOBase.lm.isOldLevel) { SetStatus(Loc.T("Needs a modern (mesh-floor) level")); return; }
            if (_cVerts < 2) { SetStatus(Loc.T("Vertices must be at least 2")); return; }
            int len = ADOBase.lm.floorAngles.Length;
            int start = Mathf.Clamp(ResolveIdx(_cStart, len), 0, len);
            int end = Mathf.Clamp(ResolveIdx(_cEnd, len), 0, len);
            try
            {
                using (new SaveStateScope(ed))
                    MagicShapeEngine.CreateShape(start, end, _cVerts, _cInverse);
                _cPreview = false;
                _layoutSig = null; // preview toggle tint refresh
                SetStatus(Loc.T("Shape created"));
            }
            catch (Exception ex)
            {
                SapphireLog.Log("MagicShape: create failed: " + ex);
                SetStatus(Loc.T("Create failed — see log"));
            }
        }

        private static void DoRotate()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;
            if (ADOBase.lm.isOldLevel) { SetStatus(Loc.T("Needs a modern (mesh-floor) level")); return; }
            int len = ADOBase.lm.floorAngles.Length;
            int start = Mathf.Clamp(ResolveIdx(_rStart, len), 0, len);
            int end = Mathf.Clamp(ResolveIdx(_rEnd, len), 0, len);
            try
            {
                using (new SaveStateScope(ed))
                    MagicShapeEngine.Rotate(start, end, _rDeg);
                SetStatus(Loc.T("Applied"));
            }
            catch (Exception ex)
            {
                SapphireLog.Log("MagicShape: rotate failed: " + ex);
                SetStatus(Loc.T("Rotate failed — see log"));
            }
        }

        private static int ResolveIdx(int v, int len) => v < 0 ? len : v;

        private static bool SelectionRange(out int min, out int max) => PanelKit.SelectionRange(out min, out max);

        private static void Refresh() { _layoutSig = null; }

        private static void SetStatus(string s)
        {
            _status = s ?? "";
            if (_statusTmp != null) _statusTmp.text = _status;
        }

        // ── UI ──────────────────────────────────────────────────────────────

        private const float PanelW = 292f;
        private const float Pad = PanelKit.Pad, RowH = PanelKit.RowH, Gap = PanelKit.Gap;

        private static void Build()
        {
            K.LblW = 92f;
            K.Rebuild(Loc.T("Magic Shape"), Toggle, new Vector2(340f, -120f));

            float y = -34f;

            float tabW = (PanelW - Pad * 2f - Gap * 2f) / 3f;
            var tabNames = new[] { Loc.T("Multiply"), Loc.T("Create"), Loc.T("Rotate") };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var cellBg = K.Cell(tabNames[i], Pad + i * (tabW + Gap), y, tabW, RowH,
                    () => { _tab = idx; _status = ""; }, false);
                cellBg.color = PanelKit.Tint(_tab == i);
            }
            y -= RowH + 10f;

            if (_tab == 0) y = BuildMultiply(y);
            else if (_tab == 1) y = BuildCreate(y);
            else y = BuildRotate(y);

            _statusTmp = K.Status(_status, y);
            y -= 32f;
            K.Footer("MagicShapeMultiply · tjwogud, JofoDuh  —  MappingHelper · Sprout34", y);
            y -= 16f;
            K.SetHeight(y);
        }

        private static float BuildMultiply(float y)
        {
            float half = (PanelW - Pad * 2f - Gap) * 0.5f;
            K.Cell(Loc.T("Target BPM"), Pad, y, half, RowH, () => { _mMode = 0; }, false)
                .color = PanelKit.Tint(_mMode == 0);
            K.Cell(Loc.T("Multiplier"), Pad + half + Gap, y, half, RowH, () => { _mMode = 1; }, false)
                .color = PanelKit.Tint(_mMode == 1);
            y -= RowH + Gap;

            if (_mMode == 0)
                y = FieldRow(y, Loc.T("BPM"), _mBpm.ToString("0.###"), v =>
                { float f; if (float.TryParse(v, out f)) _mBpm = Mathf.Clamp(f, 0.001f, 10000f); });
            else
                y = FieldRow(y, "×", _mMult.ToString("0.#####"), v =>
                { float f; if (float.TryParse(v, out f)) _mMult = Mathf.Clamp(f, 1e-7f, 128f); });

            if (!(_mMode == 1 && _mReshape))
            {
                y = SegRow(y, Loc.T("Write as"), new[] { Loc.T("BPM"), "×" }, () => _mWriteAs, v => _mWriteAs = v);
                y = SegRow(y, Loc.T("Twirls"), new[] { Loc.T("Keep"), Loc.T("Strip"), Loc.T("Inner"), Loc.T("Outer") },
                    () => _mTwirl, v => _mTwirl = v);
                y = SegRow(y, Loc.T("Top icon"), new[] { Loc.T("Speed"), Loc.T("Twirl") }, () => _mIcon, v => _mIcon = v);
            }
            if (_mMode == 1)
            {
                ToggleRow(y, Loc.T("Reshape angles instead"), _mReshape, v => { _mReshape = v; });
                y -= RowH + Gap;
                if (_mReshape)
                    y = SegRow(y, Loc.T("Angle fix"), new[] { Loc.T("Off"), "-1", "0", "+1" }, () => _mCorr, v => _mCorr = v);
            }

            y -= 4f;
            K.Cell(Loc.T("Apply to selection"), Pad, y, PanelW - Pad * 2f, RowH + 2f, DoMultiply, true, true);
            return y - (RowH + 2f) - 10f;
        }

        private static float BuildCreate(float y)
        {
            y = RangeRow(y, () => _cStart, () => _cEnd, (a, b) => { _cStart = a; _cEnd = b; }, false);
            y = FieldRow(y, Loc.T("Vertices"), _cVerts.ToString(), v =>
            { int n; if (int.TryParse(v, out n)) _cVerts = Mathf.Max(2, n); });
            ToggleRow(y, Loc.T("Inverse direction"), _cInverse, v => _cInverse = v);
            y -= RowH + Gap;
            ToggleRow(y, Loc.T("Preview (ghost tiles)"), _cPreview, v => { _cPreview = v; });
            y -= RowH + Gap + 4f;
            K.Cell(Loc.T("Create shape"), Pad, y, PanelW - Pad * 2f, RowH + 2f, DoCreate, true, true);
            return y - (RowH + 2f) - 10f;
        }

        private static float BuildRotate(float y)
        {
            y = RangeRow(y, () => _rStart, () => _rEnd, (a, b) => { _rStart = a; _rEnd = b; }, true);
            y = FieldRow(y, Loc.T("Degrees"), _rDeg.ToString("0.###"), v =>
            { float f; if (float.TryParse(v, out f)) _rDeg = f; });
            y -= 4f;
            K.Cell(Loc.T("Rotate range"), Pad, y, PanelW - Pad * 2f, RowH + 2f, DoRotate, true, true);
            return y - (RowH + 2f) - 10f;
        }

        // ── row builders (thin wrappers over PanelKit's shared rows) ─────────

        private static float FieldRow(float y, string label, string value, Action<string> commit)
            => K.FieldRow(y, label, value, commit);

        private static float SegRow(float y, string label, string[] options, Func<int> get, Action<int> set)
            => K.SegRow(y, label, options, get, set);

        private static void ToggleRow(float y, string label, bool value, Action<bool> set)
            => K.ToggleRow(y, label, value, set);

        private static float RangeRow(float y, Func<int> getA, Func<int> getB, Action<int, int> set, bool rotateTab)
            => K.RangeRow(y, getA, getB, set, Refresh, SetStatus);

        // ── fake-floor preview (MagicShapeMultiply's, driven by panel state) ─

        internal static readonly List<scrFloor> FakeFloors = new List<scrFloor>();

        internal static void ClearFakeFloors()
        {
            foreach (var f in FakeFloors) if (f != null) UnityEngine.Object.DestroyImmediate(f.gameObject);
            FakeFloors.Clear();
        }

        internal static void OnMakeLevel()
        {
            ClearFakeFloors();
            if (scnEditor.instance == null || Playing || !PreviewActive) return;
            if (ADOBase.lm.isOldLevel) return;
            int len = ADOBase.lm.floorAngles.Length;
            int startIndex = Mathf.Clamp(ResolveIdx(_cStart, len), 0, len);
            int endIndex = Mathf.Clamp(ResolveIdx(_cEnd, len), 0, len);
            if (startIndex > endIndex) { int t = startIndex; startIndex = endIndex; endIndex = t; }
            int vertex = Math.Max(2, _cVerts);
            int inverse = _cInverse ? -1 : 1;

            GameObject fakeFloorHost = GameObject.Find("FakeFloors") ?? new GameObject("FakeFloors");
            scrFloor end = ADOBase.lm.listFloors[endIndex];

            // real tiles get re-stacked above so the ghost copies draw underneath
            int order = 100 + ADOBase.lm.listFloors.Count + (endIndex - startIndex + 1) * (vertex - 1);
            for (int i = 0; i <= endIndex; i++)
            {
                ADOBase.lm.listFloors[i].SetSortingOrder(order * 5);
                order--;
            }

            Vector3 vector = end.transform.position;
            scrFloor prev = end;
            double angle;

            for (int i = 1; i < vertex; i++)
                for (int j = startIndex; j <= endIndex; j++)
                {
                    float n = j == 0 ? 0 : ADOBase.lm.listFloors[j].floatDirection;
                    angle = n == 999 ? prev.entryangle : ((-n + 90 + (360f / vertex * i * inverse)) * Mathf.PI / 180);

                    prev.exitangle = angle;
                    vector += scrMisc.getVectorFromAngle(angle, scrController.instance.tileSize);

                    GameObject obj = UnityEngine.Object.Instantiate(ADOBase.lm.meshFloor, vector, Quaternion.identity);
                    obj.name = "FakeFloor (copy of #" + j + ")";
                    obj.transform.parent = fakeFloorHost.transform;
                    scrFloor floor = obj.GetComponent<scrFloor>();
                    floor.entryangle = (angle + Mathf.PI) % (Mathf.PI * 2);
                    prev.nextfloor = floor;
                    prev.midSpin = n == 999;
                    prev.UpdateAngle();
                    prev = floor;

                    floor.floorRenderer.color = new Color(1, 1, 1, 0.5f);
                    floor.editorNumText.letterText.gameObject.SetActive(false);
                    floor.SetSortingOrder(order * 5);
                    order--;
                    FakeFloors.Add(floor);
                }

            if (ADOBase.lm.listFloors.Count > endIndex + 1)
            {
                float n = ADOBase.lm.listFloors[endIndex + 1].floatDirection;
                angle = n == 999 ? prev.entryangle : ((-n + 90) * Mathf.PI / 180);
                prev.exitangle = angle;
                prev.nextfloor = end;
                prev.UpdateAngle();
                vector -= end.transform.position;
                prev.offsetPos = vector; // stashed for the post-ApplyEvents shift below
            }
        }

        internal static void OnApplyEventsToFloors()
        {
            if (scnEditor.instance == null || Playing || !PreviewActive || FakeFloors.Count == 0) return;
            if (ADOBase.lm.isOldLevel) return;
            int len = ADOBase.lm.floorAngles.Length;
            int startIndex = Mathf.Clamp(ResolveIdx(_cStart, len), 0, len);
            int endIndex = Mathf.Clamp(ResolveIdx(_cEnd, len), 0, len);
            int after = Mathf.Max(startIndex, endIndex) + 1;
            if (ADOBase.lm.listFloors.Count <= after) return;
            Vector3 vector = FakeFloors.Last().offsetPos;
            for (int i = after; i < ADOBase.lm.listFloors.Count; i++)
                ADOBase.lm.listFloors[i].transform.position += vector;
        }
    }

    // ── preview patches (attribute-applied via PatchAll) ────────────────────

    [HarmonyPatch(typeof(scrLevelMaker), "MakeLevel")]
    internal static class MagicShapeMakeLevelPatch
    {
        private static void Postfix()
        {
            try { EditorMagicShape.OnMakeLevel(); }
            catch (Exception ex) { SapphireLog.Log("MagicShape preview: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(scnGame), "ApplyEventsToFloors",
        typeof(List<scrFloor>), typeof(ADOFAI.LevelData), typeof(scrLevelMaker), typeof(List<ADOFAI.LevelEvent>))]
    internal static class MagicShapeApplyEventsPatch
    {
        private static void Postfix()
        {
            try { EditorMagicShape.OnApplyEventsToFloors(); }
            catch (Exception ex) { SapphireLog.Log("MagicShape preview shift: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(scnEditor), "Play")]
    internal static class MagicShapePlayPatch
    {
        private static void Prefix() { EditorMagicShape.Playing = true; }
    }

    [HarmonyPatch(typeof(scnEditor), "SwitchToEditMode")]
    internal static class MagicShapeEditModePatch
    {
        private static void Prefix() { EditorMagicShape.Playing = false; }
    }

    [HarmonyPatch(typeof(scnEditor), "Awake")]
    internal static class MagicShapeEditorAwakePatch
    {
        private static void Prefix() { EditorMagicShape.Playing = false; }
    }
}

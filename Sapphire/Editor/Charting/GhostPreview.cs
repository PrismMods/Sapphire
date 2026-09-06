using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Sapphire
{
    // One step of a previewed run: the relative charter and whether that tile twirls.
    internal struct GhostStep
    {
        public readonly double Angle;
        public readonly bool Twirl;
        // Jump applied BEFORE this tile — mirrors a PositionTrack, so a preview of separated
        // circles lands where the real ones will instead of stacking them all on one spot.
        public readonly Vector2 Offset;
        public GhostStep(double angle, bool twirl) { Angle = angle; Twirl = twirl; Offset = Vector2.zero; }
        public GhostStep(double angle, bool twirl, Vector2 offset) { Angle = angle; Twirl = twirl; Offset = offset; }
    }

    /* Translucent tiles showing the run a tool WOULD place, drawn ahead of the anchor.

       MagicShapeMultiply's fake-floor trick — Instantiate lm.meshFloor, tint the renderer, hide
       the number — but for an APPEND rather than a range sweep, which makes it far simpler and
       safer: MSM mutates its neighbouring REAL tiles' exitangle/nextfloor and leans on MakeLevel
       to put them back, which here would leave the live track drawn wrong for as long as a panel
       is open. These ghosts chain only to each other; the first starts from the anchor's position
       and heading without writing to it, so the seam there is a hair off and nothing real moves.

       Single owner at a time (a string token), because two panels fighting for the same ghosts
       every frame would thrash. The walk mirrors PseudoBuild's anchor rules exactly — spin from
       the anchor's isCCW, flip BEFORE a twirled tap — so no preview can disagree with what the
       builder actually produces. */
    internal static class GhostPreview
    {
        private static readonly List<GameObject> _objs = new List<GameObject>();
        private static string _owner;

        internal static bool OwnedBy(string owner) => _owner != null && _owner == owner;

        internal static void Release(string owner) { if (OwnedBy(owner)) Clear(); }

        internal static void Clear()
        {
            for (int i = 0; i < _objs.Count; i++)
                if (_objs[i] != null) UnityEngine.Object.DestroyImmediate(_objs[i]);
            _objs.Clear();
            _owner = null;
        }

        // Ghosts are positioned from the real tiles' transforms, so a level rebuild strands them.
        internal static void OnMakeLevel() { Clear(); }

        // The tile a run would be appended to, or -1.
        internal static int AnchorSeq(scnEditor ed)
        {
            try
            {
                var sel = ed != null ? ed.selectedFloors : null;
                if (sel != null && sel.Count > 0 && sel[sel.Count - 1] != null) return sel[sel.Count - 1].seqID;
            }
            catch { }
            return -1;
        }

        internal static void Show(string owner, scnEditor ed, int anchorSeq, IList<GhostStep> steps)
        {
            Clear();
            if (owner == null || ed == null || steps == null || steps.Count == 0 || anchorSeq < 0) return;
            _owner = owner;
            try
            {
                var lm = ADOBase.lm;
                if (lm == null || lm.isOldLevel || lm.meshFloor == null) return;
                if (anchorSeq >= lm.listFloors.Count) return;
                var anchor = lm.listFloors[anchorSeq];
                if (anchor == null) return;

                float tile = scrController.instance.tileSize;
                var host = GameObject.Find("SapphireGhosts") ?? new GameObject("SapphireGhosts");

                double dir = lm.floorAngles[Mathf.Clamp(anchorSeq, 0, lm.floorAngles.Length - 1)];
                int localSign = anchor.isCCW ? -1 : 1;
                Vector3 pos = anchor.transform.position;
                var made = new List<scrFloor>();

                for (int i = 0; i < steps.Count; i++)
                {
                    if (steps[i].Offset != Vector2.zero) pos += new Vector3(steps[i].Offset.x, steps[i].Offset.y, 0f);
                    if (steps[i].Twirl) localSign = -localSign;
                    dir = (dir + localSign * (180.0 - steps[i].Angle)) % 360.0;
                    if (dir < 0) dir += 360.0;
                    // facing (clockwise degrees) -> world angle, MSM's conversion
                    double a = (-dir + 90.0) * Mathf.PI / 180.0;
                    pos += scrMisc.getVectorFromAngle(a, tile);

                    var obj = UnityEngine.Object.Instantiate(lm.meshFloor, pos, Quaternion.identity);
                    obj.name = "SapphireGhost";
                    obj.transform.parent = host.transform;
                    var f = obj.GetComponent<scrFloor>();
                    if (f == null) { UnityEngine.Object.DestroyImmediate(obj); continue; }
                    f.entryangle = (a + Mathf.PI) % (Mathf.PI * 2);
                    f.exitangle = a;                       // straight until the next one lands
                    if (made.Count > 0) { made[made.Count - 1].exitangle = a; made[made.Count - 1].nextfloor = f; }
                    made.Add(f);
                    _objs.Add(obj);
                }

                foreach (var f in made)
                {
                    try
                    {
                        f.UpdateAngle();
                        if (f.floorRenderer != null) f.floorRenderer.color = new Color(0.55f, 0.75f, 1f, 0.45f);
                        if (f.editorNumText != null && f.editorNumText.letterText != null)
                            f.editorNumText.letterText.gameObject.SetActive(false);
                    }
                    catch { }
                }
            }
            catch (Exception ex) { SapphireLog.Log("GhostPreview: " + ex.Message); Clear(); }
        }
    }

    [HarmonyPatch(typeof(scrLevelMaker), "MakeLevel")]
    internal static class GhostPreviewMakeLevelPatch
    {
        private static void Postfix()
        {
            try { GhostPreview.OnMakeLevel(); }
            catch (Exception ex) { SapphireLog.Log("GhostPreview MakeLevel: " + ex.Message); }
        }
    }
}

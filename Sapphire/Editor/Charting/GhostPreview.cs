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
        // Cached: GameObject.Find walks the whole scene, and this ran on every rebuild.
        private static GameObject _host;

        internal static bool OwnedBy(string owner) => _owner != null && _owner == owner;

        internal static void Release(string owner) { if (OwnedBy(owner)) Clear(); }

        internal static void Clear()
        {
            for (int i = 0; i < _objs.Count; i++)
                if (_objs[i] != null) UnityEngine.Object.DestroyImmediate(_objs[i]);
            _objs.Clear();
            _owner = null;
        }

        /* Reuse the instances when only the SHAPE changed. Typing in a panel re-walks every
           frame, and tearing down plus re-instantiating a few hundred mesh floors per keystroke
           is most of what made a big run feel heavy. Same owner and same count = move them. */
        private static bool CanReuse(string owner, int count)
            => _owner == owner && _objs.Count == count && _host != null;

        // Ghosts are positioned from the real tiles' transforms, so a level rebuild strands them.
        internal static void OnMakeLevel() { Clear(); }

        internal static void Dispose() { Clear(); if (_host != null) UnityEngine.Object.Destroy(_host); _host = null; }

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
            if (owner == null || ed == null || steps == null || steps.Count == 0 || anchorSeq < 0)
            { Clear(); return; }
            bool reuse = CanReuse(owner, steps.Count);
            if (!reuse) Clear();
            _owner = owner;
            try
            {
                var lm = ADOBase.lm;
                if (lm == null || lm.isOldLevel || lm.meshFloor == null) return;
                if (anchorSeq >= lm.listFloors.Count) return;
                var anchor = lm.listFloors[anchorSeq];
                if (anchor == null) return;

                float tile = scrController.instance.tileSize;
                if (_host == null) _host = GameObject.Find("SapphireGhosts") ?? new GameObject("SapphireGhosts");
                var host = _host;

                double dir = lm.floorAngles[Mathf.Clamp(anchorSeq, 0, lm.floorAngles.Length - 1)];
                int localSign = anchor.isCCW ? -1 : 1;
                Vector3 pos = anchor.transform.position;
                var made = new List<scrFloor>();

                for (int i = 0; i < steps.Count; i++)
                {
                    /* PositionTrack's offset is in TILES, not world units — ApplyEventsToFloors
                       multiplies it by scrController.tileSize before adding (verified in the IL).
                       The preview was adding it raw, so separated circles previewed at the wrong
                       spacing and the ghosts disagreed with the placement. */
                    if (steps[i].Offset != Vector2.zero)
                        pos += new Vector3(steps[i].Offset.x, steps[i].Offset.y, 0f) * tile;
                    if (steps[i].Twirl) localSign = -localSign;
                    dir = (dir + localSign * (180.0 - steps[i].Angle)) % 360.0;
                    if (dir < 0) dir += 360.0;
                    // facing (clockwise degrees) -> world angle, MSM's conversion
                    double a = (-dir + 90.0) * Mathf.PI / 180.0;
                    pos += scrMisc.getVectorFromAngle(a, tile);

                    GameObject obj;
                    if (reuse)
                    {
                        obj = _objs[i];
                        if (obj == null) { reuse = false; Clear(); return; }   // torn down under us
                        obj.transform.position = pos;
                    }
                    else
                    {
                        obj = UnityEngine.Object.Instantiate(lm.meshFloor, pos, Quaternion.identity);
                        obj.name = "SapphireGhost";
                        obj.transform.parent = host.transform;
                        _objs.Add(obj);
                    }
                    var f = obj.GetComponent<scrFloor>();
                    if (f == null) continue;
                    f.entryangle = (a + Mathf.PI) % (Mathf.PI * 2);
                    f.exitangle = a;                       // straight until the next one lands
                    if (made.Count > 0) { made[made.Count - 1].exitangle = a; made[made.Count - 1].nextfloor = f; }
                    made.Add(f);
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

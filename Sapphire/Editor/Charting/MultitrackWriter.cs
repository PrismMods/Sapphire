using System;
using System.Collections.Generic;
using ADOFAI;
using DG.Tweening;
using UnityEngine;

namespace Sapphire
{
    /* Turns a MultitrackModel into the level: AddObject floors + two planets, MoveDecorations on
       the host, and the EditorComment that stores the model.

       Never reloads the level's decorations. Every vanilla removal (scnEditor.RemoveEvent) calls
       UpdateDecorationObjects(), which rebuilds ALL of them — thousands on a VFX level — so this
       diffs only its own: a changed tile re-runs Setup on its one object, an added one goes through
       AddDecoration (inserts into levelData.decorations AND allDecorations at the same index), and
       a removed one is unhooked from the manager's lists by RemoveDecoration below. Decoration
       identity is the tile index: tile k always carries token smt{Id}f{k}, so tags never change on a
       live object and taggedDecorations never goes stale. */
    internal static class MultitrackWriter
    {
        internal sealed class Entry
        {
            public int Host;
            public MultitrackModel Model;
            public LevelEvent Comment;
        }

        internal static List<Entry> FindAll(scnEditor ed)
        {
            var list = new List<Entry>();
            foreach (var e in ed.events)
            {
                if (e == null || e.eventType != LevelEventType.EditorComment) continue;
                MultitrackModel m = null;
                try { m = MultitrackModel.Parse(e["comment"] as string); } catch { }
                if (m != null) list.Add(new Entry { Host = e.floor, Model = m, Comment = e });
            }
            return list;
        }

        internal static Entry Find(scnEditor ed, int id)
        {
            foreach (var x in FindAll(ed)) if (x.Model.Id == id) return x;
            return null;
        }

        internal static int NextId(scnEditor ed)
        {
            int max = 0;
            foreach (var x in FindAll(ed)) max = Math.Max(max, x.Model.Id);
            return max + 1;
        }

        internal static double HostBpm(scnEditor ed, int host)
        {
            float speed = 1f;
            try { speed = ADOBase.lm.listFloors[host].speed; } catch { }
            return ed.levelData.bpm * speed;
        }

        // Fake floor index carried by a decoration, or -1. id = the multitrack it belongs to.
        internal static int TileOf(LevelEvent deco, out int id)
        {
            id = -1;
            string tags = null;
            try { tags = deco["tag"] as string; } catch { }
            if (string.IsNullOrEmpty(tags)) return -1;
            foreach (var tok in tags.Split(' '))
            {
                if (!tok.StartsWith("smt")) continue;
                int f = tok.IndexOf('f');
                if (f < 4) continue;
                int a, k;
                if (int.TryParse(tok.Substring(3, f - 3), out a) && int.TryParse(tok.Substring(f + 1), out k)) { id = a; return k; }
            }
            return -1;
        }

        internal static LevelEvent FloorDeco(scnEditor ed, MultitrackModel m, int k)
        {
            string tok = m.Tag + "f" + k;
            foreach (var d in ed.levelData.decorations)
                if (d != null && MultitrackLayout.HasToken(Str(d, "tag"), tok)) return d;
            return null;
        }

        internal static scrDecoration ObjectOf(LevelEvent e)
        {
            try { return scrDecorationManager.instance.allDecorations.Find(d => d != null && d.sourceLevelEvent == e); }
            catch { return null; }
        }

        internal static void Write(scnEditor ed, int host, MultitrackModel m)
        {
            var tiles = MultitrackLayout.Build(m.Angles, m.Twirls, m.Size);
            double hostBpm = HostBpm(ed, host);
            m.HostBpm = hostBpm;
            using (new SaveStateScope(ed))
            {
                ReadBackOrigin(ed, m);
                SyncFloors(ed, host, m, tiles);
                SyncPlanets(ed, host, m);
                ReplaceEvents(ed, host, m, tiles, hostBpm);
                WriteComment(ed, host, m);
            }
        }

        internal static void Delete(scnEditor ed, int host, MultitrackModel m)
        {
            using (new SaveStateScope(ed))
            {
                var mine = new List<LevelEvent>();
                foreach (var d in ed.levelData.decorations)
                    if (d != null && MultitrackLayout.HasToken(Str(d, "tag"), m.Tag)) mine.Add(d);
                bool sel = false;
                for (int i = mine.Count - 1; i >= 0; i--) sel |= RemoveDecoration(ed, mine[i]);
                if (mine.Count > 0) AfterRemovals(ed, sel);
                ed.events.RemoveAll(e => e != null && IsOurs(e, m));
            }
        }

        // ── decorations ────────────────────────────────────────────────

        // Dragging fake tile 0 in the editor moves the whole track.
        private static void ReadBackOrigin(scnEditor ed, MultitrackModel m)
        {
            var d0 = FloorDeco(ed, m, 0);
            if (d0 == null) return;
            try { var p = (Vector2)d0["position"]; m.OriginX = p.x; m.OriginY = p.y; } catch { }
        }

        private static void SyncFloors(scnEditor ed, int host, MultitrackModel m, FakeTile[] tiles)
        {
            var have = new Dictionary<int, LevelEvent>();
            foreach (var d in ed.levelData.decorations)
            {
                if (d == null) continue;
                int id; int k = TileOf(d, out id);
                if (k >= 0 && id == m.Id) have[k] = d;
            }
            for (int k = 0; k < tiles.Length; k++)
            {
                LevelEvent d;
                if (have.TryGetValue(k, out d))
                {
                    if (ApplyFloor(d, m, tiles[k], k == tiles.Length - 1)) ed.UpdateDecorationObject(d);
                    continue;
                }
                d = new LevelEvent(host, LevelEventType.AddObject);
                d["tag"] = m.Tag + " " + m.Tag + "f" + k;
                d["objectType"] = ObjectDecorationType.Floor;
                d["relativeTo"] = DecPlacementType.Tile;
                ApplyFloor(d, m, tiles[k], k == tiles.Length - 1);
                ed.AddDecoration(d, InsertIndex(ed, m));
            }
            var gone = new List<int>();
            foreach (var kv in have) if (kv.Key >= tiles.Length) gone.Add(kv.Key);
            gone.Sort();
            bool sel = false;
            for (int i = gone.Count - 1; i >= 0; i--) sel |= RemoveDecoration(ed, have[gone[i]]);
            if (gone.Count > 0) AfterRemovals(ed, sel);
        }

        // Returns true when any field changed, so an unchanged tile costs no Setup call.
        private static bool ApplyFloor(LevelEvent d, MultitrackModel m, FakeTile t, bool last)
        {
            bool ch = false;
            Set(d, "trackType", t.Midspin ? FloorDecorationType.Midspin : FloorDecorationType.Normal, ref ch);
            Set(d, "trackAngle", (float)t.TrackAngle, ref ch);
            Set(d, "rotation", (float)t.Rotation, ref ch);
            Set(d, "position", new Vector2((float)(m.OriginX + t.X), (float)(m.OriginY + t.Y)), ref ch);
            Set(d, "scale", new Vector2((float)(100 * m.Size), (float)(100 * m.Size)), ref ch);
            Set(d, "trackColorType", TrackColorType.Single, ref ch);
            Set(d, "trackColor", m.TrackColor, ref ch);
            Set(d, "trackStyle", TrackStyle.Standard, ref ch);
            // The end tile is the game's own Portal icon, like a real level's last tile.
            var icon = last && m.EndTile ? CustomFloorIcon.Portal : t.Twirl ? CustomFloorIcon.Swirl : CustomFloorIcon.None;
            Set(d, "trackIcon", icon, ref ch);
            if (icon == CustomFloorIcon.Swirl)
            {
                // Swirl points at the middle of the sweep, like a real twirl tile.
                double mid = t.Tail + (t.Ccw ? 1 : -1) * t.Sweep / 2;
                Set(d, "trackIconFlipped", t.Ccw, ref ch);
                Set(d, "trackRedSwirl", t.Sweep < 180, ref ch);
                Set(d, "trackIconAngle", (float)MultitrackLayout.Norm(mid - 90 - t.Rotation), ref ch);
            }
            return ch;
        }

        private static void SyncPlanets(scnEditor ed, int host, MultitrackModel m)
        {
            for (int p = 0; p < 2; p++)
            {
                string tok = m.Tag + "p" + p;
                LevelEvent d = null;
                foreach (var x in ed.levelData.decorations)
                    if (x != null && MultitrackLayout.HasToken(Str(x, "tag"), tok)) { d = x; break; }
                bool add = d == null;
                if (add)
                {
                    d = new LevelEvent(host, LevelEventType.AddObject);
                    d["tag"] = m.Tag + " " + tok;
                    d["objectType"] = ObjectDecorationType.Planet;
                    d["relativeTo"] = DecPlacementType.Tile;
                    d["planetColorType"] = PlanetDecorationColorType.Custom;
                    d["depth"] = -2;
                }
                bool ch = false;
                // Base rotation 0: the MoveDecorations rotationOffsets are absolute headings.
                Set(d, "rotation", 0f, ref ch);
                Set(d, "position", new Vector2((float)m.OriginX, (float)m.OriginY), ref ch);
                Set(d, "pivotOffset", new Vector2(p == 1 ? 1f : 0f, 0f), ref ch);
                Set(d, "scale", new Vector2((float)(100 * m.Size), (float)(100 * m.Size)), ref ch);
                Set(d, "planetColor", m.PlanetColor + "ff", ref ch);
                Set(d, "planetTailColor", m.PlanetColor + "00", ref ch);
                if (add) ed.AddDecoration(d, InsertIndex(ed, m));
                else if (ch) ed.UpdateDecorationObject(d);
            }
        }

        // Just after the multitrack's last decoration, so the track stays grouped in the list.
        // AddDecoration inserts at index + 1; -1 appends.
        private static int InsertIndex(scnEditor ed, MultitrackModel m)
        {
            var list = ed.levelData.decorations;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] != null && MultitrackLayout.HasToken(Str(list[i], "tag"), m.Tag)) return i;
            return -1;
        }

        /* One decoration out, with no reload. levelData.decorations and the manager's allDecorations
           are index-aligned (GetDecoration(int) is how the editor's list selects), so both lose the
           same index. Fake floors and planets are object decorations, so the private texture cache
           (visual decorations only) never held them. Returns true when it was selected; the caller
           then runs AfterRemovals once for the batch. */
        private static bool RemoveDecoration(scnEditor ed, LevelEvent e)
        {
            var list = ed.levelData.decorations;
            int idx = list.IndexOf(e);
            if (idx < 0) return false;
            list.RemoveAt(idx);
            bool wasSelected = false;
            try { wasSelected = ed.selectedDecorations.Remove(e); } catch { }
            var mgr = scrDecorationManager.instance;
            var obj = ObjectOf(e);
            if (mgr == null || obj == null) return wasSelected;
            int oi = mgr.allDecorations.IndexOf(obj);
            if (oi != idx) SapphireLog.Log("Multitrack: decoration index drift " + oi + " vs " + idx);
            mgr.allDecorations.Remove(obj);
            foreach (var l in mgr.taggedDecorations.Values) l.Remove(obj);
            foreach (var l in mgr.hitboxEventTagDecorations.Values) l.Remove(obj);
            UnityEngine.Object.DestroyImmediate(obj.gameObject);
            return wasSelected;
        }

        /* What vanilla RemoveEvent does besides the reload: DecorationsArray flags the editor's
           decoration list only on Add/Insert, so a removal leaves a stale row whose click selects a
           shifted index; and a removed selected decoration leaves the gizmo and inspector on an
           orphan LevelEvent. */
        private static void AfterRemovals(scnEditor ed, bool selectionChanged)
        {
            try { ed.propertyControlDecorationsList.OnDecorationUpdate(); } catch { }
            if (!selectionChanged) return;
            try { ed.decTransformGizmo.UpdateGizmosVisibility(); } catch { }
            try { ed.levelEventsPanel.HideAllInspectorTabs(); } catch { }
        }

        // ── events ─────────────────────────────────────────────────────

        private static bool IsOurs(LevelEvent e, MultitrackModel m)
        {
            if (e.eventType == LevelEventType.EditorComment)
            {
                var p = MultitrackModel.Parse(Str(e, "comment"));
                return p != null && p.Id == m.Id;
            }
            return Str(e, "eventTag") == m.Tag;
        }

        /* Events own no scene objects, so the whole set is replaced. No ApplyEventsToFloors per
           edit: editor Play runs RemakePath, which applies every event (verified in IL). */
        private static void ReplaceEvents(scnEditor ed, int host, MultitrackModel m, FakeTile[] t, double hostBpm)
        {
            // Every generated event carries eventTag smt{Id}: planet moves, fades, and hit copies.
            ed.events.RemoveAll(e => e != null && e.eventType != LevelEventType.EditorComment && Str(e, "eventTag") == m.Tag);
            string both = m.Tag + "p0 " + m.Tag + "p1", orbit = m.Tag + "p1";
            if (m.HideOutside)
            {
                // OFNR's own trick: opacity 0 at level start, so the editor still shows the track.
                Add(ed, Move(0, m, m.Tag, 0, 0, Ease.Linear, "opacity", 0f));
                Add(ed, Move(host, m, m.Tag, MultitrackLayout.RevealOffset(m.StartBeat), 1, Ease.OutSine, "opacity", 100f));
                Add(ed, Move(host, m, m.Tag, MultitrackLayout.EndOffset(t, m.StartBeat, hostBpm, m.Bpm), 1, Ease.OutSine, "opacity", 0f));
            }
            foreach (var k in MultitrackLayout.Planets(t, m.StartBeat, hostBpm, m.Bpm))
            {
                if (k.Both)
                    Add(ed, Move(host, m, both, k.Offset, 0, Ease.Linear,
                        "positionOffset", new Vector2((float)k.X, (float)k.Y), "rotationOffset", (float)k.Rotation));
                else
                    Add(ed, Move(host, m, orbit, k.Offset, k.Duration, Ease.Linear, "rotationOffset", (float)k.Rotation));
            }
            if (m.HitTag.Length > 0) CopyHitEvents(ed, host, m, t, hostBpm);
            if (m.FadePassed)
                for (int i = 0; i < t.Length - 1; i++)
                    Add(ed, Move(host, m, m.Tag + "f" + i, MultitrackLayout.LeaveOffset(t, i, m.StartBeat, hostBpm, m.Bpm),
                        0.5, Ease.OutSine, "opacity", 0f));
        }

        /* "Trigger on hit": every event tagged m.HitTag is copied onto the host at each fake hit,
           keeping its own angleOffset on top. A decoration hitbox can't do this: fake floors and
           planets are object decorations, which have no hitbox, and an imageless decoration gets no
           collider. Copies are re-made on every write, so they follow the track; editing a template
           shows up on the next write (Rebuild forces one). Only timed events (with an angleOffset)
           are copied — a Twirl or SetSpeed copied onto the host would rewrite the real chart.
           Copies are enabled even when the template is switched off, so templates can stay off. */
        private static void CopyHitEvents(scnEditor ed, int host, MultitrackModel m, FakeTile[] t, double hostBpm)
        {
            var templates = new List<LevelEvent>();
            foreach (var e in ed.events)
            {
                if (e == null || e.eventType == LevelEventType.EditorComment) continue;
                string et = Str(e, "eventTag");
                if (et == m.Tag || !MultitrackLayout.HasToken(et, m.HitTag)) continue;
                bool timed = false;
                try { timed = e.ContainsKey("angleOffset"); } catch { }
                if (timed) templates.Add(e);
            }
            if (templates.Count == 0) return;
            foreach (double beat in MultitrackLayout.HitBeats(t))
            {
                double at = MultitrackLayout.HostOffset(beat, m.StartBeat, hostBpm, m.Bpm);
                foreach (var tp in templates)
                {
                    var c = tp.Copy();
                    c.floor = host;
                    float own = 0f;
                    try { own = Convert.ToSingle(tp["angleOffset"]); } catch { }
                    c["angleOffset"] = (float)(at + own);
                    c["eventTag"] = m.Tag;
                    c.active = true;
                    ed.events.Add(c);
                }
            }
        }

        // A MoveDecorations with ONLY the named fields enabled; every other optional field off.
        private static LevelEvent Move(int floor, MultitrackModel m, string target, double offset, double duration,
                                       Ease ease, params object[] kv)
        {
            var e = new LevelEvent(floor, LevelEventType.MoveDecorations);
            e["tag"] = target;
            e["eventTag"] = m.Tag;
            e["angleOffset"] = (float)offset;
            e["duration"] = (float)duration;
            e["ease"] = ease;
            var on = new HashSet<string>();
            for (int i = 0; i + 1 < kv.Length; i += 2) { e[(string)kv[i]] = kv[i + 1]; on.Add((string)kv[i]); }
            if (e.disabled != null)
                foreach (var key in new List<string>(e.disabled.Keys)) e.disabled[key] = !on.Contains(key);
            return e;
        }

        private static void Add(scnEditor ed, LevelEvent e) => ed.events.Add(e);

        private static void WriteComment(scnEditor ed, int host, MultitrackModel m)
        {
            LevelEvent c = null;
            foreach (var e in ed.events)
                if (e != null && e.eventType == LevelEventType.EditorComment)
                {
                    var p = MultitrackModel.Parse(Str(e, "comment"));
                    if (p != null && p.Id == m.Id) { c = e; break; }
                }
            if (c == null) { c = new LevelEvent(host, LevelEventType.EditorComment); ed.events.Add(c); }
            c["comment"] = m.Serialize();
        }

        // ── helpers ────────────────────────────────────────────────────

        private static void Set(LevelEvent e, string key, object v, ref bool changed)
        {
            object cur = null;
            try { cur = e[key]; } catch { }
            if (Equals(cur, v)) return;
            e[key] = v;
            changed = true;
        }

        private static string Str(LevelEvent e, string key)
        {
            try { return e[key] as string; } catch { return null; }
        }
    }
}

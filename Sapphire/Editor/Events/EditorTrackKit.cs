using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sapphire
{
    /* Technical-charting kit (experimental): one-key tile hiding and visual track-segment
       placement — the two jobs Once Forgotten, Nothing Remains does by hand hundreds of times
       (PositionTrack scale/opacity 0 on helper tiles; positionOffsets worked out elsewhere and
       noted as x/y in editor comments).

       Hidden tiles stay findable: while editing, every tile this kit hid gets a dotted ring.
       Placement is "move, then click" rather than press-and-drag, so it never competes with the
       editor's own drag-select. */
    internal static class EditorTrackKit
    {
        private static int _armSeq = -1;
        private static Vector2 _snapTiles;
        private static GameObject _root;
        private static readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        private static int _used;
        private static Sprite _dot;
        private static readonly HashSet<int> _hidden = new HashSet<int>();
        private static int _scanCd, _lastEventCount = -1;

        internal static bool Armed => _armSeq >= 0;

        private static bool Available(scnEditor ed)
            => ed != null && !ed.playMode && EditorPatterns.TabAvailable();

        internal static void Tick()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            BeginDraw();
            try
            {
                if (!Available(ed)) { _armSeq = -1; return; }
                if (Keybinds.Down(Bind.ExPatterns)) EditorPatterns.Toggle();
                if (Keybinds.Down(Bind.ExHideTile)) ToggleHideSelection(ed);
                if (Keybinds.Down(Bind.ExPlaceSegment)) ArmPlace(ed);
                TickHidden(ed);
                if (Armed) TickPlace(ed);
            }
            finally { EndDraw(); }
        }

        internal static void Dispose()
        {
            _armSeq = -1;
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null; _pool.Clear(); _used = 0;
            _hidden.Clear(); _lastEventCount = -1;
        }

        // ── hide / show ─────────────────────────────────────────────────────

        // Ours = a PositionTrack that hides only its own tile. Any other PositionTrack on the
        // tile (an offset, a scale) is the charter's and is left alone.
        private static bool IsHideEvent(ADOFAI.LevelEvent e)
        {
            if (e == null || e.eventType != ADOFAI.LevelEventType.PositionTrack) return false;
            try
            {
                bool off = e.disabled != null && e.disabled.TryGetValue("opacity", out bool d) && d;
                return !off && Convert.ToBoolean(e["justThisTile"]) && Convert.ToDouble(e["opacity"]) <= 0.0;
            }
            catch { return false; }
        }

        internal static void ToggleHideSelection(scnEditor ed)
        {
            var seqs = new List<int>();
            try { foreach (var f in ed.selectedFloors) if (f != null) seqs.Add(f.seqID); } catch { }
            if (seqs.Count == 0) { Notify(ed, Loc.T("Nothing selected")); return; }
            // One direction for the whole selection: if the first tile is hidden, show them all.
            bool show = ed.events.Exists(e => e != null && e.floor == seqs[0] && IsHideEvent(e));
            using (new SaveStateScope(ed))
            {
                var set = new HashSet<int>(seqs);
                ed.events.RemoveAll(e => e != null && set.Contains(e.floor) && IsHideEvent(e));
                if (!show)
                    foreach (int s in seqs)
                    {
                        var e = new ADOFAI.LevelEvent(s, ADOFAI.LevelEventType.PositionTrack);
                        EditorQuickChart.SetNum(e, "opacity", 0); EditorQuickChart.Enable(e, "opacity");
                        try { e["justThisTile"] = true; } catch { }
                        ed.events.Add(e);
                    }
                try { ed.ApplyEventsToFloors(); } catch { }
                try { ed.RemakePath(true, true); } catch { }
            }
            _lastEventCount = -1;
            Notify(ed, string.Format(show ? Loc.T("Showed {0} tiles") : Loc.T("Hid {0} tiles"), seqs.Count));
        }

        // Rescan on a cadence or when the event count moves; drawing is per frame (cheap: only
        // tiles inside the camera view get a ring).
        private static void TickHidden(scnEditor ed)
        {
            int count = ed.events.Count;
            if (--_scanCd <= 0 || count != _lastEventCount)
            {
                _scanCd = 30; _lastEventCount = count;
                _hidden.Clear();
                foreach (var e in ed.events) if (IsHideEvent(e)) _hidden.Add(e.floor);
            }
            if (_hidden.Count == 0) return;
            var floors = ADOBase.lm.listFloors;
            var view = ViewRect(ed);
            float r = Tile() * 0.42f;
            int drawn = 0;
            var col = new Color(1f, 1f, 1f, 0.55f);
            foreach (int s in _hidden)
            {
                if (s < 0 || s >= floors.Count || floors[s] == null) continue;
                Vector2 p = floors[s].transform.position;
                if (!view.Contains(p)) continue;
                Ring(p, r, col);
                if (++drawn >= 300) break;   // ponytail: cap rings; cull smarter if dense hide sections need all of them
            }
        }

        // ── place track segment ─────────────────────────────────────────────

        internal static void ArmPlace(scnEditor ed)
        {
            if (!UI.PanelKit.SelectionRange(out int seq, out _)) { Notify(ed, Loc.T("Nothing selected")); return; }
            if (seq <= 0) { Notify(ed, Loc.T("The first tile cannot move")); return; }
            _armSeq = seq;
            Notify(ed, Loc.T("Move to place the segment, click to drop · Esc cancels · Alt = no snap"));
        }

        private static void TickPlace(scnEditor ed)
        {
            var floors = ADOBase.lm.listFloors;
            if (_armSeq >= floors.Count || floors[_armSeq] == null || Input.GetKeyDown(KeyCode.Escape)) { _armSeq = -1; return; }
            float tile = Tile();
            Vector2 anchor = floors[_armSeq].transform.position;
            Vector2 d = (MouseWorld(ed) - anchor) / tile;
            _snapTiles = Keybinds.AltHeld ? d : new Vector2(Mathf.Round(d.x * 2f) / 2f, Mathf.Round(d.y * 2f) / 2f);
            Vector2 shift = _snapTiles * tile;

            // Ghost of the segment: the next tiles, shifted as they will be.
            var col = new Color(Sapphire.UI.Theme.Accent.r, Sapphire.UI.Theme.Accent.g, Sapphire.UI.Theme.Accent.b, 0.85f);
            for (int i = _armSeq; i < floors.Count && i < _armSeq + 128; i++)
                if (floors[i] != null) Dot((Vector2)floors[i].transform.position + shift, 0.22f, col);
            Ring(anchor + shift, tile * 0.45f, col);

            if (Input.GetMouseButtonDown(0) && !OverUi()) Commit(ed);
        }

        // An existing segment offset on this tile is extended, not stacked on.
        private static void Commit(scnEditor ed)
        {
            int seq = _armSeq;
            _armSeq = -1;
            if (_snapTiles.sqrMagnitude < 1e-6f) return;
            using (new SaveStateScope(ed))
            {
                ADOFAI.LevelEvent existing = null;
                foreach (var e in ed.events)
                    if (e != null && e.floor == seq && e.eventType == ADOFAI.LevelEventType.PositionTrack && !IsHideEvent(e)
                        && !(e.disabled != null && e.disabled.TryGetValue("positionOffset", out bool off) && off))
                    { existing = e; break; }
                if (existing != null)
                {
                    Vector2 cur = Vector2.zero;
                    try { cur = (Vector2)existing["positionOffset"]; } catch { }
                    existing["positionOffset"] = new Vector2(cur.x + _snapTiles.x, cur.y + _snapTiles.y);
                    try { ed.ApplyEventsToFloors(); } catch { }
                    try { ed.RemakePath(true, true); } catch { }
                }
                else
                {
                    EditorQuickChart.AddPositionTrack(ed, seq, _snapTiles.x, _snapTiles.y);
                    try { ed.ApplyEventsToFloors(); } catch { }
                    try { ed.RemakePath(true, true); } catch { }
                }
            }
            Notify(ed, string.Format(Loc.T("Segment moved by {0}, {1} tiles"), _snapTiles.x.ToString("0.##"), _snapTiles.y.ToString("0.##")));
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static float Tile()
        {
            try { return scrController.instance.tileSize; } catch { return 1.5f; }
        }

        private static Camera Cam(scnEditor ed)
        {
            Camera c = null;
            try { c = ed.camera; } catch { }
            return c != null ? c : Camera.main;
        }

        private static Vector2 MouseWorld(scnEditor ed)
        {
            var c = Cam(ed);
            return c != null ? (Vector2)c.ScreenToWorldPoint(Input.mousePosition) : Vector2.zero;
        }

        private static Rect ViewRect(scnEditor ed)
        {
            var c = Cam(ed);
            if (c == null) return new Rect(-1e6f, -1e6f, 2e6f, 2e6f);
            float h = c.orthographicSize, w = h * c.aspect;
            Vector2 p = c.transform.position;
            return new Rect(p.x - w - 2f, p.y - h - 2f, (w + 2f) * 2f, (h + 2f) * 2f);
        }

        private static bool OverUi()
        {
            try
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                return es != null && es.IsPointerOverGameObject();
            }
            catch { return false; }
        }

        private static void Notify(scnEditor ed, string msg)
        {
            try { ed.ShowNotification(msg); } catch { }
        }

        // Pooled sprite dots — the one world primitive proven to render in this build (see
        // EditorCameraPath: LineRenderers and stretched bars do not).
        private static void BeginDraw() { _used = 0; }

        private static void EndDraw()
        {
            for (int i = _used; i < _pool.Count; i++)
                if (_pool[i] != null && _pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
        }

        private static void Ring(Vector2 c, float r, Color col)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Dot(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, 0.12f, col);
            }
        }

        private static void Dot(Vector2 p, float scale, Color col)
        {
            if (_root == null) { _root = new GameObject("SapphireTrackKit"); UnityEngine.Object.DontDestroyOnLoad(_root); }
            SpriteRenderer sr;
            if (_used < _pool.Count && _pool[_used] != null) sr = _pool[_used];
            else
            {
                var go = new GameObject("Dot");
                go.transform.SetParent(_root.transform, false);
                sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = DotSprite();
                sr.sortingOrder = 32001;
                if (_used < _pool.Count) _pool[_used] = sr; else _pool.Add(sr);
            }
            _used++;
            if (!sr.gameObject.activeSelf) sr.gameObject.SetActive(true);
            var t = sr.transform;
            t.position = new Vector3(p.x, p.y, 0f);
            t.localScale = Vector3.one * scale;
            if (sr.color != col) sr.color = col;
        }

        private static Sprite DotSprite()
        {
            if (_dot != null) return _dot;
            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.ARGB32, false);
            float rad = S * 0.5f - 1f, cx = S * 0.5f - 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cx) * (y - cx));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(rad - d)));
                }
            tex.Apply();
            _dot = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
            return _dot;
        }
    }
}

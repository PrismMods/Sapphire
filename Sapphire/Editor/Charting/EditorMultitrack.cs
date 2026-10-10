using System;
using TMPro;
using UnityEngine;
using Sapphire.UI;

namespace Sapphire
{
    /* Multitrack edit mode (experimental). While on, the editor's own tile keys chart the fake
       track: Harmony prefixes in Patches.cs call OnAngleKey / OnDelete / OnStep and, when those
       return true, the vanilla action does not run.

       The model is re-read from the level on every action rather than cached: undo restores the
       level wholesale, and a cached model would write the undone state straight back. */
    internal static class EditorMultitrack
    {
        private static int _id = -1;
        private static int _cursor;
        private static bool _jumpPending;
        private static GameObject _ring;
        // Cached between rescans: finding the model walks every event and every decoration, which
        // is 100k+ entries on a VFX level, so per-frame drawing reads these instead.
        private static MultitrackModel _model;
        private static int _host = -1, _scanCd;
        private static ADOFAI.LevelEvent _cursorDeco, _lastSel;
        private static scrDecoration _cursorObj;

        internal static bool Editing => _id >= 0;
        internal static int Cursor => _cursor;

        internal static bool Available(scnEditor ed) => ed != null && !ed.playMode && EditorPatterns.TabAvailable();

        internal static MultitrackModel Current(out int host)
        {
            host = -1;
            var ed = scnEditor.instance;
            if (!Editing || ed == null) return null;
            var en = MultitrackWriter.Find(ed, _id);
            if (en == null) return null;
            host = en.Host;
            return en.Model;
        }

        // Read, change, write: one undo step. Exits edit mode if the track is gone (undone).
        internal static void Apply(Action<MultitrackModel> change, bool force = false)
        {
            var ed = scnEditor.instance;
            int host;
            var m = Current(out host);
            if (m == null) { Exit(); return; }
            try
            {
                string before = m.Serialize();
                change(m);
                _cursor = m.Clamp(_cursor);
                // A no-op (refused delete, focus-out of an untouched field) writes nothing: no
                // empty undo step, and no undone value written back from a stale palette field.
                if (!force && m.Serialize() == before) return;
                MultitrackWriter.Write(ed, host, m);
                _jumpPending = true;
                _scanCd = 0;
            }
            catch (Exception ex) { SapphireLog.Log("Multitrack: edit failed: " + ex); }
        }

        internal static void CreateHere(scnEditor ed)
        {
            if (!Available(ed) || ed.selectedFloors == null || ed.selectedFloors.Count != 1) return;
            int host = ed.selectedFloors[0].seqID;
            var m = new MultitrackModel { Id = MultitrackWriter.NextId(ed) };
            m.Bpm = Math.Round(MultitrackWriter.HostBpm(ed, host), 3);
            try { MultitrackWriter.Write(ed, host, m); }
            catch (Exception ex) { SapphireLog.Log("Multitrack: create failed: " + ex); return; }
            Enter(m.Id, 0);
            Notify(ed, Loc.T("Multitrack created — chart it with the tile keys"));
        }

        internal static bool HostHasOne(scnEditor ed)
        {
            if (ed == null || ed.selectedFloors == null || ed.selectedFloors.Count != 1) return false;
            int seq = ed.selectedFloors[0].seqID;
            foreach (var x in MultitrackWriter.FindAll(ed)) if (x.Host == seq) return true;
            return false;
        }

        internal static void EditHere(scnEditor ed)
        {
            if (!Available(ed) || ed.selectedFloors == null || ed.selectedFloors.Count != 1) return;
            int seq = ed.selectedFloors[0].seqID;
            foreach (var x in MultitrackWriter.FindAll(ed))
                if (x.Host == seq) { Enter(x.Model.Id, x.Model.TileCount - 1); return; }
        }

        private static void Enter(int id, int cursor)
        {
            _id = id;
            _cursor = cursor;
            _jumpPending = true;
            _scanCd = 0;
            var ed = scnEditor.instance;
            // Out of the real track's selection, or the game's keys act on that tile too.
            try { ed.DeselectFloors(false); } catch { }
            // A tempo change before the host since the last write leaves every offset stale.
            var en = MultitrackWriter.Find(ed, id);
            if (en != null && Math.Abs(en.Model.HostBpm - MultitrackWriter.HostBpm(ed, en.Host)) > 1e-6)
            {
                try { MultitrackWriter.Write(ed, en.Host, en.Model); }
                catch (Exception ex) { SapphireLog.Log("Multitrack: retime failed: " + ex); }
            }
        }

        internal static void Exit()
        {
            _id = -1;
            _model = null; _host = -1; _cursorDeco = null; _cursorObj = null;
            // The fake tile that was selected stays selected; don't let WatchSelection re-enter on it.
            try { var sel = scnEditor.instance.selectedDecorations; _lastSel = sel != null && sel.Count == 1 ? sel[0] : null; } catch { }
            if (_ring != null) _ring.SetActive(false);
        }

        // ── hooks called by Patches.cs ─────────────────────────────────

        internal static bool OnAngleKey(float deg)
        {
            if (!Editing) return false;
            Apply(m => _cursor = m.Insert(_cursor, deg));
            return true;
        }

        internal static bool OnDelete()
        {
            if (!Editing) return false;
            Apply(m =>
            {
                int c = m.Delete(_cursor);
                if (c < 0) Notify(scnEditor.instance, Loc.T("The first fake tile can't be deleted"));
                else _cursor = c;
            });
            return true;
        }

        internal static bool OnStep(int dir)
        {
            if (!Editing) return false;
            int host;
            var m = Current(out host);
            if (m == null) { Exit(); return false; }
            _cursor = m.Clamp(_cursor + dir);
            _jumpPending = true;
            _scanCd = 0;
            return true;
        }

        // ── per frame ──────────────────────────────────────────────────

        internal static void Tick()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (!Available(ed)) { if (Editing) Exit(); K.Show(false); return; }
            if (!Editing) { K.Show(false); WatchSelection(ed); return; }

            // Clicking a real tile or Esc leaves edit mode.
            if (Input.GetKeyDown(KeyCode.Escape) || (ed.selectedFloors != null && ed.selectedFloors.Count > 0)) { Exit(); return; }
            // Guarded like quick chart: the fake floor's inspector is open while editing, and an
            // "i" typed there (or Ctrl+I) must not toggle a twirl.
            if (!FieldNav.Typing && !ed.userIsEditingAnInputField && !CtrlOrCmd() && Keybinds.Down(Bind.QcSwirl))
                Apply(m => m.ToggleTwirl(_cursor));
            // Clicking another fake tile (or another multitrack's) while editing re-targets.
            WatchSelection(ed);
            if (!Editing) return;

            if (--_scanCd <= 0 || _jumpPending) { _scanCd = 30; if (!Rescan(ed)) { Exit(); return; } }
            DrawRing(_cursorObj != null ? (Vector2)_cursorObj.transform.position : (Vector2?)null,
                     _model != null ? (float)_model.Size : 1f);
            if (_jumpPending && _cursorDeco != null && _cursorObj != null)
            {
                _jumpPending = false;
                if (!InView(ed, _cursorObj.transform.position)) { try { ed.MoveCameraToDecoration(_cursorDeco); } catch { } }
            }
            TickPalette(ed);
        }

        // False when the multitrack is gone (deleted, or undone past its creation).
        private static bool Rescan(scnEditor ed)
        {
            _model = Current(out _host);
            if (_model == null) return false;
            _cursor = _model.Clamp(_cursor);
            _cursorDeco = MultitrackWriter.FloorDeco(ed, _model, _cursor);
            _cursorObj = _cursorDeco != null ? MultitrackWriter.ObjectOf(_cursorDeco) : null;
            return true;
        }

        // Selecting a fake floor in the editor enters edit mode at that tile.
        private static void WatchSelection(scnEditor ed)
        {
            var sel = ed.selectedDecorations;
            var cur = sel != null && sel.Count == 1 ? sel[0] : null;
            if (cur == _lastSel) return;
            _lastSel = cur;
            if (cur == null) return;
            int id; int k = MultitrackWriter.TileOf(cur, out id);
            if (k >= 0 && MultitrackWriter.Find(ed, id) != null) Enter(id, k);
        }

        private static bool CtrlOrCmd()
            => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
            || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

        private static bool InView(scnEditor ed, Vector3 p)
        {
            Camera c = null;
            try { c = ed.camera; } catch { }
            if (c == null) c = Camera.main;
            if (c == null) return true;
            var v = c.WorldToViewportPoint(p);
            return v.x > 0.05f && v.x < 0.95f && v.y > 0.05f && v.y < 0.95f;
        }

        // One sprite ring. Sprites are the world primitive proven to render here (EditorTrackKit).
        private static void DrawRing(Vector2? at, float size)
        {
            if (at == null) { if (_ring != null) _ring.SetActive(false); return; }
            if (_ring == null)
            {
                _ring = new GameObject("SapphireMultitrackCursor");
                UnityEngine.Object.DontDestroyOnLoad(_ring);
                var sr = _ring.AddComponent<SpriteRenderer>();
                sr.sprite = RingSprite();
                sr.sortingOrder = 32001;
                sr.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.9f);
            }
            if (!_ring.activeSelf) _ring.SetActive(true);
            _ring.transform.position = new Vector3(at.Value.x, at.Value.y, 0f);
            _ring.transform.localScale = Vector3.one * 1.5f * size;
        }

        private static Sprite RingSprite()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.ARGB32, false);
            float c = S * 0.5f - 0.5f, r = S * 0.5f - 3f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(2.5f - Mathf.Abs(d - r))));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        }

        private static void Notify(scnEditor ed, string msg)
        {
            try { ed.ShowNotification(msg); } catch { }
        }

        private static readonly PanelKit K = new PanelKit("SapphireMultitrack", 945, PanelKit.PaletteW, focusable: true);
        private static string _sig;

        // Palette while editing. Its number rows scrub; every change is one Write (one undo).
        // Reads the cached model; a full lookup per frame would walk every event in the level.
        private static void TickPalette(scnEditor ed)
        {
            var m = Editing ? _model : null;
            if (m == null) { K.Show(false); return; }
            // Everything the palette shows, so an undo or redo rebuilds it with the restored values.
            string sig = string.Join("|", new[] { m.Id.ToString(), m.Bpm.ToString("R"), m.StartBeat.ToString("R"),
                m.Size.ToString("R"), m.TrackColor, m.PlanetColor, m.HideOutside.ToString(), m.FadePassed.ToString() });
            if (K.SyncWidth()) _sig = null;
            if (!K.Built || sig != _sig) { _sig = sig; BuildPalette(m); }
            K.Show(true);
            K.TickScroll();
        }

        private static void BuildPalette(MultitrackModel m)
        {
            K.LblW = PanelKit.PaletteLblW;
            K.Scrollable = true;
            K.DefaultH = 360f;
            K.Rebuild(Loc.T("Multitrack") + " · " + Loc.T("experimental"), Exit, new Vector2(360f, -140f));
            ResizeHandle.AttachAll((RectTransform)K.PanelGo.transform, true, PanelKit.PaletteMinW, PanelKit.PaletteMinH);
            float y = -34f;
            y = K.FloatRow(y, Loc.T("BPM"), (float)m.Bpm, v => Apply(x => x.Bpm = Math.Max(1, v)));
            y = K.FloatRow(y, Loc.T("Start (host beats)"), (float)m.StartBeat, v => Apply(x => x.StartBeat = Math.Max(0, v)));
            y = K.FloatRow(y, Loc.T("Size"), (float)m.Size, v => Apply(x => x.Size = Math.Max(0.1, v)));
            y = K.FieldRow(y, Loc.T("Track colour"), m.TrackColor, v => Apply(x => x.TrackColor = Hex(v, x.TrackColor)));
            y = K.FieldRow(y, Loc.T("Planet colour"), m.PlanetColor, v => Apply(x => x.PlanetColor = Hex(v, x.PlanetColor)));
            y = K.ToggleRow(y, Loc.T("Hidden outside its run"), m.HideOutside, v => Apply(x => x.HideOutside = v));
            y = K.ToggleRow(y, Loc.T("Fade passed tiles"), m.FadePassed, v => Apply(x => x.FadePassed = v));
            y = K.PrimaryRow(y, Loc.T("Twirl on cursor tile"), () => Apply(x => x.ToggleTwirl(_cursor)));
            y = K.PrimaryRow(y, Loc.T("Rebuild"), Rebuild);
            y = K.PrimaryRow(y, Loc.T("Delete multitrack"), DeleteCurrent);
            K.Status(Loc.T("Tile keys add after the cursor · Backspace deletes · ←/→ move · I twirls · Esc exits"), y);
            y -= 32f;
            K.SetHeight(y);
        }

        // 6-digit hex only; anything else keeps the old colour.
        private static string Hex(string v, string old)
        {
            v = (v ?? "").Trim().TrimStart('#').ToLowerInvariant();
            if (v.Length != 6) return old;
            foreach (char c in v) if (!Uri.IsHexDigit(c)) return old;
            return v;
        }

        /* Recovery only: rewrite, then ONE full decoration reload. The edit path never does this,
           but if a game update leaves an object behind, this puts the scene back in step. */
        private static void Rebuild()
        {
            var ed = scnEditor.instance;
            Apply(_ => { }, true);
            try { ed.UpdateDecorationObjects(); } catch { }
        }

        private static void DeleteCurrent()
        {
            var ed = scnEditor.instance;
            int host;
            var m = Current(out host);
            if (m == null) return;
            try { MultitrackWriter.Delete(ed, host, m); } catch (Exception ex) { SapphireLog.Log("Multitrack: delete failed: " + ex); }
            Exit();
        }

        internal static void Dispose()
        {
            Exit();
            if (_ring != null) UnityEngine.Object.Destroy(_ring);
            _ring = null;
            K.Dispose(); _sig = null;
        }
    }
}

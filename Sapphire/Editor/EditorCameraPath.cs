using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* FreeCamera, first pass: a world-space overlay of the level's camera path. Each MoveCamera
       event becomes a keyframe dot at its computed target position, connected by DOTTED runs of
       mini-dots (LineRenderers and stretched sprite bars both fail to render in this build — the
       antialiased circle sprite is the one primitive proven visible, so everything is built from
       it). Position math mirrors ffxCameraPlus: target = anchor + position*tileSize, anchor by
       relativeTo (CamMovementType) — Tile/Player = the event's floor, Global = origin,
       LastPosition(NoRotation) = the previous keyframe. Player keyframes track the ball at
       runtime, so they're drawn orange (approximate); the rest cyan.
       CLICK a keyframe dot to inspect it: a details card (floor, relativeTo, offset, zoom,
       rotation, duration, ease) plus a dotted box showing the area the camera frames there —
       half-height = scrCamera.DefaultCameraOrthoSize(5) × zoom/100, width by screen aspect,
       rotated by the cumulative camera rotation. The card's ▶ previews the move: the box
       animates from the previous keyframe's pos/zoom/rot to this one over the event's duration
       (beats → seconds at the tile's BPM) with its DOTween ease. */
    internal static class EditorCameraPath
    {
        private struct Key
        {
            public Vector2 pos;
            public bool player;
            public int floor;
            public int rel;
            public Vector2 off;
            public float zoom;     // cumulative, %
            public float rot;      // cumulative, deg
            public float duration; // beats
            public DG.Tweening.Ease ease;
        }

        private static bool _on;
        private static GameObject _rootGo;      // scene-local (dies with the scene; Tick rebuilds)
        // Recycled path dots — see Rebuild. Parked (inactive) rather than destroyed between rebuilds.
        private static readonly List<SpriteRenderer> _dotPool = new List<SpriteRenderer>();
        private static int _dotUsed;
        private static GameObject _selGo;       // viewport box (animatable: local-space edges)
        private static Sprite _dotSprite;
        private static readonly List<Key> _keys = new List<Key>();
        private static int _sel = -1;
        private static int _sig;
        private static int _cooldown;

        // preview animation state
        private static bool _playing;
        private static bool _seq;               // playing the whole sequence key-by-key
        internal static bool UseGaps;           // sequence waits out the real beat gaps between events
        private static float[] _gaps = new float[0]; // key i start → key i+1 start, seconds
        private static float _playT0, _playDur;
        private static Vector2 _fromPos; private static float _fromZoom, _fromRot;
        private static GameObject _playBtnGo;   // hidden for duration-0 events

        // details card (screen-space, follows the selected dot)
        private static GameObject _uiCanvasGo;
        private static RectTransform _uiCanvasRect;
        private static GameObject _cardGo;
        private static TMPro.TextMeshProUGUI _cardText;

        internal static bool IsOn => _on;

        internal static void Toggle()
        {
            _on = !_on;
            if (!_on) { Hide(); Deselect(); }
            _sig = 0; _cooldown = 0;
        }

        internal static void Tick()
        {
            if (!_on) return;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null || ed.playMode || !MainClass.EditorSuiteOn) { Hide(); Deselect(); return; }

            HandleClick(ed);
            FollowCard(ed);
            if (_playing) TickPreview();
            if (_seq) TickSequence();
            if (_playing || _seq) return; // don't rebuild (and kill the box) mid-animation

            if (--_cooldown > 0 && _rootGo != null) return;
            _cooldown = 10;

            ComputePath(ed);
            // Dotted legs are drawn only inside the camera view (fixed spacing would otherwise
            // explode on long legs), so the signature includes the view: panning redraws.
            Rect view = ViewRect(ed);
            int sig = _keys.Count;
            for (int i = 0; i < _keys.Count; i++)
                sig = sig * 31 + Mathf.RoundToInt(_keys[i].pos.x * 10f) * 7
                    + Mathf.RoundToInt(_keys[i].pos.y * 10f) + Mathf.RoundToInt(_keys[i].zoom);
            sig = sig * 31 + Mathf.RoundToInt(view.x) + Mathf.RoundToInt(view.y) * 13
                + Mathf.RoundToInt(view.width);
            if (sig == _sig && _rootGo != null) return;
            _sig = sig;
            Rebuild(view);
            if (_sel >= _keys.Count) Deselect();
            else if (_sel >= 0 && _selGo == null) ShowSelection();
        }

        // Editor camera's world-space view, expanded for margin.
        private static Rect ViewRect(scnEditor ed)
        {
            var cam = EdCam(ed);
            if (cam == null) return new Rect(-50f, -50f, 100f, 100f);
            float hh = cam.orthographicSize * 1.4f;
            float hw = hh * cam.aspect;
            Vector3 c = cam.transform.position;
            return new Rect(c.x - hw, c.y - hh, hw * 2f, hh * 2f);
        }

        internal static void Dispose()
        {
            _on = false;
            Hide();
            if (_uiCanvasGo != null) UnityEngine.Object.Destroy(_uiCanvasGo);
            _uiCanvasGo = null; _uiCanvasRect = null; _cardGo = null; _cardText = null;
            _playBtnGo = null; _dotSprite = null; _sel = -1; _playing = false; _seq = false;
        }

        private static void Hide()
        {
            // Destroying the root takes the pooled dots with it — drop the stale references.
            if (_rootGo != null) UnityEngine.Object.Destroy(_rootGo);
            _dotPool.Clear(); _dotUsed = 0;
            _rootGo = null; _selGo = null; _playing = false;
        }

        private static void Deselect()
        {
            _sel = -1; _playing = false; _seq = false;
            if (_selGo != null) { UnityEngine.Object.Destroy(_selGo); _selGo = null; }
            if (_cardGo != null) _cardGo.SetActive(false);
        }

        // ── sequence playback (toolbar "Play all") ──────────────────────────
        internal static void PlayAll() { PlayAllFrom(false); }
        internal static void PlayAllFromSelection() { PlayAllFrom(true); }

        private static void PlayAllFrom(bool fromSelection)
        {
            if (!_on || _keys.Count == 0) return;
            int start = fromSelection && _sel >= 0 && _sel < _keys.Count ? _sel : 0;

            // Beat gaps: real seconds between event floors, straight from the game
            // (CalculateFloorEntryTimes fills scrFloor.entryTime with full speed/pause semantics).
            _gaps = new float[_keys.Count];
            if (UseGaps)
            {
                try
                {
                    if (ADOBase.lm != null) ADOBase.lm.CalculateFloorEntryTimes();
                    var fl = scnEditor.instance.floors;
                    for (int i = 0; i + 1 < _keys.Count; i++)
                    {
                        int a = _keys[i].floor, b = _keys[i + 1].floor;
                        if (fl != null && a >= 0 && a < fl.Count && b >= 0 && b < fl.Count
                            && fl[a] != null && fl[b] != null)
                            _gaps[i] = Mathf.Max(0.05f, (float)(fl[b].entryTime - fl[a].entryTime));
                    }
                }
                catch { }
            }

            _seq = true;
            _sel = start;
            ShowSelection();
            StartPreview();
        }

        // With gaps ON a key holds until the next event's real start time (cutting a still-running
        // tween short, like a later MoveCamera would); OFF plays back-to-back.
        private static void TickSequence()
        {
            bool last = _sel + 1 >= _keys.Count;
            bool advance;
            if (!last && UseGaps && _sel >= 0 && _sel < _gaps.Length && _gaps[_sel] > 0f)
                advance = Time.unscaledTime - _playT0 >= _gaps[_sel];
            else
                advance = !_playing;
            if (advance) AdvanceSequence();
        }

        private static void AdvanceSequence()
        {
            if (_sel + 1 >= _keys.Count) { if (!_playing) _seq = false; return; }
            _sel++;
            _playing = false;
            ShowSelection();
            StartPreview();
        }

        // ── interaction ─────────────────────────────────────────────────────
        private static void HandleClick(scnEditor ed)
        {
            if (!Input.GetMouseButtonDown(0) || _keys.Count == 0) return;
            try { if (EditorToolbar.PointerOverSapphireUI()) return; } catch { }
            Camera cam = EdCam(ed);
            if (cam == null) return;
            Vector2 w = cam.ScreenToWorldPoint(Input.mousePosition);
            int best = -1; float bestD = 0.6f;
            for (int i = 0; i < _keys.Count; i++)
            {
                float d = Vector2.Distance(_keys[i].pos, w);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) { Deselect(); return; }
            _sel = best;
            _playing = false; _seq = false;
            ShowSelection();
        }

        private static Camera EdCam(scnEditor ed)
        {
            Camera cam = null;
            try { cam = ed.camera; } catch { }
            return cam != null ? cam : Camera.main;
        }

        private static void ShowSelection()
        {
            if (_selGo != null) UnityEngine.Object.Destroy(_selGo);
            if (_sel < 0 || _sel >= _keys.Count || _rootGo == null) return;
            var k = _keys[_sel];

            // Viewport box, built in LOCAL space so the preview can move/rotate/scale the parent.
            _selGo = new GameObject("Viewport");
            _selGo.transform.SetParent(_rootGo.transform, false);
            float halfH = 5f * Mathf.Max(1f, k.zoom) / 100f; // scrCamera.DefaultCameraOrthoSize
            float halfW = halfH * (Screen.height > 0 ? (float)Screen.width / Screen.height : 1.778f);
            var col = new Color(1f, 0.85f, 0.3f, 0.9f);
            DrawDotted(_selGo.transform, new Vector2(-halfW, -halfH), new Vector2(halfW, -halfH), col, true);
            DrawDotted(_selGo.transform, new Vector2(halfW, -halfH), new Vector2(halfW, halfH), col, true);
            DrawDotted(_selGo.transform, new Vector2(halfW, halfH), new Vector2(-halfW, halfH), col, true);
            DrawDotted(_selGo.transform, new Vector2(-halfW, halfH), new Vector2(-halfW, -halfH), col, true);
            _selGo.transform.position = new Vector3(k.pos.x, k.pos.y, 0f);
            _selGo.transform.rotation = Quaternion.Euler(0f, 0f, k.rot);

            // details card (no ▶ for instant, duration-0 moves)
            EnsureCard();
            _cardGo.SetActive(true);
            if (_playBtnGo != null) _playBtnGo.SetActive(k.duration > 0.001f);
            _cardText.text =
                "MoveCamera · floor " + k.floor + "\n"
                + RelName(k.rel) + "  offset (" + k.off.x.ToString("0.##") + ", " + k.off.y.ToString("0.##") + ")\n"
                + "zoom " + k.zoom.ToString("0.#") + "%  rot " + k.rot.ToString("0.#") + "°\n"
                + "duration " + k.duration.ToString("0.##") + "  ease " + k.ease;
        }

        private static string RelName(int rel)
        {
            switch (rel)
            {
                case 0: return "Player-relative";
                case 1: return "Tile-relative";
                case 2: return "Global";
                case 3: return "Last position";
                case 4: return "Last position (no rot)";
                default: return "relativeTo " + rel;
            }
        }

        // ── preview animation ───────────────────────────────────────────────
        private static void StartPreview()
        {
            if (_sel < 0 || _sel >= _keys.Count || _selGo == null) return;
            var to = _keys[_sel];
            if (_sel > 0)
            {
                var p = _keys[_sel - 1];
                _fromPos = p.pos; _fromZoom = p.zoom; _fromRot = p.rot;
            }
            else
            {
                // before the first event: the camera sits on the start with default zoom/rot
                scnEditor ed = null; try { ed = scnEditor.instance; } catch { }
                _fromPos = ed != null ? FloorPos(ed, 0) : to.pos;
                _fromZoom = 100f; _fromRot = 0f;
            }

            // duration is in beats → seconds at the event tile's bpm (level bpm × tile speed)
            float bpm = 100f;
            try
            {
                var ld = scnGame.instance != null ? scnGame.instance.levelData : null;
                if (ld != null && ld.bpm > 1f) bpm = ld.bpm;
                var fl = scnEditor.instance.floors;
                if (fl != null && to.floor >= 0 && to.floor < fl.Count && fl[to.floor] != null
                    && fl[to.floor].speed > 0.01f) bpm *= fl[to.floor].speed;
            }
            catch { }
            _playDur = Mathf.Max(0.05f, to.duration * 60f / bpm);
            _playT0 = Time.unscaledTime;
            _playing = true;
        }

        private static void TickPreview()
        {
            if (_selGo == null || _sel < 0 || _sel >= _keys.Count) { _playing = false; return; }
            var to = _keys[_sel];
            float t = Time.unscaledTime - _playT0;
            float e;
            if (t >= _playDur) { e = 1f; _playing = false; }
            else
            {
                try { e = DG.Tweening.Core.Easing.EaseManager.Evaluate(to.ease, null, t, _playDur, 1.70158f, 0f); }
                catch { e = t / _playDur; }
            }
            // LerpUnclamped so Back/Elastic eases overshoot like the game
            Vector2 pos = _fromPos + (to.pos - _fromPos) * e;
            float rot = _fromRot + (to.rot - _fromRot) * e;
            float zoom = Mathf.Max(1f, _fromZoom + (to.zoom - _fromZoom) * e);
            _selGo.transform.position = new Vector3(pos.x, pos.y, 0f);
            _selGo.transform.rotation = Quaternion.Euler(0f, 0f, rot);
            _selGo.transform.localScale = Vector3.one * (zoom / Mathf.Max(1f, to.zoom));
        }

        // keep the card pinned next to the selected dot on screen
        private static void FollowCard(scnEditor ed)
        {
            if (_cardGo == null || !_cardGo.activeSelf || _sel < 0 || _sel >= _keys.Count) return;
            Camera cam = EdCam(ed);
            if (cam == null) return;
            Vector2 sp = cam.WorldToScreenPoint(_keys[_sel].pos);
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_uiCanvasRect, sp, null, out local)) return;
            var half = _uiCanvasRect.rect.size * 0.5f;
            var r = (RectTransform)_cardGo.transform;
            var pos = local + new Vector2(18f, 18f);
            pos.x = Mathf.Clamp(pos.x, -half.x + 4f, half.x - r.sizeDelta.x - 4f);
            pos.y = Mathf.Clamp(pos.y, -half.y + 4f, half.y - r.sizeDelta.y - 4f);
            r.anchoredPosition = pos;
        }

        private static void EnsureCard()
        {
            if (_cardGo != null) return;
            _uiCanvasGo = new GameObject("SapphireCameraCard", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_uiCanvasGo);
            var canvas = _uiCanvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 907;
            var scaler = _uiCanvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _uiCanvasGo.AddComponent<GraphicRaycaster>(); // only the ▶ button is a raycast target
            _uiCanvasRect = (RectTransform)_uiCanvasGo.transform;

            _cardGo = new GameObject("Card", typeof(RectTransform));
            _cardGo.transform.SetParent(_uiCanvasGo.transform, false);
            var r = (RectTransform)_cardGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); // anchoredPosition = canvas-centre local
            r.pivot = new Vector2(0f, 0f);
            r.sizeDelta = new Vector2(258f, 74f);
            var bg = _cardGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 8f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.94f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 0.85f, 0.3f, 0.5f);
            bg.raycastTarget = false; // clicks pass through the card body
            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(_cardGo.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(10f, 6f); tr.offsetMax = new Vector2(-38f, -6f);
            _cardText = UIBuilder.Tmp(txtGo, "", 12f, TextAnchor.UpperLeft, Theme.Text);
            _cardText.raycastTarget = false;

            // ▶ preview button, right edge of the card
            var btnGo = new GameObject("Play", typeof(RectTransform));
            btnGo.transform.SetParent(_cardGo.transform, false);
            var br = (RectTransform)btnGo.transform;
            br.anchorMin = br.anchorMax = new Vector2(1f, 0.5f);
            br.pivot = new Vector2(1f, 0.5f);
            br.anchoredPosition = new Vector2(-6f, 0f);
            br.sizeDelta = new Vector2(26f, 26f);
            var bbg = btnGo.AddComponent<RoundedRectGraphic>();
            bbg.Radius = 6f;
            bbg.color = new Color(1f, 0.85f, 0.3f, 0.18f);
            bbg.BorderWidth = 1f;
            bbg.BorderColor = new Color(1f, 0.85f, 0.3f, 0.5f);
            bbg.raycastTarget = true;
            var glyphGo = new GameObject("G", typeof(RectTransform));
            glyphGo.transform.SetParent(btnGo.transform, false);
            var gr = (RectTransform)glyphGo.transform;
            gr.anchorMin = Vector2.zero; gr.anchorMax = Vector2.one;
            gr.offsetMin = Vector2.zero; gr.offsetMax = Vector2.zero;
            var glyph = UIBuilder.Tmp(glyphGo, "▶", 13f, TextAnchor.MiddleCenter, Theme.Text);
            glyph.raycastTarget = false;
            UI.ClickHandler.Attach(btnGo, StartPreview);
            _playBtnGo = btnGo;
        }

        // ── path math ───────────────────────────────────────────────────────
        private static void ComputePath(scnEditor ed)
        {
            _keys.Clear();
            float tile = 1.5f;
            try { tile = ADOBase.controller.tileSize; } catch { }

            var evs = new List<ADOFAI.LevelEvent>();
            try
            {
                foreach (var e in ed.events)
                    if (e != null && e.eventType == ADOFAI.LevelEventType.MoveCamera) evs.Add(e);
            }
            catch { }
            evs.Sort((a, b) => a.floor.CompareTo(b.floor));

            Vector2 last = Vector2.zero;
            bool haveLast = false;
            float zoom = 100f, rot = 0f;
            foreach (var e in evs)
            {
                Vector2 off = Vector2.zero;
                bool hasPos = false;
                try
                {
                    if (e.ContainsKey("position") && !KeyDisabled(e, "position") && e["position"] is Vector2 v)
                    { off = v; hasPos = true; }
                }
                catch { }

                int rel = 0;
                try { if (e.ContainsKey("relativeTo") && e["relativeTo"] is CamMovementType m) rel = (int)m; }
                catch { }
                // numeric fields box as int/double/float depending on source — Convert, don't pattern-match
                if (!KeyDisabled(e, "zoom")) zoom = F(e, "zoom", zoom);
                if (!KeyDisabled(e, "rotation")) rot = F(e, "rotation", rot);
                float dur = F(e, "duration", 0f);
                var ease = DG.Tweening.Ease.Linear;
                try { if (e.ContainsKey("ease") && e["ease"] is DG.Tweening.Ease es) ease = es; } catch { }

                Vector2 anchor;
                bool isPlayer = false;
                switch (rel)
                {
                    case 1: anchor = FloorPos(ed, e.floor); break;               // Tile
                    case 2: anchor = Vector2.zero; break;                        // Global
                    case 3:                                                      // LastPosition
                    case 4: anchor = haveLast ? last : FloorPos(ed, e.floor); break;
                    default: anchor = FloorPos(ed, e.floor); isPlayer = true; break; // Player (tracks ball)
                }
                if (!hasPos && (rel == 3 || rel == 4)) continue; // relative move with no offset = no-op

                Vector2 p = anchor + off * tile;
                last = p; haveLast = true;
                _keys.Add(new Key
                {
                    pos = p, player = isPlayer, floor = e.floor, rel = rel, off = off,
                    zoom = zoom, rot = rot, duration = dur, ease = ease
                });
            }
        }

        private static bool KeyDisabled(ADOFAI.LevelEvent e, string key)
        {
            try { return e.disabled != null && e.disabled.TryGetValue(key, out var d) && d; }
            catch { return false; }
        }

        private static float F(ADOFAI.LevelEvent e, string key, float def)
        {
            try { if (e.ContainsKey(key)) return Convert.ToSingle(e[key]); } catch { }
            return def;
        }

        private static Vector2 FloorPos(scnEditor ed, int seq)
        {
            try
            {
                var fl = ed.floors;
                if (fl != null && seq >= 0 && seq < fl.Count && fl[seq] != null)
                    return fl[seq].transform.position;
            }
            catch { }
            return Vector2.zero;
        }

        // ── drawing ─────────────────────────────────────────────────────────
        /* Was: Hide() → Destroy(_rootGo) → respawn every dot as a fresh GameObject +
           SpriteRenderer. The rebuild signature folds in the camera view rect, so ANY pan
           retriggered this — hundreds of GameObjects churned several times a second, plus
           the SpriteRenderer registration cost. Keep the root and recycle the dots. Only the
           path dots are pooled; the selection box (_selGo) has its own lifetime and is
           rebuilt on selection change, not on pan. */
        private static void Rebuild(Rect view)
        {
            // Preserve Hide()'s side effects for callers (selection box drops, playback stops)
            // without tearing down the dot objects.
            if (_selGo != null) { UnityEngine.Object.Destroy(_selGo); _selGo = null; }
            _playing = false;
            if (_rootGo == null) _rootGo = new GameObject("SapphireCameraPath");
            _dotUsed = 0;

            if (_keys.Count > 0)
            {
                var lineCol = new Color(0.35f, 0.85f, 1f, 0.9f);
                for (int i = 1; i < _keys.Count; i++)
                    DrawDotted(_rootGo.transform, _keys[i - 1].pos, _keys[i].pos, lineCol, false, view, true);

                for (int i = 0; i < _keys.Count; i++)
                    MakeDot(_rootGo.transform, _keys[i].pos, 0.28f, _keys[i].player
                        ? new Color(1f, 0.65f, 0.25f, 0.95f)    // Player-relative: tracks the ball
                        : new Color(0.35f, 0.85f, 1f, 0.95f), false, true);
            }

            // Park whatever this path didn't need; they stay warm for the next rebuild.
            for (int i = _dotUsed; i < _dotPool.Count; i++)
                if (_dotPool[i] != null && _dotPool[i].gameObject.activeSelf)
                    _dotPool[i].gameObject.SetActive(false);
        }

        // Dotted run of mini-dots from a to b at FIXED world spacing (the one primitive proven to
        // render here). An optional clip rect draws only the visible slice, phase anchored to the
        // segment start so the dots don't crawl while panning.
        private static void DrawDotted(Transform parent, Vector2 a, Vector2 b, Color color, bool local,
            Rect? clip = null, bool pooled = false)
        {
            float len = Vector2.Distance(a, b);
            if (len < 0.001f) return;
            float s0 = 0f, s1 = len;
            if (clip.HasValue)
            {
                if (!ClipSegment(a, b, clip.Value, out var t0, out var t1)) return;
                s0 = t0 * len; s1 = t1 * len;
            }
            float spacing = 0.4f;
            int count = Mathf.FloorToInt((s1 - s0) / spacing) + 1;
            if (count > 1500) spacing = (s1 - s0) / 1500f;   // safety cap at extreme zoom-outs
            for (float s = Mathf.Ceil(s0 / spacing) * spacing; s <= s1; s += spacing)
                MakeDot(parent, Vector2.Lerp(a, b, s / len), 0.16f, color, local, pooled);
        }

        // Liang-Barsky segment/rect clip; returns the [t0,t1] parametric slice inside the rect.
        private static bool ClipSegment(Vector2 a, Vector2 b, Rect r, out float t0, out float t1)
        {
            t0 = 0f; t1 = 1f;
            Vector2 d = b - a;
            float[] p = { -d.x, d.x, -d.y, d.y };
            float[] q = { a.x - r.xMin, r.xMax - a.x, a.y - r.yMin, r.yMax - a.y };
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Abs(p[i]) < 1e-6f)
                {
                    if (q[i] < 0f) return false; // parallel and outside
                    continue;
                }
                float t = q[i] / p[i];
                if (p[i] < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
            }
            return true;
        }

        private static void MakeDot(Transform parent, Vector2 pos, float scale, Color color, bool local,
            bool pooled = false)
        {
            SpriteRenderer sr = null;
            if (pooled)
            {
                while (_dotUsed < _dotPool.Count && _dotPool[_dotUsed] == null) _dotPool.RemoveAt(_dotUsed);
                if (_dotUsed < _dotPool.Count)
                {
                    sr = _dotPool[_dotUsed];
                    if (!sr.gameObject.activeSelf) sr.gameObject.SetActive(true);
                }
            }
            if (sr == null)
            {
                var go = new GameObject("Dot");
                go.transform.SetParent(parent, false);
                sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = DotSprite();
                sr.sortingOrder = 32001;
                if (pooled) _dotPool.Add(sr);
            }
            if (pooled) _dotUsed++;

            var t = sr.transform;
            if (local) t.localPosition = new Vector3(pos.x, pos.y, 0f);
            else t.position = new Vector3(pos.x, pos.y, 0f);
            t.localScale = Vector3.one * scale;
            sr.color = color;
        }

        private static Sprite DotSprite()
        {
            if (_dotSprite != null) return _dotSprite;
            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.ARGB32, false);
            float r = S * 0.5f - 1f, cx = S * 0.5f - 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cx) * (y - cx));
                    float a = Mathf.Clamp01(r - d); // 1px antialiased edge
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            _dotSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
            return _dotSprite;
        }
    }
}

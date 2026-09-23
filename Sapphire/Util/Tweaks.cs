using TMPro;
using UnityEngine;

namespace Sapphire
{
    // Editor behaviour helpers: rebindable autoplay-pause key, Edit mode, tile angle.
    internal static class Tweaks
    {
        /* KeyCode (as int) the editor's autoplay-pause check should read. A transpiler in
           Patches.cs swaps the hardcoded KeyCode.Space (32) in scnEditor.Update for a call
           to this, so the pause key is rebindable. Falls back to Space so behaviour is
           unchanged when settings aren't loaded yet. */
        internal static int AutoPauseKeyCode()
        {
            try
            {
                var s = MainClass.Settings;
                if (s == null) return (int)KeyCode.Space;
                if (!s.AutoplayPauseEnabled) return (int)KeyCode.None;
                return (int)s.AutoplayPauseKey;
            }
            catch { return (int)KeyCode.Space; }
        }

        // ── Edit mode ───────────────────────────────────────────────────────
        // Clean-screen charting: force autoplay on at play start (rising edge only) and
        // fade the editor's corner icons plus the play-test HUD icons/error meter. The
        // Sapphire mode cluster covers those toggles.
        private static bool _wasEditorPlay;
        private static bool _cornersFaded;
        private static int _cornerCooldown;

        // Bismuth interop: Bismuth's overlays/key viewer honor Edit mode through a
        // static hook (`Bismuth.Settings.ExternalEditorSuppress`, Bismuth ≥1.4.0), set via
        // reflection so the dependency stays soft — no-op when Bismuth isn't installed.
        private static System.Reflection.FieldInfo _bismuthSuppress;
        private static bool _bismuthLookedUp;
        private static bool _bismuthSent;

        private static void SetBismuthSuppress(bool on)
        {
            if (on == _bismuthSent) return;
            try
            {
                if (!_bismuthLookedUp)
                {
                    _bismuthLookedUp = true;
                    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.GetName().Name != "Bismuth") continue;
                        _bismuthSuppress = asm.GetType("Bismuth.Settings")?.GetField(
                            "ExternalEditorSuppress",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        break;
                    }
                }
                if (_bismuthSuppress != null) _bismuthSuppress.SetValue(null, on);
                _bismuthSent = on;
            }
            catch { _bismuthSent = on; }
        }

        // StopMod: never leave Bismuth's overlays suppressed by a mod that's gone.
        internal static void ReleaseBismuthSuppress() => SetBismuthSuppress(false);

        // WASD pans the editor camera — only with NO tile selected (the editor uses WASD to place
        // tiles when one is), nothing being typed, and no ctrl/cmd chords (save/select-all).
        // True while A means PAN (pan-eligible + A held): a Harmony prefix swallows the
        // editor's ToggleAuto for the duration (see Patches.PanAutoGuardPatch).
        private static int _wasdPanFrame = -10; // last frame a WASD pan actually moved the camera

        internal static bool SuppressAutoToggle
        {
            get
            {
                try
                {
                    // A is the pan-left key while pan-eligible (no selection etc.) — never let
                    // it toggle autoplay. Two conditions so it's robust to Update-order skew
                    // between the game's key handling and ours: (1) A held now in a pan context,
                    // (2) a real pan moved the camera this frame or last.
                    if (Time.frameCount - _wasdPanFrame <= 1) { Diag("autoplay toggle swallowed (WASD pan just moved)"); return true; }
                    bool held = PanEligible(scnEditor.instance) && Input.GetKey(KeyCode.A);
                    if (held) Diag("autoplay toggle swallowed (A held, pan-eligible)");
                    return held;
                }
                catch { return false; }
            }
        }

        private static bool PanEligible(scnEditor ed)
        {
            if (ed == null || !MainClass.EditorSuiteOn) return false;
            try
            {
                if (ed.playMode) return false;
                if (ed.selectedFloors != null && ed.selectedFloors.Count > 0) return false;
                if (ed.userIsEditingAnInputField) return false;
                // Sapphire's own TMP fields (event selector search etc.) don't set the
                // game's flag — typing WASD there must not pan
                var es = UnityEngine.EventSystems.EventSystem.current;
                var sel = es != null ? es.currentSelectedGameObject : null;
                if (sel != null && (sel.GetComponent<TMPro.TMP_InputField>() != null
                                 || sel.GetComponent<UnityEngine.UI.InputField>() != null)) return false;
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                    || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)) return false;
            }
            catch { return false; }
            return true;
        }

        // ponytail: temporary triage for the autoplay/camera reports; drop once the cause is confirmed.
        private static void Diag(string msg) => SapphireLog.Debug("[state] " + msg);

        /* Playtest camera probe: one line a second while playing, so a single repro says whether
           the game still WANTS to follow (scrCamera.followMode) and whether the camera is moving
           with the planet — and whether any ghost-preview floors are loose in the scene. */
        private static float _probeAt;

        internal static void TickCameraProbe()
        {
            try
            {
                var ed = scnEditor.instance;
                if (ed == null || !ed.playMode) { _probeAt = 0f; return; }
                if (Time.unscaledTime < _probeAt) return;
                _probeAt = Time.unscaledTime + 1f;

                var sb = new System.Text.StringBuilder("[cam]");
                try { var c = ed.camera != null ? ed.camera : Camera.main;
                      if (c != null) sb.Append(" pos=").Append(c.transform.position.ToString("0.0")).Append(" size=").Append(c.orthographicSize.ToString("0.00")); } catch { }
                try { var sc = scrCamera.instance; if (sc != null) sb.Append(" followMode=").Append(sc.followMode); } catch { }
                try { var ctrl = scrController.instance;
                      if (ctrl != null && ctrl.planetarySystem != null && ctrl.planetarySystem.planetRed != null)
                          sb.Append(" planet=").Append(ctrl.planetarySystem.planetRed.transform.position.ToString("0.0")); } catch { }
                try { var host = GameObject.Find("SapphireGhosts");
                      sb.Append(" ghosts=").Append(host != null ? host.transform.childCount : 0); } catch { }
                try { sb.Append(" floors=").Append(ADOBase.lm.listFloors.Count); } catch { }
                try { sb.Append(" auto=").Append(RDC.auto); } catch { }
                SapphireLog.Debug(sb.ToString());
            }
            catch { }
        }

        internal static void TickWasdPan()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (!PanEligible(ed)) return;
            try
            {
                Vector2 dir = Vector2.zero;
                if (Input.GetKey(KeyCode.W)) dir.y += 1f;
                if (Input.GetKey(KeyCode.S)) dir.y -= 1f;
                if (Input.GetKey(KeyCode.A)) dir.x -= 1f;
                if (Input.GetKey(KeyCode.D)) dir.x += 1f;
                if (dir == Vector2.zero) return;
                var cam = ed.camera;
                if (cam == null) return;
                // zoom-proportional speed so panning feels the same at any zoom
                float speed = cam.orthographicSize * 1.6f;
                cam.transform.position += (Vector3)(dir.normalized * speed * Time.unscaledDeltaTime);
                if (Time.frameCount - _wasdPanFrame > 30) Diag("WASD pan moved the editor camera");
                _wasdPanFrame = Time.frameCount; // latch: the autoplay-toggle guard reads this
            }
            catch { }
        }

        /* The editor's autoplay control tip (scnEditor.controlsTip — the "Space [⎵] Pause ❚❚"
           help text on the Otto canvas) advertises Space as the pause key. But Sapphire's
           transpiler REMAPS the editor's autoplay-pause key off Space (AutoplayPauseKey), so the
           hint is misleading while the suite is on. scnEditor.Update rewrites its text every frame,
           so disable the Text component per-frame (surgical — leaves the Otto mascot). Reflection
           so it degrades gracefully if the field is absent. Restore to the game's natural state
           (enabled during play) when the suite goes off. */
        private static System.Reflection.FieldInfo _controlsTipFi;
        private static bool _controlsTipMissing, _hidControlsTip;

        private static UnityEngine.UI.Graphic ControlsTip(scnEditor ed)
        {
            if (_controlsTipMissing || ed == null) return null;
            if (_controlsTipFi == null)
            {
                _controlsTipFi = typeof(scnEditor).GetField("controlsTip",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);
                if (_controlsTipFi == null) { _controlsTipMissing = true; return null; }
            }
            return _controlsTipFi.GetValue(ed) as UnityEngine.UI.Graphic;
        }

        /* Ctrl+Z relay.

           scnEditor.HandleKeyboardActions returns early whenever userIsEditingAnInputField is
           true, and that property only asks the shared EventSystem whether the focused object
           is a focused TMP_InputField — a Sapphire field counts. So every edit made in one of
           our panels left the caret in a field and silently swallowed undo. The game's own undo
           stack is fine (our commits go through SaveStateScope, and LevelData.Copy deep-copies
           every event), it just never got the key.

           Fires ONLY while a Sapphire field holds focus, i.e. exactly when the game refuses —
           otherwise the game handles the same press and one keystroke would undo twice. */
        internal static void TickUndoRelay()
        {
            if (!Input.GetKeyDown(KeyCode.Z)) return;
            if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
               || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand))) return;
            try
            {
                if (!MainClass.MasterSwitchOn) return;
                var ed = scnEditor.instance;
                if (ed == null || ed.playMode || !ed.userIsEditingAnInputField) return;
                var es = UnityEngine.EventSystems.EventSystem.current;
                var go = es != null ? es.currentSelectedGameObject : null;
                if (go == null || !IsSapphireOwned(go)) return;   // a game field: leave it alone
                var f = go.GetComponent<TMP_InputField>();
                // A multi-line field is a document (the script editor): Ctrl+Z there must not undo the level.
                if (f != null && f.lineType != TMP_InputField.LineType.SingleLine) return;
                if (f != null) f.DeactivateInputField();
                es.SetSelectedGameObject(null);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (shift) ed.Redo(); else ed.Undo();
            }
            catch { }
        }

        private static bool IsSapphireOwned(GameObject go)
        {
            var t = go.transform;
            while (t.parent != null) t = t.parent;
            return t.name.StartsWith("Sapphire");
        }

        /* Tile-key ring off during a playtest. floorButtonContainer is the ring of angle keys
           around the selected tile (and floorButtonArbitraryContainer its free-angle twin); the
           editor leaves both up while the song plays, advertising verbs that Patches now
           refuses. Re-asserted every frame rather than once, because the editor re-shows them
           on its own schedule. */
        internal static void TickPlaytestLock()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) { _lockedRing = false; return; }
            // A playtest RUN, or Sapphire's play MODE — the same run-versus-mode mix-up as
            // PlaytestLocked had. Both rings advertise edits the path lock refuses.
            bool mode = false;
            try { var st = MainClass.Settings; mode = st != null && st.PlayModeActive; } catch { }
            bool want = MainClass.MasterSwitchOn && (ed.playMode || mode);
            if (!want && !_lockedRing) return;
            _lockedRing = want;
            try { if (ed.floorButtonContainer != null) SetRing(ed.floorButtonContainer, !want); } catch { }
            try { if (ed.floorButtonArbitraryContainer != null) SetRing(ed.floorButtonArbitraryContainer, !want); } catch { }
        }

        private static bool _lockedRing;

        // Alpha + raycast, never SetActive: the editor reads activeSelf to decide what to
        // re-show, and toggling it underneath desyncs its own state.
        private static void SetRing(GameObject go, bool on)
        {
            var cg = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
            float a = on ? 1f : 0f;
            if (cg.alpha != a) cg.alpha = a;
            if (cg.blocksRaycasts != on) cg.blocksRaycasts = on;
        }

        internal static void TickControlsTip()
        {
            if (_controlsTipMissing) return;
            try
            {
                scnEditor ed = null;
                try { ed = scnEditor.instance; } catch { }
                bool active = ed != null && MainClass.Settings != null && MainClass.EditorSuiteOn;
                if (!active && !_hidControlsTip) return; // nothing to do
                var tip = ControlsTip(ed);
                if (tip == null) return;
                if (active) { if (tip.enabled) tip.enabled = false; _hidControlsTip = true; }
                else { tip.enabled = ed != null && ed.playMode; _hidControlsTip = false; } // restore
            }
            catch { }
        }

        // StopMod: re-show the control tip we hid (the ticker won't run to restore it).
        internal static void RestoreControlsTip()
        {
            if (!_hidControlsTip) return;
            try
            {
                var ed = scnEditor.instance;
                var tip = ControlsTip(ed);
                if (tip != null) tip.enabled = ed != null && ed.playMode;
            }
            catch { }
            _hidControlsTip = false;
        }

        // StopMod: un-fade the game's corner HUD (difficulty/speed/no-fail/autoplay/error meter) —
        // Edit mode fades them per-frame, so a disable mid-fade would otherwise leave them at
        // alpha 0 and break the vanilla UI (and other mods) until a scene reload.
        internal static void DisposeEditorMode()
        {
            _cornersFaded = false;
            _cornerCooldown = 0;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            try { FadeCorners(ed, false, false); } catch { }
        }

        /* PLAY MODE — the playtest preset. Autoplay off, no-fail on (optional), and the whole
           Sapphire UI hidden except pitch (that part is free: MainClass.EditorSuiteOn turns
           false while this is active, and every module already gates on it).

           Asserted on EDGES, not every frame: switching the mode on, and starting a playtest.
           Holding autoplay off every frame would fight the game's own keys, and a charter who
           deliberately flips autoplay mid-run should keep it. */
        private static bool _wasPlayActive, _wasPlayPlaying;

        internal static void TickPlayMode()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool active = false, playing = false;
            try { active = s != null && s.PlayModeActive; } catch { }
            try { playing = ed != null && ed.playMode; } catch { }

            bool assert = active && (!_wasPlayActive || (playing && !_wasPlayPlaying));
            // Same condition as the patches in Patches.cs: with Sapphire switched off, play mode
            // must not reach into the game's lock either.
            bool lockOn = active && MainClass.MasterSwitchOn;
            SyncEditLock(ed, lockOn, lockOn && !_wasLockOn, !lockOn && _wasLockOn);
            _wasLockOn = lockOn;
            _wasPlayActive = active;
            _wasPlayPlaying = playing;
            if (!assert) return;

            try { RDC.auto = false; Diag("play mode forced autoplay OFF"); } catch { }
            if (!s.PlayModeNoFail) return;
            try
            {
                // Both: GCS is the setting the run starts from, the controller flag is the live
                // run (the editor's own N shortcut writes the controller, not GCS).
                GCS.useNoFail = true;
                var c = scrController.instance;
                if (c != null) c.noFail = true;
            }
            catch { }
        }

        /* Play mode is for PLAYING, so it borrows the game's own "Lock path" rather than guarding
           edits one method at a time. That lock is read by CreateFloorWithCharOrAngle,
           DeleteFloor, both Delete*Selection, RotateFloor, event add/remove, decoration drags and
           the undoable editor actions' Execute — far more than the handful of methods Sapphire
           patches — and every Sapphire tool already checks it. The game's own lock button shows
           the state, so nothing is locked without saying so.

           The charter's own lock setting is remembered on the way in and put back on the way
           out, so leaving play mode never unlocks a path they had locked themselves. While the
           mode is on the lock is re-asserted if something clears it, which a level load does.

           The placement rings are hidden by TickPlaytestLock, which already did it for a
           playtest run. */
        private static bool _lockBeforePlay, _wasLockOn;

        private static void SyncEditLock(scnEditor ed, bool active, bool entered, bool left)
        {
            if (ed == null) return;
            try
            {
                if (entered) _lockBeforePlay = ed.lockPathEditing;
                if (active && !ed.lockPathEditing) ed.LockPathEditing(true);
                else if (left && ed.lockPathEditing != _lockBeforePlay) ed.LockPathEditing(_lockBeforePlay);
            }
            catch { }
        }

        internal static void TickEditorMode()
        {
            bool playing = false;
            scnEditor ed = null;
            bool active = false;
            try
            {
                ed = scnEditor.instance;
                playing = ed != null && ed.playMode;
                var s = MainClass.Settings;
                active = ed != null && s != null && MainClass.EditorSuiteOn && s.EditorModeActive;
                if (playing && !_wasEditorPlay && active && s.EditorModeAutoplay)
                { RDC.auto = true; Diag("edit mode forced autoplay ON at play start"); }
            }
            catch { }
            _wasEditorPlay = playing;
            SetBismuthSuppress(active);

            /* The mode cluster carries difficulty / no-fail / autoplay itself and sits in the
               same screen corner as the game's icons, so the icons are both redundant and
               overlapping — drop them whenever the cluster is up, not just in editor mode. */
            bool hud = active;
            try { hud = active || EditorEvents.ModeClusterVisible; } catch { }
            if (hud)
            {
                // Re-assert periodically: the editor recreates these on scene reloads.
                if (--_cornerCooldown <= 0)
                {
                    _cornerCooldown = 30;
                    FadeCorners(ed, true, active);
                }
                _cornersFaded = true;
            }
            else if (_cornersFaded)
            {
                _cornersFaded = false;
                _cornerCooldown = 0;
                FadeCorners(ed, false, false);
            }
        }

        // fadeMeter is separate: the hit error meter is worth keeping while play-testing,
        // so only editor mode drops it.
        private static void FadeCorners(scnEditor ed, bool fade, bool fadeMeter)
        {
            if (ed != null)
            {
                try { FadeCorner(ed.editorDifficultySelector, fade); } catch { }
                try { FadeCorner(ed.speedIndicator, fade); } catch { }
                try { FadeCorner(ed.buttonNoFail, fade); } catch { }
                // The autoplay icon has no corner container of its own — the game drives
                // the Image directly, so flip component .enabled (Bismuth's proven way).
                try { if (ed.autoImage != null) ed.autoImage.enabled = !fade; } catch { }
                try { if (ed.buttonAuto != null) ed.buttonAuto.enabled = !fade; } catch { }
            }
            // Play-test HUD: difficulty/no-fail/autoplay icon containers + hit error meter.
            try
            {
                var uic = scrUIController.instance;
                if (uic != null)
                {
                    FadeCorner(uic.difficultyContainer, fade);
                    FadeCorner(uic.modifiersContainer, fade);
                }
            }
            catch { }
            try
            {
                var c = scrController.instance;
                if (c != null && c.errorMeter != null) FadeCorner(c.errorMeter, fadeMeter);
            }
            catch { }
        }

        private static void FadeCorner(Component c, bool fade)
        {
            if (c == null) return;
            var cg = c.GetComponent<CanvasGroup>();
            if (cg == null)
            {
                if (!fade) return;
                cg = c.gameObject.AddComponent<CanvasGroup>();
            }
            float a = fade ? 0f : 1f;
            if (cg.alpha != a) { cg.alpha = a; cg.blocksRaycasts = !fade; }
        }

        // ── Editor: selected tile angle readout ────────────────────────────
        // Small own-canvas text near the top of the editor showing the angle of the last
        // selected tile (angleLength → degrees; 180° = straight).

        private static GameObject _angleGo;
        private static TextMeshProUGUI _angleText;

        internal static void TickTileAngle()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            bool want = false;
            try
            {
                if (s != null && MainClass.EditorSuiteOn && s.EditorTileAngle)
                {
                    ed = scnEditor.instance;
                    want = ed != null && !ed.playMode
                        && ed.selectedFloors != null && ed.selectedFloors.Count > 0;
                }
            }
            catch { want = false; }

            if (!want)
            {
                if (_angleGo != null && _angleGo.activeSelf) _angleGo.SetActive(false);
                return;
            }
            if (_angleGo == null) BuildAngleDisplay();
            if (!_angleGo.activeSelf) _angleGo.SetActive(true);

            var fl = ed.selectedFloors[ed.selectedFloors.Count - 1];
            if (fl == null) return;
            float deg = (float)(fl.angleLength * Mathf.Rad2Deg);
            int count = ed.selectedFloors.Count;
            // Total swept angle over the selection — the number you actually want when checking
            // that a run adds up to a full turn. Summed here, not cached per floor: the selection
            // is small and this only runs when the readout's inputs changed.
            float sum = deg;
            if (count > 1)
            {
                sum = 0f;
                foreach (var f in ed.selectedFloors)
                    if (f != null) sum += (float)(f.angleLength * Mathf.Rad2Deg);
            }
            /* Effective bpm = the rate the tile is actually HIT at: its bpm after SetSpeed,
               times 180/angle (a 90° tile lands twice per beat, so it reads double). A midspin
               sweeps nothing, so it has no rate. */
            string eff = "";
            try
            {
                if (s.EditorEffectiveBpm && deg > 0.01f)
                {
                    double bpm = EditorEvents.SpeedBpmAt(fl.seqID) * 180.0 / deg;
                    if (bpm > 0.0 && bpm < 1e6) eff = $"  ·  {bpm:0.#} BPM";
                }
            }
            catch { }
            if (_angleText != null && (!Mathf.Approximately(deg, _lastAngleDeg)
                                       || count != _lastAngleCount
                                       || !Mathf.Approximately(sum, _lastAngleSum)
                                       || eff != _lastEff))
            {
                _lastAngleDeg = deg; _lastAngleCount = count; _lastAngleSum = sum; _lastEff = eff;
                _angleText.text = count > 1
                    ? $"Angle: {deg:0.##}°  ·  Σ {sum:0.##}°  ({count} tiles){eff}"
                    : $"Angle: {deg:0.##}°{eff}";
            }
        }

        private static float _lastAngleDeg = float.NaN;
        private static float _lastAngleSum = float.NaN;
        private static string _lastEff = null;
        private static int _lastAngleCount = -1;

        private static void BuildAngleDisplay()
        {
            _angleGo = new GameObject("SapphireTileAngle", typeof(RectTransform));
            Object.DontDestroyOnLoad(_angleGo);
            var canvas = _angleGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = _angleGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(_angleGo.transform, false);
            var rect = (RectTransform)txtGo.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.93f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(700f, 40f);
            _angleText = txtGo.AddComponent<TextMeshProUGUI>();
            _angleText.font = UI.Theme.TmpFont;
            _angleText.fontSize = 26;
            _angleText.color = Color.white;
            _angleText.alignment = TextAlignmentOptions.Center;
            _angleText.textWrappingMode = TextWrappingModes.NoWrap;
            _angleText.overflowMode = TextOverflowModes.Overflow;
            _angleText.raycastTarget = false;
            var sh = txtGo.AddComponent<TmpShadow>();
            sh.OffsetPx = new Vector2(2f, -2f);
            sh.Apply();
        }

        internal static void DisposeTileAngle()
        {
            if (_angleGo != null) Object.Destroy(_angleGo);
            _angleGo = null;
            _angleText = null;
        }
    }
}

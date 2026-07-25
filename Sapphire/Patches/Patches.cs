using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace Sapphire
{
    internal static class Patches
    {
        /* The editor's info splash (scnEditor.notificationText — "Level saved", tool feedback,
           etc.) SLIDES in from off-screen (DOAnchorPosX) near the top, where Sapphire's file
           menu bar now covers it. While the suite is active, re-home it to screen centre and
           replace the slide with a simple alpha fade in/out (no position tween). notificationText
           is public; notificationSeq (the game's tween) is private → reflected + killed so the
           two don't fight. Falls through to the vanilla animation on any failure or when off. */
        [HarmonyPatch(typeof(scnEditor), "ShowNotification")]
        private static class NotificationCenterFadePatch
        {
            private static System.Reflection.FieldInfo _seqFi;

            public static bool Prefix(scnEditor __instance, string text,
                System.Nullable<Color> textColor, float delayDuration)
            {
                try
                {
                    if (MainClass.Settings == null || !MainClass.EditorSuiteOn) return true;
                    var go = __instance.notificationText;
                    if (go == null) return true;
                    var rt = go.GetComponent<RectTransform>();
                    var tmp = go.GetComponent<TMPro.TMP_Text>();
                    if (rt == null || tmp == null) return true;

                    tmp.text = text;
                    tmp.color = textColor ?? Color.white;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero; // centre of screen

                    var cg = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
                    cg.alpha = 0f;

                    if (_seqFi == null)
                        _seqFi = typeof(scnEditor).GetField("notificationSeq",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var old = _seqFi != null ? _seqFi.GetValue(__instance) as Tween : null;
                    if (old != null && old.active) old.Kill();

                    var seq = DOTween.Sequence();
                    seq.SetUpdate(true); // unscaled — survives editor pause
                    seq.Append(DOTween.To(() => cg.alpha, a => cg.alpha = a, 1f, 0.18f));
                    seq.AppendInterval(Mathf.Max(0.4f, delayDuration));
                    seq.Append(DOTween.To(() => cg.alpha, a => cg.alpha = a, 0f, 0.35f));
                    if (_seqFi != null) _seqFi.SetValue(__instance, seq);
                    return false; // skip the vanilla slide
                }
                catch { return true; }
            }
        }

        /* The editor hardcodes Space as the autoplay-pause key inside scnEditor.Update's
           `RDC.auto && GetKeyDown(Space) && playMode` block. Swap the pushed constant 32
           for a call into Tweaks.AutoPauseKeyCode so the key is rebindable / disableable.
           Fails safe: if the pattern isn't found after a game update, nothing is replaced
           and vanilla Space still pauses. */
        [HarmonyPatch(typeof(scnEditor), "Update")]
        private static class EditorAutoPauseKeyPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                try
                {
                    var getKeyDown = AccessTools.Method(typeof(Input), "GetKeyDown", new[] { typeof(KeyCode) });
                    var repl = AccessTools.Method(typeof(Tweaks), nameof(Tweaks.AutoPauseKeyCode));
                    for (int i = 0; i < codes.Count - 1; i++)
                    {
                        bool loads32 = (codes[i].opcode == OpCodes.Ldc_I4_S || codes[i].opcode == OpCodes.Ldc_I4)
                            && codes[i].operand != null && Convert.ToInt32(codes[i].operand) == 32;
                        if (loads32 && codes[i + 1].Calls(getKeyDown))
                        {
                            codes[i].opcode = OpCodes.Call;   // preserves any labels on the instruction
                            codes[i].operand = repl;
                            break;
                        }
                    }
                }
                catch { }
                return codes;
            }
        }

        /* scnEditor.HandleMouseActions sets `freeAngleMode = Input.GetMouseButton(1) &&
           singleSelection && !oldLevel`, then (while true) drags the selected tile to a
           free angle and places it on release. We re-gate that one GetMouseButton(1) to
           EditorToolbar.FreeAngleActive() so free-angle is driven by right-Alt (or the
           toolbar tool) instead of the right mouse button — which frees plain right-click
           for the Sapphire tile menu. The predicate falls back to the real right button
           when the feature is off, so toggling it restores vanilla behavior. Fails safe:
           if the `ldc.i4.1 → GetMouseButton` feeding `freeAngleMode` isn't found, nothing
           changes and right-drag free-angle stays vanilla. */
        [HarmonyPatch(typeof(scnEditor), "HandleMouseActions")]
        private static class FreeAngleRebindPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                try
                {
                    var getMouseButton = AccessTools.Method(typeof(Input), "GetMouseButton", new[] { typeof(int) });
                    var repl = AccessTools.Method(typeof(EditorToolbar), nameof(EditorToolbar.FreeAngleActive));
                    for (int i = 0; i < codes.Count - 1; i++)
                    {
                        if (codes[i].opcode != OpCodes.Ldc_I4_1 || !codes[i + 1].Calls(getMouseButton)) continue;
                        // Only the GetMouseButton(1) that feeds `freeAngleMode`.
                        bool feedsFreeAngle = false;
                        for (int j = i + 2; j < codes.Count && j < i + 6; j++)
                            if (codes[j].opcode == OpCodes.Stfld && codes[j].operand is System.Reflection.FieldInfo fi
                                && fi.Name == "freeAngleMode") { feedsFreeAngle = true; break; }
                        if (!feedsFreeAngle) continue;
                        codes[i].opcode = OpCodes.Nop; codes[i].operand = null; // drop the button-index arg
                        codes[i + 1].opcode = OpCodes.Call; codes[i + 1].operand = repl;
                        break;
                    }
                }
                catch { }
                return codes;
            }
        }

        /* The editor zooms the camera on ANY wheel input (scnEditor.Update → ZoomCamera)
           without an over-UI check, so panning the Sapphire event timeline would zoom the
           scene underneath. Swallow the zoom while the strip is hovered. */
        // Vanilla binds number keys to "add the nth event of the current category"
        // (AddNumberedEventEditorAction → AddEventAtSelected). With the Sapphire dock on,
        // digits SELECT the event as a tool instead — swallow the vanilla add.
        [HarmonyPatch(typeof(ADOFAI.Editor.Actions.AddNumberedEventEditorAction), "Execute")]
        private static class NumberedEventAddGuardPatch
        {
            private static bool Prefix()
            {
                try
                {
                    var s = MainClass.Settings;
                    return !(s != null && MainClass.EditorSuiteOn && s.EditorEventDock);
                }
                catch { return true; }
            }
        }

        // The editor's A shortcut toggles autoplay — while A is a PAN key (no selection),
        // swallow the toggle entirely (its lambda fires on key-repeat, so post-hoc restores lose).
        [HarmonyPatch(typeof(scnEditor), "ToggleAuto")]
        private static class PanAutoGuardPatch
        {
            private static bool Prefix() => !Tweaks.SuppressAutoToggle;
        }

        // SetupConductorWithLevelData folds scnEditor.playbackSpeed into song.pitch and the
        // hitsound schedule at play start — but the editor rewrites playbackSpeed from its own
        // control first, stomping per-frame writes. Impose the practice speed HERE, the moment
        // it's consumed, so song + hitsounds both follow it.
        [HarmonyPatch(typeof(scrConductor), "SetupConductorWithLevelData")]
        private static class PracticeSpeedPatch
        {
            private static void Prefix() => EditorPitch.ImposePlaybackSpeed();
        }

        [HarmonyPatch(typeof(scnEditor), "ZoomCamera")]
        private static class EditorZoomBlockPatch
        {
            public static bool Prefix() => !EditorEvents.TimelineHovered && !EditorHelp.IsOpen && !EditorChrome.DockHovered
                && !EditorGraph.PanelHovered && !EditorFilterPicker.IsOpen && !EditorEasePicker.IsOpen && !EditorBezier.IsOpen
                && !EditorEventSelector.Hovered && !EditorEventPanel.Hovered && !EditorLevelMenu.Hovered;
        }

        /* Editor Mode hides the autoplay status label. Disabling the Text directly is the
           safe way — Bismuth's approach of flipping RDC.auto around the update made
           autoplay turn itself off in the editor. Restores itself: with Editor Mode off
           the component re-enables its own Text next frame. */
        [HarmonyPatch(typeof(scrShowIfDebug), "Update")]
        private static class AutoplayLabelHidePatch
        {
            public static void Postfix(scrShowIfDebug __instance)
            {
                try
                {
                    var s = MainClass.Settings;
                    if (s == null || !s.EditorModeActive || !MainClass.EditorSuiteOn) return;
                    var t = __instance.GetComponent<UnityEngine.UI.Text>();
                    if (t != null) t.enabled = false;
                }
                catch { }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Sapphire
{
    internal static class Patches
    {
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

        /* The editor zooms the camera on ANY wheel input (scnEditor.Update → ZoomCamera)
           without an over-UI check, so panning the Sapphire event timeline would zoom the
           scene underneath. Swallow the zoom while the strip is hovered. */
        [HarmonyPatch(typeof(scnEditor), "ZoomCamera")]
        private static class EditorZoomBlockPatch
        {
            public static bool Prefix() => !EditorEvents.TimelineHovered;
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
                    if (s == null || !s.EditorModeActive) return;
                    var t = __instance.GetComponent<UnityEngine.UI.Text>();
                    if (t != null) t.enabled = false;
                }
                catch { }
            }
        }
    }
}

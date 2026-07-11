using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire
{
    /* VFX preview / hide-everything mode: fades EVERY UI canvas — all Sapphire canvases and the
       game's editor canvas ("levelEditorScene") — for a clean look at the level itself. Entered
       from the toolbar (crossed-out eye); the ONLY exit is ESC (or leaving the editor), since the
       toolbar hides with everything else. Root-level CanvasGroups at alpha 0; module Ticks keep
       SetActive-ing their canvases but alpha wins visually, and a periodic re-sweep catches
       canvases created while hidden. */
    internal static class EditorVfxPreview
    {
        private static bool _on;
        private static readonly List<CanvasGroup> _hidden = new List<CanvasGroup>();
        private static int _cooldown;

        internal static bool IsOn => _on;

        internal static void Toggle()
        {
            if (_on) Exit();
            else { _on = true; _cooldown = 0; }
        }

        internal static void Tick()
        {
            if (!_on) return;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null || !MainClass.EditorSuiteOn) { Exit(); return; }
            // Stay hidden through play-testing (user: hide everything when Play is pressed too).
            if (Input.GetKeyDown(KeyCode.Escape)) { Exit(); return; }
            if (--_cooldown > 0) return;
            _cooldown = 15;
            Sweep();
        }

        internal static void Dispose() => Exit();

        private static void Sweep()
        {
            foreach (var canvas in Object.FindObjectsOfType<Canvas>())
            {
                if (canvas == null || canvas.rootCanvas != canvas) continue;
                string n = canvas.name;
                if (!n.StartsWith("Sapphire") && n != "levelEditorScene"
                    && n != "UICanvas" && n != "UI Root" && n != "Canvas" && n != "PauseMenu(Clone)"
                    && canvas.GetComponentInChildren<scrUIController>(true) == null) continue;
                var go = canvas.gameObject;
                var cg = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
                if (cg.alpha != 0f || !_hidden.Contains(cg))
                {
                    cg.alpha = 0f;
                    cg.blocksRaycasts = false;
                    if (!_hidden.Contains(cg)) _hidden.Add(cg);
                }
            }
        }

        private static void Exit()
        {
            _on = false;
            foreach (var cg in _hidden)
            {
                if (cg == null) continue;
                try { cg.alpha = 1f; cg.blocksRaycasts = true; } catch { }
            }
            _hidden.Clear();
        }
    }
}

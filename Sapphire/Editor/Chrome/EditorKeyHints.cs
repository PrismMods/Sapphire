using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Bottom-right key hints — a plain TEXT OVERLAY, not a window: no plate, no header, no
       close button, no drag, and nothing raycastable, so it can never eat a click over the
       level. One TMP for the whole list rather than two labels per row.

       Bottom-anchored above the event timeline (EditorEvents.BottomStripTop), so the list
       grows UPWARD and is content-height-aware for free — no height math. Shows only the keys
       live in the CURRENT context; the table is hand-maintained here because EditorHelp's
       per-topic "Keys" sections are prose and would break on the first rewrite. */
    internal static class EditorKeyHints
    {
        // context bits
        private const int Always = 0, NoSel = 1, Sel = 2, ToolNum = 4, Quick = 8;
        // Exactly one tile selected AND tile actions enabled — TickFreeAngle's real Alt gate
        // (plain Sel overclaimed for 2+ selected tiles or Sapphire-tools-off).
        private const int FreeAngle = 16;

        /* A row's key column is either a literal (game keys we don't own) or one/two Bind ids,
           rendered live so a rebind shows up here instead of leaving the card lying. */
        private struct Hint
        {
            public readonly int Ctx, Not; public readonly string Keys, What;
            public readonly Bind? Key, Key2;
            public Hint(int ctx, string keys, string what, int not = 0)
            { Ctx = ctx; Not = not; Keys = keys; What = what; Key = null; Key2 = null; }
            public Hint(int ctx, Bind key, string what, int not = 0, Bind? key2 = null)
            { Ctx = ctx; Not = not; Keys = null; What = what; Key = key; Key2 = key2; }
        }

        private static string KeyColumn(Hint h)
        {
            if (!h.Key.HasValue) return h.Keys;
            string a = Keybinds.Label(h.Key.Value);
            return h.Key2.HasValue ? a + " / " + Keybinds.Label(h.Key2.Value) : a;
        }

        private static readonly Hint[] Table =
        {
            new Hint(Always, "Ctrl+E",  "Sapphire settings"),
            new Hint(Always, "ESC",     "Close panel / disarm tool"),
            // TickToolHotkeys is the only one of these three gated on selection; TickToolSwap
            // (the tool-swap + quick-chart binds) has no selection check at all — tagging it
            // NoSel hid live hotkeys the instant a tile was selected, the commonest edit state.
            new Hint(NoSel,  "1-0",     "Select tool", not: ToolNum),
            // Tweaks.PanEligible bails once anything is selected, so this is NoSel too.
            new Hint(NoSel,  "WASD",     "Pan camera"),
            new Hint(Always, Bind.QuickChart,   "Quick chart mode"),
            new Hint(Always, Bind.ToolPrev,     "Previous tool"),
            new Hint(Always, Bind.ToolSlot,     "Saved tool slot"),
            new Hint(Always, Bind.ToolSlotSave, "Save current tool to slot"),
            new Hint(Always, Bind.HzTool,       "Hz tool"),
            new Hint(FreeAngle, "Alt",  "Hold: free-angle aim"),
            new Hint(Sel,    "Ctrl+click",  "Add/remove event row"),
            new Hint(Sel,    "Shift+click", "Select event range"),
            new Hint(ToolNum,"Digits",  "Set key count"),
            new Hint(Quick,  Bind.QcSwirl,     "Swirl on/off"),
            new Hint(Quick,  Bind.QcSetSpeed,  "Set speed"),
            new Hint(Quick,  Bind.QcSpeedDown, "Halve / double speed", key2: Bind.QcSpeedUp),
            new Hint(Quick,  Bind.QcPause,     "Pause event"),
            new Hint(Quick,  Bind.QcLocate,    "Tile location event"),
            new Hint(Quick,  Bind.QcAnglePad,  "Angle pad"),
        };

        private const float BoxW = 260f, Margin = 12f, KeyCol = 74f, FontSize = 11f;
        private static GameObject _canvasGo, _textGo;
        private static TextMeshProUGUI _tmp;
        private static int _sig = int.MinValue;
        private static float _lastBottom = float.NaN;
        private static readonly StringBuilder _sb = new StringBuilder();

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool want = ed != null && !ed.playMode && MainClass.EditorSuiteOn
                        && s != null && s.EditorKeyHints;
            // The one surface that outranks the hints: the difficulty dropdown shares this corner.
            if (want) { try { want = !EditorEvents.DiffMenuOpen; } catch { } }
            if (!want)
            {
                if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
                return;
            }

            EnsureBuilt();
            if (_canvasGo == null) return;
            if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);

            // Fold the keybind revision into the signature: a rebind changes the key column
            // without changing the context, and the card would otherwise keep the stale text.
            int ctx = Context(ed, s);
            int sig = ctx | (Keybinds.Revision << 8);
            if (sig != _sig) { _sig = sig; Compose(ctx); }
            Place();
        }

        internal static void Dispose()
        {
            if (_canvasGo != null) Object.Destroy(_canvasGo);
            _canvasGo = null; _textGo = null; _tmp = null;
            _sig = int.MinValue; _lastBottom = float.NaN;
        }

        private static int Context(scnEditor ed, Settings s)
        {
            int ctx = 0;
            int sel = 0;
            try { sel = ed.selectedFloors != null ? ed.selectedFloors.Count : 0; } catch { }
            ctx |= sel == 0 ? NoSel : Sel;
            try { if (EditorToolbar.PseudoToolOn) ctx |= ToolNum; } catch { }
            try { if (s.FeatQuickChart) ctx |= Quick; } catch { }
            try { if (s.EditorTileActions && ed.SelectionIsSingle()) ctx |= FreeAngle; } catch { }
            return ctx;
        }

        private static void EnsureBuilt()
        {
            if (_canvasGo != null) return;
            _canvasGo = new GameObject("SapphireKeyHints", typeof(RectTransform));
            Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above every Sapphire surface (help is 949, dock chrome 948, popups 945-947):
            // the hints are a reference you look at WHILE something else is open, so they must
            // never be buried. Nothing here raycasts, so sitting on top costs no interaction.
            canvas.sortingOrder = 960;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            // No GraphicRaycaster: nothing here is interactive, so the EventSystem should never
            // walk this canvas.

            _textGo = new GameObject("Hints", typeof(RectTransform));
            _textGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_textGo.transform;
            // Bottom-right anchor + pivot: text is bottom-aligned, so the list grows upward and
            // the rect height never has to be recomputed.
            r.anchorMin = r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(1f, 0f);
            r.sizeDelta = new Vector2(BoxW, 0f);
            _tmp = UIBuilder.Tmp(_textGo, "", FontSize, TextAnchor.LowerLeft, Theme.Text);
            _tmp.textWrappingMode = TextWrappingModes.NoWrap;
            _tmp.lineSpacing = 6f;
            // No background plate, so the text has to survive a bright level on its own.
            TmpShadow.Attach(_textGo, new Color(0f, 0f, 0f, 0.85f), new Vector2(1.5f, -1.5f));
        }

        private static void Compose(int ctx)
        {
            if (_tmp == null) return;
            string keyHex = ColorUtility.ToHtmlStringRGB(Theme.Text);
            string descHex = ColorUtility.ToHtmlStringRGB(Theme.TextMuted);
            _sb.Length = 0;
            foreach (var h in Table)
            {
                // Any-bit match on Ctx, but Not is a veto: a row whose keys are being consumed by
                // an active tool must not claim it still does its normal job.
                if (!(h.Ctx == Always || (ctx & h.Ctx) != 0) || (h.Not != 0 && (ctx & h.Not) != 0)) continue;
                if (_sb.Length > 0) _sb.Append('\n');
                _sb.Append("<color=#").Append(keyHex).Append('>').Append(KeyColumn(h)).Append("</color>")
                   .Append("<pos=").Append(KeyCol.ToString("0")).Append('>')
                   .Append("<color=#").Append(descHex).Append('>').Append(Loc.T(h.What)).Append("</color>");
            }
            _tmp.text = _sb.ToString();
            /* Right-flush with the mode chips below. The rect is right-anchored but the text is
               left-aligned inside it (the <pos> key column needs a left origin), so a fixed BoxW
               left a ragged gap on the right — shrink the rect to the text instead. Clamped: a
               bogus preferred width must never push the list off-screen. */
            _tmp.ForceMeshUpdate();
            ((RectTransform)_textGo.transform).sizeDelta =
                new Vector2(Mathf.Clamp(_tmp.preferredWidth, KeyCol + 80f, BoxW), 0f);
        }

        // Sits just above the timeline strip — and above the mode chips (EDITOR / NO FAIL /
        // AUTO), which share this corner and used to sit under the hint text. Only writes when
        // the strip actually moved — an unconditional transform write per frame re-batches the
        // canvas.
        private static void Place()
        {
            float below = 0f;
            try { below = EditorEvents.BottomChromeTop; } catch { }
            float bottom = below > 0f ? below : 12f;
            if (bottom == _lastBottom) return;
            _lastBottom = bottom;
            ((RectTransform)_textGo.transform).anchoredPosition = new Vector2(-Margin, bottom);
        }
    }
}

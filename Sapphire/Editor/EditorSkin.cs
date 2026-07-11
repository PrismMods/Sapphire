using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire
{
    /* Dark reskin for the game's own editor UI (Tweaks tab → Editor), applied at runtime
       and fully reversible. Rather than chasing every panel by name, it sweeps the editor's
       root canvas on a ~half-second cadence (panels instantiate lazily) and restyles what
       it finds:

       - Selectables (buttons/toggles/inputs) whose target graphic is bright greyscale get
         a dark base via the ColorBlock, so uGUI's own hover/press tinting keeps working.
         Child images (icon glyphs) are untouched — white glyphs read fine on dark.
       - Near-black text flips to light; colored text (links, accents) is left alone.

       Everything touched is recorded so toggling the setting off restores the originals.
       Sliders are skipped (their fills carry meaning), as is anything already dark. */
    internal static class EditorSkin
    {
        private static readonly Color ButtonBg = new Color(0.13f, 0.13f, 0.16f, 1f);
        private static readonly Color SelectedBg = new Color(0.3f, 0.3f, 0.37f, 1f);
        private static readonly Color TextLight = new Color(0.93f, 0.93f, 0.94f, 1f);
        private static readonly Color TextDim = new Color(0.62f, 0.62f, 0.66f, 1f);
        private static readonly Color ChromeGrey = new Color(0.5f, 0.5f, 0.55f, 1f);

        private static readonly Dictionary<Graphic, Color> _origColors = new Dictionary<Graphic, Color>();
        private static readonly Dictionary<Selectable, ColorBlock> _origBlocks = new Dictionary<Selectable, ColorBlock>();
        private static readonly Dictionary<Image, Sprite> _origSprites = new Dictionary<Image, Sprite>();
        // Images whose color the GAME rewrites at runtime (tab selection = direct .color
        // writes). A per-frame guard re-maps bright → dark, keeping the game's
        // selected(white)/unselected(grey) distinction as two dark shades.
        private static readonly List<Image> _guarded = new List<Image>();
        private static readonly HashSet<int> _seen = new HashSet<int>();
        private static readonly HashSet<int> _popupSeen = new HashSet<int>();
        private static int _cooldown;
        private static bool _applied;

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            bool want = false;
            try { ed = scnEditor.instance; want = s != null && MainClass.EditorSuiteOn && s.EditorDarkTheme && ed != null; }
            catch { }
            if (!want)
            {
                if (_applied) Restore();
                return;
            }
            GuardTick(); // cheap per-frame re-assert against the game's direct color writes
            if (--_cooldown > 0) return;
            _cooldown = 30;
            try { Apply(ed); } catch (System.Exception e) { SapphireLog.Debug("[skin] " + e.Message); }
        }

        internal static void Dispose() => Restore();

        // Threshold 0.5: the inspector tabs rest at mid-grey, not white.
        private static bool BrightGrey(Color c) =>
            c.a > 0.4f && c.r > 0.5f && c.g > 0.5f && c.b > 0.5f
            && Mathf.Abs(c.r - c.g) < 0.1f && Mathf.Abs(c.g - c.b) < 0.1f;

        private static bool VeryBright(Color c) =>
            c.a > 0.4f && c.r > 0.7f && c.g > 0.7f && c.b > 0.7f
            && Mathf.Abs(c.r - c.g) < 0.1f && Mathf.Abs(c.g - c.b) < 0.1f;

        private static bool NearBlack(Color c) =>
            c.a > 0.2f && c.r < 0.35f && c.g < 0.35f && c.b < 0.35f;

        // Mid-grey greyscale text (dropdown captions rest ~0.4-0.55): readable on the
        // game's white boxes, invisible once we darken them. Colored text stays put.
        private static bool GreyDark(Color c) =>
            c.a > 0.2f && c.r < 0.6f && c.g < 0.6f && c.b < 0.6f
            && Mathf.Abs(c.r - c.g) < 0.12f && Mathf.Abs(c.g - c.b) < 0.12f;

        private static void Apply(scnEditor ed)
        {
            // The editor's UI spans several root canvases (the inspector tabs live apart
            // from the file menu) — collect the roots of every panel we know and sweep each.
            var roots = new HashSet<Transform>();
            AddRoot(roots, ed.fileActionsPanel != null ? ed.fileActionsPanel.transform : null);
            AddRoot(roots, ed.inspectorTabs != null ? ed.inspectorTabs.transform : null);
            AddRoot(roots, ed.inspectorPanels != null ? ed.inspectorPanels.transform : null);
            AddRoot(roots, ed.settingsPanelsContainer);
            AddRoot(roots, ed.settingsPanel != null ? ed.settingsPanel.transform : null);
            AddRoot(roots, ed.levelEventsPanel != null ? ed.levelEventsPanel.transform : null);
            AddRoot(roots, ed.prefsContainer != null ? ed.prefsContainer.transform : null);
            AddRoot(roots, ed.particleEditorContainer != null ? ed.particleEditorContainer.transform : null);
            if (roots.Count == 0) return;
            _applied = true;
            foreach (var r in roots) ApplyRoot(r);
            try { if (ed.popupWindow != null) ApplyPopupButtons(ed.popupWindow.transform); } catch { }
        }

        /* Popup action buttons (save = pastel green, discard = pastel pink) are SATURATED,
           so the greyscale-only pass skips their fills while the text pass lightens their
           labels — light-on-pastel, unreadable. The popup window holds no color-swatch
           content, so inside it bright saturated fills rebase to a dark hue-preserving
           tone: semantics stay, labels read. */
        private static void ApplyPopupButtons(Transform popupRoot)
        {
            foreach (var sel in popupRoot.GetComponentsInChildren<Selectable>(true))
            {
                if (sel == null || sel is Slider || sel is Scrollbar) continue;
                if (!_popupSeen.Add(sel.GetInstanceID())) continue;
                if (sel.transition != Selectable.Transition.ColorTint) continue;
                var g = sel.targetGraphic;
                if (g == null) continue;
                var block = sel.colors;
                var c = g.color * block.normalColor;
                float h, s, v;
                Color.RGBToHSV(c, out h, out s, out v);
                if (s < 0.15f || v < 0.5f) continue; // grey or already dark: main pass territory
                _origBlocks[sel] = block;
                if (!_origColors.ContainsKey(g)) _origColors[g] = g.color;
                g.color = new Color(1f, 1f, 1f, g.color.a);
                float s2 = Mathf.Min(1f, s * 0.85f);
                block.normalColor = Color.HSVToRGB(h, s2, 0.30f);
                block.highlightedColor = Color.HSVToRGB(h, s2, 0.40f);
                block.pressedColor = Color.HSVToRGB(h, s2, 0.48f);
                block.selectedColor = block.normalColor;
                var dis = Color.HSVToRGB(h, s2, 0.30f);
                dis.a = 0.5f;
                block.disabledColor = dis;
                sel.colors = block;
                Square(g);
            }
        }

        private static void AddRoot(HashSet<Transform> roots, Transform t)
        {
            if (t == null) return;
            var canvas = t.GetComponentInParent<Canvas>(true);
            if (canvas != null) roots.Add(canvas.rootCanvas.transform);
        }

        private static void ApplyRoot(Transform root)
        {
            var selectables = root.GetComponentsInChildren<Selectable>(true);
            for (int i = 0; i < selectables.Length; i++)
            {
                var sel = selectables[i];
                if (sel == null || sel is Slider || sel is Scrollbar) continue;
                if (!_seen.Add(sel.GetInstanceID())) continue;
                var g = sel.targetGraphic;
                if (g == null) continue;

                if (sel.transition == Selectable.Transition.ColorTint)
                {
                    var block = sel.colors;
                    // Final tint = graphic.color × state color; only rebase buttons whose
                    // resting look is a bright grey pill.
                    if (!BrightGrey(g.color * block.normalColor)) continue;
                    // Glyph-only buttons (the white ICON sprite is the target graphic, no other
                    // visuals): rebasing paints the glyph dark → invisible icons (decoration
                    // add-row, panel close X). Leave them native.
                    var gImg = g as Image;
                    if (gImg != null && gImg.sprite != null && gImg.type == Image.Type.Simple
                        && sel.GetComponentsInChildren<Graphic>(true).Length <= 1) continue;
                    _origBlocks[sel] = block;
                    if (!_origColors.ContainsKey(g)) _origColors[g] = g.color;
                    g.color = Color.white;
                    block.normalColor = ButtonBg;
                    block.highlightedColor = new Color(0.22f, 0.22f, 0.27f, 1f);
                    block.pressedColor = new Color(0.3f, 0.3f, 0.36f, 1f);
                    block.selectedColor = ButtonBg;
                    block.disabledColor = new Color(0.13f, 0.13f, 0.16f, 0.5f);
                    sel.colors = block;
                    Square(g);
                }
                else if (BrightGrey(g.color))
                {
                    if (!_origColors.ContainsKey(g)) _origColors[g] = g.color;
                    var gi = g as Image;
                    if (gi != null) _guarded.Add(gi);
                    g.color = MapDark(g.color);
                    Square(g);
                }
            }

            // Chrome pass. Sliced/tiled bright images are frames and outlines → faint
            // grey. Simple-type grey PLATES (a child graphic marks them as backgrounds —
            // tab buttons behind their icons; leaf Simple images are the icons themselves
            // and stay untouched) → dark.
            var images = root.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                var img = images[i];
                if (img == null || _origColors.ContainsKey(img)) continue;
                bool sliced = img.type == Image.Type.Sliced || img.type == Image.Type.Tiled;
                if (sliced)
                {
                    if (!_seen.Add(img.GetInstanceID())) continue;
                    if (!VeryBright(img.color)) continue;
                    _origColors[img] = img.color;
                    img.color = new Color(ChromeGrey.r, ChromeGrey.g, ChromeGrey.b, img.color.a * 0.45f);
                }
                else if (img.type == Image.Type.Simple && BrightGrey(img.color)
                    && img.GetComponentsInChildren<Graphic>(true).Length > 1
                    && img.rectTransform.rect.width >= 36f && img.rectTransform.rect.height >= 36f)
                {
                    if (!_seen.Add(img.GetInstanceID())) continue;
                    // Color-picker internals (the SV gradient square carries a white tint
                    // and a child handle — a false "plate") are content, not chrome.
                    if (img.GetComponentInParent<CUIColorPicker>(true) != null) continue;
                    _origColors[img] = img.color;
                    _guarded.Add(img);
                    img.color = MapDark(img.color);
                    Square(img);
                }
            }

            // Near-black body text → light; mid-grey secondary text (dropdown captions,
            // hints) → dimmer light, keeping the original hierarchy.
            var texts = root.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t == null || !_seen.Add(t.GetInstanceID())) continue;
                var mapped = MapTextColor(t.color);
                if (!mapped.HasValue) continue;
                _origColors[t] = t.color;
                t.color = mapped.Value;
                _guardedTexts.Add(t);
            }
            var tmps = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < tmps.Length; i++)
            {
                var t = tmps[i];
                if (t == null || !_seen.Add(t.GetInstanceID())) continue;
                var mapped = MapTextColor(t.color);
                if (!mapped.HasValue) continue;
                _origColors[t] = t.color;
                t.color = mapped.Value;
                _guardedTexts.Add(t);
            }
        }

        private static Color? MapTextColor(Color c)
        {
            if (NearBlack(c)) return new Color(TextLight.r, TextLight.g, TextLight.b, c.a);
            if (GreyDark(c)) return new Color(TextDim.r, TextDim.g, TextDim.b, c.a);
            return null;
        }

        // Bright selected-white → lighter dark; mid-grey unselected → base dark.
        private static Color MapDark(Color c) =>
            VeryBright(c)
                ? new Color(SelectedBg.r, SelectedBg.g, SelectedBg.b, c.a)
                : new Color(ButtonBg.r, ButtonBg.g, ButtonBg.b, c.a);

        private static void GuardTick()
        {
            for (int i = _guarded.Count - 1; i >= 0; i--)
            {
                var img = _guarded[i];
                if (img == null) { _guarded.RemoveAt(i); continue; }
                if (BrightGrey(img.color)) img.color = MapDark(img.color);
            }
            // The game rewrites label colors on state changes (selected toggle text → black on
            // our dark plates); hold every swept text at its mapped color.
            for (int i = _guardedTexts.Count - 1; i >= 0; i--)
            {
                var t = _guardedTexts[i];
                if (t == null) { _guardedTexts.RemoveAt(i); continue; }
                var mapped = MapTextColor(t.color);
                if (mapped.HasValue) t.color = mapped.Value;
            }
        }

        private static readonly List<Graphic> _guardedTexts = new List<Graphic>();

        // Drop the rounded sprite for a plain square quad (the sprite is remembered for
        // Restore). Only used on fills we've already rebased — never on border sprites,
        // where a null sprite would turn the frame into a solid block.
        private static void Square(Graphic g)
        {
            var img = g as Image;
            if (img == null || img.sprite == null) return;
            if (!_origSprites.ContainsKey(img)) _origSprites[img] = img.sprite;
            img.sprite = null;
        }

        private static void Restore()
        {
            _guardedTexts.Clear();
            foreach (var kv in _origColors)
                if (kv.Key != null) kv.Key.color = kv.Value;
            foreach (var kv in _origBlocks)
                if (kv.Key != null) kv.Key.colors = kv.Value;
            foreach (var kv in _origSprites)
                if (kv.Key != null) kv.Key.sprite = kv.Value;
            _origColors.Clear();
            _origBlocks.Clear();
            _origSprites.Clear();
            _guarded.Clear();
            _seen.Clear();
            _popupSeen.Clear();
            _applied = false;
            _cooldown = 0;
        }
    }
}

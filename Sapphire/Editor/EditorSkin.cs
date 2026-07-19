using System.Collections.Generic;
using HarmonyLib;
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
       Sliders are skipped (their fills carry meaning), as is anything already dark.

       The game rewrites colors directly at runtime (tab selection, dropdown captions…).
       Those writes are intercepted AT THE SETTER (Harmony prefixes on Graphic.color /
       TMP_Text.color below) and remapped for graphics the skin owns — event-driven, no
       per-frame polling. Dropdown lists/captions are styled from TMP_Dropdown.Show /
       RefreshShownValue postfixes for the same reason. */
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
        // Graphics the skin OWNS: the setter interception remaps the game's direct color
        // writes on exactly these (bright → dark for plates, dark → light for labels),
        // keeping the game's selected/unselected distinction as two themed shades.
        private static readonly HashSet<int> _ownedImages = new HashSet<int>();
        private static readonly HashSet<int> _ownedTexts = new HashSet<int>();
        private static readonly HashSet<int> _seen = new HashSet<int>();
        private static readonly HashSet<int> _popupSeen = new HashSet<int>();
        private static int _cooldown;
        private static bool _applied;
        /* Read by the two global color-setter prefixes as their FIRST instruction. Those
           setters fire on every Graphic/TMP_Text color write in the WHOLE game, so the
           fast path (theme not applied → all of menus + gameplay + editor-with-theme-off)
           must be a single static-bool read: no try/catch frame, no method call. Mirrors
           _applied but lives here as a plain field so the JIT emits a bare ldsfld. */
        internal static bool InterceptActive;

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
            if (--_cooldown > 0) return;
            _cooldown = 30;
            try
            {
                // Full sweeps are expensive (whole-tree GetComponentsInChildren × several
                // passes) — only re-sweep when the UI actually grew/changed shape.
                int sig = HierarchySig(ed);
                if (sig != _lastHierarchySig)
                {
                    _lastHierarchySig = sig;
                    Apply(ed);
                }
            }
            catch (System.Exception e) { SapphireLog.Debug("[skin] " + e.Message); }
        }

        private static int _lastHierarchySig;

        // O(roots): hierarchyCount is maintained by Unity, no traversal happens here.
        private static int HierarchySig(scnEditor ed)
        {
            int sig = 17;
            void Add(Transform t)
            {
                if (t == null) return;
                var c = t.GetComponentInParent<Canvas>(true);
                if (c != null) sig = sig * 31 + c.rootCanvas.transform.hierarchyCount;
            }
            try
            {
                Add(ed.fileActionsPanel != null ? ed.fileActionsPanel.transform : null);
                Add(ed.inspectorTabs != null ? ed.inspectorTabs.transform : null);
                Add(ed.settingsPanel != null ? ed.settingsPanel.transform : null);
                Add(ed.levelEventsPanel != null ? ed.levelEventsPanel.transform : null);
                Add(ed.prefsContainer != null ? ed.prefsContainer.transform : null);
            }
            catch { }
            return sig;
        }

        /* The game's dropdown items go through localizedValue, which wraps values in
           <color=#…> rich-text tags — a dark tag color stays dark no matter what TMP.color
           says, so the caption reads as an empty box on the dark theme. Strip the tags and
           force a readable color. Called from the RefreshShownValue/Show postfixes (the
           exact moments the game writes these), not per frame. */
        private static readonly System.Text.RegularExpressions.Regex ColorTags =
            new System.Text.RegularExpressions.Regex("</?color[^>]*>",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static void FixDdText(TMP_Text t)
        {
            if (t == null) return;
            var s = t.text;
            if (!string.IsNullOrEmpty(s) && s.IndexOf("<color") >= 0) // game emits lowercase tags
                t.text = ColorTags.Replace(s, "");
            if (!(t.color.r > 0.7f && t.color.g > 0.7f && t.color.b > 0.7f))
                t.color = new Color(TextLight.r, TextLight.g, TextLight.b, t.color.a);
        }

        /* The inspector's dropdowns are the game's OWN control (TweakableDropdown): a
           searchable TMP_InputField caption + a ScrollRect list. It paints items from its
           public state-color fields, so rebasing THOSE keeps the game's hover/select logic
           correct with zero polling; the list chrome is forced opaque dark (the generic
           frame pass left it a translucent ghost = "no background"); the caption is fixed
           where the game writes it (UpdateInputFieldText postfix — localizedValue wraps it
           in <color=#…> tags that TMP.color can't override). */
        private static readonly Dictionary<TweakableDropdown, Color[]> _origDdColors =
            new Dictionary<TweakableDropdown, Color[]>();

        private static void ApplyTweakableDropdowns(Transform root)
        {
            foreach (var dd in root.GetComponentsInChildren<TweakableDropdown>(true))
            {
                if (dd == null || _origDdColors.ContainsKey(dd)) continue;
                _origDdColors[dd] = new[] { dd.normalItemBGColor, dd.hoveredItemBGColor, dd.selectedItemBGColor };
                dd.normalItemBGColor = new Color(0.11f, 0.11f, 0.14f, 1f);
                dd.hoveredItemBGColor = new Color(0.24f, 0.24f, 0.3f, 1f);
                dd.selectedItemBGColor = new Color(0.2f, 0.24f, 0.36f, 1f);
                try
                {
                    if (dd.dropdownScroll != null)
                        foreach (var img in dd.dropdownScroll.GetComponentsInChildren<Image>(true))
                        {
                            if (img.GetComponentInParent<TweakableDropdownItem>(true) != null) continue; // items = state colors
                            if (!_origColors.ContainsKey(img)) _origColors[img] = img.color;
                            img.color = new Color(0.09f, 0.09f, 0.11f, 1f); // opaque: must read over anything
                        }
                }
                catch { }
                try { if (dd.inputField != null) FixDdText(dd.inputField.textComponent); } catch { }
            }
        }

        // UpdateInputFieldText postfix: the game just rewrote the searchable caption
        internal static void OnTweakableCaption(TweakableDropdown dd)
        {
            if (!_applied || dd == null || dd.inputField == null) return;
            var f = dd.inputField;
            var s = f.text;
            if (!string.IsNullOrEmpty(s) && s.IndexOf("<color") >= 0)
                f.SetTextWithoutNotify(ColorTags.Replace(s, "")); // WithoutNotify: don't trigger its search
            FixDdText(f.textComponent);
        }

        // ShowList postfix: items spawn game-styled — labels may carry tag colors
        internal static void OnTweakableShow(TweakableDropdown dd)
        {
            if (!_applied || dd == null || dd.dropdownScroll == null) return;
            foreach (var t in dd.dropdownScroll.GetComponentsInChildren<TMP_Text>(true))
                FixDdText(t);
        }

        // RefreshShownValue postfix: the game just rewrote the caption (tags included)
        internal static void OnDropdownRefresh(Component dd)
        {
            if (!_applied || dd == null) return;
            try
            {
                var tmpDd = dd as TMP_Dropdown;
                if (tmpDd != null) { FixDdText(tmpDd.captionText); return; }
                var uDd = dd as Dropdown;
                if (uDd != null && uDd.captionText != null
                    && !(uDd.captionText.color.r > 0.7f && uDd.captionText.color.g > 0.7f && uDd.captionText.color.b > 0.7f))
                    uDd.captionText.color = new Color(TextLight.r, TextLight.g, TextLight.b, uDd.captionText.color.a);
            }
            catch { }
        }

        /* Show postfix: the option list spawns fresh inside Show, game-styled — after the
           dark sweep that lands same-on-same (white labels over the white list box). Style
           it once right after it's built: dark plates, dark item tint-blocks, light labels;
           glyphs (checkmark) and already-light text stay. */
        internal static void OnDropdownShow(Component dd)
        {
            if (!_applied || dd == null) return;
            Transform list = null;
            try { list = dd.transform.Find("Dropdown List"); } catch { }
            if (list == null) return;

            foreach (var tog in list.GetComponentsInChildren<Toggle>(true))
            {
                var g = tog.targetGraphic;
                if (g == null) continue;
                g.color = Color.white;
                var block = tog.colors;
                block.normalColor = new Color(0.11f, 0.11f, 0.14f, 1f);
                block.highlightedColor = new Color(0.24f, 0.24f, 0.3f, 1f);
                block.pressedColor = new Color(0.3f, 0.3f, 0.36f, 1f);
                block.selectedColor = new Color(0.2f, 0.24f, 0.36f, 1f);
                tog.colors = block;
            }
            foreach (var img in list.GetComponentsInChildren<Image>(true))
            {
                if (img.type != Image.Type.Sliced && img.type != Image.Type.Tiled) continue; // glyphs stay
                var owner = img.GetComponentInParent<Toggle>();
                if (owner != null && ReferenceEquals(owner.targetGraphic, img)) continue;    // tinted above
                var c = img.color;
                if (c.r > 0.45f && c.g > 0.45f && c.b > 0.45f)
                    img.color = new Color(0.09f, 0.09f, 0.11f, 1f); // opaque: must read over level art
            }
            foreach (var t in list.GetComponentsInChildren<TextMeshProUGUI>(true))
                FixDdText(t);
            foreach (var t in list.GetComponentsInChildren<Text>(true))
                if (!(t.color.r > 0.7f && t.color.g > 0.7f && t.color.b > 0.7f))
                    t.color = new Color(TextLight.r, TextLight.g, TextLight.b, t.color.a);
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
            InterceptActive = true;   // arm the color-setter fast-path guard
            foreach (var r in roots)
            {
                ApplyRoot(r);
                try { ApplyTweakableDropdowns(r); } catch { }
                // captions written BEFORE the theme applied need one pass; afterwards the
                // RefreshShownValue/UpdateInputFieldText postfixes own them
                try
                {
                    foreach (var dd in r.GetComponentsInChildren<TweakableDropdown>(true)) OnTweakableCaption(dd);
                    foreach (var dd in r.GetComponentsInChildren<TMP_Dropdown>(true)) OnDropdownRefresh(dd);
                    foreach (var dd in r.GetComponentsInChildren<Dropdown>(true)) OnDropdownRefresh(dd);
                }
                catch { }
            }
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
                    if (gi != null) _ownedImages.Add(gi.GetInstanceID());
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
                    _ownedImages.Add(img.GetInstanceID());
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
                _ownedTexts.Add(t.GetInstanceID());
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
                _ownedTexts.Add(t.GetInstanceID());
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

        /* Setter interception (called from the Harmony prefixes below): remap the game's
           direct color writes on owned graphics the moment they happen. Our own writes
           already carry mapped colors, which no predicate matches — self-terminating. */
        internal static void InterceptColor(Graphic g, ref Color value)
        {
            if (!_applied || g == null) return;
            int id = g.GetInstanceID();
            if (_ownedTexts.Contains(id))
            {
                var mapped = MapTextColor(value);
                if (mapped.HasValue) value = mapped.Value;
            }
            else if (_ownedImages.Contains(id))
            {
                if (BrightGrey(value)) value = MapDark(value);
            }
        }

        internal static bool Applied => _applied;

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
            _lastHierarchySig = 0; // re-enable must re-sweep even if the UI didn't change
            foreach (var kv in _origDdColors)
                if (kv.Key != null)
                {
                    kv.Key.normalItemBGColor = kv.Value[0];
                    kv.Key.hoveredItemBGColor = kv.Value[1];
                    kv.Key.selectedItemBGColor = kv.Value[2];
                }
            _origDdColors.Clear();
            _ownedTexts.Clear();
            foreach (var kv in _origColors)
                if (kv.Key != null) kv.Key.color = kv.Value;
            foreach (var kv in _origBlocks)
                if (kv.Key != null) kv.Key.colors = kv.Value;
            foreach (var kv in _origSprites)
                if (kv.Key != null) kv.Key.sprite = kv.Value;
            _origColors.Clear();
            _origBlocks.Clear();
            _origSprites.Clear();
            _ownedImages.Clear();
            _seen.Clear();
            _popupSeen.Clear();
            _applied = false;
            InterceptActive = false;  // disarm — the global setter prefixes now cost one bool read
            _cooldown = 0;
        }
    }

    /* Event-driven dark theme (no per-frame polling): the game's direct color writes are
       remapped AT THE SETTER for graphics the skin owns. TMP_Text overrides Graphic.color
       with its own backing field, so both setters need the hook.

       These setters fire on EVERY color write in the whole game (menus, HUD, gameplay,
       tweens) — so the fast path must be as close to free as a patched method gets. When
       the dark theme isn't applied (all non-editor time), the prefix is a single static
       bool read + return: no try/catch frame, no method call. Only when actively theming
       the editor does it pay the HashSet lookups. */
    [HarmonyPatch(typeof(UnityEngine.UI.Graphic), "color", MethodType.Setter)]
    internal static class SkinGraphicColorPatch
    {
        private static void Prefix(UnityEngine.UI.Graphic __instance, ref Color value)
        {
            if (!EditorSkin.InterceptActive) return;
            try { EditorSkin.InterceptColor(__instance, ref value); } catch { }
        }
    }

    [HarmonyPatch(typeof(TMP_Text), "color", MethodType.Setter)]
    internal static class SkinTmpColorPatch
    {
        private static void Prefix(TMP_Text __instance, ref Color value)
        {
            if (!EditorSkin.InterceptActive) return;
            try { EditorSkin.InterceptColor(__instance, ref value); } catch { }
        }
    }

    // Captions are rewritten (rich-text tags included) inside RefreshShownValue; option
    // lists spawn inside Show — style each at its source instead of scanning per frame.
    [HarmonyPatch(typeof(TMP_Dropdown), "RefreshShownValue")]
    internal static class SkinTmpDropdownRefreshPatch
    {
        private static void Postfix(TMP_Dropdown __instance)
        {
            try { EditorSkin.OnDropdownRefresh(__instance); } catch { }
        }
    }

    [HarmonyPatch(typeof(TMP_Dropdown), "Show")]
    internal static class SkinTmpDropdownShowPatch
    {
        private static void Postfix(TMP_Dropdown __instance)
        {
            try { EditorSkin.OnDropdownShow(__instance); } catch { }
        }
    }

    [HarmonyPatch(typeof(UnityEngine.UI.Dropdown), "RefreshShownValue")]
    internal static class SkinDropdownRefreshPatch
    {
        private static void Postfix(UnityEngine.UI.Dropdown __instance)
        {
            try { EditorSkin.OnDropdownRefresh(__instance); } catch { }
        }
    }

    [HarmonyPatch(typeof(UnityEngine.UI.Dropdown), "Show")]
    internal static class SkinDropdownShowPatch
    {
        private static void Postfix(UnityEngine.UI.Dropdown __instance)
        {
            try { EditorSkin.OnDropdownShow(__instance); } catch { }
        }
    }

    // The game's own searchable dropdown control (the inspector's dropdowns).
    [HarmonyPatch(typeof(TweakableDropdown), "UpdateInputFieldText")]
    internal static class SkinTweakableCaptionPatch
    {
        private static void Postfix(TweakableDropdown __instance)
        {
            try { EditorSkin.OnTweakableCaption(__instance); } catch { }
        }
    }

    [HarmonyPatch(typeof(TweakableDropdown), "ShowList")]
    internal static class SkinTweakableShowPatch
    {
        private static void Postfix(TweakableDropdown __instance)
        {
            try { EditorSkin.OnTweakableShow(__instance); } catch { }
        }
    }
}

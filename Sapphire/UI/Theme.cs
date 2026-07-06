using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire.UI
{
    internal static class Theme
    {
        public static readonly Color Panel       = new Color(0.102f, 0.102f, 0.122f, 1f);
        public static readonly Color PanelBorder = new Color(1f, 1f, 1f, 0.10f);
        public static readonly Color TitleBar    = new Color(0.078f, 0.078f, 0.094f, 1f);
        public static readonly Color TabRail     = new Color(0.067f, 0.067f, 0.082f, 1f);
        public static readonly Color TabHover    = new Color(1f, 1f, 1f, 0.05f);
        public static readonly Color TabActive   = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Text        = new Color(0.92f, 0.92f, 0.94f, 1f);
        public static readonly Color TextMuted   = new Color(0.58f, 0.58f, 0.62f, 1f);
        public static readonly Color RowBg       = new Color(1f, 1f, 1f, 0.025f);
        public static readonly Color RowBgHover  = new Color(1f, 1f, 1f, 0.05f);
        public static readonly Color ToggleOff   = new Color(1f, 1f, 1f, 0.18f);
        public static readonly Color ButtonBg    = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color ButtonHover = new Color(1f, 1f, 1f, 0.14f);
        // Windows-close red (#E81123) — the titlebar × floods this on hover.
        public static readonly Color CloseHover  = new Color(0.910f, 0.067f, 0.137f, 1f);
        // Destructive-action button palette: subtle red tint at rest, brighter on hover,
        // brightest while armed (waiting for the confirmation click).
        public static readonly Color DangerBg    = new Color(0.886f, 0.404f, 0.427f, 0.12f);
        public static readonly Color DangerHover = new Color(0.886f, 0.404f, 0.427f, 0.22f);
        public static readonly Color DangerArmed = new Color(0.886f, 0.404f, 0.427f, 0.45f);

        // Accent — re-skinned at runtime via ApplyAccent. ToggleOn always tracks Accent.
        public static Color Accent   { get; private set; } = new Color(0.604f, 0.706f, 1f, 1f);
        public static Color ToggleOn { get; private set; } = new Color(0.604f, 0.706f, 1f, 1f);

        // Preset accent swatches for the UI page. Sharp, saturated, single-tone.
        public static readonly Color[] AccentPresets = new[]
        {
            new Color(0.604f, 0.706f, 1.000f, 1f), // periwinkle (default)
            new Color(0.886f, 0.404f, 0.427f, 1f), // soft red
            new Color(0.957f, 0.694f, 0.345f, 1f), // amber
            new Color(0.667f, 0.851f, 0.467f, 1f), // green
            new Color(0.486f, 0.812f, 0.812f, 1f), // teal
            new Color(0.788f, 0.612f, 0.957f, 1f), // lavender
            new Color(0.957f, 0.541f, 0.741f, 1f), // pink
            new Color(0.85f,  0.85f,  0.85f,  1f), // monochrome
        };

        public static void ApplyAccent(Color color, GameObject canvasRoot = null)
        {
            color.a = 1f;
            Accent = color;
            ToggleOn = color;

            if (canvasRoot == null) return;
            // Only touch graphics that explicitly declare themselves accent-tinted via a marker.
            // Walking-and-matching by color was unreliable: any Graphic that happened to currently
            // equal the accent (notably swatch presets) would be repainted, corrupting them.
            var fills = canvasRoot.GetComponentsInChildren<AccentFill>(true);
            for (int i = 0; i < fills.Length; i++)
            {
                var m = fills[i]; if (m == null || !m.Active) continue;
                var g = m.GetComponent<Graphic>(); if (g == null) continue;
                var c = color; c.a = g.color.a;
                g.color = c;
            }
            var borders = canvasRoot.GetComponentsInChildren<AccentBorder>(true);
            for (int i = 0; i < borders.Length; i++)
            {
                var m = borders[i]; if (m == null || !m.Active) continue;
                var rrg = m.GetComponent<RoundedRectGraphic>(); if (rrg == null) continue;
                var c = color; c.a = rrg.BorderColor.a;
                rrg.BorderColor = c;
            }
        }

        // Panel is all TextMeshPro: ApplyFont sweeps every TMP_Text under the canvas and
        // restamps the asset. Called on build (before widgets exist, just caches) and when
        // the user picks a panel font.
        public static void ApplyFont(TMP_FontAsset font, GameObject canvasRoot = null)
        {
            if (font == null) return;
            _font = font;
            if (canvasRoot == null) return;
            var texts = canvasRoot.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null) texts[i].font = font;
            }
        }


        private static TMP_FontAsset _font;
        public static TMP_FontAsset TmpFont
        {
            get
            {
                // Hard fallback only — UICore resolves the bundled panel font into _font
                // before any widget is built, so this rarely fires.
                if (_font == null) _font = TMP_Settings.defaultFontAsset;
                return _font;
            }
        }

        private static Sprite _whiteSprite;
        public static Sprite White
        {
            get
            {
                if (_whiteSprite == null)
                {
                    Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    Color[] px = new Color[4];
                    for (int i = 0; i < 4; i++) px[i] = Color.white;
                    t.SetPixels(px);
                    t.Apply();
                    t.wrapMode = TextureWrapMode.Clamp;
                    t.filterMode = FilterMode.Point;
                    _whiteSprite = Sprite.Create(t, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
                }
                return _whiteSprite;
            }
        }
    }
}

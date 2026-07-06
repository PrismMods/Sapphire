using TMPro;
using UnityEngine;

namespace Sapphire
{
    /* Drop shadow for TMP text. The legacy uGUI Shadow (IMeshModifier) is
       ignored by the TMP mesh pipeline, so this drives the SDF shader's underlay
       instead. OffsetPx keeps Shadow.effectDistance's canvas-unit semantics. The
       shader wants padding-relative units, so the conversion depends on fontSize
       and the font asset. Re-Apply() after either changes. */
    internal class TmpShadow : MonoBehaviour
    {
        public bool Enabled = true;
        public Color Color = new Color(0f, 0f, 0f, 0.5f);
        public Vector2 OffsetPx = new Vector2(2f, -2f);

        private TextMeshProUGUI _text;

        /* Last-applied state. Apply() regenerates the whole mesh/material, so
           redundant calls must no-op. */
        private bool _appliedValid;
        private bool _appliedEnabled;
        private Color _appliedColor;
        private Vector2 _appliedOffsetPx;
        private float _appliedFontSize;
        private TMP_FontAsset _appliedFont;

        internal static TmpShadow Attach(GameObject go, Color color, Vector2 offsetPx)
        {
            var sh = go.GetComponent<TmpShadow>() ?? go.AddComponent<TmpShadow>();
            sh.Color = color;
            sh.OffsetPx = offsetPx;
            sh.Apply();
            return sh;
        }

        public void Apply()
        {
            if (_text == null) _text = GetComponent<TextMeshProUGUI>();
            if (_text == null || _text.font == null) return;

            if (_appliedValid
                && _appliedEnabled == Enabled
                && _appliedColor == Color
                && _appliedOffsetPx == OffsetPx
                && _appliedFontSize == _text.fontSize
                && _appliedFont == _text.font)
                return;
            _appliedValid = true;
            _appliedEnabled = Enabled;
            _appliedColor = Color;
            _appliedOffsetPx = OffsetPx;
            _appliedFontSize = _text.fontSize;
            _appliedFont = _text.font;

            Material mat = _text.fontMaterial;
            if (Enabled)
            {
                mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                mat.SetColor(ShaderUtilities.ID_UnderlayColor, Color);

                /* Underlay offset is in SDF-padding units: 1.0 = atlasPadding
                   texels at the sampling point size. Convert px -> em fraction ->
                   padding units. The shader can't sample past the padding, hence
                   the [-1, 1] clamp. */
                float fs = _text.fontSize > 0f ? _text.fontSize : 1f;
                var fa = _text.font;
                float sampling = fa.faceInfo.pointSize;
                if (sampling <= 0f) sampling = 90f;
                float padding = fa.atlasPadding;
                if (padding <= 0f) padding = sampling * 0.1f;
                float toUnits = sampling / (padding * fs);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, Mathf.Clamp(OffsetPx.x * toUnits, -1f, 1f));
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, Mathf.Clamp(OffsetPx.y * toUnits, -1f, 1f));
            }
            else
            {
                mat.DisableKeyword(ShaderUtilities.Keyword_Underlay);
            }

            /* TMP measures mesh quad padding from the material. A freshly
               assigned font asset's material is measured before the underlay gets
               enabled above, which clips the shadow to the glyph bounds.
               Recompute and regenerate. */
            _text.UpdateMeshPadding();
            _text.SetVerticesDirty();
            _text.SetMaterialDirty();
        }
    }
}

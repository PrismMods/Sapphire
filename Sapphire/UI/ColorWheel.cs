using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire.UI
{
    /* HSV wheel + RGB picker, shared by every colour value in Sapphire.

       The game has one of these (ADOFAI.RDColorPickerPopup) but it is light-themed, positioned
       in the game's own canvas space and only alive inside scnEditor — so it could not serve the
       settings panel. This one is a normal PanelKit window: it drags, it stacks, it themes.

       The ring and the square are generated textures, not shaders: a 160px ring is built once
       and the 64px SV square is rebuilt only when the hue changes, which is cheap enough to do
       inside a drag.

       Commit policy: the callback fires on mouse-UP and on field commit, never per drag frame.
       Level-settings edits go through SaveStateScope, so a live callback would push one undo
       state per frame of a drag. */
    internal static class ColorWheel
    {
        private const float W = 236f, Ring = 176f, SvSide = 84f;
        private const float Pad = PanelKit.Pad, RowH = PanelKit.RowH, Gap = PanelKit.Gap;

        private static readonly PanelKit K = new PanelKit("SapphireColorWheel", 960, W, focusable: true);
        private static bool _open;
        private static Action<Color> _commit;
        private static bool _useAlpha;
        private static string _title = "Color";

        private static float _h, _s, _v, _a = 1f;
        private static Texture2D _ringTex, _svTex;
        private static RawImage _svImg;
        private static RectTransform _ringRt, _svRt, _hueDot, _svDot, _alphaRt, _alphaKnob;
        private static RoundedRectGraphic _preview, _alphaBar;
        private static TMP_InputField _fHex, _fR, _fG, _fB, _fH, _fS, _fV, _fA;
        private static int _drag;              // 0 none · 1 hue ring · 2 SV square · 3 alpha bar
        private static bool _svDirty;
        // What summoned the wheel. It opens beside that element's panel, level with it.
        private static RectTransform _anchor;

        internal static bool IsOpen => _open;
        internal static PanelKit Kit => K;

        internal static void Open(string title, Color initial, bool alpha, Action<Color> commit,
                                  RectTransform anchor = null)
        {
            _anchor = anchor;
            _title = string.IsNullOrEmpty(title) ? Loc.T("Color") : title;
            _commit = commit;
            _useAlpha = alpha;
            Color.RGBToHSV(initial, out _h, out _s, out _v);
            _a = alpha ? initial.a : 1f;
            _open = true;
            _svDirty = true;
            K.Dispose();                        // rebuild: title and the alpha row both change
        }

        /* Hex convenience for the event/level-settings fields, which store colours as bare
           RRGGBB or RRGGBBAA strings. The digit count of the ORIGINAL value decides the digit
           count written back — widening a 6-digit field to 8 makes the game write an alpha the
           level never had. */
        internal static void OpenHex(string title, string hex, Action<string> commit,
                                     RectTransform anchor = null)
        {
            string h = (hex ?? "").Trim().TrimStart('#');
            bool alpha = h.Length >= 8;
            Color c;
            if (!ColorUtility.TryParseHtmlString("#" + h, out c)) c = Color.white;
            Open(title, c, alpha, col =>
            {
                var c32 = (Color32)col;
                commit(alpha
                    ? string.Format("{0:X2}{1:X2}{2:X2}{3:X2}", c32.r, c32.g, c32.b, c32.a)
                    : string.Format("{0:X2}{1:X2}{2:X2}", c32.r, c32.g, c32.b));
            }, anchor);
        }

        internal static void Close() { _open = false; _drag = 0; }

        internal static void Tick()
        {
            if (!_open) { K.Show(false); return; }
            if (!K.Built) Build();
            K.Show(true);
            if (_svDirty) { _svDirty = false; PaintSv(); }
            TickDrag();
        }

        internal static void Dispose()
        {
            K.Dispose();
            _open = false; _drag = 0; _commit = null;
            _svImg = null; _ringRt = _svRt = _hueDot = _svDot = _alphaRt = _alphaKnob = null;
            _preview = null; _alphaBar = null;
            _fHex = _fR = _fG = _fB = _fH = _fS = _fV = _fA = null;
        }

        private static Color Cur => Color.HSVToRGB(_h, _s, _v);
        private static Color CurA { get { var c = Cur; c.a = _useAlpha ? _a : 1f; return c; } }

        // ── build ────────────────────────────────────────────────────────────

        private static void Build()
        {
            float h = Pad + Ring + Gap + (RowH + Gap) * (_useAlpha ? 4 : 3) + RowH + Pad * 2f;
            K.Rebuild(_title, Close, new Vector2(320f, -120f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = new Vector2(W, h);
            if (_anchor != null) PlaceBeside(panel, _anchor, h);

            float y = -Pad;
            _ringRt = MakeRing(y);
            _svRt = MakeSv();
            _hueDot = MakeDot("HueDot", _ringRt, 11f);
            _svDot = MakeDot("SvDot", _svRt, 11f);
            y -= Ring + Gap;

            // preview swatch + hex
            _preview = SwatchRect(Pad, y, 40f, RowH);
            _fHex = Field(Pad + 40f + Gap, y, W - Pad * 2f - 40f - Gap, HexOf(CurA), s =>
            {
                Color c;
                if (!ColorUtility.TryParseHtmlString("#" + s.Trim().TrimStart('#'), out c)) { Refresh(); return; }
                Color.RGBToHSV(c, out _h, out _s, out _v);
                if (_useAlpha && s.Trim().TrimStart('#').Length >= 8) _a = c.a;
                _svDirty = true; Refresh(); Commit();
            });
            y -= RowH + Gap;

            float fw = (W - Pad * 2f - Gap * 2f) / 3f;
            _fR = Byte3("R", 0, Pad, y, fw); _fG = Byte3("G", 1, Pad + fw + Gap, y, fw);
            _fB = Byte3("B", 2, Pad + (fw + Gap) * 2f, y, fw);
            y -= RowH + Gap;
            _fH = Hsv3("H", 0, Pad, y, fw); _fS = Hsv3("S", 1, Pad + fw + Gap, y, fw);
            _fV = Hsv3("V", 2, Pad + (fw + Gap) * 2f, y, fw);
            y -= RowH + Gap;

            if (_useAlpha)
            {
                float barW = W - Pad * 2f - fw - Gap;
                _alphaBar = SwatchRect(Pad, y, barW, RowH);
                _alphaRt = (RectTransform)_alphaBar.transform;
                _alphaKnob = MakeDot("AlphaKnob", _alphaRt, 9f);
                _fA = Field(Pad + barW + Gap, y, fw, Mathf.RoundToInt(_a * 255f).ToString(), s =>
                {
                    float f; if (!float.TryParse(s, out f)) { Refresh(); return; }
                    _a = Mathf.Clamp01(f / 255f); Refresh(); Commit();
                });
                y -= RowH + Gap;
            }

            K.Cell(Loc.T("Done"), Pad, y, W - Pad * 2f, RowH, Close, true);
            Refresh();
        }

        private static RectTransform MakeRing(float y)
        {
            if (_ringTex == null) _ringTex = BuildRingTex(160);
            var go = new GameObject("Ring", typeof(RectTransform));
            go.transform.SetParent(K.RowParent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2((W - Ring) * 0.5f, y);
            r.sizeDelta = new Vector2(Ring, Ring);
            var img = go.AddComponent<RawImage>();
            img.texture = _ringTex;
            img.raycastTarget = true;
            return r;
        }

        private static RectTransform MakeSv()
        {
            var go = new GameObject("SV", typeof(RectTransform));
            go.transform.SetParent(_ringRt, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(SvSide, SvSide);
            _svTex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            _svImg = go.AddComponent<RawImage>();
            _svImg.texture = _svTex;
            _svImg.raycastTarget = true;
            return r;
        }

        private static RectTransform MakeDot(string name, Transform parent, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(size, size);
            var g = go.AddComponent<RoundedRectGraphic>();
            g.Radius = size * 0.5f;
            g.color = new Color(0f, 0f, 0f, 0f);
            g.BorderWidth = 2f;
            g.BorderColor = Color.white;
            g.raycastTarget = false;
            return r;
        }

        private static RoundedRectGraphic SwatchRect(float x, float y, float w, float h)
        {
            var go = new GameObject("Swatch", typeof(RectTransform));
            go.transform.SetParent(K.RowParent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            var g = go.AddComponent<RoundedRectGraphic>();
            g.Radius = 5f;
            g.BorderWidth = 1f;
            g.BorderColor = new Color(1f, 1f, 1f, 0.2f);
            g.raycastTarget = true;
            return g;
        }

        // PanelKit.InputField does not hand back the component, and these need live refresh.
        private static TMP_InputField Field(float x, float y, float w, string value, Action<string> commit)
        {
            var go = new GameObject("F", typeof(RectTransform));
            go.transform.SetParent(K.RowParent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, RowH);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = new Color(1f, 1f, 1f, 0.08f);
            bg.raycastTarget = true;
            var txtGo = new GameObject("T", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(7f, 0f); tr.offsetMax = new Vector2(-7f, 0f);
            var txt = UIBuilder.Tmp(txtGo, value, 12.5f, TextAnchor.MiddleLeft, Theme.Text);
            txt.richText = false;
            var f = UIBuilder.BuildInputField(go, txt);
            f.lineType = TMP_InputField.LineType.SingleLine;
            f.text = value;
            f.onEndEdit.AddListener(v => commit(v));
            return f;
        }

        private static TMP_InputField Byte3(string tag, int idx, float x, float y, float w)
        {
            return Field(x, y, w, tag + " " + Mathf.RoundToInt(Chan(idx) * 255f), s =>
            {
                float f;
                if (!float.TryParse(Strip(s, tag), out f)) { Refresh(); return; }
                var c = Cur;
                if (idx == 0) c.r = Mathf.Clamp01(f / 255f);
                else if (idx == 1) c.g = Mathf.Clamp01(f / 255f);
                else c.b = Mathf.Clamp01(f / 255f);
                Color.RGBToHSV(c, out _h, out _s, out _v);
                _svDirty = true; Refresh(); Commit();
            });
        }

        private static TMP_InputField Hsv3(string tag, int idx, float x, float y, float w)
        {
            return Field(x, y, w, tag + " " + HsvShown(idx), s =>
            {
                float f;
                if (!float.TryParse(Strip(s, tag), out f)) { Refresh(); return; }
                if (idx == 0) _h = Mathf.Repeat(f, 360f) / 360f;
                else if (idx == 1) _s = Mathf.Clamp01(f / 100f);
                else _v = Mathf.Clamp01(f / 100f);
                _svDirty = true; Refresh(); Commit();
            });
        }

        private static string Strip(string s, string tag)
            => (s ?? "").Replace(tag, "").Replace(" ", "");

        private static float Chan(int i) { var c = Cur; return i == 0 ? c.r : i == 1 ? c.g : c.b; }
        private static int HsvShown(int i)
            => i == 0 ? Mathf.RoundToInt(_h * 360f) : Mathf.RoundToInt((i == 1 ? _s : _v) * 100f);

        private static string HexOf(Color c)
        {
            var c32 = (Color32)c;
            return _useAlpha
                ? string.Format("{0:X2}{1:X2}{2:X2}{3:X2}", c32.r, c32.g, c32.b, c32.a)
                : string.Format("{0:X2}{1:X2}{2:X2}", c32.r, c32.g, c32.b);
        }

        // ── textures ─────────────────────────────────────────────────────────

        private static Texture2D BuildRingTex(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f, outer = c, inner = c * 0.66f;
            for (int yy = 0; yy < size; yy++)
                for (int xx = 0; xx < size; xx++)
                {
                    float dx = xx - c, dy = yy - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // 1px feather on both edges — a hard cut on a 160px ring is visibly jagged
                    float a = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - inner);
                    if (a <= 0f) { px[yy * size + xx] = new Color32(0, 0, 0, 0); continue; }
                    float hue = Mathf.Repeat(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, 360f) / 360f;
                    var col = Color.HSVToRGB(hue, 1f, 1f);
                    col.a = a;
                    px[yy * size + xx] = col;
                }
            t.SetPixels32(px);
            t.Apply(false);
            return t;
        }

        private static void PaintSv()
        {
            if (_svTex == null) return;
            int n = _svTex.width;
            var px = new Color32[n * n];
            for (int yy = 0; yy < n; yy++)
            {
                float v = yy / (float)(n - 1);
                for (int xx = 0; xx < n; xx++)
                    px[yy * n + xx] = Color.HSVToRGB(_h, xx / (float)(n - 1), v);
            }
            _svTex.SetPixels32(px);
            _svTex.Apply(false);
        }

        // ── interaction ──────────────────────────────────────────────────────

        private static void TickDrag()
        {
            if (_ringRt == null) return;
            var m = (Vector2)Input.mousePosition;
            if (Input.GetMouseButtonDown(0))
            {
                if (Hit(_svRt, m)) _drag = 2;
                else if (_useAlpha && Hit(_alphaRt, m)) _drag = 3;
                else if (Hit(_ringRt, m) && InRing(m)) _drag = 1;
            }
            if (_drag != 0 && Input.GetMouseButton(0))
            {
                if (_drag == 1) HueFrom(m);
                else if (_drag == 2) SvFrom(m);
                else AlphaFrom(m);
                Refresh();
            }
            if (_drag != 0 && !Input.GetMouseButton(0)) { _drag = 0; Commit(); }
        }

        /* Open beside the panel the colour came from, top level with the field that summoned it,
           rather than at a fixed spot the charter then drags the wheel away from. Right of the
           panel when there is room, left when there is not, and always kept on screen.

           Panels here are anchored at their canvas's TOP-LEFT, while
           ScreenPointToLocalPointInRectangle answers relative to the canvas's PIVOT, its centre.
           Mixing those two up is what put the tile menu half a screen off and made the colour
           ring ignore drags, so the conversion is done once, explicitly, below. */
        private static void PlaceBeside(RectTransform panel, RectTransform anchor, float h)
        {
            var canvasRt = panel.parent as RectTransform;
            if (canvasRt == null) return;
            var host = HostPanel(anchor);
            var hc = new Vector3[4]; host.GetWorldCorners(hc);      // 0 BL · 1 TL · 2 TR · 3 BR
            var ac = new Vector3[4]; anchor.GetWorldCorners(ac);
            Vector2 hostTL = TopLeftSpace(canvasRt, hc[1]), hostTR = TopLeftSpace(canvasRt, hc[2]);
            Vector2 fieldTL = TopLeftSpace(canvasRt, ac[1]);
            var cr = canvasRt.rect;
            const float gap = 8f, margin = 8f;
            float w = panel.sizeDelta.x;
            float x = hostTR.x + gap;
            if (x + w > cr.width - margin) x = hostTL.x - gap - w;   // no room right: go left
            x = Mathf.Clamp(x, margin, Mathf.Max(margin, cr.width - w - margin));
            float y = Mathf.Clamp(fieldTL.y, -cr.height + h + margin, -margin);
            panel.anchoredPosition = new Vector2(x, y);
        }

        // A point in the canvas's top-left space: x right from the left edge, y negative downward.
        private static Vector2 TopLeftSpace(RectTransform canvasRt, Vector3 world)
        {
            Vector2 lp;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRt, RectTransformUtility.WorldToScreenPoint(null, world), null, out lp);
            var r = canvasRt.rect;
            return new Vector2(lp.x - r.xMin, lp.y - r.yMax);
        }

        // The PanelKit window an element lives in (PanelKit names its root "Panel").
        private static RectTransform HostPanel(RectTransform a)
        {
            for (var t = a.transform; t != null; t = t.parent)
                if (t.name == "Panel" && t is RectTransform rt) return rt;
            return a;
        }

        private static bool Hit(RectTransform r, Vector2 m)
            => r != null && RectTransformUtility.RectangleContainsScreenPoint(r, m, null);

        /* ScreenPointToLocalPointInRectangle answers in the rect's LOCAL space, whose origin is
           the PIVOT — not the centre. The ring and the alpha bar are laid out top-left-pivoted
           like every other row, so subtracting rect.center is what makes the maths centre-based.
           (The SV square happens to be centre-pivoted, which is why it worked and the ring did
           not.) Going through rect keeps all four callers pivot-agnostic. */
        private static bool Local(RectTransform rt, Vector2 m, out Vector2 p)
        {
            p = Vector2.zero;
            Vector2 lp;
            if (rt == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, m, null, out lp))
                return false;
            p = lp - rt.rect.center;
            return true;
        }

        private static bool InRing(Vector2 m)
        {
            Vector2 p;
            if (!Local(_ringRt, m, out p)) return false;
            float d = p.magnitude, outer = _ringRt.rect.width * 0.5f;
            return d <= outer && d >= outer * 0.62f;   // a touch under the texture's 0.66 inner edge
        }

        private static void HueFrom(Vector2 m)
        {
            Vector2 p;
            if (!Local(_ringRt, m, out p)) return;
            _h = Mathf.Repeat(Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg, 360f) / 360f;
            _svDirty = true;
        }

        private static void SvFrom(Vector2 m)
        {
            Vector2 p;
            if (!Local(_svRt, m, out p)) return;
            _s = Mathf.Clamp01(p.x / _svRt.rect.width + 0.5f);
            _v = Mathf.Clamp01(p.y / _svRt.rect.height + 0.5f);
        }

        private static void AlphaFrom(Vector2 m)
        {
            Vector2 p;
            if (!Local(_alphaRt, m, out p)) return;
            _a = Mathf.Clamp01(p.x / _alphaRt.rect.width + 0.5f);
        }

        private static void Commit()
        {
            try { _commit?.Invoke(CurA); }
            catch (Exception ex) { SapphireLog.Log("ColorWheel: commit failed: " + ex.Message); }
        }

        // Fields are only written when they are NOT focused — otherwise a refresh mid-drag would
        // overwrite what the user is typing.
        private static void Refresh()
        {
            if (_preview != null) _preview.color = CurA;
            if (_hueDot != null)
            {
                float rr = Ring * 0.5f * 0.83f, ang = _h * 360f * Mathf.Deg2Rad;
                _hueDot.anchoredPosition = new Vector2(Mathf.Cos(ang) * rr, Mathf.Sin(ang) * rr);
            }
            if (_svDot != null)
                _svDot.anchoredPosition = new Vector2((_s - 0.5f) * SvSide, (_v - 0.5f) * SvSide);
            if (_alphaBar != null) _alphaBar.color = Cur;
            if (_alphaKnob != null && _alphaRt != null)
                _alphaKnob.anchoredPosition = new Vector2((_a - 0.5f) * _alphaRt.rect.width, 0f);
            Set(_fHex, HexOf(CurA));
            Set(_fR, "R " + Mathf.RoundToInt(Chan(0) * 255f));
            Set(_fG, "G " + Mathf.RoundToInt(Chan(1) * 255f));
            Set(_fB, "B " + Mathf.RoundToInt(Chan(2) * 255f));
            Set(_fH, "H " + HsvShown(0));
            Set(_fS, "S " + HsvShown(1));
            Set(_fV, "V " + HsvShown(2));
            Set(_fA, Mathf.RoundToInt(_a * 255f).ToString());
        }

        private static void Set(TMP_InputField f, string v)
        {
            if (f == null || f.isFocused || f.text == v) return;
            f.text = v;
        }
    }
}

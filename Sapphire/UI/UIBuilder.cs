using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sapphire.UI
{
    internal static class UIBuilder
    {
        public const float RowHeight = 32f;
        public const float SectionGap = 22f;
        public const float LabelFontSize = 15;
        public const float HeaderFontSize = 16;
        public const float SmallCapsFontSize = 12;

        // ── TMP helpers ─────────────────────────────────────────────────────
        // The panel is built entirely from TextMeshPro. These map the legacy uGUI
        // vocabulary the builders were written against onto TMP equivalents.

        // uGUI TextAnchor → TMP alignment. TMP's Left/Center/Right are the
        // vertically-centered ("Middle*") variants.
        public static TextAlignmentOptions Align(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft:    return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter:  return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight:   return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft:   return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight:  return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft:    return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter:  return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight:   return TextAlignmentOptions.BottomRight;
                default:                      return TextAlignmentOptions.Left;
            }
        }

        // Add a configured TMP label to an existing GameObject. Matches the legacy
        // Text defaults the builders assumed: no wrap, overflow, rich text on,
        // non-interactive.
        public static TextMeshProUGUI Tmp(GameObject go, string text, float size,
            TextAnchor anchor, Color color, bool wrap = false)
        {
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.font = Theme.TmpFont;
            t.fontSize = size;
            t.color = color;
            t.alignment = Align(anchor);
            t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // TMP_InputField factory. TMP needs a viewport RectTransform with the text
        // component nested inside it (the caret is spawned under the viewport), so the
        // field GO becomes the viewport and `txt` its child. Returns the field; caller
        // wires contentType / events / initial text.
        public static TMP_InputField BuildInputField(GameObject fieldGo, TextMeshProUGUI txt)
        {
            var input = fieldGo.AddComponent<TMP_InputField>();
            input.textViewport = (RectTransform)fieldGo.transform;
            input.textComponent = txt;
            input.caretWidth = 1;
            input.customCaretColor = true;
            input.caretColor = Theme.Text;
            input.selectionColor = new Color(Theme.ToggleOn.r, Theme.ToggleOn.g, Theme.ToggleOn.b, 0.45f);
            // AddComponent runs OnEnable BEFORE textComponent is assigned, so on an active
            // canvas the caret object is never created — a re-enable builds it properly.
            input.enabled = false;
            input.enabled = true;
            return input;
        }

        public static GameObject Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        // Standard vertical stack container — the content wrapper used by pages/subpages
        // for groups whose visibility is toggled or whose children are rebuilt.
        public static GameObject VGroup(Transform parent, string name = "Group", float spacing = 2f)
        {
            var go = Rect(name, parent);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = spacing;
            return go;
        }

        // Two equal-width side-by-side stacks — for pairing related controls (e.g. the
        // panel/overlay font selectors) instead of stacking full-width rows.
        public static void Columns(Transform parent, out Transform left, out Transform right, float gap = 12f)
        {
            var host = Rect("Columns", parent);
            var hlg = host.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;
            hlg.spacing = gap;
            hlg.childAlignment = TextAnchor.UpperLeft;
            left = VGroup(host.transform, "Left").transform;
            right = VGroup(host.transform, "Right").transform;
        }

        public static Image SolidImage(GameObject go, Color color)
        {
            var img = go.AddComponent<Image>();
            img.sprite = Theme.White;
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, int size = (int)LabelFontSize, TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null)
        {
            var go = Rect("Label", parent);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return Tmp(go, text, size, anchor, color ?? Theme.Text);
        }

        public static GameObject SectionHeader(Transform parent, string text)
        {
            SettingsSearch.Register(text);
            var go = Rect(text + "Header", parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 22f;
            le.minHeight = 22f;

            var label = Label(go.transform, text.ToUpperInvariant(), (int)SmallCapsFontSize, TextAnchor.MiddleLeft, Theme.TextMuted);
            label.fontStyle = FontStyles.Bold;
            label.rectTransform.offsetMin = new Vector2(2f, 0f);
            return go;
        }

        // SectionHeader with a [?] icon next to the label. Hover the icon to show a popup
        // with helpText. Popup parents to the canvas root so it can render over the
        // scroll viewport instead of being clipped by RectMask2D.
        public static GameObject SectionHeaderWithHelp(Transform parent, string text, string helpText)
        {
            SettingsSearch.Register(text);
            var go = Rect(text + "Header", parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 22f;
            le.minHeight = 22f;

            // childControlWidth=true so HLG honors preferred widths; false made the [?]
            // icon expand to the default RectTransform size and render as a wide pill.
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 6f;
            hlg.padding = new RectOffset(2, 0, 0, 0);

            var labelGo = Rect("L", go.transform);
            var labelT = Tmp(labelGo, text.ToUpperInvariant(), (int)SmallCapsFontSize, TextAnchor.MiddleLeft, Theme.TextMuted);
            labelT.fontStyle = FontStyles.Bold;

            var qGo = Rect("Q", go.transform);
            var qLe = qGo.AddComponent<LayoutElement>();
            qLe.preferredWidth = 14f;
            qLe.preferredHeight = 14f;
            qLe.minWidth = 14f;
            qLe.minHeight = 14f;
            var qBg = qGo.AddComponent<RoundedRectGraphic>();
            qBg.Radius = 7f;
            qBg.AAFringe = 0.5f;
            qBg.color = new Color(1f, 1f, 1f, 0.08f);
            qBg.raycastTarget = true;
            var qLbl = Label(qGo.transform, "?", (int)SmallCapsFontSize - 1, TextAnchor.MiddleCenter, Theme.TextMuted);
            qLbl.fontStyle = FontStyles.Bold;

            var popup = BuildHelpTooltip(helpText);
            if (popup != null) popup.SetActive(false);

            var qRect = (RectTransform)qGo.transform;
            var hover = qGo.AddComponent<HoverHandler>();
            hover.OnEnter = () =>
            {
                if (popup == null) return;
                popup.SetActive(true);
                popup.transform.SetAsLastSibling();
                Vector3[] corners = new Vector3[4];
                qRect.GetWorldCorners(corners);
                // corners[0] = qmark bottom-left in world coords. Popup pivot is top-left,
                // so this anchors the popup directly under the icon with a small gap.
                popup.transform.position = corners[0] + new Vector3(-4f, -4f, 0f);
            };
            hover.OnExit = () =>
            {
                if (popup != null) popup.SetActive(false);
            };

            return go;
        }

        // Floating help tooltip parented to the canvas root. Auto-sizes both axes to fit
        // the wrapped text (ContentSizeFitter + VLG padding). Hidden by default; the caller
        // toggles visibility on hover.
        private static GameObject BuildHelpTooltip(string text)
        {
            var root = UICore.CanvasRoot;
            if (root == null) return null;
            var go = Rect("HelpTooltip", root.transform);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 1f);

            // Purely a backdrop, and it sits over the '?' icon that opened it — leaving it a
            // raycast target let the tooltip swallow that icon's pointer enter/exit.
            SolidImage(go, Theme.Panel).raycastTarget = false;
            AddBorder(go, Theme.PanelBorder, 1f);

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 8, 8);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;

            var csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = Rect("T", go.transform);
            Tmp(textGo, text, (int)SmallCapsFontSize, TextAnchor.UpperLeft, Theme.Text);

            return go;
        }

        public static GameObject Row(Transform parent, float height = RowHeight)
        {
            var go = Rect("Row", parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return go;
        }

        public static GameObject Toggle(Transform parent, string label, bool initial, Action<bool> onChange)
        {
            SettingsSearch.Register(label);
            var row = Row(parent);
            bool value = initial;

            var bg = SolidImage(row, new Color(0, 0, 0, 0));
            bg.raycastTarget = true;

            var labelGo = Rect("Text", row.transform);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(8f, 0f);
            labelRect.offsetMax = new Vector2(-40f, 0f);
            var lab = Tmp(labelGo, label, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text, wrap: true);

            // Classic radio button: outer ring + filled inner dot when on.
            const float ringSize = 16f;
            const float dotSize = 7f;
            var ringGo = Rect("Ring", row.transform);
            var ringRect = (RectTransform)ringGo.transform;
            ringRect.anchorMin = new Vector2(1f, 0.5f);
            ringRect.anchorMax = new Vector2(1f, 0.5f);
            ringRect.pivot = new Vector2(1f, 0.5f);
            ringRect.anchoredPosition = new Vector2(-8f, 0f);
            ringRect.sizeDelta = new Vector2(ringSize, ringSize);
            var ring = ringGo.AddComponent<RoundedRectGraphic>();
            ring.Radius = ringSize * 0.5f;
            ring.BorderWidth = 1.25f;
            ring.BorderColor = value ? Theme.ToggleOn : Theme.ToggleOff;
            ring.color = new Color(0, 0, 0, 0);
            ring.raycastTarget = false;

            var dotGo = Rect("Dot", ringGo.transform);
            var dotRect = (RectTransform)dotGo.transform;
            dotRect.anchorMin = new Vector2(0.5f, 0.5f);
            dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(dotSize, dotSize);
            var dot = dotGo.AddComponent<RoundedRectGraphic>();
            dot.Radius = dotSize * 0.5f;
            dot.color = Theme.ToggleOn;
            dot.raycastTarget = false;
            dotGo.SetActive(value);

            void Apply(bool v)
            {
                value = v;
                ring.BorderColor = v ? Theme.ToggleOn : Theme.ToggleOff;
                dotGo.SetActive(v);
            }

            HoverFill(row, bg, Theme.RowBgHover, new Color(0, 0, 0, 0));
            ClickHandler.Attach(row, () => { Apply(!value); onChange?.Invoke(value); });

            return row;
        }

        // Header row: optional ▶ arrow (when buildBody is non-null) + clickable title + radio.
        // Arrow/title click expands/collapses the body; radio toggles the bool independently.
        // The container's VLG-computed preferredHeight lets the parent scrollable VLG reflow.
        public static GameObject Collapsible(
            Transform parent,
            string title,
            bool initial,
            Action<bool> onToggle,
            Action<Transform> buildBody = null)
        {
            SettingsSearch.Register(title);
            var container = Rect("Coll", parent);
            var clVlg = container.AddComponent<VerticalLayoutGroup>();
            clVlg.childControlWidth = true;
            clVlg.childControlHeight = true;
            clVlg.childForceExpandWidth = true;
            clVlg.childForceExpandHeight = false;
            clVlg.spacing = 0f;
            clVlg.padding = new RectOffset(0, 0, 0, 0);

            // Header row
            var header = Rect("Header", container.transform);
            var headerLe = header.AddComponent<LayoutElement>();
            headerLe.preferredHeight = RowHeight;
            headerLe.minHeight = RowHeight;
            var headerBg = SolidImage(header, new Color(0, 0, 0, 0));
            headerBg.raycastTarget = true;

            bool hasBody = buildBody != null;
            bool expanded = false;
            bool value = initial;

            // ▶ chevron (only when there's a body). Animator rotates it 90° on expand.
            TextMeshProUGUI chevron = null;
            if (hasBody)
            {
                var arrowGo = Rect("Arrow", header.transform);
                var arrowRect = (RectTransform)arrowGo.transform;
                arrowRect.anchorMin = new Vector2(0, 0);
                arrowRect.anchorMax = new Vector2(0, 1);
                arrowRect.pivot = new Vector2(0, 0.5f);
                arrowRect.sizeDelta = new Vector2(24f, 0);
                arrowRect.anchoredPosition = new Vector2(2f, 0);
                chevron = labelChild(arrowGo.transform, "▶", 15, TextAnchor.MiddleCenter, Theme.TextMuted);
            }

            // Title click zone (separate hit region — does NOT toggle the radio)
            var titleGo = Rect("Title", header.transform);
            var titleRect = (RectTransform)titleGo.transform;
            titleRect.anchorMin = new Vector2(0, 0);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.offsetMin = new Vector2(hasBody ? 32f : 8f, 0);
            titleRect.offsetMax = new Vector2(-36f, 0);
            var titleBg = SolidImage(titleGo, new Color(0, 0, 0, 0));
            titleBg.raycastTarget = true;
            labelChild(titleGo.transform, title, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

            // Radio (right side) — separate click zone, doesn't bubble to title/header
            const float ringSize = 14f;
            const float dotSize = 6f;
            var ringGo = Rect("Ring", header.transform);
            var ringRect = (RectTransform)ringGo.transform;
            ringRect.anchorMin = new Vector2(1f, 0.5f);
            ringRect.anchorMax = new Vector2(1f, 0.5f);
            ringRect.pivot = new Vector2(1f, 0.5f);
            ringRect.anchoredPosition = new Vector2(-8f, 0f);
            ringRect.sizeDelta = new Vector2(ringSize, ringSize);
            var ring = ringGo.AddComponent<RoundedRectGraphic>();
            ring.Radius = ringSize * 0.5f;
            ring.BorderWidth = 1.25f;
            ring.BorderColor = value ? Theme.ToggleOn : Theme.ToggleOff;
            ring.color = new Color(0, 0, 0, 0);
            ring.raycastTarget = true;
            var ringAccent = ringGo.AddComponent<AccentBorder>();
            ringAccent.Active = value;

            var dotGo = Rect("Dot", ringGo.transform);
            var dotRect = (RectTransform)dotGo.transform;
            dotRect.anchorMin = new Vector2(0.5f, 0.5f);
            dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.sizeDelta = new Vector2(dotSize, dotSize);
            var dot = dotGo.AddComponent<RoundedRectGraphic>();
            dot.Radius = dotSize * 0.5f;
            dot.color = Theme.ToggleOn;
            dot.raycastTarget = false;
            dotGo.AddComponent<AccentFill>();
            dotGo.SetActive(value);

            // Body — initially hidden, only created when callback provided. Wired with
            // CanvasGroup (alpha fade) + LayoutElement (height interpolation override) +
            // RectMask2D (clip children that overflow during unroll) for animation.
            GameObject bodyGo = null;
            ExpandAnimator animator = null;
            if (hasBody)
            {
                bodyGo = Rect("Body", container.transform);
                var bodyVlg = bodyGo.AddComponent<VerticalLayoutGroup>();
                bodyVlg.childControlWidth = true;
                bodyVlg.childControlHeight = true;
                bodyVlg.childForceExpandWidth = true;
                bodyVlg.childForceExpandHeight = false;
                bodyVlg.spacing = 2f;
                bodyVlg.padding = new RectOffset(24, 0, 2, 6);
                var bodyLe = bodyGo.AddComponent<LayoutElement>();
                bodyLe.preferredHeight = -1f;
                var bodyCg = bodyGo.AddComponent<CanvasGroup>();
                bodyCg.alpha = 0f;
                bodyGo.AddComponent<RectMask2D>();
                buildBody(bodyGo.transform);
                bodyGo.SetActive(false);

                animator = bodyGo.AddComponent<ExpandAnimator>();
                animator.Body = (RectTransform)bodyGo.transform;
                animator.BodyLe = bodyLe;
                animator.BodyCg = bodyCg;
                animator.Chevron = chevron != null ? chevron.rectTransform : null;
            }

            Action toggleValue = () => {
                value = !value;
                ring.BorderColor = value ? Theme.ToggleOn : Theme.ToggleOff;
                ringAccent.Active = value;
                dotGo.SetActive(value);
                onToggle?.Invoke(value);
            };

            ClickHandler.Attach(ringGo, toggleValue);

            if (hasBody)
            {
                Action toggleExpand = () => {
                    expanded = !expanded;
                    animator.Set(expanded);
                };
                ClickHandler.Attach(titleGo, toggleExpand);
                // Empty header space (the gap between title and ring) also bubbles to here
                // via the header's raycast bg — clicking it expands too.
                ClickHandler.Attach(header, toggleExpand);
            }
            else
            {
                // No body → no arrow → no expand action. Clicking anywhere on the row
                // (including the title area) toggles the radio.
                ClickHandler.Attach(titleGo, toggleValue);
                ClickHandler.Attach(header, toggleValue);
            }

            HoverFill(header, headerBg, Theme.RowBgHover, new Color(0, 0, 0, 0));
            return container;
        }

        // Drill-in row for a PageStack subpage: optional toggle ring on the left (Key
        // Viewer preset-row style), title, muted › chevron on the right. Row click
        // navigates; ring click toggles without navigating. `keywords` = comma-separated
        // phrases naming the subpage's contents, for settings search.
        public static GameObject NavRow(Transform parent, string title, Action onOpen, string keywords = null)
            => NavRowInternal(parent, title, false, false, null, onOpen, keywords);

        public static GameObject NavRow(Transform parent, string title, bool initial, Action<bool> onToggle, Action onOpen, string keywords = null)
            => NavRowInternal(parent, title, true, initial, onToggle, onOpen, keywords);

        private static GameObject NavRowInternal(
            Transform parent, string title,
            bool hasToggle, bool initial, Action<bool> onToggle, Action onOpen, string keywords)
        {
            SettingsSearch.Register(title, onOpen, keywords);
            var row = Row(parent);
            var bg = SolidImage(row, new Color(0, 0, 0, 0));
            bg.raycastTarget = true;

            const float ringSize = 14f;
            const float dotSize = 6f;

            if (hasToggle)
            {
                bool value = initial;

                // Oversized hit zone so the 14px ring is comfortably clickable.
                var hitGo = Rect("RingHit", row.transform);
                var hitRect = (RectTransform)hitGo.transform;
                hitRect.anchorMin = new Vector2(0, 0);
                hitRect.anchorMax = new Vector2(0, 1);
                hitRect.pivot = new Vector2(0, 0.5f);
                hitRect.anchoredPosition = new Vector2(2f, 0);
                hitRect.sizeDelta = new Vector2(28f, 0);
                var hitBg = SolidImage(hitGo, new Color(0, 0, 0, 0));
                hitBg.raycastTarget = true;

                var ringGo = Rect("Ring", hitGo.transform);
                var ringRect = (RectTransform)ringGo.transform;
                ringRect.anchorMin = ringRect.anchorMax = new Vector2(0.5f, 0.5f);
                ringRect.pivot = new Vector2(0.5f, 0.5f);
                ringRect.sizeDelta = new Vector2(ringSize, ringSize);
                var ring = ringGo.AddComponent<RoundedRectGraphic>();
                ring.Radius = ringSize * 0.5f;
                ring.BorderWidth = 1.25f;
                ring.BorderColor = value ? Theme.ToggleOn : Theme.ToggleOff;
                ring.color = new Color(0, 0, 0, 0);
                ring.raycastTarget = false;
                var ringAccent = ringGo.AddComponent<AccentBorder>();
                ringAccent.Active = value;

                var dotGo = Rect("Dot", ringGo.transform);
                var dotRect = (RectTransform)dotGo.transform;
                dotRect.anchorMin = dotRect.anchorMax = new Vector2(0.5f, 0.5f);
                dotRect.pivot = new Vector2(0.5f, 0.5f);
                dotRect.sizeDelta = new Vector2(dotSize, dotSize);
                var dot = dotGo.AddComponent<RoundedRectGraphic>();
                dot.Radius = dotSize * 0.5f;
                dot.color = Theme.ToggleOn;
                dot.raycastTarget = false;
                dotGo.AddComponent<AccentFill>();
                dotGo.SetActive(value);

                ClickHandler.Attach(hitGo, () =>
                {
                    value = !value;
                    ring.BorderColor = value ? Theme.ToggleOn : Theme.ToggleOff;
                    ringAccent.Active = value;
                    dotGo.SetActive(value);
                    onToggle?.Invoke(value);
                });
            }

            var labelGo = Rect("Title", row.transform);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = new Vector2(0, 0);
            labelRect.anchorMax = new Vector2(1, 1);
            labelRect.offsetMin = new Vector2(hasToggle ? 36f : 8f, 0);
            labelRect.offsetMax = new Vector2(-28f, 0);
            var titleTxt = labelChild(labelGo.transform, title, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);
            // Feature names collide with game localization keys; the guard restores the
            // text after the game's rewrite pass.
            var rowGuard = labelGo.AddComponent<TabLabelGuard>();
            rowGuard.Label = titleTxt;
            rowGuard.Expected = title;

            var chevGo = Rect("Chevron", row.transform);
            var chevRect = (RectTransform)chevGo.transform;
            chevRect.anchorMin = new Vector2(1, 0);
            chevRect.anchorMax = new Vector2(1, 1);
            chevRect.pivot = new Vector2(1, 0.5f);
            chevRect.anchoredPosition = new Vector2(-10f, 0);
            chevRect.sizeDelta = new Vector2(16f, 0);
            labelChild(chevGo.transform, "›", (int)LabelFontSize + 3, TextAnchor.MiddleCenter, Theme.TextMuted);

            HoverFill(row, bg, Theme.RowBgHover, new Color(0, 0, 0, 0));
            ClickHandler.Attach(row, onOpen);
            return row;
        }

        // Wrapping grid host for ToggleCards. Fixed cell size; the flexible constraint
        // re-flows the column count whenever the panel width changes.
        public static GameObject CardGrid(Transform parent)
        {
            var go = Rect("CardGrid", parent);
            var grid = go.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(156f, 44f);
            grid.spacing = new Vector2(6f, 6f);
            grid.padding = new RectOffset(8, 8, 2, 2);
            grid.constraint = GridLayoutGroup.Constraint.Flexible;
            grid.childAlignment = TextAnchor.UpperLeft;
            return go;
        }

        // Toggle card — a click-anywhere cell whose accent-tinted background + border show
        // the enabled state. For pages that are mostly independent on/off flags (Hide UI):
        // denser and calmer than a column of radio rows.
        public static GameObject ToggleCard(Transform parent, string label, bool initial, Action<bool> onChange)
            => CardInternal(parent, label, true, initial, onChange, null);

        // Card with a ⚙ corner button that drills into a subpage. With a toggle, the card
        // body toggles and only the gear navigates; without one, the whole card navigates.
        // `keywords` = comma-separated subpage contents, for settings search.
        public static GameObject NavCard(Transform parent, string label, Action onOpen, string keywords = null)
            => CardInternal(parent, label, false, false, null, onOpen, keywords);

        public static GameObject NavCard(Transform parent, string label, bool initial, Action<bool> onToggle, Action onOpen, string keywords = null)
            => CardInternal(parent, label, true, initial, onToggle, onOpen, keywords);

        private static GameObject CardInternal(Transform parent, string label,
            bool hasToggle, bool initial, Action<bool> onToggle, Action onOpen, string keywords = null)
        {
            SettingsSearch.Register(label, onOpen, keywords);
            var card = Rect("Card_" + label, parent);
            bool value = initial;
            bool hover = false;

            var bg = card.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.AAFringe = 0.5f;
            bg.BorderWidth = 1.25f;
            bg.raycastTarget = true;
            // Theme.ApplyAccent repaints these (preserving alpha) when the accent changes;
            // Active tracks the toggle so an off card keeps its neutral tint.
            var fillMark = card.AddComponent<AccentFill>();
            var borderMark = card.AddComponent<AccentBorder>();

            var txtGo = Rect("L", card.transform);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = new Vector2(8f, 0);
            txtRect.offsetMax = new Vector2(-8f, 0);
            var txt = Tmp(txtGo, label, (int)LabelFontSize - 1, TextAnchor.MiddleCenter, Theme.TextMuted);
            txt.enableAutoSizing = true;
            txt.fontSizeMin = 9;
            txt.fontSizeMax = (int)LabelFontSize - 1;
            txt.overflowMode = TextOverflowModes.Truncate;
            // Card names collide with game localization keys ("Difficulty" → "난이도");
            // the guard restores the text after the game's rewrite pass.
            var cardGuard = txtGo.AddComponent<TabLabelGuard>();
            cardGuard.Label = txt;
            cardGuard.Expected = label;

            void Apply()
            {
                bool on = hasToggle && value;
                fillMark.Active = on;
                borderMark.Active = on;
                var a = Theme.ToggleOn;
                if (on)
                {
                    bg.color = new Color(a.r, a.g, a.b, hover ? 0.26f : 0.17f);
                    bg.BorderColor = new Color(a.r, a.g, a.b, 0.9f);
                    txt.color = Theme.Text;
                }
                else
                {
                    bg.color = new Color(1f, 1f, 1f, hover ? 0.07f : 0.035f);
                    bg.BorderColor = new Color(1f, 1f, 1f, 0.08f);
                    txt.color = Theme.TextMuted;
                }
            }
            Apply();

            var h = card.AddComponent<HoverHandler>();
            h.OnEnter = () => { hover = true; Apply(); };
            h.OnExit = () => { hover = false; Apply(); };
            ClickHandler.Attach(card, hasToggle
                ? () => { value = !value; Apply(); onToggle?.Invoke(value); }
                : onOpen);

            if (onOpen != null)
            {
                // Settings corner button — its own raycast target, so clicking it never
                // toggles. Icon is a ··· drawn from circles: user-supplied panel fonts
                // often lack glyphs like ⚙, which rendered as nothing.
                var gearGo = Rect("Gear", card.transform);
                var gearRect = (RectTransform)gearGo.transform;
                gearRect.anchorMin = gearRect.anchorMax = new Vector2(1, 1);
                gearRect.pivot = new Vector2(1, 1);
                gearRect.anchoredPosition = new Vector2(-2f, -2f);
                gearRect.sizeDelta = new Vector2(20f, 16f);
                var gearBg = gearGo.AddComponent<RoundedRectGraphic>();
                gearBg.Radius = 4f;
                gearBg.AAFringe = 0.5f;
                gearBg.color = new Color(0, 0, 0, 0);
                gearBg.raycastTarget = true;

                var dots = new RoundedRectGraphic[3];
                for (int d = 0; d < 3; d++)
                {
                    var dotGo = Rect("D" + d, gearGo.transform);
                    var dRect = (RectTransform)dotGo.transform;
                    dRect.anchorMin = dRect.anchorMax = new Vector2(0.5f, 0.5f);
                    dRect.pivot = new Vector2(0.5f, 0.5f);
                    dRect.sizeDelta = new Vector2(2.5f, 2.5f);
                    dRect.anchoredPosition = new Vector2((d - 1) * 4.5f, 0f);
                    var dg = dotGo.AddComponent<RoundedRectGraphic>();
                    dg.Radius = 1.25f;
                    dg.AAFringe = 0.5f;
                    dg.color = Theme.TextMuted;
                    dg.raycastTarget = false;
                    dots[d] = dg;
                }

                var gh = gearGo.AddComponent<HoverHandler>();
                gh.OnEnter = () =>
                {
                    gearBg.color = new Color(1f, 1f, 1f, 0.12f);
                    foreach (var dg in dots) dg.color = Theme.Text;
                };
                gh.OnExit = () =>
                {
                    gearBg.color = new Color(0, 0, 0, 0);
                    foreach (var dg in dots) dg.color = Theme.TextMuted;
                };
                ClickHandler.Attach(gearGo, onOpen);
            }
            return card;
        }

        // Internal: create a child Text inside a parent rect (no its own LayoutElement).
        private static TextMeshProUGUI labelChild(Transform parent, string text, int size, TextAnchor anchor, Color color)
        {
            var go = Rect("L", parent);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return Tmp(go, text, size, anchor, color);
        }

        public static GameObject Button(Transform parent, string label, Action onClick)
        {
            SettingsSearch.Register(label);
            var row = Row(parent);
            var bg = SolidImage(row, Theme.ButtonBg);
            bg.raycastTarget = true;

            var t = Label(row.transform, label, (int)LabelFontSize, TextAnchor.MiddleCenter, Theme.Text);
            t.rectTransform.offsetMin = new Vector2(8f, 0f);
            t.rectTransform.offsetMax = new Vector2(-8f, 0f);

            HoverFill(row, bg, Theme.ButtonHover, Theme.ButtonBg);
            ClickHandler.Attach(row, onClick);
            return row;
        }

        public static GameObject IntSlider(
            Transform parent,
            string label,
            int value, int min, int max,
            Action<int> onChange)
        {
            return Slider(parent, label, value, min, max,
                v => onChange?.Invoke(Mathf.RoundToInt(v)),
                "0", 1f);
        }

        // Horizontal slider — label left, draggable track + handle middle, numeric value right.
        // `step` > 0 snaps the value to multiples of step (used by IntSlider with step=1).
        public static GameObject Slider(
            Transform parent,
            string label,
            float value, float min, float max,
            Action<float> onChange,
            string format = "0.00",
            float step = 0f)
        {
            SettingsSearch.Register(label);
            var row = Row(parent);
            const float labelW = 140f;
            const float valueW = 56f;
            const float undoW = 46f;   // inline "Undo" button, left of the value field
            const float undoH = 20f;
            const float undoGap = 4f;
            const float trackGap = 12f; // breathing room between the track end and the undo button
            const float rightW = valueW + undoW + undoGap + trackGap; // reserved right cluster (track sizing)

            // Label
            var labGo = Rect("Label", row.transform);
            var labRect = (RectTransform)labGo.transform;
            labRect.anchorMin = new Vector2(0, 0);
            labRect.anchorMax = new Vector2(0, 1);
            labRect.pivot = new Vector2(0, 0.5f);
            labRect.sizeDelta = new Vector2(labelW, 0);
            labRect.anchoredPosition = new Vector2(8f, 0);
            var lab = Tmp(labGo, label, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

            // Value display (right) — a TMP_InputField (on `valGo`) for click-to-type. The
            // visible text is a child so it doesn't collide with the field's own. Boxed
            // like TextInput (hover brighten + accent border while focused) so it reads
            // as editable rather than a static readout.
            var valGo = Rect("Value", row.transform);
            var valRect = (RectTransform)valGo.transform;
            valRect.anchorMin = valRect.anchorMax = new Vector2(1, 0.5f);
            valRect.pivot = new Vector2(1, 0.5f);
            valRect.sizeDelta = new Vector2(valueW, 24f);
            valRect.anchoredPosition = new Vector2(-8f, 0);
            var valBg = valGo.AddComponent<RoundedRectGraphic>();
            valBg.Radius = 4f;
            valBg.AAFringe = 0.5f;
            valBg.raycastTarget = true;

            var valTextGo = Rect("Text", valGo.transform);
            var valTextRect = (RectTransform)valTextGo.transform;
            valTextRect.anchorMin = Vector2.zero;
            valTextRect.anchorMax = Vector2.one;
            valTextRect.offsetMin = new Vector2(5f, 0);
            valTextRect.offsetMax = new Vector2(-5f, 0);
            var valT = Tmp(valTextGo, "", (int)LabelFontSize, TextAnchor.MiddleRight, Theme.TextMuted);
            valT.richText = false;

            var input = BuildInputField(valGo, valT);
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.caretBlinkRate = 0.6f;
            input.text = Mathf.Clamp(value, min, max).ToString(format);

            bool valHover = false, valFocus = false;
            void PaintValBox()
            {
                valBg.color = new Color(1f, 1f, 1f, valFocus ? 0.10f : valHover ? 0.09f : 0.05f);
                valBg.BorderWidth = valFocus ? 1f : 0f;
                valBg.BorderColor = Theme.ToggleOn;
                valT.color = valFocus ? Theme.Text : Theme.TextMuted;
            }
            PaintValBox();
            var valHoverH = valGo.AddComponent<HoverHandler>();
            valHoverH.OnEnter = () => { valHover = true; PaintValBox(); };
            valHoverH.OnExit = () => { valHover = false; PaintValBox(); };
            input.onSelect.AddListener(_ => { valFocus = true; PaintValBox(); });
            input.onDeselect.AddListener(_ => { valFocus = false; PaintValBox(); });

            // Track — stretches between label and value
            var trackGo = Rect("Track", row.transform);
            var trackRect = (RectTransform)trackGo.transform;
            trackRect.anchorMin = new Vector2(0, 0.5f);
            trackRect.anchorMax = new Vector2(1, 0.5f);
            trackRect.pivot = new Vector2(0.5f, 0.5f);
            trackRect.sizeDelta = new Vector2(-(labelW + rightW + 24f), 5f);
            trackRect.anchoredPosition = new Vector2((labelW - rightW) * 0.5f + 4f, 0);

            // Sharp flat Image instead of RoundedRectGraphic — the procedural geometry's AA
            // fringe blurred visibly at UI scale > 1. Flat rect with no antialiasing stays crisp.
            var trackBg = trackGo.AddComponent<Image>();
            trackBg.sprite = Theme.White;
            trackBg.color = Theme.ToggleOff;
            trackBg.raycastTarget = true;

            // Fill (left portion)
            var fillGo = Rect("Fill", trackGo.transform);
            var fillRect = (RectTransform)fillGo.transform;
            fillRect.anchorMin = new Vector2(0, 0);
            fillRect.anchorMax = new Vector2(0, 1);
            fillRect.pivot = new Vector2(0, 0.5f);
            fillRect.sizeDelta = new Vector2(0, 0);
            fillRect.anchoredPosition = Vector2.zero;
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = Theme.White;
            fill.color = Theme.ToggleOn;
            fill.raycastTarget = false;
            fillGo.AddComponent<AccentFill>();

            // Handle — anchored at a normalized X position (0..1)
            var handleGo = Rect("Handle", trackGo.transform);
            var handleRect = (RectTransform)handleGo.transform;
            handleRect.anchorMin = new Vector2(0f, 0.5f);
            handleRect.anchorMax = new Vector2(0f, 0.5f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(14f, 14f);
            var handle = handleGo.AddComponent<RoundedRectGraphic>();
            handle.Radius = 7f;
            // Tighter AA fringe — the default 1.25 looks like a soft halo when the canvas
            // is scaled up. 0.5 keeps the circle anti-aliased without the perceived blur.
            handle.AAFringe = 0.5f;
            handle.color = Theme.ToggleOn;
            handle.raycastTarget = false;
            handleGo.AddComponent<AccentFill>();

            var ctrl = trackGo.AddComponent<SliderControl>();
            ctrl.Min = min;
            ctrl.Max = max;
            ctrl.Value = Mathf.Clamp(value, min, max);
            ctrl.Track = trackRect;
            ctrl.Handle = handleRect;
            ctrl.Fill = fillRect;
            ctrl.ValueInput = input;
            ctrl.Format = format;
            ctrl.Step = step;
            ctrl.OnChange = onChange;
            ctrl.ApplyVisuals();

            // Inline undo button (left of the value field). Hidden until the value differs
            // from the baseline captured at the start of the most recent edit; clicking it
            // reverts that edit. Sits in the reserved right cluster so the row never reflows.
            var undoGo = Rect("Undo", row.transform);
            var undoRect = (RectTransform)undoGo.transform;
            undoRect.anchorMin = undoRect.anchorMax = new Vector2(1f, 0.5f);
            undoRect.pivot = new Vector2(1f, 0.5f);
            undoRect.sizeDelta = new Vector2(undoW, undoH);
            undoRect.anchoredPosition = new Vector2(-(8f + valueW + undoGap), 0f);
            var undoBg = undoGo.AddComponent<RoundedRectGraphic>();
            undoBg.Radius = 4f;
            undoBg.AAFringe = 0.5f;
            undoBg.color = new Color(Theme.ToggleOn.r, Theme.ToggleOn.g, Theme.ToggleOn.b, 0.18f);
            undoBg.raycastTarget = true;
            var undoLbl = Label(undoGo.transform, "Undo", 13, TextAnchor.MiddleCenter, Theme.Text);
            undoLbl.raycastTarget = false;
            undoGo.SetActive(false);

            float baseline = ctrl.Value;
            void RefreshUndo() => undoGo.SetActive(!Mathf.Approximately(ctrl.Value, baseline));

            ctrl.OnEditBegin = () => baseline = ctrl.Value;
            ctrl.OnAfterChange = RefreshUndo;

            ClickHandler.Attach(undoGo, () =>
            {
                if (Mathf.Approximately(ctrl.Value, baseline)) { RefreshUndo(); return; }
                ctrl.Value = baseline;
                ctrl.ApplyVisuals();
                input.text = baseline.ToString(format);
                onChange?.Invoke(baseline);
                RefreshUndo();
            });

            // Keyboard commit: parse, clamp, snap, push back through ApplyVisuals + onChange.
            float captureMin = min, captureMax = max, captureStep = step;
            string captureFormat = format;
            input.onEndEdit.AddListener(committed => {
                if (float.TryParse(committed, out float v))
                {
                    v = Mathf.Clamp(v, captureMin, captureMax);
                    if (captureStep > 0f) v = Mathf.Round(v / captureStep) * captureStep;
                    if (!Mathf.Approximately(v, ctrl.Value))
                    {
                        baseline = ctrl.Value;   // pre-edit value is the undo target
                        ctrl.Value = v;
                        onChange?.Invoke(v);
                    }
                }
                // Always reformat the displayed text — reverts garbage input or normalizes precision.
                input.text = ctrl.Value.ToString(captureFormat);
                ctrl.ApplyVisuals();
                RefreshUndo();
            });

            return row;
        }

        // ◀ / ▶ cycle selector — for picking from a finite ordered list (font name, etc.)
        public static GameObject CycleSelector(
            Transform parent,
            string label,
            IList<string> options,
            int currentIndex,
            Action<int> onChange)
        {
            SettingsSearch.Register(label);
            var row = Row(parent);
            const float labelW = 140f;
            const float btnW = 22f;

            var labGo = Rect("Label", row.transform);
            var labRect = (RectTransform)labGo.transform;
            labRect.anchorMin = new Vector2(0, 0);
            labRect.anchorMax = new Vector2(0, 1);
            labRect.pivot = new Vector2(0, 0.5f);
            labRect.sizeDelta = new Vector2(labelW, 0);
            labRect.anchoredPosition = new Vector2(8f, 0);
            var lab = Tmp(labGo, label, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

            // Right cluster: ◀ value ▶
            var rightGo = Rect("Right", row.transform);
            var rightRect = (RectTransform)rightGo.transform;
            rightRect.anchorMin = new Vector2(1, 0);
            rightRect.anchorMax = new Vector2(1, 1);
            rightRect.pivot = new Vector2(1, 0.5f);
            rightRect.sizeDelta = new Vector2(220f, 0);
            rightRect.anchoredPosition = new Vector2(-8f, 0);

            int idx = (options == null || options.Count == 0) ? -1 : Mathf.Clamp(currentIndex, 0, options.Count - 1);
            string currentText = (idx >= 0) ? options[idx] : "(none)";

            TextMeshProUGUI valueText = null;

            void MakeArrow(string glyph, float anchorX, Vector2 pivot, Action click)
            {
                var go = Rect(glyph, rightGo.transform);
                var r = (RectTransform)go.transform;
                r.anchorMin = new Vector2(anchorX, 0);
                r.anchorMax = new Vector2(anchorX, 1);
                r.pivot = pivot;
                r.sizeDelta = new Vector2(btnW, 0);
                r.anchoredPosition = Vector2.zero;
                var bg = SolidImage(go, new Color(0, 0, 0, 0));
                bg.raycastTarget = true;
                var t = labelChild(go.transform, glyph, (int)LabelFontSize, TextAnchor.MiddleCenter, Theme.TextMuted);
                HoverFill(go, bg, Theme.RowBgHover, new Color(0, 0, 0, 0));
                ClickHandler.Attach(go, click);
            }

            var valGo = Rect("Value", rightGo.transform);
            var valRect = (RectTransform)valGo.transform;
            valRect.anchorMin = new Vector2(0, 0);
            valRect.anchorMax = new Vector2(1, 1);
            valRect.offsetMin = new Vector2(btnW + 4, 0);
            valRect.offsetMax = new Vector2(-(btnW + 4), 0);
            valueText = Tmp(valGo, currentText, (int)LabelFontSize, TextAnchor.MiddleCenter, Theme.Text);

            MakeArrow("◂", 0f, new Vector2(0, 0.5f), () => {
                if (options == null || options.Count == 0) return;
                idx = (idx - 1 + options.Count) % options.Count;
                valueText.text = options[idx];
                onChange?.Invoke(idx);
            });
            MakeArrow("▸", 1f, new Vector2(1, 0.5f), () => {
                if (options == null || options.Count == 0) return;
                idx = (idx + 1) % options.Count;
                valueText.text = options[idx];
                onChange?.Invoke(idx);
            });

            return row;
        }

        // Dropdown — label + current value/chevron. Clicking opens a floating, scrollable
        // option list anchored to the row and parented to the canvas root, so it renders
        // over the page instead of unrolling inside it.
        public static GameObject Dropdown(
            Transform parent,
            string label,
            IList<string> options,
            int currentIndex,
            Action<int> onChange,
            IList<TMP_FontAsset> optionFonts = null)
        {
            SettingsSearch.Register(label);
            int idx = Mathf.Clamp(currentIndex, 0, options.Count - 1);

            var row = Row(parent);
            var bg = SolidImage(row, new Color(0, 0, 0, 0));
            bg.raycastTarget = true;
            HoverFill(row, bg, Theme.RowBgHover, new Color(0, 0, 0, 0));

            var labGo = Rect("Label", row.transform);
            var labRect = (RectTransform)labGo.transform;
            labRect.anchorMin = new Vector2(0, 0);
            labRect.anchorMax = new Vector2(0, 1);
            labRect.pivot = new Vector2(0, 0.5f);
            labRect.sizeDelta = new Vector2(140f, 0);
            labRect.anchoredPosition = new Vector2(8f, 0);
            Tmp(labGo, label, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

            var valGo = Rect("Value", row.transform);
            var valRect = (RectTransform)valGo.transform;
            valRect.anchorMin = new Vector2(0, 0);
            valRect.anchorMax = new Vector2(1, 1);
            valRect.offsetMin = new Vector2(150f, 0);
            valRect.offsetMax = new Vector2(-8f, 0);
            var val = Tmp(valGo, "", (int)LabelFontSize, TextAnchor.MiddleRight, Theme.Text);

            // Render the selected value (and each option in the list) in its own font.
            void SetVal()
            {
                val.text = (options.Count > 0 && idx >= 0 ? options[idx] : "") + "  ▾";
                val.font = optionFonts != null && idx >= 0 && idx < optionFonts.Count && optionFonts[idx] != null
                    ? optionFonts[idx] : Theme.TmpFont;
            }
            SetVal();

            ClickHandler.Attach(row, () =>
                OpenDropdownList((RectTransform)row.transform, options, idx, optionFonts, i =>
                {
                    if (i == idx) return;
                    idx = i;
                    SetVal();
                    onChange?.Invoke(i);
                }));

            return row;
        }

        // The floating list for Dropdown: full-screen transparent blocker (click-outside
        // closes, swallows stray wheel events) + a bordered panel right-aligned under the
        // trigger row (flips above it when there's no room), scrollable past ~8 options.
        private static void OpenDropdownList(RectTransform trigger, IList<string> options,
            int selectedIdx, IList<TMP_FontAsset> optionFonts, Action<int> onPick)
        {
            var root = UICore.CanvasRoot;
            if (root == null || options == null || options.Count == 0) return;
            var canvas = root.GetComponent<Canvas>();

            var blocker = Rect("DropdownBlocker", root.transform);
            var bRect = (RectTransform)blocker.transform;
            bRect.anchorMin = Vector2.zero;
            bRect.anchorMax = Vector2.one;
            bRect.offsetMin = Vector2.zero;
            bRect.offsetMax = Vector2.zero;
            var bImg = SolidImage(blocker, new Color(0, 0, 0, 0));
            bImg.raycastTarget = true;
            blocker.AddComponent<ScrollSwallower>();

            Action close = () =>
            {
                if (blocker == null) return;
                blocker.transform.SetParent(null);
                UnityEngine.Object.Destroy(blocker);
            };
            ClickHandler.Attach(blocker, close);

            const float rowH = 26f;
            const float pad = 4f;
            const float width = 240f;
            float natural = options.Count * rowH + pad * 2;
            float height = Mathf.Min(natural, 8 * rowH + pad * 2);

            var panel = Rect("Popup", blocker.transform);
            var pRect = (RectTransform)panel.transform;
            pRect.sizeDelta = new Vector2(width, height);
            SolidImage(panel, Theme.Panel);
            AddBorder(panel, Theme.PanelBorder, 1f);

            // SSO canvas → world corners are screen px; scaleFactor converts panel units.
            Vector3[] c = new Vector3[4];
            trigger.GetWorldCorners(c);
            float sf = canvas != null ? canvas.scaleFactor : 1f;
            bool openUp = c[0].y - (height + 4f) * sf < 0f;
            pRect.pivot = new Vector2(1f, openUp ? 0f : 1f);
            panel.transform.position = openUp
                ? c[2] + new Vector3(0f, 2f * sf, 0f)   // top-right corner, opening upward
                : c[3] + new Vector3(0f, -2f * sf, 0f); // bottom-right corner, downward

            var viewport = Rect("Viewport", panel.transform);
            var vRect = (RectTransform)viewport.transform;
            vRect.anchorMin = Vector2.zero;
            vRect.anchorMax = Vector2.one;
            vRect.offsetMin = new Vector2(1f, 1f);
            vRect.offsetMax = new Vector2(-1f, -1f);
            var vImg = SolidImage(viewport, new Color(0, 0, 0, 0));
            vImg.raycastTarget = true;
            viewport.AddComponent<RectMask2D>();

            var content = VGroup(viewport.transform, "Content", 0f);
            var cRect = (RectTransform)content.transform;
            cRect.anchorMin = new Vector2(0, 1);
            cRect.anchorMax = new Vector2(1, 1);
            cRect.pivot = new Vector2(0.5f, 1f);
            cRect.anchoredPosition = Vector2.zero;
            cRect.sizeDelta = Vector2.zero;
            content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 0, (int)pad, (int)pad);
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = null;
            if (natural > height)
            {
                scroll = panel.AddComponent<ScrollRect>();
                scroll.viewport = vRect;
                scroll.content = cRect;
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 24f;
            }

            for (int i = 0; i < options.Count; i++)
            {
                int oi = i;
                bool sel = oi == selectedIdx;

                var opt = Rect("Opt_" + i, content.transform);
                var optLe = opt.AddComponent<LayoutElement>();
                optLe.preferredHeight = rowH;
                optLe.minHeight = rowH;
                var optBg = SolidImage(opt, new Color(0, 0, 0, 0));
                optBg.raycastTarget = true;
                HoverFill(opt, optBg, Theme.RowBgHover, new Color(0, 0, 0, 0));

                if (sel)
                {
                    var dotGo = Rect("Dot", opt.transform);
                    var dotRect = (RectTransform)dotGo.transform;
                    dotRect.anchorMin = new Vector2(0, 0.5f);
                    dotRect.anchorMax = new Vector2(0, 0.5f);
                    dotRect.pivot = new Vector2(0, 0.5f);
                    dotRect.anchoredPosition = new Vector2(9f, 0);
                    dotRect.sizeDelta = new Vector2(6f, 6f);
                    var dot = dotGo.AddComponent<RoundedRectGraphic>();
                    dot.Radius = 3f;
                    dot.color = Theme.ToggleOn;
                    dot.raycastTarget = false;
                    dotGo.AddComponent<AccentFill>();
                }

                var t = labelChild(opt.transform, options[oi], (int)LabelFontSize,
                    TextAnchor.MiddleLeft, sel ? Theme.Text : Theme.TextMuted);
                t.rectTransform.offsetMin = new Vector2(24f, 0);
                t.rectTransform.offsetMax = new Vector2(-8f, 0);
                if (optionFonts != null && oi < optionFonts.Count && optionFonts[oi] != null)
                    t.font = optionFonts[oi];

                ClickHandler.Attach(opt, () => { onPick(oi); close(); });
            }

            // Start scrolled so the selected option is centered-ish in view.
            if (scroll != null && selectedIdx > 0)
            {
                float target = selectedIdx * rowH + pad - (height - rowH) * 0.5f;
                cRect.anchoredPosition = new Vector2(0f, Mathf.Clamp(target, 0f, natural - height));
            }
        }

        // Single-line text input — label on left, editable field on right. Fires onCommit
        // on Enter / focus loss (not on every character). Used for free-form strings like
        // the combo display label text.
        public static GameObject TextInput(
            Transform parent,
            string label,
            string initial,
            Action<string> onCommit)
        {
            SettingsSearch.Register(label);
            var row = Row(parent);
            const float labelW = 140f;

            var labGo = Rect("Label", row.transform);
            var labRect = (RectTransform)labGo.transform;
            labRect.anchorMin = new Vector2(0, 0);
            labRect.anchorMax = new Vector2(0, 1);
            labRect.pivot = new Vector2(0, 0.5f);
            labRect.sizeDelta = new Vector2(labelW, 0);
            labRect.anchoredPosition = new Vector2(8f, 0);
            var lab = Tmp(labGo, label, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

            var inGo = Rect("Input", row.transform);
            var inRect = (RectTransform)inGo.transform;
            inRect.anchorMin = new Vector2(0, 0.5f);
            inRect.anchorMax = new Vector2(1, 0.5f);
            inRect.pivot = new Vector2(0.5f, 0.5f);
            inRect.sizeDelta = new Vector2(-(labelW + 24f), 24f);
            inRect.anchoredPosition = new Vector2((labelW + 4f) * 0.5f, 0);
            var inBg = SolidImage(inGo, new Color(1, 1, 1, 0.06f));
            inBg.raycastTarget = true;

            var txtGo = Rect("Text", inGo.transform);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = new Vector2(8f, 0);
            txtRect.offsetMax = new Vector2(-8f, 0);
            var txt = Tmp(txtGo, "", (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);
            txt.richText = false;

            var input = BuildInputField(inGo, txt);
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.text = initial ?? "";

            input.onEndEdit.AddListener(s => onCommit?.Invoke(s));

            return row;
        }

        // Segmented control — label on left, fixed-width buttons on the right with one active.
        // For small enums like OverlayPosition (Left|Right).
        public static GameObject Segmented(
            Transform parent,
            string label,
            int currentIdx,
            string[] options,
            Action<int> onChange)
        {
            SettingsSearch.Register(label);
            var row = Row(parent);
            const float labelW = 140f;
            const float segW = 52f;
            const float gap = 2f;

            var labGo = Rect("Label", row.transform);
            var labRect = (RectTransform)labGo.transform;
            labRect.anchorMin = new Vector2(0, 0);
            labRect.anchorMax = new Vector2(0, 1);
            labRect.pivot = new Vector2(0, 0.5f);
            labRect.sizeDelta = new Vector2(labelW, 0);
            labRect.anchoredPosition = new Vector2(8f, 0);
            var lab = Tmp(labGo, label, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

            var rightGo = Rect("Segs", row.transform);
            var rr = (RectTransform)rightGo.transform;
            rr.anchorMin = new Vector2(1, 0);
            rr.anchorMax = new Vector2(1, 1);
            rr.pivot = new Vector2(1, 0.5f);
            float totalW = segW * options.Length + gap * (options.Length - 1);
            rr.sizeDelta = new Vector2(totalW, 0);
            rr.anchoredPosition = new Vector2(-8f, 0);

            int active = Mathf.Clamp(currentIdx, 0, options.Length - 1);
            const float segHeight = 22f;
            var bgs = new RoundedRectGraphic[options.Length];
            var accentMarks = new AccentFill[options.Length];

            for (int i = 0; i < options.Length; i++)
            {
                int captured = i;
                var seg = Rect("Seg" + i, rightGo.transform);
                var sr = (RectTransform)seg.transform;
                // Vertically center inside the row rather than stretching — gives the segments
                // a chip-like look that's clearly shorter than the row.
                sr.anchorMin = new Vector2(0, 0.5f);
                sr.anchorMax = new Vector2(0, 0.5f);
                sr.pivot = new Vector2(0, 0.5f);
                sr.sizeDelta = new Vector2(segW, segHeight);
                sr.anchoredPosition = new Vector2(i * (segW + gap), 0);

                var bg = seg.AddComponent<RoundedRectGraphic>();
                bg.Radius = 3f;
                bg.AAFringe = 0.5f;
                bg.color = i == active ? Theme.ToggleOn : Theme.ButtonBg;
                bg.raycastTarget = true;
                bgs[i] = bg;
                var af = seg.AddComponent<AccentFill>();
                af.Active = (i == active);
                accentMarks[i] = af;

                labelChild(seg.transform, options[i], (int)LabelFontSize, TextAnchor.MiddleCenter, Theme.Text);

                ClickHandler.Attach(seg, () => {
                    if (captured == active) return;
                    active = captured;
                    for (int j = 0; j < bgs.Length; j++)
                    {
                        bool on = j == active;
                        bgs[j].color = on ? Theme.ToggleOn : Theme.ButtonBg;
                        accentMarks[j].Active = on;
                    }
                    onChange?.Invoke(active);
                });
            }

            return row;
        }

        // Expandable section — like Collapsible but with no radio toggle. Just a clickable
        // header that toggles a body open/closed. Used as a structural grouping inside other
        // bodies (e.g. wrapping a gradient editor under a Color sub-header).
        public static GameObject ExpandSection(
            Transform parent,
            string title,
            Action<Transform> buildBody)
        {
            SettingsSearch.Register(title);
            var container = Rect("Section_" + title, parent);
            var clVlg = container.AddComponent<VerticalLayoutGroup>();
            clVlg.childControlWidth = true;
            clVlg.childControlHeight = true;
            clVlg.childForceExpandWidth = true;
            clVlg.childForceExpandHeight = false;
            clVlg.spacing = 0f;

            var header = Rect("Header", container.transform);
            var headerLe = header.AddComponent<LayoutElement>();
            headerLe.preferredHeight = RowHeight;
            headerLe.minHeight = RowHeight;
            var headerBg = SolidImage(header, new Color(0, 0, 0, 0));
            headerBg.raycastTarget = true;

            var arrowGo = Rect("Arrow", header.transform);
            var arrowRect = (RectTransform)arrowGo.transform;
            arrowRect.anchorMin = new Vector2(0, 0);
            arrowRect.anchorMax = new Vector2(0, 1);
            arrowRect.pivot = new Vector2(0, 0.5f);
            arrowRect.sizeDelta = new Vector2(24f, 0);
            arrowRect.anchoredPosition = new Vector2(2f, 0);
            var chevron = labelChild(arrowGo.transform, "▶", 15, TextAnchor.MiddleCenter, Theme.TextMuted);

            var titleGo = Rect("Title", header.transform);
            var titleRect = (RectTransform)titleGo.transform;
            titleRect.anchorMin = new Vector2(0, 0);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.offsetMin = new Vector2(30f, 0);
            titleRect.offsetMax = new Vector2(-8f, 0);
            var titleBg = SolidImage(titleGo, new Color(0, 0, 0, 0));
            titleBg.raycastTarget = true;
            labelChild(titleGo.transform, title, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

            var bodyGo = Rect("Body", container.transform);
            var bodyVlg = bodyGo.AddComponent<VerticalLayoutGroup>();
            bodyVlg.childControlWidth = true;
            bodyVlg.childControlHeight = true;
            bodyVlg.childForceExpandWidth = true;
            bodyVlg.childForceExpandHeight = false;
            bodyVlg.spacing = 2f;
            bodyVlg.padding = new RectOffset(24, 0, 2, 6);
            var bodyLe = bodyGo.AddComponent<LayoutElement>();
            bodyLe.preferredHeight = -1f;
            var bodyCg = bodyGo.AddComponent<CanvasGroup>();
            bodyCg.alpha = 0f;
            bodyGo.AddComponent<RectMask2D>();
            buildBody(bodyGo.transform);
            bodyGo.SetActive(false);

            var animator = bodyGo.AddComponent<ExpandAnimator>();
            animator.Body = (RectTransform)bodyGo.transform;
            animator.BodyLe = bodyLe;
            animator.BodyCg = bodyCg;
            animator.Chevron = chevron.rectTransform;

            bool expanded = false;
            Action toggleExpand = () => {
                expanded = !expanded;
                animator.Set(expanded);
            };
            ClickHandler.Attach(titleGo, toggleExpand);
            ClickHandler.Attach(arrowGo, toggleExpand);
            ClickHandler.Attach(header, toggleExpand);
            HoverFill(header, headerBg, Theme.RowBgHover, new Color(0, 0, 0, 0));

            return container;
        }

        // Gradient controls, built flat into `body` (a subpage section). Solid toggle hides
        // the Stops list and shows one ColorPicker bound to Stops[0] (matches
        // ColorGradient.Evaluate's solid mode). Add/Remove rebuilds the whole stops list so
        // the numbered headers stay correct.
        public static void GradientBody(
            Transform body,
            object gradient,
            Action onChange)
        {
            var grad = (Sapphire.ColorGradient)gradient;

            GameObject stopsSection = null;
            GameObject solidPicker = null;

            Collapsible(body, "Solid", grad.IsSolid, v => {
                grad.IsSolid = v;
                if (stopsSection != null) stopsSection.SetActive(!v);
                if (solidPicker != null) solidPicker.SetActive(v);
                onChange?.Invoke();
            }, null);

            stopsSection = VGroup(body, "Stops");
            BuildGradientStops(stopsSection.transform, grad, onChange);

            // Solid-mode picker. Bound to Stops[0] since that's what ColorGradient.Evaluate
            // returns when IsSolid is true. Creates a stop on first edit if Stops is empty.
            Color firstColor = grad.Stops.Count > 0
                ? new Color(grad.Stops[0].R, grad.Stops[0].G, grad.Stops[0].B, grad.Stops[0].A)
                : Color.white;
            solidPicker = ColorPicker(body, "Color", firstColor, true, c =>
            {
                if (grad.Stops.Count == 0)
                {
                    grad.Stops.Add(new Sapphire.ColorStop
                    {
                        Progress = 0f, R = c.r, G = c.g, B = c.b, A = c.a
                    });
                }
                else
                {
                    grad.Stops[0].R = c.r; grad.Stops[0].G = c.g;
                    grad.Stops[0].B = c.b; grad.Stops[0].A = c.a;
                }
                onChange?.Invoke();
            });

            // Initial visibility from the saved Solid flag.
            stopsSection.SetActive(!grad.IsSolid);
            solidPicker.SetActive(grad.IsSolid);

            var perfectColor = new Color(grad.PR, grad.PG, grad.PB, grad.PA);
            Collapsible(body, "Perfect color (t=1)", grad.HasPerfectColor, v =>
            {
                grad.HasPerfectColor = v;
                onChange?.Invoke();
            }, perfectBody =>
            {
                ColorPicker(perfectBody, "Color", perfectColor, true, c =>
                {
                    grad.PR = c.r; grad.PG = c.g; grad.PB = c.b; grad.PA = c.a;
                    onChange?.Invoke();
                });
            });
        }

        // Gradient stops editor: a full-width strip rendering the live gradient, one
        // draggable handle per stop (click selects, drag moves), and an inline editor
        // (Position / Color / Remove) for the selected stop. The stops list stays sorted
        // by Progress — Evaluate's lerp scan assumes ascending order.
        private static void BuildGradientStops(Transform parent, Sapphire.ColorGradient grad, Action onChange)
        {
            const int texW = 256;
            const float stripH = 26f;
            const float markerW = 14f;
            const float markerH = 32f;
            const float labelH = 14f;

            var host = Rect("GradientStrip", parent);
            var hostLe = host.AddComponent<LayoutElement>();
            float hostH = 4f + markerH + labelH;
            hostLe.preferredHeight = hostH;
            hostLe.minHeight = hostH;

            // Inset so the end markers (t=0 / t=1) don't clip at the panel edges.
            var stripGo = Rect("Strip", host.transform);
            var strip = (RectTransform)stripGo.transform;
            strip.anchorMin = new Vector2(0, 1);
            strip.anchorMax = new Vector2(1, 1);
            strip.pivot = new Vector2(0.5f, 1f);
            strip.sizeDelta = new Vector2(-2f * (8f + markerW * 0.5f), stripH);
            strip.anchoredPosition = new Vector2(0, -7f);

            var tex = new Texture2D(texW, 1, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var stripImg = stripGo.AddComponent<RawImage>();
            stripImg.texture = tex;
            stripImg.raycastTarget = false;
            AddBorder(stripGo, Theme.PanelBorder, 1f);
            stripGo.AddComponent<TextureReleaser>().Tex = tex;

            void Rebake()
            {
                var px = new Color[texW];
                for (int i = 0; i < texW; i++) px[i] = grad.Evaluate(i / (float)(texW - 1));
                tex.SetPixels(px);
                tex.Apply(false);
            }

            // Marker layer sits above the strip image + border edges.
            var markersHost = Rect("Markers", stripGo.transform);
            var mhRect = (RectTransform)markersHost.transform;
            mhRect.anchorMin = Vector2.zero;
            mhRect.anchorMax = Vector2.one;
            mhRect.offsetMin = Vector2.zero;
            mhRect.offsetMax = Vector2.zero;

            var editorHost = VGroup(parent, "StopEditor");

            Sapphire.ColorStop selected = null;
            var markers = new List<(Sapphire.ColorStop Stop, RectTransform Rect, RoundedRectGraphic G, TextMeshProUGUI Lbl)>();
            SliderControl posCtrl = null;
            Action rebuildMarkers = null, rebuildEditor = null;

            void SortStops() => grad.Stops.Sort((a, b) => a.Progress.CompareTo(b.Progress));

            // Reposition + repaint every live marker from its stop (no rebuild — safe to
            // call mid-drag, when destroying the dragged handle would kill the gesture).
            void RepaintMarkers()
            {
                foreach (var m in markers)
                {
                    float t = Mathf.Clamp01(m.Stop.Progress);
                    m.Rect.anchorMin = m.Rect.anchorMax = new Vector2(t, 0.5f);
                    m.Lbl.text = m.Stop.Progress.ToString("0.00");
                    m.G.color = new Color(m.Stop.R, m.Stop.G, m.Stop.B, 1f); // opaque swatch
                    bool sel = m.Stop == selected;
                    m.G.BorderColor = sel ? Theme.ToggleOn : new Color(1f, 1f, 1f, 0.35f);
                    m.Lbl.color = sel ? Theme.Text : Theme.TextMuted;
                }
            }

            rebuildMarkers = () =>
            {
                for (int i = markersHost.transform.childCount - 1; i >= 0; i--)
                {
                    var c = markersHost.transform.GetChild(i);
                    c.SetParent(null);
                    UnityEngine.Object.Destroy(c.gameObject);
                }
                markers.Clear();

                foreach (var s in grad.Stops)
                {
                    var stop = s;
                    var mGo = Rect("Stop", markersHost.transform);
                    var mRect = (RectTransform)mGo.transform;
                    mRect.pivot = new Vector2(0.5f, 0.5f);
                    mRect.sizeDelta = new Vector2(markerW, markerH);
                    var g = mGo.AddComponent<RoundedRectGraphic>();
                    g.Radius = 4f;
                    g.AAFringe = 0.5f;
                    g.BorderWidth = 1.5f;
                    g.raycastTarget = true;

                    var lblGo = Rect("Pos", mGo.transform);
                    var lblRect = (RectTransform)lblGo.transform;
                    lblRect.anchorMin = new Vector2(0.5f, 0);
                    lblRect.anchorMax = new Vector2(0.5f, 0);
                    lblRect.pivot = new Vector2(0.5f, 1f);
                    lblRect.anchoredPosition = new Vector2(0, -1f);
                    lblRect.sizeDelta = new Vector2(44f, labelH);
                    var lbl = Tmp(lblGo, "", 11, TextAnchor.UpperCenter, Theme.TextMuted);

                    var h = mGo.AddComponent<GradientStopHandle>();
                    h.Strip = strip;
                    h.OnSelect = () =>
                    {
                        if (selected == stop) return;
                        selected = stop;
                        RepaintMarkers();
                        rebuildEditor();
                    };
                    h.OnMove = t =>
                    {
                        stop.Progress = t;
                        SortStops();
                        RepaintMarkers();
                        Rebake();
                        if (posCtrl != null && selected == stop)
                        {
                            posCtrl.Value = t;
                            posCtrl.ApplyVisuals();
                        }
                        onChange?.Invoke();
                    };

                    markers.Add((stop, mRect, g, lbl));
                }
                RepaintMarkers();
            };

            rebuildEditor = () =>
            {
                for (int i = editorHost.transform.childCount - 1; i >= 0; i--)
                {
                    var c = editorHost.transform.GetChild(i);
                    c.SetParent(null);
                    UnityEngine.Object.Destroy(c.gameObject);
                }
                posCtrl = null;
                if (selected != null && !grad.Stops.Contains(selected)) selected = null;

                if (selected == null)
                {
                    var hintRow = Row(editorHost.transform, 24f);
                    var hint = Label(hintRow.transform,
                        grad.Stops.Count == 0 ? "No stops yet — add one below" : "Click a stop on the strip to edit it",
                        (int)LabelFontSize - 2, TextAnchor.MiddleLeft, Theme.TextMuted);
                    hint.rectTransform.offsetMin = new Vector2(8f, 0);
                    return;
                }

                var stop = selected;
                var posRow = Slider(editorHost.transform, "Position", stop.Progress, 0f, 1f, v =>
                {
                    stop.Progress = v;
                    SortStops();
                    RepaintMarkers();
                    Rebake();
                    onChange?.Invoke();
                }, "0.00");
                posCtrl = posRow.GetComponentInChildren<SliderControl>();

                ColorPicker(editorHost.transform, "Color",
                    new Color(stop.R, stop.G, stop.B, stop.A), true, c =>
                    {
                        stop.R = c.r; stop.G = c.g; stop.B = c.b; stop.A = c.a;
                        RepaintMarkers();
                        Rebake();
                        onChange?.Invoke();
                    });

                Button(editorHost.transform, "Remove this stop", () =>
                {
                    grad.Stops.Remove(stop);
                    selected = null;
                    Rebake();
                    rebuildMarkers();
                    rebuildEditor();
                    onChange?.Invoke();
                });
            };

            Button(parent, "+ Add stop", () =>
            {
                // Seed with the gradient's current end color so adding a stop doesn't
                // visibly change the gradient.
                var end = grad.Evaluate(1f);
                var ns = new Sapphire.ColorStop { Progress = 1f, R = end.r, G = end.g, B = end.b, A = end.a };
                grad.Stops.Add(ns);
                SortStops();
                selected = ns;
                Rebake();
                rebuildMarkers();
                rebuildEditor();
                onChange?.Invoke();
            });

            SortStops();
            Rebake();
            rebuildMarkers();
            rebuildEditor();
        }

        // Color picker. Header (clickable to expand): arrow + label + HEX + swatch preview.
        // Body: editable HEX field + R/G/B(/A) sliders. Each control round-trips through a
        // shared RefreshDisplay so dragging a slider updates HEX/swatch and vice versa.
        public static GameObject ColorPicker(
            Transform parent,
            string label,
            Color initial,
            bool hasAlpha,
            Action<Color> onChange)
        {
            SettingsSearch.Register(label);
            var container = Rect("ColorPicker", parent);
            var clVlg = container.AddComponent<VerticalLayoutGroup>();
            clVlg.childControlWidth = true;
            clVlg.childControlHeight = true;
            clVlg.childForceExpandWidth = true;
            clVlg.childForceExpandHeight = false;
            clVlg.spacing = 0f;

            Color current = initial;
            bool expanded = false;

            // Header
            var header = Rect("Header", container.transform);
            var headerLe = header.AddComponent<LayoutElement>();
            headerLe.preferredHeight = RowHeight;
            headerLe.minHeight = RowHeight;
            var headerBg = SolidImage(header, new Color(0, 0, 0, 0));
            headerBg.raycastTarget = true;

            // Chevron
            var arrowGo = Rect("Arrow", header.transform);
            var arrowRect = (RectTransform)arrowGo.transform;
            arrowRect.anchorMin = new Vector2(0, 0);
            arrowRect.anchorMax = new Vector2(0, 1);
            arrowRect.pivot = new Vector2(0, 0.5f);
            arrowRect.sizeDelta = new Vector2(24f, 0);
            arrowRect.anchoredPosition = new Vector2(2f, 0);
            var chevron = labelChild(arrowGo.transform, "▶", 15, TextAnchor.MiddleCenter, Theme.TextMuted);

            // Label
            var titleGo = Rect("Title", header.transform);
            var titleRect = (RectTransform)titleGo.transform;
            titleRect.anchorMin = new Vector2(0, 0);
            titleRect.anchorMax = new Vector2(0, 1);
            titleRect.pivot = new Vector2(0, 0.5f);
            titleRect.sizeDelta = new Vector2(180f, 0);
            titleRect.anchoredPosition = new Vector2(30f, 0);
            labelChild(titleGo.transform, label, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

            // Swatch preview (right)
            const float swatchSize = 22f;
            var swatchGo = Rect("Swatch", header.transform);
            var swatchRect = (RectTransform)swatchGo.transform;
            swatchRect.anchorMin = new Vector2(1, 0.5f);
            swatchRect.anchorMax = new Vector2(1, 0.5f);
            swatchRect.pivot = new Vector2(1, 0.5f);
            swatchRect.anchoredPosition = new Vector2(-8f, 0);
            swatchRect.sizeDelta = new Vector2(swatchSize, swatchSize);
            var swatchImg = swatchGo.AddComponent<Image>();
            swatchImg.sprite = Theme.White;
            swatchImg.color = current;
            swatchImg.raycastTarget = false;

            // HEX text (right of label, left of swatch)
            var hexGo = Rect("Hex", header.transform);
            var hexRect = (RectTransform)hexGo.transform;
            hexRect.anchorMin = new Vector2(1, 0);
            hexRect.anchorMax = new Vector2(1, 1);
            hexRect.pivot = new Vector2(1, 0.5f);
            hexRect.sizeDelta = new Vector2(110f, 0);
            hexRect.anchoredPosition = new Vector2(-(swatchSize + 16f), 0);
            var hexText = Tmp(hexGo, "", (int)LabelFontSize, TextAnchor.MiddleRight, Theme.TextMuted);

            // Body
            var bodyGo = Rect("Body", container.transform);
            var bodyVlg = bodyGo.AddComponent<VerticalLayoutGroup>();
            bodyVlg.childControlWidth = true;
            bodyVlg.childControlHeight = true;
            bodyVlg.childForceExpandWidth = true;
            bodyVlg.childForceExpandHeight = false;
            bodyVlg.spacing = 2f;
            bodyVlg.padding = new RectOffset(24, 0, 2, 6);
            var bodyLe = bodyGo.AddComponent<LayoutElement>();
            bodyLe.preferredHeight = -1f;
            var bodyCg = bodyGo.AddComponent<CanvasGroup>();
            bodyCg.alpha = 0f;
            bodyGo.AddComponent<RectMask2D>();

            // --- HEX input row ---
            TMP_InputField hexInput = null;
            {
                var hexRow = Row(bodyGo.transform);
                const float labelW = 140f;

                var lblGo = Rect("Lbl", hexRow.transform);
                var lblRect = (RectTransform)lblGo.transform;
                lblRect.anchorMin = new Vector2(0, 0);
                lblRect.anchorMax = new Vector2(0, 1);
                lblRect.pivot = new Vector2(0, 0.5f);
                lblRect.sizeDelta = new Vector2(labelW, 0);
                lblRect.anchoredPosition = new Vector2(8f, 0);
                labelChild(lblGo.transform, "HEX", (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

                var inGo = Rect("HexInput", hexRow.transform);
                var inRect = (RectTransform)inGo.transform;
                inRect.anchorMin = new Vector2(0, 0.5f);
                inRect.anchorMax = new Vector2(1, 0.5f);
                inRect.pivot = new Vector2(0.5f, 0.5f);
                inRect.sizeDelta = new Vector2(-(labelW + 24f), 24f);
                inRect.anchoredPosition = new Vector2((labelW + 4f) * 0.5f, 0);
                var inBg = SolidImage(inGo, new Color(1, 1, 1, 0.06f));
                inBg.raycastTarget = true;

                var inTxtGo = Rect("Text", inGo.transform);
                var inTxtRect = (RectTransform)inTxtGo.transform;
                inTxtRect.anchorMin = Vector2.zero;
                inTxtRect.anchorMax = Vector2.one;
                inTxtRect.offsetMin = new Vector2(8f, 0);
                inTxtRect.offsetMax = new Vector2(-8f, 0);
                var inTxt = Tmp(inTxtGo, "", (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);
                inTxt.richText = false;

                hexInput = BuildInputField(inGo, inTxt);
                hexInput.contentType = TMP_InputField.ContentType.Alphanumeric;
                hexInput.lineType = TMP_InputField.LineType.SingleLine;
                hexInput.characterLimit = 9;
            }

            // --- RGB(A) sliders ---
            // We use the existing Slider factory but bypass its individual onChange:
            // every channel routes through a single ApplyComponent → RefreshDisplay → notify.
            SliderControl rCtrl = null, gCtrl = null, bCtrl = null, aCtrl = null;

            Action refresh = null;
            refresh = () => {
                swatchImg.color = current;
                string hex = "#" + (hasAlpha
                    ? ColorUtility.ToHtmlStringRGBA(current)
                    : ColorUtility.ToHtmlStringRGB(current));
                hexText.text = hex;
                if (hexInput != null && !hexInput.isFocused) hexInput.text = hex;
                if (rCtrl != null) { rCtrl.Value = current.r * 255f; rCtrl.ApplyVisuals(); }
                if (gCtrl != null) { gCtrl.Value = current.g * 255f; gCtrl.ApplyVisuals(); }
                if (bCtrl != null) { bCtrl.Value = current.b * 255f; bCtrl.ApplyVisuals(); }
                if (aCtrl != null) { aCtrl.Value = current.a * 255f; aCtrl.ApplyVisuals(); }
            };

            GameObject MakeChannel(string ch, Func<float> get, Action<float> set)
            {
                var row = Slider(bodyGo.transform, ch, get() * 255f, 0f, 255f, v => {
                    set(Mathf.Clamp01(v / 255f));
                    refresh();
                    onChange?.Invoke(current);
                }, "0", 1f);
                return row;
            }

            rCtrl = MakeChannel("R", () => current.r, x => current.r = x).GetComponentInChildren<SliderControl>();
            gCtrl = MakeChannel("G", () => current.g, x => current.g = x).GetComponentInChildren<SliderControl>();
            bCtrl = MakeChannel("B", () => current.b, x => current.b = x).GetComponentInChildren<SliderControl>();
            if (hasAlpha)
                aCtrl = MakeChannel("A", () => current.a, x => current.a = x).GetComponentInChildren<SliderControl>();

            // HEX commit — parse, apply, refresh + notify.
            hexInput.onEndEdit.AddListener(s => {
                string parsed = s.Trim();
                if (!parsed.StartsWith("#")) parsed = "#" + parsed;
                if (ColorUtility.TryParseHtmlString(parsed, out Color parsedColor))
                {
                    if (!hasAlpha) parsedColor.a = 1f;
                    current = parsedColor;
                    refresh();
                    onChange?.Invoke(current);
                }
                else
                {
                    // Revert display on parse failure
                    refresh();
                }
            });

            // Animator on the body, like Collapsible
            var animator = bodyGo.AddComponent<ExpandAnimator>();
            animator.Body = (RectTransform)bodyGo.transform;
            animator.BodyLe = bodyLe;
            animator.BodyCg = bodyCg;
            animator.Chevron = chevron.rectTransform;
            bodyGo.SetActive(false);

            Action toggleExpand = () => {
                expanded = !expanded;
                animator.Set(expanded);
            };
            ClickHandler.Attach(titleGo, toggleExpand);
            ClickHandler.Attach(arrowGo, toggleExpand);
            ClickHandler.Attach(header, toggleExpand);
            HoverFill(header, headerBg, Theme.RowBgHover, new Color(0, 0, 0, 0));

            refresh(); // initial paint
            return container;
        }

        // Accent color preset swatches — circular, with a ring around the selected one.
        public static GameObject AccentSwatches(
            Transform parent,
            string label,
            Color[] options,
            Color current,
            Action<Color> onChange)
        {
            var row = Row(parent, RowHeight + 8f);
            const float labelW = 140f;
            const float swatchSize = 18f;
            const float gap = 6f;

            var labGo = Rect("Label", row.transform);
            var labRect = (RectTransform)labGo.transform;
            labRect.anchorMin = new Vector2(0, 0);
            labRect.anchorMax = new Vector2(0, 1);
            labRect.pivot = new Vector2(0, 0.5f);
            labRect.sizeDelta = new Vector2(labelW, 0);
            labRect.anchoredPosition = new Vector2(8f, 0);
            var lab = Tmp(labGo, label, (int)LabelFontSize, TextAnchor.MiddleLeft, Theme.Text);

            var rightGo = Rect("Swatches", row.transform);
            var rightRect = (RectTransform)rightGo.transform;
            rightRect.anchorMin = new Vector2(0, 0);
            rightRect.anchorMax = new Vector2(1, 1);
            rightRect.offsetMin = new Vector2(labelW + 8f, 0);
            rightRect.offsetMax = new Vector2(-8f, 0);

            var swatchObjs = new RoundedRectGraphic[options.Length];
            var ringObjs = new GameObject[options.Length];

            int selectedIdx = -1;
            for (int i = 0; i < options.Length; i++)
            {
                if (Mathf.Approximately(options[i].r, current.r)
                 && Mathf.Approximately(options[i].g, current.g)
                 && Mathf.Approximately(options[i].b, current.b))
                {
                    selectedIdx = i;
                    break;
                }
            }

            for (int i = 0; i < options.Length; i++)
            {
                int captured = i;
                var swGo = Rect("Sw" + i, rightGo.transform);
                var swRect = (RectTransform)swGo.transform;
                swRect.anchorMin = new Vector2(0, 0.5f);
                swRect.anchorMax = new Vector2(0, 0.5f);
                swRect.pivot = new Vector2(0, 0.5f);
                swRect.sizeDelta = new Vector2(swatchSize, swatchSize);
                swRect.anchoredPosition = new Vector2(i * (swatchSize + gap), 0);
                var swImg = swGo.AddComponent<RoundedRectGraphic>();
                swImg.Radius = swatchSize * 0.5f;
                swImg.color = options[i];
                swImg.raycastTarget = true;
                swatchObjs[i] = swImg;

                // Selection ring
                var ringGo = Rect("Ring", swGo.transform);
                var ringRect = (RectTransform)ringGo.transform;
                ringRect.anchorMin = Vector2.zero;
                ringRect.anchorMax = Vector2.one;
                ringRect.offsetMin = new Vector2(-3f, -3f);
                ringRect.offsetMax = new Vector2(3f, 3f);
                var ringG = ringGo.AddComponent<RoundedRectGraphic>();
                ringG.Radius = (swatchSize + 6f) * 0.5f;
                ringG.BorderWidth = 1.5f;
                ringG.BorderColor = Theme.Text;
                ringG.color = new Color(0, 0, 0, 0);
                ringG.raycastTarget = false;
                ringGo.SetActive(i == selectedIdx);
                ringObjs[i] = ringGo;

                ClickHandler.Attach(swGo, () => {
                    for (int j = 0; j < ringObjs.Length; j++) ringObjs[j].SetActive(j == captured);
                    onChange?.Invoke(options[captured]);
                });
            }

            return row;
        }

        // Destructive-action button with two-click confirmation. First click arms it (label
        // → "Confirm: {label}?"); second click within the timeout fires onConfirm.
        // Auto-reverts after 3s on its own timer if not confirmed.
        public static GameObject DangerButton(Transform parent, string label, Action onConfirm)
        {
            SettingsSearch.Register(label);
            var row = Row(parent);
            var bg = SolidImage(row, Theme.DangerBg);
            bg.raycastTarget = true;

            var t = Label(row.transform, label, (int)LabelFontSize, TextAnchor.MiddleCenter, Theme.Text);
            t.rectTransform.offsetMin = new Vector2(8f, 0f);
            t.rectTransform.offsetMax = new Vector2(-8f, 0f);

            HoverFill(row, bg, Theme.DangerHover, Theme.DangerBg);

            string originalLabel = label;
            bool armed = false;
            var state = row.AddComponent<DangerButtonState>();
            Action revert = () =>
            {
                armed = false;
                t.text = originalLabel;
                bg.color = Theme.DangerBg;
            };
            state.OnTimeout = revert;

            ClickHandler.Attach(row, () =>
            {
                if (!armed)
                {
                    armed = true;
                    t.text = "Click again to confirm";
                    bg.color = Theme.DangerArmed;
                    state.StartTimer(3f);
                }
                else
                {
                    state.CancelTimer();
                    revert();
                    onConfirm?.Invoke();
                }
            });

            return row;
        }

        public static GameObject Spacer(Transform parent, float height = SectionGap)
        {
            var go = Rect("Spacer", parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return go;
        }

        // 1px hairline border on all four sides — sharp aesthetic.
        public static void AddBorder(GameObject parent, Color color, float thickness = 1f)
        {
            void Edge(string n, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
            {
                var go = Rect(n, parent.transform);
                var r = (RectTransform)go.transform;
                r.anchorMin = aMin;
                r.anchorMax = aMax;
                r.offsetMin = offMin;
                r.offsetMax = offMax;
                var img = go.AddComponent<Image>();
                img.sprite = Theme.White;
                img.color = color;
                img.raycastTarget = false;
            }
            Edge("BTop",    new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -thickness), new Vector2(0, 0));
            Edge("BBottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0),          new Vector2(0, thickness));
            Edge("BLeft",   new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0),          new Vector2(thickness, 0));
            Edge("BRight",  new Vector2(1, 0), new Vector2(1, 1), new Vector2(-thickness, 0), new Vector2(0, 0));
        }

        private static void HoverFill(GameObject obj, Image target, Color hover, Color rest)
        {
            // Use HoverHandler, NOT EventTrigger — EventTrigger implements IScrollHandler and
            // absorbs the mouse-wheel event on every GameObject it's on, breaking ScrollRect
            // bubbling whenever the cursor is over a hover-tinted widget.
            var h = obj.GetComponent<HoverHandler>() ?? obj.AddComponent<HoverHandler>();
            h.OnEnter = () => target.color = hover;
            h.OnExit = () => target.color = rest;
        }
    }

    // Eats wheel events. Lives on the dropdown blocker so scrolling outside the floating
    // list doesn't scroll the page underneath it. (Everywhere else we deliberately AVOID
    // IScrollHandler so events bubble to the page ScrollRect — see HoverHandler.)
    internal class ScrollSwallower : MonoBehaviour, IScrollHandler
    {
        public void OnScroll(PointerEventData e) { }
    }

    // Gradient-strip stop handle: press selects, drag maps the pointer to a normalized
    // position across the strip. Implementing IDragHandler also keeps the ScrollRect
    // from stealing the gesture.
    internal class GradientStopHandle : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public RectTransform Strip;
        public Action OnSelect;
        public Action<float> OnMove;

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            OnSelect?.Invoke();
        }

        public void OnDrag(PointerEventData e)
        {
            if (Strip == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Strip, e.position, e.pressEventCamera, out Vector2 local))
                return;
            float w = Strip.rect.width;
            if (w <= 0f) return;
            OnMove?.Invoke(Mathf.Clamp01(local.x / w + Strip.pivot.x));
        }
    }

    // Frees a runtime-baked Texture2D when its host object is destroyed (gradient strips
    // live on subpages that are torn down on every pop).
    internal class TextureReleaser : MonoBehaviour
    {
        public Texture2D Tex;
        private void OnDestroy() { if (Tex != null) UnityEngine.Object.Destroy(Tex); }
    }

    // Hover state notifier. Implements only the pointer enter/exit interfaces — does NOT
    // implement IScrollHandler, so mouse-wheel events bubble through to a parent ScrollRect.
    // (EventTrigger absorbs scroll events even when no scroll trigger is wired.)
    internal class HoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action OnEnter;
        public Action OnExit;
        public void OnPointerEnter(PointerEventData e) { OnEnter?.Invoke(); }
        public void OnPointerExit(PointerEventData e) { OnExit?.Invoke(); }
    }

    // Lightweight click receiver — no Selectable state machine, no graphic transitions.
    // Distinguishes left vs right click via PointerEventData.button.
    internal class ClickHandler : MonoBehaviour, IPointerClickHandler
    {
        public Action OnClick;       // left click
        public Action OnRightClick;  // right click (mouse2)

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) OnRightClick?.Invoke();
            else if (e.button == PointerEventData.InputButton.Left) OnClick?.Invoke();
        }

        public static ClickHandler Attach(GameObject go, Action onClick)
        {
            var c = go.GetComponent<ClickHandler>() ?? go.AddComponent<ClickHandler>();
            c.OnClick = onClick;
            return c;
        }
    }

    // Marker components for accent-tinted graphics. Theme.ApplyAccent only repaints graphics
    // that carry these — eliminates the false-positive matching that corrupted swatch presets.
    internal class AccentFill : MonoBehaviour { public bool Active = true; }
    internal class AccentBorder : MonoBehaviour { public bool Active = true; }

    // Animates a Collapsible's body open/closed: body height (via LayoutElement.preferredHeight
    // override of the natural VLG height), alpha (CanvasGroup), and chevron rotation 0→90°.
    // RectMask2D on the body clips children that overflow while height < natural.
    internal class ExpandAnimator : MonoBehaviour
    {
        public RectTransform Body;
        public LayoutElement BodyLe;
        public CanvasGroup BodyCg;
        public RectTransform Chevron;
        public float Duration = 0.18f;

        private float _t;
        private bool _expanding;
        private bool _running;
        private float _naturalH;

        public void Set(bool expanded)
        {
            if (!Body.gameObject.activeSelf) Body.gameObject.SetActive(true);
            // Clear the height override and force a layout pass so we can read the natural,
            // VLG-derived preferred height from the children. Then re-apply current _t.
            if (BodyLe != null) BodyLe.preferredHeight = -1f;
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(Body);
            _naturalH = UnityEngine.UI.LayoutUtility.GetPreferredHeight(Body);
            _expanding = expanded;
            if (BodyLe != null) BodyLe.preferredHeight = _t * _naturalH;
            if (BodyCg != null) BodyCg.alpha = _t;
            _running = true;
            enabled = true;
        }

        private void Update()
        {
            if (!_running) return;
            float dir = _expanding ? 1f : -1f;
            _t = Mathf.Clamp01(_t + dir * Time.unscaledDeltaTime / Duration);
            float eased = EaseOutCubic(_t);

            if (BodyLe != null) BodyLe.preferredHeight = eased * _naturalH;
            if (BodyCg != null) BodyCg.alpha = eased;
            if (Chevron != null) Chevron.localRotation = Quaternion.Euler(0f, 0f, -90f * eased);

            if (_expanding && _t >= 1f)
            {
                _running = false;
                // Release the height override so future child changes can grow the body naturally.
                if (BodyLe != null) BodyLe.preferredHeight = -1f;
            }
            else if (!_expanding && _t <= 0f)
            {
                _running = false;
                Body.gameObject.SetActive(false);
            }
        }

        private static float EaseOutCubic(float t) { return 1f - Mathf.Pow(1f - t, 3f); }
    }

    // Auto-revert timer for DangerButton. Counts unscaled seconds; fires OnTimeout when
    // armed long enough without confirmation. CancelTimer disarms it on successful confirm.
    internal class DangerButtonState : MonoBehaviour
    {
        public Action OnTimeout;
        private float _expireAt;
        private bool _running;

        public void StartTimer(float seconds)
        {
            _expireAt = Time.unscaledTime + seconds;
            _running = true;
        }

        public void CancelTimer() { _running = false; }

        private void Update()
        {
            if (!_running) return;
            if (Time.unscaledTime >= _expireAt)
            {
                _running = false;
                OnTimeout?.Invoke();
            }
        }
    }

    // Click-and-drag handler living on the slider track. Pointer position → normalized t → value.
    internal class SliderControl : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public float Min, Max, Value;
        public RectTransform Track;
        public RectTransform Handle;
        public RectTransform Fill;
        public TMP_InputField ValueInput;
        public string Format = "0.00";
        public float Step = 0f;
        public Action<float> OnChange;
        public Action OnEditBegin;    // gesture start, before the first value change (undo baseline)
        public Action OnAfterChange;  // after Value changed + OnChange invoked (refresh undo button)

        public void OnPointerDown(PointerEventData e) { OnEditBegin?.Invoke(); UpdateFromPointer(e); }
        public void OnDrag(PointerEventData e) { UpdateFromPointer(e); }

        private void UpdateFromPointer(PointerEventData e)
        {
            if (Track == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Track, e.position, e.pressEventCamera, out Vector2 local))
                return;
            float w = Track.rect.width;
            if (w <= 0f) return;
            // Track pivot is centered (0.5, 0.5), so local.x ∈ [-w/2, w/2]
            float t = Mathf.Clamp01((local.x + w * 0.5f) / w);
            float newValue = Mathf.Lerp(Min, Max, t);
            if (Step > 0f) newValue = Mathf.Round(newValue / Step) * Step;
            if (Mathf.Approximately(newValue, Value)) return;
            Value = newValue;
            ApplyVisuals();
            OnChange?.Invoke(Value);
            OnAfterChange?.Invoke();
        }

        public void ApplyVisuals()
        {
            float t = (Max > Min) ? Mathf.InverseLerp(Min, Max, Value) : 0f;
            if (Handle != null)
            {
                Handle.anchorMin = new Vector2(t, 0.5f);
                Handle.anchorMax = new Vector2(t, 0.5f);
                Handle.anchoredPosition = Vector2.zero;
            }
            if (Fill != null)
            {
                Fill.anchorMax = new Vector2(t, 1f);
            }
            // Don't overwrite while the user is mid-typing — the onEndEdit handler will
            // re-format on commit. Otherwise dragging the slider while focused would clobber
            // the typed text mid-character.
            if (ValueInput != null && !ValueInput.isFocused)
            {
                string formatted = Value.ToString(Format);
                if (ValueInput.text != formatted) ValueInput.text = formatted;
            }
        }
    }
}

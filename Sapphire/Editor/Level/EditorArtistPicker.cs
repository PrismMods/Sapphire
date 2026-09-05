using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Artist autocomplete for the Level tab's `artist` field — the game's own dropdown
       (InspectorPanel.UpdateArtistPopup) rebuilt over Sapphire's row: type, see the matching
       verified artists with their approval badge, click one to fill the field.

       The list is EditorWebServices.artists, a STATIC the game only fetches when ITS inspector
       opens Level Settings. Sapphire never opens that panel, so the array stayed null and
       scnEditor.ApprovalLevelForArtist threw on it — which is why the approval chip looked
       unported. EnsureLoaded kicks the same fetch (the game caches it to verified_artists.json,
       so a later offline session still resolves). */
    internal static class EditorArtistPicker
    {
        private const int MaxRows = 8;
        private const float RowH = 26f, Pad = 4f;

        private static GameObject _canvasGo, _popupGo;
        private static TMP_InputField _field;
        private static Action<string> _commit;
        private static string _shown;      // search string the popup was built for
        private static bool _loadKicked, _loading;
        private static float _loadStart;
        private static TextMeshProUGUI _loadTmp;   // non-null while the popup shows the spinner

        /* Kick the artist fetch once per session. onReady fires on the Unity thread when the
           array lands, so the caller can redraw a row that depends on it. */
        internal static void EnsureLoaded(Action onReady)
        {
            if (_loadKicked) return;
            try
            {
                if (EditorWebServices.artists != null) { _loadKicked = true; return; }
                var ed = scnEditor.instance;
                if (ed == null || ed.webServices == null) return;   // retry next build
                _loadKicked = true;
                _loading = true;
                _loadStart = Time.realtimeSinceStartup;
                ed.webServices.LoadAllArtists(() =>
                {
                    _loading = false;
                    try { onReady?.Invoke(); } catch { }
                });
            }
            catch (Exception ex)
            {
                _loading = false;
                SapphireLog.Log("Artists: load failed: " + ex.Message);
            }
        }

        // Called per row build; the previous field is dead by then, so rebind unconditionally.
        internal static void Bind(TMP_InputField field, Action<string> commit)
        {
            if (field == null) return;
            Close();
            _field = field;
            _commit = commit;
            field.onValueChanged.AddListener(Refresh);
        }

        internal static void Tick()
        {
            /* The fetch can end without our callback running (the game swaps in its cached
               backup array on some failure paths), and a hung request must not spin the
               spinner forever. */
            if (_loading && (EditorWebServices.artists != null
                             || Time.realtimeSinceStartup - _loadStart > 20f))
                _loading = false;

            if (_popupGo == null) return;
            if (_field == null || !_field.isActiveAndEnabled) { Close(); return; }
            // A click anywhere but the popup or the field itself dismisses it.
            if (Input.GetMouseButtonDown(0)
                && !Over(_popupGo) && !Over(_field.gameObject)) { Close(); return; }

            if (_loadTmp != null)
            {
                if (_loading)
                {
                    int dots = (int)(Time.realtimeSinceStartup * 2.5f) % 4;
                    _loadTmp.text = Loc.T("Fetching artist list") + new string('.', dots);
                }
                else
                {
                    // List arrived (or gave up) while the spinner was up — redraw as results.
                    string search = _shown; _shown = null;
                    Close();
                    if (_field != null) Refresh(_field.text ?? search);
                    return;
                }
            }
            Place();
        }

        internal static void Dispose()
        {
            Close();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _field = null; _commit = null; _loadKicked = false;
        }

        private static bool Over(GameObject go)
        {
            try
            {
                return go != null && RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)go.transform, Input.mousePosition, null);
            }
            catch { return false; }
        }

        internal static void Close()
        {
            if (_popupGo != null) UnityEngine.Object.Destroy(_popupGo);
            _popupGo = null; _shown = null; _loadTmp = null;
        }

        // ── list ────────────────────────────────────────────────────────────

        private static readonly List<ArtistData> _hits = new List<ArtistData>();

        private static void Refresh(string raw)
        {
            string search = (raw ?? "").Trim().ToLower();
            if (search == _shown) return;
            Close();
            if (search.Length == 0) return;
            var all = EditorWebServices.artists;
            if (all == null)
            {
                EnsureLoaded(null);
                if (_loading) { _shown = search; BuildLoading(); Place(); }
                return;
            }

            _hits.Clear();
            for (int i = 0; i < all.Length && _hits.Count < MaxRows; i++)
            {
                var a = all[i];
                // Same filter as the game's: unreviewed (Pending) artists are never offered.
                if (a == null || a.approvalLevel == ApprovalLevel.Pending || a.name == null) continue;
                if (a.name.ToLower().Contains(search)) _hits.Add(a);
            }
            if (_hits.Count == 0) return;
            _shown = search;
            Build();
            Place();
        }

        private static void EnsureCanvas()
        {
            if (_canvasGo != null) return;
            _canvasGo = new GameObject("SapphireArtistPicker", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 948;   // above every panel, below the help overlay (949)
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
        }

        private static void Build()
        {
            EnsureCanvas();
            float w = FieldWidth();
            float h = _hits.Count * RowH + Pad * 2f;

            _popupGo = new GameObject("Popup", typeof(RectTransform));
            _popupGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_popupGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.sizeDelta = new Vector2(w, h);
            var bg = _popupGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(0.05f, 0.05f, 0.07f, 0.98f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.16f);
            bg.raycastTarget = true;

            float y = -Pad;
            foreach (var a in _hits) { Row(a, w, y); y -= RowH; }
        }

        // One-row popup while the fetch is in flight; Tick animates the dots and swaps it for
        // the results (or drops it) once the array lands.
        private static void BuildLoading()
        {
            EnsureCanvas();
            float w = FieldWidth();
            _popupGo = new GameObject("Popup", typeof(RectTransform));
            _popupGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_popupGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.sizeDelta = new Vector2(w, RowH + Pad * 2f);
            var bg = _popupGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(0.05f, 0.05f, 0.07f, 0.98f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.16f);
            bg.raycastTarget = true;

            var tGo = new GameObject("L", typeof(RectTransform));
            tGo.transform.SetParent(_popupGo.transform, false);
            var tr = (RectTransform)tGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(8f, 0f); tr.offsetMax = new Vector2(-8f, 0f);
            _loadTmp = UIBuilder.Tmp(tGo, Loc.T("Fetching artist list"), 12f,
                TextAnchor.MiddleLeft, Theme.TextMuted);
        }

        private static void Row(ArtistData a, float w, float y)
        {
            var go = new GameObject("A", typeof(RectTransform));
            go.transform.SetParent(_popupGo.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(0f, y);
            r.sizeDelta = new Vector2(w, RowH);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 4f;
            bg.color = new Color(1f, 1f, 1f, 0.03f);
            bg.raycastTarget = true;

            // Plate and label are SEPARATE GameObjects: a RoundedRectGraphic and a TMP on one GO
            // share a single CanvasRenderer and fight over it — that is what blew the badge up
            // to a screen-sized green block and starved the name label of width.
            var badgeGo = new GameObject("B", typeof(RectTransform));
            badgeGo.transform.SetParent(go.transform, false);
            var br = (RectTransform)badgeGo.transform;
            br.anchorMin = br.anchorMax = new Vector2(1f, 0.5f);
            br.pivot = new Vector2(1f, 0.5f);
            br.anchoredPosition = new Vector2(-4f, 0f);
            var bbg = badgeGo.AddComponent<RoundedRectGraphic>();
            bbg.Radius = 4f;
            bbg.color = BadgeTint(a.approvalLevel);
            bbg.raycastTarget = false;

            var btGo = new GameObject("T", typeof(RectTransform));
            btGo.transform.SetParent(badgeGo.transform, false);
            var btr = (RectTransform)btGo.transform;
            btr.anchorMin = Vector2.zero; btr.anchorMax = Vector2.one;
            btr.offsetMin = Vector2.zero; btr.offsetMax = Vector2.zero;
            var btxt = UIBuilder.Tmp(btGo, BadgeText(a.approvalLevel), 10.5f,
                TextAnchor.MiddleCenter, BadgeInk(bbg.color));
            btxt.ForceMeshUpdate();
            float bw = Mathf.Clamp(btxt.preferredWidth + 10f, 30f, w * 0.45f);
            br.sizeDelta = new Vector2(bw, RowH - 8f);

            var nameGo = new GameObject("N", typeof(RectTransform));
            nameGo.transform.SetParent(go.transform, false);
            var nr = (RectTransform)nameGo.transform;
            nr.anchorMin = Vector2.zero; nr.anchorMax = Vector2.one;
            nr.offsetMin = new Vector2(8f, 0f); nr.offsetMax = new Vector2(-(bw + 10f), 0f);
            var ntxt = UIBuilder.Tmp(nameGo, a.name, 12f, TextAnchor.MiddleLeft, Theme.Text);
            ntxt.richText = false;
            ntxt.raycastTarget = false;
            ntxt.overflowMode = TextOverflowModes.Ellipsis;

            string pick = a.name;
            ClickHandler.Attach(go, () =>
            {
                var f = _field;
                var commit = _commit;
                Close();
                // Programmatic text writes don't raise onEndEdit, so commit explicitly.
                if (f != null) { f.text = pick; f.DeactivateInputField(); }
                commit?.Invoke(pick);
            });
        }

        // ── placement + badge styling ───────────────────────────────────────

        /* World corners of a screen-space-overlay canvas ARE screen pixels, so the popup can be
           parked on the field's bottom-left corner by world position without knowing which
           canvas the row lives on. Flips above the field when there's no room below. */
        private static void Place()
        {
            if (_popupGo == null || _field == null) return;
            var fr = (RectTransform)_field.transform;
            var c = new Vector3[4];
            fr.GetWorldCorners(c);   // 0 BL, 1 TL, 2 TR, 3 BR
            var r = (RectTransform)_popupGo.transform;
            float sf = 1f;
            var canvas = _canvasGo != null ? _canvasGo.GetComponent<Canvas>() : null;
            if (canvas != null) sf = canvas.scaleFactor;
            bool up = c[0].y - r.sizeDelta.y * sf < 0f;
            r.pivot = new Vector2(0f, up ? 0f : 1f);
            _popupGo.transform.position = up ? c[1] + new Vector3(0f, 2f * sf, 0f)
                                             : c[0] - new Vector3(0f, 2f * sf, 0f);
        }

        private static float FieldWidth()
        {
            try
            {
                var fr = (RectTransform)_field.transform;
                return Mathf.Max(160f, fr.rect.width);
            }
            catch { return 220f; }
        }

        private static string BadgeText(ApprovalLevel lvl)
        {
            try
            {
                bool ex;
                var s = RDString.GetWithCheck("enum.ApprovalLevel." + lvl, out ex, null);
                if (ex && !string.IsNullOrEmpty(s)) return s;
            }
            catch { }
            return lvl.ToString();
        }

        // The game's own badge colors (RDConstants), so Sapphire's list reads identically to the
        // vanilla dropdown. MostlyAllowed has no color of its own there — it inherits the
        // prefab's, which we don't have, so it borrows the partially-declined amber.
        private static Color BadgeTint(ApprovalLevel lvl)
        {
            try
            {
                var gc = ADOBase.gc;
                if (gc != null)
                    switch (lvl)
                    {
                        case ApprovalLevel.Allowed:           return gc.allowedColor;
                        case ApprovalLevel.PartiallyDeclined:
                        case ApprovalLevel.MostlyAllowed:     return gc.particallyDeclinedColor;
                        case ApprovalLevel.Declined:          return gc.declinedColor;
                        case ApprovalLevel.ListingRejected:   return gc.listingRejectedColor;
                    }
            }
            catch { }
            return new Color(1f, 1f, 1f, 0.18f);
        }

        // Badge fills are bright pastels; pick ink that survives on them.
        private static Color BadgeInk(Color fill)
        {
            float lum = fill.r * 0.299f + fill.g * 0.587f + fill.b * 0.114f;
            return lum > 0.6f ? new Color(0.08f, 0.08f, 0.1f, 1f) : Theme.Text;
        }
    }
}

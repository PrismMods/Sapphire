using System;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire.UI
{
    /* Shared modal confirm for destructive or bulk actions (delete-all, bulk field apply).
       Own canvas above every Sapphire panel, a full-screen blocker so nothing behind it can be
       clicked mid-decision, ESC / Cancel to back out. Deliberately NOT EditorPopups: that one
       mirrors the GAME's popups, it can't raise one of ours. */
    internal static class ConfirmBox
    {
        private static GameObject _go;
        private const float CardW = 400f, CardH = 158f, BtnH = 32f, BtnW = 118f, Pad = 16f;

        internal static bool IsOpen => _go != null;

        internal static void Close()
        {
            if (_go != null) { UnityEngine.Object.Destroy(_go); _go = null; }
        }

        internal static void Ask(string message, string confirmLabel, Action onYes, bool danger = true)
        {
            Close();
            _go = new GameObject("SapphireConfirm", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_go);
            var canvas = _go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 957; // above panels/popups, below the master switch (958)
            var scaler = _go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _go.AddComponent<GraphicRaycaster>();
            _go.AddComponent<EscClose>();

            var dimGo = new GameObject("Dim", typeof(RectTransform));
            dimGo.transform.SetParent(_go.transform, false);
            var dr = (RectTransform)dimGo.transform;
            dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one;
            dr.offsetMin = dr.offsetMax = Vector2.zero;
            var dim = dimGo.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            dim.raycastTarget = true;

            var cardGo = new GameObject("Card", typeof(RectTransform));
            cardGo.transform.SetParent(_go.transform, false);
            var cr = (RectTransform)cardGo.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            cr.sizeDelta = new Vector2(CardW, CardH);
            var cbg = cardGo.AddComponent<RoundedRectGraphic>();
            cbg.Radius = 10f;
            cbg.color = new Color(0.12f, 0.12f, 0.14f, 0.99f);
            cbg.BorderWidth = 1f;
            cbg.BorderColor = new Color(1f, 1f, 1f, 0.16f);
            cbg.raycastTarget = true;

            var mGo = new GameObject("Msg", typeof(RectTransform));
            mGo.transform.SetParent(cardGo.transform, false);
            var mr = (RectTransform)mGo.transform;
            mr.anchorMin = new Vector2(0f, 1f); mr.anchorMax = new Vector2(1f, 1f);
            mr.pivot = new Vector2(0.5f, 1f);
            mr.offsetMin = new Vector2(Pad, -(CardH - Pad * 2f - BtnH));
            mr.offsetMax = new Vector2(-Pad, -Pad);
            UIBuilder.Tmp(mGo, message, 13f, TextAnchor.UpperCenter, Theme.Text).raycastTarget = false;

            Button(cr, Loc.T("Cancel"), Pad, -(CardH - Pad - BtnH), BtnW, Close, false);
            Button(cr, confirmLabel, CardW - Pad - BtnW, -(CardH - Pad - BtnH), BtnW,
                () => { Close(); onYes?.Invoke(); }, danger);
        }

        private static void Button(RectTransform parent, string label, float x, float y, float w,
            Action onClick, bool danger)
        {
            var go = new GameObject("Btn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, BtnH);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = danger ? new Color(0.80f, 0.22f, 0.26f, 0.55f) : new Color(1f, 1f, 1f, 0.08f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;
            var tGo = new GameObject("T", typeof(RectTransform));
            tGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)tGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            UIBuilder.Tmp(tGo, label, 12.5f, TextAnchor.MiddleCenter, Theme.Text).raycastTarget = false;
            ClickHandler.Attach(go, onClick);
        }

        // The box owns no Tick — it lives and dies with its own canvas, so ESC rides along on it.
        private class EscClose : MonoBehaviour
        {
            private void Update() { if (Input.GetKeyDown(KeyCode.Escape)) Close(); }
        }
    }
}

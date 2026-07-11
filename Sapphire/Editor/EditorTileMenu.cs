using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Right-click a tile → a Sapphire context menu. Right-click is only free for this
       because the FreeAngleRebindPatch moved free-angle placement onto right-Alt (see
       Patches.cs). On right-mouse-down over a tile we select that tile
       (RDUtils.GetFloorAtPosition = Physics2D.OverlapPoint on the floor layer) and open a
       menu whose rows PROXY the editor's own selection ops (copy/cut/paste/delete/flip/
       rotate), so undo and game logic come free. Gated on Settings.EditorTileActions. */
    internal static class EditorTileMenu
    {
        private static GameObject _canvasGo;
        private static RectTransform _canvasRect;
        private static GameObject _menuGo;

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            bool want = false;
            try
            {
                ed = scnEditor.instance;
                want = ed != null && !ed.playMode && s != null && MainClass.EditorSuiteOn && s.EditorTileActions;
            }
            catch { }
            if (!want) { if (_menuGo != null) CloseMenu(); return; }

            // Close an open menu on Escape.
            if (_menuGo != null && Input.GetKeyDown(KeyCode.Escape)) { CloseMenu(); return; }

            // An active event-palette tool claims right-click to stamp its event; yield the menu.
            if (EditorToolbar.EventTool >= 0) { if (_menuGo != null) CloseMenu(); return; }

            if (Input.GetMouseButtonDown(1))
            {
                CloseMenu(); // a fresh right-click reopens at the new spot
                // Gate on a real tile under the cursor (Physics2D, independent of the
                // editor's fullscreen UI canvas — IsPointerOverGameObject is true across the
                // whole play area, so it can't be used to reject right-clicks here).
                scrFloor floor = FloorUnderCursor(ed);
                if (floor == null) return;
                try { ed.SelectFloor(floor, false); } catch { }
                OpenMenu(Input.mousePosition);
            }
        }

        internal static void Dispose()
        {
            CloseMenu();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _canvasRect = null;
        }

        private static scrFloor FloorUnderCursor(scnEditor ed)
        {
            try
            {
                Camera cam = null;
                try { cam = ed.camera; } catch { }
                if (cam == null) cam = Camera.main;
                if (cam == null) return null;
                Vector2 world = cam.ScreenToWorldPoint(Input.mousePosition);
                return RDUtils.GetFloorAtPosition(world);
            }
            catch { return null; }
        }

        // ── menu ────────────────────────────────────────────────────────────

        private static void OpenMenu(Vector3 screenPos)
        {
            if (_canvasGo == null) BuildCanvas();

            _menuGo = new GameObject("TileMenu", typeof(RectTransform));
            _menuGo.transform.SetParent(_canvasGo.transform, false);
            var blocker = (RectTransform)_menuGo.transform;
            blocker.anchorMin = Vector2.zero; blocker.anchorMax = Vector2.one;
            blocker.offsetMin = Vector2.zero; blocker.offsetMax = Vector2.zero;
            var blockImg = _menuGo.AddComponent<Image>();
            blockImg.color = new Color(0f, 0f, 0f, 0.01f);
            blockImg.raycastTarget = true;
            UI.ClickHandler.Attach(_menuGo, CloseMenu);

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(_menuGo.transform, false);
            var panel = (RectTransform)panelGo.transform;
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 0f);
            panel.pivot = new Vector2(0f, 1f); // grows downward from the cursor
            var bg = panelGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 10f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.97f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;

            var entries = new List<KeyValuePair<string, Action>>
            {
                Row("Copy",      ed => ed.MultiCopyFloors(false)),
                Row("Cut",       ed => ed.MultiCutFloors()),
                Row("Paste",     ed => ed.PasteFloors(false)),
                Row("Delete",    ed => ed.DeleteSingleSelection(false)),
                Row("Rotate CW",  ed => ed.RotateSelection(true)),
                Row("Rotate CCW", ed => ed.RotateSelection(false)),
            };

            const float rowH = 30f, padY = 6f, width = 190f;
            float y = -padY;
            foreach (var e in entries)
            {
                MakeRow(panelGo.transform, e.Key, e.Value, y, rowH);
                y -= rowH;
            }
            panel.sizeDelta = new Vector2(width, -y + padY);

            // Position the panel at the cursor, clamped so it stays on screen.
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, null, out local))
            {
                var half = _canvasRect.rect.size * 0.5f;
                float maxX = half.x - width;
                float minY = -half.y + (-y + padY);
                local.x = Mathf.Min(local.x, maxX);
                local.y = Mathf.Max(local.y, minY);
                panel.anchoredPosition = local;
            }
        }

        // (label, action) — the action runs against the current scnEditor.
        private static KeyValuePair<string, Action> Row(string label, Action<scnEditor> act)
        {
            return new KeyValuePair<string, Action>(label, () =>
            {
                try { var ed = scnEditor.instance; if (ed != null) act(ed); }
                catch (Exception ex) { SapphireLog.Log("TileMenu: " + label + " failed: " + ex.Message); }
            });
        }

        private static void MakeRow(Transform panel, string label, Action onClick, float y, float rowH)
        {
            var rowGo = new GameObject("Row", typeof(RectTransform));
            rowGo.transform.SetParent(panel, false);
            var row = (RectTransform)rowGo.transform;
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.offsetMin = new Vector2(6f, 0f);
            row.offsetMax = new Vector2(-6f, 0f);
            row.anchoredPosition = new Vector2(0f, y);
            row.sizeDelta = new Vector2(row.sizeDelta.x, rowH);
            var rowBg = rowGo.AddComponent<RoundedRectGraphic>();
            rowBg.Radius = 6f;
            rowBg.color = new Color(1f, 1f, 1f, 0f);
            rowBg.raycastTarget = true;
            rowGo.AddComponent<RowHover>().Bg = rowBg;

            var txtGo = new GameObject("Label", typeof(RectTransform));
            txtGo.transform.SetParent(rowGo.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(12f, 0f); tr.offsetMax = new Vector2(-12f, 0f);
            UIBuilder.Tmp(txtGo, label, 14f, TextAnchor.MiddleLeft, Theme.Text);

            UI.ClickHandler.Attach(rowGo, () => { CloseMenu(); onClick(); });
        }

        private static void CloseMenu()
        {
            if (_menuGo != null) UnityEngine.Object.Destroy(_menuGo);
            _menuGo = null;
        }

        private static void BuildCanvas()
        {
            _canvasGo = new GameObject("SapphireTileMenu", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 908; // above chrome/toolbar/popups
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
            _canvasRect = (RectTransform)_canvasGo.transform;
        }

        private class RowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public RoundedRectGraphic Bg;
            public void OnPointerEnter(PointerEventData e) { if (Bg != null) Bg.color = new Color(1f, 1f, 1f, 0.08f); }
            public void OnPointerExit(PointerEventData e) { if (Bg != null) Bg.color = new Color(1f, 1f, 1f, 0f); }
        }
    }
}

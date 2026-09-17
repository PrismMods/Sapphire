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
       Patches.cs). On right-mouse-down over a tile we select that tile and open a
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
            if (!want)
            {
                if (_menuGo != null) CloseMenu();
                return;
            }

            // Close an open menu on Escape.
            if (_menuGo != null && Input.GetKeyDown(KeyCode.Escape)) { CloseMenu(); return; }

            // An active event-palette tool claims right-click to stamp its event; yield the menu.
            if (EditorToolbar.EventTool >= 0)
            {
                if (_menuGo != null) CloseMenu();
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                // The timeline owns right-clicks over itself (keyframe creation / ease picker)
                // even when a tile sits behind the strip. Tested here rather than above the
                // guard: it costs a mousePosition read plus up to three
                // RectangleContainsScreenPoint matrix transforms, and only a right-click
                // can act on the answer.
                if (EditorEvents.TimelineHovered) return;

                CloseMenu(); // a fresh right-click reopens at the new spot
                /* Gate on a real tile under the cursor. Can't use IsPointerOverGameObject to
                   reject clicks on empty space: the editor's canvas covers the whole play
                   area, so it is true everywhere. */
                scrFloor floor = FloorUnderCursor(ed);
                if (floor == null) return;
                /* Right-clicking INSIDE a multi-selection must not collapse it to one tile —
                   that is the selection the menu is about to act on. Only a click outside the
                   selection re-selects. */
                bool multi = InSelection(ed, floor) && SelectedCount(ed) >= 2;
                if (!multi) { try { ed.SelectFloor(floor, false); } catch { } }
                OpenMenu(Input.mousePosition, multi);
            }
        }

        internal static void Dispose()
        {
            CloseMenu();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _canvasRect = null;
        }

        private static int SelectedCount(scnEditor ed)
        {
            try { return ed.selectedFloors != null ? ed.selectedFloors.Count : 0; }
            catch { return 0; }
        }

        private static bool InSelection(scnEditor ed, scrFloor f)
        {
            try
            {
                var sel = ed.selectedFloors;
                if (sel == null || f == null) return false;
                for (int i = 0; i < sel.Count; i++) if (sel[i] == f) return true;
                return false;
            }
            catch { return false; }
        }

        /* Nearest tile to the cursor within ~a tile radius. RDUtils.GetFloorAtPosition
           (Physics2D.OverlapPoint on the "Floor" layer) never hits editor tiles here — it
           returned null on every right-click — so match the toolbar's world-distance pick. */
        private static scrFloor FloorUnderCursor(scnEditor ed)
        {
            Camera cam = null;
            try { cam = ed.camera; } catch { }
            if (cam == null) cam = Camera.main;
            if (cam == null) return null;
            Vector2 w = cam.ScreenToWorldPoint(Input.mousePosition);
            scrFloor best = null;
            float bestSqr = 0.7f * 0.7f; // squared: this walks every tile in the level
            try
            {
                var floors = ed.floors;
                if (floors != null)
                    for (int i = 0; i < floors.Count; i++)
                    {
                        var f = floors[i];
                        if (f == null) continue;
                        float d = ((Vector2)f.transform.position - w).sqrMagnitude;
                        if (d < bestSqr) { bestSqr = d; best = f; }
                    }
            }
            catch { }
            return best;
        }

        // ── menu ────────────────────────────────────────────────────────────

        private static void OpenMenu(Vector3 screenPos, bool multi = false)
        {
            if (_canvasGo == null) BuildCanvas();
            else if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);

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
            /* Anchored at the canvas CENTRE, not a corner: ScreenPointToLocalPointInRectangle
               returns a point relative to the rect's PIVOT, and the canvas pivot is (0.5, 0.5).
               A corner anchor put every menu half a screen down and left. */
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0f, 1f); // grows down-right from the cursor
            var bg = panelGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 10f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.97f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;

            /* Two menus. The single-tile one proxies the editor's own verbs; the multi-selection
               one is Sapphire's, and its items are deliberately unwired — each is a distinct
               decision about what "repeat" or "safe delete" should mean across a span of tiles,
               and guessing at those would be worse than saying so. */
            var entries = multi
                ? new List<KeyValuePair<string, Action>>
                {
                    Row(Loc.T("Copy"),        NotYet("copy")),
                    Row(Loc.T("Cut"),         NotYet("cut")),
                    Row(Loc.T("Delete (safe)"), NotYet("delete")),
                    Row(Loc.T("Add twirls"),  NotYet("twirls")),
                    Row(Loc.T("Repeat"),      NotYet("repeat")),
                    Row(Loc.T("Duplicate"),   NotYet("duplicate")),
                    Row(Loc.T("Move"),        NotYet("move")),
                }
                : new List<KeyValuePair<string, Action>>
                {
                    Row(Loc.T("Copy"),      ed => ed.MultiCopyFloors(false)),
                    Row(Loc.T("Cut"),       ed => ed.MultiCutFloors()),
                    // The type filter the multi-selection overlay already owns, reachable for
                    // one tile: pick which events ride along, then Copy/Cut there.
                    Row(Loc.T("Copy (select events)"), _ => EditorCopyPanel.OpenForSelection(false)),
                    Row(Loc.T("Cut (select events)"),  _ => EditorCopyPanel.OpenForSelection(true)),
                    Row(Loc.T("Paste"),     ed => ed.PasteFloors(false)),
                    Row(Loc.T("Delete"),    ed => ed.DeleteSingleSelection(false)),
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
                float h = -y + padY;
                local.x = Mathf.Clamp(local.x, -half.x, half.x - width);
                local.y = Mathf.Clamp(local.y, -half.y + h, half.y);
                panel.anchoredPosition = local;
            }
        }

        // A placeholder that SAYS it is one, rather than a row that silently does nothing.
        private static Action<scnEditor> NotYet(string what)
        {
            return ed =>
            {
                try { ed.ShowNotification(Loc.T("Not wired up yet") + " — " + what); } catch { }
                SapphireLog.Log("TileMenu: multi '" + what + "' is a stub");
            };
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
            if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
        }

        private static void BuildCanvas()
        {
            _canvasGo = new GameObject("SapphireTileMenu", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 954; // right-click context menu above windows/toolbar
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

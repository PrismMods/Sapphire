using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire
{
    /* Position/scale overrides for the GAME EDITOR's own UI chrome (file bar, panel
       tabs…) — GameUiLayout's wrapper model applied to scnEditor: each target is
       reparented into a stretch wrapper matching its parent rect, and the wrapper takes
       the offset/scale, so the element's own anchors keep meaning what they did.
       Overrides persist in Settings.EditorUiOverrides (same GameUiOverride shape, no
       alignment). Ticked from Overlay.Update; wrappers die with the editor scene and
       re-apply on the next tick. */
    internal static class EditorUiLayout
    {
        private const string WrapPrefix = "SapphireEditorUiWrap_";

        internal class TargetDef
        {
            public string Key;
            public string Label;
            public Func<RectTransform> Get;
        }

        internal static readonly TargetDef[] Targets =
        {
            // The bar with the save icon + level name + dropdown arrow; moving it takes
            // the file menu along, so the menu opens wherever the bar lives now.
            new TargetDef { Key = "filebar",   Label = "File Bar",
                Get = () => Ed?.buttonFileActionDropdown != null
                    ? Ed.buttonFileActionDropdown.transform.parent as RectTransform : null },
            // The vertical icon strip that switches the inspector's settings panels.
            new TargetDef { Key = "paneltabs", Label = "Panel Tabs",
                Get = () => Ed?.inspectorTabs != null
                    ? Ed.inspectorTabs.transform as RectTransform : null },
        };

        private static scnEditor Ed
        {
            get { try { return scnEditor.instance; } catch { return null; } }
        }

        private static Settings S => MainClass.Settings;

        private static readonly Dictionary<string, RectTransform> _wrappers =
            new Dictionary<string, RectTransform>();

        // ── Overrides storage ──────────────────────────────────────────────

        internal static GameUiOverride GetOverride(string key, bool create)
        {
            var s = S;
            if (s == null) return null;
            if (s.EditorUiOverrides == null) s.EditorUiOverrides = new List<GameUiOverride>();
            foreach (var o in s.EditorUiOverrides)
                if (o != null && o.Key == key) return o;
            if (!create) return null;
            var n = new GameUiOverride { Key = key };
            s.EditorUiOverrides.Add(n);
            return n;
        }

        internal static void RemoveOverride(string key)
        {
            var s = S;
            if (s?.EditorUiOverrides == null) return;
            s.EditorUiOverrides.RemoveAll(o => o == null || o.Key == key);
            Unwrap(key);
        }

        internal static void ResetToDefault(string key)
        {
            var d = Settings.DefaultEditorUiOverride(key);
            if (d == null) { RemoveOverride(key); return; }
            var o = GetOverride(key, create: true);
            o.OffX = d.OffX;
            o.OffY = d.OffY;
            o.Scale = d.Scale;
            o.Hidden = false;
            ApplyOne(key);
        }

        internal static void ResetAllToDefaults()
        {
            foreach (var t in Targets) ResetToDefault(t.Key);
        }

        internal static void ResetAllToGame()
        {
            foreach (var t in Targets) RemoveOverride(t.Key);
        }

        internal static bool HasAnyOverride =>
            S != null && S.EditorUiOverrides != null && S.EditorUiOverrides.Count > 0;

        // ── Apply ──────────────────────────────────────────────────────────

        private static int _tickCooldown;

        internal static void Tick()
        {
            if (--_tickCooldown > 0) return;
            _tickCooldown = 60;
            if (Ed == null || !HasAnyOverride) return;
            try { Reapply(); } catch (Exception e) { SapphireLog.Debug("EditorUiLayout: " + e.Message); }
        }

        internal static void Reapply()
        {
            if (S == null) return;
            PruneWrappers();
            foreach (var t in Targets)
            {
                var o = GetOverride(t.Key, create: false);
                if (o != null) ApplyOne(t, o);
            }
        }

        internal static void ApplyOne(string key)
        {
            foreach (var t in Targets)
                if (t.Key == key)
                {
                    var o = GetOverride(key, create: false);
                    if (o != null) ApplyOne(t, o);
                    return;
                }
        }

        private static void ApplyOne(TargetDef t, GameUiOverride o)
        {
            var rt = t.Get?.Invoke();
            if (rt == null) return;
            var w = EnsureWrapper(t.Key, rt);
            if (w == null) return;

            // Reset, measure the element's untouched center, pivot the wrapper there so
            // scale grows the element in place; wrapper rect == parent rect, so
            // anchoredPosition equals the on-screen shift.
            w.localScale = Vector3.one;
            w.anchoredPosition = Vector2.zero;
            Vector2 c = (Vector2)w.InverseTransformPoint(WorldCenter(rt));
            var r = w.rect;
            if (r.width > 0.01f && r.height > 0.01f)
                w.pivot = new Vector2((c.x - r.xMin) / r.width, (c.y - r.yMin) / r.height);

            w.anchoredPosition = new Vector2(o.OffX, o.OffY);
            float sc = Mathf.Clamp(o.Scale, 0.1f, 5f);
            w.localScale = new Vector3(sc, sc, 1f);

            var cg = w.GetComponent<CanvasGroup>();
            if (o.Hidden)
            {
                if (cg == null) cg = w.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false;
            }
            else if (cg != null)
            {
                cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true;
            }
        }

        private static Vector3 WorldCenter(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            return (corners[0] + corners[2]) * 0.5f;
        }

        // ── Wrappers ───────────────────────────────────────────────────────

        private static RectTransform EnsureWrapper(string key, RectTransform rt)
        {
            var parent = rt.parent as RectTransform;
            if (parent != null && parent.name == WrapPrefix + key)
            {
                _wrappers[key] = parent;
                return parent;
            }
            if (parent == null) return null;
            if (parent.GetComponent<LayoutGroup>() != null)
            {
                SapphireLog.Debug("EditorUiLayout: '" + key + "' parent has a LayoutGroup, skipping");
                return null;
            }

            var go = new GameObject(WrapPrefix + key, typeof(RectTransform));
            var w = (RectTransform)go.transform;
            w.SetParent(parent, false);
            w.SetSiblingIndex(rt.GetSiblingIndex());
            w.anchorMin = Vector2.zero;
            w.anchorMax = Vector2.one;
            w.offsetMin = Vector2.zero;
            w.offsetMax = Vector2.zero;
            w.localScale = Vector3.one;
            rt.SetParent(w, false);
            _wrappers[key] = w;
            return w;
        }

        private static void Unwrap(string key)
        {
            RectTransform w;
            if (!_wrappers.TryGetValue(key, out w)) w = null;
            _wrappers.Remove(key);
            if (w == null) return;
            var parent = w.parent;
            int idx = w.GetSiblingIndex();
            for (int i = w.childCount - 1; i >= 0; i--)
            {
                var child = w.GetChild(i);
                child.SetParent(parent, false);
                child.SetSiblingIndex(idx);
            }
            UnityEngine.Object.Destroy(w.gameObject);
        }

        private static void PruneWrappers()
        {
            List<string> dead = null;
            foreach (var kv in _wrappers)
                if (kv.Value == null) (dead = dead ?? new List<string>()).Add(kv.Key);
            if (dead != null)
                foreach (var k in dead) _wrappers.Remove(k);
        }

        // Unwrap everything (StopMod) so hot reloads leave no orphaned wrappers behind.
        internal static void RestoreAll()
        {
            var keys = new List<string>(_wrappers.Keys);
            foreach (var k in keys) Unwrap(k);
        }
    }
}

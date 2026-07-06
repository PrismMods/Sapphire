using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire.UI
{
    // Drill-in navigation for one tab: a root view plus a stack of subpages, swapped
    // in-place inside the tab's scroll content. Each subpage gets a top bar with a
    // Back button and a breadcrumb of the titles pushed so far. Generalizes the view
    // swap Key Viewer used for its preset editor.
    internal class PageStack
    {
        private class View
        {
            public string Title;
            public GameObject Container;    // top bar + body
            public Transform Body;
            public TMPro.TextMeshProUGUI TitleLabel;
            public Action<Transform> Build;
            public bool RebuildOnReveal;    // re-run Build when a child view pops back to this one
            public float SavedScrollY;      // content offset captured when a child is pushed
        }

        private readonly ScrollRect _scroll;
        private readonly RectTransform _content;
        private readonly List<View> _views = new List<View>();

        // Root view container — the tab's page builder fills this.
        public Transform Root { get; }

        // Fired when the last subpage pops back to the root (e.g. refresh list rows
        // whose labels may have changed while a subpage was open).
        public Action OnRootRevealed;

        public PageStack(ScrollRect scroll, RectTransform content)
        {
            _scroll = scroll;
            _content = content;
            Root = UIBuilder.VGroup(content, "Root").transform;
        }

        public void Push(string title, Action<Transform> build, bool rebuildOnReveal = false)
        {
            var current = TopContainer();
            if (_views.Count > 0) _views[_views.Count - 1].SavedScrollY = _content.anchoredPosition.y;
            else _rootScrollY = _content.anchoredPosition.y;
            current.SetActive(false);

            var view = new View { Title = title, Build = build, RebuildOnReveal = rebuildOnReveal };
            _views.Add(view);

            view.Container = UIBuilder.VGroup(_content, "View_" + title);
            view.TitleLabel = BuildTopBar(view.Container.transform, Breadcrumb());
            view.Body = UIBuilder.VGroup(view.Container.transform, "Body").transform;
            // Subpage widgets stay out of the search index — their entry point is the
            // registered row/card that pushed them.
            SettingsSearch.Suspend(true);
            try { build(view.Body); }
            finally { SettingsSearch.Suspend(false); }

            ScrollTo(0f);
        }

        public void Pop()
        {
            if (_views.Count == 0) return;
            var top = _views[_views.Count - 1];
            _views.RemoveAt(_views.Count - 1);
            top.Container.transform.SetParent(null);
            UnityEngine.Object.Destroy(top.Container);

            var revealed = TopContainer();
            revealed.SetActive(true);
            if (_views.Count == 0)
            {
                OnRootRevealed?.Invoke();
                ScrollTo(_rootScrollY);
            }
            else
            {
                var v = _views[_views.Count - 1];
                if (v.RebuildOnReveal) Rebuild(v);
                ScrollTo(v.SavedScrollY);
            }
        }

        public void PopToRoot()
        {
            while (_views.Count > 0) Pop();
        }

        // Clear + re-run the current subpage's builder (e.g. after a reset button so
        // sliders re-read their values). No-op on the root — pages own that content.
        public void RefreshTop()
        {
            if (_views.Count == 0) return;
            Rebuild(_views[_views.Count - 1]);
        }

        // Rename the current subpage (e.g. live-editing a preset's name) — updates its
        // breadcrumb and the prefix any deeper push will inherit.
        public void RetitleTop(string title)
        {
            if (_views.Count == 0) return;
            var top = _views[_views.Count - 1];
            top.Title = title;
            if (top.TitleLabel != null) top.TitleLabel.text = Breadcrumb();
        }

        private float _rootScrollY;

        private GameObject TopContainer()
            => _views.Count > 0 ? _views[_views.Count - 1].Container : Root.gameObject;

        private string Breadcrumb()
        {
            var titles = new string[_views.Count];
            for (int i = 0; i < _views.Count; i++) titles[i] = _views[i].Title;
            return string.Join(" / ", titles);
        }

        private void Rebuild(View view)
        {
            for (int i = view.Body.childCount - 1; i >= 0; i--)
            {
                var c = view.Body.GetChild(i);
                c.SetParent(null);
                UnityEngine.Object.Destroy(c.gameObject);
            }
            SettingsSearch.Suspend(true);
            try { view.Build(view.Body); }
            finally { SettingsSearch.Suspend(false); }
        }

        // Content is top-anchored (pivot y=1), so anchoredPosition.y is the scroll offset;
        // ScrollRect clamps it on its next update if the revealed view is shorter.
        private void ScrollTo(float y)
        {
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, y);
            if (_scroll != null) _scroll.velocity = Vector2.zero;
        }

        // ← Back + breadcrumb title. Returns the title label so it can be retitled.
        private TMPro.TextMeshProUGUI BuildTopBar(Transform parent, string title)
        {
            var topRow = UIBuilder.Rect("TopBar", parent);
            var topLe = topRow.AddComponent<LayoutElement>();
            topLe.preferredHeight = 32f;
            topLe.minHeight = 32f;

            var backBtn = UIBuilder.Rect("Back", topRow.transform);
            var backRect = (RectTransform)backBtn.transform;
            backRect.anchorMin = new Vector2(0, 0.5f);
            backRect.anchorMax = new Vector2(0, 0.5f);
            backRect.pivot = new Vector2(0, 0.5f);
            backRect.anchoredPosition = new Vector2(8f, 0);
            backRect.sizeDelta = new Vector2(90f, 24f);
            var backBg = backBtn.AddComponent<RoundedRectGraphic>();
            backBg.Radius = 3f;
            backBg.AAFringe = 0.5f;
            backBg.color = Theme.ButtonBg;
            backBg.raycastTarget = true;
            var backLbl = UIBuilder.Rect("L", backBtn.transform);
            var backLblRect = (RectTransform)backLbl.transform;
            backLblRect.anchorMin = Vector2.zero;
            backLblRect.anchorMax = Vector2.one;
            backLblRect.offsetMin = Vector2.zero;
            backLblRect.offsetMax = Vector2.zero;
            UIBuilder.Tmp(backLbl, "← Back", (int)UIBuilder.LabelFontSize, TextAnchor.MiddleCenter, Theme.Text);
            ClickHandler.Attach(backBtn, Pop);

            var titleGo = UIBuilder.Rect("Title", topRow.transform);
            var titleRect = (RectTransform)titleGo.transform;
            titleRect.anchorMin = new Vector2(0, 0);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.offsetMin = new Vector2(110f, 0);
            titleRect.offsetMax = new Vector2(-8f, 0);
            return UIBuilder.Tmp(titleGo, title, (int)UIBuilder.LabelFontSize, TextAnchor.MiddleLeft, Theme.TextMuted);
        }
    }
}

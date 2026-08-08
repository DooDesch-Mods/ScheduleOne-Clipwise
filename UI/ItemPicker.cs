using System;
using System.Collections.Generic;
using Clipwise.Index;
using Clipwise.Model;
using Clipwise.Prefs;
using DooDesch.UI;
using S1API.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Clipwise.UI
{
    /// <summary>
    /// The replacement for the clipboard's item grid: category tabs, a live search box, tag filter chips, favourites
    /// and a scrollable list of named rows.
    ///
    /// Vanilla renders an item field's options as a flat 5-wide icon grid with no scroll view, no search and no
    /// grouping (ItemSelector.CreateOptions), and shows the name of only the entry under the cursor. That works for
    /// a handful of items and falls apart at forty.
    ///
    /// Picking writes back through <see cref="ItemField.SetItem"/> exactly as vanilla's own handler does, so
    /// multi-selected objects and the "Any" entry behave identically and the choice still replicates by item ID.
    /// </summary>
    internal static class ItemPicker
    {
        private const float CardWidth = 540f;
        private const float CardHeight = 620f;
        private const float RowHeight = 34f;
        private const int MaxTagChips = 24;

        private static GameObject _scrim;
        private static Transform _canvasRoot;
        private static View _view;
        private static Action<ItemDefinition> _onPick;

        private static RectTransform _content;
        private static ScrollRect _scroll;
        private static InputField _search;
        private static RectTransform _chipBar;
        private static Button[] _tabButtons;
        private static readonly List<CategoryDef> _tabs = new();
        private static readonly List<KeyValuePair<Row, RectTransform>> _visible = new();
        private static readonly HashSet<string> _tagFilter = new(StringComparer.Ordinal);

        /// <summary>Sort modes, in the order the sort chip cycles through them. Index 0 keeps the deterministic
        /// order the view builder produced; the rest reorder rows INSIDE their category, so the tab structure and
        /// the section headers stay meaningful.</summary>
        private static readonly string[] SortLabels = { "Sort: default", "Sort: A-Z", "Sort: yield", "Sort: growth", "Sort: value" };

        private static string _query = "";
        private static string _tab = "";
        private static Row _hovered;
        private static Vector3 _lastMouse;

        /// <summary>Session-only: reveals hidden entries so they can be un-hidden again. Deliberately not
        /// persisted - "show me what I hid" is a one-off action, not a state to come back to.</summary>
        private static bool _showHidden;

        public static bool IsOpen => _scrim != null;

        /// <summary>Builds and shows the picker. Returns false if anything is missing or throws, so the caller can
        /// let the vanilla grid run instead of leaving the clipboard half-functional.</summary>
        public static bool TryOpen(Transform canvasRoot, View view, Action<ItemDefinition> onPick)
        {
            if (canvasRoot == null || view == null || onPick == null) return false;
            if (view.Rows.Count == 0) return false;

            try
            {
                Close();

                _canvasRoot = canvasRoot;
                _view = view;
                _onPick = onPick;
                _query = "";
                _showHidden = false;
                _tagFilter.Clear();
                _tab = ResolveStartTab(view);

                BuildChrome();
                Rebuild();
                return true;
            }
            catch (Exception e)
            {
                Core.Log.Warning("Clipwise: could not build the picker, falling back to the vanilla grid: " + e);
                Close();
                return false;
            }
        }

        public static void Close()
        {
            Tooltip.Hide();
            _hovered = null;
            _visible.Clear();
            _tabs.Clear();
            _tabButtons = null;
            _content = null;
            _scroll = null;
            _search = null;
            _chipBar = null;
            _view = null;
            _onPick = null;
            if (_scrim != null) UnityEngine.Object.Destroy(_scrim);
            _scrim = null;
            UserPrefs.Flush();
        }

#if DEBUG
        /// <summary>
        /// Drive the picker's filter state from the developer console, so tab switching and searching can be
        /// verified without a mouse. The MCP bridge can submit console commands but cannot click a uGUI button or
        /// type into an InputField, and a path that can only be exercised by hand goes to testers unchecked.
        /// </summary>
        internal static bool SetFilter(string tabKeyOrEmpty, string query)
        {
            if (!IsOpen) return false;
            if (tabKeyOrEmpty != null)
            {
                _tab = tabKeyOrEmpty;
                for (int i = 0; i < _tabs.Count; i++)
                {
                    bool active = i == 0 ? _tab.Length == 0 : string.Equals(_tabs[i].Key, _tab, StringComparison.Ordinal);
                    if (active) { Components.SetSegmentedActive(_tabButtons, i); break; }
                }
            }
            if (query != null) _query = query;
            Rebuild();
            return true;
        }

        /// <summary>Canonical keys of the tabs currently offered, "" first for the All tab.</summary>
        internal static List<string> TabKeys()
        {
            var keys = new List<string>();
            for (int i = 0; i < _tabs.Count; i++) keys.Add(i == 0 ? "" : _tabs[i].Key);
            return keys;
        }

        /// <summary>How many rows the current filter leaves visible.</summary>
        internal static int VisibleCount => _visible.Count;
#endif

        public static void Tick()
        {
            if (!IsOpen) return;

            // The card lives on the clipboard's canvas: if that canvas goes away (scene change, clipboard torn
            // down) the picker has to go with it rather than linger as an orphan.
            //
            // Those two checks are not enough on their own, because the card is parented to the ROOT canvas while
            // the clipboard only destroys its config panel (ManagementInterface.DestroyConfigPanel). Holster the
            // clipboard or switch hotbar slot with the picker up and the card outlives the screen it belongs to:
            // still alive, so IsOpen stays true for the rest of the session, and ExitPatch then swallows every
            // Escape and right-click the game sends - including the one ObjectSelector needs to commit a station
            // assignment (ObjectSelector.cs:64, :349-356). The clipboard's own IsOpen is the honest liveness test.
            if (_scrim == null || _content == null) { Close(); return; }

            if (!ClipboardOpen())
            {
                // Logged at message level on purpose: it fires once per occurrence, and it is the only evidence
                // that this path was reached in a build without the test kit.
                Core.Log.Msg("Clipwise: the clipboard closed while the picker was up - dropping the card.");
                Close();
                return;
            }

            UpdateHover();
        }

        /// <summary>Is the clipboard this card belongs to still up? Polled rather than subscribed, because both
        /// events that would report it want an IL2CPP delegate handed to the game.</summary>
        private static bool ClipboardOpen()
        {
            try { return Singleton<ManagementClipboard>.InstanceExists && Singleton<ManagementClipboard>.Instance.IsOpen; }
            catch { return false; }
        }

        /// <summary>Reopen on the tab the player left off on, as long as it still exists in this field.</summary>
        private static string ResolveStartTab(View view)
        {
            string last = UserPrefs.LastTab;
            if (string.IsNullOrEmpty(last)) return "";
            foreach (var c in view.Categories)
                if (string.Equals(c.Key, last, StringComparison.Ordinal)) return last;
            return "";
        }

        // ----- chrome -----

        private static void BuildChrome()
        {
            _scrim = UIFactory.Panel("CW_Scrim", _canvasRoot, new Color(0f, 0f, 0f, 0.6f), fullAnchor: true);
            _scrim.transform.SetAsLastSibling();

            var catcher = UIFactory.Panel("catcher", _scrim.transform, new Color(0f, 0f, 0f, 0.01f), fullAnchor: true);
            var cbtn = catcher.AddComponent<Button>();
            cbtn.targetGraphic = catcher.GetComponent<Image>();
            cbtn.onClick.AddListener((UnityAction)(() => Close()));

            var card = UIFactory.Panel("card", _scrim.transform, Theme.BgElevated);
            var cimg = card.GetComponent<Image>();
            if (cimg != null) { cimg.sprite = Theme.RoundedSprite(); cimg.type = Image.Type.Sliced; }
            var crt = card.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(CardWidth, CardHeight);
            var ol = card.AddComponent<Outline>();
            ol.effectColor = Theme.HairlineStrong;
            ol.effectDistance = new Vector2(1, -1);

            var title = UIFactory.Text("title", _view.Title, card.transform, Theme.H3, TextAnchor.UpperLeft, FontStyle.Bold);
            title.color = Theme.TextPrimary;
            title.raycastTarget = false;
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1);
            trt.offsetMin = new Vector2(20, -50); trt.offsetMax = new Vector2(-56, -18);

            var (closeGO, closeBtn, closeTxt) = UIFactory.ButtonWithLabel("x", "X", card.transform, Theme.Button, 28, 28);
            if (closeTxt != null) closeTxt.fontSize = Theme.Body;
            var clrt = closeGO.GetComponent<RectTransform>();
            clrt.anchorMin = clrt.anchorMax = new Vector2(1, 1); clrt.pivot = new Vector2(1, 1);
            clrt.anchoredPosition = new Vector2(-16, -16); clrt.sizeDelta = new Vector2(28, 28);
            closeBtn.onClick.AddListener((UnityAction)(() => Close()));

            BuildTabs(card.transform);

            // TextInput's own onChange only fires on onEndEdit (blur/Enter); live filtering needs onValueChanged,
            // wired directly. Passing null for onChange skips the onEndEdit listener entirely.
            _search = Components.TextInput(card.transform, "", null, "Search...", 40);
            var srt = _search.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0, 1); srt.anchorMax = new Vector2(1, 1); srt.pivot = new Vector2(0.5f, 1);
            srt.offsetMin = new Vector2(20, -134); srt.offsetMax = new Vector2(-20, -100);
            _search.onValueChanged.AddListener((UnityAction<string>)(s => { _query = s ?? ""; Rebuild(); }));

            _chipBar = HScroll(card.transform, 26f, -168f, -140f);
            BuildChips();

            var listPanel = UIFactory.Panel("list", card.transform, Theme.Clear);
            var lrt = listPanel.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.pivot = new Vector2(0.5f, 1);
            lrt.offsetMin = new Vector2(10, 16); lrt.offsetMax = new Vector2(-10, -174);
            _content = Components.ScrollList(listPanel.transform, out _scroll, 3f);
            SmoothScroll.Attach(_scroll);

            Interactions.PolishButtons(card.transform);
        }

        /// <summary>The tab bar. It scrolls horizontally on purpose: five tabs fit, but a save with several content
        /// mods installed can easily produce ten, and a fixed row would either clip them or squeeze them unreadable.</summary>
        private static void BuildTabs(Transform card)
        {
            _tabs.Clear();
            _tabs.Add(null);   // the All tab
            _tabs.AddRange(_view.Categories);

            var labels = new string[_tabs.Count];
            labels[0] = "All";
            for (int i = 1; i < _tabs.Count; i++) labels[i] = _tabs[i].Label;

            int active = 0;
            for (int i = 1; i < _tabs.Count; i++)
                if (string.Equals(_tabs[i].Key, _tab, StringComparison.Ordinal)) { active = i; break; }
            if (active == 0) _tab = "";

            RectTransform bar = HScroll(card, 30f, -96f, -62f);

            var buttons = new Button[_tabs.Count];
            for (int i = 0; i < _tabs.Count; i++)
            {
                int idx = i;
                var (go, btn, txt) = UIFactory.ButtonWithLabel("tab", labels[i], bar,
                    i == active ? Theme.Accent : Theme.Button, 0, 26);
                if (txt != null) txt.fontSize = Theme.Caption;
                var le = go.AddComponent<LayoutElement>();
                le.minHeight = 26; le.preferredHeight = 26;
                le.minWidth = Mathf.Max(52f, 11f * labels[i].Length + 18f);
                btn.onClick.AddListener((UnityAction)(() =>
                {
                    _tab = idx == 0 ? "" : _tabs[idx].Key;
                    UserPrefs.LastTab = _tab;
                    Components.SetSegmentedActive(_tabButtons, idx);
                    Rebuild();
                }));
                buttons[i] = btn;
            }
            _tabButtons = buttons;
        }

        private static void BuildChips()
        {
            UIFactory.ClearChildren(_chipBar);

            AddChip(_chipBar, "Discovered", UserPrefs.OnlyDiscovered, () =>
            {
                UserPrefs.OnlyDiscovered = !UserPrefs.OnlyDiscovered;
                BuildChips();
                Rebuild();
            });

            AddChip(_chipBar, "Favourites", UserPrefs.OnlyFavourites, () =>
            {
                UserPrefs.OnlyFavourites = !UserPrefs.OnlyFavourites;
                BuildChips();
                Rebuild();
            });

            AddChip(_chipBar, SortLabels[Mathf.Clamp(UserPrefs.SortMode, 0, SortLabels.Length - 1)],
                UserPrefs.SortMode != 0, () =>
            {
                UserPrefs.SortMode = (UserPrefs.SortMode + 1) % SortLabels.Length;
                BuildChips();
                Rebuild();
            });

            // Only offered once something is actually hidden, so the bar stays short for everyone else.
            if (UserPrefs.HiddenCount > 0)
                AddChip(_chipBar, "Hidden (" + UserPrefs.HiddenCount + ")", _showHidden, () =>
                {
                    _showHidden = !_showHidden;
                    BuildChips();
                    Rebuild();
                });

            int shown = 0;
            foreach (string tag in _view.Tags)
            {
                if (tag.StartsWith("clipwise:vanilla", StringComparison.Ordinal)) continue;
                if (tag.StartsWith("clipwise:modded", StringComparison.Ordinal)) continue;
                if (tag.StartsWith("clipwise:discovered", StringComparison.Ordinal)) continue;   // its own chip above
                // Effects are not a filter bar. There are thirty-odd of them, and a wall of toggles is a wall,
                // not a way to narrow something down. They live in the hover panel and in the search box, where
                // typing "calming" does the same job without costing a row of chips.
                if (tag.StartsWith("clipwise:effect/", StringComparison.Ordinal)) continue;
                if (shown++ >= MaxTagChips) break;

                string t = tag;
                AddChip(_chipBar, Catalog.TagLabel(t), _tagFilter.Contains(t), () =>
                {
                    if (!_tagFilter.Remove(t)) _tagFilter.Add(t);
                    BuildChips();
                    Rebuild();
                });
            }

            Interactions.PolishButtons(_chipBar);
        }

        private static void AddChip(Transform parent, string label, bool on, Action toggle)
        {
            var (go, btn, txt) = UIFactory.ButtonWithLabel("chip", label, parent,
                on ? Theme.Accent : Theme.Button, 0, 22);
            if (txt != null) txt.fontSize = Theme.Caption;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 22; le.preferredHeight = 22;
            le.minWidth = Mathf.Max(44f, 10f * (label?.Length ?? 0) + 16f);
            btn.onClick.AddListener((UnityAction)(() => toggle()));
        }

        /// <summary>A one-line horizontally scrolling strip, anchored to the top of the card between two offsets.</summary>
        private static RectTransform HScroll(Transform parent, float height, float bottomOffset, float topOffset)
        {
            var holder = UIFactory.Panel("hstrip", parent, Theme.Clear);
            var hrt = holder.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1); hrt.pivot = new Vector2(0.5f, 1);
            hrt.offsetMin = new Vector2(20, bottomOffset); hrt.offsetMax = new Vector2(-20, topOffset);

            var scroll = holder.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20f;

            var viewport = UIFactory.Panel("Viewport", holder.transform, Theme.Clear);
            var vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            // RectMask2D, not Mask: a Mask derives its shape from its Graphic's ALPHA, and this viewport is
            // deliberately fully transparent - so a Mask here clips the whole strip away and the tabs and chips
            // never appear. RectMask2D clips by rectangle and needs no graphic at all.
            viewport.AddComponent<RectMask2D>();
            scroll.viewport = vrt;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var crt = content.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 0); crt.anchorMax = new Vector2(0, 1); crt.pivot = new Vector2(0, 0.5f);
            var layout = content.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = crt;

            return crt;
        }

        // ----- rows -----

        private static void Rebuild()
        {
            if (_content == null) return;

            UIFactory.ClearChildren(_content);
            _visible.Clear();
            _hovered = null;
            Tooltip.Hide();

            string q = (_query ?? "").Trim();
            bool showHeaders = _tab.Length == 0;
            bool any = false;

            if (_view.NoneRow != null && Matches(_view.NoneRow, q) && _tab.Length == 0)
            {
                AddRow(_view.NoneRow);
                any = true;
            }

            // Favourites first, then the deterministic order the view builder produced.
            var pass1 = new List<Row>();
            var pass2 = new List<Row>();
            foreach (var row in _view.Rows)
            {
                if (!Include(row, q)) continue;
                if (UserPrefs.IsFavourite(row.ItemId)) pass1.Add(row); else pass2.Add(row);
            }

            ApplySort(pass1);
            ApplySort(pass2);

            if (pass1.Count > 0)
            {
                if (showHeaders) AddHeader("Favourites");
                foreach (var row in pass1) { AddRow(row); any = true; }
            }

            string lastCategory = null;
            foreach (var row in pass2)
            {
                if (showHeaders && row.CategoryKey != lastCategory)
                {
                    CategoryDef c = Catalog.GetCategory(row.CategoryKey);
                    AddHeader(c?.Label ?? row.CategoryKey);
                    lastCategory = row.CategoryKey;
                }
                AddRow(row);
                any = true;
            }

            if (!any)
            {
                var empty = UIFactory.Text("empty", "No matches.", _content, Theme.Body, TextAnchor.UpperLeft);
                empty.color = Theme.TextMuted;
                empty.gameObject.AddComponent<LayoutElement>().minHeight = RowHeight;
            }

            Interactions.PolishButtons(_content);
        }

        /// <summary>Reorders rows for the active sort mode while keeping them grouped by category, so switching
        /// mode never scrambles the tabs or orphans a section header.</summary>
        private static void ApplySort(List<Row> rows)
        {
            int mode = UserPrefs.SortMode;
            if (mode <= 0 || mode >= SortLabels.Length || rows.Count < 2) return;

            var order = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _view.Categories.Count; i++) order[_view.Categories[i].Key] = i;

            rows.Sort((a, b) =>
            {
                int ca = order.TryGetValue(a.CategoryKey ?? "", out int x) ? x : int.MaxValue;
                int cb = order.TryGetValue(b.CategoryKey ?? "", out int y) ? y : int.MaxValue;
                if (ca != cb) return ca.CompareTo(cb);

                int r = 0;
                switch (mode)
                {
                    case 1: r = string.Compare(a.Title, b.Title, StringComparison.InvariantCultureIgnoreCase); break;
                    case 2: r = Yield(b).CompareTo(Yield(a)); break;                      // biggest first
                    case 3: r = Growth(a).CompareTo(Growth(b)); break;                    // fastest first
                    case 4: r = Value(b).CompareTo(Value(a)); break;                      // most valuable first
                }
                if (r != 0) return r;

                r = string.Compare(a.Title, b.Title, StringComparison.InvariantCultureIgnoreCase);
                return r != 0 ? r : string.CompareOrdinal(a.ItemId, b.ItemId);
            });
        }

        private static int Yield(Row r) => r.Facts?.BaseYield ?? 0;

        /// <summary>Unknown growth time sorts last rather than first, so an additive does not lead a "fastest"
        /// list just because it has no plant.</summary>
        private static int Growth(Row r)
        {
            int h = r.Facts?.GrowthTimeHours ?? 0;
            return h > 0 ? h : int.MaxValue;
        }

        private static float Value(Row r) => r.Facts?.MarketValue ?? 0f;

        private static bool Include(Row row, string query)
        {
            // The current selection is always reachable: hiding or filtering it away would leave the player unable
            // to see what the field is set to.
            if (row.Selected) return true;

            if (!_showHidden && UserPrefs.IsHidden(row.ItemId)) return false;
            if (UserPrefs.OnlyFavourites && !UserPrefs.IsFavourite(row.ItemId)) return false;
            if (UserPrefs.OnlyDiscovered && row.Facts?.Discovered == false) return false;
            if (_tab.Length > 0 && !string.Equals(row.CategoryKey, _tab, StringComparison.Ordinal)) return false;

            foreach (string t in _tagFilter)
                if (!row.Tags.Contains(t)) return false;

            return Matches(row, query);
        }

        private static bool Matches(Row row, string query)
        {
            if (query.Length == 0) return true;
            if (Contains(row.Title, query)) return true;
            if (Contains(row.ItemId, query)) return true;

            CategoryDef c = Catalog.GetCategory(row.CategoryKey);
            if (c != null && Contains(c.Label, query)) return true;

            if (row.Facts != null)
                foreach (string e in row.Facts.Effects)
                    if (Contains(e, query)) return true;

            foreach (string t in row.Tags)
                if (Contains(Catalog.TagLabel(t), query)) return true;

            return false;
        }

        private static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack)
                && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddHeader(string label)
        {
            var lbl = UIFactory.Text("grp", (label ?? "").ToUpperInvariant(), _content, Theme.Caption,
                TextAnchor.LowerLeft, FontStyle.Bold);
            lbl.color = Theme.Accent;
            lbl.raycastTarget = false;
            var le = lbl.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 22; le.preferredHeight = 22;
        }

        private static void AddRow(Row row)
        {
            var go = UIFactory.Panel("row", _content, row.Selected ? Theme.AccentSubtle : Theme.Button);
            var img = go.GetComponent<Image>();
            if (img != null) { img.sprite = Theme.RoundedSprite(); img.type = Image.Type.Sliced; }
            var rt = go.GetComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = RowHeight; le.preferredHeight = RowHeight;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener((UnityAction)(() => Pick(row)));

            if (row.Selected)
            {
                var ol = go.AddComponent<Outline>();
                ol.effectColor = Theme.AccentBorder;
                ol.effectDistance = new Vector2(1, -1);
            }

            float textLeft = 10f;

            if (!row.IsNone && row.Facts?.Icon != null)
            {
                var iconGO = UIFactory.Panel("icon", go.transform, Theme.Clear);
                var iimg = iconGO.GetComponent<Image>();
                if (iimg != null)
                {
                    iimg.sprite = row.Facts.Icon;
                    // The panel was created transparent to have no background of its own; the sprite still has to
                    // be drawn at full opacity, and assigning a sprite does not touch the tint.
                    iimg.color = Color.white;
                    iimg.preserveAspect = true;
                    iimg.raycastTarget = false;
                }
                var irt = iconGO.GetComponent<RectTransform>();
                irt.anchorMin = new Vector2(0, 0.5f); irt.anchorMax = new Vector2(0, 0.5f);
                irt.pivot = new Vector2(0, 0.5f);
                irt.sizeDelta = new Vector2(26, 26);
                irt.anchoredPosition = new Vector2(6, 0);
                textLeft = 38f;
            }

            var title = UIFactory.Text("name", row.Title, go.transform, Theme.Body, TextAnchor.MiddleLeft);
            title.color = row.IsNone ? Theme.TextMuted : Theme.TextPrimary;
            title.raycastTarget = false;
            var nrt = title.rectTransform;
            nrt.anchorMin = Vector2.zero; nrt.anchorMax = Vector2.one;
            nrt.offsetMin = new Vector2(textLeft, 0); nrt.offsetMax = new Vector2(-200, 0);

            if (!row.IsNone)
            {
                string meta = Meta(row);
                if (meta.Length > 0)
                {
                    var m = UIFactory.Text("meta", meta, go.transform, Theme.Caption, TextAnchor.MiddleRight);
                    m.color = Theme.TextMuted;
                    m.raycastTarget = false;
                    var mrt = m.rectTransform;
                    mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one;
                    mrt.offsetMin = new Vector2(0, 0); mrt.offsetMax = new Vector2(-66, 0);
                }

                // Both buttons are added after the row's own Button, so they sit on top of it and win the click.
                bool hidden = UserPrefs.IsHidden(row.ItemId);
                AddRowButton(go.transform, "hide", hidden ? "o" : "x", -6f,
                    hidden ? Theme.DangerText : Theme.TextDisabled,
                    () => { UserPrefs.ToggleHidden(row.ItemId); BuildChips(); Rebuild(); });

                bool fav = UserPrefs.IsFavourite(row.ItemId);
                AddRowButton(go.transform, "fav", "*", -34f,
                    fav ? Theme.WarningText : Theme.TextDisabled,
                    () => { UserPrefs.ToggleFavourite(row.ItemId); Rebuild(); });
            }

            _visible.Add(new KeyValuePair<Row, RectTransform>(row, rt));
        }

        /// <summary>A small square glyph button pinned to the right edge of a row.</summary>
        private static void AddRowButton(Transform row, string name, string glyph, float xOffset, Color color, Action onClick)
        {
            var (go, btn, txt) = UIFactory.ButtonWithLabel(name, glyph, row, Theme.Clear, 26, 26);
            if (txt != null) { txt.fontSize = Theme.Label; txt.color = color; }
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(26, 26);
            rt.anchoredPosition = new Vector2(xOffset, 0);
            btn.onClick.AddListener((UnityAction)(() => onClick()));
        }

        /// <summary>The dim right-hand text on a row: the first couple of effects, or the growth time when the
        /// entry has no effects to show.</summary>
        private static string Meta(Row row)
        {
            ItemFacts f = row.Facts;
            if (f == null) return "";
            if (f.Effects.Count > 0)
                return f.Effects.Count <= 2
                    ? string.Join(", ", f.Effects)
                    : f.Effects[0] + ", " + f.Effects[1] + " +" + (f.Effects.Count - 2);
            if (f.HasPlant && f.GrowthTimeHours > 0) return f.GrowthTimeHours + " h";
            return "";
        }

        private static void Pick(Row row)
        {
            Action<ItemDefinition> cb = _onPick;
            ItemDefinition item = row.IsNone ? null : row.Item;
            Close();
            try { cb?.Invoke(item); }
            catch (Exception e) { Core.Log.Error("Clipwise: writing the selection back failed: " + e); }
        }

        // ----- hover -----

        /// <summary>
        /// Hit-tests the pointer against the visible rows instead of wiring pointer-enter events onto every one.
        /// Injecting a custom component into IL2CPP for that would cost a registered type, and the row list is
        /// short enough that a rectangle test per frame is cheaper than the plumbing.
        ///
        /// With no cursor movement the focused row from the event system takes over, which is what happens on a
        /// controller.
        /// </summary>
        private static void UpdateHover()
        {
            Row found = null;
            RectTransform anchor = null;
            bool cursor = false;

            Vector3 mouse = Input.mousePosition;
            bool mouseMoved = (mouse - _lastMouse).sqrMagnitude > 0.01f;
            if (mouseMoved) _lastMouse = mouse;

            Camera cam = null;
            Canvas canvas = _content != null ? _content.GetComponentInParent<Canvas>() : null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) cam = canvas.worldCamera;

            for (int i = 0; i < _visible.Count; i++)
            {
                RectTransform rt = _visible[i].Value;
                if (rt == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, mouse, cam))
                {
                    found = _visible[i].Key;
                    anchor = rt;
                    cursor = true;
                    break;
                }
            }

            if (found == null)
            {
                GameObject sel = EventSystem.current?.currentSelectedGameObject;
                if (sel != null)
                    for (int i = 0; i < _visible.Count; i++)
                        if (_visible[i].Value != null && _visible[i].Value.gameObject == sel)
                        {
                            found = _visible[i].Key;
                            anchor = _visible[i].Value;
                            break;
                        }
            }

            if (found == null || found.IsNone) { _hovered = null; Tooltip.Hide(); return; }

            _hovered = found;
            CategoryDef c = Catalog.GetCategory(found.CategoryKey);
            Tooltip.Show(_canvasRoot, found, c?.Label, cursor, anchor);
        }
    }
}

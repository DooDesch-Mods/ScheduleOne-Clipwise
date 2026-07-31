using System.Collections.Generic;
using Clipwise.Index;
using DooDesch.UI;
using S1API.UI;
using UnityEngine.UI;

namespace Clipwise.UI
{
    /// <summary>
    /// The floating info panel that follows the cursor while an entry is hovered - the thing the vanilla clipboard
    /// never had. Vanilla shows one name in a fixed label; this shows effects, yield, growth time, market value and
    /// discovery state for the entry under the pointer.
    ///
    /// Positioning deliberately avoids <c>RectTransform.GetWorldCorners</c>: under Il2CppInterop the array argument
    /// is not written back, so every corner reads as zero and the panel lands in a screen corner. Screen point plus
    /// <c>ScreenPointToLocalPointInRectangle</c> (a by-value struct out) is exact, and the result is clamped to the
    /// canvas so the panel never hangs off an edge.
    ///
    /// With a controller there is no cursor, so the caller passes the focused row and the panel anchors to it.
    /// </summary>
    internal static class Tooltip
    {
        private const float CursorGap = 16f;
        private const float Pad = 10f;
        private const float Width = 250f;

        private static GameObject _root;
        private static RectTransform _rt;
        private static RectTransform _canvasRT;
        private static Canvas _canvas;
        private static Text _title;
        private static Text _subtitle;
        private static Text _body;

        private static RectTransform _anchor;
        private static bool _useCursor;
        private static string _shownFor;

        public static bool IsVisible => _root != null && _root.activeSelf;

        /// <summary>Show the panel for one entry. <paramref name="anchor"/> is where it parks when there is no
        /// cursor to follow; pass the focused row's transform.</summary>
        public static void Show(Transform canvasRoot, Row row, string categoryLabel, bool useCursor, RectTransform anchor)
        {
            if (!Config.Preferences.Tooltips || row == null || row.Facts == null) { Hide(); return; }

            Build(canvasRoot);
            if (_root == null) return;

            _anchor = anchor;
            _useCursor = useCursor;

            if (_shownFor != row.ItemId)
            {
                _shownFor = row.ItemId;
                _title.text = row.Title;
                _subtitle.text = Subtitle(row, categoryLabel);
                _body.text = Body(row.Facts, row);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_rt);
            }

            if (!_root.activeSelf) _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            Reposition();
        }

        public static void Hide()
        {
            _shownFor = null;
            _anchor = null;
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }

        /// <summary>Destroy the panel, e.g. on a scene change - the canvas it lived on is gone.</summary>
        public static void Clear()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null; _rt = null; _canvas = null; _canvasRT = null;
            _title = null; _subtitle = null; _body = null;
            _shownFor = null; _anchor = null;
        }

        public static void Tick()
        {
            if (!IsVisible) return;
            if (_canvasRT == null) { Clear(); return; }
            Reposition();
        }

        private static string Subtitle(Row row, string categoryLabel)
        {
            string source = !string.IsNullOrEmpty(row.Source) ? row.Source
                          : row.Facts.IsModded ? "mod" : "vanilla";
            return string.IsNullOrEmpty(categoryLabel) ? source : categoryLabel + "  -  " + source;
        }

        private static string Body(ItemFacts facts, Row row)
        {
            var sb = new System.Text.StringBuilder();

            List<KeyValuePair<string, string>> rows = facts.TooltipRows();
            for (int i = 0; i < rows.Count; i++)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(rows[i].Key).Append(": ").Append(rows[i].Value);
            }

            if (!string.IsNullOrEmpty(row.Description))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append('\n').Append(row.Description);
            }

            if (facts.NameIdMismatch)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("\nWarning: this item's asset name and ID differ, which breaks selecting it in co-op.");
            }

            if (sb.Length == 0) sb.Append(facts.ItemId);
            return sb.ToString();
        }

        private static void Build(Transform canvasRoot)
        {
            if (_root != null && _rt != null && _canvasRT != null) return;
            Clear();
            if (canvasRoot == null) return;

            _canvas = canvasRoot.GetComponentInParent<Canvas>();
            if (_canvas == null) _canvas = canvasRoot.GetComponent<Canvas>();
            if (_canvas == null) return;
            _canvasRT = _canvas.GetComponent<RectTransform>();

            _root = UIFactory.Panel("CW_Tooltip", canvasRoot, Theme.BgElevated);
            var img = _root.GetComponent<Image>();
            if (img != null) { img.sprite = Theme.RoundedSprite(); img.type = Image.Type.Sliced; img.raycastTarget = false; }
            var ol = _root.AddComponent<Outline>();
            ol.effectColor = Theme.HairlineStrong;
            ol.effectDistance = new Vector2(1, -1);

            _rt = _root.GetComponent<RectTransform>();
            _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot = new Vector2(0f, 1f);   // grows right and down from the cursor
            _rt.sizeDelta = new Vector2(Width, 100f);

            var layout = _root.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2f;
            layout.padding = new RectOffset((int)Pad, (int)Pad, (int)Pad, (int)Pad);
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var fitter = _root.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            _title = Line("title", Theme.Label, Theme.TextPrimary, FontStyle.Bold);
            _subtitle = Line("subtitle", Theme.Caption, Theme.Accent, FontStyle.Normal);
            _body = Line("body", Theme.Body, Theme.TextMuted, FontStyle.Normal);

            _root.SetActive(false);
        }

        private static Text Line(string name, int size, Color color, FontStyle style)
        {
            Text t = UIFactory.Text(name, "", _root.transform, size, TextAnchor.UpperLeft, style);
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = Width - 2f * Pad;
            return t;
        }

        /// <summary>Places the panel next to the cursor (or the focused row) and pulls it back inside the canvas,
        /// flipping to the other side when there is no room.</summary>
        private static void Reposition()
        {
            Vector2 size = _rt.rect.size;
            Vector2 canvasSize = _canvasRT.rect.size;
            Vector2 origin;

            if (_useCursor)
            {
                Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRT, Input.mousePosition, cam, out origin);
            }
            else if (_anchor != null)
            {
                // Local point inside the canvas, computed from by-value transform properties only.
                Vector3 world = _anchor.TransformPoint(new Vector3(_anchor.rect.xMax, _anchor.rect.center.y, 0f));
                Vector3 local = _canvasRT.InverseTransformPoint(world);
                origin = new Vector2(local.x, local.y + size.y * 0.5f);
            }
            else
            {
                origin = Vector2.zero;
            }

            float x = origin.x + CursorGap;
            float y = origin.y - CursorGap;

            float right = canvasSize.x * 0.5f;
            float left = -right;
            float top = canvasSize.y * 0.5f;
            float bottom = -top;

            if (x + size.x > right) x = origin.x - CursorGap - size.x;   // flip to the left of the cursor
            if (y - size.y < bottom) y = origin.y + CursorGap + size.y;  // flip above the cursor

            x = Mathf.Clamp(x, left + 4f, right - size.x - 4f);
            y = Mathf.Clamp(y, bottom + size.y + 4f, top - 4f);

            _rt.anchoredPosition = new Vector2(x, y);
        }
    }
}

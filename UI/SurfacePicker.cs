using System;
using System.Collections.Generic;
using System.Text;
using Clipwise.Index;
using Clipwise.Model;
using Sideload.Api;
using UnityEngine;

namespace Clipwise.UI
{
    /// <summary>
    /// The picker as a Sideload surface instead of hand-built uGUI.
    ///
    /// WHY. <see cref="ItemPicker"/> is 730 lines that build a scrim, a scroll rect, an input field, a chip bar,
    /// tab buttons and a row pool by hand, plus 218 more for a tooltip. A surface is the same page engine the
    /// phone runs, mounted into a RectTransform this mod already resolves, so all of that becomes markup and a
    /// stylesheet - which is also the only sane way to answer "make it look exactly like the vanilla clipboard".
    ///
    /// THIS IS THE SPIKE, not the replacement. It is off unless <c>SurfacePicker</c> is switched on in
    /// preferences, and the uGUI picker stays the default until this one does everything it does: tag chips,
    /// favourites, sort modes, hidden items and the hover tooltip are all still only over there.
    ///
    /// What it proves: the shim compiles in, the bundle resolves, a surface mounts on the clipboard's own canvas
    /// and paints, and picking through the page writes back through the same handler the uGUI picker uses.
    ///
    /// Mounting is deliberately onto the SAME canvas <see cref="Patches.ItemFieldUIPatch"/> already resolves - the
    /// game's <c>ManagementWorldspaceCanvas</c>, which is screen-space-overlay and carries a GraphicRaycaster.
    /// Both matter: a surface on a camera-lit canvas comes out tone-mapped, and without a raycaster nothing in
    /// the page can be clicked.
    /// </summary>
    internal static class SurfacePicker
    {
        internal const string SurfaceId = "clipwise";
        private const string BundlePrefix = "Clipwise.Assets.clipwise";

        /// <summary>The card is 540x620 in the uGUI picker and the vanilla clipboard is 546x751, so the short side
        /// agrees. Passing it takes the phone's contract - the page is authored for 540 and scales with the panel -
        /// rather than device pixels, which would make the page a different size on every resolution.</summary>
        private const float DesignShortSide = 540f;

        private static SurfaceHandle _surface;
        private static GameObject _host;
        private static View _view;
        private static Action<ItemDefinition> _onPick;

        internal static bool IsOpen => _host != null;

        /// <summary>
        /// Show the picker over the clipboard. False means nothing was mounted and the caller must let the vanilla
        /// grid run - never leave the clipboard half-functional because an experiment did not come up.
        /// </summary>
        internal static bool TryOpen(Transform canvasRoot, View view, Action<ItemDefinition> onPick)
        {
            if (canvasRoot == null || view == null || onPick == null) return false;
            if (view.Rows.Count == 0) return false;
            if (!Surfaces.Available) return false;

            try
            {
                Close();

                _view = view;
                _onPick = onPick;

                // AddComponent rather than the (string, params Type[]) constructor: under IL2CPP that overload
                // wants an Il2CppReferenceArray<Il2CppSystem.Type> and a managed Type will not convert.
                _host = new GameObject("CW_Surface");
                var rect = _host.AddComponent<RectTransform>();
                rect.SetParent(canvasRoot, false);

                // Anchored to the middle rather than stretched: the card is a fixed size on a canvas that is not.
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(540f, 620f);
                _host.transform.SetAsLastSibling();

                // Named, because the signature takes designShortSide BEFORE the assembly and swapping them is a
                // silent mismatch rather than a compile error. Mount always hands a handle back, even when the
                // host is absent - IsMounted is the question that has an answer.
                _surface = Surfaces.Mount(rect, SurfaceId, BundlePrefix,
                                          designShortSide: DesignShortSide,
                                          hostAssembly: typeof(SurfacePicker).Assembly);

                _surface.OnCall("picker.view", _ => ViewJson(_view))
                        .OnCall("picker.pick", Pick)
                        .OnCall("picker.fav", Fav)
                        // The only way out that does not choose something. A surface has no back gesture - right
                        // click and Escape belong to the phone - so without this the picker can be opened and
                        // never left except by picking, which is not a choice the player asked to be given.
                        .OnCall("picker.back", _ => { Close(); return "ok"; });

                if (!Surfaces.IsMounted(SurfaceId)) { Close(); return false; }
                return true;
            }
            catch (Exception e)
            {
                Core.Log.Warning("[Clipwise] surface picker failed to open: " + e.Message);
                Close();
                return false;
            }
        }

        internal static void Close()
        {
            try
            {
                if (_surface != null || _host != null) Surfaces.Unmount(SurfaceId);
                if (_host != null) UnityEngine.Object.Destroy(_host);
            }
            catch (Exception e) { Core.Log.Warning("[Clipwise] surface picker failed to close: " + e.Message); }

            _surface = null;
            _host = null;
            _view = null;
            _onPick = null;
        }

        /// <summary>
        /// One item id in, the same write-back the uGUI picker performs. The empty id is the None/Any line and is a
        /// real answer rather than a miss, so it is matched before the lookup.
        /// </summary>
        private static string Pick(string itemId)
        {
            if (_view == null || _onPick == null) return "error";

            // The None/Any line is not in Rows and its id is empty, so it has to be matched before the lookup or
            // a pot set to "Any" could never be set back to it. Vanilla keeps the same option as the X tile.
            if (string.IsNullOrEmpty(itemId) && _view.NoneRow != null)
            {
                Action<ItemDefinition> none = _onPick;
                Close();
                none(null);
                return "ok";
            }

            foreach (Row row in _view.Rows)
            {
                if (!string.Equals(row.ItemId, itemId ?? string.Empty, StringComparison.Ordinal)) continue;
                Action<ItemDefinition> handler = _onPick;
                ItemDefinition item = row.Item;
                Close();
                handler(item);
                return "ok";
            }

            return "error";
        }

        /// <summary>
        /// Star or unstar one item, and answer the state it ended in so the page does not have to guess.
        ///
        /// Written through <see cref="Prefs.UserPrefs"/> rather than kept in the page: a favourite is the player's,
        /// not this picker's, and the uGUI card and this surface have to agree about it the moment either changes.
        /// </summary>
        private static string Fav(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return "error";
            Prefs.UserPrefs.ToggleFavourite(itemId);
            return Prefs.UserPrefs.IsFavourite(itemId) ? "on" : "off";
        }

        // ---- the wire ---------------------------------------------------------------------------------------

        /// <summary>
        /// The whole picker as one JSON object: a title, the tabs, and every row with the tab it belongs to.
        ///
        /// Sent whole rather than paged because the page filters and searches on its own - a keystroke must not
        /// cost a bridge call - and the biggest real list is a few hundred rows of two short strings.
        /// </summary>
        private static string ViewJson(View view)
        {
            if (view == null) return "";

            var sb = new StringBuilder(1024);
            sb.Append("{\"title\":").Append(Quote(view.Title));

            sb.Append(",\"tabs\":[");
            for (int i = 0; i < view.Categories.Count; i++)
            {
                CategoryDef category = view.Categories[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(Quote(category.Key))
                  .Append(",\"label\":").Append(Quote(category.Label)).Append('}');
            }
            sb.Append(']');

            // "Any" or "None", when the field allows it. Sent apart from the rows because it belongs at the top
            // whatever is being searched or sorted - it is the way out, not one of the choices.
            sb.Append(",\"none\":");
            if (view.NoneRow != null)
                sb.Append("{\"name\":").Append(Quote(view.NoneRow.Title))
                  .Append(",\"sel\":").Append(view.NoneRow.Selected ? "true" : "false").Append('}');
            else
                sb.Append("null");

            sb.Append(",\"rows\":[");
            bool first = true;
            foreach (Row row in view.Rows)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":").Append(Quote(row.ItemId))
                  .Append(",\"name\":").Append(Quote(row.Title))
                  .Append(",\"tab\":").Append(Quote(row.CategoryKey))
                  // What the registering mod wrote about this item - for a bred strain, the pair it came from.
                  .Append(",\"note\":").Append(Quote(row.Description))
                  // The page draws the game's own seeds and everything a mod added under separate headings, which
                  // is the one split a player actually thinks in.
                  .Append(",\"vanilla\":").Append(row.Facts != null && !row.Facts.IsModded ? "true" : "false")
                  // Tier, or 0. Read off the tag rather than asked of the mod: a tag is what every mod already
                  // sends, so sorting by tier costs nothing to support and works for the next one too.
                  .Append(",\"tier\":").Append(TierOf(row))
                  .Append(",\"fav\":").Append(Prefs.UserPrefs.IsFavourite(row.ItemId) ? "true" : "false")
                  .Append(",\"sel\":").Append(row.Selected ? "true" : "false")
                  .Append('}');
            }
            sb.Append("]}");

            return sb.ToString();
        }

        /// <summary>
        /// The number in a <c>...:tier/N</c> tag, or 0 when the item carries none.
        ///
        /// By convention rather than by contract - there is no tier field in the API - and that is on purpose:
        /// every mod already sends tags, so the sort works for any of them without a new call to adopt.
        /// </summary>
        private static int TierOf(Row row)
        {
            if (row?.Tags == null) return 0;

            foreach (string tag in row.Tags)
            {
                if (tag == null) continue;
                int cut = tag.IndexOf(":tier/", StringComparison.Ordinal);
                if (cut < 0) continue;
                if (int.TryParse(tag.Substring(cut + 6), out int tier)) return tier;
            }

            return 0;
        }

        /// <summary>A JSON string, or <c>null</c>. Only the five escapes JSON requires plus control characters -
        /// item titles are game text, not arbitrary input, but a stray quote would still break the parse.</summary>
        private static string Quote(string value)
        {
            if (value == null) return "null";

            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}

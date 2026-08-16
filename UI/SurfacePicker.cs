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

        /// <summary>Only for a clipboard whose own card cannot be measured - see <see cref="Fit"/>. Matches the
        /// uGUI picker's card, which is the size this page was drawn against before it had anything to copy.</summary>
        private const float FallbackWidth = 540f;
        private const float FallbackHeight = 620f;

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

                float design = Fit(rect, canvasRoot);
                _host.transform.SetAsLastSibling();

                // Named, because the signature takes designShortSide BEFORE the assembly and swapping them is a
                // silent mismatch rather than a compile error. Mount always hands a handle back, even when the
                // host is absent - IsMounted is the question that has an answer.
                _surface = Surfaces.Mount(rect, SurfaceId, BundlePrefix,
                                          designShortSide: design,
                                          hostAssembly: typeof(SurfacePicker).Assembly);

                _surface.OnCall("picker.view", _ => ViewJson(_view))
                        .OnCall("picker.pick", Pick)
                        .OnCall("picker.fav", Fav)
                        .OnCall("picker.hide", Hide)
                        .OnCall("picker.state", State)
                        // The only way out that does not choose something. A surface has no back gesture - right
                        // click and Escape belong to the phone - so without this the picker can be opened and
                        // never left except by picking, which is not a choice the player asked to be given.
                        .OnCall("picker.back", _ => { Close(); return "ok"; });

                if (!Surfaces.IsMounted(SurfaceId)) { Close(); return false; }

                // After the mount, because the page asks for its pictures as soon as it builds and the store has
                // to have them by then. Cached across opens, so this is only slow the first time.
                SurfaceIcons.Supply(_surface, view);

                return true;
            }
            catch (Exception e)
            {
                Core.Log.Warning("[Clipwise] surface picker failed to open: " + e.Message);
                Close();
                return false;
            }
        }

        /// <summary>
        /// Put the host exactly where the game's own selection card sits, and answer the width the page should be
        /// authored for.
        ///
        /// NOT a guess at a size. The first version anchored a fixed 540x620 to the middle of the root canvas, and
        /// on a real clipboard it hung over the top and bottom edges of the board - the card it replaces is inset
        /// inside the clipboard, not the size of it. `ItemSelector` is that card
        /// (`ManagementInterface.ItemSelectorScreen`, ScheduleOne.UI.Management/ItemFieldUI.cs:96), so its own
        /// RectTransform is the answer to both questions at once: where to sit, and how wide the page is.
        ///
        /// Parented to the card's PARENT rather than to the canvas root, because an anchored position means
        /// nothing without the parent it is anchored in. Falls back to the old fixed rect when the card cannot be
        /// found, which is a picker in the wrong place rather than no picker at all.
        /// </summary>
        private static float Fit(RectTransform rect, Transform canvasRoot)
        {
            rect.SetParent(canvasRoot, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(FallbackWidth, FallbackHeight);

            try
            {
                ManagementInterface mi = Singleton<ManagementInterface>.Instance;
                ItemSelector screen = mi != null ? mi.ItemSelectorScreen : null;
                RectTransform card = screen != null ? screen.GetComponent<RectTransform>() : null;

                if (card == null || card.parent == null)
                {
                    Core.Log.Warning("[Clipwise] no ItemSelector card to sit in - using " + FallbackWidth + "x" + FallbackHeight + ".");
                    return FallbackWidth;
                }

                RectTransform holder = card.parent as RectTransform;
                if (holder == null || !holder.gameObject.activeInHierarchy)
                {
                    Core.Log.Warning("[Clipwise] the ItemSelector's holder is not on screen - using the fallback size.");
                    return FallbackWidth;
                }

                // THE HOLDER FOR PLACE, THE PAPER FOR SIZE, and every part of that was learned from a
                // screenshot.
                //
                // Copying the card's anchors AND its anchoredPosition put the page beside the clipboard: a
                // position means nothing without the anchors it was measured against, and the card carries a
                // layout of its own that this object does not have. Filling the holder instead put it in the
                // right place and made it too big - the holder is the screen area, the card is the sheet inside
                // it. So: centred in the holder, at the card's own size.
                // The screen's own rect is the whole clipboard, edge to edge - taking it covered the wooden
                // frame that vanilla leaves showing. The paper inside it is what the player calls the card, and
                // the option grid is a child of that paper, so its PARENT is the thing to measure.
                RectTransform paper = card;
                try
                {
                    RectTransform grid = screen.OptionContainer;
                    if (grid != null && grid.parent is RectTransform inner && inner != card) paper = inner;
                }
                catch { }

                Vector2 size = paper.rect.size;
                if (size.x < 1f || size.y < 1f) size = new Vector2(FallbackWidth, FallbackHeight);

                rect.SetParent(holder, false);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = size;
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;

                Core.Log.Msg("[Clipwise] surface " + size.x.ToString("0") + "x" + size.y.ToString("0")
                             + " from '" + paper.name + "', centred in '" + holder.name + "' ("
                             + holder.rect.width.ToString("0") + "x" + holder.rect.height.ToString("0") + ").");

                return size.x;
            }
            catch (Exception e)
            {
                Core.Log.Warning("[Clipwise] could not place the surface: " + e.Message);
                return FallbackWidth;
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

        /// <summary>Hide or unhide one item, answering the state it ended in. Hidden entries drop out of the
        /// list until the page asks for them back.</summary>
        private static string Hide(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return "error";
            Prefs.UserPrefs.ToggleHidden(itemId);
            return Prefs.UserPrefs.IsHidden(itemId) ? "on" : "off";
        }

        /// <summary>
        /// The three view settings that outlive one open: <c>sort|favourites|discovered</c>.
        ///
        /// Kept on the C# side because they are the player's and not this page's - the same three were persisted
        /// before this picker existed, and a player who set "sort by yield" last week should not have to set it
        /// again because the screen was rebuilt in a different technology.
        /// </summary>
        private static string State(string arg)
        {
            string[] parts = (arg ?? string.Empty).Split('|');
            if (parts.Length < 3) return "error";

            if (int.TryParse(parts[0], out int sort)) Prefs.UserPrefs.SortMode = sort;
            Prefs.UserPrefs.OnlyFavourites = parts[1] == "1";
            Prefs.UserPrefs.OnlyDiscovered = parts[2] == "1";
            Prefs.UserPrefs.Flush();
            return "ok";
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

            // The heading over everything a mod added, taken from the category its own rows are in. Worked out
            // here rather than in the page: a category key is namespaced (`clipwise:vanilla`), and the page
            // guessing at that shape printed the game's own seeds under the heading "VANILLA" twice.
            sb.Append(",\"added\":").Append(Quote(AddedLabel(view)));

            // The three settings that outlive one open, so the page opens the way the player left it.
            sb.Append(",\"sort\":").Append(Prefs.UserPrefs.SortMode)
              .Append(",\"onlyFav\":").Append(Prefs.UserPrefs.OnlyFavourites ? "true" : "false")
              .Append(",\"onlyDisc\":").Append(Prefs.UserPrefs.OnlyDiscovered ? "true" : "false")
              .Append(",\"hiddenCount\":").Append(Prefs.UserPrefs.HiddenCount)
              // The tooltip preference outlived the card it was written for: the bubble beside a tile is the
              // same thing in the same place, so the same switch turns it off.
              .Append(",\"tips\":").Append(Config.Preferences.Tooltips ? "true" : "false");

            // One chip per tag a mod declared. `clipwise:` tags are the host's own bookkeeping - "is this modded",
            // "is this discovered" - and each already has a chip or a section of its own.
            sb.Append(",\"tags\":[");
            bool firstTag = true;
            foreach (string tag in view.Tags)
            {
                if (string.IsNullOrEmpty(tag) || tag.StartsWith("clipwise:", StringComparison.Ordinal)) continue;
                if (!firstTag) sb.Append(',');
                firstTag = false;
                sb.Append("{\"id\":").Append(Quote(tag))
                  .Append(",\"label\":").Append(Quote(Catalog.TagLabel(tag))).Append('}');
            }
            sb.Append(']');

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
                  // What the hover bubble says beyond the name and the pair. Sent with the row rather than
                  // fetched on hover: a bubble that has to ask before it can appear arrives after the pointer
                  // has moved on.
                  .Append(",\"effects\":").Append(Effects(row))
                  .Append(",\"hidden\":").Append(Prefs.UserPrefs.IsHidden(row.ItemId) ? "true" : "false")
                  // Tri-state on purpose: an item that reports nothing about discovery is not the same as one
                  // that reports "not discovered", and the filter must not swallow the first kind.
                  .Append(",\"disc\":").Append(row.Facts?.Discovered == false ? "false" : "true")
                  // What the sort modes read. Sent rather than asked for, because a sort must not cost a call
                  // per comparison.
                  .Append(",\"yield\":").Append((row.Facts?.BaseYield ?? 0).ToString(Culture))
                  .Append(",\"growth\":").Append(Growth(row).ToString(Culture))
                  .Append(",\"value\":").Append((row.Facts?.MarketValue ?? 0f).ToString("0.##", Culture))
                  .Append(",\"tags\":").Append(Tags(row))
                  .Append('}');
            }
            sb.Append("]}");

            return sb.ToString();
        }

        /// <summary>Numbers on the wire are read by a JSON parser, not by a person, so they are written the way
        /// that parser expects whatever the machine's locale has to say about decimal points.</summary>
        private static readonly System.Globalization.CultureInfo Culture = System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>Growth time in hours, with "no plant" sorting last rather than first - an additive must not
        /// lead a "fastest" list just because it has no growth time at all.</summary>
        private static int Growth(Row row)
        {
            int hours = row?.Facts?.GrowthTimeHours ?? 0;
            return hours > 0 ? hours : int.MaxValue;
        }

        /// <summary>The tags a mod put on this item, as a JSON array. The host's own bookkeeping tags are left
        /// out for the same reason they get no chip.</summary>
        private static string Tags(Row row)
        {
            if (row?.Tags == null || row.Tags.Count == 0) return "[]";

            var sb = new StringBuilder(64);
            sb.Append('[');
            bool first = true;
            foreach (string tag in row.Tags)
            {
                if (string.IsNullOrEmpty(tag) || tag.StartsWith("clipwise:", StringComparison.Ordinal)) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Quote(tag));
            }
            return sb.Append(']').ToString();
        }

        /// <summary>This item's effects as a JSON array, or <c>[]</c>.</summary>
        private static string Effects(Row row)
        {
            var list = row?.Facts?.Effects;
            if (list == null || list.Count == 0) return "[]";

            var sb = new StringBuilder(64);
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Quote(list[i]));
            }
            return sb.Append(']').ToString();
        }

        /// <summary>
        /// What to call the half of the list a mod contributed - its own category's label, or a plain fallback
        /// when every row is the game's.
        /// </summary>
        private static string AddedLabel(View view)
        {
            foreach (Row row in view.Rows)
            {
                if (row.Facts == null || !row.Facts.IsModded) continue;
                CategoryDef category = Catalog.GetCategory(row.CategoryKey);
                if (category != null && !string.IsNullOrEmpty(category.Label)) return category.Label;
            }

            return "Added by mods";
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

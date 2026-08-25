using System;
using System.Collections.Generic;
using Clipwise.Config;
using Clipwise.Index;
using Clipwise.UI;
using HarmonyLib;

namespace Clipwise.Patches
{
    /// <summary>
    /// Replaces the small icon grid behind the filter window's "Add" button with the Clipwise spread.
    ///
    /// ONE HOOK COVERS EVERY FILTERABLE THING. Shelves, racks, safes, the vehicle boot, and both slots on a
    /// packaging station all reach the same single window: <c>ItemSlotUI</c> shows a filter button when its slot
    /// says <c>CanPlayerSetFilter</c>, that button calls
    /// <c>ItemUIManager.Instance.FilterConfigPanel.Open(ui)</c> (ScheduleOne.UI.Items/ItemSlotFilterButton.cs:64),
    /// and there is exactly one panel. So there is exactly one Add button to intercept.
    ///
    /// THE WINDOW ITSELF IS NOT TOUCHED. Only <c>AddClicked</c> is taken over - the panel keeps its own list, its
    /// whitelist/blacklist switch, its quality row and its dropdown.
    /// </summary>
    [HarmonyPatch(typeof(FilterConfigPanel), nameof(FilterConfigPanel.AddClicked))]
    internal static class FilterAddPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(FilterConfigPanel __instance)
        {
            if (!Preferences.Enabled) return true;

            try
            {
                ItemSlot slot = __instance.OpenSlot;
                if (slot == null) return true;

                List<ItemDefinition> options = FilterOptions();
                if (options.Count == 0) return true;

                Transform canvasRoot = ResolveCanvasRoot(__instance);
                if (canvasRoot == null) return true;

                View view = ViewBuilder.BuildFilter("Filter", options);
                view.Owner = OwnerLine(slot);
                MarkAlreadyFiltered(view, slot);

                FilterConfigPanel panel = __instance;

                if (!SurfacePicker.TryOpen(canvasRoot, view, (item, want) => Apply(panel, slot, item, want))) return true;

                // The window closes itself on the next mouse-up outside its own rect
                // (ScheduleOne.UI.Items/FilterConfigPanel.cs:181-209). Every click on the spread is outside it, so
                // the flag vanilla clears when it opens the search is cleared here too - and FilterUpdatePatch
                // holds the rest of that check off while the spread is up.
                try { panel.mouseUp = false; } catch { }

                return false;
            }
            catch (Exception e)
            {
                Core.WarnThrottled("filter-add", "Clipwise: could not take over the filter's item list, using the vanilla grid: " + e.Message);
                return true;
            }
        }

        /// <summary>
        /// Puts one item on the slot's filter or takes it off again, through the same setter vanilla's own
        /// <c>AddItem</c> / <c>RemoveItem</c> use (ScheduleOne.UI.Items/FilterConfigPanel.cs:433-448) - so the
        /// change replicates, and the window's own list redraws itself off <c>onFilterChange</c>.
        ///
        /// Answers the state the filter ACTUALLY ended in, read back out of the list rather than assumed, because
        /// the tick on the page is drawn from this answer.
        /// </summary>
        private static bool Apply(FilterConfigPanel panel, ItemSlot slot, ItemDefinition item, bool want)
        {
            try { panel.mouseUp = false; } catch { }

            if (item == null || slot == null) return false;

            try
            {
                string id = item.ID;
                if (string.IsNullOrEmpty(id)) return false;

                SlotFilter filter = slot.PlayerFilter;
                if (filter == null || filter.ItemIDs == null) return false;

                // Compared here rather than with List.Contains/Remove: an interop list compares wrappers, and a
                // managed string handed to it is a fresh one every call.
                int at = -1;
                for (int i = 0; i < filter.ItemIDs.Count; i++)
                    if (string.Equals(filter.ItemIDs[i], id, StringComparison.Ordinal)) { at = i; break; }

                if (want && at < 0) filter.ItemIDs.Add(id);
                else if (!want && at >= 0) filter.ItemIDs.RemoveAt(at);
                else return at >= 0;   // already as asked - nothing to write, nothing to replicate

                slot.SetPlayerFilter(filter);

                // Read back rather than returned from `want`: SetPlayerFilter goes through the slot's owner and
                // out over the network, and the list this answer is drawn from is the one the game now holds.
                SlotFilter after = slot.PlayerFilter;
                if (after == null || after.ItemIDs == null) return want;
                for (int i = 0; i < after.ItemIDs.Count; i++)
                    if (string.Equals(after.ItemIDs[i], id, StringComparison.Ordinal)) return true;
                return false;
            }
            catch (Exception e)
            {
                Core.Log.Warning("Clipwise: changing the filter failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Every item this list may name: what the game itself offers in a filter
        /// (ScheduleOne.UI.Items/FilterConfigPanel.cs:271-293), plus everything a mod has FILED with Clipwise.
        ///
        /// The second half is not decoration. <c>UsableInFilters</c> is a flag on the definition, so a mod that
        /// keeps its items out of the game's own grid - which is the right call when the grid instantiates one
        /// prefab per entry - has no way to be in a better one. A Clipwise claim is that way, and it carries the
        /// thing the flag cannot: a claim is filed when the player MEETS the item, so a catalogue of seven
        /// hundred strains shows up here as the handful this save has bred, under the heading its own mod
        /// registered.
        ///
        /// Only items that resolve in the registry are taken. A claim is a label, not an item; an id that names
        /// nothing would put a tile on the page that no filter could ever match.
        /// </summary>
        internal static List<ItemDefinition> FilterOptions()
        {
            var options = new List<ItemDefinition>(256);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var registry = GameRegistry.Instance;
            if (registry == null) return options;

            var all = registry.GetAllItems();
            if (all == null) return options;

            for (int i = 0; i < all.Count; i++)
            {
                ItemDefinition def = all[i];
                if (def == null) continue;

                try
                {
                    if (!def.UsableInFilters) continue;
                    string id = def.ID;
                    if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                }
                catch { continue; }

                options.Add(def);
            }

            foreach (string itemId in Catalog.Resolved().Keys)
            {
                if (string.IsNullOrEmpty(itemId) || seen.Contains(itemId)) continue;

                ItemDefinition def;
                try { def = registry._GetItem(itemId, false); }
                catch { continue; }

                if (def == null || !seen.Add(itemId)) continue;
                options.Add(def);
            }

            return options;
        }

        /// <summary>Ticks the rows this slot already filters on, so the spread shows what is on the list rather
        /// than making the player remember it.</summary>
        private static void MarkAlreadyFiltered(View view, ItemSlot slot)
        {
            try
            {
                SlotFilter filter = slot.PlayerFilter;
                if (filter == null || filter.ItemIDs == null || filter.ItemIDs.Count == 0) return;

                var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < filter.ItemIDs.Count; i++) chosen.Add(filter.ItemIDs[i]);

                foreach (Row row in view.Rows)
                    if (chosen.Contains(row.ItemId)) row.Selected = true;
            }
            catch { }
        }

        /// <summary>What the list is for, in the words the window itself uses.</summary>
        private static string OwnerLine(ItemSlot slot)
        {
            try
            {
                return slot.PlayerFilter != null && slot.PlayerFilter.Type == SlotFilter.EType.Blacklist
                    ? "Unallowed items"
                    : "Allowed items";
            }
            catch { return ""; }
        }

        private static Transform ResolveCanvasRoot(FilterConfigPanel panel)
        {
            var canvas = panel.GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            Canvas root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            return root.transform;
        }
    }

    /// <summary>
    /// Holds the filter window's own click-outside-to-close check off while the spread is up.
    ///
    /// Its <c>Update</c> closes the window on any mouse-up outside its rect, and the whole spread is outside it -
    /// so without this the first click on the page takes the window (and its <c>OpenSlot</c>) away underneath the
    /// picker. A patch class of its own on purpose: a Harmony patch class is all or nothing, and a dead target in
    /// one must not take the Add button's patch down with it.
    /// </summary>
    [HarmonyPatch(typeof(FilterConfigPanel), "Update")]
    internal static class FilterUpdatePatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !SurfacePicker.IsOpen;
    }
}

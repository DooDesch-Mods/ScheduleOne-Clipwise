using System;
using System.Collections.Generic;
using Clipwise.Config;
using Clipwise.Index;
using Clipwise.UI;
using HarmonyLib;

namespace Clipwise.Patches
{
    /// <summary>
    /// Intercepts the click on a management-clipboard item field and shows Clipwise's picker instead of the vanilla
    /// icon grid.
    ///
    /// This is the earliest point that still has everything needed and nothing that must not be touched: the option
    /// list has been read but not yet handed to the shared <c>ItemSelector</c> screen, so no vanilla UI has been
    /// built and there is nothing to tear down. The copy taken here is a fresh managed list - the field's own
    /// <c>Options</c> list is never sorted, filtered or reordered, because for a pot it is the same collection whose
    /// order decides which seed a botanist grabs when the pot is set to "Any".
    ///
    /// The prefix returns true (letting vanilla run) whenever Clipwise cannot or should not help: the mod is off,
    /// the field has too few options to be worth a tabbed picker, the canvas cannot be found, or building the picker
    /// threw. A clipboard that behaves exactly as before is a much better failure than half a UI.
    /// </summary>
    [HarmonyPatch(typeof(ItemFieldUI), nameof(ItemFieldUI.Clicked))]
    internal static class ItemFieldUIPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ItemFieldUI __instance)
        {
            if (!Preferences.Enabled) return true;

            try
            {
                var fields = __instance.Fields;
                if (fields == null || fields.Count == 0) return true;

                ItemField first = fields[0];
                if (first == null) return true;

                var options = first.Options;
                if (options == null || options.Count == 0) return true;

                // Own copy: everything downstream sorts and filters this, never the game's list.
                var snapshot = new List<ItemDefinition>(options.Count);
                for (int i = 0; i < options.Count; i++) snapshot.Add(options[i]);

                if (!WorthTakingOver(snapshot)) return true;

                Transform canvasRoot = ResolveCanvasRoot(__instance);
                if (canvasRoot == null) return true;

                bool uniform = AreFieldsUniform(fields);
                ItemDefinition selected = uniform ? first.SelectedItem : null;
                string noneLabel = __instance.ShowNoneAsAny ? "Any" : "None";
                string title = __instance.FieldLabel != null && !string.IsNullOrWhiteSpace(__instance.FieldLabel.text)
                    ? __instance.FieldLabel.text
                    : "Select";

                View view = ViewBuilder.Build(title, snapshot, selected, first.CanSelectNone, noneLabel);

                // Keep a managed copy of the target fields: writing back has to hit every selected object, exactly
                // as vanilla's own handler does.
                var targets = new List<ItemField>(fields.Count);
                for (int i = 0; i < fields.Count; i++)
                    if (fields[i] != null) targets.Add(fields[i]);

                // The Sideload surface first when it is switched on, and the uGUI card whenever it declines - a
                // spike may never be the reason a clipboard stops working.
                if (Preferences.SurfacePicker
                    && SurfacePicker.TryOpen(canvasRoot, view, item => Apply(targets, item))) return false;

                if (!ItemPicker.TryOpen(canvasRoot, view, item => Apply(targets, item))) return true;

                return false;   // the picker is up - skip the vanilla grid
            }
            catch (Exception e)
            {
                Core.WarnThrottled("field-click", "Clipwise: could not take over this item field, using the vanilla grid: " + e.Message);
                return true;
            }
        }

        /// <summary>Writes the choice back through the field's normal setter - the same three lines vanilla's
        /// <c>ItemFieldUI.OptionSelected</c> runs, so the change replicates by item ID and the field's own label
        /// refresh fires.</summary>
        private static void Apply(List<ItemField> targets, ItemDefinition item)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                try { targets[i].SetItem(item, true); }
                catch (Exception e) { Core.Log.Warning("Clipwise: setting the field failed: " + e.Message); }
            }
        }

        /// <summary>A field earns the picker when it has enough options that scrolling and searching matter, or when
        /// a mod explicitly filed one of its items - the three additives on a pot keep the vanilla look.</summary>
        private static bool WorthTakingOver(List<ItemDefinition> options)
        {
            if (options.Count >= Preferences.MinOptions) return true;

            var resolved = Catalog.Resolved();
            if (resolved.Count == 0) return false;
            for (int i = 0; i < options.Count; i++)
            {
                string id = SafeId(options[i]);
                if (id != null && resolved.ContainsKey(id)) return true;
            }
            return false;
        }

        private static string SafeId(ItemDefinition def)
        {
            try { return def?.ID; } catch { return null; }
        }

        /// <summary>Mirrors vanilla's own uniformity check: with several objects selected at once, a differing
        /// selection means no entry is highlighted.</summary>
        private static bool AreFieldsUniform(Il2CppSystem.Collections.Generic.List<ItemField> fields)
        {
            for (int i = 0; i < fields.Count - 1; i++)
                if (fields[i].SelectedItem != fields[i + 1].SelectedItem) return false;
            return true;
        }

        /// <summary>The root canvas the clipboard renders on, so the picker sits above it and inherits its scaling.</summary>
        private static Transform ResolveCanvasRoot(ItemFieldUI ui)
        {
            var canvas = ui.GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            Canvas root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            return root.transform;
        }
    }
}

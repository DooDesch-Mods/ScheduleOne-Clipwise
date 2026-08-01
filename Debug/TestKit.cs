#if DEBUG
using System;
using System.Collections.Generic;
using System.Text;
using Clipwise.Content;
using Clipwise.Index;
using Clipwise.Model;
using Clipwise.Prefs;
using Clipwise.UI;
using HarmonyLib;

namespace Clipwise.Debugging
{
    /// <summary>
    /// Dev-console commands for Clipwise. Console only - no hotkeys, per the workspace rule in CLAUDE.md: a console
    /// command can be driven by tooling and verified, a key press cannot.
    ///
    /// Compiled only into Debug builds; Release contains none of this.
    ///
    ///   cwhelp        list the commands
    ///   cwcats        every category with its source, sort order and entry count
    ///   cwdump        every resolved item claim: item id, category, source, tags
    ///   cwconflicts   items claimed by more than one source at equal standing
    ///   cwnamecheck   items whose asset name and ID differ - breaks selection in co-op only
    ///   cwauto        what the classifier alone makes of the current seed list
    ///   cwreload      re-read the override files and the user preferences
    ///   cwopen        open the picker on the seed list without a clipboard
    ///   cwtab [key]   switch the open picker's tab (no arg = All); reports how many rows survive
    ///   cwsearch [q]  set the open picker's search text; reports how many rows survive
    ///
    /// The console needs <c>Settings.ConsoleEnabled</c> and the player being the host
    /// (ScheduleOne.UI/ConsoleUI.cs:25-33). That flag is a live toggle in the in-game settings window
    /// (ScheduleOne.UI.Settings/GameSettingsWindow.cs:35-41), so it is always reachable.
    /// </summary>
    internal static class TestKit
    {
        internal static bool TryHandle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return Dispatch(raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        internal static bool TryHandle(Il2CppSystem.Collections.Generic.List<string> args)
        {
            if (args == null || args.Count == 0) return false;
            string[] parts = new string[args.Count];
            for (int i = 0; i < args.Count; i++) parts[i] = args[i];
            return Dispatch(parts);
        }

        private static int _lastFrame = -1;
        private static string _lastSig = "";

        /// <summary>True if the command was ours and should be swallowed; false lets the game handle it.</summary>
        private static bool Dispatch(string[] parts)
        {
            if (parts.Length == 0) return false;
            string cmd = parts[0].ToLowerInvariant();
            if (cmd != "cwhelp" && cmd != "cwcats" && cmd != "cwdump" && cmd != "cwconflicts"
             && cmd != "cwnamecheck" && cmd != "cwauto" && cmd != "cwreload" && cmd != "cwopen"
             && cmd != "cwtab" && cmd != "cwsearch" && cmd != "cwvanilla")
                return false;

            // Both SubmitCommand overloads can fire for one submission (the string body calls the list body),
            // so run side effects once per identical command+frame and swallow the duplicate.
            string sig = string.Join(" ", parts);
            int frame = Time.frameCount;
            if (frame == _lastFrame && sig == _lastSig) return true;
            _lastFrame = frame; _lastSig = sig;

            try
            {
                switch (cmd)
                {
                    case "cwhelp": Help(); break;
                    case "cwcats": Cats(); break;
                    case "cwdump": Dump(); break;
                    case "cwconflicts": Conflicts(); break;
                    case "cwnamecheck": NameCheck(); break;
                    case "cwauto": Auto(); break;
                    case "cwreload": Reload(); break;
                    case "cwopen": Open(); break;
                    case "cwvanilla": Vanilla(); break;
                    case "cwtab": Tab(parts.Length > 1 ? parts[1] : ""); break;
                    case "cwsearch": Search(parts.Length > 1 ? string.Join(" ", parts, 1, parts.Length - 1) : ""); break;
                }
            }
            catch (Exception e) { Complain("[Clipwise] console command failed: " + e.Message); }
            return true;
        }

        // ---- commands -------------------------------------------------------------------------------------

        private static void Help()
        {
            Say("Clipwise commands:\n"
              + "  cwcats       categories with source, sort order and entry count\n"
              + "  cwdump       resolved item claims (item id, category, source, tags)\n"
              + "  cwconflicts  items claimed twice at equal standing\n"
              + "  cwnamecheck  items whose asset name and ID differ (co-op only breakage)\n"
              + "  cwauto       what the classifier alone makes of the current seed list\n"
              + "  cwreload     re-read override files and user preferences\n"
              + "  cwopen       open the picker on the seed list without a clipboard\n"
              + "  cwtab [key]  switch the open picker's tab (no arg = All)\n"
              + "  cwsearch [q] set the open picker's search text");
        }

        private static void Cats()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in Catalog.Resolved())
            {
                string key = kv.Value.CategoryKey ?? "(none)";
                counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            var lines = new List<string>();
            foreach (CategoryDef c in Catalog.Categories)
            {
                counts.TryGetValue(c.Key, out int n);
                lines.Add($"  [{c.SortOrder,5}] {c.Key,-40} \"{c.Label}\"  {n} entry(ies){(c.IsAuto ? "  (auto)" : "")}");
            }
            lines.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            sb.Append("Clipwise categories (").Append(lines.Count).AppendLine("):");
            foreach (string l in lines) sb.AppendLine(l);
            Say(sb.ToString());
        }

        private static void Dump()
        {
            var resolved = Catalog.Resolved();
            var sb = new StringBuilder();
            sb.Append("Clipwise claims (").Append(resolved.Count).AppendLine("):");

            var keys = new List<string>(resolved.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string k in keys)
            {
                ItemEntry e = resolved[k];
                sb.Append("  ").Append(e.ItemId.PadRight(28))
                  .Append(" -> ").Append((e.CategoryKey ?? "(none)").PadRight(38))
                  .Append(" src=").Append(e.Source);
                if (e.Tags.Count > 0) sb.Append("  tags=").Append(string.Join(",", e.Tags));
                sb.AppendLine();
            }
            Say(sb.ToString());
        }

        private static void Conflicts()
        {
            Catalog.Resolved();   // conflicts are recorded during resolution
            var conflicts = Catalog.Conflicts;
            if (conflicts.Count == 0) { Say("Clipwise: no conflicting claims."); return; }

            var sb = new StringBuilder();
            sb.Append("Clipwise conflicts (").Append(conflicts.Count).AppendLine("):");
            for (int i = 0; i < conflicts.Count; i++) sb.Append("  ").AppendLine(conflicts[i]);
            Say(sb.ToString());
        }

        /// <summary>
        /// Finds the trap that only shows up in co-op: the pot's seed choice is replicated as
        /// <c>SelectedItem.name</c> (ScheduleOne.Management/ConfigurationReplicator.cs:48) but resolved through the
        /// registry, which hashes <c>ID.ToLower()</c> (ScheduleOne/Registry.cs:112-115). If the two differ, the host
        /// logs "Item not found in registry!" and every client silently ends up with no seed - while singleplayer and
        /// the save file work perfectly.
        /// </summary>
        private static void NameCheck()
        {
            var breaking = new List<string>();
            int mismatched = 0;
            int checked_ = 0;
            try
            {
                var registry = GameRegistry.Instance;
                var all = registry?.ItemRegistry;
                if (all == null) { Complain("Clipwise: registry not available."); return; }

                for (int i = 0; i < all.Count; i++)
                {
                    ItemDefinition def = all[i]?.Definition;
                    if (def == null) continue;
                    checked_++;
                    string id = def.ID ?? "";
                    if (string.Equals(id, def.name ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                    mismatched++;

                    // A mismatch only breaks anything for an item a clipboard field can actually offer, because
                    // only that path replicates the asset name (ConfigurationReplicator.cs:48). A product or a
                    // piece of artwork never travels through an ItemField, so reporting it as broken co-op would
                    // be a false alarm - and 70 of those bury the one that matters.
                    if (Selectable(def)) breaking.Add($"  {id.PadRight(28)} name=\"{def.name}\"");
                }
            }
            catch (Exception e) { Complain("Clipwise: name check failed: " + e.Message); return; }

            var sb = new StringBuilder();
            if (breaking.Count == 0)
            {
                sb.Append("Clipwise: checked ").Append(checked_)
                  .AppendLine(" item(s) - every clipboard-selectable item's asset name matches its ID.");
            }
            else
            {
                breaking.Sort(StringComparer.OrdinalIgnoreCase);
                sb.Append("Clipwise: ").Append(breaking.Count)
                  .AppendLine(" clipboard-selectable item(s) have an asset name that differs from their ID.");
                sb.AppendLine("Selecting these on a clipboard field fails in co-op only - the host cannot resolve the");
                sb.AppendLine("replicated name, so every client ends up with nothing selected:");
                foreach (string l in breaking) sb.AppendLine(l);
            }

            int harmless = mismatched - breaking.Count;
            if (harmless > 0)
                sb.Append("(").Append(harmless)
                  .AppendLine(" other item(s) also differ - products, artwork and the like. They never travel through a clipboard field, so they are fine.)");

            Say(sb.ToString());
        }

        /// <summary>Whether a clipboard item field can offer this definition at all. Those are the only three
        /// types the option lists hold: seeds (pot), shroom spawns (mushroom bed) and additives (both).</summary>
        private static bool Selectable(ItemDefinition def)
        {
            return def.TryCast<SeedDefinition>() != null
                || def.TryCast<ShroomSpawnDefinition>() != null
                || def.TryCast<AdditiveDefinition>() != null;
        }

        private static void Auto()
        {
            var seeds = Seeds();
            if (seeds == null) { Complain("Clipwise: no ManagementUtilities in this scene - load a save first."); return; }

            var resolved = Catalog.Resolved();
            var sb = new StringBuilder();
            sb.Append("Clipwise classification of ").Append(seeds.Count).AppendLine(" seed(s):");

            for (int i = 0; i < seeds.Count; i++)
            {
                ItemDefinition def = seeds[i];
                ItemFacts f = ItemFacts.For(def);
                if (f == null) continue;
                bool claimed = resolved.TryGetValue(f.ItemId, out ItemEntry e);
                sb.Append("  ").Append(f.ItemId.PadRight(28))
                  .Append(f.IsModded ? " modded" : " vanilla")
                  .Append(claimed ? "  claim=" + e.CategoryKey : "  claim=(none, classified)")
                  .Append(f.HasPlant ? $"  yield={f.BaseYield} growth={f.GrowthTimeHours}h" : "")
                  .Append(f.Effects.Count > 0 ? "  fx=" + string.Join("/", f.Effects) : "")
                  .AppendLine();
            }
            Say(sb.ToString());
        }

        private static void Reload()
        {
            OverrideLoader.LoadAll();
            UserPrefs.Load();
            ItemFacts.Invalidate();
            Say($"Clipwise: reloaded. {Catalog.ClaimCount} claim(s) from {OverrideLoader.LoadedSources.Count} override file(s).");
        }

        private static void Open()
        {
            var seeds = Seeds();
            if (seeds == null) { Complain("Clipwise: no ManagementUtilities in this scene - load a save first."); return; }

            Transform canvasRoot = TopCanvas();
            if (canvasRoot == null) { Complain("Clipwise: no overlay canvas found to draw on."); return; }

            View view = ViewBuilder.Build("Seeds (cwopen)", seeds, null, false, "None");
            bool ok = ItemPicker.TryOpen(canvasRoot, view, item =>
                Say("Clipwise: cwopen picked " + (item != null ? item.ID : "(none)")));
            if (!ok) Complain("Clipwise: the picker refused to open - see the log.");
        }

        /// <summary>
        /// Open the UNTOUCHED vanilla item selector on the seed list, so its look can be compared side by side
        /// with what Clipwise makes of it.
        ///
        /// This is the reference the redesign is measured against: the goal is a screen that reads as part of
        /// the game, and the only way to judge that is to put the two next to each other.
        /// </summary>
        private static void Vanilla()
        {
            var seeds = Seeds();
            if (seeds == null) { Complain("Clipwise: no ManagementUtilities in this scene - load a save first."); return; }

            // The clipboard interface is inactive until the clipboard is equipped, and FindObjectOfType skips
            // inactive objects - so this has to go through FindObjectsOfTypeAll, filtered to real scene objects
            // so a prefab asset never gets activated.
            ItemSelector screen = null;
            var all = Resources.FindObjectsOfTypeAll<Il2CppScheduleOne.Management.ManagementInterface>();
            for (int i = 0; all != null && i < all.Length; i++)
            {
                var mi = all[i];
                if (mi == null || !mi.gameObject.scene.IsValid()) continue;
                if (mi.ItemSelectorScreen != null) { screen = mi.ItemSelectorScreen; break; }
            }
            if (screen == null) { Complain("Clipwise: no ItemSelector in this scene."); return; }

            // The selector lives inside the clipboard interface, which is inactive until the clipboard is
            // equipped. Switch the container on so the screen is actually visible on its own.
            try
            {
                Transform t = screen.transform;
                while (t != null) { if (!t.gameObject.activeSelf) t.gameObject.SetActive(true); t = t.parent; }
            }
            catch (Exception e) { Core.LogDebug("Clipwise: could not activate the selector's parents: " + e.Message); }

            var options = new Il2CppSystem.Collections.Generic.List<ItemSelector.Option>();
            for (int i = 0; i < seeds.Count; i++)
                options.Add(new ItemSelector.Option(seeds[i].Name, seeds[i]));

            screen.Initialize("Seed (vanilla reference)", options, null, null);
            screen.Open();
            Say($"Clipwise: opened the vanilla selector with {options.Count} option(s).");
        }

        /// <summary>Switch the open picker to a tab. Accepts a full category key, a substring of one, or nothing
        /// for the All tab; with no match it lists what is on offer instead of failing silently.</summary>
        private static void Tab(string wanted)
        {
            if (!ItemPicker.IsOpen) { Complain("Clipwise: no picker open - run cwopen first."); return; }

            List<string> keys = ItemPicker.TabKeys();
            string target = null;
            if (string.IsNullOrEmpty(wanted)) target = "";
            else
                foreach (string k in keys)
                    if (k.Length > 0 && k.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0) { target = k; break; }

            if (target == null)
            {
                var sb = new StringBuilder();
                sb.Append("Clipwise: no tab matches '").Append(wanted).AppendLine("'. Available:");
                foreach (string k in keys) sb.Append("  ").AppendLine(k.Length == 0 ? "(all)" : k);
                Complain(sb.ToString());
                return;
            }

            ItemPicker.SetFilter(target, null);
            Say($"Clipwise: tab '{(target.Length == 0 ? "(all)" : target)}' -> {ItemPicker.VisibleCount} row(s) visible.");
        }

        private static void Search(string query)
        {
            if (!ItemPicker.IsOpen) { Complain("Clipwise: no picker open - run cwopen first."); return; }
            ItemPicker.SetFilter(null, query);
            Say($"Clipwise: search '{query}' -> {ItemPicker.VisibleCount} row(s) visible.");
        }

        // ---- helpers --------------------------------------------------------------------------------------

        /// <summary>The seed list the pot configuration reads. Copied out - the original is never reordered.</summary>
        private static List<ItemDefinition> Seeds()
        {
            var mu = UnityEngine.Object.FindObjectOfType<ManagementUtilities>();
            var list = mu?.Seeds;
            if (list == null) return null;

            var copy = new List<ItemDefinition>(list.Count);
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) copy.Add(list[i]);
            return copy;
        }

        private static Transform TopCanvas()
        {
            Canvas best = null;
            var all = UnityEngine.Object.FindObjectsOfType<Canvas>();
            if (all == null) return null;
            for (int i = 0; i < all.Length; i++)
            {
                Canvas c = all[i];
                if (c == null || !c.isActiveAndEnabled) continue;
                if (c.rootCanvas != null && c.rootCanvas != c) continue;
                if (best == null || c.sortingOrder > best.sortingOrder) best = c;
            }
            return best?.transform;
        }

        private static void Say(string message)
        {
            Core.Log.Msg(message);
            _pending.Add(message);
        }

        private static void Complain(string message)
        {
            Core.Log.Warning(message);
            _pending.Add(message);
        }

        private static readonly List<string> _pending = new();

        /// <summary>
        /// Write the queued lines into the console window. Called once per frame from the mod's own update.
        ///
        /// Queued rather than written directly, because everything above runs inside a Harmony prefix on
        /// <c>Console.SubmitCommand</c> - the console is mid-submit at that moment, and writing back into it from
        /// there re-enters and hangs the main thread. A frame later it is idle and the write is ordinary.
        /// </summary>
        internal static void FlushToConsole()
        {
            if (_pending.Count == 0) return;

            // Copied out and cleared FIRST: if a write throws, the queue must not be replayed forever.
            var lines = _pending.ToArray();
            _pending.Clear();

            for (int i = 0; i < lines.Length; i++)
            {
                // The console lives in the ScheduleOne ROOT namespace, which is deliberately not imported globally -
                // it also holds a Console type that would collide with System.Console.
                try { Il2CppScheduleOne.Console.Log(lines[i]); } catch { }
            }
        }
    }

    // Both overloads are patched because either one can be the path a given submitter takes - the console UI
    // and scripted submitters do not agree on which.
    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), nameof(Il2CppScheduleOne.Console.SubmitCommand), new Type[] { typeof(string) })]
    internal static class Cw_Console_SubmitCommand_String_Patch
    {
        private static bool Prefix(string args)
        {
            try { return !TestKit.TryHandle(args); } catch { return true; }
        }
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), nameof(Il2CppScheduleOne.Console.SubmitCommand), new Type[] { typeof(Il2CppSystem.Collections.Generic.List<string>) })]
    internal static class Cw_Console_SubmitCommand_List_Patch
    {
        private static bool Prefix(Il2CppSystem.Collections.Generic.List<string> args)
        {
            try { return !TestKit.TryHandle(args); } catch { return true; }
        }
    }
}
#endif

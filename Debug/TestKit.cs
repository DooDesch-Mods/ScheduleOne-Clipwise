#if DEBUG
using System;
using System.Collections.Generic;
using System.Text;
using Clipwise.Content;
using Il2CppScheduleOne.ObjectScripts;   // Pot - only this file needs it, so it is not a global using
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
    ///   cwboard       open the real clipboard on the nearest pot, and close it again
    ///   cwpng         write the first few converted item icons to disk as PNGs
    ///   cwrect        the clipboard's own rects, so the picker is sized from numbers
    ///   cwtab, cwsearch   both print how to filter the page instead - see Filtering()
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
             && cmd != "cwtab" && cmd != "cwsearch" && cmd != "cwvanilla" && cmd != "cwrect"
             && cmd != "cwboard" && cmd != "cwpng" && cmd != "cwfield" && cmd != "cwphone")
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
                    case "cwfield": Field(); break;
                    case "cwvanilla": Vanilla(); break;
                    case "cwrect": Rects(); break;
                    case "cwboard": Board(); break;
                    case "cwphone": Phone(); break;
                    case "cwpng": DumpIcons(); break;
                    case "cwtab":
                    case "cwsearch": Filtering(); break;
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
              + "  cwboard      open the real clipboard on the nearest pot, and close it again\n"
              + "  cwfield      report what the pot's seed field holds, and press it for real\n"
              + "  cwvanilla    the game's own option grid, measured\n"
              + "  cwrect       the rects the picker is placed against\n"
              + "  cwpng        write every picker icon out as a PNG\n"
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

        /// <summary>
        /// Open the picker the way a PLAYER does - through the clipboard's own field - so the write-back runs.
        ///
        /// WHY <c>cwopen</c> IS NOT ENOUGH, and this cost a round trip to the tester to find out: it builds a
        /// view by hand and hands the picker a callback that <b>logs</b>. So it proves the page draws and that a
        /// click reaches a tile, and it proves nothing at all about the choice reaching a pot - which is the half
        /// that was reported broken. A harness that exercises a stand-in instead of the real path answers the
        /// wrong question confidently.
        ///
        /// This invokes the field's own button, so <see cref="Patches.ItemFieldUIPatch"/> runs exactly as it does
        /// for a mouse, and the picker comes up with the real <c>Apply</c> behind it. Then
        /// <c>sideload_click .shot</c> picks a tile through the real raycast, and <c>cwfield</c> again reports
        /// what the field holds now.
        /// </summary>
        private static void Field()
        {
            var fields = UnityEngine.Object.FindObjectsOfType<ItemFieldUI>(true);
            if (fields == null || fields.Length == 0)
            {
                Complain("Clipwise: no ItemFieldUI in the scene - open the clipboard on a pot first (cwboard).");
                return;
            }

            ItemFieldUI seed = null;
            for (int i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                if (f == null || !f.gameObject.activeInHierarchy) continue;

                string label = f.FieldLabel != null ? f.FieldLabel.text : null;
                if (label != null && label.IndexOf("seed", StringComparison.OrdinalIgnoreCase) >= 0) { seed = f; break; }
                if (seed == null) seed = f;
            }

            if (seed == null) { Complain("Clipwise: no active item field on screen."); return; }

            string held = "(none)";
            try
            {
                var one = seed.Fields != null && seed.Fields.Count > 0 ? seed.Fields[0] : null;
                if (one != null && one.SelectedItem != null) held = one.SelectedItem.ID;
            }
            catch (Exception e) { held = "unreadable: " + e.Message; }

            Say("Clipwise: field '" + (seed.FieldLabel != null ? seed.FieldLabel.text : "?") + "' holds " + held);

            // `Clicked` IS the patched method, so this enters the mod exactly where a mouse does. Pressing the
            // button instead would work too and would depend on the field's own wiring; this depends on the one
            // thing the patch is pinned to.
            seed.Clicked();
            Say("Clipwise: pressed the field - the picker should be up on the REAL path now. "
              + "sideload_click .shot to choose, then cwfield again to see what it holds.");
        }

        private static void Open()
        {
            var seeds = Seeds();
            if (seeds == null) { Complain("Clipwise: no ManagementUtilities in this scene - load a save first."); return; }

            Transform canvasRoot = TopCanvas();
            if (canvasRoot == null) { Complain("Clipwise: no overlay canvas found to draw on."); return; }

            View view = ViewBuilder.Build("Seeds (cwopen)", seeds, null, false, "None");
            bool ok = SurfacePicker.TryOpen(canvasRoot, view, item =>
                Say("Clipwise: cwopen picked " + (item != null ? item.ID : "(none)")));
            if (!ok) Complain("Clipwise: the picker refused to open - is Sideload installed? See the log.");
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

        /// <summary>
        /// Open the clipboard the way the player does, on the nearest pot - and close it on the second call.
        ///
        /// WHY. Everything this mod draws is drawn ON the clipboard, and the clipboard is reached by equipping an
        /// item and clicking a pot. Neither is something the automation can do, so every judgement about how the
        /// picker LOOKS has come from a tester's screenshot, and the seed card has now been sized three different
        /// ways off three of them. This is the missing half of the loop: a screen that can be opened and
        /// photographed here.
        ///
        /// The equippable is passed as null, and that is checked rather than hoped: vanilla only dereferences it
        /// on the rename path and in the object and transit selectors (ManagementInterface.cs:195,
        /// ObjectSelector.cs:90). The seed field touches none of them.
        ///
        /// A toggle, because there is no way to send Escape from here - the command that opens it has to be the
        /// command that closes it.
        /// </summary>
        private static void Board()
        {
            var clipboard = Singleton<ManagementClipboard>.Instance;
            if (clipboard == null) { Complain("Clipwise: no ManagementClipboard in this scene - load a save first."); return; }

            if (clipboard.IsOpen)
            {
                clipboard.Close();
                Say("Clipwise: closed the clipboard.");
                return;
            }

            Pot pot = NearestPot();
            if (pot == null) { Complain("Clipwise: no pot in this scene to open the clipboard on."); return; }

            var selection = new Il2CppSystem.Collections.Generic.List<IConfigurable>();
            var one = pot.TryCast<IConfigurable>();
            if (one == null) { Complain("Clipwise: that pot does not present as IConfigurable."); return; }
            selection.Add(one);

            clipboard.Open(selection, null);
            Say("Clipwise: clipboard open on '" + pot.gameObject.name + "'. Run cwboard again to close it.");
        }

        /// <summary>
        /// Put the phone away.
        ///
        /// Not a convenience. The picker hangs in the WORLD in front of the clipboard, and an open phone is a
        /// tilted object nearer the camera - so wherever the two overlap the phone wins the depth test and the
        /// right-hand page is a black rectangle in every screenshot. A player never meets it, because the phone
        /// blocks the interaction that opens a clipboard in the first place; only `cwboard` can put the two on
        /// screen at once, and only a screenshot has to care.
        ///
        /// A command and not a key, for the reason every dev entry point here is: the harness can run a command
        /// and cannot press a key.
        /// </summary>
        private static void Phone()
        {
            try
            {
                var phone = PlayerSingleton<Il2CppScheduleOne.UI.Phone.Phone>.Instance;
                if (phone == null) { Complain("Clipwise: no Phone in this scene."); return; }

                phone.SetIsOpen(false);
                Say("Clipwise: phone stowed.");
            }
            catch (Exception e) { Complain("Clipwise: could not stow the phone: " + e.Message); }
        }

        /// <summary>
        /// Write the first few converted icons to disk, exactly as the page receives them.
        ///
        /// The tiles draw their vials almost black (#122) and there are two candidates that look identical on
        /// screen: the conversion produces wrong bytes, or the bytes are right and the page draws them badly.
        /// A file on disk tells them apart in one look, and nothing else does - a screenshot of a tile is a
        /// picture of the second stage only.
        /// </summary>
        private static void DumpIcons()
        {
            var seeds = Seeds();
            if (seeds == null) { Complain("Clipwise: no ManagementUtilities in this scene - load a save first."); return; }

            string dir = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "Clipwise");
            System.IO.Directory.CreateDirectory(dir);

            int written = 0;
            for (int i = 0; i < seeds.Count && written < 6; i++)
            {
                ItemDefinition def = seeds[i];
                if (def == null) continue;

                Sprite icon = def.Icon;
                if (icon == null) { Say("  " + def.ID + ": no icon on the definition"); continue; }

                byte[] png = SurfaceIcons.Encode(icon);
                if (png == null || png.Length == 0) { Say("  " + def.ID + ": conversion returned nothing"); continue; }

                string file = System.IO.Path.Combine(dir, "icon-" + def.ID + ".png");
                System.IO.File.WriteAllBytes(file, png);
                Say("  " + def.ID + ": " + png.Length + " bytes -> " + file);
                written++;
            }

            if (written == 0) Complain("Clipwise: nothing was written.");
        }

        /// <summary>The pot closest to the player, or the first one found when there is no player.</summary>
        private static Pot NearestPot()
        {
            Pot best = null;
            float bestDistance = float.MaxValue;

            Vector3 where = Vector3.zero;
            try
            {
                var player = Il2CppScheduleOne.PlayerScripts.Player.Local;
                if (player != null) where = player.transform.position;
            }
            catch { }

            var pots = UnityEngine.Object.FindObjectsOfType<Pot>();
            for (int i = 0; pots != null && i < pots.Length; i++)
            {
                var pot = pots[i];
                if (pot == null) continue;

                float d = Vector3.Distance(pot.transform.position, where);
                if (d >= bestDistance) continue;

                bestDistance = d;
                best = pot;
            }

            return best;
        }

        /// <summary>
        /// Measure the clipboard, instead of sizing the picker from a screenshot.
        ///
        /// The surface has now been sized three different ways off pictures a tester sent, and every one of them
        /// was a guess about WHICH object in the vanilla card is "the card". This prints the chain from the
        /// option grid up to the canvas with each step's rect, which answers that question with numbers: the
        /// clipboard's own screen, the paper inside its frame, and the grid inside the paper are three different
        /// sizes, and the whole complaint is that we picked the wrong one of the three.
        ///
        /// It reads RectTransforms, so it needs no clipboard in the player's hands and no mouse - the sizes come
        /// from anchors and offsets, which are set whether or not the object is on screen.
        /// </summary>
        private static void Rects()
        {
            ItemSelector screen = null;
            var all = Resources.FindObjectsOfTypeAll<Il2CppScheduleOne.Management.ManagementInterface>();
            for (int i = 0; all != null && i < all.Length; i++)
            {
                var mi = all[i];
                if (mi == null || !mi.gameObject.scene.IsValid()) continue;
                if (mi.ItemSelectorScreen != null) { screen = mi.ItemSelectorScreen; break; }
            }
            if (screen == null) { Complain("Clipwise: no ItemSelector in this scene."); return; }

            RectTransform grid = null;
            try { grid = screen.OptionContainer; } catch { }

            var sb = new StringBuilder();
            sb.AppendLine("Clipwise: the clipboard, from the option grid outward -");

            Transform t = grid != null ? grid.transform : screen.transform;
            int depth = 0;
            while (t != null && depth < 12)
            {
                // GetComponent, not `as`: under IL2CPP a cast on an interop object handed back by .parent
                // returns null even when the object really is a RectTransform, and the first version of this
                // command printed "(no RectTransform)" for the entire clipboard because of it.
                var rt = t.GetComponent<RectTransform>();
                if (rt != null)
                {
                    Rect r = rt.rect;
                    sb.Append("  ").Append(new string(' ', depth * 2))
                      .Append(t.gameObject.name.PadRight(30 - System.Math.Min(28, depth * 2)))
                      .Append(r.width.ToString("0")).Append(" x ").Append(r.height.ToString("0"))
                      .Append("   scale ").Append(rt.localScale.x.ToString("0.###"))
                      .Append(t.gameObject.activeSelf ? "" : "   (off)")
                      .AppendLine();
                }
                else
                {
                    sb.Append("  ").Append(new string(' ', depth * 2)).Append(t.gameObject.name)
                      .AppendLine("   (no RectTransform)");
                }

                t = t.parent;
                depth++;
            }

            sb.AppendLine("  the picker is sized to the grid's PARENT today - the second line.");

            // And what the card is made OF. The chain above says the screen, the mask and the canvas are all
            // one size, which cannot be the whole story - the tester sees a wooden frame around a smaller sheet,
            // so the sheet is a child with a margin, and that child is what a page should be cut to.
            sb.AppendLine("  inside the ItemSelector -");
            // Indexed, not foreach: iterating a Transform under IL2CPP yields Il2CppSystem.Object and the cast
            // to Transform throws - the first version of this block died on that line.
            for (int c = 0; c < screen.transform.childCount; c++)
            {
                Transform child = screen.transform.GetChild(c);
                var crt = child.GetComponent<RectTransform>();
                sb.Append("    ").Append(child.gameObject.name.PadRight(24));
                if (crt != null)
                    sb.Append(crt.rect.width.ToString("0")).Append(" x ").Append(crt.rect.height.ToString("0"))
                      .Append("  at ").Append(crt.anchoredPosition.x.ToString("0")).Append(",")
                      .Append(crt.anchoredPosition.y.ToString("0"));
                else sb.Append("(no RectTransform)");

                if (child.GetComponent<UnityEngine.UI.Image>() != null) sb.Append("   [Image]");
                if (child.GetComponent<UnityEngine.UI.Mask>() != null) sb.Append("   [Mask]");
                if (!child.gameObject.activeSelf) sb.Append("   (off)");
                sb.AppendLine();
            }

            Say(sb.ToString());
        }

        /// <summary>
        /// Where the filtering went.
        ///
        /// `cwtab` and `cwsearch` used to reach into the uGUI card and set its fields. That card is gone and the
        /// filtering is in the page, which is a script engine - so the way to drive it is to run script in it,
        /// and the MCP already has a tool for exactly that. Printing the replacement beats leaving two commands
        /// that report on a screen nobody is looking at.
        /// </summary>
        private static void Filtering()
        {
            Say("Clipwise: the picker filters in its page now, so drive it there:");
            Say("  sideload_eval clipwise \"query = 'og'; render()\"");
            Say("  sideload_eval clipwise \"f.fav = true; render()\"");
            Say("  sideload_eval clipwise \"f.fx.push('Calming'); render()\"    // an effect tick on the right card");
            Say("  sideload_eval clipwise \"showPreview(view.rows[3])\"         // hover, without a mouse");
            Say("  sideload_eval clipwise \"console.log(visible().length + ' row(s) visible')\"");
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

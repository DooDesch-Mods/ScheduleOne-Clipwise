using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Clipwise.Prefs
{
    /// <summary>The on-disk shape. Field names are lowercase to match the JSON a user reads and edits.</summary>
    internal sealed class PrefsFile
    {
        public int schemaVersion = UserPrefs.SchemaVersion;

        /// <summary>Item IDs pinned to the top of every tab.</summary>
        public List<string> favourites = new();

        /// <summary>Item IDs the player never wants to see. The currently selected item is shown regardless, so
        /// hiding one can never make the selection unreachable.</summary>
        public List<string> hidden = new();

        /// <summary>Canonical category key of the tab to reopen, or "" for the All tab.</summary>
        public string lastTab = "";

        public bool onlyDiscovered;
        public bool onlyFavourites;

        /// <summary>Index into the picker's sort modes. Out-of-range values fall back to the default order.</summary>
        public int sortMode;

        /// <summary>Split a mod's section into one group per tier. On by default - a strain catalogue is the
        /// list this picker was rebuilt for, and tiers are how its owner thinks about it.</summary>
        public bool tierGroups = true;

        /// <summary>Highest tier first. Independent of <see cref="sortMode"/>, which orders the tiles INSIDE
        /// each group, so both can be set at once.</summary>
        public bool tierDescending;
    }

    /// <summary>
    /// Per-installation view state: favourites, hidden entries, the collapsed tabs and the last one used. All of it
    /// is cosmetic and local, so it stays out of the game save (a save-format change is a real risk for no gain)
    /// and out of co-op sync (nobody else's picker is affected by which entries this player pinned).
    ///
    /// Stored by canonical item ID, never by display name, so renaming a strain does not silently unpin it.
    /// </summary>
    internal static class UserPrefs
    {
        public const int SchemaVersion = 1;

        private static PrefsFile _data = new();
        private static string _path;
        private static bool _dirty;

        private static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "Clipwise");
        private static string PathOnDisk => _path ??= Path.Combine(Dir, "preferences.json");

        public static int SortMode
        {
            get => _data.sortMode;
            set { if (_data.sortMode != value) { _data.sortMode = value; _dirty = true; Flush(); } }
        }

        public static bool TierGroups
        {
            get => _data.tierGroups;
            set { if (_data.tierGroups != value) { _data.tierGroups = value; _dirty = true; } }
        }

        public static bool TierDescending
        {
            get => _data.tierDescending;
            set { if (_data.tierDescending != value) { _data.tierDescending = value; _dirty = true; } }
        }

        public static bool OnlyDiscovered
        {
            get => _data.onlyDiscovered;
            set { if (_data.onlyDiscovered != value) { _data.onlyDiscovered = value; _dirty = true; } }
        }

        public static bool OnlyFavourites
        {
            get => _data.onlyFavourites;
            set { if (_data.onlyFavourites != value) { _data.onlyFavourites = value; _dirty = true; } }
        }

        public static string LastTab
        {
            get => _data.lastTab ?? "";
            set { string v = value ?? ""; if (_data.lastTab != v) { _data.lastTab = v; _dirty = true; } }
        }

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (!File.Exists(PathOnDisk))
                {
                    _data = new PrefsFile { onlyDiscovered = Config.Preferences.OnlyDiscoveredDefault };
                    return;
                }

                string json = File.ReadAllText(PathOnDisk);
                var parsed = JsonConvert.DeserializeObject<PrefsFile>(json);
                if (parsed == null) { _data = new PrefsFile(); return; }

                if (parsed.schemaVersion > SchemaVersion)
                    Core.Log.Warning($"Clipwise: preferences.json was written by a newer Clipwise (schema {parsed.schemaVersion} > {SchemaVersion}). Reading what is understood and ignoring the rest.");

                parsed.favourites ??= new List<string>();
                parsed.hidden ??= new List<string>();
                _data = parsed;
            }
            catch (Exception e)
            {
                Core.Log.Warning("Clipwise: could not read preferences.json, starting from defaults: " + e.Message);
                _data = new PrefsFile();
            }
            _dirty = false;
        }

        /// <summary>Writes only when something changed. Goes through a temp file so an interrupted write cannot
        /// leave a half-written config behind.</summary>
        public static void Flush()
        {
            if (!_dirty) return;
            try
            {
                Directory.CreateDirectory(Dir);
                _data.schemaVersion = SchemaVersion;
                string json = JsonConvert.SerializeObject(_data, Formatting.Indented);
                string tmp = PathOnDisk + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(PathOnDisk)) File.Replace(tmp, PathOnDisk, null);
                else File.Move(tmp, PathOnDisk);
                _dirty = false;
            }
            catch (Exception e)
            {
                Core.Log.Warning("Clipwise: could not write preferences.json: " + e.Message);
            }
        }

        public static bool IsFavourite(string itemId) => Contains(_data.favourites, itemId);
        public static bool IsHidden(string itemId) => Contains(_data.hidden, itemId);
        public static int HiddenCount => _data.hidden?.Count ?? 0;

        public static void ToggleFavourite(string itemId) => Toggle(_data.favourites, itemId);
        public static void ToggleHidden(string itemId) => Toggle(_data.hidden, itemId);

        private static bool Contains(List<string> list, string value)
        {
            if (list == null || string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void Toggle(List<string> list, string value)
        {
            if (list == null || string.IsNullOrEmpty(value)) return;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    list.RemoveAt(i);
                    _dirty = true;
                    Flush();
                    return;
                }
            list.Add(value);
            _dirty = true;
            Flush();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Clipwise.Index;
using Clipwise.Model;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace Clipwise.Content
{
    // The types below are deserialization targets: their fields are written by Newtonsoft, never in this source.
#pragma warning disable CS0649

    /// <summary>One category declared in an override file.</summary>
    internal sealed class OverrideCategory
    {
        public string id;
        public string label;
        public int sortOrder;
        public string iconItemId;
    }

    /// <summary>One item placement declared in an override file.</summary>
    internal sealed class OverrideItem
    {
        public string id;
        public string itemId;
        public string category;
        public string[] tags;
        public int sortOrder;
        public string sortKey;
        public string description;
    }

    /// <summary>The on-disk shape. Field names are lowercase to match the JSON people write.</summary>
    internal sealed class OverrideFile
    {
        public int schemaVersion = 1;
        public string source;
        public List<OverrideCategory> categories = new();
        public List<OverrideItem> items = new();
        public Dictionary<string, string> tagLabels = new();
    }

#pragma warning restore CS0649

    /// <summary>
    /// Loads user-authored categorization from <c>UserData/Clipwise/Overrides/*.json</c>. This is the route for
    /// items whose own mod will never integrate: a player (or a compatibility-patch author) can file someone
    /// else's seeds into a proper tab without touching that mod.
    ///
    /// Overrides outrank a mod's own API registration, so a player always has the last word on where an item goes.
    /// Only exact item IDs are matched - prefix and glob rules are deliberately absent, so a broad pattern cannot
    /// quietly relocate half of the vanilla catalogue.
    /// </summary>
    internal static class OverrideLoader
    {
        public const int SchemaVersion = 1;

        public static string Root => Path.Combine(MelonEnvironment.UserDataDirectory, "Clipwise", "Overrides");

        private static readonly List<string> _loadedSources = new();

        public static IReadOnlyList<string> LoadedSources => _loadedSources;

        public static void LoadAll()
        {
            // Drop what a previous pass registered so a reload cannot leave orphans behind.
            foreach (string s in _loadedSources) Catalog.ForgetSource(s);
            _loadedSources.Clear();

            try
            {
                Directory.CreateDirectory(Root);
                WriteReadme();

                string[] files = Directory.GetFiles(Root, "*.json", SearchOption.TopDirectoryOnly);
                // Filesystem enumeration is unsorted on some platforms; sort so the resolution order is identical
                // on every machine and every launch.
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                foreach (string file in files) LoadOne(file);
            }
            catch (Exception e)
            {
                Core.Log.Warning("Clipwise: could not scan the overrides folder: " + e.Message);
            }
        }

        private static void LoadOne(string file)
        {
            string name = Path.GetFileName(file);
            try
            {
                var parsed = JsonConvert.DeserializeObject<OverrideFile>(File.ReadAllText(file));
                if (parsed == null) { Core.Log.Warning($"Clipwise: '{name}' is empty or not an object, skipped."); return; }

                if (parsed.schemaVersion > SchemaVersion)
                    Core.Log.Warning($"Clipwise: '{name}' declares schemaVersion {parsed.schemaVersion}, this build understands {SchemaVersion}. Unknown fields are ignored - update Clipwise if entries are missing.");

                if (string.IsNullOrWhiteSpace(parsed.source))
                {
                    Core.Log.Warning($"Clipwise: '{name}' has no \"source\", skipped. Pick a stable id like \"local.my-overrides\".");
                    return;
                }
                string source = parsed.source.Trim();

                if (parsed.categories != null)
                    foreach (var c in parsed.categories)
                    {
                        if (c == null || string.IsNullOrWhiteSpace(c.id)) continue;
                        Catalog.RegisterCategory(source, c.id.Trim(), c.label, c.sortOrder, c.iconItemId);
                    }

                int items = 0;
                if (parsed.items != null)
                    foreach (var it in parsed.items)
                    {
                        if (it == null || string.IsNullOrWhiteSpace(it.itemId)) continue;
                        string id = string.IsNullOrWhiteSpace(it.id) ? it.itemId.Trim() : it.id.Trim();
                        Catalog.RegisterItem(source, id, it.itemId.Trim(), Qualify(source, it.category), it.tags,
                                             it.sortOrder, it.sortKey, it.description, 0, ClaimOrigin.Override);
                        items++;
                    }

                if (parsed.tagLabels != null)
                    foreach (var kv in parsed.tagLabels) Catalog.RegisterTagLabel(kv.Key, kv.Value);

                _loadedSources.Add(source);
                Core.Log.Msg($"Clipwise: loaded '{name}' ({source}) - {items} item(s).");
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Clipwise: could not read '{name}': {e.Message}");
            }
        }

        /// <summary>Lets a file write <c>"category": "exotics"</c> instead of the full
        /// <c>"local.my-overrides:exotics"</c> - the source is already known from the file.</summary>
        private static string Qualify(string source, string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return null;
            string c = category.Trim();
            return c.IndexOf(':') >= 0 ? c : CategoryDef.MakeKey(source, c);
        }

        private static void WriteReadme()
        {
            string path = Path.Combine(Root, "README.txt");
            if (File.Exists(path)) return;
            try
            {
                File.WriteAllText(path,
@"Clipwise overrides
==================

Drop a .json file in this folder to file items into your own categories - including items from mods that
do not support Clipwise themselves. Files load in alphabetical order, and an override always beats the
registration a mod made in code.

Match items by their exact registry ID. There are no prefix or wildcard rules on purpose, so a pattern
cannot quietly move items you did not mean to touch.

Example (save as my-overrides.json):

{
  ""schemaVersion"": 1,
  ""source"": ""local.my-overrides"",
  ""categories"": [
    { ""id"": ""exotics"", ""label"": ""Exotics"", ""sortOrder"": 500 }
  ],
  ""items"": [
    {
      ""itemId"": ""expanded_purple_haze_seed"",
      ""category"": ""exotics"",
      ""sortKey"": ""Purple Haze"",
      ""tags"": [ ""local.my-overrides:outdoor"" ],
      ""description"": ""Slow, but worth it.""
    }
  ],
  ""tagLabels"": {
    ""local.my-overrides:outdoor"": ""Outdoor""
  }
}

Notes
- ""source"" is yours; keep it stable, it identifies your file's claims.
- ""category"" may be a bare id (resolved against your source) or a full ""source:id"" key.
- Tags must be namespaced as ""source:path"". The ""clipwise"" namespace is reserved.
- ""sortOrder"" orders the tabs; vanilla sits at 0 and unregistered mod items land above 1000.
- Reload without restarting the game: run cwreload in the developer console (Debug builds).
");
            }
            catch { /* a missing readme is cosmetic */ }
        }
    }
}

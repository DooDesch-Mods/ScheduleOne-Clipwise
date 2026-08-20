using System;
using System.Collections.Generic;
using System.Globalization;
using Clipwise.Model;

namespace Clipwise.Index
{
    /// <summary>
    /// Everything mods and users have declared: categories, and per-item claims. Holds no Unity objects and no
    /// <c>ItemDefinition</c> references - only ID strings - so nothing here goes stale when the registry drops its
    /// runtime items on a scene change.
    ///
    /// Registration is an idempotent upsert keyed by (source, id), so a mod may call it twice, and a reload of the
    /// JSON overrides replaces exactly its own claims.
    /// </summary>
    internal static class Catalog
    {
        /// <summary>Tag and category namespace the host owns. A mod's claim on it is rejected.</summary>
        public const string Reserved = "clipwise";

        private static readonly Dictionary<string, CategoryDef> _categories = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Claim> _claims = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _tagLabels = new(StringComparer.Ordinal);

        /// <summary>One fact provider per source. Asked every time a picker is built, never cached - see
        /// <see cref="Facts"/>.</summary>
        private static readonly Dictionary<string, Func<string, string>> _factProviders = new(StringComparer.Ordinal);

        private static Dictionary<string, ItemEntry> _resolved;
        private static List<string> _conflicts = new();

        /// <summary>All declared categories, unordered. Ordering happens in the view builder.</summary>
        public static IEnumerable<CategoryDef> Categories => _categories.Values;

        public static int ClaimCount => _claims.Count;

        /// <summary>Conflicting claims found during the last resolve, one human-readable line each.</summary>
        public static IReadOnlyList<string> Conflicts => _conflicts;

        // ----- registration -----

        public static void RegisterCategory(string source, string id, string label, int sortOrder, string iconItemId,
                                            bool isAuto = false)
        {
            if (!ValidIdent(source) || !ValidIdent(id))
            {
                Core.WarnThrottled("category-ident", $"Clipwise: rejected category '{source}:{id}' - source and id must be non-empty and free of ':' and whitespace.");
                return;
            }
            if (!isAuto && string.Equals(source, Reserved, StringComparison.OrdinalIgnoreCase))
            {
                Core.WarnThrottled("category-reserved", $"Clipwise: rejected category '{source}:{id}' - the '{Reserved}' source is reserved for the host.");
                return;
            }

            string key = CategoryDef.MakeKey(source, id);
            if (_categories.TryGetValue(key, out var existing) && !existing.IsAuto && isAuto)
                return;   // a declared category always beats the classifier's stand-in

            _categories[key] = new CategoryDef
            {
                Source = source,
                Id = id,
                Key = key,
                Label = string.IsNullOrWhiteSpace(label) ? id : label.Trim(),
                IconItemId = string.IsNullOrWhiteSpace(iconItemId) ? null : iconItemId.Trim(),
                SortOrder = sortOrder,
                IsAuto = isAuto,
            };
            _resolved = null;
        }

        public static void RegisterItem(string source, string id, string itemId, string categoryKey, string[] tags,
                                        int sortOrder, string sortKey, string description, int precedence,
                                        ClaimOrigin origin)
        {
            if (!ValidIdent(source) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(itemId))
            {
                Core.WarnThrottled("item-ident", $"Clipwise: rejected item claim '{source}/{id}' - source, id and itemId are all required.");
                return;
            }

            var claim = new Claim
            {
                Source = source,
                Id = id.Trim(),
                ItemId = itemId.Trim(),
                CategoryKey = string.IsNullOrWhiteSpace(categoryKey) ? null : categoryKey.Trim(),
                SortKey = string.IsNullOrWhiteSpace(sortKey) ? null : sortKey.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                Tags = SanitizeTags(source, tags),
                SortOrder = sortOrder,
                Precedence = precedence,
                Origin = origin,
            };

            _claims[claim.ClaimKey] = claim;   // upsert
            _resolved = null;
        }

        /// <summary>Let a source answer for its own items with extra card rows. Registering twice replaces.</summary>
        public static void RegisterFacts(string source, Func<string, string> provider)
        {
            if (!ValidIdent(source) || provider == null) return;
            _factProviders[source] = provider;
        }

        /// <summary>
        /// The extra card rows one source has for one item, as (label, value) pairs.
        ///
        /// ASKED, NEVER STORED. The reason is a name: a discoverer's alias lives in the registering mod's own
        /// register and a player can change it at any time, so a value taken once at registration is a name
        /// frozen at mod-start. Nothing here is cached, and a provider that answers nothing leaves the rows out
        /// rather than showing them empty - the card's rule everywhere else.
        ///
        /// Only the source that CLAIMED the item is asked. A mod answering for another mod's entries would be a
        /// way to put words on a card its author never wrote.
        /// </summary>
        public static List<KeyValuePair<string, string>> Facts(string source, string itemId)
        {
            var rows = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(itemId)) return rows;
            if (!_factProviders.TryGetValue(source, out var provider) || provider == null) return rows;

            string raw;
            try { raw = provider(itemId); }
            catch (Exception e)
            {
                Core.WarnThrottled("facts-" + source, "Clipwise: '" + source + "' threw while describing an item, its extra card rows are left out: " + e.Message);
                return rows;
            }

            if (string.IsNullOrEmpty(raw)) return rows;

            foreach (string line in raw.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                int tab = line.IndexOf('\t');
                if (tab <= 0 || tab >= line.Length - 1) continue;

                string label = line.Substring(0, tab).Trim();
                string value = line.Substring(tab + 1).Trim();
                if (label.Length == 0 || value.Length == 0) continue;   // an empty value is "leave the row out"

                rows.Add(new KeyValuePair<string, string>(label, value));
                if (rows.Count >= 12) break;   // a card, not a spreadsheet
            }

            return rows;
        }

        public static void RegisterTagLabel(string tag, string label)
        {
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(label)) return;
            _tagLabels[tag.Trim()] = label.Trim();
        }

        /// <summary>Drop every claim and category from one source, so reloading its JSON file does not leave
        /// orphans behind.</summary>
        public static void ForgetSource(string source)
        {
            if (string.IsNullOrEmpty(source)) return;
            var deadClaims = new List<string>();
            foreach (var kv in _claims)
                if (string.Equals(kv.Value.Source, source, StringComparison.Ordinal)) deadClaims.Add(kv.Key);
            foreach (string k in deadClaims) _claims.Remove(k);

            var deadCats = new List<string>();
            foreach (var kv in _categories)
                if (string.Equals(kv.Value.Source, source, StringComparison.Ordinal)) deadCats.Add(kv.Key);
            foreach (string k in deadCats) _categories.Remove(k);

            _resolved = null;
        }

        /// <summary>Forget every category the classifier invented. Called before a fresh classification pass so
        /// stale prefix clusters do not linger in the tab bar.</summary>
        public static void ForgetAutoCategories()
        {
            var dead = new List<string>();
            foreach (var kv in _categories)
                if (kv.Value.IsAuto) dead.Add(kv.Key);
            foreach (string k in dead) _categories.Remove(k);
            _resolved = null;
        }

        // ----- lookup -----

        public static CategoryDef GetCategory(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _categories.TryGetValue(key, out var c) ? c : null;
        }

        /// <summary>Human label for a tag's filter chip. Falls back to the tag's last path segment, so
        /// <c>"mymod:tier/1"</c> reads as "1" rather than as the raw tag.</summary>
        public static string TagLabel(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            if (_tagLabels.TryGetValue(tag, out string l)) return l;
            int slash = tag.LastIndexOf('/');
            if (slash >= 0 && slash < tag.Length - 1) return tag.Substring(slash + 1);
            int colon = tag.IndexOf(':');
            return colon >= 0 && colon < tag.Length - 1 ? tag.Substring(colon + 1) : tag;
        }

        /// <summary>The winning claim per item, keyed by lowercased item ID. Resolved lazily and cached until the
        /// next registration.</summary>
        public static Dictionary<string, ItemEntry> Resolved()
        {
            if (_resolved != null) return _resolved;

            var byItem = new Dictionary<string, List<Claim>>(StringComparer.OrdinalIgnoreCase);
            foreach (var claim in _claims.Values)
            {
                if (!byItem.TryGetValue(claim.ItemId, out var list))
                {
                    list = new List<Claim>();
                    byItem[claim.ItemId] = list;
                }
                list.Add(claim);
            }

            var result = new Dictionary<string, ItemEntry>(StringComparer.OrdinalIgnoreCase);
            var conflicts = new List<string>();

            foreach (var kv in byItem)
            {
                List<Claim> list = kv.Value;
                Claim best = list[0];
                for (int i = 1; i < list.Count; i++)
                    if (Claim.Compare(list[i], best) > 0) best = list[i];

                if (list.Count > 1)
                {
                    // Same item claimed twice with the same standing: report it instead of letting load order decide.
                    foreach (var other in list)
                    {
                        if (ReferenceEquals(other, best)) continue;
                        if (other.Origin == best.Origin && other.Precedence == best.Precedence
                            && !string.Equals(other.CategoryKey, best.CategoryKey, StringComparison.Ordinal))
                        {
                            conflicts.Add(string.Format(CultureInfo.InvariantCulture,
                                "{0}: '{1}' ({2}) wins over '{3}' ({4}) - equal standing, decided by id order",
                                kv.Key, best.CategoryKey, best.ClaimKey, other.CategoryKey, other.ClaimKey));
                        }
                    }
                }

                var entry = new ItemEntry
                {
                    ItemId = best.ItemId,
                    CategoryKey = best.CategoryKey,
                    SortKey = best.SortKey,
                    Description = best.Description,
                    Source = best.Source,
                    SortOrder = best.SortOrder,
                };

                // Tags merge across ALL claims: a compatibility file may add a tag without owning the category.
                foreach (var claim in list)
                {
                    if (claim.Tags == null) continue;
                    foreach (string t in claim.Tags)
                        if (!entry.Tags.Contains(t)) entry.Tags.Add(t);
                }
                entry.Tags.Sort(StringComparer.Ordinal);

                result[best.ItemId] = entry;
            }

            _conflicts = conflicts;
            _resolved = result;
            return _resolved;
        }

        // ----- validation -----

        /// <summary>A source or category id: non-empty, no ':' (the key separator) and no whitespace.</summary>
        private static bool ValidIdent(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            foreach (char c in s)
                if (c == ':' || char.IsWhiteSpace(c)) return false;
            return true;
        }

        /// <summary>Keeps only namespaced tags belonging to the calling source. A bare tag like "tier3" is dropped
        /// with a warning: two unrelated mods would otherwise fight over the same chip, and a player could not tell
        /// whose it is.</summary>
        private static string[] SanitizeTags(string source, string[] tags)
        {
            if (tags == null || tags.Length == 0) return Array.Empty<string>();
            var keep = new List<string>(tags.Length);
            foreach (string raw in tags)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string t = raw.Trim();
                int colon = t.IndexOf(':');
                if (colon <= 0 || colon == t.Length - 1)
                {
                    Core.WarnThrottled("tag-bare", $"Clipwise: dropped tag '{t}' from '{source}' - tags must be namespaced as \"source:path\".");
                    continue;
                }
                string ns = t.Substring(0, colon);
                if (string.Equals(ns, Reserved, StringComparison.OrdinalIgnoreCase))
                {
                    Core.WarnThrottled("tag-reserved", $"Clipwise: dropped tag '{t}' from '{source}' - the '{Reserved}' namespace is reserved for the host.");
                    continue;
                }
                if (!keep.Contains(t)) keep.Add(t);
            }
            return keep.ToArray();
        }
    }
}

using System;
using System.Collections.Generic;
using Clipwise.Model;

namespace Clipwise.Index
{
    /// <summary>One selectable line in the picker.</summary>
    internal sealed class Row
    {
        /// <summary>The item's registry ID, or "" for the None/Any line.</summary>
        public string ItemId = "";
        /// <summary>Valid for the lifetime of one open picker only - never cached across a scene change.</summary>
        public ItemDefinition Item;
        public string Title = "";
        public string CategoryKey;
        public ItemFacts Facts;
        public List<string> Tags = new();
        public bool IsNone;
        public bool Selected;
        /// <summary>Source of the winning claim, or null when only the classifier placed this row.</summary>
        public string Source;
        /// <summary>Free text the registering mod supplied for the tooltip.</summary>
        public string Description;
    }

    /// <summary>The full, ordered contents of one picker: which tabs exist, which rows they hold, and which tags
    /// can be filtered on. Built fresh every time a field is clicked, because the option list, the discovery state
    /// and the registry contents can all have changed since the last open.</summary>
    internal sealed class View
    {
        public readonly List<CategoryDef> Categories = new();
        public readonly List<Row> Rows = new();
        public readonly List<string> Tags = new();
        public Row NoneRow;
        public string Title = "";
    }

    /// <summary>
    /// Turns a vanilla option list into a categorized, deterministically ordered view.
    ///
    /// Nothing here touches <c>ManagementUtilities.Seeds</c> or <c>ItemField.Options</c>. Those lists stay exactly
    /// as the game built them: their order decides which seed a botanist grabs when a pot is set to "Any", and
    /// reordering them would change gameplay on the host. Clipwise only ever sorts its own copy.
    /// </summary>
    internal static class ViewBuilder
    {
        private const string AutoSource = Catalog.Reserved;
        private const string VanillaId = "vanilla";
        private const string OtherId = "other";
        private const string PrefixIdPrefix = "from-";

        private const int SortVanilla = 0;
        private const int SortPrefixCluster = 1000;
        private const int SortOrphanCategory = 5000;
        private const int SortOther = 9000;

        /// <summary>A prefix has to be shared by at least this many unclaimed items before it earns its own tab -
        /// otherwise a single stray item would create a one-row category.</summary>
        private const int MinClusterSize = 2;

        public static View Build(string title, IList<ItemDefinition> options, ItemDefinition selected,
                                 bool includeNone, string noneLabel)
        {
            var view = new View { Title = title ?? "" };
            var resolved = Catalog.Resolved();

            Catalog.ForgetAutoCategories();

            if (includeNone)
            {
                view.NoneRow = new Row { IsNone = true, Title = noneLabel ?? "None", Selected = selected == null };
            }

            // Pass 1: facts + winning claim per option, and collect the prefixes of everything unclaimed.
            var pending = new List<(ItemDefinition def, ItemFacts facts, ItemEntry entry)>();
            var prefixCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < options.Count; i++)
            {
                ItemDefinition def = options[i];
                if (def == null) continue;

                ItemFacts facts = ItemFacts.For(def);
                if (facts == null) continue;

                resolved.TryGetValue(facts.ItemId, out ItemEntry entry);
                pending.Add((def, facts, entry));

                if (entry?.CategoryKey == null && facts.IsModded)
                {
                    string prefix = Prefix(facts.ItemId);
                    if (prefix != null)
                        prefixCounts[prefix] = prefixCounts.TryGetValue(prefix, out int n) ? n + 1 : 1;
                }
            }

            // Pass 2: assign a category to every row, inventing auto categories where nothing was declared.
            foreach (var (def, facts, entry) in pending)
            {
                string categoryKey = ResolveCategory(entry, facts, prefixCounts);

                var row = new Row
                {
                    ItemId = facts.ItemId,
                    Item = def,
                    Title = facts.DisplayName,   // sortKey orders, it never relabels
                    CategoryKey = categoryKey,
                    Facts = facts,
                    Source = entry?.Source,
                    Description = entry?.Description,
                    Selected = selected != null && string.Equals(facts.ItemId, SafeId(selected), StringComparison.OrdinalIgnoreCase),
                };

                row.Tags.AddRange(facts.AutoTags);
                if (entry != null)
                    foreach (string t in entry.Tags)
                        if (!row.Tags.Contains(t)) row.Tags.Add(t);

                view.Rows.Add(row);
            }

            // Order: category first (sortOrder, then key), then the entry's own order inside it.
            var order = new Dictionary<string, int>(StringComparer.Ordinal);
            var cats = new List<CategoryDef>();
            foreach (var row in view.Rows)
            {
                if (order.ContainsKey(row.CategoryKey)) continue;
                CategoryDef c = Catalog.GetCategory(row.CategoryKey);
                if (c == null) continue;
                order[row.CategoryKey] = 0;
                cats.Add(c);
            }

            cats.Sort((a, b) =>
            {
                if (a.SortOrder != b.SortOrder) return a.SortOrder.CompareTo(b.SortOrder);
                return string.CompareOrdinal(a.Key, b.Key);
            });
            for (int i = 0; i < cats.Count; i++) order[cats[i].Key] = i;
            view.Categories.AddRange(cats);

            view.Rows.Sort((a, b) =>
            {
                int ca = order.TryGetValue(a.CategoryKey, out int x) ? x : int.MaxValue;
                int cb = order.TryGetValue(b.CategoryKey, out int y) ? y : int.MaxValue;
                if (ca != cb) return ca.CompareTo(cb);

                ItemEntry ea = Entry(resolved, a.ItemId);
                ItemEntry eb = Entry(resolved, b.ItemId);
                int sa = ea?.SortOrder ?? 0;
                int sb = eb?.SortOrder ?? 0;
                if (sa != sb) return sa.CompareTo(sb);

                string ka = ea?.SortKey ?? a.Title;
                string kb = eb?.SortKey ?? b.Title;
                int t = string.Compare(ka, kb, StringComparison.InvariantCultureIgnoreCase);
                if (t != 0) return t;
                return string.CompareOrdinal(a.ItemId, b.ItemId);
            });

            // Tag chips: every tag actually present, ordered so the bar does not reshuffle between opens.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in view.Rows)
                foreach (string t in row.Tags)
                    if (seen.Add(t)) view.Tags.Add(t);
            view.Tags.Sort(StringComparer.Ordinal);

            return view;
        }

        private static ItemEntry Entry(Dictionary<string, ItemEntry> resolved, string itemId)
        {
            return itemId != null && resolved.TryGetValue(itemId, out var e) ? e : null;
        }

        private static string SafeId(ItemDefinition def)
        {
            try { return def.ID; } catch { return null; }
        }

        private static string ResolveCategory(ItemEntry entry, ItemFacts facts, Dictionary<string, int> prefixCounts)
        {
            if (entry?.CategoryKey != null)
            {
                if (Catalog.GetCategory(entry.CategoryKey) != null) return entry.CategoryKey;

                // The mod named a category it never declared. Keep its items together under a stand-in rather than
                // dumping them into the rest bucket, and say so once.
                Core.WarnThrottled("orphan-category",
                    $"Clipwise: '{entry.Source}' put items in the undeclared category '{entry.CategoryKey}' - showing it with a fallback label.");
                int colon = entry.CategoryKey.IndexOf(':');
                string src = colon > 0 ? entry.CategoryKey.Substring(0, colon) : entry.Source;
                string id = colon >= 0 && colon < entry.CategoryKey.Length - 1 ? entry.CategoryKey.Substring(colon + 1) : entry.CategoryKey;
                Catalog.RegisterCategory(src, id, Prettify(id), SortOrphanCategory, null, isAuto: true);
                return CategoryDef.MakeKey(src, id);
            }

            if (!facts.IsModded)
            {
                EnsureAuto(VanillaId, "Vanilla", SortVanilla);
                return CategoryDef.MakeKey(AutoSource, VanillaId);
            }

            // No prefix clustering. An ID prefix is not a name: a tab labelled "b2s" tells a player nothing and
            // reads as a leaked internal. Anything a mod did not file itself goes into one honest bucket, and the
            // mod's own registration is what earns it a real tab.
            EnsureAuto(OtherId, "Other", SortOther);
            return CategoryDef.MakeKey(AutoSource, OtherId);
        }

        private static void EnsureAuto(string id, string label, int sortOrder)
        {
            if (Catalog.GetCategory(CategoryDef.MakeKey(AutoSource, id)) != null) return;
            Catalog.RegisterCategory(AutoSource, id, label, sortOrder, null, isAuto: true);
        }

        /// <summary>The part of an item ID before the first underscore, which by convention is the adding mod's own
        /// short prefix (<c>b2s_headband_seed</c> -&gt; <c>b2s</c>). Only a grouping hint: an explicit registration
        /// always wins, and a label built from it is shown as-is so a player can see it is a guess.</summary>
        private static string Prefix(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            int u = itemId.IndexOf('_');
            if (u <= 0 || u > 12) return null;
            return itemId.Substring(0, u).ToLowerInvariant();
        }

        private static string Prettify(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string cleaned = s.Replace('-', ' ').Replace('_', ' ').Trim();
            if (cleaned.Length == 0) return s;
            return char.ToUpperInvariant(cleaned[0]) + cleaned.Substring(1);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Clipwise.Index
{
    /// <summary>
    /// Everything Clipwise can learn about an item from the game itself, with no cooperation from the mod that
    /// added it: display name, icon, whether it came from a mod, and - for seeds - the plant it grows and the
    /// product that plant yields, with its effects and market value.
    ///
    /// This is what makes the tooltips and the tag chips work for a third-party seed mod whose author never heard
    /// of Clipwise. Read straight off the prefab, so no plant is instantiated.
    /// </summary>
    internal sealed class ItemFacts
    {
        public string ItemId;
        public string DisplayName;
        public Sprite Icon;
        public bool IsModded;

        /// <summary>Set when the item is a seed whose plant prefab could be read.</summary>
        public bool HasPlant;
        public int GrowthTimeHours;
        public int BaseYield;
        public string HarvestTarget;

        public string ProductId;
        public string ProductName;
        public string DrugType;
        public float MarketValue;
        public readonly List<string> Effects = new();

        public float PurchasePrice;

        /// <summary>Only set for items the shop gates behind a rank; null when the item has no level requirement.</summary>
        public bool? Unlocked;

        /// <summary>Null when the item has no product to discover; otherwise vanilla's own discovery state.</summary>
        public bool? Discovered;

        /// <summary>True when the ScriptableObject's <c>name</c> and its <c>ID</c> differ case-insensitively. The
        /// pot's seed choice replicates as <c>SelectedItem.name</c> but is resolved by <c>ID</c>, so a mismatch
        /// breaks the selection in co-op only - invisible in singleplayer and in the save file.</summary>
        public bool NameIdMismatch;

        /// <summary>Host-owned tags derived from the facts above, in the reserved <c>clipwise:</c> namespace.</summary>
        public readonly List<string> AutoTags = new();

        // ----- cache -----

        private static readonly Dictionary<string, ItemFacts> _cache = new(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> _moddedIds;

        /// <summary>Drop the cache. Runtime-registered definitions are removed from the registry on every scene
        /// change, so both the facts and the modded-item set have to be read again.</summary>
        public static void Invalidate()
        {
            _cache.Clear();
            _moddedIds = null;
        }

        public static ItemFacts For(ItemDefinition def)
        {
            if (def == null) return null;
            string id = def.ID ?? "";
            if (id.Length == 0) return null;
            if (_cache.TryGetValue(id, out var cached)) return cached;

            var facts = Build(def, id);
            _cache[id] = facts;
            return facts;
        }

        private static ItemFacts Build(ItemDefinition def, string id)
        {
            var f = new ItemFacts { ItemId = id };

            try
            {
                f.DisplayName = def.Name;
                f.Icon = def.Icon;
                f.NameIdMismatch = !string.Equals(def.name ?? "", id, StringComparison.OrdinalIgnoreCase);
            }
            catch { /* a definition that cannot even report its name still gets a row, just an empty one */ }

            f.IsModded = ModdedIds().Contains(id);
            f.DisplayName = string.IsNullOrWhiteSpace(f.DisplayName) ? id : f.DisplayName;

            var storable = def.TryCast<StorableItemDefinition>();
            if (storable != null)
            {
                try
                {
                    f.PurchasePrice = storable.BasePurchasePrice;
                    if (storable.RequiresLevelToPurchase) f.Unlocked = storable.IsUnlocked;
                }
                catch { }
            }

            ReadPlant(def, f);
            BuildTags(f);
            return f;
        }

        /// <summary>Walks seed -> plant prefab -> final growth stage -> harvestable -> product. Every step is
        /// optional: an additive or a mushroom spawn simply has no plant, and a mod's plant may have no
        /// harvestable wired up.</summary>
        private static void ReadPlant(ItemDefinition def, ItemFacts f)
        {
            try
            {
                var seed = def.TryCast<SeedDefinition>();
                Plant plant = seed?.PlantPrefab;
                if (plant == null) return;

                f.HasPlant = true;
                f.GrowthTimeHours = plant.GrowthTime;
                f.BaseYield = plant.BaseYieldQuantity;
                f.HarvestTarget = plant.HarvestTarget;

                var stages = plant.GrowthStages;
                if (stages == null || stages.Length == 0) return;
                PlantGrowthStage final = stages[stages.Length - 1];
                var sites = final?.GrowthSites;
                if (sites == null) return;

                for (int i = 0; i < sites.Length; i++)
                {
                    Transform site = sites[i];
                    if (site == null) continue;
                    // includeInactive: on the prefab the harvestables sit on deactivated growth sites.
                    var harvestable = site.GetComponentInChildren<PlantHarvestable>(true);
                    StorableItemDefinition product = harvestable?.Product;
                    if (product == null) continue;

                    f.ProductId = product.ID;
                    f.ProductName = product.Name;

                    var pd = product.TryCast<ProductDefinition>();
                    if (pd != null)
                    {
                        f.MarketValue = pd.MarketValue;
                        try { f.DrugType = pd.DrugType.ToString(); } catch { }

                        var props = pd.Properties;
                        if (props != null)
                            for (int p = 0; p < props.Count; p++)
                            {
                                string n = props[p]?.Name;
                                if (!string.IsNullOrWhiteSpace(n) && !f.Effects.Contains(n)) f.Effects.Add(n);
                            }

                        f.Discovered = IsDiscovered(pd);
                    }
                    break;   // one harvestable is enough: every growth site on a plant yields the same product
                }
            }
            catch (Exception e)
            {
                Core.LogDebug($"Clipwise: could not read plant facts for '{f.ItemId}': {e.Message}");
            }
        }

        private static bool IsDiscovered(ProductDefinition pd)
        {
            try
            {
                var list = ProductManager.DiscoveredProducts;
                if (list == null) return false;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && string.Equals(list[i].ID, pd.ID, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { }
            return false;
        }

        private static void BuildTags(ItemFacts f)
        {
            f.AutoTags.Add(f.IsModded ? "clipwise:modded" : "clipwise:vanilla");
            if (f.Discovered == true) f.AutoTags.Add("clipwise:discovered");
            if (!string.IsNullOrEmpty(f.DrugType)) f.AutoTags.Add("clipwise:drug/" + Slug(f.DrugType));
            foreach (string e in f.Effects) f.AutoTags.Add("clipwise:effect/" + Slug(e));
        }

        /// <summary>Item IDs the registry received at runtime, i.e. everything a mod added. Rebuilt per scene
        /// because vanilla clears that list on <c>onPreSceneChange</c>.</summary>
        private static HashSet<string> ModdedIds()
        {
            if (_moddedIds != null) return _moddedIds;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var registry = GameRegistry.Instance;
                var added = registry?.ItemsAddedAtRuntime;
                if (added != null)
                    for (int i = 0; i < added.Count; i++)
                    {
                        string id = added[i]?.ID;
                        if (!string.IsNullOrEmpty(id)) set.Add(id);
                    }
            }
            catch (Exception e)
            {
                Core.WarnThrottled("modded-set", "Clipwise: could not read the registry's runtime-item list, treating every item as vanilla: " + e.Message);
            }
            _moddedIds = set;
            return _moddedIds;
        }

        private static string Slug(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            return sb.ToString().Trim('-');
        }

        /// <summary>The tooltip body: one "Label: value" line per fact that is actually known.</summary>
        public List<KeyValuePair<string, string>> TooltipRows()
        {
            var rows = new List<KeyValuePair<string, string>>();
            var ic = CultureInfo.InvariantCulture;

            if (Effects.Count > 0) rows.Add(new("Effects", string.Join(", ", Effects)));
            if (HasPlant)
            {
                string target = string.IsNullOrWhiteSpace(HarvestTarget) ? "" : " " + HarvestTarget;
                if (BaseYield > 0) rows.Add(new("Yield", BaseYield.ToString(ic) + target));
                if (GrowthTimeHours > 0) rows.Add(new("Growth", GrowthTimeHours.ToString(ic) + " h"));
            }
            if (!string.IsNullOrEmpty(ProductName)) rows.Add(new("Product", ProductName));
            if (MarketValue > 0f) rows.Add(new("Market value", "$" + MarketValue.ToString("0.##", ic)));
            if (PurchasePrice > 0f) rows.Add(new("Buy price", "$" + PurchasePrice.ToString("0.##", ic)));
            if (Unlocked.HasValue) rows.Add(new("Shop unlocked", Unlocked.Value ? "yes" : "no"));
            if (Discovered.HasValue) rows.Add(new("Discovered", Discovered.Value ? "yes" : "no"));
            return rows;
        }
    }
}

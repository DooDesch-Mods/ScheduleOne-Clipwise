using System.Collections.Generic;

namespace Clipwise.Model
{
    /// <summary>Where a claim came from. The numeric order IS the precedence order: a user override beats the
    /// author's own API registration, which beats whatever the classifier guessed. Without a fixed order the mod
    /// load order would silently become policy.</summary>
    internal enum ClaimOrigin
    {
        Auto = 0,
        Api = 1,
        Override = 2,
    }

    /// <summary>One tab in the picker. <see cref="Key"/> is the canonical address ("source:id") every claim points
    /// at; two mods can both use the id "exotics" without colliding because their source differs.</summary>
    internal sealed class CategoryDef
    {
        public string Source;
        public string Id;
        public string Key;
        public string Label;
        public string IconItemId;
        public int SortOrder;

        /// <summary>True for categories the classifier invented (vanilla / a prefix cluster / the rest bucket)
        /// rather than ones a mod declared. Auto categories lose against a declared one with the same key.</summary>
        public bool IsAuto;

        public static string MakeKey(string source, string id) => (source ?? "") + ":" + (id ?? "");
    }

    /// <summary>One source's assertion about where an item belongs. Several sources may claim the same item; the
    /// winner is picked by <see cref="Origin"/>, then <see cref="Precedence"/>, then (Source, Id) ordinally - never
    /// by registration order, so the result is the same on every launch and on every co-op peer.</summary>
    internal sealed class Claim
    {
        public string Source;
        public string Id;
        public string ItemId;
        public string CategoryKey;
        public string SortKey;
        public string Description;
        public string[] Tags;
        public int SortOrder;
        public int Precedence;
        public ClaimOrigin Origin;

        /// <summary>Identity of the claim itself: re-registering the same pair is an upsert, not a duplicate.</summary>
        public string ClaimKey => (Source ?? "") + "/" + (Id ?? "");

        /// <summary>Ranks two claims for the same item. Positive means <paramref name="a"/> wins.</summary>
        public static int Compare(Claim a, Claim b)
        {
            if (a.Origin != b.Origin) return a.Origin > b.Origin ? 1 : -1;
            if (a.Precedence != b.Precedence) return a.Precedence > b.Precedence ? 1 : -1;
            int s = string.CompareOrdinal(b.Source ?? "", a.Source ?? "");
            if (s != 0) return s;
            return string.CompareOrdinal(b.Id ?? "", a.Id ?? "");
        }
    }

    /// <summary>A resolved row: the winning claim for one item, with the tags of every claim merged in. Holds only
    /// the item's ID string - never the <c>ItemDefinition</c> itself, because runtime-registered definitions are
    /// dropped from the registry on every scene change.</summary>
    internal sealed class ItemEntry
    {
        public string ItemId;
        public string CategoryKey;
        public string SortKey;
        public string Description;
        public string Source;
        public int SortOrder;
        public List<string> Tags = new List<string>();
    }
}

using MelonLoader;

namespace Clipwise.Config
{
    /// <summary>
    /// MelonPreferences for Clipwise. Values live in &lt;game&gt;/UserData/MelonPreferences.cfg and PERSIST once
    /// written, so changing a default here has no effect on an existing config - edit the cfg to re-test.
    ///
    /// Only plain switches belong here. Lists (favourites, hidden items, collapsed categories) live in
    /// UserData/Clipwise/preferences.json, because an entry containing a comma would break an ini-style config.
    /// </summary>
    internal static class Preferences
    {
        private const string Category = "Clipwise";

        private static MelonPreferences_Category _cat;

        private static MelonPreferences_Entry<bool> _enabled;
        private static MelonPreferences_Entry<int> _minOptions;
        private static MelonPreferences_Entry<bool> _tooltips;
        private static MelonPreferences_Entry<bool> _onlyDiscoveredDefault;
        private static MelonPreferences_Entry<bool> _verbose;
        private static MelonPreferences_Entry<bool> _surfacePicker;

        /// <summary>Master switch. Off = the field click is never intercepted and vanilla runs untouched.</summary>
        internal static bool Enabled => _enabled?.Value ?? true;

        /// <summary>Fields with fewer options than this keep the vanilla grid, unless one of their items has a
        /// registered category. A pot's three additives do not need tabs and a search box.</summary>
        internal static int MinOptions => _minOptions?.Value ?? 12;

        /// <summary>Show the floating tooltip while hovering a row.</summary>
        internal static bool Tooltips => _tooltips?.Value ?? true;

        /// <summary>Initial state of the "only discovered" filter. Off by default: a fresh install must not hide
        /// anything the player expects to see.</summary>
        internal static bool OnlyDiscoveredDefault => _onlyDiscoveredDefault?.Value ?? false;

        /// <summary>Log every classification decision. Loud - for diagnosing a mod that lands in the wrong tab.</summary>
        internal static bool Verbose => _verbose?.Value ?? false;

        /// <summary>Draw the picker as a Sideload surface instead of the hand-built uGUI card. OFF: it is a spike,
        /// and it does not yet do tag chips, hidden items or the hover tooltip.</summary>
        internal static bool SurfacePicker => _surfacePicker?.Value ?? false;

        internal static void Initialize()
        {
            _cat = MelonPreferences.CreateCategory(Category, "Clipwise");

            _enabled = _cat.CreateEntry("Enabled", true,
                description: "Master switch. Disable to leave the vanilla clipboard item grid completely untouched.");
            _minOptions = _cat.CreateEntry("MinOptions", 12,
                description: "Fields with fewer selectable options than this keep the vanilla grid (unless a mod registered a category for one of their items).");
            _tooltips = _cat.CreateEntry("Tooltips", true,
                description: "Show the floating info tooltip while hovering an entry.");
            _onlyDiscoveredDefault = _cat.CreateEntry("OnlyDiscoveredDefault", false,
                description: "Start the picker with the \"only discovered\" filter already on.");
            _verbose = _cat.CreateEntry("Verbose", false,
                description: "Log how every item was classified. Useful when a mod's items land in the wrong tab.");
            _surfacePicker = _cat.CreateEntry("SurfacePicker", false,
                description: "Experimental: draw the picker as the vanilla clipboard page, with Sideload instead of the built-in card. Needs Sideload installed. Missing features: tag chips, hidden items, tooltips.");
        }
    }
}

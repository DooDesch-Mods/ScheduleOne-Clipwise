using System;
using System.Collections.Generic;
using Clipwise.Bridge;
using Clipwise.Config;
using Clipwise.Content;
using Clipwise.Index;
using Clipwise.Prefs;
using Clipwise.UI;
using DooDesch.UI;
using MelonLoader;

[assembly: MelonInfo(typeof(Clipwise.Core), "Clipwise", "1.0.0", "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-Clipwise")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Clipwise
{
    /// <summary>
    /// MelonLoader entry point.
    ///
    /// Clipwise owns no game state. It intercepts the click on a management-clipboard item field, shows its own
    /// searchable picker, and writes the chosen item back through the field's normal setter. The vanilla option
    /// lists are never reordered: <c>ManagementUtilities.Seeds</c> doubles as the host-side priority order a
    /// botanist uses when a pot is set to "Any", so sorting it would change gameplay rather than just the view.
    ///
    /// Item definitions registered at runtime are dropped from the registry on every scene change
    /// (ScheduleOne/Registry.cs:181-189), so the catalog stores ID strings only and the facts cache is rebuilt per
    /// scene.
    /// </summary>
    public sealed class Core : MelonMod
    {
        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log { get; private set; }

        /// <summary>Debug-only trace log - compiled out of Release builds so the release log stays clean.</summary>
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDebug(string msg) { Log?.Msg(msg); }

        private static readonly Dictionary<string, int> _throttled = new(StringComparer.Ordinal);
        private static bool _rescanned;

        /// <summary>
        /// A warning that can fire from a per-frame or per-registration path, logged at most once every ten seconds
        /// per <paramref name="key"/>.
        ///
        /// A malformed registration repeated by a loop, or a fault in a path driven by what the player hovers,
        /// produces one warning per iteration. That buries the rest of the log within seconds, and the hundredth
        /// copy says nothing the first one did not.
        /// </summary>
        public static void WarnThrottled(string key, string msg)
        {
            int frame = Time.frameCount;
            if (_throttled.TryGetValue(key, out int last) && frame - last < 600) return;
            _throttled[key] = frame;
            Log?.Warning(msg);
        }

        public override void OnInitializeMelon()
        {
            Instance = this;
            Log = LoggerInstance;

            Preferences.Initialize();

            if (!Preferences.Enabled)
            {
                Log.Msg("Clipwise is disabled in preferences - the vanilla item grid stays untouched.");
                return;
            }

            UserPrefs.Load();

            // Install the bridge before anything else: a consumer mod that loads later binds immediately, and one
            // that already ran queued its registrations in the shim and replays them on bind.
            BridgeHost.Install();

            // Mods that loaded before Clipwise are picked up here; ones that load later bind through the shim.
            ProbeScanner.RunAll();

            OverrideLoader.LoadAll();

            Log.Msg($"Clipwise ready. {Catalog.ClaimCount} registered item claim(s), overrides from {OverrideLoader.LoadedSources.Count} file(s).");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (!Preferences.Enabled) return;

            // Second and last sweep: a mod whose assembly was not loaded yet at init time gets one more chance
            // before any clipboard can be opened.
            if (!_rescanned)
            {
                _rescanned = true;
                ProbeScanner.RunAll();
            }

            // Runtime-registered definitions are gone and re-added per scene, and discovery state differs per save.
            ItemFacts.Invalidate();
            ItemPicker.Close();
            // The canvas the tooltip was parented to is gone with the old scene; rebuild it on the next hover.
            Tooltip.Clear();
            SmoothScroll.Clear();
        }

        public override void OnUpdate()
        {
            ItemPicker.Tick();
            Tooltip.Tick();
            SmoothScroll.Tick();

#if DEBUG
            Clipwise.Debugging.TestKit.FlushToConsole();
#endif
        }

        public override void OnApplicationQuit()
        {
            UserPrefs.Flush();
        }
    }
}

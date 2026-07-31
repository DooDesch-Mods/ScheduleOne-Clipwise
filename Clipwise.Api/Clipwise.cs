using System;
using System.Collections.Generic;
using System.Reflection;

namespace Clipwise.Api
{
    /// <summary>
    /// Clipwise's modder API. Reference Clipwise.Api.dll OR drop this single file into your mod.
    ///
    /// The management clipboard shows every selectable item of a field in one flat, unscrollable icon grid.
    /// Clipwise replaces that with a searchable, tabbed picker. Tell it which category your items belong to and
    /// they get their own tab instead of being mixed into the vanilla ones.
    ///
    /// Every call is a zero-overhead no-op when Clipwise is not installed and lights up automatically when it is,
    /// so this can ship unconditionally with no hard dependency. Registration order does not matter: calls made
    /// before Clipwise loads are queued and replayed on bind.
    ///
    /// <code>
    ///   using Clipwise.Api;
    ///   Clipboard.Category("doodesch.breedtoseed", "tier-1", "Bred - Tier 1", sortOrder: 100)
    ///            .Item("headband", "b2s_headband_seed", sortKey: "Headband",
    ///                  tags: new[] { "doodesch.breedtoseed:tier/1" });
    /// </code>
    ///
    /// Tip: a class named <c>ClipwiseProbe</c> with a static <c>Register()</c> is auto-discovered and called on
    /// bind (see <see cref="AutoRegister"/>), so a mod's Core does not have to wire anything.
    ///
    /// Identifiers: <paramref name="source"/> is the reverse-DNS id of the registering mod
    /// (<c>"doodesch.breedtoseed"</c>). A category's canonical key is <c>"source:id"</c>. Tags MUST be namespaced
    /// the same way (<c>"doodesch.breedtoseed:tier/1"</c>) - bare tags are rejected by the host so two unrelated
    /// mods cannot fight over a name like "tier3". The <c>clipwise:</c> namespace is reserved for the host.
    ///
    /// All calls MUST be made from the Unity main thread.
    /// </summary>
    public static class Clipboard
    {
        // bound bridge delegates (null until the host is found)
        private static bool _bound;
        private static bool _autoDone;
        private static int _probeAttempts;
        private static readonly List<Action> _pending = new List<Action>();

        private static Action<string, string, string, int, string> _registerCategory;
        private static Action<string, string, string, string, string[], int, string, string, int> _registerItem;
        private static Action<string, string> _registerTagLabel;

        /// <summary>True only when the Clipwise host is installed AND bound. Rarely needed - the API is a safe
        /// no-op when absent.</summary>
        public static bool Available { get { EnsureBound(); return _bound; } }

        /// <summary>The host's ABI version: 0 when Clipwise is absent, 1 for the initial contract. Use this only to
        /// gate on capabilities added in a later ABI; everything below degrades to a no-op on its own.</summary>
        public static int AbiVersion { get { EnsureBound(); return _abi; } }
        private static int _abi;

        /// <summary>Declare a category - one tab in the picker. <paramref name="sortOrder"/> orders the tabs
        /// (lower first; vanilla sits at 0). <paramref name="iconItemId"/> optionally names an item whose icon
        /// represents the tab. Returns a fluent builder for the items in it. Load-order-proof.</summary>
        public static CategoryRef Category(string source, string id, string label, int sortOrder = 0, string iconItemId = null)
        {
            var cat = new CategoryRef(source, id);
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(id)) return cat;
            string lbl = label, icon = iconItemId;
            EnsureBound();
            if (_registerCategory != null) _registerCategory(source, id, lbl, sortOrder, icon);
            else _pending.Add(() => _registerCategory?.Invoke(source, id, lbl, sortOrder, icon));
            return cat;
        }

        /// <summary>Same as <see cref="Category"/> without the builder, for callers that keep their own loop.</summary>
        public static void RegisterCategory(string source, string id, string label, int sortOrder = 0, string iconItemId = null)
        {
            Category(source, id, label, sortOrder, iconItemId);
        }

        /// <summary>Place one item in a category.
        /// <paramref name="id"/> is this mod's own stable key for the entry, unique within <paramref name="source"/>;
        /// <paramref name="itemId"/> is the game's <c>ItemDefinition.ID</c> the entry stands for;
        /// <paramref name="category"/> is a canonical category key (<c>"source:id"</c>).
        /// <paramref name="sortKey"/> orders entries inside the category (falls back to the item's display name);
        /// <paramref name="description"/> shows up in the tooltip; <paramref name="precedence"/> breaks ties when
        /// several sources claim the same item (higher wins).
        ///
        /// Returns nothing on purpose: a call queued before Clipwise loaded cannot know whether the future host
        /// accepts it. Re-registering the same (source, id) is an idempotent upsert, so calling this twice is safe.
        /// Load-order-proof.</summary>
        public static void RegisterItem(string source, string id, string itemId, string category,
                                        string[] tags = null, int sortOrder = 0, string sortKey = null,
                                        string description = null, int precedence = 0)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(itemId)) return;
            string[] t = tags;
            string cat = category, sk = sortKey, desc = description;
            EnsureBound();
            if (_registerItem != null) _registerItem(source, id, itemId, cat, t, sortOrder, sk, desc, precedence);
            else _pending.Add(() => _registerItem?.Invoke(source, id, itemId, cat, t, sortOrder, sk, desc, precedence));
        }

        /// <summary>Give a tag a human-readable label for its filter chip, e.g. <c>"mymod:tier/1"</c> -&gt;
        /// <c>"Tier 1"</c>. Without this the chip shows the tag's last path segment. Load-order-proof.</summary>
        public static void RegisterTagLabel(string tag, string label)
        {
            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(label)) return;
            string tg = tag, lbl = label;
            EnsureBound();
            if (_registerTagLabel != null) _registerTagLabel(tg, lbl);
            else _pending.Add(() => _registerTagLabel?.Invoke(tg, lbl));
        }

        /// <summary>Discover a convention type named <c>ClipwiseProbe</c> with a static <c>Register()</c> in THIS
        /// mod's own assembly and invoke it once - so a mod never has to wire a Register() call into its Core.
        /// Drive it from a <c>[ModuleInitializer]</c> in the probe file. No-op + load-order-proof.</summary>
        public static void AutoRegister()
        {
            EnsureBound();
            if (_bound) RunAutoRegister();   // else: the bind flush will run it (load-order-proof, both directions)
        }

        /// <summary>Fluent handle to a declared category. Every <see cref="Item"/> call lands in it, so the source
        /// and category key are stated once.</summary>
        public sealed class CategoryRef
        {
            /// <summary>The registering mod's reverse-DNS id.</summary>
            public string Source { get; }
            /// <summary>The category's id within <see cref="Source"/>.</summary>
            public string Id { get; }
            /// <summary>The canonical <c>"source:id"</c> key this category is addressed by.</summary>
            public string Key { get; }

            internal CategoryRef(string source, string id)
            {
                Source = source ?? "";
                Id = id ?? "";
                Key = Source + ":" + Id;
            }

            /// <summary>Place an item in this category. See <see cref="Clipboard.RegisterItem"/> for the parameters.</summary>
            public CategoryRef Item(string id, string itemId, string[] tags = null, int sortOrder = 0,
                                    string sortKey = null, string description = null, int precedence = 0)
            {
                Clipboard.RegisterItem(Source, id, itemId, Key, tags, sortOrder, sortKey, description, precedence);
                return this;
            }
        }

        private static void RunAutoRegister()
        {
            if (_autoDone) return;
            _autoDone = true;   // latch before invoking so a throw can't loop
            try
            {
                Assembly self = typeof(Clipboard).Assembly;   // only this mod's assembly - single, fast, no AppDomain scan
                Type probe = self.GetType("ClipwiseProbe", false) ?? FindByLeafName(self, "ClipwiseProbe");
                MethodInfo reg = probe?.GetMethod("Register",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                reg?.Invoke(null, null);
            }
            catch { /* a mod's probe threw -> stays a no-op, never crashes the mod */ }
        }

        private static Type FindByLeafName(Assembly asm, string leaf)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            catch { return null; }
            if (types == null) return null;
            foreach (Type t in types)
                if (t != null && t.IsClass && t.IsAbstract && t.IsSealed && t.Name == leaf) return t;   // static class
            return null;
        }

        // ----- reflection handshake (runs until it binds, then latches) -----

        private static void EnsureBound()
        {
            if (_bound) return;   // bound once, never probe again (fast path)
            try
            {
                Type t = FindBridge((_probeAttempts++ % 30) == 0);
                if (t == null) return;   // host not present yet - cheap re-probe next call (load-order proof)
                object abi = t.GetField("AbiVersion", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (abi is int v && v < 1) return;
                _abi = abi is int av ? av : 1;

                _registerCategory = Get<Action<string, string, string, int, string>>(t, "RegisterCategory");
                _registerItem = Get<Action<string, string, string, string, string[], int, string, string, int>>(t, "RegisterItem");
                _registerTagLabel = Get<Action<string, string>>(t, "RegisterTagLabel");

                if (_registerCategory == null || _registerItem == null) return;   // partial table - try again next call
                _bound = true;

                // flush any registrations made before the host was up
                for (int i = 0; i < _pending.Count; i++) { try { _pending[i](); } catch { } }
                _pending.Clear();
                RunAutoRegister();
            }
            catch { /* any failure -> stays a no-op, retries next call */ }
        }

        private static T Get<T>(Type t, string field) where T : class
        {
            object v = t.GetField(field, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            return v as T;   // works because Func<>/Action<> are shared BCL types in both assemblies
        }

        private static Type FindBridge(bool scan)
        {
            Type t = Type.GetType("Clipwise.Bridge.ClipwiseBridge, Clipwise", false);
            if (t != null || !scan) return t;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType("Clipwise.Bridge.ClipwiseBridge", false); if (t != null) return t; }
                catch { }
            }
            return null;
        }
    }
}

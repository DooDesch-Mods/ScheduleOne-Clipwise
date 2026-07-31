using Clipwise.Index;
using Clipwise.Model;

namespace Clipwise.Bridge
{
    /// <summary>Fills <see cref="ClipwiseBridge"/> once, translating the flat BCL-only contract into calls on the
    /// catalog. Every entry point swallows its exceptions: a malformed registration from one mod must never take
    /// down the mod that made it, let alone Clipwise.</summary>
    internal static class BridgeHost
    {
        private static bool _installed;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            ClipwiseBridge.RegisterCategory = (source, id, label, sortOrder, iconItemId) =>
            {
                try { Catalog.RegisterCategory(source, id, label, sortOrder, iconItemId); }
                catch (System.Exception e) { Core.Log.Warning("Clipwise: RegisterCategory failed: " + e.Message); }
            };

            ClipwiseBridge.RegisterItem = (source, id, itemId, category, tags, sortOrder, sortKey, description, precedence) =>
            {
                try { Catalog.RegisterItem(source, id, itemId, category, tags, sortOrder, sortKey, description, precedence, ClaimOrigin.Api); }
                catch (System.Exception e) { Core.Log.Warning("Clipwise: RegisterItem failed: " + e.Message); }
            };

            ClipwiseBridge.RegisterTagLabel = (tag, label) =>
            {
                try { Catalog.RegisterTagLabel(tag, label); }
                catch (System.Exception e) { Core.Log.Warning("Clipwise: RegisterTagLabel failed: " + e.Message); }
            };
        }
    }
}

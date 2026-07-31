using System;

namespace Clipwise.Bridge
{
    /// <summary>
    /// The ONE stable contract between the Clipwise host and the modder shim (Clipwise.Api). The shim locates this
    /// type by full name via reflection and binds these standard-BCL delegates - so the two assemblies share no
    /// custom type and stay version-independent. NEVER rename this type, its namespace, or these fields without
    /// bumping <see cref="AbiVersion"/>; only ADD fields (additive ABI). A newer shim against an older host simply
    /// leaves its delegate null and degrades to a no-op. Filled by <see cref="BridgeHost"/>.
    /// </summary>
    public static class ClipwiseBridge
    {
        public const int AbiVersion = 1;

        // source, id, label, sortOrder, iconItemId
        public static Action<string, string, string, int, string> RegisterCategory;

        // source, id, itemId, categoryKey, tags, sortOrder, sortKey, description, precedence
        public static Action<string, string, string, string, string[], int, string, string, int> RegisterItem;

        // tag, label
        public static Action<string, string> RegisterTagLabel;
    }
}

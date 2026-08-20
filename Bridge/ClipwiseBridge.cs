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
        /// <summary>2 added <see cref="RegisterFacts"/>. Additive only: an older shim never reads the new field,
        /// a newer shim finds it null against an older host and degrades to a no-op.</summary>
        public const int AbiVersion = 2;

        // source, id, label, sortOrder, iconItemId
        public static Action<string, string, string, int, string> RegisterCategory;

        // source, id, itemId, categoryKey, tags, sortOrder, sortKey, description, precedence
        public static Action<string, string, string, string, string[], int, string, string, int> RegisterItem;

        // tag, label
        public static Action<string, string> RegisterTagLabel;

        // source, provider: given an itemId the provider answers "LABEL	value" lines, one per fact
        //
        // A CALLBACK RATHER THAN A VALUE, and that is the whole point of it. A fact like "who discovered this"
        // resolves against a live register - a player renames themselves at the desk and every surface has to
        // follow - so a string sent once at registration time is a name frozen at the moment the mod started.
        // This is asked every time the picker opens instead.
        //
        // `Func<string, string>` is a shared BCL type, so the two assemblies still have nothing in common but
        // the framework, which is the rule this whole contract exists to keep.
        public static Action<string, Func<string, string>> RegisterFacts;
    }
}

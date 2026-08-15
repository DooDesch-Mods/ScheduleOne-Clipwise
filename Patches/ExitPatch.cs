using Clipwise.UI;
using HarmonyLib;

namespace Clipwise.Patches
{
    /// <summary>
    /// Makes Escape (and right-click) close the picker without also closing the clipboard behind it.
    ///
    /// Vanilla dispatches one exit through a priority-ordered listener list, and <c>ClipboardScreen</c> claims it at
    /// priority 10. Registering another listener would mean handing the game an IL2CPP delegate; swallowing the
    /// dispatch for the one frame the picker is open achieves the same thing in managed code only, and no listener
    /// can act on an exit the player meant for the picker.
    /// </summary>
    [HarmonyPatch(typeof(GameInput), "Exit")]
    internal static class ExitPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            if (!SurfacePicker.IsOpen) return true;
            SurfacePicker.Close();
            return false;
        }
    }
}

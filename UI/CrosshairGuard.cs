using System;
using UnityEngine;
using UnityEngine.UI;

namespace Clipwise.UI
{
    /// <summary>
    /// Stop the game's crosshair eating the mouse wheel while the picker is open.
    ///
    /// THE BUG THIS EXISTS FOR. The seed list would not scroll with the pointer over the tiles - and the tiles are
    /// the whole list, so in practice it would not scroll at all. It looked like the list refusing to move, and it
    /// was not: the notch never arrived. Sideload's own wheel probe named the culprit outright:
    ///
    ///   [Sideload/probe] clipwise: wheel -3 hit 'UI/HUD/Crosshair' (1 candidate(s)), scroll handled by '&lt;none&gt;'.
    ///
    /// `UI/HUD/Crosshair` is an `Image` at the exact centre of the screen with `raycastTarget` on. The clipboard
    /// opens centred, so the seed tiles sit underneath it - and an Image that catches the ray and handles no scroll
    /// swallows the wheel and passes nothing on. Clicks were never affected, which is why this read as a scrolling
    /// problem rather than an input one.
    ///
    /// WHY THE CROSSHAIR AND NOT THE LIST. The engine already forwards the wheel from any interactive element to
    /// the nearest scroll area above it, and the tiles are inside one. That path was correct the whole time; it was
    /// simply never reached.
    ///
    /// ONLY raycastTarget, never the object. The crosshair stays visible and stays where it is - turning it off
    /// would change what the player sees for a reason that has nothing to do with looks. And the previous value is
    /// remembered rather than assumed, because a mod or a future patch may already have turned it off.
    /// </summary>
    internal static class CrosshairGuard
    {
        private const string Path = "UI/HUD/Crosshair";

        private static Image _crosshair;
        private static bool _was;
        private static bool _muted;

        /// <summary>The wheel reaches the picker from here until <see cref="Restore"/>. Idempotent.</summary>
        internal static void Mute()
        {
            if (_muted) return;

            try
            {
                Image found = Find();
                if (found == null) return;

                _crosshair = found;
                _was = found.raycastTarget;
                found.raycastTarget = false;
                _muted = true;
            }
            catch (Exception e) { Core.Log.Warning("[Clipwise] could not free the wheel from the crosshair: " + e.Message); }
        }

        /// <summary>
        /// Put it back exactly as it was.
        ///
        /// Called from the picker's Close, which runs on every way out - picking, backing out, and the unmount that
        /// follows the clipboard closing. A crosshair left un-raycastable would be a change to the game that
        /// outlives the screen that wanted it.
        /// </summary>
        internal static void Restore()
        {
            if (!_muted) return;

            try
            {
                if (_crosshair != null) _crosshair.raycastTarget = _was;
            }
            catch (Exception e) { Core.Log.Warning("[Clipwise] could not restore the crosshair: " + e.Message); }
            finally
            {
                _crosshair = null;
                _muted = false;
            }
        }

        /// <summary>
        /// The crosshair, by the path the probe reported.
        ///
        /// Not cached across opens: the HUD is rebuilt with the scene, so a handle kept from the last save points
        /// at a destroyed object - and under IL2CPP that is a null-ish wrapper that answers property writes without
        /// doing anything, which would be a silent no-op instead of an error.
        /// </summary>
        private static Image Find()
        {
            GameObject go = GameObject.Find(Path);
            if (go == null) return null;

            return go.GetComponent<Image>();
        }
    }
}

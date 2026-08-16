using System;
using System.Collections.Generic;
using System.Diagnostics;
using Clipwise.Index;
using Sideload.Api;
using UnityEngine;

namespace Clipwise.UI
{
    /// <summary>
    /// Hands the surface's page the item icons, as PNGs it can draw with <c>src="s1://icon/&lt;id&gt;"</c>.
    ///
    /// WHY THIS HAS TO EXIST AT ALL. A page cannot reach a Sprite - it gets HTML, CSS and strings, and nothing
    /// that lives in Unity. The engine has a store for pictures a mod produced at runtime, and it is keyed by
    /// (id, name) without caring whether the id belongs to an app or a surface, so a surface can fill it the same
    /// way an app does. The shim only exposes it on <c>AppHandle</c>, but this mod compiles the shim in as source,
    /// so the internal entry point is reachable from here.
    ///
    /// WHY IT IS NOT OPTIONAL. The picker draws the game's own seed page, and that page is a grid of tiles. A tile
    /// is sixty pixels wide: it holds a picture, or three letters of a name. Without the icons the grid is the
    /// wrong shape for what it shows.
    ///
    /// EVERY STEP CAN FAIL, AND THE FALLBACK IS A NAME. An icon may be an atlas sub-rect, its texture may be
    /// unreadable, and encoding is a Unity call that IL2CPP is free to have stripped. So each conversion is
    /// guarded on its own and a failure costs one tile its picture rather than taking the picker down.
    /// </summary>
    internal static class SurfaceIcons
    {
        /// <summary>The <c>s1://</c> name a page uses, minus the id.</summary>
        internal const string Prefix = "icon/";

        /// <summary>Item ids already supplied in this process. The store keeps the bytes; this keeps the work
        /// from being done twice, which matters because a clipboard is opened over and over.</summary>
        private static readonly HashSet<string> _done = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Ids whose icon could not be produced, so a broken one is attempted once and not every open.</summary>
        private static readonly HashSet<string> _failed = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Longest this may spend in one open. A picker that takes half a second to appear is a worse
        /// bug than a grid whose last tiles are lettered, and the next open continues where this one stopped.</summary>
        private const int BudgetMs = 250;

        internal static void Supply(SurfaceHandle surface, View view)
        {
            if (view == null || surface == null) return;

            var watch = Stopwatch.StartNew();
            int made = 0, skipped = 0;

            foreach (Row row in view.Rows)
            {
                if (row == null || string.IsNullOrEmpty(row.ItemId)) continue;
                if (_done.Contains(row.ItemId) || _failed.Contains(row.ItemId)) continue;

                if (watch.ElapsedMilliseconds > BudgetMs) { skipped++; continue; }

                // THE LIVE DEFINITION FIRST, and the cached facts only as a fallback.
                //
                // `ItemFacts` captures `def.Icon` once and caches it by item id for the rest of the scene. A mod
                // that dresses its items later - BreedToSeed tints a seed's icon per strain when the strain is
                // materialized - swaps the sprite on the definition long after that snapshot was taken, and the
                // cache never hears about it. The symptom is every vial in the grid drawn as the donor's: one
                // picture, 684 strains, reported as exactly that.
                Sprite icon = row.Item != null ? row.Item.Icon : null;
                if (icon == null) icon = row.Facts?.Icon;
                if (icon == null) { _failed.Add(row.ItemId); continue; }

                // The first few, with the numbers that decide whether a tile can look like itself: a tester
                // reported every vial in the grid drawn identically, and the two candidates - every item
                // handing back the same sprite, versus this conversion cropping the same region out of an
                // atlas for all of them - are told apart by exactly these values.
                if (made < 4)
                    Core.LogDebug("[Clipwise] icon " + row.ItemId + ": sprite '" + icon.name
                                  + "' on '" + (icon.texture != null ? icon.texture.name : "?")
                                  + "' rect " + icon.textureRect + ".");

                byte[] png = Encode(icon);
                if (png == null || png.Length == 0) { _failed.Add(row.ItemId); continue; }

                surface.Image(Prefix + row.ItemId, png);
                _done.Add(row.ItemId);
                made++;
            }

            if (made > 0 || skipped > 0)
                Core.LogDebug("[Clipwise] icons: " + made + " made in " + watch.ElapsedMilliseconds + "ms"
                              + (skipped > 0 ? ", " + skipped + " left for the next open" : "")
                              + (_failed.Count > 0 ? ", " + _failed.Count + " without one" : ""));
        }

        /// <summary>
        /// One sprite as PNG bytes, or null.
        ///
        /// Through a RenderTexture rather than off <c>sprite.texture</c> directly: an item icon is usually an
        /// atlas page and is usually not readable, and both of those make a direct <c>GetPixels</c> throw. A blit
        /// copies the sprite's own rectangle out of whatever it sits in, and the copy is readable by construction.
        /// </summary>
        internal static byte[] Encode(Sprite sprite)
        {
            RenderTexture rt = null;
            RenderTexture previous = null;
            Texture2D copy = null;

            try
            {
                Texture2D source = sprite.texture;
                if (source == null) return null;

                Rect area = sprite.textureRect;
                int w = Mathf.Clamp(Mathf.RoundToInt(area.width), 1, 256);
                int h = Mathf.Clamp(Mathf.RoundToInt(area.height), 1, 256);

                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

                // Normalised, because Blit's scale and offset are in UV space and the sprite's rectangle is in
                // pixels of the page it was packed into.
                var scale = new Vector2(area.width / source.width, area.height / source.height);
                var offset = new Vector2(area.x / source.width, area.y / source.height);
                Graphics.Blit(source, rt, scale, offset);

                previous = RenderTexture.active;
                RenderTexture.active = rt;

                copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                copy.Apply(false, false);

                // Squared before it leaves, with the picture centred in transparent space.
                //
                // A seed vial's sprite is 73 by 225 - one to three - and the tile that draws it is a square. The
                // page cannot fix that: a box in this engine is sized by CSS alone, there is no object-fit, and
                // the mod is the only side that knows the sprite's proportions. So a 73x225 vial was drawn into
                // 40x40 and came out as a dark smear with a dot under it, which is what "every seed looks the
                // same" turned out to be - the pictures were right the whole time. Padding here fixes it for
                // every item at once instead of teaching the page one aspect ratio per category.
                Texture2D squared = Square(copy, w, h);
                try
                {
                    // The extension method, not ImageConversion.EncodeToPNG - the interop assembly puts it on the
                    // texture and has no static class of that name.
                    return (squared ?? copy).EncodeToPNG();
                }
                finally
                {
                    if (squared != null) UnityEngine.Object.Destroy(squared);
                }
            }
            catch (Exception e)
            {
                Core.WarnThrottled("icon-encode", "Clipwise: an item icon could not be converted, its tile falls back to a name: " + e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (copy != null) UnityEngine.Object.Destroy(copy);
            }
        }

        /// <summary>
        /// The same picture on a transparent square, or null when it cannot be built - in which case the caller
        /// ships the unpadded one, which is a squashed tile rather than an empty one.
        /// </summary>
        private static Texture2D Square(Texture2D source, int w, int h)
        {
            if (w == h) return null;

            try
            {
                int side = Mathf.Max(w, h);
                var square = new Texture2D(side, side, TextureFormat.RGBA32, false);

                // A fresh Texture2D is not promised to be blank, and the padding is the whole point - so it is
                // written explicitly. A zeroed Color32 is transparent black.
                square.SetPixels32(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32>(side * side));
                square.SetPixels32((side - w) / 2, (side - h) / 2, w, h, source.GetPixels32());
                square.Apply(false, false);

                return square;
            }
            catch (Exception e)
            {
                Core.WarnThrottled("icon-square", "Clipwise: an icon could not be padded to a square, its tile is squashed: " + e.Message);
                return null;
            }
        }
    }
}

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

        /// <summary>
        /// Ids whose icon could not be CONVERTED, so a broken sprite is attempted once and not every open.
        ///
        /// AN ITEM WITH NO SPRITE AT ALL IS NOT IN HERE, and that distinction is the whole of "ten of nineteen
        /// tiles are empty boxes". A strain catalogue registers its items long before it materialises them, so at
        /// the first open a bred seed's definition has no icon yet - and blacklisting it there meant the tile
        /// stayed blank for the rest of the process even once the mod had dressed it. A missing sprite is a
        /// "not yet"; a sprite that will not encode is a "no".
        /// </summary>
        private static readonly HashSet<string> _failed = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Longest the OPENING pass may spend. A picker that takes half a second to appear is a worse bug than a
        /// grid whose last tiles are lettered.
        ///
        /// WHAT THIS NUMBER USED TO DECIDE, AND MUST NOT AGAIN. It was the only pass there was, so it was also the
        /// ceiling on how many pictures a save could ever have at once - and the rows are sorted with the game's
        /// own category first (ViewBuilder.SortVanilla), so the ones it cut off were always a mod's. Measured with
        /// a save carrying 21 bred strains and the budget forced to 20ms: "6 made, 20 left for the next open",
        /// every vanilla vial drawn and every bred one lettered, which is the report exactly. At the real 250ms it
        /// takes about seventy, so it bit any save past that and never announced itself as anything but a grid of
        /// initials. <see cref="Rest"/> is what finishes the rest now; this only decides how much is paid for in
        /// the frame the clipboard opens.
        /// </summary>
        internal const int OpenBudgetMs = 250;

        /// <summary>What a later frame may spend. Small enough to disappear into a frame that also draws the
        /// world - the picker is up and the player is reading it while this runs.</summary>
        internal const int FrameBudgetMs = 6;

        /// <summary>The longest side of the picture that leaves here - see <see cref="Encode"/>.</summary>
        private const int MaxSide = 128;

        /// <summary>
        /// Convert what is left of <paramref name="view"/>'s icons, for at most <paramref name="budgetMs"/>.
        ///
        /// Returns how many pictures this pass added. <paramref name="more"/> says whether anything is still
        /// waiting, which is what lets the caller come back next frame instead of leaving a grid of initials up
        /// until the player closes the clipboard and opens it again.
        ///
        /// <paramref name="arrived"/> collects the item ids that got one, and the count alone is not a substitute
        /// for it: the search and the filters live in the page and this loop walks every row the mod knows
        /// about, so "some pictures arrived" says nothing about whether any of them has a tile on the screen
        /// right now. Telling the page WHICH ones lets it rebuild only when the answer is yes - and every
        /// rebuild it does not do is a click it cannot swallow.
        /// </summary>
        internal static int Supply(SurfaceHandle surface, View view, int budgetMs, bool announce, out bool more,
                                   List<string> arrived = null)
        {
            more = false;
            if (view == null || surface == null) return 0;

            var watch = Stopwatch.StartNew();
            int made = 0, skipped = 0, bare = 0;

            foreach (Row row in view.Rows)
            {
                if (row == null || string.IsNullOrEmpty(row.ItemId)) continue;
                if (_done.Contains(row.ItemId) || _failed.Contains(row.ItemId)) continue;

                if (watch.ElapsedMilliseconds > budgetMs) { skipped++; more = true; continue; }

                // THE LIVE DEFINITION FIRST, and the cached facts only as a fallback.
                //
                // `ItemFacts` captures `def.Icon` once and caches it by item id for the rest of the scene. A mod
                // that dresses its items later - BreedToSeed tints a seed's icon per strain when the strain is
                // materialized - swaps the sprite on the definition long after that snapshot was taken, and the
                // cache never hears about it. The symptom is every vial in the grid drawn as the donor's: one
                // picture, 684 strains, reported as exactly that.
                Sprite icon = row.Item != null ? row.Item.Icon : null;
                if (icon == null) icon = row.Facts?.Icon;

                // NOT BLACKLISTED - see `_failed`. The next open asks again, which is what lets a strain that was
                // only registered when this picker first opened arrive with its own vial the second time.
                if (icon == null)
                {
                    bare++;
                    if (bare <= 3)
                        Core.LogDebug("[Clipwise] no icon yet for " + row.ItemId
                                      + " (definition " + (row.Item != null ? "present" : "null")
                                      + ", cached facts " + (row.Facts != null ? "present" : "null") + ").");
                    continue;
                }

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
                arrived?.Add(row.ItemId);
                made++;
            }

            // ALWAYS SAID, INCLUDING THE ALL-ZERO CASE. The old line was printed only when something HAPPENED, so
            // an open where every remaining row had no sprite yet logged nothing at all - and a grid of empty
            // tiles with a silent log reads as a page that never asked for its pictures.
            //
            // Not from the per-frame passes, now that there are sixty of those a second: the opening pass says
            // what it did, and the frames after it are summed up in one line when they finish - see Rest.
            if (announce)
                Core.LogDebug("[Clipwise] icons: " + made + " made in " + watch.ElapsedMilliseconds + "ms"
                              + ", " + _done.Count + " ready"
                              + (bare > 0 ? ", " + bare + " with no sprite yet" : "")
                              + (skipped > 0 ? ", " + skipped + " left for a later frame" : "")
                              + (_failed.Count > 0 ? ", " + _failed.Count + " that will not convert" : ""));

            return made;
        }

        /// <summary>Whether this item's picture is in the store, so the page can draw a letter instead of an empty
        /// box. Answered before the page builds - <see cref="Supply"/> runs between the mount and the first
        /// render, so a row's answer is already true or already false by the time it is asked for.</summary>
        internal static bool Has(string itemId) => !string.IsNullOrEmpty(itemId) && _done.Contains(itemId);

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

                // NO BIGGER THAN THE TILE WILL EVER BE, and that is the difference between seventy pictures in a
                // frame and three hundred. A seed vial's sprite is 73x225 and a tile is at most 74 css pixels
                // square: the old clamp kept both numbers, so every icon was blitted, read back and PNG-encoded at
                // 225x225 to be drawn at a third of that. The longest side is held to 128 - still generous against
                // a 74px tile - and the two sides are scaled together, because a clamp per side squashes a tall
                // picture instead of shrinking it.
                Rect area = sprite.textureRect;
                int sw = Mathf.Max(1, Mathf.RoundToInt(area.width));
                int sh = Mathf.Max(1, Mathf.RoundToInt(area.height));

                float k = Mathf.Min(1f, (float)MaxSide / Mathf.Max(sw, sh));
                int w = Mathf.Max(1, Mathf.RoundToInt(sw * k));
                int h = Mathf.Max(1, Mathf.RoundToInt(sh * k));

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

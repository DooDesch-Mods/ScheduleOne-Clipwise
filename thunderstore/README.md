# Clipwise - A Clipboard You Can Actually Read

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/clipwise](https://support.doodesch.de/clipwise).

Pick up the management clipboard, click a pot's seed field, and the game shows you every seed in one flat grid
of look-alike icons. No scrolling, no search, no grouping, and the name only shows up for whichever one your
cursor happens to be on. Install a mod that adds thirty strains and that grid runs off the bottom of your
screen.

Clipwise replaces it with tabs, a search box, filters, favorites, and a tooltip that actually tells you what
a seed grows.

## What you get

- **Tabs instead of a wall:** vanilla seeds in one tab, each content mod's in its own.
- **Search as you type:** matches names, categories, effects and filter labels.
- **Filter chips:** narrow by effect, tier, drug type, or flip on "only discovered".
- **Favorites:** star the five you actually plant; they sit at the top of every tab.
- **Sort how you like:** default order, A-Z, yield, growth time or market value.
- **Hide the rest:** cross out what you never plant. It comes back whenever you want it.
- **Tooltips:** effects, yield, growth time, market value, buy price, discovered or not.
- **Nothing to configure:** mods that never heard of Clipwise still get sorted and still get tooltips.

It covers every item field on the clipboard, not just seeds, so pot additives and mushroom bed spawns come
along for free. Fields with only a handful of options keep the vanilla look, because three additives don't
need a search box.

## It sorts other mods for you

Clipwise reads the game rather than a config. Anything the registry received at runtime is a mod's item, so
vanilla and modded split themselves. Modded items sharing an ID prefix group together. Effects, yield, growth
time and market value come off the seed's own plant prefab, which is why the tooltips work for any seed mod at
all.

Want it filed differently? Drop a JSON file in `UserData/Clipwise/Overrides/` and you get the last word, even
over what a mod registered in code. There's a commented example in that folder after the first launch.

## For mod authors

Two lines and your items get their own tab. Copy `Clipwise.Api/Clipwise.cs` from the GitHub repo into your
project (or build that project and reference the DLL): zero dependencies, load-order independent, and a no-op
when Clipwise isn't installed.

```csharp
using Clipwise.Api;

Clipboard.Category("yourname.yourmod", "exotics", "Exotics", sortOrder: 500)
         .Item("purple-haze", "yourmod_purple_haze_seed", sortKey: "Purple Haze");
```

## Multiplayer

Safe. Clipwise changes what you see, never what the game stores. Your choice still travels over the network as
the item's ID exactly as before, and two players can have completely different tabs open without disagreeing
about anything.

## Install

Needs MelonLoader 0.7.3+, S1API and Sideload 1.31.0+. Drop `Clipwise.dll` into `Schedule I/Mods/`.

Without Sideload the clipboard keeps the game's own item grid: Clipwise draws its picker as a Sideload page,
so there is nothing for it to draw with.

Settings live in `UserData/MelonPreferences.cfg` under `[Clipwise]`. Favorites and hidden entries live in
`UserData/Clipwise/preferences.json`. Neither touches your save, and neither is shared in co-op.

# Clipwise - A Clipboard You Can Actually Read

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/clipwise](https://support.doodesch.de/clipwise).

> Pick up the management clipboard, click a pot's seed field, and the game shows you every seed in one flat
> grid of look-alike icons. No scrolling, no search, no grouping, and the name only shows up for whichever
> one your cursor happens to be on. Install a mod that adds thirty strains and that grid runs off the bottom
> of your screen. Clipwise replaces it with tabs, a search box, filters, favorites, and a tooltip that
> actually tells you what a seed grows.

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![Type](https://img.shields.io/badge/type-framework-orange)

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

## How it sorts mods that never integrated

Clipwise reads the game, not a config:

- Anything the registry received at runtime is a mod's item, so vanilla and modded split themselves.
- Modded items that share an ID prefix are grouped together, with the prefix as the tab name until the mod
  says otherwise.
- Effects, yield, growth time and market value come off the seed's own plant prefab, which is why the
  tooltips work for any seed mod at all.

If you want it filed differently, you can do that yourself without touching anyone's mod. See
[Overrides](#overrides).

## For mod authors

Two lines and your items get their own tab:

```csharp
using Clipwise.Api;

Clipboard.Category("yourname.yourmod", "exotics", "Exotics", sortOrder: 500)
         .Item("purple-haze", "yourmod_purple_haze_seed", sortKey: "Purple Haze");
```

Grab `Clipwise.Api/Clipwise.cs` from this repo and drop it into your project, or build `Clipwise.Api` and
reference the DLL. It has zero dependencies, works whether Clipwise loads before or after you, and does
nothing at all when Clipwise isn't installed, so you can ship it unconditionally with no hard dependency.

Full API, tag conventions and the JSON schema: **[Modder API](https://docs.doodesch.de/mods/clipwise/)**

## Overrides

Drop a JSON file in `UserData/Clipwise/Overrides/` to file items into your own categories, including items
from mods that will never support Clipwise. A file in there beats what a mod registered in code, so you get
the last word. The folder gets a commented example on first launch.

## Install

1. [MelonLoader](https://melonwiki.xyz/) 0.7.3+
2. [S1API](https://thunderstore.io/c/schedule-i/p/ifBars/S1API_Forked/)
3. [Sideload](https://thunderstore.io/c/schedule-i/p/DooDesch/Sideload/) 1.31.0+
4. Drop `Clipwise.dll` into `Schedule I/Mods/`

Without Sideload the clipboard keeps the game's own item grid: Clipwise draws its picker as a Sideload page,
so there is nothing for it to draw with.

Settings live in `UserData/MelonPreferences.cfg` under `[Clipwise]`. Favorites and hidden entries live in
`UserData/Clipwise/preferences.json`. Neither touches your save, and neither is shared in co-op: what you
pin is yours.

## Multiplayer

Safe. Clipwise changes what you see, never what the game stores. Your choice still travels over the network
as the item's ID exactly as before, and two players can have completely different tabs open without
disagreeing about anything. It also never reorders the game's own seed list, because on the host that order
decides which seed a botanist grabs when a pot is set to "Any".

## License

MIT. See [LICENSE.md](LICENSE.md).

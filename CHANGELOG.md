# Changelog

All notable changes to Clipwise are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **Clipwise now needs [Sideload](https://thunderstore.io/c/schedule-i/p/DooDesch/Sideload/).** Without it the clipboard keeps the game's own grid, exactly as if the mod were not installed.
- The picker is drawn as a Sideload page instead of hand-built uGUI, so it looks like the clipboard page it replaces rather than like a mod menu.
- Hiding an entry moved behind the Hidden chip. A tile is sixty pixels wide and cannot carry two permanent buttons.

### Removed

- The built-in uGUI card and its tooltip, 948 lines of them. They were the same screen twice: every change cost double, and the card never did look like the page it replaces.

### Added

- The Sideload picker looks like the vanilla clipboard page it replaces, and adds a search box and a filter bar: all, tier up, tier down, favourites, vanilla, bred.
- The game's own seeds and the ones a mod added sit under separate headings, so a list of 700 strains no longer buries the four you started with.
- The picker is a grid of tiles with the item's own icon, the way the game's page is, and hovering one opens a bubble beside it with the name, the parents, the tier and the effects.
- FAVOURITES is a section of its own at the top, holding starred entries from both halves, and a mod's own section is divided by tier where its items carry one. A tier nobody has anything in does not appear.
- A star on every row, and a Back button. Until now the Sideload picker could only be left by choosing something.

### Fixed

- The keyboard reaches the game again while the picker is open. The search box held the caret whenever nothing else did, and a caret in a field is what stops the game seeing keys - including the Escape that closes the picker. Click the box to type.
- The picker fills the box the game's own selection screen fills. It was a fixed 540x620 in the middle of the screen, and then a copy of the card's position - which put it beside the clipboard, because a position means nothing without the anchors it was measured against.
- The picker sits inside the clipboard's wooden frame. It measured the whole selection screen, which is the board edge to edge; the paper inside it is what the player calls the card.
- A hover bubble also goes away when the pointer moves anywhere that is not a tile. `mouseleave` does not always arrive - through the gap between two tiles, or when the grid redraws under a still pointer - and the bubble outlived its tile.
- The filter bar fits the card. A chip per effect ran off the edge as soon as a mod declared more than a handful; a tag group is a drop-down with tick boxes now, and there is a Clear button when anything is on.
- One hover bubble for the page instead of one per tile. A mouseleave that never arrived left the old ones on screen - five at once in a tester's shot.
- A tile carries its picture and nothing else, the way the game's own does. The name is in the bubble.
- The Discovered filter is gone. Only discovered seeds reach this page, so it never removed anything.
- The heading over a mod's own seeds says what the mod called them. It read "VANILLA" over both halves.
- The Sideload picker offers "Any" again. It was missing, so a pot set to Any could not be set back to it.

### Known

- The Sideload picker is still off by default. Switch it on with `SurfacePicker` in `MelonPreferences.cfg`; tag chips, hidden items and the hover tooltip are only on the built-in card.

## [1.0.0] - 2026-07-30

First release.

### Added

- Replaces the management clipboard's flat item grid with a picker that has category tabs, a live search box,
  tag filter chips, favourites, hiding, five sort modes and a floating tooltip.
- Covers every item field on the clipboard, not just a pot's seed field. Fields with fewer than 12 options
  keep the vanilla grid unless a mod registered a category for one of their items.
- Classifies items with no registration at all: vanilla and modded split by what the registry received at
  runtime, modded items group by ID prefix, and effects, yield, growth time, market value and discovery state
  are read off the seed's own plant prefab.
- Tooltip shows effects, yield, growth time, product, market value, buy price and discovery state, and warns
  when an item's asset name differs from its ID (which breaks selecting it in co-op only).
- `Clipwise.Api`: a single-file, zero-dependency shim mods can reference as a DLL or copy into their project.
  Registration is load-order independent in both directions and a no-op when Clipwise is absent.
- JSON overrides in `UserData/Clipwise/Overrides/` so a player can re-file items from mods that will never
  integrate. An override outranks a mod's own registration.
- Favourites, hidden entries, the sort mode and the last used tab persist in
  `UserData/Clipwise/preferences.json`, outside the game save and never shared in co-op. Sort modes reorder rows
  inside their category, so the tabs and section headers keep their meaning.
- Developer console commands in Debug builds: `cwhelp`, `cwcats`, `cwdump`, `cwconflicts`, `cwnamecheck`,
  `cwauto`, `cwreload`, `cwopen`.

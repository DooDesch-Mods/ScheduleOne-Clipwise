# Changelog

All notable changes to Clipwise are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- The Sideload picker looks like the vanilla clipboard page it replaces, and adds a search box and a filter bar: all, tier up, tier down, favourites, vanilla, bred.
- The game's own seeds and the ones a mod added sit under separate headings, so a list of 700 strains no longer buries the four you started with.
- FAVOURITES is a section of its own at the top, holding starred entries from both halves, and a mod's own section is divided by tier where its items carry one. A tier nobody has anything in does not appear.
- A star on every row, and a Back button. Until now the Sideload picker could only be left by choosing something.

### Fixed

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

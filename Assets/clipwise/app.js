/*
  The picker, as a pad: vanilla's own seed card is the top sheet, and the next sheet hangs off its perforated
  edge and lies past the right edge of the board.

  Wire format is deliberately flat: `picker.view` answers one JSON object, `picker.pick` takes one item id,
  `picker.fav` stars one, `picker.back` closes without choosing. Everything else - what was typed, which filter
  is on - is the page's own business and never crosses the bridge, so a keystroke costs nothing on the C# side.

  s1.call is SYNCHRONOUS and returns a string. An empty string means no handler or a handler that threw, and
  both are states this page has to survive: it draws the empty case rather than throwing on JSON.parse.

  NO SYMBOL GLYPHS ANYWHERE. Anything outside Latin-1 comes out as a box in the game's font, so a star is a
  picture, a cross is the letter X and an ellipsis is three full stops.

  TILES, NOT ROWS, because this replaces the game's own seed page and that page is a grid. A tile is square,
  sized so that five of them fill the sheet, and holds a picture; everything a row used to say - the parents,
  the tier, the effects, the numbers - is on the record on the facing sheet, which stays put instead of
  following the pointer.
*/

const $ = (id) => document.getElementById(id);

/* The grid. Five to a line and seven between them; the size of a tile is not a constant, because the sheet it
   has to fill is vanilla's card and only the mod can measure that - see tileSize. */
const PER_ROW = 5;
const TILE_GAP = 7;

/* What a tier group is set in from its section, so the tiles line up with the heading over them. */
const TIER_INDENT = 14;

/* The floor is the design's own 57. The ceiling is vanilla's tile, which this page has no business exceeding
   on the game's own card. */
const TILE_MIN = 57;
const TILE_MAX = 74;

/* Worked out once per render and written onto every tile and picture - see tileSize. */
let TILE = TILE_MIN;

/* The side padding of each page, from app.css. The mod sends the page widths; what is left after the padding
   is the only width this script can lay anything out against. */
const PAD_LEFT = 50;    // .page.left  - 20 + 30, the right one clearing the fold and the perforation
const PAD_RIGHT = 54;   // .page.right - 32 + 22, the left one clearing the other half of the crease

/* `.list`'s own right padding. The scroll bar is painted OVER the content rather than laid out beside it, so
   without this the bar sits on every section count and on the last column of every line. Anything laid out
   inside the list is divided out of the room that is left, not out of the whole sheet. */
const LIST_PAD = 10;

/* The header vial on the facing sheet and the gap after it, from `.rec-bud` / `.rec-top`. The pills stand in
   the name's column, so what is left beside the vial is the width they are chunked against. */
const BUD_W = 72;
const BUD_GAP = 16;

let view = {
  title: 'Select', tabs: [], rows: [], none: null, added: 'Added by mods', tags: [],
  w: 840, h: 620, pageW: 420, pageRW: 420, gap: 0,
};

let query = '';

/*
  Filters are independent toggles, not one choice out of a list, because that is what they were before this
  screen was rebuilt: a player who wants their favourites of one tier picks both. `tags` is what the effect
  chips in the dock write into.
*/
let f = { fav: false, hidden: false, fx: [], tags: [] };

/*
  ONE SORT, AND TIER IS NO LONGER ONE OF ITS MODES.

  Ordering the tier groups and ordering the tiles inside them are two different questions, and as one chip they
  could not both be answered: picking "tier down" gave up A-Z. So the tier order moved to its own control in
  the section head and these are what is left. Index 0 keeps the order the mod supplied, which is the only
  order that means anything to the mod that supplied it.
*/
const SORTS = ['Sort: default', 'Sort: A-Z', 'Sort: yield', 'Sort: growth', 'Sort: value'];
let sort = 0;

/* Tier groups inside a mod's section, and which way round they run. Both are the player's and are kept on the
   C# side - see remember(). */
let tierGroups = true;
let tierDesc = false;

/* Has the opening effect run. False for exactly one render - the one it starts from. */
let opened = false;

/* ---- the wire ------------------------------------------------------------------------------------------ */

function load() {
  const raw = s1.call('picker.view');
  if (!raw) return;
  try {
    view = JSON.parse(raw);
  } catch (err) {
    console.error('picker.view was not JSON: ' + err.message);
    return;
  }

  // Opened the way it was left. These are the player's settings, kept on the mod's side.
  sort = view.sort || 0;
  f.fav = !!view.onlyFav;
  tierGroups = view.group !== false;
  tierDesc = !!view.dir;

  // 5 and 6 were "tier up" and "tier down" while the sort chip still owned them. A stored one means the player
  // asked for a tier order, so it is carried across to the control that owns it now rather than dropped.
  if (sort === 5) { tierGroups = true; tierDesc = false; sort = 0; }
  else if (sort === 6) { tierGroups = true; tierDesc = true; sort = 0; }
  if (sort < 0 || sort >= SORTS.length) sort = 0;
}

/** Push the settings that outlive one open back to the mod. */
function remember() {
  // The third field is the discovered filter, which this page no longer has - the mod still stores it, so it
  // is sent back unchanged rather than silently turned off for anybody using the other picker.
  s1.call('picker.state',
    sort + '|' + (f.fav ? 1 : 0) + '|' + (view.onlyDisc ? 1 : 0)
    + '|' + (tierGroups ? 1 : 0) + '|' + (tierDesc ? 1 : 0));
}

/* ---- filtering and sorting ------------------------------------------------------------------------------ */

/** Whether this row carries an effect, whatever case either side spelled it in. */
function hasEffect(row, name) {
  const want = String(name || '').toLowerCase();
  return (row.effects || []).some((e) => String(e).toLowerCase() === want);
}

function visible() {
  const q = query.trim().toLowerCase();

  let rows = (view.rows || []).filter((row) => {
    // The current selection is always reachable. Filtering it away would leave the player unable to see what
    // the field is even set to.
    if (row.sel) return true;

    if (!f.hidden && row.hidden) return false;
    if (f.fav && !row.fav) return false;

    // AND, not OR. Ticking two effects asks for a seed that has both - which is the question a breeder is
    // actually holding when they tick the second one.
    //
    // Matched without regard to case, because the same effect arrives spelled two ways: the game's own rows
    // report "calming" and a mod that fills the field itself writes "Calming". One chip, either spelling.
    for (const fx of f.fx) if (!hasEffect(row, fx)) return false;
    for (const tag of f.tags) if ((row.tags || []).indexOf(tag) < 0) return false;

    if (!q) return true;
    return (
      (row.name || '').toLowerCase().indexOf(q) >= 0 ||
      (row.note || '').toLowerCase().indexOf(q) >= 0
    );
  });

  if (sort > 0) rows = rows.slice().sort(compare);
  return rows;
}

function byName(a, b) {
  const an = (a.name || '').toLowerCase();
  const bn = (b.name || '').toLowerCase();
  return an < bn ? -1 : an > bn ? 1 : 0;
}

/** Ties always fall back to the name, so a sort is stable to look at rather than merely stable in memory. */
function compare(a, b) {
  let r = 0;
  switch (sort) {
    case 1: r = byName(a, b); break;
    case 2: r = (b.yield || 0) - (a.yield || 0); break;          // biggest first
    case 3: r = (a.growth || 0) - (b.growth || 0); break;        // fastest first
    case 4: r = (b.value || 0) - (a.value || 0); break;          // most valuable first
  }
  return r !== 0 ? r : byName(a, b);
}

/** First letter up, and nothing else touched. */
function cap(text) {
  const s = String(text || '');
  return s ? s.charAt(0).toUpperCase() + s.slice(1) : s;
}

function el(kind, cls, text) {
  const node = document.createElement(kind);
  if (cls) node.className = cls;
  if (text !== undefined) node.textContent = text;
  return node;
}

function img(cls, src) {
  const node = document.createElement('img');
  node.className = cls;
  node.src = src;
  return node;
}

/* ---- the two pages --------------------------------------------------------------------------------------- */

/*
  The page is told how big it is, because it cannot ask.

  A surface answers layout coordinates and nothing about the viewport, so "how wide is half of me" has no answer
  here. The mod measured vanilla's own card in `SurfacePicker.Fit` and sent the numbers along, and this is where
  they land on the boxes. The two are equal by construction - the second sheet is the same sheet of paper - and
  that ceiling is applied on the C# side, where the measuring happens, so there is one place it can be got wrong.
*/
function shell() {
  const left = Math.round(view.pageW || 420);
  const right = Math.round(view.pageRW || left);

  $('pageL').style.width = left + 'px';
  $('pageR').style.width = right + 'px';
}

/** What is left of the top sheet after its own padding: the width everything on it is laid out against. */
function leftRoom() {
  return Math.round(view.pageW || 420) - PAD_LEFT;
}

/** What is left inside the scrolling list, which is everything the grid and its headings are laid out against. */
function listRoom() {
  return leftRoom() - LIST_PAD;
}

/** The same for the facing sheet. */
function rightRoom() {
  return Math.round(view.pageRW || view.pageW || 420) - PAD_RIGHT;
}

/** The width of the name's column on the facing sheet: the sheet, less the vial standing beside it. */
function recRoom() {
  return rightRoom() - BUD_W - BUD_GAP;
}

/*
  A TILE IS AS BIG AS THE SHEET ALLOWS, and the stylesheet cannot say how big that is.

  Five 57px tiles and four gaps come to 313 in a 370px sheet, so the design's own number left sixty pixels of
  bare paper down the right-hand side of every line - the grid hanging left of a rule that ran to the fold. The
  size is therefore divided out of the room instead, off the width the mod measured on vanilla's card.

  MEASURED AGAINST THE NARROWEST LINE, which is a tier group: it is set in 14px and its tiles have to be the
  same size as everything above it, or a group reads as a different grid rather than part of one.
*/
function tileSize() {
  const room = listRoom() - TIER_INDENT;
  const size = Math.floor((room - TILE_GAP * (PER_ROW - 1)) / PER_ROW);
  return Math.max(TILE_MIN, Math.min(TILE_MAX, size));
}

/* ---- the filter chips ------------------------------------------------------------------------------------ */

/*
  CHIP WIDTHS ARE ESTIMATED, NOT MEASURED, and that is the only option here. There is no flex-wrap in this
  engine, so a row of chips either runs off the sheet or gets chunked in script - and chunking needs to know how
  wide each chip will be BEFORE it is laid out. `el.rect()` reports the last render, which is a frame too late.
  Six per em plus the padding matches the sheet's font closely enough that a row breaks one chip early at
  worst, and one chip early is invisible while one chip late is a word sliced in half at the edge of the board.
*/
const CHIP_PAD = 28;

function chipWidth(label) {
  return Math.round(String(label).length * 6.4) + CHIP_PAD;
}

function chip(row, label, on, act) {
  const button = el('button', 'chip' + (on ? ' on' : ''), label);
  button.addEventListener('click', () => { act(); render(); });
  row.push({ node: button, width: chipWidth(label) });
  return button;
}

function renderChips() {
  const box = $('chips');
  box.replaceChildren();

  const wanted = [];

  chip(wanted, 'Favorites', f.fav, () => { f.fav = !f.fav; remember(); });

  /*
    THERE IS NO VANILLA/BRED CHIP, AND THAT IS THE POINT OF THE SECTIONS.

    It used to cycle all -> vanilla -> bred, which is the split the grid already draws: the game's own seeds
    stand under "Vanilla seeds" and every mod has a heading of its own. A chip that hides one of two headings
    answers a question the headings answer better, and it cost the row the width that pushed Clear onto a
    second line.
  */

  // What the dock has ticked, said on the sheet the player is looking at. Clicking it clears them, which is the
  // only thing this chip could usefully do that the dock does not already do better.
  //
  // ALWAYS DRAWN, EVEN AT ZERO. Appearing only once something was ticked made the chip row change width the
  // moment a filter went on, which moved `Favorites` under the pointer - and the row is where a player looks to
  // find out whether a filter IS on, so the one state worth showing is the one that was being hidden.
  const ticked = f.fx.length + f.tags.length;
  chip(wanted, ticked ? 'Effects ' + ticked : 'Effects', ticked > 0,
       () => { f.fx.length = 0; f.tags.length = 0; });

  if (view.hiddenCount || f.hidden) {
    chip(wanted, 'Hidden (' + (view.hiddenCount || 0) + ')', f.hidden, () => { f.hidden = !f.hidden; });
  }

  // Only when something is actually on. A button that does nothing is worse than no button.
  if (f.fav || ticked || query || sort !== 0) {
    chip(wanted, 'Clear', false, () => {
      // Emptied in place rather than replaced: the dock hands each chip the array it writes into, and a fresh
      // array would leave the chips of this render pointing at the old one.
      f.fav = false; f.fx.length = 0; f.tags.length = 0;
      sort = 0;
      query = '';
      $('find').value = '';
      // A keystroke waiting for its render would otherwise land after this one and type the query back in.
      findWant = null;
      remember();
    });
  }

  const room = leftRoom();
  let row = null;
  let used = 0;
  for (const one of wanted) {
    if (!row || used + one.width > room) {
      row = el('div', 'chip-row');
      box.appendChild(row);
      used = 0;
    }
    row.appendChild(one.node);
    used += one.width + 6;   // the row's gap
  }
}

/* ---- the dock: effect filters ------------------------------------------------------------------------------ */

/** The tag groups a mod declared, as `prefix -> [tag]`, so `mod:effect/calming` becomes the "effect" list. */
function groups() {
  const out = new Map();

  for (const t of view.tags || []) {
    const cut = t.id.indexOf('/');
    const colon = t.id.indexOf(':');
    if (cut < 0 || colon < 0 || cut < colon) continue;

    const name = t.id.substring(colon + 1, cut);
    if (!out.has(name)) out.set(name, []);
    out.get(name).push(t);
  }

  return out;
}

/*
  WHAT THE CHIPS ARE, AND WHY THEY ARE NOT ONLY THE MOD'S TAGS.

  The drop-down this replaces was built from `view.tags` - the tags a MOD declared - so on a save with no
  strain catalogue installed the whole filter had nothing in it and hid itself. That is a filter that only
  exists for people who already have another mod, and the effects are the game's own data: every row already
  carries `effects`, vanilla seeds included.

  So the list is the union of the effects the rows actually have, and then any other group a mod declared
  after it. Nothing is invented and nothing is asked for over the bridge - both halves are already on the row.
*/
function filterDefs() {
  const out = [];

  // ONE CHIP PER EFFECT, NOT ONE PER SPELLING. The game's own rows report "calming" and a mod that fills the
  // field itself writes "Calming", so a case-sensitive list put both in the dock - the same filter twice, side
  // by side, each one ticking half the catalogue.
  const names = [];
  for (const row of view.rows || []) {
    for (const e of row.effects || []) {
      if (!names.some((n) => n.toLowerCase() === String(e).toLowerCase())) names.push(e);
    }
  }
  names.sort((a, b) => a.toLowerCase() < b.toLowerCase() ? -1 : 1);
  // Capitalised for the chip only. The game's own effect names arrive lowercase and a mod's tag labels do not,
  // so a dock built straight off the data read "calming, energizing, Refreshing, Sedating" - one filter, two
  // spellings. The value that is filtered on is untouched.
  for (const name of names) out.push({ key: 'fx:' + name, label: cap(name), list: f.fx, value: name });

  // Whatever else a mod files - a "flavour" group, a "source" group - gets its chip under the effects rather
  // than being dropped for not being the group this sheet was drawn for.
  for (const [name, list] of groups()) {
    if (name === 'tier') continue;   // tier is what the section head does, and having it twice is a filter fighting itself
    for (const t of list) {
      if (out.some((x) => x.key === 'tag:' + t.id)) continue;

      // A mod that tags its own strains "Calming" while the game already reports the effect "calming" was
      // getting TWO chips for one question, and ticking either of them answered it. The mod's spelling is the
      // nicer one, so it wins the label; the filter stays on the effect, which is the half that also works for
      // a vanilla seed.
      const same = out.find((x) => x.label.toLowerCase() === String(t.label).toLowerCase());
      if (same) { same.label = cap(t.label); continue; }

      out.push({ key: 'tag:' + t.id, label: t.label, list: f.tags, value: t.id });
    }
  }

  return out;
}

function dchipWidth(label) {
  return Math.round(String(label).length * 6.2) + 25;
}

function renderDock() {
  const box = $('fx');
  box.replaceChildren();

  const defs = filterDefs();
  if (defs.length === 0) { box.style.display = 'none'; return; }
  box.style.display = 'flex';

  const on = f.fx.length + f.tags.length;

  const head = el('div', 'dock-head');
  head.appendChild(el('div', 'dock-name', 'Filter by effect'));
  head.appendChild(el('div', 'dock-fill'));
  head.appendChild(el('div', 'dock-count', on ? on + ' on' : 'none on'));
  box.appendChild(head);

  const rows = el('div', 'dock-rows');
  box.appendChild(rows);

  // There is no flex-wrap in this engine, so the rows are made rather than found.
  const room = rightRoom();
  let line = null;
  let used = 0;
  for (const def of defs) {
    const lit = def.list.indexOf(def.value) >= 0;
    const node = el('button', 'dchip' + (lit ? ' on' : ''), def.label);
    node.addEventListener('click', () => {
      const at = def.list.indexOf(def.value);
      if (at >= 0) def.list.splice(at, 1); else def.list.push(def.value);
      render();
    });

    const w = dchipWidth(def.label);
    if (!line || used + w > room) {
      line = el('div', 'dock-row');
      rows.appendChild(line);
      used = 0;
    }
    line.appendChild(node);
    used += w + 5;
  }
}

/* ---- the record on the facing sheet ------------------------------------------------------------------------ */

/*
  Which row the record is showing. Not bookkeeping - it is what stops the page rebuilding forever.

  Every DOM write rebuilds the page, and a rebuild destroys and recreates every box on it. The pointer has not
  moved, but the box under it is a NEW object, so uGUI raises its enter again, which fills the record, which
  writes to the DOM, which rebuilds. A probe listener counted about ten of those rounds a second with the
  pointer standing still.

  It is not only wasted work. uGUI raises a click only when the press and the release land on the SAME object,
  and while this runs the tile a player pressed is destroyed several times over before they let go - so no click
  is raised at all, nothing is picked, and nothing is logged either, since there was no click event to log. That
  is "I click a seed and nothing happens", and it needs nothing in the way to happen.

  So a request for the row that is already showing is answered with nothing at all.
*/
let shown = null;

/*
  WHICH TILE, not which seed.

  A starred seed is drawn TWICE - once under Favourites and once in its own tier group - and both tiles carry
  the same row object with the same id. Keyed by the row, "is this the hovered one" was true in both places at
  once, so pointing at the favourite lit its label AND the label of the copy further down the page.

  So a tile is numbered as it is built, in render order, and the number is what the hover is remembered by. It
  is stable across a rebuild - the same list builds the same tiles in the same order - which is what keeps the
  loop below closed: the tile the pointer is on comes back with the number it had.
*/
let shownAt = 0;

/** Handed to every tile as it is built - see `shownAt`. Reset per render, so the numbers are the positions. */
let slots = 0;

/*
  WHERE EACH TILE'S LABEL IS, BY SLOT: the node itself and the two numbers that place it.

  A HOVER MUST NOT REBUILD THE CATALOGUE. Two things change when the pointer moves to another tile - the record
  on the facing sheet and which line's label carries a word - and both are writes to nodes that already exist.
  Reaching them through `render()` meant `renderRows` opened with `replaceChildren`, so every tile on the page
  was thrown away and made again: a tile is five nodes, a starred seed is drawn twice, and several hundred seeds
  therefore cost a few thousand nodes rebuilt per tile crossed. This is the handle that makes the write
  targeted. Filled as the grid is built, thrown away with the grid.
*/
let tipAt = [];

/* Which slot's label carries the `up` class right now - on screen, or scaled away by hideTip and still up. Not
   the same question as `shownAt`: leaving the grid takes the slip off the screen and leaves the class where it
   was, and something has to know which node to put back. Reset with the grid, which hands out fresh nodes. */
let slipAt = 0;

/** The Any tile has no row of its own, so it points at this. Compared by identity, never by id. */
const ANY = { id: ' any', name: 'Any' };

/** A class write that is skipped when it would change nothing: every write to the document rebuilds the page,
    so a write that says what the node already says costs a whole rebuild for no change at all. */
function setClass(node, name) {
  if (node && node.className !== name) node.className = name;
}

/*
  THE POINTER OUTRUNS THE PAGE, AND IT IS ALLOWED TO.

  A write to the document does not repaint a box here - it rebuilds the whole page on the mod's side, every
  GameObject destroyed and made again. Measured on a catalogue of five hundred seeds: about a second, and the
  pointer crosses five tiles in that second. Answering each one in turn meant five seconds of frozen screen for
  a flick of the wrist, and the four records in the middle were never on screen long enough to be read.

  So the pointer's position is remembered and the sheet catches up once, a fraction of a second later. A sweep
  across the grid costs ONE rebuild instead of one per tile, and the only thing given up is that the record
  arrives a tenth of a second after the pointer does - which is what a tooltip does everywhere else.
*/
const HOVER_MS = 90;
let hoverTimer = 0;
let hoverWant = null;

function showRecord(row, slot) {
  const at = slot || 0;
  const same = (shown === row || (shown && row && shown.id === row.id)) && shownAt === at;
  if (same) {
    // A leave is raised before the enter that follows it, so crossing from the picture onto the star of the same
    // tile queues a "gone" and then arrives back at the tile it never really left. Take the queued one back.
    if (hoverWant && hoverWant.gone) hoverWant = null;
    return;
  }

  // The LAST tile the pointer was on wins. Nothing is written here at all - see hoverCatchUp.
  hoverWant = { row: row, slot: at, gone: false };
  if (!hoverTimer) hoverTimer = setTimeout(hoverCatchUp, HOVER_MS);
}

/*
  THE POINTER LEFT A TILE WITHOUT ARRIVING AT ANOTHER ONE.

  The slip belongs to the pointer and goes with it; the record on the facing sheet stays, because that sheet is
  the memory of what was last pointed at and saying the same name twice on one screen is what made the slip look
  stuck.

  Queued through the same timer as an enter, not done on the spot: uGUI raises the leave of the old tile and the
  enter of the new one in the same frame, so a hide that wrote at once would cost one paint on the way out and
  another on the way in for every tile crossed. Both land in `hoverWant` and only the last one is paid for.

  NO LEAVE ARRIVES FOR A DESTROYED TILE (Sideload/Input/Interaction.cs:117-119), which is what makes this safe
  against the rebuild every write causes: the tile the pointer is on is destroyed and remade constantly, and not
  one of those raises a spurious leave.
*/
function leaveRecord(slot) {
  const at = slot || 0;
  if (hoverWant) { if (hoverWant.slot !== at) return; }
  else if (shownAt !== at) return;

  hoverWant = { row: shown, slot: at, gone: true };
  if (!hoverTimer) hoverTimer = setTimeout(hoverCatchUp, HOVER_MS);
}

function hoverCatchUp() {
  hoverTimer = 0;

  const want = hoverWant;
  hoverWant = null;
  if (!want) return;

  if (want.gone) {
    // The record is left exactly as it is - see leaveRecord.
    hideTip(shownAt);
    shownAt = 0;
    return;
  }

  if (shown === want.row && shownAt === want.slot) return;

  // Coming back to the tile that is already on the sheet - off the picture onto the star and back, or onto the
  // paper and back - leaves the record saying what it already says. Rebuilding it to write the same words is a
  // whole page rebuilt for nothing.
  const changed = shown !== want.row;
  shown = want.row;
  shownAt = want.slot;
  hoverPaint(changed);
}

/*
  THE HOVER'S OWN PAINT, AND IT NEVER TOUCHES THE LIST.

  The label the pointer left goes back to its resting class, the label it arrived at is filled, and the facing
  sheet is redrawn. The grid, the chip row, the dock and the head are left exactly as they are, because nothing
  on any of them says which tile the pointer is on.

  One rebuild still happens on the mod's side - a label that goes from `display: none` to a slip is a layout
  change and there is no cheaper answer to that. What this removes is the SECOND cost, which was building the
  whole catalogue again in script to reach two nodes.
*/
function hoverPaint(record) {
  // `slipAt`, not the tile just left: the pointer may have gone off the grid in between, and that takes the slip
  // off the screen without giving its node the resting class back - see hideTip.
  if (slipAt && slipAt !== shownAt) {
    const before = tipAt[slipAt];
    if (before) setClass(before.node, 'tip');
    slipAt = 0;
  }

  const now = tipAt[shownAt];
  if (now && view.tips !== false && shown && shown !== ANY) {
    fillTip(now.node, shown, now.col, now.indent);
    slipAt = shownAt;
  }

  if (record) renderSheet();
}

/** One "LABEL value" line, and only when there is a value. A record that promises a field and shows nothing
    under it is worse than a shorter record. */
function fact(box, key, value) {
  if (value === null || value === undefined || value === '') return;
  const line = el('div', 'fact');
  line.appendChild(el('span', 'fact-k', key));
  line.appendChild(el('span', 'fact-v', String(value)));
  box.appendChild(line);
}

function money(n) {
  const v = Number(n) || 0;
  if (v <= 0) return '';
  return '$' + (Math.round(v * 100) / 100);
}

function hours(n) {
  const v = Number(n) || 0;
  // "12 h", with the space: the number is written in the hand and the unit is printed, so they are two marks
  // rather than one word.
  return v > 0 && v < 2000000000 ? v + ' h' : '';
}

/** "Tier 4 - Breed to Seed Strains", or whichever half of it exists. */
function subLine(row) {
  const bits = [];
  if (row.tier) bits.push('Tier ' + row.tier);

  // The category the registering mod NAMED, not its assembly id: `source` is `doodesch.breedtoseed`, and
  // "Breed to Seed Strains" is what that mod calls its own shelf on this very page.
  const from = row.vanilla ? '' : tabLabel(row.tab || '');
  if (from) bits.push(from);
  else if (row.source) bits.push(String(row.source));

  return bits.join(' - ');
}

/** One line of facts for the resting state - what the field holds, without the full record. */
function restLine(row) {
  const bits = [];
  if (row.tier) bits.push('Tier ' + row.tier);
  const n = (row.effects || []).length;
  if (n) bits.push(n + (n === 1 ? ' effect' : ' effects'));
  if (row.yield) bits.push(row.yield + (row.harvest ? ' ' + row.harvest : ''));
  const v = money(row.value);
  if (v) bits.push(v);
  return bits.join(' - ');
}

/*
  THE INITIALS OF A NAME, for a seed whose picture has not arrived.

  Two letters, because one is not a name and three do not fit a tile. Words first - "Purple Kush" is PK - and
  the first two letters of a single word otherwise. Upper case: this stands in for a picture, not for the name
  written out, and the name is on the facing sheet anyway.
*/
function initials(name) {
  const words = String(name || '').trim().split(/\s+/).filter((w) => w);
  if (words.length === 0) return '?';
  if (words.length === 1) return words[0].substring(0, 2).toUpperCase();
  return (words[0].charAt(0) + words[1].charAt(0)).toUpperCase();
}

/** The vial, or the frame with the letter in it when there is no item to show a picture of. Smaller in the
    resting card, where it shares a line instead of heading a record. */
function budNode(row, any, rest) {
  if (any) return el('div', 'rec-any', 'X');
  const small = rest ? ' small' : '';
  if (row.icon === false) return el('div', 'rec-mark' + small, initials(row.name || row.id));
  return img('rec-bud' + small, 's1://icon/' + row.id);
}

/** The head block every state shares: picture, name, one line under it. In the resting card the name is
    written smaller, because it shares the block with the line that says which field it belongs to.

    ANSWERS THE NAME'S COLUMN, not the block: the effect pills are written under the name and have to start
    where it does, which they cannot do from outside it. */
function recTop(box, row, any, sub, rest) {
  const top = el('div', 'rec-top' + (rest ? ' spaced' : ''));
  top.appendChild(budNode(row, any, rest));

  const id = el('div', 'rec-id');
  const name = 'rec-name' + (rest ? ' small' : '') + (any ? ' dim' : '');
  id.appendChild(el('div', name, any ? (row && row.name) || 'Any' : (row.name || row.id)));
  if (sub) id.appendChild(el('div', 'rec-sub', sub));
  top.appendChild(id);

  // ONLY IN THE RESTING CARD. On the record it hung in the far top corner of the sheet, a gold mark with
  // nothing beside it - the grid already stars the tile the pointer is on, and saying it twice on one screen
  // reads as a stray. In the resting card it belongs: that block is the state of the field, and whether the
  // field's own seed is a favourite is part of it.
  if (rest && !any && row.fav) top.appendChild(img('rec-star', 'star.png'));

  box.appendChild(top);
  return id;
}

/** The two parents of a cross, as the mod wrote them: "A x B". A vanilla seed has no note and gets no cells. */
function crossNode(box, note) {
  if (!note) return;

  const parts = String(note).split(/\s+[xX]\s+/);
  if (parts.length !== 2) { fact(box, 'CROSS', note); return; }

  const row = el('div', 'cross');

  const a = el('div', 'parent');
  a.appendChild(el('div', 'parent-k', 'PARENT'));
  a.appendChild(el('div', 'parent-v', parts[0]));
  row.appendChild(a);

  row.appendChild(el('div', 'cross-x', 'x'));

  const b = el('div', 'parent');
  b.appendChild(el('div', 'parent-k', 'PARENT'));
  b.appendChild(el('div', 'parent-v', parts[1]));
  row.appendChild(b);

  box.appendChild(row);
}

/** BUDS / TIME / VALUE / TIER, in four equal cells. A cell with no number says so with a dash rather than
    disappearing - four cells that move about are harder to read than one that is empty. */
function cellsNode(box, row) {
  const cells = el('div', 'cells');

  const four = [
    ['BUDS', row.yield ? String(row.yield) : '-'],
    ['TIME', hours(row.growth) || '-'],
    ['VALUE', money(row.value) || '-'],
    ['TIER', row.tier ? String(row.tier) : '-'],
  ];

  for (const [k, v] of four) {
    const cell = el('div', 'cell');
    cell.appendChild(el('div', 'cell-k', k));
    cell.appendChild(el('div', 'cell-v', v));
    cells.appendChild(cell);
  }

  box.appendChild(cells);
}

function renderSheet() {
  const box = $('sheet');
  box.replaceChildren();

  // ---- hovered: the full record of whatever the pointer is on
  if (shown && shown !== ANY) {
    const row = shown;
    const id = recTop(box, row, false, subLine(row));

    // Chunked in script like every other row of things on this page: there is no flex-wrap here, and a strain
    // with eight effects would otherwise run off the sheet and be clipped without a mark to say so. Chunked
    // against the NAME'S column rather than the sheet, because that is where they stand.
    if (row.effects && row.effects.length) {
      const room = recRoom();
      let line = null;
      let used = 0;
      for (const e of row.effects) {
        const w = dchipWidth(e);
        if (!line || used + w > room) {
          line = el('div', 'pill-row');
          id.appendChild(line);
          used = 0;
        }
        line.appendChild(el('span', 'pill', cap(e)));
        used += w + 6;
      }
    }

    box.appendChild(img('rec-rule', 'rule.png'));

    crossNode(box, row.note);
    cellsNode(box, row);

    // The rest of what is known, under the numbers rather than instead of them. Same rule as ever: no value,
    // no row - and whatever the registering mod answered goes last, so it cannot push the game's own facts off
    // the top of the sheet.
    fact(box, 'PRODUCT', row.product);
    fact(box, 'TYPE', row.drug);
    fact(box, 'BUY', money(row.buy));
    if (row.disc === false) fact(box, 'DISCOVERED', 'not yet');
    for (const extra of row.facts || []) fact(box, extra.k, extra.v);
    return;
  }

  // ---- resting, and the Any state, which is the same block with a dashed frame instead of a picture
  //
  // Resting says what the FIELD holds, not what the pointer last touched: with nothing hovered the useful
  // question is "what is this pot set to", and the answer was previously nowhere on the screen.
  const sel = selected();
  const isAny = shown === ANY || !sel;
  const row = isAny ? { name: (view.none && view.none.name) || 'Any' } : sel;

  // Boxed, so it reads as the state of the field rather than as the record of a seed. The frame is empty when
  // nothing is planted, the same way the Any tile is.
  const card = el('div', 'rec-card' + (isAny ? ' any' : ''));
  card.appendChild(el('div', 'rec-head', ownerLine()));
  recTop(card, row, isAny, isAny ? '' : restLine(row), true);
  box.appendChild(card);

  box.appendChild(el('div', 'rec-gap', 'Point at a seed to read it here.'));
}

/*
  "POT 3 IS SET TO", not "SEED IS SET TO".

  The field's own label says what KIND of thing is being chosen, which the player can already see - they are
  looking at a page of seeds. What is worth printing is which of the four pots on the clipboard this page is
  about, and that is the name the station carries in its own configuration, sent as `owner`. A field with no
  owner - several pots selected at once, or a screen that has none - falls back to the label.
*/
function ownerLine() {
  return String(view.owner || view.title || 'This field').toUpperCase() + ' IS SET TO';
}

/** The row the field is set to, or null. Off the whole list, not the filtered one: what a field holds does not
    stop being true because a filter is on. */
function selected() {
  for (const row of view.rows || []) if (row.sel) return row;
  return null;
}

/* ---- the grid -------------------------------------------------------------------------------------------- */

/*
  A click that lands in the list but on nothing says WHAT it landed on.

  The report this picker gets is "I click a seed and nothing happens", and from the outside that has two
  completely different causes: the pointer reached the page and hit the wrong node, or it never reached the page
  at all. Nothing in a screenshot separates them, and neither does the pick handler - it is not called in either
  case. So the list itself listens, and SILENCE now means the pointer never arrived.
*/
$('rows').addEventListener('click', (e) => {
  const on = e && e.target ? String(e.target.className || '') : '';
  // The controls that live INSIDE the list are not strays: the section heads sit in the scroll area with the
  // tiles, so a knob press bubbles to here and was being reported as a click that hit nothing.
  if (on.indexOf('shot-item') >= 0 || on.indexOf('shot-any') >= 0 || on.indexOf('star') >= 0
      || on.indexOf('hide') >= 0 || on.indexOf('knob') >= 0) return;
  s1.call('picker.stray', on || '(unnamed)');
});

/*
  THE HOVER LABEL.

  WIDTH IS THE CONTENT'S NOW, AND CAPPED. A fixed 148 was wide enough for a long name and far too wide for a
  short one, and on this pad there is a hard edge to respect: the label must never reach the fold. So it is
  estimated from the text - there is nothing to measure before a render - and then clamped to the room that is
  actually left on the side it stands on.

  WHICH SIDE IS DECIDED BY THE COLUMN, not by measuring. The old code took `anchor.rect()` minus `line.rect()`,
  because the difference between two rects inside the same scrolled box cancels the scroll offset a surface
  cannot read. Inside the line there is nothing left to measure at all: the column number says where the tile
  starts, and whether there is room to its right. On the last two columns it flips to the left.

  AT FIVE COLUMNS IT COVERS THE TWO TILES BESIDE IT, stars and all. That is accepted rather than worked around:
  the label answers "which one is this" for the tile under the pointer, and the record on the facing sheet
  carries everything else about it.
*/
const TIP_GAP = 6;
const TIP_MIN = 74;

function tipX(col) {
  return col * (TILE + TILE_GAP);
}

/** How wide the label may be at this column, on the side it will stand. A tier group's line starts 14 in, so
    that much less paper is left beside it. */
function tipRoom(col, flip, indent) {
  const room = listRoom() - (indent ? TIER_INDENT : 0);
  return flip ? tipX(col) - TIP_GAP : room - (tipX(col) + TILE + TIP_GAP);
}

/** Estimated, because nothing can be measured before it is drawn: the hand at 18px runs a little over eight
    pixels a letter, the printed tier about 5.6 - and they stand side by side with a 7px gap between them.

    ROUNDED UP, NOT DOWN. At 7.6 the estimate was a hair under the truth and "Granddaddy Purple Seed" came out
    as "Granddaddy Purple..." on a label with room to spare: too wide only costs paper, too narrow costs the
    word the label exists to say. */
function tipWidth(name, tier) {
  const a = String(name || '').length * 8.3;
  const b = tier ? String(tier).length * 5.6 + 7 : 0;
  return Math.round(a + b) + 22;
}

/*
  EVERY LINE GETS ONE, HOVERED OR NOT.

  A hover never adds a node and never removes one. That is the rule #69 turned into: the old bubble was CREATED
  on `mouseenter`, which is a structural change, which rebuilt the page, which destroyed and recreated every
  tile, which raised `mouseenter` again for the NEW object under a pointer that had not moved. About ten rounds
  a second with the mouse standing still - and, far worse than the wasted work, uGUI raises a click only when
  the press and the release land on the SAME object, so the tile a player pressed was destroyed several times
  before they let go and no click was ever raised. Nothing appeared in the log either, because there was no
  click to log.

  An empty label is absolutely placed and fully transparent, so it costs no room and draws nothing.

  LAST IN THE LINE, AND THAT IS NOT TIDINESS. There is no z-index in this engine and paint order is document
  order, so a label written before the tiles is painted UNDERNEATH them: the only part of it that showed was
  the seven-pixel gap between two tiles, which reads exactly like a label drawn far too narrow. It is appended
  once the line's tiles are in, which is still a build-time decision and never a hover-time one.

  THE ARROW IS LAST INSIDE THE LABEL, for the same reason: it is the same ink as the body and the half that
  overlaps it has to be painted over it.
*/
function tipNode() {
  const tip = el('div', 'tip');

  // AS TALL AS THE TILE IT POINTS AT, which is a number the stylesheet does not have - see tileSize. The slip
  // inside is centred in that height, so the tile's middle, the slip's middle and the arrow are ONE line
  // instead of three. Sitting at the top of the line with a fixed offset, the arrow came out level with the
  // tile's centre but near the BOTTOM edge of a slip that is only thirty pixels tall.
  tip.style.height = TILE + 'px';

  const body = el('div', 'tip-body');
  body.appendChild(el('div', 'tip-name', ''));
  body.appendChild(el('div', 'tip-tier', ''));
  tip.appendChild(body);

  // Half the tile, less half the arrow: the middle of the tile, the middle of the slip, the same number.
  const arrow = el('div', 'tip-arrow');
  arrow.style.top = Math.round(TILE / 2 - 4) + 'px';
  tip.appendChild(arrow);
  return tip;
}

/*
  TEXT AND OFFSETS ON NODES THAT ARE ALREADY THERE. Nothing is created and nothing is removed.

  ONE CLASS WRITE, NOT TWO, AND THERE IS NO FADE.

  The label used to go up in two steps - `up` to give it a box, then `on` a tick later to take it from
  transparent to opaque, so that the declared `transition: opacity` had a frame to run from. It never ran. A
  class change is not a repaint on this surface: it rebuilds the page, the transition runner is cleared at the
  top of every rebuild and only ever driven from the repaint path a `:hover` rule takes. So the second write
  bought no animation at all and cost a second full rebuild of the page - at five hundred seeds, another
  second of frozen screen for every tile the pointer touched.
*/
function fillTip(node, row, col, indent) {
  if (!node) return;

  const name = row.name || row.id;
  // Beside the name rather than under it, so the label is a slip with a word on it: "Purple Kush  T1".
  const tier = row.tier ? 'T' + row.tier : '';

  const body = node.children[0];
  body.children[0].textContent = name;
  body.children[1].textContent = tier;

  const flip = col >= PER_ROW - 2;
  const width = Math.max(TIP_MIN, Math.min(tipWidth(name, tier), tipRoom(col, flip, indent)));

  node.style.width = width + 'px';
  node.style.left = (flip ? tipX(col) - width - TIP_GAP : tipX(col) + TILE + TIP_GAP) + 'px';

  // `up` is what makes the label exist at all - a class write on a node that is already there, see tipNode.
  // The transform is what a previous hide left behind - see hideTip.
  node.style.transform = '';
  setClass(node, 'tip up' + (flip ? ' flip' : ''));
}

/*
  TAKE THE SLIP AWAY WITHOUT REBUILDING THE PAGE.

  `display` is the obvious answer and the wrong one here: it is layout, so a class write costs the whole page
  destroyed and made again - a second of it on a catalogue of several hundred, spent on a label going away.

  A transform is the one write this engine answers with a repaint of the single box that was written
  (Sideload/Css/PaintOnlyProperties.cs), which is what the sheet's opening fold is animated with. Scaled to
  nothing the slip has no width and draws nothing, and `fillTip` clears it again on the way back up.
*/
function hideTip(slot) {
  const at = tipAt[slot];
  if (at && at.node) at.node.style.transform = 'scaleX(0)';
}

/** Square, at whatever size the sheet allowed this render. The picture is inset by the same six pixels the
    design has at 57. */
function sizeTile(tile, shot, picture) {
  tile.style.width = TILE + 'px';
  tile.style.height = TILE + 'px';
  if (shot) shot.style.height = (TILE - 2) + 'px';
  if (picture) {
    picture.style.width = (TILE - 6) + 'px';
    picture.style.height = (TILE - 6) + 'px';
  }
}

/** Keeps the place of a tile on a short last line. */
function holeNode() {
  const hole = el('div', 'tile hole');
  sizeTile(hole, null, null);
  return hole;
}

/** One tile: the seed's own picture and its star. Pointing at it fills the record on the facing sheet and the
    label on its own line. */
function tileNode(row, slot) {
  const tile = el('div', 'tile' + (row.sel ? ' sel' : ''));

  // A button, not a div: the engine wires a hit target unconditionally for button, a, input and textarea.
  //
  // `shot-item` carries no style. It exists so a test can aim at a seed: the Any tile is also a `.shot` and is
  // drawn FIRST, so `sideload_click .shot` clears the field instead of choosing anything - which reads exactly
  // like the click being broken, and cost a whole diagnosis round.
  const pick = el('button', 'shot shot-item');
  // Supplied by the mod at open time, off the LIVE item definition - which is what lets a mod that tints a seed
  // per strain have every vial on the grid look like itself. `icon` is false when the store has none, and then
  // the pen writes the initials rather than the tile drawing an empty box - see `.shot-mark`.
  const picture = row.icon === false ? null : img('shot-img', 's1://icon/' + row.id);
  pick.appendChild(picture || el('span', 'shot-mark', initials(row.name || row.id)));
  sizeTile(tile, pick, picture);
  pick.addEventListener('click', () => s1.call('picker.pick', row.id));
  // The TILE, not the seed: a favourite is on the page twice and only one of them is under the pointer.
  pick.addEventListener('mouseenter', () => showRecord(row, slot));
  // The slip goes when the pointer does - see leaveRecord.
  pick.addEventListener('mouseleave', () => leaveRecord(slot));
  tile.appendChild(pick);

  const star = el('button', 'star' + (row.fav ? ' on' : ''));
  star.appendChild(img('star-img', 'star.png'));
  // The star stands ON the tile, so drifting five pixels onto it has not left the seed - it says so itself
  // rather than letting the picture's own leave take the slip away.
  star.addEventListener('mouseenter', () => showRecord(row, slot));
  star.addEventListener('mouseleave', () => leaveRecord(slot));
  star.addEventListener('click', () => {
    row.fav = s1.call('picker.fav', row.id) === 'on';
    render();
  });
  tile.appendChild(star);

  // Only while the Hidden chip is on. A tile is fifty-seven pixels wide and cannot carry two permanent buttons,
  // and hiding things is a tidying-up job rather than something done in passing.
  if (f.hidden) {
    const hide = el('button', 'hide' + (row.hidden ? ' on' : ''), row.hidden ? 'o' : 'x');
    hide.addEventListener('click', () => {
      row.hidden = s1.call('picker.hide', row.id) === 'on';
      view.hiddenCount = (view.hiddenCount || 0) + (row.hidden ? 1 : -1);
      render();
    });
    tile.appendChild(hide);
  }

  return tile;
}

/*
  A grid of tiles, five to a line. There is no wrapping here, so the lines are made rather than found.

  The FIRST grid drawn takes the Any tile as its first box, which is why the column is counted rather than
  taken from the index: past that tile every seed on the line sits one column further right, and the column is
  what places the hover label.
*/
function grid(box, rows, indent) {
  let line = null;
  let tip = null;
  let col = 0;

  // The label goes in once the line's tiles are - see tipNode for why it cannot go in first.
  const close = () => { if (line && tip) line.appendChild(tip); };

  const open = () => {
    close();
    line = el('div', indent ? 'line in' : 'line');
    tip = tipNode();
    box.appendChild(line);
    col = 0;
  };

  const lead = takeLead();
  if (lead) { open(); line.appendChild(lead); col = 1; }

  for (const row of rows) {
    if (!line || col === PER_ROW) open();

    // Numbered as it is built - see `shownAt`. Never from the index: the same seed is drawn in Favourites and
    // again in its tier group, and those two tiles have to be able to answer differently.
    const slot = ++slots;
    line.appendChild(tileNode(row, slot));

    // Where this tile's label is and how it is placed, so a hover can fill it without the grid being rebuilt to
    // find it - see `tipAt`.
    tipAt[slot] = { node: tip, col: col, indent: indent };

    // The line's own label, filled only for the TILE the pointer is on.
    if (view.tips !== false && shown && shown !== ANY && shown.id === row.id && shownAt === slot) {
      fillTip(tip, row, col, indent);
      slipAt = slot;
    }
    col++;
  }

  // The last line is short, and a stretched tile would be a different size from the rest of the grid.
  if (line) {
    while (col < PER_ROW) { line.appendChild(holeNode()); col++; }
    close();
  }
}

/* ---- the sections ---------------------------------------------------------------------------------------- */

/** The label a mod's own category carries, or the one the mod sent for "everything added". */
function tabLabel(key) {
  for (const tab of view.tabs || []) if (tab.id === key) return tab.label;
  return view.added || 'Added by mods';
}

/** The tier control cycles all three states, so both values it writes have a control that can reach them. */
function tierKnob(head) {
  const label = !tierGroups ? 'No tiers' : tierDesc ? 'Tier down' : 'Tier up';
  const knob = el('button', 'knob' + (tierGroups ? ' on' : ''), label);
  knob.addEventListener('click', () => {
    if (!tierGroups) { tierGroups = true; tierDesc = false; }
    else if (!tierDesc) { tierDesc = true; }
    else { tierGroups = false; tierDesc = false; }
    remember();
    render();
  });
  head.appendChild(knob);
}

/** Orders the tiles inside every group. Independent of the tier control on purpose - both can be set at once. */
function sortKnob(head) {
  const knob = el('button', 'knob' + (sort !== 0 ? ' on' : ''), SORTS[sort] || SORTS[0]);
  knob.addEventListener('click', () => {
    sort = (sort + 1) % SORTS.length;
    remember();
    render();
  });
  head.appendChild(knob);
}

/*
  A section head: the name in the hand, the hairline, then whatever orders what is under it, then the count.

  THE HAIRLINE COMES SECOND, NOT LAST. With the knobs pressed up against the label the head read as a title
  with two buttons welded to it and a stroke trailing off to the right. The rule runs from the name to the
  controls, which is what makes the controls belong to the count end of the line rather than to the name.

  THE CONTROLS ARE THE MOD SECTION'S. Favourites and the game's own seeds carry a name, a rule and a number and
  nothing else - there is no tier to order there, and a sort control repeated over every heading is three
  copies of one switch.
*/
function sectionHead(box, name, count, withTier, withSort) {
  const head = el('div', 'section');
  head.appendChild(el('div', 'section-name', name));
  head.appendChild(el('div', 'section-fill'));
  if (withTier) tierKnob(head);
  if (withSort) sortKnob(head);
  head.appendChild(el('div', 'section-count', String(count)));
  box.appendChild(head);
}

/** The tiers actually present in a set of rows, in the order the tier control asks for. */
function tiersOf(rows) {
  const tiers = [];
  for (const row of rows) if (tiers.indexOf(row.tier || 0) < 0) tiers.push(row.tier || 0);
  tiers.sort((a, b) => (tierDesc ? b - a : a - b));
  return tiers;
}

/** One mod's section: its heading, then a group per tier that is actually in the list. */
function modSection(box, key, rows) {
  const tiers = tiersOf(rows);
  sectionHead(box, tabLabel(key), rows.length, tierGroups && tiers.length > 1, true);

  if (!tierGroups) { grid(box, rows, false); return; }

  for (const tier of tiers) {
    const group = rows.filter((row) => (row.tier || 0) === tier);

    // A tier nobody has reached yet must not announce itself by having an empty heading - and an untagged
    // item has no tier to head at all, so it simply follows the last group.
    // "Tier 1", not "TIER 1": the letter-spacing and the weight are what mark it as printed matter, and full
    // caps on top of both made a sub-heading shout louder than the handwritten section name above it.
    if (tier > 0) {
      const head = el('div', 'tier');
      head.appendChild(el('div', 'tier-name', 'Tier ' + tier));
      head.appendChild(el('div', 'tier-fill'));
      head.appendChild(el('div', 'tier-count', String(group.length)));
      box.appendChild(head);
    }

    // Set in with its heading. A tier is a division of the section above it, not a grid of its own.
    grid(box, group, tier > 0);
  }
}

/*
  THE ANY TILE IS A TILE, and it stands in the first line of the grid rather than in a block of its own.

  It used to be a line to itself above every heading - one box and four empty places - which read as a second
  grid over the first and pushed the whole catalogue down by a row. It is the way OUT of the field rather than
  one of the choices, and the first place on the first line says that without spending a line on it.

  Handed to `grid` through this rather than as an argument, because the first grid on the page can be inside a
  mod's tier group, three calls down.
*/
let pendingLead = null;

function takeLead() {
  const one = pendingLead;
  pendingLead = null;
  return one;
}

/** The way out of the field: an empty frame with a cross in it, the way the game draws its own None. */
function anyTile() {
  const tile = el('div', 'tile any' + (view.none.sel ? ' sel' : ''));
  const pick = el('button', 'shot shot-any');
  pick.appendChild(el('span', 'shot-cross', 'X'));
  pick.addEventListener('click', () => s1.call('picker.pick', ''));
  pick.addEventListener('mouseenter', () => showRecord(ANY));
  tile.appendChild(pick);
  sizeTile(tile, pick, null);
  return tile;
}

function renderRows(rows) {
  const box = $('rows');
  box.replaceChildren();

  // The tile numbers are positions in THIS render - see `shownAt`. Reset here, before the first tile is made,
  // so the same list hands the same tile the same number every time it is rebuilt.
  slots = 0;
  tipAt = [];
  slipAt = 0;

  pendingLead = view.none ? anyTile() : null;

  if (rows.length === 0) {
    // Still drawn: with everything filtered away, clearing the field is the one thing left to do here.
    if (pendingLead) grid(box, [], false);
    box.appendChild(el('div', 'empty', query ? 'Nothing matches "' + query + '".' : 'Nothing to pick here.'));
    return;
  }

  // Favourites first, from both halves - a vanilla seed can be starred too - and they stay in their own section
  // as well, which is what the game's product page does with a starred product.
  const favs = rows.filter((r) => r.fav);
  const vanilla = rows.filter((r) => r.vanilla);
  const modded = rows.filter((r) => !r.vanilla);

  // Headings only where there is something under them, so a filter that empties one half does not leave a label
  // standing over nothing.
  // ONE SECTION PER MOD, not one for "everything a mod added". Two strain catalogues installed at once used to
  // land in the same heap under the same heading; the category each mod registered is the split it asked for.
  const keys = [];
  for (const row of modded) if (keys.indexOf(row.tab || '') < 0) keys.push(row.tab || '');

  // The sort control belongs to a mod's section - see sectionHead. With no mod installed there is no such
  // section, and the switch would have nowhere to live at all, so on that save the game's own seeds carry it.
  const orphanSort = keys.length === 0;

  if (favs.length) {
    sectionHead(box, 'Favourites', favs.length, false, false);
    grid(box, favs, false);
  }

  if (vanilla.length) {
    sectionHead(box, 'Vanilla seeds', vanilla.length, false, orphanSort);
    grid(box, vanilla, false);
  }

  for (const key of keys) modSection(box, key, modded.filter((row) => (row.tab || '') === key));
}

/* ---- the head -------------------------------------------------------------------------------------------- */

/*
  "SEED" STANDS IN THE MIDDLE OF THE SHEET, and that is not what a row of three flex items does on its own.

  With the title at `flex: 1` between Back and the count, its middle is the middle of what those two LEFT OVER -
  Back is fifty-five pixels and a count is seven, so the word sat twenty-four pixels right of the sheet's middle
  and moved again the moment the count went from "7" to "12 of 723". The design's own head has it centred on the
  sheet, and a title that shifts when a filter is typed is a title nobody can find twice.

  So the narrower end is padded out to the width of the wider one. What is left for the title is then equal on
  both sides, whatever either end says, and its middle is the sheet's.

  MEASURED, NOT ESTIMATED. `rect()` answers for the LAST render, which is exactly right for these two: neither
  is ever rebuilt - the head is markup and a render only writes text into it - so their widths are a frame old
  only in the render where the count's own text changed, and correct in every other. The fallbacks are for the
  very first render, before anything has been laid out at all and every rect reads as zero.
*/
function widthOf(node, fallback) {
  if (!node || typeof node.rect !== 'function') return fallback;
  const box = node.rect();
  const w = box ? Math.round(box.width) : 0;
  return w > 0 ? w : fallback;
}

function centreHead() {
  const back = widthOf($('back'), 56);
  const count = widthOf($('count'), String($('count').textContent || '').length * 7);

  $('padL').style.width = Math.max(0, count - back) + 'px';
  $('padR').style.width = Math.max(0, back - count) + 'px';
}

/* ---- render ---------------------------------------------------------------------------------------------- */

function render() {
  shell();

  // Divided out of the sheet the mod measured, before anything is laid out against it.
  TILE = tileSize();

  $('pageR').className = 'page right' + (opened ? '' : ' shut');

  $('title').textContent = view.title || 'Select';

  const rows = visible();
  const all = (view.rows || []).length;
  $('count').textContent = rows.length === all ? String(all) : rows.length + ' of ' + all;
  centreHead();

  renderChips();
  renderRows(rows);
  renderSheet();
  renderDock();
}

/* ---- the opening effect ---------------------------------------------------------------------------------- */

/*
  THE SHEET IS MOVED FRAME BY FRAME FROM SCRIPT, and that is not the first thing that was tried.

  The parts are all here: `transform` renders on this surface (an inline `scaleX(0.35)` folds the sheet on the
  spot), `transform-origin: left center` puts the hinge on the crease, and the engine has a transition runner
  that interpolates scale. What does NOT happen is the tween. Measured with a 2400ms transition declared and a
  screenshot every 130ms: the sheet was folded at rest, and fully open in the first frame after the write. No
  intermediate was ever drawn, on either route into it - a class swap or an inline write.

  So the interpolation is done here instead. Fourteen inline writes of `transform` over 300ms; each one is a
  paint-only property, so each repaints ONE box rather than rebuilding the page, which is what makes this cheap
  enough to do per frame. The easing is the cubic `ease-out` would have drawn.

  It is worth knowing the difference: a transition that does not fire costs nothing and shows nothing, so a page
  can carry one for months and read as if it animates. Only a burst of screenshots says otherwise.
*/
const FLIP_MS = 300;
const FLIP_STEPS = 14;

function flip() {
  const page = $('pageR');
  if (!page) { opened = true; return; }

  let step = 0;
  const timer = setInterval(() => {
    step++;
    const t = Math.min(1, step / FLIP_STEPS);
    // The same shape `ease-out` draws, worked out here because the engine will not draw it - see above.
    const eased = 1 - Math.pow(1 - t, 3);
    page.style.transform = 'scaleX(' + eased.toFixed(3) + ')';

    if (t < 1) return;
    clearInterval(timer);

    // CLEARED BY HAND. `#pageR` is markup and not something `render` rebuilds, so an inline transform left on
    // it outlives the effect - and a transform that is still there paints the sheet's CHILDREN through it. Seen
    // by leaving one at 0.75: the dock and the record drew clipped forty pixels short of the sheet's own
    // edge while every rect still answered the full width. `scaleX(1)` is identity and would have looked
    // right, which is exactly why it would have sat there unnoticed until something wrote a different value.
    page.style.transform = '';
    opened = true;
    render();
  }, Math.round(FLIP_MS / FLIP_STEPS));
}

/* ---- wiring ---------------------------------------------------------------------------------------------- */

/*
  ONE RENDER PER BURST OF TYPING, NOT ONE PER LETTER.

  Narrowing seven hundred seeds down to twelve is five keystrokes, and every one of them used to rebuild the
  whole page - including the four on the way there, which nobody was reading. The letter that matters is the
  last one typed, so the render is held back until the typing stops for a moment and then run once, against
  whatever the field says by then.

  Held rather than restarted: a timer that is pushed back on every keystroke never fires while somebody types
  quickly, and a search box that shows nothing until the hands come off the keyboard is worse than a slow one.
  This one guarantees a render every FIND_MS however fast the typing is.
*/
const FIND_MS = 140;
let findTimer = 0;
let findWant = null;

$('find').addEventListener('input', (e) => {
  findWant = e.value || '';
  if (!findTimer) findTimer = setTimeout(findCatchUp, FIND_MS);
});

function findCatchUp() {
  findTimer = 0;

  const q = findWant;
  findWant = null;
  if (q === null || q === query) return;

  query = q;
  render();
}

$('back').addEventListener('click', () => s1.call('picker.back'));

// The mod says when the underlying list moved - a category registered late, an item unlocked. Reloading the
// whole view is right here: it is one call and the list is small enough that a diff would cost more to read
// than it saves.
s1.on('picker.changed', () => {
  load();
  render();
});

load();
render();
flip();

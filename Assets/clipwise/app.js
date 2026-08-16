/*
  The picker, as a page.

  Wire format is deliberately flat: `picker.view` answers one JSON object, `picker.pick` takes one item id,
  `picker.fav` stars one, `picker.back` closes without choosing. Everything else - what was typed, which filter
  is on - is the page's own business and never crosses the bridge, so a keystroke costs nothing on the C# side.

  s1.call is SYNCHRONOUS and returns a string. An empty string means no handler or a handler that threw, and
  both are states this page has to survive: it draws the empty case rather than throwing on JSON.parse.

  NO SYMBOL GLYPHS ANYWHERE. Anything outside Latin-1 comes out as a box in the game's font, so an arrow is the
  word "up" and a star is a picture.

  TILES, NOT ROWS, because this replaces the game's own seed page and that page is a grid. A tile is sixty
  pixels wide and holds a picture; everything a row used to say - the parents, the tier, the effects - moved
  into the bubble that appears while the pointer is on it.
*/

const $ = (id) => document.getElementById(id);

const PER_ROW = 5;

let view = { title: 'Select', tabs: [], rows: [], none: null, added: 'ADDED BY MODS', tags: [] };
let query = '';

/*
  Filters are independent toggles, not one choice out of a list, because that is what they were before this
  screen was rebuilt: a player who wants their favourites of one tier picks both.
*/
let f = { fav: false, hidden: false, vanilla: false, bred: false, tags: [] };

/* One chip cycles through these. Index 0 keeps the order the mod supplied, which is the only order that means
   anything to the mod that supplied it. */
const SORTS = ['Sort: default', 'Sort: A-Z', 'Sort: yield', 'Sort: growth', 'Sort: value',
               'Sort: tier up', 'Sort: tier down'];
let sort = 0;

function load() {
  const raw = s1.call('picker.view');
  if (!raw) return;
  try {
    view = JSON.parse(raw);
  } catch (err) {
    console.error('picker.view was not JSON: ' + err.message);
    return;
  }

  // Opened the way it was left. These three are the player's settings, kept on the mod's side.
  sort = view.sort || 0;
  f.fav = !!view.onlyFav;
}

/** Push the three settings that outlive one open back to the mod. */
function remember() {
  // The third field is the discovered filter, which this page no longer has - the mod still stores it, so
  // it is sent back unchanged rather than silently turned off for anybody using the other picker.
  s1.call('picker.state', sort + '|' + (f.fav ? 1 : 0) + '|' + (view.onlyDisc ? 1 : 0));
}

function visible() {
  const q = query.trim().toLowerCase();

  let rows = (view.rows || []).filter((row) => {
    // The current selection is always reachable. Filtering it away would leave the player unable to see what
    // the field is even set to.
    if (row.sel) return true;

    if (!f.hidden && row.hidden) return false;
    if (f.fav && !row.fav) return false;
    if (f.vanilla && !row.vanilla) return false;
    if (f.bred && row.vanilla) return false;

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
    case 5: r = (a.tier || 0) - (b.tier || 0); break;
    case 6: r = (b.tier || 0) - (a.tier || 0); break;
  }
  return r !== 0 ? r : byName(a, b);
}

function el(kind, cls, text) {
  const node = document.createElement(kind);
  if (cls) node.className = cls;
  if (text !== undefined) node.textContent = text;
  return node;
}

/*
  The filter bar.

  FIXED CHIPS FOR THE SWITCHES, DROP-DOWNS FOR THE LISTS. A chip per effect ran off the edge of the card as
  soon as a mod declared more than a handful of tags, which is exactly what a strain catalogue does. A list
  that can be any length gets a drop-down; a switch that is on or off stays a chip.

  There is no `Discovered` chip. Only discovered seeds reach this page at all, so it was a filter that never
  removed anything.
*/
let open = null;   // which drop-down is showing, or null

/** The tag groups a mod declared, as `prefix -> [tag]`, so `mod:effect/calming` becomes the "effect" list. */
function groups() {
  const out = new Map();

  for (const tag of view.tags || []) {
    const cut = tag.id.indexOf('/');
    const colon = tag.id.indexOf(':');
    if (cut < 0 || colon < 0 || cut < colon) continue;

    const name = tag.id.substring(colon + 1, cut);
    if (!out.has(name)) out.set(name, []);
    out.get(name).push(tag);
  }

  return out;
}

/*
  CHIP WIDTHS ARE ESTIMATED, NOT MEASURED, and that is the only option here.

  There is no flex-wrap in this engine, so a row of chips either runs off the card or gets chunked in script -
  and chunking needs to know how wide each chip will be BEFORE it is laid out. `el.rect()` reports the last
  render, which is a frame too late. Seven per em plus the padding matches the card's font closely enough that
  a row breaks one chip early at worst, and one chip early is invisible while one chip late is a word sliced in
  half at the edge of the clipboard - which is what the tester was looking at.
*/
const CHIP_ROOM = 396;    // the card's 420 minus its side padding
const CHIP_PAD = 22;      // the chip's own padding, both sides

function chipWidth(label) {
  return Math.round(String(label).length * 7) + CHIP_PAD;
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

  chip(wanted, 'Favourites', f.fav, () => { f.fav = !f.fav; remember(); });
  chip(wanted, 'Vanilla', f.vanilla, () => { f.vanilla = !f.vanilla; f.bred = false; });
  chip(wanted, 'Bred', f.bred, () => { f.bred = !f.bred; f.vanilla = false; });
  if (view.hiddenCount || f.hidden) {
    chip(wanted, 'Hidden (' + (view.hiddenCount || 0) + ')', f.hidden, () => { f.hidden = !f.hidden; });
  }
  chip(wanted, SORTS[sort] || SORTS[0], sort !== 0, () => { sort = (sort + 1) % SORTS.length; remember(); });

  // One drop-down per tag group, named after the group. Its chip counts what is ticked, so a filter that is on
  // is visible without opening it.
  for (const [name, tags] of groups()) {
    const chosen = tags.filter((t) => f.tags.indexOf(t.id) >= 0).length;
    const label = name.charAt(0).toUpperCase() + name.slice(1) + (chosen ? ' (' + chosen + ')' : '');
    chip(wanted, label, chosen > 0 || open === name, () => { open = open === name ? null : name; });
  }

  // Only when something is actually on. A button that does nothing is worse than no button.
  if (f.fav || f.vanilla || f.bred || f.tags.length || sort !== 0) {
    chip(wanted, 'Clear', false, () => {
      f.fav = false; f.vanilla = false; f.bred = false; f.tags = [];
      sort = 0; open = null;
      remember();
    });
  }

  let row = null;
  let used = 0;
  for (const one of wanted) {
    if (!row || used + one.width > CHIP_ROOM) {
      row = el('div', 'chip-row');
      box.appendChild(row);
      used = 0;
    }
    row.appendChild(one.node);
    used += one.width + 6;   // the row's gap
  }

  renderDropdown();
}

/** The open group's tick list, under the bar rather than over the grid - nothing to dismiss by accident. */
function renderDropdown() {
  const box = $('drop');
  box.replaceChildren();

  if (!open) { box.className = 'drop closed'; return; }
  box.className = 'drop';

  const tags = groups().get(open) || [];
  for (const tag of tags) {
    const ticked = f.tags.indexOf(tag.id) >= 0;
    const line = el('button', 'tick' + (ticked ? ' on' : ''));
    line.appendChild(el('span', 'tick-box', ticked ? 'x' : ''));
    line.appendChild(el('span', 'tick-name', tag.label));
    line.addEventListener('click', () => {
      const at = f.tags.indexOf(tag.id);
      if (at >= 0) f.tags.splice(at, 1); else f.tags.push(tag.id);
      render();
    });
    box.appendChild(line);
  }
}

/*
  The floating bubble, which is the whole reason a tile can afford to say nothing.

  ONE NODE FOR THE WHOLE PAGE, not one per tile. Per-tile nodes left five bubbles on screen at once in a
  tester's shot: a `mouseleave` that never arrives - the pointer left through a gap, the grid re-rendered under
  it, the tile it belonged to was replaced - leaves a node with nobody to remove it. A single node cannot
  accumulate, and anything that shows a new one takes the old one down first.

  Two halves are still needed for WHEN and WHERE: mouseenter/mouseleave, and `rect()`, which reports the last
  render - so a node the script has only just created reads as zeroes.
*/
let tip = null;

function hideTip() {
  if (tip) { tip.remove(); tip = null; }
}

function showTip(anchor, row) {
  hideTip();

  const r = anchor.rect();
  if (!r || !r.height) return;

  tip = el('div', 'bubble');
  tip.appendChild(el('div', 'bubble-name', row.name));
  if (row.note) tip.appendChild(el('div', 'bubble-line', row.note));
  if (row.tier) tip.appendChild(el('div', 'bubble-line', 'Tier ' + row.tier));
  if (row.effects && row.effects.length) tip.appendChild(el('div', 'bubble-line', row.effects.join(', ')));

  // Beside the tile, on whichever side has room. The card's own width is the only bound the page knows, so it
  // is measured rather than assumed.
  const card = document.body.rect();
  const width = 168;
  let left = r.x + r.width + 6;
  if (card && left + width > card.width - 4) left = r.x - width - 6;
  if (left < 4) left = 4;

  tip.style.left = Math.round(left) + 'px';
  tip.style.top = Math.round(r.y) + 'px';
  tip.style.width = width + 'px';
  document.body.appendChild(tip);
}

function bubble(anchor, row) {
  anchor.addEventListener('mouseenter', () => showTip(anchor, row));
  anchor.addEventListener('mouseleave', hideTip);
  // A click re-renders the grid, and the tile this belonged to is gone with it.
  anchor.addEventListener('click', hideTip);
}

/*
  The backstop.

  `mouseleave` is the right event and it does not always arrive: the pointer can leave a tile through the gap
  between two of them, or the grid can re-render under a stationary pointer. Either way the bubble outlives the
  tile it belonged to, which is what a tester saw five of at once.

  So the page as a whole also takes the pointer moving anywhere that is NOT a tile as "nothing is hovered". One
  listener, and it makes a missed mouseleave cost a pixel of movement rather than a stuck bubble.
*/
document.body.addEventListener('mousemove', (e) => {
  if (!tip) return;
  const over = e && e.target ? e.target : null;
  if (over && over.className && String(over.className).indexOf('shot') >= 0) return;
  hideTip();
});

/** One tile: the seed's picture, its star, and the bubble that carries the words. */
function tileNode(row) {
  const tile = el('div', 'tile' + (row.sel ? ' sel' : ''));

  // A button, not a div: the engine wires a hit target unconditionally for button, a, input and textarea.
  const pick = el('button', 'shot');
  const shot = document.createElement('img');
  shot.className = 'shot-img';
  // Supplied by the mod at open time. When a conversion failed there is no picture behind this name, and the
  // letters underneath are what the tile has left to identify itself with.
  shot.src = 's1://icon/' + row.id;
  pick.appendChild(shot);
  // No name under the picture. The game's own tiles carry a vial and nothing else, and the bubble is where the
  // words go - a nine-pixel name on a sixty-pixel tile was neither vanilla nor readable.
  pick.addEventListener('click', () => s1.call('picker.pick', row.id));
  tile.appendChild(pick);

  const star = el('button', 'star' + (row.fav ? ' on' : ''));
  const glyph = document.createElement('img');
  glyph.className = 'star-img';
  glyph.src = 'star.png';
  star.appendChild(glyph);
  star.addEventListener('click', () => {
    row.fav = s1.call('picker.fav', row.id) === 'on';
    render();
  });
  tile.appendChild(star);

  // Only while the Hidden chip is on. A tile is sixty pixels wide and cannot carry two permanent buttons, and
  // hiding things is a tidying-up job rather than something done in passing.
  if (f.hidden) {
    const hide = el('button', 'hide' + (row.hidden ? ' on' : ''), row.hidden ? 'o' : 'x');
    hide.addEventListener('click', () => {
      row.hidden = s1.call('picker.hide', row.id) === 'on';
      view.hiddenCount = (view.hiddenCount || 0) + (row.hidden ? 1 : -1);
      render();
    });
    tile.appendChild(hide);
  }

  if (view.tips !== false) bubble(pick, row);
  return tile;
}

/** A grid of tiles, five to a line. There is no wrapping here, so the lines are made rather than found. */
function grid(box, rows) {
  let line = null;
  rows.forEach((row, i) => {
    if (i % PER_ROW === 0) {
      line = el('div', 'line');
      box.appendChild(line);
    }
    line.appendChild(tileNode(row));
  });

  // The last line is short, and a stretched tile would be a different size from the rest of the grid.
  if (line) {
    for (let i = rows.length % PER_ROW; i > 0 && i < PER_ROW; i++) line.appendChild(el('div', 'tile hole'));
  }
}

function renderRows() {
  const box = $('rows');
  box.replaceChildren();

  // "Any" first and always, whatever is filtered: it is how a field is cleared, not one of the options. Drawn
  // as the game draws it, a tile with a cross in it.
  if (view.none) {
    const line = el('div', 'line');
    const tile = el('div', 'tile' + (view.none.sel ? ' sel' : ''));
    const pick = el('button', 'shot');
    pick.appendChild(el('span', 'shot-cross', 'X'));
    pick.appendChild(el('span', 'shot-name', view.none.name || 'None'));
    pick.addEventListener('click', () => s1.call('picker.pick', ''));
    tile.appendChild(pick);
    line.appendChild(tile);
    for (let i = 1; i < PER_ROW; i++) line.appendChild(el('div', 'tile hole'));
    box.appendChild(line);
  }

  const rows = visible();
  if (rows.length === 0) {
    box.appendChild(el('div', 'empty', query ? 'Nothing matches "' + query + '".' : 'Nothing to pick here.'));
    return;
  }

  // Favourites first, from both halves - a vanilla seed can be starred too - and they stay in their own
  // section as well, which is what the game's product page does with a starred product.
  const favs = rows.filter((r) => r.fav);
  const vanilla = rows.filter((r) => r.vanilla);
  const modded = rows.filter((r) => !r.vanilla);

  // Headings only where there is something under them, so a filter that empties one half does not leave a
  // label standing over nothing.
  if (favs.length) {
    box.appendChild(el('div', 'section', 'FAVOURITES'));
    grid(box, favs);
  }

  if (vanilla.length) {
    box.appendChild(el('div', 'section', 'VANILLA SEEDS'));
    grid(box, vanilla);
  }

  if (modded.length) {
    box.appendChild(el('div', 'section', (view.added || 'ADDED BY MODS').toUpperCase()));

    // A sub-heading per tier, and only for tiers that are actually in the list: a tier nobody has reached
    // yet must not announce itself by having an empty heading.
    const tiers = [];
    for (const row of modded) if (tiers.indexOf(row.tier || 0) < 0) tiers.push(row.tier || 0);
    tiers.sort((a, b) => (sort === 6 ? b - a : a - b));

    for (const tier of tiers) {
      if (tier > 0 && tiers.length > 1) box.appendChild(el('div', 'section sub', 'TIER ' + tier));
      grid(box, modded.filter((row) => (row.tier || 0) === tier));
    }
  }
}

function render() {
  hideTip();
  $('title').textContent = view.title || 'Select';

  const rows = visible();
  const all = (view.rows || []).length;
  $('count').textContent = rows.length === all ? String(all) : rows.length + ' of ' + all;

  renderChips();
  renderRows();
}

$('find').addEventListener('input', (e) => {
  query = e.value || '';
  render();
});

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

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

let view = { title: 'Select', tabs: [], rows: [], none: null, added: 'ADDED BY MODS' };
let query = '';
let mode = 'all';

/* All six, in the order they sit on the bar. `bred` is the other half of `vanilla`: the spec asks for a vanilla
   toggle that shows the game's seeds when on and the mod's when off, and a bar cannot be half a toggle. */
const MODES = [
  { id: 'all', label: 'All' },
  { id: 'tierup', label: 'Tier up' },
  { id: 'tierdown', label: 'Tier down' },
  { id: 'fav', label: 'Favourites' },
  { id: 'vanilla', label: 'Vanilla' },
  { id: 'bred', label: 'Bred' },
];

function load() {
  const raw = s1.call('picker.view');
  if (!raw) return;
  try {
    view = JSON.parse(raw);
  } catch (err) {
    console.error('picker.view was not JSON: ' + err.message);
  }
}

function visible() {
  const q = query.trim().toLowerCase();

  let rows = (view.rows || []).filter((row) => {
    if (mode === 'fav' && !row.fav) return false;
    if (mode === 'vanilla' && !row.vanilla) return false;
    if (mode === 'bred' && row.vanilla) return false;
    if (!q) return true;
    return (
      (row.name || '').toLowerCase().indexOf(q) >= 0 ||
      (row.note || '').toLowerCase().indexOf(q) >= 0
    );
  });

  // Sorted on a copy. The order the mod supplied is the default and has to survive a filter being switched off.
  if (mode === 'tierup' || mode === 'tierdown') {
    const dir = mode === 'tierup' ? 1 : -1;
    rows = rows.slice().sort((a, b) => {
      if ((a.tier || 0) !== (b.tier || 0)) return ((a.tier || 0) - (b.tier || 0)) * dir;
      const an = (a.name || '').toLowerCase();
      const bn = (b.name || '').toLowerCase();
      return an < bn ? -1 : an > bn ? 1 : 0;
    });
  }

  return rows;
}

function el(kind, cls, text) {
  const node = document.createElement(kind);
  if (cls) node.className = cls;
  if (text !== undefined) node.textContent = text;
  return node;
}

function renderChips() {
  const box = $('chips');
  box.replaceChildren();

  for (const entry of MODES) {
    const button = el('button', 'chip' + (entry.id === mode ? ' on' : ''), entry.label);
    button.addEventListener('click', () => {
      mode = entry.id;
      render();
    });
    box.appendChild(button);
  }
}

/*
  The floating bubble, which is the whole reason a tile can afford to say nothing.

  Two halves and both are needed: mouseenter/mouseleave for WHEN, and `rect()` for WHERE. A CSS-only tooltip
  cannot work here - a `:hover` rule may repaint a box but never lay one out - and `rect()` reports the last
  render, so a node the script has only just created reads as zeroes.
*/
function bubble(anchor, row) {
  let node = null;
  const hide = () => { if (node) { node.remove(); node = null; } };

  anchor.addEventListener('mouseenter', () => {
    if (node) return;
    const r = anchor.rect();
    if (!r || !r.height) return;

    node = el('div', 'bubble');
    node.appendChild(el('div', 'bubble-name', row.name));
    if (row.note) node.appendChild(el('div', 'bubble-line', row.note));
    if (row.tier) node.appendChild(el('div', 'bubble-line', 'Tier ' + row.tier));
    if (row.effects && row.effects.length) {
      node.appendChild(el('div', 'bubble-line', row.effects.join(', ')));
    }

    // Beside the tile, and on whichever side has room. The card's own width is the only bound the page knows,
    // so it is measured rather than assumed.
    const card = document.body.rect();
    const width = 168;
    let left = r.x + r.width + 6;
    if (card && left + width > card.width - 4) left = r.x - width - 6;
    if (left < 4) left = 4;

    node.style.left = Math.round(left) + 'px';
    node.style.top = Math.round(r.y) + 'px';
    node.style.width = width + 'px';
    document.body.appendChild(node);
  });

  anchor.addEventListener('mouseleave', hide);
  // A click re-renders the grid; a bubble left behind would hang over the new one.
  anchor.addEventListener('click', hide);
}

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
  pick.appendChild(el('span', 'shot-name', row.name));
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

  bubble(pick, row);
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
    tiers.sort((a, b) => (mode === 'tierdown' ? b - a : a - b));

    for (const tier of tiers) {
      if (tier > 0 && tiers.length > 1) box.appendChild(el('div', 'section sub', 'TIER ' + tier));
      grid(box, modded.filter((row) => (row.tier || 0) === tier));
    }
  }
}

function render() {
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

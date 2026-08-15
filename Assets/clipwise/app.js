/*
  The picker, as a page.

  Wire format is deliberately flat: `picker.view` answers one JSON object, `picker.pick` takes one item id,
  `picker.fav` stars one, `picker.back` closes without choosing. Everything else - what was typed, which filter
  is on - is the page's own business and never crosses the bridge, so a keystroke costs nothing on the C# side.

  s1.call is SYNCHRONOUS and returns a string. An empty string means no handler or a handler that threw, and
  both are states this page has to survive: it draws the empty case rather than throwing on JSON.parse.

  NO SYMBOL GLYPHS ANYWHERE. Anything outside Latin-1 comes out as a box in the game's font, so an arrow is the
  word "up" and a star is an asterisk that changes colour.
*/

const $ = (id) => document.getElementById(id);

let view = { title: 'Select', tabs: [], rows: [], none: null };
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

/** The label the mod gave its own category, which is what the second heading should say. */
function moddedLabel() {
  const tabs = view.tabs || [];
  for (const tab of tabs) {
    if (tab.id !== 'vanilla' && tab.label) return tab.label.toUpperCase();
  }
  return 'ADDED BY MODS';
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

/**
 * One pickable line: the star, then the name, what it came from and its tier.
 *
 * The star sits BESIDE the pick button rather than inside it. Nested, a click on the star would reach the row
 * as well and star the item on its way to selecting it.
 */
function rowNode(row) {
  const line = el('div', 'row' + (row.sel ? ' sel' : ''));

  const star = el('button', 'star' + (row.fav ? ' on' : ''));
  // A picture, not a character: the game's font has no star, and anything outside Latin-1 paints as a box.
  // It is drawn white and tinted through `color`, so one file serves both states.
  const glyph = document.createElement('img');
  glyph.className = 'star-img';
  glyph.src = 'star.png';
  star.appendChild(glyph);
  star.addEventListener('click', () => {
    row.fav = s1.call('picker.fav', row.id) === 'on';
    render();
  });
  line.appendChild(star);

  // A button, not a div: the engine wires a hit target unconditionally for button, a, input and textarea.
  const pick = el('button', 'pick');
  pick.appendChild(el('span', 'row-name', row.name));
  if (row.note) pick.appendChild(el('span', 'row-note', row.note));
  pick.appendChild(el('span', 'row-tier', row.tier ? 'T' + row.tier : ''));
  pick.addEventListener('click', () => s1.call('picker.pick', row.id));
  line.appendChild(pick);

  return line;
}

function renderRows() {
  const box = $('rows');
  box.replaceChildren();

  // "Any" first and always, whatever is filtered: it is how a field is cleared, not one of the options.
  if (view.none) {
    const line = el('div', 'row' + (view.none.sel ? ' sel' : ''));
    line.appendChild(el('span', 'star'));

    const pick = el('button', 'pick');
    pick.appendChild(el('span', 'row-name', view.none.name || 'None'));
    pick.addEventListener('click', () => s1.call('picker.pick', ''));
    line.appendChild(pick);

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
    for (const row of favs) box.appendChild(rowNode(row));
  }

  if (vanilla.length) {
    box.appendChild(el('div', 'section', 'VANILLA SEEDS'));
    for (const row of vanilla) box.appendChild(rowNode(row));
  }

  if (modded.length) {
    box.appendChild(el('div', 'section', moddedLabel()));

    // A sub-heading per tier, and only for tiers that are actually in the list: a tier nobody has reached
    // yet must not announce itself by having an empty heading.
    const tiers = [];
    for (const row of modded) if (tiers.indexOf(row.tier || 0) < 0) tiers.push(row.tier || 0);
    tiers.sort((a, b) => (mode === 'tierdown' ? b - a : a - b));

    for (const tier of tiers) {
      if (tier > 0 && tiers.length > 1) box.appendChild(el('div', 'section sub', 'TIER ' + tier));
      for (const row of modded) if ((row.tier || 0) === tier) box.appendChild(rowNode(row));
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

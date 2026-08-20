/*
  The picker, as a page: vanilla's own seed card, and a narrower second card beside it.

  Wire format is deliberately flat: `picker.view` answers one JSON object, `picker.pick` takes one item id,
  `picker.fav` stars one, `picker.back` closes without choosing. Everything else - what was typed, which filter
  is on - is the page's own business and never crosses the bridge, so a keystroke costs nothing on the C# side.

  s1.call is SYNCHRONOUS and returns a string. An empty string means no handler or a handler that threw, and
  both are states this page has to survive: it draws the empty case rather than throwing on JSON.parse.

  NO SYMBOL GLYPHS ANYWHERE. Anything outside Latin-1 comes out as a box in the game's font, so a star is a
  picture and a cross is the letter X.

  TILES, NOT ROWS, because this replaces the game's own seed page and that page is a grid. A tile is seventy
  pixels wide and holds a picture; everything a row used to say - the parents, the tier, the effects - is on the
  preview on the facing card, which stays put instead of following the pointer.
*/

const $ = (id) => document.getElementById(id);

const PER_ROW = 5;

let view = {
  title: 'Select', tabs: [], rows: [], none: null, added: 'ADDED BY MODS', tags: [],
  w: 682, h: 620, pageW: 420, pageRW: 252, gap: 10,
};

let query = '';

/*
  Filters are independent toggles, not one choice out of a list, because that is what they were before this
  screen was rebuilt: a player who wants their favourites of one tier picks both. `tags` is what the effect
  ticks on the right card write into.
*/
let f = { fav: false, hidden: false, vanilla: false, bred: false, fx: [], tags: [] };

/* One chip cycles through these. Index 0 keeps the order the mod supplied, which is the only order that means
   anything to the mod that supplied it. */
const SORTS = ['Sort: default', 'Sort: A-Z', 'Sort: yield', 'Sort: growth', 'Sort: value',
               'Sort: tier up', 'Sort: tier down'];
let sort = 0;

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
}

/** Push the three settings that outlive one open back to the mod. */
function remember() {
  // The third field is the discovered filter, which this page no longer has - the mod still stores it, so it
  // is sent back unchanged rather than silently turned off for anybody using the other picker.
  s1.call('picker.state', sort + '|' + (f.fav ? 1 : 0) + '|' + (view.onlyDisc ? 1 : 0));
}

/* ---- filtering and sorting ------------------------------------------------------------------------------ */

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

    // AND, not OR. Ticking two effects asks for a seed that has both - which is the question a breeder is
    // actually holding when they tick the second one.
    for (const fx of f.fx) if ((row.effects || []).indexOf(fx) < 0) return false;
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

/* ---- the two cards -------------------------------------------------------------------------------------- */

/*
  The page is told how big it is, because it cannot ask.

  A surface answers layout coordinates and nothing about the viewport, so "how wide is half of me" has no answer
  here. The mod measured vanilla's own card in `SurfacePicker.Fit` and sent the numbers along, and this is where
  they land on the boxes. The right card is never wider or taller than the left - that ceiling is applied on the
  C# side, where the measuring happens, so there is one place it can be got wrong.
*/
function shell() {
  const left = Math.round(view.pageW || 420);
  const right = Math.round(view.pageRW || Math.round(left * 0.6));
  const gap = Math.round(view.gap || 10);

  $('pageL').style.width = left + 'px';
  $('pageR').style.width = right + 'px';
  $('gap').style.width = gap + 'px';
}

/** How much room a chip row has: the left card, less its own side padding. */
function chipRoom() {
  return Math.round(view.pageW || 420) - 40;
}

/* ---- the filter chips ------------------------------------------------------------------------------------ */

/*
  FIXED CHIPS FOR THE SWITCHES. The lists - one per tag group a mod declared - are the ticks on the right card
  now, where they can all be read at once instead of hiding behind a chip.

  There is no `Discovered` chip. Only discovered seeds reach this page at all, so it was a filter that never
  removed anything.

  CHIP WIDTHS ARE ESTIMATED, NOT MEASURED, and that is the only option here. There is no flex-wrap in this
  engine, so a row of chips either runs off the card or gets chunked in script - and chunking needs to know how
  wide each chip will be BEFORE it is laid out. `el.rect()` reports the last render, which is a frame too late.
  Seven per em plus the padding matches the card's font closely enough that a row breaks one chip early at
  worst, and one chip early is invisible while one chip late is a word sliced in half at the edge of the
  clipboard.
*/
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

  chip(wanted, 'Favorites', f.fav, () => { f.fav = !f.fav; remember(); });
  /*
    ONE CHIP FOR THREE STATES, not two chips for two.

    Vanilla and Bred were never independent - turning one on turned the other off - so as two buttons they
    spent a chip's worth of room on a choice that only ever has one answer at a time. One chip, cycling
    all -> vanilla -> bred, labelled with the state it is in - the way the sort chip already worked.
  */
  chip(wanted, f.vanilla ? 'Vanilla' : f.bred ? 'Bred' : 'All', f.vanilla || f.bred, () => {
    if (f.vanilla) { f.vanilla = false; f.bred = true; }
    else if (f.bred) { f.bred = false; }
    else { f.vanilla = true; }
  });
  if (view.hiddenCount || f.hidden) {
    chip(wanted, 'Hidden (' + (view.hiddenCount || 0) + ')', f.hidden, () => { f.hidden = !f.hidden; });
  }
  chip(wanted, SORTS[sort] || SORTS[0], sort !== 0, () => { sort = (sort + 1) % SORTS.length; remember(); });

  // Only when something is actually on. A button that does nothing is worse than no button.
  if (f.fav || f.vanilla || f.bred || f.fx.length || f.tags.length || query || sort !== 0) {
    chip(wanted, 'Clear', false, () => {
      // Emptied in place rather than replaced: `tickDefs` hands each tick the array it writes into, and a
      // fresh array would leave the ticks of this render pointing at the old one.
      f.fav = false; f.vanilla = false; f.bred = false; f.fx.length = 0; f.tags.length = 0;
      sort = 0;
      query = '';
      $('find').value = '';
      remember();
    });
  }

  const room = chipRoom();
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

/* ---- the effect filters ---------------------------------------------------------------------------------- */

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

/** There is no flex-wrap in this engine, so the lines are made rather than found. How many fit is a question
    about the right card, which is the only box whose width this page is told. */
function ticksPerRow() {
  const room = Math.round(view.pageRW || 252) - 40;
  return room >= 300 ? 3 : room >= 190 ? 2 : 1;
}

/*
  WHAT THE TICKS ARE, AND WHY THEY ARE NOT ONLY THE MOD'S TAGS.

  The drop-down this replaces was built from `view.tags` - the tags a MOD declared - so on a save with no
  strain catalogue installed the whole filter had nothing in it and hid itself. That is a filter that only
  exists for people who already have another mod, and the effects are the game's own data: every row already
  carries `effects`, vanilla seeds included.

  So the list is the union of the effects the rows actually have, and then any other group a mod declared
  after it. Nothing is invented and nothing is asked for over the bridge - both halves are already on the row.
*/
function tickDefs() {
  const out = [];

  const names = [];
  for (const row of view.rows || []) {
    for (const e of row.effects || []) if (names.indexOf(e) < 0) names.push(e);
  }
  names.sort((a, b) => a.toLowerCase() < b.toLowerCase() ? -1 : 1);
  for (const name of names) out.push({ key: 'fx:' + name, label: name, list: f.fx, value: name });

  // Whatever else a mod files - a "flavour" group, a "source" group - gets its ticks under the effects rather
  // than being dropped for not being the group this card was drawn for.
  for (const [name, list] of groups()) {
    if (name === 'tier') continue;   // tier is what the sort chip does, and having it twice is a filter fighting itself
    for (const t of list) {
      if (out.some((x) => x.key === 'tag:' + t.id)) continue;
      out.push({ key: 'tag:' + t.id, label: t.label, list: f.tags, value: t.id });
    }
  }

  return out;
}

function renderTicks() {
  const box = $('fx');
  box.replaceChildren();

  const defs = tickDefs();
  if (defs.length === 0) { box.style.display = 'none'; return; }
  box.style.display = 'flex';

  const per = ticksPerRow();
  let line = null;
  defs.forEach((def, i) => {
    if (i % per === 0) {
      line = el('div', 'fx-row');
      box.appendChild(line);
    }

    const on = def.list.indexOf(def.value) >= 0;
    const node = el('button', 'tick' + (on ? ' on' : ''));
    node.appendChild(el('span', 'tick-box', on ? 'x' : ''));
    node.appendChild(el('span', 'tick-name', def.label));
    node.addEventListener('click', () => {
      const at = def.list.indexOf(def.value);
      if (at >= 0) def.list.splice(at, 1); else def.list.push(def.value);
      render();
    });
    line.appendChild(node);
  });

  // The last line is short, and a stretched tick would be a different width from the rest.
  if (line) {
    for (let i = defs.length % per; i > 0 && i < per; i++) line.appendChild(el('div', 'tick'));
  }
}

/* ---- the hover preview ----------------------------------------------------------------------------------- */

/*
  Which row the preview is showing. Not bookkeeping - it is what stops the page rebuilding forever.

  Every DOM write rebuilds the page, and a rebuild destroys and recreates every box on it. The pointer has not
  moved, but the box under it is a NEW object, so uGUI raises its enter again, which fills the preview, which
  writes to the DOM, which rebuilds. A probe listener counted about ten of those rounds a second with the
  pointer standing still.

  It is not only wasted work. uGUI raises a click only when the press and the release land on the SAME object,
  and while this runs the tile a player pressed is destroyed several times over before they let go - so no click
  is raised at all, nothing is picked, and nothing is logged either, since there was no click event to log. That
  is "I click a seed and nothing happens", and it needs nothing in the way to happen.

  So a request for the row that is already showing is answered with nothing at all.
*/
let shown = null;

/* Whose tooltip has already faded up. See `fillTip` - it is what stops the fade restarting itself. */
let faded = null;

function showPreview(row) {
  if (shown && row && shown.id === row.id) return;
  shown = row;
  faded = null;
  render();
}

/** One "LABEL  value" line, and only when there is a value. A card that promises a field and shows nothing
    under it is worse than a shorter card. */
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

/*
  The sentence over the table.

  Built from what the mod actually sent - the parents, the tier and the number of effects on the plant - and
  never from a rule about what a tier ought to mean. Every clause drops out on its own when its fact is missing,
  so a vanilla seed with no parents gets a shorter line rather than a line with a hole in it.
*/
function prose(row) {
  const bits = [];
  if (row.note) bits.push('Root from ' + row.note + '.');

  const effects = (row.effects || []).length;
  if (row.tier && effects) bits.push('Tier ' + row.tier + ', ' + effects + (effects === 1 ? ' effect' : ' effects') + ' straight off the plant.');
  else if (row.tier) bits.push('Tier ' + row.tier + '.');
  else if (effects) bits.push(effects + (effects === 1 ? ' effect' : ' effects') + ' straight off the plant.');

  return bits.join(' ');
}

function renderPreview() {
  const box = $('preview');
  box.replaceChildren();

  const row = shown;
  if (!row) {
    box.appendChild(el('div', 'pv-empty', 'Point at a seed to read it here.'));
    return;
  }

  const top = el('div', 'pv-top');
  const head = el('div', 'pv-head');
  head.appendChild(el('div', 'pv-name', row.name || row.id));

  const line = prose(row);
  if (line) head.appendChild(el('div', 'pv-prose', line));
  top.appendChild(head);

  // The item's own picture, the same one its tile carries - so the bud on the card is the bud in hand.
  const bud = document.createElement('img');
  bud.className = 'pv-bud';
  bud.src = 's1://icon/' + row.id;
  top.appendChild(bud);
  box.appendChild(top);

  if (row.fav) box.appendChild(el('div', 'pv-badge', 'FAVOURITE'));

  box.appendChild(el('div', 'pv-rule'));

  // Who filed this entry. Only for something a mod claimed - the game's own seeds need no credit line.
  if (row.source) box.appendChild(el('div', 'pv-src', String(row.source).toUpperCase()));

  fact(box, 'CROSS', row.note);
  fact(box, 'TIER', row.tier ? row.tier : '');
  if (row.effects && row.effects.length) fact(box, 'EFFECTS', row.effects.join(', ') + ' (' + row.effects.length + ')');
  fact(box, 'YIELD', row.yield ? row.yield + (row.harvest ? ' ' + row.harvest : '') : '');
  fact(box, 'GROWTH', row.growth && row.growth < 2000000000 ? row.growth + ' h' : '');
  fact(box, 'PRODUCT', row.product);
  fact(box, 'TYPE', row.drug);
  fact(box, 'VALUE', money(row.value));
  fact(box, 'BUY', money(row.buy));
  if (row.disc === false) fact(box, 'DISCOVERED', 'not yet');

  // Whatever the registering mod answered for this item just now - a discoverer's alias, a trait, anything it
  // knows and this page does not. Last, so a mod cannot push the game's own facts off the top of the card, and
  // every one of them is subject to the same rule as the rest: no value, no row.
  for (const extra of row.facts || []) fact(box, extra.k, extra.v);
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
  if (on.indexOf('shot-item') >= 0 || on.indexOf('star') >= 0 || on.indexOf('hide') >= 0) return;
  s1.call('picker.stray', on || '(unnamed)');
});

/*
  THE TOOLTIP.

  148 AND NOT MORE, because the width decides whether it can stand BESIDE its tile at all. A line is 380 wide
  and a tile 71. At 168 the middle column had room on neither side and the tooltip ended up drawn over the very
  seed the pointer was on. 148 is the widest that still leaves room on one side of every one of the five
  columns.

  WHICH SIDE IS DECIDED BY THE COLUMN, not by measuring. The old code took `anchor.rect()` minus `line.rect()`,
  because the difference between two rects inside the same scrolled box cancels the scroll offset a surface
  cannot read. Inside the line there is nothing left to measure at all: the column number says where the tile
  starts, and whether there is room to its right.

  THE NAME, AND NOTHING ELSE. The tile shows a picture and no words, so the name is the one thing the pointer
  cannot already see; the cross, the tier, the effects and every number are on the preview on the facing card,
  which is filled by the same hover and stays put afterwards. A second line here would cover the tile below it
  to repeat something already on screen.
*/
const TIP_WIDTH = 148;
const TIP_GAP = 6;

/** Where a line's tooltip stands, measured across the line. */
function tipLeft(col) {
  // The tile's own width, from the numbers the mod sent: the card's content width less the four gaps, over five.
  const tileW = Math.round((chipRoom() - (PER_ROW - 1) * 6) / PER_ROW);
  const x = col * (tileW + 6);
  // The last column has no room to its right, and a tooltip that ran off the end would be cut off by the list.
  return col >= PER_ROW - 1 ? x - TIP_WIDTH - TIP_GAP : x + tileW + TIP_GAP;
}

function lineNode() {
  return el('div', 'line');
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

  An empty tooltip is absolutely placed and fully transparent, so it costs no room and draws nothing.

  LAST IN THE LINE, AND THAT IS NOT TIDINESS. There is no z-index in this engine and paint order is document
  order, so a tooltip written before the tiles is painted UNDERNEATH them: the only part of it that showed was
  the six-pixel gap between two tiles, which reads exactly like a tooltip that is drawn far too narrow. It is
  appended once the line's tiles are in, which is still a build-time decision and never a hover-time one.
*/
function tipNode() {
  return el('div', 'tip');
}

/*
  THREE PROPERTY WRITES ON A NODE THAT IS ALREADY THERE: the text, the offset, and the class that fades it up.

  THE FADE IS THE ONE WRITE THAT IS NOT PART OF A RENDER, and it is bounded on purpose. A transition needs the
  SAME element to change, so a rebuild cannot carry one - the class has to be set on the node that is already
  there. `faded` is what makes that safe: if the class change does rebuild the page, the tooltip comes back
  already lit and schedules nothing, so the worst case is one extra rebuild and a fade that is cut short, never
  a loop.
*/
function fillTip(node, row, col) {
  if (!node) return;

  node.textContent = row.name || row.id;
  node.style.left = tipLeft(col) + 'px';
  node.style.width = TIP_WIDTH + 'px';

  const up = faded === row.id;
  node.className = 'tip' + (up ? ' on' : '');
  if (up) return;

  setTimeout(() => {
    if (!shown || shown.id !== row.id || faded === row.id) return;
    faded = row.id;
    node.className = 'tip on';
  }, 0);
}

/** One tile: the seed's own picture and its star. Pointing at it fills the preview on the facing card and the
    tooltip on its own line. */
function tileNode(row) {
  const tile = el('div', 'tile' + (row.sel ? ' sel' : ''));

  // A button, not a div: the engine wires a hit target unconditionally for button, a, input and textarea.
  //
  // `shot-item` carries no style. It exists so a test can aim at a seed: the Any tile is also a `.shot` and is
  // drawn FIRST, so `sideload_click .shot` clears the field instead of choosing anything - which reads exactly
  // like the click being broken, and cost a whole diagnosis round.
  const pick = el('button', 'shot shot-item');
  const shot = document.createElement('img');
  shot.className = 'shot-img';
  // Supplied by the mod at open time, off the LIVE item definition - which is what lets a mod that tints a seed
  // per strain have every vial on the grid look like itself.
  shot.src = 's1://icon/' + row.id;
  pick.appendChild(shot);
  pick.addEventListener('click', () => s1.call('picker.pick', row.id));
  pick.addEventListener('mouseenter', () => showPreview(row));
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

  // Only while the Hidden chip is on. A tile is seventy pixels wide and cannot carry two permanent buttons, and
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

  return tile;
}

/** A grid of tiles, five to a line. There is no wrapping here, so the lines are made rather than found. */
function grid(box, rows) {
  let line = null;
  let tip = null;

  // The tooltip goes in once the line's tiles are - see tipNode for why it cannot go in first.
  const close = () => { if (line && tip) line.appendChild(tip); };

  rows.forEach((row, i) => {
    const col = i % PER_ROW;
    if (col === 0) {
      close();
      line = lineNode();
      tip = tipNode();
      box.appendChild(line);
    }
    line.appendChild(tileNode(row));

    // The line's own tooltip, filled only for the seed the pointer is on.
    if (view.tips !== false && shown && shown.id === row.id) fillTip(tip, row, col);
  });

  // The last line is short, and a stretched tile would be a different size from the rest of the grid.
  if (line) {
    for (let i = rows.length % PER_ROW; i > 0 && i < PER_ROW; i++) line.appendChild(el('div', 'tile hole'));
  }

  close();
}

function renderRows(rows) {
  const box = $('rows');
  box.replaceChildren();

  // "Any" first and always, whatever is filtered: it is how a field is cleared, not one of the options. Drawn
  // as the game draws it, a tile with a cross in it.
  if (view.none) {
    const line = lineNode();
    const tile = el('div', 'tile' + (view.none.sel ? ' sel' : ''));
    const pick = el('button', 'shot shot-any');
    pick.appendChild(el('span', 'shot-cross', 'X'));
    pick.appendChild(el('span', 'shot-name', view.none.name || 'None'));
    pick.addEventListener('click', () => s1.call('picker.pick', ''));
    tile.appendChild(pick);
    line.appendChild(tile);
    for (let i = 1; i < PER_ROW; i++) line.appendChild(el('div', 'tile hole'));
    // Nothing ever hovers the Any tile into a tooltip, but the line carries one anyway so every line is built
    // the same way and the node count is the same whatever is on screen.
    line.appendChild(tipNode());
    box.appendChild(line);
  }

  if (rows.length === 0) {
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

    // A sub-heading per tier, and only for tiers that are actually in the list: a tier nobody has reached yet
    // must not announce itself by having an empty heading.
    const tiers = [];
    for (const row of modded) if (tiers.indexOf(row.tier || 0) < 0) tiers.push(row.tier || 0);
    tiers.sort((a, b) => (sort === 6 ? b - a : a - b));

    for (const tier of tiers) {
      if (tier > 0 && tiers.length > 1) box.appendChild(el('div', 'section sub', 'TIER ' + tier));
      grid(box, modded.filter((row) => (row.tier || 0) === tier));
    }
  }
}

/* ---- render ---------------------------------------------------------------------------------------------- */

function render() {
  shell();

  $('pageR').className = 'card right' + (opened ? '' : ' shut');

  $('title').textContent = view.title || 'Select';

  const rows = visible();
  const all = (view.rows || []).length;
  $('count').textContent = rows.length === all ? String(all) : rows.length + ' of ' + all;

  renderChips();
  renderTicks();
  renderRows(rows);
  renderPreview();
}

/* ---- the opening effect ---------------------------------------------------------------------------------- */

/*
  THE CARD IS MOVED FRAME BY FRAME FROM SCRIPT, and that is not the first thing that was tried.

  The parts are all here: `transform` renders on this surface (an inline `scaleX(0.35)` folds the card on the
  spot), `transform-origin: left center` puts the hinge on its left edge, and the engine has a transition runner
  that interpolates scale. What does NOT happen is the tween. Measured with a 2400ms transition declared and a
  screenshot every 130ms: the card was folded at rest, and fully open in the first frame after the write. No
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
    // it outlives the effect - and a transform that is still there paints the card's CHILDREN through it. Seen
    // by leaving one at 0.75: the ticks and the preview drew clipped forty pixels short of the card's own
    // edge while every rect still answered the full width. `scaleX(1)` is identity and would have looked
    // right, which is exactly why it would have sat there unnoticed until something wrote a different value.
    page.style.transform = '';
    opened = true;
    render();
  }, Math.round(FLIP_MS / FLIP_STEPS));
}

/* ---- wiring ---------------------------------------------------------------------------------------------- */

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
flip();

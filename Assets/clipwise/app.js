/*
  The picker, as a page: a binder opened flat.

  Wire format is deliberately flat: `picker.view` answers one JSON object, `picker.pick` takes one item id,
  `picker.fav` stars one, `picker.back` closes without choosing. Everything else - what was typed, which tab is
  out - is the page's own business and never crosses the bridge, so a keystroke costs nothing on the C# side.

  s1.call is SYNCHRONOUS and returns a string. An empty string means no handler or a handler that threw, and
  both are states this page has to survive: it draws the empty case rather than throwing on JSON.parse.

  NO SYMBOL GLYPHS ANYWHERE. Anything outside Latin-1 comes out as a box in the game's font, so a star is a
  picture and every tab icon is a letter.

  TILES, NOT ROWS, because this replaces the game's own seed page and that page is a grid. What a row used to
  say - the parents, the tier, the effects - is on the detail card on the facing page, which stays put instead
  of following the pointer.
*/

const $ = (id) => document.getElementById(id);

const PER_ROW = 5;

let view = {
  title: 'Select', tabs: [], rows: [], none: null, added: 'ADDED BY MODS', tags: [],
  w: 940, h: 620, pageW: 420, binder: 46, edge: 14, rail: 44,
};

let query = '';

/* Which tab is out. Exactly one, because a tab is a place to be rather than a switch: the tester's list reads
   as one choice ("favorites, all, default, a-z, ...") and every entry in it answers the same question about
   the grid. The effect ticks are the independent filter and sit on the other page. */
let tab = 'all';

/* Ticked effect tags. Independent of the tab on purpose - a player who wants their calming tier-2 strains
   picks a tier tab and a tick, not one compound thing. */
let ticks = [];

/* Hidden entries are out of the list until this is on. Without it a hidden item could never be unhidden. */
let showHidden = false;

/* Has the flip run. False for exactly one render - the one the flip starts from. */
let opened = false;

/*
  THE TABS, IN THE ORDER THAT WAS ASKED FOR.

  `need` decides whether a tab is shown at all: only tabs the player has something for. Without a tier-5
  discovery there is no tier-5 tab, and with nothing starred there is no Favorites tab - a tab that filters a
  list down to nothing is a dead end with a colour on it.

  The icon is a LETTER, never a symbol. The game's TMP atlases carry Latin text and little else, so an arrow or
  a crown comes out as an empty box. The one picture is the star, which ships with the bundle.
*/
const SORT_TABS = {
  'default': 0,
  'a-z': 1,
  'yield': 2,
  'growth': 3,
  'value': 4,
  'tier up': 5,
  'tier down': 6,
};

function tabDefs(rows) {
  const tiers = [];
  for (const row of rows) {
    const t = row.tier || 0;
    if (t > 0 && tiers.indexOf(t) < 0) tiers.push(t);
  }
  tiers.sort((a, b) => a - b);

  const anyFav = rows.some((r) => r.fav);
  const anyTier = tiers.length > 0;
  const anyYield = rows.some((r) => (r.yield || 0) > 0);
  const anyGrowth = rows.some((r) => (r.growth || 0) > 0 && r.growth < 2000000000);
  const anyValue = rows.some((r) => (r.value || 0) > 0);

  const out = [];
  if (anyFav) out.push({ id: 'favorites', star: true, lines: ['Favorites'], col: '#e0a72c' });
  out.push({ id: 'all', ico: 'A', lines: ['All'], col: '#d97c2b' });
  out.push({ id: 'default', ico: 'D', lines: ['Sort:', 'default'], col: '#c04a3c' });
  out.push({ id: 'a-z', ico: 'Z', lines: ['Sort:', 'A-Z'], col: '#c04a3c' });
  if (anyYield) out.push({ id: 'yield', ico: 'Y', lines: ['Sort:', 'yield'], col: '#b8533f' });
  if (anyGrowth) out.push({ id: 'growth', ico: 'G', lines: ['Sort:', 'growth'], col: '#b8533f' });
  if (anyValue) out.push({ id: 'value', ico: 'V', lines: ['Sort:', 'value'], col: '#b8533f' });
  if (anyTier) out.push({ id: 'tier up', ico: '+', lines: ['Tier', 'up'], col: '#a4566e' });
  if (anyTier) out.push({ id: 'tier down', ico: '-', lines: ['Tier', 'down'], col: '#a4566e' });

  // A violet ramp, one step per tier the player actually has. Fixed steps rather than a gradient over the
  // tiers present, so tier 3 is the same colour on a save that has reached tier 5 and on one that has not.
  const violet = ['#7a54c0', '#8a5fc9', '#9a6bd1', '#a878d8', '#b585df'];
  for (const t of tiers) out.push({ id: 'tier ' + t, ico: String(t), lines: ['Tier', String(t)], col: violet[Math.min(t, 5) - 1] });

  return out;
}

function tierOfTab(id) {
  const m = /^tier ([0-9]+)$/.exec(id || '');
  return m ? parseInt(m[1], 10) : 0;
}

function sortOfTab(id) {
  const s = SORT_TABS[id];
  return s === undefined ? 0 : s;
}

/* ---- the wire ---------------------------------------------------------------------------------------- */

function load() {
  const raw = s1.call('picker.view');
  if (!raw) return;
  try {
    view = JSON.parse(raw);
  } catch (err) {
    console.error('picker.view was not JSON: ' + err.message);
    return;
  }

  // Opened the way it was left. The mod keeps the sort and the favourites switch because they are the
  // player's and not this page's; the tab is only how they are shown here, so it is derived rather than
  // stored twice.
  if (view.onlyFav && (view.rows || []).some((r) => r.fav)) tab = 'favorites';
  else {
    tab = 'all';
    for (const name in SORT_TABS) if (SORT_TABS[name] === (view.sort || 0) && SORT_TABS[name] !== 0) tab = name;
  }
}

/** Push the settings that outlive one open back to the mod. */
function remember() {
  // The third field is the discovered filter, which this page does not have - the mod still stores it, so it
  // is sent back unchanged rather than silently turned off for anybody using the other picker.
  s1.call('picker.state', sortOfTab(tab) + '|' + (tab === 'favorites' ? 1 : 0) + '|' + (view.onlyDisc ? 1 : 0));
}

/* ---- filtering and sorting ---------------------------------------------------------------------------- */

function visible() {
  const q = query.trim().toLowerCase();
  const tier = tierOfTab(tab);

  let rows = (view.rows || []).filter((row) => {
    // The current selection is always reachable. Filtering it away would leave the player unable to see what
    // the field is even set to.
    if (row.sel) return true;

    if (!showHidden && row.hidden) return false;
    if (tab === 'favorites' && !row.fav) return false;
    if (tier > 0 && (row.tier || 0) !== tier) return false;

    for (const tag of ticks) if ((row.tags || []).indexOf(tag) < 0) return false;

    if (!q) return true;
    return (
      (row.name || '').toLowerCase().indexOf(q) >= 0 ||
      (row.note || '').toLowerCase().indexOf(q) >= 0
    );
  });

  if (sortOfTab(tab) > 0) rows = rows.slice().sort(compare);
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
  switch (sortOfTab(tab)) {
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

/* ============================================================================================================
   THE SHELL

   The clipboard, which every screen this mod draws sits in - and which knows nothing about seeds. A second
   screen calls `shell()` and then fills `#pageL` and `#pageR`; it never lays out the board, the spiral or the
   rail, because two copies of those are two things to keep in step.
   ============================================================================================================ */

/*
  The page is told how big it is, because it cannot ask.

  A surface answers layout coordinates and nothing about the viewport, so "how wide is half of me" has no
  answer here. The mod measured vanilla's own card in `SurfacePicker.Fit` and sent the numbers along, and this
  is where they land on the boxes.
*/
const CLIP_W = 132;

function shell() {
  const pageW = Math.round(view.pageW || 420);
  const binderW = Math.round(view.binder || 46);
  const railW = Math.round(view.rail || 44);
  const h = Math.round(view.h || 620);

  $('rail').style.width = railW + 'px';
  $('pageL').style.width = pageW + 'px';
  $('pageR').style.width = pageW + 'px';
  $('binder').style.width = binderW + 'px';
  // Over the middle of the LEFT page, which now starts after the rail.
  $('clip').style.left = Math.max(0, Math.round(railW + pageW / 2 - CLIP_W / 2)) + 'px';

  // The rings, as many as the fold is tall. 7px each with a 7px gap, and a margin at both ends so the top
  // ring does not sit against the clip.
  const box = $('binder');
  const want = Math.max(6, Math.floor((h - 90) / 14));
  if (box.children.length !== want) {
    box.replaceChildren();
    for (let i = 0; i < want; i++) box.appendChild(el('div', 'ring'));
  }
}

/* ============================================================================================================
   THE SEED PICKER - one screen, inside the shell above.
   ============================================================================================================ */

/* ---- the tabs ------------------------------------------------------------------------------------------ */

function renderTabs(rows) {
  const rail = $('rail');
  rail.replaceChildren();

  const defs = tabDefs(rows);

  // The tab that was out may have stopped existing - the last favourite was unstarred, a filter emptied a
  // tier. Falling back to All is the only choice that always has something under it.
  if (!defs.some((d) => d.id === tab)) tab = 'all';

  for (const def of defs) {
    // `tab-<id>` carries no style. It exists so a test can aim at one particular tab - every tab is a `.tab`,
    // and a selector that can only say "the first one" cannot check that clicking Tier 2 filters to tier 2.
    const node = el('button', 'tab tab-' + def.id.replace(/[^a-z0-9]+/g, '-') + (def.id === tab ? ' out' : ''));
    node.style.background = def.col;

    if (def.star) {
      const img = document.createElement('img');
      img.className = 'tab-img';
      img.src = 'star.png';
      node.appendChild(img);
    } else {
      node.appendChild(el('span', 'tab-ico', def.ico));
    }

    // One box per line rather than a newline in one string: whitespace collapses in a text leaf, and
    // `white-space: pre` on a box this small would stop it wrapping anything else either.
    const txt = el('div', 'tab-txt');
    for (const line of def.lines) txt.appendChild(el('div', 'tab-l', line));
    node.appendChild(txt);

    node.addEventListener('click', () => {
      tab = def.id;
      remember();
      render();
    });

    rail.appendChild(node);
  }
}

/* ---- the effect ticks ---------------------------------------------------------------------------------- */

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

/** The effects, four to a line. There is no flex-wrap in this engine, so the lines are made rather than found. */
const TICKS_PER_ROW = 4;

function renderTicks() {
  const box = $('fx');
  box.replaceChildren();

  const all = groups();
  // The effect list is what the board is for. Any other group a mod declared is appended after it rather than
  // dropped - a mod that files "flavour" tags gets its ticks too.
  const tags = [];
  for (const [name, list] of all) {
    if (name === 'tier') continue;   // the tiers are tabs, and having them twice is a filter fighting itself
    for (const t of list) if (!tags.some((x) => x.id === t.id)) tags.push(t);
  }

  if (tags.length === 0) { box.style.display = 'none'; return; }
  box.style.display = 'flex';

  tags.sort((a, b) => (a.label || '').toLowerCase() < (b.label || '').toLowerCase() ? -1 : 1);

  let line = null;
  tags.forEach((t, i) => {
    if (i % TICKS_PER_ROW === 0) {
      line = el('div', 'fx-row');
      box.appendChild(line);
    }

    const on = ticks.indexOf(t.id) >= 0;
    const node = el('button', 'tick' + (on ? ' on' : ''));
    node.appendChild(el('span', 'tick-box', on ? 'x' : ''));
    node.appendChild(el('span', 'tick-name', t.label));
    node.addEventListener('click', () => {
      const at = ticks.indexOf(t.id);
      if (at >= 0) ticks.splice(at, 1); else ticks.push(t.id);
      render();
    });
    line.appendChild(node);
  });

  // The last line is short, and a stretched tick would be a different width from the rest.
  if (line) {
    for (let i = tags.length % TICKS_PER_ROW; i > 0 && i < TICKS_PER_ROW; i++) line.appendChild(el('div', 'tick'));
  }
}

/* ---- the detail card ----------------------------------------------------------------------------------- */

/*
  Which row the card is showing. Not bookkeeping - it is what stops the page rebuilding forever.

  Every DOM write rebuilds the page, and a rebuild destroys and recreates every box on it. The pointer has not
  moved, but the box under it is a NEW object, so uGUI raises its enter again, which fills the card, which
  writes to the DOM, which rebuilds. A probe listener counted about ten of those rounds a second with the
  pointer standing still.

  It is not only wasted work. uGUI raises a click only when the press and the release land on the SAME object,
  and while this runs the tile a player pressed is destroyed several times over before they let go - so no
  click is raised at all, nothing is picked, and nothing is logged either, since there was no click event to
  log. That is "I click a seed and the card just stays open", and it needs nothing in the way to happen.

  So a request for the row that is already showing is answered with nothing at all.
*/
let cardRow = null;

function showCard(row) {
  if (cardRow && row && cardRow.id === row.id) return;
  cardRow = row;
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
  never from a rule about what a tier ought to mean. Every clause drops out on its own when its fact is
  missing, so a vanilla seed with no parents gets a shorter line rather than a line with a hole in it.
*/
function prose(row) {
  const bits = [];
  if (row.note) bits.push('Root from ' + row.note + '.');

  const effects = (row.effects || []).length;
  if (row.tier && effects) bits.push('Tier ' + row.tier + ', which means ' + effects + (effects === 1 ? ' effect' : ' effects') + ' straight off the plant.');
  else if (row.tier) bits.push('Tier ' + row.tier + '.');
  else if (effects) bits.push(effects + (effects === 1 ? ' effect' : ' effects') + ' straight off the plant.');

  return bits.join(' ');
}

function renderCard() {
  const box = $('card');
  box.replaceChildren();

  const row = cardRow;
  if (!row) {
    box.appendChild(el('div', 'card-empty', 'Point at a seed to read it here.'));
    return;
  }

  const top = el('div', 'card-top');
  const head = el('div', 'card-head');
  head.appendChild(el('div', 'card-name', row.name || row.id));

  const line = prose(row);
  if (line) head.appendChild(el('div', 'card-prose', line));
  top.appendChild(head);

  // The item's own picture, the same one its tile carries - so the bud on the card is the bud in hand.
  const bud = document.createElement('img');
  bud.className = 'card-bud';
  bud.src = 's1://icon/' + row.id;
  top.appendChild(bud);
  box.appendChild(top);

  if (row.fav) box.appendChild(el('div', 'card-badge', 'FAVOURITE'));

  box.appendChild(el('div', 'card-rule'));

  // Who filed this entry. Only for something a mod claimed - the game's own seeds need no credit line.
  if (row.source) box.appendChild(el('div', 'card-src', String(row.source).toUpperCase()));

  fact(box, 'CROSS', row.note);
  fact(box, 'TIER', row.tier ? row.tier : '');
  if (row.effects && row.effects.length) fact(box, 'EFFECTS', row.effects.join(', ') + ' (' + row.effects.length + ')');
  fact(box, 'YIELD', row.yield ? row.yield + (row.harvest ? ' ' + row.harvest : '') : '');
  fact(box, 'GROWTH', row.growth && row.growth < 2000000000 ? row.growth + ' h' : '');
  fact(box, 'PRODUCT', row.product);
  fact(box, 'TYPE', row.drug);
  fact(box, 'VALUE', money(row.value));
  fact(box, 'BUY PRICE', money(row.buy));
  if (row.disc === false) fact(box, 'DISCOVERED', 'not yet');

  // Whatever the registering mod answered for this item just now - a discoverer's alias, a trait, anything it
  // knows and this page does not. Last, so a mod cannot push the game's own facts off the top of the card, and
  // every one of them is subject to the same rule as the rest: no value, no row.
  for (const extra of row.facts || []) fact(box, extra.k, extra.v);
}

/* ---- the grid ------------------------------------------------------------------------------------------ */

/*
  A click that lands in the list but on nothing says WHAT it landed on.

  The report this picker gets is "I click a seed and the card just stays open", and from the outside that has
  two completely different causes: the pointer reached the page and hit the wrong node, or it never reached the
  page at all. Nothing in a screenshot separates them, and neither does the pick handler - it is not called in
  either case. So the list itself listens, and SILENCE now means the pointer never arrived.
*/
$('rows').addEventListener('click', (e) => {
  const on = e && e.target ? String(e.target.className || '') : '';
  if (on.indexOf('shot-item') >= 0 || on.indexOf('star') >= 0 || on.indexOf('hide') >= 0) return;
  s1.call('picker.stray', on || '(unnamed)');
});

/** One tile: the seed's own picture and its star. Pointing at it fills the card on the facing page. */
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
  // Supplied by the mod at open time, off the LIVE item definition - which is what lets a mod that tints a
  // seed per strain have every vial on the grid look like itself.
  shot.src = 's1://icon/' + row.id;
  pick.appendChild(shot);
  pick.addEventListener('click', () => s1.call('picker.pick', row.id));
  pick.addEventListener('mouseenter', () => showCard(row));
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

  // Only while Hidden is on. A tile is sixty pixels wide and cannot carry two permanent buttons, and hiding
  // things is a tidying-up job rather than something done in passing.
  // THE BUBBLE, DRAWN BY THE RENDER THAT THE HOVER ALREADY CAUSED - not by a second write of its own.
  //
  // That is the whole of #69. The old bubble was built inside the `mouseenter` handler, which wrote to the
  // DOM, which rebuilt the page, which destroyed and recreated every tile, which raised `mouseenter` again for
  // the NEW object under a pointer that had not moved. About ten rounds a second with the mouse standing
  // still - and, far worse than the wasted work, uGUI raises a click only when the press and the release land
  // on the SAME object, so the tile a player pressed was destroyed several times before they let go and no
  // click was ever raised. Nothing appeared in the log either, because there was no click to log.
  //
  // Here the hover sets `cardRow` and asks for ONE render; this line is part of that render. A second enter
  // for the same row is refused by `showCard`, so the page settles after one rebuild and stays settled.
  if (cardRow && cardRow.id === row.id) tile.appendChild(el('div', 'tip', row.name));

  if (showHidden) {
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

function renderRows(rows) {
  const box = $('rows');
  box.replaceChildren();

  // "Any" first and always, whatever is filtered: it is how a field is cleared, not one of the options. Drawn
  // as the game draws it, a tile with a cross in it.
  if (view.none) {
    const line = el('div', 'line');
    const tile = el('div', 'tile' + (view.none.sel ? ' sel' : ''));
    const pick = el('button', 'shot shot-any');
    pick.appendChild(el('span', 'shot-cross', 'X'));
    pick.appendChild(el('span', 'shot-name', view.none.name || 'None'));
    pick.addEventListener('click', () => s1.call('picker.pick', ''));
    tile.appendChild(pick);
    line.appendChild(tile);
    for (let i = 1; i < PER_ROW; i++) line.appendChild(el('div', 'tile hole'));
    box.appendChild(line);
  }

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

    // A sub-heading per tier, and only for tiers that are actually in the list: a tier nobody has reached yet
    // must not announce itself by having an empty heading.
    const tiers = [];
    for (const row of modded) if (tiers.indexOf(row.tier || 0) < 0) tiers.push(row.tier || 0);
    tiers.sort((a, b) => (sortOfTab(tab) === 6 ? b - a : a - b));

    for (const tier of tiers) {
      if (tier > 0 && tiers.length > 1) box.appendChild(el('div', 'section sub', 'TIER ' + tier));
      grid(box, modded.filter((row) => (row.tier || 0) === tier));
    }
  }
}

/* ---- the footer ---------------------------------------------------------------------------------------- */

function renderFoot() {
  const box = $('foot');
  box.replaceChildren();

  if (view.hiddenCount || showHidden) {
    const b = el('button', 'mini' + (showHidden ? ' on' : ''), 'Hidden (' + (view.hiddenCount || 0) + ')');
    b.addEventListener('click', () => { showHidden = !showHidden; render(); });
    box.appendChild(b);
  }

  // Only when something is actually on. A button that does nothing is worse than no button.
  if (ticks.length || query || tab !== 'all') {
    const b = el('button', 'mini', 'Clear');
    b.addEventListener('click', () => {
      ticks = [];
      query = '';
      $('find').value = '';
      tab = 'all';
      remember();
      render();
    });
    box.appendChild(b);
  }
}

/* ---- render -------------------------------------------------------------------------------------------- */

function render() {
  shell();

  $('pageR').className = 'page right' + (opened ? '' : ' shut');

  const title = view.title || 'Select';
  $('title').textContent = title;
  $('title2').textContent = title;

  const rows = visible();
  const all = (view.rows || []).length;
  const count = rows.length === all ? String(all) : rows.length + ' of ' + all;
  $('count').textContent = count;
  $('count2').textContent = count;

  renderTabs(view.rows || []);
  renderTicks();
  renderRows(rows);
  renderCard();
  renderFoot();
}

/* ---- the flip ------------------------------------------------------------------------------------------ */

/*
  THE PAGE IS MOVED FRAME BY FRAME FROM SCRIPT, and that is not the first thing that was tried.

  The parts are all here: `transform` renders on this surface (an inline `scaleX(0.35)` folds the page on the
  spot), `transform-origin: left center` puts the hinge on the spine, and the engine has a transition runner
  that interpolates scale. What does NOT happen is the tween. Measured with a 2400ms transition declared and a
  screenshot every 130ms: the page was folded at rest, and fully open in the first frame after the write. No
  intermediate was ever drawn, on either route into it - a class swap or an inline write.

  So the interpolation is done here instead. Fourteen inline writes of `transform` over 300ms; each one is a
  paint-only property, so each repaints ONE box rather than rebuilding the page, which is what makes this cheap
  enough to do per frame. The easing is the cubic `ease-out` would have drawn.

  It is worth knowing the difference: a transition that does not fire costs nothing and shows nothing, so a
  page can carry one for months and read as if it animates. Only a burst of screenshots says otherwise.
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
    // Latch and draw once more. The rebuild replaces the box, which takes the inline transform with it, so
    // nothing is left holding the page at a scale after the flip is over.
    opened = true;
    render();
  }, Math.round(FLIP_MS / FLIP_STEPS));
}

/* ---- wiring -------------------------------------------------------------------------------------------- */

$('find').addEventListener('input', (e) => {
  query = e.value || '';
  render();
});

$('back').addEventListener('click', () => s1.call('picker.back'));
$('back2').addEventListener('click', () => s1.call('picker.back'));

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

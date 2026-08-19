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
  half at the edge of the clipboard.
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

  Two halves are still needed for WHEN and WHERE: `mouseenter`/`mouseleave` say which tile the pointer is on,
  and the tile's own row is what the bubble is hung inside.
*/
let tip = null;

/*
  Which row the bubble on screen belongs to. Not bookkeeping - it is what stops the page rebuilding forever.

  Every DOM write rebuilds the page, and a rebuild destroys and recreates every box on it. The pointer has not
  moved, but the box under it is a NEW object, so uGUI raises its enter again, which shows a bubble, which
  writes to the DOM, which rebuilds. A probe listener on the list counted about ten of those rounds a second
  with the pointer standing still.

  It is not only wasted work. uGUI raises a click only when the press and the release land on the SAME object,
  and while this runs the tile a player pressed is destroyed several times over before they let go - so no
  click is raised at all, nothing is picked, and nothing is logged either, since there was no click event to
  log. That is "I click a seed and the card just stays open", and it needs nothing in the way to happen.

  So a request for the row that is already showing is answered with nothing at all: the first round builds the
  bubble, the second finds it already there and writes nothing, and the loop stops after one turn.
*/
let tipRow = null;

/*
  THE FADE, AND WHY IT IS BUILT LIKE THIS.

  A bubble that lingers on purpose is a bubble that can be left behind on purpose - that was the original
  stuck-bubble bug, and a fade-out reintroduces exactly the state it came from: a node outliving the tile it
  belonged to. So the lifetime is bounded from both ends.

  At most ONE dying bubble exists, and anything that shows a new one kills it on the spot rather than waiting
  for its timer. That is the whole safety property: the only way a bubble stays on screen is if the timer never
  fires AND the pointer never moves again, and the body-level `mousemove` backstop already covers the second.

  The failure direction is right too - a mistake here loses a bubble, it does not strand one.
*/
let dying = null;
let reaper = 0;

function reap() {
  if (reaper) { clearTimeout(reaper); reaper = 0; }
  if (dying) { dying.remove(); dying = null; }
}

function hideTip() {
  // Whatever was already on its way out goes now. Two fading bubbles is the state this is written to prevent.
  reap();

  if (!tip) return;

  // Dropping the class starts the transition back to zero; the node is removed once it has finished.
  tip.className = 'bubble';
  dying = tip;
  tip = null;
  tipRow = null;
  reaper = setTimeout(reap, 160);
}

/*
  PLACED INSIDE THE ROW IT BELONGS TO, which is the only frame this page can measure.

  Everything `rect()` answers is in LAYOUT coordinates: the engine sums each box's parent-relative x and y up
  the tree, and the scroll offset is not in any of those numbers - scrolling moves a Unity transform, and the
  layout never hears about it. Measured on a padded list scrolled to the bottom, the last tile reported y=882
  while it was drawn at about y=525: out by the 357 pixels the list had been scrolled.

  So a `position: fixed` bubble cannot be placed at all, because placing one needs a VIEWPORT coordinate for
  a box the page can only describe in layout coordinates. Three ways out were checked against the running game
  and two are closed:

    - The pointer's own position. A probe listener on the list reported `clientX`, `clientY`, `offsetX`,
      `offsetY`, `normX` and `normY` ALL ZERO for every enter over this surface. Whatever the engine cannot
      convert here, it answers with zeroes rather than with an error, so a bubble placed from them lands in the
      top left corner and stays there. Not usable, and not this page's to fix.
    - The scroll offset itself. Nothing on the DOM surface reports it: no `scrollTop`, no `scrollHeight`.

  What IS exact is the DIFFERENCE between two rects inside the SAME scrolled box: both carry the same missing
  offset, so it cancels. That is the whole of the placement below - the tile's position within its own row -
  and the bubble is hung inside that row rather than over the page, so the list carries it along when it
  scrolls. Nothing left to go stale, and no viewport coordinate needed anywhere.
*/
/*
  148 AND NOT MORE, because the width decides whether the bubble can stand BESIDE its tile at all.

  A row is 380 wide and a tile 69. At 168 the middle column had room on neither side and the bubble ended up
  drawn over the very seed the pointer was on. 148 is the widest that still leaves 149 to the left of the
  middle column and 150 to the right of it, so every one of the five columns gets a bubble next to its tile
  and none of them needs to be pushed back inside the row.
*/
const TIP_WIDTH = 148;
const TIP_GAP = 6;

function showTip(line, anchor, row) {
  // Already up for this row. See `tipRow`: answering this with a rebuild is what looped the page.
  if (tip && tipRow === row) return;

  hideTip();

  tip = el('div', 'bubble');
  tip.appendChild(el('div', 'bubble-name', row.name));
  if (row.note) tip.appendChild(el('div', 'bubble-line', row.note));
  if (row.tier) tip.appendChild(el('div', 'bubble-line', 'Tier ' + row.tier));
  if (row.effects && row.effects.length) tip.appendChild(el('div', 'bubble-line', row.effects.join(', ')));

  // The tile's own place in its row, and the row's width. Both are differences or sizes rather than positions
  // on the page, so both survive the list being scrolled.
  const box = anchor.rect();
  const bar = line.rect();
  const room = bar.width;
  const at = box.x - bar.x;

  // Beside the tile, on whichever side of it the row has room for, so the seed being pointed at stays visible.
  // The last line is a guard rather than a case that happens at five to a row: a bubble that ran off the end of
  // the row would be cut in half by the list, so it is pushed back inside instead.
  let left = at + box.width + TIP_GAP;
  if (left + TIP_WIDTH > room) left = at - TIP_GAP - TIP_WIDTH;
  if (left < 0) left = Math.max(0, Math.min(at, room - TIP_WIDTH));

  tip.style.left = Math.round(left) + 'px';
  tip.style.width = TIP_WIDTH + 'px';
  line.appendChild(tip);
  tipRow = row;

  // Faded up a frame later, so the transition has a zero to start from - setting the class in the same pass
  // as the append gives the engine one style to apply and no change to animate.
  //
  // The node is captured rather than read back off `tip`: by the time this runs the pointer may already be on
  // the next tile, and turning on whatever `tip` happens to be then would light a bubble the script has
  // already replaced.
  const mine = tip;
  setTimeout(() => { if (mine === tip) mine.className = 'bubble on'; }, 0);
}

/*
  The row is handed in, not looked up, because a page has no way to walk back up from a box to its parent here.

  No listener is added anywhere new: `mouseenter` and `mouseleave` are on the tile's own button, which the
  engine wires unconditionally for being a `button`. That matters more than it reads - an element with a
  listener gets a hit target, a hit target answers every pointer interface including the wheel, and uGUI stops
  at the first one it finds, so one in the wrong place takes the list's scrolling away. Nothing here is new, so
  nothing here can.
*/
function bubble(anchor, row, line) {
  anchor.addEventListener('mouseenter', () => showTip(line, anchor, row));
  anchor.addEventListener('mouseleave', hideTip);
  // A click re-renders the grid, and the tile this belonged to is gone with it.
  anchor.addEventListener('click', hideTip);
}

/*
  The backstop.

  `mouseleave` is the right event and it does not always arrive: the pointer can leave a tile through the gap
  between two of them, or the grid can re-render under a stationary pointer. Either way the bubble outlives the
  tile it belonged to.

  So the page also takes the pointer moving anywhere that is NOT a tile as "nothing is hovered". One listener,
  and it makes a missed mouseleave cost a pixel of movement rather than a stuck bubble.

  ON THE LIST AND NOT ON `document.body`, AND THAT IS THE WHOLE MOUSE WHEEL.

  Registering any listener gives an element a hit target, and a hit target is an `EventTrigger`, which
  implements EVERY pointer interface - `IScrollHandler` included. uGUI stops at the first handler it finds, so
  a listener on the body swallowed every notch before the list underneath could see one. Sideload compensates
  by forwarding the wheel to the nearest scroll area ABOVE the element (`Interaction.PassScrollingThrough`) -
  and above the body there is nothing, so the notch died there.

  The probe says it plainly: with this on the body it reported `scroll handled by 'body/hit'`; on a page
  without it, `scroll handled by 'scroll-viewport'` and the content moves.

  The list is inside the scroll area, so the same forward finds it. And the backstop only ever needed to catch
  "the pointer moved within the list but off a tile" - leaving the list entirely is what `mouseleave` on the
  tile already handles.
*/
$('rows').addEventListener('mousemove', (e) => {
  if (!tip) return;
  const over = e && e.target ? e.target : null;
  if (over && over.className && String(over.className).indexOf('shot') >= 0) return;
  hideTip();
});

/*
  A click that lands in the list but on nothing says WHAT it landed on.

  The report this picker gets is "I click a seed and the card just stays open", and from the outside that has two
  completely different causes: the pointer reached the page and hit the wrong node, or it never reached the page
  at all. Nothing in a screenshot separates them, and neither does the pick handler - it is not called in either
  case.

  So the list itself listens. A click on a tile is answered by the tile and never gets here; anything else names
  the node that took it, and SILENCE now means the pointer never arrived, which is an answer too. Costs one line
  in the log per stray click and nothing at all in normal use.
*/
$('rows').addEventListener('click', (e) => {
  const on = e && e.target ? String(e.target.className || '') : '';
  if (on.indexOf('shot-item') >= 0 || on.indexOf('star') >= 0 || on.indexOf('hide') >= 0) return;
  s1.call('picker.stray', on || '(unnamed)');
});

/** One tile: the seed's picture, its star, and the bubble that carries the words. The row the tile goes into is
    handed in because the bubble hangs inside it - see showTip. */
function tileNode(row, line) {
  const tile = el('div', 'tile' + (row.sel ? ' sel' : ''));

  // A button, not a div: the engine wires a hit target unconditionally for button, a, input and textarea.
  //
  // `shot-item` carries no style. It exists so a test can aim at a seed: the Any tile is also a `.shot` and is
  // drawn FIRST, so `sideload_click .shot` clears the field instead of choosing anything - which reads exactly
  // like the click being broken, and cost a whole diagnosis round.
  const pick = el('button', 'shot shot-item');
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

  if (view.tips !== false) bubble(pick, row, line);
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
    line.appendChild(tileNode(row, line));
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
    const pick = el('button', 'shot shot-any');
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

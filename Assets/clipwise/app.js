/*
  The picker, as a page.

  Wire format is deliberately flat: `picker.view` answers one JSON object and `picker.pick` takes one item id.
  Everything else - which tab, what was typed - is the page's own business and never crosses the bridge, so a
  keystroke costs nothing on the C# side.

  s1.call is SYNCHRONOUS and returns a string. An empty string means no handler or a handler that threw, and
  both are states this page has to survive: it draws the empty case rather than throwing on JSON.parse.
*/

const $ = (id) => document.getElementById(id);

let view = { title: 'Pick an item', tabs: [], rows: [] };
let tab = '';
let query = '';

function load() {
  const raw = s1.call('picker.view');
  if (!raw) return;
  try {
    view = JSON.parse(raw);
  } catch (err) {
    console.error('picker.view was not JSON: ' + err.message);
    return;
  }
  if (!tab && view.tabs && view.tabs.length) tab = view.tabs[0].id;
}

function visible() {
  const q = query.trim().toLowerCase();
  return (view.rows || []).filter((row) => {
    if (tab && row.tab && row.tab !== tab) return false;
    if (!q) return true;
    return (row.name || '').toLowerCase().indexOf(q) >= 0;
  });
}

function el(kind, cls, text) {
  const node = document.createElement(kind);
  if (cls) node.className = cls;
  if (text !== undefined) node.textContent = text;
  return node;
}

function renderTabs() {
  const box = $('tabs');
  box.replaceChildren();

  for (const entry of view.tabs || []) {
    const button = el('button', 'tab' + (entry.id === tab ? ' on' : ''), entry.label);
    button.addEventListener('click', () => {
      tab = entry.id;
      render();
    });
    box.appendChild(button);
  }
}

function renderRows() {
  const box = $('rows');
  box.replaceChildren();

  const rows = visible();
  if (rows.length === 0) {
    box.appendChild(el('div', 'empty', query ? 'Nothing matches "' + query + '".' : 'Nothing to pick here.'));
    return;
  }

  let section = null;
  for (const row of rows) {
    if (row.section && row.section !== section) {
      section = row.section;
      box.appendChild(el('div', 'section', section));
    }

    // A button, not a div: the engine wires a hit target unconditionally for button, a, input and textarea.
    // A div only gets one when a state rule targets it or the script listens for click - true here, but the
    // button also gets the keyboard and the semantics for free.
    const line = el('button', 'row');
    line.appendChild(el('span', 'row-name', row.name));
    if (row.note) line.appendChild(el('span', 'row-note', row.note));
    line.addEventListener('click', () => s1.call('picker.pick', row.id));
    box.appendChild(line);
  }
}

function render() {
  $('title').textContent = view.title || 'Pick an item';
  const rows = visible();
  const all = (view.rows || []).length;
  $('count').textContent = rows.length === all ? String(all) : rows.length + ' of ' + all;
  renderTabs();
  renderRows();
}

$('find').addEventListener('input', (e) => {
  query = e.value || '';
  render();
});

// The mod says when the underlying list moved - a category registered late, an item unlocked. Reloading the
// whole view is right here: it is one call and the list is small enough that a diff would cost more to read
// than it saves.
s1.on('picker.changed', () => {
  load();
  render();
});

load();
render();

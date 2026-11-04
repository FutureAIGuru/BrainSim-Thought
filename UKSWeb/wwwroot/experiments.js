import { sandbox, request } from './sandbox.js?v=20261103-simple';

// Presentation-only draft: prompts and choices live here until their UKS
// representation is agreed. All facts and relationship expansion use the UKS API.
const facts = [
  { source: 'Fido', relationshipType: 'is', target: 'brown', caption: 'Fido is brown' },
  { source: 'Fido', relationshipType: 'has', target: 'tail', caption: 'Fido has a tail' },
  { source: 'Fido', relationshipType: 'makes', target: 'barking-sound', caption: 'Fido makes a barking sound' },
  { source: 'Fido', relationshipType: 'is-in', target: 'yard', caption: 'Fido is in the yard' },
  { source: 'Fido', relationshipType: 'chases', target: 'ball', caption: 'Fido chases a ball' },
  { source: 'Fido', relationshipType: 'plays', target: 'fetch', caption: 'Fido plays fetch' },
  { source: '[Fido->plays->fetch]', sourceFact: 5, relationshipType: 'using', target: 'frisbee', caption: 'Fido plays fetch using a frisbee', requires: [5] },
  { source: '[Fido->plays->fetch]', sourceFact: 5, relationshipType: 'basis', target: '"personal experience"', targetLabel: 'personal experience', caption: 'The play was known through personal experience', requires: [5] }
];
const steps = [
  { title: 'Things and attributes are Thoughts.', copy: 'Chapter 2 starts with Fido. Brown and tail are Thoughts too. Relationships connect them into knowledge.', choices: [0, 1], try: 'Add both facts. Hover over an edge to read [source → type → target], then tap the edge to look inside.' },
  { title: 'The Relationship type matters.', copy: 'Color, structure, sound, location, and action use the same triple. The Relationship type tells us how the Thoughts are connected.', choices: [0, 1, 2, 3, 4], try: 'Add different kinds of facts about Fido. Tap an edge to reveal its type as a Thought, alongside its source and target.' },
  { title: 'A Relationship is a Thought.', copy: 'Figure 2.3 expands [Fido → plays → fetch]. The whole Relationship is a Thought connected to its source, type, and target.', choices: [1, 5], try: 'Add Fido’s play, then tap the plays edge. The purple Thought represents [Fido → plays → fetch], not just Fido or fetch.' },
  { title: 'Relationships have attributes too.', copy: 'Figure 2.3 adds using → frisbee and basis → personal experience to the play Relationship. These describe the play itself.', choices: [5, 6, 7], try: 'Choose an attribute of the play. Tap its edge to reveal a Relationship whose source is another Relationship. Explore the nested brackets in its label.' }
];
function factNotation(index) {
  const fact = facts[index];
  const source = fact.sourceFact === undefined ? fact.source : factNotation(fact.sourceFact);
  return `[${source} → ${fact.relationshipType} → ${fact.targetLabel || fact.target}]`;
}
const $ = selector => document.querySelector(selector);
const created = new Map();
const expanded = new Map();
const promoted = new Map();
const base = new Map();
const positions = new Map();
let step = 0, graph, busy = false;

function feedback(text, error = false) {
  $('#feedback').textContent = text;
  $('#feedback').classList.toggle('error', error);
}
function controls() {
  document.querySelectorAll('#choices button, #reset, #back, #next, .progress button').forEach(button => button.disabled = busy);
  $('#back').disabled = busy || step === 0;
  $('#next').disabled = busy || step === steps.length - 1;
}
function showStep() {
  const current = steps[step];
  $('#step-number').textContent = `Experiment ${step + 1} of ${steps.length}`;
  $('#prompt-title').textContent = current.title;
  $('#prompt-copy').textContent = current.copy;
  $('#try-this').textContent = current.try;
  $('#choices').replaceChildren(...current.choices.map(index => {
    const fact = facts[index], button = document.createElement('button');
    button.className = 'choice' + (created.has(index) ? ' added' : '');
    button.textContent = `${created.has(index) ? '✓' : '+'} ${fact.caption}`;
    const hint = document.createElement('small');
    hint.textContent = factNotation(index);
    button.setAttribute('aria-label', `${created.has(index) ? 'Inspect' : 'Add'}: ${fact.caption}`);
    button.append(hint);
    button.onclick = () => run(async () => { await addFact(index); draw(true); showStep(); });
    return button;
  }));
  $('.progress').replaceChildren(...steps.map((_, index) => {
    const button = document.createElement('button');
    button.className = index <= step ? 'active' : '';
    button.setAttribute('aria-label', `Experiment ${index + 1}`);
    if (index === step) button.setAttribute('aria-current', 'step');
    button.onclick = () => { step = index; showStep(); };
    return button;
  }));
  controls();
}
async function run(action) {
  if (busy) return;
  busy = true; controls();
  try { await action(); } catch (error) { feedback(error.message, true); }
  finally { busy = false; controls(); }
}
async function snapshot(query) { return request('/api/graph?' + new URLSearchParams(query)); }
async function addFact(index) {
  if (created.has(index)) {
    const id = created.get(index);
    promoted.set(id, await snapshot({ id, expand: true }));
    $('#selection').textContent = factNotation(index);
    feedback('Relationship opened: explore its source, type, and target in the graph.');
    return;
  }
  const fact = facts[index];
  for (const prerequisite of fact.requires || []) await addFact(prerequisite);
  feedback('Adding relationship…');
  await request('/api/statements', { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ source: fact.source, relationshipType: fact.relationshipType, target: fact.target }) });
  const data = await snapshot(fact.sourceFact === undefined
    ? { label: fact.source, expand: true }
    : { id: created.get(fact.sourceFact), expand: true });
  const target = data.nodes.find(node => node.label === (fact.targetLabel || fact.target))?.id;
  const link = data.links.find(link => link.source === data.root && link.type === fact.relationshipType && link.target === target);
  if (!link) throw new Error('The fact was added, but its graph connection could not be located. Try resetting the experiment.');
  // Keep the curated graph free of initialization scaffolding; users can still
  // discover the full direct neighborhood by expanding an ordinary Thought.
  base.set(link.id, { root: data.root, nodes: data.nodes.filter(node => node.id === link.source || node.id === link.target), links: [link] });
  created.set(index, link.id);
  $('#selection').textContent = factNotation(index);
  feedback(`Added: ${fact.caption}. Your earlier facts remain available.`);
}
function draw(fit = false) {
  const nodes = new Map(), links = new Map();
  for (const data of [...base.values(), ...expanded.values(), ...promoted.values()]) {
    for (const node of data.nodes) nodes.set(node.id, { ...node });
    for (const link of data.links) links.set(link.id, { ...link });
  }
  for (const id of promoted.keys()) links.delete(id);
  for (const node of nodes.values()) {
    const preset = [...created].find(([, id]) => id === node.id);
    if (preset) node.label = factNotation(preset[0]);
    const old = positions.get(node.id);
    if (old) Object.assign(node, { x: old.x, y: old.y, z: old.z });
    node.expanded = expanded.has(node.id);
    positions.set(node.id, node);
  }
  for (const link of links.values())
    link.notation = link.structural ? link.type
      : `[${nodes.get(link.source)?.label || '?'} → ${link.type} → ${nodes.get(link.target)?.label || '?'}]`;
  graph.graphData({ nodes: [...nodes.values()], links: [...links.values()] });
  $('#empty').hidden = nodes.size > 0;
  $('#counts').textContent = `${nodes.size} visible Thoughts`;
  if (fit) graph.d3ReheatSimulation();
}
async function reveal(node) {
  if (node.isRelationship) {
    if (promoted.has(node.id)) { promoted.delete(node.id); expanded.delete(node.id); }
    else promoted.set(node.id, await snapshot({ id: node.id, expand: true }));
  } else if (expanded.has(node.id)) expanded.delete(node.id);
  else expanded.set(node.id, await snapshot({ id: node.id, expand: true }));
  $('#selection').textContent = node.isRelationship ? 'Purple nodes are relationships: Thoughts with source, type, and target.' : `${node.label} · showing direct connections only`;
  draw(true);
}
$('#back').onclick = () => { step--; showStep(); };
$('#next').onclick = () => { step++; showStep(); };
$('#fit').onclick = () => graph?.zoomToFit(400, 55);
$('#reset').onclick = () => run(async () => {
  if (!confirm('Remove the preset facts added in this experiment?')) return;
  // Reverse order removes outer statements before their embedded relationships.
  for (const [index, id] of [...created].reverse()) {
    try { await request('/api/relationships?id=' + id, { method: 'DELETE' }); }
    catch (error) { if (error.status !== 404) throw error; }
    created.delete(index); base.delete(id);
  }
  expanded.clear(); promoted.clear(); positions.clear(); step = 0;
  draw(); showStep(); feedback('Preset relationships removed. Unconnected Thoughts may remain in the store.');
  $('#selection').textContent = 'Choose a relationship on the left to begin.';
});
showStep(); busy = true; controls();
try {
  if (!await sandbox.ready) throw sandbox.error;
  if (typeof ForceGraph3D === 'undefined') throw new Error('The graph library did not load. Check your connection and reload.');
  const { default: SpriteText } = await import('https://esm.sh/three-spritetext@1.10.0');
  const container = $('#graph');
  graph = new ForceGraph3D(container, { controlType: 'orbit' })
    .backgroundColor('#0c1422').nodeRelSize(5).nodeOpacity(1)
    .nodeColor(node => node.isRelationship ? '#b79aff' : node.expanded ? '#6cdbbd' : '#80bbef')
    .nodeThreeObject(node => {
      const label = new SpriteText(node.label);
      label.color = '#e6edf7'; label.textHeight = 4; label.center.y = -0.9;
      label.backgroundColor = '#0c1422cc'; label.padding = 1;
      label.material.depthTest = false;
      return label;
    }).nodeThreeObjectExtend(true)
    .nodeLabel(node => { const text = document.createElement('span'); text.textContent = node.label; return text; })
    .linkLabel(link => { const text = document.createElement('span'); text.textContent = link.notation; return text; })
    .linkColor(link => link.structural ? '#b79aff' : '#7798b6')
    .linkOpacity(.8).linkWidth(1.6).linkDirectionalArrowLength(4).linkDirectionalArrowRelPos(.85)
    .onNodeClick(node => run(() => reveal(node)))
    .onLinkClick(link => run(async () => {
      if (link.structural) { $('#selection').textContent = `${link.type} connector · tap the connected Thought to explore`; return; }
      promoted.set(link.id, await snapshot({ id: link.id, expand: true }));
      $('#selection').textContent = 'This relationship is a Thought. Its three connectors reveal source, type, and target.';
      draw(true);
    }))
    .cooldownTicks(60).onEngineStop(() => graph.zoomToFit(400, 55));
  new ResizeObserver(() => graph.width(container.clientWidth).height(container.clientHeight)).observe(container);
  draw(); feedback('Choose any preset. You can explore freely and move on whenever you like.');
} catch (error) { feedback(error.message, true); }
finally { busy = !graph; controls(); }

const container = document.querySelector('#graph');
const status = document.querySelector('#status');
const userSelect = document.querySelector('#user-select');
const rootInput = document.querySelector('#root');
const loadButton = document.querySelector('#load');
const refreshButton = document.querySelector('#refresh');
const showLabels = document.querySelector('#show-labels');
const edgeDetail = document.querySelector('#edge-detail');
const nodes = new Map();
const links = new Map();
const neighborhoods = new Map();
const pending = new Set();
let rootId = null;
let rootSnapshot = null;
let generation = 0;
let revision = 0;
let signature = '';
let refreshing = false;
let graph;
let fitAfterLayout = false;
let makeNodeLabel;
let batchRefreshSupported = true;

const parameters = new URLSearchParams(location.search);
if (/^[0-4]$/.test(parameters.get('user') || '')) userSelect.value = parameters.get('user');
if (parameters.get('root')) rootInput.value = parameters.get('root');

function message(text, error = false) {
  status.textContent = text;
  status.classList.toggle('error', error);
}

function summary() {
  message(`Showing ${nodes.size} nodes and ${links.size} edges · ${neighborhoods.size} expanded · labels ${showLabels.checked ? 'on' : 'off'}. Auto-refresh: 2s.`);
}

function tooltip(text) {
  const label = document.createElement('span');
  label.textContent = text;
  return label;
}

function endpoint(value) { return typeof value === 'object' ? value.id : value; }

function describeEdge(link) {
  return `${nodes.get(endpoint(link.source))?.label || '?'} → ${link.type || '(unnamed type)'} → ${nodes.get(endpoint(link.target))?.label || '?'}`;
}

async function retrieve(query, user) {
  query.set('user', user);
  const response = await fetch('/api/graph?' + query, { signal: AbortSignal.timeout(15000), cache: 'no-store' });
  const data = await response.json();
  if (!response.ok) {
    const error = new Error(data.error || 'Could not load graph content.');
    error.status = response.status;
    throw error;
  }
  return data;
}

async function retrieveNeighborhoods(ids, user) {
  if (batchRefreshSupported) {
    try {
      const data = await retrieve(new URLSearchParams({ ids: ids.join(',') }), user);
      if (Array.isArray(data.snapshots) && Array.isArray(data.missingIds)) return data;
      // A previously built server can serve the new page but lack the batch API.
      batchRefreshSupported = false;
    } catch (error) {
      if (error.status !== 400 && error.status !== 404) throw error;
      batchRefreshSupported = false;
    }
  }
  const snapshots = [];
  const missingIds = [];
  for (let offset = 0; offset < ids.length; offset += 4) {
    await Promise.all(ids.slice(offset, offset + 4).map(async id => {
      try {
        snapshots.push(await retrieve(new URLSearchParams({ id, expand: 'true' }), user));
      } catch (error) {
        if (error.status === 404) missingIds.push(id);
        else throw error;
      }
    }));
  }
  return { snapshots, missingIds };
}

function render() {
  const lanes = new Map();
  for (const link of links.values()) {
    const source = endpoint(link.source);
    const target = endpoint(link.target);
    const key = JSON.stringify([Math.min(source, target), Math.max(source, target)]);
    const lane = lanes.get(key) || 0;
    link.curvature = (source === target ? .5 : .08) + lane * .18;
    lanes.set(key, lane + 1);
  }
  graph.graphData({ nodes: Array.from(nodes.values()), links: Array.from(links.values()) });
}

// Discover neighborhoods from the root outward. An expanded descendant must not
// keep its own branch visible after the ancestor that revealed it is collapsed.
function reconcile(anchor = nodes.get(rootId)) {
  const nextNodes = new Map();
  const nextLinks = new Map();
  const active = new Set();
  const queue = rootId === null ? [] : [rootId];
  const visited = new Set();
  if (rootSnapshot) {
    for (const node of rootSnapshot.nodes) if (node.id === rootId) nextNodes.set(node.id, node);
  }
  for (let index = 0; index < queue.length; index++) {
    const id = queue[index];
    if (visited.has(id)) continue;
    visited.add(id);
    const snapshot = neighborhoods.get(id);
    if (!snapshot) continue;
    active.add(id);
    for (const node of snapshot.nodes) {
      nextNodes.set(node.id, node);
      if (!visited.has(node.id)) queue.push(node.id);
    }
    for (const link of snapshot.links) {
      nextLinks.set(link.id, link);
      // A promoted edge is reachable even though its relationship Thought wasn't
      // a node in the original source/target neighborhood.
      if (!link.structural && neighborhoods.get(link.id)?.nodes.some(node => node.id === link.id && node.isRelationship))
        queue.push(link.id);
    }
  }
  for (const id of neighborhoods.keys()) if (!active.has(id)) neighborhoods.delete(id);
  for (const [id, link] of nextLinks) {
    if ((!link.structural && active.has(id) && nextNodes.get(id)?.isRelationship) ||
        !nextNodes.has(link.source) || !nextNodes.has(link.target)) nextLinks.delete(id);
  }
  const nextSignature = JSON.stringify({
    nodes: Array.from(nextNodes.values()).sort((a, b) => a.id - b.id).map(node => ({ ...node, expanded: neighborhoods.has(node.id) })),
    links: Array.from(nextLinks.values()).sort((a, b) => String(a.id).localeCompare(String(b.id)))
  });
  if (signature === nextSignature) return false;
  signature = nextSignature;
  for (const id of nodes.keys()) if (!nextNodes.has(id)) nodes.delete(id);
  for (const node of nextNodes.values()) {
    if (nodes.has(node.id)) Object.assign(nodes.get(node.id), node);
    else nodes.set(node.id, {
      ...node,
      x: (anchor?.x || 0) + (Math.random() - .5) * 30,
      y: (anchor?.y || 0) + (Math.random() - .5) * 30,
      z: (anchor?.z || 0) + (Math.random() - .5) * 30
    });
    nodes.get(node.id).expanded = neighborhoods.has(node.id);
  }
  for (const id of links.keys()) if (!nextLinks.has(id)) links.delete(id);
  for (const link of nextLinks.values()) {
    const existing = links.get(link.id);
    if (existing) Object.assign(existing, link);
    else links.set(link.id, { ...link });
  }
  edgeDetail.textContent = '';
  render();
  return true;
}

async function loadRoot(event) {
  event?.preventDefault();
  if (!graph) return;
  const version = ++generation;
  ++revision;
  const user = userSelect.value;
  const label = rootInput.value.trim();
  loadButton.disabled = true;
  nodes.clear();
  links.clear();
  neighborhoods.clear();
  pending.clear();
  signature = '';
  rootSnapshot = null;
  edgeDetail.textContent = '';
  rootId = null;
  fitAfterLayout = false;
  render();
  if (!label) {
    loadButton.disabled = false;
    message('Enter a root Thought label.');
    return;
  }
  message('Loading root…');
  document.querySelector('#editor-link').href = '/?user=' + user + '&root=' + encodeURIComponent(label);
  try {
    const data = await retrieve(new URLSearchParams({ label }), user);
    if (version !== generation) return;
    rootId = data.root;
    rootSnapshot = data;
    reconcile();
    graph.cameraPosition({ x: 0, y: 0, z: 180 }, { x: 0, y: 0, z: 0 }, 400);
    message('Root loaded. Click the gold node to reveal its immediate connections. Auto-refresh: 2s.');
  } catch (error) {
    if (version === generation) message(error.message, true);
  } finally {
    if (version === generation) loadButton.disabled = false;
  }
}

async function expand(node) {
  if (pending.has(node.id) || loadButton.disabled) return;
  ++revision; // Discard any refresh started before this user action.
  if (neighborhoods.has(node.id)) {
    neighborhoods.delete(node.id);
    fitAfterLayout = false;
    reconcile(node);
    summary();
    return;
  }
  const version = generation;
  const user = userSelect.value;
  pending.add(node.id);
  message(`Expanding ${node.label}…`);
  try {
    const data = await retrieve(new URLSearchParams({ id: node.id, expand: 'true' }), user);
    if (version !== generation) return;
    if (node.promotedFromEdge && !data.links.some(link => link.structural))
      throw new Error('Restart the web service to enable relationship-node expansion.');
    ++revision;
    neighborhoods.set(node.id, data);
    fitAfterLayout = true;
    if (!reconcile(node)) fitAfterLayout = false;
    summary();
  } catch (error) {
    if (version === generation) message(error.message, true);
  } finally {
    if (version === generation) pending.delete(node.id);
  }
}

async function expandEdge(link) {
  // These connectors describe an existing Link; they are not UKS Links themselves.
  if (link.structural) return;
  const source = nodes.get(endpoint(link.source));
  const target = nodes.get(endpoint(link.target));
  await expand({
    id: link.id, label: link.type, isRelationship: true, promotedFromEdge: true,
    x: ((source?.x || 0) + (target?.x || 0)) / 2,
    y: ((source?.y || 0) + (target?.y || 0)) / 2,
    z: ((source?.z || 0) + (target?.z || 0)) / 2
  });
}

async function refreshGraph({ automatic = false } = {}) {
  if (!graph || rootId === null || refreshing || pending.size || loadButton.disabled || (automatic && document.hidden)) return;
  refreshing = true;
  refreshButton.disabled = true;
  const version = generation;
  const startedRevision = revision;
  const user = userSelect.value;
  try {
    if (!neighborhoods.size) {
      const data = await retrieve(new URLSearchParams({ id: rootId }), user);
      if (version !== generation || startedRevision !== revision) return;
      rootSnapshot = data;
    } else {
      const ids = Array.from(neighborhoods.keys());
      const snapshots = [];
      const missing = [];
      for (let offset = 0; offset < ids.length; offset += 256) {
        const data = await retrieveNeighborhoods(ids.slice(offset, offset + 256), user);
        if (version !== generation || startedRevision !== revision) return;
        snapshots.push(...data.snapshots);
        missing.push(...data.missingIds);
      }
      if (missing.includes(rootId)) {
        rootId = null;
        rootSnapshot = null;
        neighborhoods.clear();
        reconcile();
        message('The root Thought was deleted. Enter a root and press Load root.', true);
        return;
      }
      for (const id of missing) neighborhoods.delete(id);
      for (const data of snapshots) neighborhoods.set(data.root, data);
    }
    const previousNodes = new Set(nodes.keys());
    // Fit new content so an update cannot exist outside the current camera view.
    const changed = reconcile();
    if (changed && (!automatic || Array.from(nodes.keys()).some(id => !previousNodes.has(id)))) fitAfterLayout = true;
    else if (!automatic) graph.zoomToFit(500, 65);
    if (neighborhoods.size) summary();
  } catch (error) {
    if (version !== generation || startedRevision !== revision) return;
    if (error.status === 404) {
      rootId = null;
      rootSnapshot = null;
      neighborhoods.clear();
      reconcile();
    }
    message(`${error.message} ${rootId === null ? 'Load a root to continue.' : 'Refresh will retry automatically.'}`, true);
  } finally {
    refreshing = false;
    refreshButton.disabled = false;
  }
}

document.querySelector('#root-form').addEventListener('submit', loadRoot);
userSelect.addEventListener('change', loadRoot);
document.querySelector('#fit').addEventListener('click', () => graph?.zoomToFit(500, 65));
refreshButton.addEventListener('click', () => refreshGraph());
showLabels.addEventListener('change', () => {
  if (!graph || !makeNodeLabel) return;
  // A new accessor invalidates cached objects even when the graph data is unchanged.
  graph.nodeThreeObject(showLabels.checked ? node => makeNodeLabel(node) : () => null);
  summary();
});
setInterval(() => refreshGraph({ automatic: true }), 2000);
document.addEventListener('visibilitychange', () => {
  if (!document.hidden) refreshGraph({ automatic: true });
});

try {
  if (typeof ForceGraph3D === 'undefined') throw new Error('The 3D library could not be loaded. Check your connection to cdn.jsdelivr.net and reload this page.');
  const { default: SpriteText } = await import('https://esm.sh/three-spritetext@1.10.0');
  makeNodeLabel = node => {
    const sprite = new SpriteText(node.label.length > 100 ? node.label.slice(0, 97) + '…' : node.label);
    sprite.color = '#ffffff';
    sprite.textHeight = 5;
    sprite.backgroundColor = 'rgba(11,18,32,.8)';
    sprite.padding = 1;
    sprite.center.y = -.7;
    sprite.material.depthWrite = false;
    sprite.material.depthTest = false;
    return sprite;
  };
  graph = new ForceGraph3D(container, { controlType: 'orbit' })
    .backgroundColor('#0b1220')
    .nodeLabel(node => tooltip(`${node.label} · ${node.expanded ? 'click to collapse' : 'click to expand'}`))
    .nodeColor(node => node.id === rootId ? '#f6c66d' : node.isRelationship ? '#c19bff' : node.expanded ? '#4ad4b2' : '#72b7ff')
    .nodeThreeObject(node => makeNodeLabel(node))
    .nodeThreeObjectExtend(true)
    .nodeRelSize(5)
    .nodeOpacity(1)
    .linkLabel(link => tooltip(describeEdge(link)))
    .linkColor(link => link.structural ? '#c19bff' : '#9caec7')
    .linkOpacity(.65)
    .linkWidth(link => link.structural ? .8 : 1.8)
    .linkCurvature('curvature')
    .linkDirectionalArrowLength(4)
    .linkDirectionalArrowRelPos(.85)
    .linkHoverPrecision(6)
    .onNodeClick(expand)
    .onLinkClick(expandEdge)
    .showPointerCursor(object => Boolean(object) && !object.structural)
    .onLinkHover(link => { edgeDetail.textContent = link ? describeEdge(link) : ''; })
    .warmupTicks(30)
    .cooldownTicks(90)
    .onEngineStop(() => {
      if (fitAfterLayout) {
        fitAfterLayout = false;
        graph.zoomToFit(500, 65);
      }
    });
  new ResizeObserver(() => {
    graph.width(container.clientWidth).height(container.clientHeight);
  }).observe(container);
  loadRoot();
} catch (error) {
  message(error.message || 'The 3D renderer could not start. A WebGL-capable browser is required.', true);
  loadButton.disabled = true;
  refreshButton.disabled = true;
  document.querySelector('#fit').disabled = true;
}

// Shared graph composition and presentation. Pages supply snapshots and actions;
// this module knows nothing about chapters, choices, or particular DOM element IDs.
export function composeGraph(seeds, expansions, labels = new Map(), collapsed = new Set()) {
  const nodes = new Map(), links = new Map(), active = new Set(), queue = [], groups = [];
  // A collapsed Relationship is shown as its ordinary edge. Relationships
  // attached to it are hidden from this view, never removed from the UKS.
  const definitions = new Map(), knownNodes = new Map(), knownParts = new Map();
  for (const snapshot of [...seeds,...expansions.values()]) if(snapshot) {
    for(const node of snapshot.nodes)knownNodes.set(node.id,node);
    for(const link of snapshot.links) {
      if(!link.structural)definitions.set(link.id,link);
      else {
        const id=link.type==='target'?link.source:link.target;
        if(!knownParts.has(id))knownParts.set(id,{});
        knownParts.get(id)[link.type]=link.type==='target'?link.target:link.source;
      }
    }
  }
  for(const [id,parts] of knownParts)if(!definitions.has(id) && parts.source!==undefined && parts.target!==undefined)
    definitions.set(id,{id,source:parts.source,target:parts.target,type:knownNodes.get(parts['relationship type'])?.label || '?',structural:false});
  const hidden = new Set();
  let changed=true;
  while(changed) {
    changed=false;
    for(const [id,link] of definitions)if(!hidden.has(id) &&
      (collapsed.has(link.source)||collapsed.has(link.target)||hidden.has(link.source)||hidden.has(link.target))) {
      hidden.add(id);changed=true;
    }
  }
  function include(snapshot) {
    if(hidden.has(snapshot.selectedLinkId))return;
    const visibleLinks=snapshot.links.filter(link=>link.structural
      ? !collapsed.has(link.type==='target'?link.source:link.target) && !hidden.has(link.type==='target'?link.source:link.target)
      : !hidden.has(link.id));
    // Keep an edge available to reopen a collapsed node, even when its original
    // edge was replaced by component connectors in the incoming snapshot.
    for(const node of snapshot.nodes)if(collapsed.has(node.id) && !hidden.has(node.id) && definitions.has(node.id))
      visibleLinks.push(definitions.get(node.id));
    const affected=visibleLinks.length!==snapshot.links.length || snapshot.nodes.some(node=>collapsed.has(node.id)||hidden.has(node.id));
    const required=new Set([...(snapshot.roots || (snapshot.root==null?[]:[snapshot.root])),...visibleLinks.flatMap(link=>[link.source,link.target])]);
    groups.push(...(snapshot.sequences || []));
    for (const node of snapshot.nodes) {
      if(collapsed.has(node.id)||hidden.has(node.id)||affected&&!required.has(node.id))continue;
      if (!nodes.has(node.id)) queue.push(node.id);
      nodes.set(node.id, { ...node });
    }
    // Compact edges can reference components supplied by another seed.
    for(const id of required)if(!collapsed.has(id)&&!hidden.has(id)&&!nodes.has(id)&&knownNodes.has(id)) {
      nodes.set(id,{...knownNodes.get(id)});queue.push(id);
    }
    for (const link of visibleLinks) {
      if (!links.has(link.id) && !link.structural) queue.push(link.id);
      links.set(link.id, { ...link });
    }
  }
  for (const snapshot of seeds) if (snapshot) include(snapshot);
  for (let index = 0; index < queue.length; index++) {
    const id = queue[index];
    if (collapsed.has(id) || hidden.has(id) || active.has(id) || !expansions.has(id)) continue;
    active.add(id); include(expansions.get(id));
  }
  const components = new Map();
  for (const link of links.values()) if (link.structural) {
    const relationshipId = link.type === 'target' ? link.source : link.target;
    if (!components.has(relationshipId)) components.set(relationshipId,[]);
    components.get(relationshipId).push(link);
  }
  for (const [id, link] of links) {
    if ((active.has(id) || components.has(id)) && nodes.get(id)?.isRelationship || !nodes.has(link.source) || !nodes.has(link.target)) links.delete(id);
  }
  function describe(id, seen = new Set()) {
    const node = nodes.get(id);
    if (labels.has(id)) return labels.get(id);
    if (!node?.isRelationship || seen.has(id) || seen.size >= 16) return node?.label || '?';
    const parts = components.get(id) || [];
    const source = parts.find(link => link.type === 'source')?.source;
    const type = parts.find(link => link.type === 'relationship type')?.source;
    const target = parts.find(link => link.type === 'target')?.target;
    if (source === undefined || type === undefined || target === undefined) return node.label;
    const next = new Set(seen).add(id);
    return `[${describe(source, next)} → ${describe(type, next)} → ${describe(target, next)}]`;
  }
  for (const node of nodes.values()) {
    node.label = describe(node.id);
    node.expanded = active.has(node.id);
    node.hasComponents = components.has(node.id);
  }
  const roles = new Map();
  for (const link of links.values()) {
    if (!link.structural) {
      link.notation = `[${nodes.get(link.source)?.label || '?'} → ${link.type} → ${nodes.get(link.target)?.label || '?'}]`;
      continue;
    }
    const role = link.type === 'relationship type' ? 'Type' : link.type === 'source' ? 'Source' : 'Target';
    const componentId = link.type === 'target' ? link.target : link.source;
    const relationshipId = link.type === 'target' ? link.source : link.target;
    const description = `${role}: ${nodes.get(componentId)?.label || '?'} — ${nodes.get(relationshipId)?.label || '?'}`;
    link.notation = description;
    if (!roles.has(componentId)) roles.set(componentId, []);
    roles.get(componentId).push(description);
  }
  for (const node of nodes.values()) node.description = roles.get(node.id)?.join(' · ') || node.label;
  const occupied = new Set();
  const sequences = groups.slice().sort((a, b) => b.elements.length - a.elements.length).filter(group => {
    if (group.elements.some(id => !nodes.has(id) || occupied.has(id))) return false;
    group.elements.forEach(id => occupied.add(id));
    return true;
  });
  return { nodes, links, active, sequences };
}

// A selected annotation must retain the represented Relationship's components,
// including nested Relationship endpoints, and any attached sequence unit.
export function selectRelationship(snapshot, link) {
  const ids = new Set([link.source, link.target]), selected = new Map([[link.id,link]]);
  const pending = [...ids], visited = new Set(), sequences = [];
  for (let index=0;index<pending.length;index++) {
    const id=pending[index];
    if(visited.has(id))continue;
    visited.add(id);
    for(const part of snapshot.links) {
      if(!part.structural || (part.type==='target'?part.source:part.target)!==id)continue;
      selected.set(part.id,part);
      for(const endpoint of [part.source,part.target])if(!ids.has(endpoint)){ids.add(endpoint);pending.push(endpoint);}
    }
    for(const group of snapshot.sequences || []) {
      if(!group.elements.includes(id) || sequences.includes(group))continue;
      sequences.push(group);
      for(const cell of group.elements)if(!ids.has(cell)){ids.add(cell);pending.push(cell);}
    }
  }
  for(const edge of snapshot.links)if(edge.sequence && ids.has(edge.source) && ids.has(edge.target))selected.set(edge.id,edge);
  return {selectedLinkId:link.id,nodes:snapshot.nodes.filter(node=>ids.has(node.id)),links:[...selected.values()],sequences};
}

// Fixed coordinates keep each row rigid as the surrounding graph settles.
export function arrangeSequences(nodes, sequences, previous = new Map()) {
  const rows = new Map(), members = new Map(), valueOwners = new Map(), byId = new Map(nodes.map(node => [node.id, node]));
  for (const node of nodes) if (node.sequenceFixed) {
    delete node.fx; delete node.fy; delete node.fz; delete node.sequenceFixed;
  }
  function apply(row) {
    row.cells.forEach((node, index) => {
      node.x = node.fx = row.anchor.x + index * 22;
      node.y = node.fy = row.anchor.y; node.z = node.fz = row.anchor.z;
      node.sequenceFixed = true;
    });
    for (const [id,offset] of row.values) {
      const value = byId.get(id);
      value.x = value.fx = row.anchor.x + offset.x;
      value.y = value.fy = row.anchor.y + offset.y;
      value.z = value.fz = row.anchor.z + offset.z;
      value.sequenceFixed = true;
    }
  }
  for (const group of sequences) {
    const cells = group.elements.map(id => byId.get(id)).filter(Boolean);
    if (!cells.length) continue;
    const anchor = previous.get(group.id)?.anchor || { x: cells[0].x || 0, y: cells[0].y || 0, z: cells[0].z || 0 };
    const row = { anchor, cells, values:new Map() }; rows.set(group.id, row);
    const occurrences = new Map();
    cells.forEach((node,index) => {
      members.set(node.id,row);
      const id = node.showSequenceCellLabel ? node.sequenceValue?.id : null;
      if (id == null || !byId.has(id) || valueOwners.has(id)) return;
      if (!occurrences.has(id)) occurrences.set(id,[]);
      occurrences.get(id).push(index);
    });
    let valueIndex = 0;
    for (const [id] of occurrences) {
      const x = occurrences.size > 1 ? valueIndex * (cells.length - 1) * 22 / (occurrences.size - 1) : (cells.length - 1) * 11;
      row.values.set(id,previous.get(group.id)?.values?.get(id) || {x,y:55,z:0});
      valueIndex++;
      valueOwners.set(id,row);
    }
    // Timing is a second VLU facet; keep its Thoughts in a separate nearby row.
    cells.forEach((node,index)=>{
      for(const value of node.sequenceValues||[]){
        if(!value.metadata||!byId.has(value.id)||valueOwners.has(value.id))continue;
        row.values.set(value.id,previous.get(group.id)?.values?.get(value.id)||{x:index*22,y:-45,z:0});
        valueOwners.set(value.id,row);
      }
    });
    apply(row);
  }
  return { rows, enforce() { for (const row of rows.values()) apply(row); }, move(id, delta) {
    const row = members.get(id) || valueOwners.get(id); if (!row) return;
    const position = members.has(id) ? row.anchor : row.values.get(id);
    for (const axis of ['x', 'y', 'z']) position[axis] += delta[axis] || 0;
    apply(row);
  } };
}

function graphHelp(container, supplied = {}) {
  const button = supplied.button || document.createElement('button');
  const dialog = supplied.dialog || document.createElement('dialog');
  button.innerHTML = '<img src="help-icon.svg" width="20" height="20" alt="" aria-hidden="true" style="display:block">'; button.title = 'Graph help'; button.setAttribute('aria-label', 'Graph help');
  button.setAttribute('aria-haspopup','dialog');
  if (!supplied.button) {

    Object.assign(button.style, { position: 'absolute', right: '12px', top: '12px', zIndex: 5 });
    container.parentElement.append(button);
  }
  if (!supplied.dialog) document.body.append(dialog);
  const note = dialog.querySelector('#inactivity-note');
  if (note) { note.hidden = true; dialog.after(note); }
  dialog.removeAttribute('aria-labelledby'); dialog.setAttribute('aria-label', 'Graph commands');
  Object.assign(dialog.style, { maxWidth: '480px', background: '#160c29', color: '#f3eef9', border: '1px solid #833ecf', borderRadius: '12px', padding: '24px' });
  // Shared graph Help content. Edit the HTML below; each <li> is one instruction.
  // Backticks allow this HTML template to span multiple lines.
  dialog.innerHTML = `
    <div class="help-heading"
         style="display:flex;align-items:center;justify-content:space-between;gap:12px">
      <h2 style="margin:0;font-size:19px">Graph commands</h2>
      <button type="button"
              id="close-help"
              aria-label="Close help"
              style="flex-shrink:0;font-size:22px;padding:0 9px">
        &times;
      </button>
    </div>

    <ul>
      <li>Click a node to expand or collapse its connections.</li>
      <li>Click an edge to reveal the Relationship and its source, type, and target. Component connectors do not expand.</li>
      <li>Hover over nodes or edges to inspect them in the lower left.</li>
      <li>Drag the background to rotate. Right-drag to pan. Use the mouse wheel or pinch to zoom.</li>
      <li>Drag a node to move it. Sequence elements move together as a rigid row.</li>
      <li>Use Fit view to see everything, or select another root.</li>
    </ul>
  `;
  button.onclick = () => dialog.showModal();
  dialog.querySelector('button').onclick = () => dialog.close();
}

async function createGraphView(container, {
  getRootIds = () => new Set(),
  onNodeClick = () => {}, onLinkClick = () => {}, onInspect = () => {},
  onNodeDragEnd = () => {}, onSettled = () => {}, onViewChange = () => {},
  labels = true,
  colors = {}
} = {}) {
  if (typeof ForceGraph3D === 'undefined') throw new Error('The 3D graph library did not load. Check your connection and reload.');
  const { default: SpriteText } = await import('./external/three-spritetext-1.10.0.mjs');
  const { Sprite, SpriteMaterial, CanvasTexture } = await import('./external/three.module.min.js');
  // Color the color Thoughts themselves, never sequence cells (whose displayed
  // labels may borrow the names of their VLU targets).
  const thoughtColors={red:'#db3838',orange:'#ed8b23',yellow:'#f3d840',green:'#339d55',blue:'#397ddd',purple:'#9750c6',brown:'#94603d',pink:'#ee9ebd',white:'#f5f5f5',black:'#202020',gray:'#888888'};
  const palette = { background: '#090018', text: '#f3eef9', root: '#efbd64', node: '#58adfe',
    expanded: '#62d5be', relationship: '#ba8aef', edge: '#819ac2', connector: '#b28adc', ...colors };
  let showLabels = labels;
  let hoveredNode = null, hoveredLink = null;
  const sprites = new Map();
  let layout = arrangeSequences([], []);
  function makeLabel(node) {
    const sprite = new SpriteText(node.label);
    sprite.color = palette.text; sprite.textHeight = 5; sprite.center.y = -0.8;
    sprite.backgroundColor = palette.background + 'dd'; sprite.padding = 1;
    sprite.renderOrder = 10;
    sprite.material.depthWrite = false;
    sprite.material.depthTest = false;
    // Labels can overlap edges. They must not intercept the edge's pointer hit.
    sprite.raycast = () => {};
    sprites.set(node.id, sprite);
    return sprite;
  }
  // The library's default controls handle the synthetic touch release emitted
  // after node dragging; orbit controls can treat it as a missing touch pointer.
  const graph = new ForceGraph3D(container, { controlType: 'trackball' })
    .backgroundColor(palette.background).showNavInfo(false).nodeRelSize(6).nodeOpacity(1)
    .nodeColor(node => (!node.sequenceValue && !node.isRelationship && thoughtColors[node.label]) || (getRootIds().has(node.id) ? palette.root : node.isRelationship ? palette.relationship : node.expanded ? palette.expanded : palette.node))
    .nodeThreeObject(node => showLabels && !node.sequenceValue ? makeLabel(node) : null).nodeThreeObjectExtend(true)
    .nodeLabel(() => '').linkLabel(() => '')
    .linkColor(link => link.highlighted ? '#ff7a18' : link.structural ? palette.connector : palette.edge)
    .linkOpacity(.95).linkWidth(link => link.structural ? 1.5 : 2)
    .linkCurvature(link => link.curvature || 0).linkHoverPrecision(8)
    .linkDirectionalArrowLength(9).linkDirectionalArrowRelPos(.78)
    .showPointerCursor(object => Boolean(object) && !object.structural)
    // Let the library finish its pointer/drag cleanup before a page changes
    // graph data or resizes the canvas in response to a click.
    .onNodeClick((node, event) => routeClick(event))
    .onLinkClick((link, event) => routeClick(event))
    .onBackgroundClick(event => routeClick(event))
    .onNodeHover(node => { hoveredNode = node; if (node || !hoveredLink) onInspect(node?.description || node?.label || ''); })
    .onLinkHover(link => { hoveredLink = link; if (link || !hoveredNode) onInspect(link?.notation || ''); })
    .onNodeDrag((node, delta) => layout.move(node.id, delta))
    .onNodeDragEnd(node => { layout.enforce(); onNodeDragEnd(node); })
    .onEngineTick(() => layout.enforce()).cooldownTicks(60).onEngineStop(onSettled);
  // One reusable halo follows the actual sequence cell, not its VLU target.
  // Attach it to the rendered node so dragging/rotation cannot detach it. It
  // never participates in pointer picking, and runs only during playback.
  const haloCanvas=document.createElement('canvas');haloCanvas.width=haloCanvas.height=128;
  const context=haloCanvas.getContext('2d'),gradient=context.createRadialGradient(64,64,0,64,64,64);
  gradient.addColorStop(0,'rgba(255,200,80,0)');
  gradient.addColorStop(.38,'rgba(255,200,80,0)');
  gradient.addColorStop(.48,'rgba(255,255,230,1)');
  gradient.addColorStop(.57,'rgba(255,185,50,.85)');
  gradient.addColorStop(1,'rgba(255,150,30,0)');
  context.fillStyle=gradient;context.fillRect(0,0,128,128);
  const halo=new Sprite(new SpriteMaterial({map:new CanvasTexture(haloCanvas),transparent:true,depthTest:false,depthWrite:false}));
  halo.scale.set(34,34,1);halo.renderOrder=9;halo.raycast=()=>{};
  let playbackId=null,haloFrame;
  function setPlayback(id) {
    playbackId=id;cancelAnimationFrame(haloFrame);halo.removeFromParent();
    if(id===null)return;
    const follow=()=>{
      const node=graph.graphData().nodes.find(node=>node.id===playbackId);
      const object=node?.__threeObj;
      if(object&&halo.parent!==object){halo.removeFromParent();object.add(halo);}
      halo.visible=!!object;
      haloFrame=requestAnimationFrame(follow);
    };
    follow();
  }
  graph.controls().addEventListener('start', onViewChange);
  const centeringForce = graph.d3Force('center');
  const ordinaryLinkStrength = graph.d3Force('link').strength();
  graph.d3Force('link').distance(link => link.sequenceReferrer ? 40 : 65)
    .strength(link => link.sequenceReferrer ? 1 : ordinaryLinkStrength(link));
  graph.d3Force('charge').strength(-110);

  function routeClick(event) {
    // The library dispatches clicks against its last rendered hover target.
    // A quick move-and-click can otherwise expand the previous node, or be
    // treated as background. Let the next frame resolve the current pointer.
    requestAnimationFrame(() => {
      if (hoveredNode) onNodeClick(hoveredNode, event);
      else if (hoveredLink) onLinkClick(hoveredLink, event);
    });
  }

  function fit(duration = 400, padding = 65) {
    const nodes = graph.graphData().nodes.filter(node => Number.isFinite(node.x) && Number.isFinite(node.y) && Number.isFinite(node.z));
    if (!nodes.length) return;
    const bounds = axis => [Math.min(...nodes.map(node => node[axis])), Math.max(...nodes.map(node => node[axis]))];
    const [xmin, xmax] = bounds('x'), [ymin, ymax] = bounds('y'), [zmin, zmax] = bounds('z');
    const center = { x: (xmin + xmax) / 2, y: (ymin + ymax) / 2, z: (zmin + zmax) / 2 };
    const aspect = Math.max(.2, container.clientWidth / Math.max(1, container.clientHeight));
    const halfFov = (graph.camera().fov || 50) * Math.PI / 360;
    const labelWidth = showLabels ? Math.max(...nodes.map(node => node.label.length * 2.8), 20) : 20;
    let distance, direction = { x: 0, y: 0, z: 1 };
    if (nodes.length === 1) {
      // Node centers have zero extent here. Keep both sphere and label readable.
      distance = Math.max(200, labelWidth / (2 * Math.tan(halfFov) * aspect) * 1.4);
    } else {
      // Fit numerical node positions, not rendered meshes: the mesh update may
      // still be pending immediately after graphData changes on a fresh page.
      const minimumSide = Math.max(1, Math.min(container.clientWidth, container.clientHeight));
      const usable = Math.max(.5, 1 - 2 * Math.min(padding, minimumSide / 4) / minimumSide);
      const camera = graph.camera().position, target = graph.controls().target;
      const delta = { x: camera.x - target.x, y: camera.y - target.y, z: camera.z - target.z };
      const length = Math.hypot(delta.x, delta.y, delta.z);
      if (Number.isFinite(length) && length > 0) direction = { x: delta.x / length, y: delta.y / length, z: delta.z / length };
      // Fit the actual camera-facing extents. A bounding sphere around the
      // entire 3D diagonal wastes space, particularly in a wide graph pane.
      graph.camera().updateMatrixWorld();
      const basis = graph.camera().matrixWorld.elements;
      const vertical = Math.tan(halfFov) * usable, horizontal = vertical * aspect;
      distance = Math.max(200, ...nodes.map(node => {
        const x = node.x - center.x, y = node.y - center.y, z = node.z - center.z;
        const side = x * basis[0] + y * basis[1] + z * basis[2];
        const up = x * basis[4] + y * basis[5] + z * basis[6];
        const depth = x * direction.x + y * direction.y + z * direction.z;
        const text = showLabels ? Math.min(node.label.length * 1.4, 100) : 8;
        return depth + Math.max((Math.abs(side) + text + 8) / horizontal, (Math.abs(up) + 16) / vertical);
      }));
    }
    graph.cameraPosition({ x: center.x + direction.x * distance, y: center.y + direction.y * distance, z: center.z + direction.z * distance }, center, duration);
  }
  const observer = new ResizeObserver(() => {
    graph.width(container.clientWidth).height(container.clientHeight);
    if (graph.graphData().nodes.length === 1) fit(0);
  });
  observer.observe(container);
  return {
    graph, fit, setPlayback,
    cameraState() {
      const position=graph.camera().position, target=graph.controls().target, up=graph.camera().up;
      return {position:{x:position.x,y:position.y,z:position.z},target:{x:target.x,y:target.y,z:target.z},up:{x:up.x,y:up.y,z:up.z}};
    },
    restoreCamera(state) {
      const valid=value=>value && ['x','y','z'].every(axis=>Number.isFinite(value[axis]));
      if (!valid(state?.position) || !valid(state?.target)) return;
      if (valid(state.up)) graph.camera().up.set(state.up.x,state.up.y,state.up.z);
      graph.cameraPosition(state.position,state.target,0);
    },
    resetLayout() { layout=arrangeSequences(graph.graphData().nodes,[]); },
    setData(data) {
      const lanes = new Map();
      const sequenceStarts = new Set((data.sequences || []).map(row => row.elements[0]));
      const sequenceCells = new Set((data.sequences || []).flatMap(row => row.elements));
      for (const link of data.links) {
        const source = typeof link.source === 'object' ? link.source.id : link.source;
        const target = typeof link.target === 'object' ? link.target.id : link.target;
        link.sequenceReferrer = sequenceStarts.has(target) && !sequenceCells.has(source);
        const key = JSON.stringify([Math.min(source, target), Math.max(source, target)]);
        const lane = lanes.get(key) || 0;
        link.curvature = source === target ? .5 + lane * .18 : lane * .18;
        lanes.set(key, lane + 1);
      }
      const ids = new Set(data.nodes.map(node => node.id));
      for (const id of sprites.keys()) if (!ids.has(id)) sprites.delete(id);
      for (const node of data.nodes) {
        const sprite = sprites.get(node.id);
        if (sprite && sprite.text !== node.label) sprite.text = node.label;
      }
      layout = arrangeSequences(data.nodes, data.sequences || [], layout.rows);
      // A fixed row cannot follow the centering force. Letting that force move
      // only free nodes pushes the referrer far away to balance the row's mass.
      graph.d3Force('center', layout.rows.size ? null : centeringForce);
      graph.graphData({ nodes: data.nodes, links: data.links });
    },
    setLabels(visible) {
      showLabels = visible;
      sprites.clear();
      graph.nodeThreeObject(node => showLabels && !node.sequenceValue ? makeLabel(node) : null);
      if (graph.graphData().nodes.length === 1) fit();
    }
  };
}

// Reusable UKS explorer: owns graph snapshots, root selection, expansion,
// refresh, layout state, and rendering. Demo engines only supply preset seeds.
export async function createUKSGraph(container, {
  request, root = 'Thought', colors, labels = true, help, alwaysShowSequenceValues = false,
  perform = action => action(), onChange = () => {}, onInspect = () => {},
  onRoot = () => {}, onError = () => {}, onDrag = () => {}
}) {
  const expansions = new Map(), positions = new Map(), pending = new Set(), collapsed = new Set(), collapsedSnapshots = new Map();
  let seeds = [], aliases = new Map(), rootSnapshot = null, rootId = null;
  let rootIds = new Set(), expandRoots = false, showSequenceValues = false, evidenceSnapshot = null, highlightedLinks = new Set();
  let highlightUntil = 0, highlightTimer;
  graphHelp(container, help);
  let rootLabel = root, customRoot = false, signature = '', generation = 0, fitPending = false;
  let view;
  async function read(query, automatic = false) {
    // === WEB SERVICE: GET /api/graph — retrieve graph nodes, Relationships, and VLU/NXT sequence structure for the requested roots. ===
    const parameters=new URLSearchParams(query);
    if(alwaysShowSequenceValues)parameters.set('sequenceValues','true');
    return request('/api/graph?' + parameters, { automatic, signal: AbortSignal.timeout(15000) });
  }
  async function dispatch(action) {
    try { await perform(action); } catch (error) { onError(error); }
  }
  function render(fit = false) {
    const inputs = [...(rootSnapshot ? [rootSnapshot] : []), ...(evidenceSnapshot ? [evidenceSnapshot] : []), ...collapsedSnapshots.values(), ...(!customRoot ? seeds : [])];
    const { nodes, links, active, sequences } = composeGraph(inputs, expansions, aliases, collapsed);
    for (const id of expansions.keys()) if (!active.has(id)) expansions.delete(id);
    for (const link of links.values()) link.highlighted = highlightedLinks.has(link.id) ||
      link.structural && (highlightedLinks.has(link.source) || highlightedLinks.has(link.target));
    const nextSignature = JSON.stringify({ roots: [...rootIds], sequences, nodes: [...nodes.values()], links: [...links.values()] });
    if (nextSignature !== signature) {
      signature = nextSignature;
      let index = 0;
      const rendered = [...nodes.values()].map(node => {
        let existing = positions.get(node.id);
        if (existing) Object.assign(existing, node);
        else {
          const angle = index / Math.max(2, nodes.size) * Math.PI * 2;
          existing = { ...node, x: Math.cos(angle) * 75, y: Math.sin(angle) * 75, z: 0 };
        }
        index++; return existing;
      });
      positions.clear(); for (const node of rendered) positions.set(node.id, node);
      view.setData({ nodes: rendered, links: [...links.values()], sequences });
      if (fit) { fitPending = true; view.graph.d3ReheatSimulation(); }
    }
    if (fit) view.fit(350);
    onChange({ nodes: [...nodes.values()], nodeCount: nodes.size, edgeCount: links.size, expandedCount: active.size, rootId });
  }
  async function expand(id, expectedRelationship = false) {
    if (pending.has(id)) return false;
    const version = generation;
    pending.add(id);
    try {
      const data = await read({ id, expand: true });
      if (version !== generation) return false;
      if (expectedRelationship && (!data.nodes.find(node => node.id === id)?.isRelationship || !data.links.some(link => link.structural)))
        throw new Error('The selected edge did not resolve to a Relationship Thought.');
      collapsed.delete(id);collapsedSnapshots.delete(id);
      expansions.set(id, data);
      render(true);
    } finally { pending.delete(id); }
  }
  async function toggleNode(node) {
    onInspect(node.description || node.label);
    if (node.isRelationship && node.hasComponents) {
      collapsed.add(node.id);
      if(expansions.has(node.id))collapsedSnapshots.set(node.id,expansions.get(node.id));
      expansions.delete(node.id);render(true);
    } else if (expansions.has(node.id)) { expansions.delete(node.id); render(true); }
    else return expand(node.id, node.isRelationship);
  }
  async function toggleEdge(link) {
    onInspect(link.notation);
    if (link.structural) return false;
    return expand(link.id, true);
  }
  view = await createGraphView(container, {
    getRootIds: () => rootIds, colors, labels,
    onNodeClick: node => dispatch(() => toggleNode(node)),
    onLinkClick: link => dispatch(() => toggleEdge(link)),
    onInspect, onNodeDragEnd: onDrag,
    onViewChange: () => { fitPending = false; },
    onSettled: () => { if (fitPending) { fitPending = false; view?.fit(); } }
  });
  return {
    read,
    getState() {
      return {roots:[...rootIds],rootLabel,customRoot,expandRoots,showSequenceValues,expanded:[...expansions.keys()],collapsed:[...collapsed],
        positions:[...positions.values()].map(({id,x,y,z})=>({id,x,y,z})),camera:view.cameraState()};
    },
    async restoreState(state) {
      if (!state || !Array.isArray(state.roots)) return;
      const version=++generation;
      const ids=state.roots.filter(Number.isSafeInteger).slice(0,256);
      const data=await read({roots:ids.join(','),expand:!!state.expandRoots,sequenceValues:!!state.showSequenceValues});
      if (version!==generation) return;
      rootSnapshot=data; rootIds=new Set(data.roots); rootId=data.root;
      rootLabel=typeof state.rootLabel==='string'?state.rootLabel:rootLabel;
      customRoot=!!state.customRoot; expandRoots=!!state.expandRoots; showSequenceValues=!!state.showSequenceValues;
      expansions.clear();collapsed.clear();collapsedSnapshots.clear();
      for(const id of Array.isArray(state.collapsed)?state.collapsed:[])if(Number.isSafeInteger(id))collapsed.add(id);
      const expanded=[...new Set([...(Array.isArray(state.expanded)?state.expanded.filter(Number.isSafeInteger):[]),...collapsed])];
      for(let offset=0;offset<expanded.length;offset+=256) {
        const packet=await read({ids:expanded.slice(offset,offset+256).join(','),sequenceValues:showSequenceValues});
        if(version!==generation)return;
        for(const snapshot of packet.snapshots)(collapsed.has(snapshot.root)?collapsedSnapshots:expansions).set(snapshot.root,snapshot);
      }
      view.resetLayout(); positions.clear();
      for(const node of Array.isArray(state.positions)?state.positions:[]) {
        if(Number.isSafeInteger(node.id)&&['x','y','z'].every(axis=>Number.isFinite(node[axis])))positions.set(node.id,{...node});
      }
      fitPending=false; signature=''; render(); view.restoreCamera(state.camera);
      if(rootIds.size===1)onRoot(rootLabel);
    },
    async loadRoot(label, { showPresets = false } = {}) {
      if (!label.trim()) return false;
      const version = ++generation;
      const data = await read({ label: label.trim() });
      if (version !== generation) return false;
      expandRoots = false; showSequenceValues = false; evidenceSnapshot = null; highlightedLinks.clear();
      rootLabel = label.trim(); rootId = data.root; rootIds = new Set(data.roots || [data.root]); rootSnapshot = data;
      customRoot = !showPresets; expansions.clear(); collapsed.clear(); collapsedSnapshots.clear();
      onRoot(rootLabel); onInspect('');
      render(true);
    },
    async loadThoughts(values, { expand = false, sequenceValues = false } = {}) {
      const version = ++generation;
      const data = !values.length ? {roots:[],root:null,nodes:[],links:[],sequences:[]}
        : await read(typeof values[0] === 'number' ? {roots:values.join(','), expand, sequenceValues} : [...values.map(label => ['labels',label]), ['expand',String(expand)], ['sequenceValues',String(sequenceValues)]]);
      if (version !== generation) return false;
      expandRoots = expand; showSequenceValues = sequenceValues; evidenceSnapshot = null; highlightedLinks.clear();
      rootSnapshot = data; rootId = data.root; rootIds = new Set(data.roots);
      customRoot = true; expansions.clear(); collapsed.clear(); collapsedSnapshots.clear();
      if (data.roots.length === 1) {
        rootLabel = data.nodes.find(node => node.id === data.root)?.label || rootLabel;
        onRoot(rootLabel); onInspect('');
      }
      render(true);
    },
    async setEvidence(attributes = [], {fit = false, automatic = false} = {}) {
      const version = generation;
      const ids = [...new Set(attributes.flatMap(row => row.evidenceThoughtIds || []))];
      const selectedLinks = new Set(attributes.flatMap(row => row.evidenceLinkIds || []));
      const packets = [];
      for (let offset = 0; offset < ids.length; offset += 256) packets.push(await read({roots:ids.slice(offset,offset+256).join(','),expand:true}, automatic));
      if (version !== generation) return;
      const links = packets.flatMap(packet => packet.links).filter(link => selectedLinks.has(link.id));
      const endpoints = new Set(links.flatMap(link => [link.source,link.target]));
      evidenceSnapshot = {nodes:packets.flatMap(packet => packet.nodes).filter(node => endpoints.has(node.id)),links};
      if (!automatic) {
        highlightUntil = Date.now() + 2000;
        clearTimeout(highlightTimer);
        highlightTimer = setTimeout(() => { highlightedLinks.clear(); render(); },2000);
      }
      highlightedLinks = Date.now() < highlightUntil ? selectedLinks : new Set();
      render(fit);
    },
    setPresets(snapshots, presetLabels = new Map(), { show = false, fit = false } = {}) {
      if(show) {
        const existing=new Set(seeds.map(snapshot=>snapshot.selectedLinkId));
        for(const snapshot of snapshots)if(snapshot.selectedLinkId!=null && !existing.has(snapshot.selectedLinkId))
          for(const link of snapshot.links)if(link.structural){const id=link.type==='target'?link.source:link.target;collapsed.delete(id);collapsedSnapshots.delete(id);}
      }
      seeds = snapshots; aliases = presetLabels;
      if (show) customRoot = false;
      const rootNode = seeds.flatMap(snapshot => snapshot.nodes).find(node => node.label === rootLabel);
      if (!customRoot && rootNode && rootIds.size <= 1) { rootId = rootNode.id; rootIds = new Set([rootId]);
        if (!rootSnapshot?.nodes.some(node => node.id === rootId)) rootSnapshot = { nodes: [rootNode], links: [] }; }
      render(fit);
    },
    async refresh({ automatic = false, fit = false } = {}) {
      if (pending.size) return;
      const version = generation;
      if (rootIds.size) {
        const data = await read({roots:[...rootIds].join(','), expand: expandRoots, sequenceValues: showSequenceValues}, automatic);
        if (version !== generation) return;
        rootSnapshot = data; rootId = data.root; rootIds = new Set(data.roots);
      }
      const ids = [...new Set([...expansions.keys(),...collapsedSnapshots.keys()])];
      for (let offset = 0; offset < ids.length; offset += 256) {
        const data = await read({ ids: ids.slice(offset, offset + 256).join(','), sequenceValues: showSequenceValues }, automatic);
        if (version !== generation) return;
        for (const id of data.missingIds) {
          expansions.delete(id);collapsedSnapshots.delete(id);collapsed.delete(id);

        }
        for (const snapshot of data.snapshots) {
          if (expansions.has(snapshot.root)) expansions.set(snapshot.root, snapshot);
          if (collapsedSnapshots.has(snapshot.root)) collapsedSnapshots.set(snapshot.root,snapshot);

        }
      }
      render(fit);
    },
    setPlayback: id => view.setPlayback(id),
    fit: () => view.fit(),
    setLabels: visible => view.setLabels(visible)
  };
}

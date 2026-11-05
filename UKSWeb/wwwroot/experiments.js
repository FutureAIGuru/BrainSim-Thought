import { sandbox, request } from './sandbox.js?v=20261105-resume';
import { createUKSGraph, selectRelationship } from './uks-graph.js?v=20261105-collapse3';
import { createQueryPanel } from './uks-query.js?v=20261105-resume';
import { createSequencePanel } from './uks-sequences.js?v=20261105-resume';
import { createDemoComponents } from './demo-components.js?v=20261105-components';

// Common guided-demo engine. A page supplies facts, steps, and its HTML layout.
// No chapter-specific content, graph rendering, or expansion state lives here.
export async function startExperiment({ facts, steps, colors, query, sequence, initialFacts = [], seedIfMissing, seedGroups = [] }) {
  const $ = selector => document.querySelector(selector);
  const created = new Map(), completed = new Set();
  const componentChoices = steps.some(item=>(item.choices || []).some(entry=>entry && typeof entry==='object' && entry.component));
  const rootInput = $('#root');
  let step = 0, graph, queryPanel, busy = true, selection = '', refreshTask = null;
  const parameters = new URLSearchParams(location.search);
  if (parameters.get('root')) rootInput.value = parameters.get('root');
  let stateReady=false, savedState=null, saveTimer;
  const stateKey=()=>`uks-demo-state:1:${sandbox.id}:${location.pathname}`;
  function saveState() {
    if(!stateReady || !sandbox.available || !graph)return;
    try {
      sessionStorage.setItem(stateKey(),JSON.stringify({version:1,step,completed:[...completed],
        urlRoot:new URL(location.href).searchParams.get('root'),selection,
        panel:queryPanel?.getState(),graph:graph.getState(),
        scrolls:Object.fromEntries(['.action-zone','.prompt-content','.invitation','#query-results'].map(selector=>[selector,$(selector)?.scrollTop||0]))}));
    } catch { /* Storage can be disabled or full; the demo still works. */ }
  }
  function scheduleSave() {clearTimeout(saveTimer);saveTimer=setTimeout(saveState,100);}


  function notation(index, input = false, path = new Set()) {
    if (path.has(index) || !facts[index]) throw new Error('A fact has a missing or circular sourceFact reference.');
    const next = new Set(path).add(index), fact = facts[index];
    const source = fact.sourceFact === undefined ? fact.source : notation(fact.sourceFact, input, next);
    return input ? `[${source}->${fact.relationshipType}->${fact.target}]`
      : `[${source} → ${fact.relationshipType} → ${targetLabel(fact)}]`;
  }
  function targetLabel(fact) {
    if (fact.targetLabel) return fact.targetLabel;
    return fact.target.startsWith('"') ? JSON.parse(fact.target) : fact.target;
  }
  function feedback(text, error = false) {
    $('#feedback').textContent = text;
    $('#feedback').classList.toggle('error', error);
  }
  function inspect(text = '') { $('#selection').textContent = text || selection; }
  function controls() {
    const unavailable = busy || !graph || !sandbox.available;
    queryPanel?.setBusy(unavailable);
    $('#reset-content').disabled = busy;
    document.querySelectorAll('#choices button, #load-root').forEach(button => button.disabled = unavailable);
    $('#back').disabled = busy || step === 0;
    const lastStep = step === steps.length - 1;
    $('#next').textContent = lastStep ? 'More demos' : 'Next →';
    $('#next').disabled = busy || (!lastStep && (unavailable || !completed.has(step)));
    $('#next').title = lastStep ? 'Explore more demos' : completed.has(step) ? 'Next experiment' : 'Add, remove, or explore something first';
    document.querySelectorAll('.progress button').forEach((button, index) => {
      button.disabled = busy || index > 0 && !completed.has(index - 1);
    });
  }
  function actionIcon(remove) {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 24 24'); svg.setAttribute('class', 'action-icon'); svg.setAttribute('aria-hidden', 'true');
    const path = document.createElementNS(svg.namespaceURI, 'path');
    path.setAttribute('d', remove ? 'M5 12h14' : 'M12 5v14M5 12h14');
    path.setAttribute('fill', 'none'); path.setAttribute('stroke', 'currentColor');
    path.setAttribute('stroke-width', '1.7'); path.setAttribute('stroke-linecap', 'round');
    svg.append(path); return svg;
  }
  function showStep() {
    const actionZone = $('.action-zone') || $('#choices');
    const current = steps[step], scrollTop = actionZone.scrollTop;
    queryPanel?.showStep(current.query || current.sequence);
    $('#step-number').textContent = `Experiment ${step + 1} of ${steps.length}`;
    // These fields are trusted author-written HTML from the experiment page.
    // UKS labels, user input, feedback and results continue to use textContent.
    $('#prompt-title').innerHTML = current.title || '';
    $('#prompt-copy').innerHTML = current.copy || '';
    $('#try-this').innerHTML = current.try || '';
    const choiceKey=entry=>typeof entry==='number'?`fact:${entry}`:entry.id || `component:${entry.component}`;
    const offeredChoices = new Map();
    for (const item of steps.slice(0, step + 1)) {
      if (item.inheritChoices === false) offeredChoices.clear();
      for (const entry of item.choices || []) offeredChoices.set(choiceKey(entry),entry);
      for (const entry of item.removeChoices || []) {
        const key=typeof entry==='number'?`fact:${entry}`:entry;
        offeredChoices.delete(key);offeredChoices.delete(`component:${key}`);
      }
    }
    const choices = [...offeredChoices.values()].filter(entry => typeof entry!=='number' ||
      current.query !== 'exceptions' || facts[entry].source === queryPanel?.subject);
    $('#choices').replaceChildren(...choices.map(index => {
      if(typeof index!=='number') {
        if(queryPanel?.createComponent)return queryPanel.createComponent(index);
        const pending=document.createElement('div');pending.textContent='Loading…';return pending;
      }
      const fact = facts[index], added = created.has(index), button = document.createElement('button');
      button.className = 'choice' + (added ? ' added' : '');
      button.setAttribute('aria-label', `${added ? 'Delete' : 'Add'}: ${fact.caption}`);
      button.title = `${added ? 'Delete' : 'Add'} this Relationship`;
      const label = document.createElement('span'); label.textContent = fact.caption;
      button.append(label, actionIcon(added));
      button.onclick = () => run(async () => {
        const removing = created.has(index);
        try {
          if (removing) {
            try { await request('/api/relationships?id=' + created.get(index), { method: 'DELETE' }); }
            catch (error) { if (error.status !== 404 || error.code === 'sandbox_unavailable') throw error; }
          } else await addFact(index);
        } finally { await synchronize({ show: !removing && !query && !sequence, fit: true }); showStep(); }
        await queryPanel?.refresh();
        selection = removing ? '' : notation(index); inspect();
        feedback(`${removing ? 'Removed' : 'Added'}: ${fact.caption}.`);
      });
      return button;
    }));
    actionZone.scrollTop = scrollTop;
    $('.progress').replaceChildren(...steps.map((_, index) => {
      const button = document.createElement('button');
      button.className = index <= step ? 'active' : '';
      button.setAttribute('aria-label', `Experiment ${index + 1}`);
      if (index === step) button.setAttribute('aria-current', 'step');
      button.onclick = () => moveStep(index);
      return button;
    }));
    controls();
  }
  function moveStep(index) {
    if (busy || index < 0 || index >= steps.length || index > 0 && !completed.has(index - 1)) return;
    step = index; ($('.action-zone') || $('#choices')).scrollTop = 0; showStep(); feedback('');
    if (queryPanel) run(() => queryPanel.activate());
    saveState();
  }
  async function run(action) {
    if (refreshTask) await refreshTask;
    if (busy || !graph) return false;
    const actionStep = step;
    busy = true; controls();
    try { if (await action() !== false) completed.add(actionStep); }
    catch (error) { feedback(error.message, true); }
    finally { busy = false; controls(); saveState(); }
  }
  function findFact(index, data) {
    const fact = facts[index];
    const target = data.nodes.find(node => node.label === targetLabel(fact))?.id;
    return data.links.find(link => !link.structural && link.source === data.root && link.type === fact.relationshipType && link.target === target);
  }
  async function addFact(index, path = new Set()) {
    if (created.has(index)) return;
    if (path.has(index)) throw new Error('A fact has circular prerequisites.');
    const next = new Set(path).add(index), fact = facts[index];
    const prerequisites = [...(fact.requires || []), ...(fact.sourceFact === undefined ? [] : [fact.sourceFact])];
    for (const prerequisite of new Set(prerequisites)) await addFact(prerequisite, next);
    const source = fact.sourceFact === undefined ? fact.source : notation(fact.sourceFact, true);
    await request('/api/statements', { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ source, relationshipType: fact.relationshipType, target: fact.target }) });
    const data = await graph.read(fact.sourceFact === undefined ? { label: fact.source, expand: true } : { id: created.get(fact.sourceFact), expand: true });
    const link = findFact(index, data);
    if (!link) throw new Error('The fact was stored, but its connection could not be displayed. Reload the page to synchronize.');
    created.set(index, link.id);
  }
  // Check actual UKS membership, so reloads and edits from another tab update
  // choice buttons. The graph component handles its own exploration/refresh.
  async function synchronize({ automatic = false, show = false, fit = false } = {}) {
    const cache = new Map(), found = new Map(), checked = new Set(), path = new Set();
    async function resolve(index) {
      if (checked.has(index)) return;
      if (path.has(index)) throw new Error('A fact has a circular sourceFact reference.');
      path.add(index);
      const fact = facts[index];
      if (fact.sourceFact !== undefined) await resolve(fact.sourceFact);
      if (fact.sourceFact === undefined || found.has(fact.sourceFact)) {
        const query = fact.sourceFact === undefined ? { label: fact.source, expand: true } : { id: found.get(fact.sourceFact).link.id, expand: true };
        const key = JSON.stringify(query);
        if (!cache.has(key)) {
          try { cache.set(key, await graph.read(query, automatic)); }
          catch (error) { if (error.status !== 404 || error.code === 'sandbox_unavailable') throw error; cache.set(key, null); }
        }
        const data = cache.get(key), link = data && findFact(index, data);
        if (link) found.set(index, { data, link });
      }
      path.delete(index); checked.add(index);
    }
    for (let index = 0; index < facts.length; index++) await resolve(index);
    created.clear(); for (const [index, { link }] of found) created.set(index, link.id);
    await graph.refresh({ automatic });
    const seeds = [...found.values()].map(({ data, link }) => selectRelationship(data, link));
    graph.setPresets(seeds, new Map([...created].map(([index, id]) => [id, notation(index)])), { show, fit });
  }
  function updateGraphControls({ nodes, nodeCount }) {
    $('#counts').textContent = `${nodeCount} visible`;
    const labels = new Set([rootInput.value, 'Thought', ...facts.flatMap(fact => fact.sourceFact === undefined ? [fact.source, targetLabel(fact)] : [targetLabel(fact)]), ...nodes.filter(node => !node.isRelationship).map(node => node.label)]);
    $('#root-options').replaceChildren(...[...labels].map(label => { const option = document.createElement('option'); option.value = label; return option; }));
  }
  $('#back').onclick = () => moveStep(step - 1);
  $('#next').onclick = () => {
    if (step === steps.length - 1) window.top.location.href = 'https://futureaisociety.org/demos';
    else moveStep(step + 1);
  };
  $('#fit').onclick = () => graph?.fit();
  $('#reset-content').onclick = async () => {
    if (refreshTask) await refreshTask;
    if (busy) return;
    busy = true; controls();
    try {
      const creation = new URL('/api/sandboxes', location.origin);
      const demo = sandbox.demo || parameters.get('demo');
      if (demo) creation.searchParams.set('demo', demo);
      const response = await fetch(creation, {method:'POST',cache:'no-store'});
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || data.detail || 'Could not reset the demo.');
      const url = new URL(location.pathname, location.origin);
      if (demo) url.searchParams.set('demo', demo);
      url.searchParams.set('session', data.id);
      url.searchParams.set('root', rootInput.defaultValue || rootInput.options?.[0]?.value || 'Thought');
      location.assign(url);
    } catch (error) { feedback(error.message, true); }
    finally { busy = false; controls(); }
  };
  $('#root-form').onsubmit = event => { event.preventDefault(); run(async () => {
    await graph.loadRoot(rootInput.value);
    feedback(`Showing root: ${rootInput.value.trim()}.`);
  }); };
  controls();
  try {
    if (!await sandbox.ready) throw sandbox.error;
    try {
      const stored=JSON.parse(sessionStorage.getItem(stateKey())||'null');
      if(stored?.version===1 && Number.isInteger(stored.step) && stored.step>=0 && stored.step<steps.length &&
          stored.urlRoot===new URL(location.href).searchParams.get('root')) {
        savedState=stored;step=stored.step;
        for(const index of Array.isArray(stored.completed)?stored.completed:[])if(Number.isInteger(index)&&index>=0&&index<steps.length)completed.add(index);
      }
    } catch { /* Ignore unavailable storage and outdated/invalid state. */ }
    showStep();
    graph = await createUKSGraph($('#graph'), {
      request, root: rootInput.value, colors, help: {button: $('#help'), dialog: $('#help-box')}, perform: run, onInspect: inspect,
      onRoot: label => { rootInput.value = label; sandbox.updateRoot(label); },
      onChange: updateGraphControls, onError: error => feedback(error.message, true),
      onDrag: () => { completed.add(step); controls(); scheduleSave(); }
    });
    let seed = false;
    if (seedIfMissing) {
      try { await graph.read({label:seedIfMissing}); }
      catch (error) { if (error.status !== 404 || error.code === 'sandbox_unavailable') throw error; seed = true; }
    }
    if (seed) for (const index of initialFacts) await addFact(index);
    for (const group of seedGroups) {
      try { await graph.read({label:group.thought}); }
      catch (error) {
        if (error.status !== 404 || error.code === 'sandbox_unavailable') throw error;
        for (const index of group.facts) await addFact(index);
      }
    }
    const onSubject=() => { selection = ''; inspect(); feedback(''); showStep(); };
    if(componentChoices)queryPanel=createDemoComponents({request,graph,perform:run,feedback,onSubject,query,sequence,facts,steps});
    else if (query) queryPanel = createQueryPanel({request, graph, perform:run, config:query, feedback, onSubject});
    else if (sequence) queryPanel = createSequencePanel({request,graph,perform:run,config:sequence,feedback});
    await synchronize({ fit: true });
    try { if (query || sequence) await graph.loadThoughts([]); else await graph.loadRoot(rootInput.value, { showPresets: true }); }
    catch (error) { if (error.status !== 404 || error.code === 'sandbox_unavailable') throw error; }
    showStep(); feedback('');
    if(savedState) {
      if(savedState.panel && queryPanel) {await queryPanel.restoreState(savedState.panel);showStep();}
      if(savedState.graph)await graph.restoreState(savedState.graph);
      selection=typeof savedState.selection==='string'?savedState.selection:'';inspect();
      for(const [selector,top] of Object.entries(savedState.scrolls||{}))if($(selector)&&Number.isFinite(top))$(selector).scrollTop=top;
    }
  } catch (error) { feedback(error.message, true); }
  finally { busy = false; controls(); }
  if (queryPanel) await run(() => queryPanel.activate());
  stateReady=sandbox.available && !!graph;
  saveState();
  document.addEventListener('input',scheduleSave);
  document.addEventListener('change',scheduleSave);
  document.addEventListener('scroll',event=>{
    if(event.target?.matches?.('.action-zone,.prompt-content,.invitation,#query-results'))scheduleSave();
  },{capture:true,passive:true});
  window.addEventListener('pagehide',saveState);
  // Native select menus close when their controls are disabled/recreated.
  // Poll quietly and defer updates while the user is choosing or editing.
  const interacting = () => document.querySelector('dialog[open]') || document.activeElement?.matches('select,input,textarea');
  let needsStepRender = false;
  const timer = setInterval(async () => {
    if (busy || !graph || !sandbox.available || document.hidden || interacting()) return;
    busy = true;
    refreshTask = (async () => {
      const membership = JSON.stringify([...created]);
      await synchronize({ automatic: true });
      if (!interacting()) await queryPanel?.refresh({automatic:true});
      needsStepRender ||= membership !== JSON.stringify([...created]);
      if (needsStepRender && !interacting()) { showStep(); needsStepRender = false; }
    })();
    try { await refreshTask; }
    catch (error) { feedback(error.message, true); }
    finally { refreshTask = null; busy = false; if (!interacting()) controls(); }
  }, 4000);
  return { graph, stopRefreshing: () => clearInterval(timer) };
}

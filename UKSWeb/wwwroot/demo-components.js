import { createQueryPanel } from './uks-query.js?v=20261111-location-weights';
import { createSequencePanel } from './uks-sequences.js?v=20261107-compact-music';

const queryNames=new Set(['thought-selector','attribute-editor','attribute-choices','attribute-search','attribute-results','candidate-results']);
const sequenceNames=new Set(['word-input','color-pad','note-keyboard','sequence-input','sequence-actions','sequence-finder','sequence-match','remember-sequence','tune-selector','playback','sequence-library','sequence-results']);

// Components share the page's UKS and graph, but own their controls/results.
// A page lists component names in steps[].choices. createComponent() below routes
// query/editor names to uks-query.js and sequence/music names to uks-sequences.js.
// Labels and actual control markup are created in those two files, not here.
export function createDemoComponents({request,graph,perform,feedback,onSubject,query,sequence,facts,steps}) {
  const specs=steps.flatMap(step=>step.choices || []).filter(entry=>entry && typeof entry==='object');
  const queryNeeded=!!query || specs.some(spec=>queryNames.has(spec.component));
  const sequenceNeeded=!!sequence || specs.some(spec=>sequenceNames.has(spec.component));
  const unique=values=>[...new Set(values.filter(Boolean))];
  const label=fact=>(fact.target.startsWith('"')?JSON.parse(fact.target):fact.target);
  const defaults={root:'Thought',subjects:unique(facts.filter(fact=>fact.sourceFact===undefined).map(fact=>fact.source)),
    types:unique(['is','has','is-a',...facts.map(fact=>fact.relationshipType)]),targets:unique(facts.map(label)),attributes:[]};
  if(!defaults.subjects.length)defaults.subjects=['Thought'];
  if(!defaults.targets.length)defaults.targets=['Thought'];
  function results(name) {
    const node=document.createElement('div');node.className='component-results';node.setAttribute('role','region');node.setAttribute('aria-label',name);node.tabIndex=0;return node;
  }
  let dialog=document.querySelector('#attribute-box');
  if(queryNeeded && !dialog){dialog=document.createElement('dialog');dialog.id='attribute-box';dialog.setAttribute('aria-label','Manage Attributes');document.body.append(dialog);}
  const queryResults=results('Attribute results'),sequenceResults=results('Sequence results');
  const openDisclosures = new Set();
  function addDisclosure(host, key, label, output, onOpen) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'choice';
    button.textContent = label;
    function update() {
      const open = openDisclosures.has(key);
      button.setAttribute('aria-expanded', String(open));
      output.hidden = !open;
    }
    update();
    button.onclick = () => perform(async () => {
      if (openDisclosures.delete(key)) update();
      else {
        openDisclosures.add(key);
        update();
        await onOpen?.();
      }
      return false;
    });
    host.append(button, output);
  }
  const eventGraphViews = new Map();
  const seenEventSequences = new Set();
  const q=queryNeeded?createQueryPanel({request,graph,perform,feedback,onSubject,config:{...defaults,...query},controls:document.createElement('div'),results:queryResults,dialog}):null;
  const s=sequenceNeeded?createSequencePanel({request,graph,perform,feedback,config:{words:[],fragments:[],notes:['C4','D4','E4','F4','G4','A4','B4','C5'],tunes:[],...sequence},controls:document.createElement('div'),results:sequenceResults}):null;
  for(const id of ['#query-controls','#query-results']) {const legacy=document.querySelector(id);if(legacy)legacy.style.display='none';}
  let mode='attributes',active=new Set(),mounted=[];
  return {
    showStep(next) {
      mode=next || 'attributes';active=new Set();mounted=[];
      q?.showStep(['attributes','candidates','exceptions'].includes(mode)?mode:'attributes');
      s?.showStep(['spelling','letters','colors','music'].includes(mode)?mode:'spelling');
    },
    createComponent(spec) {
      const host=document.createElement('div');host.className='demo-component';
      host.dataset.component=spec.component;if(spec.id)host.dataset.componentId=spec.id;
      if(spec.component==='html'){host.innerHTML=spec.html || '';return host;}
      if (spec.component === 'event-history') {
        const output = results('Event history');
        const show = document.createElement('button');
        show.type = 'button';
        show.className = 'choice';
        show.dataset.unavailable = 'true';
        function updateToggle(state = graph.getState()) {
          const showing = eventGraphViews.has(spec.subject) && state.rootLabel === spec.subject &&
            state.expandRoots && state.showSequenceValues;
          show.textContent = showing ? 'Hide event sequence in graph' : 'Show event sequence in graph';
          show.setAttribute('aria-pressed', String(showing));
          return showing;
        }
        async function openEventGraph(current = graph.getState()) {
          await graph.loadThoughts([spec.subject], { expand: true, sequenceValues: true });
          eventGraphViews.set(spec.subject, current);
          seenEventSequences.add(spec.subject);
        }
        updateToggle();
        show.onclick = () => perform(async () => {
          const current = graph.getState();
          if (updateToggle(current)) {
            await graph.restoreState(eventGraphViews.get(spec.subject));
            eventGraphViews.delete(spec.subject);
          } else {
            await openEventGraph(current);
          }
          updateToggle();
          return false;
        });
        const refresh = async ({ automatic = false } = {}) => {
          const parameters = new URLSearchParams({ label: spec.subject });
          // === WEB SERVICE: GET /api/events — read the stored event sequence in head-first order. ===
          const { events } = await request(`/api/events?${parameters}`, { automatic });
          if (events.length && !seenEventSequences.has(spec.subject)) await openEventGraph();
          updateToggle();
          const heading = document.createElement('h3');
          heading.textContent = `Event history of ${spec.subject}`;
          if (events.length === 0) {
            const empty = document.createElement('p');
            empty.textContent = 'Change the current location to record the first event.';
            output.replaceChildren(heading, empty);
            show.dataset.unavailable = 'true';
            return;
          }
          const list = document.createElement('ul');
          for (const event of events) {
            const row = document.createElement('li');
            row.textContent = event.relationship;
            list.append(row);
          }
          output.replaceChildren(heading, list);
          show.dataset.unavailable = 'false';
        };
        host.append(show, output);
        mounted.push(host);
        active.add({ activate: refresh, refresh });
        return host;
      }
      if (spec.component === 'condition-explanation') {
        const { subject, relationshipType, target } = spec;
        const key = JSON.stringify([spec.component, subject, relationshipType, target]);
        const output = results('Condition explanation');
        output.setAttribute('aria-live', 'polite');
        const refresh = async ({ automatic = false } = {}) => {
          if (!openDisclosures.has(key)) return;
          const parameters = new URLSearchParams({ label: subject, explainType: relationshipType, explainTarget: target });
          // === WEB SERVICE: GET /api/attributes — evaluate the conclusion and read the UKS's reasons in the same request. ===
          const { explanation } = await request(`/api/attributes?${parameters}`, { automatic });
          const heading = document.createElement('h3');
          heading.textContent = `${explanation.holds ? 'True' : 'Not established'} · [${subject} → ${relationshipType} → ${target}]`;
          const summary = document.createElement('small');
          summary.textContent = explanation.asserted && explanation.holds ? 'This relationship is asserted directly.'
            : explanation.hasRule ? 'Conditions tested by the UKS:'
            : explanation.holds ? 'The UKS query returned this relationship.'
            : 'There is no supporting fact or rule for this relationship.';
          const list = document.createElement('ul');
          for (const condition of explanation.conditions) {
            const row = document.createElement('li');
            const label = document.createElement('span');
            const status = document.createElement('strong');
            label.textContent = condition.relationship;
            status.textContent = condition.status;
            status.className = condition.status === 'True' ? 'direct' : condition.status === 'False' ? 'error' : '';
            row.append(label, status);
            list.append(row);
          }
          output.replaceChildren(heading, summary, list);
        };
        addDisclosure(host, key, 'Why? / Why not?', output, refresh);
        mounted.push(host);
        active.add({ activate: refresh, refresh });
        return host;
      }
      let panel=queryNames.has(spec.component)?q:sequenceNames.has(spec.component)?s:null;
      if(spec.component==='results')panel=['spelling','letters','colors','music'].includes(mode)?s:q;
      if(!panel)throw new Error(`Unknown or unconfigured demo component: ${spec.component}`);
      const node=panel.createComponent(spec);
      if(mounted.some(item=>item.contains(node)))throw new Error(`Component ${spec.component} is already present in this step.`);
      if (spec.toggleLabel) {
        const key = JSON.stringify([spec.component, spec.id || spec.subject || spec.source]);
        addDisclosure(host, key, spec.toggleLabel, node, () => panel.refresh());
      } else {
        node.hidden = false;
        host.append(node);
      }
      mounted.push(host);active.add(panel);
      if(spec.height){node.style.height=spec.height;node.style.maxHeight=spec.height;node.style.overflowY='auto';}
      return host;
    },
    get subject(){return q?.subject;},
    async activate(){for(const panel of active)await panel.activate();return false;},
    async refresh(options={}){for(const panel of active)await panel.refresh(options);},
    setBusy(busy){
      q?.setBusy(busy);s?.setBusy(busy);
      for(const host of mounted)for(const node of host.querySelectorAll('button,input,select')) {
        const unavailable=busy || node.dataset.unavailable==='true';if(node.disabled!==unavailable)node.disabled=unavailable;
      }
    },
    getState(){return {components:true,query:q?.getState(),sequence:s?.getState(),eventGraphViews:[...eventGraphViews],seenEventSequences:[...seenEventSequences],openDisclosures:[...openDisclosures],scrolls:[queryResults.scrollTop,sequenceResults.scrollTop]};},
    async restoreState(state){
      if(!state)return;
      for (const subject of Array.isArray(state.seenEventSequences) ? state.seenEventSequences : []) {
        if (typeof subject === 'string') seenEventSequences.add(subject);
      }
      for (const key of Array.isArray(state.openDisclosures) ? state.openDisclosures : []) {
        if (typeof key === 'string') openDisclosures.add(key);
      }
      for (const entry of Array.isArray(state.eventGraphViews) ? state.eventGraphViews : []) {
        if (Array.isArray(entry) && typeof entry[0] === 'string' && Array.isArray(entry[1]?.roots)) {
          eventGraphViews.set(entry[0], entry[1]);
          seenEventSequences.add(entry[0]);
        }
      }
      if(state.components){if(state.query&&q)await q.restoreState(state.query);if(state.sequence&&s)await s.restoreState(state.sequence);}
      else if(['attributes','candidates','exceptions'].includes(state.mode))await q?.restoreState(state);
      else await s?.restoreState(state);
      // Restoring panel state rebuilds its controls; mount those updated controls.
      onSubject();
      if(Array.isArray(state.scrolls)){queryResults.scrollTop=state.scrolls[0]||0;sequenceResults.scrollTop=state.scrolls[1]||0;}
    }
  };
}

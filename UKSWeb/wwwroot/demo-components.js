import { createQueryPanel } from './uks-query.js?v=20261106-commit-input';
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
      let panel=queryNames.has(spec.component)?q:sequenceNames.has(spec.component)?s:null;
      if(spec.component==='results')panel=['spelling','letters','colors','music'].includes(mode)?s:q;
      if(!panel)throw new Error(`Unknown or unconfigured demo component: ${spec.component}`);
      const node=panel.createComponent(spec);
      if(mounted.some(item=>item.contains(node)))throw new Error(`Component ${spec.component} is already present in this step.`);
      host.append(node);mounted.push(host);active.add(panel);
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
    getState(){return {components:true,query:q?.getState(),sequence:s?.getState(),scrolls:[queryResults.scrollTop,sequenceResults.scrollTop]};},
    async restoreState(state){
      if(!state)return;
      if(state.components){if(state.query&&q)await q.restoreState(state.query);if(state.sequence&&s)await s.restoreState(state.sequence);}
      else if(['attributes','candidates','exceptions'].includes(state.mode))await q?.restoreState(state);
      else await s?.restoreState(state);
      // Restoring panel state rebuilds its controls; mount those updated controls.
      onSubject();
      if(Array.isArray(state.scrolls)){queryResults.scrollTop=state.scrolls[0]||0;sequenceResults.scrollTop=state.scrolls[1]||0;}
    }
  };
}

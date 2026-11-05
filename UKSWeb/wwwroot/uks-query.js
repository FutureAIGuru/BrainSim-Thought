// Reusable query controls and result pane. Demo pages supply selectable content.
export function createQueryPanel({ request, graph, perform, config, feedback, onSubject = () => {}, controls:controlHost, results:resultHost, dialog:dialogHost }) {
  const controls = controlHost || document.querySelector('#query-controls');
  const results = resultHost || document.querySelector('#query-results');
  const dialog = dialogHost || document.querySelector('#attribute-box');
  const parts = new Map();
  let resultSource = null;
  const selected = new Map();
  const attributes = [...config.attributes];
  let mode = 'attributes', subject = '', lastQuery = null;
  const key = attribute => JSON.stringify([attribute.relationshipType, attribute.target]);
  function element(tag, text, className) {
    const node = document.createElement(tag); if (text !== undefined) node.textContent = text;
    if (className) node.className = className; return node;
  }
  function select(values, label, value) {
    const field = element('label', label), input = document.createElement('select');
    input.setAttribute('aria-label',label);
    for (const item of values) { const option = element('option', item); option.value = item; input.append(option); }
    if (value) input.value = value;
    field.append(input); return {field,input};
  }
  function button(text, action) {
    const node = element('button', text); node.type = 'button'; node.onclick = () => perform(action); return node;
  }
  function notation(row) { return `[${row.source} → ${row.relationshipType} → ${row.target}]`; }
  function showAttributes(data, title = data.label, differences = new Set()) {
    const section = element('section'), heading = element('h3', title);
    section.append(heading);
    if (!data.attributes.length) section.append(element('p', 'No effective attributes.'));
    const list = element('ul');
    for (const row of [...data.attributes].sort((a,b) => Number(differences.has(b.target))-Number(differences.has(a.target)))) {
      const item = element('li', undefined, differences.has(row.target) ? 'attribute-difference' : '');
      const detail = button(notation(row), async () => {
        if (lastQuery) lastQuery.evidenceKey = JSON.stringify([row.source,row.relationshipType,row.target]);
        await graph.setEvidence([row], {fit:true});
        feedback(row.evidenceLinkIds?.length ? 'Orange relationships show a stored supporting route for 8 seconds.' : 'No stored supporting route is available for this computed result.');
      });
      detail.className = 'result-notation result-link'; detail.title = 'Highlight supporting relationships';
      item.append(detail, element('small',
        row.depth > 0 ? `Inherited from ${row.inheritedFrom || 'an ancestor'} · ${row.depth} hop${row.depth === 1 ? '' : 's'}` : 'Direct', row.depth > 0 ? 'inherited' : 'direct'));
      list.append(item);
    }
    section.append(list); results.append(section);
  }
  async function execute(query, { automatic = false, showGraph = true } = {}) {
    let data;
    const paused = () => automatic && (dialog.open || document.activeElement?.matches('select,input,textarea'));
    if (paused()) return false;
    if (resultSource && query.source && resultSource!==query.source) return false;
    results.classList.remove('comparison-results');
    if (query.mode === 'candidates') {
      if (!query.attributes.length) { feedback('Select at least one attribute.'); return false; }
      const params = new URLSearchParams({root: config.root});
      for (const attribute of query.attributes) { params.append('types',attribute.relationshipType); params.append('targets',attribute.target); }
      data = await request('/api/candidates?' + params, { automatic });
      if (paused()) return false;
      results.replaceChildren(element('h3', 'Likely candidates'));
      if (!data.candidates.length) results.append(element('p', 'No candidates found. Try fewer or different attributes.'));
      for (const candidate of data.candidates) {
        const row = element('div', undefined, 'candidate-row');
        row.append(button(candidate.label, async () => {
          subject = candidate.label;
          await execute({mode:'attributes',subject:candidate.label,evidenceFilter:candidate.attributes});
        }), element('small', `${candidate.matched}/${candidate.total} selected attributes match`));
        results.append(row);
      }
      results.append(element('p', 'Ordered by UKS match score; these are candidates, not probabilities.', 'query-note'));
      if (showGraph) await graph.loadThoughts(data.candidates.slice(0,32).map(item => item.id), {expand:true});
      await graph.setEvidence(data.candidates.slice(0,32).flatMap(item => item.attributes || []), {fit:showGraph,automatic});
    } else {
      if (!query.subject) return false;
      const labels = [query.subject];
      data = await Promise.all(labels.map(label => request('/api/attributes?' + new URLSearchParams({label}), {automatic})));
      if (paused()) return false;
      results.replaceChildren();
      const typesByTarget = new Map();
      for (const item of data) for (const row of item.attributes) {
        if (!typesByTarget.has(row.target)) typesByTarget.set(row.target,new Set());
        typesByTarget.get(row.target).add(row.relationshipType);
      }
      const differences = new Set([...typesByTarget].filter(([,types]) => types.size > 1).map(([target]) => target));
      for (const item of data) showAttributes(item,item.label,differences);
      if (showGraph) await graph.loadThoughts(data.map(item => item.id), {expand:true});
      let evidence = data.flatMap(item => item.attributes);
      if (query.evidenceKey) evidence = evidence.filter(row => JSON.stringify([row.source,row.relationshipType,row.target]) === query.evidenceKey);
      else if (query.evidenceFilter) evidence = evidence.filter(row => query.evidenceFilter.some(attribute => attribute.relationshipType === row.relationshipType && attribute.target === row.target));
      else if (query.mode === 'exceptions' && differences.size) evidence = evidence.filter(row => differences.has(row.target));
      await graph.setEvidence(evidence, {fit:showGraph,automatic});
    }
    lastQuery = query;
    return true;
  }
  function render() {
    controls.replaceChildren();parts.clear();
    if (mode === 'candidates') {
      const list = element('div', undefined, 'attribute-choices'); list.setAttribute('aria-label','Search attributes');
      for (const attribute of attributes) {
        const label = element('label'), input = document.createElement('input');
        input.type = 'checkbox'; input.checked = selected.has(key(attribute));
        input.onchange = () => input.checked ? selected.set(key(attribute),attribute) : selected.delete(key(attribute));
        label.append(input, document.createTextNode(`${attribute.relationshipType} → ${attribute.target}`)); list.append(label);
      }
      const search=button('Find candidates', () => execute({mode,source:search.dataset.source || 'search',attributes:[...selected.values()]}));
      parts.set('attribute-choices',list);parts.set('candidate-search',search);
      const group=element('div',undefined,'attribute-search');group.append(list,search);parts.set('attribute-search',group);controls.append(group);
    } else {
      const subjects = mode === 'exceptions' ? config.exceptionSubjects || config.subjects : config.subjects;
      const choice = select(['',...subjects], 'Choose a Thought', subject);
      choice.input.options[0].textContent = 'Select a Thought…';
      choice.input.value = subject;
      choice.input.onchange = () => {
        subject = choice.input.value;
        perform(async () => {
          onSubject(subject);
          if (!subject) {lastQuery = null; results.replaceChildren(element('p','Choose a Thought to see its attributes.')); await graph.loadThoughts([]); return false;}
          return execute({mode,subject,source:choice.input.dataset.source || 'subject'});
        });
      };
      parts.set('thought-selector',choice.field);controls.append(choice.field);
    }
    const add = element('button','Manage attributes…'); add.type = 'button';
    add.onclick = () => openAttributeDialog();parts.set('attribute-editor',add); controls.append(add);
  }
  function openAttributeDialog() {
    const owner = select(config.subjects, 'Thought', subject || config.subjects[0]);
    const type = select(config.types, 'Relationship type');
    const target = select(config.targets, 'Target');
    const title = element('div', undefined, 'help-heading');
    const close = element('button','×'); close.type = 'button'; close.setAttribute('aria-label','Close attribute window'); close.onclick = () => dialog.close();
    title.append(element('h2','Manage attributes'),close);
    const message = element('p','Add an attribute or delete a direct relationship. To remove an inherited attribute, select the Thought it comes from.','query-note');
    const existing = select([], 'Existing direct attribute');
    let directVersion = 0;
    const remove = button('Delete', async () => {
      if (!existing.input.value) return false;
      await request('/api/relationships?' + new URLSearchParams({id:existing.input.value}), {method:'DELETE'});
      await loadExisting();
      subject = owner.input.value;onSubject(subject);
      await execute(mode === 'candidates' && selected.size ? {mode,attributes:[...selected.values()]} : {mode:mode === 'exceptions' ? mode : 'attributes',subject});
      message.textContent = 'Deleted. Inherited defaults, if any, are now visible in the results.';
    });
    remove.className = 'delete-attribute';
    async function loadExisting() {
      const version = ++directVersion;
      remove.disabled = true; remove.dataset.unavailable = 'true';
      const data = await request('/api/children?' + new URLSearchParams({label:owner.input.value}));
      if (version !== directVersion) return;
      existing.input.replaceChildren(...data.relationships.map(row => {
        const option = element('option', notation(row)); option.value = row.id; return option;
      }));
      if (!data.relationships.length) existing.input.append(element('option','No direct attributes'));
      if (!data.relationships.length) existing.input.options[0].value = '';
      remove.dataset.unavailable = String(!data.relationships.length); remove.disabled = !data.relationships.length;
    }
    owner.input.onchange = () => perform(loadExisting);
    const save = button('Add', async () => {
      // Quoted noun fields preserve the exact selected identities (e.g. legs).
      await request('/api/statements', {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({
        source:JSON.stringify(owner.input.value), relationshipType:type.input.value, target:JSON.stringify(target.input.value)
      })});
      remember({relationshipType:type.input.value,target:target.input.value});
      subject = owner.input.value; dialog.close(); render();onSubject(subject);
      if (mode !== 'candidates' || selected.size) await execute(mode === 'candidates' ? {mode,attributes:[...selected.values()]} : {mode,subject});
      else feedback('Attribute added to the UKS.');
    });
    const use = button('Use in search', async () => {
      const attribute = {relationshipType:type.input.value,target:target.input.value};
      remember(attribute); selected.set(key(attribute),attribute); dialog.close(); render();onSubject(subject);
      feedback('Attribute selected. Choose Find candidates to search.');
    });
    use.hidden = mode !== 'candidates';
    const deleteRow = element('div',undefined,'attribute-dialog-row'); deleteRow.append(existing.field,remove);
    const addRow = element('div',undefined,'attribute-dialog-row attribute-add-row'); addRow.append(type.field,target.field,save);
    dialog.replaceChildren(title,message,owner.field,deleteRow,element('hr'),addRow,use);
    const graphBounds = document.querySelector('#graph').getBoundingClientRect();
    const width = Math.min(440,window.innerWidth - 24);
    dialog.style.width = width + 'px';
    dialog.style.margin = '0';
    dialog.style.left = Math.max(12,Math.min(graphBounds.left + 12,window.innerWidth - width - 12)) + 'px';
    dialog.style.top = Math.max(12,Math.min(graphBounds.top + 12,window.innerHeight - 160)) + 'px';
    dialog.style.maxHeight = `calc(100dvh - ${parseFloat(dialog.style.top) + 12}px)`;
    dialog.showModal();
    perform(async () => { await loadExisting(); return false; });
  }
  function remember(attribute) {
    if (!attributes.some(item => key(item) === key(attribute))) attributes.push(attribute);
  }
  return {
    createComponent(spec) {
      const name=spec.component;
      if(['attribute-results','candidate-results','results'].includes(name)) {
        resultSource=spec.source || (mode==='candidates'?'search':'subject');return results;
      }
      const node=parts.get(name);
      if(!node)throw new Error(`Component ${name} is unavailable in this query step.`);
      if(name==='thought-selector')node.querySelector('select').dataset.source=spec.id || 'subject';
      if(['candidate-search','attribute-search'].includes(name)) {
        const search=name==='candidate-search'?node:node.querySelector('button');search.dataset.source=spec.id || 'search';
      }
      if(spec.label && node.tagName==='BUTTON')node.textContent=spec.label;
      return node;
    },
    showStep(nextMode) {
      resultSource=null;
      const next = nextMode || 'attributes';
      if (mode !== next) {subject = ''; lastQuery = null; results.replaceChildren(element('p',next === 'candidates' ? 'Select attributes to find likely candidates.' : 'Choose a Thought to see its attributes.'));}
      mode = next; render();
    },
    async activate() {
      // Each step waits for the visitor to make a selection.
      return false; // Initial output does not count as a user experiment action.
    },
    getState() { return {mode,subject,selected:[...selected.values()],attributes,lastQuery}; },
    async restoreState(state) {
      if(!state || state.mode!==mode)return;
      subject=typeof state.subject==='string' && config.subjects.includes(state.subject)?state.subject:'';
      const valid=row=>row && typeof row.relationshipType==='string' && typeof row.target==='string';
      for(const row of Array.isArray(state.attributes)?state.attributes:[])if(valid(row))remember(row);
      selected.clear();for(const row of Array.isArray(state.selected)?state.selected:[])if(valid(row))selected.set(key(row),row);
      render();onSubject(subject);
      if(state.lastQuery && ['attributes','exceptions','candidates'].includes(state.lastQuery.mode)) {
        try { await execute(state.lastQuery); }
        catch(error) {if(error.status!==404 || error.code==='sandbox_unavailable')throw error;lastQuery=null;}
      }
    },
    get subject() { return subject; },
    async refresh(options = {}) { if (lastQuery) await execute(lastQuery,{...options,showGraph:false}); },
    setBusy(busy) { controls.querySelectorAll('button,select,input').forEach(node => node.disabled = busy);
      dialog.querySelectorAll('button,select').forEach(node => node.disabled = node.dataset.unavailable === 'true' || busy && node.getAttribute('aria-label') !== 'Close attribute window'); }
  };
}

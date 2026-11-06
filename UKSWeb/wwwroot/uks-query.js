// Reusable query controls and result panes. Demo pages supply selectable content.
// Navigation guide:
//   render()                 builds Show Attributes / search controls.
//   openAttributeDialog()    builds Manage Attributes; includes a visual row map.
//   execute()                calls UKS queries and fills the result panes.
//   createComponent()        maps names in a page's choices list to these controls.
// Visible wording is beside the element creation; dialog placement is in its
// LAYOUT ASSEMBLY block. Corresponding CSS is grouped under #attribute-box.
export function createQueryPanel({ request, graph, perform, config, feedback, onSubject = () => {}, controls:controlHost, results:resultHost, dialog:dialogHost }) {
  const controls = controlHost || document.querySelector('#query-controls');
  const results = resultHost || document.querySelector('#query-results');
  const dialog = dialogHost || document.querySelector('#attribute-box');
  const parts = new Map();
  // Candidate searches and subject inspection can coexist in one step.
  const resultPanes = new Map([
    ['attributes', results],
    ['candidates', controlHost ? results.cloneNode(false) : results]
  ]);
  if (controlHost) resultPanes.get('candidates').setAttribute('aria-label', 'Candidate results');
  const resultSources = new Map(), lastQueries = new Map();
  const selected = new Map();
  const attributes = [...config.attributes];
  let mode = 'attributes', subject = '', lastQuery = null, useMenuFields = true, searchTimer;
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
  const formatWeight = value => Number.isFinite(value) ? Number(value.toFixed(3)).toString() : '—';
  function notation(row) { return `[${row.source} → ${row.relationshipType} → ${row.target}]`; }
  function showAttributes(data, title = data.label, differences = new Set(), output = results) {
    const section = element('section'), heading = element('h3', title);
    section.append(heading);
    if (!data.attributes.length) section.append(element('p', 'No effective attributes.'));
    const list = element('ul');
    for (const row of [...data.attributes].sort((a,b) => Number(differences.has(b.target))-Number(differences.has(a.target)))) {
      const item = element('li', undefined, differences.has(row.target) ? 'attribute-difference' : '');
      const detail = button(notation(row), async () => {
        const inspected = lastQueries.get('attributes');
        if (inspected) inspected.evidenceKey = JSON.stringify([row.source,row.relationshipType,row.target]);
        await graph.setEvidence([row], {fit:true});
        feedback(row.evidenceLinkIds?.length ? 'Orange relationships show a stored supporting route for 2 seconds.' : 'No stored supporting route is available for this computed result.');
      });
      detail.className = 'result-notation result-link'; detail.title = 'Highlight supporting relationships';
      const inherited = row.depth > 0;
      const origin = inherited ? `${row.inheritedFrom || 'ancestor'} · ${row.depth} hop${row.depth === 1 ? '' : 's'}` : 'Direct';
      const metadata = element('small',`${origin} · weight ${formatWeight(row.weight)}`,inherited ? 'inherited' : 'direct');
      metadata.title = `${inherited ? 'Inherited from ' + (row.inheritedFrom || 'an ancestor') : 'Stored directly'}; effective Relationship weight ${formatWeight(row.weight)}`;
      item.append(detail,metadata);
      list.append(item);
    }
    section.append(list); output.append(section);
  }
  async function execute(query, { automatic = false, showGraph = true } = {}) {
    const stream = query.mode === 'candidates' ? 'candidates' : 'attributes';
    // A single candidate pane also supports drilling into a candidate. If an
    // attribute-results component is present, keep that inspection separate.
    const paneStream = stream === 'attributes' && mode === 'candidates' && !resultSources.has('attributes') ? 'candidates' : stream;
    const results = resultPanes.get(paneStream), resultSource = resultSources.get(stream);
    let data;
    const paused = () => automatic && (dialog.open || document.activeElement?.matches('select,input,textarea'));
    if (paused()) return false;
    if (resultSource && query.source && resultSource!==query.source) return false;
    results.classList.remove('comparison-results');
    if (query.mode === 'candidates') {
      if (!query.attributes.length) {
        results.replaceChildren(); lastQueries.delete('candidates');
        if (!resultSources.has('attributes')) lastQueries.delete('attributes');
        if (showGraph && !lastQueries.has('attributes')) await graph.loadThoughts([]);
        feedback('Select attributes to find likely candidates.');return false;
      }
      if (!automatic) feedback('');
      const params = new URLSearchParams({root: config.root});
      for (const attribute of query.attributes) { params.append('types',attribute.relationshipType); params.append('targets',attribute.target); }
      // === WEB SERVICE: GET /api/candidates — search by selected attributes; return ranked candidates and supporting evidence. ===
      data = await request('/api/candidates?' + params, { automatic });
      if (paused()) return false;
      results.replaceChildren(element('h3', 'Likely candidates'));
      if (!data.candidates.length) results.append(element('p', 'No candidates found. Try fewer or different attributes.'));
      const list = element('ul');
      for (const candidate of data.candidates) {
        const row = element('li');
        const inspect = button(candidate.label, async () => {
          subject = candidate.label;onSubject(subject);
          await execute({mode:'attributes',subject:candidate.label,evidenceFilter:candidate.attributes});
        });
        inspect.className='result-link';inspect.title='Show this candidate’s attributes';
        // Earlier servers called the returned search weight "score". Prefer that
        // field when present: their separate "weight" was the Thought's own weight.
        const searchWeight=Number.isFinite(candidate.score) ? candidate.score : candidate.weight;
        const metadata=element('small',`${candidate.matched}/${candidate.total} match · weight ${Number.isFinite(searchWeight) ? searchWeight.toFixed(2) : "—"}`);
        metadata.title='Search weight returned by UKS SearchByAttributes, as displayed in the Windows query dialog.';
        row.append(inspect,metadata);list.append(row);
      }
      results.append(list);
      results.append(element('p', 'Ordered by UKS search weight; these are candidates, not probabilities.', 'query-note'));
      if (showGraph) await graph.loadThoughts(data.candidates.slice(0,32).map(item => item.id), {expand:true});
      await graph.setEvidence(data.candidates.slice(0,32).flatMap(item => item.attributes || []), {fit:showGraph,automatic});
    } else {
      if (!query.subject) return false;
      const labels = [query.subject];
      try {
        // === WEB SERVICE: GET /api/attributes (one per label) — retrieve effective attributes, including inheritance and exceptions. ===
        data = await Promise.all(labels.map(label => request('/api/attributes?' + new URLSearchParams({label}), {automatic})));
      } catch (error) {
        if (error.status !== 404 || error.code === 'sandbox_unavailable') throw error;
        if (paused()) return false;
        results.replaceChildren(element('h3',query.subject),element('p','No attributes yet. Add a relationship to begin.'));
        // Keep the selection so adding its first fact refreshes these results.
        lastQuery = query; lastQueries.set(stream,query);
        if (showGraph) await graph.loadThoughts([]);
        return false;
      }
      if (paused()) return false;
      results.replaceChildren();
      const typesByTarget = new Map();
      for (const item of data) for (const row of item.attributes) {
        if (!typesByTarget.has(row.target)) typesByTarget.set(row.target,new Set());
        typesByTarget.get(row.target).add(row.relationshipType);
      }
      const differences = new Set([...typesByTarget].filter(([,types]) => types.size > 1).map(([target]) => target));
      for (const item of data) showAttributes(item,item.label,differences,results);
      if (showGraph) await graph.loadThoughts(data.map(item => item.id), {expand:true});
      let evidence = data.flatMap(item => item.attributes);
      if (query.evidenceKey) evidence = evidence.filter(row => JSON.stringify([row.source,row.relationshipType,row.target]) === query.evidenceKey);
      else if (query.evidenceFilter) evidence = evidence.filter(row => query.evidenceFilter.some(attribute => attribute.relationshipType === row.relationshipType && attribute.target === row.target));
      else if (query.mode === 'exceptions' && differences.size) evidence = evidence.filter(row => differences.has(row.target));
      await graph.setEvidence(evidence, {fit:showGraph,automatic});
    }
    lastQuery = query;lastQueries.set(stream,query);
    return true;
  }
  // Refresh only queries the visitor has actually requested. An unselected
  // Attributes of control stays on its prompt with an empty results pane.
  async function refreshResults(options = {}) {
    for (const query of [...lastQueries.values()]) await execute(query,{...options,showGraph:false});
  }
  function render() {
    controls.replaceChildren();parts.clear();
    // Build every query component independently. The page's choices list decides
    // what is mounted; query mode must not prevent mixed search/attribute steps.
    {
      const list = element('div', undefined, 'attribute-choices'); list.setAttribute('aria-label','Search attributes');
      for (const attribute of attributes) {
        const label = element('label'), input = document.createElement('input');
        input.type = 'checkbox'; input.checked = selected.has(key(attribute));
        // Search after checkbox changes; a short debounce groups rapid selections.
        input.onchange = () => {
          if (input.checked) selected.set(key(attribute),attribute); else selected.delete(key(attribute));
          clearTimeout(searchTimer);
          searchTimer=setTimeout(()=>perform(()=>execute({mode:'candidates',source:parts.get('attribute-search')?.dataset.source || 'search',attributes:[...selected.values()]})),120);
        };
        label.append(input, document.createTextNode(`${attribute.relationshipType} → ${attribute.target}`)); list.append(label);
      }
      parts.set('attribute-choices',list);
      const group=element('div',undefined,'attribute-search');group.append(list);parts.set('attribute-search',group);controls.append(group);
    }
    {
      const subjects = mode === 'exceptions' ? config.exceptionSubjects || config.subjects : config.subjects;
      const choice = select(['',...subjects], 'Select Thought to show attributes', subject);
      // The prompt belongs inside the menu, not on a separate header line.
      // Keep the accessible name on the select after removing its visible label.
      choice.field.firstChild.remove();
      choice.input.options[0].textContent = 'Select Thought to show attributes';
      // Display the control's purpose with the name; option values stay UKS labels.
      for (const option of choice.input.options) {
        if (option.value) option.textContent = `Attributes of: ${option.value}`;
      }
      choice.input.value = subject;
      choice.input.onchange = () => {
        subject = choice.input.value;
        perform(async () => {
          onSubject(subject);
          if (!subject) {lastQuery = null; lastQueries.delete('attributes'); results.replaceChildren(element('p','Choose a Thought to see its attributes.')); await graph.loadThoughts([]); return false;}
          return execute({mode:mode === 'candidates' ? 'attributes' : mode,subject,source:choice.input.dataset.source || 'subject'});
        });
      };
      parts.set('thought-selector',choice.field);controls.append(choice.field);
    }
    const add = element('button','Manage Attributes…'); add.type = 'button';
    add.onclick = () => openAttributeDialog();parts.set('attribute-editor',add); controls.append(add);
  }
  // MANAGE ATTRIBUTES DIALOG
  // Visual order (assembled near the end of this function):
  //   Manage Attributes                              [?]  [close]
  //   Add/delete prompt                          [Use dropdowns]
  //   Source Thought
  //   Relationship type          Target                       [Add]
  //   Existing direct attribute                            [Delete]
  //   Operation status
  // Styles for these rows are grouped under #attribute-box in experiments.css.
  function openAttributeDialog() {
    // FIELD CREATION: each field owns a menu and a textbox; only one is visible.
    const menuToggle = element('label', undefined, 'attribute-menu-toggle');
    const useMenus = element('input'); useMenus.type = 'checkbox'; useMenus.checked = useMenuFields;
    menuToggle.append(useMenus, document.createTextNode('Use dropdowns'));
    function editableField(values, label, value) {
      const choice = select(values, label, value);
      const text = element('input'); text.type = 'text'; text.hidden = useMenus.checked; choice.input.hidden = !useMenus.checked;
      text.value = choice.input.value; text.setAttribute('aria-label', label);
      text.placeholder = `Enter ${label.toLowerCase()}`;
      const field = element('div', undefined, 'attribute-field');
      field.append(element('span', label), choice.input, text);
      choice.field = field; choice.text = text;
      choice.value = () => (useMenus.checked ? choice.input.value : text.value).trim();
      choice.input.onchange = () => { text.value = choice.input.value; choice.changed?.(); };
      text.onchange = () => choice.changed?.();
      return choice;
    }
    const sourceField = editableField(config.subjects, 'Source Thought', subject || config.subjects[0]);
    const type = editableField(config.types, 'Relationship type');
    const target = editableField(config.targets, 'Target');
    // MODE SWITCH: preserve values and refresh the deletion list for the source.
    useMenus.onchange = () => {
      useMenuFields = useMenus.checked;
      for (const choice of [sourceField,type,target]) {
        if (useMenus.checked && [...choice.input.options].some(option => option.value === choice.text.value.trim())) choice.input.value = choice.text.value.trim();
        choice.input.hidden = !useMenus.checked; choice.text.hidden = useMenus.checked;
      }
      sourceField.changed?.();
      if (!useMenus.checked) sourceField.text.focus();
    };
    const title = element('div', undefined, 'help-heading');
    const close = element('button','×'); close.type = 'button'; close.setAttribute('aria-label','Close attribute window'); close.onclick = () => dialog.close();
    const help = element('button'); help.type = 'button';
    // Shared question-circle artwork, also used by graph Help.
    help.innerHTML = '<img src="help-icon.svg" width="20" height="20" alt="" aria-hidden="true" style="display:block">'; help.title = 'Attribute editor help';
    help.setAttribute('aria-label','Attribute editor help'); help.setAttribute('aria-haspopup','dialog');
    help.onclick = () => {
      let helpDialog = document.querySelector('#attribute-help-box');
      if (!helpDialog) {
        helpDialog = element('dialog'); helpDialog.id = 'attribute-help-box';
        helpDialog.setAttribute('aria-label','Attribute editor help'); document.body.append(helpDialog);
      }
      // ATTRIBUTE EDITOR HELP CONTENT: editable HTML, in visible reading order.
      // All examples are sent to the existing server parser when Add is pressed.
      helpDialog.innerHTML = `
        <div class="help-heading">
          <h2>Creating Thoughts and Relationships</h2>
          <button type="button" aria-label="Close attribute help">&times;</button>
        </div>
        <p>
          With <strong>Use dropdowns</strong> checked, select existing preset labels.
          Uncheck it to type into Source Thought, Relationship type, and Target.
          Press <strong>Add</strong> to parse the fields and create any new Thoughts needed.
        </p>
        <h3>Names and attributes</h3>
        <ul>
          <li>A single name such as <code>fred</code> creates or reuses that Thought.</li>
          <li>
            Noun attributes come first: <code>big dog</code> becomes <code>dog.big</code>.
            Lowercase plural nouns are singularized: <code>legs</code> becomes <code>leg</code>.
          </li>
          <li>
            Type attributes follow the verb: <code>has 2</code> becomes <code>has.2</code>.
            <code>have 2</code> also becomes <code>has.2</code>.
          </li>
          <li>
            Double quotes preserve an exact label, including spaces or plurals:
            <code>"personal experience"</code> or <code>"legs"</code>.
          </li>
          <li>
            Dot notation such as <code>dog.big</code> creates a Thought inheriting
            from both <code>dog</code> and <code>big</code>.
          </li>
          <li>For a negative attribute, use a type such as <code>needs.NOT</code> with target <code>food</code>.</li>
        </ul>
        <h3>Sequences</h3>
        <p>
          Start a field with <code>^</code>, followed by space-separated values.
          For example, Source <code>Fido</code>, Type <code>spelled</code>,
          Target <code>^f i d o</code> creates a spelling sequence.
        </p>
        <h3>Relationships as Thoughts</h3>
        <p>
          Use brackets and arrows to put a whole Relationship in Source or Target.
          Both <code>-&gt;</code> and <code>→</code> arrows are accepted.
        </p>
        <pre>Source: [Fido-&gt;plays-&gt;fetch]
Type:   using
Target: frisbee</pre>
        <h3>Conditions and combined Relationships</h3>
        <p>Use <code>IF</code> as the Type and bracketed Relationships in the other fields. Conditions can be combined with <code>AND</code>:</p>
        <pre>Source: [Fido-&gt;gets-&gt;wet]
Type:   IF
Target: [[Fido-&gt;is-&gt;outside] AND [weather-&gt;is-&gt;rainy]]</pre>
        <p>The parser applies the possible (<code>.?</code>) property to conditional operands automatically.</p>
        <p>
          <strong>Delete</strong> removes the selected direct Relationship for the source.
          To delete an inherited attribute, choose the category where it is stored.
          Add and Delete leave the editor open for further changes.
        </p>
      `;
      helpDialog.querySelector('button').onclick = () => helpDialog.close();
      helpDialog.showModal();
    };
    const headingActions = element('div', undefined, 'attribute-heading-actions');
    headingActions.append(help,close);
    title.append(element('h2','Manage Attributes'),headingActions);
    // The introduction stays fixed; action feedback has its own line below the fields.
    const prompt = element('p','Add an attribute or delete a direct relationship.','query-note');
    const introduction = element('div', undefined, 'attribute-intro');
    introduction.append(prompt,menuToggle);
    const message = element('p','', 'attribute-status'); message.setAttribute('role','status');
    let storedSource = null; // Canonical source returned by the server for typed phrases.
    const existing = select([], 'Existing direct attribute');
    let directVersion = 0;
    // DELETE ACTION: remove only the selected stored relationship, then refresh in place.
    const remove = button('Delete', async () => {
      if (!existing.input.value) return false;
      // === WEB SERVICE: DELETE /api/relationships — delete the exact Relationship selected in the dialog. ===
      await request('/api/relationships?' + new URLSearchParams({id:existing.input.value}), {method:'DELETE'});
      await loadExisting();
      // Editing a category must not replace the Thought being inspected.
      render();onSubject(subject);
      await refreshResults();
      await graph.refresh();
      message.textContent = 'Deleted.';
    });
    remove.className = 'delete-attribute';
    // Populate the delete menu from UKS, not the demo's preset facts.
    // Versioning discards a late reply if the user has already changed the source.
    async function loadExisting() {
      const version = ++directVersion;
      remove.disabled = true; remove.dataset.unavailable = 'true';
      existing.input.replaceChildren(element('option','No direct attributes'));
      existing.input.options[0].value = '';
      const label = storedSource || sourceField.value();
      if (!label) return;
      let data;
      // === WEB SERVICE: GET /api/children — populate the Delete dropdown with the source’s stored direct Relationships. ===
      try { data = await request('/api/children?' + new URLSearchParams({label})); }
      catch (error) { if (error.status === 404 && error.code !== 'sandbox_unavailable') return; throw error; }
      if (version !== directVersion) return;
      existing.input.replaceChildren(...data.relationships.map(row => {
        const option = element('option', notation(row)); option.value = row.id; return option;
      }));
      if (!data.relationships.length) existing.input.append(element('option','No direct attributes'));
      if (!data.relationships.length) existing.input.options[0].value = '';
      remove.dataset.unavailable = String(!data.relationships.length); remove.disabled = !data.relationships.length;
    }
    sourceField.changed = () => { storedSource = null; loadExisting().catch(error => { message.textContent = error.message; }); };
    sourceField.text.oninput = () => {
      storedSource = null;
      // Never allow deletion of a relationship belonging to the previous source.
      ++directVersion; existing.input.replaceChildren(element('option','No direct attributes'));
      existing.input.options[0].value = ''; remove.disabled = true; remove.dataset.unavailable = 'true';
    };
    // ADD ACTION: use the common server parser; keep the dialog and entered values.
    const save = button('Add', async () => {
      const source = sourceField.value(), relationshipType = type.value(), destination = target.value();
      if (!source || !relationshipType || !destination) { message.textContent = 'Enter a source, relationship type, and target.'; return false; }
      // Menu choices are exact identities; typed fields use the table page's parser.
      // === WEB SERVICE: POST /api/statements — send Source/Type/Target to the shared parser; store the parsed Relationship. ===
      const added = await request('/api/statements', {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({
        source:useMenus.checked ? JSON.stringify(source) : source, relationshipType, target:useMenus.checked ? JSON.stringify(destination) : destination
      })});
      const sourceLabel = added.sourceLabel ?? (added.source.startsWith('[') ? '' : added.source);
      for (const [values,value] of [[config.subjects,sourceLabel],[config.types,added.relationshipType],[config.targets,added.target]]) {
        if (value && !values.includes(value)) values.push(value);
      }
      if (sourceLabel && config.exceptionSubjects && !config.exceptionSubjects.includes(sourceLabel)) config.exceptionSubjects.push(sourceLabel);
      remember({relationshipType:added.relationshipType,target:added.target});
      storedSource = sourceLabel || null;
      // Newly parsed identities become available in menu mode without rebuilding the dialog.
      for (const [field,values] of [[sourceField,config.subjects],[type,config.types],[target,config.targets]]) {
        for (const value of values) if (![...field.input.options].some(option => option.value === value)) {
          const option = element('option',value); option.value = value; field.input.append(option);
        }
      }
      await loadExisting();
      // Preserve Attributes of and the graph root, even when another source is edited.
      render();onSubject(subject);
      await refreshResults();
      await graph.refresh();
      message.textContent = `Added [${added.source} → ${added.relationshipType} → ${added.target}].`;
      feedback(message.textContent);
    });
    const use = button('Use in search', async () => {
      const attribute = {relationshipType:type.value(),target:target.value()};
      if (!attribute.relationshipType || !attribute.target) { message.textContent = 'Enter a relationship type and target.'; return false; }
      remember(attribute); selected.set(key(attribute),attribute); dialog.close(); render();onSubject(subject);
      await execute({mode:'candidates',attributes:[...selected.values()]});
    });
    use.hidden = mode !== 'candidates';
    // LAYOUT ASSEMBLY: changing this order changes the visible row order.
    const deleteRow = element('div',undefined,'attribute-dialog-row'); deleteRow.append(existing.field,remove);
    const addRow = element('div',undefined,'attribute-dialog-row attribute-add-row'); addRow.append(type.field,target.field,save);
    dialog.replaceChildren(
      title,          // Heading and close button.
      introduction,   // Prompt and menu/text toggle on the same row.
      sourceField.field,
      addRow,         // Type, target, Add.
      element('hr'),
      deleteRow,      // Existing relationship, Delete.
      use,            // Candidate-search-only shortcut.
      message         // Add/delete feedback; does not move the introduction.
    );
    // POSITIONING: anchor the popup near the graph’s left edge and keep it on screen.
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
        const stream = name === 'candidate-results' || name === 'results' && mode === 'candidates' ? 'candidates' : 'attributes';
        resultSources.set(stream,spec.source || (stream === 'candidates' ? 'search' : 'subject'));
        return resultPanes.get(stream);
      }
      const node=parts.get(name);
      if(!node)throw new Error(`Component ${name} is unavailable in this query step.`);
      if(name==='thought-selector') {
        const input = node.querySelector('select'); input.dataset.source=spec.id || 'subject';
        if (spec.label) { input.options[0].textContent=spec.label; input.setAttribute('aria-label',spec.label); }
        // Pages can choose a starting subject without issuing a query on page open.
        if (!subject && spec.value && config.subjects.includes(spec.value)) subject=spec.value;
        input.value=subject;
      }
      if(name==='attribute-search')node.dataset.source=spec.id || 'search';
      if(spec.label && node.tagName==='BUTTON')node.textContent=spec.label;
      return node;
    },
    showStep(nextMode) {
      clearTimeout(searchTimer);
      resultSources.clear();
      const next = nextMode || 'attributes';
      if (mode !== next) {
        subject = ''; lastQuery = null; lastQueries.clear();
        for (const [stream,pane] of resultPanes) pane.replaceChildren(element('p',stream === 'candidates' ? 'Select attributes to find likely candidates.' : 'Choose a Thought to see its attributes.'));
        if (!controlHost) results.replaceChildren(element('p', next === 'candidates' ? 'Select attributes to find likely candidates.' : 'Choose a Thought to see its attributes.'));
      }
      mode = next; render();
    },
    async activate() {
      // Each step waits for the visitor to make a selection.
      return false; // Initial output does not count as a user experiment action.
    },
    getState() { return {mode,subject,useMenuFields,selected:[...selected.values()],attributes,lastQuery,lastQueries:[...lastQueries.values()], vocabulary:{subjects:config.subjects,types:config.types,targets:config.targets,exceptionSubjects:config.exceptionSubjects}}; },
    async restoreState(state) {
      if(!state || state.mode!==mode)return;
      for (const name of ['subjects','types','targets','exceptionSubjects']) {
        for (const value of Array.isArray(state.vocabulary?.[name]) ? state.vocabulary[name] : []) {
          if (typeof value === 'string' && value.trim() && config[name] && !config[name].includes(value)) config[name].push(value);
        }
      }
      if (typeof state.useMenuFields === 'boolean') useMenuFields = state.useMenuFields;
      subject=typeof state.subject==='string' && config.subjects.includes(state.subject)?state.subject:'';
      const valid=row=>row && typeof row.relationshipType==='string' && typeof row.target==='string';
      for(const row of Array.isArray(state.attributes)?state.attributes:[])if(valid(row))remember(row);
      selected.clear();for(const row of Array.isArray(state.selected)?state.selected:[])if(valid(row))selected.set(key(row),row);
      render();onSubject(subject);
      for (const query of Array.isArray(state.lastQueries) ? state.lastQueries : [state.lastQuery]) {
        if (!query || !['attributes','exceptions','candidates'].includes(query.mode)) continue;
        try { await execute(query); }
        catch(error) {if(error.status!==404 || error.code==='sandbox_unavailable')throw error;lastQuery=null;}
      }
    },
    get subject() { return subject; },
    async refresh(options = {}) { await refreshResults(options); },
    setBusy(busy) { controls.querySelectorAll('button,select,input').forEach(node => node.disabled = busy);
      dialog.querySelectorAll('button,select,input').forEach(node => node.disabled = node.dataset.unavailable === 'true' || busy && node.getAttribute('aria-label') !== 'Close attribute window'); }
  };
}

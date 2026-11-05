// Sequence interactions shared by spelling, ordered events and melody demos.
// Content (words, patterns, tunes) belongs to the experiment HTML.
export function createSequencePanel({request,graph,perform,config,feedback,controls:controlHost,results:resultHost}) {
  const controls=controlHost || document.querySelector('#query-controls'), results=resultHost || document.querySelector('#query-results');
  const parts=new Map();
  const buffers=new Map(), catalogs=new Map();
  let mode='spelling', lastAction=null, currentWord='', currentTune='', showValues=true, audio;
  const kind=()=>mode==='digits'?'digits':mode==='music'?'music':'word';
  const buffer=()=>buffers.get(mode)||[];
  function el(tag,text,cls) {const node=document.createElement(tag);if(text!==undefined)node.textContent=text;if(cls)node.className=cls;return node;}
  function button(text,action,cls) {const node=el('button',text,cls);node.type='button';node.onclick=()=>perform(action);return node;}
  function select(items,label,onChange) {
    const field=el('label',label),input=el('select');input.setAttribute('aria-label',label);
    input.append(el('option','Select…'));input.options[0].value='';
    for(const item of items){const option=el('option',item.label||item);option.value=item.label||item;input.append(option);}
    input.onchange=()=>onChange(input.value);field.append(input);return {field,input};
  }
  function display(value) {return value.startsWith('note:')?value.slice(5):value.startsWith('c:')?value.slice(2):value;}
  async function catalog(automatic=false) {
    const data=await request('/api/sequences?'+new URLSearchParams({kind:kind()}),{automatic});
    catalogs.set(kind(),data.sequences);return data.sequences;
  }
  async function showSequence(sequence) {
    await graph.loadThoughts([sequence.startId],{sequenceValues:showValues});
    results.replaceChildren(el('h3',sequence.label));
    const values=el('p',sequence.cells.map(cell=>display(cell.value)).join(' → '),'sequence-text');results.append(values);
    if(showValues){
      const list=el('ul');
      sequence.cells.forEach((cell,index)=>{
        list.append(el('li',`[${index+1}] → VLU → ${cell.value}`));
        if(cell.nextId!==null)list.append(el('li',`[${index+1}] → NXT → [${index+2}]`));
      });results.append(list);
    }
    lastAction={type:'sequence',sequence};
  }
  async function store(label,values) {
    if(!values.length){feedback('Choose some sequence values first.');return false;}
    const data=await request('/api/sequences',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({kind:kind(),label,values})});
    await catalog();await showSequence(data);return data;
  }
  function showLibrary() {
    results.replaceChildren(el('h3','Stored sequences'));
    const list=catalogs.get(kind())||[];
    if(!list.length)results.append(el('p',mode==='spelling'?'Choose a word and store its spelling.':'Store a sequence to try matching it.'));
    for(const sequence of list)results.append(button(sequence.label,()=>showSequence(sequence),'sequence-library-item'));
    lastAction={type:'library'};
  }
  async function match({automatic=false}={}) {
    const values=[...buffer()];if(!values.length){feedback('Select a sequence to match.');return false;}
    const data=await request('/api/sequence-matches?'+new URLSearchParams({kind:kind(),values:JSON.stringify(values)}),{automatic});
    if(automatic&&(document.querySelector('dialog[open]')||document.activeElement?.matches('select,input,textarea')))return false;
    results.replaceChildren(el('h3','Sequence matches'));
    if(!data.matches.length)results.append(el('p','No matching sequence. Try a shorter fragment, or store another sequence.'));
    for(const item of data.matches){
      const section=el('section');
      section.append(button(item.sequence.label,()=>showSequence(item.sequence),'sequence-result-title'));
      section.append(el('p',item.full?'Full sequence match':`Partial sequence match · positions ${item.offset+1}–${item.offset+item.matched} of ${item.total}`,'query-note'));
      const line=el('p',undefined,'sequence-text');
      item.sequence.cells.forEach((cell,index)=>{const span=el('span',display(cell.value)+(index+1===item.total?'':' '));if(index>=item.offset&&index<item.offset+item.matched)span.className='matched-values';line.append(span);});
      section.append(line);
      if(mode==='music'){
        section.append(el('p',item.continuation.length?'Remainder: '+item.continuation.map(display).join(' '):'No notes remain after this match.','query-note'));
        const play=el('button','Play completed tune');play.type='button';play.onclick=()=>playNotes(item.sequence.cells.map(cell=>cell.value));section.append(play);
        if(item.continuation.length){const finish=el('button','Play remainder');finish.type='button';finish.onclick=()=>playNotes(item.continuation);section.append(finish);}
      }
      results.append(section);
    }
    if(!automatic)await graph.loadThoughts([...new Set(data.matches.slice(0,12).map(item=>item.sequence.startId))],{sequenceValues:showValues});
    lastAction={type:'match'};return true;
  }
  function paintBuffer() {
    const output=parts.get('sequence-input')?.querySelector('.sequence-buffer') || controls.querySelector('.sequence-buffer');if(output)output.textContent=buffer().length?buffer().join(' → '):'No values selected';
  }
  function append(value){const values=buffer();if(values.length>=32){feedback('Keep this sequence to 32 values.');return false;}buffers.set(mode,[...values,value]);paintBuffer();}
  function editor(values,{grid=false}={}) {
    const keyboard=el('div',undefined,grid?'digit-pad':'sequence-keyboard');
    for(const value of values)keyboard.append(button(value,()=>append(value)));
    const output=el('div',undefined,'sequence-buffer');output.setAttribute('aria-live','polite');
    const tools=el('div',undefined,'sequence-actions');
    tools.append(button('Undo',()=>{buffers.set(mode,buffer().slice(0,-1));paintBuffer();}),button('Clear',()=>{buffers.set(mode,[]);paintBuffer();}));
    const input=el('div',undefined,'sequence-input');input.append(output,tools);
    parts.set(grid?'digit-pad':mode==='music'?'note-keyboard':'letter-keyboard',keyboard);parts.set('sequence-input',input);
    controls.append(keyboard,input);paintBuffer();
  }
  async function playNotes(values) {
    const notes={'C4':261.63,'D4':293.66,'E4':329.63,'F4':349.23,'G4':392,'A4':440,'B4':493.88,'C5':523.25};
    audio ||= new AudioContext();await audio.resume();
    const start=audio.currentTime+.05;
    values.forEach((value,index)=>{
      const frequency=notes[display(value)];if(!frequency)return;
      const oscillator=audio.createOscillator(),gain=audio.createGain(),time=start+index*.32;
      oscillator.frequency.value=frequency;oscillator.type='sine';gain.gain.setValueAtTime(0,time);gain.gain.linearRampToValueAtTime(.13,time+.02);gain.gain.exponentialRampToValueAtTime(.001,time+.28);
      oscillator.connect(gain);gain.connect(audio.destination);oscillator.start(time);oscillator.stop(time+.3);
      oscillator.onended=()=>{oscillator.disconnect();gain.disconnect();};
    });
  }
  function render() {
    controls.replaceChildren();parts.clear();
    if(mode==='spelling'){
      const word=el('input');word.type='text';word.maxLength=20;word.placeholder='Choose or enter a word';word.setAttribute('aria-label','Word');word.value=currentWord;
      word.oninput=()=>{currentWord=word.value;};
      const presets=select(config.words,'Try a word',value=>{currentWord=value;word.value=value;});
      const row=el('div',undefined,'sequence-actions');row.append(word,button('Add',async()=>{
        const text=currentWord.trim().toLowerCase();if(!/^[a-z]{1,20}$/.test(text)){feedback('Choose a word of 1–20 letters.');return false;}await store('w:'+text,[...text]);
      }));const group=el('div',undefined,'word-input');group.append(presets.field,row);parts.set('word-input',group);controls.append(group);
    } else if(mode==='letters'){
      editor([...'abcdefghijklmnopqrstuvwxyz']);
      const search=button('Find matches',()=>match());parts.set('sequence-match',search);controls.append(search);
      const examples=el('div',undefined,'sequence-actions');
      for(const fragment of config.fragments)examples.append(button(fragment,()=>{buffers.set(mode,[...fragment]);paintBuffer();return match();}));parts.set('sequence-examples',examples);controls.append(examples);
    } else if(mode==='digits'){
      editor(['1','2','3','4','5','6','7','8','9'],{grid:true});
      const row=el('div',undefined,'sequence-actions');const remember=button('Remember order',()=>store('order:'+buffer().join('-'),buffer())),search=button('Match order',()=>match());parts.set('remember-sequence',remember);parts.set('sequence-match',search);row.append(remember,search);controls.append(row);
      const examples=select(config.orders,'Remember an example',value=>perform(async()=>{const entry=config.orders.find(item=>item.label===value);if(entry)await store(entry.label,entry.values);}));parts.set('order-examples',examples.field);controls.append(examples.field);
    } else {
      const tunes=select(config.tunes,'Tune',value=>{currentTune=value;});
      if(currentTune)tunes.input.value=currentTune;
      const row=el('div',undefined,'sequence-actions');row.append(button('Learn tune',async()=>{
        const tune=config.tunes.find(item=>item.label===currentTune);if(!tune){feedback('Choose a tune first.');return false;}await store(tune.label,tune.values);
      }),button('Use opening',()=>{
        const tune=config.tunes.find(item=>item.label===currentTune);if(!tune){feedback('Choose a tune first.');return false;}buffers.set(mode,tune.values.slice(0,4));paintBuffer();
      }));const selector=el('div',undefined,'tune-selector');selector.append(tunes.field,row);parts.set('tune-selector',selector);controls.append(selector);
      editor(config.notes);
      const tools=el('div',undefined,'sequence-actions');const play=el('button','Play entered notes');play.type='button';play.onclick=()=>playNotes(buffer());
      const search=button('Find tune / complete',()=>match());parts.set('sequence-match',search);parts.set('playback',play);tools.append(search,play);controls.append(tools);
    }
    const toggle=el('label',undefined,'sequence-toggle'),checkbox=el('input');checkbox.type='checkbox';checkbox.checked=showValues;
    checkbox.onchange=()=>perform(async()=>{showValues=checkbox.checked;if(lastAction?.type==='sequence')await showSequence(lastAction.sequence);else if(lastAction?.type==='match')await match();});
    toggle.append(checkbox,document.createTextNode('Show VLU links'));parts.set('sequence-values',toggle);controls.append(toggle);
    const library=button('Stored sequences',async()=>{await catalog();showLibrary();});parts.set('sequence-library',library);controls.append(library);
  }
  return {
    createComponent(spec){
      if(['sequence-results','results'].includes(spec.component))return results;
      const node=parts.get(spec.component);
      if(!node)throw new Error(`Component ${spec.component} is unavailable in this sequence step.`);
      if(spec.label && node.tagName==='BUTTON')node.textContent=spec.label;
      return node;
    },
    showStep(next){if(next!==mode){mode=next;showValues=mode==='spelling';lastAction=null;results.replaceChildren(el('p','Choose or create a sequence.'));}render();},
    getState(){return {mode,buffers:[...buffers],currentWord,currentTune,showValues,
      lastAction:lastAction?.type==='sequence'?{type:'sequence',id:lastAction.sequence.id}:lastAction};},
    async restoreState(state){
      if(!state || state.mode!==mode)return;
      for(const pair of Array.isArray(state.buffers)?state.buffers:[]) {
        if(Array.isArray(pair)&&['spelling','letters','digits','music'].includes(pair[0])&&Array.isArray(pair[1])&&pair[1].every(value=>typeof value==='string'))buffers.set(pair[0],pair[1].slice(0,32));
      }
      currentWord=typeof state.currentWord==='string'?state.currentWord:'';
      currentTune=config.tunes.some(tune=>tune.label===state.currentTune)?state.currentTune:'';
      showValues=typeof state.showValues==='boolean'?state.showValues:showValues;
      await catalog();render();
      if(state.lastAction?.type==='sequence') {
        const sequence=(catalogs.get(kind())||[]).find(item=>item.id===state.lastAction.id);
        if(sequence)await showSequence(sequence);
      } else if(state.lastAction?.type==='match'&&buffer().length)await match();
      else if(state.lastAction?.type==='library')showLibrary();
    },
    async activate(){await catalog();return false;},
    async refresh({automatic=false}={}){await catalog(automatic);if(lastAction?.type==='match')await match({automatic});},
    setBusy(busy){controls.querySelectorAll('button,select,input').forEach(node=>{if(node.disabled!==busy)node.disabled=busy;});}
  };
}

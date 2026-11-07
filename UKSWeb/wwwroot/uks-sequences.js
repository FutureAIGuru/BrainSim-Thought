// Sequence interactions shared by spelling, ordered events and melody demos.
// Content (words, patterns, tunes) belongs to the experiment HTML.
export function createSequencePanel({request,graph,perform,config,feedback,controls:controlHost,results:resultHost}) {
  const controls=controlHost || document.querySelector('#query-controls'), results=resultHost || document.querySelector('#query-results');
  const parts=new Map();
  const buffers=new Map(), catalogs=new Map(), expandedSpellings=new Set(), captures=new Map(), oscillators=new Set();
  const frequencies={'C4':261.63,'C#4':277.18,'D4':293.66,'D#4':311.13,'E4':329.63,'F4':349.23,'F#4':369.99,'G4':392,'G#4':415.30,'A4':440,'A#4':466.16,'B4':493.88,'C5':523.25};
  const timed=()=>mode==='colors'||mode==='music';
  const capture=()=>{if(!captures.has(mode))captures.set(mode,{delays:[],lastAt:0,closed:false});return captures.get(mode);};
  let mode='spelling', lastAction=null, currentWord='', currentTune='', pendingTuneName='', matchFirst=false, matchLast=false, audio, captureTimer, playbackTimer, playbackVersion=0;
  const kind=()=>mode==='colors'?'colors':mode==='music'?'music':'word';
  const buffer=()=>buffers.get(mode)||[];
  function el(tag,text,cls) {const node=document.createElement(tag);if(text!==undefined)node.textContent=text;if(cls)node.className=cls;return node;}
  function button(text,action,cls) {const node=el('button',text,cls);node.type='button';node.onclick=()=>perform(action);return node;}
  function select(items,label,onChange) {
    const field=el('label',label),input=el('select');input.setAttribute('aria-label',label);
    input.append(el('option','Select…'));input.options[0].value='';
    for(const item of items){const option=el('option',item.label||item);option.value=item.label||item;input.append(option);}
    input.onchange=()=>onChange(input.value);field.append(input);return {field,input};
  }
  function display(value) {return value.startsWith('c:')?value.slice(2):value;}
  async function catalog(automatic=false) {
    // === WEB SERVICE: GET /api/sequences — list stored demo sequences for the current word/color/music kind. ===
    const data=await request('/api/sequences?'+new URLSearchParams({kind:kind()}),{automatic});
    catalogs.set(kind(),data.sequences);return data.sequences;
  }
  async function showSequence(sequence,{keepExpanded=false,playback=timed()}={}) {
    clearTimeout(captureTimer);stopPlayback();
    if(mode==='spelling'){
      if(!keepExpanded)expandedSpellings.clear();
      expandedSpellings.add(sequence.label);
    }
    await graph.loadThoughts(mode==='spelling'?['word',...expandedSpellings]:[sequence.startId],{expand:mode==='spelling',sequenceValues:true});
    results.replaceChildren(el('h3',sequence.cells[0].label));
    const values=el('p',sequence.cells.map(cell=>display(cell.value)).join(' → '),'sequence-text');results.append(values);
    {
      const list=el('ul');
      sequence.cells.forEach((cell,index)=>{
        list.append(el('li',`${cell.label} → VLU → ${cell.value}`));
        if(cell.delay&&timed())list.append(el('li',`${cell.label} → VLU → delay:${cell.delay}`));
        if(cell.nextId!==null)list.append(el('li',`${cell.label} → NXT → ${sequence.cells.find(next=>next.id===cell.nextId)?.label}`));
      });results.append(list);
    }
    lastAction={type:'sequence',sequence};
    if(playback&&mode==='colors')playColors(sequence);else if(playback&&mode==='music')await playNotes(sequence.cells.map(cell=>cell.value),sequence.cells);
  }
  async function store(label,values,suppliedDelays) {
    if(!values.length){feedback('Choose some sequence values first.');return false;}
    clearTimeout(captureTimer);
    const delays=suppliedDelays||(timed()?values.map((_,index)=>index===values.length-1&&!capture().closed?Math.max(1,Math.min(12,Math.round((Date.now()-capture().lastAt)/250)||1)):capture().delays[index]||1):undefined);
    if(delays&&!suppliedDelays){capture().delays=[...delays];capture().closed=true;}
    // === WEB SERVICE: POST /api/sequences — store the entered sequence using the UKS sequence operations. ===
    const data=await request('/api/sequences',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({kind:kind(),label,values,...(delays?{delays}:{})})});
    await catalog();await showSequence(data,{keepExpanded:mode==='spelling',playback:false});return data;
  }
  function showLibrary() {
    clearTimeout(captureTimer);stopPlayback();paintBuffer();
    results.replaceChildren(el('h3','Stored sequences'));
    const list=catalogs.get(kind())||[];
    if(!list.length)results.append(el('p',mode==='spelling'?'Choose a word and store its spelling.':'Store a sequence to try matching it.'));
    for(const sequence of list)results.append(button(sequence.cells[0].label,()=>showSequence(sequence),'sequence-library-item'));
    lastAction={type:'library'};
  }
  async function match({automatic=false,values=buffer()}={}) {
    if(!automatic){
      clearTimeout(captureTimer);stopPlayback();
      if(timed()&&buffer().length&&!capture().closed){capture().delays[buffer().length-1]=Math.max(1,Math.min(12,Math.round((Date.now()-capture().lastAt)/250)||1));capture().closed=true;}
    }
    values=[...values];if(mode==='letters'&&values.length&&!/^[a-z]{1,32}$/.test(values.join(''))){feedback('Enter 1–32 letters.');return false;}if(!values.length){feedback('Select a sequence to match.');return false;}
    // === WEB SERVICE: GET /api/sequence-matches — match the entered values; return full/partial matches and continuations. ===
    const data=await request('/api/sequence-matches?'+new URLSearchParams({kind:kind(),values:JSON.stringify(values),matchFirst:String(matchFirst),matchLast:String(matchLast)}),{automatic});
    if(automatic&&(document.querySelector('dialog[open]')||document.activeElement?.matches('select,input,textarea')))return false;
    results.replaceChildren(el('h3','Sequence matches'));
    if(!data.matches.length)results.append(el('p','No matching sequence. Try a shorter fragment, or store another sequence.'));
    const list=el('ul',undefined,'sequence-match-list');
    for(const item of data.matches){
      const row=el('li',undefined,'sequence-match-row');
      const name=button(item.sequence.label,()=>showSequence(item.sequence),'result-link');
      name.title=item.full?'Full sequence match':`Partial sequence match · positions ${item.offset+1}–${item.offset+item.matched} of ${item.total}`;
      const line=el('span',undefined,'sequence-match-values');
      item.sequence.cells.forEach((cell,index)=>{
        if(index)line.append(document.createTextNode(' '));
        const span=el('span',display(cell.value));
        if(index>=item.offset&&index<item.offset+item.matched)span.className='matched-values';
        line.append(span);
      });
      row.append(name,line);list.append(row);
      if(mode==='music'){
        const playback=el('li',undefined,'sequence-match-playback');
        playback.append(button('Play completed tune',()=>playNotes(item.sequence.cells.map(cell=>cell.value),item.sequence.cells)));
        if(item.continuation.length)playback.append(button('Play remainder',()=>playNotes(item.continuation,item.sequence.cells.slice(item.offset+item.matched))));
        list.append(playback);
      }
    }
    results.append(list);
    if(!automatic)await graph.loadThoughts([...new Set(data.matches.slice(0,12).map(item=>item.sequence.startId))],{sequenceValues:true});
    lastAction={type:'match',values};
    return true;
  }
  // The palette names are the existing UKS color Thought labels, not characters.
  const colors={red:'#db3838',orange:'#ed8b23',yellow:'#f3d840',green:'#339d55',blue:'#397ddd',purple:'#9750c6',brown:'#94603d',pink:'#ee9ebd',white:'#f5f5f5'};
  function paintBuffer() {
    const text=parts.get('sequence-input')?.querySelector('.sequence-pattern');
    if(text){text.value=buffer().join('');return;}
    const output=parts.get('sequence-input')?.querySelector('.sequence-buffer') || controls.querySelector('.sequence-buffer');
    if(!output)return;
    output.replaceChildren();
    if(!buffer().length){output.textContent=timed()?'No values selected — a 3-sec pause ends the sequence.':'No values selected';return;}
    buffer().forEach((value,index)=>{
      if(index)output.append(document.createTextNode(' → '));
      const item=el('span',value);
      if(mode==='colors'){item.className='color-token';item.style.setProperty('--sequence-color',colors[value]);}
      output.append(item);
    });
  }
  function scheduleCaptureEnd() {
    clearTimeout(captureTimer);
    if(!timed()||!buffer().length||capture().closed)return;
    captureTimer=setTimeout(function finish(){
      if(!timed()||!buffer().length||capture().closed)return;
      const remember=parts.get('remember-sequence');
      if(remember?.disabled){captureTimer=setTimeout(finish,100);return;}
      perform(async()=>{
        capture().delays[buffer().length-1]=12;capture().closed=true;
        feedback('Sequence ended. Choose Remember to learn it, or Match to search.');return false;
      });
    },Math.max(0,3000-(Date.now()-capture().lastAt)));
  }
  function stopPlayback() {
    clearTimeout(playbackTimer);playbackVersion++;graph.setPlayback(null);
    for(const part of ['color-pad','note-keyboard'])for(const key of parts.get(part)?.querySelectorAll('.playing')||[])key.classList.remove('playing');
    for(const oscillator of oscillators){try{oscillator.stop();}catch{}}oscillators.clear();
  }
  function playColors(sequence) {
    const version=playbackVersion, replay=el('p',undefined,'sequence-playback-progress'), status=el('p',undefined,'sequence-playback-status');status.setAttribute('aria-live','polite');results.append(replay,status);
    let index=0;
    const advance=()=>{
      if(version!==playbackVersion||mode!=='colors')return;
      for(const square of parts.get('color-pad')?.querySelectorAll('button')||[])square.classList.toggle('playing',square.getAttribute('aria-label')===display(sequence.cells[index]?.value||''));
      if(index>=sequence.cells.length){graph.setPlayback(null);status.textContent='Playback complete.';return;}
      const cell=sequence.cells[index],units=cell.delay||1;graph.setPlayback(cell.id);
      status.textContent=`Playing ${cell.value} · delay ${units} (${units/4} sec)`;
      // Playback has its own progress area; the user's selected pattern is untouched.
      const output=replay;
      {
        output.replaceChildren();
        for(const [position,played] of sequence.cells.slice(0,index+1).entries()){
          if(position)output.append(document.createTextNode(' → '));
          const token=el('span',played.value,'color-token');token.style.setProperty('--sequence-color',colors[played.value]);output.append(token);
        }
      }
      index++;playbackTimer=setTimeout(advance,units*250);
    };
    advance();
  }
  async function showInput() {
    // === WEB SERVICE: POST /api/sequence-input — replace the live UKS color sequence (not a remembered example). ===
    const data=await request('/api/sequence-input',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({values:buffer()})});
    await graph.loadThoughts(data.startId===null?[]:[data.startId],{sequenceValues:true});
    results.replaceChildren(el('h3','Sequence being entered'),el('p',buffer().length?buffer().join(' → '):'Select colors to build a sequence.','sequence-text'));
    lastAction={type:'input'};
  }
  async function updateBuffer(values) {
    clearTimeout(captureTimer);stopPlayback();
    const previous=buffer();buffers.set(mode,values);if(timed()&&!values.length){capture().delays=[];capture().lastAt=0;capture().closed=false;if(mode==='music'){pendingTuneName='';currentTune='';const menu=parts.get('tune-selector')?.querySelector('select');if(menu)menu.value='';}}paintBuffer();
    try {if(mode==='colors')await showInput();}
    catch(error){buffers.set(mode,previous);paintBuffer();throw error;}
  }
  async function append(value){
    const values=buffer();if(values.length>=32){feedback('Keep this sequence to 32 values.');return false;}
    if(mode==='music')pendingTuneName='';
    if(timed()){
      const now=Date.now();
      if(values.length&&!capture().closed)capture().delays[values.length-1]=Math.max(1,Math.min(12,Math.round((now-capture().lastAt)/250)));
      capture().delays.push(1);capture().lastAt=now;capture().closed=false;
    }
    await updateBuffer([...values,value]);
    if(timed())scheduleCaptureEnd();
  }
  function editor(values,{grid=false}={}) {
    if(mode==='letters'){
      const text=el('input',undefined,'sequence-pattern');text.type='text';text.maxLength=32;
      text.placeholder='Type a word or letter pattern';text.setAttribute('aria-label','Letter pattern');text.autocomplete='off';text.spellcheck=false;
      text.oninput=()=>{buffers.set(mode,[...text.value.toLowerCase()]);};
      text.onkeydown=event=>{if(event.key==='Enter'&&!event.isComposing){event.preventDefault();parts.get('sequence-match')?.click();}};
      const input=el('div',undefined,'sequence-input');input.append(text);
      const tools=el('div',undefined,'sequence-actions finder-actions');tools.append(button('Clear',()=>updateBuffer([])));
      parts.set('sequence-input',input);parts.set('sequence-actions',tools);controls.append(input);paintBuffer();return;
    }
    if(mode==='music'){
      const ns='http://www.w3.org/2000/svg',piano=document.createElementNS(ns,'svg');
      piano.setAttribute('viewBox','0 0 320 120');piano.setAttribute('role','group');piano.setAttribute('aria-label','Piano keyboard');piano.classList.add('piano-keyboard');
      const keys=[['C4',0],['D4',40],['E4',80],['F4',120],['G4',160],['A4',200],['B4',240],['C5',280],['C#4',28],['D#4',68],['F#4',148],['G#4',188],['A#4',228]];
      for(const [note,x] of keys){
        const black=note.includes('#'),key=document.createElementNS(ns,'g'),rect=document.createElementNS(ns,'rect'),label=document.createElementNS(ns,'text');
        key.setAttribute('role','button');key.setAttribute('aria-label',note);key.setAttribute('tabindex','0');key.classList.add('piano-key',black?'black-key':'white-key');
        for(const [name,value] of Object.entries({x,y:0,width:black?24:40,height:black?74:120,rx:3}))rect.setAttribute(name,String(value));
        label.setAttribute('x',String(x+(black?12:20)));label.setAttribute('y',black?'63':'106');label.textContent=note;
        key.append(rect,label);piano.append(key);
        const press=()=>perform(async()=>{await append(note);await playNotes([note],[],true);});
        key.addEventListener('click',()=>{if(key.getAttribute('aria-disabled')!=='true')press();});
        key.addEventListener('keydown',event=>{if(['Enter',' '].includes(event.key)){event.preventDefault();if(key.getAttribute('aria-disabled')!=='true')press();}});
      }
      const output=el('div',undefined,'sequence-buffer');output.setAttribute('aria-live','polite');
      const input=el('div',undefined,'sequence-input');input.append(output);
      const tools=el('div',undefined,'sequence-actions finder-actions');tools.append(button('Clear',()=>updateBuffer([])));
      parts.set('note-keyboard',piano);parts.set('sequence-input',input);parts.set('sequence-actions',tools);controls.append(piano,input);paintBuffer();return;
    }
    const keyboard=el('div',undefined,grid?'color-pad':'sequence-keyboard');
    for(const value of values){
      const key=button(value,()=>append(value));
      if(grid){key.className='color-square';key.style.setProperty('--sequence-color',colors[value]);key.setAttribute('aria-label',value);key.title=value;}
      keyboard.append(key);
    }
    const output=el('div',undefined,'sequence-buffer');output.setAttribute('aria-live','polite');
    const tools=el('div',undefined,'sequence-actions');
    tools.append(button('Clear',()=>updateBuffer([])));
    const input=el('div',undefined,'sequence-input');input.append(output);
    tools.classList.add('finder-actions');parts.set('sequence-actions',tools);
    parts.set(grid?'color-pad':mode==='music'?'note-keyboard':'letter-keyboard',keyboard);parts.set('sequence-input',input);
    controls.append(keyboard,input);paintBuffer();
  }
  async function playNotes(values,cells=[],preview=false) {
    if(!preview){
      clearTimeout(captureTimer);
      if(mode==='music'&&buffer().length&&!capture().closed){capture().delays[buffer().length-1]=Math.max(1,Math.min(12,Math.round((Date.now()-capture().lastAt)/250)||1));capture().closed=true;}
    }
    stopPlayback();const version=playbackVersion;
    // Keyboard input sounds immediately; stored playback shows the actual cells.
    audio ||= new AudioContext();await audio.resume();
    if(cells.length)await graph.loadThoughts([cells[0].id],{sequenceValues:true});
    if(version!==playbackVersion)return;
    const delays=values.map((_,index)=>preview ? .18 : (cells[index]?.delay||captures.get('music')?.delays[index]||1)/4);
    const start=audio.currentTime+.03;
    let elapsed=0;
    values.forEach((value,index)=>{
      const frequency=frequencies[display(value)];if(!frequency)return;
      const oscillator=audio.createOscillator(),gain=audio.createGain(),time=start+elapsed,duration=delays[index];elapsed+=duration;
      oscillator.frequency.value=frequency;oscillator.type='sine';gain.gain.setValueAtTime(0,time);gain.gain.linearRampToValueAtTime(.13,time+.01);gain.gain.exponentialRampToValueAtTime(.001,time+Math.max(.02,duration-.02));
      oscillator.connect(gain);gain.connect(audio.destination);oscillators.add(oscillator);oscillator.start(time);oscillator.stop(time+duration);
      oscillator.onended=()=>{oscillators.delete(oscillator);oscillator.disconnect();gain.disconnect();};
    });
    let index=0,total=0;
    const advance=()=>{
      if(version!==playbackVersion)return;
      for(const key of parts.get('note-keyboard')?.querySelectorAll('.piano-key')||[])key.classList.toggle('playing',key.getAttribute('aria-label')===display(values[index]||''));
      graph.setPlayback(cells[index]?.id??null);
      if(index>=values.length)return;
      total+=delays[index++];playbackTimer=setTimeout(advance,Math.max(0,(start+total-audio.currentTime)*1000));
    };
    playbackTimer=setTimeout(advance,30);
  }
  function render() {
    controls.replaceChildren();parts.clear();
    if(mode==='spelling'){
      // Selection commits a spelling; the word hierarchy remains visible in the graph.
      const presets=select(config.words,'Choose a word',value=>perform(async()=>{
        if(!value)return false;
        currentWord=value;
        await store('w:'+value.toLowerCase(),[...value.toLowerCase()]);
      }));
      if(currentWord)presets.input.value=currentWord;
      const group=el('div',undefined,'word-input');group.append(presets.field);parts.set('word-input',group);controls.append(group);
    } else if(mode==='letters'){
      editor([]);
    } else if(mode==='colors'){
      editor(Object.keys(colors),{grid:true});
    } else {
      const tunes=select(config.tunes,'Tune',value=>perform(async()=>{
        currentTune=value;const tune=config.tunes.find(item=>item.label===value);if(!tune)return false;
        clearTimeout(captureTimer);stopPlayback();
        buffers.set(mode,[...tune.values]);
        captures.set(mode,{delays:[...(tune.delays||tune.values.map(()=>1))],lastAt:0,closed:true});
        // This derives a short new Thought name from an author-written menu caption.
        pendingTuneName=tune.label.trim().split(/[\s,]+/)[0];
        paintBuffer();results.replaceChildren(el('p','Choose Remember to learn this tune, or Match to search.'));lastAction={type:'input'};
      }));
      if(currentTune)tunes.input.value=currentTune;
      const selector=el('div',undefined,'tune-selector');selector.append(tunes.field);parts.set('tune-selector',selector);controls.append(selector);
      editor(config.notes);
      const play=el('button','Play entered notes');play.type='button';play.onclick=()=>playNotes(buffer());parts.set('playback',play);
    }
    if(mode!=='spelling'){
      // The same finder is composed into the letter, color, and music steps.
      const row=parts.get('sequence-actions');
      const remember=button('Remember',async()=>{
        const values=[...buffer()];
        if(mode==='letters'&&!/^[a-z]{1,32}$/.test(values.join(''))){feedback('Enter 1–32 letters.');return false;}
        const name=kind()==='word'?'w:'+values.join(''):kind()==='music'?(pendingTuneName||'t:new'):'o:new';
        await store(name.length<=80?name:'Sequence '+Date.now(),values);
      }),search=button('Match',()=>match());
      parts.set('remember-sequence',remember);parts.set('sequence-match',search);row.append(remember,search);if(mode==='music')row.append(parts.get('playback'));
      const options=el('div',undefined,'sequence-match-options');
      for(const [name,first] of [['Match first',true],['Match last',false]]){
        const label=el('label'),input=el('input');input.type='checkbox';input.checked=first?matchFirst:matchLast;
        input.title=first?'Require the pattern to start at the beginning of the sequence.':'Require the pattern to end at the end of the sequence.';
        input.onchange=()=>perform(async()=>{
          if(first)matchFirst=input.checked;else matchLast=input.checked;
          const values=buffer().length?buffer():lastAction?.type==='match'?lastAction.values:[];
          if(values.length)await match({values});
        });
        label.append(input,document.createTextNode(name));options.append(label);
      }
      const finder=el('div',undefined,'sequence-finder');finder.classList.toggle('music-finder',mode==='music');finder.append(row,options);
      parts.set('sequence-finder',finder);controls.append(finder);
    }
    const library=button('Stored sequences',async()=>{await catalog();showLibrary();});parts.set('sequence-library',library);if(mode==='music')parts.get('sequence-finder').querySelector('.sequence-match-options').append(library);else controls.append(library);
  }
  return {
    createComponent(spec){
      if(['sequence-results','results'].includes(spec.component))return results;
      const node=parts.get(spec.component);
      if(!node)throw new Error(`Component ${spec.component} is unavailable in this sequence step.`);
      if(spec.label && node.tagName==='BUTTON')node.textContent=spec.label;
      return node;
    },
    showStep(next){if(next!==mode){clearTimeout(captureTimer);stopPlayback();mode=next;lastAction=null;results.replaceChildren(el('p','Choose a letter sequence to create.'));}render();},
    getState(){return {mode,buffers:[...buffers],captures:[...captures],currentWord,currentTune,pendingTuneName,matchFirst,matchLast,expandedSpellings:[...expandedSpellings],
      lastAction:lastAction?.type==='sequence'?{type:'sequence',id:lastAction.sequence.id}:lastAction};},
    async restoreState(state){
      if(!state || state.mode!==mode)return;
      for(const pair of Array.isArray(state.buffers)?state.buffers:[]) {
        if(Array.isArray(pair)&&['spelling','letters','colors','music'].includes(pair[0])&&Array.isArray(pair[1])&&pair[1].every(value=>typeof value==='string'))buffers.set(pair[0],pair[1].slice(0,32));
      }
      captures.clear();
      for(const [name,saved] of Array.isArray(state.captures)?state.captures:[]){
        if(!['colors','music'].includes(name)||!saved)continue;
        captures.set(name,{delays:Array.isArray(saved.delays)?saved.delays.map(delay=>Number.isInteger(delay)&&delay>=1&&delay<=12?delay:1):[],lastAt:Number.isFinite(saved.lastAt)?saved.lastAt:Date.now(),closed:saved.closed===true});
      }
      currentWord=typeof state.currentWord==='string'?state.currentWord:'';
      currentTune=config.tunes.some(tune=>tune.label===state.currentTune)?state.currentTune:'';
      pendingTuneName=typeof state.pendingTuneName==='string'?state.pendingTuneName:'';
      matchFirst=state.matchFirst===true;matchLast=state.matchLast===true;
      await catalog();
      expandedSpellings.clear();
      for(const label of Array.isArray(state.expandedSpellings)?state.expandedSpellings:[])
        if((catalogs.get('word')||[]).some(sequence=>sequence.label===label))expandedSpellings.add(label);
      render();
      if(state.lastAction?.type==='sequence') {
        const sequence=(catalogs.get(kind())||[]).find(item=>item.id===state.lastAction.id);
        if(sequence)await showSequence(sequence,{keepExpanded:true,playback:false});
      } else if(state.lastAction?.type==='match'&&(state.lastAction.values?.length||buffer().length))await match({values:state.lastAction.values||buffer()});
      else if(state.lastAction?.type==='library')showLibrary();
      else if(mode==='colors'&&buffer().length)await showInput();
    },
    async activate(){await catalog();if(mode==='spelling'&&!lastAction)await graph.loadThoughts(['word'],{expand:true,sequenceValues:true});if(mode==='colors'&&(!lastAction||lastAction.type==='input'))await showInput();if(timed()&&(!lastAction||lastAction.type==='input'))scheduleCaptureEnd();return false;},
    async refresh({automatic=false}={}){await catalog(automatic);if(lastAction?.type==='match')await match({automatic,values:lastAction.values});},
    setBusy(busy){for(const key of parts.get('note-keyboard')?.querySelectorAll('.piano-key')||[])key.setAttribute('aria-disabled',String(busy));controls.querySelectorAll('button,select,input').forEach(node=>{if(node.disabled!==busy)node.disabled=busy;});}
  };
}

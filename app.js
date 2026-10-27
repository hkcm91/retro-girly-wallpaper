/* Petal OS — dependency-free, offline-first wallpaper. */
(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  const STORAGE_KEY = 'petal-os-v1';
  const defaults = {theme:'rose',accent:'#b64e71',background:'gingham',customImage:'',label1:'daily little things',label2:'dreams in progress',label3:'just for fun',motion:!matchMedia('(prefers-reduced-motion: reduce)').matches,sound:false,hour24:false,showNotes:true,volume:20,notes:{}};
  const palettes = {
    rose:['#f6d6e0','#fff7f6','#f2b6ca','#633c50','#b96d89','#d899af','#b64e71'],
    lavender:['#e4dcf3','#faf7ff','#cdc0e6','#514364','#9a84b3','#b4a1ce','#80619f'],
    mint:['#dcebe1','#f7fff9','#b8d7c5','#36584a','#7f9f8c','#a4c5b1','#487c63']
  };
  const backgrounds = ['gingham','dots','clouds','plain','custom'];
  let storageOK = true;
  const own = (object, key) => Object.prototype.hasOwnProperty.call(object, key);
  function validImage(value) {return typeof value === 'string' && value.length < 2900000 && /^data:image\/(png|jpeg|webp);base64,[a-z\d+/=]+$/i.test(value);}
  function clean(input) {
    const s = {...defaults,notes:{}};
    if (!input || typeof input !== 'object' || Array.isArray(input)) return s;
    if (own(palettes,input.theme)) s.theme = input.theme;
    if (typeof input.accent === 'string' && /^#[a-f\d]{6}$/i.test(input.accent)) s.accent = input.accent;
    if (backgrounds.includes(input.background)) s.background = input.background;
    if (validImage(input.customImage)) s.customImage = input.customImage;
    for (const key of ['label1','label2','label3']) if (typeof input[key] === 'string') s[key] = input[key].slice(0,36);
    for (const key of ['motion','sound','hour24','showNotes']) if (typeof input[key] === 'boolean') s[key] = input[key];
    if (Number.isFinite(input.volume)) s.volume = Math.max(0,Math.min(100,input.volume));
    if (input.notes && typeof input.notes === 'object') for (const [key,value] of Object.entries(input.notes).slice(0,5000)) {
      if (/^\d{4}-\d{2}-\d{2}$/.test(key) && typeof value === 'string' && value.trim()) s.notes[key] = value.slice(0,160);
    }
    if (s.background === 'custom' && !s.customImage) s.background = 'gingham';
    return s;
  }
  let state;
  try {state = clean(JSON.parse(localStorage.getItem(STORAGE_KEY)));} catch {state = clean(null); storageOK = false;}
  let selected = new Date(), viewed = new Date(selected.getFullYear(),selected.getMonth(),1), lastDay = '', toastTimer, audio;
  let livelyImage = '';
  const keyFor = date => `${String(date.getFullYear()).padStart(4,'0')}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`;
  function toast(message) { $('toast').textContent=message; $('toast').classList.add('visible'); clearTimeout(toastTimer); toastTimer=setTimeout(()=>$('toast').classList.remove('visible'),3000); }
  function save() {
    try {localStorage.setItem(STORAGE_KEY,JSON.stringify(state)); storageOK=true;} catch {storageOK=false; toast('Storage unavailable or full. Export settings to keep a copy.');}
    $('storage-status').textContent = storageOK ? 'Saved on this device. Export a copy to transfer or back up.' : 'Storage unavailable or full. Export a copy before closing.';
  }
  function apply(sync = true) {
    const colors=palettes[state.theme];
    ['base','paper','soft','ink','line','shadow'].forEach((name,i)=>document.documentElement.style.setProperty('--'+name,colors[i]));
    document.documentElement.style.setProperty('--accent',state.accent);
    document.body.dataset.background=state.background;
    const image = livelyImage || state.customImage;
    $('custom-bg').style.backgroundImage=state.background==='custom' && image ? `url("${image.replace(/["\\\n\r]/g,'')}")` : 'none';
    document.body.classList.toggle('paused',!state.motion);
    for(let i=1;i<=3;i++) $('zone'+i+'-label').textContent=state['label'+i];
    $('note-form').hidden=!state.showNotes;
    $('motion-shortcut').textContent=state.motion?'✧ Motion on':'✧ Motion off';
    $('motion-shortcut').setAttribute('aria-pressed',String(state.motion));
    $('sound-shortcut').textContent=state.sound?'♪ Sound on':'♪ Sound off';
    $('sound-shortcut').setAttribute('aria-pressed',String(state.sound));
    if(sync) {
      for(const id of ['accent','background','label1','label2','label3','volume']) $(id).value=state[id];
      for(const [id,key] of [['motion','motion'],['sound','sound'],['hour24','hour24'],['show-notes','showNotes']]) $(id).checked=state[key];
      document.querySelectorAll('[data-theme]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.theme===state.theme)));
    }
    tick();
  }
  function tick() {
    const now=new Date(),hours=now.getHours();
    $('clock').textContent=`${String(state.hour24?hours:(hours%12||12)).padStart(2,'0')}:${String(now.getMinutes()).padStart(2,'0')}`;
    $('clock').dateTime=now.toISOString();
    $('period').textContent=state.hour24?'':hours<12?'AM':'PM';
    $('date').textContent=now.toLocaleDateString(undefined,{weekday:'long',month:'long',day:'numeric',year:'numeric'});
    $('greeting').textContent=hours<12?'GOOD MORNING, LOVELY':hours<18?'A LITTLE AFTERNOON MAGIC':'SLOW DOWN, SWEET DREAMER';
    if(lastDay!==keyFor(now)) {lastDay=keyFor(now); renderCalendar();}
  }
  function renderCalendar() {
    $('month-title').textContent=viewed.toLocaleDateString(undefined,{month:'long',year:'numeric'});
    const container=$('calendar'); container.replaceChildren();
    const start=new Date(viewed.getFullYear(),viewed.getMonth(),1).getDay();
    const total=new Date(viewed.getFullYear(),viewed.getMonth()+1,0).getDate();
    for(let i=0;i<start;i++) {const space=document.createElement('span');space.setAttribute('aria-hidden','true');container.append(space);}
    for(let day=1;day<=total;day++) {
      const date=new Date(viewed.getFullYear(),viewed.getMonth(),day),key=keyFor(date),button=document.createElement('button');
      button.type='button';button.textContent=day;button.dataset.date=key;
      button.setAttribute('aria-label',date.toLocaleDateString(undefined,{weekday:'long',month:'long',day:'numeric',year:'numeric'})+(state.notes[key]?', has note':''));
      button.setAttribute('aria-pressed',String(key===keyFor(selected)));
      if(key===keyFor(new Date())) {button.classList.add('today');button.setAttribute('aria-current','date');}
      if(state.notes[key]) button.classList.add('has-note');
      button.addEventListener('click',()=>{selected=date;renderCalendar();});
      container.append(button);
    }
    $('prev-month').disabled=viewed.getFullYear()<=1900 && viewed.getMonth()===0;
    $('next-month').disabled=viewed.getFullYear()>=2099 && viewed.getMonth()===11;
    $('note-label').textContent='A little note · '+selected.toLocaleDateString(undefined,{month:'short',day:'numeric'});
    $('day-note').value=state.notes[keyFor(selected)]||'';
  }
  function update(key,value) {state[key]=value;apply();save();}
  async function chime() {
    if(!state.sound || state.volume===0) return;
    try {
      const AudioContext=window.AudioContext||window.webkitAudioContext;
      audio ||= new AudioContext(); await audio.resume();
      const osc=audio.createOscillator(), gain=audio.createGain(), t=audio.currentTime;
      osc.type='sine';osc.frequency.setValueAtTime(880,t);osc.frequency.exponentialRampToValueAtTime(1320,t+.06);
      gain.gain.setValueAtTime(Math.max(.0001,state.volume/100*.08),t);gain.gain.exponentialRampToValueAtTime(.0001,t+.12);
      osc.connect(gain);gain.connect(audio.destination);osc.start(t);osc.stop(t+.13);osc.onended=()=>{osc.disconnect();gain.disconnect();};
    } catch {toast('Sound is unavailable in this player.');}
  }
  $('open-settings').onclick=()=>$('settings').showModal();
  $('close-settings').onclick=()=>$('settings').close();
  $('help-button').onclick=()=>$('help').showModal();
  $('close-help').onclick=()=>$('help').close();
  $('prev-month').onclick=()=>{viewed=new Date(viewed.getFullYear(),viewed.getMonth()-1,1);renderCalendar();};
  $('next-month').onclick=()=>{viewed=new Date(viewed.getFullYear(),viewed.getMonth()+1,1);renderCalendar();};
  $('today').onclick=()=>{selected=new Date();viewed=new Date(selected.getFullYear(),selected.getMonth(),1);renderCalendar();};
  $('note-form').onsubmit=event=>{event.preventDefault();const value=$('day-note').value.trim();if(value)state.notes[keyFor(selected)]=value;else delete state.notes[keyFor(selected)];save();renderCalendar();toast(value?'A little memory saved ♡':'Note cleared');};
  document.querySelectorAll('[data-theme]').forEach(button=>button.onclick=()=>{state.theme=button.dataset.theme;state.accent=palettes[state.theme][6];apply();save();});
  for(const id of ['accent','label1','label2','label3','volume']) $(id).addEventListener('input',()=>{state[id]=id==='volume'?Number($(id).value):$(id).value;apply(false);save();});
  $('background').onchange=()=>{livelyImage='';const value=$('background').value;if(value==='custom'&&!state.customImage){toast('Choose an image below first.');$('background').value=state.background;return;}update('background',value);};
  for(const [id,key] of [['motion','motion'],['sound','sound'],['hour24','hour24'],['show-notes','showNotes']]) $(id).onchange=()=>update(key,$(id).checked);
  $('motion-shortcut').onclick=()=>update('motion',!state.motion);
  $('sound-shortcut').onclick=()=>update('sound',!state.sound);
  document.addEventListener('click',event=>{if(event.target.closest('button'))chime();});
  $('image-upload').onchange=async event=>{
    const file=event.target.files[0];if(!file)return;
    if(!['image/png','image/jpeg','image/webp'].includes(file.type)||file.size>2*1024*1024){toast('Choose a PNG, JPG or WebP smaller than 2 MB.');event.target.value='';return;}
    const reader=new FileReader();reader.onerror=()=>toast('Could not read this image.');reader.onload=()=>{const image=new Image();image.onerror=()=>toast('This image could not be opened.');image.onload=()=>{livelyImage='';state.customImage=reader.result;update('background','custom');};image.src=reader.result;};reader.readAsDataURL(file);
  };
  $('export').onclick=()=>{
    const blob=new Blob([JSON.stringify({version:1,...state},null,2)],{type:'application/json'}),url=URL.createObjectURL(blob),link=document.createElement('a');
    link.href=url;link.download='petal-os-settings.json';link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);toast('Settings exported, including notes and your image.');
  };
  $('import').onchange=async event=>{
    const file=event.target.files[0];if(!file)return;
    try {if(file.size>4*1024*1024)throw Error();const input=JSON.parse(await file.text());if(input.version!==1||!own(palettes,input.theme))throw Error();state=clean(input);livelyImage='';apply();renderCalendar();save();toast('Your little world, restored ♡');}catch{toast('Use a Petal OS settings export smaller than 4 MB.');}event.target.value='';
  };
  $('reset').onclick=()=>{state={...defaults,notes:{...state.notes}};livelyImage='';apply();save();toast('Appearance reset. Your calendar notes are kept.');};
  document.addEventListener('visibilitychange',()=>{document.body.classList.toggle('hidden-page',document.hidden);if(document.hidden&&audio)audio.suspend();if(!document.hidden)tick();});
  /* Lively sends these controls on launch and whenever Customise changes.
     They override matching browser-local values; communication is one-way. */
  window.livelyPropertyListener=(name,value)=>{
    if(name==='theme' && Number.isInteger(Number(value)) && Number(value)>=0 && Number(value)<3){state.theme=['rose','lavender','mint'][Number(value)];}
    else if(name==='accent' && /^#[a-f\d]{6}$/i.test(value)) state.accent=value;
    else if(name==='background' && Number.isInteger(Number(value)) && Number(value)>=0 && Number(value)<5) state.background=backgrounds[Number(value)];
    else if(['label1','label2','label3'].includes(name)) state[name]=String(value??'').slice(0,36);
    else if(['motion','sound','hour24','showNotes'].includes(name) && typeof value==='boolean') state[name]=value;
    else if(name==='volume' && Number.isFinite(Number(value))) state.volume=Math.max(0,Math.min(100,Number(value)));
    else if(name==='customBackground') {
      // Lively folderDropdown returns a relative path, not an arbitrary URL.
      let path=typeof value==='string'?value.replace(/\\/g,'/').replace(/^\.\//,''):'';
      if(path && !path.includes('/')) path='backgrounds/'+path;
      if(/^backgrounds\/[^/\\:\x00-\x1f"?<>|]+\.(png|jpe?g|webp)$/i.test(path)) {livelyImage=path;}
      else livelyImage='';
    } else return;
    apply();
  };
  apply();renderCalendar();
  if(!storageOK)$('storage-status').textContent='Storage could not be read. Export settings before closing.';
  setInterval(tick,1000);
})();

// Keep demo identity in the address bar; the UI only exposes the XML selector.
export const sandbox = {
  id: null,
  available: false,
  idleTimeoutMs: 30 * 60 * 1000,
  demo: null,
  error: null,
  ready: null,
  pageUrl(path, root) {
    const url = new URL(path, location.origin);
    if (this.demo) url.searchParams.set('demo', this.demo);
    if (this.id) url.searchParams.set('session', this.id);
    if (root) url.searchParams.set('root', root);
    return url.href;
  },
  updateRoot(root) {
    if (this.id) {
      history.replaceState(null, '', this.pageUrl(location.pathname, root));
      try { sessionStorage.setItem(activeSessionKey,JSON.stringify({id:this.id,root})); } catch {}
    }
  }
};

const activeKey=(demo)=>`uks-active-demo:${location.pathname}:${demo || ''}`;
const activeSessionKey=activeKey(new URLSearchParams(location.search).get('demo'));
const status = document.querySelector('#sandbox-status');
const demoSelect = document.querySelector('#demo-select');
const inactivityNote = document.querySelector('#inactivity-note');

function currentRoot() {
  return (document.querySelector('#root') || document.querySelector('#children-form [name="label"]'))?.value.trim() || 'Thought';
}

function message(text, error = false) {
  status.textContent = text;
  status.classList.toggle('error', error);
}

function updateControls() {
  for (const control of document.querySelectorAll('#add-form input, #add-form button, #children-list .delete-button'))
    control.disabled = !sandbox.available;
}

async function jsonRequest(url, options = {}) {
  const response = await fetch(url, { cache: 'no-store', ...options });
  const data = await response.json();
  if (!response.ok) {
    const error = new Error(data.error || data.detail || data.title || 'The request failed.');
    error.status = response.status;
    error.code = data.code;
    throw error;
  }
  return data;
}

export async function request(path, options = {}) {
  if (!await sandbox.ready) throw sandbox.error;
  if (!sandbox.available) throw sandbox.error;
  const { automatic = false, ...fetchOptions } = options;
  const url = new URL(path, location.origin);
  url.searchParams.set('session', sandbox.id);
  const headers = new Headers(fetchOptions.headers);
  if (automatic) headers.set('X-Sandbox-Poll', '1');
  try {
    return await jsonRequest(url, { ...fetchOptions, headers });
  } catch (error) {
    if (error.code === 'sandbox_unavailable') {
      sandbox.available = false;
      sandbox.error = error;
      demoSelect.value = '__choose';
      updateControls();
      message(error.message, true);
    }
    throw error;
  }
}

async function initialize() {
  updateControls();
  message('Loading demo…');
  const parameters = new URLSearchParams(location.search);
  const demos = await jsonRequest('/api/demos');
  for (const demo of demos) {
    const option = document.createElement('option');
    option.value = demo.id;
    option.textContent = demo.label;
    demoSelect.append(option);
  }
  demoSelect.disabled = false;
  let data, remembered;
  if (!parameters.has('session')) {
    try { remembered=JSON.parse(sessionStorage.getItem(activeSessionKey)||'null'); } catch {}
    if(typeof remembered?.id==='string') {
      try { data=await jsonRequest('/api/sandbox?session='+encodeURIComponent(remembered.id)); }
      catch(error) {if(error.code!=='sandbox_unavailable')throw error;remembered=null;}
    }
  }
  if (parameters.has('session')) {
    sandbox.id = parameters.get('session');
    data = await jsonRequest('/api/sandbox?session=' + encodeURIComponent(sandbox.id));
  } else if (!data) {
    const query = new URLSearchParams();
    if (parameters.get('demo')) query.set('demo', parameters.get('demo'));
    data = await jsonRequest('/api/sandboxes?' + query, { method: 'POST' });
  }
  sandbox.id = data.id;
  sandbox.demo = data.demo;
  sandbox.idleTimeoutMs = data.idleTimeoutMinutes * 60 * 1000;
  sandbox.available = true;
  demoSelect.value = sandbox.demo || '';
  sandbox.updateRoot(parameters.get('root') || (typeof remembered?.root==='string'?remembered.root:null) || currentRoot());
  updateControls();
  inactivityNote.textContent = `Edits are deleted after ${data.idleTimeoutMinutes} minutes of inactivity or a server restart.`;
  message('');
  return true;
}

demoSelect.addEventListener('change', () => {
  if (demoSelect.value === '__choose') return;
  demoSelect.disabled = true;
  // An explicit XML selection always creates a fresh sandbox.
  try { sessionStorage.removeItem(activeKey(demoSelect.value)); } catch {}
  const url = new URL(location.pathname, location.origin);
  if (demoSelect.value) url.searchParams.set('demo', demoSelect.value);
  url.searchParams.set('root', 'Thought');
  location.assign(url);
});

sandbox.ready = initialize().catch(error => {
  sandbox.id = null;
  sandbox.available = false;
  sandbox.error = error;
  demoSelect.value = '__choose';
  updateControls();
  message(error.message, true);
  return false;
});

// Local interaction counts as activity; an unattended tab does not send heartbeats.
let lastActivityPing = performance.now();
function recordActivity(event) {
  if (!event.isTrusted || !sandbox.available || document.hidden) return;
  const now = performance.now();
  const interval = Math.max(100, Math.min(30000, sandbox.idleTimeoutMs / 3));
  if (now - lastActivityPing < interval) return;
  lastActivityPing = now;
  request('/api/sandbox').catch(() => {});
}
for (const type of ['pointerdown', 'pointermove', 'keydown', 'wheel'])
  document.addEventListener(type, event => {
    if (type !== 'pointermove' || event.buttons) recordActivity(event);
  }, { passive: true });

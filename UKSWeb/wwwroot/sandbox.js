// Keep demo identity in the address bar; the UI only exposes the XML selector.
export const sandbox = {
  id: null,
  available: false,
  changed: false,
  pendingWrites: 0,
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
    if (this.id) history.replaceState(null, '', this.pageUrl(location.pathname, root));
  }
};

const status = document.querySelector('#sandbox-status');
const demoSelect = document.querySelector('#demo-select');
const inactivityNote = document.querySelector('#inactivity-note');
let approvedDeparture = false;
let sameSessionNavigation = false;

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

function hasChanges() {
  return sandbox.available && (sandbox.changed || sandbox.pendingWrites > 0);
}

function warnBeforeLeaving(event) {
  if (!hasChanges() || approvedDeparture || sameSessionNavigation) return;
  event.preventDefault();
  event.returnValue = '';
}

function updateLeaveGuard() {
  if (hasChanges()) window.addEventListener('beforeunload', warnBeforeLeaving);
  else window.removeEventListener('beforeunload', warnBeforeLeaving);
}

function sameSession(url) {
  return sandbox.id && url.origin === location.origin && url.searchParams.get('session') === sandbox.id;
}

// Following our editor/graph links keeps every edit. Do not warn on that move.
document.addEventListener('click', event => {
  const link = event.target.closest?.('a[href]');
  if (!link || event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || link.target === '_blank') return;
  const url = new URL(link.href, location.href);
  if (sameSession(url) && (url.pathname !== location.pathname || url.search !== location.search)) sameSessionNavigation = true;
});
// Modern browsers also identify reloads/back-forward navigation to the same
// session. Other browsers retain their standard beforeunload warning.
window.navigation?.addEventListener('navigate', event => {
  if (!event.destination.sameDocument) sameSessionNavigation = Boolean(sameSession(new URL(event.destination.url)));
});
window.navigation?.addEventListener('navigateerror', () => { approvedDeparture = false; sameSessionNavigation = false; });

async function jsonRequest(url, options = {}) {
  const response = await fetch(url, { cache: 'no-store', ...options });
  // Shared tabs learn about changes made by another visitor through normal reads.
  if (response.headers.get('X-Demo-Changed') === '1' && sameSession(new URL(url, location.origin))) {
    sandbox.changed = true;
    updateLeaveGuard();
  }
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
  const method = (fetchOptions.method || 'GET').toUpperCase();
  const writing = method !== 'GET' && method !== 'HEAD';
  const url = new URL(path, location.origin);
  url.searchParams.set('session', sandbox.id);
  const headers = new Headers(fetchOptions.headers);
  if (automatic) headers.set('X-Sandbox-Poll', '1');
  if (writing) {
    sandbox.pendingWrites++;
    updateLeaveGuard();
  }
  try {
    const data = await jsonRequest(url, { ...fetchOptions, headers });
    if (writing) sandbox.changed = true;
    return data;
  } catch (error) {
    if (error.code === 'sandbox_unavailable') {
      sandbox.available = false;
      sandbox.error = error;
      demoSelect.value = '__choose';
      updateControls();
      updateLeaveGuard();
      message(error.message, true);
    }
    throw error;
  } finally {
    if (writing) sandbox.pendingWrites--;
    updateLeaveGuard();
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
  let data;
  if (parameters.has('session')) {
    sandbox.id = parameters.get('session');
    data = await jsonRequest('/api/sandbox?session=' + encodeURIComponent(sandbox.id));
  } else {
    const query = new URLSearchParams();
    if (parameters.get('demo')) query.set('demo', parameters.get('demo'));
    data = await jsonRequest('/api/sandboxes?' + query, { method: 'POST' });
  }
  sandbox.id = data.id;
  sandbox.demo = data.demo;
  sandbox.changed = data.hasChanges;
  sandbox.idleTimeoutMs = data.idleTimeoutMinutes * 60 * 1000;
  sandbox.available = true;
  demoSelect.value = sandbox.demo || '';
  sandbox.updateRoot(parameters.get('root') || currentRoot());
  updateControls();
  updateLeaveGuard();
  inactivityNote.textContent = `Edits are deleted after ${data.idleTimeoutMinutes} minutes of inactivity or a server restart.`;
  message('');
  return true;
}

demoSelect.addEventListener('change', () => {
  if (demoSelect.value === '__choose') return;
  if (hasChanges() && !window.confirm('You have changes in this demo. Open another demo?')) {
    demoSelect.value = sandbox.demo || '';
    return;
  }
  approvedDeparture = true; // The explicit confirmation replaces a second browser warning.
  demoSelect.disabled = true;
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
  updateLeaveGuard();
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

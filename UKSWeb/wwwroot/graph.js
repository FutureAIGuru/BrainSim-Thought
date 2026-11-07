// Standalone graph.html controls. All graph behavior lives in uks-graph.js.
import { sandbox, request } from './sandbox.js?v=20261105-resume';
import { createUKSGraph } from './uks-graph.js?v=20261107-compact-music';

const root = document.querySelector('#root');
const status = document.querySelector('#status');
const load = document.querySelector('#load');
const refresh = document.querySelector('#refresh');
const labels = document.querySelector('#show-labels');
let graph, busy = false, stats;
const parameters = new URLSearchParams(location.search);
if (parameters.get('root')) root.value = parameters.get('root');
function message(text, error = false) {
  status.textContent = text; status.classList.toggle('error', error);
}
function summary() {
  if (stats) message(`Showing ${stats.nodeCount} nodes and ${stats.edgeCount} edges · ${stats.expandedCount} expanded · labels ${labels.checked ? 'on' : 'off'}. Auto-refresh: 2s.`);
}
async function run(action) {
  if (busy || !graph) return false;
  busy = true; load.disabled = refresh.disabled = true;
  try { return await action(); }
  catch (error) { message(error.message, true); return false; }
  finally { busy = false; load.disabled = refresh.disabled = !sandbox.available; }
}
document.querySelector('#root-form').onsubmit = event => { event.preventDefault(); run(() => graph.loadRoot(root.value)); };
document.querySelector('#fit').onclick = () => graph?.fit();
refresh.onclick = () => run(() => graph.refresh({ fit: true }));
labels.onchange = () => { graph?.setLabels(labels.checked); summary(); };
try {
  if (!await sandbox.ready) throw sandbox.error;
  graph = await createUKSGraph(document.querySelector('#graph'), {
    request, root: root.value, labels: labels.checked, perform: run,
    colors: { background: '#0b1220', root: '#f6c66d', node: '#72b7ff', expanded: '#4ad4b2', relationship: '#c19bff', edge: '#9caec7', connector: '#c19bff' },
    onRoot: label => {
      root.value = label; sandbox.updateRoot(label);
      document.querySelector('#editor-link').href = sandbox.pageUrl('/', label);
    },
    onChange: value => { stats = value; summary(); },
    onInspect: text => { document.querySelector('#edge-detail').textContent = text; },
    onError: error => message(error.message, true)
  });
  await run(() => graph.loadRoot(root.value));
} catch (error) { message(error.message, true); load.disabled = refresh.disabled = true; }
setInterval(() => {
  if (graph && sandbox.available && !busy && !document.hidden) run(() => graph.refresh({ automatic: true }));
}, 2000);
document.addEventListener('visibilitychange', () => {
  if (graph && !document.hidden) run(() => graph.refresh({ automatic: true }));
});

import { api } from './api.js';
import { h, poll } from './ui.js';
import worker from './views/worker.js';
import backlog from './views/backlog.js';
import runs from './views/runs.js';
import gapScout from './views/gap-scout.js';

// Each view is { id, title, mount(root) } — mount renders into root and may
// return a cleanup function (stop polling, ...), called when navigating away.
// A new area of the panel is a new module here plus its endpoints under /api.
const views = [worker, backlog, runs, gapScout];

const nav = document.getElementById('nav');
const root = document.getElementById('view');
let unmount = null;

function render() {
  const id = location.hash.slice(1) || views[0].id;
  const view = views.find((v) => v.id === id) ?? views[0];

  for (const link of nav.children) link.classList.toggle('active', link.dataset.id === view.id);

  unmount?.();
  root.replaceChildren();
  unmount = view.mount(root) ?? null;
}

nav.append(...views.map((v) => h('a', { href: `#${v.id}`, 'data-id': v.id }, v.title)));
window.addEventListener('hashchange', render);
render();

api('/project').then((p) => {
  document.getElementById('project').textContent = `${p.root} · backlog ${p.backlogProvider}`;
});

const stateLabels = { Running: 'worker en marcha', Stopping: 'worker parándose', Stopped: 'worker parado' };
const pill = document.getElementById('worker-pill');
poll(async () => {
  const status = await api('/worker');
  const external = status.state === 'Stopped' && status.otherWorkers.length > 0;
  pill.textContent = external ? 'worker en otro proceso' : stateLabels[status.state];
  pill.dataset.state = external ? 'External' : status.state;
}, 3000);

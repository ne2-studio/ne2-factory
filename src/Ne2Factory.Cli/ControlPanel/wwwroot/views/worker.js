import { api } from '../api.js';
import { act, formatDate, h, poll } from '../ui.js';

// `ne2-factory run`, from the browser: start/stop the worker and follow its log.
export default {
  id: 'worker',
  title: 'Worker',
  mount(root) {
    const state = h('span', { class: 'pill' });
    const detail = h('span', { class: 'muted' });
    const start = h('button', { class: 'primary', onclick: () => act(start, error, () => api('/worker/start', { method: 'POST' }).then(show)) }, 'Arrancar');
    const stop = h('button', { onclick: () => act(stop, error, () => api('/worker/stop', { method: 'POST' }).then(show)) }, 'Parar');
    const error = h('p', { class: 'error' });
    const warning = h('p', { class: 'warning' });
    const log = h('pre', { class: 'log' });

    root.append(
      h('section', { class: 'card' },
        h('div', { class: 'row' }, h('h2', {}, 'Worker'), state, detail, h('span', { class: 'spacer' }), start, stop),
        h('p', { class: 'muted' },
          'Sondea el backlog y los PRs de la factory, y ejecuta los jobs encolados (tickets, verificaciones, gap-scout). ',
          'Cada ticket corre headless con --dangerously-skip-permissions: arráncalo solo si puedes dejarlo desatendido. ',
          'Parar deja terminar el job en curso.'),
        warning,
        error),
      h('section', { class: 'card' }, h('h2', {}, 'Log'), log));

    function show(status) {
      state.textContent = status.state;
      state.dataset.state = status.state;
      detail.textContent = status.startedAt ? `desde ${formatDate(status.startedAt)}` : '';
      start.hidden = status.state !== 'Stopped';
      stop.hidden = status.state !== 'Running';
      warning.textContent = status.otherWorkers.length > 0
        ? `Hay otro worker vivo para este repo (${status.otherWorkers.map((w) => w.name).join(', ')}), p. ej. \`ne2-factory run\` en una terminal. No se puede arrancar otro aquí mientras siga vivo.`
        : '';
    }

    let lastSeq = 0;
    async function appendLogs() {
      const entries = await api(`/worker/logs?after=${lastSeq}`);
      if (entries.length === 0) return;
      const atBottom = log.scrollTop + log.clientHeight >= log.scrollHeight - 8;
      log.append(...entries.map((e) =>
        h('div', { class: `level-${e.level}` }, `${new Date(e.timestamp).toLocaleTimeString('es-ES')} ${e.message}`)));
      lastSeq = entries.at(-1).seq;
      if (atBottom) log.scrollTop = log.scrollHeight;
    }

    const stopStatus = poll(async () => show(await api('/worker')), 2000);
    const stopLogs = poll(appendLogs, 1500);
    return () => { stopStatus(); stopLogs(); };
  },
};

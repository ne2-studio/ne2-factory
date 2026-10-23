import { api } from '../api.js';
import { formatDate, h, poll } from '../ui.js';

// `runs [--limit n]`, refreshed live.
export default {
  id: 'runs',
  title: 'Runs',
  mount(root) {
    const limit = h('select', { onchange: () => refresh() },
      [20, 50, 100, 500].map((n) => h('option', { value: n }, `últimas ${n}`)));
    const error = h('p', { class: 'error' });
    const body = h('tbody');

    root.append(
      h('div', { class: 'row' }, h('h2', {}, 'Runs'), h('span', { class: 'spacer' }), limit),
      error,
      h('section', { class: 'card' },
        h('table', {},
          h('thead', {}, h('tr', {}, ['#', 'Estado', 'Agente', 'Encolada', 'Inicio', 'Fin', 'Outcome', 'Error'].map((c) => h('th', {}, c)))),
          body)));

    async function refresh() {
      try {
        const runs = await api(`/runs?limit=${limit.value}`);
        error.textContent = '';
        body.replaceChildren(...(runs.length === 0
          ? [h('tr', {}, h('td', { colspan: 8, class: 'muted' }, 'Sin runs registradas todavía.'))]
          : runs.map((run) => h('tr', { title: `run ${run.id}` },
              h('td', {}, `#${run.issueNumber}`),
              h('td', {}, h('span', { class: 'pill', 'data-state': run.status }, run.status)),
              h('td', {}, run.agentName),
              h('td', {}, formatDate(run.queuedAt)),
              h('td', {}, formatDate(run.startedAt)),
              h('td', {}, formatDate(run.finishedAt)),
              h('td', {}, run.outcome ?? '—'),
              h('td', { class: 'wrap' }, run.error ?? '—')))));
      } catch (err) {
        error.textContent = err.message;
      }
    }

    return poll(refresh, 5000);
  },
};
